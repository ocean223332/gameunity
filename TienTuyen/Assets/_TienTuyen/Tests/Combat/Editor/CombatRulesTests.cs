using NUnit.Framework;
using UnityEngine;

namespace TienTuyen.Combat.Tests
{
    public sealed class CombatRulesTests
    {
        [TestCase(0, 0)]
        [TestCase(199, 0)]
        [TestCase(200, 100)]
        [TestCase(660, 300)]
        [TestCase(1100, 500)]
        public void RemainingPickupsSettleOnceToWholeSupply(int remainingMinorUnits, int expected)
        {
            Assert.That(CombatRules.Settlement(remainingMinorUnits), Is.EqualTo(expected));
        }

        [Test]
        public void SettlementAggregatesFractionsBeforeRounding()
        {
            // Two 1.10-supply pickups must settle to 1 supply, not zero each.
            Assert.That(CombatRules.Settlement(110 + 110), Is.EqualTo(100));
        }

        [TestCase(1, 8)]
        [TestCase(2, 12)]
        [TestCase(5, 24)]
        public void LevelThresholdFollowsProgression(int level, int expected)
        {
            Assert.That(CombatRules.ExperienceThreshold(level), Is.EqualTo(expected));
        }

        [TestCase(5.99f, false)]
        [TestCase(6f, true)]
        [TestCase(6.01f, true)]
        public void SpawnRespectsSixUnitPlayerExclusion(float distance, bool expected)
        {
            var player = new Vector3(3f, 0f, -2f);
            Assert.That(CombatRules.IsSpawnSafe(player + Vector3.right * distance, player), Is.EqualTo(expected));
        }

        [Test]
        public void VisualHeightCannotBypassSpawnExclusionOnFlatArena()
        {
            Assert.That(CombatRules.IsSpawnSafe(new Vector3(2f, 20f, 0f), Vector3.zero), Is.False);
        }

        [Test]
        public void P2WaveAndSupplyMilestonesAreExplicit()
        {
            Assert.That(CombatRules.MaxWaves, Is.EqualTo(6));
            Assert.That(CombatRules.SupplyWave, Is.EqualTo(3));
        }
    }
}
