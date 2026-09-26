# Tiền Tuyến — Unity and P2 combat slice status

## Implemented

- The user approved the existing Unity installation, URP, and offline Windows; later, they requested publishing the source code to GitHub at `ocean223332/gameunity`.
- Unity project: `../TienTuyen`, Editor **6000.3.24f1**, template `com.unity.template.urp-blank` (Universal 3D).
- URP **17.3.0**, Input System **1.20.0**, Test Framework **1.6.0**, and uGUI **2.0.0** came with the template; Pipeline **0.7.0-exp.1** was added via CLI to control the local Editor.
- The template's auxiliary packages remain unchanged; AI Navigation, Multiplayer Center, Visual Scripting, and Timeline have not been configured as features. No IAP, ads, or backend were added.
- The baseline scene, `Assets/_TienTuyen/Scenes/Bootstrap.unity`, remains unchanged for project validation.
- Playable scene, `Assets/_TienTuyen/Scenes/CombatSpike.unity`: a top-down orthographic arena with cover, a player, an enemy pool, projectile/pickup pools, spawn warnings, and URP lighting. Only the combat scene is enabled in Build Settings.
- PC quality, Linear color, HDR, directional light/soft shadows; Global Volume with ACES, Bloom 0.5/threshold 0.9, and Vignette 0.15. Materials use the URP/Lit shader.
- Windows64 Mono, 1280×720 windowed; ForceText, Visible Meta Files. CombatSpike is the only scene enabled in build settings.
- Runtime UI: uGUI + TextMeshPro, Rifle/SMG menu, health/wave/time/XP/currency/ammo/reload HUD, pause, defeat/victory, and restart/menu.
- Current combat slice: 6 waves (waves 5–6 last 50 seconds), auto-firing and auto-reloading Rifle/SMG/Shotgun, infantry/shooter/charger/elite with telegraphs, cover/LOS, grid-based movement, currency/XP pickups, pause/death/restart, wave settlement, and a wave 3 supply crate (20 supplies + 10 HP healing). Chargers unlock from wave 2, telegraph their lane, then dash in the locked direction.
- Pure C# P2 modules integrated into combat: `ProgressionRun`/`ShopSession` for 4 slots, passive stacks, pending upgrades, pricing/rerolls, locks, buying/selling/merging, and bandaging; `PassiveCatalog` has 6 stable IDs, I01–I06; `ContentCatalog` has 3 weapons, 3 regular enemies, and 1 elite; `SupplyEvent` handles progress/idempotent claiming. Presentation includes Vietnamese upgrade and shop screens, before/after previews, and transaction actions.

## Verification evidence

Animation update, 2026-09-27: **57/57 Edit Mode** and **24/24 Play Mode** tests passed. Added articulated low-poly animation, two-handed weapon handling, recoil/reload, muzzle flashes/casings, and geometry checks for all three guns against clothing/armor. Reviewed close-ups from two angles in idle, running, firing, and reloading poses. The build figures in the table below refer to the P2 build before the animation update, not confirmation of a new build. New test logs and images are stored locally in `TestResults/`.

| Check | Result |
|---|---|
| Script compile | Completed after importing TMP and combat assemblies; no new compile errors |
| Foundation validator | Passed; the build scene points to CombatSpike |
| Combat Edit Mode tests | **53/53 passed**: combat rules, P2 catalog, charger eligibility/stats, progression transactions/caps, passive catalog, idempotent rewards, and supply lifecycle |
| Combat Play Mode tests | **11/11 passed**: full health/start, P2 Shotgun, pause clock, invulnerability, defeat reward, charger telegraph/dash, charger reward idempotency, full 6-wave upgrade/shop→Victory flow, upgrade→shop flow, shop transactions, and 20 restarts/pool resets |
| Foundation Edit Mode tests | **4/4 passed** |
| Edit/Play render | Reviewed the actual camera in Play Mode: arena, cover, player, enemies, and pickups rendered correctly; the Vietnamese menu was checked |
| Integrity CLI | **271 files**, 0 errors, 0 warnings, 0 uncheckable items |
| Windows build | P2 build succeeded; `Builds/Windows/TienTuyen.exe`, 105,848,797 bytes, 14.9 seconds, 0 errors, 1 warning (Pipeline runtime disabled in Player) |
| Standalone smoke | Launched the Windows executable; the `Tiền Tuyến` window appeared, the start key advanced through the menu/play flow, and Alt+F4 closed it |
| Network configuration | Cloud link empty, Unity Connect/ads/analytics disabled; Pipeline `enableInBuilds=false`, `autoStart=false`; no process TCP sockets observed at the time of inspection |

Images: [Edit Mode](art/unity-baseline-edit.png), [Play Mode](art/unity-baseline-play.png). These are actual Unity images, not the previously generated concept.

Initial reference machine: Intel i7-12700H, 16 GB RAM, RTX3060 Laptop GPU with 6 GB VRAM; FPS/gameplay has not been benchmarked. Background startup does not establish keyboard interaction, gameplay, or completely offline operation in every situation.

## Tool notes

- CLI beta8 does not accept `--caller`/`--skill` on eval commands even though the newer skill describes them; the queried, actual schema was used.
- The first build through synchronous eval exceeded Pipeline's 5-second timeout but still completed; the tool timeout error appeared in the initial report. The build was rerun through an Editor update callback, returning a successful 3.2-second log.
- The Pipeline build warning confirms that the runtime bridge is disabled, which is the intended state. The package still contains an internal runtime assembly; this does not claim the entire package is excluded from Player.
- The Editor reported a Hub IPC timeout warning; it did not block compilation/rendering/tests/builds during this run. Player logged a D3D12 info-queue warning but continued initializing the GPU and loading the scene.
- The initial Edit Mode screenshot was taken too early and showed only the clear color; it was retaken after rendering updated and then verified.
- A clean checkout and direct standalone UI testing have not yet been performed. Build outputs/logs/caches are excluded from history by .gitignore.

## P2 limitations and next steps

The slice still uses graybox primitives and URP materials, not final art. Multiple character classes, complete audio, and stress profiling are not yet available. The P2 enemy roster now includes all three regular archetypes plus an elite; a full human playthrough of 6 waves remains outstanding. TMP warns about some Vietnamese characters missing from the base glyph set and uses fallback/dynamic glyphs as needed; a font with full support must be finalized before P2 is complete.

The next step is to finish P2 validation: one representative, fully finished art/audio section, complete Vietnamese font coverage, and then a full 6-wave playthrough on the Windows build.
