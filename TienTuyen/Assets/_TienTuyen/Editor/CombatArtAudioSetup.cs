#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace TienTuyen.Editor
{
    /// <summary>Installs the art/audio presentation layer through Unity Editor APIs.</summary>
    public static class CombatArtAudioSetup
    {
        private const string ScenePath = "Assets/_TienTuyen/Scenes/CombatSpike.unity";
        private const string AudioFolder = "Assets/_TienTuyen/Audio";
        private const string MixerPath = AudioFolder + "/CombatMixer.mixer";
        private const string ModelResourceFolder = "Assets/_TienTuyen/Art/Resources/Models";
        private const string ModelSourceFolder = "Assets/_TienTuyen/Art/Source/FBX";

        [MenuItem("Tien Tuyen/Combat/Install Art + Audio Presentation")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Exit Play Mode and wait for compilation before installing presentation.");
            EnsureFolder(AudioFolder);
            EnsureFolder(AudioFolder + "/Resources");
            EnsureFolder(AudioFolder + "/Resources/CombatAudio");
            InstallRuntimeModels();
            var mixer = EnsureMixer();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var game = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TienTuyen.Combat.CombatGame>(true)).Single();
            var art = game.GetComponent<CombatArtDirector>() ?? game.gameObject.AddComponent<CombatArtDirector>();
            var audio = game.GetComponent<CombatAudio>() ?? game.gameObject.AddComponent<CombatAudio>();
            audio.Mixer = mixer;
            EditorUtility.SetDirty(game.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Combat presentation installed: " + (art != null) + ", audio mixer: " + (mixer != null) + ".");
        }

        private static void InstallRuntimeModels()
        {
            EnsureFolder("Assets/_TienTuyen/Art");
            EnsureFolder("Assets/_TienTuyen/Art/Resources");
            EnsureFolder(ModelResourceFolder);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] names = { "hero", "rifle", "smg", "shotgun", "enemy_infantry", "enemy_shooter", "enemy_charger", "enemy_elite", "supply_crate", "crate", "sandbag", "tarp", "tree", "rock", "bush", "grass" };
            foreach (string name in names)
            {
                string source = ModelSourceFolder + "/" + name + ".fbx";
                string destination = ModelResourceFolder + "/" + name + ".fbx";
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(source))
                {
                    Debug.LogWarning("Runtime model source missing: " + source);
                    continue;
                }
                string sourceFile = Path.Combine(projectRoot, source);
                string destinationFile = Path.Combine(projectRoot, destination);
                if (!File.Exists(destinationFile))
                {
                    if (!AssetDatabase.CopyAsset(source, destination))
                        throw new IOException("Could not copy runtime model: " + source + " -> " + destination);
                }
                else if (!FilesAreEqual(sourceFile, destinationFile))
                {
                    // Keep the destination .meta file (and its GUID/import settings) intact.
                    File.Copy(sourceFile, destinationFile, true);
                    Debug.Log("Updated runtime model: " + destination);
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool FilesAreEqual(string first, string second)
        {
            var firstInfo = new FileInfo(first);
            var secondInfo = new FileInfo(second);
            if (firstInfo.Length != secondInfo.Length) return false;

            using (var firstStream = File.OpenRead(first))
            using (var secondStream = File.OpenRead(second))
            {
                var firstBuffer = new byte[81920];
                var secondBuffer = new byte[81920];
                int count;
                while ((count = firstStream.Read(firstBuffer, 0, firstBuffer.Length)) != 0)
                {
                    int offset = 0;
                    while (offset < count)
                    {
                        int read = secondStream.Read(secondBuffer, offset, count - offset);
                        if (read == 0) return false;
                        offset += read;
                    }
                    for (int i = 0; i < count; i++)
                        if (firstBuffer[i] != secondBuffer[i]) return false;
                }
                return secondStream.ReadByte() == -1;
            }
        }

        /// <summary>Batch-safe build entry point: opens the saved scene before the existing validator runs.</summary>
        public static void BuildWindowsBatch()
        {
            // Keep the runtime Resources catalog aligned even when CI/builds are
            // invoked without first clicking the presentation installer menu item.
            InstallRuntimeModels();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log(CombatSceneSetup.BuildWindows());
        }

        public static void RebuildMixer()
        {
            AssetDatabase.DeleteAsset(MixerPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mixer = EnsureMixer();
            Debug.Log("Rebuilt combat mixer: " + (mixer != null));
        }

        private static AudioMixer EnsureMixer()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (existing != null) { TryCreateGroups(existing); return existing; }
            try
            {
                Type controllerType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
                if (controllerType != null)
                {
                    var create = controllerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name.IndexOf("CreateMixer", StringComparison.OrdinalIgnoreCase) >= 0 && m.GetParameters().Length == 1);
                    create?.Invoke(null, new object[] { MixerPath });
                }
                AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceSynchronousImport);
                var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
                if (mixer != null)
                {
                    TryCreateGroups(mixer);
                    return mixer;
                }
            }
            catch (Exception ex) { Debug.LogWarning("AudioMixer editor API unavailable; using routed AudioSources fallback: " + ex.Message); }
            return null;
        }

        private static void TryCreateGroups(AudioMixer mixer)
        {
            // Unity's mixer controller API is internal, so use reflection while staying
            // entirely inside the Editor.  On versions where the API changes, the runtime
            // remains valid and uses named AudioSources with the same independent volumes.
            try
            {
                Type controllerType = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
                if (controllerType == null) return;
                object controller = mixer;
                var method = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "CreateNewGroup" && m.GetParameters().Length == 2);
                var addChild = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "AddChildToParent" && m.GetParameters().Length == 2);
                var masterProperty = controllerType.GetProperty("masterGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null || addChild == null || masterProperty == null)
                {
                    Debug.LogWarning("Mixer group reflection incomplete: create=" + (method != null) + ", addChild=" + (addChild != null) + ", master=" + (masterProperty != null));
                    return;
                }
                object master = masterProperty.GetValue(controller);
                foreach (string groupName in new[] { "Music", "SFX", "UI", "Ambience" })
                {
                    if (mixer.FindMatchingGroups(groupName).Any(g => g.name == groupName)) continue;
                    object group = method.Invoke(controller, new object[] { groupName, false });
                    // Internal API takes (child, parent).
                    addChild.Invoke(controller, new[] { group, master });
                }
                var allGroups = controllerType.GetMethod("GetAllAudioGroupsSlow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(controller, null) as System.Collections.IEnumerable;
                if (allGroups != null)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var item in allGroups) names.Add(item == null ? "null" : item.ToString());
                    Debug.Log("Mixer group creation result: " + string.Join(";", names));
                }
                EditorUtility.SetDirty(mixer);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex) { Debug.LogWarning("Could not create named mixer groups: " + ex); }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
#endif

