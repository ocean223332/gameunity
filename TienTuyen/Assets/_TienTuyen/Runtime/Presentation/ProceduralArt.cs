using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Runtime-generated low-poly art: faceted flora meshes, tileable ground
    /// textures and particle materials. Everything is deterministic and uses its
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

        private static Texture2D softDot;

        /// <summary>URP particle material; additive for light, alpha blended for dust and smoke.</summary>
        public static Material Particle(string name, bool additive, Color tint)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = name };
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

        // ---------- Noise ----------

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Value noise that tiles every <paramref name="period"/> lattice cells.</summary>
        private static float PeriodicNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static int Mod(int value, int period) { int m = value % period; return m < 0 ? m + period : m; }

        private static float TileFbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0, amplitude = .5f, total = 0;
            int period = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += PeriodicNoise(u * period, v * period, period, seed + o * 31) * amplitude;
                total += amplitude;
                amplitude *= .5f;
                period *= 2;
            }
            return sum / total;
        }

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

        // ---------- Ground textures ----------

        public static void Laterite(int size, out Texture2D albedo, out Texture2D normal)
        {
            var height = new float[size * size];
            var colors = new Color32[size * size];
            Color deep = Hex("#673825"), warm = Hex("#8E5638"), dust = Hex("#A27658"), pebble = Hex("#A88A6E"), dark = Hex("#4E2C1C");
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float broad = TileFbm(u, v, 3, 5, 11);
                float fine = TileFbm(u, v, 24, 3, 23);
                float cells = PeriodicNoise(u * 48, v * 48, 48, 41);
                float streak = Mathf.Abs(TileFbm(u, v, 6, 3, 57) - .5f);
                Color c = Color.Lerp(deep, warm, Mathf.SmoothStep(.25f, .75f, broad));
                c = Color.Lerp(c, dust, Mathf.Clamp01((fine - .55f) * 2.2f) * .35f);
                float h = broad * .5f + fine * .3f;
                if (cells > .86f)
                {
                    float t = Mathf.InverseLerp(.86f, .97f, cells);
                    c = Color.Lerp(c, pebble, t * .45f);
                    h += t * .4f;
                }
                if (streak < .018f)
                {
                    c = Color.Lerp(c, dark, (1 - streak / .018f) * .4f);
                    h -= .25f * (1 - streak / .018f);
                }
                height[y * size + x] = h;
                colors[y * size + x] = c;
            }
            albedo = Finish("Laterite", size, colors);
            normal = NormalFromHeight("LateriteNormal", size, height, 3.2f);
        }

        public static void JungleFloor(int size, out Texture2D albedo, out Texture2D normal)
        {
            var height = new float[size * size];
            var colors = new Color32[size * size];
            Color moss = Hex("#40592F"), olive = Hex("#62733A"), litter = Hex("#735A38"), dry = Hex("#8E7C4A"), soil = Hex("#4E3C28");
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float broad = TileFbm(u, v, 4, 4, 101);
                float patch = TileFbm(u, v, 8, 3, 131);
                float leaf = PeriodicNoise(u * 96, v * 96, 96, 151);
                float leaf2 = PeriodicNoise(u * 64 + 7, v * 64, 64, 173);
                Color c = Color.Lerp(moss, olive, Mathf.SmoothStep(.3f, .7f, broad));
                c = Color.Lerp(c, litter, Mathf.SmoothStep(.55f, .8f, patch) * .8f);
                c = Color.Lerp(c, soil, Mathf.SmoothStep(.62f, .32f, broad) * .35f);
                float h = broad * .4f + patch * .2f;
                if (leaf > .74f) { c = Color.Lerp(c, dry, .5f); h += .25f; }
                else if (leaf2 > .78f) { c = Color.Lerp(c, olive * 1.2f, .45f); h += .2f; }
                height[y * size + x] = h;
                colors[y * size + x] = c;
            }
            albedo = Finish("JungleFloor", size, colors);
            normal = NormalFromHeight("JungleFloorNormal", size, height, 2.4f);
        }

        private static Texture2D Finish(string name, int size, Color32[] colors)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            texture.SetPixels32(colors);
            texture.Apply(true, true);
            return texture;
        }

        private static Texture2D NormalFromHeight(string name, int size, float[] height, float strength)
        {
            var colors = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float left = height[y * size + Mod(x - 1, size)], right = height[y * size + Mod(x + 1, size)];
                float down = height[Mod(y - 1, size) * size + x], up = height[Mod(y + 1, size) * size + x];
                var n = new Vector3((left - right) * strength, (down - up) * strength, 1f).normalized;
                // URP unpacks DXT5nm-style AG on desktop: X lives in alpha, Y in green, R must be 1.
                colors[y * size + x] = new Color32(255, (byte)((n.y * .5f + .5f) * 255), 0, (byte)((n.x * .5f + .5f) * 255));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            texture.SetPixels32(colors);
            texture.Apply(true, true);
            return texture;
        }

        public static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var c); return c; }

        // ---------- Flora ----------

        /// <summary>Material slots shared by every flora template.</summary>
        public enum Slot { Bark, PalmBark, LeafDark, LeafMid, LeafLight, Bamboo, Rock, Moss, GrassA, GrassB, Banana, Stem, DeadLeaf, Count }

        public static void PalmInto(FlatMeshBuilder b, System.Random random, Vector3 origin)
        {
            float height = Range(random, 6.2f, 10.5f), lean = Range(random, .6f, 2.4f);
            float leanAngle = Range(random, 0, Mathf.PI * 2);
            var leanDirection = new Vector3(Mathf.Cos(leanAngle), 0, Mathf.Sin(leanAngle));
            const int segments = 10;
            var spine = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                spine[i] = origin + leanDirection * (lean * t * t) + Vector3.up * (height * t);
            }
            Tube(b, (int)Slot.PalmBark, spine, t => Mathf.Lerp(.25f, .14f, t) * (1f + (Mathf.Repeat(t * segments, 1f) > .8f ? .12f : 0f)), 7);
            Vector3 crown = spine[segments];
            int fronds = random.Next(9, 13);
            for (int f = 0; f < fronds; f++)
            {
                float azimuth = f * Mathf.PI * 2 / fronds + Range(random, -.2f, .2f);
                bool dead = f % 5 == 4 && random.NextDouble() < .6;
                Frond(b, random, crown, azimuth, dead ? Range(random, -40f, -20f) : Range(random, 18f, 48f),
                    Range(random, 2.6f, 3.8f), dead ? (int)Slot.DeadLeaf : (f % 2 == 0 ? (int)Slot.LeafMid : (int)Slot.LeafDark), .85f);
            }
            // A few coconuts under the crown.
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + Range(random, 0, 1);
                Blob(b, random, (int)Slot.Stem, crown + new Vector3(Mathf.Cos(a) * .25f, -.3f, Mathf.Sin(a) * .25f), new Vector3(.17f, .19f, .17f), 0f);
            }
        }

        public static void FernInto(FlatMeshBuilder b, System.Random random, Vector3 origin, float scale)
        {
            int fronds = random.Next(7, 11);
            for (int f = 0; f < fronds; f++)
            {
                float azimuth = f * Mathf.PI * 2 / fronds + Range(random, -.3f, .3f);
                Frond(b, random, origin + Vector3.up * .05f, azimuth, Range(random, 30f, 60f), Range(random, .8f, 1.4f) * scale,
                    random.NextDouble() < .5 ? (int)Slot.LeafLight : (int)Slot.LeafMid, .55f);
            }
        }

        /// <summary>A drooping rib with paired leaflets, shared by palms and ferns.</summary>
        private static void Frond(FlatMeshBuilder b, System.Random random, Vector3 root, float azimuth, float elevationDegrees,
            float length, int slot, float leafletScale)
        {
            const int steps = 11;
            var direction = new Vector3(Mathf.Cos(azimuth), 0, Mathf.Sin(azimuth));
            var side = new Vector3(-direction.z, 0, direction.x);
            float elevation = elevationDegrees * Mathf.Deg2Rad, droop = Range(random, .9f, 1.4f);
            var rib = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float u = (float)i / steps;
                rib[i] = root + direction * (length * u * Mathf.Cos(elevation)) +
                         Vector3.up * (length * (u * Mathf.Sin(elevation) - droop * .55f * u * u));
            }
            for (int i = 1; i < steps; i++)
            {
                float u = (float)i / steps;
                float leaflet = (Mathf.Sin(u * Mathf.PI) * .72f + .12f) * length * .32f * leafletScale;
                Vector3 along = rib[i + 1] - rib[i];
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 tip = rib[i] + side * (s * leaflet) + along * .9f + Vector3.down * (leaflet * .55f);
                    b.Triangle(slot, rib[i], rib[i] + along * .55f, tip, true);
                }
            }
            for (int i = 0; i < steps; i++)
                b.Triangle(slot, rib[i], rib[i + 1], rib[i] + side * .03f, true);
        }

        public static void BananaInto(FlatMeshBuilder b, System.Random random, Vector3 origin, float scale)
        {
            float stem = Range(random, .7f, 1.3f) * scale;
            Tube(b, (int)Slot.Stem, new[] { origin, origin + Vector3.up * stem }, t => Mathf.Lerp(.12f, .08f, t) * scale, 6);
            int leaves = random.Next(5, 8);
            Vector3 top = origin + Vector3.up * stem;
            for (int l = 0; l < leaves; l++)
            {
                float azimuth = l * Mathf.PI * 2 / leaves + Range(random, -.35f, .35f);
                var direction = new Vector3(Mathf.Cos(azimuth), 0, Mathf.Sin(azimuth));
                var side = new Vector3(-direction.z, 0, direction.x);
                float length = Range(random, 1.5f, 2.3f) * scale, width = Range(random, .32f, .46f) * scale;
                float elevation = Range(random, 45f, 72f) * Mathf.Deg2Rad, droop = Range(random, .9f, 1.3f);
                const int steps = 8;
                Vector3 previousRib = top, previousLeft = top, previousRight = top;
                int slot = l % 3 == 0 ? (int)Slot.LeafLight : (int)Slot.Banana;
                for (int i = 1; i <= steps; i++)
                {
                    float u = (float)i / steps;
                    Vector3 rib = top + direction * (length * u * Mathf.Cos(elevation) * .9f) +
                                  Vector3.up * (length * (u * Mathf.Sin(elevation) - droop * .7f * u * u));
                    float w = width * Mathf.Pow(Mathf.Sin(Mathf.Min(1f, u * 1.12f) * Mathf.PI), .6f);
                    Vector3 fold = Vector3.down * (w * .28f);
                    Vector3 left = rib + side * w + fold, right = rib - side * w + fold;
                    b.Quad(slot, previousRib, rib, left, previousLeft, true);
                    b.Quad(slot, previousRight, right, rib, previousRib, true);
                    previousRib = rib; previousLeft = left; previousRight = right;
                }
            }
        }

        public static void BambooInto(FlatMeshBuilder b, System.Random random, Vector3 origin, float scale)
        {
            int culms = random.Next(6, 12);
            for (int c = 0; c < culms; c++)
            {
                float a = Range(random, 0, Mathf.PI * 2), r = Range(random, 0, .65f) * scale;
                Vector3 root = origin + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float height = Range(random, 5f, 8.5f) * scale, lean = Range(random, .05f, .28f);
                var outward = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                int nodes = Mathf.Max(6, Mathf.RoundToInt(height / .55f));
                var spine = new Vector3[nodes + 1];
                for (int i = 0; i <= nodes; i++)
                {
                    float t = (float)i / nodes;
                    spine[i] = root + outward * (height * lean * t * t) + Vector3.up * (height * t);
                }
                float radius = Range(random, .045f, .075f) * scale;
                Tube(b, (int)Slot.Bamboo, spine, t => radius * (Mathf.Repeat(t * nodes, 1f) < .08f ? 1.25f : 1f), 5);
                for (int i = nodes / 2; i < nodes; i += 2)
                {
                    Vector3 node = spine[i];
                    for (int k = 0; k < 5; k++)
                    {
                        float la = Range(random, 0, Mathf.PI * 2);
                        var dir = new Vector3(Mathf.Cos(la), Range(random, -.5f, .1f), Mathf.Sin(la)).normalized;
                        var perp = Vector3.Cross(dir, Vector3.up).normalized;
                        float length = Range(random, .3f, .5f) * scale;
                        b.Triangle(k % 2 == 0 ? (int)Slot.LeafLight : (int)Slot.LeafMid, node, node + dir * length * .45f + perp * .06f, node + dir * length, true);
                    }
                }
            }
        }

        public static void CanopyTreeInto(FlatMeshBuilder b, System.Random random, Vector3 origin, float scale)
        {
            float height = Range(random, 4f, 7f) * scale;
            var lean = new Vector3(Range(random, -.5f, .5f), 0, Range(random, -.5f, .5f)) * scale;
            Vector3 top = origin + Vector3.up * height + lean;
            Tube(b, (int)Slot.Bark, new[] { origin, origin + Vector3.up * height * .5f + lean * .3f, top },
                t => Mathf.Lerp(.42f, .2f, t) * scale, 7);
            // Buttress fins at the base.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f + Range(random, -.3f, .3f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Triangle((int)Slot.Bark, origin + d * .9f * scale, origin + Vector3.up * 1.3f * scale + d * .2f * scale, origin - d * .05f, true);
            }
            int blobs = random.Next(3, 6);
            for (int i = 0; i < blobs; i++)
            {
                float a = Range(random, 0, Mathf.PI * 2), r = Range(random, .4f, 1.6f) * scale;
                Vector3 center = top + new Vector3(Mathf.Cos(a) * r, Range(random, -.5f, 1.2f) * scale, Mathf.Sin(a) * r);
                float size = Range(random, 1.5f, 2.5f) * scale;
                int slot = i == 0 ? (int)Slot.LeafDark : random.NextDouble() < .5 ? (int)Slot.LeafMid : (int)Slot.LeafDark;
                Blob(b, random, slot, center, new Vector3(size, size * .62f, size), .22f);
                Tube(b, (int)Slot.Bark, new[] { top - Vector3.up * .6f * scale, center - Vector3.up * size * .3f }, t => .1f * scale, 5);
            }
        }

        public static void BoulderInto(FlatMeshBuilder b, System.Random random, Vector3 origin, Vector3 size)
        {
            var mesh = Icosphere();
            var offset = new Vector3(Range(random, 0, 50), Range(random, 0, 50), Range(random, 0, 50));
            for (int i = 0; i < mesh.Length; i += 3)
            {
                Vector3 a = Displace(mesh[i], offset, .28f), c = Displace(mesh[i + 1], offset, .28f), d = Displace(mesh[i + 2], offset, .28f);
                a = origin + Vector3.Scale(a, size); c = origin + Vector3.Scale(c, size); d = origin + Vector3.Scale(d, size);
                Vector3 normal = Vector3.Cross(c - a, d - a).normalized;
                b.Triangle(normal.y > .62f ? (int)Slot.Moss : (int)Slot.Rock, a, c, d, false);
            }
        }

        public static void GrassTuftInto(FlatMeshBuilder b, System.Random random, Vector3 origin, float scale)
        {
            int blades = random.Next(5, 10);
            int slot = random.NextDouble() < .5 ? (int)Slot.GrassA : (int)Slot.GrassB;
            for (int i = 0; i < blades; i++)
            {
                float a = Range(random, 0, Mathf.PI * 2), r = Range(random, 0, .14f) * scale;
                Vector3 root = origin + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                var outward = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = new Vector3(-outward.z, 0, outward.x) * (.035f * scale);
                float height = Range(random, .22f, .6f) * scale;
                Vector3 middle = root + Vector3.up * height * .55f + outward * height * .12f;
                Vector3 tip = root + Vector3.up * height + outward * height * Range(random, .2f, .5f);
                b.Quad(slot, root - side, root + side, middle + side * .6f, middle - side * .6f, true, Vector3.up);
                b.Triangle(slot, middle - side * .6f, middle + side * .6f, tip, true, Vector3.up);
            }
        }

        /// <summary>A faceted, noise-displaced blob such as a canopy clump or a coconut.</summary>
        private static void Blob(FlatMeshBuilder b, System.Random random, int slot, Vector3 center, Vector3 size, float roughness)
        {
            var mesh = Icosphere();
            var offset = new Vector3(Range(random, 0, 50), Range(random, 0, 50), Range(random, 0, 50));
            for (int i = 0; i < mesh.Length; i += 3)
                b.Triangle(slot, center + Vector3.Scale(Displace(mesh[i], offset, roughness), size * .5f),
                    center + Vector3.Scale(Displace(mesh[i + 1], offset, roughness), size * .5f),
                    center + Vector3.Scale(Displace(mesh[i + 2], offset, roughness), size * .5f), false);
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

        /// <summary>Faceted tube along a spine with a radius profile over t in [0, 1].</summary>
        public static void Tube(FlatMeshBuilder b, int slot, Vector3[] spine, Func<float, float> radius, int sides)
        {
            Vector3[] previous = null;
            for (int i = 0; i < spine.Length; i++)
            {
                float t = spine.Length == 1 ? 0 : (float)i / (spine.Length - 1);
                Vector3 forward = i < spine.Length - 1 ? spine[i + 1] - spine[i] : spine[i] - spine[i - 1];
                forward.Normalize();
                Vector3 right = Vector3.Cross(forward, Mathf.Abs(forward.y) > .95f ? Vector3.right : Vector3.up).normalized;
                if (Mathf.Abs(forward.y) > .95f) right = Vector3.Cross(Vector3.forward, forward).normalized;
                Vector3 up = Vector3.Cross(right, forward);
                var ring = new Vector3[sides];
                float r = radius(t);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2 / sides;
                    ring[s] = spine[i] + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * r;
                }
                if (previous != null)
                    for (int s = 0; s < sides; s++)
                    {
                        int n = (s + 1) % sides;
                        b.Quad(slot, previous[s], ring[s], ring[n], previous[n], false);
                    }
                previous = ring;
            }
        }

        public static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }

    /// <summary>
    /// Accumulates flat-shaded triangles per material slot. Also used as a
    /// static batcher: whole instances are appended in world space so a jungle
    /// of hundreds of plants renders as a handful of meshes.
    /// </summary>
    internal sealed class FlatMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<List<int>> slots = new List<List<int>>();

        public int VertexCount => vertices.Count;

        public void Triangle(int slot, Vector3 a, Vector3 b, Vector3 c, bool foliage, Vector3? forcedNormal = null)
        {
            Vector3 normal = forcedNormal ?? Vector3.Cross(b - a, c - a).normalized;
            if (foliage && forcedNormal == null)
            {
                // Two-sided leaves: light both faces like the sky-facing side.
                if (normal.y < 0) normal = -normal;
                normal = (normal * .45f + Vector3.up * .55f).normalized;
            }
            var list = Slot(slot);
            list.Add(vertices.Count); vertices.Add(a); normals.Add(normal);
            list.Add(vertices.Count); vertices.Add(b); normals.Add(normal);
            list.Add(vertices.Count); vertices.Add(c); normals.Add(normal);
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
                var uv = new Vector2[indices.Count];
                var tris = new int[indices.Count];
                for (int i = 0; i < indices.Count; i++)
                {
                    int source = indices[i];
                    v[i] = vertices[source];
                    n[i] = normals[source];
                    uv[i] = new Vector2(v[i].x, v[i].z) * .25f;
                    tris[i] = i;
                }
                var mesh = new Mesh { name = name + "_" + slot, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(tris, 0, true);
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

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = slots.Count;
            for (int i = 0; i < slots.Count; i++) mesh.SetTriangles(slots[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
