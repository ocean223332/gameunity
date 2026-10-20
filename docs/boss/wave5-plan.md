# Wave-5 commander + RPG18 execution ledger

User approved implementation 2026-10-20, on feature branch with incremental commits. Scope is the six steps in the immediately preceding message: weapon fit, telegraphed RPG attack, wave-5 boss, health bar and retreat cutscene, weapon reward/upgrades, test cheat and Windows build. Full 10-wave/M113 campaign remains separate.

## Commits / verification gates
1. Commit previously verified asset foundation (22-bone Humanoid, 24k-triangle RPG, isolated review scene); rerun asset tests.
2. Add deterministic boss encounter and launcher reward/upgrade rules with failing-then-passing tests. Wave 5 cannot finish on its survival timer while boss remains alive; reward exactly once, even with full weapon inventory.
3. Integrate gameplay: intro, RPG telegraph/projectile/explosion, movement with cover collision, retreat, reward and test entry. Existing guns, knife, restart, pause and death must remain correct.
4. Fit boss/player weapon, HUD, cutscene camera/subtitles, effects and RPG upgrade descriptions; preserve existing uGUI style. Cutscenes skip with hold Space, no damage or run time while playing. English subtitles + Vietnamese translation; include spoken audio if an offline voice is available.
5. Integration tests, fresh whole-branch review, fixes, Windows build and evidence. Commit each verified batch. No push requested.

Rulings: RPG is a game interpretation with 1-round capacity and reload, not a simulator of a disposable tube. Upgrades affect damage, reload and blast radius, capped at three each. If all four slots are full, replace the current gun (explicit reward notice) to guarantee the unique reward without exceeding the established slot cap. F9 starts a marked cheat run directly at wave 5; F7 cannot bypass a live boss or fabricate its reward. Existing six-wave campaign is retained for this deliverable.

## Completed wave-5 implementation (2026-10-20)

- Asset foundation commit: 5721b63; progression/reward: 41621ac; combat encounter: 13b6bf6.
- Wave 5 reserves an enemy-pool slot for a 1,000 HP commander; its survival timer cannot end the encounter while the boss is active.
- Intro 6s and retreat 5s, English synthetic voice and Vietnamese subtitles. Hold Space for 0.6s to skip. Pause and cutscenes do not advance run time or combat damage.
- Commander locks a 2.5m-radius blast warning for 1.8s, fires a simulated 18m/s rocket, then repositions during recovery. Crouching does not avoid explosion damage; leaving the area or using solid cover does.
- Victory credits one RPG, +20 extra supply (normal wave survival award is separate), and one free selection from launcher-specific upgrades. Four-slot inventory replaces the held gun with an explicit reward message; no fifth slot or duplicate reward.
- RPG base damage 140, single-round capacity, 4s base reload. Three capped upgrade branches: +25% damage, -15% reload time, +0.5m radius per level, max three levels each. Reload/damage global modifiers still apply. RPG is unique to the boss reward, not rolled in the regular shop.
- Boss/player two-arm launcher fit, first-person launcher display, health bar, red blast warning, camera shots, launch/explosion sound and smoke.
- F9 starts a cheat-marked wave-5 test run (including from menu); F6 toggles god mode; F10 adds 1,000 supply. F7 does not skip an active commander. Weapon selection remains scroll/Q/number keys.
- Existing six-wave campaign is retained; M113, ten waves, airstrikes and full speedrun records are the later campaign work, not claimed by this deliverable.

## Verification ledger

- Asset tests: 2/2 pass before the first commit.
- Reward tests: failed before reward API, then 3/3 pass; inventory-full and upgrade-cap tests added and included in the 66/66 EditMode pass.
- Encounter tests: failed before encounter, then 3/3 pass before combat commit.
- Review regressions: auto-aim rocket outside forward ray and late camera acquisition failed first, fixed and passed. Cursor processing stays enabled through cinematic camera ownership.
- Blast warning test failed when shared presentation pulsed its diameter down to 1.85m; commander warning now retains 5m diameter. Full six-wave lifecycle test now explicitly defeats the commander before proceeding.
- 66/66 EditMode; 56/56 selected PlayMode regression tests (commander, sniper, lifecycle, manual aim, interface).
- Static whole-branch review: two reported correctness issues resolved; follow-up found no further important defects.
- UI composition screenshots captured and inspected in Builds/CommanderReview. Synthetic voice is a placeholder performance, replaceable via the two CommanderVoice WAV files.
