# TIỀN TUYẾN — Art direction & asset plan v0.1

Date: 25/09/2026. **Proposal for approval**, not implemented art or a benchmark. Gameplay follows [SPEC.md](SPEC.md); the production schedule follows [PLAN.md](PLAN.md). No changes to the 12 waves, 4 weapon slots, or MVP scope.

## 1. Visual direction

**Stylized low-poly 3D with hand-painted colors**, a moderately serious tone, clean shapes, and low-noise materials. Not chibi, photorealistic, or pixel art. Characters have near-realistic human proportions, with slightly oversized hands, weapons, and equipment pouches for readability. Do not give enemies exaggerated, monster-like bodies.

- Third-person perspective camera behind the soldier's right shoulder (FOV 56°, 40° when aiming), with a slow cinematic orbit on menus. Review assets at eye level and from behind the hero, not only from above.
- The clearing sits in generated jungle hills: laterite ground, grass, banana, palm, bamboo, canopy trees, late-afternoon sun, haze, ACES grading and bloom (`CombatEnvironment`).
- Muted olive forest, warm brown earth, and soft daylight. Sparse grass along movement paths; concentrate tree canopies around the edges and fade them when they obscure characters or warnings.
- Soft shadows and minimal reflections; limit bloom, dense fog, and depth of field. Do not add dynamic rain in the MVP.
- Visual priority: player → danger → supply objective → enemies → obstacles → decoration.

The setting is a fictional supply station in Trường Sơn in 1972. Uniforms, insignia, weapons, and opposing forces are currently placeholders: historical references must be approved before final art. A gameplay role does not establish that a particular unit actually used a given piece of equipment.

## 2. Experimental color palette

| Role | Starting colors | Additional identification cues |
|---|---|---|
| Forest / background shadows | `#354638`, `#24352D` | Large shapes, sparse detail |
| Ground / paths | `#80664C` | Low-contrast surfaces |
| Player | `#8E9B66` | Bright outline, foot ring with a chevron |
| Enemies | `#766C5E` | Role-specific silhouettes, not just color swaps |
| Supplies | `#E7BD62` | Crate icon + progress ring |
| Danger | `#F17858` | Light/dark outlines, area shapes, and countdowns |
| UI text / background | `#EEE7D4`, `#202A25` | Clear Vietnamese diacritics, solid panel backgrounds |

These colors are an art hypothesis; contrast has not yet been tested. Enemy bullets use bright dots with short trails; player tracers use thin streaks. Danger, pickups, and the player must remain distinguishable in grayscale.

## 3. Asset catalog by milestone

The quantities below are cumulative totals; share meshes/rigs where appropriate. Each role does not necessarily require an entirely new model.

| Group | P1: combat spike | P2: vertical slice | P3: MVP |
|---|---|---|---|
| Characters | 1 placeholder | 1 fully finished art sample | 3 classes: infantry, scout, support |
| Weapons | 2 placeholders | 3 models + icons | 6 roles + 6 icons |
| Enemies | 2 placeholders | 3 regular + 1 elite | 6 regular + 1 elite + 1 boss |
| Items | Not needed yet | 6 icons | 18 distinct icons; 18 separate 3D models are not required |
| Arena | 1 graybox | 1 representative, fully finished section | 1 cohesive forest arena |
| Supplies | Standard pickups | Add an objective crate | Reuse in waves 3/6/9 |
| UI/VFX | Minimal HUD and feedback | Menu, shop, upgrades, results | Complete state coverage and accessibility |

### Interface (implemented 2026-09-30)

- Typefaces: Oswald Bold for titles, numbers and captions; Source Sans Pro Semibold for body text. Both are SIL Open Font License fonts with full Vietnamese coverage; the license and copyright notices ship in `Art/Fonts/OFL.txt`.
- Palette: jungle-night ink panels (`#0C120F`–`#26332A`), parchment text (`#F1EBDC`), brass accent (`#E4B24A`), olive-lime health (`#B7D36A`), red danger (`#E2553F`).
- The combat HUD keeps the screen centre clear for the shoulder camera: supplies and level top-left, wave clock with six wave pips top-centre, radar top-right, health with 25 HP ticks and a damage trail bottom-left, weapon silhouette, ammo count and magazine pips bottom-right, secondary weapon chips above it. Soft edge gradients replace solid bars.
- Menus use rounded 9-sliced panels with hairline outlines, keycap hints, animated hover/focus states and short fade/slide transitions on unscaled time. Weapon, item and upgrade icons are vector silhouettes rasterised at runtime (`UiGlyphs`).

The six weapons are a rifle, submachine gun, light machine gun, shotgun, precision rifle, and grenade launcher; specific real-world models will be chosen after verification. Tiers I/II/III use UI labels; do not create 18 meshes yet. All four slots remain visible on the HUD; the proposal is to show one primary weapon in hand, with small effect emitters for the other slots. Validate readability in P1 before committing; do not add AI squadmates.

The six enemy types are melee, charger, shooter, grenadier, heavy, and support. Differentiate them through stance, pouch/weapon size, and attack telegraphs; do not use unsupported costume choices merely to distinguish roles. Emphasize elites through equipment and markers, not by stretching them into giants. The boss is a fictional fire team with normally proportioned soldiers; its behavior still follows SPEC, with no controllable vehicles or new mechanics.

### Faction flags (implemented 2026-10-01)

- The hero carries the flag of Vietnam (red field, centred yellow star, 2:3) and every enemy carries the flag of the United States (13 stripes, blue canton with a simplified star field), matching the resistance-war setting of the SKS and K-50M weapons.
- Each flag is a back banner on a pole strapped to the backpack, plus a small sewn patch: the hero's on the backpack (seen by the shoulder camera), the enemy's on the left chest (seen as they advance). The hero's flag flies out to the left, clear of the crosshair; an enemy's flies across above its helmet so the player sees the obverse, canton upper left.
- The geometry is authored in `build_stylized_catalog.py` (`back_banner`, `vietnam_flag`, `us_flag`). `CombatBanner` merges each character's cloth into one mesh and ripples it away from the pole; running makes it trail and flutter harder, and hidden flags skip the vertex update.

### Modular environment kit

Propose 12–16 reusable modules: sandbag sections/corners, closed/open crates, a canopy, posts, a barrel, a log, two rocks, two trees, a bush, grass, and a ground patch. Assemble the arena by hand; do not generate the entire map as a single mesh. Sandbags/rocks have clearly defined colliders; grass/leaves have none. Each cover cluster has at least two routes around it. Separate roofs/canopies to handle occlusion.

## 4. Rig, animation, and provisional budgets

Use a shared humanoid skeleton where appropriate, with equipment and mesh variants. The basic set is idle, run, recoil/fire, reload, hit, and death; add aim, charge, and throw animations for roles that need them. No ragdolls. Animation must not delay or shorten the specified gameplay warning periods.

| Asset | Initial trial target, not a measured limit |
|---|---|
| Character | 2,000–4,000 triangles; 1–2 materials |
| Weapon | 300–1,000 triangles/model |
| Small prop / tree | 100–800 / 500–1,500 triangles |
| Texture | 1K character atlas; 1K–2K environment; 256–512 VFX |
| Icon | 256×256 source; check at the displayed size of 48–64 px |

Prioritize atlases, shared materials, pooled VFX, and few layers of transparent foliage. Finalize budgets only after standalone measurements on the reference PC defined in PLAN; do not infer 60 FPS from polygon counts.

## 5. UI, VFX, and asset sources

The UI suggests a quartermaster board through paper/olive colors, without placing aged textures over text. The shop has four item cards; the inventory has four weapon slots. Equipment tiers use Roman numerals; locked states use icons; explosion warnings use rings, lines, and timing, rather than relying on red/green. Keep hit flashes short and do not obscure telegraphs; allow shake/damage numbers to be disabled. Danger sounds supplement visuals rather than replace them.

Possible sources include self-authored assets, commissioned artists, appropriately licensed libraries, or generated models after cost approval. Do not assume free assets are licensed for commercial use. Record the source, author, license, evidence of usage rights, and modifications in the manifest; check music/fonts/animation separately. No purchases, asset-pack downloads, tool installations, or model generation at this stage.

Concept/keyframe images are for approving color, composition, and style; they are **not meshes, rigs, texture atlases, or Unity gameplay screenshots**. The visualization supports discussion of density, HUD layout, and viewing areas; it does not establish performance or navigability.

## 6. Approval gates and QA

1. Approve one combat keyframe and the palette from the actual camera angle.
2. Approve one hero with a sample weapon/rig; check the image at 720p before producing the full catalog.
3. Integrate into the blockout; verify bullets, cover, tree canopies, and the four slots, then finish the vertical-slice section.
4. Expand to the full MVP once the art pipeline is stable.

QA: test at 720p/1080p, in grayscale, in crowded combat, and with shake disabled; the player, bullets, and telegraphs must remain trackable. Meshes have correct scales/pivots, no materials are missing, animations have no obvious foot sliding, and Vietnamese text retains its diacritics. Compare visuals against colliders; check licenses and historical references before labeling art final.

Further approval is needed for this 3D direction, the degree of character exaggeration, the representation of four weapons, and the appearance/identity of opposing forces after research. Every asset does not need to be finalized before the combat spike.

## 7. Illustrations v01

- [Color and environment concept](art/concept-v01.png), created with the integrated image-generation tool; [prompt and review notes](art/CONCEPT_PROMPT.md).
- [HUD layout QA image](art/hud-layout-qa-v01.png): a symbolic diagram, not game art. The accompanying interactive version in the conversation switches between combat and quartermaster screens.
- The concept currently exceeds the proposed detail budget; production needs to reduce ground/foliage noise and make the two sides' projectiles more distinct. The image's uniform/weapon details have not received historical approval.
- Browser checks of the HUD version: both screens switch correctly, no runtime errors occurred during the check, and there was no horizontal overflow at 320 px. Unity, real touch input, and gameplay performance have not yet been verified.
