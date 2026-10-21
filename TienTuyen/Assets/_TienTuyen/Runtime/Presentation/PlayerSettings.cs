using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>Shared mouse preference for the station and duel cameras.</summary>
    public static class PlayerSettings
    {
        private const string SensitivityKey = "TienTuyen.Settings.MouseSensitivity";
        public const float MinSensitivity = .25f, MaxSensitivity = 3f;

        public static float MouseSensitivity
        {
            get
            {
                float value = PlayerPrefs.GetFloat(SensitivityKey, 1f);
                return float.IsNaN(value) || float.IsInfinity(value) ? 1f :
                    Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            }
            set => PlayerPrefs.SetFloat(SensitivityKey,
                float.IsNaN(value) || float.IsInfinity(value) ? 1f :
                Mathf.Clamp(value, MinSensitivity, MaxSensitivity));
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
