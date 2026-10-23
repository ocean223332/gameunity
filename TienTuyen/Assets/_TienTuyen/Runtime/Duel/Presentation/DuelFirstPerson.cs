using System;
using TienTuyen.Combat;
using TienTuyen.Content;
using TienTuyen.Duel.Net;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The local player's eye in a duel. It reads look and aim-down-sights itself, owns the cursor lock, and stands the
    /// camera at <see cref="DuelPlayerSim.EyePosition"/> of the local body as <see cref="DuelSession"/> predicts it,
    /// drawn between the two fixed steps either side of now. The weapon in hand is the station's
    /// <see cref="CombatFirstPersonView"/>, fed from the same state (<see cref="IFirstPersonSource"/>).
    /// The crosshair ray starts at the eye, and the server also tests the shot from the muzzle, which sits a fixed
    /// distance straight below the eye at every crouch level: that gap is the only place what the player sees and what
    /// the server resolves can disagree, so it must stay small and positive (DuelPlayerSim's EyePosition test pins it).
    /// A correction of the predicted body (lost packets) is eased into the drawn eye and body rather than jumped to;
    /// a respawn or a new round cuts straight to the new place.
    /// </summary>
    internal sealed class DuelFirstPerson : IDuelAimSource, IFirstPersonSource
    {
        public bool LauncherEquipped => false;

        public const float Sensitivity = .11f, PadDegreesPerSecond = 170f;
        public const float FieldOfView = 70f, AimFieldOfView = 52f;
        public const float MinPitch = -60f, MaxPitch = 70f;

        // A correction's offset decays with this time constant (95% gone in three of them), and is never more than
        // CorrectionCap: the measured worst case on a lossy LAN is about .32 m, the cap keeps the drawn eye well inside
        // the body's .38 m radius of the true one so it never shows the player inside cover, and anything beyond the
        // cap is taken at once instead of sliding.
        public const float CorrectionTime = .1f, CorrectionCap = .25f;
        // Faster than a body can move between frames (a run, or a jump's take-off speed): beyond it the drawn body jumped.
        private const float MaxBodySpeed = 8f;
        // A jump this long is a teleport (a respawn), never a correction to ease.
        private const float TeleportDistance = 3f;

        private readonly Camera view;
        private readonly Transform body;
        private float yaw, pitch, aimBlend;
        private bool cursorFree;
        private Vector3 lastTarget, offset;
        private bool hasTarget;
        private int lastGeneration = int.MinValue;
        private DuelSimState pose;
        private int lastShotSlot = -1, lastShotAmmo;

        /// <param name="body">The local player's transform: drawn at the eased pose, its "HeroVisual" child hidden in first person.</param>
        /// <param name="profile">The character the local body wears, whose first-person arms the viewmodels show.</param>
        public DuelFirstPerson(Camera view, Transform body, int slot, string profile)
        {
            this.view = view;
            this.body = body;
            yaw = DuelArenaLayout.SpawnYaw(slot);
            var data = view.GetComponent<CombatFirstPersonView>() ?? view.gameObject.AddComponent<CombatFirstPersonView>();
            // Every viewmodel carries each character's sleeves and hands (ViewmodelArmsPostprocessor): the hero's replace its
            // own "Arms", another's sit beside them as "Arms_<set>"; the profile names its set, empty for the hero's.
            data.ArmsSet = Resources.Load<SkinnedCharacterProfile>(profile)?.firstPersonArms;
            data.Initialize(this, body);
        }

        // ---------- IDuelAimSource ----------

        public float Yaw => yaw;
        public float Pitch => pitch;
        public Vector3 AimOrigin { get; private set; }
        public Vector3 AimDirection => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        public bool Aiming { get; private set; }

        /// <summary>The local body as drawn this frame: between steps, with any correction eased in.</summary>
        public DuelSimState Pose => pose;

        /// <summary>
        /// Reads look and aim and turns the camera. Call before the input collector reads <see cref="Yaw"/> and
        /// <see cref="Pitch"/>; <see cref="AimOrigin"/> is then still the eye of the last <see cref="Place"/>.
        /// </summary>
        public void Look(bool playing, float deltaTime)
        {
            UpdateCursor(playing);
            ReadLook(playing, deltaTime);
            view.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            view.fieldOfView = Mathf.Lerp(FieldOfView, AimFieldOfView, aimBlend);
        }

        /// <summary>
        /// Places the body and the eye from the session's state. Call after the session steps (so the pose holds this
        /// frame's input) and before the plants pick their view.
        /// </summary>
        public void Place(DuelSession session, float deltaTime)
        {
            var current = session.LocalState;
            var previous = session.PreviousLocalState;
            pose = current;
            pose.Position = Vector3.Lerp(previous.Position, current.Position, session.LocalStepFraction);
            pose.Crouch = Mathf.Lerp(previous.Crouch, current.Crouch, session.LocalStepFraction);
            Ease(pose.Position, session.Match.RoundGeneration, deltaTime);
            pose.Position += offset;
            body.position = pose.Position;
            AimOrigin = DuelPlayerSim.EyePosition(pose);
            view.transform.position = AimOrigin;
            DetectShot(current);
        }

        /// <summary>
        /// Keeps the drawn body continuous across a correction: the part of this frame's move no body could make is
        /// taken back into an offset that then decays. Respawns and new rounds clear it.
        /// </summary>
        private void Ease(Vector3 target, int generation, float deltaTime)
        {
            Vector3 step = target - lastTarget;
            bool cut = !hasTarget || generation != lastGeneration || step.sqrMagnitude > TeleportDistance * TeleportDistance;
            lastTarget = target;
            lastGeneration = generation;
            hasTarget = true;
            if (cut) { offset = Vector3.zero; return; }
            float allowed = MaxBodySpeed * deltaTime + .01f;
            if (step.sqrMagnitude > allowed * allowed) offset -= step - Vector3.ClampMagnitude(step, allowed);
            offset = Vector3.ClampMagnitude(offset, CorrectionCap) * Mathf.Exp(-deltaTime / CorrectionTime);
        }

        private void UpdateCursor(bool playing)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (!playing) cursorFree = false;
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) cursorFree = !cursorFree;
            else if (cursorFree && mouse != null && mouse.leftButton.wasPressedThisFrame) cursorFree = false;
            if (!Application.isFocused) return;
            bool locked = playing && !cursorFree;
            var wanted = locked ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != wanted) Cursor.lockState = wanted;
            Cursor.visible = !locked;
        }

        private void ReadLook(bool playing, float deltaTime)
        {
            Vector2 look = Vector2.zero;
            bool aim = false;
            var mouse = Mouse.current;
            if (playing && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                look += mouse.delta.ReadValue() * Sensitivity * PlayerSettings.MouseSensitivity;
                aim |= mouse.rightButton.isPressed;
            }
            var pad = Gamepad.current;
            if (playing && pad != null)
            {
                look += pad.rightStick.ReadValue() * (PadDegreesPerSecond * deltaTime);
                aim |= pad.leftTrigger.isPressed;
            }
            Aiming = aim;
            look *= Mathf.Lerp(1f, .55f, aimBlend);
            yaw = Mathf.Repeat(yaw + look.x, 360f);
            pitch = Mathf.Clamp(pitch - look.y, MinPitch, MaxPitch);
            aimBlend = Mathf.MoveTowards(aimBlend, aim ? 1f : 0f, deltaTime * CombatThirdPersonCamera.AimBlendSpeed);
        }

        /// <summary>A round leaving the magazine of the gun in hand is a shot; a reload or a switch is not.</summary>
        private void DetectShot(in DuelSimState state)
        {
            int slot = state.ActiveWeaponSlot;
            int ammo = state.AmmoAt(slot);
            if (slot == lastShotSlot && ammo < lastShotAmmo && slot < DuelPlayerSim.KnifeSlot)
            {
                Vector3 muzzle = DuelPlayerSim.MuzzlePosition(pose);
                WeaponFired?.Invoke(slot, muzzle, muzzle + AimDirection * DuelPlayerSim.WeaponAt(slot).Range);
            }
            lastShotSlot = slot;
            lastShotAmmo = ammo;
        }

        // ---------- IFirstPersonSource: the duel state under the station's names ----------

        private int GunSlot => pose.ActiveWeaponSlot < DuelPlayerSim.KnifeSlot ? pose.ActiveWeaponSlot : lastGun;
        private int lastGun;

        public CombatState State => pose.Dead ? CombatState.Menu : CombatState.Playing;
        public bool LauncherEquipped => false;
        public WeaponDefinition ActiveWeapon
        {
            get
            {
                if (pose.ActiveWeaponSlot < DuelPlayerSim.KnifeSlot) lastGun = pose.ActiveWeaponSlot;
                return DuelPlayerSim.WeaponAt(GunSlot);
            }
        }
        public WeaponDefinition WeaponAt(int slot) => slot >= 0 && slot < DuelPlayerSim.KnifeSlot ? DuelPlayerSim.WeaponAt(slot) : null;
        public int SelectedEntry => pose.ActiveWeaponSlot == DuelPlayerSim.KnifeSlot || pose.KnifeRemaining > 0 ? WeaponCount : GunSlot;
        public int WeaponCount => DuelPlayerSim.KnifeSlot;
        public bool IsSwitching => pose.DrawRemaining > 0;
        public float SwitchProgress => pose.DrawRemaining <= 0 ? 1f : Mathf.Clamp01(1f - pose.DrawRemaining / CombatRules.WeaponDrawSeconds);
        public float ReloadRemaining => pose.ReloadAt(GunSlot);
        // The sim keeps no total: a reload's length follows the rounds missing, and the magazine fills only at its end.
        public float ReloadDuration => DuelPlayerSim.WeaponAt(GunSlot).ReloadSecondsFor(MagazineSize - Ammo);
        public int Ammo => pose.AmmoAt(GunSlot);
        public int MagazineSize => DuelPlayerSim.WeaponAt(GunSlot).MagazineSize;
        public float KnifeSwingTime => pose.KnifeRemaining > 0 ? CombatRules.KnifeSwingSeconds - pose.KnifeRemaining : -1f;
        public event Action<int, Vector3, Vector3> WeaponFired;
        public float FirstPersonWeight => 1f;
        public float AimBlend => aimBlend;
    }
}
