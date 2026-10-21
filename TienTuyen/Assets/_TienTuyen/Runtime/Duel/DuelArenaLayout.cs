using UnityEngine;

namespace TienTuyen.Duel
{
    /// <summary>
    /// The duel arena's shape, shared by the authoritative simulation and by the visuals that dress it.
    /// The layout is its own mirror about x = 0: slot 1's spawn, cover and ground are slot 0's reflected, so both
    /// face the same fight, and a sun with no x component (see the duel lighting) lights both spawns alike.
    /// </summary>
    /// <summary>What a cover block is, for the visuals that dress it; the simulation reads only its box.</summary>
    public enum DuelCoverKind
    {
        Plain, Parapet, Traverse, GunPit, Dugout, Sap, CollapsedWall,
        WarehouseWall, Column, BrickWall, Bunker, RoofSlab, Rubble,
        Truck, Apc
    }

    public static class DuelArenaLayout
    {
        /// <summary>
        /// Axis-aligned cover: a box over Center ± Half in the XZ plane, from the floor (y = 0) up to Height.
        /// Shots and sight pass over it; bodies never do, since nothing can be climbed and a jump clears no cover.
        /// </summary>
        public readonly struct Block
        {
            public readonly Vector2 Center, Half;
            public readonly float Height;
            public readonly DuelCoverKind Kind;

            public Block(float centerX, float centerZ, float halfX, float halfZ, float height,
                DuelCoverKind kind = DuelCoverKind.Plain)
            {
                Center = new Vector2(centerX, centerZ);
                Half = new Vector2(halfX, halfZ);
                Height = height;
                Kind = kind;
            }
        }

        /// <summary>Playable half-extents; a body is clamped to these.</summary>
        public const float HalfWidth = 24f, HalfHeight = 16f;

        /// <summary>The y a standing body's pivot sits at, matching the wave arena so the hero rig reads the same.</summary>
        public const float GroundY = 0.8f;

        public const float PlayerRadius = 0.38f;
        public const float JumpHeight = 1.2f, Gravity = -20f;

        // West half and the pieces on the centre line; the east half is the west reflected (MirrorEast).
        // Heights: 1.25-1.4 is chest-high work a crouching body hides behind and a standing one fires over;
        // 2 m and up hides a standing body.
        private static readonly Block[] West =
        {
            // Breastwork trench: the parapet with its exits, traverses guarding them.
            new Block(-18f, 0f, .6f, 3.6f, 1.35f, DuelCoverKind.Parapet),
            new Block(-18f, 10.4f, .6f, 2.8f, 1.35f, DuelCoverKind.Parapet), new Block(-18f, -10.4f, .6f, 2.8f, 1.35f, DuelCoverKind.Parapet),
            new Block(-14.8f, 5.6f, .5f, 2.4f, 2f, DuelCoverKind.Traverse), new Block(-14.8f, -5.6f, .5f, 2.4f, 2f, DuelCoverKind.Traverse),
            // Gun pit, its wing run back to the parapet; a dugout against the parapet; a sap; a collapsed wall.
            new Block(-11.5f, 9.5f, .6f, 2f, 1.25f, DuelCoverKind.GunPit), new Block(-14.75f, 11f, 2.65f, .5f, 1.25f, DuelCoverKind.GunPit),
            new Block(-15f, -12.8f, 2.4f, 1f, 1.7f, DuelCoverKind.Dugout),
            new Block(-13f, -7f, 2f, .5f, 1.3f, DuelCoverKind.Sap),
            new Block(-13f, 0f, .5f, 1.6f, 1.25f, DuelCoverKind.CollapsedWall),
            // Ruins: the warehouse's west wall and a column, brick wall stubs on both edges.
            new Block(-4.6f, -.2f, .4f, 3.3f, 3.2f, DuelCoverKind.WarehouseWall), new Block(-2.4f, 2.2f, .45f, .45f, 3.2f, DuelCoverKind.Column),
            new Block(-10.2f, .8f, .45f, .45f, 3.2f, DuelCoverKind.Column),
            new Block(-7f, 13.4f, .4f, 2.6f, 2.8f, DuelCoverKind.BrickWall), new Block(-8f, -12f, .4f, 4f, 2.8f, DuelCoverKind.BrickWall),
            // Wrecks: a burnt truck run into the warehouse wall's end, a split APC.
            new Block(-7.6f, 5.6f, 1.1f, 2.6f, 2.1f, DuelCoverKind.Truck), new Block(-8f, -4f, 1.4f, 2.4f, 2.2f, DuelCoverKind.Apc),
        };

        private static readonly Block[] Centre =
        {
            new Block(0f, 5.5f, 6.6f, .4f, 3.2f, DuelCoverKind.WarehouseWall),     // warehouse north wall, its broken ends past the corners
            new Block(0f, -.5f, .6f, .6f, 3.2f, DuelCoverKind.Column),      // the warehouse's middle column
            new Block(0f, 12f, 1.8f, 4f, 2.6f, DuelCoverKind.Bunker),       // shattered concrete bunker
            new Block(0f, -8.3f, 2.6f, 1.8f, 1.2f, DuelCoverKind.RoofSlab),   // collapsed roof slab
            new Block(0f, -13.4f, 2.2f, .6f, 1.25f, DuelCoverKind.Rubble),  // sandbags on shattered concrete
        };

        public static readonly Block[] Covers = MirrorEast();

        private static Block[] MirrorEast()
        {
            var all = new Block[Centre.Length + West.Length * 2];
            Centre.CopyTo(all, 0);
            for (int i = 0; i < West.Length; i++)
            {
                var west = West[i];
                all[Centre.Length + 2 * i] = west;
                all[Centre.Length + 2 * i + 1] = new Block(-west.Center.x, west.Center.y, west.Half.x, west.Half.y, west.Height, west.Kind);
            }
            return all;
        }

        /// <summary>Where slot 0 and slot 1 start each round, facing the centre.</summary>
        public static Vector3 Spawn(int slot) => new Vector3(slot == 0 ? -21f : 21f, GroundY, 0f);

        /// <summary>The yaw a body spawns with, looking across the arena.</summary>
        public static float SpawnYaw(int slot) => slot == 0 ? 90f : -90f;

        public static bool IsInsideCover(Vector2 point, float radius)
        {
            foreach (var cover in Covers)
                if (Mathf.Abs(point.x - cover.Center.x) < cover.Half.x + radius &&
                    Mathf.Abs(point.y - cover.Center.y) < cover.Half.y + radius)
                    return true;
            return false;
        }

        /// <summary>
        /// Axis-separated swept motion: it slides along a cover face instead of sticking, and a long step
        /// cannot tunnel through a block. Returns the resulting position, clamped to the arena.
        /// </summary>
        public static Vector3 MoveWithCover(Vector3 position, Vector3 delta, float radius)
        {
            MoveAxis(ref position, delta.x, true, radius);
            MoveAxis(ref position, delta.z, false, radius);
            position.x = Mathf.Clamp(position.x, -HalfWidth, HalfWidth);
            position.z = Mathf.Clamp(position.z, -HalfHeight, HalfHeight);
            return position;
        }

        private static void MoveAxis(ref Vector3 position, float amount, bool horizontal, float radius)
        {
            if (amount == 0) return;
            float destination = (horizontal ? position.x : position.z) + amount;
            foreach (var cover in Covers)
            {
                float cross = horizontal ? position.z : position.x;
                float otherCenter = horizontal ? cover.Center.y : cover.Center.x;
                float otherHalf = horizontal ? cover.Half.y : cover.Half.x;
                if (Mathf.Abs(cross - otherCenter) >= otherHalf + radius) continue;
                float center = horizontal ? cover.Center.x : cover.Center.y;
                float half = horizontal ? cover.Half.x : cover.Half.y;
                float near = center - half - radius, far = center + half + radius;
                float old = horizontal ? position.x : position.z;
                if (amount > 0 && old <= near && destination > near) destination = near;
                if (amount < 0 && old >= far && destination < far) destination = far;
            }
            if (horizontal) position.x = destination; else position.z = destination;
        }

        /// <summary>Whether the straight segment between two points passes clear of every block.</summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            return !RayHitsCover(from, delta.normalized, delta.magnitude, out _);
        }

        /// <summary>
        /// Segment against the cover boxes in 3D, independent of colliders and layers: a ray that passes over a
        /// block's top clears it. hitDistance is measured along direction.
        /// </summary>
        public static bool RayHitsCover(Vector3 origin, Vector3 direction, float distance, out float hitDistance)
        {
            hitDistance = float.MaxValue;
            if (distance <= 0) return false;
            foreach (var cover in Covers)
            {
                float near = 0, far = distance;
                if (!ClipAxis(origin.x, direction.x, cover.Center.x - cover.Half.x, cover.Center.x + cover.Half.x, ref near, ref far)) continue;
                if (!ClipAxis(origin.z, direction.z, cover.Center.y - cover.Half.y, cover.Center.y + cover.Half.y, ref near, ref far)) continue;
                if (!ClipAxis(origin.y, direction.y, 0f, cover.Height, ref near, ref far)) continue;
                hitDistance = Mathf.Min(hitDistance, near);
            }
            return hitDistance <= distance;
        }

        private static bool ClipAxis(float origin, float direction, float min, float max, ref float near, ref float far)
        {
            if (Mathf.Abs(direction) < 0.0001f) return origin >= min && origin <= max;
            float a = (min - origin) / direction, b = (max - origin) / direction;
            if (a > b) { float temp = a; a = b; b = temp; }
            near = Mathf.Max(near, a);
            far = Mathf.Min(far, b);
            return near <= far;
        }
    }
}
