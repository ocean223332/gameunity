# Boss pacing, shop, RPG and 60 FPS design

Date: 2026-10-23. Base: main a4b694c. User requested implementation and subsequently said to continue; user selected 60 FPS with reduced GPU load. Remaining small tuning choices below are implementation defaults, not independently user-specified values.

## Intent and constraints
Focus the boss encounter on fighting the boss, allow useful shop purchases after it, make rocket impacts readable, and reduce GPU waste and intermittent stutter. Unity 6000.3.24f1, existing URP/uGUI architecture, Windows, existing weapon/slot/lock systems. Retain world style and both camera modes. No packages or replacement asset pipeline required.

## Chosen approach
Extend the existing encounter, reroll and merge systems. A separate duplicate reset button would conflict with existing reroll/locking semantics; refill-only behavior would not satisfy replacing unwanted items. Use the current button with a cheap full reroll of unlocked slots. Performance uses an explicit 60 FPS budget plus measured render changes; lower GPU usage alone is not evidence that frame drops are fixed.

## Boss pacing
While boss encounter is active, at most two non-boss enemies may be alive OR pending. Only standard infantry may spawn. The first support appears 6 seconds after combat resumes, subsequent spawns at least 12 seconds apart. Cinematics do not advance this timer. Reserve the boss actor's pool entry, avoid deferred enemies bypassing the cap, and keep all other waves unchanged.

## Shop and RPG
Reroll costs exactly 2 supply each time, independent of wave and previous rolls. Preserve locked offers, refill empty purchased slots, allow repeated purchases/rerolls if affordable; disabled or unsuccessful transactions charge nothing.
After CommanderRewardClaimed becomes true, RPG can appear immediately in the boss-exit shop and later rerolls. Unlock lasts only the current run, even if RPG is sold. Set RPG base price to 24: standard pricing gives 30 supply for tier I and 60 for tier II in wave 5. Keep four slots, existing manual merge, equal ID+tier requirements and max tier III. Launcher damage must apply existing weapon tier factors 1/1.35/1.8, multiplied by rocket and general damage upgrades. Keep one rocket per magazine and existing reload/radius upgrades. RPG shop details show actual damage per rocket, reload and blast radius.

## Rocket effects
Add launch/backblast flash, short flame/smoke trail, impact fireball, sparks and smoke, plus a fading scorch on nearby ground. Use shared materials and bounded reusable effects (8 simultaneous impact effects, 12 scorch marks); recycle oldest when full and clear on restart/menu. No extra damaging colliders. Effects work for player and boss, first and third person. Avoid large overlapping transparent spheres and per-impact material allocation.

## Performance
Target 60 FPS at 1920x1080 on RTX 3060 Laptop, reduce background load. Cap gameplay/menu at60 and unfocused at15; benchmarks explicitly bypass cap. Measure pinned scene/lighting/resolution, frame distribution and GPU telemetry before/after. Inspect CPU/render/GPU timing validity instead of trusting zero counters. Prefer reduced short-vegetation shadow reach, appropriate shadow budgets and measured STP/render-scale changes; do not blindly disable scenery or fog. Keep simulation cadence, weapon responsiveness and correct effects. Report actual measured results and remaining limits; 60 FPS cannot be promised from configuration alone.

## Verification
Regression tests for boss cap including pending actors and 12-second spawn cadence; normal wave behavior; reroll exact charge and purchased-slot refill/locks/no funds; RPG locked/unlocked/sold/new-run rules, merge tiers and actual projectile damage; VFX bounded lifetimes/reset;60 FPS policy and benchmark override. Graphics screenshots check shop and RPG impacts. EditMode suite and affected PlayMode cases plus Windows build. Existing shouldered-face alignment test failure was confirmed before this work and remains separately tracked.