using TMPro;
using UnityEngine;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Palette, typefaces and text presets shared by every combat screen.</summary>
    public static class UiTheme
    {
        // Jungle night ink, parchment text and brass accents.
        public static readonly Color Ink = Hex("#0C120F");
        public static readonly Color Panel = Hex("#121A16");
        public static readonly Color PanelRaised = Hex("#1C2620");
        public static readonly Color PanelHover = Hex("#26332A");
        public static readonly Color Paper = Hex("#F1EBDC");
        public static readonly Color Muted = Hex("#A2AB98");
        public static readonly Color Faint = Hex("#6E7868");
        public static readonly Color Gold = Hex("#E4B24A");
        public static readonly Color GoldBright = Hex("#F6CD6E");
        public static readonly Color GoldDeep = Hex("#9C7428");
        public static readonly Color Health = Hex("#B7D36A");
        public static readonly Color Danger = Hex("#E2553F");
        public static readonly Color Olive = Hex("#56663F");

        public static TMP_FontAsset Display { get; private set; }
        public static TMP_FontAsset Body { get; private set; }
        private static Material displayShadow, bodyShadow;

        /// <summary>Loads the baked interface fonts, falling back to the scene font when they are absent.</summary>
        public static void Load(TMP_FontAsset fallback)
        {
            Display = Resources.Load<TMP_FontAsset>("UiFonts/Display SDF") ?? fallback;
            Body = Resources.Load<TMP_FontAsset>("UiFonts/Body SDF") ?? fallback;
            displayShadow = Resources.Load<Material>("UiFonts/Display SDF Shadow");
            bodyShadow = Resources.Load<Material>("UiFonts/Body SDF Shadow");
            if (displayShadow != null && Display != null && displayShadow.mainTexture != Display.material.mainTexture) displayShadow = null;
            if (bodyShadow != null && Body != null && bodyShadow.mainTexture != Body.material.mainTexture) bodyShadow = null;
        }

        /// <summary>Soft drop shadow so text stays readable over the 3D scene.</summary>
        public static void Shadow(TMP_Text label)
        {
            if (label.font == Display && displayShadow != null) label.fontSharedMaterial = displayShadow;
            else if (label.font == Body && bodyShadow != null) label.fontSharedMaterial = bodyShadow;
        }

        public static Color Alpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        public static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString(value, out var color);
            return color;
        }
    }
}
