using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using TienTuyen.Combat;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Temporary trailer tool: only active when the player is launched with -trailer <outDir>.
public sealed class TrailerCapture : MonoBehaviour
{
    private const int Fps = 60;
    private CombatGame game;
    private string outDir, weaponArg = "3";
    private Process ffmpeg;
    private Stream videoPipe;
    private FileStream audioFile;
    private StreamWriter log;
    private byte[] frameBuffer;
    private int frame;
    private float stateTime;
    private CombatState lastState = (CombatState)(-1);
    private int lastWave;
    private Vector2 wander = Vector2.right;
    private PropertyInfo healthProperty;
    private Transform playerT;
    private readonly Transform[] enemies = new Transform[64];
    private readonly Transform[] pickups = new Transform[64];
    private int enemyCount, pickupCount;
    private bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-trailer");
        if (i < 0 || i + 1 >= args.Length) return;
        var capture = new GameObject("TrailerCapture").AddComponent<TrailerCapture>();
        capture.outDir = args[i + 1];
        int w = Array.IndexOf(args, "-trailerWeapon");
        if (w >= 0 && w + 1 < args.Length) capture.weaponArg = args[w + 1];
    }

    private void Start()
    {
        game = FindAnyObjectByType<CombatGame>();
        game.enabled = false; // we drive StepSimulation with synthetic movement
        healthProperty = typeof(CombatGame).GetProperty("Health");
        Cursor.visible = false;
        Time.captureFramerate = Fps;
        Directory.CreateDirectory(outDir);
        log = new StreamWriter(Path.Combine(outDir, "timeline.txt")) { AutoFlush = true };
        int width = Screen.width, height = Screen.height;
        log.WriteLine($"size {width}x{height} fps {Fps} audio {AudioSettings.outputSampleRate} {AudioSettings.speakerMode}");
        ffmpeg = Process.Start(new ProcessStartInfo
        {
            FileName = @"C:\Users\Admin\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe",
            Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {Fps} -i - -vf vflip " +
                        $"-c:v libx264 -preset veryfast -crf 14 -pix_fmt yuv420p \"{Path.Combine(outDir, "video.mp4")}\"",
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
        });
        videoPipe = ffmpeg.StandardInput.BaseStream;
        audioFile = new FileStream(Path.Combine(outDir, "audio.f32"), FileMode.Create);
        AudioRenderer.Start();
        StartCoroutine(CaptureLoop());
    }

    private void Update()
    {
        if (finished) return;
        stateTime += Time.deltaTime;
        if (game.State != lastState || game.Wave != lastWave)
        {
            log.WriteLine($"frame {frame} t {frame / (float)Fps:F2} state {game.State} wave {game.Wave}");
            lastState = game.State; lastWave = game.Wave; stateTime = 0;
        }
        switch (game.State)
        {
            case CombatState.Menu:
                if (stateTime > 1.0f && stateTime - Time.deltaTime <= 1.0f)
                    game.SelectWeapon(weaponArg == "1" ? WeaponKind.Rifle : weaponArg == "2" ? WeaponKind.Smg : WeaponKind.Shotgun);
                if (stateTime > 3.0f) game.BeginRun();
                break;
            case CombatState.Playing:
                if (game.MaxHealth > 0 && game.Health < game.MaxHealth * 0.4f)
                    healthProperty.SetValue(game, game.MaxHealth * 0.4f);
                game.StepSimulation(Time.deltaTime, Steer());
                break;
            case CombatState.Upgrade:
                if (stateTime > 1.6f) { game.ChooseUpgrade(UnityEngine.Random.Range(0, game.UpgradeChoiceCount)); stateTime = 0; }
                break;
            case CombatState.Shop:
                if (stateTime > 1.2f && stateTime - Time.deltaTime <= 1.2f)
                    for (int i = 0; i < 4; i++) game.PurchaseOffer(i);
                if (stateTime > 1.8f && stateTime - Time.deltaTime <= 1.8f) game.CombineFirstMatchingWeapons();
                if (stateTime > 2.8f) game.ContinueWave();
                break;
            case CombatState.Victory:
            case CombatState.Defeat:
                if (stateTime > 4f) StartCoroutine(Finish());
                break;
        }
    }

    private Vector2 Steer()
    {
        if (playerT == null) playerT = GameObject.Find("Player").transform;
        if (enemyCount == 0) Collect();
        Vector3 p = playerT.position;
        Vector2 flee = Vector2.zero, seek = Vector2.zero;
        float nearestPickup = float.MaxValue;
        for (int i = 0; i < enemyCount; i++)
        {
            if (!enemies[i].gameObject.activeInHierarchy) continue;
            Vector2 d = new Vector2(p.x - enemies[i].position.x, p.z - enemies[i].position.z);
            float m = d.magnitude;
            if (m < 6f) flee += d.normalized * (6f - m);
        }
        for (int i = 0; i < pickupCount; i++)
        {
            if (!pickups[i].gameObject.activeInHierarchy) continue;
            Vector2 d = new Vector2(pickups[i].position.x - p.x, pickups[i].position.z - p.z);
            if (d.magnitude < nearestPickup) { nearestPickup = d.magnitude; seek = d.normalized; }
        }
        Vector2 home = new Vector2(-p.x / 12f, -p.z / 7f);
        wander = Quaternion.Euler(0, 0, 40f * Time.deltaTime) * wander;
        Vector2 move = flee * 0.6f + seek * 0.8f + home * 1.2f + wander * 0.35f;
        return move.sqrMagnitude < 0.04f ? Vector2.zero : move.normalized;
    }

    private void Collect()
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name.StartsWith("Enemy_") && enemyCount < enemies.Length) enemies[enemyCount++] = t;
            else if (t.name.StartsWith("Pickup_") && pickupCount < pickups.Length) pickups[pickupCount++] = t;
        }
    }

    private IEnumerator CaptureLoop()
    {
        var wait = new WaitForEndOfFrame();
        while (!finished)
        {
            yield return wait;
            if (finished) break;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            NativeArray<byte> raw = tex.GetRawTextureData<byte>();
            if (frameBuffer == null || frameBuffer.Length != raw.Length) frameBuffer = new byte[raw.Length];
            raw.CopyTo(frameBuffer);
            Destroy(tex);
            videoPipe.Write(frameBuffer, 0, frameBuffer.Length);
            int samples = AudioRenderer.GetSampleCountForCaptureFrame();
            int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            using (var audio = new NativeArray<float>(samples * channels, Allocator.Temp))
            {
                AudioRenderer.Render(audio);
                var bytes = new byte[audio.Length * 4];
                Buffer.BlockCopy(audio.ToArray(), 0, bytes, 0, bytes.Length);
                audioFile.Write(bytes, 0, bytes.Length);
            }
            frame++;
        }
    }

    private IEnumerator Finish()
    {
        finished = true;
        yield return null;
        AudioRenderer.Stop();
        audioFile.Dispose();
        videoPipe.Close();
        ffmpeg.WaitForExit();
        log.WriteLine($"done frames {frame}");
        log.Dispose();
        Application.Quit();
    }
}
