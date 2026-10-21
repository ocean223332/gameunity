# Commander asset integration, phase 1

Branch: codex/commander-boss, based on main 224fd8f.

The user explicitly approved the shirtless muscular soldier GLB as the boss after reviewing its rendered appearance. The supplied RPG-18 GLB is the boss weapon. Original input files must remain untouched.

## Current deliverable
- Normalize the boss to metres, feet at zero and forward +Z in Unity.
- Generate a Humanoid skeleton, automatic skin weights, validate deformation and retarget the existing idle/run/aim clips.
- Simplify the 1,499,044-triangle RPG-18 to a maximum 25,000 triangles, preserve UV/PBR textures and create muzzle/rear sockets.
- Create URP materials, prefabs, character profile and a separate review scene. Existing enemies remain unchanged during asset verification.
- Verify the avatar, weights, clips, weapon geometry and visual previews before gameplay integration.

## Following gameplay work
Wave-5 boss carries the RPG-18. Defeating it unlocks the weapon and eligible weapon upgrades. The commander retreats and returns in the M113 at wave 10. Full 10-wave, airstrike, cutscene, rewards and speedrun design remains as agreed in the conversation; this asset milestone does not claim those systems are implemented.

## Inputs on this machine
- Boss: C:/Users/Admin/Downloads/032fbd58e2f7249c376046b745b8d32b.glb.glb
- RPG-18: D:/Move3/e76641ff0cb9857121dbb9d2bdbcb40e.glb

The weapon is a gameplay interpretation of the user-requested RPG-18, not a claim of historical issue to US troops.

## Rebuild and review

Blender 5.2 was used. From the repository root:

```powershell
D:\Blender\blender.exe --background --python tools/boss_assets/build_assets.py -- --boss <boss.glb> --weapon <rpg18.glb> --output output/commander-assets
```

In Unity use `Tien Tuyen > Characters > Import Commander Assets`, or batch-execute `TienTuyen.Editor.CommanderAssetImporter.Import`. The menu checks for unsaved scenes before creating the review scene. If the generated directory is unavailable, the committed normalized geometry JSON and textures can recreate the assets. Original GLBs are never modified. The .blend and FBX export are retained only in the ignored output directory as optional interchange files; Unity uses normalized geometry and explicit skin bind matrices to avoid FBX axis ambiguity.

Open `TienTuyen/Assets/_TienTuyen/Scenes/CommanderReview.unity` and enter Play mode to preview existing idle, run and aim clips on three copies. RPG18 is displayed separately; two-hand shoulder fitting, launcher-specific animations and combat integration are not implemented in this milestone. The model has 22 body bones and no individual finger rig; its source hands are already curled. Source mesh is welded before heat weighting, capped to four influences per vertex, then converted to Unity coordinates with a T-pose avatar reference.

## Verification, 2026-10-19

- Red: CommanderAssetTests failed 0/2 before imported assets existed.
- Green: CommanderAssetTests passed 2/2 after import.
- Generated rig: 22 bones, 1.85m body, zero unweighted vertices.
- RPG18: 1,499,044 source triangles -> 24,000 game triangles, normalized length 1.05m.
- Unity import exited 0; preview `Builds/CommanderReview/poses.png` was inspected after correcting source quaternion orientation.
- Static review identified a dirty-scene loss risk; importer now respects save/cancel before replacing the active scene.

The current combat scene and existing enemy profiles were not changed. No claim is made that the wave-5 fight, unlock rewards, weapon upgrades or the ten-wave campaign are implemented yet.
