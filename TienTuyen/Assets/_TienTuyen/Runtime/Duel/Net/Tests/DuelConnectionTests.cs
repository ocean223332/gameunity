using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TienTuyen.Duel.Net;
using UnityEngine;
using UnityEngine.TestTools;

namespace TienTuyen.Duel.Net.Tests
{
    /// <summary>Real UnityTransport sessions over 127.0.0.1, host and joiners in one process.</summary>
    public sealed class DuelConnectionTests
    {
        private static ushort s_Port = 7790;
        private const float Timeout = 15f;

        private readonly List<(DuelNetworkBootstrap Net, LanDuelConnection Connection)> m_Peers = new List<(DuelNetworkBootstrap, LanDuelConnection)>();

        private ushort Port;

        [SetUp]
        public void SetUp() => Port = ++s_Port;

        private readonly List<DuelSession> m_Sessions = new List<DuelSession>();

        [TearDown]
        public void TearDown()
        {
            m_Sessions.Clear();
            foreach (var peer in m_Peers)
            {
                peer.Connection.Shutdown();
                peer.Net.Dispose();
            }
            m_Peers.Clear();
        }

        [UnityTest]
        public IEnumerator HostAndJoinerConnect_ThenCarrySimValuesOnceReady()
        {
            var host = Peer("Host");
            var joiner = Peer("Joiner");
            host.Connection.BeginHost();
            Assert.AreEqual(DuelConnectionState.Listening, host.Connection.State, host.Connection.LastError);
            // The OS route to the internet, not adapter enumeration: what the implementation must agree with.
            string routed = RoutedLocalAddress();
            if (routed != null) Assert.AreEqual(routed + ":" + Port, host.Connection.JoinInfo);
            else StringAssert.EndsWith(":" + Port, host.Connection.JoinInfo);

            joiner.Connection.BeginClient("127.0.0.1:" + Port);
            yield return Until(() => host.Connection.State == DuelConnectionState.Connected && joiner.Connection.State == DuelConnectionState.Connected);
            Assert.AreEqual(DuelNetworkBootstrap.HostSlot, host.Net.LocalSlot);
            Assert.AreEqual(DuelNetworkBootstrap.JoinerSlot, joiner.Net.LocalSlot);

            DuelOwnerSnapshot? owner = null;
            joiner.Net.Channel.OwnerSnapshotReceived += s => owner = s;
            var sent = new DuelOwnerSnapshot { ServerTick = 41, LastConsumedInputTick = 39, State = DuelPlayerSim.CreateState(1) };
            sent.State.Health = 63.25f;
            sent.State.Reload2 = 0.125f;
            Assert.IsFalse(host.Net.Channel.SendOwnerSnapshot(DuelNetworkBootstrap.JoinerSlot, sent), "snapshots wait for the joiner's ready");

            host.Net.MarkLocalReady();
            joiner.Net.MarkLocalReady();
            yield return Until(() => host.Net.BothReady);

            var inputs = new List<(int Slot, DuelInputFrame Frame)>();
            host.Net.Channel.InputReceived += (slot, frame) => inputs.Add((slot, frame));
            var input = new DuelInputFrame
            {
                Tick = 40, Move = new Vector2(0.6f, -0.8f), Yaw = 91.5f, Pitch = -3.25f,
                AimOrigin = new Vector3(-15f, 2.2f, 0.1f), AimDirection = new Vector3(1f, -0.05f, 0f).normalized,
                Buttons = DuelButtons.Fire | DuelButtons.Aim, WeaponSlot = -1
            };
            joiner.Net.Channel.SendInput(input);
            host.Net.Channel.SendInput(new DuelInputFrame { Tick = 7 });
            Assert.IsTrue(host.Net.Channel.SendOwnerSnapshot(DuelNetworkBootstrap.JoinerSlot, sent));
            yield return Until(() => owner.HasValue && inputs.Count == 2);

            Assert.AreEqual((0, 7), (inputs[0].Slot, inputs[0].Frame.Tick), "the host's own input loops back as slot 0");
            Assert.AreEqual(DuelNetworkBootstrap.JoinerSlot, inputs[1].Slot);
            Assert.AreEqual(input, inputs[1].Frame);
            Assert.AreEqual(sent, owner.Value);
        }

        [UnityTest]
        public IEnumerator ThirdConnectionIsRefused_AndTheDuelKeepsGoing()
        {
            var host = Peer("Host");
            var joiner = Peer("Joiner");
            var third = Peer("Third");
            host.Connection.BeginHost();
            joiner.Connection.BeginClient("127.0.0.1:" + Port);
            yield return Until(() => joiner.Connection.State == DuelConnectionState.Connected);

            third.Connection.BeginClient("127.0.0.1:" + Port);
            yield return Until(() => third.Connection.State == DuelConnectionState.Failed);
            Assert.AreEqual(DuelNetworkBootstrap.FullReason, third.Connection.LastError);

            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(DuelConnectionState.Connected, joiner.Connection.State);
            Assert.AreEqual(DuelConnectionState.Connected, host.Connection.State);
        }

        [UnityTest]
        public IEnumerator NobodyListening_FailsWithAReason()
        {
            var joiner = Peer("Joiner");
            LogAssert.Expect(LogType.Error, new Regex("Failed to connect to server"));
            joiner.Connection.BeginClient("127.0.0.1:" + Port);
            Assert.AreEqual(DuelConnectionState.Starting, joiner.Connection.State);
            yield return Until(() => joiner.Connection.State == DuelConnectionState.Failed);
            StringAssert.Contains("127.0.0.1:" + Port, joiner.Connection.LastError);
        }

        [UnityTest]
        public IEnumerator Session_JoinerInputMovesItsBodyOnBothMachines_ThenLeavingForfeits()
        {
            var host = Peer("Host");
            var joiner = Peer("Joiner");
            var hostSession = new DuelSession(host.Net, host.Connection);
            var joinerSession = new DuelSession(joiner.Net, joiner.Connection);
            m_Sessions.Add(hostSession);
            m_Sessions.Add(joinerSession);
            host.Connection.BeginHost();
            joiner.Connection.BeginClient("127.0.0.1:" + Port);
            yield return Until(() => joiner.Connection.State == DuelConnectionState.Connected);

            hostSession.MarkArenaReady();
            joinerSession.MarkArenaReady();
            yield return Until(() => hostSession.Match.Phase == DuelPhase.Live && joinerSession.HasState && joinerSession.Match.Phase == DuelPhase.Live);
            Assert.AreEqual(DuelSessionStage.Playing, hostSession.Stage);
            Assert.AreEqual(DuelSessionStage.Playing, joinerSession.Stage);

            Vector3 spawn = DuelArenaLayout.Spawn(DuelNetworkBootstrap.JoinerSlot);
            var toCentre = new DuelInputFrame
            {
                Move = new Vector2(-1f, 0f), Yaw = DuelArenaLayout.SpawnYaw(1), WeaponSlot = -1,
                AimOrigin = spawn, AimDirection = Vector3.left
            };
            float until = Time.realtimeSinceStartup + 1f;
            yield return Until(() =>
            {
                joinerSession.SubmitLocalInput(toCentre);
                return Time.realtimeSinceStartup > until;
            });
            var stand = toCentre;
            stand.Move = Vector2.zero;
            joinerSession.SubmitLocalInput(stand);
            yield return Until(() => Mathf.Abs(hostSession.Opponent.Position.x - joinerSession.LocalState.Position.x) < 0.25f);
            Assert.Less(hostSession.Opponent.Position.x, spawn.x - 1f, "the host stepped the joiner's input");
            Assert.Less(joinerSession.LocalState.Position.x, spawn.x - 1f, "the joiner predicted its own move");

            joinerSession.Leave();
            yield return Until(() => hostSession.Stage == DuelSessionStage.Ended);
            Assert.AreEqual(DuelMatchEndReason.Forfeit, hostSession.Match.MatchEndReason);
            Assert.AreEqual(DuelNetworkBootstrap.HostSlot, hostSession.Match.MatchWinnerSlot);
            Assert.AreEqual(0, hostSession.Match.Score0, "no round wins are made up");
            Assert.IsNotEmpty(hostSession.EndMessage);
        }

        private (DuelNetworkBootstrap Net, LanDuelConnection Connection) Peer(string name)
        {
            var net = new DuelNetworkBootstrap("Duel" + name);
            var peer = (net, new LanDuelConnection(net, Port));
            m_Peers.Add(peer);
            return peer;
        }

        private static string RoutedLocalAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 53);
                return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
            }
            catch (SocketException)
            {
                return null;
            }
        }

        private IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + Timeout;
            while (!condition())
            {
                if (m_Sessions.Count == 0)
                    foreach (var peer in m_Peers) peer.Connection.Tick();
                foreach (var session in m_Sessions) session.Update(Time.unscaledDeltaTime);
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail("timed out; " + string.Join(", ", m_Peers.ConvertAll(p => p.Connection.State + " " + p.Connection.LastError)));
                yield return null;
            }
        }
    }
}
