# Blender profile: Tiền Tuyến

Read by the `blender-code-model` skill. Paths are relative to the repo root.

## Engine
- Unity, URP. Project in `TienTuyen/`.
- Blender builds with front −Y, Z up. The runtime model catalog is Z-up (`CombatModelImportPostprocessor`).
- Characters face +X in the knife and third-person takes' own space. The import applies `orientationOffsetY -90`, so a
  new take authored in that space gets the same quarter turn as `stand_idle`.

## Art priority
- Graded coursework. Beauty first; stylised is fine, crude is not. Third-person camera; visuals within the FPS budget.

## Budgets
- Prop 1–5k triangles, hero prop or weapon 5–15k, character 15–30k. One material, one atlas (1024; 2048 up close).
- Texel density about 512 px/m for third-person props, 2048 px/m for the first-person viewmodel.
- Skin influences: at most 4 per vertex (Unity PC quality).

## Skeleton and clips
- **ValveBiped** (50 bones, xdR CS:GO rest). Humanoids bind to it and play the CS:GO third-person, knife and body
  takes through Unity Humanoid.
- Build the armature with `output/hero-valve/_scripts/valve_rig.py`, and skin with `skin.py` (bone heat through
  joint-to-joint helpers).
- For stylised proportions, keep the xdR bone rotations and move the joints: `output/enemy/boss/_scripts/fit_bind.py`
  writes `rest_fit.json`.
- `output/enemy/boss/_scripts/bpose.py` poses a character on a take frame the way Humanoid playback does, e.g.
  `pose('stand_idle', 0, 'takes_knife')`. `shrug_probe.py` measures shoulder rise; `check_fbx.py` checks the bind
  joints against `rest_fit.json`.
- Accessories hang on ValveBiped bones (`ValveBiped.Bip01_Spine4`, `ValveBiped.Bip01_R_Hand`, …).
- Hand-keyed takes: 30 fps; start and end on the knife `stand_idle` frame 0.
- Known issues the Humanoid retarget adds: the knife takes' shoulder Down-Up muscle reaches +1.55 at the slash peak
  (a shrug on broad bodies). Muscle limits exist only in Unity, so check shoulders in game.
- ARP scripts for this skeleton: `output/enemy/boss/arp/_scripts/arp_rig.py` and `arp_to_valve.py` (`--body` /
  `--arp-body` name the meshes; ring and pinky go to Finger3x/Finger4x on a 62-bone body, Finger2x on the 50-bone
  xdR); the US soldier's chain is `output/us-slot1/_scripts/` (arp_rig, fit_valve, prep_transfer, export). Quick Rig
  `arp/quickrig/_scripts/quickrig.py`, Remap `arp/remap/_scripts/remap.py`. Measure and export a take with
  `arp/_scripts/take_check.py`.
- Quick Rig limb map for ValveBiped: spine Pelvis, Spine, Spine1, Spine2, Spine4; head Neck1, Head1 (one neck bone);
  legs Thigh / Calf / Foot / Toe0, twist 0, auto_knee off; arms Clavicle / UpperArm / Forearm / Hand, twist 0,
  fingers '3' on the 50-bone xdR (thumb Finger0, index Finger1, middle Finger2), '5' on a 62-bone body (ring
  Finger3, little Finger4), auto_ik_roll on. `CTRL` in quickrig.py names the ARP
  control for each bone.
- Remap mixamorig → ValveBiped: Hips→Pelvis (root), Spine/Spine1/Spine2, Neck→Neck1, Head→Head1, Shoulder→Clavicle,
  Arm→UpperArm, ForeArm→Forearm, Hand, Thumb/Index/Middle 1–3 → Finger0x/1x/2x, UpLeg→Thigh, Leg→Calf, Foot,
  ToeBase→Toe0; Spine4 unmapped. ARP's mixamo presets target ARP controls, so don't use them here.
- ARP's AI markers space the trunk joints evenly along a straight line. xdR's are not spaced that way. Limb-slab
  checks and skeleton overlays can't see this, because a joint 5 cm off still lies inside a thick chest. Check the
  trunk joints against xdR's own proportions instead. Measure each joint's fraction along Pelvis→Head1 and its
  offset behind that line, as a share of the line's length (+ is towards the back):

  | | Spine | Spine1 | Spine2 | Spine4 | Neck1 |
  |---|---|---|---|---|---|
  | xdR (hero) | .134 / +.108 | .287 / +.113 | .422 / +.121 | .764 / +.109 | .888 / +.081 |
  | ARP on the US soldier | .160 / +.008 | .320 / +.015 | .480 / +.023 | .640 / +.035 | .799 / +.048 |

  Also, xdR's Spine4 sits level with the upper-arm joints (+5 mm). Spine4 matters most:
  - `WeaponClipImporter.Bake` anchors the Weapon bone to it (UpperChest), so it sets the rifle's height against the
    face;
  - the aim pitch bends the spine chain about it.

  The whole curve also sets the Humanoid chest's pitch: on ARP's straight trunk the US chest, and the rifle riding it,
  tipped about 7° off the hero's (0.8° after).

  Rule for every ARP-rigged (or generated) body: place Spine, Spine1, Spine2, Spine4 and Neck1 at xdR's row above, scaled
  to the body's own Pelvis→Head1 line, with xdR rest rotations. `valve_rig.xdr_trunk(pelvis, head1, forward)` gives the
  places. `output/us-slot1/_scripts/fit_valve.py` uses it, and `retrunk.py` re-fits an already skinned body and moves
  the trunk weights with their joints. Check the table on the exported bind before importing.

## Textures
- `<name>_BaseColor.png` (sRGB), `<name>_Normal.png` (OpenGL, green up), `<name>_MetallicSmoothness.png` with
  R metallic, **G ambient occlusion**, A smoothness (Non-Color, RGBA).
- `TienTuyen.Editor.PropArtInstaller` reads this and makes a URP Lit material.
- Generated characters (Tripo) bake shadow into the base colour under the arms. Fix with
  `output/enemy/boss/anim/_scripts/armfix.py`.

## FBX export
- From `output/hero-valve/_scripts/export_fbx.py`:
  `object_types={'ARMATURE','MESH'}, add_leaf_bones=False, armature_nodetype='NULL', apply_scale_options='FBX_SCALE_ALL',
  bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False, path_mode='STRIP', mesh_smooth_type='FACE'`.

## Into the engine
- Props: `TienTuyen/Assets/_TienTuyen/Art/Resources/Models/<name>.fbx`, with textures in `Art/Props/Textures/`. Then
  run `TienTuyen.Editor.PropArtInstaller.Install` in batch.
- Characters: `ValveBipedCharacterImporter` (add an entry like `ImportBoss`).
- Every C# file saved under `Assets` must compile (Roslyn check from the shell) before Unity opens it.

## In-game proof
- Batchmode PlayMode route: a throwaway test that is never committed, run with `-batchmode -runTests`. It needs
  ≥ 4.8 GB free RAM.
- Per take, print the lowest skinned vertex, shoulder rise and hand-to-grip distance against the infantry character on
  the same take.
- Review character captures with the project skill `pose-capture-review`.

## Project traps
- Only one Unity or Blender at a time on this PC; gate every launch.
- Player builds in batch need `-job-worker-count 2`.
- `output/enemy/boss/work/boss_skin.blend` carries 8 influences; limit to 4 and normalise on a copy before exporting.
- `docs/ART_DIRECTION.md` is outdated; don't treat it as a constraint.
