# -*- coding: utf-8 -*-
"""终端模式手机网页的图标(添加到手机桌面用)：一道诏书加朱印，照 imperial_forge 的冷峻写实 · 像素输出，
28×28 的像素图按整数倍放大贴到深色底板上(底板四周留边，安卓裁成圆形、方形都不伤到图样)。

    python Tools/IconForge/terminal_icon_forge.py

输出 Terminal/icon-512.png、icon-192.png、apple-touch-icon.png(180)。
"""
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import imperial_forge as forge  # noqa: E402

OUT = os.path.join(forge.ROOT, "Terminal")
BACK = (20, 21, 15, 255)        # 网页的底色
FRAME = (107, 90, 38, 255)      # 旧铜边
FRAME_DARK = (58, 61, 46, 255)


def emblem():
    img = forge.canvas()
    forge.obj_edict(img)
    return forge.pixelize(img)


def plate(size, scale):
    """size 见方的底板，像素图放大 scale 倍居中；边框按一个"像素"(scale)的粗细画。"""
    out = Image.new("RGBA", (size, size), BACK)
    draw = ImageDraw.Draw(out)
    inset = max(scale, size // 24)
    draw.rectangle([inset, inset, size - 1 - inset, size - 1 - inset], outline=FRAME_DARK, width=scale)
    draw.rectangle([inset + scale, inset + scale, size - 1 - inset - scale, size - 1 - inset - scale], outline=FRAME,
                   width=max(1, scale // 2))
    art = emblem().resize((forge.PIXEL * scale, forge.PIXEL * scale), Image.NEAREST)
    out.alpha_composite(art, ((size - art.width) // 2, (size - art.height) // 2))
    return out.convert("RGB")


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, size, scale in (("icon-512.png", 512, 13), ("icon-192.png", 192, 5), ("apple-touch-icon.png", 180, 5)):
        plate(size, scale).save(os.path.join(OUT, name), optimize=True)
        print(name, size)


if __name__ == "__main__":
    main()
