using System;
using System.Collections.Generic;
using TienTuyen.Combat;
using TienTuyen.Content;
using TienTuyen.Duel.Net;
using UnityEngine;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The duel's sound, over the campaign's clip bank (Resources/CombatAudio, the names CombatAudio plays), derived
    /// from what the play view already receives: the local player's predicted state, the opponent's snapshots and the
    /// match phase. No sound needs a network message.
    /// Combat layer: shots, draws, reloads (contacts timed by the viewmodel's .takes files, so they land on its frames),
    /// knife slashes and hits, hurt, deaths, the opponent's bullets passing close, and the round stings.
    /// Second layer: the forest ambience and both players' footsteps.
    /// Your own sounds are flat; the opponent's sit at its body in 3D. Run with -duelAudioLog to log every sound.
    /// </summary>
    internal sealed class DuelAudio
    {
        private const string ResourceRoot = "CombatAudio/";
        private const float Master = .88f, Sfx = .75f, AmbienceVolume = .32f;
        private const float WhizzDistance = 1.5f;
        private const string HeadPing = "Impact_Cover__impactGeneric_light_000";
        private const float HeadPingPitch = 1.6f;
        private const string ImpactClip = "Impact_Cover__impactGeneric_light_000";
        private static readonly bool LogEvents = Array.IndexOf(Environment.GetCommandLineArgs(), "-duelAudioLog") >= 0;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private static Dictionary<string, List<(float time, string clip)>> takes;

        private readonly Transform root;
        private readonly int localSlot;
        private readonly AudioSource flat, ping;
        private readonly AudioSource[] world = new AudioSource[12];
        private int worldCursor;

        private readonly Player own = new Player(), them = new Player();
        private bool started;
        private DuelPhase phase;
        private int generation;

        /// <summary>What one player last did, as far as the sounds need it.</summary>
        private sealed class Player
        {
            public float health;
            public int weapon;
            public bool reloading;
            public float knife = -1f;                       // seconds since the slash began; -1 when none
            public Reload reload;
            public Vector3 lastFoot;
            public float stride;
            public int step;
        }

        private sealed class Reload
        {
            public int slot;
            public float elapsed, total;
            public readonly List<(float time, string clip)> contacts = new List<(float time, string clip)>();
            public int next;
            public bool shells;
        }

        /// <summary>A player lost health: (the local player, damage, killed, by a knife). The play view's blood and hit feedback.</summary>
        public event Action<bool, float, bool, bool> Hurt;

        public DuelAudio(Transform parent, int localSlot)
        {
            this.localSlot = localSlot;
            root = new GameObject("DuelAudio").transform;
            root.SetParent(parent, false);
            flat = Source("Own", false);
            // The bank has no head-hit clip: a light impact pitched up is the stand-in "dink".
            ping = Source("HeadPing", false);
            ping.pitch = HeadPingPitch;
            for (int i = 0; i < world.Length; i++) world[i] = Source("World" + i, true);
            LoadTakes();
            StartAmbience();
        }

        // ---------- Combat layer ----------

        /// <summary>The local player's round left the magazine (DuelFirstPerson.WeaponFired).</summary>
        public void OwnShot(int slot) => PlayFlat(ShotClip(slot), 1f, localSlot, "shot");

        /// <summary>A line in the -duelAudioLog event log that is not a sound.</summary>
        public void Note(string what) => Log(localSlot, what, "(none)");

        /// <summary>Your bullet hit the opponent: one flat confirm at any range, a head hit with a sharp ping over it.</summary>
        public void HitConfirm(bool head)
        {
            PlayFlat("Hit_Enemy__impactSoft_medium_000", head ? .7f : .85f, localSlot, head ? "head confirm" : "hit confirm");
            if (!head) return;
            var clip = Clip(HeadPing);
            if (clip == null) return;
            ping.PlayOneShot(clip, 1f);
            Log(localSlot, "head confirm ping", HeadPing + " pitch " + HeadPingPitch);
        }

        /// <summary>
        /// A round striking cover or the ground, at the point, from either gun. One generic impact for every surface: the
        /// soft impact the campaign uses for earth is the hit confirm here, and a miss must not sound like a hit.
        /// </summary>
        public void Impact(int shooter, Vector3 at) => PlayAt(ImpactClip, at, .6f, 1.5f, 25f, shooter, "impact");

        /// <summary>The opponent's shot as the play view times it, and whether it passes close by the local eye.</summary>
        public void OpponentShot(int slot, Vector3 muzzle, Vector3 end, Vector3 localEye)
        {
            int slotOf = 1 - localSlot;
            PlayAt(ShotClip(slot), muzzle, 1f, 4f, 70f, slotOf, "shot");
            Vector3 line = end - muzzle;
            float t = Mathf.Clamp01(Vector3.Dot(localEye - muzzle, line) / Mathf.Max(line.sqrMagnitude, 1e-4f));
            Vector3 closest = muzzle + line * t;
            float miss = Vector3.Distance(closest, localEye);
            if (t > 0f && t < 1f && miss > .3f && miss < WhizzDistance)
                PlayAt("Bullet_Whizz__swish-1", closest, .8f, 1f, 8f, slotOf, "bullet passes close");
        }

        /// <summary>
        /// Called every drawn frame after the bodies are placed: compares what each player is doing with last frame.
        /// <paramref name="opponent"/> is the opponent as drawn; <paramref name="opponentReloading"/> its snapshot's flag.
        /// </summary>
        public void Tick(in DuelMatchView match, in DuelSimState mine, in DuelSimState opponent, bool opponentReloading, float deltaTime)
        {
            if (!started || match.RoundGeneration != generation)
            {
                // A new round respawns both players: start every comparison afresh, so the reset is not a heal or a draw.
                Baseline(own, mine, mine.ReloadAt(mine.ActiveWeaponSlot) > 0);
                Baseline(them, opponent, opponentReloading);
                generation = match.RoundGeneration;
            }
            Round(match);
            started = true;

            Vector3 opponentChest = opponent.Position + Vector3.up * (opponent.Height * .7f - DuelArenaLayout.GroundY);
            TickPlayer(own, mine, mine.ReloadAt(mine.ActiveWeaponSlot) > 0, true, mine.KnifeRemaining > 0, Vector3.zero, deltaTime);
            TickPlayer(them, opponent, opponentReloading, false, KnifeHeld(opponent), opponentChest, deltaTime);
            Damage(own, mine, true, them.knife >= 0f, Vector3.zero);
            Damage(them, opponent, false, own.knife >= 0f, opponentChest);

            TickFootsteps(own, mine, true);
            TickFootsteps(them, opponent, false);
        }

        private static bool KnifeHeld(in DuelSimState s) =>
            s.ActiveWeaponSlot == DuelPlayerSim.KnifeSlot && (s.PreviousButtons & DuelButtons.Fire) != 0 && !s.Dead;

        private static void Baseline(Player p, in DuelSimState s, bool reloading)
        {
            p.health = s.Health;
            p.weapon = s.ActiveWeaponSlot;
            p.reloading = reloading;
            p.reload = null;
            p.knife = -1f;
            p.lastFoot = s.Position;
            p.stride = 0f;
        }

        private void TickPlayer(Player p, in DuelSimState s, bool reloading, bool mine, bool slashing, Vector3 at, float deltaTime)
        {
            int slot = mine ? localSlot : 1 - localSlot;
            if (s.ActiveWeaponSlot != p.weapon)
            {
                p.weapon = s.ActiveWeaponSlot;
                p.reload = null;
                // The knife comes out of its sheath without a draw sound, as in the campaign.
                if (p.weapon < DuelPlayerSim.KnifeSlot) Play(mine, DrawClip(p.weapon), .8f, at, slot, "draw");
            }

            // Knife: your own slash starts when the sim's swing does; the opponent's repeats every swing while held.
            if (p.knife >= 0f)
            {
                p.knife += deltaTime;
                if (p.knife >= CombatRules.KnifeSwingSeconds) p.knife = -1f;
            }
            if (slashing && p.knife < 0f)
            {
                p.knife = 0f;
                Play(mine, Variant("Enemy_Knife_Slash", "knife_slash"), 1f, at, slot, "knife slash");
            }

            if (reloading && !p.reloading && s.ActiveWeaponSlot < DuelPlayerSim.KnifeSlot)
            {
                var gun = DuelPlayerSim.WeaponAt(s.ActiveWeaponSlot);
                int missing = mine ? gun.MagazineSize - s.AmmoAt(s.ActiveWeaponSlot) : gun.MagazineSize;
                p.reload = BuildReload(s.ActiveWeaponSlot, missing);
                p.reload.total = gun.ReloadSecondsFor(missing);
            }
            if (!reloading && p.reload != null)
            {
                // A shell reload's last rounds are not known for the opponent: close it with the end cloth when it stops.
                if (p.reload.shells && p.reload.next > 0 && p.reload.contacts[p.reload.contacts.Count - 1].clip is string end)
                    Play(mine, end, .8f, at, slot, "reload end");
                p.reload = null;
            }
            p.reloading = reloading;
            if (p.reload != null)
            {
                var r = p.reload;
                // Your own reload follows the sim's timer, so a long frame (the session steps at most Time.deltaTime)
                // cannot run the contacts ahead of the viewmodel. The opponent's snapshot carries only a reloading flag,
                // no timer, so its contacts count drawn frames: that asymmetry is deliberate, not an oversight.
                if (mine) r.elapsed = Mathf.Max(r.elapsed, r.total - s.ReloadAt(r.slot));
                else r.elapsed += deltaTime;
                int last = r.shells ? r.contacts.Count - 1 : r.contacts.Count;
                while (r.next < last && r.contacts[r.next].time <= r.elapsed)
                {
                    Play(mine, r.contacts[r.next].clip, .85f, at, slot, "reload contact");
                    r.next++;
                }
            }
        }

        private void Damage(Player p, in DuelSimState s, bool mine, bool byKnife, Vector3 at)
        {
            int slot = mine ? localSlot : 1 - localSlot;
            if (s.Health < p.health - .01f)
            {
                Hurt?.Invoke(mine, p.health - s.Health, s.Health <= 0f && p.health > 0f, byKnife);
                if (byKnife) Play(mine, Variant("Enemy_Knife_Hit", "knife_hit"), 1f, at, slot, "knife hit");
                if (mine) PlayFlat(Variant3("Player_Hurt", "hit"), .9f, slot, "hurt");
                if (s.Health <= 0f && p.health > 0f) Play(mine, "Death_Vocal__die1", 1f, at, slot, "death");
            }
            p.health = s.Health;
        }

        private void Round(in DuelMatchView match)
        {
            if (started && match.Phase == phase) return;
            var was = phase;
            phase = match.Phase;
            if (!started) return;
            if (phase == DuelPhase.Live && was != DuelPhase.Live)
                PlayFlat("Supply_Complete__metalLatch_confirmation", .9f, -1, "round start");
            else if ((phase == DuelPhase.RoundOver || phase == DuelPhase.MatchOver) && was == DuelPhase.Live)
            {
                // A draw is silent by its own branch: it must never fall through to the loss.
                if (match.RoundWinnerSlot < 0) Log(-1, "round end: draw", "(silent)");
                else if (match.RoundWinnerSlot == localSlot) PlayFlat("Victory__confirmation_004", .9f, -1, "round end: won");
                else PlayFlat("Defeat__error_004", .9f, -1, "round end: lost");
            }
        }

        private static Reload BuildReload(int slot, int missing)
        {
            var weapon = DuelPlayerSim.WeaponAt(slot);
            var reload = new Reload { slot = slot };
            if (weapon.RoundReload is RoundReload rounds)
            {
                // Start cloth, a shell per round at the insert take's contact, then the end cloth.
                reload.shells = true;
                float insert = takes != null && takes.TryGetValue(ContentIds.Shotgun, out var shell) && shell.Count > 0 ? shell[0].time : .175f;
                float local = Mathf.Clamp01(insert / .425f);
                reload.contacts.Add((0f, "XM1014_Reload_Start__cloth2"));
                for (int i = 0; i < Mathf.Max(1, missing); i++)
                    reload.contacts.Add((rounds.StartSeconds + (i + local) * rounds.PerRoundSeconds, "XM1014_Insertshell__xm1014_insertshell"));
                reload.contacts.Add((rounds.StartSeconds + Mathf.Max(1, missing) * rounds.PerRoundSeconds, "XM1014_Reload_End__cloth1"));
                return reload;
            }
            if (takes != null && takes.TryGetValue(weapon.Id, out var events))
            {
                // The .takes contacts are in the take's seconds; the take and the sim's reload are the same length.
                foreach (var e in events) reload.contacts.Add(e);
            }
            return reload;
        }

        // ---------- Second layer: ambience and footsteps ----------

        private void StartAmbience()
        {
            var clip = Clip("Ambience_Forest__cicadas_loop");
            if (clip == null) return;
            var source = Source("Ambience", false);
            source.clip = clip;
            source.loop = true;
            source.volume = Master * AmbienceVolume;
            source.Play();
        }

        /// <summary>A step every stride on the ground: the opponent's clear in 3D, your own quieter than your gunfire.</summary>
        private void TickFootsteps(Player p, in DuelSimState s, bool mine)
        {
            Vector3 delta = s.Position - p.lastFoot;
            p.lastFoot = s.Position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance > 1.8f || s.Dead || s.Position.y > DuelArenaLayout.GroundY + .05f) { p.stride = 0f; return; }
            p.stride += distance;
            if (p.stride < (s.Crouch > .5f ? .7f : .9f)) return;
            p.stride = 0f;
            p.step = (p.step + 1) % 4;
            string clip = "Footstep_Dirt__dirt_0" + (p.step + 1);
            float quiet = s.Walk > .5f || s.Crouch > .5f ? .45f : 1f;
            var foot = s.Position - Vector3.up * DuelArenaLayout.GroundY;
            if (mine) PlayFlat(clip, .22f * quiet, localSlot, "footstep");
            else PlayAt(clip, foot, .9f * quiet, 2f, 28f, 1 - localSlot, "footstep");
        }

        // ---------- Clips and sources ----------

        private static string ShotClip(int slot)
        {
            string id = DuelPlayerSim.WeaponAt(slot).Id;
            return id == ContentIds.Smg ? "SMG_Shot__mp5_01" : id == ContentIds.Shotgun ? "Shotgun_Shot__xm1014-1" : "Rifle_Shot__ak47_01";
        }

        private static string DrawClip(int slot)
        {
            string id = DuelPlayerSim.WeaponAt(slot).Id;
            return id == ContentIds.Smg ? "MP5SD_Draw__mp5_draw" : id == ContentIds.Shotgun ? "XM1014_Draw__xm1014_draw" : "AK47_Draw__ak47_draw";
        }

        private int variant;
        private string Variant(string prefix, string suffix) { variant = 1 - variant; return prefix + (variant + 1) + "__" + suffix + (variant + 1); }
        private int hurt;
        private string Variant3(string prefix, string suffix) { hurt = (hurt + 1) % 3; return prefix + "__" + suffix + (hurt + 1); }

        /// <summary>The reload contacts of the rifle and SMG viewmodel takes, and the shotgun's insert contact.</summary>
        private static void LoadTakes()
        {
            if (takes != null) return;
            takes = new Dictionary<string, List<(float, string)>>(StringComparer.Ordinal);
            Load(ContentIds.Rifle, "Viewmodels/weapon.rifle/v_ak47.takes", "reload", "AK47_");
            Load(ContentIds.Smg, "Viewmodels/weapon.smg/v_mp5sd.takes", "reload", "MP5SD_");
            Load(ContentIds.Shotgun, "Viewmodels/weapon.shotgun/v_xm1014.takes", "insert", "XM1014_");

            static void Load(string weapon, string path, string take, string prefix)
            {
                var text = Resources.Load<TextAsset>(path);
                if (text == null) { Debug.LogWarning("Duel audio: no " + path + "; that gun reloads silently."); return; }
                string json = "{\"takes\":" + text.text.Trim() + "}";
                json = json.Replace("\"events\": \"none\"", "\"events\": []").Replace("\"events\":\"none\"", "\"events\":[]");
                TakesFile file;
                try { file = JsonUtility.FromJson<TakesFile>(json); }
                catch (ArgumentException) { return; }
                if (file?.takes == null) return;
                foreach (var t in file.takes)
                {
                    if (t == null || !string.Equals(t.take, take, StringComparison.OrdinalIgnoreCase) || t.events == null) continue;
                    var list = new List<(float, string)>();
                    foreach (var e in t.events)
                    {
                        if (e == null) continue;
                        string name = (e.options ?? string.Empty).ToLowerInvariant();
                        string clip = ContactClip(weapon, prefix, name);
                        if (clip != null) list.Add((e.time_s, clip));
                    }
                    takes[weapon] = list;
                    return;
                }
            }
        }

        private static string ContactClip(string weapon, string prefix, string name)
        {
            bool smg = weapon == ContentIds.Smg;
            if (name.Contains("insertshell")) return "XM1014_Insertshell__xm1014_insertshell";
            if (name.Contains("clipout")) return prefix + "Clipout__" + (smg ? "mp5_clipout" : "ak47_clipout");
            if (name.Contains("clipin")) return prefix + "Clipin__" + (smg ? "mp5_clipin" : "ak47_clipin");
            if (name.Contains("slideback")) return "MP5SD_Slideback__mp5_slideback";
            if (name.Contains("slideforward") || name.Contains("boltpull"))
                return smg ? "MP5SD_Slideforward__mp5_slideforward" : "AK47_Boltpull__ak47_boltpull";
            return null;
        }

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] private sealed class TakesFile { public TakesTake[] takes; }
        [Serializable] private sealed class TakesTake { public string take; public TakesEvent[] events; }
        [Serializable] private sealed class TakesEvent { public float time_s; public string options; }
#pragma warning restore 0649

        private static AudioClip Clip(string name)
        {
            if (clips.TryGetValue(name, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(ResourceRoot + name);
            if (clip == null) Debug.LogWarning("Duel audio: no clip " + ResourceRoot + name + ".");
            clips[name] = clip;
            return clip;
        }

        private AudioSource Source(string name, bool positional)
        {
            var go = new GameObject("DuelAudio_" + name);
            go.transform.SetParent(root, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = positional ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f;
            source.volume = Master * Sfx;
            return source;
        }

        private void Play(bool mine, string clip, float volume, Vector3 at, int slot, string what)
        {
            if (mine) PlayFlat(clip, volume, slot, what);
            else PlayAt(clip, at, volume, 2f, 30f, slot, what);
        }

        private void PlayFlat(string name, float volume, int slot, string what)
        {
            var clip = Clip(name);
            if (clip == null) return;
            flat.PlayOneShot(clip, volume);
            Log(slot, what, name);
        }

        private void PlayAt(string name, Vector3 at, float volume, float near, float far, int slot, string what)
        {
            var clip = Clip(name);
            if (clip == null) return;
            var source = world[worldCursor];
            worldCursor = (worldCursor + 1) % world.Length;
            source.Stop();
            source.transform.position = at;
            source.minDistance = near;
            source.maxDistance = far;
            source.clip = clip;
            source.volume = Master * Sfx * volume;
            source.Play();
            Log(slot, what, name + " at " + at.ToString("F1"));
        }

        private void Log(int slot, string what, string clip)
        {
            if (LogEvents) Debug.Log($"DuelAudio t={Time.time:0.00} slot {(slot < 0 ? "-" : slot.ToString())} {what} -> {clip}");
        }
    }
}
