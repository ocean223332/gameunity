using System.Collections;
using NUnit.Framework;
using TienTuyen.Presentation.Interface;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TienTuyen.Combat.Tests
{
    /// <summary>The runtime interface: baked fonts, generated art and screens following the run state.</summary>
    public sealed class CombatInterfaceTests
    {
        private CombatGame game;
        private Transform canvas;

        [UnitySetUp]
        public IEnumerator LoadCombatScene()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            Assert.That(game, Is.Not.Null);
            yield return null;
            yield return null;
            canvas = GameObject.Find("CombatInterface")?.transform;
        }

        [UnityTearDown]
        public IEnumerator CleanScene()
        {
            var combatScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Interface test cleanup"));
            yield return SceneManager.UnloadSceneAsync(combatScene);
        }

        [Test]
        public void InterfaceUsesTheBakedVietnameseFonts()
        {
            Assert.That(canvas, Is.Not.Null, "The interface canvas is a scene root, not a child of the combat object.");
            Assert.That(canvas.IsChildOf(game.transform), Is.False);
            Assert.That(UiTheme.Display.name, Is.EqualTo("Display SDF"));
            Assert.That(UiTheme.Body.name, Is.EqualTo("Body SDF"));
            Assert.That(UiTheme.Display.HasCharacters("TIỀN TUYẾN ĐỢT TIẾP TẾ"), Is.True);
            Assert.That(UiTheme.Body.HasCharacters("Giữ trạm tiếp tế qua sáu đợt tấn công"), Is.True);
        }

        [Test]
        public void GeneratedGlyphsAndPanelsAreCrisp()
        {
            var rifle = UiGlyphs.Weapon("weapon.rifle");
            Assert.That(rifle.texture.width, Is.GreaterThanOrEqualTo(512));
            Assert.That(UiGlyphs.Weapon("weapon.smg"), Is.Not.SameAs(rifle));
            Assert.That(UiSprites.Rounded(6f).border.x, Is.GreaterThan(0f), "Panels must be 9-sliced to keep their corner radius.");
        }

        [UnityTest]
        public IEnumerator ScreensFollowTheRunState()
        {
            Assert.That(Screen("MainMenu"), Is.True);
            Assert.That(Screen("HUD"), Is.False);

            game.BeginRun();
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Screen("HUD"), Is.True);
            Assert.That(Screen("MainMenu"), Is.False);

            game.TogglePause();
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Screen("RunDialog"), Is.True);
            Assert.That(Screen("HUD"), Is.True, "The HUD stays visible, frozen, behind the pause menu.");

            game.TogglePause();
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Screen("RunDialog"), Is.False);
        }

        private bool Screen(string name)
        {
            var screen = canvas.Find(name);
            Assert.That(screen, Is.Not.Null, "Missing screen " + name);
            return screen.gameObject.activeSelf;
        }
    }
}
