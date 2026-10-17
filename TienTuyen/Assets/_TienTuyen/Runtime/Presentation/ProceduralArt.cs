using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Runtime-generated low-poly art: faceted flora, rock and structure meshes,
    /// tileable ground textures and particle materials. Everything is deterministic and uses its
    /// own System.Random so cosmetic generation never touches UnityEngine.Random.
    /// </summary>
    internal static class ProceduralArt
    {
        // ---------- Materials ----------

        public static Material Lit(string name, Color color, float smoothness = .08f, bool doubleSided = false,
            Texture2D albedo = null, Texture2D normal = null, float normalStrength = 1f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, enableInstancing = true };
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            if (albedo != null)
            {
                material.mainTexture = albedo;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
            }
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", normalStrength);
                material.EnableKeyword("_NORMALMAP");
            }
            if (doubleSided)
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.doubleSidedGI = true;
            }
            return material;
        }

        public static Material Emissive(string name, Color color, float intensity)
        {
            var material = Lit(name, color, .3f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * intensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return material;
        }

        /// <summary>
        /// Material on one of the environment shaders, or null when that shader is
        /// unavailable so callers can fall back to URP Lit.
        /// </summary>
        public static Material Environment(string shaderName, string name)
        {
            var shader = Shader.Find("TienTuyen/Environment/" + shaderName);
            if (shader == null || !shader.isSupported) return null;
            return new Material(shader) { name = name, enableInstancing = true };
        }

        /// <summary>Boulder rock: grey scanned stone, moss on the faces that look up, laterite dust at the foot.</summary>
        public static Material Rock(string name)
        {
            var material = Surface(name, "Rock", 1.6f);
            if (material == null) return Lit(name, Hex("#77736A"), .06f);
            material.SetColor("_BaseColor", Hex("#A8A49A"));
            material.SetFloat("_Saturation", .35f);
            material.SetFloat("_Moss", 1f);
            material.SetVector("_DustBand", new Vector4(.36f, .46f, .45f, 0f));
            return material;
        }

        /// <summary>Burnt bark of the dead trees: charcoal and ash, pale wood where the trunk broke off.</summary>
        public static Material CharredBark(string name)
        {
            var material = Surface(name, "Bark", 1.3f);
            if (material == null) return Lit(name, Hex("#3A3632"), .1f);
            material.SetFloat("_Burn", 1f);
            // A lone snag in open ground: no forest-floor darkening towards its foot.
            material.SetFloat("_Occlusion", 0f);
            material.SetVector("_DustBand", new Vector4(0f, .06f, .4f, 0f));
            return material;
        }

        /// <summary>
        /// EnvSurface material on a scanned set in Resources/Environment/Surface, projected along the world axes
        /// (the batched flora meshes have no UVs), or null when the shader is unavailable.
        /// </summary>
        private static Material Surface(string name, string set, float tiling)
        {
            var material = Environment("Surface", name);
            if (material == null) return null;
            foreach (var map in new[] { "Albedo", "Normal", "Mask" })
                material.SetTexture("_" + map + "Map", Resources.Load<Texture2D>("Environment/Surface/" + set + "_" + map));
            material.SetFloat("_Tiling", tiling);
            return material;
        }

        private static Texture2D softDot;

        /// <summary>URP particle material; additive for light, alpha blended for dust and smoke.</summary>
        public static Material Particle(string name, bool additive, Color tint)
        {
            // From the transparent material asset where it exists: a material made only in code leaves the
            // transparent shader variant out of the Player build.
            var template = Resources.Load<Material>("Blood/BloodParticleUnlit");
            var material = template != null ? new Material(template) { name = name }
                : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            material.SetTexture("_BaseMap", SoftDot());
            material.mainTexture = SoftDot();
            material.SetColor("_BaseColor", tint);
            material.color = tint;
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        public static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int size = 64;
            softDot = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            softDot.SetPixels32(pixels);
            softDot.Apply(true, true);
            return softDot;
        }

        /// <summary>
        /// Runs <paramref name="row"/> for 0..count-1 on dedicated worker threads and
        /// waits. The thread pool ramps up too slowly for a one-off startup bake.
        /// <paramref name="row"/> must be thread-safe (no Unity object API).
        /// </summary>
        public static void ParallelRows(int count, Action<int> row)
        {
            int workers = Mathf.Clamp(SystemInfo.processorCount - 1, 1, 16);
#if UNITY_WEBGL && !UNITY_EDITOR
            workers = 1;
#endif
            if (workers == 1 || count < 32)
            {
                for (int i = 0; i < count; i++) row(i);
                return;
            }
            int next = -1;
            Exception failure = null;
            var threads = new System.Threading.Thread[workers];
            for (int w = 0; w < workers; w++)
            {
                threads[w] = new System.Threading.Thread(() =>
                {
                    try
                    {
                        int i;
                        while ((i = System.Threading.Interlocked.Increment(ref next)) < count) row(i);
                    }
                    catch (Exception e) { failure = e; }
                }) { IsBackground = true, Name = "ProceduralArtBake" };
                threads[w].Start();
            }
            foreach (var thread in threads) thread.Join();
            if (failure != null) throw failure;
        }

        // ---------- Noise ----------

        public static float Fbm(float x, float z, int octaves = 4)
        {
            float sum = 0, amplitude = .5f, total = 0, frequency = 1f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Mathf.PerlinNoise(x * frequency + o * 17.3f, z * frequency - o * 9.1f) * amplitude;
                total += amplitude;
                amplitude *= .5f;
                frequency *= 2.03f;
            }
            return sum / total;
        }

        public static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var c); return c; }

        // ---------- Flora ----------

        /// <summary>Material slots of the procedural flora: EnvironmentFlora's boulders and dead trees.</summary>
        public enum Slot
        {
            Rock, Charred, Count
        }

        /// <summary>A shattered, charred trunk with a few bare limbs: the bombed slope.</summary>
        public static void DeadTreeInto(FlatMeshBuilder b, System.Random random, Vector3 origin, float height)
        {
            b.Begin(origin.y, height, .08f, (float)random.NextDouble());
            var lean = new Vector3(Range(random, -1f, 1f), 0, Range(random, -1f, 1f)) * height * .08f;
            const int segments = 6;
            var spine = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                spine[i] = origin + lean * t + Vector3.up * height * t + new Vector3(Range(random, -.08f, .08f), 0, Range(random, -.08f, .08f)) * height * t;
            }
            float girth = Mathf.Clamp(height * .045f, .18f, .5f);
            var rim = Tube(b, (int)Slot.Charred, spine, t => Mathf.Lerp(girth, girth * .5f, t), 6, true);
            // Splintered top: the broken face and its shards are above the bark's exposed-wood height (EnvSurface).
            Vector3 top = spine[segments], face = top + Vector3.up * girth * .15f;
            for (int i = 0; i < rim.Length; i++)
            {
                Vector3 p = rim[i], q = rim[(i + 1) % rim.Length];
                if (Vector3.Cross(q - p, face - p).y < 0) (p, q) = (q, p);
                b.Triangle((int)Slot.Charred, p, q, face, false);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.57f + Range(random, -.4f, .4f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * girth * .5f;
                Vector3 p = top + d, q = top + d * .2f + Vector3.up * Range(random, .4f, 1.2f), r = top - new Vector3(-d.z, 0, d.x);
                b.Triangle((int)Slot.Charred, p, q, r, false);
                b.Triangle((int)Slot.Charred, p, r, q, false);
            }
            int limbs = random.Next(1, 4);
            for (int i = 0; i < limbs; i++)
            {
                int at = random.Next(segments / 2, segments);
                float a = Range(random, 0, Mathf.PI * 2);
                var dir = new Vector3(Mathf.Cos(a), Range(random, .3f, 1f), Mathf.Sin(a)).normalized;
                Vector3 start = spine[at], end = start + dir * height * Range(random, .15f, .3f);
                Tube(b, (int)Slot.Charred, new[] { start, (start + end) * .5f + Vector3.up * .2f, end }, t => Mathf.Lerp(girth * .35f, girth * .08f, t), 5, true);
            }
        }

        public static void BoulderInto(FlatMeshBuilder b, System.Random random, Vector3 origin, Vector3 size)
        {
            b.Begin(origin.y - size.y * .5f, size.y * 1.4f, 0f, (float)random.NextDouble());
            var mesh = Icosphere();
            var offset = new Vector3(Range(random, 0, 50), Range(random, 0, 50), Range(random, 0, 50));
            var corners = new Vector3[mesh.Length];
            var faces = new Vector3[mesh.Length / 3];
            var rounded = new Dictionary<Vector3, Vector3>();
            for (int i = 0; i < mesh.Length; i++)
                corners[i] = origin + Vector3.Scale(Displace(mesh[i], offset, .28f), size);
            for (int f = 0; f < faces.Length; f++)
            {
                Vector3 a = corners[f * 3], c = corners[f * 3 + 1], d = corners[f * 3 + 2];
                faces[f] = Vector3.Cross(c - a, d - a).normalized;
                for (int k = 0; k < 3; k++)
                    rounded[corners[f * 3 + k]] = (rounded.TryGetValue(corners[f * 3 + k], out var sum) ? sum : Vector3.zero) + faces[f];
            }
            // Chipped rather than polished: each corner normal leans 60% of the way from its facet to the rounded stone.
            Vector3 Corner(int f, int k) => Vector3.Lerp(faces[f], rounded[corners[f * 3 + k]].normalized, .6f).normalized;
            for (int f = 0; f < faces.Length; f++)
                b.Triangle((int)Slot.Rock, corners[f * 3], corners[f * 3 + 1], corners[f * 3 + 2], Corner(f, 0), Corner(f, 1), Corner(f, 2));
        }

        private static Vector3 Displace(Vector3 point, Vector3 offset, float amount)
        {
            float n = Mathf.PerlinNoise(point.x * 1.7f + offset.x + point.y * .9f, point.z * 1.7f + offset.z - point.y * .7f);
            return point * (1f + (n - .5f) * 2f * amount);
        }

        private static Vector3[] icosphere;

        /// <summary>Once-subdivided icosahedron as a flat triangle list (80 faces).</summary>
        private static Vector3[] Icosphere()
        {
            if (icosphere != null) return icosphere;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var result = new List<Vector3>();
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = v[f[i]].normalized, b = v[f[i + 1]].normalized, c = v[f[i + 2]].normalized;
                Vector3 ab = ((a + b) * .5f).normalized, bc = ((b + c) * .5f).normalized, ca = ((c + a) * .5f).normalized;
                result.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            icosphere = result.ToArray();
            return icosphere;
        }

        /// <summary>
        /// Faceted tube along a spine with a radius profile over t in [0, 1]; returns its last ring. A smooth tube
        /// shades round over the same faceted shape (vertex normals point out from the spine).
        /// </summary>
        public static Vector3[] Tube(FlatMeshBuilder b, int slot, Vector3[] spine, Func<float, float> radius, int sides, bool smooth = false)
        {
            Vector3[] previous = null, previousOut = null;
            for (int i = 0; i < spine.Length; i++)
            {
                float t = spine.Length == 1 ? 0 : (float)i / (spine.Length - 1);
                Vector3 forward = i < spine.Length - 1 ? spine[i + 1] - spine[i] : spine[i] - spine[i - 1];
                forward.Normalize();
                Vector3 right = Vector3.Cross(forward, Mathf.Abs(forward.y) > .95f ? Vector3.right : Vector3.up).normalized;
                if (Mathf.Abs(forward.y) > .95f) right = Vector3.Cross(Vector3.forward, forward).normalized;
                Vector3 up = Vector3.Cross(right, forward);
                var ring = new Vector3[sides];
                var outward = new Vector3[sides];
                float r = radius(t);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2 / sides;
                    outward[s] = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    ring[s] = spine[i] + outward[s] * r;
                }
                if (previous != null)
                    for (int s = 0; s < sides; s++)
                    {
                        int n = (s + 1) % sides;
                        if (!smooth)
                        {
                            b.Quad(slot, previous[s], ring[s], ring[n], previous[n], false);
                            continue;
                        }
                        b.Triangle(slot, previous[s], ring[s], ring[n], previousOut[s], outward[s], outward[n]);
                        b.Triangle(slot, previous[s], ring[n], previous[n], previousOut[s], outward[n], previousOut[n]);
                    }
                previous = ring;
                previousOut = outward;
            }
            return previous;
        }

        public static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }

    /// <summary>
    /// Accumulates flat-shaded triangles per material slot. Also used as a
    /// static batcher: whole instances are appended in world space so a jungle
    /// of hundreds of plants renders as a handful of meshes. Each vertex carries
    /// colour for the environment shaders: R wind weight, G per-instance tint,
    /// B height above the instance base (occlusion), A translucency.
    /// </summary>
    internal sealed class FlatMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<List<int>> slots = new List<List<int>>();
        private float baseY, height = 1f, flex, tint;

        public int VertexCount => vertices.Count;

        /// <summary>Starts a new instance: wind and occlusion are measured from its base over its height.</summary>
        public void Begin(float instanceBaseY, float instanceHeight, float windFlex, float instanceTint)
        {
            baseY = instanceBaseY;
            height = Mathf.Max(.05f, instanceHeight);
            flex = windFlex;
            tint = instanceTint;
        }

        public void Triangle(int slot, Vector3 a, Vector3 b, Vector3 c, bool foliage, Vector3? forcedNormal = null)
        {
            Vector3 normal = forcedNormal ?? Vector3.Cross(b - a, c - a).normalized;
            if (foliage && forcedNormal == null)
            {
                // Two-sided leaves: light both faces like the sky-facing side.
                if (normal.y < 0) normal = -normal;
                normal = (normal * .45f + Vector3.up * .55f).normalized;
            }
            Add(slot, a, b, c, normal, normal, normal, foliage ? (byte)255 : (byte)0);
        }

        /// <summary>A solid triangle with its own normal at each corner, for surfaces that shade smooth.</summary>
        public void Triangle(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 normalA, Vector3 normalB, Vector3 normalC) =>
            Add(slot, a, b, c, normalA, normalB, normalC, 0);

        private void Add(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 normalA, Vector3 normalB, Vector3 normalC, byte alpha)
        {
            var list = Slot(slot);
            list.Add(vertices.Count); vertices.Add(a); normals.Add(normalA); colors.Add(Paint(a, alpha));
            list.Add(vertices.Count); vertices.Add(b); normals.Add(normalB); colors.Add(Paint(b, alpha));
            list.Add(vertices.Count); vertices.Add(c); normals.Add(normalC); colors.Add(Paint(c, alpha));
        }

        private Color32 Paint(Vector3 p, byte alpha)
        {
            float h = Mathf.Clamp01((p.y - baseY) / height);
            return new Color32((byte)(Mathf.Clamp01(h * h * flex) * 255f), (byte)(tint * 255f), (byte)(h * 255f), alpha);
        }

        public void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool foliage, Vector3? forcedNormal = null)
        {
            Triangle(slot, a, b, c, foliage, forcedNormal);
            Triangle(slot, a, c, d, foliage, forcedNormal);
        }

        private List<int> Slot(int index)
        {
            while (slots.Count <= index) slots.Add(new List<int>());
            return slots[index];
        }

        /// <summary>Returns one mesh per non-empty slot so each can take its own material.</summary>
        public void Emit(Transform parent, string name, Material[] materials, bool castShadows, int layer = 0)
        {
            for (int slot = 0; slot < slots.Count; slot++)
            {
                var indices = slots[slot];
                if (indices.Count == 0) continue;
                // Faceted triangles never share vertices, so each slot is a straight copy.
                var v = new Vector3[indices.Count];
                var n = new Vector3[indices.Count];
                var col = new Color32[indices.Count];
                var tris = new int[indices.Count];
                for (int i = 0; i < indices.Count; i++)
                {
                    int source = indices[i];
                    v[i] = vertices[source];
                    n[i] = normals[source];
                    col[i] = colors[source];
                    tris[i] = i;
                }
                var mesh = new Mesh { name = name + "_" + slot, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetColors(col);
                mesh.SetTriangles(tris, 0, true);
                // Wind sway moves leaves outside their rest bounds.
                var bounds = mesh.bounds;
                bounds.Expand(1f);
                mesh.bounds = bounds;
                mesh.UploadMeshData(true);
                var go = new GameObject(name + "_" + (ProceduralArt.Slot)slot, typeof(MeshFilter), typeof(MeshRenderer));
                go.layer = layer;
                go.transform.SetParent(parent, false);
                go.isStatic = true;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[Mathf.Min(slot, materials.Length - 1)];
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }
        }
    }
}
