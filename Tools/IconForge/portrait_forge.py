# -*- coding: utf-8 -*-
"""Pack the terminal’s full-body pixel portraits and wardrobe catalogue (Pillow).

portrait_art.png contains the fitted pixel artwork for each interchangeable layer.
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CELL = (88, 136)
COLUMNS = 10
THUMB = (20, 1, 48, 60)
ORDER = ("body", "head", "mark", "beard", "hair", "hat")

# Source tile order, left to right. Periods are inclusive technology-era indices.
# id, display name, category, allowed sex, first period, last period, preferred period, headwear
COSTUMES = {
    "hx": [
        ("zhou", "玄衣纁裳", "royal", "any", 0, 2, 1, "mianguan"),
        ("tang", "绯色圆领袍", "court", "any", 2, 3, 3, "futou"),
        ("ming", "青罗鹤补服", "court", "any", 3, 4, 4, "wushamao"),
        ("qing", "石青龙袍", "royal", "any", 4, 5, 4, "qing_hat"),
        ("zhongshan", "中山装", "court", "any", 5, 6, 6, "peaked"),
    ],
    "nomad": [
        ("fur", "毛缘皮袍", "commoner", "any", 0, 2, 0, "fur_hat"),
        ("steppe_lamellar", "草原札甲", "armor", "any", 1, 4, 2, "steppe_helm"),
        ("deel", "松石色斜襟袍", "court", "any", 2, 5, 3, "fur_hat"),
        ("khan", "赤地绣纹大袍", "royal", "any", 2, 4, 3, "fur_hat"),
        ("steppe_court", "金绣礼袍", "court", "any", 4, 6, 5, "fur_hat"),
    ],
    "west": [
        ("toga", "元老托加袍", "court", "any", 1, 2, 2, "laurel"),
        ("byzantine", "紫衣珠饰礼袍", "royal", "any", 2, 3, 3, "crown"),
        ("surcoat", "纹章罩袍", "armor", "any", 2, 4, 3, "knight_helm"),
        ("doublet", "轮领礼服", "royal", "any", 4, 5, 4, "crown"),
        ("uniform", "绶饰礼制服", "court", "any", 5, 6, 5, "peaked"),
    ],
    "arab": [
        ("desert", "沙色披巾袍", "commoner", "any", 0, 2, 0, "turban"),
        ("caliphate", "象牙色织带长袍", "court", "any", 2, 3, 2, "turban"),
        ("abbasid", "黑地金绣礼袍", "royal", "any", 2, 4, 3, "high_turban"),
        ("mamluk", "马穆鲁克札甲", "armor", "any", 2, 4, 3, "high_turban"),
        ("bisht", "金边比什特披袍", "court", "any", 4, 6, 5, "keffiyeh"),
    ],
    "jp": [
        ("yayoi", "弥生麻衣", "commoner", "any", 0, 2, 1, "none"),
        ("heian", "平安菊纹束带", "royal", "any", 2, 3, 3, "eboshi"),
        ("samurai", "赤漆大铠", "armor", "any", 2, 4, 3, "kabuto"),
        ("kamishimo", "纹付裃", "court", "any", 3, 4, 4, "eboshi"),
        ("meiji", "明治金绣大礼服", "court", "any", 5, 6, 5, "peaked"),
    ],
}
WOMEN = [
    ("ruqun", "绯碧襦裙", "lady", "f", 1, 6, 3, "none"),
    ("steppe_lady", "松石色贵女袍", "lady", "f", 1, 6, 3, "none"),
    ("gown", "翡翠色织锦长裙", "lady", "f", 2, 6, 3, "none"),
    ("kaftan", "绛紫绣花长袍", "lady", "f", 1, 6, 3, "none"),
    ("kosode", "叠色花纹小袖", "lady", "f", 1, 6, 3, "none"),
]
COMMON = [
    ("hemp", "青灰麻布交领衣", "commoner", "any", 0, 6, 2, "none"),
    ("work_deel", "系带粗布袍", "commoner", "any", 0, 6, 3, "none"),
    ("jerkin", "赭色系绳短袍", "commoner", "any", 0, 6, 3, "none"),
    ("work_robe", "砂青布袍", "commoner", "any", 0, 6, 3, "turban"),
    ("work_kimono", "靛蓝条纹小袖", "commoner", "any", 0, 6, 3, "none"),
]
# id, display name, culture, category, first period, last period
HATS = [
    ("mianguan", "冕冠", "hx", "royal", 0, 3),
    ("futou", "软脚幞头", "hx", "court", 2, 3),
    ("wushamao", "乌纱帽", "hx", "court", 3, 4),
    ("qing_hat", "红缨凉帽", "hx", "court", 4, 5),
    ("peaked", "大檐礼帽", "any", "court", 5, 6),
    ("fur_hat", "毡边皮帽", "nomad", "casual", 0, 6),
    ("steppe_helm", "草原缨盔", "nomad", "armor", 1, 4),
    ("laurel", "金桂冠", "west", "court", 1, 2),
    ("crown", "宝石冠", "west", "royal", 2, 5),
    ("knight_helm", "开面钢盔", "west", "armor", 2, 4),
    ("turban", "素布缠头", "arab", "casual", 0, 6),
    ("high_turban", "金线高缠冠", "arab", "royal", 2, 4),
    ("keffiyeh", "黑箍方巾", "arab", "casual", 4, 6),
    ("eboshi", "乌帽子", "jp", "court", 2, 4),
    ("kabuto", "金月兜", "jp", "armor", 2, 4),
]
EXTRA = [
    ("hx", ("qin_armor", "玄革朱缀甲", "armor", "any", 0, 2, 2, "none")),
    ("hx", ("tang_armor", "虎首明光甲", "armor", "any", 3, 3, 3, "none")),
    ("hx", ("ming_armor", "青布铜钉甲", "armor", "any", 4, 4, 4, "none")),
    ("hx", ("field_jacket", "橄榄色军常服", "armor", "any", 5, 6, 6, "peaked")),
    ("west", ("bronze_armor", "铜扣皮甲", "armor", "any", 0, 2, 1, "none")),
    ("jp", ("tanko", "古坟短甲", "armor", "any", 0, 2, 2, "none")),
    ("arab", ("desert_mail", "沙巾环锁甲", "armor", "any", 0, 2, 2, "turban")),
    ("hx", ("kasaya", "赭色袈裟", "clergy", "any", 2, 6, 3, "none")),
    ("hx", ("qipao", "靛蓝绣花旗袍", "lady", "f", 5, 6, 6, "none")),
    ("west", ("late_dress", "绛紫蕾丝立领裙", "lady", "f", 5, 6, 5, "none")),
    ("jp", ("furisode", "藤色花纹振袖", "lady", "f", 5, 6, 6, "none")),
    ("nomad", ("velvet_coat", "绛红丝绒绣边袍", "lady", "f", 5, 6, 6, "none")),
]
PALETTES = {
    "skin": [-12, 0, 8, 16],
    "hair": [
        [0, 0, 0], [17, 6, -4], [33, 12, -8], [75, 51, 13],
        [38, 17, -6], [32, 33, 33], [77, 72, 63],
    ],
    "eye": [[113, 76, 55], [143, 103, 60], [86, 123, 89], [71, 114, 157], [120, 103, 150]],
}
HEADS = [
    "eastern", "steppe", "western", "arabian", "eastern_woman", "steppe_woman",
    "western_woman", "arabian_woman", "elder_eastern", "elder_western", "eastern_lady", "japanese",
]
FACES = {
    # culture: male variations, female variations, older man
    "hx": [["eastern", "japanese"], ["eastern_woman", "eastern_lady"], "elder_eastern"],
    "nomad": [["steppe", "eastern"], ["steppe_woman", "eastern_lady"], "elder_eastern"],
    "west": [["western", "elder_western"], ["western_woman"], "elder_western"],
    "arab": [["arabian", "western"], ["arabian_woman"], "arabian"],
    "jp": [["japanese", "eastern"], ["eastern_lady", "eastern_woman"], "elder_eastern"],
}
HAIRSTYLES = {
    "topknot": ("束发", "hx", "m"),
    "steppe_braid": ("草原双辫", "nomad", "any"),
    "wavy": ("侧分卷发", "west", "any"),
    "curls": ("短鬈发", "arab", "any"),
    "mage": ("髷", "jp", "m"),
    "high_bun": ("高髻", "hx", "f"),
    "braided_bun": ("盘辫", "nomad", "f"),
    "long_waves": ("披肩卷发", "west", "f"),
    "wrapped_braid": ("低盘发", "arab", "f"),
    "taregami": ("垂发", "jp", "f"),
    "short": ("利落短发", "any", "any"),
    "loose_bun": ("松挽侧髻", "any", "f"),
    "bald": ("剃发", "any", "any"),
}
BEARDS = ("moustache", "goatee", "long", "boxed")


def garment(name, civ, kind, sex, first=0, last=6):
    return dict(name=name, civ=civ, kind=kind, sex=sex, period=[f"p{i}" for i in range(first, last + 1)])


def draw_marks(add):
    add("mark", "scar").line([(43, 28), (46, 25), (48, 24)], "#ab695f")
    add("mark", "mole").point((46, 28), "#7d4747")
    add("mark", "rouge").line([(41, 27), (44, 28), (46, 27)], "#d5807c")
    add("mark", "stubble").point([(38, 30), (40, 32), (42, 32), (45, 30)], "#92695f")


def main():
    art = Image.open(HERE / "portrait_art.png").convert("RGBA")
    layers = {layer: {"none": Image.new("RGBA", CELL)} for layer in ORDER}
    wardrobe = {slot: {} for slot in ("body", "hat", "hair")}
    presets = []

    def add(layer, name):
        tile = layers[layer][name] = Image.new("RGBA", CELL)
        return ImageDraw.Draw(tile)

    def raster(layer, name, index):
        x, y = index % COLUMNS * CELL[0], index // COLUMNS * CELL[1]
        tile = art.crop((x, y, x + CELL[0], y + CELL[1]))
        if not tile.getbbox():
            raise ValueError(f"Empty art tile {index} for {name}")
        layers[layer][name] = tile

    for index, name in enumerate(HEADS, 62):
        raster("head", name, index)
    for index, (name, (label, civ, sex)) in enumerate(HAIRSTYLES.items(), 74):
        if name != "bald":
            raster("hair", name, index)
        else:
            add("hair", name)
        wardrobe["hair"][name] = garment(label, civ, "casual", sex)
    for index, name in enumerate(BEARDS, 86):
        raster("beard", name, index)
    draw_marks(add)
    for index, (name, label, civ, kind, first, last) in enumerate(HATS, 35):
        raster("hat", name, index)
        wardrobe["hat"][name] = garment(label, civ, kind, "any", first, last)

    for civ_index, (civ, costumes) in enumerate(COSTUMES.items()):
        male_hair = ("topknot", "steppe_braid", "wavy", "curls", "mage")[civ_index]
        female_hair = ("high_bun", "braided_bun", "long_waves", "wrapped_braid", "taregami")[civ_index]
        tiles = [(civ_index * 5 + i, item) for i, item in enumerate(costumes)]
        tiles.extend([(25 + civ_index, WOMEN[civ_index]), (30 + civ_index, COMMON[civ_index])])
        tiles.extend((i, item) for i, (region, item) in enumerate(EXTRA, 50) if region == civ)
        for index, item in tiles:
            name, label, kind, sex, first, last, period, hat = item
            raster("body", name, index)
            wardrobe["body"][name] = garment(label, civ, kind, sex, first, last)
            roles = {
                "royal": ("emperor", "empress"),
                "court": ("minister", "elder_minister", "clerk", "heir"),
                "armor": ("general", "old_general"),
                "lady": ("woman", "empress"),
                "commoner": ("commoner", "scholar", "woman"),
                "clergy": ("monk",),
            }[kind]
            if period >= 5 and kind == "court":
                roles = ("emperor", "empress", *roles, "general", "old_general")
            for role in roles:
                female = role in ("woman", "empress")
                old = role.startswith("old_") or role.startswith("elder_")
                hair = "bald" if role == "monk" else female_hair if female else "short" if period >= 5 else male_hair
                male_faces, female_faces, elder = FACES[civ]
                faces = female_faces if female else male_faces
                head = elder if old else faces[(index + len(role)) % len(faces)]
                beard = "none"
                if not female and role not in ("heir", "monk"):
                    beard = "goatee" if old else "moustache" if role == "emperor" and period < 5 else "none"
                presets.append(dict(
                    id=f"{name}_{role}", civ=civ, period=f"p{period}", role=role, body=name,
                    head=head, hair=hair, hat="none" if role == "woman" else hat,
                    beard=beard, mark="none", skin=(1, 2, 0, 2, 1)[civ_index],
                    hairColor=5 if old else 1 if civ == "west" else 0, eye=3 if civ == "west" else 0,
                ))

    offsets = {}
    width, height = CELL
    count = sum(len(tiles) - 1 for tiles in layers.values())
    atlas = Image.new("RGBA", (COLUMNS * width, ((count + COLUMNS - 1) // COLUMNS) * height))
    index = 0
    for layer, tiles in layers.items():
        offsets[layer] = index
        for name, tile in tiles.items():
            if name == "none":
                continue
            atlas.paste(tile, (index % COLUMNS * width, index // COLUMNS * height))
            index += 1
    atlas.save(ROOT / "Terminal" / "portraits.png", optimize=True)
    parts = dict(cell=CELL, thumb=THUMB, columns=COLUMNS, offsets=offsets, order=ORDER,
                 rows={name: list(tiles) for name, tiles in layers.items()}, faces=FACES,
                 palettes=PALETTES, wardrobe=wardrobe, presets=presets)
    (HERE / "portrait_parts.json").write_text(json.dumps(parts, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
