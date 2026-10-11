"""Round-trip FBX geometry, pivots, and scale through Blender's FBX importer: the low-poly catalog
in FBX/ and the realistic weapons and props in ../Resources/Models."""

from pathlib import Path

import bpy
from mathutils import Vector

root = Path(__file__).resolve().parent
names = ["hero", "enemy_infantry", "enemy_shooter", "enemy_charger", "enemy_elite",
         "tarp", "tree", "bush", "grass"]

for name in names:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(root / "FBX" / (name + ".fbx")))
    bpy.context.view_layer.update()
    pivot = bpy.data.objects.get("TT_" + name + "_PIVOT")
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    assert pivot is not None, name + ": missing explicit root"
    assert meshes, name + ": no mesh"
    assert pivot.location.length < .0001, name + ": root moved"
    assert all(abs(s - 1) < .0001 for s in pivot.scale), name + ": root scaled"
    assert all(abs(a) < .0001 for a in pivot.rotation_euler), name + ": root rotated"
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    lo = [min(point[i] for point in points) for i in range(3)]
    hi = [max(point[i] for point in points) for i in range(3)]
    dims = [hi[i] - lo[i] for i in range(3)]
    triangles = sum(sum(len(face.vertices) - 2 for face in obj.data.polygons) for obj in meshes)
    if name.startswith("enemy_") or name == "hero":
        # The back banner rises above the figure; measure the body without it.
        body = [obj.matrix_world @ Vector(corner) for obj in meshes
                if not obj.name.startswith("banner") for corner in obj.bound_box]
        body_height = max(p[2] for p in body) - min(p[2] for p in body)
        assert 1.85 <= body_height <= 2.1, name + ": character height changed"
        assert -.01 <= lo[2] <= .01, name + ": feet not grounded"
        parts = {obj.name.split(".")[0] for obj in meshes}
        flag = {"banner cloth red", "banner cloth star"} if name == "hero" else             {"banner cloth red stripes", "banner cloth white stripes", "banner cloth blue canton", "banner cloth stars"}
        assert flag <= parts, name + ": faction banner missing " + str(flag - parts)
        assert "banner pole" in parts, name + ": banner pole missing"
        assert hi[2] <= 2.7, name + ": banner too tall"
    print("VERIFY", name, "root", tuple(round(v, 4) for v in pivot.location),
          "dims", tuple(round(v, 3) for v in dims), "tris", triangles)

print("VERIFY COMPLETE", len(names), "FBX files")

# Realistic catalog: CombatCharacterRig.ConfigureWeapon finds the hands' grip, support and muzzle by
# part name; the hero weapons also need "barrel" and "receiver" for the shot-direction test.
# Budgets: at most 15k triangles near, and each "<part>_LOD1" at most 40% of its part.
weapons = {"rifle": {"grip", "handguard", "muzzle", "barrel", "receiver"},
           "smg": {"grip", "handguard", "muzzle", "barrel", "receiver"},
           "shotgun": {"grip", "pump handguard", "muzzle", "barrel", "receiver"},
           "m16": {"grip", "handguard", "muzzle", "receiver"},
           "etool": {"grip", "handguard", "spade blade"}}
props = ["supply_crate", "crate", "sandbag", "drum", "rock", "shed", "watchtower", "cookfire",
         "bunker", "truck", "truck_zil", "aa_gun", "bicycle"]
models = root.parent / "Resources" / "Models"
for name in list(weapons) + props:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(models / (name + ".fbx")))
    bpy.context.view_layer.update()
    pivot = bpy.data.objects.get("TT_" + name + "_PIVOT")
    assert pivot is not None, name + ": missing explicit root"
    assert pivot.location.length < .0001 and all(abs(a) < .0001 for a in pivot.rotation_euler), name + ": root moved"
    parts = {obj.name.split(".")[0]: obj for obj in bpy.context.scene.objects if obj.type == "MESH"}
    count = {part: sum(len(face.vertices) - 2 for face in obj.data.polygons) for part, obj in parts.items()}
    near = [part for part in parts if not part.endswith("_LOD1")]
    # The sandbag's muddy bottom-course copies replace bags, they do not add to them.
    budget = sum(count[part] for part in near if not part.endswith("_ground"))
    assert budget <= 15000, name + ": over 15k triangles"
    for part in near:
        far = count.get(part + "_LOD1")
        assert far is None or far <= .4 * count[part], part + ": LOD1 over 40%"
    if name in weapons:
        assert weapons[name] <= set(near), name + ": missing parts " + str(weapons[name] - set(near))
        points = [obj.matrix_world @ Vector(corner) for obj in parts.values() for corner in obj.bound_box]
        lo = [min(point[i] for point in points) for i in range(3)]
        hi = [max(point[i] for point in points) for i in range(3)]
        # Authored for the attach scale: x.72 on the hero, x.58 x.9 on enemies (m16 0.99 m in hand).
        assert .6 <= hi[1] - lo[1] <= 2.0 and lo[1] < 0 < hi[1], name + ": length or grip pivot changed"
    else:
        assert all(part + "_LOD1" in count for part in near), name + ": missing LOD1"
    print("VERIFY", name, "tris", budget, "parts", len(near))

print("VERIFY COMPLETE", len(weapons) + len(props), "realistic FBX files")
