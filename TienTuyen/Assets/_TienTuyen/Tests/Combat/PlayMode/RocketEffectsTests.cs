using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class RocketEffectsTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        CombatGame game;
        Component effects;
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike");
            game = Object.FindFirstObjectByType<CombatGame>(); game.enabled = false;
            yield return null; game.BeginRun();
            effects = game.GetComponent("RocketEffects");
            Assert.That(effects, Is.Not.Null, "Rocket presentation must install its reusable effects component.");
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var old = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Rocket effects cleanup"));
            yield return SceneManager.UnloadSceneAsync(old);
        }
        [TestCase(false, false)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(true, true)]
        public void RealProjectileLaunchAndImpactShowFlameTrailSparksSmokeAndGroundScorch(bool hostile, bool firstPerson)
        {
            Camera.main.GetComponent<CombatThirdPersonCamera>().SetFirstPerson(firstPerson);
            Launch(hostile, new Vector3(0, .3f, 0), 2);
            Assert.That(Count("ActiveTrailCount"), Is.EqualTo(1));
            Assert.That(Particles("RocketLaunchFlame"), Is.GreaterThan(0), "Launch/backblast must be visible immediately.");
            Advance(.04f); Tick(.04f);
            Assert.That(Particles("RocketTrailSmoke"), Is.GreaterThan(0));
            Assert.That(Particles("RocketTrailFlame"), Is.GreaterThan(0));
            Advance(.2f);
            Assert.That(Particles("RocketImpactFire"), Is.GreaterThan(0));
            Assert.That(Particles("RocketImpactSparks"), Is.GreaterThan(0));
            Assert.That(Particles("RocketImpactSmoke"), Is.GreaterThan(0));
            Assert.That(Count("ActiveExplosionCount"), Is.EqualTo(1));
            Assert.That(Count("ActiveScorchCount"), Is.EqualTo(1));
            Tick(3);
            Assert.That(Count("ActiveTrailCount"), Is.Zero, "A detonated projectile must cease emission.");
            Assert.That(Count("ActiveExplosionCount"), Is.Zero);
        }
        [TestCase(false)] [TestCase(true)]
        public void ImpactHasLargeLayeredFireBriefFlashAndGroundOnlyDust(bool airborne)
        {
            Launch(false, new Vector3(0, airborne ? 6f : .3f, 0), 2);
            Advance(.2f);
            Assert.That(Particles("RocketImpactFlash"), Is.GreaterThan(0));
            Assert.That(Particles("RocketGroundDust"), airborne ? Is.Zero : Is.GreaterThan(0));
            var fire = effects.GetComponentsInChildren<ParticleSystem>().First(p => p.name == "RocketImpactFire" && p.particleCount > 0);
            var particles = new ParticleSystem.Particle[fire.main.maxParticles];
            int count = fire.GetParticles(particles);
            Assert.That(particles.Take(count).Max(p => p.startSize), Is.GreaterThanOrEqualTo(1.4f), "Fire should read as a large explosion, rather than a small muzzle flash.");
            Tick(.9f);
            Assert.That(Particles("RocketImpactFlash"), Is.Zero, "The bright flash must not obscure aiming for the smoke's whole lifetime.");
            Assert.That(Particles("RocketImpactSmoke"), Is.GreaterThan(0));
            Tick(2f);
            Assert.That(Count("ActiveExplosionCount"), Is.Zero);
        }
        [Test] public void SustainedDetonationsRecycleBoundedObjectsAndSharedMaterials()
        {
            var root = effects.transform.Find("RocketEffectsPool");
            var objects = root.GetComponentsInChildren<Transform>(true);
            var materials = root.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).ToArray();
            for (int i = 0; i < 30; i++) { Launch(i % 2 == 0, new Vector3(i * .02f, .3f, 0), .2f); Advance(.1f); }
            Assert.That(Count("ActiveExplosionCount"), Is.EqualTo(8));
            Assert.That(Count("ActiveScorchCount"), Is.EqualTo(12));
            CollectionAssert.AreEquivalent(objects, root.GetComponentsInChildren<Transform>(true));
            CollectionAssert.AreEqual(materials, root.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).ToArray());
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty, "Presentation must not introduce damaging or blocking colliders.");
            Tick(20);
            Assert.That(Count("ActiveScorchCount"), Is.Zero, "Ground scorch must fade out and release its slot.");
        }
        [TestCase(false)] [TestCase(true)] public void RestartAndMenuStopAllEffectsIncludingLiveTrails(bool menu)
        {
            Launch(false, new Vector3(0, .3f, 0), .2f); Advance(.1f);
            Launch(true, Vector3.up * 3, 20); Tick(.04f);
            if (menu) game.ReturnToMenu(); else game.Restart();
            Assert.That(Count("ActiveTrailCount"), Is.Zero);
            Assert.That(Count("ActiveExplosionCount"), Is.Zero);
            Assert.That(Count("ActiveScorchCount"), Is.Zero);
            foreach (var ps in effects.transform.Find("RocketEffectsPool").GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.That(ps.particleCount, Is.Zero);
                Assert.That(ps.isEmitting, Is.False);
            }
        }
        void Launch(bool hostile, Vector3 origin, float distance) => typeof(CombatGame).GetMethod("LaunchRocket", Private).Invoke(game,
            new object[] { origin, origin + Vector3.forward * distance, 0f, 2.5f, hostile });
        void Advance(float seconds) => typeof(CombatGame).GetMethod("UpdateRockets", Private).Invoke(game, new object[] { seconds });
        void Tick(float seconds) => effects.GetType().GetMethod("Tick").Invoke(effects, new object[] { seconds });
        int Count(string name) => (int)effects.GetType().GetProperty(name).GetValue(effects);
        int Particles(string name) => effects.transform.Find("RocketEffectsPool").GetComponentsInChildren<ParticleSystem>(true)
            .Where(ps => ps.name == name).Sum(ps => ps.particleCount);
    }
}
