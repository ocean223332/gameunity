using System;
using NUnit.Framework;
using TienTuyen.Waves;

namespace TienTuyen.Combat.Tests
{
    public sealed class SupplyEventTests
    {
        [Test]
        public void SpawnsOnlyAtConfiguredWaveAndSecondWhileAlive()
        {
            var supply = new SupplyEvent(wave: 4);

            Assert.That(supply.IsSpawnEligible(3, 20f, true, false), Is.False);
            Assert.That(supply.IsSpawnEligible(4, 14.99f, true, false), Is.False);
            Assert.That(supply.IsSpawnEligible(4, 15f, false, false), Is.False);
            Assert.That(supply.IsSpawnEligible(4, 15f, true, true), Is.False);
            Assert.That(supply.IsSpawnEligible(4, 15f, true, false), Is.True);

            supply.Advance(4, 15f, 0f, false, true, false);
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Active));
            Assert.That(supply.IsSpawnEligible(4, 16f, true, false), Is.False);
        }

        [Test]
        public void ProgressStartsAtSpawnSecondAndClampsAtRequirement()
        {
            var supply = new SupplyEvent();

            supply.Advance(3, 15.5f, 1f, true, true, false);
            Assert.That(supply.ProgressSeconds, Is.EqualTo(0.5f).Within(0.0001f));
            supply.Advance(3, 15.5f, 1f, true, true, false);
            Assert.That(supply.ProgressSeconds, Is.EqualTo(0.5f).Within(0.0001f));
            supply.Advance(3, 17f, 1.5f, false, true, false);
            Assert.That(supply.ProgressSeconds, Is.EqualTo(0.5f).Within(0.0001f));
            supply.Advance(3, 18f, 1f, true, true, false, paused: true);
            Assert.That(supply.ProgressSeconds, Is.EqualTo(0.5f).Within(0.0001f));
            supply.Advance(3, 22f, 4f, true, true, false);

            Assert.That(supply.ProgressSeconds, Is.EqualTo(3f));
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Completed));
            supply.Advance(3, 25f, 3f, true, true, false);
            Assert.That(supply.ProgressSeconds, Is.EqualTo(3f));
        }

        [Test]
        public void IgnoredSupplyDoesNotBlockWaveCompletion()
        {
            var supply = new SupplyEvent();
            supply.Advance(3, 40f, 1f, false, true, true);

            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Skipped));
            Assert.That(supply.TryClaim(true, out _), Is.False);
            supply.Advance(4, 0f, 0f, true, true, false);
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Skipped));
        }

        [Test]
        public void ExplicitSkipCannotBeReversed()
        {
            var supply = new SupplyEvent();
            supply.Advance(3, 15f, 0f, false, true, false);
            supply.Skip();
            supply.Advance(3, 20f, 5f, true, true, false);
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Skipped));
            Assert.That(supply.ProgressSeconds, Is.Zero);
        }

        [Test]
        public void ClaimSucceedsOnceWithExactDefaultReward()
        {
            var supply = CompletedSupply();

            Assert.That(supply.TryClaim(true, out var reward), Is.True);
            Assert.That(reward.CurrencyMinorUnits, Is.EqualTo(2000));
            Assert.That(reward.Experience, Is.Zero);
            Assert.That(reward.Heal, Is.EqualTo(10));
            Assert.That(reward.HealthAfterHealing(95, 100), Is.EqualTo(100));
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Claimed));
            Assert.That(supply.TryClaim(true, out var duplicate), Is.False);
            Assert.That(duplicate.CurrencyMinorUnits, Is.Zero);
        }

        [Test]
        public void IncompleteSupplyCannotBeClaimed()
        {
            var supply = new SupplyEvent();
            supply.Advance(3, 16f, 1f, true, true, false);

            Assert.That(supply.TryClaim(true, out _), Is.False);
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Active));
        }

        [Test]
        public void DeathBeforeClaimForfeitsEvenCompletedSupply()
        {
            var supply = CompletedSupply();

            Assert.That(supply.TryClaim(false, out _), Is.False);
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Forfeited));
            Assert.That(supply.TryClaim(true, out _), Is.False);
        }

        [Test]
        public void DeathWinsWhenRecoveryAndWaveEndCoincide()
        {
            var supply = new SupplyEvent();
            supply.Advance(3, 39f, 2f, true, true, false);
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Active));

            supply.Advance(3, 40f, 1f, true, false, true);

            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Forfeited));
            Assert.That(supply.TryClaim(false, out _), Is.False);
        }

        [Test]
        public void RecoveryAtWaveTimeoutRemainsClaimable()
        {
            var onTimeout = new SupplyEvent();
            onTimeout.Advance(3, 38f, 1f, true, true, false);
            onTimeout.Advance(3, 40f, 2f, true, true, true);

            Assert.That(onTimeout.Phase, Is.EqualTo(SupplyEventPhase.Completed));
            Assert.That(onTimeout.TryClaim(true, out _), Is.True);
        }

        [Test]
        public void UnclaimedSupplyExpiresWhenNextWaveStarts()
        {
            var supply = CompletedSupply();
            supply.Advance(4, 0f, 0f, false, true, false);

            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Skipped));
            Assert.That(supply.TryClaim(true, out _), Is.False);
        }

        [Test]
        public void ConfiguredRewardIsReturnedWithoutDoubleClaim()
        {
            var supply = new SupplyEvent(currencyMinorUnits: 750, experience: 4, heal: 6);
            supply.Advance(3, 18f, 3f, true, true, false);

            Assert.That(supply.TryClaim(true, out var reward), Is.True);
            Assert.That(reward.CurrencyMinorUnits, Is.EqualTo(750));
            Assert.That(reward.Experience, Is.EqualTo(4));
            Assert.That(reward.Heal, Is.EqualTo(6));
            Assert.That(reward.HealthAfterHealing(50, 100), Is.EqualTo(56));
            Assert.That(supply.TryClaim(true, out _), Is.False);
        }

        [Test]
        public void RejectsInvalidTimingAndRewardInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SupplyEvent(wave: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SupplyEvent(spawnSecond: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SupplyEvent(recoverySeconds: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SupplyEvent(currencyMinorUnits: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SupplyEvent(experience: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SupplyEvent(heal: -1));

            var supply = new SupplyEvent();
            Assert.Throws<ArgumentOutOfRangeException>(() => supply.IsSpawnEligible(0, 16f, true, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => supply.Advance(3, float.PositiveInfinity, 0f, true, true, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => supply.Advance(0, 16f, 1f, true, true, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => supply.Advance(3, 16f, -1f, true, true, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => supply.Advance(3, 1f, 2f, true, true, false));
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Pending));
        }

        private static SupplyEvent CompletedSupply()
        {
            var supply = new SupplyEvent();
            supply.Advance(3, 18f, 3f, true, true, false);
            Assert.That(supply.Phase, Is.EqualTo(SupplyEventPhase.Completed));
            return supply;
        }
    }
}
