# Bakes the distant landscape (Art/Resources/Environment/Mountains.bytes): ranges from real elevation data and the
# limestone tower karst standing in front of them, with what EnvKarst.hlsl needs to lay valley cloud between them.
#
#   python -I build_mountains.py <terrarium_tile_dir> <out.bytes>
#
# Source: AWS Terrain Tiles, terrarium PNGs z12 x 3252-3260 y 1842-1851 (SRTM, public domain; see docs/CREDITS.md),
# downloaded as {x}_{y}.png from https://s3.amazonaws.com/elevation-tiles-prod/terrarium/12/{x}/{y}.png.
# The station sits at mosaic pixel (1150, 1100) in the Phong Nha - Ke Bang / Truong Son country. Terrain is
# rescaled for the game camera: distances x0.4, relief x1.9 rising with distance, the ranges lifted per azimuth to set
# elevation angles (lower in front, so the towers and the far layers read), and the first kilometres kept low so
# the game's own hills (EnvironmentLayout.BaseHeight) own the foreground. EnvironmentBackdrop adds those hills
# at runtime. The towers are separate meshes (a sheer, fluted wall on a ragged footprint under a forested crown),
# far finer than the polar grid, which only carries the forested mound each one stands on.
#
# The river of EnvironmentLayout.River runs on past the ends of the game's map as a gorge cut through the ranges, nearly
# straight away from the station east and west, framed by towers; its near reaches get a corridor mesh fine enough to
# carry the water (kind 2), and the rings record how far each vertex lies from the river.
#
# Layout (little endian): "TTMB" int32 version=4, rings, columns, meshes; float32 radii[rings];
# float32 azimuth_deg[columns] (0 = +Z north, 90 = +X east); float16 height[rings][columns] (virtual metres);
# uint8 sun[rings][columns]; uint8 sky[rings][columns]; uint8 cavity[rings][columns] (128 = flat);
# uint8 rise[rings][columns] (degrees x 20 above the ranges in front, where valley cloud lies; see "rise");
# uint8 river[rings][columns] (distance to the river's centreline / 4 m, 255 farther);
# then per mesh: int32 kind (0 tower, 1 cloud skirt, 2 river corridor), vertices, indices; float32 position[vertices][3];
# float16 normal[vertices][3] (kind 2: x the weight of the baked height against the game's hills, y the distance to
# the river (m); the runtime computes the normals);
# uint8 color[vertices][4] (R sun visibility, G sky visibility, B cavity (128 flat), A height up the wall, 0 foot ..
# 255 crown; kind 2: distance to the river x 4 m, the water where under 14 m); uint8 rise[vertices]; uint16 index[indices].
import os, sys, struct, math
import numpy as np
from PIL import Image

TILES, OUT = sys.argv[1], sys.argv[2]
X0, X1, Y0, Y1 = 3252, 3260, 1842, 1851
ROW, COL, PX = 1150, 1100, 36.44            # station pixel, metres per pixel at 17.5 N
HS, VS, FAR_LIFT = .4, 1.9, .35
SUN_FORWARD = np.array([.55, -.4, .73]); SUN_FORWARD /= np.linalg.norm(SUN_FORWARD)   # Sunny LightingPreset

def smooth(x): x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)

_lattice = np.random.default_rng(1972).random(1 << 16)
def noise(x, z, seed=0, period=None):
    """Value noise in [0, 1] on a unit lattice; with a period, x wraps every period cells."""
    xi, zi = np.floor(x), np.floor(z); fx, fz = x - xi, z - zi
    fx, fz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
    xi, zi = xi.astype(np.int64), zi.astype(np.int64)
    x1 = xi + 1
    if period is not None: xi, x1 = xi % period, x1 % period
    xi, x1 = xi + 7919 * seed, x1 + 7919 * seed
    at = lambda i, j: _lattice[(i * 73856093 ^ j * 19349663) & 0xFFFF]
    return (at(xi, zi) * (1 - fx) + at(x1, zi) * fx) * (1 - fz) + (at(xi, zi + 1) * (1 - fx) + at(x1, zi + 1) * fx) * fz

def fbm(x, z, octaves=4, seed=0, ridged=False, period=None):
    total, amp, norm = 0.0, .5, 0.0
    for o in range(octaves):
        n = noise(x, z, seed + o, period)
        total = total + amp * ((1 - np.abs(2 * n - 1)) if ridged else n); norm += amp
        if period is None: x, z = x * 2.03 + 17.1, z * 2.03 + 5.3
        else: x, z, period = x * 2, z * 2.03 + 5.3, period * 2
        amp *= .5
    return total / norm

# ---------------- elevation ----------------
rows = []
for y in range(Y0, Y1 + 1):
    row = []
    for x in range(X0, X1 + 1):
        a = np.asarray(Image.open(os.path.join(TILES, f"{x}_{y}.png")).convert("RGB"), dtype=np.float64)
        row.append(a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768)
    rows.append(np.hstack(row))
dem = np.maximum(np.vstack(rows), 0)
k = np.array([1., 2., 1.]) / 4               # one binomial pass: SRTM speckle turns into needles once exaggerated
dem = np.apply_along_axis(lambda v: np.convolve(np.pad(v, 1, mode="edge"), k, "valid"), 0, dem)
dem = np.apply_along_axis(lambda v: np.convolve(np.pad(v, 1, mode="edge"), k, "valid"), 1, dem)
E0 = float(np.median(dem[ROW - 25:ROW + 26, COL - 25:COL + 26]))

def sample_dem(rr, cc):
    rr = np.clip(rr, 0, dem.shape[0] - 2); cc = np.clip(cc, 0, dem.shape[1] - 2)
    r0, c0 = rr.astype(int), cc.astype(int); fr, fc = rr - r0, cc - c0
    return (dem[r0, c0] * (1 - fr) * (1 - fc) + dem[r0 + 1, c0] * fr * (1 - fc) +
            dem[r0, c0 + 1] * (1 - fr) * fc + dem[r0 + 1, c0 + 1] * fr * fc)

def raw_height(r, az_deg):
    """DEM relief at virtual radius r (m) and azimuth, before the composition lift."""
    a = np.radians(az_deg); real = r / HS
    d = (sample_dem(ROW - np.cos(a) * real / PX, COL + np.sin(a) * real / PX) - E0) * VS
    d = d * (1 + FAR_LIFT * smooth((r - 3000.0) / 12000.0))
    w = smooth((r - 250.0) / 2750.0)
    d = d * (.15 + .85 * w)
    return np.maximum(d, -60.0 - 400.0 * w)

# ---------------- polar grid ----------------
radii = [96.0]
while radii[-1] < 24000.0:   # finer rings through the middle distance, where the ranges fill most of the view
    r_ = radii[-1]           # and every 8 m just past the terrain edge, where they carry BaseHeight's river banks
    radii.append(r_ + 8.0 if 320.0 <= r_ < 720.0 else r_ * (1.012 if 2000.0 <= r_ <= 9000.0 else 1.028) + 2.5)
radii = np.array(radii)
def density(a):   # columns per degree: dense where the game can see sky (south arc, eastern sightline)
    a = np.mod(a, 360.0)
    vis = smooth((a - 50.0) / 15.0) * (1 - smooth((a - 275.0) / 15.0))
    return 1 / 1.5 + (4.0 - 1 / 1.5) * vis
fine = np.arange(0, 360, .05)
cum = np.concatenate([[0], np.cumsum(density(fine[:-1]) * .05)])
cols = int(round(cum[-1] + density(359.99) * .05))
azimuths = np.interp(np.arange(cols) * (cum[-1] + density(359.99) * .05) / cols, cum, fine)

# Composition: target elevation angle of the ranges 2-8 km out, per azimuth: a continuous massif, highest to the east
# and south, with the valley mouth to the south open on the far ranges. The towers stand in front of it.
def target_angle(a):
    a = np.mod(a, 360.0)
    wobble = .5 + .5 * np.sin(np.radians(a) * 7.0 + 1.3) * np.cos(np.radians(a) * 3.0)
    south = smooth((a - 135.0) / 15.0) * (1 - smooth((a - 255.0) / 15.0))
    notch = np.exp(-((a - 186.0) / 11.0) ** 2)
    east = smooth((a - 55.0) / 10.0) * (1 - smooth((a - 125.0) / 10.0))
    t = 11.0 + 4.0 * wobble
    t = t * (1 - south) + (17.0 + 7.0 * wobble) * south
    t = t * (1 - notch * south) + 8.0 * notch * south
    t = t * (1 - east) + (22.0 + 5.0 * wobble) * east
    return t
band = lambda r: smooth((r - 1800.0) / 1200.0) * (1 - smooth((r - 7000.0) / 5000.0))
A, R = np.meshgrid(azimuths, radii)
raw = raw_height(R, A)
# Broad mass of the relief (about 2.5 degrees by 4 rings): lifting it raises whole massifs while the ridges and
# gullies keep their real scale, instead of stretching every crest into a needle.
def blur(g, ri, cj):
    out = np.zeros_like(g); n = 0
    padded = np.pad(g, ((ri, ri), (0, 0)), mode="edge")
    for di in range(-ri, ri + 1):
        for dj in range(-cj, cj + 1):
            out += np.roll(padded, dj, axis=1)[ri + di:ri + di + g.shape[0]]; n += 1
    return out / n
mass = np.maximum(blur(raw, 4, 10), 0) * band(R)
def column_smooth(v, width=3.0):
    ext = np.concatenate([v, v, v]); az3 = np.concatenate([azimuths - 360, azimuths, azimuths + 360])
    return np.array([ext[np.abs(az3 - a) < width].mean() for a in azimuths])
target = np.tan(np.radians(target_angle(azimuths)))
lift = np.zeros(cols)
for _ in range(3):   # a few fixed-point steps towards the target angle of the highest point in the band
    Hc = raw + lift[None, :] * mass
    achieved = np.where(band(R) > .5, Hc / R, -1).max(0)
    peak_mass = np.take_along_axis(np.where(band(R) > .5, mass / R, 0), np.argmax(np.where(band(R) > .5, Hc / R, -1), 0)[None, :], 0)[0]
    lift = column_smooth(np.clip(lift + (target - achieved) / np.maximum(peak_mass, 1e-3), 0, 4.0))
X, Z = np.sin(np.radians(A)) * R, np.cos(np.radians(A)) * R
# The lift scales a blurred mass, which on its own rounds every crest into rubber: the lifted part gets ridges and
# gullies of its own (ridged noise, a few hundred metres across).
H = raw + lift[None, :] * mass * (1 + .5 * (fbm(X / 300.0, Z / 300.0, 4, 11, ridged=True) - .55))
# Limestone bedding: the lifted massif is cut into warped strata, a steep riser under each flat bench, so its faces
# step like the karst walls rather than slump (the shader adds the finer beds and their ledge forest).
STRATUM = 85.0
lifted = np.clip(lift[None, :] * mass / 250.0, 0, 1)
bed = (H + 60.0 * (fbm(X / 1100.0, Z / 1100.0, 3, 23) - .5)) / STRATUM
f = bed - np.floor(bed)
H = H + lifted * .85 * STRATUM * (smooth((f - .55) / .45) - f)
# Walls the lift raises over a clamped valley floor (a massif 2 km above the -460 m floor 200 m away, east-north-east
# at 6-7 km) read as pale slabs of glass in the haze. Ease any drop of more than 250 m into a slope of at most 1.4:
# every point meets the cut from above and the fill from below half-way. The strata risers stay as they are.
def ease_walls(g, s=1.4, c=250.0, K=5, J=16):
    th = np.radians(azimuths); n = len(radii)
    cut, fill = g.copy(), g.copy()
    for di in range(-K, K + 1):
        i1 = np.clip(np.arange(n) + di, 0, n - 1); rq = radii[i1][:, None]
        for dj in range(-J, J + 1):
            if di == 0 and dj == 0: continue
            dth = np.angle(np.exp(1j * (np.roll(th, -dj) - th)))[None, :]
            d = np.sqrt(radii[:, None] ** 2 + rq ** 2 - 2 * radii[:, None] * rq * np.cos(dth))
            q = np.roll(g[i1], -dj, 1)
            cut = np.minimum(cut, q + c + s * d); fill = np.maximum(fill, q - c - s * d)
    return .5 * (cut + fill)
H = ease_walls(H)

# ---------------- tower karst ----------------
# Limestone towers as in Phong Nha, Ha Long and Ha Giang: sheer fluted walls, a forested crown, standing out of the
# forest in groups. A few heroes are composed by hand either side of the valley mouth and over the eastern
# sightline; the rest are scattered in loose clusters, smaller and paler with distance. Each entry: azimuth deg,
# virtual radius m, summit elevation deg (from the station), footprint radius m, seed, crown (0 low .. 1 tall
# pinnacles), form: "jagged" (a flat top broken into pinnacles), "split" (twin summits either side of a cleft),
# "tilt" (a cap sloping off one side over a leaning wall). Never a dome.
HEROES = [
    (207.0, 2900.0, 31.0, 420.0, 1, .7, "split"), (199.5, 3500.0, 21.0, 300.0, 2, .4, "jagged"),
    (216.0, 3900.0, 19.5, 330.0, 3, .9, "tilt"), (166.0, 3300.0, 28.0, 400.0, 4, .3, "tilt"),
    (157.0, 4300.0, 18.0, 300.0, 5, .8, "jagged"), (174.0, 5200.0, 13.0, 300.0, 6, .5, "split"),
    (76.0, 3300.0, 15.5, 360.0, 7, .6, "tilt"), (94.0, 4200.0, 17.5, 420.0, 8, .9, "jagged"),
    (106.0, 3000.0, 12.5, 280.0, 9, .2, "jagged"), (118.0, 4600.0, 14.0, 360.0, 10, .7, "split"),
    (64.0, 5200.0, 12.0, 320.0, 11, .4, "jagged"),
    # The first and fourth are the giants framing the valley mouth. Lower companions joined to a hero by a forested saddle (SADDLES).
    (220.0, 2950.0, 17.0, 260.0, 12, .8, "jagged"), (90.5, 3430.0, 12.5, 250.0, 13, .5, "split"),
    (177.5, 3200.0, 15.0, 240.0, 14, .6, "tilt"),
]
SADDLES = [(0, 11), (3, 13)]   # hero index pairs (the river runs between heroes 6 and 12)
FORMS = ("jagged", "jagged", "split", "split", "tilt", "tilt", "jagged")
CLUSTERS = [  # azimuth from, to; radius from, to; count; summit elevation at the near radius
    (140.0, 235.0, 5500.0, 9000.0, 14, 12.5), (150.0, 225.0, 9000.0, 14000.0, 12, 9.0),
    (55.0, 130.0, 5500.0, 9000.0, 9, 10.5), (60.0, 125.0, 9000.0, 14000.0, 9, 8.0),
    (235.0, 300.0, 4000.0, 9000.0, 7, 12.0), (300.0, 410.0, 4000.0, 9000.0, 9, 11.0),
]
rng = np.random.default_rng(1975)
towers = [dict(az=a, r=r, el=e, base=b, seed=s, crown=c, form=f) for a, r, e, b, s, c, f in HEROES]
for a0, a1, r0, r1, count, el0 in CLUSTERS:
    placed = 0
    for _ in range(400):
        if placed >= count: break
        a, r = rng.uniform(a0, a1) % 360.0, math.exp(rng.uniform(math.log(r0), math.log(r1)))
        el = el0 * (r0 / r) ** .35 * rng.uniform(.75, 1.15)
        base = math.tan(math.radians(el)) * r / rng.uniform(2.6, 4.2)
        cx, cz = math.sin(math.radians(a)) * r, math.cos(math.radians(a)) * r
        if any(math.hypot(cx - t["cx"], cz - t["cz"]) < (base + t["base"]) * 1.05 for t in towers if "cx" in t): continue
        if any(math.hypot(cx - math.sin(math.radians(t["az"])) * t["r"], cz - math.cos(math.radians(t["az"])) * t["r"]) < (base + t["base"]) * 1.05 for t in towers): continue
        towers.append(dict(az=a, r=r, el=el, base=base, seed=100 + len(towers), crown=float(rng.uniform(0, 1)), cx=cx, cz=cz,
                           form=FORMS[int(len(towers) * 7.31 + 3) % len(FORMS)]))
        placed += 1
for t in towers:
    t["cx"], t["cz"] = math.sin(math.radians(t["az"])) * t["r"], math.cos(math.radians(t["az"])) * t["r"]

# ---------------- river ----------------
# EnvironmentLayout.River (keep in step: its bends, shifted by RiverCentreZ), and past each end the course on through
# the ranges, meandering but nearly straight away from the station, so from the game it cuts a notch through every
# layer and the towers stand either side of it into the distance.
RIVER_CZ = -4.6 - (24.0 + 1.6) / .14 - 14.0 - 6.0
RIVER_GAME = [(-1600, 520), (-900, 210), (-560, 80), (-380, 26), (-250, -4), (-150, 0), (-70, -6), (0, -12), (80, -4),
              (160, -16), (250, 6), (370, 40), (560, 110), (900, 240), (1600, 560)]
RIVER_WEST = [(-2300, 560), (-3000, 840), (-3800, 1150), (-4600, 1380), (-5600, 1760), (-6800, 2200), (-8500, 2700), (-10500, 3300)]
RIVER_EAST = [(2250, 600), (2800, 640), (3316, 384), (3950, 620), (4750, 1000), (5650, 1300), (6650, 1520), (8100, 2000), (10200, 2600)]
RIVER = np.array(RIVER_WEST[::-1] + [(x, z + RIVER_CZ) for x, z in RIVER_GAME] + RIVER_EAST, float)
RIVER_LEVEL, RIVER_HALF = -24.0, 14.0
def river_distance(x, z):
    """Distance (m) to the river's centreline, as EnvironmentLayout.LineDistance."""
    best = np.full(np.shape(x), 1e18)
    for i in range(len(RIVER) - 1):
        a, d = RIVER[i], RIVER[i + 1] - RIVER[i]
        t = np.clip(((x - a[0]) * d[0] + (z - a[1]) * d[1]) / (d @ d), 0, 1)
        best = np.minimum(best, (x - a[0] - t * d[0]) ** 2 + (z - a[1] - t * d[1]) ** 2)
    return np.sqrt(best)
def gorge(dist, r):
    """The gorge's cross-section: a flat floor round the water, walls climbing ever steeper with distance."""
    level = RIVER_LEVEL + .004 * np.maximum(r - 1700.0, 0)
    floor = 44.0 + .012 * r
    return level + .9 * smooth(np.maximum(dist - RIVER_HALF, 0) / 3.0) + np.maximum(dist - floor, 0) * (.55 + .5 * smooth((r - 1500.0) / 3000.0))
def smin(a, b, k):
    h = np.clip(.5 + .5 * (b - a) / k, 0, 1)
    return b * (1 - h) + a * h - k * h * (1 - h)
# Towers framing the gorge, alternating banks from 2 km out (x, z, summit elevation deg, footprint m, crown, form).
FRAMERS = [(-2150, 960, 14.0, 220, .6, "jagged"), (-2650, 290, 15.5, 240, .8, "tilt"), (-3250, 1290, 13.0, 260, .4, "split"),
           (-3650, 720, 12.5, 250, .9, "jagged"), (-4500, 930, 11.5, 260, .5, "tilt"),
           (2150, 1000, 14.5, 220, .7, "tilt"), (2500, 210, 13.5, 230, .5, "jagged"), (4150, 1260, 12.0, 220, .8, "jagged"),
           (4500, 430, 11.5, 240, .3, "split")]
for k, (x, z, el, base, crown, form) in enumerate(FRAMERS):
    towers.append(dict(az=math.degrees(math.atan2(x, z)) % 360.0, r=math.hypot(x, z), el=el, base=base, seed=200 + k, crown=crown,
                       form=form, cx=float(x), cz=float(z)))
# The gorge mouth seen from the station (shot 1 looks south at 186 deg, terra's karst wall fills its right half): a few
# towers 0.7-1.2 km out on the left, past the game's open south zone (EnvironmentBackdrop.Weight), so the valley reads
# as walled on both sides. They stand on the game's own hills (BaseHeight), not the ranges: their walls start low.
NEAR = [(166.0, 760.0, 19.0, 68.0, .9, "split"), (152.0, 980.0, 15.0, 72.0, .9, "jagged"),
        (178.0, 1150.0, 12.5, 62.0, .9, "jagged"), (141.0, 1200.0, 12.0, 82.0, .6, "tilt"), (159.0, 1380.0, 10.0, 66.0, .8, "split")]
for k, (a, r, el, base, crown, form) in enumerate(NEAR):
    towers.append(dict(az=a, r=r, el=el, base=base, seed=300 + k, crown=crown, form=form,
                       cx=math.sin(math.radians(a)) * r, cz=math.cos(math.radians(a)) * r, near=True))

def grid_height(r, a):
    """Bilinear lookup of the composed grid (radius on the ring list, azimuth periodic)."""
    ri = np.interp(r, radii, np.arange(len(radii)))
    aa = np.mod(a, 360.0)
    ext_az = np.concatenate([azimuths, [azimuths[0] + 360.0]])
    cj = np.interp(aa, ext_az, np.arange(cols + 1)) % cols
    r0 = np.clip(ri.astype(int), 0, len(radii) - 2); fr = ri - r0
    c0 = cj.astype(int) % cols; c1 = (c0 + 1) % cols; fc = cj - np.floor(cj)
    return (H[r0, c0] * (1 - fr) * (1 - fc) + H[r0 + 1, c0] * fr * (1 - fc) + H[r0, c1] * (1 - fr) * fc + H[r0 + 1, c1] * fr * fc)

# Each tower stands on a forested mound in the grid (so its foot meets the ranges in forest, not on a seam), its top
# set so the summit makes the wanted elevation angle from the station.
for t in towers:
    dx, dz = X - t["cx"], Z - t["cz"]
    d = np.sqrt(dx * dx + dz * dz) / t["base"]
    ring = np.linspace(0, 2 * np.pi, 24, endpoint=False)
    around = grid_height(np.hypot(t["cx"] + np.sin(ring) * t["base"], t["cz"] + np.cos(ring) * t["base"]),
                         np.degrees(np.arctan2(t["cx"] + np.sin(ring) * t["base"], t["cz"] + np.cos(ring) * t["base"])))
    ground = float(np.median(around))
    t["top"] = max(math.tan(math.radians(t["el"])) * t["r"], ground + t["base"] * 1.4)
    mound = ground + (t["top"] - ground) * .16 * (1 - smooth((d - .6) / 1.4))
    H = np.maximum(H, H + (mound - H) * (1 - smooth((d - 1.2) / .8)))
    t["foot"] = min(ground, float(around.min())) - 20.0   # the wall starts below the lowest ground round its foot
    t["ground"] = ground
# Saddles: a forested ridge between paired towers, dipping to about 40% of the lower one's height mid-way.
for i, j in SADDLES:
    a, b = towers[i], towers[j]
    ax, az_, bx, bz = a["cx"], a["cz"], b["cx"], b["cz"]
    L = math.hypot(bx - ax, bz - az_)
    s_ = np.clip(((X - ax) * (bx - ax) + (Z - az_) * (bz - az_)) / (L * L), 0, 1)
    perp = np.hypot(X - (ax + s_ * (bx - ax)), Z - (az_ + s_ * (bz - az_)))
    ground = min(a["ground"], b["ground"])
    low = ground + (min(a["top"], b["top"]) - ground) * .4
    ridge = low + (min(a["top"], b["top"]) - low) * .6 * (2 * np.abs(s_ - .5)) ** 2
    width = .5 * min(a["base"], b["base"])
    H = np.maximum(H, H + (ridge - H) * (1 - smooth((perp / width - .5) / .9)))

H0 = H.copy()   # before the gorge: the corridor carves its own, finer
RIVER_GRID = river_distance(X, Z)
H = smin(H, gorge(RIVER_GRID, R), 25.0)
for t in towers:
    ring = np.linspace(0, 2 * np.pi, 24, endpoint=False)
    rx, rz = t["cx"] + np.sin(ring) * t["base"] * 1.6, t["cz"] + np.cos(ring) * t["base"] * 1.6
    low = smin(grid_height(np.hypot(rx, rz), np.degrees(np.arctan2(rx, rz))), gorge(river_distance(rx, rz), np.hypot(rx, rz)), 25.0)
    t["foot"] = min(t["foot"], float(low.min()) - 20.0)
    if t.get("near"): t["foot"] = min(t["foot"], -45.0)   # below any of BaseHeight's ground round them

def near_tower_shape(t, TH, V, u, lobes, flutes, bulge, phi):
    """The gorge mouth's towers, a kilometre from the game, as on Trang An and Ha Long: the wall swells and draws in
    up its height (never a straight shaft), undercut by a notch just above the ground and overhung by a shoulder under
    the crown; the crown is a rounded, lumpy forest cap, broken into knobs (a cleft between two for "split"), not a
    lid of spikes."""
    seed, base, top, foot = t["seed"], t["base"], t["top"], t["foot"]
    height = top - foot
    vg = float(np.clip(-foot / height, 0, .3))   # where the game's ground (about 0 m) meets the wall
    vc = .8 - .04 * t["crown"]
    taper = .35 + .2 * ((seed * .618) % 1.0)
    swell = fbm(u * 2 + V * .4, V * 2.6 + seed * .37, 3, seed + 50, period=2) - .5
    wall = (1 + .35 * (1 - smooth((V - vg + .05) / .12)) ** 2) * (1 - taper * np.clip(V, 0, 1) ** 1.1) * (1 + .32 * swell)
    notch = 1 - .2 * np.exp(-((V - vg - .04) / .05) ** 2) * (.6 + .8 * noise(u * 6, seed * .1, seed + 60, period=6))
    shoulder = 1 + .09 * np.exp(-((V - vc + .08) / .1) ** 2)
    lid = np.sqrt(np.clip(1 - ((V - vc) / (1 - vc)) ** 2, 0, 1)) ** .7
    knobs = fbm(u * 5, V * 3 + seed * .9, 3, seed + 70, period=5) - .5
    fins = fbm(u * 7 + .3 * V, V * 1.6 + seed * .3, 2, seed + 90, ridged=True, period=7) - .5   # buttresses, a few per side
    trees = fbm(u * 29, V * 14 + seed, 2, seed + 80, period=29) - .5   # the canopy's own lumps on the crown
    crown_w = smooth((V - vc + .06) / .12)
    lid = lid ** (.75 + .5 * (knobs + .5))           # some knobs round, some sheer to the top
    profile = wall * notch * shoulder * np.where(V > vc, lid, 1.0) * (1 + crown_w * (.18 * knobs + .1 * trees)) * (1 + .2 * fins * (1 - .6 * crown_w))
    if t["form"] == "split":
        profile = profile * (1 - .3 * crown_w * np.sin(TH - phi) ** 2)
    radius = base * lobes * profile * (1 + .08 * flutes * (1 - crown_w) + .03 * (bulge - .5) * (V > vg) * (V < vc))   # the shader draws the beds: a stepped mesh catches the sun in hard lines
    y = foot + height * (V + crown_w * (.15 * knobs + .025 * trees))
    if t["form"] == "split":
        y = y - height * .14 * crown_w * (1 - np.abs(np.cos(TH - phi))) ** .6
    return radius, y

def tower_mesh(t, n_around, n_up):
    """Cylindrical grid around the tower axis: rows from below the ground to the crown, columns around."""
    seed, base, top, foot = t["seed"], t["base"], t["top"], t["foot"]
    height = top - foot
    theta = np.linspace(0, 2 * np.pi, n_around + 1)
    v = np.linspace(-.1, 1.0, n_up)
    TH, V = np.meshgrid(theta, v)
    u = TH / (2 * np.pi)
    # Footprint: a few broad lobes, turning slowly with height so the tower is not an extrusion.
    lobes = 1 + .8 * (fbm(u * 3 + V * .35, V * 1.2 + seed * 3.1, 3, seed, period=3) - .5)
    form = t["form"]
    # Profile: flared foot, sheer wall with a slight lean in, and a flat lid with a sharp rim (pinnacles below).
    vc = .9 - .05 * t["crown"]
    taper = .15 + .4 * ((seed * .618) % 1.0)
    wall = (1 + .55 * (1 - smooth(V / .16)) ** 2) * (1 - taper * np.clip(V, 0, 1))
    lid = np.sqrt(np.clip(1 - ((V - vc) / (1 - vc)) ** 2, 0, 1)) ** .15
    profile = wall * np.where(V > vc, lid, 1.0)
    # Vertical flutes (water-cut grooves) and buttresses.
    flutes = fbm(u * 18 + .6 * fbm(u * 4, V * 2, 2, seed + 7, period=4), V * 3.5, 3, seed + 20, ridged=True, period=18) - .55
    # Bedding: each bed bulges out as it rises (an overhang, dark beneath) and the next starts set back from it (a
    # bench that holds the ledge forest). Warped and broken round the tower, never an even stripe.
    beds = int(np.clip(n_up / 12, 3, 9))
    g = V * beds + .9 * (fbm(u * 5, V * 2, 2, seed + 30, period=5) - .5)
    g = g - np.floor(g)
    bulge = smooth(g / .82) * (1 - smooth((g - .82) / .18))
    phi = seed * 1.93                          # the axis of a cleft or of a cap's slope
    crown_w = smooth((V - vc + .12) / .22)
    if form == "split":                        # waisted across the cleft, so the two summits read as two
        profile = profile * (1 - .3 * crown_w * np.sin(TH - phi) ** 2)
    radius = base * lobes * profile * (1 + .1 * flutes + .09 * (bulge - .5) * (V > .05) * (V < vc))
    # Crown: pinnacles and notches; a jagged top is pinnacles all through.
    jag = {"jagged": .3 + .1 * t["crown"]}.get(form, .16 + .1 * t["crown"])
    crest = (fbm(u * (11 if form == "jagged" else 7), seed * 1.7, 3, seed + 40, ridged=True, period=11 if form == "jagged" else 7) - .5) * jag
    y = foot + height * (V + crest * smooth((V - vc + .1) / .15))
    if form == "split":                        # twin summits on the cleft's axis, the saddle between them
        y = y - height * .22 * crown_w * (1 - np.abs(np.cos(TH - phi))) ** .6
    elif form == "tilt":                       # the cap slopes off one side
        y = y + height * .14 * crown_w * np.cos(TH - phi)
    if t.get("near"):
        radius, y = near_tower_shape(t, TH, V, u, lobes, flutes, bulge, phi)
    # A lean: the upper wall drifts off the axis, so the towers do not stand like silos; a tilted tower leans hard
    # towards its low side.
    lean_a = phi + math.pi if form == "tilt" else seed * 2.4
    lean = base * ((.6 if form == "tilt" else .2) + .35 * ((seed * .377) % 1.0)) * np.clip(V, 0, 1) ** 1.5
    pos = np.stack([t["cx"] + np.sin(TH) * radius + np.sin(lean_a) * lean, y, t["cz"] + np.cos(TH) * radius + np.cos(lean_a) * lean], -1)
    pos[:, -1] = pos[:, 0]                       # close the seam exactly
    pos[-1, :, 0] = pos[-1, :, 0].mean(); pos[-1, :, 2] = pos[-1, :, 2].mean()
    pos[-1, :, 1] = pos[-2, :, 1].min() if form == "split" else pos[-2, :, 1].mean()
    # Normals across the seam and up the rows.
    ext = np.concatenate([pos[:, -2:-1], pos, pos[:, 1:2]], 1)
    along = ext[:, 2:] - ext[:, :-2]
    up = np.concatenate([pos[1:2] - pos[0:1], pos[2:] - pos[:-2], pos[-1:] - pos[-2:-1]], 0)
    nrm = np.cross(up, along); nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True) + 1e-9
    outward = np.stack([np.sin(TH), np.zeros_like(TH), np.cos(TH)], -1)
    flip = (nrm * outward).sum(-1) < -.2
    nrm[flip] *= -1
    nrm[-1] = [0, 1, 0]
    cavity = np.clip(flutes * 4.0 - .7 * (1 - smooth(g / .3)) * (V > .05) * (V < vc), -1, 1)   # dark under each overhang
    return pos, nrm, cavity, np.clip(V, 0, 1)

def tower_resolution(t):
    """Columns and rows for about a vertex every 4 pixels of a 1080p view at the tower's distance (the shader's
    flutes and ledges carry the finer relief)."""
    px = t["r"] * .0009 * 4.0
    around = int(np.clip(2 * np.pi * t["base"] * 1.2 / px, 32, 160)) // 8 * 8
    up = int(np.clip((t["top"] - t["foot"]) / px * .8, 16, 110))
    return around, up

# ---------------- bake ----------------
# Shadow and sky occlusion are marched through one Cartesian height map of the ranges and towers.
def bake_height(r, a):   # approximate foreground: the runtime blends in EnvironmentLayout.BaseHeight
    w = smooth((r - 250.0) / 2750.0)
    return grid_height(r, a) * w + 30.0 * (1 - w)
CELL, EXT = 25.0, 24000.0
gx = np.arange(-EXT, EXT + CELL, CELL)
GX, GZ = np.meshgrid(gx, gx)
HMAP = bake_height(np.maximum(np.sqrt(GX * GX + GZ * GZ), 96.0), np.degrees(np.arctan2(GX, GZ)))
meshes = []
# The gorge mouth's towers take their sun from a map of the ground and of each other only: the far hero towers would
# put them in shadow all day, and they are the towers the game sees closest.
HMAP_NEAR = HMAP.copy()
for t in towers:
    pos, nrm, cav, v = tower_mesh(t, *tower_resolution(t))
    t["mesh"] = (pos, nrm, cav, v)
    sel = (np.abs(GX - t["cx"]) < t["base"] * 2) & (np.abs(GZ - t["cz"]) < t["base"] * 2)
    d = np.sqrt((GX[sel] - t["cx"]) ** 2 + (GZ[sel] - t["cz"]) ** 2) / t["base"]
    cast = np.where(d < 1.0, t["top"] - (t["top"] - t["foot"]) * .25 * smooth((d - .5) / .5), -1e4)
    HMAP[sel] = np.maximum(HMAP[sel], cast)
    if t.get("near"): HMAP_NEAR[sel] = np.maximum(HMAP_NEAR[sel], cast)
def at(x, z):
    fx, fz = (x + EXT) / CELL, (z + EXT) / CELL
    fx = np.clip(fx, 0, len(gx) - 2); fz = np.clip(fz, 0, len(gx) - 2)
    i, j = fz.astype(int), fx.astype(int); tz, tx = fz - i, fx - j
    return (HMAP[i, j] * (1 - tx) * (1 - tz) + HMAP[i, j + 1] * tx * (1 - tz) + HMAP[i + 1, j] * (1 - tx) * tz + HMAP[i + 1, j + 1] * tx * tz)
to_sun = -SUN_FORWARD                          # Unity axes: x east, y up, z north
sun_h = np.array([to_sun[0], to_sun[2]]); sun_h /= np.linalg.norm(sun_h)
sun_tan = to_sun[1] / math.hypot(to_sun[0], to_sun[2])
steps = 30.0 * 1.09 ** np.arange(60)
dist = np.cumsum(steps)
dirs = [(math.sin(q), math.cos(q)) for q in np.radians(np.arange(0, 360, 45))]
def visibility(xs, zs, hs):
    """Soft sun visibility and sky visibility (mean of 8 horizons) for points just above the height map."""
    sun = np.ones(len(xs)); sky = np.zeros(len(xs))
    for start in range(0, len(xs), 20000):
        sl = slice(start, start + 20000)
        x, z, h = xs[sl], zs[sl], hs[sl]
        vis = np.ones(len(x))
        for d in dist:
            g = at(x + sun_h[0] * d, z + sun_h[1] * d)
            vis = np.minimum(vis, np.clip(1 - (g - (h + d * sun_tan)) / (d * .06 + 10.0), 0, 1))
        sun[sl] = vis
        occ = np.zeros(len(x))
        for dx, dz in dirs:
            horizon = np.zeros(len(x))
            for d in dist[:40]:
                horizon = np.maximum(horizon, (at(x + dx * d, z + dz * d) - h) / d)
            occ += np.sin(np.arctan(np.maximum(horizon, 0)))
        sky[sl] = 1 - occ / len(dirs)
    return sun, sky
HB = bake_height(R, A)
sun, sky = visibility(X.ravel(), Z.ravel(), HB.ravel() + 2.0)
sun = sun.reshape(H.shape); sky = sky.reshape(H.shape)
# Cavity: height against the mean of its neighbourhood (ravines < 128 < ridges), scaled by distance.
pad = np.pad(HB, ((2, 2), (0, 0)), mode="edge")
mean = np.zeros_like(HB)
for di in range(-2, 3):
    for dj in range(-3, 4):
        mean += np.roll(pad, dj, axis=1)[2 + di:2 + di + HB.shape[0]]
mean /= 35.0
cav = np.clip((HB - mean) / (8.0 + R * .004), -1, 1)

def grid_indices(rows_, cols_):
    i, j = np.meshgrid(np.arange(rows_ - 1), np.arange(cols_ - 1), indexing="ij")
    v0 = i * cols_ + j
    return np.stack([v0, v0 + cols_, v0 + 1, v0 + 1, v0 + cols_, v0 + cols_ + 1], -1).ravel()

for t in towers:
    pos, nrm, cv, v = t.pop("mesh")
    rows_, cols_ = v.shape
    flat = pos.reshape(-1, 3); n = nrm.reshape(-1, 3)
    # March from a little outside the wall, so a point does not shadow itself on its own tower's cells.
    out = np.stack([n[:, 0], n[:, 2]], -1); out /= np.linalg.norm(out, axis=-1, keepdims=True) + 1e-6
    if t.get("near"): HMAP, HMAP_NEAR = HMAP_NEAR, HMAP
    s, k = visibility(flat[:, 0] + out[:, 0] * CELL * 3, flat[:, 2] + out[:, 1] * CELL * 3, flat[:, 1] + 4.0)
    if t.get("near"): HMAP, HMAP_NEAR = HMAP_NEAR, HMAP
    # Deep in a flute the sky is narrower; the foot sits in the forest.
    k = k * (1 - .35 * np.clip(-cv.ravel(), 0, 1)) * (.75 + .25 * smooth(v.ravel() / .25))
    col = np.stack([s * 255, np.clip(k, 0, 1) * 255, 128 + cv.ravel() * 127, v.ravel() * 255], -1).astype(np.uint8)
    idx = grid_indices(rows_, cols_).reshape(-1, 3)
    mid = rows_ // 2 * (cols_ - 1)                     # a triangle half way up the wall
    a, b, c = flat[idx[mid]]
    if np.dot(np.cross(b - a, c - a), n[idx[mid][0]]) < 0: idx = idx[:, ::-1]
    meshes.append((0, flat, n, col, idx.ravel()))

# ---------------- rise ----------------
# Valley cloud lies along the visible base of every range and the towers stand in it (EnvKarstCloud.hlsl). Each
# vertex keeps how far it stands, in degrees of elevation from the station, above the level the ranges in front of
# it reach on its azimuth (towers left out, so the band runs on behind and round them).
EYE, AZ_BINS, MARGIN = 10.0, 1800, 400.0
def elevation(x, y, z): return np.degrees(np.arctan2(y - EYE, np.hypot(x, z)))
def az_bin(x, z): return (np.degrees(np.arctan2(x, z)) % 360.0 / 360.0 * AZ_BINS).astype(int) % AZ_BINS
sil = np.full((len(radii), AZ_BINS), -90.0)
centres = (np.arange(AZ_BINS) + .5) * 360.0 / AZ_BINS
ext_az = np.concatenate([azimuths - 360.0, azimuths, azimuths + 360.0])
grid_el = elevation(X, HB, Z)
for i in np.nonzero(radii > 1500.0)[0]:   # the foreground is the game's own ground, never under cloud
    sil[i] = np.interp(centres, ext_az, np.tile(grid_el[i], 3))
nearer = np.maximum.accumulate(sil, axis=0)          # nearer[i]: the ranges' silhouette out to ring i
# Cloud lies near level, half filling the dips of the ridge in front: the baseline is that silhouette averaged over
# a few degrees of azimuth.
ease = int(AZ_BINS * 3.0 / 360.0)
nearer = np.mean([np.roll(nearer, k, axis=1) for k in range(-ease, ease + 1)], 0)
# Rise in a byte: x16 from 3 degrees below the ridge in front, so a wall's long triangles that reach down behind the
# ridge interpolate the true rise, not one clamped at the ridge line (it streaked the band's top down the walls).
def encode_rise(deg): return (np.clip(deg + 3.0, 0, 255 / 16.0) * 16.0).astype(np.uint8)
def rise(x, y, z):
    ri = np.searchsorted(radii, np.hypot(x, z) - MARGIN) - 1
    front = np.where(ri >= 0, nearer[np.clip(ri, 0, None), az_bin(x, z)], -90.0)
    return encode_rise(elevation(x, y, z) - front)
grid_rise = rise(X, HB, Z)
meshes = [(kind, pos, nrm, col, idx, rise(pos[:, 0], pos[:, 1], pos[:, 2])) for kind, pos, nrm, col, idx in meshes]
# The river corridor (kind 2): from just inside the game's terrain edge (the runtime pulls it to the edge) along both
# reaches to past the map's ends, a vertex every 6-25 m along and a few metres across the water. The baked height is
# the ranges' with the gorge carved in; the runtime blends it with the game's hills as it does the rings, but towards
# the far course (weight in normal.x) it keeps the carved height, so the gorge runs on through the game's foothills.
EDGE, CORRIDOR_END = 340.0, 2150.0
fine = [RIVER[0]]
for i in range(len(RIVER) - 1):
    a, b = RIVER[i], RIVER[i + 1]
    n_ = max(1, int(np.linalg.norm(b - a) / 2.0))
    fine += [a + (b - a) * k / n_ for k in range(1, n_ + 1)]
fine = np.array(fine)
kern = np.ones(31) / 31.0                      # rounds the bends (60 m), so the lateral offsets never fold
smooth_line = np.stack([np.convolve(np.pad(fine[:, k], 15, mode="edge"), kern, "valid") for k in (0, 1)], -1)
LATERAL = np.array([0, 4, 8, 11, 13, 14.5, 16, 19, 24, 32, 44, 60, 80, 105, 135], float)
LATERAL = np.concatenate([-LATERAL[:0:-1], LATERAL])
radius = np.hypot(smooth_line[:, 0], smooth_line[:, 1])
square = np.maximum(np.abs(smooth_line[:, 0]), np.abs(smooth_line[:, 1]))
arc = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(smooth_line, axis=0), axis=1))])
for side in (np.nonzero(smooth_line[:, 0] < 0)[0], np.nonzero(smooth_line[:, 0] > 0)[0]):
    keep = side[(square[side] > EDGE - 60.0) & (radius[side] < CORRIDOR_END)]
    stops, last = [], -1e9
    for i in keep:
        if arc[i] - last >= np.clip(.012 * radius[i], 6.0, 25.0) or i == keep[-1]: stops.append(i); last = arc[i]
    stops = np.array(stops)
    tangent = np.gradient(smooth_line[:, 0])[stops], np.gradient(smooth_line[:, 1])[stops]
    tl = np.hypot(*tangent)
    nx, nz = -tangent[1] / tl, tangent[0] / tl
    cx = smooth_line[stops, 0][:, None] + nx[:, None] * LATERAL[None, :]
    cz = smooth_line[stops, 1][:, None] + nz[:, None] * LATERAL[None, :]
    cr, ca = np.hypot(cx, cz), np.degrees(np.arctan2(cx, cz)) % 360.0
    rdist = river_distance(cx, cz)
    ri = np.interp(cr, radii, np.arange(len(radii))); ci = np.interp(ca, np.concatenate([azimuths, [azimuths[0] + 360]]), np.arange(cols + 1)) % cols
    r0 = np.clip(ri.astype(int), 0, len(radii) - 2); fr = ri - r0; c0 = ci.astype(int) % cols; c1 = (c0 + 1) % cols; fc = ci - np.floor(ci)
    raw0 = (H0[r0, c0] * (1 - fr) * (1 - fc) + H0[r0 + 1, c0] * fr * (1 - fc) + H0[r0, c1] * (1 - fr) * fc + H0[r0 + 1, c1] * fr * fc)
    yfar = smin(raw0, gorge(rdist, cr), 25.0)
    keepfar = smooth((cr - 1400.0) / 300.0) * (1 - smooth((rdist - 250.0) / 150.0))
    w_ = smooth((cr - 250.0) / 2750.0)
    yvis = yfar * w_ + np.minimum(30.0, gorge(rdist, cr)) * (1 - w_)
    sv, kv = visibility(cx.ravel(), cz.ravel(), yvis.ravel() + 2.0)
    pos = np.stack([cx, yfar, cz], -1).reshape(-1, 3)
    col = np.stack([sv * 255, kv * 255, np.full(sv.shape, 128.0), np.clip(rdist.ravel() * 4.0, 0, 255)], -1).astype(np.uint8)
    nrm = np.stack([keepfar.ravel(), rdist.ravel(), np.zeros(pos.shape[0])], -1)
    rows_, cols_ = cx.shape
    idx = grid_indices(rows_, cols_).reshape(-1, 3)
    a_, b_, c_ = pos[idx[:, 0]], pos[idx[:, 1]], pos[idx[:, 2]]
    flip = np.cross(b_ - a_, c_ - a_)[:, 1] < 0
    idx[flip] = idx[flip][:, ::-1]
    inside = (np.maximum(np.abs(pos[:, 0]), np.abs(pos[:, 2])) < EDGE - 4.0)[idx].all(1)   # under the terrain mesh
    meshes.append((2, pos, nrm, col, idx[~inside].ravel(), rise(pos[:, 0], yvis.ravel(), pos[:, 2])))
    print("corridor", rows_, "x", cols_, "vertices", len(pos))
river_grid = (np.clip(RIVER_GRID / 4.0, 0, 255)).astype(np.uint8)

# The cloud skirt (kind 1): a ring far behind everything carrying the same band where the view between the ranges
# and the towers opens onto the sky, so the band does not end where a surface ends. Rows at fixed elevations above
# the level of all the ranges; its rise is that elevation. The rows reach well above where the band fades out
# (KarstValleyCloud's feathered top), so the skirt's own edge never shows.
SKIRT_R, SKIRT_ROWS = 30000.0, np.array([-10.0, 0.0, 1.5, 3.0, 5.0, 8.0, 12.0, 18.0])
def ease_around(v, half, passes=3):
    for _ in range(passes): v = np.mean([np.roll(v, k) for k in range(-half, half + 1)], 0)
    return v
sa = np.radians(np.append(centres, centres[0] + 360.0))
top = np.append(nearer[-1], nearer[-1][0])
SE, SA = np.meshgrid(SKIRT_ROWS, sa, indexing="ij")
el_ = np.radians(top[None, :] + SE)
spos = np.stack([np.sin(SA) * SKIRT_R, EYE + np.tan(el_) * SKIRT_R, np.cos(SA) * SKIRT_R], -1).reshape(-1, 3)
# Only where the skyline dips (a notch, a gap between massifs) does the skirt carry cloud; along a high crest it
# would trace the ridge top like a halo, so there its rise is lifted out of the band.
window = int(AZ_BINS * 12.0 / 360.0)
peak = np.max([np.roll(nearer[-1], k) for k in range(-window, window + 1)], 0)
dip = np.append(peak - nearer[-1], peak[0] - nearer[-1][0])
lift = ease_around(8.0 * (1.0 - smooth((dip[:-1] - 0.5) / 2.0)), int(AZ_BINS * 2.0 / 360.0))
lift = np.append(lift, lift[0])
sup = encode_rise(SE + lift[None, :]).reshape(-1)
meshes.append((1, spos, np.tile([0.0, 1.0, 0.0], (len(spos), 1)), np.zeros((len(spos), 4), np.uint8),
               grid_indices(len(SKIRT_ROWS), len(sa)), sup))

# ---------------- write ----------------
os.makedirs(os.path.dirname(OUT) or ".", exist_ok=True)
with open(OUT, "wb") as f:
    f.write(b"TTMB"); f.write(struct.pack("<4i", 4, len(radii), cols, len(meshes)))
    f.write(radii.astype("<f4").tobytes()); f.write(azimuths.astype("<f4").tobytes())
    f.write(H.astype("<f2").tobytes())
    f.write((np.clip(sun, 0, 1) * 255).astype(np.uint8).tobytes())
    f.write((np.clip(sky, 0, 1) * 255).astype(np.uint8).tobytes())
    f.write((128 + cav * 127).astype(np.uint8).tobytes())
    f.write(grid_rise.tobytes())
    f.write(river_grid.tobytes())
    for kind, pos, nrm, col, idx, up in meshes:
        assert len(pos) < 65536
        f.write(struct.pack("<3i", kind, len(pos), len(idx)))
        f.write(pos.astype("<f4").tobytes()); f.write(nrm.astype("<f2").tobytes())
        f.write(np.ascontiguousarray(col, np.uint8).tobytes()); f.write(np.ascontiguousarray(up).tobytes()); f.write(idx.astype("<u2").tobytes())
verts = sum(len(m[1]) for m in meshes if m[0] == 0)
print("rings", len(radii), "columns", cols, "towers", len(towers), "tower vertices", verts)
print("wrote", OUT, os.path.getsize(OUT), "bytes")
