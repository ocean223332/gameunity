using System;

namespace TienTuyen.Duel.Net
{
    /// <summary>
    /// What a <see cref="DuelSession"/> needs from the link between the two machines: seats, readiness and the
    /// duel's messages. <see cref="DuelNetworkBootstrap"/> is the LAN and direct-IP link; an in-process pair
    /// implements it to play a match without sockets, and the session never sees NGO.
    /// The host is slot 0 and the one joiner slot 1. Most members mean something on one side only: on the host,
    /// <see cref="BothReady"/>, the slot events, <see cref="InputReceived"/> and the three snapshot sends; on the
    /// joiner, the three snapshot events. Elsewhere they stay false, silent or return false.
    /// <see cref="IsServer"/>, <see cref="LocalSlot"/>, <see cref="MarkLocalReady"/> and <see cref="SendInput"/> work on both.
    /// </summary>
    public interface IDuelSessionLink
    {
        bool IsServer { get; }

        /// <summary>0 on the host, 1 on the joiner, -1 while not linked.</summary>
        int LocalSlot { get; }

        /// <summary>Server: both slots joined and reported ready, so the match may start.</summary>
        bool BothReady { get; }

        /// <summary>This machine's arena is built and can take snapshots.</summary>
        void MarkLocalReady();

        /// <summary>Server: a slot finished connecting.</summary>
        event Action<int> SlotJoined;

        /// <summary>Server: a joined slot dropped or left; it is no longer ready.</summary>
        event Action<int> SlotLeft;

        /// <summary>Server: (slot, frame) for every input that arrives, the host's own included as slot 0.</summary>
        event Action<int, DuelInputFrame> InputReceived;

        /// <summary>Joiner.</summary>
        event Action<DuelOwnerSnapshot> OwnerSnapshotReceived;

        /// <summary>Joiner.</summary>
        event Action<DuelOpponentView> OpponentViewReceived;

        /// <summary>Joiner.</summary>
        event Action<DuelMatchView> MatchViewReceived;

        /// <summary>To the server; on the host it arrives at once as slot 0.</summary>
        void SendInput(in DuelInputFrame frame);

        /// <summary>Server. False when the slot is the host itself, absent, or not ready yet.</summary>
        bool SendOwnerSnapshot(int slot, in DuelOwnerSnapshot snapshot);

        /// <summary>Server: the view of the other body, sent to <paramref name="toSlot"/>. False as above.</summary>
        bool SendOpponentView(int toSlot, in DuelOpponentView view);

        /// <summary>Server: the match for <paramref name="toSlot"/>'s HUD. False as above.</summary>
        bool SendMatchView(int toSlot, in DuelMatchView view);
    }
}
