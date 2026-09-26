using System;
using NUnit.Framework;
using TienTuyen.Content;
using TienTuyen.Progression;

namespace TienTuyen.Combat.Tests
{
    public sealed class ProgressionTests
    {
        private static ShopOffer Weapon(string id = "rifle", int price = 20) =>
            new ShopOffer(id, OfferKind.Weapon, 1, price);

        private static ShopOffer Passive(string id = "armor", int price = 20) =>
            new ShopOffer(id, OfferKind.Passive, 1, price, 2, new StatModifier { Armor = 10 });

        private static ShopSession Shop(ProgressionRun run) => new ShopSession(run, 1,
            new[] { Weapon(), Passive(), Weapon("smg"), Passive("boots") });

        [Test]
        public void ExactFundsBuyOnceAndConsumeOffer()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(2000);
            ShopSession shop = Shop(run);
            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(run.CurrencyMinor, Is.Zero);
            Assert.That(run.Weapons.Count, Is.EqualTo(2));
            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Empty));
            Assert.That(run.Weapons.Count, Is.EqualTo(2));
        }

        [Test]
        public void InsufficientCurrencyDoesNotConsumeOffer()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(1999);
            ShopSession shop = Shop(run);
            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.InsufficientCurrency));
            Assert.That(run.CurrencyMinor, Is.EqualTo(1999));
            Assert.That(shop.OfferAt(0), Is.Not.Null);
            Assert.That(run.Weapons.Count, Is.EqualTo(1));
        }

        [Test]
        public void FullWeaponSlotsDoNotChargeAndSellNeverRemovesLastWeapon()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(10000);
            ShopSession shop = new ShopSession(run, 1,
                new[] { Weapon("a"), Weapon("b"), Weapon("c"), Weapon("d") });
            for (int i = 0; i < 3; i++) Assert.That(shop.TryPurchase(i), Is.EqualTo(PurchaseResult.Bought));
            long balance = run.CurrencyMinor;
            Assert.That(shop.TryPurchase(3), Is.EqualTo(PurchaseResult.WeaponSlotsFull));
            Assert.That(run.CurrencyMinor, Is.EqualTo(balance));
            Assert.That(shop.OfferAt(3), Is.Not.Null);
            Assert.That(run.TrySellWeapon(0, out int refund), Is.True);
            Assert.That(refund, Is.Zero);
            Assert.That(run.TrySellWeapon(0, out _), Is.True);
            Assert.That(run.TrySellWeapon(0, out _), Is.True);
            Assert.That(run.TrySellWeapon(0, out _), Is.False);
        }

        [Test]
        public void PassiveStackCapClearsOtherOfferAndItsLock()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(6000);
            var shop = new ShopSession(run, 1,
                new[] { Passive(), Passive(), Passive(), Weapon() });
            Assert.That(shop.SetLocked(2, true), Is.True);
            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(shop.TryPurchase(1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(run.PassiveCount("armor"), Is.EqualTo(2));
            Assert.That(shop.OfferAt(2), Is.Null);
            Assert.That(shop.IsLocked(2), Is.False);
            Assert.That(shop.TryPurchase(2), Is.EqualTo(PurchaseResult.Empty));
            Assert.That(run.Stats.Armor, Is.EqualTo(20));
        }

        [Test]
        public void RerollPricesRiseAndInvalidRollDoesNotCharge()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(1500);
            ShopSession shop = Shop(run);
            ShopOffer kept = shop.OfferAt(0);
            Assert.That(shop.SetLocked(0, true), Is.True);
            Assert.That(shop.NextRerollPrice, Is.EqualTo(4));
            Assert.That(shop.TryReroll(new[] { Weapon("wrong"), Passive(), Weapon(), Passive() }), Is.False);
            Assert.That(run.CurrencyMinor, Is.EqualTo(1500));
            Assert.That(shop.TryReroll(new[] { kept, Passive("new"), Weapon("new"), Passive("other") }), Is.True);
            Assert.That(run.CurrencyMinor, Is.EqualTo(1100));
            Assert.That(shop.NextRerollPrice, Is.EqualTo(6));
            Assert.That(shop.TryReroll(new[] { kept, Passive(), Weapon(), Passive() }), Is.True);
            Assert.That(run.CurrencyMinor, Is.EqualTo(500));
            Assert.That(shop.TryReroll(new[] { kept, Passive(), Weapon(), Passive() }), Is.False);
            Assert.That(run.CurrencyMinor, Is.EqualTo(500));
        }

        [Test]
        public void FourLockedOffersDisableRerollWithoutFee()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(1000);
            ShopSession shop = Shop(run);
            var row = new ShopOffer[4];
            for (int i = 0; i < 4; i++)
            {
                row[i] = shop.OfferAt(i);
                shop.SetLocked(i, true);
            }
            Assert.That(shop.TryReroll(row), Is.False);
            Assert.That(run.CurrencyMinor, Is.EqualTo(1000));
            Assert.That(shop.Rerolls, Is.Zero);
        }

        [Test]
        public void NextShopCarriesLockedOfferAndResetsRerollPrice()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(2000);
            ShopSession first = Shop(run);
            ShopOffer kept = first.OfferAt(0);
            Assert.That(first.SetLocked(0, true), Is.True);
            Assert.That(first.TryReroll(new[] { kept, Passive("new"), Weapon("new"), Passive("other") }), Is.True);
            ShopSession next = first.CreateNextShop(2,
                new[] { Weapon("replacement"), Weapon("second"), Passive("third"), Weapon("fourth") });
            Assert.That(next.OfferAt(0), Is.SameAs(kept));
            Assert.That(next.IsLocked(0), Is.True);
            Assert.That(next.OfferAt(1).Id, Is.EqualTo("second"));
            Assert.That(next.Rerolls, Is.Zero);
            Assert.That(next.NextRerollPrice, Is.EqualTo(4));
        }

        [Test]
        public void MultipleLevelUpsResolveExactlyOnceEach()
        {
            var run = new ProgressionRun("starter");
            run.AddExperience(20); // 8 for level 2, then 12 for level 3.
            Assert.That(run.Level, Is.EqualTo(3));
            Assert.That(run.PendingUpgrades, Is.EqualTo(2));
            Assert.That(run.OpenNextUpgrade(new Random(1)), Is.True);
            Assert.That(run.CurrentChoices.Count, Is.EqualTo(3));
            Assert.That(run.OpenNextUpgrade(new Random(1)), Is.False);
            UpgradeKind first = run.CurrentChoices[0];
            Assert.That(run.TryChooseUpgrade(first), Is.True);
            Assert.That(run.TryChooseUpgrade(first), Is.False);
            Assert.That(run.PendingUpgrades, Is.EqualTo(1));
            Assert.That(run.OpenNextUpgrade(new Random(2)), Is.True);
            Assert.That(run.TryChooseUpgrade(run.CurrentChoices[1]), Is.True);
            Assert.That(run.PendingUpgrades, Is.Zero);
        }

        [Test]
        public void AggregateCapsAndIgnoresNonFiniteBonuses()
        {
            ProgressionStats stats = ProgressionRules.Aggregate(new[]
            {
                new StatModifier { Speed = 8, Armor = 500, AttackSpeed = 5, CriticalChance = 1,
                    ReloadDuration = -9, Range = -9, MaxHealth = -1000, Damage = -9,
                    Regen = -10, PickupRadius = 100, Magazine = -20, Income = -20 },
                new StatModifier { Speed = float.NaN, Armor = float.PositiveInfinity,
                    AttackSpeed = float.NegativeInfinity }
            });
            Assert.That(stats.SpeedFactor, Is.EqualTo(1.8f));
            Assert.That(stats.Armor, Is.EqualTo(100));
            Assert.That(stats.AttackSpeedBonus, Is.EqualTo(1));
            Assert.That(stats.CriticalChance, Is.EqualTo(0.6f));
            Assert.That(stats.ReloadFactor, Is.EqualTo(0.5f));
            Assert.That(stats.RangeFactor, Is.EqualTo(0.7f));
            Assert.That(stats.MaxHealth, Is.EqualTo(1));
            Assert.That(stats.DamageFactor, Is.Zero);
            Assert.That(stats.Regen, Is.Zero);
            Assert.That(stats.PickupRadius, Is.EqualTo(4));
            Assert.That(stats.IncomeFactor, Is.Zero);
            Assert.That(ProgressionRules.MagazineSize(20, stats), Is.EqualTo(1));
        }

        [Test]
        public void PriceAndRerollFollowWaveRules()
        {
            Assert.That(ProgressionRules.OfferPrice(20, 2, 5), Is.EqualTo(50));
            Assert.That(ProgressionRules.RerollPrice(5, 0), Is.EqualTo(5));
            Assert.That(ProgressionRules.RerollPrice(5, 1), Is.EqualTo(7));
        }

        [Test]
        public void ShopOfferRejectsTierOutsideTheThreeTierCatalog()
        {
            Assert.Throws<ArgumentException>(() => new ShopOffer("tier4", OfferKind.Weapon, 4, 20));
        }

        [Test]
        public void P2PassivesHaveStableIdsPricesAndModifiers()
        {
            string[] ids = { "I01", "I02", "I03", "I04", "I05", "I06" };
            Assert.That(PassiveCatalog.P2.Items.Count, Is.EqualTo(ids.Length));
            for (int i = 0; i < ids.Length; i++)
            {
                Assert.That(PassiveCatalog.P2.Items[i].Id, Is.EqualTo(ids[i]));
                Assert.That(PassiveCatalog.P2.Items[i].StackCap, Is.EqualTo(2));
                Assert.That(PassiveCatalog.P2.Items[i].BasePrice, Is.EqualTo(i == 1 ? 28 : 20));
            }
            Assert.That(PassiveCatalog.P2.Items[0].Modifier.MaxHealth, Is.EqualTo(15));
            Assert.That(PassiveCatalog.P2.Items[1].Modifier.Regen, Is.EqualTo(0.3f));
            Assert.That(PassiveCatalog.P2.Items[2].Modifier.Armor, Is.EqualTo(10));
            Assert.That(PassiveCatalog.P2.Items[2].Modifier.Speed, Is.EqualTo(-0.03f));
            Assert.That(PassiveCatalog.P2.Items[3].Modifier.Speed, Is.EqualTo(0.08f));
            Assert.That(PassiveCatalog.P2.Items[4].Modifier.ReloadDuration, Is.EqualTo(-0.1f));
            Assert.That(PassiveCatalog.P2.Items[5].Modifier.Damage, Is.EqualTo(0.08f));
            Assert.That(PassiveCatalog.P2.Items[1].OfferForWave(5).Price, Is.EqualTo(35));
        }

        [Test]
        public void GeneratorPreservesLocksAndFillsMissingCategories()
        {
            var run = new ProgressionRun(ContentIds.Rifle);
            var generator = new ShopOfferGenerator();
            ShopOffer[] first = generator.RollOffers(run, 1, new Random(1));
            Assert.That(Array.Exists(first, o => o.Kind == OfferKind.Weapon), Is.True);
            Assert.That(Array.Exists(first, o => o.Kind == OfferKind.Passive), Is.True);
            Assert.That(Array.TrueForAll(first, o => o.Kind != OfferKind.Weapon || o.Tier == 1), Is.True);
            var shop = new ShopSession(run, 1, first);
            Assert.That(shop.SetLocked(0, true), Is.True);
            ShopOffer[] next = generator.RollOffers(run, 2, new Random(2), shop);
            Assert.That(next[0], Is.SameAs(first[0]));
            Assert.That(Array.Exists(next, o => o.Kind == OfferKind.Weapon), Is.True);
            Assert.That(Array.Exists(next, o => o.Kind == OfferKind.Passive), Is.True);
        }

        [Test]
        public void EnemyAndPickupRewardsAreClaimedOnlyOnce()
        {
            var run = new ProgressionRun("starter");
            Assert.That(run.TryRewardEnemy(10, 1, 2, out long pickup), Is.True);
            Assert.That(pickup, Is.EqualTo(200));
            Assert.That(run.Experience, Is.EqualTo(1));
            Assert.That(run.TryRewardEnemy(10, 1, 2, out _), Is.False);
            Assert.That(run.Experience, Is.EqualTo(1));
            Assert.That(run.TryCollectPickup(20, pickup), Is.True);
            Assert.That(run.TryCollectPickup(20, pickup), Is.False);
            Assert.That(run.CurrencyMinor, Is.EqualTo(200));
        }

        [Test]
        public void WaveSettlementRoundsRemainingValueOnceAndNeverRepeats()
        {
            var run = new ProgressionRun("starter");
            Assert.That(run.TryResolveWave(1, 599, out long award), Is.True);
            Assert.That(award, Is.EqualTo(1400)); // floor(5.99 / 2) + 12 survival
            Assert.That(run.TryResolveWave(1, 599, out _), Is.False);
            Assert.That(run.CurrencyMinor, Is.EqualTo(1400));
        }

        [Test]
        public void PreviewAndFailedPurchaseDoNotMutateRun()
        {
            var run = new ProgressionRun("starter");
            ProgressionStats preview = run.PreviewStats(new StatModifier { MaxHealth = 15, Speed = -0.03f });
            Assert.That(preview.MaxHealth, Is.EqualTo(115));
            Assert.That(run.Stats.MaxHealth, Is.EqualTo(100));
            var shop = new ShopSession(run, 1, new[]
            {
                PassiveCatalog.P2.Items[0].OfferForWave(1), Weapon(), Weapon("smg"), Weapon("shotgun")
            });
            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.InsufficientCurrency));
            Assert.That(run.PassiveCount(PassiveCatalog.MedKit), Is.Zero);
            Assert.That(run.CurrencyMinor, Is.Zero);
            Assert.That(shop.OfferAt(0), Is.Not.Null);
        }

        [Test]
        public void CombiningWeaponsPreservesPaidValueAndResaleRoundsDownOnce()
        {
            var run = new ProgressionRun("starter");
            run.AddCurrency(4100);
            var shop = new ShopSession(run, 1,
                new[] { Weapon("rifle", 20), Weapon("rifle", 21), Passive(), Passive("boots") });

            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(shop.TryPurchase(1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(run.CurrencyMinor, Is.Zero);
            Assert.That(run.TryCombineWeapons(1, 2), Is.True);
            Assert.That(run.Weapons.Count, Is.EqualTo(2));
            Assert.That(run.Weapons[1].Tier, Is.EqualTo(2));
            Assert.That(run.Weapons[1].PaidPrice, Is.EqualTo(41));
            Assert.That(run.TrySellWeapon(1, out int refund), Is.True);
            Assert.That(refund, Is.EqualTo(20));
            Assert.That(run.CurrencyMinor, Is.EqualTo(2000));
            Assert.That(run.TrySellWeapon(0, out _), Is.False);
        }

        [Test]
        public void FailedCombineLeavesWeaponSlotsAndCurrencyUntouched()
        {
            var run = new ProgressionRun("rifle");
            run.AddCurrency(4000);
            var shop = new ShopSession(run, 1,
                new[] { Weapon("rifle"), Weapon("smg"), Passive(), Passive("boots") });
            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(shop.TryPurchase(1), Is.EqualTo(PurchaseResult.Bought));
            long balance = run.CurrencyMinor;

            Assert.That(run.TryCombineWeapons(0, 1), Is.True);
            Assert.That(run.TryCombineWeapons(0, 1), Is.False);
            Assert.That(run.TryCombineWeapons(0, 0), Is.False);
            Assert.That(run.TryCombineWeapons(-1, 1), Is.False);
            Assert.That(run.Weapons.Count, Is.EqualTo(2));
            Assert.That(run.Weapons[0].Tier, Is.EqualTo(2));
            Assert.That(run.Weapons[1].Id, Is.EqualTo("smg"));
            Assert.That(run.CurrencyMinor, Is.EqualTo(balance));
        }
    }
}
