using System;
using System.Collections.Generic;
using NUnit.Framework;
using TienTuyen.Combat;
using TienTuyen.Duel.Net;
using UnityEngine;

namespace TienTuyen.Duel.Tests
{
    public sealed class DuelSessionNetworkTests
    {
        private const float Frame = 1f / 120f;
        private const float FixedStep = 1f / 30f;

        [Test]
        public void BestOfFiveConvergesThroughLossJitterAndRoundResets()
            => RunMatch(.03, .15, 20);

        [Test]
        public void LanCorrectionsStayWithinTwoMissingMovementSteps()
            => RunMatch(.01, .01, 20);

        [Test]
        public void FreshSnapshotCorrectsHealthWithoutAnotherConsumedInput()
        {
            var wire = new ImpairedLink(.0005, .0005, 0);
            var client = new DuelSession(wire.Client, new Connected(), FixedStep);
            var state = DuelPlayerSim.CreateState(1);
            DuelTestArena.ClearPair(5, out state.Position, out _);
            var snapshot = new DuelOwnerSnapshot { ServerTick = 100, LastConsumedInputTick = 9, RoundGeneration = 1, CanAct = true, State = state };
            wire.Client.DeliverOwner(snapshot);
            client.SubmitLocalInput(new DuelInputFrame { WeaponSlot = -1, Move = Vector2.right });
            client.Update(FixedStep);
            var predictedPosition = client.LocalState.Position;
            snapshot.ServerTick++;
            snapshot.State.Health -= DuelPlayerSim.WeaponAt(0).DamagePerHit;
            wire.Client.DeliverOwner(snapshot);
            Assert.That(client.LocalState.Health, Is.EqualTo(snapshot.State.Health), "Health and timers can change while packet loss leaves the input ack unchanged.");
            Assert.That(client.LocalState.Position, Is.EqualTo(predictedPosition), "Fresh authority must still replay the unacknowledged local input.");
            var corrected = client.LocalState;
            snapshot.ServerTick--;
            snapshot.RoundGeneration--;
            snapshot.CanAct = false;
            snapshot.State.Health += DuelPlayerSim.WeaponAt(0).DamagePerHit;
            wire.Client.DeliverOwner(snapshot);
            Assert.That(client.LocalState.Health, Is.EqualTo(state.Health - DuelPlayerSim.WeaponAt(0).DamagePerHit),
                "An older snapshot sharing the ack must not undo the fresh health correction.");
            Assert.That(client.LocalState, Is.EqualTo(corrected), "A late old-round packet must not trigger Reset before freshness is checked.");
        }

        internal static void RunMatch(double minimumDelay, double maximumDelay, int lossEvery)
        {
            var wire = new ImpairedLink(minimumDelay, maximumDelay, lossEvery);
            var host = new DuelSession(wire.Host, new Connected(), FixedStep);
            var client = new DuelSession(wire.Client, new Connected(), FixedStep);
            var hostBot = new Bot(0);
            var clientBot = new Bot(1);
            host.MarkArenaReady(); client.MarkArenaReady();
            float maxPosition = 0, maxCorrection = 0, maxHealth = 0, maxReplay = 0, maxLiveReset = 0;
            int resets = 0, replayCorrections = 0, lastGeneration = 0, healthLagFrames = 0, maxHealthLagFrames = 0, staleHealthSnapshots = 0;
            int hitLagFrames = 0, maxHitLagFrames = 0;
            bool lastCanAct = false;
            wire.Client.OwnerDelivered = snapshot =>
            {
                var before = client.LocalState;
                wire.Client.DeliverOwner(snapshot);
                if (client.LocalState.Health != snapshot.State.Health) staleHealthSnapshots++;
                if (snapshot.RoundGeneration != lastGeneration)
                {
                    Assert.That(client.LocalState, Is.EqualTo(snapshot.State), "New round must flush old inputs before any replay.");
                    resets++; lastGeneration = snapshot.RoundGeneration;
                }
                else
                {
                    float correction = Vector3.Distance(before.Position, client.LocalState.Position);
                    maxCorrection = Mathf.Max(maxCorrection, correction);
                    if (snapshot.CanAct && !lastCanAct) maxLiveReset = Mathf.Max(maxLiveReset, correction);
                    else maxReplay = Mathf.Max(maxReplay, correction);
                    if (correction > .0001f) replayCorrections++;
                }
                lastCanAct = snapshot.CanAct;
            };

            for (int frame = 0; frame < 180 / Frame; frame++)
            {
                wire.Advance(Frame);
                host.SubmitLocalInput(hostBot.Controls(host));
                client.SubmitLocalInput(clientBot.Controls(client));
                host.Update(Frame); client.Update(Frame);
                if (client.HasState && host.Match.RoundGeneration == client.Match.RoundGeneration)
                {
                    maxHealth = Mathf.Max(maxHealth, Mathf.Abs(host.Opponent.Health - client.LocalState.Health));
                    healthLagFrames = host.Opponent.Health != client.LocalState.Health ? healthLagFrames + 1 : 0;
                    maxHealthLagFrames = Math.Max(maxHealthLagFrames, healthLagFrames);
                    hitLagFrames = Mathf.Abs(host.Opponent.Health - client.LocalState.Health) == DuelPlayerSim.WeaponAt(0).DamagePerHit ? hitLagFrames + 1 : 0;
                    maxHitLagFrames = Math.Max(maxHitLagFrames, hitLagFrames);
                    if (host.Match.Phase == DuelPhase.Live && client.Match.Phase == DuelPhase.Live)
                        maxPosition = Mathf.Max(maxPosition, Vector3.Distance(host.Opponent.Position, client.LocalState.Position));
                }
                else { healthLagFrames = 0; hitLagFrames = 0; }
                if (host.Stage == DuelSessionStage.Ended && client.Stage == DuelSessionStage.Ended) break;
            }
            Console.WriteLine($"Network match: delay={minimumDelay * 1000:F1}..{maximumDelay * 1000:F1}ms each way, loss=1/{lossEvery}, t={wire.Now:F3}s, host={host.Match.Score0}:{host.Match.Score1} {host.Match.Phase}, client={client.Match.Score0}:{client.Match.Score1} {client.Match.Phase}, position={maxPosition:F3}m correction={maxCorrection:F3}m (replay={maxReplay:F3},liveReset={maxLiveReset:F3}) health={maxHealth} for <= {maxHealthLagFrames * Frame * 1000:F1}ms (full hit={maxHitLagFrames * Frame * 1000:F1}ms), staleHealthSnapshots={staleHealthSnapshots}, resets={resets}, corrections={replayCorrections}, dropped={wire.Dropped}, reordered={wire.Reordered}, terminalDrops={wire.TerminalDrops}");
            Assert.That(host.Match.Phase, Is.EqualTo(DuelPhase.MatchOver), "Bots must finish a real match through Session input, movement and combat.");
            Assert.That(client.Match.Phase, Is.EqualTo(DuelPhase.MatchOver), "A lost result packet must not strand the joiner in a live match.");
            Assert.That(host.Match.Score0, Is.EqualTo(3));
            Assert.That(host.Match.Score1, Is.EqualTo(1));
            Assert.That(client.Match.Score0, Is.EqualTo(host.Match.Score0));
            Assert.That(client.Match.Score1, Is.EqualTo(host.Match.Score1));
            Assert.That(client.Match.MatchWinnerSlot, Is.EqualTo(host.Match.MatchWinnerSlot));
            Assert.That(client.LocalState.Health, Is.EqualTo(host.Opponent.Health));
            Assert.That(staleHealthSnapshots, Is.Zero, "Every accepted owner snapshot must immediately install authoritative health.");
            Assert.That(maxHealthLagFrames * Frame, Is.LessThanOrEqualTo(maximumDelay + 3 * FixedStep + Frame));
            Assert.That(resets, Is.EqualTo(4));
            if (lossEvery > 0) Assert.That(replayCorrections, Is.GreaterThan(0));
            if (maximumDelay - minimumDelay > FixedStep) Assert.That(wire.Reordered, Is.GreaterThan(0));
            Assert.That(wire.Dropped, Is.GreaterThan(0));
            Assert.That(wire.TerminalDrops, Is.GreaterThan(0));
            if (maximumDelay <= .01)
            {
                // This LAN trace loses at most two adjacent input packets: two 30Hz movement steps need repair.
                Assert.That(maxPosition, Is.LessThanOrEqualTo(CombatRules.PlayerSpeed * (.02f + 3 * FixedStep)));
                Assert.That(maxCorrection, Is.LessThanOrEqualTo(CombatRules.PlayerSpeed * 2 * FixedStep + .0001f));
            }
            else
            {
                // Stress margin: 300ms round trip + two fixed steps at 4.75m/s = 1.742m of prediction lead.
                float leadBound = CombatRules.PlayerSpeed * (.3f + 2 * FixedStep);
                Assert.That(maxPosition, Is.LessThanOrEqualTo(leadBound));
                Assert.That(maxCorrection, Is.LessThanOrEqualTo(leadBound));
            }
            // A 350ms rifle cooldown exceeds the expected snapshot gap: at most one unseen 24HP hit.
            Assert.That(maxHealth, Is.LessThanOrEqualTo(DuelPlayerSim.WeaponAt(0).DamagePerHit));
        }

        private sealed class Bot
        {
            private readonly int slot;
            private readonly Vector2 goal, otherGoal;
            private Vector2[] route;
            private int generation, waypoint;

            internal Bot(int slot)
            {
                this.slot = slot;
                DuelTestArena.FiringPositions(out var a, out var b);
                goal = slot == 0 ? a : b;
                otherGoal = slot == 0 ? b : a;
            }

            internal DuelInputFrame Controls(DuelSession session)
            {
                if (!session.HasState) return new DuelInputFrame { WeaponSlot = -1 };
                var state = session.LocalState;
                if (generation != session.Match.RoundGeneration)
                {
                    generation = session.Match.RoundGeneration; waypoint = 0;
                    route = DuelTestArena.Route(DuelPlayerSim.CreateState(slot).Position, goal);
                }
                var at = new Vector2(state.Position.x, state.Position.z);
                while (route != null && waypoint < route.Length && (route[waypoint] - at).sqrMagnitude < .01f * .01f) waypoint++;
                Vector2 next = route != null && waypoint < route.Length ? route[waypoint] : goal;
                Vector2 opponent = new Vector2(session.Opponent.Position.x, session.Opponent.Position.z);
                bool ready = (goal - at).magnitude < .2f && (otherGoal - opponent).magnitude < .2f;
                int winner = session.Match.RoundNumber == 2 ? 1 : 0;
                return new DuelInputFrame
                {
                    WeaponSlot = -1, Move = Vector2.ClampMagnitude((next - at) / (CombatRules.PlayerSpeed * FixedStep), 1),
                    AimOrigin = DuelPlayerSim.MuzzlePosition(state), AimDirection = (session.Opponent.Position - state.Position).normalized,
                    Yaw = slot == 0 ? 90 : -90,
                    Buttons = ready && slot == winner && session.Match.Phase == DuelPhase.Live ? DuelButtons.Fire : DuelButtons.None
                };
            }
        }

        // One unreliable-sequenced packet per direction/update, matching NGO's batched snapshot messages.
        // Configured latency and loss, plus one explicitly lost match-result packet on every trace.
        private sealed class ImpairedLink
        {
            private sealed class Packet
            {
                internal int Direction, Sequence;
                internal double Due;
                internal bool Terminal;
                internal readonly List<Action> Messages = new List<Action>();
            }
            private readonly Packet[] outgoing = new Packet[2];
            private readonly List<Packet> packets = new List<Packet>();
            private readonly int[] sent = new int[2], received = new int[2];
            private readonly bool[] ready = new bool[2];
            private uint random = 0x65d1a33;
            private readonly double minimumDelay, maximumDelay;
            private readonly int lossEvery;
            internal readonly Endpoint Host, Client;
            internal double Now;
            internal int Dropped, Reordered, TerminalDrops;

            internal ImpairedLink(double minimumDelay, double maximumDelay, int lossEvery)
            {
                this.minimumDelay = minimumDelay; this.maximumDelay = maximumDelay; this.lossEvery = lossEvery;
                Host = new Endpoint(this, 0); Client = new Endpoint(this, 1);
            }
            internal void Advance(float dt)
            {
                for (int direction = 0; direction < outgoing.Length; direction++)
                {
                    var packet = outgoing[direction];
                    if (packet == null) continue;
                    outgoing[direction] = null;
                    if (lossEvery > 0 && Next() % lossEvery == 0 || packet.Terminal && TerminalDrops == 0)
                    {
                        Dropped++;
                        if (packet.Terminal) TerminalDrops++;
                        continue;
                    }
                    packet.Due = Now + minimumDelay + (Next() % 12001) / 12000.0 * (maximumDelay - minimumDelay);
                    packets.Add(packet);
                }
                Now += dt;
                packets.Sort((a, b) => a.Due.CompareTo(b.Due));
                while (packets.Count > 0 && packets[0].Due <= Now)
                {
                    var packet = packets[0]; packets.RemoveAt(0);
                    if (packet.Sequence <= received[packet.Direction]) { Reordered++; continue; }
                    received[packet.Direction] = packet.Sequence;
                    foreach (var message in packet.Messages) message();
                }
            }
            private uint Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return random; }
            private void Enqueue(int direction, Action message, bool terminal = false)
            {
                var packet = outgoing[direction];
                if (packet == null) outgoing[direction] = packet = new Packet { Direction = direction, Sequence = ++sent[direction] };
                packet.Messages.Add(message); packet.Terminal |= terminal;
            }

            internal sealed class Endpoint : IDuelSessionLink
            {
                private readonly ImpairedLink link;
                private readonly int slot;
                internal Endpoint(ImpairedLink link, int slot) { this.link = link; this.slot = slot; }
                public bool IsServer => slot == 0;
                public int LocalSlot => slot;
                public bool BothReady => link.ready[0] && link.ready[1];
                public event Action<int> SlotJoined { add { } remove { } }
                public event Action<int> SlotLeft { add { } remove { } }
                public event Action<int, DuelInputFrame> InputReceived;
                public event Action<DuelOwnerSnapshot> OwnerSnapshotReceived;
                public event Action<DuelOpponentView> OpponentViewReceived;
                public event Action<DuelMatchView> MatchViewReceived;
                internal Action<DuelOwnerSnapshot> OwnerDelivered;
                public void MarkLocalReady() => link.ready[slot] = true;
                public void SendInput(in DuelInputFrame frame)
                {
                    var copy = frame;
                    if (IsServer) InputReceived?.Invoke(0, copy);
                    else link.Enqueue(1, () => link.Host.InputReceived?.Invoke(1, copy));
                }
                public bool SendOwnerSnapshot(int toSlot, in DuelOwnerSnapshot snapshot)
                {
                    if (!CanSend(toSlot)) return false;
                    var copy = snapshot;
                    link.Enqueue(0, () =>
                    {
                        if (link.Client.OwnerDelivered != null) link.Client.OwnerDelivered(copy);
                        else link.Client.DeliverOwner(copy);
                    });
                    return true;
                }
                internal void DeliverOwner(DuelOwnerSnapshot snapshot) => OwnerSnapshotReceived?.Invoke(snapshot);
                public bool SendOpponentView(int toSlot, in DuelOpponentView view)
                {
                    if (!CanSend(toSlot)) return false;
                    var copy = view; link.Enqueue(0, () => link.Client.OpponentViewReceived?.Invoke(copy)); return true;
                }
                public bool SendMatchView(int toSlot, in DuelMatchView view)
                {
                    if (!CanSend(toSlot)) return false;
                    var copy = view; link.Enqueue(0, () => link.Client.MatchViewReceived?.Invoke(copy), copy.Phase == DuelPhase.MatchOver); return true;
                }
                private bool CanSend(int toSlot) => IsServer && toSlot == 1 && link.ready[1];
            }
        }

        private sealed class Connected : IDuelConnection
        {
            public DuelConnectionState State { get; private set; } = DuelConnectionState.Connected;
            public string JoinInfo => "virtual";
            public string LastError => string.Empty;
            public event Action<DuelConnectionState> StateChanged;
            public void BeginHost() { }
            public void BeginClient(string target) { }
            public void Tick() { }
            public void Shutdown() { State = DuelConnectionState.Offline; StateChanged?.Invoke(State); }
        }
    }
}
