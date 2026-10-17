using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    /// <summary>Throwaway slot check (sage): the live intro title over the valley and its hand-off to the menu. Never commit.</summary>
    public sealed class SlotIntroTitle
    {
        private const string Out = "D:/daiduong/gameunity/output/gameplay/slot/intro";
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [UnityTest, Timeout(300000)]
        public IEnumerator TitleOverTheValley()
        {
            Directory.CreateDirectory(Out);
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            var game = Object.FindFirstObjectByType<CombatGame>();
            for (int i = 0; i < 30; i++) yield return null;
            var gate = game.GetComponent<IntroVideoGate>() ?? game.gameObject.AddComponent<IntroVideoGate>();
            var t = typeof(IntroVideoGate);
            t.GetField("combatAudio", F).SetValue(gate, game.GetComponent<CombatAudio>());
            t.GetField("presentation", F).SetValue(gate, game.GetComponent<CombatPresentation>());
            t.GetMethod("BuildOverlay", F).Invoke(gate, null);
            t.GetMethod("BuildVideoPlayer", F).Invoke(gate, null);
            var overlay = (Canvas)t.GetField("overlay", F).GetValue(gate);
            var frame = overlay.transform.Find("IntroVideo").GetComponent<UnityEngine.UI.RawImage>();
            // Stand-in for the film's last frame: a flat dusk tone, so the dissolve is visible in the captures.
            var still = new Texture2D(2, 2);
            still.SetPixels(new[] { new Color(.32f, .28f, .3f), new Color(.32f, .28f, .3f), new Color(.5f, .42f, .4f), new Color(.5f, .42f, .4f) });
            still.Apply();
            frame.texture = still;
            frame.color = Color.white;
            t.GetField("frame", F).SetValue(gate, frame);
            t.GetField("playing", F).SetValue(gate, true);
            for (int i = 0; i < 60; i++) yield return null;
            var log = new List<string> { "state " + game.State + " preset " + PlayerPrefs.GetString(LightingPreset.LastKey, "?") };

            t.GetMethod("BeginTitle", F).Invoke(gate, null);
            var marks = new[] { .6f, 2.6f, 4.6f, 6.6f, 7.6f, 8.6f, 10f };
            int next = 0;
            float start = Time.unscaledTime, worst = 0f, worstAt = 0f;
            var presentation = game.GetComponent<CombatPresentation>();
            float menuAt = -1f;
            while (Time.unscaledTime - start < 10.5f)
            {
                yield return null;
                float now = Time.unscaledTime - start;
                if (Time.unscaledDeltaTime > worst) { worst = Time.unscaledDeltaTime; worstAt = now; }
                if (menuAt < 0f && presentation.enabled) menuAt = now;
                if (next < marks.Length && now >= marks[next])
                {
                    Capture(Out + "/title_" + marks[next].ToString("0.0") + ".png");
                    next++;
                }
            }
            log.Add($"menu shown at {menuAt:F2} s; worst frame {worst * 1000f:F1} ms at {worstAt:F2} s (capture frames included)");
            log.Add("overlay gone: " + (t.GetField("overlay", F).GetValue(gate) == null));

            // The drift under each preset, without the title: start, middle, end.
            var cam = Camera.main.GetComponent<CombatThirdPersonCamera>();
            var env = Object.FindFirstObjectByType<CombatEnvironment>();
            foreach (var preset in LightingPreset.All)
            {
                env.Apply(preset);
                foreach (float at in new[] { 0f, 4.5f, 9f })
                {
                    cam.TitleVista = 1f;
                    cam.TitleVistaTime = at;
                    for (int i = 0; i < 30; i++) yield return null;
                    Capture(Out + "/vista_" + preset.name + "_" + at.ToString("0.0") + ".png");
                }
            }
            cam.TitleVista = 0f;
            Object.Destroy(still);
            File.WriteAllLines(Out + "/title.log", log);
        }

        private static void Capture(string path)
        {
            var camera = Camera.main;
            var canvases = new List<(Canvas c, RenderMode m)>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy)
                {
                    canvases.Add((c, c.renderMode));
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = camera;
                    c.planeDistance = camera.nearClipPlane + .05f + .01f * c.sortingOrder / 1000f;
                }
            var rt = RenderTexture.GetTemporary(1600, 900, 24);
            var old = camera.targetTexture;
            camera.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            camera.targetTexture = old;
            foreach (var (c, m) in canvases) c.renderMode = m;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
