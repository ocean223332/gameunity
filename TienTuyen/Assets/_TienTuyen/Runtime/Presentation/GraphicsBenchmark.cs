using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Development builds only: <c>TienTuyen.exe -benchmark DIR</c> skips the intro, holds the four graphics
    /// shots (valley, forest edge, crowded fight, enemy at the edge) and records FrameTimingManager CPU/GPU times
    /// and frame rate for each, with a screenshot, into DIR/benchmark.csv; then films two short clips at a fixed
    /// 60 Hz step (still camera, slow pan) for judging shimmer and ghosting, and quits. The fight is the real
    /// simulation (wave 5, auto-fire, hero invulnerable) so effects and AI are in the numbers.
    /// For comparisons on one build: <c>-aa smaa|msaa|stp</c> overrides the anti-aliasing, <c>-shadowres N</c> and
    /// <c>-shadowdist M</c> the sun shadows, and <c>-lighting NAME</c> pins the LightingPreset;
    /// <c>-florafade off</c> drops the plants' LOD cross-fade; <c>-renderscale X</c> and <c>-cascades N</c> set those URP
    /// values; <c>-shadowperheight M</c> limits plant shadows to M metres per metre of height; <c>-softquality
    /// Low|Medium|High</c> sets the sun's soft shadows; <c>-off a,b</c> switches features off (ssao, decals, volumetric, post, flora, opaquetex, softshadows, shafts; ssaonormals rebuilds the SSAO normals from
    /// depth) to price them; <c>-depthpriming Auto|Forced</c> sets the renderer's depth priming;
    /// <c>-passes</c> also writes each segment's GPU time per profiler sampler to <c>segment_passes.csv</c>;
    /// <c>-propshots a,b</c> only photographs those prop texture sets up close (see PropShots) and quits.
    /// </summary>
    public sealed class GraphicsBenchmark : MonoBehaviour
    {
        private const int Warmup = 60, Measured = 600;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private string outDir;
        private CombatGame game;
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private readonly StringBuilder csv = new StringBuilder(
            "segment,frames,fps_p50,fps_1pct_low,frame_ms_p50,frame_ms_p99,cpu_ms_p50,cpu_main_ms_p50,cpu_render_ms_p50,gpu_ms_p50,gpu_ms_p99,gfx_driver_mb,alive_enemies\n");

        /// <summary>True when the benchmark was asked for on the command line.</summary>
        public static bool Requested => Argument() != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string dir = Argument();
            if (dir == null || !Debug.isDebugBuild) return;
            var game = FindFirstObjectByType<CombatGame>();
            if (game == null) return;
            var bench = new GameObject("GraphicsBenchmark").AddComponent<GraphicsBenchmark>();
            bench.outDir = dir;
            bench.game = game;
        }

        private static string Argument(string name = "-benchmark")
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static string ApplyAntialiasing(Camera view)
        {
            string mode = Argument("-aa")?.ToLowerInvariant();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var data = view.GetComponent<UniversalAdditionalCameraData>();
            if (urp != null && int.TryParse(Argument("-shadowres"), out int resolution)) urp.mainLightShadowmapResolution = resolution;
            if (urp != null && float.TryParse(Argument("-shadowdist"), NumberStyles.Float, CultureInfo.InvariantCulture, out float distance)) urp.shadowDistance = distance;
            if (string.Equals(Argument("-florafade"), "off", StringComparison.OrdinalIgnoreCase)) FloraScatter.FadeBand = 0f;
            if (urp != null && float.TryParse(Argument("-renderscale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)) urp.renderScale = scale;
            if (urp != null && int.TryParse(Argument("-cascades"), out int cascades)) urp.shadowCascadeCount = cascades;
            if (float.TryParse(Argument("-shadowperheight"), NumberStyles.Float, CultureInfo.InvariantCulture, out float perHeight)) FloraScatter.ShadowMetresPerHeight = perHeight;
            if (Enum.TryParse(Argument("-softquality"), true, out SoftShadowQuality soft) && RenderSettings.sun != null)
                RenderSettings.sun.GetUniversalAdditionalLightData().softShadowQuality = soft;
            SwitchOff(urp, data);
            if (urp == null || data == null || mode == null) return "shipped";
            // The shipped setting is STP from the URP asset; the other modes switch it off.
            if (mode != "stp") urp.upscalingFilter = UpscalingFilterSelection.Auto;
            switch (mode)
            {
                case "msaa":
                    urp.msaaSampleCount = 4; view.allowMSAA = true; data.antialiasing = AntialiasingMode.None;
                    break;
                case "smaa":
                    urp.msaaSampleCount = 1; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    break;
            }
            return mode;
        }

        private static void SwitchOff(UniversalRenderPipelineAsset urp, UniversalAdditionalCameraData data)
        {
            var renderers = urp == null ? Array.Empty<ScriptableRendererData>()
                : (ScriptableRendererData[])typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", Private).GetValue(urp);
            if (Enum.TryParse(Argument("-depthpriming"), true, out DepthPrimingMode priming))
                foreach (var renderer in renderers)
                    if (renderer is UniversalRendererData universal) universal.depthPrimingMode = priming;
            string list = Argument("-off");
            if (list == null) return;
            foreach (string feature in list.ToLowerInvariant().Split(','))
                switch (feature)
                {
                    case "ssao":
                    case "ssaonormals":
                        foreach (var renderer in renderers)
                            if (renderer != null)
                                foreach (var rendererFeature in renderer.rendererFeatures)
                                {
                                    if (!(rendererFeature is ScreenSpaceAmbientOcclusion)) continue;
                                    if (feature == "ssao") { rendererFeature.SetActive(false); continue; }
                                    // SSAO rebuilt from depth alone, so the prepass need not write normals.
                                    var settings = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", Private).GetValue(rendererFeature);
                                    var source = settings.GetType().GetField("Source", Private);
                                    source.SetValue(settings, Enum.Parse(source.FieldType, "Depth"));
                                }
                        break;
                    case "decals":
                        foreach (var renderer in renderers)
                            if (renderer != null)
                                foreach (var rendererFeature in renderer.rendererFeatures)
                                    if (rendererFeature is DecalRendererFeature) rendererFeature.SetActive(false);
                        break;
                    case "volumetric":
                        foreach (var renderer in renderers)
                            if (renderer != null)
                                foreach (var rendererFeature in renderer.rendererFeatures)
                                    if (rendererFeature is VolumetricFogRendererFeature) rendererFeature.SetActive(false);
                        break;
                    case "post": if (data != null) data.renderPostProcessing = false; break;
                    case "flora": foreach (var flora in FindObjectsByType<FloraScatter>(FindObjectsSortMode.None)) flora.enabled = false; break;
                    case "opaquetex": if (urp != null) urp.supportsCameraOpaqueTexture = false; break;
                    case "softshadows": if (RenderSettings.sun != null) RenderSettings.sun.shadows = LightShadows.Hard; break;
                    case "shafts":
                        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            if (r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("LightShaft")) r.enabled = false;
                        break;
                }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(outDir);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            game.enabled = false;
            for (int i = 0; i < 300 && game.transform.Find("Player/HeroVisual") == null; i++) yield return null;
            game.BeginRun();
            typeof(CombatGame).GetField("<Wave>k__BackingField", Private).SetValue(game, 5);
            typeof(CombatGame).GetMethod("ResetWave", Private).Invoke(game, null);
            var player = (Transform)typeof(CombatGame).GetField("player", Private).GetValue(game);
            var view = Camera.main;
            var follow = view.GetComponent<CombatThirdPersonCamera>();
            for (int i = 0; i < 60; i++) yield return null;
            string aa = ApplyAntialiasing(view);
            string props = Argument("-propshots");
            if (props != null)
            {
                follow.enabled = false;
                yield return PropShots(view, follow.AimDistance, props.Split(','));
                Application.Quit();
                yield break;
            }

            // Scenery: no enemies yet, camera placed by hand.
            follow.enabled = false;
            player.position = new Vector3(0f, .8f, -8.5f);
            Place(view, new Vector3(.9f, 2f, -4.6f), new Vector3(-12f, -6f, -120f), 56f);
            yield return Measure("1_ref1_valley_mountains", null);
            var edge = new Vector3(-15f, 0f, 3f);
            edge.y = CombatEnvironment.GroundHeight(edge.x, edge.z) + 1.1f;
            Place(view, edge, edge + new Vector3(-40f, 5f, -22f), 56f);
            yield return Measure("2_ref2_forest_edge_sun", null);

            // Fill the arena with the hero holding fire, then fight live behind the shoulder camera.
            player.position = new Vector3(0f, .8f, 0f);
            follow.enabled = true;
            for (int i = 0; i < 3000 && game.AliveEnemies < 12 && game.State == CombatState.Playing; i++)
            {
                HoldFire();
                Step(1f / 60f);
                yield return null;
            }
            yield return Measure("3_crowded_combat", () => Step(Time.deltaTime));

            // The live enemy nearest the arena edge, where the ground cover grows, seen at aim FOV.
            // The fight is held still here, so nobody walks into the camera while it measures.
            var target = EdgeEnemy();
            follow.enabled = false;
            if (target != null) Place(view, ClearEye(target.position), target.position + Vector3.up * .9f, 40f);
            yield return Measure("4_enemy_in_grass", HoldFire);

            // Clips at the forest edge: the still one shows wind shimmer, the pan shows edge crawl and ghosting.
            Place(view, edge, edge + new Vector3(-40f, 5f, -22f), 56f);
            for (int i = 0; i < 30; i++) yield return null;
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 40; i++)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "clip_still_" + i.ToString("00") + ".png"));
                yield return null;
            }
            var look = view.transform.rotation;
            for (int i = 0; i < 40; i++)
            {
                view.transform.rotation = Quaternion.AngleAxis(i * .12f, Vector3.up) * look;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "clip_pan_" + i.ToString("00") + ".png"));
                yield return null;
            }
            Time.captureDeltaTime = 0f;
            yield return null;

            File.WriteAllText(Path.Combine(outDir, "benchmark.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outDir, "system.txt"),
                SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType + " | " + Screen.width + "x" + Screen.height +
                " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " | aa " + aa +
                " | lighting " + FindFirstObjectByType<CombatEnvironment>()?.Preset?.name +
                " | unity reserved MB " + Profiler.GetTotalReservedMemoryLong() / (1024 * 1024) +
                " | gfx driver MB " + Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024) + "\n");
            Application.Quit();
        }

        private void Step(float dt)
        {
            typeof(CombatGame).GetField("invulnerability", Private).SetValue(game, 9999f);
            game.StepSimulation(dt, Vector2.zero);
        }

        private void HoldFire()
        {
            var timers = (float[])typeof(CombatGame).GetField("weaponFireTimer", Private).GetValue(game);
            for (int i = 0; i < timers.Length; i++) timers[i] = 999f;
        }

        private Transform EdgeEnemy()
        {
            Transform best = null;
            float closest = float.MaxValue;
            foreach (var enemy in (Array)typeof(CombatGame).GetField("enemies", Private).GetValue(game))
            {
                if (enemy == null) continue;
                var type = enemy.GetType();
                if (!(bool)type.GetField("active").GetValue(enemy)) continue;
                var body = (GameObject)type.GetField("body").GetValue(enemy);
                if (body == null || !body.activeInHierarchy) continue;
                var p = body.transform.position;
                float edge = Mathf.Min(CombatEnvironment.ArenaHalfX - Mathf.Abs(p.x), CombatEnvironment.ArenaHalfZ - Mathf.Abs(p.z));
                if (edge < closest) { closest = edge; best = body.transform; }
            }
            return best;
        }

        /// <summary>
        /// An eye 3.5 m from the target, towards the arena centre where possible, with no other body within a metre
        /// of it or of the line of sight. The first clear one of 12 fixed bearings, so a run repeats.
        /// </summary>
        private Vector3 ClearEye(Vector3 target)
        {
            var bodies = new List<Vector3>();
            foreach (var enemy in (Array)typeof(CombatGame).GetField("enemies", Private).GetValue(game))
            {
                if (enemy == null) continue;
                var type = enemy.GetType();
                var body = (GameObject)type.GetField("body").GetValue(enemy);
                if ((bool)type.GetField("active").GetValue(enemy) && body != null && body.activeInHierarchy) bodies.Add(body.transform.position);
            }
            float toCentre = Mathf.Atan2(-target.x, -target.z);
            Vector3 eye = target;
            for (int i = 0; i < 12; i++)
            {
                float bearing = toCentre + (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * Mathf.PI / 6f;
                eye = target + new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * 3.5f;
                eye.y = CombatEnvironment.GroundHeight(eye.x, eye.z) + 1.5f;
                bool clear = true;
                Vector3 sight = target - eye;
                sight.y = 0f;
                foreach (var body in bodies)
                {
                    if ((body - target).sqrMagnitude < .01f) continue;
                    var flat = new Vector3(body.x, eye.y, body.z);
                    float along = Mathf.Clamp01(Vector3.Dot(flat - eye, sight) / sight.sqrMagnitude);
                    if (Vector3.Distance(flat, eye + sight * along) < 1f) { clear = false; break; }
                }
                if (clear) break;
            }
            return eye;
        }

        /// <summary>
        /// Each named prop texture set (its material) from four sides, the camera at the aiming shoulder camera's distance
        /// from the prop's near face, written to prop_NAME_SIDE.png: the closest a player sees it, to judge texture size.
        /// </summary>
        private IEnumerator PropShots(Camera view, float distance, string[] names)
        {
            float fov = view.fieldOfView;
            foreach (string name in names)
            {
                if (!PropBounds(name, out Bounds bounds)) { Debug.LogWarning("Benchmark prop shot: no material " + name); continue; }
                for (int side = 0; side < 4; side++)
                {
                    var direction = new Vector3(Mathf.Sin(side * Mathf.PI / 2f), 0f, Mathf.Cos(side * Mathf.PI / 2f));
                    float reach = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z;
                    var eye = bounds.center + direction * (reach + distance);
                    eye.y = CombatEnvironment.GroundHeight(eye.x, eye.z) + 1.7f;
                    Place(view, eye, bounds.center, fov);
                    for (int i = 0; i < 40; i++) yield return null;
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "prop_" + name + "_" + side + ".png"));
                    yield return null;
                    yield return null;
                }
            }
        }

        /// <summary>World bounds of the first merged prop submesh drawn with the material called <paramref name="name"/>.</summary>
        private static bool PropBounds(string name, out Bounds bounds)
        {
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !renderer.name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length && i < mesh.subMeshCount; i++)
                {
                    if (materials[i] == null || materials[i].name != name) continue;
                    var local = mesh.GetSubMesh(i).bounds;
                    bounds = new Bounds(renderer.transform.TransformPoint(local.center), Vector3.Scale(local.size, renderer.transform.lossyScale));
                    return true;
                }
            }
            bounds = default;
            return false;
        }

        private static void Place(Camera view, Vector3 at, Vector3 lookAt, float fov)
        {
            view.transform.SetPositionAndRotation(at, Quaternion.LookRotation(lookAt - at, Vector3.up));
            view.fieldOfView = fov;
        }

        private IEnumerator Measure(string segment, Action tick)
        {
            var frame = new List<double>(Measured); var cpu = new List<double>(); var main = new List<double>();
            var render = new List<double>(); var gpu = new List<double>();
            // One row per frame, to see where the slow ones go: main thread, render thread, waiting on present, GPU, or a GC.
            var rows = new StringBuilder("frame_ms,main_ms,render_ms,present_wait_ms,gpu_ms,gc\n");
            int collections = GC.CollectionCount(0);
            List<string> samplers = null;
            Recorder[] recorders = null;
            double[] passGpu = null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-passes") >= 0 && SystemInfo.supportsGpuRecorder)
            {
                samplers = new List<string>();
                Sampler.GetNames(samplers);
                recorders = new Recorder[samplers.Count];
                passGpu = new double[samplers.Count];
                for (int s = 0; s < samplers.Count; s++)
                {
                    recorders[s] = Recorder.Get(samplers[s]);
                    if (recorders[s].isValid) recorders[s].enabled = true;
                }
            }
            for (int i = 0; i < Warmup + Measured; i++)
            {
                tick?.Invoke();
                if (i == Warmup + Measured / 2) ScreenCapture.CaptureScreenshot(Path.Combine(outDir, segment + ".png"));
                yield return null;
                FrameTimingManager.CaptureFrameTimings();
                if (i < Warmup) continue;
                if (recorders != null)
                    for (int s = 0; s < recorders.Length; s++)
                        if (recorders[s].isValid) passGpu[s] += recorders[s].gpuElapsedNanoseconds;
                frame.Add(Time.unscaledDeltaTime * 1000.0);
                int gc = GC.CollectionCount(0) - collections;
                collections += gc;
                if (FrameTimingManager.GetLatestTimings(1, timing) == 0) continue;
                rows.AppendFormat(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5}\n", Time.unscaledDeltaTime * 1000.0,
                    timing[0].cpuMainThreadFrameTime, timing[0].cpuRenderThreadFrameTime, timing[0].cpuMainThreadPresentWaitTime, timing[0].gpuFrameTime, gc);
                cpu.Add(timing[0].cpuFrameTime); main.Add(timing[0].cpuMainThreadFrameTime);
                render.Add(timing[0].cpuRenderThreadFrameTime); gpu.Add(timing[0].gpuFrameTime);
            }
            File.WriteAllText(Path.Combine(outDir, segment + "_frames.csv"), rows.ToString());
            if (recorders != null)
            {
                // Samplers nest, so a parent's time includes its children's.
                var passes = new StringBuilder("sampler,gpu_ms_mean\n");
                var order = new List<int>();
                for (int s = 0; s < recorders.Length; s++)
                {
                    if (recorders[s].isValid) recorders[s].enabled = false;
                    if (passGpu[s] / Measured > 2e4) order.Add(s);
                }
                order.Sort((a, b) => passGpu[b].CompareTo(passGpu[a]));
                foreach (int s in order)
                    passes.AppendFormat(CultureInfo.InvariantCulture, "\"{0}\",{1:F3}\n", samplers[s].Replace("\"", "'"), passGpu[s] / Measured / 1e6);
                File.WriteAllText(Path.Combine(outDir, segment + "_passes.csv"), passes.ToString());
            }
            csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2:F1},{3:F1},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F2},{10:F2},{11},{12}\n",
                segment, frame.Count, 1000.0 / Percentile(frame, .5), 1000.0 / Percentile(frame, .99), Percentile(frame, .5), Percentile(frame, .99),
                Percentile(cpu, .5), Percentile(main, .5), Percentile(render, .5), Percentile(gpu, .5), Percentile(gpu, .99),
                Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024), game.AliveEnemies);
        }

        private static double Percentile(List<double> values, double q)
        {
            if (values.Count == 0) return double.NaN;
            values.Sort();
            return values[Mathf.Clamp((int)Math.Round(q * (values.Count - 1)), 0, values.Count - 1)];
        }
    }
}
