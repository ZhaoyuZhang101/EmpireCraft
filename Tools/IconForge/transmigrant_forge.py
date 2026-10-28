"""穿越政体(澳宋)的一组像素图标，单独生成，不重画 imperial_forge 的其余图标。

两面旗按实物配色，直接在目标尺寸上平涂(几何形状 16 倍超采样后按半数覆盖取整成硬边像素，细线按 1 像素直接画)，
不走"写实绘制再缩小"的流程——那样缩出来是糊的，不像像素画：

  · 星拳旗(元老院旗)：红底，柠檬黄八齿齿轮(右下一道暗黄齿边)；齿轮内一道红圈，黄底上红线向两侧上方放射，
    正中黄色举拳(红描边、红指缝)，小臂伸到下方的红五角星；
  · 启明星旗(国旗)：深蓝底，白色四芒星(每芒顺时针半边是阴面)，圆环在四芒穿过处断开，环外四道白线(左右长、上下短)。

输出：
  · 「穿越降临」godpower 按钮 ui/icons/godpowers/transmigrant_landing(28×28，黑描边、右下投影、骨白加号角标)；
  · 「元老院名册」窗口按钮 ui/icons/godpowers/transmigrant_senate(28×28，红皮名册、封面星拳纹章)；
  · 政体按钮 ui/buttons/TransmigrantOn/Off(64×64，共和政体按钮的边框，内框铺旗面红)；
  · 原版王国旗帜纹样 ui/banners/aosong_senate、aosong_national(54×54；游戏里整张乘国家的旗帜色，
    所以只用白、浅灰和镂空：浅灰乘出来是同色的暗面，细节靠镂空留出底色)；
  · 整面旗 ui/banners/aosong_senate_flag、aosong_national_flag(54×36，原色，名册、提示框用)。

用法: python Tools/IconForge/transmigrant_forge.py [--preview [输出路径]]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

RED = (204, 36, 34)
YELLOW = (246, 216, 54)
YELLOW_DARK = (198, 150, 22)
NAVY = (44, 56, 134)
WHITE = (246, 246, 250)
WHITE_SHADE = (190, 198, 228)
BONE = (232, 226, 206)
BONE_DARK = (170, 160, 132)
OUTLINE = (0, 0, 0, 255)
SHADOW = (0, 0, 0, 80)
SPINE = (150, 24, 22)                 # 名册书脊的暗红
TINT_SHADE = (200, 200, 200)          # 旗帜纹样里的暗面：乘上国家旗帜色后是同色的暗一档
K = 16                                # 超采样倍数


# ═════════════ 基本工具：按单位坐标(0~1)画形状，取整成硬边蒙版 ═════════════
def crisp(n, draw):
    """在 n×n 上取整的蒙版：draw(d, s) 用 s=n*K 的比例在超采样画布上画白色。"""
    big = Image.new("L", (n * K, n * K), 0)
    draw(ImageDraw.Draw(big), n * K)
    small = big.resize((n, n), Image.BOX)
    return np.asarray(small) >= 128


def thin(n, segments):
    """1 像素细线，直接在目标尺寸上画(不抗锯齿)。segments 为单位坐标的线段。"""
    im = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(im)
    for (x0, y0), (x1, y1) in segments:
        d.line([(x0 * n - 0.5, y0 * n - 0.5), (x1 * n - 0.5, y1 * n - 0.5)], fill=255, width=1)
    return np.asarray(im) > 0


def grow(mask, px=1):
    out = mask.copy()
    for _ in range(px):
        p = np.pad(out, 1)
        out = out | p[:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, :-2] | p[1:-1, 2:]
    return out


def edge_br(mask):
    """右下边缘：本格在形状里、右下角那格不在(画一道暗边显出厚度)。"""
    p = np.pad(mask, ((0, 1), (0, 1)))
    return mask & ~p[1:, 1:]


def paint(arr, mask, color):
    arr[mask] = tuple(color) + (255,) if len(color) == 3 else color


def circle(cx, cy, r):
    return lambda d, s: d.ellipse([(cx - r) * s, (cy - r) * s, (cx + r) * s, (cy + r) * s], fill=255)


# ═════════════ 星拳旗纹章(单位坐标) ═════════════
TEETH = 8
GEAR_OUT, GEAR_ROOT = 0.49, 0.395
RING_OUT, INNER = 0.33, 0.29
FIST_CX, FIST_TOP = 0.5, 0.24
STAR = (0.5, 0.735, 0.07)


def gear_draw(d, s):
    cx = cy = 0.5
    d.ellipse([(cx - GEAR_ROOT) * s, (cy - GEAR_ROOT) * s, (cx + GEAR_ROOT) * s, (cy + GEAR_ROOT) * s], fill=255)
    half = math.pi / TEETH * 0.5
    for i in range(TEETH):
        a = 2 * math.pi * i / TEETH - math.pi / 2
        pts = []
        for r, da in ((GEAR_ROOT - 0.02, -half * 1.12), (GEAR_OUT, -half * 0.8), (GEAR_OUT, half * 0.8), (GEAR_ROOT - 0.02, half * 1.12)):
            pts.append(((cx + r * math.cos(a + da)) * s, (cy + r * math.sin(a + da)) * s))
        d.polygon(pts, fill=255)


def fist_draw(d, s):
    cx, top = FIST_CX, FIST_TOP
    w = 0.115                                   # 半宽
    fw = 2 * w / 4
    d.rounded_rectangle([(cx - w) * s, (top + 0.06) * s, (cx + w) * s, (top + 0.2) * s], radius=0.02 * s, fill=255)
    for i in range(4):
        x = cx - w + fw * i
        d.rounded_rectangle([x * s, top * s, (x + fw) * s, (top + 0.12) * s], radius=0.02 * s, fill=255)
    d.rounded_rectangle([(cx - w - 0.025) * s, (top + 0.115) * s, (cx + 0.03) * s, (top + 0.17) * s], radius=0.02 * s, fill=255)
    d.polygon([((cx - 0.065) * s, (top + 0.19) * s), ((cx + 0.065) * s, (top + 0.19) * s),
               ((cx + 0.042) * s, (STAR[1] - 0.085) * s), ((cx - 0.042) * s, (STAR[1] - 0.085) * s)], fill=255)


def star_draw(d, s, cx=STAR[0], cy=STAR[1], r=STAR[2]):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + math.pi / 5 * i
        rr = r if i % 2 == 0 else r * 0.42
        pts.append(((cx + rr * math.cos(a)) * s, (cy + rr * math.sin(a)) * s))
    d.polygon(pts, fill=255)


def star_small(n):
    """小尺寸时五角星按点阵画(取整画出来是一团)。"""
    m = np.zeros((n, n), bool)
    cx, cy = round(STAR[0] * n - 0.5), round(STAR[1] * n - 0.5)
    for dx, dy in ((0, -1), (-1, 0), (0, 0), (1, 0), (-1, 1), (1, 1)):
        m[cy + dy, cx + dx] = True
    return m


def ray_segments(count):
    """从下方五角星一带向两侧上方放射(像日出)，铺满小臂两旁的空当。"""
    ox, oy = 0.5, STAR[1] - 0.01
    out = []
    step = 62 / max(1, count - 1)
    for side in (-1, 1):
        for i in range(count):
            a = math.radians(24 + step * i)
            out.append(((ox + side * 0.04 * math.sin(a), oy - 0.04 * math.cos(a)),
                        (ox + side * 0.5 * math.sin(a), oy - 0.5 * math.cos(a))))
    return out


def finger_gap_segments():
    """指缝三道、拇指上缘一道、手腕一道。"""
    w = 0.115
    fw = 2 * w / 4
    gaps = [((FIST_CX - w + fw * i, FIST_TOP + 0.01), (FIST_CX - w + fw * i, FIST_TOP + 0.105)) for i in (1, 2, 3)]
    gaps.append(((FIST_CX - w - 0.02, FIST_TOP + 0.112), (FIST_CX + 0.025, FIST_TOP + 0.112)))
    gaps.append(((FIST_CX - 0.06, FIST_TOP + 0.205), (FIST_CX + 0.06, FIST_TOP + 0.205)))
    return gaps


def senate_parts(n, rays):
    gear = crisp(n, gear_draw)
    ring = crisp(n, circle(0.5, 0.5, RING_OUT))
    inner = crisp(n, circle(0.5, 0.5, INNER))
    fist = crisp(n, fist_draw)
    star = crisp(n, star_draw) if n >= 40 else star_small(n)
    gaps = thin(n, finger_gap_segments()) & fist
    ray = thin(n, ray_segments(rays)) & inner & ~grow(fist, 1) & ~grow(star, 1)
    return gear, ring, inner, fist, star, gaps, ray


def senate_color(n, rays):
    """原色纹章(透明底)。"""
    gear, ring, inner, fist, star, gaps, ray = senate_parts(n, rays)
    a = np.zeros((n, n, 4), np.uint8)
    paint(a, gear, YELLOW)
    paint(a, edge_br(gear) & ~ring, YELLOW_DARK)
    paint(a, ring, RED)
    paint(a, inner, YELLOW)
    paint(a, ray, RED)
    paint(a, grow(fist, 1) & inner, RED)
    paint(a, fist, YELLOW)
    paint(a, gaps, RED)
    paint(a, star, RED)
    return a


def senate_tint(n, rays):
    """旗帜纹样：白色为主，红的部分镂空，齿轮右下边用浅灰(乘出暗面)。"""
    gear, ring, inner, fist, star, gaps, ray = senate_parts(n, rays)
    a = np.zeros((n, n, 4), np.uint8)
    paint(a, gear, WHITE)
    paint(a, edge_br(gear) & ~ring, TINT_SHADE)
    a[ring & ~inner] = 0
    paint(a, inner, WHITE)
    a[ray] = 0
    a[grow(fist, 1) & inner & ~fist] = 0
    paint(a, fist, WHITE)
    a[gaps] = 0
    a[star] = 0
    return a


# ═════════════ 启明星旗(单位坐标) ═════════════
def star_points(cx=0.5, cy=0.5, long_r=0.4, waist=0.1):
    pts = []
    for i in range(8):
        a = math.pi / 2 - math.pi / 4 * i
        r = long_r if i % 2 == 0 else waist
        pts.append((cx + r * math.cos(a), cy - r * math.sin(a)))
    return pts


def national_parts(n, long_r=0.4, waist=0.1, ring=(0.265, 0.22), bars=True):
    pts = star_points(long_r=long_r, waist=waist)
    star = crisp(n, lambda d, s: d.polygon([(x * s, y * s) for x, y in pts], fill=255))
    shade = crisp(n, lambda d, s: [d.polygon([(0.5 * s, 0.5 * s), (pts[i][0] * s, pts[i][1] * s),
                                              (pts[(i + 1) % 8][0] * s, pts[(i + 1) % 8][1] * s)], fill=255)
                                   for i in range(0, 8, 2)]) & star
    r_out, r_in = ring
    band = crisp(n, circle(0.5, 0.5, r_out)) & ~crisp(n, circle(0.5, 0.5, r_in))
    band &= ~grow(star, 1)
    lines = np.zeros((n, n), bool)
    if bars:
        lines = thin(n, [((0.5, 0.0), (0.5, 0.09)), ((0.5, 0.91), (0.5, 1.0)),
                         ((0.0, 0.5), (0.13, 0.5)), ((0.87, 0.5), (1.0, 0.5))])
    return star, shade, band, lines


def national_tint(n):
    star, shade, band, lines = national_parts(n)
    a = np.zeros((n, n, 4), np.uint8)
    paint(a, band, WHITE)
    paint(a, lines, WHITE)
    paint(a, star, WHITE)
    paint(a, shade, TINT_SHADE)
    return a


# ═════════════ 成品 ═════════════
def place(canvas, arr, x, y):
    canvas.alpha_composite(Image.fromarray(arr, "RGBA"), (x, y))


def outline_and_shadow(arr):
    body = arr[..., 3] == 255
    p = np.pad(body, 1)
    near = p[:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, :-2] | p[1:-1, 2:]
    arr[near & ~body] = OUTLINE
    solid = arr[..., 3] > 0
    shifted = np.zeros_like(solid)
    shifted[1:, 1:] = solid[:-1, :-1]
    arr[shifted & ~solid] = SHADOW
    return arr


def plus_badge(n=9):
    """骨白加号角标(3 像素粗)，右下暗边。"""
    a = np.zeros((n, n, 4), np.uint8)
    m = np.zeros((n, n), bool)
    c = n // 2
    m[c - 1:c + 2, 1:n - 1] = True
    m[1:n - 1, c - 1:c + 2] = True
    paint(a, m, BONE)
    paint(a, edge_br(m), BONE_DARK)
    return a


def make_godpower():
    canvas = np.zeros((28, 28, 4), np.uint8)
    canvas[1:25, 1:25] = senate_color(24, rays=3)
    badge = plus_badge(9)
    region = canvas[17:26, 17:26]
    mask = badge[..., 3] > 0
    region[mask] = badge[mask]
    return Image.fromarray(outline_and_shadow(canvas), "RGBA")


def make_roster():
    """元老院名册 godpower：立着的红皮名册(左书脊暗红、右侧露骨白书页)，封面正中星拳纹章。"""
    canvas = np.zeros((28, 28, 4), np.uint8)
    pages = np.zeros((28, 28), bool)
    pages[3:25, 4:25] = True
    paint(canvas, pages, BONE)
    canvas[3:25, 24] = BONE_DARK + (255,)
    canvas[24, 4:25] = BONE_DARK + (255,)
    cover = np.zeros((28, 28), bool)
    cover[1:24, 2:23] = True
    paint(canvas, cover, RED)
    canvas[1:24, 2:4] = SPINE + (255,)
    canvas[23, 4:23] = SPINE + (255,)
    region = canvas[3:22, 4:23]
    paint(region, small_emblem(19), YELLOW)
    region[small_emblem(19, shade=True)] = YELLOW_DARK + (255,)
    return Image.fromarray(outline_and_shadow(canvas), "RGBA")


# 小纹章里的举拳点阵(以中心为原点的列、行偏移)：Y 黄、D 暗黄(指缝、蜷起的拇指)
FIST_PIXELS = """
.YDYDY.
YYDYDYY
YDDDDYY
.YYYYY.
..YYY..
..YYY..
..YYY..
"""


def small_emblem(n, shade=False):
    """小尺寸的星拳纹章(封面红底上用)：黄齿轮、红心，红心里一只黄色举拳按点阵画；shade 取暗黄的指缝、拇指线与齿边。"""
    gear = crisp(n, gear_draw)
    hole = crisp(n, circle(0.5, 0.5, 0.3))
    rows = [row for row in FIST_PIXELS.strip().splitlines()]
    fist = np.zeros((n, n), bool)
    dark = np.zeros((n, n), bool)
    c = n // 2
    top, left = c - len(rows) // 2 - 1, c - len(rows[0]) // 2
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch != ".":
                fist[top + y, left + x] = True
            if ch == "D":
                dark[top + y, left + x] = True
    if shade:
        return (edge_br(gear) & ~grow(hole, 1)) | dark
    return (gear & ~hole) | fist


def make_regime_button(state):
    base = Image.open(os.path.join(ROOT, "GameResources", "ui", "buttons", f"Republic{state}.png")).convert("RGBA")
    arr = np.asarray(base).copy()
    arr[6:58, 6:58, :3] = RED
    arr[6:58, 6:58, 3] = 255
    out = Image.fromarray(arr, "RGBA")
    place(out, senate_color(48, rays=4), 8, 8)
    return out


def make_banner(arr):
    out = Image.new("RGBA", (54, 54), (255, 255, 255, 0))
    n = arr.shape[0]
    place(out, arr, (54 - n) // 2, (54 - n) // 2)
    return out


def flag_image(kind, w=54, h=36):
    if kind == "senate":
        out = Image.new("RGBA", (w, h), RED + (255,))
        place(out, senate_color(30, rays=3), (w - 30) // 2, (h - 30) // 2)
        return out
    out = Image.new("RGBA", (w, h), NAVY + (255,))
    n = 30
    star, shade, band, _ = national_parts(n, bars=False)
    a = np.zeros((n, n, 4), np.uint8)
    paint(a, band, WHITE)
    paint(a, star, WHITE)
    paint(a, shade, WHITE_SHADE)
    place(out, a, (w - n) // 2, (h - n) // 2)
    d = ImageDraw.Draw(out)
    cy, cx = h // 2, w // 2
    d.line([(4, cy), (cx - 11, cy)], fill=WHITE, width=1)
    d.line([(cx + 11, cy), (w - 5, cy)], fill=WHITE, width=1)
    d.line([(cx, 1), (cx, 2)], fill=WHITE, width=1)
    d.line([(cx, h - 3), (cx, h - 2)], fill=WHITE, width=1)
    return out


def build():
    return {
        "GameResources/ui/icons/godpowers/transmigrant_landing.png": make_godpower(),
        "GameResources/ui/icons/godpowers/transmigrant_senate.png": make_roster(),
        "GameResources/ui/buttons/TransmigrantOn.png": make_regime_button("On"),
        "GameResources/ui/buttons/TransmigrantOff.png": make_regime_button("Off"),
        "GameResources/ui/banners/aosong_senate.png": make_banner(senate_tint(38, rays=4)),
        "GameResources/ui/banners/aosong_national.png": make_banner(national_tint(40)),
        "GameResources/ui/banners/aosong_senate_flag.png": flag_image("senate"),
        "GameResources/ui/banners/aosong_national_flag.png": flag_image("national"),
    }


def tinted(im, field, ink):
    """预览：模拟原版旗帜的染色(底色乘旗面色，纹样乘旗帜色)。"""
    a = np.asarray(im).astype(np.float32)
    rgb = a[..., :3] / 255.0 * np.array(ink, np.float32)
    bg = Image.new("RGBA", im.size, field + (255,))
    bg.alpha_composite(Image.fromarray(np.dstack([rgb, a[..., 3]]).astype(np.uint8), "RGBA"))
    return bg


if __name__ == "__main__":
    icons = build()
    for path, im in icons.items():
        full = os.path.join(ROOT, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        im.save(full)
    if "--preview" in sys.argv:
        k = 6
        tiles = []
        for key, im in icons.items():
            if key.endswith("aosong_senate.png"):
                im = tinted(im, RED, YELLOW)
            elif key.endswith("aosong_national.png"):
                im = tinted(im, (38, 101, 191), (250, 250, 255))       # 原版调色板 navy_4 的旗面、旗帜色
            tiles.append(im.resize((im.width * k, im.height * k), Image.NEAREST))
        W = sum(t.width for t in tiles) + 8 * (len(tiles) + 1)
        H = max(t.height for t in tiles) + 16
        sheet = Image.new("RGBA", (W, H), (74, 78, 68, 255))
        x = 8
        for t in tiles:
            sheet.alpha_composite(t, (x, 8))
            x += t.width + 8
        idx = sys.argv.index("--preview")
        out = sys.argv[idx + 1] if len(sys.argv) > idx + 1 else os.path.join(HERE, "transmigrant_preview.png")
        sheet.save(out)
        print("preview:", out)
