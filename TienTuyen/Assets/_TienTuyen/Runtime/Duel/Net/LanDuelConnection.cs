using System;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace TienTuyen.Duel.Net
{
    /// <summary>
    /// Direct-IP play over UnityTransport: the host listens on every interface and shows its LAN address,
    /// the friend types it in. A failed or dropped session ends in <see cref="DuelConnectionState.Failed"/>
    /// with <see cref="LastError"/> worded for the player.
    /// </summary>
    public sealed class LanDuelConnection : IDuelConnection
    {
        public const ushort DefaultPort = 7777;

        // 10 attempts half a second apart: a LAN answers in milliseconds, so a wrong IP is refused within 5 s.
        private const int ConnectTimeoutMs = 500, ConnectAttempts = 10;
        private const float WatchdogSlack = 3f;

        private enum Pending { None, Host, Client }

        private readonly DuelNetworkBootstrap m_Net;
        private readonly ushort m_Port;
        private Pending m_Pending;
        private string m_Address;
        private ushort m_TargetPort;
        private float m_ConnectStartedAt;
        private bool m_Stopping;

        public DuelConnectionState State { get; private set; } = DuelConnectionState.Offline;
        public string JoinInfo { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;

        public event Action<DuelConnectionState> StateChanged;

        public LanDuelConnection(DuelNetworkBootstrap net, ushort port = DefaultPort)
        {
            m_Net = net;
            m_Port = port;
            m_Net.Transport.ConnectTimeoutMS = ConnectTimeoutMs;
            m_Net.Transport.MaxConnectAttempts = ConnectAttempts;
            m_Net.Manager.OnClientConnectedCallback += OnClientConnected;
            m_Net.Manager.OnClientDisconnectCallback += OnClientDisconnected;
            m_Net.Manager.OnTransportFailure += OnTransportFailure;
            m_Net.SlotJoined += slot => { if (slot == DuelNetworkBootstrap.JoinerSlot) SetState(DuelConnectionState.Connected); };
            m_Net.SlotLeft += OnSlotLeft;
        }

        public void BeginHost() => Begin(Pending.Host);

        public void BeginClient(string joinTarget)
        {
            if (!TryParseTarget(joinTarget, m_Port, out m_Address, out m_TargetPort, out string error))
            {
                StopSession();
                Fail(error);
                return;
            }
            Begin(Pending.Client);
        }

        public void Tick()
        {
            if (m_Pending != Pending.None)
            {
                TryStartPending();
                return;
            }
            // Backstop in case the transport never reports its own timeout.
            if (State == DuelConnectionState.Starting && !m_Net.Manager.IsServer &&
                Time.realtimeSinceStartup - m_ConnectStartedAt > ConnectTimeoutMs * ConnectAttempts / 1000f + WatchdogSlack)
            {
                StopSession();
                Fail(UnreachableMessage());
            }
        }

        public void Shutdown()
        {
            m_Pending = Pending.None;
            StopSession();
            JoinInfo = string.Empty;
            SetState(DuelConnectionState.Offline);
        }

        private void Begin(Pending pending)
        {
            LastError = string.Empty;
            JoinInfo = string.Empty;
            StopSession();
            m_Pending = pending;
            SetState(DuelConnectionState.Starting);
            TryStartPending();
        }

        /// <summary>A previous session's shutdown finishes on a later frame, so a restart waits for it here.</summary>
        private void TryStartPending()
        {
            var manager = m_Net.Manager;
            if (manager.ShutdownInProgress || manager.IsListening) return;
            var pending = m_Pending;
            m_Pending = Pending.None;
            m_Stopping = false;
            if (pending == Pending.Host)
            {
                m_Net.Transport.SetConnectionData("127.0.0.1", m_Port, "0.0.0.0");
                if (!m_Net.StartHost())
                {
                    StopSession();
                    Fail($"Không mở được phòng ở cổng {m_Port}. Có thể một chương trình khác đang dùng cổng này.");
                    return;
                }
                JoinInfo = $"{FindLanAddress()}:{m_Port}";
                SetState(DuelConnectionState.Listening);
            }
            else if (pending == Pending.Client)
            {
                JoinInfo = $"{m_Address}:{m_TargetPort}";
                m_Net.Transport.SetConnectionData(m_Address, m_TargetPort);
                m_ConnectStartedAt = Time.realtimeSinceStartup;
                if (!m_Net.StartClient())
                {
                    StopSession();
                    Fail(UnreachableMessage());
                }
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!m_Net.Manager.IsServer && clientId == m_Net.Manager.LocalClientId) SetState(DuelConnectionState.Connected);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (m_Stopping || m_Net.Manager.IsServer) return;
            // NGO puts the server's own refusal here; a locally generated reason starts with its "[Disconnect Event]" tag.
            string reason = m_Net.Manager.DisconnectReason;
            bool serverSaid = !string.IsNullOrEmpty(reason) && !reason.StartsWith("[", StringComparison.Ordinal);
            string message = State == DuelConnectionState.Connected
                ? (serverSaid ? $"Mất kết nối với chủ phòng: {reason}" : "Mất kết nối với chủ phòng. Chủ phòng đã thoát hoặc mạng bị ngắt.")
                : serverSaid ? reason : UnreachableMessage();
            StopSession();
            Fail(message);
        }

        private void OnSlotLeft(int slot)
        {
            if (slot != DuelNetworkBootstrap.JoinerSlot || State != DuelConnectionState.Connected) return;
            LastError = "Người chơi kia đã rời phòng hoặc mất kết nối.";
            SetState(DuelConnectionState.Listening);
        }

        private void OnTransportFailure()
        {
            if (m_Stopping) return;
            StopSession();
            Fail("Lỗi mạng: kết nối bị gián đoạn. Hãy thử lại.");
        }

        private string UnreachableMessage() =>
            $"Không kết nối được tới {JoinInfo}. Kiểm tra lại IP, hai máy phải cùng mạng LAN, và tường lửa Windows phải cho phép trò chơi.";

        private void StopSession()
        {
            m_Stopping = true;
            var manager = m_Net.Manager;
            if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
        }

        private void Fail(string message)
        {
            m_Pending = Pending.None;
            LastError = message;
            SetState(DuelConnectionState.Failed);
        }

        private void SetState(DuelConnectionState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        internal static bool TryParseTarget(string target, ushort defaultPort, out string address, out ushort port, out string error)
        {
            address = string.Empty;
            port = defaultPort;
            error = string.Empty;
            string text = (target ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = "Hãy nhập địa chỉ IP của chủ phòng, ví dụ 192.168.1.14 hoặc 192.168.1.14:7777.";
                return false;
            }
            int colon = text.LastIndexOf(':');
            string host = colon >= 0 ? text.Substring(0, colon).Trim() : text;
            if (colon >= 0 && (!ushort.TryParse(text.Substring(colon + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port == 0))
            {
                error = $"Cổng \"{text.Substring(colon + 1).Trim()}\" không hợp lệ. Cổng là một số từ 1 đến 65535, mặc định {defaultPort}.";
                return false;
            }
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) host = "127.0.0.1";
            if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || host.Split('.').Length != 4)
            {
                error = $"\"{host}\" không phải địa chỉ IP hợp lệ. Địa chỉ có dạng bốn số, ví dụ 192.168.1.14.";
                return false;
            }
            address = ip.ToString();
            return true;
        }

        /// <summary>
        /// The IPv4 address a friend on the same network can reach: an up, non-virtual adapter with a gateway,
        /// private ranges first. Falls back to 127.0.0.1 so the host can still be joined from this machine.
        /// </summary>
        internal static string FindLanAddress()
        {
            string best = null;
            int bestScore = int.MinValue;
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var properties = adapter.GetIPProperties();
                    bool hasGateway = false;
                    foreach (var gateway in properties.GatewayAddresses)
                        if (gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any))
                            hasGateway = true;
                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        var ip = unicast.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                        byte[] b = ip.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;
                        bool isPrivate = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                        int score = (hasGateway ? 4 : 0) + (isPrivate ? 2 : 0) +
                                    (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 1 : 0);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = ip.ToString();
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is NetworkInformationException || exception is NotSupportedException)
            {
            }
            return best ?? "127.0.0.1";
        }
    }
}
