using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// Sun, sky, ambient, reflections and grading for the duel, from the station's <see cref="LightingPreset"/>s
    /// (weather, colours, grade) with one change: the sun keeps the preset's height but always stands due south, so
    /// it rakes across the line between the spawns and neither fighter looks into it. Mirrors the parts of
    /// CombatEnvironment that do not depend on the station's map.
    /// </summary>
    internal sealed class DuelArenaLighting
    {
        private readonly Light sun;
        private readonly Material sky;
        private readonly ReflectionProbe reflections;
        private readonly ColorAdjustments color;
        private readonly WhiteBalance balance;
        private readonly ShadowsMidtonesHighlights toning;
        private readonly Bloom bloom;
        private readonly Vignette vignette;
        private readonly VolumetricFogVolumeComponent volumetric;
        private readonly SplitToning coolShadows;

        private static readonly Color CoolShadowTint = new Color(.325f, .388f, .5f);
        private static readonly Color NeutralTint = new Color(.5f, .5f, .5f);

        /// <summary>
        /// The duel's weather, approved by the Human: fixed rather than the station's per-run roll (stored per machine),
        /// so both ends of a duel always stand in the same light.
        /// </summary>
        public const string PresetName = "Overcast";

        public LightingPreset Preset { get; private set; }

        /// <summary>
        /// The preset named by -lighting NAME (captures, comparisons), else <see cref="PresetName"/>. Logs the one
        /// chosen, so a playtest report can be checked afterwards, and warns when a name does not resolve.
        /// </summary>
        public static LightingPreset Choose()
        {
            LightingPreset Named(string name)
            {
                foreach (var preset in LightingPreset.All)
                    if (string.Equals(preset.name, name, System.StringComparison.OrdinalIgnoreCase)) return preset;
                return null;
            }
            string forced = LightingPreset.Forced;
            if (!string.IsNullOrEmpty(forced))
            {
                var chosen = Named(forced);
                if (chosen != null) { Debug.Log($"Duel lighting: {chosen.name} (-lighting)."); return chosen; }
                Debug.LogWarning($"Duel lighting: -lighting {forced} is not a preset in Resources/{LightingPreset.ResourceFolder}; ignoring it.");
            }
            var own = Named(PresetName);
            if (own != null) { Debug.Log($"Duel lighting: {own.name}."); return own; }
            // Never silent: the menu's preset is this machine's last roll, so two ends of a duel could differ.
            var menu = LightingPreset.ForMenu();
            Debug.LogError($"Duel lighting: preset {PresetName} is missing from Resources/{LightingPreset.ResourceFolder}; " +
                           $"using this machine's last single-player preset ({(menu != null ? menu.name : "none")}), which the other player may not share.");
            return menu;
        }

        public DuelArenaLighting(Transform root, Camera view)
        {
            sun = new GameObject("DuelSun").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowNormalBias = .5f;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            reflections = new GameObject("Reflections").AddComponent<ReflectionProbe>();
            reflections.transform.SetParent(root, false);
            reflections.transform.localPosition = new Vector3(0, 2.5f, 0);
            reflections.mode = ReflectionProbeMode.Realtime;
            reflections.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            reflections.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            reflections.resolution = 128;
            reflections.hdr = true;
            reflections.size = new Vector3(600f, 300f, 600f);
            reflections.shadowDistance = 0;
            reflections.clearFlags = ReflectionProbeClearFlags.Skybox;

            sky = ProceduralArt.Environment("Sky", "Duel_Sky");
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                if (view != null) view.clearFlags = CameraClearFlags.Skybox;
            }
            else if (view != null)
            {
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = ProceduralArt.Hex("#C2BB9F");
            }

            var volume = new GameObject("DuelPostProcess").AddComponent<Volume>();
            volume.transform.SetParent(root, false);
            volume.isGlobal = true;
            volume.priority = 50;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "DuelPostProfile";
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
            coolShadows = profile.Add<SplitToning>(true);
            coolShadows.highlights.Override(NeutralTint);
            coolShadows.balance.Override(12f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(.5f);
            grain.response.Override(.5f);
        }

        /// <summary>
        /// The preset's sun turned to stand due south at the same height above the horizon. This is a fairness rule, not
        /// a look: the arena is point-symmetric, a half-turn does not carry a sun direction onto itself, and only a sun
        /// with no X component (across the spawn axis) lights both spawns alike, so never give it an angle along X.
        /// </summary>
        public static Vector3 SunAlongZ(Vector3 sunForward)
        {
            sunForward.Normalize();
            float down = Mathf.Clamp(sunForward.y, -.95f, -.2f);
            return new Vector3(0f, down, Mathf.Sqrt(1f - down * down));
        }

        public void Apply(LightingPreset preset)
        {
            if (preset == null) return;
            Preset = preset;
            Vector3 sunForward = SunAlongZ(preset.SunForward);
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
                sky.SetFloat("_PanoramaOffset", preset.SkyPanoramaSunU - Mathf.Atan2(-sunForward.x, -sunForward.z) / (2f * Mathf.PI));
            }
            VolumetricLight.Apply(volumetric, VolumetricLight.Load());

            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = SkyAmbient(preset, sunForward);

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
            coolShadows.active = CombatEnvironment.CoolShadows;
            coolShadows.shadows.Override(Color.Lerp(NeutralTint, CoolShadowTint, preset.name == "Overcast" ? .5f : 1f));
            reflections.RenderProbe();
        }

        /// <summary>The preset's sky gathered into spherical harmonics, as CombatEnvironment does, under the turned sun.</summary>
        private static SphericalHarmonicsL2 SkyAmbient(LightingPreset preset, Vector3 sunForward)
        {
            var sh = new SphericalHarmonicsL2();
            Vector3 toSun = -sunForward;
            Color zenith = preset.SkyZenith.linear, horizon = preset.SkyHorizon.linear, glow = preset.SkyGlow.linear;
            Color cloudLit = preset.CloudColor.linear, cloudShade = preset.CloudShade.linear;
            Color ground = preset.GroundAlbedo.linear * (preset.SunColor.linear * preset.SunIntensity * Mathf.Max(toSun.y, 0f) + horizon);
            const int samples = 512;
            float weight = 4f / samples;
            for (int i = 0; i < samples; i++)
            {
                float y = 1f - (i + .5f) * 2f / samples, r = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                var direction = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                Color radiance;
                if (y < 0) radiance = ground;
                else
                {
                    float toward = Mathf.Max(Vector3.Dot(direction, toSun), 0f);
                    radiance = Color.Lerp(horizon, zenith, Mathf.Sqrt(y));
                    radiance += glow * (Mathf.Pow(toward, 6f) * .55f * (1f - y * .6f) + Mathf.Pow(toward, 48f) * .9f);
                    Color cloud = Color.Lerp(cloudShade, cloudLit, Mathf.Clamp01(.35f + toward * toward * toward * .9f));
                    radiance = Color.Lerp(radiance, cloud, preset.CloudCover * .55f * Mathf.SmoothStep(0f, 1f, y / .18f));
                }
                sh.AddDirectionalLight(direction, radiance, weight);
            }
            return sh * preset.AmbientIntensity;
        }
    }
}
