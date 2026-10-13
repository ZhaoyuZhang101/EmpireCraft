"""纯净地图城市平面图的建筑图块(见 Scripts/GamePatches/PureMapPatch.cs 的"城市平面图")。

纯净地图模式下建筑不画，地图色块里按每座城的建筑数据生成一张平面图：街格(白线)切出的每个街区放一座建筑的
3×3 像素图块。不依赖原版或兼容模组的建筑贴图，全部在这里画，按时代分代：
  住宅 house_1..8：茅屋、瓦房、院落、楼阁、坊市、近代楼房、摩天楼、生态城(对应科技树的八项住房技术)；
  工业 industry_0..2：作坊、蒸汽工厂、现代工厂；商业 market_0..1：集市、商场；
  官署 civic_0..1：衙署、现代政府大楼；寺庙 temple、兵营 barracks、码头 docks；
  殿宇 palace_hall：华夏都城的宫城里沿中轴对称摆满，配朱红宫墙、白石广场拼出紫禁城的格局(格局在 PureMapPatch 里排)；
  废墟 ruin(瓦砾)、burning(焦土火光)；工地 construction(改建城池拆旧建新)；
  各文化系的风味建筑 <系>_house_a/b/c、temple、civic、landmark(western 西欧、roma 罗马、viking 维京、arab 阿拉伯、
  persia 波斯、egypt 埃及、india 印度、aztec 阿兹特克、japan 日本、nomad 游牧蒙古包、woodland 林地棚屋；华夏用通用图块)。
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
    "E": (220, 90, 40),     # 火光
    "d": (150, 140, 128),   # 瓦砾
    "H": (110, 80, 50),     # 木筋
    "h": (236, 224, 196),   # 灰泥
    "r": (190, 90, 60),     # 赤陶瓦
    "c": (180, 176, 168),   # 石板
    "m": (200, 160, 100),   # 土坯
    "p": (220, 120, 150),   # 彩绘
    "u": (238, 230, 210),   # 白灰
    "q": (70, 150, 150),    # 青穹顶
    "v": (246, 244, 236),   # 毡
    "b": (120, 90, 60),     # 树皮
    "L": (200, 50, 40),     # 朱漆(鸟居)
    "k": (70, 55, 45),      # 深木
    "z": (60, 110, 180),    # 釉蓝
    "n": (165, 160, 150),   # 石
    "x": (196, 160, 90),    # 脚手架
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
    "palace_hall": ["TTT", "RTR", "WWW"],  # 殿宇(金瓦、朱柱、白石台基)：宫城里按中轴对称摆满，拼出紫禁城的格局
    "ruin": ["D.d", "dDd", "d.D"],        # 瓦砾(屠城、战乱后的废墟)
    "burning": ["E.D", "DEd", "d.E"],     # 焦土火光(新近屠城或正在交战)
    "construction": ["x.x", "xdx", "x.x"],  # 工地(改建城池时拆旧建新)
    # —— 各文化系的风味建筑(<系>_house_a/b/c 古代民居低中高、temple、civic、landmark 都城地标的拼块)；华夏用上面的通用图块 ——
    "western_house_a": ["rrr", "HhH", "hDh"],   # 木筋墙红瓦
    "western_house_b": ["rrr", "hHh", "HDH"],
    "western_house_c": ["rrr", "rhr", "hDh"],   # 联排
    "western_temple": [".n.", "nnn", "nDn"],    # 尖塔教堂
    "western_civic": ["nrn", "nhn", "nDn"],     # 市政厅
    "western_landmark": ["n.n", "nnn", "nDn"],  # 城堡主楼(雉堞)
    "roma_house_a": ["rrr", "r.r", "rrr"],      # 中庭宅院
    "roma_house_b": ["rrr", "hhh", "hDh"],      # 公寓楼
    "roma_house_c": ["rhr", "h.h", "rhr"],      # 别墅
    "roma_temple": ["rrr", "h.h", "h.h"],       # 柱廊神殿
    "roma_civic": ["rrr", "hhh", "h.h"],        # 会堂
    "roma_landmark": ["h.h", "...", "h.h"],     # 广场柱廊
    "viking_house_a": ["kkk", "kbk", "kkk"],    # 长屋
    "viking_house_b": ["bkb", "kkk", "bkb"],
    "viking_temple": [".k.", "kkk", "kDk"],     # 木板教堂
    "viking_civic": ["kTk", "kkk", "kDk"],
    "viking_landmark": ["kkk", "kTk", "kDk"],   # 大殿
    "arab_house_a": ["uuu", "u.u", "uuu"],      # 白色平顶院落
    "arab_house_b": ["uuu", "uDu", "uuu"],
    "arab_house_c": ["uqu", "uuu", "uDu"],
    "arab_temple": ["u.n", "qqn", "uDn"],       # 清真寺(穹顶与宣礼塔)
    "arab_civic": ["OuO", "uuu", "OuO"],        # 巴扎
    "arab_landmark": ["qnq", "n.n", "qnq"],     # 大清真寺(穹顶、院落)
    "persia_house_a": ["mmm", "mzm", "mDm"],    # 土坯釉砖
    "persia_house_b": ["mqm", "mmm", "mDm"],    # 穹顶屋
    "persia_temple": ["mEm", "mmm", "mDm"],     # 拜火庙
    "persia_civic": ["zzz", "mmm", "mDm"],
    "persia_landmark": ["n.n", ".n.", "n.n"],   # 百柱大殿
    "egypt_house_a": ["mmm", "m.m", "mDm"],     # 土坯平顶
    "egypt_house_b": ["mmm", "mmm", "mDm"],
    "egypt_temple": ["T.T", "mmm", "mDm"],      # 塔门与方尖碑
    "egypt_civic": ["mTm", "mmm", "mDm"],
    "india_house_a": ["ppp", "hhh", "hDh"],     # 彩绘民居
    "india_house_b": ["OOO", "php", "hDh"],
    "india_temple": [".O.", "OOO", "hDh"],      # 希卡拉塔庙
    "india_civic": ["pOp", "hhh", "hDh"],
    "india_landmark": [".O.", "OpO", "OpO"],    # 门塔
    "aztec_house_a": ["mmm", "mhm", "mDm"],     # 土坯屋
    "aztec_house_b": ["mhm", "mmm", "mDm"],
    "aztec_temple": [".L.", "mmm", "mmm"],      # 小金字塔
    "aztec_civic": ["mmm", "mLm", "mDm"],
    "japan_house_a": ["kkk", "hhh", "hDh"],     # 町屋
    "japan_house_b": ["kkk", "hkh", "hDh"],
    "japan_temple": ["LLL", "L.L", "L.L"],      # 鸟居
    "japan_civic": ["kkk", "LhL", "hDh"],
    "japan_landmark": ["kkk", "hkh", "nnn"],    # 天守
    "nomad_house_a": [".v.", "vkv", ".v."],     # 蒙古包
    "nomad_house_b": ["vvv", "vkv", "vvv"],
    "nomad_temple": [".n.", "nnn", ".n."],      # 敖包
    "nomad_civic": ["vTv", "TvT", "vTv"],
    "woodland_house_a": [".b.", "bkb", ".b."],  # 棚屋
    "woodland_house_b": ["bbb", "bkb", "bbb"],  # 长屋
    "woodland_temple": [".b.", "bbb", "b.b"],
    "woodland_civic": ["bbb", "bTb", "bbb"],
    "woodland_landmark": ["bbb", "bTb", "bDb"], # 议事长屋
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
    cols = 20
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * 4 * scale + scale, rows * 4 * scale + scale), (60, 90, 70, 255))
    block = (236, 232, 224, 255)
    for i, name in enumerate(names):
        cell = Image.new("RGBA", (3, 3), block)
        cell.alpha_composite(images[f"GameResources/ui/citymap/{name}.png"])
        sheet.alpha_composite(cell.resize((3 * scale, 3 * scale), Image.NEAREST),
                              (scale + i % cols * 4 * scale, scale + i // cols * 4 * scale))
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
