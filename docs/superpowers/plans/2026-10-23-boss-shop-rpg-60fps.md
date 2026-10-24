# Boss, Shop, RPG and 60 FPS Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans. Parent coordinates all Unity execution; independent file owners prepare tests first and never run competing Unity processes.

**Goal:** Deliver calmer boss combat, affordable repeat shopping and RPG upgrades, readable pooled rocket effects, and measured60 FPS optimization.
**Architecture:** CombatGame owns spawning/damage; pure progression classes own transactions/unlocks; existing ShopView owns shop display; a focused rocket-effects component owns reusable visuals; frame-budget policy owns runtime FPS settings. Preserve the existing domain boundaries.
**Tech Stack:** Unity6000.3.24f1, C#, URP17, uGUI/TMP, NUnit Unity Test Framework, existing GraphicsBenchmark.
**Spec:** docs/superpowers/specs/2026-10-23-boss-shop-rpg-performance-design.md

## Global Constraints
- Follow the linked design's exact tuning defaults; user selected60 FPS.
- No concurrent Unity test/build runs or GPU measurements; parent schedules them.
- Preserve other agents' edits and existing branch contents; no automatic push.
- Test first, observe expected failures, implement narrowly, rerun affected tests.

## Review Focus
- Pending spawn warnings must count toward the two-support cap.
- All locked offers/no funds must preserve shop and currency atomically.
- Selling RPG retains unlock; a new run removes it; buying/merging does not give free ammunition.
- Reset/menu clears trails/explosions/scorches and stops further emission.
- Benchmark overrides must not leave normal play uncapped; isolated profiling must not confuse startup/recording hitches with gameplay.

## Task1 — Boss pacing and RPG tier damage (parent)
Files: Runtime/Combat/CombatGame.cs, CombatGame.Commander.cs; new Tests/Combat/PlayMode/BossPacingTests.cs and RocketTierDamageTests.cs.
- [x] Add tests: at most2 infantry active+pending excluding boss over long simulation, no elite/sniper/charger, first spawn>=6s and subsequent>=12s, pause/cinematic do not accumulate burst, later normal waves unaffected.
- [x] Add projectile-path damage tests for RPG tiersI/II/III; verify rocket/general modifier composition through preview regression tests and source review.
- [x] Run focused PlayMode tests red; implement branch-local boss support selection/cadence and tier factor in FireLauncher; rerun green.
Interfaces: keep existing event and progression APIs. Other agents do not edit CombatGame shared files.

## Task2 — Cheap shop and earnable RPG (shop owner)
Files: Runtime/Progression/ProgressionRules.cs, ShopSession.cs, ShopOfferGenerator.cs; Runtime/Content/ContentCatalog.cs; Runtime/Presentation/Interface/ShopView.cs and CombatText.cs; Tests/Combat/Editor/ProgressionTests.cs, CommanderRewardTests.cs; dedicated shop integration tests.
- [x] Write exact price2 and atomic reroll/lock/refill tests; unlock-after-reward, sold-RPG and new-run cases; RPG mergeI→II→III and cap; wave5 RPG price30/60.
- [x] Parent runs EditMode red. Set fixed reroll price, gate RPG pool by reward, set RPG base24, retain existing merge and slot semantics, improve existing reroll label and RPG statistics.
- [x] Parent runs EditMode green and shop integration tests, captures shop UI.
Interfaces: CommanderRewardClaimed is the unlock; parent owns actual launcher tier damage; no duplicate reset button.

## Task3 — Pooled rocket VFX (effects owner)
Files: new Runtime/Presentation/RocketEffects.cs and meta; CommanderPresentation.cs only old Explosion/Smoke event path and related transient cleanup; new PlayMode/RocketEffectsTests.cs.
- [x] Tests must drive actual player/boss rocket launch/explosion and inspect effects, pool8 explosions/12scorches, trail emission and reset cleanup.
- [x] Parent runs red; implement shared-material pooled flame/smoke/sparks/scorch visuals, replace old allocate/destroy effects, keep existing sound/cinematics/FP weapon placement.
- [x] Parent runs green and graphics captures for launch and impact.
Interfaces: consume existing rocket events; parent can add a typed presentation-only spawn event if no real projectile seam exists. Agree signature before changing shared CombatGame.

## Task4 — Measure and implement60 FPS budget (performance owner)
Files: GraphicsBenchmark.cs only as required to validate measurements; focused runtime frame-policy component and test; URP/vegetation tuning only after evidence.
- [x] Build existing development benchmark and record baseline. Pin1920x1080/lighting and separate ablation from clean timing runs. Check timing availability and processGPU.
- [x] Add failing policy tests for normal60/unfocused15/benchmarkuncapped, then implement. Keep benchmark invocations distinct from gameplay acceptance runs.
- [x] Measure plant-shadow reach and render scale independently. Apply measured visual-preserving tunings with before/after timing/GPU evidence, including dropped-frame/percentile limits.
- [x] Verify settings restore, both camera modes and scene-independent frame policy; capture images and document commands/results, including the lack of a live two-client duel performance benchmark.

## Final Integration
- [x] Review combined diff and tests with a fresh reviewer.
- [x] Run complete EditMode suite and affected PlayMode integration tests, graphics QA, and Windows build.
- [x] Check diff whitespace and metadata, retain benchmark artifacts under ignored outputs, report exact passing counts/known unrelated failures and measured performance limitations.
Execution evidence and remaining measurement limitations: docs/boss-shop-rpg-performance-validation.md. Windows release build succeeded with 0 errors and 6 warnings; report finished UTC 2026-10-23T07:43:21Z.
