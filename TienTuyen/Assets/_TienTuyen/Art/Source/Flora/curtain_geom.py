# curtain_geom.py: the cards of a hanging vine curtain, in plain numpy (curtain_gen.py builds them in Blender).
# Blender axes: z up, the curtain hangs down from the lip at z = 0 towards -z, the river (outward) is -y, the shelf +y.
# A card is (corners 4x3, uv rect (u0, v0, u1, v1), mirrored, material): corners top-left, top-right, bottom-right,
# bottom-left as seen from the river.
import math
import numpy as np

# Atlas pieces (u0, v0, u1, v1) measured from the pack textures' alpha.
IVY_CASCADE = (0.011, 0.410, 0.339, 0.987)      # Ivy_herb_basecolor_4K: the long hanging ivy, attached at its top
ROOTS = [(0.50, 0.028, 0.699, 0.546), (0.700, 0.031, 0.849, 0.512), (0.850, 0.033, 0.986, 0.509)]  # Monstera_Obliqua_4K
IVY_ASPECT = 1.75                                # height / width of the cascade piece on the square atlas
# The curtains: name, length (m), width (m), seed. A middling sheet, a long one for big drops, a narrow tongue.
CURTAINS = (("Curtain_A", 4.5, 2.6, 11), ("Curtain_B", 7.5, 3.2, 23), ("Curtain_C", 5.0, 1.5, 37))


def _card(centre_top, width, height, yaw, lean, uv, mirrored, material):
    """A card hanging from centre_top: turned by yaw about z, its foot swung outward (-y) by lean (radians)."""
    c, s = math.cos(yaw), math.sin(yaw)
    across = np.array((c, s, 0.0)) * width / 2
    down = np.array((0.0, -math.sin(lean), -math.cos(lean))) * height
    top = np.asarray(centre_top, float)
    corners = np.array([top - across, top + across, top + across + down, top - across + down])
    return corners, uv, mirrored, material


def envelope(seed, width):
    """How far the strands reach at x across the curtain, 0..1: longest near a centre that wanders, tapering to the
    sides with a bump or two, so the outline is a ragged tongue, never a straight cut. Shared by both LODs."""
    rng = np.random.default_rng(seed + 1000)
    centre, k, phase = rng.uniform(-0.2, 0.2) * width, rng.uniform(1.2, 2.4), rng.uniform(0, 2 * math.pi)
    def reach(x):
        t = min(1.0, abs(x - centre) / (width * 0.6))
        return float(np.clip(1.0 - 0.6 * t ** 1.4 + 0.14 * math.sin(2 * math.pi * k * x / width + phase), 0.25, 1.0))
    return reach


def curtain(seed, height, width, lod):
    """Cards of one curtain about `height` m long and `width` m wide; lod 1 keeps a third of the cards, larger."""
    rng = np.random.default_rng(seed)
    reach = envelope(seed, width)
    cards = []
    strands = max(3, int(round(width / (0.3 if lod == 0 else 0.7))))
    scale = 1.0 if lod == 0 else 1.6
    for i in range(strands):
        x = (i + 0.5) / strands * width - width / 2 + rng.uniform(-0.12, 0.12)
        depth = rng.uniform(0.08, 0.45)                     # front and back layers
        length = height * reach(x) * rng.uniform(0.72, 1.08)
        w = rng.uniform(0.55, 0.8) * scale
        # Over the lip: one card lies back on the shelf and bends over the edge, so the curtain grows from the ledge.
        cards.append(_card((x, rng.uniform(0.2, 0.45), rng.uniform(0.0, 0.25)), w * 1.1, rng.uniform(0.6, 1.0), rng.uniform(-0.3, 0.3), math.radians(rng.uniform(50, 72)),
                           IVY_CASCADE, bool(rng.integers(2)), "Ivy"))
        # Then cards down the strand, each overlapping the one above so the joins vanish in the leaves.
        z = -0.3
        while -z < length:
            if -z > length * 0.7:
                w = max(w * 0.8, 0.3 * scale)                # the tip narrows towards a point
            h = w * IVY_ASPECT * rng.uniform(0.9, 1.1)
            cards.append(_card((x + rng.uniform(-0.08, 0.08), -depth, z), w, h, rng.uniform(-0.35, 0.35),
                               math.radians(rng.uniform(-4, 8)), IVY_CASCADE, bool(rng.integers(2)), "Ivy"))
            z -= h * 0.6
            w *= rng.uniform(0.88, 1.0)                     # strands thin towards their tips
    # A few aerial roots, longer than the leaves and nearer the stone.
    for i in range(3 if lod == 0 else 1):
        x = rng.uniform(-width / 2, width / 2)
        uv = ROOTS[int(rng.integers(len(ROOTS)))]
        aspect = (uv[3] - uv[1]) / (uv[2] - uv[0])
        w = 0.3 * scale
        z = -0.1
        while -z < height * rng.uniform(0.6, 1.05):
            h = w * aspect
            cards.append(_card((x, 0.05, z), w, h, rng.uniform(-0.4, 0.4), 0.0, uv, bool(rng.integers(2)), "Root"))
            z -= h * 0.85
    return cards


def mesh(cards):
    """Vertices, quads, per-corner uvs and per-quad material names."""
    verts, quads, uvs, mats = [], [], [], []
    for corners, (u0, v0, u1, v1), mirrored, material in cards:
        n = len(verts)
        verts.extend(map(tuple, corners))
        quads.append((n, n + 1, n + 2, n + 3))
        a, b = (u1, u0) if mirrored else (u0, u1)
        uvs.append(((a, v1), (b, v1), (b, v0), (a, v0)))
        mats.append(material)
    return np.array(verts), quads, uvs, mats


if __name__ == "__main__":
    for name, h, w, seed in CURTAINS:
        for lod in (0, 1):
            v, q, _, m = mesh(curtain(seed, h, w, lod))
            lo, hi = v.min(0), v.max(0)
            print(name, "LOD%d" % lod, "quads", len(q), "tris", 2 * len(q), "ivy", m.count("Ivy"), "roots", m.count("Root"),
                  "bounds", np.round(lo, 2), np.round(hi, 2))
