# Tiền Tuyến

A third-person (over-the-shoulder) survival shooter inspired by Brotato's wave-and-shop loop, set at a fictional supply station in Trường Sơn in 1972. The player character is a soldier in the People's Army of Vietnam; this is not a recreation of an actual battle.

## Confirmed configuration

- Unity **6000.3.24f1**, Universal 3D template, URP.
- Windows, single-player, offline; no backend or Unity Cloud.
- Public repository: [ocean223332/gameunity](https://github.com/ocean223332/gameunity), containing the documentation and Unity project.
- The P2 combat slice runs in the dedicated `CombatSpike.unity` scene: 6 survival waves, 3 weapons, 3 regular enemy types (including chargers unlocked from wave 2) and an elite enemy, a wave 3 supply crate, resource pickups, pause/restart, end-of-wave upgrades, and a 4-offer shop. `ProgressionRun`/`ShopSession` manage currency, XP, passives, upgrades, rerolls, locks, buying/selling/merging, and up to 4 weapon slots.

## Open the project

1. In Unity Hub, choose **Add project from disk** and select the `TienTuyen` folder next to this README. Do not select its parent folder, `gameunity`.
2. Open it with Unity **6000.3.24f1** and wait for asset importing and package resolution to finish. The game runs offline; initial setup/import may require an internet connection to download Unity packages.
3. Open `Assets/_TienTuyen/Scenes/CombatSpike.unity`. Press Play, choose a weapon, and start. Move with WASD relative to the camera, look and aim with the mouse, hold the left mouse button to fire, hold the right mouse button to aim down the shoulder, and press `R` to reload. A gamepad also works (left stick, right stick, triggers). Press `Esc` to pause.

Git tracks `Assets` with all `.meta` files, `Packages/manifest.json`, `Packages/packages-lock.json`, and `ProjectSettings`. Caches, logs, IDE files, and build outputs are not stored in Git.

## Foundation verification

- Import and compilation: passed with Unity 6000.3.24f1.
- `CombatSpike.unity`: saved successfully; the camera output was checked in Play Mode with the arena, cover, player, enemies, and pickups visible.
- Automated tests after the faction flags (2026-10-01): **57/57 Edit Mode** (including 4 Foundation tests) and **34/34 Play Mode** passed, including manual-aim tests (trigger, crosshair hit, miss, cover blocking, reload, camera-relative movement, perspective camera) and interface tests (baked Vietnamese fonts, generated glyphs, screens following the run state) and the flag test (Vietnamese banner on the hero that waves, US banner on every enemy model).
- Low-poly characters have code-driven joint animation for idle/running, aiming, two-handed weapon grips, recoil, and reloading. Geometry checks cover weapon/body clipping for Rifle/SMG/Shotgun.
- Windows x64 Mono: the P2 build succeeded at `Builds/Windows/TienTuyen.exe`, 105,848,797 bytes, 14.9 seconds, 0 errors and 1 warning. The warning only indicates that the Pipeline runtime is disabled in the Player build.
- Integrity check: **271 files**, 0 errors, 0 warnings, and 0 unchecked items after completing the assets/settings.
- Git: branch `main`; caches, builds, `TestResults/`, and `video_output/` are not pushed to the remote.

For details, limitations, and tooling warnings, see [SETUP_STATUS](docs/SETUP_STATUS.md).

## Run tests and rebuild

- In Unity, open **Window → General → Test Runner → EditMode/PlayMode** and run the `TienTuyen.Foundation.Tests`, `TienTuyen.Combat.Editor.Tests`, and `TienTuyen.Combat.Play.Tests` assemblies.
- **Tien Tuyen → Combat → Create Scene** creates the combat scene once and deliberately refuses to overwrite an existing scene. **Tien Tuyen → Combat → Build Windows (Queued)** or **Tien Tuyen → Foundation → Build Windows** creates `Builds/Windows/TienTuyen.exe` in the repository directory. Close any running instance of the `.exe` before rebuilding.
- Do not rerun **Create Baseline** on an existing scene: the tool deliberately refuses to overwrite it. Make subsequent scene changes in the Editor, not by editing YAML manually.
- The runtime UI uses uGUI + TextMeshPro, built in code under `Runtime/Presentation/Interface`: `CombatPresentation` only switches screens (`MenuView`, `HudView`, `UpgradeView`, `ShopView`, `RunDialogView`); `UiTheme`, `UiKit`, `UiButton`, `UiSprites` and `UiGlyphs` hold the palette, builders, animated buttons, rounded panels and vector weapon/item icons. Fonts are Oswald Bold and Source Sans Pro Semibold (SIL OFL, see `Art/Fonts/OFL.txt`), baked with full Vietnamese coverage by **Tien Tuyen → Combat → Rebuild Interface Fonts** (the Windows build creates them automatically when missing). The third-person camera is a small custom controller (`CombatThirdPersonCamera`), so Cinemachine is not required.

Building and compiling do not replace a full 6-wave playtest. Automated lifecycle tests and build-artifact checks are available; balancing, a complete human playthrough, profiling, and P3 systems have not been declared complete. P2 now includes all three regular enemy archetypes and the elite; a representative art/audio pass and a full human playthrough remain open items.

## Documentation

- [SPEC — gameplay and acceptance criteria](docs/SPEC.md)
- [PLAN — milestones and architecture](docs/PLAN.md)
- [ART_DIRECTION — style and assets](docs/ART_DIRECTION.md)
- [CREDITS — third-party and derived assets](docs/CREDITS.md)

### Audio provenance

The approved combat SFX are stored in `TienTuyen/Assets/_TienTuyen/Audio/Resources/CombatAudio`.
Valve's AK47, MP5-SD and XM1014 recordings and the ground footsteps come from the
pinned [sourcesounds/csgo mirror](https://github.com/sourcesounds/csgo/tree/08f1bd6835d4f510d2ccaedeab6bb9f637b388ab).
Reload, draw, knife and shell contacts come from the [RAGNA SS GameBanana pack
552059](https://gamebanana.com/mods/552059). Both sets contain Valve proprietary
originals; their credits and the pack's licence note are recorded in
[CREDITS](docs/CREDITS.md) and [Audio/Sources.md](TienTuyen/Assets/_TienTuyen/Audio/Sources.md).
The remaining UI, impact, vocal, swish and cicada recordings are CC0 from Kenney,
TinyWorlds, artisticdude and syncopika.

The menu plays **Adagio for Strings, Op. 11** by Samuel Barber (1936), performed by
the Vancouver Symphony Orchestra, conducted by Georges Delerue, from the *Platoon*
soundtrack (Atlantic, 1986; orchestra credit from the album, the video names only
Delerue). Source: [YouTube](https://www.youtube.com/watch?v=hqVFWlZcnUg), uploaded
2023-03-19 by [서해지](https://www.youtube.com/channel/UC92UvwU5ZlP3M__GQRSuMHg).
The composition and recording are copyrighted; the fan upload grants no licence.

Starting a run crossfades to **Constance** (Kevin MacLeod, 2011, ISRC USUAN1100850)
at the existing quieter in-game level; returning to the menu crossfades back.
Both tracks use edited internal loops. Constance is sourced from
[incompetech.com](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1100850)
under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/):

> "Constance" Kevin MacLeod (incompetech.com)
>
> Licensed under Creative Commons: By Attribution 4.0 License
>
> http://creativecommons.org/licenses/by/4.0/

The design documents describe the target experience; features are only considered available once implemented and verified. The confirmed configuration above supersedes the corresponding "undecided" notes in earlier proposal documents.

## Sniper enemy and local test controls

- The sniper reuses the existing shooter soldier model and animation, carrying the existing rifle model with a scope and extended barrel. It replaces the pistol enemy.
- It spawns rarely from wave 2 (weight 1), with at most two active/announced at once. It retreats inside 8 m, approaches when beyond 10 m, and can aim/fire out to 14 m. The 8 m inner firing boundary keeps it usable along the arena's shorter edge.
- Its red warning locks direction for 1.5 seconds before a single shot. Hold C or Left Ctrl to crouch under the shot; movement and cover also avoid it. Its first encounter each run pauses for a short Vietnamese tutorial.
- F6 toggles invulnerability; F7 skips a wave without rewards; F8 announces a sniper from wave 2, respecting its cap. Restart resets test mode and the tutorial.
