"""世界地图剧本的丝绸之路：把 silkroad_data.py 里各时代的陆上、海上丝路(史实走向、沿途各站)写进 WorldMaps/eras/*.json 的
"silk" 一节。游戏里由 Scripts/GeneralSystems/WorldMap/SilkRoadSystem.cs 用：开局陆路相邻两站之间沿地形修成路(不荒)，
沿途各站对上最近的城，市场互市时丝路相邻两站的城所属的国家连通(用户 2026-10-30 "预设剧本里面的丝绸之路呢""要符合历史"，
问答定"陆上和海上都做""修成路并连通沿途市场")。

用法：python -I -X utf8 silkroad_source.py <模组目录>
"""
import glob
import json
import os
import sys


def load_routes(path):
    scope = {}
    with open(path, encoding="utf-8") as f:
        exec(f.read(), scope)
    return scope["ROUTES"]


def check(route, where):
    parts = route["name"].split("|")
    if len(parts) != 3 or not all(parts):
        raise SystemExit("%s：丝路名要写成 简体|英文|繁體：%s" % (where, route["name"]))
    pts, stops = [], []
    for name, lon, lat in route["pts"]:
        if not (-30 <= lon <= 180 and -45 <= lat <= 80):
            raise SystemExit("%s：%s 的经纬度不对 %s, %s" % (where, name, lon, lat))
        pts.append([round(float(lon), 2), round(float(lat), 2)])
        stops.append(name)
    if len(pts) < 2:
        raise SystemExit("%s：至少两站" % where)
    return {"name": route["name"], "sea": bool(route.get("sea")), "pts": pts, "stops": stops}


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    here = os.path.dirname(os.path.abspath(__file__))
    sys.path.insert(0, here)
    from add_traditional import dump_records  # noqa: E402
    routes = load_routes(os.path.join(here, "silkroad_data.py"))
    for path in sorted(glob.glob(os.path.join(root, "WorldMaps", "eras", "*.json"))):
        era = os.path.splitext(os.path.basename(path))[0]
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        data["silk"] = [check(route, "%s %s" % (era, route.get("name", "?"))) for route in routes.get(era, [])]
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        land = sum(1 for r in data["silk"] if not r["sea"])
        print(era, "陆路", land, "条，海路", len(data["silk"]) - land, "条，", sum(len(r["pts"]) for r in data["silk"]), "站")


if __name__ == "__main__":
    main()
