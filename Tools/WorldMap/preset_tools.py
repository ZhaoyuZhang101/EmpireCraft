"""按现有时代文件改写新预设剧本(ming_qing_source.py、song_jin_source.py)共用的小工具。"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from add_traditional import dump_records, fix_cities, fix_era, tw  # noqa: E402


def T(zh, en):
    """事件文字、国号："简体|英文|繁体"。"""
    return "%s|%s|%s" % (zh, en, tw(zh))


def person(fam, giv, age, **extra):
    """君主、宗室后妃。fam 写 None 表示不写这个键(宗室随君主姓)；写 "" 表示没有姓。"""
    item = {"giv": giv, "age": age}
    if fam is not None:
        item["fam"] = fam
    item.update(extra)
    return item


def famous(fam, giv, age, kind, rank, post, **extra):
    item = {"fam": fam, "giv": giv, "age": age, "kind": kind, "rank": rank, "post": post}
    item.update(extra)
    return item


def polity(id_, zh, en, culture, cap, **extra):
    item = {"id": id_, "zh": zh, "en": en, "c": culture, "cap": cap}
    item.update(extra)
    return item


def relation(a, b, kind, goal=None, value=None):
    item = {"a": a, "b": b, "kind": kind}
    if goal:
        item["goal"] = goal
    if value is not None:
        item["value"] = value
    return item


def rel_step(a, b, kind, goal=None, value=None):
    item = {"kind": "relation", "a": a, "b": b, "relation": kind}
    if goal:
        item["goal"] = goal
    if value is not None:
        item["value"] = value
    return item


def event(name, text, after, when, then):
    return {"name": name, "text": text, "after": after, "when": when, "then": then}


def death(name, text, after, realm, people, cause):
    """人物按史实身故：不看别的条件，到年份就走；点名的人早没了文字也不上屏幕。name 是日志名，text、cause 是 (简体, 英文)。"""
    return event(name, T(*text), after, [], [{"kind": "dies", "a": realm, "person": people, "name": T(*cause)}])


def load_era(root, era_id):
    with open(os.path.join(root, "WorldMaps", "eras", era_id + ".json"), encoding="utf-8") as f:
        return json.load(f)


def write_era(root, era):
    path = os.path.join(root, "WorldMaps", "eras", era["id"] + ".json")
    head = {k: v for k, v in era.items() if k not in ("cores", "polities")}
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        for key, value in head.items():
            f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
        dump_records(f, "cores", era.get("cores", []), False)
        dump_records(f, "polities", era.get("polities", []), True)
        f.write("}\n")
    # 只补这一份的繁体：别的时代不跟着重排
    fix_era(path)


def update_cities(root, new_cities, alts):
    """城表加城、改旧称；反复跑结果不变(繁体由 add_traditional.py 补)。"""
    path = os.path.join(root, "WorldMaps", "cities.json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    by_id = {city["id"]: city for city in data["cities"]}
    # 没变就不写：城表常被别的进程占着(Errno 22)
    changed = False
    for city in new_cities:
        old = by_id.get(city["id"])
        if old is None:
            data["cities"].append(dict(city))
            changed = True
        elif any(old.get(key) != value for key, value in city.items()):
            old.update(city)
            changed = True
    for cid, alt in alts.items():
        if ["|".join(entry.split("|")[:3]) for entry in by_id[cid].get("alt", [])] != alt:
            by_id[cid]["alt"] = alt
            changed = True
    if not changed:
        return data
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('  "_note": %s,\n' % json.dumps(data["_note"], ensure_ascii=False))
        f.write('  "tianxia": %s,\n' % json.dumps(data["tianxia"], ensure_ascii=False))
        dump_records(f, "regions", data["regions"], False)
        dump_records(f, "cores", data.get("cores", []), False)
        dump_records(f, "cities", data["cities"], True)
        f.write("}\n")
    fix_cities(path)
    return data


def active_ids(data, year):
    return {c["id"] for c in data["cities"] if c.get("from", -99999) <= year <= c.get("to", 99999)}
