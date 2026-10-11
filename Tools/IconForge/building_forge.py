"""EmpireCraft 建筑锻造器 —— 矿场、伐木场分级、牧场、屠宰场的地图建筑像素图，按原版建筑的画法：

  · 尺度：1 像素 = 1 格，原版民居约 14×13、矿场 24×21，本套在 20~36 像素之间；
  · 视角：3/4 俯视，屋顶占上半截、正面墙占下半截；
  · 配色：取自原版建筑调色板——木头四档暖棕、石基灰绿、岩石冷灰、窗户亮黄；
    屋顶一律用原版的品红四档 (88,0,88)/(167,0,167)/(222,0,222)/(255,0,255)，游戏里自动换成王国颜色
    (建筑设了 has_kingdom_color，见 IndustryBuildingSystem)；
  · 描边：不用纯黑外框，边缘像素换成本材质的最暗一档(原版就是这样)。

  · 矿场 ec_mine_2~5：矿井 → 水力矿场 → 蒸汽煤矿 → 现代矿业工厂；
  · 伐木场 ec_lumber_1~5：伐木营 → 锯木坊 → 水力锯木厂 → 蒸汽锯木厂 → 现代木材加工厂；
  · 牧场 ec_pasture(大型)、屠宰场 ec_slaughterhouse。

每座输出 main_0(建成)、ruin_0(废墟)、construction_0(施工脚手架)、mini_0(小地图色块)，
写到 GameResources/buildings/<id>/。原版矿场(1 级)沿用原版图。

用法: python Tools/IconForge/building_forge.py [--preview]
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "GameResources", "buildings")

# ═════════════ 原版调色板 ═════════════
# 每种材质四档：最暗(描边) / 暗 / 中 / 亮
WOOD = [(42, 24, 13), (75, 46, 27), (122, 76, 45), (160, 101, 59)]
WOOD_HI = (198, 125, 73)
ROOF = [(88, 0, 88), (167, 0, 167), (222, 0, 222), (255, 0, 255)]   # 王国颜色占位
STONE = [(62, 58, 55), (134, 145, 123), (184, 194, 173), (217, 222, 212)]
ROCK = [(33, 31, 28), (46, 54, 62), (77, 87, 98), (117, 122, 129)]
ROCK_HI = (151, 158, 161)
BRICK = [(70, 22, 0), (109, 37, 0), (145, 57, 11), (192, 113, 17)]
CONCRETE = [(62, 58, 55), (120, 127, 135), (151, 158, 161), (211, 217, 197)]
WATER = [(33, 97, 150), (43, 134, 209), (127, 169, 188)]
GRASS = [(40, 70, 40), (63, 107, 63), (98, 140, 70), (195, 200, 66)]
WINDOW = (255, 216, 0)
DARK = (33, 31, 28)
HAY = [(145, 57, 11), (192, 113, 17), (204, 147, 66), (215, 196, 147)]
WOOL = [(92, 93, 94), (173, 152, 120), (217, 222, 212)]
ORE = (204, 147, 66)
IRON = [(33, 31, 28), (62, 58, 55), (85, 85, 85), (120, 127, 135)]


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.a = np.zeros((h, w, 4), np.uint8)

    def px(self, x, y, c):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < self.w and 0 <= y < self.h:
            self.a[y, x, :3] = c
            self.a[y, x, 3] = 255

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.px(x, y, c)

    def line(self, x0, y0, x1, y1, c):
        n = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(n + 1):
            self.px(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, c)

    def image(self):
        """光从左上来：右侧、下侧的边缘换成本材质的暗色(原版建筑没有统一的黑色外框)，左、上边缘不压暗。"""
        a = self.a.copy()
        solid = a[..., 3] > 0
        pad = np.pad(solid, 1)
        right_open = solid & ~pad[1:-1, 2:]
        bottom_open = solid & ~pad[2:, 1:-1]
        rgb = a[..., :3].astype(np.float32)
        edge = right_open | bottom_open
        a[edge, :3] = np.clip(rgb[edge] * 0.45, 0, 255).astype(np.uint8)
        return Image.fromarray(a, "RGBA")


# ═════════════ 部件(俯视 3D：屋顶占大半，正面墙只露一小截) ═════════════
def roof(c, x0, x1, top, eave, mat=ROOF, dormer=True):
    """从上往下看的四坡屋顶(原版画法)：上半截收尖、下半截整宽。
    左坡迎光：底色中亮，靠左的斜边一道最亮的高光；右坡背光：暗一档，右缘最暗；
    从尖顶往两个下角各拉一道暗的斜脊线；檐口一行最暗；正面山墙上开一扇亮黄的老虎窗。"""
    w = x1 - x0 + 1
    h = eave - top + 1
    tip = max(1, round(h * 0.55))
    mid = (x0 + x1) / 2
    for y in range(top, eave + 1):
        if y < top + tip:
            half = (w / 2) * (y - top + 1) / tip
            left, right = round(mid - half), round(mid + half)
        else:
            left, right = x0, x1
        for x in range(left, right + 1):
            if y == eave:
                col = mat[0]
            elif x <= mid:
                col = mat[3] if x - left <= 1 else mat[2]
            else:
                col = mat[1] if right - x >= 1 else mat[0]
            c.px(x, y, col)
        # 斜脊线：从尖顶到两个下角
        if y >= top + tip and y < eave:
            t = (y - top - tip + 1) / max(1, h - tip)
            c.px(round(x0 + (mid - x0) * (1 - t) * 0.5), y, mat[1])
            c.px(round(x1 - (x1 - mid) * (1 - t) * 0.5), y, mat[0])
    # 屋脊：尖顶往下一道
    for y in range(top, top + tip):
        c.px(round(mid), y, mat[3])
    # 老虎窗：屋顶正面靠下居中，亮黄小三角，外框暗
    if dormer and w >= 8 and h >= 5:
        dy = eave - 2
        cx = round(mid)
        c.px(cx - 1, dy, WINDOW); c.px(cx, dy, WINDOW); c.px(cx, dy - 1, WINDOW)
        c.px(cx - 2, dy, mat[0]); c.px(cx + 1, dy, mat[0]); c.px(cx - 1, dy - 1, mat[0]); c.px(cx + 1, dy - 1, mat[0])
        c.px(cx, dy - 2, mat[0])

def flat_roof(c, x0, x1, top, eave, mat=CONCRETE, trim=ROOF):
    """平屋顶(厂房，俯视)：一整块屋面，分成一格格屋面板(深浅交替)，中间一道采光天窗(浅蓝)，
    几台通风机(顶面亮、下面投一格阴影)；四周一圈王国色女儿墙，下沿最暗。"""
    for y in range(top, eave + 1):
        for x in range(x0, x1 + 1):
            col = mat[2] if ((x - x0) // 3 + (y - top) // 2) % 2 == 0 else \
                tuple((mat[2][i] + mat[3][i]) // 2 for i in range(3))
            c.px(x, y, col)
    sky = top + (eave - top) // 2
    for x in range(x0 + 2, x1 - 1):
        c.px(x, sky, WATER[2] if (x - x0) % 3 else WATER[1])
    for x in range(x0 + 3, x1 - 2, 5):
        c.px(x, top + 2, IRON[3]); c.px(x + 1, top + 2, IRON[3])
        c.px(x, top + 3, mat[1]); c.px(x + 1, top + 3, mat[1])
    for x in range(x0, x1 + 1):
        c.px(x, top, trim[3])
        c.px(x, eave, trim[0])
    for y in range(top, eave + 1):
        c.px(x0, y, trim[2])
        c.px(x1, y, trim[1])

def front(c, x0, x1, top, base, mat):
    """正面墙：竖向木板(或砖)一亮一暗交替，顶上一行亮的檐下梁，右边一截在阴影里；
    最下两行是石基——上沿亮、下面灰、缝隙暗(原版民居都是这样)。"""
    for y in range(top, base - 1):
        for x in range(x0, x1 + 1):
            col = mat[2] if (x - x0) % 2 == 0 else mat[1]
            if (x - x0) % 4 == 3:
                col = mat[0]
            if y == top:
                col = mat[3]
            if x >= x1 - max(1, (x1 - x0) // 5):
                col = mat[1] if col != mat[0] else mat[0]
            c.px(x, y, col)
    for x in range(x0, x1 + 1):
        seam = (x - x0) % 3 == 2
        c.px(x, base - 1, STONE[3] if not seam else STONE[1])
        c.px(x, base, ROCK[3] if not seam else ROCK[1])

def house(c, x0, x1, top, base, mat=WOOD, roof_share=0.55):
    """一座房子：屋顶占 roof_share(左右各出檐一格)，下面是正面墙和两行石基。
    返回(开窗的行, 门的底行)：窗开在檐下梁下面一行，门落在石基上面。"""
    eave = top + max(3, round((base - top) * roof_share))
    front(c, x0 + 1, x1 - 1, eave, base, mat)
    roof(c, x0, x1, top, eave)
    return eave + 2, base - 2

def window(c, x, y):
    """亮黄窗户：上一格、下一格，左边一格窗框(暗)。"""
    c.px(x, y, WINDOW)
    c.px(x, y + 1, WINDOW)
    c.px(x - 1, y, WOOD[0])

def door(c, x, wall_bottom, w=2):
    """门：深色门洞，上面一道亮的门楣。"""
    c.rect(x, wall_bottom - 1, x + w - 1, wall_bottom, DARK)
    for k in range(-1, w + 1):
        c.px(x + k, wall_bottom - 2, WOOD[3])

def mound(c, x0, x1, base, top, mat=ROCK, specks=()):
    """岩石矿堆(原版画法)：几块大小不一的石头叠成一堆，每块左上亮、右下暗，石头之间有暗缝，表面点几颗亮斑和矿石。"""
    import random
    rng = random.Random(x0 * 131 + top * 17 + x1)
    cx, w, h = (x0 + x1) / 2, x1 - x0, base - top
    stones = []
    for k in range(9):
        r = rng.uniform(0.18, 0.32) * w
        sx = rng.uniform(x0 + r * 0.8, x1 - r * 0.8)
        # 越靠中间越高
        sy = base - r * 0.7 - (1 - abs(sx - cx) / (w / 2 + 1)) * h * rng.uniform(0.35, 0.75)
        stones.append((sy, sx, r))
    stones.sort()                                     # 先画后面(上面)的，前面的石头压在上面
    for sy, sx, r in stones:
        ry = r * 0.75
        for y in range(int(sy - ry) - 1, int(sy + ry) + 2):
            for x in range(int(sx - r) - 1, int(sx + r) + 2):
                d = ((x - sx) / r) ** 2 + ((y - sy) / ry) ** 2
                if d > 1.0 or y > base:
                    continue
                lit = (x - sx) / r + (y - sy) / ry
                col = mat[3] if lit < -0.7 else mat[2] if lit < 0.35 else mat[1]
                if d > 0.78 and lit > 0:
                    col = mat[0]
                c.px(x, y, col)
        c.px(round(sx - r * 0.4), round(sy - ry * 0.4), ROCK_HI)
    for x, y in specks:
        c.px(x, y, ORE)
        c.px(x + 1, y, (160, 101, 59))

def chimney(c, x, top, base, mat=STONE):
    """烟囱：一根方柱，顶面露一格亮色。"""
    c.rect(x, top, x + 1, base, mat[1])
    c.px(x, top, mat[3])
    c.px(x + 1, top, mat[2])


def tower(c, x0, x1, top, base, mat=CONCRETE):
    """塔楼/料仓(从斜上方看)：顶上王国色顶面，柱身左亮右暗，每隔三行一道环箍。"""
    c.rect(x0, top + 1, x1, base, mat[2])
    for y in range(top + 1, base + 1):
        c.px(x0, y, mat[3])
        c.px(x0 + 1, y, mat[3] if (y - top) % 3 else mat[2])
        c.px(x1, y, mat[1])
        if (y - top) % 3 == 0:
            for x in range(x0, x1 + 1):
                c.px(x, y, mat[1] if x > x0 else mat[2])
    c.rect(x0, top, x1, top + 1, ROOF[2])
    c.px(x0, top, ROOF[3])
    c.px(x1, top + 1, ROOF[1])

def wheel(c, cx, cy, r):
    """水车(侧对画面)：木轮 + 下面一汪水。"""
    for y in range(cy - r, cy + r + 1):
        for x in range(cx - r, cx + r + 1):
            d = (x - cx) ** 2 + (y - cy) ** 2
            if r * r - r * 1.6 <= d <= r * r + r * 0.6:
                c.px(x, y, WOOD[1])
    c.line(cx - r + 1, cy, cx + r - 1, cy, WOOD[2])
    c.line(cx, cy - r + 1, cx, cy + r - 1, WOOD[2])
    c.rect(cx - r - 1, cy + r - 1, cx + r + 1, cy + r, WATER[1])
    c.px(cx - r, cy + r - 1, WATER[2])


def log_pile(c, x, y, rows=2, cols=3):
    """原木堆(从斜上方看)：顶上露出木头的长条，前面是圆形截面。"""
    for r in range(rows):
        for k in range(cols - r):
            cx, cy = x + k * 3 + r, y - r * 2
            c.px(cx, cy - 1, WOOD[2])
            c.px(cx + 1, cy - 1, WOOD[2])
            c.rect(cx, cy, cx + 1, cy + 1, WOOD_HI)
            c.px(cx + 1, cy + 1, WOOD[2])


def plank_stack(c, x0, x1, y0, layers):
    """木板垛(从斜上方看)：顶面一块亮色，前面一层层板材。"""
    c.rect(x0, y0 - layers - 1, x1, y0 - layers, WOOD_HI)
    for k in range(layers):
        for x in range(x0, x1 + 1):
            c.px(x, y0 - k, WOOD[3] if k % 2 == 0 else WOOD[2])
        c.px(x1, y0 - k, WOOD[1])


def yard(c, x0, x1, top, base, ground=GRASS, fence=WOOD):
    """地面上的场地(俯视)：一块地面，四周一圈木栅栏(和原版仓库围栏同一画法)。"""
    c.rect(x0, top, x1, base, ground[1])
    for x in range(x0, x1 + 1):
        c.px(x, top, fence[3])
        c.px(x, base, fence[2])
    for y in range(top, base + 1):
        c.px(x0, y, fence[3])
        c.px(x1, y, fence[2])
    for x in range(x0, x1 + 1, 4):
        c.px(x, top - 1, fence[1])
        c.px(x, base + 1, fence[1])


def barrel(c, x, y):
    """木桶(俯视)：顶面一圈亮，桶身暗，中间一道铁箍。"""
    c.px(x, y, WOOD_HI); c.px(x + 1, y, WOOD[3])
    c.px(x, y + 1, WOOD[2]); c.px(x + 1, y + 1, WOOD[1])
    c.px(x, y + 2, IRON[3]); c.px(x + 1, y + 2, IRON[2])


def crate(c, x, y):
    """木箱(俯视)：顶面亮、正面暗，对角一道木条。"""
    c.rect(x, y, x + 2, y, WOOD_HI)
    c.rect(x, y + 1, x + 2, y + 2, WOOD[2])
    c.px(x + 1, y + 1, WOOD[3]); c.px(x + 2, y + 2, WOOD[1])


# ═════════════ 矿场 ═════════════
def mine_2():
    """矿井：原版风格的岩石矿堆，正面开一个矿洞，上面立木头井架，井架顶一片王国色遮棚，旁边一辆矿车。"""
    c = Canvas(22, 22)
    mound(c, 0, 21, 21, 8, specks=[(4, 15), (17, 17), (6, 18)])
    c.rect(9, 15, 12, 18, DARK)
    c.line(9, 14, 12, 14, WOOD[3])
    c.line(7, 14, 10, 4, WOOD[2])
    c.line(14, 14, 11, 4, WOOD[1])
    c.line(8, 10, 13, 10, WOOD[2])
    roof(c, 7, 14, 1, 4)
    c.line(11, 5, 11, 13, IRON[1])
    c.rect(15, 18, 19, 19, IRON[3])
    c.line(16, 17, 18, 17, ORE)
    c.px(15, 20, DARK)
    c.px(19, 20, DARK)
    return c


def mine_3():
    """水力矿场：矿堆前一座木板机房(王国色屋顶)，侧面水车带动提升。"""
    c = Canvas(24, 22)
    mound(c, 3, 23, 20, 6, specks=[(20, 15), (21, 17)])
    wt, wb = house(c, 9, 19, 5, 20)
    door(c, 13, wb)
    window(c, 11, wt)
    window(c, 17, wt)
    wheel(c, 5, 14, 4)
    c.line(9, 14, 5, 14, IRON[2])
    crate(c, 19, 18)
    return c


def mine_4():
    """蒸汽煤矿：砖砌机房(王国色屋顶)带石砌烟囱，铁井架与天轮，一堆黑煤。"""
    c = Canvas(26, 26)
    mound(c, 12, 25, 25, 17, mat=[(20, 20, 22), (33, 31, 28), (46, 54, 62), (77, 87, 98)])
    wt, wb = house(c, 1, 14, 9, 24, BRICK)
    chimney(c, 11, 2, 12)
    door(c, 6, wb, 3)
    window(c, 3, wt)
    window(c, 11, wt)
    c.line(16, 24, 19, 5, IRON[2])
    c.line(23, 24, 20, 5, IRON[1])
    c.line(17, 16, 22, 16, IRON[2])
    c.line(18, 10, 21, 10, IRON[2])
    for (x, y) in [(18, 3), (19, 2), (20, 2), (21, 3), (21, 4), (18, 4)]:
        c.px(x, y, IRON[3])
    c.line(19, 5, 14, 12, IRON[1])
    return c


def mine_5():
    """现代矿业工厂：俯视看到一大块混凝土平屋顶(王国色女儿墙、通风口)，正面一排亮窗；井塔，斜向传送带。"""
    c = Canvas(28, 28)
    flat_roof(c, 1, 17, 11, 20)
    front(c, 1, 17, 21, 26, CONCRETE)
    for x in range(3, 16, 3):
        window(c, x, 22)
        window(c, x, 24)
    door(c, 8, 25, 3)
    tower(c, 20, 25, 3, 26)
    for y in range(7, 25, 4):
        window(c, 22, y)
    c.line(15, 10, 20, 6, IRON[3])
    c.line(15, 11, 20, 7, IRON[1])
    return c


# ═════════════ 伐木场 ═════════════
def lumber_1():
    """伐木营：王国色帆布帐篷(从上往下看)，一堆原木，树桩上插着斧头。"""
    c = Canvas(20, 16)
    roof(c, 0, 9, 4, 13)
    c.rect(4, 11, 5, 13, DARK)
    log_pile(c, 10, 13, rows=3, cols=3)
    c.rect(16, 12, 17, 14, WOOD[2])
    c.px(16, 12, WOOD_HI)
    c.px(17, 12, WOOD_HI)
    c.line(17, 11, 18, 8, WOOD[3])
    c.px(19, 8, IRON[3])
    c.px(19, 9, IRON[3])
    return c


def lumber_2():
    """锯木坊：小木屋(王国色屋顶)，门前锯台架着原木，一垛木板。"""
    c = Canvas(22, 18)
    wt, wb = house(c, 1, 11, 2, 16)
    door(c, 5, wb)
    window(c, 3, wt)
    window(c, 9, wt)
    c.line(12, 17, 13, 13, WOOD[1])
    c.line(16, 17, 15, 13, WOOD[1])
    c.line(11, 12, 18, 12, WOOD[3])
    c.line(14, 10, 14, 14, IRON[3])
    plank_stack(c, 17, 21, 17, 3)
    barrel(c, 12, 14)
    return c


def lumber_3():
    """水力锯木厂：大木构厂房(王国色屋顶)，侧面水车，门口原木堆和木板。"""
    c = Canvas(24, 22)
    wt, wb = house(c, 7, 20, 1, 20)
    for x in (9, 13, 18):
        window(c, x, wt)
    door(c, 12, wb, 3)
    wheel(c, 3, 14, 4)
    log_pile(c, 16, 21, rows=1, cols=2)
    plank_stack(c, 21, 23, 21, 3)
    return c


def lumber_4():
    """蒸汽锯木厂：砖砌厂房(王国色屋顶)带烟囱，旁边敞棚(俯视的平顶)，一垛垛成材。"""
    c = Canvas(26, 24)
    wt, wb = house(c, 1, 14, 4, 23, BRICK)
    chimney(c, 11, 0, 8)
    for x in (3, 7, 11):
        window(c, x, wt)
    door(c, 6, wb, 3)
    flat_roof(c, 15, 25, 9, 14, mat=WOOD, trim=ROOF)
    c.line(16, 15, 16, 23, WOOD[2])
    c.line(24, 15, 24, 23, WOOD[1])
    plank_stack(c, 17, 23, 23, 4)
    crate(c, 21, 17)
    barrel(c, 18, 16)
    return c


def lumber_5():
    """现代木材加工厂：俯视的大块平屋顶厂房，圆柱料仓，门式吊臂吊着一捆木材，成捆板材。"""
    c = Canvas(28, 26)
    flat_roof(c, 1, 17, 9, 18)
    front(c, 1, 17, 19, 25, CONCRETE)
    for x in range(3, 16, 3):
        window(c, x, 20)
    door(c, 7, 24, 4)
    tower(c, 18, 22, 5, 22)
    c.line(26, 2, 26, 25, IRON[2])
    c.line(15, 2, 26, 2, IRON[3])
    c.line(16, 3, 16, 5, IRON[1])
    c.rect(15, 6, 17, 7, WOOD[3])
    plank_stack(c, 19, 27, 25, 2)
    crate(c, 23, 20)
    return c


# ═════════════ 畜牧 ═════════════
def sheep(c, x, y):
    c.rect(x, y, x + 2, y + 1, WOOL[2])
    c.px(x + 2, y + 1, WOOL[1])
    c.px(x + 3, y, WOOL[0])
    c.px(x, y + 2, DARK)
    c.px(x + 2, y + 2, DARK)


def cow(c, x, y):
    c.rect(x, y, x + 3, y + 1, WOOL[2])
    c.px(x + 1, y, DARK)
    c.px(x + 3, y + 1, DARK)
    c.px(x + 4, y, WOOD[2])
    c.px(x, y + 2, DARK)
    c.px(x + 3, y + 2, DARK)


def pasture():
    """牧场(大型)：俯视的一大片围栏草场(和原版仓库围栏同一画法)，左上角木板畜棚(王国色屋顶)，草垛，
    场里几只羊和一头牛。"""
    c = Canvas(36, 24)
    yard(c, 1, 34, 8, 22)
    for x in range(4, 34, 4):
        c.px(x, 11 + (x % 3) * 3, GRASS[2])
        c.px(x + 1, 13 + (x % 4) * 2, GRASS[3] if x % 8 == 4 else GRASS[2])
    wt, wb = house(c, 2, 12, 0, 13)
    c.rect(5, wb - 1, 8, wb, DARK)
    for (x, y) in [(15, 9), (16, 8), (17, 8), (18, 9), (14, 10), (15, 10), (16, 10), (17, 10), (18, 10), (19, 10)]:
        c.px(x, y, HAY[2])
    c.px(16, 9, HAY[3])
    sheep(c, 16, 14)
    sheep(c, 22, 17)
    sheep(c, 27, 12)
    cow(c, 18, 19)
    cow(c, 28, 18)
    barrel(c, 13, 11)
    return c


def slaughterhouse():
    """屠宰场：石墙屠房(王国色屋顶)带烟囱，门前晾皮架挂着两张兽皮，旁边木桶。"""
    c = Canvas(22, 20)
    wt, wb = house(c, 1, 13, 2, 18, STONE)
    chimney(c, 10, 0, 6)
    door(c, 5, wb, 3)
    window(c, 3, wt)
    window(c, 11, wt)
    c.line(15, 9, 15, 19, WOOD[1])
    c.line(21, 9, 21, 19, WOOD[1])
    c.line(15, 9, 21, 9, WOOD[3])
    c.rect(16, 10, 17, 14, WOOD_HI)
    c.rect(19, 10, 20, 13, WOOD[3])
    c.rect(17, 17, 18, 19, WOOD[2])
    c.px(17, 17, IRON[3])
    c.px(18, 17, IRON[3])
    return c


BUILDINGS = {
    "ec_mine_2": mine_2, "ec_mine_3": mine_3, "ec_mine_4": mine_4, "ec_mine_5": mine_5,
    "ec_lumber_1": lumber_1, "ec_lumber_2": lumber_2, "ec_lumber_3": lumber_3, "ec_lumber_4": lumber_4,
    "ec_lumber_5": lumber_5,
    "ec_pasture": pasture, "ec_slaughterhouse": slaughterhouse,
}


def ruin(main):
    """废墟：上半截塌掉，整体变灰变暗(王国色也褪掉，不会被换色)，留一些散落的残块。"""
    a = np.asarray(main).copy()
    h = a.shape[0]
    cut = h // 2
    rng = np.random.default_rng(7)
    for y in range(cut):
        keep = rng.random(a.shape[1]) < (y - cut * 0.6) / (cut * 0.4 + 1e-6)
        a[y, ~keep, 3] = 0
    rgb = a[..., :3].astype(np.float32)
    lum = rgb.mean(-1, keepdims=True)
    a[..., :3] = np.clip((rgb * 0.25 + lum * 0.75) * 0.6, 0, 255).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def construction(w, h):
    """施工：石基 + 木脚手架(原版施工图的样子)。"""
    c = Canvas(max(12, w - 6), max(9, h // 2))
    W, H = c.w, c.h
    for x in range(W):
        c.px(x, H - 1, STONE[2] if x % 3 else STONE[1])
    for x in range(1, W, 4):
        c.line(x, 1, x, H - 2, WOOD[2])
    for y in range(2, H - 1, 3):
        c.line(1, y, W - 2, y, WOOD[1])
    c.line(1, H - 2, W - 2, 1, WOOD[3])
    return c.image()


def mini(main):
    a = np.asarray(main).astype(np.float32)
    solid = a[..., 3] > 0
    col = a[solid][:, :3].mean(0) if solid.any() else np.array([128, 128, 128])
    m = np.zeros((3, 3, 4), np.uint8)
    m[..., :3] = col.astype(np.uint8)
    m[..., 3] = 255
    return Image.fromarray(m, "RGBA")


def build():
    out = {}
    for bid, fn in BUILDINGS.items():
        main = fn().image()
        out[bid] = {"main_0": main, "ruin_0": ruin(main), "construction_0": construction(*main.size), "mini_0": mini(main)}
    return out


def preview(sprites, path):
    """预览：上排建成(品红换成示例王国色)，下排废墟；左边并排一座原版民居尺寸的参照框。"""
    k = 6
    cell = 38 * k
    names = list(sprites)
    sheet = Image.new("RGBA", (cell * len(names), cell * 2 + 8), (214, 170, 60, 255))
    kingdom = np.array([58, 110, 190], np.float32)
    for i, bid in enumerate(names):
        m = np.asarray(sprites[bid]["main_0"]).copy()
        mag = (m[..., 0] > 60) & (m[..., 1] < 10) & (m[..., 2] > 60) & (m[..., 3] > 0)
        m[mag, :3] = np.clip(kingdom * (m[mag, 0:1].astype(np.float32) / 222.0), 0, 255).astype(np.uint8)
        m = Image.fromarray(m, "RGBA")
        sheet.alpha_composite(m.resize((m.width * k, m.height * k), Image.NEAREST),
                              (i * cell + (cell - m.width * k) // 2, cell - m.height * k))
        r = sprites[bid]["ruin_0"]
        sheet.alpha_composite(r.resize((r.width * k, r.height * k), Image.NEAREST),
                              (i * cell + (cell - r.width * k) // 2, cell * 2 + 8 - r.height * k))
    sheet.save(path)


if __name__ == "__main__":
    sprites = build()
    for bid, files in sprites.items():
        folder = os.path.join(OUT, bid)
        os.makedirs(folder, exist_ok=True)
        for name, im in files.items():
            im.save(os.path.join(folder, name + ".png"))
    if "--preview" in sys.argv:
        preview(sprites, os.path.join(HERE, "building_preview.png"))
    print("wrote", len(sprites))
