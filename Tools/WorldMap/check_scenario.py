"""校验一份剧本(自定剧本 scenario.json 或内置 WorldMaps/eras/*.json)是否照 Docs/scenario-authoring-guide.md 的接口写：
键名、取值、城/地区/国编号、宗室后妃序号、外交、剧情事件(条件、结果、起兵要找的人)。只读不改。

用法：python -I -X utf8 check_scenario.py <模组目录> <剧本文件>
输出"错"的开局会跳过或读错，要改；"注意"是可能不合意的地方。全无"错"时最后一行是 ok。
"""
import json
import os
import sys

TOP = {"id", "year", "focus", "order", "zh", "en", "tw", "dzh", "den", "dtw", "author", "tech", "techExtra", "techSkip", "inst",
       "culture", "ranks", "transmigrant", "relations", "walls", "passes", "wilderness", "silk", "events", "cores", "polities",
       "regions", "cities", "tianxia", "map", "paint", "terrain", "tribes", "frame", "wilderness_regions",
       "exact", "own_wilderness", "paint_base", "base_stale", "size", "own_cities", "independent", "develop", "base"}
POLITY = {"id", "zh", "en", "tw", "c", "cap", "r", "cities", "seats", "boundary", "of", "emp", "banner", "hero", "court", "shogun", "core", "zhou", "king", "ruler", "kin",
          "era_name", "dynasty", "era_year", "grand", "holy", "old_capital", "divisions", "colonies", "famous", "conquest",
          "conquest_city", "decline"}
PERSON = {"fam", "giv", "age", "sex", "rel", "of", "heir", "mo", "in", "house", "f", "p"}
FAMOUS = {"fam", "giv", "age", "sex", "kind", "rank", "post", "in"}
RELS = {"son", "daughter", "grandson", "granddaughter", "brother", "sister", "nephew", "cousin", "wife", "consort"}
CULTURES = {"Huaxia", "Youmu", "Japan", "Roma", "Frankish", "Germanic", "Viking", "Slavonic", "Arab", "Persepolis", "India", "Egypt",
            "Kosher", "Manzhou", "Xizang", "Swahili", "Bantu", "Zulu", "Aztec", "Ojibwe"}
RELATIONS = {"war", "truce", "alliance", "marriage", "pact", "protect", "tribute", "vassal", "union", "friend", "feud"}
GOALS = {"夺城", "收复", "争法理", "劫掠", "侵略", "争夺资源", "吞并", "迫使附庸"}
DECLINE = {"strain", "graft", "annexation", "bloat", "military", "factionalism", "levies", "age"}
CONDITIONS = {"holds_city": ("a", "city"), "holds_region": ("a", "region"), "gone": ("a",), "alive": ("a",), "empire": ("a",),
              "at_war": ("a", "b"), "strain_above": ("a", "value"), "before": ("value",), "dead": ("person",)}
EFFECTS = {"relation": ("a", "b", "relation"), "proclaim": ("a",), "rename": ("a", "name"), "move_capital": ("a", "city"),
           "culture": ("a", "culture"), "adopt_huaxia": ("a",), "rebel": ("a", "city", "name", "person"),
           "cede": ("a", "b"), "dies": ("a", "person"), "heir": ("a", "person"),
           "grant_title": ("a", "person", "name"), "rally": ("a", "b"), "submit": ("a", "b"),
           "banner_city": ("a",), "banner_enclosure": ("a",), "banner_law": ("a",)}
STEP = {"kind", "a", "b", "c", "city", "cities", "region", "value", "name", "relation", "goal", "culture", "person", "roving", "hero"}
EVENT = {"name", "text", "after", "repeat", "when", "then"}

errors, notes = [], []


def err(text):
    errors.append(text)


def note(text):
    notes.append(text)


def first(text):
    return (text or "").split("|")[0]


def check_boundary(polygons, where):
    if polygons is None:
        return
    if not isinstance(polygons, list):
        err(where + "：boundary 必须是 MultiPolygon 坐标数组")
        return
    for polygon in polygons:
        if not isinstance(polygon, list) or not polygon:
            err(where + "：boundary 的 polygon 没有外圈")
            continue
        for ring in polygon:
            if not isinstance(ring, list) or len(ring) < 4:
                err(where + "：boundary 的环至少四点并首尾闭合")
                continue
            valid = all(isinstance(p, list) and len(p) == 2 and
                        all(isinstance(v, (int, float)) and not isinstance(v, bool) for v in p) and
                        -180 <= p[0] <= 180 and -90 <= p[1] <= 90 for p in ring)
            if not valid or ring[0] != ring[-1]:
                err(where + "：boundary 经纬度无效或环未闭合")


def full_names(person):
    fam, giv = person.get("fam"), person.get("giv") or ""
    yield (first(fam) if fam else "") + first(giv)
    if fam is None:
        yield first(giv)


def main():
    root, path = sys.argv[1], sys.argv[2]
    try:
        with open(path, encoding="utf-8") as f:
            era = json.load(f)
    except Exception as exception:  # noqa: BLE001
        print("错：JSON 读不出来(注释、末尾逗号、单引号都不行)：%s" % exception)
        return
    with open(os.path.join(root, "WorldMaps", "cities.json"), encoding="utf-8") as f:
        base = json.load(f)
    own = bool(era.get("cities")) or bool(era.get("map"))
    cities = {c["id"]: c for c in (era.get("cities") or [] if own else base["cities"])}
    regions = {r["id"] for r in (era.get("regions") or [] if own else base["regions"])}
    cores = {c["id"] for c in (era.get("cores") or [])} | ({c["id"] for c in base.get("cores", [])} if not own else set())
    year = era.get("year")
    if not isinstance(year, int):
        err("year 要写整数年份(公元前写负数)")
        year = 0
    for key in era:
        if key not in TOP:
            err("顶层不认得的键 %s" % key)
    if era.get("focus", "east") not in ("east", "west"):
        err("focus 只能是 east 或 west")

    def city(cid, where, required=True, soft=False):
        if not cid:
            if required:
                err("%s：没写城" % where)
            return None
        c = cities.get(cid)
        if c is None:
            err("%s：城表上没有 %s(关隘不是城)" % (where, cid))
            return None
        if not (c.get("from", -99999) <= year <= c.get("to", 99999)):
            (note if soft else err)("%s：城 %s(%s)在 %s 年不存在(from %s to %s)" % (where, cid, c["zh"], year, c.get("from"), c.get("to")))
        return c

    for key in ("culture",):
        for region, culture in era.get(key, {}).items():
            if region not in regions:
                err("culture：没有地区 %s" % region)
            if culture not in CULTURES:
                err("culture：%s 不是文化键" % culture)
    for cid in era.get("ranks", {}):
        city(cid, "ranks")
    for unit, region in era.get("wilderness_regions", {}).items():
        if unit not in era.get("wilderness", {}):
            err("wilderness_regions：%s 没有对应的国家归属" % unit)
        if region not in regions:
            err("wilderness_regions：没有地区 %s" % region)
    for culture in list(era.get("tech", {})) + list(era.get("inst", {})):
        if culture != "*" and culture not in CULTURES:
            err("tech/inst：%s 不是文化键" % culture)

    polities = era.get("polities") or []
    by_id = {}
    for p in polities:
        pid = p.get("id", "")
        where = "国 %s" % (pid or p.get("zh"))
        if not pid:
            err("%s：没有 id" % where)
        elif pid in by_id:
            err("国 id 重复：%s" % pid)
        by_id[pid] = p
        for key in p:
            if key not in POLITY:
                err("%s：不认得的键 %s" % (where, key))
        if p.get("c") and p["c"] not in CULTURES:
            err("%s：c=%s 不是文化键" % (where, p["c"]))
        city(p.get("cap"), where + " 的都城 cap")
        for cid in p.get("cities", []):
            city(cid, where + " 的 cities")
        for r in p.get("r", []):
            if r not in regions:
                err("%s：认领的地区 %s 不存在" % (where, r))
        if p.get("core") and p["core"] not in cores:
            note("%s：core %s 不在帝国法理表里" % (where, p["core"]))
        for cid in (p.get("old_capital"), p.get("conquest_city")):
            if cid:
                city(cid, where)
        check_boundary(p.get("boundary"), where)
        for d in p.get("divisions", []):
            check_boundary(d.get("boundary"), where + " 行政区 " + first(d.get("name")))
            for cid in d.get("cities", []):
                city(cid, where + " 行政区 " + first(d.get("name")) + "(开局跳过)", soft=True)
        for key in p.get("decline", {}):
            if key not in DECLINE:
                err("%s：decline 不认得的项 %s" % (where, key))
    for p in polities:
        if p.get("of") and p["of"] not in by_id:
            err("国 %s：宗主 of=%s 不存在" % (p.get("id"), p["of"]))
        elif p.get("of") and not by_id[p["of"]].get("emp"):
            note("国 %s：宗主 %s 没有 emp(开局不是帝国)" % (p.get("id"), p["of"]))

    people = {}
    for p in polities:
        pid = p.get("id", "")
        persons = []
        if p.get("ruler"):
            persons.append(p["ruler"])
            for key in p["ruler"]:
                if key not in PERSON:
                    err("国 %s 君主：不认得的键 %s" % (pid, key))
        kin = p.get("kin", [])
        for i, k in enumerate(kin):
            where = "国 %s kin[%d] %s" % (pid, i, first(k.get("giv")))
            for key in k:
                if key not in PERSON:
                    err("%s：不认得的键 %s" % (where, key))
            rel = k.get("rel", "")
            if rel not in RELS:
                err("%s：rel=%s 不认得" % (where, rel))
            if not k.get("giv"):
                err("%s：没有 giv" % where)
            for key in ("of", "mo", "in"):
                v = k.get(key, -1)
                if v is None or v == -1:
                    continue
                if not isinstance(v, int) or v < 0 or v >= len(kin) or v == i:
                    err("%s：%s=%s 不是别的 kin 的序号" % (where, key, v))
                elif key in ("mo", "in") and kin[v].get("rel") not in ("wife", "consort"):
                    err("%s：%s 指向的 kin[%d] 不是后妃" % (where, key, v))
            if k.get("mo", -1) not in (-1, None) and rel not in ("son", "daughter", "grandson", "granddaughter", "nephew"):
                note("%s：只有子孙辈记生母" % where)
            if k.get("house"):
                if rel not in ("wife", "consort"):
                    err("%s：house 只给后妃" % where)
                elif k["house"] not in by_id or not by_id[k["house"]].get("ruler"):
                    err("%s：house=%s 不是本剧本有君主的国" % (where, k["house"]))
            if rel in ("wife", "consort") and "fam" not in k:
                note("%s：后妃没写 fam(会按文化随机起姓)" % where)
        wives = {}
        for k in kin:
            if k.get("rel") == "wife":
                husband = k.get("of", -1)
                if husband in wives:
                    err("国 %s：%s 有两位正妻" % (pid, "君主" if husband == -1 else "kin[%d]" % husband))
                wives[husband] = k
        persons += kin
        for i, f in enumerate(p.get("famous", [])):
            where = "国 %s famous[%d] %s" % (pid, i, first(f.get("giv")))
            for key in f:
                if key not in FAMOUS:
                    err("%s：不认得的键 %s" % (where, key))
            if f.get("kind") not in ("general", "minister"):
                err("%s：kind 只能是 general 或 minister" % where)
            if f.get("rank") not in (0, 1, 2):
                err("%s：rank 只能是 0、1、2" % where)
            post = f.get("post", "")
            if post not in ("chief", "core", "div", "general", "none"):
                city(post, where + " 的 post")
            v = f.get("in", -1)
            if v not in (-1, None) and (not isinstance(v, int) or v < 0 or v >= len(kin) or kin[v].get("rel") not in ("wife", "consort")):
                err("%s：in=%s 要指向本国 kin 里的后妃" % (where, v))
        persons += p.get("famous", [])
        for person in persons:
            for name in full_names(person):
                people.setdefault(name, pid)

    for i, r in enumerate(era.get("relations", [])):
        where = "relations[%d]" % i
        for key in ("a", "b"):
            if r.get(key) not in by_id:
                err("%s：%s=%s 不是本剧本的国" % (where, key, r.get(key)))
        if r.get("kind") not in RELATIONS:
            err("%s：kind=%s 不认得" % (where, r.get("kind")))
        if r.get("kind") == "war" and r.get("goal") and r["goal"] not in GOALS:
            err("%s：goal=%s 不认得" % (where, r["goal"]))
        if r.get("city"):
            if r.get("kind") != "war":
                err("%s：只有宣战(war)才写目标城" % where)
            city(r["city"], where + " city")

    realms = set(by_id)
    for i, e in enumerate(era.get("events", [])):
        where = "events[%d] %s" % (i, e.get("name", ""))
        for key in e:
            if key not in EVENT:
                err("%s：不认得的键 %s" % (where, key))
        if not isinstance(e.get("after", 0), int) or e.get("after", 0) < 0:
            err("%s：after 要写非负整数(开局后第几年)" % where)
        # 只出文字的史事可以没有结果，但要有文字、要用 before 卡住年份(不然条件晚了十几年才凑齐，史书上的年份就错了)
        if not e.get("then"):
            if not e.get("text"):
                err("%s：没有结果 then，也没有文字" % where)
            elif not any(c.get("kind") == "before" for c in e.get("when", [])):
                err("%s：只出文字的事件要有 before 条件卡住年份" % where)
        for part, table in (("when", CONDITIONS), ("then", EFFECTS)):
            for j, s in enumerate(e.get(part, [])):
                at = "%s %s[%d] %s" % (where, part, j, s.get("kind"))
                for key in s:
                    if key not in STEP:
                        err("%s：不认得的键 %s" % (at, key))
                kind = s.get("kind")
                if kind not in table:
                    err("%s：%s 里没有这一种(条件、结果别放反)" % (at, part))
                    continue
                for key in table[kind]:
                    if s.get(key) in (None, "", []):
                        err("%s：缺 %s" % (at, key))
                for key in ("a", "b"):
                    if key == "b" and kind == "rebel":
                        continue
                    if s.get(key) and s[key] not in realms:
                        err("%s：%s=%s 不是本剧本的国(也不是前面起兵立的国)" % (at, key, s[key]))
                for cid in [s.get("city")] + list(s.get("cities") or []):
                    if cid:
                        c = city(cid, at)
                        if c and part == "when" and era.get("ranks", {}).get(cid, c.get("k", 2)) > 2:
                            note("%s：条件里的 %s 是小城(k=%s)，最小地图上没有，条件永远不成" % (at, cid, c.get("k")))
                if s.get("region") and s["region"] not in regions:
                    err("%s：没有地区 %s" % (at, s["region"]))
                if kind == "relation":
                    if s.get("relation") not in RELATIONS:
                        err("%s：relation=%s 不认得" % (at, s.get("relation")))
                    if s.get("goal") and s["goal"] not in GOALS:
                        err("%s：goal=%s 不认得" % (at, s["goal"]))
                if kind == "culture" and s.get("culture") not in CULTURES:
                    err("%s：culture=%s 不是文化键" % (at, s.get("culture")))
                if kind == "cede" and not s.get("city") and not s.get("cities") and not s.get("person"):
                    err("%s：城和人至少写一样" % at)
                if kind in ("dies", "heir", "grant_title", "dead"):
                    for name in [n.strip() for n in (s.get("person") or "").replace("，", "、").replace(",", "、").split("、") if n.strip()]:
                        if name not in people:
                            err("%s：剧本人物里找不到 %s(简体姓+名连写)" % (at, name))
                if kind == "cede":
                    for name in [n.strip() for n in (s.get("person") or "").replace("，", "、").replace(",", "、").split("、") if n.strip()]:
                        if name not in people:
                            err("%s：剧本人物里找不到 %s(简体姓+名连写)" % (at, name))
                        elif people[name] != s.get("a"):
                            note("%s：%s 是国 %s 的人，不在 a=%s 里，投奔时找不到他" % (at, name, people[name], s.get("a")))
                for flag in ("roving", "hero"):
                    if flag in s and (kind != "rebel" or not isinstance(s[flag], bool)):
                        err("%s：%s 只用在起兵(rebel)上，写 true/false" % (at, flag))
                if kind == "rebel":
                    for name in [n.strip() for n in (s.get("person") or "").replace("，", "、").replace(",", "、").split("、") if n.strip()]:
                        if name not in people:
                            err("%s：剧本人物里找不到 %s(写进 famous，简体姓+名连写)" % (at, name))
                        elif people[name] != s.get("a"):
                            note("%s：%s 是国 %s 的人，不在 a=%s 里，起兵时找不到他(帝国成员国的人可以)" % (at, name, people[name], s.get("a")))
                    if s.get("b"):
                        realms.add(s["b"])
                    else:
                        note("%s：没写 b，后面的事件用不上这个新国" % at)
    for text in errors:
        print("错：" + text)
    for text in notes:
        print("注意：" + text)
    print("ok" if not errors else "共 %d 处错" % len(errors))


if __name__ == "__main__":
    main()
