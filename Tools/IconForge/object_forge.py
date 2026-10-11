"""EmpireCraft 器物锻造器 —— EU4 修正图标式的写实器物 + 角标(样品阶段)。

在 256×256 画布上用遮罩画器物的各个部件，每个部件按材质上色：距离场浮雕光影(光源左上)、
整体明暗渐变、材质颗粒(纸、木、布的细噪声)，最后缩到 56×56(原版 28 的 2 倍)。
角标按 EU4 习惯：绿色加号/箭头 = 增加、获得；红色叉号 = 取消、销毁；金色向上箭头 = 提升。

用法: python Tools/IconForge/object_forge.py   (输出 Tools/IconForge/object_samples.png)
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import distance_transform_edt, gaussian_filter

S = 256
OUT = 56
LIGHT = np.array([-0.62, -0.78])
RNG = np.random.default_rng(7)
NOISE = gaussian_filter(RNG.normal(0, 1, (S, S)), 1.2)
NOISE /= np.abs(NOISE).max()

#             高光              本色              暗部
# 冷峻色板(黑、骨白、血红、枪灰、氧化旧铜)：低饱和、高对比、重阴影
GOLD = ((214, 196, 150), (140, 112, 62), (40, 30, 16))        # 氧化旧铜
STEEL = ((214, 216, 214), (112, 116, 118), (22, 24, 26))      # 枪灰
IRON = ((170, 172, 170), (78, 80, 82), (14, 14, 16))          # 黑铁
PARCH = ((214, 206, 182), (156, 146, 120), (62, 56, 42))      # 旧纸
WOOD = ((150, 112, 80), (88, 60, 38), (24, 14, 8))            # 黑胡桃
RED = ((178, 48, 40), (120, 14, 14), (34, 2, 2))              # 暗血红
LACQ = ((72, 70, 70), (24, 22, 22), (4, 4, 4))                # 黑
JADE = ((150, 168, 150), (72, 92, 76), (18, 28, 20))          # 灰绿(原野灰)
GREEN = ((240, 236, 222), (196, 190, 172), (60, 56, 48))      # 骨白(增加类角标)
BLOOD = ((210, 60, 50), (140, 16, 16), (40, 0, 0))            # 血红(取消类角标)
BRASS = ((214, 196, 150), (140, 112, 62), (40, 30, 16))       # 旧铜


def blank():
    return Image.new("L", (S, S), 0)


def shade(mask, palette, bevel=10.0, gloss=0.6, grain=0.0, flat=False):
    m = np.asarray(mask, dtype=np.float32) / 255.0
    inside = m > 0.5
    out = np.zeros((S, S, 4), np.float32)
    ys, xs = np.nonzero(inside)
    if len(xs) == 0:
        return out
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    t = np.clip((xx - x0) / max(1, x1 - x0) * 0.45 + (yy - y0) / max(1, y1 - y0) * 0.55, 0, 1)
    hi, mid, lo = (np.array(c, np.float32) for c in palette)
    base = mid + (hi - mid) * np.clip(0.5 - t, 0, 0.5)[..., None] * 0.9 \
               + (lo - mid) * np.clip(t - 0.5, 0, 0.5)[..., None] * 1.0
    if not flat:
        d = distance_transform_edt(inside)
        height = np.sin(np.clip(d / bevel, 0, 1) * math.pi / 2)
        gy, gx = np.gradient(height)
        lit = np.clip(-(gx * LIGHT[0] + gy * LIGHT[1]) * bevel * 0.9, -1, 1)
        base = np.where(lit[..., None] > 0, base + (hi - base) * lit[..., None] * gloss,
                        base + (lo - base) * (-lit[..., None]) * 0.8)
        edge_ao = np.clip(1 - d / 3.0, 0, 1)
        base = base + (lo - base) * edge_ao[..., None] * 0.35
    if grain:
        base = base + NOISE[..., None] * grain * 40
    out[..., :3] = np.clip(base, 0, 255)
    out[..., 3] = m * 255
    return out


def over(dst, src):
    a = src[..., 3:4] / 255.0
    dst[..., :3] = src[..., :3] * a + dst[..., :3] * (1 - a)
    dst[..., 3:4] = np.maximum(dst[..., 3:4], src[..., 3:4])
    return dst


def drop_shadow(img, dx=7, dy=8, blur=5, alpha=0.55):
    a = img[..., 3] / 255.0
    sh = gaussian_filter(np.roll(np.roll(a, dy, 0), dx, 1), blur)
    out = np.zeros_like(img)
    out[..., 3] = np.clip(sh * alpha * 255, 0, 255)
    return over(out, img)


def grade(img, desat=0.4, contrast=1.25, gamma=1.15):
    """冷峻调色：降饱和、提对比、压暗中间调。"""
    rgb = img[..., :3] / 255.0
    lum = (rgb * np.array([0.3, 0.59, 0.11])).sum(-1, keepdims=True)
    rgb = rgb + (lum - rgb) * desat
    rgb = np.clip((rgb - 0.5) * contrast + 0.5, 0, 1) ** gamma
    img[..., :3] = rgb * 255
    return img


def hard_outline(img, width=7):
    a = img[..., 3] > 20
    edge = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(width))) > 0
    out = np.zeros_like(img)
    out[edge, :3] = (8, 6, 6); out[edge, 3] = 255
    return over(out, img)


def finish(img):
    img = grade(img)
    img = hard_outline(img)
    img = drop_shadow(img, alpha=0.7)
    pil = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGBA")
    return pil.resize((OUT, OUT), Image.LANCZOS)


def poly(points):
    m = blank(); ImageDraw.Draw(m).polygon(points, fill=255); return m


def ellipse(box):
    m = blank(); ImageDraw.Draw(m).ellipse(box, fill=255); return m


def rrect(box, r):
    m = blank(); ImageDraw.Draw(m).rounded_rectangle(box, radius=r, fill=255); return m


def sub(a, b):
    return Image.fromarray(np.clip(np.asarray(a, np.int16) - np.asarray(b, np.int16), 0, 255).astype(np.uint8))


def rotate(mask, deg, cx, cy):
    return mask.rotate(deg, resample=Image.BICUBIC, center=(cx, cy))


# ───────────── 角标(EU4 式，不带圆底) ─────────────
def badge_plus(img, cx=196, cy=196, r=44):
    w = r * 0.42
    m = rrect([cx - r, cy - w, cx + r, cy + w], 8)
    m2 = rrect([cx - w, cy - r, cx + w, cy + r], 8)
    m = Image.fromarray(np.maximum(np.asarray(m), np.asarray(m2)))
    over(img, shade(m.filter(ImageFilter.MaxFilter(9)), LACQ, bevel=2, flat=True))
    over(img, shade(m, GREEN, bevel=9, gloss=0.8))


def badge_cross(img, cx=196, cy=196, r=44):
    w = r * 0.38
    a = rrect([cx - r, cy - w, cx + r, cy + w], 8)
    m = Image.fromarray(np.maximum(np.asarray(rotate(a, 45, cx, cy)), np.asarray(rotate(a, -45, cx, cy))))
    over(img, shade(m.filter(ImageFilter.MaxFilter(9)), LACQ, bevel=2, flat=True))
    over(img, shade(m, BLOOD, bevel=9, gloss=0.8))


def badge_arrow(img, cx=196, cy=196, r=44, deg=0, palette=GREEN):
    """箭头：deg=0 向右，90 向上。"""
    pts = [(cx - r, cy - r * 0.3), (cx + r * 0.1, cy - r * 0.3), (cx + r * 0.1, cy - r * 0.75), (cx + r, cy),
           (cx + r * 0.1, cy + r * 0.75), (cx + r * 0.1, cy + r * 0.3), (cx - r, cy + r * 0.3)]
    m = rotate(poly(pts), deg, cx, cy)
    over(img, shade(m.filter(ImageFilter.MaxFilter(9)), LACQ, bevel=2, flat=True))
    over(img, shade(m, palette, bevel=9, gloss=0.8))


# ───────────── 器物 ─────────────
def chain(img):
    """铁链：粗铁环正、侧交替相扣，斜向排列。"""
    centers = [(64, 64), (110, 110), (156, 156)]
    for k, (cx, cy) in enumerate(centers):
        if k % 2 == 0:
            ring = sub(ellipse([cx - 44, cy - 30, cx + 44, cy + 30]), ellipse([cx - 24, cy - 12, cx + 24, cy + 12]))
        else:
            ring = sub(ellipse([cx - 44, cy - 18, cx + 44, cy + 18]), ellipse([cx - 30, cy - 4, cx + 30, cy + 4]))
        ring = rotate(ring, -45 if k % 2 == 0 else 45, cx, cy)
        over(img, shade(ring, IRON, bevel=10, gloss=0.9, grain=0.2))


def crown(img):
    """冕旒：黑漆冕板、金玉相间的垂旒、金冠身、红宝石、玉簪。"""
    over(img, shade(poly([(30, 70), (226, 70), (214, 92), (42, 92)]), LACQ, bevel=6, grain=0.2))
    for x in range(44, 216, 18):
        for k, y in enumerate(range(100, 150, 12)):
            over(img, shade(ellipse([x - 6, y - 6, x + 6, y + 6]), JADE if (k + x // 18) % 2 else GOLD, bevel=5))
    over(img, shade(rrect([78, 96, 178, 196], 16), GOLD, bevel=14, gloss=0.8))
    for x in range(90, 170, 14):
        over(img, shade(ellipse([x - 3, 176, x + 3, 182]), GOLD, bevel=2))
    over(img, shade(ellipse([112, 124, 144, 156]), RED, bevel=10, gloss=1.0))
    over(img, shade(rrect([20, 112, 236, 122], 5), JADE, bevel=4))
    over(img, shade(rrect([118, 40, 138, 72], 5), GOLD, bevel=5))


def scroll_map(img):
    """法理图卷：展开的羊皮地图、两端木轴、朱红大印。"""
    over(img, shade(rrect([46, 52, 214, 196], 4), PARCH, bevel=14, gloss=0.4, grain=0.35))
    m = blank(); d = ImageDraw.Draw(m)
    d.line([(64, 110), (100, 92), (140, 120), (180, 96), (200, 118)], fill=255, width=5)    # 河流/边界
    d.line([(70, 160), (110, 140), (150, 170), (196, 150)], fill=255, width=4)
    d.line([(120, 60), (118, 190)], fill=255, width=3)
    over(img, shade(m, WOOD, bevel=2, flat=True))
    for x in (34, 222):
        over(img, shade(rrect([x - 14, 40, x + 14, 208], 12), WOOD, bevel=10, grain=0.3))
        over(img, shade(ellipse([x - 16, 30, x + 16, 50]), BRASS, bevel=6))
        over(img, shade(ellipse([x - 16, 198, x + 16, 218]), BRASS, bevel=6))
    over(img, shade(rrect([140, 126, 186, 172], 4), RED, bevel=8, grain=0.25))
    over(img, shade(sub(rrect([146, 132, 180, 166], 2), rrect([152, 138, 174, 160], 1)), PARCH, bevel=2, flat=True))


def crossed_swords(img):
    for flip in (1, -1):
        blade = poly([(128 - flip * 4, 30), (128 + flip * 6, 36), (128 + flip * 8, 170), (128 - flip * 6, 170)])
        blade = rotate(blade, flip * 38, 128, 140)
        over(img, shade(blade, STEEL, bevel=6, gloss=1.0))
        guard = rotate(rrect([100, 168, 156, 180], 6), flip * 38, 128, 140)
        over(img, shade(guard, BRASS, bevel=5))
        grip = rotate(rrect([120, 180, 136, 214], 4), flip * 38, 128, 140)
        over(img, shade(grip, WOOD, bevel=4, grain=0.3))


def coin_stack(img):
    """方孔铜钱叠成一摞，旁边散落两枚。"""
    for k in range(6):
        y = 186 - k * 16
        over(img, shade(rrect([50, y - 10, 170, y + 14], 12), BRASS, bevel=4, gloss=0.5))
        over(img, shade(ellipse([50, y - 22, 170, y + 6]), GOLD, bevel=8, gloss=0.8))
    over(img, shade(rrect([100, 98, 120, 110], 2), LACQ, bevel=2, flat=True))
    for cx, cy in ((190, 150), (200, 190)):
        over(img, shade(ellipse([cx - 34, cy - 18, cx + 34, cy + 18]), GOLD, bevel=8, gloss=0.8))
        over(img, shade(rrect([cx - 8, cy - 5, cx + 8, cy + 5], 2), LACQ, bevel=2, flat=True))


def sample(draw_fn, badge=None, **badge_args):
    img = np.zeros((S, S, 4), np.float32)
    draw_fn(img)
    if badge:
        badge(img, **badge_args)
    return finish(img)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    samples = [
        ("要求称臣", sample(chain, badge_arrow)),
        ("称帝", sample(crown, badge_arrow, deg=90, palette=GOLD)),
        ("法理战争", sample(lambda i: (scroll_map(i), crossed_swords(i)))),
        ("加入朝贡", sample(coin_stack, badge_plus)),
        ("销毁法理", sample(scroll_map, badge_cross)),
    ]
    k = 4
    W = OUT * k + 20
    sheet = Image.new("RGBA", (len(samples) * W + 20, OUT * k + 40 + OUT + 20), (74, 78, 68, 255))
    for i, (_, im) in enumerate(samples):
        sheet.alpha_composite(im.resize((OUT * k, OUT * k), Image.LANCZOS), (20 + i * W, 20))
        sheet.alpha_composite(im, (20 + i * W + (OUT * k - OUT) // 2, OUT * k + 40))
    sheet.save(os.path.join(here, "object_samples.png"))
    print("ok")
