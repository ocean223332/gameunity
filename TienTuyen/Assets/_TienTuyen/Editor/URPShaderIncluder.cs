using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Ensures URP shaders are included in builds to prevent missing shader issues.
    /// </summary>
    public class URPShaderIncluder : IPreprocessBuildWithReport
    {
        private static readonly string[] EssentialShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Blit",
            "Universal Render Pipeline/Complex Lit",
            "Shader Graphs/Lit",
            "Shader Graphs/Unlit"
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            int addedCount = AddEssentialShaders();
            Debug.Log($"[URPShaderIncluder] Added {addedCount} URP shaders to Always Included Shaders before the build.");
        }

        internal static int AddEssentialShaders()
        {
            // Unity 6.3 has no EditorGraphicsSettings.AddAlwaysIncludedShader API.
            // Edit the same list exposed in Project Settings > Graphics instead.
            var graphicsSettings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var includedShaders = graphicsSettings.FindProperty("m_AlwaysIncludedShaders");
            if (includedShaders == null || !includedShaders.isArray)
            {
                throw new InvalidOperationException("Could not find Always Included Shaders in Graphics Settings.");
            }

            int addedCount = 0;
            foreach (string shaderName in EssentialShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[URPShaderIncluder] Shader not found: {shaderName}");
                    continue;
                }

                bool alreadyIncluded = false;
                for (int index = 0; index < includedShaders.arraySize; index++)
                {
                    if (includedShaders.GetArrayElementAtIndex(index).objectReferenceValue == shader)
                    {
                        alreadyIncluded = true;
                        break;
                    }
                }

                if (alreadyIncluded)
                {
                    continue;
                }

                int newIndex = includedShaders.arraySize;
                includedShaders.InsertArrayElementAtIndex(newIndex);
                includedShaders.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
                addedCount++;
                Debug.Log($"[URPShaderIncluder] Added shader to build: {shaderName}");
            }

            if (addedCount > 0)
            {
                graphicsSettings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }

            return addedCount;
        }
    }

    /// <summary>
    /// Editor menu command to manually add URP shaders.
    /// </summary>
    public static class URPShaderMenu
    {
        [MenuItem("TienTuyen/Fix Build Shaders")]
        public static void AddURPShaders()
        {
            int addedCount = URPShaderIncluder.AddEssentialShaders();

            Debug.Log($"[URPShaderIncluder] Added {addedCount} URP shaders to Always Included Shaders list.");
            EditorUtility.DisplayDialog("URP Shaders Added",
                $"Added {addedCount} URP shaders to the build.\n\nYour 3D models should now display correctly in builds.",
                "OK");
        }
    }
}
