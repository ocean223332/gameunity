using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TienTuyen.Combat.Tests
{
    public sealed class CombatCharacterAnimationTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private CombatGame game;
        private CombatArtDirector art;
        private CombatCharacterRig hero;

        [UnitySetUp]
        public IEnumerator LoadPresentation()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            Assert.That(game, Is.Not.Null);
            game.enabled = false;
            art = game.GetComponent<CombatArtDirector>();
            Assert.That(art, Is.Not.Null);
            for (var frame = 0; frame < 20 && game.transform.Find("Player/HeroVisual") == null; frame++)
                yield return null;
            hero = game.transform.Find("Player/HeroVisual").GetComponent<CombatCharacterRig>();
            Assert.That(hero, Is.Not.Null);
            Assert.That(hero.IsInitialized, Is.True);
            game.BeginRun();
            SetField(game, "spawnTimer", 999f);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanScene()
        {
            var combatScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Character animation test cleanup"));
            yield return SceneManager.UnloadSceneAsync(combatScene);
        }

        [UnityTest]
        public IEnumerator GameplayMovementArticulatesFeetAndStoppingReturnsToIdle()
        {
            Assert.That(hero.LeftFoot, Is.Not.Null);
            Assert.That(hero.RightFoot, Is.Not.Null);
            var start = hero.transform.position;
            var initialLeft = hero.PoseRoot.InverseTransformPoint(hero.LeftFoot.position);
            var initialRight = hero.PoseRoot.InverseTransformPoint(hero.RightFoot.position);
            float leftMotion = 0, rightMotion = 0, footLift = 0, elapsed = 0;
            while (elapsed < .8f)
            {
                var dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
                game.StepSimulation(dt, Vector2.right);
                elapsed += dt;
                yield return null;
                var left = hero.PoseRoot.InverseTransformPoint(hero.LeftFoot.position);
                var right = hero.PoseRoot.InverseTransformPoint(hero.RightFoot.position);
                leftMotion = Mathf.Max(leftMotion, Vector3.Distance(left, initialLeft));
                rightMotion = Mathf.Max(rightMotion, Vector3.Distance(right, initialRight));
                footLift = Mathf.Max(footLift, left.y - initialLeft.y, right.y - initialRight.y);
            }
            Assert.That(Vector3.Distance(hero.transform.position, start), Is.GreaterThan(.5f));
            Assert.That(leftMotion, Is.GreaterThan(.04f), "Left leg must move relative to the character, not only follow its root.");
            Assert.That(rightMotion, Is.GreaterThan(.04f));
            Assert.That(footLift, Is.GreaterThan(.015f), "Walking must lift a foot above its idle sole position.");
            Assert.That(hero.MovementBlend, Is.GreaterThan(.4f));
            elapsed = 0;
            while (elapsed < 1f)
            {
                elapsed += Time.unscaledDeltaTime;
                game.StepSimulation(Time.unscaledDeltaTime, Vector2.zero);
                yield return null;
            }
            Assert.That(hero.MovementBlend, Is.LessThan(.03f));
            Assert.That(Vector3.Distance(hero.PoseRoot.InverseTransformPoint(hero.LeftFoot.position), initialLeft), Is.LessThan(.04f));
            Assert.That(Vector3.Distance(hero.PoseRoot.InverseTransformPoint(hero.RightFoot.position), initialRight), Is.LessThan(.04f));
        }

        [Test]
        public void SkinnedStrafeReversalKeepsAnklesContinuous()
        {
            var actor = CreateTarget();
            actor.GetComponent<Renderer>().sharedMaterial = game.ShooterMaterial;
            Invoke(art, "UpdateEnemies");
            var shooter = actor.GetComponentInChildren<CombatCharacterRig>();
            Assert.That(shooter.IsSkinned, Is.True);
            var failures = new List<string>();
            foreach (var rig in new[] { hero, shooter })
            {
                float speed = rig == hero ? 4.75f : 2.05f;
                foreach (float direction in new[] { 1f, -1f })
                {
                    rig.ResetPose();
                    Vector3 previousLeft = Vector3.zero, previousRight = Vector3.zero;
                    float maximum = 0f;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        rig.Tick(1f / 60f, Vector3.right * (speed * direction * (frame < 60 ? 1f : -1f)), Vector3.forward);
                        Vector3 left = rig.PoseRoot.InverseTransformPoint(rig.LeftFoot.position);
                        Vector3 right = rig.PoseRoot.InverseTransformPoint(rig.RightFoot.position);
                        if (frame >= 60)
                            maximum = Mathf.Max(maximum, Vector3.Distance(left, previousLeft), Vector3.Distance(right, previousRight));
                        previousLeft = left;
                        previousRight = right;
                    }
                    string sample = rig.Profile.name + (direction > 0f ? " right_to_left" : " left_to_right") + ": " + maximum.ToString("F6") + " m/frame";
                    TestContext.WriteLine(sample);
                    if (maximum >= .15f) failures.Add(sample);
                }
            }
            Assert.That(failures, Is.Empty, "Reversing must stay below 0.15 m/frame at 60 Hz, above the shipping clips' normal ankle travel: " + string.Join("; ", failures));
        }

        [Test]
        public void HeldWeaponAimsAtRealTargetsAndBothHandsFollowTheirGrips()
        {
            var target = CreateTarget();
            // Standing, then crouched on the crouched clips.
            foreach (float crouch in new[] { 0f, 1f })
            foreach (var direction in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
            {
                hero.Crouch = crouch;
                target.position = game.transform.Find("Player").position + direction * 4f;
                FireReadyWeapon();
                Assert.That(Vector3.Dot(game.PlayerAimDirection.normalized, direction), Is.GreaterThan(.99f));
                Assert.That(hero.MuzzleTransform, Is.Not.Null);
                Assert.That(Vector3.Dot(hero.MuzzleTransform.forward.normalized, direction), Is.GreaterThan(.97f),
                    "The accepted shot must aim its muzzle immediately, before the next animation tick.");
                for (var frame = 0; frame < 90; frame++) hero.Tick(1f / 60f, Vector3.zero, game.PlayerAimDirection);
                Assert.That(hero.MuzzleTransform, Is.Not.Null);
                Assert.That(Vector3.Dot(hero.MuzzleTransform.forward.normalized, direction), Is.GreaterThan(.97f));
                AssertHandsAttached(hero);
            }
            hero.Crouch = 0f;
        }

        // The right cheek in the hero's bind pose (body mesh space, metres).
        private static readonly Vector3 CheekBind = new Vector3(.035f, -.088f, 1.49f);
        private const float ProbeRadius = .01f;

        [Test]
        public void ShoulderedFaceStaysOnTheStockAcrossTheAimPitchRange()
        {
            var animator = hero.GetComponentInChildren<Animator>();
            var shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            SkinnedMeshRenderer skin = null;
            foreach (var s in hero.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (skin == null || s.sharedMesh.vertexCount > skin.sharedMesh.vertexCount) skin = s;
            Vector3 cheek = skin.sharedMesh.bindposes[Array.IndexOf(skin.bones, head)].MultiplyPoint(CheekBind);
            var baked = new Mesh();
            List<int> face = null;
            var drifts = new List<string>();
            // Level, then the clamps of the shouldered pitch (+10 up, -15 down).
            foreach (float pitch in new[] { 0f, 10f, -15f })
            {
                hero.ResetPose();
                hero.Relaxed = false;
                hero.SelectWeapon(0);
                hero.AimWeight = 1f;
                for (int frame = 0; frame < 60; frame++)
                {
                    var pose = hero.PoseRoot;
                    hero.AimPoint = shoulder.position + Quaternion.AngleAxis(-pitch, pose.right) * pose.forward * 20f;
                    hero.Tick(1f / 60f, Vector3.zero, pose.forward);
                }
                skin.BakeMesh(baked, false);
                var posed = baked.vertices;
                if (face == null)
                {
                    // The face over the comb: skin within 5 cm of the cheek (renderer-local, the body's metres).
                    face = new List<int>();
                    Vector3 centre = skin.transform.InverseTransformPoint(head.TransformPoint(cheek));
                    for (int i = 0; i < posed.Length; i++)
                        if ((posed[i] - centre).sqrMagnitude < .05f * .05f) face.Add(i);
                }
                // The gap: the face's nearest distance to the weapon, from a probe sphere's overlap (negative inside it).
                var root = hero.CurrentWeaponRoot;
                var colliders = new List<MeshCollider>();
                foreach (var part in root.GetComponentsInChildren<MeshFilter>())
                    colliders.Add(part.gameObject.AddComponent<MeshCollider>());
                float scale = skin.transform.lossyScale.y, gap = float.MaxValue;
                var probe = new GameObject("Face probe").AddComponent<SphereCollider>();
                probe.radius = ProbeRadius * scale;
                Physics.SyncTransforms();
                foreach (int i in face)
                {
                    Vector3 p = skin.transform.TransformPoint(posed[i]);
                    foreach (var collider in colliders)
                        if (Physics.ComputePenetration(probe, p, Quaternion.identity, collider, collider.transform.position, collider.transform.rotation, out _, out float depth))
                            gap = Mathf.Min(gap, ProbeRadius - depth / scale);
                }
                Object.DestroyImmediate(probe.gameObject);
                foreach (var collider in colliders) Object.DestroyImmediate(collider);
                TestContext.WriteLine("pitch " + pitch + ": cheek " + (gap * 1000f).ToString("F1") + " mm over the comb");
                if (gap < -.003f || gap > .005f) drifts.Add(pitch + " deg: " + (gap * 1000f).ToString("F1") + " mm");
            }
            hero.AimPoint = null;
            Object.Destroy(baked);
            Assert.That(drifts, Is.Empty, "The bore follows the whole aim pitch, so the face must follow it too and stay on the comb " +
                "(at most 3 mm into it or 5 mm off it): " + string.Join("; ", drifts));
        }

        [Test]
        public void EveryWeaponKeepsBothHandsOnItsGripsWithoutBrokenWrists()
        {
            var actor = CreateTarget();
            actor.GetComponent<Renderer>().sharedMaterial = game.ShooterMaterial;
            Invoke(art, "UpdateEnemies");
            var shooter = actor.GetComponentInChildren<CombatCharacterRig>();
            Assert.That(shooter.IsSkinned, Is.True);
            var failures = new List<string>();
            // The hero's guns standing, crouched (its crouched clips, reloads on the standing reloads' hand tracks) and walking.
            foreach (var (rig, weapon, crouch, walk) in new[] { (hero, 0, 0f, 0f), (hero, 1, 0f, 0f), (hero, 2, 0f, 0f), (shooter, 0, 0f, 0f),
                         (hero, 0, 1f, 0f), (hero, 1, 1f, 0f), (hero, 2, 1f, 0f), (hero, 0, 0f, 1f), (hero, 1, 0f, 1f), (hero, 2, 0f, 1f) })
            {
                rig.ResetPose();
                rig.SelectWeapon(weapon);
                rig.Crouch = crouch;
                rig.Walk = walk;
                // The hero's reload timed as the game times it (CombatArtDirector): a reload by the round plays the shell clips.
                var definition = rig == hero ? TienTuyen.Content.ContentCatalog.P2.Weapons[weapon] : null;
                var rounds = definition?.RoundReload;
                rig.ReloadSeconds = definition?.ReloadSecondsFor(definition.MagazineSize) ?? 0f;
                rig.ReloadPhases = rounds.HasValue ? new Vector2(rounds.Value.StartSeconds, rounds.Value.EndSeconds) : Vector2.zero;
                var right = new Wrist(rig, false);
                var left = new Wrist(rig, true);
                for (int frame = 0; frame < 260; frame++)
                {
                    // Idle, run, strafe both ways, run back with a hit, shots, then a full reload.
                    var velocity = frame < 40 ? Vector3.zero : frame < 80 ? Vector3.forward * 4f : frame < 110 ? Vector3.right * 3f :
                        frame < 140 ? Vector3.left * 3f : frame < 170 ? Vector3.back * 2f : Vector3.zero;
                    // Crouched or walking, the game moves at its crouch or walk speed.
                    velocity *= Mathf.Lerp(1f, CombatRules.CrouchSpeedFactor, crouch) * Mathf.Lerp(1f, CombatRules.WalkSpeedFactor, walk);
                    float hit = frame >= 150 && frame < 155 ? 1f : 0f;
                    float reload = frame >= 200 ? (frame - 200) / 59f : -1f;
                    if (frame == 175 || frame == 185) rig.Fire(weapon);
                    rig.Tick(1f / 60f, velocity, Vector3.forward, reload, hit);
                    string state = rig.Profile.name + (crouch > 0f ? " crouched" : walk > 0f ? " walking" : "") + " weapon " + weapon + " frame " + frame + ": ";
                    var set = CarriedSet(rig);
                    HandGaps(rig, out float grip, out float support);
                    if (grip > .02f) failures.Add(state + "grip hand off its grip");
                    right.Check(state, 40f, failures);
                    // Mid-reload a fitted hold's support hand leaves the fore-end for the magazine; a carried weapon's
                    // support target follows the reload clip's hand there.
                    if (set == null && reload >= .04f && reload <= .96f) continue;
                    if (support > .02f)
                        failures.Add(state + "support hand off the fore-end " + (support * 1000f).ToString("F0") + " mm");
                    left.Check(state, 70f, failures);
                }
                rig.Crouch = rig.Walk = 0f;
            }
            // The wrist limits sit past the approved hands' worst (bend 70 degrees): they catch broken data (a flipped or wrongly
            // framed hand turns 90 degrees and more), not the look of a wrist, which is judged by eye.
            Assert.That(failures, Is.Empty, "Hands must stay on their grips and no wrist may break past the approved hands " +
                "(bend off the forearm <= 75 deg, twist <= 40 grip / 70 support, elbow a hinge when a roll bone takes the twist): " +
                string.Join("; ", failures.GetRange(0, Mathf.Min(12, failures.Count))) + (failures.Count > 12 ? " ... " + failures.Count : ""));
        }

        private static Transform Bone(CombatCharacterRig rig, HumanBodyBones bone) => rig.GetComponentInChildren<Animator>().GetBoneTransform(bone);

        /// <summary>The profile's clip set of the rig's current weapon when the right hand carries it, else null.</summary>
        private static WeaponClipSet CarriedSet(CombatCharacterRig rig)
        {
            var held = typeof(CombatCharacterRig).GetField("weapons", PrivateInstance).GetValue(rig) as Array;
            var entry = held.GetValue((int)typeof(CombatCharacterRig).GetField("selectedWeapon", PrivateInstance).GetValue(rig));
            int carry = (int)entry.GetType().GetField("carry").GetValue(entry);
            return carry >= 0 ? rig.Profile.weaponSets[carry] : null;
        }

        /// <summary>
        /// How far each hand is off the weapon. A fitted hold: palm sockets to the weapon's grip nodes. A carried weapon:
        /// the wrists to their targets on it (the grip nodes then hold the wrist targets).
        /// </summary>
        private static void HandGaps(CombatCharacterRig rig, out float grip, out float support)
        {
            bool carried = rig.Profile != null && CarriedSet(rig) != null;
            grip = Vector3.Distance(carried ? Bone(rig, HumanBodyBones.RightHand).position : rig.GripHand.position, rig.WeaponGrip.position);
            support = Vector3.Distance(carried ? Bone(rig, HumanBodyBones.LeftHand).position : rig.SupportHand.position, rig.WeaponSupport.position);
        }

        /// <summary>
        /// Hand rotation relative to the distal forearm (the lower arm turned by its skinned roll bone, if any), measured from
        /// the model's bind pose. With a roll bone the elbow must stay a hinge: the lower arm may not roll off the upper arm.
        /// </summary>
        private sealed class Wrist
        {
            // Largest roll (degrees) of the lower arm off the upper arm when a roll bone carries the forearm's twist. The solve
            // leaves none; before the roll bone took it the left lower arm rolled about 60 degrees under the fore-end.
            private const float MaxElbowRoll = 10f;
            // Largest bend (degrees) of either hand off the distal forearm.
            private const float MaxBend = 75f;
            private readonly Transform upper, lower, hand, roll;
            private readonly Quaternion bind, elbowBind, rollBind;
            private readonly Vector3 axis, elbowAxis;

            public Wrist(CombatCharacterRig rig, bool left)
            {
                var animator = rig.GetComponentInChildren<Animator>();
                upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                Transform model = rig.Profile.model.transform;
                Transform bindUpper = Find(model, upper.name), bindLower = Find(model, lower.name), bindHand = Find(model, hand.name);
                bind = Quaternion.Inverse(bindLower.rotation) * bindHand.rotation;
                axis = (Quaternion.Inverse(bindLower.rotation) * (bindHand.position - bindLower.position)).normalized;
                elbowBind = Quaternion.Inverse(Quaternion.Inverse(bindUpper.rotation) * bindLower.rotation);
                elbowAxis = (Quaternion.Inverse(bindUpper.rotation) * (bindHand.position - bindLower.position)).normalized;
                var skinned = new HashSet<Transform>();
                foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skinned.UnionWith(skin.bones);
                foreach (Transform child in lower)
                    if ((child.name.EndsWith(left ? "_L_Ulna" : "_R_Ulna") || child.name.EndsWith(left ? "LeftForearmTwist" : "RightForearmTwist")) &&
                        skinned.Contains(child))
                    {
                        roll = child;
                        rollBind = Find(model, child.name).localRotation;
                    }
            }

            public void Check(string state, float maxTwist, List<string> failures)
            {
                Quaternion distal = roll != null ? lower.rotation * (roll.localRotation * Quaternion.Inverse(rollBind)) : lower.rotation;
                Quaternion d = Quaternion.Inverse(distal) * hand.rotation * Quaternion.Inverse(bind);
                float twist = Twist(d, axis);
                (d * Quaternion.Inverse(Quaternion.AngleAxis(twist, axis))).ToAngleAxis(out float bend, out _);
                if (bend > 180f) bend = 360f - bend;
                if (bend > MaxBend || Mathf.Abs(twist) > maxTwist)
                    failures.Add(state + hand.name + " bend " + bend.ToString("F0") + " twist " + twist.ToString("F0"));
                if (roll == null) return;
                float elbowRoll = Twist(Quaternion.Inverse(upper.rotation) * lower.rotation * elbowBind, elbowAxis);
                if (Mathf.Abs(elbowRoll) > MaxElbowRoll)
                    failures.Add(state + lower.name + " rolls " + elbowRoll.ToString("F0") + " off the upper arm");
            }

            private static float Twist(Quaternion d, Vector3 axis)
            {
                if (d.w < 0f) d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
                return 2f * Mathf.Atan2(Vector3.Dot(new Vector3(d.x, d.y, d.z), axis), d.w) * Mathf.Rad2Deg;
            }

            private static Transform Find(Transform root, string name)
            {
                if (root.name == name) return root;
                foreach (Transform child in root)
                {
                    var found = Find(child, name);
                    if (found != null) return found;
                }
                return null;
            }
        }

        [TestCase(WeaponKind.Rifle)]
        [TestCase(WeaponKind.Smg)]
        public void EveryActualShotRefreshesRecoilEvenWhileTracerRemainsVisible(WeaponKind weapon)
        {
            game.ReturnToMenu();
            game.SelectWeapon(weapon);
            game.BeginRun();
            CreateTarget();
            Invoke(art, "UpdatePlayer");
            int shots = 0;
            game.WeaponFired += (slot, start, end) => shots++;
            int ammo = game.Ammo;
            for (var shot = 0; shot < 3; shot++)
            {
                // Fire the actual combat method; only bypass cooldown to prove
                // that overlapping tracer visibility cannot suppress recoil.
                hero.Tick(.05f, Vector3.zero, Vector3.right);
                float before = hero.RecoilAmount;
                FireReadyWeapon();
                Assert.That(shots, Is.EqualTo(shot + 1));
                Assert.That(game.Ammo, Is.EqualTo(ammo - shot - 1));
                Assert.That(hero.RecoilAmount, Is.GreaterThan(before + .001f));
                Assert.That(game.transform.Find("Tracer").gameObject.activeSelf, Is.True);
                hero.Tick(.001f, Vector3.zero, Vector3.right);
                AssertHandsAttached(hero);
            }
            for (var frame = 0; frame < 120; frame++) hero.Tick(1f / 60f, Vector3.zero, Vector3.right);
            Assert.That(hero.RecoilAmount, Is.LessThan(.001f));
        }

        [Test]
        public void CosmeticMuzzleFlashesDoNotConsumeSharedUnityRandomState()
        {
            var savedState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(7319);
                float expectedFirst = UnityEngine.Random.value;
                float expectedSecond = UnityEngine.Random.value;
                UnityEngine.Random.InitState(7319);
                for (var shot = 0; shot < 12; shot++) hero.Fire(shot % 3);
                var root = new GameObject("First-person RNG test");
                var clips = new AnimationClip[3];
                clips[0] = new AnimationClip { name = "idle" };
                clips[1] = new AnimationClip { name = "fire1" };
                clips[2] = new AnimationClip { name = "fire2" };
                var takesType = typeof(CombatFirstPersonView).GetNestedType("Takes", BindingFlags.NonPublic);
                object takes = null;
                try
                {
                    var load = takesType.GetMethod("Load", BindingFlags.Static | BindingFlags.Public);
                    takes = load.Invoke(null, new object[] { root.transform, clips, null });
                    var firstPersonShot = takesType.GetMethod("Shot", BindingFlags.Instance | BindingFlags.Public);
                    for (var shot = 0; shot < 12; shot++) firstPersonShot.Invoke(takes, null);
                }
                finally
                {
                    if (takes != null) takesType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public).Invoke(takes, null);
                    Object.DestroyImmediate(root);
                    foreach (var clip in clips) Object.DestroyImmediate(clip);
                }
                // This assertion failed before Takes used its private System.Random: first-person clip picks consumed UnityEngine.Random.
                Assert.That(UnityEngine.Random.value, Is.EqualTo(expectedFirst));
                Assert.That(UnityEngine.Random.value, Is.EqualTo(expectedSecond));
            }
            finally
            {
                UnityEngine.Random.state = savedState;
            }
        }

        [UnityTest]
        public IEnumerator PauseFreezesAllRigTransformsAndRecoil()
        {
            hero.Tick(.1f, Vector3.right * 4f, Vector3.right);
            hero.Fire();
            game.TogglePause();
            var poses = new Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)>();
            foreach (var node in hero.GetComponentsInChildren<Transform>(true))
                poses.Add(node, (node.localPosition, node.localRotation, node.localScale));
            float phase = hero.GaitPhase, recoil = hero.RecoilAmount;
            for (var frame = 0; frame < 12; frame++)
            {
                game.StepSimulation(1f / 60f, Vector2.one);
                yield return null;
            }
            Assert.That(hero.GaitPhase, Is.EqualTo(phase));
            Assert.That(hero.RecoilAmount, Is.EqualTo(recoil));
            foreach (var pair in poses)
            {
                Assert.That(pair.Key.localPosition, Is.EqualTo(pair.Value.position), pair.Key.name);
                Assert.That(pair.Key.localRotation, Is.EqualTo(pair.Value.rotation), pair.Key.name);
                Assert.That(pair.Key.localScale, Is.EqualTo(pair.Value.scale), pair.Key.name);
            }
        }

        [Test]
        public void ShooterTelegraphAndProjectileKeepBothPlanarAimComponents()
        {
            var actor = CreateTarget();
            actor.position = new Vector3(4f, .8f, 2f);
            var enemy = ((Array)GetField(game, "enemies")).GetValue(0);
            SetField(enemy, "shooter", true);
            SetField(enemy, "attackTimer", 0f);
            actor.GetComponent<Renderer>().sharedMaterial = game.ShooterMaterial;
            Vector3 expected = (game.transform.Find("Player").position - actor.position).normalized;
            Vector3 observed = Vector3.zero;
            Transform observedActor = null;
            int shots = 0;
            game.EnemyFired += (firingActor, direction) => { observedActor = firingActor; observed = direction; shots++; };

            // Exercise the actual telegraph branch, not a hand-authored aim vector.
            Invoke(game, "UpdateEnemies", .01f);
            Assert.That((bool)GetField(enemy, "aiming"), Is.True);
            var telegraph = (Vector2)GetField(enemy, "aimDirection");
            Assert.That(telegraph.x, Is.EqualTo(expected.x).Within(.0001f));
            Assert.That(telegraph.y, Is.EqualTo(expected.z).Within(.0001f));
            int loaded = (int)GetField(enemy, "ammo");
            Invoke(game, "UpdateEnemies", .81f);
            Assert.That(shots, Is.EqualTo(1));
            Assert.That(observedActor, Is.SameAs(actor));
            // One aim is one 3-round burst, then nothing until the cooldown: exactly 3 shots and 3 rounds spent.
            for (int i = 0; i < 6; i++) Invoke(game, "UpdateEnemies", .09f);
            Assert.That(shots, Is.EqualTo(3));
            Assert.That((int)GetField(enemy, "ammo"), Is.EqualTo(loaded - 3));
            Assert.That(Vector3.Dot(observed.normalized, expected), Is.GreaterThan(.999f));
            var bullets = (Array)GetField(game, "bullets");
            object activeBullet = null;
            foreach (var bullet in bullets)
                if ((bool)GetField(bullet, "active")) { activeBullet = bullet; break; }
            Assert.That(activeBullet, Is.Not.Null);
            var velocity = (Vector3)GetField(activeBullet, "velocity");
            // The projectile leaves within the Shooter's random planar spread (4 degrees) of the telegraphed aim.
            Assert.That(Vector3.Angle(new Vector3(velocity.x, 0f, velocity.z), new Vector3(telegraph.x, 0f, telegraph.y)), Is.LessThanOrEqualTo(4.01f));
            Assert.That(Mathf.Abs(velocity.z), Is.GreaterThan(.1f));
        }

        [Test]
        public void GunEnemiesHoldTheirGunWithTheGripDataWristsAndFingers()
        {
            var actor = CreateTarget();
            Invoke(art, "UpdateEnemies");
            var visual = ((System.Collections.IDictionary)GetField(art, "enemyVisuals"))[actor];
            var failures = new List<string>();
            foreach (var name in new[] { "shooter", "elite" })
            {
                ((GameObject)GetField(visual, name)).SetActive(true);
                var rig = (CombatCharacterRig)GetField(visual, name + "Rig");
                var data = Array.Find(rig.Profile.weaponSets, set => set.grip != null)?.grip;
                Assert.That(data, Is.Not.Null, name + " carries no grip data");
                for (int frame = 0; frame < 60; frame++) rig.Tick(1f / 60f, Vector3.zero, rig.transform.forward);
                var bones = new Dictionary<string, Transform>();
                foreach (var t in rig.GetComponentsInChildren<Transform>()) bones[t.name] = t;
                var weapon = bones["ValveBiped.weapon_bone"];
                var animator = rig.GetComponentInChildren<Animator>();
                foreach (var (hand, grip) in new[] { (HumanBodyBones.RightHand, data.right), (HumanBodyBones.LeftHand, data.left) })
                {
                    float gap = Vector3.Distance(animator.GetBoneTransform(hand).position, weapon.position + weapon.rotation * (grip.position * weapon.lossyScale.x));
                    if (gap > .003f) failures.Add(name + " " + hand + " " + (gap * 1000f).ToString("F1") + " mm off its grip");
                }
                // Humanoid playback does not keep the source fingers; the data's rotations must reach the bones.
                foreach (var finger in data.rightFingers.Concat(data.leftFingers))
                {
                    float angle = Quaternion.Angle(bones[finger.bone].localRotation, finger.rotation);
                    if (angle > 1f) failures.Add(name + " " + finger.bone + " " + angle.ToString("F0") + " deg off");
                }
            }
            Assert.That(failures, Is.Empty, string.Join("; ", failures));
        }

        [Test]
        public void TheHeroAndTheGunEnemiesPlayClipsBakedForTheirOwnBodies()
        {
            // Each import re-bakes its clips on its own body: a clip shared with another body would change under it.
            AnimationClip[] Clips(string path) => Resources.Load<SkinnedCharacterProfile>(path).weaponSets
                .SelectMany(s => new[] { s.hold, s.aim, s.run, s.fire, s.reload, s.reloadStart, s.reloadInsert, s.reloadEnd })
                .Where(c => c != null).ToArray();
            var hero = Clips("Characters/PAVN/PAVN_ValveProfile");
            Assert.That(hero, Is.Not.Empty);
            foreach (var enemy in new[] { "Shooter", "Elite" })
                Assert.That(Clips("Characters/Enemies/" + enemy + "ValveProfile").Intersect(hero).Select(c => c.name), Is.Empty, enemy);
        }

        [Test]
        public void HeroFingersBendAsCleanChainsInEveryGripPose()
        {
            // The approved hands pose fingers 1-4 as clean chains: the base swings from bind and may roll the whole finger; the
            // mid and tip then bend in the one plane the base's hinge sets, with no roll of their own. A clip's crumpled finger
            // (a mid bone rolled 100 degrees and more) breaks this in the static grips or any track frame. The approved data's
            // worst whole-finger roll is 59.8 degrees (right little finger mid-reload); its segments hold under 0.05.
            const float MaxFingerRoll = 62f, MaxSegmentRoll = .5f, MaxOffHinge = .5f;
            var bindRotation = new Dictionary<string, Quaternion>();
            var bindPosition = new Dictionary<string, Vector3>();
            foreach (var t in hero.Profile.model.GetComponentsInChildren<Transform>(true))
            {
                bindRotation[t.name] = t.localRotation;
                bindPosition[t.name] = t.localPosition;
            }
            // A tip's long axis runs to its farthest skinned vertex (the hand solve measured it so); the base and mid run to
            // their child joints.
            var tip = new System.Text.RegularExpressions.Regex(@"_[LR]_Finger[1-4]2$");
            var tipAxis = new Dictionary<string, Vector3>();
            foreach (var skin in hero.Profile.model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh;
                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                var bindposes = mesh.bindposes;
                for (int v = 0; v < vertices.Length; v++)
                {
                    var w = weights[v];
                    int bone = w.weight0 >= w.weight1 && w.weight0 >= w.weight2 && w.weight0 >= w.weight3 ? w.boneIndex0 :
                        w.weight1 >= w.weight2 && w.weight1 >= w.weight3 ? w.boneIndex1 : w.weight2 >= w.weight3 ? w.boneIndex2 : w.boneIndex3;
                    string name = skin.bones[bone].name;
                    if (!tip.IsMatch(name)) continue;
                    Vector3 p = bindposes[bone].MultiplyPoint3x4(vertices[v]);
                    if (!tipAxis.TryGetValue(name, out var far) || p.sqrMagnitude > far.sqrMagnitude) tipAxis[name] = p;
                }
            }
            Assert.That(tipAxis.Count, Is.EqualTo(8), "a skinned tip on fingers 1-4 of both hands");
            static float Twist(Quaternion q, Vector3 axis) =>
                Mathf.Abs(Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(Vector3.Dot(new Vector3(q.x, q.y, q.z), axis), q.w) * Mathf.Rad2Deg));
            var failures = new List<string>();
            var worst = new float[3];
            var worstAt = new string[3];
            int chains = 0;
            void Note(int metric, float value, float limit, string what)
            {
                if (value > worst[metric]) { worst[metric] = value; worstAt[metric] = what; }
                if (value > limit) failures.Add(what + " " + value.ToString("F1"));
            }
            void Check(Dictionary<string, Quaternion> pose, char side, string where)
            {
                string hand = "ValveBiped.Bip01_" + side + "_Finger";
                Vector3 Knuckle(int f) => bindPosition[hand + f];
                Vector3 palm = Vector3.Cross((Knuckle(1) + Knuckle(2) + Knuckle(3) + Knuckle(4)) / 4f, Knuckle(4) - Knuckle(1)).normalized;
                for (int f = 1; f <= 4; f++)
                {
                    var bones = new[] { hand + f, hand + f + "1", hand + f + "2" };
                    var local = bones.Select(b => pose.TryGetValue(b, out var r) ? r : bindRotation[b]).ToArray();
                    var axis = new[] { bindPosition[bones[1]].normalized, bindPosition[bones[2]].normalized, tipAxis[bones[2]].normalized };
                    string what = where + " " + side + f;
                    Note(0, Twist(Quaternion.Inverse(bindRotation[bones[0]]) * local[0], axis[0]), MaxFingerRoll, what + " finger roll");
                    for (int k = 1; k < 3; k++)
                        Note(1, Twist(Quaternion.Inverse(bindRotation[bones[k]]) * local[k], axis[k]), MaxSegmentRoll, what + " segment " + k + " roll");
                    // In the hand's frame: the base's bind hinge (across the bind finger and the palm normal), carried by its turn.
                    Vector3 hinge = (local[0] * Quaternion.Inverse(bindRotation[bones[0]]) * Vector3.Cross(bindRotation[bones[0]] * axis[0], palm)).normalized;
                    Quaternion frame = local[0];
                    for (int k = 1; k < 3; k++)
                    {
                        frame *= local[k];
                        Note(2, Mathf.Asin(Mathf.Min(1f, Mathf.Abs(Vector3.Dot(frame * axis[k], hinge)))) * Mathf.Rad2Deg, MaxOffHinge, what + " segment " + k + " off the hinge");
                    }
                    chains++;
                }
            }
            foreach (var set in hero.Profile.weaponSets)
            {
                var data = set.grip;
                Assert.That(data, Is.Not.Null, set.weapon + " has no grip data");
                var grip = data.rightFingers.Concat(data.leftFingers).ToDictionary(f => f.bone, f => f.rotation);
                Check(grip, 'R', data.gun + " grip");
                Check(grip, 'L', data.gun + " grip");
                foreach (var track in data.tracks)
                    foreach (var (side, names, rows) in new[] { ('R', track.rightBones, track.rightFingers), ('L', track.leftBones, track.leftFingers) })
                        for (int i = 0; i + names.Length <= rows.Length; i += names.Length)
                        {
                            var pose = new Dictionary<string, Quaternion>();
                            for (int j = 0; j < names.Length; j++) pose[names[j]] = rows[i + j];
                            Check(pose, side, data.gun + " " + track.clip.name + " frame " + i / names.Length);
                        }
            }
            Assert.That(chains, Is.GreaterThan(100));
            Assert.That(failures, Is.Empty, "Fingers 1-4 must stay clean chains (whole-finger roll <= " + MaxFingerRoll + ", segment roll <= " +
                MaxSegmentRoll + ", mid and tip off the base's hinge plane <= " + MaxOffHinge + " deg). Worst: " +
                string.Join("; ", Enumerable.Range(0, 3).Select(m => worstAt[m] + " " + worst[m].ToString("F1"))) + ". " +
                string.Join("; ", failures.Take(8)) + (failures.Count > 8 ? " ... " + failures.Count : ""));
        }

        [Test]
        public void HeroFireFingersArePerGunAndReachTheBones()
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in hero.GetComponentsInChildren<Transform>()) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            var pulled = new List<Dictionary<string, Quaternion>>();
            var failures = new List<string>();
            for (int weapon = 0; weapon < 3; weapon++)
            {
                hero.ResetPose();
                hero.SelectWeapon(weapon);
                for (int frame = 0; frame < 30; frame++) hero.Tick(1f / 60f, Vector3.zero, Vector3.forward);
                hero.Fire(weapon);
                for (int frame = 0; frame < 6; frame++) hero.Tick(1f / 60f, Vector3.zero, Vector3.forward);
                var set = CarriedSet(hero);
                var track = set.grip.Track(set.fire);
                Assert.That(track, Is.Not.Null, set.grip.gun + " has no fire finger track");
                float at = (float)GetField(GetField(hero, "skinned"), "fireTime") * HandGripData.TrackRate;
                int frames = track.rightFingers.Length / track.rightBones.Length, a = Mathf.Min((int)at, frames - 1), b = Mathf.Min(a + 1, frames - 1);
                pulled.Add(new Dictionary<string, Quaternion>());
                for (int i = 0; i < track.rightBones.Length; i++)
                {
                    var row = Quaternion.Slerp(track.rightFingers[a * track.rightBones.Length + i], track.rightFingers[b * track.rightBones.Length + i], at - a);
                    float angle = Quaternion.Angle(bones[track.rightBones[i]].localRotation, row);
                    if (angle > 1f) failures.Add(set.grip.gun + " " + track.rightBones[i] + " " + angle.ToString("F1") + " deg off its fire frame " + at.ToString("F1"));
                    pulled[weapon][track.rightBones[i]] = row;
                }
            }
            Assert.That(failures, Is.Empty, string.Join("; ", failures));
            float Apart(int x, int y) => pulled[x].Keys.Intersect(pulled[y].Keys).Max(n => Quaternion.Angle(pulled[x][n], pulled[y][n]));
            Assert.That(Mathf.Min(Apart(0, 1), Apart(1, 2), Apart(0, 2)), Is.GreaterThan(1f),
                "Each gun fires with its own right hand: the fire fingers come from that gun's grip, not one shared hand");
        }

        [UnityTest]
        public IEnumerator ReusingEnemyPoolResetsGaitAndUnloadingReleasesEveryGraph()
        {
            var actor = CreateTarget();
            // Exercise the new Humanoid shooter, not only the procedural fallback.
            actor.GetComponent<Renderer>().sharedMaterial = game.ShooterMaterial;
            Invoke(art, "UpdateEnemies");
            var visual = actor.Find("EnemyVisual");
            var rigs = visual.GetComponentsInChildren<CombatCharacterRig>(true);
            Assert.That(rigs.Length, Is.EqualTo(4));
            int count = visual.GetComponentsInChildren<Transform>(true).Length;
            var graphs = new List<PlayableGraph>();
            for (var reuse = 0; reuse < 20; reuse++)
            {
                var activeRig = visual.GetComponentInChildren<CombatCharacterRig>();
                Assert.That(activeRig, Is.Not.Null);
                activeRig.Tick(.1f, Vector3.forward * 4f, Vector3.forward);
                activeRig.Fire();
                graphs.Add((PlayableGraph)GetField(GetField(activeRig, "skinned"), "graph"));
                actor.gameObject.SetActive(false);
                actor.gameObject.SetActive(true);
                Assert.That(activeRig.MovementBlend, Is.EqualTo(0f));
                Assert.That(activeRig.RecoilAmount, Is.EqualTo(0f));
                Assert.That(visual.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(count));
                Assert.That(visual.GetComponentsInChildren<CombatCharacterRig>(true).Length, Is.EqualTo(4));
            }

            foreach (var rig in game.GetComponentsInChildren<CombatCharacterRig>(true))
            {
                var driver = GetField(rig, "skinned");
                if (driver == null) continue;
                var graph = (PlayableGraph)GetField(driver, "graph");
                graphs.Add(graph);
            }
            Assert.That(graphs.Count, Is.GreaterThan(rigs.Length), "Include unused pooled enemies, not only the actor activated by this test.");
            var scene = game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("Skinned graph unload test"));
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
            int surviving = graphs.FindAll(graph => graph.IsValid()).Count;
            TestContext.WriteLine($"Scene unload: {graphs.Count} graphs captured, {surviving} still valid.");
            LogAssert.NoUnexpectedReceived();
            Assert.That(surviving, Is.Zero, "Every graph must be destroyed, including rigs that were never active.");
        }

        [Test]
        public void InfantryDaggerContactLandsOnEveryStrikeThenReturnsToLocomotion()
        {
            var actor = CreateTarget(); // wave 1 spawns infantry only
            var enemy = ((Array)GetField(game, "enemies")).GetValue(0);
            var player = (Transform)GetField(game, "player");
            // Walks into reach with the attack ready: the first strike still waits out the windup.
            actor.position = player.position + Vector3.right * 2f;
            SetField(enemy, "attackTimer", 0f);
            // Strikes absorbed by invulnerability must still swing.
            SetField(game, "invulnerability", 999f);
            int strikes = 0;
            game.EnemyMeleeStruck += struck =>
            {
                if (struck != actor) return;
                Vector3 separation = actor.position - player.position;
                separation.y = 0;
                Assert.That(separation.magnitude, Is.LessThanOrEqualTo(1.12f),
                    "The dagger strike must land within melee contact range.");
                strikes++;
            };
            Invoke(art, "UpdateEnemies");
            var visual = ((System.Collections.IDictionary)GetField(art, "enemyVisuals"))[actor];
            var rig = actor.GetComponentInChildren<CombatCharacterRig>();
            Assert.That(rig.Profile.meleeAttack, Is.Not.Null);
            float contact = rig.Profile.meleeContactTime;
            int reached = -1;
            for (int frame = 0; frame < 240; frame++)
            {
                int before = strikes;
                game.StepSimulation(1f / 60f, Vector2.zero);
                Invoke(art, "UpdateEnemies");
                game.TryGetEnemyAction(actor, out var state);
                if (reached < 0 && state.InMeleeReach) reached = frame;
                if (strikes == 1 && before == 0)
                    Assert.That((frame - reached + 1) / 60f, Is.GreaterThanOrEqualTo(contact - 1e-4f), "The first strike after entering reach waits out the windup");
                float time = (float)GetField(visual, "meleeTime");
                if (strikes > before) Assert.That(time, Is.EqualTo(contact).Within(1e-5f), "Contact pose on the damage tick, frame " + frame);
                else if (!(bool)GetField(visual, "followThrough")) Assert.That(time, Is.LessThanOrEqualTo(contact), "The windup never passes contact without a strike, frame " + frame);
                // The dagger hold survives guard, strike and a hit flinch.
                if (frame == 100) SetField(visual, "hitRemaining", .18f);
                AssertHandsAttached(rig);
            }
            Assert.That(strikes, Is.GreaterThanOrEqualTo(2));

            // Pool reuse mid-swing keeps no stale action; out of reach the rig returns to locomotion.
            actor.gameObject.SetActive(false);
            Invoke(art, "UpdateEnemies");
            actor.position = player.position + Vector3.right * 6f;
            actor.gameObject.SetActive(true);
            Invoke(art, "UpdateEnemies");
            Assert.That((float)GetField(visual, "meleeTime"), Is.LessThan(0f));
            Assert.That(rig.ActionWeight, Is.Zero);
            strikes = 0;
            SetField(enemy, "attackTimer", 0f);
            actor.position = player.position + Vector3.right;
            game.StepSimulation(1f / 60f, Vector2.zero);
            Invoke(art, "UpdateEnemies");
            Assert.That(strikes, Is.EqualTo(1));
            Assert.That(rig.ActionWeight, Is.GreaterThan(0f));
            actor.position = player.position + Vector3.right * 6f;
            for (int call = 0; call < 5000 && rig.ActionWeight > 0f; call++) Invoke(art, "UpdateEnemies");
            Assert.That(rig.ActionWeight, Is.Zero, "The one-shot ends and hands the body back to locomotion.");
        }

        [Test]
        public void ChargerDashActionPlaysExactlyWhileThatActorDashes()
        {
            var actor = CreateTarget();
            var enemy = ((Array)GetField(game, "enemies")).GetValue(0);
            var player = (Transform)GetField(game, "player");
            SetField(enemy, "definition", TienTuyen.Content.ContentCatalog.P2.GetEnemy(TienTuyen.Content.ContentIds.Charger));
            SetField(enemy, "charger", true);
            SetField(enemy, "attackTimer", 0f);
            ((GameObject)GetField(enemy, "chargerMarker")).SetActive(true);
            actor.localScale = new Vector3(.55f, .65f, .55f);
            actor.position = player.position + Vector3.right * 4f;
            SetField(game, "invulnerability", 0f);
            int daggerHits = 0;
            Vector3 dashStart = actor.position;
            game.PlayerDamaged += (_, damage) =>
            {
                Vector3 step = actor.position - dashStart;
                step.y = 0;
                Vector3 toPlayer = player.position - dashStart;
                toPlayer.y = 0;
                float fraction = step.sqrMagnitude > 0f
                    ? Mathf.Clamp01(Vector3.Dot(toPlayer, step) / step.sqrMagnitude) : 0f;
                float contactGap = (toPlayer - step * fraction).magnitude;
                Assert.That(contactGap, Is.LessThanOrEqualTo(1.1f),
                    "A Charger dagger hit must cross the player's contact range.");
                Assert.That((bool)GetField(enemy, "charging"), Is.True,
                    "The telegraph and recovery cannot deal dagger damage.");
                Assert.That(damage, Is.GreaterThan(0f));
                daggerHits++;
            };
            Invoke(art, "UpdateEnemies");
            var visual = ((System.Collections.IDictionary)GetField(art, "enemyVisuals"))[actor];
            var rig = actor.GetComponentInChildren<CombatCharacterRig>();
            Assert.That(rig.Profile.chargeDash, Is.Not.Null);
            int telegraphFrames = 0, dashFrames = 0;
            bool recovering = false, recovered = false;
            for (int frame = 0; frame < 240; frame++)
            {
                dashStart = actor.position;
                game.StepSimulation(1f / 60f, Vector2.zero);
                Invoke(art, "UpdateEnemies");
                Assert.That(game.TryGetEnemyAction(actor, out var state), Is.True);
                bool dashing = state.Kind == EnemyActionKind.ChargeDash;
                Assert.That((bool)GetField(visual, "dashing"), Is.EqualTo(dashing), "Dash clip follows gameplay, frame " + frame);
                if (state.Kind == EnemyActionKind.ChargeTelegraph) telegraphFrames++;
                if (dashing) dashFrames++;
                recovering |= (float)GetField(visual, "recoveryTime") >= 0f;
                recovered |= dashFrames > 0 && !dashing && state.Kind == EnemyActionKind.None && rig.ActionWeight == 0f;
            }
            Assert.That(telegraphFrames, Is.GreaterThan(0));
            Assert.That(dashFrames, Is.GreaterThan(0));
            Assert.That(daggerHits, Is.GreaterThan(0), "The dash must actually land a dagger hit.");
            Assert.That(recovering, Is.True, "The recovery clip plays once the dash stops.");
            Assert.That(recovered, Is.True, "After the dash the body returns to locomotion.");
        }

        [Test]
        public void KilledShooterVisualOutlivesActorReuseWithoutChangingCombatState()
        {
            var actor = CreateTarget();
            actor.GetComponent<Renderer>().sharedMaterial = game.ShooterMaterial;
            Invoke(art, "UpdateEnemies");
            var enemy = ((Array)GetField(game, "enemies")).GetValue(0);
            int kills = game.Kills;
            Invoke(game, "DamageEnemy", enemy, 100000f);
            var pool = game.GetComponent<CombatCorpsePool>();
            Assert.That(pool, Is.Not.Null);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(actor.gameObject.activeSelf, Is.False);
            Assert.That(game.Kills, Is.EqualTo(kills + 1));
            CombatCharacterRig corpse = null;
            // The pool component sits on CombatGame, so select only its detached body.
            foreach (var candidate in pool.GetComponentsInChildren<CombatCharacterRig>())
                if (candidate.name == "EnemyDeathVisual") { corpse = candidate; break; }
            Assert.That(corpse, Is.Not.Null);
            Assert.That(corpse.transform.IsChildOf(actor), Is.False);
            Vector3 bodyPosition = corpse.transform.position;
            actor.position += Vector3.right * 8f;
            actor.gameObject.SetActive(true);
            Invoke(art, "UpdateEnemies");
            pool.Tick(.5f);
            Assert.That(corpse.transform.position, Is.EqualTo(bodyPosition));
            Assert.That(game.Kills, Is.EqualTo(kills + 1));
            var activeRig = actor.GetComponentInChildren<CombatCharacterRig>();
            Assert.That(activeRig.DeathFinished, Is.False);
            Assert.That(activeRig.RecoilAmount, Is.Zero);
            pool.Tick(20f);
            Assert.That(pool.ActiveCount, Is.Zero, "Expired bodies must release bounded presentation slots.");
        }

        [UnityTest]
        public IEnumerator ReloadKeepsLocomotionAndRestartClearsDeath()
        {
            // A presentation clip must neither stall movement nor own ammo timing.
            ((int[])GetField(game, "weaponAmmo"))[0] = 0;
            game.RequestReload();
            float duration = game.ReloadDuration;
            var start = game.transform.Find("Player").position;
            var foot = hero.PoseRoot.InverseTransformPoint(hero.LeftFoot.position);
            float motion = 0;
            for (int frame = 0; frame < Mathf.CeilToInt(duration * 60f) + 2; frame++)
            {
                game.StepSimulation(1f / 60f, Vector2.up);
                yield return null;
                motion = Mathf.Max(motion, Vector3.Distance(foot, hero.PoseRoot.InverseTransformPoint(hero.LeftFoot.position)));
            }
            Assert.That(game.ReloadRemaining, Is.Zero);
            Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
            Assert.That(Vector3.Distance(start, game.transform.Find("Player").position), Is.GreaterThan(1f));
            Assert.That(motion, Is.GreaterThan(.04f));

            SetField(game, "invulnerability", 0f);
            game.ApplyPlayerDamage(100000f);
            Assert.That(game.State, Is.EqualTo(CombatState.Defeat));
            Vector3 actor = game.transform.Find("Player").position;
            for (int frame = 0; frame < 300; frame++)
                hero.Tick(1f / 60f, Vector3.zero, Vector3.forward, -1, 0, 1);
            Assert.That(hero.DeathFinished, Is.True);
            Assert.That(game.transform.Find("Player").position, Is.EqualTo(actor), "Death is visual motion, not actor root motion.");
            game.Restart();
            SetField(game, "spawnTimer", 999f);
            yield return null;
            Assert.That(hero.DeathFinished, Is.False);
            Assert.That(hero.RecoilAmount, Is.Zero);
            Assert.That(game.Health, Is.EqualTo(game.MaxHealth));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ActualWeaponMeshesAgreeWithShotDirectionAcrossPoses(int weapon)
        {
            hero.SelectWeapon(weapon);
            var weaponRoot = hero.WeaponGrip.parent;
            var parts = weaponRoot.GetComponentsInChildren<MeshRenderer>(false);
            var barrel = FindMeshPart(parts, "barrel");
            var receiver = FindMeshPart(parts, "receiver");
            var muzzle = FindMeshPart(parts, "muzzle");
            Assert.That(barrel, Is.Not.Null);
            Assert.That(receiver, Is.Not.Null);
            Assert.That(muzzle, Is.Not.Null);

            for (var frame = 0; frame < 240; frame++)
            {
                // Sample all gait phases, recoil onset/recovery, and a complete
                // reload arc; include strafing and a change in world facing.
                var velocity = frame < 30 ? Vector3.zero : frame < 120 ? Vector3.forward * 4f : Vector3.right * 4f;
                var aim = frame < 120 ? Vector3.forward : Vector3.right;
                float reload = frame >= 180 ? (frame - 180) / 59f : -1f;
                if (frame == 90 || frame == 120 || frame == 150) hero.Fire(weapon);
                hero.Tick(1f / 60f, velocity, aim, reload);
                string context = "weapon=" + weapon + ", frame=" + frame + ", reload=" + reload;

                // Body/stock clearance is reviewed on the deformed skinned mesh in
                // Play mode. Static bounds of old rigid tunic pieces cannot prove it.
                Vector3 geometryDirection = (MeshCenter(muzzle) - MeshCenter(receiver)).normalized;
                Assert.That(Vector3.Dot(geometryDirection, hero.MuzzleTransform.forward), Is.GreaterThan(.8f),
                    "The physical muzzle must be ahead of the receiver along the aim socket: " + context);
                Assert.That(Mathf.Abs(Vector3.Dot(LongestMeshAxis(barrel), hero.MuzzleTransform.forward)), Is.GreaterThan(.97f),
                    "The real barrel axis must agree with the aim socket: " + context);
                Assert.That(Vector3.Distance(MeshCenter(muzzle), hero.MuzzleTransform.position), Is.LessThan(.12f),
                    "A correctly oriented socket cannot hide a displaced mesh muzzle: " + context);
            }
        }

        [Test]
        public void HeroCarriesAWavingVietnameseFlagAndEnemiesCarryTheUsFlag()
        {
            Assert.That(hero.Banner, Is.Not.Null, "The hero model must carry its back banner.");
            var heroColours = MaterialNames(hero.Banner.Materials);
            Assert.That(heroColours, Does.Contain("vn_red").And.Contain("vn_star"), heroColours);
            Assert.That(heroColours, Does.Not.Contain("us_"), heroColours);

            hero.Banner.CullWhenHidden = false;
            var before = hero.Banner.Mesh.vertices;
            for (var frame = 0; frame < 12; frame++) hero.Tick(1f / 60f, Vector3.forward * 4f, Vector3.forward);
            var after = hero.Banner.Mesh.vertices;
            float ripple = 0;
            for (var i = 0; i < before.Length; i++) ripple = Mathf.Max(ripple, Vector3.Distance(before[i], after[i]));
            Assert.That(ripple, Is.GreaterThan(.005f), "The flag cloth must wave as the hero runs.");
            Assert.That(ripple, Is.LessThan(.25f), "The ripple must not tear the cloth away from its pole.");

            foreach (var model in new[] { "enemy_infantry", "enemy_shooter", "enemy_charger", "enemy_elite" })
            {
                var prefab = Resources.Load<GameObject>("Models/" + model);
                Assert.That(prefab, Is.Not.Null, model);
                var colours = new List<Material>();
                foreach (var part in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    if (part.name.StartsWith("banner cloth", StringComparison.Ordinal)) colours.Add(part.sharedMaterial);
                string names = MaterialNames(colours.ToArray());
                Assert.That(names, Does.Contain("us_red").And.Contain("us_white").And.Contain("us_blue"), model + ": " + names);
                Assert.That(names, Does.Not.Contain("vn_"), model + ": " + names);
            }
        }

        private static string MaterialNames(Material[] materials)
        {
            var names = new List<string>();
            foreach (var material in materials) names.Add(material != null ? material.name : "null");
            return string.Join(", ", names);
        }

        private static MeshRenderer FindMeshPart(MeshRenderer[] parts, string name, bool exact = false)
        {
            foreach (var part in parts)
                if (exact ? part.name == name : part.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return part;
            return null;
        }

        private static Vector3 MeshCenter(MeshRenderer renderer) =>
            renderer.transform.TransformPoint(renderer.GetComponent<MeshFilter>().sharedMesh.bounds.center);

        private static Vector3 LongestMeshAxis(MeshRenderer renderer)
        {
            var size = renderer.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            var x = renderer.transform.TransformVector(Vector3.right * size.x);
            var y = renderer.transform.TransformVector(Vector3.up * size.y);
            var z = renderer.transform.TransformVector(Vector3.forward * size.z);
            return (x.sqrMagnitude > y.sqrMagnitude ? x.sqrMagnitude > z.sqrMagnitude ? x : z : y.sqrMagnitude > z.sqrMagnitude ? y : z).normalized;
        }

        private Transform CreateTarget()
        {
            SetField(game, "spawnTimer", 0f);
            Invoke(game, "UpdateSpawn", 0f);
            SetField(game, "spawnTimer", 999f);
            var enemies = (Array)GetField(game, "enemies");
            var enemy = enemies.GetValue(0);
            Assert.That(GetField(enemy, "definition"), Is.Not.Null);
            SetField(enemy, "active", true);
            SetField(enemy, "pending", false);
            SetField(enemy, "hp", 100000f);
            var body = (GameObject)GetField(enemy, "body");
            body.SetActive(true);
            body.transform.position = new Vector3(4f, .8f, 0f);
            ((GameObject)GetField(enemy, "warning")).SetActive(false);
            return body.transform;
        }

        private void FireReadyWeapon()
        {
            ((float[])GetField(game, "weaponFireTimer"))[0] = 0f;
            Invoke(game, "FireWeapon");
        }

        private static void AssertHandsAttached(CombatCharacterRig rig)
        {
            Assert.That(rig.GripHand, Is.Not.Null);
            Assert.That(rig.SupportHand, Is.Not.Null);
            Assert.That(rig.WeaponGrip, Is.Not.Null);
            Assert.That(rig.WeaponSupport, Is.Not.Null);
            HandGaps(rig, out float grip, out float support);
            Assert.That(grip, Is.LessThan(.02f));
            // A guard-holding body carries a one-handed blade; its support hand is free.
            if (!rig.Profile.guardHold) Assert.That(support, Is.LessThan(.02f));
        }

        private static FieldInfo Field(object target, string name)
        {
            var field = target.GetType().GetField(name, PrivateInstance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, "Missing test seam: " + name);
            return field;
        }
        private static object GetField(object target, string name) => Field(target, name).GetValue(target);
        private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
        private static void Invoke(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, PrivateInstance);
            Assert.That(method, Is.Not.Null, "Missing test seam: " + name);
            method.Invoke(target, arguments);
        }
    }
}
