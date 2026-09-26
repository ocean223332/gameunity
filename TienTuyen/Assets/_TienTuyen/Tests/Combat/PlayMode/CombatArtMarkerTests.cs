using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class CombatArtMarkerTests
    {
        private CombatGame game;
        private CombatArtDirector art;

        [UnitySetUp]
        public IEnumerator LoadPresentation()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            Assert.That(game, Is.Not.Null);
            game.enabled = false;
            art = game.GetComponent<CombatArtDirector>();
            Assert.That(art, Is.Not.Null);
            // Start schedules the real presentation build on the following frame.
            for (var frame = 0; frame < 10 && game.transform.Find("Player/HeroVisual") == null; frame++)
                yield return null;
            Assert.That(game.transform.Find("Player/HeroVisual"), Is.Not.Null);
            art.enabled = false;
            game.BeginRun();
        }

        [UnityTearDown]
        public IEnumerator CleanScene()
        {
            var combatScene = SceneManager.GetActiveScene();
            var empty = SceneManager.CreateScene("Art marker test cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(combatScene);
        }

        [Test]
        public void HeroUpdatePulsesFootprintWithoutGrowingMarkerHeight()
        {
            var actor = game.transform.Find("Player");
            var marker = actor.Find("HeroVisual/HeroFootMarker");
            Assert.That(marker, Is.Not.Null);
            for (var frame = 0; frame < 240; frame++)
            {
                var time = frame / 60f;
                SetTime(time);
                InvokeUpdate("UpdatePlayer");
                AssertMarker(marker, .72f * (1f + Mathf.Sin(time * 4f) * .035f));
            }
        }

        [UnityTest]
        public IEnumerator EnemyUpdateKeepsMarkerFlatAcrossPoolReuseAndActorScales()
        {
            var actor = game.transform.Find("Enemy_0");
            var marker = actor.Find("EnemyVisual/EnemyFootMarker");
            Assert.That(marker, Is.Not.Null);
            var actorScales = new[]
            {
                new Vector3(.7f, .75f, .7f),
                new Vector3(.55f, .65f, .55f),
                new Vector3(.95f, 1.05f, .95f)
            };
            for (var frame = 0; frame < 240; frame++)
            {
                // Reuse the same pooled actor, including inactive updates and
                // reactivation at the normal, charger and elite gameplay scales.
                var active = frame % 80 >= 10;
                actor.gameObject.SetActive(active);
                actor.localScale = actorScales[frame / 80];
                var time = frame / 60f;
                SetTime(time);
                InvokeUpdate("UpdateEnemies");
                Assert.That(marker.gameObject.activeSelf, Is.EqualTo(active));
                AssertMarker(marker, .55f * (1f + Mathf.Sin(time * 3f + actor.GetInstanceID() * .01f) * .04f));
            }
            var screenshot = System.Environment.GetEnvironmentVariable("TIEN_TUYEN_MARKER_SCREENSHOT");
            if (!string.IsNullOrEmpty(screenshot))
            {
                actor.position = new Vector3(2f, .8f, 1f);
                actor.localScale = actorScales[0];
                InvokeUpdate("UpdatePlayer");
                InvokeUpdate("UpdateEnemies");
                ScreenCapture.CaptureScreenshot(screenshot);
                for (var frame = 0; frame < 10; frame++) yield return null;
                Assert.That(System.IO.File.Exists(screenshot), Is.True, "Optional marker screenshot was not written.");
            }
        }

        private static void AssertMarker(Transform marker, float expectedDiameter)
        {
            Assert.That(marker.localScale.x, Is.EqualTo(expectedDiameter).Within(.00001f));
            Assert.That(marker.localScale.z, Is.EqualTo(expectedDiameter).Within(.00001f));
            Assert.That(marker.localScale.y, Is.EqualTo(.025f).Within(.00001f));
            // Check actual mesh height through the hierarchy, including actor
            // scale cancellation, even when the pooled enemy is inactive.
            var mesh = marker.GetComponent<MeshFilter>().sharedMesh;
            var worldHeight = marker.TransformVector(Vector3.up * mesh.bounds.size.y).magnitude;
            Assert.That(worldHeight, Is.EqualTo(.05f).Within(.00001f));
        }

        private void SetTime(float time)
        {
            var field = typeof(CombatArtDirector).GetField("time", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(art, time);
        }

        private void InvokeUpdate(string name)
        {
            var method = typeof(CombatArtDirector).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(art, null);
        }
    }
}
