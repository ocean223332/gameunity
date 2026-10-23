using System;
using TienTuyen.Combat;
using UnityEngine;

namespace TienTuyen.Duel
{
    [Flags]
    public enum DuelHitZone : byte { None = 0, Body = 1, Head = 2 }

    /// <summary>Zones combines all landed pellets; HitPoint stays the nearest impact, including mixed hits.</summary>
    public readonly struct DuelHitResult
    {
        public readonly float Damage;
        public readonly int HitCount;
        public readonly Vector3 HitPoint;
        public readonly bool HitCover;
        public readonly DuelHitZone Zones;
        public DuelHitResult(float damage, int hitCount, Vector3 hitPoint, bool hitCover = false,
            DuelHitZone zones = DuelHitZone.None)
        {
            Damage = damage; HitCount = hitCount; HitPoint = hitPoint; HitCover = hitCover; Zones = zones;
        }
    }

    /// <summary>Geometry only; the caller applies returned damage once per authorized attack.</summary>
    public static class DuelCombatResolver
    {
        // The catalog has no spread field. Only multi-pellet weapons use this four-degree cone.
        public const float PelletSpreadDegrees = 4f;
        public const float HeadDamageMultiplier = 2f;

        // Measured taller crown, shared for equal hit volumes in both slots. The US model receives
        // 3.1 cm standing / 5.6 cm crouched forgiveness instead of a skin-dependent target size.
        private const float StandingCrown = 1.887f, CrouchingCrown = 1.466f;

        // Baked standing/crouched ADS rifle idle, both skins: shoulders/upper arms reach 1.6566 / 1.2849 m.
        // No animation or pitch sweep: other poses may raise a shoulder into the head band.
        // Keep a shared plane 1 cm above them; the overlapping lower jaw deliberately stays body damage.
        // A later head sphere could recover the jaw: radius ~.163 m, centres y=1.724 standing / 1.304 crouched.
        private const float StandingHeadPlane = 1.667f, CrouchingHeadPlane = 1.295f;

        public static DuelHitResult Resolve(in DuelSimState shooter, in DuelAttack attack, in DuelSimState target)
        {
            if (shooter.Dead || target.Dead || attack.Kind == DuelAttackKind.None ||
                !DuelPlayerSim.Finite(attack.Direction) || attack.Direction.sqrMagnitude < 0.000001f) return default;
            if (attack.Kind == DuelAttackKind.Knife) return ResolveKnife(shooter, attack.Direction, target);
            if (attack.WeaponSlot < 0 || attack.WeaponSlot >= DuelPlayerSim.KnifeSlot ||
                !DuelPlayerSim.Finite(attack.Origin)) return default;

            var weapon = DuelPlayerSim.WeaponAt(attack.WeaponSlot);
            Vector3 muzzle = DuelPlayerSim.MuzzlePosition(shooter);
            Vector3 direction = attack.Direction.normalized;
            // A third-person view ray starts behind the body. Range is measured from the shooter.
            float damage = 0;
            int hits = 0;
            DuelHitZone zones = DuelHitZone.None;
            float headPlane = target.Position.y - DuelArenaLayout.GroundY +
                Mathf.Lerp(StandingHeadPlane, CrouchingHeadPlane, Mathf.Clamp01(target.Crouch));
            float nearestBody = float.PositiveInfinity, nearestCover = float.PositiveInfinity;
            Vector3 bodyPoint = default, coverPoint = default;
            Vector3 endPoint = muzzle + direction * weapon.Range;
            for (int pellet = 0; pellet < weapon.PelletsPerShot; pellet++)
            {
                Vector3 ray = weapon.PelletsPerShot == 1 ? direction :
                    Spread(direction, attack.Tick, shooter.Slot, pellet, shooter.Aiming ? .5f : 1f);
                // Ignore the trailing camera segment behind the weapon. Each pellet still follows
                // its view ray, with range measured from the muzzle and muzzle obstruction checked below.
                Vector3 rayOrigin = attack.Origin + ray * Mathf.Max(0, Vector3.Dot(muzzle - attack.Origin, ray));
                // Exit distance through the weapon's range sphere centred on the muzzle.
                Vector3 offset = rayOrigin - muzzle;
                float b = Vector3.Dot(offset, ray);
                float discriminant = b * b - offset.sqrMagnitude + weapon.Range * weapon.Range;
                if (discriminant < 0) continue;
                float rayLength = -b + Mathf.Sqrt(discriminant);
                if (rayLength < 0) continue;
                if (pellet == 0) endPoint = rayOrigin + ray * rayLength;
                bool cameraBlocked = DuelArenaLayout.RayHitsCover(rayOrigin, ray, rayLength, out float coverDistance);
                if (cameraBlocked && coverDistance < nearestCover)
                {
                    nearestCover = coverDistance;
                    coverPoint = rayOrigin + ray * coverDistance;
                }
                if (!RayHitsCapsule(rayOrigin, ray, target, rayLength, out float distance)) continue;
                Vector3 point = rayOrigin + ray * distance;
                Vector3 fromMuzzle = point - muzzle;
                if (fromMuzzle.sqrMagnitude > weapon.Range * weapon.Range) continue;
                if (cameraBlocked && coverDistance <= distance) continue;
                // Also test muzzle-to-impact: a camera peeking past a wall cannot fire through it.
                if (DuelArenaLayout.RayHitsCover(muzzle, fromMuzzle.normalized, fromMuzzle.magnitude, out float muzzleCover))
                {
                    if (muzzleCover < nearestCover)
                    {
                        nearestCover = muzzleCover;
                        coverPoint = muzzle + fromMuzzle.normalized * muzzleCover;
                    }
                    continue;
                }
                DuelHitZone zone = point.y >= headPlane ? DuelHitZone.Head : DuelHitZone.Body;
                damage += weapon.DamagePerHit * (zone == DuelHitZone.Head ? HeadDamageMultiplier : 1f);
                zones |= zone;
                hits++;
                if (distance < nearestBody) { nearestBody = distance; bodyPoint = point; }
            }
            return hits > 0 ? new DuelHitResult(damage, hits, bodyPoint, zones: zones) :
                new DuelHitResult(0, 0, float.IsPositiveInfinity(nearestCover) ? endPoint : coverPoint,
                    !float.IsPositiveInfinity(nearestCover));
        }

        private static DuelHitResult ResolveKnife(in DuelSimState shooter, Vector3 direction, in DuelSimState target)
        {
            Vector3 planar = target.Position - shooter.Position;
            planar.y = 0;
            if (planar.sqrMagnitude > CombatRules.KnifeReach * CombatRules.KnifeReach) return default;
            direction.y = 0;
            if (direction.sqrMagnitude < .000001f) return default;
            if (planar.sqrMagnitude > .000001f && Vector3.Dot(planar.normalized, direction.normalized) <
                Mathf.Cos(CombatRules.KnifeArcDegrees * Mathf.Deg2Rad)) return default;
            Capsule(target, out Vector3 bottom, out Vector3 top);
            Vector3 muzzle = DuelPlayerSim.MuzzlePosition(shooter);
            Vector3 point = new Vector3(bottom.x, Mathf.Clamp(muzzle.y, bottom.y, top.y), bottom.z);
            Vector3 toBody = point - muzzle;
            float distance = toBody.magnitude;
            if (distance > CombatRules.KnifeReach + DuelArenaLayout.PlayerRadius) return default;
            if (DuelArenaLayout.RayHitsCover(muzzle, toBody.normalized, distance, out float cover))
                return new DuelHitResult(0, 0, muzzle + toBody.normalized * cover, true);
            return new DuelHitResult(CombatRules.KnifeDamage, 1,
                point - toBody.normalized * DuelArenaLayout.PlayerRadius, zones: DuelHitZone.Body);
        }

        private static Vector3 Spread(Vector3 forward, int tick, int slot, int pellet, float factor)
        {
            uint seed = unchecked((uint)tick * 0x9E3779B9u ^ (uint)(slot + 1) * 0x85EBCA6Bu ^
                (uint)(pellet + 1) * 0xC2B2AE35u);
            float radius = (float)Math.Sqrt(Unit(Hash(seed)));
            double angle = Unit(Hash(seed ^ 0x68E31DA4u)) * Math.PI * 2;
            float cone = (float)Math.Tan(PelletSpreadDegrees * factor * Math.PI / 180);
            Vector3 right = Vector3.Cross(Mathf.Abs(forward.y) > .99f ? Vector3.forward : Vector3.up, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right);
            return (forward + (right * (float)Math.Cos(angle) + up * (float)Math.Sin(angle)) * (radius * cone)).normalized;
        }

        private static uint Hash(uint value)
        {
            unchecked
            {
                value ^= value >> 16; value *= 0x7FEB352Du;
                value ^= value >> 15; value *= 0x846CA68Bu;
                return value ^ value >> 16;
            }
        }
        private static float Unit(uint value) => (value & 0x00FFFFFFu) / 16777216f;

        private static void Capsule(in DuelSimState target, out Vector3 bottom, out Vector3 top)
        {
            float feet = target.Position.y - DuelArenaLayout.GroundY;
            bottom = new Vector3(target.Position.x, feet + DuelArenaLayout.PlayerRadius, target.Position.z);
            // Enclose the head's measured forward offset; the crown plane below clips the rounded cap.
            top = new Vector3(target.Position.x, feet + target.Height, target.Position.z);
        }

        private static bool RayHitsCapsule(Vector3 origin, Vector3 direction, in DuelSimState target,
            float limit, out float distance)
        {
            distance = float.PositiveInfinity;
            float ceiling = target.Position.y - DuelArenaLayout.GroundY +
                Mathf.Lerp(StandingCrown, CrouchingCrown, Mathf.Clamp01(target.Crouch));
            float start = 0;
            if (origin.y > ceiling)
            {
                if (direction.y >= 0) return false;
                start = (ceiling - origin.y) / direction.y;
                if (start > limit) return false;
                origin += direction * start;
                limit -= start;
            }
            else if (direction.y > 0) limit = Mathf.Min(limit, (ceiling - origin.y) / direction.y);
            Capsule(target, out Vector3 bottom, out Vector3 top);
            float radius = DuelArenaLayout.PlayerRadius;
            Vector3 closest = new Vector3(bottom.x, Mathf.Clamp(origin.y, bottom.y, top.y), bottom.z);
            if ((origin - closest).sqrMagnitude <= radius * radius) { distance = start; return true; }
            // Cylinder between the end-cap centres.
            float dx = origin.x - bottom.x, dz = origin.z - bottom.z;
            float a = direction.x * direction.x + direction.z * direction.z;
            float b = dx * direction.x + dz * direction.z;
            float c = dx * dx + dz * dz - radius * radius;
            float discriminant = b * b - a * c;
            if (a > .000001f && discriminant >= 0)
            {
                float root = Mathf.Sqrt(discriminant);
                CylinderCandidate((-b - root) / a, origin, direction, bottom.y, top.y, ref distance);
                CylinderCandidate((-b + root) / a, origin, direction, bottom.y, top.y, ref distance);
            }
            SphereCandidate(origin, direction, bottom, radius, ref distance);
            SphereCandidate(origin, direction, top, radius, ref distance);
            bool hit = distance <= limit;
            distance += start;
            return hit;
        }

        private static void CylinderCandidate(float t, Vector3 origin, Vector3 direction, float low, float high,
            ref float nearest)
        {
            float y = origin.y + direction.y * t;
            if (t >= 0 && t < nearest && y >= low && y <= high) nearest = t;
        }

        private static void SphereCandidate(Vector3 origin, Vector3 direction, Vector3 centre, float radius,
            ref float nearest)
        {
            Vector3 offset = origin - centre;
            float b = Vector3.Dot(offset, direction), c = offset.sqrMagnitude - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0) return;
            float root = Mathf.Sqrt(discriminant);
            float t = -b - root;
            if (t < 0) t = -b + root;
            if (t >= 0 && t < nearest) nearest = t;
        }
    }
}
