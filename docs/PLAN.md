# PLAN — Top-down arena survivor set during the Vietnam War

Status: proposal v0.2, dated 25/09/2026. P0 and most of the P2 vertical slice have been implemented/verified in Unity; the enemy roster now includes all 3 regular archetypes and an elite. Representative art/audio and a full human playthrough of all 6 waves remain outstanding. [SPEC.md](SPEC.md) is the source of gameplay requirements; PLAN describes how to implement and demonstrate those requirements.

Implementation update: the user has approved the existing Unity 6000.3.24f1 + URP installation, offline Windows, and local Git without a remote. The `../TienTuyen` project has been initialized; `CombatSpike.unity` is the current playable P2 scene. Combat now includes 3 regular enemies (infantry, shooter, charger) and an elite; the latest verification status is in README and `SETUP_STATUS.md`, superseding the original plan's statement that no project had been created. Representative art/audio and playtesting still need to be completed before P2 can be closed.

## 1. Scope and decisions to finalize

The goal is a game built around moving, dodging, auto-firing, collecting resources, and choosing a build, inspired by Brotato but with its own visuals, content, and supply mechanics. Do not copy assets, characters, interfaces, or balance data from the reference game.

The following decisions support planning. The user selected the “Vietnamese side” and delegated the choice of period; the working direction is the People's Army of Vietnam in 1972. The other technical choices have not yet been confirmed by the user:

| Decision | Proposal for estimation | When it must be finalized |
| --- | --- | --- |
| Platform | Windows PC, offline, single-player | Before P1 |
| Graphics | Stylized 3D, planar 2.5D gameplay, orthographic camera, URP | Before asset production |
| Editor | Unity 6 LTS; exact patch version TBD, compatibility checked before locking | P0 |
| Controls | Keyboard movement; automatic aiming, firing, and reloading | P0 |
| Setting | People's Army of Vietnam; a fictional supply station in Trường Sơn, 1972 | Working direction established; uniform, equipment, and opposing-force references require approval before final art |
| Scale | One forest arena, 12 waves, 3 character classes, 6 weapon definitions, 18 items, 6 regular enemies + 1 elite + 1 boss | P0 |
| Business model | Undecided; no backend, ads, or IAP in the MVP | After MVP evaluation |
| Performance reference machine | Reference CPU/GPU/RAM and Windows build TBD | P0; do not claim minimum system requirements before measuring |

“People's Army of Vietnam” is the communicated working interpretation of “Vietnamese side” and can be revised if the user intends a different side. The graybox uses role names; real-world content requires source checks and separate approval. P0 adds a 1972 reference sheet for uniforms, firearms, and opposing forces; unsuitable imagery must be removed or replaced. The story of holding a station/waiting for a convoy does not introduce station HP or AI escort mechanics. A switch to 2D/pixel art or mobile-first requires re-estimating art, UI, rendering, and testing before P1.

## 2. Delivery strategy

Critical path: P0 direction approval → P1 3-wave combat spike → P2 6-wave vertical slice → P3 12-wave MVP → P4 testing and handoff. Do not expand content before combat and the shopping loop pass their gates.

| Milestone | Inputs/dependencies | Concrete deliverables | Exit criteria | Estimated person-days |
| --- | --- | --- | --- | --- |
| P0 — Direction approval | Feedback on SPEC/PLAN | Approved brief; engine/platform/UI stack; reference PC; asset and usage-rights inventory; prioritized backlog | No remaining decisions that could force an architectural overhaul or complete art rework | 1–2 |
| P1 — Combat spike | P0 | Graybox arena; 1 class; 2 weapons; 2 enemies; 3 waves; firing/reloading/LOS; resource collection; pause/death/restart; no shop or supply crate yet | **Implemented and verified** through 13 Edit Mode + 5 Play Mode tests, Play Mode camera checks, and a Windows standalone smoke test. A full human 3-wave playthrough and balancing remain open | 4–6 |
| P2 — Vertical slice | P1 passes the combat gate | 6 waves; 1 class; 3 weapons; 6 items; 3 regular enemies and an elite; up to 4 slots; upgrade → shop; one supply event in wave 3; one representative area with finished art/audio | The complete loop from menu through the end of 6 waves works in a Windows build; at least 2 builds with different playstyles; initial captures against the performance budget | 7–10 |
| P3 — Content MVP | P2 passes the loop gate | 12 waves; all 3 classes, 6 weapons, 18 items, 6 regular enemies, elite, and boss; supply in waves 3/6/9; consistent art across the arena; short tutorial; settings and results | All 3 classes can win, lose, and replay; all content actually appears and is usable; no missing required prefabs or sounds | 8–12 |
| P4 — Stabilization and balancing | P3 feature complete | Test suite, bug fixes, accessibility pass, profiling, balancing, internal Windows package, build instructions | Acceptance criteria below are met; no blocking bugs; 3 consecutive complete runs are stable on the reference machine | 5–8 |

Base total: 25–38 person-days, plus a 20–30% contingency of approximately 5–12 person-days → approximately 30–50 person-days. This is a planning estimate, not a deadline commitment. It assumes one experienced Unity developer, simple stylized assets or assets with existing usage rights, and no voice acting or cinematic animation. A person-day measures effort, not a calendar day; custom art and in-depth historical research require separate estimates. Reassess after P1 and P2.

### Work that can run in parallel

- After P0: graybox/combat design and the moodboard + asset usage-rights inventory can proceed independently.
- After the data schema is locked in P1: content authoring, UI presentation, and formula tests can run alongside gameplay work. Assign clear scene/prefab ownership; avoid simultaneous edits to scene YAML.
- When a gameplay interface changes: update the data contract first and integrate through small branches; the integration owner reruns tests and builds before merging.
- Start production of all 18 items only after the complete pipeline for one item has been tested.

## 3. Proposed architecture

Keep C# + MonoBehaviour simple enough to debug. Do not adopt ECS/DOTS, a DI framework, netcode, Addressables, or a backend merely because they are available. Add a dependency only for a measurable need. Use URP from an appropriate template; choose Input System and Test Framework versions compatible with the selected Editor, rather than locking versions to example documentation pages.

Input Actions separate player intent from devices; this organization supports adding gamepad/touch input later. [Unity Input System — Actions](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Actions.html)

| Module | Responsibilities and contract | Must not own |
| --- | --- | --- |
| RunFlow | State machine; run initialization/teardown; wave → upgrade → shop ordering; victory/defeat | Damage formulas, shop prices |
| Player | Input intent, planar movement, health, taking damage, invulnerability according to SPEC | Spawning, inventory UI |
| Combat | Weapon runtime, targeting/LOS, cooldown/reload, projectiles, damage resolver | Direct currency writes or ScriptableObject mutations |
| Enemies | AI roles, movement, attack telegraphs, target registry access | Per-enemy full-scene searches every frame |
| Waves | Wave clock, spawn budget, enemy cap, elite/boss events, and optional objectives | Direct UI activation or duplicate reward grants |
| Progression | XP, upgrade choices, stat aggregation, inventory with 4 weapon slots and passive items | Text display or device input handling |
| Economy | Currency, prices, reroll, buy/sell according to SPEC; atomic transactions | GameObjects or animation |
| Presentation | HUD, menus, shop, audio/VFX, camera feedback | Direct stat or health changes outside valid commands |
| Infrastructure | Pools, clocks, seeded RNG, settings persistence, validation, debug counters | Victory/defeat rules |

Dependency direction: presentation/input sends commands to the runtime; the runtime emits result notifications; UI reads snapshots for display. Formula and transaction logic is standalone C# for fast tests; MonoBehaviour adapts it to scenes, physics, and the Unity lifecycle. Prefer explicit references or per-run bootstrapping over a global singleton that manages everything.

### States and lifecycle

Basic flow: Menu → RunSetup → WaveActive → WaveResolve → UpgradeChoice → Shop → next WaveActive → Victory/Defeat → Results. Pause is an overlay that preserves the previous state and may only apply to permitted states. Final-wave, boss-kill, timeout, and simultaneous player-death branches must follow SPEC's precedence rules and have dedicated tests; callback arrival order must not determine the outcome.

- The gameplay clock stops during pause, shop, upgrades, and results. UI animation may use unscaled time; prevent combat input from passing through menus.
- WaveResolve executes only once: stop spawning/attacks, resolve rewards and remaining pickups according to SPEC, and clean up entities; old coroutines must not fire shots or grant rewards in the next wave.
- Restart creates a new RunState with a new seed or the selected debug seed; release event subscriptions and active pooled objects; restore time scale/input/audio. Settings persist; stats/currency/inventory from the previous run do not.
- The MVP saves only settings and result data required by SPEC; there is no mid-run resume. Do not persist runtime state into design assets.

### Design data and runtime data

ScriptableObjects are suitable for shared definitions; runtime state is created separately for each run and entity. This is a project architecture decision based on Unity's asset-based data storage capabilities; do not use ScriptableObject assets as a save-game mechanism in a standalone Player. [Unity — ScriptableObject](https://docs.unity3d.com/6000.0/Documentation/Manual/class-ScriptableObject.html)

| Design asset | Immutable during play | Separate runtime state |
| --- | --- | --- |
| CharacterDefinition | ID, base stats, trait, starter weapon, presentation refs | Health, stats after modifiers, XP/level |
| WeaponDefinition | ID, attack type, damage, cooldown, magazine, reload, range, projectile/presentation refs | Level/tier, remaining cooldown, rounds in magazine, target |
| ItemDefinition | ID, modifiers, rarity/shop weight, price, stack cap, icon | Owned quantity; modifier instances |
| EnemyDefinition | ID, stats, AI role, attack config, loot, prefab | Health, AI state, attack timer, position |
| WaveDefinition | Duration, spawn composition/budget, max alive, elite/boss/objective config | Elapsed time, spawned count, pending budget, completion flag |
| ArenaDefinition | Bounds, spawn regions, obstacle/spawn rules, scene/prefab refs | Active entities, occupancy/path cache |
| Economy/UpgradeTables | Price/probability curves, eligible choices, stat caps | Currency, shop offers, reroll count, pending level-ups |

Each definition has a stable ID; validators catch duplicate IDs, null references, negative durations/prices, zero total weight, more than 4 slots, and invalid waves. Settings saves use a schema version. JSON saves contain only IDs and necessary values, not direct GameObject/asset references.

### Movement, targeting, and obstacles

- Gameplay uses the XZ plane with consistent 3D colliders/physics; do not mix in Physics2D. Each layer has a purpose: player, enemy, player projectile, enemy projectile, obstacle, pickup.
- P1 verifies character movement + collision sweeps and AI steering through the actual arena. Static obstacles must have navigable routes around them; do not simply steer directly toward the player and accept stuck enemies.
- If detours are unreliable, select one solution after the spike: a shared coarse-grid flow field for crowds or AI Navigation with a path-update budget. Do not build both, and do not add dynamic obstacles/destruction to the MVP.
- The target registry filters by range and enemy type, then checks LOS using layer masks; retarget at a defined cadence instead of having every weapon scan every frame.
- Hitscan/high-speed projectiles must test the traveled segment or use an appropriate raycast to avoid passing through colliders at low framerates. Projectile colliders do not block movement.
- Camera/foliage must not obscure bullet paths or telegraphs; decorative trees must not silently act as cover when their visuals do not communicate it.

### Pooling and performance by design

Pool enemies, projectiles, pickups, and hit VFX; fully reset health/timer/target/velocity/trail/events on reuse. Unity includes `ObjectPool<T>`; gameplay remains responsible for active-entity limits, which must not be inferred from pool size. [Unity — ObjectPool](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.ObjectPool_1.html)

Prewarm based on measured stress scenes; avoid continuous Instantiate/Destroy during combat. Maintain active/pooled/high-water-mark counters and capacity policies: delay enemy spawns, merge pickups, and drop decorative VFX. Do not silently delete damage-dealing projectiles because the pool is exhausted and thereby alter combat outcomes.

## 4. Testing and acceptance criteria

Use Edit Mode for formulas/data validation, Play Mode for lifecycle/physics/state, and standalone Windows for input, UI, builds, and performance. Unity Test Framework supports both Edit Mode and Play Mode, including Play Mode on a Player. Select its package version alongside the Editor in P0. [Unity — Automated tests](https://unity.com/how-to/automated-tests-unity-test-framework)

| Group | Required cases | Pass criteria |
| --- | --- | --- |
| Combat | Damage/armor/crit, fire rate, auto reload, range, weapon tier/slot | Results match SPEC formulas; valid stat caps; no division by zero or negative damage; independent magazine/reload per slot |
| LOS/physics | Player/enemies on opposite sides of cover; high-speed projectiles; 30/60/120 FPS; target dies/loses LOS | No firing through cover except where SPEC permits; no targeting objects returned to the pool; no collider tunneling during a long frame |
| Spawn/pathing | 100 debug seeds; player near boundaries; obstacles surrounding spawn regions; cap reached | No spawning inside colliders or the player's safe zone; alive count stays within cap; failed spawns do not cause infinite loops; a valid route always exists or another position is selected |
| Waves | 12 waves lasting 40/50/60 seconds, in groups of 4; pause mid-wave; final kill and timeout in the same frame | Correct clock, no double resolve; boss/elite follow SPEC's schedule; state transitions occur exactly once |
| Economy | Insufficient funds; double click; reroll; full slots; sell/buy/stack; shop transitions | Nonnegative currency; all-or-nothing transactions; no item duplication; tiers/stacks/prices match SPEC; an offer cannot be purchased twice |
| Upgrade | Multiple level-ups in one wave; options become ineligible; duplicates; exhausted choice pool | Every pending upgrade is resolved exactly once; no softlocks; transparent fallback |
| Supply | Complete/skip in waves 3/6/9; player dies while receiving rewards; timeout coincides with completed progress | Truly optional: skipping does not block the wave; rewards granted once; simultaneous events follow SPEC |
| Pool | 1,000 spawn/despawn cycles; restart while projectiles are in flight; double release | No stale health/targets/trails, event leaks, damage from the previous run, or double releases |
| Pause/restart | Pause during reload/telegraph; exit shop; focus lost; 20 restarts | Consistent timer/gameplay freeze; UI input does not leak into combat; no stuck timeScale=0; no leftover entities/currency in a new run |
| Content | Every class/weapon/item/enemy, prefab references, and Vietnamese text | Complete SPEC catalog; no missing scripts/materials; tooltips match effects; no broken Vietnamese diacritics |
| UX/readability | 1280×720, 1366×768, and 1920×1080; dense combat; screen shake disabled; inability to distinguish red/green | No clipped HUD; hazards use shapes/telegraphs as well as color; players can clearly see themselves and dodge routes |
| Build | Clean checkout/import; tests; Windows build; offline launch; corrupt settings | Reproducible build following README; no exceptions; fallback for corrupt settings; no account/network required to play |

Seeds reproduce spawn/loot/shop RNG for debugging only; they do not promise bit-for-bit deterministic physics simulation. Each bug report records the seed, build version, wave, class/loadout, and reproduction steps.

### Balancing and playtesting

- P1: 3–5 observed play sessions to check whether players understand auto fire/reload and cover; record causes of death, stuck locations, and moments when players lose track of their character. This is early issue discovery, not statistical research. Supply is verified in P2.
- P2: verify at least 2 build directions with observable differences; no single shop choice should always dominate the others. Log damage taken/dealt, income, purchases/rerolls, remaining resources, and time of death locally.
- P3/P4: 5–8 testers if available; each class should be winnable by someone who understands the rules; newcomers should understand the core loop in approximately 2 minutes. Set a win-rate target only after obtaining a sufficiently suitable playtest sample; do not replace game-feel evaluation with bots.
- Economy simulations check the availability of affordable offers after the first wave and in midgame, and rule out infinitely profitable sell/buy loops; tune exact price curves using logs, not isolated impressions.

## 5. Proposed performance budget

The following numbers are provisional engineering targets, not benchmark results or minimum system requirements. Lock the reference PC in P0, check in P1, and document the reasons for subsequent updates.

| Category | Initial target |
| --- | --- |
| Resolution/framerate | 1920×1080, 60 FPS, 16.67 ms frame budget |
| Stress scene | 150 living enemies, 200 simultaneous projectiles, 150 pickups; 4 player weapons; boss + telegraphs; representative arena art. This intentionally tests headroom above SPEC's gameplay cap of 80 enemies and does not change the in-game cap |
| CPU/GPU | Gameplay main thread approximately ≤ 5 ms; total CPU and GPU each ≤ 16.67 ms at p95 during steady combat; measure separately, do not add CPU+GPU |
| Stutter | Release-build p99 frame time ≤ 25 ms at the target stress load; no recurring combat spikes > 50 ms after warm-up |
| Managed allocation | Combat hot path 0 B/frame after warm-up; UI state transitions have bounded allocations that do not grow with replay count |
| Memory | Target Windows working set ≤ 1 GB; no monotonic growth after 20 restarts, comparing snapshots after equivalent warm-up/garbage collection |
| Measurement duration | 30-second warm-up; 120-second stress capture; plus 3 complete 12-wave runs |

Connect the Profiler to a Development Build on the target machine to identify causes; validate final frame times in a non-Development Build because profiling introduces overhead. Save machine specifications, quality level, resolution, build hash, seed, and captures alongside results; Editor FPS is not evidence of passing. [Unity — Profiling on target device](https://docs.unity3d.com/6000.0/Documentation/Manual/profiling-target-device.html), [Unity — Profiler overhead](https://docs.unity3d.com/cn/6000.0/ScriptReference/Profiling.Profiler.html)

If targets are missed: measure the bottleneck → reduce the corresponding cost (AI/LOS cadence, shaders/overdraw/shadows, VFX/audio voices, pickup count) → recheck gameplay. Consider Jobs/ECS only after simpler approaches have been measured and found insufficient; that is a scope change requiring a new estimate.

## 6. Risks and scope-cut priorities

| Risk | Early warning | Mitigation / scope-cut option |
| --- | --- | --- |
| Not fun despite complete content | P1 players only circle around; auto fire creates no decisions | First change telegraphs, open space, enemy roles, and supply; do not try to fix it by adding 20 gun types |
| Cover traps AI | Enemies gather against the same obstacle | Gate pathing in P1, fix layout/algorithms; do not wait until P4 |
| Sensitive or inconsistent setting | Mixed-up uniforms/weapons/periods | Approve the content bible before final art, use fictional labels in the prototype, and verify sources for real-world details |
| Art obscures gameplay | Foliage/VFX hide bullets and telegraphs | Prioritize silhouettes and ground readability; reduce foliage/VFX before increasing overall scene brightness |
| Build combinatorics | Item modifier interactions are difficult to explain or test | Keep an explicit modifier application order; exclude proc chains/recursion and complex active abilities from the MVP |
| Entity-count explosion | FPS drops in the second half of a run | Explicit caps, pooling, pickup merging, early stress measurements |
| Art/content overload | P2 still lacks a reliable asset pipeline | Share skeletons/animations, reduce decorative variants; do not multiply map count |
| Insufficient time | P2 estimate exceeds the budget | Cut cosmetics, postprocessing, and elaborate camera shake first; then propose catalog reductions requiring SPEC reapproval |

Do not independently cut gameplay safety requirements: clear LOS, fair spawning, correct pause/restart, currency duplication prevention, and death feedback. Reducing the 3 classes/6 weapons/18 items or 12 waves is a documented scope change, not grounds to claim completion of the original MVP.

Outside the MVP: multiplayer/co-op, historical campaign, multiple maps, procedural maps, destructible fortifications, finite ammunition requiring loot, persistent skill trees, stat-granting meta progression, controllable vehicles, voice acting, accounts/cloud, ads/IAP, and public release.

## 7. Future mobile/Web evaluation

Do not build or publish mobile/Web versions during the documentation phase, and do not treat them as Windows MVP handoff criteria. Input and UI architecture should not be hardwired to a device, allowing later experiments.

- Touch prototype: left-side virtual stick; retain auto aim/fire/reload; pause button and shop UI must not obscure dodge space; check dead zones, safe areas, text, and tap targets on real devices. Do not add a manual-aim stick or active-skill button beyond SPEC.
- Mobile: select at least one reference device, check heat/battery and FPS after 15 minutes; use separate entity/shadow/VFX budgets rather than extrapolating from Windows.
- Web: check startup/download size, memory, tab focus loss, audio unlock, settings persistence, and input capture in target browsers; do not assume the Windows test suite is sufficient.
- After P2, estimate a port branch only if the user chooses it; select modules/packages and versions based on the platforms available then. Do not automatically implement a backend or hosting.

## 8. Handoff and definition of done

Each milestone includes a corresponding internal executable (once implementation begins), a short changelog, known issues, test results, and clips/captures of new features. The final MVP includes project source + `.meta`, an asset license inventory, a README for opening/building/running, an inventory of actual content, a test report, and a performance report on the reference PC.

A feature is “done” only when it works in a standalone build, passes the relevant acceptance criteria, has a single source of truth for its parameters, preserves pause/restart behavior, and provides sufficiently understandable visual/audio/UI feedback. “Code written” or “no compile errors” is not enough.

Next step after document approval: finalize the platform, graphics style, and content references for the 1972 direction in P0; only then begin project setup and the 3-wave combat spike. There is no need to commit to the entire catalog before the spike demonstrates that the combat loop is worth playing.
