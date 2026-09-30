using System;
using System.Collections.Generic;
using UnityEngine;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>
    /// Anti-aliased interface sprites generated once at runtime: rounded panels
    /// (9-sliced), outlines, fades, glows and discs. Supersampled so edges stay
    /// crisp at 1080p and above.
    /// </summary>
    public static class UiSprites
    {
        private const int Supersample = 4;
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>Filled rounded rectangle, 9-sliced so any size keeps the same corner radius.</summary>
        public static Sprite Rounded(float radius) => Cached("rounded" + radius, () => RoundedSprite(radius, 0f));

        /// <summary>Rounded outline of the given thickness (in canvas units).</summary>
        public static Sprite Outline(float radius, float thickness) =>
            Cached("outline" + radius + "_" + thickness, () => RoundedSprite(radius, thickness));

        /// <summary>Opaque at the bottom, transparent at the top (rotate or flip for other edges).</summary>
        public static Sprite FadeUp => Cached("fadeUp", () => Gradient(false));

        /// <summary>Opaque on the left, transparent on the right.</summary>
        public static Sprite FadeRight => Cached("fadeRight", () => Gradient(true));

        public static Sprite Glow => Cached("glow", () => Radial(128, r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f)));

        public static Sprite Disc => Cached("disc", () => Radial(128, r => Mathf.Clamp01((1f - r) * 64f)));

        public static Sprite Ring(float thickness) => Cached("ring" + thickness, () =>
            Radial(256, r => Mathf.Clamp01((1f - r) * 128f) * Mathf.Clamp01((r - (1f - thickness)) * 128f)));

        /// <summary>Screen-edge darkening used behind menus.</summary>
        public static Sprite Vignette => Cached("vignette", () => Radial(256, r => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, 1.25f, r))));

        public static Sprite White => Cached("white", () =>
        {
            var texture = NewTexture(4, 4);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 100f);
        });

        private static Sprite Cached(string key, Func<Sprite> create)
        {
            if (!cache.TryGetValue(key, out var sprite) || sprite == null)
            {
                sprite = create();
                sprite.name = "Ui_" + key;
                cache[key] = sprite;
            }
            return sprite;
        }

        private static Sprite RoundedSprite(float radius, float thickness)
        {
            float r = radius * Supersample, t = thickness * Supersample;
            int border = Mathf.CeilToInt(r) + 2;
            int size = border * 2 + 2;
            var texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            float half = size * .5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Signed distance to a rounded box that fills the texture.
                float px = Mathf.Abs(x + .5f - half) - (half - r), py = Mathf.Abs(y + .5f - half) - (half - r);
                float outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude + Mathf.Min(Mathf.Max(px, py), 0) - r;
                float alpha = Mathf.Clamp01(.5f - outside / 1.4f);
                if (t > 0) alpha *= Mathf.Clamp01(.5f + (outside + t) / 1.4f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f * Supersample, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static Sprite Gradient(bool horizontal)
        {
            const int length = 256;
            var texture = NewTexture(horizontal ? length : 2, horizontal ? 2 : length);
            var pixels = new Color32[length * 2];
            for (int i = 0; i < length; i++)
            {
                float u = i / (length - 1f);
                byte alpha = (byte)(Mathf.SmoothStep(1f, 0f, u) * 255);
                for (int j = 0; j < 2; j++)
                    pixels[horizontal ? j * length + i : i * 2 + j] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
        }

        private static Sprite Radial(int size, Func<float, float> alpha)
        {
            var texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(Mathf.Sqrt(u * u + v * v))) * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
        }

        private static Texture2D NewTexture(int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
    }
}
