using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class WeaponViewLifecycleTests
    {
        private CombatGame game;
        private CombatThirdPersonCamera controller;
        private Camera camera;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike");
            game = Object.FindFirstObjectByType<CombatGame>();
            game.enabled = false;
            for (int frame = 0; frame < 20 && Object.FindFirstObjectByType<CombatThirdPersonCamera>() == null; frame++)
                yield return null;
            controller = Object.FindFirstObjectByType<CombatThirdPersonCamera>();
            Assert.That(controller, Is.Not.Null, "Presentation must create its camera controller before the test starts.");
            camera = controller.GetComponent<Camera>();
            game.BeginRun();
            controller.SetFirstPerson(true);
            yield return new WaitForSeconds(.7f);
            Assert.That(camera.GetComponent<CombatFirstPersonView>().Active, Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var scene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Weapon lifecycle cleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator DestroyingAndRecreatingViewDoesNotLeaveOldArmsForTheNewOverlayToDraw()
        {
            // DuelPlayView.Dispose removes this component while retaining the main camera for replay.
            Object.Destroy(camera.GetComponent<CombatFirstPersonView>());
            yield return null;
            Assert.That(camera.transform.Cast<Transform>().Count(t => t.name == "ViewmodelCamera"), Is.Zero,
                "The old weapon camera and all its arms must leave with their owning view.");
            var next = camera.gameObject.AddComponent<CombatFirstPersonView>();
            next.Initialize(game, game.transform.Find("Player"), controller);
            yield return null;
            Assert.That(camera.transform.Cast<Transform>().Count(t => t.name == "ViewmodelCamera"), Is.EqualTo(1));
            Assert.That(next.Active, Is.True);
        }

        [UnityTest]
        public IEnumerator NormalRunRestartKeepsOneVisibleWeaponAndOneOverlay()
        {
            for (int i = 0; i < 3; i++)
            {
                game.ReturnToMenu();
                yield return null;
                game.BeginRun();
                controller.SetFirstPerson(true);
                yield return new WaitForSeconds(.7f);
                Assert.That(camera.transform.Cast<Transform>().Count(t => t.name == "ViewmodelCamera"), Is.EqualTo(1));
                var rig = camera.transform.Find("ViewmodelCamera/Viewmodel");
                Assert.That(rig.Cast<Transform>().Count(t => t.gameObject.activeSelf), Is.EqualTo(1));
            }
        }

        private void CaptureFirstPerson()
        {
            var canvas = GameObject.Find("CombatInterface").GetComponent<Canvas>();
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            var oldPlane = canvas.planeDistance;
            var oldTarget = camera.targetTexture;
            var target = RenderTexture.GetTemporary(1280, 720, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = .5f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Builds/bugfix-rpg-first-person.png"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
                System.IO.File.WriteAllBytes(output, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldPlane;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(texture);
            }
        }
        [UnityTest]
        public IEnumerator LauncherInFirstPersonHidesBodyAndUsesTheWeaponOverlayThenRestoresThirdPerson()
        {
            game.ExecuteCheat("boss");
            game.StepSimulation(6.1f, Vector2.zero);
            game.DamageCommander(100000);
            game.StepSimulation(5.1f, Vector2.zero);
            while (game.State == CombatState.Upgrade) game.ChooseUpgrade(0);
            if (game.State == CombatState.Shop) game.ContinueWave();
            game.SwitchWeapon(game.Run.LauncherSlot);
            yield return new WaitForSeconds(.7f);
            Assert.That(game.LauncherEquipped, Is.True);
            var firstPerson = camera.GetComponent<CombatFirstPersonView>();
            Assert.That(firstPerson.Active, Is.True, "RPG needs the same body hiding and depth-safe camera as guns.");
            var player = game.transform.Find("Player/HeroVisual");
            Assert.That(player.GetComponentsInChildren<Renderer>().All(r => r.forceRenderingOff || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly), Is.True);
            Assert.That(camera.transform.Find("ViewmodelCamera").GetComponentsInChildren<Renderer>().Any(r => r.gameObject.activeInHierarchy && r.name.IndexOf("rpg", System.StringComparison.OrdinalIgnoreCase) >= 0), Is.True,
                "The eye must actually draw the RPG, not just hide the normal gun.");
            var presentation = game.GetComponent<CommanderPresentation>();
            var worldGun = (Transform)typeof(CommanderPresentation).GetField("playerGun", Private).GetValue(presentation);
            Assert.That(worldGun.gameObject.activeSelf, Is.False);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CaptureFirstPerson();
            }
            controller.SetFirstPerson(false);
            yield return new WaitForSeconds(.7f);
            Assert.That(worldGun.gameObject.activeSelf, Is.True);
            Assert.That(firstPerson.Active, Is.False);
            Assert.That(player.GetComponentsInChildren<Renderer>().Any(r => !r.forceRenderingOff && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly), Is.True);
        }
    }
}
