"""EmpireCraft 像素图标锻造器 —— 与 godpower 图标同一套风格(规范见同目录 STYLE.md)。

用法: python Tools/IconForge/icon_forge.py [图标名 ...]     (不带参数则全部重画)
      python Tools/IconForge/icon_forge.py --preview         (另存 8 倍放大预览表到 Tools/IconForge/preview.png)

画法: 每个图标只用"材质"画平涂形状, 之后统一流水线自动加工:
  1. 受光: 材质区域的上/左边缘 -> 亮色, 下/右边缘 -> 暗色(光源固定在左上);
  2. 描边: 形状外一圈 1px 深褐 #2a1410;
  3. 投影: 描边整体向右下平移 1px, 黑色 alpha 70。
需要固定颜色的细节(文字、宝石高光等)用 tone 参数手动指定, 流水线不会再改。
"""
import math
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SIZE = 28
OUTLINE = (0x2a, 0x14, 0x10, 255)
SHADOW = (0, 0, 0, 70)

# 材质色阶: [高光, 本色, 暗部, 深色] —— 取自模组现有 godpower 图标(iconToolCoreCreate 等)
MATERIALS = {
    "gold":   ["#fff2ae", "#f2bd3c", "#c98d22", "#9c6a14"],
    "red":    ["#ff7466", "#d95050", "#a0242e", "#5c0e18"],
    "paper":  ["#fffbe6", "#ffe9c4", "#e5cb83", "#b89a5e"],
    "wood":   ["#d9a066", "#a8693c", "#7a4526", "#4a2716"],
    "jade":   ["#9be08a", "#47af4d", "#207729", "#145a1c"],
    "blue":   ["#b8e0ff", "#4a86d0", "#1b4486", "#0f2a5a"],
    "steel":  ["#ffffff", "#dfd8cb", "#b7ad9c", "#7a7266"],
    "bamboo": ["#fff2ae", "#e5cb83", "#b89a5e", "#7a5a2e"],
    "ink":    ["#5a4a44", "#3a2c28", "#26201c", "#140c0a"],
    "skin":   ["#ffe0bd", "#f2b98a", "#d08a5c", "#9c5a3a"],
    "agold":   ["#f0d48a", "#c9a046", "#8f6b25", "#5a3f12"],
    "crimson": ["#c4503f", "#8e2420", "#5e1414", "#360a0a"],
    "lacquer": ["#5a4a46", "#2e2220", "#1c1412", "#0e0a08"],
    "bronze":  ["#c99a62", "#8a5f33", "#5e3d1e", "#38230f"],
    "djade":   ["#8fbf9a", "#3f7f5a", "#2a5a3e", "#163323"],
    "oldbamboo": ["#e8d6a0", "#bfa26a", "#8a7044", "#55432a"],
    "stone":   ["#d9d2c4", "#a49c8c", "#75705f", "#4a463b"],
    "indigo":  ["#8ea6c8", "#3b5680", "#26395a", "#141f33"],
    "silk":    ["#f1e6c6", "#d6c39a", "#a68f62", "#6e5c3c"],
    "heat0":  ["#7fb98f", "#4d7f63", "#3a6049", "#244030"],
    "heat1":  ["#cfe08a", "#9bb64e", "#748a36", "#4f6024"],
    "heat2":  ["#fff2ae", "#e2c344", "#b8962a", "#8a6c18"],
    "heat3":  ["#ffb27a", "#e47f35", "#b85a1e", "#8a3e12"],
    "heat4":  ["#ff7466", "#cd352b", "#a0242e", "#5c0e18"],
}


def hex_rgba(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)


class Canvas:
    """mat[y][x] = 材质名, tone[y][x] = 指定色阶(None 表示交给受光流水线)。"""

    def __init__(self, remap=None):
        self.mat = [[None] * SIZE for _ in range(SIZE)]
        self.tone = [[None] * SIZE for _ in range(SIZE)]
        self.remap = remap or {}
        # detail=True：写实渲染(体积明暗、投影遮挡、金属高光、材质纹理)，庄重风格的图标都开
        self.detail = False

    def px(self, x, y, m, tone=None):
        if 0 <= x < SIZE and 0 <= y < SIZE:
            self.mat[y][x] = m
            self.tone[y][x] = tone

    def clear(self, x, y):
        if 0 <= x < SIZE and 0 <= y < SIZE:
            self.mat[y][x] = None
            self.tone[y][x] = None

    def rect(self, x0, y0, x1, y1, m, tone=None):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.px(x, y, m, tone)

    def hline(self, x0, x1, y, m, tone=None):
        self.rect(x0, y, x1, y, m, tone)

    def vline(self, x, y0, y1, m, tone=None):
        self.rect(x, y0, x, y1, m, tone)

    def disc(self, cx, cy, r, m, tone=None):
        for y in range(SIZE):
            for x in range(SIZE):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    self.px(x, y, m, tone)

    def poly(self, pts, m, tone=None):
        for y in range(SIZE):
            for x in range(SIZE):
                if _inside(x + 0.5, y + 0.5, pts):
                    self.px(x, y, m, tone)

    def render(self):
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        filled = lambda x, y: 0 <= x < SIZE and 0 <= y < SIZE and self.mat[y][x] is not None
        same = lambda x, y, m: 0 <= x < SIZE and 0 <= y < SIZE and self.mat[y][x] == m
        for y in range(SIZE):
            for x in range(SIZE):
                m = self.mat[y][x]
                if m is None:
                    continue
                t = self.tone[y][x]
                if t is None and self.detail:
                    t = self._detail_tone(x, y, m)
                elif t is None:
                    if not same(x, y - 1, m) or not same(x - 1, y, m):
                        t = 0
                    elif not same(x, y + 1, m) or not same(x + 1, y, m):
                        t = 2
                    else:
                        t = 1
                img.putpixel((x, y), hex_rgba(MATERIALS[self.remap.get(m, m)][t]))
        outline = set()
        for y in range(SIZE):
            for x in range(SIZE):
                if not filled(x, y) and any(filled(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    outline.add((x, y))
        for x, y in outline:
            img.putpixel((x, y), OUTLINE)
        for y in range(SIZE):
            for x in range(SIZE):
                if img.getpixel((x, y))[3] == 0 and (x - 1, y - 1) in outline | _solid(self) and \
                        ((x - 1, y) in outline or (x, y - 1) in outline):
                    img.putpixel((x, y), SHADOW)
        return img


METALLIC = {"gold", "agold", "bronze", "steel", "stone", "jade", "djade"}
TEXTURED = {"wood", "lacquer", "bamboo", "oldbamboo", "silk", "paper", "crimson", "red"}


def _run(c, x, y, dx, dy, m, limit=8):
    n = 0
    while n < limit:
        x += dx; y += dy
        if not (0 <= x < SIZE and 0 <= y < SIZE) or c.mat[y][x] != m:
            break
        n += 1
    return n


def _detail_tone_impl(c, x, y, m):
    """写实明暗：按到上左/下右边缘的距离分层，抖动过渡；遮挡阴影；金属高光；材质颗粒。"""
    base = c.remap.get(m, m)
    tl = min(_run(c, x, y, 0, -1, m), _run(c, x, y, -1, 0, m))
    br = min(_run(c, x, y, 0, 1, m), _run(c, x, y, 1, 0, m))
    checker = (x + y) % 2 == 0
    if tl == 0 and br == 0:
        t = 1
    elif tl == 0:
        t = 0
    elif br == 0:
        corner = _run(c, x, y, 0, 1, m) == 0 and _run(c, x, y, 1, 0, m) == 0
        t = 3 if corner else 2
    elif br == 1:
        t = 2 if checker else 1
    elif tl == 1:
        t = 0 if (base in METALLIC and checker) else 1
    else:
        t = 1
        if tl + br >= 6 and br * 2 < tl and (x + 2 * y) % 3 == 0:
            t = 2                                         # 大块区域背光一侧的渐暗
    # 投影遮挡：正上方压着别的材质(屋檐、兽钮、冠)，本行压暗
    above = c.mat[y - 1][x] if y > 0 else None
    if above is not None and above != m and t < 2:
        t = 2
    # 金属高光：受光面零星反光点
    if base in METALLIC and t == 1 and tl in (1, 2) and br >= 2 and (x - y) % 7 == 0:
        t = 0
    # 材质颗粒：木纹、漆面、竹纹、绢纹
    if base in TEXTURED and t == 1 and (y * 5 + x * 3) % 13 == 0:
        t = 2
    return t


Canvas._detail_tone = lambda self, x, y, m: _detail_tone_impl(self, x, y, m)


def _solid(c):
    return {(x, y) for y in range(SIZE) for x in range(SIZE) if c.mat[y][x] is not None}


def _inside(x, y, pts):
    n, hit = len(pts), False
    for i in range(n):
        (x1, y1), (x2, y2) = pts[i], pts[(i + 1) % n]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            hit = not hit
    return hit


# ───────────────────────── 图标定义 ─────────────────────────
ICONS = {}


def icon(path):
    def deco(fn):
        ICONS[fn.__name__] = (path, fn)
        return fn
    return deco


@icon("GameResources/TabBureau.png")
def bureau(c):
    """官署：重檐殿宇——暗金正脊与鸱吻、黑瓦瓦垄、斗拱、深朱楹柱、匾额、门钉、汉白玉台基。"""
    c.detail = True
    c.hline(8, 19, 1, "agold"); c.hline(8, 19, 2, "agold", 2)          # 正脊
    c.rect(6, 0, 7, 2, "agold"); c.rect(20, 0, 21, 2, "agold")          # 鸱吻
    c.px(6, 0, "agold", 0); c.px(21, 0, "agold", 0)
    c.poly([(6, 3), (21, 3), (25, 7), (2, 7)], "lacquer")               # 上檐
    for x in range(5, 23, 2):
        c.vline(x, 4, 6, "lacquer", 0 if x < 14 else 2)                 # 瓦垄
    c.hline(2, 25, 7, "agold", 2); c.px(1, 6, "agold", 0); c.px(26, 6, "agold", 0)
    c.rect(6, 8, 21, 9, "crimson", 3)
    for x in range(7, 21, 3):
        c.px(x, 8, "agold", 1); c.px(x + 1, 9, "djade", 1)              # 斗拱彩画
    c.poly([(4, 10), (23, 10), (26, 13), (1, 13)], "lacquer")           # 下檐
    for x in range(3, 25, 2):
        c.vline(x, 11, 12, "lacquer", 0 if x < 14 else 2)
    c.hline(1, 26, 13, "agold", 2); c.px(0, 12, "agold", 0); c.px(27, 12, "agold", 0)
    c.rect(5, 14, 22, 21, "crimson", 3)                                 # 殿身
    for x in (5, 9, 17, 21):
        c.rect(x, 14, x + 1, 21, "crimson")
        c.px(x, 14, "agold", 1); c.px(x + 1, 14, "agold", 2)            # 柱头
    c.rect(11, 14, 16, 15, "lacquer"); c.hline(12, 15, 14, "agold", 0)  # 匾额
    c.rect(12, 16, 15, 21, "crimson", 2)                                # 门
    c.vline(13, 16, 21, "lacquer", 2); c.vline(14, 16, 21, "lacquer", 3)
    for y in (17, 19):
        c.px(12, y, "agold", 0); c.px(15, y, "agold", 0)               # 门钉
    c.rect(3, 22, 24, 23, "stone"); c.hline(3, 24, 22, "stone", 0)      # 台基
    for x in range(4, 24, 3):
        c.px(x, 23, "stone", 2)
    c.rect(9, 24, 18, 25, "stone"); c.hline(9, 18, 25, "stone", 2)


@icon("GameResources/TabConstitution.png")
def constitution(c):
    """宪法：深红皮面法典——錾花包金四角、压花框、中央金章、书脊金箍、铜扣、做旧书页。"""
    c.detail = True
    c.rect(8, 4, 23, 24, "oldbamboo")
    for y in range(5, 24, 2):
        c.px(23, y, "oldbamboo", 2); c.px(22, y + 1, "oldbamboo", 3)
    c.rect(4, 2, 21, 23, "crimson")
    c.rect(4, 2, 7, 23, "crimson", 3)
    c.vline(6, 2, 23, "crimson", 2)
    for y in (4, 9, 15, 20):                                             # 书脊金箍
        c.hline(4, 7, y, "agold", 1); c.px(4, y, "agold", 0)
    for x0, y0, fx, fy in ((17, 2, 1, 0), (17, 20, 1, 1), (8, 2, 0, 0), (8, 20, 0, 1)):
        c.rect(x0, y0, x0 + 4, y0 + 3, "agold")                          # 包角
        c.px(x0 + (3 if fx else 1), y0 + (2 if fy else 1), "agold", 3)  # 錾花
        c.px(x0 + 2, y0 + 1 + fy, "agold", 0)
    c.rect(10, 6, 19, 19, "crimson", 2)                                 # 压花框
    c.rect(11, 7, 18, 18, "crimson")
    c.disc(14.5, 12.5, 3.9, "agold")
    c.disc(14.5, 12.5, 2.6, "crimson", 3)
    c.px(14, 11, "agold", 0); c.px(15, 12, "agold", 1); c.px(14, 13, "agold", 1); c.px(15, 14, "agold", 2)
    c.rect(21, 11, 23, 14, "bronze"); c.px(23, 12, "bronze", 0)          # 铜扣


@icon("GameResources/TabDynasty.png")
def dynasty(c):
    """先帝录：宗庙牌位——云头顶饰、黑漆框、暗金卷草边、朱红牌心与金字、须弥座。"""
    c.detail = True
    c.poly([(8, 5), (9, 3), (12, 1), (16, 1), (19, 3), (20, 5)], "agold")
    c.px(14, 0, "agold", 0); c.px(13, 0, "agold", 1)
    c.px(10, 3, "agold", 3); c.px(17, 3, "agold", 3); c.px(14, 2, "crimson", 1)
    c.rect(7, 5, 21, 21, "lacquer")
    c.rect(8, 6, 20, 20, "agold", 2)
    for y in range(7, 20, 2):                                            # 卷草纹
        c.px(8, y, "agold", 0); c.px(20, y + 1, "agold", 3)
    for x in range(9, 20, 2):
        c.px(x, 6, "agold", 0); c.px(x + 1, 20, "agold", 3)
    c.rect(10, 8, 18, 18, "crimson", 2)
    c.rect(11, 9, 17, 17, "crimson", 1)
    c.vline(14, 10, 16, "agold", 1)
    for y in (10, 13, 16):
        c.hline(13, 15, y, "agold", 0)
    c.px(12, 11, "agold", 2); c.px(16, 14, "agold", 2)
    c.rect(5, 21, 23, 22, "stone"); c.hline(5, 23, 21, "stone", 0)       # 须弥座
    c.rect(7, 23, 21, 23, "stone", 2)
    c.rect(4, 24, 24, 25, "stone"); c.hline(4, 24, 25, "stone", 3)
    c.px(5, 20, "agold"); c.px(23, 20, "agold")


@icon("GameResources/TabSetting.png")
def setting(c):
    """设置：黑漆令牌——暗金卷云纹边、朱红"令"纹、顶部铜环、下垂朱穗。"""
    c.detail = True
    c.poly([(7, 3), (21, 3), (23, 6), (23, 19), (14, 23), (5, 19), (5, 6)], "lacquer")
    c.poly([(8, 4), (20, 4), (22, 6.5), (22, 18.5), (14, 22), (6, 18.5), (6, 6.5)], "agold", 2)
    c.poly([(9, 6), (19, 6), (20, 8), (20, 17), (14, 20), (8, 17), (8, 8)], "lacquer", 1)
    for x, y in ((9, 7), (19, 7), (9, 16), (19, 16)):                    # 角上卷云
        c.px(x, y, "agold", 0)
    for y in range(8, 17, 3):
        c.px(6, y, "agold", 0); c.px(22, y + 1, "agold", 3)
    c.poly([(14, 8), (18, 11), (10, 11)], "crimson", 1)                 # "令"
    c.px(14, 8, "crimson", 0)
    c.hline(11, 17, 13, "crimson", 1)
    c.rect(13, 14, 15, 17, "crimson", 1); c.px(16, 16, "crimson", 2); c.px(13, 14, "crimson", 0)
    c.rect(12, 0, 16, 2, "bronze")
    c.clear(14, 1)                                                       # 铜环
    c.vline(14, 24, 25, "crimson"); c.rect(13, 26, 15, 27, "crimson", 2)
    c.px(14, 23, "agold", 1)


@icon("GameResources/TabInstitutions.png")
def institutions(c):
    """制度：做旧竹简——竹节、墨字、古铜编绳与绳结，左端卷起。"""
    c.detail = True
    c.rect(7, 4, 24, 23, "oldbamboo")
    for x in range(9, 25, 3):
        c.vline(x, 4, 23, "oldbamboo", 3)
    for x in range(7, 25, 3):
        c.vline(x, 5, 22, "oldbamboo", 0)
        c.px(x, 13, "oldbamboo", 2); c.px(x + 1, 13, "oldbamboo", 2)     # 竹节
        for y in (10, 11, 15, 17):
            c.px(x + 1, y, "lacquer", 1 if (x + y) % 3 else 2)
    for y in (7, 20):
        c.hline(3, 25, y, "bronze", 1)
        c.hline(3, 25, y + 1, "bronze", 3)
        c.px(25, y - 1, "bronze", 0); c.px(26, y + 1, "bronze", 2)       # 绳结
    c.rect(2, 3, 6, 24, "oldbamboo")
    for x, t in ((2, 2), (3, 0), (4, 1), (5, 2), (6, 3)):
        c.vline(x, 4, 23, "oldbamboo", t)
    for y in (7, 20):
        c.hline(2, 6, y, "bronze", 1); c.hline(2, 6, y + 1, "bronze", 3)


@icon("GameResources/TabColor.png")
def color(c):
    """颜色与旗帜: 木调色板, 四色颜料, 斜放一支朱漆毛笔。"""
    c.poly([(2, 12), (6, 5), (13, 3), (21, 4), (25, 9), (24, 15), (18, 17), (14, 21), (8, 23), (3, 20)], "wood")
    c.disc(8.5, 17.5, 1.6, "wood", 3)                     # 拇指孔
    for (x, y), m in zip(((7, 10), (12, 6), (18, 7), (21, 12)), ("red", "gold", "jade", "blue")):
        c.disc(x + 0.5, y + 0.5, 1.9, m)
    for i in range(8):                                     # 朱漆笔杆
        c.px(18 + i, 18 + i, "red", 0)
        c.px(19 + i, 18 + i, "red", 2)
        c.px(18 + i, 19 + i, "red", 3)
    c.rect(16, 16, 17, 17, "gold")                         # 铜箍
    c.px(15, 15, "ink"); c.px(14, 14, "ink"); c.px(15, 14, "ink", 2); c.px(14, 15, "ink", 2)   # 笔锋
    c.px(13, 13, "ink", 0)


@icon("GameResources/ui/icons/iconNationLayer.png")
def nation_layer(c):
    """民族情绪图层: 木杆金顶, 由冷到热五色旗。"""
    c.vline(4, 3, 25, "wood"); c.vline(5, 3, 25, "wood", 2)
    c.rect(3, 1, 6, 2, "gold")
    for i in range(5):
        x0 = 6 + i * 4
        wave = (0, -1, -1, 0, 1)[i]
        c.rect(x0, 4 + wave, x0 + 3, 16 + wave, f"heat{i}")
    c.hline(6, 25, 16, "heat0", 3)


@icon("GameResources/ui/icons/actor_traits/iconFounderRuler.png")
def founder_ruler(c):
    """开国雄主：冕旒冠压在出鞘宝剑上(开创基业、平定四方)。"""
    c.vline(13, 0, 25, "steel", 0); c.vline(14, 0, 25, "steel", 2)           # 剑身
    c.rect(9, 21, 18, 22, "gold"); c.px(9, 21, "gold", 0)                   # 剑格
    c.rect(12, 23, 15, 26, "red", 2); c.vline(12, 23, 26, "red", 0)         # 剑柄
    c.rect(2, 5, 25, 7, "ink"); c.hline(2, 25, 5, "ink", 0)                  # 冕板
    for x in range(3, 25, 3):
        c.px(x, 6, "gold", 1)
    for x in range(3, 25, 2):
        if x in (13, 14):
            continue
        for y in range(8, 13):
            c.px(x, y, "jade" if (x + y) % 2 else "gold", 1 if y < 11 else 2)
        c.px(x, 13, "red", 0)
    c.rect(8, 8, 19, 18, "gold"); c.hline(8, 19, 8, "gold", 0)              # 冠身
    for x in range(9, 19, 2):
        c.px(x, 16, "gold", 3)
    c.rect(12, 11, 15, 13, "red"); c.px(12, 11, "red", 0)
    c.hline(5, 22, 10, "jade", 1); c.px(5, 10, "jade", 0)                    # 玉簪
    c.rect(6, 18, 21, 20, "gold", 2)


@icon("GameResources/ui/icons/actor_traits/iconRestorerRuler.png")
def restorer_ruler(c):
    """中兴之主: 旭日从祥云中重新升起。"""
    for k in range(1, 8):                                                   # 光芒
        a = math.pi + k * math.pi / 8
        dx, dy, px, py = math.cos(a), math.sin(a), -math.sin(a), math.cos(a)
        c.poly([(14 + dx * 7 + px * 1.8, 16 + dy * 7 + py * 1.8), (14 + dx * 12.5, 16 + dy * 12.5),
                (14 + dx * 7 - px * 1.8, 16 + dy * 7 - py * 1.8)], "gold")
    c.disc(14, 16, 7.4, "red")                                            # 日
    c.disc(11.5, 13, 2.2, "red", 0)
    for y in range(17, 28):
        for x in range(28):
            c.clear(x, y)
    for cx, cy, r in ((6, 19, 3.2), (11, 18.5, 3.6), (17, 19, 3.4), (22, 19.5, 3)):   # 祥云
        c.disc(cx, cy, r, "paper")
    c.rect(3, 19, 24, 22, "paper")
    c.hline(5, 22, 22, "paper", 2)
    c.hline(8, 12, 20, "blue", 1); c.hline(15, 19, 20, "blue", 1)


# ───────────── 文化图标(GameResources/ui/icons/cultures/<文化>.png)，没有图标的文化用 Default ─────────────
CULTURE_DIR = "GameResources/ui/icons/cultures/"


def star(c, cx, cy, r_out, r_in, m, tone=None, points=5, rot=-math.pi / 2):
    pts = []
    for k in range(points * 2):
        r = r_out if k % 2 == 0 else r_in
        a = rot + k * math.pi / points
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    c.poly(pts, m, tone)


def shield(c, m, tone=None):
    c.poly([(4, 3), (23, 3), (23, 15), (13.5, 25), (4, 15)], m, tone)


@icon(CULTURE_DIR + "Default.png")
def culture_default(c):
    """默认: 素白旗。"""
    c.vline(6, 3, 25, "wood"); c.vline(7, 3, 25, "wood", 2)
    c.rect(5, 1, 8, 2, "gold")
    c.poly([(8, 4), (23, 5), (21, 10), (24, 15), (8, 15)], "paper")
    c.hline(11, 18, 9, "paper", 2); c.hline(11, 16, 11, "paper", 2)


@icon(CULTURE_DIR + "Huaxia.png")
def culture_huaxia(c):
    """华夏: 青铜鼎。"""
    c.rect(8, 3, 9, 7, "gold"); c.rect(18, 3, 19, 7, "gold")
    c.rect(5, 7, 22, 8, "gold", 0)
    c.poly([(5, 9), (22, 9), (21, 18), (6, 18)], "jade")
    c.hline(7, 20, 12, "gold", 1); c.hline(7, 20, 15, "gold", 2)
    for x in (9, 13, 17):
        c.rect(x, 13, x + 1, 14, "jade", 3)
    for x in (7, 13, 19):
        c.rect(x, 19, x + 1, 24, "jade", 2)


@icon(CULTURE_DIR + "China.png")
def culture_china(c):
    """现代中国: 长城。"""
    c.rect(2, 13, 25, 22, "steel")
    for x in range(2, 26, 4):
        c.rect(x, 10, x + 1, 12, "steel")
    c.rect(10, 6, 17, 22, "steel")
    for x in (10, 13, 16):
        c.rect(x, 4, x + 1, 5, "steel")
    c.rect(13, 16, 14, 22, "ink", 2)
    c.hline(2, 25, 23, "jade", 2); c.hline(2, 25, 24, "jade", 3)
    for y in (15, 18):
        c.hline(2, 9, y, "steel", 2); c.hline(18, 25, y, "steel", 2)
    c.rect(12, 8, 15, 10, "red")


@icon(CULTURE_DIR + "Japan.png")
def culture_japan(c):
    """日本: 朱红鸟居。"""
    c.poly([(2, 4), (25, 4), (24, 7), (3, 7)], "red")
    c.hline(2, 25, 3, "ink", 1)
    c.rect(4, 10, 23, 11, "red")
    c.rect(13, 8, 14, 9, "red", 2)
    c.rect(6, 8, 8, 25, "red"); c.rect(19, 8, 21, 25, "red")
    c.rect(5, 24, 9, 25, "ink"); c.rect(18, 24, 22, 25, "ink")


@icon(CULTURE_DIR + "Shanhai.png")
def culture_shanhai(c):
    """山海经: 青山与海浪。"""
    c.poly([(2, 20), (9, 6), (13, 13), (17, 4), (25, 20)], "jade")
    c.poly([(15, 7), (17, 4), (19, 7)], "paper")
    c.poly([(7.5, 9), (9, 6), (10.5, 9)], "paper")
    c.rect(2, 19, 25, 24, "blue")
    for x in range(3, 25, 5):
        c.hline(x, x + 2, 20, "blue", 0); c.px(x + 3, 21, "blue", 0)
    c.disc(21.5, 5.5, 2.4, "red")


@icon(CULTURE_DIR + "Youmu.png")
def culture_youmu(c):
    """游牧: 毡帐。"""
    c.poly([(3, 15), (13.5, 5), (24, 15)], "paper")
    c.rect(3, 15, 24, 23, "paper")
    c.hline(3, 24, 15, "red", 1); c.hline(3, 24, 16, "red", 2)
    c.rect(11, 17, 16, 23, "wood", 2); c.vline(13, 17, 23, "wood", 3)
    c.rect(12, 3, 15, 5, "wood")
    for x in (6, 21):
        c.vline(x, 18, 22, "paper", 2)


@icon(CULTURE_DIR + "Arab.png")
def culture_arab(c):
    """阿拉伯: 新月与星。"""
    c.disc(12, 14, 9.5, "gold")
    for y in range(28):
        for x in range(28):
            if (x - 15.6) ** 2 + (y - 12.2) ** 2 <= 7.4 ** 2:
                c.clear(x, y)
    star(c, 20.5, 14, 3.6, 1.5, "gold")


@icon(CULTURE_DIR + "Egypt.png")
def culture_egypt(c):
    """埃及: 金字塔与太阳。"""
    c.disc(21, 6, 3, "red")
    c.poly([(1, 24), (13, 5), (26, 24)], "gold")
    c.poly([(13, 5), (26, 24), (14, 24)], "gold", 2)
    for y in (11, 16, 21):
        c.hline(int(13 - (y - 5) * 0.63) + 1, 13, y, "gold", 0)
    c.hline(1, 26, 25, "bamboo", 2)


@icon(CULTURE_DIR + "Aztec.png")
def culture_aztec(c):
    """阿兹特克: 阶梯神庙。"""
    for i, (x0, x1) in enumerate(((3, 24), (5, 22), (7, 20), (9, 18))):
        y = 22 - i * 4
        c.rect(x0, y, x1, y + 3, "steel")
        c.hline(x0, x1, y, "jade", 1)
    c.rect(11, 5, 16, 9, "red")
    c.rect(13, 7, 14, 9, "ink", 2)
    c.rect(10, 4, 17, 4, "gold")
    c.rect(13, 10, 14, 25, "steel", 2)


@icon(CULTURE_DIR + "Ojibwe.png")
def culture_ojibwe(c):
    """奥吉布瓦: 羽毛。"""
    c.poly([(7, 24), (11, 13), (16, 5), (20, 3), (19, 9), (15, 17), (9, 24)], "paper")
    c.poly([(16, 5), (20, 3), (19, 8), (17, 8)], "ink", 1)
    for i in range(18):
        c.px(8 + i // 2 + (1 if i > 12 else 0), 24 - i, "wood", 2)
    c.poly([(10, 17), (13, 14), (14, 16), (11, 19)], "red", 1)
    c.vline(6, 23, 26, "wood"); c.px(7, 25, "red")


@icon(CULTURE_DIR + "Roma.png")
def culture_roma(c):
    """罗马: 桂冠。"""
    for k in range(9):
        a = math.pi * (0.62 + k * 0.095)
        for side in (-1, 1):
            cx = 13.5 + side * math.cos(a) * 9
            cy = 14 - math.sin(a) * 9
            c.poly([(cx - 1.6, cy), (cx, cy - 2.4), (cx + 1.6, cy), (cx, cy + 2.4)], "jade")
    c.rect(11, 22, 16, 24, "red"); c.px(10, 25, "red"); c.px(17, 25, "red")


@icon(CULTURE_DIR + "Western.png")
def culture_western(c):
    """西方: 城堡塔楼。"""
    c.rect(6, 8, 21, 24, "steel")
    for x in (6, 10, 14, 18):
        c.rect(x, 4, x + 2, 7, "steel")
    c.rect(11, 16, 16, 24, "wood", 2); c.hline(12, 15, 15, "wood", 2)
    c.rect(9, 11, 10, 13, "ink", 2); c.rect(17, 11, 18, 13, "ink", 2)
    c.vline(13, 1, 4, "wood"); c.rect(14, 1, 17, 3, "red")


@icon(CULTURE_DIR + "Frankish.png")
def culture_frankish(c):
    """法兰克: 蓝底金百合。"""
    shield(c, "blue")
    c.poly([(13.5, 5), (16, 10), (13.5, 16), (11, 10)], "gold")
    c.poly([(11, 13), (7, 9), (6.5, 13), (9, 15)], "gold")
    c.poly([(16, 13), (20, 9), (20.5, 13), (18, 15)], "gold")
    c.rect(9, 15, 18, 16, "gold"); c.rect(12, 17, 15, 20, "gold")


@icon(CULTURE_DIR + "Germanic.png")
def culture_germanic(c):
    """日耳曼: 金盾黑鹰(纹章式双翼上扬)。"""
    shield(c, "gold")
    c.poly([(12, 10), (6, 4), (5, 7), (7, 9), (5, 10), (7, 12), (6, 14), (12, 14)], "ink")    # 左翼
    c.poly([(15, 10), (21, 4), (22, 7), (20, 9), (22, 10), (20, 12), (21, 14), (15, 14)], "ink")  # 右翼
    c.rect(12, 8, 15, 18, "ink")                                            # 身
    c.rect(12, 5, 14, 7, "ink"); c.px(11, 6, "red", 1)                      # 头、喙
    c.poly([(11, 18), (16, 18), (13.5, 22)], "ink")                         # 尾
    c.px(10, 17, "red", 1); c.px(17, 17, "red", 1)                          # 爪


@icon(CULTURE_DIR + "Slavonic.png")
def culture_slavonic(c):
    """斯拉夫: 洋葱顶教堂。"""
    c.vline(13, 1, 4, "gold"); c.hline(12, 14, 2, "gold")
    c.poly([(13.5, 4), (19, 10), (17, 13), (10, 13), (8, 10)], "blue")
    c.poly([(13.5, 4), (19, 10), (17, 13), (13.5, 13)], "blue", 2)
    c.rect(9, 13, 18, 24, "paper")
    c.rect(12, 17, 15, 24, "wood", 2)
    c.rect(10, 15, 11, 16, "blue", 2); c.rect(16, 15, 17, 16, "blue", 2)
    c.rect(5, 18, 8, 24, "paper"); c.rect(19, 18, 22, 24, "paper")
    c.poly([(6.5, 15), (8.5, 18), (4.5, 18)], "gold"); c.poly([(20.5, 15), (22.5, 18), (18.5, 18)], "gold")


@icon(CULTURE_DIR + "Viking.png")
def culture_viking(c):
    """维京: 龙头长船与条纹帆。"""
    c.vline(13, 3, 17, "wood")
    c.rect(7, 4, 20, 14, "paper")
    for x in (7, 11, 15, 19):
        c.rect(x, 4, x + 1, 14, "red")
    c.poly([(2, 17), (25, 17), (21, 23), (6, 23)], "wood")
    c.rect(1, 13, 2, 17, "wood"); c.px(1, 12, "wood", 0); c.px(2, 12, "red")
    c.rect(25, 14, 26, 17, "wood")
    for x in range(6, 22, 3):
        c.disc(x + 0.5, 18.5, 1.1, "gold")
    c.hline(4, 23, 24, "blue", 1)


@icon(CULTURE_DIR + "India.png")
def culture_india(c):
    """印度: 粉莲。"""
    def petal(cx, cy, rx, ry, ang, tone=None):
        pts = []
        for k in range(16):
            t = k * math.pi / 8
            x, y = math.cos(t) * rx, math.sin(t) * ry
            pts.append((cx + x * math.cos(ang) - y * math.sin(ang), cy + x * math.sin(ang) + y * math.cos(ang)))
        c.poly(pts, "red", tone)
    petal(7, 16, 2.6, 6, -1.0, 2)
    petal(20, 16, 2.6, 6, 1.0, 2)
    petal(10, 14, 2.8, 7, -0.45)
    petal(17, 14, 2.8, 7, 0.45)
    petal(13.5, 12, 3.2, 8, 0)
    c.vline(13, 6, 17, "red", 0)
    c.rect(4, 21, 23, 23, "jade"); c.hline(6, 21, 21, "jade", 0)


@icon(CULTURE_DIR + "Persepolis.png")
def culture_persepolis(c):
    """波斯: 波斯波利斯双牛柱头石柱。"""
    c.rect(4, 4, 23, 7, "steel")
    c.rect(2, 6, 5, 9, "steel"); c.rect(22, 6, 25, 9, "steel")
    c.px(2, 5, "steel", 0); c.px(25, 5, "steel", 0)
    c.rect(9, 8, 18, 9, "gold")
    c.rect(10, 10, 17, 22, "steel")
    for x in (11, 13, 15):
        c.vline(x, 10, 22, "steel", 2)
    c.rect(7, 23, 20, 25, "steel"); c.hline(7, 20, 23, "gold", 1)


@icon(CULTURE_DIR + "Kosher.png")
def culture_kosher(c):
    """犹太: 七枝烛台。"""
    for k, h in zip(range(7), (8, 6, 4, 3, 4, 6, 8)):
        x = 4 + k * 3
        c.vline(x, h, 15 if k != 3 else 21, "gold")
        c.px(x, h - 1, "red", 0); c.px(x, h - 2, "gold", 0)
    c.hline(4, 22, 15, "gold")
    c.hline(7, 19, 13, "gold", 2)
    c.rect(9, 22, 18, 24, "gold")


@icon(CULTURE_DIR + "Corporate.png")
def culture_corporate(c):
    """企业: 玻璃摩天楼与金币。"""
    c.rect(5, 6, 13, 24, "blue"); c.rect(14, 2, 20, 24, "blue", 2)
    for y in range(8, 23, 3):
        c.hline(6, 12, y, "blue", 0)
    for y in range(4, 23, 3):
        c.hline(15, 19, y, "blue", 1)
    c.disc(20.5, 20.5, 4.2, "gold")
    c.vline(20, 18, 23, "gold", 3); c.vline(21, 18, 23, "gold", 0)


@icon(CULTURE_DIR + "Emperor.png")
def culture_emperor(c):
    """帝皇: 金色双头鹰。"""
    c.poly([(13.5, 10), (25, 5), (23, 9), (25, 10), (22, 13), (23, 15), (16, 17)], "gold")
    c.poly([(13.5, 10), (2, 5), (4, 9), (2, 10), (5, 13), (4, 15), (11, 17)], "gold")
    c.rect(11, 9, 16, 20, "gold")
    c.rect(9, 5, 11, 8, "gold"); c.rect(16, 5, 18, 8, "gold")
    c.px(8, 6, "gold", 2); c.px(19, 6, "gold", 2)
    c.disc(13.5, 14, 2.1, "red")
    c.poly([(11, 20), (16, 20), (13.5, 25)], "gold", 2)


@icon(CULTURE_DIR + "ElfFancy.png")
def culture_elffancy(c):
    """精灵幻想: 发光的叶。"""
    c.poly([(5, 23), (6, 13), (12, 6), (22, 3), (21, 13), (15, 20)], "jade")
    for i in range(16):
        c.px(6 + i, 22 - i, "jade", 3 if i < 15 else 2)
    for x, y in ((10, 15), (13, 12), (16, 9)):
        c.px(x, y + 2, "jade", 0); c.px(x + 2, y, "jade", 0)
    star(c, 21.5, 20.5, 3.4, 1.2, "gold", points=4, rot=0)
    c.px(4, 6, "gold", 0); c.px(25, 9, "gold", 0)


# ───────────── 谋划图标(GameResources/ui/icons/plots/plot_<谋划id>.png)：主体 + 右下角圆形角标 ─────────────
# 主体在 22×22 左上区域内画，角标压在右下角(同 godpower 的加减号徽章)；角标自带一圈深色描边，与主体分开。
PLOT_DIR = "GameResources/ui/icons/plots/"


def badge(c, kind):
    """右下角徽章：深色描边圈 → 金边 → 底色 → 白色符号。"""
    cx, cy = 21, 21
    inner = {"plus": "jade", "check": "jade", "minus": "red", "cross": "red", "war": "red", "alert": "red",
             "seal": "red", "arrow": "blue", "back": "blue", "swap": "blue", "star": "blue", "up": "gold"}[kind]
    if kind == "war":
        inner = "ink"
    for y in range(28):
        for x in range(28):
            d = (x - cx) ** 2 + (y - cy) ** 2
            if d <= 6.4 ** 2:
                c.px(x, y, "ink", 3)
            if d <= 5.4 ** 2:
                c.px(x, y, "gold", 1 if (x - cx) + (y - cy) < 0 else 2)
            if d <= 4.2 ** 2:
                c.px(x, y, inner, 1 if (x - cx) + (y - cy) < 0 else 2)
    w = lambda x, y: c.px(x, y, "steel", 0)
    if kind == "plus":
        for k in range(-2, 3):
            w(cx + k, cy); w(cx, cy + k)
    elif kind == "minus":
        for k in range(-2, 3):
            w(cx + k, cy)
    elif kind == "cross":
        for k in range(-2, 3):
            w(cx + k, cy + k); w(cx + k, cy - k)
    elif kind == "check":
        for x, y in ((cx - 2, cy), (cx - 1, cy + 1), (cx, cy + 2), (cx + 1, cy + 1), (cx + 2, cy), (cx + 3, cy - 1)):
            w(x, y)
    elif kind == "arrow":
        for k in range(-2, 3):
            w(cx + k, cy)
        w(cx + 1, cy - 1); w(cx + 1, cy + 1); w(cx, cy - 2); w(cx, cy + 2)
    elif kind == "back":
        for k in range(-2, 3):
            w(cx + k, cy)
        w(cx - 1, cy - 1); w(cx - 1, cy + 1); w(cx, cy - 2); w(cx, cy + 2)
    elif kind == "swap":
        for k in range(-2, 3):
            w(cx + k, cy - 1); w(cx + k, cy + 2)
        w(cx + 1, cy - 2); w(cx - 1, cy + 3)
    elif kind == "up":
        for k in range(-2, 3):
            w(cx, cy + k)
        w(cx - 1, cy - 1); w(cx + 1, cy - 1); w(cx - 2, cy); w(cx + 2, cy)
    elif kind == "star":
        w(cx, cy - 2); w(cx, cy - 1); w(cx - 2, cy); w(cx - 1, cy); w(cx, cy); w(cx + 1, cy); w(cx + 2, cy)
        w(cx - 1, cy + 1); w(cx + 1, cy + 1); w(cx - 2, cy + 2); w(cx + 2, cy + 2)
    elif kind == "war":
        for k in range(-2, 2):
            w(cx + k, cy + k); w(cx - k, cy + k)
        for x, y in ((cx - 2, cy + 2), (cx + 2, cy + 2), (cx - 3, cy + 1), (cx + 3, cy + 1)):
            c.px(x, y, "gold", 0)
    elif kind == "alert":
        for k in range(-2, 2):
            w(cx, cy + k)
        w(cx, cy + 3)
    elif kind == "seal":
        for x in range(cx - 2, cx + 3):
            w(x, cy - 2); w(x, cy + 2)
        for y in range(cy - 2, cy + 3):
            w(cx - 2, y); w(cx + 2, y)
        w(cx, cy)


# —— 主体(都画在 2..22 的范围里，给角标留出右下角) ——
def m_crown(c):
    """冕旒冠：纹饰冕板、前后垂旒(玉珠串)、金冠身、玉簪横贯、朱缨。"""
    c.rect(2, 5, 22, 7, "ink"); c.hline(2, 22, 5, "ink", 0)
    for x in range(3, 22, 3):
        c.px(x, 6, "gold", 1)                                            # 冕板纹饰
    for x in range(3, 22, 2):
        for y in range(8, 13):
            c.px(x, y, "jade" if (y + x) % 2 else "gold", 1 if y < 11 else 2)   # 旒
        c.px(x, 13, "red", 0)
    c.rect(7, 8, 17, 18, "gold")
    c.hline(7, 17, 8, "gold", 0)
    for x in range(8, 17, 2):
        c.px(x, 15, "gold", 3)                                           # 冠身錾纹
    c.rect(11, 11, 13, 13, "red"); c.px(11, 11, "red", 0)
    c.hline(4, 20, 10, "jade", 1); c.px(4, 10, "jade", 0); c.px(20, 10, "jade", 2)   # 玉簪
    c.rect(5, 18, 19, 20, "gold", 2); c.hline(5, 19, 20, "gold", 3)
    c.vline(12, 1, 4, "gold"); c.px(12, 0, "red", 0); c.px(11, 2, "gold", 0)


def m_seal(c):
    """玉玺：方正印台上蹲伏兽钮，朱红印面。"""
    c.rect(4, 12, 20, 19, "jade")
    c.hline(4, 20, 12, "jade", 0); c.vline(4, 12, 19, "jade", 0)
    c.rect(4, 19, 20, 21, "red")
    c.rect(7, 7, 16, 11, "jade")                              # 兽身
    c.rect(14, 4, 18, 8, "jade")                              # 兽首
    c.px(17, 5, "gold", 0)                                    # 眼
    c.rect(6, 9, 7, 11, "jade", 2); c.rect(9, 10, 10, 11, "jade", 2); c.rect(14, 10, 15, 11, "jade", 2)
    c.px(6, 6, "jade", 1); c.px(5, 5, "jade", 0)              # 尾
    for x in (7, 12, 17):
        c.px(x, 15, "jade", 3); c.px(x + 1, 16, "jade", 3)


def m_title(c):
    """法理印：方形朱印、四周篆刻回纹边、"印"纹、顶部印钮。"""
    c.rect(4, 1, 7, 3, "gold"); c.px(5, 1, "gold", 0)                    # 印钮
    c.rect(3, 3, 21, 21, "paper")
    c.rect(4, 4, 20, 20, "red")
    for k in range(5, 20, 2):                                            # 回纹边
        c.px(k, 5, "paper", 2); c.px(k, 19, "paper", 2); c.px(5, k, "paper", 2); c.px(19, k, "paper", 2)
    c.rect(7, 7, 9, 17, "paper", 0); c.rect(7, 7, 11, 8, "paper", 0); c.rect(7, 12, 11, 13, "paper", 0)
    c.rect(13, 7, 17, 8, "paper", 0); c.rect(16, 7, 17, 15, "paper", 0); c.rect(13, 7, 14, 17, "paper", 0)
    c.px(10, 16, "paper", 1); c.px(16, 17, "paper", 1)                   # 印泥不匀


def m_scroll(c):
    """诏书：明黄卷轴。"""
    c.rect(4, 5, 20, 19, "gold")
    for y in (8, 11, 14):
        c.hline(7, 17, y, "gold", 3)
    c.rect(2, 3, 4, 21, "wood"); c.rect(20, 3, 22, 21, "wood")
    c.px(3, 2, "red"); c.px(21, 2, "red"); c.px(3, 22, "red"); c.px(21, 22, "red")


def m_bamboo(c):
    """竹简制度。"""
    c.rect(4, 4, 21, 20, "bamboo")
    for x in range(6, 21, 3):
        c.vline(x, 4, 20, "bamboo", 3)
        for y in (8, 11, 14):
            c.px(x - 1, y, "ink", 1)
    c.hline(3, 22, 7, "red", 1); c.hline(3, 22, 17, "red", 1)


def m_chain(c, broken=False):
    """锁链(附庸)：三节粗环；broken 为断链(独立)，中间一节断开、迸出火星。"""
    links = [(2, 2), (8, 8), (14, 14)]
    for k, (x, y) in enumerate(links):
        if broken and k == 1:
            c.rect(x, y, x + 2, y + 6, "steel"); c.rect(x + 5, y, x + 6, y + 6, "steel")
            c.px(x + 3, y + 2, "gold", 0); c.px(x + 4, y + 4, "gold", 0); c.px(x + 3, y + 5, "red", 0)
            continue
        c.rect(x, y, x + 6, y + 6, "steel")
        for yy in range(y + 2, y + 5):
            for xx in range(x + 2, x + 5):
                c.clear(xx, yy)


def m_palace(c):
    """宫门：重檐、瓦垄、斗拱、红墙金钉大门、石阶。"""
    c.hline(6, 18, 2, "gold"); c.px(5, 1, "gold", 0); c.px(19, 1, "gold", 0)
    c.poly([(5, 3), (19, 3), (23, 8), (1, 8)], "ink")
    for x in range(4, 21, 2):
        c.vline(x, 4, 7, "ink", 0 if x < 12 else 2)
    c.hline(1, 23, 8, "gold", 2)
    for x in range(5, 20, 3):
        c.px(x, 9, "jade", 1); c.px(x + 1, 9, "gold", 1)
    c.rect(4, 10, 20, 20, "red", 3)
    for x in (4, 19):
        c.rect(x, 10, x + 1, 20, "red")
    c.rect(8, 12, 16, 20, "red", 2)
    c.vline(12, 12, 20, "ink", 3)
    for y in (13, 15, 17):
        for x in (9, 11, 13, 15):
            c.px(x, y, "gold", 0)
    c.rect(2, 20, 22, 22, "steel"); c.hline(2, 22, 20, "steel", 0)


def m_brush(c):
    """毛笔与墨池(文化)。"""
    c.rect(3, 17, 13, 21, "ink"); c.hline(4, 12, 17, "ink", 0)            # 砚
    c.rect(5, 18, 11, 20, "blue", 3)
    for i in range(12):
        c.px(9 + i, 14 - i, "wood", 0 if i % 2 else 1); c.px(10 + i, 14 - i, "wood", 2)
    c.rect(7, 13, 9, 15, "gold"); c.px(6, 16, "ink", 1); c.px(7, 16, "ink", 1)


def m_minister(c):
    """乌纱帽(权臣)。"""
    c.poly([(6, 9), (18, 9), (19, 18), (5, 18)], "ink")
    c.rect(8, 4, 16, 9, "ink"); c.hline(8, 16, 4, "ink", 0)
    c.rect(1, 11, 5, 13, "ink"); c.rect(19, 11, 23, 13, "ink")               # 双翅
    c.hline(5, 19, 16, "gold", 1)


def m_axe(c):
    """斧钺(九锡)：弯月形刃口。"""
    c.vline(9, 2, 22, "wood"); c.vline(10, 2, 22, "wood", 2)
    c.poly([(11, 4), (17, 2), (21, 6), (22, 11), (20, 16), (16, 15), (11, 12)], "gold")
    for y in range(3, 16):
        c.px(int(17 + 4 * (1 - abs(y - 9) / 7)), y, "gold", 0)
    c.rect(8, 1, 11, 2, "red"); c.rect(8, 21, 11, 22, "gold")


def m_phoenix(c):
    """凤冠(太后)。"""
    c.poly([(4, 18), (5, 9), (12, 5), (19, 9), (20, 18)], "gold")
    c.rect(4, 17, 20, 20, "gold", 2)
    for x in (7, 12, 17):
        c.disc(x + 0.5, 12.5, 1.6, "red")
    c.poly([(10, 4), (12, 1), (14, 4)], "blue")
    c.hline(6, 18, 15, "jade", 1)


def m_tablet(c):
    """牌位(追封)：云头、卷草边、朱红牌心、金字、须弥座。"""
    c.poly([(7, 4), (9, 2), (12, 1), (15, 2), (17, 4)], "gold")
    c.rect(7, 4, 17, 19, "ink")
    c.rect(8, 5, 16, 18, "gold", 2)
    for y in range(6, 18, 2):
        c.px(8, y, "gold", 0); c.px(16, y + 1, "gold", 3)
    c.rect(9, 6, 15, 17, "red", 2)
    c.vline(12, 7, 16, "gold", 1)
    for y in (8, 11, 14):
        c.hline(11, 13, y, "gold", 0)
    c.rect(5, 19, 19, 20, "steel"); c.rect(4, 21, 20, 22, "steel", 2)


def m_swords(c):
    """交叉双剑(战事)。"""
    for i in range(15):
        c.px(4 + i, 4 + i, "steel", 0); c.px(5 + i, 4 + i, "steel", 2)
        c.px(20 - i, 4 + i, "steel", 0); c.px(19 - i, 4 + i, "steel", 2)
    for x, y in ((16, 18), (6, 18)):
        c.rect(x, y, x + 2, y + 1, "gold")
    c.rect(18, 19, 20, 21, "red"); c.rect(4, 19, 6, 21, "red")


def m_people(c):
    """派系：三枚象牙笏板分开排列，中间一枚最高(朝堂上的一派人)。"""
    for x0, top in ((3, 6), (10, 2), (17, 6)):
        c.rect(x0, top + 1, x0 + 3, 20, "paper")
        c.hline(x0 + 1, x0 + 2, top, "paper")                  # 圆头
        c.vline(x0 + 1, top + 3, 18, "paper", 2)
        c.rect(x0, 17, x0 + 3, 18, "red", 1)                   # 系绳
    c.rect(2, 21, 21, 22, "wood")


def m_temple(c):
    """神庙(宗教)。"""
    c.poly([(2, 9), (12, 2), (22, 9)], "paper")
    c.rect(3, 9, 21, 10, "paper", 2)
    for x in (4, 9, 14, 19):
        c.rect(x, 11, x + 1, 19, "paper")
    c.rect(2, 19, 22, 21, "steel")
    c.disc(12, 6, 1.4, "gold")


def m_scales(c):
    """天平(揭发、断案)。"""
    c.vline(12, 3, 19, "gold"); c.hline(3, 21, 5, "gold")
    for x in (5, 19):
        c.vline(x, 6, 10, "gold", 2)
        c.poly([(x - 3, 11), (x + 3, 11), (x + 1, 13), (x - 1, 13)], "gold")
    c.rect(8, 19, 16, 21, "wood")


def m_calendar(c):
    """历书(年号)。"""
    c.rect(4, 4, 20, 21, "paper")
    c.rect(4, 4, 20, 8, "red")
    c.vline(8, 2, 5, "steel"); c.vline(16, 2, 5, "steel")
    for y in range(11, 20, 3):
        for x in range(6, 19, 3):
            c.px(x, y, "ink", 1)
    c.rect(11, 13, 13, 15, "red", 0)


def m_shield(c):
    """盾(军队)。"""
    c.poly([(4, 3), (20, 3), (20, 12), (12, 21), (4, 12)], "steel")
    c.poly([(12, 3), (20, 3), (20, 12), (12, 21)], "blue")
    c.vline(12, 3, 20, "gold", 1)


def m_envoy(c):
    """节杖(外交)。"""
    c.vline(11, 2, 22, "wood"); c.vline(12, 2, 22, "wood", 2)
    for y in (5, 9, 13):
        c.rect(8, y, 15, y + 2, "red")
        c.hline(8, 15, y, "red", 0)
    c.rect(10, 1, 13, 2, "gold")


def m_sword(c):
    """单剑(自建军队)。"""
    c.vline(11, 2, 16, "steel", 0); c.vline(12, 2, 16, "steel", 2)
    c.rect(7, 16, 16, 17, "gold")
    c.rect(11, 18, 12, 21, "red"); c.rect(10, 21, 13, 22, "gold")


def m_city(c):
    """城池(城墙与城楼)。"""
    c.rect(2, 11, 22, 21, "steel")
    for x in range(2, 23, 4):
        c.rect(x, 9, x + 1, 10, "steel")
    c.rect(8, 5, 16, 11, "red")
    c.poly([(6, 5), (18, 5), (12, 1)], "gold")
    c.rect(10, 15, 13, 21, "wood", 3)


def m_tribute(c):
    """贡箱(朝贡)。"""
    c.rect(3, 9, 21, 21, "red")
    c.rect(3, 9, 21, 11, "red", 3)
    c.poly([(3, 9), (5, 5), (19, 5), (21, 9)], "red")
    c.vline(12, 5, 21, "gold", 1); c.hline(3, 15, 21, "gold", 1)
    c.rect(11, 12, 13, 14, "gold", 0)
    c.disc(8, 4, 1.6, "gold"); c.disc(15, 3, 1.6, "gold")


def m_banners(c):
    """两面旗(合并国家)。"""
    c.vline(5, 2, 21, "wood"); c.vline(18, 2, 21, "wood")
    c.rect(6, 3, 12, 9, "red"); c.rect(11, 3, 17, 9, "blue")
    c.rect(6, 9, 17, 11, "gold")


def m_whiteflag(c):
    """白旗(停战)。"""
    c.vline(5, 2, 22, "wood"); c.vline(6, 2, 22, "wood", 2)
    c.poly([(7, 3), (21, 4), (19, 8), (21, 12), (7, 12)], "paper")


REGAL = {"gold": "agold", "red": "crimson", "ink": "lacquer", "steel": "stone", "jade": "djade",
         "blue": "indigo", "paper": "silk", "bamboo": "oldbamboo"}


def plot_icon(name, base, mark):
    def fn(c):
        c.remap = REGAL
        c.detail = True
        base(c)
        if mark:
            badge(c, mark)
    fn.__name__ = "plot_" + name
    ICONS["plot_" + name] = (PLOT_DIR + "plot_" + name + ".png", fn)


PLOT_ICONS = {
    "feudal_offer_vassalage": (m_chain, "arrow"),
    "feudal_tighten_vassalage": (m_chain, "up"),
    "feudal_annex_vassal": (m_chain, "plus"),
    "feudal_independence_war": (lambda c: m_chain(c, True), "war"),
    "become_empire": (m_crown, "up"),
    "usurp_imperial_legitimacy": (m_seal, "swap"),
    "adopt_central_plains_institutions": (m_bamboo, "plus"),
    "combine_kingdom": (m_banners, "plus"),
    "empire_plots": (m_people, "alert"),
    "force_stop_war": (m_whiteflag, "minus"),
    "empire_move_back_to_capital": (m_palace, "back"),
    "kingdom_petition_title": (m_title, "up"),
    "kingdom_start_join_taken_alliance": (m_tribute, "plus"),
    "kingdom_start_invite_to_faction": (m_people, "plus"),
    "faction_leader_influence_local_kingdom": (m_people, "arrow"),
    "empirecraft_city_culture_shift": (m_brush, "swap"),
    "empirecraft_independent_title_culture_conversion": (m_brush, "seal"),
    "empirecraft_restore_native_culture": (m_brush, "back"),
    "empirecraft_cultural_assimilation_duty": (m_brush, "check"),
    "empirecraft_kingdom_regime_conversion": (m_scroll, "swap"),
    "kingdom_expose_crime": (m_scales, "alert"),
    "kingdom_start_religion_war": (m_temple, "war"),
    "new_empire_royal": (m_crown, "star"),
    "emperor_year_name": (m_calendar, None),
    "kingdom_allow_army": (m_shield, "check"),
    "kingdom_allow_diplomacy": (m_envoy, "check"),
    "kingdom_allow_succession": (m_crown, "check"),
    "kingdom_allow_self_army": (m_sword, "check"),
    "kingdom_allow_independent": (lambda c: m_chain(c, True), "check"),
    "empire_take_back_title": (m_title, "back"),
    "emperor_posthumous_name": (m_tablet, "star"),
    "king_acquire_title": (m_title, "war"),
    "kingdom_destroy_title": (m_title, "cross"),
    "kingdom_get_title": (m_title, "plus"),
    "kingdom_change_capital_title": (m_palace, "arrow"),
    "kingdom_join_empire": (m_crown, "arrow"),
    "kingdom_create_title": (m_title, "star"),
    "kingdom_add_city_into_title": (m_city, "seal"),
    "empress_dowager_install_son": (m_phoenix, "swap"),
    "minister_acquire_empire": (m_minister, "up"),
    "minister_acquire_title": (m_minister, "seal"),
    "minister_receive_nine_bestowments": (m_axe, "star"),
}
for _name, (_base, _mark) in PLOT_ICONS.items():
    plot_icon(_name, _base, _mark)


# ───────────── 身份特质图标(GameResources/ui/icons/actor_traits/)：科举、官制、军制的器物 ─────────────
TRAIT_DIR = "GameResources/ui/icons/actor_traits/"


def _branch(c, flower, petal_tone=1):
    """一枝花：斜出的枝干、叶、花簇(桂、杏共用)。"""
    for i in range(18):
        c.px(5 + i, 23 - i, "bronze", 1 if i % 3 else 2)
        if i % 4 == 1:
            c.px(4 + i, 21 - i, "djade", 1); c.px(5 + i, 20 - i, "djade", 0)
    for cx, cy in ((9, 17), (13, 12), (17, 8), (20, 4), (8, 12), (16, 15), (21, 10)):
        c.disc(cx + 0.5, cy + 0.5, 1.7, flower, petal_tone)
        c.px(cx, cy, flower, 0)


@icon(TRAIT_DIR + "iconJuren.png")
def trait_juren(c):
    """举人：桂枝(乡试放榜正值桂花开，称桂榜)。"""
    c.detail = True
    _branch(c, "agold")


@icon(TRAIT_DIR + "iconGongshi.png")
def trait_gongshi(c):
    """贡士：杏花枝(会试在杏花时节放榜，称杏榜)。"""
    c.detail = True
    _branch(c, "silk")
    for cx, cy in ((9, 17), (13, 12), (17, 8), (20, 4)):
        c.px(cx, cy, "crimson", 0)


@icon(TRAIT_DIR + "iconJingshi.png")
def trait_jingshi(c):
    """进士：金榜(殿试黄纸金字，金榜题名)。"""
    c.detail = True
    c.rect(4, 4, 23, 22, "agold")
    c.rect(6, 6, 21, 20, "crimson", 2)
    c.rect(7, 7, 20, 19, "silk")
    for x in range(9, 20, 2):
        for y in range(9, 18, 2):
            c.px(x, y, "lacquer", 1 if (x + y) % 4 else 2)
    c.rect(12, 8, 15, 10, "crimson", 1)                         # 首名朱圈
    c.rect(2, 2, 25, 3, "lacquer"); c.rect(2, 23, 25, 24, "lacquer")
    c.px(2, 2, "agold", 0); c.px(25, 2, "agold", 0); c.px(2, 24, "agold", 0); c.px(25, 24, "agold", 0)
    c.vline(13, 0, 1, "crimson"); c.vline(14, 0, 1, "crimson", 2)


@icon(TRAIT_DIR + "iconEmpireOfficer.png")
def trait_officer(c):
    """帝国官员：乌纱帽。"""
    c.detail = True
    c.poly([(6, 12), (21, 12), (22, 22), (5, 22)], "lacquer")
    c.rect(9, 5, 18, 12, "lacquer"); c.hline(9, 18, 5, "lacquer", 0)
    c.rect(1, 14, 6, 16, "lacquer"); c.rect(21, 14, 26, 16, "lacquer")      # 展角
    c.px(1, 14, "lacquer", 0); c.px(26, 16, "lacquer", 3)
    c.hline(5, 22, 19, "agold", 1); c.hline(5, 22, 20, "agold", 3)
    c.rect(12, 15, 15, 17, "crimson", 1); c.px(12, 15, "crimson", 0)        # 帽正


@icon(TRAIT_DIR + "iconOfficerLeave.png")
def trait_officer_leave(c):
    """告老还乡：鸠杖(汉代赐给老人的鸠首杖)。"""
    c.detail = True
    for i in range(20):
        c.px(8 + i // 3, 25 - i, "bronze", 1 if i % 4 else 2)
        c.px(9 + i // 3, 25 - i, "bronze", 2)
    c.poly([(9, 7), (12, 4), (17, 3), (21, 5), (19, 8), (14, 9)], "djade")   # 鸠首
    c.px(18, 4, "lacquer", 0); c.px(21, 5, "agold", 1)
    c.poly([(9, 7), (5, 6), (7, 9)], "djade", 2)                             # 尾
    c.rect(10, 13, 14, 14, "crimson", 1); c.px(14, 15, "crimson", 2)          # 系绶


@icon(TRAIT_DIR + "iconEmpireArmy.png")
def trait_army(c):
    """帝国军人：铁兜鍪、红缨、护耳。"""
    c.detail = True
    c.poly([(6, 18), (6, 11), (9, 6), (14, 4), (19, 6), (22, 11), (22, 18)], "stone")
    c.vline(14, 4, 18, "stone", 2)
    for y in range(9, 18, 3):
        c.hline(7, 21, y, "stone", 2)                                         # 甲片
    c.rect(4, 17, 9, 23, "stone"); c.rect(19, 17, 24, 23, "stone")            # 护耳
    c.rect(9, 18, 19, 19, "bronze", 1)
    c.vline(14, 1, 3, "bronze"); c.poly([(12, 0), (16, 0), (17, 3), (11, 3)], "crimson")   # 缨


@icon(TRAIT_DIR + "iconEmpireEliteArmy.png")
def trait_elite_army(c):
    """军府军人：鎏金兜鍪、护颈、翎羽。"""
    c.detail = True
    c.poly([(6, 17), (6, 11), (9, 6), (14, 4), (19, 6), (22, 11), (22, 17)], "agold")
    c.vline(14, 4, 17, "agold", 2)
    for y in range(9, 17, 3):
        c.hline(7, 21, y, "agold", 2)
    c.rect(11, 9, 17, 11, "crimson", 1); c.px(14, 10, "agold", 0)              # 额饰
    c.poly([(3, 16), (25, 16), (23, 25), (5, 25)], "stone")                   # 护颈
    for x in range(5, 24, 3):
        c.vline(x, 17, 24, "stone", 2)
    c.hline(4, 24, 16, "agold", 1)
    c.poly([(13, 4), (15, 4), (19, 0), (17, 0)], "silk"); c.px(19, 0, "lacquer", 1)   # 翎


def _regal(fn):
    def wrapped(c):
        c.remap = REGAL
        c.detail = True
        fn(c)
    wrapped.__name__ = fn.__name__
    return wrapped


for _name in list(ICONS):
    if _name.startswith("culture_") or _name in ("founder_ruler", "restorer_ruler"):
        ICONS[_name] = (ICONS[_name][0], _regal(ICONS[_name][1]))


# 已改由 imperial_forge.py(冷峻写实风格，56×56)生成的图标：本脚本只在显式点名时才重画，免得覆盖
SUPERSEDED_PREFIXES = ("plot_", "culture_", "trait_")
SUPERSEDED = {"bureau", "constitution", "dynasty", "setting", "institutions", "founder_ruler", "restorer_ruler",
              "nation_layer"}


def main(argv):
    preview = "--preview" in argv
    names = [a for a in argv if not a.startswith("--")] or [
        n for n in ICONS if n not in SUPERSEDED and not n.startswith(SUPERSEDED_PREFIXES)]
    rendered = []
    for name in names:
        path, fn = ICONS[name]
        canvas = Canvas()
        fn(canvas)
        img = canvas.render()
        img.save(os.path.join(ROOT, path))
        rendered.append(img)
        print("wrote", path)
    if preview:
        s, pad = 8, 16
        sheet = Image.new("RGBA", (len(rendered) * (SIZE * s + pad) + pad, SIZE * s + pad * 2), (60, 64, 56, 255))
        for i, img in enumerate(rendered):
            big = img.resize((SIZE * s, SIZE * s), Image.NEAREST)
            sheet.alpha_composite(big, (pad + i * (SIZE * s + pad), pad))
        out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "preview.png")
        sheet.save(out)
        print("preview", out)


if __name__ == "__main__":
    main(sys.argv[1:])
