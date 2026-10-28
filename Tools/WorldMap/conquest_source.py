"""剧本里定下的入主：写进 WorldMaps/eras/*.json 各国的 "conquest"(入主华夏时的国号，美号，显示加"大")与 "conquest_city"
(据有即算入关的城)。进世界后记在本国上，入关即采纳华夏制度走兼用，国号照它(见 CompositeEmpireService.ScriptedConquestName)。

用法：python -I -X utf8 conquest_source.py <模组目录>
"""
import json
import os
import sys

CONQUESTS = {
    # 后金入关(1644 据北京)称大清
    "lingao": {"later_jin": ("清|Qing|清", "beijing")},
}


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records  # noqa: E402
    for era_id, items in CONQUESTS.items():
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        polities = {p["id"]: p for p in data["polities"]}
        for pid, (name, city) in items.items():
            if pid not in polities:
                raise SystemExit("%s：没有 %s" % (era_id, pid))
            polities[pid]["conquest"] = name
            polities[pid]["conquest_city"] = city
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        print(era_id, len(items), "国")


if __name__ == "__main__":
    main()
