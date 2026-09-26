"""Build the Tien Tuyen P2 static low-poly art catalog with Blender.

Run: blender --background --python build_stylized_catalog.py
Source .blend files and interchange exports are written beside this script.
All figures are unrigged visual placeholders; Blender units are metres.
"""

import math
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parent
BLENDS = ROOT / "Blend"
GLBS = ROOT / "GLB"
FBXS = ROOT / "FBX"
for folder in (BLENDS, GLBS, FBXS):
    folder.mkdir(parents=True, exist_ok=True)
bpy.context.preferences.filepaths.save_version = 0

PALETTE = {
    "hero_cloth": "#8E9B66", "hero_dark": "#536548", "hero_light": "#B7BD87",
    "enemy_cloth": "#766C5E", "enemy_dark": "#46483E", "enemy_light": "#A99B7D",
    "elite_cloth": "#6C6252", "skin": "#B78763", "boot": "#342F2B",
    "metal": "#454C49", "metal_light": "#7A8179", "wood": "#765338",
    "wood_light": "#A27A50", "sand": "#A58D64", "sand_light": "#C6AF80",
    "tarp": "#647458", "leaf": "#354638", "leaf_light": "#596D46",
    "leaf_dark": "#24352D", "rock": "#77776C", "rock_light": "#A09D89",
    "supply": "#E7BD62", "supply_dark": "#9E7138", "danger": "#F17858",
}
MATS = {}


def material(name):
    if name in MATS and MATS[name].name in bpy.data.materials:
        return MATS[name]
    color = PALETTE[name].lstrip("#")
    rgb = [int(color[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    # sRGB swatches are converted to linear for faithful GLB/FBX base colors.
    rgba = tuple(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb) + (1,)
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = rgba
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Roughness"].default_value = .88
    bsdf.inputs["Metallic"].default_value = 0
    MATS[name] = mat
    return mat


def finish(obj, name, mat):
    obj.name = name
    obj.data.materials.append(material(mat))
    if obj.type == "MESH":
        for face in obj.data.polygons:
            face.use_smooth = False
    return obj


def box(name, center, dims, mat, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.dimensions = dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new("soft hard edges", "BEVEL")
        mod.width = bevel
        mod.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.modifiers.new("weighted corners", "WEIGHTED_NORMAL")
    return finish(obj, name, mat)


def sphere(name, center, scale, mat, segments=8, rings=4):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1, location=center)
    obj = bpy.context.object
    obj.scale = scale
    return finish(obj, name, mat)


def ico(name, center, scale, mat, subdivisions=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1, location=center)
    obj = bpy.context.object
    obj.scale = scale
    return finish(obj, name, mat)


def cylinder(name, center, radius, depth, mat, vertices=8, rotation=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=center)
    obj = bpy.context.object
    if rotation:
        obj.rotation_euler = rotation
    return finish(obj, name, mat)


def cone(name, center, r1, r2, depth, mat, vertices=7):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r1, radius2=r2, depth=depth, location=center)
    return finish(bpy.context.object, name, mat)


def segment(name, start, end, width, mat, vertices=8):
    a, b = Vector(start), Vector(end)
    obj = cylinder(name, (a + b) / 2, width, (b - a).length, mat, vertices)
    obj.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    return obj


def mesh(name, verts, faces, mat):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    return finish(obj, name, mat)


def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def person(role):
    hero = role == "hero"
    elite = role == "elite"
    cloth = "hero_cloth" if hero else "elite_cloth" if elite else "enemy_cloth"
    dark = "hero_dark" if hero else "enemy_dark"
    light = "hero_light" if hero else "enemy_light"
    # Asymmetric, broad upper silhouettes stay legible at CombatSpike's 60-degree
    # orthographic camera. +Y is the visible front in the source files.
    for side, x in (("L", -.17), ("R", .17)):
        spread = .13 if role == "charger" else 0
        box(side + " boot", (x * 1.15, .045, .105), (.20, .32, .21), "boot", .035)
        segment(side + " shin", (x * 1.15, 0, .24), (x, 0, .77), .09, cloth, 7)
        shoulder = ((-.38 if x < 0 else .38), .015, 1.42)
        hand = ((-.47 - spread if x < 0 else .47 + spread), .35 if role == "charger" else .19,
                1.02 if role == "charger" else .91)
        segment(side + " arm", shoulder, hand, .105 if role == "charger" else .098, cloth, 7)
        sphere(side + " hand", hand, (.10, .10, .105), "skin")
        box(side + " cuff", (hand[0] * .97, hand[1] * .86, hand[2] + .14),
            (.16, .13, .09), dark, .012)
    box("belt", (0, 0, .86), (.52, .3, .17), dark, .025)
    box("torso tunic", (0, .02 if role == "charger" else 0, 1.18),
        (.68 if elite else .60, .38, .64), cloth, .055)
    box("front webbing", (0, .23, 1.20), (.41, .075, .43), light, .018)
    box("backpack", (0, -.27, 1.23), (.44, .21, .53), dark, .035)
    segment("diagonal front strap", (-.19, .28, 1.49), (.20, .28, .98), .032, dark, 6)
    for x in (-.16, .16):
        box("belt pouch", (x, .22, .89), (.14, .12, .14), dark, .01)
    box("neck", (0, 0, 1.56), (.16, .15, .12), "skin")
    ico("head", (0, .015, 1.69), (.20, .18, .23), "skin", 1)
    box("face shadow", (0, .185, 1.69), (.23, .025, .07), dark, .008)
    if role in ("hero", "infantry"):
        sphere("soft helmet", (0, 0, 1.83), (.255, .23, .145), cloth, 10, 4)
        box("helmet brim", (0, .07, 1.79), (.51, .35, .055), dark, .02)
        box("helmet front band", (0, .235, 1.84), (.32, .04, .055), light, .006)
    elif role == "shooter":
        box("wide cap", (0, 0, 1.84), (.46, .36, .09), dark, .02)
        box("cap visor", (0, .22, 1.81), (.28, .28, .035), cloth, .01)
    elif role == "charger":
        sphere("light cap", (0, 0, 1.82), (.22, .21, .11), dark)
        for x in (-.34, .34):
            box("forward shoulder pad", (x, .12, 1.43), (.19, .25, .13), light, .025)
    else:
        sphere("elite helmet", (0, 0, 1.83), (.27, .24, .15), dark, 10, 4)
        box("helmet rim", (0, .05, 1.79), (.56, .38, .06), dark, .015)
        for x in (-.31, .31):
            box("elite shoulder pack", (x, -.04, 1.46), (.18, .35, .23), light, .02)
        box("chest plate", (0, .245, 1.24), (.43, .06, .34), "metal", .015)
    # Simple role markers belong to the visual placeholder, not gameplay state.
    if hero:
        box("bright chest chevron", (0, .279, 1.4), (.22, .035, .075), "supply")
        for x in (-.31, .31):
            box("hero shoulder yoke", (x, .08, 1.46), (.21, .34, .15), "hero_light", .025)
        box("rolled kit over backpack", (0, -.30, 1.59), (.50, .20, .16), "sand", .045)
    if role == "infantry":
        box("right field pouch", (.37, -.04, 1.04), (.17, .27, .23), "wood", .025)
        segment("slung tool", (-.41, -.07, 1.50), (-.42, .23, .73), .045, "wood", 7)
    if role == "shooter":
        box("rear ammunition case", (.25, -.30, 1.04), (.22, .19, .35), "wood")
        # A long, offset rifle makes this role readable even in a tiny top view.
        segment("shooter rifle barrel", (.45, -.10, 1.18), (.45, .74, 1.18), .049, "metal", 8)
        box("shooter rifle stock", (.45, -.21, 1.13), (.15, .35, .18), "wood", .013)
        box("shooter muzzle highlight", (.45, .70, 1.18), (.12, .09, .13), "metal_light", .008)
    if role == "charger":
        box("front satchel", (0, .30, .98), (.36, .17, .30), "wood", .02)
        box("forward helmet stripe", (0, .16, 1.88), (.16, .19, .035), "danger")
        for x in (-.38, .38):
            box("charge shoulder flash", (x, .22, 1.47), (.13, .12, .11), "danger", .015)
    if elite:
        box("elite shoulder stripe", (0, .275, 1.55), (.37, .035, .07), "danger")
        box("elite back radio", (-.22, -.36, 1.45), (.21, .16, .33), "metal", .014)
        segment("elite antenna", (-.22, -.36, 1.53), (-.22, -.36, 1.95), .014, "metal_light", 6)


def gun(kind):
    # Grip center is the object origin; muzzle points along +Y. Build at a
    # readable modeling scale, then reduce to a plausible handheld length.
    if kind == "rifle":
        box("receiver", (0, .13, .09), (.18, .43, .18), "metal", .014)
        box("long vented handguard", (0, .46, .08), (.20, .35, .14), "wood", .012)
        segment("barrel", (0, .53, .10), (0, .98, .10), .042, "metal", 8)
        box("butt stock", (0, -.31, .04), (.24, .42, .15), "wood", .025)
        box("stock neck", (0, -.105, .06), (.13, .18, .12), "wood_light", .01)
        magazine = box("swept magazine", (0, .20, -.12), (.12, .17, .30), "metal_light", .008)
        magazine.rotation_euler.x = -.28
        box("grip", (0, -.06, -.115), (.10, .11, .23), "wood", .01)
        box("top rear sight", (0, .03, .21), (.12, .06, .07), "metal_light", .004)
        box("front sight", (0, .89, .19), (.06, .045, .14), "metal")
        box("muzzle brake", (0, .99, .10), (.13, .08, .11), "metal_light", .007)
    elif kind == "smg":
        box("compact receiver", (0, .10, .08), (.23, .43, .20), "metal", .018)
        box("short heat shield", (0, .37, .10), (.20, .21, .16), "metal_light", .012)
        segment("short barrel", (0, .38, .09), (0, .67, .09), .048, "metal", 8)
        box("folded side stock", (-.13, -.24, .12), (.045, .40, .055), "metal_light")
        box("wire stock end", (-.13, -.43, .01), (.055, .05, .24), "metal_light")
        box("vertical magazine", (0, .15, -.18), (.125, .13, .38), "metal_light", .006)
        box("grip", (0, -.10, -.12), (.11, .12, .26), "wood", .01)
        box("raised top sight", (0, .15, .23), (.09, .11, .065), "sand", .005)
        box("short muzzle", (0, .65, .09), (.15, .08, .12), "metal", .008)
    else:
        box("shotgun receiver", (0, .06, .08), (.20, .37, .19), "metal", .012)
        box("wide pump", (0, .43, .05), (.27, .33, .15), "wood", .019)
        segment("heavy barrel", (0, .31, .14), (0, .99, .14), .055, "metal", 10)
        segment("underslung tube", (0, .31, .025), (0, .84, .025), .043, "metal_light", 8)
        box("broad butt stock", (0, -.36, .035), (.25, .54, .17), "wood", .025)
        box("grip", (0, -.09, -.11), (.11, .13, .24), "wood", .01)
        for y in (.17, .29, .41):
            box("shell band", (.13, y, .14), (.045, .045, .11), "supply_dark", .004)
        box("muzzle ring", (0, .98, .14), (.16, .07, .14), "metal_light", .006)
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            obj.location *= .72
            obj.scale *= .72


def crate(supply=False):
    base = "supply" if supply else "wood"
    slat = "supply_dark" if supply else "wood_light"
    box("crate body", (0, 0, .36), (.78, .66, .72), base, .025)
    for z in (.10, .61):
        for y in (-.345, .345):
            box("horizontal side slat", (0, y, z), (.82, .045, .09), slat, .006)
        for x in (-.405, .405):
            box("horizontal end slat", (x, 0, z), (.045, .70, .09), slat, .006)
    for x in (-.33, .33):
        box("lid slat", (x, 0, .75), (.1, .72, .055), slat, .006)
    if supply:
        box("front supply panel", (0, .37, .38), (.37, .023, .34), "supply_dark")
        box("front supply vertical mark", (0, .387, .38), (.065, .015, .27), "supply")
        box("front supply horizontal mark", (0, .387, .38), (.25, .015, .065), "supply")


def sandbag():
    for row in range(3):
        count = 3 if row != 1 else 2
        for i in range(count):
            x = (i - (count - 1) / 2) * (.54 if row != 1 else .67)
            y = -.13 if row == 1 else 0
            box("stacked sandbag", (x, y, .115 + row * .20), (.59, .43, .20),
                "sand_light" if (i + row) % 3 == 0 else "sand", .08)


def tarp():
    # Two roof slopes form a separate opaque mesh, suitable for roof fading in Unity.
    for x in (-1.05, 1.05):
        for y in (-.75, .75):
            segment("support pole", (x, y, 0), (x, y, 1.85), .052, "wood", 7)
    segment("ridge pole", (0, -.87, 2.26), (0, .87, 2.26), .045, "wood", 7)
    canopy = mesh("separate tarp canopy",
                  [(-1.13, -.85, 1.81), (0, -.85, 2.30), (1.13, -.85, 1.81),
                   (-1.13, .85, 1.81), (0, .85, 2.30), (1.13, .85, 1.81)],
                  [(0, 3, 4, 1), (1, 4, 5, 2)], "tarp")
    solid = canopy.modifiers.new("opaque double-sided fabric", "SOLIDIFY")
    solid.thickness = .035
    bpy.context.view_layer.objects.active = canopy
    bpy.ops.object.modifier_apply(modifier=solid.name)
    for y in (-.85, .85):
        segment("edge cord", (-1.13, y, 1.8), (1.13, y, 1.8), .013, "sand", 6)


def tree():
    cone("olive trunk", (0, 0, .87), .19, .12, 1.74, "wood", 7)
    for x, y, z, sx, sy, sz, mat in [
        (-.36, -.05, 2.07, .9, .76, .62, "leaf"),
        (.42, .12, 2.18, .93, .78, .64, "leaf_dark"),
        (0, -.10, 2.62, .77, .68, .59, "leaf_light"),
    ]:
        ico("separate olive canopy", (x, y, z), (sx, sy, sz), mat, 1)


def rock():
    ico("faceted rock", (0, 0, .40), (.78, .57, .49), "rock", 1)
    ico("rock light facet", (-.30, .10, .59), (.37, .34, .23), "rock_light", 1)


def bush():
    for x, y, z, scale, mat in [
        (-.33, 0, .32, (.44, .39, .36), "leaf_dark"),
        (.27, -.08, .38, (.49, .44, .43), "leaf"),
        (0, .22, .52, (.45, .35, .42), "leaf_light"),
    ]:
        ico("bush clump", (x, y, z), scale, mat, 1)


def grass():
    for i, (x, y, h) in enumerate([(-.32, -.11, .48), (-.18, .15, .59),
                                   (.02, -.06, .54), (.21, .15, .47), (.34, -.14, .38)]):
        mesh("solid grass blade", [(x - .08, y, 0), (x + .08, y, 0), (x, y + .05, h),
                                    (x, y - .05, h)],
             [(0, 1, 2), (1, 0, 3)], "leaf_light" if i % 2 else "leaf")


ASSETS = {
    "hero": lambda: person("hero"),
    "rifle": lambda: gun("rifle"),
    "smg": lambda: gun("smg"),
    "shotgun": lambda: gun("shotgun"),
    "enemy_infantry": lambda: person("infantry"),
    "enemy_shooter": lambda: person("shooter"),
    "enemy_charger": lambda: person("charger"),
    "enemy_elite": lambda: person("elite"),
    "supply_crate": lambda: crate(True),
    "crate": lambda: crate(False),
    "sandbag": sandbag,
    "tarp": tarp,
    "tree": tree,
    "rock": rock,
    "bush": bush,
    "grass": grass,
}


def export(name, builder):
    reset()
    builder()
    # A single explicit root gives Unity a stable model pivot at (0, 0, 0),
    # independent of the bounds or order of the component meshes.
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=(0, 0, 0))
    pivot = bpy.context.object
    pivot.name = "TT_" + name + "_PIVOT"
    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH":
            world = obj.matrix_world.copy()
            obj.parent = pivot
            obj.matrix_world = world
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = pivot
    bpy.ops.wm.save_as_mainfile(filepath=str(BLENDS / f"{name}.blend"))
    bpy.ops.export_scene.gltf(filepath=str(GLBS / f"{name}.glb"), export_format="GLB",
                              use_selection=True, export_apply=True)
    bpy.ops.export_scene.fbx(filepath=str(FBXS / f"{name}.fbx"), use_selection=True,
                             apply_unit_scale=True, add_leaf_bones=False,
                             axis_forward="-Z", axis_up="Y")
    print(f"EXPORTED {name}: {len(bpy.context.selected_objects)} objects")


for asset_name, build in ASSETS.items():
    export(asset_name, build)

print(f"CATALOG COMPLETE: {len(ASSETS)} assets in {ROOT}")
