"""虚拟族人头像(无小人模式)：族谱卡片里没有实体单位的族人用的通用小人图标。

沿用 imperial_forge 的"冷峻写实 · 像素输出"流程：256 画布上画半身像(枪灰衣身、骨白面庞)，
像素化成 28×28 后居中放进 36×36 透明画布(与 ui/deadIcon 同尺寸，族谱卡片直接替换显示)。

用法: python Tools/IconForge/virtual_person_icon.py
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import imperial_forge as imf  # noqa: E402

of = imf.of


def draw():
    img = imf.canvas()
    # 衣身：圆肩半身，枪灰
    of.over(img, of.shade(of.rrect([40, 150, 216, 300], 70), imf.STEEL, bevel=18, gloss=0.5, grain=0.12))
    # 交领：骨白斜襟
    of.over(img, of.shade(of.poly([(128, 160), (96, 150), (128, 230)]), imf.BONE, bevel=4, flat=True))
    of.over(img, of.shade(of.poly([(128, 160), (160, 150), (128, 230)]), imf.BONE, bevel=4, flat=True))
    # 颈与头：骨白，不画五官(通用人物)
    of.over(img, of.shade(of.rrect([110, 120, 146, 160], 10), imf.BONE, bevel=6))
    of.over(img, of.shade(of.ellipse([82, 36, 174, 136]), imf.BONE, bevel=22, gloss=0.6))
    # 发髻/头巾：旧铜，表示"族人"而非具体身份
    of.over(img, of.shade(of.ellipse([86, 30, 170, 92]), imf.BRASS, bevel=12, gloss=0.8))
    return img


def main():
    small = imf.pixelize(draw())
    out = Image.new("RGBA", (36, 36), (0, 0, 0, 0))
    out.alpha_composite(small, (4, 4))
    path = os.path.join(imf.ROOT, "GameResources", "ui", "virtualPersonIcon.png")
    out.save(path)
    print("saved", path)


if __name__ == "__main__":
    main()
