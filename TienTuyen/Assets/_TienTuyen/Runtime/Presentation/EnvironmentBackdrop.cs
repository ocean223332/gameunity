using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The horizon: Truong Son ranges from real elevation data, baked by
    /// Art/Source/build_mountains.py into Resources/Environment/Mountains.bytes,
    /// with low cloud on the peaks and mist belts in the valleys between them.
    /// Close in, the same polar grid becomes a canopy shell over the hills past
    /// the far flora band, so the forest roof runs on into the ranges. The ranges
    /// are kilometres out; their shader squeezes distance along each view ray so
    /// they fit inside the camera's far plane. No colliders, no shadows.
    /// </summary>
    internal static class EnvironmentBackdrop
    {
        /// <summary>Virtual distance past which the shaders squeeze, and the squeeze scale (see EnvMountains.shader).</summary>
        private static readonly Vector4 Squeeze = new Vector4(300f, 15f, 0f, 0f);
        /// <summary>Canopy shell height over the bare hills, and where it starts (just inside the far flora band's edge).</summary>
        public const float CanopyHeight = 12f, ShellStart = 96f;
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

            float[] radii, azimuths, heights, clouds;
            byte[] sun, sky, cavity;
            Read(data.bytes, out radii, out azimuths, out heights, out sun, out sky, out cavity, out clouds);
            int rings = radii.Length, columns = azimuths.Length;

            var positions = new Vector3[rings * columns];
            for (int i = 0; i < rings; i++)
            {
                float r = radii[i], w = Smooth((r - 250f) / 2750f);
                for (int j = 0; j < columns; j++)
                {
                    float a = azimuths[j] * Mathf.Deg2Rad, x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
                    float far = heights[i * columns + j];
                    float near = w < 1f ? L.BaseHeight(x, z) + ShellHeight(x, z, r) : 0f;
                    positions[i * columns + j] = new Vector3(x, near * (1f - w) + far * w, z);
                }
            }

            var material = new Material(shader) { name = "Env_Mountains" };
            material.SetVector("_Squeeze", Squeeze);
            for (int start = 0; start < columns; start += SectorColumns)
            {
                int count = Mathf.Min(SectorColumns, columns - start) + 1;   // shares its last column with the next sector
                Spawn(backdrop, "Range_" + start / SectorColumns, Sector(positions, sun, sky, cavity, rings, columns, start, count), material);
            }
            var cloudShader = Shader.Find("TienTuyen/Environment/MountainCloud");
            if (cloudShader != null && cloudShader.isSupported && clouds.Length > 0)
            {
                var cloudMaterial = new Material(cloudShader) { name = "Env_MountainCloud" };
                cloudMaterial.SetVector("_Squeeze", Squeeze);
                Spawn(backdrop, "Clouds", Clouds(clouds), cloudMaterial);
            }
        }

        /// <summary>Forest roof over the bare hills: thin over the bombed valley (not its regrown west flank) and the defoliated sightline.</summary>
        private static float ShellHeight(float x, float z, float r)
        {
            float open = Mathf.Max(L.Bombed(x, z), EnvironmentFlora.Sightline(x, z));
            return (2f + CanopyHeight * Smooth((r - ShellStart) / 45f)) * (1f - .8f * open);
        }

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        private static void Read(byte[] bytes, out float[] radii, out float[] azimuths, out float[] heights,
            out byte[] sun, out byte[] sky, out byte[] cavity, out float[] clouds)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            if (new string(reader.ReadChars(4)) != "TTMB" || reader.ReadInt32() != 1)
                throw new InvalidDataException("Mountains.bytes: unknown format");
            int rings = reader.ReadInt32(), columns = reader.ReadInt32(), cloudCount = reader.ReadInt32();
            radii = Floats(reader, rings);
            azimuths = Floats(reader, columns);
            heights = new float[rings * columns];
            for (int k = 0; k < heights.Length; k++) heights[k] = Mathf.HalfToFloat(reader.ReadUInt16());
            sun = reader.ReadBytes(rings * columns);
            sky = reader.ReadBytes(rings * columns);
            cavity = reader.ReadBytes(rings * columns);
            clouds = Floats(reader, cloudCount * 5);
        }

        private static float[] Floats(BinaryReader reader, int count)
        {
            var values = new float[count];
            for (int k = 0; k < count; k++) values[k] = reader.ReadSingle();
            return values;
        }

        private static Mesh Sector(Vector3[] grid, byte[] sun, byte[] sky, byte[] cavity, int rings, int columns, int start, int count)
        {
            var vertices = new Vector3[rings * count];
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
                    if (i == rings - 1 || c == count - 1) continue;
                    triangles[t++] = v; triangles[t++] = v + count; triangles[t++] = v + 1;
                    triangles[t++] = v + 1; triangles[t++] = v + count; triangles[t++] = v + count + 1;
                }
            }
            var mesh = new Mesh { name = "Env_Range_" + start };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.bounds = SqueezedBounds(vertices);
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Mesh Clouds(float[] cards)
        {
            int count = cards.Length / 5;
            var vertices = new Vector3[count * 4];
            var uv = new Vector2[vertices.Length];
            var seed = new Vector2[vertices.Length];
            var triangles = new int[count * 6];
            var random = new System.Random(1972);
            for (int k = 0; k < count; k++)
            {
                float a = cards[k * 5] * Mathf.Deg2Rad, r = cards[k * 5 + 1], h = cards[k * 5 + 2];
                float halfWidth = cards[k * 5 + 3] * .5f, halfTall = cards[k * 5 + 4] * .5f;
                var centre = new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r);
                var side = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)) * halfWidth;   // faces the station
                var up = Vector3.up * halfTall;
                var s = new Vector2((float)random.NextDouble(), .7f + (float)random.NextDouble() * .6f);
                int v = k * 4;
                vertices[v] = centre - side - up; vertices[v + 1] = centre + side - up;
                vertices[v + 2] = centre - side + up; vertices[v + 3] = centre + side + up;
                uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(1, 0); uv[v + 2] = new Vector2(0, 1); uv[v + 3] = new Vector2(1, 1);
                seed[v] = seed[v + 1] = seed[v + 2] = seed[v + 3] = s;
                int tr = k * 6;
                triangles[tr] = v; triangles[tr + 1] = v + 2; triangles[tr + 2] = v + 1;
                triangles[tr + 3] = v + 1; triangles[tr + 4] = v + 2; triangles[tr + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Env_MountainClouds" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.uv2 = seed;
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
                float d = view.magnitude, s = d <= Squeeze.x ? d : Squeeze.x + Squeeze.y * Mathf.Log(1f + (d - Squeeze.x) / Squeeze.y);
                bounds.Encapsulate(eye + view * (s / Mathf.Max(d, 1e-3f)));
            }
            bounds.Expand(60f);
            return bounds;
        }

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
