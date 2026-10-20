# Wave-5 commander + RPG18 execution ledger

User approved implementation 2026-10-20, on feature branch with incremental commits. Scope is the six steps in the immediately preceding message: weapon fit, telegraphed RPG attack, wave-5 boss, health bar and retreat cutscene, weapon reward/upgrades, test cheat and Windows build. Full 10-wave/M113 campaign remains separate.

## Commits / verification gates
1. Commit previously verified asset foundation (22-bone Humanoid, 24k-triangle RPG, isolated review scene); rerun asset tests.
2. Add deterministic boss encounter and launcher reward/upgrade rules with failing-then-passing tests. Wave 5 cannot finish on its survival timer while boss remains alive; reward exactly once, even with full weapon inventory.
3. Integrate gameplay: intro, RPG telegraph/projectile/explosion, movement with cover collision, retreat, reward and test entry. Existing guns, knife, restart, pause and death must remain correct.
4. Fit boss/player weapon, HUD, cutscene camera/subtitles, effects and RPG upgrade descriptions; preserve existing uGUI style. Cutscenes skip with hold Space, no damage or run time while playing. English subtitles + Vietnamese translation; include spoken audio if an offline voice is available.
5. Integration tests, fresh whole-branch review, fixes, Windows build and evidence. Commit each verified batch. No push requested.

Rulings: RPG is a game interpretation with 1-round capacity and reload, not a simulator of a disposable tube. Upgrades affect damage, reload and blast radius, capped at three each. If all four slots are full, replace the current gun (explicit reward notice) to guarantee the unique reward without exceeding the established slot cap. F9 starts a marked cheat run directly at wave 5; F7 cannot bypass a live boss or fabricate its reward. Existing six-wave campaign is retained for this deliverable.
