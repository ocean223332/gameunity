using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Bakes the interface typefaces (Oswald Bold for display, Source Sans Pro Semibold
    /// for body text, both SIL OFL) into static TextMeshPro SDF assets with the full
    /// Vietnamese character set, plus a soft drop-shadow material preset for each.
    /// </summary>
    public static class CombatUiFontSetup
    {
        private const string SourceFolder = "Assets/_TienTuyen/Art/Fonts";
        private const string OutputFolder = "Assets/_TienTuyen/Art/Resources/UiFonts";

        private static readonly (string source, string name)[] Fonts =
        {
            ("Oswald-Bold.ttf", "Display"),
            ("SourceSansPro-Semibold.ttf", "Body"),
        };

        [MenuItem("Tien Tuyen/Combat/Rebuild Interface Fonts")]
        public static void RebuildFonts()
        {
            foreach (var font in Fonts) AssetDatabase.DeleteAsset(AssetPath(font.name));
            EnsureFonts();
        }

        /// <summary>Creates any missing font asset; existing ones are kept.</summary>
        public static void EnsureFonts()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder)) AssetDatabase.CreateFolder("Assets/_TienTuyen/Art/Resources", "UiFonts");
            var created = new List<TMP_FontAsset>();
            foreach (var font in Fonts)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(font.name));
                if (asset == null) asset = Bake(font.source, font.name);
                created.Add(asset);
            }
            // Display glyphs that Oswald lacks (arrows) come from the body face.
            var display = created[0];
            if (display.fallbackFontAssetTable == null) display.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!display.fallbackFontAssetTable.Contains(created[1]))
            {
                display.fallbackFontAssetTable.Add(created[1]);
                EditorUtility.SetDirty(display);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Interface fonts ready: " + string.Join(", ", created.ConvertAll(asset => asset.name)));
        }

        private static string AssetPath(string name) => OutputFolder + "/" + name + " SDF.asset";

        private static TMP_FontAsset Bake(string sourceFile, string name)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFolder + "/" + sourceFile);
            if (source == null) throw new InvalidOperationException("Missing interface font source: " + sourceFile);
            var font = TMP_FontAsset.CreateFontAsset(source, 72, 9, GlyphRenderMode.SDFAA, 2048, 2048,
                AtlasPopulationMode.Dynamic, false);
            if (font == null) throw new InvalidOperationException("TextMeshPro could not create a font asset from " + sourceFile);
            if (!font.TryAddCharacters(Characters(), out string missing) && !string.IsNullOrEmpty(missing))
                Debug.Log(name + " font lacks optional glyphs (fallback will render them): " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.name = name + " SDF";

            string path = AssetPath(name);
            AssetDatabase.CreateAsset(font, path);
            font.material.name = name + " SDF Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (Texture2D atlas in font.atlasTextures)
            {
                atlas.name = name + " SDF Atlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            EditorUtility.SetDirty(font);

            // Material preset with a soft underlay, used for text drawn directly over the 3D scene.
            string shadowPath = OutputFolder + "/" + name + " SDF Shadow.mat";
            AssetDatabase.DeleteAsset(shadowPath);
            var shadow = new Material(font.material) { name = name + " SDF Shadow" };
            shadow.EnableKeyword("UNDERLAY_ON");
            shadow.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, .55f));
            shadow.SetFloat("_UnderlayOffsetX", .35f);
            shadow.SetFloat("_UnderlayOffsetY", -.45f);
            shadow.SetFloat("_UnderlayDilate", .15f);
            shadow.SetFloat("_UnderlaySoftness", .55f);
            AssetDatabase.CreateAsset(shadow, shadowPath);
            return font;
        }

        private static string Characters()
        {
            var characters = new StringBuilder();
            for (int c = 32; c <= 126; c++) characters.Append((char)c);
            for (int c = 160; c <= 255; c++) characters.Append((char)c);
            characters.Append("ĂăĐđĨĩŨũƠơƯư");
            for (int c = 0x1EA0; c <= 0x1EF9; c++) characters.Append((char)c);
            characters.Append("–—‘’“”•…›‹→←↑↓×");
            return characters.ToString();
        }
    }
}
