using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TienTuyen.Content;
using TienTuyen.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TienTuyen.Combat.Tests
{
    public sealed class CombatLifecycleTests
    {
        private CombatGame game;

        [UnitySetUp]
        public IEnumerator LoadCombatScene()
        {
            yield return SceneManager.LoadSceneAsync("CombatSpike", LoadSceneMode.Single);
            game = Object.FindFirstObjectByType<CombatGame>();
            Assert.That(game, Is.Not.Null, "CombatSpike must contain the gameplay entry point.");
            game.enabled = false;
            game.BeginRun();
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator CleanScene()
        {
            var combatScene = SceneManager.GetActiveScene();
            var empty = SceneManager.CreateScene("Combat test cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(combatScene);
        }

        [Test]
        public void RunStartsWithFullHealthAndReadyWeapon()
        {
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.WaveRemaining, Is.EqualTo(40f).Within(0.001f));
            Assert.That(game.Health, Is.EqualTo(game.MaxHealth));
            Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
            Assert.That(game.ReloadRemaining, Is.Zero);
            Assert.That(game.Currency, Is.Zero);
            Assert.That(game.Level, Is.EqualTo(1));
            Assert.That(game.Experience, Is.Zero);
            Assert.That(game.Kills, Is.Zero);
        }

        [Test]
        public void MusicCrossfadeCanReverseAndPauseOrMuteBothTracks()
        {
            var audio = game.GetComponent<CombatAudio>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var tick = typeof(CombatAudio).GetMethod("TickMusic", flags);
            var menu = (AudioSource)typeof(CombatAudio).GetField("menuMusic", flags).GetValue(audio);
            var playing = (AudioSource)typeof(CombatAudio).GetField("playingMusic", flags).GetValue(audio);
            float menuLevel = audio.MasterVolume * audio.MusicVolume;
            float playingLevel = audio.MasterVolume * audio.PlayingMusicVolume;
            game.ReturnToMenu();
            var ambience = (AudioSource)typeof(CombatAudio).GetField("ambience", flags).GetValue(audio);
            audio.SetIntroPlaying(true);
            Assert.That(ambience.isPlaying, Is.False);
            Assert.That(audio.GetComponentsInChildren<AudioSource>().All(source => source.volume == 0f), Is.True);
            audio.SetIntroPlaying(false);
            Assert.That(ambience.isPlaying, Is.True);
            Assert.That(ambience.volume, Is.EqualTo(audio.MasterVolume * audio.AmbienceVolume).Within(.001f));
            audio.StartMusic(IntroVideoGate.HandoffMenuOffsetSeconds, .25f);
            Assert.That(menu.time, Is.EqualTo(IntroVideoGate.HandoffMenuOffsetSeconds).Within(.05f));
            Assert.That(menu.volume + playing.volume, Is.Zero);
            tick.Invoke(audio, new object[] { .125f });
            Assert.That(menu.volume, Is.EqualTo(menuLevel * .5f).Within(.001f));
            tick.Invoke(audio, new object[] { .125f });
            Assert.That(menu.volume, Is.EqualTo(menuLevel).Within(.001f));
            audio.StartMusic();
            Assert.That(menu.time, Is.Zero.Within(.05f));
            Assert.That(menu.volume, Is.EqualTo(menuLevel).Within(.001f));
            Assert.That(playing.volume, Is.Zero);

            game.BeginRun();
            tick.Invoke(audio, new object[] { .5f });
            Assert.That(menu.volume, Is.GreaterThan(0f).And.LessThan(menuLevel));
            Assert.That(playing.volume, Is.GreaterThan(0f).And.LessThan(playingLevel));
            float menuHalf = menu.volume;
            float playingHalf = playing.volume;
            game.TogglePause();
            tick.Invoke(audio, new object[] { 0f });
            Assert.That(menu.volume, Is.EqualTo(menuHalf * .06f).Within(.001f));
            Assert.That(playing.volume, Is.EqualTo(playingHalf * .06f).Within(.001f));
            audio.MuteMusic = true;
            tick.Invoke(audio, new object[] { 0f });
            Assert.That(menu.volume + playing.volume, Is.Zero);
            audio.MuteMusic = false;
            game.ReturnToMenu();
            tick.Invoke(audio, new object[] { .25f });
            Assert.That(menu.volume, Is.GreaterThan(menuHalf));
            Assert.That(playing.volume, Is.LessThan(playingHalf));
            tick.Invoke(audio, new object[] { 1f });
            Assert.That(menu.volume, Is.EqualTo(menuLevel).Within(.001f));
            Assert.That(playing.volume, Is.Zero);
            game.BeginRun();
            tick.Invoke(audio, new object[] { 1f });
            Assert.That(menu.volume, Is.Zero);
            Assert.That(playing.volume, Is.EqualTo(playingLevel).Within(.001f));
        }

        [Test]
        public void ShotgunIsAvailableAsThirdP2Weapon()
        {
            game.ReturnToMenu();
            game.SelectWeapon(WeaponKind.Shotgun);
            game.BeginRun();
            Assert.That(game.SelectedWeapon, Is.EqualTo(WeaponKind.Shotgun));
            Assert.That(game.MagazineSize, Is.EqualTo(5));
            Assert.That(game.Ammo, Is.EqualTo(5));
            Assert.That(game.MaxWave, Is.EqualTo(6));

            // The shotgun loads shell by shell: its reload is a start, a time per missing shell and an end.
            var rounds = ContentCatalog.P2.GetWeapon(ContentIds.Shotgun).RoundReload.Value;
            float Expected(int missing) => (rounds.StartSeconds + rounds.PerRoundSeconds * missing + rounds.EndSeconds) * game.Stats.ReloadFactor;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ammo = (int[])typeof(CombatGame).GetField("weaponAmmo", flags).GetValue(game);
            var reload = (float[])typeof(CombatGame).GetField("weaponReload", flags).GetValue(game);
            ammo[0] = 4;
            game.RequestReload();
            Assert.That(game.ReloadDuration, Is.EqualTo(Expected(1)).Within(0.001f));
            Assert.That(game.ReloadRemaining, Is.EqualTo(Expected(1)).Within(0.001f));
            reload[0] = 0;
            ammo[0] = 0;
            game.RequestReload();
            Assert.That(game.ReloadDuration, Is.EqualTo(Expected(5)).Within(0.001f));
            Assert.That(Expected(5), Is.GreaterThan(Expected(1) + 0.5f));
        }

        [UnityTest]
        public IEnumerator ChargerSpawnsFromWaveTwoAndExposesTelegraphThenDash()
        {
            // There is no player-facing jump-to-wave command. Force the wave through
            // the private state boundary so this test avoids a 40-second RNG-dependent wait.
            SetAutoPropertyBackingField("<Wave>k__BackingField", 2);
            InvokePrivate("ResetWave");
            var fireTimers = new float[TienTuyen.Progression.ProgressionRun.MaxWeaponSlots];
            for (var i = 0; i < fireTimers.Length; i++) fireTimers[i] = 999f;
            SetPrivateField("weaponFireTimer", fireTimers);
            // The run seed is random; keep the idle, unarmed player alive so melee
            // hits cannot end the run before a charger is rolled.
            SetPrivateField("invulnerability", 999f);
            // Squads are small; make the first spawn the charger rather than wait on the roll.
            SetPrivateField("forcedEnemyDefinition", ContentCatalog.P2.GetEnemy(ContentIds.Charger));
            var sawCharger = false;
            var sawTelegraph = false;
            var sawDash = false;
            for (var frame = 0; frame < 60 * 24; frame++)
            {
                game.StepSimulation(1f / 60f, Vector2.zero);
                // Encounter tutorials pause the real run; acknowledge them before continuing this lifecycle probe.
                if (game.SniperTutorialVisible) game.TogglePause();
                sawCharger |= game.ActiveChargerCount > 0 || game.PendingChargerCount > 0;
                sawTelegraph |= game.ChargerTelegraphVisible;
                sawDash |= game.ChargerDashActive;
                if (sawTelegraph && sawDash) break;
                yield return null;
            }
            Assert.That(sawCharger, Is.True, "Wave 2 must be eligible to spawn a charger.");
            Assert.That(sawTelegraph, Is.True, "Charger must show a directional telegraph before dashing.");
            Assert.That(sawDash, Is.True, "Charger must enter its distinct dash state after telegraph.");
            game.Restart();
            Assert.That(game.ActiveChargerCount, Is.Zero);
            Assert.That(game.PendingChargerCount, Is.Zero);
            Assert.That(game.ChargerTelegraphVisible, Is.False);
            Assert.That(game.ChargerDashActive, Is.False);
            Assert.That(game.AliveEnemies, Is.Zero);
        }

        [Test]
        public void WavesThreeAndFourFieldTheirEliteQuotaSpreadOutUnderTheCap()
        {
            // Waves 3-4 field exactly their quota (3, 6) whatever the run seed: the first not before 8 s,
            // never more in play than the wave allows. Every enemy falls 6 s after landing (the player's part).
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var entries = (System.Array)typeof(CombatGame).GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            var damage = typeof(CombatGame).GetMethod("DamageEnemy", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var wave = 3; wave <= 4; wave++)
            {
                SetAutoPropertyBackingField("<Wave>k__BackingField", wave);
                InvokePrivate("ResetWave");
                var fireTimers = new float[TienTuyen.Progression.ProgressionRun.MaxWeaponSlots];
                for (var i = 0; i < fireTimers.Length; i++) fireTimers[i] = 999f;
                SetPrivateField("weaponFireTimer", fireTimers);
                SetPrivateField("invulnerability", 999f);
                var landed = new float[entries.Length];
                var announced = new bool[entries.Length];
                int fielded = 0, peak = 0;
                float first = -1f, elapsed = 0f;
                while (elapsed < game.WaveLength - .1f && game.State == CombatState.Playing)
                {
                    game.StepSimulation(1f / 60f, Vector2.zero);
                // Encounter tutorials pause the real run; acknowledge them before continuing this lifecycle probe.
                if (game.SniperTutorialVisible) game.TogglePause();
                    elapsed += 1f / 60f;
                    var inPlay = 0;
                    for (var i = 0; i < entries.Length; i++)
                    {
                        var entry = entries.GetValue(i);
                        var type = entry.GetType();
                        bool active = (bool)type.GetField("active", flags).GetValue(entry), pending = (bool)type.GetField("pending", flags).GetValue(entry);
                        bool elite = (active || pending) && (bool)type.GetField("elite", flags).GetValue(entry);
                        if (elite && !announced[i]) { fielded++; if (first < 0f) first = elapsed; }
                        announced[i] = elite;
                        if (elite) inPlay++;
                        if (!active) { landed[i] = -1f; continue; }
                        if (landed[i] < 0f) landed[i] = elapsed;
                        if (elapsed - landed[i] >= 6f) damage.Invoke(game, new object[] { entry, 1e6f });
                    }
                    peak = Mathf.Max(peak, inPlay);
                }
                Assert.That(fielded, Is.EqualTo(CombatGame.EliteQuota(wave)), "Elites fielded in wave " + wave);
                Assert.That(first, Is.GreaterThanOrEqualTo(8f), "First Elite of wave " + wave);
                Assert.That(peak, Is.LessThanOrEqualTo(CombatGame.MaxElitesInPlay(wave)), "Elites in play at once in wave " + wave);
            }
        }

        [Test]
        public void WavesSendSquadsWithLullsAndNeverPassTheAliveCap()
        {
            // Nobody dies here, so wave 2 shows the bare squad schedule and wave 6 fills up to the cap.
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var entries = ((System.Array)typeof(CombatGame).GetField("enemies", flags).GetValue(game)).Cast<object>().ToArray();
            var fireTimers = new float[TienTuyen.Progression.ProgressionRun.MaxWeaponSlots];
            for (var wave = 2; wave <= 6; wave += 4)
            {
                SetAutoPropertyBackingField("<Wave>k__BackingField", wave);
                InvokePrivate("ResetWave");
                for (var i = 0; i < fireTimers.Length; i++) fireTimers[i] = 999f;
                SetPrivateField("weaponFireTimer", fireTimers);
                SetPrivateField("invulnerability", 999f);
                var announced = new bool[entries.Length];
                var spawns = new System.Collections.Generic.List<float>();
                int peak = 0;
                float elapsed = 0f;
                while (elapsed < game.WaveLength - .1f && game.State == CombatState.Playing)
                {
                    SimulatePastTutorials(1f / 60f);
                    elapsed += 1f / 60f;
                    for (var i = 0; i < entries.Length; i++)
                    {
                        bool pending = (bool)entries[i].GetType().GetField("pending", flags).GetValue(entries[i]);
                        if (pending && !announced[i]) spawns.Add(elapsed);
                        announced[i] = pending;
                    }
                    Assert.That(game.AliveEnemies, Is.LessThanOrEqualTo(CombatRules.EnemyCap), "Alive in wave " + wave);
                    peak = Mathf.Max(peak, game.AliveEnemies);
                }
                if (wave == 6)
                {
                    Assert.That(peak, Is.EqualTo(CombatRules.EnemyCap), "Wave 6 sends more than the cap and must fill it");
                    continue;
                }
                int size = CombatRules.SquadSize(wave);
                float lull = CombatRules.SquadIntervalSeconds(wave) - (size - 1) * CombatRules.SquadSpacingSeconds;
                int squads = Mathf.CeilToInt((game.WaveLength - .1f - CombatRules.FirstSquadSeconds) / CombatRules.SquadIntervalSeconds(wave));
                Assert.That(spawns.Count, Is.EqualTo(squads * size), "Spawns in wave " + wave + ": " + string.Join(", ", spawns));
                Assert.That(spawns[0], Is.EqualTo(CombatRules.FirstSquadSeconds).Within(.05f));
                for (var i = 1; i < spawns.Count; i++)
                {
                    float gap = spawns[i] - spawns[i - 1];
                    if (i % size == 0) Assert.That(gap, Is.EqualTo(lull).Within(.05f), "Lull before spawn " + i);
                    else Assert.That(gap, Is.EqualTo(CombatRules.SquadSpacingSeconds).Within(.05f), "Squad spacing at spawn " + i);
                }
            }
        }

        [Test]
        public void DefeatedChargerPaysExperienceAndPickupRewardOnlyOnce()
        {
            SetAutoPropertyBackingField("<Wave>k__BackingField", 2);
            InvokePrivate("ResetWave");
            SetPrivateField("spawnTimer", 0f);
            InvokePrivate("UpdateSpawn", 0f);
            var entries = (System.Array)typeof(CombatGame)
                .GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var pendingEntries = entries.Cast<object>()
                .Where(entry => (bool)entry.GetType().GetField("pending", flags).GetValue(entry)).ToArray();
            Assert.That(pendingEntries, Has.Length.EqualTo(1), "Reward setup must announce exactly one enemy.");
            var pendingCharger = pendingEntries[0];
            var enemyType = pendingCharger.GetType();
            var definition = ContentCatalog.P2.GetEnemy(ContentIds.Charger);
            enemyType.GetField("definition", flags).SetValue(pendingCharger, definition);
            enemyType.GetField("elite", flags).SetValue(pendingCharger, definition.Role == EnemyRole.Elite);
            enemyType.GetField("shooter", flags).SetValue(pendingCharger, definition.Role == EnemyRole.Shooter);
            enemyType.GetField("charger", flags).SetValue(pendingCharger, definition.Role == EnemyRole.Charger);
            InvokePrivate("UpdateEnemies", (float)enemyType.GetField("warningTimer", flags).GetValue(pendingCharger));
            var charger = FindActiveCharger();
            Assert.That(charger, Is.Not.Null, "The explicitly spawned charger must be active for reward testing.");
            var killsBefore = game.Kills;
            var experienceBefore = game.Experience;
            var pickupsBefore = CountActivePickups();
            // DamageEnemy is intentionally private; invoking the same pooled entry twice
            // isolates the idempotency seam without depending on weapon RNG or aim timing.
            var damageEnemy = typeof(CombatGame).GetMethod("DamageEnemy", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(damageEnemy, Is.Not.Null, "Test seam changed: missing private method DamageEnemy");
            damageEnemy.Invoke(game, new[] { charger, (object)100000f });
            var killsAfterFirstHit = game.Kills;
            var experienceAfterFirstHit = game.Experience;
            var pickupsAfterFirstHit = CountActivePickups();
            damageEnemy.Invoke(game, new[] { charger, (object)100000f });
            Assert.That(killsAfterFirstHit, Is.EqualTo(killsBefore + 1));
            Assert.That(game.Kills, Is.EqualTo(killsAfterFirstHit));
            Assert.That(experienceAfterFirstHit, Is.EqualTo(experienceBefore + 1));
            Assert.That(game.Experience, Is.EqualTo(experienceAfterFirstHit));
            Assert.That(pickupsAfterFirstHit, Is.EqualTo(pickupsBefore + 1));
            Assert.That(CountActivePickups(), Is.EqualTo(pickupsAfterFirstHit));
        }

        [UnityTest]
        public IEnumerator PauseFreezesRealUpdateAndDeterministicSimulation()
        {
            Simulate(2f, Vector2.right);
            game.TogglePause();
            Assert.That(game.State, Is.EqualTo(CombatState.Paused));
            var remaining = game.WaveRemaining;
            var elapsed = game.RunElapsed;
            var health = game.Health;
            var ammo = game.Ammo;
            var reload = game.ReloadRemaining;
            var positions = game.GetComponentsInChildren<Transform>(true)
                .ToDictionary(t => t, t => t.position);

            game.enabled = true;
            for (var frame = 0; frame < 5; frame++) yield return null;
            game.enabled = false;
            Simulate(2f, Vector2.one);

            Assert.That(game.WaveRemaining, Is.EqualTo(remaining));
            Assert.That(game.RunElapsed, Is.EqualTo(elapsed));
            Assert.That(game.Health, Is.EqualTo(health));
            Assert.That(game.Ammo, Is.EqualTo(ammo));
            Assert.That(game.ReloadRemaining, Is.EqualTo(reload));
            foreach (var item in positions)
                Assert.That(item.Key.position, Is.EqualTo(item.Value), item.Key.name);

            game.TogglePause();
            Simulate(0.2f, Vector2.zero);
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Assert.That(game.WaveRemaining, Is.LessThan(remaining));
        }

        [Test]
        public void DamageInvulnerabilityPreventsStackedHits()
        {
            game.ApplyPlayerDamage(10f);
            var afterFirst = game.Health;
            game.ApplyPlayerDamage(10f);
            Assert.That(game.Health, Is.EqualTo(afterFirst));
            Simulate(0.4f, Vector2.zero);
            game.ApplyPlayerDamage(10f);
            Assert.That(game.Health, Is.EqualTo(afterFirst - 10f).Within(0.001f));
        }

        [Test]
        public void BulletNearMissIsAnnouncedOnceAfterPassingThePlayer()
        {
            var player = (Transform)typeof(CombatGame).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            var bullets = (System.Array)typeof(CombatGame).GetField("bullets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            var bullet = bullets.GetValue(0);
            var type = bullet.GetType();
            type.GetField("active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(bullet, true);
            type.GetField("velocity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(bullet, Vector3.left * 18f);
            type.GetField("remaining", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(bullet, 3f);
            type.GetField("nearMissPlayed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(bullet, false);
            var body = (GameObject)type.GetField("body", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(bullet);
            body.SetActive(true);
            body.transform.position = player.position + new Vector3(.8f, 0, .7f);
            var announcements = 0;
            System.Action<Vector3> onNearMiss = _ => announcements++;
            game.BulletNearMiss += onNearMiss;
            game.StepSimulation(.1f, Vector2.zero);
            game.StepSimulation(.1f, Vector2.zero);
            game.BulletNearMiss -= onNearMiss;
            Assert.That(announcements, Is.EqualTo(1));
        }

        [Test]
        public void DeathStopsClockAndDoesNotPaySurvivalReward()
        {
            var currency = game.Currency;
            game.ApplyPlayerDamage(game.MaxHealth * 10f);
            var remaining = game.WaveRemaining;
            Simulate(41f, Vector2.one);
            game.ContinueWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Defeat));
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.WaveRemaining, Is.EqualTo(remaining));
            Assert.That(game.Currency, Is.EqualTo(currency));
        }

        [Test]
        public void TwentyRestartsResetRunAndDoNotGrowObjectPools()
        {
            var objectCount = game.GetComponentsInChildren<Transform>(true).Length;
            for (var run = 0; run < 20; run++)
            {
                Simulate(2f, Vector2.right);
                game.ApplyPlayerDamage(10f);
                game.TogglePause();
                game.Restart();
                Assert.That(game.State, Is.EqualTo(CombatState.Playing), $"Restart {run}");
                Assert.That(game.Health, Is.EqualTo(game.MaxHealth));
                Assert.That(game.Wave, Is.EqualTo(1));
                Assert.That(game.WaveRemaining, Is.EqualTo(40f).Within(0.001f));
                Assert.That(game.Currency, Is.Zero);
                Assert.That(game.Level, Is.EqualTo(1));
                Assert.That(game.Experience, Is.Zero);
                Assert.That(game.Kills, Is.Zero);
                Assert.That(game.AliveEnemies, Is.Zero);
                Assert.That(game.RunElapsed, Is.Zero);
                Assert.That(game.DamageDealt, Is.Zero);
                Assert.That(game.Ammo, Is.EqualTo(game.MagazineSize));
                Assert.That(game.ReloadRemaining, Is.Zero);
                Assert.That(game.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objectCount));
            }
        }

        [Test]
        public void WaveCompletionConsumesUpgradeBeforeShopAndNextWave()
        {
            game.Run.AddExperience(8);
            Assert.That(game.Level, Is.EqualTo(2));
            FinishWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Upgrade));
            Assert.That(game.UpgradeChoiceCount, Is.EqualTo(3));
            game.ContinueWave();
            Assert.That(game.Wave, Is.EqualTo(1));
            Assert.That(game.ChooseUpgrade(0), Is.True);
            Assert.That(game.State, Is.EqualTo(CombatState.Shop));
            Assert.That(game.Shop, Is.Not.Null);
            Assert.That(Enumerable.Range(0, 4).Count(i => game.Shop.OfferAt(i) != null), Is.EqualTo(4));
            game.ContinueWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Playing));
            Assert.That(game.Wave, Is.EqualTo(2));
        }

        [Test]
        public void FullSixWaveLoopReachesVictoryThroughUpgradeAndShopStates()
        {
            // Seed one level-up so the first inter-wave transition exercises the
            // upgrade branch; subsequent waves validate the direct shop branch.
            game.Run.AddExperience(8);
            Assert.That(game.Level, Is.EqualTo(2));

            for (var completedWave = 1; completedWave <= game.MaxWave; completedWave++)
            {
                Assert.That(game.State, Is.EqualTo(CombatState.Playing));
                Assert.That(game.Wave, Is.EqualTo(completedWave));

                if (completedWave == game.MaxWave)
                {
                    // There is no boss: the last wave ends the run on its own clock, surviving it wins.
                    SetPrivateField("invulnerability", 999f);
                    SimulatePastTutorials(game.WaveRemaining - .1f);
                    Assert.That(game.State, Is.EqualTo(CombatState.Playing));
                    SimulatePastTutorials(.2f);
                    Assert.That(game.State, Is.EqualTo(CombatState.Victory));
                    break;
                }

                FinishWave();

                // A wave can expose more than one level-up choice. Consume all
                // pending choices before asserting that the shop is available.
                while (game.State == CombatState.Upgrade)
                {
                    Assert.That(game.UpgradeChoiceCount, Is.EqualTo(3));
                    Assert.That(game.ChooseUpgrade(0), Is.True);
                }

                Assert.That(game.State, Is.EqualTo(CombatState.Shop));
                Assert.That(game.Shop, Is.Not.Null);
                Assert.That(Enumerable.Range(0, 4).Count(i => game.Shop.OfferAt(i) != null), Is.EqualTo(4));

                game.ContinueWave();
                Assert.That(game.State, Is.EqualTo(CombatState.Playing));
                Assert.That(game.Wave, Is.EqualTo(completedWave + 1));
            }

            Assert.That(game.State, Is.EqualTo(CombatState.Victory));
            Assert.That(game.Wave, Is.EqualTo(game.MaxWave));
        }

        [Test]
        public void ShopTransactionsRespectLocksAndWeaponSlots()
        {
            FinishWave();
            Assert.That(game.State, Is.EqualTo(CombatState.Shop));
            game.Run.AddCurrency(10000);
            var firstOffer = game.Shop.OfferAt(0);
            Assert.That(game.SetOfferLocked(0, true), Is.True);
            Assert.That(game.RerollShop(), Is.True);
            Assert.That(game.Shop.OfferAt(0), Is.SameAs(firstOffer));
            Assert.That(game.Shop.IsLocked(0), Is.True);
            int weaponSlot = Enumerable.Range(0, 4).First(i => game.Shop.OfferAt(i) != null &&
                game.Shop.OfferAt(i).Kind == TienTuyen.Progression.OfferKind.Weapon);
            Assert.That(game.PurchaseOffer(weaponSlot), Is.EqualTo(TienTuyen.Progression.PurchaseResult.Bought));
            Assert.That(game.WeaponCount, Is.EqualTo(2));
            Assert.That(game.AmmoAt(1), Is.EqualTo(game.WeaponAt(1).MagazineSize), "A bought weapon arrives loaded.");
            Assert.That(game.SellWeapon(1, out int refund), Is.True);
            Assert.That(refund, Is.GreaterThanOrEqualTo(0));
            Assert.That(game.WeaponCount, Is.EqualTo(1));
            Assert.That(game.SellWeapon(0, out _), Is.False);
        }

        [Test]
        public void HoldingCrouchOrWalkSlowsThePlayerUntilReleased()
        {
            var player = game.transform.Find("Player");
            float Walk()
            {
                float start = player.position.x;
                Simulate(0.5f, Vector2.right);
                return player.position.x - start;
            }
            float standing = Walk();

            game.SetCrouchInput(true);
            Simulate(CombatRules.CrouchSeconds, Vector2.zero);
            Assert.That(game.Crouch, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(game.PlayerHeight, Is.EqualTo(CombatRules.PlayerCrouchHeight).Within(1e-4f));
            Assert.That(Walk(), Is.EqualTo(standing * CombatRules.CrouchSpeedFactor).Within(1e-3f));

            game.SetCrouchInput(false);
            Simulate(CombatRules.CrouchSeconds, Vector2.zero);
            Assert.That(game.Crouch, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(game.PlayerHeight, Is.EqualTo(CombatRules.PlayerStandHeight).Within(1e-4f));
            Assert.That(Walk(), Is.EqualTo(standing).Within(1e-3f));

            game.SetWalkInput(true);
            Simulate(CombatRules.WalkSeconds, Vector2.zero);
            Assert.That(Walk(), Is.EqualTo(standing * CombatRules.WalkSpeedFactor).Within(1e-3f));
            // Crouching while walking moves at the crouch speed, as in CS:GO.
            game.SetCrouchInput(true);
            Simulate(CombatRules.CrouchSeconds, Vector2.zero);
            Assert.That(Walk(), Is.EqualTo(standing * CombatRules.CrouchSpeedFactor).Within(1e-3f));

            game.SetCrouchInput(false);
            game.SetWalkInput(false);
            Simulate(Mathf.Max(CombatRules.CrouchSeconds, CombatRules.WalkSeconds), Vector2.zero);
            Assert.That(Walk(), Is.EqualTo(standing).Within(1e-3f));
        }

        private void FinishWave()
        {
            typeof(CombatGame).GetMethod("FinishWave", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(game, null);
        }

        /// <summary>Steps the run, acknowledging the sniper's first-encounter tutorial pause as a player would.</summary>
        private void SimulatePastTutorials(float seconds)
        {
            var frames = Mathf.Max(1, Mathf.RoundToInt(seconds * 60f));
            for (var frame = 0; frame < frames; frame++)
            {
                game.StepSimulation(1f / 60f, Vector2.zero);
                if (game.SniperTutorialVisible) game.TogglePause();
            }
        }

        private void Simulate(float seconds, Vector2 movement)
        {
            var frames = Mathf.RoundToInt(seconds * 60f);
            for (var frame = 0; frame < frames; frame++) game.StepSimulation(1f / 60f, movement);
        }

        private void SetPrivateField(string name, object value)
        {
            var field = typeof(CombatGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Test seam changed: missing private field " + name);
            field.SetValue(game, value);
        }

        private void SetAutoPropertyBackingField(string name, object value) => SetPrivateField(name, value);

        private void InvokePrivate(string name, params object[] arguments)
        {
            var method = typeof(CombatGame).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Test seam changed: missing private method " + name);
            method.Invoke(game, arguments);
        }

        private object FindActiveCharger()
        {
            var field = typeof(CombatGame).GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Test seam changed: missing private field enemies");
            var entries = field.GetValue(game) as System.Array;
            Assert.That(entries, Is.Not.Null);
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var type = entry.GetType();
                var active = (bool)type.GetField("active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                var charger = (bool)type.GetField("charger", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (active && charger) return entry;
            }
            return null;
        }

        private int CountActivePickups()
        {
            var field = typeof(CombatGame).GetField("pickups", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Test seam changed: missing private field pickups");
            var entries = field.GetValue(game) as System.Array;
            Assert.That(entries, Is.Not.Null);
            var count = 0;
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var active = (bool)entry.GetType().GetField("active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (active) count++;
            }
            return count;
        }
    }
}
