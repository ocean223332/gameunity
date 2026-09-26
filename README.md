# Tiền Tuyến

A top-down survival shooter inspired by Brotato's gameplay loop, set at a fictional supply station in Trường Sơn in 1972. The player character is a soldier in the People's Army of Vietnam; this is not a recreation of an actual battle.

## Confirmed configuration

- Unity **6000.3.24f1**, Universal 3D template, URP.
- Windows, single-player, offline; no backend or Unity Cloud.
- Public repository: [ocean223332/gameunity](https://github.com/ocean223332/gameunity), containing the documentation and Unity project.
- The P2 combat slice runs in the dedicated `CombatSpike.unity` scene: 6 survival waves, 3 weapons, 3 regular enemy types (including chargers unlocked from wave 2) and an elite enemy, a wave 3 supply crate, resource pickups, pause/restart, end-of-wave upgrades, and a 4-offer shop. `ProgressionRun`/`ShopSession` manage currency, XP, passives, upgrades, rerolls, locks, buying/selling/merging, and up to 4 weapon slots.

## Open the project

1. In Unity Hub, choose **Add project from disk** and select the `TienTuyen` folder next to this README. Do not select its parent folder, `gameunity`.
2. Open it with Unity **6000.3.24f1** and wait for asset importing and package resolution to finish. The game runs offline; initial setup/import may require an internet connection to download Unity packages.
3. Open `Assets/_TienTuyen/Scenes/CombatSpike.unity`. Press Play, choose Rifle or SMG, and start. Move with WASD or the arrow keys; the character aims and fires automatically. Press `Esc` to pause.

Git tracks `Assets` with all `.meta` files, `Packages/manifest.json`, `Packages/packages-lock.json`, and `ProjectSettings`. Caches, logs, IDE files, and build outputs are not stored in Git.

## Foundation verification

- Import and compilation: passed with Unity 6000.3.24f1.
- `CombatSpike.unity`: saved successfully; the camera output was checked in Play Mode with the arena, cover, player, enemies, and pickups visible.
- Automated tests after the animation and weapon-placement fixes: **57/57 Edit Mode** (including 4 Foundation tests) and **24/24 Play Mode** passed.
- Low-poly characters have code-driven joint animation for idle/running, aiming, two-handed weapon grips, recoil, and reloading. Geometry checks cover weapon/body clipping for Rifle/SMG/Shotgun.
- Windows x64 Mono: the P2 build succeeded at `Builds/Windows/TienTuyen.exe`, 105,848,797 bytes, 14.9 seconds, 0 errors and 1 warning. The warning only indicates that the Pipeline runtime is disabled in the Player build.
- Integrity check: **271 files**, 0 errors, 0 warnings, and 0 unchecked items after completing the assets/settings.
- Git: branch `main`; caches, builds, `TestResults/`, and `video_output/` are not pushed to the remote.

For details, limitations, and tooling warnings, see [SETUP_STATUS](docs/SETUP_STATUS.md).

## Run tests and rebuild

- In Unity, open **Window → General → Test Runner → EditMode/PlayMode** and run the `TienTuyen.Foundation.Tests`, `TienTuyen.Combat.Editor.Tests`, and `TienTuyen.Combat.Play.Tests` assemblies.
- **Tien Tuyen → Combat → Create Scene** creates the combat scene once and deliberately refuses to overwrite an existing scene. **Tien Tuyen → Combat → Build Windows (Queued)** or **Tien Tuyen → Foundation → Build Windows** creates `Builds/Windows/TienTuyen.exe` in the repository directory. Close any running instance of the `.exe` before rebuilding.
- Do not rerun **Create Baseline** on an existing scene: the tool deliberately refuses to overwrite it. Make subsequent scene changes in the Editor, not by editing YAML manually.
- The runtime UI uses uGUI + TextMeshPro for the weapon-selection menu, health/wave/time/XP/currency/ammo HUD, pause menu, and results screen. Cinemachine is not needed for the fixed camera yet.

Building and compiling do not replace a full 6-wave playtest. Automated lifecycle tests and build-artifact checks are available; balancing, a complete human playthrough, profiling, and P3 systems have not been declared complete. P2 now includes all three regular enemy archetypes and the elite; a representative art/audio pass and a full human playthrough remain open items.

## Documentation

- [SPEC — gameplay and acceptance criteria](docs/SPEC.md)
- [PLAN — milestones and architecture](docs/PLAN.md)
- [ART_DIRECTION — style and assets](docs/ART_DIRECTION.md)

The design documents describe the target experience; features are only considered available once implemented and verified. The confirmed configuration above supersedes the corresponding "undecided" notes in earlier proposal documents.
