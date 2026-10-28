"""殖民地两个政体的政体按钮(总督府、特许公司)，平涂像素直出，画法同 transmigrant_forge(几何形状超采样后取整成硬边)。

  · 总督府 ui/buttons/ColonialOn/Off：天蓝底、下方海面，海岛上一座骨白柱廊总督府(三角山花、四柱、黑门)，屋顶旗杆挂红旗；
  · 特许公司 ui/buttons/CompanyOn/Off：赭黄底、下方深青海面一道白浪，三桅商船(棕色船身、黄色腰线、骨白帆、桅顶红旗)。

64×64，用共和政体按钮的边框，内框整块铺底色。

用法: python Tools/IconForge/colonial_forge.py [--preview [输出路径]]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from transmigrant_forge import (BONE, BONE_DARK, OUTLINE, RED, ROOT, HERE, crisp, edge_br, grow, paint, place,  # noqa: E402
                                thin)

SKY = (120, 178, 226)
SEA = (38, 101, 191)
SEA_LIGHT = (92, 146, 214)
SAND = (226, 200, 140)
SAND_DARK = (184, 154, 96)
GRASS = (86, 150, 70)
GRASS_DARK = (58, 112, 50)
POLE = (70, 70, 78)
DOOR = (52, 40, 34)

OCHRE = (214, 168, 84)
TEAL = (32, 96, 120)
FOAM = (236, 240, 244)
HULL = (120, 70, 34)
HULL_DARK = (84, 46, 22)
STRIPE = (232, 190, 64)
MAST = (78, 50, 30)

N = 48


def rect(x0, y0, x1, y1):
    return lambda d, s: d.rectangle([x0 * s, y0 * s, x1 * s, y1 * s], fill=255)


def poly(*pts):
    return lambda d, s: d.polygon([(x * s, y * s) for x, y in pts], fill=255)


def ellipse(cx, cy, rx, ry):
    return lambda d, s: d.ellipse([(cx - rx) * s, (cy - ry) * s, (cx + rx) * s, (cy + ry) * s], fill=255)


def outlined(a, mask):
    """形状外一圈黑描边(只描在空白或底色上，不压已画的形状)。"""
    a[grow(mask, 1) & ~mask] = OUTLINE


def governorate():
    a = np.zeros((N, N, 4), np.uint8)
    a[...] = SKY + (255,)
    a[int(N * 0.72):] = SEA + (255,)
    a[int(N * 0.72), 4:N - 4:6] = SEA_LIGHT + (255,)
    island = crisp(N, ellipse(0.5, 0.8, 0.44, 0.13))
    paint(a, island, SAND)
    paint(a, edge_br(island), SAND_DARK)
    grass = crisp(N, ellipse(0.5, 0.75, 0.36, 0.07))
    paint(a, grass, GRASS)
    paint(a, edge_br(grass), GRASS_DARK)

    body = crisp(N, rect(0.25, 0.47, 0.75, 0.74))
    roof = crisp(N, poly((0.19, 0.48), (0.5, 0.29), (0.81, 0.48)))
    steps = crisp(N, rect(0.21, 0.73, 0.79, 0.78))
    building = body | roof | steps
    outlined(a, building)
    paint(a, building, BONE)
    paint(a, edge_br(roof) | edge_br(steps), BONE_DARK)
    cols = thin(N, [((x, 0.53), (x, 0.73)) for x in (0.33, 0.43, 0.57, 0.67)]) & body
    paint(a, cols, BONE_DARK)
    paint(a, thin(N, [((0.24, 0.5), (0.76, 0.5))]) & body, BONE_DARK)
    door = crisp(N, rect(0.46, 0.6, 0.54, 0.735))
    paint(a, door, DOOR)

    pole = thin(N, [((0.5, 0.08), (0.5, 0.3))])
    paint(a, pole, POLE)
    flag = crisp(N, rect(0.52, 0.08, 0.72, 0.19))
    outlined(a, flag)
    paint(a, flag, RED)
    paint(a, pole, POLE)
    return a


def company():
    a = np.zeros((N, N, 4), np.uint8)
    a[...] = OCHRE + (255,)
    a[int(N * 0.7):] = TEAL + (255,)
    waves = thin(N, [((0.06, 0.86), (0.2, 0.84)), ((0.2, 0.84), (0.34, 0.86)),
                     ((0.62, 0.88), (0.78, 0.86)), ((0.78, 0.86), (0.94, 0.88))])
    paint(a, waves, FOAM)

    hull = crisp(N, poly((0.12, 0.6), (0.88, 0.6), (0.8, 0.78), (0.2, 0.78)))
    stern = crisp(N, rect(0.66, 0.53, 0.86, 0.61))
    sails = np.zeros((N, N), bool)
    for x, top, w in ((0.27, 0.2, 0.075), (0.5, 0.12, 0.085), (0.72, 0.24, 0.07)):
        sails |= crisp(N, poly((x - w, top + 0.04), (x + w, top + 0.04), (x + w + 0.02, top + 0.17), (x - w - 0.02, top + 0.17)))
        sails |= crisp(N, poly((x - w - 0.01, top + 0.2), (x + w + 0.01, top + 0.2), (x + w + 0.03, top + 0.33), (x - w - 0.03, top + 0.33)))
    masts = thin(N, [((0.27, 0.18), (0.27, 0.6)), ((0.5, 0.1), (0.5, 0.6)), ((0.72, 0.22), (0.72, 0.55))])
    bowsprit = thin(N, [((0.12, 0.6), (0.04, 0.52))])
    ship = hull | stern | sails
    outlined(a, ship)
    paint(a, sails, BONE)
    paint(a, edge_br(sails), BONE_DARK)
    paint(a, hull | stern, HULL)
    paint(a, edge_br(hull | stern), HULL_DARK)
    paint(a, thin(N, [((0.15, 0.665), (0.85, 0.665))]) & hull, STRIPE)
    paint(a, masts | bowsprit, MAST)
    flags = np.zeros((N, N), bool)
    for x, top in ((0.27, 0.18), (0.5, 0.1), (0.72, 0.22)):
        flags |= crisp(N, poly((x, top - 0.06), (x + 0.12, top - 0.035), (x, top - 0.01)))
    outlined(a, flags)
    paint(a, flags, RED)
    paint(a, masts, MAST)
    return a


def make_button(state, art):
    base = Image.open(os.path.join(ROOT, "GameResources", "ui", "buttons", f"Republic{state}.png")).convert("RGBA")
    out = Image.fromarray(np.asarray(base).copy(), "RGBA")
    field = np.asarray(out).copy()
    field[6:58, 6:58] = art[0, 0]
    out = Image.fromarray(field, "RGBA")
    place(out, art, 8, 8)
    return out


def build():
    gov, com = governorate(), company()
    return {
        "GameResources/ui/buttons/ColonialOn.png": make_button("On", gov),
        "GameResources/ui/buttons/ColonialOff.png": make_button("Off", gov),
        "GameResources/ui/buttons/CompanyOn.png": make_button("On", com),
        "GameResources/ui/buttons/CompanyOff.png": make_button("Off", com),
    }


if __name__ == "__main__":
    icons = build()
    for path, im in icons.items():
        full = os.path.join(ROOT, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        im.save(full)
    if "--preview" in sys.argv:
        k = 5
        tiles = [im.resize((im.width * k, im.height * k), Image.NEAREST) for im in icons.values()]
        W = sum(t.width for t in tiles) + 8 * (len(tiles) + 1)
        H = max(t.height for t in tiles) + 16
        sheet = Image.new("RGBA", (W, H), (74, 78, 68, 255))
        x = 8
        for t in tiles:
            sheet.alpha_composite(t, (x, 8))
            x += t.width + 8
        idx = sys.argv.index("--preview")
        out = sys.argv[idx + 1] if len(sys.argv) > idx + 1 else os.path.join(HERE, "colonial_preview.png")
        sheet.save(out)
        print("preview:", out)
