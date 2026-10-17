using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The volumetric light (Qiu's URP Volumetric Fog) laid over every lighting preset: sun shafts through the canopy
    /// and a light haze near the camera. One shared asset, Resources/Lighting/VolumetricLight; each preset's own sun
    /// colour and strength scale it, so it needs no per-preset copy. Without the asset nothing is drawn. Its
    /// in-scatter is added on top of everything behind it, sky and backdrop included, so the distance stays short
    /// and the scattering low: otherwise the sun side washes out and the far karst disappears.
    /// </summary>
    [CreateAssetMenu(menuName = "Tien Tuyen/Volumetric Light")]
    public sealed class VolumetricLight : ScriptableObject
    {
        public const string ResourcePath = "Lighting/VolumetricLight";

        [Tooltip("Metres from the camera it is drawn to; beyond it the preset's fog and haze carry the air.")]
        public float Distance = 25f;
        public float BaseHeight = -20f;
        public float MaximumHeight = 25f;
        [Range(0, 1)] public float Density = .15f;
        [Tooltip("Higher keeps the scene behind it brighter.")]
        public float AttenuationDistance = 500f;
        [Range(-1, 1)] public float Anisotropy = .2f;
        [Range(0, 1)] public float Scattering = .1f;
        public Color Tint = Color.white;
        [Range(8, 256)] public int MaxSteps = 32;
        [Range(1, 4)] public int BlurIterations = 1;
        [Tooltip("Multiplies the preset's painted canopy shafts, so they do not double the real ones.")]
        [Range(0, 1)] public float CanopyShafts = 1f;

        /// <summary>The shared volumetric light; null when there is none.</summary>
        public static VolumetricLight Load() => Resources.Load<VolumetricLight>(ResourcePath);

        /// <summary>Puts these settings on the volume component, or turns it off for null.</summary>
        public static void Apply(VolumetricFogVolumeComponent fog, VolumetricLight light)
        {
            if (fog == null) return;
            fog.enabled.Override(light != null);
            if (light == null) return;
            fog.distance.Override(light.Distance);
            fog.baseHeight.Override(light.BaseHeight);
            fog.maximumHeight.Override(light.MaximumHeight);
            fog.density.Override(light.Density);
            fog.attenuationDistance.Override(light.AttenuationDistance);
            fog.enableMainLightContribution.Override(true);
            fog.anisotropy.Override(light.Anisotropy);
            fog.scattering.Override(light.Scattering);
            fog.tint.Override(light.Tint);
            fog.enableAdditionalLightsContribution.Override(false);
            fog.maxSteps.Override(light.MaxSteps);
            fog.blurIterations.Override(light.BlurIterations);
        }
    }
}
