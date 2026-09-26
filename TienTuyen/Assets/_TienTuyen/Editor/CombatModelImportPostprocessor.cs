using System;
using UnityEditor;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Import the runtime FBX catalog at metre scale. The source geometry is
    /// Z-up; CombatArtDirector rotates instantiated models to Unity's Y-up axis.
    /// </summary>
    public sealed class CombatModelImportPostprocessor : AssetPostprocessor
    {
        private const string ModelFolder = "Assets/_TienTuyen/Art/Resources/Models/";

        private bool IsRuntimeModel =>
            assetPath.StartsWith(ModelFolder, StringComparison.Ordinal) &&
            assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        private void OnPreprocessModel()
        {
            if (!IsRuntimeModel) return;

            var model = (ModelImporter)assetImporter;
            // Unity's default file scale makes this Blender catalog 100x too small.
            model.useFileScale = false;
            model.globalScale = 1f;
            model.bakeAxisConversion = false;
        }

    }
}
