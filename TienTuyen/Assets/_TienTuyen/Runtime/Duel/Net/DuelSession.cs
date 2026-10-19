using System;

namespace TienTuyen.Duel.Net
{
    public enum DuelSessionStage
    {
        /// <summary>No session; the lobby offers Host and Join.</summary>
        Idle,
        Connecting,
        /// <summary>Hosting with nobody joined yet; show <see cref="IDuelConnection.JoinInfo"/>.</summary>
        WaitingForOpponent,
        /// <summary>Both connected, waiting for both arenas to be built and the first state to arrive.</summary>
        Preparing,
        Playing,
        /// <summary>The match is decided or the session failed; <see cref="DuelSession.EndMessage"/> says why.</summary>
        Ended
    }

    /// <summary>
    /// Owns one running duel. The host runs the authoritative <see cref="DuelServerLoop"/> for both slots; the
    /// joiner predicts its own body with <see cref="DuelPredictor"/> and shows the opponent and the match as the
    /// server last sent them. Call <see cref="Update"/> every frame, <see cref="SubmitLocalInput"/> with the
    /// player's controls, and <see cref="MarkArenaReady"/> once the arena exists; the match starts only when
    /// both machines have done so.
    /// </summary>
    public sealed class DuelSession
    {
        public const float DefaultFixedDeltaTime = 1f / 30f;

        // A long hitch must not turn into a burst of catch-up steps; time beyond this is dropped.
        private const int MaxStepsPerUpdate = 5;

        internal const string OpponentLeftMessage = "Đối thủ đã rời trận. Bạn thắng vì đối thủ bỏ cuộc.";

        private readonly IDuelSessionLink m_Net;
        private readonly float m_FixedDeltaTime;
        private float m_Accumulator;
        private int m_LocalTick;
        private DuelInputFrame m_LatestInput;
        private DuelButtons m_HeldSinceStep;
        private int m_PendingWeaponSlot = -1;
        private DuelSimState m_PreviousLocal;

        // Host.
        private DuelServerLoop m_Loop;
        private int m_LatestJoinerInputTick = -1;

        // Joiner.
        private DuelPredictor m_Predictor;
        private int m_PredictedGeneration;
        private int m_LastSnapshotTick;
        // Per-tick replay flag from the owner snapshot; the HUD's phase comes from Match, not from this.
        private bool m_CanAct;
        private bool m_HasOpponent, m_HasMatch;
        private DuelOpponentView m_Opponent;
        private DuelMatchView m_Match;

        public DuelSession(IDuelSessionLink net, IDuelConnection connection, float fixedDeltaTime = DefaultFixedDeltaTime)
        {
            if (!(fixedDeltaTime > 0f)) throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));
            m_Net = net;
            Connection = connection;
            m_FixedDeltaTime = fixedDeltaTime;
            m_Net.InputReceived += OnInputReceived;
            m_Net.OwnerSnapshotReceived += OnOwnerSnapshot;
            m_Net.OpponentViewReceived += view => { m_Opponent = view; m_HasOpponent = true; };
            m_Net.MatchViewReceived += view => { m_Match = view; m_HasMatch = true; };
            m_Net.SlotLeft += OnSlotLeft;
            m_Net.SlotJoined += OnSlotJoined;
            Connection.StateChanged += OnConnectionStateChanged;
        }

        public IDuelConnection Connection { get; }

        public DuelSessionStage Stage { get; private set; }

        /// <summary>Why the session ended, ready to show; empty while it runs.</summary>
        public string EndMessage { get; private set; } = string.Empty;

        /// <summary>0 on the host, 1 on the joiner, -1 with no session.</summary>
        public int LocalSlot => m_Net.LocalSlot;

        /// <summary>True once there is a body and a match to show.</summary>
        public bool HasState => m_Loop != null || (m_Predictor != null && m_HasOpponent && m_HasMatch);

        /// <summary>This player's body: authoritative on the host, predicted on the joiner.</summary>
        public DuelSimState LocalState => m_Loop != null ? m_Loop.GetState(DuelNetworkBootstrap.HostSlot) : m_Predictor?.State ?? default;

        /// <summary>
        /// <see cref="LocalState"/> one fixed step earlier. Draw the body at
        /// Lerp(PreviousLocalState, LocalState, <see cref="LocalStepFraction"/>) so it moves smoothly between steps.
        /// </summary>
        public DuelSimState PreviousLocalState => HasState ? m_PreviousLocal : LocalState;

        /// <summary>How far this frame is past the last fixed step, 0 to 1.</summary>
        public float LocalStepFraction => Math.Min(m_Accumulator / m_FixedDeltaTime, 1f);

        public DuelOpponentView Opponent =>
            m_Loop != null ? OpponentViewOf(DuelNetworkBootstrap.JoinerSlot) : m_Opponent;

        public DuelMatchView Match => m_Loop != null ? DuelMatchView.From(m_Loop.Match, m_Loop.ServerTick) : m_Match;

        /// <summary>Host only: the last authoritative step, for hit and shot effects. Default on the joiner.</summary>
        public DuelServerStepResult LastServerStep { get; private set; }

        // Diagnostics, read by the duel stats log.

        /// <summary>Joiner: owner snapshots accepted so far.</summary>
        public int SnapshotsReceived { get; private set; }

        /// <summary>Joiner: the last correction against an owner snapshot, and how many inputs it replayed.</summary>
        public DuelReconciliationResult LastReconciliation { get; private set; }

        /// <summary>Host: joiner inputs received but not yet consumed by the server loop.</summary>
        public int JoinerInputBacklog => m_Loop == null ? 0 :
            Math.Max(0, unchecked(m_LatestJoinerInputTick - m_Loop.GetLastConsumedInputTick(DuelNetworkBootstrap.JoinerSlot)));

        /// <summary>Host: server steps that found no joiner input queued, so the joiner stood idle on the server.</summary>
        public int JoinerStarvedSteps { get; private set; }

        /// <summary>The player's controls this frame. Taps between two fixed steps are kept, not lost.</summary>
        public void SubmitLocalInput(in DuelInputFrame input)
        {
            m_LatestInput = input;
            m_HeldSinceStep |= input.Buttons;
            if (input.WeaponSlot >= 0) m_PendingWeaponSlot = input.WeaponSlot;
        }

        /// <summary>Call once this machine's arena is built.</summary>
        public void MarkArenaReady() => m_Net.MarkLocalReady();

        public void Leave()
        {
            Connection.Shutdown();
            ResetMatch();
            EndMessage = string.Empty;
            Stage = DuelSessionStage.Idle;
        }

        public void Update(float deltaTime)
        {
            Connection.Tick();
            if (Stage == DuelSessionStage.Ended && Connection.State == DuelConnectionState.Failed) return;

            if (m_Net.IsServer && m_Loop == null && m_Net.BothReady) StartHostMatch();

            if (m_Loop != null || m_Predictor != null)
            {
                m_Accumulator += deltaTime;
                int steps = 0;
                while (m_Accumulator >= m_FixedDeltaTime && steps < MaxStepsPerUpdate)
                {
                    m_Accumulator -= m_FixedDeltaTime;
                    FixedStep();
                    steps++;
                }
                if (steps == MaxStepsPerUpdate) m_Accumulator = 0f;
            }
            Stage = ComputeStage();
        }

        private void FixedStep()
        {
            DuelInputFrame input = m_LatestInput;
            input.Buttons = m_HeldSinceStep;
            input.WeaponSlot = m_PendingWeaponSlot;
            input.Tick = ++m_LocalTick;
            m_HeldSinceStep = m_LatestInput.Buttons;
            m_PendingWeaponSlot = -1;
            m_PreviousLocal = LocalState;

            if (m_Loop != null)
            {
                // Loops back as slot 0 through OnInputReceived.
                m_Net.SendInput(input);
                // A decided match stops stepping but keeps sending its final state every step: snapshots are
                // unreliable, and the joiner must not be left playing a round the host has already ended.
                int consumed = m_Loop.GetLastConsumedInputTick(DuelNetworkBootstrap.JoinerSlot);
                bool stepping = m_Loop.Match.Phase != DuelPhase.MatchOver;
                LastServerStep = stepping ? m_Loop.Step() : default;
                if (stepping && m_LatestJoinerInputTick >= 0 &&
                    m_Loop.GetLastConsumedInputTick(DuelNetworkBootstrap.JoinerSlot) == consumed) JoinerStarvedSteps++;
                SendSnapshots();
                return;
            }

            m_Predictor.TryPredict(input, out _, m_CanAct);
            m_Net.SendInput(input);
        }

        private void StartHostMatch()
        {
            ResetMatch();
            m_Loop = new DuelServerLoop(m_FixedDeltaTime);
            EndMessage = string.Empty;
            SendSnapshots();
        }

        private void SendSnapshots()
        {
            const int joiner = DuelNetworkBootstrap.JoinerSlot;
            if (!m_Loop.IsConnected(joiner)) return;
            m_Net.SendOwnerSnapshot(joiner, new DuelOwnerSnapshot
            {
                ServerTick = m_Loop.ServerTick,
                LastConsumedInputTick = m_Loop.GetLastConsumedInputTick(joiner),
                RoundGeneration = m_Loop.Match.RoundGeneration,
                CanAct = m_Loop.Match.CanAct,
                State = m_Loop.GetState(joiner)
            });
            m_Net.SendOpponentView(joiner, OpponentViewOf(DuelNetworkBootstrap.HostSlot));
            m_Net.SendMatchView(joiner, DuelMatchView.From(m_Loop.Match, m_Loop.ServerTick));
        }

        private DuelOpponentView OpponentViewOf(int slot) =>
            DuelOpponentView.From(m_Loop.GetState(slot), m_Loop.ServerTick);

        private void OnInputReceived(int slot, DuelInputFrame input)
        {
            if (slot == DuelNetworkBootstrap.JoinerSlot && (m_LatestJoinerInputTick < 0 || unchecked(input.Tick - m_LatestJoinerInputTick) > 0))
                m_LatestJoinerInputTick = input.Tick;
            m_Loop?.TryQueueInput(slot, input);
        }

        private void OnOwnerSnapshot(DuelOwnerSnapshot snapshot)
        {
            // A late snapshot must not flip CanAct or the round back; the predictor orders by ServerTick too.
            if (m_Predictor != null && unchecked(snapshot.ServerTick - m_LastSnapshotTick) <= 0) return;
            m_LastSnapshotTick = snapshot.ServerTick;
            SnapshotsReceived++;
            bool wentLive = snapshot.CanAct && !m_CanAct;
            m_CanAct = snapshot.CanAct;
            if (m_Predictor == null)
            {
                m_Predictor = new DuelPredictor(snapshot.State, m_FixedDeltaTime, initialTick: snapshot.LastConsumedInputTick,
                    initialServerTick: snapshot.ServerTick);
                m_PreviousLocal = snapshot.State;
            }
            else if (snapshot.RoundGeneration != m_PredictedGeneration || wentLive)
            {
                // A new round respawned both bodies and dropped queued input, or the round just went live and
                // the buffered inputs were predicted as frozen: replaying either would snap the body, so take the
                // server's state flat. Nothing is lost; frozen inputs could not act anyway.
                m_Predictor.Reset(snapshot.State, snapshot.LastConsumedInputTick, snapshot.ServerTick);
                // A respawn is a teleport, not a move to draw in between.
                m_PreviousLocal = snapshot.State;
            }
            else
            {
                LastReconciliation = m_Predictor.Reconcile(snapshot.State, snapshot.LastConsumedInputTick, snapshot.ServerTick);
                return;
            }
            m_PredictedGeneration = snapshot.RoundGeneration;
            // Keep sending ticks the server has not consumed yet.
            if (unchecked(snapshot.LastConsumedInputTick - m_LocalTick) > 0) m_LocalTick = snapshot.LastConsumedInputTick;
        }

        private void OnSlotJoined(int slot)
        {
            // A new opponent after a decided match gets a fresh one once both are ready again.
            if (slot == DuelNetworkBootstrap.JoinerSlot && m_Loop != null && m_Loop.Match.Phase == DuelPhase.MatchOver)
                ResetMatch();
        }

        private void OnSlotLeft(int slot)
        {
            if (m_Loop == null) return;
            bool decided = m_Loop.Match.Phase == DuelPhase.MatchOver;
            // Ends the match at once with the host as winner (MatchEndReason.Forfeit); no round wins are made up.
            m_Loop.Disconnect(slot);
            if (!decided) EndMessage = OpponentLeftMessage;
        }

        private void OnConnectionStateChanged(DuelConnectionState state)
        {
            if (state == DuelConnectionState.Failed)
            {
                ResetMatch();
                EndMessage = Connection.LastError;
                Stage = DuelSessionStage.Ended;
            }
            else if (state == DuelConnectionState.Offline)
            {
                ResetMatch();
            }
        }

        private DuelSessionStage ComputeStage()
        {
            switch (Connection.State)
            {
                case DuelConnectionState.Offline: return DuelSessionStage.Idle;
                case DuelConnectionState.Starting: return DuelSessionStage.Connecting;
                case DuelConnectionState.Failed: return DuelSessionStage.Ended;
            }
            if (m_Loop != null)
            {
                if (m_Loop.Match.Phase == DuelPhase.MatchOver) return DuelSessionStage.Ended;
                return Connection.State == DuelConnectionState.Listening ? DuelSessionStage.WaitingForOpponent : DuelSessionStage.Playing;
            }
            if (Connection.State == DuelConnectionState.Listening) return DuelSessionStage.WaitingForOpponent;
            if (!HasState) return DuelSessionStage.Preparing;
            return m_Match.Phase == DuelPhase.MatchOver ? DuelSessionStage.Ended : DuelSessionStage.Playing;
        }

        private void ResetMatch()
        {
            m_Loop = null;
            m_Predictor = null;
            m_HasOpponent = m_HasMatch = false;
            m_Opponent = default;
            m_Match = default;
            m_CanAct = false;
            m_PredictedGeneration = 0;
            m_LastSnapshotTick = 0;
            m_Accumulator = 0f;
            m_PreviousLocal = default;
            LastServerStep = default;
        }
    }
}
