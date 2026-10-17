using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The horizon: Truong Son ranges from real elevation data and the limestone
    /// tower karst in front of them, baked by Art/Source/build_mountains.py into
    /// Resources/Environment/Mountains.bytes. A sea of cloud lies between the
    /// layers: drawn on the slopes (EnvKarstCloud.hlsl) and on a skirt far behind
    /// them, so it fills the gaps where the view opens on sky.
    /// Close in, the same polar grid continues the terrain mesh from its edge
    /// (EnvironmentGround.OuterExtent) on GroundHeight's hills, so the meadow runs
    /// on into the ranges without a gap. The river leaves the terrain mesh east and
    /// west and runs on down a gorge through the ranges: the rings sink along it and
    /// a corridor mesh carries the riverbed and its water (EnvKarst.hlsl KarstWater)
    /// at the spacing the rings cannot give. The ranges
    /// are kilometres out; their shader squeezes distance along each view ray so
    /// they fit inside the camera's far plane. No colliders, no shadows.
    /// </summary>
    internal static class EnvironmentBackdrop
    {
        /// <summary>
        /// The shaders' squeeze (EnvSqueeze.hlsl): earliest start, log scale, where 30 km lands (inside the gameplay
        /// camera's 420 m far plane), and the terrain mesh's half size, where each view ray's squeeze starts.
        /// </summary>
        private static readonly Vector4 Squeeze = new Vector4(300f, 15f, 414f, EnvironmentGround.OuterExtent);
        private const int SectorColumns = 110;

        public static void Build(Transform root)
        {
            var data = Resources.Load<TextAsset>("Environment/Mountains");
            var shader = Shader.Find("TienTuyen/Environment/Mountains");
            if (data == null || shader == null || !shader.isSupported)
            {
                Debug.LogWarning("EnvironmentBackdrop: mountain data or shader missing; the horizon stays empty.");
                return;
            }
            var backdrop = new GameObject("Backdrop").transform;
            backdrop.SetParent(root, false);

            float[] radii, azimuths, heights;
            byte[] sun, sky, cavity, rise, river;
            Mesh[] meshes;
            int[] kinds;
            Read(data.bytes, out radii, out azimuths, out heights, out sun, out sky, out cavity, out rise, out river, out meshes, out kinds);
            Resources.UnloadAsset(data);   // 13 MB of bytes, all in the meshes now
            int rings = radii.Length, columns = azimuths.Length;

            var positions = new Vector3[rings * columns];
            var inside = new bool[rings * columns];
            for (int i = 0; i < rings; i++)
            {
                float r = radii[i];
                for (int j = 0; j < columns; j++)
                {
                    int k = i * columns + j;
                    float a = azimuths[j] * Mathf.Deg2Rad, fromRiver = river[k] * 4f;
                    var p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                    float y = Ground(ref p, heights[k], Far(fromRiver, r), out inside[k]);
                    // Sunk under the corridor, which carries the riverbed at its own spacing.
                    y -= (4f + .01f * r) * (1f - Smooth((fromRiver - 100f) / 50f)) * (1f - Smooth((r - 2050f) / 100f));
                    positions[k] = new Vector3(p.x, y, p.z);
                }
            }

            var material = new Material(shader) { name = "Env_Mountains" };
            material.SetVector("_Squeeze", Squeeze);
            var rock = Resources.Load<Texture2D>("Environment/KarstRock");
            if (rock != null) material.SetTexture("_KarstRock", rock);
            for (int start = 0; start < columns; start += SectorColumns)
            {
                int count = Mathf.Min(SectorColumns, columns - start) + 1;   // shares its last column with the next sector
                Spawn(backdrop, "Range_" + start / SectorColumns, Sector(positions, inside, sun, sky, cavity, rise, rings, columns, start, count), material);
            }
            var towerMaterial = new Material(material) { name = "Env_Towers" };
            towerMaterial.SetFloat("_Tower", 1f);
            var cloudShader = Shader.Find("TienTuyen/Environment/MountainCloud");
            Material cloudMaterial = null;
            if (cloudShader != null && cloudShader.isSupported)
            {
                // The skirt's cloud must match the band on the slopes: same air, same cloud.
                cloudMaterial = new Material(cloudShader) { name = "Env_MountainCloud" };
                cloudMaterial.SetVector("_Squeeze", Squeeze);
                foreach (var name in new[] { "_Aerial", "_CloudSea" }) cloudMaterial.SetVector(name, material.GetVector(name));
                cloudMaterial.SetColor("_AerialTint", material.GetColor("_AerialTint"));
            }
            for (int k = 0; k < meshes.Length; k++)
            {
                if (kinds[k] == 0) Spawn(backdrop, "Tower_" + k, meshes[k], towerMaterial);
                else if (kinds[k] == 2) Spawn(backdrop, "River_" + k, meshes[k], material);
                else if (cloudMaterial != null) Spawn(backdrop, "CloudSkirt_" + k, meshes[k], cloudMaterial);
            }
            EnvironmentSkyLife.Build(backdrop, Squeeze);
            RenderPipelineManager.beginContextRendering -= FollowSky;
            RenderPipelineManager.beginContextRendering += FollowSky;
        }

        private static readonly int SkyId = Shader.PropertyToID("_KarstSky"), SkyParamsId = Shader.PropertyToID("_KarstSkyParams");
        private static RenderTexture skyCopy;
        private static Texture skyCopied;

        /// <summary>
        /// The ranges' air takes the colour of the sky behind them (EnvKarst.hlsl KarstSkyBehind): EnvSky's photo,
        /// blurred through the mips of a small copy, re-made only when the weather swaps the photo.
        /// </summary>
        private static void FollowSky(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            var sky = RenderSettings.skybox;
            var photo = sky != null && sky.HasProperty("_Panorama") ? sky.GetTexture("_Panorama") : null;
            float weight = photo != null && photo != Texture2D.blackTexture ? sky.GetFloat("_PanoramaWeight") : 0f;
            if (weight > 0f && (photo != skyCopied || skyCopy == null || !skyCopy.IsCreated()))
            {
                if (skyCopy == null)
                {
                    skyCopy = new RenderTexture(256, 128, 0, RenderTextureFormat.ARGBHalf)
                        { name = "Env_KarstSky", useMipMap = true, autoGenerateMips = true, wrapModeU = TextureWrapMode.Repeat,
                          wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                }
                Graphics.Blit(photo, skyCopy);
                skyCopied = photo;
            }
            Shader.SetGlobalTexture(SkyId, skyCopy != null ? skyCopy : Texture2D.blackTexture);
            Shader.SetGlobalVector(SkyParamsId, weight > 0f
                ? new Vector4(sky.GetFloat("_PanoramaOffset"), sky.GetFloat("_PanoramaExposure"), weight, 0f) : Vector4.zero);
        }

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>
        /// How far the baked ranges replace BaseHeight's hills: from 250 m to 3 km, but on BaseHeight out to 600 m
        /// over the open south (bearings 140-240), and fully baked along the gorge past 1.4 km (far), where the
        /// carved river must win over the uncarved foothills.
        /// </summary>
        private static float Weight(Vector3 p, float far)
        {
            float r = new Vector2(p.x, p.z).magnitude, bearing = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
            if (bearing < 0f) bearing += 360f;
            float w = Smooth((r - 250f) / 2750f);
            float south = Smooth((bearing - 140f) / 10f) * (1f - Smooth((bearing - 230f) / 10f));
            w = Mathf.Lerp(w, Smooth((r - 600f) / 2400f), south);
            return Mathf.Max(w, far);
        }

        /// <summary>The gorge's hold on the far ground: within ~300 m of the river, past 1.4 km.</summary>
        private static float Far(float fromRiver, float r) => Smooth((r - 1400f) / 300f) * (1f - Smooth((fromRiver - 250f) / 150f));

        /// <summary>
        /// Height of the far ground at p, blended from BaseHeight into the baked height. Inside the terrain mesh
        /// the ground is not drawn: a vertex there that still belongs to a drawn quad is pulled out to just inside the
        /// mesh's edge and a little below it, so the two overlap without a crack.
        /// </summary>
        private static float Ground(ref Vector3 p, float baked, float far, out bool inside)
        {
            const float edge = EnvironmentGround.OuterExtent;
            float w = Weight(p, far), square = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)), drop = 0f;
            inside = square < edge;
            if (inside)
            {
                float pull = (edge - 1f) / Mathf.Max(square, 1e-3f);
                p.x *= pull;
                p.z *= pull;
                drop = .3f;
            }
            float near = w < 1f ? L.BaseHeight(p.x, p.z) - drop : 0f;
            return near * (1f - w) + baked * w;
        }

        private static void Read(byte[] bytes, out float[] radii, out float[] azimuths, out float[] heights,
            out byte[] sun, out byte[] sky, out byte[] cavity, out byte[] rise, out byte[] river, out Mesh[] meshes, out int[] kinds)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            if (new string(reader.ReadChars(4)) != "TTMB" || reader.ReadInt32() != 4)
                throw new InvalidDataException("Mountains.bytes: unknown format");
            int rings = reader.ReadInt32(), columns = reader.ReadInt32(), meshCount = reader.ReadInt32();
            radii = Floats(reader, rings);
            azimuths = Floats(reader, columns);
            heights = new float[rings * columns];
            for (int k = 0; k < heights.Length; k++) heights[k] = Mathf.HalfToFloat(reader.ReadUInt16());
            sun = reader.ReadBytes(rings * columns);
            sky = reader.ReadBytes(rings * columns);
            cavity = reader.ReadBytes(rings * columns);
            rise = reader.ReadBytes(rings * columns);
            river = reader.ReadBytes(rings * columns);
            meshes = new Mesh[meshCount];
            kinds = new int[meshCount];
            for (int m = 0; m < meshCount; m++) meshes[m] = Piece(reader, m, out kinds[m]);
        }

        /// <summary>
        /// One piece, kind 0 a tower, 1 the cloud skirt, 2 a river corridor: positions, half normals, colours (sun, sky,
        /// cavity, height up the wall), rise, 16-bit triangles. A corridor's y is the baked (carved) height, its normal
        /// slot holds the gorge's hold (Far) in x, and its alpha the distance to the river in quarter metres;
        /// it is set on the ground here like the rings, and its normals recomputed.
        /// </summary>
        private static Mesh Piece(BinaryReader reader, int index, out int kind)
        {
            kind = reader.ReadInt32();
            int count = reader.ReadInt32(), indexCount = reader.ReadInt32();
            var vertices = new Vector3[count];
            for (int v = 0; v < count; v++) vertices[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var normals = new Vector3[count];
            for (int v = 0; v < count; v++)
                normals[v] = new Vector3(Mathf.HalfToFloat(reader.ReadUInt16()), Mathf.HalfToFloat(reader.ReadUInt16()), Mathf.HalfToFloat(reader.ReadUInt16()));
            var colors = new Color32[count];
            for (int v = 0; v < count; v++) colors[v] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            var rise = reader.ReadBytes(count);
            var uv = new Vector2[count];
            for (int v = 0; v < count; v++) uv[v] = Rise(rise[v]);
            var triangles = new int[indexCount];
            for (int t = 0; t < indexCount; t++) triangles[t] = reader.ReadUInt16();
            if (kind == 2)
                for (int v = 0; v < count; v++)
                    vertices[v].y = Ground(ref vertices[v], vertices[v].y, normals[v].x, out _);
            var mesh = new Mesh { name = (kind == 0 ? "Env_Tower_" : kind == 1 ? "Env_CloudSkirt_" : "Env_River_") + index };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            if (kind == 2) mesh.RecalculateNormals();
            mesh.bounds = SqueezedBounds(vertices);
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Rise (degrees above the ridges in front, baked x16 from -3) into uv.x, which drives the valley cloud.</summary>
        private static Vector2 Rise(byte baked) => new Vector2(baked / 16f - 3f, 0f);

        private static float[] Floats(BinaryReader reader, int count)
        {
            var values = new float[count];
            for (int k = 0; k < count; k++) values[k] = reader.ReadSingle();
            return values;
        }

        private static Mesh Sector(Vector3[] grid, bool[] inside, byte[] sun, byte[] sky, byte[] cavity, byte[] rise, int rings, int columns, int start, int count)
        {
            var vertices = new Vector3[rings * count];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var colors = new Color32[vertices.Length];
            var triangles = new int[(rings - 1) * (count - 1) * 6];
            Vector3 At(int i, int j) => grid[Mathf.Clamp(i, 0, rings - 1) * columns + ((j % columns) + columns) % columns];
            int t = 0;
            for (int i = 0; i < rings; i++)
            {
                for (int c = 0; c < count; c++)
                {
                    int j = (start + c) % columns, v = i * count + c, source = i * columns + j;
                    vertices[v] = grid[source];
                    // Normal from the neighbours along the ring and across rings.
                    Vector3 along = At(i, j + 1) - At(i, j - 1), across = At(i + 1, j) - At(i - 1, j);
                    var n = Vector3.Cross(along, across).normalized;
                    normals[v] = n.y < 0 ? -n : n;
                    colors[v] = new Color32(sun[source], sky[source], cavity[source], 255);
                    uv[v] = Rise(rise[source]);
                    if (i == rings - 1 || c == count - 1) continue;
                    int j1 = (j + 1) % columns, next = (i + 1) * columns;
                    if (inside[source] && inside[i * columns + j1] && inside[next + j] && inside[next + j1]) continue;
                    triangles[t++] = v; triangles[t++] = v + count; triangles[t++] = v + 1;
                    triangles[t++] = v + 1; triangles[t++] = v + count; triangles[t++] = v + count + 1;
                }
            }
            System.Array.Resize(ref triangles, t);
            var mesh = new Mesh { name = "Env_Range_" + start };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.bounds = SqueezedBounds(vertices);
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// Culling bounds where the shader actually draws the vertices: squeezed towards a camera near the
        /// station, padded for the camera's travel around the clearing.
        /// </summary>
        private static Bounds SqueezedBounds(Vector3[] vertices)
        {
            var eye = new Vector3(0f, 2f, 0f);
            var bounds = new Bounds(eye, Vector3.zero);
            foreach (var p in vertices)
            {
                var view = p - eye;
                float d = Mathf.Max(view.magnitude, 1e-3f);
                // Both ends of where a ray's squeeze may start.
                bounds.Encapsulate(eye + view * (Squeezed(d, Squeeze.x) / d));
                bounds.Encapsulate(eye + view * (Squeezed(d, Squeeze.x + 100f) / d));
            }
            bounds.Expand(60f);
            return bounds;
        }

        private static float Squeezed(float d, float start) => d <= start ? d
            : start + (Squeeze.z - start) * Mathf.Log(1f + (d - start) / Squeeze.y) / Mathf.Log(1f + (30000f - start) / Squeeze.y);

        private static void Spawn(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }
}
