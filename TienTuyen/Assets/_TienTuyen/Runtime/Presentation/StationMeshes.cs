using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>Materials of the station's built structures, indexes into <see cref="StationMaterials.Create"/>.</summary>
    internal enum StationMaterial { Frond, Banana, DriedFrond, Bamboo, BambooTan, BambooGrey, Wood, Canvas, Net, Count }

    /// <summary>
    /// The trellis, hammock and camouflage net materials: copies of the flora's EnvLeaf materials (cut
    /// leaves, bamboo culms, log bark), and the hammock nylon and garnished net from Resources/Props.
    /// </summary>
    internal static class StationMaterials
    {
        /// <summary>One material per <see cref="StationMaterial"/>; null without the flora biome or the textures.</summary>
        public static Material[] Create()
        {
            Material Flora(string species, string suffix)
            {
                var plant = FloraPrefab(species);
                var group = plant != null ? plant.GetComponent<LODGroup>() : null;
                if (group == null) return null;
                foreach (var renderer in group.GetLODs()[0].renderers)
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && material.name.EndsWith(suffix, System.StringComparison.Ordinal)) return material;
                return null;
            }
            Material palm = Flora("Palm_A", "_M0"), banana = Flora("Banana_A", "_M0"), log = Flora("Log", "_M0");
            Material culm = Flora("Bamboo", "_M0"), oldCulm = Flora("Bamboo", "_M3"), greyCulm = Flora("Bamboo", "_M4");
            var canvas = Resources.Load<Texture2D>("Props/StationCanvas_BaseColor");
            var canvasNormal = Resources.Load<Texture2D>("Props/StationCanvas_Normal");
            var net = Resources.Load<Texture2D>("Props/StationNet_BaseColor");
            if (palm == null || banana == null || log == null || culm == null || oldCulm == null || greyCulm == null
                || canvas == null || canvasNormal == null || net == null)
                return null;

            var m = new Material[(int)StationMaterial.Count];
            // Living palms read beige lying flat in full sun; the cut fronds are greener.
            m[(int)StationMaterial.Frond] = Copy(palm, "StationFrond", new Color(.75f, .92f, .65f), 1.05f);
            m[(int)StationMaterial.Banana] = banana;
            m[(int)StationMaterial.DriedFrond] = Copy(palm, "StationDriedFrond", new Color(1.05f, .85f, .55f), .45f);
            // Cut culms weather from green towards straw.
            m[(int)StationMaterial.Bamboo] = Copy(culm, "StationBamboo", new Color(1.05f, 1f, .78f), .8f);
            m[(int)StationMaterial.BambooTan] = Copy(oldCulm, "StationBambooTan", new Color(1.3f, 1.08f, .7f), .75f);
            m[(int)StationMaterial.BambooGrey] = Copy(greyCulm, "StationBambooGrey", new Color(1.12f, 1.02f, .8f), .7f);
            m[(int)StationMaterial.Wood] = Copy(log, "StationWood", Color.white, 1f);

            var cloth = Copy(log, "StationCanvas", Color.white, 1f);
            cloth.SetTexture("_BaseMap", canvas);
            cloth.SetTexture("_BumpMap", canvasNormal);
            cloth.SetFloat("_BumpScale", .6f);
            cloth.SetFloat("_Smoothness", .32f);
            cloth.SetFloat("_Translucency", .35f);
            cloth.SetFloat("_Cull", (float)CullMode.Off);
            m[(int)StationMaterial.Canvas] = cloth;

            var netting = Copy(palm, "StationNet", Color.white, 1f);
            netting.SetTexture("_BaseMap", net);
            netting.SetFloat("_Cutoff", .5f);
            netting.SetFloat("_Smoothness", .05f);
            netting.SetFloat("_Translucency", .25f);
            m[(int)StationMaterial.Net] = netting;
            return m;
        }

        /// <summary>A flora species' prefab (FloraBiome), or null.</summary>
        public static GameObject FloraPrefab(string species)
        {
            var biome = Resources.Load<FloraBiome>(FloraBiome.ResourcePath);
            int index = biome != null ? biome.IndexOf(species) : -1;
            return index < 0 ? null : biome.Plants[index].Prefab;
        }

        /// <summary>
        /// A bunker-mound fern's material: the forest's FernSmall is an olive understory plant that reads as dark
        /// blotches on the open red earth, so the mound's copies are greener (the forest's ferns keep theirs).
        /// </summary>
        public static Material OpenGroundFern(Material source)
        {
            var material = Copy(source, source.name + "_Open", new Color(.9f, 1.28f, .78f), 1.1f);
            material.SetFloat("_TintVariation", source.GetFloat("_TintVariation"));
            return material;
        }

        /// <summary>
        /// A shade draped just above the ground: it multiplies what is under it by its vertex colour, A the
        /// strength, and a mottled earth texture (UV in texture tiles). URP's particle multiply mode, so it needs
        /// no shader of its own; null if that shader is missing.
        /// </summary>
        public static Material GroundShade()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return null;
            var material = new Material(shader) { name = "StationGroundShade" };
            material.SetTexture("_BaseMap", Mottle());
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 3f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAMODULATE_ON");
            // Before the other transparents, which draw over the ground.
            material.renderQueue = (int)RenderQueue.Transparent - 10;
            return material;
        }

        private static Texture2D mottle;

        /// <summary>Trodden earth: soft light and dark patches with a few scuffs, tiling.</summary>
        private static Texture2D Mottle()
        {
            if (mottle != null) return mottle;
            const int size = 128;
            mottle = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "StationMottle", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var pixels = new Color32[size * size];
            float Tiled(float x, float y, float period, float seed)
            {
                // Blends four shifted samples so the noise repeats every period.
                float u = x / size, v = y / size;
                float a = Mathf.PerlinNoise(seed + x / size * period, seed + y / size * period);
                float b = Mathf.PerlinNoise(seed + (x / size - 1f) * period, seed + y / size * period);
                float c = Mathf.PerlinNoise(seed + x / size * period, seed + (y / size - 1f) * period);
                float d = Mathf.PerlinNoise(seed + (x / size - 1f) * period, seed + (y / size - 1f) * period);
                return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float broad = Tiled(x, y, 3f, 11.3f), fine = Tiled(x, y, 14f, 47.1f);
                float value = Mathf.Clamp01(.86f + (broad - .5f) * .22f + (fine - .5f) * .12f);
                byte level = (byte)(value * 255f);
                pixels[y * size + x] = new Color32(level, level, level, 255);
            }
            mottle.SetPixels32(pixels);
            mottle.Apply(true, true);
            return mottle;
        }

        private static Material Copy(Material source, string name, Color tint, float saturation)
        {
            var material = new Material(source) { name = name };
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Saturation", saturation);
            material.SetFloat("_TintVariation", 0f);
            return material;
        }
    }

    /// <summary>
    /// Textured geometry for one station structure, one submesh per material: round tubes, cloth and net
    /// sheets, and single-leaf cards. Vertex colours carry EnvLeaf's R wind weight, G sway phase, B flutter
    /// and A occlusion; UVs are in texture tiles.
    /// </summary>
    internal sealed class StationMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int>[] triangles = new List<int>[(int)StationMaterial.Count];

        public StationMesh()
        {
            for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();
        }

        /// <summary>
        /// A round tube along a path (Catmull-Rom smoothed with <paramref name="subdivisions"/> per segment).
        /// U wraps the circumference once; V runs the length in units of <paramref name="tile"/> metres.
        /// </summary>
        public void Tube(StationMaterial material, IList<Vector3> path, float radius, int sides, float tile, int subdivisions = 1, float endRadius = -1f)
        {
            var points = Smooth(path, subdivisions);
            if (endRadius < 0) endRadius = radius;
            float total = 0;
            for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);
            Vector3 tangent = (points[1] - points[0]).normalized;
            Vector3 side = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < .9f ? Vector3.up : Vector3.right).normalized;
            float along = 0;
            int start = vertices.Count;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) along += Vector3.Distance(points[i - 1], points[i]);
                Vector3 next = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                // Parallel transport keeps the seam from twisting round bends.
                side = Vector3.ProjectOnPlane(side, next).normalized;
                tangent = next;
                Vector3 up = Vector3.Cross(side, tangent);
                float r = Mathf.Lerp(radius, endRadius, total > 0 ? along / total : 0);
                for (int j = 0; j <= sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    Vector3 normal = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    vertices.Add(points[i] + normal * r);
                    normals.Add(normal);
                    uvs.Add(new Vector2((float)j / sides, along / tile));
                    colors.Add(new Color(0, 0, 0, 1));
                }
            }
            var list = triangles[(int)material];
            int ring = sides + 1;
            for (int i = 0; i < points.Count - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = start + i * ring + j, b = a + ring;
                    list.Add(a); list.Add(b); list.Add(a + 1);
                    list.Add(a + 1); list.Add(b); list.Add(b + 1);
                }
        }

        /// <summary>A sheet over a grid of points (rows by columns) with its UVs; normals follow the surface.</summary>
        public void Sheet(StationMaterial material, Vector3[,] grid, Vector2[,] uv, float wind)
        {
            int rows = grid.GetLength(0), columns = grid.GetLength(1);
            int start = vertices.Count;
            float phase = (grid[0, 0].x * .37f + grid[0, 0].z * .21f) % 1f;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < columns; j++)
                {
                    Vector3 du = grid[i, Mathf.Min(j + 1, columns - 1)] - grid[i, Mathf.Max(j - 1, 0)];
                    Vector3 dv = grid[Mathf.Min(i + 1, rows - 1), j] - grid[Mathf.Max(i - 1, 0), j];
                    Vector3 normal = Vector3.Cross(dv, du).normalized;
                    vertices.Add(grid[i, j]);
                    normals.Add(normal.y < 0 ? -normal : normal);
                    uvs.Add(uv[i, j]);
                    // Edges are tied; the middle moves.
                    float free = Mathf.Sin(Mathf.PI * i / (rows - 1)) * Mathf.Sin(Mathf.PI * j / (columns - 1));
                    colors.Add(new Color(wind * free, phase, 0f, 1f));
                }
            var list = triangles[(int)material];
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < columns - 1; j++)
                {
                    int a = start + i * columns + j, b = a + columns;
                    list.Add(a); list.Add(b); list.Add(a + 1);
                    list.Add(a + 1); list.Add(b); list.Add(b + 1);
                }
        }

        /// <summary>
        /// A cut leaf as a square card bent along its length, from <paramref name="root"/> along
        /// <paramref name="dir"/>, its tip moved by <paramref name="bend"/>. The leaf textures draw the
        /// stalk at the top centre, so the card's top edge sits at the root. The card spans
        /// <paramref name="side"/> when given, otherwise the horizontal across <paramref name="dir"/>.
        /// </summary>
        public void Card(StationMaterial material, Vector3 root, Vector3 dir, float length, Vector3 bend, float occlusion, float phase, Vector3 side = default)
        {
            var across = side.sqrMagnitude > 1e-4f ? side : Vector3.Cross(dir, Vector3.up);
            across = (across.sqrMagnitude < 1e-4f ? Vector3.right : across.normalized) * length;
            Vector3 normal = Vector3.Cross(across, dir).normalized;
            if (normal.y < 0) normal = -normal;
            int start = vertices.Count;
            for (int row = 0; row < 3; row++)
            {
                float v = row / 2f;
                Vector3 spine = root + dir * (length * v) + bend * (v * v);
                for (int column = 0; column < 2; column++)
                {
                    vertices.Add(spine + across * (column - .5f));
                    normals.Add(normal);
                    uvs.Add(new Vector2(column, 1f - v));
                    // Tied at the root.
                    colors.Add(new Color(.2f * v, phase, .4f, occlusion));
                }
            }
            var list = triangles[(int)material];
            for (int row = 0; row < 2; row++)
            {
                int a = start + row * 2;
                list.Add(a); list.Add(a + 1); list.Add(a + 2);
                list.Add(a + 1); list.Add(a + 3); list.Add(a + 2);
            }
        }

        /// <summary>Builds the mesh under <paramref name="parent"/> with the used materials only.</summary>
        public void Emit(Transform parent, string name, Material[] materials)
        {
            var used = new List<int>();
            for (int i = 0; i < triangles.Length; i++)
                if (triangles[i].Count > 0) used.Add(i);
            if (used.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, subMeshCount = used.Count };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            var shared = new Material[used.Count];
            for (int s = 0; s < used.Count; s++)
            {
                mesh.SetTriangles(triangles[used[s]], s, false);
                shared[s] = materials[used[s]];
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            // Wind sway moves leaves and cloth outside their rest bounds.
            var bounds = mesh.bounds;
            bounds.Expand(1f);
            mesh.bounds = bounds;
            mesh.UploadMeshData(true);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = shared;
        }

        private static List<Vector3> Smooth(IList<Vector3> path, int subdivisions)
        {
            var points = new List<Vector3>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 p0 = path[Mathf.Max(i - 1, 0)], p1 = path[i], p2 = path[i + 1], p3 = path[Mathf.Min(i + 2, path.Count - 1)];
                for (int s = 0; s < subdivisions; s++)
                {
                    float t = (float)s / subdivisions, t2 = t * t, t3 = t2 * t;
                    points.Add(.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            points.Add(path[path.Count - 1]);
            return points;
        }
    }
}
