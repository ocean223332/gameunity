"""Renders the intro's text cards as transparent 1920x1080 PNGs.

Usage: python cards.py <out dir>
Fonts are the game's own interface fonts (Oswald Bold, Source Sans Pro Semibold, SIL OFL).
"""
import os
import sys
import unicodedata

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.abspath(__file__))
FONTS = os.path.join(ROOT, "..", "..", "TienTuyen", "Assets", "_TienTuyen", "Art", "Fonts")
DISPLAY = os.path.join(FONTS, "Oswald-Bold.ttf")
BODY = os.path.join(FONTS, "SourceSansPro-Semibold.ttf")
W, H = 1920, 1080
PARCHMENT = (241, 235, 220)
BRASS = (228, 178, 74)


def tracked(draw, xy, text, font, fill, tracking, anchor_center=False):
    """Draws text with extra letter spacing; returns its width."""
    text = unicodedata.normalize("NFC", text)
    widths = [draw.textlength(ch, font=font) for ch in text]
    total = sum(widths) + tracking * (len(text) - 1)
    x, y = xy
    if anchor_center:
        x -= total / 2
    for ch, w in zip(text, widths):
        draw.text((x, y), ch, font=font, fill=fill)
        x += w + tracking
    return total


def shadowed(layer, radius=10, opacity=170):
    """Soft dark halo under the text so it reads over bright foliage."""
    alpha = layer.split()[3].filter(ImageFilter.GaussianBlur(radius))
    shadow = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    shadow.putalpha(alpha.point(lambda a: min(255, a * opacity // 255 * 2)))
    return Image.alpha_composite(shadow, layer)


def caption(path, kicker, line, left=True):
    """Lower-third caption: small brass kicker, a rule, then the line."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    small = ImageFont.truetype(DISPLAY, 30)
    big = ImageFont.truetype(BODY, 46)
    x = 150 if left else W / 2
    y = 780
    if kicker:
        kw = tracked(d, (x, y), kicker, small, BRASS + (255,), 9, not left)
        rule_x = x if left else x - 32
        d.rectangle([rule_x, y + 50, rule_x + 64, y + 53], fill=BRASS + (255,))
    lw = d.textlength(line, font=big)
    d.text((x if left else x - lw / 2, y + 68), line, font=big, fill=PARCHMENT + (255,))
    shadowed(layer).save(path)


def title(path):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    huge = ImageFont.truetype(DISPLAY, 190)
    sub = ImageFont.truetype(DISPLAY, 40)
    small = ImageFont.truetype(BODY, 30)
    cy = 400

    # Brass gradient title with a warm glow.
    mask = Image.new("L", (W, H), 0)
    tracked(ImageDraw.Draw(mask), (W / 2, cy), "TIỀN TUYẾN", huge, 255, 22, True)
    gradient = Image.new("RGBA", (W, H))
    gd = ImageDraw.Draw(gradient)
    top, bottom = (255, 226, 150), (196, 128, 40)
    for y in range(H):
        k = min(1, max(0, (y - cy - 40) / 230))
        gd.line([(0, y), (W, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * k) for i in range(3)) + (255,))
    glow = Image.new("RGBA", (W, H), (255, 150, 40, 0))
    glow.putalpha(mask.filter(ImageFilter.GaussianBlur(28)).point(lambda a: a * 150 // 255))
    word = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    word.paste(gradient, (0, 0), mask)
    layer = Image.alpha_composite(layer, glow)
    layer = Image.alpha_composite(layer, word)

    d = ImageDraw.Draw(layer)
    y = cy + 275
    # Rule — star — rule, echoing the banner.
    d.rectangle([W / 2 - 330, y, W / 2 - 40, y + 3], fill=BRASS + (255,))
    d.rectangle([W / 2 + 40, y, W / 2 + 330, y + 3], fill=BRASS + (255,))
    star(d, W / 2, y + 1, 17, BRASS + (255,))
    tracked(d, (W / 2, y + 34), "GIỮ VỮNG TUYẾN TIẾP TẾ", sub, PARCHMENT + (255,), 14, True)
    tracked(d, (W / 2, y + 102), "TRƯỜNG SƠN · 1972", small, (200, 194, 178, 255), 8, True)
    shadowed(layer, 16, 140).save(path)


def star(draw, cx, cy, r, fill):
    import math
    points = []
    for i in range(10):
        radius = r if i % 2 == 0 else r * .4
        a = -math.pi / 2 + i * math.pi / 5
        points.append((cx + radius * math.cos(a), cy + radius * math.sin(a)))
    draw.polygon(points, fill=fill)


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    caption(os.path.join(out, "card_place.png"), "TRƯỜNG SƠN · 1972", "Tuyến đường tiếp tế xuyên đại ngàn")
    caption(os.path.join(out, "card_camp.png"), "TRẠM TIẾP TẾ", "Mỗi chuyến hàng là một mạch máu của tiền tuyến")
    caption(os.path.join(out, "card_flag.png"), "", "Con đường này không được phép đứt.")
    caption(os.path.join(out, "card_hero.png"), "", "Và có người ở lại để giữ nó.")
    title(os.path.join(out, "card_title.png"))
    print("cards written to", out)
