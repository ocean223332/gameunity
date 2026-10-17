using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// What lives in the river valley: the karst falls (falling sheets, the pool on the ledge, plunge foam and spray),
    /// the mist lying on the river, the stilt-house hamlet on the north bank with smoke from its burnt houses, and the
    /// rope bridge below the falls. Presentation only, no colliders. Everything is placed on EnvironmentLayout's
    /// ground, so it follows the river constants.
    /// </summary>
    internal static class EnvironmentRiverlands
    {
        private struct Fall
        {
            public float X, Speed, Width;
            public Fall(float x, float speed, float width) { X = x; Speed = speed; Width = width; }
        }

        /// <summary>The main fall (two tiers, wide) and its slender twin off the tower: lip speed (m/s) and width (m).</summary>
        private static readonly Fall[] Falls = { new Fall(L.FallX, 3f, 24f), new Fall(L.TwinFallX, 3.5f, 7f) };

        internal const float BridgeX = -86f;

        public static void Build(Transform root, Material riverWater)
        {
            var parent = new GameObject("Riverlands").transform;
            parent.SetParent(root, false);
            var mist = new Billboards();
            var waterfall = ProceduralArt.Environment("Waterfall", "Env_Waterfall");
            var foam = Resources.Load<Texture2D>("Environment/Water/WaterfallFoam");
            if (waterfall != null && foam != null) waterfall.SetTexture("_FoamTex", foam);
            foreach (var fall in Falls) BuildFall(parent, fall, waterfall, riverWater, mist);
            RiverMist(mist);
            var props = new FlatMeshBuilder();
            BuildHamlet(props, mist);
            BuildBridge(props);
            props.Emit(parent, "Riverlands", new[]
            {
                ProceduralArt.Lit("Env_HamletWood", ProceduralArt.Hex("#5E4630"), .1f),
                ProceduralArt.Lit("Env_HamletThatch", ProceduralArt.Hex("#8F7748"), .05f, true),
                ProceduralArt.CharredBark("Env_HamletCharred")
            }, false);
            var mistMaterial = ProceduralArt.Environment("Mist", "Env_Mist");
            var puff = Resources.Load<Texture2D>("Environment/Water/MistPuff");
            if (mistMaterial != null && puff != null) mistMaterial.SetTexture("_PuffTex", puff);
            if (mistMaterial != null) mist.Emit(parent, "Env_ValleyMist", mistMaterial);
        }

        // ---------- River geometry ----------

        /// <summary>The river's centreline z at x (its line runs west to east).</summary>
        internal static float RiverZ(float x)
        {
            var line = L.River;
            for (int i = 0; i < line.Length - 1; i++)
                if (x <= line[i + 1].x) return Mathf.Lerp(line[i].y, line[i + 1].y, Mathf.InverseLerp(line[i].x, line[i + 1].x, x));
            return line[line.Length - 1].y;
        }

        /// <summary>Horizontal unit direction towards the river from a point on its south bank (up the signed distance).</summary>
        private static Vector3 TowardRiver(float x, float z)
        {
            float gx = L.RiverSide(x + 1f, z) - L.RiverSide(x - 1f, z), gz = L.RiverSide(x, z + 1f) - L.RiverSide(x, z - 1f);
            return new Vector3(gx, 0, gz).normalized;
        }

        // ---------- Karst face ----------

        // The terrain's 2 m grid can only draw the wall as one smooth slope, and it cannot overhang. The face skin is a
        // mesh standing just proud of it: per column x and row height y, the point where the ground first reaches y going
        // south from the river, pushed toward the river by the relief: buttresses, deep fluting, tilted ledges with
        // overhung undersides and flat tops, alcoves. Rough limestone blocks break the crest above it. Both use the
        // ground's material (the wall paint keys on position and normal), with their occlusion baked into uv0.x.
        private const float FaceWest = L.KarstWest, FaceEast = -40f, FaceDx = 1f, FaceDy = .5f, ScanStep = .25f;
        private const int ScanSamples = 180;
        private const float LedgeSpacing = 11f;

        private sealed class FaceTable
        {
            public int Columns, Rows;
            public float Y0;
            public float[] Back;   // [column * Rows + row]: how far south of RiverZ the ground first reaches the row's height
            public float[] Top;    // per column: the face's top edge (m)
        }

        private static FaceTable faceTable;
        private static List<(Vector3, Vector3, float, float)> ledges;

        /// <summary>
        /// The ledges of the karst face, for hanging plants: lip is the real top of the skin at the ledge's front edge,
        /// outward the horizontal unit toward the river, drop the free height below the lip (to the next ledge or the
        /// water), shelf how far back from the lip its top stays flat (m). The tops of the crest blocks are in it too.
        /// A pure function of the layout: no shared Random, the same list every run.
        /// </summary>
        internal static IReadOnlyList<(Vector3 lip, Vector3 outward, float drop, float shelf)> KarstLedges => ledges ??= FindLedges();

        private static float Ease(float from, float to, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, t));
        private static float Noise(float x, float y) => ProceduralArt.Fbm(x, y, 2);

        /// <summary>Where the skin shows (1) or sinks into the terrain (0): smooth behind the falls' sheets and at the bridge's cleft, faded at both ends.</summary>
        private static float FaceMask(float x) =>
            Mathf.Min(Ease(13f, 20f, Mathf.Abs(x - L.FallX)), Ease(5f, 9f, Mathf.Abs(x - L.TwinFallX))) * Ease(4f, 8f, Mathf.Abs(x - BridgeX))
            * Ease(FaceWest, FaceWest + 15f, x) * (1f - Ease(FaceEast - 20f, FaceEast - 5f, x));

        /// <summary>Which ledge band a height h over the river falls in: bands about 11 m apart, tilted and wandering along x.</summary>
        private static float LedgeBand(float x, float h) =>
            h / LedgeSpacing + 1.1f * (Noise(x * .009f + 2f, h * .012f + 9.3f) - .5f) + .25f * (Noise(x * .04f + 6f, h * .03f) - .5f);

        /// <summary>How far ledge band b juts out at x (m): in runs along the face, 0 between them.</summary>
        private static float LedgeDepth(float x, float band) =>
            3.4f * Ease(.45f, .68f, Noise(x * .025f + band * 3.1f, band * 1.7f + 4f)) * (.6f + .8f * Noise(x * .05f + band, 2.2f));

        private static float Alcove(float x, float h) => Ease(.6f, .74f, Noise(x * .035f + 13f, h * .05f + 2f)) * Ease(5f, 10f, h);

        /// <summary>How far the skin stands toward the river from the ground's face at x, height y (m): 1.2 m plus the relief, or 1 m inside the ground where it fades out.</summary>
        private static float Relief(float x, float y)
        {
            float mask = FaceMask(x);
            if (mask <= 0) return -1f;
            float h = y - L.RiverLevel;
            float wavelength = 5f + 2.5f * Noise(x * .02f + 1.3f, 3.1f);
            float warp = 3f * (Noise(x * .01f + 5f, h * .03f) - .5f);
            float rib = 1f - Mathf.Abs(2f * Mathf.Repeat((x + warp) / wavelength, 1f) - 1f);
            rib = rib * rib * (3f - 2f * rib);
            float flutes = 3f * rib * Ease(4f, 12f, h) * (.45f + .55f * Noise(x * .03f + 4f, h * .02f));
            float buttress = 5f * Ease(.38f, .66f, Noise(x * .012f + 7f, h * .004f));
            float t = LedgeBand(x, h), band = Mathf.Floor(t), f = t - band;
            float ledge = LedgeDepth(x, band) * Ease(.62f, .96f, f) * (1f - Ease(.965f, 1f, f)) * Ease(6f, 10f, h);
            float rest = (buttress + flutes + ledge) * (1f - .85f * Alcove(x, h)) * Ease(1.5f, 5f, h);
            return -1f + mask * (2.2f + rest);
        }

        private static FaceTable Face()
        {
            if (faceTable != null) return faceTable;
            int columns = Mathf.RoundToInt((FaceEast - FaceWest) / FaceDx) + 1;
            float y0 = EnvironmentGround.RiverSurface - .8f, highest = y0;
            var starts = new float[columns];
            var tops = new float[columns];
            var runs = new float[columns][];
            for (int i = 0; i < columns; i++)
            {
                float x = FaceWest + i * FaceDx, zr = RiverZ(x), start = L.KarstFoot(x) - .5f, highestSoFar = float.MinValue;
                var run = runs[i] = new float[ScanSamples];
                for (int k = 0; k < ScanSamples; k++) run[k] = highestSoFar = Mathf.Max(highestSoFar, L.GroundHeight(x, zr - start - k * ScanStep));
                starts[i] = start;
                tops[i] = Mathf.Min(L.KarstCrest(x), run[ScanSamples - 1]) - .3f;
                highest = Mathf.Max(highest, tops[i]);
            }
            int rows = Mathf.CeilToInt((highest - y0) / FaceDy) + 2;
            var back = new float[columns * rows];
            for (int i = 0; i < columns; i++)
            {
                var run = runs[i];
                for (int j = 0, k = 0; j < rows; j++)
                {
                    float y = Mathf.Min(y0 + j * FaceDy, tops[i]);
                    while (k < ScanSamples - 1 && run[k] < y) k++;
                    float s = k == 0 || run[k] < y ? k : k - 1 + Mathf.InverseLerp(run[k - 1], run[k], y);
                    back[i * rows + j] = starts[i] + s * ScanStep;
                }
            }
            return faceTable = new FaceTable { Columns = columns, Rows = rows, Y0 = y0, Back = back, Top = tops };
        }

        /// <summary>The skin's point at column i, height y (clamped to the column's top).</summary>
        private static Vector3 FacePoint(FaceTable table, int i, float y)
        {
            float x = FaceWest + i * FaceDx;
            y = Mathf.Min(y, table.Top[i]);
            float fj = Mathf.Clamp((y - table.Y0) / FaceDy, 0, table.Rows - 1.001f);
            int j = (int)fj;
            float back = Mathf.Lerp(table.Back[i * table.Rows + j], table.Back[i * table.Rows + j + 1], fj - j);
            return new Vector3(x, y, RiverZ(x) - back + Relief(x, y));
        }

        /// <summary>The face skin: one row per 0.5 m up to each column's top, then folded back under the plateau's edge.</summary>
        private static void BuildFace(Transform parent, Material skin)
        {
            var table = Face();
            int columns = table.Columns, rows = table.Rows;
            var positions = new Vector3[columns * rows];
            var reliefs = new float[columns * rows];
            var lastRow = new int[columns];
            for (int i = 0; i < columns; i++)
            {
                float x = FaceWest + i * FaceDx;
                lastRow[i] = Mathf.Clamp(Mathf.FloorToInt((table.Top[i] - table.Y0) / FaceDy) + 1, 1, rows - 1);
                for (int j = 0; j < rows; j++)
                {
                    int v = i * rows + j;
                    if (j < lastRow[i])
                    {
                        positions[v] = FacePoint(table, i, table.Y0 + j * FaceDy);
                        reliefs[v] = Relief(x, positions[v].y);
                    }
                    else
                    {
                        // Past the top: back under the plateau, which stands at least 0.3 m higher, so the fold never shows.
                        float top = table.Top[i];
                        positions[v] = new Vector3(x, top, RiverZ(x) - table.Back[i * rows + rows - 1] - 4f);
                        reliefs[v] = -1f;
                    }
                }
            }
            // Normals across the grid, so ledge tops face up and their undersides down.
            var normals = new Vector3[positions.Length];
            var occlusion = new Vector2[positions.Length];
            for (int i = 0; i < columns; i++)
            for (int j = 0; j < rows; j++)
            {
                int v = i * rows + j;
                var along = positions[Mathf.Min(i + 1, columns - 1) * rows + j] - positions[Mathf.Max(i - 1, 0) * rows + j];
                var up = positions[i * rows + Mathf.Min(j + 1, rows - 1)] - positions[i * rows + Mathf.Max(j - 1, 0)];
                var n = Vector3.Cross(along, up);
                normals[v] = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up;
                occlusion[v] = new Vector2(FaceOcclusion(reliefs, rows, columns, i, j, lastRow[i]), 0);
            }
            var triangles = new List<int>(columns * rows * 6);
            for (int i = 0; i < columns - 1; i++)
            {
                int reach = Mathf.Max(lastRow[i], lastRow[i + 1]);
                for (int j = 0; j < reach && j < rows - 1; j++)
                {
                    int a = i * rows + j, b = a + rows;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(b); triangles.Add(b + 1); triangles.Add(a + 1);
                }
            }
            var mesh = new Mesh { name = "Env_KarstFace", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, occlusion);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EnvironmentGround.Spawn(parent, "KarstFace", mesh, skin, false);
        }

        /// <summary>
        /// Baked occlusion (0..1) of a skin vertex: darker in recesses below their surroundings and under the ledges and
        /// buttresses that jut out above it.
        /// </summary>
        private static float FaceOcclusion(float[] reliefs, int rows, int columns, int i, int j, int lastRow)
        {
            if (j >= lastRow) return 1f;
            float own = reliefs[i * rows + j], over = 0, around = 0;
            int count = 0;
            for (int dj = 1; dj <= 6 && j + dj < lastRow; dj++) over = Mathf.Max(over, reliefs[i * rows + j + dj] - own - .15f * dj);
            for (int di = -3; di <= 3; di++)
            for (int dj = -4; dj <= 4; dj += 2)
            {
                int ci = Mathf.Clamp(i + di, 0, columns - 1), cj = Mathf.Clamp(j + dj, 0, lastRow - 1);
                around += reliefs[ci * rows + cj];
                count++;
            }
            float recess = Mathf.Max(0, around / count - own);
            return Mathf.Clamp01(1f - .5f * Mathf.Clamp01(over / 2.2f) - .3f * Mathf.Clamp01(recess / 2f));
        }

        /// <summary>
        /// The crest: rough limestone blocks on the face's top edge, 3 to 14 m high and fluted, leaning a little, with
        /// gaps between them (notches) and none over the falls, so the skyline breaks instead of running smooth.
        /// </summary>
        private static List<(Vector3 centre, float radiusX, float radiusZ, float height, float lean, int seed)> CrestBlocks()
        {
            var table = Face();
            var blocks = new List<(Vector3, float, float, float, float, int)>();
            int cell = 0;
            for (float x = FaceWest + 6f; x < FaceEast - 12f; cell++)
            {
                float h1 = Hash(cell, 1), h2 = Hash(cell, 2), h3 = Hash(cell, 3);
                float width = 6f + 10f * h1;
                float bx = x + width * .5f;
                x += width + (h2 < .3f ? 4f + 8f * h3 : 0f);   // every third or so leaves a notch
                if (h2 < .12f || FaceMask(bx) < .6f) continue;
                int i = Mathf.Clamp(Mathf.RoundToInt((bx - FaceWest) / FaceDx), 0, table.Columns - 1);
                float top = table.Top[i];
                var lip = FacePoint(table, i, top - .2f);
                float radiusX = width * .5f * (.8f + .3f * h3), radiusZ = 2.5f + 4f * Hash(cell, 4);
                float height = (3f + 11f * h1 * h3 + 3f * Hash(cell, 5)) * (h2 > .85f ? .5f : 1f);
                var centre = new Vector3(bx, top - 3f, lip.z - radiusZ * .7f);
                blocks.Add((centre, radiusX, radiusZ, height + 3f, (Hash(cell, 6) - .5f) * .25f, cell));
            }
            return blocks;
        }

        private static float Hash(int i, int salt)
        {
            uint h = unchecked((uint)i * 73856093u ^ (uint)salt * 83492791u ^ 0x9e3779b9u);
            h ^= h >> 13;
            h = unchecked(h * 0x5bd1e995u);
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }

        private static void BuildCrest(Transform parent, Material skin)
        {
            const int sides = 11, rings = 7;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            foreach (var (centre, radiusX, radiusZ, height, lean, seed) in CrestBlocks())
            {
                int first = vertices.Count;
                float flutes = 5f + 4f * Hash(seed, 7);
                for (int k = 0; k < rings; k++)
                {
                    float f = k / (rings - 1f), y = centre.y + height * f;
                    for (int s = 0; s < sides; s++)
                    {
                        float angle = s * 2f * Mathf.PI / sides;
                        float rough = .8f + .4f * Noise(seed * 3.7f + Mathf.Cos(angle) * 1.3f, Mathf.Sin(angle) * 1.3f + f * 1.6f);
                        float flute = 1f - .12f * Mathf.Abs(Mathf.Sin(angle * flutes * .5f + seed));
                        float shrink = 1f - .3f * f * f;
                        var p = new Vector3(Mathf.Cos(angle) * radiusX, 0, Mathf.Sin(angle) * radiusZ) * (rough * flute * shrink);
                        vertices.Add(new Vector3(centre.x + p.x + lean * height * f, y, centre.z + p.z));
                        uv.Add(new Vector2(Mathf.Lerp(.45f, 1f, Mathf.Clamp01(f * 2.5f)), 0));
                    }
                }
                // A low, uneven crown: flat enough to hold soil and plants.
                int crown = vertices.Count;
                vertices.Add(new Vector3(centre.x + lean * height, centre.y + height + .25f * Mathf.Min(radiusX, radiusZ), centre.z));
                uv.Add(new Vector2(1f, 0));
                for (int k = 0; k < rings - 1; k++)
                for (int s = 0; s < sides; s++)
                {
                    int a = first + k * sides + s, b = first + k * sides + (s + 1) % sides;
                    triangles.Add(a); triangles.Add(a + sides); triangles.Add(b);
                    triangles.Add(b); triangles.Add(a + sides); triangles.Add(b + sides);
                }
                int topRing = first + (rings - 1) * sides;
                for (int s = 0; s < sides; s++) { triangles.Add(crown); triangles.Add(topRing + (s + 1) % sides); triangles.Add(topRing + s); }
            }
            if (triangles.Count == 0) return;
            var mesh = new Mesh { name = "Env_KarstCrest" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EnvironmentGround.Spawn(parent, "KarstCrest", mesh, skin, false);
        }

        /// <summary>The face skin and the crest, on a copy of the ground's material that reads the baked occlusion.</summary>
        public static void BuildKarst(Transform root, Material ground)
        {
            if (ground == null || !ground.HasProperty("_SkinAO")) return;
            var skin = new Material(ground) { name = "Env_KarstSkin" };
            skin.SetFloat("_SkinAO", 1f);
            var parent = new GameObject("Karst").transform;
            parent.SetParent(root, false);
            BuildFace(parent, skin);
            BuildCrest(parent, skin);
        }

        private static List<(Vector3, Vector3, float, float)> FindLedges()
        {
            var table = Face();
            var found = new List<(Vector3, Vector3, float, float)>();
            var heights = new List<float>();
            for (int i = 2; i < table.Columns - 2; i += 3)
            {
                float x = FaceWest + i * FaceDx;
                if (FaceMask(x) < .5f) continue;
                float lowest = EnvironmentGround.RiverSurface;
                int top = Mathf.CeilToInt((table.Top[i] - L.RiverLevel) / LedgeSpacing) + 2;
                for (int band = 0; band <= top; band++)
                {
                    // The top of the band's ledge, where its profile turns from the flat top to the face above (f = 0.965).
                    float h = (band + .965f) * LedgeSpacing;
                    for (int it = 0; it < 4; it++) h += (band + .965f - LedgeBand(x, h)) * LedgeSpacing;
                    float y = L.RiverLevel + h;
                    if (h < 10f || y > table.Top[i] - 1f) continue;
                    float depth = LedgeDepth(x, band) * (1f - .85f * Alcove(x, h)) * FaceMask(x);
                    if (depth < 1.2f) continue;
                    var lip = FacePoint(table, i, y);
                    var outward = TowardRiver(x, lip.z - 1f);
                    found.Add((lip, outward, y - lowest, depth * .85f));
                    lowest = y;
                    heights.Add(y);
                }
                FaceLips(table, i, heights, found);
                heights.Clear();
            }
            foreach (var (centre, radiusX, radiusZ, height, lean, _) in CrestBlocks())
                found.Add((new Vector3(centre.x + lean * height, centre.y + height, centre.z + radiusZ * .6f), TowardRiver(centre.x, centre.z), height - 3f, Mathf.Min(radiusX, radiusZ)));
            return found;
        }

        /// <summary>
        /// Lips without a ledge, so plants drape the smooth faces and hang from the top edge too: going down column i
        /// from just under its top, every point at least 3.5 m from a ledge or another lip where the skin below bulges
        /// out by at most 0.6 m over the next 2 m. The lip is pushed out past that bulge, so a curtain hung there clears
        /// the rounded domes as well as the receding faces. Its drop runs down to where the skin bulges out further, or to
        /// the next lip; no shelf.
        /// </summary>
        private static void FaceLips(FaceTable table, int i, List<float> ledgeHeights, List<(Vector3, Vector3, float, float)> found)
        {
            const float step = .5f, gap = 3.5f, clear = 2f, bulgeMax = .6f;
            float floor = L.RiverLevel + 8f;
            var taken = new List<float>(ledgeHeights);
            for (float y = table.Top[i] - .5f; y > floor + 4f; y -= step)
            {
                bool near = false;
                foreach (float t in taken) near |= Mathf.Abs(t - y) < gap;
                if (near) continue;
                var lip = FacePoint(table, i, y);
                float bulge = 0f;
                for (float d = step; d <= clear; d += step) bulge = Mathf.Max(bulge, FacePoint(table, i, y - d).z - lip.z);
                if (bulge > bulgeMax) continue;
                lip.z += bulge;
                float bottom = y - clear;
                while (bottom > floor && FacePoint(table, i, bottom - step).z <= lip.z + .25f)
                {
                    bool hit = false;
                    foreach (float t in taken) hit |= t < y && t > bottom - step - .5f;
                    if (hit) break;
                    bottom -= step;
                }
                found.Add((lip, TowardRiver(lip.x, lip.z - 1f), y - bottom, 0f));
                taken.Add(y);
            }
        }

        // ---------- Falls ----------

        private struct Drop
        {
            public Vector3 Position;
            public float Time;
        }

        /// <summary>
        /// The water's path: from the notch in the crest it leaves at the fall's speed and drops under gravity; where it
        /// lands on a ledge it pools, runs to the ledge's edge and drops again, until it reaches the river. Returns the
        /// tiers, each a run of samples about 1.5 m apart, and the pools on the ledges.
        /// </summary>
        private static List<List<Drop>> FallPath(Fall fall, out Vector3 toward, List<Vector3> pools)
        {
            float x = fall.X, crest = L.KarstCrest(x), z = RiverZ(x);
            for (int i = 0; i < 400 && L.GroundHeight(x, z) < crest - .6f; i++) z -= .25f;
            toward = TowardRiver(x, z);
            var pos = new Vector3(x, L.GroundHeight(x, z) + .3f, z) + toward * .4f;
            var velocity = toward * fall.Speed;
            var tiers = new List<List<Drop>>();
            var tier = new List<Drop> { new Drop { Position = pos } };
            float time = 0;
            const float dt = .04f;
            for (int step = 0; step < 400 && pos.y > EnvironmentGround.RiverSurface; step++)
            {
                velocity.y -= 9.81f * dt;
                pos += velocity * dt;
                time += dt;
                float ground = L.GroundHeight(pos.x, pos.z);
                if (pos.y < ground + .3f && ground > L.RiverLevel + 3f)
                {
                    float rise = L.GroundHeight(pos.x - toward.x, pos.z - toward.z) - L.GroundHeight(pos.x + toward.x, pos.z + toward.z);
                    if (rise > 3f)
                    {
                        // Grazing the wall, not landing: the water is pushed off it.
                        pos += toward * .3f;
                        pos.y = Mathf.Max(pos.y, ground + .3f);
                        continue;
                    }
                    // A ledge: the water pools, runs to the edge and drops again.
                    tier.Add(new Drop { Position = new Vector3(pos.x, ground + .3f, pos.z), Time = time });
                    tiers.Add(tier);
                    pools.Add(new Vector3(pos.x, ground, pos.z));
                    Vector3 edge = pos;
                    for (int i = 0; i < 200 && L.GroundHeight(edge.x, edge.z) > ground - 1.5f; i++) edge += toward * .25f;
                    pos = new Vector3(edge.x, ground + .2f, edge.z) - toward * .5f;
                    velocity = toward * 2.5f;
                    time = 0;
                    tier = new List<Drop> { new Drop { Position = pos } };
                    continue;
                }
                if (Vector3.Distance(pos, tier[tier.Count - 1].Position) >= 1.5f) tier.Add(new Drop { Position = pos, Time = time });
            }
            pos.y = EnvironmentGround.RiverSurface;
            tier.Add(new Drop { Position = pos, Time = time });
            tiers.Add(tier);
            return tiers;
        }

        private static void BuildFall(Transform parent, Fall fall, Material waterfall, Material riverWater, Billboards mist)
        {
            var pools = new List<Vector3>();
            var tiers = FallPath(fall, out var toward, pools);
            var across = Vector3.Cross(Vector3.up, toward).normalized;
            float total = Mathf.Max(1f, tiers[0][0].Position.y - EnvironmentGround.RiverSurface);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var data = new List<Vector4>();
            var triangles = new List<int>();
            var random = new System.Random(Mathf.RoundToInt(fall.X));
            for (int t = 0; t < tiers.Count; t++)
            {
                var tier = tiers[t];
                float width = fall.Width * (t == 0 ? 1f : 1.2f);
                // The sheet, then a wider, fainter veil of spray just in front of it.
                for (int layer = 0; layer < 2; layer++)
                    Sheet(tier, toward, toward * (layer * .9f), across, width * (layer == 0 ? 1f : 1.2f), tiers[0][0].Position.y, total, layer,
                        vertices, normals, uv, data, triangles);
                var landing = tier[tier.Count - 1].Position;
                bool river = t == tiers.Count - 1;
                float drop = tier[0].Position.y - landing.y;
                // Foam where it lands, and spray rising off it: the hero's backlit cloud at the foot of the main fall.
                Foam(landing + Vector3.up * .05f, width * (river ? .9f : .6f), vertices, normals, uv, data, triangles);
                int puffs = Mathf.RoundToInt(Mathf.Clamp(drop * (river ? .3f : .18f) * width / 16f, 4f, 16f));
                for (int i = 0; i < puffs; i++)
                {
                    float k = (float)i / puffs;
                    float size = Mathf.Lerp(width * .5f, width * 1.15f, k) * ProceduralArt.Range(random, .8f, 1.2f);
                    var centre = landing + across * ProceduralArt.Range(random, -.5f, .5f) * width + toward * ProceduralArt.Range(random, 1f, 6f)
                                 + Vector3.up * (size * .3f + k * drop * .35f);
                    mist.Add(centre, size, size * ProceduralArt.Range(random, .7f, 1f), Mathf.Lerp(.55f, .15f, k), random, 0);
                }
                // A faint glow of spray down the face of the curtain.
                for (int i = 1; i < 4; i++)
                {
                    var along = tier[Mathf.Min(tier.Count - 1, i * tier.Count / 4)].Position + toward * 1.5f;
                    mist.Add(along, width * 1.3f, drop * .3f, .1f, random, 0);
                }
            }
            if (vertices.Count > 0 && waterfall != null)
            {
                var mesh = new Mesh { name = "Env_Fall", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.SetUVs(1, data);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                EnvironmentGround.Spawn(parent, "Fall", mesh, waterfall, false);
            }
            // The ledge pools: still water filling the hollow the upper tier lands in.
            foreach (var pool in pools)
            {
                var mesh = Ellipse("Env_FallPool", new Vector3(pool.x, L.KarstLedge(pool.x) - .4f, pool.z) + toward * 1.5f, across * fall.Width * .55f, toward * 3f);
                // The river shader's coordinates: three quarters out across (a slow current), flowing off the ledge; the
                // distance along the flow far upstream of the falls, so no trail.
                var place = new Vector2[mesh.vertexCount];
                var flow = new Vector2[mesh.vertexCount];
                var corners = mesh.vertices;
                for (int i = 0; i < place.Length; i++) { place[i] = new Vector2(Vector3.Dot(corners[i], toward) - 1000f, .75f); flow[i] = new Vector2(toward.x, toward.z); }
                mesh.SetUVs(0, place);
                mesh.SetUVs(1, flow);
                EnvironmentGround.Spawn(parent, "FallPool", mesh, riverWater, false);
            }
        }

        /// <summary>
        /// One falling sheet along a tier: rows at the path samples, widening as it falls. UV0 = (metres across, seconds
        /// since the lip) so streaks stretch as the water speeds up; UV1 = (share of the whole drop fallen, 0..1 across,
        /// tier, layer: 0 sheet, 1 veil, 2 foam).
        /// </summary>
        private static void Sheet(List<Drop> tier, Vector3 toward, Vector3 offset, Vector3 across, float width, float fallTop, float total, int layer,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<Vector4> data, List<int> triangles)
        {
            const int columns = 10;
            float top = tier[0].Position.y, height = Mathf.Max(1f, top - tier[tier.Count - 1].Position.y);
            var normal = (toward + Vector3.up * .3f).normalized;
            int start = vertices.Count;
            for (int r = 0; r < tier.Count; r++)
            {
                var drop = tier[r];
                float w = width * (1f + .35f * Mathf.Clamp01((top - drop.Position.y) / height));
                float fallen = Mathf.Clamp01((fallTop - drop.Position.y) / total);
                for (int c = 0; c <= columns; c++)
                {
                    float u = (float)c / columns;
                    vertices.Add(drop.Position + offset + across * (u - .5f) * w);
                    normals.Add(normal);
                    uv.Add(new Vector2((u - .5f) * w, drop.Time));
                    data.Add(new Vector4(fallen, u, 0, layer));
                }
            }
            for (int r = 0; r < tier.Count - 1; r++)
            for (int c = 0; c < columns; c++)
            {
                int a = start + r * (columns + 1) + c, b = a + columns + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
            }
        }

        /// <summary>A disc of white water spreading from where a tier lands; its streaks run outward (UV0.y grows with the radius).</summary>
        private static void Foam(Vector3 centre, float radius, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<Vector4> data, List<int> triangles)
        {
            const int segments = 24, rings = 4;
            int start = vertices.Count;
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                float k = (float)r / rings, a = s * Mathf.PI * 2f / segments;
                float rr = radius * Mathf.Lerp(.15f, 1f, k);
                vertices.Add(centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rr);
                normals.Add(Vector3.up);
                uv.Add(new Vector2(a * radius, rr * .4f));
                data.Add(new Vector4(1, k, 0, 2));
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + r * (segments + 1) + s, b = a + segments + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
            }
        }

        private static Mesh Ellipse(string name, Vector3 centre, Vector3 axisA, Vector3 axisB)
        {
            const int segments = 24;
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = centre;
            for (int s = 0; s < segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                vertices[s + 1] = centre + axisA * Mathf.Cos(a) + axisB * Mathf.Sin(a);
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            for (int s = 0; s < segments; s++)
            {
                triangles[s * 3] = 0;
                triangles[s * 3 + 1] = 1 + (s + 1) % segments;
                triangles[s * 3 + 2] = 1 + s;
            }
            // Wind the fan to face up whichever way the axes turn.
            if (Vector3.Cross(vertices[triangles[1]] - centre, vertices[triangles[2]] - centre).y < 0)
                for (int s = 0; s < segments; s++) (triangles[s * 3 + 1], triangles[s * 3 + 2]) = (triangles[s * 3 + 2], triangles[s * 3 + 1]);
            var mesh = new Mesh { name = name, vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Mist ----------

        /// <summary>Mist lying on the river: a few long, low wisps a few metres over the water, thicker in hazier weather (EnvMist).
        /// Sparse and thin, so from above the water still reads between them.</summary>
        private static void RiverMist(Billboards mist)
        {
            var random = new System.Random(7);
            float edge = EnvironmentGround.OuterExtent - 10f;
            for (float x = -edge; x <= edge; x += 70f)
            {
                float z = RiverZ(x) + ProceduralArt.Range(random, -8f, 8f);
                float length = ProceduralArt.Range(random, 40f, 64f);
                mist.Add(new Vector3(x, EnvironmentGround.RiverSurface + ProceduralArt.Range(random, 2.5f, 5f), z), length, ProceduralArt.Range(random, 7f, 11f),
                    ProceduralArt.Range(random, .05f, .09f), random, 0);
            }
        }

        /// <summary>Camera-facing soft puffs (EnvMist): centre in the position, corner in UV0, (width, height, density, seed) in UV1, kind in colour (R: smoke).</summary>
        private sealed class Billboards
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> corners = new List<Vector2>();
            private readonly List<Vector4> shapes = new List<Vector4>();
            private readonly List<Color32> kinds = new List<Color32>();
            private float largest;

            public void Add(Vector3 centre, float width, float height, float density, System.Random random, byte smoke)
            {
                float seed = (float)random.NextDouble() * 100f;
                foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) })
                {
                    vertices.Add(centre);
                    corners.Add(corner);
                    shapes.Add(new Vector4(width, height, density, seed));
                    kinds.Add(new Color32((byte)(smoke * 255), 0, 0, 255));
                }
                largest = Mathf.Max(largest, width, height);
            }

            public void Emit(Transform parent, string name, Material material)
            {
                if (vertices.Count == 0) return;
                var triangles = new int[vertices.Count / 4 * 6];
                for (int q = 0; q < vertices.Count / 4; q++)
                {
                    int v = q * 4;
                    triangles[q * 6] = v; triangles[q * 6 + 1] = v + 1; triangles[q * 6 + 2] = v + 2;
                    triangles[q * 6 + 3] = v; triangles[q * 6 + 4] = v + 2; triangles[q * 6 + 5] = v + 3;
                }
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, corners);
                mesh.SetUVs(1, shapes);
                mesh.SetColors(kinds);
                mesh.SetTriangles(triangles, 0);
                // The vertices sit at the puff centres; the shader spreads them out to the puffs' size.
                mesh.RecalculateBounds();
                var bounds = mesh.bounds;
                bounds.Expand(largest);
                mesh.bounds = bounds;
                EnvironmentGround.Spawn(parent, name, mesh, material, false);
            }
        }

        // ---------- Hamlet ----------

        internal struct House
        {
            public float X, Side, Yaw, Width, Depth;
            public House(float x, float side, float yaw, float width, float depth) { X = x; Side = side; Yaw = yaw; Width = width; Depth = depth; }
        }

        /// <summary>
        /// Stilt houses on the north floodplain, at the foot of the slope (x, metres north of the river, yaw, width,
        /// depth). East of the station's line of sight to the river, so none of them hides the water.
        /// </summary>
        internal static readonly House[] Houses =
        {
            new House(74f, 58f, 8f, 8f, 5f), new House(86f, 68f, -6f, 9f, 5.5f), new House(97f, 54f, 18f, 7f, 4.5f),
            new House(110f, 64f, 4f, 10f, 6f), new House(122f, 52f, -12f, 7f, 5f), new House(134f, 66f, 10f, 8f, 5f),
            new House(146f, 56f, 24f, 6.5f, 4.5f), new House(101f, 72f, 0f, 6f, 4f)
        };

        /// <summary>Which houses the war has burnt in each hamlet state; burnt houses with smoke still rising from them.</summary>
        private static bool Burnt(int index) =>
            L.Hamlet == L.HamletState.Burnt || (L.Hamlet == L.HamletState.Mixed && (index == 1 || index == 4 || index == 6));
        private static bool Smoking(int index) =>
            L.Hamlet == L.HamletState.Burnt ? index == 1 || index == 5 : L.Hamlet == L.HamletState.Mixed && index == 4;

        private const int Wood = 0, Thatch = 1, Charred = 2;

        private static void BuildHamlet(FlatMeshBuilder b, Billboards mist)
        {
            var random = new System.Random(31);
            for (int i = 0; i < Houses.Length; i++)
            {
                var house = Houses[i];
                float z = RiverZ(house.X) + house.Side;
                var origin = new Vector3(house.X, L.GroundHeight(house.X, z), z);
                var turn = Quaternion.Euler(0, house.Yaw, 0);
                b.Begin(origin.y, 7f, 0f, (float)random.NextDouble());
                if (Burnt(i)) BurntHouse(b, random, origin, turn, house.Width, house.Depth);
                else StiltHouse(b, origin, turn, house.Width, house.Depth);
                if (!Smoking(i)) continue;
                // A thin column of smoke, leaning downwind as it rises.
                var wind = new Vector3(.6f, 0, .8f);
                for (int k = 0; k < 9; k++)
                {
                    float t = k / 8f, size = Mathf.Lerp(3f, 16f, t);
                    mist.Add(origin + Vector3.up * (2f + t * 38f) + wind * (t * t * 22f), size, size * 1.2f, Mathf.Lerp(.7f, .2f, t), random, 1);
                }
            }
        }

        /// <summary>A house on stilts: posts, a plank floor, woven walls and a tall, steep thatched hip roof.</summary>
        private static void StiltHouse(FlatMeshBuilder b, Vector3 origin, Quaternion turn, float width, float depth)
        {
            const float stilt = 2.1f, wall = 2.2f, roofRise = 3.2f, overhang = .9f;
            Vector3 P(float x, float y, float z) => origin + turn * new Vector3(x, y, z);
            for (int i = 0; i < 4; i++)
            for (int k = 0; k < 3; k++)
                Box(b, Wood, P((i / 3f - .5f) * (width - .4f), stilt * .5f - .2f, (k / 2f - .5f) * (depth - .4f)), new Vector3(.22f, stilt + .4f, .22f), turn);
            Box(b, Wood, P(0, stilt, 0), new Vector3(width, .2f, depth), turn);
            Box(b, Wood, P(0, stilt + .1f + wall * .5f, 0), new Vector3(width - .6f, wall, depth - .6f), turn);
            // Hip roof: eaves rectangle out past the walls, a short ridge along the long side.
            float ex = width * .5f - .3f + overhang, ez = depth * .5f - .3f + overhang, eaves = stilt + .1f + wall - .1f, ridgeY = eaves + roofRise, rx = width * .22f;
            Vector3 a = P(-ex, eaves, -ez), c = P(ex, eaves, -ez), d = P(ex, eaves, ez), e = P(-ex, eaves, ez);
            Vector3 r0 = P(-rx, ridgeY, 0), r1 = P(rx, ridgeY, 0);
            var centre = P(0, eaves, 0);
            Face(b, Thatch, centre, a, c, r1, r0);
            Face(b, Thatch, centre, d, e, r0, r1);
            Face(b, Thatch, centre, c, d, r1);
            Face(b, Thatch, centre, e, a, r0);
        }

        /// <summary>What fire left: charred stilts of uneven height, a floor fallen to one side, the roof collapsed onto it.</summary>
        private static void BurntHouse(FlatMeshBuilder b, System.Random random, Vector3 origin, Quaternion turn, float width, float depth)
        {
            Vector3 P(float x, float y, float z) => origin + turn * new Vector3(x, y, z);
            for (int i = 0; i < 4; i++)
            for (int k = 0; k < 3; k++)
            {
                if (random.NextDouble() < .25) continue;
                float h = ProceduralArt.Range(random, .6f, 2.4f);
                Box(b, Charred, P((i / 3f - .5f) * (width - .4f), h * .5f - .2f, (k / 2f - .5f) * (depth - .4f)), new Vector3(.24f, h + .4f, .24f), turn);
            }
            // The floor, tipped down onto the ground at one end.
            var tilt = turn * Quaternion.Euler(0, 0, 14f);
            Box(b, Charred, P(width * .1f, 1.1f, 0), new Vector3(width * .8f, .2f, depth * .9f), tilt);
            // The roof, collapsed: a charred slab leaning from the ground across the posts.
            var slump = turn * Quaternion.Euler(-22f, 0, 6f);
            Box(b, Charred, P(-width * .1f, 1.2f, depth * .15f), new Vector3(width * .9f, .25f, depth * 1.1f), slump);
            for (int i = 0; i < 4; i++)
            {
                var lean = turn * Quaternion.Euler(ProceduralArt.Range(random, -50f, 50f), ProceduralArt.Range(random, 0f, 180f), ProceduralArt.Range(random, -30f, 30f));
                Box(b, Charred, P(ProceduralArt.Range(random, -width, width) * .4f, .8f, ProceduralArt.Range(random, -depth, depth) * .4f),
                    new Vector3(.18f, .18f, ProceduralArt.Range(random, 2.5f, 4.5f)), lean);
            }
        }

        // ---------- Bridge ----------

        /// <summary>
        /// A rope bridge just downstream of the main fall, from a bamboo tower on the north floodplain into a cleft in
        /// the karst wall: plank deck on a sagging catenary, hand ropes and hangers.
        /// </summary>
        private static void BuildBridge(FlatMeshBuilder b)
        {
            float zr = RiverZ(BridgeX), deck = L.RiverLevel + 9f;
            var north = new Vector3(BridgeX, deck, zr + 24f);
            var toward = TowardRiver(BridgeX, zr - 20f);
            var south = new Vector3(BridgeX, deck, zr - 15f);
            for (int i = 0; i < 160 && L.GroundHeight(south.x, south.z) < deck + 2f; i++) south -= toward * .25f;
            south -= toward * 1f;
            b.Begin(L.RiverLevel, 20f, 0f, .5f);
            // The north tower: four bamboo posts braced together, standing on the floodplain.
            float ground = L.GroundHeight(north.x, north.z);
            var along = (south - north).normalized;
            var side = Vector3.Cross(Vector3.up, along).normalized;
            foreach (float s in new[] { -1f, 1f })
            foreach (float f in new[] { -1f, 0f })
            {
                var foot = north + side * s * 1f + along * f * 1.2f;
                foot.y = ground - .3f;
                Box(b, Wood, (foot + new Vector3(foot.x, deck + 1.6f, foot.z)) * .5f, new Vector3(.2f, deck + 1.9f - ground, .2f), Quaternion.LookRotation(along));
            }
            Box(b, Wood, north + Vector3.up * 1.4f, new Vector3(2.4f, .15f, .15f), Quaternion.LookRotation(along));
            float span = Vector3.Distance(north, south), sag = span * .06f;
            Vector3 Deck(float t) => Vector3.Lerp(north, south, t) - Vector3.up * sag * 4f * t * (1f - t);
            for (float d = .4f; d < span - .2f; d += .6f)
            {
                float t = d / span;
                Box(b, Wood, Deck(t), new Vector3(1.4f, .06f, .3f), Quaternion.LookRotation(along));
            }
            const int segments = 28;
            foreach (float s in new[] { -.72f, .72f })
            foreach (float y in new[] { 0f, 1.1f })
            {
                var spine = new Vector3[segments + 1];
                for (int i = 0; i <= segments; i++) spine[i] = Deck((float)i / segments) + side * s + Vector3.up * (y - .03f);
                ProceduralArt.Tube(b, Thatch, spine, t => .045f, 4);
            }
            for (float d = 1.8f; d < span - 1f; d += 1.8f)
            {
                var p = Deck(d / span);
                foreach (float s in new[] { -.72f, .72f })
                    Box(b, Thatch, p + side * s + Vector3.up * .55f, new Vector3(.03f, 1.1f, .03f), Quaternion.LookRotation(along));
            }
        }

        // ---------- Mesh helpers ----------

        /// <summary>A box of the given size, centre and rotation; each face wound to face out.</summary>
        private static void Box(FlatMeshBuilder b, int slot, Vector3 centre, Vector3 size, Quaternion rotation)
        {
            Vector3 x = rotation * Vector3.right * size.x * .5f, y = rotation * Vector3.up * size.y * .5f, z = rotation * Vector3.forward * size.z * .5f;
            foreach (var (n, u, v) in new[] { (x, y, z), (-x, y, z), (y, x, z), (-y, x, z), (z, x, y), (-z, x, y) })
                Face(b, slot, centre, centre + n - u - v, centre + n + u - v, centre + n + u + v, centre + n - u + v);
        }

        /// <summary>A flat polygon (3 or 4 corners) wound so its normal points away from <paramref name="inside"/>.</summary>
        private static void Face(FlatMeshBuilder b, int slot, Vector3 inside, params Vector3[] corners)
        {
            var mid = Vector3.zero;
            foreach (var c in corners) mid += c;
            mid /= corners.Length;
            if (Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), mid - inside) < 0) System.Array.Reverse(corners);
            if (corners.Length == 3) b.Triangle(slot, corners[0], corners[1], corners[2], false);
            else b.Quad(slot, corners[0], corners[1], corners[2], corners[3], false);
        }
    }
}
