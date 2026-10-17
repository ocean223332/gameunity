using UnityEngine;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The flora of map Phase 2's riverlands: bamboo along both banks, thickest around the hamlet and the bridge
    /// tower; banana and areca palms between the stilt houses; ferns and broad-leaved herbs on the karst ledges, vines
    /// hanging from their lips (EnvironmentRiverlands.KarstLedges); jungle on the plateau and its cones; big rocks with climbers at the wall's foot. Allow keeps the water, the paddies, the wall faces, the
    /// falls, the bridge, the ground in front of the houses and view V's knoll open.
    /// </summary>
    internal static partial class EnvironmentFlora
    {
        // Points over the river are given as (x, metres north of the river's centre), the way EnvironmentRiverlands
        // places the houses and the bridge.
        private const float BankStrip = 7f, FallClear = 20f, OpenHeight = 1.2f, Cliff = 1.1f;
        private static readonly Vector2 ViewTarget = new Vector2(-80f, -215f);
        private static int bamboo, bananaA, bananaB, bananaC, arecaA, arecaB, palmYoungA, palmYoungB, vineA, vineB, vineMonstera,
            climberA, climberB, curtainA, curtainB, fernD, fernE, earA, earB, heliconiaA, heliconiaB, ginger, jungleA, jungleB, jungleTall;

        static partial void Riverlands()
        {
            bamboo = biome.IndexOf("Bamboo");
            bananaA = biome.IndexOf("Banana_A"); bananaB = biome.IndexOf("Banana_B"); bananaC = biome.IndexOf("Banana_C");
            arecaA = Or("Areca_A", "Palm_A"); arecaB = Or("Areca_B", "Palm_A");
            palmYoungA = biome.IndexOf("PalmYoung_A"); palmYoungB = biome.IndexOf("PalmYoung_B");
            vineA = biome.IndexOf("Vine_A"); vineB = biome.IndexOf("Vine_B"); vineMonstera = biome.IndexOf("Vine_Monstera");
            climberA = biome.IndexOf("Climber_A"); climberB = biome.IndexOf("Climber_B");
            curtainA = biome.IndexOf("Curtain_A"); curtainB = biome.IndexOf("Curtain_B");
            fernD = Or("Fern_D", "Fern_A"); fernE = Or("Fern_E", "Fern_B");
            earA = biome.IndexOf("ElephantEar_A"); earB = biome.IndexOf("ElephantEar_B");
            heliconiaA = biome.IndexOf("Heliconia_A"); heliconiaB = biome.IndexOf("Heliconia_B"); ginger = biome.IndexOf("RedGinger");
            jungleA = biome.IndexOf("JungleTree_A"); jungleB = biome.IndexOf("JungleTree_B"); jungleTall = biome.IndexOf("JungleTree_Tall");
            float outer = EnvironmentGround.OuterExtent - 6f;
            Scatter(-outer, outer, -outer, outer, 3.5f, FarBand, float.MaxValue, Bank);
            KarstLedges();
            Scatter(-outer, outer, -outer, outer, 6f, FarBand, float.MaxValue, Plateau);
            WallFoot();
            foreach (var house in EnvironmentRiverlands.Houses)
            {
                // Palms beside and behind each house, bamboo farther back; Allow drops any that land in front.
                for (int i = 0, n = 3 + random.Next(3); i < n; i++)
                {
                    float angle = Range(-100f, 100f) * Mathf.Deg2Rad, r = house.Width * .5f + Range(2.5f, 8f);
                    float px = house.X + Mathf.Sin(angle) * r, side = house.Side + Mathf.Cos(angle) * r;
                    Plant(Chance(.6) ? Pick(bananaA, bananaB, bananaC) : Pick(arecaA, arecaB), px, SideToZ(px, side), Range(.8f, 1.15f), .1f);
                }
                // Behind the house a drift of one herb, the way a garden plot reads.
                float gx = house.X + Range(-house.Width, house.Width), gside = house.Side + house.Depth * .5f + Range(3f, 7f);
                Patch(gx, gside, Pick(earA, earB, heliconiaA, palmYoungA), 2.5f, 4 + random.Next(4));
                for (int i = 0; i < 3; i++)
                {
                    float angle = Range(-70f, 70f) * Mathf.Deg2Rad, r = house.Width * .5f + Range(8f, 14f);
                    BambooClump(bamboo, house.X + Mathf.Sin(angle) * r, house.Side + Mathf.Cos(angle) * r);
                }
            }
            // The bridge's north tower stands in a bamboo grove.
            for (int i = 0; i < 10; i++)
            {
                float angle = Range(0f, 360f) * Mathf.Deg2Rad, r = Range(4f, 13f);
                BambooClump(bamboo, EnvironmentRiverlands.BridgeX + Mathf.Sin(angle) * r, TowerSide + Mathf.Cos(angle) * r);
            }
        }

        private const float TowerSide = 24f;

        // Trial switch for the lane (vista #4133): judged on shot 1 before/after, dropped if it adds no depth.
        private const bool RiverLaneOn = true;
        private static readonly Vector2 LaneEye = new Vector2(.9f, -4.6f);

        /// <summary>
        /// 0..1: the lane from the station's south edge down to the river (bearing about 187 degrees, 80 to 210 m
        /// out). Its width and centre wander with distance and its edges are soft, so it reads as a natural gap that
        /// leads the eye, not a cut corridor.
        /// </summary>
        private static float RiverLane(float x, float z)
        {
            Vector2 d = new Vector2(x, z) - LaneEye;
            float distance = d.magnitude;
            if (distance < 70f || distance > 230f) return 0f;
            float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            if (bearing < 0f) bearing += 360f;
            float wander = Mathf.PerlinNoise(distance * .02f + 5.3f, 1.7f) - .5f;
            float centre = 187f + wander * 10f, half = 9f + (Mathf.PerlinNoise(distance * .035f, 8.1f) - .5f) * 8f;
            float across = Mathf.Abs(Mathf.DeltaAngle(bearing, centre));
            float alongIn = Smooth(70f, 95f, distance) * (1f - Smooth(195f, 230f, distance));
            return (1f - Smooth(half * .6f, half * 1.3f, across)) * alongIn;
        }

        private static int Or(string name, string fallback)
        {
            int i = biome.IndexOf(name);
            return i >= 0 ? i : biome.IndexOf(fallback);
        }

        /// <summary>
        /// One species from the list, the same over a patch some ten metres across: herbs and flowers grow in
        /// single-species drifts, as the packs' demos lay them, not as an even mix.
        /// </summary>
        private static int PatchPick(float x, float z, params int[] species)
        {
            float n = Mathf.PerlinNoise(x * .09f + 311f, z * .09f - 57f);
            return species[Mathf.Clamp((int)(n * species.Length * 1.4f - species.Length * .2f), 0, species.Length - 1)];
        }

        /// <summary>A tight drift of one species around (x, side), at the wide scale spread the demos use.</summary>
        private static void Patch(float x, float side, int species, float radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float px = x + Range(-radius, radius);
                Plant(species, px, SideToZ(px, side + Range(-radius, radius)), Range(.55f, 1.4f), .05f);
            }
        }

        /// <summary>The jungle's ground layer: a fern and broad-leaf carpet in patches, now and then a flowering clump.</summary>
        private static void Carpet(float x, float z)
        {
            if (Chance(.04)) { Patch(x, L.RiverSide(x, z), Pick(heliconiaA, heliconiaB, ginger), 1.5f, 3); return; }
            Plant(PatchPick(x, z, fernD, fernE, earA, palmYoungB, earB, fernD), x, z, Range(.45f, 1.5f), .05f);
        }

        /// <summary>Metres from (x, side) to the line from behind the bridge's north tower to the main fall's foot.</summary>
        private static float FallSightline(float x, float side)
        {
            Vector2 a = new Vector2(EnvironmentRiverlands.BridgeX, TowerSide + 12f), b = new Vector2(L.FallX, -L.KarstFoot(L.FallX));
            Vector2 ab = b - a, p = new Vector2(x, side);
            return Vector2.Distance(p, a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude));
        }

        /// <summary>The world z of the point <paramref name="side"/> metres north of the river's centre at x.</summary>
        private static float SideToZ(float x, float side) => EnvironmentRiverlands.RiverZ(x) + side;

        /// <summary>The steepest ground slope at (x, z), rise over run.</summary>
        private static float Slope(float x, float z) =>
            Mathf.Max(Mathf.Abs(L.GroundHeight(x + 1f, z) - L.GroundHeight(x - 1f, z)), Mathf.Abs(L.GroundHeight(x, z + 1f) - L.GroundHeight(x, z - 1f))) * .5f;

        private static bool OnKarst(float x, float side) => L.KarstMask(x) > .15f && -side > L.KarstFoot(x);

        private static void BambooClump(int bamboo, float x, float side)
        {
            for (int i = 0, n = 2 + random.Next(3); i < n; i++)
            {
                float px = x + Range(-1.5f, 1.5f);
                Plant(bamboo, px, SideToZ(px, side + Range(-1.5f, 1.5f)), Range(.8f, 1.25f), .1f);
            }
        }

        /// <summary>0..1: how close (x, side) is to the hamlet or the bridge tower, where the bamboo is thickest.</summary>
        private static float Settled(float x, float side)
        {
            float tower = Mathf.Exp(-((x - EnvironmentRiverlands.BridgeX) * (x - EnvironmentRiverlands.BridgeX) + (side - TowerSide) * (side - TowerSide)) / (30f * 30f));
            float hamlet = side > 0 ? 1f - Smooth(0f, 25f, Mathf.Max(64f - x, x - 156f)) : 0f;
            return Mathf.Max(tower, hamlet);
        }

        /// <summary>Bamboo along the banks, between the water and the paddies, thickest at the hamlet and the tower.</summary>
        private static void Bank(float x, float z, float d)
        {
            float side = L.RiverSide(x, z), bank = Mathf.Abs(side) - L.RiverHalfWidth;
            // The strip's inner edge wanders and long stretches thin out, so the bank never reads as a planted hedge.
            float edge = Mathf.PerlinNoise(x / 17f, side > 0 ? 3.1f : 7.9f), stretch = Mathf.PerlinNoise(x / 41f + 11.3f, side > 0 ? 5.2f : 1.4f);
            if (bank < .5f + 3.5f * edge || bank > BankStrip || OnKarst(x, side)) return;
            if (Chance((.22f + .7f * Settled(x, side)) * Mathf.Clamp01(stretch * 2.4f - .5f))) BambooClump(bamboo, x, side);
        }

        /// <summary>
        /// The karst wall's ledges and crest blocks (EnvironmentRiverlands.KarstLedges): vine curtains hang from the
        /// lips in runs, the stone bare between them so the wall's form reads; ferns and herbs fringe each shelf, the
        /// front ones leaning out over the lip.
        /// </summary>
        private static void KarstLedges()
        {
            onSkin = true;
            foreach (var (lip, outward, drop, shelf) in EnvironmentRiverlands.KarstLedges)
            {
                // Runs about 25 m long; about three quarters of the wall's length carries a curtain, thick in the strong runs.
                float run = Mathf.PerlinNoise(lip.x / 23f + 4.1f, lip.y / 9f + (outward.z > 0 ? 7.7f : 1.3f));
                var along = new Vector3(outward.z, 0f, -outward.x);
                // In the strong runs a curtain from every ledge (they stand 3 m apart and are about as wide), so the run
                // reads as one green fall of leaves; strands fringe the runs and fill drops too short for a curtain.
                int curtain = drop * .9f >= 6f ? curtainB : curtainA;
                bool sheet = run > .4f && drop > 3f && curtain >= 0;
                if (sheet)
                {
                    var at = lip + outward * .3f + along * Range(-.4f, .4f);
                    Hang(curtain, at.x, at.z, lip.y + .1f, Mathf.Min(Range(.85f, 1.25f), drop * .9f / Height(curtain)),
                        Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg + Range(-12f, 12f));
                }
                if (drop > 2.5f && run > .32f)
                    for (int i = 0, n = sheet ? random.Next(2) : run > .55f ? 4 + random.Next(3) : 2 + random.Next(2); i < n; i++)
                    {
                        int vine = Chance(.2) ? vineMonstera : Pick(vineA, vineB, vineB);
                        if (vine < 0) continue;
                        var at = lip + along * Range(-1.6f, 1.6f) + outward * Range(.25f, .6f);
                        Hang(vine, at.x, at.z, lip.y + .1f, Mathf.Min(Range(1.1f, 2.2f), drop * .85f / Height(vine)));
                    }
                if (shelf < .7f || !Chance(.35 + .6 * run)) continue;
                for (int i = 0, n = 2 + random.Next(shelf > 2f ? 5 : 3); i < n; i++)
                {
                    var at = lip + along * Range(-1.4f, 1.4f) - outward * Range(.2f, shelf * .8f);
                    Sit(Chance(.06) ? Pick(heliconiaA, ginger) : PatchPick(at.x, at.z, fernD, earA, fernE, earB, climberA, palmYoungA, fernD),
                        at, Range(.8f, 1.6f), .05f);
                }
            }
            onSkin = false;
        }

        /// <summary>Places a plant at a height of its own (a ledge of the karst skin, not the terrain under it).</summary>
        private static void Sit(int species, Vector3 at, float scale, float sink)
        {
            if (species < 0) return;
            bool allow = true;
            Allow(species, at.x, at.z, Height(species) * scale, ref allow);
            if (!allow) return;
            placed.Add(new FloraInstance { Species = species, Position = at - Vector3.up * sink * scale, Yaw = Range(0f, 360f), Scale = scale });
        }

        /// <summary>True while placing on the karst skin: the terrain under a ledge says nothing about the ledge, so Allow skips its slope and karst tests.</summary>
        private static bool onSkin;

        /// <summary>Places a hanging plant with its top at <paramref name="top"/>, turned to <paramref name="yaw"/> (degrees; random if NaN).</summary>
        private static void Hang(int species, float x, float z, float top, float scale, float yaw = float.NaN)
        {
            bool allow = true;
            Allow(species, x, z, Height(species) * scale, ref allow);
            if (!allow) return;
            placed.Add(new FloraInstance
            {
                Species = species,
                Position = new Vector3(x, top - (Height(species) - .15f) * scale, z),
                Yaw = float.IsNaN(yaw) ? Range(0f, 360f) : yaw,
                Scale = scale
            });
        }

        /// <summary>Jungle over the plateau behind the wall and up its cones: broadleaf crowns, palms and bamboo over undergrowth.</summary>
        private static void Plateau(float x, float z, float d)
        {
            float side = L.RiverSide(x, z);
            if (!OnKarst(x, side) || -side < L.KarstFoot(x) + 12f || L.GroundHeight(x, z) < L.KarstCrest(x) - 15f) return;
            // Few, large trees over a full ground layer, the Jungle demo's proportions; a rare giant breaks the canopy.
            float roll = (float)random.NextDouble();
            if (roll < .05f && jungleTall >= 0) Plant(jungleTall, x, z, Range(.9f, 1.25f), .3f);
            else if (roll < .42f) Plant(jungleA >= 0 && Chance(.7) ? Pick(jungleA, jungleB) : Broadleaf(), x, z, Range(.8f, 1.2f), .25f);
            else if (roll < .55f) Plant(Pick(arecaA, arecaB), x, z, Range(.9f, 1.3f), .1f);
            else if (roll < .65f) Plant(bamboo, x, z, Range(.9f, 1.3f), .1f);
            for (int i = 0, n = 2 + random.Next(3); i < n; i++)
                Carpet(x + Range(-3f, 3f), z + Range(-3f, 3f));
        }

        /// <summary>Big fallen blocks along the wall's foot where it stands on land, clear of both falls.</summary>
        private static void WallFoot()
        {
            for (float x = L.KarstWest + 20f; x < L.KarstEast - 10f; x += Range(7f, 14f))
            {
                float a = L.KarstFoot(x) + Range(-1f, 2.5f);
                if (L.KarstMask(x) < .3f || a < L.RiverHalfWidth + 1f || Mathf.Abs(x - L.TwinFallX) < 8f) continue;
                Plant(Pick(rockFace, outcropA, outcropB, outcropC), x, SideToZ(x, -a), Range(.9f, 1.6f), .4f);
                // Climbers over the block's foot and up the wall behind it.
                for (int i = 0, n = 2 + random.Next(3); i < n; i++)
                {
                    float px = x + Range(-3f, 3f);
                    Plant(Pick(climberA, climberB), px, SideToZ(px, -a + Range(-2.5f, 1f)), Range(.7f, 1.4f), .05f);
                }
            }
        }

        static partial void Allow(int species, float x, float z, float height, ref bool allow)
        {
            float side = L.RiverSide(x, z), off = Mathf.Abs(side) - L.RiverHalfWidth;
            bool rock = species == rockFace || species == outcropA || species == outcropB || species == outcropC;
            bool hangs = species == vineA || species == vineB || species == vineMonstera || species == curtainA || species == curtainB;
            // The water, and the paddies past the bank strip except where the hamlet stands.
            float plain = side > 0 ? L.RiverPlain : L.FarPlain;
            bool karst = onSkin || OnKarst(x, side);
            if (off < .5f || (!karst && off > BankStrip && off < plain && Settled(x, side) < .5f)) { allow = false; return; }
            // The falls' paths and plunge pools, the bridge's deck line, and the wall faces.
            if (side < 0 && -side < L.KarstFoot(x) + 30f && (Mathf.Abs(x - L.FallX) < FallClear || Mathf.Abs(x - L.TwinFallX) < 8f)) { allow = false; return; }
            if (Mathf.Abs(x - EnvironmentRiverlands.BridgeX) < 3f && side > -30f && side < TowerSide + 3f) { allow = false; return; }
            if (!rock && !hangs && !onSkin && Slope(x, z) > Cliff) { allow = false; return; }
            if (height <= OpenHeight) return;
            // The falls seen from the bridge's north tower (shot F): no tall plant between them.
            if (FallSightline(x, side) < 5f) { allow = false; return; }
            // Shot 1's look down to the river: inside the lane only the lower trees stand.
            if (RiverLaneOn && height > Mathf.Lerp(60f, 3f, RiverLane(x, z))) { allow = false; return; }
            // In front of each house, between it and the river, stays open.
            foreach (var house in EnvironmentRiverlands.Houses)
                if (Mathf.Abs(x - house.X) < house.Width * .5f + 2f && side > house.Side - house.Depth * .5f - 12f && side < house.Side + house.Depth * .5f + 1f)
                { allow = false; return; }
            // View V's knoll: low scrub only on its top and its south-west face.
            Vector2 fromKnoll = new Vector2(x, z) - L.Knoll;
            float r = fromKnoll.magnitude;
            if (r < 18f || (r < L.KnollRadius + 8f && Vector2.Angle(fromKnoll, ViewTarget - L.Knoll) < 50f)) allow = false;
        }
    }
}
