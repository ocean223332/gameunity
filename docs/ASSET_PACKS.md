# Asset packs: what they teach and how we port them

Four Unity Asset Store packs back the environment: Rocky Hills, Mountain Lake, Mountain Trees and SeedMesh Jungle. Each
section says where the pack lives, what it is worth taking, and how its conventions map onto ours. What ships, and under
which licence, is listed in [CREDITS.md](CREDITS.md) only.

## Our side of a port

- Flora goes through `Art/Source/Flora/flora_build.py` (background Blender). A pack mesh becomes a species through an `EXT`
  entry: source FBX, LOD0/LOD1 object prefixes, target height, leaf materials, textures (`maps`, or `mats` for one map set per
  source material), wind stiffness, texture size (`tex`) and the leaf `look`. The comment above `EXT` documents the keys.
- `paint()` writes our vertex colour, whatever the pack used: R wind weight (grows with height; `hang` reverses it for
  vines), G wind phase, B leaf flag, A occlusion. Packs' own colour channels are not kept.
- Every species gets LOD0, LOD1 and a cross-card LOD2; tall trees switch to cards from 60 m (`FloraPrefabBuilder`).
- One shader draws all flora: `Art/Shaders/EnvLeaf.shader`. Wind is `EnvWindOffsetAt` (EnvCommon.hlsl) plus the meadow's
  gust fronts (`EnvGrassGust`, EnvTerrain.hlsl), so grass and trees move in one wave. The per-species leaf `look`
  (cutoff, smoothness, translucency, tint variation) is read by FloraPrefabBuilder from the species json.
- Ground layers go through `Art/Source/pack_textures.py` (see its modes).
- Import scales: an FBX's real size depends on its `.meta` (globalScale, useFileScale) and on the pack prefab's scale.
  Measure heights in Blender; give `EXT` absolute heights.

## Handling the downloads

- Each pack sits in `output/assets/_downloads/<pack>/` beside its `.unitypackage`. Unpack into that folder's `extract/`,
  read with `python -I`. Extracts can be pruned and re-extracted at any time; keep D: above 10 GB free.
- Tools in `output/flora_tools/`:
  - `upkg_metas.py`: import scales from the package's metas;
  - `fbx_stats_r.py`: tris, LODs, sizes and materials per FBX (Blender);
  - `scene_scatter.py`: counts, scale range and clustering of a demo scene's prefabs;
  - `jt_textures.py`: Jungle TIFFs to PNG with the coverage alpha kept;
  - `ext_paths.py`: checks that every file an `EXT` entry reads exists;
  - `pack_slice.py`: copies a demo scene and its guid closure into a scratch project, textures capped.
- Pack scripts may run, but demo scenes open only in the scratch project `output/pack_demo` (`run.sh` captures them),
  never in TienTuyen. `PackDemo.cs` lists what it patches so a demo renders (light units, ambient, broken materials).
  The guid slice does not carry terrain layers, so demo terrain shows a checker.

## The demos, seen running

Captures, one contact sheet per pack: `output/pack_demo/caps/<pack>/sheet.jpg`; `caps/side_by_side.jpg` sets each
pack's best view beside ours (Sunny forest floor and forest edge).

- **Rocky Hills** (`TFS Demo (New)`, Built-in RP with its own post) is the most useful.
  - Rock and meadow merge: grass and moss cover the upper faces of boulders (Detail Map Coverage), and grass tufts root
    along the rims and between the stones. Ours: stones sit on the grass with a hard contact line.
  - Rocks are stacked into tiers and terraces with plants filling the gaps, so each view has foreground rock, a
    midground meadow and a rock skyline. Ours: single stones, no mid-scale rock structure.
  - Colour accents: yellow and orange shrubs and wildflower drifts, about one bush in ten. Bare snags among the green
    trees break the canopy line.
  - Sky ambient at 1.4 keeps the shadows readable under a strong sun; the pale limestone carries the contrast.
- **Jungle** (`Assets.unity` showcase; `Jungle.unity` is too large to open here).
  - Variation is painted into the albedo: rust-dead fronds inside green ferns, moss on the tops and joints of the
    masonry, vines draped over the walls.
  - Backlit leaves render black in URP: the HDRP diffusion-profile translucency does not survive the URP graphs.
- **Mountain Lake** did not port in this pass (rocks render black under Env Lit, the terrain shows a checker); its
  lessons are its URP profile and assets above.
- **Mountain Trees** was not run; its demo has 51 instances.

## Rocky Hills Environment – Mega Pack v4.1 (Toby Fredson)

`output/assets/_downloads/rocky_hills/`

- **Pipeline.** Built-in RP only: surface shaders (Leaves Fast, TFS Tree Bark Foliage Wind, Grass Foliage, Detail Map
  Coverage). Pink under URP. The demo grades with Post Processing v2 and drives wind through the CTI_CustomWind script.
- **TFS tree meshes have no material slots.** Bark and leaves are separate objects (`GTSA_Bark_LOD0`,
  `GTSA_Leaves_LOD0`…). `flora_build` names a slot after each object so `mats` keywords match.
- **Take the texture the pack's material names.** Vegetation textures come in numbered copies that are other variants:
  `NeedleBranchesRHE_Albedo 1` is snowy and `VegetationA2_Albedo 2` is a grass atlas. The summer leaves are
  `VegetationA2_Albedo` (GenericTree_LeafsSummer) and `NeedleBranchesRHE_Albedo` (PineLeavesRHE). `listing.tsv` beside the
  package maps guids to paths.
- **Taken.**
  - Ground layers: grass, flowering sward, soil, mud, needles, rock. Each is a TerrainLayer: diffuse 2048, OpenGL normal,
    mask at half size (R metal, G AO, B height, A smoothness), albedo gained to a target average. EnvGround blends them by
    height, so the highest surface in a mix wins.
  - Mossy stones, rock face, log, snag, stump, low bush, wildflower and tall-grass patches.
  - Waterfall foam (WaterfallFoamRHE_Albedo) and mist puffs (CloudsA), both 512 (staged).
  - Staged trees: GenericTreeSummer_A, GenericTree_B, GenericTree_E, SpruceTree_C, BeechTree05Summer (gold beech).
- **Not taken.**
  - BackdropMountain A/B/C: 16-tri curved cards with a painted albedo; flat, no relief, not karst.
  - Cliffs (ModularCliff, GenericCliff, CliffLargeA 5.6k tris, CliffLargeTerrain 54k/73k): rounded alpine limestone. Fine
    as boulders, wrong for sheer fluted towers; CliffLargeTerrain is too heavy for the far field.
- **Open candidate.** A 2K Rocky Hills cliff normal in place of the tower rock normal in the EnvMountains material (one
  texture). Untested. It can only help the near towers (about 0.9–3 km): the rock detail fades out between 8.4 and
  19.6 km, and the far ranges are already squeezed past it. Judge it on 1_valley and 14_vista_south in one preset.
  - Waterfall Base shader: a panning additive texture; EnvWaterfall does more. WaterfallBase mesh: fixed shape, while ours
    follows the cliff.
  - Simple Water shader: a generic lake. waternormal.png: nearly flat.

## Mountain Lake – Forest Pack v1.0.1 (VIVID Arts)

`output/assets/_downloads/mountain_lake/`. Detailed listing: `output/assets/mountain_lake/README.md`.

- **Pipeline.** Ships `Packages/MLRT_URP`, `MLRT_SD` and `MLRT_HDRP` sub-packages. The URP one, extracted to
  `extract_urp/`, holds the ASE shaders "VIVID Arts/Foliage" and "Env Lit".
- **Taken.**
  - Moss ground (ML_Moss_Green_Ground).
  - MLRT_Cliff_Tiled, packed to 1K as `Art/Resources/Environment/KarstRock.png` (rgb albedo, a height). It is the rock of
    the near karst wall and of the far towers. On the wall, EnvGround tiles it at 14 m, tones it to pale limestone, and
    lets its height hold streaks, moss and spray-wetness in the low ground first.
  - Ferns (MLRT_Fern_01–03) and the mossy outcrops (MT_Rock).
  - River ripples (Ocean_Shallow_Normal, 2K → 1K; staged).
  - Staged sugar maples (MLRT_SugarMaple_01/02_Var1, `*_Trunk_LOD*` plus `*_Branches_LOD*`). The frond atlas is grey and
    the pack tints it in the shader, so `leaf_mul` bakes the tint (0.48, 0.70, 0.44) into the albedo.
- **Not taken.**
  - MLRT_MountainRange: one 7.2 km heightfield with 270 m of rolling alpine relief, plus a ring of fog cards at its foot.
    The idea stays (one range, fog at its base), but our DEM rings, karst towers and procedural cloud bands do it with real
    relief.
  - Water.shadergraph (an ocean look) and Ocean_Deep_Normal (too choppy).
  - MLRT_FogCard: a plain blob; CloudsA has more detail.

## Mountain Trees – Dynamic Nature v2.8.5 (NatureManufacture)

`output/assets/_downloads/mountain_trees/`

- **Pipeline.** Built-in shaders, with URP 15 and HDRP 15 support packages nested under `HD and URP Support Packs
  Mountain Trees/`.
- **Taken (staged).**
  - Firs: Fir_01 plant, Fir_02 small, Fir_03 medium, Fir_05 standalone, Fir_06/07 forest, Fir_08 dead.
  - The grove core is Fir_05 + Fir_03; Fir_06/07 serve only as emergents.
  - Each species' LOD1 is the pack's LOD2. Leaves ship at 2048, barks at 1024.
  - Their impostor cards render empty (cause unknown, see the flora stage notes). The tall firs show Spruce's card past
    350 m (`cardFrom`); Fir_Small, Fir_Plant and Fir_Dead keep LOD1 to their last distance.
  - Material to texture: M_leaves_Fir → T_Fir_leaves_BC_T (alpha is the cut-out); Bark_Fir_02 and Bark_Fir_04_02 →
    bark_02; Bark_Fir_06 → bark_06; the rest → bark_01.
- **Demo.** 51 instances, so it teaches nothing about scatter.

## Jungle – Tropical Vegetation v3.2 (SeedMesh)

`output/assets/_downloads/jungle_tropical/`

- **Pipeline.** HDRP. `Shader_graph/URP_Shaders.unitypackage` replaces the six vegetation graphs by guid, and
  `Tutorials/HDRP_to_URP.pdf` covers the rest. Its post (HDRP volume) has no URP counterpart.
- **Textures.**
  - The 4-sample TIFFs mark the fourth sample "unspecified", so most readers drop it.
  - In leaf albedos that sample is the coverage; `jt_textures.py` keeps it as alpha.
  - In masks the fourth sample is smoothness.
- **Missing.** No bamboo and no areca mesh. Our Bamboo stays; Tall_palm stands in for areca. Green_tall_basecolor, a
  segmented palm trunk, could re-skin the bamboo.
- **Taken (staged), LOD0 / LOD1 tris.**

  | Species | Source | Tris |
  |---|---|---|
  | Banana_B/C | BananaTree_var1/2 | 4575/963, 4606/678 |
  | Areca_A/B | Tall_palm_var1/2 | 2399/452, 2467/657 |
  | PalmYoung_A/B | Young_palm_var1/4 | 802/252, 716/226 |
  | Vine_A/B | Hanging_vine_var1/2 | 18/6, 36/12 |
  | Vine_Monstera | Vine_monstera_hanging_var1 | 309/126 |
  | Climber_A/B | Herb_climbing_var1, Monstera_climbing_var1 | 107/71, 147/90 |
  | Fern_D/E | Fern_var1/2 | 180/63, 282/100 |
  | ElephantEar_A/B | Elephant_ear_var1/4 | 314/106, 495/233 |
  | Heliconia_A/B | Heliconia_var1/3 | 528/302, 434/162 |
  | RedGinger | Red_Ginger_var2 | 1750/702 |
  | JungleTree_A/B | Jungle_tree_var2/3 | 5700/1275, 5198/955 |
  | JungleTree_Tall | Tall_tree_var1 | 7056/936 |

- **Not taken.**
  - Tree_on_rock: 66k tris.
  - Medium_tree_var1/3: 7.7k–8.5k tris, no better than the jungle trees.
  - Sea_water and Moss graphs: Shader Graph, sea look; we already do the moss projection in HLSL.
  - Cliff_var1/2 and Rock_var1/2 are open candidates for the cliff foot and plunge pool, kept clear of the fall at
    x −108 ± 20.

## What makes them beautiful, and what we applied

| Technique (pack) | Applied |
|---|---|
| Normal flipped on back faces of two-sided leaves (Mountain Trees BackFaceMirrorNormal, Rocky Hills NormalBackFaceFix, Mountain Lake) | EnvLeaf, ForwardLit and DepthNormals (staged) |
| Translucency with the light bent by the normal (distortion .5, exponent 2); shadowed leaves keep a quarter (half glowed in shade); a small sky term (Mountain Lake ASE; SeedMesh by thickness) | EnvLeaf (staged) |
| Healthy/dry tint from world noise ~50 m across, so groves differ, not single trees (Mountain Trees) | EnvLeaf: 65 % grove noise, 35 % per-plant hash (staged) |
| Gusts that travel across the forest; bend about the pivot keeping length (Mountain Trees NMWind) | EnvLeaf gust from `EnvGrassGust` at the plant's root; crowns drop by offset²/2h (staged) |
| Tropical leaves: waxy, alpha clip ~.25, strong translucency (SeedMesh); firs cut at .4 (Mountain Trees); TFS leaves at .32 | Per-species `look` in `EXT` |
| ~65 % of the jungle demo's instances are ground carpet, scales 0.15–1.7; few, large trees; ~240 climbers | Plateau: fewer trees over a 2–4 plant carpet per cell (scale .45–1.5), a rare giant; climbers at the wall foot; vines hung from ledge lips |
| Flowers and herbs in one-species drifts, nearest neighbour ~0.5 m (Rocky Hills Summer) | `PatchPick` / `Patch`: herbs chosen by 10 m noise; heliconia and ginger in small clusters (~4 %) |
| Rocks in families: debris around boulders, bushes at rocks and edges, litter under trees (Rocky Hills) | Outcrops get a skirt of stones and ferns |
| Grass and moss over boulder tops, tufts along their rims; rocks stacked in tiers (Rocky Hills) | EnvLeaf `_ROCK_TOPBLEND` on the rock `*_M0` materials and Outcrop tiers (terra); `RootTufts` rings and rim tufts |
| Warm accent shrubs, about one in ten, and bare snags among green trees (Rocky Hills) | Beech_Gold, Maple_A/B and Fir_Dead (staged) |
| Dead or rust fronds painted into the albedo (Jungle) | Not yet; the grove tint varies whole plants only |
| Post and light: see below | Not applied yet |
| Depth from one textured range plus fog at its foot (Mountain Lake) | Same idea with DEM rings, aerial perspective per layer and procedural cloud bands (EnvKarstCloud.hlsl) |

## Post-processing and light

Profiles:
- Rocky Hills `Toby Fredson/Post Processing/*.asset` (Post Processing v2).
- Jungle `Scene/Post Processing/Jungle_scene.asset` and `Groups_demo/*Profile.asset` (HDRP volumes).
- Mountain Trees `Demo/PostProcessProfile.asset` (v2).
- Mountain Lake `VolumeProfiles/MLRT_GlobalVolume_URP.asset` in the MLRT_URP sub-package (URP volume). Its pipeline asset
  has HDR grading with a 32-cube LUT, shadows to 170 m in 3 cascades, MSAA off, and SSAO present but inactive. Its sun is
  warm (1, .89, .53); scene fog is off.

Settings by pack:
- **Tonemapping:** ACES in all four. We already use it.
- **Grade:** neutral base with small offsets.
  - Rocky Hills: exposure +.2 to +.3, contrast −2, temperature +2, saturation 0 to +12, gamma +.13 to +.22 (lifts the
    mids), gain −.15 to −.17 (pulls the highlights in).
  - Jungle: saturation −10, contrast −10, hue −5. Cool shadows (.91–.93, .96–.98, 1), warm mids (1, .95–.98, .91–.95).
  - Mountain Lake: contrast +10, saturation −7, split toning with cool blue shadows (.33, .39, .50) at balance +12, and
    colour curves.
  - Cool shadows come from the grade in Jungle and Mountain Lake, but from the AO colour in Rocky Hills.
- **AO:**
  - Rocky Hills: multi-scale volumetric obscurance at .5–1, radius .25, tinted sky-blue (.18, .24, .33), so creases read
    as cool skylight rather than black.
  - Jungle: HDRP AO 1.1–1.9, radius .7–1.4, direct-light strength .32, with contact shadows.
- **Bloom:**
  - Rocky Hills: 1.2, threshold 1, knee .7, diffusion 7, lens dirt 2.
  - Mountain Trees: 2, threshold 1.
  - Mountain Lake: .2, threshold 1.25, scatter .8.
- **Vignette:** Jungle .33–.39, Mountain Lake .34, Mountain Trees .53, Rocky Hills none.
- **Film grain:** Jungle .3, Mountain Lake .38. Both also use motion blur; Mountain Lake adds chromatic aberration .04.
- **Auto exposure:** Rocky Hills, −.6 to +1.3 EV. URP has no built-in equivalent.

Our post (CombatEnvironment, per preset) already has ACES, HDR grading with a 32-cube LUT, bloom, vignette, white balance
and shadows/midtones/highlights. It lacks tinted AO, film grain, lens dirt, a gamma/gain lift-and-roll-off, and exposure
adaptation.
