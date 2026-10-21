using System.Collections.Generic;
using UnityEngine;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Plants the highland mixed forest in bands by distance from the playfield: knee-high herbs, ferns, stones
    /// and dead wood where the shoulder camera can reach; a soft edge of young firs, pines and shrubs behind them;
    /// then groves of full firs and Pinus kesiya with a younger tier and undergrowth, broadleaf clumps in their gaps
    /// and the odd tall emergent or gold beech; dense conifer slopes beyond, climbing the north, east and west
    /// foothills to the terrain's edge while the river, its floodplain and the open south corridor stay clear.
    /// The plants are the FloraBiome species, drawn instanced by FloraScatter; the bombed valley to the south and a
    /// defoliated strip east along the road get charred dead trees from ProceduralArt and stumps instead, and both
    /// open the view to the mountains. A burnt-out M113 lies off the road in that strip. A fixed seed gives the same
    /// forest every run.
    /// </summary>
    internal static partial class EnvironmentFlora
    {
        private const float ChunkSize = 40f;
        // The camera trails the player by about 4.3 m; nothing taller than FloraBiome.TallHeight grows inside that reach.
        private const float CoverBand = 5.5f, UnderstoryBand = 14f, ForestBand = 60f, FarBand = 112f, StoneInBand = .55f;

        private static readonly Vector3[] Emergents =
        {
            new Vector3(-34, 26, 31), new Vector3(-8, 31, 28), new Vector3(17, 27, 33), new Vector3(40, 17, 27),
            new Vector3(46, -16, 25), new Vector3(-43, 13, 29), new Vector3(-20, 42, 34), new Vector3(31, 41, 30),
            new Vector3(-48, -18, 26)
        };

        // The wreck: off the road's south side in the sprayed strip, nose down the bank, in view from the clearing.
        private static readonly Vector3 Wreck = new Vector3(38.5f, -1.8f, 128f);

        private sealed class Chunk
        {
            public readonly FlatMeshBuilder Tall = new FlatMeshBuilder(), Low = new FlatMeshBuilder();
        }

        private static readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private static readonly List<FloraInstance> placed = new List<FloraInstance>();
        private static FloraBiome biome;
        private static System.Random random;
        private static int pineA, pineB, pineC, pineD, pineYoung, sapling, fernA, fernB, fernC, fernSmall, shrub, sorrel,
            periwinkle, rockA, rockB, rockC, rockFace, outcropA, outcropB, outcropC, log, snag, stump, wreck,
            wildYellow, wildBlue, tallGrassA, tallGrassB,
            firTall, firMedium, firSmall, firPlant, firForestA, firForestB, firDead, spruce,
            broadA, broadB, broadC, mapleA, mapleB, beechGold;

        public static void Build(Transform root)
        {
            biome = Resources.Load<FloraBiome>(FloraBiome.ResourcePath);
            if (biome == null)
            {
                Debug.LogError("[EnvironmentFlora] Missing Resources/" + FloraBiome.ResourcePath + "; run Tien Tuyen/Build Flora.");
                return;
            }
            chunks.Clear();
            placed.Clear();
            random = new System.Random(biome.Seed);
            pineA = biome.IndexOf("Pine_A"); pineB = biome.IndexOf("Pine_B"); pineC = biome.IndexOf("Pine_C"); pineD = biome.IndexOf("Pine_D");
            pineYoung = biome.IndexOf("PineYoung"); sapling = biome.IndexOf("PineSapling");
            fernA = biome.IndexOf("Fern_A"); fernB = biome.IndexOf("Fern_B"); fernC = biome.IndexOf("Fern_C"); fernSmall = biome.IndexOf("FernSmall");
            shrub = biome.IndexOf("Bush"); sorrel = biome.IndexOf("Sorrel"); periwinkle = biome.IndexOf("Periwinkle");
            rockA = biome.IndexOf("Rock_A"); rockB = biome.IndexOf("Rock_B"); rockC = biome.IndexOf("Rock_C"); rockFace = biome.IndexOf("RockFace");
            outcropA = biome.IndexOf("Outcrop_A"); outcropB = biome.IndexOf("Outcrop_B"); outcropC = biome.IndexOf("Outcrop_C");
            log = biome.IndexOf("PineLog"); snag = biome.IndexOf("Snag"); stump = biome.IndexOf("Stump"); wreck = biome.IndexOf("WreckM113");
            wildYellow = biome.IndexOf("Wildflower_Yellow"); wildBlue = biome.IndexOf("Wildflower_Blue");
            tallGrassA = biome.IndexOf("TallGrass_A"); tallGrassB = biome.IndexOf("TallGrass_B");
            firTall = biome.IndexOf("Fir_Tall"); firMedium = biome.IndexOf("Fir_Medium"); firSmall = biome.IndexOf("Fir_Small");
            firPlant = biome.IndexOf("Fir_Plant"); firForestA = biome.IndexOf("Fir_Forest_A"); firForestB = biome.IndexOf("Fir_Forest_B");
            firDead = biome.IndexOf("Fir_Dead"); spruce = biome.IndexOf("Spruce");
            broadA = biome.IndexOf("Broadleaf_A"); broadB = biome.IndexOf("Broadleaf_B"); broadC = biome.IndexOf("Broadleaf_C");
            mapleA = biome.IndexOf("Maple_A"); mapleB = biome.IndexOf("Maple_B"); beechGold = biome.IndexOf("Beech_Gold");

            Scatter(-L.HalfX - CoverBand - 1, L.HalfX + CoverBand + 1, -L.HalfZ - CoverBand - 1, L.HalfZ + CoverBand + 1, 1.5f, 0f, CoverBand, GroundCover);
            Scatter(-L.HalfX - UnderstoryBand, L.HalfX + UnderstoryBand, -L.HalfZ - UnderstoryBand, L.HalfZ + UnderstoryBand, 3.2f, CoverBand, UnderstoryBand, Understory);
            Scatter(-L.HalfX - ForestBand, L.HalfX + ForestBand, -L.HalfZ - ForestBand, L.HalfZ + ForestBand, 6.5f, UnderstoryBand, ForestBand, Forest);
            Scatter(-L.HalfX - ForestBand, L.HalfX + ForestBand, -L.HalfZ - ForestBand, L.HalfZ + ForestBand, 2.4f, CoverBand, ForestBand, Verge);
            Scatter(-L.HalfX - FarBand, L.HalfX + FarBand, -L.HalfZ - FarBand, L.HalfZ + FarBand, 8.5f, ForestBand, FarBand, Far);
            float outer = EnvironmentGround.OuterExtent - 6f;
            Scatter(-outer, outer, -outer, outer, 9f, FarBand, float.MaxValue, Foothill);
            Riverlands();
            foreach (var e in Emergents)
                if (Sightline(e.x, e.y) < .1f)
                    Plant(pineA, e.x, e.y, e.z / Height(pineA), .3f);
            DepotBank();
            Scatter(-L.HalfX - ForestBand, L.HalfX + ForestBand, -L.HalfZ - ForestBand, L.HalfZ + ForestBand, 4f, CoverBand, ForestBand, Meadow);
            RootTufts();
            if (wreck >= 0)
                placed.Add(new FloraInstance { Species = wreck, Position = Ground(Wreck.x, Wreck.y, .15f), Yaw = Wreck.z, Scale = 1f });

            var flora = FloraScatter.Create(root, biome, placed).transform;
            var materials = new Material[(int)ProceduralArt.Slot.Count];
            materials[(int)ProceduralArt.Slot.Rock] = ProceduralArt.Rock("Env_Rock");
            materials[(int)ProceduralArt.Slot.Charred] = ProceduralArt.CharredBark("Env_Charred");
            foreach (var pair in chunks)
            {
                var chunk = new GameObject("Chunk_" + pair.Key.x + "_" + pair.Key.y).transform;
                chunk.SetParent(flora, false);
                pair.Value.Tall.Emit(chunk, "Plants", materials, true);
                pair.Value.Low.Emit(chunk, "Cover", materials, false);
            }
            chunks.Clear();
            placed.Clear();
        }

        /// <summary>
        /// 0..1: the strip east along the road that was sprayed bare, widening away from the station. It opens
        /// a view from the clearing to the karst massif.
        /// </summary>
        public static float Sightline(float x, float z)
        {
            const float dirX = .985f, dirZ = -.174f;   // azimuth 100 degrees
            float along = x * dirX + z * dirZ;
            if (along < 8f) return 0f;
            float across = Mathf.Abs(x * dirZ - z * dirX), half = 6f + along * .2f;
            return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half * .7f, half, across))) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 16f, along));
        }

        private static Chunk ChunkAt(float x, float z)
        {
            var key = new Vector2Int(Mathf.FloorToInt(x / ChunkSize), Mathf.FloorToInt(z / ChunkSize));
            if (!chunks.TryGetValue(key, out var chunk)) chunks.Add(key, chunk = new Chunk());
            return chunk;
        }

        private static float Range(float min, float max) => ProceduralArt.Range(random, min, max);
        private static bool Chance(double p) => random.NextDouble() < p;
        private static float Height(int species) => species < 0 ? 1f : biome.Plants[species].Height;

        /// <summary>Places a species, unless it would stand taller than TallHeight within the camera's reach.</summary>
        private static void Plant(int species, float x, float z, float scale, float sink = .05f)
        {
            if (species < 0) return;
            if (biome.Plants[species].Height * scale > FloraBiome.TallHeight && L.OutsideDistance(x, z) < CoverBand) return;
            bool allow = true;
            Allow(species, x, z, biome.Plants[species].Height * scale, ref allow);
            if (!allow) return;
            placed.Add(new FloraInstance
            {
                Species = species,
                Position = new Vector3(x, L.GroundHeight(x, z) - sink * scale, z),
                Yaw = Range(0f, 360f),
                Scale = scale
            });
        }

        // The riverlands of map Phase 2 (EnvironmentFlora.Riverlands.cs) plant their own ground and veto plants where the
        // map must stay open. Without that file both calls compile away.
        static partial void Riverlands();
        static partial void Allow(int species, float x, float z, float height, ref bool allow);

        private static int Pick(params int[] species) => species[random.Next(species.Length)];

        /// <summary>A conifer for the grove cores: full firs first, the kesiya pines among them.</summary>
        private static int GroveCore() => Pick(firTall, firTall, firMedium, firTall, pineB, pineC, pineA);

        /// <summary>A broadleaf for the clumps between the groves; about one in ten is the gold beech, the warm accent.</summary>
        private static int Broadleaf() => Chance(.1) ? beechGold : Pick(broadA, broadB, broadC, mapleA, mapleB);

        /// <summary>0..1: where broadleaf clumps take over from the conifers, mostly in the gaps between groves.</summary>
        private static float BroadleafField(float x, float z) => Smooth(.58f, .72f, Mathf.PerlinNoise(x * .03f + 41f, z * .03f - 23f));

        /// <summary>
        /// A mossy stone, sized to about <paramref name="size"/> m across and sunk into the ground. Inside the cover
        /// band it stays below knee height, so it never reads as cover that has no collider.
        /// </summary>
        private static void Stone(float x, float z, float size)
        {
            int species = Pick(rockA, rockB, rockC);
            float scale = size / 2f * Range(.8f, 1.2f);
            if (L.OutsideDistance(x, z) < CoverBand) scale = Mathf.Min(scale, StoneInBand / Height(species));
            Plant(species, x, z, scale, .18f);
        }

        private static bool IsRock(int species) =>
            species >= 0 && (species == rockA || species == rockB || species == rockC || species == rockFace
                             || species == outcropA || species == outcropB || species == outcropC);

        /// <summary>
        /// Roots every rock placed so far in the ground: a ring of grass and fern tufts around its foot and, on rocks
        /// taller than a knee, a few tufts on the rim, so rock and meadow merge instead of meeting at a hard line
        /// (Rocky Hills). Tufts go through Plant, so the cover band and the map's vetoes still apply.
        /// </summary>
        private static void RootTufts()
        {
            for (int i = 0, n = placed.Count; i < n; i++)
            {
                var rock = placed[i];
                if (!IsRock(rock.Species)) continue;
                var plant = biome.Plants[rock.Species];
                float radius = plant.Radius * rock.Scale, top = plant.Height * rock.Scale;
                if (radius < .15f) continue;
                for (int k = 0, ring = Mathf.Clamp((int)(radius * 4f), 3, 8); k < ring; k++)
                {
                    float angle = (k + Range(-.3f, .3f)) / ring * 2f * Mathf.PI, r = radius * Range(.8f, 1.1f);
                    Tuft(rock.Position.x + Mathf.Cos(angle) * r, rock.Position.z + Mathf.Sin(angle) * r, Range(.6f, 1f));
                }
                if (top < 1.2f) continue;
                for (int k = 0, rim = 1 + random.Next(3); k < rim; k++)
                {
                    float angle = Range(0f, 2f * Mathf.PI), r = radius * Range(.25f, .5f);
                    int before = placed.Count;
                    Tuft(rock.Position.x + Mathf.Cos(angle) * r, rock.Position.z + Mathf.Sin(angle) * r, Range(.45f, .7f));
                    if (placed.Count == before) continue;
                    var tuft = placed[before];
                    tuft.Position.y = rock.Position.y + top * .8f;
                    placed[before] = tuft;
                }
            }
        }

        private static void Tuft(float x, float z, float scale)
        {
            float roll = (float)random.NextDouble();
            if (roll < .55f) Plant(Pick(tallGrassA, tallGrassB), x, z, scale, .03f);
            else if (roll < .9f) Plant(Pick(fernSmall, fernA), x, z, scale, .05f);
            else Plant(wildYellow, x, z, scale, .02f);
        }

        /// <summary>
        /// A mossy boulder outcrop, 2.5 to 7 m across, half sunk; the stand's landmarks between the pines. Rocks come
        /// in families: a skirt of fallen stones and ferns tucked against its foot ties it into the ground. Away from
        /// the station about half of them step up the slope in two or three tiers, each smaller block set back on the
        /// one below and sunk into it.
        /// </summary>
        private static void Outcrop(float x, float z)
        {
            int species = Pick(rockFace, outcropA, outcropB, outcropC);
            float scale = Range(.5f, .85f);
            Plant(species, x, z, scale, .35f);
            // The tiers draw from their own stream, seeded by where the outcrop stands, so the rest of the forest
            // grows exactly as it did without them.
            var stand = random;
            random = new System.Random(Mathf.RoundToInt(x * 7.31f) * 73856093 ^ Mathf.RoundToInt(z * 7.31f) * 19349663);
            if (L.OutsideDistance(x, z) > UnderstoryBand && Chance(.5))
            {
                // Step back up the slope, or any way on the flat.
                float e = 1f, gx = L.GroundHeight(x + e, z) - L.GroundHeight(x - e, z), gz = L.GroundHeight(x, z + e) - L.GroundHeight(x, z - e);
                float uphill = gx * gx + gz * gz > .01f ? Mathf.Atan2(gz, gx) : Range(0f, 360f) * Mathf.Deg2Rad;
                float bx = x, bz = z, top = L.GroundHeight(x, z) + Height(species) * scale * .65f;
                for (int tier = 0, tiers = Chance(.4) ? 2 : 1; tier < tiers; tier++)
                {
                    float angle = uphill + Range(-.6f, .6f), step = Height(species) * scale * Range(.5f, .8f);
                    bx += Mathf.Cos(angle) * step; bz += Mathf.Sin(angle) * step;
                    species = Pick(rockFace, outcropA, outcropB, outcropC);
                    scale *= Range(.55f, .75f);
                    // Plant sinks by sink * scale below the ground: a negative sink lifts the block onto the one below.
                    float ground = L.GroundHeight(bx, bz), seat = Mathf.Max(ground, top - Height(species) * scale * .3f);
                    Plant(species, bx, bz, scale, (ground - seat) / scale + .2f);
                    top = seat + Height(species) * scale * .65f;
                }
            }
            random = stand;
            for (int i = 0, n = 3 + random.Next(4); i < n; i++)
            {
                float angle = Range(0f, 360f) * Mathf.Deg2Rad, r = Range(2f, 4.5f);
                float px = x + Mathf.Cos(angle) * r, pz = z + Mathf.Sin(angle) * r;
                if (Chance(.6)) Stone(px, pz, Range(.3f, 1f));
                else Plant(Pick(fernA, fernB, fernSmall), px, pz, Range(.7f, 1.1f));
            }
        }

        /// <summary>Herbs of the open ground: ferns, low shrubs, sorrel and periwinkle.</summary>
        private static void Herb(float x, float z, float grow)
        {
            float roll = (float)random.NextDouble();
            if (roll < .3f) Plant(Pick(fernA, fernB, fernC, fernSmall), x, z, Range(.75f, 1.05f) * grow);
            else if (roll < .55f) Plant(shrub, x, z, Range(.6f, 1f) * grow, .05f);
            else if (roll < .8f) Plant(sorrel, x, z, Range(.8f, 1.4f), .02f);
            else Plant(periwinkle, x, z, Range(.8f, 1.3f), .02f);
        }

        /// <summary>
        /// A knot of undergrowth around (x, z): shrubs, ferns and tall grass with the odd seedling, the layer that
        /// fills the gaps between trunks. Only used outside the cover band.
        /// </summary>
        private static void Thicket(float x, float z, float radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float px = x + Range(-radius, radius), pz = z + Range(-radius, radius);
                if (L.OutsideDistance(px, pz) < CoverBand || L.RoadDistance(px, pz, out _) < 4f || L.InSite(px, pz, 1.5f)) continue;
                float roll = (float)random.NextDouble();
                if (roll < .35f) Plant(shrub, px, pz, Range(.8f, 1.3f), .05f);
                else if (roll < .6f) Plant(Pick(fernA, fernB, fernC), px, pz, Range(.9f, 1.3f));
                else if (roll < .85f) Plant(Chance(.6) ? tallGrassA : tallGrassB, px, pz, Range(.9f, 1.3f), .03f);
                else Plant(Chance(.5) ? sapling : firPlant, px, pz, Chance(.5) ? Range(.8f, 1.3f) : Range(.25f, .4f), .03f);
            }
        }

        /// <summary>Jittered grid over a rectangle, keeping points whose playfield distance falls in [minD, maxD).</summary>
        private static void Scatter(float minX, float maxX, float minZ, float maxZ, float cell, float minD, float maxD,
            System.Action<float, float, float> plant)
        {
            for (float z = minZ; z < maxZ; z += cell)
            for (float x = minX; x < maxX; x += cell)
            {
                float px = x + Range(0, cell), pz = z + Range(0, cell);
                float d = L.OutsideDistance(px, pz);
                // Inside the playfield measure inwards (negative) so ground cover can creep over the edge.
                if (d <= 0) d = -Mathf.Min(L.HalfX - Mathf.Abs(px), L.HalfZ - Mathf.Abs(pz));
                if (d < minD || d >= maxD) continue;
                if (L.InSite(px, pz, d < CoverBand ? .5f : 1.5f)) continue;
                plant(px, pz, d);
            }
        }

        private static Vector3 Ground(float x, float z, float sink = .05f) => new Vector3(x, L.GroundHeight(x, z) - sink, z);

        private static bool Valley(float x, float z, float d) => d > 2f && z < -L.HalfZ && Chance(L.Bombed(x, z) * 1.15f - .1f);

        /// <summary>Clear of the wreck, so the scatter never grows through it.</summary>
        private static bool NearWreck(float x, float z, float reach) => (new Vector2(x - Wreck.x, z - Wreck.y)).sqrMagnitude < reach * reach;

        /// <summary>
        /// The edge band, from the yard's edge to where the young pines start: herbs, stones and dead wood in the
        /// meadow, pine seedlings by its far side. Nothing here outgrows FloraBiome.TallHeight.
        /// </summary>
        private static void GroundCover(float x, float z, float d)
        {
            if (L.RoadDistance(x, z, out _) < 3.2f || L.LineDistance(L.DepotTrack, x, z, out _) < 2.4f) return;
            if (L.CraterDistance(x, z, out _) < .4f) return;
            float noise = Mathf.PerlinNoise(x * .15f + 3f, z * .15f);
            if (Valley(x, z, d))
            {
                if (Chance(.06)) Stone(x, z, Range(.5f, 1f));
                else if (Chance(.04)) Plant(stump, x, z, Range(.7f, 1f), .05f);
                return;
            }
            float depth = Mathf.Clamp01(d / CoverBand);
            float roll = (float)random.NextDouble();
            // Most of the band stays open meadow; herbs gather in patches.
            if (roll < .1f + noise * .3f) Herb(x, z, Mathf.Lerp(.8f, 1.05f, depth));
            else if (roll < .5f && depth > .4f && Chance(.25)) Plant(sapling, x, z, Range(.7f, 1.15f), .03f);
            else if (roll < .6f) Stone(x, z, Range(.45f, 1.1f));
            else if (roll < .63f && d > 2f) Plant(log, x, z, Range(.7f, 1f), .12f);
            else if (roll < .66f && d > 1f) Plant(stump, x, z, Range(.7f, 1.1f), .05f);
        }

        /// <summary>Road verges through the woodland: herbs and seedlings where the stand opens for the road.</summary>
        private static void Verge(float x, float z, float d)
        {
            float road = L.RoadDistance(x, z, out _);
            if (road < 3.2f || road > 7.5f || L.CraterDistance(x, z, out _) < 1f || L.InSite(x, z, .5f) || NearWreck(x, z, 4f)) return;
            if (Chance(L.Bombed(x, z)))
            {
                if (Chance(.08)) Plant(stump, x, z, Range(.7f, 1f), .05f);
                return;
            }
            float roll = (float)random.NextDouble();
            float back = Mathf.InverseLerp(3.2f, 7.5f, road);
            if (roll < .3f) Herb(x, z, Range(.9f, 1.15f));
            else if (roll < .3f + .15f * back) Plant(Chance(.6) ? sapling : pineYoung, x, z, Range(.6f, 1f), .05f);
            else if (roll < .5f && Chance(.3)) Stone(x, z, Range(.5f, 1.2f));
        }

        /// <summary>
        /// Sparse accents on the open sward: wildflower patches and taller grass where the meadow is lush, never on the
        /// road, track, craters or under the woods. The GPU grass stays the carpet; these only break it up.
        /// </summary>
        private static void Meadow(float x, float z, float d)
        {
            if (L.RoadDistance(x, z, out _) < 4f || L.LineDistance(L.DepotTrack, x, z, out _) < 3f) return;
            if (L.CraterDistance(x, z, out _) < 2f || NearWreck(x, z, 5f) || Sightline(x, z) > .3f) return;
            float lush = 1f - MeadowDryness(x, z, out float woods);
            if (woods > .3f || lush < .55f || !Chance((lush - .55f) * .9f)) return;
            if (Chance(.55)) Plant(Chance(.5) ? wildYellow : wildBlue, x, z, Range(.8f, 1.2f), .02f);
            else Plant(Chance(.6) ? tallGrassA : tallGrassB, x, z, Range(.8f, 1.15f), .03f);
        }

        /// <summary>0 lush .. 1 dry sward, and the woods cover, as the ground shader paints them (EnvTerrain.hlsl
        /// EnvMeadowFields and EnvMeadowDryness), so the accents follow the green the player sees.</summary>
        private static float MeadowDryness(float x, float z, out float woods)
        {
            float dry = Smooth(.45f, .68f, Fbm(x / 46f + 13.7f, z / 46f + 13.7f));
            woods = Smooth(.62f, .74f, Fbm(x / 34f + 3.3f, z / 34f + 3.3f) * .55f + Fbm(x / 210f - 7.1f, z / 210f - 7.1f) * .6f);
            float h = L.GroundHeight(x, z);
            var normal = new Vector3(h - L.GroundHeight(x + 1f, z), 1f, h - L.GroundHeight(x, z + 1f)).normalized;
            float south = Mathf.Clamp01(-normal.z * 2.5f), crest = Mathf.Clamp01((h - 12f) / 30f), hollow = Mathf.Clamp01((3f - h) / 6f);
            return Mathf.Clamp01(dry * .5f + south * .25f + crest * .2f - hollow * .45f);
        }

        private static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // EnvCommon.hlsl EnvHash21 / EnvValueNoise / EnvFbm.
        private static float Hash(float x, float y)
        {
            x = Frac(x * 123.34f); y = Frac(y * 456.21f);
            float k = x * (x + 45.32f) + y * (y + 45.32f);
            x += k; y += k;
            return Frac(x * y);
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        private static float Fbm(float x, float y)
        {
            float sum = 0f, amplitude = .5f;
            for (int i = 0; i < 4; i++)
            {
                float ix = Mathf.Floor(x), iy = Mathf.Floor(y), fx = x - ix, fy = y - iy;
                fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
                float a = Hash(ix, iy), b = Hash(ix + 1f, iy), c = Hash(ix, iy + 1f), e = Hash(ix + 1f, iy + 1f);
                sum += Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, e, fx), fy) * amplitude;
                x = x * 2.03f + 17.1f; y = y * 2.03f + 17.1f;
                amplitude *= .5f;
            }
            return sum / .9375f;
        }

        /// <summary>
        /// The rise behind the depot truck lies inside the depot site, which keeps the stand off it. Grow it as a
        /// bank of ferns, shrubs and seedlings with stones and a half-buried trunk, young pines on the crest, while
        /// the truck, its net, the drums and the wheel track keep their clearance and the flat depot floor stays open.
        /// </summary>
        private static void DepotBank()
        {
            var site = L.Sites[4];
            float reach = site.z + 1.5f;
            // EnvironmentStation.Depot: truck frame at (-27.5, -12), yaw 72; the net spans 3.75 x 5.25 m either side.
            Vector2 center = new Vector2(-27.5f, -12f), forward = new Vector2(.951f, .309f), right = new Vector2(.309f, -.951f);
            for (float z = site.y - reach; z < site.y + reach; z += 1.3f)
            for (float x = site.x - reach; x < site.x + reach; x += 1.3f)
            {
                float px = x + Range(0f, 1.3f), pz = z + Range(0f, 1.3f);
                var p = new Vector2(px, pz);
                if ((p - new Vector2(site.x, site.y)).sqrMagnitude > reach * reach) continue;
                Vector2 local = new Vector2(Vector2.Dot(p - center, right), Vector2.Dot(p - center, forward));
                if (Mathf.Abs(local.x) < 4.6f && Mathf.Abs(local.y) < 6.1f) continue;
                if ((p - new Vector2(-20.4f, -13.2f)).sqrMagnitude < 3.4f * 3.4f) continue;
                if (L.RoadDistance(px, pz, out _) < 3.2f || L.LineDistance(L.DepotTrack, px, pz, out _) < 2.6f) continue;
                float rise = Mathf.InverseLerp(.25f, 1.6f, L.GroundHeight(px, pz));
                if (rise <= 0f) continue;
                float roll = (float)random.NextDouble();
                if (roll < .35f) Herb(px, pz, Range(.95f, 1.25f));
                else if (roll < .35f + .2f * rise)
                {
                    // Seedlings to young pines, tallest along the crest, so the bank's skyline is broken.
                    int young = rise > .6f && Chance(.5) ? pineYoung : sapling;
                    Plant(young, px, pz, Mathf.Lerp(1.3f, 3.6f, rise) * Range(.8f, 1.1f) / Height(young), .06f);
                }
                else if (roll < .7f && Chance(.25)) Stone(px, pz, Range(.4f, .9f));
                else if (roll < .74f)
                    // A trunk washed out of the bank, mostly buried in the slope.
                    Plant(log, px, pz, Range(.5f, .7f), .3f);
            }
        }

        /// <summary>Young pines and seedlings in the meadow behind the edge band, the first tall stems further back.</summary>
        private static void Understory(float x, float z, float d)
        {
            if (L.RoadDistance(x, z, out _) < 4.2f || L.CraterDistance(x, z, out _) < 1f || NearWreck(x, z, 5f)) return;
            var c = ChunkAt(x, z);
            if (Valley(x, z, d))
            {
                if (Chance(.1)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .2f), Range(3f, 7f));
                else if (Chance(.1)) Plant(stump, x, z, Range(.8f, 1.2f), .05f);
                else if (Chance(.08)) Stone(x, z, Range(.6f, 1.3f));
                return;
            }
            if (Chance(Sightline(x, z)))
            {
                // The sprayed strip regrows as meadow with seedlings and the stumps of the old stand.
                if (Chance(.15)) Plant(stump, x, z, Range(.8f, 1.2f), .05f);
                else if (Chance(.15)) Plant(sapling, x, z, Range(.7f, 1.1f), .03f);
                return;
            }
            float clump = Mathf.PerlinNoise(x * .07f - 11f, z * .07f + 4f);
            float roll = (float)random.NextDouble();
            if (d > 9f && clump > .55f && roll < .22f) Plant(Pick(firMedium, pineB, pineC, firTall), x, z, Range(.65f, .9f), .2f);
            else if (roll < .3f) Plant(Pick(pineYoung, firSmall), x, z, Range(.6f, 1.25f) * Mathf.Lerp(.7f, 1f, Mathf.InverseLerp(CoverBand, 11f, d)), .06f);
            else if (roll < .45f) Plant(sapling, x, z, Range(.8f, 1.2f), .03f);
            else if (roll < .53f) Stone(x, z, Range(.6f, 1.5f));
            else if (roll < .57f) Plant(Chance(.5) ? log : stump, x, z, Range(.8f, 1.1f), .1f);
            if (Chance(.35)) Herb(x + Range(-1.2f, 1.2f), z + Range(-1.2f, 1.2f), Range(.95f, 1.25f));
            // The wood's edge facing the meadow: a soft wall of shrubs and young pines that thickens towards the stand.
            float edge = Mathf.InverseLerp(7f, UnderstoryBand, d);
            if (Chance(.25f + .5f * edge)) Thicket(x, z, 1.6f, 2 + (int)(edge * 3f));
            if (d > 9f && Chance(.3f * edge)) Plant(Pick(pineYoung, firSmall), x + Range(-1.5f, 1.5f), z + Range(-1.5f, 1.5f), Range(.9f, 1.4f), .06f);
            else if (d > 10f && Chance(.12f * edge)) Plant(Broadleaf(), x + Range(-1.5f, 1.5f), z + Range(-1.5f, 1.5f), Range(.5f, .75f), .2f);
        }

        /// <summary>
        /// The open stand: tall pines spaced wide in loose groups with meadow between them, the odd young pine in a
        /// gap, ferns and stones at their feet, a fallen trunk, outcrop or bare snag here and there.
        /// </summary>
        private static void Forest(float x, float z, float d)
        {
            if (L.RoadDistance(x, z, out _) < 5f || L.CraterDistance(x, z, out _) < 1.5f || NearWreck(x, z, 7f)) return;
            var c = ChunkAt(x, z);
            if (Valley(x, z, d))
            {
                if (Chance(.3)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .2f), Range(4f, 10f));
                else if (Chance(.2)) Plant(stump, x, z, Range(.9f, 1.3f), .05f);
                if (Chance(.1)) Stone(x + 1.5f, z, Range(.8f, 1.6f));
                return;
            }
            if (Chance(Sightline(x, z)))
            {
                if (Chance(.22)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .2f), Range(3f, 7f));
                if (Chance(.3)) Plant(stump, x + Range(-2f, 2f), z + Range(-2f, 2f), Range(.9f, 1.3f), .05f);
                return;
            }
            float clump = Mathf.PerlinNoise(x * .045f + 21f, z * .045f - 9f);
            float roll = (float)random.NextDouble();
            float grow = Mathf.Lerp(.8f, 1.1f, Mathf.InverseLerp(UnderstoryBand, 40f, d));
            float grove = Smooth(.35f, .7f, clump), broad = BroadleafField(x, z) * (1f - grove * .7f);
            if (roll < broad * .8f) Plant(Broadleaf(), x, z, Range(.75f, 1.05f) * grow, .25f);
            else if (roll < .12f + .78f * grove) Plant(GroveCore(), x, z, Range(.8f, 1.1f) * grow, .25f);
            else if (roll < .85f) Plant(Pick(pineYoung, firSmall, firSmall), x, z, Range(.8f, 1.5f), .06f);
            // A second, younger tier inside the groves, the odd tall fir standing over them, and undergrowth
            // thickest in the gaps between them.
            if (Chance(.45f * grove)) Plant(Pick(firSmall, firMedium, pineYoung, sapling), x + Range(-3f, 3f), z + Range(-3f, 3f), Range(.7f, 1.2f), .06f);
            if (d > 22f && Chance(.05f * grove)) Plant(Pick(firForestA, firForestB), x + Range(-2f, 2f), z + Range(-2f, 2f), Range(1.15f, 1.35f), .3f);
            if (d < 45f) Thicket(x + Range(-2f, 2f), z + Range(-2f, 2f), 2.5f, 1 + (int)((1f - grove) * 3f + Range(0f, 1.5f)));
            if (Chance(.12)) Stone(x + Range(-3f, 3f), z + Range(-3f, 3f), Range(.8f, 2f));
            else if (Chance(.06)) Outcrop(x + Range(-3f, 3f), z + Range(-3f, 3f));
            else if (Chance(.08)) Plant(Chance(.6) ? log : stump, x + Range(-3f, 3f), z + Range(-3f, 3f), Range(.9f, 1.2f), .1f);
            // Bare firs stand a little over the grove so they break its canopy line (Rocky Hills).
            else if (Chance(.03)) { if (Chance(.4)) Plant(snag, x, z, Range(.8f, 1.1f), .2f); else Plant(firDead, x, z, Range(1.05f, 1.25f), .3f); }
        }

        /// <summary>
        /// The foothills beyond the far band, out to the terrain's edge: dense conifer slopes with broadleaf patches,
        /// all at card range. The river, its paddy floodplain and banks, and the low south corridor that opens the view
        /// onto the river stay clear.
        /// </summary>
        private static void Foothill(float x, float z, float d)
        {
            float side = L.RiverSide(x, z), plain = side > 0 ? L.RiverPlain : L.FarPlain;
            if (Mathf.Abs(side) < L.RiverHalfWidth + plain + 12f || L.RoadDistance(x, z, out _) < 8f || Sightline(x, z) > .2f) return;
            // South, within 40 degrees either side, the land stays low and open onto the river and its terraces.
            if (z < 0f && Mathf.Abs(Mathf.Atan2(x, -z)) < 40f * Mathf.Deg2Rad) return;
            float clump = Mathf.PerlinNoise(x * .02f + 7f, z * .02f - 31f);
            if (Chance(BroadleafField(x, z) * .6f)) Plant(Broadleaf(), x, z, Range(.9f, 1.2f), .3f);
            else if (Chance(.45 + .5 * clump)) Plant(Pick(firTall, firMedium, firForestA, firForestB, spruce, spruce, pineD), x, z, Range(.9f, 1.25f), .3f);
        }

        /// <summary>Far hills: tall crowns spaced like the stand, reading as an open pine slope through the haze.</summary>
        private static void Far(float x, float z, float d)
        {
            if (L.RoadDistance(x, z, out _) < 6f) return;
            var c = ChunkAt(x, z);
            if (Chance(Sightline(x, z)))
            {
                if (Chance(.2)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .3f), Range(4f, 8f));
                return;
            }
            if (z > -60f && Chance(L.Bombed(x, z)))
            {
                if (Chance(.25)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .3f), Range(5f, 11f));
                return;
            }
            float clump = Mathf.PerlinNoise(x * .03f - 5f, z * .03f + 17f);
            if (Chance(BroadleafField(x, z) * .7f)) Plant(Broadleaf(), x, z, Range(.85f, 1.15f), .3f);
            else if (Chance(.35 + .6 * clump)) Plant(Pick(firTall, firMedium, firForestA, firForestB, spruce, pineD, pineB), x, z, Range(.85f, 1.15f), .3f);
            else if (Chance(.45)) Plant(Pick(pineYoung, firSmall, spruce), x, z, Range(1f, 1.4f), .1f);
            else if (Chance(.05)) Outcrop(x, z);
        }
    }
}
