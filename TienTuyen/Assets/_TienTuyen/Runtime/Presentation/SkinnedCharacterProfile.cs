using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Everything <see cref="CombatCharacterRig.InitializeSkinned"/> needs to animate one
    /// skinned Humanoid character. Built by an editor importer; shared by hero and enemies.
    /// </summary>
    [CreateAssetMenu(menuName = "Tien Tuyen/Skinned Character Profile")]
    public sealed class SkinnedCharacterProfile : ScriptableObject
    {
        [Tooltip("Model root with a SkinnedMeshRenderer: feet at y=0, faces +Z, metres, materials assigned.")]
        public GameObject model;
        [Tooltip("Humanoid avatar of the model.")]
        public Avatar avatar;
        [Tooltip("Spine, head and arms only (no legs or root). Null builds one at runtime.")]
        public AvatarMask upperBodyMask;

        [Header("Humanoid clips, root motion baked into pose")]
        public AnimationClip idle;
        [Tooltip("Shouldered aim, used as the upper-body layer while a weapon is held.")]
        public AnimationClip aim;
        public AnimationClip runForward, runBackward, strafeLeft, strafeRight;
        [Tooltip("Upper body only; sampled from the gameplay reload progress.")]
        public AnimationClip reload;
        [Tooltip("Upper body only; one shot.")]
        public AnimationClip hit;
        [Tooltip("Full body; one shot, holds the last frame.")]
        public AnimationClip death;

        [Header("Enemy actions (optional, both hands stay on the weapon)")]
        [Tooltip("Upper body only; one shot. Its contact pose is at meleeContactTime.")]
        public AnimationClip meleeAttack;
        [Tooltip("Seconds into meleeAttack where the blow lands (the gameplay damage tick).")]
        public float meleeContactTime;
        [Tooltip("Melee-only enemies: the weapon is a one-handed blade in the right fist (see fistChannel), never shouldered, and aim is the guard clip.")]
        public bool guardHold;
        [Tooltip("runForward carries its own upper body (lean and weapon carry): the aim/guard layer fades out over the forward run.")]
        public bool runCarriesUpperBody;
        [Tooltip("Centre of the closed fist from the wrist, in model metres: along the hand toward the knuckles, and toward the palm. " +
                 "Zero keeps the open-hand socket between wrist and knuckles (rifle grips).")]
        public Vector2 fistChannel;
        [Tooltip("Full body; sampled by the gameplay telegraph progress, ending in the launch pose.")]
        public AnimationClip chargeTelegraph;
        [Tooltip("Full body loop, advanced by actual travel.")]
        public AnimationClip chargeDash;
        [Tooltip("Metres travelled during one chargeDash cycle.")]
        public float chargeDashCycleDistance;
        [Tooltip("Full body; one shot after a dash ends.")]
        public AnimationClip chargeRecovery;

        [Tooltip("Metres the body is raised over death-clip time so the lying body stays on the floor (measured by the importer).")]
        public AnimationCurve deathFloorLift = new AnimationCurve();

        [Tooltip("Outline of each shoe sole in its foot / toe bone space (measured by the importer); empty falls back to joint heights.")]
        public SolePoint[] leftSole = new SolePoint[0], rightSole = new SolePoint[0];

        [Header("Planted-foot speed of each locomotion clip at 1x (m/s)")]
        public float runForwardSpeed, runBackwardSpeed, strafeLeftSpeed, strafeRightSpeed;
        [Tooltip("Locomotion never plays faster than this; beyond it the hips turn toward travel.")]
        public float maxPlaybackRate = 1.3f;

        [Header("Crouched locomotion (optional; an empty clip keeps the standing one)")]
        public AnimationClip crouchIdle;
        public AnimationClip crouchForward, crouchBackward, crouchLeft, crouchRight;
        [Tooltip("Planted-foot speed of each crouched locomotion clip at 1x (m/s).")]
        public float crouchForwardSpeed, crouchBackwardSpeed, crouchLeftSpeed, crouchRightSpeed;

        [Header("Walking locomotion (optional; an empty clip keeps the run; idle keeps the standing one)")]
        public AnimationClip walkForward, walkBackward, walkLeft, walkRight;
        [Tooltip("Planted-foot speed of each walking locomotion clip at 1x (m/s).")]
        public float walkForwardSpeed, walkBackwardSpeed, walkLeftSpeed, walkRightSpeed;

        [Header("Faction flag")]
        [Tooltip("Catalog model whose 'banner pole' / 'banner cloth*' meshes become the back flag.")]
        public GameObject bannerSource;
        [Tooltip("Body-space offset (metres, +Z forward) applied to the flag after it is fitted to the back.")]
        public Vector3 bannerOffset;
        [Tooltip("Point on the back surface in chest-bone space (measured by the importer); zero keeps the catalog placement.")]
        public Vector3 chestBack;

        public float modelScale = 1f;

        [Header("Hand-carried weapons (optional)")]
        [Tooltip("Per-weapon world model and upper-body clips. A weapon listed here rides the model's Weapon bone with both " +
                 "hands solved onto it, instead of the fitted WeaponHold; weapons not listed keep that.")]
        public WeaponClipSet[] weaponSets = new WeaponClipSet[0];

        /// <summary>Model, a valid Humanoid avatar and at least the idle clip; every other clip is optional.</summary>
        public bool IsValid => model != null && avatar != null && avatar.isValid && avatar.isHuman && idle != null;
    }

    /// <summary>
    /// One weapon on the model's Weapon bone: its world model and the upper-body clips that animate the bone and the
    /// wrist targets under it (WeaponRHand, WeaponLHand) with the body. Baked by WeaponClipImporter.
    /// </summary>
    [System.Serializable]
    public sealed class WeaponClipSet
    {
        /// <summary>Bones the clips animate: the weapon (child of the upper chest) and the wrist targets on it.</summary>
        public const string WeaponBone = "Weapon", RightHandBone = "WeaponRHand", LeftHandBone = "WeaponLHand";

        [Tooltip("Game weapon id (rifle, smg, shotgun).")]
        public string weapon;
        [Tooltip("World model whose origin and axes are the Weapon bone's frame, metres; null keeps the weapon's own model.")]
        public GameObject model;
        [Tooltip("Upper body loop: the ready stance standing.")]
        public AnimationClip hold;
        [Tooltip("Upper body loop or pose: aiming; blended over hold by the aim request.")]
        public AnimationClip aim;
        [Tooltip("Upper body loop: the carry while moving; blended in with the locomotion.")]
        public AnimationClip run;
        [Tooltip("Upper body, one shot from each shot. Empty: the procedural kick only.")]
        public AnimationClip fire;
        [Tooltip("Upper body; sampled from the gameplay reload progress.")]
        public AnimationClip reload;
        [Tooltip("Shell-by-shell reload, used instead of reload when all three are set and the game reloads by the round: " +
                 "start and end fitted to the game's start and end phases, the insert looping at its own speed between them.")]
        public AnimationClip reloadStart, reloadInsert, reloadEnd;
        [Tooltip("The same slots crouched, blended in by the crouch; an empty one keeps its standing clip. A crouched reload " +
                 "or fire clip without its own grip track takes its standing clip's.")]
        public AnimationClip crouchHold, crouchAim, crouchRun, crouchFire, crouchReload, crouchReloadStart, crouchReloadInsert, crouchReloadEnd;
        [Tooltip("The moving carry while walking, blended in by the walk; empty keeps run. Every other walking slot is the standing one.")]
        public AnimationClip walkRun;
        [Tooltip("This body's hands on this gun: grip frames, reload tracks and finger corrections. Empty: the hands follow " +
                 "the clips' wrist targets.")]
        public HandGripData grip;

        public bool ShellReload => reloadStart != null && reloadInsert != null && reloadEnd != null;
    }

    /// <summary>A bind-pose sole vertex, local to the foot bone or (when <see cref="toes"/>) the toe bone.</summary>
    [System.Serializable]
    public struct SolePoint
    {
        public bool toes;
        public Vector3 local;
    }
}
