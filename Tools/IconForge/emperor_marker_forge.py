"""纯净地图上皇帝、教宗所在的标记(28×28，两层叠画)：

  · marker_field.png：圆形印面(骨白浮雕)，游戏里按帝国(教宗所在国)颜色染色(Image.color 相乘)，各枚冠共用；
  · marker.png：旧铜(金)环与正中的冕(明定陵衮冕，见 imperial_forge.obj_crown)，印面处透明，叠在染色印面之上——
    华夏一线，以及暂未单画冠的文化；
  · marker_roma.png：同一枚环，冠换成西方帝冠(金冠圈嵌宝石、冠圈上的尖饰、红绒冠顶、拱架、顶上宝球与十字)——
    罗马一线(西方各族)的皇帝；
  · marker_pope.png：同一枚环，冠换成教宗三重冕(骨白蜂巢形冠身、三道金冠、顶上宝球与十字、身后两条朱红垂带)——教宗。

游戏里按文化取冠(见 EmperorMarkerRenderer.Crowns)：以后加汗、哈里发等，在这里写一个冠函数、在 CROWNS 里加一行，
游戏里的表再加一行即可。所有图用同一套像素化流水线(imperial_forge.pixelize)，几何完全对齐。

用法: python Tools/IconForge/emperor_marker_forge.py [--preview] [文件名…]
      给了文件名(如 marker_roma.png)只写这几张，不给全写；预览总是画全部。
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


# 帝冠、三重冕按 28 像素图的格子画(每个部件占整格)：十字、宝球、拱架都只有一两个像素宽，不对齐格子缩下去就糊成一团。
# 换算按 overlay 的贴法(冠图层缩到 0.7、贴在 C+2)与 pixelize(内缩 1 像素、缩到 25 像素)
INNER = imf.PIXEL - 3


def gx(c):
    """第 c 列像素(像素化内框里，0~25)左缘在冠图层上的横坐标。"""
    return imf.C + (c * imf.S / INNER - imf.C) / 0.7


def gy(r):
    """第 r 行像素上缘在冠图层上的纵坐标。"""
    return imf.C + (r * imf.S / INNER - (imf.C + 2)) / 0.7


def cells(c0, r0, c1, r1, pad=1.5):
    """占第 c0~c1 列、r0~r1 行(含)的格子框。"""
    return [gx(c0) + pad, gy(r0) + pad, gx(c1 + 1) - pad, gy(r1 + 1) - pad]


def orb_cross(img, top, orb_rows=2):
    """顶上的宝球与十字(帝冠、三重冕共用)：十字三行(竖杠一格、横杠三格)，下面三格宽的宝球。"""
    shade, over, rrect, ellipse = imf.shade, imf.over, imf.rrect, imf.ellipse
    over(img, shade(rrect(cells(12, top, 12, top + 2), 2), imf.GOLD, bevel=4, gloss=1.0))
    over(img, shade(rrect(cells(11, top + 1, 13, top + 1), 2), imf.GOLD, bevel=4, gloss=1.0))
    over(img, shade(ellipse(cells(11, top + 3, 13, top + 2 + orb_rows, pad=0.5)), imf.BRASS, bevel=8, gloss=1.0))
    return top + 3 + orb_rows


def western_crown(img):
    """西方帝冠(闭合皇冠；43 像素下比月桂冠好认——月桂冠缩到十来个像素只剩一圈，和外面的环分不开)：
    嵌宝石的金冠圈、红绒冠顶、从两侧拱到正中的金拱架与正中一道竖拱、顶上宝球与十字。"""
    shade, over, rrect, ellipse, sub = imf.shade, imf.over, imf.rrect, imf.ellipse, imf.sub
    np = imf.np
    arch_top = orb_cross(img, 5)                                                                       # 十字、宝球占第 5~9 行
    band = 16
    for c0, c1 in ((7, 11), (13, 17)):                                                                 # 红绒冠顶
        over(img, shade(rrect(cells(c0, arch_top + 2, c1, band - 1, pad=0), 2), imf.RED, bevel=6, gloss=0.6, grain=0.15))
    outer = ellipse([gx(5), gy(arch_top), gx(20), gy(2 * band - arch_top)])                            # 两侧拱架(一格宽)
    inner = ellipse([gx(6), gy(arch_top + 1), gx(19), gy(2 * band - arch_top - 1)])
    keep = (np.mgrid[0:imf.S, 0:imf.S][0] < gy(band)).astype(np.float32)
    arch = Image.fromarray((np.asarray(sub(outer, inner), np.float32) * keep).astype(np.uint8))
    over(img, shade(arch, imf.GOLD, bevel=8, gloss=1.0, grain=0.1))
    over(img, shade(rrect(cells(12, arch_top, 12, band - 1), 2), imf.GOLD, bevel=5, gloss=1.0))       # 正中竖拱
    over(img, shade(rrect(cells(5, band, 19, band + 2, pad=1), 6), imf.BRASS, bevel=12, gloss=0.9, grain=0.1))  # 冠圈
    over(img, shade(ellipse(cells(12, band + 1, 12, band + 1, pad=0)), imf.RED, bevel=6, gloss=1.0))  # 宝石
    for c in (8, 16):
        over(img, shade(ellipse(cells(c, band + 1, c, band + 1, pad=1)), imf.BONE, bevel=4, gloss=1.0))


def papal_tiara(img):
    """教宗三重冕：骨白蜂巢形冠身、上中下三道带尖饰的金冠(最下一道嵌红宝石)、顶上宝球与十字、身后垂下两条朱红垂带。"""
    shade, over, poly, rrect, ellipse = imf.shade, imf.over, imf.poly, imf.rrect, imf.ellipse
    for c in (8, 16):                                                                                  # 垂带
        over(img, shade(rrect(cells(c, 18, c, 20, pad=1), 3), imf.RED, bevel=5, grain=0.2))
    top = orb_cross(img, 4, orb_rows=1)                                                                # 第 4~7 行
    body = imf.union(ellipse(cells(10, top, 14, top + 3, pad=0)), rrect(cells(9, top + 2, 15, top + 4, pad=0), 6),
                     poly([(gx(9), gy(top + 4)), (gx(16), gy(top + 4)), (gx(17), gy(top + 8)), (gx(18), gy(top + 11)),
                           (gx(7), gy(top + 11)), (gx(8), gy(top + 8))]))
    over(img, shade(body, imf.BONE, bevel=26, gloss=0.8, grain=0.08))                                  # 冠身
    for row, c0, c1, points in ((top + 3, 9, 15, (10, 12, 14)), (top + 6, 8, 16, (9, 12, 15)),     # 三道金冠
                                (top + 9, 7, 17, (8, 10, 12, 14, 16))):
        over(img, shade(rrect(cells(c0, row, c1, row, pad=0.5), 3), imf.GOLD, bevel=5, gloss=1.0, grain=0.1))
        for c in points:
            over(img, shade(rrect(cells(c, row - 1, c, row - 1, pad=2.5), 2), imf.GOLD, bevel=3, gloss=1.0))
    over(img, shade(rrect(cells(7, top + 10, 17, top + 10, pad=0.5), 3), imf.GOLD, bevel=5, gloss=1.0))
    over(img, shade(ellipse(cells(12, top + 9, 12, top + 10, pad=1)), imf.RED, bevel=5, gloss=1.0))


def overlay(crown):
    img = imf.canvas()
    imf.over(img, ef.shade(ef.ring(124, 104), ef.GOLD, bevel=9))
    layer = imf.canvas()
    crown(layer)
    return imf.pixelize(imf.over(img, imf.inset(layer, 0.7, imf.C, imf.C + 2)))


# 冠：文件名 → 冠函数。marker.png 是最初那枚(华夏冕)，画法不动
CROWNS = {
    "marker.png": gold_crown,
    "marker_roma.png": western_crown,
    "marker_pope.png": papal_tiara,
}


def build():
    icons = {"marker_field.png": field()}
    for name, crown in CROWNS.items():
        icons[name] = overlay(crown)
    return icons


def tint(field_img, rgb):
    f = field_img.copy()
    px = f.load()
    r, g, b = rgb
    for y in range(f.height):
        for x in range(f.width):
            cr, cg, cb, ca = px[x, y]
            px[x, y] = (cr * r // 255, cg * g // 255, cb * b // 255, ca)
    return f


def preview(icons):
    """每种冠一行：四种国色放大 6 倍，右边是游戏里的大小(43 像素，最近邻)。"""
    k, real = 6, 43
    tints = [(200, 60, 50), (60, 110, 200), (230, 190, 60), (90, 160, 90), (235, 235, 235)]
    rows = list(CROWNS)
    cell = 28 * k + 8
    width = len(tints) * cell + len(tints) * (real + 6) + 12
    sheet = Image.new("RGBA", (width, len(rows) * cell), (74, 78, 68, 255))
    for j, name in enumerate(rows):
        for i, rgb in enumerate(tints):
            f = tint(icons["marker_field.png"], rgb)
            f.alpha_composite(icons[name])
            sheet.alpha_composite(f.resize((28 * k, 28 * k), Image.NEAREST), (i * cell + 4, j * cell + 4))
            sheet.alpha_composite(f.resize((real, real), Image.NEAREST),
                                  (len(tints) * cell + 8 + i * (real + 6), j * cell + (cell - real) // 2))
    sheet.save(os.path.join(HERE, "emperor_marker_preview.png"))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    icons = build()
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    for name, im in icons.items():
        if not only or name in only:
            im.save(os.path.join(OUT, name))
    if "--preview" in sys.argv:
        preview(icons)
