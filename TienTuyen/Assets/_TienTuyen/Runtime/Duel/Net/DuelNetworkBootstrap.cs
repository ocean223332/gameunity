using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TienTuyen.Duel.Net
{
    /// <summary>
    /// Builds the NetworkManager and UnityTransport in code and holds the duel's seats. NGO carries only the
    /// connection, approval and messages; there are no NetworkObjects or player prefabs, the bodies are plain
    /// GameObjects driven from <see cref="Channel"/>.
    /// This is the LAN and direct-IP <see cref="IDuelSessionLink"/>.
    /// The host is always slot 0 and the one joiner slot 1. Approval refuses a third connection, and a build
    /// whose wire structs differ. Slot events are raised on the server only.
    /// </summary>
    public sealed class DuelNetworkBootstrap : IDisposable, IDuelSessionLink
    {
        public const int SlotCount = 2, HostSlot = 0, JoinerSlot = 1, NoSlot = -1;

        public const string FullReason = "Phòng đã đủ 2 người chơi.";
        internal const string VersionReason = "Hai máy đang chạy hai bản trò chơi khác nhau. Hãy dùng cùng một bản build.";

        private const string ReadyMessage = "TT.Duel.Ready";

        private readonly GameObject m_Root;
        private readonly byte[] m_Protocol;
        private readonly bool[] m_Joined = new bool[SlotCount], m_Ready = new bool[SlotCount];
        private ulong m_JoinerId;
        private bool m_HasJoiner;

        internal NetworkManager Manager { get; }
        internal UnityTransport Transport { get; }

        public DuelNetChannel Channel { get; }

        /// <summary>Server: a slot's client finished connecting (slot 0 is the host itself).</summary>
        public event Action<int> SlotJoined;

        /// <summary>Server: a joined slot dropped or left; its ready flag is already cleared.</summary>
        public event Action<int> SlotLeft;

        /// <summary>Server: a slot has built its world and can take snapshots.</summary>
        public event Action<int> SlotReady;

        public DuelNetworkBootstrap(string name = "DuelNetwork")
        {
            m_Root = new GameObject(name);
            Transport = m_Root.AddComponent<UnityTransport>();
            // A dropped peer is noticed in seconds instead of the default half minute of frozen arena.
            Transport.DisconnectTimeoutMS = 5000;
            Manager = m_Root.AddComponent<NetworkManager>();
            m_Protocol = ProtocolPayload();
            Manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = Transport,
                ConnectionApproval = true,
                ConnectionData = m_Protocol,
                EnableSceneManagement = false,
                TickRate = 60
            };
            Manager.RunInBackground = true;
            Manager.LogLevel = LogLevel.Error;
            Manager.ConnectionApprovalCallback = Approve;
            Manager.OnClientConnectedCallback += OnClientConnected;
            Manager.OnClientDisconnectCallback += OnClientDisconnected;
            Manager.OnServerStopped += _ => ResetSeats();
            Manager.OnClientStopped += _ => ResetSeats();
            Channel = new DuelNetChannel(this);
        }

        public bool IsServer => Manager.IsServer;

        /// <summary>This machine's slot while a session runs; <see cref="NoSlot"/> otherwise.</summary>
        public int LocalSlot => !Manager.IsListening ? NoSlot : Manager.IsServer ? HostSlot : JoinerSlot;

        /// <summary>Server: both slots joined and reported ready, so the match may start.</summary>
        public bool BothReady => m_Ready[HostSlot] && m_Ready[JoinerSlot];

        public bool IsJoined(int slot) => slot >= 0 && slot < SlotCount && m_Joined[slot];

        public bool IsReady(int slot) => slot >= 0 && slot < SlotCount && m_Ready[slot];

        public int SlotOf(ulong clientId)
        {
            if (!Manager.IsListening) return NoSlot;
            if (clientId == NetworkManager.ServerClientId) return HostSlot;
            if (Manager.IsServer) return m_HasJoiner && clientId == m_JoinerId ? JoinerSlot : NoSlot;
            return clientId == Manager.LocalClientId ? JoinerSlot : NoSlot;
        }

        public bool TryGetClient(int slot, out ulong clientId)
        {
            clientId = NetworkManager.ServerClientId;
            if (!Manager.IsListening) return false;
            if (slot == HostSlot) return true;
            if (slot != JoinerSlot) return false;
            if (!Manager.IsServer)
            {
                clientId = Manager.LocalClientId;
                return true;
            }
            clientId = m_JoinerId;
            return m_HasJoiner;
        }

        /// <summary>
        /// Call once this machine has built the arena and can take snapshots. The server sends snapshots only
        /// to a ready slot, and the match waits for <see cref="BothReady"/>.
        /// </summary>
        public void MarkLocalReady()
        {
            if (!Manager.IsListening) return;
            if (Manager.IsServer)
            {
                SetReady(HostSlot);
                return;
            }
            if (!Manager.IsConnectedClient) return;
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            writer.WriteByteSafe(1);
            Manager.CustomMessagingManager.SendNamedMessage(ReadyMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        internal bool StartHost()
        {
            ResetSeats();
            if (!Manager.StartHost()) return false;
            RegisterMessages();
            return true;
        }

        internal bool StartClient()
        {
            ResetSeats();
            if (!Manager.StartClient()) return false;
            RegisterMessages();
            return true;
        }

        /// <summary>
        /// Ends any session, then destroys the GameObject once NGO has finished stopping: destroying it
        /// mid-shutdown frees the transport's driver before NetworkManager's own teardown still uses it.
        /// </summary>
        public void Dispose()
        {
            if (m_Root == null) return;
            if (!Manager.IsListening && !Manager.ShutdownInProgress)
            {
                Object.Destroy(m_Root);
                return;
            }
            Manager.OnServerStopped += _ => Object.Destroy(m_Root);
            Manager.OnClientStopped += _ => Object.Destroy(m_Root);
            if (!Manager.ShutdownInProgress) Manager.Shutdown(true);
        }

        event Action<int, DuelInputFrame> IDuelSessionLink.InputReceived { add => Channel.InputReceived += value; remove => Channel.InputReceived -= value; }
        event Action<DuelOwnerSnapshot> IDuelSessionLink.OwnerSnapshotReceived { add => Channel.OwnerSnapshotReceived += value; remove => Channel.OwnerSnapshotReceived -= value; }
        event Action<DuelOpponentView> IDuelSessionLink.OpponentViewReceived { add => Channel.OpponentViewReceived += value; remove => Channel.OpponentViewReceived -= value; }
        event Action<DuelMatchView> IDuelSessionLink.MatchViewReceived { add => Channel.MatchViewReceived += value; remove => Channel.MatchViewReceived -= value; }
        void IDuelSessionLink.SendInput(in DuelInputFrame frame) => Channel.SendInput(frame);
        bool IDuelSessionLink.SendOwnerSnapshot(int slot, in DuelOwnerSnapshot snapshot) => Channel.SendOwnerSnapshot(slot, snapshot);
        bool IDuelSessionLink.SendOpponentView(int toSlot, in DuelOpponentView view) => Channel.SendOpponentView(toSlot, view);
        bool IDuelSessionLink.SendMatchView(int toSlot, in DuelMatchView view) => Channel.SendMatchView(toSlot, view);

        private void RegisterMessages()
        {
            Manager.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage, OnReadyMessage);
            Channel.Register(Manager.CustomMessagingManager);
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Approved = false;
            if (!SameProtocol(request.Payload))
                response.Reason = VersionReason;
            else if (request.ClientNetworkId == NetworkManager.ServerClientId)
                response.Approved = true;
            else if (m_HasJoiner)
                response.Reason = FullReason;
            else
            {
                m_JoinerId = request.ClientNetworkId;
                m_HasJoiner = true;
                response.Approved = true;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!Manager.IsServer) return;
            int slot = SlotOf(clientId);
            if (slot == NoSlot || m_Joined[slot]) return;
            m_Joined[slot] = true;
            SlotJoined?.Invoke(slot);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!Manager.IsServer || !m_HasJoiner || clientId != m_JoinerId) return;
            m_HasJoiner = false;
            m_Ready[JoinerSlot] = false;
            if (!m_Joined[JoinerSlot]) return;
            m_Joined[JoinerSlot] = false;
            SlotLeft?.Invoke(JoinerSlot);
        }

        private void OnReadyMessage(ulong sender, FastBufferReader reader)
        {
            if (Manager.IsServer && SlotOf(sender) == JoinerSlot && m_Joined[JoinerSlot]) SetReady(JoinerSlot);
        }

        private void SetReady(int slot)
        {
            if (m_Ready[slot]) return;
            m_Ready[slot] = true;
            SlotReady?.Invoke(slot);
        }

        private void ResetSeats()
        {
            m_HasJoiner = false;
            Array.Clear(m_Joined, 0, SlotCount);
            Array.Clear(m_Ready, 0, SlotCount);
        }

        private static byte[] ProtocolPayload()
        {
            uint[] prints = { DuelWire.Fingerprint<DuelInputFrame>(), DuelWire.Fingerprint<DuelOwnerSnapshot>(), DuelWire.Fingerprint<DuelOpponentView>(), DuelWire.Fingerprint<DuelMatchView>() };
            var payload = new byte[prints.Length * sizeof(uint)];
            Buffer.BlockCopy(prints, 0, payload, 0, payload.Length);
            return payload;
        }

        private bool SameProtocol(byte[] payload)
        {
            if (payload == null || payload.Length != m_Protocol.Length) return false;
            for (int i = 0; i < payload.Length; i++)
                if (payload[i] != m_Protocol[i]) return false;
            return true;
        }
    }
}
