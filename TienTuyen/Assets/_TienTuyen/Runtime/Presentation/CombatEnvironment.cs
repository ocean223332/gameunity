using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Presentation-only world around the combat clearing: a Truong Son supply
    /// station at golden hour. Sets up light, sky, haze and grading, then has the
    /// Environment* builders generate terrain, station, jungle, horizon and
    /// atmosphere once at startup. Nothing here has colliders or affects the
    /// simulation; CombatGame's cover boxes keep their colliders and are dressed
    /// with sandbags that match them exactly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatEnvironment : MonoBehaviour
    {
        // Mirrors CombatGame's movement clamp.
        public const float ArenaHalfX = EnvironmentLayout.HalfX, ArenaHalfZ = EnvironmentLayout.HalfZ;

        // Low sun in the south-west: long shadows across the station, shafts through the western forest.
        private static readonly Vector3 SunForward = new Vector3(.55f, -.4f, .73f).normalized;
        private static readonly Color Haze = ProceduralArt.Hex("#C2BB9F");

        /// <summary>Distance from the arena rectangle; zero inside it.</summary>
        public static float OutsideDistance(float x, float z) => EnvironmentLayout.OutsideDistance(x, z);

        /// <summary>Visual ground height: a flat station on a plateau, jungle hills, craters and the valley below.</summary>
        public static float GroundHeight(float x, float z) => EnvironmentLayout.GroundHeight(x, z);

        public void Build(Camera view)
        {
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
            EnvironmentAtmosphere.Build(root, SunForward);
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
            sun.transform.rotation = Quaternion.LookRotation(SunForward);
            sun.color = ProceduralArt.Hex("#FFCB8E");
            sun.intensity = 2.4f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .85f;
            sun.shadowNormalBias = .5f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ProceduralArt.Hex("#7F98A6");
            // Bright, warm bounce from the laterite and the canopy, so shade under the shed, between the bags and
            // inside the foliage stays readable rather than near black.
            RenderSettings.ambientEquatorColor = ProceduralArt.Hex("#A8A088");
            RenderSettings.ambientGroundColor = ProceduralArt.Hex("#6A5440");
            RenderSettings.ambientIntensity = 1.35f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0068f;
            RenderSettings.fogColor = Haze;

            Shader.SetGlobalVector("_EnvSunDir", -SunForward);
            Shader.SetGlobalVector("_EnvWind", new Vector4(.6f, .8f, .12f, 1.1f));
            Shader.SetGlobalColor("_EnvSkyZenith", ProceduralArt.Hex("#6E8FA8"));
            Shader.SetGlobalColor("_EnvSkyHorizon", Haze);
            Shader.SetGlobalColor("_EnvSkyGlow", ProceduralArt.Hex("#FFB066"));
            // Scenery haze (EnvCommon.EnvHazeAmount): the jungle around the station stays crisp (1% at 30 m, 4% at
            // 60 m) and only the hills beyond take on air (18% at 150 m, 30% at most), separating the forest layers;
            // the atmosphere then greys the ranges kilometre by kilometre. The global fog above only reaches characters and props.
            Shader.SetGlobalVector("_EnvHaze", new Vector4(.3f, 160f, 14000f, 60f));
        }

        private static void SetupSky(Camera view)
        {
            var sky = ProceduralArt.Environment("Sky", "Env_Sky");
            if (sky == null)
            {
                if (view != null) { view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = Haze; }
                return;
            }
            sky.SetColor("_CloudColor", ProceduralArt.Hex("#FFD9A8"));
            sky.SetColor("_CloudShade", ProceduralArt.Hex("#8E8C86"));
            sky.SetFloat("_CloudCover", .42f);
            sky.SetFloat("_SunDisk", 6f);
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
            //color.postExposure.Override(.35f);
            //color.contrast.Override(6f);
            //color.saturation.Override(0f);
            //color.colorFilter.Override(new Color(1f, .975f, .94f));
            color.postExposure.Override(.5f);
            color.contrast.Override(10f);
            color.saturation.Override(10f);
            color.colorFilter.Override(Color.white);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(.8f);
            bloom.scatter.Override(.7f);
            bloom.tint.Override(ProceduralArt.Hex("#FFD7A6"));
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.28f);
            var balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(6f);
            balance.tint.Override(2f);
            vignette.smoothness.Override(.45f);
            // Teal shadows, warm highlights.
            var toning = profile.Add<ShadowsMidtonesHighlights>(true);
            toning.shadows.Override(new Vector4(.92f, 1f, 1.07f, 0f));
            toning.highlights.Override(new Vector4(1.06f, 1f, .92f, 0f));
        }
    }
}
