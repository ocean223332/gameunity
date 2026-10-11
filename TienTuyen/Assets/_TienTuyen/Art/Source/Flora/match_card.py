# Rescales a cross-card albedo per channel so its opaque texels average the plant's own albedo times a factor
# (cards have no self-shadowing, so they sit a little darker). Alpha is kept.
# usage: python -I match_card.py <card_albedo.png> <reference_albedo.png> <factor>
import sys

import numpy as np
from PIL import Image

card_path, ref_path, factor = sys.argv[1], sys.argv[2], float(sys.argv[3])


def opaque_mean(rgba):
    mask = rgba[..., 3] > 0.5
    return rgba[..., :3][mask].mean(axis=0)


card = np.asarray(Image.open(card_path).convert("RGBA"), np.float32) / 255
ref = np.asarray(Image.open(ref_path).convert("RGBA"), np.float32) / 255
scale = opaque_mean(ref) * factor / np.maximum(opaque_mean(card), 1e-3)
card[..., :3] = np.clip(card[..., :3] * scale, 0, 1)
Image.fromarray((card * 255 + 0.5).astype(np.uint8), "RGBA").save(card_path)
print(card_path, "scale", scale.round(3), "mean now", opaque_mean(card).round(3))
