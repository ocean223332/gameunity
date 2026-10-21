using System;
using Unity.Collections;
using Unity.Netcode;

namespace TienTuyen.Duel.Net
{
    /// <summary>
    /// What the two machines say once connected. Client to server: <see cref="DuelInputFrame"/>. Server to
    /// each client: its own <see cref="DuelOwnerSnapshot"/>, a <see cref="DuelOpponentView"/> of the other and the <see cref="DuelMatchView"/>.
    /// The host is slot 0 and its own input loops back without touching the network. Snapshots only go to a
    /// slot that has called <see cref="DuelNetworkBootstrap.MarkLocalReady"/>.
    /// </summary>
    public sealed class DuelNetChannel
    {
        internal const string InputMessage = "TT.Duel.Input";
        internal const string OwnerMessage = "TT.Duel.Owner";
        internal const string OpponentMessage = "TT.Duel.Opponent";
        internal const string MatchMessage = "TT.Duel.Match";

        private readonly DuelNetworkBootstrap m_Net;

        internal DuelNetChannel(DuelNetworkBootstrap net) => m_Net = net;

        /// <summary>Server only: (slot, frame) for every input that arrives, the host's own included.</summary>
        public event Action<int, DuelInputFrame> InputReceived;

        /// <summary>Joiner only.</summary>
        public event Action<DuelOwnerSnapshot> OwnerSnapshotReceived;

        /// <summary>Joiner only.</summary>
        public event Action<DuelOpponentView> OpponentViewReceived;

        /// <summary>Joiner only.</summary>
        public event Action<DuelMatchView> MatchViewReceived;

        public void SendInput(in DuelInputFrame frame)
        {
            var manager = m_Net.Manager;
            if (!manager.IsListening) return;
            if (manager.IsServer)
            {
                InputReceived?.Invoke(DuelNetworkBootstrap.HostSlot, frame);
                return;
            }
            if (!manager.IsConnectedClient) return;
            Send(InputMessage, NetworkManager.ServerClientId, frame);
        }

        /// <summary>Server only. False when the slot is the host itself, absent, or not ready yet.</summary>
        public bool SendOwnerSnapshot(int slot, in DuelOwnerSnapshot snapshot) =>
            TryRemoteReadyClient(slot, out ulong clientId) && Send(OwnerMessage, clientId, snapshot);

        /// <summary>Server only: the view of the other body, sent to <paramref name="toSlot"/>.</summary>
        public bool SendOpponentView(int toSlot, in DuelOpponentView view) =>
            TryRemoteReadyClient(toSlot, out ulong clientId) && Send(OpponentMessage, clientId, view);

        /// <summary>Server only: the match state for <paramref name="toSlot"/>'s HUD.</summary>
        public bool SendMatchView(int toSlot, in DuelMatchView view) =>
            TryRemoteReadyClient(toSlot, out ulong clientId) && Send(MatchMessage, clientId, view);

        internal void Register(CustomMessagingManager messaging)
        {
            messaging.RegisterNamedMessageHandler(InputMessage, OnInput);
            messaging.RegisterNamedMessageHandler(OwnerMessage, OnOwner);
            messaging.RegisterNamedMessageHandler(OpponentMessage, OnOpponent);
            messaging.RegisterNamedMessageHandler(MatchMessage, OnMatch);
        }

        private bool TryRemoteReadyClient(int slot, out ulong clientId)
        {
            clientId = 0;
            return m_Net.Manager.IsServer && slot != DuelNetworkBootstrap.HostSlot && m_Net.IsReady(slot) &&
                   m_Net.TryGetClient(slot, out clientId);
        }

        private bool Send<T>(string message, ulong clientId, in T value) where T : unmanaged
        {
            using var writer = new FastBufferWriter(DuelWire.SizeOf<T>(), Allocator.Temp);
            DuelWire.Write(writer, value);
            m_Net.Manager.CustomMessagingManager.SendNamedMessage(message, clientId, writer, NetworkDelivery.UnreliableSequenced);
            return true;
        }

        private void OnInput(ulong sender, FastBufferReader reader)
        {
            int slot = m_Net.SlotOf(sender);
            if (!m_Net.Manager.IsServer || slot == DuelNetworkBootstrap.NoSlot) return;
            if (DuelWire.TryRead(reader, out DuelInputFrame frame)) InputReceived?.Invoke(slot, frame);
        }

        private void OnOwner(ulong sender, FastBufferReader reader)
        {
            if (sender == NetworkManager.ServerClientId && DuelWire.TryRead(reader, out DuelOwnerSnapshot snapshot))
                OwnerSnapshotReceived?.Invoke(snapshot);
        }

        private void OnOpponent(ulong sender, FastBufferReader reader)
        {
            if (sender == NetworkManager.ServerClientId && DuelWire.TryRead(reader, out DuelOpponentView view))
                OpponentViewReceived?.Invoke(view);
        }

        private void OnMatch(ulong sender, FastBufferReader reader)
        {
            if (sender == NetworkManager.ServerClientId && DuelWire.TryRead(reader, out DuelMatchView view))
                MatchViewReceived?.Invoke(view);
        }
    }
}
