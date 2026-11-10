"""高原两种地面格的贴图(近看时原版地图按格子画的 8×8 小图，远看用格子颜色，见 Scripts/GeneralSystems/PlateauTerrain.cs)。

照原版地面格的格式：8×8 RGBA、三种花样、中间 6×6 的圆角块不透明、外圈透明(透明处也填颜色，同原版)、
最下一排不透明的(第 7 行)压暗一档当地坎；sprites.json 里每单位 1 像素、轴心居中(同原版 soil_high 的 tile_*.png)。
配色：
  高寒高原 alpine_plateau：高寒草甸的灰橄榄(比原版草地灰、比丘陵亮)；
  黄土高原 loess_plateau：黄土的暗土黄(比原版荒漠暗、不偏橙)。
远看的像素色写在 PlateauTerrain.cs(Alpine #8F9070、Loess #B99A68)，与这里的中间色一致。

用法: python Tools/IconForge/tile_forge.py [--preview]
输出: GameResources/tiles/<id>/tile_0..2.png + sprites.json；--preview 另出 tile_preview.png(16 倍放大)
"""
import json
import os
import random
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "GameResources", "tiles")

MASK = [
    "........",
    "..####..",
    ".######.",
    ".######.",
    ".######.",
    ".######.",
    "..####..",
    "........",
]

# 每种：亮、中、暗、地坎(最下一排)
PALETTES = {
    "alpine_plateau": [(156, 157, 124), (143, 144, 112), (130, 131, 101), (112, 112, 86)],
    "loess_plateau": [(196, 166, 116), (185, 154, 104), (171, 141, 94), (150, 122, 80)],
}


def tile(palette, seed):
    rng = random.Random(seed)
    light, mid, dark, lip = palette
    img = Image.new("RGBA", (8, 8))
    # 地坎：圆角块最下一排(第 6 行)
    for y in range(8):
        for x in range(8):
            opaque = MASK[y][x] == "#"
            if y == 6 or y == 7:
                rgb = lip
            else:
                roll = rng.random()
                rgb = light if roll < 0.28 else dark if roll > 0.78 else mid
            img.putpixel((x, y), rgb + ((255,) if opaque else (0,)))
    return img


ICON_OUT = os.path.join(ROOT, "GameResources", "ui", "icons")
ICONS = {"alpine_plateau": "iconTileAlpinePlateau", "loess_plateau": "iconTileLoessPlateau"}


def icon(name, palette):
    """剧本编辑器刷地形的按钮图标(28×28，同原版地形图标大小)：平顶台地，顶面亮、崖面暗；
    高寒高原台地边上两座积雪小峰，黄土高原崖面上几道横的梯田沟坎。1 像素纯黑描边、右下投影(同本模组图标)。"""
    light, mid, dark, lip = palette
    img = Image.new("RGBA", (28, 28), (0, 0, 0, 0))
    px = img.load()

    def put(x, y, rgb):
        if 0 <= x < 28 and 0 <= y < 28:
            px[x, y] = rgb + (255,)

    # 崖面(梯形，往下放宽)
    for y in range(13, 22):
        half = 9 + (y - 13) // 2
        for x in range(14 - half, 14 + half):
            put(x, y, dark if (x + y) % 5 else lip)
    # 顶面(扁的圆角长条)
    for y in range(9, 14):
        inset = {9: 3, 10: 1, 11: 0, 12: 0, 13: 0}[y]
        for x in range(4 + inset, 24 - inset):
            put(x, y, light if y <= 10 else mid)
    if name == "alpine_plateau":
        rock, snow = (96, 98, 100), (236, 240, 246)
        for cx, h in ((7, 5), (19, 6)):
            for k in range(h):
                for x in range(cx - k, cx + k + 1):
                    put(x, 10 - h + k, snow if k < 2 else rock)
    else:
        for y in (15, 18):
            for x in range(6, 22):
                if (x + y) % 3:
                    put(x, y, lip)
    # 描边与投影
    alpha = [[px[x, y][3] > 0 for x in range(28)] for y in range(28)]
    out = Image.new("RGBA", (28, 28), (0, 0, 0, 0))
    o = out.load()
    for y in range(28):
        for x in range(28):
            if alpha[y][x]:
                continue
            near = any(0 <= x + dx < 28 and 0 <= y + dy < 28 and alpha[y + dy][x + dx]
                       for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if near:
                o[x, y] = (0, 0, 0, 255)
    shadow = Image.new("RGBA", (28, 28), (0, 0, 0, 0))
    s = shadow.load()
    for y in range(27):
        for x in range(27):
            if alpha[y][x] or o[x, y][3]:
                s[x + 1, y + 1] = (0, 0, 0, 80)
    result = Image.alpha_composite(shadow, out)
    result.alpha_composite(img)
    return result


def main():
    preview = "--preview" in sys.argv
    sheet = Image.new("RGBA", (8 * 3 * 16 + 32 + 128 + 16, 8 * len(PALETTES) * 16 + 16 * len(PALETTES)), (40, 40, 40, 255))
    for row, (name, palette) in enumerate(PALETTES.items()):
        folder = os.path.join(OUT, name)
        os.makedirs(folder, exist_ok=True)
        for i in range(3):
            img = tile(palette, hash((name, i)) & 0xFFFF if False else (row * 97 + i * 31 + 7))
            img.save(os.path.join(folder, "tile_%d.png" % i))
            if preview:
                sheet.alpha_composite(img.resize((128, 128), Image.NEAREST), (i * (128 + 16), row * (128 + 16)))
        with open(os.path.join(folder, "sprites.json"), "w", encoding="utf-8") as f:
            json.dump({"Default": {"PivotX": 0.5, "PivotY": 0.5, "PixelsPerUnit": 1}}, f, indent=2)
            f.write("\n")
        os.makedirs(ICON_OUT, exist_ok=True)
        art = icon(name, palette)
        art.save(os.path.join(ICON_OUT, ICONS[name] + ".png"))
        if preview:
            sheet.alpha_composite(art.resize((112, 112), Image.NEAREST), (3 * (128 + 16), row * (128 + 16)))
        print("写出", folder)
    if preview:
        path = os.path.join(HERE, "tile_preview.png")
        sheet.save(path)
        print("预览", path)


if __name__ == "__main__":
    main()
