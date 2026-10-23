using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace TienTuyen.Combat.Tests
{
    public sealed class CombatRocketAudioTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        CombatGame game;
        CombatAudio audio;
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike");
            game = Object.FindFirstObjectByType<CombatGame>(); game.enabled = false;
            yield return null; game.BeginRun(); audio = game.GetComponent<CombatAudio>();
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var old = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Rocket audio cleanup"));
            yield return SceneManager.UnloadSceneAsync(old);
        }
        [TestCase(false)] [TestCase(true)]
        public void RocketLaunchAndDetonationPlayRecordedCues(bool hostile)
        {
            if (hostile) { game.ExecuteCheat("boss"); game.StepSimulation(6.1f, Vector2.zero); }
            var played = (Dictionary<string, float>)typeof(CombatAudio).GetField("lastPlayed", Private).GetValue(audio);
            played.Clear(); var from = new Vector3(0, 3, 0);
            typeof(CombatGame).GetMethod("LaunchRocket", Private).Invoke(game,
                new object[] { from, from + Vector3.forward, 0f, 1f, hostile });
            Assert.That(played.ContainsKey("Rocket_Launch" + (hostile ? "@world" : "")), Is.True,
                "Both launcher owners must produce a launch sound.");
            typeof(CombatGame).GetMethod("UpdateRockets", Private).Invoke(game, new object[] { .1f });
            Assert.That(played.ContainsKey("Rocket_Explosion@world"), Is.True, "Detonation must produce positional explosion audio.");
            AssertRecordedClip("Rocket_Launch"); AssertRecordedClip("Rocket_Explosion");
        }
        [Test] public void DisabledAudioDoesNotConsumeRocketEvents()
        {
            audio.enabled = false;
            var played = (Dictionary<string, float>)typeof(CombatAudio).GetField("lastPlayed", Private).GetValue(audio);
            played.Clear();
            typeof(CombatGame).GetMethod("LaunchRocket", Private).Invoke(game,
                new object[] { Vector3.up * 3, Vector3.up * 3 + Vector3.forward, 0f, 1f, false });
            typeof(CombatGame).GetMethod("UpdateRockets", Private).Invoke(game, new object[] { .1f });
            Assert.That(played.ContainsKey("Rocket_Launch"), Is.False);
            Assert.That(played.ContainsKey("Rocket_Explosion@world"), Is.False);
        }
        [Test] public void RocketPresentationDoesNotCreateSyntheticAudioOutsideMixer()
        {
            foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                Assert.That(clip.name, Is.Not.EqualTo("RPGLaunch"));
                Assert.That(clip.name, Is.Not.EqualTo("RPGExplosion"));
            }
        }
        void AssertRecordedClip(string name)
        {
            var loaded = (Dictionary<string, AudioClip>)typeof(CombatAudio).GetField("clips", Private).GetValue(audio);
            var recording = Resources.Load<AudioClip>("CombatAudio/" + name);
            Assert.That(recording, Is.Not.Null, name + " must ship a recording, not the synthetic fallback tone.");
            Assert.That(loaded[name], Is.SameAs(recording));
            Assert.That(recording.length, Is.GreaterThan(.2f));
        }
    }
}
