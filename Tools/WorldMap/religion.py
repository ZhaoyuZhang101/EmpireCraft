"""世界地图各时代的宗教：生成 WorldMaps/religions.json(开局按它立教、定圣地、各城信仰，见 WorldMapReligion)。

  religions：各宗教 名(按年换名) 类型(FaithType) 天下(Sphere) 母宗教(分出的教会) 圣地(按年，城不在图上或不信本教时取本教最大的城)；
  regions：各地区七个时代的宗教(年份同 YEARS)，按当时多数人的信仰(伊斯兰早期的西亚、北非按统治者)；
  cities：个别城另算(十字军据点、葡属荷属港口、天竺诸苏丹国的都城……)。
开局年不在 YEARS 上的取前一个时代。

用法：python -I -X utf8 religion.py <模组目录>
"""
import json
import os
import sys

from add_traditional import tw

YEARS = [-260, 117, 230, 750, 800, 1190, 1628]
EARLY = -9999


def R(id, names, faith, sphere, holy, parent="", caesaro=False, split=""):
    return {"id": id, "n": [[year, name + "|" + tw(name.split("|")[0])] for year, name in names], "faith": faith,
            "sphere": sphere, "holy": holy, "parent": parent, "caesaro": caesaro, "split": split}


RELIGIONS = [
    R("huaxia", [(EARLY, "周礼|Rites of Zhou"), (-200, "儒教|Confucianism"), (600, "三教|Three Teachings"),
                 (1368, "儒教|Confucianism")], "Sanjiao", "Huaxia", [[EARLY, "qufu"]]),
    R("hellenic", [(EARLY, "希腊罗马诸神|Graeco-Roman Gods")], "Folk", "Roma", [[EARLY, "athens"], [-30, "rome"]]),
    R("celtic", [(EARLY, "德鲁伊教|Druidism")], "Folk", "Roma", [[EARLY, "orleans"], [0, "tara"]]),
    R("germanic", [(EARLY, "日耳曼诸神|Germanic Paganism")], "Folk", "Roma", [[EARLY, "uppsala"]]),
    R("slavic", [(EARLY, "斯拉夫诸神|Slavic Paganism")], "Folk", "Roma", [[EARLY, "kiev"]]),
    R("tengri", [(EARLY, "腾格里教|Tengrism")], "Tengri", "Steppe", [[EARLY, "onon"]]),
    R("shamanism", [(EARLY, "萨满教|Shamanism")], "Tengri", "Steppe", [[EARLY, "fuyu"]], parent="tengri"),
    R("bon", [(EARLY, "苯教|Bon")], "Folk", "Steppe", [[EARLY, "qonggyai"]]),
    R("manichaean", [(EARLY, "摩尼教|Manichaeism")], "Folk", "Steppe", [[EARLY, "ordu"]]),
    R("zoroastrian", [(EARLY, "琐罗亚斯德教|Zoroastrianism")], "Folk", "Umma", [[EARLY, "fars"]]),
    R("arab_pagan", [(EARLY, "阿拉伯诸神|Arabian Polytheism")], "Folk", "Umma", [[EARLY, "mecca"]]),
    R("egyptian", [(EARLY, "埃及诸神|Egyptian Religion")], "Folk", "Umma", [[EARLY, "thebes"]]),
    R("punic", [(EARLY, "迦太基诸神|Punic Religion")], "Folk", "Umma", [[EARLY, "carthage"]]),
    R("african", [(EARLY, "非洲传统信仰|African Traditional Religion")], "Folk", "Umma", [[EARLY, "djenne"]]),
    R("islam", [(EARLY, "伊斯兰教|Islam")], "Islam", "Umma", [[EARLY, "mecca"]]),
    R("shia", [(EARLY, "什叶派|Shia Islam")], "Islam", "Umma", [[EARLY, "kufa"]], parent="islam", split="shia"),
    R("judaism", [(EARLY, "犹太教|Judaism")], "Judaism", "Kosher", [[EARLY, "jerusalem"]]),
    R("catholic", [(EARLY, "大公教会|Catholic Church"), (1054, "天主教会|Roman Catholic Church")], "Church", "Roma",
      [[EARLY, "rome"]]),
    R("orthodox", [(EARLY, "东正教会|Eastern Orthodox Church")], "Church", "Roma", [[EARLY, "byzantium"]],
      parent="catholic", caesaro=True, split="eastern"),
    R("armenian", [(EARLY, "亚美尼亚使徒教会|Armenian Apostolic Church")], "Church", "Roma", [[EARLY, "artaxata"]],
      parent="catholic"),
    R("coptic", [(EARLY, "科普特教会|Coptic Church")], "Church", "Roma", [[EARLY, "alexandria"]], parent="catholic"),
    R("lutheran", [(EARLY, "路德宗|Lutheran Church")], "Church", "Roma", [[EARLY, "magdeburg"]], parent="catholic"),
    R("anglican", [(EARLY, "英格兰国教会|Church of England")], "Church", "Roma", [[EARLY, "london"]], parent="catholic"),
    R("reformed", [(EARLY, "归正宗|Reformed Church")], "Church", "Roma", [[EARLY, "amsterdam"]], parent="catholic"),
    R("hindu", [(EARLY, "婆罗门教|Brahmanism"), (500, "印度教|Hinduism")], "Dharmic", "India", [[EARLY, "varanasi"]]),
    R("buddhism", [(EARLY, "佛教|Buddhism")], "Dharmic", "India", [[EARLY, "pataliputra"], [1300, "lhasa"]],
      parent="hindu"),
    R("animism", [(EARLY, "万物有灵|Animism")], "Folk", "Nanyang", []),
    R("shinto", [(EARLY, "神道|Shinto")], "Shinto", "Tenka", [[EARLY, "izumo"]]),
]

# 地区：-260 117 230 750 800 1190 1628
H7 = ["huaxia"] * 7
REGIONS = {
    "guanzhong": H7, "longxi": H7, "hanzhong": H7, "bashu": H7, "hedong": H7, "henan": H7, "hebei": H7, "youyan": H7,
    "qilu": H7, "huaisi": H7, "jingchu": H7, "jiangdong": H7, "jiangxi": H7,
    "hexi": ["tengri", "huaxia", "huaxia", "huaxia", "buddhism", "buddhism", "huaxia"],
    "nanzhong": ["animism", "animism", "animism", "animism", "buddhism", "buddhism", "huaxia"],
    "liaodong": ["huaxia", "huaxia", "huaxia", "huaxia", "buddhism", "huaxia", "huaxia"],
    "minyue": ["animism"] + ["huaxia"] * 6,
    "lingnan": ["animism"] + ["huaxia"] * 6,
    "jiaozhi": ["animism", "huaxia", "huaxia", "huaxia", "huaxia", "buddhism", "huaxia"],
    "hetao": ["tengri", "huaxia", "tengri", "huaxia", "huaxia", "buddhism", "buddhism"],
    "korea_n": ["shamanism"] * 3 + ["buddhism"] * 3 + ["huaxia"],
    "korea_s": ["shamanism"] * 3 + ["buddhism"] * 3 + ["huaxia"],
    "japan_kyushu": ["shinto"] * 7, "japan_kinai": ["shinto"] * 7, "japan_kanto": ["shinto"] * 7,
    "japan_tohoku": ["animism"] * 4 + ["shinto"] * 3,
    "ryukyu": ["animism"] * 7, "taiwan": ["animism"] * 7,
    "monan": ["tengri"] * 5 + ["huaxia", "buddhism"],
    "mobei": ["tengri"] * 4 + ["manichaean", "tengri", "buddhism"],
    "manchuria": ["shamanism"] * 7,
    "xiyu": ["tengri"] + ["buddhism"] * 5 + ["islam"],
    "dzungaria": ["tengri"] * 5 + ["buddhism", "buddhism"],
    "kazakh": ["tengri"] * 5 + ["islam", "islam"],
    "siberia": ["shamanism"] * 7,
    "tibet": ["bon"] * 4 + ["buddhism"] * 3,
    "qinghai": ["bon"] * 4 + ["buddhism"] * 3,
    "sogdiana": ["zoroastrian"] * 4 + ["islam"] * 3,
    "ferghana": ["zoroastrian"] * 5 + ["islam"] * 2,
    "khwarazm": ["zoroastrian"] * 4 + ["islam"] * 3,
    "bactria": ["zoroastrian", "buddhism", "buddhism", "buddhism", "islam", "islam", "islam"],
    "khorasan": ["zoroastrian"] * 3 + ["islam"] * 3 + ["shia"],
    "arachosia": ["zoroastrian", "buddhism", "buddhism", "hindu", "hindu", "islam", "islam"],
    "persis": ["zoroastrian"] * 5 + ["islam", "shia"],
    "media": ["zoroastrian"] * 3 + ["islam"] * 3 + ["shia"],
    "mesopotamia": ["zoroastrian"] * 3 + ["islam"] * 4,
    "arabia": ["arab_pagan"] * 3 + ["islam"] * 4,
    "syria": ["hellenic"] * 3 + ["islam"] * 4,
    "palestine": ["judaism"] * 3 + ["islam"] * 4,
    "cyprus": ["hellenic"] * 3 + ["catholic", "catholic", "orthodox", "orthodox"],
    "armenia": ["zoroastrian"] * 3 + ["armenian"] * 4,
    "caucasus": ["zoroastrian"] * 3 + ["catholic", "catholic", "orthodox", "orthodox"],
    "asia_minor": ["hellenic"] * 3 + ["catholic", "catholic", "orthodox", "islam"],
    "cappadocia": ["hellenic"] * 3 + ["catholic", "catholic", "islam", "islam"],
    "egypt": ["egyptian"] * 3 + ["coptic", "coptic", "islam", "islam"],
    "nubia": ["egyptian"] * 3 + ["coptic"] * 3 + ["islam"],
    "aksum": ["african"] * 3 + ["coptic"] * 4,
    "cyrenaica": ["hellenic"] * 3 + ["islam"] * 4,
    "africa": ["punic", "hellenic", "hellenic"] + ["islam"] * 4,
    "numidia": ["punic", "hellenic", "hellenic"] + ["islam"] * 4,
    "mauretania": ["punic", "hellenic", "hellenic"] + ["islam"] * 4,
    "sahel": ["african"] * 6 + ["islam"],
    "swahili": ["african"] * 5 + ["islam", "islam"],
    "italy_n": ["hellenic"] * 3 + ["catholic"] * 4,
    "italy_c": ["hellenic"] * 3 + ["catholic"] * 4,
    "italy_s": ["hellenic"] * 3 + ["catholic"] * 4,
    "sicily": ["hellenic"] * 3 + ["catholic"] * 4,
    "sardinia": ["punic", "hellenic", "hellenic"] + ["catholic"] * 4,
    "greece": ["hellenic"] * 3 + ["catholic", "catholic", "orthodox", "orthodox"],
    "macedonia": ["hellenic"] * 3 + ["catholic", "catholic", "orthodox", "orthodox"],
    "thrace": ["hellenic"] * 3 + ["catholic", "catholic", "orthodox", "islam"],
    "moesia": ["hellenic"] * 3 + ["tengri", "tengri", "orthodox", "orthodox"],
    "dacia": ["hellenic"] * 3 + ["slavic", "slavic", "orthodox", "orthodox"],
    "illyria": ["hellenic"] * 3 + ["catholic"] * 4,
    "pannonia": ["celtic", "hellenic", "hellenic", "tengri", "tengri", "catholic", "catholic"],
    "bavaria": ["celtic", "hellenic", "hellenic"] + ["catholic"] * 4,
    "rhine": ["celtic", "hellenic", "hellenic"] + ["catholic"] * 4,
    "germania": ["germanic"] * 4 + ["catholic", "catholic", "lutheran"],
    "bohemia": ["celtic", "germanic", "germanic", "slavic", "slavic", "catholic", "catholic"],
    "poland": ["germanic"] * 3 + ["slavic", "slavic", "catholic", "catholic"],
    "gaul_n": ["celtic", "hellenic", "hellenic"] + ["catholic"] * 4,
    "gaul_s": ["celtic", "hellenic", "hellenic"] + ["catholic"] * 4,
    "hispania_n": ["celtic", "hellenic", "hellenic"] + ["catholic"] * 4,
    "hispania_s": ["punic", "hellenic", "hellenic", "islam", "islam", "islam", "catholic"],
    "britain_s": ["celtic", "hellenic", "hellenic", "catholic", "catholic", "catholic", "anglican"],
    "britain_n": ["celtic"] * 3 + ["catholic", "catholic", "catholic", "reformed"],
    "ireland": ["celtic"] * 3 + ["catholic"] * 4,
    "denmark": ["germanic"] * 5 + ["catholic", "lutheran"],
    "norway": ["germanic"] * 5 + ["catholic", "lutheran"],
    "sweden": ["germanic"] * 5 + ["catholic", "lutheran"],
    "rus_n": ["slavic"] * 5 + ["orthodox"] * 2,
    "rus_c": ["slavic"] * 5 + ["orthodox"] * 2,
    "rus_s": ["tengri"] * 3 + ["slavic", "slavic", "orthodox", "orthodox"],
    "pontic": ["tengri"] * 6 + ["islam"],
    "crimea": ["hellenic"] * 3 + ["catholic", "catholic", "orthodox", "islam"],
    "volga": ["tengri"] * 3 + ["judaism", "judaism", "islam", "islam"],
    "gandhara": ["hindu", "buddhism", "buddhism", "hindu", "hindu", "islam", "islam"],
    "punjab": ["hindu"] * 6 + ["islam"],
    "sindh": ["hindu", "buddhism", "buddhism"] + ["islam"] * 4,
    "hindustan": ["hindu"] * 7,
    "magadha": ["buddhism", "buddhism", "hindu", "buddhism", "buddhism", "buddhism", "hindu"],
    "bengal": ["hindu"] * 3 + ["buddhism", "buddhism", "hindu", "hindu"],
    "gujarat": ["hindu"] * 7, "malwa": ["hindu"] * 7, "deccan": ["hindu"] * 7, "tamil": ["hindu"] * 7,
    "kalinga": ["hindu", "buddhism", "buddhism"] + ["hindu"] * 4,
    "lanka": ["buddhism"] * 7,
    "burma": ["animism"] + ["buddhism"] * 6,
    "khmer": ["animism"] + ["hindu"] * 4 + ["buddhism", "buddhism"],
    "champa": ["animism"] + ["hindu"] * 5 + ["huaxia"],
    "dvaravati": ["animism"] + ["buddhism"] * 6,
    "malaya": ["animism", "hindu", "hindu", "buddhism", "buddhism", "buddhism", "islam"],
    "sumatra": ["animism", "animism", "hindu", "buddhism", "buddhism", "buddhism", "islam"],
    "java": ["animism", "animism", "hindu", "hindu", "buddhism", "hindu", "islam"],
    "luzon": ["animism"] * 6 + ["catholic"],
}

# 个别城另算：城 → {时代年: 宗教}
ALL = YEARS
CITIES = {
    "massilia": {-260: "hellenic"},
    "alexandria": {-260: "hellenic", 117: "hellenic", 230: "hellenic"},
    "olbia": {-260: "hellenic", 117: "hellenic", 230: "hellenic"},
    "tanais": {-260: "hellenic", 117: "hellenic", 230: "hellenic"},
    "lilybaeum": {-260: "punic"},
    "varanasi": {year: "hindu" for year in ALL},
    "kashgar": {1190: "islam"},
    "balasagun": {1190: "buddhism"},
    "antioch": {1190: "catholic"}, "acre": {1190: "catholic"}, "tyre": {1190: "catholic"}, "tripoli_lb": {1190: "catholic"},
    "lisbon": {1190: "catholic"}, "toledo": {1190: "catholic"},
    "trebizond": {1190: "orthodox"},
    "derbent": {750: "islam", 800: "islam", 1190: "islam", 1628: "islam"},
    "erzurum": {1628: "islam"},
    "philippopolis": {1628: "orthodox"},
    "lviv": {1628: "catholic"},
    "amsterdam": {1628: "reformed"}, "apulum": {1628: "reformed"},
    "batavia": {1628: "reformed"}, "tayouan": {1628: "reformed"}, "keelung": {1628: "catholic"},
    "colombo": {1628: "catholic"}, "goa": {1628: "catholic"}, "malacca": {1628: "catholic"}, "macau": {1628: "catholic"},
    "agra": {1628: "islam"}, "delhi": {1628: "islam"}, "dhaka": {1628: "islam"}, "ahmedabad": {1628: "islam"},
    "bijapur": {1628: "islam"}, "golconda": {1628: "islam"}, "ahmednagar": {1628: "islam"}, "devagiri": {1628: "islam"},
    "srinagar": {1628: "islam"},
    "champa": {1628: "hindu"},
}


def main():
    root = sys.argv[1]
    with open(os.path.join(root, "WorldMaps", "cities.json"), encoding="utf-8") as f:
        data = json.load(f)
    regions = [region["id"] for region in data["regions"]]
    cities = {city["id"] for city in data["cities"]}
    ids = {religion["id"] for religion in RELIGIONS}
    problems = [r for r in regions if r not in REGIONS] + [r for r in REGIONS if r not in regions]
    problems += ["%s 列数 %d" % (r, len(row)) for r, row in REGIONS.items() if len(row) != len(YEARS)]
    problems += ["%s 未知宗教 %s" % (r, x) for r, row in REGIONS.items() for x in row if x not in ids]
    problems += ["城 %s 不在表上" % c for c in CITIES if c not in cities]
    problems += ["%s 未知宗教 %s" % (c, x) for c, row in CITIES.items() for x in row.values() if x not in ids]
    problems += ["%s 圣地 %s 不在表上" % (r["id"], h[1]) for r in RELIGIONS for h in r["holy"] if h[1] not in cities]
    problems += ["%s 母宗教 %s 未知" % (r["id"], r["parent"]) for r in RELIGIONS if r["parent"] and r["parent"] not in ids]
    if problems:
        raise SystemExit("\n".join(problems))
    out = {"_note": "各时代宗教(由 Tools/WorldMap/religion.py 生成，勿手改)", "years": YEARS, "religions": RELIGIONS,
           "regions": REGIONS, "cities": {city: {str(year): rel for year, rel in row.items()} for city, row in CITIES.items()}}
    with open(os.path.join(root, "WorldMaps", "religions.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("ok", len(RELIGIONS), "religions")


if __name__ == "__main__":
    main()
