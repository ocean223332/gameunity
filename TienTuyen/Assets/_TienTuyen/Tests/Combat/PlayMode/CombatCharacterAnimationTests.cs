using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
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
        public void HeldWeaponAimsAtRealTargetsAndBothHandsFollowTheirGrips()
        {
            var target = CreateTarget();
            foreach (var direction in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
            {
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
            Invoke(game, "UpdateEnemies", .81f);
            Assert.That(shots, Is.EqualTo(1));
            Assert.That(observedActor, Is.SameAs(actor));
            Assert.That(Vector3.Dot(observed.normalized, expected), Is.GreaterThan(.999f));
            var bullets = (Array)GetField(game, "bullets");
            object activeBullet = null;
            foreach (var bullet in bullets)
                if ((bool)GetField(bullet, "active")) { activeBullet = bullet; break; }
            Assert.That(activeBullet, Is.Not.Null);
            var velocity = (Vector3)GetField(activeBullet, "velocity");
            Assert.That(Vector3.Dot(velocity.normalized, expected), Is.GreaterThan(.999f));
            Assert.That(Mathf.Abs(velocity.z), Is.GreaterThan(.1f));
        }

        [Test]
        public void ReusingEnemyPoolResetsGaitWithoutAddingRigTransforms()
        {
            var actor = CreateTarget();
            Invoke(art, "UpdateEnemies");
            var visual = actor.Find("EnemyVisual");
            var rigs = visual.GetComponentsInChildren<CombatCharacterRig>(true);
            Assert.That(rigs.Length, Is.EqualTo(4));
            int count = visual.GetComponentsInChildren<Transform>(true).Length;
            for (var reuse = 0; reuse < 20; reuse++)
            {
                var activeRig = visual.GetComponentInChildren<CombatCharacterRig>();
                Assert.That(activeRig, Is.Not.Null);
                activeRig.Tick(.1f, Vector3.forward * 4f, Vector3.forward);
                activeRig.Fire();
                actor.gameObject.SetActive(false);
                actor.gameObject.SetActive(true);
                Assert.That(activeRig.MovementBlend, Is.EqualTo(0f));
                Assert.That(activeRig.RecoilAmount, Is.EqualTo(0f));
                Assert.That(visual.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(count));
                Assert.That(visual.GetComponentsInChildren<CombatCharacterRig>(true).Length, Is.EqualTo(4));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ActualWeaponMeshesPointForwardAndStayOutsideTorsoAcrossPoses(int weapon)
        {
            hero.SelectWeapon(weapon);
            var body = FindMeshPart(hero.GetComponentsInChildren<MeshRenderer>(true), "torso tunic", true);
            Assert.That(body, Is.Not.Null, "Test requires the imported torso mesh, not an animation socket.");
            var bodyParts = new List<MeshRenderer> { body };
            foreach (var part in hero.GetComponentsInChildren<MeshRenderer>(true))
            {
                string name = part.name.ToLowerInvariant();
                if (part.enabled && (name.Contains("webbing") || name.Contains("chest plate") || name.Contains("front strap") || name.Contains("chevron")))
                    bodyParts.Add(part);
            }
            Assert.That(bodyParts.Count, Is.GreaterThan(1), "Include visible chest equipment as well as the tunic.");
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

                // Imported meshes are non-readable; transformed mesh bounds
                // preserve their actual nested rotations without reading vertices.
                // Shrink only the torso by 1 cm to allow surface contact, never
                // deep stock/receiver penetration. SAT avoids world-AABB false hits.
                int checkedParts = 0;
                foreach (var part in parts)
                {
                    if (!part.enabled || !part.gameObject.activeInHierarchy || part.name.StartsWith("Flash", StringComparison.Ordinal)) continue;
                    var weaponBox = MeshBox.From(part);
                    foreach (var bodyPart in bodyParts)
                        Assert.That(MeshBox.Overlaps(MeshBox.From(bodyPart, .01f), weaponBox), Is.False,
                            part.name + " intersects " + bodyPart.name + ": " + context);
                    checkedParts++;
                }
                Assert.That(checkedParts, Is.GreaterThanOrEqualTo(5));
                Vector3 geometryDirection = (MeshCenter(muzzle) - MeshCenter(receiver)).normalized;
                Assert.That(Vector3.Dot(geometryDirection, hero.PoseRoot.forward), Is.GreaterThan(.8f),
                    "The physical muzzle must be ahead of the receiver: " + context);
                Assert.That(Mathf.Abs(Vector3.Dot(LongestMeshAxis(barrel), hero.MuzzleTransform.forward)), Is.GreaterThan(.97f),
                    "The real barrel axis must agree with the aim socket: " + context);
                Assert.That(Vector3.Distance(MeshCenter(muzzle), hero.MuzzleTransform.position), Is.LessThan(.12f),
                    "A correctly oriented socket cannot hide a displaced mesh muzzle: " + context);
            }
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

        // Bounds represented by a center and three transformed half-edges. This
        // remains valid under nested nonuniform scale/shear, unlike world AABBs.
        private sealed class MeshBox
        {
            private Vector3 center;
            private readonly Vector3[] edges = new Vector3[3];

            public static MeshBox From(MeshRenderer renderer, float inset = 0f)
            {
                var bounds = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                var result = new MeshBox { center = renderer.transform.TransformPoint(bounds.center) };
                result.edges[0] = renderer.transform.TransformVector(Vector3.right * bounds.extents.x);
                result.edges[1] = renderer.transform.TransformVector(Vector3.up * bounds.extents.y);
                result.edges[2] = renderer.transform.TransformVector(Vector3.forward * bounds.extents.z);
                for (var i = 0; i < 3; i++)
                    result.edges[i] *= Mathf.Max(0f, 1f - inset / Mathf.Max(.00001f, result.edges[i].magnitude));
                return result;
            }

            public static bool Overlaps(MeshBox a, MeshBox b)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (Separated(a, b, Vector3.Cross(a.edges[(i + 1) % 3], a.edges[(i + 2) % 3]))) return false;
                    if (Separated(a, b, Vector3.Cross(b.edges[(i + 1) % 3], b.edges[(i + 2) % 3]))) return false;
                    for (var j = 0; j < 3; j++)
                        if (Separated(a, b, Vector3.Cross(a.edges[i], b.edges[j]))) return false;
                }
                return true;
            }

            private static bool Separated(MeshBox a, MeshBox b, Vector3 axis)
            {
                if (axis.sqrMagnitude < 1e-14f) return false;
                axis.Normalize();
                float radius = 0;
                for (var i = 0; i < 3; i++)
                    radius += Mathf.Abs(Vector3.Dot(axis, a.edges[i])) + Mathf.Abs(Vector3.Dot(axis, b.edges[i]));
                return Mathf.Abs(Vector3.Dot(axis, b.center - a.center)) >= radius - .000001f;
            }
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
            Assert.That(Vector3.Distance(rig.GripHand.position, rig.WeaponGrip.position), Is.LessThan(.02f));
            Assert.That(Vector3.Distance(rig.SupportHand.position, rig.WeaponSupport.position), Is.LessThan(.02f));
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
