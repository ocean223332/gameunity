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
/// -story, -ending or -trailer &lt;outDir&gt;: it hides the interface, drives a scripted
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
    // The story film's photo (a PNG, -photo <path>), held in the hero's support hand in its first shot.
    private string photoPath;
    private Transform photo;
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
    private readonly List<Renderer> hidden = new List<Renderer>();
    private Array enemyPool;
    private FieldInfo enemyActive, enemyBody, enemyAttack;
    private MethodInfo applyDamage;
    private FieldInfo spawnTimer;
    private bool hasFocus, hasThreat, wasDashing;
    private CombatThirdPersonCamera orbit;
    private FieldInfo orbitMenuBlend, orbitAimBlend, orbitAimPoint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        string film = "trailer";
        int i = Array.IndexOf(args, "-trailer");
        if (i < 0) { film = "story"; i = Array.IndexOf(args, "-story"); }
        if (i < 0) { film = "ending"; i = Array.IndexOf(args, "-ending"); }
        if (i < 0 || i + 1 >= args.Length) return;
        var capture = new GameObject("IntroCinematic").AddComponent<IntroCinematic>();
        capture.film = film;
        capture.outDir = args[i + 1];
        int o = Array.IndexOf(args, "-introOnly");
        if (o >= 0 && o + 1 < args.Length) capture.only = new HashSet<string>(args[o + 1].Split(','));
        int ph = Array.IndexOf(args, "-photo");
        if (ph >= 0 && ph + 1 < args.Length) capture.photoPath = args[ph + 1];
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
        orbit = view.GetComponent<CombatThirdPersonCamera>();
        if (orbit == null) return false;
        orbit.enabled = false;
        // The presentation still reads the (disabled) orbit camera for the menu pose and the aim; the film feeds it.
        orbitMenuBlend = typeof(CombatThirdPersonCamera).GetField("menuBlend", BindingFlags.NonPublic | BindingFlags.Instance);
        orbitAimBlend = typeof(CombatThirdPersonCamera).GetField("aimBlend", BindingFlags.NonPublic | BindingFlags.Instance);
        orbitAimPoint = typeof(CombatThirdPersonCamera).GetField("aimPoint", BindingFlags.NonPublic | BindingFlags.Instance);
        game.enabled = false; // the film drives StepSimulation itself
        var canvas = GameObject.Find("CombatInterface");
        if (canvas != null) canvas.GetComponent<Canvas>().enabled = false;
        var audio = game.GetComponent<CombatAudio>();
        if (audio != null) audio.MuteMusic = true; // the score is mixed in afterwards
        foreach (var r in game.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == "EnemyFootMarker") r.enabled = false;
        foreach (Transform child in game.transform)
            if (child.name.StartsWith("Enemy_", StringComparison.Ordinal))
                enemies.Add(child);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        enemyPool = (Array)typeof(CombatGame).GetField("enemies", flags).GetValue(game);
        Type enemyType = enemyPool.GetType().GetElementType();
        enemyActive = enemyType.GetField("active");
        enemyBody = enemyType.GetField("body");
        enemyAttack = enemyType.GetField("attackTimer");
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

        if (film == "ending") Dusk(); else GoldenHour();
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
                        $"-c:v libx264 -preset veryfast -threads 4 -crf 12 -pix_fmt yuv420p \"{Path.Combine(outDir, "raw.mp4")}\"",
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

    /// <summary>
    /// Dusk for the ending, matching the shelter's: the last red sun low in the west behind him, a dimming sky, and
    /// the eastern ranges ahead catching the last light over a darker horizon where the flashes show.
    /// </summary>
    private static void Dusk()
    {
        Light sun = RenderSettings.sun;
        if (sun == null) return;
        sun.transform.rotation = Quaternion.Euler(6f, 72f, 0f);
        sun.color = new Color(1f, .5f, .28f);
        sun.intensity = 1.9f;
        RenderSettings.ambientIntensity = 1.25f;
        RenderSettings.ambientSkyColor = new Color(.32f, .36f, .5f);
        RenderSettings.fogColor = new Color(.5f, .4f, .38f);
        RenderSettings.fogDensity = .011f;
        Shader.SetGlobalVector("_EnvSunDir", -sun.transform.forward);
        Shader.SetGlobalColor("_EnvSkyZenith", new Color(.2f, .25f, .4f));
        Shader.SetGlobalColor("_EnvSkyHorizon", new Color(.62f, .45f, .4f));
        Shader.SetGlobalColor("_EnvSkyGlow", new Color(1f, .45f, .2f));
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
        else if (film == "ending") BuildEndingShots();
        else BuildStoryShots();
    }

    /// <summary>
    /// The in-engine half of the story intro (output/intro/STORYBOARD.md, E1–E3 and T), filmed in one run with the AK
    /// (-story &lt;dir&gt; [-photo &lt;png&gt;]). From the photo in his hand back to him standing in the clearing, then low
    /// behind him looking up as he stands at the ready, the enemy gathering ahead. He never walks or fires.
    /// </summary>
    private void BuildStoryShots()
    {
        heroRig.IdleHold = true; // the stand idle hold throughout: he never shoulders the AK
        Vector3 side = Vector3.Cross(Vector3.up, Front); // the hero's right while facing the enemy line

        // Unfilmed: a quiet run with the AK in hand, the hero already at his post in the west lane.
        shots.Add(new Shot
        {
            Name = "calm", Seconds = 2.5f, Record = false, Spawns = false, Fire = Never,
            Begin = () =>
            {
                UnityEngine.Random.InitState(1972);
                game.SelectWeapon(WeaponKind.Rifle);
                game.BeginRun();
                typeof(CombatGame).GetProperty("Wave").SetValue(game, 5);
                CallPrivate(game, "ResetWave");
                Teleport(-6f, 0f);
            },
            Camera = (u, t) => Place(Hero - Front * 3f + Vector3.up * 1.4f, Hero + Vector3.up, 46f)
        });
        // E1: the photo pinched in his support hand against the outside of the AK's handguard, filling the frame face-on
        // from his left (the edit dissolves to it from the archive face it shows); then the camera pulls back and up
        // to him standing in the clearing.
        shots.Add(new Shot
        {
            Name = "photo", Seconds = 7f, Spawns = false, Fire = Never,
            Begin = ShowPhoto,
            Camera = (u, t) =>
            {
                Vector3 held = PlacePhoto();
                float e = Ease(Mathf.InverseLerp(.3f, 1f, u));
                Vector3 near = held - side * .23f;
                Vector3 far = Hero - Front * 3.4f - side * 1.4f + Vector3.up * 2.4f;
                Vector3 subject = Vector3.Lerp(held, Hero + Front * 1.5f + Vector3.up * 1.2f, e);
                Place(Vector3.Lerp(near, far, e), subject, Mathf.Lerp(32f, 46f, e), 0, Mathf.Lerp(.05f, .15f, e));
                Lens(true, Vector3.Lerp(held, Hero + Vector3.up * 1.4f, e), 4f);
            }
        });
        // E2: low behind him, looking up past his shoulder at the clearing ahead.
        shots.Add(new Shot
        {
            Name = "behind", Seconds = 8.5f, Spawns = false, Fire = Never,
            Begin = () => { if (photo != null) photo.gameObject.SetActive(false); },
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-44f, -34f, e), 0) * -Front;
                Vector3 p = Hero + around * Mathf.Lerp(3.6f, 2.9f, e) + Vector3.up * .3f;
                Place(p, Hero + Front * 1.6f + Vector3.up * 1.55f, 46f, 0, .2f);
                Lens(true, Hero + Vector3.up * 1.5f, 3.5f);
            }
        });
        // E3: closer behind him; the enemy appears at the treeline ahead and holds there.
        shots.Add(new Shot
        {
            Name = "ready", Seconds = 8.5f, Fire = Never,
            Tick = t =>
            {
                if (t < 4.5f) spawnTimer.SetValue(game, 1f);
                HoldLine(7f);
            },
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 around = Quaternion.Euler(0, Mathf.Lerp(-24f, -16f, e), 0) * -Front;
                Vector3 p = Hero + around * Mathf.Lerp(2.4f, 2.0f, e) + Vector3.up * .5f;
                Place(p, Hero + Front * 1.8f + Vector3.up * 1.5f, 44f, 0, .2f);
                ClearLine(p, Hero, .8f);
                Lens(true, Hero + Vector3.up * 1.4f, 2.8f);
            }
        });
        // T: crane up and back behind him toward the low sun, the enemy line ahead, under the title.
        shots.Add(new Shot
        {
            Name = "crane", Seconds = 8.5f, Spawns = false, Fire = Never,
            Tick = t => HoldLine(7f),
            Camera = (u, t) =>
            {
                float e = Ease(u);
                Vector3 p = Hero - Front * Mathf.Lerp(1.9f, 8.5f, e) + side * Mathf.Lerp(.8f, 1.8f, e) + Vector3.up * Mathf.Lerp(1.5f, 6.0f, e);
                Place(p, Hero + Front * Mathf.Lerp(6f, 15f, e) + Vector3.up * Mathf.Lerp(1.6f, 1.2f, e), Mathf.Lerp(46f, 54f, e), 0, .2f);
                ClearLine(p, Hero, 1f);
                Lens(false);
            }
        });
    }

    /// <summary>
    /// The intro's ending in one unbroken shot at dusk (-ending &lt;dir&gt; -photo &lt;png&gt;; docs/INTRO_VIDEO.md):
    /// beside his cheek onto the print in his hand, pushing in slowly; then the camera glides over his shoulder and
    /// helmet, rises and pulls back over the clearing and the road, and climbs above the treetops to the ranges in
    /// the east. The enemy line stands at the far side of the clearing throughout. He never walks or fires.
    /// </summary>
    private void BuildEndingShots()
    {
        heroRig.IdleHold = true;
        Vector3 side = Vector3.Cross(Vector3.up, Front); // the hero's right while facing east
        const float Hold = 9f, Rise = 9.5f, Length = 22f; // the print alone until Hold (the edit's 99.0 s), then the crane

        shots.Add(new Shot
        {
            // Unrecorded: the enemy gathers ahead, so the line already stands there when the camera leaves the print.
            Name = "calm", Seconds = 6f, Record = false, Fire = Never,
            Begin = () =>
            {
                UnityEngine.Random.InitState(1972);
                game.SelectWeapon(WeaponKind.Rifle);
                game.BeginRun();
                typeof(CombatGame).GetProperty("Wave").SetValue(game, 5);
                CallPrivate(game, "ResetWave");
                Teleport(-6f, 0f);
            },
            Tick = t => { HoldLine(7f); HoldFire(); },
            Camera = (u, t) => Place(Hero - Front * 3f + Vector3.up * 1.4f, Hero + Vector3.up, 46f)
        });
        shots.Add(new Shot
        {
            Name = "ending", Seconds = Length, Spawns = false, Fire = Never,
            Begin = ShowPhoto,
            Tick = t => { HoldLine(7f); HoldFire(); },
            Camera = (u, t) =>
            {
                if (t < Hold)
                {
                    // Beside his left cheek, looking down onto the print; it pushes in very slowly. The print, not his
                    // hand, fills the frame.
                    Vector3 held = HoldPhoto(out Vector3 eye);
                    Vector3 cheek = eye - side * .11f - Front * .04f + Vector3.up * .02f;
                    Place(Vector3.Lerp(cheek, held, Mathf.Lerp(.34f, .4f, Ease(t / Hold))), held + side * .015f, 40f, 0, .03f);
                    Lens(true, held, 4f);
                    return;
                }
                // The edit dips to black at Hold (animatic.py); from there the print is put away, both hands are back on
                // the AK in the stand idle, and the camera starts behind his helmet, looking past it down the clearing.
                // Then it cranes back and up over the clearing and above the treetops.
                if (photo != null && photo.gameObject.activeSelf) photo.gameObject.SetActive(false);
                Transform head = heroBody != null ? heroBody.GetBoneTransform(HumanBodyBones.Head) : null;
                Vector3 top = head != null ? head.position : Hero + Vector3.up * 1.6f;
                Vector3 behindHelmet = top - side * .12f + Vector3.up * .42f - Front * .85f;
                Vector3 ahead = Hero + Front * 5f + Vector3.up * .4f;
                Vector3 p, look;
                float k = Ease(Mathf.Clamp01((t - Hold) / Rise));
                p = Bezier(behindHelmet, behindHelmet - Front * 2f + Vector3.up * 1.5f, Hero - Front * 7f - side * 1.5f + Vector3.up * 12f,
                    Hero - Front * 6f - side * 1f + Vector3.up * 58f, k);
                p += Vector3.up * .4f * Mathf.Clamp01((t - Hold - Rise) / (Length - Hold - Rise));
                // The look runs down the clearing and the road, then lifts to the ranges in the east.
                look = Bezier(ahead, Hero + Front * 12f, Hero + Front * 40f + Vector3.up * 2f, Hero + Front * 200f + Vector3.up * 32f, k);
                Place(p, look, Mathf.Lerp(44f, 52f, k), 0, Mathf.Lerp(.04f, .1f, k));
                Lens(false);
            }
        });
    }

    private Animator heroBody;

    /// <summary>
    /// The ending's print on his left hand: the hand leaves the handguard (the right keeps the rifle) and holds the
    /// print on its open palm low in front of his chest, the picture tilted up to him; his head bows to it. The print is placed from the hand's own bones, so it never floats or sits on the rifle.
    /// Returns the print's centre and, through <paramref name="eye"/>, his eye line.
    /// </summary>
    private Vector3 HoldPhoto(out Vector3 eye)
    {
        if (heroBody == null) heroBody = heroRig.GetComponentInChildren<Animator>();
        Vector3 side = Vector3.Cross(Vector3.up, Front);
        Transform B(HumanBodyBones b) => heroBody != null && heroBody.isHuman ? heroBody.GetBoneTransform(b) : null;
        Transform head = B(HumanBodyBones.Head), chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
        Transform upper = B(HumanBodyBones.LeftUpperArm), lower = B(HumanBodyBones.LeftLowerArm), hand = B(HumanBodyBones.LeftHand);
        Transform middle = B(HumanBodyBones.LeftMiddleProximal), index = B(HumanBodyBones.LeftIndexProximal), little = B(HumanBodyBones.LeftLittleProximal) ?? B(HumanBodyBones.LeftRingProximal) ?? B(HumanBodyBones.LeftMiddleProximal);
        if (head == null || chest == null || upper == null || lower == null || hand == null || middle == null || index == null || little == null)
        {
            eye = Hero + Vector3.up * 1.6f;
            return PlacePhoto();
        }
        float scale = heroBody.transform.lossyScale.y;
        Vector3 at = chest.position + Front * (.36f * scale) - side * (.05f * scale) - Vector3.up * (.18f * scale);
        // Bow the head about half way toward the print.
        Vector3 bow = Vector3.Slerp(Front, (at - head.position).normalized, .55f);
        head.rotation = Quaternion.FromToRotation(Front, bow) * head.rotation;
        eye = head.position + Vector3.up * (.07f * scale) + bow * (.09f * scale);

        Vector3 away = (at - eye).normalized;                                  // the picture faces his eyes
        // The open palm turned up to him, fingers pointing ahead and a little to his right; the print lies on it with
        // its top toward his fingertips, so he reads it upright.
        Vector3 reach = Vector3.ProjectOnPlane(Front - side * .3f, away).normalized;
        Vector3 knuckleLine = Vector3.Cross(reach, away);
        if (Vector3.Dot(knuckleLine, side) > 0) knuckleLine = -knuckleLine;   // index toward his left on a palm-up left hand
        float palm = Vector3.Distance(hand.position, middle.position);
        const float HalfWidth = .045f, Above = .035f, Ahead = .02f; // Above clears the palm's skin, not just its bones
        Reach(upper, lower, hand, at - reach * (palm * .55f + Ahead) + away * Above, -Vector3.up - side * .6f);
        hand.rotation = Quaternion.FromToRotation(middle.position - hand.position, reach) * hand.rotation;
        Vector3 knuckles = Vector3.ProjectOnPlane(index.position - little.position, reach);
        hand.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(knuckles, knuckleLine, reach), reach) * hand.rotation;
        // The fingers come out of the rifle grip and lie open under the print, the tips lifting a little.
        Vector3 lay = Vector3.Slerp(reach, -away, .06f);
        // ValveBiped names its finger chains Finger1..4 (index..little), each with joints N, N1, N2; the avatar maps
        // only some of them. The tip joint has no child, so it takes the middle joint's bone axis.
        if (fingers == null)
        {
            var named = new Dictionary<string, Transform>();
            foreach (var c in heroBody.GetComponentsInChildren<Transform>()) named[c.name] = c;
            fingers = new List<Transform[]>();
            for (int f = 1; f <= 4; f++)
                if (named.TryGetValue($"ValveBiped.Bip01_L_Finger{f}", out var a) && named.TryGetValue($"ValveBiped.Bip01_L_Finger{f}1", out var m)
                    && named.TryGetValue($"ValveBiped.Bip01_L_Finger{f}2", out var e))
                    fingers.Add(new[] { a, m, e });
        }
        foreach (var chain in fingers)
        {
            Vector3 axis = Quaternion.Inverse(chain[1].rotation) * (chain[2].position - chain[1].position);
            for (int b = 0; b < 2; b++)
                chain[b].rotation = Quaternion.FromToRotation(chain[b + 1].position - chain[b].position, lay) * chain[b].rotation;
            chain[2].rotation = Quaternion.FromToRotation(chain[2].rotation * axis, lay) * chain[2].rotation;
        }
        if (photo == null) return at;
        // Resting on the palm and the open fingers.
        Vector3 centre = Vector3.Lerp(hand.position, middle.position, .55f) + reach * Ahead - away * Above;
        photo.SetPositionAndRotation(centre, Quaternion.LookRotation(away, -reach));
        // The thumb lies open beside the print's left edge, off the picture.
        PinThumb(centre + knuckleLine * (HalfWidth + .012f) - reach * .02f - away * (Above - .004f));
        return centre;
    }

    private Transform[] thumb;
    private List<Transform[]> fingers;

    /// <summary>Bends the left thumb (cyclic coordinate descent over its three bones) so its tip lands on <paramref name="target"/>.</summary>
    private void PinThumb(Vector3 target)
    {
        // The ValveBiped avatar leaves the thumb unmapped: fall back to its bones by name.
        Transform Bone(HumanBodyBones b, string name)
        {
            Transform t = heroBody.GetBoneTransform(b);
            if (t != null) return t;
            foreach (var c in heroBody.GetComponentsInChildren<Transform>()) if (c.name == name) return c;
            return null;
        }
        if (thumb == null)
            thumb = new[] { Bone(HumanBodyBones.LeftThumbProximal, "ValveBiped.Bip01_L_Finger0"),
                Bone(HumanBodyBones.LeftThumbIntermediate, "ValveBiped.Bip01_L_Finger01"), Bone(HumanBodyBones.LeftThumbDistal, "ValveBiped.Bip01_L_Finger02") };
        Transform[] bones = thumb;
        if (Array.IndexOf(bones, null) >= 0) { if (shotFrame == 0) log.WriteLine("thumb bones missing"); return; }
        float tipLength = Vector3.Distance(bones[1].position, bones[2].position) * .9f;
        Vector3 Tip() => bones[2].position + (bones[2].position - bones[1].position).normalized * tipLength;
        for (int pass = 0; pass < 6; pass++)
            for (int b = bones.Length - 1; b >= 0; b--)
                bones[b].rotation = Quaternion.FromToRotation(Tip() - bones[b].position, target - bones[b].position) * bones[b].rotation;
    }

    /// <summary>Two-bone reach: bends the elbow toward <paramref name="pole"/> so the hand lands on <paramref name="target"/>.</summary>
    private static void Reach(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
    {
        Vector3 a = upper.position;
        float la = Vector3.Distance(a, lower.position), lb = Vector3.Distance(lower.position, hand.position);
        Vector3 toTarget = target - a;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 1e-4f, la + lb - 1e-4f);
        Vector3 dir = toTarget.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole, dir).normalized;
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        Vector3 elbow = a + (dir * cosA + bend * Mathf.Sqrt(1f - cosA * cosA)) * la;
        upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, a + dir * d - lower.position) * lower.rotation;
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float s = 1f - t;
        return s * s * s * a + 3f * s * s * t * b + 3f * s * t * t * c + t * t * t * d;
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
            bool fire = shot.Fire == null || shot.Fire();
            orbitMenuBlend.SetValue(orbit, 0f);
            orbitAimBlend.SetValue(orbit, fire ? 1f : 0f);
            orbitAimPoint.SetValue(orbit, aimPoint + Vector3.up * .6f);
            game.SetViewInput(aimYaw, aimPoint, fire);
            game.StepSimulation(Time.deltaTime, shot.Move != null ? shot.Move(t) : Vector2.zero);
            if (recording && shotFrame % 60 == 0) log.WriteLine($"clock {shot.Name} {Time.deltaTime:F4} {Time.unscaledDeltaTime:F4} {TienTuyen.Presentation.CombatArtDirector.FrameSeconds:F4}");
            HidePickups();
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
        HideMarkers();
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
        heroRig.IdleHold = false;
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

    /// <summary>Gameplay markers (the hero's foot ring, enemy spawn warnings) stay out of the film.</summary>
    private void HideMarkers()
    {
        foreach (var r in player.GetComponentsInChildren<Renderer>())
            if (r.enabled && r.gameObject.name == "HeroFootMarker") { r.enabled = false; hidden.Add(r); }
        foreach (object enemy in enemyPool)
            if (enemyWarning.GetValue(enemy) is GameObject warning)
                foreach (var r in warning.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
    }

    /// <summary>Makes the photo prop: a 9 x 12 cm print (a thin box, so it reads from either side) from <see cref="photoPath"/>.</summary>
    private void ShowPhoto()
    {
        if (photo != null || string.IsNullOrEmpty(photoPath) || !File.Exists(photoPath)) return;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
        texture.LoadImage(File.ReadAllBytes(photoPath));
        var print = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(print.GetComponent<Collider>());
        print.name = "FilmPhoto";
        print.transform.localScale = new Vector3(.09f, .116f, .002f);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Smoothness", .2f);
        // A little of its own light, so the print in his shade still matches the archive frame it dissolves from.
        material.EnableKeyword("_EMISSION");
        material.SetTexture("_EmissionMap", texture);
        material.SetColor("_EmissionColor", Color.white * .35f);
        print.GetComponent<Renderer>().sharedMaterial = material;
        photo = print.transform;
    }

    /// <summary>Holds the photo upright on the outside of the support hand, its picture facing his left; returns its centre.</summary>
    private Vector3 PlacePhoto()
    {
        Transform hand = heroRig != null ? heroRig.SupportHand : null;
        Vector3 side = Vector3.Cross(Vector3.up, Front);
        Vector3 at = hand != null ? hand.position : Hero + Front * .4f + Vector3.up * 1.1f;
        if (photo == null) return at;
        at += Vector3.up * .05f - side * .05f;
        // Seen from its -Z side the box face shows the texture upside down: point +Z away from the lens, up down.
        photo.SetPositionAndRotation(at, Quaternion.LookRotation(side, Vector3.down));
        return at;
    }

    /// <summary>Keeps every enemy at least <paramref name="ahead"/> metres east of the hero: the line masses, it does not close.</summary>
    private void HoldLine(float ahead)
    {
        foreach (object enemy in enemyPool)
        {
            if (!(bool)enemyActive.GetValue(enemy)) continue;
            Transform body = ((GameObject)enemyBody.GetValue(enemy)).transform;
            Vector3 p = body.position;
            if (p.x < player.position.x + ahead) body.position = new Vector3(player.position.x + ahead, p.y, p.z);
        }
    }

    /// <summary>The line stands ready and does not shoot: the gunfire over the rise is on the soundtrack only.</summary>
    private void HoldFire()
    {
        foreach (object enemy in enemyPool)
            if ((bool)enemyActive.GetValue(enemy)) enemyAttack.SetValue(enemy, 1f);
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
            // Looked up each time: enemy visuals are rebuilt at runtime (models, ragdolls).
            foreach (var r in enemy.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.gameObject.name != "EnemyFootMarker") { r.enabled = false; hidden.Add(r); }
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
