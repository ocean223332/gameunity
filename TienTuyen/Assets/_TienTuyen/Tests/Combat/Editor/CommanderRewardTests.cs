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
    }
}
