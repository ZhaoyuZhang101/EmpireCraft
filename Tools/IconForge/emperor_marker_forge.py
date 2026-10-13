"""纯净地图上皇帝所在的标记(28×28，两层叠画)：

  · marker_field.png：圆形印面(骨白浮雕)，游戏里按帝国颜色染色(Image.color 相乘)；
  · marker.png：旧铜(金)环与正中的冕(明定陵衮冕，见 imperial_forge.obj_crown)，印面处透明，叠在染色印面之上。

两张图用同一套像素化流水线(imperial_forge.pixelize)，几何完全对齐。

用法: python Tools/IconForge/emperor_marker_forge.py [--preview]
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import imperial_forge as imf       # noqa: E402
import emblem_forge as ef          # noqa: E402

OUT = os.path.join(imf.ROOT, "GameResources", "ui", "icons", "emperor")


def field():
    img = imf.canvas()
    imf.over(img, ef.shade(ef.disc(108), imf.BONE, bevel=30, gloss=0.2))
    return imf.pixelize(img)


def gold_crown(img):
    """金冕(简化，小尺寸认得出)：前低后高的金冕板、前沿垂六旒骨白珠、金冠身、横贯玉笄。"""
    shade, over, poly, rrect, ellipse = imf.shade, imf.over, imf.poly, imf.rrect, imf.ellipse
    over(img, shade(rrect([92, 96, 164, 200], 12), imf.BRASS, bevel=16, gloss=0.9, grain=0.1))      # 冠身
    over(img, shade(rrect([90, 176, 166, 190], 4), imf.RED, bevel=4))
    over(img, shade(rrect([58, 140, 198, 154], 6), imf.BONE, bevel=5, gloss=1.0))                  # 玉笄
    over(img, shade(poly([(58, 46), (198, 46), (234, 80), (22, 80)]), imf.GOLD, bevel=12, gloss=1.0))  # 冕板
    over(img, shade(poly([(22, 80), (234, 80), (234, 94), (22, 94)]), imf.RED, bevel=4))
    for k in range(6):                                                                            # 六旒
        x = 40 + k * 35
        over(img, shade(imf.lines([[(x, 94), (x, 150)]], 4), imf.GOLD, bevel=2, flat=True))
        for b in range(3):
            y = 106 + b * 17
            over(img, shade(ellipse([x - 7, y - 7, x + 7, y + 7]), imf.BONE, bevel=4, gloss=1.0))


def overlay():
    img = imf.canvas()
    imf.over(img, ef.shade(ef.ring(124, 104), ef.GOLD, bevel=9))
    layer = imf.canvas()
    gold_crown(layer)
    return imf.pixelize(imf.over(img, imf.inset(layer, 0.7, imf.C, imf.C + 2)))


def build():
    return {"marker_field.png": field(), "marker.png": overlay()}


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    icons = build()
    for name, im in icons.items():
        im.save(os.path.join(OUT, name))
    if "--preview" in sys.argv:
        k = 6
        tints = [(200, 60, 50), (60, 110, 200), (230, 190, 60), (90, 160, 90)]
        sheet = Image.new("RGBA", (len(tints) * (28 * k + 8), 28 * k + 8), (74, 78, 68, 255))
        for i, (r, g, b) in enumerate(tints):
            f = icons["marker_field.png"].copy()
            px = f.load()
            for y in range(28):
                for x in range(28):
                    cr, cg, cb, ca = px[x, y]
                    px[x, y] = (cr * r // 255, cg * g // 255, cb * b // 255, ca)
            f.alpha_composite(icons["marker.png"])
            sheet.alpha_composite(f.resize((28 * k, 28 * k), Image.NEAREST), (i * (28 * k + 8) + 4, 4))
        sheet.save(os.path.join(HERE, "emperor_marker_preview.png"))
