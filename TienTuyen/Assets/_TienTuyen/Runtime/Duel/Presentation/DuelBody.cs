using TienTuyen.Combat;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// One fighter's body: a skinned ValveBiped rig with the AK, MP5 and XM1014 and the knife, posed from a duel
    /// state. Each slot has its own character (<see cref="ProfileFor"/>), so the enemy in front of you never looks
    /// like you. Its root stands at the simulated pivot (feet + GroundY); the rig turns itself
    /// to the aim. If the rig cannot be built, an olive stand-in of the body's height takes its place, so a fighter is
    /// never invisible.
    /// </summary>
    internal sealed class DuelBody
    {
        public const string HeroProfile = "Characters/PAVN/PAVN_ValveProfile";
        // Slot 1 is the US soldier. Its first-person arms are
        // not this profile's: they are the viewmodel FBX in Resources/Viewmodels/<id>/ (gloves and sleeves included),
        // the guns' folder picked by the source's ActiveWeapon.Id and the knife's by CombatFirstPersonView.KnifeId.
        // So US gun arms need a WeaponDefinition with a distinct Id: Id is get-only, so build one with its constructor.
        public const string OpponentProfile = "Characters/US/US_ValveProfile";

        /// <summary>The Resources path of the character the fighter in <paramref name="slot"/> wears.</summary>
        public static string ProfileFor(int slot) => slot == 1 ? OpponentProfile : HeroProfile;

        // The rig's guns in the duel's slot order (DuelPlayerSim.WeaponAt): the station's visual names, which pick the hold.
        private static readonly (string group, string model, string instance)[] Guns =
        {
            ("RifleVisual", "rifle", "RifleBlenderModel"), ("SmgVisual", "smg", "SmgBlenderModel"), ("ShotgunVisual", "shotgun", "ShotgunBlenderModel")
        };

        /// <summary>At the body's pivot; for the local player the first-person view hides its "HeroVisual" child.</summary>
        public readonly Transform Actor;
        private CombatCharacterRig rig;
        private readonly Transform standIn;
        private Vector3 lastPosition;
        private bool hasLast;
        private float lastHealth = float.MaxValue, hitTimer, aim, reloadElapsed;
        private int lastGun;

        public DuelBody(Transform parent, string name, int slot)
        {
            Actor = new GameObject(name).transform;
            Actor.SetParent(parent, false);
            var root = new GameObject("HeroVisual").transform;
            root.SetParent(Actor, false);
            var weapons = new Transform[Guns.Length];
            for (int i = 0; i < Guns.Length; i++) weapons[i] = Gun(root, Guns[i].group, Guns[i].model, Guns[i].instance);
            rig = root.gameObject.AddComponent<CombatCharacterRig>();
            string profile = ProfileFor(slot);
            if (rig.InitializeSkinned(Resources.Load<SkinnedCharacterProfile>(profile), weapons, new Vector3(0, -DuelArenaLayout.GroundY, 0)))
            {
                AttachKnife(root);
                return;
            }
            Debug.LogWarning("Duel body: " + profile + " cannot drive a Humanoid; drawing a stand-in.");
            Object.Destroy(rig);
            rig = null;
            standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
            Object.Destroy(standIn.GetComponent<Collider>());
            standIn.name = "StandIn";
            standIn.SetParent(root, false);
            standIn.GetComponent<Renderer>().material.color = new Color(.33f, .37f, .22f);
        }

        private static Transform Gun(Transform root, string group, string model, string instance)
        {
            var holder = new GameObject(group).transform;
            holder.SetParent(root, false);
            var prefab = Resources.Load<GameObject>("Models/" + model);
            if (prefab == null) return holder;
            var gun = Object.Instantiate(prefab, holder).transform;
            gun.name = instance;
            // The catalog is Blender Z-up with the muzzle along +Y; the station turns it the same way (AttachImportedModel).
            gun.localRotation = Quaternion.Euler(0, -90, 0) * Quaternion.Euler(-90f, 0f, 0f);
            gun.localScale = Vector3.one * .72f;
            return holder;
        }

        private void AttachKnife(Transform root)
        {
            var model = Resources.Load<GameObject>("Models/hero_knife");
            if (model == null) return;
            var knife = Object.Instantiate(model, root);
            knife.name = "HeroKnife";
            var steel = Resources.Load<Material>("Models/hero_knife_mat");
            foreach (var renderer in knife.GetComponentsInChildren<Renderer>())
            {
                if (steel != null) renderer.sharedMaterial = steel;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            var sheath = new GameObject("HeroSheath").transform;
            sheath.SetParent(root, false);
            rig.AttachKnife(knife.transform, sheath);
        }

        /// <summary>A shot leaving the gun in hand: recoil, flash and brass, the bore on <paramref name="end"/>.</summary>
        public void Fired(int slot, Vector3 end)
        {
            if (rig == null || slot < 0 || slot >= Guns.Length) return;
            rig.AimPoint = end;
            rig.Fire(slot);
        }

        /// <param name="reloading">Known on the opponent only as a flag; its progress is then timed here.</param>
        public void Tick(in DuelSimState pose, bool aiming, bool reloading, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            Actor.position = pose.Position;
            Vector3 velocity = hasLast ? (pose.Position - lastPosition) / deltaTime : Vector3.zero;
            lastPosition = pose.Position;
            hasLast = true;
            if (pose.Health < lastHealth - .01f) hitTimer = .18f;
            lastHealth = pose.Health;
            hitTimer = Mathf.Max(0f, hitTimer - deltaTime);
            if (standIn != null)
            {
                float height = pose.Height;
                standIn.localScale = new Vector3(DuelArenaLayout.PlayerRadius * 2f, height * .5f, DuelArenaLayout.PlayerRadius * 2f);
                standIn.localPosition = new Vector3(0f, height * .5f - DuelArenaLayout.GroundY, 0f);
                return;
            }

            bool knife = pose.ActiveWeaponSlot >= DuelPlayerSim.KnifeSlot || pose.KnifeRemaining > 0;
            if (!knife) lastGun = pose.ActiveWeaponSlot;
            var weapon = DuelPlayerSim.WeaponAt(lastGun);
            rig.SelectWeapon(lastGun);
            rig.KnifeInHand = knife;
            var profile = rig.Profile;
            float clipRate = profile != null && profile.meleeContactTime > 0f ? profile.meleeContactTime / CombatRules.KnifeContactSeconds : 1f;
            rig.KnifeSwing = pose.KnifeRemaining > 0 ? (CombatRules.KnifeSwingSeconds - pose.KnifeRemaining) * clipRate : -1f;
            rig.DrawProgress = pose.DrawRemaining > 0 ? Mathf.Clamp01(1f - pose.DrawRemaining / CombatRules.WeaponDrawSeconds) : -1f;
            rig.Crouch = pose.Crouch;
            rig.Walk = pose.Walk;
            aim = Mathf.MoveTowards(aim, aiming ? 1f : 0f, deltaTime * CombatThirdPersonCamera.AimBlendSpeed);
            rig.AimWeight = aim;
            Vector3 look = Quaternion.Euler(pose.Pitch, pose.Yaw, 0f) * Vector3.forward;
            rig.AimPoint = DuelPlayerSim.EyePosition(pose) + look * 30f;

            float duration = weapon.ReloadSecondsFor(weapon.MagazineSize - pose.AmmoAt(lastGun));
            reloadElapsed = reloading ? reloadElapsed + deltaTime : 0f;
            float remaining = pose.ReloadAt(lastGun);
            float progress = remaining > 0 ? 1f - remaining / Mathf.Max(.01f, duration) :
                reloading ? Mathf.Clamp01(reloadElapsed / Mathf.Max(.01f, weapon.ReloadSeconds)) : -1f;
            rig.ReloadSeconds = remaining > 0 ? duration : weapon.ReloadSeconds;
            var rounds = weapon.RoundReload;
            rig.ReloadPhases = rounds.HasValue ? new Vector2(rounds.Value.StartSeconds, rounds.Value.EndSeconds) : Vector2.zero;
            rig.Tick(deltaTime, velocity, look, progress, hitTimer / .18f, pose.Dead ? 1f : 0f);
        }
    }
}
