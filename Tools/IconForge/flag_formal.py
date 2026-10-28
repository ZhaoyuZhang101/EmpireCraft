"""正式国旗(用户 2026-10-29："要庄严严肃的正式国旗风格，而且要有像素感""兽纹……一点都不大气和庄严""龙为什么不用明朝的团龙纹")。

  · 旗式：3:2 长方形正式国旗，不要旗杆、流苏、布褶；旗面平涂、色调庄重(冷峻配色，金转旧铜)；
  · 像素感：先在 60×40 的像素格上成图(按旗限色、无半透明过渡)，纹样一格高光一格阴影的浮雕，旗外 1 像素深色框与投影，
    再最近邻放大两倍成 120×120(旗面 120×80)，游戏里用最近邻显示(不进 HiResIconFilter)；
  · 兽纹照公有领域/CC0 纹章底稿(Tools/IconForge/source/heraldry，来源见 SOURCES.md)：团龙(清旗龙置于金圆章内)、神罗双头鹰、
    罗马鹰、狼首、渡鸦、雷鸟、百合花；别的纹样沿用 flag_forge.py 里的画法(只取中间的纹章，不要旗边条纹)。
  · 不排出像现代国旗的配色。

用法：python Tools/IconForge/flag_formal.py [--preview]
输出：GameResources/ui/flags/culture_<文化>.png、ideology_<理念>.png；预览 Tools/IconForge/flag_preview.png
"""
import io
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import flag_forge as ff  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "GameResources", "ui", "flags")
SRC = os.path.join(HERE, "source", "heraldry")

AW, AH = 60, 40          # 像素格(旗面)
R = 10                   # 成图倍数
W, H = AW * R, AH * R    # 高分辨率画布
SCALE = 2                # 输出放大
CANVAS = 120             # 输出边长

OUTLINE = (14, 12, 12)
SHADOW = (0, 0, 0, 90)


def hexc(h):
    h = h.lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


GOLD = hexc("#C8A04A")
GOLD_DARK = hexc("#6E5220")
GOLD_LIGHT = hexc("#E8D08A")


# ───────── 底稿 ─────────

def _render_svg(name, width=900):
    import cairosvg
    png = cairosvg.svg2png(url=os.path.join(SRC, name), output_width=width)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def _crop(mask_img):
    box = mask_img.getbbox()
    return mask_img.crop(box) if box else mask_img


def source_emblem(name, mode):
    """底稿 → (剪影遮罩, 线条遮罩)；线条是剪影内部的深色描线(纹章的羽片、鳞甲)"""
    path = os.path.join(SRC, name)
    if not os.path.exists(path):
        return None
    im = _render_svg(name)
    a = np.asarray(im).astype(np.float32)
    rgb, alpha = a[..., :3], a[..., 3]
    lum = rgb @ np.array([0.3, 0.59, 0.11])
    if mode == "lineart":          # 白底黑线的纹章画：整只剪影 + 内部黑线
        sil = alpha > 40
        sil = ndimage.binary_fill_holes(sil)
        lines = sil & (lum < 110)
    elif mode == "yellow":         # 盾上的黄色兽首
        r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
        sil = (alpha > 40) & (r > 180) & (g > 140) & (b < 120)
        lines = np.zeros_like(sil)
    elif mode == "recolor":        # 盾上的黄兽连同里面的牙、眼(黄→金、白→骨白、黑→深色)
        r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
        yellow = (alpha > 40) & (r > 180) & (g > 140) & (b < 120)
        sil = ndimage.binary_fill_holes(ndimage.binary_closing(yellow, iterations=3))
        lines = sil & ~yellow
    elif mode == "dark":           # 银底黑兽
        sil = (alpha > 40) & (lum < 80)
        inside = ndimage.binary_erosion(sil, iterations=1)
        lines = np.zeros_like(sil) & inside
    elif mode == "strokes":        # 细笔画的图画文字(雷鸟签名)：笔画加粗成剪影
        sil = ndimage.binary_dilation((alpha > 40) & (lum < 160), iterations=max(4, im.width // 90))
        lines = np.zeros_like(sil)
    else:                          # 整块剪影(百合、清旗龙)：深色当描线
        sil = alpha > 40
        lines = sil & (lum < 70)
    sil_img = Image.fromarray((sil * 255).astype(np.uint8))
    box = sil_img.getbbox()
    sil_img = sil_img.crop(box)
    line_img = Image.fromarray((lines * 255).astype(np.uint8)).crop(box)
    return sil_img, line_img


# ───────── 成图 ─────────

class Flag:
    def __init__(self, field):
        self.img = Image.new("RGB", (W, H), hexc(field))
        self.d = ImageDraw.Draw(self.img)
        self.emblem = Image.new("L", (W, H), 0)   # 纹样所在(像素格上起浮雕)

    def rect(self, box, color):
        self.d.rectangle(box, fill=hexc(color) if isinstance(color, str) else color)

    def border(self, color, width=1.0, inset=1.4):
        """金边：离旗边 inset 格、宽 width 格"""
        c = hexc(color) if isinstance(color, str) else color
        o, w = int(inset * R), max(1, int(width * R))
        self.d.rectangle([o, o, W - 1 - o, H - 1 - o], outline=c, width=w)

    def place(self, sil, lines, color, line_color, cx, cy, height, flip=False):
        """把剪影按高度 height(格)放在(cx, cy)(格)为中心"""
        h = int(height * R)
        w = max(1, int(sil.width * h / sil.height))
        s = sil.resize((w, h), Image.LANCZOS)
        ln = lines.resize((w, h), Image.LANCZOS)
        if flip:
            s, ln = s.transpose(Image.FLIP_LEFT_RIGHT), ln.transpose(Image.FLIP_LEFT_RIGHT)
        x, y = int(cx * R - w / 2), int(cy * R - h / 2)
        self.img.paste(color, (x, y, x + w, y + h), s.point(lambda v: 255 if v > 110 else 0))
        if line_color is not None:
            self.img.paste(line_color, (x, y, x + w, y + h), ln.point(lambda v: 255 if v > 120 else 0))
        self.emblem.paste(255, (x, y), s.point(lambda v: 255 if v > 110 else 0))

    def paste_rgba(self, layer, cx, cy, height):
        h = int(height * R)
        w = max(1, int(layer.width * h / layer.height))
        lay = layer.resize((w, h), Image.LANCZOS)
        x, y = int(cx * R - w / 2), int(cy * R - h / 2)
        a = lay.split()[3].point(lambda v: 255 if v > 120 else 0)
        self.img.paste(lay.convert("RGB"), (x, y), a)
        self.emblem.paste(255, (x, y), a)


def old_emblem(fn):
    """沿用 flag_forge 的纹样画法：在透明方框里画，只留不碰框边的部件(去掉旗边条纹、边框、穗)"""
    ff.BX0, ff.BY0, ff.BX1, ff.BY1 = 30, 30, 186, 186
    canvas = Image.new("RGBA", (ff.N, ff.N), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    ff.EXT = ImageDraw.Draw(Image.new("RGBA", (ff.N, ff.N)))
    fn(d)
    a = np.asarray(canvas)[..., 3] > 40
    labels, count = ndimage.label(a)
    x0, y0, x1, y1 = ff.S(30), ff.S(30), ff.S(186), ff.S(186)
    keep = np.zeros_like(a)
    for k in range(1, count + 1):
        ys, xs = np.nonzero(labels == k)
        if len(xs) < 30:
            continue
        touches = xs.min() <= x0 + ff.S(3) or xs.max() >= x1 - ff.S(3) or ys.max() >= y1 - ff.S(3)
        if not touches:
            keep |= labels == k
    arr = np.asarray(canvas).copy()
    arr[..., 3] = np.where(keep, arr[..., 3], 0)
    out = Image.fromarray(arr, "RGBA")
    return out.crop(out.getbbox()) if out.getbbox() else out


def grade_colors(arr):
    """冷峻：降饱和、金转旧铜、提对比(照 flag_forge.grade，力度轻些，旗色要庄重不要灰)"""
    a = np.zeros((arr.shape[0], arr.shape[1], 4), np.float32)
    a[..., :3] = arr
    a[..., 3] = 255
    a = ff.grade(a)
    return a[..., :3]


def finish(flag, colors=16):
    """高分辨率 → 60×40 像素格：限色、纹样浮雕、外框与投影，最近邻放大两倍"""
    small = flag.img.resize((AW, AH), Image.BOX)
    arr = grade_colors(np.asarray(small).astype(np.float32))
    pal = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).quantize(colors=colors, method=Image.Quantize.MEDIANCUT)
    rgb = np.asarray(pal.convert("RGB")).astype(np.int16)
    em = np.asarray(flag.emblem.resize((AW, AH), Image.BOX)) > 110
    # 纹样一格浮雕：左上边亮、右下边暗
    up = np.zeros_like(em); up[1:, :] = em[:-1, :]
    left = np.zeros_like(em); left[:, 1:] = em[:, :-1]
    down = np.zeros_like(em); down[:-1, :] = em[1:, :]
    right = np.zeros_like(em); right[:, :-1] = em[:, 1:]
    light = em & (~up | ~left)
    dark = em & (~down | ~right) & ~light
    rgb = np.where(light[..., None], np.clip(rgb * 1.22 + 18, 0, 255), rgb)
    rgb = np.where(dark[..., None], rgb * 0.62, rgb)
    # 纹样在旗面上的投影(右下一格)
    sh = np.zeros_like(em); sh[1:, 1:] = em[:-1, :-1]
    rgb = np.where((sh & ~em)[..., None], rgb * 0.72, rgb)
    # 旗面边缘：上、左一格稍亮，下、右一格稍暗(旗面像钉在板上，庄重不飘)
    rgb[0, :] = np.clip(rgb[0, :] * 1.12, 0, 255)
    rgb[:, 0] = np.clip(rgb[:, 0] * 1.12, 0, 255)
    rgb[-1, :] = rgb[-1, :] * 0.78
    rgb[:, -1] = rgb[:, -1] * 0.78
    art = np.zeros((AH + 3, AW + 3, 4), np.uint8)
    art[..., :] = (0, 0, 0, 0)
    art[1:AH + 1, 1:AW + 1, :3] = np.clip(rgb, 0, 255)
    art[1:AH + 1, 1:AW + 1, 3] = 255
    art[0, 0:AW + 2, :] = OUTLINE + (255,)
    art[AH + 1, 0:AW + 2, :] = OUTLINE + (255,)
    art[0:AH + 2, 0, :] = OUTLINE + (255,)
    art[0:AH + 2, AW + 1, :] = OUTLINE + (255,)
    art[2:AH + 3, AW + 2, :] = SHADOW
    art[AH + 2, 2:AW + 3, :] = SHADOW
    im = Image.fromarray(art, "RGBA").resize(((AW + 3) * SCALE, (AH + 3) * SCALE), Image.NEAREST)
    out = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    out.alpha_composite(im, ((CANVAS - im.width) // 2, (CANVAS - im.height) // 2))
    return out


def text(flag, s, cx, cy, size, color):
    f = ImageFont.truetype("timesbd.ttf", int(size * R))
    tw = flag.d.textlength(s, font=f)
    flag.d.text((cx * R - tw / 2, cy * R - size * R * 0.62), s, font=f, fill=color)
    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).text((cx * R - tw / 2, cy * R - size * R * 0.62), s, font=f, fill=255)
    flag.emblem.paste(255, (0, 0), mask)


# ───────── 各旗 ─────────

def huaxia():
    """团龙：赤底金圆章，章内金龙(清旗龙底稿)，四角云头式金角"""
    f = Flag("#7E1C18")
    f.border(GOLD, 0.8, 1.6)
    cx, cy, r = AW / 2, AH / 2, 15.5
    f.d.ellipse([(cx - r) * R, (cy - r) * R, (cx + r) * R, (cy + r) * R], fill=GOLD_DARK)
    f.d.ellipse([(cx - r + 1.2) * R, (cy - r + 1.2) * R, (cx + r - 1.2) * R, (cy + r - 1.2) * R], fill=hexc("#5E1210"))
    f.emblem.paste(255, (0, 0), ring_mask(cx, cy, r, 1.2))
    src = source_emblem("dragon_banner.svg", "solid")
    if src:
        f.place(src[0], src[1], GOLD, GOLD_DARK, cx, cy + 0.3, 25)
    for (x, y) in ((4.5, 4.5), (AW - 4.5, 4.5), (4.5, AH - 4.5), (AW - 4.5, AH - 4.5)):
        f.d.ellipse([(x - 1.6) * R, (y - 1.6) * R, (x + 1.6) * R, (y + 1.6) * R], fill=GOLD)
    return f


def ring_mask(cx, cy, r, w):
    m = Image.new("L", (W, H), 0)
    ImageDraw.Draw(m).ellipse([(cx - r) * R, (cy - r) * R, (cx + r) * R, (cy + r) * R], outline=255, width=int(w * R))
    return m


def roma():
    f = Flag("#7A1616")
    f.border(GOLD, 0.8, 1.6)
    src = source_emblem("eagle_rising.svg", "lineart")
    if src:
        f.place(src[0], src[1], GOLD, GOLD_DARK, AW / 2, 15.5, 21)
    text(f, "SPQR", AW / 2, 32.5, 7.2, GOLD)
    return f


def germanic():
    f = Flag("#C8A04A")
    src = source_emblem("eagle_double.svg", "lineart")
    if src:
        f.place(src[0], src[1], (22, 20, 20), (70, 64, 58), AW / 2, AH / 2, 33)
    return f


def frankish():
    f = Flag("#1E3570")
    src = source_emblem("fleur.svg", "solid")
    if src:
        for (x, y) in ((AW / 2 - 12, 13), (AW / 2 + 12, 13), (AW / 2, 28)):
            f.place(src[0], src[1], GOLD, GOLD_DARK, x, y, 13)
    return f


def youmu():
    f = Flag("#5A2420")
    f.rect([0, (AH - 6) * R, W, H], hexc("#2E1210"))
    src = source_emblem("wolf_porsanger.svg", "recolor")
    if src:
        f.place(src[0], src[1], GOLD, hexc("#2A1A10"), AW / 2, AH / 2 - 2.5, 26, flip=True)
    return f


def viking():
    f = Flag("#E0DAC8")
    f.border("#7E1C18", 1.6, 0)
    src = source_emblem("raven_ramnes.svg", "dark")
    if src:
        f.place(src[0], src[1], (20, 20, 24), None, AW / 2, AH / 2, 28)
    else:
        f.paste_rgba(old_emblem(ff.em_raven), AW / 2, AH / 2, 26)
    return f


def ojibwe():
    f = Flag("#B8946A")
    f.rect([0, 0, W, int(2.2 * R)], hexc("#3A2418"))
    f.rect([0, H - int(2.2 * R), W, H], hexc("#3A2418"))
    # 1701 年蒙特利尔大和约上的密西沙加雷鸟签名(source/heraldry)是真迹，但细笔画缩到旗上认不出，旗上用几何雷鸟
    f.paste_rgba(old_emblem(ff.em_thunderbird), AW / 2, AH / 2, 28)
    return f


def shanhai():
    """山海：青底，金日、白山三峰、下两行浪"""
    f = Flag("#1F5250")
    d, white = f.d, hexc("#E6E2D4")
    m = ImageDraw.Draw(f.emblem)
    sx, sy, sr = AW * 0.74, AH * 0.26, 4.2
    for i in range(12):
        a = math.radians(i * 30)
        d.line([(sx * R, sy * R), ((sx + math.cos(a) * 6.5) * R, (sy + math.sin(a) * 6.5) * R)], fill=GOLD, width=int(0.6 * R))
    d.ellipse([(sx - sr) * R, (sy - sr) * R, (sx + sr) * R, (sy + sr) * R], fill=GOLD)
    m.ellipse([(sx - sr) * R, (sy - sr) * R, (sx + sr) * R, (sy + sr) * R], fill=255)
    base = AH * 0.72
    for (mx, mh, mw) in ((AW * 0.3, 15, 18), (AW * 0.5, 21, 22), (AW * 0.68, 12, 16)):
        pts = [((mx - mw / 2) * R, base * R), (mx * R, (base - mh) * R), ((mx + mw / 2) * R, base * R)]
        d.polygon(pts, fill=white)
        m.polygon(pts, fill=255)
        d.polygon([(mx * R, (base - mh) * R), ((mx + mw * 0.12) * R, (base - mh * 0.62) * R), ((mx - mw * 0.05) * R, (base - mh * 0.55) * R)],
                  fill=hexc("#9DB4AE"))
    for row in range(2):
        y = base + 2.4 + row * 3.6
        for k in range(9):
            x = 3 + k * 6.6
            d.arc([x * R, (y - 1.6) * R, (x + 5.4) * R, (y + 1.6) * R], 180, 360, fill=white, width=int(0.75 * R))
    return f


def derafsh():
    """德拉夫什·卡维亚尼：紫红底金菱格，正中金四芒星嵌红宝石"""
    f = Flag("#5A1830")
    d = f.d
    step = 5
    for k in range(-10, 22):
        d.line([(k * step * R, 0), ((k * step + AH) * R, H)], fill=hexc("#9C7A3A"), width=int(0.45 * R))
        d.line([(k * step * R, H), ((k * step + AH) * R, 0)], fill=hexc("#9C7A3A"), width=int(0.45 * R))
    f.border(GOLD, 0.9, 1.4)
    cx, cy = AW / 2, AH / 2
    star = [(cx + (14 if i % 2 == 0 else 4) * math.cos(math.radians(-90 + i * 45)) * R / R,
             cy + (14 if i % 2 == 0 else 4) * math.sin(math.radians(-90 + i * 45)) * R / R) for i in range(8)]
    pts = [(x * R, y * R) for x, y in star]
    d.polygon(pts, fill=GOLD)
    ImageDraw.Draw(f.emblem).polygon(pts, fill=255)
    d.ellipse([(cx - 2.6) * R, (cy - 2.6) * R, (cx + 2.6) * R, (cy + 2.6) * R], fill=hexc("#9A1E28"))
    return f


def umayyad():
    """倭马亚白旗：骨白底旧铜边，正中旧铜八角星(两方相叠)、白心金点"""
    f = Flag("#E4DECF")
    bronze = hexc("#7A5C28")
    f.border(bronze, 1.0, 1.4)
    cx, cy, r = AW / 2, AH / 2, 12.5
    m = ImageDraw.Draw(f.emblem)
    for rot in (0, 45):
        pts = [((cx + r * math.cos(math.radians(rot + 45 + 90 * k))) * R, (cy + r * math.sin(math.radians(rot + 45 + 90 * k))) * R)
               for k in range(4)]
        f.d.polygon(pts, fill=bronze)
        m.polygon(pts, fill=255)
    f.d.ellipse([(cx - 5) * R, (cy - 5) * R, (cx + 5) * R, (cy + 5) * R], fill=hexc("#E4DECF"))
    f.d.ellipse([(cx - 2.4) * R, (cy - 2.4) * R, (cx + 2.4) * R, (cy + 2.4) * R], fill=GOLD)
    return f


def sword_flag():
    """权威：铁灰底，银剑直立、金柄"""
    f = Flag("#42474E")
    cx = AW / 2
    steel, edge = hexc("#D8DAD8"), hexc("#7A7E82")
    blade = [((cx - 1.6) * R, 9 * R), (cx * R, 4 * R), ((cx + 1.6) * R, 9 * R), ((cx + 1.6) * R, 28 * R), ((cx - 1.6) * R, 28 * R)]
    f.d.polygon(blade, fill=steel)
    f.d.line([(cx * R, 8 * R), (cx * R, 27 * R)], fill=edge, width=int(0.5 * R))
    f.d.rectangle([(cx - 7) * R, 28 * R, (cx + 7) * R, 30 * R], fill=GOLD)
    f.d.rectangle([(cx - 1) * R, 30 * R, (cx + 1) * R, 35 * R], fill=GOLD_DARK)
    f.d.ellipse([(cx - 1.8) * R, 34.5 * R, (cx + 1.8) * R, 38 * R], fill=GOLD)
    m = ImageDraw.Draw(f.emblem)
    m.polygon(blade, fill=255)
    m.rectangle([(cx - 7) * R, 28 * R, (cx + 7) * R, 38 * R], fill=255)
    return f


def militarist():
    """军国(法西斯一档；不用任何真实党徽)：黑底，暗红横带，交叉双剑"""
    f = Flag("#121212")
    red = hexc("#A02424")
    f.rect([0, int(26 * R), W, int(30 * R)], red)
    m = ImageDraw.Draw(f.emblem)
    for sx in (-1, 1):
        a, b = ((AW / 2 + sx * 11) * R, 5 * R), ((AW / 2 - sx * 9) * R, 30 * R)
        f.d.line([a, b], fill=red, width=int(1.8 * R))
        m.line([a, b], fill=255, width=int(1.8 * R))
        hx, hy = (AW / 2 - sx * 7.5) * R, 28 * R
        f.d.line([(hx - 3 * R, hy - sx * 2.4 * R), (hx + 3 * R, hy + sx * 2.4 * R)], fill=hexc("#C8A04A"), width=int(1.2 * R))
    return f


def from_old(field, fn, height=28, border=None, band=None):
    def make():
        f = Flag(field)
        if band:
            f.rect([0, (AH - band[1]) * R, W, H], hexc(band[0]))
        if border:
            f.border(border, 0.8, 1.6)
        f.paste_rgba(old_emblem(fn), AW / 2, AH / 2 - (band[1] / 2 if band else 0), height)
        return f
    return make


CULTURES = {
    "Huaxia": huaxia,
    "China": huaxia,
    "Shanhai": shanhai,
    "Japan": from_old("#6E1A1E", ff.em_hinomaru_nobori, 30, border=GOLD),
    "Youmu": youmu,
    "Roma": roma,
    "Western": from_old("#3E2258", ff.em_labarum, 30, border=GOLD),
    "Frankish": frankish,
    "Germanic": germanic,
    "Viking": viking,
    "Slavonic": from_old("#741A20", ff.em_spas, 26, border=GOLD),
    "Arab": umayyad,
    "Persepolis": derafsh,
    "Egypt": from_old("#1C3570", ff.em_winged_sun, 22, border=GOLD),
    "India": from_old("#C8742A", ff.em_surya, 28),
    "Kosher": from_old("#33204A", ff.em_menorah, 28, border=GOLD),
    "Aztec": from_old("#1C5C58", ff.em_quetzal, 26, border="#8A2A22"),
    "Ojibwe": ojibwe,
    "Emperor": from_old("#151515", ff.em_sunburst_crown, 30, border=GOLD),
    "ElfFancy": from_old("#284E32", ff.em_tree, 30),
    "Corporate": from_old("#323C46", ff.em_corporate, 24),
}

IDEOLOGIES = {
    "Anarchism": lambda: anarchism(),
    "SocialDemocracy": from_old("#8A2A22", ff.em_rose, 28),
    "Libertarianism": from_old("#A88430", ff.em_torch, 30),
    "SocialLiberalism": from_old("#36607E", ff.em_dove, 24),
    "Capitalism": from_old("#2A603E", ff.em_coins, 26),
    "ConservativeLiberalism": from_old("#203C66", ff.em_oak, 28),
    "Centrism": from_old("#584080", ff.em_ring, 28),
    "Socialism": from_old("#7E1A1A", ff.em_cog_wheat, 28),
    "Communism": from_old("#761216", ff.em_star, 28),
    "ReligiousDemocracy": from_old("#E2DCCC", ff.em_radiance, 30),
    "Authoritarianism": sword_flag,
    "Conservatism": from_old("#1C2A50", ff.em_crown_blue, 22),
    "Fascism": militarist,
}


def anarchism():
    f = Flag("#121212")
    f.d.polygon([(W, 0), (W, H), (0, H)], fill=hexc("#7E1A1A"))
    return f


def build():
    icons = {}
    for name, make in CULTURES.items():
        icons["culture_" + name] = finish(make())
    for name, make in IDEOLOGIES.items():
        icons["ideology_" + name] = finish(make())
    return icons


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    icons = build()
    for key, im in icons.items():
        im.save(os.path.join(OUT, key + ".png"))
    print(len(icons), "flags")
    if "--preview" in sys.argv:
        cols = 9
        cell = CANVAS * 2
        rows = (len(icons) + cols - 1) // cols
        sheet = Image.new("RGBA", (cols * cell, rows * (cell + 40)), (70, 72, 70, 255))
        for i, (key, im) in enumerate(icons.items()):
            x, y = (i % cols) * cell, (i // cols) * (cell + 40)
            sheet.alpha_composite(im.resize((cell, cell), Image.NEAREST).crop((0, 30, cell, cell - 30)), (x, y))
            small = im.resize((60, 60), Image.NEAREST)
            sheet.alpha_composite(small, (x + (cell - 60) // 2, y + cell - 60))
        sheet.save(os.path.join(HERE, "flag_preview.png"))
