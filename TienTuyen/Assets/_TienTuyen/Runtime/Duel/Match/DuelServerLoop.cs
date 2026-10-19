using System;
using UnityEngine;

namespace TienTuyen.Duel
{
    public readonly struct DuelServerStepResult
    {
        public readonly DuelStepResult Player0, Player1;
        public readonly DuelHitResult Hit0, Hit1;
        public readonly bool Respawned;

        internal DuelServerStepResult(DuelStepResult player0, DuelStepResult player1,
            DuelHitResult hit0, DuelHitResult hit1, bool respawned)
        {
            Player0 = player0; Player1 = player1; Hit0 = hit0; Hit1 = hit1; Respawned = respawned;
        }
    }

    /// <summary>
    /// Fixed-step authority for two players. Each step consumes at most one queued input per slot.
    /// Missing input is idle (no movement/buttons), while gravity and weapon timers keep advancing.
    /// Input ticks are sequence numbers, with wrap supported within a half-int-range comparison window.
    /// </summary>
    public sealed class DuelServerLoop
    {
        public const int DefaultInputCapacity = 128;
        /// <summary>Frames a slot may keep queued after a step: absorbs arrival jitter, not a backlog.</summary>
        public const int MaxQueuedInputs = 1;
        private const DuelButtons PressButtons = DuelButtons.Fire | DuelButtons.Reload | DuelButtons.Jump;
        private readonly DuelSimState[] players = new DuelSimState[2];
        private readonly InputBuffer[] inputs;
        private readonly bool[] connected = { true, true };

        public DuelMatch Match { get; }
        public float FixedDeltaTime { get; }
        public int InputCapacity { get; }
        public int ServerTick { get; private set; }

        public DuelServerLoop(float fixedDeltaTime = 1f / 30f, DuelMatch match = null,
            int inputCapacity = DefaultInputCapacity)
        {
            if (!DuelPlayerSim.Finite(fixedDeltaTime) || fixedDeltaTime <= 0)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));
            if (inputCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(inputCapacity));
            FixedDeltaTime = fixedDeltaTime;
            InputCapacity = inputCapacity;
            Match = match ?? new DuelMatch();
            inputs = new[] { new InputBuffer(inputCapacity), new InputBuffer(inputCapacity) };
            players[0] = DuelPlayerSim.CreateState(0);
            players[1] = DuelPlayerSim.CreateState(1);
            RespawnIfRequested();
        }

        public DuelSimState GetState(int slot) { ValidateSlot(slot); return players[slot]; }
        public int GetLastConsumedInputTick(int slot) { ValidateSlot(slot); return inputs[slot].LastConsumedTick; }
        public int GetBufferedInputCount(int slot) { ValidateSlot(slot); return inputs[slot].Count; }
        public bool IsConnected(int slot) { ValidateSlot(slot); return connected[slot]; }

        /// <summary>Reject stale/duplicate input or a full queue; reorder unconsumed frames by sequence tick.</summary>
        public bool TryQueueInput(int slot, in DuelInputFrame input)
        {
            ValidateSlot(slot);
            return connected[slot] && Match.Phase != DuelPhase.MatchOver && inputs[slot].TryAdd(input);
        }

        /// <summary>Immediately forfeits the match to the survivor. A second notification preserves that result.</summary>
        public void Disconnect(int slot)
        {
            ValidateSlot(slot);
            connected[slot] = false;
            inputs[slot].Clear();
            Match.ReportForfeit(slot);
        }

        public DuelServerStepResult Step()
        {
            if (Match.Phase == DuelPhase.MatchOver) return default;
            ServerTick = unchecked(ServerTick + 1);
            bool respawned = RespawnIfRequested();
            bool canAct = Match.CanAct;
            DuelPhase phaseBefore = Match.Phase;
            DuelInputFrame input0 = TakeInputOrIdle(0), input1 = TakeInputOrIdle(1);
            DuelStepResult step0 = DuelPlayerSim.Step(ref players[0], input0, FixedDeltaTime, canAct);
            DuelStepResult step1 = DuelPlayerSim.Step(ref players[1], input1, FixedDeltaTime, canAct);

            // Both traces see the same pair of living states. Never apply slot 0's hit before tracing slot 1.
            DuelHitResult hit0 = DuelCombatResolver.Resolve(players[0], step0.Attack, players[1]);
            DuelHitResult hit1 = DuelCombatResolver.Resolve(players[1], step1.Attack, players[0]);
            float damage0 = Mathf.Min(players[1].Health, hit0.Damage);
            float damage1 = Mathf.Min(players[0].Health, hit1.Damage);
            players[0].Health -= damage1;
            players[1].Health -= damage0;
            Match.ReportDamage(0, damage0);
            Match.ReportDamage(1, damage1);
            if (canAct) Match.ReportDeaths(players[0].Dead, players[1].Dead);

            // Death occurs at the end of this interval; start the result pause on the following step.
            if (Match.Phase == phaseBefore) Match.Tick(FixedDeltaTime, players[0].Health, players[1].Health);
            respawned |= RespawnIfRequested();
            return new DuelServerStepResult(step0, step1, hit0, hit1, respawned);
        }

        private DuelInputFrame TakeInputOrIdle(int slot)
        {
            if (inputs[slot].TryTake(out DuelInputFrame input))
            {
                // A host stall skips server steps while the client keeps sending, and a surplus left queued would
                // delay every later input by that many steps for the rest of the round. Fold it into this step,
                // keeping any press or weapon switch from the frames folded away; held states (aim, crouch, walk)
                // are the latest frame's, so a released crouch is not simulated as held.
                while (inputs[slot].Count > MaxQueuedInputs && inputs[slot].TryTake(out DuelInputFrame next))
                {
                    next.Buttons |= input.Buttons & PressButtons;
                    if (next.WeaponSlot < 0) next.WeaponSlot = input.WeaponSlot;
                    input = next;
                }
                return input;
            }
            return new DuelInputFrame
            {
                Tick = inputs[slot].LastConsumedTick, WeaponSlot = -1,
                Yaw = players[slot].Yaw, Pitch = players[slot].Pitch,
                AimOrigin = DuelPlayerSim.MuzzlePosition(players[slot])
            };
        }

        private bool RespawnIfRequested()
        {
            if (!Match.ConsumeRespawnRequest()) return false;
            for (int slot = 0; slot < 2; slot++)
            {
                players[slot] = DuelPlayerSim.CreateState(slot);
                inputs[slot].Clear();
            }
            return true;
        }

        private static void ValidateSlot(int slot)
        {
            if (slot != 0 && slot != 1) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        private sealed class InputBuffer
        {
            private readonly DuelInputFrame[] frames;
            public int Count { get; private set; }
            public int LastConsumedTick { get; private set; } = -1;

            public InputBuffer(int capacity) { frames = new DuelInputFrame[capacity]; }
            public void Clear() { Count = 0; } // A round reset does not rewind the session's input acknowledgement.

            public bool TryAdd(in DuelInputFrame input)
            {
                if (!IsNewer(input.Tick, LastConsumedTick) || Count == frames.Length) return false;
                int index = 0;
                while (index < Count && IsNewer(input.Tick, frames[index].Tick)) index++;
                if (index < Count && frames[index].Tick == input.Tick) return false;
                Array.Copy(frames, index, frames, index + 1, Count - index);
                frames[index] = input;
                Count++;
                return true;
            }

            public bool TryTake(out DuelInputFrame input)
            {
                input = default;
                if (Count == 0) return false;
                input = frames[0];
                Count--;
                Array.Copy(frames, 1, frames, 0, Count);
                LastConsumedTick = input.Tick;
                return true;
            }

            private static bool IsNewer(int tick, int baseline) => unchecked(tick - baseline) > 0;
        }
    }
}
