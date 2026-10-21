using NUnit.Framework;
using UnityEngine;

namespace TienTuyen.Duel.Tests
{
    public sealed class DuelPredictorTests
    {
        private const float StepSeconds = 1f / 30f;

        [Test]
        public void ReconciliationReplaysUnacknowledgedInputsWithoutDivergence()
        {
            var server = InitialState();
            var predictor = new DuelPredictor(server, StepSeconds);
            var snapshot = server;
            for (int tick = 0; tick < 80; tick++)
            {
                var input = Input(tick);
                bool canAct = tick % 17 >= 4;
                Assert.That(predictor.TryPredict(input, out _, canAct), Is.True);
                DuelPlayerSim.Step(ref server, input, StepSeconds, canAct);
                if (tick == 39) snapshot = server;
            }
            var before = predictor.State;
            var correction = predictor.Reconcile(snapshot, 39, 39);
            Assert.That(correction.Applied, Is.True);
            Assert.That(correction.Diverged, Is.False);
            Assert.That(correction.PositionError, Is.Zero);
            Assert.That(correction.ReplayedInputs, Is.EqualTo(40));
            Assert.That(predictor.State, Is.EqualTo(before));
            Assert.That(predictor.State, Is.EqualTo(server));
            Assert.That(predictor.BufferedInputCount, Is.EqualTo(40));
        }

        [Test]
        public void OlderSnapshotCannotRewindThePredictedPresent()
        {
            var server = InitialState();
            var predictor = new DuelPredictor(server, StepSeconds);
            var oldSnapshot = server;
            var newerSnapshot = server;
            for (int tick = 0; tick < 12; tick++)
            {
                var input = Input(tick);
                predictor.TryPredict(input, out _);
                DuelPlayerSim.Step(ref server, input, StepSeconds);
                if (tick == 2) oldSnapshot = server;
                if (tick == 6) newerSnapshot = server;
            }
            predictor.Reconcile(newerSnapshot, 6, 6);
            var before = predictor.State;
            int buffered = predictor.BufferedInputCount;
            var ignored = predictor.Reconcile(oldSnapshot, 2, 2);
            Assert.That(ignored.Applied, Is.False);
            Assert.That(ignored.Diverged, Is.False);
            Assert.That(predictor.State, Is.EqualTo(before));
            Assert.That(predictor.BufferedInputCount, Is.EqualTo(buffered));
            Assert.That(predictor.LastAcknowledgedTick, Is.EqualTo(6));
        }

        [Test]
        public void SilentServerBoundsHistoryWithoutLosingUnacknowledgedReplay()
        {
            var server = InitialState();
            var predictor = new DuelPredictor(server, StepSeconds, capacity: 4);
            var snapshot = server;
            for (int tick = 0; tick < predictor.Capacity; tick++)
            {
                var input = Input(tick);
                Assert.That(predictor.TryPredict(input, out _), Is.True);
                DuelPlayerSim.Step(ref server, input, StepSeconds);
                if (tick == 1) snapshot = server;
            }
            var fullState = predictor.State;
            for (int tick = predictor.Capacity; tick < 1000; tick++)
                Assert.That(predictor.TryPredict(Input(tick), out _), Is.False);
            Assert.That(predictor.BufferedInputCount, Is.EqualTo(predictor.Capacity));
            Assert.That(predictor.State, Is.EqualTo(fullState));
            var correction = predictor.Reconcile(snapshot, 1, 1);
            Assert.That(correction.Diverged, Is.False);
            Assert.That(predictor.BufferedInputCount, Is.EqualTo(2));
            // Tick numbers may jump; one submitted input remains one fixed step, with no synthetic catch-up.
            var next = Input(1000);
            DuelPlayerSim.Step(ref server, next, StepSeconds);
            Assert.That(predictor.TryPredict(next, out _), Is.True);
            Assert.That(predictor.State, Is.EqualTo(server));
        }

        [Test]
        public void MissingAckReportsCorrectionAndServerAheadAdvancesTheInputFloor()
        {
            var initial = InitialState();
            var predictor = new DuelPredictor(initial, StepSeconds);
            var snapshot = initial;
            predictor.TryPredict(Input(0), out _);
            predictor.TryPredict(Input(2), out _);
            DuelPlayerSim.Step(ref snapshot, Input(0), StepSeconds);
            DuelPlayerSim.Step(ref snapshot, Input(2), StepSeconds);
            predictor.TryPredict(Input(4), out _);
            snapshot.Position.x += .5f;
            snapshot.Health -= 10;
            var expected = snapshot;
            DuelPlayerSim.Step(ref expected, Input(4), StepSeconds);
            var before = predictor.State;
            var correction = predictor.Reconcile(snapshot, 3, 3); // Ack need not match a buffered input.
            Assert.That(correction.Diverged, Is.True);
            Assert.That(correction.ReplayedInputs, Is.EqualTo(1));
            Assert.That(predictor.State, Is.EqualTo(expected));
            Assert.That(correction.PositionDelta, Is.EqualTo(expected.Position - before.Position));
            Assert.That(correction.PositionError, Is.GreaterThan(0));

            var serverAhead = InitialState();
            serverAhead.Position.x += 1;
            predictor.Reconcile(serverAhead, 100, 100);
            Assert.That(predictor.BufferedInputCount, Is.Zero);
            Assert.That(predictor.State, Is.EqualTo(serverAhead));
            Assert.That(predictor.NextInputTick, Is.EqualTo(101));
            Assert.That(predictor.TryPredict(Input(99), out _), Is.False);
            Assert.That(predictor.TryPredict(Input(101), out _), Is.True);
        }

        private static DuelSimState InitialState()
        {
            var state = DuelPlayerSim.CreateState(0);
            state.Position.z = 10;
            return state;
        }

        private static DuelInputFrame Input(int tick) => new DuelInputFrame
        {
            Tick = tick, WeaponSlot = tick < 20 ? -1 : tick < 50 ? 1 : 3,
            Move = new Vector2(.3f, 0), Yaw = tick * 2, Pitch = tick % 15,
            AimOrigin = new Vector3(-15, .88f, 10), AimDirection = Vector3.right,
            Buttons = DuelButtons.Fire | DuelButtons.Aim | (tick % 10 < 5 ? DuelButtons.Crouch : DuelButtons.None)
        };
    }
}
