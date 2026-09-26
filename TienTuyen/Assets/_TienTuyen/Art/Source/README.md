# Stylized low-poly source catalog

Generated in Blender 5.2 from `build_stylized_catalog.py`. All geometry and matte
materials in this catalog are original procedural placeholders for Tiền Tuyến.
There are no external textures, downloads, or licensed third-party meshes.

The 16 assets are `hero`, `rifle`, `smg`, `shotgun`, `enemy_infantry`,
`enemy_shooter`, `enemy_charger`, `enemy_elite`, `supply_crate`, `crate`,
`sandbag`, `tarp`, `tree`, `rock`, `bush`, and `grass`. Each has an editable
`Blend/<name>.blend` source and equivalent `FBX/<name>.fbx` and `GLB/<name>.glb`
exports. Run the script again with Blender's background Python command to
regenerate all files. `verify_catalog.py` reimports every FBX and checks its
pivot, rotation, scale, character height, and weapon length.

## Import into Unity

- Use the **FBX** model as the runtime source. Unity imports FBX directly. GLB
  files are interchange copies for external review and would require a glTF
  importer package before use in Unity. Do not place both formats in a scene.
- Models use metres. The runtime importer applies scale factor **1** with file
  scale disabled; `CombatModelImportPostprocessor` owns those settings for the
  copies under `Art/Resources/Models`. FBX round-trip bounds are hero **1.975 m**
  high; rifle **1.116 m**, SMG **0.824 m**, shotgun **1.184 m** long; crate
  **0.855 m** wide; tree **3.21 m** high. Do not add a scale of 100.
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
