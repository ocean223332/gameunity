using NUnit.Framework;
using TienTuyen.Combat;
using UnityEngine;

namespace TienTuyen.Duel.Tests
{
    public sealed class DuelServerLoopTests
    {
        [Test]
        public void WarmupSuppressesFireAndDamageForBothSlots()
        {
            var loop = new DuelServerLoop(match: new DuelMatch(1, 300, 3));
            var before0 = loop.GetState(0);
            var before1 = loop.GetState(1);
            loop.TryQueueInput(0, Input(loop, 0, 0, Vector2.up, true));
            loop.TryQueueInput(1, Input(loop, 1, 0, Vector2.up, true));
            var result = loop.Step();
            Assert.That(loop.Match.Phase, Is.EqualTo(DuelPhase.Warmup));
            // Empty attacks prove the gate works even when spawn distance or cover would also cause a miss.
            Assert.That(result.Player0.Attack.Kind, Is.EqualTo(DuelAttackKind.None));
            Assert.That(result.Player1.Attack.Kind, Is.EqualTo(DuelAttackKind.None));
            Assert.That(result.Hit0.Damage + result.Hit1.Damage, Is.Zero);
            Assert.That(loop.GetState(0).Health, Is.EqualTo(before0.Health));
            Assert.That(loop.GetState(1).Health, Is.EqualTo(before1.Health));
            Assert.That(loop.GetState(0).Ammo0, Is.EqualTo(before0.Ammo0));
            Assert.That(loop.GetState(1).Ammo0, Is.EqualTo(before1.Ammo0));
        }

        [Test]
        public void FullRoundAwardsWinnerCountsActualDamageAndRespawnsCleanBodies()
        {
            var loop = new DuelServerLoop(match: new DuelMatch(0, 300, .05f));
            loop.Step();
            int tick = 0;
            Approach(loop, ref tick);
            for (int i = 0; i < 200 && loop.Match.CanAct; i++)
                ShootStep(loop, ref tick, true, false);
            Assert.That(loop.Match.Score0, Is.EqualTo(1));
            Assert.That(loop.Match.RoundWinnerSlot, Is.EqualTo(0));
            Assert.That(loop.GetState(1).Dead, Is.True);
            Assert.That(loop.Match.DamageDealt0, Is.EqualTo(DuelPlayerSim.MaxHealth));
            int lastConsumed = loop.GetLastConsumedInputTick(0);
            Assert.That(lastConsumed, Is.EqualTo(tick - 1));
            bool respawned = false;
            for (int i = 0; i < 10 && !respawned; i++) respawned = loop.Step().Respawned;
            Assert.That(respawned, Is.True);
            Assert.That(loop.GetState(0), Is.EqualTo(DuelPlayerSim.CreateState(0)));
            Assert.That(loop.GetState(1), Is.EqualTo(DuelPlayerSim.CreateState(1)));
            Assert.That(loop.GetLastConsumedInputTick(0), Is.EqualTo(lastConsumed));
        }

        [Test]
        public void SimultaneousTradeScoresNobodyAndConsumesOneRound()
        {
            var loop = new DuelServerLoop(match: new DuelMatch(0, 300, 3));
            loop.Step();
            int tick = 0;
            Approach(loop, ref tick);
            DuelServerStepResult final = default;
            for (int i = 0; i < 200 && loop.Match.CanAct; i++)
                final = ShootStep(loop, ref tick, true, true);
            Assert.That(final.Hit0.Damage, Is.GreaterThan(0));
            Assert.That(final.Hit1.Damage, Is.GreaterThan(0));
            Assert.That(loop.GetState(0).Dead && loop.GetState(1).Dead, Is.True);
            Assert.That(loop.Match.Score0 + loop.Match.Score1, Is.Zero);
            Assert.That(loop.Match.CompletedRounds, Is.EqualTo(1));
            Assert.That(loop.Match.RoundEndReason, Is.EqualTo(DuelRoundEndReason.DoubleKnockout));
            Assert.That(loop.Match.DamageDealt0, Is.EqualTo(DuelPlayerSim.MaxHealth));
            Assert.That(loop.Match.DamageDealt1, Is.EqualTo(DuelPlayerSim.MaxHealth));
        }

        [Test]
        public void MissingInputIsIdleAndDoesNotStallTheOtherPlayerOrGravity()
        {
            var loop = new DuelServerLoop(match: new DuelMatch(0, 300, 3));
            loop.Step();
            var first = Input(loop, 0, 0, Vector2.up, true);
            first.Buttons |= DuelButtons.Jump;
            loop.TryQueueInput(0, first);
            loop.Step();
            var missing = loop.GetState(0);
            var other = loop.GetState(1);
            Assert.That(missing.Position.y, Is.GreaterThan(DuelArenaLayout.GroundY));
            for (int tick = 1; tick <= 90; tick++)
            {
                Assert.That(loop.TryQueueInput(1, Input(loop, 1, tick, Vector2.up, false)), Is.True);
                loop.Step();
            }
            Assert.That(loop.GetState(0).Position.x, Is.EqualTo(missing.Position.x));
            Assert.That(loop.GetState(0).Position.z, Is.EqualTo(missing.Position.z));
            Assert.That(loop.GetState(0).Position.y, Is.EqualTo(DuelArenaLayout.GroundY));
            Assert.That(loop.GetState(0).Ammo0, Is.EqualTo(missing.Ammo0));
            Assert.That(loop.GetState(1).Position.z, Is.GreaterThan(other.Position.z));
            Assert.That(loop.GetLastConsumedInputTick(0), Is.Zero);
            Assert.That(loop.GetLastConsumedInputTick(1), Is.EqualTo(90));
        }

        [Test]
        public void DisconnectForfeitsMatchWithoutInventingRoundWins()
        {
            var loop = new DuelServerLoop(match: new DuelMatch(0, 300, 3));
            loop.Step();
            loop.TryQueueInput(1, Input(loop, 1, 0, Vector2.up, true));
            loop.Disconnect(1);
            Assert.That(loop.Match.Phase, Is.EqualTo(DuelPhase.MatchOver));
            loop.Step();
            Assert.That(loop.Match.Phase, Is.EqualTo(DuelPhase.MatchOver));
            Assert.That(loop.Match.MatchWinnerSlot, Is.EqualTo(0));
            Assert.That(loop.Match.MatchEndReason, Is.EqualTo(DuelMatchEndReason.Forfeit));
            loop.Disconnect(0);
            Assert.That(loop.Match.MatchWinnerSlot, Is.EqualTo(0));
            Assert.That(loop.Match.Score0 + loop.Match.Score1, Is.Zero);
            Assert.That(loop.Match.CompletedRounds, Is.Zero);
            Assert.That(loop.GetBufferedInputCount(1), Is.Zero);
            Assert.That(loop.TryQueueInput(1, Input(loop, 1, 1, Vector2.zero, false)), Is.False);
        }

        [Test]
        public void QueuedSurplusIsFoldedIntoOneStepKeepingPressesNotHolds()
        {
            var loop = new DuelServerLoop(match: new DuelMatch(0, 300, 3));
            loop.Step();
            int ammo = loop.GetState(1).Ammo0;
            for (int tick = 0; tick < 5; tick++)
            {
                var input = Input(loop, 1, tick, Vector2.zero, tick == 1);
                if (tick == 1 || tick == 2) input.Buttons |= DuelButtons.Crouch; // released before the kept frame
                loop.TryQueueInput(1, input);
            }
            loop.Step();
            Assert.That(loop.GetBufferedInputCount(1), Is.AtMost(1));
            Assert.That(loop.GetLastConsumedInputTick(1), Is.EqualTo(3));
            Assert.That(loop.GetState(1).Ammo0, Is.EqualTo(ammo - 1));
            Assert.That(loop.GetState(1).Crouch, Is.Zero);
        }

        private static void Approach(DuelServerLoop loop, ref int tick)
        {
            DuelTestArena.FiringPositions(out var target0, out var target1);
            var path0 = DuelTestArena.Route(loop.GetState(0).Position, target0);
            var path1 = DuelTestArena.Route(loop.GetState(1).Position, target1);
            int index0 = 0, index1 = 0;
            float stepDistance = CombatRules.PlayerSpeed * loop.FixedDeltaTime;
            int limit = (path0.Length + path1.Length + 2) * (Mathf.CeilToInt(DuelTestArena.GridStep / stepDistance) + 2);
            for (int i = 0; i < limit; i++)
            {
                Vector3 p0 = loop.GetState(0).Position, p1 = loop.GetState(1).Position;
                Vector2 at0 = new Vector2(p0.x, p0.z), at1 = new Vector2(p1.x, p1.z);
                if (index0 < path0.Length && (path0[index0] - at0).sqrMagnitude < .000001f) index0++;
                if (index1 < path1.Length && (path1[index1] - at1).sqrMagnitude < .000001f) index1++;
                if (index0 == path0.Length && index1 == path1.Length) return;
                Vector2 d0 = index0 < path0.Length ? path0[index0] - at0 : Vector2.zero;
                Vector2 d1 = index1 < path1.Length ? path1[index1] - at1 : Vector2.zero;
                Assert.That(loop.TryQueueInput(0, Input(loop, 0, tick, d0 / stepDistance, false)), Is.True);
                Assert.That(loop.TryQueueInput(1, Input(loop, 1, tick, d1 / stepDistance, false)), Is.True);
                loop.Step();
                tick++;
            }
            Assert.Fail("Players did not reach their clear firing positions.");
        }

        private static DuelServerStepResult ShootStep(DuelServerLoop loop, ref int tick, bool fire0, bool fire1)
        {
            loop.TryQueueInput(0, Input(loop, 0, tick, Vector2.zero, fire0));
            loop.TryQueueInput(1, Input(loop, 1, tick, Vector2.zero, fire1));
            tick++;
            return loop.Step();
        }

        private static DuelInputFrame Input(DuelServerLoop loop, int slot, int tick, Vector2 move, bool fire)
        {
            Vector3 position = loop.GetState(slot).Position;
            return new DuelInputFrame
            {
                Tick = tick, WeaponSlot = -1, Move = move, Yaw = DuelArenaLayout.SpawnYaw(slot),
                AimOrigin = DuelPlayerSim.MuzzlePosition(loop.GetState(slot)),
                AimDirection = (loop.GetState(1 - slot).Position - position).normalized,
                Buttons = fire ? DuelButtons.Fire : DuelButtons.None
            };
        }
    }
}
