using System.Collections;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class CombatBloodEffectsTests
    {
        [UnityTest]
        public IEnumerator BloodParticlesAdvanceOnlyWhenPresentationTicks()
        {
            var root = new GameObject("Blood clock test");
            try
            {
                var blood = root.AddComponent<CombatBloodEffects>();
                blood.Initialize();
                blood.Hit(root.transform, Vector3.up, Vector3.forward, false, false);
                var particles = root.transform.Find("BloodDroplets").GetComponent<ParticleSystem>();
                var snapshot = new ParticleSystem.Particle[particles.main.maxParticles];
                int count = particles.GetParticles(snapshot);
                Assert.That(count, Is.GreaterThan(0));
                float lifetime = snapshot[0].remainingLifetime;
                yield return null;
                yield return null;
                Assert.That(particles.GetParticles(snapshot), Is.EqualTo(count));
                Assert.That(snapshot[0].remainingLifetime, Is.EqualTo(lifetime).Within(.00001f), "A paused presentation must not advance blood through Unity's automatic particle simulation.");
                blood.Tick(.05f);
                particles.GetParticles(snapshot);
                Assert.That(snapshot[0].remainingLifetime, Is.LessThan(lifetime));
            }
            finally { Object.Destroy(root); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SustainedSplatterReusesOldestDecalsWithoutGrowingThePool()
        {
            var root = new GameObject("Blood pool stress test");
            try
            {
                var blood = root.AddComponent<CombatBloodEffects>();
                blood.Initialize();
                var objects = root.GetComponentsInChildren<Transform>();
                Assert.That(root.transform.childCount, Is.EqualTo(CombatBloodEffects.TotalDecalCapacity + 3));
                for (int i = 0; i < CombatBloodEffects.DecalCapacity * 3 + 1; i++)
                    blood.GroundSplatter(new Vector3(i * .01f, 0, 0), 1f);
                for (int i = 0; i < CombatBloodEffects.SurfaceDecalCapacity * 3 + 1; i++)
                    blood.SurfaceSplatter(new Vector3(0, i * .01f, 0), Vector3.back, 1f);
                Assert.That(blood.ActiveDecalCount, Is.EqualTo(CombatBloodEffects.TotalDecalCapacity));
                CollectionAssert.AreEquivalent(objects, root.GetComponentsInChildren<Transform>());
                Assert.That(root.transform.GetChild(0).position.x, Is.EqualTo(CombatBloodEffects.DecalCapacity * 3 * .01f).Within(.001f));
                Assert.That(root.transform.GetChild(1).position.x, Is.EqualTo((CombatBloodEffects.DecalCapacity * 2 + 1) * .01f).Within(.001f));
                Assert.That(root.transform.GetChild(CombatBloodEffects.DecalCapacity).position.y, Is.EqualTo(CombatBloodEffects.SurfaceDecalCapacity * 3 * .01f).Within(.001f));
                blood.Tick(46f);
                Assert.That(blood.ActiveDecalCount, Is.Zero);
                blood.GroundSplatter(Vector3.zero, 1f);
                Assert.That(blood.ActiveDecalCount, Is.EqualTo(1));
                CollectionAssert.AreEquivalent(objects, root.GetComponentsInChildren<Transform>());
            }
            finally { Object.Destroy(root); }
            yield return null;
        }
    }
}
