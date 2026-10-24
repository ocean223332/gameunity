using System;
using System.Linq;
using NUnit.Framework;
using TienTuyen.Content;
using TienTuyen.Progression;
namespace TienTuyen.Combat.Tests
{
    public sealed class CommanderRewardTests
    {
        bool Claim(ProgressionRun run,int slot=0)
        {
            var method=typeof(ProgressionRun).GetMethod("TryClaimCommanderReward");
            Assert.That(method,Is.Not.Null,"Commander reward API must exist.");
            return (bool)method.Invoke(run,new object[]{slot});
        }
        [Test] public void DefeatingCommanderAwardsOneLauncherBonusAndUpgradeOnce()
        {
            var run=new ProgressionRun(ContentIds.Rifle);
            Assert.That(Claim(run),Is.True);
            Assert.That(run.Weapons.Count(w=>w.Id=="weapon.rpg18"),Is.EqualTo(1));
            Assert.That(run.CurrencyMinor,Is.EqualTo(2000));
            Assert.That(run.PendingUpgrades,Is.EqualTo(1));
            Assert.That(Claim(run),Is.False);
            Assert.That(run.CurrencyMinor,Is.EqualTo(2000));
        }
        [Test] public void ShopCannotOfferLauncherBeforeItIsEarned()
        {
            var run=new ProgressionRun(ContentIds.Rifle);var rng=new Random(17);
            for(int i=0;i<100;i++)
                Assert.That(new ShopOfferGenerator().RollOffers(run,5,rng).Any(o=>o.Id=="weapon.rpg18"),Is.False);
        }

        [Test]
        public void EarnedLauncherAppearsAtNormalTierPricesAndStaysUnlockedAfterSale()
        {
            var run = new ProgressionRun(ContentIds.Rifle);
            Claim(run);
            Assert.That(run.TrySellWeapon(run.LauncherSlot, out _), Is.True);
            var rng = new Random(42);
            bool tierOne = false, tierTwo = false;
            for (int i = 0; i < 200; i++)
                foreach (var offer in new ShopOfferGenerator().RollOffers(run, 5, rng))
                    if (offer.Id == ContentIds.Rpg18)
                    {
                        Assert.That(offer.Tier, Is.InRange(1, 2));
                        Assert.That(offer.Price, Is.EqualTo(offer.Tier == 1 ? 30 : 60));
                        tierOne |= offer.Tier == 1;
                        tierTwo |= offer.Tier == 2;
                    }
            Assert.That(tierOne && tierTwo, Is.True, "Both RPG tiers must be eligible after the reward, including after selling it.");
            var freshRun = new ProgressionRun(ContentIds.Rifle);
            for (int i = 0; i < 100; i++)
                Assert.That(new ShopOfferGenerator().RollOffers(freshRun, 12, rng).Any(o => o.Id == ContentIds.Rpg18), Is.False);
        }

        [Test]
        public void BoughtLaunchersMergeThroughTierThreeAndPreservePaidValue()
        {
            var run = new ProgressionRun(ContentIds.Rifle);
            Claim(run);
            run.AddCurrency(12000);
            var shop = new ShopSession(run, 5, new[]
            {
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 1, 30),
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 2, 60),
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 3, 120),
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 3, 120)
            });
            Assert.That(shop.TryPurchase(0), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(run.TryCombineWeapons(1, 2), Is.True);
            Assert.That(run.Weapons[1].Tier, Is.EqualTo(2));
            Assert.That(shop.TryPurchase(1), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(run.TryCombineWeapons(1, 2), Is.True);
            Assert.That(run.Weapons[1].Tier, Is.EqualTo(3));
            Assert.That(run.Weapons[1].PaidPrice, Is.EqualTo(90));
            run.AddCurrency(24000);
            Assert.That(shop.TryPurchase(2), Is.EqualTo(PurchaseResult.Bought));
            Assert.That(shop.TryPurchase(3), Is.EqualTo(PurchaseResult.Bought));
            long balance = run.CurrencyMinor;
            Assert.That(run.TryCombineWeapons(1, 2), Is.False);
            Assert.That(run.Weapons.Count, Is.EqualTo(4));
            Assert.That(run.CurrencyMinor, Is.EqualTo(balance));
        }

        [Test] public void BossRewardOffersThreeWeaponSpecificUpgrades()
        {
            var run=new ProgressionRun(ContentIds.Rifle);Claim(run);
            Assert.That(run.OpenNextUpgrade(new Random(1)),Is.True);
            Assert.That(run.CurrentChoices.Select(c=>c.ToString()),Is.EquivalentTo(new[]{"RocketDamage","RocketReload","RocketRadius"}));
            Assert.That(run.TryChooseUpgrade(run.CurrentChoices[0]),Is.True);
            Assert.That(run.PendingUpgrades,Is.Zero);
        }
        [Test]public void FullInventoryReplacesSelectedGunWithoutAddingFifthSlot()
        {
            var run=new ProgressionRun(ContentIds.Rifle);
            var purchase=typeof(ProgressionRun).GetMethod("Purchase",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            for(int i=0;i<3;i++)purchase.Invoke(run,new object[]{new ShopOffer(ContentIds.Smg,OfferKind.Weapon,1,0)});
            Assert.That(run.Weapons.Count,Is.EqualTo(4));Assert.That(Claim(run,2),Is.True);
            Assert.That(run.Weapons.Count,Is.EqualTo(4));Assert.That(run.Weapons[2].Id,Is.EqualTo(ContentIds.Rpg18));
            Assert.That(run.Weapons[0].Id,Is.EqualTo(ContentIds.Rifle));
        }
        [Test]public void LauncherUpgradeLevelsStopAtThreeAndDoNotAffectOtherGuns()
        {
            var run=new ProgressionRun(ContentIds.Rifle);Claim(run);
            var rng=new Random(13);
            for(int i=0;i<100;i++)
            {
                if(i>0)run.AddExperience(100);
                if(!run.OpenNextUpgrade(rng))continue;
                var choices=run.CurrentChoices.ToArray();
                foreach(var choice in choices.Where(c=>c>=UpgradeKind.RocketDamage))Assert.That(run.RocketUpgradeLevel(choice),Is.LessThan(3));
                var selected=choices.FirstOrDefault(c=>c>=UpgradeKind.RocketDamage);
                if(!choices.Contains(selected) || selected<UpgradeKind.RocketDamage)selected=choices[0];
                Assert.That(run.TryChooseUpgrade(selected),Is.True);
            }
            Assert.That(run.RocketUpgradeLevel(UpgradeKind.RocketDamage),Is.EqualTo(3));
            Assert.That(run.RocketUpgradeLevel(UpgradeKind.RocketReload),Is.EqualTo(3));
            Assert.That(run.RocketUpgradeLevel(UpgradeKind.RocketRadius),Is.EqualTo(3));
        }    }
}
