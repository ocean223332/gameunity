using System.Runtime.InteropServices;
using UnityEngine;

namespace TienTuyen.Duel.Net
{
    /// <summary>
    /// Server to the slot's own client: the full sim state to reconcile from, and the newest input tick the
    /// server has stepped, so the client drops buffered inputs up to it and replays the rest. Everything
    /// prediction needs rides in this one message, so a lost packet cannot pair a state with the wrong round.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DuelOwnerSnapshot
    {
        public int ServerTick;
        public int LastConsumedInputTick;
        /// <summary>DuelMatch.RoundGeneration after this step. A change means the server respawned both
        /// bodies and dropped queued input: reset prediction to <see cref="State"/> without replaying.</summary>
        public int RoundGeneration;
        /// <summary>DuelMatch.CanAct after this step: what the next inputs will be stepped with.</summary>
        public bool CanAct;
        public DuelSimState State;
    }

    /// <summary>
    /// Server to the other client: just enough of a body to pose it. Nothing re-simulates the remote player,
    /// so this is a projection of <see cref="DuelSimState"/>, not a second copy of it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DuelOpponentView
    {
        public int ServerTick;
        public Vector3 Position;
        public float Yaw, Pitch, Crouch, Health;
        public int ActiveWeaponSlot;
        /// <summary>Buttons held on the last stepped input; Aim and Fire pose the body.</summary>
        public DuelButtons Buttons;
        public bool Reloading;

        public static DuelOpponentView From(in DuelSimState state, int serverTick) => new DuelOpponentView
        {
            ServerTick = serverTick,
            Position = state.Position,
            Yaw = state.Yaw,
            Pitch = state.Pitch,
            Crouch = state.Crouch,
            Health = state.Health,
            ActiveWeaponSlot = state.ActiveWeaponSlot,
            Buttons = state.PreviousButtons,
            Reloading = state.ReloadAt(state.ActiveWeaponSlot) > 0f
        };
    }

    /// <summary>Server to both clients: the match as the joiner's HUD shows it. Sent with every snapshot.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DuelMatchView
    {
        public int ServerTick;
        public DuelPhase Phase;
        public int RoundNumber, RoundGeneration, Score0, Score1;
        /// <summary>-1 when none or a draw.</summary>
        public int RoundWinnerSlot, MatchWinnerSlot;
        public DuelRoundEndReason RoundEndReason;
        public DuelMatchEndReason MatchEndReason;
        public float PhaseRemaining, RoundRemaining;

        public static DuelMatchView From(DuelMatch match, int serverTick) => new DuelMatchView
        {
            ServerTick = serverTick,
            Phase = match.Phase,
            RoundNumber = match.RoundNumber,
            RoundGeneration = match.RoundGeneration,
            Score0 = match.Score0,
            Score1 = match.Score1,
            RoundWinnerSlot = match.RoundWinnerSlot,
            MatchWinnerSlot = match.MatchWinnerSlot,
            RoundEndReason = match.RoundEndReason,
            MatchEndReason = match.MatchEndReason,
            PhaseRemaining = match.PhaseRemaining,
            RoundRemaining = match.RoundRemaining
        };
    }
}
