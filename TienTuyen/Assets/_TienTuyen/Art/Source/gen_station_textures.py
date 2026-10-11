"""Tiling textures for the station's hammock and camouflage net (the project's own, generated here).

python -I gen_station_textures.py <out dir>
Writes:
  StationCanvas_BaseColor.png, StationCanvas_Normal.png  (512², olive parachute nylon, 0.5 m tile)
  StationNet_BaseColor.png                               (512², RGBA: knotted cord with burlap and cloth
                                                          garnish strips, alpha cut-out, 1 m tile)
"""
import os
import sys

import numpy as np
from PIL import Image

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
N = 512
rng = np.random.default_rng(1968)
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)


def tiling_noise(scale, octaves=4, persistence=.5):
    """Periodic value noise in [0, 1] built from random grids upsampled with wrap (tiles seamlessly)."""
    total, amp, norm = np.zeros((N, N), np.float32), 1., 0.
    for o in range(octaves):
        cells = max(2, int(scale * 2 ** o))
        grid = rng.random((cells, cells)).astype(np.float32)
        # Bicubic-ish smooth upsample with wrap: interpolate on a periodic grid.
        gx, gy = xx / N * cells, yy / N * cells
        x0, y0 = np.floor(gx).astype(int), np.floor(gy).astype(int)
        fx, fy = gx - x0, gy - y0
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        x1, y1 = (x0 + 1) % cells, (y0 + 1) % cells
        x0, y0 = x0 % cells, y0 % cells
        v = (grid[y0, x0] * (1 - fx) + grid[y0, x1] * fx) * (1 - fy) + (grid[y1, x0] * (1 - fx) + grid[y1, x1] * fx) * fy
        total += v * amp
        norm += amp
        amp *= persistence
    return total / norm


def save_rgb(name, arr):
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").save(os.path.join(OUT, name), optimize=True)


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * .5 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * .5 * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return (n * .5 + .5) * 255


# ---------- Canvas: olive parachute nylon, ripstop grid, creases, fading and dirt ----------
weave = (np.sin(xx * np.pi * 2 / 2.0) * np.sin(yy * np.pi * 2 / 2.0)) * .5 + .5          # 1 px threads
rip = ((xx % 32 < 2) | (yy % 32 < 2)).astype(np.float32)                                   # ripstop, 3 cm grid at 0.5 m
crease = tiling_noise(3, 3)
crease = np.abs(crease - .5) * 2                                                           # ridged creases
fade = tiling_noise(2, 4)
dirt = np.clip((tiling_noise(4, 5) - .58) * 3.5, 0, 1)
base = np.array([86, 92, 58], np.float32)                                                   # olive drab
colour = base[None, None] * (0.86 + .22 * fade[..., None]) * (0.94 + .06 * weave[..., None])
colour *= (1 - .03 * rip[..., None])
colour = colour * (1 - .35 * dirt[..., None]) + np.array([70, 58, 40], np.float32) * .35 * dirt[..., None]
colour *= (0.9 + .14 * (1 - crease[..., None]))
save_rgb("StationCanvas_BaseColor.png", colour)
height = weave * .25 + rip * .25 + (1 - crease) * 6
save_rgb("StationCanvas_Normal.png", normal_from_height(height, 1.2))

# ---------- Net: knotted cord (10 cm mesh at 1 m), garnish strips woven through ----------
cell = N / 10

# Diamond mesh: cords along both diagonals of each cell.
d1 = np.abs(((xx + yy) % cell) - cell / 2)
d2 = np.abs(((xx - yy) % cell) - cell / 2)
cord = (np.maximum(d1, d2) > cell / 2 - 1.8).astype(np.float32)
alpha = cord.copy()
rgb = np.zeros((N, N, 3), np.float32) + np.array([62, 56, 40], np.float32)                 # tarred jute cord
rgb *= (0.85 + .3 * tiling_noise(16, 2))[..., None]
# Garnish: irregular strips of burlap and cloth, about 40% cover, in olive, brown and khaki.
palette = np.array([[78, 84, 48], [96, 78, 50], [122, 110, 78], [58, 64, 40], [84, 66, 44]], np.float32)
for i in range(110):
    w, l = rng.uniform(14, 30), rng.uniform(50, 130)
    ang = rng.uniform(0, np.pi)
    ox, oy = rng.uniform(0, N), rng.uniform(0, N)
    ca, sa = np.cos(ang), np.sin(ang)
    # Wrapped local coordinates so strips tile across the edges.
    dx = (xx - ox + N / 2) % N - N / 2
    dy = (yy - oy + N / 2) % N - N / 2
    u, v = dx * ca + dy * sa, -dx * sa + dy * ca
    ragged = w * (0.75 + .5 * tiling_noise(24, 2))
    mask = (np.abs(u) < l / 2) & (np.abs(v) < ragged / 2)
    shade = palette[rng.integers(len(palette))] * rng.uniform(.85, 1.1)
    fibre = 0.88 + .24 * (np.sin(u * 1.7) * .5 + .5)
    rgb[mask] = (shade[None] * fibre[mask][:, None])
    alpha[mask] = 1
noise = tiling_noise(6, 3)
rgb *= (0.9 + .2 * noise)[..., None]
rgba = np.dstack([np.clip(rgb, 0, 255), alpha * 255]).astype(np.uint8)
Image.fromarray(rgba, "RGBA").save(os.path.join(OUT, "StationNet_BaseColor.png"), optimize=True)
print("WROTE canvas mean", colour.reshape(-1, 3).mean(0).round(), "net cover", round(float(alpha.mean()), 3))
