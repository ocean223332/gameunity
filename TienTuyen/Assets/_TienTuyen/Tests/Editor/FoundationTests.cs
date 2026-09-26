using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TienTuyen.Tests
{
    // These verify the authored foundation only, not combat or gameplay.
    public class FoundationTests
    {
        private const string ScenePath = "Assets/_TienTuyen/Scenes/Bootstrap.unity";
        private Scene scene;
        private bool opened;

        [SetUp]
        public void LoadAuthoredScene()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            scene = SceneManager.GetSceneByPath(ScenePath);
            opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Assert.That(scene.isDirty, Is.False, "Save the authored scene before running foundation tests.");
        }

        [TearDown]
        public void RestoreSceneState()
        {
            if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        private T[] Components<T>() where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        [Test]
        public void CameraUsesTopDownOrthographicPostProcessing()
        {
            var cameras = Components<Camera>();
            Assert.That(cameras, Has.Length.EqualTo(1));
            var camera = cameras[0];
            Assert.That(camera.orthographic, Is.True);
            Assert.That(camera.transform.eulerAngles.x, Is.EqualTo(60f).Within(0.1f));
            Assert.That(camera.allowHDR, Is.True);
            Assert.That(camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing, Is.True);
        }

        [Test]
        public void GlobalVolumeHasSavedActiveOverrides()
        {
            var volumes = Components<Volume>();
            Assert.That(volumes, Has.Length.EqualTo(1));
            var volume = volumes[0];
            Assert.That(volume.isGlobal && volume.enabled, Is.True);
            var profile = volume.sharedProfile;
            Assert.That(profile, Is.Not.Null);
            Assert.That(AssetDatabase.Contains(profile), Is.True);
            Assert.That(EditorUtility.IsDirty(profile), Is.False, "Save the profile before testing.");
            foreach (var component in profile.components)
                Assert.That(EditorUtility.IsDirty(component), Is.False, "Save profile overrides before testing.");
            Assert.That(profile.TryGet<Tonemapping>(out var tone), Is.True);
            Assert.That(tone.mode.overrideState, Is.True);
            Assert.That(tone.mode.value, Is.EqualTo(TonemappingMode.ACES));
            Assert.That(profile.TryGet<Bloom>(out var bloom), Is.True);
            Assert.That(bloom.intensity.overrideState && bloom.intensity.value > 0, Is.True);
            var data = Components<Camera>()[0].GetComponent<UniversalAdditionalCameraData>();
            Assert.That(data.volumeLayerMask.value & (1 << volume.gameObject.layer), Is.Not.EqualTo(0));
        }

        [Test]
        public void PreviewMaterialsHaveSupportedShaders()
        {
            var renderers = Components<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(4));
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, renderer.name);
                    Assert.That(material.shader, Is.Not.Null, renderer.name);
                    Assert.That(material.shader.isSupported, Is.True, material.shader.name);
                    Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"));
                }
        }

        [Test]
        public void BuildUsesCombatSpikeAndLinearUrp()
        {
            var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
            Assert.That(enabled, Has.Length.EqualTo(1));
            Assert.That(enabled[0].path, Is.EqualTo("Assets/_TienTuyen/Scenes/CombatSpike.unity"));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(enabled[0].path), Is.Not.Null);
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear));
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.InstanceOf<UniversalRenderPipelineAsset>());
            Assert.That(((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).supportsHDR, Is.True);
        }
    }
}
