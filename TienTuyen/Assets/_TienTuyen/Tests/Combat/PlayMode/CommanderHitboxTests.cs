using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TienTuyen.Combat.Tests
{
    // Regression: a real shot at the rendered commander's head must not miss the smaller soldier column.
    public sealed class CommanderHitboxTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private CombatGame game;
        private Transform visual;
        private Vector3 head;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            game.enabled = false;
            yield return null;
            game.BeginRun();
            game.ExecuteCheat("boss");
            game.StepSimulation(6.1f, Vector2.zero);
            game.CommanderActor.position = new Vector3(4f, .8f, 0f);
            game.transform.Find("Player").position = new Vector3(0f, .8f, 0f);
            yield return null;
            yield return null;
            visual = GameObject.Find("CommanderVisual").transform;
            var animator = visual.GetComponentInChildren<Animator>();
            head = animator.GetBoneTransform(HumanBodyBones.Head).position + Vector3.up * .08f;
            var renderer = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            renderer.BakeMesh(mesh);
            var bounds = mesh.bounds;
            Vector3 feet = renderer.transform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            Vector3 top = renderer.transform.TransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            Debug.Log($"Commander regression geometry: rendered feet={feet}, top={top}, head aim={head}, actor={game.CommanderActor.position}");
            Object.Destroy(mesh);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            var scene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Commander hitbox cleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void ShotAtRenderedCommanderHeadHitsAndReportsHeadshot()
        {
            float before = game.CommanderHealth;
            bool impactHit = false, headshot = false;
            float reported = 0;
            game.ShotImpact += (point, hit) => impactHit = hit;
            game.DamageFeedback += (point, damage, headHit, critical) => { reported = damage; headshot = headHit; };
            Shoot(head);
            Assert.That(impactHit, Is.True, "The crosshair is on the actual animated head, above the ordinary soldier's hit column.");
            Assert.That(headshot, Is.True, "A hit on the rendered boss head must receive headshot feedback.");
            Assert.That(reported, Is.GreaterThan(0));
            Assert.That(before - game.CommanderHealth, Is.EqualTo(reported).Within(.01f));
        }

        [Test]
        public void CommanderHeadshotDealsMoreDamageThanRenderedTorsoShot()
        {
            float bodyDamage = 0, headDamage = 0;
            bool lastHead = false;
            game.DamageFeedback += (point, damage, headHit, critical) => { headDamage = damage; lastHead = headHit; };
            Shoot(head - Vector3.up * .65f);
            bodyDamage = headDamage;
            Assert.That(bodyDamage, Is.GreaterThan(0));
            Assert.That(lastHead, Is.False, "The rendered chest is a body hit.");
            headDamage = 0;
            Shoot(head);
            Assert.That(lastHead, Is.True);
            Assert.That(headDamage, Is.EqualTo(bodyDamage * CombatRules.HeadshotMultiplier).Within(.01f));
        }

        [Test]
        public void ShotAboveRenderedCommanderHeadMisses()
        {
            float before = game.CommanderHealth;
            bool? hit = null;
            game.ShotImpact += (point, struck) => hit = struck;
            Shoot(head + Vector3.up * .65f);
            Assert.That(hit, Is.False);
            Assert.That(game.CommanderHealth, Is.EqualTo(before));
        }

        private void Shoot(Vector3 target)
        {
            ((float[])typeof(CombatGame).GetField("weaponFireTimer", PrivateInstance).GetValue(game))[game.ActiveWeaponSlot] = 0;
            typeof(CombatGame).GetField("rng", PrivateInstance).SetValue(game, new System.Random(123));
            Vector3 origin = target - Vector3.right * 7f;
            game.SetViewInput(90f, target, true, origin, Vector3.right);
            typeof(CombatGame).GetMethod("FireWeapon", PrivateInstance).Invoke(game, null);
        }
    }
}
