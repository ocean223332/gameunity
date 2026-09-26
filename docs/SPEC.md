# TIỀN TUYẾN — Game specification v0.2

Date: 25/09/2026. Status: **proposal for approval**, not finalized requirements.

Approved implementation decisions: the existing Unity 6000.3.24f1 installation, Universal 3D/URP, offline Windows, and local Git without a remote. The project is located at `../TienTuyen`. The stylized 3D art direction continues to follow ART_DIRECTION; balance numbers remain hypotheses to validate through playtesting.

v0.2 update: the user selected “the Vietnamese side” and delegated the choice of period. The working interpretation is a character serving in the People's Army of Vietnam, with 1972 selected as the year. This interpretation of the force is stated explicitly so the user can revise it; the phrase “the Vietnamese side” is not treated as uniquely identifying one side in the war.

## 1. Product summary

A single-player, top-down survival shooter built around waves of attacks. Players move to evade attacks, their weapons fire automatically, and they collect supplies and develop a build between waves. The player takes the role of a People's Army of Vietnam soldier at a fictional supply station in the Trường Sơn region in 1972; the character, specific unit, station location, and battle are fictional.

Working title: **Tiền Tuyến**; the name has not been checked for commercial availability.

Brotato is a reference for the gameplay loop and equipment synergies, not for copying its characters, interface, visuals, or stat system. According to its [official Steam overview](https://store.steampowered.com/app/1942280/Brotato/), Brotato features auto-firing weapons, short combat waves, and shopping between waves. The numbers below are original design choices for this prototype.

### Existing user requirements

- Unity engine.
- Top-down shooter with mechanics similar to Brotato.
- Vietnam War setting.
- Prepare a plan and specification before programming.

### Proposed assumptions requiring approval before project creation

| Decision | v0.1 proposal | Rationale |
|---|---|---|
| Acceptance platform | Offline Windows PC | Focus on validating combat and builds |
| Visuals | Stylized 3D, 2.5D view, orthographic camera | Show vegetation and fortifications while keeping projectiles readable |
| Engine/pipeline | A compatible Unity 6 LTS version at setup time, URP | Pin the patch version and packages after checking the environment |
| Character/side | People's Army of Vietnam; Trường Sơn, 1972 | Interpretation of “the Vietnamese side”; period selection delegated by the user |
| Authenticity | Arcade gameplay in a historical setting, not a military simulation | Allows multiple weapons, upgrades, and auto-fire |
| Release model | No ads/IAP/backend in the MVP | No commercial requirements yet |

If 2D is chosen instead of 3D, revise the art/pipeline/movement sections before the first milestone; do not silently change direction during development.

### Working setting: a forest supply station, 1972

- Mission pitch: hold the position and recover supplies through 12 attack waves while waiting for a transport convoy to leave the area safely. The convoy is only narrative/ending context, not a new AI escort system.
- “Holding the station” means surviving in the arena; the station has no HP bar or secondary loss condition. The win/loss rules in section 4 remain unchanged.
- The shop is a quartermaster station between waves; supply currency represents arcade-style equipment allocation points, not a simulation of wartime weapons trading.
- Visual direction: dirt roads, camouflage shelters, sandbags, supply crates, and forest vegetation; uniforms, insignia, and weapon models require references before final art production.
- Do not describe this as a recreation of a real battle; do not equate 600 seconds of combat with the duration of a historical event.
- This setting fits the existing terrain and supply mechanics without adding maps, vehicles, or systems. It is a design decision based on the game-design skill's principles of keeping scope small and validating the gameplay loop.

Background source: [People's Army Newspaper — Logistics in the Trị–Thiên campaign of 1972](https://hc.qdnd.vn/lich-su-hau-can/cong-tac-hau-can-chien-dich-tri-thien-1972-463504) provides context on the organization of support operations in 1972. It does not establish the existence of the game's fictional station or mission; equipment details and opposing forces still require separate research.

## 2. Design pillars

1. **Movement is the primary skill:** threats must be visible, with a reasonable escape route always available.
2. **Every purchase changes how you play:** range, projectile volume, area damage, defense, and economy involve trade-offs.
3. **The setting affects gameplay:** obstacles block shots, and supply crates make players consider positioning.
4. **A short run with a beginning and an end:** approximately 13–18 minutes with typical shopping time; players may spend longer in the shop.

MVP differentiators: terrain with fixed obstacles and optional supply objectives. Dynamic rain, morale systems, destructible terrain, and tactical simulation are outside the MVP.

## 3. Version scope

| Content | Combat spike | Vertical slice | MVP |
|---|---:|---:|---:|
| Arena | 1 blockout | 1 partially finished | 1 forest arena |
| Combat waves | 3 | 6 | 12 |
| Character classes | 1 | 1 | 3 |
| Weapon types | 2 | 3 | 6 |
| Passive items | 0 | 6 | 18 |
| Regular enemy types | 2 | 3 | 6 |
| Elite / boss | 0 / 0 | 1 / 0 | 1 / 1 |
| Shop and upgrades | None | Complete basic loop | Complete |
| Supply crates | None | 1 event | Waves 3, 6, 9 |

The MVP has one difficulty level; all characters and weapons are unlocked for testing. No stats increase permanently between runs.

Out of scope: multiplayer/co-op, a historical campaign, an open world, controllable vehicles, AI teammates, crafting, long-term skill trees, accounts, cloud saves, Steam integration, ads/IAP, procedural maps, destructible fortifications, and mobile/Web releases. An expansion can be planned after the MVP meets its criteria.

## 4. Gameplay loop and states

`Menu → Character selection → Preparation → Combat → Wave summary → Upgrade selection → Supply station → Next wave → Results`

- Start with the character's tier I weapon, full HP, 0 supplies, and character level 1.
- Clear a wave by remaining alive when the timer reaches 0. Killing every enemy is not required.
- Win wave 12 when time expires or the boss is defeated. If HP reaches 0 in the same simulation step as a win condition, process death first.
- At wave end: stop spawning enemies and dealing damage; remove projectiles/enemies without granting extra rewards; settle supplies remaining on the ground; process upgrades, then open the shop.
- Completing wave 12 goes directly to results, with no final shop or upgrade selection.
- Current HP carries over between waves; it does not automatically refill. Bandaging and recovery builds have meaningful value.
- At the start of a new wave, all weapons have full magazines and ready cooldowns; reload progress does not carry over from the previous wave.
- Death removes the entire run's build. Restart creates a clean RunState; statistics and settings persist.
- Pause freezes gameplay, timers, cooldowns, reloads, and telegraphs; the UI remains functional.

## 5. Controls and camera

| Action | PC | Planned mapping for touch experiments |
|---|---|---|
| Move | WASD / arrow keys | Left virtual joystick |
| Fire / aim | Automatic by default | Automatic |
| Manual aim | Hold the right mouse button to aim at the cursor; firing remains automatic | Outside the MVP |
| Interact with crate | Enter its recovery zone | Same |
| Pause | Esc | Pause button |
| Shop | Mouse; keyboard focus supported | Touch buttons during experiments |

There is no dash, jump, or reload button in the MVP. Gamepad support and full remapping are extensions, not promised features.

The camera has a fixed orientation, tilted approximately 60° relative to the ground, with no free rotation. The first arena fits within a single 16:9 frame; letterbox other aspect ratios to preserve the same viewing advantage. Default camera shake must not be excessive; allow it to be reduced or disabled.

## 6. Player and stats

The values below are **starting points for playtesting**, not claims of completed balance.

| Stat | Default | Rule |
|---|---:|---|
| Maximum HP | 100 | Increasing maximum HP adds the same amount to current HP, up to the new cap |
| Speed | 5 units/second | Multiplier limited to 0.6–1.8 |
| Armor | 0 | Damage received = raw × 100 / (100 + armor); armor ranges from 0–100 |
| Damage bonus | 0% | Add modifiers within the same group |
| Attack speed bonus | 0% | Interval = base / (1 + bonus); bonus ranges from -50% to +100% |
| Critical chance | 5% | Limited to 0–60%; critical damage ×1.5 |
| Regen | 0 HP/second | Continuous healing during combat, none during pause/shop |
| Pickup radius | 1.5 units | Maximum 4 units |
| Reload duration modifier | 0% | Add signed modifiers; final duration cannot be below 50% of the base duration |
| Range bonus | 0% | From -30% to +50% |

HP uses a float; the UI rounds up for display. Damage after armor reduction is at least 1. After taking a hit, the player is invulnerable to all damage sources for 0.35 seconds, with a clearly flashing outline. There is no armor penetration or damage over time in the MVP.

Reload = `baseReload × max(0.5; 1 + sum of duration modifiers)`. For example, Support's +20% and one Gun Sling's -10% produce a duration multiplier of ×1.10, not ×0.90. Magazine-size modifiers are also added with their signs before multiplying the base value.

Damage order: weapon damage by tier → total damage modifier → critical → target armor → HP loss → death event fired only once. A single explosion hits each target only once. A non-piercing projectile hits only the first target; hitscan selects the closest obstacle or target along the ray.

### Three character classes

| Class | Starting weapon | Traits |
|---|---|---|
| Infantry | Rifle | +15 maximum HP, -5% speed |
| Scout | Submachine gun | +15% speed, +0.5 pickup radius, -20 maximum HP |
| Support | Light machine gun | +20% damage, +20% reload duration, -10% speed |

Class names describe gameplay roles and are not yet associated with specific military units or uniforms.

## 7. Weapons and builds

- A maximum of **4 weapon slots**, with duplicate types allowed. This is an arcade abstraction requiring user approval; it must not be presented as a real soldier simultaneously using four guns.
- Each weapon has its own cooldown, magazine, and reload; it reloads automatically when empty. There is no finite ammunition reserve or ammunition-type pickup system.
- Visually, the character carries one primary gun; secondary equipment uses simple models around the character or effect emission points. Choose the presentation after readability testing in the combat spike, without adding AI soldiers.
- Auto-aim prioritizes the nearest enemy within range and with a clear line of fire; retain valid targets to avoid jittery direction changes. Recheck before firing.
- Direct-fire weapons do not penetrate obstacles. Grenades can travel over obstacles, but their blast only hits targets with line of sight from the explosion center.
- Weapon tiers I/II/III have damage multipliers of ×1/1.35/1.8. Other parameters remain unchanged in the MVP.
- The shop has an explicit merge button: two copies of the same type and tier below III → one copy of the next tier, freeing one slot. No automatic merging or tiers above III.

| Weapon (temporary functional name) | Tier I damage | Fire interval / magazine / reload | Range | Role |
|---|---:|---|---:|---|
| Rifle | 12 | 0.35s / 12 / 1.5s | 10 | Balanced; hitscan with tracer |
| Submachine gun | 5 | 0.12s / 24 / 1.4s | 6.5 | Many hits, close range |
| Light machine gun | 8 | 0.15s / 40 / 2.8s | 8 | Sustained fire, long reload downtime |
| Shotgun | 4 × 6 pellets | 0.85s / 5 / 2.0s | 4.5 | Close-range damage, cone spread |
| Precision rifle | 36 | 1.1s / 5 / 2.2s | 13 | Few hits, high damage |
| Grenade launcher | 22 / explosion | 1.6s / 3 / 2.5s | 8 | Area damage with radius 2; no self-damage |

Historical weapon model names and appearances must be verified for the People's Army of Vietnam in 1972 before asset production. The current table defines gameplay roles; it does not claim that all six types were standard equipment for the unit. Any type without suitable evidence must receive a different presentation or be replaced by a weapon fulfilling the same role, followed by a specification update.

### 18 passive items

A maximum of 2 copies of each type may be purchased; add modifiers together, then apply stat limits. There is no finite passive-inventory slot limit in the MVP.

| ID | Temporary name | Effect per copy |
|---|---|---|
| I01 | First Aid Kit | +15 maximum HP |
| I02 | Adhesive Bandage | +0.3 HP/second |
| I03 | Protective Vest | +10 armor, -3% speed |
| I04 | Marching Boots | +8% speed |
| I05 | Gun Sling | -10% reload duration |
| I06 | Gun Cleaning Kit | +8% damage |
| I07 | Scope | +10% range, -3% speed |
| I08 | Grip | +10% attack speed |
| I09 | Large Ammo Pouch | +20% magazine size, +5% reload duration |
| I10 | Marksmanship Manual | +5 percentage points of critical chance |
| I11 | Supply Backpack | +0.5 pickup radius |
| I12 | Logistics Card | +10% currency from pickups and wave-end rewards |
| I13 | Training Manual | +10% XP |
| I14 | Lightweight Gear | +10% speed, -10 maximum HP |
| I15 | Reinforced Barrel | +15% damage, -8% attack speed |
| I16 | Sensitive Trigger | +15% attack speed, -8% damage |
| I17 | Medic Bag | +0.5 HP/second, -5% damage |
| I18 | Compact Sling | -15% reload duration, -10% magazine size |

Round magazine size down after totaling modifiers, with a minimum of 1. Reducing maximum HP clamps current HP to the new cap; maximum HP cannot fall below 1. Effects apply only upon purchase and must not be reapplied when loading the UI. Passive items cannot be resold in the MVP.

## 8. XP, currency, and shop

XP and supplies are **two separate resources** to avoid conflicts between leveling up and spending currency.

- Defeating a regular enemy directly grants 1 XP and drops a pickup worth 2 supplies. Elite: 10 XP and 20 supplies. Boss: a results reward, not currency for further purchases.
- XP required for the next level: `8 + 4 × (level - 1)`. Subtract the threshold and retain excess XP; support multiple level-ups in one wave.
- Leveling up does not interrupt combat. At wave end, each level gained grants a choice of 1 of 3 distinct upgrades: +10 maximum HP, +5% damage, +5% attack speed, +5 armor, +5% speed, +0.2 regen. No upgrade rerolls in the MVP.
- At wave end, uncollected pickups convert into 50% of their total remaining value, rounded down once. No XP is lost, as it was granted upon enemy defeat.
- Wave survival reward: `10 + 2 × wave`. No survival reward on death. Income bonuses apply when creating rewards/pickups, not a second time upon collection.
- Unspent currency carries over to the next wave and resets for a new run. No interest or permanent currency.

The wallet/pickup system uses integer minor units worth 1/100 of a supply to accumulate income bonuses: a pickup worth 2 with +10% yields 220 minor units. Do not round down individual pickups. The UI displays the spendable whole-number amount; tooltips show up to two decimal places. Prices are always whole supplies and are converted to minor units for transactions. Only the 50% settlement of remaining pickups rounds down to whole supplies once, as specified above; already collected currency retains its fractional amount.

### Shop rules

1. The shop has 4 offer slots. On the first shop visit, with no locked offers: at least 1 weapon and 1 passive, with the other two slots randomized. When refilling, retain locked offers, then use unlocked slots to supply missing categories before rolling randomly; if there is not enough space for both categories, prioritize weapons. Guaranteeing both categories must not overwrite locked offers; if no valid passives remain, fill with weapons.
2. Tier I weapons are always eligible; tier II becomes available from wave 5, and tier III from wave 9. Corresponding I/II/III weights are 100/0/0, 75/25/0, and 55/35/10.
3. Base weapon prices: 20/18/26/22/28/30 in table order; tier multipliers ×1/2/4. Passive base price is 20, except I02/I12/I13/I17 at 28.
4. Final price = `ceil(base × tierFactor × (1 + 0.06 × (wave - 1)))`. Passives have tierFactor = 1.
5. Reroll k within a shop, starting at k=0: `4 + 2k + floor((wave-1)/3)`. Unlimited rerolls while funds permit; reset the counter at the next shop.
6. Lock individual offers to retain them across rerolls and subsequent shops, at no locking cost. Purchasing an offer leaves the slot empty until the next reroll/shop transition. After each transaction, remove and unlock all passive offers whose stack cap has been reached, without immediately refilling them; always revalidate eligibility on purchase. If all four slots contain valid locked offers, disable reroll and charge no fee.
7. With 4 weapons equipped, further purchases are blocked; the player must merge or sell first. Selling a weapon returns 50% of the actual price paid for that copy, rounded down; the starting weapon's actual price paid is 0. The final remaining weapon cannot be sold.
8. A merged weapon records the sum of the actual prices paid for both inputs to calculate its sale price; merging must not generate extra currency.
9. Bandaging: once per shop, costs 15, restores 25 HP; does not occupy an offer slot and cannot be used at full HP.
10. Double-clicking a purchase button must not charge twice; the wallet cannot go negative. The shop has no time limit.

Use separate RNG streams for spawning and the shop, recording the seed in results to reproduce random choices; do not promise bit-identical combat replays across machines.

## 9. Enemies, waves, and difficulty

Enemies are currently specified by combat roles within a fictional raiding force. Individual archetypes are not yet assigned historical units or nationalities; choose forces/clothing appropriate to 1972 when approving references, without assuming that every enemy is American infantry. Most pressure comes from pursuit and positioning, not dense, difficult-to-read bullet patterns.

| Role | Behavior | Counterplay |
|---|---|---|
| Chaser | Pursues, pauses briefly, then attacks in melee | Move and maintain distance |
| Charger | Fast, low HP, charges in a telegraphed direction | Dodge sideways |
| Shooter | Stops to aim for 0.8s, then fires a slow projectile | Read its direction and use obstacles |
| Grenadier | Explosion warning circle lasting at least 1.2s | Leave the warning circle |
| Heavy | Slow, high HP, no directional shield in the MVP | Kite and concentrate damage |
| Support | Speeds up nearby enemies; aura does not stack | Prioritize eliminating the buff source |

The elite is a larger heavy variant with two telegraphed charges; it appears in waves 6 and 9. The boss is a fictional fire team with three phases: a fan-shaped volley, telegraphed blast areas, and a rest period; it does not summon unlimited reinforcements.

| Wave | Duration | Added content |
|---|---:|---|
| 1–2 | 40s each | Chaser → charger |
| 3–4 | 40s each | Shooter; supply crate in wave 3 |
| 5–6 | 50s each | Grenadier; elite and supplies in wave 6 |
| 7–8 | 50s each | Heavy; increased role combinations |
| 9–11 | 60s each | Support; elite and supplies in wave 9 |
| 12 | 60s | Boss and a few supporting enemies |

Maximum total combat time is 600 seconds. Shopping and upgrade selection account for the rest of a run.

Spawning uses wave budgets, archetype weights, and simultaneous living-enemy caps, all data-driven. Begin testing caps of 25/45/65/80 enemies for wave groups 1–3/4–6/7–9/10–12. Do not raise damage, HP, and enemy counts simultaneously without playtesting. Each archetype's HP/damage/spawn rate is an output of the combat spike and is not considered balanced in this document.

Mandatory rules: do not spawn inside obstacles or within 6 units of the player; mark spawn locations for at least 0.6s before activation. If no valid point exists, postpone the spawn; do not force enemies onto the player or release the entire backlog in a single frame. Detect stuck enemies; do not teleport them close to the player.

Enemy shots are not hitscan. Standard projectiles and melee attacks respect the player's invulnerability window. The cap on simultaneously active ranged enemies must be tuned to avoid blocking every escape route.

## 10. Arena and supply objectives

- Fixed arena approximately 34 × 20 units; validate against camera framing and character speed.
- Theme: a fictional supply station in the Trường Sơn region in 1972; forest edge, dirt roads, sandbags, rocks, and supply crates. Do not use the name of a real station or battle.
- At least two wide routes around each obstacle cluster, without accidental dead ends that trap the player.
- Sandbags/rocks block movement and direct-fire shots from both sides; there is no separate crouching/hiding mechanic or cover bonus.
- Decorative leaves/grass have no colliders. Tree canopies that obscure characters must fade or be cut away; they must not obscure explosion warning circles.
- In waves 3, 6, and 9, one crate appears at second 15 at a location confirmed to be reachable. Recovery radius is 1.5 units, requiring 3 accumulated seconds inside the zone; leaving retains progress, and pause does not advance it.
- The crate remains until wave end. Recovering it once grants 20 supplies and restores 10 HP, up to maximum HP. It grants no XP. Enemies do not destroy crates in the MVP.
- Ignoring a crate does not cause a loss or reduce the base reward. The objective must create a risk-taking choice, not force the player to stand still.

## 11. Visuals, audio, and historical presentation

A moderately serious stylized look: olive green, earthy brown, and yellow accents for supplies; enemy projectiles and hazards have outlines that contrast with the background. Do not rely solely on color to distinguish sides or hazards: use distinct silhouettes, symbols, and warning shapes.

Asset production priorities: (1) the player character and first two enemies, (2) guns/projectiles/hit effects, (3) obstacles and crates, (4) remaining archetypes and the boss, (5) trees/grass/backgrounds. Use blockouts during the spike; do not spend money generating assets or downloading paid assets without a request. If Thrixel is used later, assess costs and obtain separate approval before generating models.

Minimum animations: idle, run, fire/recoil, hit reaction, death; enemies share rigs where appropriate. Ragdolls are not required. Limit VFX so they do not obscure projectiles; distinguish player/enemy audio and cap simultaneous gunshot voices.

Proposed content principles: depict soldiers as people, without ethnic slurs; the prototype does not target civilians or reward massacres. Do not use propaganda imagery, slogans, specific historical units, or historical figures before the perspective is finalized and verified. Use music and audio with appropriate usage rights; do not assume famous period songs are available for use.

## 12. UI, persistence, and accessibility

The HUD shows only HP, wave/time remaining, level/XP, supplies, four weapon slots with reload status, and crate progress when nearby. Level-up notifications are brief and do not obscure combat.

The shop shows stats before/after purchase, remaining currency, reroll price, offer locks, merge/sell controls, HP, and bandaging. Every trade-off must appear in Vietnamese tooltips.

Results: win/loss, character, seed, wave reached, combat time, damage, enemies defeated, final build, and retry/return-to-menu buttons. Statistics must come from the actual run, not estimates based on time.

Versioned local saves: audio, shake, damage-number, and language settings, plus aggregate statistics. No mid-run saves in the MVP; quitting loses the current run and must show a warning. Corrupt/old saves must not leave the game stuck at the menu; provide a fallback and clear logs.

Vietnamese UI with full diacritics; fonts must be tested in practice. Test at least 1280×720 and 1920×1080. Text sizes, hazard warnings, and buttons must be clear at the target frame size. Account for touch/mobile in input design, but do not claim support without a build and measured testing.

## 13. MVP gameplay acceptance criteria

- Complete the menu → all 12 waves → results → restart loop without exceptions or leftover state.
- The entire game is playable with movement and auto-fire; manual aim is not required.
- Simultaneous death, time expiry, boss defeat, and pause must not grant rewards or open screens twice.
- Automated tests cover the shop, merging, selling, locking, rerolls, multiple level-up selections, and stat caps.
- Obstacles correctly block shots; pickups/crates are not placed at unreachable locations.
- All three character classes and at least three distinct build approaches can complete a run in internal playtests; winning does not require one mandatory item.
- At least 5 new testers: 4/5 understand the controls and how to start wave 2 without guidance; record confusing causes of death for correction. This is a small test, not market validation.
- Target 60 FPS at 1080p on a reference PC to be finalized in P0, tested under maximum load; this is not measured performance yet. Benchmark and technical details are in PLAN.md.

## 14. Items requiring approval

1. Keep stylized 3D/2.5D, or switch to 2D closer to Brotato?
2. Historical accuracy level and art reference set: the working direction is already the People's Army of Vietnam in 1972 with a fictional mission, not a historically accurate simulation.
3. Is PC-first appropriate, or must mobile be the primary platform?
4. Accept 4 arcade weapon slots, or prefer one primary gun plus support devices?

Answers to the above may revise the draft; no Unity installation, project creation, programming, asset generation, or game publication has taken place during this documentation turn.
