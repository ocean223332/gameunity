using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// Stand-in connection for exercising the lobby without a network: hosting shows this machine's LAN address and
    /// waits, joining "connects" for a moment and then fails the way an unreachable host does. With
    /// <see cref="ConnectAfter"/> set, hosting and joining succeed instead and raise <see cref="Connected"/>.
    /// </summary>
    public sealed class FakeDuelLobbyHost : IDuelLobbyHost
    {
        public const int Port = 7777;

        public string JoinInfo { get; private set; } = "";
        public string Status { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public bool Busy => mode != Mode.Idle;

        /// <summary>Seconds after which a request succeeds; zero keeps hosting waiting and makes joining fail.</summary>
        public float ConnectAfter;
        public event System.Action Connected;

        private enum Mode { Idle, Hosting, Joining }
        private Mode mode;
        private float started;
        private string target;

        public void RequestHost()
        {
            if (Busy) return;
            mode = Mode.Hosting;
            started = Time.unscaledTime;
            LastError = "";
            JoinInfo = LocalAddress() + ":" + Port;
            Status = "Đang chờ đối thủ vào phòng…";
        }

        public void RequestJoin(string address)
        {
            if (Busy) return;
            address = (address ?? "").Trim();
            if (address.Length == 0)
            {
                LastError = "Hãy nhập địa chỉ của chủ phòng.";
                return;
            }
            mode = Mode.Joining;
            started = Time.unscaledTime;
            target = address.Contains(":") ? address : address + ":" + Port;
            LastError = "";
            Status = "Đang kết nối tới " + target + "…";
        }

        public void RequestCancel()
        {
            mode = Mode.Idle;
            JoinInfo = "";
            Status = "";
        }

        public int LocalCharacter { get; private set; } = DuelRoster.NoPick;
        public void RequestCharacter(int index) => LocalCharacter = index;
        /// <summary>The fake plays slot 0; the opponent wears its default.</summary>
        public DuelCharacter CharacterOf(int slot) => DuelRoster.For(slot, slot == 0 ? LocalCharacter : DuelRoster.NoPick);

        /// <summary>Advances the fake; call once a frame.</summary>
        public void Tick()
        {
            if (mode == Mode.Idle) return;
            float age = Time.unscaledTime - started;
            if (ConnectAfter > 0f && age >= ConnectAfter)
            {
                mode = Mode.Idle;
                Status = "Đã kết nối";
                Connected?.Invoke();
                return;
            }
            if (mode == Mode.Joining && ConnectAfter <= 0f && age > 2.5f)
            {
                mode = Mode.Idle;
                Status = "";
                LastError = "Không kết nối được tới " + target + ". Kiểm tra địa chỉ và để hai máy cùng một mạng.";
            }
        }

        /// <summary>
        /// This machine's LAN IPv4 address: on an interface that is up and has a gateway, never loopback or a
        /// self-assigned 169.254 address (virtual adapters carry those and are unreachable from the other machine);
        /// else 127.0.0.1.
        /// </summary>
        public static string LocalAddress()
        {
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var properties = adapter.GetIPProperties();
                    bool routed = false;
                    foreach (var gateway in properties.GatewayAddresses)
                        if (gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any)) routed = true;
                    if (!routed) continue;
                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        var address = unicast.Address;
                        if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && !SelfAssigned(address))
                            return address.ToString();
                    }
                }
            }
            catch (NetworkInformationException) { }
            catch (System.NotSupportedException) { }
            return "127.0.0.1";
        }

        private static bool SelfAssigned(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }
    }

    /// <summary>
    /// A scripted best of five for looking at the HUD without the match logic: a countdown, a fight in which both
    /// sides take hits, the round result and, when either side wins three rounds, the match result. Freeze it with
    /// <see cref="Hold"/> to inspect one moment.
    /// </summary>
    public sealed class FakeDuelHudState : IDuelHudState
    {
        public const float RoundLength = 90f, CountdownLength = 3f, ResultLength = 3f;

        public DuelPhase Phase { get; set; } = DuelPhase.Warmup;
        public int Round { get; set; } = 1;
        public int RoundsToWin => DuelMatch.RoundsToWin;
        public int LocalRounds { get; set; }
        public int OpponentRounds { get; set; }
        public float RoundRemaining { get; set; } = RoundLength;
        public float CountdownRemaining { get; set; } = CountdownLength;
        public DuelRoundResult LastRound { get; set; }
        public DuelRoundResult MatchResult { get; set; }

        public string LocalName { get; set; } = "Bạn";
        public string OpponentName { get; set; } = "Đối thủ";
        public float LocalHealth { get; set; } = 100f;
        public float LocalMaxHealth => 100f;
        public float OpponentHealth { get; set; } = 100f;
        public float OpponentMaxHealth => 100f;

        public string WeaponId { get; set; } = "weapon.rifle";
        public string WeaponName { get; set; } = "AK-47";
        public int Ammo { get; set; } = 30;
        public int MagazineSize => 30;
        public float ReloadRemaining { get; set; }
        public float ReloadDuration => 2.4f;

        /// <summary>When true, <see cref="Tick"/> leaves every value as it is.</summary>
        public bool Hold;
        private float phaseAge, nextShot;

        public void Tick(float deltaTime)
        {
            if (Hold) return;
            phaseAge += deltaTime;
            switch (Phase)
            {
                case DuelPhase.Warmup:
                    CountdownRemaining = Mathf.Max(0f, CountdownLength - phaseAge);
                    if (CountdownRemaining <= 0f) Enter(DuelPhase.Live);
                    break;
                case DuelPhase.Live:
                    RoundRemaining = Mathf.Max(0f, RoundRemaining - deltaTime);
                    Fight(deltaTime);
                    break;
                case DuelPhase.RoundOver:
                    if (phaseAge < ResultLength) break;
                    if (LocalRounds >= RoundsToWin || OpponentRounds >= RoundsToWin)
                    {
                        MatchResult = LocalRounds > OpponentRounds ? DuelRoundResult.Won : DuelRoundResult.Lost;
                        Enter(DuelPhase.MatchOver);
                    }
                    else
                    {
                        Round++;
                        LocalHealth = OpponentHealth = 100f;
                        RoundRemaining = RoundLength;
                        Ammo = MagazineSize;
                        Enter(DuelPhase.Warmup);
                    }
                    break;
            }
        }

        private void Fight(float deltaTime)
        {
            if (ReloadRemaining > 0f)
            {
                ReloadRemaining = Mathf.Max(0f, ReloadRemaining - deltaTime);
                if (ReloadRemaining <= 0f) Ammo = MagazineSize;
                return;
            }
            nextShot -= deltaTime;
            if (nextShot > 0f) return;
            nextShot = .45f;
            Ammo--;
            if (Ammo <= 0) ReloadRemaining = ReloadDuration;
            // Round 2 favours the opponent; the other rounds favour the local player.
            bool localAhead = Round != 2;
            if (Random.value < (localAhead ? .55f : .3f)) OpponentHealth = Mathf.Max(0f, OpponentHealth - 18f);
            if (Random.value < (localAhead ? .3f : .55f)) LocalHealth = Mathf.Max(0f, LocalHealth - 18f);
            if (OpponentHealth <= 0f) EndRound(DuelRoundResult.Won);
            else if (LocalHealth <= 0f) EndRound(DuelRoundResult.Lost);
        }

        private void EndRound(DuelRoundResult result)
        {
            LastRound = result;
            if (result == DuelRoundResult.Won) LocalRounds++;
            if (result == DuelRoundResult.Lost) OpponentRounds++;
            Enter(DuelPhase.RoundOver);
        }

        private void Enter(DuelPhase phase)
        {
            Phase = phase;
            phaseAge = 0f;
        }
    }
}
