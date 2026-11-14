# -*- coding: utf-8 -*-
"""Pack the terminal’s full-body pixel portraits and wardrobe catalogue (Pillow).

portrait_art.png and portrait_art.json contain the artwork registered to the
shared front-facing body grid. See PORTRAIT_STANDARD.md for the layer contract.
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CELL = (88, 136)
COLUMNS = 10
THUMB = (22, 0, 44, 55)
ORDER = ("back", "base", "head", "body", "mark", "beard", "hair", "hat")

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
    ("any", ("hide", "兽皮衣", "commoner", "any", 0, 0, 0, "none")),
    ("hx", ("zhaiyi", "深青翟衣", "royal", "f", 2, 5, 4, "fengguan")),
    ("hx", ("ming_royal", "明黄团龙袍", "royal", "m", 3, 4, 4, "yishan")),
    ("hx", ("duangua", "对襟短褂", "commoner", "any", 4, 6, 5, "none")),
    ("west", ("houppelande", "毛缘学士长袍", "court", "any", 3, 4, 3, "none")),
]
ROYAL_HATS = (
    ("yishan", "乌纱翼善冠", "hx", "royal", 3, 4, "m"),
    ("fengguan", "凤冠", "hx", "royal", 2, 5, "f"),
)
# "all" also hides the rear hair. Other hats cover only the top of the front hair.
HAT_CLIP = {
    "mianguan": 18, "futou": 20, "wushamao": 20, "qing_hat": 20,
    "peaked": 20, "fur_hat": 20, "steppe_helm": "all",
    "knight_helm": "all", "turban": "all", "high_turban": "all",
    "keffiyeh": "all", "eboshi": 20, "kabuto": "all", "yishan": 20,
}
PALETTES = {
    "skin": [[227, 182, 156], [210, 155, 122], [176, 123, 94], [139, 91, 67]],
    "hair": [
        [0, 0, 0], [17, 6, -4], [33, 12, -8], [75, 51, 13],
        [38, 17, -6], [32, 33, 33], [77, 72, 63],
    ],
    "eye": [[113, 76, 55], [143, 103, 60], [86, 123, 89], [71, 114, 157], [120, 103, 150]],
}
DEFAULT_HAIR = {
    "hx": ["topknot", "high_bun", "loose_bun"],
    "nomad": ["steppe_braid", "braided_bun", "braided_bun"],
    "west": ["wavy", "long_waves", "long_waves"],
    "arab": ["curls", "wrapped_braid", "wrapped_braid"],
    "jp": ["mage", "taregami", "taregami"],
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


def draw_marks(layers):
    for name, points, color in (
        ("scar", [(47, 28), (48, 26), (48, 25)], "#ac6960"),
        ("mole", [(46, 27)], "#734740"),
        ("rouge", [(40, 27), (41, 27), (46, 27), (47, 27)], "#d08378"),
        ("stubble", [(43, 30), (44, 31), (45, 30)], "#977066"),
    ):
        tile = layers["mark:" + name] = Image.new("RGBA", CELL)
        draw = ImageDraw.Draw(tile)
        if name == "scar":
            draw.line(points, color)
        else:
            draw.point(points, color)


def main():
    width, height = CELL
    art = Image.open(HERE / "portrait_art.png").convert("RGBA")
    source = json.loads((HERE / "portrait_art.json").read_text(encoding="utf-8"))
    if art.width != COLUMNS * width or art.height % height != 0:
        raise ValueError("portrait_art.png does not match the portrait cell grid")
    if len(source) != len(set(source.values())):
        raise ValueError("Two parts are assigned to the same source tile")

    layers = {}
    wardrobe = {slot: {} for slot in ("body", "hat", "hair")}
    presets = []
    skin_bases = {}

    def raster(key):
        index = source[key]
        x, y = index % COLUMNS * width, index // COLUMNS * height
        if not 0 <= y <= art.height - height:
            raise ValueError(f"Tile {key} is outside the source atlas")
        tile = art.crop((x, y, x + width, y + height))
        if not tile.getbbox():
            raise ValueError(f"Empty tile for {key}")
        layers[key] = tile
        skin = sorted((r, g, b) for r, g, b, alpha in tile.getdata() if alpha == 254)
        if skin:
            skin_bases[key] = skin[len(skin) // 2]

    for sex in ("m", "f"):
        raster(f"base:{sex}")
    for name in HEADS:
        raster(f"head:{name}")
    for name, (label, civ, sex) in HAIRSTYLES.items():
        if name != "bald":
            raster(f"hair:{name}")
        if f"back:{name}" in source:
            raster(f"back:{name}")
        wardrobe["hair"][name] = garment(label, civ, "casual", sex)
    for name in BEARDS:
        raster(f"beard:{name}")
    draw_marks(layers)
    for name, label, civ, kind, first, last, sex in [(*hat, "any") for hat in HATS] + list(ROYAL_HATS):
        raster(f"hat:{name}")
        wardrobe["hat"][name] = garment(label, civ, kind, sex, first, last)

    costumes = []
    for i, (civ, items) in enumerate(COSTUMES.items()):
        costumes.extend((civ, item) for item in [*items, WOMEN[i], COMMON[i]])
    for civ, (name, label, kind, sex, first, last, period, hat) in [*costumes, *EXTRA]:
        sexes = ("m", "f") if sex == "any" else (sex,)
        wardrobe["body"][name] = {**garment(label, civ, kind, sex, first, last), "pref": period, "hat": hat}
        for fit in sexes:
            raster(f"body:{name}:{fit}")
            male = fit == "m"
            faces = FACES.get(civ, FACES["hx"])
            hair = DEFAULT_HAIR.get(civ, DEFAULT_HAIR["hx"])
            roles = {
                "royal": ["emperor" if male else "empress"],
                "court": ["minister", "heir"],
                "armor": ["general", "old_general"],
                "lady": ["woman", "empress"],
                "commoner": ["commoner" if male else "woman"],
                "clergy": ["monk"],
            }[kind]
            for role in roles:
                old = role == "old_general"
                head = faces[2] if male and old else faces[0 if male else 1][0]
                hat_item = wardrobe["hat"].get(hat, {})
                presets.append(dict(
                    id=f"{name}_{fit}_{role}", civ=civ, period=f"p{period}", role=role,
                    sex=fit, body=name, head=head,
                    hat=hat if hat_item.get("sex") in ("any", fit) else "none",
                    hair="bald" if role == "monk" else "short" if male and period >= 5 else hair[0 if male else 1],
                    beard="goatee" if male and old else "none", mark="none",
                    skin=1, hairColor=5 if old else 0, eye=0,
                ))

    atlas = Image.new("RGBA", (COLUMNS * width, ((len(layers) + COLUMNS - 1) // COLUMNS) * height))
    tiles = {}
    for i, (key, tile) in enumerate(layers.items()):
        tiles[key] = i
        atlas.paste(tile, (i % COLUMNS * width, i // COLUMNS * height))
    atlas.save(ROOT / "Terminal" / "portraits.png", optimize=True)
    parts = dict(cell=CELL, thumb=THUMB, columns=COLUMNS, tiles=tiles, order=ORDER, faces=FACES,
                 palettes=PALETTES, skinBases=skin_bases, hairStyle=DEFAULT_HAIR, clip=HAT_CLIP,
                 wardrobe=wardrobe, presets=presets)
    (HERE / "portrait_parts.json").write_text(json.dumps(parts, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
