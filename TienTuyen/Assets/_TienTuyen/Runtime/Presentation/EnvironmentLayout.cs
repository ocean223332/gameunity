using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The shared map of the supply station: the flat playfield, the truck road
    /// that crosses it, the bombed valley to the south and the sites of the
    /// station buildings. Ground height and every generator read from here so
    /// terrain, ground paint, vegetation and props agree.
    /// </summary>
    internal static class EnvironmentLayout
    {
        // Mirrors CombatGame's movement clamp.
        public const float HalfX = 16.55f, HalfZ = 9.55f;
        public const float FlatMargin = 3.5f;

        /// <summary>Truck road centreline, west to east. It crosses the playfield diagonally.</summary>
        public static readonly Vector2[] Road =
        {
            new Vector2(-160, 46), new Vector2(-110, 34), new Vector2(-78, 18), new Vector2(-54, 4), new Vector2(-38, -3.4f),
            new Vector2(-25, -5.6f), new Vector2(-13, -3.6f), new Vector2(0, -.4f), new Vector2(12, 2.7f), new Vector2(23, 4.4f),
            new Vector2(34, 4f), new Vector2(46, .5f), new Vector2(64, -6), new Vector2(92, -13), new Vector2(160, -20)
        };

        /// <summary>
        /// Wheel track from the road into the truck depot, ending under the parked truck along its axis
        /// (EnvironmentStation.Depot: truck at (-27.5, -12), yaw 72). Painted as ruts only; the ground stays as GroundHeight.
        /// </summary>
        public static readonly Vector2[] DepotTrack =
        {
            new Vector2(-18.6f, -4.8f), new Vector2(-20.8f, -8f), new Vector2(-23.7f, -10.76f), new Vector2(-27.5f, -12f), new Vector2(-31.3f, -13.24f)
        };

        public struct Crater
        {
            public Vector2 Center;
            public float Radius, Depth, Floor;
            public bool Water;
            public Crater(float x, float z, float radius, float depth, bool water)
            { Center = new Vector2(x, z); Radius = radius; Depth = depth; Water = water; Floor = 0; }
            /// <summary>Height of standing water; the bowl rises above it beyond about half the radius.</summary>
            public float WaterLevel => Floor - Depth * .6f;
        }

        /// <summary>B-52 strike craters on the slope below the station.</summary>
        public static readonly Crater[] Craters = Settle(new[]
        {
            new Crater(-8, -19.5f, 3.2f, 1.3f, true), new Crater(5, -18, 2.4f, 1f, false), new Crater(14.5f, -23, 3.6f, 1.5f, true),
            new Crater(-18, -25, 3f, 1.2f, true), new Crater(-2, -27.5f, 4.2f, 1.7f, true), new Crater(24, -30, 3f, 1.2f, false),
            new Crater(9, -33, 3.4f, 1.4f, true), new Crater(-14, -36, 3.8f, 1.5f, true), new Crater(-28, -31, 2.6f, 1f, false),
            new Crater(30, -21, 2.8f, 1.1f, true), new Crater(0, -40, 3f, 1.2f, true), new Crater(19, -41, 3.6f, 1.4f, false),
            new Crater(-24, -44, 3.2f, 1.2f, true), new Crater(-36, -22, 2.6f, 1f, false), new Crater(36, -36, 3.4f, 1.3f, true),
            new Crater(-6, -50, 3.8f, 1.4f, true), new Crater(12, -52, 3f, 1.1f, false)
        });

        /// <summary>Station sites (x, z, radius) kept free of trees and undergrowth.</summary>
        public static readonly Vector3[] Sites =
        {
            new Vector3(-1, 17.6f, 7.5f),    // supply shed
            new Vector3(-11.5f, 16, 4f),     // A-frame bunker
            new Vector3(-22.5f, 15.5f, 3.6f),// watchtower
            new Vector3(21.5f, 14.5f, 4.5f), // cook fire and hammocks
            new Vector3(-26.5f, -11, 7.5f),  // truck depot
            new Vector3(13.5f, -17.5f, 4f),  // anti-aircraft pit
            new Vector3(24, 9.4f, 2.8f)      // bicycles at the trellis
        };

        // A field initializer rather than a static constructor: the type stays
        // beforefieldinit, so the bake threads' hot loops skip class-init checks.
        private static Crater[] Settle(Crater[] craters)
        {
            for (int i = 0; i < craters.Length; i++)
                craters[i].Floor = BaseHeight(craters[i].Center.x, craters[i].Center.y);
            return craters;
        }

        /// <summary>Distance from the playfield rectangle; zero inside it.</summary>
        public static float OutsideDistance(float x, float z)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x) - HalfX), dz = Mathf.Max(0, Mathf.Abs(z) - HalfZ);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Distance to the road centreline; <paramref name="side"/> is positive left of travel (west to east).</summary>
        public static float RoadDistance(float x, float z, out float side) => LineDistance(Road, x, z, out side);

        /// <summary>Distance to a polyline; <paramref name="side"/> is the signed distance, positive left of travel.</summary>
        public static float LineDistance(Vector2[] line, float x, float z, out float side)
        {
            float best = float.MaxValue;
            side = 0;
            var p = new Vector2(x, z);
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[i], segment = line[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / segment.sqrMagnitude);
                Vector2 offset = p - (a + segment * t);
                float d = offset.sqrMagnitude;
                if (d >= best) continue;
                best = d;
                side = segment.x * offset.y - segment.y * offset.x;
            }
            best = Mathf.Sqrt(best);
            side = side >= 0 ? best : -best;
            return best;
        }

        /// <summary>0..1: how much of the open, bombed south valley a point belongs to.</summary>
        public static float Valley(float x, float z) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-11.5f, -17f, z)) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(42f, 88f, Mathf.Abs(x))));

        /// <summary>
        /// 0..1: how bombed and bare the valley is. Its west flank, behind the truck depot, has grown back into
        /// forest, so the depot looks out at a jungle wall instead of an empty gap; the terrain keeps the valley's shape.
        /// </summary>
        public static float Bombed(float x, float z) =>
            Valley(x, z) * Mathf.SmoothStep(1, 0, Mathf.InverseLerp(-26f, -46f, x + (z + 20f) * .35f));

        /// <summary>Terrain without craters: flat station, jungle hills, a road cutting and the south valley.</summary>
        public static float BaseHeight(float x, float z)
        {
            float d = OutsideDistance(x, z) - FlatMargin;
            if (d <= 0) return 0;
            float near = Mathf.SmoothStep(0, 1, d / 26f) * (1.2f + ProceduralArt.Fbm(x * .045f, z * .045f, 3) * 7f);
            float far = Mathf.SmoothStep(0, 1, (d - 28f) / 70f) * (8f + ProceduralArt.Fbm(x * .012f + 40f, z * .012f, 4) * 42f);
            float h = near + far;
            // The road keeps a gentle grade through the hills.
            float road = RoadDistance(x, z, out _);
            h *= 1f - .78f * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3.5f, 10f, road)));
            // South of the station the plateau breaks into a long open valley.
            float valley = Valley(x, z);
            if (valley > 0)
            {
                float fall = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-13f, -40f, z));
                float floor = -9f * fall - 4f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-45f, -110f, z)) +
                              (ProceduralArt.Fbm(x * .035f + 7f, z * .035f, 3) - .5f) * 3.5f * fall;
                h = Mathf.Lerp(h, floor, valley);
            }
            return h;
        }

        /// <summary>Visual ground height including crater bowls and rims.</summary>
        public static float GroundHeight(float x, float z)
        {
            float d = OutsideDistance(x, z) - FlatMargin;
            if (d <= 0) return 0;
            float h = BaseHeight(x, z);
            if (z > -12f) return h;
            for (int i = 0; i < Craters.Length; i++)
            {
                var c = Craters[i];
                float dx = x - c.Center.x, dz = z - c.Center.y;
                float r2 = (dx * dx + dz * dz) / (c.Radius * c.Radius);
                if (r2 > 2.4f) continue;
                float r = Mathf.Sqrt(r2);
                // Level the slope under the crater so water lies flat in it.
                h = Mathf.Lerp(h, c.Floor, 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.85f, 1.45f, r)));
                if (r < 1f) h -= c.Depth * (1f - r2) * (1f - r2);
                float rim = (r - 1.04f) / .22f;
                h += c.Depth * .3f * Mathf.Exp(-rim * rim);
            }
            return h;
        }

        /// <summary>True when a point is inside a station site, enlarged by <paramref name="margin"/>.</summary>
        public static bool InSite(float x, float z, float margin)
        {
            foreach (var s in Sites)
            {
                float dx = x - s.x, dz = z - s.y, r = s.z + margin;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        /// <summary>Distance to the nearest crater rim, negative inside.</summary>
        public static float CraterDistance(float x, float z, out int index)
        {
            float best = float.MaxValue;
            index = -1;
            for (int i = 0; i < Craters.Length; i++)
            {
                float d = Vector2.Distance(new Vector2(x, z), Craters[i].Center) - Craters[i].Radius;
                if (d < best) { best = d; index = i; }
            }
            return best;
        }
    }
}
