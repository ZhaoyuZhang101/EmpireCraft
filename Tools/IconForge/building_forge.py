"""EmpireCraft 建筑锻造器 —— 矿场、伐木场分级的地图建筑像素图(与原版建筑同尺度，1 像素 = 1 格)。

  · 矿场 ec_mine_2~5：矿井(木架井口) → 水力矿场(水车) → 蒸汽煤矿(砖砌机房、烟囱、铁井架) → 现代矿业工厂(钢构厂房、井塔、传送带)；
  · 牧场 ec_pasture(大型)、屠宰场 ec_slaughterhouse；
  · 伐木场 ec_lumber_1~5：伐木营(帐篷、原木堆) → 锯木坊(木屋、锯台) → 水力锯木厂(水车) → 蒸汽锯木厂(砖房、烟囱) → 现代木材加工厂(钢构厂房、料仓、吊臂)。

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

# 调色(原版建筑那种略降饱和的明快色)
OUTLINE = (34, 28, 26)
ROCK = (122, 112, 100)
ROCK_D = (92, 84, 76)
DIRT = (118, 88, 60)
WOOD = (150, 104, 62)
WOOD_D = (112, 76, 44)
WOOD_L = (184, 138, 88)
PLANK = (196, 160, 106)
LOGEND = (210, 176, 120)
CANVAS = (214, 200, 160)
CANVAS_D = (176, 160, 122)
STONE = (164, 160, 150)
STONE_D = (126, 122, 114)
BRICK = (156, 78, 58)
BRICK_D = (118, 56, 44)
IRON = (70, 72, 78)
IRON_L = (104, 108, 116)
STEEL = (128, 140, 150)
STEEL_D = (94, 104, 114)
STEEL_L = (170, 182, 190)
ROOF_R = (150, 60, 50)
ROOF_R_D = (112, 42, 36)
ROOF_B = (78, 100, 122)
ROOF_B_D = (58, 76, 96)
COAL = (40, 40, 44)
COAL_L = (64, 64, 70)
ORE = (196, 150, 70)
WATER = (80, 132, 196)
WATER_L = (130, 176, 222)
GLASS = (230, 206, 120)
LEAF = (78, 122, 62)
SAFETY = (216, 176, 52)


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.a = np.zeros((h, w, 4), np.uint8)

    def px(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.a[y, x, :3] = c
            self.a[y, x, 3] = 255

    def rect(self, x0, y0, x1, y1, c, shade=None):
        """含端点的矩形；shade 给出右侧一列暗色。"""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.px(x, y, c)
        if shade is not None:
            for y in range(y0, y1 + 1):
                self.px(x1, y, shade)

    def hline(self, x0, x1, y, c):
        for x in range(x0, x1 + 1):
            self.px(x, y, c)

    def vline(self, x, y0, y1, c):
        for y in range(y0, y1 + 1):
            self.px(x, y, c)

    def line(self, x0, y0, x1, y1, c):
        n = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(n + 1):
            self.px(round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n), c)

    def tri(self, x0, x1, base, apex_y, c, shade=None):
        """等腰三角形屋顶：底边 base 行从 x0 到 x1，顶点在 apex_y。"""
        mid = (x0 + x1) / 2
        for y in range(apex_y, base + 1):
            t = (y - apex_y) / max(1, base - apex_y)
            l, r = round(mid - (mid - x0) * t), round(mid + (x1 - mid) * t)
            for x in range(l, r + 1):
                self.px(x, y, shade if shade is not None and x > mid else c)

    def disc(self, cx, cy, r, c):
        for y in range(cy - r, cy + r + 1):
            for x in range(cx - r, cx + r + 1):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r + r * 0.6:
                    self.px(x, y, c)

    def ring(self, cx, cy, r, c, spokes=None):
        for y in range(cy - r, cy + r + 1):
            for x in range(cx - r, cx + r + 1):
                d = (x - cx) ** 2 + (y - cy) ** 2
                if r * r - r * 1.6 <= d <= r * r + r * 0.6:
                    self.px(x, y, c)
        if spokes:
            self.line(cx - r, cy, cx + r, cy, spokes)
            self.line(cx, cy - r, cx, cy + r, spokes)

    def image(self, outline=True):
        a = self.a.copy()
        if outline:
            solid = a[..., 3] > 0
            pad = np.pad(solid, 1)
            near = pad[:-2, 1:-1] | pad[2:, 1:-1] | pad[1:-1, :-2] | pad[1:-1, 2:]
            edge = near & ~solid
            a[edge, :3] = OUTLINE
            a[edge, 3] = 255
        return Image.fromarray(a, "RGBA")


def mound(c, x0, x1, base, top, col=ROCK, dark=ROCK_D):
    """碎石矿堆：半椭圆，右下偏暗，点几颗矿石。"""
    cx, rx, ry = (x0 + x1) / 2, (x1 - x0) / 2, base - top
    for y in range(top, base + 1):
        for x in range(x0, x1 + 1):
            if ((x - cx) / rx) ** 2 + ((base - y) / ry) ** 2 <= 1.0:
                c.px(x, y, dark if (x - cx) > rx * 0.3 or y > base - 1 else col)


def log_pile(c, x, y, rows=2, cols=3):
    """原木堆：圆形截面朝外，一排排码起来。"""
    for r in range(rows):
        for k in range(cols - r):
            cx = x + k * 3 + r * 1
            cy = y - r * 2
            c.rect(cx, cy, cx + 1, cy + 1, LOGEND)
            c.px(cx + 1, cy + 1, WOOD)


def plank_stack(c, x0, x1, y0, layers):
    for k in range(layers):
        c.hline(x0, x1, y0 - k, PLANK if k % 2 == 0 else WOOD_L)
    c.vline(x1, y0 - layers + 1, y0, WOOD_D)


def waterwheel(c, cx, cy, r):
    c.rect(cx - r, cy + r - 1, cx + r, cy + r, WATER)
    c.hline(cx - r, cx - r + 2, cy + r - 1, WATER_L)
    c.ring(cx, cy, r, WOOD_D, spokes=WOOD)
    c.px(cx, cy, IRON)


def chimney(c, x, top, base, col=BRICK, dark=BRICK_D, w=2):
    c.rect(x, top, x + w - 1, base, col, shade=dark)
    c.hline(x - 1, x + w, top, dark)


def windows(c, x0, x1, y, step=3, col=GLASS):
    for x in range(x0, x1 + 1, step):
        c.px(x, y, col)


# ═════════════ 矿场 ═════════════
def mine_2():
    """矿井：碎石矿堆上立着木头 A 字井架，井口挂着滑轮，旁边一辆矿车。"""
    c = Canvas(20, 18)
    mound(c, 0, 19, 17, 9)
    c.rect(8, 12, 11, 14, COAL)                      # 井口
    c.line(6, 14, 10, 2, WOOD)                       # 井架两腿
    c.line(13, 14, 10, 2, WOOD_D)
    c.hline(7, 13, 8, WOOD)                          # 横撑
    c.disc(10, 3, 1, IRON_L)                         # 滑轮
    c.vline(10, 4, 12, IRON)                         # 绳
    c.rect(14, 13, 17, 14, IRON_L, shade=IRON)       # 矿车
    c.hline(15, 16, 12, ORE)
    c.px(14, 15, COAL); c.px(17, 15, COAL)
    c.px(3, 14, ORE); c.px(5, 15, ORE)
    return c


def mine_3():
    """水力矿场：矿堆、木板机房(红瓦顶)，侧面水车带动提升，门口矿石堆。"""
    c = Canvas(22, 20)
    mound(c, 0, 21, 19, 11)
    c.rect(7, 9, 16, 17, WOOD, shade=WOOD_D)         # 机房
    for x in range(8, 16, 2):
        c.vline(x, 9, 17, WOOD_L)
    c.tri(6, 17, 9, 3, ROOF_R, shade=ROOF_R_D)       # 屋顶
    c.rect(10, 13, 12, 17, COAL)                     # 门
    waterwheel(c, 4, 13, 4)
    c.line(8, 13, 4, 13, IRON)                       # 传动轴
    c.rect(17, 16, 20, 17, ORE)                      # 矿石堆
    c.px(18, 15, ORE); c.px(19, 15, ORE)
    return c


def mine_4():
    """蒸汽煤矿：砖砌机房带高烟囱，铁井架与天轮，旁边一座煤堆。"""
    c = Canvas(24, 24)
    mound(c, 0, 23, 23, 17, COAL_L, COAL)            # 煤渣地
    c.rect(2, 13, 12, 22, BRICK, shade=BRICK_D)      # 机房
    for y in (15, 18):
        windows(c, 4, 10, y, 3)
    c.tri(1, 13, 13, 8, STONE, shade=STONE_D)        # 石板顶
    chimney(c, 4, 1, 10)
    c.line(15, 22, 18, 4, IRON_L)                    # 铁井架
    c.line(22, 22, 18, 4, IRON)
    c.line(16, 15, 21, 15, IRON)
    c.line(17, 10, 20, 10, IRON)
    c.ring(18, 4, 2, IRON_L, spokes=IRON)            # 天轮
    c.line(18, 6, 12, 14, IRON)                      # 钢缆
    mound(c, 14, 23, 22, 18, COAL_L, COAL)           # 煤堆
    return c


def mine_5():
    """现代矿业工厂：钢构锯齿顶厂房，高耸的钢井塔，斜向传送带接到料仓。"""
    c = Canvas(26, 26)
    c.rect(1, 15, 16, 25, STEEL, shade=STEEL_D)      # 厂房
    for k in range(4):                               # 锯齿屋顶
        x = 1 + k * 4
        c.line(x, 15, x + 3, 12, STEEL_L)
        c.vline(x + 3, 12, 15, STEEL_D)
    windows(c, 3, 14, 18, 2, GLASS)
    windows(c, 3, 14, 21, 2, GLASS)
    c.rect(6, 22, 9, 25, IRON)                       # 卷帘门
    c.rect(19, 3, 24, 25, STEEL_D, shade=IRON)       # 井塔
    for y in range(5, 24, 4):
        c.hline(19, 23, y, STEEL_L)
    c.rect(18, 1, 25, 3, ROOF_B, shade=ROOF_B_D)     # 塔顶机房
    c.vline(21, 0, 0, SAFETY)
    c.line(12, 12, 19, 6, IRON_L)                    # 传送带
    c.line(12, 13, 19, 7, IRON)
    c.rect(10, 7, 13, 12, SAFETY, shade=(170, 134, 36))  # 料仓
    c.px(11, 6, SAFETY); c.px(12, 6, SAFETY)
    return c


# ═════════════ 伐木场 ═════════════
def lumber_1():
    """伐木营：帆布帐篷，一堆原木，树桩上插着斧头。"""
    c = Canvas(18, 14)
    c.hline(0, 17, 13, DIRT)
    c.tri(0, 8, 12, 4, CANVAS, shade=CANVAS_D)        # 帐篷
    c.vline(4, 9, 12, WOOD_D)                         # 帐门
    log_pile(c, 9, 11, rows=3, cols=3)
    c.rect(15, 10, 16, 12, WOOD, shade=WOOD_D)        # 树桩
    c.hline(15, 16, 10, LOGEND)
    c.line(16, 9, 17, 7, WOOD_L)                      # 斧柄
    c.px(17, 6, IRON_L)
    return c


def lumber_2():
    """锯木坊：木屋(坡顶)，门前锯台上架着一根原木，一摞木板。"""
    c = Canvas(20, 16)
    c.hline(0, 19, 15, DIRT)
    c.rect(1, 8, 9, 14, WOOD, shade=WOOD_D)           # 木屋
    for x in range(2, 9, 2):
        c.vline(x, 8, 14, WOOD_L)
    c.tri(0, 10, 8, 3, WOOD_D, shade=(90, 60, 34))    # 屋顶
    c.rect(4, 11, 6, 14, COAL)                        # 门
    c.line(11, 14, 12, 11, WOOD_D)                    # 锯台
    c.line(15, 14, 14, 11, WOOD_D)
    c.hline(10, 17, 10, WOOD)                         # 原木
    c.px(10, 10, LOGEND)
    c.line(13, 9, 13, 12, IRON_L)                     # 锯
    plank_stack(c, 15, 19, 14, 3)
    return c


def lumber_3():
    """水力锯木厂：两层木构厂房(红瓦)，侧面水车，门口原木堆。"""
    c = Canvas(22, 19)
    c.hline(0, 21, 18, DIRT)
    c.rect(7, 7, 18, 17, WOOD, shade=WOOD_D)
    c.hline(7, 18, 11, WOOD_D)
    windows(c, 9, 16, 9, 3, GLASS)
    c.tri(6, 19, 7, 1, ROOF_R, shade=ROOF_R_D)
    c.rect(11, 13, 14, 17, COAL)
    waterwheel(c, 4, 12, 4)
    log_pile(c, 15, 17, rows=2, cols=2)
    plank_stack(c, 19, 21, 17, 4)
    return c


def lumber_4():
    """蒸汽锯木厂：砖砌厂房带烟囱，侧面木料棚，一垛垛成材。"""
    c = Canvas(24, 22)
    c.hline(0, 23, 21, DIRT)
    c.rect(1, 9, 13, 20, BRICK, shade=BRICK_D)
    windows(c, 3, 11, 12, 2, GLASS)
    windows(c, 3, 11, 16, 2, GLASS)
    c.tri(0, 14, 9, 4, STONE, shade=STONE_D)
    chimney(c, 10, 0, 7)
    c.rect(15, 12, 22, 13, WOOD_D)                    # 棚顶
    c.vline(15, 14, 20, WOOD); c.vline(22, 14, 20, WOOD)
    plank_stack(c, 16, 21, 20, 5)
    log_pile(c, 3, 20, rows=1, cols=3)
    return c


def lumber_5():
    """现代木材加工厂：钢构厂房(蓝灰顶)，圆柱料仓，门式吊臂吊着一捆木材，成捆板材堆场。"""
    c = Canvas(26, 24)
    c.hline(0, 25, 23, STONE_D)
    c.rect(1, 11, 15, 22, STEEL, shade=STEEL_D)
    c.rect(0, 9, 16, 11, ROOF_B, shade=ROOF_B_D)
    windows(c, 3, 13, 14, 2, GLASS)
    c.rect(5, 17, 9, 22, IRON)
    c.rect(17, 5, 21, 18, STEEL_L, shade=STEEL_D)     # 料仓
    c.hline(17, 21, 4, STEEL_D)
    c.line(19, 18, 19, 22, IRON)
    c.vline(24, 2, 22, SAFETY)                        # 吊臂
    c.hline(14, 24, 2, SAFETY)
    c.vline(15, 3, 5, IRON)
    c.rect(14, 6, 16, 7, PLANK)
    plank_stack(c, 18, 25, 22, 3)
    return c


# ═════════════ 畜牧 ═════════════
GRASS = (112, 150, 70)
GRASS_D = (90, 124, 56)
WOOL = (232, 228, 214)
HIDE = (150, 104, 70)
HAY = (214, 182, 92)
HAY_D = (176, 144, 64)


def sheep(c, x, y):
    c.rect(x, y, x + 2, y + 1, WOOL)
    c.px(x + 3, y, OUTLINE)
    c.px(x, y + 2, OUTLINE); c.px(x + 2, y + 2, OUTLINE)


def cow(c, x, y):
    c.rect(x, y, x + 3, y + 1, WOOL)
    c.px(x + 1, y, OUTLINE); c.px(x + 3, y + 1, OUTLINE)
    c.px(x + 4, y, WOOD_D)
    c.px(x, y + 2, OUTLINE); c.px(x + 3, y + 2, OUTLINE)


def pasture():
    """牧场(大型)：一大片围栏草场，左侧红顶畜棚，草垛，场里几只羊和一头牛。"""
    c = Canvas(36, 22)
    c.rect(1, 12, 34, 21, GRASS)
    for x in range(2, 34, 5):
        c.px(x, 15 + (x % 3), GRASS_D)
    for x in range(1, 35, 4):                            # 围栏立柱
        c.vline(x, 11, 13, WOOD_D)
        c.vline(x, 19, 21, WOOD_D)
    c.hline(1, 34, 12, WOOD_L); c.hline(1, 34, 20, WOOD_L)
    c.vline(34, 12, 20, WOOD_L); c.vline(1, 12, 20, WOOD_L)
    c.rect(2, 5, 11, 14, ROOF_R, shade=ROOF_R_D)         # 畜棚
    c.tri(1, 12, 6, 0, ROOF_R_D, shade=(90, 32, 28))
    c.rect(5, 9, 8, 14, WOOD_D)
    c.line(5, 9, 8, 14, WOOD_L); c.line(8, 9, 5, 14, WOOD_L)
    c.disc(14, 10, 2, HAY); c.px(15, 11, HAY_D); c.px(13, 8, HAY_D)   # 草垛
    sheep(c, 16, 15); sheep(c, 22, 17); sheep(c, 27, 14)
    cow(c, 18, 18); cow(c, 29, 17)
    return c


def slaughterhouse():
    """屠宰场：石墙木顶的屠房，门前晾皮架挂着两张兽皮，旁边木桶。"""
    c = Canvas(22, 18)
    c.hline(0, 21, 17, DIRT)
    c.rect(1, 7, 12, 16, STONE, shade=STONE_D)
    for y in (9, 12, 15):
        c.hline(1, 11, y, STONE_D)
    c.tri(0, 13, 7, 2, WOOD_D, shade=(90, 60, 34))
    c.rect(5, 11, 8, 16, COAL)
    chimney(c, 10, 0, 5, STONE, STONE_D)
    c.vline(14, 8, 16, WOOD_D); c.vline(20, 8, 16, WOOD_D)  # 晾皮架
    c.hline(14, 20, 8, WOOD)
    c.rect(15, 9, 16, 13, HIDE); c.rect(18, 9, 19, 12, HIDE)
    c.px(15, 14, HIDE); c.px(19, 13, HIDE)
    c.rect(17, 15, 18, 16, WOOD, shade=WOOD_D)              # 木桶
    c.hline(17, 18, 15, IRON_L)
    return c


BUILDINGS = {
    "ec_mine_2": mine_2, "ec_mine_3": mine_3, "ec_mine_4": mine_4, "ec_mine_5": mine_5,
    "ec_lumber_1": lumber_1, "ec_lumber_2": lumber_2, "ec_lumber_3": lumber_3, "ec_lumber_4": lumber_4,
    "ec_lumber_5": lumber_5,
    "ec_pasture": pasture, "ec_slaughterhouse": slaughterhouse,
}


def ruin(main):
    """废墟：上半截塌掉，整体变灰变暗，留一些散落的残块。"""
    a = np.asarray(main).copy()
    h = a.shape[0]
    cut = h // 2
    rng = np.random.default_rng(7)
    for y in range(cut):
        keep = rng.random(a.shape[1]) < (y - cut * 0.6) / (cut * 0.4 + 1e-6)
        a[y, ~keep, 3] = 0
    rgb = a[..., :3].astype(np.float32)
    lum = rgb.mean(-1, keepdims=True)
    a[..., :3] = np.clip((rgb * 0.35 + lum * 0.65) * 0.7, 0, 255).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def construction(w, h):
    """施工：地基 + 木脚手架。"""
    c = Canvas(max(12, w - 4), max(10, h // 2 + 2))
    W, H = c.w, c.h
    c.rect(0, H - 2, W - 1, H - 1, STONE, shade=STONE_D)
    for x in range(1, W, 4):
        c.vline(x, 1, H - 3, WOOD)
    for y in range(2, H - 2, 3):
        c.hline(1, W - 2, y, WOOD_D)
    c.line(1, H - 3, W - 2, 1, WOOD_L)
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


if __name__ == "__main__":
    sprites = build()
    for bid, files in sprites.items():
        folder = os.path.join(OUT, bid)
        os.makedirs(folder, exist_ok=True)
        for name, im in files.items():
            im.save(os.path.join(folder, name + ".png"))
    if "--preview" in sys.argv:
        k = 8
        cell = 38 * k
        names = list(sprites)
        sheet = Image.new("RGBA", (cell * len(names), cell * 2 + 8), (92, 128, 70, 255))
        for i, bid in enumerate(names):
            m = sprites[bid]["main_0"]
            sheet.alpha_composite(m.resize((m.width * k, m.height * k), Image.NEAREST),
                                  (i * cell + (cell - m.width * k) // 2, cell - m.height * k))
            r = sprites[bid]["ruin_0"]
            sheet.alpha_composite(r.resize((r.width * k, r.height * k), Image.NEAREST),
                                  (i * cell + (cell - r.width * k) // 2, cell * 2 + 8 - r.height * k))
        sheet.save(os.path.join(HERE, "building_preview.png"))
    print("wrote", len(sprites))
