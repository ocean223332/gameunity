"""Round-trip FBX geometry, pivots, and scale through Blender's FBX importer."""

from pathlib import Path

import bpy
from mathutils import Vector

root = Path(__file__).resolve().parent
names = ["hero", "rifle", "smg", "shotgun", "enemy_infantry", "enemy_shooter",
         "enemy_charger", "enemy_elite", "supply_crate", "crate", "sandbag",
         "tarp", "tree", "rock", "bush", "grass"]

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
        assert 1.85 <= dims[2] <= 2.1, name + ": character height changed"
        assert -.01 <= lo[2] <= .01, name + ": feet not grounded"
    if name in ("rifle", "smg", "shotgun"):
        assert .7 <= dims[1] <= 1.8, name + ": weapon length changed"
        assert lo[1] < 0 < hi[1], name + ": grip pivot moved"
    print("VERIFY", name, "root", tuple(round(v, 4) for v in pivot.location),
          "dims", tuple(round(v, 3) for v in dims), "tris", triangles)

print("VERIFY COMPLETE", len(names), "FBX files")
