#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using TienTuyen.Combat;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace TienTuyen.Editor
{
    /// <summary>Explicit scene creation; never replaces the foundation scene or an existing combat scene.</summary>
    public static class CombatSceneSetup
    {
        public const string ScenePath = "Assets/_TienTuyen/Scenes/CombatSpike.unity";
        private const string AssetFolder = "Assets/_TienTuyen/Settings/Combat";
        private const string FontPath = AssetFolder + "/CombatVietnamese.asset";
        private static bool buildQueued;

        [MenuItem("Tien Tuyen/Combat/Create Scene")]
        public static void CreateScene()
        {
            RequireReady();
            if (File.Exists(ScenePath) || AssetDatabase.LoadMainAssetAtPath(ScenePath) != null)
                throw new InvalidOperationException("Combat scene already exists. Refusing to overwrite: " + ScenePath);
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null || pipeline.defaultMaterial == null)
                throw new InvalidOperationException("The current quality level must use the existing URP pipeline.");
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
            {
                TMP_PackageResourceImporter.ImportResources(true, false, false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
                    throw new InvalidOperationException("TMP resources are still importing. Wait for import, then invoke CreateScene again.");
            }
            EnsureFolder(AssetFolder);
            TMP_FontAsset font = CreateFont();
            Shader shader = pipeline.defaultMaterial.shader;
            Material ground = MaterialAsset("Ground", shader, "#80664C");
            Material cover = MaterialAsset("Cover", shader, "#A39876");
            Material player = MaterialAsset("Player", shader, "#8E9B66");
            Material enemy = MaterialAsset("Enemy", shader, "#766C5E");
            Material shooter = MaterialAsset("Shooter", shader, "#A36A4C");
            Material projectile = MaterialAsset("Projectile", shader, "#FFF4C0", true);
            Material pickup = MaterialAsset("Pickup", shader, "#E7BD62", true);
            Material warning = MaterialAsset("Warning", shader, "#F17858", true);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ColorFromHex("#929A83");
            RenderSettings.fog = false;
            var cameraObject = new GameObject("CombatCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0, 30, -17.32051f), Quaternion.Euler(60, 0, 0));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 12.1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = ColorFromHex("#24352D");
            camera.allowHDR = true;
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = false;
            var sunObject = new GameObject("CombatSun", typeof(Light));
            sunObject.transform.rotation = Quaternion.Euler(50, -30, 0);
            var sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = ColorFromHex("#FFF3D9");
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            var root = new GameObject("CombatSpike");
            var game = root.AddComponent<CombatGame>();
            game.GroundMaterial = ground;
            game.CoverMaterial = cover;
            game.PlayerMaterial = player;
            game.EnemyMaterial = enemy;
            game.ShooterMaterial = shooter;
            game.ProjectileMaterial = projectile;
            game.PickupMaterial = pickup;
            game.WarningMaterial = warning;
            root.AddComponent<CombatPresentation>().InterfaceFont = font;
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Combat scene could not be saved.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log(ValidateScene());
        }

        public static string ValidateScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open the saved CombatSpike scene before validation or building.");
            var roots = scene.GetRootGameObjects();
            var games = roots.SelectMany(root => root.GetComponentsInChildren<CombatGame>(true)).ToArray();
            if (games.Length != 1)
                throw new InvalidOperationException("CombatSpike requires exactly one CombatGame.");
            var game = games[0];
            Material[] materials = { game.GroundMaterial, game.CoverMaterial, game.PlayerMaterial, game.EnemyMaterial,
                game.ShooterMaterial, game.ProjectileMaterial, game.PickupMaterial, game.WarningMaterial };
            if (materials.Any(material => material == null || material.shader == null || !AssetDatabase.Contains(material)))
                throw new InvalidOperationException("Every runtime material must reference a saved asset with a shader.");
            var presentation = game.GetComponent<CombatPresentation>();
            if (presentation == null || presentation.InterfaceFont == null || presentation.InterfaceFont.material == null)
                throw new InvalidOperationException("Combat presentation requires a saved Vietnamese TMP font.");
            if (!presentation.InterfaceFont.HasCharacters("TIỀN TUYẾN Súng trường Tiểu liên Đợt tiếp tế", out System.Collections.Generic.List<char> missing))
                Debug.LogWarning("Combat font is missing some Vietnamese glyphs; TMP fallback rendering will be used: " + new string(missing.ToArray()));
            var cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            // CombatThirdPersonCamera switches this camera to perspective at runtime.
            if (cameras.Length != 1 || !cameras[0].CompareTag("MainCamera"))
                throw new InvalidOperationException("CombatSpike requires exactly one MainCamera.");
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != ScenePath || !scenes[0].enabled)
                throw new InvalidOperationException("Windows build must enable only CombatSpike.");
            return "Combat scene validation passed: runtime controller, Vietnamese HUD font, serialized materials, main camera and combat build entry. Play Mode validation is separate.";
        }

        [MenuItem("Tien Tuyen/Combat/Build Windows (Queued)")]
        public static void QueueBuildWindows()
        {
            RequireReady();
            ValidateScene();
            if (buildQueued || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("A Windows build is already queued or running.");
            buildQueued = true;
            Debug.Log("Combat Windows build queued. Completion and BuildReport summary will be written to Logs/CombatBuildReport.txt.");
            EditorApplication.delayCall += () =>
            {
                try { Debug.Log(BuildWindows()); }
                catch (Exception exception) { Debug.LogException(exception); }
                finally { buildQueued = false; }
            };
        }

        public static string BuildWindows()
        {
            RequireReady();
            ValidateScene();
            string project = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.GetFullPath(Path.Combine(project, "../Builds/Windows/TienTuyen.exe"));
            string evidence = Path.Combine(project, "Logs/CombatBuildReport.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory(Path.GetDirectoryName(evidence));
            File.WriteAllText(evidence, "RUNNING " + DateTime.UtcNow.ToString("O") + "\nScene: " + ScenePath);
            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = output,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
                });
                string summary = report == null ? "FAILED: no BuildReport" :
                    $"{report.summary.result}: {output}\nScene: {ScenePath}\nBytes: {report.summary.totalSize}\nSeconds: {report.summary.totalTime.TotalSeconds:F1}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}";
                File.WriteAllText(evidence, summary + "\nFinished UTC: " + DateTime.UtcNow.ToString("O"));
                if (report == null || report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Combat Windows build failed. " + summary);
                return "Combat Windows build succeeded. " + summary + "\nLaunch and playtest still required.";
            }
            catch (Exception exception)
            {
                File.AppendAllText(evidence, "\nException: " + exception);
                throw;
            }
        }

        private static TMP_FontAsset CreateFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing != null) return existing;
            // Unity supplies this font. Bake all needed glyphs so the player does not require an installed OS font.
            Font source = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048);
            if (font == null)
            {
                var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (fallback == null) throw new InvalidOperationException("Could not create or load a TextMeshPro interface font.");
                font = UnityEngine.Object.Instantiate(fallback);
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            }
            var characters = new StringBuilder();
            for (int i = 32; i <= 126; i++) characters.Append((char)i);
            characters.Append("ÀÁÂÃÈÉÊÌÍÒÓÔÕÙÚÝàáâãèéêìíòóôõùúýĂăĐđĨĩŨũƠơƯư·—");
            for (int i = 0x1EA0; i <= 0x1EF9; i++) characters.Append((char)i);
            // LegacyRuntime.ttf does not include every Vietnamese precomposed glyph on all Unity
            // installations. Keep the available glyphs and let TMP fallback handle the remainder.
            font.TryAddCharacters(characters.ToString(), out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning("Interface font is missing optional Vietnamese glyphs: " + missing);
            font.name = "CombatVietnamese";
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, FontPath);
            font.material.name = "CombatVietnamese Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (Texture2D atlas in font.atlasTextures)
            {
                atlas.name = "CombatVietnamese Atlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            EditorUtility.SetDirty(font);
            return font;
        }

        private static Material MaterialAsset(string name, Shader shader, string hex, bool emissive = false)
        {
            string path = AssetFolder + "/Combat_" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(shader) { name = "Combat_" + name };
            Color color = ColorFromHex(hex);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.12f);
            material.SetFloat("_Metallic", 0);
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.5f);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void RequireReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for compilation and exit Play Mode before scene setup or build.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or discard dirty scenes before setup or building.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static Color ColorFromHex(string hex) { ColorUtility.TryParseHtmlString(hex, out var color); return color; }
    }
}
#endif
