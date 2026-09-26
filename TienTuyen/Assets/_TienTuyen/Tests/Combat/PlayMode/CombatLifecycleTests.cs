using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class CombatLifecycleTests
    {
        private CombatGame game;

        [UnitySetUp]
        public IEnumerator LoadCombatScene()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            Assert.That(game, Is.Not.Null, "CombatSpike must contain the gameplay entry point.");
            game.enabled = false;
            game.BeginRun();
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator CleanScene()
        {
            var combatScene = SceneManager.GetActiveScene();
            var empty = SceneManager.CreateScene("Combat test cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(combatScene);
        }

        [Test]
        public void RunStartsWithFullHealthAndReadyWeapon()
        {
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.WaveRemaining, Is.EqualTo(40f).Within(0.001f));
            Assert.That(game.Health, Is.EqualTo(game.MaxHealth));
            Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
            Assert.That(game.ReloadRemaining, Is.Zero);
            Assert.That(game.Currency, Is.Zero);
            Assert.That(game.Level, Is.EqualTo(1));
            Assert.That(game.Experience, Is.Zero);
            Assert.That(game.Kills, Is.Zero);
        }

        [Test]
        public void ShotgunIsAvailableAsThirdP2Weapon()
        {
            game.ReturnToMenu();
            game.SelectWeapon(WeaponKind.Shotgun);
            game.BeginRun();
            Assert.That(game.SelectedWeapon, Is.EqualTo(WeaponKind.Shotgun));
            Assert.That(game.MagazineSize, Is.EqualTo(5));
            Assert.That(game.Ammo, Is.EqualTo(5));
            Assert.That(game.ReloadDuration, Is.EqualTo(2f).Within(0.001f));
            Assert.That(game.MaxWave, Is.EqualTo(6));
        }

        [UnityTest]
        public IEnumerator ChargerSpawnsFromWaveTwoAndExposesTelegraphThenDash()
        {
            // There is no player-facing jump-to-wave command. Force the wave through
            // the private state boundary so this test avoids a 40-second RNG-dependent wait.
            SetAutoPropertyBackingField("<Wave>k__BackingField", 2);
            InvokePrivate("ResetWave");
            var fireTimers = new float[TienTuyen.Progression.ProgressionRun.MaxWeaponSlots];
            for (var i = 0; i < fireTimers.Length; i++) fireTimers[i] = 999f;
            SetPrivateField("weaponFireTimer", fireTimers);
            var sawCharger = false;
            var sawTelegraph = false;
            var sawDash = false;
            for (var frame = 0; frame < 60 * 24; frame++)
            {
                game.StepSimulation(1f / 60f, Vector2.zero);
                sawCharger |= game.ActiveChargerCount > 0 || game.PendingChargerCount > 0;
                sawTelegraph |= game.ChargerTelegraphVisible;
                sawDash |= game.ChargerDashActive;
                if (sawTelegraph && sawDash) break;
                yield return null;
            }
            Assert.That(sawCharger, Is.True, "Wave 2 must be eligible to spawn a charger.");
            Assert.That(sawTelegraph, Is.True, "Charger must show a directional telegraph before dashing.");
            Assert.That(sawDash, Is.True, "Charger must enter its distinct dash state after telegraph.");
            game.Restart();
            Assert.That(game.ActiveChargerCount, Is.Zero);
            Assert.That(game.PendingChargerCount, Is.Zero);
            Assert.That(game.ChargerTelegraphVisible, Is.False);
            Assert.That(game.ChargerDashActive, Is.False);
            Assert.That(game.AliveEnemies, Is.Zero);
        }

        [Test]
        public void DefeatedChargerPaysExperienceAndPickupRewardOnlyOnce()
        {
            SetAutoPropertyBackingField("<Wave>k__BackingField", 2);
            InvokePrivate("ResetWave");
            var fireTimers = new float[TienTuyen.Progression.ProgressionRun.MaxWeaponSlots];
            for (var i = 0; i < fireTimers.Length; i++) fireTimers[i] = 999f;
            SetPrivateField("weaponFireTimer", fireTimers);
            object charger = null;
            for (var frame = 0; frame < 60 * 12 && charger == null; frame++)
            {
                game.StepSimulation(1f / 60f, Vector2.zero);
                charger = FindActiveCharger();
            }
            Assert.That(charger, Is.Not.Null, "Wave 2 must provide an active charger for reward testing.");
            var killsBefore = game.Kills;
            var experienceBefore = game.Experience;
            var pickupsBefore = CountActivePickups();
            // DamageEnemy is intentionally private; invoking the same pooled entry twice
            // isolates the idempotency seam without depending on weapon RNG or aim timing.
            var damageEnemy = typeof(CombatGame).GetMethod("DamageEnemy", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(damageEnemy, Is.Not.Null, "Test seam changed: missing private method DamageEnemy");
            damageEnemy.Invoke(game, new[] { charger, (object)100000f });
            var killsAfterFirstHit = game.Kills;
            var experienceAfterFirstHit = game.Experience;
            var pickupsAfterFirstHit = CountActivePickups();
            damageEnemy.Invoke(game, new[] { charger, (object)100000f });
            Assert.That(killsAfterFirstHit, Is.EqualTo(killsBefore + 1));
            Assert.That(game.Kills, Is.EqualTo(killsAfterFirstHit));
            Assert.That(experienceAfterFirstHit, Is.EqualTo(experienceBefore + 1));
            Assert.That(game.Experience, Is.EqualTo(experienceAfterFirstHit));
            Assert.That(pickupsAfterFirstHit, Is.EqualTo(pickupsBefore + 1));
            Assert.That(CountActivePickups(), Is.EqualTo(pickupsAfterFirstHit));
        }

        [UnityTest]
        public IEnumerator PauseFreezesRealUpdateAndDeterministicSimulation()
        {
            Simulate(2f, Vector2.right);
            game.TogglePause();
            Assert.That(game.State, Is.EqualTo(CombatState.Paused));
            var remaining = game.WaveRemaining;
            var elapsed = game.RunElapsed;
            var health = game.Health;
            var ammo = game.Ammo;
            var reload = game.ReloadRemaining;
            var positions = game.GetComponentsInChildren<Transform>(true)
                .ToDictionary(t => t, t => t.position);

            game.enabled = true;
            for (var frame = 0; frame < 5; frame++) yield return null;
            game.enabled = false;
            Simulate(2f, Vector2.one);

            Assert.That(game.WaveRemaining, Is.EqualTo(remaining));
            Assert.That(game.RunElapsed, Is.EqualTo(elapsed));
            Assert.That(game.Health, Is.EqualTo(health));
            Assert.That(game.Ammo, Is.EqualTo(ammo));
            Assert.That(game.ReloadRemaining, Is.EqualTo(reload));
            foreach (var item in positions)
                Assert.That(item.Key.position, Is.EqualTo(item.Value), item.Key.name);

            game.TogglePause();
            Simulate(0.2f, Vector2.zero);
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Assert.That(game.WaveRemaining, Is.LessThan(remaining));
        }

        [Test]
        public void DamageInvulnerabilityPreventsStackedHits()
        {
            game.ApplyPlayerDamage(10f);
            var afterFirst = game.Health;
            game.ApplyPlayerDamage(10f);
            Assert.That(game.Health, Is.EqualTo(afterFirst));
            Simulate(0.4f, Vector2.zero);
            game.ApplyPlayerDamage(10f);
            Assert.That(game.Health, Is.EqualTo(afterFirst - 10f).Within(0.001f));
        }

        [Test]
        public void DeathStopsClockAndDoesNotPaySurvivalReward()
        {
            var currency = game.Currency;
            game.ApplyPlayerDamage(game.MaxHealth * 10f);
            var remaining = game.WaveRemaining;
            Simulate(41f, Vector2.one);
            game.ContinueWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Defeat));
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.WaveRemaining, Is.EqualTo(remaining));
            Assert.That(game.Currency, Is.EqualTo(currency));
        }

        [Test]
        public void TwentyRestartsResetRunAndDoNotGrowObjectPools()
        {
            var objectCount = game.GetComponentsInChildren<Transform>(true).Length;
            for (var run = 0; run < 20; run++)
            {
                Simulate(2f, Vector2.right);
                game.ApplyPlayerDamage(10f);
                game.TogglePause();
                game.Restart();
                Assert.That(game.State, Is.EqualTo(CombatState.Playing), $"Restart {run}");
                Assert.That(game.Health, Is.EqualTo(game.MaxHealth));
                Assert.That(game.Wave, Is.EqualTo(1));
                Assert.That(game.WaveRemaining, Is.EqualTo(40f).Within(0.001f));
                Assert.That(game.Currency, Is.Zero);
                Assert.That(game.Level, Is.EqualTo(1));
                Assert.That(game.Experience, Is.Zero);
                Assert.That(game.Kills, Is.Zero);
                Assert.That(game.AliveEnemies, Is.Zero);
                Assert.That(game.RunElapsed, Is.Zero);
                Assert.That(game.DamageDealt, Is.Zero);
                Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
                Assert.That(game.ReloadRemaining, Is.Zero);
                Assert.That(game.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objectCount));
            }
        }

        [Test]
        public void WaveCompletionConsumesUpgradeBeforeShopAndNextWave()
        {
            game.Run.AddExperience(8);
            Assert.That(game.Level, Is.EqualTo(2));
            FinishWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Upgrade));
            Assert.That(game.UpgradeChoiceCount, Is.EqualTo(3));
            game.ContinueWave();
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.ChooseUpgrade(0), Is.True);
            Assert.That(game.State, Is.EqualTo(CombatState.Shop));
            Assert.That(game.Shop, Is.Not.Null);
            Assert.That(Enumerable.Range(0, 4).Count(i => game.Shop.OfferAt(i) != null), Is.EqualTo(4));
            game.ContinueWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Assert.That(game.Wave, Is.EqualTo(2));
        }

        [Test]
        public void FullSixWaveLoopReachesVictoryThroughUpgradeAndShopStates()
        {
            // Seed one level-up so the first inter-wave transition exercises the
            // upgrade branch; subsequent waves validate the direct shop branch.
            game.Run.AddExperience(8);
            Assert.That(game.Level, Is.EqualTo(2));

            for (var completedWave = 1; completedWave <= game.MaxWave; completedWave++)
            {
                Assert.That(game.State, Is.EqualTo(CombatState.Playing));
                Assert.That(game.Wave, Is.EqualTo(completedWave));

                FinishWave();

                if (completedWave == game.MaxWave)
                {
                    Assert.That(game.State, Is.EqualTo(CombatState.Victory));
                    break;
                }

                // A wave can expose more than one level-up choice. Consume all
                // pending choices before asserting that the shop is available.
                while (game.State == CombatState.Upgrade)
                {
                    Assert.That(game.UpgradeChoiceCount, Is.EqualTo(3));
                    Assert.That(game.ChooseUpgrade(0), Is.True);
                }

                Assert.That(game.State, Is.EqualTo(CombatState.Shop));
                Assert.That(game.Shop, Is.Not.Null);
                Assert.That(Enumerable.Range(0, 4).Count(i => game.Shop.OfferAt(i) != null), Is.EqualTo(4));

                game.ContinueWave();
                Assert.That(game.State, Is.EqualTo(CombatState.Playing));
                Assert.That(game.Wave, Is.EqualTo(completedWave + 1));
            }

            Assert.That(game.State, Is.EqualTo(CombatState.Victory));
            Assert.That(game.Wave, Is.EqualTo(game.MaxWave));
        }

        [Test]
        public void ShopTransactionsRespectLocksAndWeaponSlots()
        {
            FinishWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Shop));
            game.Run.AddCurrency(10000);
            var firstOffer = game.Shop.OfferAt(0);
            Assert.That(game.SetOfferLocked(0, true), Is.True);
            Assert.That(game.RerollShop(), Is.True);
            Assert.That(game.Shop.OfferAt(0), Is.SameAs(firstOffer));
            Assert.That(game.Shop.IsLocked(0), Is.True);
            int weaponSlot = Enumerable.Range(0, 4).First(i => game.Shop.OfferAt(i) != null &&
                game.Shop.OfferAt(i).Kind == TienTuyen.Progression.OfferKind.Weapon);
            Assert.That(game.PurchaseOffer(weaponSlot), Is.EqualTo(TienTuyen.Progression.PurchaseResult.Bought));
            Assert.That(game.WeaponCount, Is.EqualTo(2));
            Assert.That(game.SellWeapon(1, out int refund), Is.True);
            Assert.That(refund, Is.GreaterThanOrEqualTo(0));
            Assert.That(game.WeaponCount, Is.EqualTo(1));
            Assert.That(game.SellWeapon(0, out _), Is.False);
        }

        private void FinishWave()
        {
            typeof(CombatGame).GetMethod("FinishWave", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(game, null);
        }

        private void Simulate(float seconds, Vector2 movement)
        {
            var frames = Mathf.RoundToInt(seconds * 60f);
            for (var frame = 0; frame < frames; frame++) game.StepSimulation(1f / 60f, movement);
        }

        private void SetPrivateField(string name, object value)
        {
            var field = typeof(CombatGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Test seam changed: missing private field " + name);
            field.SetValue(game, value);
        }

        private void SetAutoPropertyBackingField(string name, object value) => SetPrivateField(name, value);

        private void InvokePrivate(string name)
        {
            var method = typeof(CombatGame).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Test seam changed: missing private method " + name);
            method.Invoke(game, null);
        }

        private object FindActiveCharger()
        {
            var field = typeof(CombatGame).GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Test seam changed: missing private field enemies");
            var entries = field.GetValue(game) as System.Array;
            Assert.That(entries, Is.Not.Null);
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var type = entry.GetType();
                var active = (bool)type.GetField("active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                var charger = (bool)type.GetField("charger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (active && charger) return entry;
            }
            return null;
        }

        private int CountActivePickups()
        {
            var field = typeof(CombatGame).GetField("pickups", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Test seam changed: missing private field pickups");
            var entries = field.GetValue(game) as System.Array;
            Assert.That(entries, Is.Not.Null);
            var count = 0;
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var active = (bool)entry.GetType().GetField("active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (active) count++;
            }
            return count;
        }
    }
}
