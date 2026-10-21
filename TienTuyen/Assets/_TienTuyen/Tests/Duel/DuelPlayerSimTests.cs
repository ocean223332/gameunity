using NUnit.Framework;
using TienTuyen.Combat;
using UnityEngine;

namespace TienTuyen.Duel.Tests
{
    public sealed class DuelPlayerSimTests
    {
        [Test]
        public void EyeStaysAboveMuzzleThroughCrouchAndJump()
        {
            var state = DuelPlayerSim.CreateState(0);
            state.Position.y += DuelArenaLayout.JumpHeight;
            foreach (float crouch in new[] { 0f, .5f, 1f })
            {
                state.Crouch = crouch;
                var gap = DuelPlayerSim.EyePosition(state) - DuelPlayerSim.MuzzlePosition(state);
                Assert.That((gap - Vector3.up * .1f).sqrMagnitude, Is.LessThan(1e-10f));
            }
        }

        [Test]
        public void SameInputsProduceIdenticalFullSnapshotsAndAttacks()
        {
            var first = DuelPlayerSim.CreateState(0);
            var second = first;
            bool fired = false, contacted = false;
            for (int tick = 1; tick <= 360; tick++)
            {
                var input = Input(tick);
                input.Move = new Vector2(tick % 90 < 45 ? 1 : -1, .25f);
                input.WeaponSlot = tick / 90 % 4;
                input.Yaw = tick * 3.17f;
                input.Pitch = tick % 40 - 20;
                input.Buttons = DuelButtons.Fire | DuelButtons.Aim;
                if (tick % 40 < 20) input.Buttons |= DuelButtons.Crouch | DuelButtons.Walk;
                if (tick % 60 == 1) input.Buttons |= DuelButtons.Jump;
                if (tick % 35 == 1) input.Buttons |= DuelButtons.Reload;
                var a = DuelPlayerSim.Step(ref first, input, 1f / 30f);
                var b = DuelPlayerSim.Step(ref second, input, 1f / 30f);
                Assert.That(first, Is.EqualTo(second), "Snapshot at tick " + tick);
                Assert.That(a, Is.EqualTo(b), "Attack at tick " + tick);
                fired |= a.ShotFired;
                contacted |= a.KnifeContact;
            }
            Assert.That(fired && contacted, Is.True);
        }

        [Test]
        public void FrozenPlayerCannotMoveSwitchOrAttack()
        {
            var state = DuelPlayerSim.CreateState(0);
            var start = state.Position;
            var input = Input(42);
            input.Move = Vector2.one;
            input.WeaponSlot = 2;
            input.Buttons = DuelButtons.Fire | DuelButtons.Jump;
            var result = DuelPlayerSim.Step(ref state, input, 1f / 30f, false);
            Assert.That(state.Position, Is.EqualTo(start));
            Assert.That(state.ActiveWeaponSlot, Is.Zero);
            Assert.That(state.Ammo0, Is.EqualTo(DuelPlayerSim.WeaponAt(0).MagazineSize));
            Assert.That(result.Attack.Kind, Is.EqualTo(DuelAttackKind.None));
        }

        [Test]
        public void BodyCannotTunnelThroughCoverOrLeaveArenaBounds()
        {
            var state = DuelPlayerSim.CreateState(0);
            var start = state.Position;
            var input = Input(1);
            input.Move = Vector2.right;
            Assert.That(DuelArenaLayout.RayHitsCover(start, Vector3.right, DuelArenaLayout.HalfWidth * 2,
                out float obstacleDistance), Is.True);
            DuelPlayerSim.Step(ref state, input, DuelArenaLayout.HalfWidth * 2 / CombatRules.PlayerSpeed);
            Assert.That(state.Position.x, Is.EqualTo(start.x + obstacleDistance - DuelArenaLayout.PlayerRadius).Within(.0001f));
            Assert.That(DuelArenaLayout.IsInsideCover(new Vector2(state.Position.x, state.Position.z),
                DuelArenaLayout.PlayerRadius - .0001f), Is.False);
            state.Position = new Vector3(DuelArenaLayout.HalfWidth - DuelArenaLayout.PlayerRadius - .1f,
                DuelArenaLayout.GroundY, DuelArenaLayout.HalfHeight - DuelArenaLayout.PlayerRadius - .1f);
            input.Move = Vector2.one;
            DuelPlayerSim.Step(ref state, input, 20);
            Assert.That(state.Position.x + DuelArenaLayout.PlayerRadius, Is.LessThanOrEqualTo(DuelArenaLayout.HalfWidth));
            Assert.That(state.Position.z + DuelArenaLayout.PlayerRadius, Is.LessThanOrEqualTo(DuelArenaLayout.HalfHeight));
        }

        [Test]
        public void SpawnPointsAndFacingAreHalfTurnSymmetricAndClear()
        {
            var a = DuelPlayerSim.CreateState(0);
            var b = DuelPlayerSim.CreateState(1);
            Assert.That(a.Position.x, Is.EqualTo(-b.Position.x));
            Assert.That(a.Position.z, Is.EqualTo(-b.Position.z));
            Assert.That(a.Position.y, Is.EqualTo(b.Position.y));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(a.Yaw, b.Yaw)), Is.EqualTo(180));
            Assert.That(DuelArenaLayout.IsInsideCover(new Vector2(a.Position.x, a.Position.z), DuelArenaLayout.PlayerRadius), Is.False);
            Assert.That(DuelArenaLayout.IsInsideCover(new Vector2(b.Position.x, b.Position.z), DuelArenaLayout.PlayerRadius), Is.False);
        }

        [Test]
        public void CrouchOverridesWalkSlowdownAfterBothBlendsComplete()
        {
            var state = DuelPlayerSim.CreateState(0);
            float startX = state.Position.x;
            var input = Input(1);
            input.Move = Vector2.right;
            input.Buttons = DuelButtons.Crouch | DuelButtons.Walk;
            float dt = Mathf.Max(CombatRules.CrouchSeconds, CombatRules.WalkSeconds);
            DuelPlayerSim.Step(ref state, input, dt);
            Assert.That(state.Crouch, Is.EqualTo(1));
            Assert.That(state.Position.x - startX, Is.EqualTo(CombatRules.PlayerSpeed * CombatRules.CrouchSpeedFactor * dt).Within(.00001f));
        }

        [Test]
        public void JumpLandsAndHeldButtonDoesNotRepeat()
        {
            var state = DuelPlayerSim.CreateState(0);
            var input = Input(1);
            input.Buttons = DuelButtons.Jump;
            float highest = state.Position.y;
            for (int tick = 1; tick <= 90; tick++)
            {
                input.Tick = tick;
                DuelPlayerSim.Step(ref state, input, 1f / 60f);
                highest = Mathf.Max(highest, state.Position.y);
            }
            Assert.That(highest, Is.GreaterThan(DuelArenaLayout.GroundY + DuelArenaLayout.JumpHeight * .8f));
            Assert.That(highest, Is.LessThanOrEqualTo(DuelArenaLayout.GroundY + DuelArenaLayout.JumpHeight));
            Assert.That(state.Position.y, Is.EqualTo(DuelArenaLayout.GroundY));
            Assert.That(state.VerticalVelocity, Is.Zero);
        }

        [Test]
        public void GunSpendsAmmoRespectsCooldownAndReloadsWhenEmpty()
        {
            var state = DuelPlayerSim.CreateState(0);
            state.Ammo0 = 2;
            var weapon = DuelPlayerSim.WeaponAt(0);
            var input = Input(1);
            input.Buttons = DuelButtons.Fire;
            Assert.That(DuelPlayerSim.Step(ref state, input, .01f).ShotFired, Is.True);
            Assert.That(state.Ammo0, Is.EqualTo(1));
            Assert.That(DuelPlayerSim.Step(ref state, input, weapon.ShotCooldownSeconds / 2).ShotFired, Is.False);
            Assert.That(DuelPlayerSim.Step(ref state, input, weapon.ShotCooldownSeconds / 2).ShotFired, Is.True);
            Assert.That(state.Ammo0, Is.Zero);
            Assert.That(state.Reload0, Is.EqualTo(weapon.ReloadSecondsFor(weapon.MagazineSize)));
            Assert.That(DuelPlayerSim.Step(ref state, input, .01f).ShotFired, Is.False);
        }

        [Test]
        public void SwitchingCancelsReloadAndPreventsFireDuringDraw()
        {
            var state = DuelPlayerSim.CreateState(0);
            state.Ammo0 = 3;
            var input = Input(1);
            input.Buttons = DuelButtons.Reload;
            DuelPlayerSim.Step(ref state, input, .01f);
            Assert.That(state.Reload0, Is.GreaterThan(0));
            input.WeaponSlot = 1;
            input.Buttons = DuelButtons.Fire;
            Assert.That(DuelPlayerSim.Step(ref state, input, .01f).ShotFired, Is.False);
            Assert.That(state.Reload0, Is.Zero);
            Assert.That(state.Ammo0, Is.EqualTo(3));
            Assert.That(DuelPlayerSim.Step(ref state, input, CombatRules.WeaponDrawSeconds - .01f).ShotFired, Is.False);
            Assert.That(DuelPlayerSim.Step(ref state, input, .02f).ShotFired, Is.True);
        }

        [Test]
        public void ShotgunReloadDurationUsesMissingRoundsAndFillsAtCompletion()
        {
            var state = DuelPlayerSim.CreateState(0);
            state.ActiveWeaponSlot = 2;
            var weapon = DuelPlayerSim.WeaponAt(2);
            state.Ammo2 = weapon.MagazineSize - 1;
            var input = Input(1);
            input.Buttons = DuelButtons.Reload;
            DuelPlayerSim.Step(ref state, input, .01f);
            Assert.That(state.Reload2, Is.EqualTo(weapon.ReloadSecondsFor(1)));
            input.Buttons = DuelButtons.None;
            DuelPlayerSim.Step(ref state, input, state.Reload2 - .01f);
            Assert.That(state.Ammo2, Is.EqualTo(weapon.MagazineSize - 1));
            DuelPlayerSim.Step(ref state, input, .02f);
            Assert.That(state.Reload2, Is.Zero);
            Assert.That(state.Ammo2, Is.EqualTo(weapon.MagazineSize));
        }

        [Test]
        public void KnifeEmitsOneContactAfterWindupAndCannotRestartDuringRecovery()
        {
            var state = DuelPlayerSim.CreateState(0);
            state.ActiveWeaponSlot = DuelPlayerSim.KnifeSlot;
            var input = Input(1);
            input.Buttons = DuelButtons.Fire;
            Assert.That(DuelPlayerSim.Step(ref state, input, .01f).KnifeContact, Is.False);
            Assert.That(DuelPlayerSim.Step(ref state, input, CombatRules.KnifeContactSeconds - .01f).KnifeContact, Is.False);
            Assert.That(DuelPlayerSim.Step(ref state, input, .02f).KnifeContact, Is.True);
            float remaining = state.KnifeRemaining;
            Assert.That(DuelPlayerSim.Step(ref state, input, .01f).KnifeContact, Is.False);
            Assert.That(state.KnifeRemaining, Is.LessThan(remaining));
            input.Buttons = DuelButtons.None;
            DuelPlayerSim.Step(ref state, input, CombatRules.KnifeSwingSeconds);
            Assert.That(state.KnifeRemaining, Is.Zero);
            Assert.That(state.KnifeContactPending, Is.False);
        }

        private static DuelInputFrame Input(int tick) => new DuelInputFrame
        {
            Tick = tick, WeaponSlot = -1, AimOrigin = new Vector3(-15, .88f, 0), AimDirection = Vector3.right
        };
    }
}
