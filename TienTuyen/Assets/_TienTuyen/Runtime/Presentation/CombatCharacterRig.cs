using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Rigid-piece animation for the deliberately unskinned low-poly catalog.
    /// Feet are driven by travelled distance, not a clock; two-bone limbs meet
    /// planted boots and actual gun sockets. This never moves the gameplay actor.
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
        private float gaitPhase, movementBlend, clock, recoil, flashTime;
        private float stanceFraction = .6f;
        private Vector3 smoothedVelocity;
        private bool freshPose = true;
        private float scale = 1f;
        private float chestFront = .19f;
        private int shotSequence;
        private readonly Shell[] shells = new Shell[3];
        private CombatBanner banner;
        private static Mesh limbMesh;
        private static Material flashMaterial;
        private static Material brassMaterial;
        private static int resourceUsers;
        private bool ownsResources;

        public bool IsInitialized { get; private set; }
        public float GaitPhase => gaitPhase;
        public float MovementBlend => movementBlend;
        public float RecoilAmount => recoil;
        public Transform LeftFoot => leftLeg?.end;
        public Transform RightFoot => rightLeg?.end;
        public Transform GripHand => rightArm?.end;
        public Transform SupportHand => leftArm?.end;
        public Transform WeaponGrip => CurrentWeapon?.grip;
        public Transform WeaponSupport => CurrentWeapon?.support;
        public Transform MuzzleTransform => CurrentWeapon?.muzzle;
        public Transform PoseRoot => pose;
        public Transform ChestPivot => torso;
        public Transform CurrentWeaponRoot => CurrentWeapon?.root;
        public CombatBanner Banner => banner;
        private HeldWeapon CurrentWeapon => weapons.Length > 0 ? weapons[selectedWeapon] : null;

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
                weapons[i] = ConfigureWeapon(weaponTransforms[i], i);
            }
            if (count > 0)
                for (int i = 0; i < shells.Length; i++)
                {
                    var casing = AddShape("EjectedCasing_" + i, pose, Vector3.zero, new Vector3(.025f, .07f, .025f), brassMaterial);
                    casing.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    casing.gameObject.SetActive(false);
                    shells[i] = new Shell { transform = casing, age = 1f };
                }
            IsInitialized = true;
            SelectWeapon(0);
            ResetMotion();
            ApplyPose(Vector3.zero, -1, 0, 0);
        }

        public void SelectWeapon(int index)
        {
            if (weapons.Length == 0) return;
            selectedWeapon = Mathf.Clamp(index, 0, weapons.Length - 1);
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] == null) continue;
                weapons[i].root.gameObject.SetActive(i == selectedWeapon);
                if (i != selectedWeapon) weapons[i].flash.SetActive(false);
            }
        }

        /// <summary>Call for every actual shot, including shots in an automatic burst.</summary>
        public void Fire(int weaponIndex = 0)
        {
            SelectWeapon(weaponIndex);
            recoil = 1f;
            flashTime = .055f;
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
        }

        public void Tick(float dt, Vector3 worldVelocity, Vector3 worldAim, float reloadProgress = -1f, float hit = 0f, float death = 0f)
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
            if (CurrentWeapon != null) CurrentWeapon.flash.SetActive(flashTime > 0 && death <= 0f);
            Vector3 localVelocity = pose.InverseTransformDirection(smoothedVelocity);
            ApplyPose(localVelocity, reloadProgress, Mathf.Clamp01(hit), Mathf.Clamp01(death));
            banner?.Tick(dt, localVelocity);
            UpdateShells(dt);
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

        private HeldWeapon ConfigureWeapon(Transform root, int index)
        {
            root.SetParent(torso, false);
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
                    support = center - Vector3.right * (partBounds.extents.x * .60f) - Vector3.up * .025f;
                if (part.Contains("muzzle") && center.x > furthest) { muzzle = center + Vector3.right * .028f; furthest = center.x; }
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
        private void ResetMotion()
        {
            movementBlend = 0; gaitPhase = 0; recoil = 0; flashTime = 0; clock = 0;
            stanceFraction = .6f;
            smoothedVelocity = Vector3.zero; freshPose = true;
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
}
