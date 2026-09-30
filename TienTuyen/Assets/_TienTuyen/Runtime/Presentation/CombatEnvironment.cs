using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Slot = TienTuyen.Presentation.ProceduralArt.Slot;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Presentation-only world around the combat clearing: late-afternoon light,
    /// sky, haze, post-processing, a laterite clearing in jungle-covered hills and
    /// set dressing. Nothing here has colliders or affects the simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatEnvironment : MonoBehaviour
    {
        // Mirrors CombatGame's movement clamp.
        public const float ArenaHalfX = 16.55f, ArenaHalfZ = 9.55f;
        private const float FlatMargin = 3.5f;
        private const int Sectors = 8;

        private Material[] flora;
        private Material laterite, jungleFloor, mud, rut;
        private readonly System.Random random = new System.Random(1972);

        /// <summary>Distance from the arena rectangle; zero inside it.</summary>
        public static float OutsideDistance(float x, float z)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x) - ArenaHalfX), dz = Mathf.Max(0, Mathf.Abs(z) - ArenaHalfZ);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Visual ground height: a flat clearing rising into jungle hills and far ridges.</summary>
        public static float GroundHeight(float x, float z)
        {
            float d = OutsideDistance(x, z) - FlatMargin;
            if (d <= 0) return 0;
            float near = Mathf.SmoothStep(0, 1, d / 26f) * (1.2f + ProceduralArt.Fbm(x * .045f, z * .045f, 3) * 7f);
            float far = Mathf.SmoothStep(0, 1, (d - 28f) / 70f) * (8f + ProceduralArt.Fbm(x * .012f + 40f, z * .012f, 4) * 42f);
            return near + far;
        }

        public void Build(Camera view)
        {
            CreateMaterials();
            SetupLighting();
            SetupSky(view);
            SetupPostProcessing();
            var root = new GameObject("Environment").transform;
            root.SetParent(transform, false);
            BuildTerrain(root);
            BuildClearing(root);
            BuildVegetation(root);
            BuildDressing(root);
            BuildAtmosphere(root);
        }

        private void CreateMaterials()
        {
            ProceduralArt.Laterite(512, out var lateriteAlbedo, out var lateriteNormal);
            ProceduralArt.JungleFloor(512, out var floorAlbedo, out var floorNormal);
            laterite = ProceduralArt.Lit("Env_Laterite", Color.white, .12f, false, lateriteAlbedo, lateriteNormal, .9f);
            jungleFloor = ProceduralArt.Lit("Env_JungleFloor", Color.white, .06f, false, floorAlbedo, floorNormal, .8f);
            mud = ProceduralArt.Lit("Env_Mud", ProceduralArt.Hex("#5A3423"), .32f);
            rut = ProceduralArt.Lit("Env_Rut", ProceduralArt.Hex("#6A3C27"), .1f);
            flora = new Material[(int)Slot.Count];
            flora[(int)Slot.Bark] = ProceduralArt.Lit("Flora_Bark", ProceduralArt.Hex("#5B4636"), .05f);
            flora[(int)Slot.PalmBark] = ProceduralArt.Lit("Flora_PalmBark", ProceduralArt.Hex("#7B6A55"), .05f);
            flora[(int)Slot.LeafDark] = ProceduralArt.Lit("Flora_LeafDark", ProceduralArt.Hex("#2C4A26"), .12f, true);
            flora[(int)Slot.LeafMid] = ProceduralArt.Lit("Flora_LeafMid", ProceduralArt.Hex("#3F6B2E"), .14f, true);
            flora[(int)Slot.LeafLight] = ProceduralArt.Lit("Flora_LeafLight", ProceduralArt.Hex("#6E8C35"), .14f, true);
            flora[(int)Slot.Bamboo] = ProceduralArt.Lit("Flora_Bamboo", ProceduralArt.Hex("#8C9A48"), .25f);
            flora[(int)Slot.Rock] = ProceduralArt.Lit("Flora_Rock", ProceduralArt.Hex("#6F6C63"), .1f);
            flora[(int)Slot.Moss] = ProceduralArt.Lit("Flora_Moss", ProceduralArt.Hex("#4F6331"), .05f);
            flora[(int)Slot.GrassA] = ProceduralArt.Lit("Flora_GrassA", ProceduralArt.Hex("#5B7A2C"), .1f, true);
            flora[(int)Slot.GrassB] = ProceduralArt.Lit("Flora_GrassB", ProceduralArt.Hex("#7E8A3A"), .1f, true);
            flora[(int)Slot.Banana] = ProceduralArt.Lit("Flora_Banana", ProceduralArt.Hex("#4E8A32"), .22f, true);
            flora[(int)Slot.Stem] = ProceduralArt.Lit("Flora_Stem", ProceduralArt.Hex("#556B2B"), .1f);
            flora[(int)Slot.DeadLeaf] = ProceduralArt.Lit("Flora_DeadLeaf", ProceduralArt.Hex("#8A6A3A"), .05f, true);
        }

        // ---------- Light, sky, haze, grading ----------

        private static void SetupLighting()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) { sun = light; break; }
            if (sun == null)
            {
                sun = new GameObject("CombatSun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            // Low late-afternoon sun: long shadows across the clearing.
            sun.transform.rotation = Quaternion.Euler(31f, -142f, 0f);
            sun.color = ProceduralArt.Hex("#FFD9A6");
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .88f;
            sun.shadowNormalBias = .6f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ProceduralArt.Hex("#8FA7B2");
            RenderSettings.ambientEquatorColor = ProceduralArt.Hex("#7D8461");
            RenderSettings.ambientGroundColor = ProceduralArt.Hex("#3F3324");
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0115f;
            RenderSettings.fogColor = ProceduralArt.Hex("#B4BDA6");
        }

        private static void SetupSky(Camera view)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                if (view != null) { view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = RenderSettings.fogColor; }
                return;
            }
            var sky = new Material(shader) { name = "Env_Sky" };
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", .05f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            sky.SetFloat("_AtmosphereThickness", 1.35f);
            sky.SetColor("_SkyTint", ProceduralArt.Hex("#8C9CA0"));
            sky.SetColor("_GroundColor", ProceduralArt.Hex("#6B7361"));
            sky.SetFloat("_Exposure", 1.05f);
            RenderSettings.skybox = sky;
            if (view != null) view.clearFlags = CameraClearFlags.Skybox;
        }

        private void SetupPostProcessing()
        {
            var volume = new GameObject("CombatPostProcess").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            volume.priority = 50;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CombatPostProfile";
            volume.sharedProfile = profile;
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.45f);
            color.contrast.Override(16f);
            color.saturation.Override(4f);
            color.colorFilter.Override(new Color(1f, .985f, .96f));
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(.7f);
            bloom.scatter.Override(.68f);
            bloom.tint.Override(ProceduralArt.Hex("#FFE2B8"));
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.26f);
            vignette.smoothness.Override(.42f);
            var balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(2f);
            balance.tint.Override(2f);
            var toning = profile.Add<ShadowsMidtonesHighlights>(true);
            toning.shadows.Override(new Vector4(.93f, 1f, 1.04f, 0f));
            toning.highlights.Override(new Vector4(1.04f, 1f, .94f, 0f));
        }

        // ---------- Ground ----------

        private void BuildTerrain(Transform root)
        {
            const float extent = 170f, cell = 2.5f;
            int count = Mathf.RoundToInt(extent * 2 / cell);
            var heights = new float[(count + 1) * (count + 1)];
            for (int z = 0; z <= count; z++)
            for (int x = 0; x <= count; x++)
                heights[z * (count + 1) + x] = GroundHeight(-extent + x * cell, -extent + z * cell) - .04f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int z = 0; z < count; z++)
            for (int x = 0; x < count; x++)
            {
                Vector3 P(int ix, int iz) => new Vector3(-extent + ix * cell, heights[iz * (count + 1) + ix], -extent + iz * cell);
                Vector3 a = P(x, z), b = P(x, z + 1), c = P(x + 1, z + 1), d = P(x + 1, z);
                AddFlat(vertices, normals, uvs, triangles, a, b, c, 6f);
                AddFlat(vertices, normals, uvs, triangles, a, c, d, 6f);
            }
            var mesh = new Mesh { name = "Env_Terrain", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            Spawn(root, "Terrain", mesh, jungleFloor, false);
        }

        private static void AddFlat(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, float tile)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            // Keep the flat clearing smooth; facet only the hills.
            if (normal.y > .995f) normal = Vector3.up;
            foreach (var p in new[] { a, b, c })
            {
                t.Add(v.Count);
                v.Add(p);
                n.Add(normal);
                uv.Add(new Vector2(p.x, p.z) / tile);
            }
        }

        /// <summary>Radius of the laterite clearing along an angle: a noisy rounded rectangle.</summary>
        private static float ClearingRadius(float angle)
        {
            float a = ArenaHalfX + 2.6f, b = ArenaHalfZ + 2.4f, c = Mathf.Abs(Mathf.Cos(angle)), s = Mathf.Abs(Mathf.Sin(angle));
            float r = 1f / Mathf.Pow(Mathf.Pow(c / a, 5f) + Mathf.Pow(s / b, 5f), 1f / 5f);
            return r + (Mathf.PerlinNoise(Mathf.Cos(angle) * 2.2f + 5f, Mathf.Sin(angle) * 2.2f + 5f) - .5f) * 3.2f;
        }

        private void BuildClearing(Transform root)
        {
            Spawn(root, "LateriteClearing", Disc(Vector3.up * .004f, ClearingRadius, 160, 10, 5f, 0), laterite, false);
            // Grass islands and wet mud break up the clearing floor.
            var islands = new[] { new Vector3(-13.5f, 0, -1f), new Vector3(13.8f, 0, 1.5f), new Vector3(-6.5f, 0, -7.8f),
                new Vector3(6.2f, 0, 7.9f), new Vector3(-2f, 0, 8.4f), new Vector3(11.5f, 0, -7.6f), new Vector3(-12f, 0, -6.8f) };
            for (int i = 0; i < islands.Length; i++)
            {
                float size = ProceduralArt.Range(random, 1.4f, 2.6f);
                int seed = i * 13;
                Spawn(root, "GrassIsland", Disc(islands[i] + Vector3.up * .01f,
                    a => size * (.75f + Mathf.PerlinNoise(Mathf.Cos(a) * 1.8f + seed, Mathf.Sin(a) * 1.8f + seed) * .6f), 48, 3, 6f, seed), jungleFloor, false);
                for (int g = 0; g < 26; g++)
                {
                    float a = ProceduralArt.Range(random, 0, Mathf.PI * 2), r = ProceduralArt.Range(random, 0, size);
                    grass.Add(islands[i] + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                }
            }
            var puddles = new[] { new Vector3(-4.6f, 0, -1.2f), new Vector3(7.1f, 0, 2.6f), new Vector3(-9.8f, 0, 5.9f), new Vector3(1.8f, 0, -6.2f) };
            for (int i = 0; i < puddles.Length; i++)
            {
                float size = ProceduralArt.Range(random, .8f, 1.5f);
                int seed = 90 + i * 7;
                Spawn(root, "MudPatch", Disc(puddles[i] + Vector3.up * .014f,
                    a => size * (.7f + Mathf.PerlinNoise(Mathf.Cos(a) * 2f + seed, Mathf.Sin(a) * 2f + seed) * .7f), 40, 2, 3f, seed), mud, false);
            }
            // Truck ruts crossing the station from the western approach.
            BuildRut(root, new Vector3(-24f, 0, -5.2f), new Vector3(-6f, 0, -2.5f), new Vector3(8f, 0, 3.5f), new Vector3(24f, 0, 6.2f), 0f);
            BuildRut(root, new Vector3(-24f, 0, -3.5f), new Vector3(-6f, 0, -.8f), new Vector3(8f, 0, 5.2f), new Vector3(24f, 0, 7.9f), 3f);
        }

        private readonly List<Vector3> grass = new List<Vector3>();

        private void BuildRut(Transform root, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float seed)
        {
            const int steps = 64;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            Vector3 previousLeft = Vector3.zero, previousRight = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float u = (float)i / steps;
                Vector3 p = Bezier(p0, p1, p2, p3, u), tangent = (Bezier(p0, p1, p2, p3, Mathf.Min(1, u + .01f)) - Bezier(p0, p1, p2, p3, Mathf.Max(0, u - .01f))).normalized;
                Vector3 side = new Vector3(-tangent.z, 0, tangent.x);
                float width = .14f + Mathf.PerlinNoise(u * 9f + seed, seed) * .1f;
                // Ruts fade out where the clearing ends.
                float fade = Mathf.Clamp01(1f - OutsideDistance(p.x, p.z) / 5f);
                width *= fade;
                Vector3 left = p + side * width, right = p - side * width;
                left.y = right.y = GroundHeight(p.x, p.z) + .018f;
                if (i > 0)
                {
                    AddFlat(v, n, uv, t, previousLeft, left, right, 3f);
                    AddFlat(v, n, uv, t, previousLeft, right, previousRight, 3f);
                }
                previousLeft = left; previousRight = right;
            }
            var mesh = new Mesh { name = "Env_Rut" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            Spawn(root, "TruckRut", mesh, rut, false);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        /// <summary>A flat, smooth-shaded polygon disc with world-space UVs.</summary>
        private static Mesh Disc(Vector3 center, Func<float, float> radius, int segments, int rings, float tile, int seed)
        {
            var vertices = new List<Vector3> { center };
            for (int r = 1; r <= rings; r++)
            for (int s = 0; s < segments; s++)
            {
                float angle = s * Mathf.PI * 2 / segments;
                float distance = radius(angle) * r / rings;
                vertices.Add(center + new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance));
            }
            var triangles = new List<int>();
            for (int s = 0; s < segments; s++)
            {
                int n = (s + 1) % segments;
                triangles.AddRange(new[] { 0, 1 + n, 1 + s });
            }
            for (int r = 1; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int n = (s + 1) % segments;
                int a = 1 + (r - 1) * segments + s, b = 1 + (r - 1) * segments + n, c = 1 + r * segments + n, d = 1 + r * segments + s;
                triangles.AddRange(new[] { a, b, c, a, c, d });
            }
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            foreach (var p in vertices) { uvs.Add(new Vector2(p.x, p.z) / tile); normals.Add(Vector3.up); }
            var mesh = new Mesh { name = "Env_Disc_" + seed };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------- Jungle ----------

        private void BuildVegetation(Transform root)
        {
            var foliage = new FlatMeshBuilder[Sectors];
            var grassBatches = new FlatMeshBuilder[Sectors];
            for (int i = 0; i < Sectors; i++) { foliage[i] = new FlatMeshBuilder(); grassBatches[i] = new FlatMeshBuilder(); }
            FlatMeshBuilder SectorOf(FlatMeshBuilder[] set, Vector3 p) =>
                set[Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(p.z, p.x) + Mathf.PI) / (Mathf.PI * 2) * Sectors), 0, Sectors - 1)];

            // Undergrowth hugging the clearing: the jungle edge enemies emerge from.
            Scatter(150, 1.2f, 7f, p => ProceduralArt.BananaInto(SectorOf(foliage, p), random, p, ProceduralArt.Range(random, .85f, 1.25f)));
            Scatter(220, .8f, 9f, p => ProceduralArt.FernInto(SectorOf(foliage, p), random, p, ProceduralArt.Range(random, .8f, 1.4f)));
            Scatter(26, 3.5f, 12f, p => ProceduralArt.BambooInto(SectorOf(foliage, p), random, p, ProceduralArt.Range(random, .85f, 1.15f)));
            Scatter(22, 1.5f, 10f, p => ProceduralArt.BoulderInto(SectorOf(foliage, p), random, p - Vector3.up * .25f,
                new Vector3(ProceduralArt.Range(random, 1.2f, 2.6f), ProceduralArt.Range(random, .7f, 1.4f), ProceduralArt.Range(random, 1.1f, 2.2f))));
            // Tree wall.
            Scatter(190, 5.5f, 30f, p => ProceduralArt.PalmInto(SectorOf(foliage, p), random, p));
            Scatter(170, 7f, 34f, p => ProceduralArt.CanopyTreeInto(SectorOf(foliage, p), random, p, ProceduralArt.Range(random, 1f, 1.6f)));
            Scatter(160, 12f, 40f, p => ProceduralArt.BananaInto(SectorOf(foliage, p), random, p, ProceduralArt.Range(random, 1f, 1.5f)));
            // Hazy far slopes.
            Scatter(420, 30f, 120f, p => ProceduralArt.CanopyTreeInto(SectorOf(foliage, p), random, p, ProceduralArt.Range(random, 1.5f, 2.6f)));
            Scatter(160, 30f, 110f, p => ProceduralArt.PalmInto(SectorOf(foliage, p), random, p));

            // Grass: a dense band along the clearing edge, sparse tufts inside, carpet outside.
            for (int i = 0; i < 1300; i++)
            {
                float a = ProceduralArt.Range(random, 0, Mathf.PI * 2);
                float r = ClearingRadius(a) + ProceduralArt.Range(random, -1.4f, 1.8f);
                grass.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
            }
            for (int i = 0; i < 160; i++)
                grass.Add(new Vector3(ProceduralArt.Range(random, -ArenaHalfX, ArenaHalfX), 0, ProceduralArt.Range(random, -ArenaHalfZ, ArenaHalfZ)));
            Scatter(3200, 1f, 26f, p => grass.Add(p));
            foreach (var p in grass)
            {
                var at = new Vector3(p.x, GroundHeight(p.x, p.z) - .02f, p.z);
                // Short tufts on the fighting ground, lush grass beyond it.
                bool inside = OutsideDistance(p.x, p.z) <= 0f;
                ProceduralArt.GrassTuftInto(SectorOf(grassBatches, at), random, at,
                    inside ? ProceduralArt.Range(random, .55f, 1f) : ProceduralArt.Range(random, .8f, 1.6f));
            }

            var jungle = new GameObject("Jungle").transform;
            jungle.SetParent(root, false);
            for (int i = 0; i < Sectors; i++)
            {
                foliage[i].Emit(jungle, "Foliage" + i, flora, true);
                grassBatches[i].Emit(jungle, "Grass" + i, flora, false);
            }
        }

        /// <summary>Random points in a band outside the arena, dropped onto the ground.</summary>
        private void Scatter(int count, float minDistance, float maxDistance, Action<Vector3> place)
        {
            int placed = 0;
            for (int attempt = 0; attempt < count * 40 && placed < count; attempt++)
            {
                float x = ProceduralArt.Range(random, -ArenaHalfX - maxDistance, ArenaHalfX + maxDistance);
                float z = ProceduralArt.Range(random, -ArenaHalfZ - maxDistance, ArenaHalfZ + maxDistance);
                float d = OutsideDistance(x, z);
                if (d < minDistance || d > maxDistance) continue;
                // Keep the western truck approach open.
                if (x < -ArenaHalfX && Mathf.Abs(z + 4.3f) < 2.2f && d < 12f) continue;
                place(new Vector3(x, GroundHeight(x, z) - .05f, z));
                placed++;
            }
        }

        // ---------- Set dressing ----------

        private void BuildDressing(Transform root)
        {
            var props = new GameObject("SetDressing").transform;
            props.SetParent(root, false);
            var olive = ProceduralArt.Lit("Prop_OliveDrab", ProceduralArt.Hex("#4E5838"), .2f);
            var canvas = ProceduralArt.Lit("Prop_Canvas", ProceduralArt.Hex("#5F6444"), .05f, true);
            var rubber = ProceduralArt.Lit("Prop_Rubber", ProceduralArt.Hex("#1F1F1C"), .15f);
            var rust = ProceduralArt.Lit("Prop_Rust", ProceduralArt.Hex("#6D4A2E"), .25f);
            var steel = ProceduralArt.Lit("Prop_Steel", ProceduralArt.Hex("#3E4540"), .35f);
            var wood = ProceduralArt.Lit("Prop_Log", ProceduralArt.Hex("#6A4E36"), .05f);
            var woodEnd = ProceduralArt.Lit("Prop_LogEnd", ProceduralArt.Hex("#B08A5E"), .05f);
            var glass = ProceduralArt.Lit("Prop_Glass", ProceduralArt.Hex("#26302E"), .85f);

            BuildTruck(props, new Vector3(-22.5f, 0, -4.3f), 94f, olive, canvas, rubber, steel, glass);
            BuildDrums(props, new Vector3(-19.2f, 0, 8.6f), olive, rust);
            BuildDrums(props, new Vector3(19.4f, 0, -8.9f), rust, olive);
            BuildDrums(props, new Vector3(18.8f, 0, 10.4f), olive, olive);
            BuildLogs(props, new Vector3(-8.5f, 0, 12.4f), 12f, wood, woodEnd);
            BuildLogs(props, new Vector3(9.5f, 0, -12.6f), -8f, wood, woodEnd);
            BuildNetShelter(props, new Vector3(20.8f, 0, 3.2f), canvas, wood);
        }

        private static void BuildTruck(Transform parent, Vector3 position, float yaw, Material body, Material canvas,
            Material rubber, Material steel, Material glass)
        {
            var truck = new GameObject("SupplyTruck").transform;
            truck.SetParent(parent, false);
            truck.SetPositionAndRotation(position + Vector3.up * GroundHeight(position.x, position.z), Quaternion.Euler(0, yaw, 0));
            Box(truck, "Chassis", new Vector3(0, .72f, 0), new Vector3(2.1f, .28f, 6.2f), steel);
            Box(truck, "Cab", new Vector3(0, 1.45f, 2.15f), new Vector3(2.1f, 1.2f, 1.5f), body);
            Box(truck, "Hood", new Vector3(0, 1.12f, 3.35f), new Vector3(1.8f, .62f, 1.1f), body);
            Box(truck, "Windshield", new Vector3(0, 1.72f, 2.92f), new Vector3(1.8f, .52f, .06f), glass);
            Box(truck, "Grille", new Vector3(0, 1.05f, 3.92f), new Vector3(1.4f, .45f, .06f), steel);
            Box(truck, "Bed", new Vector3(0, 1.1f, -1.05f), new Vector3(2.2f, .45f, 4.1f), body);
            // Canvas cover arched over the bed.
            const int arches = 7;
            for (int i = 0; i < arches; i++)
            {
                float a0 = Mathf.PI * i / arches, a1 = Mathf.PI * (i + 1) / arches;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * 1.12f, 1.3f + Mathf.Sin(a0) * 1.1f, 0);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * 1.12f, 1.3f + Mathf.Sin(a1) * 1.1f, 0);
                var panel = Box(truck, "Canvas", (p0 + p1) * .5f + new Vector3(0, 0, -1.05f), new Vector3((p1 - p0).magnitude + .05f, .05f, 4.15f), canvas);
                panel.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg);
            }
            foreach (var z in new[] { 2.3f, -.6f, -2.2f })
            foreach (var x in new[] { -1f, 1f })
            {
                var wheel = Cylinder(truck, "Wheel", new Vector3(x * 1.02f, .5f, z), new Vector3(1f, .18f, 1f), rubber);
                wheel.localRotation = Quaternion.Euler(0, 0, 90);
                var hub = Cylinder(truck, "Hub", new Vector3(x * 1.13f, .5f, z), new Vector3(.45f, .05f, .45f), steel);
                hub.localRotation = Quaternion.Euler(0, 0, 90);
            }
        }

        private void BuildDrums(Transform parent, Vector3 center, Material a, Material b)
        {
            var group = new GameObject("FuelDrums").transform;
            group.SetParent(parent, false);
            group.position = center + Vector3.up * GroundHeight(center.x, center.z);
            int count = random.Next(3, 6);
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.4f, r = i == 0 ? 0 : .62f;
                bool lying = i == count - 1 && count > 3;
                var drum = Cylinder(group, "Drum", new Vector3(Mathf.Cos(angle) * r, lying ? .3f : .45f, Mathf.Sin(angle) * r) + (lying ? new Vector3(1.1f, 0, .4f) : Vector3.zero),
                    new Vector3(.58f, .45f, .58f), i % 2 == 0 ? a : b);
                if (lying) drum.localRotation = Quaternion.Euler(90, angle * 30f, 0);
                Cylinder(drum, "Rim", new Vector3(0, .35f, 0), new Vector3(1.04f, .04f, 1.04f), b);
                Cylinder(drum, "Rim", new Vector3(-0, -.35f, 0), new Vector3(1.04f, .04f, 1.04f), b);
            }
        }

        private static void BuildLogs(Transform parent, Vector3 center, float yaw, Material bark, Material end)
        {
            var pile = new GameObject("LogPile").transform;
            pile.SetParent(parent, false);
            pile.SetPositionAndRotation(center + Vector3.up * GroundHeight(center.x, center.z), Quaternion.Euler(0, yaw, 0));
            int[] rows = { 4, 3, 2 };
            for (int row = 0; row < rows.Length; row++)
            for (int i = 0; i < rows[row]; i++)
            {
                float x = (i - (rows[row] - 1) * .5f) * .46f;
                var log = Cylinder(pile, "Log", new Vector3(x, .23f + row * .4f, 0), new Vector3(.44f, 1.7f, .44f), bark);
                log.localRotation = Quaternion.Euler(90, 0, 0);
                Cylinder(log, "Cut", new Vector3(0, 1.001f, 0), new Vector3(.9f, .002f, .9f), end);
                Cylinder(log, "Cut", new Vector3(0, -1.001f, 0), new Vector3(.9f, .002f, .9f), end);
            }
        }

        private static void BuildNetShelter(Transform parent, Vector3 center, Material net, Material pole)
        {
            var shelter = new GameObject("CamouflageShelter").transform;
            shelter.SetParent(parent, false);
            shelter.position = center + Vector3.up * GroundHeight(center.x, center.z);
            foreach (var p in new[] { new Vector3(-1.6f, 0, -1.4f), new Vector3(1.6f, 0, -1.4f), new Vector3(-1.6f, 0, 1.4f), new Vector3(1.6f, 0, 1.4f) })
                Cylinder(shelter, "Pole", p + Vector3.up * 1.1f, new Vector3(.09f, 1.1f, .09f), pole);
            var sheet = Box(shelter, "Net", new Vector3(0, 2.2f, 0), new Vector3(3.8f, .05f, 3.4f), net);
            sheet.localRotation = Quaternion.Euler(4, 0, -3);
            var crates = ProceduralArt.Lit("Prop_Crate", ProceduralArt.Hex("#5E4A2E"), .05f);
            Box(shelter, "AmmoCrate", new Vector3(-.5f, .25f, 0), new Vector3(1f, .5f, .6f), crates);
            Box(shelter, "AmmoCrate", new Vector3(.6f, .25f, .2f), new Vector3(1f, .5f, .6f), crates);
            Box(shelter, "AmmoCrate", new Vector3(0, .75f, .1f), new Vector3(1f, .5f, .6f), crates);
        }

        // ---------- Atmosphere ----------

        private void BuildAtmosphere(Transform root)
        {
            // Smouldering cook fire at the north-east corner, plus distant smoke on the ridges.
            var fire = new Vector3(19.6f, GroundHeight(19.6f, 12.4f), 12.4f);
            Smoke(root, "CookSmoke", fire, 1.1f, 30, new Color(.55f, .55f, .52f, .38f), 1.4f, 5.5f);
            var glow = new GameObject("FireGlow").AddComponent<Light>();
            glow.transform.SetParent(root, false);
            glow.transform.position = fire + Vector3.up * .6f;
            glow.type = LightType.Point;
            glow.color = ProceduralArt.Hex("#FF8A3D");
            glow.intensity = 3.2f;
            glow.range = 6f;
            glow.shadows = LightShadows.None;
            glow.gameObject.AddComponent<Flicker>();
            var embers = ProceduralArt.Emissive("Env_Embers", ProceduralArt.Hex("#FF6A20"), 3f);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.26f;
                var log = Cylinder(root, "FireLog", fire + new Vector3(Mathf.Cos(a) * .25f, .1f, Mathf.Sin(a) * .25f), new Vector3(.12f, .45f, .12f), flora[(int)Slot.Bark]);
                log.localRotation = Quaternion.Euler(78, a * Mathf.Rad2Deg, 0);
            }
            Cylinder(root, "Embers", fire + Vector3.up * .05f, new Vector3(.6f, .03f, .6f), embers);
            Smoke(root, "RidgeSmoke", new Vector3(-70f, GroundHeight(-70f, 60f), 60f), 4f, 40, new Color(.45f, .45f, .43f, .3f), 8f, 30f);
            Smoke(root, "RidgeSmoke", new Vector3(85f, GroundHeight(85f, -40f), -40f), 5f, 40, new Color(.42f, .41f, .4f, .28f), 9f, 34f);
        }

        private static void Smoke(Transform parent, string name, Vector3 position, float radius, int maxParticles, Color color, float size, float rise)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = color;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;
            var emission = ps.emission;
            emission.rateOverTime = maxParticles / 7f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90, 0, 0);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(.3f, .9f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise / 8f, rise / 6f);
            velocity.z = new ParticleSystem.MinMaxCurve(.1f, .5f);
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .6f, 1, 2.2f));
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(.6f, .6f), new GradientAlphaKey(0, 1) });
            colorOverLifetime.color = gradient;
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-.3f, .3f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ProceduralArt.Particle(name + "Mat", false, Color.white);
            renderer.sortingFudge = 10;
            // A fixed seed keeps cosmetic smoke off the shared UnityEngine.Random state.
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)Mathf.Abs(name.GetHashCode());
            ps.Play();
        }

        // ---------- Helpers ----------

        private static Transform Spawn(Transform parent, string name, Mesh mesh, Material material, bool castShadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return go.transform;
        }

        private static Transform Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material) =>
            Primitive(PrimitiveType.Cube, parent, name, position, scale, material);

        private static Transform Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material) =>
            Primitive(PrimitiveType.Cylinder, parent, name, position, scale, material);

        private static Transform Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        /// <summary>Cheap firelight flicker driven by Perlin noise, not UnityEngine.Random.</summary>
        private sealed class Flicker : MonoBehaviour
        {
            private Light glow;
            private float baseIntensity;
            private void Awake() { glow = GetComponent<Light>(); baseIntensity = glow.intensity; }
            private void Update() => glow.intensity = baseIntensity * (.75f + Mathf.PerlinNoise(Time.time * 7f, 1.3f) * .5f);
        }
    }
}
