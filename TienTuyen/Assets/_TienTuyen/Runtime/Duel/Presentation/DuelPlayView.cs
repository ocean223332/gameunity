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
            eye.WeaponFired += (slot, start, end) => local.Fired(slot, end);
        }

        public IDuelAimSource Aim => eye;

        /// <param name="playing">The match is on screen (not its result): the cursor is locked and look is read.</param>
        public void Look(bool playing, float deltaTime) => eye.Look(playing, deltaTime);

        /// <summary>Draws both bodies and places the eye: after the session's step, before the plants pick their view.</summary>
        public void Draw(DuelSession session, float deltaTime)
        {
            eye.Place(session, deltaTime);
            var pose = eye.Pose;
            local.Tick(pose, eye.Aiming, false, deltaTime);
            UpdateOpponent(session.Opponent, deltaTime);
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
            bool aiming = (newer.Buttons & DuelButtons.Aim) != 0;
            opponent.Tick(pose, aiming, newer.Reloading, deltaTime);
            InferOpponentShot(pose, deltaTime);
        }

        /// <summary>The simulation fires a held trigger every cooldown while the gun is up and loaded.</summary>
        private void InferOpponentShot(in DuelSimState pose, float deltaTime)
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
            opponent.Fired(pose.ActiveWeaponSlot, DuelPlayerSim.MuzzlePosition(pose) + look * weapon.Range);
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
