# Bakes the distant mountain backdrop (Art/Resources/Environment/Mountains.bytes) from real elevation data.
#
#   python -I build_mountains.py <terrarium_tile_dir> <out.bytes> [preview_dir]
#
# Source: AWS Terrain Tiles, terrarium PNGs z12 x 3252-3260 y 1842-1851 (SRTM, public domain; see docs/CREDITS.md),
# downloaded as {x}_{y}.png from https://s3.amazonaws.com/elevation-tiles-prod/terrarium/12/{x}/{y}.png.
# The station sits at mosaic pixel (1150, 1100) in the Phong Nha - Ke Bang / Truong Son country. Terrain is
# rescaled for the game camera: distances x0.4, relief x1.9 rising with distance, the ranges seen through the
# south valley and the eastern sightline lifted to set elevation angles, a group of tower-karst peaks composed
# either side of the valley mouth (KARST) and stepped into cliff bands and ledges with a ragged crest
# (TOWER_T/TOWER_H), and the first kilometres kept low so
# the game's own hills (EnvironmentLayout.BaseHeight) own the foreground. EnvironmentBackdrop adds those hills
# at runtime; this file only carries the far terrain, baked sun visibility, sky occlusion and cavity, and the
# cloud cards.
#
# Layout (little endian): "TTMB" int32 version=1, rings, columns, clouds; float32 radii[rings];
# float32 azimuth_deg[columns] (0 = +Z north, 90 = +X east); float16 height[rings][columns] (virtual metres);
# uint8 sun[rings][columns]; uint8 sky[rings][columns]; uint8 cavity[rings][columns] (128 = flat);
# float32 cloud[clouds][5] = azimuth_deg, radius, height, width, tall.
import os, sys, struct, math
import numpy as np
from PIL import Image

TILES, OUT = sys.argv[1], sys.argv[2]
PREVIEW = sys.argv[3] if len(sys.argv) > 3 else None
X0, X1, Y0, Y1 = 3252, 3260, 1842, 1851
ROW, COL, PX = 1150, 1100, 36.44            # station pixel, metres per pixel at 17.5 N
HS, VS, FAR_LIFT = .4, 1.9, .35
SUN_FORWARD = np.array([.55, -.4, .73]); SUN_FORWARD /= np.linalg.norm(SUN_FORWARD)   # CombatEnvironment.SunForward

def smooth(x): x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)

_lattice = np.random.default_rng(1972).random(1 << 16)
def noise(x, z, seed=0):
    """Value noise in [0, 1] on a unit lattice."""
    xi, zi = np.floor(x), np.floor(z); fx, fz = x - xi, z - zi
    fx, fz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
    xi, zi = xi.astype(np.int64) + 7919 * seed, zi.astype(np.int64)
    at = lambda i, j: _lattice[(i * 73856093 ^ j * 19349663) & 0xFFFF]
    return (at(xi, zi) * (1 - fx) + at(xi + 1, zi) * fx) * (1 - fz) + (at(xi, zi + 1) * (1 - fx) + at(xi + 1, zi + 1) * fx) * fz

def fbm(x, z, octaves=4, seed=0, ridged=False):
    total, amp, norm = 0.0, .5, 0.0
    for o in range(octaves):
        n = noise(x, z, seed + o)
        total = total + amp * ((1 - np.abs(2 * n - 1)) if ridged else n); norm += amp
        x, z, amp = x * 2.03 + 17.1, z * 2.03 + 5.3, amp * .5
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
while radii[-1] < 24000.0:   # finer rings through the tower karst so its cliff bands and ledges are resolved
    radii.append(radii[-1] * (1.01 if 2200.0 <= radii[-1] <= 5000.0 else 1.028) + 2.5)
radii = np.array(radii)
def density(a):   # columns per degree: dense where the game can see sky (south arc, eastern sightline)
    a = np.mod(a, 360.0)
    vis = smooth((a - 50.0) / 15.0) * (1 - smooth((a - 275.0) / 15.0))
    return 1 / 1.5 + (4.0 - 1 / 1.5) * vis
fine = np.arange(0, 360, .05)
cum = np.concatenate([[0], np.cumsum(density(fine[:-1]) * .05)])
cols = int(round(cum[-1] + density(359.99) * .05))
azimuths = np.interp(np.arange(cols) * (cum[-1] + density(359.99) * .05) / cols, cum, fine)

# Composition: target elevation angle of the near and middle ranges (1-8 km) per azimuth.
def target_angle(a):
    a = np.mod(a, 360.0)
    wobble = .5 + .5 * np.sin(np.radians(a) * 7.0 + 1.3) * np.cos(np.radians(a) * 3.0)
    south = smooth((a - 135.0) / 15.0) * (1 - smooth((a - 255.0) / 15.0))
    notch = np.exp(-((a - 186.0) / 11.0) ** 2)                        # the valley mouth shows the far ranges
    east = smooth((a - 62.0) / 10.0) * (1 - smooth((a - 120.0) / 10.0))
    t = 11.0 + 4.0 * wobble
    t = t * (1 - south) + (17.0 + 7.0 * wobble) * south
    t = t * (1 - notch * south) + 8.0 * notch * south
    t = t * (1 - east) + (23.0 + 5.0 * wobble) * east
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

# Focal tower karst, as round Phong Nha: a few limestone towers composed into the near range either side of the
# valley mouth, so the view south has steep, jagged peaks above the rounded DEM ridges. Each tower steps down in
# sheer cliff bands with forested slopes between them (radial profile below), on a ragged footprint, with a crown
# broken into pinnacles.
KARST = [  # azimuth deg, virtual radius m, summit elevation deg, footprint radius m, seed
    (207.0, 2900.0, 27.0, 430.0, 1), (200.0, 3300.0, 24.0, 330.0, 2), (215.0, 3600.0, 22.0, 300.0, 3),
    (165.0, 3500.0, 24.0, 400.0, 4), (158.0, 4200.0, 21.0, 320.0, 5),
]
# Fraction of the footprint radius -> fraction of the summit height. Cliff bands (about 85 degrees on the largest
# tower) alternate with forest slopes of 50-60 degrees, then a forested skirt into the ridges.
TOWER_T = [0.0, .25, .32, .35, .49, .52, .64, .67, .85, 1.4]
TOWER_H = [1.0, .97, .93, .80, .75, .52, .47, .30, .22, 0.0]
for az, rk, el, base, seed in KARST:
    top = math.tan(math.radians(el)) * rk
    base = max(base, top / 3.9)   # slender towers would push their forest slopes past 60 degrees
    cx, cz = math.sin(math.radians(az)) * rk, math.cos(math.radians(az)) * rk
    dx, dz = X - cx, Z - cz
    near = dx * dx + dz * dz < (1.6 * base) ** 2
    dx, dz = dx[near], dz[near]
    theta = np.arctan2(dz, dx)
    ragged = 1 + .16 * np.sin(5 * theta + seed) + .08 * np.sin(11 * theta + 2 * seed) + .05 * np.sin(23 * theta + 3 * seed)
    t = np.sqrt(dx * dx + dz * dz) / (base * ragged)
    # Each band edge wanders on its own so cliffs split into buttresses and gullies instead of rings.
    jitter = (fbm(np.cos(theta) * 2.5 + seed, np.sin(theta) * 2.5, 3, seed) - .5) * .12
    h = np.interp(t + jitter * np.clip(t * 3, 0, 1), TOWER_T, TOWER_H)
    # Broken crest: pinnacles and notches on the crown, a little roughness everywhere.
    crown = 1 - smooth((t - .2) / .25)
    rough = fbm(dx / 55.0, dz / 55.0, 3, 20 + seed, ridged=True) - .55
    h = h + (.09 * crown + .025) * rough
    H[near] = np.maximum(H[near], top * h)

def grid_height(r, a):
    """Bilinear lookup of the composed grid (radius on the ring list, azimuth periodic)."""
    ri = np.interp(r, radii, np.arange(len(radii)))
    aa = np.mod(a, 360.0)
    ext_az = np.concatenate([azimuths, [azimuths[0] + 360.0]])
    cj = np.interp(aa, ext_az, np.arange(cols + 1)) % cols
    r0 = np.clip(ri.astype(int), 0, len(radii) - 2); fr = ri - r0
    c0 = cj.astype(int) % cols; c1 = (c0 + 1) % cols; fc = cj - np.floor(cj)
    return (H[r0, c0] * (1 - fr) * (1 - fc) + H[r0 + 1, c0] * fr * (1 - fc) + H[r0, c1] * (1 - fr) * fc + H[r0 + 1, c1] * fr * fc)
# Approximate foreground for baking only: the runtime blends in EnvironmentLayout.BaseHeight plus canopy.
def bake_height(r, a):
    w = smooth((r - 250.0) / 2750.0)
    return grid_height(r, a) * w + 30.0 * (1 - w)
HB = bake_height(R, A)
print("rings", len(radii), "columns", cols, "vertices", H.size, "E0", E0, "height", H.min(), H.max())

# ---------------- bake ----------------
def at(x, z):
    r = np.sqrt(x * x + z * z); a = np.degrees(np.arctan2(x, z))
    return bake_height(np.maximum(r, 96.0), a)
to_sun = -SUN_FORWARD                          # Unity axes: x east, y up, z north
sun_h = np.array([to_sun[0], to_sun[2]]); sun_h /= np.linalg.norm(sun_h)
sun_tan = to_sun[1] / math.hypot(to_sun[0], to_sun[2])
steps = 30.0 * 1.09 ** np.arange(60)
dist = np.cumsum(steps)
sun = np.ones(H.size); sky = np.zeros(H.size)
xs, zs, hs = X.ravel(), Z.ravel(), HB.ravel()
dirs = [(math.sin(t), math.cos(t)) for t in np.radians(np.arange(0, 360, 45))]
for start in range(0, H.size, 20000):
    sl = slice(start, start + 20000)
    x, z, h = xs[sl], zs[sl], hs[sl] + 2.0
    # Sun visibility: soft shadow from terrain rising above the sun ray.
    vis = np.ones(len(x))
    for d in dist:
        g = at(x + sun_h[0] * d, z + sun_h[1] * d)
        over = (g - (h + d * sun_tan)) / (d * .06 + 10.0)
        vis = np.minimum(vis, np.clip(1 - over, 0, 1))
    sun[sl] = vis
    # Sky visibility: mean of 8 horizon directions.
    occ = np.zeros(len(x))
    for dx, dz in dirs:
        horizon = np.zeros(len(x))
        for d in dist[:40]:
            g = at(x + dx * d, z + dz * d)
            horizon = np.maximum(horizon, (g - h) / d)
        occ += np.sin(np.arctan(np.maximum(horizon, 0)))
    sky[sl] = 1 - occ / len(dirs)
sun = sun.reshape(H.shape); sky = sky.reshape(H.shape)
# Cavity: height against the mean of its neighbourhood (ravines < 128 < ridges), scaled by distance.
pad = np.pad(HB, ((2, 2), (0, 0)), mode="edge")
mean = np.zeros_like(HB)
for di in range(-2, 3):
    for dj in range(-3, 4):
        mean += np.roll(pad, dj, axis=1)[2 + di:2 + di + HB.shape[0]]
mean /= 35.0
cav = np.clip((HB - mean) / (8.0 + R * .004), -1, 1)

# ---------------- clouds ----------------
rng = np.random.default_rng(7)
clouds = []
elev = np.degrees(np.arctan2(H, R))
peaks = []
for i in range(5, len(radii) - 5):
    if not 2500 <= radii[i] <= 20000: continue
    for j in range(0, cols, 3):
        if not 60 <= azimuths[j] <= 290: continue
        if H[i, j] > 250 and H[i, j] >= H[i - 5:i + 6, max(j - 10, 0):j + 11].max():
            peaks.append((elev[i, j], i, j))
peaks.sort(reverse=True)
chosen = []
for e, i, j in peaks:
    if all(abs(azimuths[j] - azimuths[c[2]]) > 9 for c in chosen): chosen.append((e, i, j))
    if len(chosen) >= 7: break
for e, i, j in chosen:   # low cloud caught on the highest peaks
    r = radii[i]
    for layer in range(2):
        # On the narrow near karst towers a wide card reads as a slab: keep those puffs short.
        wide = rng.uniform(.25, .4) if r > 5000 else rng.uniform(.1, .16)
        clouds.append((azimuths[j] + rng.uniform(-1.5, 1.5), r * (.95 - .04 * layer), H[i, j] * rng.uniform(.55, .8),
                       r * wide, r * rng.uniform(.05, .08)))
for Rk in (3200.0, 5200.0, 8000.0, 12000.0, 17000.0):   # mist belts threading the valleys between ranges
    i = int(np.argmin(np.abs(radii - Rk)))
    for a0 in np.arange(105.0, 300.0, 9.0):
        # A thin belt drawn across a karst tower reads as a hard white line: leave those sectors clear.
        if any(abs(a0 - az) < 22 and abs(Rk - rk) < 900 for az, rk, *_ in KARST): continue
        m = np.abs(((azimuths - a0 + 180) % 360) - 180) < 9.0
        floor = np.percentile(H[i - 3:i + 4][:, m], 30)
        clouds.append((a0 + rng.uniform(-2, 2), Rk, floor + Rk * rng.uniform(.004, .012),
                       Rk * math.radians(9.0) * rng.uniform(1.6, 2.4), Rk * rng.uniform(.03, .05)))   # thinner reads as a hard line

# ---------------- write ----------------
os.makedirs(os.path.dirname(OUT) or ".", exist_ok=True)
with open(OUT, "wb") as f:
    f.write(b"TTMB"); f.write(struct.pack("<4i", 1, len(radii), cols, len(clouds)))
    f.write(radii.astype("<f4").tobytes()); f.write(azimuths.astype("<f4").tobytes())
    f.write(H.astype("<f2").tobytes())
    f.write((np.clip(sun, 0, 1) * 255).astype(np.uint8).tobytes())
    f.write((np.clip(sky, 0, 1) * 255).astype(np.uint8).tobytes())
    f.write((128 + cav * 127).astype(np.uint8).tobytes())
    f.write(np.array(clouds, dtype="<f4").tobytes())
print("wrote", OUT, os.path.getsize(OUT), "bytes,", len(clouds), "clouds")

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    # Elevation-angle panorama from the station (eye 2 m), 0-360 degrees, for checking the composition.
    Wd, Ht = 1440, 240
    img = np.zeros((Ht, Wd, 3))
    e_hi, e_lo = 30.0, -3.0
    ang = np.degrees(np.arctan2(HB - 2.0, R))
    for x in range(Wd):
        a = x / Wd * 360.0
        j = int(np.argmin(np.abs(((azimuths - a + 180) % 360) - 180)))
        col = ang[:, j]
        for y in range(Ht):
            e = e_hi - (y + .5) / Ht * (e_hi - e_lo)
            hit = np.nonzero(col >= e)[0]
            if len(hit) == 0: img[y, x] = (.75, .74, .66); continue
            i = hit[0]; fog = 1 - np.exp(-(radii[i] / 11000.0) ** 2)
            shade = .35 + .65 * sun[i, j] * .6 + .4 * sky[i, j] * .4
            base = np.array([.16, .22, .13]) * shade * (1 + .4 * cav[i, j])
            img[y, x] = base * (1 - fog) + np.array([.68, .72, .74]) * fog
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(os.path.join(PREVIEW, "mountains_panorama.png"))
