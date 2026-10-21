using System.Collections.Generic;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Block = TienTuyen.Duel.DuelArenaLayout.Block;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The duel ground: a laterite clearing in the jungle, ringed by a knee-high sandbag berm on the edge of play,
    /// with the nine cover blocks of <see cref="DuelArenaLayout"/> built as sandbag emplacements that fill their
    /// collision boxes exactly. Past the berm the ground stays flat for the trailing camera, then rises into a bowl of
    /// forested hills under the station's far ranges.
    /// The whole arena is its own half-turn mirror, as the layout is: terrain, ground paint, bags and every plant
    /// are generated for one half and rotated onto the other, so neither slot sees a different fight. Nothing here
    /// has a collider; the simulation reads <see cref="DuelArenaLayout"/> directly.
    /// </summary>
    internal static class DuelArenaView
    {
        /// <summary>Inner faces of the berm: just outside the clamp plus a body's radius.</summary>
        public const float BermX = DuelArenaLayout.HalfWidth + .5f, BermZ = DuelArenaLayout.HalfHeight + .5f;
        /// <summary>Flat ground past the berm, wider than the camera's 4.3 m trailing reach.</summary>
        private const float FlatMargin = 4.5f;
        /// <summary>The clearing's 1 m grid; the hills' 4 m grid reaches the backdrop's ground at <see cref="EnvironmentGround.OuterExtent"/>.</summary>
        internal const float InnerExtentX = 48f, InnerExtentZ = 40f, OuterCell = 4f;
        private const float OuterExtent = EnvironmentGround.OuterExtent;
        /// <summary>Width of the band before the terrain's edge over which the hills settle onto the backdrop's ground.</summary>
        private const float EdgeBlend = 90f;
        private const float SplatHalfX = 30f, SplatHalfZ = 16f;
        private const int SplatWidth = 1024, SplatHeight = 546;
        private const float ReliefRange = .12f;
        private static readonly string[] Bags = { "bag_a", "bag_b", "bag_c", "bag_d" };
        private const float BagLength = .62f, BagWidth = .37f, BagHeight = .20f;
        private static readonly Matrix4x4 HalfTurn = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));

        private static System.Random random;

        public static Transform Build(Transform parent)
        {
            random = new System.Random(1972);
            var root = new GameObject("DuelArena").transform;
            root.SetParent(parent, false);
            var ground = GroundMaterial();
            Terrain(root, ground);
            DuelArenaGrass.Build(root, ground);
            var props = new PropBatch(root);
            foreach (var (block, mirrored) in CoverPairs())
                Emplacement(props, block, mirrored);
            Berm(props);
            SpawnStores(props);
            props.Emit();
            Stakes(root);
            Revetments(root);
            DuelArenaFlora.Build(root);
            EnvironmentBackdrop.Build(root);
            DuelArenaAir.Build(root);
            return root;
        }

        // ---------- Terrain ----------

        /// <summary>Distance outside the berm's rectangle; zero inside it.</summary>
        public static float OutsideBerm(float x, float z)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x) - BermX), dz = Mathf.Max(0, Mathf.Abs(z) - BermZ);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Ground height: flat to <see cref="FlatMargin"/> past the berm, then a rim of low knolls and the hills behind
        /// it. Every noise is averaged with its half-turn so h(x, z) = h(-x, -z) exactly, out to the last
        /// <see cref="EdgeBlend"/> metres, where the hills settle onto the height the backdrop's innermost ring stands at
        /// (EnvironmentLayout.BaseHeight), so the two meet without a cut skyline.
        /// </summary>
        public static float Height(float x, float z)
        {
            float d = OutsideBerm(x, z) - FlatMargin;
            if (d <= 0) return 0;
            float edge = Mathf.SmoothStep(0, 1, (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (OuterExtent - EdgeBlend)) / EdgeBlend);
            float hills = Hills(x, z, d);
            return edge <= 0f ? hills : Mathf.Lerp(hills, EnvironmentLayout.BaseHeight(x, z), edge);
        }

        private static float Hills(float x, float z, float d)
        {
            float near = Mathf.SmoothStep(0, 1, d / 22f) * (1.6f + 7f * Mirrored(x, z, .05f, 3, 4.1f));
            float far = Mathf.SmoothStep(0, 1, (d - 18f) / 90f) * (8f + 46f * Mirrored(x, z, .011f, 3, -7.7f));
            // Gullies down the slopes keep the rim from reading as one smooth ring.
            float gully = 1f - Mathf.Abs(Mirrored(x, z, .035f, 2, 13.3f) * 2f - 1f);
            return near + far - gully * gully * 3f * Mathf.SmoothStep(0, 1, (d - 8f) / 30f);
        }

        private static float Mirrored(float x, float z, float frequency, int octaves, float offset) =>
            .5f * (ProceduralArt.Fbm(x * frequency + offset, z * frequency - offset, octaves) +
                   ProceduralArt.Fbm(-x * frequency + offset, -z * frequency - offset, octaves));

        private static void Terrain(Transform root, Material ground)
        {
            var inner = new Rect(-InnerExtentX, -InnerExtentZ, 2f * InnerExtentX, 2f * InnerExtentZ);
            var outer = new Rect(-OuterExtent, -OuterExtent, 2f * OuterExtent, 2f * OuterExtent);
            Spawn(root, "Terrain_Clearing", Grid("Duel_TerrainInner", inner, 1f, Rect.zero, OuterCell), ground, true);
            Spawn(root, "Terrain_Hills", Grid("Duel_TerrainOuter", outer, OuterCell, inner, OuterCell), ground, false);
        }

        private static void Spawn(Transform root, string name, Mesh mesh, Material material, bool castShadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.isStatic = true;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>A smooth height grid over a rectangle minus a hole; its border follows the coarser neighbour's cells.</summary>
        private static Mesh Grid(string name, Rect area, float cell, Rect hole, float borderCell)
        {
            int nx = Mathf.RoundToInt(area.width / cell), nz = Mathf.RoundToInt(area.height / cell), stride = nx + 1;
            var heights = new float[stride * (nz + 1)];
            ProceduralArt.ParallelRows(nz + 1, k =>
            {
                for (int i = 0; i < stride; i++) heights[k * stride + i] = Height(area.xMin + i * cell, area.yMin + k * cell);
            });
            int step = Mathf.RoundToInt(borderCell / cell);
            if (step > 1)
            {
                for (int i = 0; i <= nx; i++)
                {
                    int i0 = i / step * step, i1 = Mathf.Min(nx, i0 + step);
                    float t = i1 == i0 ? 0 : (float)(i - i0) / (i1 - i0);
                    heights[i] = Mathf.Lerp(heights[i0], heights[i1], t);
                    heights[nz * stride + i] = Mathf.Lerp(heights[nz * stride + i0], heights[nz * stride + i1], t);
                }
                for (int k = 0; k <= nz; k++)
                {
                    int k0 = k / step * step, k1 = Mathf.Min(nz, k0 + step);
                    float t = k1 == k0 ? 0 : (float)(k - k0) / (k1 - k0);
                    heights[k * stride] = Mathf.Lerp(heights[k0 * stride], heights[k1 * stride], t);
                    heights[k * stride + nx] = Mathf.Lerp(heights[k0 * stride + nx], heights[k1 * stride + nx], t);
                }
            }
            var vertices = new Vector3[heights.Length];
            var normals = new Vector3[heights.Length];
            for (int k = 0; k <= nz; k++)
            for (int i = 0; i <= nx; i++)
            {
                int index = k * stride + i;
                vertices[index] = new Vector3(area.xMin + i * cell, heights[index], area.yMin + k * cell);
                float left = heights[k * stride + Mathf.Max(0, i - 1)], right = heights[k * stride + Mathf.Min(nx, i + 1)];
                float down = heights[Mathf.Max(0, k - 1) * stride + i], up = heights[Mathf.Min(nz, k + 1) * stride + i];
                float spanX = (Mathf.Min(nx, i + 1) - Mathf.Max(0, i - 1)) * cell, spanZ = (Mathf.Min(nz, k + 1) - Mathf.Max(0, k - 1)) * cell;
                normals[index] = new Vector3(-(right - left) / spanX, 1f, -(up - down) / spanZ).normalized;
            }
            var triangles = new List<int>(nx * nz * 6);
            for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                float x0 = area.xMin + i * cell, z0 = area.yMin + k * cell;
                if (x0 >= hole.xMin && x0 + cell <= hole.xMax && z0 >= hole.yMin && z0 + cell <= hole.yMax) continue;
                int a = k * stride + i, b = a + stride, c = b + 1, d = a + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        // ---------- Ground paint ----------

        /// <summary>
        /// The station's ground material (its layers, meadow and hill shading) with the station's own paint swapped
        /// for this clearing's, and its road, river and karst switched off.
        /// </summary>
        private static Material GroundMaterial()
        {
            var material = EnvironmentGround.CreateMaterial();
            foreach (string map in new[] { "_SplatMap", "_ToneMap", "_ReliefMap", "_RoadField", "_RoadFar", "_RiverField" })
            {
                if (!material.HasProperty(map)) continue;
                var station = material.GetTexture(map);
                if (station != null) Object.Destroy(station);
            }
            BakeSplat(out var splat, out var tone, out var relief);
            material.SetTexture("_SplatMap", splat);
            material.SetTexture("_ToneMap", tone);
            material.SetTexture("_ReliefMap", relief);
            // No road anywhere: every distance far away.
            var noRoad = Field("Duel_NoRoad", 1000f);
            material.SetTexture("_RoadField", noRoad);
            material.SetTexture("_RoadFar", noRoad);
            material.SetTexture("_RiverField", Field("Duel_NoRiver", 1000f));
            material.SetVector("_SplatRect", new Vector4(-SplatHalfX, -SplatHalfZ, .5f / SplatHalfX, .5f / SplatHalfZ));
            material.SetVector("_Playfield", new Vector4(BermX, BermZ, FlatMargin, 0));
            // The river and karst branches test against these; out of reach, they never run.
            material.SetVector("_River", new Vector4(-1000f, 0f, 0f, 0f));
            material.SetVector("_Karst", Vector4.zero);
            return material;
        }

        private static Texture2D Field(string name, float distance)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            var value = new Color(distance, 0, 0, 0);
            texture.SetPixels(new[] { value, value, value, value });
            texture.Apply(false, true);
            return texture;
        }

        private static readonly Vector3[] Puddles = { new Vector3(-12.5f, -9.6f, 1.1f), new Vector3(5.6f, -9.8f, .8f) };
        private static readonly Vector3[] Scorches = { new Vector3(-5.8f, -7.2f, 1.3f), new Vector3(12.2f, -1.2f, 1.1f), new Vector3(-1.5f, 3.6f, .9f) };

        /// <summary>
        /// Splat, tone and relief over the clearing in EnvironmentGround's channel layout (splat: laterite, wetness,
        /// ash, grass; tone: dust, damp, red, value; relief: slope x, slope z, height, compaction). The bottom half is
        /// painted and the top half is its half-turn copy, so the paint is exactly point-symmetric.
        /// </summary>
        private static void BakeSplat(out Texture2D splat, out Texture2D tone, out Texture2D relief)
        {
            const int w = SplatWidth, h = SplatHeight;
            var pixels = new Color32[w * h];
            var tones = new Color32[w * h];
            var heights = new float[w * h];
            var compaction = new float[w * h];
            ProceduralArt.ParallelRows(h / 2, row =>
            {
                for (int column = 0; column < w; column++)
                {
                    float x = -SplatHalfX + (column + .5f) * 2f * SplatHalfX / w, z = -SplatHalfZ + (row + .5f) * 2f * SplatHalfZ / h;
                    int i = row * w + column, mirror = (h - 1 - row) * w + (w - 1 - column);
                    pixels[i] = pixels[mirror] = Paint(x, z, out tones[i], out heights[i], out compaction[i]);
                    tones[mirror] = tones[i];
                    heights[mirror] = heights[i];
                    compaction[mirror] = compaction[i];
                }
            });
            var reliefPixels = new Color32[w * h];
            float texelX = 2f * SplatHalfX / w, texelZ = 2f * SplatHalfZ / h;
            ProceduralArt.ParallelRows(h, row =>
            {
                for (int column = 0; column < w; column++)
                {
                    int i = row * w + column;
                    float dx = (heights[row * w + Mathf.Min(w - 1, column + 1)] - heights[row * w + Mathf.Max(0, column - 1)]) / (2f * texelX);
                    float dz = (heights[Mathf.Min(h - 1, row + 1) * w + column] - heights[Mathf.Max(0, row - 1) * w + column]) / (2f * texelZ);
                    reliefPixels[i] = new Color32((byte)(Mathf.Clamp01(.5f + dx) * 255), (byte)(Mathf.Clamp01(.5f + dz) * 255),
                        (byte)(Mathf.Clamp01(.5f + heights[i] / (2f * ReliefRange)) * 255), (byte)(Mathf.Clamp01(compaction[i]) * 255));
                }
            });
            splat = Texture("Duel_GroundSplat", pixels);
            tone = Texture("Duel_GroundTone", tones);
            relief = Texture("Duel_GroundRelief", reliefPixels);
        }

        private static Texture2D Texture(string name, Color32[] pixels)
        {
            var texture = new Texture2D(SplatWidth, SplatHeight, TextureFormat.RGBA32, true, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float Band(float value, float from, float to) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distance to the nearest cover box's footprint; negative inside.</summary>
        private static float CoverDistance(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var cover in DuelArenaLayout.Covers)
            {
                float dx = Mathf.Abs(x - cover.Center.x) - cover.Half.x, dz = Mathf.Abs(z - cover.Center.y) - cover.Half.y;
                float outside = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dz, 0)).magnitude + Mathf.Min(Mathf.Max(dx, dz), 0);
                best = Mathf.Min(best, outside);
            }
            return best;
        }

        // Worn lanes the fighters tread between spawn and cover; point-mirrored onto the other half when baked.
        private static readonly Vector2[] Lanes =
        {
            new Vector2(-15f, 0f), new Vector2(-10.5f, -1.6f), new Vector2(-10.5f, -1.6f), new Vector2(-5.6f, 1f),
            new Vector2(-5.6f, 1f), new Vector2(-1.2f, -1.9f), new Vector2(-15f, 0f), new Vector2(-11.2f, 6.2f),
            new Vector2(-11.2f, 6.2f), new Vector2(-2f, 6.3f), new Vector2(-6.4f, -5.8f), new Vector2(-1.4f, -6.4f),
            new Vector2(15f, 0f), new Vector2(10.5f, 1.6f), new Vector2(10.5f, 1.6f), new Vector2(5.6f, -1f),
            new Vector2(5.6f, -1f), new Vector2(1.2f, 1.9f), new Vector2(15f, 0f), new Vector2(11.2f, -6.2f),
            new Vector2(11.2f, -6.2f), new Vector2(2f, -6.3f), new Vector2(6.4f, 5.8f), new Vector2(1.4f, 6.4f)
        };

        private static Color32 Paint(float x, float z, out Color32 tone, out float height, out float compact)
        {
            var p = new Vector2(x, z);
            float outside = OutsideBerm(x, z);
            float inward = Mathf.Min(BermX - Mathf.Abs(x), BermZ - Mathf.Abs(z));
            float medium = ProceduralArt.Fbm(x * .19f, z * .19f, 3), fine = ProceduralArt.Fbm(x * .7f + 31f, z * .7f, 2);
            float earth, wet = 0, ash = 0, grass = 0;
            compact = 0;
            height = (ProceduralArt.Fbm(x * .55f + 40f, z * .55f - 12f, 2) - .5f) * .07f + (fine - .5f) * .02f;

            float zone = ProceduralArt.Fbm(x * .045f + 11f, z * .045f - 7f, 3);
            float dust = Band(zone, .47f, .6f) * .7f, damp = Band(zone, .42f, .3f) * .4f;
            float red = Band(ProceduralArt.Fbm(x * .08f - 3f, z * .08f + 5f, 2), .5f, .68f) * .6f;
            height -= damp * .06f;

            // Bare laterite over the fighting ground, fraying into the forest floor a few metres past the berm.
            earth = 1f - Band(outside + (medium - .5f) * 4f + (fine - .5f) * 1.4f, 1.5f, 6f);
            // Grass keeps to the edges: tufts along the berm's foot inside, a sward beyond it.
            float tufts = Band(ProceduralArt.Fbm(x * .9f + 5f, z * .9f - 3f, 2) + (fine - .5f) * .3f, .58f, .7f);
            float edge = 1f - Band(inward, .4f, 3.2f);
            grass = Mathf.Max(edge * tufts * .8f, Band(outside + (medium - .5f) * 3f, .8f, 5f) * (.55f + .45f * tufts));
            earth *= 1f - grass * .7f;
            damp = Mathf.Max(damp, Band(outside, 0f, 3f) * .55f, edge * .3f);
            if (outside > 0) dust *= 1f - Band(outside, 0f, 2f);

            // Leaves and twigs blown in from the forest: sparse drifts, denser by the edge.
            float drift = ProceduralArt.Fbm(x * .33f + 21f, z * .33f - 13f, 3) + (fine - .5f) * .45f + edge * .12f;
            earth *= 1f - Band(drift, .6f, .74f) * .55f;

            // The cover sits in trodden, damp soil where boots and rain gather at its foot.
            float cover = CoverDistance(x, z) + (fine - .5f) * .3f;
            if (cover < 1.8f)
            {
                float foot = 1f - Band(cover, .05f, 1.6f);
                damp = Mathf.Max(damp, foot * .55f);
                compact = Mathf.Max(compact, foot * .7f);
                grass *= 1f - foot;
                height -= foot * .015f;
            }

            for (int i = 0; i < Lanes.Length; i += 2)
            {
                float d = SegmentDistance(p, Lanes[i], Lanes[i + 1]) + (medium - .5f) * 1.2f;
                if (d > 2.2f) continue;
                float lane = (1f - Band(d, .3f, 1.9f)) * Band(fine, .2f, .55f);
                dust = Mathf.Max(dust, lane * .7f);
                grass *= 1f - lane * .8f;
                compact = Mathf.Max(compact, lane * .55f);
            }

            // Spawn pads: a ring of hard-packed pale earth where each fighter starts.
            for (int slot = 0; slot < 2; slot++)
            {
                var spawn = DuelArenaLayout.Spawn(slot);
                float d = Vector2.Distance(p, new Vector2(spawn.x, spawn.z)) + (fine - .5f) * .35f;
                if (d > 3f) continue;
                float pad = 1f - Band(d, 1.2f, 2.4f);
                float ring = (1f - Band(Mathf.Abs(d - 1.55f), .06f, .2f)) * .8f;
                dust = Mathf.Max(dust, pad * .85f);
                compact = Mathf.Max(compact, pad);
                grass *= 1f - pad;
                earth = Mathf.Max(earth, pad);
                damp = Mathf.Max(damp * (1f - pad), ring * .9f);
                height += -pad * .012f - ring * .01f;
            }

            foreach (var puddle in Puddles)
            {
                float d = Vector2.Distance(p, new Vector2(puddle.x, puddle.y)) / puddle.z + (fine - .5f) * .7f + (medium - .5f) * .4f;
                if (d > 3f) continue;
                height -= (1f - Band(d, .1f, 1.3f)) * .05f;
                wet = Mathf.Max(wet, (1f - Band(d, .1f, .75f)) * .85f, (1f - Band(d, .5f, 1.6f)) * .4f);
                damp = Mathf.Max(damp, (1f - Band(d, .8f, 2.6f)) * .5f);
                grass *= Band(d, .9f, 1.4f);
            }

            foreach (var scorch in Scorches)
            {
                float d = Vector2.Distance(p, new Vector2(scorch.x, scorch.y)) / scorch.z + (fine - .5f) * .6f;
                if (d > 1.6f) continue;
                ash = Mathf.Max(ash, (1f - Band(d, .35f, 1.05f)) * .9f);
                height += -(1f - Band(d, .1f, .6f)) * .05f + (1f - Band(Mathf.Abs(d - .75f), .05f, .35f)) * .02f;
            }

            // Fade to plain values before the texture edge clamps; the shader's meadow covers the seam.
            float keep = (1f - Band(Mathf.Abs(x), SplatHalfX - 4f, SplatHalfX - 1f)) * (1f - Band(Mathf.Abs(z), SplatHalfZ - 2f, SplatHalfZ - .5f));
            grass = Mathf.Lerp(.55f, grass, keep);
            damp = Mathf.Lerp(.25f, damp, keep);
            height = Mathf.Clamp(height * keep, -ReliefRange, ReliefRange);
            compact *= keep;
            float value = ProceduralArt.Fbm(x * .055f - 17f, z * .055f + 4f, 3) * .6f + ProceduralArt.Fbm(x * .16f + 9f, z * .16f + 27f, 2) * .4f;
            value = Mathf.Lerp(.5f, Band(value, .3f, .7f), keep);
            tone = new Color32((byte)(Mathf.Clamp01(dust * keep) * 255), (byte)(Mathf.Clamp01(damp) * 255),
                (byte)(Mathf.Clamp01(red * keep) * 255), (byte)(value * 255));
            return new Color32((byte)(Mathf.Clamp01(earth * keep) * 255), (byte)(Mathf.Clamp01(wet * keep) * 255),
                (byte)(Mathf.Clamp01(ash * keep) * 255), (byte)(Mathf.Clamp01(grass) * 255));
        }

        // ---------- Sandbags ----------

        /// <summary>Each cover once, with whether to also lay its half-turn copy (its mirror in the layout).</summary>
        private static IEnumerable<(Block block, bool mirrored)> CoverPairs()
        {
            var covers = DuelArenaLayout.Covers;
            var done = new bool[covers.Length];
            for (int i = 0; i < covers.Length; i++)
            {
                if (done[i]) continue;
                done[i] = true;
                int partner = -1;
                for (int j = i + 1; j < covers.Length; j++)
                    if (!done[j] && (covers[j].Center + covers[i].Center).sqrMagnitude < 1e-4f && (covers[j].Half - covers[i].Half).sqrMagnitude < 1e-4f
                        && Mathf.Abs(covers[j].Height - covers[i].Height) < 1e-4f) { partner = j; break; }
                if (partner >= 0) done[partner] = true;
                yield return (covers[i], partner >= 0);
            }
        }

        private static void Bag(PropBatch props, Matrix4x4 at, Vector3 size, bool bottom, bool mirrored)
        {
            string variant = Bags[random.Next(Bags.Length)] + (bottom ? "_ground" : "");
            var placed = at * Matrix4x4.Scale(new Vector3(size.x / BagLength, size.y / BagHeight, size.z / BagWidth));
            props.Add("sandbag", variant, placed);
            if (mirrored) props.Add("sandbag", variant, HalfTurn * placed);
        }

        /// <summary>
        /// Staggered courses of bags that fill the cover's box from the ground to its height: rows across its depth,
        /// alternate courses laid half a bag over, each bag scaled to its cell so the outer faces sit on the box faces.
        /// Enclosed bags are left out. What you see is what stops bullets.
        /// </summary>
        private static void Emplacement(PropBatch props, Block block, bool mirrored)
        {
            var size = new Vector3(block.Half.x * 2f, block.Height, block.Half.y * 2f);
            int courses = Mathf.Max(4, Mathf.RoundToInt(size.y / .19f));
            float course = size.y / courses;
            // Bags lie along the block's long side.
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, depth = alongX ? size.z : size.x;
            int rows = Mathf.Max(2, Mathf.RoundToInt(depth / BagWidth));
            float rowWidth = depth / rows;
            int bags = Mathf.Max(2, Mathf.RoundToInt(length / BagLength));
            float bagLength = length / bags;
            var frame = Matrix4x4.TRS(new Vector3(block.Center.x, 0f, block.Center.y), Quaternion.Euler(0, alongX ? 0f : 90f, 0), Vector3.one);
            props.Begin("Cover");
            for (int c = 0; c < courses; c++)
            {
                bool offset = c % 2 == 1, top = c == courses - 1;
                int count = bags + (offset ? 1 : 0);
                for (int row = 0; row < rows; row++)
                {
                    float across = -depth * .5f + rowWidth * (row + .5f);
                    for (int i = 0; i < count; i++)
                    {
                        float from = Mathf.Max(-length * .5f, -length * .5f + bagLength * (i - (offset ? .5f : 0f)));
                        float to = Mathf.Min(length * .5f, -length * .5f + bagLength * (i + 1 - (offset ? .5f : 0f)));
                        if (row > 0 && row < rows - 1 && i > 0 && i < count - 1 && !top) continue;
                        float yaw = (random.NextDouble() < .5 ? 180f : 0f) + ProceduralArt.Range(random, -3f, 3f);
                        var at = frame * Matrix4x4.TRS(new Vector3((from + to) * .5f, c * course - .012f, across), Quaternion.Euler(0, yaw, 0), Vector3.one);
                        Bag(props, at, new Vector3((to - from) * 1.04f, (course + .03f) * ProceduralArt.Range(random, .96f, 1f), rowWidth * 1.06f), c == 0, mirrored);
                    }
                }
            }
        }

        /// <summary>
        /// The edge of play: two courses of bags around the whole arena just outside the clamp, laid on the east and
        /// north sides and turned onto the west and south.
        /// </summary>
        private static void Berm(PropBatch props)
        {
            const float width = .44f, course = .27f;
            props.Begin("Berm");
            // East side runs past the corners so the north side butts against it.
            Wall(props, new Vector2(BermX + width * .5f, -BermZ - width), new Vector2(BermX + width * .5f, BermZ + width), width, course);
            Wall(props, new Vector2(-BermX, BermZ + width * .5f), new Vector2(BermX, BermZ + width * .5f), width, course);
        }

        private static void Wall(PropBatch props, Vector2 a, Vector2 b, float width, float course)
        {
            float yaw = -Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, total = Vector2.Distance(a, b);
            int count = Mathf.Max(1, Mathf.RoundToInt(total / .64f));
            float length = total / count;
            for (int c = 0; c < 2; c++)
            for (int k = 0; k < count + c; k++)
            {
                float from = Mathf.Max(0f, (k - c * .5f) * length), to = Mathf.Min(total, (k + 1 - c * .5f) * length);
                if (to - from < .05f) continue;
                Vector2 p = Vector2.Lerp(a, b, (from + to) * .5f / total);
                var at = Matrix4x4.TRS(new Vector3(p.x, c * course - .015f, p.y),
                    Quaternion.Euler(0, yaw + ProceduralArt.Range(random, -2.5f, 2.5f) + (random.NextDouble() < .5 ? 180f : 0f), 0), Vector3.one);
                Bag(props, at, new Vector3((to - from) * 1.05f, (course + .03f) * ProceduralArt.Range(random, .94f, 1f), width), c == 0, true);
            }
        }

        /// <summary>Each fighter's stores behind their spawn, past the berm: the same crates and drums, turned.</summary>
        private static void SpawnStores(PropBatch props)
        {
            var spawn = DuelArenaLayout.Spawn(0);
            float x = -BermX - 2.4f;
            props.Begin("Stores");
            void Store(string model, string part, float dx, float y, float dz, float yaw)
            {
                var at = Matrix4x4.TRS(new Vector3(x + dx, y, spawn.z + dz), Quaternion.Euler(0, yaw, 0), Vector3.one);
                if (props.Add(model, part, at)) props.Add(model, part, HalfTurn * at);
            }
            for (int i = 0; i < 3; i++) Store("crate", "crate", ProceduralArt.Range(random, -.03f, .03f), 0f, -1.9f + i * .41f, 90f + ProceduralArt.Range(random, -3f, 3f));
            for (int i = 0; i < 2; i++) Store("crate", "crate", .02f, .35f, -1.7f + i * .42f, ProceduralArt.Range(random, -3f, 3f));
            Store("crate", "crate", -.05f, .7f, -1.5f, 96f);
            Store("crate", "crate", -.3f, 0f, 1.5f, 12f);
            Store("drum", "drum", .4f, 0f, 2.4f, 0f);
            Store("drum", "drum", -.35f, 0f, 2.9f, 40f);
        }

        /// <summary>Bamboo stakes along the berm with a rope strung between them, so the edge reads from across the arena.</summary>
        private static void Stakes(Transform root)
        {
            var materials = StationMaterials.Create();
            if (materials == null) return;
            var mesh = new StationMesh();
            const float stakeHeight = 1.15f, ropeHeight = 1.02f;
            void Side(Vector2 a, Vector2 b)
            {
                int spans = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(a, b) / 6f));
                var tops = new List<Vector3>();
                for (int i = 0; i <= spans; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)spans);
                    float lean = ProceduralArt.Range(random, -.04f, .04f);
                    var foot = new Vector3(p.x, -.1f, p.y);
                    var top = new Vector3(p.x + lean, stakeHeight, p.y - lean);
                    foreach (var sign in new[] { 1f, -1f })
                        mesh.Tube(StationMaterial.BambooTan, new[] { Flip(foot, sign), Flip(top, sign) }, .035f, 8, 1.2f);
                    tops.Add(new Vector3(top.x, ropeHeight, top.z));
                }
                for (int i = 0; i + 1 < tops.Count; i++)
                {
                    // The rope sags a little between stakes.
                    Vector3 from = tops[i], to = tops[i + 1], middle = (from + to) * .5f + Vector3.down * .12f;
                    var path = new[] { from, Vector3.Lerp(from, middle, .5f) + Vector3.down * .03f, middle, Vector3.Lerp(middle, to, .5f) + Vector3.down * .03f, to };
                    foreach (var sign in new[] { 1f, -1f })
                    {
                        var turned = new Vector3[path.Length];
                        for (int k = 0; k < path.Length; k++) turned[k] = Flip(path[k], sign);
                        mesh.Tube(StationMaterial.Canvas, turned, .012f, 5, .5f, 2);
                    }
                }
            }
            float ox = BermX + .5f, oz = BermZ + .5f;
            Side(new Vector2(ox, -oz), new Vector2(ox, oz));
            Side(new Vector2(ox, oz), new Vector2(-ox, oz));
            mesh.Emit(root, "BoundaryStakes", materials);
        }

        /// <summary>
        /// Bamboo revetment on the face of each breastwork that looks back at its owner's spawn: posts with rails
        /// lashed between them, as a trench wall is held up from inside. Everything sits on the box face, flush with its
        /// top, so it changes what the cover looks like and never what it stops. Its own random stream, so the bags,
        /// stores and stakes laid from the shared one stay exactly where they were.
        /// </summary>
        private static void Revetments(Transform root)
        {
            var station = StationMaterials.Create();
            if (station == null) return;
            // Dry cut culms: the bags are the picture, so the bamboo is desaturated towards straw and matte. The green culm
            // texture needs a strongly warm tint to land on straw; any less and the shaded face goes grey-blue under the sky.
            Material Straw(StationMaterial source, string name, Color tint, float saturation)
            {
                var material = new Material(station[(int)source]) { name = name };
                material.SetColor("_BaseColor", tint);
                material.SetFloat("_Saturation", saturation);
                material.SetFloat("_TintVariation", 0f);
                material.SetFloat("_Smoothness", .05f);
                material.SetFloat("_Occlusion", 1f);
                return material;
            }
            // A stack of cut culms is never one flat tone. EnvLeaf's tint variation is per object (one value for this
            // whole mesh), so each culm instead picks one of a few copies a few percent apart in value and warmth.
            Material Shade(StationMaterial source, string name, Color tint, float saturation, float value, float warmth) =>
                Straw(source, name, new Color(tint.r * value * (1f + warmth), tint.g * value, tint.b * value * (1f - warmth)), saturation);
            var tan = new Color(1.45f, 1.08f, .56f);
            var grey = new Color(1.32f, 1.04f, .66f);
            var materials = new[]
            {
                Shade(StationMaterial.BambooTan, "RevetmentPost", tan, .5f, 1f, 0f),
                Shade(StationMaterial.BambooTan, "RevetmentPostLight", tan, .5f, 1.07f, -.02f),
                Shade(StationMaterial.BambooTan, "RevetmentPostWarm", tan, .52f, .93f, .04f),
                Shade(StationMaterial.BambooGrey, "RevetmentPostGrey", grey, .38f, 1f, 0f),
                Shade(StationMaterial.BambooGrey, "RevetmentPostGreyDark", grey, .38f, .9f, .02f),
                Shade(StationMaterial.BambooTan, "RevetmentRail", tan, .5f, .97f, .01f),
                Shade(StationMaterial.BambooTan, "RevetmentRailDark", tan, .5f, .9f, .03f)
            };
            var rng = new System.Random(1981);
            var tubes = new RevetmentMesh(materials.Length);
            var shade = new RevetmentMesh(1);
            const float post = .045f, rail = .03f, spacing = .8f;
            foreach (var block in DuelArenaLayout.Covers)
            {
                bool rails;
                switch (block.Kind)
                {
                    case DuelCoverKind.Parapet: case DuelCoverKind.Traverse: case DuelCoverKind.GunPit: rails = true; break;
                    case DuelCoverKind.Sap: case DuelCoverKind.Dugout: rails = false; break;
                    default: continue;
                }
                // The face toward the owner's spawn, across the block's short side: west blocks are slot 0's, east slot 1's.
                var spawn = DuelArenaLayout.Spawn(block.Center.x < 0 ? 0 : 1);
                bool alongX = block.Half.x >= block.Half.y;
                Vector3 normal = alongX ? new Vector3(0, 0, Mathf.Sign(spawn.z - block.Center.y)) : new Vector3(Mathf.Sign(spawn.x - block.Center.x), 0, 0);
                if (normal.sqrMagnitude < .5f) normal = alongX ? Vector3.back : Vector3.left;
                Vector3 along = alongX ? Vector3.right : Vector3.forward;
                float half = alongX ? block.Half.x : block.Half.y, depth = alongX ? block.Half.y : block.Half.x;
                // Posts and rails stand in the box face, at most 1.5 cm proud of it (the bags already bulge that far).
                var face = new Vector3(block.Center.x, 0, block.Center.y) + normal * (depth + .015f);
                float top = block.Height - .01f;
                int posts = Mathf.Max(2, Mathf.RoundToInt(half * 2f / spacing) + 1);
                for (int i = 0; i < posts; i++)
                {
                    float t = -half + .06f + (half * 2f - .12f) * i / (posts - 1);
                    if (i > 0 && i < posts - 1) t += ProceduralArt.Range(rng, -.06f, .06f);
                    // Culms differ in thickness; each stays flush with the face whatever its radius.
                    float radius = post * ProceduralArt.Range(rng, .85f, 1.15f);
                    var center = face - normal * radius;
                    var foot = center + along * t + Vector3.down * .08f;
                    var head = center + along * (t + ProceduralArt.Range(rng, -.015f, .015f)) + Vector3.up * top;
                    int culm = rng.NextDouble() < .3 ? 3 + rng.Next(2) : rng.Next(3);
                    tubes.Tube(culm, foot, head, head, radius, 8, 1.2f, normal);
                    // The bags darken beside the post, where it presses into them; the band floats just off the bags.
                    shade.Strip(face + along * t, along, Vector3.up, radius + .012f, .13f, -.02f, top, normal * .005f);
                }
                if (rails)
                    foreach (float y in new[] { top - .06f, block.Height * .45f })
                    {
                        float radius = rail * ProceduralArt.Range(rng, .85f, 1.15f);
                        var railCenter = face - normal * radius + Vector3.up * y;
                        var a = railCenter - along * (half - .02f);
                        var b = railCenter + along * (half - .02f);
                        var sag = (a + b) * .5f + Vector3.down * ProceduralArt.Range(rng, .005f, .015f);
                        tubes.Tube(5 + rng.Next(2), a, sag, b, radius, 7, 1.4f, normal);
                        shade.Strip(face + Vector3.up * y, Vector3.up, along, radius + .01f, .08f, -(half - .02f), half - .02f, normal * .005f);
                    }
            }
            tubes.Emit(root, "Revetments", materials, ShadowCastingMode.On);
            var multiply = StationMaterials.GroundShade();
            if (multiply != null) shade.Emit(root, "RevetmentContact", new[] { multiply }, ShadowCastingMode.Off);
        }

        /// <summary>
        /// The revetment's own geometry: bamboo tubes whose vertex alpha (EnvLeaf occlusion) darkens the side pressed
        /// against the bags and the foot, and soft multiply bands (GroundShade: RGB the shade, A its strength) on the
        /// bag face behind each post and rail.
        /// </summary>
        private sealed class RevetmentMesh
        {
            private readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Color> colors = new List<Color>();
            private readonly List<int>[] triangles;

            public RevetmentMesh(int materials)
            {
                triangles = new List<int>[materials];
                for (int i = 0; i < materials; i++) triangles[i] = new List<int>();
            }

            /// <summary>A round tube from a through b to c (straight when b == c), its back toward -<paramref name="facing"/>.</summary>
            public void Tube(int material, Vector3 a, Vector3 b, Vector3 c, float radius, int sides, float tile, Vector3 facing)
            {
                const int rings = 5;
                var points = new Vector3[rings];
                for (int i = 0; i < rings; i++)
                {
                    float t = i / (rings - 1f);
                    points[i] = Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);
                }
                Vector3 axis = (points[rings - 1] - points[0]).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
                Vector3 up = Vector3.Cross(side, axis);
                float ground = Mathf.Min(a.y, c.y), along = 0;
                int start = vertices.Count;
                for (int i = 0; i < rings; i++)
                {
                    if (i > 0) along += Vector3.Distance(points[i - 1], points[i]);
                    for (int j = 0; j <= sides; j++)
                    {
                        float angle = j * Mathf.PI * 2f / sides;
                        Vector3 n = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                        vertices.Add(points[i] + n * radius);
                        normals.Add(n);
                        uvs.Add(new Vector2((float)j / sides, along / tile));
                        // Pressed against the bags: dark on the back, half dark at the sides; and darker into the earth.
                        float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.3f, 1f, -Vector3.Dot(n, facing)));
                        float foot = Mathf.Lerp(.55f, 1f, Mathf.Clamp01((points[i].y - ground) / .4f));
                        colors.Add(new Color(0, 0, 0, (1f - .6f * back) * foot));
                    }
                }
                var list = triangles[material];
                int ring = sides + 1;
                for (int i = 0; i < rings - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int p = start + i * ring + j, q = p + ring;
                        list.Add(p); list.Add(q); list.Add(p + 1);
                        list.Add(p + 1); list.Add(q); list.Add(q + 1);
                    }
            }

            /// <summary>
            /// A soft shade band either side of the line from origin + length * from to origin + length * to: open for
            /// <paramref name="gap"/> each side, so it never covers the culm on the line, darkest at the gap's edge and
            /// gone at <paramref name="width"/>.
            /// </summary>
            public void Strip(Vector3 origin, Vector3 across, Vector3 length, float gap, float width, float from, float to, Vector3 lift)
            {
                var tint = new Color(.5f, .45f, .4f);
                float[] offsets = { -width, -(gap + .035f), -gap, gap, gap + .035f, width };
                float[] strengths = { 0f, .35f, .55f, .55f, .35f, 0f };
                int start = vertices.Count, columns = offsets.Length;
                foreach (float l in new[] { from, to })
                    for (int k = 0; k < columns; k++)
                    {
                        vertices.Add(origin + lift + length * l + across * offsets[k]);
                        normals.Add(lift.normalized);
                        uvs.Add(new Vector2(offsets[k], l));
                        colors.Add(new Color(tint.r, tint.g, tint.b, strengths[k]));
                    }
                var list = triangles[0];
                for (int k = 0; k < columns - 1; k++)
                {
                    if (k == columns / 2 - 1) continue;
                    int p = start + k, q = p + columns;
                    // Both windings, so the band shows from either side.
                    list.Add(p); list.Add(q); list.Add(p + 1);
                    list.Add(p + 1); list.Add(q); list.Add(q + 1);
                    list.Add(p); list.Add(p + 1); list.Add(q);
                    list.Add(p + 1); list.Add(q + 1); list.Add(q);
                }
            }

            public void Emit(Transform parent, string name, Material[] materials, ShadowCastingMode shadows)
            {
                if (vertices.Count == 0) return;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, subMeshCount = triangles.Length };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colors);
                for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(triangles[s], s, false);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                mesh.UploadMeshData(true);
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = shadows;
            }
        }

        private static Vector3 Flip(Vector3 p, float sign) => sign > 0 ? p : new Vector3(-p.x, p.y, -p.z);
    }
}
