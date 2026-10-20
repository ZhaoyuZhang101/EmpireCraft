"""各文化线古代学说的徽章(用户 2026-10-20："理念图标各文化一套")。

画法与华夏那套(imperial_forge.ANCIENT_IDEOLOGIES)相同：旧铜环 + 理念底色(同一格理念各文化同色) + 写实器物，
最后像素化成 28×28。器物只用通用意象(十字、新月、狼头、法轮、鸟居、羽蛇、图腾……)，不画具体人物与史名。

输出 GameResources/ui/icons/actor_traits/ideology_<理念>_<谱系或文化>.png；游戏里按派系所在文化取图，
缺图回退华夏那枚(见 Scripts/GameLibrary/IdeologyTraitIcons.Path)。

用法: python Tools/IconForge/doctrine_forge.py [--preview]   (只写本脚本的图，不动别的图标)
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import imperial_forge as F  # noqa: E402

shade, over, poly, ellipse, rrect, sub, rotate, lines, union = (
    F.shade, F.over, F.poly, F.ellipse, F.rrect, F.sub, F.rotate, F.lines, F.union)
BRASS, GOLD, STEEL, IRON, PARCH, WOOD, RED, LACQ, JADE, BONE, BLOOD = (
    F.BRASS, F.GOLD, F.STEEL, F.IRON, F.PARCH, F.WOOD, F.RED, F.LACQ, F.JADE, F.BONE, F.BLOOD)
GILT, AMBER, NAVY, SLATE, FOREST, VIOLET, TEAL, OCHRE, MOSS = (
    F.GILT, F.AMBER, F.NAVY, F.SLATE, F.FOREST, F.VIOLET, F.TEAL, F.OCHRE, F.MOSS)

# 同一格理念各文化同色(与华夏那套一致)
FIELD = {
    "Confucianism": NAVY, "Legalism": SLATE, "HuangLao": FOREST, "Mohism": LACQ, "Agrarianism": WOOD,
    "Strategists": RED, "Diplomatists": VIOLET, "YinYang": STEEL, "ImperialPower": AMBER, "GreatClans": TEAL,
    "ConsortKin": RED, "Eunuchs": VIOLET, "RoyalKin": AMBER, "MilitaryMerit": OCHRE,
}


def blank():
    return F.of.blank()


def pieslice(box, start, end):
    m = blank(); ImageDraw.Draw(m).pieslice(box, start, end, fill=255)
    return m


def chord(box, start, end):
    m = blank(); ImageDraw.Draw(m).chord(box, start, end, fill=255)
    return m


def arc(box, start, end, width):
    m = blank(); ImageDraw.Draw(m).arc(box, start, end, fill=255, width=width)
    return m


def ring(cx, cy, r_out, r_in):
    return sub(ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out]),
               ellipse([cx - r_in, cy - r_in, cx + r_in, cy + r_in]))


def disc(cx, cy, r):
    return ellipse([cx - r, cy - r, cx + r, cy + r])


def cross(cx, cy, w, h, arm_y, arm_w, t):
    """十字：竖杆宽 t、高 h，横杆在 arm_y、宽 arm_w。"""
    return union(rrect([cx - t // 2, cy - h // 2, cx + t // 2, cy + h // 2], 3),
                 rrect([cx - arm_w // 2, arm_y - t // 2, cx + arm_w // 2, arm_y + t // 2], 3))


def crescent(cx, cy, r, dx, dy, k=0.82):
    return sub(disc(cx, cy, r), disc(cx + dx, cy + dy, int(r * k)))


def star(cx, cy, r, points=8, inner=0.45, rot=0.0):
    pts = []
    for i in range(points * 2):
        rr = r if i % 2 == 0 else r * inner
        a = rot + i * math.pi / points - math.pi / 2
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return poly(pts)


def curve(points, width):
    return lines([points], width)


def bezier(p0, p1, p2, n=24):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in (i / n for i in range(n + 1))]


# ═════════════ 通用器物 ═════════════
def obj_book_emblem(img, cover, emblem):
    """合上的典籍：书脊、书口、包角，封面上一枚徽记。"""
    over(img, shade(rrect([58, 34, 206, 226], 10), cover, bevel=12, gloss=0.6, grain=0.2))
    over(img, shade(rrect([190, 44, 214, 216], 4), BONE, bevel=4, grain=0.3))
    over(img, shade(rrect([50, 34, 76, 226], 8), cover, bevel=8, gloss=0.8))
    for x, y in ((80, 40), (184, 40), (80, 206), (184, 206)):
        over(img, shade(rrect([x - 12, y - 6, x + 12, y + 14], 3), BRASS, bevel=3, gloss=0.9))
    emblem(img)


def obj_codex_cross(img):
    obj_book_emblem(img, RED, lambda i: over(i, shade(cross(132, 132, 0, 132, 106, 92, 26), GILT, bevel=8, gloss=1.0)))


def obj_book_crescent(img):
    def emblem(i):
        over(i, shade(crescent(126, 128, 50, 22, -10), GILT, bevel=8, gloss=1.0))
        over(i, shade(star(164, 104, 16, 5, 0.45), GILT, bevel=4, gloss=1.0))
    obj_book_emblem(img, FOREST, emblem)


def obj_anvil_hammer(img):
    """行会：铁砧与锤。"""
    anvil = union(poly([(40, 110), (196, 110), (196, 134), (160, 150), (96, 150), (64, 134), (24, 122)]),
                  rrect([104, 146, 152, 196], 4), rrect([72, 190, 184, 222], 6))
    over(img, shade(anvil, IRON, bevel=12, gloss=0.9, grain=0.2))
    over(img, shade(rotate(rrect([120, 20, 138, 120], 6), -35, 150, 70), WOOD, bevel=6, grain=0.3))
    over(img, shade(rotate(rrect([126, 26, 214, 62], 8), -35, 170, 44), STEEL, bevel=10, gloss=0.9))


def obj_alms_bowl(img):
    """托钵：木钵，上挂一枚小木十字。"""
    over(img, shade(lines([[(128, 26), (128, 112)]], 6), PARCH, bevel=2, flat=True))
    over(img, shade(cross(128, 70, 0, 70, 58, 46, 14), WOOD, bevel=5, gloss=0.6))
    over(img, shade(chord([30, 70, 226, 236], 0, 180), WOOD, bevel=16, gloss=0.6, grain=0.35))
    over(img, shade(ellipse([30, 132, 226, 172]), OCHRE, bevel=6, gloss=0.6))
    over(img, shade(ellipse([46, 140, 210, 166]), LACQ, bevel=3, flat=True))


def obj_sheaf(img):
    """公地：三支麦穗扎成一束，系红绳。"""
    for deg in (-28, 28, 0):
        layer = F.canvas()
        over(layer, shade(lines([[(128, 236), (128, 110)]], 8), OCHRE, bevel=2))
        for k in range(5):
            y = 22 + k * 20
            over(layer, shade(ellipse([104, y, 130, y + 28]), GILT, bevel=6, gloss=1.0))
            over(layer, shade(ellipse([126, y + 8, 152, y + 36]), GILT, bevel=6, gloss=1.0))
        rot = Image.fromarray(np.clip(layer, 0, 255).astype(np.uint8), "RGBA").rotate(deg, center=(128, 200),
                                                                                         resample=Image.BICUBIC)
        over(img, np.asarray(rot, np.float32))
    over(img, shade(rrect([104, 176, 152, 196], 6), RED, bevel=5, gloss=0.9))

def obj_kite_shield(img):
    """骑士：长矛斜贯一面鸢形盾。"""
    over(img, shade(lines([[(40, 230), (214, 26)]], 10), WOOD, bevel=4, grain=0.3))
    over(img, shade(poly([(214, 26), (198, 64), (224, 50)]), STEEL, bevel=4, gloss=1.0))
    shield = poly([(66, 52), (190, 52), (194, 120), (128, 232), (62, 120)])
    over(img, shade(shield, STEEL, bevel=14, gloss=0.9, grain=0.15))
    over(img, shade(poly([(84, 66), (172, 66), (176, 118), (128, 206), (80, 118)]), BONE, bevel=8, gloss=0.5))
    over(img, shade(union(rrect([118, 66, 138, 200], 2), rrect([80, 100, 176, 120], 2)), BRASS, bevel=4, gloss=0.9))


def obj_latin_cross(img):
    """教权：三级台座上的十字。"""
    over(img, shade(cross(128, 104, 0, 176, 74, 116, 30), GILT, bevel=10, gloss=1.0))
    for k, (w, y) in enumerate(((60, 192), (84, 208), (108, 224))):
        over(img, shade(rrect([128 - w, y - 8, 128 + w, y + 8], 3), STEEL, bevel=5, gloss=0.6))


def obj_west_crown(img):
    """王权：西式王冠，五个尖顶各缀一珠，箍上镶红石。"""
    pts = [(36, 196), (36, 108), (66, 150), (82, 70), (106, 140), (128, 50), (150, 140), (174, 70), (190, 150), (220, 108),
           (220, 196)]
    over(img, shade(poly(pts), GILT, bevel=12, gloss=1.0))
    for x, y in ((36, 104), (82, 66), (128, 46), (174, 66), (220, 104)):
        over(img, shade(disc(x, y, 12), BONE, bevel=4, gloss=1.0))
    over(img, shade(rrect([30, 180, 226, 214], 6), GILT, bevel=8, gloss=0.9))
    for x in (66, 128, 190):
        over(img, shade(ellipse([x - 13, 186, x + 13, 208]), RED, bevel=4, gloss=1.0))


def obj_escutcheon(img):
    """贵族世家：纹章盾，人字带与三圆章。"""
    shield = poly([(46, 40), (210, 40), (210, 120), (190, 180), (128, 230), (66, 180), (46, 120)])
    over(img, shade(shield, BONE, bevel=12, gloss=0.6, grain=0.15))
    over(img, shade(poly([(52, 170), (128, 96), (204, 170), (204, 200), (128, 126), (52, 200)]), RED, bevel=6, gloss=0.8))
    for x, y in ((90, 72), (166, 72), (128, 186)):
        over(img, shade(disc(x, y, 17), LACQ, bevel=5, gloss=0.8))
    over(img, shade(sub(shield, poly([(58, 52), (198, 52), (198, 118), (180, 172), (128, 214), (76, 172), (58, 118)])),
                    BRASS, bevel=4, gloss=0.9))


def obj_rings(img):
    """联姻：两只相扣的金环，一只镶红石。"""
    over(img, shade(ring(98, 142, 64, 44), GILT, bevel=8, gloss=1.0))
    over(img, shade(ring(158, 142, 64, 44), GILT, bevel=8, gloss=1.0))
    over(img, shade(sub(ring(98, 142, 64, 44), rrect([150, 70, 190, 130], 0)), GILT, bevel=8, gloss=1.0))
    over(img, shade(poly([(158, 52), (184, 76), (158, 100), (132, 76)]), RED, bevel=8, gloss=1.0))


def obj_orb(img):
    """王族：十字圣球。"""
    over(img, shade(cross(128, 56, 0, 72, 46, 54, 16), GILT, bevel=5, gloss=1.0))
    over(img, shade(disc(128, 152, 80), GILT, bevel=18, gloss=1.0))
    over(img, shade(rrect([48, 140, 208, 160], 4), BRASS, bevel=4))
    over(img, shade(rrect([118, 74, 138, 230], 4), BRASS, bevel=4))
    over(img, shade(disc(128, 150, 12), RED, bevel=4, gloss=1.0))


def obj_great_helm(img):
    """武勋：桶形大盔，横目缝与气孔。"""
    helm = union(chord([60, 26, 196, 120], 180, 360), rrect([60, 72, 196, 226], 10))
    over(img, shade(helm, STEEL, bevel=16, gloss=0.9, grain=0.15))
    over(img, shade(rrect([70, 104, 186, 118], 3), LACQ, bevel=2, flat=True))
    over(img, shade(rrect([120, 30, 136, 222], 3), BRASS, bevel=4, gloss=0.9))
    for y in (150, 170, 190):
        for x in (88, 104, 152, 168):
            over(img, shade(disc(x, y, 4), LACQ, bevel=1, flat=True))


def obj_decree(img):
    """王法：卷轴诏令，坠一枚红蜡封。"""
    over(img, shade(rrect([62, 44, 194, 196], 4), PARCH, bevel=8, grain=0.35))
    for y in (76, 100, 124, 148):
        over(img, shade(rrect([82, y, 174, y + 6], 2), LACQ, bevel=1, flat=True))
    over(img, shade(rrect([46, 26, 210, 52], 12), WOOD, bevel=8, gloss=0.8))
    over(img, shade(rrect([46, 188, 210, 214], 12), WOOD, bevel=8, gloss=0.8))
    over(img, shade(lines([[(128, 210), (128, 226)]], 6), RED, bevel=2))
    over(img, shade(disc(128, 222, 22), RED, bevel=8, gloss=1.0))


def obj_rosary(img):
    """苏非：一串念珠，下垂流苏。"""
    for k in range(18):
        a = k * 2 * math.pi / 18
        x, y = 128 + 82 * math.cos(a), 112 + 82 * math.sin(a)
        if 70 < math.degrees(a) < 110:
            continue
        over(img, shade(disc(x, y, 14), WOOD, bevel=6, gloss=1.0))
    over(img, shade(disc(128, 196, 16), BRASS, bevel=6, gloss=1.0))
    over(img, shade(poly([(116, 206), (140, 206), (152, 244), (104, 244)]), RED, bevel=6, grain=0.3))


def obj_palm(img):
    """农社：椰枣树，树下垂两串果。"""
    for k in range(9):
        over(img, shade(rrect([114 + (k % 2) * 2, 92 + k * 16, 142 + (k % 2) * 2, 110 + k * 16], 5), OCHRE, bevel=5,
                        grain=0.3))
    for (ex, ey), (cx, cy) in (((24, 116), (70, 50)), ((232, 116), (186, 50)), ((40, 66), (90, 26)), ((216, 66), (166, 26)),
                               ((128, 14), (128, 40)), ((58, 150), (84, 84)), ((198, 150), (172, 84))):
        over(img, shade(curve(bezier((128, 90), (cx, cy), (ex, ey)), 22), MOSS, bevel=6, gloss=1.0))
    for x in (100, 156):
        for dy in (0, 16):
            over(img, shade(disc(x, 112 + dy, 9), RED, bevel=3, gloss=1.0))
            over(img, shade(disc(x + 10, 120 + dy, 9), RED, bevel=3, gloss=1.0))


def blade_curved(cx, cy, length, width, bend, deg):
    """弯刀刃：两条弧之间的月牙形，竖直画好再转。"""
    top = cy - length // 2
    m = sub(ellipse([cx - bend, top, cx + width + bend, top + length * 2]),
            ellipse([cx - bend + width, top + 14, cx + width + bend + 14, top + length * 2 + 6]))
    m = sub(m, rrect([0, cy + length // 2, 256, 256], 0))
    return rotate(m, deg, cx, cy)


def obj_scimitars(img):
    """圣战：两把交叉的弯刀。"""
    for sign in (1, -1):
        def X(x):
            return 128 + (x - 128) * sign
        over(img, shade(curve(bezier((X(86), 182), (X(96), 70), (X(214), 26)), 22), STEEL, bevel=6, gloss=1.0))
        over(img, shade(lines([[(X(86), 182), (X(56), 230)]], 16), WOOD, bevel=4))
        over(img, shade(rotate(rrect([X(86) - 26, 176, X(86) + 26, 190], 5), -30 * sign, X(86), 183), GILT, bevel=4,
                        gloss=1.0))
        over(img, shade(disc(X(54), 232, 10), GILT, bevel=3, gloss=1.0))

def obj_camel(img):
    """商旅：骆驼剪影。"""
    body = union(ellipse([60, 100, 200, 168]), ellipse([96, 62, 156, 124]),
                 rotate(rrect([176, 44, 204, 140], 12), -20, 190, 100), ellipse([180, 28, 232, 62]))
    legs = union(*[rrect([x, 150, x + 16, 228], 5) for x in (74, 96, 160, 182)])
    over(img, shade(legs, OCHRE, bevel=5, grain=0.2))
    over(img, shade(body, OCHRE, bevel=14, gloss=0.8, grain=0.25))
    over(img, shade(lines([[(62, 120), (36, 170)]], 7), OCHRE, bevel=2))
    over(img, shade(rrect([96, 92, 162, 108], 4), RED, bevel=4))
    over(img, shade(disc(218, 40, 4), LACQ, bevel=1, flat=True))


def obj_astrolabe(img):
    """星象：星盘。"""
    over(img, shade(ring(128, 132, 98, 78), GILT, bevel=8, gloss=1.0))
    over(img, shade(disc(128, 132, 78), NAVY, bevel=6, flat=True))
    over(img, shade(star(128, 132, 70, 8, 0.36), GILT, bevel=6, gloss=1.0))
    over(img, shade(ring(128, 132, 40, 32), BRASS, bevel=3, gloss=0.8))
    over(img, shade(rrect([116, 18, 140, 40], 8), GILT, bevel=4, gloss=1.0))
    over(img, shade(ring(128, 22, 16, 8), GILT, bevel=4, gloss=1.0))
    over(img, shade(disc(128, 132, 9), RED, bevel=3, gloss=1.0))


def obj_turban(img):
    """君权：洋葱顶缠头巾冠，额前宝石插一支直立翎羽。"""
    over(img, shade(ellipse([116, 4, 144, 92]), BONE, bevel=6, gloss=0.8))
    over(img, shade(ellipse([116, 4, 144, 30]), LACQ, bevel=2, flat=True))
    dome = union(ellipse([40, 110, 216, 222]), poly([(128, 40), (196, 130), (60, 130)]), ellipse([70, 70, 186, 170]))
    over(img, shade(dome, BONE, bevel=18, gloss=0.6, grain=0.2))
    for k, y in enumerate((126, 152, 178, 204)):
        over(img, shade(lines([[(44 + k * 4, y + 16), (212 - k * 4, y - 16)]], 8), PARCH, bevel=2, flat=True))
    over(img, shade(poly([(128, 84), (146, 104), (128, 124), (110, 104)]), RED, bevel=6, gloss=1.0))
    over(img, shade(sub(poly([(128, 76), (154, 104), (128, 132), (102, 104)]),
                        poly([(128, 84), (146, 104), (128, 124), (110, 104)])), GILT, bevel=3, gloss=1.0))

def obj_lattice(img):
    """后宫：尖拱花窗，窗格交错。"""
    arch = union(rrect([56, 110, 200, 230], 2), pieslice([56, 28, 200, 192], 180, 360))
    over(img, shade(arch, GILT, bevel=8, gloss=0.9))
    inner = union(rrect([74, 116, 182, 216], 0), pieslice([74, 46, 182, 186], 180, 360))
    over(img, shade(inner, LACQ, bevel=3, flat=True))
    grid = union(*([lines([[(x, 30), (x + 160, 230)]], 6) for x in range(-60, 200, 30)] +
                   [lines([[(x, 30), (x - 160, 230)]], 6) for x in range(60, 330, 30)]))
    over(img, shade(Image.fromarray(np.minimum(np.asarray(grid), np.asarray(inner))), BONE, bevel=2, gloss=0.6))


def obj_khanjar(img):
    """王族：嵌宝弯匕首。"""
    over(img, shade(curve(bezier((128, 120), (124, 206), (186, 236)), 28), STEEL, bevel=7, gloss=1.0))
    over(img, shade(rrect([110, 36, 146, 116], 12), GILT, bevel=8, gloss=1.0))
    over(img, shade(disc(128, 32, 22), GILT, bevel=6, gloss=1.0))
    over(img, shade(rrect([78, 106, 178, 128], 8), GILT, bevel=6, gloss=1.0))
    for x, y in ((128, 32), (128, 76), (92, 117), (164, 117)):
        over(img, shade(disc(x, y, 9), RED, bevel=3, gloss=1.0))

def obj_spiked_helm(img):
    """军功：尖顶盔，护鼻与锁子护颈。"""
    over(img, shade(rrect([62, 140, 194, 224], 18), IRON, bevel=8, grain=0.5))
    over(img, shade(union(poly([(128, 16), (138, 60), (118, 60)]), chord([56, 50, 200, 230], 180, 360)), STEEL, bevel=16,
                    gloss=1.0))
    over(img, shade(rrect([52, 128, 204, 148], 6), BRASS, bevel=5, gloss=0.9))
    over(img, shade(rrect([120, 140, 136, 204], 4), STEEL, bevel=4, gloss=0.9))


def obj_dharma_wheel(img):
    """正法：八辐法轮。"""
    for k in range(8):
        a = k * math.pi / 4
        x, y = 128 + 98 * math.cos(a), 128 + 98 * math.sin(a)
        over(img, shade(disc(x, y, 12), GILT, bevel=4, gloss=1.0))
        over(img, shade(lines([[(128, 128), (128 + 80 * math.cos(a), 128 + 80 * math.sin(a))]], 12), GILT, bevel=4,
                        gloss=0.9))
    over(img, shade(ring(128, 128, 92, 74), GILT, bevel=8, gloss=1.0))
    over(img, shade(disc(128, 128, 26), GILT, bevel=8, gloss=1.0))
    over(img, shade(disc(128, 128, 10), RED, bevel=3, gloss=1.0))


def obj_rod(img):
    """利论：刑赏之杖，杖头莲苞，杖身三道金箍。"""
    over(img, shade(rrect([116, 70, 140, 236], 8), WOOD, bevel=6, grain=0.3))
    for y in (110, 160, 210):
        over(img, shade(rrect([108, y, 148, y + 12], 4), GILT, bevel=4, gloss=1.0))
    bud = union(ellipse([92, 22, 164, 84]), poly([(128, 8), (146, 40), (110, 40)]))
    over(img, shade(bud, GILT, bevel=10, gloss=1.0))
    over(img, shade(ellipse([116, 40, 140, 64]), RED, bevel=4, gloss=1.0))


def obj_lotus(img):
    """沙门：莲花。"""
    for deg in (-60, -30, 30, 60, 0):
        petal = rotate(ellipse([104, 40, 152, 170]), deg, 128, 170)
        over(img, shade(petal, BONE, bevel=10, gloss=0.8))
    over(img, shade(ellipse([96, 60, 160, 168]), BONE, bevel=10, gloss=0.9))
    over(img, shade(chord([30, 150, 226, 230], 0, 180), FOREST, bevel=8, gloss=0.6))
    over(img, shade(ellipse([116, 120, 140, 150]), GILT, bevel=4, gloss=1.0))


def obj_kalash(img):
    """村社：水罐，口插芒叶。"""
    for deg in (-50, -25, 0, 25, 50):
        over(img, shade(rotate(ellipse([118, 14, 138, 84]), deg, 128, 84), FOREST, bevel=5, gloss=0.7))
    over(img, shade(disc(128, 70, 24), OCHRE, bevel=8, gloss=0.8))
    over(img, shade(rrect([100, 90, 156, 112], 6), BRASS, bevel=4, gloss=0.9))
    over(img, shade(ellipse([50, 100, 206, 236]), BRASS, bevel=18, gloss=1.0))
    over(img, shade(rrect([60, 150, 196, 162], 4), RED, bevel=3))


def obj_elephant(img):
    """尚武：战象正面，大耳、长鼻、象牙，额上金饰。"""
    over(img, shade(ellipse([18, 60, 106, 190]), IRON, bevel=10, gloss=0.6, grain=0.2))
    over(img, shade(ellipse([150, 60, 238, 190]), IRON, bevel=10, gloss=0.6, grain=0.2))
    over(img, shade(ellipse([70, 40, 186, 170]), IRON, bevel=14, gloss=0.8, grain=0.2))
    over(img, shade(curve(bezier((128, 140), (124, 220), (160, 236)), 30), IRON, bevel=8, gloss=0.8))
    for x0, x1 in ((96, 70), (160, 186)):
        over(img, shade(curve(bezier((x0, 150), ((x0 + x1) / 2, 196), (x1, 200)), 12), BONE, bevel=4, gloss=1.0))
    over(img, shade(poly([(128, 44), (160, 76), (128, 108), (96, 76)]), GILT, bevel=6, gloss=1.0))
    over(img, shade(disc(128, 76, 9), RED, bevel=3, gloss=1.0))


def obj_fire_altar(img):
    """祭祀：阶式火坛，坛上烈焰。"""
    over(img, shade(rrect([56, 200, 200, 232], 4), PARCH, bevel=6, grain=0.35))
    over(img, shade(rrect([82, 156, 174, 204], 4), OCHRE, bevel=6, grain=0.35))
    over(img, shade(rrect([66, 140, 190, 160], 4), PARCH, bevel=6, grain=0.35))
    flame = union(poly([(128, 6), (180, 90), (172, 140), (84, 140), (76, 90)]),
                  poly([(80, 40), (116, 120), (80, 140)]), poly([(178, 46), (176, 140), (140, 120)]))
    over(img, shade(flame, RED, bevel=10, gloss=1.0))
    over(img, shade(poly([(128, 56), (154, 108), (144, 140), (112, 140), (102, 108)]), GILT, bevel=6, gloss=1.0))


def obj_parasol(img):
    """王权：华盖伞，垂金流苏，顶有宝珠。"""
    over(img, shade(rrect([120, 60, 136, 240], 4), GILT, bevel=4, gloss=1.0))
    over(img, shade(chord([18, 30, 238, 230], 180, 360), RED, bevel=16, gloss=0.8))
    for x in range(26, 232, 24):
        over(img, shade(poly([(x, 128), (x + 24, 128), (x + 12, 152)]), GILT, bevel=3, gloss=1.0))
    over(img, shade(rrect([18, 122, 238, 134], 3), GILT, bevel=3, gloss=1.0))
    over(img, shade(disc(128, 26, 14), GILT, bevel=4, gloss=1.0))


def obj_peacock(img):
    """王族：孔雀翎。"""
    over(img, shade(curve(bezier((128, 236), (110, 160), (128, 80)), 8), GILT, bevel=3))
    for k in range(9):
        y = 120 + k * 12
        over(img, shade(lines([[(122 - k, y), (70 - k * 2, y - 30)], [(128 + k, y), (186 + k * 2, y - 30)]], 4), FOREST,
                        bevel=1, flat=True))
    over(img, shade(ellipse([74, 22, 182, 140]), FOREST, bevel=12, gloss=0.8))
    over(img, shade(ellipse([96, 46, 160, 118]), GILT, bevel=8, gloss=1.0))
    over(img, shade(ellipse([108, 60, 148, 106]), NAVY, bevel=6, gloss=0.9))
    over(img, shade(ellipse([118, 74, 138, 96]), LACQ, bevel=3, flat=True))


def obj_mace(img):
    """武勋：金瓜锤。"""
    over(img, shade(lines([[(70, 230), (156, 100)]], 16), WOOD, bevel=5, grain=0.3))
    over(img, shade(rrect([60, 210, 100, 240], 6), GILT, bevel=4))
    over(img, shade(disc(168, 82, 56), GILT, bevel=14, gloss=1.0))
    for a in range(0, 360, 45):
        r = math.radians(a)
        over(img, shade(lines([[(168, 82), (168 + 52 * math.cos(r), 82 + 52 * math.sin(r))]], 5), AMBER, bevel=1, flat=True))
    over(img, shade(disc(168, 82, 12), RED, bevel=4, gloss=1.0))


def obj_wolf_head(img):
    """祖训：狼首侧影(朝右)，竖耳长吻。"""
    head = poly([(56, 236), (50, 170), (62, 116), (80, 76), (78, 14), (112, 56), (134, 52), (160, 12), (166, 70), (200, 104),
                 (238, 130), (236, 150), (200, 160), (168, 176), (130, 200), (124, 236)])
    over(img, shade(head, STEEL, bevel=16, gloss=0.8, grain=0.3))
    over(img, shade(poly([(86, 34), (104, 62), (90, 70)]), LACQ, bevel=2, flat=True))
    over(img, shade(poly([(154, 34), (156, 66), (144, 60)]), LACQ, bevel=2, flat=True))
    over(img, shade(poly([(142, 96), (170, 100), (148, 110)]), GILT, bevel=3, gloss=1.0))
    over(img, shade(disc(232, 138, 8), LACQ, bevel=2, flat=True))
    over(img, shade(poly([(170, 166), (226, 152), (190, 172)]), LACQ, bevel=2, flat=True))
    over(img, shade(poly([(56, 236), (60, 190), (90, 230)]), IRON, bevel=4, grain=0.3))


def obj_paiza(img):
    """法令：长圆令牌，上穿环。"""
    over(img, shade(ring(128, 34, 22, 12), GILT, bevel=4, gloss=1.0))
    over(img, shade(rrect([78, 50, 178, 234], 44), GILT, bevel=14, gloss=1.0))
    over(img, shade(disc(128, 80, 10), LACQ, bevel=2, flat=True))
    for y in (114, 142, 170, 198):
        over(img, shade(rrect([108, y, 148, y + 10], 3), AMBER, bevel=2, flat=True))


def obj_horse_head(img):
    """安牧：马首侧影，飘鬃。"""
    head = poly([(70, 230), (78, 160), (96, 100), (120, 54), (132, 22), (150, 50), (196, 120), (222, 174), (206, 196), (176, 186),
                 (150, 160), (140, 236)])
    over(img, shade(head, OCHRE, bevel=16, gloss=0.8, grain=0.25))
    mane = poly([(120, 54), (96, 100), (78, 160), (70, 230), (50, 214), (58, 150), (80, 86), (108, 46)])
    over(img, shade(mane, LACQ, bevel=6, grain=0.3))
    over(img, shade(ellipse([150, 92, 168, 108]), LACQ, bevel=2, flat=True))
    over(img, shade(lines([[(146, 150), (214, 176)]], 6), RED, bevel=2))


def obj_yurt(img):
    """牧本：毡帐。"""
    over(img, shade(rrect([40, 130, 216, 214], 4), BONE, bevel=10, grain=0.3))
    over(img, shade(chord([40, 40, 216, 220], 180, 360), BONE, bevel=14, gloss=0.6, grain=0.3))
    over(img, shade(rrect([40, 140, 216, 152], 2), RED, bevel=3))
    over(img, shade(rrect([40, 190, 216, 200], 2), RED, bevel=3))
    over(img, shade(rrect([108, 156, 148, 214], 4), RED, bevel=5, gloss=0.6))
    over(img, shade(ellipse([110, 44, 146, 64]), WOOD, bevel=4))
    over(img, shade(rrect([30, 210, 226, 226], 4), OCHRE, bevel=4, grain=0.3))


def obj_bow(img):
    """骑射：反曲弓与箭。"""
    over(img, shade(curve(bezier((70, 24), (16, 128), (70, 232)), 20), OCHRE, bevel=6, gloss=0.9))
    over(img, shade(curve([(70, 24), (92, 30), (100, 14)], 14), OCHRE, bevel=4))
    over(img, shade(curve([(70, 232), (92, 226), (100, 242)], 14), OCHRE, bevel=4))
    over(img, shade(lines([[(84, 28), (84, 228)]], 4), BONE, bevel=1, flat=True))
    over(img, shade(lines([[(46, 128), (214, 128)]], 9), PARCH, bevel=2))
    over(img, shade(poly([(242, 128), (206, 108), (206, 148)]), STEEL, bevel=5, gloss=1.0))
    over(img, shade(poly([(48, 128), (24, 106), (66, 108)]), BONE, bevel=2))
    over(img, shade(poly([(48, 128), (24, 150), (66, 148)]), BONE, bevel=2))


def obj_drum(img):
    """萨满：神鼓，鼓面画纹，下系彩带。"""
    over(img, shade(poly([(100, 196), (90, 244), (110, 244)]), RED, bevel=3))
    over(img, shade(poly([(156, 196), (148, 244), (168, 244)]), NAVY, bevel=3))
    over(img, shade(ellipse([34, 30, 222, 206]), WOOD, bevel=10, gloss=0.6))
    over(img, shade(ellipse([46, 42, 210, 194]), PARCH, bevel=8, grain=0.35))
    over(img, shade(disc(128, 118, 20), RED, bevel=4, gloss=0.8))
    for a in range(0, 360, 45):
        r = math.radians(a)
        over(img, shade(lines([[(128 + 28 * math.cos(r), 118 + 28 * math.sin(r)),
                                 (128 + 60 * math.cos(r), 118 + 54 * math.sin(r))]], 5), RED, bevel=1, flat=True))


def obj_tug(img):
    """汗权：白马尾大纛，叉形枪头。"""
    over(img, shade(rrect([120, 50, 136, 244], 5), WOOD, bevel=5, grain=0.3))
    over(img, shade(union(poly([(128, 6), (142, 50), (114, 50)]), poly([(88, 18), (106, 58), (92, 62)]),
                          poly([(168, 18), (164, 62), (150, 58)]), rrect([86, 50, 170, 64], 4)), GILT, bevel=5, gloss=1.0))
    for k in range(-5, 6):
        over(img, shade(curve(bezier((128 + k * 4, 84), (128 + k * 10, 150), (128 + k * 16, 214)), 9),
                        BONE if k % 2 else PARCH, bevel=2, gloss=0.6))
    over(img, shade(ring(128, 82, 24, 14), GILT, bevel=4, gloss=1.0))

def obj_fur_hat(img):
    """宗王：圆顶冠帽，宽毛边，顶缀金珠。"""
    dome = sub(ellipse([60, 36, 196, 200]), rrect([0, 150, 256, 256], 0))
    over(img, shade(dome, RED, bevel=14, gloss=0.8))
    for x in (96, 128, 160):
        over(img, shade(sub(lines([[(x, 44), (128 + (x - 128) * 1.6, 150)]], 6), rrect([0, 150, 256, 256], 0)), GILT,
                        bevel=2, gloss=1.0))
    over(img, shade(ellipse([28, 136, 228, 214]), WOOD, bevel=14, gloss=0.4, grain=0.8))
    over(img, shade(disc(128, 30, 16), GILT, bevel=5, gloss=1.0))
    over(img, shade(disc(128, 174, 14), JADE, bevel=4, gloss=1.0))

def obj_sun_stone(img):
    """古训：刻纹日石。"""
    over(img, shade(disc(128, 128, 104), PARCH, bevel=12, gloss=0.5, grain=0.4))
    over(img, shade(ring(128, 128, 90, 82), OCHRE, bevel=2, flat=True))
    for k in range(8):
        a = k * math.pi / 4 + math.pi / 8
        over(img, shade(poly([(128 + 80 * math.cos(a - 0.18), 128 + 80 * math.sin(a - 0.18)),
                              (128 + 80 * math.cos(a + 0.18), 128 + 80 * math.sin(a + 0.18)),
                              (128 + 52 * math.cos(a), 128 + 52 * math.sin(a))]), OCHRE, bevel=2, flat=True))
    over(img, shade(ring(128, 128, 50, 42), OCHRE, bevel=2, flat=True))
    over(img, shade(rrect([104, 104, 152, 152], 8), JADE, bevel=6, gloss=0.8))
    over(img, shade(rrect([114, 116, 124, 124], 1), LACQ, bevel=1, flat=True))
    over(img, shade(rrect([132, 116, 142, 124], 1), LACQ, bevel=1, flat=True))


def obj_fold_codex(img):
    """律令：折页法典，页上色块纹样(不写字)。"""
    pages = [(36, 70), (84, 52), (132, 70), (180, 52)]
    for k, (x, y) in enumerate(pages):
        page = poly([(x, y), (x + 48, y + (18 if k % 2 == 0 else -18)), (x + 48, y + 160 + (18 if k % 2 == 0 else -18)),
                     (x, y + 160)])
        over(img, shade(page, PARCH, bevel=6, grain=0.3))
        over(img, shade(rrect([x + 10, y + 30, x + 34, y + 60], 3), RED, bevel=2, flat=True))
        over(img, shade(rrect([x + 10, y + 80, x + 34, y + 110], 3), JADE, bevel=2, flat=True))


def obj_flower(img):
    """安民：四瓣花。"""
    over(img, shade(curve(bezier((128, 236), (112, 190), (128, 150)), 10), FOREST, bevel=3))
    over(img, shade(rotate(ellipse([60, 170, 124, 200]), -30, 100, 186), FOREST, bevel=5))
    for deg in (0, 90, 180, 270):
        over(img, shade(rotate(ellipse([104, 18, 152, 98]), deg, 128, 98), RED, bevel=10, gloss=0.9))
    over(img, shade(disc(128, 98, 22), GILT, bevel=6, gloss=1.0))


def obj_feather_fan(img):
    """匠作：羽扇。"""
    for deg in (-48, -24, 0, 24, 48):
        over(img, shade(rotate(ellipse([110, 14, 146, 150]), deg, 128, 170), FOREST, bevel=8, gloss=0.9))
        over(img, shade(rotate(lines([[(128, 30), (128, 150)]], 3), deg, 128, 170), GILT, bevel=1, flat=True))
    over(img, shade(disc(128, 168, 22), GILT, bevel=6, gloss=1.0))
    over(img, shade(rrect([120, 180, 136, 240], 5), WOOD, bevel=4))


def obj_maize(img):
    """田社：玉米穗，两片苞叶。"""
    over(img, shade(poly([(128, 230), (60, 120), (82, 70), (128, 190)]), MOSS, bevel=8, gloss=0.6))
    over(img, shade(poly([(128, 230), (196, 120), (174, 70), (128, 190)]), MOSS, bevel=8, gloss=0.6))
    over(img, shade(ellipse([94, 26, 162, 200]), GILT, bevel=12, gloss=1.0))
    for y in range(44, 190, 16):
        over(img, shade(lines([[(100, y), (156, y)]], 3), AMBER, bevel=1, flat=True))
    over(img, shade(lines([[(128, 30), (128, 196)]], 3), AMBER, bevel=1, flat=True))


def obj_eagle_head(img):
    """武士：鹰首侧影，钩喙。"""
    head = poly([(40, 230), (50, 140), (80, 70), (130, 40), (176, 50), (206, 80), (232, 120), (200, 118), (176, 140),
                 (150, 160), (120, 236)])
    over(img, shade(head, WOOD, bevel=16, gloss=0.7, grain=0.3))
    over(img, shade(poly([(176, 70), (232, 120), (222, 150), (200, 128), (170, 120)]), GILT, bevel=8, gloss=1.0))
    over(img, shade(disc(150, 88, 12), GILT, bevel=3, gloss=1.0))
    over(img, shade(disc(152, 88, 5), LACQ, bevel=1, flat=True))
    for k in range(4):
        over(img, shade(poly([(60 + k * 14, 150 + k * 18), (40 + k * 10, 210), (76 + k * 14, 168 + k * 18)]), BONE, bevel=2))


def obj_cacao(img):
    """远商：可可荚与豆。"""
    over(img, shade(rotate(ellipse([84, 20, 172, 200]), 25, 128, 110), OCHRE, bevel=14, gloss=0.8, grain=0.2))
    for k in (-24, 0, 24):
        over(img, shade(rotate(lines([[(128 + k, 34), (128 + k * 1.2, 186)]], 4), 25, 128, 110), AMBER, bevel=1, flat=True))
    for x, y in ((60, 206), (100, 222), (150, 214), (190, 200)):
        over(img, shade(ellipse([x - 18, y - 12, x + 18, y + 12]), WOOD, bevel=5, gloss=0.9))


def obj_step_pyramid(img):
    """祭司：阶梯神庙，顶上祠堂。"""
    for k, (w, y) in enumerate(((110, 206), (90, 176), (70, 146), (50, 116))):
        over(img, shade(rrect([128 - w, y, 128 + w, y + 32], 2), OCHRE, bevel=6, grain=0.35))
    over(img, shade(rrect([112, 116, 144, 238], 2), OCHRE, bevel=3))
    over(img, shade(rrect([96, 70, 160, 118], 4), RED, bevel=6, gloss=0.6))
    over(img, shade(poly([(90, 72), (128, 38), (166, 72)]), LACQ, bevel=5))
    over(img, shade(rrect([118, 88, 138, 118], 2), LACQ, bevel=2, flat=True))


def obj_feather_crown(img):
    """王权：金箍羽冠。"""
    for deg in range(-60, 61, 20):
        over(img, shade(rotate(ellipse([110, 6, 146, 134]), deg, 128, 160), MOSS, bevel=8, gloss=1.0))
        over(img, shade(rotate(ellipse([120, 6, 136, 40]), deg, 128, 160), BONE, bevel=3, gloss=0.8))
    over(img, shade(rrect([46, 140, 210, 196], 12), GILT, bevel=10, gloss=1.0))
    for x in (78, 128, 178):
        over(img, shade(disc(x, 168, 12), RED, bevel=4, gloss=1.0))
    over(img, shade(poly([(100, 196), (156, 196), (128, 236)]), GILT, bevel=6, gloss=1.0))


def obj_jade_pendant(img):
    """贵胄：玉璧佩，金边红绳。"""
    over(img, shade(lines([[(128, 10), (128, 60)]], 6), RED, bevel=2))
    over(img, shade(ring(128, 140, 94, 30), GILT, bevel=6, gloss=1.0))
    over(img, shade(ring(128, 140, 84, 38), BONE, bevel=12, gloss=0.9))
    for a in range(0, 360, 60):
        r = math.radians(a)
        over(img, shade(disc(128 + 61 * math.cos(r), 140 + 61 * math.sin(r), 9), MOSS, bevel=2, flat=True))
    over(img, shade(poly([(116, 228), (140, 228), (150, 252), (106, 252)]), RED, bevel=3))


def obj_spindle(img):
    """后族：织机上的织物，阶梯回纹。"""
    over(img, shade(rrect([30, 30, 226, 50], 8), WOOD, bevel=6, gloss=0.7))
    over(img, shade(rrect([30, 206, 226, 226], 8), WOOD, bevel=6, gloss=0.7))
    over(img, shade(rrect([50, 46, 206, 210], 2), BONE, bevel=6, grain=0.35))
    for k, y in enumerate((70, 118, 166)):
        for x in range(58, 200, 36):
            over(img, shade(poly([(x, y + 24), (x + 12, y + 24), (x + 12, y + 12), (x + 24, y + 12), (x + 24, y), (x + 36, y),
                                  (x + 36, y + 12), (x + 24, y + 12), (x + 24, y + 24), (x + 12, y + 24), (x + 12, y + 36),
                                  (x, y + 36)]), RED if k % 2 == 0 else NAVY, bevel=2, flat=True))

def obj_serpent(img):
    """王族：羽蛇。"""
    pts = bezier((52, 236), (236, 196), (128, 136)) + bezier((128, 136), (20, 84), (146, 60))[1:]
    over(img, shade(curve(pts, 54), MOSS, bevel=14, gloss=1.0, grain=0.2))
    for t in range(4, 44, 7):
        x, y = pts[t]
        over(img, shade(disc(x, y, 9), GILT, bevel=2, flat=True))
    over(img, shade(ellipse([130, 18, 214, 82]), MOSS, bevel=12, gloss=1.0))
    for k in range(5):
        over(img, shade(rotate(ellipse([138, -6, 162, 50]), -70 + k * 22, 162, 46), RED, bevel=5, gloss=0.9))
    over(img, shade(disc(188, 46, 9), GILT, bevel=2, gloss=1.0))
    over(img, shade(poly([(208, 56), (242, 64), (208, 74)]), RED, bevel=2, flat=True))


def obj_macuahuitl(img):
    """武勋：镶黑曜石刃的木棍。"""
    for k in range(5):
        y = 26 + k * 36
        for x in (96, 160):
            over(img, shade(rotate(poly([(x, y), (x + (-34 if x < 128 else 34), y + 16), (x, y + 32)]), 30, 128, 128),
                            LACQ, bevel=4, gloss=1.0))
    over(img, shade(rotate(rrect([96, 12, 160, 214], 20), 30, 128, 128), PARCH, bevel=12, gloss=0.7, grain=0.3))
    over(img, shade(rotate(rrect([112, 200, 144, 254], 10), 30, 128, 128), RED, bevel=5))


def obj_feather(img):
    """长老：一根鹰羽，羽根缠红绳。"""
    vane = rotate(union(ellipse([100, 20, 156, 200]), poly([(128, 8), (150, 40), (106, 40)])), 30, 128, 128)
    over(img, shade(vane, BONE, bevel=10, gloss=0.7, grain=0.2))
    over(img, shade(rotate(ellipse([100, 20, 156, 80]), 30, 128, 128), LACQ, bevel=6, gloss=0.6))
    over(img, shade(rotate(lines([[(128, 30), (128, 236)]], 5), 30, 128, 128), PARCH, bevel=1, flat=True))
    over(img, shade(rotate(rrect([116, 196, 140, 222], 4), 30, 128, 128), RED, bevel=4))


def obj_pipe(img):
    """和议：长烟管，垂两支羽。"""
    over(img, shade(lines([[(18, 190), (196, 80)]], 30), OCHRE, bevel=8, grain=0.3))
    over(img, shade(union(rrect([160, 40, 240, 100], 14), rrect([196, 8, 242, 80], 12)), RED, bevel=12, gloss=0.8))
    for x, y in ((80, 152), (128, 122)):
        over(img, shade(lines([[(x, y), (x - 6, y + 40)]], 8), RED, bevel=1, flat=True))
        over(img, shade(ellipse([x - 30, y + 30, x + 6, y + 120]), BONE, bevel=8, gloss=0.7))
        over(img, shade(ellipse([x - 30, y + 90, x + 6, y + 120]), LACQ, bevel=2, flat=True))


def obj_canoe(img):
    """共有：桦皮舟与桨。"""
    over(img, shade(lines([[(70, 40), (170, 196)]], 10), WOOD, bevel=3))
    over(img, shade(rotate(ellipse([156, 180, 186, 236]), -32, 171, 208), WOOD, bevel=4))
    hull = union(chord([20, 70, 236, 210], 0, 180), poly([(20, 140), (8, 110), (40, 140)]), poly([(236, 140), (248, 110), (216, 140)]))
    over(img, shade(hull, PARCH, bevel=12, gloss=0.6, grain=0.35))
    for x in range(48, 214, 28):
        over(img, shade(lines([[(x, 146), (x + 4, 196)]], 3), LACQ, bevel=1, flat=True))
    over(img, shade(rrect([20, 134, 236, 146], 4), WOOD, bevel=3))


def obj_war_club(img):
    """勇士：圆头战棍，系羽。"""
    over(img, shade(curve(bezier((70, 236), (90, 140), (150, 80)), 16), WOOD, bevel=5, gloss=0.6, grain=0.3))
    over(img, shade(disc(166, 68, 44), WOOD, bevel=14, gloss=0.8, grain=0.3))
    over(img, shade(rrect([92, 150, 120, 168], 4), RED, bevel=3))
    over(img, shade(ellipse([60, 150, 92, 222]), BONE, bevel=5, gloss=0.6))
    over(img, shade(ellipse([60, 196, 92, 222]), LACQ, bevel=2, flat=True))


def obj_wampum(img):
    """贸易：贝珠带。"""
    over(img, shade(rrect([22, 82, 234, 174], 10), BONE, bevel=8, grain=0.3))
    for k in range(7):
        x = 34 + k * 28
        if k % 2 == 0:
            over(img, shade(poly([(x, 128), (x + 14, 96), (x + 28, 128), (x + 14, 160)]), NAVY, bevel=3, flat=True))
        else:
            over(img, shade(rrect([x + 6, 112, x + 22, 144], 2), NAVY, bevel=2, flat=True))
    for x in (34, 222):
        over(img, shade(lines([[(x, 174), (x, 222)]], 5), WOOD, bevel=1, flat=True))


def obj_turtle(img):
    """药巫：龟。"""
    for x, y in ((64, 74), (192, 74), (64, 188), (192, 188)):
        over(img, shade(disc(x, y, 22), FOREST, bevel=6, gloss=0.6))
    over(img, shade(ellipse([104, 14, 152, 66]), FOREST, bevel=6, gloss=0.6))
    over(img, shade(ellipse([50, 50, 206, 216]), FOREST, bevel=16, gloss=0.8, grain=0.2))
    over(img, shade(poly([(128, 92), (158, 112), (158, 150), (128, 170), (98, 150), (98, 112)]), MOSS, bevel=5, gloss=0.8))
    for x, y in ((128, 70), (128, 192), (82, 110), (174, 110), (82, 156), (174, 156)):
        over(img, shade(disc(x, y, 12), MOSS, bevel=3, gloss=0.6))


def obj_eagle_staff(img):
    """酋权：弯头权杖，垂三支长羽。"""
    over(img, shade(rrect([150, 64, 184, 248], 8), OCHRE, bevel=8, grain=0.3))
    over(img, shade(arc([60, 6, 184, 130], 180, 360, 34), OCHRE, bevel=8, grain=0.3))
    over(img, shade(rrect([56, 56, 96, 90], 8), RED, bevel=5))
    for k in range(3):
        y = 92 + k * 52
        over(img, shade(ellipse([46, y - 6, 138, y + 40]), BONE, bevel=8, gloss=0.8))
        over(img, shade(ellipse([46, y - 6, 78, y + 40]), LACQ, bevel=2, flat=True))
        over(img, shade(rrect([132, y + 6, 156, y + 24], 4), RED, bevel=2, flat=True))


def obj_bear_paw(img):
    """氏族：熊掌印。"""
    over(img, shade(union(ellipse([60, 110, 196, 224]), ellipse([84, 96, 172, 160])), WOOD, bevel=14, gloss=0.6, grain=0.3))
    for k, (x, y) in enumerate(((58, 86), (92, 52), (128, 42), (164, 52), (198, 86))):
        over(img, shade(poly([(x - 8, y - 18), (x, y - 44), (x + 8, y - 18)]), BONE, bevel=3, gloss=1.0))
        over(img, shade(disc(x, y, 20), WOOD, bevel=8, gloss=0.6, grain=0.3))


def obj_scroll_roll(img):
    """律令(日本)：横展的卷轴。"""
    over(img, shade(rrect([40, 66, 216, 190], 4), PARCH, bevel=8, grain=0.35))
    for x in range(64, 200, 22):
        over(img, shade(rrect([x, 86, x + 6, 170], 2), LACQ, bevel=1, flat=True))
    for x in (28, 210):
        over(img, shade(rrect([x, 50, x + 20, 206], 8), WOOD, bevel=6, gloss=0.8))
        over(img, shade(rrect([x - 4, 42, x + 24, 58], 6), GILT, bevel=3, gloss=1.0))
        over(img, shade(rrect([x - 4, 198, x + 24, 214], 6), GILT, bevel=3, gloss=1.0))
    over(img, shade(lines([[(128, 190), (140, 236)]], 5), RED, bevel=2))


def obj_bell(img):
    """佛门：梵钟。"""
    over(img, shade(ring(128, 30, 18, 8), BRASS, bevel=4, gloss=1.0))
    bell = union(rrect([62, 40, 194, 210], 40), rrect([52, 180, 204, 222], 8))
    over(img, shade(bell, BRASS, bevel=16, gloss=0.9, grain=0.25))
    over(img, shade(rrect([62, 120, 194, 128], 2), AMBER, bevel=2, flat=True))
    for x in (84, 104, 152, 172):
        for y in (62, 82, 102):
            over(img, shade(disc(x, y, 5), GILT, bevel=2, gloss=1.0))
    over(img, shade(disc(128, 160, 16), GILT, bevel=4, gloss=1.0))


def obj_rice(img):
    """农本：三支稻穗，穗头低垂。"""
    for dx, bend in ((-44, -1), (0, 1), (44, 1)):
        stem = bezier((128 + dx * 0.3, 236), (128 + dx, 110), (128 + dx + bend * 56, 70))
        over(img, shade(curve(stem, 14), MOSS, bevel=3, gloss=0.8))
        for k in range(5):
            t = 0.5 + k * 0.12
            x = (1 - t) ** 2 * stem[0][0] + 2 * (1 - t) * t * (128 + dx) + t * t * stem[-1][0]
            y = (1 - t) ** 2 * stem[0][1] + 2 * (1 - t) * t * 110 + t * t * stem[-1][1]
            over(img, shade(ellipse([x - 14, y - 10, x + 14, y + 20]), GILT, bevel=5, gloss=1.0))
    over(img, shade(rrect([96, 192, 160, 214], 6), RED, bevel=4))


def obj_katanas(img):
    """兵学：两把交叉的太刀。"""
    for sign in (1, -1):
        def X(x):
            return 128 + (x - 128) * sign
        over(img, shade(curve(bezier((X(78), 190), (X(120), 120), (X(222), 34)), 13), STEEL, bevel=4, gloss=1.0))
        over(img, shade(lines([[(X(78), 190), (X(42), 236)]], 15), LACQ, bevel=3))
        for t in (0.3, 0.6):
            x = X(78) + (X(42) - X(78)) * t
            y = 190 + 46 * t
            over(img, shade(disc(x, y, 4), GILT, bevel=1, flat=True))
        over(img, shade(disc(X(80), 188, 16), GILT, bevel=4, gloss=1.0))

def obj_torii(img):
    """神道：鸟居。"""
    over(img, shade(poly([(14, 46), (128, 60), (242, 46), (236, 72), (128, 80), (20, 72)]), LACQ, bevel=6, gloss=0.6))
    over(img, shade(rrect([30, 76, 226, 96], 4), RED, bevel=6, gloss=0.8))
    over(img, shade(rrect([42, 120, 214, 138], 4), RED, bevel=6, gloss=0.8))
    for x in (66, 166):
        over(img, shade(poly([(x, 78), (x + 24, 78), (x + 28, 236), (x - 4, 236)]), RED, bevel=8, gloss=0.8))
    over(img, shade(rrect([118, 96, 138, 122], 2), RED, bevel=3))


def obj_mirror(img):
    """皇权：铜镜，背钮系绳。"""
    over(img, shade(disc(128, 136, 100), BRASS, bevel=14, gloss=1.0, grain=0.2))
    over(img, shade(ring(128, 136, 84, 76), AMBER, bevel=2, flat=True))
    for k in range(12):
        a = k * math.pi / 6
        over(img, shade(disc(128 + 62 * math.cos(a), 136 + 62 * math.sin(a), 7), AMBER, bevel=2, flat=True))
    over(img, shade(disc(128, 136, 24), GILT, bevel=6, gloss=1.0))
    over(img, shade(curve(bezier((110, 132), (60, 220), (40, 240)), 6), RED, bevel=2))


def obj_folding_fan(img):
    """豪族：折扇。"""
    over(img, shade(sub(pieslice([20, 30, 236, 246], 200, 340), disc(128, 138, 44)), PARCH, bevel=8, grain=0.3))
    for deg in range(200, 341, 14):
        r = math.radians(deg)
        over(img, shade(lines([[(128, 138), (128 + 106 * math.cos(r), 138 + 106 * math.sin(r))]], 3), WOOD, bevel=1, flat=True))
    over(img, shade(arc([60, 70, 196, 206], 200, 340, 8), RED, bevel=2, flat=True))
    over(img, shade(rrect([116, 130, 140, 236], 6), WOOD, bevel=5, gloss=0.6))
    over(img, shade(disc(128, 222, 8), GILT, bevel=2, gloss=1.0))


def obj_magatama(img):
    """皇族：勾玉，穿绳。"""
    body = union(disc(120, 100, 66), poly([(64, 130), (120, 166), (150, 236), (100, 220), (70, 180)]))
    body = sub(body, disc(176, 150, 34))
    over(img, shade(body, JADE, bevel=16, gloss=1.0))
    over(img, shade(disc(130, 86, 14), LACQ, bevel=2, flat=True))
    over(img, shade(curve(bezier((130, 72), (170, 20), (226, 30)), 5), RED, bevel=2))


def obj_kabuto(img):
    """武家：鍬形盔。"""
    over(img, shade(union(poly([(84, 92), (70, 20), (104, 80)]), poly([(172, 92), (186, 20), (152, 80)])), GILT, bevel=5,
                    gloss=1.0))
    over(img, shade(chord([58, 50, 198, 200], 180, 360), IRON, bevel=14, gloss=0.9, grain=0.2))
    for x in range(72, 190, 16):
        over(img, shade(lines([[(x, 70), (x, 124)]], 3), STEEL, bevel=1, flat=True))
    neck = poly([(58, 120), (198, 120), (236, 190), (20, 190)])
    over(img, shade(neck, RED, bevel=10, gloss=0.6))
    for y in (146, 168):
        over(img, shade(lines([[(40, y), (216, y)]], 3), LACQ, bevel=1, flat=True))
    over(img, shade(disc(128, 104, 12), GILT, bevel=4, gloss=1.0))


def obj_runestone(img):
    """古法(维京)：刻纹立石。"""
    over(img, shade(rrect([30, 216, 226, 236], 6), MOSS, bevel=4, grain=0.4))
    stone = poly([(70, 226), (60, 90), (90, 30), (160, 22), (196, 70), (190, 226)])
    over(img, shade(stone, STEEL, bevel=16, gloss=0.5, grain=0.5))
    over(img, shade(curve(bezier((96, 200), (60, 80), (128, 52)) + bezier((128, 52), (196, 70), (160, 200))[1:], 8), RED,
                    bevel=2, flat=True))
    for y in (100, 130, 160):
        over(img, shade(lines([[(112, y), (120, y + 18)], [(120, y + 18), (140, y)]], 5), LACQ, bevel=1, flat=True))


def obj_axe_shield(img):
    """战团(维京)：圆盾与战斧。"""
    over(img, shade(lines([[(60, 230), (196, 30)]], 12), WOOD, bevel=4, grain=0.3))
    over(img, shade(rotate(chord([150, 10, 250, 110], 90, 270), -35, 196, 52), STEEL, bevel=8, gloss=1.0))
    over(img, shade(disc(112, 140, 82), WOOD, bevel=12, gloss=0.6, grain=0.4))
    for deg in (0, 60, 120):
        over(img, shade(rotate(rrect([106, 60, 118, 220], 2), deg, 112, 140), RED, bevel=2, flat=True))
    over(img, shade(ring(112, 140, 82, 72), IRON, bevel=4, gloss=0.8))
    over(img, shade(disc(112, 140, 20), IRON, bevel=6, gloss=1.0))


def obj_hammer_amulet(img):
    """古神祭司(维京)：锤形护符。"""
    over(img, shade(ring(128, 30, 18, 8), BRASS, bevel=4, gloss=1.0))
    over(img, shade(rrect([110, 44, 146, 150], 8), BRASS, bevel=8, gloss=1.0))
    head = poly([(48, 140), (208, 140), (190, 236), (128, 214), (66, 236)])
    over(img, shade(head, BRASS, bevel=14, gloss=1.0, grain=0.2))
    for x in (90, 128, 166):
        over(img, shade(disc(x, 182, 9), AMBER, bevel=2, flat=True))


def obj_scroll_pair(img):
    """经师(犹太)：双轴经卷。"""
    over(img, shade(rrect([70, 60, 186, 200], 4), PARCH, bevel=8, grain=0.35))
    for y in range(80, 190, 18):
        over(img, shade(rrect([88, y, 168, y + 6], 2), LACQ, bevel=1, flat=True))
    for x in (40, 186):
        over(img, shade(rrect([x, 52, x + 30, 208], 12), WOOD, bevel=8, gloss=0.8))
        over(img, shade(rrect([x + 10, 18, x + 20, 240], 4), BRASS, bevel=3, gloss=1.0))


def obj_oil_lamp(img):
    """祭司(犹太)：陶油灯，灯焰。"""
    over(img, shade(union(ellipse([30, 140, 200, 220]), poly([(180, 160), (236, 150), (236, 172), (190, 200)])), OCHRE,
                    bevel=14, gloss=0.7, grain=0.3))
    over(img, shade(ellipse([86, 146, 136, 166]), LACQ, bevel=2, flat=True))
    over(img, shade(arc([10, 140, 70, 210], 90, 270, 12), OCHRE, bevel=4))
    flame = poly([(226, 40), (246, 110), (226, 152), (206, 110)])
    over(img, shade(flame, RED, bevel=8, gloss=1.0))
    over(img, shade(poly([(226, 84), (236, 118), (226, 146), (216, 118)]), GILT, bevel=4, gloss=1.0))


def obj_ankh(img):
    """神庙祭司(埃及)：生命之环。"""
    over(img, shade(ring(128, 76, 50, 26), GILT, bevel=10, gloss=1.0))
    over(img, shade(rrect([40, 116, 216, 142], 6), GILT, bevel=8, gloss=1.0))
    over(img, shade(poly([(114, 130), (142, 130), (154, 236), (102, 236)]), GILT, bevel=10, gloss=1.0))


# ═════════════ 各谱系的对照表 ═════════════
# 同名同义的学说共用器物(行会派各线都是铁砧)；身份立场按各文化的说法画
DOCTRINES = {
    "roma": {
        "Confucianism": obj_codex_cross, "Legalism": F.obj_scales, "HuangLao": obj_alms_bowl, "Mohism": obj_anvil_hammer,
        "Agrarianism": obj_sheaf, "Strategists": obj_kite_shield, "Diplomatists": F.obj_sailboat, "YinYang": obj_latin_cross,
        "ImperialPower": obj_west_crown, "GreatClans": obj_escutcheon, "ConsortKin": obj_rings, "RoyalKin": obj_orb,
        "MilitaryMerit": obj_great_helm,
    },
    "arabic": {
        "Confucianism": obj_book_crescent, "Legalism": obj_decree, "HuangLao": obj_rosary, "Mohism": obj_anvil_hammer,
        "Agrarianism": obj_palm, "Strategists": obj_scimitars, "Diplomatists": obj_camel, "YinYang": obj_astrolabe,
        "ImperialPower": obj_turban, "GreatClans": F.obj_banners, "ConsortKin": obj_lattice, "Eunuchs": F.obj_keys,
        "RoyalKin": obj_khanjar, "MilitaryMerit": obj_spiked_helm,
    },
    "india": {
        "Confucianism": obj_dharma_wheel, "Legalism": obj_rod, "HuangLao": obj_lotus, "Mohism": obj_anvil_hammer,
        "Agrarianism": obj_kalash, "Strategists": obj_elephant, "Diplomatists": F.obj_sailboat, "YinYang": obj_fire_altar,
        "ImperialPower": obj_parasol, "GreatClans": F.obj_tower, "ConsortKin": obj_rings, "RoyalKin": obj_peacock,
        "MilitaryMerit": obj_mace,
    },
    "youmu": {
        "Confucianism": obj_wolf_head, "Legalism": obj_paiza, "HuangLao": obj_horse_head, "Mohism": obj_anvil_hammer,
        "Agrarianism": obj_yurt, "Strategists": obj_bow, "Diplomatists": obj_camel, "YinYang": obj_drum,
        "ImperialPower": obj_tug, "GreatClans": F.obj_banners, "ConsortKin": obj_rings, "RoyalKin": obj_fur_hat,
        "MilitaryMerit": obj_spiked_helm,
    },
    "aztec": {
        "Confucianism": obj_sun_stone, "Legalism": obj_fold_codex, "HuangLao": obj_flower, "Mohism": obj_feather_fan,
        "Agrarianism": obj_maize, "Strategists": obj_eagle_head, "Diplomatists": obj_cacao, "YinYang": obj_step_pyramid,
        "ImperialPower": obj_feather_crown, "GreatClans": obj_jade_pendant, "ConsortKin": obj_spindle, "RoyalKin": obj_serpent,
        "MilitaryMerit": obj_macuahuitl,
    },
    "ojibwe": {
        "Confucianism": obj_feather, "HuangLao": obj_pipe, "Agrarianism": obj_canoe, "Strategists": obj_war_club,
        "Diplomatists": obj_wampum, "YinYang": obj_turtle, "ImperialPower": obj_eagle_staff, "GreatClans": obj_bear_paw,
        "MilitaryMerit": obj_bow,
    },
    "japan": {
        "Confucianism": F.obj_book, "Legalism": obj_scroll_roll, "HuangLao": obj_bell, "Mohism": obj_anvil_hammer,
        "Agrarianism": obj_rice, "Strategists": obj_katanas, "Diplomatists": F.obj_sailboat, "YinYang": obj_torii,
        "ImperialPower": obj_mirror, "GreatClans": obj_folding_fan, "RoyalKin": obj_magatama, "MilitaryMerit": obj_kabuto,
    },
    # 同一谱系里个别文化另有叫法的(见 Locales 的 party_ideology_<理念>_<文化>)
    "persepolis": {"YinYang": obj_fire_altar},
    "egypt": {"YinYang": obj_ankh},
    "viking": {"Confucianism": obj_runestone, "Strategists": obj_axe_shield, "YinYang": obj_hammer_amulet},
    "kosher": {"Confucianism": obj_scroll_pair, "YinYang": obj_oil_lamp},
}


def build():
    out = {}
    for suffix, table in DOCTRINES.items():
        for ideology, obj in table.items():
            out[f"GameResources/ui/icons/actor_traits/ideology_{ideology}_{suffix}.png"] = F.make_ideology(obj, FIELD[ideology])
    return out


if __name__ == "__main__":
    icons = build()
    for path, im in icons.items():
        full = os.path.join(F.ROOT, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        im.save(full)
    if "--preview" in sys.argv:
        # 每行一个谱系，前面先放华夏那一格作对照
        order = ["Confucianism", "Legalism", "HuangLao", "Mohism", "Agrarianism", "Strategists", "Diplomatists", "YinYang",
                 "ImperialPower", "GreatClans", "ConsortKin", "Eunuchs", "RoyalKin", "MilitaryMerit"]
        k = 4
        W = F.PIXEL * k + 8
        rows = ["huaxia"] + list(DOCTRINES)
        sheet = Image.new("RGBA", (len(order) * W, len(rows) * W), (74, 78, 68, 255))
        for r, suffix in enumerate(rows):
            for c, ideology in enumerate(order):
                name = f"ideology_{ideology}" + ("" if suffix == "huaxia" else f"_{suffix}")
                path = os.path.join(F.ROOT, "GameResources", "ui", "icons", "actor_traits", name + ".png")
                if not os.path.exists(path):
                    continue
                im = Image.open(path).convert("RGBA")
                sheet.alpha_composite(im.resize((F.PIXEL * k, F.PIXEL * k), Image.NEAREST), (c * W + 4, r * W + 4))
        sheet.save(os.path.join(HERE, "doctrine_preview.png"))
    print("wrote", len(icons))
