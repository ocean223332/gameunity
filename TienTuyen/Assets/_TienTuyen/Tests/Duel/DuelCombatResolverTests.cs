using NUnit.Framework;
using TienTuyen.Combat;
using UnityEngine;

namespace TienTuyen.Duel.Tests
{
    public sealed class DuelCombatResolverTests
    {
        [Test]
        public void ShotOverLowCoverHitsStandingTargetButMissesCrouchingTarget()
        {
            var low = LowCover();
            float laneX = low.Center.x + low.Half.x * .5f;
            var shooter = At(0, laneX, low.Center.y + low.Half.y + 5.2f);
            var target = At(1, laneX, low.Center.y - low.Half.y - .6f);
            // Above both measured crouched crowns (1.41-1.47 m), inside the standing head.
            const float headRayHeight = 1.7f;
            var attack = new DuelAttack(DuelAttackKind.Shot, 0, 12,
                new Vector3(shooter.Position.x, headRayHeight, shooter.Position.z), Vector3.back);
            var standing = DuelCombatResolver.Resolve(shooter, attack, target);
            Assert.That(standing.Damage, Is.EqualTo(DuelPlayerSim.WeaponAt(0).DamagePerHit * DuelCombatResolver.HeadDamageMultiplier));
            target.Crouch = 1;
            Assert.That(DuelCombatResolver.Resolve(shooter, attack, target).Damage, Is.Zero);
        }

        [Test]
        public void ShoulderPeekClearsLowCoverButCrouchedMuzzleCannotFollowTheCamera()
        {
            var low = LowCover();
            float laneX = low.Center.x + low.Half.x * .5f;
            var shooter = At(0, laneX, low.Center.y - low.Half.y - .5f);
            var weapon = DuelPlayerSim.WeaponAt(0);
            var target = At(1, laneX, low.Center.y + low.Half.y + 1);
            var attack = new DuelAttack(DuelAttackKind.Shot, 0, 12,
                DuelPlayerSim.MuzzlePosition(shooter), Vector3.forward);
            Assert.That(DuelCombatResolver.Resolve(shooter, attack, target).Damage, Is.EqualTo(weapon.DamagePerHit));
            shooter.Crouch = 1;
            // Keep the camera above the wall: the actual crouched muzzle must independently block the shot.
            Assert.That(DuelArenaLayout.RayHitsCover(attack.Origin, attack.Direction,
                Vector3.Distance(shooter.Position, target.Position), out _), Is.False);
            var crouched = DuelCombatResolver.Resolve(shooter, attack, target);
            Assert.That(crouched.Damage, Is.Zero);
            Assert.That(crouched.HitCover, Is.True);
        }

        [Test]
        public void ClearShotDealsCatalogDamageAndReportsBodyImpact()
        {
            // Keep the long-range regression independent of authored cover.
            var shooter = At(0, 0, DuelArenaLayout.HalfHeight + 2);
            var target = At(1, 18, shooter.Position.z);
            var attack = Shot(DuelPlayerSim.MuzzlePosition(shooter));
            var result = DuelCombatResolver.Resolve(shooter, attack, target);
            Assert.That(result.Damage, Is.EqualTo(DuelPlayerSim.WeaponAt(0).DamagePerHit));
            Assert.That(result.HitCount, Is.EqualTo(1));
            float feet = target.Position.y - DuelArenaLayout.GroundY;
            var axisPoint = new Vector3(target.Position.x,
                Mathf.Clamp(result.HitPoint.y, feet + DuelArenaLayout.PlayerRadius, feet + target.Height), target.Position.z);
            Assert.That((result.HitPoint - axisPoint).magnitude, Is.EqualTo(DuelArenaLayout.PlayerRadius).Within(.0001f));
            Assert.That(result.HitCover, Is.False);
        }

        [Test]
        public void CoverBlocksShotAndReportsCoverImpact()
        {
            var block = DuelArenaLayout.Covers[0];
            var shooter = At(0, block.Center.x, block.Center.y - block.Half.y - 1);
            var target = At(1, block.Center.x, block.Center.y + block.Half.y + 1);
            var attack = new DuelAttack(DuelAttackKind.Shot, 0, 12, shooter.Position, Vector3.forward);
            var result = DuelCombatResolver.Resolve(shooter, attack, target);
            Assert.That(result.Damage, Is.Zero);
            Assert.That(result.HitCount, Is.Zero);
            Assert.That(result.HitCover, Is.True);
            Assert.That(result.HitPoint.z, Is.EqualTo(block.Center.y - block.Half.y).Within(.0001f));
        }

        [Test]
        public void CameraRayCannotBypassCoverInFrontOfMuzzle()
        {
            var block = DuelArenaLayout.Covers[0];
            var shooter = At(0, block.Center.x, block.Center.y - block.Half.y - 1);
            var target = At(1, block.Center.x, block.Center.y + block.Half.y + 1);
            // The view origin has cleared the central block, while the body remains behind it.
            var attack = new DuelAttack(DuelAttackKind.Shot, 0, 12,
                new Vector3(block.Center.x, .88f, block.Center.y + block.Half.y + .1f), Vector3.forward);
            Assert.That(DuelArenaLayout.RayHitsCover(attack.Origin, attack.Direction, .9f, out _), Is.False);
            var result = DuelCombatResolver.Resolve(shooter, attack, target);
            Assert.That(result.Damage, Is.Zero);
            Assert.That(result.HitCover, Is.True);
        }

        [Test]
        public void ThirdPersonRayMeasuresWeaponRangeFromShooter()
        {
            // Isolate the range sphere from authored cover: this ray lies outside the arena.
            var shooter = At(0, 0, DuelArenaLayout.HalfHeight + 2);
            var target = At(1, DuelPlayerSim.WeaponAt(0).Range - 1, shooter.Position.z);
            var attack = Shot(DuelPlayerSim.MuzzlePosition(shooter) - Vector3.right * 5);
            Assert.That(DuelCombatResolver.Resolve(shooter, attack, target).HitCount, Is.EqualTo(1));
            target.Position.x = shooter.Position.x + DuelPlayerSim.WeaponAt(0).Range + 1;
            Assert.That(DuelCombatResolver.Resolve(shooter, attack, target).HitCount, Is.Zero);
        }

        [Test]
        public void CapsuleHitUsesCrouchHeightAndJumpElevation()
        {
            Pair(5, out var shooter, out var target);
            // Baked upper heads reach 1.856-1.887 m, about 23 cm forward of the body axis.
            const float visibleHeadHeight = 1.85f;
            var headShot = Shot(new Vector3(shooter.Position.x, visibleHeadHeight, shooter.Position.z + .23f));
            Assert.That(DuelCombatResolver.Resolve(shooter, headShot, target).Damage,
                Is.EqualTo(DuelPlayerSim.WeaponAt(0).DamagePerHit * DuelCombatResolver.HeadDamageMultiplier));
            var aboveHead = Shot(headShot.Origin + Vector3.up * .05f);
            Assert.That(DuelCombatResolver.Resolve(shooter, aboveHead, target).HitCount, Is.Zero);
            target.Crouch = 1;
            Assert.That(DuelCombatResolver.Resolve(shooter, headShot, target).HitCount, Is.Zero);
            target.Crouch = 0;
            target.Position.y += DuelArenaLayout.JumpHeight;
            Assert.That(DuelCombatResolver.Resolve(shooter, Shot(shooter.Position), target).HitCount, Is.Zero);
        }

        [Test]
        public void HeadHitsDealDoubleDamageButChestAndShouldersStayBodyHits()
        {
            Pair(5, out var shooter, out var target);
            var weapon = DuelPlayerSim.WeaponAt(0);
            var headShot = Shot(new Vector3(shooter.Position.x, 1.75f, shooter.Position.z + .23f));
            var head = DuelCombatResolver.Resolve(shooter, headShot, target);
            Assert.That(head.Damage, Is.EqualTo(weapon.DamagePerHit * 2f));
            Assert.That(head.Zones, Is.EqualTo(DuelHitZone.Head));
            var chestShot = Shot(new Vector3(shooter.Position.x, 1.5f, shooter.Position.z));
            var chest = DuelCombatResolver.Resolve(shooter, chestShot, target);
            Assert.That(chest.Damage, Is.EqualTo(weapon.DamagePerHit));
            Assert.That(chest.Zones, Is.EqualTo(DuelHitZone.Body));
            // Highest baked shoulder in either standing skin; it must never receive the head reward.
            var shoulderShot = Shot(new Vector3(shooter.Position.x, 1.65658784f, shooter.Position.z));
            Assert.That(DuelCombatResolver.Resolve(shooter, shoulderShot, target).Damage,
                Is.EqualTo(weapon.DamagePerHit));
            // Just above the head plane (1.667 m) the same level ray already earns the head reward.
            var neckShot = Shot(new Vector3(shooter.Position.x, 1.68f, shooter.Position.z));
            Assert.That(DuelCombatResolver.Resolve(shooter, neckShot, target).Zones, Is.EqualTo(DuelHitZone.Head));
        }

        [Test]
        public void ShotgunPelletsUseDeterministicSpreadAndCatalogDamage()
        {
            Pair(1, out var shooter, out var target);
            shooter.ActiveWeaponSlot = 2;
            shooter.PreviousButtons = DuelButtons.Aim;
            var attack = new DuelAttack(DuelAttackKind.Shot, 2, 129,
                DuelPlayerSim.MuzzlePosition(shooter), Vector3.right);
            var first = DuelCombatResolver.Resolve(shooter, attack, target);
            var second = DuelCombatResolver.Resolve(shooter, attack, target);
            var weapon = DuelPlayerSim.WeaponAt(2);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.HitCount, Is.EqualTo(weapon.PelletsPerShot));
            Assert.That(first.Damage, Is.EqualTo(weapon.PelletsPerShot * weapon.DamagePerHit));
            var later = new DuelAttack(attack.Kind, attack.WeaponSlot, attack.Tick + 1, attack.Origin, attack.Direction);
            Assert.That(DuelCombatResolver.Resolve(shooter, later, target).HitPoint, Is.Not.EqualTo(first.HitPoint));
        }

        [Test]
        public void KnifeDamagesOnlyAnOpponentInReachAndFrontalArc()
        {
            Pair(1, out var shooter, out var target);
            var attack = new DuelAttack(DuelAttackKind.Knife, DuelPlayerSim.KnifeSlot, 1,
                shooter.Position, Vector3.right);
            Assert.That(DuelCombatResolver.Resolve(shooter, attack, target).Damage, Is.EqualTo(CombatRules.KnifeDamage));
            target.Position.x = shooter.Position.x - 1;
            Assert.That(DuelCombatResolver.Resolve(shooter, attack, target).Damage, Is.Zero);
            target.Position.x = shooter.Position.x + CombatRules.KnifeReach + .01f;
            Assert.That(DuelCombatResolver.Resolve(shooter, attack, target).Damage, Is.Zero);
        }

        private static DuelSimState At(int slot, float x, float z)
        {
            var state = DuelPlayerSim.CreateState(slot);
            state.Position = new Vector3(x, DuelArenaLayout.GroundY, z);
            return state;
        }

        private static DuelArenaLayout.Block LowCover()
        {
            foreach (var cover in DuelArenaLayout.Covers)
                if (cover.Height >= CombatRules.PlayerCrouchHeight && cover.Height < CombatRules.PlayerStandHeight - .2f)
                    return cover;
            throw new System.InvalidOperationException("The low-cover fixture needs a crouch-height arena block.");
        }

        private static void Pair(float distance, out DuelSimState shooter, out DuelSimState target)
        {
            DuelTestArena.ClearPair(distance, out var from, out var to);
            shooter = At(0, from.x, from.z); target = At(1, to.x, to.z);
        }

        private static DuelAttack Shot(Vector3 origin) =>
            new DuelAttack(DuelAttackKind.Shot, 0, 12, origin, Vector3.right);
    }
}
