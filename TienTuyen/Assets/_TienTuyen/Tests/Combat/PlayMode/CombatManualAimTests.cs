using System;
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
    /// <summary>Third-person manual aim: trigger, crosshair ray, misses, reload and view-relative input.</summary>
    public sealed class CombatManualAimTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private CombatGame game;
        private Transform player;

        [UnitySetUp]
        public IEnumerator LoadCombatScene()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            Assert.That(game, Is.Not.Null);
            game.enabled = false;
            game.BeginRun();
            SetField(game, "spawnTimer", 999f);
            player = game.transform.Find("Player");
        }

        [UnityTearDown]
        public IEnumerator CleanScene()
        {
            var combatScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Manual aim test cleanup"));
            yield return SceneManager.UnloadSceneAsync(combatScene);
        }

        [Test]
        public void TriggerFiresOnlyWhileHeldAndHitsTheEnemyUnderTheCrosshair()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0f));
            int ammo = game.Ammo;
            game.SetViewInput(90f, enemy.transform.position, false);
            Invoke("FireWeapon");
            Assert.That(game.ManualAim, Is.True);
            Assert.That(game.AimOnTarget, Is.True);
            Assert.That(game.Ammo, Is.EqualTo(ammo), "Manual aim must not fire without the trigger.");

            bool? impactHit = null;
            game.ShotImpact += (point, hit) => impactHit = hit;
            float hp = (float)GetField(enemy.entry, "hp");
            game.SetViewInput(90f, enemy.transform.position, true);
            Invoke("FireWeapon");
            Assert.That(game.Ammo, Is.EqualTo(ammo - 1));
            Assert.That(impactHit, Is.True);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.LessThan(hp));
            Assert.That(Vector3.Dot(game.PlayerAimDirection, Vector3.right), Is.GreaterThan(.99f));
        }

        [Test]
        public void MissesSpendAmmunitionAndLandOnTheAimRay()
        {
            var enemy = CreateTarget(new Vector3(4f, .8f, 0f));
            float hp = (float)GetField(enemy.entry, "hp");
            int ammo = game.Ammo;
            Vector3 impact = Vector3.zero;
            bool hit = true;
            game.ShotImpact += (point, struck) => { impact = point; hit = struck; };
            // Aim at open ground to the north, away from the enemy in the east.
            game.SetViewInput(0f, player.position + new Vector3(0, 0, 6f), true);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.False);
            Assert.That(game.Ammo, Is.EqualTo(ammo - 1));
            Assert.That(hit, Is.False);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.EqualTo(hp));
            Assert.That(Mathf.Abs(impact.x - player.position.x), Is.LessThan(.01f));
            Assert.That(impact.z, Is.GreaterThan(player.position.z + 1f));
        }

        [Test]
        public void CoverStopsTheAimRayBeforeAHiddenEnemy()
        {
            // The first cover box is centred at (-8, -3.4); put the soldier east of it and the enemy west.
            player.position = new Vector3(-5f, .8f, -3.4f);
            var enemy = CreateTarget(new Vector3(-11f, .8f, -3.4f));
            float hp = (float)GetField(enemy.entry, "hp");
            game.SetViewInput(-90f, enemy.transform.position, true);
            Invoke("FireWeapon");
            Assert.That(game.AimOnTarget, Is.False);
            Assert.That((float)GetField(enemy.entry, "hp"), Is.EqualTo(hp));
        }

        [Test]
        public void ReloadKeyRefillsAPartlyUsedMagazine()
        {
            CreateTarget(new Vector3(4f, .8f, 0f));
            game.SetViewInput(90f, new Vector3(4f, .8f, 0f), true);
            Invoke("FireWeapon");
            Assert.That(game.Ammo, Is.LessThan(game.MagazineSize));
            game.RequestReload();
            Assert.That(game.ReloadRemaining, Is.GreaterThan(0f));
            game.SetViewInput(90f, new Vector3(4f, .8f, 0f), false);
            for (int frame = 0; frame < 60 * 3; frame++) game.StepSimulation(1f / 60f, Vector2.zero);
            Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
        }

        [Test]
        public void MovementInputTurnsWithTheCamera()
        {
            var rotate = typeof(CombatGame).GetMethod("RotatePlanar", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(rotate, Is.Not.Null);
            var forwardEast = (Vector2)rotate.Invoke(null, new object[] { Vector2.up, 90f });
            var rightSouth = (Vector2)rotate.Invoke(null, new object[] { Vector2.right, 90f });
            var forwardNorth = (Vector2)rotate.Invoke(null, new object[] { Vector2.up, 0f });
            Assert.That(Vector2.Distance(forwardEast, Vector2.right), Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(rightSouth, Vector2.down), Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(forwardNorth, Vector2.up), Is.LessThan(.0001f));
        }

        [UnityTest]
        public IEnumerator PresentationSwitchesToAPerspectiveShoulderCamera()
        {
            for (int frame = 0; frame < 20 && game.transform.Find("Player/HeroVisual") == null; frame++)
                yield return null;
            yield return null;
            var view = Camera.main;
            Assert.That(view, Is.Not.Null);
            Assert.That(view.orthographic, Is.False);
            Assert.That(view.GetComponent<CombatThirdPersonCamera>(), Is.Not.Null);
            Assert.That(game.GetComponent<CombatEnvironment>(), Is.Not.Null);
        }

        private (object entry, Transform transform) CreateTarget(Vector3 position)
        {
            SetField(game, "spawnTimer", 0f);
            Invoke("UpdateSpawn", 0f);
            SetField(game, "spawnTimer", 999f);
            var enemies = (Array)GetField(game, "enemies");
            var enemy = enemies.GetValue(0);
            Assert.That(GetField(enemy, "definition"), Is.Not.Null);
            SetField(enemy, "active", true);
            SetField(enemy, "pending", false);
            SetField(enemy, "hp", 100000f);
            var body = (GameObject)GetField(enemy, "body");
            body.SetActive(true);
            body.transform.position = position;
            ((GameObject)GetField(enemy, "warning")).SetActive(false);
            ((float[])GetField(game, "weaponFireTimer"))[0] = 0f;
            return (enemy, body.transform);
        }

        private void Invoke(string name, params object[] arguments)
        {
            var method = typeof(CombatGame).GetMethod(name, PrivateInstance);
            Assert.That(method, Is.Not.Null, "Missing test seam: " + name);
            method.Invoke(game, arguments);
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing test seam: " + name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing test seam: " + name);
            field.SetValue(target, value);
        }
    }
}
