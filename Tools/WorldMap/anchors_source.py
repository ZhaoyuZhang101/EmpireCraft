"""世界地图剧本的州府治所锚点：把 Tools/WorldMap/anchors/*.py 里各时代、各国、各行政区当年的州府治所经纬度写进
WorldMaps/eras/*.json(divisions[].seats；都城直辖那一块与没有行政区的国家写 polities[].seats)。
游戏里由 Scripts/GeneralSystems/WorldMap/WorldMapScenario.cs 的 AnchorTerritory 用：开局铺辖地前，所有的城连同这些锚点
一起按地形代价分地，分给某国某个行政区的地块归那个行政区里离它最近的城，行政区的位置、形状照当年的州府走
(用户 2026-10-30 "同一时代下你的行政区划的位置和形状不符合历史"，选"补州府治所锚点"，"网上多对比对比已有的各个时代的行政区划图")。

数据文件(anchors/*.py)各有一个 ANCHORS = {时代 id: {政权 id: {"_core" 或行政区中文名: [(名, 经度, 纬度), ...]}}}；
行政区名对 era 文件 divisions[].name 竖线前那段；MOVE 里的城挪进所写的行政区。DROP 里的行政区从 era 文件里去掉(城归都城直辖)：
唐的都城直辖那一块游戏里就叫京畿道(见 DynastyNaming.CapitalRegionLabel)，原来另有一个只装陈仓的"京畿道"行政区，两个同名。

用法：python -I -X utf8 anchors_source.py <模组目录>
"""
import glob
import json
import os
import sys

DROP = {
    "high_tang": {"tang": ["京畿道"]},
    "charlemagne": {"tang": ["京畿道"]},
}

# MOVE 里的城挪进所写的行政区(从别的行政区里去掉)：明的彰德府属河南布政使司，原来留在都城直辖；
# 汉魏吴的始安(桂林)属零陵郡、归荆州，原来写在交州
MOVE = {
    "lingao": {"ming": {"河南": ["ye"]}},
    "roman_empire": {"han": {"荆州": ["guilin"]}},
    "three_kingdoms": {"wu": {"荆州": ["guilin"]}},
}

# REGIONS 改政权认领的地区("+"加、"-"去)：800 年唐仍有灵、盐、夏、丰诸州与天德、振武二军，河套不归回鹘
REGIONS = {
    "charlemagne": {"tang": ["+hetao"], "uyghur": ["-hetao"]},
}

# UNDIVIDE 里的城从各行政区里去掉(归都城直辖)：新郑属河南尹(东汉、魏都城直辖)，原来写在司隶；
# 230 年辽阳是公孙渊的，原来写在魏的幽州
UNDIVIDE = {
    "roman_empire": {"han": ["xinzheng"]},
    "three_kingdoms": {"wei": ["xinzheng", "liaoyang"]},
}


def load_anchors(folder):
    merged = {}
    for path in sorted(glob.glob(os.path.join(folder, "*.py"))):
        scope = {}
        with open(path, encoding="utf-8") as f:
            exec(f.read(), scope)
        for era, polities in scope["ANCHORS"].items():
            for polity, groups in polities.items():
                target = merged.setdefault(era, {}).setdefault(polity, {})
                for key, seats in groups.items():
                    if key in target:
                        raise SystemExit("%s：%s %s %s 重复" % (os.path.basename(path), era, polity, key))
                    target[key] = seats
    return merged


def check_seat(seat, where):
    name, lon, lat = seat
    if not (-30 <= lon <= 180 and -45 <= lat <= 80):
        raise SystemExit("%s：%s 的经纬度不对 %s, %s" % (where, name, lon, lat))
    return [round(float(lon), 2), round(float(lat), 2)]


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    here = os.path.dirname(os.path.abspath(__file__))
    sys.path.insert(0, here)
    from add_traditional import dump_records  # noqa: E402
    anchors = load_anchors(os.path.join(here, "anchors"))
    for path in sorted(glob.glob(os.path.join(root, "WorldMaps", "eras", "*.json"))):
        era = os.path.splitext(os.path.basename(path))[0]
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        polities = {p["id"]: p for p in data["polities"]}
        # 先清掉旧的，再按数据写(重跑结果一样)
        for polity in data["polities"]:
            polity.pop("seats", None)
            for division in polity.get("divisions", []):
                division.pop("seats", None)
        for polity_id, names in DROP.get(era, {}).items():
            polity = polities.get(polity_id)
            if polity is not None:
                polity["divisions"] = [d for d in polity.get("divisions", []) if d["name"].split("|")[0] not in names]
        for polity_id, changes in REGIONS.get(era, {}).items():
            polity = polities.get(polity_id)
            if polity is None:
                raise SystemExit("%s：没有政权 %s" % (era, polity_id))
            regions = polity.setdefault("r", [])
            for change in changes:
                region = change[1:]
                if change[0] == "+" and region not in regions:
                    regions.append(region)
                elif change[0] == "-" and region in regions:
                    regions.remove(region)
        for polity_id, cities in UNDIVIDE.get(era, {}).items():
            for division in polities.get(polity_id, {}).get("divisions", []):
                division["cities"] = [c for c in division["cities"] if c not in cities]
        for polity_id, targets in MOVE.get(era, {}).items():
            polity = polities.get(polity_id)
            if polity is None:
                continue
            for name, cities in targets.items():
                division = next((d for d in polity.get("divisions", []) if d["name"].split("|")[0] == name), None)
                if division is None:
                    raise SystemExit("%s：%s 没有行政区 %s" % (era, polity_id, name))
                for other in polity.get("divisions", []):
                    other["cities"] = [c for c in other["cities"] if c not in cities or other is division]
                division["cities"] += [c for c in cities if c not in division["cities"]]
        count = 0
        for polity_id, groups in anchors.get(era, {}).items():
            polity = polities.get(polity_id)
            if polity is None:
                raise SystemExit("%s：没有政权 %s" % (era, polity_id))
            divisions = {d["name"].split("|")[0]: d for d in polity.get("divisions", [])}
            for key, seats in groups.items():
                where = "%s %s %s" % (era, polity_id, key)
                points = [check_seat(seat, where) for seat in seats]
                if not points:
                    continue
                if key == "_core":
                    polity["seats"] = points
                elif key in divisions:
                    divisions[key]["seats"] = points
                else:
                    raise SystemExit("%s：没有行政区 %s" % (where, key))
                count += len(points)
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        if count:
            print(era, count, "个锚点")


if __name__ == "__main__":
    main()
