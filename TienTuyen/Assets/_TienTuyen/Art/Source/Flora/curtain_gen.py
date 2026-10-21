# curtain_gen.py: builds the vine curtain sources (curtain_geom.CURTAINS) for flora_build from curtain_geom's cards.
# blender -b --factory-startup -P curtain_gen.py -- OUTDIR   (writes OUTDIR/<name>.fbx)
# Each file holds <name>_LOD0 and <name>_LOD1 with the materials "Ivy" and "Root"; flora_build's EXT maps those names to
# the Jungle pack's ivy and monstera atlases.
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import curtain_geom  # noqa: E402

out = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out, exist_ok=True)

for name, height, width, seed in curtain_geom.CURTAINS:
    for block in (bpy.data.objects, bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)
    materials = {m: bpy.data.materials.new(m) for m in ("Ivy", "Root")}
    for lod in (0, 1):
        verts, quads, uvs, mats = curtain_geom.mesh(curtain_geom.curtain(seed, height, width, lod))
        me = bpy.data.meshes.new(f"{name}_LOD{lod}")
        me.from_pydata([tuple(v) for v in verts], [], quads)
        layer = me.uv_layers.new(name="UVMap")
        for poly, corners in zip(me.polygons, uvs):
            for li, uv in zip(poly.loop_indices, corners):
                layer.data[li].uv = uv
        order = ["Ivy", "Root"]
        for m in order:
            me.materials.append(materials[m])
        for poly, m in zip(me.polygons, mats):
            poly.material_index = order.index(m)
        me.update()
        obj = bpy.data.objects.new(f"{name}_LOD{lod}", me)
        bpy.context.scene.collection.objects.link(obj)
    bpy.ops.object.select_all(action='SELECT')
    # The same axes flora_build exports with, so its default import gives back these coordinates.
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='OFF')
    print("CURTAIN DONE", name, {o.name: len(o.data.polygons) for o in bpy.data.objects})
