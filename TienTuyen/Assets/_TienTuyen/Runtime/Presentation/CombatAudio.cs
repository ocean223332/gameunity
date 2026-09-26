using System;
using System.Collections.Generic;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.Audio;

namespace TienTuyen.Presentation
{
    /// <summary>Event-driven presentation audio layered over CombatGame's public state.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatGame))]
    public sealed class CombatAudio : MonoBehaviour
    {
        private const string ResourceRoot = "CombatAudio/";
        private static readonly string[] ClipNames =
        {
            "Ambience_Forest", "Music_Combat", "Rifle_Shot", "SMG_Shot", "Shotgun_Shot", "Enemy_Shot",
            "Hit_Enemy", "Player_Hurt", "Player_Death", "Reload", "Pickup", "Supply_Spawn", "Supply_Progress",
            "Supply_Complete", "Charger_Telegraph", "Charger_Dash", "Shop_Open", "Upgrade_Select", "Buy_Success",
            "Buy_Fail", "Reroll", "Victory", "Defeat", "UI_Click", "UI_Hover"
        };

        private CombatGame game;
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>(StringComparer.Ordinal);
        private AudioSource music, ambience, sfx, ui;
        private CombatState previousState;
        private float previousHealth, previousSupplyProgress;
        private bool previousSupplyVisible;
        private int previousActiveEnemies;
        private readonly bool[] previousWarnings = new bool[25];
        private int previousActivePickups;
        private float previousReload;
        private bool previousChargerDash;
        private float unscaledTime;

        [Header("Per-group volume")]
        public AudioMixer Mixer;
        [Range(0, 1)] public float MasterVolume = .88f;
        [Range(0, 1)] public float MusicVolume = .38f;
        [Range(0, 1)] public float SfxVolume = .75f;
        [Range(0, 1)] public float UiVolume = .72f;
        [Range(0, 1)] public float AmbienceVolume = .32f;
        public bool MuteMusic;
        public bool MuteSfx;
        public bool MuteUi;
        public bool MuteAmbience;

        private void Awake()
        {
            game = GetComponent<CombatGame>();
            LoadClips();
            music = CreateSource("Music", true, MusicVolume);
            ambience = CreateSource("Ambience", true, AmbienceVolume);
            sfx = CreateSource("SFX", false, SfxVolume);
            ui = CreateSource("UI", false, UiVolume);
            previousState = game.State;
            previousHealth = game.Health;
        }

        private void Start()
        {
            PlayLoop(music, "Music_Combat");
            PlayLoop(ambience, "Ambience_Forest");
        }

        private void OnEnable()
        {
            if (game == null) game = GetComponent<CombatGame>();
            game.WeaponFired += OnWeaponFired;
            game.EnemyFired += OnEnemyFired;
        }

        private void OnDisable()
        {
            if (game == null) return;
            game.WeaponFired -= OnWeaponFired;
            game.EnemyFired -= OnEnemyFired;
        }

        private void OnWeaponFired(int slot, Vector3 start, Vector3 end)
        {
            var weapon = game.WeaponAt(slot);
            string id = weapon != null ? weapon.Id : "";
            string clip = id.IndexOf("smg", StringComparison.OrdinalIgnoreCase) >= 0 ? "SMG_Shot" :
                id.IndexOf("shotgun", StringComparison.OrdinalIgnoreCase) >= 0 ? "Shotgun_Shot" : "Rifle_Shot";
            // Every accepted shot has an audible attack, even while a previous
            // tracer is visible or several weapon slots fire in the same frame.
            PlaySfx(clip, 0f);
        }

        private void OnEnemyFired(Transform actor, Vector3 direction) => PlaySfx("Enemy_Shot", .045f);

        private void Update()
        {
            if (game == null) return;
            unscaledTime += Time.unscaledDeltaTime;
            UpdateVolumes();
            DetectStateEvents();
            DetectCombatEvents();
        }

        private void LoadClips()
        {
            foreach (string clipName in ClipNames)
            {
                var clip = Resources.Load<AudioClip>(ResourceRoot + clipName);
                if (clip == null) clip = CreateFallbackClip(clipName);
                clips[clipName] = clip;
            }
        }

        private AudioSource CreateSource(string name, bool loop, float volume)
        {
            var go = new GameObject("CombatAudio_" + name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0;
            source.volume = volume;
            source.ignoreListenerPause = name == "UI";
            if (Mixer != null)
            {
                var groups = Mixer.FindMatchingGroups(name);
                if (groups != null && groups.Length > 0) source.outputAudioMixerGroup = groups[0];
            }
            return source;
        }

        private void UpdateVolumes()
        {
            // Combat audio respects pause by ducking simulation ambience/music while UI
            // feedback continues with unscaled time and ignoreListenerPause.
            float paused = game.State == CombatState.Paused ? .06f : 1f;
            music.volume = MasterVolume * MusicVolume * paused * (MuteMusic ? 0 : 1);
            ambience.volume = MasterVolume * AmbienceVolume * paused * (MuteAmbience ? 0 : 1);
            sfx.volume = MasterVolume * SfxVolume * paused * (MuteSfx ? 0 : 1);
            ui.volume = MasterVolume * UiVolume * (MuteUi ? 0 : 1);
        }

        private void DetectStateEvents()
        {
            CombatState state = game.State;
            if (state != previousState)
            {
                switch (state)
                {
                    case CombatState.Shop: PlayUi("Shop_Open"); break;
                    case CombatState.Upgrade: PlayUi("Upgrade_Select"); break;
                    case CombatState.Victory: PlaySfx("Victory", .1f); break;
                    case CombatState.Defeat: PlaySfx("Defeat", .1f); break;
                }
                previousState = state;
            }
        }

        private void DetectCombatEvents()
        {
            int activeEnemies = 0;
            for (int i = 0; i < 48; i++)
            {
                Transform enemy = transform.Find("Enemy_" + i);
                if (enemy != null && enemy.gameObject.activeSelf) activeEnemies++;
            }
            if (activeEnemies < previousActiveEnemies) PlaySfx("Hit_Enemy", .06f);
            int activePickups = 0;
            for (int i = 0; i < 48; i++)
            {
                Transform pickup = transform.Find("Pickup_" + i);
                if (pickup != null && pickup.gameObject.activeSelf) activePickups++;
            }
            if (activePickups < previousActivePickups) PlaySfx("Pickup", .06f);
            if (game.ReloadRemaining > .01f && previousReload <= .01f) PlaySfx("Reload", .08f);
            bool chargerDash = game.ChargerDashActive;
            if (chargerDash && !previousChargerDash) PlaySfx("Charger_Dash", .12f);
            if (game.Health < previousHealth - .01f) PlaySfx(game.Health <= 0 ? "Player_Death" : "Player_Hurt", .08f);
            previousActiveEnemies = activeEnemies;
            previousActivePickups = activePickups;
            previousReload = game.ReloadRemaining;
            previousChargerDash = chargerDash;
            previousHealth = game.Health;

            bool supplyVisible = game.SupplyVisible;
            if (supplyVisible && !previousSupplyVisible) PlaySfx("Supply_Spawn", .1f);
            if (supplyVisible && game.SupplyProgress > previousSupplyProgress + .05f)
                PlaySfx(game.SupplyProgress >= .999f ? "Supply_Complete" : "Supply_Progress", .18f);
            previousSupplyVisible = supplyVisible;
            previousSupplyProgress = game.SupplyProgress;

            for (int i = 0; i < previousWarnings.Length; i++)
            {
                Transform warning = transform.Find("EnemyWarning_" + i);
                bool active = warning != null && warning.gameObject.activeSelf;
                if (active && !previousWarnings[i])
                {
                    Transform marker = transform.Find("Enemy_" + i + "/ChargerMarker_" + i);
                    PlaySfx(marker != null && marker.gameObject.activeSelf ? "Charger_Telegraph" : "Enemy_Shot", .12f);
                }
                previousWarnings[i] = active;
            }
        }

        public void PlaySfx(string clipName, float cooldown = .025f)
        {
            if (sfx == null || !clips.TryGetValue(clipName, out var clip)) return;
            if (lastPlayed.TryGetValue(clipName, out float last) && unscaledTime - last < cooldown) return;
            lastPlayed[clipName] = unscaledTime;
            sfx.PlayOneShot(clip, 1f);
        }

        public void PlayUi(string clipName)
        {
            if (ui == null || !clips.TryGetValue(clipName, out var clip)) return;
            ui.PlayOneShot(clip, 1f);
        }

        private static void PlayLoop(AudioSource source, string clipName)
        {
            if (source == null || source.clip != null) return;
            var clipsRoot = source.transform.parent.GetComponent<CombatAudio>();
            if (clipsRoot != null && clipsRoot.clips.TryGetValue(clipName, out var clip)) { source.clip = clip; source.Play(); }
        }

        private static AudioClip CreateFallbackClip(string name)
        {
            const int sampleRate = 22050;
            float length = name.StartsWith("Music", StringComparison.Ordinal) ? 4f : name.StartsWith("Ambience", StringComparison.Ordinal) ? 3f : .18f;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * length));
            var clip = AudioClip.Create("Fallback_" + name, samples, 1, sampleRate, false);
            var data = new float[samples];
            float baseHz = name.IndexOf("Shot", StringComparison.Ordinal) >= 0 ? 95f : name.IndexOf("Hurt", StringComparison.Ordinal) >= 0 ? 180f : name.IndexOf("Victory", StringComparison.Ordinal) >= 0 ? 440f : 220f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Clamp01(1f - t / length);
                float tone = Mathf.Sin(t * baseHz * Mathf.PI * 2f) * .22f;
                float noise = Mathf.Sin(t * 1731f) * .04f;
                data[i] = (tone + noise) * envelope;
            }
            clip.SetData(data, 0);
            clip.name = "Fallback_" + name;
            return clip;
        }
    }
}
