using System;
using UnityEngine;

namespace TienTuyen.Duel
{
    /// <summary>Correction from the old predicted present to the replayed present, not to the past snapshot.</summary>
    public readonly struct DuelReconciliationResult
    {
        public readonly bool Applied, Diverged;
        public readonly Vector3 PositionDelta;
        public readonly float YawDelta, PitchDelta;
        public readonly int ReplayedInputs;
        public float PositionError => PositionDelta.magnitude;

        internal DuelReconciliationResult(bool applied, bool diverged, Vector3 positionDelta,
            float yawDelta, float pitchDelta, int replayedInputs)
        {
            Applied = applied; Diverged = diverged; PositionDelta = positionDelta;
            YawDelta = yawDelta; PitchDelta = pitchDelta; ReplayedInputs = replayedInputs;
        }
    }

    /// <summary>
    /// Bounded local prediction at one fixed timestep per submitted input, independent of wall clocks.
    /// Ticks are increasing sequence numbers (wrap supported within a half-int-range window), not elapsed time.
    /// Send inputs independently: when history fills, prediction pauses rather than losing unacknowledged inputs.
    /// </summary>
    public sealed class DuelPredictor
    {
        public const int DefaultCapacity = 128;
        private readonly BufferedInput[] history;
        private DuelSimState state;
        private int head, count, latestTick, acknowledgedTick;
        private int latestSnapshotTick;

        private struct BufferedInput
        {
            public DuelInputFrame Input;
            public bool CanAct;
        }

        public DuelSimState State => state;
        public int BufferedInputCount => count;
        public int Capacity => history.Length;
        public float FixedDeltaTime { get; }
        public int LastAcknowledgedTick => acknowledgedTick;
        public int LatestPredictedTick => latestTick;
        /// <summary>The minimum next sequence tick; use this to catch a local input clock up to a server ack.</summary>
        public int NextInputTick => unchecked(latestTick + 1);

        public DuelPredictor(in DuelSimState initialState, float fixedDeltaTime,
            int capacity = DefaultCapacity, int initialTick = -1, int initialServerTick = -1)
        {
            if (!DuelPlayerSim.Finite(fixedDeltaTime) || fixedDeltaTime <= 0)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            FixedDeltaTime = fixedDeltaTime;
            history = new BufferedInput[capacity];
            Reset(initialState, initialTick, initialServerTick);
        }

        /// <summary>
        /// False for an old/duplicate tick or full history: state and history remain unchanged.
        /// canAct is recorded with each input so replay preserves warmup freezes.
        /// Only new predictions expose attack events; reconciliation never emits them again.
        /// </summary>
        public bool TryPredict(in DuelInputFrame input, out DuelStepResult result, bool canAct = true)
        {
            result = default;
            if (!IsNewer(input.Tick, latestTick) || count == history.Length) return false;
            result = DuelPlayerSim.Step(ref state, input, FixedDeltaTime, canAct);
            history[(head + count) % history.Length] = new BufferedInput { Input = input, CanAct = canAct };
            count++;
            latestTick = input.Tick;
            return true;
        }

        /// <summary>
        /// Server ticks order snapshots independently of input acknowledgements. A fresh snapshot with the same
        /// ack still corrects health, gravity and timers after an idle server step, then replays pending input.
        /// Discard inputs through the ack and replay all newer inputs once; no exact buffered tick is required.
        /// </summary>
        public DuelReconciliationResult Reconcile(in DuelSimState authoritativeState, int lastConsumedInputTick,
            int serverTick)
        {
            if (!IsNewer(serverTick, latestSnapshotTick) ||
                IsNewer(acknowledgedTick, lastConsumedInputTick)) return default;
            latestSnapshotTick = serverTick;
            return ApplySnapshot(authoritativeState, lastConsumedInputTick);
        }

        private DuelReconciliationResult ApplySnapshot(in DuelSimState authoritativeState, int lastConsumedInputTick)
        {
            DuelSimState before = state;
            while (count > 0 && !IsNewer(history[head].Input.Tick, lastConsumedInputTick))
            {
                head = (head + 1) % history.Length;
                count--;
            }
            acknowledgedTick = lastConsumedInputTick;
            if (IsNewer(lastConsumedInputTick, latestTick)) latestTick = lastConsumedInputTick;
            state = authoritativeState;
            for (int i = 0; i < count; i++)
            {
                BufferedInput entry = history[(head + i) % history.Length];
                DuelPlayerSim.Step(ref state, entry.Input, FixedDeltaTime, entry.CanAct);
            }
            // Value equality also detects corrections to health, ammo, held buttons and weapon timers.
            bool diverged = !state.Equals(before);
            return new DuelReconciliationResult(true, diverged, state.Position - before.Position,
                Mathf.DeltaAngle(before.Yaw, state.Yaw), state.Pitch - before.Pitch, count);
        }

        /// <summary>Install a round baseline and its snapshot ordering floor without replaying old input.</summary>
        public void Reset(in DuelSimState authoritativeState, int lastConsumedInputTick, int serverTick)
        {
            state = authoritativeState;
            head = count = 0;
            latestTick = acknowledgedTick = lastConsumedInputTick;
            latestSnapshotTick = serverTick;
        }

        private static bool IsNewer(int tick, int baseline) => unchecked(tick - baseline) > 0;
    }
}
