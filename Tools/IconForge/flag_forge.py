"""国旗(用户 2026-10-29："给我有写过的文明都弄一个符合现实世界的古典国旗，古典时代都用这个""现代就优先使用理念作为国旗"
"酋长国或者原始国家就只用随机的原版图""国旗的像素可以不看原版，把像素都调高点，这样能显示更多的细节")。

  · 各文明一面古典旗(照史上的旗：周制交龙旂、罗马军旗、君士坦丁拉布兰旗、卡佩百合旗、神罗双头鹰旗、维京渡鸦旗、
    罗斯军旗、倭马亚白旗、德拉夫什·卡维亚尼、埃及有翼日轮、天竺日旗、圣殿七枝灯台、阿兹特克羽冠、雷鸟……)；
  · 各现代理念一面旗(按理念的通行意象，不用任何真实国家、法西斯党派的旗与符号)；
  · 原色整面画，游戏里不再乘国家颜色(见 KingdomFlagSystem)；
  · 风格照 STYLE.md 的"冷峻写实 · 像素输出"(用户："你这国旗太丑了，我之前和你说的图标设计方案风格你忘记了?")：
    先在大画布上写实画(纹样金属浮雕、布纹颗粒、受光)，再冷峻调色(降饱和、提对比、金色转氧化旧铜)，缩成像素图
    (原版旗帜 54×54，这里两倍 108×108 显得出细节)、透明度二值化、限色、1px 纯黑描边与右下投影；
    不排出像现代国旗的配色(日本用锦之御旗、犹太不用蓝白条、游牧不用天蓝配金)。

现在只作纹样画法库：成品旗由 flag_formal.py 出(用户要"庄严严肃的正式国旗风格，而且要有像素感")。
用法：python Tools/IconForge/flag_formal.py [--preview]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from scipy.ndimage import distance_transform_edt, gaussian_filter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "GameResources", "ui", "flags")

SIZE = 216
SS = 3
# 像素输出边长(原版旗帜的两倍)
PIXEL = 108
N = SIZE * SS
# 旗面(最终像素)
BX0, BY0, BX1, BY1 = 50, 36, 166, 184
OUTLINE = (24, 18, 14, 255)
GOLD = (222, 176, 62, 255)
GOLD_DARK = (128, 84, 22, 255)
WOOD = (92, 58, 32, 255)
# 旗面以外的部件(流苏、穗、乳)画在这层上，不按旗面形状裁(见 make)
EXT = None


def S(v):
    return int(round(v * SS))


def box():
    return S(BX0), S(BY0), S(BX1), S(BY1)


def hexc(h, a=255):
    h = h.lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a


def font(size, bold=True):
    for name in ("timesbd.ttf" if bold else "times.ttf", "georgiab.ttf", "DejaVuSerif-Bold.ttf"):
        try:
            return ImageFont.truetype(name, S(size))
        except OSError:
            continue
    return ImageFont.load_default()


# ───────── 旗面形状 ─────────

def body_mask(shape):
    m = Image.new("L", (N, N), 0)
    d = ImageDraw.Draw(m)
    x0, y0, x1, y1 = box()
    w, h = x1 - x0, y1 - y0
    if shape == "rect":
        d.rectangle([x0, y0, x1, y1], fill=255)
    elif shape == "short":  # 罗马军旗：方旗，下有流苏
        d.rectangle([x0, y0, x1, y0 + int(h * 0.78)], fill=255)
    elif shape == "swallow":
        d.polygon([(x0, y0), (x1, y0), (x1, y1), ((x0 + x1) // 2, y1 - int(h * 0.2)), (x0, y1)], fill=255)
    elif shape == "swallow3":
        third = w / 3
        pts = [(x0, y0), (x1, y0), (x1, y1)]
        for k in (2, 1):
            pts += [(x0 + third * k + third / 2, y1 - int(h * 0.16)), (x0 + third * k, y1)]
        pts += [(x0 + third / 2, y1 - int(h * 0.16)), (x0, y1)]
        d.polygon(pts, fill=255)
    elif shape == "point":
        d.polygon([(x0, y0), (x1, y0), (x1, y1 - int(h * 0.2)), ((x0 + x1) // 2, y1), (x0, y1 - int(h * 0.2))], fill=255)
    elif shape == "round":
        r = w // 2
        d.rectangle([x0, y0, x1, y1 - r], fill=255)
        d.pieslice([x0, y1 - 2 * r, x1, y1], 0, 180, fill=255)
    elif shape == "nobori":  # 幟：窄长，左缘有乳
        nx0 = x0 + int(w * 0.16)
        nx1 = x1 - int(w * 0.16)
        d.rectangle([nx0, y0, nx1, y1], fill=255)
    elif shape == "pennant2":  # 天竺双尖旗：底边两个尖
        d.polygon([(x0, y0), (x1, y0), (x1, y1 - int(h * 0.18)), (x0 + w * 0.75, y1), (x0 + w * 0.5, y1 - int(h * 0.18)),
                   (x0 + w * 0.25, y1), (x0, y1 - int(h * 0.18))], fill=255)
    elif shape == "leaf":
        d.rectangle([x0, y0, x1, y1 - int(h * 0.28)], fill=255)
        d.polygon([(x0, y1 - int(h * 0.28)), (x1, y1 - int(h * 0.28)), ((x0 + x1) // 2, y1)], fill=255)
        d.chord([x0, y1 - int(h * 0.5), x1, y1], 0, 180, fill=255)
    return m


def nobori_box():
    x0, y0, x1, y1 = box()
    w = x1 - x0
    return x0 + int(w * 0.16), y0, x1 - int(w * 0.16), y1


# ───────── 纹样小件 ─────────

def star_points(cx, cy, r_out, r_in, n=5, rot=-90):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def outlined_polygon(d, pts, fill, line=OUTLINE, width=None):
    d.polygon(pts, fill=fill)
    d.line(pts + [pts[0]], fill=line, width=width or S(1.4), joint="curve")


def outlined_ellipse(d, b, fill, line=OUTLINE, width=None):
    d.ellipse(b, fill=fill, outline=line, width=width or S(1.4))


def fat_line(d, p, q, width, fill, line=OUTLINE, edge=None):
    """圆头粗线段，带描边"""
    edge = edge if edge is not None else S(1.4)
    for col, wd in ((line, width + 2 * edge), (fill, width)):
        d.line([p, q], fill=col, width=int(wd))
        for (x, y) in (p, q):
            d.ellipse([x - wd / 2, y - wd / 2, x + wd / 2, y + wd / 2], fill=col)


def thick_path(d, pts, width, fill, line=OUTLINE, edge=None):
    """一条粗线(龙身、拉布兰十字、灯台的枝)：先画外描边再画本色"""
    edge = edge if edge is not None else S(1.4)
    for p in pts:
        r = width / 2 + edge
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=line)
    for p in pts:
        r = width / 2
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill)


def bezier(p0, p1, p2, p3, steps=60):
    out = []
    for i in range(steps + 1):
        t = i / steps
        a = (1 - t) ** 3
        b = 3 * (1 - t) ** 2 * t
        c = 3 * (1 - t) * t * t
        e = t ** 3
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + e * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + e * p3[1]))
    return out


def rect_center():
    x0, y0, x1, y1 = box()
    return (x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0


# ───────── 各文明 ─────────

def em_dragon(d):
    """交龙旂(周制"交龙为旂")：朱底金龙，火焰宝珠，飞边金齿"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    for i in range(10):
        yy = y0 + S(4) + i * (h * 0.74) / 10
        d.polygon([(x1, yy), (x1 - S(8), yy + S(4.5)), (x1, yy + S(9))], fill=GOLD)
    belly = (246, 214, 120, 255)
    path = bezier((cx - w * 0.06, y0 + h * 0.26), (cx + w * 0.62, y0 + h * 0.24), (cx - w * 0.62, y0 + h * 0.6),
                  (cx + w * 0.26, y0 + h * 0.8), 110)
    n = len(path)
    widths = [S(17) * (1 - 0.62 * i / n) + S(3) for i in range(n)]
    # 四足(先画，身子压在上面)
    for idx, side in ((22, -1), (36, 1), (66, -1), (82, 1)):
        p, q = path[idx], path[idx + 2]
        tx, ty = q[0] - p[0], q[1] - p[1]
        ln = math.hypot(tx, ty) or 1
        nx, ny = -ty / ln * side, tx / ln * side
        knee = (p[0] + nx * S(14) - tx / ln * S(4), p[1] + ny * S(14) - ty / ln * S(4))
        foot = (knee[0] + nx * S(6) + tx / ln * S(6), knee[1] + ny * S(6) + ty / ln * S(6))
        fat_line(d, p, knee, S(5), GOLD, GOLD_DARK)
        fat_line(d, knee, foot, S(4), GOLD, GOLD_DARK)
        for k in (-1, 0, 1):
            ang = math.atan2(ny, nx) + k * 0.6
            d.line([foot, (foot[0] + math.cos(ang) * S(6), foot[1] + math.sin(ang) * S(6))], fill=(196, 64, 36, 255), width=S(1.8))
    # 背鳍
    for i in range(10, n - 8, 7):
        p, q = path[i], path[i + 3]
        nx, ny = -(q[1] - p[1]), q[0] - p[0]
        ln = math.hypot(nx, ny) or 1
        tip = ((p[0] + q[0]) / 2 + nx / ln * widths[i] * 0.95, (p[1] + q[1]) / 2 + ny / ln * widths[i] * 0.95)
        d.polygon([p, tip, q], fill=(200, 66, 38, 255), outline=GOLD_DARK)
    for (p, wd) in zip(path, widths):
        r = wd / 2 + S(1.6)
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=GOLD_DARK)
    for (p, wd) in zip(path, widths):
        r = wd / 2
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=GOLD)
    # 腹甲(身子一侧浅色)与鳞
    for i in range(4, n - 4, 3):
        p, q = path[i], path[i + 1]
        nx, ny = (q[1] - p[1]), -(q[0] - p[0])
        ln = math.hypot(nx, ny) or 1
        bx, by = p[0] + nx / ln * widths[i] * 0.22, p[1] + ny / ln * widths[i] * 0.22
        r = widths[i] * 0.2
        d.ellipse([bx - r, by - r * 0.6, bx + r, by + r * 0.6], fill=belly)
    for i in range(6, n - 6, 5):
        p = path[i]
        r = widths[i] * 0.28
        d.arc([p[0] - r, p[1] - r, p[0] + r, p[1] + r], 200, 340, fill=GOLD_DARK, width=S(1.2))
    # 尾鬣
    tx, ty = path[-1]
    d.polygon([(tx, ty), (tx + S(12), ty - S(6)), (tx + S(6), ty + S(2)), (tx + S(14), ty + S(6)), (tx, ty + S(4))], fill=(200, 66, 38, 255))
    # 龙头(朝左，张口)
    hx, hy = path[0]
    mane = [(hx + S(10), hy - S(4)), (hx + S(20), hy - S(12)), (hx + S(14), hy - S(2)), (hx + S(22), hy + S(2)), (hx + S(12), hy + S(6)),
            (hx + S(18), hy + S(12)), (hx + S(6), hy + S(9))]
    d.polygon(mane, fill=(200, 66, 38, 255))
    upper = [(hx + S(8), hy - S(9)), (hx - S(8), hy - S(11)), (hx - S(22), hy - S(6)), (hx - S(26), hy - S(1)), (hx - S(14), hy + S(1)),
             (hx + S(6), hy + S(1))]
    lower = [(hx + S(4), hy + S(2)), (hx - S(12), hy + S(3)), (hx - S(22), hy + S(9)), (hx - S(10), hy + S(11)), (hx + S(6), hy + S(8))]
    d.polygon([(hx - S(14), hy + S(1)), (hx - S(24), hy + S(1)), (hx - S(20), hy + S(8)), (hx - S(10), hy + S(4))], fill=(150, 30, 30, 255))
    outlined_polygon(d, lower, GOLD, GOLD_DARK, S(1.6))
    outlined_polygon(d, upper, GOLD, GOLD_DARK, S(1.6))
    for k in range(3):
        d.polygon([(hx - S(14) - k * S(3), hy + S(1)), (hx - S(15.5) - k * S(3), hy + S(3.5)), (hx - S(17) - k * S(3), hy + S(1))], fill=(250, 246, 236, 255))
    # 鹿角
    for (bx, by, ang) in ((hx + S(2), hy - S(9), -2.2), (hx + S(7), hy - S(8), -1.8)):
        tip = (bx + math.cos(ang) * S(16), by + math.sin(ang) * S(16))
        fat_line(d, (bx, by), tip, S(2.4), GOLD, GOLD_DARK, S(1))
        mid = ((bx + tip[0]) / 2, (by + tip[1]) / 2)
        fat_line(d, mid, (mid[0] - S(6), mid[1] - S(3)), S(1.8), GOLD, GOLD_DARK, S(0.8))
    d.ellipse([hx - S(8), hy - S(8), hx - S(3), hy - S(3)], fill=(250, 240, 210, 255), outline=GOLD_DARK, width=S(1))
    d.ellipse([hx - S(6.5), hy - S(6.5), hx - S(4.5), hy - S(4.5)], fill=OUTLINE)
    # 龙须
    for (yy, k) in ((-3, -1), (0, 1)):
        d.line(bezier((hx - S(24), hy + S(yy)), (hx - S(34), hy + S(yy) + k * S(2)), (hx - S(30), hy + S(yy) + k * S(10)),
                      (hx - S(38), hy + S(yy) + k * S(12)), 14), fill=GOLD_DARK, width=S(1.2))
    # 火焰宝珠
    px, py = hx - S(34), hy - S(18)
    d.polygon([(px - S(6), py + S(2)), (px - S(3), py - S(10)), (px, py - S(4)), (px + S(4), py - S(12)), (px + S(7), py + S(2))],
              fill=(240, 120, 40, 255))
    outlined_ellipse(d, [px - S(6), py - S(4), px + S(6), py + S(8)], (250, 244, 226, 255), GOLD_DARK)
    d.arc([px - S(3), py - S(1), px + S(3), py + S(5)], 180, 360, fill=(220, 120, 60, 255), width=S(1.2))


def em_shanhai(d):
    """山海：青底，金日、白山、浪纹"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    sun = (cx + w * 0.2, y0 + h * 0.2)
    for i in range(16):
        a = math.radians(i * 22.5)
        d.line([sun, (sun[0] + math.cos(a) * S(20), sun[1] + math.sin(a) * S(20))], fill=GOLD, width=S(1.6))
    outlined_ellipse(d, [sun[0] - S(12), sun[1] - S(12), sun[0] + S(12), sun[1] + S(12)], GOLD, GOLD_DARK)
    base = y0 + h * 0.72
    for (mx, mh, mw) in ((cx - w * 0.22, h * 0.36, w * 0.36), (cx + w * 0.12, h * 0.46, w * 0.42), (cx + w * 0.34, h * 0.28, w * 0.3)):
        pts = [(mx - mw / 2, base), (mx - mw * 0.12, base - mh * 0.7), (mx, base - mh), (mx + mw * 0.14, base - mh * 0.75), (mx + mw / 2, base)]
        outlined_polygon(d, pts, (236, 232, 218, 255), (40, 60, 58, 255))
        d.polygon([(mx, base - mh), (mx + mw * 0.14, base - mh * 0.75), (mx + mw * 0.06, base - mh * 0.55), (mx - mw * 0.05, base - mh * 0.7)],
                  fill=(176, 196, 190, 255))
    for row in range(3):
        yy = base + S(8) + row * S(12)
        for k in range(6):
            xx = x0 + k * w / 5.5
            d.arc([xx, yy - S(7), xx + S(18), yy + S(7)], 180, 360, fill=(236, 232, 218, 255), width=S(2.2))


def em_roma(d):
    """罗马军旗(vexillum)：绯底金字 SPQR、金月桂冠，下垂金流苏"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    fy1 = y0 + int(h * 0.78)
    d.rectangle([x0 + S(4), y0 + S(4), x1 - S(4), fy1 - S(4)], outline=GOLD, width=S(2))
    wc = (cx, y0 + h * 0.3)
    r = S(24)
    for side in (-1, 1):
        for i in range(8):
            a = math.radians(110 + side * 0 + i * 18) if side < 0 else math.radians(70 - i * 18)
            px, py = wc[0] + side * 0 + r * math.cos(a), wc[1] + r * math.sin(a)
            ang = a + math.pi / 2 * side
            lx, ly = math.cos(ang) * S(6), math.sin(ang) * S(6)
            d.ellipse([px - S(3.5) + lx * 0.3, py - S(2) + ly * 0.3, px + S(3.5) + lx * 0.3, py + S(2) + ly * 0.3], fill=GOLD,
                      outline=GOLD_DARK, width=S(0.8))
    d.ellipse([wc[0] - S(6), wc[1] - S(6), wc[0] + S(6), wc[1] + S(6)], fill=(150, 24, 28, 255), outline=GOLD, width=S(2))
    f = font(19)
    t = "SPQR"
    tw = d.textlength(t, font=f)
    d.text((cx - tw / 2 + S(1), y0 + h * 0.5 + S(1)), t, font=f, fill=OUTLINE)
    d.text((cx - tw / 2, y0 + h * 0.5), t, font=f, fill=GOLD)
    for i in range(int((x1 - x0) / S(6)) + 1):
        xx = x0 + i * S(6) + S(2)
        EXT.line([(xx, fy1), (xx, fy1 + S(16))], fill=GOLD, width=S(2.4))
        EXT.ellipse([xx - S(2), fy1 + S(14), xx + S(2), fy1 + S(19)], fill=GOLD_DARK)


def eagle_top(d):
    """军旗顶上的金鹰"""
    cx = (S(BX0) + S(BX1)) / 2
    y = S(BY0) - S(22)
    d.rectangle([cx - S(1.5), y, cx + S(1.5), S(BY0)], fill=GOLD_DARK)
    wing_l = [(cx - S(3), y), (cx - S(20), y - S(12)), (cx - S(22), y - S(4)), (cx - S(14), y + S(2)), (cx - S(4), y + S(4))]
    wing_r = [(cx + x - cx if False else 2 * cx - px, py) for (px, py) in wing_l]
    outlined_polygon(d, wing_l, GOLD, GOLD_DARK)
    outlined_polygon(d, wing_r, GOLD, GOLD_DARK)
    outlined_ellipse(d, [cx - S(5), y - S(6), cx + S(5), y + S(6)], GOLD, GOLD_DARK)
    outlined_ellipse(d, [cx - S(4), y - S(13), cx + S(3), y - S(5)], GOLD, GOLD_DARK)
    d.polygon([(cx - S(4), y - S(10)), (cx - S(9), y - S(8)), (cx - S(4), y - S(7))], fill=GOLD_DARK)


def em_labarum(d):
    """拉布兰旗：紫底金 Chi-Rho 套月桂环，金边金流苏"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    fy1 = y0 + int(h * 0.78)
    d.rectangle([x0 + S(4), y0 + S(4), x1 - S(4), fy1 - S(4)], outline=GOLD, width=S(2))
    c = (cx, y0 + h * 0.38)
    R = S(34)
    d.ellipse([c[0] - R, c[1] - R, c[0] + R, c[1] + R], outline=GOLD_DARK, width=S(4))
    d.ellipse([c[0] - R, c[1] - R, c[0] + R, c[1] + R], outline=GOLD, width=S(2.4))
    for i in range(28):
        a = math.radians(i * 360 / 28)
        px, py = c[0] + math.cos(a) * R, c[1] + math.sin(a) * R
        ang = a + math.pi / 2
        d.polygon([(px - math.cos(ang) * S(4), py - math.sin(ang) * S(4)), (px + math.cos(a) * S(2.5), py + math.sin(a) * S(2.5)),
                   (px + math.cos(ang) * S(4), py + math.sin(ang) * S(4)), (px - math.cos(a) * S(2.5), py - math.sin(a) * S(2.5))], fill=GOLD)
    k = S(19)
    fat_line(d, (c[0] - k, c[1] - k), (c[0] + k, c[1] + k), S(5), GOLD, GOLD_DARK)
    fat_line(d, (c[0] + k, c[1] - k), (c[0] - k, c[1] + k), S(5), GOLD, GOLD_DARK)
    fat_line(d, (c[0], c[1] - S(26)), (c[0], c[1] + S(26)), S(5), GOLD, GOLD_DARK)
    box_p = [c[0] - S(2), c[1] - S(28), c[0] + S(18), c[1] - S(8)]
    d.arc(box_p, -90, 90, fill=GOLD_DARK, width=S(8))
    d.arc([box_p[0] + S(0.8), box_p[1] + S(0.8), box_p[2] - S(0.8), box_p[3] - S(0.8)], -90, 90, fill=GOLD, width=S(5))
    for i in range(int((x1 - x0) / S(6)) + 1):
        xx = x0 + i * S(6) + S(2)
        EXT.line([(xx, fy1), (xx, fy1 + S(16))], fill=GOLD, width=S(2.4))


def fleur(d, cx, cy, s):
    col, edge = GOLD, GOLD_DARK
    centre = [(cx, cy - s), (cx + s * 0.28, cy - s * 0.45), (cx + s * 0.16, cy + s * 0.1), (cx - s * 0.16, cy + s * 0.1),
              (cx - s * 0.28, cy - s * 0.45)]
    outlined_polygon(d, centre, col, edge)
    for sx in (-1, 1):
        petal = [(cx + sx * s * 0.12, cy + s * 0.05), (cx + sx * s * 0.55, cy - s * 0.55), (cx + sx * s * 0.85, cy - s * 0.35),
                 (cx + sx * s * 0.75, cy - s * 0.05), (cx + sx * s * 0.55, cy + s * 0.12), (cx + sx * s * 0.66, cy + s * 0.02),
                 (cx + sx * s * 0.5, cy - s * 0.3), (cx + sx * s * 0.25, cy + s * 0.2)]
        outlined_polygon(d, petal, col, edge)
        foot = [(cx + sx * s * 0.08, cy + s * 0.28), (cx + sx * s * 0.42, cy + s * 0.55), (cx + sx * s * 0.2, cy + s * 0.62)]
        outlined_polygon(d, foot, col, edge)
    d.rectangle([cx - s * 0.42, cy + s * 0.12, cx + s * 0.42, cy + s * 0.3], fill=col, outline=edge, width=S(1.2))


def em_fleurs(d):
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    for (fx, fy) in ((cx - w * 0.22, y0 + h * 0.26), (cx + w * 0.22, y0 + h * 0.26), (cx, y0 + h * 0.58)):
        fleur(d, fx, fy, S(17))


def double_eagle(d, cx, cy, s, body, accent):
    """双头鹰(黑身红喙爪)"""
    for sx in (-1, 1):
        for k in range(5):
            base = (cx + sx * s * 0.18, cy - s * 0.15 + k * s * 0.09)
            tip = (cx + sx * s * (0.95 - k * 0.08), cy - s * (0.55 - k * 0.22))
            pts = [base, tip, (tip[0] - sx * s * 0.08, tip[1] + s * 0.14), (base[0], base[1] + s * 0.12)]
            outlined_polygon(d, pts, body, (60, 50, 40, 255))
        neck = [(cx + sx * s * 0.06, cy - s * 0.2), (cx + sx * s * 0.28, cy - s * 0.55), (cx + sx * s * 0.38, cy - s * 0.5),
                (cx + sx * s * 0.16, cy - s * 0.12)]
        outlined_polygon(d, neck, body, (60, 50, 40, 255))
        hx, hy = cx + sx * s * 0.34, cy - s * 0.6
        outlined_ellipse(d, [hx - s * 0.11, hy - s * 0.1, hx + s * 0.11, hy + s * 0.1], body, (60, 50, 40, 255))
        d.polygon([(hx + sx * s * 0.08, hy - s * 0.03), (hx + sx * s * 0.24, hy + s * 0.02), (hx + sx * s * 0.08, hy + s * 0.06)], fill=accent)
        d.line([(hx + sx * s * 0.2, hy + s * 0.04), (hx + sx * s * 0.28, hy + s * 0.12)], fill=accent, width=S(1.5))
        d.ellipse([hx + sx * s * 0.02 - S(1.5), hy - s * 0.04 - S(1.5), hx + sx * s * 0.02 + S(1.5), hy - s * 0.04 + S(1.5)],
                  fill=(240, 220, 160, 255))
        # 爪
        lx, ly = cx + sx * s * 0.16, cy + s * 0.42
        d.line([(cx + sx * s * 0.1, cy + s * 0.25), (lx, ly)], fill=body, width=S(3))
        for k in (-1, 0, 1):
            d.line([(lx, ly), (lx + sx * s * 0.06 + k * s * 0.05, ly + s * 0.1)], fill=accent, width=S(2))
    outlined_ellipse(d, [cx - s * 0.2, cy - s * 0.28, cx + s * 0.2, cy + s * 0.32], body, (60, 50, 40, 255))
    tail = [(cx - s * 0.16, cy + s * 0.25), (cx + s * 0.16, cy + s * 0.25), (cx + s * 0.24, cy + s * 0.62), (cx, cy + s * 0.52),
            (cx - s * 0.24, cy + s * 0.62)]
    outlined_polygon(d, tail, body, (60, 50, 40, 255))


def em_reichsadler(d):
    cx, cy, w, h = rect_center()
    double_eagle(d, cx, S(BY0) + h * 0.48, w * 0.5, (22, 20, 20, 255), (196, 40, 36, 255))


def em_raven(d):
    """渡鸦旗：白底黑鸦展翅、红边流苏"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    c = (cx, y0 + h * 0.42)
    black = (20, 20, 24, 255)
    for sx in (-1, 1):
        wing = [(c[0] + sx * S(4), c[1] - S(4)), (c[0] + sx * S(26), c[1] - S(32)), (c[0] + sx * S(46), c[1] - S(36)),
                (c[0] + sx * S(40), c[1] - S(26)), (c[0] + sx * S(46), c[1] - S(20)), (c[0] + sx * S(36), c[1] - S(14)),
                (c[0] + sx * S(40), c[1] - S(6)), (c[0] + sx * S(26), c[1] - S(4)), (c[0] + sx * S(8), c[1] + S(10))]
        d.polygon(wing, fill=black)
    d.ellipse([c[0] - S(9), c[1] - S(12), c[0] + S(9), c[1] + S(22)], fill=black)
    d.ellipse([c[0] - S(8), c[1] - S(24), c[0] + S(6), c[1] - S(10)], fill=black)
    d.polygon([(c[0] - S(7), c[1] - S(19)), (c[0] - S(20), c[1] - S(15)), (c[0] - S(7), c[1] - S(13))], fill=black)
    d.ellipse([c[0] - S(4), c[1] - S(20), c[0] - S(1), c[1] - S(17)], fill=(230, 230, 230, 255))
    d.polygon([(c[0] - S(8), c[1] + S(18)), (c[0] + S(8), c[1] + S(18)), (c[0] + S(12), c[1] + S(36)), (c[0], c[1] + S(30)),
               (c[0] - S(12), c[1] + S(36))], fill=black)
    for sx in (-1, 1):
        d.line([(c[0] + sx * S(4), c[1] + S(20)), (c[0] + sx * S(9), c[1] + S(30))], fill=black, width=S(2))


def em_spas(d):
    """罗斯军旗：绯底，金光轮里白十字(救主圣容的光环)"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    c = (cx, y0 + h * 0.36)
    for i in range(24):
        a = math.radians(i * 15)
        d.line([c, (c[0] + math.cos(a) * S(36), c[1] + math.sin(a) * S(36))], fill=(230, 190, 90, 255), width=S(1.4))
    outlined_ellipse(d, [c[0] - S(26), c[1] - S(26), c[0] + S(26), c[1] + S(26)], GOLD, GOLD_DARK, S(2))
    d.ellipse([c[0] - S(19), c[1] - S(19), c[0] + S(19), c[1] + S(19)], fill=(250, 238, 200, 255))
    for (dx, dy, ww, hh) in ((0, 0, 5, 30), (0, 0, 30, 5)):
        d.rectangle([c[0] - S(ww / 2), c[1] - S(hh / 2), c[0] + S(ww / 2), c[1] + S(hh / 2)], fill=(168, 30, 36, 255))
    d.line([(x0 + S(6), y0 + h * 0.66), (x1 - S(6), y0 + h * 0.66)], fill=GOLD, width=S(2.5))
    d.line([(x0 + S(6), y0 + h * 0.7), (x1 - S(6), y0 + h * 0.7)], fill=(240, 232, 214, 255), width=S(1.5))


def em_umayyad(d):
    """倭马亚白旗：白底绿边，绿八角星(两方相叠)、金心"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    green = (122, 92, 40, 255)
    d.rectangle([x0 + S(5), y0 + S(5), x1 - S(5), y1 - S(5)], outline=green, width=S(4))
    c = (cx, y0 + h * 0.46)
    r = S(30)
    for rot in (0, 45):
        pts = [(c[0] + r * math.cos(math.radians(rot + 45 + 90 * k)), c[1] + r * math.sin(math.radians(rot + 45 + 90 * k))) for k in range(4)]
        d.polygon(pts, fill=green)
    d.ellipse([c[0] - S(12), c[1] - S(12), c[0] + S(12), c[1] + S(12)], fill=(236, 230, 214, 255))
    outlined_ellipse(d, [c[0] - S(7), c[1] - S(7), c[0] + S(7), c[1] + S(7)], GOLD, GOLD_DARK)
    for i in range(10):
        xx = x0 + S(12) + i * (w - S(24)) / 9
        d.rectangle([xx - S(2), y1 - S(18), xx + S(2), y1 - S(12)], fill=green)


def em_derafsh(d):
    """德拉夫什·卡维亚尼：紫红底，金菱格，正中金四芒星嵌红宝石，下垂红黄紫三色流苏"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    fy1 = y0 + int(h * 0.78)
    step = S(16)
    for k in range(-12, 14):
        d.line([(x0 + k * step, y0), (x0 + k * step + (fy1 - y0), fy1)], fill=(200, 150, 60, 255), width=S(1.2))
        d.line([(x0 + k * step, fy1), (x0 + k * step + (fy1 - y0), y0)], fill=(200, 150, 60, 255), width=S(1.2))
    d.rectangle([x0, y0, x1, fy1], outline=GOLD, width=S(4))
    c = (cx, y0 + h * 0.4)
    outlined_polygon(d, star_points(c[0], c[1], S(34), S(10), 4, -90), GOLD, GOLD_DARK)
    outlined_polygon(d, star_points(c[0], c[1], S(20), S(8), 4, -45), (240, 200, 90, 255), GOLD_DARK)
    outlined_ellipse(d, [c[0] - S(6), c[1] - S(6), c[0] + S(6), c[1] + S(6)], (190, 30, 40, 255), GOLD_DARK)
    cols = [(190, 40, 40, 255), (230, 180, 50, 255), (110, 40, 110, 255)]
    for i in range(int((x1 - x0) / S(8)) + 1):
        xx = x0 + i * S(8) + S(3)
        col = cols[i % 3]
        EXT.line([(xx, fy1), (xx, fy1 + S(22))], fill=col, width=S(3))
        EXT.ellipse([xx - S(3), fy1 + S(20), xx + S(3), fy1 + S(27)], fill=col)


def em_winged_sun(d):
    """有翼日轮：青金石蓝底，红日两侧金翼与双蛇，下为金蓝相间的条纹"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    c = (cx, y0 + h * 0.36)
    for sx in (-1, 1):
        for k in range(4):
            yy = c[1] - S(8) + k * S(5)
            ln = S(52) - k * S(9)
            pts = [(c[0] + sx * S(12), yy), (c[0] + sx * ln, yy - S(6) + k * S(1)), (c[0] + sx * (ln - S(4)), yy + S(4)),
                   (c[0] + sx * S(12), yy + S(5))]
            outlined_polygon(d, pts, GOLD if k % 2 == 0 else (240, 206, 110, 255), GOLD_DARK, S(1))
        # 圣蛇
        ux = c[0] + sx * S(17)
        d.line([(ux, c[1] + S(10)), (ux, c[1] - S(6))], fill=GOLD, width=S(3.5))
        d.ellipse([ux - S(4), c[1] - S(12), ux + S(4), c[1] - S(4)], fill=GOLD, outline=GOLD_DARK, width=S(1))
    outlined_ellipse(d, [c[0] - S(13), c[1] - S(13), c[0] + S(13), c[1] + S(13)], (196, 46, 34, 255), GOLD_DARK, S(2))
    for i in range(6):
        yy = y0 + h * 0.62 + i * S(9)
        d.rectangle([x0 + S(6), yy, x1 - S(6), yy + S(4)], fill=GOLD if i % 2 == 0 else (60, 110, 190, 255))


def em_surya(d):
    """天竺日旗：番红花色双尖旗，金日"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    c = (cx, y0 + h * 0.38)
    rays = star_points(c[0], c[1], S(42), S(24), 16, -90)
    outlined_polygon(d, rays, (252, 214, 96, 255), (176, 92, 24, 255))
    outlined_ellipse(d, [c[0] - S(22), c[1] - S(22), c[0] + S(22), c[1] + S(22)], (246, 186, 40, 255), (176, 92, 24, 255), S(2))
    d.ellipse([c[0] - S(14), c[1] - S(14), c[0] + S(14), c[1] + S(14)], outline=(196, 96, 24, 255), width=S(2))
    for i in range(8):
        a = math.radians(i * 45)
        d.line([c, (c[0] + math.cos(a) * S(14), c[1] + math.sin(a) * S(14))], fill=(196, 96, 24, 255), width=S(1.4))
    d.line([(x0 + S(5), y0 + h * 0.66), (x1 - S(5), y0 + h * 0.66)], fill=(196, 60, 30, 255), width=S(3))


def em_menorah(d):
    """七枝灯台：深蓝底，上下白条，金灯台"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    for yy in (y0 + h * 0.08, y1 - h * 0.12):
        d.rectangle([x0 + S(6), yy, x1 - S(6), yy + S(2)], fill=GOLD)
    base_y = y0 + h * 0.74
    top_y = y0 + h * 0.28
    c = cx
    for k in range(1, 4):
        r = S(11) * k
        d.arc([c - r, top_y - r + S(30) - r * 0.0, c + r, top_y + r + S(30)], 0, 180, fill=GOLD_DARK, width=S(6))
        d.arc([c - r, top_y - r + S(30), c + r, top_y + r + S(30)], 0, 180, fill=GOLD, width=S(3.5))
        for sx in (-1, 1):
            arm_x = c + sx * r
            d.rectangle([arm_x - S(2), top_y, arm_x + S(2), top_y + S(30)], fill=GOLD, outline=GOLD_DARK, width=S(1))
    d.rectangle([c - S(2.5), top_y, c + S(2.5), base_y], fill=GOLD, outline=GOLD_DARK, width=S(1))
    d.polygon([(c - S(18), base_y + S(10)), (c + S(18), base_y + S(10)), (c + S(8), base_y - S(2)), (c - S(8), base_y - S(2))], fill=GOLD,
              outline=GOLD_DARK)
    for k in range(-3, 4):
        fx = c + k * S(11)
        d.polygon([(fx - S(3), top_y - S(1)), (fx, top_y - S(11)), (fx + S(3), top_y - S(1))], fill=(250, 150, 40, 255))
        d.ellipse([fx - S(4), top_y - S(3), fx + S(4), top_y + S(2)], fill=GOLD, outline=GOLD_DARK, width=S(1))


def em_quetzal(d):
    """阿兹特克羽冠旗：绿底，绿蓝相间的长羽扇，金日盘，红阶梯纹边"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    for i in range(int((x1 - x0) / S(10)) + 1):
        xx = x0 + i * S(10)
        d.polygon([(xx, y1), (xx + S(5), y1 - S(8)), (xx + S(10), y1)], fill=(180, 40, 36, 255))
    c = (cx, y0 + h * 0.52)
    for i in range(13):
        a = math.radians(-180 + i * 15)
        tip = (c[0] + math.cos(a) * S(48), c[1] + math.sin(a) * S(48))
        mid = (c[0] + math.cos(a) * S(30), c[1] + math.sin(a) * S(30))
        nx, ny = -math.sin(a) * S(7), math.cos(a) * S(7)
        col = (40, 170, 120, 255) if i % 2 == 0 else (40, 110, 180, 255)
        outlined_polygon(d, [c, (mid[0] + nx, mid[1] + ny), tip, (mid[0] - nx, mid[1] - ny)], col, (20, 50, 40, 255), S(1))
        d.ellipse([tip[0] - S(3), tip[1] - S(3), tip[0] + S(3), tip[1] + S(3)], fill=GOLD)
    outlined_ellipse(d, [c[0] - S(16), c[1] - S(16), c[0] + S(16), c[1] + S(16)], GOLD, GOLD_DARK, S(2))
    d.ellipse([c[0] - S(9), c[1] - S(9), c[0] + S(9), c[1] + S(9)], fill=(190, 40, 36, 255))
    for i in range(8):
        a = math.radians(i * 45 + 22)
        d.line([(c[0] + math.cos(a) * S(10), c[1] + math.sin(a) * S(10)), (c[0] + math.cos(a) * S(15), c[1] + math.sin(a) * S(15))],
               fill=GOLD_DARK, width=S(1.4))


def em_thunderbird(d):
    """雷鸟：鞣皮底，黑红几何雷鸟，下垂皮穗"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    black, red = (26, 22, 20, 255), (170, 40, 30, 255)
    c = (cx, y0 + h * 0.4)
    for sx in (-1, 1):
        wing = [(c[0] + sx * S(6), c[1] - S(8)), (c[0] + sx * S(46), c[1] - S(22)), (c[0] + sx * S(46), c[1] - S(10)),
                (c[0] + sx * S(6), c[1] + S(6))]
        d.polygon(wing, fill=black)
        for k in range(4):
            fx = c[0] + sx * (S(16) + k * S(8))
            d.polygon([(fx, c[1] - S(12) - k * S(2.6)), (fx + sx * S(5), c[1] - S(12) - k * S(2.6)), (fx + sx * S(2), c[1] + S(4))], fill=red)
        zz = [(c[0] + sx * S(40), c[1] - S(8)), (c[0] + sx * S(34), c[1] + S(4)), (c[0] + sx * S(42), c[1] + S(8)),
              (c[0] + sx * S(36), c[1] + S(22))]
        d.line(zz, fill=red, width=S(2.5))
    d.rectangle([c[0] - S(7), c[1] - S(10), c[0] + S(7), c[1] + S(20)], fill=black)
    d.polygon([(c[0] - S(7), c[1] + S(20)), (c[0] + S(7), c[1] + S(20)), (c[0] + S(16), c[1] + S(36)), (c[0] - S(16), c[1] + S(36))], fill=black)
    d.ellipse([c[0] - S(8), c[1] - S(26), c[0] + S(8), c[1] - S(10)], fill=black)
    d.polygon([(c[0] - S(3), c[1] - S(20)), (c[0] - S(16), c[1] - S(16)), (c[0] - S(3), c[1] - S(13))], fill=red)
    d.polygon([(c[0] - S(3), c[1] - S(26)), (c[0] + S(4), c[1] - S(36)), (c[0] + S(6), c[1] - S(24))], fill=black)
    d.ellipse([c[0] - S(4), c[1] - S(21), c[0] - S(1), c[1] - S(18)], fill=(230, 210, 170, 255))
    for i in range(int((x1 - x0) / S(7)) + 1):
        xx = x0 + i * S(7) + S(2)
        EXT.line([(xx, y1), (xx, y1 + S(14))], fill=(150, 112, 70, 255), width=S(2))


def em_crown(d, cx=None, cy=None, s=None, col=GOLD, edge=GOLD_DARK, jewel=(180, 30, 40, 255)):
    cxx, cyy, w, h = rect_center()
    cx = cx if cx is not None else cxx
    cy = cy if cy is not None else S(BY0) + h * 0.42
    s = s or w * 0.36
    pts = [(cx - s, cy + s * 0.4), (cx - s, cy - s * 0.3), (cx - s * 0.6, cy + s * 0.05), (cx - s * 0.32, cy - s * 0.55),
           (cx, cy - s * 0.05), (cx + s * 0.32, cy - s * 0.55), (cx + s * 0.6, cy + s * 0.05), (cx + s, cy - s * 0.3), (cx + s, cy + s * 0.4)]
    outlined_polygon(d, pts, col, edge, S(1.8))
    d.rectangle([cx - s, cy + s * 0.25, cx + s, cy + s * 0.45], fill=col, outline=edge, width=S(1.4))
    for px, py in ((cx - s, cy - s * 0.3), (cx - s * 0.32, cy - s * 0.55), (cx + s * 0.32, cy - s * 0.55), (cx + s, cy - s * 0.3)):
        d.ellipse([px - S(3.5), py - S(3.5), px + S(3.5), py + S(3.5)], fill=col, outline=edge, width=S(1))
    for k in (-0.55, 0, 0.55):
        d.ellipse([cx + s * k - S(3), cy + s * 0.35 - S(3), cx + s * k + S(3), cy + s * 0.35 + S(3)], fill=jewel)


def em_sunburst_crown(d):
    """帝皇：黑底金光芒与皇冠"""
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.42)
    for i in range(24):
        a = math.radians(i * 15)
        ln = S(52) if i % 2 == 0 else S(38)
        d.polygon([(c[0] + math.cos(a - 0.06) * S(14), c[1] + math.sin(a - 0.06) * S(14)), (c[0] + math.cos(a) * ln, c[1] + math.sin(a) * ln),
                   (c[0] + math.cos(a + 0.06) * S(14), c[1] + math.sin(a + 0.06) * S(14))], fill=(196, 150, 56, 255))
    em_crown(d, c[0], c[1], w * 0.24)


def em_tree(d):
    """精灵：林绿底银树与新月"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    silver, edge = (222, 228, 232, 255), (90, 110, 120, 255)
    trunk = [(cx - S(5), y0 + h * 0.72), (cx - S(3), y0 + h * 0.42), (cx + S(3), y0 + h * 0.42), (cx + S(5), y0 + h * 0.72)]
    outlined_polygon(d, trunk, silver, edge)
    for (dx, dy, r) in ((0, 0.28, 22), (-18, 0.36, 16), (18, 0.36, 16), (-10, 0.2, 14), (12, 0.2, 14), (0, 0.13, 13)):
        outlined_ellipse(d, [cx + S(dx) - S(r), y0 + h * dy - S(r), cx + S(dx) + S(r), y0 + h * dy + S(r)], silver, edge)
    for (dx, dy) in ((-14, 0.3), (10, 0.25), (0, 0.18), (16, 0.38)):
        d.ellipse([cx + S(dx) - S(3), y0 + h * dy - S(2), cx + S(dx) + S(3), y0 + h * dy + S(2)], fill=(150, 200, 160, 255))
    mx, my = cx + w * 0.3, y0 + h * 0.1
    d.ellipse([mx - S(10), my - S(10), mx + S(10), my + S(10)], fill=silver)
    d.ellipse([mx - S(5), my - S(12), mx + S(13), my + S(6)], fill=(46, 94, 58, 255))
    for k in range(3):
        d.line([(cx - S(10) - k * S(6), y0 + h * 0.72), (cx - S(18) - k * S(8), y0 + h * 0.78)], fill=silver, width=S(2))
        d.line([(cx + S(10) + k * S(6), y0 + h * 0.72), (cx + S(18) + k * S(8), y0 + h * 0.78)], fill=silver, width=S(2))


def gear(d, cx, cy, r, teeth, col, edge, hole=None):
    pts = []
    for i in range(teeth * 4):
        a = math.radians(i * 360 / (teeth * 4))
        rr = r if (i // 2) % 2 == 0 else r * 0.8
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    outlined_polygon(d, pts, col, edge, S(1.6))
    if hole:
        d.ellipse([cx - r * 0.38, cy - r * 0.38, cx + r * 0.38, cy + r * 0.38], fill=hole, outline=edge, width=S(1.4))


def em_corporate(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.42)
    gear(d, c[0], c[1], S(34), 10, (200, 204, 210, 255), (60, 66, 74, 255), (58, 70, 82, 255))
    outlined_ellipse(d, [c[0] - S(10), c[1] - S(10), c[0] + S(10), c[1] + S(10)], GOLD, GOLD_DARK)
    d.rectangle([c[0] - S(1.5), c[1] - S(6), c[0] + S(1.5), c[1] + S(6)], fill=GOLD_DARK)


def em_hinomaru_nobori(d):
    """锦之御旗：赤锦幟，上金日、下银月，锦纹"""
    x0, y0, x1, y1 = nobori_box()
    cx = (x0 + x1) / 2
    h = y1 - y0
    for k in range(int(h / S(10))):
        for j in range(int((x1 - x0) / S(10)) + 1):
            px, py = x0 + j * S(10) + (S(5) if k % 2 else 0), y0 + k * S(10)
            d.polygon([(px, py - S(2.5)), (px + S(2.5), py), (px, py + S(2.5)), (px - S(2.5), py)], fill=(150, 40, 44, 255))
    r = (x1 - x0) * 0.3
    for (yy, col, edge) in ((y0 + h * 0.26, GOLD, GOLD_DARK), (y0 + h * 0.62, (214, 216, 214, 255), (90, 92, 94, 255))):
        outlined_ellipse(d, [cx - r, yy - r, cx + r, yy + r], col, edge, S(2))
    # 银月：圆里压一块底色成月牙
    my = y0 + h * 0.62
    d.ellipse([cx - r * 0.55, my - r * 1.05, cx + r * 1.15, my + r * 0.55], fill=hexc("#7A1E22"))
    for k in range(6):
        yy = y0 + S(14) + k * (h - S(20)) / 5
        EXT.rectangle([x0 - S(8), yy, x0, yy + S(4)], fill=(214, 196, 150, 255), outline=OUTLINE, width=S(1))


def em_wolf(d):
    """突厥狼头纛：天青底金狼头，白马尾流苏"""
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    c = (cx + S(6), y0 + h * 0.42)
    head = [(c[0] + S(26), c[1] + S(26)), (c[0] + S(22), c[1] - S(6)), (c[0] + S(18), c[1] - S(30)), (c[0] + S(8), c[1] - S(16)),
            (c[0] - S(2), c[1] - S(34)), (c[0] - S(8), c[1] - S(14)), (c[0] - S(22), c[1] - S(6)), (c[0] - S(40), c[1] + S(2)),
            (c[0] - S(42), c[1] + S(8)), (c[0] - S(30), c[1] + S(12)), (c[0] - S(36), c[1] + S(16)), (c[0] - S(18), c[1] + S(18)),
            (c[0] - S(4), c[1] + S(24)), (c[0] + S(6), c[1] + S(36))]
    outlined_polygon(d, head, GOLD, GOLD_DARK, S(2))
    d.polygon([(c[0] - S(12), c[1] - S(4)), (c[0] - S(4), c[1] - S(8)), (c[0] - S(6), c[1] - S(2))], fill=OUTLINE)
    d.line([(c[0] - S(40), c[1] + S(4)), (c[0] - S(36), c[1] + S(4))], fill=OUTLINE, width=S(2))
    d.line([(c[0] - S(30), c[1] + S(12)), (c[0] - S(20), c[1] + S(14))], fill=GOLD_DARK, width=S(1.6))
    for k in range(4):
        d.line([(c[0] + S(4) + k * S(5), c[1] + S(2)), (c[0] + S(10) + k * S(5), c[1] + S(26))], fill=GOLD_DARK, width=S(1.2))
    for i in range(int((x1 - x0) / S(4)) + 1):
        xx = x0 + i * S(4) + S(1)
        EXT.line([(xx, y1), (xx + S(1), y1 + S(18) + (i % 3) * S(3))], fill=(238, 236, 228, 255), width=S(1.6))


# ───────── 现代理念 ─────────

def em_anarchism(d):
    x0, y0, x1, y1 = box()
    d.polygon([(x0, y0), (x1, y0), (x0, y1)], fill=(20, 20, 20, 255))
    d.polygon([(x1, y0), (x1, y1), (x0, y1)], fill=(176, 28, 32, 255))


def em_rose(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.36)
    green = (50, 120, 60, 255)
    d.line([(c[0], c[1] + S(10)), (c[0] - S(4), c[1] + S(56))], fill=green, width=S(4))
    for sx in (-1, 1):
        lf = [(c[0] - S(2), c[1] + S(32)), (c[0] + sx * S(20), c[1] + S(24)), (c[0] + sx * S(6), c[1] + S(38))]
        outlined_polygon(d, lf, green, (20, 60, 30, 255))
    white = (246, 240, 236, 255)
    for i in range(5):
        a = math.radians(-90 + i * 72)
        px, py = c[0] + math.cos(a) * S(11), c[1] + math.sin(a) * S(11)
        outlined_ellipse(d, [px - S(13), py - S(13), px + S(13), py + S(13)], white, (150, 40, 40, 255))
    outlined_ellipse(d, [c[0] - S(9), c[1] - S(9), c[0] + S(9), c[1] + S(9)], (236, 210, 210, 255), (150, 40, 40, 255))
    d.arc([c[0] - S(5), c[1] - S(5), c[0] + S(5), c[1] + S(5)], 0, 270, fill=(150, 40, 40, 255), width=S(1.5))


def em_torch(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.3)
    black = (26, 24, 22, 255)
    d.polygon([(c[0] - S(6), c[1] + S(20)), (c[0] + S(6), c[1] + S(20)), (c[0] + S(3), c[1] + S(70)), (c[0] - S(3), c[1] + S(70))], fill=black)
    d.polygon([(c[0] - S(14), c[1] + S(6)), (c[0] + S(14), c[1] + S(6)), (c[0] + S(8), c[1] + S(22)), (c[0] - S(8), c[1] + S(22))], fill=black)
    flame = [(c[0] - S(12), c[1] + S(6)), (c[0] - S(16), c[1] - S(12)), (c[0] - S(6), c[1] - S(6)), (c[0] - S(2), c[1] - S(32)),
             (c[0] + S(6), c[1] - S(14)), (c[0] + S(14), c[1] - S(22)), (c[0] + S(12), c[1] + S(6))]
    outlined_polygon(d, flame, (232, 96, 30, 255), black)
    d.polygon([(c[0] - S(6), c[1] + S(4)), (c[0] - S(2), c[1] - S(16)), (c[0] + S(6), c[1] + S(4))], fill=(252, 220, 90, 255))


def em_dove(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.42)
    white, edge = (246, 246, 244, 255), (60, 80, 100, 255)
    outlined_ellipse(d, [c[0] - S(26), c[1] - S(8), c[0] + S(14), c[1] + S(12)], white, edge)
    wing = [(c[0] - S(8), c[1] - S(2)), (c[0] + S(4), c[1] - S(40)), (c[0] + S(20), c[1] - S(36)), (c[0] + S(10), c[1] + S(2))]
    outlined_polygon(d, wing, white, edge)
    outlined_ellipse(d, [c[0] + S(6), c[1] - S(14), c[0] + S(22), c[1] + S(2)], white, edge)
    d.polygon([(c[0] + S(21), c[1] - S(8)), (c[0] + S(30), c[1] - S(5)), (c[0] + S(21), c[1] - S(3))], fill=(230, 160, 60, 255))
    d.polygon([(c[0] - S(24), c[1]), (c[0] - S(42), c[1] - S(8)), (c[0] - S(40), c[1] + S(10))], fill=white, outline=edge)
    d.ellipse([c[0] + S(13), c[1] - S(9), c[0] + S(16), c[1] - S(6)], fill=edge)
    olive = (90, 140, 70, 255)
    d.line([(c[0] + S(26), c[1] - S(2)), (c[0] + S(34), c[1] + S(14))], fill=olive, width=S(2))
    for k in range(3):
        px, py = c[0] + S(28) + k * S(2.5), c[1] + S(2) + k * S(4.5)
        d.ellipse([px - S(4), py - S(2), px + S(4), py + S(2)], fill=olive)


def em_coins(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.3)
    for col in range(3):
        for k in range(4 - col):
            x = c[0] + (col - 1) * S(24)
            y = c[1] + S(46) - k * S(9) - (S(4) if col == 1 else 0)
            outlined_ellipse(d, [x - S(13), y - S(5), x + S(13), y + S(5)], GOLD, GOLD_DARK)
    outlined_ellipse(d, [c[0] - S(18), c[1] - S(18), c[0] + S(18), c[1] + S(18)], GOLD, GOLD_DARK, S(2))
    d.ellipse([c[0] - S(12), c[1] - S(12), c[0] + S(12), c[1] + S(12)], outline=GOLD_DARK, width=S(1.4))
    d.rectangle([c[0] - S(4), c[1] - S(4), c[0] + S(4), c[1] + S(4)], outline=GOLD_DARK, width=S(2))


def em_oak(d):
    """保守自由：深蓝底白橡叶与金橡实"""
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.42)
    white, edge = (240, 240, 236, 255), (30, 50, 90, 255)
    L, W = S(70), S(26)
    right, left = [], []
    for i in range(41):
        t = i / 40
        hw = W * (0.2 + 0.8 * math.sin(math.pi * t) ** 0.8) * (0.62 + 0.38 * abs(math.sin(t * math.pi * 4.5)))
        y = c[1] - L / 2 + t * L
        right.append((c[0] + hw, y))
        left.append((c[0] - hw, y))
    outlined_polygon(d, right + left[::-1], white, edge, S(1.8))
    d.line([(c[0], c[1] - L / 2 + S(4)), (c[0], c[1] + L / 2 + S(10))], fill=edge, width=S(2))
    for i in range(1, 5):
        y = c[1] - L / 2 + i * L / 5
        d.line([(c[0], y + S(6)), (c[0] + S(12), y)], fill=edge, width=S(1.2))
        d.line([(c[0], y + S(6)), (c[0] - S(12), y)], fill=edge, width=S(1.2))
    ax, ay = c[0] + S(22), c[1] + S(28)
    outlined_ellipse(d, [ax - S(7), ay - S(4), ax + S(7), ay + S(14)], GOLD, GOLD_DARK)
    d.chord([ax - S(9), ay - S(8), ax + S(9), ay + S(6)], 180, 360, fill=(120, 90, 40, 255), outline=GOLD_DARK)


def em_ring(d):
    """中间派：紫底白天平"""
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.3)
    white, edge = (244, 240, 236, 255), (60, 40, 90, 255)
    fat_line(d, (c[0], c[1] - S(10)), (c[0], c[1] + S(56)), S(4), white, edge)
    fat_line(d, (c[0] - S(38), c[1]), (c[0] + S(38), c[1]), S(4), white, edge)
    d.polygon([(c[0] - S(16), c[1] + S(62)), (c[0] + S(16), c[1] + S(62)), (c[0] + S(8), c[1] + S(54)), (c[0] - S(8), c[1] + S(54))],
              fill=white, outline=edge)
    outlined_ellipse(d, [c[0] - S(5), c[1] - S(16), c[0] + S(5), c[1] - S(6)], white, edge)
    for sx in (-1, 1):
        px = c[0] + sx * S(38)
        d.line([(px, c[1]), (px - S(12), c[1] + S(28))], fill=white, width=S(1.4))
        d.line([(px, c[1]), (px + S(12), c[1] + S(28))], fill=white, width=S(1.4))
        d.chord([px - S(15), c[1] + S(18), px + S(15), c[1] + S(36)], 0, 180, fill=white, outline=edge, width=S(1.2))


def wheat(d, x0, y0, x1, y1, col, edge):
    pts = bezier((x0, y0), (x0, (y0 + y1) / 2), ((x0 + x1) / 2, y1), (x1, y1), 12)
    d.line(pts, fill=edge, width=S(2))
    for i, p in enumerate(pts[1:-1]):
        for sx in (-1, 1):
            d.ellipse([p[0] + sx * S(4) - S(3), p[1] - S(5), p[0] + sx * S(4) + S(3), p[1] + S(1)], fill=col, outline=edge, width=S(0.8))


def em_cog_wheat(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.4)
    gear(d, c[0], c[1], S(24), 9, GOLD, GOLD_DARK, (178, 34, 34, 255))
    wheat(d, c[0] - S(36), c[1] - S(26), c[0] - S(6), c[1] + S(38), GOLD, GOLD_DARK)
    wheat(d, c[0] + S(36), c[1] - S(26), c[0] + S(6), c[1] + S(38), GOLD, GOLD_DARK)


def em_star(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.4)
    outlined_polygon(d, star_points(c[0], c[1], S(30), S(12), 5), GOLD, GOLD_DARK, S(2))
    wheat(d, c[0] - S(40), c[1] - S(20), c[0] - S(4), c[1] + S(44), GOLD, GOLD_DARK)
    wheat(d, c[0] + S(40), c[1] - S(20), c[0] + S(4), c[1] + S(44), GOLD, GOLD_DARK)


def em_radiance(d):
    cx, cy, w, h = rect_center()
    c = (cx, S(BY0) + h * 0.42)
    for i in range(32):
        a = math.radians(i * 11.25)
        ln = S(48) if i % 2 == 0 else S(34)
        d.line([c, (c[0] + math.cos(a) * ln, c[1] + math.sin(a) * ln)], fill=(214, 168, 70, 255), width=S(2))
    outlined_ellipse(d, [c[0] - S(18), c[1] - S(18), c[0] + S(18), c[1] + S(18)], GOLD, GOLD_DARK, S(2))
    d.ellipse([c[0] - S(10), c[1] - S(10), c[0] + S(10), c[1] + S(10)], fill=(250, 236, 190, 255))


def sword(d, cx, top, bottom, blade, edge, guard):
    d.polygon([(cx - S(4), top + S(14)), (cx, top), (cx + S(4), top + S(14)), (cx + S(4), bottom - S(16)), (cx - S(4), bottom - S(16))],
              fill=blade, outline=edge)
    d.rectangle([cx - S(16), bottom - S(18), cx + S(16), bottom - S(13)], fill=guard, outline=edge, width=S(1))
    d.rectangle([cx - S(2.5), bottom - S(13), cx + S(2.5), bottom], fill=guard, outline=edge, width=S(1))
    d.ellipse([cx - S(4), bottom - S(2), cx + S(4), bottom + S(6)], fill=guard, outline=edge, width=S(1))


def em_sword_laurel(d):
    cx, cy, w, h = rect_center()
    top = S(BY0) + h * 0.1
    sword(d, cx, top, top + S(96), (236, 236, 232, 255), (40, 40, 44, 255), (210, 210, 206, 255))
    for sx in (-1, 1):
        for k in range(6):
            a = math.radians(100 + k * 14) if sx < 0 else math.radians(80 - k * 14)
            px, py = cx + math.cos(a) * S(34), top + S(50) + math.sin(a) * S(34)
            d.ellipse([px - S(4), py - S(2.4), px + S(4), py + S(2.4)], fill=(210, 210, 206, 255))


def em_crown_blue(d):
    em_crown(d)


def em_militarist(d):
    cx, cy, w, h = rect_center()
    x0, y0, x1, y1 = box()
    red = (150, 20, 24, 255)
    d.rectangle([x0, y0 + h * 0.62, x1, y0 + h * 0.7], fill=red)
    top = y0 + h * 0.1
    for sx in (-1, 1):
        # 交叉双剑
        pts = [(cx + sx * S(30), top), (cx + sx * S(26), top + S(4)), (cx - sx * S(18), top + S(70)), (cx - sx * S(14), top + S(66))]
        d.polygon([(cx + sx * S(31), top - S(1)), (cx + sx * S(24), top + S(3)), (cx - sx * S(19), top + S(72)), (cx - sx * S(12), top + S(68))],
                  fill=red)
        d.line([(cx - sx * S(24), top + S(64)), (cx - sx * S(8), top + S(76))], fill=red, width=S(4))
        d.line([(cx - sx * S(16), top + S(70)), (cx - sx * S(22), top + S(80))], fill=red, width=S(3))


# ───────── 表 ─────────

CULTURES = {
    "Huaxia": dict(shape="swallow", field="#8E1E1A", draw=em_dragon),
    "China": dict(shape="swallow", field="#8E1E1A", draw=em_dragon),
    "Shanhai": dict(shape="rect", field="#1F5E5B", draw=em_shanhai),
    "Japan": dict(shape="nobori", field="#7A1E22", draw=em_hinomaru_nobori),
    "Youmu": dict(shape="rect", field="#5E2622", draw=em_wolf),
    "Roma": dict(shape="short", field="#8E1B1B", draw=em_roma, top="eagle"),
    "Western": dict(shape="short", field="#4B2A6B", draw=em_labarum),
    "Frankish": dict(shape="rect", field="#1E3F8A", draw=em_fleurs, fringe="#D9A62E"),
    "Germanic": dict(shape="point", field="#D9A62E", draw=em_reichsadler),
    "Viking": dict(shape="round", field="#E6E1D3", draw=em_raven, trim="#A3202A"),
    "Slavonic": dict(shape="swallow3", field="#841C22", draw=em_spas),
    "Arab": dict(shape="rect", field="#ECE7DA", draw=em_umayyad),
    "Persepolis": dict(shape="short", field="#6E1E3A", draw=em_derafsh),
    "Egypt": dict(shape="rect", field="#1F3F8F", draw=em_winged_sun),
    "India": dict(shape="pennant2", field="#E8862A", draw=em_surya),
    "Kosher": dict(shape="rect", field="#3B2350", draw=em_menorah),
    "Aztec": dict(shape="rect", field="#1F6A66", draw=em_quetzal),
    "Ojibwe": dict(shape="rect", field="#C9A774", draw=em_thunderbird),
    "Emperor": dict(shape="point", field="#1A1A1A", draw=em_sunburst_crown),
    "ElfFancy": dict(shape="leaf", field="#2E5E3A", draw=em_tree),
    "Corporate": dict(shape="rect", field="#3A4652", draw=em_corporate),
}

IDEOLOGIES = {
    "Anarchism": dict(shape="rect", field="#141414", draw=em_anarchism),
    "SocialDemocracy": dict(shape="rect", field="#9A2E26", draw=em_rose),
    "Libertarianism": dict(shape="rect", field="#B88E2E", draw=em_torch),
    "SocialLiberalism": dict(shape="rect", field="#3E6E94", draw=em_dove),
    "Capitalism": dict(shape="rect", field="#2F7A4A", draw=em_coins),
    "ConservativeLiberalism": dict(shape="rect", field="#24467A", draw=em_oak),
    "Centrism": dict(shape="rect", field="#6A4A9A", draw=em_ring),
    "Socialism": dict(shape="rect", field="#8E1C1C", draw=em_cog_wheat),
    "Communism": dict(shape="rect", field="#861418", draw=em_star),
    "ReligiousDemocracy": dict(shape="rect", field="#EEE9DD", draw=em_radiance),
    "Authoritarianism": dict(shape="rect", field="#4A4F57", draw=em_sword_laurel),
    "Conservatism": dict(shape="rect", field="#1F2F5A", draw=em_crown_blue),
    "Fascism": dict(shape="rect", field="#141414", draw=em_militarist),
}


def folds(img, mask):
    """布纹：竖向明暗起伏与左上受光"""
    px = img.load()
    mk = mask.load()
    x0, y0, x1, y1 = box()
    for y in range(y0, min(N, y1 + S(30))):
        for x in range(x0 - S(10), min(N, x1 + S(10))):
            if mk[x, y] == 0:
                continue
            r, g, b, a = px[x, y]
            t = (x - x0) / max(1, x1 - x0)
            k = 1 + 0.09 * math.sin(t * math.pi * 3.2 + 0.6) - 0.08 * ((y - y0) / max(1, y1 - y0)) + 0.05 * (1 - t)
            px[x, y] = (max(0, min(255, int(r * k))), max(0, min(255, int(g * k))), max(0, min(255, int(b * k))), a)


LIGHT = np.array([-0.62, -0.78])
_RNG = np.random.default_rng(11)
GRAIN = gaussian_filter(_RNG.normal(0, 1, (N, N)), 1.6)
WEAVE = (np.sin(np.arange(N) / SS * 2.4)[None, :] * np.sin(np.arange(N) / SS * 2.4)[:, None])


def realize(img, field, body):
    """写实：纹样(与旗面底色不同的部分、杆、穗)按距离场起浮雕受光，旗面加布纹颗粒、纹样投下浅影"""
    arr = np.asarray(img, np.float32).copy()
    rgb, alpha = arr[..., :3], arr[..., 3]
    solid = alpha > 40
    inside = np.asarray(body) > 127
    diff = np.abs(rgb - np.array(field[:3], np.float32)).sum(-1)
    emblem = solid & ((diff > 60) | ~inside)
    cloth = solid & inside & ~emblem
    # 浮雕
    dist = distance_transform_edt(emblem)
    bevel = S(3.5)
    height = np.sin(np.clip(dist / bevel, 0, 1) * math.pi / 2)
    gy, gx = np.gradient(height)
    lit = np.clip(-(gx * LIGHT[0] + gy * LIGHT[1]) * bevel * 0.9, -1, 1)[..., None]
    hi = np.minimum(255, rgb * 1.35 + 30)
    lo = rgb * 0.45
    rgb = np.where(emblem[..., None] & (lit > 0), rgb + (hi - rgb) * lit * 0.55, rgb)
    rgb = np.where(emblem[..., None] & (lit < 0), rgb + (lo - rgb) * (-lit) * 0.7, rgb)
    edge_ao = np.clip(1 - dist / S(1.2), 0, 1)[..., None]
    rgb = np.where(emblem[..., None], rgb + (lo - rgb) * edge_ao * 0.3, rgb)
    # 纹样投在旗面上的浅影
    sh = np.roll(np.roll(emblem.astype(np.float32), S(2), 0), S(2), 1)
    sh = gaussian_filter(sh, S(1.2))
    rgb = np.where(cloth[..., None], rgb * (1 - 0.35 * sh[..., None]), rgb)
    # 布纹与颗粒
    rgb = np.where(cloth[..., None], rgb * (1 + 0.035 * WEAVE[..., None] + 0.06 * GRAIN[..., None]), rgb)
    rgb = np.where(emblem[..., None], rgb * (1 + 0.04 * GRAIN[..., None]), rgb)
    arr[..., :3] = np.clip(rgb, 0, 255)
    return arr


def grade(arr):
    """冷峻调色：冷色多降饱和，金黄转氧化旧铜，提对比、压中间调"""
    rgb = arr[..., :3] / 255.0
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = np.where(mx - mn == 0, 1, mx - mn)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    hue = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    lum = (rgb * np.array([0.3, 0.59, 0.11])).sum(-1, keepdims=True)
    cool = ((hue > 75) & (hue < 280))[..., None]
    rgb = rgb + (lum - rgb) * np.where(cool, 0.55, 0.4)
    gold = ((hue >= 28) & (hue <= 68) & (mx - mn > 0.18))[..., None]
    brass = np.clip(np.array([150, 120, 64]) / 255.0 * (lum / 0.45), 0, 1)
    rgb = np.where(gold, rgb * 0.4 + brass * 0.6, rgb)
    rgb = np.clip((rgb - 0.5) * 1.3 + 0.5, 0, 1) ** 1.18
    arr[..., :3] = rgb * 255
    return arr


def pixel_out(arr, colors=40):
    """写实大图 → 像素图：缩小、锐化、透明度二值化、限色、1px 纯黑描边、右下投影"""
    pil = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")
    inner = PIXEL - 3
    small = pil.resize((inner, inner), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1, percent=70, threshold=2))
    a = np.asarray(small).copy()
    solid = a[..., 3] > 110
    q = np.asarray(Image.fromarray(a[..., :3]).quantize(colors=colors, method=Image.Quantize.MEDIANCUT).convert("RGB"))
    out = np.zeros((PIXEL, PIXEL, 4), np.uint8)
    out[1:1 + inner, 1:1 + inner, :3] = q
    out[1:1 + inner, 1:1 + inner, 3] = np.where(solid, 255, 0)
    filled = out[..., 3] == 255
    pad = np.pad(filled, 1)
    near = pad[:-2, 1:-1] | pad[2:, 1:-1] | pad[1:-1, :-2] | pad[1:-1, 2:]
    out[near & ~filled] = (0, 0, 0, 255)
    body = out[..., 3] > 0
    shifted = np.zeros_like(body)
    shifted[1:, 1:] = body[:-1, :-1]
    out[shifted & ~body] = (0, 0, 0, 80)
    return Image.fromarray(out, "RGBA")


def make(spec):
    global EXT
    shape = spec["shape"]
    cloth = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(cloth)
    outside = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    EXT = ImageDraw.Draw(outside)
    mask = body_mask(shape)
    d.rectangle([0, 0, N, N], fill=hexc(spec["field"]))
    trim = spec.get("trim")
    if trim:
        inner = mask.filter(ImageFilter.MinFilter(S(4) | 1))
        ring = mask.copy()
        ring.paste(0, mask=inner)
        d.bitmap((0, 0), ring, fill=hexc(trim))
    spec["draw"](d)
    fringe = spec.get("fringe")
    if fringe:
        x0, y0, x1, y1 = box()
        for i in range(int((x1 - x0) / S(6)) + 1):
            xx = x0 + i * S(6) + S(2)
            EXT.line([(xx, y1), (xx, y1 + S(14))], fill=hexc(fringe), width=S(2.4))
    clipped = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    clipped.paste(cloth, (0, 0), mask)
    folds(clipped, mask)
    # 横杆与杆头
    pole = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    pd = ImageDraw.Draw(pole)
    bx0, by0, bx1, by1 = box()
    if shape == "nobori":
        bx0, by0, bx1, by1 = nobori_box()
        pd.rectangle([bx0 - S(14), by0 - S(10), bx0 - S(9), by1 + S(14)], fill=WOOD)
        pd.rectangle([bx0 - S(14), by0 - S(8), bx1 + S(4), by0 - S(4)], fill=WOOD)
    else:
        pd.rectangle([bx0 - S(10), by0 - S(9), bx1 + S(10), by0 - S(3)], fill=WOOD)
        for ex in (bx0 - S(12), bx1 + S(12)):
            pd.ellipse([ex - S(5), by0 - S(11), ex + S(5), by0 - S(1)], fill=GOLD, outline=GOLD_DARK, width=S(1))
        if spec.get("top") == "eagle":
            eagle_top(pd)
        else:
            cx = (bx0 + bx1) / 2
            pd.polygon([(cx - S(5), by0 - S(9)), (cx, by0 - S(24)), (cx + S(5), by0 - S(9))], fill=GOLD, outline=GOLD_DARK)
    comp = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    comp.alpha_composite(pole)
    comp.alpha_composite(outside)
    comp.alpha_composite(clipped)
    arr = realize(comp, hexc(spec["field"]), mask)
    return pixel_out(grade(arr))


def build():
    icons = {}
    for name, spec in CULTURES.items():
        icons["culture_" + name] = make(spec)
    for name, spec in IDEOLOGIES.items():
        icons["ideology_" + name] = make(spec)
    return icons


if __name__ == "__main__":
    # 成品改由 flag_formal.py 出(3:2 正式国旗、像素格成图)；本文件只留纹样画法给它用
    import subprocess
    subprocess.run([sys.executable, os.path.join(HERE, "flag_formal.py")] + sys.argv[1:], check=True)
