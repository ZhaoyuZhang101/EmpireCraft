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


def obj_ding(img):
    """鼎：青铜三足双耳鼎(天子赐胙、霸主之器)。"""
    for x0 in (60, 168):
        over(img, shade(sub(rrect([x0, 36, x0 + 28, 98], 6), rrect([x0 + 8, 46, x0 + 20, 92], 3)), BRASS, bevel=5))
    over(img, shade(union(rrect([46, 88, 210, 110], 6), ellipse([50, 78, 206, 196])), BRASS, bevel=12, gloss=0.8))
    over(img, shade(rrect([62, 118, 194, 134], 4), LACQ, bevel=4))
    for x in (92, 120, 148):
        over(img, shade(ellipse([x, 120, x + 16, 132]), RED, bevel=2))
    for x in (74, 122, 170):
        over(img, shade(rrect([x, 172, x + 12, 238], 4), BRASS, bevel=4, gloss=0.6))


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
    "kingdom_culture_reform": (lambda i: (obj_crown(i), obj_brush(i)), "swap"),
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
    "emperor_extend_posthumous": (obj_tablet, "plus"),
    "emperor_simplify_posthumous": (obj_tablet, "minus"),
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
    "member_request_aid": (obj_envoy, "alert"),
    "rebuild_city_style": (lambda i: obj_blueprint(i), "swap"),
    "member_aid_neighbour": (obj_swords, "arrow"),
    "member_aid_center": (obj_banners, "up"),
    "war_forage": (lambda i: obj_farmland(i), "minus"),
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


def obj_military(img):
    """军团：两柄交叉的戟，前有铜钉圆盾(黑漆盾面、朱红菱纹)。"""
    for x0, y0, x1, y1 in ((44, 232, 208, 34), (212, 232, 48, 34)):
        over(img, shade(lines([[(x0, y0), (x1, y1)]], 10), WOOD, bevel=4, grain=0.3))
    for cx, cy, d in ((208, 34, 1), (48, 34, -1)):
        over(img, shade(poly([(cx, cy - 26), (cx + 12 * d, cy + 6), (cx - 6 * d, cy + 12)]), STEEL, bevel=4, gloss=0.9))
        over(img, shade(poly([(cx - 4 * d, cy + 8), (cx - 34 * d, cy - 4), (cx - 28 * d, cy + 20)]), STEEL, bevel=4,
                        gloss=0.9))
    over(img, shade(ellipse([60, 68, 196, 204]), BRASS, bevel=10, gloss=0.9))
    over(img, shade(ellipse([76, 84, 180, 188]), LACQ, bevel=10, gloss=0.7, grain=0.15))
    over(img, shade(poly([(128, 100), (162, 136), (128, 172), (94, 136)]), RED, bevel=6))
    for x, y in ((128, 76), (128, 196), (68, 136), (188, 136)):
        over(img, shade(ellipse([x - 7, y - 7, x + 7, y + 7]), BRASS, bevel=3, gloss=0.9))


def obj_keys(img):
    """教廷：交叉的金、银两把钥匙(柄环在上、齿在下)，交叉处系一段红绳。"""
    for (x0, y0, x1, y1), metal, d in (((66, 212, 190, 60), BRASS, 1), ((190, 212, 66, 60), STEEL, -1)):
        over(img, shade(lines([[(x0, y0), (x1, y1)]], 14), metal, bevel=5, gloss=0.9))
        over(img, shade(sub(ellipse([x1 - 30, y1 - 30, x1 + 30, y1 + 30]),
                            ellipse([x1 - 13, y1 - 13, x1 + 13, y1 + 13])), metal, bevel=6, gloss=0.9))
        left, right = sorted((x0, x0 + 38 * d))
        over(img, shade(rrect([left, y0 - 36, right, y0 - 22], 3), metal, bevel=4))
        over(img, shade(rrect([left, y0 - 14, right, y0], 3), metal, bevel=4))
    over(img, shade(ellipse([112, 118, 144, 150]), RED, bevel=6))
    over(img, shade(lines([[(128, 146), (116, 182)], [(128, 146), (140, 182)]], 7), RED, bevel=3))


def obj_diet(img):
    """帝国议会：展开的决议书(骨白卷面、三行墨字、上下木轴)，右下一枚朱红蜡封垂两条红绶。"""
    over(img, shade(rrect([62, 66, 194, 204], 6), BONE, bevel=6, grain=0.2))
    for y in (50, 200):
        over(img, shade(rrect([46, y, 210, y + 18], 8), WOOD, bevel=5, grain=0.3))
    for y, right in ((96, 176), (122, 176), (148, 140)):
        over(img, shade(rrect([82, y, right, y + 9], 3), IRON, bevel=2, flat=True))
    over(img, shade(lines([[(158, 196), (144, 240)], [(168, 196), (182, 240)]], 9), RED, bevel=3))
    over(img, shade(ellipse([138, 160, 190, 212]), RED, bevel=7, gloss=1.0))
    over(img, shade(ellipse([154, 176, 174, 196]), BLOOD, bevel=3))


def obj_gentry(img):
    """世家页签：门前牌坊(阀阅)——深色木柱，起翘的瓦顶，两道额枋，中悬金匾。"""
    for x in (58, 176):
        over(img, shade(rrect([x, 96, x + 22, 228], 5), WOOD, bevel=8, gloss=0.5, grain=0.2))
        over(img, shade(rrect([x - 8, 218, x + 30, 234], 3), IRON, bevel=4))
    over(img, shade(rrect([50, 108, 206, 120], 3), RED, bevel=4))
    over(img, shade(rrect([50, 150, 206, 160], 3), RED, bevel=4))
    # 瓦顶：两端起翘
    over(img, shade(poly([(20, 92), (40, 66), (216, 66), (236, 92), (200, 82), (56, 82)]), IRON, bevel=6, gloss=0.6))
    over(img, shade(rrect([96, 44, 160, 68], 6), IRON, bevel=6, gloss=0.6))
    over(img, shade(rrect([100, 120, 156, 150], 4), GOLD, bevel=6, gloss=1.0))
    over(img, shade(rrect([106, 126, 150, 144], 3), RED, bevel=2, flat=True))


TABS = {
    "TabMilitary": obj_military,
    "TabGentry": obj_gentry,
    "TabPapacy": obj_keys,
    "TabDynasty": obj_tablet,
    "TabDependencies": obj_banners,
    "TabBureau": obj_palace,
    "TabSetting": obj_token,
    "TabConstitution": obj_book,
    "TabInstitutions": obj_bamboo,
    "TabDiet": obj_diet,
}


def make_tab(obj):
    """页签：与原版页签一样是独立器物(不套徽章)。"""
    img = canvas()
    obj(img)
    return pixelize(img)


# 能力栏(godpower 栏)顶上的标签按钮：同原版，26×16 纯白剪影，左半边画好后镜像成对称图形
POWER_TABS = {
    # 列国征伐：交叉的两把剑
    "TabWarfare": [
        ".............",
        ".##..........",
        ".###.........",
        "..###........",
        "...###.......",
        "....###......",
        ".....###.....",
        "......###....",
        ".......###...",
        "........###.#",
        ".........####",
        ".......#####.",
        "........####.",
        ".......##..##",
        "......##.....",
        ".....##......",
    ],
    # 文化与规划：摊开的书
    "TabCulturePlanning": [
        ".............",
        ".............",
        ".###.........",
        ".######......",
        ".#########...",
        ".###########.",
        ".##.....####.",
        ".###########.",
        ".##.....####.",
        ".###########.",
        ".##.....####.",
        ".###########.",
        "....########.",
        ".......#####.",
        "..........##.",
        ".............",
    ],
}


def make_power_tab(left_half):
    img = Image.new("RGBA", (26, 16), (0, 0, 0, 0))
    for y, row in enumerate(left_half):
        for x, ch in enumerate(row + row[::-1]):
            if ch == "#":
                img.putpixel((x, y), (255, 255, 255, 255))
    return img


# 地图军团兵牌上的棋子：白色剪影 + 1 像素深色描边，游戏里按国色染色(白色变国色、描边仍深)。
# 中央禁军为后、军区为车、军府为马、义军为兵
CHESS = {
    "pawn": [
        "...........",
        "....###....",
        "...#####...",
        "...#####...",
        "....###....",
        "...#####...",
        "....###....",
        "....###....",
        "...#####...",
        "..#######..",
        ".#########.",
        ".#########.",
    ],
    "rook": [
        ".##.###.##.",
        ".##.###.##.",
        ".#########.",
        ".#########.",
        "..#######..",
        "...#####...",
        "...#####...",
        "...#####...",
        "...#####...",
        "..#######..",
        ".#########.",
        ".#########.",
    ],
    "knight": [
        ".....##....",
        "....####...",
        "...######..",
        "..########.",
        ".##.######.",
        ".#...#####.",
        ".....#####.",
        "....######.",
        "...######..",
        "..#######..",
        ".#########.",
        ".#########.",
    ],
    "queen": [
        "#....#....#",
        "##..###..##",
        "###.###.###",
        ".#########.",
        "..#######..",
        "...#####...",
        "...#####...",
        "...#####...",
        "...#####...",
        "..#######..",
        ".#########.",
        ".#########.",
    ],
}


def make_chess(rows):
    w, h = len(rows[0]) + 2, len(rows) + 2
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    filled = {(x + 1, y + 1) for y, row in enumerate(rows) for x, ch in enumerate(row) if ch == "#"}
    for x, y in filled:
        img.putpixel((x, y), (255, 255, 255, 255))
    for x in range(w):
        for y in range(h):
            if (x, y) in filled:
                continue
            if any((x + dx, y + dy) in filled for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                img.putpixel((x, y), (24, 24, 24, 255))
    return img


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


def obj_wine(img):
    """昏聩：一只歪倒的青铜酒爵，酒洒了出来。"""
    over(img, shade(poly([(64, 96), (190, 96), (172, 168), (84, 168)]), BRASS, bevel=8, gloss=0.9))
    over(img, shade(rrect([54, 84, 200, 102], 6), BRASS, bevel=5, gloss=0.9))
    for x0, x1 in ((96, 82), (128, 128), (160, 174)):
        over(img, shade(lines([[(x0, 166), (x1, 214)]], 9), BRASS, bevel=3))
    over(img, shade(ellipse([140, 200, 222, 230]), RED, bevel=4, gloss=0.8))


def obj_broken_scepter(img):
    """庸弱：折成两截的玉圭。"""
    over(img, shade(poly([(128, 26), (152, 52), (148, 118), (108, 118), (104, 52)]), BONE, bevel=7, gloss=0.8))
    over(img, shade(rotate(poly([(106, 136), (150, 136), (146, 222), (110, 222)]), 22, 128, 180), BONE, bevel=7,
                    gloss=0.8))


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
    # 君主资质(见 RulerQualitySystem)：明君 竹简、庸弱 断圭、昏聩 倾爵、荒淫 白花枝、暴虐 钺
    "iconWiseRuler": obj_bamboo,
    "iconWeakRuler": obj_broken_scepter,
    "iconFatuousRuler": obj_wine,
    "iconDissoluteRuler": lambda i: obj_branch(i, BONE),
    "iconTyrantRuler": obj_axe,
    # 名将名臣(见 FamousTalentSystem)：名将 良才单剑、名双剑、国柱军旗；名臣 良才毛笔、名笏板、国柱官印
    "iconTalentGeneralFine": obj_sword,
    "iconTalentGeneralFamous": obj_swords,
    "iconTalentGeneralPillar": obj_banners,
    "iconTalentMinisterFine": obj_brush,
    "iconTalentMinisterFamous": obj_tablet,
    "iconTalentMinisterPillar": obj_seal,
    # WarBox 外交性格特质：原包没带图标(只有酗酒与兵种两张)，这里按同一画风补上，由 WarBoxCompatibility 挂接
    "warbox_xenophobe": lambda i: obj_lock(i),
    "warbox_cosmopolite": obj_envoy,
    "warbox_peacemaker": obj_whiteflag,
    "warbox_hoarder": obj_coins,
    "warbox_generous": lambda i: obj_farmland(i),
    "warbox_loyal": obj_shield,
    "warbox_treacherous": obj_broken_chain,
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


def obj_anchor(img):
    """港口：黑铁船锚，顶上铜环、横杆。"""
    over(img, shade(sub(ellipse([104, 24, 152, 72]), ellipse([118, 38, 138, 58])), BRASS, bevel=6, gloss=0.9))
    over(img, shade(rrect([120, 66, 136, 206], 4), IRON, bevel=6, gloss=0.6))
    over(img, shade(rrect([80, 88, 176, 104], 4), IRON, bevel=5, gloss=0.6))
    arc = sub(ellipse([52, 116, 204, 226]), ellipse([72, 100, 184, 206]))
    over(img, shade(sub(arc, rrect([40, 100, 216, 160], 0)), IRON, bevel=7, gloss=0.6))
    for x, d in ((54, -1), (202, 1)):
        over(img, shade(poly([(x, 150), (x + 22 * d, 142), (x + 6 * d, 176)]), IRON, bevel=4, gloss=0.6))


# 纯净地图模式下城市发展的小图标(见 CityDevelopmentRenderer)：住房、工厂、市场、港口、兵营、寺庙
CITYDEV = {
    "house": obj_houses,
    "factory": obj_factory,
    "market": lambda i: obj_market(i),
    "port": obj_anchor,
    "barracks": obj_swords,
    "temple": obj_temple,
}


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


def obj_relief(img):
    """地形浮雕：旧铜底座上浮起的铁灰山峦(骨白雪顶)，山前一道暗翠丘陵——像沙盘一样的立体地形。"""
    over(img, shade(rrect([22, 160, 234, 226], 10), BRASS, bevel=12, gloss=0.8))
    over(img, shade(poly([(34, 182), (96, 62), (146, 150), (186, 92), (228, 182)]), IRON, bevel=18, gloss=0.6, grain=0.4))
    over(img, shade(poly([(80, 92), (96, 62), (112, 92)]), BONE, bevel=6, gloss=0.8))
    over(img, shade(poly([(174, 112), (186, 92), (198, 112)]), BONE, bevel=5, gloss=0.8))
    over(img, shade(poly([(28, 196), (84, 160), (146, 182), (228, 194), (228, 210), (28, 210)]), JADE, bevel=10, gloss=0.4, grain=0.3))


def obj_edification(img):
    """教化图层：摊开的骨白书卷(暗红封皮)，上方三道由近及远、由浓到淡的旧铜弧——教化四方远播。"""
    for k, (r, w) in enumerate(((60, 16), (92, 14), (122, 12))):
        ring = sub(ellipse([128 - r, 150 - r, 128 + r, 150 + r]), ellipse([128 - r + w, 150 - r + w, 128 + r - w, 150 + r - w]))
        arc = sub(ring, rrect([0, 132, 256, 256], 0))
        over(img, shade(arc, BRASS if k < 2 else STEEL, bevel=6, gloss=0.8 - 0.2 * k, grain=0.2))
    over(img, shade(poly([(30, 170), (128, 150), (226, 170), (226, 230), (128, 212), (30, 230)]), RED, bevel=8, grain=0.3))
    over(img, shade(poly([(40, 164), (124, 148), (124, 206), (40, 222)]), PARCH, bevel=8, grain=0.4))
    over(img, shade(poly([(132, 148), (216, 164), (216, 222), (132, 206)]), PARCH, bevel=8, grain=0.4))
    over(img, shade(lines([[(56, 176), (108, 166)], [(56, 192), (108, 182)], [(148, 166), (200, 176)], [(148, 182), (200, 192)]], 5), WOOD, bevel=2, flat=True))


def obj_governance(img):
    """政令图层：朱红官印居中(旧铜印钮、骨白印文框)，外面两道黑铁环由近及远——政令所及。"""
    for r, w in ((116, 12), (90, 10)):
        over(img, shade(sub(ellipse([128 - r, 128 - r, 128 + r, 128 + r]), ellipse([128 - r + w, 128 - r + w, 128 + r - w, 128 + r - w])),
                        IRON if r > 100 else STEEL, bevel=6, gloss=0.7, grain=0.2))
    over(img, shade(union(ellipse([106, 54, 150, 92]), rrect([112, 80, 144, 104], 6)), BRASS, bevel=10, gloss=0.9))
    over(img, shade(rrect([76, 98, 180, 186], 8), RED, bevel=12, gloss=0.6, grain=0.3))
    over(img, shade(sub(rrect([90, 112, 166, 172], 4), rrect([100, 122, 156, 162], 3)), BONE, bevel=3))
    over(img, shade(rrect([122, 128, 134, 158], 2), BONE, bevel=2))


GODPOWERS = {
    "terrain_relief": (obj_relief, None),
    "edification_layer": (obj_edification, None),
    "governance_layer": (obj_governance, None),
    "zhou_conference": (obj_banners, "plus"),
    "zhou_bestow": (obj_ding, "up"),
    "zhou_advance": (obj_calendar, "arrow"),
    "dissolve_city": (obj_city, "cross"),
    "empire_layer": (obj_title, "crown"),
    "create_empire": (obj_crown, "plus"),
    "empire_form": (obj_crown, "up"),
    "remove_empire": (obj_crown, "cross"),
    "empire_list": (obj_list, None),
    "actor_create_kingdom": (obj_flag, "plus"),
    "territory_font": (obj_calligraphy, None),
    "city_view": (obj_city, None),
    "jimi_policy": (obj_bridle, None),
    "wealth_layer": (obj_coins, None),
    "force_vassal": (obj_chain, None),
    "release_vassal": (obj_broken_chain, None),
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
    # 天下(帝国核心之上，见 TianxiaSystem)：并入、退出联合天下
    "iconToolTianxiaJoin": (obj_ding, "arrow"),
    "iconToolTianxiaLeave": (obj_ding, "minus"),
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


# 传国玉玺(装备，见 Scripts/GeneralSystems/ImperialSealSystem.cs)：青玉方印、盘龙钮、金镶一角、朱红绶带
SEAL_JADE = ((186, 214, 188), (92, 138, 106), (20, 42, 28))


def obj_imperial_seal(img):
    # 立体方印：顶面、正面，金镶一角
    over(img, shade(poly([(58, 132), (198, 132), (176, 108), (80, 108)]), SEAL_JADE, bevel=6, gloss=1.0, grain=0.1))
    over(img, shade(rrect([58, 130, 198, 214], 8), SEAL_JADE, bevel=14, gloss=0.8, grain=0.12))
    over(img, shade(poly([(58, 130), (112, 130), (58, 184)]), GOLD, bevel=6, gloss=1.0))
    over(img, shade(rrect([58, 202, 198, 224], 6), RED, bevel=6))
    # 盘龙钮：一团盘曲的龙身，龙头居中昂起
    coil = union(ellipse([70, 84, 186, 128]), ellipse([88, 70, 168, 106]))
    over(img, shade(coil, SEAL_JADE, bevel=12, gloss=1.0, grain=0.12))
    over(img, shade(lines([[(92, 106), (128, 92), (164, 106)]], 6), (96, 140, 108) and ((96, 140, 108), (52, 88, 64), (12, 30, 18)), bevel=2))
    over(img, shade(ellipse([108, 46, 148, 82]), SEAL_JADE, bevel=10, gloss=1.0))
    over(img, shade(lines([[(114, 52), (96, 30)], [(142, 52), (160, 30)]], 6), GOLD, bevel=2, gloss=1.0))
    over(img, shade(ellipse([116, 60, 124, 68]), RED, bevel=2, gloss=1.0))
    over(img, shade(ellipse([132, 60, 140, 68]), RED, bevel=2, gloss=1.0))
    # 朱红绶带
    over(img, shade(lines([[(168, 104), (214, 150), (220, 206)], [(168, 104), (230, 140), (240, 196)]], 8), RED, bevel=3))
    over(img, shade(ellipse([160, 96, 180, 116]), RED, bevel=4, gloss=0.8))


# 受命宝(没有传国玉玺的皇帝另刻)：铜金方印、伏虎钮
def obj_imperial_seal_lesser(img):
    over(img, shade(rrect([66, 144, 190, 214], 8), GOLD, bevel=12, gloss=0.9))
    over(img, shade(rrect([66, 204, 190, 222], 6), RED, bevel=5))
    tiger = union(ellipse([90, 104, 166, 150]), ellipse([148, 88, 186, 124]), rrect([96, 136, 114, 152], 5),
                  rrect([142, 136, 160, 152], 5), lines([[(94, 118), (70, 100)]], 10))
    over(img, shade(tiger, GOLD, bevel=10, gloss=1.0))
    over(img, shade(ellipse([166, 98, 174, 106]), LACQ, bevel=2, flat=True))


ITEMS = {"icon_imperial_seal": obj_imperial_seal, "icon_imperial_seal_lesser": obj_imperial_seal_lesser}


# 古代理念徽章(见 Scripts/GeneralSystems/PartySystem.cs 的诸子百家与立场)：旧铜环、各家底色、中间一件器物
NAVY = ((84, 98, 128), (30, 40, 64), (6, 8, 18))
OCHRE = ((190, 150, 92), (120, 84, 40), (34, 22, 8))
VIOLET = ((132, 102, 140), (66, 44, 78), (14, 8, 20))
FOREST = ((96, 128, 96), (36, 60, 40), (8, 18, 10))
SLATE = ((128, 134, 142), (56, 62, 70), (10, 12, 16))
TEAL = ((92, 132, 132), (32, 64, 66), (6, 16, 18))
GILT = ((226, 200, 132), (150, 112, 48), (44, 30, 8))
AMBER = ((150, 104, 34), (86, 52, 8), (22, 10, 0))


def obj_wheat(img):
    """农家：一支大麦穗，麦粒左右交错，芒向上。"""
    over(img, shade(lines([[(128, 222), (128, 40)]], 8), BRASS, bevel=2))
    for k in range(6):
        y = 52 + k * 24
        over(img, shade(ellipse([96, y, 128, y + 26]), GILT, bevel=6, gloss=1.0))
        over(img, shade(ellipse([128, y + 10, 160, y + 36]), GILT, bevel=6, gloss=1.0))
    over(img, shade(lines([[(112, 54), (96, 24)], [(144, 64), (162, 32)], [(128, 42), (128, 14)]], 4), GILT, bevel=1))


def obj_gourd(img):
    """黄老：系着红绳的葫芦。"""
    over(img, shade(union(ellipse([92, 112, 176, 200]), ellipse([108, 62, 160, 120])), OCHRE, bevel=14, gloss=0.9))
    over(img, shade(rrect([122, 44, 140, 66], 4), WOOD, bevel=4))
    over(img, shade(lines([[(114, 116), (154, 116)]], 8), RED, bevel=3))
    over(img, shade(lines([[(150, 118), (170, 150)]], 6), RED, bevel=2))


def obj_compass(img):
    """墨家：规与矩。"""
    over(img, shade(poly([(60, 70), (84, 70), (84, 178), (196, 178), (196, 202), (60, 202)]), BONE, bevel=6))
    over(img, shade(lines([[(150, 50), (112, 196)], [(150, 50), (192, 196)]], 10), BRASS, bevel=4, gloss=0.9))
    over(img, shade(ellipse([140, 40, 160, 60]), BRASS, bevel=4, gloss=1.0))


def obj_taiji(img):
    """阴阳：太极。"""
    over(img, shade(ellipse([52, 52, 204, 204]), BONE, bevel=10, gloss=0.6))
    dark = union(of_half_disc(), ellipse([90, 128, 166, 204]))
    over(img, shade(dark, LACQ, bevel=4, flat=True))
    over(img, shade(ellipse([90, 52, 166, 128]), BONE, bevel=2, flat=True))
    over(img, shade(ellipse([118, 80, 138, 100]), LACQ, bevel=2, flat=True))
    over(img, shade(ellipse([118, 156, 138, 176]), BONE, bevel=2, flat=True))


def of_half_disc():
    m = of.blank(); d = ImageDraw.Draw(m)
    d.pieslice([52, 52, 204, 204], 90, 270, fill=255)
    return m


def obj_whisk(img):
    """宦官：拂尘。"""
    over(img, shade(lines([[(78, 200), (150, 110)]], 12), LACQ, bevel=4, gloss=0.8))
    over(img, shade(rrect([140, 96, 162, 118], 6), BRASS, bevel=4, gloss=1.0))
    tail = [[(152, 108), (150 + dx, 40 + abs(dx) // 2)] for dx in range(-36, 40, 8)]
    over(img, shade(lines(tail, 7), BONE, bevel=3, gloss=0.5))


MOSS = ((128, 136, 82), (60, 68, 30), (14, 16, 4))


def obj_totem(img):
    """泛灵论：木刻图腾柱，上有展翅，下叠两张兽面。"""
    over(img, shade(poly([(28, 84), (128, 44), (228, 84), (128, 104)]), BONE, bevel=6, gloss=0.6))
    over(img, shade(rrect([92, 76, 164, 236], 10), WOOD, bevel=10, gloss=0.5, grain=0.2))
    for y in (100, 168):
        over(img, shade(rrect([78, y, 178, y + 58], 16), GILT, bevel=8, gloss=0.8))
        over(img, shade(ellipse([94, y + 14, 118, y + 32]), LACQ, bevel=2, flat=True))
        over(img, shade(ellipse([138, y + 14, 162, y + 32]), LACQ, bevel=2, flat=True))
        over(img, shade(rrect([110, y + 40, 146, y + 50], 4), RED, bevel=2))


def make_ideology(obj, field):
    img = canvas()
    over(img, ef.shade(ef.ring(124, 106), ef.GOLD, bevel=9))
    over(img, ef.shade(ef.disc(105), field, bevel=30, gloss=0.25))
    layer = canvas()
    obj(layer)
    img = over(img, inset(layer, 0.72))
    return pixelize(img)


ANCIENT_IDEOLOGIES = {
    "Confucianism": (obj_bamboo, NAVY), "Legalism": (obj_edict, SLATE), "HuangLao": (obj_gourd, FOREST),
    "Mohism": (obj_compass, LACQ), "Agrarianism": (obj_wheat, WOOD), "Strategists": (obj_swords, RED),
    "Diplomatists": (obj_envoy, VIOLET), "YinYang": (obj_taiji, STEEL), "ImperialPower": (obj_crown, AMBER),
    "GreatClans": (obj_tablet, TEAL), "ConsortKin": (obj_phoenix, RED), "Eunuchs": (obj_whisk, VIOLET),
    "Animism": (obj_totem, MOSS), "RoyalKin": (obj_imperial_seal_lesser, AMBER),
    # 军功(将领与武职)：铁兜鍪、血红盔缨，赭黄底
    "MilitaryMerit": (lambda img: obj_helmet(img, STEEL, RED), OCHRE),
}


def obj_park(img):
    """绿地：一圈骨白石子小径围着草坪(暗翠)，坪上一株圆冠大树，路边一盏旧铜石灯。"""
    over(img, shade(sub(ellipse([18, 128, 238, 238]), ellipse([40, 144, 216, 222])), BONE, bevel=5, grain=0.3))
    over(img, shade(ellipse([40, 144, 216, 222]), JADE, bevel=8, grain=0.5))
    over(img, shade(rrect([118, 120, 138, 196], 4), WOOD, bevel=5, grain=0.3))
    over(img, shade(union(ellipse([60, 34, 160, 128]), ellipse([108, 22, 200, 118]), ellipse([82, 70, 186, 150])), JADE,
                    bevel=16, gloss=0.6, grain=0.35))
    over(img, shade(rrect([206, 160, 222, 214], 3), BRASS, bevel=4, gloss=0.8))
    over(img, shade(rrect([200, 148, 228, 166], 3), BRASS, bevel=4, gloss=0.9))


def obj_street(img):
    """街道：一段透视的石板大道(枪灰)，旧铜路肩，中线一道道骨白虚线。"""
    over(img, shade(poly([(98, 22), (158, 22), (238, 236), (18, 236)]), STEEL, bevel=10, grain=0.35))
    over(img, shade(lines([[(98, 22), (18, 236)], [(158, 22), (238, 236)]], 9), BRASS, bevel=3, gloss=0.7))
    for y0, y1, w in ((36, 64, 6), (90, 128, 8), (156, 214, 11)):
        over(img, shade(lines([[(128, y0), (128, y1)]], w), BONE, bevel=2, flat=True))


def obj_plaza(img):
    """广场：俯视斜放的一方石板广场(骨白，枪灰石缝)，正中一座旧铜方尖碑。"""
    over(img, shade(poly([(128, 92), (236, 156), (128, 220), (20, 156)]), BONE, bevel=10, grain=0.3))
    seams = lines([[(56, 135), (164, 199)], [(92, 113), (200, 177)], [(164, 113), (56, 177)], [(200, 135), (92, 199)]], 4)
    over(img, shade(seams, STEEL, bevel=1, flat=True))
    over(img, shade(poly([(112, 166), (144, 166), (136, 50), (120, 50)]), BRASS, bevel=8, gloss=0.9))
    over(img, shade(poly([(120, 50), (136, 50), (128, 26)]), GOLD, bevel=4, gloss=1.0))
    over(img, shade(rrect([102, 162, 154, 178], 3), STEEL, bevel=4))


def obj_wall(img):
    """城墙：一段枪灰城墙，上有一排雉堞，墙身砖缝，正中拱门(黑漆)。"""
    over(img, shade(rrect([16, 108, 240, 218], 4), STEEL, bevel=12, grain=0.4))
    for x in range(16, 236, 32):
        over(img, shade(rrect([x, 80, x + 20, 110], 2), STEEL, bevel=5, grain=0.3))
    seams = [[(16, 144), (240, 144)], [(16, 180), (240, 180)]]
    seams += [[(x, 110), (x, 144)] for x in range(48, 240, 48)] + [[(x, 144), (x, 180)] for x in range(24, 240, 48)]
    seams += [[(x, 180), (x, 218)] for x in range(48, 240, 48)]
    over(img, shade(lines(seams, 3), IRON, bevel=1, flat=True))
    over(img, shade(union(rrect([104, 166, 152, 218], 4), ellipse([104, 144, 152, 190])), LACQ, bevel=6))


# 通路之约(决议)：大道加一个加号
PLOTS.update({
    "empirecraft_road_treaty": (obj_street, "plus"),
})
# 军令(列国征伐页)：军旗加箭头
GODPOWERS.update({
    "corps_command": (obj_banners, "arrow"),
})
# 天下图层：鼎
GODPOWERS.update({
    "tianxia_layer": (obj_ding, None),
})
# 营建(城乡营建组)：立村、设镇、毁弃、改隶、迁城
GODPOWERS.update({
    "place_village": (obj_houses, "plus"),
    "place_town": (obj_market, "plus"),
    "raze_settlement": (obj_houses, "cross"),
    "assign_settlement": (obj_houses, "arrow"),
    "place_fort": (obj_wall, "plus"),
    "place_city": (obj_city, "plus"),
    "relocate_city": (obj_city, "arrow"),
    # 城貌：城池加互换角标(换成别的文化风格)
    "restyle_city": (obj_city, "swap"),
    "build_palace": (obj_palace, "up"),
    # 小、中、大厂区
    "factory_small": (obj_factory, "plus"),
    "factory_medium": (obj_factory, "up"),
    "factory_large": (obj_factory, "star"),
})


# 工厂区里的八类厂(见 FactorySystem)：输出到 GameResources/ui/icons/factories/<门类>.png
def obj_powder_barrel(img):
    """火药厂：一只箍铁的火药桶，引信上一点火星。"""
    over(img, shade(rrect([70, 70, 186, 214], 34), OCHRE, bevel=10, grain=0.4))
    for y in (98, 178):
        over(img, shade(rrect([64, y, 192, y + 14], 5), IRON, bevel=4, gloss=0.6))
    over(img, shade(lines([[(128, 70), (140, 44), (168, 30)]], 6), BRASS, bevel=2, flat=True))
    over(img, shade(ellipse([160, 14, 196, 50]), AMBER, bevel=6, gloss=1.0))


def obj_furnace(img):
    """钢铁厂：梯形高炉，炉口通红，铁水淌进一块金色铁锭。"""
    over(img, shade(poly([(64, 222), (88, 58), (152, 58), (176, 222)]), STEEL, bevel=10, grain=0.3))
    over(img, shade(ellipse([92, 40, 148, 76]), AMBER, bevel=6, gloss=1.0))
    over(img, shade(rrect([96, 120, 144, 132], 3), IRON, bevel=3))
    over(img, shade(lines([[(176, 150), (206, 176), (210, 196)]], 10), GILT, bevel=3, gloss=1.0))
    over(img, shade(rrect([170, 196, 238, 226], 5), GILT, bevel=6, gloss=0.9))


def obj_loom(img):
    """轻工厂：一卷红布绕在铜齿轮前。"""
    gear = sub(ellipse([60, 40, 196, 176]), ellipse([104, 84, 152, 132]))
    for k in range(10):
        a = k * math.pi / 5
        cx, cy = 128 + 70 * math.cos(a), 108 + 70 * math.sin(a)
        gear = union(gear, rrect([cx - 12, cy - 12, cx + 12, cy + 12], 3))
    over(img, shade(sub(gear, ellipse([104, 84, 152, 132])), BRASS, bevel=8, gloss=0.9))
    over(img, shade(rrect([40, 170, 216, 220], 22), RED, bevel=10, gloss=0.6))
    over(img, shade(ellipse([186, 170, 236, 220]), RED, bevel=8, gloss=0.8))


def obj_sailboat(img):
    """轻型船厂：木船体、一面帆。"""
    over(img, shade(rrect([122, 30, 134, 176], 3), WOOD, bevel=3))
    over(img, shade(poly([(138, 40), (138, 160), (214, 160)]), BONE, bevel=8, gloss=0.5))
    over(img, shade(poly([(118, 60), (118, 160), (60, 160)]), BONE, bevel=6, gloss=0.5))
    over(img, shade(poly([(28, 172), (228, 172), (196, 222), (60, 222)]), OCHRE, bevel=10, grain=0.4))


def obj_heavy_works(img):
    """重工厂：铁齿轮前一把大铁锤。"""
    gear = sub(ellipse([96, 24, 236, 164]), ellipse([140, 68, 192, 120]))
    for k in range(8):
        a = k * math.pi / 4
        cx, cy = 166 + 72 * math.cos(a), 94 + 72 * math.sin(a)
        gear = union(gear, rrect([cx - 13, cy - 13, cx + 13, cy + 13], 3))
    over(img, shade(sub(gear, ellipse([140, 68, 192, 120])), IRON, bevel=8, gloss=0.6))
    over(img, shade(rotate(rrect([52, 100, 72, 236], 6), -35, 62, 168), WOOD, bevel=5))
    over(img, shade(rotate(rrect([20, 92, 124, 136], 8), -35, 72, 114), STEEL, bevel=10, gloss=0.9))


def obj_steamship(img):
    """重型船厂：钢壳轮船、白色上层建筑、一根红烟囱。"""
    over(img, shade(rrect([150, 50, 182, 120], 4), RED, bevel=6, gloss=0.7))
    over(img, shade(rrect([150, 44, 182, 60], 3), LACQ, bevel=3))
    over(img, shade(rrect([84, 104, 206, 150], 6), BONE, bevel=8, gloss=0.5))
    over(img, shade(poly([(20, 146), (236, 146), (210, 218), (52, 218)]), STEEL, bevel=12, gloss=0.6))
    for x in (100, 136, 172):
        over(img, shade(ellipse([x, 118, x + 14, 132]), LACQ, bevel=2))


def obj_biplane(img):
    """轻型飞机厂：一架双翼机(侧面)。"""
    over(img, shade(rrect([24, 64, 232, 88], 10), OCHRE, bevel=6, gloss=0.6))
    over(img, shade(rrect([40, 150, 216, 172], 10), OCHRE, bevel=6, gloss=0.6))
    for x in (64, 192):
        over(img, shade(rrect([x, 84, x + 8, 154], 2), WOOD, bevel=2))
    over(img, shade(rrect([40, 100, 214, 140], 20), STEEL, bevel=8, gloss=0.7))
    over(img, shade(rrect([214, 84, 226, 156], 4), IRON, bevel=3))
    over(img, shade(poly([(40, 100), (16, 74), (28, 130)]), STEEL, bevel=5))


def obj_bomber(img):
    """重型飞机厂：一架四发轰炸机(俯视)。"""
    over(img, shade(poly([(16, 128), (240, 128), (240, 150), (16, 150)]), STEEL, bevel=6, gloss=0.6))
    for x in (40, 84, 164, 208):
        over(img, shade(ellipse([x - 12, 110, x + 12, 152]), IRON, bevel=5, gloss=0.7))
    over(img, shade(rrect([112, 24, 144, 232], 16), STEEL, bevel=10, gloss=0.8))
    over(img, shade(rrect([76, 204, 180, 222], 6), STEEL, bevel=5, gloss=0.6))
    over(img, shade(ellipse([116, 28, 140, 52]), NAVY, bevel=4, gloss=1.0))


FACTORY_TYPES = {
    "gunpowder_mill": obj_powder_barrel,
    "steel_mill": obj_furnace,
    "light_factory": obj_loom,
    "light_shipyard": obj_sailboat,
    "heavy_factory": obj_heavy_works,
    "heavy_shipyard": obj_steamship,
    "light_aircraft_factory": obj_biplane,
    "heavy_aircraft_factory": obj_bomber,
}


# ═════════════ 生成 ═════════════
# 人口、矿藏笔刷(这几个对象定义在 GODPOWERS 之后，这里补进去)
GODPOWERS.update({
    "population_add": (obj_people, "plus"),
    "population_remove": (obj_people, "minus"),
    "deposit_clear": (obj_res_copper, "cross"),
    # 名臣名将窗口按钮
    "famous_talents": (obj_swords, "crown"),
    "bestow_famous_general": (obj_sword, "up"),
    "bestow_famous_minister": (obj_brush, "up"),
    # 易俗范围开关：举国易俗(王冠 + 笔)、举朝易俗(帝国法理 + 笔)；恢复法理初始文化(法理 + 返回)
    "realm_culture_kingdom": (lambda i: (obj_crown(i), obj_brush(i)), "swap"),
    "realm_culture_empire": (lambda i: (obj_title(i), obj_brush(i)), "swap"),
    "restore_title_culture": (obj_title, "back"),
})


def b_lens(img):
    """放大镜角标：骨白镜框与斜柄，镜片枪灰高光。"""
    cx, cy = BX - 8, BY - 8
    frame = union(sub(ellipse([cx - 32, cy - 32, cx + 32, cy + 32]), ellipse([cx - 20, cy - 20, cx + 20, cy + 20])),
                  lines([[(cx + 20, cy + 20), (BX + 44, BY + 44)]], 16))
    _badge(img, frame, BONE)
    over(img, shade(ellipse([cx - 20, cy - 20, cx + 20, cy + 20]), STEEL, bevel=6, gloss=1.0))


BADGES["lens"] = b_lens


def obj_battle_sound(img):
    """战声开关：交叉双剑偏左，右侧两道由近及远的声波弧(旧铜、枪灰；弧间留足空隙，缩到 28 像素还分得开)。"""
    swords = canvas()
    of.crossed_swords(swords)
    over(img, inset(swords, 0.92, 84, 134))
    cx, cy = 120, 128
    wedge = poly([(cx, cy), (cx + 170, cy - 170), (cx + 170, cy + 170)])
    for k, (r, w) in enumerate(((66, 24), (126, 24))):
        ring = sub(ellipse([cx - r, cy - r, cx + r, cy + r]), ellipse([cx - r + w, cy - r + w, cx + r - w, cy + r - w]))
        arc = Image.fromarray(np.minimum(np.asarray(ring), np.asarray(wedge)))
        over(img, shade(arc, STEEL if k else BRASS, bevel=7, gloss=0.9 - 0.2 * k, grain=0.15))


GODPOWERS["battle_sound"] = (obj_battle_sound, None)


def obj_civ_music(img):
    """文明乐开关：一口生了铜绿的古钟(旧铜钟钮与口沿、钟身三排枚)，右上一个骨白的八分音符。"""
    over(img, shade(rrect([68, 22, 116, 40], 8), BRASS, bevel=6, gloss=0.8))                    # 钟钮
    over(img, shade(rrect([84, 30, 100, 56], 4), BRASS, bevel=4))
    bell = poly([(48, 52), (136, 52), (168, 206), (92, 190), (16, 206)])
    over(img, shade(bell, JADE, bevel=18, gloss=0.6, grain=0.3))                                # 钟身(铜绿)
    over(img, shade(poly([(16, 200), (92, 184), (168, 200), (168, 214), (92, 198), (16, 214)]), BRASS, bevel=5, gloss=0.8))
    for row, y in enumerate((80, 112, 144)):
        for k in range(3):
            x = 64 + k * 28 - row * 4 + (row * 4 if k == 1 else 0)
            over(img, shade(ellipse([x - 7, y - 7, x + 7, y + 7]), BRASS, bevel=4, gloss=0.9))  # 枚
    over(img, shade(ellipse([166, 132, 206, 162]), BONE, bevel=6, gloss=0.7))                  # 音符
    over(img, shade(rrect([194, 40, 208, 148], 4), BONE, bevel=4))
    over(img, shade(poly([(194, 40), (240, 66), (240, 92), (208, 70)]), BONE, bevel=4))


GODPOWERS["civ_music"] = (obj_civ_music, None)


def obj_road_paint(img):
    """绘制道路：左下一段缩小的透视石板路(同街道)，一支大毛笔从右上角往左下落笔，旧铜笔箍、黑漆笔尖点在路边。"""
    road = canvas()
    obj_street(road)
    over(img, inset(road, 0.74, 92, 156))
    over(img, shade(lines([[(240, 14), (186, 96)]], 36), WOOD, bevel=8, grain=0.35))             # 笔杆
    over(img, shade(lines([[(194, 86), (172, 118)]], 42), BRASS, bevel=4, gloss=0.9))             # 笔箍
    over(img, shade(poly([(152, 100), (198, 128), (170, 166), (140, 188), (144, 148)]), LACQ, bevel=7, gloss=0.5))  # 笔尖


def obj_road_connect(img):
    """连路：左下、右上两座枪灰小城(雉堞、黑漆城门)，中间一条宽石板路弯着连起来，路心一道道骨白虚线。"""
    path = [(70, 190), (110, 150), (150, 128), (186, 70)]
    over(img, shade(lines([path], 44), STEEL, bevel=9, grain=0.3))
    for a, b in ((path[0], path[1]), (path[1], path[2]), (path[2], path[3])):
        mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        dx, dy = (b[0] - a[0]) * 0.22, (b[1] - a[1]) * 0.22
        over(img, shade(lines([[(mx - dx, my - dy), (mx + dx, my + dy)]], 8), BONE, bevel=2, flat=True))
    for x, y in ((2, 138), (142, 2)):
        over(img, shade(rrect([x, y + 30, x + 112, y + 114], 5), STEEL, bevel=12, grain=0.4))
        for k in range(4):
            over(img, shade(rrect([x + k * 32, y + 6, x + k * 32 + 18, y + 34], 2), STEEL, bevel=5, grain=0.3))
        over(img, shade(rrect([x + 38, y + 66, x + 74, y + 114], 16), LACQ, bevel=5))


GODPOWERS.update({
    "road_paint": (obj_road_paint, None),
    "road_erase": (obj_street, "cross"),
    "road_connect": (obj_road_connect, None),
})
# 制度窗口左栏的两枚文化按钮：浏览别的文化(法典 + 放大镜)、改从文化(旗帜 + 互换)
GODPOWERS.update({
    "institution_browse_culture": (obj_book, "lens"),
    "institution_convert_culture": (obj_flag, "swap"),
})


def build():
    out = {}
    for name, obj in TABS.items():
        out[f"GameResources/{name}.png"] = make_tab(obj)
    for name, obj in CITYDEV.items():
        out[f"GameResources/ui/icons/citydev/{name}.png"] = make_tab(obj)
    for name, art in POWER_TABS.items():
        out[f"GameResources/{name}.png"] = make_power_tab(art)
    for name, rows in CHESS.items():
        out[f"GameResources/ui/icons/corps/chess_{name}.png"] = make_chess(rows)
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
    for name, obj in ITEMS.items():
        out[f"GameResources/ui/icons/items/{name}.png"] = make_plot(obj, None)
    for name, (obj, field) in ANCIENT_IDEOLOGIES.items():
        out[f"GameResources/ui/icons/actor_traits/ideology_{name}.png"] = make_ideology(obj, field)
    for name, obj in FACTORY_TYPES.items():
        out[f"GameResources/ui/icons/factories/{name}.png"] = make_plot(obj, None)
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
