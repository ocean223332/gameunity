using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Terrain meshes and ground paint. One blended surface replaces separate
    /// clearing, puddle and rut meshes: the splat map paints four scanned layers
    /// (laterite, mud, leaf litter, grass) plus puddles, trampled paths, scorch and
    /// crater ejecta onto a single heightfield; a relief map shades the hollows, path
    /// beds and blast pits the flat playfield cannot carry, and a road distance field
    /// lets the shader cut the wheel ruts at any resolution.
    /// </summary>
    internal static class EnvironmentGround
    {
        private const float InnerExtent = 64f, InnerCell = 1f, OuterExtent = 208f, OuterCell = 4f;
        private const float SplatMin = -64f, SplatSize = 128f;
        private const int SplatResolution = 1024;
        private const float RoadCell = .5f;
        private const int RoadGrid = (int)(SplatSize / RoadCell) + 1;

        /// <summary>Potholes (x, z, radius): shallow, irregular bowls with a little standing water at the bottom.</summary>
        private static readonly Vector3[] Puddles =
        {
            new Vector3(7.4f, 1.9f, .55f), new Vector3(-9.8f, 6.4f, .5f), new Vector3(12.6f, -7.2f, .6f), new Vector3(-13.8f, -7.4f, .5f),
            new Vector3(28f, 3.6f, .75f)
        };

        private static readonly Vector3[] Scorches =
        {
            new Vector3(-1.5f, -6.9f, 1.7f), new Vector3(8.2f, 6.6f, 1.3f), new Vector3(-12.6f, -1.6f, 1.4f), new Vector3(21.5f, 14.5f, 1f)
        };

        /// <summary>Footpaths worn between the playfield and the station buildings, and between buildings (polylines).</summary>
        private static readonly Vector2[][] Paths =
        {
            new[] { new Vector2(-.5f, 6f), new Vector2(-.9f, 9.5f), new Vector2(-1.7f, 12.5f), new Vector2(-1f, 14.6f) },        // shed
            new[] { new Vector2(-5f, 5f), new Vector2(-7.8f, 8.6f), new Vector2(-9.9f, 12f), new Vector2(-11f, 13.6f) },        // bunker
            new[] { new Vector2(-6f, 15.2f), new Vector2(-8.4f, 15.8f), new Vector2(-9.6f, 15.2f) },                             // shed - bunker
            new[] { new Vector2(-13f, 6f), new Vector2(-16.6f, 9.8f), new Vector2(-20f, 12.6f), new Vector2(-21.8f, 13.8f) },   // watchtower
            new[] { new Vector2(9f, 5f), new Vector2(13.4f, 7.9f), new Vector2(17.2f, 10.6f), new Vector2(20.4f, 12.8f) },       // cook fire
            new[] { new Vector2(14f, 5.2f), new Vector2(18.6f, 7.4f), new Vector2(22.2f, 8.8f) },                                // bicycles
            new[] { new Vector2(8f, -5f), new Vector2(10.4f, -9f), new Vector2(12.2f, -12.4f), new Vector2(13.4f, -14.8f) },    // anti-aircraft pit
            new[] { new Vector2(-2f, -6f), new Vector2(-3.1f, -9.6f), new Vector2(-5f, -13f), new Vector2(-6.5f, -17f) },       // down to the valley
            new[] { new Vector2(-9f, -4.6f), new Vector2(-13.6f, -7.6f), new Vector2(-18.6f, -9.4f), new Vector2(-22.6f, -10.4f) } // depot
        };

        /// <summary>Trodden lanes across the yard where those footpaths meet in the middle (pairs of points).</summary>
        private static readonly Vector2[] Lanes =
        {
            new Vector2(-.5f, 8f), new Vector2(.5f, .5f), new Vector2(-7.5f, 7.5f), new Vector2(-2.5f, 1.5f),
            new Vector2(13.5f, 7.5f), new Vector2(4f, 1.5f), new Vector2(-15f, 8.5f), new Vector2(-6f, 2.5f),
            new Vector2(10.5f, -8f), new Vector2(3f, -1.5f), new Vector2(-3f, -9f), new Vector2(-1f, -2f)
        };

        public static Material CreateMaterial()
        {
            var material = ProceduralArt.Environment("Ground", "Env_Ground");
            if (material == null)
                return ProceduralArt.Lit("Env_Ground", Color.white, .06f, false, Layer("Litter", "Albedo"), Layer("Litter", "Normal"), .8f);
            foreach (string layer in Layers)
            {
                material.SetTexture("_" + layer + "Map", Layer(layer, "Albedo"));
                material.SetTexture("_" + layer + "Normal", Layer(layer, "Normal"));
                material.SetTexture("_" + layer + "Mask", Layer(layer, "Mask"));
            }
            BakeSplat(out var splat, out var tone, out var relief, out var roadField);
            material.SetTexture("_SplatMap", splat);
            material.SetTexture("_ToneMap", tone);
            material.SetTexture("_ReliefMap", relief);
            material.SetTexture("_RoadField", roadField);
            material.SetVector("_SplatRect", new Vector4(SplatMin, SplatMin, 1f / SplatSize, 1f / SplatSize));
            return material;
        }

        /// <summary>The scanned ground layers in Resources/Environment/Ground (see Art/Source/README.md).</summary>
        private static readonly string[] Layers = { "Laterite", "Mud", "Litter", "Grass" };

        private static Texture2D Layer(string layer, string map) => Resources.Load<Texture2D>("Environment/Ground/" + layer + "_" + map);

        public static void Build(Transform root, Material ground)
        {
            var inner = Grid(-InnerExtent, InnerExtent, InnerCell, false);
            Spawn(root, "Terrain_Station", inner, ground, true);
            var outer = Grid(-OuterExtent, OuterExtent, OuterCell, true);
            Spawn(root, "Terrain_Hills", outer, ground, false);
            BuildCraterWater(root);
        }

        // ---------- Heightfield ----------

        /// <summary>
        /// A smooth-shaded height grid. The outer grid skips the inner square; the
        /// inner grid's border follows the outer grid's coarser edge so they meet
        /// without cracks.
        /// </summary>
        private static Mesh Grid(float min, float max, float cell, bool skipInner)
        {
            int n = Mathf.RoundToInt((max - min) / cell);
            int stride = n + 1;
            var heights = new float[stride * stride];
            ProceduralArt.ParallelRows(stride, z =>
            {
                for (int x = 0; x < stride; x++)
                    heights[z * stride + x] = L.GroundHeight(min + x * cell, min + z * cell);
            });
            if (!skipInner)
            {
                int step = Mathf.RoundToInt(OuterCell / cell);
                for (int i = 0; i <= n; i++)
                {
                    int i0 = i / step * step, i1 = Mathf.Min(n, i0 + step);
                    float t = i1 == i0 ? 0 : (float)(i - i0) / (i1 - i0);
                    heights[i] = Mathf.Lerp(heights[i0], heights[i1], t);
                    heights[n * stride + i] = Mathf.Lerp(heights[n * stride + i0], heights[n * stride + i1], t);
                    heights[i * stride] = Mathf.Lerp(heights[i0 * stride], heights[i1 * stride], t);
                    heights[i * stride + n] = Mathf.Lerp(heights[i0 * stride + n], heights[i1 * stride + n], t);
                }
            }
            var vertices = new Vector3[stride * stride];
            var normals = new Vector3[stride * stride];
            for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                int i = z * stride + x;
                vertices[i] = new Vector3(min + x * cell, heights[i], min + z * cell);
                float left = heights[z * stride + Mathf.Max(0, x - 1)], right = heights[z * stride + Mathf.Min(n, x + 1)];
                float down = heights[Mathf.Max(0, z - 1) * stride + x], up = heights[Mathf.Min(n, z + 1) * stride + x];
                float spanX = (Mathf.Min(n, x + 1) - Mathf.Max(0, x - 1)) * cell, spanZ = (Mathf.Min(n, z + 1) - Mathf.Max(0, z - 1)) * cell;
                normals[i] = new Vector3(-(right - left) / spanX, 1f, -(up - down) / spanZ).normalized;
            }
            var triangles = new List<int>(n * n * 6);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (skipInner)
                {
                    float x0 = min + x * cell, z0 = min + z * cell;
                    if (x0 >= -InnerExtent && x0 + cell <= InnerExtent && z0 >= -InnerExtent && z0 + cell <= InnerExtent) continue;
                }
                int a = z * stride + x, b = a + stride, c = b + 1, d = a + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
            var mesh = new Mesh { name = skipInner ? "Env_TerrainOuter" : "Env_TerrainInner", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void BuildCraterWater(Transform root)
        {
            var material = ProceduralArt.Environment("Water", "Env_CraterWater") ??
                           ProceduralArt.Lit("Env_CraterWater", ProceduralArt.Hex("#1C1E17"), .95f);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const int segments = 28;
            foreach (var crater in L.Craters)
            {
                if (!crater.Water) continue;
                int center = vertices.Count;
                vertices.Add(new Vector3(crater.Center.x, crater.WaterLevel, crater.Center.y));
                float radius = crater.Radius * .58f;
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2 / segments;
                    vertices.Add(new Vector3(crater.Center.x + Mathf.Cos(a) * radius, crater.WaterLevel, crater.Center.y + Mathf.Sin(a) * radius));
                    triangles.Add(center); triangles.Add(center + 1 + (s + 1) % segments); triangles.Add(center + 1 + s);
                }
            }
            var mesh = new Mesh { name = "Env_CraterWater" };
            mesh.SetVertices(vertices);
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            Spawn(root, "CraterWater", mesh, material, false);
        }

        // ---------- Ground paint ----------

        private static float Band(float value, float from, float to) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distance to the nearest footpath.</summary>
        private static float PathDistance(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            foreach (var path in Paths)
                for (int i = 0; i < path.Length - 1; i++)
                {
                    Vector2 a = path[i], b = path[i + 1];
                    if (x < Mathf.Min(a.x, b.x) - 2f || x > Mathf.Max(a.x, b.x) + 2f || z < Mathf.Min(a.y, b.y) - 2f || z > Mathf.Max(a.y, b.y) + 2f) continue;
                    best = Mathf.Min(best, SegmentDistance(p, a, b));
                }
            return best;
        }

        /// <summary>Relief range encoded in the relief map's height channel (m either side of the terrain).</summary>
        private const float ReliefRange = .12f;
        private const int RoadFieldResolution = 512;

        /// <summary>
        /// Splat RGBA = laterite, wetness, ash, grass tint; tone RGBA = dry compacted dust, damp dark soil, fresh red
        /// laterite, broad value (0.5 neutral); relief RGBA = micro-relief slope along x and z (0.5 flat), height
        /// (0.5 level), compaction. All linear, sampled in world XZ. The relief only shades: the mesh keeps
        /// GroundHeight, so the playfield stays flat for the simulation.
        /// </summary>
        private static void BakeSplat(out Texture2D splat, out Texture2D tone, out Texture2D relief, out Texture2D roadField)
        {
            // The signed road distance is smooth, so a half-metre grid interpolates it
            // exactly enough for the ruts at a fraction of the per-texel cost.
            var road = new float[RoadGrid * RoadGrid];
            ProceduralArt.ParallelRows(RoadGrid, row =>
            {
                for (int column = 0; column < RoadGrid; column++)
                {
                    L.RoadDistance(SplatMin + column * RoadCell, SplatMin + row * RoadCell, out float side);
                    road[row * RoadGrid + column] = side;
                }
            });
            var clumps = GrassClumps();
            const int n = SplatResolution;
            var pixels = new Color32[n * n];
            var tones = new Color32[n * n];
            var heights = new float[n * n];
            var compaction = new float[n * n];
            ProceduralArt.ParallelRows(n, row =>
            {
                for (int column = 0; column < n; column++)
                {
                    float x = SplatMin + (column + .5f) * SplatSize / n, z = SplatMin + (row + .5f) * SplatSize / n;
                    int i = row * n + column;
                    pixels[i] = Paint(x, z, road, clumps, out tones[i], out heights[i], out compaction[i]);
                }
            });
            // Slopes by central differences, so the shader's relief normal interpolates smoothly between texels.
            var reliefPixels = new Color32[n * n];
            float texel = SplatSize / n;
            ProceduralArt.ParallelRows(n, row =>
            {
                for (int column = 0; column < n; column++)
                {
                    int i = row * n + column;
                    float dx = (heights[row * n + Mathf.Min(n - 1, column + 1)] - heights[row * n + Mathf.Max(0, column - 1)]) / (2f * texel);
                    float dz = (heights[Mathf.Min(n - 1, row + 1) * n + column] - heights[Mathf.Max(0, row - 1) * n + column]) / (2f * texel);
                    reliefPixels[i] = new Color32((byte)(Mathf.Clamp01(.5f + dx) * 255), (byte)(Mathf.Clamp01(.5f + dz) * 255),
                        (byte)(Mathf.Clamp01(.5f + heights[i] / (2f * ReliefRange)) * 255), (byte)(Mathf.Clamp01(compaction[i]) * 255));
                }
            });
            splat = Texture("Env_GroundSplat", n, pixels);
            tone = Texture("Env_GroundTone", n, tones);
            relief = Texture("Env_GroundRelief", n, reliefPixels);
            roadField = BakeRoadField(road);
        }

        /// <summary>
        /// Signed distance to the nearer of the road and the depot track (R), rut strength (G) and the distance's
        /// gradient (B, A), half floats. The shader cuts the wheel ruts from it at any resolution.
        /// </summary>
        private static Texture2D BakeRoadField(float[] road)
        {
            const int n = RoadFieldResolution;
            float texel = SplatSize / n;
            var side = new float[n * n];
            var strength = new float[n * n];
            Vector2 trackEnd = L.DepotTrack[L.DepotTrack.Length - 1];
            ProceduralArt.ParallelRows(n, row =>
            {
                for (int column = 0; column < n; column++)
                {
                    float x = SplatMin + (column + .5f) * texel, z = SplatMin + (row + .5f) * texel;
                    int i = row * n + column;
                    float s = RoadSide(road, x, z);
                    // Broken ruts: deep in stretches, nearly smoothed out in others.
                    float rut = Mathf.Lerp(.45f, 1f, Band(ProceduralArt.Fbm(x * .11f + 13f, z * .11f - 6f, 2), .35f, .6f));
                    float t = L.LineDistance(L.DepotTrack, x, z, out float trackSide);
                    if (t < Mathf.Abs(s))
                    {
                        // The track's ruts start once it is clear of the road and stop under the truck.
                        rut = Band(Vector2.Distance(new Vector2(x, z), trackEnd), .8f, 3f) * Band(Mathf.Abs(s), 1.2f, 2.2f);
                        s = trackSide;
                    }
                    side[i] = Mathf.Clamp(s, -8f, 8f);
                    strength[i] = rut;
                }
            });
            var data = new ushort[n * n * 4];
            for (int row = 0; row < n; row++)
            for (int column = 0; column < n; column++)
            {
                int i = row * n + column;
                float gx = side[row * n + Mathf.Min(n - 1, column + 1)] - side[row * n + Mathf.Max(0, column - 1)];
                float gz = side[Mathf.Min(n - 1, row + 1) * n + column] - side[Mathf.Max(0, row - 1) * n + column];
                var g = new Vector2(gx, gz);
                g = g.sqrMagnitude > 1e-8f ? g.normalized : Vector2.zero;
                data[i * 4] = Mathf.FloatToHalf(side[i]);
                data[i * 4 + 1] = Mathf.FloatToHalf(strength[i]);
                data[i * 4 + 2] = Mathf.FloatToHalf(g.x);
                data[i * 4 + 3] = Mathf.FloatToHalf(g.y);
            }
            var texture = new Texture2D(n, n, TextureFormat.RGBAHalf, true, true)
            {
                name = "Env_GroundRoadField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixelData(data, 0);
            texture.Apply(true, true);
            return texture;
        }

        private static Texture2D Texture(string name, int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float RoadSide(float[] road, float x, float z)
        {
            float u = Mathf.Clamp((x - SplatMin) / RoadCell, 0, RoadGrid - 1.001f), v = Mathf.Clamp((z - SplatMin) / RoadCell, 0, RoadGrid - 1.001f);
            int i = (int)u, k = (int)v;
            float fu = u - i, fv = v - k;
            int a = k * RoadGrid + i;
            float bottom = road[a] + (road[a + 1] - road[a]) * fu, top = road[a + RoadGrid] + (road[a + RoadGrid + 1] - road[a + RoadGrid]) * fu;
            return bottom + (top - bottom) * fv;
        }

        private static bool Near(float x, float z, float cx, float cz, float reach)
        {
            float dx = x - cx, dz = z - cz;
            return dx * dx + dz * dz < reach * reach;
        }

        // ---------- Grass clumps ----------

        /// <summary>Cell of the grid that holds at most one grass clump each.</summary>
        public const float ClumpCell = 2.2f;

        private static uint Hash(int i, int k, uint salt)
        {
            uint h = unchecked((uint)i * 73856093u ^ (uint)k * 19349663u ^ salt * 83492791u);
            h ^= h >> 13;
            h = unchecked(h * 0x5bd1e995u);
            h ^= h >> 15;
            return h;
        }

        private static float Unit(uint h) => (h & 0xFFFFFF) / 16777216f;

        /// <summary>
        /// The grass clump of grid cell (i, k), if it has one: a tussock a metre or two across on the open ground
        /// of the playfield and its margin. EnvironmentFlora plants its blades; the ground paint greens and darkens
        /// the soil under it, so the clump grows out of the ground rather than standing on it.
        /// </summary>
        public static bool GrassClump(int i, int k, out Vector2 center, out float radius)
        {
            center = new Vector2((i + .15f + .7f * Unit(Hash(i, k, 1))) * ClumpCell, (k + .15f + .7f * Unit(Hash(i, k, 2))) * ClumpCell);
            radius = .45f + .55f * Unit(Hash(i, k, 3));
            float x = center.x, z = center.y;
            float outside = L.OutsideDistance(x, z);
            if (outside > 1.5f) return false;
            if (L.RoadDistance(x, z, out _) < 3f || L.LineDistance(L.DepotTrack, x, z, out _) < 2.2f) return false;
            if (L.InSite(x, z, 1f) || PathDistance(x, z) < 1.3f) return false;
            // Lanes across the yard stay bare.
            for (int l = 0; l < Lanes.Length; l += 2)
                if (SegmentDistance(center, Lanes[l], Lanes[l + 1]) < 1.6f) return false;
            float patch = GrassPatch(x, z, outside, ProceduralArt.Fbm(x * .19f, z * .19f, 3));
            return Unit(Hash(i, k, 4)) < Band(patch, .42f, .62f) * .9f;
        }

        private struct ClumpSpot
        {
            public Vector2 Center;
            public float Radius;
        }

        /// <summary>The clumps over the playfield and its margin, by cell.</summary>
        private static Dictionary<Vector2Int, ClumpSpot> GrassClumps()
        {
            var spots = new Dictionary<Vector2Int, ClumpSpot>();
            int reachX = Mathf.CeilToInt((L.HalfX + 3f) / ClumpCell), reachZ = Mathf.CeilToInt((L.HalfZ + 3f) / ClumpCell);
            for (int k = -reachZ; k <= reachZ; k++)
            for (int i = -reachX; i <= reachX; i++)
                if (GrassClump(i, k, out var c, out float r))
                    spots.Add(new Vector2Int(i, k), new ClumpSpot { Center = c, Radius = r });
            return spots;
        }

        /// <summary>0..1: under a grass clump, with a soft, frayed edge.</summary>
        private static float ClumpCover(float x, float z, float fine, Dictionary<Vector2Int, ClumpSpot> clumps)
        {
            int ci = Mathf.FloorToInt(x / ClumpCell), ck = Mathf.FloorToInt(z / ClumpCell);
            float cover = 0;
            for (int k = ck - 1; k <= ck + 1; k++)
            for (int i = ci - 1; i <= ci + 1; i++)
            {
                if (!clumps.TryGetValue(new Vector2Int(i, k), out var spot)) continue;
                float d = Vector2.Distance(new Vector2(x, z), spot.Center) / spot.Radius + (fine - .5f) * .8f;
                cover = Mathf.Max(cover, 1f - Band(d, .7f, 1.7f));
            }
            return cover;
        }

        private static Color32 Paint(float x, float z, float[] roadField, Dictionary<Vector2Int, ClumpSpot> clumps, out Color32 tone,
            out float relief, out float compact)
        {
            var p = new Vector2(x, z);
            float outside = L.OutsideDistance(x, z);
            float medium = ProceduralArt.Fbm(x * .19f, z * .19f, 3), fine = ProceduralArt.Fbm(x * .7f + 31f, z * .7f, 2);
            float earth = 0, wet = 0, ash = 0, grass = 0;
            compact = 0;
            // Gentle undulation of the trodden ground: a few centimetres over a metre or two, which the low sun picks out.
            relief = (ProceduralArt.Fbm(x * .55f + 40f, z * .55f - 12f, 2) - .5f) * .07f + (fine - .5f) * .02f;

            // Broad soil zones, tens of metres across: dry trodden dust, damp hollows, raw red patches.
            float zone = ProceduralArt.Fbm(x * .045f + 11f, z * .045f - 7f, 3);
            float dust = Band(zone, .47f, .6f) * .85f, damp = Band(zone, .42f, .3f) * .45f;
            float red = Band(ProceduralArt.Fbm(x * .08f - 3f, z * .08f + 5f, 2), .55f, .7f) * .5f;
            // Damp soil lies in the low ground: the damp zones are shallow hollows, not stains.
            relief -= damp * .06f;
            if (outside > 0)
            {
                // Moist, shaded soil under the forest edge.
                damp = Mathf.Max(damp, Band(outside, .5f, 5f) * .6f);
                dust *= 1f - Band(outside, 0f, 3f);
            }

            // Laterite across the playfield, fraying into jungle floor past its edge over a wide, broken seam.
            earth = 1f - Band(outside + (medium - .5f) * 5f + (fine - .5f) * 1.6f, -1.5f, 4f);
            // Inside the playfield grass grows as clumps (GrassClump) with only a thin sward between them near the
            // edges; past the edge the paint ramps into the forest fringe's GrassPatch across a wobbling line.
            float inward = Mathf.Min(L.HalfX - Mathf.Abs(x), L.HalfZ - Mathf.Abs(z));
            float across = -inward + (ProceduralArt.Fbm(x * .07f - 8f, z * .07f + 15f, 2) - .5f) * 7f + (medium - .5f) * 2.5f;
            float yard = ProceduralArt.Fbm(x * .085f + 3f, z * .085f + 9f, 3) + .22f * (1f - Mathf.Clamp01(-across / 3.5f)) + .12f * Mathf.Clamp01(across / 2f);
            float patch = Mathf.Lerp(yard, GrassPatch(x, z, outside, medium), Band(outside, 2f, 5f));
            float tufts = Band(ProceduralArt.Fbm(x * .9f + 5f, z * .9f - 3f, 2) + (fine - .5f) * .3f, .6f, .72f);
            float sward = Band(patch + (fine - .5f) * .1f, .52f, .74f) * Mathf.Lerp(.55f, 1f, tufts);
            // The thin sward keeps to the edge of the yard; further in, grass only grows in the clumps.
            grass = sward * Mathf.Lerp(.35f, 1f, Band(across, -4f, 0f));
            float seam = Band(patch, .49f, .57f) * (1f - Band(patch, .63f, .74f));
            float clump = ClumpCover(x, z, fine, clumps);
            grass = Mathf.Max(grass, clump * .9f);
            earth *= 1f - grass * .7f - seam * .3f;
            // Damp soil darkens the ground along the grass edge and under the clumps.
            damp = Mathf.Max(damp, seam * .45f, clump * .35f);
            relief += clump * .015f;
            if (outside > 0)
            {
                // Forest floor on the hills: dark, damp litter broken by moss and seedling patches, so sunlit
                // slopes between the trees never read as bare orange leaf fall.
                float floor = Band(outside, 5f, 14f);
                grass = Mathf.Max(grass, floor * (.3f + .6f * Band(medium + (fine - .5f) * .3f, .38f, .62f)));
                damp = Mathf.Max(damp, floor * .9f);
            }
            if (outside < 3f)
            {
                // Leaves and twigs blown into the yard: sparse drifts, a little denser towards the edges.
                float drift = ProceduralArt.Fbm(x * .33f + 21f, z * .33f - 13f, 3) + (fine - .5f) * .45f + (1f - Mathf.Clamp01(inward / 5f)) * .1f;
                earth *= 1f - Band(drift, .6f, .74f) * .6f;
            }

            // Truck road and the depot track: compacted beds; the shader cuts the wheel ruts from the road field.
            float side = RoadSide(roadField, x, z), road = Mathf.Abs(side);
            float track = L.LineDistance(L.DepotTrack, x, z, out _);
            if (road < 3.5f || track < 3f)
            {
                float bed = 1f - Band(road + (fine - .5f) * .9f, 1.7f, 2.9f);
                float trackBed = (1f - Band(track + (fine - .5f) * .7f, 1.2f, 2.1f)) * Band(Vector2.Distance(p, L.DepotTrack[L.DepotTrack.Length - 1]), 0f, 2.5f);
                earth = Mathf.Max(earth, bed, trackBed * .9f);
                grass *= 1f - Mathf.Max(bed, trackBed);
                float rut = (1f - Band(Mathf.Abs(road - .925f), .12f, .32f)) * bed;
                float trackRut = (1f - Band(Mathf.Abs(track - .925f), .12f, .32f)) * trackBed;
                // Damp along the tracks; water stands only in a few short low spots of them.
                float pool = Band(medium, .64f, .7f) * Band(fine, .52f, .62f);
                wet = Mathf.Max(wet, rut * Mathf.Lerp(.04f, .2f, Band(medium, .4f, .65f)), Mathf.Max(rut, trackRut) * pool * .8f, trackRut * .12f);
                // Pale compacted crown between the ruts, churned mud on the shoulders.
                dust = Mathf.Max(dust, (1f - Band(road, .35f, .7f)) * bed * .6f, (1f - Band(track, .3f, .6f)) * trackBed * .4f);
                damp = Mathf.Max(damp, rut * .45f, trackRut * .4f, bed * Band(road, 1.1f, 1.5f) * (1f - Band(road, 2.3f, 3f)) * .5f);
                compact = Mathf.Max(compact, bed * (1f - Band(road, 1.3f, 1.9f)), trackBed * .8f);
                // The bed lies a little lower than its shoulders, where the wheels pushed the soil aside.
                relief += -bed * .02f + bed * Band(road, 1.5f, 1.9f) * (1f - Band(road, 2.1f, 2.8f)) * .045f - trackBed * .012f;
                // A grassy crown between the ruts once the road leaves the station.
                if (outside > 2f) grass = Mathf.Max(grass, (1f - Band(road, .25f, .5f)) * bed * .8f);
            }

            foreach (var puddle in Puddles)
            {
                if (!Near(x, z, puddle.x, puddle.y, puddle.z * 3f)) continue;
                // An irregular, slightly elongated bowl with water only at the bottom.
                Vector2 offset = p - new Vector2(puddle.x, puddle.y);
                float angle = puddle.x * 1.7f + puddle.y;
                var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float along = Vector2.Dot(offset, axis), cross = axis.x * offset.y - axis.y * offset.x;
                float d = Mathf.Sqrt(along * along / 1.6f + cross * cross) / puddle.z + (fine - .5f) * .7f + (medium - .5f) * .4f;
                float bowl = 1f - Band(d, .1f, 1.3f);
                relief -= bowl * .05f;
                wet = Mathf.Max(wet, (1f - Band(d, .1f, .75f)) * .85f, (1f - Band(d, .5f, 1.6f)) * .4f);
                damp = Mathf.Max(damp, (1f - Band(d, .8f, 2.6f)) * .5f);
                grass *= Band(d, .9f, 1.4f);
            }

            float footpath = PathDistance(x, z) + (fine - .5f) * .35f;
            if (footpath < 1.4f)
            {
                // A hard, pale walking line sunk a little into the ground, with trodden edges where the grass gives up.
                float path = 1f - Band(footpath, .25f, .6f), worn = 1f - Band(footpath, .4f, 1.3f);
                earth = Mathf.Max(earth, worn * .9f);
                grass *= 1f - worn * .85f;
                dust = Mathf.Max(dust, path * .75f);
                compact = Mathf.Max(compact, path, worn * .4f);
                relief -= path * .02f;
            }
            for (int i = 0; i < Lanes.Length; i += 2)
            {
                // Wide, faint compacted lanes: lighter dust, never a drawn line.
                float d = SegmentDistance(p, Lanes[i], Lanes[i + 1]) + (medium - .5f) * 1.6f;
                if (d > 2.6f) continue;
                float lane = (1f - Band(d, .4f, 2.4f)) * Band(fine, .25f, .6f);
                dust = Mathf.Max(dust, lane * .65f);
                grass *= 1f - lane * .7f;
                compact = Mathf.Max(compact, lane * .5f);
            }

            foreach (var site in L.Sites)
            {
                if (!Near(x, z, site.x, site.y, site.z * 1.25f)) continue;
                float d = Vector2.Distance(p, new Vector2(site.x, site.y)) / site.z + (medium - .5f) * .5f;
                float floor = 1f - Band(d, .55f, .95f);
                earth = Mathf.Max(earth, floor * .8f);
                grass *= 1f - floor * .8f;
                dust = Mathf.Max(dust, floor * .7f);
                compact = Mathf.Max(compact, floor * .6f);
            }

            foreach (var scorch in Scorches)
            {
                if (!Near(x, z, scorch.x, scorch.y, scorch.z * 1.4f)) continue;
                float d = Vector2.Distance(p, new Vector2(scorch.x, scorch.y)) / scorch.z + (fine - .5f) * .6f;
                ash = Mathf.Max(ash, (1f - Band(d, .35f, 1.05f)) * .9f);
                // A shell scar: a shallow blast pit with thrown earth around it.
                relief += -(1f - Band(d, .1f, .6f)) * .05f + (1f - Band(Mathf.Abs(d - .75f), .05f, .35f)) * .02f;
            }

            // Bombed slope: burnt bare soil, craters ringed with ash and ejecta. The west flank has grown back over.
            float burnt = L.Bombed(x, z) * Band(z, -14f, -20f);
            if (burnt > 0)
            {
                grass *= 1f - burnt * .85f;
                earth = Mathf.Max(earth, burnt * Band(medium, .4f, .7f) * .65f);
                ash = Mathf.Max(ash, burnt * Band(fine, .55f, .8f) * .35f);
                // Churned soil dries pale in patches across the slope.
                dust = Mathf.Max(dust, burnt * Band(zone, .44f, .58f) * .6f);
            }
            if (L.Valley(x, z) > 0)
            {
                float rim = L.CraterDistance(x, z, out int index);
                if (index >= 0)
                {
                    float r = rim / L.Craters[index].Radius;
                    float ejecta = 1f - Band(r + (medium - .5f) * .5f, .35f, 1.1f);
                    earth = Mathf.Max(earth, ejecta);
                    // Freshly thrown subsoil is the reddest earth on the map.
                    red = Mathf.Max(red, ejecta * .4f);
                    ash = Mathf.Max(ash, (1f - Band(Mathf.Abs(r - .05f) + (fine - .5f) * .3f, .15f, .55f)) * .6f);
                    if (r < 0)
                    {
                        wet = Mathf.Max(wet, Band(-r, .3f, .6f) * .8f);
                        damp = Mathf.Max(damp, Band(-r, 0f, .5f) * .8f);
                    }
                    grass *= Band(r, .2f, 1.4f);
                }
            }

            // Fade to a plain forest floor (damp litter with some moss) before the texture edge clamps, so the
            // hills past the splat continue it without a seam.
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float keep = 1f - Band(edge, SplatSize * .5f - 6f, SplatSize * .5f - 1f);
            grass = Mathf.Lerp(.55f, grass, keep);
            damp = Mathf.Lerp(.9f, damp, keep);
            relief = Mathf.Clamp(relief * keep, -ReliefRange, ReliefRange);
            compact *= keep;
            // Broad value at two scales, so no stretch of open ground a few tens of metres across keeps one tone.
            float value = ProceduralArt.Fbm(x * .055f - 17f, z * .055f + 4f, 3) * .6f + ProceduralArt.Fbm(x * .16f + 9f, z * .16f + 27f, 2) * .4f;
            value = Mathf.Lerp(.5f, Band(value, .3f, .7f), keep);
            tone = new Color32((byte)(Mathf.Clamp01(dust * keep) * 255), (byte)(Mathf.Clamp01(damp) * 255),
                (byte)(Mathf.Clamp01(red * keep) * 255), (byte)(value * 255));
            return new Color32((byte)(Mathf.Clamp01(earth * keep) * 255), (byte)(Mathf.Clamp01(wet * keep) * 255),
                (byte)(Mathf.Clamp01(ash * keep) * 255), (byte)(Mathf.Clamp01(grass) * 255));
        }

        /// <summary>
        /// 0..1 grass before roads, paths and craters clear it: islands creeping in along the playfield edges,
        /// then a fringe a few metres deep along the forest edge. EnvironmentFlora grows its grass clumps here.
        /// </summary>
        public static float GrassCover(float x, float z) =>
            GrassCover(x, z, L.OutsideDistance(x, z), ProceduralArt.Fbm(x * .19f, z * .19f, 3));

        private static float GrassCover(float x, float z, float outside, float medium) => Band(GrassPatch(x, z, outside, medium), .56f, .7f);

        /// <summary>The continuous grass field behind GrassCover, whose .56-.7 band is where the clumps grow.</summary>
        private static float GrassPatch(float x, float z, float outside, float medium)
        {
            // Past the playfield edge, scaled so that band gives 1 - Band(outside', 3, 9).
            if (outside > 0) return .7f - (outside + (medium - .5f) * 4f - 3f) / 6f * .14f;
            float inward = Mathf.Min(L.HalfX - Mathf.Abs(x), L.HalfZ - Mathf.Abs(z));
            return ProceduralArt.Fbm(x * .085f + 3f, z * .085f + 9f, 3) + (1f - Mathf.Clamp01(inward / 3.5f)) * .22f;
        }

        private static void Spawn(Transform parent, string name, Mesh mesh, Material material, bool castShadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }
    }
}
