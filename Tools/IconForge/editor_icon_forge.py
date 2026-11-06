"""帝国盒子编辑器(RegimeEditor)的程序图标：诏书 + 右下角一支朱笔(编写制度，取皇帝批红的意思)。

画法同 imperial_forge 的谋划图标：256 画布写实绘制 → 像素化成 28×28(冷峻调色、1px 纯黑描边与投影)，
再按整数倍最近邻放大做程序图标，像素不糊：
  RegimeEditor/assets/app-icon.png   256×256，窗口图标
  RegimeEditor/assets/app-icon.ico   16~256 多尺寸，打包便携版用(package.json 的 build.win.icon)

用法: python Tools/IconForge/editor_icon_forge.py [--preview]
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import imperial_forge as imf       # noqa: E402

of, shade, over, poly, rrect, rotate = imf.of, imf.shade, imf.over, imf.poly, imf.rrect, imf.rotate
ROOT = imf.ROOT
OUT_DIR = os.path.join(ROOT, "RegimeEditor", "assets")
PIXEL = imf.PIXEL


def obj_edict_bold(img):
    """诏书：同 imperial_forge.obj_edict，字行加粗、少几行，缩到 28 像素还看得出是写了字的。"""
    over(img, shade(rrect([58, 44, 198, 212], 4), imf.PARCH, bevel=12, gloss=0.4, grain=0.35))
    for k, bottom in enumerate((186, 186, 186, 140)):
        x = 168 - k * 28
        over(img, shade(rrect([x - 6, 66, x + 6, bottom], 3), imf.LACQ, bevel=2, flat=True))
    over(img, shade(rrect([66, 150, 112, 194], 4), imf.RED, bevel=6, grain=0.2))
    for y in (36, 220):
        over(img, shade(rrect([42, y - 12, 214, y + 12], 10), imf.WOOD, bevel=8, grain=0.3))
        over(img, shade(imf.ellipse([30, y - 14, 50, y + 14]), imf.BRASS, bevel=5))
        over(img, shade(imf.ellipse([206, y - 14, 226, y + 14]), imf.BRASS, bevel=5))


def badge_brush(img, cx=imf.BX, cy=imf.BY):
    """角标：斜放的朱笔(朱漆笔杆、旧铜箍、骨白笔毫蘸朱砂)，笔杆朝右上、笔尖朝左下落在诏书上。"""
    handle = rrect([cx - 15, cy - 70, cx + 15, cy + 16], 7)
    cap = rrect([cx - 16, cy - 80, cx + 16, cy - 64], 5)
    band = rrect([cx - 17, cy + 10, cx + 17, cy + 26], 4)
    hair = poly([(cx - 16, cy + 24), (cx + 16, cy + 24), (cx + 12, cy + 50), (cx, cy + 76), (cx - 12, cy + 50)])
    ink = poly([(cx - 10, cy + 54), (cx + 10, cy + 54), (cx, cy + 76)])
    parts = [rotate(m, -45, cx, cy) for m in (handle, cap, band, hair, ink)]
    halo = imf.union(*parts).filter(of.ImageFilter.MaxFilter(11))
    over(img, shade(halo, imf.LACQ, bevel=2, flat=True))
    over(img, shade(parts[0], imf.RED, bevel=8, gloss=0.8, grain=0.2))
    for m in (parts[1], parts[2]):
        over(img, shade(m, imf.BRASS, bevel=4, gloss=0.9))
    over(img, shade(parts[3], imf.BONE, bevel=6, gloss=0.5, grain=0.3))
    over(img, shade(parts[4], imf.BLOOD, bevel=4))


def build_pixel():
    layer = imf.canvas()
    obj_edict_bold(layer)
    img = imf.inset(layer, 0.86, imf.C - 14, imf.C - 14)
    badge_brush(img)
    return imf.pixelize(img)


def fit(px, size):
    """28 像素图放进 size×size：能整倍放大就最近邻放大居中，太小的尺寸才平滑缩小。"""
    k = size // PIXEL
    if k >= 1 and (size - k * PIXEL) <= size // 4:
        big = px.resize((PIXEL * k, PIXEL * k), Image.NEAREST)
        out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        out.alpha_composite(big, ((size - big.width) // 2, (size - big.height) // 2))
        return out
    return px.resize((PIXEL * 8, PIXEL * 8), Image.NEAREST).resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    px = build_pixel()
    os.makedirs(OUT_DIR, exist_ok=True)
    fit(px, 256).save(os.path.join(OUT_DIR, "app-icon.png"))
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [fit(px, s) for s in sizes]
    frames[-1].save(os.path.join(OUT_DIR, "app-icon.ico"), format="ICO", sizes=[(s, s) for s in sizes],
                    append_images=frames[:-1])
    if "--preview" in sys.argv:
        bg = (74, 78, 68, 255)
        sheet = Image.new("RGBA", (256 + 24 + sum(s + 12 for s in sizes[:-1]), 280), bg)
        sheet.alpha_composite(frames[-1], (12, 12))
        x = 256 + 24
        for s, im in zip(sizes[:-1], frames[:-1]):
            sheet.alpha_composite(im, (x, 268 - s))
            x += s + 12
        sheet.save(os.path.join(HERE, "editor_icon_preview.png"))
    print("wrote", os.path.relpath(OUT_DIR, ROOT))
