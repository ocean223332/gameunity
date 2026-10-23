using System;
using System.Collections.Generic;
using TienTuyen.Combat;
using TienTuyen.Content;
using UnityEngine;
using UnityEngine.Audio;

namespace TienTuyen.Presentation
{
    /// <summary>Event-driven presentation audio layered over CombatGame's public action events.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatGame))]
    public sealed class CombatAudio : MonoBehaviour
    {
        private const string ResourceRoot = "CombatAudio/";
        private static readonly string[] ClipNames =
        {
            "Ambience_Forest__cicadas_loop",
            "Music_Menu_Adagio",
            "Music_Combat_Constance",
            "Rifle_Shot__ak47_01",
            "Rocket_Launch",
            "Rocket_Explosion",
            "Rifle_Shot_Distant__ak47_distant",
            "SMG_Shot__mp5_01",
            "Shotgun_Shot__xm1014-1",
            "AK47_Clipout__ak47_clipout",
            "AK47_Clipin__ak47_clipin",
            "AK47_Boltpull__ak47_boltpull",
            "AK47_Draw__ak47_draw",
            "MP5SD_Slideback__mp5_slideback",
            "MP5SD_Clipout__mp5_clipout",
            "MP5SD_Clipin__mp5_clipin",
            "MP5SD_Slideforward__mp5_slideforward",
            "MP5SD_Draw__mp5_draw",
            "XM1014_Insertshell__xm1014_insertshell",
            "XM1014_Draw__xm1014_draw",
            "XM1014_Reload_Start__cloth2",
            "XM1014_Reload_End__cloth1",
            "Enemy_Knife_Slash1__knife_slash1",
            "Enemy_Knife_Slash2__knife_slash2",
            "Enemy_Knife_Hit1__knife_hit1",
            "Enemy_Knife_Hit2__knife_hit2",
            "Footstep_Dirt__dirt_01",
            "Footstep_Dirt__dirt_02",
            "Footstep_Dirt__dirt_03",
            "Footstep_Dirt__dirt_04",
            "Footstep_Grass__grass_01",
            "Footstep_Grass__grass_02",
            "Footstep_Grass__grass_03",
            "Footstep_Grass__grass_04",
            "Footstep_Mud__mud_01",
            "Footstep_Mud__mud_02",
            "Pickup__handleSmallLeather",
            "Hit_Enemy__impactSoft_medium_000",
            "Impact_Cover__impactGeneric_light_000",
            "Bullet_Whizz__swish-1",
            "Supply_Spawn__dropLeather_question",
            "Supply_Complete__metalLatch_confirmation",
            "Charger_Telegraph__clothBelt_question",
            "Charger_Dash__cloth1_swish5",
            "UI_Click__click_001",
            "UI_Hover__select_001",
            "Shop_Open__open_001",
            "Upgrade_Select__confirmation_001",
            "Buy_Success__confirmation_002",
            "Buy_Fail__error_001",
            "Reroll__scroll_001",
            "Victory__confirmation_004",
            "Defeat__error_004",
            "Supply_Progress__tick_001",
            "Player_Hurt__hit1",
            "Player_Hurt__hit2",
            "Player_Hurt__hit3",
            "Death_Vocal__die1"
        };

        private CombatGame game;
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, ReloadSourceContacts> reloadSources =
            new Dictionary<string, ReloadSourceContacts>(StringComparer.Ordinal);
        private readonly List<AudioSource> worldSources = new List<AudioSource>();
        private AudioSource menuMusic;
        private AudioSource playingMusic;
        private AudioSource ambience;
        private AudioSource sfx;
        private AudioSource ui;
        private CombatState previousState;
        private float previousHealth;
        private float unscaledTime;
        private bool musicStarted;
        private bool introPlaying;
        private float musicBlend;
        private float musicFade = 1f;
        private float musicFadeDuration = 1f;
        private Transform player;
        private readonly Dictionary<Transform, StepState> steps = new Dictionary<Transform, StepState>();
        private ReloadState reload;
        private SkinnedCharacterProfile heroProfile;
        private int worldSourceCursor;

        [Header("Per-group volume")]
        public AudioMixer Mixer;
        [Range(0, 1)] public float MasterVolume = .88f;
        [Range(0, 1)] public float MusicVolume = .38f;
        [Range(0, 1)] public float PlayingMusicVolume = .19f;
        [Range(0, 1)] public float SfxVolume = .75f;
        [Range(0, 1)] public float UiVolume = .72f;
        [Range(0, 1)] public float AmbienceVolume = .32f;
        public bool MuteMusic;
        public bool MuteSfx;
        public bool MuteUi;
        public bool MuteAmbience;

        [Header("Music startup")]
        public bool AutoStartMusic = true;

        private sealed class StepState
        {
            public Vector3 last;
            public float distance;
            public int variant;
        }

        private sealed class Contact
        {
            public float progress;
            public string clip;
        }

        private sealed class ReloadState
        {
            public int slot;
            public string weapon;
            public bool firstPerson;
            public float previousProgress;
            public readonly List<Contact> contacts = new List<Contact>();
        }

        [Serializable]
        private sealed class TakesFile
        {
            public TakesTake[] takes;
        }

        [Serializable]
        private sealed class TakesTake
        {
            public string take;
            public TakesEvent[] events;
            public TakesDone done;
        }

        [Serializable]
        private sealed class TakesEvent
        {
            public float time_s;
            public string options;
        }

        [Serializable]
        private sealed class TakesDone
        {
            public float time_s;
        }

        private sealed class ReloadSourceContacts
        {
            public float done;
            public float insertTime;
            public bool hasInsert;
            public readonly List<SourceContact> events = new List<SourceContact>();
        }

        private sealed class SourceContact
        {
            public float time;
            public string options;
        }

        private void Awake()
        {
            game = GetComponent<CombatGame>();
            player = transform.Find("Player");
            LoadClips();
            heroProfile = Resources.Load<SkinnedCharacterProfile>("Characters/PAVN/PAVN_ValveProfile");
            menuMusic = CreateSource("Music_Menu", true, 0f, false);
            playingMusic = CreateSource("Music_Playing", true, 0f, false);
            menuMusic.clip = clips["Music_Menu_Adagio"];
            playingMusic.clip = clips["Music_Combat_Constance"];
            ambience = CreateSource("Ambience", true, AmbienceVolume, false);
            sfx = CreateSource("SFX", false, SfxVolume, false);
            ui = CreateSource("UI", false, UiVolume, false);
            for (int i = 0; i < 12; i++)
            {
                worldSources.Add(CreateSource("World_" + i, false, SfxVolume, true));
            }
            previousState = game.State;
            previousHealth = game.Health;
        }

        private void Start()
        {
            if (AutoStartMusic && !musicStarted)
            {
                StartMusic();
            }
            if (!introPlaying) StartAmbience();
        }

        /// <summary>Silences game sources while the intro owns the soundtrack, then starts menu ambience.</summary>
        public void SetIntroPlaying(bool playing)
        {
            introPlaying = playing;
            if (playing) ambience.Stop();
            else StartAmbience();
            UpdateVolumes();
        }

        private void StartAmbience()
        {
            ambience.clip = clips["Ambience_Forest__cicadas_loop"];
            if (!ambience.isPlaying) ambience.Play();
        }

        /// <summary>
        /// With AutoStartMusic disabled, an intro can start the menu score at its handoff offset,
        /// optionally at an exact DSP time so it joins the intro's soundtrack without a gap.
        /// </summary>
        public void StartMusic(float menuOffsetSeconds = 0f, float fadeInSeconds = 0f, double startDspTime = 0d)
        {
            menuMusic.Stop();
            playingMusic.Stop();
            menuMusic.time = Mathf.Repeat(menuOffsetSeconds, menuMusic.clip.length);
            musicBlend = game.State == CombatState.Menu ? 0f : 1f;
            musicFadeDuration = Mathf.Max(.01f, fadeInSeconds);
            musicFade = fadeInSeconds > 0f ? 0f : 1f;
            musicStarted = true;
            if (startDspTime > 0d) menuMusic.PlayScheduled(startDspTime);
            TickMusic(0f);
        }

        private void OnEnable()
        {
            if (game == null) game = GetComponent<CombatGame>();
            game.WeaponFired += OnWeaponFired;
            game.LauncherFired += OnLauncherFired;
            game.RocketExploded += OnRocketExploded;
            game.EnemyFired += OnEnemyFired;
            game.EnemyDamaged += OnEnemyDamaged;
            game.PlayerDamaged += OnPlayerDamaged;
            game.EnemyMeleeSwing += OnEnemyMeleeSwing;
            game.EnemyMeleeStruck += OnEnemyMeleeStruck;
            game.KnifeSwung += OnKnifeSwung;
            game.KnifeStruck += OnKnifeStruck;
            game.EnemyChargeStarted += OnEnemyChargeStarted;
            game.SurfaceImpact += OnSurfaceImpact;
            game.BulletNearMiss += OnBulletNearMiss;
            game.PickupCollected += OnPickupCollected;
            game.SupplySpawned += OnSupplySpawned;
            game.SupplyClaimed += OnSupplyClaimed;
            game.SupplyMilestone += OnSupplyMilestone;
            game.ReloadStarted += OnReloadStarted;
            game.EnemyReloadStarted += OnEnemyReloadStarted;
            game.WeaponSwitched += OnWeaponSwitched;
            game.TransientsCleared += ClearTransientAudio;
        }

        private void OnDisable()
        {
            if (game == null) return;
            game.WeaponFired -= OnWeaponFired;
            game.LauncherFired -= OnLauncherFired;
            game.RocketExploded -= OnRocketExploded;
            game.EnemyFired -= OnEnemyFired;
            game.EnemyDamaged -= OnEnemyDamaged;
            game.PlayerDamaged -= OnPlayerDamaged;
            game.EnemyMeleeSwing -= OnEnemyMeleeSwing;
            game.EnemyMeleeStruck -= OnEnemyMeleeStruck;
            game.KnifeSwung -= OnKnifeSwung;
            game.KnifeStruck -= OnKnifeStruck;
            game.EnemyChargeStarted -= OnEnemyChargeStarted;
            game.SurfaceImpact -= OnSurfaceImpact;
            game.BulletNearMiss -= OnBulletNearMiss;
            game.PickupCollected -= OnPickupCollected;
            game.SupplySpawned -= OnSupplySpawned;
            game.SupplyClaimed -= OnSupplyClaimed;
            game.SupplyMilestone -= OnSupplyMilestone;
            game.ReloadStarted -= OnReloadStarted;
            game.EnemyReloadStarted -= OnEnemyReloadStarted;
            game.WeaponSwitched -= OnWeaponSwitched;
            game.TransientsCleared -= ClearTransientAudio;
        }

        private void OnWeaponFired(int slot, Vector3 start, Vector3 end)
        {
            // Launch audio is emitted once through LauncherFired for both owners.
            if (game.WeaponAt(slot)?.Id == ContentIds.Rpg18) return;
            string id = game.WeaponAt(slot)?.Id;
            string clip = id == ContentIds.Smg
                ? "SMG_Shot__mp5_01"
                : id == ContentIds.Shotgun
                    ? "Shotgun_Shot__xm1014-1"
                    : "Rifle_Shot__ak47_01";
            PlaySfx(clip);
        }

        private void OnLauncherFired(bool hostile)
        {
            if (hostile)
            {
                var actor = game.CommanderActor;
                if (actor != null) PlayWorld("Rocket_Launch", actor.position + Vector3.up * .65f, 0f);
            }
            else
            {
                PlaySfx("Rocket_Launch", 0f);
            }
        }

        private void OnRocketExploded(Vector3 point, float radius, bool hostile)
        {
            // Distinct rockets can detonate together; each must retain its own report.
            PlayWorld("Rocket_Explosion", point, 0f);
        }

        private void OnEnemyFired(Transform actor, Vector3 direction)
        {
            if (!game.TryGetEnemyAction(actor, out var action)) return;
            string clip = action.Role == EnemyRole.Shooter
                ? "SMG_Shot__mp5_01"
                : "Rifle_Shot__ak47_01";
            PlayWorld(clip, actor.position, .02f);
        }

        private void OnEnemyDamaged(Transform actor, float damage, bool critical, bool killed)
        {
            PlayWorld("Hit_Enemy__impactSoft_medium_000", actor.position, .02f);
            if (killed) PlayWorld("Death_Vocal__die1", actor.position, .12f);
        }

        private void OnPlayerDamaged(Vector3 source, float damage)
        {
            PlaySfx(game.Health <= 0 ? "Death_Vocal__die1" : HurtVariant());
        }

        private void OnEnemyMeleeSwing(Transform actor)
        {
            PlayWorld(KnifeVariant(true), actor.position, .06f);
        }

        private void OnEnemyMeleeStruck(Transform actor)
        {
            PlayWorld(KnifeVariant(false), actor.position, .02f);
        }

        private void OnKnifeSwung()
        {
            PlaySfx(KnifeVariant(true));
        }

        private void OnKnifeStruck(Transform enemy)
        {
            PlayWorld(KnifeVariant(false), enemy.position, .02f);
        }

        private void OnEnemyChargeStarted(Transform actor, EnemyActionKind kind)
        {
            string clip = kind == EnemyActionKind.ChargeTelegraph
                ? "Charger_Telegraph__clothBelt_question"
                : "Charger_Dash__cloth1_swish5";
            PlayWorld(clip, actor.position, .06f);
        }

        private void OnSurfaceImpact(Vector3 point, bool cover)
        {
            string clip = cover
                ? "Impact_Cover__impactGeneric_light_000"
                : "Hit_Enemy__impactSoft_medium_000";
            PlayWorld(clip, point, .02f);
        }

        private void OnBulletNearMiss(Vector3 point)
        {
            PlayWorld("Bullet_Whizz__swish-1", point, .02f);
        }

        private void OnPickupCollected(Vector3 point)
        {
            PlayWorld("Pickup__handleSmallLeather", point, .02f);
        }

        private void OnSupplySpawned(Vector3 point)
        {
            PlayWorld("Supply_Spawn__dropLeather_question", point, .08f);
        }

        private void OnSupplyClaimed(Vector3 point)
        {
            PlayWorld("Supply_Complete__metalLatch_confirmation", point, .08f);
        }

        private void OnSupplyMilestone(int milestone)
        {
            PlayUi("Supply_Progress__tick_001");
        }

        private void OnWeaponSwitched(int from, int to)
        {
            reload = null;
            // The knife comes out of its sheath without a draw sound.
            if (to == game.WeaponCount) return;
            string id = game.WeaponAt(to)?.Id;
            string clip = id == ContentIds.Smg
                ? "MP5SD_Draw__mp5_draw"
                : id == ContentIds.Shotgun
                    ? "XM1014_Draw__xm1014_draw"
                    : "AK47_Draw__ak47_draw";
            PlaySfx(clip);
        }

        private void OnReloadStarted(int slot, float seconds, int missing)
        {
            reload = new ReloadState
            {
                slot = slot,
                weapon = game.WeaponAt(slot)?.Id,
                firstPerson = IsFirstPerson()
            };
            BuildReloadContacts(reload, missing);
            if (reload.contacts.Count == 0)
            {
                PlaySfx("XM1014_Reload_Start__cloth2", .06f);
            }
        }

        private void OnEnemyReloadStarted(Transform actor, float seconds)
        {
            bool smg = game.TryGetEnemyAction(actor, out var action) && action.Role == EnemyRole.Shooter;
            string clip = smg
                ? "MP5SD_Clipout__mp5_clipout"
                : "AK47_Clipout__ak47_clipout";
            PlayWorld(clip, actor.position, .08f);
        }

        private bool IsFirstPerson()
        {
            var camera = FindFirstObjectByType<CombatThirdPersonCamera>();
            return camera != null && camera.FirstPersonWeight > .5f;
        }

        private void BuildReloadContacts(ReloadState state, int missing)
        {
            if (state.weapon == ContentIds.Shotgun)
            {
                BuildShellReloadContacts(state, missing);
                return;
            }

            string prefix = state.weapon == ContentIds.Smg ? "MP5SD_" : "AK47_";
            if (state.firstPerson)
            {
                if (!reloadSources.TryGetValue(state.weapon, out var source)) return;
                foreach (var sourceContact in source.events)
                {
                    string name = sourceContact.options.ToLowerInvariant();
                    string clip = ResolveReloadClip(state.weapon, prefix, name);
                    if (clip == null) continue;
                    float progress = source.done > 0
                        ? sourceContact.time / source.done
                        : .5f;
                    state.contacts.Add(new Contact { progress = progress, clip = clip });
                }
                return;
            }

            var set = FindWeaponSet(heroProfile, state.weapon);
            var clipAsset = ReloadClip(set, game.Crouch > .5f);
            var track = set != null && set.grip != null && clipAsset != null
                ? set.grip.Track(clipAsset)
                : null;
            if (track == null) return;

            float frames = Mathf.Max(1f, clipAsset.length * HandGripData.TrackRate);
            if (track.magazineRelease >= 0)
            {
                state.contacts.Add(new Contact
                {
                    progress = Mathf.Clamp01(track.magazineRelease / frames),
                    clip = prefix + "Clipout__" + (state.weapon == ContentIds.Smg ? "mp5_clipout" : "ak47_clipout")
                });
            }
            if (track.magazineInsert >= 0)
            {
                state.contacts.Add(new Contact
                {
                    progress = Mathf.Clamp01(track.magazineInsert / frames),
                    clip = prefix + "Clipin__" + (state.weapon == ContentIds.Smg ? "mp5_clipin" : "ak47_clipin")
                });
            }
        }

        private void BuildShellReloadContacts(ReloadState state, int missing)
        {
            var rounds = game.WeaponAt(state.slot)?.RoundReload;
            if (rounds == null) return;

            state.contacts.Add(new Contact
            {
                progress = 0f,
                clip = "XM1014_Reload_Start__cloth2"
            });
            float start = rounds.Value.StartSeconds / Mathf.Max(.01f, game.ReloadDuration);
            float end = 1f - rounds.Value.EndSeconds / Mathf.Max(.01f, game.ReloadDuration);
            if (state.firstPerson && reloadSources.TryGetValue(state.weapon, out var source) && source.hasInsert)
            {
                float local = source.done > 0
                    ? Mathf.Clamp01(source.insertTime / source.done)
                    : 1f;
                for (int i = 0; i < missing; i++)
                {
                    state.contacts.Add(new Contact
                    {
                        progress = Mathf.Lerp(start, end, (i + local) / Mathf.Max(1, missing)),
                        clip = "XM1014_Insertshell__xm1014_insertshell"
                    });
                }
                state.contacts.Add(new Contact
                {
                    progress = Mathf.Clamp01(end),
                    clip = "XM1014_Reload_End__cloth1"
                });
                return;
            }

            var set = FindWeaponSet(heroProfile, state.weapon);
            if (set == null || set.grip == null) return;
            var insertClip = set.crouchReloadInsert != null && game.Crouch > .5f
                ? set.crouchReloadInsert
                : set.reloadInsert;
            var track = set.grip.Track(insertClip);
            if (track == null || track.shellTo < 0 || insertClip == null) return;

            float frames = Mathf.Max(1f, insertClip.length * HandGripData.TrackRate);
            float trackLocal = Mathf.Clamp01((track.shellTo + 1f) / frames);
            for (int i = 0; i < missing; i++)
            {
                state.contacts.Add(new Contact
                {
                    progress = Mathf.Lerp(start, end, (i + trackLocal) / Mathf.Max(1, missing)),
                    clip = "XM1014_Insertshell__xm1014_insertshell"
                });
            }
            state.contacts.Add(new Contact
            {
                progress = Mathf.Clamp01(end),
                clip = "XM1014_Reload_End__cloth1"
            });
        }

        private static string ResolveReloadClip(string weapon, string prefix, string name)
        {
            if (name.Contains("clipout"))
            {
                return prefix + "Clipout__" + (weapon == ContentIds.Smg ? "mp5_clipout" : "ak47_clipout");
            }
            if (name.Contains("clipin"))
            {
                return prefix + "Clipin__" + (weapon == ContentIds.Smg ? "mp5_clipin" : "ak47_clipin");
            }
            if (name.Contains("slideback")) return "MP5SD_Slideback__mp5_slideback";
            if (name.Contains("slideforward") || name.Contains("boltpull"))
            {
                return weapon == ContentIds.Smg
                    ? "MP5SD_Slideforward__mp5_slideforward"
                    : "AK47_Boltpull__ak47_boltpull";
            }
            return null;
        }

        private void LoadReloadContacts()
        {
            LoadReloadContacts(ContentIds.Smg, "Viewmodels/weapon.smg/v_mp5sd.takes", "reload");
            LoadReloadContacts(ContentIds.Rifle, "Viewmodels/weapon.rifle/v_ak47.takes", "reload");
            LoadReloadContacts(ContentIds.Shotgun, "Viewmodels/weapon.shotgun/v_xm1014.takes", "insert");
        }

        private void LoadReloadContacts(string weapon, string resourcePath, string takeName)
        {
            var text = Resources.Load<TextAsset>(resourcePath);
            if (text == null) return;
            TakesFile file = ParseTakes(text.text);
            if (file == null || file.takes == null) return;

            foreach (var take in file.takes)
            {
                if (take == null || !string.Equals(take.take, takeName, StringComparison.OrdinalIgnoreCase)) continue;
                var contacts = new ReloadSourceContacts
                {
                    done = take.done != null ? take.done.time_s : 0f
                };
                if (take.events != null)
                {
                    foreach (var contact in take.events)
                    {
                        if (contact == null) continue;
                        contacts.events.Add(new SourceContact
                        {
                            time = contact.time_s,
                            options = contact.options ?? string.Empty
                        });
                        if (weapon == ContentIds.Shotgun && !contacts.hasInsert)
                        {
                            contacts.insertTime = contact.time_s;
                            contacts.hasInsert = true;
                        }
                    }
                }
                reloadSources[weapon] = contacts;
                return;
            }
        }

        private static TakesFile ParseTakes(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            string wrapped = json.Trim();
            if (wrapped.StartsWith("[", StringComparison.Ordinal))
            {
                wrapped = "{\"takes\":" + wrapped + "}";
            }
            wrapped = wrapped.Replace("\"events\": \"none\"", "\"events\": []");
            wrapped = wrapped.Replace("\"events\":\"none\"", "\"events\":[]");
            try
            {
                return JsonUtility.FromJson<TakesFile>(wrapped);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static WeaponClipSet FindWeaponSet(SkinnedCharacterProfile profile, string weapon)
        {
            if (profile == null || profile.weaponSets == null) return null;
            foreach (var set in profile.weaponSets)
            {
                if (set != null && string.Equals(set.weapon, weapon, StringComparison.OrdinalIgnoreCase))
                    return set;
            }
            return null;
        }

        private static AnimationClip ReloadClip(WeaponClipSet set, bool crouched)
        {
            if (set == null) return null;
            if (crouched && set.crouchReload != null) return set.crouchReload;
            return set.reload;
        }

        private void Update()
        {
            if (game == null) return;
            unscaledTime += Time.unscaledDeltaTime;
            UpdateVolumes();
            DetectStateEvents();
            TickReload();
            TickFootsteps();
        }

        private void TickReload()
        {
            if (reload == null || game.State != CombatState.Playing || game.ReloadRemaining <= 0)
            {
                if (game.ReloadRemaining <= 0) reload = null;
                return;
            }

            bool first = IsFirstPerson();
            if (first != reload.firstPerson)
            {
                // A view switch rebuilds contacts, then consumes every contact already behind the current progress.
                reload = new ReloadState
                {
                    slot = game.ActiveWeaponSlot,
                    weapon = game.ActiveWeapon?.Id,
                    firstPerson = first
                };
                BuildReloadContacts(reload, Mathf.Max(1, game.MagazineSize - game.Ammo));
                reload.previousProgress = 1f - game.ReloadRemaining / Mathf.Max(.01f, game.ReloadDuration);
            }

            float progress = 1f - game.ReloadRemaining / Mathf.Max(.01f, game.ReloadDuration);
            foreach (var contact in reload.contacts)
            {
                if (contact.progress > reload.previousProgress && contact.progress <= progress)
                    PlaySfx(contact.clip, .01f);
            }
            reload.previousProgress = progress;
        }

        private void TickFootsteps()
        {
            if (game.State != CombatState.Playing) return;
            if (player == null) player = transform.Find("Player");
            TickStep(player, false);
            for (int i = 0; i < 48; i++)
            {
                TickStep(transform.Find("Enemy_" + i), true);
            }
        }

        private void TickStep(Transform actor, bool enemy)
        {
            if (actor == null || !actor.gameObject.activeSelf) return;
            if (!steps.TryGetValue(actor, out var state))
            {
                state = new StepState { last = actor.position };
                steps[actor] = state;
            }

            Vector3 delta = actor.position - state.last;
            state.last = actor.position;
            delta.y = 0;
            float distance = delta.magnitude;
            if (distance > 1.8f)
            {
                state.distance = 0;
                return;
            }
            state.distance += distance;
            if (state.distance < (enemy ? .95f : .85f)) return;
            state.distance = 0;

            string surface = EnvironmentGround.WetPatch(actor.position.x, actor.position.z)
                ? "Mud"
                : EnvironmentGround.GrassCover(actor.position.x, actor.position.z) > .45f
                    ? "Grass"
                    : "Dirt";
            int variants = surface == "Mud" ? 2 : 4;
            state.variant = (state.variant + 1) % variants;
            string clip = "Footstep_" + surface + "__" + surface.ToLowerInvariant() + "_0" + (state.variant + 1);
            PlayWorld(clip, actor.position, enemy ? .04f : .02f);
        }

        private void DetectStateEvents()
        {
            CombatState state = game.State;
            if (state != previousState)
            {
                if (state == CombatState.Shop)
                    PlayUi("Shop_Open__open_001");
                else if (state == CombatState.Victory)
                    PlayUi("Victory__confirmation_004");
                else if (state == CombatState.Defeat)
                {
                    PlayUi("Defeat__error_004");
                    ClearTransientAudio();
                }
                else if (state == CombatState.Paused)
                {
                    ClearTransientAudio();
                }
                previousState = state;
            }

            if (game.Health < previousHealth - .01f && game.Health <= 0)
                ClearTransientAudio();
            previousHealth = game.Health;
        }

        private string ResolveClip(string clipName)
        {
            switch (clipName)
            {
                case "UI_Click": return "UI_Click__click_001";
                case "UI_Hover": return "UI_Hover__select_001";
                case "Shop_Open": return "Shop_Open__open_001";
                case "Upgrade_Select": return "Upgrade_Select__confirmation_001";
                case "Buy_Success": return "Buy_Success__confirmation_002";
                case "Buy_Fail": return "Buy_Fail__error_001";
                case "Reroll": return "Reroll__scroll_001";
                case "Victory": return "Victory__confirmation_004";
                case "Defeat": return "Defeat__error_004";
                default: return clipName;
            }
        }

        public void PlaySfx(string clipName, float cooldown = .025f)
        {
            clipName = ResolveClip(clipName);
            if (sfx == null || !clips.TryGetValue(clipName, out var clip)) return;
            if (lastPlayed.TryGetValue(clipName, out float last) && unscaledTime - last < cooldown) return;
            lastPlayed[clipName] = unscaledTime;
            sfx.PlayOneShot(clip, 1f);
        }

        public void PlayUi(string clipName)
        {
            clipName = ResolveClip(clipName);
            if (ui == null || !clips.TryGetValue(clipName, out var clip)) return;
            ui.PlayOneShot(clip, 1f);
        }

        private void PlayWorld(string clipName, Vector3 position, float cooldown)
        {
            if (!clips.TryGetValue(clipName, out var clip) || worldSources.Count == 0) return;
            string key = clipName + "@world";
            if (lastPlayed.TryGetValue(key, out float last) && unscaledTime - last < cooldown) return;
            lastPlayed[key] = unscaledTime;
            var source = worldSources[worldSourceCursor++ % worldSources.Count];
            source.transform.position = position;
            source.PlayOneShot(clip, 1f);
        }

        private void ClearTransientAudio()
        {
            foreach (var source in worldSources) source.Stop();
            if (sfx != null) sfx.Stop();
            reload = null;
            steps.Clear();
        }

        private string HurtVariant()
        {
            return "Player_Hurt__hit" + ((int)(unscaledTime * 10f) % 3 + 1);
        }

        private string KnifeVariant(bool slash)
        {
            int n = ((int)(unscaledTime * 10f) & 1) + 1;
            return slash
                ? "Enemy_Knife_Slash" + n + "__knife_slash" + n
                : "Enemy_Knife_Hit" + n + "__knife_hit" + n;
        }

        private void LoadClips()
        {
            foreach (string name in ClipNames)
            {
                var clip = Resources.Load<AudioClip>(ResourceRoot + name);
                if (clip == null) clip = CreateFallbackClip(name);
                clips[name] = clip;
            }
            LoadReloadContacts();
        }

        private AudioSource CreateSource(string name, bool loop, float volume, bool positional)
        {
            var go = new GameObject("CombatAudio_" + name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = positional ? 1f : 0f;
            source.minDistance = 3f;
            source.maxDistance = 24f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.volume = volume;
            source.ignoreListenerPause = name == "UI";
            if (Mixer != null)
            {
                string group = name.StartsWith("Music", StringComparison.Ordinal) ? "Music" : name;
                var groups = Mixer.FindMatchingGroups(positional ? "SFX" : group);
                if (groups != null && groups.Length > 0)
                    source.outputAudioMixerGroup = groups[0];
            }
            return source;
        }

        private void UpdateVolumes()
        {
            bool paused = game.State == CombatState.Paused;
            float duck = paused ? .06f : 1f;
            float master = introPlaying ? 0f : MasterVolume;
            TickMusic(Time.unscaledDeltaTime);
            ambience.volume = master * AmbienceVolume * duck * (MuteAmbience ? 0 : 1);
            sfx.volume = master * SfxVolume * duck * (MuteSfx ? 0 : 1);
            foreach (var source in worldSources)
                source.volume = master * SfxVolume * duck * (MuteSfx ? 0 : 1);
            ui.volume = master * UiVolume * (MuteUi ? 0 : 1);
        }

        private void TickMusic(float deltaTime)
        {
            if (!musicStarted) return;
            if (musicFade < 1f)
            {
                musicFade = Mathf.MoveTowards(musicFade, 1f, deltaTime / musicFadeDuration);
            }
            float target = game.State == CombatState.Menu ? 0f : 1f;
            // Keep the blend independent of ducking/mute so either changes both tracks immediately.
            musicBlend = Mathf.MoveTowards(musicBlend, target, deltaTime);
            float volume = (introPlaying ? 0f : MasterVolume) * musicFade * (MuteMusic ? 0f : 1f);
            if (game.State == CombatState.Paused) volume *= .06f;
            SetMusicWeight(menuMusic, 1f - musicBlend, MusicVolume * volume);
            SetMusicWeight(playingMusic, musicBlend, PlayingMusicVolume * volume);
        }

        private static void SetMusicWeight(AudioSource source, float weight, float volume)
        {
            source.volume = weight * volume;
            if (weight <= 0f)
            {
                source.Pause();
                return;
            }
            if (source.isPlaying)
            {
                return;
            }
            source.UnPause();
            if (!source.isPlaying)
            {
                source.Play();
            }
        }

        private static AudioClip CreateFallbackClip(string name)
        {
            const int rate = 22050;
            float length = name.StartsWith("Ambience", StringComparison.Ordinal) ? 3f : .18f;
            int samples = Mathf.Max(1, Mathf.RoundToInt(rate * length));
            var clip = AudioClip.Create("Fallback_" + name, samples, 1, rate, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
                data[i] = Mathf.Sin(i * 1731f / rate) * .04f * (1f - i / (float)samples);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
