using NUnit.Framework;

namespace TienTuyen.Duel.Tests
{
    public sealed class DuelMatchTests
    {
        [Test]
        public void BestOfFiveEndsAtThreeWinsAndNotBefore()
        {
            var match = new DuelMatch(0, 10, 0);
            Assert.That(match.ConsumeRespawnRequest(), Is.True);
            Assert.That(match.ConsumeRespawnRequest(), Is.False);
            Live(match);
            match.ReportDeath(1);
            match.ReportDeath(1); // A repeated death notification cannot award a second point.
            Assert.That(match.Score0, Is.EqualTo(1));
            Assert.That(match.Phase, Is.EqualTo(DuelPhase.RoundOver));
            Live(match);
            Assert.That(match.RoundNumber, Is.EqualTo(2));
            Assert.That(match.ConsumeRespawnRequest(), Is.True);
            match.ReportDeath(0);
            Assert.That(match.MatchWinnerSlot, Is.EqualTo(-1));
            Live(match);
            Assert.That(match.RoundNumber, Is.EqualTo(3));
            match.ReportDeath(1);
            Assert.That(match.Score0, Is.EqualTo(2));
            Assert.That(match.Phase, Is.EqualTo(DuelPhase.RoundOver));
            Live(match);
            match.ReportDeath(0);
            Assert.That(match.Phase, Is.EqualTo(DuelPhase.RoundOver));
            Live(match);
            match.ReportDeath(1);
            Assert.That(match.CompletedRounds, Is.EqualTo(5));
            Assert.That(match.MatchEndReason, Is.EqualTo(DuelMatchEndReason.ScoreLimit));
            Assert.That(match.Phase, Is.EqualTo(DuelPhase.MatchOver));
            Assert.That(match.MatchWinnerSlot, Is.EqualTo(0));
            Assert.That(match.Score0, Is.EqualTo(3));
            Assert.That(match.Score1, Is.EqualTo(2));
        }

        [Test]
        public void TimeoutAwardsThePlayerWithMoreRemainingHealth()
        {
            var match = new DuelMatch(0, 1, 0);
            Live(match);
            match.Tick(1, 50, 100);
            Assert.That(match.RoundWinnerSlot, Is.EqualTo(1));
            Assert.That(match.Score1, Is.EqualTo(1));
            Assert.That(match.RoundEndReason, Is.EqualTo(DuelRoundEndReason.Timeout));
        }

        [Test]
        public void EqualHealthTimeoutsReachFiveRoundCapAndDeclareDraw()
        {
            var match = new DuelMatch(0, 1, 0);
            for (int round = 1; round <= DuelMatch.MaxRounds; round++)
            {
                Live(match);
                Assert.That(match.RoundNumber, Is.EqualTo(round));
                match.Tick(1, DuelPlayerSim.MaxHealth, DuelPlayerSim.MaxHealth);
                Assert.That(match.Score0 + match.Score1, Is.Zero);
                Assert.That(match.Phase == DuelPhase.MatchOver, Is.EqualTo(round == DuelMatch.MaxRounds));
            }
            Assert.That(match.IsDraw, Is.True);
            Assert.That(match.MatchWinnerSlot, Is.EqualTo(-1));
        }

        [Test]
        public void RoundCapPrefersScoreOverCumulativeDamage()
        {
            var match = new DuelMatch(0, 1, 0);
            Live(match);
            match.ReportDamage(0, 100);
            match.ReportDeath(1);
            for (int round = 2; round <= DuelMatch.MaxRounds; round++)
            {
                Live(match);
                match.ReportDamage(1, 75);
                match.ReportDamage(0, 25);
                match.ReportDeaths(true, true);
            }
            Assert.That(match.DamageDealt1, Is.GreaterThan(match.DamageDealt0));
            Assert.That(match.MatchWinnerSlot, Is.EqualTo(0));
        }

        [Test]
        public void RoundCapUsesCumulativeDamageWhenScoresTie()
        {
            var match = new DuelMatch(0, 1, 0);
            for (int round = 1; round <= DuelMatch.MaxRounds; round++)
            {
                Live(match);
                match.ReportDamage(0, 10);
                match.ReportDamage(1, 20);
                if (round < DuelMatch.MaxRounds) match.ReportDeath(round % 2);
                else match.ReportDeaths(true, true);
            }
            Assert.That(match.Score0, Is.EqualTo(2));
            Assert.That(match.Score1, Is.EqualTo(2));
            Assert.That(match.MatchEndReason, Is.EqualTo(DuelMatchEndReason.RoundLimit));
            Assert.That(match.MatchWinnerSlot, Is.EqualTo(1));
            Assert.That(match.IsDraw, Is.False);
        }

        [Test]
        public void SimultaneousDeathsAwardNeitherSlotAndRequireRespawn()
        {
            var match = new DuelMatch(0, 1, 0);
            Live(match);
            match.ConsumeRespawnRequest();
            match.ReportDeaths(true, true);
            Assert.That(match.RoundWinnerSlot, Is.EqualTo(-1));
            Assert.That(match.RoundEndReason, Is.EqualTo(DuelRoundEndReason.DoubleKnockout));
            Assert.That(match.Score0 + match.Score1, Is.Zero);
            Assert.That(match.CanAct, Is.False);
            match.Tick(0, 0, 0);
            Assert.That(match.ConsumeRespawnRequest(), Is.True);
            Assert.That(match.Phase, Is.EqualTo(DuelPhase.Warmup));
        }

        private static void Live(DuelMatch match)
        {
            if (match.Phase == DuelPhase.RoundOver) match.Tick(0, DuelPlayerSim.MaxHealth, DuelPlayerSim.MaxHealth);
            match.Tick(0, DuelPlayerSim.MaxHealth, DuelPlayerSim.MaxHealth);
            Assert.That(match.CanAct, Is.True);
        }
    }
}
