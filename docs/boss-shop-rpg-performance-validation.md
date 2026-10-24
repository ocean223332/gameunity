# Boss/shop/RPG validation and graphics measurements — 2026-10-23

Base: main a4b694c. Branch: codex/boss-shop-rpg-60fps. User selected a 60 FPS target and lower GPU load.

## Delivered behavior

- Boss escorts: at most two active or pending infantry; first warning after six seconds, subsequent warnings at least twelve seconds apart. Full escorts reset the replacement delay. Other waves retain their existing squads.
- Shop rerolls cost two supply, retain locked offers and refill purchased slots. Earning the boss RPG unlocks RPG offers for the current run, including after selling it. Existing manual equal-tier merges reach tier III; actual rocket damage applies tier and upgrade factors. Offer and upgrade previews include the relevant damage/reload modifiers.
- Rocket launch flame, flame/smoke trail, fire/sparks/smoke impacts and fading ground scorches use bounded pools and shared materials. Restart/menu clear them.
- Release players target 60 FPS while focused and 15 while unfocused, across scenes. Development benchmarks explicitly own their frame budget. PC render scale is 0.85 with STP retained; short-plant shadow reach is eight metres per metre of plant height. Trees keep shadows within the existing pipeline shadow distance.

## Verification

- Complete EditMode: 122/122 passed (`Builds/boss-shop-final-edit.xml`).
- Affected PlayMode integration: 48/48 passed (`Builds/boss-shop-green-play-v2.xml`), covering boss pacing, actual merged rocket damage, effects pools/cleanup, shop/merge ammo, boss encounter/hitboxes, viewmodel lifecycle, audio and combat lifecycle.
- Final graphics/shop/viewmodel verification after tuning and preview fix: 10/10 passed (`Builds/boss-shop-final-play.xml`). This includes all three new preview cases, which first failed on the old formula (`Builds/rpg-preview-red.xml`).
- Inspected shop, launch, impact and scorch captures under `Builds/boss-shop-rpg-*.png`; corrected the fixture trajectory so the hero cannot obscure the effects. Inspected final forest and crowded-combat images at 1080p.
- An older shouldered-face/stock alignment failure was confirmed on the pre-change source in the previous task; that unrelated full-suite case was not rerun or changed here. These counts describe the stated suites, not every PlayMode test in the repository.

## Measurement method and limits

RTX 3060 Laptop GPU, Direct3D11, 1920x1080, PC quality, Misty lighting, seed 7319. Four scenes, each with 60 warmup and 600 measured frames. The crowded scene uses normal wave six, twelve or more actors (thirteen in these runs), actual simulation and firing, and no boss cinematic/tutorial pause.

All accepted runs used a hidden Windows player. Hidden back-buffer and automatic-target rendering were blank on this machine. `-offscreen` therefore manually renders the enabled main camera to an HDR target and waits for a one-pixel GPU readback each frame. Keeping it enabled preserves Camera.main consumers, including instanced flora. This serializes CPU/GPU work and adds synchronization overhead: the FPS below is completed offscreen diagnostic throughput, not a native visible-player presentation benchmark. Do not use this mode in a visible/non-minimized window, where automatic rendering can add a second render. Use the ordinary benchmark path for visible-player measurements.

Native GPU frame counters remained zero despite enabled frame timing; reports mark those GPU times unavailable (NaN), not free. Independent nvidia-smi telemetry was sampled once per second and filtered to each scene's UTC measurement window. Reported utilization is GPU-wide, not process-isolated; small power/utilization differences should be interpreted cautiously. Screenshot readback/PNG encoding happens outside measured intervals; the one-pixel completion wait is deliberately inside.

| Scene | Baseline median FPS | Final median FPS | Baseline p99 frame ms | Final p99 frame ms | Baseline GPU mean | Final GPU mean |
|---|---:|---:|---:|---:|---:|---:|
| Valley | 52.7 | 60.0 | 21.02 | 17.03 | 68.7% | 61.5% |
| Forest edge | 51.7 | 60.0 | 22.92 | 20.43 | 65.6% | 61.8% |
| Crowded combat | 49.3 | 57.1 | 24.67 | 20.45 | 62.4% | 59.4% |
| Enemy in grass | 52.8 | 60.0 | 21.79 | 18.56 | 63.2% | 58.2% |

Final 1% low FPS: 58.7 / 48.9 / 48.9 / 53.9. The heavy scene and tails still fall below 60; this work does not establish that all stutters or 100% GPU cases are eliminated. Across the measured windows, mean GPU utilization was about 65.0% before and 60.3% after, with roughly 98.6 W versus 96.8 W. The cap prevents unnecessary frames in normal play; internal 1632x918 rendering reduces shaded pixel count by 27.75% relative to full 1080p. It does not imply total GPU work decreases by that same percentage.

Single-variable checks: shadow reach eight alone gave median FPS 54.3/53.3/50.1/54.4; scale 0.85 alone gave 60.2/58.2/55.2/59.0. The combined, capped setting was selected after these checks. Images retained the scene's vegetation, fog and lighting.

Accepted artifacts (gitignored):
- `output/perf-20261023-validated-uncapped`
- `output/perf-20261023-shadowreach8`
- `output/perf-20261023-renderscale85`
- `output/perf-20261023-capped-final`

Each contains benchmark.csv, raw frame CSVs, screenshots, GPU telemetry and UTC measurement windows. Earlier baseline-misty, baseline-normal, offscreen-uncapped and auto-target-uncapped attempts are invalid and excluded (blank targets, missing vegetation or boss-camera/workload interference).

Reproduce with a Development benchmark build from `TienTuyen.Editor.BenchmarkBuild.Build`, then launch hidden (PowerShell `Start-Process -WindowStyle Hidden`) using:

```text
TienTuyen.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -force-d3d11 -force-device-index 0 -lighting Misty -seed 7319 -offscreen -no-clips -benchmark ABSOLUTE_OUTPUT -logFile ABSOLUTE_LOG
```

For the pre-tuning baseline add `-renderscale 1 -shadowperheight 0`; for the selected capped setting add `-renderscale 0.85 -shadowperheight 8 -fps 60`. Build helper restores both the prior frame-timing API setting and the original ProjectSettings file bytes. No real-time two-client duel performance benchmark was performed; the frame policy is scene-independent and existing duel simulation tests are included in EditMode.

Windows release build succeeded: `Builds/Windows/TienTuyen.exe`, 43.3 seconds, 0 errors, 6 warnings. `TienTuyen/Logs/CombatBuildReport.txt` finished UTC 2026-10-23T07:43:21.4961912Z; Combat/Presentation/Progression managed assemblies were freshly written.

## Larger explosion visual follow-up

User requested more spectacular RPG explosions after the initial performance work. The impact now combines a 0.13-second central flash, eighteen larger fire puffs with yellow/orange/cooling-red color progression, thirty-two sparks, fourteen textured smoke puffs and eighteen outward ground-dust puffs. Airbursts omit ground dust. A shared 128x128 procedural cloud texture replaces uniform circular smoke/fire sprites. Smoke appears after the hot fire phase. Impact lifetime is bounded at 2.8 seconds; eight impact slots, shared materials, restart/menu clearing and no extra damaging colliders remain verified.

Two new ground/air regression cases first failed because flash/dust layers were absent. All ten effects/graphics cases subsequently passed (`Builds/rpg-big-explosion-final.xml`); final color timing passed another real-graphics capture (`Builds/rpg-big-explosion-graphics.xml`). Inspected flash, fire, smoke and scorch images, including `Builds/rpg-big-fire.png` and `Builds/rpg-big-smoke.png`. Windows build succeeded with zero errors and six warnings, 22.8 seconds, finished UTC 2026-10-23T08:00:49Z.

The performance table above predates this larger visual effect and does not quantify its additional transient GPU cost. Pools, particle counts and shared assets remain bounded; no shadowed dynamic lights were added. The 60/15 FPS policy and previous render settings remain in place.
