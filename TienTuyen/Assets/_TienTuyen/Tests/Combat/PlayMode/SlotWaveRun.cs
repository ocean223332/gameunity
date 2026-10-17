using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    /// <summary>Throwaway slot check (sage): a real-time wave 2 and wave 4, spawn-time log, flag count, two frames. Never commit.</summary>
    public sealed class SlotWaveRun
    {
        private const string Out = "D:/daiduong/gameunity/output/gameplay/slot";
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [UnityTest, Timeout(300000)]
        public IEnumerator RealWaves()
        {
            Directory.CreateDirectory(Out);
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            var game = Object.FindFirstObjectByType<CombatGame>();
            for (int i = 0; i < 10; i++) yield return null;
            game.BeginRun();
            var log = new List<string>();
            var entries = (System.Array)typeof(CombatGame).GetField("enemies", F).GetValue(game);
            int shot = 0;
            foreach (int wave in new[] { 2, 4 })
            {
                typeof(CombatGame).GetProperty("Wave").GetSetMethod(true).Invoke(game, new object[] { wave });
                typeof(CombatGame).GetMethod("ResetWave", F).Invoke(game, null);
                var announced = new bool[entries.Length];
                float start = Time.time;
                log.Add("wave " + wave + " squad " + CombatRules.SquadSize(wave) + " every " + CombatRules.SquadIntervalSeconds(wave) + " s");
                while (Time.time - start < 26f)
                {
                    typeof(CombatGame).GetField("invulnerability", F).SetValue(game, 999f);
                    if (game.SniperTutorialVisible) game.TogglePause();
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var e = entries.GetValue(i);
                        bool pending = (bool)e.GetType().GetField("pending", F).GetValue(e);
                        if (pending && !announced[i])
                        {
                            var def = (TienTuyen.Content.EnemyDefinition)e.GetType().GetField("definition", F).GetValue(e);
                            log.Add(string.Format("  t={0,6:F2}  {1,-8} alive={2}", game.WaveLength - game.WaveRemaining, def.Role, game.AliveEnemies));
                        }
                        announced[i] = pending;
                    }
                    if (shot < wave && Time.time - start > 12f) { Capture(Out + "/wave" + wave + ".png"); shot = wave; }
                    yield return null;
                }
                int flags = 0;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (r.enabled && r.gameObject.activeInHierarchy && r.name.StartsWith("banner", System.StringComparison.OrdinalIgnoreCase)) flags++;
                log.Add("  visible banner renderers: " + flags);
            }
            File.WriteAllLines(Out + "/spawns.log", log);
        }

        private static void Capture(string path)
        {
            var cam = Camera.main;
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex); Object.Destroy(rt);
        }
    }
}
