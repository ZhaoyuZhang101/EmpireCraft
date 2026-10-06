"""EmpireCraft 徽章锻造器 —— 国徽式的金属浮雕图标(样品阶段)。

与 icon_forge.py 的 28×28 像素风不同：在 256×256 画布上画形状遮罩，按距离场算浮雕光影
(光源左上)，金属用渐变上色，最后缩到 64×64。构图参照国徽：圆形徽章、金环、朱红底、
中央金色浮雕主体，下方麦穗与绶带。

用法: python Tools/IconForge/emblem_forge.py            (输出样品到 Tools/IconForge/emblem_samples.png)
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import distance_transform_edt

S = 256          # 绘制尺寸
OUT = 64         # 输出尺寸
LIGHT = np.array([-0.62, -0.78])   # 光源方向(左上)

GOLD = ((214, 196, 150), (140, 112, 62), (40, 30, 16))      # 氧化旧铜
RED = ((178, 48, 40), (120, 14, 14), (34, 2, 2))            # 暗血红
STEEL = ((214, 216, 214), (112, 116, 118), (22, 24, 26))    # 枪灰
JADE = ((150, 168, 150), (72, 92, 76), (18, 28, 20))
LACQUER = ((72, 70, 70), (24, 22, 22), (4, 4, 4))           # 黑


def blank():
    return Image.new("L", (S, S), 0)


def draw(mask):
    return ImageDraw.Draw(mask)


def mix(c0, c1, t):
    return tuple(c0[i] + (c1[i] - c0[i]) * t for i in range(3))


def shade(mask, palette, bevel=10.0, gloss=0.55, flat=False):
    """把遮罩渲染成带浮雕光影的金属/漆面图层(RGBA numpy)。"""
    m = np.asarray(mask, dtype=np.float32) / 255.0
    inside = m > 0.5
    h, w = m.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    # 整体渐变：左上亮、右下暗
    ys, xs = np.nonzero(inside)
    if len(xs) == 0:
        return np.zeros((h, w, 4), np.float32)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    t = ((xx - x0) / max(1, x1 - x0) * 0.45 + (yy - y0) / max(1, y1 - y0) * 0.55)
    t = np.clip(t, 0, 1)
    hi, mid, lo = (np.array(c, np.float32) for c in palette)
    base = np.where(t[..., None] < 0.5, hi + (mid - hi) * (t[..., None] / 0.5) * 1.0,
                    mid + (lo - mid) * ((t[..., None] - 0.5) / 0.5) * 0.8)
    base = mid + (base - mid) * 0.6
    if not flat:
        # 浮雕：距离场当高度，梯度与光照点乘
        d = distance_transform_edt(inside)
        height = np.clip(d / bevel, 0, 1)
        height = np.sin(height * math.pi / 2)
        gy, gx = np.gradient(height)
        lit = -(gx * LIGHT[0] + gy * LIGHT[1]) * bevel * 0.9
        lit = np.clip(lit, -1, 1)
        base = np.where(lit[..., None] > 0, base + (hi - base) * lit[..., None] * gloss,
                        base + (lo - base) * (-lit[..., None]) * 0.75)
        # 金属反光带
        band = np.exp(-((xx + yy - (x0 + y0) - 0.35 * ((x1 - x0) + (y1 - y0))) / (0.12 * (x1 - x0 + y1 - y0) + 1)) ** 2)
        base = base + (np.array([255, 250, 230], np.float32) - base) * band[..., None] * 0.18 * gloss
    out = np.zeros((h, w, 4), np.float32)
    out[..., :3] = np.clip(base, 0, 255)
    out[..., 3] = m * 255
    return out


def over(dst, src):
    a = src[..., 3:4] / 255.0
    dst[..., :3] = src[..., :3] * a + dst[..., :3] * (1 - a)
    dst[..., 3:4] = np.maximum(dst[..., 3:4], src[..., 3:4])
    return dst


def outline_and_shadow(img):
    """整体外描深色边与右下投影，保证缩小后在界面上清楚。"""
    alpha = img[..., 3] > 10
    edge = Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9))
    edge = np.asarray(edge) > 0
    shadow = np.roll(np.roll(edge, 6, 0), 6, 1)
    out = np.zeros_like(img)
    out[shadow, :3] = 0; out[shadow, 3] = 110
    out[edge, :3] = (36, 18, 12); out[edge, 3] = 255
    return over(out, img)


def grade(img, desat=0.4, contrast=1.25, gamma=1.15):
    rgb = img[..., :3] / 255.0
    lum = (rgb * np.array([0.3, 0.59, 0.11])).sum(-1, keepdims=True)
    rgb = rgb + (lum - rgb) * desat
    img[..., :3] = (np.clip((rgb - 0.5) * contrast + 0.5, 0, 1) ** gamma) * 255
    return img


def finish(img):
    img = grade(img)
    img = outline_and_shadow(img)
    pil = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGBA")
    return pil.resize((OUT, OUT), Image.LANCZOS)


# ───────────── 构件 ─────────────
C = S / 2


def ring(r_out, r_in):
    m = blank(); d = draw(m)
    d.ellipse([C - r_out, C - r_out, C + r_out, C + r_out], fill=255)
    d.ellipse([C - r_in, C - r_in, C + r_in, C + r_in], fill=0)
    return m


def disc(r, cx=C, cy=C):
    m = blank(); draw(m).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    return m


def rivets(r, n, size):
    m = blank(); d = draw(m)
    for k in range(n):
        a = 2 * math.pi * k / n
        x, y = C + math.cos(a) * r, C + math.sin(a) * r
        d.ellipse([x - size, y - size, x + size, y + size], fill=255)
    return m


def star(cx, cy, r, inner=0.42):
    m = blank()
    pts = []
    for k in range(10):
        rr = r if k % 2 == 0 else r * inner
        a = -math.pi / 2 + k * math.pi / 5
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    draw(m).polygon(pts, fill=255)
    return m


def wheat(side):
    """一侧麦穗：沿圆弧排列的麦粒。side=-1 左，1 右。"""
    m = blank(); d = draw(m)
    for k in range(9):
        a = math.radians(115 - k * 11) if side < 0 else math.radians(65 + k * 11)
        r = 104
        x, y = C + math.cos(a) * r * (1 if side > 0 else 1), C + math.sin(a) * r
        if side < 0:
            x = C - (x - C) * -1 if False else x
        ang = a + (math.pi / 2 if side > 0 else -math.pi / 2)
        for g in (-1, 1):
            gx = x + math.cos(ang + g * 0.6) * 9
            gy = y + math.sin(ang + g * 0.6) * 9
            d.ellipse([gx - 6, gy - 9, gx + 6, gy + 9], fill=255)
        d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=255)
    return m


def ribbon():
    m = blank(); d = draw(m)
    d.polygon([(C - 70, 200), (C + 70, 200), (C + 82, 226), (C + 50, 220), (C, 232), (C - 50, 220), (C - 82, 226)],
              fill=255)
    return m


def medallion(img, field=RED):
    over(img, shade(ring(124, 104), GOLD, bevel=9))
    over(img, shade(rivets(114, 24, 3.2), GOLD, bevel=3))
    over(img, shade(disc(103), field, bevel=30, gloss=0.25))
    over(img, shade(ring(98, 94), GOLD, bevel=2, gloss=0.4))


def wreath(img):
    over(img, shade(wheat(-1), GOLD, bevel=5))
    over(img, shade(wheat(1), GOLD, bevel=5))
    over(img, shade(ribbon(), RED, bevel=8))


# ───────────── 样品 ─────────────
def gate_symbol():
    """官署：城楼(重檐殿宇 + 城台 + 三门洞)。"""
    m = blank(); d = draw(m)
    d.polygon([(C - 60, 92), (C + 60, 92), (C + 46, 74), (C - 46, 74)], fill=255)          # 上檐
    d.rectangle([C - 38, 92, C + 38, 104], fill=255)
    d.polygon([(C - 72, 116), (C + 72, 116), (C + 56, 100), (C - 56, 100)], fill=255)      # 下檐
    d.rectangle([C - 48, 116, C + 48, 136], fill=255)                                      # 楼身
    d.rectangle([C - 66, 136, C + 66, 176], fill=255)                                      # 城台
    for x in (-34, 0, 34):
        d.rectangle([C + x - 9, 150, C + x + 9, 176], fill=0)
        d.ellipse([C + x - 9, 141, C + x + 9, 159], fill=0)
    for x in range(-40, 41, 16):
        d.rectangle([C + x - 3, 118, C + x + 3, 134], fill=0)                              # 柱间
    d.rectangle([C - 4, 64, C + 4, 74], fill=255)                                          # 宝顶
    return m


def book_symbol():
    """宪法：展开的法典，书页上有条文刻线。"""
    m = blank(); d = draw(m)
    d.polygon([(C, 96), (C - 66, 84), (C - 70, 166), (C, 178)], fill=255)
    d.polygon([(C, 96), (C + 66, 84), (C + 70, 166), (C, 178)], fill=255)
    for k in range(5):
        y = 104 + k * 13
        d.line([(C - 54, y - 6 + k), (C - 10, y + 2)], fill=0, width=4)
        d.line([(C + 10, y + 2), (C + 54, y - 6 + k)], fill=0, width=4)
    d.line([(C, 96), (C, 178)], fill=0, width=5)
    return m


def crown_symbol():
    """称帝：冕旒(冕板、垂旒、冠身)。"""
    m = blank(); d = draw(m)
    d.polygon([(C - 76, 86), (C + 76, 86), (C + 70, 100), (C - 70, 100)], fill=255)        # 冕板
    for x in range(-64, 65, 16):
        d.line([(C + x, 100), (C + x, 132)], fill=255, width=4)
        d.ellipse([C + x - 6, 128, C + x + 6, 140], fill=255)
    d.rounded_rectangle([C - 40, 104, C + 40, 172], radius=10, fill=255)                   # 冠身
    d.ellipse([C - 12, 124, C + 12, 148], fill=0)
    d.ellipse([C - 7, 129, C + 7, 143], fill=255)
    d.rectangle([C - 92, 112, C + 92, 118], fill=255)                                      # 簪
    return m


def make(symbol, field=RED, top_star=True):
    img = np.zeros((S, S, 4), np.float32)
    medallion(img, field)
    wreath(img)
    if top_star:
        over(img, shade(star(C, 54, 16), GOLD, bevel=5))
    over(img, shade(symbol(), GOLD, bevel=7))
    return finish(img)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    samples = [make(gate_symbol, field=LACQUER, top_star=False), make(book_symbol, top_star=False),
               make(crown_symbol, field=LACQUER, top_star=False)]
    k = 4
    sheet = Image.new("RGBA", (len(samples) * (OUT * k + 20) + 20, OUT * k + 40), (74, 78, 68, 255))
    small = Image.new("RGBA", (len(samples) * 40 + 20, 40), (74, 78, 68, 255))
    for i, im in enumerate(samples):
        sheet.alpha_composite(im.resize((OUT * k, OUT * k), Image.LANCZOS), (20 + i * (OUT * k + 20), 20))
        small.alpha_composite(im.resize((28, 28), Image.LANCZOS), (20 + i * 40, 6))
    sheet.save(os.path.join(here, "emblem_samples.png"))
    small.save(os.path.join(here, "emblem_samples_small.png"))
    print("ok")
