# Stylized low-poly source catalog

Generated in Blender 5.2 from `build_stylized_catalog.py`. All geometry and matte
materials in this catalog are original procedural placeholders for Tiền Tuyến.
There are no external textures, downloads, or licensed third-party meshes.

The 9 assets are `hero`, `enemy_infantry`, `enemy_shooter`, `enemy_charger`,
`enemy_elite`, `tarp`, `tree`, `bush`, and `grass`. Each has an editable
`Blend/<name>.blend` source and equivalent `FBX/<name>.fbx` and `GLB/<name>.glb`
exports. Run the script again with Blender's background Python command to
regenerate all files, or pass asset names after `--` to rebuild only those
(`blender --background --python build_stylized_catalog.py -- hero enemy_elite`).
`verify_catalog.py` reimports every FBX and checks its
pivot, rotation, scale and character height, and the realistic catalog below.

## Realistic weapons and props

`rifle`, `smg`, `shotgun`, `m16` (enemy rifle), `supply_crate`, `crate`,
`sandbag`, `drum`, `rock`, `shed`, `watchtower`, `cookfire`, `bunker`, `truck`,
`truck_zil`, `aa_gun` and `bicycle` are textured models built outside the Unity
project by the scripts in `output/props-r/scripts` (sources and licences in
`docs/CREDITS.md`). Their FBX files go straight into `Art/Resources/Models`; the
editor installer no longer copies these names. Conventions are the catalog's:
`TT_<name>_PIVOT` root, Z-up, metres, grip or footprint origin, muzzles along
Blender +Y. Each mesh part `<part>` may have a `<part>_LOD1` at most 40% of its
triangles; a model stays under 15k triangles.

- Weapons keep the part names `CombatCharacterRig` reads: `grip`, `handguard`
  (`pump handguard`), `muzzle`, and on the hero guns `barrel` and `receiver`;
  a bladed tool's blade part contains "blade". Hero guns are authored for the
  x0.72 hero attach scale, `m16` for the enemies' x0.58 x0.9.
- Station props are merged per cluster at runtime by `PropBatch`
  (`EnvironmentStation`), so `CombatModelImportPostprocessor` imports them as
  readable. Sandbag parts are `bag_a`..`bag_d`; `bag_*_ground` are the muddy
  bottom-course variants. Bags are 0.62 x 0.37 x 0.20 m and scaled per cell.
- Materials: **Tien Tuyen > Props > Install Prop Materials** (`PropArtInstaller`)
  builds URP Lit materials in `Art/Props/Materials` from
  `Art/Props/Textures/<material>_BaseColor`, `_Normal` and
  `_MetallicSmoothness` (R metallic, G occlusion, A smoothness) and remaps each
  model's FBX materials to them. Run it after replacing a model or texture.
- The trellis, hammock and camouflage net are built at runtime (`StationMeshes`)
  on copies of the flora's leaf, bamboo and log materials. The hammock nylon and
  garnished net textures in `Art/Resources/Props` come from
  `gen_station_textures.py` (`python gen_station_textures.py <that folder>`).

## Import into Unity

- Use the **FBX** model as the runtime source. Unity imports FBX directly. GLB
  files are interchange copies for external review and would require a glTF
  importer package before use in Unity. Do not place both formats in a scene.
- Models use metres. The runtime importer applies scale factor **1** with file
  scale disabled; `CombatModelImportPostprocessor` owns those settings for the
  copies under `Art/Resources/Models`. FBX round-trip bounds are hero **1.975 m**
  high; tree **3.21 m** high. Do not add a scale of 100.
- Every FBX has a `TT_<name>_PIVOT` root at the footprint or grip origin, with
  zero translation/rotation and unit scale after a Blender FBX round trip.
  Children are separate named mesh parts. Character feet are at local Z=0 in
  Blender. Weapon grip pivots are at local origin.
- Characters face and weapon muzzles point along **Blender +Y**. Exports use
  Blender FBX `axis_forward="-Z", axis_up="Y"`. The imported prefabs retain
  the source Z-up mesh axis; `CombatArtDirector` applies one X=-90° conversion
  when attaching each instance, then applies the caller's weapon yaw.
- `catalog_preview.png` is an orthographic **60° elevation** source review from
  the Blender +Y side; it is not a Unity Play Mode capture.
- The hero/enemy assets are **static unrigged placeholders**. Animation, hand
  sockets, colliders, materials, and historical reference approval remain
  integration tasks. Supply symbols and elite stripe are role cues only.
- Faction flags: the hero carries a Vietnamese flag, enemies a US flag, each on
  a back banner (`banner pole`, `banner cloth <colour>` meshes) plus a sewn
  `flag patch`. Banner cloth is cut into 5 cm columns so `CombatBanner` can
  ripple it in Unity; the character FBX files import as readable for that.
  Character height checks exclude the banner, whose top is at 2.62 m.
- The tarp roof and tree canopy are separate named meshes so the gameplay
  renderer can fade them when they obstruct the camera. Grass and foliage
  should not receive collision; use simple box/capsule colliders for solid props.

The palette follows `docs/ART_DIRECTION.md`: muted olive, brown earth,
warm sand, and a brighter amber supply cue. Every material is opaque and matte.

## Runtime integration

The editor installer copies the FBX files into `Art/Resources/Models` for the
runtime presentation layer. `CombatArtDirector` keeps the gameplay actor and
collider transforms unchanged; character instances receive a -0.8 m local Y
offset because `CombatGame` places its capsule actor at the body centre while
the Blender pivot is at the feet. It converts the source Z-up axis to Unity's
Y-up axis at this boundary, rotates weapons into the gameplay hand axis and
uses the imported mesh when available. The authored low-poly fallback remains
visible when a model resource fails validation.

## Environment sources

- `build_mountains.py` bakes the far backdrop `Art/Resources/Environment/Mountains.bytes` from AWS terrain
  tiles (usage in its header). The mountain look itself is shader-side (`EnvMountains.shader`).
- `pack_textures.py` packs the ground layers (Rocky Hills and Mountain Lake tiles) and the Poly Haven bark and rock sets into `Art/Resources/Environment`.
- `pack_water.py` packs the falls' white water and the mist puffs (Rocky Hills) and the river's ripple normals (Mountain Lake) into `Art/Resources/Environment/Water`; usage in its header.
- `Flora/` builds the `Art/Flora` species in background Blender: `flora_build.py` makes one species
  (LOD0/LOD1 meshes, cross-card LOD2, textures, json) from the stage-1 source scene
  `output/environment-review/stage1/blender/env_stage1_sources.blend`, the Poly Haven downloads and the
  pine-woodland downloads in `output/assets` (its `EXT` table). Sources and licences are in `docs/CREDITS.md`.
