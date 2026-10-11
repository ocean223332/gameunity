using System.Collections.Generic;
using UnityEngine;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Plants the jungle in bands by distance from the playfield: knee-high cover where the shoulder camera
    /// can reach, understory and young trees behind it, the canopy over the hills and big crowns far out.
    /// The plants are the FloraBiome species, drawn instanced by FloraScatter; the bombed valley to the south
    /// and a defoliated strip east along the road get charred dead trees, grass and stones from ProceduralArt
    /// instead, and both open the view to the mountains. A fixed seed gives the same forest every run.
    /// </summary>
    internal static class EnvironmentFlora
    {
        private const float ChunkSize = 40f;
        // The camera trails the player by about 4.3 m; nothing taller than FloraBiome.TallHeight grows inside that reach.
        private const float CoverBand = 5.5f, UnderstoryBand = 14f, ForestBand = 60f, FarBand = 112f;

        private static readonly Vector3[] Emergents =
        {
            new Vector3(-34, 26, 31), new Vector3(-8, 31, 28), new Vector3(17, 27, 33), new Vector3(40, 17, 27),
            new Vector3(46, -16, 25), new Vector3(-43, 13, 29), new Vector3(-20, 42, 34), new Vector3(31, 41, 30),
            new Vector3(-48, -18, 26)
        };

        private sealed class Chunk
        {
            public readonly FlatMeshBuilder Tall = new FlatMeshBuilder(), Low = new FlatMeshBuilder();
        }

        private static readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private static readonly List<FloraInstance> placed = new List<FloraInstance>();
        private static FloraBiome biome;
        private static System.Random random;
        private static int fernA, fernB, fernSmall, calathea, monsteraA, monsteraB, log, bananaA, bananaB,
            bambooYoung, bamboo, palmA, palmB, treeA, treeB, grass, grassTall;

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
            fernA = biome.IndexOf("Fern_A"); fernB = biome.IndexOf("Fern_B"); fernSmall = biome.IndexOf("FernSmall");
            calathea = biome.IndexOf("Calathea"); monsteraA = biome.IndexOf("Monstera_A"); monsteraB = biome.IndexOf("Monstera_B");
            log = biome.IndexOf("Log"); bananaA = biome.IndexOf("Banana_A"); bananaB = biome.IndexOf("Banana_B");
            bambooYoung = biome.IndexOf("BambooYoung"); bamboo = biome.IndexOf("Bamboo");
            palmA = biome.IndexOf("Palm_A"); palmB = biome.IndexOf("Palm_B");
            treeA = biome.IndexOf("CanopyTree_A"); treeB = biome.IndexOf("CanopyTree_B");
            grass = biome.IndexOf("GrassClump"); grassTall = biome.IndexOf("GrassTall");

            GrassClumps();
            Scatter(-L.HalfX - CoverBand - 1, L.HalfX + CoverBand + 1, -L.HalfZ - CoverBand - 1, L.HalfZ + CoverBand + 1, 1.2f, 0f, CoverBand, GroundCover);
            Scatter(-L.HalfX - UnderstoryBand, L.HalfX + UnderstoryBand, -L.HalfZ - UnderstoryBand, L.HalfZ + UnderstoryBand, .9f, 0f, UnderstoryBand, DetailGrass);
            Scatter(-L.HalfX - UnderstoryBand, L.HalfX + UnderstoryBand, -L.HalfZ - UnderstoryBand, L.HalfZ + UnderstoryBand, 2.6f, CoverBand, UnderstoryBand, Understory);
            Scatter(-L.HalfX - ForestBand, L.HalfX + ForestBand, -L.HalfZ - ForestBand, L.HalfZ + ForestBand, 5.2f, UnderstoryBand, ForestBand, Forest);
            Scatter(-L.HalfX - ForestBand, L.HalfX + ForestBand, -L.HalfZ - ForestBand, L.HalfZ + ForestBand, 1.6f, CoverBand, ForestBand, Verge);
            Scatter(-L.HalfX - FarBand, L.HalfX + FarBand, -L.HalfZ - FarBand, L.HalfZ + FarBand, 8.5f, ForestBand, FarBand, Far);
            foreach (var e in Emergents)
                if (Sightline(e.x, e.y) < .1f)
                    Plant(treeA, e.x, e.y, e.z / biome.Plants[treeA].Height, .3f);
            // Last, so it draws from the seed after everything else and leaves the rest of the forest as it was.
            DepotBank();

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
        /// a view from the clearing to the karst massif; EnvironmentBackdrop thins the canopy shell over it too.
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

        /// <summary>Places a species, unless it would stand taller than TallHeight within the camera's reach.</summary>
        private static void Plant(int species, float x, float z, float scale, float sink = .05f)
        {
            if (species < 0) return;
            if (biome.Plants[species].Height * scale > FloraBiome.TallHeight && L.OutsideDistance(x, z) < CoverBand) return;
            placed.Add(new FloraInstance
            {
                Species = species,
                Position = new Vector3(x, L.GroundHeight(x, z) - sink * scale, z),
                Yaw = Range(0f, 360f),
                Scale = scale
            });
        }

        private static int Pick(params int[] species) => species[random.Next(species.Length)];

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

        /// <summary>
        /// The yard's grass: each EnvironmentGround.GrassClump becomes a tussock of blades, tallest in the middle,
        /// sometimes with a small fern at its foot, over the soil the ground paint greened for it.
        /// </summary>
        private static void GrassClumps()
        {
            int reachX = Mathf.CeilToInt((L.HalfX + 3f) / EnvironmentGround.ClumpCell), reachZ = Mathf.CeilToInt((L.HalfZ + 3f) / EnvironmentGround.ClumpCell);
            for (int k = -reachZ; k <= reachZ; k++)
            for (int i = -reachX; i <= reachX; i++)
            {
                if (!EnvironmentGround.GrassClump(i, k, out var center, out float radius)) continue;
                float inward = Mathf.Min(L.HalfX - Mathf.Abs(center.x), L.HalfZ - Mathf.Abs(center.y));
                int blades = 4 + Mathf.RoundToInt(radius * 6f);
                for (int b = 0; b < blades; b++)
                {
                    float a = Range(0f, Mathf.PI * 2f), r = Mathf.Sqrt((float)random.NextDouble()) * radius * .85f;
                    float x = center.x + Mathf.Cos(a) * r, z = center.y + Mathf.Sin(a) * r;
                    // Rank grass in the middle of the big clumps near the forest; short tufts out in the yard.
                    if (b == 0 && radius > .7f && inward < 5f) Plant(grassTall, x, z, Range(.6f, .8f), .02f);
                    else Plant(grass, x, z, Mathf.Lerp(1.2f, .6f, r / radius) * Range(.85f, 1.1f), .02f);
                }
                if (inward < 4f && Chance(.3)) Plant(fernSmall, center.x + Range(-radius, radius), center.y + Range(-radius, radius), Range(.6f, .85f));
            }
        }

        /// <summary>
        /// The edge band, from the yard's edge to where the understory starts: knee-high herbs, grass and stones at
        /// the edge, thickening into head-high young growth (sapling bananas and bamboo) by its far side, so the
        /// clearing runs into the forest instead of stopping at it. Nothing here outgrows FloraBiome.TallHeight.
        /// </summary>
        private static void GroundCover(float x, float z, float d)
        {
            float road = L.RoadDistance(x, z, out _);
            var c = ChunkAt(x, z);
            if (road < 3.2f || L.LineDistance(L.DepotTrack, x, z, out _) < 2.4f) return;
            if (L.CraterDistance(x, z, out _) < .4f) return;
            float noise = Mathf.PerlinNoise(x * .15f + 3f, z * .15f);
            if (Valley(x, z, d))
            {
                if (Chance(.6)) Plant(Chance(.4) ? grassTall : grass, x, z, Range(.8f, 1.2f), .02f);
                if (Chance(.05)) ProceduralArt.BoulderInto(c.Low, random, Ground(x, z, .1f), new Vector3(Range(.5f, 1f), Range(.25f, .45f), Range(.4f, .8f)));
                return;
            }
            float depth = d / CoverBand;
            float grow = Mathf.Lerp(.75f, 1.1f, depth);
            float roll = (float)random.NextDouble();
            if (roll < .24f + noise * .2f)
                Plant(Pick(fernA, fernB, fernSmall, fernSmall), x, z, Range(.8f, 1.15f) * grow);
            else if (roll < .44f + noise * .2f)
                Plant(Pick(calathea, monsteraA, calathea), x, z, Range(.8f, 1.2f) * grow);
            else if (roll < .66f)
            {
                // Grass in tussocks, rank and taller deeper in.
                int blades = random.Next(2, 5);
                for (int b = 0; b < blades; b++)
                    Plant(Chance(depth * .8f) ? grassTall : grass, x + Range(-.5f, .5f), z + Range(-.5f, .5f), Range(.85f, 1.25f) * (b == 0 ? 1f : .8f), .02f);
            }
            else if (roll < .66f + .2f * depth)
            {
                // Young growth: saplings of the understory, head-high at most.
                int sapling = Pick(bananaA, bananaB, bambooYoung, monsteraB);
                float tall = Mathf.Lerp(1.1f, 1.75f, depth) * Range(.85f, 1f);
                Plant(sapling, x, z, tall / biome.Plants[sapling].Height, .05f);
            }
            else if (roll < .9f)
                ProceduralArt.BoulderInto(c.Low, random, Ground(x, z, .1f), new Vector3(Range(.5f, 1.1f), Range(.3f, .55f), Range(.5f, 1f)));
            else if (roll < .93f && d > 2f)
                Plant(log, x, z, Range(.8f, 1.1f), .12f);
        }

        /// <summary>Dense grass clumps wherever the ground paints grass along the forest fringe.</summary>
        private static void DetailGrass(float x, float z, float d)
        {
            if (L.RoadDistance(x, z, out _) < 2.6f || L.CraterDistance(x, z, out _) < .5f) return;
            if (L.LineDistance(L.DepotTrack, x, z, out _) < 2.2f) return;
            float cover = EnvironmentGround.GrassCover(x, z);
            if (Chance(cover * .8f))
                Plant(Chance(d > 3f ? .25 : 0) ? grassTall : grass, x, z, Range(.75f, 1.25f), .02f);
        }

        /// <summary>
        /// Road verges and cut banks through the forest: rank grass, ferns and saplings where the trees stand back
        /// from the road, so the cutting's banks are overgrown rather than bare earth mounds.
        /// </summary>
        private static void Verge(float x, float z, float d)
        {
            float road = L.RoadDistance(x, z, out _);
            if (road < 3.2f || road > 7.5f || L.CraterDistance(x, z, out _) < 1f || L.InSite(x, z, .5f)) return;
            if (Chance(L.Bombed(x, z)))
            {
                if (Chance(.35)) Plant(grassTall, x, z, Range(.8f, 1.2f), .02f);
                return;
            }
            float roll = (float)random.NextDouble();
            float back = Mathf.InverseLerp(3.2f, 7.5f, road);
            if (roll < .4f) Plant(Chance(.6) ? grassTall : grass, x, z, Range(.85f, 1.3f), .02f);
            else if (roll < .65f) Plant(Pick(fernA, fernB, monsteraA, calathea), x, z, Range(.9f, 1.3f));
            else if (roll < .65f + .25f * back) Plant(Pick(bananaA, bananaB, bambooYoung), x, z, Range(.45f, .8f), .1f);
        }

        /// <summary>
        /// The rise behind the depot truck lies inside the depot site, which keeps the forest off it. Overgrow it as a
        /// jungle bank (ferns, rank grass, saplings on the crest, stones and half-buried roots) while the truck, its
        /// net, the drums and the wheel track keep their clearance and the flat depot floor stays open.
        /// </summary>
        private static void DepotBank()
        {
            var site = L.Sites[4];
            float reach = site.z + 1.5f;
            // EnvironmentStation.Depot: truck frame at (-27.5, -12), yaw 72; the net spans 3.75 x 5.25 m either side.
            Vector2 center = new Vector2(-27.5f, -12f), forward = new Vector2(.951f, .309f), right = new Vector2(.309f, -.951f);
            for (float z = site.y - reach; z < site.y + reach; z += 1f)
            for (float x = site.x - reach; x < site.x + reach; x += 1f)
            {
                float px = x + Range(0f, 1f), pz = z + Range(0f, 1f);
                var p = new Vector2(px, pz);
                if ((p - new Vector2(site.x, site.y)).sqrMagnitude > reach * reach) continue;
                Vector2 local = new Vector2(Vector2.Dot(p - center, right), Vector2.Dot(p - center, forward));
                if (Mathf.Abs(local.x) < 4.6f && Mathf.Abs(local.y) < 6.1f) continue;
                if ((p - new Vector2(-20.4f, -13.2f)).sqrMagnitude < 3.4f * 3.4f) continue;
                if (L.RoadDistance(px, pz, out _) < 3.2f || L.LineDistance(L.DepotTrack, px, pz, out _) < 2.6f) continue;
                float rise = Mathf.InverseLerp(.25f, 1.6f, L.GroundHeight(px, pz));
                if (rise <= 0f) continue;
                float roll = (float)random.NextDouble();
                if (roll < .3f)
                    Plant(Pick(fernA, fernB, fernSmall, calathea, monsteraA), px, pz, Range(.9f, 1.3f));
                else if (roll < .45f)
                {
                    for (int b = random.Next(2, 4); b > 0; b--)
                        Plant(grassTall, px + Range(-.4f, .4f), pz + Range(-.4f, .4f), Range(.9f, 1.3f), .02f);
                }
                else if (roll < .45f + .25f * rise)
                {
                    // Head-high to small saplings, tallest along the crest, so the bank's skyline is broken.
                    int sapling = Pick(bananaA, bananaB, bambooYoung, palmA, monsteraB);
                    Plant(sapling, px, pz, Mathf.Lerp(1.4f, 3.2f, rise) * Range(.8f, 1.1f) / biome.Plants[sapling].Height, .08f);
                }
                else if (roll < .78f && Chance(.3))
                    ProceduralArt.BoulderInto(ChunkAt(px, pz).Low, random, Ground(px, pz, .15f), new Vector3(Range(.35f, .8f), Range(.2f, .4f), Range(.3f, .7f)));
                else if (roll < .84f)
                    // Roots washed out of the bank: a log mostly buried in the slope.
                    Plant(log, px, pz, Range(.5f, .8f), .3f);
                else if (rise > .7f && roll < .87f)
                    Plant(treeB, px, pz, Range(.4f, .55f), .15f);
            }
        }

        /// <summary>Head-high to small trees: bananas, bamboo, palms and young canopy trees over ferns.</summary>
        private static void Understory(float x, float z, float d)
        {
            float road = L.RoadDistance(x, z, out _);
            if (road < 4.2f) return;
            if (L.CraterDistance(x, z, out _) < 1f) return;
            var c = ChunkAt(x, z);
            if (Valley(x, z, d))
            {
                if (Chance(.12)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .2f), Range(3f, 7f));
                else if (Chance(.4)) Plant(grassTall, x, z, Range(.8f, 1.15f), .02f);
                else if (Chance(.15)) Plant(Pick(fernA, fernSmall), x, z, Range(.8f, 1.1f));
                return;
            }
            if (Chance(Sightline(x, z)))
            {
                // Regrowth in the sprayed strip stays below the view: bananas, grass and ferns.
                if (Chance(.25)) Plant(Pick(bananaA, bananaB), x, z, Range(.6f, .85f));
                else if (Chance(.5)) Plant(grassTall, x, z, Range(.85f, 1.2f), .02f);
                if (Chance(.4)) Plant(Pick(fernA, fernB), x + Range(-1f, 1f), z + Range(-1f, 1f), Range(.9f, 1.2f));
                return;
            }
            float clump = Mathf.PerlinNoise(x * .09f - 11f, z * .09f + 4f);
            float roll = (float)random.NextDouble();
            // Younger, smaller plants just behind the edge band, so the forest steps up instead of standing as a wall.
            float young = Mathf.Lerp(.5f, 1f, Mathf.InverseLerp(CoverBand, 11f, d));
            if (clump > .62f && roll < .45f) Plant(roll < .15f && d > 8f ? bamboo : bambooYoung, x, z, Range(.75f, 1.05f) * young, .1f);
            else if (roll < .3f) Plant(Pick(bananaA, bananaB), x, z, Range(.85f, 1.3f) * young, .1f);
            else if (roll < .45f) Plant(Pick(palmA, palmB), x, z, Range(.7f, .95f) * young, .1f);
            else if (roll < .62f) Plant(treeB, x, z, Range(.75f, 1.1f) * young, .15f);
            else if (roll < .78f) Plant(monsteraB, x, z, Range(1f, 1.4f));
            if (Chance(.65)) Plant(Pick(fernA, fernB, monsteraA, calathea), x + Range(-1f, 1f), z + Range(-1f, 1f), Range(.9f, 1.3f));
            if (d > 7f && Chance(.45)) Plant(Pick(bananaA, bananaB, bambooYoung), x + Range(-1.2f, 1.2f), z + Range(-1.2f, 1.2f), Range(.6f, .9f));
        }

        /// <summary>The canopy over the hills: big crowns, palms in the open, bamboo in the hollows, leafy mass beneath.</summary>
        private static void Forest(float x, float z, float d)
        {
            float road = L.RoadDistance(x, z, out _);
            if (road < 5f) return;
            if (L.CraterDistance(x, z, out _) < 1.5f) return;
            var c = ChunkAt(x, z);
            if (Valley(x, z, d))
            {
                if (Chance(.3)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .2f), Range(4f, 10f));
                else if (Chance(.15)) Plant(treeB, x, z, Range(.6f, .85f), .15f);
                if (Chance(.5)) Plant(grassTall, x + 1f, z, Range(.9f, 1.3f), .02f);
                return;
            }
            if (Chance(Sightline(x, z)))
            {
                if (Chance(.22)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .2f), Range(3f, 7f));
                if (Chance(.5)) Plant(grassTall, x + 1f, z, Range(.9f, 1.3f), .02f);
                if (Chance(.35)) Plant(Pick(bananaA, bananaB), x + Range(-2f, 2f), z + Range(-2f, 2f), Range(.6f, .85f));
                return;
            }
            float roll = (float)random.NextDouble();
            float clump = Mathf.PerlinNoise(x * .05f + 21f, z * .05f - 9f);
            float grow = Mathf.Lerp(.8f, 1.25f, Mathf.InverseLerp(UnderstoryBand, 40f, d));
            if (roll < .1f) { }
            else if (clump > .66f && roll < .35f) Plant(bamboo, x, z, Range(.9f, 1.2f), .1f);
            else if (roll < .25f) Plant(Pick(palmA, palmB), x, z, Range(.85f, 1.1f), .1f);
            else if (roll < .55f) Plant(treeB, x, z, Range(.9f, 1.3f) * grow, .15f);
            else Plant(treeA, x, z, Range(.6f, .85f) * grow, .2f);
            if (d < 30f && Chance(.45)) Plant(Pick(fernB, monsteraA, calathea), x + Range(-2f, 2f), z + Range(-2f, 2f), Range(1f, 1.4f));
            // The leafy mass between trunks.
            int fill = random.Next(1, 3);
            for (int i = 0; i < fill; i++)
                Plant(Pick(bananaA, bananaB, monsteraB, bambooYoung), x + Range(-2.4f, 2.4f), z + Range(-2.4f, 2.4f), Range(.8f, 1.25f));
        }

        /// <summary>Far hills: big crowns that read as one canopy through the haze.</summary>
        private static void Far(float x, float z, float d)
        {
            if (L.RoadDistance(x, z, out _) < 6f) return;
            var c = ChunkAt(x, z);
            float valley = L.Bombed(x, z);
            if (Chance(Sightline(x, z)))
            {
                if (Chance(.2)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .3f), Range(4f, 8f));
                return;
            }
            if (z > -60f && Chance(valley))
            {
                if (Chance(.25)) ProceduralArt.DeadTreeInto(c.Tall, random, Ground(x, z, .3f), Range(5f, 11f));
                return;
            }
            if (Chance(.2)) Plant(Pick(palmA, palmB), x, z, Range(.9f, 1.15f), .1f);
            else Plant(Chance(.6) ? treeA : treeB, x, z, Range(.75f, 1.05f) * (Chance(.6) ? 1f : 1.6f), .3f);
        }
    }
}
