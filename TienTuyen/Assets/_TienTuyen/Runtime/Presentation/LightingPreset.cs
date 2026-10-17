using System;
using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// One weather and time of day: sun, sky, air and grading, applied by CombatEnvironment. The presets live in
    /// Resources/Lighting; each run picks one at random but never the previous run's, which is saved in PlayerPrefs so
    /// this holds across restarts too.
    /// <c>-lighting NAME</c> on the command line (or <see cref="Forced"/>) pins one, for captures and comparisons.
    /// </summary>
    [CreateAssetMenu(menuName = "Tien Tuyen/Lighting Preset")]
    public sealed class LightingPreset : ScriptableObject
    {
        public const string ResourceFolder = "Lighting";

        [Header("Sun")]
        public Vector3 SunForward = new Vector3(.55f, -.4f, .73f);
        public Color SunColor = Color.white;
        public float SunIntensity = 2f;
        [Range(0, 1)] public float ShadowStrength = .85f;
        /// <summary>Strength of the shafts through the canopy gaps; their geometry follows the sunny preset's sun.</summary>
        public float ShaftIntensity = .26f;

        [Header("Sky")]
        public Color SkyZenith = Color.blue;
        public Color SkyHorizon = Color.white;
        /// <summary>Warm scatter around the sun.</summary>
        public Color SkyGlow = Color.white;
        public Color CloudColor = Color.white;
        public Color CloudShade = Color.grey;
        [Range(0, 1)] public float CloudCover = .4f;
        public float SunDisk = 6f;
        /// <summary>
        /// Photographed sky (equirectangular HDR with its sun painted out) drawn above the horizon instead of the
        /// gradient and procedural clouds; null keeps the procedural sky. The cloud fields still feed the ambient.
        /// </summary>
        public Texture SkyPanorama;
        /// <summary>Where the photo's sun was across the image (0..1); the photo turns to put it under this sun.</summary>
        public float SkyPanoramaSunU;
        /// <summary>Scales the photo's radiance to the brightness the grading was tuned for.</summary>
        public float SkyPanoramaExposure = 1f;

        [Header("Ambient and reflections")]
        /// <summary>Scales the ambient light taken from the sky and the sunlit ground.</summary>
        public float AmbientIntensity = 1f;
        /// <summary>Albedo of the ground and canopy that bounce light up into shade.</summary>
        public Color GroundAlbedo = new Color(.3f, .25f, .2f);

        [Header("Air")]
        public Color FogColor = Color.grey;
        public float FogDensity = .0068f;
        /// <summary>EnvCommon _EnvHaze: forest haze amount, its distance (m), atmosphere distance (m), haze height (m).</summary>
        public Vector4 Haze = new Vector4(.3f, 160f, 14000f, 60f);
        /// <summary>EnvCommon _EnvWind: direction xy, strength (m), speed.</summary>
        public Vector4 Wind = new Vector4(.6f, .8f, .12f, 1.1f);

        [Header("Grading (HDR)")]
        public float PostExposure;
        public float Contrast;
        public float Saturation;
        public Color ColorFilter = Color.white;
        public float Temperature;
        public float Tint;
        public Vector4 ShadowsToning = new Vector4(1, 1, 1, 0);
        public Vector4 HighlightsToning = new Vector4(1, 1, 1, 0);
        public float BloomThreshold = 1f;
        public float BloomIntensity = .8f;
        public Color BloomTint = Color.white;
        public float Vignette = .28f;

        /// <summary>Name of the preset every run uses instead of a random one; null for random.</summary>
        public static string Forced = Argument();

        private static LightingPreset[] all;

        /// <summary>The presets in Resources/Lighting, by name.</summary>
        public static LightingPreset[] All
        {
            get
            {
                if (all == null)
                {
                    all = Resources.LoadAll<LightingPreset>(ResourceFolder);
                    Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
                }
                return all;
            }
        }

        /// <summary>PlayerPrefs key holding the name of the last run's preset.</summary>
        public const string LastKey = "TienTuyen.LastLighting";

        /// <summary>The preset to show before any run: the forced one, else the last run's; null when there are none.</summary>
        public static LightingPreset ForMenu()
        {
            var presets = All;
            if (presets.Length == 0) return null;
            return Named(Forced) ?? Named(PlayerPrefs.GetString(LastKey, null)) ?? presets[0];
        }

        /// <summary>
        /// The preset for a new run: the forced one, else a random one other than the last run's, which it then saves.
        /// Null when there are none.
        /// </summary>
        public static LightingPreset NextRun()
        {
            var presets = All;
            if (presets.Length == 0) return null;
            var forced = Named(Forced);
            if (forced != null) return forced;
            var last = Named(PlayerPrefs.GetString(LastKey, null));
            int index = UnityEngine.Random.Range(0, last != null && presets.Length > 1 ? presets.Length - 1 : presets.Length);
            // Skip over the last run's preset, so every other one is equally likely.
            if (last != null && presets.Length > 1 && index >= Array.IndexOf(presets, last)) index++;
            PlayerPrefs.SetString(LastKey, presets[index].name);
            PlayerPrefs.Save();
            return presets[index];
        }

        private static LightingPreset Named(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var preset in All)
                if (string.Equals(preset.name, name, StringComparison.OrdinalIgnoreCase)) return preset;
            return null;
        }

        private static string Argument()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-lighting", StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
