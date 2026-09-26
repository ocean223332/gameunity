"""Render a quick visual QA sheet for the generated GLB catalog."""

from pathlib import Path

import bpy
from mathutils import Vector

root = Path(__file__).resolve().parent
names = ["hero", "rifle", "smg", "shotgun", "enemy_infantry", "enemy_shooter",
         "enemy_charger", "enemy_elite", "supply_crate", "crate", "sandbag",
         "tarp", "tree", "rock", "bush", "grass"]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

for i, name in enumerate(names):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(root / "GLB" / (name + ".glb")))
    imported = set(bpy.data.objects) - before
    x, y = (i % 4) * 3.1, (3 - i // 4) * 3.7
    for obj in imported:
        if obj.parent is None:
            obj.location.x += x
            obj.location.y += y
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, -.085))
    tile = bpy.context.object
    tile.name = name + " display tile"
    tile.dimensions = (2.9, 3.5, .16)
    tile.data.materials.append(bpy.data.materials.new("warm grey ground"))
    tile.active_material.diffuse_color = (.25, .25, .22, 1)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.text_add(location=(x + 1.15, y + 1.18, .005))
    label = bpy.context.object
    label.name = name + " label"
    label.data.body = name.replace("enemy_", "")
    label.data.size = .20
    label.data.extrude = .003
    label.rotation_euler.z = 3.141592653589793
    label.data.materials.append(bpy.data.materials.new("label charcoal"))
    label.active_material.diffuse_color = (.035, .04, .035, 1)
    label.active_material.use_nodes = True
    label.active_material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (.035, .04, .035, 1)

world = bpy.context.scene.world
world.color = (.45, .47, .46)
bpy.ops.object.light_add(type="AREA", location=(1, 17, 16))
bpy.context.object.data.energy = 3500
bpy.context.object.data.shape = "DISK"
bpy.context.object.data.size = 10
bpy.ops.object.camera_add(location=(4.65, 25.55, 34.64))
cam = bpy.context.object
direction = Vector((4.65, 5.55, 0)) - cam.location
cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
cam.data.type = "ORTHO"
cam.data.ortho_scale = 18.5
scene = bpy.context.scene
scene.camera = cam
scene.render.engine = "CYCLES"
scene.cycles.samples = 16
scene.render.resolution_x = 1600
scene.render.resolution_y = 1600
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = str(root / "catalog_preview.png")
bpy.ops.render.render(write_still=True)
