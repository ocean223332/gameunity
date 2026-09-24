#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TienTuyen.Editor
{
    /// <summary>Editor-only foundation tools. These preview shapes are not gameplay.</summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/_TienTuyen/Scenes/Bootstrap.unity";
        private const string SettingsPath = "Assets/_TienTuyen/Settings";
        private const string ProfilePath = SettingsPath + "/Preview_VolumeProfile.asset";
        private static readonly string[] MaterialNames =
            { "Preview_Earth", "Preview_Olive", "Preview_Sand", "Preview_Gold" };

        [MenuItem("Tien Tuyen/Foundation/Create Baseline")]
        public static void CreateBaseline()
        {
            RequireEditMode();
            RequireCleanScenes();
            // Never overwrite a user's scene or partially created assets on a second invocation.
            RequireNewAsset(ScenePath);
            RequireNewAsset(ProfilePath);
            foreach (string name in MaterialNames)
                RequireNewAsset(SettingsPath + "/" + name + ".mat");

            int desktopQuality = Array.FindLastIndex(QualitySettings.names,
                name => name.IndexOf("PC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Desktop", StringComparison.OrdinalIgnoreCase) >= 0);
            if (desktopQuality < 0)
                throw new InvalidOperationException("No PC/Desktop quality level found; choose one explicitly before setup.");
            QualitySettings.SetQualityLevel(desktopQuality, true);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
                throw new InvalidOperationException("The selected desktop quality must already use URP.");
            RequirePostProcessData(urp);
            Material templateMaterial = urp.defaultMaterial;
            if (templateMaterial == null || templateMaterial.shader == null)
                throw new InvalidOperationException("URP default material/shader is missing.");

            EnsureFolder("Assets/_TienTuyen/Scenes");
            EnsureFolder(SettingsPath);
            urp.supportsHDR = true;
            EditorUtility.SetDirty(urp);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Tiền Tuyến";
            PlayerSettings.companyName = "TienTuyen";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";

            var materials = new[]
            {
                CreateMaterial(MaterialNames[0], templateMaterial.shader, "#80664C"),
                CreateMaterial(MaterialNames[1], templateMaterial.shader, "#8E9B66"),
                CreateMaterial(MaterialNames[2], templateMaterial.shader, "#A39876"),
                CreateMaterial(MaterialNames[3], templateMaterial.shader, "#E7BD62")
            };
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Preview_VolumeProfile";
            AssetDatabase.CreateAsset(profile, ProfilePath);
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.5f);
            bloom.threshold.Override(0.9f);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.15f);
            foreach (VolumeComponent component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ColorFromHex("#747C6E");
            RenderSettings.fog = false;

            var cameraObject = new GameObject("Preview_MainCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 26f, -15.011f), Quaternion.Euler(60f, 0f, 0f));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 12f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = ColorFromHex("#354638");
            camera.allowHDR = true;
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = 1;
            cameraData.volumeTrigger = camera.transform;

            var lightObject = new GameObject("Preview_Sun", typeof(Light));
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light sunlight = lightObject.GetComponent<Light>();
            sunlight.type = LightType.Directional;
            sunlight.intensity = 1.2f;
            sunlight.color = ColorFromHex("#FFF3D9");
            sunlight.shadows = LightShadows.Soft;
            RenderSettings.sun = sunlight;

            var volumeObject = new GameObject("Preview_GlobalVolume", typeof(Volume));
            volumeObject.layer = 0;
            Volume volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            CreateSwatch("Preview_Ground_34x20", PrimitiveType.Cube,
                new Vector3(0f, -0.15f, 0f), new Vector3(34f, 0.3f, 20f), materials[0]);
            CreateSwatch("Preview_PlayerSilhouette_NotPlayable", PrimitiveType.Capsule,
                new Vector3(0f, 1f, 0f), Vector3.one, materials[1]);
            CreateSwatch("Preview_CoverMaterial", PrimitiveType.Cube,
                new Vector3(-5f, 0.6f, 2f), new Vector3(3f, 1.2f, 1f), materials[2]);
            CreateSwatch("Preview_SupplyMaterial", PrimitiveType.Cube,
                new Vector3(5f, 0.6f, 1f), new Vector3(1.2f, 1.2f, 1.2f), materials[3]);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Failed to save Bootstrap scene.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log(ValidateBaseline());
        }

        public static string ValidateBaseline()
        {
            var errors = new List<string>();
            Scene scene = SceneManager.GetActiveScene();
            Check(scene.path == ScenePath, "Open the saved Bootstrap scene before validation.", errors);
            Check(!scene.isDirty, "Bootstrap has unsaved changes.", errors);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Check(urp != null && urp.supportsHDR, "Active pipeline must be URP with HDR.", errors);
            if (urp != null)
            {
                try { RequirePostProcessData(urp); }
                catch (InvalidOperationException exception) { errors.Add(exception.Message); }
            }
            Check(PlayerSettings.colorSpace == ColorSpace.Linear, "Color space must be Linear.", errors);
            Camera[] cameras = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            Check(cameras.Length == 1, "Bootstrap must contain exactly one camera.", errors);
            if (cameras.Length == 1)
            {
                Camera camera = cameras[0];
                Check(camera.enabled && camera.gameObject.activeInHierarchy && camera.CompareTag("MainCamera") &&
                      camera.orthographic && Mathf.Approximately(camera.orthographicSize, 12f) && camera.allowHDR,
                    "Camera must be active MainCamera, orthographic size 12, and HDR-enabled.", errors);
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                Check(data != null && data.renderType == CameraRenderType.Base && data.renderPostProcessing &&
                      data.volumeLayerMask.value == 1, "Camera needs base URP post-processing and Default-only volume mask.", errors);
            }
            Volume[] volumes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Volume>(true)).ToArray();
            Check(volumes.Length == 1, "Bootstrap must contain exactly one volume.", errors);
            if (volumes.Length == 1)
            {
                Volume volume = volumes[0];
                Check(volume.enabled && volume.gameObject.activeInHierarchy && volume.isGlobal &&
                      volume.gameObject.layer == 0 && Mathf.Approximately(volume.weight, 1f), "Global volume must be active on Default at weight 1.", errors);
                VolumeProfile profile = volume.sharedProfile;
                Check(profile != null && AssetDatabase.GetAssetPath(profile) == ProfilePath, "Expected saved preview volume profile.", errors);
                if (profile != null)
                {
                    Check(profile.TryGet(out Tonemapping tone) && tone.active && tone.mode.overrideState && tone.mode.value == TonemappingMode.ACES,
                        "ACES tonemapping override missing.", errors);
                    Check(profile.TryGet(out Bloom bloom) && bloom.active && bloom.intensity.overrideState && bloom.threshold.overrideState &&
                          Mathf.Approximately(bloom.intensity.value, 0.5f) && Mathf.Approximately(bloom.threshold.value, 0.9f), "Bloom override values incorrect.", errors);
                    Check(profile.TryGet(out Vignette vignette) && vignette.active && vignette.intensity.overrideState &&
                          Mathf.Approximately(vignette.intensity.value, 0.15f), "Vignette override value incorrect.", errors);
                }
            }
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            Check(buildScenes.Length == 1 && buildScenes[0].enabled && buildScenes[0].path == ScenePath,
                "Build settings must enable only Bootstrap.", errors);
            Check(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) == ScriptingImplementation.Mono2x,
                "Standalone scripting backend must be Mono.", errors);
            Check(PlayerSettings.defaultScreenWidth == 1280 && PlayerSettings.defaultScreenHeight == 720 &&
                  PlayerSettings.fullScreenMode == FullScreenMode.Windowed, "Player must default to 1280x720 windowed.", errors);
            Check(EditorSettings.serializationMode == SerializationMode.ForceText && VersionControlSettings.mode == "Visible Meta Files",
                "ForceText and Visible Meta Files are required.", errors);
            if (errors.Count > 0)
                throw new InvalidOperationException("Baseline validation failed:\n- " + string.Join("\n- ", errors));
            return "Baseline validation passed: saved Bootstrap, URP/HDR/Linear, orthographic camera, ACES/Bloom/Vignette, Windows Mono settings. No gameplay implemented.";
        }

        [MenuItem("Tien Tuyen/Foundation/Validate Baseline")]
        private static void ValidateMenu() => Debug.Log(ValidateBaseline());

        public static string BuildWindows()
        {
            RequireEditMode();
            RequireCleanScenes();
            ValidateBaseline();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.GetFullPath(Path.Combine(projectRoot, "../Builds/Windows/TienTuyen.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report == null || report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + (report == null ? "no report" : report.summary.result.ToString()));
            return $"Windows build succeeded: {output}; {report.summary.totalSize} bytes; {report.summary.totalTime.TotalSeconds:F1} seconds. Launch smoke test still required.";
        }

        [MenuItem("Tien Tuyen/Foundation/Build Windows")]
        private static void BuildMenu() => Debug.Log(BuildWindows());

        private static void RequirePostProcessData(UniversalRenderPipelineAsset urp)
        {
            var serialized = new SerializedObject(urp);
            SerializedProperty list = serialized.FindProperty("m_RendererDataList");
            SerializedProperty index = serialized.FindProperty("m_DefaultRendererIndex");
            if (list == null || index == null || index.intValue < 0 || index.intValue >= list.arraySize ||
                !(list.GetArrayElementAtIndex(index.intValue).objectReferenceValue is UniversalRendererData renderer) || renderer.postProcessData == null)
                throw new InvalidOperationException("Default URP renderer needs valid PostProcessData.");
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for compilation and exit Play Mode before running setup/build.");
        }

        private static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or discard open scene changes yourself before proceeding.");
        }

        private static void RequireNewAsset(string path)
        {
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("Refusing to overwrite existing asset: " + path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static Material CreateMaterial(string name, Shader shader, string color)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", ColorFromHex(color));
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, SettingsPath + "/" + name + ".mat");
            return material;
        }

        private static void CreateSwatch(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            // Render-only preview, not a placeholder physics/gameplay implementation.
            UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<Collider>());
        }

        private static Color ColorFromHex(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out Color color))
                throw new ArgumentException("Invalid color: " + value);
            return color;
        }

        private static void Check(bool condition, string error, List<string> errors)
        {
            if (!condition) errors.Add(error);
        }
    }
}
#endif
