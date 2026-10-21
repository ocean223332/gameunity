using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using L = TienTuyen.Presentation.EnvironmentLayout;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// What hangs in the air: sun shafts through the canopy edge, valley mist,
    /// dust over the station, smoke from the craters, the cook fire and distant
    /// ridges, and the fire's glow. All of it is particles or static quads set up
    /// once; only the fire light flickers per frame.
    /// </summary>
    internal static class EnvironmentAtmosphere
    {
        private static Material shafts;

        /// <summary>Brightens or hides the sun shafts for the weather (none under overcast).</summary>
        public static void SetShaftIntensity(float intensity)
        {
            if (shafts != null) shafts.SetFloat("_Intensity", intensity);
        }

        public static void Build(Transform root, Vector3 sunForward)
        {
            var air = new GameObject("Atmosphere").transform;
            air.SetParent(root, false);
            LightShafts(air, sunForward);
            Mist(air, "ValleyMist", new Vector3(0, -7f, -46f), new Vector3(110f, 2f, 40f), 70, new Color(.86f, .82f, .72f, .16f), 18f, 30f);
            Mist(air, "ForestMist", new Vector3(0, 4f, 46f), new Vector3(120f, 3f, 24f), 50, new Color(.82f, .82f, .74f, .14f), 16f, 26f);
            Dust(air);

            var fire = new Vector3(21.5f, L.GroundHeight(21.5f, 14.5f), 14.5f);
            Smoke(air, "CookSmoke", fire + Vector3.up * .3f, .4f, 34, new Color(.62f, .6f, .56f, .42f), 1.1f, 4.2f, 2.2f);
            Fire(air, fire);
            // Smouldering strikes in the valley and a burning wreck.
            foreach (int index in new[] { 1, 5, 11 })
            {
                var c = L.Craters[index];
                Smoke(air, "CraterSmoke", new Vector3(c.Center.x, c.Floor - c.Depth * .4f, c.Center.y), c.Radius * .35f, 26,
                    new Color(.5f, .48f, .45f, .3f), 2.2f, 7f, 1.4f);
            }
            var wreck = new Vector3(-19.5f, L.GroundHeight(-19.5f, -31.5f) + 1.2f, -31.5f);
            // Thin warm-grey smoke that is mostly gone before it clears the canopy: against the hazy sky a dark,
            // dense plume reads as a stain floating in the air.
            Smoke(air, "WreckSmoke", wreck, .8f, 40, new Color(.5f, .46f, .41f, .36f), 2.6f, 10f, 1.6f, .3f);
            Embers(air, "WreckEmbers", wreck - Vector3.up * .6f, .8f, 14);
            Smoke(air, "RidgeSmoke", new Vector3(-82f, L.GroundHeight(-82f, 70f), 70f), 5f, 40, new Color(.6f, .58f, .55f, .3f), 9f, 36f, 1f);
            Smoke(air, "RidgeSmoke", new Vector3(96f, L.GroundHeight(96f, -64f), -64f), 6f, 40, new Color(.55f, .53f, .5f, .28f), 10f, 40f, 1f);
        }

        // ---------- Sun shafts ----------

        /// <summary>
        /// Crossed additive quads slanting down along the sun, gathered under a few
        /// gaps in the canopy around the station. The shader fades them near the
        /// camera, edge-on and with the sun behind the viewer.
        /// </summary>
        private static void LightShafts(Transform parent, Vector3 sunForward)
        {
            var shader = Shader.Find("TienTuyen/Environment/LightShaft");
            if (shader == null || !shader.isSupported) return;
            var material = shafts = new Material(shader) { name = "Env_LightShaft" };
            material.SetColor("_Color", ProceduralArt.Hex("#FFC888"));
            material.SetFloat("_Intensity", .26f);
            var random = new System.Random(77);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            Vector3 down = sunForward.normalized;
            Vector3 across = Vector3.Cross(down, Vector3.up).normalized;
            Vector3 across2 = Vector3.Cross(down, across).normalized;
            // Canopy gaps: each lets a loose bundle of shafts through.
            var gaps = new Vector2[6];
            for (int i = 0; i < gaps.Length; i++)
                gaps[i] = new Vector2((i + ProceduralArt.Range(random, -.3f, .3f)) * Mathf.PI * 2 / gaps.Length, ProceduralArt.Range(random, 7f, 22f));
            // The low sun makes shafts long and slanted: keep only those that stay
            // in the forest around the station instead of striping across the playfield.
            for (int attempt = 0, made = 0; attempt < 600 && made < 22; attempt++)
            {
                var gap = gaps[attempt % gaps.Length];
                float angle = gap.x + ProceduralArt.Range(random, -.12f, .12f), reach = gap.y + ProceduralArt.Range(random, -3f, 3f);
                var s = new Vector2(Mathf.Cos(angle) * (L.HalfX + reach), Mathf.Sin(angle) * (L.HalfZ + reach));
                if (L.Valley(s.x, s.y) > .3f) continue;
                float groundY = L.GroundHeight(s.x, s.y);
                var foot = new Vector3(s.x, groundY, s.y);
                float height = ProceduralArt.Range(random, 6f, 12f);
                Vector3 top = foot - down * (height / -down.y);
                bool clear = true;
                for (float t = 0; t <= 1f && clear; t += .25f)
                {
                    Vector3 p = Vector3.Lerp(foot, top, t);
                    clear = L.OutsideDistance(p.x, p.z) > 4.5f && !L.InSite(p.x, p.z, 0f);
                }
                if (!clear) continue;
                made++;
                float width = ProceduralArt.Range(random, .6f, 3f);
                // Most shafts are faint; a few carry the light.
                float roll = (float)random.NextDouble();
                byte strength = (byte)((.25f + .75f * roll * roll) * 255);
                byte seed = (byte)random.Next(256);
                for (int plane = 0; plane < 3; plane++)
                {
                    float a = plane * Mathf.PI / 3f;
                    Vector3 side = across * Mathf.Cos(a) + across2 * Mathf.Sin(a);
                    Vector3 normal = Vector3.Cross(side, down).normalized;
                    int v = vertices.Count;
                    vertices.Add(top - side * width * .5f); vertices.Add(top + side * width * .5f);
                    vertices.Add(foot + side * width * .8f); vertices.Add(foot - side * width * .8f);
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                    for (int i = 0; i < 4; i++) { normals.Add(normal); colors.Add(new Color32(seed, 255, 255, strength)); }
                    triangles.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                }
            }
            var mesh = new Mesh { name = "Env_LightShafts" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var go = new GameObject("LightShafts", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // ---------- Particles ----------

        private static ParticleSystem Emitter(Transform parent, string name, Vector3 position, int maxParticles, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // A fixed seed keeps cosmetic particles off the shared UnityEngine.Random state.
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)Mathf.Abs((name + position).GetHashCode());
            return ps;
        }

        private static Gradient Fade(float peak, float hold)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(peak, .2f), new GradientAlphaKey(peak * hold, .65f), new GradientAlphaKey(0, 1) });
            return gradient;
        }

        private static void Smoke(Transform parent, string name, Vector3 position, float radius, int maxParticles, Color color, float size, float rise, float lifetimeScale, float hold = .6f)
        {
            var ps = Emitter(parent, name, position, maxParticles, ProceduralArt.Particle(name + "Mat", false, Color.white));
            var main = ps.main;
            float life = 6f * lifetimeScale;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life, life * 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
            main.startColor = color;
            var emission = ps.emission;
            emission.rateOverTime = maxParticles / (life * 1.25f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90, 0, 0);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // Drift downwind (towards the north-east, with the sun).
            velocity.x = new ParticleSystem.MinMaxCurve(.25f, .7f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise / (life * 1.3f), rise / life);
            velocity.z = new ParticleSystem.MinMaxCurve(.15f, .5f);
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .5f, 1, 2.6f));
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Fade(1f, hold);
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-.25f, .25f);
            ps.GetComponent<ParticleSystemRenderer>().sortingFudge = 10;
            ps.Play();
        }

        /// <summary>Large, slow, nearly transparent sheets lying low in a box.</summary>
        private static void Mist(Transform parent, string name, Vector3 center, Vector3 size, int maxParticles, Color color, float minSize, float maxSize)
        {
            var ps = Emitter(parent, name, center, maxParticles, ProceduralArt.Particle(name + "Mat", false, Color.white));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 26f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            var emission = ps.emission;
            emission.rateOverTime = maxParticles / 22f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(.2f, .6f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, .05f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.1f, .2f);
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Fade(1f, .9f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            // Soft fade where sheets cut into the ground keeps them from showing hard lines.
            renderer.sortingFudge = 20;
            renderer.maxParticleSize = 4f;
            ps.Play();
        }

        /// <summary>Motes drifting over the station that glint in the low sun.</summary>
        private static void Dust(Transform parent)
        {
            var ps = Emitter(parent, "DustMotes", new Vector3(0, 2.2f, 0), 160, ProceduralArt.Particle("DustMat", true, Color.white));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .06f);
            main.startColor = new Color(1f, .82f, .55f, .55f);
            var emission = ps.emission;
            emission.rateOverTime = 14f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(L.HalfX * 2.4f, 3.5f, L.HalfZ * 2.6f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = .25f;
            noise.frequency = .3f;
            noise.scrollSpeed = .1f;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(.05f, .2f);
            velocity.y = new ParticleSystem.MinMaxCurve(-.02f, .05f);
            velocity.z = new ParticleSystem.MinMaxCurve(.02f, .12f);
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Fade(1f, .8f);
            ps.Play();
        }

        private static void Embers(Transform parent, string name, Vector3 position, float radius, int maxParticles)
        {
            var ps = Emitter(parent, name, position, maxParticles, ProceduralArt.Particle(name + "Mat", true, Color.white));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f);
            main.startColor = new Color(2.4f, .9f, .25f, 1f);
            var emission = ps.emission;
            emission.rateOverTime = maxParticles / 1.8f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90, 0, 0);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-.2f, .4f);
            velocity.y = new ParticleSystem.MinMaxCurve(.8f, 1.8f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.2f, .3f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = .6f;
            noise.frequency = 1.2f;
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Fade(1f, .7f);
            ps.Play();
        }

        private static void Fire(Transform parent, Vector3 fire)
        {
            var flames = Emitter(parent, "CookFlames", fire + Vector3.up * .1f, 24, ProceduralArt.Particle("FlameMat", true, Color.white));
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .7f);
            main.startSize = new ParticleSystem.MinMaxCurve(.25f, .5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(2.6f, 1.1f, .3f, .8f), new Color(2.2f, .6f, .15f, .7f));
            var emission = flames.emission;
            emission.rateOverTime = 34f;
            var shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = .22f;
            shape.rotation = new Vector3(-90, 0, 0);
            var velocity = flames.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-.05f, .1f);
            velocity.y = new ParticleSystem.MinMaxCurve(.7f, 1.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(-.05f, .1f);
            var sizeOverLifetime = flames.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, .2f));
            var colorOverLifetime = flames.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Fade(1f, .7f);
            flames.Play();
            Embers(parent, "CookEmbers", fire + Vector3.up * .2f, .3f, 12);

            var glow = new GameObject("FireGlow").AddComponent<Light>();
            glow.transform.SetParent(parent, false);
            glow.transform.position = fire + Vector3.up * .7f;
            glow.type = LightType.Point;
            glow.color = ProceduralArt.Hex("#FF8A3D");
            glow.intensity = 3.5f;
            glow.range = 7f;
            glow.shadows = LightShadows.None;
            glow.gameObject.AddComponent<Flicker>();
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
