#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using TienTuyen.Duel.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Explicit creation of a standalone duel scene, for working on the arena and its screens in the Editor; never
    /// replaces an existing one and leaves the build's scene list alone (the game opens the duel from the menu in a
    /// scene made at runtime).
    /// </summary>
    public static class DuelSceneBuilder
    {
        public const string ScenePath = "Assets/_TienTuyen/Scenes/DuelArena.unity";
        private const string FontPath = "Assets/_TienTuyen/Settings/Combat/CombatVietnamese.asset";

        [MenuItem("Tien Tuyen/Duel/Create Scene")]
        public static void CreateScene()
        {
            RequireReady();
            if (File.Exists(ScenePath) || AssetDatabase.LoadMainAssetAtPath(ScenePath) != null)
                throw new InvalidOperationException("Duel scene already exists. Refusing to overwrite: " + ScenePath);
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null || pipeline.defaultMaterial == null)
                throw new InvalidOperationException("The current quality level must use the existing URP pipeline.");
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
                throw new InvalidOperationException("TMP resources are missing. Run Tien Tuyen/Combat/Create Scene or import TMP Essentials first.");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("DuelCamera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0, 14.5f, -31f), Quaternion.Euler(25, 0, 0));
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = true;

            var stage = new GameObject("DuelStage").AddComponent<DuelStage>();
            stage.InterfaceFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Duel scene could not be saved.");
            Debug.Log(ValidateScene());
        }

        public static string ValidateScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open the saved DuelArena scene before validation.");
            var roots = scene.GetRootGameObjects();
            if (roots.SelectMany(root => root.GetComponentsInChildren<DuelStage>(true)).Count() != 1)
                throw new InvalidOperationException("DuelArena requires exactly one DuelStage.");
            var cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            if (cameras.Length != 1 || !cameras[0].CompareTag("MainCamera"))
                throw new InvalidOperationException("DuelArena requires exactly one MainCamera.");
            return "Duel scene validation passed: one DuelStage and one main camera. The build's scene list is unchanged.";
        }

        private static void RequireReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for compilation and exit Play Mode before scene setup.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or discard dirty scenes before setup.");
        }
    }
}
#endif
