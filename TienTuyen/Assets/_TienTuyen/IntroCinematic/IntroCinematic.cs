using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using TienTuyen.Combat;
using TienTuyen.Presentation;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

/// <summary>
/// Intro and trailer film capture. Only active when the player is launched with
/// -intro &lt;outDir&gt; or -trailer &lt;outDir&gt;: it hides the interface, drives a scripted
/// run and films it with a sequence of cinematic camera shots, piping frames to ffmpeg
/// and the game audio to a raw file. docs/INTRO_VIDEO.md describes the full pipeline.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed partial class IntroCinematic : MonoBehaviour
{
    private const int Fps = 60;

    private sealed class Shot
    {
        public string Name;
        public float Seconds;
        public bool Record = true;
        public Action Begin;
        /// <summary>Per-frame simulation input (game time t in seconds since shot start).</summary>
        public Func<float, Vector2> Move;
        /// <summary>Per-frame camera placement; u is 0..1 over the shot.</summary>
        public Action<float, float> Camera;
        /// <summary>Whether the hero holds the trigger this frame; null means always.</summary>
        public Func<bool> Fire;
        /// <summary>Whether the game keeps spawning enemies of its own.</summary>
        public bool Spawns = true;
        /// <summary>Whether enemies that reach the hero are cut down before they can crowd the frame.</summary>
        public bool Guard = true;
        /// <summary>Per-frame staging before the simulation step (t in seconds since shot start).</summary>
        public Action<float> Tick;
    }

    private string outDir, film;
    private HashSet<string> only;
    private int width = 2560, height = 1440;
    private CombatGame game;
    private Transform player;
    private CombatCharacterRig heroRig;
    private Camera view;
    private RenderTexture target;
    private Texture2D readback;
    private byte[] frameBuffer;
    private Process ffmpeg;
    private Stream videoPipe;
    private FileStream audioFile;
    private StreamWriter log;
    private DepthOfField dof;
    private readonly List<Shot> shots = new List<Shot>();
    private readonly List<Transform> enemies = new List<Transform>();
    private int shotIndex = -1, shotFrame, recordedFrames;
    private bool ready, finished, recording;
    private float trauma, noiseTime;
    private Vector3 smoothAim = Vector3.forward, smoothFocus, smoothThreat;
    private readonly HashSet<object> relocated = new HashSet<object>();
    private FieldInfo enemyPending, enemyWarning;
    private float aimYaw = 90f;
    private readonly Dictionary<Transform, Renderer[]> enemyRenderers = new Dictionary<Transform, Renderer[]>();
    private readonly List<Renderer> hidden = new List<Renderer>();
    private Array enemyPool;
    private FieldInfo enemyActive, enemyBody;
    private MethodInfo applyDamage;
    private FieldInfo spawnTimer;
    private bool hasFocus, hasThreat, wasDashing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        string film = "intro";
        int i = Array.IndexOf(args, "-intro");
        if (i < 0) { film = "trailer"; i = Array.IndexOf(args, "-trailer"); }
        if (i < 0 || i + 1 >= args.Length) return;
        var capture = new GameObject("IntroCinematic").AddComponent<IntroCinematic>();
        capture.film = film;
        capture.outDir = args[i + 1];
        int o = Array.IndexOf(args, "-introOnly");
        if (o >= 0 && o + 1 < args.Length) capture.only = new HashSet<string>(args[o + 1].Split(','));
        int s = Array.IndexOf(args, "-introSize");
        if (s >= 0 && s + 1 < args.Length)
        {
            string[] size = args[s + 1].Split('x');
            capture.width = int.Parse(size[0]);
            capture.height = int.Parse(size[1]);
        }
    }

    private void Start()
    {
        game = FindAnyObjectByType<CombatGame>();
        Time.captureFramerate = Fps;
        Application.runInBackground = true;
    }

    private bool TrySetup()
    {
        player = game.transform.Find("Player");
        heroRig = player != null ? player.GetComponentInChildren<CombatCharacterRig>() : null;
        view = Camera.main;
        if (heroRig == null || !heroRig.IsInitialized || view == null) return false;
        var orbit = view.GetComponent<CombatThirdPersonCamera>();
        if (orbit == null) return false;
        orbit.enabled = false;
        game.enabled = false; // the film drives StepSimulation itself
        var canvas = GameObject.Find("CombatInterface");
        if (canvas != null) canvas.GetComponent<Canvas>().enabled = false;
        var audio = game.GetComponent<CombatAudio>();
        if (audio != null) audio.MuteMusic = true; // the score is mixed in afterwards
        foreach (var r in game.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == "EnemyFootMarker") r.enabled = false;
        foreach (Transform child in game.transform)
            if (child.name.StartsWith("Enemy_", StringComparison.Ordinal))
            {
                enemies.Add(child);
                var renderers = new List<Renderer>();
                foreach (var r in child.GetComponentsInChildren<Renderer>(true))
                    if (r.gameObject.name != "EnemyFootMarker") renderers.Add(r);
                enemyRenderers[child] = renderers.ToArray();
            }
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        enemyPool = (Array)typeof(CombatGame).GetField("enemies", flags).GetValue(game);
        Type enemyType = enemyPool.GetType().GetElementType();
        enemyActive = enemyType.GetField("active");
        enemyBody = enemyType.GetField("body");
        enemyPending = enemyType.GetField("pending");
        enemyWarning = enemyType.GetField("warning");
        applyDamage = typeof(CombatGame).GetMethod("ApplyShotDamage", flags);
        spawnTimer = typeof(CombatGame).GetField("spawnTimer", flags);
        enemyCharging = enemyType.GetField("charging");
        pickupPool = (Array)typeof(CombatGame).GetField("pickups", flags).GetValue(game);
        Type pickupType = pickupPool.GetType().GetElementType();
        pickupActive = pickupType.GetField("active");
        pickupBody = pickupType.GetField("body");
        game.WeaponFired += OnHeroFired;
        game.EnemyDamaged += OnEnemyDamaged;

        GoldenHour();
        BuildLensVolume();
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
        target.Create();
        view.targetTexture = target;
        view.fieldOfView = 50f;
        readback = new Texture2D(width, height, TextureFormat.RGBA32, false);

        Directory.CreateDirectory(outDir);
        log = new StreamWriter(Path.Combine(outDir, "timeline.txt")) { AutoFlush = true };
        int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
        log.WriteLine($"size {width}x{height} fps {Fps} audio {AudioSettings.outputSampleRate} {channels}");
        ffmpeg = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {Fps} -i - -vf vflip " +
                        $"-c:v libx264 -preset fast -crf 12 -pix_fmt yuv420p \"{Path.Combine(outDir, "raw.mp4")}\"",
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
        });
        videoPipe = ffmpeg.StandardInput.BaseStream;
        audioFile = new FileStream(Path.Combine(outDir, "game_audio.f32"), FileMode.Create);
        AudioRenderer.Start();
        BuildShots();
        return true;
    }

    /// <summary>A lower, warmer sun than gameplay: long shadows and a sun disk the camera can catch.</summary>
    private static void GoldenHour()
    {
        Light sun = RenderSettings.sun;
        if (sun == null) return;
        sun.transform.rotation = Quaternion.Euler(19f, -142f, 0f);
        sun.color = new Color(1f, .78f, .52f);
        sun.intensity = 2.9f;
        RenderSettings.ambientIntensity = 1.3f;
        RenderSettings.fogColor = new Color(.78f, .74f, .62f);
        RenderSettings.fogDensity = .0135f;
        RenderSettings.ambientSkyColor = new Color(.52f, .6f, .66f);
    }

    private void BuildLensVolume()
    {
        var volume = new GameObject("IntroLens").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 200;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;
        dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(5f);
        dof.focalLength.Override(60f);
        dof.aperture.Override(2.8f);
        dof.active = false;
        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(.22f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(.34f);
        vignette.smoothness.Override(.45f);
        var aberration = profile.Add<ChromaticAberration>(true);
        aberration.intensity.Override(.08f);
    }

    // ---------------------------------------------------------------- shots

    private Vector3 Hero => player.position + Vector3.down * .8f; // feet
    private static readonly Vector3 Facing = Quaternion.Euler(0, 38f, 0) * Vector3.forward; // toward the sun
    /// <summary>The battle is staged west to east: the hero holds the west lane, the enemy comes from the east.</summary>
    private static readonly Vector3 Front = Vector3.right;

    private void BuildShots()
    {
        if (film == "trailer") BuildTrailerShots();
        else BuildIntroShots();
    }

    private void BuildIntroShots()
    {
        Vector3 right = Vector3.Cross(Vector3.up, Facing);
        Vector3 side = Vector3.Cross(Vector3.up, Front); // the hero's right while facing the enemy line

        // Over the canopy toward the clearing.
        shots.Add(new Shot
        {
            Name = "aerial", Seconds = 8f,
            Begin = () => heroRig.SnapAim(Facing),
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Vector3.Lerp(new Vector3(-92f, 0, -74f), new Vector3(-21f, 0, -16f), e);
                p.y = CombatEnvironment.GroundHeight(p.x, p.z) + Mathf.Lerp(34f, 17f, e);
                Place(p, Vector3.Lerp(new Vector3(8f, 4f, 8f), new Vector3(0, .5f, 0), e), 44f, Mathf.Lerp(-4f, 0f, e));
                Lens(false);
            }
        });
        // Inside the camp: the lone soldier, the supply truck beyond.
        shots.Add(new Shot
        {
            Name = "camp", Seconds = 5.5f,
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Vector3.Lerp(new Vector3(9.5f, 2.0f, 1.4f), new Vector3(7.6f, 2.2f, 4.6f), e);
                Place(p, Vector3.Lerp(new Vector3(-1.6f, 1.0f, -.9f), new Vector3(-1.0f, 1.0f, -.3f), e), 30f, 0, .25f);
                Lens(true, Hero + Vector3.up, 5.6f);
            }
        });
        // Low behind the hero, looking up at the banner against the sky.
        shots.Add(new Shot
        {
            Name = "flag", Seconds = 6f,
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-38f, -12f, e), 0) * -Facing;
                Vector3 p = Hero + around * Mathf.Lerp(2.1f, 1.6f, e) + Vector3.up * .45f;
                Place(p, Hero + Vector3.up * 2.25f - right * .35f + Facing * .6f, 46f);
                Lens(true, Hero + Vector3.up * 1.9f, 2.4f);
            }
        });
        // Low front three-quarter push-in on the hero.
        shots.Add(new Shot
        {
            Name = "hero", Seconds = 5.5f,
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(28f, 16f, e), 0) * Facing;
                Vector3 p = Hero + around * Mathf.Lerp(3.6f, 2.5f, e) + Vector3.up * .42f;
                Place(p, Hero + Vector3.up * 1.18f, 34f);
                Lens(true, Hero + Vector3.up * 1.4f, 2.2f);
            }
        });
        // Unfilmed: start the run at a late wave so every enemy type can appear.
        shots.Add(new Shot
        {
            Name = "preroll", Seconds = 5f, Record = false,
            Begin = () =>
            {
                UnityEngine.Random.InitState(1972);
                game.SelectWeapon(WeaponKind.Smg);
                game.BeginRun();
                typeof(CombatGame).GetProperty("Wave").SetValue(game, 5);
                CallPrivate(game, "ResetWave");
                Teleport(-6f, 0f);
            },
            Camera = (u, t) => Place(Hero + new Vector3(-6f, 3f, 0), Hero, 50f)
        });
        // Ground level in the grass as the enemy line comes on.
        shots.Add(new Shot
        {
            Name = "enemies", Seconds = 4.2f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                Vector3 threat = Threat();
                Vector3 p = Hero + Front * 2.4f + side * (1.2f - .5f * u) + Vector3.up * .3f;
                Place(p, threat + Vector3.up * 1.2f, 30f, 0, .35f);
                ClearLine(p, threat, 1.2f);
                Lens(true, threat + Vector3.up, 2f);
            }
        });
        shots.Add(new Shot
        {
            Name = "shoulder", Seconds = 4.5f,
            Begin = () => Teleport(-6f, .5f),
            Move = t => new Vector2(0, -.25f),
            Camera = (u, t) =>
            {
                Vector3 aim = Vector3.Slerp(Front, Aim(), .35f);
                Vector3 across = Vector3.Cross(Vector3.up, aim);
                Vector3 p = Hero - aim * Mathf.Lerp(2.6f, 2.0f, u) + across * .78f + Vector3.up * 1.75f;
                Place(p, Hero + aim * 12f + Vector3.up * 1.0f, 50f, 0, 1f);
                ClearLine(p, Hero, .6f);
                Lens(false);
            }
        });
        // Running the lane under fire, filmed from behind and to the side.
        shots.Add(new Shot
        {
            Name = "track", Seconds = 4.5f,
            Begin = () => Teleport(-6f, 6.3f),
            Move = t => new Vector2(0, -.45f * Mathf.Clamp01(t * 3f)),
            Camera = (u, t) =>
            {
                Vector3 p = Hero - Front * 3.7f - Vector3.forward * .4f + Vector3.up * 1.15f;
                Place(p, Hero + Front * 3f - Vector3.forward * .8f + Vector3.up * 1.1f, 42f, 0, .7f);
                ClearLine(p, Hero, .9f);
                Lens(true, Hero + Vector3.up, 2f);
            }
        });
        // In front of the muzzle, a little off the line of fire.
        shots.Add(new Shot
        {
            Name = "reverse", Seconds = 3.5f,
            Begin = () => Teleport(-6f, -1f),
            Camera = (u, t) =>
            {
                Vector3 p = Hero + Front * Mathf.Lerp(5.0f, 4.3f, u) + side * 1.9f + Vector3.up * .75f;
                Place(p, Hero + Vector3.up * 1.15f, 30f, 0, .45f);
                ClearLine(p, Hero, 1.1f);
                Lens(true, Hero + Vector3.up * 1.2f, 1.8f);
            }
        });
        // Profile medium shot; played back at half speed in the edit.
        shots.Add(new Shot
        {
            Name = "slowmo", Seconds = 3.2f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-62f, -48f, u), 0) * Front;
                Vector3 p = Hero + around * 3.1f + Vector3.up * .95f;
                Place(p, Hero + Vector3.up * 1.2f + Front * .6f, 34f, 0, .15f);
                ClearLine(p, Hero, 1f);
                Lens(true, Hero + Vector3.up * 1.3f, 1.6f);
            }
        });
        // Behind the hero, craning up and back over the battle toward the low sun.
        shots.Add(new Shot
        {
            Name = "finale", Seconds = 8f,
            Begin = () => Teleport(-6f, 0f),
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Hero - Front * Mathf.Lerp(1.9f, 7.5f, e) + side * Mathf.Lerp(.8f, 1.6f, e) + Vector3.up * Mathf.Lerp(1.5f, 5.2f, e);
                Place(p, Hero + Front * Mathf.Lerp(6f, 14f, e) + Vector3.up * Mathf.Lerp(1.6f, 1.2f, e), Mathf.Lerp(46f, 52f, e), 0, .2f);
                ClearLine(p, Hero, 1f);
                Lens(false);
            }
        });
    }

    // ---------------------------------------------------------------- frame loop

    private void Update()
    {
        if (finished || game == null) return;
        if (!ready)
        {
            if (!TrySetup()) return;
            ready = true;
            NextShot();
        }
        Shot shot = shots[shotIndex];
        if (game.State == CombatState.Playing)
        {
            // Keep the hero standing and the wave open for the whole film.
            var health = typeof(CombatGame).GetProperty("Health");
            if (game.Health < game.MaxHealth * .7f) health.SetValue(game, game.MaxHealth * .7f);
            if (game.WaveRemaining < 20f) typeof(CombatGame).GetProperty("WaveRemaining").SetValue(game, 30f);
            // A thinner, steadier stream of enemies than wave 5 really sends, so the hero stays in frame.
            if (!shot.Spawns) spawnTimer.SetValue(game, 1f);
            else if (game.AliveEnemies >= 8 && (float)spawnTimer.GetValue(game) < .4f) spawnTimer.SetValue(game, .4f);
            RelocateSpawns();
            if (shot.Guard) KeepDistance();
            float t = shotFrame / (float)Fps;
            shot.Tick?.Invoke(t);
            Vector3 aimPoint = AimPoint();
            game.SetViewInput(aimYaw, aimPoint, shot.Fire == null || shot.Fire());
            game.StepSimulation(Time.deltaTime, shot.Move != null ? shot.Move(t) : Vector2.zero);
            if (film == "trailer") HidePickups();
            // The edit syncs cuts and hits to these moments.
            bool dashing = game.ChargerDashActive;
            if (dashing && !wasDashing && recording) log.WriteLine($"charge {shot.Name} {recordedFrames}");
            wasDashing = dashing;
        }
    }

    private void LateUpdate()
    {
        if (!ready || finished) return;
        foreach (var r in hidden) if (r != null) r.enabled = true;
        hidden.Clear();
        Shot shot = shots[shotIndex];
        float u = Mathf.Clamp01(shotFrame / (shot.Seconds * Fps - 1f));
        Vector3 aim = game.PlayerAimDirection;
        if (aim.sqrMagnitude > .01f) smoothAim = Vector3.Slerp(smoothAim, aim, 1f - Mathf.Exp(-3.2f * Time.deltaTime)).normalized;
        trauma = Mathf.Max(0, trauma - Time.deltaTime * 2.2f);
        noiseTime += Time.deltaTime;
        shot.Camera(u, shotFrame / (float)Fps);
    }

    private void OnEndOfFrame()
    {
        if (!ready || finished) return;
        if (recording)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            RenderTexture.active = previous;
            NativeArray<byte> raw = readback.GetRawTextureData<byte>();
            if (frameBuffer == null || frameBuffer.Length != raw.Length) frameBuffer = new byte[raw.Length];
            raw.CopyTo(frameBuffer);
            videoPipe.Write(frameBuffer, 0, frameBuffer.Length);
            recordedFrames++;
        }
        int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
        int samples = AudioRenderer.GetSampleCountForCaptureFrame();
        using (var audio = new NativeArray<float>(samples * channels, Allocator.Temp))
        {
            AudioRenderer.Render(audio);
            if (recording)
            {
                var bytes = new byte[audio.Length * 4];
                Buffer.BlockCopy(audio.ToArray(), 0, bytes, 0, bytes.Length);
                audioFile.Write(bytes, 0, bytes.Length);
            }
        }
        shotFrame++;
        if (shotFrame >= Mathf.RoundToInt(shots[shotIndex].Seconds * Fps)) NextShot();
    }

    private void OnEnable() => StartCoroutine(EndOfFrameLoop());

    private System.Collections.IEnumerator EndOfFrameLoop()
    {
        var wait = new WaitForEndOfFrame();
        while (!finished)
        {
            yield return wait;
            OnEndOfFrame();
        }
    }

    private void NextShot()
    {
        if (recording) log.WriteLine($"end {shots[shotIndex].Name} {recordedFrames}");
        shotIndex++;
        shotFrame = 0;
        if (shotIndex >= shots.Count) { Finish(); return; }
        Shot shot = shots[shotIndex];
        recording = shot.Record && (only == null || only.Contains(shot.Name));
        if (recording) log.WriteLine($"shot {shot.Name} {recordedFrames}");
        hasFocus = false;
        trauma = 0;
        shot.Begin?.Invoke();
    }

    private void Finish()
    {
        finished = true;
        AudioRenderer.Stop();
        audioFile.Dispose();
        videoPipe.Close();
        ffmpeg.WaitForExit();
        log.WriteLine($"done {recordedFrames}");
        log.Dispose();
        Application.Quit();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Film heroics: every hit lands hard, so enemies drop in two or three shots.</summary>
    private void OnHeroFired(int slot, Vector3 start, Vector3 end)
    {
        trauma = Mathf.Min(1f, trauma + .12f);
        foreach (object enemy in enemyPool)
        {
            if (!(bool)enemyActive.GetValue(enemy)) continue;
            var body = (GameObject)enemyBody.GetValue(enemy);
            if (Vector3.Distance(Flat(body.transform.position), Flat(end)) > 1f) continue;
            applyDamage.Invoke(game, new object[] { enemy, 40f, false });
            break;
        }
    }

    private void OnEnemyDamaged(Transform actor, float damage, bool critical, bool killed)
    {
        if (!killed || !recording) return;
        log.WriteLine($"kill {shots[shotIndex].Name} {recordedFrames}");
        if (actor == subject) trauma = 1f;
    }

    private void KeepDistance()
    {
        foreach (object enemy in enemyPool)
        {
            if (!(bool)enemyActive.GetValue(enemy)) continue;
            var body = (GameObject)enemyBody.GetValue(enemy);
            if (Vector3.Distance(Flat(body.transform.position), Flat(player.position)) < 1.6f)
                applyDamage.Invoke(game, new object[] { enemy, 999f, false });
        }
    }

    /// <summary>New enemies appear along the east side of the clearing, facing the hero's lane.</summary>
    private void RelocateSpawns()
    {
        foreach (object enemy in enemyPool)
        {
            if (!(bool)enemyPending.GetValue(enemy)) { relocated.Remove(enemy); continue; }
            if (!relocated.Add(enemy)) continue;
            Vector2 at;
            do at = new Vector2(UnityEngine.Random.Range(7f, 14.5f), UnityEngine.Random.Range(-7.4f, 7.4f));
            while (InBox(at, 9f, 3.1f, 2.3f, 1.3f) || InBox(at, 10.6f, -3.7f, 1.4f, 1.5f));
            ((GameObject)enemyBody.GetValue(enemy)).transform.position = new Vector3(at.x, .8f, at.y);
            ((GameObject)enemyWarning.GetValue(enemy)).transform.position = new Vector3(at.x, .035f, at.y);
        }
    }

    private static bool InBox(Vector2 p, float x, float z, float width, float depth) =>
        Mathf.Abs(p.x - x) < width * .5f + .9f && Mathf.Abs(p.y - z) < depth * .5f + .9f;

    /// <summary>Sweeps the hero's aim smoothly onto the nearest enemy in front of the lane.</summary>
    private Vector3 AimPoint()
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        foreach (var enemy in enemies)
        {
            if (!enemy.gameObject.activeInHierarchy) continue;
            Vector3 to = Flat(enemy.position - player.position);
            if (Vector3.Angle(Front, to) > 60f) continue;
            if (to.magnitude < bestDistance) { bestDistance = to.magnitude; best = enemy; }
        }
        float wanted = best != null ? Mathf.Atan2(best.position.x - player.position.x, best.position.z - player.position.z) * Mathf.Rad2Deg : 90f;
        aimYaw = Mathf.MoveTowardsAngle(aimYaw, wanted, 140f * Time.deltaTime);
        return player.position + Quaternion.Euler(0, aimYaw, 0) * Vector3.forward * 12f;
    }

    private void Teleport(float x, float z)
    {
        player.position = new Vector3(x, .8f, z);
        smoothAim = Front;
    }

    /// <summary>Hides enemies standing between the lens and the subject for this frame.</summary>
    private void ClearLine(Vector3 camera, Vector3 subject, float radius)
    {
        Vector3 a = Flat(camera), b = Flat(subject), ab = b - a;
        foreach (var enemy in enemies)
        {
            if (!enemy.gameObject.activeInHierarchy) continue;
            Vector3 p = Flat(enemy.position);
            float along = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(.001f, ab.sqrMagnitude));
            if (along > .92f || Vector3.Distance(p, a + ab * along) > radius) continue;
            foreach (var r in enemyRenderers[enemy])
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
        }
    }

    private static void CallPrivate(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);

    private void Place(Vector3 position, Vector3 lookAt, float fov, float roll = 0f, float handheld = 0f)
    {
        position.y = Mathf.Max(position.y, CombatEnvironment.GroundHeight(position.x, position.z) + .25f);
        Quaternion rotation = Quaternion.LookRotation(lookAt - position, Vector3.up) * Quaternion.Euler(0, 0, roll);
        float drift = handheld * .55f, shake = trauma * trauma * 2.4f * Mathf.Max(handheld, .15f);
        rotation *= Quaternion.Euler(
            (Mathf.PerlinNoise(noiseTime * .6f, 1.7f) - .5f) * drift + (Mathf.PerlinNoise(noiseTime * 24f, 3.1f) - .5f) * shake,
            (Mathf.PerlinNoise(5.3f, noiseTime * .5f) - .5f) * drift + (Mathf.PerlinNoise(7.7f, noiseTime * 24f) - .5f) * shake,
            (Mathf.PerlinNoise(noiseTime * 24f, 11.3f) - .5f) * shake * .6f);
        view.transform.SetPositionAndRotation(position, rotation);
        view.fieldOfView = fov;
    }

    private void Lens(bool enabled, Vector3 subject = default, float aperture = 2.8f)
    {
        dof.active = enabled;
        if (!enabled) return;
        smoothFocus = hasFocus ? Vector3.Lerp(smoothFocus, subject, 1f - Mathf.Exp(-6f * Time.deltaTime)) : subject;
        hasFocus = true;
        dof.focusDistance.Override(Vector3.Dot(smoothFocus - view.transform.position, view.transform.forward));
        dof.aperture.Override(aperture);
        dof.focalLength.Override(Mathf.Clamp(24f / Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad) * .5f, 30f, 120f));
    }

    private Vector3 Aim() => Flat(smoothAim).sqrMagnitude > .01f ? Flat(smoothAim).normalized : Facing;

    /// <summary>Where the nearest enemies are, weighted toward the closest.</summary>
    private Vector3 Threat()
    {
        Vector3 sum = Vector3.zero;
        float weight = 0;
        foreach (var enemy in enemies)
        {
            if (!enemy.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(enemy.position, player.position);
            float w = 1f / (1f + d * d * .05f);
            sum += (enemy.position + Vector3.down * .8f) * w;
            weight += w;
        }
        Vector3 threat = weight > 0 ? sum / weight : Hero + Aim() * 8f;
        smoothThreat = hasThreat ? Vector3.Lerp(smoothThreat, threat, 1f - Mathf.Exp(-2.5f * Time.deltaTime)) : threat;
        hasThreat = true;
        return smoothThreat;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    private static Vector2 Planar(Vector3 v) => new Vector2(v.x, v.z);
    private static float Ease(float u) => u * u * (3f - 2f * u);
}
