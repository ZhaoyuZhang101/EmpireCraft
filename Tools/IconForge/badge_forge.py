"""国旗圆徽章(用户 2026-10-29："这个国旗的大小太突兀了，建议都改成圆形徽章，图案不变""带点设计感吧""直接就切成圆形吗")。

不是把成品旗切圆：用 flag_formal 里同一面旗的画法重画——旗面底色与纹样照旧，旗的方边框不画，
改成徽章自己的一圈环：
  · 圆面：旗面正中一块(纹样所在)排进圆里，同国旗一样在像素格上限色、纹样一格浮雕、一格投影；
  · 环：圆面外一道深线，一圈旧铜环(左上亮、右下暗)，环上一圈联珠(铜珠一颗一颗)，最外黑描边、右下投影；
    旗本来有金边的用旧铜环，没有金边的用旗面底色压暗的环(同样有联珠)，与旗的气质一致。
像素格 46×46，放大两倍居中放进 120×120(同国旗的画布，游戏里旗帜框按同一尺寸摆)。

临高(穿越政体)的两面旗也做成同样的徽章(用户 10-29 "临高政体的澳洲人文化的星拳旗和启明星旗也用这个风格")：
星拳旗(元老院旗，红底黄齿轮举拳)、启明星旗(国旗，深蓝底白四芒星、断开的圆环、四道白线)，纹样照 transmigrant_forge 的画法。

用法：python Tools/IconForge/badge_forge.py [--preview]
输出：GameResources/ui/flags/badge_culture_<文化>.png、badge_ideology_<理念>.png、badge_aosong_senate.png、
badge_aosong_national.png；预览 Tools/IconForge/badge_preview.png
"""
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import flag_formal as fl  # noqa: E402

G = 46                     # 像素格边长
C = (G - 1) / 2            # 圆心
R_FACE = 16.6              # 圆面半径(格)
R_LINE = 17.6              # 圆面外的深线
R_RING = 21.2              # 环外沿
R_OUT = 22.2               # 黑描边外沿
CROP = 36                  # 从旗面正中取多宽(格，旗面 60×40)
BRASS = [fl.hexc("#EAD9A2"), fl.hexc("#C8A04A"), fl.hexc("#94723A"), fl.hexc("#5E4520")]
LINE = (30, 22, 16)


def _border_recorder(self, color, width=1.0, inset=1.4):
    """徽章不画旗的方边框，只记下边框色(决定环用旧铜还是旗色)"""
    self.border_color = fl.hexc(color) if isinstance(color, str) else color


def face(flag):
    """旗面正中 CROP×CROP 格排成圆面：照 flag_formal.finish 限色、浮雕、投影"""
    x0 = (fl.AW - CROP) / 2 * fl.R
    y0 = (fl.AH - CROP) / 2 * fl.R
    box = (int(x0), int(y0), int(x0 + CROP * fl.R), int(y0 + CROP * fl.R))
    d = int(round(R_FACE * 2)) + 1
    small = flag.img.crop(box).resize((d, d), Image.BOX)
    arr = fl.grade_colors(np.asarray(small).astype(np.float32))
    pal = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).quantize(colors=16, method=Image.Quantize.MEDIANCUT)
    rgb = np.asarray(pal.convert("RGB")).astype(np.float32)
    em = np.asarray(flag.emblem.crop(box).resize((d, d), Image.BOX)) > 110
    up = np.zeros_like(em); up[1:, :] = em[:-1, :]
    left = np.zeros_like(em); left[:, 1:] = em[:, :-1]
    down = np.zeros_like(em); down[:-1, :] = em[1:, :]
    right = np.zeros_like(em); right[:, :-1] = em[:, 1:]
    light = em & (~up | ~left)
    dark = em & (~down | ~right) & ~light
    rgb = np.where(light[..., None], np.clip(rgb * 1.22 + 18, 0, 255), rgb)
    rgb = np.where(dark[..., None], rgb * 0.62, rgb)
    sh = np.zeros_like(em); sh[1:, 1:] = em[:-1, :-1]
    rgb = np.where((sh & ~em)[..., None], rgb * 0.72, rgb)
    return rgb, d


def field_ring(field):
    """没有金边的旗：用旗面底色做环，压暗、提一点亮面"""
    base = fl.grade_colors(np.asarray(field, np.float32).reshape(1, 1, 3)).reshape(3)
    return [tuple(np.clip(base * k + add, 0, 255).astype(int)) for k, add in ((1.25, 30), (0.95, 8), (0.7, 0), (0.48, 0))]


def badge(make):
    original = fl.Flag.border
    fl.Flag.border = _border_recorder
    try:
        flag = make()
    finally:
        fl.Flag.border = original
    rgb, d = face(flag)
    field = np.asarray(flag.img.resize((1, 1), Image.BOX)).reshape(3).astype(np.float32)
    gold = getattr(flag, "border_color", None) is not None
    return compose(rgb, d, BRASS if gold else field_ring(field))


def face_size():
    return int(round(R_FACE * 2)) + 1


def aosong(kind):
    """临高的两面旗：在像素格上直接排圆面(纹样本来就是像素画，不再缩放)"""
    import transmigrant_forge as tf
    d = face_size()
    field = tf.RED if kind == "senate" else tf.NAVY
    face_rgba = np.zeros((d, d, 4), np.uint8)
    face_rgba[..., :3] = field
    face_rgba[..., 3] = 255
    n = 30
    if kind == "senate":
        emblem = tf.senate_color(n, rays=3)
    else:
        star, shade, band, _ = tf.national_parts(n, bars=False)
        emblem = np.zeros((n, n, 4), np.uint8)
        tf.paint(emblem, band, tf.WHITE)
        tf.paint(emblem, star, tf.WHITE)
        tf.paint(emblem, shade, tf.WHITE_SHADE)
    off = (d - n) // 2
    canvas = Image.fromarray(face_rgba, "RGBA")
    canvas.alpha_composite(Image.fromarray(emblem, "RGBA"), (off, off))
    rgb = np.asarray(canvas.convert("RGB")).astype(np.float32)
    if kind == "national":
        # 环外四道白线：左右长、上下短，伸到圆面边上(同国旗)
        c = (d - 1) // 2
        white = np.asarray(tf.WHITE, np.float32)
        for x in range(d):
            if abs(x - c) > 11:
                rgb[c, x] = white
        for y in list(range(0, 3)) + list(range(d - 3, d)):
            rgb[y, c] = white
    return compose(rgb, d, field_ring(field))


def compose(rgb, d, ring):
    """圆面 + 深线 + 联珠环 + 黑描边 + 投影，放大两倍放进 120×120"""
    art = np.zeros((G, G, 4), np.uint8)
    off = C - (d - 1) / 2
    for y in range(G):
        for x in range(G):
            dx, dy = x - C, y - C
            r = math.hypot(dx, dy)
            if r <= R_FACE:
                fx, fy = int(round(x - off)), int(round(y - off))
                fx, fy = min(max(fx, 0), d - 1), min(max(fy, 0), d - 1)
                c = rgb[fy, fx]
                # 圆面靠边一圈略暗：像嵌在环里
                if r > R_FACE - 1.2:
                    c = c * 0.8
                art[y, x, :3] = np.clip(c, 0, 255)
                art[y, x, 3] = 255
            elif r <= R_LINE:
                art[y, x] = LINE + (255,)
            elif r <= R_RING:
                k = (-dx - dy) / max(1.0, r)          # 左上受光
                tone = 0 if k > 0.55 else 1 if k > -0.1 else 2
                c = ring[tone]
                # 联珠：环中线上每 10° 一颗珠(亮点，右下带一格暗)
                mid = (R_LINE + R_RING) / 2
                ang = math.degrees(math.atan2(dy, dx)) % 360
                if abs(r - mid) < 0.75 and (ang % 10) < 4.2:
                    c = ring[0] if k > -0.6 else ring[1]
                elif abs(r - mid) < 0.75:
                    c = ring[3] if tone >= 1 else ring[2]
                art[y, x, :3] = c
                art[y, x, 3] = 255
            elif r <= R_OUT:
                art[y, x] = fl.OUTLINE + (255,)
            elif math.hypot(dx - 1, dy - 1) <= R_OUT:
                art[y, x] = fl.SHADOW
    im = Image.fromarray(art, "RGBA").resize((G * fl.SCALE, G * fl.SCALE), Image.NEAREST)
    out = Image.new("RGBA", (fl.CANVAS, fl.CANVAS), (0, 0, 0, 0))
    out.alpha_composite(im, ((fl.CANVAS - im.width) // 2, (fl.CANVAS - im.height) // 2))
    return out


def build():
    icons = {}
    for name, make in fl.CULTURES.items():
        icons["badge_culture_" + name] = badge(make)
    for name, make in fl.IDEOLOGIES.items():
        icons["badge_ideology_" + name] = badge(make)
    icons["badge_aosong_senate"] = aosong("senate")
    icons["badge_aosong_national"] = aosong("national")
    return icons


if __name__ == "__main__":
    os.makedirs(fl.OUT, exist_ok=True)
    icons = build()
    for key, im in icons.items():
        im.save(os.path.join(fl.OUT, key + ".png"))
    print(len(icons), "badges")
    if "--preview" in sys.argv:
        cols = 9
        cell = fl.CANVAS * 2
        rows = (len(icons) + cols - 1) // cols
        sheet = Image.new("RGBA", (cols * cell, rows * (cell + 20)), (70, 72, 70, 255))
        for i, (key, im) in enumerate(icons.items()):
            x, y = (i % cols) * cell, (i // cols) * (cell + 20)
            sheet.alpha_composite(im.resize((cell, cell), Image.NEAREST), (x, y))
            small = im.resize((40, 40), Image.NEAREST)
            sheet.alpha_composite(small, (x + cell - 44, y + cell - 30))
        sheet.save(os.path.join(HERE, "badge_preview.png"))
        print("preview:", os.path.join(HERE, "badge_preview.png"))
