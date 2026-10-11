using System;
using System.IO;
using UnityEditor;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Import settings for the scanned environment textures: flora atlases in Art/Flora, the ground layers and
    /// bark and rock sets in Art/Resources/Environment. "_Normal" files are normal maps, "_Mask" files hold linear data
    /// (R occlusion, G smoothness); everything else is sRGB albedo whose alpha is a cut-out or a height.
    /// </summary>
    public sealed class EnvironmentTexturePostprocessor : AssetPostprocessor
    {
        private const string FloraFolder = "Assets/_TienTuyen/Art/Flora/";
        private const string EnvironmentFolder = "Assets/_TienTuyen/Art/Resources/Environment/";

        private void OnPreprocessTexture()
        {
            bool flora = assetPath.StartsWith(FloraFolder, StringComparison.Ordinal);
            bool ground = assetPath.StartsWith(EnvironmentFolder, StringComparison.Ordinal);
            if (!flora && !ground) return;
            var texture = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            bool normal = file.EndsWith("_Normal", StringComparison.Ordinal);
            bool mask = file.EndsWith("_Mask", StringComparison.Ordinal);
            texture.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            texture.sRGBTexture = !normal && !mask;
            texture.mipmapEnabled = true;
            texture.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            texture.anisoLevel = ground ? 8 : 2;
            texture.maxTextureSize = file.Contains("_Card_") || mask ? 1024 : 2048;
            texture.textureCompression = TextureImporterCompression.CompressedHQ;
            if (flora && !normal)
            {
                // Keep cut-out leaves from thinning to nothing in the small mips.
                texture.alphaIsTransparency = true;
                texture.mipMapsPreserveCoverage = true;
                texture.alphaTestReferenceValue = .45f;
            }
        }
    }
}
