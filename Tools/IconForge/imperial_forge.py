"""EmpireCraft 帝国图标锻造器 —— 冷峻配色的写实图标(56×56)，一次生成全部：

  · 帝国窗口页签(5)：国徽式徽章(旧铜环、黑/血红底、金属浮雕主体、麦穗绶带)；
  · 谋划(42)：EU4 修正图标式写实器物 + 右下角标；
  · 身份特质(9)：小徽章(旧铜环 + 器物)；
  · 文化(22)：纹章盾(底色 + 浮雕纹章)。

配色：黑、骨白、暗血红、枪灰、氧化旧铜，统一降饱和、提对比、粗黑描边(见 object_forge.grade/finish)。
游戏里由 Scripts/UI/Components/HiResIconFilter.cs 改成平滑过滤。

用法: python Tools/IconForge/imperial_forge.py [--preview]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import object_forge as of          # noqa: E402
import emblem_forge as ef          # noqa: E402

ROOT = os.path.dirname(os.path.dirname(HERE))
S = of.S
C = S / 2
shade, over, poly, ellipse, rrect, sub, rotate = of.shade, of.over, of.poly, of.ellipse, of.rrect, of.sub, of.rotate
BRASS, GOLD, STEEL, IRON, PARCH, WOOD, RED, LACQ, JADE, BONE, BLOOD = (
    of.BRASS, of.GOLD, of.STEEL, of.IRON, of.PARCH, of.WOOD, of.RED, of.LACQ, of.JADE, of.GREEN, of.BLOOD)


def canvas():
    return np.zeros((S, S, 4), np.float32)


def lines(points_list, width):
    m = of.blank(); d = ImageDraw.Draw(m)
    for pts in points_list:
        d.line(pts, fill=255, width=width)
    return m


def union(*masks):
    return Image.fromarray(np.max(np.stack([np.asarray(m) for m in masks]), axis=0))


def inset(layer, scale, cx=C, cy=C):
    """把一整张图层缩放后贴到(cx, cy)为中心的位置。"""
    pil = Image.fromarray(np.clip(layer, 0, 255).astype(np.uint8), "RGBA")
    size = int(S * scale)
    small = pil.resize((size, size), Image.LANCZOS)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.alpha_composite(small, (int(cx - size / 2), int(cy - size / 2)))
    return np.asarray(out, np.float32)


# ═════════════ 像素化输出(与原版像素界面融为一体) ═════════════
PIXEL = 28
PX_OUTLINE = (0, 0, 0, 255)
PX_SHADOW = (0, 0, 0, 80)


def pixelize(img, colors=20, inset_px=1):
    """256 画布的写实图层 → 28×28 像素图：冷峻调色、缩小、透明度二值化、限色、1px 描边与右下投影。"""
    img = of.grade(img.copy(), desat=0.25, contrast=1.3, gamma=1.08)
    pil = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGBA")
    inner = PIXEL - 2 * inset_px - 1
    small = pil.resize((inner, inner), Image.LANCZOS)
    small = small.filter(of.ImageFilter.UnsharpMask(radius=1, percent=60, threshold=2))
    arr = np.asarray(small).copy()
    solid = arr[..., 3] > 110
    rgb = Image.fromarray(arr[..., :3])
    q = np.asarray(rgb.quantize(colors=colors, method=Image.Quantize.MEDIANCUT).convert("RGB"))
    out = np.zeros((PIXEL, PIXEL, 4), np.uint8)
    out[inset_px:inset_px + inner, inset_px:inset_px + inner, :3] = q
    out[inset_px:inset_px + inner, inset_px:inset_px + inner, 3] = np.where(solid, 255, 0)
    filled = out[..., 3] == 255
    pad = np.pad(filled, 1)
    near = pad[:-2, 1:-1] | pad[2:, 1:-1] | pad[1:-1, :-2] | pad[1:-1, 2:]
    outline = near & ~filled
    out[outline] = PX_OUTLINE
    body = out[..., 3] > 0
    shifted = np.zeros_like(body); shifted[1:, 1:] = body[:-1, :-1]
    shadow = shifted & ~body
    out[shadow] = PX_SHADOW
    return Image.fromarray(out, "RGBA")


# ═════════════ 器物(谋划主体，按 256 画布满幅画) ═════════════
def obj_chain(img):
    of.chain(img)


def obj_broken_chain(img):
    for k, (cx, cy) in enumerate([(64, 64), (110, 110)]):
        if k == 0:
            ring = sub(ellipse([cx - 44, cy - 30, cx + 44, cy + 30]), ellipse([cx - 24, cy - 12, cx + 24, cy + 12]))
        else:
            ring = sub(ellipse([cx - 44, cy - 18, cx + 44, cy + 18]), ellipse([cx - 30, cy - 4, cx + 30, cy + 4]))
        over(img, shade(rotate(ring, -45 if k == 0 else 45, cx, cy), IRON, bevel=10, gloss=0.9, grain=0.2))
    half = sub(ellipse([132, 126, 220, 186]), ellipse([152, 144, 200, 168]))
    half = sub(half, poly([(120, 120), (170, 120), (160, 200), (120, 200)]))
    over(img, shade(rotate(half, -45, 176, 156), IRON, bevel=10, gloss=0.9, grain=0.2))
    for x, y, r in ((150, 138, 7), (140, 156, 5), (162, 128, 4)):
        over(img, shade(ellipse([x - r, y - r, x + r, y + r]), BLOOD, bevel=3, gloss=1.0))


def obj_crown(img):
    """冕(参照明定陵衮冕)：前低后高的长方冕板(黑面朱边)、黑冠身与金红冠带、横贯玉笄、
    前沿垂十二旒(细丝串五色珠：赤白黑黄青，透出后面的冠身)、两侧朱红丝绳与流苏。"""
    bead_colors = (RED, BONE, BRASS, JADE, BONE)
    for x0, x1 in ((72, 62), (184, 194)):                               # 丝绳与流苏
        over(img, shade(lines([[(x0, 152), (x1, 210)]], 5), RED, bevel=2, grain=0.2))
        over(img, shade(poly([(x1 - 8, 206), (x1 + 8, 206), (x1 + 11, 232), (x1 - 11, 232)]), RED, bevel=5, grain=0.35))
    over(img, shade(rrect([90, 86, 166, 200], 10), LACQ, bevel=16, gloss=0.7, grain=0.2))   # 冠身
    over(img, shade(rrect([88, 176, 168, 188], 3), RED, bevel=4))
    over(img, shade(rrect([88, 170, 168, 176], 2), BRASS, bevel=2))
    over(img, shade(rrect([62, 146, 194, 155], 4), BONE, bevel=4, gloss=0.9))               # 玉笄
    over(img, shade(ellipse([54, 142, 70, 160]), JADE, bevel=4))
    over(img, shade(ellipse([186, 142, 202, 160]), JADE, bevel=4))
    over(img, shade(poly([(62, 42), (194, 42), (236, 74), (20, 74)]), LACQ, bevel=10, gloss=0.8, grain=0.15))   # 冕板
    over(img, shade(poly([(20, 74), (236, 74), (236, 86), (20, 86)]), RED, bevel=4, grain=0.2))
    for k in range(12):                                                  # 十二旒
        x = 28 + k * 18
        over(img, shade(lines([[(x, 86), (x, 158)]], 2), LACQ, bevel=1, flat=True))
        for b in range(5):
            y = 96 + b * 15
            over(img, shade(ellipse([x - 4, y - 4, x + 4, y + 4]), bead_colors[(k + b) % 5], bevel=3, gloss=0.9))


def obj_seal(img):
    over(img, shade(rrect([52, 130, 204, 214], 8), JADE, bevel=14, gloss=0.8, grain=0.15))
    over(img, shade(rrect([52, 200, 204, 222], 6), RED, bevel=6))
    beast = union(ellipse([80, 86, 176, 140]), ellipse([150, 60, 200, 108]), rrect([86, 120, 104, 140], 6),
                  rrect([150, 120, 168, 140], 6), lines([[(84, 100), (56, 74)]], 12))
    over(img, shade(beast, JADE, bevel=12, gloss=0.9, grain=0.15))
    over(img, shade(ellipse([180, 74, 190, 84]), BRASS, bevel=3, gloss=1.0))


def obj_title(img):
    of.scroll_map(img)


def obj_edict(img):
    over(img, shade(rrect([58, 44, 198, 212], 4), PARCH, bevel=12, gloss=0.4, grain=0.35))
    for k in range(7):
        x = 172 - k * 18
        over(img, shade(lines([[(x, 66), (x, 186)]], 5), LACQ, bevel=2, flat=True))
    over(img, shade(rrect([70, 150, 110, 190], 4), RED, bevel=6, grain=0.2))
    for y in (36, 220):
        over(img, shade(rrect([42, y - 12, 214, y + 12], 10), WOOD, bevel=8, grain=0.3))
        over(img, shade(ellipse([30, y - 14, 50, y + 14]), BRASS, bevel=5))
        over(img, shade(ellipse([206, y - 14, 226, y + 14]), BRASS, bevel=5))


def obj_bamboo(img):
    for k in range(7):
        x = 50 + k * 24
        over(img, shade(rrect([x, 40, x + 20, 216], 6), PARCH if k % 2 else WOOD, bevel=6, grain=0.35))
        for y in (90, 120, 150):
            over(img, shade(rrect([x + 6, y, x + 14, y + 12], 2), LACQ, bevel=2, flat=True))
    for y in (68, 188):
        over(img, shade(rrect([36, y - 6, 220, y + 6], 6), RED, bevel=5, grain=0.2))


def obj_palace(img):
    over(img, shade(rrect([40, 128, 216, 220], 4), STEEL, bevel=10, grain=0.25))        # 城台
    over(img, shade(union(rrect([108, 160, 148, 220], 4), ellipse([108, 140, 148, 180])), LACQ, bevel=6))
    over(img, shade(rrect([62, 92, 194, 130], 2), RED, bevel=8, grain=0.25))            # 楼身
    for x in range(72, 190, 22):
        over(img, shade(rrect([x, 92, x + 8, 130], 2), BLOOD, bevel=3))
    over(img, shade(poly([(30, 96), (226, 96), (196, 62), (60, 62)]), LACQ, bevel=10, grain=0.2))   # 屋顶
    over(img, shade(rrect([70, 50, 186, 62], 4), BRASS, bevel=5))
    over(img, shade(rrect([120, 30, 136, 52], 4), BRASS, bevel=5))
    for x in range(116, 146, 10):
        for y in range(176, 214, 12):
            over(img, shade(ellipse([x - 3, y - 3, x + 3, y + 3]), BRASS, bevel=2))


def obj_brush(img):
    over(img, shade(rrect([40, 156, 176, 216], 14), LACQ, bevel=12, gloss=0.8, grain=0.15))        # 砚
    over(img, shade(ellipse([62, 170, 150, 202]), STEEL, bevel=4, gloss=0.4, flat=False))
    handle = rotate(rrect([120, 20, 140, 170], 8), -40, 130, 150)
    over(img, shade(handle, WOOD, bevel=7, grain=0.35))
    tip = rotate(poly([(118, 168), (142, 168), (136, 210), (130, 222), (124, 210)]), -40, 130, 150)
    over(img, shade(tip, LACQ, bevel=6))
    band = rotate(rrect([116, 160, 144, 172], 3), -40, 130, 150)
    over(img, shade(band, BRASS, bevel=3))


def obj_minister(img):
    """官帽(参照宋代直脚幞头)：前低后高的台阶形帽身、两侧极长平直的展脚(末端金饰)、额前小饰。"""
    over(img, shade(rrect([2, 100, 254, 110], 5), LACQ, bevel=4, gloss=0.9))            # 展脚
    for x in (0, 242):
        over(img, shade(rrect([x, 96, x + 14, 114], 4), BRASS, bevel=4, gloss=1.0))
    over(img, shade(rrect([84, 60, 172, 150], 16), LACQ, bevel=14, gloss=0.6, grain=0.15))   # 后山(高)
    over(img, shade(rrect([70, 120, 186, 204], 8), LACQ, bevel=16, gloss=0.8, grain=0.15))   # 前屋(低)
    over(img, shade(lines([[(72, 122), (184, 122)]], 3), STEEL, bevel=1, flat=True))           # 前后分界的反光
    over(img, shade(rrect([70, 196, 186, 206], 4), WOOD, bevel=3))
    over(img, shade(ellipse([120, 150, 136, 166]), BRASS, bevel=5, gloss=1.0))


def obj_axe(img):
    """斧钺(九锡)：长柄、宽刃弧口的礼仪大斧。"""
    over(img, shade(rrect([70, 20, 88, 236], 8), WOOD, bevel=7, grain=0.35))
    blade = poly([(88, 54), (140, 40), (188, 34), (214, 70), (222, 120), (206, 168), (178, 192), (140, 176), (88, 150)])
    over(img, shade(blade, BRASS, bevel=16, gloss=1.0, grain=0.15))
    over(img, shade(lines([[(196, 44), (214, 90), (210, 150), (184, 186)]], 6), BONE, bevel=2, gloss=0.6))   # 刃口
    over(img, shade(ellipse([128, 90, 160, 122]), LACQ, bevel=4))
    over(img, shade(rrect([62, 10, 96, 26], 4), RED, bevel=4))
    over(img, shade(rrect([64, 140, 94, 152], 3), RED, bevel=3))


def obj_phoenix(img):
    over(img, shade(poly([(40, 196), (48, 104), (128, 64), (208, 104), (216, 196)]), BRASS, bevel=16, gloss=0.9))
    over(img, shade(rrect([36, 186, 220, 214], 6), BRASS, bevel=6))
    for x in (78, 128, 178):
        over(img, shade(ellipse([x - 13, 124, x + 13, 150]), RED, bevel=8, gloss=1.0))
    over(img, shade(poly([(100, 66), (128, 22), (156, 66), (128, 52)]), JADE, bevel=8, gloss=0.8))
    for x in range(52, 210, 16):
        over(img, shade(ellipse([x - 5, 214, x + 5, 238]), BONE, bevel=3))


def obj_tablet(img):
    over(img, shade(poly([(70, 60), (88, 34), (128, 22), (168, 34), (186, 60)]), BRASS, bevel=10))
    over(img, shade(rrect([68, 56, 188, 206], 6), LACQ, bevel=12, gloss=0.8, grain=0.15))
    over(img, shade(sub(rrect([80, 68, 176, 196], 4), rrect([92, 80, 164, 184], 3)), BRASS, bevel=4))
    over(img, shade(rrect([92, 80, 164, 184], 3), RED, bevel=8, grain=0.25))
    for y in (98, 124, 150):
        over(img, shade(rrect([118, y, 138, y + 12], 3), BRASS, bevel=3))
    over(img, shade(rrect([44, 204, 212, 232], 6), STEEL, bevel=8, grain=0.25))


def obj_swords(img):
    of.crossed_swords(img)


def obj_hu(img):
    for x, top in ((60, 70), (110, 36), (160, 70)):
        over(img, shade(union(rrect([x, top + 10, x + 34, 214], 6), ellipse([x, top, x + 34, top + 26])), BONE,
                        bevel=10, gloss=0.8, grain=0.2))
        over(img, shade(rrect([x, 186, x + 34, 196], 2), RED, bevel=3))
    over(img, shade(rrect([44, 212, 212, 228], 6), WOOD, bevel=6, grain=0.3))


def obj_temple(img):
    over(img, shade(poly([(30, 92), (128, 30), (226, 92)]), STEEL, bevel=12, grain=0.25))
    over(img, shade(rrect([40, 92, 216, 108], 2), STEEL, bevel=6, grain=0.25))
    for x in (52, 98, 144, 190):
        over(img, shade(rrect([x, 112, x + 20, 200], 4), STEEL, bevel=7, grain=0.25))
    over(img, shade(rrect([30, 200, 226, 224], 4), STEEL, bevel=8, grain=0.25))
    over(img, shade(ellipse([116, 62, 140, 86]), BRASS, bevel=6, gloss=1.0))


def obj_scales(img):
    over(img, shade(rrect([122, 34, 134, 200], 4), BRASS, bevel=5))
    over(img, shade(rrect([40, 52, 216, 62], 4), BRASS, bevel=5))
    for x in (56, 200):
        over(img, shade(lines([[(x, 62), (x - 30, 130)], [(x, 62), (x + 30, 130)]], 4), BRASS, bevel=2))
        over(img, shade(poly([(x - 40, 128), (x + 40, 128), (x + 24, 150), (x - 24, 150)]), BRASS, bevel=8, gloss=0.9))
    over(img, shade(rrect([80, 196, 176, 222], 6), WOOD, bevel=7, grain=0.3))
    over(img, shade(ellipse([116, 24, 140, 46]), BRASS, bevel=6, gloss=1.0))


def obj_calendar(img):
    over(img, shade(rrect([52, 40, 210, 220], 6), PARCH, bevel=10, grain=0.35))
    over(img, shade(rrect([40, 34, 76, 226], 4), LACQ, bevel=8, grain=0.2))         # 线装书脊
    for y in range(52, 220, 34):
        over(img, shade(rrect([30, y, 86, y + 6], 3), BONE, bevel=2))
    over(img, shade(rrect([108, 54, 196, 82], 3), RED, bevel=5))
    over(img, shade(ellipse([116, 112, 190, 186]), BRASS, bevel=12, gloss=0.9))
    over(img, shade(ellipse([134, 112, 196, 174]), PARCH, bevel=3, flat=True))       # 月相


def obj_shield(img):
    over(img, shade(poly([(48, 40), (208, 40), (208, 130), (128, 226), (48, 130)]), LACQ, bevel=16, gloss=0.8,
                    grain=0.15))
    over(img, shade(sub(poly([(48, 40), (208, 40), (208, 130), (128, 226), (48, 130)]),
                        poly([(62, 54), (194, 54), (194, 126), (128, 206), (62, 126)])), BRASS, bevel=6))
    over(img, shade(ellipse([104, 96, 152, 144]), BRASS, bevel=12, gloss=1.0))


def obj_envoy(img):
    over(img, shade(rrect([118, 20, 136, 236], 8), WOOD, bevel=7, grain=0.35))
    for y in (52, 96, 140):
        over(img, shade(poly([(92, y), (162, y), (170, y + 32), (84, y + 32)]), RED, bevel=8, grain=0.3))
        over(img, shade(rrect([88, y - 4, 166, y + 4], 3), BRASS, bevel=3))
    over(img, shade(ellipse([110, 10, 144, 36]), BRASS, bevel=6, gloss=1.0))


def obj_sword(img):
    over(img, shade(poly([(120, 20), (136, 20), (140, 170), (116, 170)]), STEEL, bevel=7, gloss=1.0))
    over(img, shade(rrect([84, 168, 172, 182], 6), BRASS, bevel=5))
    over(img, shade(rrect([118, 182, 138, 222], 4), WOOD, bevel=5, grain=0.3))
    over(img, shade(ellipse([114, 218, 142, 242]), BRASS, bevel=6))


def obj_city(img):
    over(img, shade(rrect([24, 120, 232, 220], 4), STEEL, bevel=12, grain=0.3))
    for x in range(26, 230, 26):
        over(img, shade(rrect([x, 104, x + 14, 124], 2), STEEL, bevel=4, grain=0.3))
    over(img, shade(union(rrect([108, 168, 148, 220], 4), ellipse([108, 150, 148, 186])), LACQ, bevel=6))
    over(img, shade(rrect([88, 70, 168, 120], 4), RED, bevel=8, grain=0.25))
    over(img, shade(poly([(70, 74), (186, 74), (160, 40), (96, 40)]), LACQ, bevel=10))


def obj_coins(img):
    of.coin_stack(img)


def obj_banners(img):
    """两面旗交叉(合并国家)：黑旗与血红旗。"""
    for pole_x, cloth, pal in ((84, [(94, 36), (204, 46), (190, 82), (206, 116), (94, 110)], LACQ),
                               (172, [(162, 60), (52, 70), (66, 104), (50, 138), (162, 132)], RED)):
        over(img, shade(rrect([pole_x - 6, 24, pole_x + 6, 236], 4), WOOD, bevel=4, grain=0.3))
        over(img, shade(poly(cloth), pal, bevel=10, gloss=0.6, grain=0.3))
        over(img, shade(ellipse([pole_x - 10, 14, pole_x + 10, 32]), BRASS, bevel=4))


def obj_whiteflag(img):
    over(img, shade(rrect([58, 24, 72, 236], 5), WOOD, bevel=5, grain=0.3))
    over(img, shade(poly([(72, 34), (210, 46), (192, 90), (214, 132), (72, 124)]), BONE, bevel=10, gloss=0.6,
                    grain=0.35))


def obj_token(img):
    """令牌：黑漆牌身、旧铜卷云边、血红"令"纹、顶部铜环、下垂朱穗。"""
    over(img, shade(sub(ellipse([108, 6, 148, 46]), ellipse([118, 16, 138, 36])), BRASS, bevel=5))
    body = poly([(70, 40), (186, 40), (206, 66), (206, 168), (128, 222), (50, 168), (50, 66)])
    over(img, shade(body, BRASS, bevel=8, gloss=0.8))
    over(img, shade(poly([(80, 52), (176, 52), (192, 72), (192, 162), (128, 206), (64, 162), (64, 72)]), LACQ,
                    bevel=14, gloss=0.8, grain=0.15))
    over(img, shade(poly([(128, 76), (168, 116), (88, 116)]), RED, bevel=6))
    over(img, shade(rrect([96, 126, 160, 138], 3), RED, bevel=4))
    over(img, shade(rrect([120, 138, 136, 180], 3), RED, bevel=4))
    over(img, shade(lines([[(128, 220), (128, 240)]], 6), RED, bevel=2))
    over(img, shade(poly([(116, 236), (140, 236), (146, 256), (110, 256)]), RED, bevel=4, grain=0.3))


def obj_book(img):
    """法典：深红皮面、旧铜包角与中央圆章、书脊金箍、做旧书页、铜扣。"""
    over(img, shade(rrect([70, 34, 222, 222], 4), PARCH, bevel=8, grain=0.4))
    over(img, shade(rrect([40, 24, 204, 214], 8), RED, bevel=14, gloss=0.7, grain=0.3))
    over(img, shade(rrect([40, 24, 74, 214], 6), BLOOD, bevel=8, grain=0.25))
    for y in (50, 104, 158):
        over(img, shade(rrect([40, y, 74, y + 10], 3), BRASS, bevel=3))
    for x0, y0 in ((166, 24), (166, 176), (78, 24), (78, 176)):
        over(img, shade(rrect([x0, y0, x0 + 38, y0 + 38], 6), BRASS, bevel=6, gloss=0.9))
    over(img, shade(ellipse([104, 82, 176, 154]), BRASS, bevel=12, gloss=0.9))
    over(img, shade(ellipse([118, 96, 162, 140]), BLOOD, bevel=8))
    over(img, shade(rrect([196, 100, 226, 136], 6), BRASS, bevel=5))


# ═════════════ 角标 ═════════════
BX = BY = 194


def _badge(img, mask, palette):
    over(img, shade(mask.filter(of.ImageFilter.MaxFilter(11)), LACQ, bevel=2, flat=True))
    over(img, shade(mask, palette, bevel=9, gloss=0.8))


def b_plus(img): of.badge_plus(img, BX, BY, 42)
def b_cross(img): of.badge_cross(img, BX, BY, 42)
def b_arrow(img): of.badge_arrow(img, BX, BY, 42, deg=0, palette=BONE)
def b_back(img): of.badge_arrow(img, BX, BY, 42, deg=180, palette=BONE)
def b_up(img): of.badge_arrow(img, BX, BY, 42, deg=90, palette=BRASS)


def b_minus(img):
    _badge(img, rrect([BX - 42, BY - 15, BX + 42, BY + 15], 8), BLOOD)


def b_swap(img):
    a = poly([(BX - 40, BY - 18), (BX + 10, BY - 18), (BX + 10, BY - 34), (BX + 40, BY - 10), (BX + 10, BY + 12),
              (BX + 10, BY - 4), (BX - 40, BY - 4)])
    b = rotate(a, 180, BX, BY)
    _badge(img, union(a, b), BONE)


def b_check(img):
    _badge(img, lines([[(BX - 36, BY), (BX - 10, BY + 26), (BX + 38, BY - 30)]], 20), BONE)


def b_star(img):
    _badge(img, ef.star(BX, BY, 44), BRASS)


def b_alert(img):
    _badge(img, union(rrect([BX - 11, BY - 44, BX + 11, BY + 14], 6), ellipse([BX - 12, BY + 22, BX + 12, BY + 46])), BLOOD)


def b_seal(img):
    _badge(img, rrect([BX - 36, BY - 36, BX + 36, BY + 36], 6), RED)
    over(img, shade(sub(rrect([BX - 24, BY - 24, BX + 24, BY + 24], 3), rrect([BX - 14, BY - 14, BX + 14, BY + 14], 2)),
                    BONE, bevel=2, flat=True))


def b_war(img):
    m = union(rotate(rrect([BX - 46, BY - 7, BX + 46, BY + 7], 4), 45, BX, BY),
              rotate(rrect([BX - 46, BY - 7, BX + 46, BY + 7], 4), -45, BX, BY))
    over(img, shade(m.filter(of.ImageFilter.MaxFilter(11)), LACQ, bevel=2, flat=True))
    over(img, shade(m, STEEL, bevel=6, gloss=1.0))
    for x in (BX - 30, BX + 30):
        over(img, shade(ellipse([x - 9, BY + 21, x + 9, BY + 39]), BRASS, bevel=4))


BADGES = {"plus": b_plus, "cross": b_cross, "arrow": b_arrow, "back": b_back, "up": b_up, "minus": b_minus,
          "swap": b_swap, "check": b_check, "star": b_star, "alert": b_alert, "seal": b_seal, "war": b_war}


def make_plot(obj, badge):
    layer = canvas()
    obj(layer)
    img = inset(layer, 0.86, C - 14, C - 14) if badge else layer
    if badge:
        BADGES[badge](img)
    return pixelize(img)


PLOTS = {
    "feudal_offer_vassalage": (obj_chain, "arrow"),
    "feudal_tighten_vassalage": (obj_chain, "up"),
    "feudal_annex_vassal": (obj_chain, "plus"),
    "feudal_independence_war": (obj_broken_chain, "war"),
    "become_empire": (obj_crown, "up"),
    "usurp_imperial_legitimacy": (obj_seal, "swap"),
    "adopt_central_plains_institutions": (obj_bamboo, "plus"),
    "combine_kingdom": (obj_banners, "plus"),
    "empire_plots": (obj_hu, "alert"),
    "force_stop_war": (obj_whiteflag, "minus"),
    "empire_move_back_to_capital": (obj_palace, "back"),
    "kingdom_petition_title": (obj_title, "up"),
    "kingdom_start_join_taken_alliance": (obj_coins, "plus"),
    "kingdom_start_invite_to_faction": (obj_hu, "plus"),
    "faction_leader_influence_local_kingdom": (obj_hu, "arrow"),
    "empirecraft_city_culture_shift": (obj_brush, "swap"),
    "empirecraft_independent_title_culture_conversion": (obj_brush, "seal"),
    "empirecraft_restore_native_culture": (obj_brush, "back"),
    "empirecraft_cultural_assimilation_duty": (obj_brush, "check"),
    "empirecraft_kingdom_regime_conversion": (obj_edict, "swap"),
    "kingdom_expose_crime": (obj_scales, "alert"),
    "kingdom_start_religion_war": (obj_temple, "war"),
    "new_empire_royal": (obj_crown, "star"),
    "emperor_year_name": (obj_calendar, None),
    "kingdom_allow_army": (obj_shield, "check"),
    "kingdom_allow_diplomacy": (obj_envoy, "check"),
    "kingdom_allow_succession": (obj_crown, "check"),
    "kingdom_allow_self_army": (obj_sword, "check"),
    "kingdom_allow_independent": (obj_broken_chain, "check"),
    "empire_take_back_title": (obj_title, "back"),
    "emperor_posthumous_name": (obj_tablet, "star"),
    "king_acquire_title": (lambda i: (obj_title(i), obj_swords(i)), None),
    "kingdom_destroy_title": (obj_title, "cross"),
    "kingdom_get_title": (obj_title, "plus"),
    "kingdom_change_capital_title": (obj_palace, "arrow"),
    "kingdom_join_empire": (obj_crown, "arrow"),
    "kingdom_create_title": (obj_title, "star"),
    "kingdom_add_city_into_title": (obj_city, "seal"),
    "empress_dowager_install_son": (obj_phoenix, "swap"),
    "minister_acquire_empire": (obj_minister, "up"),
    "minister_acquire_title": (obj_minister, "seal"),
    "minister_receive_nine_bestowments": (obj_axe, "star"),
}


# ═════════════ 页签：国徽式徽章 ═════════════
def emblem_tablet():
    m = of.blank(); d = ImageDraw.Draw(m)
    d.polygon([(96, 74), (110, 58), (146, 58), (160, 74)], fill=255)
    d.rectangle([94, 74, 162, 168], fill=255)
    d.rectangle([108, 88, 148, 154], fill=0)
    d.rectangle([122, 96, 134, 146], fill=255)
    d.rectangle([80, 168, 176, 180], fill=255)
    return m


def emblem_token():
    m = of.blank(); d = ImageDraw.Draw(m)
    d.polygon([(92, 70), (164, 70), (176, 84), (176, 150), (128, 184), (80, 150), (80, 84)], fill=255)
    d.polygon([(100, 80), (156, 80), (164, 90), (164, 146), (128, 172), (92, 146), (92, 90)], fill=0)
    d.polygon([(128, 92), (150, 116), (106, 116)], fill=255)
    d.rectangle([110, 124, 146, 132], fill=255)
    d.rectangle([122, 132, 134, 160], fill=255)
    d.ellipse([120, 54, 136, 70], fill=255)
    return m


def emblem_slips():
    m = of.blank(); d = ImageDraw.Draw(m)
    for k in range(6):
        x = 82 + k * 17
        d.rounded_rectangle([x, 70, x + 12, 176], radius=4, fill=255)
    d.rectangle([72, 96, 184, 102], fill=255); d.rectangle([72, 146, 184, 152], fill=255)
    return m


TABS = {
    "TabDynasty": obj_tablet,
    "TabBureau": obj_palace,
    "TabSetting": obj_token,
    "TabConstitution": obj_book,
    "TabInstitutions": obj_bamboo,
}


def make_tab(obj):
    """页签：与原版页签一样是独立器物(不套徽章)。"""
    img = canvas()
    obj(img)
    return pixelize(img)


# ═════════════ 身份特质：小徽章(旧铜环 + 器物) ═════════════
def obj_branch(img, flower):
    over(img, shade(lines([[(52, 214), (200, 46)]], 12), WOOD, bevel=4, grain=0.3))
    for x, y in ((80, 160), (110, 130), (130, 104), (156, 90), (176, 62), (96, 116), (150, 128), (190, 96)):
        over(img, shade(ellipse([x - 16, y - 16, x + 16, y + 16]), flower, bevel=8, gloss=0.9))
    for x, y in ((70, 184), (120, 150), (168, 108)):
        over(img, shade(rotate(ellipse([x - 22, y - 8, x + 22, y + 8]), 40, x, y), JADE, bevel=6, grain=0.2))


def obj_list(img):
    over(img, shade(rrect([40, 40, 216, 216], 6), BRASS, bevel=8))
    over(img, shade(rrect([56, 56, 200, 200], 4), RED, bevel=6))
    over(img, shade(rrect([66, 66, 190, 190], 3), PARCH, bevel=6, grain=0.35))
    for x in range(176, 80, -18):
        over(img, shade(lines([[(x, 86), (x, 176)]], 6), LACQ, bevel=2, flat=True))
    over(img, shade(ellipse([160, 72, 190, 102]), RED, bevel=5))


def obj_staff(img):
    over(img, shade(rotate(rrect([120, 40, 136, 236], 8), -12, 128, 236), WOOD, bevel=7, grain=0.35))
    bird = union(ellipse([96, 34, 176, 80]), ellipse([156, 22, 196, 58]), poly([(100, 60), (52, 46), (66, 80)]))
    over(img, shade(bird, JADE, bevel=10, gloss=0.8))
    over(img, shade(ellipse([178, 34, 186, 42]), LACQ, bevel=2, flat=True))
    over(img, shade(rrect([106, 108, 142, 120], 4), RED, bevel=4))


def obj_helmet(img, metal, plume):
    over(img, shade(union(ellipse([60, 50, 196, 186]), rrect([60, 118, 196, 186], 6)), metal, bevel=18, gloss=0.9,
                    grain=0.2))
    for y in (104, 130, 156):
        over(img, shade(lines([[(66, y), (190, y)]], 3), LACQ, bevel=1, flat=True))
    over(img, shade(rrect([40, 168, 92, 222], 8), metal, bevel=8, grain=0.2))
    over(img, shade(rrect([164, 168, 216, 222], 8), metal, bevel=8, grain=0.2))
    over(img, shade(rrect([88, 176, 168, 190], 4), BRASS, bevel=4))
    over(img, shade(poly([(110, 50), (146, 50), (156, 14), (100, 14)]), plume, bevel=8, grain=0.3))
    over(img, shade(rrect([120, 36, 136, 56], 3), BRASS, bevel=3))


def obj_founder(img):
    over(img, shade(poly([(120, 30), (136, 30), (140, 236), (116, 236)]), STEEL, bevel=6, gloss=1.0))
    obj_crown(img)


def obj_sun(img):
    for k in range(9):
        a = math.pi + k * math.pi / 8
        tip = (128 + math.cos(a) * 112, 150 + math.sin(a) * 112)
        l = (128 + math.cos(a - 0.12) * 66, 150 + math.sin(a - 0.12) * 66)
        r = (128 + math.cos(a + 0.12) * 66, 150 + math.sin(a + 0.12) * 66)
        over(img, shade(poly([l, tip, r]), BRASS, bevel=5, gloss=0.9))
    over(img, shade(ellipse([64, 86, 192, 214]), RED, bevel=20, gloss=0.8))
    over(img, shade(union(ellipse([30, 156, 110, 214]), ellipse([90, 146, 170, 214]), ellipse([150, 156, 228, 214]),
                          rrect([30, 180, 228, 222], 16)), BONE, bevel=12, grain=0.25))


TRAITS = {
    "iconJuren": lambda i: obj_branch(i, BRASS),
    "iconGongshi": lambda i: obj_branch(i, BONE),
    "iconJingshi": obj_list,
    "iconEmpireOfficer": obj_minister,
    "iconOfficerLeave": obj_staff,
    "iconEmpireArmy": lambda i: obj_helmet(i, IRON, RED),
    "iconEmpireEliteArmy": lambda i: obj_helmet(i, BRASS, BONE),
    "iconFounderRuler": obj_founder,
    "iconRestorerRuler": obj_sun,
}


def make_trait(obj):
    img = canvas()
    over(img, ef.shade(ef.ring(124, 106), ef.GOLD, bevel=9))
    over(img, ef.shade(ef.disc(105), ef.RED, bevel=30, gloss=0.25))
    layer = canvas()
    obj(layer)
    img = over(img, inset(layer, 0.8))
    return pixelize(img)


# ═════════════ 文化：纹章盾 ═════════════
SHIELD = [(40, 30), (216, 30), (216, 130), (196, 184), (128, 232), (60, 184), (40, 130)]


def charge(fn):
    m = of.blank(); fn(ImageDraw.Draw(m)); return m


def c_ding(d):
    d.rectangle([92, 70, 104, 100], fill=255); d.rectangle([152, 70, 164, 100], fill=255)
    d.rectangle([76, 96, 180, 108], fill=255); d.polygon([(80, 108), (176, 108), (168, 156), (88, 156)], fill=255)
    for x in (92, 124, 156):
        d.rectangle([x, 156, x + 10, 190], fill=255)


def c_wall(d):
    d.rectangle([60, 120, 196, 180], fill=255)
    for x in range(60, 197, 22):
        d.rectangle([x, 106, x + 12, 122], fill=255)
    d.rectangle([104, 80, 152, 124], fill=255); d.polygon([(96, 82), (160, 82), (128, 58)], fill=255)
    d.rectangle([118, 146, 138, 180], fill=0)


def c_torii(d):
    d.polygon([(60, 70), (196, 70), (188, 86), (68, 86)], fill=255); d.rectangle([72, 104, 184, 114], fill=255)
    d.rectangle([84, 84, 98, 190], fill=255); d.rectangle([158, 84, 172, 190], fill=255)


def c_mountain(d):
    d.polygon([(56, 170), (100, 80), (124, 120), (152, 66), (200, 170)], fill=255)
    for y in (180, 196):
        d.line([(60, y), (90, y - 8), (120, y), (150, y - 8), (196, y)], fill=255, width=7)


def c_yurt(d):
    d.polygon([(64, 130), (128, 70), (192, 130)], fill=255); d.rectangle([64, 128, 192, 186], fill=255)
    d.rectangle([116, 146, 140, 186], fill=0); d.rectangle([118, 60, 138, 74], fill=255)


def c_crescent(d):
    d.ellipse([60, 66, 180, 186], fill=255); d.ellipse([86, 60, 196, 170], fill=0)
    pts = []
    for k in range(10):
        r = 22 if k % 2 == 0 else 9
        a = -math.pi / 2 + k * math.pi / 5
        pts.append((168 + math.cos(a) * r, 124 + math.sin(a) * r))
    d.polygon(pts, fill=255)


def c_pyramid(d):
    d.polygon([(52, 190), (128, 60), (204, 190)], fill=255); d.ellipse([168, 54, 196, 82], fill=255)


def c_steps(d):
    for k, (x0, x1) in enumerate(((64, 192), (80, 176), (96, 160), (112, 144))):
        d.rectangle([x0, 176 - k * 26, x1, 190 - k * 26], fill=255)
    d.rectangle([116, 76, 140, 98], fill=255)


def c_feather(d):
    d.polygon([(84, 196), (102, 130), (140, 76), (170, 56), (166, 92), (134, 150), (96, 200)], fill=255)
    d.line([(88, 204), (164, 64)], fill=0, width=4)


def c_laurel(d):
    for k in range(9):
        for side in (-1, 1):
            a = math.pi * (0.62 + k * 0.095)
            x = 128 + side * math.cos(a) * 70; y = 130 - math.sin(a) * 70
            d.ellipse([x - 11, y - 7, x + 11, y + 7], fill=255)
    d.rectangle([110, 186, 146, 198], fill=255)


def c_tower(d):
    d.rectangle([88, 84, 168, 196], fill=255)
    for x in (88, 112, 136, 160):
        d.rectangle([x, 64, x + 10, 86], fill=255)
    d.rectangle([116, 150, 140, 196], fill=0); d.ellipse([116, 138, 140, 162], fill=0)


def c_fleur(d):
    d.ellipse([112, 54, 144, 130], fill=255)
    d.ellipse([66, 96, 116, 136], fill=255); d.ellipse([140, 96, 190, 136], fill=255)
    d.rectangle([86, 136, 170, 150], fill=255); d.polygon([(110, 150), (146, 150), (128, 196)], fill=255)


def c_eagle(d, double=False):
    d.polygon([(128, 104), (196, 70), (182, 104), (200, 118), (172, 140), (140, 140)], fill=255)
    d.polygon([(128, 104), (60, 70), (74, 104), (56, 118), (84, 140), (116, 140)], fill=255)
    d.rectangle([112, 96, 144, 166], fill=255); d.polygon([(108, 166), (148, 166), (128, 198)], fill=255)
    if double:
        d.ellipse([90, 64, 118, 92], fill=255); d.ellipse([138, 64, 166, 92], fill=255)
    else:
        d.ellipse([114, 66, 142, 96], fill=255)


def c_onion(d):
    d.polygon([(128, 54), (166, 104), (150, 124), (106, 124), (90, 104)], fill=255)
    d.rectangle([100, 124, 156, 196], fill=255); d.rectangle([124, 36, 132, 56], fill=255)
    d.rectangle([116, 42, 140, 48], fill=255); d.rectangle([118, 160, 138, 196], fill=0)


def c_ship(d):
    d.polygon([(52, 150), (204, 150), (184, 186), (72, 186)], fill=255)
    d.rectangle([124, 62, 132, 152], fill=255); d.rectangle([92, 70, 166, 132], fill=255)
    d.polygon([(40, 120), (60, 150), (52, 150)], fill=255); d.polygon([(216, 120), (196, 150), (204, 150)], fill=255)


def c_lotus(d):
    d.ellipse([110, 60, 146, 160], fill=255)
    d.polygon([(66, 100), (118, 130), (116, 170), (84, 160)], fill=255)
    d.polygon([(190, 100), (138, 130), (140, 170), (172, 160)], fill=255)
    d.rectangle([76, 170, 180, 184], fill=255)


def c_column(d):
    d.rectangle([60, 64, 196, 84], fill=255); d.ellipse([52, 72, 84, 100], fill=255); d.ellipse([172, 72, 204, 100], fill=255)
    d.rectangle([100, 84, 156, 180], fill=255); d.rectangle([84, 180, 172, 196], fill=255)
    for x in (112, 128, 144):
        d.line([(x, 90), (x, 176)], fill=0, width=3)


def c_menorah(d):
    for k in range(7):
        x = 68 + k * 20
        d.rectangle([x - 3, 70 + abs(k - 3) * 6, x + 3, 140], fill=255)
        d.ellipse([x - 6, 58 + abs(k - 3) * 6, x + 6, 72 + abs(k - 3) * 6], fill=255)
    d.rectangle([64, 136, 192, 146], fill=255); d.rectangle([122, 140, 134, 180], fill=255)
    d.rectangle([96, 178, 160, 192], fill=255)


def c_tower_block(d):
    d.rectangle([76, 90, 120, 196], fill=255); d.rectangle([126, 56, 172, 196], fill=255)
    for y in range(68, 190, 16):
        d.line([(132, y), (166, y)], fill=0, width=4)
    for y in range(100, 190, 16):
        d.line([(82, y), (114, y)], fill=0, width=4)
    d.ellipse([150, 150, 198, 198], fill=255)


def c_leaf(d):
    d.polygon([(70, 196), (74, 130), (110, 84), (186, 58), (180, 120), (130, 176)], fill=255)
    d.line([(76, 190), (176, 66)], fill=0, width=4)


def c_flag(d):
    d.rectangle([76, 56, 86, 200], fill=255); d.polygon([(86, 62), (186, 72), (170, 104), (188, 136), (86, 128)], fill=255)


CULTURES = {
    "Default": (c_flag, STEEL, BONE), "Huaxia": (c_ding, RED, BRASS), "China": (c_wall, RED, BRASS),
    "Japan": (c_torii, BONE, RED), "Shanhai": (c_mountain, JADE, BRASS), "Youmu": (c_yurt, WOOD, BONE),
    "Arab": (c_crescent, JADE, BRASS), "Egypt": (c_pyramid, PARCH, LACQ), "Aztec": (c_steps, JADE, BONE),
    "Ojibwe": (c_feather, WOOD, BONE), "Roma": (c_laurel, RED, BRASS), "Western": (c_tower, STEEL, LACQ),
    "Frankish": (c_fleur, LACQ, BRASS), "Germanic": (c_eagle, BRASS, LACQ), "Slavonic": (c_onion, STEEL, BRASS),
    "Viking": (c_ship, LACQ, BONE), "India": (c_lotus, RED, BONE), "Persepolis": (c_column, PARCH, LACQ),
    "Kosher": (c_menorah, LACQ, BRASS), "Corporate": (c_tower_block, STEEL, LACQ),
    "Emperor": (lambda d: c_eagle(d, double=True), LACQ, BRASS), "ElfFancy": (c_leaf, JADE, BONE),
}


def make_culture(fn, field, metal):
    img = canvas()
    over(img, shade(poly(SHIELD), BRASS, bevel=8, gloss=0.8))
    inner = [(52, 42), (204, 42), (204, 128), (186, 178), (128, 220), (70, 178), (52, 128)]
    over(img, shade(poly(inner), field, bevel=24, gloss=0.35, grain=0.25))
    over(img, shade(charge(fn), metal, bevel=8, gloss=0.9))
    return pixelize(img)


# ═════════════ godpower 按钮(帝国组；宗族列表、普天之下保留原图) ═════════════
def b_crown(img):
    """冕角标：小冕板 + 垂旒 + 冠身。"""
    m = union(poly([(BX - 40, BY - 30), (BX + 40, BY - 30), (BX + 46, BY - 18), (BX - 46, BY - 18)]),
              rrect([BX - 20, BY - 18, BX + 20, BY + 34], 6))
    over(img, shade(m.filter(of.ImageFilter.MaxFilter(11)), LACQ, bevel=2, flat=True))
    over(img, shade(poly([(BX - 40, BY - 30), (BX + 40, BY - 30), (BX + 46, BY - 18), (BX - 46, BY - 18)]), LACQ, bevel=4))
    over(img, shade(rrect([BX - 20, BY - 18, BX + 20, BY + 34], 6), BRASS, bevel=8, gloss=0.9))
    for x in range(BX - 38, BX + 40, 13):
        over(img, shade(rrect([x - 3, BY - 16, x + 3, BY + 6], 3), BONE, bevel=2))
    over(img, shade(ellipse([BX - 7, BY + 4, BX + 7, BY + 18]), RED, bevel=4, gloss=1.0))


BADGES["crown"] = b_crown


def obj_flag(img):
    over(img, shade(rrect([58, 24, 72, 236], 5), WOOD, bevel=5, grain=0.3))
    over(img, shade(ellipse([54, 12, 76, 34]), BRASS, bevel=5))
    over(img, shade(poly([(72, 34), (210, 46), (192, 90), (214, 132), (72, 124)]), RED, bevel=12, gloss=0.6, grain=0.35))


def obj_calligraphy(img):
    """铭牌字体：书法字帖——旧纸上横、竖、撇、捺几道浓墨(抽象笔画，不成字)，左下朱印，右侧斜放毛笔。"""
    over(img, shade(rrect([24, 30, 168, 224], 6), PARCH, bevel=12, gloss=0.3, grain=0.4))
    ink = union(
        lines([[(48, 76), (140, 66)]], 18),
        lines([[(92, 72), (96, 182)]], 16),
        lines([[(90, 124), (50, 178)]], 14),
        lines([[(100, 128), (148, 174)]], 14),
    )
    over(img, shade(ink, LACQ, bevel=3, flat=True))
    over(img, shade(rrect([40, 190, 68, 216], 3), RED, bevel=4, grain=0.2))
    handle = rotate(rrect([178, 10, 210, 166], 12), 20, 194, 120)
    over(img, shade(handle, WOOD, bevel=9, gloss=0.6, grain=0.35))
    tip = rotate(poly([(174, 164), (214, 164), (206, 212), (194, 238), (182, 212)]), 20, 194, 120)
    over(img, shade(tip, LACQ, bevel=8, gloss=0.5))
    band = rotate(rrect([172, 152, 216, 168], 4), 20, 194, 120)
    over(img, shade(band, BRASS, bevel=4, gloss=0.9))


def obj_farmland(img):
    """规划农田：斜放的一方田地(黑胡桃色垄沟)，田里几株旧铜色麦穗，田边一根木界桩拉着暗红绳(耕地红线)。"""
    field = poly([(28, 150), (150, 104), (232, 150), (110, 214)])
    over(img, shade(field, WOOD, bevel=10, grain=0.5))
    furrows = lines([[(52, 152), (156, 112)], [(74, 166), (178, 124)], [(96, 180), (200, 138)],
                     [(118, 194), (214, 150)]], 6)
    over(img, shade(furrows, LACQ, bevel=2, flat=True))
    for x, y in [(80, 120), (112, 106), (144, 96), (100, 150), (132, 136)]:
        over(img, shade(lines([[(x, y + 34), (x + 4, y)]], 5), GOLD, bevel=2, flat=True))
        over(img, shade(ellipse([x - 8, y - 22, x + 14, y + 6]), GOLD, bevel=5, gloss=0.8, grain=0.3))
    over(img, shade(rrect([196, 52, 214, 176], 4), WOOD, bevel=5, grain=0.3))
    over(img, shade(ellipse([192, 44, 218, 66]), BRASS, bevel=5, gloss=0.9))
    over(img, shade(lines([[(204, 92), (232, 150)], [(204, 92), (150, 104)]], 7), RED, bevel=3, gloss=0.6))


def obj_factory(img):
    """工业区：锯齿屋顶的厂房(枪灰)，两根黑铁烟囱，门前旧铜齿轮。"""
    over(img, shade(rrect([150, 30, 176, 150], 3), IRON, bevel=6, grain=0.3))
    over(img, shade(rrect([190, 56, 214, 150], 3), IRON, bevel=6, grain=0.3))
    roof = poly([(28, 120), (28, 96), (76, 72), (76, 96), (124, 72), (124, 96), (172, 72), (172, 96), (228, 96),
                 (228, 120)])
    over(img, shade(roof, STEEL, bevel=8, gloss=0.6))
    over(img, shade(rrect([28, 116, 228, 214], 4), STEEL, bevel=12, grain=0.3))
    for x in (48, 92, 136):
        over(img, shade(rrect([x, 136, x + 26, 160], 2), BRASS, bevel=3, gloss=0.8))
    gear = sub(ellipse([150, 140, 222, 212]), ellipse([174, 164, 198, 188]))
    for k in range(8):
        a = k * math.pi / 4
        cx, cy = 186 + 38 * math.cos(a), 176 + 38 * math.sin(a)
        gear = union(gear, rrect([cx - 8, cy - 8, cx + 8, cy + 8], 2))
    over(img, shade(sub(gear, ellipse([174, 164, 198, 188])), BRASS, bevel=8, gloss=0.9))


def obj_houses(img):
    """居住区：一排三座民居，骨白墙、暗红坡顶、黑胡桃门窗。"""
    for x0, h, pal in ((24, 96, BONE), (92, 70, BONE), (164, 100, BONE)):
        top = 214 - h - 40
        over(img, shade(rrect([x0, 214 - h, x0 + 68, 214], 3), pal, bevel=8, grain=0.3))
        over(img, shade(poly([(x0 - 6, 214 - h + 4), (x0 + 34, top), (x0 + 74, 214 - h + 4)]), RED, bevel=8, gloss=0.6))
        over(img, shade(rrect([x0 + 24, 176, x0 + 44, 214], 2), LACQ, bevel=3))
        over(img, shade(rrect([x0 + 10, 214 - h + 14, x0 + 26, 214 - h + 30], 2), LACQ, bevel=2))


def obj_market(img):
    """商业区：条纹遮阳篷(暗红/骨白)的货摊，台面上旧铜钱币一摞。"""
    over(img, shade(rrect([40, 96, 50, 214], 3), WOOD, bevel=4, grain=0.3))
    over(img, shade(rrect([206, 96, 216, 214], 3), WOOD, bevel=4, grain=0.3))
    for k in range(6):
        x0 = 30 + k * 33
        over(img, shade(poly([(x0, 60), (x0 + 33, 60), (x0 + 36, 110), (x0 - 3, 110)]), RED if k % 2 == 0 else BONE,
                        bevel=6, gloss=0.6))
    over(img, shade(rrect([30, 150, 226, 176], 4), WOOD, bevel=8, grain=0.4))
    for k in range(4):
        over(img, shade(ellipse([96, 136 - k * 12, 160, 156 - k * 12]), GOLD, bevel=5, gloss=1.0))


def obj_tower(img):
    """军事区：石砌箭楼(枪灰)带雉堞，木杆上一面暗红三角旗(无任何国家标志)。"""
    over(img, shade(rrect([120, 20, 128, 80], 2), WOOD, bevel=3, grain=0.3))
    over(img, shade(poly([(128, 22), (180, 38), (128, 56)]), RED, bevel=5, gloss=0.6))
    body = union(rrect([72, 92, 184, 222], 4), *[rrect([68 + k * 30, 70, 88 + k * 30, 100], 2) for k in range(4)])
    over(img, shade(body, STEEL, bevel=14, grain=0.4))
    over(img, shade(rrect([116, 120, 140, 160], 8), LACQ, bevel=3))
    over(img, shade(rrect([108, 178, 148, 222], 10), LACQ, bevel=3))


def obj_pine(img):
    """保护区：两株苍松(暗翠)与一根旧铜界碑。"""
    for cx, base, scale in ((100, 222, 1.0), (176, 222, 0.75)):
        over(img, shade(rrect([cx - 8, base - 40 * scale, cx + 8, base], 3), WOOD, bevel=4, grain=0.3))
        for k in range(3):
            y = base - 40 * scale - k * 46 * scale
            w = (64 - k * 14) * scale
            over(img, shade(poly([(cx - w, y), (cx, y - 70 * scale), (cx + w, y)]), JADE, bevel=10, gloss=0.5, grain=0.3))
    over(img, shade(rrect([30, 150, 58, 222], 6), BRASS, bevel=6, gloss=0.8))


def obj_blueprint(img):
    """规划决议：摊开的规划图(羊皮纸，方格与分区色块)，上压一把旧铜分规。"""
    over(img, shade(rrect([30, 40, 226, 216], 6), PARCH, bevel=10, grain=0.4))
    grid = lines([[(30, y), (226, y)] for y in range(72, 216, 32)] + [[(x, 40), (x, 216)] for x in range(62, 226, 32)], 2)
    over(img, shade(grid, WOOD, bevel=1, flat=True))
    over(img, shade(rrect([64, 74, 124, 134], 2), RED, bevel=3, flat=True))
    over(img, shade(rrect([128, 138, 188, 198], 2), JADE, bevel=3, flat=True))
    over(img, shade(rrect([64, 138, 124, 166], 2), STEEL, bevel=3, flat=True))
    over(img, shade(lines([[(150, 40), (96, 220)], [(150, 40), (214, 210)]], 9), BRASS, bevel=4, gloss=0.9))
    over(img, shade(ellipse([136, 26, 164, 54]), BRASS, bevel=6, gloss=1.0))


def obj_bridle(img):
    """羁縻：皮革笼头一圈，两只旧铜环，衔铁一横，骨白缰绳垂下(羁为马络头、縻为牛缰)。"""
    over(img, shade(sub(ellipse([52, 14, 172, 150]), ellipse([76, 38, 148, 126])), LACQ, bevel=10, grain=0.4))
    over(img, shade(lines([[(52, 132), (172, 132)]], 18), STEEL, bevel=6, gloss=1.0))
    for x0 in (14, 154):
        over(img, shade(sub(ellipse([x0, 100, x0 + 64, 164]), ellipse([x0 + 18, 118, x0 + 46, 146])), BRASS, bevel=7, gloss=1.0))
    over(img, shade(lines([[(204, 160), (214, 196), (190, 220), (220, 246)]], 12), BONE, bevel=4, grain=0.3))


GODPOWERS = {
    "empire_layer": (obj_title, "crown"),
    "create_empire": (obj_crown, "plus"),
    "empire_form": (obj_crown, "up"),
    "remove_empire": (obj_crown, "cross"),
    "empire_list": (obj_list, None),
    "actor_create_kingdom": (obj_flag, "plus"),
    "territory_font": (obj_calligraphy, None),
    "farm_planning": (obj_farmland, None),
    "zone_industry": (obj_factory, None),
    "zone_residential": (obj_houses, None),
    "zone_commerce": (obj_market, None),
    "zone_military": (obj_tower, None),
    "zone_reserve": (obj_pine, None),
    "plan_policy": (obj_blueprint, None),
    "jimi_policy": (obj_bridle, None),
}


def obj_stamp(img):
    """行政区官印：古铜印钮、朱红印面、骨白篆刻边框。"""
    over(img, shade(union(ellipse([94, 24, 162, 80]), rrect([104, 60, 152, 100], 8)), BRASS, bevel=12, gloss=0.9))
    over(img, shade(rrect([40, 92, 216, 224], 10), RED, bevel=16, gloss=0.6, grain=0.3))
    over(img, shade(sub(rrect([60, 112, 196, 204], 6), rrect([76, 128, 180, 188], 4)), BONE, bevel=4))
    over(img, shade(rrect([96, 140, 160, 152], 3), BONE, bevel=2))
    over(img, shade(rrect([122, 140, 134, 180], 3), BONE, bevel=2))


def obj_heat_flag(img):
    """民族情绪图层：木杆旧铜顶，五道由冷到热的色带(黑铁 → 枪灰 → 旧铜 → 暗红 → 血红；避免像任何国旗)。"""
    over(img, shade(rrect([40, 24, 56, 238], 6), WOOD, bevel=5, grain=0.3))
    over(img, shade(ellipse([34, 10, 62, 38]), BRASS, bevel=6, gloss=1.0))
    bands = (IRON, STEEL, BRASS, RED, BLOOD)
    for k, pal in enumerate(bands):
        x0 = 56 + k * 36
        wave = (0, -8, -12, -6, 4)[k]
        over(img, shade(rrect([x0, 40 + wave, x0 + 38, 150 + wave], 2), pal, bevel=8, gloss=0.6, grain=0.3))


# godpower：法理、行政区、帝国核心工具(直接覆盖原文件路径，代码不用改)
def obj_lock(img):
    """未解锁：黑铁锁梁，旧铜锁体，骨白钥匙孔。"""
    loop = sub(ellipse([66, 30, 190, 164]), ellipse([91, 55, 165, 154]))
    over(img, shade(loop, STEEL, bevel=9, gloss=0.8))
    over(img, shade(rrect([52, 112, 204, 230], 14), BRASS, bevel=12, gloss=0.65, grain=0.25))
    keyhole = union(ellipse([113, 145, 143, 175]), poly([(122, 164), (134, 164), (143, 200), (113, 200)]))
    over(img, shade(keyhole, LACQ, bevel=3, flat=True))


TOOLS = {
    "iconLock": (obj_lock, None),
    "iconToolTitleLayer": (obj_title, None),
    "iconToolTitleCreate": (obj_title, "star"),
    "iconToolTitleAdd": (obj_title, "arrow"),
    "iconToolTitleRemove": (obj_title, "minus"),
    "iconToolProvinceCreate": (obj_stamp, "star"),
    "iconToolProvinceAdd": (obj_stamp, "arrow"),
    "iconToolProvinceRemove": (obj_stamp, "minus"),
    "iconToolCoreCreate": (obj_seal, "star"),
    "iconToolCoreAddTitle": (obj_seal, "arrow"),
    "iconToolCoreRemoveTitle": (obj_seal, "minus"),
    "iconToolCoreDestroy": (obj_seal, "cross"),
    "iconNationLayer": (obj_heat_flag, None),
}


# ═════════════ 理念徽章：在原图(Tools/IconForge/source/ideology_*.png)上调成冷峻色、纯黑描边 ═════════════
def regrade_badge(src):
    arr = np.asarray(Image.open(src).convert("RGBA")).astype(np.float32)
    rgb, a = arr[..., :3] / 255.0, arr[..., 3]
    mx, mn = rgb.max(-1), rgb.min(-1)
    hue = np.zeros_like(mx)
    d = np.where(mx - mn == 0, 1, mx - mn)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    hue = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    lum = (rgb * np.array([0.3, 0.59, 0.11])).sum(-1, keepdims=True)
    cool = ((hue > 75) & (hue < 280))[..., None]
    desat = np.where(cool, 0.75, 0.45)
    rgb = rgb + (lum - rgb) * desat
    gold = ((hue >= 30) & (hue <= 70) & (mx - mn > 0.15))[..., None]
    brass = np.array([140, 112, 62]) / 255.0 * (lum / 0.45)
    rgb = np.where(gold, rgb * 0.55 + np.clip(brass, 0, 1) * 0.45, rgb)
    rgb = np.clip((rgb - 0.5) * 1.25 + 0.5, 0, 1) ** 1.18
    out = np.zeros_like(arr)
    out[..., :3] = rgb * 255
    out[..., 3] = a
    solid = a > 0
    pad = np.pad(solid, 1)
    edge = solid & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    dark = out[..., :3].mean(-1) < 70
    out[edge & dark, :3] = 0
    shifted = np.zeros_like(solid); shifted[1:, 1:] = solid[:-1, :-1]
    shadow = shifted & ~solid
    out[shadow] = (0, 0, 0, 80)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def obj_people(img):
    """人口：三人剪影(骨白)，中间一人在前。"""
    for cx, cy, pal in ((74, 92, STEEL), (182, 92, STEEL), (128, 76, BONE)):
        over(img, shade(ellipse([cx - 26, cy - 26, cx + 26, cy + 26]), pal, bevel=10, gloss=0.7))
        over(img, shade(union(ellipse([cx - 48, cy + 34, cx + 48, cy + 130]), rrect([cx - 48, cy + 82, cx + 48, 236], 6)),
                        pal, bevel=14, gloss=0.6, grain=0.2))


# 帝国列表窗口用的小图标(ui/icons/empirelist/)
EMPIRELIST = {
    "stat_cities": (obj_city, None),
    "stat_population": (obj_people, None),
    "stat_mandate": (obj_seal, None),
    "stat_years": (obj_calendar, None),
    "head_monarch": (obj_crown, None),
    "head_republic": (obj_hu, None),
    "filter_alive": (obj_crown, None),
    "filter_archived": (obj_tablet, None),
    "filter_all": (obj_title, None),
}


# ═════════════ 战略矿产(ui/icons/resources/iconRes_*) ═════════════
def _ore_pile(img, body, fleck=None, flecks=()):
    """三块堆叠的矿石，fleck 给出矿石表面的闪点(矿脉)。"""
    for box in ([40, 120, 150, 222], [110, 104, 222, 218], [70, 52, 184, 160]):
        over(img, shade(poly([(box[0], (box[1] + box[3]) // 2), ((box[0] + box[2]) // 2 - 20, box[1]),
                              (box[2] - 10, box[1] + 14), (box[2], (box[1] + box[3]) // 2 + 10),
                              ((box[0] + box[2]) // 2 + 14, box[3]), (box[0] + 12, box[3] - 8)]),
                        body, bevel=14, gloss=0.5, grain=0.4))
    if fleck is not None:
        for x, y, r in flecks:
            over(img, shade(ellipse([x - r, y - r, x + r, y + r]), fleck, bevel=4, gloss=1.0))


def obj_res_copper(img):
    """铜矿：铁灰矿石上一道道铜绿与赤铜色矿脉。"""
    _ore_pile(img, IRON, BRASS, [(100, 90, 10), (150, 150, 9), (82, 170, 8), (176, 120, 7), (126, 112, 6)])
    over(img, shade(ellipse([150, 76, 172, 98]), JADE, bevel=4, gloss=0.7))


def obj_res_coal(img):
    """煤：漆黑发亮的煤块。"""
    _ore_pile(img, LACQ, STEEL, [(110, 80, 5), (170, 140, 4), (80, 160, 4)])


def obj_res_saltpeter(img):
    """硝石：骨白色结晶簇。"""
    for x0, h in ((60, 130), (100, 170), (140, 150), (176, 110)):
        over(img, shade(poly([(x0, 220), (x0 + 16, 220 - h), (x0 + 34, 220)]), BONE, bevel=8, gloss=0.9))
    over(img, shade(rrect([40, 210, 216, 230], 6), STEEL, bevel=5, grain=0.3))


def obj_res_sulfur(img):
    """硫磺：旧铜黄的晶块。"""
    _ore_pile(img, GOLD, BONE, [(110, 82, 6), (164, 140, 5), (84, 166, 5)])


def obj_res_oil(img):
    """石油：黑铁油桶，桶口淌出一滴黑油。"""
    over(img, shade(rrect([64, 60, 192, 224], 14), IRON, bevel=14, gloss=0.7, grain=0.2))
    for y in (100, 168):
        over(img, shade(rrect([60, y, 196, y + 12], 4), STEEL, bevel=4, gloss=0.8))
    over(img, shade(ellipse([150, 44, 178, 72]), STEEL, bevel=5))
    over(img, shade(union(ellipse([196, 150, 230, 196]), poly([(198, 166), (213, 116), (228, 166)])), LACQ,
                    bevel=6, gloss=1.0))


def obj_res_aluminium(img):
    """铝土：赭红色铝土矿石旁边一块银白铝锭。"""
    _ore_pile(img, RED, BONE, [(96, 92, 4), (164, 150, 4)])
    over(img, shade(poly([(118, 200), (140, 168), (228, 168), (206, 200)]), STEEL, bevel=8, gloss=1.0))
    over(img, shade(poly([(118, 200), (206, 200), (206, 228), (118, 228)]), STEEL, bevel=6, gloss=0.6))


def obj_res_rare_earth(img):
    """稀土：灰黑矿石嵌着几颗暗翠与血红的晶粒。"""
    _ore_pile(img, STEEL, JADE, [(104, 92, 9), (160, 146, 8), (84, 168, 7)])
    for x, y, r in ((140, 104, 8), (120, 180, 6)):
        over(img, shade(ellipse([x - r, y - r, x + r, y + r]), BLOOD, bevel=4, gloss=1.0))


def obj_res_uranium(img):
    """铀：铅灰矿石泛着暗绿的光，旁边一个黑底骨白的辐射警示牌(通用安全标识，非国家标志)。"""
    _ore_pile(img, IRON, JADE, [(100, 96, 12), (160, 150, 10), (84, 170, 9), (172, 110, 8)])
    over(img, shade(ellipse([150, 20, 236, 106]), BONE, bevel=6, gloss=0.6))
    for k in range(3):
        a0 = -math.pi / 2 + k * 2 * math.pi / 3 - 0.5
        pts = [(193, 63)]
        for j in range(7):
            a = a0 + j * (1.0 / 6)
            pts.append((193 + 36 * math.cos(a), 63 + 36 * math.sin(a)))
        over(img, shade(poly(pts), LACQ, bevel=2, flat=True))
    over(img, shade(ellipse([186, 56, 200, 70]), BONE, bevel=2, flat=True))
    over(img, shade(ellipse([189, 59, 197, 67]), LACQ, bevel=1, flat=True))


RESOURCES = {
    "copper": obj_res_copper, "coal": obj_res_coal, "saltpeter": obj_res_saltpeter, "sulfur": obj_res_sulfur,
    "oil": obj_res_oil, "aluminium": obj_res_aluminium, "rare_earth": obj_res_rare_earth, "uranium": obj_res_uranium,
}


# ═════════════ 生成 ═════════════
def build():
    out = {}
    for name, obj in TABS.items():
        out[f"GameResources/{name}.png"] = make_tab(obj)
    for pid, (obj, badge) in PLOTS.items():
        out[f"GameResources/ui/icons/plots/plot_{pid}.png"] = make_plot(obj, badge)
    for name, obj in TRAITS.items():
        out[f"GameResources/ui/icons/actor_traits/{name}.png"] = make_trait(obj)
    for name, (obj, badge) in GODPOWERS.items():
        out[f"GameResources/ui/icons/godpowers/{name}.png"] = make_plot(obj, badge)
    for name, (obj, badge) in EMPIRELIST.items():
        out[f"GameResources/ui/icons/empirelist/{name}.png"] = make_plot(obj, badge)
    for name, obj in RESOURCES.items():
        out[f"GameResources/ui/icons/resources/iconRes_{name}.png"] = make_plot(obj, None)
    for name, (obj, badge) in TOOLS.items():
        out[f"GameResources/ui/icons/{name}.png"] = make_plot(obj, badge)
    src = os.path.join(HERE, "source")
    for f in sorted(os.listdir(src)) if os.path.isdir(src) else []:
        if f.startswith("ideology_"):
            out[f"GameResources/ui/icons/actor_traits/{f}"] = regrade_badge(os.path.join(src, f))
    for name, (fn, field, metal) in CULTURES.items():
        out[f"GameResources/ui/icons/cultures/{name}.png"] = make_culture(fn, field, metal)
    return out


if __name__ == "__main__":
    icons = build()
    for path, im in icons.items():
        full = os.path.join(ROOT, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        im.save(full)
    if "--preview" in sys.argv:
        ims = list(icons.values())
        cols, k = 12, 4
        W = PIXEL * k + 8
        sheet = Image.new("RGBA", (cols * W, ((len(ims) + cols - 1) // cols) * W), (74, 78, 68, 255))
        for i, im in enumerate(ims):
            sheet.alpha_composite(im.resize((PIXEL * k, PIXEL * k), Image.NEAREST), ((i % cols) * W + 4, (i // cols) * W + 4))
        sheet.save(os.path.join(HERE, "imperial_preview.png"))
    print("wrote", len(icons))
