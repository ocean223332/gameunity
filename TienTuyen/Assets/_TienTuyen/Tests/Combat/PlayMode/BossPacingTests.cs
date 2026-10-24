using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Content;
using TienTuyen.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TienTuyen.Combat.Tests
{
    public sealed class BossPacingTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        CombatGame game;
        object Read(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        void Write(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        void Call(string name, params object[] args) => typeof(CombatGame).GetMethod(name, Private).Invoke(game, args);
        object[] Supports() => ((Array)Read(game, "enemies")).Cast<object>().Where(e =>
            (((bool)Read(e, "active")) || ((bool)Read(e, "pending"))) &&
            ((GameObject)Read(e, "body")).transform != game.CommanderActor).ToArray();
        void Advance(float seconds)
        {
            for (float elapsed = 0; elapsed < seconds - .0001f; elapsed += .1f)
                game.StepSimulation(Mathf.Min(.1f, seconds - elapsed), Vector2.zero);
        }
        void StartBoss()
        {
            game.ExecuteCheat("boss");
            game.StepSimulation(6.1f, Vector2.zero);
            game.ExecuteCheat("god");
            Write(game, "sniperTutorialSeen", true);
        }
        [UnitySetUp] public IEnumerator Setup()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike");
            game = Object.FindFirstObjectByType<CombatGame>();
            game.enabled = false;
            game.BeginRun();
            game.SetViewInput(0, Vector3.forward * 30, false);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            var old = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Boss pacing cleanup"));
            yield return SceneManager.UnloadSceneAsync(old);
        }
        [Test] public void BossHasAtMostTwoActiveOrPendingInfantrySupports()
        {
            StartBoss();
            for (int i = 0; i < 600; i++)
            {
                game.StepSimulation(.1f, Vector2.zero);
                var supports = Supports();
                Assert.That(supports.Length, Is.LessThanOrEqualTo(2), "Pending warnings count toward the boss support cap.");
                foreach (var support in supports)
                    Assert.That(((EnemyDefinition)Read(support, "definition")).Role, Is.EqualTo(EnemyRole.Infantry));
            }
        }
        [Test] public void BossSupportStartsAfterSixSecondsAndOnlyAddsOneEveryTwelveSeconds()
        {
            StartBoss();
            Advance(5.8f);
            Assert.That(Supports(), Is.Empty, "The boss has an initial six-second window without reinforcements.");
            Advance(.4f);
            Assert.That(Supports().Length, Is.EqualTo(1));
            Advance(11.5f);
            Assert.That(Supports().Length, Is.EqualTo(1));
            Advance(.6f);
            Assert.That(Supports().Length, Is.EqualTo(2));
        }
        [Test] public void PausingBossDoesNotAccumulateReinforcements()
        {
            StartBoss();
            game.TogglePause();
            game.StepSimulation(60f, Vector2.zero);
            Assert.That(Supports(), Is.Empty);
            game.TogglePause();
            Advance(5.8f);
            Assert.That(Supports(), Is.Empty);
        }
        [Test] public void NormalWaveStillSpawnsMoreThanTwoEnemies()
        {
            Write(game, "<Wave>k__BackingField", 6);
            Call("ResetWave");
            game.ExecuteCheat("god");
            Write(game, "sniperTutorialSeen", true);
            Advance(16);
            Assert.That(game.CommanderActive, Is.False);
            Assert.That(Supports().Length, Is.GreaterThan(2));
        }
        [TestCase(1, 140f)] [TestCase(2, 189f)] [TestCase(3, 252f)]
        public void MergedLauncherAppliesTierDamageToTheActualRocket(int tier, float damage)
        {
            Assert.That(game.Run.TryClaimCommanderReward(0), Is.True);
            int launcher = game.Run.LauncherSlot;
            for (int next = 1; next < tier; next++)
            {
                int other = game.Run.Weapons.Count;
                var offer = new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, next, 0);
                var shop = new ShopSession(game.Run, 5, new ShopOffer[] { offer, null, null, null });
                Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Bought));
                Assert.That(game.Run.TryCombineWeapons(launcher, other), Is.True);
            }
            Call("ResetWave");
            Assert.That(game.SwitchWeapon(launcher), Is.True);
            Advance(.6f);
            game.SetViewInput(0, Vector3.forward * 20, true, new Vector3(0, 1.6f, 0), Vector3.forward);
            Call("FireLauncher");
            var rockets = (IList)Read(game, "rockets");
            Assert.That(rockets.Count, Is.EqualTo(1));
            Assert.That((float)Read(rockets[0], "damage"), Is.EqualTo(damage).Within(.001f));
        }
    }
}