using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Character presentation for a gameplay actor; never moves the actor itself.
    /// <see cref="InitializeSkinned"/> drives a skinned Humanoid from clips
    /// (<see cref="SkinnedCharacterAnimator"/>); <see cref="Initialize"/> is the procedural
    /// fallback for the unskinned low-poly catalog, whose feet are driven by travelled
    /// distance and whose two-bone limbs meet planted boots and actual gun sockets.
    /// Weapons, recoil, muzzle flash, brass and the faction flag are shared by both.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatCharacterRig : MonoBehaviour
    {
        private sealed class Limb
        {
            public Transform upper, lower, end;
            public float side;
        }

        private sealed class HeldWeapon
        {
            public Transform root, grip, support, muzzle;
            public WeaponHold hold;
            public float holdScale = 1f;
            /// <summary>Resources/Models name of an imported firearm, and its clip set on a skinned profile (-1: none).</summary>
            public string model;
            public int carry = -1;
            public GameObject flash;
            public Vector3[] boundsCorners;
        }

        private sealed class Shell
        {
            public Transform transform;
            public Vector3 position, velocity;
            public float age;
        }

        private Transform pose, torso, head;
        private Limb leftLeg, rightLeg, leftArm, rightArm;
        private HeldWeapon[] weapons = Array.Empty<HeldWeapon>();
        private int selectedWeapon;
        private float gaitPhase, movementBlend, clock, recoil, flashTime, aim, shotHold;
        // A shot shoulders the weapon; it stays up this long after the last one, then returns to the low-ready.
        private const float ShotHoldSeconds = 2f;
        private float stanceFraction = .6f;
        private float lastReload = -1f;
        private bool weaponsHidden;
        private Vector3 smoothedVelocity;
        private bool freshPose = true;
        private float scale = 1f;
        private float chestFront = .19f;
        private int shotSequence;
        private readonly Shell[] shells = new Shell[3];
        private CombatBanner banner;
        private SkinnedCharacterAnimator skinned;
        private Transform bannerMount;
        private float bannerHidden;
        private Vector3 bannerAnchor, bannerForward;
        private const float ShoulderBladeOffset = .07f;
        // Screen size grows as 1/distance: inside these camera distances (the bodies between the
        // over-the-shoulder camera and the hero, and the hero's own flag while aiming) a full flag
        // would cover the crosshair, so it lowers into the back and is gone at the near one.
        private const float BannerHiddenDistance = 2f, BannerFullDistance = 4f;
        private static Mesh limbMesh;
        private static Material flashMaterial;
        private static Material brassMaterial;
        private static int resourceUsers;
        private bool ownsResources;

        public bool IsInitialized { get; private set; }
        public bool IsSkinned => skinned != null;
        /// <summary>Hold the weapon in the low-ready whatever the aim request (the menu pose); a shot still shoulders it.</summary>
        public bool Relaxed { get; set; }
        /// <summary>Hold the weapon in the stand idle, neither shouldered nor aimed at a point, keeping the facing and the
        /// banner (a film's ready stance); a shot still shoulders it.</summary>
        public bool IdleHold { get; set; }
        /// <summary>0..1 request to shoulder the weapon (aiming down the sights); shots raise it on their own.</summary>
        public float AimWeight { get; set; }
        /// <summary>0..1 progress of drawing the weapon in hand (the game's switch timing); negative when not drawing.</summary>
        public float DrawProgress { get; set; } = -1f;
        /// <summary>Seconds the game's current reload lasts; times a weapon's shell-by-shell reload clips.</summary>
        public float ReloadSeconds { get; set; }
        /// <summary>A reload by the round: its start and end phases in seconds; zero for a whole reload.</summary>
        public Vector2 ReloadPhases { get; set; }
        /// <summary>0 standing to 1 crouched (the game's crouch); a skinned body blends in its crouched clips.</summary>
        public float Crouch { get; set; }
        /// <summary>0 running to 1 walking (the game's walk); a skinned body blends in its walking clips unless crouched.</summary>
        public float Walk { get; set; }
        /// <summary>World point the shouldered bore meets (the crosshair); null keeps the bore level.</summary>
        public Vector3? AimPoint { get; set; }
        public SkinnedCharacterProfile Profile { get; private set; }
        /// <summary>The death clip has reached its last frame and is holding it.</summary>
        public bool DeathFinished => skinned != null && skinned.DeathFinished;
        public float GaitPhase => skinned != null ? skinned.GaitPhase : gaitPhase;
        public float MovementBlend => movementBlend;
        /// <summary>0..1 influence of an enemy action clip (melee or charge); 0 without one.</summary>
        public float ActionWeight => skinned != null ? skinned.ActionWeight : 0f;
        public float RecoilAmount => recoil;
        public Transform LeftFoot => skinned != null ? skinned.LeftFoot : leftLeg?.end;
        public Transform RightFoot => skinned != null ? skinned.RightFoot : rightLeg?.end;
        public Transform GripHand => skinned != null ? skinned.GripSocket : rightArm?.end;
        public Transform SupportHand => skinned != null ? skinned.SupportSocket : leftArm?.end;
        public Transform WeaponGrip => CurrentWeapon?.grip;
        public Transform WeaponSupport => CurrentWeapon?.support;
        public Transform MuzzleTransform => CurrentWeapon?.muzzle;
        public Transform PoseRoot => pose;
        public Transform ChestPivot => skinned != null ? skinned.Chest : torso;
        public Transform CurrentWeaponRoot => CurrentWeapon?.root;
        public CombatBanner Banner => banner;
        private HeldWeapon CurrentWeapon => weapons.Length > 0 ? weapons[selectedWeapon] : null;
        private bool Carried => skinned != null && CurrentWeapon != null && CurrentWeapon.carry >= 0;

        public void Initialize(Transform importedModel, Transform[] weaponTransforms)
        {
            if (IsInitialized || importedModel == null) return;
            EnsureResources();
            ownsResources = true;
            resourceUsers++;
            scale = Mathf.Max(.01f, Mathf.Abs(importedModel.localScale.x));
            pose = Node("CharacterPose", transform, importedModel.localPosition);
            pose.localScale = Vector3.one * scale;
            importedModel.SetParent(pose, false);
            importedModel.localPosition = Vector3.zero;
            importedModel.localRotation = Quaternion.Euler(0, 180, 0) * Quaternion.Euler(-90, 0, 0);
            importedModel.localScale = Vector3.one;
            torso = Node("ChestPivot", pose, new Vector3(0, .93f, 0));
            head = Node("HeadPivot", torso, new Vector3(0, .62f, 0));

            // The catalog supplies useful shared materials and sculpted hands /
            // boots. Only its single-piece straight limbs must be replaced.
            var renderers = importedModel.GetComponentsInChildren<MeshRenderer>(true);
            Material cloth = FindMaterial(renderers, "torso tunic");
            Material dark = FindMaterial(renderers, "belt") ?? cloth;
            Material skin = FindMaterial(renderers, "head") ?? cloth;
            leftLeg = MakeLimb("LeftLeg", -1, cloth);
            rightLeg = MakeLimb("RightLeg", 1, cloth);
            leftArm = MakeLimb("SupportArm", -1, cloth);
            rightArm = MakeLimb("GripArm", 1, cloth);
            leftLeg.end = Node("LeftFoot", pose, new Vector3(-.195f, 0, 0));
            rightLeg.end = Node("RightFoot", pose, new Vector3(.195f, 0, 0));
            leftArm.end = Node("SupportHand", pose, new Vector3(-.47f, .91f, .19f));
            rightArm.end = Node("GripHand", pose, new Vector3(.47f, .91f, .19f));
            bool leftBoot = false, rightBoot = false, leftHand = false, rightHand = false;
            foreach (var renderer in renderers)
            {
                string part = renderer.name.ToLowerInvariant();
                Transform mesh = renderer.transform;
                Vector3 center = pose.InverseTransformPoint(MeshCenter(renderer));
                bool left = center.x < 0;
                if (part.Contains("shooter rifle") || part.Contains("shooter muzzle") || part.Contains("slung tool"))
                { renderer.enabled = false; continue; }
                if (part.EndsWith(" arm", StringComparison.Ordinal) || part.EndsWith(" shin", StringComparison.Ordinal) || part.EndsWith(" cuff", StringComparison.Ordinal))
                { renderer.enabled = false; continue; }
                if (part.EndsWith(" boot", StringComparison.Ordinal))
                {
                    mesh.SetParent(left ? leftLeg.end : rightLeg.end, true);
                    if (left) leftBoot = true; else rightBoot = true;
                    continue;
                }
                if (part.EndsWith(" hand", StringComparison.Ordinal))
                {
                    Transform hand = left ? leftArm.end : rightArm.end;
                    hand.localPosition = center;
                    mesh.SetParent(hand, true);
                    if (left) leftHand = true; else rightHand = true;
                    continue;
                }
                bool headPiece = part.Contains("head") || part.Contains("helmet") || part.Contains("cap") || part.Contains("visor") || part.Contains("face") || part == "neck";
                mesh.SetParent(headPiece ? head : torso, true);
                if (part == "torso tunic" || part.Contains("webbing") || part.Contains("front strap") || part.Contains("chest chevron") || part.Contains("chest plate"))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        foreach (var corner in BoundsCorners(filter.sharedMesh.bounds))
                            chestFront = Mathf.Max(chestFront, torso.InverseTransformPoint(mesh.TransformPoint(corner)).z);
                }
            }
            // The faction flag rides on the chest pivot so it leans with the body.
            banner = CombatBanner.Create(torso, renderers, (GetInstanceID() & 1023) * .37f);
            if (!leftBoot) AddShape("Boot", leftLeg.end, new Vector3(0, .1f, .045f), new Vector3(.20f, .20f, .32f), dark);
            if (!rightBoot) AddShape("Boot", rightLeg.end, new Vector3(0, .1f, .045f), new Vector3(.20f, .20f, .32f), dark);
            if (!leftHand) AddShape("Glove", leftArm.end, Vector3.zero, Vector3.one * .16f, skin);
            if (!rightHand) AddShape("Glove", rightArm.end, Vector3.zero, Vector3.one * .16f, skin);
            // Dark knee guards and sleeve cuffs make the new articulation legible.
            AddShape("KneeGuard", leftLeg.lower, new Vector3(0, .41f, .11f), new Vector3(1.08f, .20f, .35f), dark);
            AddShape("KneeGuard", rightLeg.lower, new Vector3(0, .41f, .11f), new Vector3(1.08f, .20f, .35f), dark);

            int count = weaponTransforms == null ? 0 : weaponTransforms.Length;
            weapons = new HeldWeapon[count];
            for (int i = 0; i < count; i++)
            {
                if (weaponTransforms[i] == null) continue;
                weapons[i] = ConfigureWeapon(weaponTransforms[i], i, torso);
            }
            if (count > 0) CreateShells();
            IsInitialized = true;
            SelectWeapon(0);
            ResetMotion();
            ApplyPose(Vector3.zero, -1, 0, 0);
        }

        /// <summary>
        /// Animates <paramref name="profile"/>'s model from its clips. Returns false, changing
        /// nothing, when the profile cannot drive a Humanoid; the caller may then fall back to
        /// <see cref="Initialize"/>. <paramref name="feetOffset"/> is the rig-local sole position
        /// (gameplay actors stand at y=.8, so (0,-.8,0)). Weapons may be empty (unarmed, corpse).
        /// </summary>
        public bool InitializeSkinned(SkinnedCharacterProfile profile, Transform[] weaponTransforms, Vector3 feetOffset)
        {
            if (IsInitialized || profile == null || !profile.IsValid) return false;
            EnsureResources();
            ownsResources = true;
            resourceUsers++;
            Profile = profile;
            scale = Mathf.Max(.01f, profile.modelScale);
            pose = Node("CharacterPose", transform, feetOffset);
            pose.localScale = Vector3.one * scale;
            var model = Instantiate(profile.model, pose, false).transform;
            model.name = profile.model.name;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            skinned = new SkinnedCharacterAnimator(profile, pose, model);
            AttachBanner(profile, skinned.Chest);

            int count = weaponTransforms == null ? 0 : weaponTransforms.Length;
            weapons = new HeldWeapon[count];
            for (int i = 0; i < count; i++)
                if (weaponTransforms[i] != null)
                {
                    weapons[i] = ConfigureWeapon(weaponTransforms[i], i, pose);
                    weapons[i].carry = skinned.FindWeaponSet(weapons[i].model);
                    var worldModel = skinned.WeaponModel(weapons[i].carry);
                    if (worldModel != null) WearModel(weapons[i], worldModel);
                }
            if (count > 0) CreateShells();
            IsInitialized = true;
            SelectWeapon(0);
            ResetMotion();
            ApplySkinned(0f, Vector3.zero, -1f, 0f, 0f);
            return true;
        }

        /// <summary>Pool reuse / respawn: restart locomotion and clear death, hit, reload and recoil.</summary>
        public void ResetPose()
        {
            if (!IsInitialized) return;
            ResetMotion();
            if (skinned != null) ApplySkinned(0f, Vector3.zero, -1f, 0f, 0f);
            else ApplyPose(Vector3.zero, -1, 0, 0);
        }

        private void AttachBanner(SkinnedCharacterProfile profile, Transform chest)
        {
            if (profile.bannerSource == null || chest == null) return;
            // Fitted on the bind pose (nothing is animated yet), so the fit never depends on the
            // playing clip or on whether the character is active. The mount then rides the back
            // point every frame and turns with the chest while the pole stays upright.
            bannerAnchor = profile.chestBack;
            bannerForward = Quaternion.Inverse(chest.rotation) * pose.forward;
            bannerMount = Node("BannerMount", pose, Vector3.zero);
            bannerMount.SetPositionAndRotation(chest.TransformPoint(bannerAnchor), pose.rotation);
            // The catalog figure carries the faction flag at its authored height.
            var source = Instantiate(profile.bannerSource, pose, false).transform;
            source.localPosition = Vector3.zero;
            source.localRotation = Quaternion.Euler(0, 180, 0) * Quaternion.Euler(-90, 0, 0);
            source.localScale = Vector3.one;
            var renderers = source.GetComponentsInChildren<MeshRenderer>(true);
            banner = CombatBanner.Create(bannerMount, renderers, (GetInstanceID() & 1023) * .37f);
            if (banner != null)
            {
                MeshRenderer pole = null;
                foreach (var part in renderers)
                    if (part.name == "banner pole") { pole = part; part.transform.SetParent(banner.Pivot, true); break; }
                Vector3 shift = profile.bannerOffset;
                if (pole != null && bannerAnchor != Vector3.zero)
                {
                    // The catalog figure is bulkier than this body: stand the pole against the
                    // measured back, over the shoulder blade on the catalog's side.
                    var bounds = pole.bounds;
                    Vector3 axis = bannerMount.InverseTransformPoint(bounds.center);
                    float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
                    shift.x += Mathf.Sign(axis.x) * ShoulderBladeOffset - axis.x;
                    shift.z += -(radius + .02f) - axis.z;
                }
                banner.Pivot.localPosition += shift;
            }
            source.gameObject.SetActive(false);
            Destroy(source.gameObject);
        }

        private void FollowBanner()
        {
            if (bannerMount == null) return;
            Transform chest = skinned.Chest;
            Vector3 up = pose.up;
            Vector3 forward = Vector3.ProjectOnPlane(chest.rotation * bannerForward, up);
            bannerMount.position = chest.TransformPoint(bannerAnchor);
            if (forward.sqrMagnitude > 1e-4f) bannerMount.rotation = Quaternion.LookRotation(forward, up);
            var view = Camera.main;
            float shown = view == null ? 1f : Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(BannerHiddenDistance, BannerFullDistance, Vector3.Distance(view.transform.position, bannerMount.position)));
            // The relaxed menu pose folds the banner away, and unfolds it as the camera swings round.
            bannerHidden = Mathf.MoveTowards(bannerHidden, Relaxed ? 1f : 0f, CombatArtDirector.FrameSeconds * 3f);
            shown *= 1f - Mathf.SmoothStep(0f, 1f, bannerHidden);
            bannerMount.localScale = new Vector3(shown, shown, shown);
        }

        public void SelectWeapon(int index)
        {
            if (weapons.Length == 0) return;
            selectedWeapon = Mathf.Clamp(index, 0, weapons.Length - 1);
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] == null) continue;
                weapons[i].root.gameObject.SetActive(i == selectedWeapon && !weaponsHidden);
                if (i != selectedWeapon) weapons[i].flash.SetActive(false);
            }
        }

        /// <summary>Call for every actual shot, including shots in an automatic burst.</summary>
        public void Fire(int weaponIndex = 0)
        {
            SelectWeapon(weaponIndex);
            recoil = 1f;
            flashTime = .055f;
            shotHold = ShotHoldSeconds;
            if (Carried) skinned.Fired();
            else if (aim < 1f)
            {
                // The shot leaves along the bore: shoulder the weapon now, not over the next ticks.
                aim = 1f;
                if (skinned != null) { skinned.RaiseAim(); ApplySkinnedHold(lastReload); }
            }
            if (CurrentWeapon != null)
            {
                CurrentWeapon.flash.SetActive(true);
                CurrentWeapon.flash.transform.localRotation = Quaternion.Euler(0, 0, shotSequence * 137.508f);
                EmitShell();
                shotSequence++;
            }
        }

        /// <summary>A confirmed shot must face its exact trajectory immediately.</summary>
        public void SnapAim(Vector3 worldDirection)
        {
            if (!IsInitialized) return;
            worldDirection.y = 0;
            if (worldDirection.sqrMagnitude < .0001f) return;
            pose.localRotation = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
            freshPose = false;
            // Re-seat weapon and hands on the new facing without advancing any clip.
            if (skinned != null)
            {
                ApplySkinnedHold(lastReload);
                FollowBanner();
            }
        }

        /// <param name="action">Enemy action clip and time derived from gameplay this tick; skinned rigs only.</param>
        public void Tick(float dt, Vector3 worldVelocity, Vector3 worldAim, float reloadProgress = -1f, float hit = 0f, float death = 0f,
            CharacterAction action = default)
        {
            if (!IsInitialized || dt <= 0f || !gameObject.activeInHierarchy) return;
            dt = Mathf.Min(dt, .1f);
            worldVelocity.y = 0;
            // Pool spawn/teleport deltas are not running animation.
            if (worldVelocity.sqrMagnitude > 400f) worldVelocity = Vector3.zero;
            clock += dt;
            float smoothing = 1f - Mathf.Exp(-14f * dt);
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, worldVelocity, smoothing);
            float speed = worldVelocity.magnitude;
            movementBlend = Mathf.Lerp(movementBlend, Mathf.Clamp01(speed / 1.7f), smoothing);
            // Running shortens ground contact rather than accelerating a walking
            // gait into tiny frantic steps. Share contact duty with the foot path.
            float stride = StrideLength(speed);
            stanceFraction = speed <= 3f ? Mathf.Lerp(.6f, .36f, Mathf.InverseLerp(.5f, 3f, speed)) : Mathf.Lerp(.36f, .29f, Mathf.InverseLerp(3f, 6f, speed));
            float cycleDistance = 2f * stride * scale / stanceFraction;
            float cycleRate = Mathf.Min(2.7f, speed / cycleDistance);
            if (speed > .03f) gaitPhase = Mathf.Repeat(gaitPhase + cycleRate * dt * Mathf.PI * 2f, Mathf.PI * 2f);
            worldAim.y = 0;
            if (worldAim.sqrMagnitude < .0001f) worldAim = speed > .05f ? worldVelocity : pose.forward;
            Quaternion facing = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(worldAim.normalized, Vector3.up);
            pose.localRotation = freshPose ? facing : Quaternion.Slerp(pose.localRotation, facing, 1f - Mathf.Exp(-19f * dt));
            freshPose = false;
            recoil = Mathf.Max(0, recoil - dt * 8.5f);
            flashTime = Mathf.Max(0, flashTime - dt);
            shotHold = Mathf.Max(0, shotHold - dt);
            // A carried weapon is always shouldered: a shot does not bring it to the sights.
            float aimTarget = Mathf.Max(Relaxed || IdleHold ? 0f : Mathf.Clamp01(AimWeight), shotHold > 0f && !Carried ? 1f : 0f);
            aim = Mathf.MoveTowards(aim, aimTarget, dt / (aimTarget > aim ? .15f : .35f));
            if (CurrentWeapon != null) CurrentWeapon.flash.SetActive(flashTime > 0 && death <= 0f);
            Vector3 localVelocity = pose.InverseTransformDirection(smoothedVelocity);
            if (skinned != null) ApplySkinned(dt, localVelocity, reloadProgress, Mathf.Clamp01(hit), Mathf.Clamp01(death), action);
            else ApplyPose(localVelocity, reloadProgress, Mathf.Clamp01(hit), Mathf.Clamp01(death));
            banner?.Tick(dt, localVelocity);
            UpdateShells(dt);
        }

        private void ApplySkinned(float dt, Vector3 localVelocity, float reload, float hit, float death, CharacterAction action = default)
        {
            var weapon = CurrentWeapon;
            lastReload = reload;
            localVelocity.y = 0;
            skinned.Carry(weapon != null ? weapon.carry : -1);
            skinned.Draw(DrawProgress);
            skinned.ReloadLength(ReloadSeconds, ReloadPhases);
            skinned.Crouch(Crouch);
            skinned.Walk(Walk);
            skinned.Advance(dt, localVelocity, movementBlend, reload, hit, death, weapon != null, aim, action);
            // A falling body drops its weapon and flag from view (a rigid pole would spear the
            // floor in a backward fall); respawn/reset shows them again.
            if (skinned.Dying != weaponsHidden)
            {
                weaponsHidden = skinned.Dying;
                SelectWeapon(selectedWeapon);
                if (bannerMount != null) bannerMount.gameObject.SetActive(!weaponsHidden);
            }
            ApplySkinnedHold(reload);
            if (!weaponsHidden) FollowBanner();
        }

        private void ApplySkinnedHold(float reload)
        {
            var weapon = CurrentWeapon;
            skinned.Apply(weapon?.root, weapon?.grip, weapon?.support, weapon?.muzzle, weapon?.hold, weapon != null ? weapon.holdScale : 1f,
                recoil, reload, (Relaxed || IdleHold) && shotHold <= 0f ? null : AimPoint, weapon != null);
        }

        private void ApplyPose(Vector3 localVelocity, float reload, float hit, float death)
        {
            float strideSin = Mathf.Sin(gaitPhase);
            float stepBounce = (Mathf.Abs(Mathf.Cos(gaitPhase)) - .5f) * .065f * movementBlend;
            float breath = Mathf.Sin(clock * 2.5f) * .009f * (1f - movementBlend);
            Vector3 travel = localVelocity.sqrMagnitude > .001f ? localVelocity.normalized : Vector3.forward;
            float speed = localVelocity.magnitude;
            float stride = StrideLength(speed);
            float crouch = death * .48f;
            torso.localPosition = new Vector3(strideSin * .028f * movementBlend, .93f + stepBounce + breath - crouch, 0);
            float chestPitch = Mathf.Clamp(localVelocity.z * 1.5f, -7, 9) - recoil * 7 + hit * 8 + death * 72;
            torso.localRotation = Quaternion.Euler(chestPitch,
                strideSin * 3f * movementBlend, -Mathf.Clamp(localVelocity.x * 1.8f, -9, 9) + strideSin * 2.5f * movementBlend);
            head.localRotation = Quaternion.Euler(-3f - chestPitch * .25f, -strideSin * 2 * movementBlend, 0);
            AnimateLeg(leftLeg, gaitPhase, travel, stride, stepBounce, death);
            AnimateLeg(rightLeg, gaitPhase + Mathf.PI, travel, stride, stepBounce, death);

            var weapon = CurrentWeapon;
            if (weapon != null)
            {
                float reloadArc = reload >= 0 ? Mathf.Sin(Mathf.Clamp01(reload) * Mathf.PI) : 0;
                // Native weapon +X becomes character +Z; keep aim level as the chest leans.
                weapon.root.localRotation = Quaternion.Inverse(torso.localRotation) * Quaternion.Euler(0, -90, 0) * Quaternion.Euler(0, 0, recoil * 7f - reloadArc * 16f);
                // Seat the actual stock in front of the chest, not the weapon's
                // authoring origin inside it. Project the cached mesh bounds after
                // recoil/reload rotation so even a tilted buttstock stays clear.
                float rear = float.PositiveInfinity;
                foreach (var corner in weapon.boundsCorners)
                    rear = Mathf.Min(rear, (weapon.root.localRotation * Vector3.Scale(corner, weapon.root.localScale)).z);
                weapon.root.localPosition = new Vector3(.04f, .35f - reloadArc * .08f,
                    chestFront + .035f - rear - recoil * .020f);
                Vector3 grip = pose.InverseTransformPoint(weapon.grip.position);
                Vector3 support = pose.InverseTransformPoint(weapon.support.position);
                if (reloadArc > 0)
                {
                    // Reach to the belt for a magazine, then return to the fore-end.
                    Vector3 magazine = torso.localPosition + new Vector3(-.22f, -.12f, .22f);
                    support = Vector3.Lerp(support, magazine, reloadArc);
                }
                AnimateArm(rightArm, grip, false);
                AnimateArm(leftArm, support, true);
                rightArm.end.rotation = weapon.grip.rotation;
                leftArm.end.rotation = weapon.support.rotation;
            }
            else
            {
                // Unarmed charging infantry still has a full counter-swing gait.
                AnimateArm(leftArm, new Vector3(-.39f, 1.04f + Mathf.Max(0, strideSin) * .09f * movementBlend,
                    .12f - strideSin * .24f * movementBlend), true);
                AnimateArm(rightArm, new Vector3(.39f, 1.04f + Mathf.Max(0, -strideSin) * .09f * movementBlend,
                    .12f + strideSin * .24f * movementBlend), false);
            }
        }

        private void AnimateLeg(Limb limb, float phase, Vector3 travel, float stride, float bounce, float death)
        {
            float cycle = Mathf.Repeat(phase / (Mathf.PI * 2f), 1f);
            // Linear planted sweep cancels actor travel. Run/sprint adds an airborne
            // interval; recovery keeps a smooth high knee and heel-first landing.
            bool stance = cycle < stanceFraction;
            float t = stance ? cycle / stanceFraction : (cycle - stanceFraction) / (1f - stanceFraction);
            float forward = stance ? Mathf.Lerp(1, -1, t) : Mathf.Lerp(-1, 1, t * t * (3 - 2 * t));
            float lift = stance ? 0 : Mathf.Sin(t * Mathf.PI) * .24f;
            Vector3 foot = new Vector3(limb.side * .195f, lift * movementBlend, 0) + travel * (forward * stride * movementBlend);
            foot.y = lift * movementBlend;
            limb.end.localPosition = foot;
            float pitch = stance ? Mathf.Lerp(-7, 13, t) : Mathf.Lerp(19, -9, t);
            limb.end.localRotation = Quaternion.Euler(pitch * movementBlend * (1 - death), 0, 0);
            Vector3 hip = new Vector3(limb.side * .17f, .80f + bounce - death * .39f, 0);
            Vector3 ankle = foot + Vector3.up * .18f;
            Vector3 knee = SolveJoint(hip, ankle, .34f, .34f, Vector3.forward);
            Segment(limb.upper, hip, knee, .19f);
            Segment(limb.lower, knee, ankle, .16f);
        }

        private void AnimateArm(Limb limb, Vector3 hand, bool support)
        {
            // The arm emerges from the forward half of the sculpted shoulder pad,
            // allowing a natural two-handed reach without elongating the forearm.
            Vector3 shoulder = pose.InverseTransformPoint(torso.TransformPoint(new Vector3(limb.side * .335f, .48f, .12f)));
            Vector3 pole = new Vector3(limb.side * .65f, -.55f, -.18f);
            Vector3 elbow = SolveJoint(shoulder, hand, .40f, .42f, pole);
            Segment(limb.upper, shoulder, elbow, .19f);
            Segment(limb.lower, elbow, hand, .15f);
            limb.end.localPosition = hand;
            if (CurrentWeapon == null) limb.end.localRotation = Quaternion.Euler(0, 0, support ? -8 : 8);
        }

        private static Vector3 SolveJoint(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole)
        {
            Vector3 delta = target - root;
            float realDistance = delta.magnitude;
            Vector3 axis = realDistance > .0001f ? delta / realDistance : Vector3.down;
            float distance = Mathf.Clamp(realDistance, .015f, upper + lower - .004f);
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            float bend = Mathf.Sqrt(Mathf.Max(.0001f, upper * upper - along * along));
            Vector3 projectedPole = pole - axis * Vector3.Dot(pole, axis);
            if (projectedPole.sqrMagnitude < .001f) projectedPole = Vector3.right;
            return root + axis * along + projectedPole.normalized * bend;
        }

        private HeldWeapon ConfigureWeapon(Transform root, int index, Transform parent)
        {
            root.SetParent(parent, false);
            root.localPosition = new Vector3(.04f, .35f, .55f);
            root.localRotation = Quaternion.Euler(0, -90, 0);
            root.localScale = Vector3.one * .90f;
            var result = new HeldWeapon { root = root };
            Vector3 grip = new Vector3(-.04f, -.065f, 0);
            Vector3 support = new Vector3(.22f, .008f, 0);
            Vector3 muzzle = new Vector3(.53f, .055f, 0);
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            float furthest = float.NegativeInfinity;
            Bounds gunBounds = new Bounds();
            bool hasBounds = false;
            // A bladed tool (E-tool) is held round its handle: no drop of the support hand under a rifle fore-end.
            bool bladed = Array.Exists(renderers, r => r.name.ToLowerInvariant().Contains("blade"));
            // Firearms imported from Resources/Models have a fitted hold and its grip sockets.
            if (!bladed)
                foreach (Transform child in root)
                {
                    string model = child.name.StartsWith("Rifle", StringComparison.Ordinal) ? "rifle" :
                        child.name.StartsWith("Smg", StringComparison.Ordinal) ? "smg" :
                        child.name.StartsWith("Shotgun", StringComparison.Ordinal) ? "shotgun" :
                        child.name.EndsWith("WeaponBlenderModel", StringComparison.Ordinal) ? "m16" : null;
                    if (model == null) continue;
                    result.model = model;
                    result.hold = WeaponHold.Find(model);
                    if (result.hold == null) continue;
                    result.holdScale = child.localScale.x;
                    break;
                }
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || !ActiveBelow(renderer.transform, root)) continue;
                string part = renderer.name.ToLowerInvariant();
                Vector3 center = root.InverseTransformPoint(MeshCenter(renderer));
                var filter = renderer.GetComponent<MeshFilter>();
                Bounds partBounds = new Bounds(center, Vector3.zero);
                if (filter != null && filter.sharedMesh != null)
                    foreach (var corner in BoundsCorners(filter.sharedMesh.bounds))
                        partBounds.Encapsulate(root.InverseTransformPoint(renderer.transform.TransformPoint(corner)));
                if (!hasBounds) { gunBounds = partBounds; hasBounds = true; }
                else gunBounds.Encapsulate(partBounds);
                if (part == "grip") grip = center;
                if (part.Contains("handguard") || part.Contains("heat shield") || part.Contains("wide pump"))
                    support = center - Vector3.right * (partBounds.extents.x * .60f) - (bladed ? Vector3.zero : Vector3.up * .025f);
                if (part.Contains("muzzle") && center.x > furthest) { muzzle = center + Vector3.right * .028f; furthest = center.x; }
            }
            if (result.hold != null)
            {
                grip = WeaponHold.ToRoot(result.hold.grip) * result.holdScale;
                support = WeaponHold.ToRoot(result.hold.support) * result.holdScale;
            }
            result.grip = Node("WeaponGrip_" + index, root, grip);
            result.boundsCorners = BoundsCorners(hasBounds ? gunBounds : new Bounds(Vector3.zero, new Vector3(.7f, .2f, .1f)));
            result.support = Node("WeaponSupport_" + index, root, support);
            result.muzzle = Node("Muzzle_" + index, root, muzzle);
            result.muzzle.localRotation = Quaternion.Euler(0, 90, 0);
            var flashRoot = Node("MuzzleFlash", result.muzzle, Vector3.forward * .08f);
            // Crossed faceted spikes stay readable from the overhead camera.
            AddShape("FlashCore", flashRoot, Vector3.zero, new Vector3(.10f, .09f, .24f), flashMaterial);
            var spike = AddShape("FlashStar", flashRoot, new Vector3(0, 0, .035f), new Vector3(.20f, .025f, .11f), flashMaterial);
            spike.localRotation = Quaternion.Euler(0, 0, 35);
            result.flash = flashRoot.gameObject;
            result.flash.SetActive(false);
            return result;
        }

        /// <summary>
        /// A clip set's world model replaces the weapon's own: it sits in the root's frame (the Weapon bone's) at the
        /// character's metres, and the muzzle moves to the model's Muzzle node with the bore along the model's +Z.
        /// </summary>
        private void WearModel(HeldWeapon weapon, GameObject model)
        {
            Transform root = weapon.root;
            foreach (Transform child in root)
                if (child != weapon.grip && child != weapon.support && child != weapon.muzzle) child.gameObject.SetActive(false);
            var worn = Instantiate(model, root, false).transform;
            worn.name = model.name;
            worn.localPosition = Vector3.zero;
            worn.localRotation = Quaternion.identity;
            worn.localScale = Vector3.one * (pose.lossyScale.x / root.lossyScale.x);
            // The magazine comes off for the reload (SkinnedCharacterAnimator moves it by the grip data's track).
            var grip = skinned.GripData(weapon.carry);
            if (grip != null && grip.HasMagazine) MagazineSplit.Apply(worn, grip.magazineBox);
            Bounds bounds = new Bounds();
            bool any = false;
            foreach (var renderer in worn.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                foreach (var corner in BoundsCorners(filter.sharedMesh.bounds))
                {
                    Vector3 point = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (any) weapon.boundsCorners = BoundsCorners(bounds);
            Transform muzzle = worn.Find("Muzzle");
            if (muzzle != null) weapon.muzzle.localPosition = root.InverseTransformPoint(muzzle.position);
            weapon.muzzle.localRotation = Quaternion.identity;
        }

        private void CreateShells()
        {
            for (int i = 0; i < shells.Length; i++)
            {
                var casing = AddShape("EjectedCasing_" + i, pose, Vector3.zero, new Vector3(.025f, .07f, .025f), brassMaterial);
                casing.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                casing.gameObject.SetActive(false);
                shells[i] = new Shell { transform = casing, age = 1f };
            }
        }

        private Limb MakeLimb(string name, float side, Material material)
        {
            return new Limb { side = side,
                upper = AddShape(name + "Upper", pose, Vector3.zero, Vector3.one, material),
                lower = AddShape(name + "Lower", pose, Vector3.zero, Vector3.one, material) };
        }

        private static Transform AddShape(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            Transform node = Node(name, parent, position);
            node.localScale = size;
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = limbMesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = material == flashMaterial ? ShadowCastingMode.Off : ShadowCastingMode.On;
            return node;
        }

        private static void Segment(Transform segment, Vector3 from, Vector3 to, float thickness)
        {
            Vector3 delta = to - from;
            segment.localPosition = (from + to) * .5f;
            segment.localRotation = Quaternion.FromToRotation(Vector3.up, delta);
            segment.localScale = new Vector3(thickness, delta.magnitude + .035f, thickness);
        }

        private static Transform Node(string name, Transform parent, Vector3 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = position;
            return node;
        }

        private static Vector3 MeshCenter(MeshRenderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? renderer.transform.TransformPoint(filter.sharedMesh.bounds.center) : renderer.transform.position;
        }

        private static bool ActiveBelow(Transform node, Transform root)
        {
            for (var current = node; current != null && current != root; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }

        private static Vector3[] BoundsCorners(Bounds bounds)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < corners.Length; i++)
                corners[i] = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            return corners;
        }

        private static Material FindMaterial(MeshRenderer[] renderers, string exactName)
        {
            foreach (var renderer in renderers) if (renderer.name == exactName) return renderer.sharedMaterial;
            return renderers.Length > 0 ? renderers[0].sharedMaterial : null;
        }

        private void OnEnable() { ResetMotion(); }
        private void OnDisable() { skinned?.ReleaseGraph(); }
        private void ResetMotion()
        {
            movementBlend = 0; gaitPhase = 0; recoil = 0; flashTime = 0; clock = 0; aim = 0; shotHold = 0;
            stanceFraction = .6f; lastReload = -1f;
            smoothedVelocity = Vector3.zero; freshPose = true;
            skinned?.Reset();
            foreach (var weapon in weapons) if (weapon != null && weapon.flash != null) weapon.flash.SetActive(false);
            foreach (var shell in shells)
                if (shell != null) { shell.age = 1f; shell.transform.gameObject.SetActive(false); }
        }

        private static float StrideLength(float speed) => Mathf.Lerp(.20f, .32f, Mathf.Clamp01(speed / 6f));

        private void EmitShell()
        {
            var shell = shells[shotSequence % shells.Length];
            if (shell == null) return;
            shell.age = 0;
            shell.position = CurrentWeapon.grip.position + pose.up * .10f + pose.right * .08f;
            shell.velocity = pose.right * (1.4f + (shotSequence % 3) * .22f) + Vector3.up * 1.65f - pose.forward * .3f;
            shell.transform.position = shell.position;
            shell.transform.rotation = pose.rotation * Quaternion.Euler(0, 0, 65);
            shell.transform.gameObject.SetActive(true);
        }

        private void UpdateShells(float dt)
        {
            foreach (var shell in shells)
            {
                if (shell == null || shell.age >= .42f) continue;
                shell.age += dt;
                if (shell.age >= .42f) { shell.transform.gameObject.SetActive(false); continue; }
                shell.velocity += Vector3.down * (7f * dt);
                shell.position += shell.velocity * dt;
                shell.transform.position = shell.position;
                shell.transform.Rotate(510f * dt, 370f * dt, 220f * dt, Space.World);
            }
        }

        private void OnDestroy()
        {
            banner?.Dispose();
            banner = null;
            skinned?.Dispose();
            skinned = null;
            if (!ownsResources) return;
            if (--resourceUsers > 0) return;
            if (limbMesh != null) Destroy(limbMesh);
            if (flashMaterial != null) Destroy(flashMaterial);
            if (brassMaterial != null) Destroy(brassMaterial);
            limbMesh = null; flashMaterial = null; brassMaterial = null;
        }

        private static void EnsureResources()
        {
            if (limbMesh == null)
            {
                // Hard normals on each octagonal side preserve the catalog's faceted style.
                const int sides = 8;
                var vertices = new Vector3[sides * 4 + sides * 6];
                var triangles = new int[sides * 6 + sides * 6];
                int vertex = 0, triangle = 0;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                    Vector3 p = new Vector3(Mathf.Cos(a) * .5f, 0, Mathf.Sin(a) * .5f);
                    Vector3 q = new Vector3(Mathf.Cos(b) * .5f, 0, Mathf.Sin(b) * .5f);
                    int start = vertex;
                    vertices[vertex++] = p - Vector3.up * .5f; vertices[vertex++] = p + Vector3.up * .5f;
                    vertices[vertex++] = q + Vector3.up * .5f; vertices[vertex++] = q - Vector3.up * .5f;
                    triangles[triangle++] = start; triangles[triangle++] = start + 1; triangles[triangle++] = start + 2;
                    triangles[triangle++] = start; triangles[triangle++] = start + 2; triangles[triangle++] = start + 3;
                    start = vertex;
                    vertices[vertex++] = Vector3.up * .5f; vertices[vertex++] = q + Vector3.up * .5f; vertices[vertex++] = p + Vector3.up * .5f;
                    triangles[triangle++] = start; triangles[triangle++] = start + 1; triangles[triangle++] = start + 2;
                    start = vertex;
                    vertices[vertex++] = -Vector3.up * .5f; vertices[vertex++] = p - Vector3.up * .5f; vertices[vertex++] = q - Vector3.up * .5f;
                    triangles[triangle++] = start; triangles[triangle++] = start + 1; triangles[triangle++] = start + 2;
                }
                limbMesh = new Mesh { name = "CharacterRigFacetedLimb", hideFlags = HideFlags.DontSave };
                limbMesh.vertices = vertices; limbMesh.triangles = triangles;
                limbMesh.RecalculateNormals(); limbMesh.RecalculateBounds();
            }
            if (flashMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                flashMaterial = new Material(shader) { name = "CharacterMuzzleFlash", hideFlags = HideFlags.DontSave };
                flashMaterial.color = new Color(1f, .72f, .20f);
                if (flashMaterial.HasProperty("_BaseColor")) flashMaterial.SetColor("_BaseColor", new Color(1f, .72f, .20f));
            }
            if (brassMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                brassMaterial = new Material(shader) { name = "CharacterSpentBrass", hideFlags = HideFlags.DontSave };
                brassMaterial.color = new Color(.73f, .49f, .16f);
                if (brassMaterial.HasProperty("_BaseColor")) brassMaterial.SetColor("_BaseColor", new Color(.73f, .49f, .16f));
                if (brassMaterial.HasProperty("_Metallic")) brassMaterial.SetFloat("_Metallic", .6f);
                if (brassMaterial.HasProperty("_Smoothness")) brassMaterial.SetFloat("_Smoothness", .45f);
            }
        }
    }

    public enum CharacterActionKind { None, Melee, ChargeTelegraph, ChargeDash, ChargeRecovery }

    /// <summary>An enemy action clip and the seconds into it to show this tick.</summary>
    public struct CharacterAction
    {
        public CharacterActionKind kind;
        public float time;

        public CharacterAction(CharacterActionKind kind, float time)
        {
            this.kind = kind;
            this.time = time;
        }
    }
}
