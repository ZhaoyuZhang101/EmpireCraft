"""各时代剧本的剧情事件(用户 2026-10-30："对了唐朝剧本的安史之乱呢")：
写进 WorldMaps/eras/*.json 的 "events"，进世界后每年查一次，开局满 after 年、条件全部满足就触发(见 ScenarioEventSystem)。

每条事件：name 事件名(日志里用)、text 史书与屏幕上的一句("简体|英文"，脚本补繁体；只写游戏里对得上的城名、国名)、
after 开局后第几年起、when 条件(全部满足)、then 结果(逐条做)。条件与结果的种类、各项含义见 WorldMapData.WorldMapEventStep：
  条件 holds_city 某国据有某城 / holds_region 某国据有某地区法理过半 / gone 某国已亡 / empire 已称帝 / at_war 两国交战 / strain_above 积弊高于某数；
  结果 relation 两国关系 / proclaim 称帝 / rename 改国号 / move_capital 迁都 / culture 改文化 / adopt_huaxia 采纳华夏制度 /
       rebel 起兵割据(person 领头与部将，顿号隔开，按剧本 famous、kin、ruler 的姓名找人；city 治所、cities 另带的城；
       name 国号；b 新国的编号，后面的事件用它)。
国、城、地区都用本档的 id；改完跑 check_eras.py。

用法：python -I -X utf8 events_source.py <模组目录>
"""
import json
import os
import sys


def holds_city(a, city):
    return {"kind": "holds_city", "a": a, "city": city}


def holds_region(a, region):
    return {"kind": "holds_region", "a": a, "region": region}


EVENTS = {
    "high_tang": [
        # 天宝十四载(755)十一月，范阳、平卢、河东三镇节度使安禄山以讨杨国忠为名起兵，河北诸郡望风瓦解；
        # 史思明为其部将(平卢兵马使出身)。云州大同军使高秀岩随之。唐还据有幽州、河北过半才起
        {
            "name": "安禄山起兵",
            "text": "幽州兵起|Revolt breaks out at Youzhou",
            "after": 5,
            "when": [holds_city("tang", "beijing"), holds_region("tang", "hebei")],
            "then": [{
                "kind": "rebel", "a": "tang", "b": "yan", "city": "beijing",
                "cities": ["zhuozhou", "liaoyang", "dingzhou", "zhending", "handan", "ye", "datong"],
                "name": "燕|Yan", "person": "安禄山、史思明",
            }],
        },
        # 至德元载(756)正月，安禄山在洛阳称大燕皇帝，以洛阳为都
        {
            "name": "安禄山洛阳称帝",
            "text": "燕主于洛阳称帝|The lord of Yan proclaims himself emperor at Luoyang",
            "after": 6,
            "when": [holds_city("yan", "luoyang")],
            "then": [{"kind": "proclaim", "a": "yan", "name": "燕|Yan"}, {"kind": "move_capital", "a": "yan", "city": "luoyang"}],
        },
    ],
}

STEP_KEYS = ("kind", "a", "b", "city", "cities", "region", "value", "name", "relation", "goal", "culture", "person")


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records, triple  # noqa: E402
    with open(os.path.join(root, "WorldMaps", "cities.json"), encoding="utf-8") as f:
        cities = json.load(f)
    city_ids = {city["id"] for city in cities["cities"]}
    region_ids = {region["id"] for region in cities["regions"]}
    for era_id, events in EVENTS.items():
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        realms = {p["id"] for p in data["polities"]}
        # 起兵割据新立的国：之后的事件可以用它的编号
        realms |= {step["b"] for event in events for step in event["then"] if step["kind"] == "rebel" and step.get("b")}
        out = []
        for event in events:
            for step in event["when"] + event["then"]:
                for key in step:
                    if key not in STEP_KEYS:
                        raise SystemExit("%s %s：不认得的项 %s" % (era_id, event["name"], key))
                for key in ("a", "b"):
                    if step.get(key) and step[key] not in realms:
                        raise SystemExit("%s %s：没有国 %s" % (era_id, event["name"], step[key]))
                for city in [step.get("city")] + step.get("cities", []):
                    if city and city not in city_ids:
                        raise SystemExit("%s %s：城表上没有 %s" % (era_id, event["name"], city))
                if step.get("region") and step["region"] not in region_ids:
                    raise SystemExit("%s %s：没有地区 %s" % (era_id, event["name"], step["region"]))
                if step.get("name"):
                    step["name"] = triple(step["name"])
            item = {"name": event["name"], "text": triple(event.get("text", "")), "after": event.get("after", 0)}
            if event.get("repeat"):
                item["repeat"] = True
            item["when"] = event["when"]
            item["then"] = event["then"]
            out.append(item)
        data["events"] = out
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        print(era_id, len(out), "条")


if __name__ == "__main__":
    main()
