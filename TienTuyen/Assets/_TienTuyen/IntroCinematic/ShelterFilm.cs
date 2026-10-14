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
/// Films the intro's shelter takes (output/intro/STORYBOARD.md, 0:00–0:44): the radio in the A-frame shelter, built
/// by <see cref="ShelterSet"/> where the game's bunker stands, with the hero standing outside its mouth, his rifle leant inside. Only active when launched
/// with -shelter &lt;outDir&gt; or added by a capture test. Each take is written to &lt;outDir&gt;/&lt;take&gt;/ as raw.mp4
/// (60 fps, picture only) and timeline.txt, on the film clock (Time.captureFramerate), with clip second t matching
/// intro second t for the pull-back and the jolts landing at clip 1.0 s and 2.0 s (intro 44.0 and 45.0).
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class ShelterFilm : MonoBehaviour
{
    private const int Fps = 60;

    private sealed class Take
    {
        public string Name;
        public float Seconds;
        public bool Record = true;
        /// <summary>Per-frame staging; t in seconds since the take began.</summary>
        public Action<float> Tick;
        /// <summary>Per-frame camera; t in seconds since the take began.</summary>
        public Action<float> Camera;
    }

    private string outDir, photoPath;
    private HashSet<string> only;
    private int width = 1920, height = 1080;
    /// <summary>When above zero, writes every stride-th frame as a PNG instead of video (quick looks).</summary>
    private int stride;
    private CombatGame game;
    private Transform player;
    private CombatCharacterRig heroRig;
    private Camera view;
    private CombatThirdPersonCamera orbit;
    private FieldInfo orbitMenuBlend, orbitAimBlend, orbitAimPoint, spawnTimer;
    private RenderTexture target;
    private Texture2D readback;
    private byte[] frameBuffer;
    private Process ffmpeg;
    private Stream videoPipe;
    private StreamWriter log;
    private DepthOfField dof;
    private ShelterSet set;
    private readonly List<Take> takes = new List<Take>();
    private readonly List<Renderer> hidden = new List<Renderer>();
    private int takeIndex = -1, takeFrame;
    private bool ready, finished, recording;
    private float trauma, noiseTime;
    private Vector3 heroFeet, heroAim, smoothFocus;
    private bool hasFocus;
    private Animator heroBody;
    private readonly List<Renderer> hiddenOnHero = new List<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-shelter");
        if (i < 0 || i + 1 >= args.Length) return;
        var film = new GameObject("ShelterFilm").AddComponent<ShelterFilm>();
        film.outDir = args[i + 1];
        int o = Array.IndexOf(args, "-shelterOnly");
        if (o >= 0 && o + 1 < args.Length) film.only = new HashSet<string>(args[o + 1].Split(','));
        int p = Array.IndexOf(args, "-shelterPhoto");
        if (p >= 0 && p + 1 < args.Length) film.photoPath = args[p + 1];
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
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        orbitMenuBlend = typeof(CombatThirdPersonCamera).GetField("menuBlend", flags);
        orbitAimBlend = typeof(CombatThirdPersonCamera).GetField("aimBlend", flags);
        orbitAimPoint = typeof(CombatThirdPersonCamera).GetField("aimPoint", flags);
        spawnTimer = typeof(CombatGame).GetField("spawnTimer", flags);
        game.enabled = false; // the film drives StepSimulation itself
        var canvas = GameObject.Find("CombatInterface");
        if (canvas != null) canvas.GetComponent<Canvas>().enabled = false;
        var audio = game.GetComponent<CombatAudio>();
        if (audio != null) audio.MuteMusic = true;

        BuildSet();
        Dusk();
        BuildLensVolume();
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
        target.Create();
        view.targetTexture = target;
        view.nearClipPlane = .02f;
        readback = new Texture2D(width, height, TextureFormat.RGBA32, false);

        UnityEngine.Random.InitState(1972);
        game.SelectWeapon(WeaponKind.Rifle);
        game.BeginRun();
        heroRig.IdleHold = true;
        LeanRifle();
        BuildTakes();
        return true;
    }

    /// <summary>The shelter goes where the game's A-frame bunker stands (EnvironmentStation, mouth to the clearing).</summary>
    private void BuildSet()
    {
        var site = new GameObject("ShelterSite").transform;
        Quaternion yaw = Quaternion.Euler(0, 6f, 0);
        site.SetPositionAndRotation(new Vector3(-11.5f, 0, 16.2f) + yaw * new Vector3(0, 0, -1.3f), yaw);
        // Seat the floor on the highest ground under it so no terrain shows through.
        float floor = float.MinValue;
        foreach (var p in ShelterSet.FootprintPoints())
        {
            Vector3 w = site.TransformPoint(p);
            floor = Mathf.Max(floor, CombatEnvironment.GroundHeight(w.x, w.z));
        }
        site.position += Vector3.up * (floor + .02f);
        // The game's own bunker, its mound and ferns stand in the way: hide everything small inside the footprint.
        var names = new List<string>();
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.transform.IsChildOf(player) || !r.enabled) continue;
            Vector3 c = site.InverseTransformPoint(r.bounds.center);
            if (Mathf.Abs(c.x) > 3.4f || c.z < -1.8f || c.z > ShelterSet.Length + 1.5f || c.y < -1.5f || c.y > 4f) continue;
            if (r.bounds.size.magnitude > 16f) continue;
            r.enabled = false;
            names.Add(r.name);
        }
        set = ShelterSet.Build(site, photoPath);
        heroFeet = set.HeroSpot;
        heroFeet.y = Mathf.Max(heroFeet.y, CombatEnvironment.GroundHeight(heroFeet.x, heroFeet.z));
        // He stands at ease just outside the mouth, looking out and a little to the right; every CS:GO rifle idle
        // shoulders the gun, so the rifle leans inside and RestPose stands him at rest.
        heroAim = set.Root.TransformPoint(new Vector3(2.4f, 1.2f, -5f));
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "set.txt"), $"floor {floor:F3}\nhidden {string.Join(", ", names)}\n");
    }

    /// <summary>Dusk: a low red sun from the west and little sky light, so the lamp carries the inside.</summary>
    private static void Dusk()
    {
        Light sun = RenderSettings.sun;
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(9f, 72f, 0f);
            sun.color = new Color(1f, .58f, .34f);
            sun.intensity = 1.7f;
        }
        RenderSettings.ambientIntensity = .2f;
        RenderSettings.reflectionIntensity = .35f;
        RenderSettings.fogColor = new Color(.62f, .5f, .42f);
        RenderSettings.fogDensity = .012f;
    }

    private void BuildLensVolume()
    {
        var volume = new GameObject("ShelterLens").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 200;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;
        dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(1f);
        dof.focalLength.Override(50f);
        dof.aperture.Override(2.8f);
        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(.25f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(.38f);
        vignette.smoothness.Override(.45f);
    }

    // ---------------------------------------------------------------- takes

    private void BuildTakes()
    {
        Vector3 dial = set.DialCentre, normal = set.DialNormal, right = Vector3.Cross(Vector3.up, normal).normalized * -1f;
        Vector3 station = dial + right * ((ShelterSet.Station - .5f) * .3f * .75f);
        Vector3 L(float x, float y, float z) => set.Root.TransformPoint(new Vector3(x, y, z));
        Vector3 head = heroFeet + Vector3.up * 1.6f;

        // Unfilmed: the hero settles into the idle, the dust motes fill the air.
        takes.Add(new Take { Name = "settle", Seconds = 2f, Record = false, Camera = t => Place(L(0, 1f, -.7f), head, 44f) });
        // Only on request (-shelterOnly posecheck): his whole figure from his side and front, to judge the pose.
        Vector3 aimFlat = Flat(heroAim - heroFeet).normalized, aimSide = Vector3.Cross(Vector3.up, aimFlat);
        takes.Add(new Take
        {
            Name = "posecheck", Seconds = 1f, Record = only != null && only.Contains("posecheck"),
            Camera = t => { Place(heroFeet + (t < .5f ? aimSide : aimFlat) * 3.2f + Vector3.up * 1f, heroFeet + Vector3.up * .9f, 40f); Lens(heroFeet + Vector3.up * .9f, 11f); }
        });

        // 1. From the dial (the doorway glowing behind it), back past the letter and him to the back of the shelter,
        // looking out of the mouth onto the clearing (intro 0:00–0:44). The needle hunts while the radio is tuned
        // (0.35–3.0 s, slow under the announcer, a flick past the Morse) and settles on the station at 3.0 s.
        var path = new Spline(
            (0f, station + normal * .16f, station, 40f),
            (3f, station + normal * .19f + Vector3.up * .004f, station, 40f),
            (8f, dial + normal * .6f + Vector3.up * .14f - right * .02f, dial - Vector3.up * .03f - right * .1f, 40f),
            (15f, L(.45f, 1.2f, 1.9f), Vector3.Lerp(head, dial, .45f) - Vector3.up * .1f, 44f),
            (24f, L(.3f, 1.3f, ShelterSet.Length - .45f), L(-.1f, .85f, -1.5f), 52f),
            (44f, L(.32f, 1.33f, ShelterSet.Length - .27f), L(-.1f, .85f, -1.5f), 54f));
        float[] tuneTimes = { 0f, .35f, 1.0f, 1.6f, 2.0f, 2.15f, 2.5f, 2.8f, 3.0f };
        float[] tuneValues = { .30f, .30f, .47f, .52f, .58f, .63f, .685f, .645f, ShelterSet.Station };
        takes.Add(new Take
        {
            Name = "pullback", Seconds = 44f,
            Tick = t =>
            {
                set.Tuning = Keyed(tuneTimes, tuneValues, t) + (t > .35f && t < 3f ? (Mathf.PerlinNoise(t * 9f, 4.2f) - .5f) * .006f : 0f);
                set.DialGlow = .45f;
            },
            Camera = t =>
            {
                path.Evaluate(t, out Vector3 p, out Vector3 look, out float fov);
                Place(p, look, fov, .06f);
                Lens(t < 3f ? station : Vector3.Lerp(dial, head, Mathf.SmoothStep(0, 1, (t - 5f) / 10f)), t < 8f ? 2.0f : 2.8f);
            }
        });
        // 2. A spare: over his right shoulder, out onto the clearing he watches; he is the soft edge of the frame.
        Vector3 facing = Flat(heroAim - heroFeet).normalized, across = Vector3.Cross(Vector3.up, facing);
        takes.Add(new Take
        {
            Name = "soldier", Seconds = 8f,
            Tick = t => { set.Tuning = ShelterSet.Station; set.DialGlow = .45f; },
            Camera = t =>
            {
                float e = Mathf.SmoothStep(0, 1, t / 8f);
                Vector3 p = heroFeet - facing * Mathf.Lerp(.8f, .7f, e) + across * .38f + Vector3.up * 1.62f;
                Vector3 far = heroFeet + facing * 9f + across * .6f + Vector3.up * .9f;
                Place(p, far, 42f, .25f);
                Lens(far, 3.2f);
            }
        });
        // 3. Close on the radio as the song dies (0.5–0.95 s, the dial light fails) and the bombs land at 1.0 and 2.0 s.
        takes.Add(new Take
        {
            Name = "jolt_close", Seconds = 4f,
            Tick = t => { set.Tuning = ShelterSet.Station; set.DialGlow = DyingGlow(t); Hits(t); },
            Camera = t =>
            {
                Place(dial + normal * .5f + Vector3.up * .13f - right * .04f, dial - Vector3.up * .035f - right * .06f, 34f, .1f);
                Lens(dial, 2.4f);
            }
        });
        // 4. The same blasts from the back of the shelter, out through the mouth onto the clearing.
        takes.Add(new Take
        {
            Name = "jolt_wide", Seconds = 4f,
            Tick = t => { set.Tuning = ShelterSet.Station; set.DialGlow = DyingGlow(t); Hits(t); },
            Camera = t =>
            {
                Place(L(.3f, 1.35f, ShelterSet.Length - .3f), L(-.1f, .85f, -1.5f), 55f, .12f);
                Lens(heroFeet + Vector3.up * 1.3f, 4f);
            }
        });
    }

    /// <summary>Copies the rifle in his hands onto the logs behind the crate, and hides the held one and his banner.</summary>
    private void LeanRifle()
    {
        Transform held = heroRig.CurrentWeaponRoot;
        if (held == null) return;
        Vector3 axis = held.InverseTransformDirection(heroRig.MuzzleTransform.position - heroRig.WeaponGrip.position).normalized;
        var prop = Instantiate(held.gameObject);
        prop.name = "LeaningRifle";
        foreach (var behaviour in prop.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);
        foreach (Transform child in prop.GetComponentsInChildren<Transform>(true))
            if (child.name.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0) child.gameObject.SetActive(false);
        prop.transform.localScale = held.lossyScale;
        Vector3 along = (set.RifleTop - set.RifleFoot).normalized;
        prop.transform.rotation = Quaternion.FromToRotation(held.rotation * axis, along) * held.rotation;
        prop.transform.position = held.position;
        Bounds b = default;
        bool any = false;
        foreach (var r in prop.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }
        Vector3 middle = Vector3.Lerp(set.RifleFoot, set.RifleTop, .5f);
        if (any) prop.transform.position += new Vector3(middle.x - b.center.x, set.Root.position.y + .005f - b.min.y, middle.z - b.center.z);
        prop.transform.SetParent(set.Root, true);
        hiddenOnHero.AddRange(held.GetComponentsInChildren<Renderer>(true));
        // The game's back banner has no place in the shelter (the art director drives Relaxed, so hide it outright).
        if (heroRig.Banner != null) hiddenOnHero.AddRange(heroRig.Banner.Pivot.GetComponentsInChildren<Renderer>(true));
        heroBody = heroRig.GetComponentInChildren<Animator>();
    }

    /// <summary>
    /// His standing pose at rest, set in muscle space over whatever the rig played (every CS:GO rifle idle shoulders
    /// the gun): weight on the right leg, the left knee eased, hands clasped behind, a slow breath. Upright() stands the
    /// spine, neck and body pitch up by measurement. Seated on the ground the rig's idle stood on.
    /// </summary>
    private static readonly Dictionary<string, float> AtRest = new Dictionary<string, float>
    {
        ["Spine Front-Back"] = 0f, ["Chest Front-Back"] = -.02f, ["Neck Nod Down-Up"] = 0f, ["Head Nod Down-Up"] = .02f,
        ["Head Turn Left-Right"] = .06f,
        // Leg Front-Back runs negative forward and In-Out positive outward: feet a little apart, the left knee eased.
        ["Left Upper Leg Front-Back"] = -.05f, ["Left Upper Leg In-Out"] = .08f, ["Left Lower Leg Stretch"] = .95f,
        ["Right Upper Leg Front-Back"] = .02f, ["Right Upper Leg In-Out"] = .05f, ["Right Lower Leg Stretch"] = .99f,
        ["Spine Left-Right"] = .03f, ["Chest Left-Right"] = -.03f,
        // Plain upright standing; the arms start behind him and ClaspHands closes them at the small of his back.
        ["Left Shoulder Front-Back"] = -.1f, ["Right Shoulder Front-Back"] = -.1f,
        ["Left Arm Down-Up"] = -.6f, ["Left Arm Front-Back"] = -.45f, ["Left Forearm Stretch"] = .1f,
        ["Right Arm Down-Up"] = -.6f, ["Right Arm Front-Back"] = -.45f, ["Right Forearm Stretch"] = .1f,
    };

    private HumanPoseHandler poseHandler;
    private HumanPose humanPose;
    private float[] restMuscles;
    private float soleHeight = float.NaN;
    private bool poseLogged;

    private void RestPose()
    {
        foreach (var r in hiddenOnHero) if (r != null) r.enabled = false;
        if (heroBody == null || !heroBody.isHuman || heroBody.avatar == null) return;
        Transform leftFoot = heroBody.GetBoneTransform(HumanBodyBones.LeftFoot), rightFoot = heroBody.GetBoneTransform(HumanBodyBones.RightFoot);
        float Lowest() => Mathf.Min(leftFoot.position.y, rightFoot.position.y);
        if (poseHandler == null)
        {
            poseHandler = new HumanPoseHandler(heroBody.avatar, heroBody.transform);
            restMuscles = new float[HumanTrait.MuscleCount];
            var names = new List<string>(HumanTrait.MuscleName);
            for (int i = 0; i < restMuscles.Length; i++)
            {
                string n = names[i];
                if (n.Contains("Stretched"))
                {
                    // Fingers closed loosely round the other wrist.
                    restMuscles[i] = n.Contains("Thumb") ? .2f : n.Contains("Index") ? 0f : n.Contains("Middle") ? -.05f : n.Contains("Ring") ? -.1f : -.15f;
                }
                else if (n.Contains("Spread")) restMuscles[i] = n.Contains("Thumb") ? .2f : 0f;
            }
            var missing = new List<string>();
            foreach (var pair in AtRest)
            {
                int i = names.IndexOf(pair.Key);
                if (i >= 0) restMuscles[i] = pair.Value; else missing.Add(pair.Key);
            }
            if (missing.Count > 0) File.AppendAllText(Path.Combine(outDir, "set.txt"), $"unknown muscles {string.Join(", ", missing)}" + Environment.NewLine);
        }
        // The idle's soles stand on the ground: keep the first frame's ankle height and seat the rest pose on it.
        if (float.IsNaN(soleHeight)) soleHeight = Lowest() - heroFeet.y;
        poseHandler.GetHumanPose(ref humanPose);
        Array.Copy(restMuscles, humanPose.muscles, restMuscles.Length);
        float breath = Mathf.Sin(noiseTime * 1.5f);
        humanPose.muscles[Array.IndexOf(HumanTrait.MuscleName, "Chest Front-Back")] += .015f * breath;
        humanPose.muscles[Array.IndexOf(HumanTrait.MuscleName, "Head Turn Left-Right")] += .05f * (Mathf.PerlinNoise(noiseTime * .12f, 8.1f) - .5f);
        if (!poseLogged)
        {
            poseLogged = true;
            Upright();
        }
        Lean(humanPose.muscles);
        humanPose.bodyRotation = BodyRotation(bodyPitch);
        poseHandler.SetHumanPose(ref humanPose);
        Transform hips = heroBody.GetBoneTransform(HumanBodyBones.Hips);
        hips.position += Vector3.up * (heroFeet.y + soleHeight - Lowest());
        ClaspHands(hips);
    }

    private float bodyPitch = 17f, legsBack, neckNod, headNod;
    private int leftLegIndex, rightLegIndex, neckIndex, headIndex;

    private void Lean(float[] muscles)
    {
        muscles[leftLegIndex] = restMuscles[leftLegIndex] + legsBack;
        muscles[rightLegIndex] = restMuscles[rightLegIndex] + legsBack;
        muscles[neckIndex] = neckNod;
        muscles[headIndex] = headNod;
    }

    /// <summary>Body rotation in the handler root's space, turned to where he looks and pitched by <paramref name="pitch"/>.</summary>
    private Quaternion BodyRotation(float pitch) =>
        Quaternion.Inverse(heroBody.transform.rotation) * Quaternion.LookRotation(Flat(heroAim - heroFeet), Vector3.up) * Quaternion.Euler(pitch, -3f, -2f);

    /// <summary>
    /// Stands him up by measurement, once: the body pitch puts the neck over the hips, both Upper Leg Front-Back muscles
    /// the hips over the ankles, Neck Nod slopes the neck a little forward as a real one does, and Head Nod keeps his gaze
    /// where the rifle idle had it. A guessed pitch with zero muscles jackknifes this avatar.
    /// </summary>
    private void Upright()
    {
        leftLegIndex = Array.IndexOf(HumanTrait.MuscleName, "Left Upper Leg Front-Back");
        rightLegIndex = Array.IndexOf(HumanTrait.MuscleName, "Right Upper Leg Front-Back");
        neckIndex = Array.IndexOf(HumanTrait.MuscleName, "Neck Nod Down-Up");
        headIndex = Array.IndexOf(HumanTrait.MuscleName, "Head Nod Down-Up");
        neckNod = restMuscles[neckIndex];
        headNod = restMuscles[headIndex];
        Vector3 forward = Flat(heroAim - heroFeet).normalized;
        Transform hips = heroBody.GetBoneTransform(HumanBodyBones.Hips), neck = heroBody.GetBoneTransform(HumanBodyBones.Neck);
        Transform head = heroBody.GetBoneTransform(HumanBodyBones.Head);
        Transform leftFoot = heroBody.GetBoneTransform(HumanBodyBones.LeftFoot), rightFoot = heroBody.GetBoneTransform(HumanBodyBones.RightFoot);
        var pose = new HumanPose { muscles = new float[restMuscles.Length] };
        poseHandler.GetHumanPose(ref pose);
        float Ahead(Transform a, Vector3 b) => Vector3.Dot(a.position - b, forward);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float scale = heroBody.transform.lossyScale.y;
        Quaternion gaze = Quaternion.identity;
        // Hip over ankle, neck over hip, head bone ahead of the neck base, along his facing in metres; then head pitch
        // against the first pose's, in degrees.
        Vector4 Offsets()
        {
            Array.Copy(restMuscles, pose.muscles, restMuscles.Length);
            Lean(pose.muscles);
            pose.bodyRotation = BodyRotation(bodyPitch);
            poseHandler.SetHumanPose(ref pose);
            return new Vector4(Ahead(hips, (leftFoot.position + rightFoot.position) * .5f), Ahead(neck, hips.position),
                Ahead(head, neck.position) - .03f * scale, Vector3.SignedAngle(forward, head.rotation * Quaternion.Inverse(gaze) * forward, right));
        }
        Vector4 before = Offsets();
        gaze = head.rotation;
        var trace = new System.Text.StringBuilder();
        // Secant on each in turn; a few passes settle the coupling between them.
        for (int pass = 0; pass < 4; pass++)
        {
            bodyPitch = Mathf.Clamp(Solve(bodyPitch, 2f, x => { bodyPitch = Mathf.Clamp(x, -40f, 60f); return Offsets().y; }), -40f, 60f);
            legsBack = Mathf.Clamp(Solve(legsBack, .05f, x => { legsBack = Mathf.Clamp(x, -.6f, .6f); return Offsets().x; }), -.6f, .6f);
            neckNod = Mathf.Clamp(Solve(neckNod, .05f, x => { neckNod = Mathf.Clamp(x, -1f, 1f); return Offsets().z; }), -1f, 1f);
            headNod = Mathf.Clamp(Solve(headNod, .05f, x => { headNod = Mathf.Clamp(x, -1f, 1f); return Offsets().w; }), -1f, 1f);
            Vector4 o = Offsets();
            trace.Append($" [{pass}: pitch {bodyPitch:F1} legs {legsBack:F3} neck {neckNod:F3} head {headNod:F3} -> {o.x:F3} {o.y:F3} {o.z:F3} {o.w:F1}]");
        }
        File.AppendAllText(Path.Combine(outDir, "set.txt"),
            $"upright before (pitch 17): hip-over-ankle {before.x:F3} neck-over-hip {before.y:F3} head-ahead-of-neck {before.z + .03f * scale:F3} m;{trace}" + Environment.NewLine);
    }

    private static float Solve(float x0, float step, Func<float, float> f)
    {
        float x1 = x0 + step, f0 = f(x0), f1 = f(x1);
        for (int i = 0; i < 6 && Mathf.Abs(f1) > 1e-4f && Mathf.Abs(f1 - f0) > 1e-6f; i++)
        {
            float x2 = x1 - f1 * (x1 - x0) / (f1 - f0);
            x0 = x1; f0 = f1; x1 = x2; f1 = f(x1);
        }
        f(x1);
        return x1;
    }

    /// <summary>At ease: his hands meet at the small of his back, the right over the left wrist, elbows out and back.</summary>
    private void ClaspHands(Transform hips)
    {
        Vector3 forward = Flat(heroAim - heroFeet).normalized, right = Vector3.Cross(Vector3.up, forward);
        float scale = heroBody.transform.lossyScale.y;
        Vector3 small = hips.position - forward * (.15f * scale) - right * (.04f * scale) + Vector3.up * (.04f * scale);
        foreach (float side in new[] { -1f, 1f })
        {
            bool left = side < 0;
            Transform upper = heroBody.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = heroBody.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = heroBody.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform finger = heroBody.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (upper == null || lower == null || hand == null) continue;
            Vector3 target = small + right * (side * .045f * scale) - forward * ((left ? 0f : .015f) * scale) + Vector3.up * ((left ? 0f : .015f) * scale);
            Vector3 pole = right * side - forward * .6f;
            Reach(upper, lower, hand, target, pole);
            // Fingers across the back toward the other hand, palm in against him.
            if (finger != null)
                hand.rotation = Quaternion.FromToRotation(finger.position - hand.position, (-right * side - Vector3.up * .3f).normalized) * hand.rotation;
        }
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

    private static float DyingGlow(float t)
    {
        if (t < .5f) return .45f;
        if (t >= .95f) return 0f;
        float fade = 1f - (t - .5f) / .45f;
        return .45f * fade * (Mathf.PerlinNoise(t * 40f, 2.2f) > .45f ? 1f : .15f);
    }

    private float lastHitT = -1f;

    private void Hits(float t)
    {
        foreach (var (at, strength) in new[] { (1.0f, 1f), (2.0f, .7f) })
        {
            if (lastHitT < at && t >= at)
            {
                set.Impact(strength);
                trauma = Mathf.Max(trauma, strength);
                if (recording) log.WriteLine($"impact {takes[takeIndex].Name} {takeFrame}");
            }
        }
        lastHitT = t;
    }

    // ---------------------------------------------------------------- frame loop

    private void Update()
    {
        if (finished || game == null) return;
        if (!ready)
        {
            if (!TrySetup()) return;
            ready = true;
            NextTake();
        }
        Take take = takes[takeIndex];
        float t = takeFrame / (float)Fps;
        if (game.State == CombatState.Playing)
        {
            if (game.Health < game.MaxHealth) typeof(CombatGame).GetProperty("Health").SetValue(game, game.MaxHealth);
            if (game.WaveRemaining < 20f) typeof(CombatGame).GetProperty("WaveRemaining").SetValue(game, 30f);
            spawnTimer.SetValue(game, 1f);
            Vector3 aim = heroAim;
            float yaw = Mathf.Atan2(aim.x - heroFeet.x, aim.z - heroFeet.z) * Mathf.Rad2Deg;
            orbitMenuBlend.SetValue(orbit, 0f);
            orbitAimBlend.SetValue(orbit, 0f);
            orbitAimPoint.SetValue(orbit, aim);
            game.SetCrouchInput(false);
            game.SetViewInput(yaw, aim, false);
            game.StepSimulation(Time.deltaTime, Vector2.zero);
            // The game clamps the hero to the playfield; the shelter lies just beyond it.
            player.position = heroFeet + Vector3.up * .8f;
        }
        take.Tick?.Invoke(t);
        set.Tick(CombatArtDirector.FrameSeconds);
    }

    private void LateUpdate()
    {
        if (!ready || finished) return;
        RestPose();
        foreach (var r in hidden) if (r != null) r.enabled = true;
        hidden.Clear();
        foreach (var r in player.GetComponentsInChildren<Renderer>())
            if (r.enabled && r.gameObject.name == "HeroFootMarker") { r.enabled = false; hidden.Add(r); }
        trauma = Mathf.Max(0, trauma - Time.deltaTime * 1.6f);
        noiseTime += Time.deltaTime;
        takes[takeIndex].Camera(takeFrame / (float)Fps);
    }

    /// <summary>Whether this frame is kept; a batch runner renders the camera only then.</summary>
    private bool WantsFrame => ready && !finished && recording && (stride <= 0 || takeFrame % Stride == 0);

    /// <summary>Quick looks sample the short jolts five times as often.</summary>
    private int Stride => takes[takeIndex].Seconds < 5f ? Mathf.Max(1, stride / 5) : stride;

    private void OnEndOfFrame()
    {
        if (!ready || finished) return;
        if (WantsFrame)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            RenderTexture.active = previous;
            if (stride > 0)
            {
                File.WriteAllBytes(Path.Combine(outDir, takes[takeIndex].Name, $"f{takeFrame:D4}.png"), readback.EncodeToPNG());
            }
            else
            {
                NativeArray<byte> raw = readback.GetRawTextureData<byte>();
                if (frameBuffer == null || frameBuffer.Length != raw.Length) frameBuffer = new byte[raw.Length];
                raw.CopyTo(frameBuffer);
                videoPipe.Write(frameBuffer, 0, frameBuffer.Length);
            }
            if (takeFrame % Fps == 0)
                log.WriteLine($"clock {takeFrame} {Time.deltaTime:F4} {CombatArtDirector.FrameSeconds:F4}");
        }
        takeFrame++;
        if (takeFrame >= Mathf.RoundToInt(takes[takeIndex].Seconds * Fps)) NextTake();
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

    private void NextTake()
    {
        CloseTake();
        takeIndex++;
        takeFrame = 0;
        lastHitT = -1f;
        if (takeIndex >= takes.Count) { Finish(); return; }
        Take take = takes[takeIndex];
        recording = take.Record && (only == null || only.Contains(take.Name));
        set.Settle();
        trauma = 0;
        hasFocus = false;
        if (!recording) return;
        string dir = Path.Combine(outDir, take.Name);
        Directory.CreateDirectory(dir);
        log = new StreamWriter(Path.Combine(dir, "timeline.txt")) { AutoFlush = true };
        log.WriteLine($"size {width}x{height} fps {Fps} take {take.Name} seconds {take.Seconds}");
        if (stride > 0) return;
        ffmpeg = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {Fps} -i - -vf vflip " +
                        $"-c:v libx264 -preset veryfast -threads 4 -crf 12 -pix_fmt yuv420p \"{Path.Combine(dir, "raw.mp4")}\"",
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
        });
        videoPipe = ffmpeg.StandardInput.BaseStream;
    }

    private void CloseTake()
    {
        if (log != null)
        {
            log.WriteLine($"done {takeFrame}");
            log.Dispose();
            log = null;
        }
        if (videoPipe != null)
        {
            videoPipe.Close();
            ffmpeg.WaitForExit();
            videoPipe = null;
            ffmpeg = null;
        }
    }

    private void Finish()
    {
        finished = true;
        heroRig.IdleHold = false;
        Application.Quit();
    }

    // ---------------------------------------------------------------- camera

    private void Place(Vector3 position, Vector3 lookAt, float fov, float handheld = 0f)
    {
        Quaternion rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
        float drift = handheld * .55f, shake = trauma * trauma * 2.2f;
        rotation *= Quaternion.Euler(
            (Mathf.PerlinNoise(noiseTime * .6f, 1.7f) - .5f) * drift + (Mathf.PerlinNoise(noiseTime * 24f, 3.1f) - .5f) * shake,
            (Mathf.PerlinNoise(5.3f, noiseTime * .5f) - .5f) * drift + (Mathf.PerlinNoise(7.7f, noiseTime * 24f) - .5f) * shake,
            (Mathf.PerlinNoise(noiseTime * 24f, 11.3f) - .5f) * shake * .6f);
        view.transform.SetPositionAndRotation(position + Vector3.up * ((Mathf.PerlinNoise(noiseTime * 20f, 6.6f) - .5f) * .01f * shake), rotation);
        view.fieldOfView = fov;
    }

    private void Lens(Vector3 subject, float aperture)
    {
        smoothFocus = hasFocus ? Vector3.Lerp(smoothFocus, subject, 1f - Mathf.Exp(-6f * Time.deltaTime)) : subject;
        hasFocus = true;
        dof.focusDistance.Override(Mathf.Max(.05f, Vector3.Dot(smoothFocus - view.transform.position, view.transform.forward)));
        dof.aperture.Override(aperture);
        dof.focalLength.Override(Mathf.Clamp(24f / Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad) * .5f, 30f, 120f));
    }

    private static float Keyed(float[] times, float[] values, float t)
    {
        if (t <= times[0]) return values[0];
        for (int i = 1; i < times.Length; i++)
            if (t < times[i]) return Mathf.Lerp(values[i - 1], values[i], Mathf.SmoothStep(0, 1, (t - times[i - 1]) / (times[i] - times[i - 1])));
        return values[values.Length - 1];
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    /// <summary>A camera move through timed keys (position, look point, field of view), smooth through each key (Barry–Goldman Catmull–Rom), easing in and out at the ends.</summary>
    private sealed class Spline
    {
        private readonly float[] times;
        private readonly Vector3[] positions, looks;
        private readonly float[] fovs;

        public Spline(params (float time, Vector3 position, Vector3 look, float fov)[] keys)
        {
            int n = keys.Length;
            times = new float[n + 2];
            positions = new Vector3[n + 2];
            looks = new Vector3[n + 2];
            fovs = new float[n + 2];
            for (int i = 0; i < n; i++)
            {
                times[i + 1] = keys[i].time;
                positions[i + 1] = keys[i].position;
                looks[i + 1] = keys[i].look;
                fovs[i + 1] = keys[i].fov;
            }
            // Phantom end keys that repeat the ends: the move starts and stops gently.
            times[0] = times[1] - (times[2] - times[1]);
            times[n + 1] = times[n] + (times[n] - times[n - 1]);
            positions[0] = positions[1]; positions[n + 1] = positions[n];
            looks[0] = looks[1]; looks[n + 1] = looks[n];
            fovs[0] = fovs[1]; fovs[n + 1] = fovs[n];
        }

        public void Evaluate(float t, out Vector3 position, out Vector3 look, out float fov)
        {
            int n = times.Length;
            t = Mathf.Clamp(t, times[1], times[n - 2]);
            int i = 1;
            while (i < n - 3 && t > times[i + 1]) i++;
            position = Segment(positions, i, t);
            look = Segment(looks, i, t);
            Vector3 f = Segment(Array.ConvertAll(fovs, v => new Vector3(v, 0, 0)), i, t);
            fov = f.x;
        }

        private Vector3 Segment(Vector3[] p, int i, float t)
        {
            float t0 = times[i - 1], t1 = times[i], t2 = times[i + 1], t3 = times[i + 2];
            Vector3 a1 = Mix(p[i - 1], p[i], t0, t1, t), a2 = Mix(p[i], p[i + 1], t1, t2, t), a3 = Mix(p[i + 1], p[i + 2], t2, t3, t);
            Vector3 b1 = Mix(a1, a2, t0, t2, t), b2 = Mix(a2, a3, t1, t3, t);
            return Mix(b1, b2, t1, t2, t);
        }

        private static Vector3 Mix(Vector3 a, Vector3 b, float ta, float tb, float t) => tb - ta < 1e-5f ? a : Vector3.LerpUnclamped(a, b, (t - ta) / (tb - ta));
    }
}
