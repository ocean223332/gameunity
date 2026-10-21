using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Presentation-only world around the combat clearing: a Truong Son supply station. Sets up light, sky,
    /// haze and grading, then has the Environment* builders generate terrain, station, forest, horizon and
    /// atmosphere once at startup. The weather and time of day come from a <see cref="LightingPreset"/>, chosen
    /// again for every run. Nothing here has colliders or affects the simulation; CombatGame's cover boxes keep
    /// their colliders and are dressed with sandbags that match them exactly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatEnvironment : MonoBehaviour
    {
        // Mirrors CombatGame's movement clamp.
        public const float ArenaHalfX = EnvironmentLayout.HalfX, ArenaHalfZ = EnvironmentLayout.HalfZ;

        // A fixed low sun in the south-west, the same for every preset; the shafts through the forest are built along it.
        private static readonly Vector3 ShaftSun = new Vector3(.55f, -.4f, .73f).normalized;

        private CombatGame game;
        private int presetSeed;
        private Light sun;
        private Material sky;
        private ReflectionProbe reflections;
        private ColorAdjustments color;
        private WhiteBalance balance;
        private ShadowsMidtonesHighlights toning;
        private Bloom bloom;
        private Vignette vignette;
        private VolumetricFogVolumeComponent volumetric;
        private SplitToning coolShadows;
        private FilmGrain grain;

        // Grade layers over every preset, each on its own switch so either can be dropped alone.
        /// <summary>Tints the shadows towards the sky's blue, as Mountain Lake's grade does.</summary>
        public static bool CoolShadows = true;
        /// <summary>A light film grain over the final image (drawn after STP's upscale).</summary>
        public static bool Grain = true;
        // Mountain Lake's shadow blue at full strength; Overcast's shadows are already neutral, so it gets half or
        // they turn cold.
        private static readonly Color CoolShadowTint = new Color(.325f, .388f, .5f);
        private static readonly Color NeutralTint = new Color(.5f, .5f, .5f);

        /// <summary>The weather and time of day in use.</summary>
        public LightingPreset Preset { get; private set; }

        /// <summary>Distance from the arena rectangle; zero inside it.</summary>
        public static float OutsideDistance(float x, float z) => EnvironmentLayout.OutsideDistance(x, z);

        /// <summary>Visual ground height: a flat station on a plateau, jungle hills, craters and the valley below.</summary>
        public static float GroundHeight(float x, float z) => EnvironmentLayout.GroundHeight(x, z);

        public void Build(Camera view)
        {
            game = GetComponent<CombatGame>();
            SetupLighting();
            SetupSky(view);
            SetupPostProcessing();
            var root = new GameObject("Environment").transform;
            root.SetParent(transform, false);
            var ground = EnvironmentGround.CreateMaterial();
            EnvironmentGround.Build(root, ground);
            EnvironmentStation.Build(root, transform, ground);
            EnvironmentFlora.Build(root);
            EnvironmentBackdrop.Build(root);
            EnvironmentAtmosphere.Build(root, ShaftSun);
            presetSeed = game != null ? game.Seed : 0;
            // Before a run the menu keeps the last run's weather; the run itself gets new weather (LateUpdate).
            Apply(game != null && game.Seed != 0 ? LightingPreset.NextRun() : LightingPreset.ForMenu());
        }

        private void LateUpdate()
        {
            // A new run has a new seed, and with it new weather.
            if (game == null || Preset == null || game.Seed == presetSeed) return;
            presetSeed = game.Seed;
            Apply(LightingPreset.NextRun());
        }

        /// <summary>Puts the preset's sun, sky, air and grading in place and refreshes the reflections.</summary>
        public void Apply(LightingPreset preset)
        {
            if (preset == null) return;
            Preset = preset;
            Vector3 sunForward = preset.SunForward.normalized;
            sun.transform.rotation = Quaternion.LookRotation(sunForward);
            sun.color = preset.SunColor;
            sun.intensity = preset.SunIntensity;
            sun.shadowStrength = preset.ShadowStrength;

            RenderSettings.fogColor = preset.FogColor;
            RenderSettings.fogDensity = preset.FogDensity;
            Shader.SetGlobalVector("_EnvSunDir", -sunForward);
            Shader.SetGlobalVector("_EnvWind", preset.Wind);
            Shader.SetGlobalColor("_EnvSkyZenith", preset.SkyZenith);
            Shader.SetGlobalColor("_EnvSkyHorizon", preset.SkyHorizon);
            Shader.SetGlobalColor("_EnvSkyGlow", preset.SkyGlow);
            // Scenery haze (EnvCommon.EnvHazeAmount): the forest around the station stays crisp and only the hills
            // beyond take on air; the global fog above only reaches characters and props.
            Shader.SetGlobalVector("_EnvHaze", preset.Haze);
            if (sky != null)
            {
                sky.SetColor("_CloudColor", preset.CloudColor);
                sky.SetColor("_CloudShade", preset.CloudShade);
                sky.SetFloat("_CloudCover", preset.CloudCover);
                sky.SetFloat("_SunDisk", preset.SunDisk);
                bool photo = preset.SkyPanorama != null;
                sky.SetTexture("_Panorama", photo ? preset.SkyPanorama : Texture2D.blackTexture);
                sky.SetFloat("_PanoramaWeight", photo ? 1f : 0f);
                sky.SetFloat("_PanoramaExposure", preset.SkyPanoramaExposure);
                // EnvSky's u is atan2(x, z) / 2pi plus this offset; towards the sun it must be the photo's sun u.
                sky.SetFloat("_PanoramaOffset", preset.SkyPanoramaSunU - Mathf.Atan2(-sunForward.x, -sunForward.z) / (2f * Mathf.PI));
            }
            var light = VolumetricLight.Load();
            VolumetricLight.Apply(volumetric, light);
            EnvironmentAtmosphere.SetShaftIntensity(preset.ShaftIntensity * (light != null ? light.CanopyShafts : 1f));

            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = SkyAmbient(preset);

            color.postExposure.Override(preset.PostExposure);
            color.contrast.Override(preset.Contrast);
            color.saturation.Override(preset.Saturation);
            color.colorFilter.Override(preset.ColorFilter);
            balance.temperature.Override(preset.Temperature);
            balance.tint.Override(preset.Tint);
            toning.shadows.Override(preset.ShadowsToning);
            toning.highlights.Override(preset.HighlightsToning);
            bloom.threshold.Override(preset.BloomThreshold);
            bloom.intensity.Override(preset.BloomIntensity);
            bloom.tint.Override(preset.BloomTint);
            vignette.intensity.Override(preset.Vignette);
            coolShadows.active = CoolShadows;
            coolShadows.shadows.Override(Color.Lerp(NeutralTint, CoolShadowTint, preset.name == "Overcast" ? .5f : 1f));
            grain.active = Grain;

            reflections.RenderProbe();
            // Any other scripted probe (the river's own, EnvironmentGround) sees the same weather.
            foreach (var probe in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
                if (probe != reflections && probe.mode == ReflectionProbeMode.Realtime && probe.refreshMode == ReflectionProbeRefreshMode.ViaScripting)
                    probe.RenderProbe();
        }

        // ---------- Light, sky, haze, grading ----------

        private void SetupLighting()
        {
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) { sun = light; break; }
            if (sun == null)
            {
                sun = new GameObject("CombatSun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.shadows = LightShadows.Soft;
            sun.shadowNormalBias = .5f;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            // One realtime probe around the whole clearing, rendered when the weather changes: wet leaves, steel and
            // water reflect this sky and this forest instead of a grey default.
            reflections = new GameObject("Reflections").AddComponent<ReflectionProbe>();
            reflections.transform.SetParent(transform, false);
            reflections.transform.localPosition = new Vector3(0, 2.5f, 0);
            reflections.mode = ReflectionProbeMode.Realtime;
            reflections.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            reflections.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            reflections.resolution = 128;
            reflections.hdr = true;
            reflections.size = new Vector3(1200f, 400f, 1200f);
            reflections.shadowDistance = 0;
            reflections.clearFlags = ReflectionProbeClearFlags.Skybox;
        }

        /// <summary>
        /// Ambient light as the sky really looks in this preset: EnvCommon's sky colour (with the average cloud) over
        /// the upper half, sun- and sky-lit ground under it, gathered into the spherical harmonics URP shades with.
        /// </summary>
        private static SphericalHarmonicsL2 SkyAmbient(LightingPreset preset)
        {
            var sh = new SphericalHarmonicsL2();
            Vector3 toSun = -preset.SunForward.normalized;
            Color zenith = preset.SkyZenith.linear, horizon = preset.SkyHorizon.linear, glow = preset.SkyGlow.linear;
            Color cloudLit = preset.CloudColor.linear, cloudShade = preset.CloudShade.linear;
            Color ground = preset.GroundAlbedo.linear * (preset.SunColor.linear * preset.SunIntensity * Mathf.Max(toSun.y, 0f) + horizon);
            const int samples = 512;
            // Each sample adds a cosine lobe; 4/N makes a uniform sky of radiance L light a surface by L.
            float weight = 4f / samples;
            for (int i = 0; i < samples; i++)
            {
                // Fibonacci sphere: even directions without a random source.
                float y = 1f - (i + .5f) * 2f / samples, r = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                var direction = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                Color radiance;
                if (y < 0) radiance = ground;
                else
                {
                    float sun = Mathf.Max(Vector3.Dot(direction, toSun), 0f);
                    radiance = Color.Lerp(horizon, zenith, Mathf.Sqrt(y));
                    radiance += glow * (Mathf.Pow(sun, 6f) * .55f * (1f - y * .6f) + Mathf.Pow(sun, 48f) * .9f);
                    Color cloud = Color.Lerp(cloudShade, cloudLit, Mathf.Clamp01(.35f + sun * sun * sun * .9f));
                    radiance = Color.Lerp(radiance, cloud, preset.CloudCover * .55f * Mathf.SmoothStep(0f, 1f, y / .18f));
                }
                sh.AddDirectionalLight(direction, radiance, weight);
            }
            return sh * preset.AmbientIntensity;
        }

        private void SetupSky(Camera view)
        {
            sky = ProceduralArt.Environment("Sky", "Env_Sky");
            if (sky == null)
            {
                if (view != null) { view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = ProceduralArt.Hex("#C2BB9F"); }
                return;
            }
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
            color = profile.Add<ColorAdjustments>(true);
            bloom = profile.Add<Bloom>(true);
            bloom.scatter.Override(.7f);
            vignette = profile.Add<Vignette>(true);
            vignette.smoothness.Override(.45f);
            balance = profile.Add<WhiteBalance>(true);
            toning = profile.Add<ShadowsMidtonesHighlights>(true);
            volumetric = profile.Add<VolumetricFogVolumeComponent>(true);
            // Mountain Lake's split toning: blue shadows (set per preset in Apply), neutral highlights.
            coolShadows = profile.Add<SplitToning>(true);
            coolShadows.highlights.Override(NeutralTint);
            coolShadows.balance.Override(12f);
            grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(.5f);
            grain.response.Override(.5f);
        }
    }
}
