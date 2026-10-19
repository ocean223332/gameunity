using System;

namespace TienTuyen.Duel
{
    public enum DuelPhase { Warmup, Live, RoundOver, MatchOver }
    public enum DuelRoundEndReason { None, Elimination, Timeout, DoubleKnockout }
    public enum DuelMatchEndReason { None, ScoreLimit, RoundLimit, Forfeit }

    /// <summary>
    /// First to three wins. At timeout higher remaining health wins; equal health (or a double
    /// knockout) awards no point. After five rounds, score then cumulative damage decides the
    /// match, with exact ties declared draws. Report actual applied damage and simultaneous deaths.
    /// </summary>
    public sealed class DuelMatch
    {
        public const float DefaultWarmupSeconds = 3f;
        public const float DefaultRoundSeconds = 90f;
        public const float DefaultRoundOverSeconds = 3f;
        public const int MaxRounds = 5;
        public const int RoundsToWin = 3;

        private readonly float warmupSeconds, roundSeconds, roundOverSeconds;
        private bool respawnRequested;

        public DuelPhase Phase { get; private set; }
        public int RoundNumber { get; private set; } = 1;
        public int RoundGeneration { get; private set; }
        public int CompletedRounds { get; private set; }
        public int Score0 { get; private set; }
        public int Score1 { get; private set; }
        public int RoundWinnerSlot { get; private set; } = -1;
        public int MatchWinnerSlot { get; private set; } = -1;
        public DuelRoundEndReason RoundEndReason { get; private set; }
        public DuelMatchEndReason MatchEndReason { get; private set; }
        public float PhaseRemaining { get; private set; }
        public float RoundRemaining { get; private set; }
        public float DamageDealt0 { get; private set; }
        public float DamageDealt1 { get; private set; }
        public bool CanAct => Phase == DuelPhase.Live;
        public bool IsDraw => Phase == DuelPhase.MatchOver && MatchWinnerSlot < 0;

        public DuelMatch(float warmupSeconds = DefaultWarmupSeconds,
            float roundSeconds = DefaultRoundSeconds, float roundOverSeconds = DefaultRoundOverSeconds)
        {
            ValidateDuration(warmupSeconds, nameof(warmupSeconds), true);
            ValidateDuration(roundSeconds, nameof(roundSeconds), false);
            ValidateDuration(roundOverSeconds, nameof(roundOverSeconds), true);
            this.warmupSeconds = warmupSeconds;
            this.roundSeconds = roundSeconds;
            this.roundOverSeconds = roundOverSeconds;
            BeginRound();
        }

        /// <summary>Initially true, and true once per new warmup. Reset both sims to Spawn(slot).</summary>
        public bool ConsumeRespawnRequest()
        {
            bool result = respawnRequested;
            respawnRequested = false;
            return result;
        }

        public int Score(int slot) { ValidateSlot(slot); return slot == 0 ? Score0 : Score1; }

        /// <summary>Report health actually removed, excluding overkill. Damage is retained across rounds.</summary>
        public void ReportDamage(int attackerSlot, float damage)
        {
            ValidateSlot(attackerSlot);
            ValidateDuration(damage, nameof(damage), true);
            if (Phase != DuelPhase.Live) return;
            if (attackerSlot == 0) DamageDealt0 += damage; else DamageDealt1 += damage;
        }

        public void ReportDeath(int victimSlot)
        {
            ValidateSlot(victimSlot);
            ReportDeaths(victimSlot == 0, victimSlot == 1);
        }

        /// <summary>Resolve both attacks before calling this, so a trade is independent of slot order.</summary>
        public void ReportDeaths(bool slot0Dead, bool slot1Dead)
        {
            if (Phase != DuelPhase.Live || !slot0Dead && !slot1Dead) return;
            FinishRound(slot0Dead && slot1Dead ? -1 : slot0Dead ? 1 : 0,
                slot0Dead && slot1Dead ? DuelRoundEndReason.DoubleKnockout : DuelRoundEndReason.Elimination);
        }

        /// <summary>A disconnect forfeits the match without fabricating played rounds or scores. Idempotent.</summary>
        public void ReportForfeit(int disconnectedSlot)
        {
            ValidateSlot(disconnectedSlot);
            if (Phase == DuelPhase.MatchOver) return;
            MatchWinnerSlot = 1 - disconnectedSlot;
            MatchEndReason = DuelMatchEndReason.Forfeit;
            Phase = DuelPhase.MatchOver;
            PhaseRemaining = RoundRemaining = 0;
        }

        /// <summary>Stops at a respawn boundary; consume that request before simulating the next round.</summary>
        public void Tick(float dt, float health0, float health1)
        {
            ValidateDuration(dt, nameof(dt), true);
            ValidateDuration(health0, nameof(health0), true);
            ValidateDuration(health1, nameof(health1), true);
            while (Phase != DuelPhase.MatchOver)
            {
                float remaining = Phase == DuelPhase.Live ? RoundRemaining : PhaseRemaining;
                if (remaining > dt)
                {
                    if (Phase == DuelPhase.Live) RoundRemaining -= dt; else PhaseRemaining -= dt;
                    return;
                }
                dt -= remaining;
                switch (Phase)
                {
                    case DuelPhase.Warmup:
                        Phase = DuelPhase.Live;
                        PhaseRemaining = 0;
                        break;
                    case DuelPhase.Live:
                        RoundRemaining = 0;
                        FinishRound(health0 > health1 ? 0 : health1 > health0 ? 1 : -1, DuelRoundEndReason.Timeout);
                        break;
                    case DuelPhase.RoundOver:
                        RoundNumber = CompletedRounds + 1;
                        BeginRound();
                        return;
                }
                if (dt <= 0) return;
            }
        }

        private void BeginRound()
        {
            Phase = DuelPhase.Warmup;
            PhaseRemaining = warmupSeconds;
            RoundRemaining = roundSeconds;
            RoundWinnerSlot = -1;
            RoundEndReason = DuelRoundEndReason.None;
            RoundGeneration++;
            respawnRequested = true;
        }

        private void FinishRound(int winner, DuelRoundEndReason reason)
        {
            RoundWinnerSlot = winner;
            RoundEndReason = reason;
            CompletedRounds++;
            if (winner == 0) Score0++;
            if (winner == 1) Score1++;
            if (Score0 >= RoundsToWin || Score1 >= RoundsToWin || CompletedRounds >= MaxRounds)
            {
                MatchEndReason = Score0 >= RoundsToWin || Score1 >= RoundsToWin ?
                    DuelMatchEndReason.ScoreLimit : DuelMatchEndReason.RoundLimit;
                MatchWinnerSlot = Score0 > Score1 ? 0 : Score1 > Score0 ? 1 :
                    DamageDealt0 > DamageDealt1 ? 0 : DamageDealt1 > DamageDealt0 ? 1 : -1;
                Phase = DuelPhase.MatchOver;
                PhaseRemaining = 0;
            }
            else
            {
                Phase = DuelPhase.RoundOver;
                PhaseRemaining = roundOverSeconds;
            }
        }

        private static void ValidateSlot(int slot)
        {
            if (slot != 0 && slot != 1) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        private static void ValidateDuration(float value, string name, bool allowZero)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || !allowZero && value == 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
