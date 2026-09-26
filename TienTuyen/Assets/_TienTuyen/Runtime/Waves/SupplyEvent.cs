using System;

namespace TienTuyen.Waves
{
    public enum SupplyEventPhase
    {
        Pending,
        Active,
        Completed,
        Claimed,
        Skipped,
        Forfeited
    }

    public struct SupplyReward
    {
        public readonly int CurrencyMinorUnits;
        public readonly int Experience;
        public readonly int Heal;

        public SupplyReward(int currencyMinorUnits, int experience, int heal)
        {
            if (currencyMinorUnits < 0) throw new ArgumentOutOfRangeException(nameof(currencyMinorUnits));
            if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
            if (heal < 0) throw new ArgumentOutOfRangeException(nameof(heal));
            CurrencyMinorUnits = currencyMinorUnits;
            Experience = experience;
            Heal = heal;
        }

        public int HealthAfterHealing(int currentHealth, int maximumHealth)
        {
            if (maximumHealth < 0) throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (currentHealth < 0 || currentHealth > maximumHealth)
                throw new ArgumentOutOfRangeException(nameof(currentHealth));

            return (int)Math.Min((long)maximumHealth, (long)currentHealth + Heal);
        }
    }

    public sealed class SupplyEvent
    {
        public const int DefaultWave = 3;
        public const float DefaultSpawnSecond = 15f;
        public const float DefaultRecoverySeconds = 3f;
        public const int DefaultCurrencyMinorUnits = 2000;
        public const int DefaultHeal = 10;

        private readonly int wave;
        private readonly float spawnSecond;
        private readonly float recoverySeconds;
        private readonly SupplyReward reward;
        private float lastElapsedSeconds;

        public SupplyEventPhase Phase { get; private set; }
        public float ProgressSeconds { get; private set; }
        public int TargetWave => wave;
        public float SpawnSecond => spawnSecond;
        public float RecoverySeconds => recoverySeconds;
        public bool IsClaimable => Phase == SupplyEventPhase.Completed;

        public SupplyEvent(
            int wave = DefaultWave,
            float spawnSecond = DefaultSpawnSecond,
            float recoverySeconds = DefaultRecoverySeconds,
            int currencyMinorUnits = DefaultCurrencyMinorUnits,
            int experience = 0,
            int heal = DefaultHeal)
        {
            if (wave < 1) throw new ArgumentOutOfRangeException(nameof(wave));
            if (!IsFiniteNonNegative(spawnSecond)) throw new ArgumentOutOfRangeException(nameof(spawnSecond));
            if (!IsFinitePositive(recoverySeconds)) throw new ArgumentOutOfRangeException(nameof(recoverySeconds));
            if (currencyMinorUnits < 0) throw new ArgumentOutOfRangeException(nameof(currencyMinorUnits));
            if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
            if (heal < 0) throw new ArgumentOutOfRangeException(nameof(heal));

            this.wave = wave;
            this.spawnSecond = spawnSecond;
            this.recoverySeconds = recoverySeconds;
            reward = new SupplyReward(currencyMinorUnits, experience, heal);
            Phase = SupplyEventPhase.Pending;
        }

        public bool IsSpawnEligible(int currentWave, float elapsedSeconds, bool playerAlive, bool waveComplete)
        {
            ValidateWave(currentWave);
            ValidateElapsed(elapsedSeconds);
            return Phase == SupplyEventPhase.Pending && currentWave == wave &&
                   elapsedSeconds >= spawnSecond && playerAlive && !waveComplete;
        }

        // Death wins ties with recovery and the wave timer. Recovery then wins a timer tie.
        // elapsedSeconds is the time at the end of this simulation step.
        public void Advance(
            int currentWave,
            float elapsedSeconds,
            float deltaSeconds,
            bool inRange,
            bool playerAlive,
            bool waveComplete,
            bool paused = false)
        {
            ValidateWave(currentWave);
            ValidateElapsed(elapsedSeconds);
            if (!IsFiniteNonNegative(deltaSeconds) || deltaSeconds > elapsedSeconds)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

            if (IsTerminal(Phase)) return;
            if (!playerAlive)
            {
                Phase = SupplyEventPhase.Forfeited;
                return;
            }

            if (currentWave > wave)
            {
                Phase = SupplyEventPhase.Skipped;
                return;
            }

            if (currentWave != wave) return;
            if (elapsedSeconds < lastElapsedSeconds)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (Phase == SupplyEventPhase.Completed) return;

            if (Phase == SupplyEventPhase.Pending && elapsedSeconds >= spawnSecond && !paused)
                Phase = SupplyEventPhase.Active;

            if (Phase == SupplyEventPhase.Active && inRange && !paused)
            {
                var activeStart = Math.Max(spawnSecond, Math.Max(lastElapsedSeconds, elapsedSeconds - deltaSeconds));
                var activeSeconds = Math.Max(0f, elapsedSeconds - activeStart);
                ProgressSeconds = Math.Min(recoverySeconds, ProgressSeconds + activeSeconds);
                if (ProgressSeconds >= recoverySeconds)
                    Phase = SupplyEventPhase.Completed;
            }

            if (waveComplete && Phase != SupplyEventPhase.Completed)
                Phase = SupplyEventPhase.Skipped;

            lastElapsedSeconds = elapsedSeconds;
        }

        public void Skip()
        {
            if (!IsTerminal(Phase)) Phase = SupplyEventPhase.Skipped;
        }

        // The caller applies the returned reward exactly when this method succeeds.
        public bool TryClaim(bool playerAlive, out SupplyReward claimedReward)
        {
            claimedReward = default(SupplyReward);
            if (!playerAlive && !IsTerminal(Phase))
                Phase = SupplyEventPhase.Forfeited;

            if (Phase != SupplyEventPhase.Completed) return false;
            Phase = SupplyEventPhase.Claimed;
            claimedReward = reward;
            return true;
        }

        private static bool IsTerminal(SupplyEventPhase phase)
        {
            return phase == SupplyEventPhase.Claimed || phase == SupplyEventPhase.Skipped ||
                   phase == SupplyEventPhase.Forfeited;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFiniteNonNegative(value) && value > 0f;
        }

        private static void ValidateElapsed(float elapsedSeconds)
        {
            if (!IsFiniteNonNegative(elapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        private static void ValidateWave(int currentWave)
        {
            if (currentWave < 1)
                throw new ArgumentOutOfRangeException(nameof(currentWave));
        }
    }
}
