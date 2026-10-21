using System;
using System.Collections;
using System.Linq;
using System.IO;
using TienTuyen.Presentation;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Content;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class CombatSniperTests
    {
        private CombatGame game;
        private Transform player;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<CombatGame>();
            game.enabled = false;
            yield return null;
            yield return null;
            game.BeginRun();
            player = (Transform)Get(game, "player");
            game.SetViewInput(0, player.position + Vector3.forward * 6, false);
            Set(game, "spawnTimer", 999f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var old = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Sniper test cleanup"));
            yield return SceneManager.UnloadSceneAsync(old);
        }

        [Test]
        public void SniperWaitsOneAndAHalfSecondsThenFiresOnlyOneRound()
        {
            var enemy = ActivateSniper();
            game.TogglePause();
            int shots = 0;
            game.EnemyFired += (_, __) => shots++;
            Invoke("UpdateEnemies", .01f);
            Assert.That(Get(enemy, "aiming"), Is.True);
            var laser = (GameObject)Get(enemy, "laser");
            Assert.That(laser.activeSelf, Is.True);
            Assert.That(laser.transform.position.y, Is.EqualTo(1.45f).Within(.001f));
            var red = laser.GetComponent<Renderer>().sharedMaterial.color;
            Assert.That(red.r, Is.GreaterThan(red.g * 4f));
            Invoke("UpdateEnemies", 1.49f);
            Assert.That(shots, Is.Zero);
            Invoke("UpdateEnemies", .02f);
            Assert.That(shots, Is.EqualTo(1));
            Invoke("UpdateEnemies", .3f);
            Assert.That(shots, Is.EqualTo(1), "The sniper must not inherit an automatic burst.");
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void StandingIsHitButCrouchingPassesUnderSniperRound(bool crouch, bool hit)
        {
            ActivateSniper();
            game.TogglePause();
            game.SetCrouchInput(crouch);
            Invoke("MovePlayer", .15f, Vector2.zero);
            Invoke("UpdateEnemies", .01f);
            Invoke("UpdateEnemies", 1.51f);
            float before = game.Health;
            for (int i = 0; i < 10; i++) Invoke("UpdateBullets", .05f);
            Assert.That(game.Health < before, Is.EqualTo(hit));
        }

        [Test]
        public void MovingSidewaysDuringWarningEscapesLockedShot()
        {
            ActivateSniper();
            game.TogglePause();
            Invoke("UpdateEnemies", .01f);
            player.position += Vector3.right * 2f;
            Invoke("UpdateEnemies", 1.51f);
            float before = game.Health;
            for (int i = 0; i < 10; i++) Invoke("UpdateBullets", .05f);
            Assert.That(game.Health, Is.EqualTo(before));
        }

        [Test]
        public void CoverStopsSniperRound()
        {
            var enemy = ActivateSniper();
            game.TogglePause();
            var actor = ((GameObject)Get(enemy, "body")).transform;
            actor.position = new Vector3(-3.2f, .8f, 6.5f);
            player.position = new Vector3(-3.2f, .8f, 1f);
            Set(enemy, "aiming", true);
            Set(enemy, "aimDirection", Vector2.down);
            Set(enemy, "aimTimer", .01f);
            Invoke("UpdateEnemies", .02f);
            float before = game.Health;
            for (int i = 0; i < 10; i++) Invoke("UpdateBullets", .05f);
            Assert.That(game.Health, Is.EqualTo(before));
            Assert.That(((Array)Get(game, "bullets")).Cast<object>().Any(b => (bool)Get(b, "active")), Is.False);
        }

        [UnityTest]
        public IEnumerator TutorialPausesOncePerRunAndSniperHasDistinctModel()
        {
            var enemy = ActivateSniper();
            Assert.That(game.State, Is.EqualTo(CombatState.Paused));
            float remaining = game.WaveRemaining;
            game.StepSimulation(1, Vector2.one);
            Assert.That(game.WaveRemaining, Is.EqualTo(remaining));
            yield return null;
            var actor = ((GameObject)Get(enemy, "body")).transform;
            var sniper = actor.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "ScopedSniperRifle");
            Assert.That(sniper, Is.Not.Null);
            Assert.That(sniper.gameObject.activeInHierarchy, Is.True);
            Assert.That(UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None)
                .Any(t => t.gameObject.activeInHierarchy && t.text.Contains("GIỮ C")), Is.True);
            yield return new WaitForSecondsRealtime(.35f);
            Capture("sniper-tutorial.png");
            yield return null;
            yield return null;
            var acknowledge = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None)
                .First(t => t.gameObject.activeInHierarchy && t.text == "ĐÃ HIỂU")
                .GetComponentInParent<UnityEngine.UI.Button>();
            Assert.That(acknowledge, Is.Not.Null);
            acknowledge.onClick.Invoke();
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Set(enemy, "active", false);
            Set(enemy, "pending", true);
            Set(enemy, "warningTimer", 0f);
            Invoke("UpdateEnemies", 0f);
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            game.Restart();
            ActivateSniper();
            Assert.That(game.State, Is.EqualTo(CombatState.Paused));
        }

        [UnityTest]
        public IEnumerator LaserAndProjectileStartAtTheVisibleScopedSniperRifleMuzzle()
        {
            var enemy = ActivateSniper();
            game.TogglePause();
            Invoke("UpdateEnemies", .01f);
            yield return null;
            yield return null;
            var actor = ((GameObject)Get(enemy, "body")).transform;
            var rig = actor.GetComponentsInChildren<CombatCharacterRig>().First(r => r.name == "Sniper");
            Assert.That(rig.IsSkinned, Is.True, "The sniper must reuse the existing animated enemy body.");
            Assert.That(rig.Profile, Is.SameAs(Resources.Load<SkinnedCharacterProfile>("Characters/Enemies/ShooterValveProfile")));
            Assert.That(game.TryGetEnemyAction(actor, out var action), Is.True);
            Assert.That(Vector3.Distance(rig.MuzzleTransform.position, action.ShotOrigin), Is.LessThan(.015f));
            var laser = (GameObject)Get(enemy, "laser");
            var laserStart = laser.transform.position - laser.transform.forward * (laser.transform.localScale.z * .5f);
            Assert.That(Vector3.Distance(laserStart, rig.MuzzleTransform.position), Is.LessThan(.015f));
            yield return new WaitForSecondsRealtime(.35f);
            Capture("sniper-laser.png");
            yield return null;
            yield return null;
            Invoke("UpdateEnemies", 1.51f);
            var bullet = ((Array)Get(game, "bullets")).Cast<object>().Single(b => (bool)Get(b, "active"));
            Assert.That(Vector3.Distance(((GameObject)Get(bullet, "body")).transform.position, action.ShotOrigin), Is.LessThan(.015f));
        }

        [Test]
        public void SniperRetreatsFromClosePlayerAndAimsAtLongRange()
        {
            var enemy = ActivateSniper();
            game.TogglePause();
            var actor = ((GameObject)Get(enemy, "body")).transform;
            actor.position = new Vector3(0, .8f, 6f);
            Invoke("UpdateEnemies", .1f);
            Assert.That(actor.position.z, Is.GreaterThan(6f), "The sniper must retreat instead of aiming at the old sniper distance.");
            Assert.That(Get(enemy, "aiming"), Is.False);
            actor.position = new Vector3(12f, .8f, 0);
            Invoke("UpdateEnemies", .01f);
            Assert.That(Get(enemy, "aiming"), Is.True, "A sniper must acquire targets beyond the old 10 m sniper range.");
            Assert.That(Get(enemy, "aimTimer"), Is.EqualTo(1.5f).Within(.001f));
        }

        [Test]
        public void DebugGodModeBlocksDamageAndNextWaveDoesNotLeaveActorsBehind()
        {
            Cheat("god");
            float before = game.Health;
            Invoke("DamagePlayer", 50f, Vector3.zero);
            Assert.That(game.Health, Is.EqualTo(before));
            Cheat("god");
            Invoke("DamagePlayer", 50f, Vector3.zero);
            Assert.That(game.Health, Is.LessThan(before));
            Cheat("nextwave");
            Assert.That(game.Wave, Is.EqualTo(2));
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Assert.That(game.AliveEnemies, Is.Zero);
            Assert.That(((Array)Get(game, "bullets")).Cast<object>().Any(b => (bool)Get(b, "active")), Is.False);
        }

        [Test]
        public void CheatSpawnsSniperFromWaveTwoAndStopsAtTwoActors()
        {
            Cheat("nextwave");
            for (int i = 0; i < 3; i++) Cheat("sniper");
            int count = ((Array)Get(game, "enemies")).Cast<object>().Count(e =>
                ((bool)Get(e, "active") || (bool)Get(e, "pending")) &&
                ((EnemyDefinition)Get(e, "definition"))?.Id == "enemy.sniper");
            Assert.That(count, Is.EqualTo(2));
        }

        private object ActivateSniper()
        {
            Assert.That(ContentCatalog.P2.TryGetEnemy("enemy.sniper", out var definition), Is.True,
                "The new sniper enemy is missing from the actual spawn catalog.");
            Assert.That(CombatGame.IsEnemyRoleEligibleForWave(definition.Role, 1), Is.False);
            Assert.That(CombatGame.IsEnemyRoleEligibleForWave(definition.Role, 2), Is.True);
            Set(game, "<Wave>k__BackingField", 2);
            Invoke("ResetWave");
            Set(game, "spawnTimer", 999f);
            var enemy = ((Array)Get(game, "enemies")).GetValue(0);
            Set(enemy, "definition", definition);
            Set(enemy, "shooter", true);
            Set(enemy, "pending", true);
            Set(enemy, "warningTimer", 0f);
            ((GameObject)Get(enemy, "body")).transform.position = new Vector3(0, .8f, 9f);
            Invoke("UpdateEnemies", 0f);
            Set(enemy, "attackTimer", 0f);
            return enemy;
        }

        // Batch tests have no visible Game View; render the same camera and canvas into a fixed-size QA image.
        private static void Capture(string name)
        {
            var camera = Camera.main;
            var canvas = GameObject.Find("CombatInterface").GetComponent<Canvas>();
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            var oldTarget = camera.targetTexture;
            var target = RenderTexture.GetTemporary(1280, 720, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = .5f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/" + name));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllBytes(output, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(texture);
            }
        }

        private void Cheat(string command)
        {
            var method = typeof(CombatGame).GetMethod("ExecuteCheat", Flags);
            Assert.That(method, Is.Not.Null, "The debug cheat entry point is missing.");
            method.Invoke(game, new object[] { command });
        }

        private void Invoke(string name, params object[] args) =>
            typeof(CombatGame).GetMethod(name, Flags).Invoke(game, args);
        private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    }
}
