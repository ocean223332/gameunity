# Background Blender: builds one flora species for Unity -> <out>/<Species>/<Species>.fbx with
# <Species>_LOD0/_LOD1 (source or generated meshes) and <Species>_LOD2 (cross-card impostor), the textures
# each material needs (<Material>_Albedo.png, optional <Material>_Normal.png) and <Species>.json.
#
# Vertex colour (linear): R wind weight, G wind phase, B leaf flutter, A occlusion.
# usage: blender -b --factory-startup <env_stage1_sources.blend> -P flora_build.py -- <species> <out_root> <polyhaven_dir> <twig_dir>
import bpy, bmesh, sys, os, json, math, random
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SPECIES, OUT_ROOT, PH_DIR, TWIG_DIR = argv[:4]
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


def save_image(img, path, max_size, non_color=False):
    w, h = img.size
    # A fresh image from the pixels: copies of file-backed images carry no data of their own.
    copy = bpy.data.images.new(img.name + "_out", w, h, alpha=True)
    if non_color:
        copy.colorspace_settings.name = 'Non-Color'
    copy.pixels[:] = img.pixels[:]
    f = min(1.0, max_size / max(w, h))
    if f < 1.0:
        copy.scale(max(4, int(w * f)), max(4, int(h * f)))
    copy.filepath_raw = path
    copy.file_format = 'PNG'
    if non_color:
        copy.colorspace_settings.name = 'Non-Color'
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


def paint(obj, leaf_slots, stiff, sphere, rng, ao_floor=0.45):
    """Wind/occlusion colours and leaf normals bent towards a sphere around the foliage mass."""
    me = obj.data
    zs = [v.co.z for v in me.vertices]
    H = max(max(zs), 1e-3)
    leafy = [v.co for p in me.polygons if p.material_index in leaf_slots for v in (me.vertices[i] for i in p.vertices)]
    if leafy:
        lo = Vector([min(c[i] for c in leafy) for i in range(3)]); hi = Vector([max(c[i] for c in leafy) for i in range(3)])
        centre = (lo + hi) / 2; radii = Vector([max((hi[i] - lo[i]) / 2, 0.05) for i in range(3)])
    else:
        centre = Vector((0, 0, H / 2)); radii = Vector((1, 1, H / 2))
    if "Col" in me.color_attributes:
        me.color_attributes.remove(me.color_attributes["Col"])
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
            weight = h ** stiff
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


# ---------------------------------------------------------------- generated canopy trees

def tube(bm, uvl, path, radii, sides, slot, v_scale, u_tiles, lobes=None):
    """Bark tube along path points; optional buttress lobes (amplitude per ring)."""
    rings = []
    length = 0.0
    for i, p in enumerate(path):
        if i:
            length += (p - path[i - 1]).length
        d = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        side = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.95 else Vector((1, 0, 0))).normalized()
        up = side.cross(d).normalized()
        ring = []
        for k in range(sides + 1):
            t = k / sides * 2 * math.pi
            r = radii[i]
            if lobes:
                amp, count, phase = lobes[i]
                r *= 1 + amp * max(0.0, math.cos(count * t + phase)) ** 3
            ring.append((bm.verts.new(p + (side * math.cos(t) + up * math.sin(t)) * r), (k / sides * u_tiles, length / v_scale)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(sides):
            f = bm.faces.new([a[k][0], a[k + 1][0], b[k + 1][0], b[k][0]])
            f.material_index = slot
            for loop, uv in zip(f.loops, (a[k][1], a[k + 1][1], b[k + 1][1], b[k][1])):
                loop[uvl].uv = uv


def card(bm, uvl, centre, normal, along, size, tile):
    """Twig card: base near the branch, tip along 'along', facing 'normal'."""
    along = (along - normal * along.dot(normal)).normalized()
    side = normal.cross(along).normalized()
    base = centre - along * size * 0.45
    pts = (base - side * size / 2, base + side * size / 2, base + along * size + side * size / 2, base + along * size - side * size / 2)
    tx, ty = tile % 2, tile // 2
    uvs = ((tx / 2, ty / 2), ((tx + 1) / 2, ty / 2), ((tx + 1) / 2, (ty + 1) / 2), (tx / 2, (ty + 1) / 2))
    f = bm.faces.new([bm.verts.new(p) for p in pts])
    f.material_index = 1
    for loop, uv in zip(f.loops, uvs):
        loop[uvl].uv = uv


def canopy_tree(seed, height, crown_frac, limbs, lod):
    rng = random.Random(seed)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    fork = height * crown_frac
    trunk_r = height * 0.022
    sides = 12 if lod == 0 else 7
    n = 16 if lod == 0 else 8
    lean = Vector((rng.uniform(-.04, .04), rng.uniform(-.04, .04), 0))
    path = []; radii = []; lobes = []
    phase0 = rng.uniform(0, 6.28)
    for i in range(n + 1):
        t = (i / n) ** 1.35
        z = t * (fork + (height - fork) * 0.5)   # the leader ends inside the crown
        path.append(Vector((0, 0, z)) + lean * z + Vector((math.sin(z * 0.4) * 0.12, math.cos(z * 0.33) * 0.1, 0)))
        radii.append(trunk_r * (1.0 - 0.6 * t) + trunk_r * 0.9 * math.exp(-z / 0.9))
        lobes.append((1.6 * math.exp(-z / 1.3), 5, phase0))
    tube(bm, uvl, path, radii, sides, 0, 2.2, 2, lobes)
    crown_c = Vector((0, 0, fork + (height - fork) * 0.55))
    crown_r = Vector((height * 0.3, height * 0.3, (height - fork) * 0.5))
    ends = []
    for b in range(limbs):
        az = b / limbs * 2 * math.pi + rng.uniform(-0.35, 0.35)
        z0 = fork + rng.uniform(-0.08, 0.12) * height
        el = math.radians(rng.uniform(18, 42))
        L = height * rng.uniform(0.3, 0.4)
        d = Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))
        start = Vector((0, 0, z0)) + lean * z0
        segs = 5 if lod == 0 else 3
        pts = [start + d * L * (s / segs) + Vector((0, 0, (s / segs) ** 2 * L * 0.18)) for s in range(segs + 1)]
        r0 = trunk_r * 0.55
        tube(bm, uvl, pts, [r0 * (1 - 0.8 * s / segs) for s in range(segs + 1)], 6 if lod == 0 else 4, 0, 2.2, 1)
        ends.append((pts[-1], d))
        if lod == 0:
            for k in range(2):
                p = pts[rng.randint(2, segs - 1)]
                az2 = az + rng.choice((-1, 1)) * rng.uniform(0.6, 1.1)
                d2 = Vector((math.cos(az2) * 0.8, math.sin(az2) * 0.8, rng.uniform(0.3, 0.7))).normalized()
                L2 = L * rng.uniform(0.35, 0.5)
                pts2 = [p + d2 * L2 * (s / 3) for s in range(4)]
                tube(bm, uvl, pts2, [r0 * 0.35 * (1 - 0.8 * s / 3) for s in range(4)], 4, 0, 2.2, 1)
                ends.append((pts2[-1], d2))
    # Leaf clumps: a dome of twig cards round every branch end, then a fill inside the crown so it reads solid.
    per_clump = 16 if lod == 0 else 6
    size_k = 1.0 if lod == 0 else 1.5
    for p, d in ends:
        rc = height * rng.uniform(0.085, 0.12)
        for _ in range(per_clump):
            w = rng.uniform(-0.35, 1.0)
            u = rng.uniform(0, 2 * math.pi)
            dirv = Vector((math.cos(u) * math.sqrt(1 - w * w), math.sin(u) * math.sqrt(1 - w * w), w))
            centre = p + dirv * rc * rng.uniform(0.45, 1.0) + Vector((0, 0, rc * 0.25))
            normal = (dirv * 0.65 + Vector((0, 0, 0.55)) + Vector((rng.uniform(-.3, .3), rng.uniform(-.3, .3), 0))).normalized()
            along = Vector((dirv.x, dirv.y, max(dirv.z, 0) * 0.5)) if dirv.xy.length > 0.15 else Vector((d.x, d.y, 0))
            card(bm, uvl, centre, normal, along, height * rng.uniform(0.11, 0.15) * size_k, rng.randrange(4))
    fill = 34 if lod == 0 else 12
    for _ in range(fill):
        u = rng.uniform(0, 2 * math.pi); w = rng.uniform(-0.2, 1.0)
        dirv = Vector((math.cos(u) * math.sqrt(1 - w * w), math.sin(u) * math.sqrt(1 - w * w), w))
        centre = crown_c + Vector((dirv.x * crown_r.x, dirv.y * crown_r.y, dirv.z * crown_r.z)) * rng.uniform(0.35, 0.8)
        normal = (dirv * 0.5 + Vector((0, 0, 0.8))).normalized()
        along = Vector((dirv.x, dirv.y, 0.15)) if dirv.xy.length > 0.1 else Vector((1, 0, 0))
        card(bm, uvl, centre, normal, along, height * rng.uniform(0.13, 0.17) * size_k, rng.randrange(4))
    me = bpy.data.meshes.new(f"{SPECIES}_LOD{lod}")
    bm.to_mesh(me); bm.free()
    obj = bpy.data.objects.new(f"{SPECIES}_LOD{lod}", me)
    scene.collection.objects.link(obj)
    return obj


def generated_materials(obj):
    bark = bpy.data.materials.new(SPECIES + "_Bark"); leaf = bpy.data.materials.new(SPECIES + "_Leaf")
    for m, img in ((bark, os.path.join(PH_DIR, "island_tree_01/textures/island_tree_01_branches_diff_1k.jpg")),
                   (leaf, os.path.join(TWIG_DIR, "CanopyTree_Twig_Albedo.png"))):
        m.use_nodes = True
        bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        tex = m.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(img, check_existing=True)
        m.node_tree.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
        if m is leaf:
            m.node_tree.links.new(tex.outputs[1], bsdf.inputs["Alpha"])
    obj.data.materials.append(bark); obj.data.materials.append(leaf)


def grass_clump(seed, height, blades, lod):
    """A tuft of curved blade strips cut from the scanned blade atlas (twig dir Grass_Blades.*)."""
    rects = json.load(open(os.path.join(TWIG_DIR, "Grass_Blades.json")))
    rng = random.Random(seed)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    count = blades if lod == 0 else blades // 2
    segs = 3 if lod == 0 else 2
    for _ in range(count):
        x0, y0, x1, y1 = rects[rng.randrange(len(rects))]
        h = height * rng.uniform(0.5, 1.0) * (y1 - y0) / 0.95
        w = h * (x1 - x0) / (y1 - y0) * (1.6 if lod == 0 else 2.2)
        az = rng.uniform(0, 2 * math.pi)
        out = Vector((math.cos(az), math.sin(az), 0))
        lean = math.radians(rng.uniform(8, 40))
        twist = az + math.pi / 2 + rng.uniform(-0.6, 0.6)
        side = Vector((math.cos(twist), math.sin(twist), 0)) * w / 2
        base = out * rng.uniform(0, height * 0.12)
        rows = []
        for k in range(segs + 1):
            t = k / segs
            # Rises, then arches outwards and droops a little at the tip.
            p = base + Vector((0, 0, h * t * math.cos(lean * t))) + out * h * (t ** 1.6) * math.sin(lean) * 1.2
            v = y0 + (y1 - y0) * t
            rows.append(((bm.verts.new(p - side), (x0, v)), (bm.verts.new(p + side), (x1, v))))
        for k in range(segs):
            (a, ua), (b, ub) = rows[k]
            (c, uc), (d, ud) = rows[k + 1]
            f = bm.faces.new([a, b, d, c])
            for loop, uv in zip(f.loops, (ua, ub, ud, uc)):
                loop[uvl].uv = uv
    me = bpy.data.meshes.new(f"{SPECIES}_LOD{lod}")
    bm.to_mesh(me); bm.free()
    obj = bpy.data.objects.new(f"{SPECIES}_LOD{lod}", me)
    scene.collection.objects.link(obj)
    mat = bpy.data.materials.get(SPECIES + "_Blades") or bpy.data.materials.new(SPECIES + "_Blades")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    if not bsdf.inputs["Base Color"].links:
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(TWIG_DIR, "Grass_Blades_Albedo.png"), check_existing=True)
        mat.node_tree.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
        mat.node_tree.links.new(tex.outputs[1], bsdf.inputs["Alpha"])
    obj.data.materials.append(mat)
    return obj


# ---------------------------------------------------------------- species table

SKF = 0.01  # Sketchfab exports in centimetres
TP = {  # name: (source groups, target height m, leaf material keywords, sphere blend, stiffness, LOD1 mode)
    "Banana_A": (["SM_MZRa_Banana_B091"], 3.6, ["B09a"], 0.25, 1.3, "decimate"),
    "Banana_B": (["SM_MZRa_Banana_B092"], 4.2, ["B09a"], 0.25, 1.3, "decimate"),
    "Palm_A": (["SM_MZRa_Palm_B081"], 9.0, ["B08a"], 0.3, 2.2, "decimate"),
    "Palm_B": (["SM_MZRa_Palm_B083"], 11.0, ["B08a"], 0.3, 2.2, "decimate"),
    "Fern_A": (["SM_MZRa_Fern_B0512"], 0.95, ["Fern"], 0.45, 1.0, "decimate"),
    "Fern_B": (["SM_MZRa_Fern_B0532"], 1.1, ["Fern"], 0.45, 1.0, "decimate"),
    "Monstera_A": (["tree.002SM_MZRa_Monstera_B074"], 1.3, ["B07a"], 0.35, 1.2, "decimate"),
    "Monstera_B": (["tree.007SM_MZRa_Monstera_B072"], 1.7, ["B07a"], 0.35, 1.2, 0.6),
    "BambooYoung": (["bambooBranch.019_1", "bambooBranch.020_0"], 3.2, ["alpha"], 0.3, 1.4, "decimate"),
}
PH = {  # name: (gltf asset, nodes, target height, sphere blend, stiffness, LOD0 ratio, LOD1 ratio, leafy)
    "Shrub": ("shrub_02", ["shrub_02_b"], 1.9, 0.5, 1.2, 0.4, 0.15, True),
    "Calathea": ("calathea_orbifolia_01", ["calathea_orbifolia_01_d"], 0.8, 0.35, 1.0, 0.8, 0.35, True),
    "FernSmall": ("fern_02", ["fern_02_a"], 0.7, 0.4, 1.0, 1.0, 0.45, True),
    # A 3 m fallen log scan; negative height = keep real size times this scale. Stiffness 40: it does not sway.
    "Log": ("dead_tree_trunk", ["dead_tree_trunk"], -1.25, 0.0, 40.0, 0.02, 0.006, False),
}
GRASS = {"GrassClump": (5, 0.6, 40), "GrassTall": (11, 1.3, 46)}   # name: (seed, height m, blades)
TREES = {"CanopyTree_A": (31, 19.0, 0.6, 7), "CanopyTree_B": (47, 11.5, 0.42, 6)}

rng = random.Random(hash(SPECIES) & 0xffff)
info = {"name": SPECIES, "materials": []}
lods = []

if SPECIES in TREES:
    seed, height, crown, limbs = TREES[SPECIES]
    for lod in (0, 1):
        o = canopy_tree(seed, height, crown, limbs, lod)
        generated_materials(o)
        paint(o, {1}, 1.8, 0.55, random.Random(seed + lod), ao_floor=0.4)
        lods.append(o)
    leaf_names = {SPECIES + "_Leaf"}
    save = {SPECIES + "_Bark": (bpy.data.images.load(os.path.join(PH_DIR, "island_tree_01/textures/island_tree_01_branches_diff_1k.jpg"), check_existing=True),
                                 bpy.data.images.load(os.path.join(PH_DIR, "island_tree_01/textures/island_tree_01_branches_nor_gl_1k.jpg"), check_existing=True), False),
            SPECIES + "_Leaf": (bpy.data.images.load(os.path.join(TWIG_DIR, "CanopyTree_Twig_Albedo.png"), check_existing=True),
                                 bpy.data.images.load(os.path.join(TWIG_DIR, "CanopyTree_Twig_Normal.png"), check_existing=True), True)}
    tile = 1024
elif SPECIES in TP or SPECIES == "Bamboo":
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
elif SPECIES in GRASS:
    seed, height, blades = GRASS[SPECIES]
    for lod in (0, 1):
        o = grass_clump(seed, height, blades, lod)
        paint(o, {0}, 1.5, 0.5, random.Random(seed + lod), ao_floor=0.55)
        lods.append(o)
    save = {SPECIES + "_Blades": (bpy.data.images.load(os.path.join(TWIG_DIR, "Grass_Blades_Albedo.png"), check_existing=True),
                                   bpy.data.images.load(os.path.join(TWIG_DIR, "Grass_Blades_Normal.png"), check_existing=True), True)}
    tile = 512
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
        entry["cutout"] = save_image(base, os.path.join(OUT, new + "_Albedo.png"), 2048 if SPECIES.startswith("CanopyTree") else 1024)
    if nrm:
        save_image(nrm, os.path.join(OUT, new + "_Normal.png"), 1024, non_color=True)
        entry["normal"] = True
    info["materials"].append(entry)

card = impostor(lods[0], tile)
info["materials"].append({"name": SPECIES + "_Card", "leaf": True, "cutout": True, "normal": False, "card": True})
lods.append(card)
xs = [v.co for v in lods[0].data.vertices]
info["height"] = round(max(c.z for c in xs), 3)
info["radius"] = round(max(math.hypot(c.x, c.y) for c in xs), 3)
info["tris"] = [tris(o) for o in lods]

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
