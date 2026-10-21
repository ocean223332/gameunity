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

        /// <summary>How far the river's water lies below the plateau (m).</summary>
        public const float RiverDepth = 24f;
        public const float RiverLevel = -RiverDepth;
        /// <summary>Half width of the water, and of the flat paddy floodplain either side of it (m).</summary>
        public const float RiverHalfWidth = 14f, RiverPlain = 60f, FarPlain = 30f;
        // The plateau's edge in front of a standing eye at the south side of the playfield lets the view fall at most
        // about 0.156 (9 degrees) below level, so a deeper river has to lie farther out to stay in sight: its near
        // bank sits where a depression of 0.14 reaches the water, a few metres to spare.
        private const float RiverSightSlope = .14f, RiverEye = 1.6f, RiverViewZ = -4.6f;
        private const float RiverCentreZ = RiverViewZ - (RiverDepth + RiverEye) / RiverSightSlope - RiverHalfWidth - 6f;
        // The valley's north side is a plane from the shelled slope's foot (z -60, 9.5 m down; craters and station stay
        // above it) to the floodplain. It always falls faster than that view, so the water is never hidden behind it.
        private const float RiverAnchorZ = -60f, RiverAnchorHeight = -9.5f;
        private const float RiverAnchorSide = RiverAnchorZ - RiverCentreZ;
        private const float RiverNorthGrade = (RiverAnchorHeight - (RiverLevel + .9f)) / (RiverAnchorSide - RiverHalfWidth - RiverPlain);

        /// <summary>How far south the terrain mesh must reach to carry the river, its far floodplain and the bank above it.</summary>
        public const float RiverReach = -RiverCentreZ + RiverHalfWidth + FarPlain + 60f;

        /// <summary>
        /// River centreline, west to east, across the south valley. Its bends only ever lie farther than
        /// <see cref="RiverCentreZ"/> in front of the station; far east and west it turns north between the hills.
        /// </summary>
        public static readonly Vector2[] River = RiverLine(new[]
        {
            new Vector2(-1600, 520), new Vector2(-900, 210), new Vector2(-560, 80), new Vector2(-380, 26), new Vector2(-250, -4),
            new Vector2(-150, 0), new Vector2(-70, -6), new Vector2(0, -12), new Vector2(80, -4), new Vector2(160, -16),
            new Vector2(250, 6), new Vector2(370, 40), new Vector2(560, 110), new Vector2(900, 240), new Vector2(1600, 560)
        });

        private static Vector2[] RiverLine(Vector2[] bends)
        {
            for (int i = 0; i < bends.Length; i++) bends[i].y += RiverCentreZ;
            return bends;
        }

        // ---------- The karst wall and its falls ----------

        /// <summary>
        /// Where the water leaves the karst (x): the main fall, two tiers with a pool on the ledge between them, and
        /// its taller, slender twin off the tower beside it. Both sit just left of the low south-western shaft sun the
        /// presets share (CombatEnvironment) as the station looks south, so they are backlit.
        /// </summary>
        public const float FallX = -108f, TwinFallX = -190f;
        /// <summary>The karst wall runs along the river's south bank between these x (m), tapering at both ends.</summary>
        public const float KarstWest = -300f, KarstEast = -30f;

        /// <summary>0..1 along x: how much of the karst wall's river face stands there.</summary>
        public static float KarstMask(float x) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(KarstWest, KarstWest + 25f, x)) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(KarstEast - 50f, KarstEast, x)));

        private static float Gauss(float x, float centre, float width) { float t = (x - centre) / width; return Mathf.Exp(-t * t); }

        /// <summary>Height of the wall's top edge: about 70 m above the water, the twin's tower 30 m more, notches where the falls leave.</summary>
        public static float KarstCrest(float x) =>
            RiverLevel + 70f + 28f * (ProceduralArt.Fbm(x * .017f, 5.1f, 2) - .5f) + 32f * TwinTower(x)
            - 5f * Gauss(x, FallX, 9f) - 4f * Gauss(x, TwinFallX, 4f) - 30f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(KarstEast - 60f, KarstEast, x));

        /// <summary>The twin's tower over the crest: 0..1, flat-topped with rounded shoulders, about 60 m across.</summary>
        private static float TwinTower(float x)
        {
            float t = Mathf.Clamp01((1f - Mathf.Abs(x - TwinFallX) / 34f) / .45f);
            return t * t * (3f - 2f * t) * (1f - .12f * (x - TwinFallX) * (x - TwinFallX) / (34f * 34f));
        }

        /// <summary>How far south of the river's centre the wall rises out of the water: buttresses and bays, closer in under the falls.</summary>
        public static float KarstFoot(float x) =>
            16f + 5f * ProceduralArt.Fbm(x * .025f, 7.7f, 2) + 3f * (1f - Mathf.Abs(2f * ProceduralArt.Fbm(x * .14f, 2.3f, 1) - 1f))
            - 6f * Gauss(x, FallX, 22f) - 5f * Gauss(x, TwinFallX, 12f);

        /// <summary>Height of the ledge the main fall breaks on, and of the pool on it.</summary>
        public static float KarstLedge(float x) => RiverLevel + .45f * (KarstCrest(x) - RiverLevel);

        /// <summary>
        /// The karst raised over the ground <paramref name="h"/> at distance <paramref name="a"/> south of the river's
        /// centre: a sheer wall rising out of the water to its crest, stepped under the main fall into two tiers with a
        /// pool on the ledge, and tower karst on the plateau behind. The river face ends square at KarstWest; behind it
        /// the plateau's west edge wanders, and towers stand on out over the hills west of it. Past the terrain mesh
        /// the plateau sinks into the hills and only its towers go on, straggling farther west the farther out.
        /// </summary>
        private static float Karst(float x, float z, float a, float h)
        {
            float u = a - KarstFoot(x);
            float east = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(KarstEast - 50f, KarstEast, x));
            if (u <= 0 || east <= 0) return h;
            float past = Mathf.SmoothStep(0, 1, (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - EnvironmentGround.OuterExtent) / 220f);
            float back = Mathf.SmoothStep(0, 1, (u - 20f) / 40f);
            float west = KarstWest + ((ProceduralArt.Fbm(z * .012f + 5.3f, x * .004f - 2.1f, 2) - .5f) * 60f - 10f) * back;
            float wallMask = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(west, west + 25f, x)) * east;
            float towerMask = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(west - 70f * back - 180f * past, west + 25f - 40f * back, x)) * east;
            if (towerMask <= 0) return h;
            float crest = KarstCrest(x), low = RiverLevel - 1f;
            float single = low + (crest - low) * Mathf.SmoothStep(0, 1, u / 10f);
            float ledge = KarstLedge(x);
            float pool = 1f - (u - 9.5f) * (u - 9.5f) / (3.2f * 3.2f);
            float two = low + (ledge - low) * Mathf.SmoothStep(0, 1, u / 5f) - 1.4f * (pool > 0 ? pool * pool : 0) * Gauss(x, FallX, 12f)
                        + (crest - ledge) * Mathf.SmoothStep(0, 1, (u - 14f) / 6f);
            float wall = Mathf.Lerp(Mathf.Lerp(single, two, Gauss(x, FallX, 26f)), h, past);
            float towers = Cones(x, z) * Mathf.SmoothStep(0, 1, (u - 12f) / 20f);
            return h + Mathf.Max(0, wall + towers - h) * wallMask + towers * Mathf.Max(0, towerMask - wallMask);
        }

        /// <summary>
        /// Tower karst on the plateau, one per 55 m cell, 18 to 52 m high: round towers and long fins, sheer-sided with a
        /// rounded, weathered crown, their outlines lobed by the weather. Never a spike.
        /// </summary>
        private static float Cones(float x, float z)
        {
            const float cell = 55f;
            int ci = Mathf.FloorToInt(x / cell), ck = Mathf.FloorToInt(z / cell);
            float best = 0;
            float lobes = (ProceduralArt.Fbm(x * .06f + 11f, z * .06f - 4f, 2) - .5f) * .3f;
            for (int k = ck - 1; k <= ck + 1; k++)
            for (int i = ci - 1; i <= ci + 1; i++)
            {
                float h1 = Hash01(i, k, 1), h2 = Hash01(i, k, 2), h3 = Hash01(i, k, 3), h4 = Hash01(i, k, 4);
                float dx = x - (i + .2f + .6f * h1) * cell, dz = z - (k + .2f + .6f * h2) * cell;
                // A fin is a tower drawn out along its own axis.
                float fin = h4 > .55f ? 1f : 0f, angle = h3 * Mathf.PI;
                float along = dx * Mathf.Cos(angle) + dz * Mathf.Sin(angle), across = -dx * Mathf.Sin(angle) + dz * Mathf.Cos(angle);
                float r = 16f + 9f * h3;
                float q = Mathf.Sqrt(along * along / (1f + 2.2f * fin) / (1f + 2.2f * fin) + across * across) / r + lobes;
                if (q >= 1f) continue;
                float height = Mathf.Min(18f + 24f * h1 * h2 + 10f * h3, 1.8f * r);   // never taller than it is wide: no needles
                // Sheer sides over the outer third, then a domed crown.
                float side = Mathf.SmoothStep(0, 1, (1f - q) / .32f), crown = 1f - .3f * q * q;
                best = Mathf.Max(best, height * side * crown);
            }
            return best;
        }

        private static float Hash01(int i, int k, int salt)
        {
            uint h = unchecked((uint)i * 73856093u ^ (uint)k * 19349663u ^ (uint)salt * 83492791u);
            h ^= h >> 13;
            h = unchecked(h * 0x5bd1e995u);
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }

        // ---------- Viewpoint knoll and hamlet ----------

        /// <summary>The wooded knoll north-east of the station whose top looks out over the valley (view V).</summary>
        public static readonly Vector2 Knoll = new Vector2(-105f, 85f);
        public const float KnollHeight = 28f, KnollRadius = 60f;

        /// <summary>The riverside hamlet: every house standing, some burnt (the war has passed through), or all burnt.</summary>
        public enum HamletState { Standing, Mixed, Burnt }
        public const HamletState Hamlet = HamletState.Mixed;

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

        /// <summary>0..1: how much of the open, bombed south valley a point belongs to. It ends above the river's floodplain.</summary>
        public static float Valley(float x, float z) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-11.5f, -17f, z)) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(42f, 88f, Mathf.Abs(x)))) *
            (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-110f, -150f, z)));

        /// <summary>Signed distance to the river's centreline, positive on the north (station) side.</summary>
        public static float RiverSide(float x, float z)
        {
            LineDistance(River, x, z, out float side);
            return side;
        }

        /// <summary>
        /// The river valley's cross-section at signed distance <paramref name="s"/> from the water's centre: the bed,
        /// a flat paddy floodplain either side, then the valley sides. The north side rises at RiverNorthGrade to the
        /// shelled slope's foot and steeply past it, so it never cuts into the craters; the south side climbs at 0.26
        /// and steepens into the hills.
        /// </summary>
        public static float RiverValley(float s)
        {
            float a = Mathf.Abs(s);
            if (a < RiverHalfWidth) { float u = a / RiverHalfWidth; return RiverLevel - 1.6f * (1f - u * u); }
            float plain = s > 0 ? RiverPlain : FarPlain, onPlain = a - RiverHalfWidth;
            float h = RiverLevel + .5f * Mathf.SmoothStep(0, 1, onPlain / 3f) + .4f * Mathf.Clamp01(onPlain / plain);
            float side = Mathf.Max(0, onPlain - plain);
            if (s > 0)
            {
                float above = Mathf.Max(0, s - RiverAnchorSide);
                return h + side * RiverNorthGrade + above * above * .03f;
            }
            float steep = Mathf.Max(0, a - 150f);
            return h + side * .26f + steep * steep * .012f;
        }

        /// <summary>
        /// 0..1 away from the open south and east: the foothills that carry the hills up to the ranges keep low within
        /// about 40 degrees either side of due south, so the river runs out between the karst rather than into a wall,
        /// and about 45 degrees either side of due east, where the intro's crane looks out over the clearing to the ranges.
        /// </summary>
        private static float Foothills(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r < 150f) return 0;
            float south = Mathf.Max(0, -z / r), east = Mathf.Max(0, x / r);   // cos of the angle from due south, due east
            return Mathf.SmoothStep(0, 1, (r - 150f) / 250f) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.62f, .85f, south)))
                * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, .8f, east)));
        }

        private static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(.5f + .5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        /// <summary>
        /// 0..1: how bombed and bare the valley is. Its west flank, behind the truck depot, has grown back into
        /// forest, so the depot looks out at a jungle wall instead of an empty gap; the terrain keeps the valley's shape.
        /// </summary>
        public static float Bombed(float x, float z) =>
            Valley(x, z) * Mathf.SmoothStep(1, 0, Mathf.InverseLerp(-26f, -46f, x + (z + 20f) * .35f));

        /// <summary>Terrain without craters: flat station, upland hills, a road cutting and the south valley.</summary>
        public static float BaseHeight(float x, float z)
        {
            float d = OutsideDistance(x, z) - FlatMargin;
            if (d <= 0) return 0;
            float near = Mathf.SmoothStep(0, 1, d / 26f) * (1.2f + ProceduralArt.Fbm(x * .045f, z * .045f, 3) * 7f);
            float ridges = Ridges(x, z);
            float far = Mathf.SmoothStep(0, 1, (d - 28f) / 70f) * ridges;
            float h = near + far;
            // The road keeps a gentle grade through the hills, in a valley that opens as wide as the hill it crosses
            // is high (side slopes of about 30 degrees, wandering a little), never a slot cut straight down.
            float road = RoadDistance(x, z, out _);
            float valleySide = (4f + h * 1.2f) * (.8f + .4f * ProceduralArt.Fbm(x * .02f - 31f, z * .02f + 9f, 2));
            h *= 1f - .78f * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(3.5f, 3.5f + valleySide, road)));
            // South of the station the plateau breaks into a long open valley.
            float valley = Valley(x, z);
            if (valley > 0)
            {
                float fall = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-13f, -40f, z));
                float floor = -9f * fall - 4f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-45f, -110f, z)) +
                              (ProceduralArt.Fbm(x * .035f + 7f, z * .035f, 3) - .5f) * 3.5f * fall;
                h = Mathf.Lerp(h, floor, valley);
            }
            // Past the station the hills climb on into foothills, to meet the ranges at their own height.
            float foot = Foothills(x, z);
            if (foot > 0) h += foot * (30f + 55f * ProceduralArt.Fbm(x * .004f - 3f, z * .004f + 11f, 2));
            // The knoll: a flat-topped rise, nothing of it within 60 m of the station.
            float knoll = 1f - Vector2.Distance(new Vector2(x, z), Knoll) / KnollRadius;
            if (knoll > 0) h += KnollHeight * Mathf.SmoothStep(0, 1, Mathf.Min(1f, knoll * 1.15f));
            // The river valley cuts through all of it, and the karst wall rises sheer out of its south side. Up its
            // north side gullies run down towards the floodplain with ribs between them, and away from shot 1's view of
            // the river the hills' spurs come down too, so the valley side reads as hill country rather than one smooth
            // ramp (none of it near the station). Gullies only cut down, so they never hide the water.
            float s = RiverSide(x, z);
            float riverValley = RiverValley(s), up = s - RiverHalfWidth - RiverPlain, carve = 0, blend = 3f;
            // West of the karst the south bank is hill country too: spurs down to the plain and a rounder shoulder,
            // coming in under the towers that straggle west of the wall's end.
            float westBank = s < 0 ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(KarstWest, KarstWest - 80f, x)) : 0;
            float upSouth = -s - RiverHalfWidth - FarPlain;
            if (westBank > 0 && upSouth > 0)
            {
                riverValley += .6f * ridges * Mathf.SmoothStep(0, 1, upSouth / 40f) * westBank;
                blend += 15f * westBank;
            }
            if (up > 0 && up < 200f)
            {
                float fromStation = Mathf.Sqrt(x * x + z * z);
                float reach = Mathf.SmoothStep(0, 1, (fromStation - 70f) / 50f);
                float offView = Mathf.SmoothStep(0, 1, (Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(x, z) * Mathf.Rad2Deg, 186f)) - 40f) / 20f);
                riverValley += .6f * ridges * Mathf.SmoothStep(0, 1, up / 40f) * reach * offView;
                // Where the valley cuts the hills, a rounder shoulder, and gullies down the cut face itself (off the road).
                blend += 7f * reach;
                float wander = (ProceduralArt.Fbm(x * .01f + 3f, s * .01f, 2) - .5f) * 2.5f;
                float gully = 1f - Mathf.Abs(Mathf.PerlinNoise(x * .028f + wander, s * .007f + 5f) * 2f - 1f);
                carve = 9f * gully * gully * gully * Mathf.SmoothStep(0, 1, up / 30f) * (1f - Mathf.SmoothStep(0, 1, (up - 140f) / 60f)) * reach
                        * Mathf.SmoothStep(0, 1, (road - 6f) / 10f);
            }
            h = SmoothMin(h, riverValley, blend) - carve;
            if (s < 0) h = Karst(x, z, -s, h);
            return h;
        }

        /// <summary>
        /// The hills past the station, 4 to about 50 m: broad massifs carved into spurs and gullies by ridged noise,
        /// its finer octaves only on the crests so the valley floors stay smooth. A slow domain warp bends the ridges.
        /// </summary>
        private static float Ridges(float x, float z)
        {
            float wx = x + (ProceduralArt.Fbm(x * .0045f - 13f, z * .0045f + 5f, 2) - .5f) * 90f;
            float wz = z + (ProceduralArt.Fbm(x * .0045f + 29f, z * .0045f - 17f, 2) - .5f) * 90f;
            float broad = ProceduralArt.Fbm(wx * .006f + 40f, wz * .006f, 3);
            float ridges = 0, amplitude = 1, weight = 1, frequency = .0075f, total = 0;
            for (int o = 0; o < 3; o++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(wx * frequency + o * 31.7f, wz * frequency - o * 13.3f) * 2f - 1f);
                n *= n;
                ridges += n * weight * amplitude;
                total += amplitude;
                weight = Mathf.Clamp01(n * 1.4f);
                amplitude *= .45f;
                frequency *= 2.1f;
            }
            return 4f + broad * 24f + ridges / total * (8f + 30f * broad);
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
