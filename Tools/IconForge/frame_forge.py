"""卷册卡片框(九宫格)：帝国风格。

36×36，与旧 FactionFrame 同尺寸：代码里切九宫格的边距是 (左 11, 下 11, 右 24, 上 24)，
所以装饰只放在离边 10 像素以内，其余是均匀的边与底，拉伸后不变形。

  · FactionFrame          ：深橄榄底(#2c312a，同宽窗口示意图的卡片)、橄榄青铜边(上左受光、下右背光)，
                            四角金色回纹角饰，回纹中心一点朱红(印泥)；
  · FactionFrame_dominate ：高亮卡(身份卡、主导派系)，边换成金色，角饰同上；
  · regimeFrame           ：选中框/强调面板，朱漆边配金色回纹角；
  · clanFrame             ：宗族与谱系卡，橄榄边，四角缠枝纹(金茎、朱红小花、玉色叶)。

python Tools/IconForge/frame_forge.py --preview   # 另存 4 倍放大与卡片效果图 frame_preview.png
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SIZE = 36
BORDER = (11, 11, 24, 24)  # 左 下 右 上，同 UIHelper.AddCardFrame / AddStretchBackground

OUTLINE = (20, 22, 15, 255)
FILL = (44, 49, 42, 246)
INNER = (28, 31, 23, 255)
OLIVE = ((108, 116, 92, 255), (90, 98, 76, 255), (62, 68, 52, 255))        # 受光 本色 背光
LACQUER = ((196, 72, 56, 255), (150, 44, 34, 255), (96, 26, 20, 255))
GOLD_EDGE = ((243, 210, 122, 255), (201, 162, 74, 255), (138, 106, 42, 255))
GOLD = ((250, 222, 140, 255), (214, 172, 78, 255), (140, 104, 40, 255))
GOLD_DARK = (58, 40, 14, 255)
CINNABAR = (196, 58, 44, 255)
BONE = (232, 220, 192, 255)
JADE = (94, 138, 90, 255)

# 缠枝纹(左上角，10×10)：h 金受光 l 金背光 r 朱红花瓣 b 花心 j 玉色叶；其余不动
VINE = [
    "..........",
    "..........",
    "..........",
    "...lhhhhhh",
    "...hlj....",
    "...hjh..r.",
    "...h..lrbr",
    "...h..r.r.",
    "...h.rbr..",
    "...h..r...",
]


def frame(edge, ornament="meander"):
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    px = img.load()
    n = SIZE - 1
    for y in range(SIZE):
        for x in range(SIZE):
            d = min(x, y, n - x, n - y)
            if d == 0:
                px[x, y] = OUTLINE
            elif d <= 2:
                # 边：外沿一圈上、左受光，下、右背光；里沿一圈是本色
                lit = y == d or x == d
                px[x, y] = edge[1] if d == 2 else edge[0] if lit else edge[2]
            elif d == 3:
                px[x, y] = INNER
            else:
                px[x, y] = FILL
    for flip_x in (False, True):
        for flip_y in (False, True):
            (vine if ornament == "vine" else corner)(px, flip_x, flip_y)
    return img


def vine(px, flip_x, flip_y):
    n = SIZE - 1
    colors = {"h": GOLD[0], "l": GOLD[2], "r": CINNABAR, "b": BONE, "j": JADE}
    for y, row in enumerate(VINE):
        for x, ch in enumerate(row):
            if ch in colors:
                px[n - x if flip_x else x, n - y if flip_y else y] = colors[ch]


def corner(px, flip_x, flip_y):
    """左上角的回纹角饰，按 flip 镜像到其他三角。坐标 (x, y) 从左上算。"""
    n = SIZE - 1

    def put(x, y, color):
        px[n - x if flip_x else x, n - y if flip_y else y] = color

    hi, mid, lo = GOLD
    # 角铁：贴着边压一道 L 形金条(两臂各 9 像素，2 像素宽)，臂端折一个回钩
    for i in range(1, 10):
        put(i, 1, hi)
        put(i, 2, mid)
        put(1, i, hi)
        put(2, i, mid)
    put(1, 1, hi)
    put(2, 2, mid)
    for i in range(3, 10):
        put(i, 3, lo)
        put(3, i, lo)
    # 回钩：臂端向内折
    put(9, 3, mid)
    put(9, 4, lo)
    put(8, 4, GOLD_DARK)
    put(3, 9, mid)
    put(4, 9, lo)
    put(4, 8, GOLD_DARK)
    # 回纹小方：3×3 金框，中心一点朱红
    for x in range(5, 8):
        for y in range(5, 8):
            if (x, y) == (6, 6):
                put(x, y, CINNABAR)
            elif x == 5 or y == 5:
                put(x, y, hi)
            else:
                put(x, y, lo)
    # 角饰外缘描暗，和底色分开
    for x, y in ((4, 4), (8, 5), (8, 6), (8, 7), (5, 8), (6, 8), (7, 8), (4, 5), (5, 4)):
        put(x, y, GOLD_DARK if (x, y) not in ((4, 5), (5, 4)) else INNER)


def nine_slice(img, w, h, border=BORDER):
    """按游戏里的九宫格边距拉伸，预览实际效果(最近邻)。"""
    left, bottom, right, top = border
    sw, sh = img.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    xs = [(0, left, 0, left), (left, sw - right, left, w - right), (sw - right, sw, w - right, w)]
    ys = [(0, top, 0, top), (top, sh - bottom, top, h - bottom), (sh - bottom, sh, h - bottom, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            part = img.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.NEAREST)
            out.alpha_composite(part, (dx0, dy0))
    return out


def build():
    return {
        "GameResources/ui/FactionFrame.png": frame(OLIVE),
        "GameResources/ui/FactionFrame_dominate.png": frame(GOLD_EDGE),
        "GameResources/ui/regimeFrame.png": frame(LACQUER),
        "GameResources/ui/clanFrame.png": frame(OLIVE, "vine"),
    }


def preview(icons, old):
    k = 3
    font_path = "C:/Windows/Fonts/msyh.ttc"
    font = ImageFont.truetype(font_path, 9 * k) if os.path.exists(font_path) else ImageFont.load_default()
    small = ImageFont.truetype(font_path, 7 * k) if os.path.exists(font_path) else ImageFont.load_default()
    sheet = Image.new("RGBA", (1240, 760), (58, 62, 49, 255))
    draw = ImageDraw.Draw(sheet)
    names = [("FactionFrame", "普通卡"), ("FactionFrame_dominate", "高亮卡"), ("regimeFrame", "选中/强调"),
             ("clanFrame", "宗族/谱系")]
    for row, source in enumerate(("旧", "新")):
        x = 16
        for name, label in names:
            img = old[name] if source == "旧" else icons[f"GameResources/ui/{name}.png"]
            draw.text((x, 8 + row * 236), f"{source} · {label}", fill=(243, 195, 74), font=small)
            sheet.alpha_composite(img.resize((SIZE * 5, SIZE * 5), Image.NEAREST), (x, 34 + row * 236))
            x += SIZE * 5 + 24

    def card(img, w, h, title, value, rows, ox, oy):
        c = nine_slice(img, w, h).resize((w * k, h * k), Image.NEAREST)
        sheet.alpha_composite(c, (ox, oy))
        draw.text((ox + 8 * k, oy + 4 * k), title, fill=(243, 195, 74), font=font)
        draw.text((ox + w * k - 8 * k - draw.textlength(value, font=small), oy + 6 * k), value, fill=(200, 194, 180), font=small)
        draw.line((ox + 6 * k, oy + 18 * k, ox + w * k - 6 * k, oy + 18 * k), fill=(74, 80, 64), width=1)
        for i, (a, b, col) in enumerate(rows):
            yy = oy + (22 + i * 10) * k
            draw.text((ox + 8 * k, yy), a, fill=(143, 160, 168), font=small)
            draw.text((ox + w * k - 8 * k - draw.textlength(b, font=small), yy), b, fill=col, font=small)

    rows = [("国库", "1248.05万", (243, 195, 74)), ("宪政合法性", "85", (101, 214, 110)), ("民族情绪", "34%", (255, 211, 78))]
    top = 480
    card(icons["GameResources/ui/FactionFrame.png"], 150, 56, "财政", "收支 +3.2万/年", rows, 16, top)
    card(icons["GameResources/ui/FactionFrame_dominate.png"], 128, 56, "中华苏维埃共和国", "",
         [("现代政体 · 华夏", "", (0, 0, 0)), ("共和1845年", "", (0, 0, 0))], 16 + 150 * k + 16, top)
    card(icons["GameResources/ui/regimeFrame.png"], 60, 40, "已选中", "", [], 16 + 278 * k + 32, top)
    card(icons["GameResources/ui/clanFrame.png"], 60, 40, "宗族", "", [], 16 + 338 * k + 48, top)
    sheet.save(os.path.join(HERE, "frame_preview.png"))


if __name__ == "__main__":
    old = {name: Image.open(os.path.join(ROOT, "GameResources/ui", name + ".png")).convert("RGBA")
           for name in ("FactionFrame", "FactionFrame_dominate", "regimeFrame", "clanFrame")}
    icons = build()
    if "--preview" in sys.argv:
        preview(icons, old)
    if "--write" in sys.argv:
        for path, im in icons.items():
            im.save(os.path.join(ROOT, path))
    print("ok", list(icons))
