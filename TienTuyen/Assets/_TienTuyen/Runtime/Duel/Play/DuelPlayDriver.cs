using TienTuyen.Combat;
using TienTuyen.Content;
using TienTuyen.Duel.Net;
using TienTuyen.Duel.Presentation;
using UnityEngine;

namespace TienTuyen.Duel.Play
{
    /// <summary>
    /// Runs a real duel on a <see cref="DuelStage"/>: owns the connection and the <see cref="DuelSession"/>,
    /// feeds the player's controls into it every frame, and shows it through the stage's lobby and HUD.
    /// Attached to every stage as it is created; destroyed with it, which ends the session.
    /// </summary>
    public sealed class DuelPlayDriver : MonoBehaviour, IDuelLobbyHost, IDuelHudState
    {
        private DuelStage stage;
        private DuelNetworkBootstrap net;
        private LanDuelConnection connection;
        private DuelSession session;
        private readonly DuelLocalInput input = new DuelLocalInput();
        private bool readySent, aimMissingLogged, matchEndLogged;
        private DuelNetStats stats;

        // By assignment, so a Play without a domain reload never attaches a second driver.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => DuelStage.Created = stage => stage.gameObject.AddComponent<DuelPlayDriver>().Begin(stage);

        // Runs inside the stage's Awake, so the stage sees its lobby and session from its first Start.
        private void Begin(DuelStage owner)
        {
            stage = owner;
            net = new DuelNetworkBootstrap();
            connection = new LanDuelConnection(net);
            session = new DuelSession(net, connection);
            connection.StateChanged += _ => readySent = false;
            stage.Session = session;
            stage.Lobby = this;
            stage.LeaveMatchRequested += session.Leave;
            stats = DuelNetStats.Open();
        }

        // After DuelStage.Update (order -50) has refreshed Aim, so the input carries this frame's look.
        private void Update()
        {
            // The arena is ready for snapshots only once the stage has built it, never at Enter.
            if (!readySent && stage.IsBuilt && connection.State == DuelConnectionState.Connected)
            {
                session.MarkArenaReady();
                readySent = true;
            }
            if (session.Stage == DuelSessionStage.Playing)
            {
                var aim = stage.Aim;
                if (aim != null) session.SubmitLocalInput(input.Read(aim, session.LocalState.ActiveWeaponSlot));
                else if (!aimMissingLogged)
                {
                    // Without the camera's aim ray there is no input to send; say so once instead of standing frozen.
                    Debug.LogWarning("Duel is running but DuelStage.Aim is null: no camera aim source, so no player input is sent.");
                    aimMissingLogged = true;
                }
            }
            session.Update(Time.deltaTime);
            if (!session.HasState || session.Match.Phase != DuelPhase.MatchOver) matchEndLogged = false;
            else if (IsHost && !matchEndLogged)
            {
                var result = session.Match;
                Debug.Log($"Duel match ended: reason={result.MatchEndReason}, score={result.Score0}:{result.Score1}, round={result.RoundNumber}.");
                matchEndLogged = true;
            }
            stats?.Write(session, Time.unscaledDeltaTime);
            // The stage shows the lobby while Match is null: until there is a match to show, and again after leaving.
            stage.Match = session.HasState ? this : null;
        }

        private void OnDestroy()
        {
            stats?.Dispose();
            if (session == null) return;
            session.Leave();
            net.Dispose();
        }

        // ---------- IDuelLobbyHost ----------

        public string JoinInfo => connection?.State == DuelConnectionState.Listening ? connection.JoinInfo : string.Empty;

        public string Status
        {
            get
            {
                if (session == null) return string.Empty;
                switch (session.Stage)
                {
                    case DuelSessionStage.Connecting:
                        return net.IsServer || connection.JoinInfo.Length == 0 ? "Đang mở phòng…" : $"Đang kết nối tới {connection.JoinInfo}…";
                    case DuelSessionStage.WaitingForOpponent:
                        return $"Đang chờ đối thủ. Đưa cho bạn của bạn địa chỉ {connection.JoinInfo}.";
                    case DuelSessionStage.Preparing:
                        return "Đã kết nối. Đang chuẩn bị trận đấu…";
                    default:
                        return string.Empty;
                }
            }
        }

        public string LastError => session == null ? string.Empty : session.EndMessage.Length > 0 ? session.EndMessage : connection.LastError;

        public bool Busy => session != null && (session.Stage == DuelSessionStage.Connecting ||
                                                session.Stage == DuelSessionStage.WaitingForOpponent ||
                                                session.Stage == DuelSessionStage.Preparing);

        public void RequestHost() => connection.BeginHost();

        public void RequestJoin(string target) => connection.BeginClient(target);

        public void RequestCancel() => session.Leave();

        // ---------- IDuelHudState ----------

        private DuelMatchView View => session.Match;
        private bool IsHost => session.LocalSlot == DuelNetworkBootstrap.HostSlot;

        public DuelPhase Phase => View.Phase;

        public int Round => View.RoundNumber;
        public int RoundsToWin => DuelMatch.RoundsToWin;
        public int LocalRounds => IsHost ? View.Score0 : View.Score1;
        public int OpponentRounds => IsHost ? View.Score1 : View.Score0;
        public float RoundRemaining => View.RoundRemaining;
        public float CountdownRemaining => View.Phase == DuelPhase.Warmup ? View.PhaseRemaining : 0f;

        public DuelRoundResult LastRound
        {
            get
            {
                var view = View;
                if (view.Phase != DuelPhase.RoundOver && view.Phase != DuelPhase.MatchOver) return DuelRoundResult.None;
                if (view.RoundWinnerSlot < 0) return view.RoundEndReason == DuelRoundEndReason.None ? DuelRoundResult.None : DuelRoundResult.Draw;
                return view.RoundWinnerSlot == session.LocalSlot ? DuelRoundResult.Won : DuelRoundResult.Lost;
            }
        }

        public bool MatchWon => View.MatchWinnerSlot >= 0 && View.MatchWinnerSlot == session.LocalSlot;

        public string LocalName => "Bạn";
        public string OpponentName => "Đối thủ";
        public float LocalHealth => session.LocalState.Health;
        public float LocalMaxHealth => DuelPlayerSim.MaxHealth;
        public float OpponentHealth => session.Opponent.Health;
        public float OpponentMaxHealth => DuelPlayerSim.MaxHealth;

        private int ActiveSlot => session.LocalState.ActiveWeaponSlot;
        private bool KnifeInHand => ActiveSlot == DuelPlayerSim.KnifeSlot;

        public string WeaponId => KnifeInHand ? DuelHudView.KnifeWeaponId : DuelPlayerSim.WeaponAt(ActiveSlot).Id;
        public string WeaponName => KnifeInHand ? "Dao găm" : DuelPlayerSim.WeaponAt(ActiveSlot).DisplayName;
        public int Ammo => KnifeInHand ? 0 : session.LocalState.AmmoAt(ActiveSlot);
        public int MagazineSize => KnifeInHand ? 0 : DuelPlayerSim.WeaponAt(ActiveSlot).MagazineSize;
        public float ReloadRemaining => KnifeInHand ? 0f : session.LocalState.ReloadAt(ActiveSlot);
        public float ReloadDuration => KnifeInHand ? 0f : DuelPlayerSim.WeaponAt(ActiveSlot).ReloadSeconds;
    }
}
