using System.Collections.Generic;
using TienTuyen.Presentation;
using UnityEngine;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The jungle around the clearing, from the station's FloraBiome species drawn by FloraScatter: knee-high herbs
    /// and grass along the berm, undergrowth and banana past the camera's reach, then bamboo, areca and broadleaf
    /// giving way to firs and pines up the hills. Plants are placed on one half and every one is copied by a half
    /// turn, so the forest each fighter looks into is the same forest.
    /// </summary>
    internal static class DuelArenaFlora
    {
        // The trailing camera reaches 4.3 m behind a fighter at the clamp: nothing taller than a knee inside that.
        private const float HerbBand = 4.8f, CoverBand = 6.2f;

        private static readonly List<FloraInstance> placed = new List<FloraInstance>();
        private static FloraBiome biome;
        private static System.Random random;

        public static void Build(Transform root)
        {
            biome = Resources.Load<FloraBiome>(FloraBiome.ResourcePath);
            if (biome == null)
            {
                Debug.LogError("[DuelArenaFlora] Missing Resources/" + FloraBiome.ResourcePath + "; run Tien Tuyen/Build Flora.");
                return;
            }
            placed.Clear();
            random = new System.Random(1975);
            Scatter(1.4f, 1.2f, HerbBand, Edge);
            Scatter(2.3f, HerbBand, 11f, Undergrowth);
            Scatter(4.2f, 9f, 24f, Fringe);
            Scatter(6.4f, 22f, 85f, Forest);
            Scatter(9.5f, 80f, 236f, Far);
            FloraScatter.Create(root, biome, placed);
            placed.Clear();
        }

        private static int Species(string name) => biome.IndexOf(name);
        private static float Range(float min, float max) => ProceduralArt.Range(random, min, max);
        private static bool Chance(double p) => random.NextDouble() < p;
        private static string Pick(params string[] names) => names[random.Next(names.Length)];

        /// <summary>Jittered grid over the north half (z &gt;= 0); <paramref name="plant"/> gets the berm distance.</summary>
        private static void Scatter(float cell, float minD, float maxD, System.Action<float, float, float> plant)
        {
            float reachX = DuelArenaView.BermX + maxD, reachZ = DuelArenaView.BermZ + maxD;
            for (float z = 0; z < reachZ; z += cell)
            for (float x = -reachX; x < reachX; x += cell)
            {
                float px = x + Range(0, cell), pz = z + Range(0, cell);
                float d = DuelArenaView.OutsideBerm(px, pz);
                if (d < minD || d >= maxD) continue;
                plant(px, pz, d);
            }
        }

        /// <summary>Plants a species here and its twin half a turn away; nothing taller than a knee inside the herb band.</summary>
        private static void Plant(string name, float x, float z, float scale, float sink = .05f)
        {
            int species = Species(name);
            if (species < 0) return;
            float height = biome.Plants[species].Height * scale, d = DuelArenaView.OutsideBerm(x, z);
            if (d < HerbBand && height > .65f) return;
            if (d < CoverBand && height > FloraBiome.TallHeight) return;
            float yaw = Range(0f, 360f);
            float y = DuelArenaView.Height(x, z) - sink * scale;
            placed.Add(new FloraInstance { Species = species, Position = new Vector3(x, y, z), Yaw = yaw, Scale = scale });
            placed.Add(new FloraInstance { Species = species, Position = new Vector3(-x, y, -z), Yaw = yaw + 180f, Scale = scale });
        }

        private static void Edge(float x, float z, float d)
        {
            float roll = (float)random.NextDouble();
            float lush = Mathf.PerlinNoise(x * .2f + 7f, z * .2f);
            if (roll < .25f + lush * .3f) Plant(Pick("TallGrass_A", "TallGrass_B"), x, z, Range(.8f, 1.2f), .03f);
            else if (roll < .6f) Plant(Pick("Sorrel", "Periwinkle", "Fern_A", "Fern_B"), x, z, Range(.8f, 1.2f), .02f);
            else if (roll < .7f) Plant(Pick("Wildflower_Yellow", "Wildflower_Blue"), x, z, Range(.8f, 1.1f), .02f);
            else if (roll < .76f) Plant(Pick("Rock_A", "Rock_B"), x, z, Range(.25f, .4f), .18f);
        }

        private static void Undergrowth(float x, float z, float d)
        {
            float roll = (float)random.NextDouble();
            if (roll < .2f) Plant(Pick("Fern_A", "Fern_B", "Fern_C", "FernSmall", "Fern_D", "Fern_E"), x, z, Range(.8f, 1.2f));
            else if (roll < .34f) Plant(Pick("Bush", "RedGinger", "Heliconia_A", "Heliconia_B"), x, z, Range(.8f, 1.15f), .05f);
            else if (roll < .46f) Plant(Pick("ElephantEar_A", "ElephantEar_B", "PalmYoung_A", "PalmYoung_B"), x, z, Range(.8f, 1.1f), .05f);
            else if (roll < .58f) Plant(Pick("TallGrass_A", "TallGrass_B"), x, z, Range(1f, 1.4f), .03f);
            else if (roll < .66f) Plant(Pick("PineSapling", "Fir_Plant"), x, z, Range(.8f, 1.2f), .03f);
            else if (roll < .72f) Plant(Pick("Rock_A", "Rock_B", "Rock_C"), x, z, Range(.3f, .6f), .2f);
            else if (roll < .75f) Plant(Pick("Log", "Stump"), x, z, Range(.7f, 1f), .1f);
            else if (roll < .82f && d > CoverBand) Plant(Pick("Banana_A", "Banana_B", "Banana_C"), x, z, Range(.8f, 1.1f), .05f);
        }

        private static void Fringe(float x, float z, float d)
        {
            float roll = (float)random.NextDouble();
            if (roll < .22f) Plant(Pick("Banana_A", "Banana_B", "Banana_C"), x, z, Range(.85f, 1.15f), .05f);
            else if (roll < .36f) Plant("Bamboo", x, z, Range(.65f, .95f), .1f);
            else if (roll < .48f) Plant(Pick("Areca_A", "Areca_B", "Palm_A"), x, z, Range(.75f, 1.05f), .1f);
            else if (roll < .6f) Plant(Pick("PineYoung", "Fir_Small"), x, z, Range(.8f, 1.2f), .05f);
            else if (roll < .7f) Plant(Pick("JungleTree_A", "JungleTree_B", "Broadleaf_C"), x, z, Range(.7f, .95f), .1f);
            for (int i = 0; i < 2; i++)
                if (Chance(.6)) Plant(Pick("Fern_D", "Fern_E", "Bush", "ElephantEar_A", "Heliconia_A", "RedGinger"),
                    x + Range(-1.6f, 1.6f), z + Range(-1.6f, 1.6f), Range(.9f, 1.3f));
        }

        private static void Forest(float x, float z, float d)
        {
            float pines = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(30f, 70f, d + (Mathf.PerlinNoise(x * .02f + 3f, z * .02f) - .5f) * 30f));
            float roll = (float)random.NextDouble();
            if (roll < .06f) { Plant(Pick("Outcrop_A", "Outcrop_B", "Outcrop_C", "RockFace"), x, z, Range(.5f, .8f), .35f); return; }
            if (Chance(pines))
            {
                if (roll < .55f) Plant(Pick("Fir_Tall", "Fir_Medium", "Fir_Tall"), x, z, Range(.8f, 1.15f), .1f);
                else if (roll < .85f) Plant(Pick("Pine_A", "Pine_B", "Pine_C", "Pine_D"), x, z, Range(.75f, 1.05f), .1f);
                else Plant(Pick("Broadleaf_A", "Maple_A", "Beech_Gold"), x, z, Range(.8f, 1.05f), .1f);
            }
            else
            {
                if (roll < .35f) Plant(Pick("JungleTree_A", "JungleTree_B", "JungleTree_Tall"), x, z, Range(.8f, 1.1f), .1f);
                else if (roll < .55f) Plant(Pick("Broadleaf_A", "Broadleaf_B", "Broadleaf_C"), x, z, Range(.8f, 1.1f), .1f);
                else if (roll < .72f) Plant("Bamboo", x, z, Range(.8f, 1.1f), .1f);
                else if (roll < .86f) Plant(Pick("Areca_A", "Areca_B"), x, z, Range(.9f, 1.15f), .1f);
                else Plant(Pick("Banana_A", "Banana_B"), x, z, Range(.9f, 1.2f), .05f);
            }
            if (d < 50f && Chance(.7)) Plant(Pick("Fern_D", "Fern_E", "Bush", "ElephantEar_B", "PalmYoung_A"),
                x + Range(-2.5f, 2.5f), z + Range(-2.5f, 2.5f), Range(.9f, 1.4f));
        }

        private static void Far(float x, float z, float d)
        {
            float roll = (float)random.NextDouble();
            if (roll < .4f) Plant(Pick("Fir_Forest_A", "Fir_Forest_B"), x, z, Range(.8f, 1.1f), .1f);
            else if (roll < .65f) Plant(Pick("Pine_A", "Pine_B", "Spruce"), x, z, Range(.8f, 1.1f), .1f);
            else if (roll < .85f) Plant(Pick("Broadleaf_A", "Broadleaf_B", "JungleTree_A"), x, z, Range(.9f, 1.2f), .1f);
        }
    }

    /// <summary>What hangs in the air: dust motes over the clearing and mist lying in the forest, mirrored like the rest.</summary>
    internal static class DuelArenaAir
    {
        public static void Build(Transform root)
        {
            var air = new GameObject("Atmosphere").transform;
            air.SetParent(root, false);
            var dust = ProceduralArt.Particle("Duel_Dust", true, new Color(1f, .9f, .7f, .5f));
            Emitter(air, "Motes", Vector3.up * 1.8f, new Vector3(2f * DuelArenaLayout.HalfWidth, 2.6f, 2f * DuelArenaLayout.HalfHeight), 160, dust,
                new Vector2(.03f, .07f), new Vector2(7f, 12f), .25f, new Color(1f, .92f, .75f, .35f));
            var mist = ProceduralArt.Particle("Duel_Mist", false, new Color(.86f, .85f, .78f, .16f));
            foreach (float side in new[] { 1f, -1f })
            {
                Emitter(air, "ForestMist", new Vector3(0, 6f, 48f * side), new Vector3(150f, 4f, 24f), 50, mist,
                    new Vector2(16f, 28f), new Vector2(18f, 26f), .4f, new Color(.86f, .85f, .78f, .13f));
                Emitter(air, "ForestMist", new Vector3(64f * side, 6f, 0), new Vector3(24f, 4f, 110f), 40, mist,
                    new Vector2(16f, 28f), new Vector2(18f, 26f), .4f, new Color(.86f, .85f, .78f, .13f));
            }
        }

        private static void Emitter(Transform parent, string name, Vector3 position, Vector3 box, int maxParticles, Material material,
            Vector2 size, Vector2 lifetime, float drift, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.duration = 10f;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = 0f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = system.emission;
            emission.rateOverTime = maxParticles / ((lifetime.x + lifetime.y) * .5f);
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-drift, drift);
            velocity.y = new ParticleSystem.MinMaxCurve(-drift * .3f, drift * .5f);
            velocity.z = new ParticleSystem.MinMaxCurve(-drift, drift);
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .3f), new GradientAlphaKey(1f, .7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
        }
    }
}
