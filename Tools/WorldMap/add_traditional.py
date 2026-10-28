"""给 WorldMaps 下的地名、国名、人名、时代说明补繁体(ch 界面用；cz 为简体)。改了简体名之后重跑一遍。

用法：python -X utf8 add_traditional.py <模组目录>
需要 zhconv(pip install zhconv)。
  - 地区、城市、帝国法理、国家加 tw 字段，时代加 tw、dtw；
  - "简体|英文" 写法的字段(地区法理名 t、天下名、剧本人物姓 fam 名 giv、年号 era_name)补成 "简体|英文|繁体"；
  - 城市旧称写成 "到某年|简体|英文|繁体"。
"""
import json
import os
import sys

from zhconv import convert

# zhconv 按常用词转，地名有几处会转错：拜占庭、占城的"占"不是"佔"，咸阳不是"鹹陽"，里昂不是"裡昂"，东征不是"東徵"，德干、塔什干的"干"不是"幹"，托勒密不是"託"
FIXES = {"佔": "占", "幹": "干", "託": "托", "鹹陽": "咸陽", "裡": "里", "東徵": "東征", "徵服": "征服", "範陽": "范陽"}


def tw(text):
    text = convert(text, "zh-hant")
    for wrong, right in FIXES.items():
        text = text.replace(wrong, right)
    return text


def triple(text):
    """"简体|英文" → "简体|英文|繁体"(已有繁体的重算)；空串、没写英文的原样。"""
    if not text or "|" not in text:
        return text
    parts = text.split("|")
    return "|".join(parts[:2] + [tw(parts[0])])


def dump_records(f, key, records, last):
    f.write('  "%s": [\n' % key)
    for i, record in enumerate(records):
        f.write("    " + json.dumps(record, ensure_ascii=False) + ("," if i + 1 < len(records) else "") + "\n")
    f.write("  ]" + ("" if last else ",") + "\n")


def fix_cities(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    for region in data["regions"]:
        region["tw"] = tw(region["zh"])
        if region.get("t"):
            region["t"] = triple(region["t"])
    for city in data["cities"]:
        city["tw"] = tw(city["zh"])
        alts = []
        for entry in city.get("alt", []):
            parts = entry.split("|")
            alts.append("|".join(parts[:3] + [tw(parts[1])]))
        if alts:
            city["alt"] = alts
    for core in data.get("cores", []):
        core["tw"] = tw(core["zh"])
    data["tianxia"] = {key: triple(value) for key, value in data.get("tianxia", {}).items()}
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('  "_note": %s,\n' % json.dumps(data["_note"], ensure_ascii=False))
        f.write('  "tianxia": %s,\n' % json.dumps(data["tianxia"], ensure_ascii=False))
        dump_records(f, "regions", data["regions"], False)
        dump_records(f, "cores", data.get("cores", []), False)
        dump_records(f, "cities", data["cities"], True)
        f.write("}\n")


def fix_person(person):
    for key in ("fam", "giv"):
        if person.get(key):
            person[key] = triple(person[key])


def fix_era(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    data["tw"] = tw(data["zh"])
    data["dtw"] = tw(data["dzh"])
    for item in data.get("polities", []) + data.get("cores", []):
        item["tw"] = tw(item["zh"])
    for polity in data.get("polities", []):
        if polity.get("ruler"):
            fix_person(polity["ruler"])
        for person in polity.get("kin", []):
            fix_person(person)
        if polity.get("era_name"):
            polity["era_name"] = triple(polity["era_name"])
        for item in polity.get("divisions", []) + polity.get("colonies", []):
            item["name"] = triple(item["name"])
    head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        for key, value in head.items():
            f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
        dump_records(f, "cores", data.get("cores", []), False)
        dump_records(f, "polities", data.get("polities", []), True)
        f.write("}\n")


def fix_history(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    for d in data["dynasties"]:
        d["tw"] = tw(d["zh"])
        for key in ("full", "fam"):
            if d.get(key):
                d[key] = triple(d[key])
        for r in d["rulers"]:
            for key in ("n", "t", "dyn"):
                if r.get(key):
                    r[key] = triple(r[key])
            for key in ("shi", "miao", "era"):
                if r.get(key):
                    r[key + "_tw"] = tw(r[key])
        for p in d["people"]:
            p["n"] = triple(p["n"])
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


def main():
    root = os.path.join(sys.argv[1], "WorldMaps")
    fix_cities(os.path.join(root, "cities.json"))
    if os.path.exists(os.path.join(root, "history.json")):
        fix_history(os.path.join(root, "history.json"))
    era_dir = os.path.join(root, "eras")
    for name in sorted(os.listdir(era_dir)):
        if name.endswith(".json"):
            fix_era(os.path.join(era_dir, name))
    print("ok")


if __name__ == "__main__":
    main()
