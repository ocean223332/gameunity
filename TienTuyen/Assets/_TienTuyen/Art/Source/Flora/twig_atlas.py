# Background Blender: renders leafy twig cards (albedo+alpha and tangent-space normal) into a 2x2 atlas
# for the generated canopy trees. Leaves come from ambientCG LeafSet scans (CC0), passed as arguments.
# usage: blender -b --factory-startup -P twig_atlas.py -- <out_dir> <leafset_dir>...
import bpy, bmesh, sys, os, math, random
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
only = None
if argv[0] == "--only":
    only = int(argv[1]); argv = argv[2:]
out_dir, leaf_dirs = argv[0], argv[1:]
TILE = 1024


def load(path):
    return bpy.data.images.load(path, check_existing=True)


def leaf_rects(opacity_img):
    """Bounding boxes (u0,v0,u1,v1) of the separate leaves in a LeafSet opacity map."""
    w, h = opacity_img.size
    px = np.array(opacity_img.pixels[:], dtype=np.float32).reshape(h, w, 4)[:, :, 0]
    s = 4
    small = px[::s, ::s] > 0.5
    H, W = small.shape
    labels = np.zeros((H, W), np.int32)
    n = 0
    boxes = []
    for y in range(H):
        for x in range(W):
            if small[y, x] and labels[y, x] == 0:
                n += 1
                stack = [(y, x)]
                labels[y, x] = n
                y0 = y1 = y
                x0 = x1 = x
                count = 0
                while stack:
                    cy, cx = stack.pop()
                    count += 1
                    y0 = min(y0, cy); y1 = max(y1, cy); x0 = min(x0, cx); x1 = max(x1, cx)
                    for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                        if 0 <= ny < H and 0 <= nx < W and small[ny, nx] and labels[ny, nx] == 0:
                            labels[ny, nx] = n
                            stack.append((ny, nx))
                if count > 200:
                    # Blender pixel rows run bottom-up, so y is already v.
                    boxes.append(((x0 - 1) / W, (y0 - 1) / H, (x1 + 2) / W, (y1 + 2) / H))
    return boxes


scene = bpy.context.scene
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
try:
    scene.render.engine = 'BLENDER_EEVEE'
except TypeError:
    pass
scene.render.resolution_x = TILE
scene.render.resolution_y = TILE
scene.render.film_transparent = True
scene.view_settings.view_transform = 'Standard'
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
try:
    scene.eevee.taa_render_samples = 32
except Exception:
    pass
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = 1.0
cam.location = (0, 0, 5)
cam.rotation_euler = (0, 0, 0)


def emission_normal(nt, emit):
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    vm = nt.nodes.new("ShaderNodeVectorMath")
    vm.operation = 'MULTIPLY_ADD'
    vm.inputs[1].default_value = (0.5, 0.5, 0.5)
    vm.inputs[2].default_value = (0.5, 0.5, 0.5)
    nt.links.new(geo.outputs["Normal"], vm.inputs[0])
    nt.links.new(vm.outputs[0], emit.inputs[0])


def leaf_material(name, color_path, opacity_path, mode):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    try:
        m.surface_render_method = 'DITHERED'
    except Exception:
        pass
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    uv = nt.nodes.new("ShaderNodeUVMap")
    col = nt.nodes.new("ShaderNodeTexImage")
    col.image = load(color_path)
    op = nt.nodes.new("ShaderNodeTexImage")
    op.image = load(opacity_path)
    op.image.colorspace_settings.name = 'Non-Color'
    nt.links.new(uv.outputs[0], col.inputs[0])
    nt.links.new(uv.outputs[0], op.inputs[0])
    emit = nt.nodes.new("ShaderNodeEmission")
    if mode == "albedo":
        tint = nt.nodes.new("ShaderNodeVertexColor")
        tint.layer_name = "tint"
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type = 'RGBA'
        mul.blend_type = 'MULTIPLY'
        mul.inputs[0].default_value = 1.0
        nt.links.new(col.outputs[0], mul.inputs[6])
        nt.links.new(tint.outputs[0], mul.inputs[7])
        nt.links.new(mul.outputs[2], emit.inputs[0])
    else:
        # Seen from the top-down camera, the world normal is the card's tangent-space normal.
        emission_normal(nt, emit)
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(op.outputs[0], mix.inputs[0])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(emit.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
    return m


def stem_material(mode):
    m = bpy.data.materials.new("stem_" + mode)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    if mode == "albedo":
        emit.inputs[0].default_value = (0.16, 0.11, 0.06, 1)
    else:
        emission_normal(nt, emit)
    nt.links.new(emit.outputs[0], out.inputs[0])
    return m


def build_twig(leafset, seed, leaves_per_side, leaf_len, spread):
    """A branchlet: a main stem with side twigs, leaves alternating along all of them, fitted to the unit card."""
    r = random.Random(seed)
    color = [f for f in os.listdir(leafset) if f.endswith("_Color.jpg")][0]
    opac = [f for f in os.listdir(leafset) if f.endswith("_Opacity.jpg")][0]
    rects = leaf_rects(load(os.path.join(leafset, opac)))
    mesh = bpy.data.meshes.new("twig")
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    tint = bm.loops.layers.float_color.new("tint")
    stems = []

    def leaf(base, direction, length):
        u0, v0, u1, v1 = rects[r.randrange(len(rects))]
        aspect = (u1 - u0) / max(v1 - v0, 1e-3)
        width = length * aspect
        d = direction.normalized()
        side = Vector((-d.y, d.x, 0)).normalized()
        side = Matrix.Rotation(r.uniform(-0.5, 0.5), 3, d) @ side
        lift = Vector((0, 0, r.uniform(-0.04, 0.06)))
        pts = (base - side * width / 2, base + side * width / 2,
               base + d * length + side * width / 2 + lift, base + d * length - side * width / 2 + lift)
        f = bm.faces.new([bm.verts.new(p) for p in pts])
        uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        shade = r.uniform(0.78, 1.08)
        hue = r.uniform(-0.05, 0.05)
        for loop, uv in zip(f.loops, uvs):
            loop[uvl].uv = uv
            loop[tint] = (shade * (1 - hue), shade, shade * (1 + hue * 0.5), 1)

    def branch(start, direction, length, depth):
        d = direction.normalized()
        stems.append((start, start + d * length, 0.006 * (2 - depth)))
        n = leaves_per_side if depth else leaves_per_side + 2
        for i in range(n):
            t = (i + 0.6) / (n + 0.4)
            p = start + d * length * t
            for s in (-1, 1):
                ld = Matrix.Rotation(s * r.uniform(0.6, 1.1) * spread, 3, 'Z') @ d
                leaf(p + Vector((0, 0, 0.001 * i)), ld, leaf_len * r.uniform(0.75, 1.1) * (1 - 0.35 * t * t))
        leaf(start + d * length, d, leaf_len * 0.9)
        if depth == 0:
            for i in range(r.randint(2, 3)):
                t = r.uniform(0.25, 0.65)
                s = 1 if i % 2 else -1
                bd = Matrix.Rotation(s * r.uniform(0.5, 0.9), 3, 'Z') @ d
                branch(start + d * length * t, bd, length * r.uniform(0.45, 0.6), 1)

    branch(Vector((0, -0.48, 0)), Vector((r.uniform(-0.1, 0.1), 1, 0)), 0.78, 0)
    for a, b, rad in stems:
        d = b - a
        side = Vector((-d.y, d.x, 0)).normalized() * rad
        f = bm.faces.new([bm.verts.new(p) for p in (a - side, a + side, b + side * 0.5, b - side * 0.5)])
        for loop in f.loops:
            loop[uvl].uv = (0, 0)
            loop[tint] = (1, 1, 1, 1)
        f.material_index = 1
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("twig", mesh)
    scene.collection.objects.link(obj)
    # Fit into the unit card with a small margin.
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    scale = 0.94 / max(max(xs) - min(xs), max(ys) - min(ys))
    cx = (max(xs) + min(xs)) / 2
    cy = (max(ys) + min(ys)) / 2
    for v in mesh.vertices:
        v.co = Vector(((v.co.x - cx) * scale, (v.co.y - cy) * scale, v.co.z * scale))
    return obj, (os.path.join(leafset, color), os.path.join(leafset, opac))


def bleed(px):
    """Spread leaf colour into transparent texels so mipmaps do not fringe dark; un-premultiply edges."""
    rgb = px[:, :, :3].copy()
    a = px[:, :, 3]
    cov = a > 0.3
    fill = rgb.copy()
    fill[cov] = rgb[cov] / np.maximum(a[cov], 1e-3)[:, None]
    known = cov.copy()
    for _ in range(24):
        m = known.astype(np.float32)
        acc = np.zeros_like(rgb)
        cnt = np.zeros(a.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(np.roll(fill * m[..., None], dy, 0), dx, 1)
            cnt += np.roll(np.roll(m, dy, 0), dx, 1)
        grow = (~known) & (cnt > 0)
        fill[grow] = acc[grow] / cnt[grow][:, None]
        known = known | grow
    fill[~known] = rgb[cov].mean(axis=0) if cov.any() else (0.2, 0.3, 0.1)
    return np.concatenate([np.clip(fill, 0, 1), a[..., None]], axis=2)


specs = [  # (leafset index, seed, leaves per side, leaf length, spread)
    (0, 11, 6, 0.17, 1.0), (0, 12, 5, 0.2, 0.9), (1, 13, 6, 0.16, 1.05), (2, 14, 4, 0.26, 0.8)]
atlas = {m: np.zeros((TILE * 2, TILE * 2, 4), np.float32) for m in ("albedo", "normal")}
for k, (li, seed, n, ll, sp) in enumerate(specs):
    path0 = os.path.join(out_dir, f"_twig{k}_normal.png")
    if only is not None and k != only:
        if not os.path.exists(path0): continue
        for mode in ("albedo", "normal"):
            img = bpy.data.images.load(os.path.join(out_dir, f"_twig{k}_{mode}.png"))
            if mode == "normal": img.colorspace_settings.name = 'Non-Color'
            atlas[mode][(k // 2) * TILE:(k // 2 + 1) * TILE, (k % 2) * TILE:(k % 2 + 1) * TILE] = np.array(img.pixels[:], np.float32).reshape(TILE, TILE, 4)
            bpy.data.images.remove(img)
        continue
    obj, (cpath, opath) = build_twig(leaf_dirs[li], seed, n, ll, sp)
    for mode in ("albedo", "normal"):
        obj.data.materials.clear()
        obj.data.materials.append(leaf_material(f"leaf_{k}_{mode}", cpath, opath, mode))
        obj.data.materials.append(stem_material(mode))
        scene.view_settings.view_transform = 'Standard'
        if mode == "normal":
            try:
                scene.view_settings.view_transform = 'Raw'
            except TypeError:
                pass
        path = os.path.join(out_dir, f"_twig{k}_{mode}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        if mode == "normal":
            img.colorspace_settings.name = 'Non-Color'
        px = np.array(img.pixels[:], np.float32).reshape(TILE, TILE, 4)
        tx, ty = k % 2, k // 2
        atlas[mode][ty * TILE:(ty + 1) * TILE, tx * TILE:(tx + 1) * TILE] = px
        bpy.data.images.remove(img)
    bpy.data.objects.remove(obj)

if only is not None and not all(os.path.exists(os.path.join(out_dir, f"_twig{k}_normal.png")) for k in range(len(specs))):
    print("TWIG DONE", only); sys.exit(0)
alpha = atlas["albedo"][:, :, 3].copy()
final = {"Albedo": bleed(atlas["albedo"])}
nrm = atlas["normal"]
flat = np.array([0.5, 0.5, 1.0], np.float32)
nrm[:, :, :3] = np.where(alpha[..., None] > 0.01, nrm[:, :, :3] / np.maximum(alpha, 1e-3)[..., None], flat)
nrm[:, :, 3] = 1
final["Normal"] = np.clip(nrm, 0, 1)
for name, px in final.items():
    img = bpy.data.images.new("atlas_" + name, TILE * 2, TILE * 2, alpha=True)
    if name == "Normal":
        img.colorspace_settings.name = 'Non-Color'
    img.pixels[:] = px.ravel()
    img.filepath_raw = os.path.join(out_dir, f"CanopyTree_Twig_{name}.png")
    img.file_format = 'PNG'
    img.save()
print("TWIG ATLAS DONE")
