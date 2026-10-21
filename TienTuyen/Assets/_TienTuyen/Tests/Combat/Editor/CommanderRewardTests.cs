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
