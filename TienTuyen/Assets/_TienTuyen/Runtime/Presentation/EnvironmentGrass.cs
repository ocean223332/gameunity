using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using L = TienTuyen.Presentation.EnvironmentLayout;
using Random = System.Random;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Meadow grass and wildflowers (EnvGrass.shader). The GPU places every clump: one instance per cell of a world
    /// grid around each camera, in square rings of ever coarser cells and larger clumps, thinning towards each
    /// ring's edge to the next ring's density and fading into the ground past the last. Where grass grows, how tall
    /// and how dry comes from a grass field baked once from the ground's own paint and meadow fields, so the blades
    /// stand where the ground shows grass. The CPU only picks the visible chunks of each ring.
    /// Clump art is swappable: meshes in Resources/Environment/Grass (GrassNear, GrassMid, GrassFar, Flowers, with
    /// an optional Albedo texture, cut out by its alpha) replace the procedural blades; see EnvGrass.shader for the
    /// mesh convention.
    /// </summary>
    internal static class EnvironmentGrass
    {
        /// <summary>Half size of the grass field: the whole terrain mesh.</summary>
        private const float FieldExtent = EnvironmentGround.OuterExtent;
        private const int FieldResolution = 1024;

        public static void Build(Transform root, Material ground)
        {
            if (Disabled() || ground == null || !SystemInfo.supportsComputeShaders) return;
            var shader = Shader.Find("TienTuyen/Environment/Grass");
            if (shader == null || !shader.isSupported) return;
            var material = new Material(shader) { name = "Env_Grass" };
            var albedo = Resources.Load<Texture2D>("Environment/Grass/Albedo");
            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetFloat("_AlphaClip", 1);
                material.EnableKeyword("_ALPHATEST_ON");
            }
            var field = BakeField(material, ground);
            if (field == null) return;
            var meshes = new[]
            {
                Resources.Load<Mesh>("Environment/Grass/GrassNear") ?? Clump("Env_GrassNear", 14, 3, .22f, .016f, 1),
                Resources.Load<Mesh>("Environment/Grass/GrassMid") ?? Clump("Env_GrassMid", 12, 2, .24f, .02f, 2),
                Resources.Load<Mesh>("Environment/Grass/GrassFar") ?? Clump("Env_GrassFar", 9, 1, .26f, .028f, 3)
            };
            var flowers = Resources.Load<Mesh>("Environment/Grass/Flowers") ?? FlowerClump("Env_GrassFlowers");
            var drawer = new GameObject("MeadowGrass").AddComponent<GrassDrawer>();
            drawer.transform.SetParent(root, false);
            drawer.Init(material, field, meshes, flowers);
        }

        /// <summary>"-grass off" on the command line leaves the grass out (benchmarks compare with and without).</summary>
        private static bool Disabled()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-grass", StringComparison.OrdinalIgnoreCase))
                    return string.Equals(args[i + 1], "off", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        // ---------- Grass field ----------

        /// <summary>
        /// Height and mask at whole metres over the terrain: R the height of the terrain mesh as drawn (the 1 m
        /// station grid, between the vertices of the 2 m valley and 4 m hills grids), G where grass may grow at all:
        /// not in the arena, on the road and the depot track, at the station sites, in the craters or the river.
        /// </summary>
        private static Texture2D BakeHeightMask()
        {
            int n = Mathf.RoundToInt(FieldExtent * 2f) + 1;
            var middle = new CoarseGrid(Rect.MinMaxRect(EnvironmentGround.MiddleWest, EnvironmentGround.MiddleSouth,
                EnvironmentGround.MiddleEast, EnvironmentGround.MiddleNorth), 2);
            var outer = new CoarseGrid(new Rect(-FieldExtent, -FieldExtent, 2f * FieldExtent, 2f * FieldExtent), 4);
            var data = new Color[n * n];
            Vector2 trackEnd = L.DepotTrack[L.DepotTrack.Length - 1];
            ProceduralArt.ParallelRows(n, row =>
            {
                for (int column = 0; column < n; column++)
                {
                    float x = -FieldExtent + column, z = -FieldExtent + row;
                    float h = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) <= EnvironmentGround.InnerExtent ? L.GroundHeight(x, z)
                        : middle.Contains(x, z) ? middle.Height(x, z) : outer.Height(x, z);
                    float mask = Band(L.OutsideDistance(x, z), .5f, 4.5f);
                    // No grass in the river or on its wet mud banks.
                    if (mask > 0) mask *= Band(Mathf.Abs(L.RiverSide(x, z)) - L.RiverHalfWidth, 2f, 5f);
                    if (mask > 0) mask *= Band(L.RoadDistance(x, z, out _), 2.4f, 3.6f);
                    if (mask > 0 && Mathf.Abs(x) < 70f && Mathf.Abs(z) < 70f)
                    {
                        mask *= Band(L.LineDistance(L.DepotTrack, x, z, out _), 1.2f, 2.2f);
                        mask *= Band(L.CraterDistance(x, z, out _), .3f, 2f);
                        foreach (var site in L.Sites)
                            mask *= Band(Vector2.Distance(new Vector2(x, z), new Vector2(site.x, site.y)) - site.z, 0f, 1.5f);
                    }
                    data[row * n + column] = new Color(h, mask, 0, 0);
                }
            });
            var texture = new Texture2D(n, n, TextureFormat.RGFloat, false, true)
            {
                name = "Env_GrassHeight", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(data);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>GroundHeight at a terrain grid's vertices, bilinear between them as the mesh draws it.</summary>
        private sealed class CoarseGrid
        {
            private readonly Rect area;
            private readonly int step, columns, rows;
            private readonly float[] heights;

            public CoarseGrid(Rect area, int step)
            {
                this.area = area;
                this.step = step;
                int n = columns = Mathf.RoundToInt(area.width / step) + 1;
                rows = Mathf.RoundToInt(area.height / step) + 1;
                var h = heights = new float[columns * rows];
                ProceduralArt.ParallelRows(rows, row =>
                {
                    for (int column = 0; column < n; column++)
                        h[row * n + column] = L.GroundHeight(area.xMin + column * step, area.yMin + row * step);
                });
            }

            public bool Contains(float x, float z) => x >= area.xMin && x <= area.xMax && z >= area.yMin && z <= area.yMax;

            public float Height(float x, float z)
            {
                float u = Mathf.Clamp((x - area.xMin) / step, 0, columns - 1.001f), v = Mathf.Clamp((z - area.yMin) / step, 0, rows - 1.001f);
                int i = (int)u, k = (int)v, a = k * columns + i;
                float fu = u - i, fv = v - k;
                float bottom = Mathf.Lerp(heights[a], heights[a + 1], fu), top = Mathf.Lerp(heights[a + columns], heights[a + columns + 1], fu);
                return Mathf.Lerp(bottom, top, fv);
            }
        }

        private static float Band(float value, float from, float to) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));

        /// <summary>The grass field (EnvGrass.shader, pass "Field"): ground height, grass cover and dryness.</summary>
        private static RenderTexture BakeField(Material material, Material ground)
        {
            int pass = material.FindPass("Field");
            if (pass < 0) return null;
            var heightMask = BakeHeightMask();
            int n = heightMask.width;
            material.SetTexture("_GrassHeight", heightMask);
            // Texel centres sit on whole metres.
            material.SetVector("_GrassHeightRect", new Vector4(-FieldExtent - .5f, -FieldExtent - .5f, 1f / n, 1f / n));
            foreach (string name in new[] { "_SplatMap" }) material.SetTexture(name, ground.GetTexture(name));
            foreach (string name in new[] { "_SplatRect", "_RockSlope", "_Playfield" }) material.SetVector(name, ground.GetVector(name));
            var fieldRect = new Vector4(-FieldExtent, -FieldExtent, .5f / FieldExtent, .5f / FieldExtent);
            material.SetVector("_GrassFieldRect", fieldRect);
            var field = new RenderTexture(FieldResolution, FieldResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "Env_GrassField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false
            };
            field.Create();
            using (var bake = new CommandBuffer { name = "Bake grass field" })
            {
                bake.SetRenderTarget(field);
                bake.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(bake);
            }
            material.SetTexture("_GrassField", field);
            return field;
        }

        // ---------- Clump meshes ----------

        /// <summary>
        /// A clump of tapered, curved blades in a disc of the given radius, 1 m at the tallest; uv.y is the height
        /// up the blade. A few blades are straw-coloured. Each blade a strip of quads narrowing to a point.
        /// </summary>
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
                // Blades lean outwards from the clump's heart and arch over by their tip.
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
                var tip = root + lean * height + Vector3.up * height;
                vertices.Add(tip);
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
                // Stem: a thin two-sided ribbon.
                var across = new Vector3(Mathf.Cos(angle + 1.3f), 0, Mathf.Sin(angle + 1.3f)) * .004f;
                int s0 = vertices.Count;
                vertices.Add(root - across); vertices.Add(root + across); vertices.Add(top - across); vertices.Add(top + across);
                var stemNormal = Vector3.Cross(across, Vector3.up).normalized;
                for (int k = 0; k < 4; k++) { normals.Add(stemNormal); uvs.Add(new Vector2(k & 1, k < 2 ? 0 : height)); colors.Add(new Color(.9f, 1f, .8f, 0)); }
                triangles.Add(s0); triangles.Add(s0 + 2); triangles.Add(s0 + 1);
                triangles.Add(s0 + 1); triangles.Add(s0 + 2); triangles.Add(s0 + 3);
                // Head: five petals around a centre, tilted a little.
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
    /// Draws the grass rings for each game camera just before it renders: the visible chunks of each ring go into
    /// one instanced draw per ring (grass) and one for the flowers of the near rings.
    /// </summary>
    internal sealed class GrassDrawer : MonoBehaviour
    {
        /// <summary>One ring: cell size (m), the square band it covers, where it thins, clump scale, flower share.</summary>
        private readonly struct Ring
        {
            public readonly float Cell, Inner, Outer, ThinStart, ThinEnd, Kept, Scale, Flowers;
            public readonly bool Last;
            public Ring(float cell, float inner, float outer, float thinStart, float thinEnd, float kept, float scale, float flowers, bool last)
            {
                Cell = cell; Inner = inner; Outer = outer; ThinStart = thinStart; ThinEnd = thinEnd; Kept = kept; Scale = scale; Flowers = flowers; Last = last;
            }
        }

        // Each ring has a quarter of the clumps per square metre of the one inside it, each clump about twice as wide;
        // a ring thins to that density before its edge. The last fades into the ground's own colour.
        private static readonly Ring[] Rings =
        {
            new Ring(.3f, 0f, 9.6f, 5f, 9.6f, .25f, 1f, .035f, false),
            new Ring(.6f, 9.6f, 28.8f, 18f, 28.8f, .25f, 1.9f, .025f, false),
            new Ring(1.2f, 28.8f, 67.2f, 42f, 67.2f, 0f, 3.6f, 0f, true)
        };
        private const int ChunkCells = 16;

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
        private readonly Dictionary<Camera, CameraBuffers> cameras = new Dictionary<Camera, CameraBuffers>();
        private readonly Plane[] planes = new Plane[6];

        public void Init(Material grassMaterial, RenderTexture grassField, Mesh[] clumpMeshes, Mesh flowerMesh)
        {
            material = grassMaterial;
            field = grassField;
            meshes = clumpMeshes;
            flowers = flowerMesh;
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += Draw;

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= Draw;

        private void OnDestroy()
        {
            cameras.Clear();
            if (field != null) field.Release();
            if (material != null) Destroy(material);
        }

        /// <summary>Chunks one ring may draw at most (the shader's _GrassChunks array).</summary>
        private const int MaxChunks = 64;
        private static readonly Vector4[] Origins = new Vector4[MaxChunks];

        private CameraBuffers BuffersFor(Camera camera)
        {
            if (cameras.TryGetValue(camera, out var buffers)) return buffers;
            // Forget cameras that are gone (a capture rig, a cutscene camera).
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
            // One layout per camera and frame: the buffers are not rewritten while a frame may still read them.
            if (buffers.Frame == Time.frameCount) { Submit(camera, buffers); return; }
            buffers.Frame = Time.frameCount;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            Vector3 eye = camera.transform.position;
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
                    // Off the terrain, or wholly inside the ring's hole or the bare arena.
                    if (maxX < -EnvironmentGround.OuterExtent || minX > EnvironmentGround.OuterExtent ||
                        maxZ < -EnvironmentGround.OuterExtent || minZ > EnvironmentGround.OuterExtent) continue;
                    if (minX > eye.x - ring.Inner && maxX < eye.x + ring.Inner && minZ > eye.z - ring.Inner && maxZ < eye.z + ring.Inner) continue;
                    if (minX > -L.HalfX && maxX < L.HalfX && minZ > -L.HalfZ && maxZ < L.HalfZ) continue;
                    float y = L.GroundHeight(minX + size * .5f, minZ + size * .5f);
                    var bounds = new Bounds(new Vector3(minX + size * .5f, y + 1f, minZ + size * .5f), new Vector3(size + 2f, 20f, size + 2f));
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds) || visible.Count >= MaxChunks) continue;
                    visible.Add(new Vector4(minX, minZ, 0, 0));
                }
                SetRing(buffers.GrassProps[i], buffers.Chunks[i], ring, eye, 0);
                SetRing(buffers.FlowerProps[i], buffers.Chunks[i], ring, eye, 1);
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
