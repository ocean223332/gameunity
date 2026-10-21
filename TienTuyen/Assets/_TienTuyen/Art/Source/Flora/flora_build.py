# Background Blender: builds one flora species for Unity -> <out>/<Species>/<Species>.fbx with
# <Species>_LOD0/_LOD1 (source meshes) and <Species>_LOD2 (cross-card impostor), the textures
# each material needs (<Material>_Albedo.png, optional <Material>_Normal.png) and <Species>.json.
#
# Vertex colour (linear): R wind weight, G wind phase, B leaf flutter, A occlusion.
# usage: blender -b --factory-startup <env_stage1_sources.blend> -P flora_build.py -- <species> <out_root> <polyhaven_dir> <asset_dir>
# <asset_dir> holds the pine-woodland downloads (output/assets) the EXT species read.
import bpy, bmesh, sys, os, json, math, random
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SPECIES, OUT_ROOT, PH_DIR, ASSET_DIR = argv[:4]
OUT = os.path.join(OUT_ROOT, SPECIES)
os.makedirs(OUT, exist_ok=True)
scene = bpy.context.scene
for c in bpy.data.collections:
    if c.name.startswith("SRC_") and c.name not in scene.collection.children:
        scene.collection.children.link(c)
for o in list(scene.collection.objects):
    bpy.data.objects.remove(o)


# ---------------------------------------------------------------- helpers

def override(objs, active):
    return bpy.context.temp_override(selected_objects=objs, selected_editable_objects=objs, active_object=active, object=active)


def meshes_under(o):
    r = [o] if o.type == 'MESH' else []
    for c in o.children:
        r += meshes_under(c)
    return r


def bake_copy(objs, name):
    """Joined copy of objs with transforms applied (sources stay untouched)."""
    copies = []
    for o in objs:
        c = o.copy()
        c.data = o.data.copy()
        c.parent = None
        c.matrix_world = o.matrix_world.copy()
        scene.collection.objects.link(c)
        c.data.transform(c.matrix_world)
        c.matrix_world = Matrix.Identity(4)
        copies.append(c)
    if len(copies) > 1:
        with override(copies, copies[0]):
            bpy.ops.object.join()
    obj = copies[0]
    obj.name = name
    obj.data.name = name
    return obj


def source_group(name):
    o = bpy.data.objects[name]
    return meshes_under(o)


def import_gltf(path, node_names):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    keep = [o for o in new if o.type == 'MESH' and o.name.split(".")[0] in node_names]
    return keep


def normalize(obj, height=None, scale=None):
    """Origin at the base centre, sized to height (m) or by scale."""
    me = obj.data
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    s = scale if scale else height / (max(zs) - min(zs))
    off = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, min(zs)))
    me.transform(Matrix.Scale(s, 4) @ Matrix.Translation(-off))
    return s


def decimated(obj, ratio, name):
    m = obj.modifiers.new("dec", 'DECIMATE')
    m.ratio = ratio
    m.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    obj.modifiers.remove(m)
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return o


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def material_info(mat):
    """(base colour image, alpha used?, normal image) of a source material."""
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    base = normal = None
    cut = False
    if bsdf:
        def upstream_image(sock):
            stack = [l.from_node for l in sock.links]
            while stack:
                n = stack.pop()
                if n.type == 'TEX_IMAGE' and n.image:
                    return n.image
                for s in n.inputs:
                    stack += [l.from_node for l in s.links]
            return None
        base = upstream_image(bsdf.inputs["Base Color"])
        cut = bool(bsdf.inputs["Alpha"].links) or getattr(mat, "blend_method", "OPAQUE") in ("CLIP", "HASHED", "BLEND")
        normal = upstream_image(bsdf.inputs["Normal"])
    return base, cut, normal


def save_image(img, path, max_size, non_color=False, tint=None):
    w, h = img.size
    # A fresh image from the pixels: copies of file-backed images carry no data of their own.
    copy = bpy.data.images.new(img.name + "_out", w, h, alpha=True)
    if non_color:
        copy.colorspace_settings.name = 'Non-Color'
    px = np.array(img.pixels[:], np.float32)
    if callable(tint):
        px = tint(px.reshape(h, w, 4)).ravel()
    elif tint:
        # Recolour towards tint at the texel's own brightness, keeping the texture's detail.
        colour, amount = tint
        rgba = px.reshape(-1, 4)
        lum = rgba[:, :3] @ np.array((.3, .59, .11), np.float32)
        target = lum[:, None] * np.array(colour, np.float32) / float(np.dot(colour, (.3, .59, .11)))
        rgba[:, :3] = np.clip(rgba[:, :3] * (1 - amount) + target * amount, 0, 1)
    copy.pixels[:] = px
    f = min(1.0, max_size / max(w, h))
    if f < 1.0:
        copy.scale(max(4, int(w * f)), max(4, int(h * f)))
    copy.filepath_raw = path
    copy.file_format = 'PNG'
    copy.save()
    has_alpha = False
    if copy.channels == 4:
        a = np.array(copy.pixels[:], np.float32)[3::4]
        has_alpha = bool((a < 0.5).mean() > 0.02)
    bpy.data.images.remove(copy)
    return has_alpha


def islands(me):
    """Connected face groups of a mesh: list of face index lists."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen = set()
    groups = []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack = [f]; seen.add(f.index); g = []
        while stack:
            cur = stack.pop(); g.append(cur.index)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.index not in seen:
                        seen.add(nf.index); stack.append(nf)
        groups.append(g)
    bm.free()
    return groups


def thin_leaves(obj, keep, leaf_slots, rng):
    """LOD1 for card-built foliage: drop leaf islands, grow the survivors to keep the silhouette."""
    me = obj.data
    groups = islands(me)
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    kill = set()
    grow = 1.0 / math.sqrt(keep) ** 0.8
    for g in groups:
        if bm.faces[g[0]].material_index not in leaf_slots:
            continue
        if rng.random() > keep:
            kill.update(g)
        else:
            verts = {v for i in g for v in bm.faces[i].verts}
            c = sum((v.co for v in verts), Vector()) / len(verts)
            for v in verts:
                v.co = c + (v.co - c) * grow
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in kill], context='FACES')
    bm.to_mesh(me); bm.free()


def paint(obj, leaf_slots, stiff, sphere, rng, ao_floor=0.45, rigid=False, hang=False):
    """Wind/occlusion colours and leaf normals bent towards a sphere around the foliage mass. Rigid: no wind.
    Hang: the plant hangs from its top (vines over a ledge), so the wind grows downwards."""
    me = obj.data
    zs = [v.co.z for v in me.vertices]
    H = max(max(zs), 1e-3)
    leafy = [v.co for p in me.polygons if p.material_index in leaf_slots for v in (me.vertices[i] for i in p.vertices)]
    if leafy:
        lo = Vector([min(c[i] for c in leafy) for i in range(3)]); hi = Vector([max(c[i] for c in leafy) for i in range(3)])
        centre = (lo + hi) / 2; radii = Vector([max((hi[i] - lo[i]) / 2, 0.05) for i in range(3)])
    else:
        centre = Vector((0, 0, H / 2)); radii = Vector((1, 1, H / 2))
    # Only our layer: Unity reads one vertex colour set, and a pack's own (AO, masks) would shadow it.
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    col = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER')
    phase = {}
    for g in islands(me):
        ph = rng.random()
        for i in g:
            phase[i] = ph
    normals = []
    for p in me.polygons:
        leaf = p.material_index in leaf_slots
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            h = max(v.z, 0) / H
            weight = 0.0 if rigid else (1 - h if hang else h) ** stiff
            rel = Vector(((v.x - centre.x) / radii.x, (v.y - centre.y) / radii.y, (v.z - centre.z) / radii.z))
            depth = min(rel.length, 1.0)
            ao = ao_floor + (1 - ao_floor) * (depth if leaf else min(1.0, 0.6 + 0.4 * h))
            col.data[li].color = (weight, phase.get(p.index, 0.0), 1.0 if leaf else 0.0, ao)
            n = Vector(p.normal) if not me.has_custom_normals else Vector(me.corner_normals[li].vector)
            if leaf and sphere > 0 and rel.length > 1e-4:
                out = rel.normalized()
                if n.dot(out) < 0:
                    n = -n
                n = (n * (1 - sphere) + out * sphere).normalized()
            normals.append(n)
    me.normals_split_custom_set(normals)
    me.color_attributes.active_color = col


# ---------------------------------------------------------------- impostor cards

def emission_copy(mat):
    base, cut, _ = material_info(mat) if mat.node_tree else (None, False, None)
    m = bpy.data.materials.new(mat.name + "_imp")
    m.use_nodes = True
    try:
        m.surface_render_method = 'DITHERED'
    except Exception:
        pass
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    attr = nt.nodes.new("ShaderNodeAttribute"); attr.attribute_name = "Col"
    occl = nt.nodes.new("ShaderNodeMath"); occl.operation = 'MULTIPLY_ADD'
    occl.inputs[1].default_value = 0.35; occl.inputs[2].default_value = 0.65
    nt.links.new(attr.outputs["Alpha"], occl.inputs[0])
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
    nt.links.new(occl.outputs[0], mul.inputs[7])
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    if base:
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = base
        nt.links.new(tex.outputs[0], mul.inputs[6])
        if cut:
            nt.links.new(tex.outputs[1], mix.inputs[0])
        else:
            mix.inputs[0].default_value = 1.0
    else:
        mul.inputs[6].default_value = mat.diffuse_color
        mix.inputs[0].default_value = 1.0
    nt.links.new(mul.outputs[2], emit.inputs[0])
    nt.links.new(tr.outputs[0], mix.inputs[1]); nt.links.new(emit.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
    return m


def bleed(px, steps=32):
    rgb = px[:, :, :3].copy(); a = px[:, :, 3]
    cov = a > 0.3
    fill = rgb.copy()
    fill[cov] = rgb[cov] / np.maximum(a[cov], 1e-3)[:, None]
    known = cov.copy()
    for _ in range(steps):
        m = known.astype(np.float32)
        acc = np.zeros_like(rgb); cnt = np.zeros(a.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(np.roll(fill * m[..., None], dy, 0), dx, 1); cnt += np.roll(np.roll(m, dy, 0), dx, 1)
        grow = (~known) & (cnt > 0)
        fill[grow] = acc[grow] / cnt[grow][:, None]; known |= grow
    fill[~known] = rgb[cov].mean(axis=0) if cov.any() else (0.2, 0.25, 0.1)
    return np.concatenate([np.clip(fill, 0, 1), a[..., None]], axis=2)


def impostor(src, tile):
    """Two orthographic views of src into <Species>_Card_Albedo.png and the crossed quads that show them."""
    for o in scene.objects:
        o.hide_render = o is not src
    saved = list(src.data.materials)
    for i, m in enumerate(saved):
        src.data.materials[i] = emission_copy(m)
    try:
        scene.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        pass
    scene.render.resolution_x = tile; scene.render.resolution_y = tile
    scene.render.film_transparent = True
    scene.view_settings.view_transform = 'Standard'
    scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'
    try:
        scene.eevee.taa_render_samples = 24
    except Exception:
        pass
    cam = bpy.data.objects.new("ImpCam", bpy.data.cameras.new("ImpCam"))
    scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = 'ORTHO'
    vs = [v.co for v in src.data.vertices]
    lo = Vector([min(c[i] for c in vs) for i in range(3)]); hi = Vector([max(c[i] for c in vs) for i in range(3)])
    c = (lo + hi) / 2; ext = hi - lo
    atlas = np.zeros((tile, tile * 2, 4), np.float32)
    views = []
    for k, (d, horiz) in enumerate(((Vector((0, 1, 0)), 0), (Vector((-1, 0, 0)), 1))):
        s = max(ext[horiz], ext.z) * 1.02
        cam.data.ortho_scale = s
        cam.location = c - d * (ext.length + 5)
        cam.data.clip_end = (ext.length + 5) * 3
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        path = os.path.join(OUT, f"_imp{k}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        atlas[:, k * tile:(k + 1) * tile] = np.array(img.pixels[:], np.float32).reshape(tile, tile, 4)
        bpy.data.images.remove(img); os.remove(path)
        views.append(s)
    for i, m in enumerate(saved):
        src.data.materials[i] = m
    bpy.data.objects.remove(cam)
    global CARD_EMPTY
    # Mountain Trees' firs render nothing here (cause unknown); their prefab then keeps LOD1 instead of the card.
    CARD_EMPTY = not (atlas[:, :, 3] > 0.3).any()
    if CARD_EMPTY:
        print("CARD EMPTY", SPECIES)
    px = bleed(atlas)
    img = bpy.data.images.new("card", tile * 2, tile, alpha=True)
    img.pixels[:] = px.ravel()
    name = SPECIES + "_Card"
    img.filepath_raw = os.path.join(OUT, name + "_Albedo.png"); img.file_format = 'PNG'; img.save()
    # Crossed quads matching the two views.
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    for k, s in enumerate(views):
        horiz = 0 if k == 0 else 1
        half = ext[horiz] / 2
        corners = []
        for a, z in ((-half, lo.z), (half, lo.z), (half, hi.z), (-half, hi.z)):
            p = Vector((c.x + a, c.y, z)) if k == 0 else Vector((c.x, c.y + a, z))
            u = 0.5 + a / s
            v = 0.5 + (z - c.z) / s
            corners.append((p, ((k + u) / 2, v)))
        f = bm.faces.new([bm.verts.new(p) for p, _ in corners])
        for loop, (_, uv) in zip(f.loops, corners):
            loop[uvl].uv = uv
    me = bpy.data.meshes.new(SPECIES + "_LOD2")
    bm.to_mesh(me); bm.free()
    card_mat = bpy.data.materials.new(name)
    me.materials.append(card_mat)
    obj = bpy.data.objects.new(SPECIES + "_LOD2", me)
    scene.collection.objects.link(obj)
    # Cards light like a round mass: normals point out from the middle of the plant.
    col = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER')
    normals = []
    mid = Vector((c.x, c.y, lo.z + ext.z * 0.45))
    for p in me.polygons:
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            h = max(v.z - lo.z, 0) / max(ext.z, 1e-3)
            col.data[li].color = (h ** 1.5 * 0.7, 0.5, 0.0, 1.0)
            out = Vector(((v.x - mid.x) / max(ext.x, .1), (v.y - mid.y) / max(ext.y, .1), (v.z - mid.z) / max(ext.z, .1) * 0.7))
            n = Vector(p.normal)
            normals.append((out.normalized() * 0.7 + Vector((0, 0, 0.5))).normalized() if out.length > 1e-4 else Vector((0, 0, 1)))
    me.normals_split_custom_set(normals)
    me.color_attributes.active_color = col
    return obj


# ---------------------------------------------------------------- species table

SKF = 0.01  # Sketchfab exports in centimetres
TP = {  # name: (source groups, target height m, leaf material keywords, sphere blend, stiffness, LOD1 mode)
    "Banana_A": (["SM_MZRa_Banana_B091"], 3.6, ["B09a"], 0.25, 1.3, "decimate"),
    "Palm_A": (["SM_MZRa_Palm_B081"], 9.0, ["B08a"], 0.3, 2.2, "decimate"),
}
PH = {  # name: (gltf asset, nodes, target height, sphere blend, stiffness, LOD0 ratio, LOD1 ratio, leafy)
    "FernSmall": ("fern_02", ["fern_02_a"], 0.7, 0.4, 1.0, 1.0, 0.45, True),
    # A 3 m fallen log scan; negative height = keep real size times this scale. Stiffness 40: it does not sway.
    "Log": ("dead_tree_trunk", ["dead_tree_trunk"], -1.25, 0.0, 40.0, 0.02, 0.006, False),
}
# Pine woodland, from the downloads in <asset_dir> (credits in docs/CREDITS.md). Keys:
#   src: file under asset_dir; lod0: name prefixes of the source objects joined into LOD0;
#   lod1: name prefixes of a source LOD, "thin" (drop leaf cards, decimate the bark by r1) or None (decimate all by r1);
#   h: height in m to scale to (negative: keep the real size times -h); base: m sunk below the ground (roots, scan bases);
#   leaf: material name keywords of the foliage, or "all"; r0: LOD0 decimate ratio of the bark/solid parts;
#   rigid: no wind (stones, wood);
#   bark: (colour, amount) recolour of the non-leaf textures; tex: largest texture size;
#   src1: a second file holding the lod1 objects; maps: albedo/normal files that replace the source materials
#   (cutout: albedo alpha is coverage; crop: the mesh uses part of a shared atlas); moss: baked moss on the upper faces;
#   mats: {keyword: maps} one material per source material whose name holds the keyword ("*": the rest), for packs with
#   bark and leaves in separate engine materials; the keywords name the materials, so leaf lists keywords of mats;
#   hang: hangs from its top, wind grows downwards; the scatter sinks it by its height under a ledge's lip;
#   leaf_mul: (r, g, b) multiplies the leaf albedo, for packs whose shader tints a grey leaf atlas;
#   look: leaf material settings for FloraPrefabBuilder (cutoff, smoothness, translucency, tint), from how the pack's own
#   materials set them.
KESIYA_BARK = ((0.58, 0.36, 0.26), 0.55)
# Kesiya needles: deep green (linear), the opaque texels' mean moved onto it with the texture's detail kept.
KESIYA_NEEDLES = ((0.055, 0.10, 0.03), 0.85)
EXT_DEFAULT = dict(lod1=None, base=0.0, leaf=(), sphere=0.0, stiff=40.0, r0=1.0, r1=0.35, keep=0.45, rigid=False, bark=None, tex=1024,
                   wreck=False, moss=False, src1=None, maps=None, needles=None, mats=None, hang=False, look=None, leaf_mul=None)
LOLIPOP = "pines/pines_lolipop_lods/pines_lolipop_lods.glb"
RH = "_downloads/rocky_hills/extract/"
RH_STATIC = RH + "Assets/Toby Fredson/Rocky Hills Environment - Mega Pack/Models/Models_Static/"
RH_TEX = RH + "rocks/Assets/Toby Fredson/Rocky Hills Environment - Mega Pack/Textures/Mesh_Textures/"
RH_STONES = dict(albedo=RH_TEX + "IrregularStones_Albedo.png", normal=RH_TEX + "IrregularStones_Normal.png")
ML = "_downloads/mountain_lake/extract/Assets/VIVID_Arts/Assets/MountainLake/"
ML_FERN = dict(albedo=ML + "Textures/MLRT_Fern_Albedo.png", normal=ML + "Textures/MLRT_Fern_Normal.png", cutout=True, crop=True)
ML_ROCKS = dict(albedo=ML + "Textures/MT_Rocks_Optimized_DefaultMaterial_BaseMap.png",
                normal=ML + "Textures/MT_Rocks_Optimized_DefaultMaterial_Normal.png", crop=True)
RH_TFS = RH + "Assets/Toby Fredson/Rocky Hills Environment - Mega Pack (TFS)/Models_TFS/"
RH_MESH_TEX = RH + "Assets/Toby Fredson/Rocky Hills Environment - Mega Pack/Textures/"
RH_ALB_TEX = RH + "alb/Assets/Toby Fredson/Rocky Hills Environment - Mega Pack/Textures/"
RH_DEADWOOD = dict(albedo=RH_ALB_TEX + "Mesh_Textures/DeadBarckRHE_Albedo.png", normal=RH_MESH_TEX + "Mesh_Textures/DeadBarckRHE_Normal.png")
RH_TRUNK = dict(albedo=RH_ALB_TEX + "Vegetation_Textures/GenericTreeTrunkRHE_Albedo.tif",
                normal=RH_MESH_TEX + "Vegetation_Textures/GenericTreeTrunkRHE_Normal.png")
RH_PLANTS = dict(albedo=RH_MESH_TEX + "Vegetation_Textures/GroundPlantsRHE_Albedo.png",
                 normal=RH_MESH_TEX + "Vegetation_Textures/GroundPlantsRHE_Normal.png", cutout=True, crop=True)
RH_BOULDERS = dict(albedo=RH_TEX + "BouldersRHE_Albedo.png", normal=RH_TEX + "BouldersRHE_Normal.png")
EXT = {
    "Pine_A": dict(src="pines/pine_intice184_a/pine_intice184_a.glb", lod0=["Pine01_LOD0"], lod1="thin", h=27.0, base=2.2,
                   leaf=["Branch"], sphere=0.3, stiff=1.8, r1=0.45, bark=KESIYA_BARK, needles=KESIYA_NEEDLES, tex=2048),
    "Pine_B": dict(src=LOLIPOP, lod0=["Pine_large_1_LOD0"], lod1=["Pine_large_1_LOD2"], h=29.0, base=0.2,
                   leaf=["Clusters"], sphere=0.3, stiff=1.8, bark=KESIYA_BARK, needles=KESIYA_NEEDLES, tex=2048),
    "Pine_C": dict(src=LOLIPOP, lod0=["Pine_large_2_LOD0"], lod1=["Pine_large_2_LOD2"], h=25.0, base=0.2,
                   leaf=["Clusters"], sphere=0.3, stiff=1.8, bark=KESIYA_BARK, needles=KESIYA_NEEDLES, tex=2048),
    "Pine_D": dict(src="pines/pine_evolveduk/pine_evolveduk.glb", lod0=["Tree_"], lod1="thin", h=22.0, base=0.2,
                   leaf=["Tree_1"], sphere=0.3, stiff=1.8, r1=0.5, bark=KESIYA_BARK, needles=KESIYA_NEEDLES),
    "PineYoung": dict(src=LOLIPOP, lod0=["Pine_small_1_LOD0"], lod1=["Pine_small_1_LOD2"], h=4.2, base=0.1,
                      leaf=["Clusters"], sphere=0.35, stiff=1.4, bark=KESIYA_BARK, needles=KESIYA_NEEDLES),
    "PineSapling": dict(src=LOLIPOP, lod0=["Pine_sapling_1_LOD0"], lod1=["Pine_sapling_1_LOD1"], h=1.5, base=0.05,
                        leaf=["Clusters"], sphere=0.4, stiff=1.2, bark=KESIYA_BARK, needles=KESIYA_NEEDLES, tex=512),
    # Rocky Hills (Toby Fredson): game-ready stones with their own LOD1, textured from the pack's summer (green moss) maps.
    "Rock_A": dict(src=RH_STATIC + "IrregularStoneA_MossRHE_LOD0.fbx", src1=RH_STATIC + "IrregularStoneA_MossRHE_LOD1.fbx",
                   lod0=["IrregularStoneA_MossRHE_LOD0"], lod1=["IrregularStoneA_MossRHE_LOD1"], h=-1.0, base=0.1, rigid=True, maps=RH_STONES),
    "Rock_B": dict(src=RH_STATIC + "IrregularStoneC_MossRHE_LOD0.fbx", src1=RH_STATIC + "IrregularStoneC_MossRHE_LOD1.fbx",
                   lod0=["IrregularStoneC_MossRHE_LOD0"], lod1=["IrregularStoneC_MossRHE_LOD1"], h=-1.0, base=0.1, rigid=True, maps=RH_STONES),
    "Rock_C": dict(src=RH_STATIC + "IrregularStoneE_MossRHE_LOD0.fbx", src1=RH_STATIC + "IrregularStoneE_MossRHE_LOD1.fbx",
                   lod0=["IrregularStoneE_MossRHE_LOD0"], lod1=["IrregularStoneE_MossRHE_LOD1"], h=-1.0, base=0.1, rigid=True, maps=RH_STONES),
    "RockFace": dict(src=RH_STATIC + "CliffStoneMediumARHE_LOD0.fbx", src1=RH_STATIC + "CliffStoneMediumARHE_LOD1.fbx",
                     lod0=["CliffStoneMediumARHE_LOD0"], lod1=["CliffStoneMediumARHE_LOD1"], h=-1.0, base=0.15, rigid=True, maps=RH_BOULDERS),
    # Mountain Lake (VIVID Arts): bracken-like ferns for the highland edge, and big boulders mossed for the forest outcrops.
    "Fern_A": dict(src=ML + "Mesh/MLRT_Fern_01.fbx", lod0=["MLRT_Fern_01_LOD0"], lod1=["MLRT_Fern_01_LOD2"], h=-1.0, leaf="all",
                   sphere=0.45, stiff=1.0, maps=ML_FERN),
    "Fern_B": dict(src=ML + "Mesh/MLRT_Fern_02.fbx", lod0=["MLRT_Fern_02_LOD0"], lod1=["MLRT_Fern_02_LOD2"], h=-1.0, leaf="all",
                   sphere=0.45, stiff=1.0, maps=ML_FERN),
    "Fern_C": dict(src=ML + "Mesh/MLRT_Fern_03.fbx", lod0=["MLRT_Fern_03_LOD0"], lod1=["MLRT_Fern_03_LOD2"], h=-1.0, leaf="all",
                   sphere=0.45, stiff=1.0, maps=ML_FERN),
    "Outcrop_A": dict(src=ML + "Mesh/MT_Rock_Med_05.fbx", lod0=["MT_Rock_Med_05_LOD0"], lod1=["MT_Rock_Med_05_LOD2"], h=-1.0, base=0.3,
                      rigid=True, moss=True, maps=ML_ROCKS),
    "Outcrop_B": dict(src=ML + "Mesh/MT_Rock_Med_03.fbx", lod0=["MT_Rock_Med_03_LOD0"], lod1=["MT_Rock_Med_03_LOD2"], h=-1.0, base=0.3,
                      rigid=True, moss=True, maps=ML_ROCKS),
    "Outcrop_C": dict(src=ML + "Mesh/MT_Rock_Large_02.fbx", lod0=["MT_Rock_Large_02_LOD0"], lod1=["MT_Rock_Large_02_LOD2"], h=-1.0,
                      base=0.5, rigid=True, moss=True, maps=ML_ROCKS),
    # Rocky Hills dead wood, mossed on top: a fallen trunk, a standing snag and a broken stump, each with the pack's LOD1.
    "PineLog": dict(src=RH_STATIC + "DeadLogsRHEB_LOD0.fbx", src1=RH_STATIC + "DeadLogsRHEB_LOD1.fbx", lod0=["DeadLogsRHEB_LOD0"],
                    lod1=["DeadLogsRHEB_LOD1"], h=-1.0, base=0.15, rigid=True, moss=True, maps=RH_DEADWOOD),
    "Snag": dict(src=RH_STATIC + "DeadLogsRHEC_LOD0.fbx", src1=RH_STATIC + "DeadLogsRHEC_LOD1.fbx", lod0=["DeadLogsRHEC_LOD0"],
                 lod1=["DeadLogsRHEC_LOD1"], h=-1.0, base=0.2, rigid=True, moss=True, maps=RH_DEADWOOD),
    "Stump": dict(src=RH_STATIC + "TrunkDeadRHE_LOD0.fbx", src1=RH_STATIC + "TrunkDeadRHE_LOD1.fbx", lod0=["TrunkDeadRHE_LOD0"],
                  lod1=["TrunkDeadRHE_LOD1"], h=0.9, base=0.05, rigid=True, moss=True, maps=RH_TRUNK),
    "Sorrel": dict(src="meadow/shrub_sorrel_01/shrub_sorrel_01_2k.gltf", lod0=["shrub_sorrel_01_d"], h=0.3, leaf="all", sphere=0.4, stiff=1.0,
                   r1=0.4, tex=512),
    "Periwinkle": dict(src="meadow/periwinkle_plant/periwinkle_plant_2k.gltf", lod0=["periwinkle_plant_05"], h=0.3, leaf="all", sphere=0.4,
                       stiff=1.0, r0=0.5, r1=0.3, tex=512),
    "Bush": dict(src=RH_TFS + "MTFS_Bushes/GPBSRHE_A.fbx", lod0=["GPBSRHE_A_LOD0"], h=1.0, leaf="all", sphere=0.45, stiff=1.1,
                 r0=1.0, r1=1.0, tex=512, maps=RH_PLANTS),
    # Rocky Hills meadow clumps (terra_grass.py exports in output/assets/grass): sparse wildflower and tall-grass patches.
    "Wildflower_Yellow": dict(src="grass/FlowersClump_Yellow.fbx", lod0=["FlowersClump_Yellow"], h=0.5, leaf="all", sphere=0.45, stiff=1.0, r1=0.4, tex=512,
              maps=dict(albedo="grass/FlowersClump_Yellow_Albedo.png", cutout=True)),
    "Wildflower_Blue": dict(src="grass/FlowersClump_Blue.fbx", lod0=["FlowersClump_Blue"], h=0.5, leaf="all", sphere=0.45, stiff=1.0, r1=0.4, tex=512,
              maps=dict(albedo="grass/FlowersClump_Blue_Albedo.png", cutout=True)),
    "TallGrass_A": dict(src="grass/GrassShortClump_A.fbx", lod0=["GrassShortClump_A"], h=0.5, leaf="all", sphere=0.45, stiff=1.0, r1=0.4, tex=512,
              maps=dict(albedo="grass/GrassShortClump_A_Albedo.png", cutout=True)),
    "TallGrass_B": dict(src="grass/GrassShortClump_B.fbx", lod0=["GrassShortClump_B"], h=0.5, leaf="all", sphere=0.45, stiff=1.0, r1=0.4, tex=512,
              maps=dict(albedo="grass/GrassShortClump_B_Albedo.png", cutout=True)),
    # Period US/ARVN M113s were plain olive drab: the source's woodland camo is repainted as a burnt-out OD wreck.
    "WreckM113": dict(src="vehicles/m113_arc4de/m113_arc4de.glb", lod0=["Object_"], h=2.3, base=0.12, r0=0.5, r1=0.12, rigid=True,
                      tex=2048, wreck=True),
}

# The forest fill-out and the Phase 2 riverlands. look: leaf material settings taken from each pack's own materials.
MT = "_downloads/mountain_trees/extract/Assets/NatureManufacture Assets/Dynamic Nature - Mountain Tree Pack/Models/"
MT_LEAVES = dict(albedo=MT + "Textures/T_Fir_leaves_BC_T.TGA", normal=MT + "Textures/T_Fir_leaves_N.TGA", cutout=True)
MT_BARK = {n: dict(albedo=MT + f"Textures/T_Fir_bark_{n}_BC.tga", normal=MT + f"Textures/T_{'f' if n == '01' else 'F'}ir_bark_{n}_N.tga")
           for n in ("01", "02", "06")}
FIR = dict(mats={"leaves": MT_LEAVES, "Bark_Fir_02": MT_BARK["02"], "Bark_Fir_04_02": MT_BARK["02"], "Bark_Fir_06": MT_BARK["06"],
                 "*": MT_BARK["01"]}, leaf=["leaves"], sphere=0.3, stiff=1.8, base=0.2, tex=2048, look=dict(cutoff=0.4, smoothness=0.2))
RH_VEG_ALB = RH_ALB_TEX + "Vegetation_Textures/"
RH_VEG_N = RH_MESH_TEX + "Vegetation_Textures/"
BROAD = dict(leaf=["Leaves"], sphere=0.35, stiff=1.6, base=0.2, tex=1024, look=dict(cutoff=0.32, smoothness=0.3))
RH_GENERIC = {"Leaves": dict(albedo=RH_VEG_ALB + "VegetationA2_Albedo.png", cutout=True), "*": RH_TRUNK}
MAPLE = dict(mats={"Frond": dict(albedo=ML + "Textures/MLRT_SugarMaple_Frond_Albedo.png",
                                 normal=ML + "Textures/MLRT_SugarMaple_Frond_Normal.png", cutout=True),
                   "*": dict(albedo=ML + "Textures/ML_Tree_007_Bark_BaseColor.png", normal=ML + "Textures/ML_Tree_007_Bark_Normal.png")},
             leaf=["Frond"], sphere=0.35, stiff=1.6, base=0.2, tex=1024, leaf_mul=(0.48, 0.70, 0.44), look=dict(cutoff=0.4))
JT = "_downloads/jungle_tropical/extract/Assets/SeedMesh/Jungle-Tropical Vegetation/Vegetation/"
JT_PLANTS = JT + "Tropical_plants/Meshes and Materials/"
JT_TEX = "_downloads/jungle_tropical/tex/"   # flora_tools/jt_textures.py: the TIFFs' fourth sample kept as coverage alpha


def jt(albedo, normal, cutout=True, crop=False):
    return dict(albedo=JT_TEX + albedo + ".png", normal=JT_TEX + normal + ".png", cutout=cutout, crop=crop)


JT_PALM = jt("Palm_Albedo_4K", "Palm_Normal_4K")
JT_HELICONIA = jt("Heliconia_albedo_4K", "Heliconia_Normal_4K")
JT_LEAVES = jt("Jungle_tree_leaves", "Jungle_tree_normal")
JT_MONSTERA = jt("Monstera_Obliqua_4K", "Monstera_Obliqua_Normal_4K")
JT_BARK = jt("Jungle_bark_basecolor1", "Jungle_bark_normal_var1", cutout=False)
JT_BANANA = {"leaves": JT_HELICONIA, "*": jt("Albedo_banana_bark", "Normal_banana_bark", cutout=False)}
JT_ARECA = {"Leaves": JT_PALM, "*": jt("Green_tall_basecolor", "Green_tall_normal", cutout=False)}
# SeedMesh's vegetation shader: alpha clip .25, waxy leaves, strong thickness-driven translucency.
TROPICAL = dict(cutoff=0.3, smoothness=0.4, translucency=0.95, tintVariation=0.12)
JT_TREE = dict(mats={"Leaves": JT_LEAVES, "*": JT_BARK}, leaf=["Leaves"], sphere=0.35, stiff=1.6, base=0.3, tex=1024, look=TROPICAL)
PLANT = dict(leaf="all", sphere=0.45, stiff=1.1, tex=1024, look=TROPICAL)
VINE = dict(PLANT, stiff=1.0, tex=512, hang=True)
# The jungle-tree leaf atlas crop (leaves over a pale yellow stem) and the ivy read straw-olive on a sunlit wall; pulled to green.
WALL_GREEN = (0.6, 1.0, 0.5)
# Wall vines and curtains darker still: from afar they sit in the rock's shade with the ledge moss instead of glowing lime.
WALL_DEEP = (0.4, 0.66, 0.36)
# farLod: kept to the tall trees' distances whatever their length (FloraPrefabBuilder); tint: curtains side by side differ.
CURTAIN = dict(PLANT, stiff=1.0, hang=True, leaf_mul=WALL_DEEP, look=dict(TROPICAL, tintVariation=0.3, farLod=True),
               mats={"Ivy": jt("Ivy_herb_basecolor_4K", "Ivy_herb_Normal_4K"), "Root": JT_MONSTERA})


def lods(name, a, b):
    return dict(lod0=[f"{name}_LOD{a}"], lod1=[f"{name}_LOD{b}"])


def rh_tree(fbx, prefix, h, mats=RH_GENERIC, lod1=2, **kw):
    return dict(src=RH_TFS + "MTFS_Trees/" + fbx, lod0=[f"{prefix}_Bark_LOD0", f"{prefix}_Leaves_LOD0"],
                lod1=[f"{prefix}_Bark_LOD{lod1}", f"{prefix}_Leaves_LOD{lod1}"], h=h, mats=mats, **dict(BROAD, **kw))


EXT.update({
    # Mountain Trees (NatureManufacture): each LOD1 is the pack's LOD2.
    "Fir_Tall": dict(src=MT + "Fir_05_Standalone.FBX", **lods("Fir_05_standalone", 0, 2), h=26.0, **dict(FIR, base=0.3)),
    "Fir_Medium": dict(src=MT + "Fir_03_Medium.FBX", **lods("Fir_03_medium", 0, 2), h=16.0, **FIR),
    "Fir_Small": dict(src=MT + "Fir_02_Small.FBX", **lods("Fir_02_small", 0, 2), h=6.0, **dict(FIR, base=0.1, tex=1024)),
    "Fir_Plant": dict(src=MT + "Fir_01_Plant.FBX", **lods("Fir_01_plant", 0, 1), h=1.6, **dict(FIR, base=0.05, tex=512)),
    "Fir_Forest_A": dict(src=MT + "Fir_06_Forest.FBX", **lods("Fir_06_forest", 0, 2), h=30.0, **dict(FIR, base=0.3)),
    "Fir_Forest_B": dict(src=MT + "Fir_07_Forest.FBX", **lods("Fir_07_forest", 0, 2), h=28.0, **dict(FIR, base=0.3)),
    "Fir_Dead": dict(src=MT + "Fir_08_Dead.FBX", **lods("Fir_08_dead_forest", 0, 2), h=20.0, **dict(FIR, base=0.3, tex=1024)),
    # Rocky Hills TFS.
    "Spruce": rh_tree("SpruceTree_C.fbx", "STC", 22.0, stiff=1.8,
                      mats={"Leaves": dict(albedo=RH_VEG_ALB + "NeedleBranchesRHE_Albedo.png", normal=RH_VEG_N + "NeedleBranchesRHE_Normal.png",
                                           cutout=True), "*": RH_TRUNK}),
    "Broadleaf_A": rh_tree("GenericTreeSummer_A.fbx", "GTSA", 15.0),
    "Broadleaf_B": rh_tree("GenericTree_B.fbx", "GTB", 14.0),
    "Broadleaf_C": rh_tree("GenericTree_E.fbx", "GTE", 13.0),
    "Beech_Gold": rh_tree("BeechTree05Summer.fbx", "BeechTree05Summer", 16.0,
                          mats={"Leaves": dict(albedo=RH_VEG_ALB + "VegetationB_Albedo 2.png", normal=RH_VEG_N + "VegetationB_Normal.png",
                                               cutout=True), "*": RH_TRUNK}),
    # Mountain Lake sugar maples.
    "Maple_A": dict(src=ML + "Mesh/MLRT_SugarMaple_01_Var1.fbx", h=15.0, **MAPLE,
                    lod0=["MLRT_SugarMaple_01_Var1_Trunk_LOD0", "MLRT_SugarMaple_01_Var1_Branches_LOD0"],
                    lod1=["MLRT_SugarMaple_01_Var1_Trunk_LOD1", "MLRT_SugarMaple_01_Var1_Branches_LOD1"]),
    "Maple_B": dict(src=ML + "Mesh/MLRT_SugarMaple_02_Var1.fbx", h=13.0, **MAPLE,
                    lod0=["MLRT_SugarMaple_02_Var1_Trunk_LOD0", "MLRT_SugarMaple_02_Var1_Branches_LOD0"],
                    lod1=["MLRT_SugarMaple_02_Var1_Trunk_LOD1", "MLRT_SugarMaple_02_Var1_Branches_LOD1"]),
    # SeedMesh Jungle - Tropical Vegetation: hamlet banana and areca, karst vines and climbers, plateau jungle.
    "Banana_B": dict(src=JT + "Trees/banana_tree/BananaTree_var1.fbx", **lods("BananaTree_var1", 0, 2), h=4.8, base=0.1, mats=JT_BANANA,
                     leaf=["leaves"], sphere=0.25, stiff=1.3, tex=1024, look=TROPICAL),
    "Banana_C": dict(src=JT + "Trees/banana_tree/BananaTree_var2.fbx", **lods("BananaTree_var2", 0, 2), h=4.2, base=0.1, mats=JT_BANANA,
                     leaf=["leaves"], sphere=0.25, stiff=1.3, tex=1024, look=TROPICAL),
    "Areca_A": dict(src=JT + "Trees/Tall_palm/Tall_palm_var1.fbx", **lods("Palm_var1", 0, 3), h=11.0, base=0.1, mats=JT_ARECA,
                    leaf=["Leaves"], sphere=0.3, stiff=2.0, tex=1024, look=TROPICAL),
    "Areca_B": dict(src=JT + "Trees/Tall_palm/Tall_palm_var2.fbx", **lods("Palm_var2", 0, 3), h=9.5, base=0.1, mats=JT_ARECA,
                    leaf=["Leaves"], sphere=0.3, stiff=2.0, tex=1024, look=TROPICAL),
    "PalmYoung_A": dict(src=JT_PLANTS + "Palm/Young_palm_var1.fbx", **lods("Palm_var1", 0, 2), h=1.7, maps=JT_PALM, **PLANT),
    "PalmYoung_B": dict(src=JT_PLANTS + "Palm/Young_palm_var4.fbx", **lods("Palm_var2", 0, 2), h=1.4, maps=JT_PALM, **PLANT),
    "Vine_A": dict(src=JT_PLANTS + "Vines/Hanging_vine_var1.fbx", **lods("hanging_vine_var1", 0, 1), h=3.5, maps=dict(JT_LEAVES, crop=True),
                   leaf_mul=WALL_DEEP, **VINE),
    "Vine_B": dict(src=JT_PLANTS + "Vines/Hanging_vine_var2.fbx", **lods("hanging_vine_var2", 0, 1), h=4.5, maps=dict(JT_LEAVES, crop=True),
                   leaf_mul=WALL_DEEP, **VINE),
    "Vine_Monstera": dict(src=JT_PLANTS + "Monstera_andasonii/Vine_monstera_hanging_var1.fbx", **lods("MO_var1", 0, 2), h=2.5,
                          maps=JT_MONSTERA, **dict(VINE, tex=1024)),
    # Vine curtains: dense sheets of the ivy's hanging cascade with a few monstera aerial roots, built by curtain_gen.py.
    "Curtain_A": dict(src="flora_gen/Curtain_A.fbx", **lods("Curtain_A", 0, 1), h=4.5, **CURTAIN),
    "Curtain_B": dict(src="flora_gen/Curtain_B.fbx", **lods("Curtain_B", 0, 1), h=7.5, **CURTAIN),
    "Curtain_C": dict(src="flora_gen/Curtain_C.fbx", **lods("Curtain_C", 0, 1), h=5.0, **CURTAIN),
    "Climber_A": dict(src=JT_PLANTS + "Climbing_leaves/Herb_climbing_var1.fbx", **lods("Herb_climbing_var1", 0, 1), h=1.4,
                      maps=jt("Ivy_herb_basecolor_4K", "Ivy_herb_Normal_4K"), leaf_mul=WALL_GREEN, **dict(PLANT, tex=512)),
    "Climber_B": dict(src=JT_PLANTS + "Climbing_leaves/Monstera_climbing_var1.fbx", **lods("Monstera_climbing_var1", 0, 1), h=1.2,
                      maps=JT_MONSTERA, **dict(PLANT, tex=512)),
    "Fern_D": dict(src=JT_PLANTS + "Fern/Fern_var1.fbx", **lods("Fern_var1", 0, 2), h=1.1, maps=jt("FernMaps_basecolor", "FernMaps_normal"),
                   **dict(PLANT, tex=512)),
    "Fern_E": dict(src=JT_PLANTS + "Fern/Fern_var2.fbx", **lods("Fern_var2", 0, 2), h=1.0, maps=jt("FernMaps_basecolor", "FernMaps_normal"),
                   **dict(PLANT, tex=512)),
    "ElephantEar_A": dict(src=JT_PLANTS + "Elephant_ear/Elephant_ear_var1.fbx", **lods("Elephant_Ear_var1", 0, 2), h=1.5,
                          maps=jt("Elephant_ear_4K", "Elephant_ear_Normal_4K"), **PLANT),
    "ElephantEar_B": dict(src=JT_PLANTS + "Elephant_ear/Elephant_ear_var4.fbx", **lods("Elephant_Ear_var4", 0, 2), h=1.8,
                          maps=jt("Elephant_ear_4K", "Elephant_ear_Normal_4K"), **PLANT),
    "Heliconia_A": dict(src=JT_PLANTS + "Heliconia/Heliconia_var1.fbx", **lods("Heliconia_var1", 0, 2), h=1.8, maps=JT_HELICONIA, **PLANT),
    "Heliconia_B": dict(src=JT_PLANTS + "Heliconia/Heliconia_var3.fbx", **lods("Heliconia_var3", 0, 2), h=1.6, maps=JT_HELICONIA, **PLANT),
    "RedGinger": dict(src=JT_PLANTS + "Red_ginger/Red_Ginger_var2.fbx", **lods("Red_Ginger_var2", 0, 2), h=1.5,
                      maps=jt("Red_Ginger_diffuce_4k", "Red_Ginger_normal_4K"), **PLANT),
    "JungleTree_A": dict(src=JT + "Trees/Jungle_tree/Jungle_tree_var2.fbx", **lods("Jungle_tree2", 0, 2), h=16.0, **JT_TREE),
    "JungleTree_B": dict(src=JT + "Trees/Jungle_tree/Jungle_tree_var3.fbx", **lods("Jungle_tree_var3", 0, 2), h=14.0, **JT_TREE),
    "JungleTree_Tall": dict(src=JT + "Trees/Tall_trees/Tall_tree_var1.fbx", **lods("Tall_tree_var1", 0, 2), h=24.0,
                            **dict(JT_TREE, stiff=1.8)),
})

rng = random.Random(hash(SPECIES) & 0xffff)
info = {"name": SPECIES, "materials": []}
lods = []
extra = {}   # source material name: save_image size and tint


def bark_decimated(obj, ratio, leaf_slots, name):
    """Copy of obj with only the non-leaf faces decimated: collapse ruins alpha cards."""
    me = obj.data
    group = obj.vertex_groups.new(name="bark")
    group.add(list({v for p in me.polygons if p.material_index not in leaf_slots for v in p.vertices}), 1.0, 'REPLACE')
    m = obj.modifiers.new("dec", 'DECIMATE')
    m.ratio = ratio
    m.use_collapse_triangulate = True
    m.vertex_group = "bark"
    m.vertex_group_factor = 1000.0
    dg = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    obj.modifiers.remove(m)
    obj.vertex_groups.remove(group)
    o = bpy.data.objects.new(name, out)
    scene.collection.objects.link(o)
    return o


def burn_mask(obj, size):
    """
    Where a wreck burnt, baked into its UV layout (0..1, h x w): blotches of a noise in object space, so they run
    across seams, heaviest on the upper faces and high up where the fire licked out of the hatches.
    """
    H = max(max(v.co.z for v in obj.data.vertices), 1e-3)
    mat = bpy.data.materials.new("burn"); mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); emit = nt.nodes.new("ShaderNodeEmission")
    coords = nt.nodes.new("ShaderNodeTexCoord"); noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.9; noise.inputs["Detail"].default_value = 4.0
    nt.links.new(coords.outputs["Object"], noise.inputs["Vector"])
    blot = nt.nodes.new("ShaderNodeMapRange"); blot.inputs[1].default_value = 0.45; blot.inputs[2].default_value = 0.68
    nt.links.new(noise.outputs[0], blot.inputs[0])
    geo = nt.nodes.new("ShaderNodeNewGeometry"); split = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], split.inputs[0])
    up = nt.nodes.new("ShaderNodeMapRange"); up.inputs[1].default_value = -0.2; up.inputs[2].default_value = 1.0
    up.inputs[3].default_value = 0.35; up.inputs[4].default_value = 1.0
    nt.links.new(split.outputs[2], up.inputs[0])
    pos = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(coords.outputs["Object"], pos.inputs[0])
    high = nt.nodes.new("ShaderNodeMapRange"); high.inputs[1].default_value = 0.0; high.inputs[2].default_value = H
    high.inputs[3].default_value = 0.3; high.inputs[4].default_value = 1.0
    nt.links.new(pos.outputs[2], high.inputs[0])
    m1 = nt.nodes.new("ShaderNodeMath"); m1.operation = 'MULTIPLY'
    m2 = nt.nodes.new("ShaderNodeMath"); m2.operation = 'MULTIPLY'
    nt.links.new(blot.outputs[0], m1.inputs[0]); nt.links.new(up.outputs[0], m1.inputs[1])
    nt.links.new(m1.outputs[0], m2.inputs[0]); nt.links.new(high.outputs[0], m2.inputs[1])
    nt.links.new(m2.outputs[0], emit.inputs["Color"]); nt.links.new(emit.outputs[0], out.inputs[0])
    target = bpy.data.images.new("burn_mask", size, size, alpha=False, float_buffer=True)
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = target
    nt.nodes.active = tex
    saved = list(obj.data.materials)
    for i in range(len(saved)):
        obj.data.materials[i] = mat
    for o in scene.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    engine = scene.render.engine
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 1
    bpy.ops.object.bake(type='EMIT', margin=8)
    scene.render.engine = engine
    for i, m in enumerate(saved):
        obj.data.materials[i] = m
    mask = np.array(target.pixels[:], np.float32).reshape(size, size, 4)[:, :, 0]
    bpy.data.images.remove(target); bpy.data.materials.remove(mat)
    return mask


def crop_atlas(objs, images, opaque):
    """Crops images to the UV box the objects use (plus a margin) and remaps their UVs into it."""
    uvs = np.concatenate([np.array([l.uv[:] for l in o.data.uv_layers.active.data], np.float32) for o in objs])
    u0, v0 = np.clip(uvs.min(0), 0, 1); u1, v1 = np.clip(uvs.max(0), 0, 1)
    for img in images:
        w, h = img.size
        x0, x1 = max(0, int(u0 * w) - 8), min(w, int(np.ceil(u1 * w)) + 8)
        y0, y1 = max(0, int(v0 * h) - 8), min(h, int(np.ceil(v1 * h)) + 8)
        px = np.empty(w * h * 4, np.float32); img.pixels.foreach_get(px)
        part = px.reshape(h, w, 4)[y0:y1, x0:x1].copy()
        del px
        if opaque and img.colorspace_settings.name != 'Non-Color':
            part[:, :, 3] = 1.0   # the pack's alpha is a smoothness/height mask, not coverage
        img.scale(x1 - x0, y1 - y0)
        img.pixels.foreach_set(part.ravel())
        img.pack()
        box = (x0 / w, y0 / h, x1 / w, y1 / h)
    bu0, bv0, bu1, bv1 = box
    for o in objs:
        for l in o.data.uv_layers.active.data:
            l.uv = ((l.uv[0] - bu0) / (bu1 - bu0), (l.uv[1] - bv0) / (bv1 - bv0))


def leaf_multiply(mul):
    """Multiplies the texels' colour and keeps alpha: the tint a pack's own leaf shader puts on a grey atlas."""
    m = np.array(mul, np.float32)

    def recolour(px):
        out = px.copy()
        out[:, :, :3] = np.clip(px[:, :, :3] * m, 0, 1)
        return out
    return recolour


def leaf_grade(saturation, mul):
    """Pulls the texels towards their own grey by 1 - saturation, then multiplies them; keeps alpha. For a leaf atlas
    too pure to take the weather's light: a multiply alone cannot add the blue that grey light needs to act on."""
    m, lum = np.array(mul, np.float32), np.array((.2126, .7152, .0722), np.float32)

    def recolour(px):
        out = px.copy()
        rgb = px[:, :, :3]
        grey = (rgb @ lum)[:, :, None]
        out[:, :, :3] = np.clip((grey + (rgb - grey) * saturation) * m, 0, 1)
        return out
    return recolour


def needle_albedo(needles):
    """Moves the needle texels' mean colour and brightness onto the species' needle colour, keeping their detail.
    Only green-leaning texels count as needles; the atlas' brown twigs and bark keep their colour."""
    colour, amount = np.array(needles[0], np.float32), needles[1]
    def recolour(px):
        rgb = px[:, :, :3]
        green = np.clip((rgb[:, :, 1] - .85 * rgb[:, :, 0]) / np.maximum(rgb[:, :, 1], 1e-3) / .12, 0, 1)
        needle = (px[:, :, 3] > .5) & (green > .5)
        lum = rgb @ np.array((.3, .59, .11), np.float32)
        detail = np.clip(lum / max(float(lum[needle].mean()) if needle.any() else 1.0, 1e-3), 0, 2.5)[:, :, None]
        k = (amount * green)[:, :, None]
        out = px.copy()
        out[:, :, :3] = np.clip(rgb * (1 - k) + colour * detail * k, 0, 1)
        return out
    return recolour


def moss_albedo(mask):
    """Moss over the stone where mask is high (upper faces, blotched), keeping the stone's own detail."""
    def recolour(px):
        h, w = px.shape[:2]
        m = mask
        if m.shape != (h, w):
            ys = (np.arange(h) * m.shape[0] // h); xs = (np.arange(w) * m.shape[1] // w)
            m = m[ys][:, xs]
        rgb = px[:, :, :3]
        lum = rgb @ np.array((.3, .59, .11), np.float32)
        detail = np.clip(lum / max(float(lum.mean()), 1e-3), .5, 1.5)[:, :, None]
        a = np.clip(m * 1.6, 0, .9)[:, :, None]
        out = px.copy()
        out[:, :, :3] = rgb * (1 - a) + np.array((.16, .21, .07), np.float32) * detail * a
        out[:, :, 3] = 1.0
        return out
    return recolour


def wreck_albedo(mask):
    """Recolour of a vehicle albedo into a burnt-out wreck: faded olive drab with rust, sooted where mask is high."""
    def recolour(px):
        h, w = px.shape[:2]
        m = mask
        if m.shape != (h, w):
            ys = (np.arange(h) * m.shape[0] // h); xs = (np.arange(w) * m.shape[1] // w)
            m = m[ys][:, xs]
        rgb = px[:, :, :3]
        lum = rgb @ np.array((.3, .59, .11), np.float32)
        # Detail (panel lines, bolts, grime) relative to the brightness of the pixel's camo colour, so the source's
        # few flat camo colours all become one paint: cluster the colours, divide each pixel by its cluster's mean.
        flat = rgb.reshape(-1, 3)
        rs = np.random.default_rng(5)
        cent = flat[rs.choice(len(flat), 5, replace=False)].copy()
        sample = flat[rs.choice(len(flat), min(len(flat), 40000), replace=False)]
        for _ in range(12):
            lab = ((sample[:, None, :] - cent[None]) ** 2).sum(2).argmin(1)
            for i in range(len(cent)):
                if (lab == i).any():
                    cent[i] = sample[lab == i].mean(0)
        lab = np.empty(len(flat), np.int64)
        for i0 in range(0, len(flat), 1 << 20):
            lab[i0:i0 + (1 << 20)] = ((flat[i0:i0 + (1 << 20), None, :] - cent[None]) ** 2).sum(2).argmin(1)
        clum = cent @ np.array((.3, .59, .11), np.float32)
        local = clum[lab].reshape(h, w)
        detail = np.sqrt(np.clip(lum / np.maximum(local, 1e-3), .5, 1.6))[:, :, None]
        rng = np.random.default_rng(11)

        def blotches(cells):
            g = rng.random((cells + 1, cells + 1)).astype(np.float32)
            y = np.linspace(0, cells, h, endpoint=False); x = np.linspace(0, cells, w, endpoint=False)
            y0 = y.astype(int); x0 = x.astype(int); fy = (y - y0)[:, None]; fx = (x - x0)[None, :]
            fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
            a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy

        def ramp(v, lo, hi):
            t = np.clip((v - lo) / (hi - lo), 0, 1)
            return t * t * (3 - 2 * t)
        n = blotches(9) * .6 + blotches(37) * .3 + blotches(150) * .1
        rust = np.maximum(ramp(n, .6, .78) * .7, ramp(blotches(90), .74, .84) * .35)[:, :, None]
        olive = np.array((.30, .31, .22), np.float32) * detail
        out = olive * (1 - rust) + np.array((.34, .21, .12), np.float32) * detail * rust
        burn = np.clip(m, 0, 1)[:, :, None]
        # Soot over burnt paint, and bare heat-scaled metal with rust where it burnt hottest.
        out = out * (1 - burn * .9) + np.array((.05, .045, .04), np.float32) * detail * burn * .9
        px[:, :, :3] = np.clip(out, 0, 1)
        return px
    return recolour

if SPECIES in TP or SPECIES == "Bamboo":
    if SPECIES == "Bamboo":
        culms = ["bambooBlock.008_21", "bambooBlock.011_18", "bambooBlock.012_17", "bambooBlock.014_15"]
        parts = []
        for k, g in enumerate(culms):
            o = bake_copy(source_group(g), f"culm{k}")
            normalize(o, height=random.Random(k).uniform(10.5, 13.5))
            a = k / len(culms) * 2 * math.pi + 0.4
            o.data.transform(Matrix.Translation((math.cos(a) * 0.55, math.sin(a) * 0.55, 0)) @ Matrix.Rotation(math.radians(5 + 3 * k % 4), 4, Vector((-math.sin(a), math.cos(a), 0))) @ Matrix.Rotation(a * 2.3, 4, 'Z'))
            parts.append(o)
        with override(parts, parts[0]):
            bpy.ops.object.join()
        src = parts[0]; src.name = "src"
        normalize(src, scale=1.0)
        leaf_kw, sphere, stiff, H = ["alpha"], 0.3, 1.6, 13.5
    else:
        groups, H, leaf_kw, sphere, stiff, lod0_ratio = TP[SPECIES]
        # Each source group is centred on its own base first (some packs lay variants out side by side).
        parts = []
        for k, g in enumerate(groups):
            o = bake_copy(source_group(g), f"part{k}")
            normalize(o, scale=1.0)
            o.data.transform(Matrix.Rotation(k * 2.1, 4, 'Z') @ Matrix.Translation((k * 0.35, 0, 0)))
            parts.append(o)
        if len(parts) > 1:
            with override(parts, parts[0]):
                bpy.ops.object.join()
        src = parts[0]; src.name = "src"
        normalize(src, height=H)
    leaf_slots = {i for i, m in enumerate(src.data.materials) if any(k in m.name for k in leaf_kw)}
    if SPECIES == "Bamboo":
        # Stems and branches are over-tessellated; leaf cards stay.
        lod0 = decimated(src, 0.45, SPECIES + "_LOD0")
    elif isinstance(lod0_ratio, float):
        lod0 = decimated(src, lod0_ratio, SPECIES + "_LOD0")
    else:
        lod0 = src.copy(); lod0.data = src.data.copy(); lod0.name = SPECIES + "_LOD0"; scene.collection.objects.link(lod0)
    lod1 = decimated(lod0, 0.4, SPECIES + "_LOD1")
    for o in (lod0, lod1):
        paint(o, leaf_slots, stiff, sphere, random.Random(5))
    lods = [lod0, lod1]
    bpy.data.objects.remove(src)
    leaf_names = {m.name for i, m in enumerate(lod0.data.materials) if i in leaf_slots}
    save = {}
    for m in lod0.data.materials:
        base, cut, nrm = material_info(m)
        save[m.name] = (base, nrm, m.name in leaf_names)
        if SPECIES == "Bamboo" and m.name in leaf_names:
            # The pack's leaves are pure lime (no blue, saturation ~1) and brighter than the jungle around them, so
            # under Overcast and Misty they kept glowing while their neighbours went grey-green.
            extra[m.name] = dict(tint=leaf_grade(0.6, (0.88, 0.84, 0.88)))
    tile = 1024 if H > 3 else 512
elif SPECIES in PH:
    asset, nodes, H, sphere, stiff, r0, r1, leafy = PH[SPECIES]
    objs = import_gltf(os.path.join(PH_DIR, asset, asset + "_1k.gltf"), nodes)
    src = bake_copy(objs, "src")
    for o in objs:
        bpy.data.objects.remove(o)
    normalize(src, height=H) if H > 0 else normalize(src, scale=-H)
    lod0 = decimated(src, r0, SPECIES + "_LOD0") if r0 < 1 else src
    lod0.name = SPECIES + "_LOD0"
    lod1 = decimated(src, r1, SPECIES + "_LOD1")
    slots = set(range(len(lod0.data.materials))) if leafy else set()
    for o in (lod0, lod1):
        paint(o, slots, stiff, sphere, random.Random(9))
    lods = [lod0, lod1]
    leaf_names = {m.name for m in lod0.data.materials} if leafy else set()
    save = {}
    for m in lod0.data.materials:
        base, cut, nrm = material_info(m)
        save[m.name] = (base, nrm, leafy)
    tile = 1024 if H > 3 else 512
elif SPECIES in EXT:
    e = dict(EXT_DEFAULT, **EXT[SPECIES])
    before = set(bpy.data.objects)
    for f in [e["src"]] + ([e["src1"]] if e["src1"] else []):
        if f.lower().endswith(".fbx"):
            bpy.ops.import_scene.fbx(filepath=os.path.join(ASSET_DIR, f))
        else:
            bpy.ops.import_scene.gltf(filepath=os.path.join(ASSET_DIR, f))
    new = [o for o in bpy.data.objects if o not in before]

    def picked(prefixes):
        found = [o for o in new if o.type == 'MESH' and any(o.name.startswith(p) for p in prefixes)]
        if not found:
            raise SystemExit(f"{SPECIES}: no source object starts with {prefixes}")
        for o in found:
            # Packs that name the material only on the object (Rocky Hills TFS trees): a slot named after it.
            if not o.data.materials:
                o.data.materials.append(bpy.data.materials.new(o.name))
        return found
    src = bake_copy(picked(e["lod0"]), "src")
    src1 = bake_copy(picked(e["lod1"]), "src1") if isinstance(e["lod1"], list) else None
    if src1:
        # Packs that lay their LODs out side by side: put LOD1's base centre on LOD0's.
        def base_centre(o):
            vs = [v.co for v in o.data.vertices]
            return Vector(((min(c.x for c in vs) + max(c.x for c in vs)) / 2, (min(c.y for c in vs) + max(c.y for c in vs)) / 2,
                           min(c.z for c in vs)))
        src1.data.transform(Matrix.Translation(base_centre(src) - base_centre(src1)))
    for o in new:
        bpy.data.objects.remove(o)
    def maps_material(name, maps):
        """A material from texture files, for packs whose materials live in their engine (Unity .mat)."""
        mat = bpy.data.materials.new(name); mat.use_nodes = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        albedo = mat.node_tree.nodes.new("ShaderNodeTexImage")
        albedo.image = bpy.data.images.load(os.path.join(ASSET_DIR, maps["albedo"]), check_existing=True)
        mat.node_tree.links.new(albedo.outputs[0], bsdf.inputs["Base Color"])
        if maps.get("cutout"):
            mat.node_tree.links.new(albedo.outputs[1], bsdf.inputs["Alpha"])
        if maps.get("normal"):
            normal = mat.node_tree.nodes.new("ShaderNodeTexImage")
            normal.image = bpy.data.images.load(os.path.join(ASSET_DIR, maps["normal"]), check_existing=True)
            normal.image.colorspace_settings.name = 'Non-Color'
            bump = mat.node_tree.nodes.new("ShaderNodeNormalMap")
            mat.node_tree.links.new(normal.outputs[0], bump.inputs["Color"])
            mat.node_tree.links.new(bump.outputs[0], bsdf.inputs["Normal"])
        return mat
    if e["mats"]:
        made = {}
        for o in (src, src1):
            for i, slot in enumerate(o.data.materials if o else []):
                key = next((k for k in e["mats"] if k != "*" and k.lower() in (slot.name if slot else "").lower()), "*")
                if key not in made:
                    made[key] = maps_material(key if key != "*" else SPECIES + "_rest", e["mats"][key])
                o.data.materials[i] = made[key]
    elif e["maps"]:
        mat = maps_material(SPECIES + "_M0", e["maps"])
        for o in (src, src1):
            if o:
                o.data.materials.clear()
                o.data.materials.append(mat)
        if e["maps"].get("crop"):
            # Pieces of a shared pack atlas: keep only this mesh's part, so its own texture spends no texels on the rest.
            crop_atlas([o for o in (src, src1) if o], [n.image for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE'],
                       opaque=not e["maps"].get("cutout"))
    # One transform for both levels: base centre at the origin, sized, sunk by base.
    vs = [v.co for v in src.data.vertices]
    lo = Vector([min(c[i] for c in vs) for i in range(3)]); hi = Vector([max(c[i] for c in vs) for i in range(3)])
    s = e["h"] / (hi.z - lo.z) if e["h"] > 0 else -e["h"]
    fit = Matrix.Translation((0, 0, -e["base"])) @ Matrix.Scale(s, 4) @ Matrix.Translation(-Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)))
    for o in (src, src1):
        if o:
            o.data.transform(fit)
    if e["leaf"] == "all":
        leaf_slots = set(range(len(src.data.materials)))
    else:
        leaf_slots = {i for i, m in enumerate(src.data.materials) if any(k in m.name for k in e["leaf"])}
    if e["r0"] < 1:
        lod0 = decimated(src, e["r0"], SPECIES + "_LOD0") if e["leaf"] == "all" or not leaf_slots else bark_decimated(src, e["r0"], leaf_slots, SPECIES + "_LOD0")
    else:
        lod0 = src.copy(); lod0.data = src.data.copy(); lod0.name = SPECIES + "_LOD0"; scene.collection.objects.link(lod0)
    if src1:
        lod1 = src1; lod1.name = SPECIES + "_LOD1"
        # The source LOD may carry its own copies of the materials; draw it with LOD0's.
        for i, m in enumerate(lod1.data.materials):
            match = next((n for n in lod0.data.materials if n.name.split(".")[0] == m.name.split(".")[0]), None)
            if match:
                lod1.data.materials[i] = match
    elif e["lod1"] == "thin":
        thinned = lod0.copy(); thinned.data = lod0.data.copy(); scene.collection.objects.link(thinned)
        thin_leaves(thinned, e["keep"], leaf_slots, random.Random(3))
        lod1 = bark_decimated(thinned, e["r1"], leaf_slots, SPECIES + "_LOD1")
        bpy.data.objects.remove(thinned)
    else:
        lod1 = decimated(src, e["r1"], SPECIES + "_LOD1")
    for o in (lod0, lod1):
        paint(o, leaf_slots, e["stiff"], e["sphere"], random.Random(7), rigid=e["rigid"], hang=e["hang"])
    lods = [lod0, lod1]
    bpy.data.objects.remove(src)
    H = max(v.co.z for v in lod0.data.vertices)
    leaf_names = {m.name for i, m in enumerate(lod0.data.materials) if i in leaf_slots}
    save = {}
    for m in lod0.data.materials:
        base, cut, nrm = material_info(m)
        save[m.name] = (base, nrm, m.name in leaf_names)
        extra[m.name] = dict(size=e["tex"], tint=e["bark"] if m.name not in leaf_names else
                             needle_albedo(e["needles"]) if e["needles"] else
                             leaf_multiply(e["leaf_mul"]) if e["leaf_mul"] else None)
        if e["wreck"]:
            extra[m.name]["tint"] = wreck_albedo(burn_mask(lod0, e["tex"]))
        elif e["moss"]:
            extra[m.name]["tint"] = moss_albedo(burn_mask(lod0, e["tex"]))
    tile = 1024 if H > 3 else 512
else:
    raise SystemExit("unknown species " + SPECIES)

# Materials: rename to <Species>_M<k> so names are unique across species, export their textures.
renamed = {}
for k, m in enumerate(lods[0].data.materials):
    new = m.name if m.name.startswith(SPECIES) else f"{SPECIES}_M{k}"
    renamed[m.name] = new
for o in lods:
    for i, m in enumerate(o.data.materials):
        if m.name in renamed and m.name != renamed[m.name]:
            m.name = renamed[m.name]
for old, new in renamed.items():
    base, nrm, leaf = save[old]
    entry = {"name": new, "leaf": bool(leaf), "cutout": False, "normal": False}
    if base:
        size = extra.get(old, {}).get("size", 1024)
        entry["cutout"] = save_image(base, os.path.join(OUT, new + "_Albedo.png"), size, tint=extra.get(old, {}).get("tint"))
        if extra.get(old, {}).get("tint"):
            # The impostor cards render from the materials: show them the recoloured texture.
            recoloured = bpy.data.images.load(os.path.join(OUT, new + "_Albedo.png"))
            for n in bpy.data.materials[new].node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image == base:
                    n.image = recoloured
    if nrm:
        save_image(nrm, os.path.join(OUT, new + "_Normal.png"), min(1024, extra.get(old, {}).get("size", 1024)), non_color=True)
        entry["normal"] = True
    info["materials"].append(entry)

card = impostor(lods[0], tile)
info["materials"].append({"name": SPECIES + "_Card", "leaf": True, "cutout": True, "normal": False, "card": True})
info["noCard"] = CARD_EMPTY
lods.append(card)
xs = [v.co for v in lods[0].data.vertices]
info["height"] = round(max(c.z for c in xs), 3)
info["radius"] = round(max(math.hypot(c.x, c.y) for c in xs), 3)
info["tris"] = [tris(o) for o in lods]
if SPECIES in EXT and EXT[SPECIES].get("look"):
    info.update(EXT[SPECIES]["look"])

for o in scene.objects:
    o.select_set(False)
for o in lods:
    o.select_set(True)
bpy.context.view_layer.objects.active = lods[0]
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, SPECIES + ".fbx"), use_selection=True, object_types={'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='OFF', colors_type='LINEAR',
                         use_mesh_modifiers=False, path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
json.dump(info, open(os.path.join(OUT, SPECIES + ".json"), "w"), indent=1)
print("SPECIES DONE", json.dumps(info))
