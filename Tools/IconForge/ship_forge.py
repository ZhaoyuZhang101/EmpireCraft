"""海军舰船贴图：体素建模 → 按原版船的视角渲染 8 个航向 × 2 帧(浪花与旗帜摆动)，外加 normal 与 broken。

原版船的贴图格式(actors/boats/<单位编号>)：每帧 31×34，锚点 (0.5, 0.2)，帧名
  -90@0 -45@0 0@0 45@0 90@0 135@0 180@0 -135@0 normal / 同名 @1 与 broken；
角度是航向：0 向右(东)、90 向上(北，远离观者)、-90 向下(朝观者)、180 向左。
旗帜用品红 (255,0,255) 与暗品红 (127,0,127)，游戏会换成国家颜色。

四个舰种(NavySystem 的舰种，单位编号 ec_warship_0..3)：
  0 楼船      木壳、三层楼阁(朱漆柱、黛瓦檐)、船首竹篾帆；
  1 远洋帆舰  三桅白帆、船艉楼、金饰条与炮窗；
  2 蒸汽铁甲  铁灰船身、红色水线、烟囱(红箍、冒烟)、前后炮塔；
  3 现代舰艇  灰色舰身、阶梯式上层建筑与舰桥、雷达桅、前主炮、后烟囱。
兵种兵牌 ui/icons/corps/arm_<兵种>(步兵、弓兵、车兵、骑兵、火枪、步枪、炮兵、装甲车、坦克、直升机、战斗机、轰炸机、民兵，见 ARMS)；
另出地图兵牌用的船形小图标 ui/icons/corps/ship_0..3(13×14 白色剪影 + 暗描边，与棋子同风格，按国色染色)；
运兵船兵牌(渡海中的军团)用 ui/icons/corps/troopship_<棋子>：小号棋子站在船上，与舰队的船形图标区分开。

python Tools/IconForge/ship_forge.py --preview   # 输出 ship_preview.png(放大)
python Tools/IconForge/ship_forge.py --write     # 写入 GameResources
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
W, H = 31, 34
PIVOT = (15.5, 27.2)
S = 8          # 渲染超采样倍数
K = 0.45       # 纵深(向北)在屏幕上的压缩
SCALE = 1.15   # 一格体素在屏幕上的像素数(原版船大致占满 31×34)
ANGLES = [-90, -45, 0, 45, 90, 135, 180, -135]

# 材质 → (顶面, 侧面) 颜色；侧面再按高度略暗
MAT = {
    "hull": ((125, 66, 27), (96, 52, 24)),
    "hull_dark": ((75, 46, 27), (58, 36, 21)),
    "deck": ((192, 140, 80), (150, 104, 56)),
    "gold": ((251, 211, 82), (226, 176, 56)),
    "lacquer": ((196, 58, 44), (150, 40, 30)),
    "roof": ((70, 78, 84), (48, 54, 60)),
    "sail": ((226, 230, 214), (190, 198, 182)),
    "sail_shade": ((198, 206, 190), (167, 183, 168)),
    "mat": ((214, 178, 116), (176, 140, 84)),
    "mast": ((90, 56, 30), (70, 42, 22)),
    "port": ((30, 24, 20), (20, 16, 14)),
    "iron": ((112, 118, 124), (84, 88, 94)),
    "iron_dark": ((70, 74, 80), (52, 56, 60)),
    "redline": ((150, 40, 30), (111, 29, 0)),
    "funnel": ((48, 48, 48), (34, 34, 34)),
    "smoke": ((205, 205, 200), (170, 170, 166)),
    "grey": ((150, 158, 166), (120, 128, 136)),
    "grey_light": ((184, 192, 198), (150, 158, 166)),
    "window": ((40, 52, 64), (28, 36, 46)),
    "flag": ((255, 0, 255), (127, 0, 127)),
}
FOAM = (213, 226, 234, 255)
FOAM_DARK = (167, 183, 196, 255)


def hull(model, length, beam, height, mat="hull", deck="deck", top_mat=None, bow=0.35, stern=0.1):
    half = length / 2
    for xi in range(length):
        x = xi - half + 0.5
        t = (x + half) / length          # 0 船艉 → 1 船首
        w = beam / 2
        if t > 1 - bow:
            w *= max(0.15, (1 - t) / bow)
        elif t < stern:
            w *= 0.75 + 0.25 * t / stern
        for z in range(height):
            wz = w * (0.7 + 0.3 * z / max(1, height - 1))
            for yi in range(-int(beam), int(beam) + 1):
                y = yi * 0.5
                if abs(y) <= wz:
                    m = deck if z == height - 1 else (top_mat if top_mat and z == height - 2 else mat)
                    model[(round(x * 2) / 2, y, z)] = m


def box(model, x0, x1, y0, y1, z0, z1, mat):
    x = x0
    while x <= x1:
        y = y0
        while y <= y1:
            for z in range(z0, z1 + 1):
                model[(x, y, z)] = mat
            y += 0.5
        x += 0.5


def ship(tier, frame):
    m = {}
    if tier == 0:  # 楼船：宽木壳，三层楼阁(朱漆柱、黛瓦檐)层层收分，船首竹篾帆，艉立旗
        hull(m, 22, 9, 4, mat="hull", deck="deck", top_mat="hull_dark")
        box(m, -7, 6, -3.5, 3.5, 4, 6, "lacquer")
        box(m, -6.5, 5.5, -3, 3, 4, 6, "hull_dark")
        box(m, -8, 7, -4.5, 4.5, 7, 7, "roof")
        box(m, -5.5, 4, -2.5, 2.5, 8, 9, "lacquer")
        box(m, -5, 3.5, -2, 2, 8, 9, "hull_dark")
        box(m, -6.5, 5, -3.5, 3.5, 10, 10, "roof")
        box(m, -3.5, 1.5, -1.5, 1.5, 11, 12, "lacquer")
        box(m, -4.5, 2.5, -2.5, 2.5, 13, 13, "roof")
        box(m, 8.5, 8.5, 0, 0, 4, 14, "mast")
        box(m, 7.5, 9.5, -2, 2, 7, 13, "mat")
        box(m, -10, -10, 0, 0, 4, 17, "mast")
        fx = -10.5 if frame == 0 else -11
        box(m, fx - 2.5, fx, 0, 0, 15, 17, "flag")
    elif tier == 1:  # 远洋帆舰：三桅鼓帆、船艉楼、金饰条、两排炮窗
        hull(m, 24, 9, 5, mat="hull", deck="deck", top_mat="gold")
        box(m, -11.5, -7, -3.5, 3.5, 5, 7, "hull")
        box(m, -11.5, -7, -3.5, 3.5, 8, 8, "gold")
        for gx in (-5, -2, 1, 4):
            for gy in (-4.5, 4.5):
                m[(gx, gy, 2)] = "port"
                m[(gx + 1.5, gy, 3)] = "port"
        for i, (mx, low, top) in enumerate(((7, 7, 15), (1, 7, 17), (-5, 9, 15))):
            box(m, mx, mx, 0, 0, 5, top, "mast")
            box(m, mx - 1.5, mx + 1.5, -2.5, 2.5, low, top - 2, "sail" if i % 2 == 0 else "sail_shade")
            box(m, mx - 2, mx + 2, 0, 0, top - 1, top - 1, "mast")
        fx = 0.5 if frame == 0 else 0
        box(m, fx - 2.5, fx, 0, 0, 17, 18, "flag")
    elif tier == 2:  # 蒸汽铁甲：铁灰船身、红色水线、上层建筑、烟囱(红箍冒烟)、前后炮塔
        hull(m, 24, 8, 4, mat="iron_dark", deck="iron", bow=0.3)
        for (x, y, z), v in list(m.items()):
            if z <= 1:
                m[(x, y, z)] = "redline"
        box(m, -4, 3.5, -2.5, 2.5, 4, 6, "iron")
        box(m, -0.5, 1.5, -1.5, 1.5, 7, 12, "funnel")
        box(m, -0.5, 1.5, -1.5, 1.5, 11, 11, "redline")
        sz = 13 if frame == 0 else 14
        box(m, -1, 2, -1.5, 1.5, sz, sz + 1, "smoke")
        box(m, -3 if frame == 0 else -4, -1, -1, 1, sz + 2, sz + 2, "smoke")
        box(m, 5.5, 8.5, -2, 2, 4, 5, "iron_dark")
        box(m, 9, 12, 0, 0, 5, 5, "funnel")
        box(m, -8.5, -5.5, -2, 2, 4, 5, "iron_dark")
        box(m, -12, -9, 0, 0, 5, 5, "funnel")
        box(m, -5, -5, 0, 0, 7, 15, "mast")
        box(m, -7.5 if frame == 0 else -8, -5.5, 0, 0, 14, 15, "flag")
    else:  # 现代舰艇：灰色舰身、阶梯上层建筑与舰桥窗、雷达桅、烟囱、前主炮与导弹格
        hull(m, 26, 8, 4, mat="grey", deck="grey_light", bow=0.45)
        for (x, y, z), v in list(m.items()):
            if z == 0:
                m[(x, y, z)] = "iron_dark"
        box(m, -7, 3.5, -2.5, 2.5, 4, 6, "grey")
        box(m, -2.5, 3.5, -2, 2, 7, 8, "grey_light")
        box(m, 3, 3.5, -2, 2, 8, 8, "window")
        box(m, 0, 0, 0, 0, 9, 16, "iron_dark")
        box(m, 0, 0, -2, 2, 14, 14, "iron_dark")
        box(m, -6, -4, -1.5, 1.5, 7, 10, "funnel")
        box(m, 7, 9, -1.5, 1.5, 4, 5, "grey_light")
        box(m, 9.5, 12.5, 0, 0, 5, 5, "funnel")
        box(m, 4.5, 6, -1.5, 1.5, 4, 4, "window")
        box(m, -11, -8.5, -1.5, 1.5, 4, 4, "grey_light")
        box(m, -2.5 if frame == 0 else -3, -0.5, 0, 0, 15, 16, "flag")
    return m


def shade(color, factor):
    return tuple(max(0, min(255, int(c * factor))) for c in color) + (255,)


def render(tier, angle, frame, wake=True, broken=False):
    """返回 31×34 RGBA。"""
    model = ship(tier, frame)
    if broken:
        model = {k: v for k, v in model.items() if v not in ("flag", "smoke", "sail", "mat") or k[2] < 8}
    rad = math.radians(angle)
    ca, sa = math.cos(rad), math.sin(rad)
    big = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    px, py = PIVOT[0] * S, PIVOT[1] * S

    def project(x, y, z):
        X = x * ca - y * sa
        Y = x * sa + y * ca
        return px + X * S * SCALE, py - (Y * K + z) * S * SCALE

    # 浪花：吃水线(z=0)船体一圈往外扩，两帧错开
    if wake and not broken:
        foot = [(x, y) for (x, y, z) in model if z == 0]
        spread = 1.2 if frame == 0 else 1.6
        for (x, y) in foot:
            for dx, dy in ((spread, 0), (-spread, 0), (0, spread), (0, -spread), (-spread * 1.6, 0)):
                sx, sy = project(x + dx * (1.5 if dx < 0 else 1), y + dy, -0.2)
                draw.rectangle([sx - S * 0.6, sy - S * 0.3, sx + S * 0.6, sy + S * 0.3], fill=FOAM if frame == 0 or dx < 0 else FOAM_DARK)

    order = sorted(model.items(), key=lambda item: (-(item[0][0] * sa + item[0][1] * ca), item[0][2]))
    for (x, y, z), mat in order:
        top, side = MAT[mat]
        h = 0.5
        corners = [(x - h, y - h), (x + h, y - h), (x + h, y + h), (x - h, y + h)]
        # 朝观者(世界 Y 负方向)的竖面
        for i in range(4):
            a, b = corners[i], corners[(i + 1) % 4]
            nx, ny = (b[1] - a[1]), -(b[0] - a[0])     # 外法线(局部)
            wy = nx * sa + ny * ca
            if wy >= -1e-6:
                continue
            factor = 0.8 + 0.15 * min(1, z / 8)
            poly = [project(a[0], a[1], z - 0.5), project(b[0], b[1], z - 0.5),
                    project(b[0], b[1], z + 0.5), project(a[0], a[1], z + 0.5)]
            draw.polygon(poly, fill=shade(side, factor))
        draw.polygon([project(cx, cy, z + 0.5) for cx, cy in corners], fill=shade(top, 1.0))

    small = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for sy in range(H):
        for sx in range(W):
            block = big.crop((sx * S, sy * S, sx * S + S, sy * S + S))
            counts = {}
            opaque = 0
            for p in block.getdata():
                if p[3] < 128:
                    continue
                opaque += 1
                counts[p] = counts.get(p, 0) + 1
            if opaque * 2 < S * S * 0.7:
                continue
            small.putpixel((sx, sy), max(counts, key=counts.get))
    # 船体轮廓：贴着船(不含浪花)的透明像素描一圈暗色
    out = small.copy()
    for sy in range(H):
        for sx in range(W):
            if small.getpixel((sx, sy))[3] > 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = sx + dx, sy + dy
                if 0 <= nx < W and 0 <= ny < H:
                    p = small.getpixel((nx, ny))
                    if p[3] > 0 and p not in (FOAM, FOAM_DARK) and p[:3] not in ((255, 0, 255), (127, 0, 127)):
                        out.putpixel((sx, sy), shade(p[:3], 0.45))
                        break
    if broken:
        # 断成两截：船首往右下错开
        left = out.crop((0, 0, 16, H))
        right = out.crop((16, 0, W, H))
        broken_img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        broken_img.alpha_composite(left.rotate(-12, resample=Image.NEAREST, center=(15, 27)), (-1, 1))
        broken_img.alpha_composite(right.rotate(14, resample=Image.NEAREST, center=(0, 27)), (17, 2))
        out = Image.eval(broken_img, lambda v: v)
        px_ = out.load()
        for yy in range(H):
            for xx in range(W):
                r, g, b, a = px_[xx, yy]
                if a:
                    px_[xx, yy] = (int(r * 0.75), int(g * 0.75), int(b * 0.75), a)
    return out


def sprites(tier):
    result = {}
    for frame in (0, 1):
        for angle in ANGLES:
            result[f"{angle}@{frame}"] = render(tier, angle, frame)
    result["normal"] = render(tier, -45, 0, wake=False)
    result["broken"] = render(tier, -45, 0, wake=False, broken=True)
    return result


# 兵牌小图标(11×12 剪影，# 为实心)：楼船三层檐、帆舰双帆、铁甲烟囱炮塔、现代阶梯舰桥与桅
ICONS = [
    ["....#......",
     "...###.....",
     "..#####....",
     "...###...#.",
     ".#######.#.",
     "..#####..#.",
     "#########.#",
     ".#########.",
     "..#######..",
     "...........",
     "...........",
     "..........."],
    [".....#.....",
     "..#..#..#..",
     ".###.#.###.",
     ".###.#.###.",
     ".####.####.",
     ".####.####.",
     ".###...###.",
     "#.#..#..#.#",
     "###########",
     ".#########.",
     "..#######..",
     "..........."],
    ["....##.....",
     "....##.....",
     "....##..#..",
     "....##..#..",
     "...####.#..",
     ".########..",
     "##########.",
     "###########",
     ".#########.",
     "..#######..",
     "...........",
     "..........."],
    ["......#....",
     ".....###...",
     "......#....",
     "......#....",
     ".....###...",
     "....#####..",
     "..########.",
     "###########",
     ".##########",
     "..#######..",
     "...........",
     "..........."],
]


# 运兵船：军团的棋子(缩成 7 行)站在船舷之间，下面是船身(右为翘起的船首)
_HULL = ["#........##",
         "###########",
         ".##########",
         "..#######..",
         "..........."]
TROOPSHIP = {
    "pawn": ["...........",
             "....###....",
             "...#####...",
             "....###....",
             "....###....",
             "...#####...",
             "..#######.."] + _HULL,
    "knight": ["....##.....",
               "...####....",
               "..######...",
               ".##.####...",
               "....####...",
               "...######..",
               "..#######.."] + _HULL,
    "rook": ["...........",
             "..#.###.#..",
             "..#######..",
             "...#####...",
             "...#####...",
             "...#####...",
             "..#######.."] + _HULL,
    "queen": ["..#.#.#.#..",
              "..#######..",
              "...#####...",
              "....###....",
              "....###....",
              "...#####...",
              "..#######.."] + _HULL,
}


# 兵种兵牌(CorpsArmSystem.Arm)：与棋子同格式的白色剪影，ui/icons/corps/arm_<兵种>
ARMS = {
    # 步兵：持盾执矛
    "infantry": ["........#..",
                 "...##...#..",
                 "...##...#..",
                 "..####..#..",
                 ".##.####...",
                 ".##.###.#..",
                 ".##.###.#..",
                 "....###.#..",
                 "...#..#.#..",
                 "...#..#.#..",
                 "..##..##...",
                 "..........."],
    # 弓兵：张弓
    "archer": ["...##..#...",
               "...##...#..",
               "..####...#.",
               ".#######.#.",
               "..####...#.",
               "..####...#.",
               "...##...#..",
               "...##..#...",
               "..#..#.....",
               "..#..#.....",
               ".##..##....",
               "..........."],
    # 车兵：驭手站在车厢里，前面一匹马，大车轮
    "chariot": ["..#........",
                ".###....#..",
                "..#....###.",
                "#####.####.",
                "#####.###.#",
                "###########",
                ".###..####.",
                "#.#.#.#..#.",
                "#####.#..#.",
                "#.#.#.#..#.",
                ".###.......",
                "..........."],
    # 骑兵：骑手在马背上
    "cavalry": ["...##......",
                "...##...##.",
                "..###..####",
                "..###.###..",
                "#########..",
                "#########..",
                ".########..",
                ".##....##..",
                ".#.#..#.#..",
                ".#.#..#.#..",
                "#..#.#..#..",
                "..........."],
    # 火枪兵：三角帽，肩扛带刺刀的火枪
    "musket": ["........#..",
               "..#####.#..",
               "...###..#..",
               "...##..##..",
               "..#######..",
               ".#.####.#..",
               "...####.#..",
               "...####....",
               "...#..#....",
               "...#..#....",
               "..##..##...",
               "..........."],
    # 步枪兵：钢盔，平端步枪
    "rifle": ["...###.....",
              "..#####....",
              "...##......",
              "..####.....",
              ".##########",
              "..####..#..",
              "..####.....",
              "..####.....",
              "..#..#.....",
              "..#..#.....",
              ".##..##....",
              "..........."],
    # 炮兵：炮管斜指，炮轮与炮尾
    "artillery": ["...........",
                  ".........##",
                  "........##.",
                  "......###..",
                  "....####...",
                  "...#####...",
                  "..###.###..",
                  ".#.#.#..##.",
                  ".#####...##",
                  ".#.#.#....#",
                  "..###......",
                  "..........."],
    # 装甲车：低矮车身、小炮塔，车身下三个分开的车轮(与坦克的履带区分)
    "mechanized": ["...........",
                   "...........",
                   "....##.....",
                   "...#######.",
                   ".#########.",
                   "###########",
                   "###########",
                   "...........",
                   ".##.##..##.",
                   ".##.##..##.",
                   "...........",
                   "..........."],
    # 坦克：炮塔与长炮管，履带与负重轮
    "tank": ["...........",
             "...........",
             "...####....",
             "..#########",
             "..#####....",
             ".#########.",
             "###########",
             "#.#.#.#.#.#",
             ".#########.",
             "...........",
             "...........",
             "..........."],
    # 直升机：旋翼、机身、尾梁、起落橇
    "helicopter": ["###########",
                   ".....#.....",
                   "...####....",
                   "..######..#",
                   ".##########",
                   "..######..#",
                   "...####....",
                   "..#....#...",
                   ".########..",
                   "...........",
                   "...........",
                   "..........."],
    # 战斗机：俯视，后掠三角翼
    "fighter": [".....#.....",
                "....###....",
                "....###....",
                "...#####...",
                "..#######..",
                ".#########.",
                "###########",
                "....###....",
                "....###....",
                "...#####...",
                "..##.#.##..",
                "..........."],
    # 轰炸机：俯视，平直长翼与发动机
    "bomber": [".....#.....",
               "....###....",
               "....###....",
               "###########",
               "###########",
               "#.#.###.#.#",
               "....###....",
               "....###....",
               "....###....",
               "..#######..",
               "....#.#....",
               "..........."],
    # 民兵：举草叉
    "militia": ["#.#.#......",
                "#####......",
                "..#..##....",
                "..#..##....",
                "..#.####...",
                "..######...",
                "..#.####...",
                "..#.####...",
                "..#.#..#...",
                "..#.#..#...",
                "....##.##..",
                "..........."],
}


def icon(tier):
    return silhouette(ICONS[tier])


def silhouette(rows):
    img = Image.new("RGBA", (13, 14), (0, 0, 0, 0))
    filled = {(x + 1, y + 1) for y, row in enumerate(rows) for x, ch in enumerate(row) if ch == "#"}
    for x, y in filled:
        img.putpixel((x, y), (255, 255, 255, 255))
    for x in range(13):
        for y in range(14):
            if (x, y) in filled:
                continue
            if any((x + dx, y + dy) in filled for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                img.putpixel((x, y), (24, 24, 24, 255))
    return img


def build():
    out = {}
    for tier in range(4):
        for name, img in sprites(tier).items():
            out[f"GameResources/actors/boats/ec_warship_{tier}/{name}.png"] = img
        out[f"GameResources/ui/icons/corps/ship_{tier}.png"] = icon(tier)
    for name, rows in TROOPSHIP.items():
        out[f"GameResources/ui/icons/corps/troopship_{name}.png"] = silhouette(rows)
    for name, rows in ARMS.items():
        assert all(len(row) == 11 for row in rows) and len(rows) == 12, name
        out[f"GameResources/ui/icons/corps/arm_{name}.png"] = silhouette(rows)
    return out


def preview(images):
    scale = 4
    cols = len(ANGLES) + 2
    sheet = Image.new("RGBA", (cols * (W + 2) * scale + 20, 4 * 2 * (H + 2) * scale + 140), (60, 90, 140, 255))
    for tier in range(4):
        for frame in (0, 1):
            for c, angle in enumerate(ANGLES):
                img = images[f"GameResources/actors/boats/ec_warship_{tier}/{angle}@{frame}.png"]
                sheet.alpha_composite(img.resize((W * scale, H * scale), Image.NEAREST),
                                      (10 + c * (W + 2) * scale, 10 + (tier * 2 + frame) * (H + 2) * scale))
            extra = "normal" if frame == 0 else "broken"
            img = images[f"GameResources/actors/boats/ec_warship_{tier}/{extra}.png"]
            sheet.alpha_composite(img.resize((W * scale, H * scale), Image.NEAREST),
                                  (10 + len(ANGLES) * (W + 2) * scale, 10 + (tier * 2 + frame) * (H + 2) * scale))
        ic = images[f"GameResources/ui/icons/corps/ship_{tier}.png"]
        sheet.alpha_composite(ic.resize((13 * 8, 14 * 8), Image.NEAREST),
                              (10 + tier * 130, sheet.height - 125))
    for i, name in enumerate(TROOPSHIP):
        ic = images[f"GameResources/ui/icons/corps/troopship_{name}.png"]
        sheet.alpha_composite(ic.resize((13 * 8, 14 * 8), Image.NEAREST),
                              (10 + (4 + i) * 130, sheet.height - 125))
    sheet.save(os.path.join(HERE, "ship_preview.png"))


if __name__ == "__main__":
    images = build()
    if "--preview" in sys.argv:
        preview(images)
    if "--write" in sys.argv:
        for path, img in images.items():
            full = os.path.join(ROOT, path)
            os.makedirs(os.path.dirname(full), exist_ok=True)
            img.save(full)
        for tier in range(4):
            folder = os.path.join(ROOT, f"GameResources/actors/boats/ec_warship_{tier}")
            with open(os.path.join(folder, "sprites.json"), "w", encoding="utf-8") as f:
                json.dump({"Default": {"PivotX": 0.5, "PivotY": 0.2, "PixelsPerUnit": 1}}, f, indent=2)
    print("ok", len(images))
