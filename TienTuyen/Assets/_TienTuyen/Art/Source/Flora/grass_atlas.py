# Grass blade atlas for the GrassClump species: the Poly Haven grass_medium_02 blade scan (blades on black)
# gets an alpha from its brightness, and every blade's UV rectangle is written to Grass_Blades.json.
# usage: python -I grass_atlas.py <grass_medium_02/textures dir> <out dir>
import json
import os
import sys

import numpy as np
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
rgb = np.asarray(Image.open(os.path.join(src, "grass_medium_02_diff_1k.jpg")).convert("RGB"), np.float32) / 255
lum = rgb.max(axis=2)
alpha = np.clip((lum - 0.06) / 0.08, 0, 1)
h, w = alpha.shape

# Bleed blade colour into the background so mips and the cut-out edge do not darken.
colour = rgb.copy()
mask = alpha > 0.5
for _ in range(24):
    grown = mask.copy()
    acc = np.zeros_like(colour); cnt = np.zeros((h, w), np.float32)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        m = np.roll(mask, (dy, dx), (0, 1)); c = np.roll(colour, (dy, dx), (0, 1))
        acc += c * m[..., None]; cnt += m
    fill = (~mask) & (cnt > 0)
    colour[fill] = acc[fill] / cnt[fill][:, None]
    mask = mask | fill
img = np.dstack([colour, alpha]) * 255
Image.fromarray(img.astype(np.uint8), "RGBA").save(os.path.join(out, "Grass_Blades_Albedo.png"))
Image.open(os.path.join(src, "grass_medium_02_nor_gl_1k.jpg")).convert("RGB").save(os.path.join(out, "Grass_Blades_Normal.png"))

# Blade rectangles: connected components of the cut-out on a quarter-resolution grid.
small = np.asarray(Image.fromarray((alpha * 255).astype(np.uint8)).resize((w // 4, h // 4))) > 60
seen = np.zeros_like(small)
rects = []
for y0 in range(small.shape[0]):
    for x0 in range(small.shape[1]):
        if not small[y0, x0] or seen[y0, x0]:
            continue
        stack = [(y0, x0)]; seen[y0, x0] = True; ys = []; xs = []
        while stack:
            y, x = stack.pop(); ys.append(y); xs.append(x)
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < small.shape[0] and 0 <= nx < small.shape[1] and small[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; stack.append((ny, nx))
        x_lo, x_hi, y_lo, y_hi = min(xs) * 4, (max(xs) + 1) * 4, min(ys) * 4, (max(ys) + 1) * 4
        if y_hi - y_lo > 3 * (x_hi - x_lo) and y_hi - y_lo > h * 0.2:
            # UV: v = 0 at the image bottom, where the blade bases are.
            rects.append([x_lo / w, 1 - y_hi / h, x_hi / w, 1 - y_lo / h])
json.dump(rects, open(os.path.join(out, "Grass_Blades.json"), "w"), indent=1)
print(len(rects), "blades")
