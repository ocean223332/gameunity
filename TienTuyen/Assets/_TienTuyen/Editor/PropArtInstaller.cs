using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// URP Lit materials for the realistic weapon and prop catalog (Art/Resources/Models, see
    /// Art/Source/README.md). An FBX material named "x" gets Art/Props/Materials/x.mat built from
    /// Art/Props/Textures/x_BaseColor, x_Normal and x_MetallicSmoothness (R metallic, G occlusion,
    /// A smoothness), and the model importer remaps it there. Re-runnable.
    /// Batch: Unity -batchmode -projectPath TienTuyen -executeMethod TienTuyen.Editor.PropArtInstaller.Install -quit
    /// </summary>
    public static class PropArtInstaller
    {
        public const string Models = "Assets/_TienTuyen/Art/Resources/Models";
        public const string Textures = "Assets/_TienTuyen/Art/Props/Textures";
        public const string Materials = "Assets/_TienTuyen/Art/Props/Materials";
        public const string StationTextures = "Assets/_TienTuyen/Art/Resources/Props";

        public static readonly string[] Catalog =
        {
            "rifle", "smg", "shotgun", "m16", "etool", "supply_crate", "crate", "sandbag", "drum", "rock",
            "shed", "watchtower", "cookfire", "bunker", "truck", "truck_zil", "aa_gun", "bicycle",
        };

        // Models that PropBatch merges into station meshes at runtime: their meshes must stay CPU-readable in the player.
        public static readonly string[] Merged =
        {
            "sandbag", "crate", "drum", "rock", "shed", "watchtower", "cookfire", "bunker", "truck", "truck_zil", "aa_gun",
            "bicycle",
        };

        // The muddy bottom-course bags have their own colour over the clean bags' relief.
        private static string MapsOf(string material) => material == "sandbag_ground" ? "sandbag" : material;

        [MenuItem("Tien Tuyen/Props/Install Prop Materials")]
        public static void Install()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable");
            if (!AssetDatabase.IsValidFolder(Materials)) AssetDatabase.CreateFolder("Assets/_TienTuyen/Art/Props", "Materials");
            foreach (string model in Catalog)
            {
                string path = Models + "/" + model + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogWarning("Prop model missing: " + path);
                    continue;
                }
                var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name)
                    .Concat(importer.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name))
                    .Distinct().ToArray();
                foreach (string name in names)
                {
                    var material = BuildMaterial(shader, name);
                    if (material != null) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
                }
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Prop materials installed for " + Catalog.Length + " models.");
        }

        private static Material BuildMaterial(Shader shader, string name)
        {
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/" + name + "_BaseColor.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/" + MapsOf(name) + "_Normal.png");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "/" + MapsOf(name) + "_MetallicSmoothness.png");
            if (baseMap == null || normal == null || mask == null)
            {
                Debug.LogWarning("Prop textures missing for material " + name);
                return null;
            }
            string path = Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_OcclusionMap", mask);
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_OCCLUSIONMAP");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }

    /// <summary>Import settings for Art/Props/Textures: "_Normal" normal maps, linear "_MetallicSmoothness" masks, sRGB colour.</summary>
    public sealed class PropTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            bool station = assetPath.StartsWith(PropArtInstaller.StationTextures + "/", StringComparison.Ordinal);
            if (!station && !assetPath.StartsWith(PropArtInstaller.Textures + "/", StringComparison.Ordinal)) return;
            var texture = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            bool normal = file.EndsWith("_Normal", StringComparison.Ordinal);
            bool mask = file.EndsWith("_MetallicSmoothness", StringComparison.Ordinal);
            texture.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            texture.sRGBTexture = !normal && !mask;
            texture.alphaSource = TextureImporterAlphaSource.FromInput;
            texture.mipmapEnabled = true;
            texture.anisoLevel = 4;
            texture.maxTextureSize = 2048;
            texture.textureCompression = TextureImporterCompression.CompressedHQ;
            if (!station) return;
            // Tiling cloth and the cut-out net (alpha), loaded at runtime by StationMaterials.
            texture.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            texture.alphaIsTransparency = !normal;
            texture.mipMapsPreserveCoverage = !normal;
            texture.alphaTestReferenceValue = .5f;
        }
    }
}
