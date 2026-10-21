using System.Collections.Generic;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The station's meadow grass (EnvGrass.shader) on the duel's hills: the same GPU-placed clumps and wildflowers,
    /// fed with a grass field baked from this terrain and this clearing's paint instead of the station's. Grass
    /// starts just past the berm; the yard, the berm and the spawn stores stay clear. Clumps are placed by the
    /// shader's world-cell hash, so unlike the plants they are not mirrored; they stand ankle to knee high, below
    /// anything a fighter is seen against.
    /// </summary>
    internal static class DuelArenaGrass
    {
        private const float FieldExtent = EnvironmentGround.OuterExtent;
        private const int FieldResolution = 1024;

        public static void Build(Transform root, Material ground)
        {
            if (ground == null || !SystemInfo.supportsComputeShaders) return;
            var shader = Shader.Find("TienTuyen/Environment/Grass");
            if (shader == null || !shader.isSupported) return;
            var material = new Material(shader) { name = "Duel_Grass" };
            var albedo = Resources.Load<Texture2D>("Environment/Grass/Albedo");
            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetFloat("_AlphaClip", 1);
                material.EnableKeyword("_ALPHATEST_ON");
            }
            var heights = new TerrainHeights();
            var field = BakeField(material, ground, heights);
            if (field == null) return;
            var meshes = new[]
            {
                Resources.Load<Mesh>("Environment/Grass/GrassNear") ?? Clump("Duel_GrassNear", 14, 3, .22f, .016f, 1),
                Resources.Load<Mesh>("Environment/Grass/GrassMid") ?? Clump("Duel_GrassMid", 12, 2, .24f, .02f, 2),
                Resources.Load<Mesh>("Environment/Grass/GrassFar") ?? Clump("Duel_GrassFar", 9, 1, .26f, .028f, 3)
            };
            var flowers = Resources.Load<Mesh>("Environment/Grass/Flowers") ?? FlowerClump("Duel_GrassFlowers");
            var drawer = new GameObject("MeadowGrass").AddComponent<DuelGrassDrawer>();
            drawer.transform.SetParent(root, false);
            drawer.Init(material, field, meshes, flowers, heights);
        }

        // ---------- Grass field ----------

        /// <summary>
        /// The terrain as its meshes draw it: <see cref="DuelArenaView.Height"/> on the clearing's 1 m grid, bilinear
        /// between the hills' 4 m vertices elsewhere.
        /// </summary>
        internal sealed class TerrainHeights
        {
            private const float Cell = DuelArenaView.OuterCell;
            private readonly int side;
            private readonly float[] lattice;

            public TerrainHeights()
            {
                side = Mathf.RoundToInt(2f * FieldExtent / Cell) + 1;
                var h = lattice = new float[side * side];
                int n = side;
                ProceduralArt.ParallelRows(n, row =>
                {
                    for (int column = 0; column < n; column++)
                        h[row * n + column] = DuelArenaView.Height(-FieldExtent + column * Cell, -FieldExtent + row * Cell);
                });
            }

            public float At(float x, float z)
            {
                if (Mathf.Abs(x) <= DuelArenaView.InnerExtentX && Mathf.Abs(z) <= DuelArenaView.InnerExtentZ)
                    return DuelArenaView.Height(x, z);
                float u = Mathf.Clamp((x + FieldExtent) / Cell, 0, side - 1.001f), v = Mathf.Clamp((z + FieldExtent) / Cell, 0, side - 1.001f);
                int i = (int)u, k = (int)v, a = k * side + i;
                float fu = u - i, fv = v - k;
                float bottom = Mathf.Lerp(lattice[a], lattice[a + 1], fu), top = Mathf.Lerp(lattice[a + side], lattice[a + side + 1], fu);
                return Mathf.Lerp(bottom, top, fv);
            }
        }

        private static float Band(float value, float from, float to) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));

        /// <summary>R height, G where grass may grow: past the berm, clear of the spawn stores behind it.</summary>
        private static Texture2D BakeHeightMask(TerrainHeights heights)
        {
            int n = Mathf.RoundToInt(FieldExtent * 2f) + 1;
            var data = new Color[n * n];
            float storeX = DuelArenaView.BermX + 2.4f;
            ProceduralArt.ParallelRows(n, row =>
            {
                for (int column = 0; column < n; column++)
                {
                    float x = -FieldExtent + column, z = -FieldExtent + row;
                    float mask = Band(DuelArenaView.OutsideBerm(x, z), .6f, 2.4f);
                    if (mask > 0) mask *= Band(Vector2.Distance(new Vector2(Mathf.Abs(x), z * Mathf.Sign(x)), new Vector2(storeX, 0f)) - 2.4f, 0f, 1.2f);
                    data[row * n + column] = new Color(heights.At(x, z), mask, 0, 0);
                }
            });
            var texture = new Texture2D(n, n, TextureFormat.RGFloat, false, true)
            {
                name = "Duel_GrassHeight", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(data);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>The grass field (EnvGrass.shader, pass "Field"): ground height, grass cover and dryness.</summary>
        private static RenderTexture BakeField(Material material, Material ground, TerrainHeights heights)
        {
            int pass = material.FindPass("Field");
            if (pass < 0) return null;
            var heightMask = BakeHeightMask(heights);
            int n = heightMask.width;
            material.SetTexture("_GrassHeight", heightMask);
            // Texel centres sit on whole metres.
            material.SetVector("_GrassHeightRect", new Vector4(-FieldExtent - .5f, -FieldExtent - .5f, 1f / n, 1f / n));
            material.SetTexture("_SplatMap", ground.GetTexture("_SplatMap"));
            foreach (string name in new[] { "_SplatRect", "_RockSlope", "_Playfield" }) material.SetVector(name, ground.GetVector(name));
            material.SetVector("_GrassFieldRect", new Vector4(-FieldExtent, -FieldExtent, .5f / FieldExtent, .5f / FieldExtent));
            var field = new RenderTexture(FieldResolution, FieldResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "Duel_GrassField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false
            };
            field.Create();
            using (var bake = new CommandBuffer { name = "Bake duel grass field" })
            {
                bake.SetRenderTarget(field);
                bake.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(bake);
            }
            Object.Destroy(heightMask);
            material.SetTexture("_GrassField", field);
            return field;
        }

        // ---------- Clump meshes (as EnvironmentGrass's) ----------

        /// <summary>A clump of tapered, curved blades in a disc of the given radius, 1 m at the tallest; uv.y is the height up the blade.</summary>
        private static Mesh Clump(string name, int blades, int segments, float radius, float width, int seed)
        {
            var random = new Random(seed * 7919);
            float Next() => (float)random.NextDouble();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int b = 0; b < blades; b++)
            {
                float angle = Next() * Mathf.PI * 2f, distance = Mathf.Sqrt(Next()) * radius;
                var root = new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
                float facing = Next() * Mathf.PI * 2f;
                var across = new Vector3(Mathf.Cos(facing), 0, Mathf.Sin(facing));
                var normal = Vector3.Cross(across, Vector3.up).normalized;
                var lean = (root.sqrMagnitude > 1e-6f ? root.normalized : across) * Mathf.Lerp(.1f, .35f, Next());
                float height = Mathf.Lerp(.55f, 1f, Next()), bladeWidth = width * Mathf.Lerp(.7f, 1.3f, Next());
                var tint = Next() < .15f ? new Color(1.25f, 1.1f, .7f, 0) : Color.white * Mathf.Lerp(.85f, 1.1f, Next());
                tint.a = 0;
                int start = vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / (segments + 1);
                    var spine = root + lean * (t * t) * height + Vector3.up * (height * t);
                    float half = bladeWidth * (1f - t) * .5f;
                    vertices.Add(spine - across * half);
                    vertices.Add(spine + across * half);
                    for (int k = 0; k < 2; k++) { normals.Add(normal); uvs.Add(new Vector2(k, t * height)); colors.Add(tint); }
                }
                vertices.Add(root + lean * height + Vector3.up * height);
                normals.Add(normal);
                uvs.Add(new Vector2(.5f, height));
                colors.Add(tint);
                for (int s = 0; s < segments; s++)
                {
                    int a = start + s * 2;
                    triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3);
                }
                int last = start + segments * 2;
                triangles.Add(last); triangles.Add(vertices.Count - 1); triangles.Add(last + 1);
            }
            return Build(name, vertices, normals, uvs, colors, triangles);
        }

        /// <summary>A few thin stems, each with a small flat head of five petals (vertex alpha 1 marks the petals).</summary>
        private static Mesh FlowerClump(string name)
        {
            var random = new Random(4241);
            float Next() => (float)random.NextDouble();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int f = 0; f < 4; f++)
            {
                float angle = Next() * Mathf.PI * 2f, distance = Mathf.Sqrt(Next()) * .14f;
                var root = new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
                float height = Mathf.Lerp(.75f, 1.15f, Next());
                var top = root + new Vector3(Next() - .5f, 0, Next() - .5f) * .12f + Vector3.up * height;
                var across = new Vector3(Mathf.Cos(angle + 1.3f), 0, Mathf.Sin(angle + 1.3f)) * .004f;
                int s0 = vertices.Count;
                vertices.Add(root - across); vertices.Add(root + across); vertices.Add(top - across); vertices.Add(top + across);
                var stemNormal = Vector3.Cross(across, Vector3.up).normalized;
                for (int k = 0; k < 4; k++) { normals.Add(stemNormal); uvs.Add(new Vector2(k & 1, k < 2 ? 0 : height)); colors.Add(new Color(.9f, 1f, .8f, 0)); }
                triangles.Add(s0); triangles.Add(s0 + 2); triangles.Add(s0 + 1);
                triangles.Add(s0 + 1); triangles.Add(s0 + 2); triangles.Add(s0 + 3);
                float size = Mathf.Lerp(.035f, .055f, Next());
                int centre = vertices.Count;
                vertices.Add(top + Vector3.up * .006f);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(.5f, height));
                colors.Add(new Color(1, 1, 1, 1));
                const int petals = 10;
                for (int p = 0; p < petals; p++)
                {
                    float a = p * Mathf.PI * 2f / petals;
                    float r = size * (p % 2 == 0 ? 1f : .45f);
                    vertices.Add(top + new Vector3(Mathf.Cos(a) * r, -.004f, Mathf.Sin(a) * r));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(.5f, height));
                    colors.Add(new Color(1, 1, 1, 1));
                    triangles.Add(centre); triangles.Add(centre + 1 + (p + 1) % petals); triangles.Add(centre + 1 + p);
                }
            }
            return Build(name, vertices, normals, uvs, colors, triangles);
        }

        private static Mesh Build(string name, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.up * .5f, Vector3.one * 2f);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }

    /// <summary>
    /// Draws the grass rings for each game camera just before it renders, as the station's GrassDrawer does, but
    /// culling its chunks against this terrain and skipping the bare clearing inside the berm.
    /// </summary>
    internal sealed class DuelGrassDrawer : MonoBehaviour
    {
        private readonly struct Ring
        {
            public readonly float Cell, Inner, Outer, ThinStart, ThinEnd, Kept, Scale, Flowers;
            public readonly bool Last;
            public Ring(float cell, float inner, float outer, float thinStart, float thinEnd, float kept, float scale, float flowers, bool last)
            {
                Cell = cell; Inner = inner; Outer = outer; ThinStart = thinStart; ThinEnd = thinEnd; Kept = kept; Scale = scale; Flowers = flowers; Last = last;
            }
        }

        private static readonly Ring[] Rings =
        {
            new Ring(.3f, 0f, 9.6f, 5f, 9.6f, .25f, 1f, .035f, false),
            new Ring(.6f, 9.6f, 28.8f, 18f, 28.8f, .25f, 1.9f, .025f, false),
            new Ring(1.2f, 28.8f, 67.2f, 42f, 67.2f, 0f, 3.6f, 0f, true)
        };
        private const int ChunkCells = 16;
        /// <summary>Chunks one ring may draw at most (the shader's _GrassChunks array).</summary>
        private const int MaxChunks = 64;
        private static readonly Vector4[] Origins = new Vector4[MaxChunks];

        private sealed class CameraBuffers
        {
            public List<Vector4>[] Chunks;
            public MaterialPropertyBlock[] GrassProps, FlowerProps;
            public int[] Counts;
            public int Frame = -1;
        }

        private Material material;
        private RenderTexture field;
        private Mesh[] meshes;
        private Mesh flowers;
        private DuelArenaGrass.TerrainHeights heights;
        private readonly Dictionary<Camera, CameraBuffers> cameras = new Dictionary<Camera, CameraBuffers>();
        private readonly Plane[] planes = new Plane[6];

        public void Init(Material grassMaterial, RenderTexture grassField, Mesh[] clumpMeshes, Mesh flowerMesh, DuelArenaGrass.TerrainHeights terrain)
        {
            material = grassMaterial;
            field = grassField;
            meshes = clumpMeshes;
            flowers = flowerMesh;
            heights = terrain;
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += Draw;

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= Draw;

        private void OnDestroy()
        {
            cameras.Clear();
            if (field != null) field.Release();
            if (material != null) Destroy(material);
        }

        private CameraBuffers BuffersFor(Camera camera)
        {
            if (cameras.TryGetValue(camera, out var buffers)) return buffers;
            var gone = new List<Camera>();
            foreach (var key in cameras.Keys) if (key == null) gone.Add(key);
            foreach (var key in gone) cameras.Remove(key);
            buffers = new CameraBuffers
            {
                Chunks = new List<Vector4>[Rings.Length],
                GrassProps = new MaterialPropertyBlock[Rings.Length], FlowerProps = new MaterialPropertyBlock[Rings.Length],
                Counts = new int[Rings.Length]
            };
            for (int i = 0; i < Rings.Length; i++)
            {
                buffers.Chunks[i] = new List<Vector4>(MaxChunks);
                buffers.GrassProps[i] = new MaterialPropertyBlock();
                buffers.FlowerProps[i] = new MaterialPropertyBlock();
            }
            cameras[camera] = buffers;
            return buffers;
        }

        private void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (material == null || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)) return;
            var buffers = BuffersFor(camera);
            if (buffers.Frame == Time.frameCount) { Submit(camera, buffers); return; }
            buffers.Frame = Time.frameCount;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            Vector3 eye = camera.transform.position;
            const float extent = EnvironmentGround.OuterExtent;
            for (int i = 0; i < Rings.Length; i++)
            {
                var ring = Rings[i];
                float size = ring.Cell * ChunkCells;
                var visible = buffers.Chunks[i];
                visible.Clear();
                int x0 = Mathf.FloorToInt((eye.x - ring.Outer) / size), x1 = Mathf.FloorToInt((eye.x + ring.Outer) / size);
                int z0 = Mathf.FloorToInt((eye.z - ring.Outer) / size), z1 = Mathf.FloorToInt((eye.z + ring.Outer) / size);
                for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    float minX = cx * size, minZ = cz * size, maxX = minX + size, maxZ = minZ + size;
                    // Off the terrain, or wholly inside the ring's hole or the bare clearing.
                    if (maxX < -extent || minX > extent || maxZ < -extent || minZ > extent) continue;
                    if (minX > eye.x - ring.Inner && maxX < eye.x + ring.Inner && minZ > eye.z - ring.Inner && maxZ < eye.z + ring.Inner) continue;
                    if (minX > -DuelArenaView.BermX && maxX < DuelArenaView.BermX && minZ > -DuelArenaView.BermZ && maxZ < DuelArenaView.BermZ) continue;
                    float y = heights.At(minX + size * .5f, minZ + size * .5f);
                    var bounds = new Bounds(new Vector3(minX + size * .5f, y + 1f, minZ + size * .5f), new Vector3(size + 2f, 20f, size + 2f));
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds) || visible.Count >= MaxChunks) continue;
                    visible.Add(new Vector4(minX, minZ, 0, 0));
                }
                SetRing(buffers.GrassProps[i], visible, ring, eye, 0);
                SetRing(buffers.FlowerProps[i], visible, ring, eye, 1);
                buffers.Counts[i] = visible.Count;
            }
            Submit(camera, buffers);
        }

        private static void SetRing(MaterialPropertyBlock props, List<Vector4> chunks, Ring ring, Vector3 eye, int flowers)
        {
            // Always the full length: a property block keeps an array's first length.
            for (int c = 0; c < MaxChunks; c++) Origins[c] = c < chunks.Count ? chunks[c] : Vector4.zero;
            props.SetVectorArray("_GrassChunks", Origins);
            props.SetVector("_GrassRing", new Vector4(ring.Cell, ChunkCells, ring.Inner, ring.Outer));
            props.SetVector("_GrassFade", new Vector4(ring.ThinStart, ring.ThinEnd, ring.Kept, ring.Last ? 1 : 0));
            props.SetVector("_GrassCenter", new Vector4(eye.x, eye.z, flowers, ring.Flowers));
            props.SetFloat("_GrassScale", flowers == 1 ? 1f : ring.Scale);
        }

        private void Submit(Camera camera, CameraBuffers buffers)
        {
            for (int i = 0; i < Rings.Length; i++)
            {
                if (buffers.Counts[i] == 0) continue;
                var grass = new RenderParams(material)
                {
                    camera = camera, matProps = buffers.GrassProps[i], shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    motionVectorMode = MotionVectorGenerationMode.Object, layer = gameObject.layer,
                    worldBounds = new Bounds(camera.transform.position, Vector3.one * (Rings[i].Outer * 2f + 40f))
                };
                int instances = buffers.Counts[i] * ChunkCells * ChunkCells;
                Graphics.RenderMeshPrimitives(grass, meshes[i], 0, instances);
                if (Rings[i].Flowers <= 0) continue;
                var flower = grass;
                flower.matProps = buffers.FlowerProps[i];
                Graphics.RenderMeshPrimitives(flower, flowers, 0, instances);
            }
        }
    }
}
