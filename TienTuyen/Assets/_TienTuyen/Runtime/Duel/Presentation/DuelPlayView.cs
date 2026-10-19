using TienTuyen.Combat;
using TienTuyen.Duel.Net;
using TienTuyen.Presentation;
using UnityEngine;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The duel as played, from a running <see cref="DuelSession"/>: the local player's eye (<see cref="DuelFirstPerson"/>)
    /// with their body as a shadow, and the opponent's body in full. The opponent is drawn one snapshot behind the newest,
    /// moving between the last two, so it glides at any frame rate instead of stepping at the snapshot rate; a jump too
    /// long to walk (a respawn) is taken at once. The joiner hears no shots, so the opponent's are timed here from its
    /// held trigger and the gun's cadence, as the simulation fires them.
    /// </summary>
    internal sealed class DuelPlayView
    {
        private const float TeleportDistance = 3f;

        private readonly Transform root;
        private readonly Camera view;
        private readonly DuelFirstPerson eye;
        private readonly DuelBody local, opponent;
        private readonly DuelAudio audio;
        private readonly CombatBloodEffects blood;
        private readonly DuelImpacts impacts;
        private DuelSimState opponentPose;

        /// <summary>
        /// Your attacks not yet answered by a drop in the opponent's health, oldest first. The server resolves them in
        /// order, so a drop belongs to the oldest that could have hit; no time window, so a slow link only delays it.
        /// </summary>
        private readonly System.Collections.Generic.List<Attack> pending = new System.Collections.Generic.List<Attack>();
        private const int MaxPending = 32;
        private int roundGeneration = -1;
        private float lastKnife;

        private struct Attack
        {
            public bool Knife;
            public int Weapon;
            public Vector3 Origin, Direction;
            public DuelSimState Target;
        }
        private readonly int localSlot;
        private DuelOpponentView older, newer;
        private bool hasNewer;
        private float sinceNewer, opponentCooldown;

        public DuelPlayView(Transform parent, Camera view, int localSlot)
        {
            this.view = view;
            this.localSlot = localSlot;
            root = new GameObject("DuelPlay").transform;
            root.SetParent(parent, false);
            local = new DuelBody(root, "LocalPlayer", localSlot);
            opponent = new DuelBody(root, "Opponent", 1 - localSlot);
            eye = new DuelFirstPerson(view, local.Actor, localSlot);
            audio = new DuelAudio(root, localSlot);
            var bleeding = new GameObject("DuelBlood");
            bleeding.transform.SetParent(root, false);
            blood = bleeding.AddComponent<CombatBloodEffects>();
            blood.GroundHeight = DuelArenaView.Height;
            blood.Initialize();
            impacts = new DuelImpacts(root);
            eye.WeaponFired += (slot, start, end) =>
            {
                local.Fired(slot, end);
                audio.OwnShot(slot);
                Strike(localSlot, slot, eye.AimOrigin, eye.AimDirection, start, eye.Aiming, opponentPose);
                Remember(new Attack { Weapon = slot, Origin = start, Direction = (end - start).normalized, Target = opponentPose });
            };
            audio.Hurt += OnHurt;
        }

        public IDuelAimSource Aim => eye;

        /// <summary>You lost health: where the opponent stood (its chest, as drawn), for the hit arc.</summary>
        public event System.Action<Vector3> Wounded;

        /// <param name="playing">The match is on screen (not its result): the cursor is locked and look is read.</param>
        public void Look(bool playing, float deltaTime) => eye.Look(playing, deltaTime);

        /// <summary>Draws both bodies and places the eye: after the session's step, before the plants pick their view.</summary>
        public void Draw(DuelSession session, float deltaTime)
        {
            eye.Place(session, deltaTime);
            var pose = eye.Pose;
            local.Tick(pose, eye.Aiming, false, deltaTime);
            UpdateOpponent(session.Opponent, deltaTime);
            var match = session.Match;
            if (match.RoundGeneration != roundGeneration)
            {
                roundGeneration = match.RoundGeneration;
                pending.Clear();
                blood.Clear();
                impacts.Clear();
            }
            if (pose.KnifeRemaining > lastKnife)
                Remember(new Attack { Knife = true, Origin = DuelPlayerSim.MuzzlePosition(pose), Target = opponentPose });
            lastKnife = pose.KnifeRemaining;
            audio.Tick(match, pose, opponentPose, newer.Reloading, deltaTime);
            blood.Tick(deltaTime);
            impacts.Tick(deltaTime);
        }

        private void Remember(in Attack attack)
        {
            if (pending.Count == MaxPending) pending.RemoveAt(0);
            pending.Add(attack);
        }

        /// <summary>
        /// Blood on the opponent where your hit landed. The server's hit point is not in the snapshot, so it is taken
        /// where the matched attack's ray passes the opponent's body as it stood when you fired (the capsule the resolver
        /// tests); a knife strikes the chest. Only your bullets and knife lower the opponent's health, so a drop with no
        /// attack pending paints nothing. Your own wounds are not drawn: your body is a shadow.
        /// </summary>
        private void OnHurt(bool mine, float damage, bool killed, bool knife)
        {
            if (mine)
            {
                Body(opponentPose, out Vector3 bottom, out Vector3 top);
                Wounded?.Invoke(Vector3.Lerp(bottom, top, .7f));
                return;
            }
            int match = -1, candidates = 0;
            for (int i = 0; i < pending.Count; i++)
                if (!pending[i].Knife && CouldHit(pending[i])) candidates++;
            for (int i = 0; i < pending.Count && match < 0; i++)
                if (CouldHit(pending[i])) match = i;
            // The server's pose is not the drawn one; if none lines up, the oldest still fired first.
            if (match < 0) match = pending.Count > 0 ? 0 : -1;
            if (match < 0) { if (!knife) audio.HitConfirm(false); return; }
            var attack = pending[match];
            // Your hit is confirmed flat, as CS:GO does, so it carries at any range; a knife sounds its own hit.
            // Two shots that could both have hit may have landed between two snapshots as one drop of twice the damage:
            // that reads as a head hit and is not, so it confirms a body hit. A shot that plainly missed does not count.
            bool head = false;
            if (!attack.Knife)
            {
                head = IsHead(attack.Weapon, damage, killed);
                if (head && candidates > 1) { head = false; audio.Note("head read ambiguous: " + candidates + " shots could have hit"); }
                audio.HitConfirm(head);
            }
            pending.RemoveRange(0, match + 1);
            Body(attack.Target, out Vector3 low, out Vector3 high);
            Vector3 point, direction;
            if (!attack.Knife)
            {
                point = ClosestOnAxis(attack.Origin, attack.Direction, low, high, out Vector3 onRay);
                // Inside the body the ray's own point is the better guess; a pellet that grazed keeps to the axis. A head
                // hit lands above the axis's top end, and only this branch puts its blood at the face, not on the end cap.
                if ((onRay - point).sqrMagnitude < DuelArenaLayout.PlayerRadius * DuelArenaLayout.PlayerRadius) point = onRay;
                direction = attack.Direction;
            }
            else
            {
                point = Vector3.Lerp(low, high, .7f);
                direction = point - attack.Origin;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            }
            // Move the point with the body from where it stood when you fired to where it is drawn now.
            Vector3 moved = opponentPose.Position - attack.Target.Position;
            // Never the critical spray, on purpose: at duel range its billboard reads as a flat red sprite beside the
            // head. A head hit is already told by its confirm sound and the doubled damage.
            blood.Hit(opponent.Actor, point + moved, direction, false, killed);
        }

        /// <summary>
        /// A head hit, read off the damage: it is worth <see cref="HeadMultiplier"/> body hits of that gun, or it killed
        /// from more health than a body hit takes. A shotgun's pellets sum to something that cannot be split, so it
        /// is always a body hit; two hits in one server step read as one.
        /// </summary>
        private static bool IsHead(int weapon, float damage, bool killed)
        {
            var gun = DuelPlayerSim.WeaponAt(weapon);
            if (gun.PelletsPerShot > 1) return false;
            float body = gun.DamagePerHit;
            return damage >= body * HeadMultiplier - .5f || (killed && damage > body + .5f);
        }

        /// <summary>The resolver's head damage over a body hit's (duellogic's head zone).</summary>
        private const float HeadMultiplier = 2f;

        /// <summary>
        /// The body's axis between the capsule's end-cap centres, as DuelCombatResolver builds it: the top centre at
        /// the sim's height, so the cap (cut at the crown) holds the head; a hit there must not clamp to the chest.
        /// </summary>
        private static void Body(in DuelSimState body, out Vector3 low, out Vector3 high)
        {
            Vector3 feet = body.Position - Vector3.up * DuelArenaLayout.GroundY;
            low = feet + Vector3.up * DuelArenaLayout.PlayerRadius;
            high = feet + Vector3.up * Mathf.Max(DuelArenaLayout.PlayerRadius, body.Height);
        }

        /// <summary>Roughly whether the attack met the body as drawn when it was made (twice the radius, for the lag).</summary>
        private static bool CouldHit(in Attack attack)
        {
            Body(attack.Target, out Vector3 low, out Vector3 high);
            float reach = DuelArenaLayout.PlayerRadius * 2f;
            if (attack.Knife)
            {
                Vector3 planar = attack.Target.Position - attack.Origin;
                planar.y = 0f;
                return planar.magnitude <= CombatRules.KnifeReach + reach;
            }
            Vector3 point = ClosestOnAxis(attack.Origin, attack.Direction, low, high, out Vector3 onRay);
            return (onRay - point).sqrMagnitude <= reach * reach;
        }

        /// <summary>
        /// Marks where a shot's rounds struck cover or the ground (<see cref="DuelImpacts.Trace"/>, the resolver's
        /// geometry). A round whose ray meets the target's body first is a hit: it draws blood when the health drops,
        /// not a mark. Pellets scatter in the resolver's cone but not on its exact lines, which are seeded by the attack's
        /// tick and the view never sees it; the shot is heard once, where its first round struck.
        /// </summary>
        private void Strike(int shooter, int weapon, Vector3 origin, Vector3 direction, Vector3 muzzle, bool aiming, in DuelSimState target)
        {
            var gun = DuelPlayerSim.WeaponAt(weapon);
            Body(target, out Vector3 low, out Vector3 high);
            float radius = DuelArenaLayout.PlayerRadius;
            bool heard = false;
            for (int pellet = 0; pellet < gun.PelletsPerShot; pellet++)
            {
                Vector3 ray = gun.PelletsPerShot == 1 ? direction : Scatter(direction, aiming);
                if (!DuelImpacts.Trace(origin, ray, muzzle, gun.Range, out Vector3 point, out Vector3 normal, out var kind)) continue;
                if (!target.Dead)
                {
                    Vector3 axis = ClosestOnAxis(origin, ray, low, high, out Vector3 onRay);
                    if ((onRay - axis).sqrMagnitude < radius * radius && Vector3.Dot(onRay - origin, ray) < Vector3.Dot(point - origin, ray)) continue;
                }
                impacts.Strike(point, normal, kind, shooter);
                if (heard) continue;
                audio.Impact(shooter, point);
                heard = true;
            }
        }

        private static Vector3 Scatter(Vector3 forward, bool aiming)
        {
            float cone = Mathf.Tan(DuelCombatResolver.PelletSpreadDegrees * (aiming ? .5f : 1f) * Mathf.Deg2Rad);
            float radius = Mathf.Sqrt(Random.value) * cone, angle = Random.value * Mathf.PI * 2f;
            Vector3 right = Vector3.Cross(Mathf.Abs(forward.y) > .99f ? Vector3.forward : Vector3.up, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right);
            return (forward + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius).normalized;
        }

        /// <summary>The point of segment low..high nearest the ray, and the ray's point nearest it.</summary>
        private static Vector3 ClosestOnAxis(Vector3 origin, Vector3 direction, Vector3 low, Vector3 high, out Vector3 onRay)
        {
            Vector3 axis = high - low, w = origin - low;
            float a = Vector3.Dot(direction, direction), b = Vector3.Dot(direction, axis), c = Vector3.Dot(axis, axis);
            float d = Vector3.Dot(direction, w), e = Vector3.Dot(axis, w), denominator = a * c - b * b;
            float s = denominator > 1e-6f ? Mathf.Clamp01((a * e - b * d) / denominator) : 0f;
            Vector3 point = low + axis * s;
            onRay = origin + direction * Mathf.Max(0f, Vector3.Dot(point - origin, direction));
            return point;
        }

        private void UpdateOpponent(in DuelOpponentView latest, float deltaTime)
        {
            if (!hasNewer || latest.ServerTick != newer.ServerTick)
            {
                older = hasNewer ? newer : latest;
                newer = latest;
                hasNewer = true;
                sinceNewer = 0f;
            }
            else sinceNewer += deltaTime;
            float interval = Mathf.Max(1, newer.ServerTick - older.ServerTick) * DuelSession.DefaultFixedDeltaTime;
            bool cut = (newer.Position - older.Position).sqrMagnitude > TeleportDistance * TeleportDistance;
            float t = cut ? 1f : Mathf.Clamp01(sinceNewer / interval);
            var pose = new DuelSimState
            {
                Slot = 1 - localSlot,
                Position = Vector3.Lerp(older.Position, newer.Position, t),
                Yaw = Mathf.LerpAngle(older.Yaw, newer.Yaw, t),
                Pitch = Mathf.Lerp(older.Pitch, newer.Pitch, t),
                Crouch = Mathf.Lerp(older.Crouch, newer.Crouch, t),
                Health = newer.Health,
                ActiveWeaponSlot = newer.ActiveWeaponSlot,
                PreviousButtons = newer.Buttons,
                Walk = (newer.Buttons & DuelButtons.Walk) != 0 ? 1f : 0f
            };
            opponentPose = pose;
            bool aiming = (newer.Buttons & DuelButtons.Aim) != 0;
            opponent.Tick(pose, aiming, newer.Reloading, deltaTime);
            InferOpponentShot(pose, aiming, deltaTime);
        }

        /// <summary>The simulation fires a held trigger every cooldown while the gun is up and loaded.</summary>
        private void InferOpponentShot(in DuelSimState pose, bool aiming, float deltaTime)
        {
            bool firing = (pose.PreviousButtons & DuelButtons.Fire) != 0 && !pose.Dead && !newer.Reloading &&
                          pose.ActiveWeaponSlot < DuelPlayerSim.KnifeSlot;
            if (!firing) { opponentCooldown = 0f; return; }
            opponentCooldown -= deltaTime;
            if (opponentCooldown > 0f) return;
            var weapon = DuelPlayerSim.WeaponAt(pose.ActiveWeaponSlot);
            opponentCooldown += weapon.ShotCooldownSeconds;
            if (opponentCooldown < 0f) opponentCooldown = weapon.ShotCooldownSeconds;
            Vector3 look = Quaternion.Euler(pose.Pitch, pose.Yaw, 0f) * Vector3.forward;
            Vector3 muzzle = DuelPlayerSim.MuzzlePosition(pose), end = muzzle + look * weapon.Range;
            opponent.Fired(pose.ActiveWeaponSlot, end);
            audio.OpponentShot(pose.ActiveWeaponSlot, muzzle, end, DuelPlayerSim.EyePosition(eye.Pose));
            Strike(1 - localSlot, pose.ActiveWeaponSlot, DuelPlayerSim.EyePosition(pose), look, muzzle, aiming, eye.Pose);
        }

        public void Dispose()
        {
            var firstPerson = view != null ? view.GetComponent<CombatFirstPersonView>() : null;
            if (firstPerson != null) Object.Destroy(firstPerson);
            if (root != null) Object.Destroy(root.gameObject);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
