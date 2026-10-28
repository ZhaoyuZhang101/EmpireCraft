"""外交权能的 godpower 按钮：一卷摊开的盟书(骨白纸、两端卷轴暗木色、三行字迹)，下方一枚红色火漆印。
平涂像素直出，画法同 transmigrant_forge(黑描边、右下投影)。

输出 GameResources/ui/icons/godpowers/diplomacy.png(28×28)。
用法: python Tools/IconForge/diplomacy_forge.py [--preview [输出路径]]
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from transmigrant_forge import (BONE, BONE_DARK, RED, ROOT, HERE, crisp, circle, edge_br, outline_and_shadow,  # noqa: E402
                                paint)

WOOD = (120, 78, 40)
WOOD_DARK = (84, 52, 26)
INK = (88, 80, 70)
SEAL_DARK = (150, 24, 22)


def make():
    n = 28
    a = np.zeros((n, n, 4), np.uint8)
    paper = np.zeros((n, n), bool)
    paper[5:21, 5:23] = True
    paint(a, paper, BONE)
    paint(a, edge_br(paper), BONE_DARK)
    # 两端卷轴
    for y0 in (3, 20):
        rod = np.zeros((n, n), bool)
        rod[y0:y0 + 3, 3:25] = True
        paint(a, rod, WOOD)
        a[y0 + 2, 3:25] = WOOD_DARK + (255,)
    # 字迹
    for y, x1 in ((8, 20), (11, 18), (14, 20)):
        a[y, 8:x1] = INK + (255,)
    # 火漆印
    seal = crisp(n, circle(0.5, 0.74, 0.14))
    seal[:17, :] = False
    paint(a, seal, RED)
    paint(a, edge_br(seal), SEAL_DARK)
    a[20:22, 13:15] = (246, 216, 54, 255)
    return Image.fromarray(outline_and_shadow(a), "RGBA")


if __name__ == "__main__":
    im = make()
    path = os.path.join(ROOT, "GameResources", "ui", "icons", "godpowers", "diplomacy.png")
    im.save(path)
    if "--preview" in sys.argv:
        idx = sys.argv.index("--preview")
        out = sys.argv[idx + 1] if len(sys.argv) > idx + 1 else os.path.join(HERE, "diplomacy_preview.png")
        big = Image.new("RGBA", (28 * 10 + 20, 28 * 10 + 20), (74, 78, 68, 255))
        big.alpha_composite(im.resize((280, 280), Image.NEAREST), (10, 10))
        big.alpha_composite(im, (2, 2))
        big.save(out)
        print("preview:", out)
