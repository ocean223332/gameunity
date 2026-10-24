using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Content;
using TienTuyen.Presentation.Interface;
using TienTuyen.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class ShopRpgIntegrationTests
    {
        private CombatGame game;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike");
            game = Object.FindFirstObjectByType<CombatGame>();
            game.enabled = false;
            game.BeginRun();
            Assert.That(game.Run.TryClaimCommanderReward(0), Is.True);
            typeof(CombatGame).GetMethod("FinishWave", Private).Invoke(game, null);
            while (game.State == CombatState.Upgrade) Assert.That(game.ChooseUpgrade(0), Is.True);
            Assert.That(game.State, Is.EqualTo(CombatState.Shop));
            game.Run.AddCurrency(10000);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var scene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Shop RPG cleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private void SupplyLauncherOffers()
        {
            var row = new ShopOffer[4];
            for (int i = 0; i < row.Length; i++) row[i] = new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 1, 30);
            typeof(CombatGame).GetProperty("Shop").GetSetMethod(true).Invoke(game,
                new object[] { new ShopSession(game.Run, game.Wave, row) });
        }

        [Test]
        public void BoughtLauncherArrivesLoadedAndMergeRetainsLowerSlotAmmoAndReload()
        {
            SupplyLauncherOffers();
            int first = game.Run.LauncherSlot;
            var ammo = (int[])typeof(CombatGame).GetField("weaponAmmo", Private).GetValue(game);
            var reload = (float[])typeof(CombatGame).GetField("weaponReload", Private).GetValue(game);
            var total = (float[])typeof(CombatGame).GetField("weaponReloadTotal", Private).GetValue(game);
            ammo[first] = 0;
            reload[first] = total[first] = 1.25f;
            Assert.That(game.PurchaseOffer(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(game.AmmoAt(2), Is.EqualTo(1));
            Assert.That(game.CombineWeapons(first, 2), Is.True);
            Assert.That(game.Run.Weapons[first].Tier, Is.EqualTo(2));
            Assert.That(game.AmmoAt(first), Is.Zero, "Merging must not refill the surviving launcher.");
            Assert.That(game.ReloadAt(first), Is.EqualTo(1.25f));
            Assert.That(game.ReloadDurationAt(first), Is.EqualTo(1.25f));
        }

        [Test]
        public void RerollRefillsBoughtSlotForTwoAndFullInventoryDoesNotAutoMerge()
        {
            SupplyLauncherOffers();
            var kept = game.Shop.OfferAt(3);
            game.SetOfferLocked(3, true);
            Assert.That(game.PurchaseOffer(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(game.Shop.OfferAt(0), Is.Null);
            Assert.That(game.PurchaseOffer(1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(game.WeaponCount, Is.EqualTo(4));
            long balance = game.Run.CurrencyMinor;
            Assert.That(game.PurchaseOffer(2), Is.EqualTo(PurchaseResult.WeaponSlotsFull));
            Assert.That(game.Run.CurrencyMinor, Is.EqualTo(balance));
            Assert.That(game.RerollShop(), Is.True);
            Assert.That(game.Run.CurrencyMinor, Is.EqualTo(balance - 200));
            Assert.That(game.Shop.OfferAt(0), Is.Not.Null);
            Assert.That(game.Shop.OfferAt(3), Is.SameAs(kept));
            Assert.That(game.Shop.IsLocked(3), Is.True);
            Assert.That(game.WeaponCount, Is.EqualTo(4));
        }


        private void PrepareMergedLauncherAndGeneralBonuses()
        {
            var row = new[]
            {
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 1, 30),
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 3, 0),
                PassiveCatalog.P2.Items[5].OfferForWave(1),
                PassiveCatalog.P2.Items[4].OfferForWave(1)
            };
            typeof(CombatGame).GetProperty("Shop").GetSetMethod(true).Invoke(game,
                new object[] { new ShopSession(game.Run, game.Wave, row) });
            Assert.That(game.PurchaseOffer(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(game.CombineWeapons(1, 2), Is.True);
            Assert.That(game.Run.Weapons[1].Tier, Is.EqualTo(2));
            Assert.That(game.PurchaseOffer(1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(game.PurchaseOffer(2), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(game.PurchaseOffer(3), Is.EqualTo(PurchaseResult.Bought));
        }

        [TestCase(false, "255 → 306")]
        [TestCase(true, "340 → 408")]
        public void RocketDamageUpgradePreviewUsesRelevantLauncherTierAndGeneralDamage(bool equipSecondLauncher, string damageChange)
        {
            PrepareMergedLauncherAndGeneralBonuses();
            if (equipSecondLauncher)
            {
                game.ContinueWave();
                Assert.That(game.SwitchWeapon(2), Is.True);
                Assert.That(game.LauncherEquipped, Is.True);
            }
            // First launcher: 140 * 1.35 * 1.25 * 1.08 = 255.15 -> 306.18.
            // Equipped second: 140 * 1.8 * 1.25 * 1.08 = 340.2 -> 408.24.
            Assert.That(CombatText.UpgradePreview(game, UpgradeKind.RocketDamage), Does.Contain(damageChange));
        }

        [Test]
        public void RocketReloadUpgradePreviewIncludesGeneralReloadBonus()
        {
            PrepareMergedLauncherAndGeneralBonuses();
            // Sling reduces general reload by 10%; rocket upgrade takes 3.6 to 3.06 seconds.
            Assert.That(CombatText.UpgradePreview(game, UpgradeKind.RocketReload), Does.Contain("3.6s → 3.1s"));
        }

        [Test]
        public void LauncherOfferShowsTierAndUpgradeAdjustedDamageReloadAndRadius()
        {
            var offer = new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 2, 60);
            string summary = CombatText.OfferSummary(game, offer);
            string preview = CombatText.OfferPreview(game, offer);
            // The setup selects RocketDamage: 140 * 1.35 * 1.25 = 236.25.
            Assert.That(summary, Does.Contain("236"));
            Assert.That(summary, Does.Contain("4.0"));
            Assert.That(preview, Does.Contain("236.3"));
            Assert.That(preview, Does.Contain("4.0"));
            Assert.That(preview, Does.Contain("2.5"));
            Assert.That(preview, Does.Not.Contain("DPS"));
        }
    }
}
