"""纯净地图城市平面图的建筑图块(见 Scripts/GamePatches/PureMapPatch.cs 的"城市平面图")。

纯净地图模式下建筑不画，地图色块里按每座城的建筑数据生成一张平面图：街格(白线)切出的每个街区放一座建筑的
3×3 像素图块。不依赖原版或兼容模组的建筑贴图，全部在这里画，按时代分代：
  住宅 house_1..8：茅屋、瓦房、院落、楼阁、坊市、近代楼房、摩天楼、生态城(对应科技树的八项住房技术)；
  工业 industry_0..2：作坊、蒸汽工厂、现代工厂；商业 market_0..1：集市、商场；
  官署 civic_0..1：衙署、现代政府大楼；寺庙 temple、兵营 barracks、码头 docks。
每张图块 3×3 像素，"." 为透明(露出街区底色)；行自上而下。

python Tools/IconForge/citymap_forge.py --preview   # 输出 citymap_preview.png(放大)
python Tools/IconForge/citymap_forge.py --write     # 写入 GameResources/ui/citymap/<名>.png
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

PALETTE = {
    "Y": (196, 160, 90),    # 茅草
    "K": (90, 98, 110),     # 青瓦
    "W": (240, 234, 222),   # 粉墙
    "D": (90, 80, 70),      # 门
    "R": (170, 70, 55),     # 朱漆
    "S": (200, 205, 210),   # 钢、石
    "B": (178, 96, 72),     # 红砖
    "w": (230, 230, 210),   # 窗
    "G": (120, 160, 190),   # 玻璃
    "g": (120, 190, 140),   # 绿植
    "T": (225, 190, 90),    # 金瓦
    "t": (140, 100, 60),    # 木
    "C": (70, 70, 78),      # 烟囱
    "s": (205, 205, 200),   # 烟
    "O": (240, 170, 80),    # 篷布
    "M": (150, 70, 65),     # 营垒
    "N": (110, 160, 215),   # 码头
    "U": (80, 110, 170),    # 蓝顶
}

ICONS = {
    "house_1": [".Y.", "YYY", "WDW"],   # 茅屋
    "house_2": ["KKK", "WWW", "WDW"],   # 瓦房
    "house_3": ["KKK", "K.K", "KKK"],   # 院落(四合院)
    "house_4": ["KKK", "RWR", "RDR"],   # 楼阁
    "house_5": ["SKS", "KKK", "SKS"],   # 坊市(坊墙围起的街坊)
    "house_6": ["BBB", "BwB", "BBB"],   # 近代楼房
    "house_7": ["SGS", "SGS", "SGS"],   # 摩天楼
    "house_8": ["GgG", "gGg", "GgG"],   # 生态城
    "industry_0": ["K.K", "ttt", "tDt"],  # 作坊
    "industry_1": ["C.s", "BBB", "BBB"],  # 蒸汽工厂(烟囱冒烟)
    "industry_2": ["S.S", "SSS", "GGG"],  # 现代工厂
    "market_0": ["OWO", "O.O", "OOO"],    # 集市
    "market_1": ["OGO", "GGG", "OGO"],    # 商场
    "civic_0": ["TTT", "RWR", "RDR"],     # 衙署
    "civic_1": ["UUU", "WWW", "WDW"],     # 现代政府大楼
    "temple": [".T.", "TWT", "WDW"],
    "barracks": ["MMM", "M.M", "MMM"],
    "docks": ["NWN", "NWN", "NNN"],
}


def tile(rows):
    img = Image.new("RGBA", (3, 3), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        assert len(row) == 3, row
        for x, ch in enumerate(row):
            if ch != ".":
                img.putpixel((x, y), PALETTE[ch] + (255,))
    return img


def build():
    assert all(len(rows) == 3 for rows in ICONS.values())
    return {f"GameResources/ui/citymap/{name}.png": tile(rows) for name, rows in ICONS.items()}


def preview(images):
    scale = 16
    names = list(ICONS)
    sheet = Image.new("RGBA", (len(names) * 4 * scale + scale, 4 * scale + scale), (60, 90, 70, 255))
    block = (236, 232, 224, 255)
    for i, name in enumerate(names):
        cell = Image.new("RGBA", (3, 3), block)
        cell.alpha_composite(images[f"GameResources/ui/citymap/{name}.png"])
        sheet.alpha_composite(cell.resize((3 * scale, 3 * scale), Image.NEAREST), (scale + i * 4 * scale, scale))
    sheet.save(os.path.join(HERE, "citymap_preview.png"))


if __name__ == "__main__":
    images = build()
    if "--preview" in sys.argv:
        preview(images)
    if "--write" in sys.argv:
        for path, img in images.items():
            full = os.path.join(ROOT, path)
            os.makedirs(os.path.dirname(full), exist_ok=True)
            img.save(full)
    print("ok", len(images))
