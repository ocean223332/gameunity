using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Content;
using TienTuyen.Presentation;
using TienTuyen.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TienTuyen.Combat.Tests
{
    public sealed class BossShopGraphicsTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnityTest] public IEnumerator ShopAndRocketLaunchAndImpactHaveReadableGraphics()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("This QA case requires real graphics.");
            yield return SceneManager.LoadSceneAsync("CombatSpike");
            var game = Object.FindFirstObjectByType<CombatGame>();
            game.enabled = false;
            for (int i = 0; i < 30 && Object.FindFirstObjectByType<CombatThirdPersonCamera>() == null; i++) yield return null;
            game.BeginRun();
            game.ExecuteCheat("boss");
            game.StepSimulation(6.1f, Vector2.zero);
            game.DamageCommander(100000f);
            game.StepSimulation(5.1f, Vector2.zero);
            while (game.State == CombatState.Upgrade) Assert.That(game.ChooseUpgrade(0), Is.True);
            Assert.That(game.State, Is.EqualTo(CombatState.Shop));
            var offers = new[] {
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 1, 30),
                new ShopOffer(ContentIds.Rpg18, OfferKind.Weapon, 2, 60),
                new ShopOffer(ContentIds.Rifle, OfferKind.Weapon, 1, 20),
                new ShopOffer(ContentIds.Smg, OfferKind.Weapon, 1, 18) };
            typeof(CombatGame).GetField("<Shop>k__BackingField", Private).SetValue(game, new ShopSession(game.Run, 5, offers));
            yield return null;
            yield return null;
            var camera = Camera.main;
            Capture(camera, "boss-shop-rpg-shop.png");
            game.ContinueWave();
            yield return new WaitForSecondsRealtime(2.8f);
            typeof(CombatGame).GetField("<WaveRemaining>k__BackingField", Private).SetValue(game, game.WaveRemaining - 5f);
            var follow = camera.GetComponent<CombatThirdPersonCamera>();
            follow.enabled = false;
            camera.transform.position = new Vector3(0, 2.8f, -4.5f);
            camera.transform.LookAt(new Vector3(0, .45f, 4));
            camera.fieldOfView = 56;
            var effects = game.GetComponent<RocketEffects>();
            var launch = typeof(CombatGame).GetMethod("LaunchRocket", Private);
            var update = typeof(CombatGame).GetMethod("UpdateRockets", Private);
            launch.Invoke(game, new object[] { new Vector3(2, 1.45f, -.3f), new Vector3(3, .08f, 4), 0f, 2.5f, false });
            update.Invoke(game, new object[] { .08f });
            effects.Tick(.03f);
            AssertParticles(game, "RocketLaunchFlame", "RocketTrailFlame", "RocketTrailSmoke");
            Capture(camera, "boss-shop-rpg-launch.png");
            update.Invoke(game, new object[] { .5f });
            effects.Tick(.06f);
            AssertParticles(game, "RocketImpactFire", "RocketImpactSparks", "RocketImpactSmoke");
            Capture(camera, "boss-shop-rpg-impact.png");
            effects.Tick(.18f);
            AssertParticles(game, "RocketImpactFire", "RocketGroundDust");
            Capture(camera, "rpg-big-fire.png");
            effects.Tick(.65f);
            AssertParticles(game, "RocketImpactSmoke");
            yield return null;
            Capture(camera, "rpg-big-smoke.png");
            effects.Tick(1.7f);
            Assert.That(effects.ActiveScorchCount, Is.GreaterThan(0));
            Capture(camera, "boss-shop-rpg-scorch.png");
            Assert.That(game.transform.Find("RocketEffectsPool"), Is.Not.Null);
            var old = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Boss shop graphics cleanup"));
            yield return SceneManager.UnloadSceneAsync(old);
        }
        static void AssertParticles(CombatGame game, params string[] names)
        {
            var particles = game.GetComponentsInChildren<ParticleSystem>();
            foreach (var name in names)
                Assert.That(particles.Where(p => p.name == name).Sum(p => p.particleCount), Is.GreaterThan(0), name);
        }
        static void Capture(Camera camera, string name)
        {
            var canvas = GameObject.Find("CombatInterface").GetComponent<Canvas>();
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            var oldPlane = canvas.planeDistance;
            var oldTarget = camera.targetTexture;
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1280, 720, 24);
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
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/" + name));
                File.WriteAllBytes(path, texture.EncodeToPNG());
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
    }
}