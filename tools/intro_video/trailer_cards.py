"""Renders the trailer's text as 1920x1080 PNGs (captions and labels transparent, cards opaque).

Usage: python trailer_cards.py <out dir>
Same fonts and palette as the intro cards (cards.py).
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

from cards import BODY, BRASS, DISPLAY, H, PARCHMENT, W, caption, shadowed, star, title, tracked


def backdrop():
    """Near-black with a faint warm glow in the middle, so the cards are not flat black."""
    layer = Image.new("RGB", (W, H))
    px = layer.load()
    for y in range(0, H, 2):
        for x in range(0, W, 2):
            d = math.hypot((x - W / 2) / (W * .55), (y - H / 2) / (H * .55))
            k = max(0.0, 1 - d) ** 2
            c = (int(6 + 30 * k), int(5 + 16 * k), int(4 + 6 * k))
            for dx in (0, 1):
                for dy in (0, 1):
                    px[x + dx, y + dy] = c
    return layer.convert("RGBA")


def brass_text(text, font, cy, tracking):
    """Brass-gradient, glowing text centred on cy; returns the layer."""
    mask = Image.new("L", (W, H), 0)
    tracked(ImageDraw.Draw(mask), (W / 2, cy), text, font, 255, tracking, True)
    box = mask.getbbox()
    gradient = Image.new("RGBA", (W, H))
    gd = ImageDraw.Draw(gradient)
    top, bottom = (255, 226, 150), (196, 128, 40)
    for y in range(H):
        k = min(1, max(0, (y - box[1]) / max(1, box[3] - box[1])))
        gd.line([(0, y), (W, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * k) for i in range(3)) + (255,))
    glow = Image.new("RGBA", (W, H), (255, 150, 40, 0))
    glow.putalpha(mask.filter(ImageFilter.GaussianBlur(30)).point(lambda a: a * 130 // 255))
    word = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    word.paste(gradient, (0, 0), mask)
    return Image.alpha_composite(glow, word)


def number_card(path, number, words):
    """A full-frame beat card: a large brass numeral over a tracked line."""
    layer = backdrop()
    layer = Image.alpha_composite(layer, brass_text(number, ImageFont.truetype(DISPLAY, 330), 250, 0))
    d = ImageDraw.Draw(layer)
    y = 700
    d.rectangle([W / 2 - 46, y, W / 2 + 46, y + 4], fill=BRASS + (255,))
    tracked(d, (W / 2, y + 34), words, ImageFont.truetype(DISPLAY, 76), PARCHMENT + (255,), 18, True)
    layer.convert("RGB").save(path)


def label(path, kicker, name):
    """Lower-left feature label: brass kicker, rule, name."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x, y = 150, 812
    tracked(d, (x, y), kicker, ImageFont.truetype(BODY, 28), BRASS + (255,), 7)
    d.rectangle([x, y + 44, x + 56, y + 47], fill=BRASS + (255,))
    tracked(d, (x, y + 60), name, ImageFont.truetype(DISPLAY, 62), PARCHMENT + (255,), 6)
    shadowed(layer, 12, 190).save(path)


def slate(path):
    """End slate on the backdrop: title, rule-star-rule, tagline, genre line."""
    layer = backdrop()
    layer = Image.alpha_composite(layer, brass_text("TIỀN TUYẾN", ImageFont.truetype(DISPLAY, 150), 330, 18))
    d = ImageDraw.Draw(layer)
    y = 560
    d.rectangle([W / 2 - 260, y, W / 2 - 34, y + 3], fill=BRASS + (255,))
    d.rectangle([W / 2 + 34, y, W / 2 + 260, y + 3], fill=BRASS + (255,))
    star(d, W / 2, y + 1, 14, BRASS + (255,))
    tracked(d, (W / 2, y + 32), "GIỮ VỮNG TUYẾN TIẾP TẾ", ImageFont.truetype(DISPLAY, 40), PARCHMENT + (255,), 14, True)
    tracked(d, (W / 2, y + 108), "TRÒ CHƠI BẮN SÚNG GÓC NHÌN THỨ BA", ImageFont.truetype(BODY, 28), (200, 194, 178, 255), 8, True)
    layer.convert("RGB").save(path)


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    caption(os.path.join(out, "cap_road.png"), "TRƯỜNG SƠN · 1972", "Mỗi viên đạn, mỗi hạt gạo ra mặt trận", left=False)
    caption(os.path.join(out, "cap_through.png"), "", "…đều phải đi qua con đường này.", left=False)
    caption(os.path.join(out, "cap_enemy.png"), "", "Kẻ thù quyết cắt đứt nó.", left=False)
    caption(os.path.join(out, "cap_people.png"), "", "Còn người, còn đường.", left=False)
    caption(os.path.join(out, "cap_hold.png"), "", "Tuyến đường này không được phép đứt.", left=False)
    for name, number, words in (("n6", "6", "ĐỢT TẤN CÔNG"), ("n4", "4", "LOẠI QUÂN ĐỊCH"),
                                ("n3", "3", "LOẠI VŨ KHÍ"), ("n1", "1", "NGƯỜI Ở LẠI")):
        number_card(os.path.join(out, f"card_{name}.png"), number, words)
    for name, kicker, text in (("infantry", "QUÂN ĐỊCH", "LÍNH ÁP SÁT"), ("shooter", "QUÂN ĐỊCH", "XẠ THỦ"),
                               ("charger", "QUÂN ĐỊCH", "LÍNH XUNG KÍCH"), ("elite", "QUÂN ĐỊCH", "LÍNH NẶNG TINH NHUỆ"),
                               ("rifle", "VŨ KHÍ", "SÚNG TRƯỜNG"), ("smg", "VŨ KHÍ", "TIỂU LIÊN"),
                               ("shotgun", "VŨ KHÍ", "SÚNG TẢN ĐẠN")):
        label(os.path.join(out, f"label_{name}.png"), kicker, text)
    title(os.path.join(out, "card_title.png"))
    slate(os.path.join(out, "card_slate.png"))
    print("trailer cards written to", out)
