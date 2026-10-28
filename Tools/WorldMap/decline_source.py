"""各时代剧本开局的王朝积弊与六弊(照史实)：写进 WorldMaps/eras/*.json 各国的 "decline"，进世界后由 WorldMapScenario.ApplyDecline 套。

键：age(现王室立国多少年；前史一个朝代跨了几个王室时另给)、strain(王朝积弊)、六弊 graft 吏治、annexation 兼并、bloat 冗费、
military 武备、factionalism 党争、levies 苛捐，都是 0~100。不写的照国祚给同岁数最少该有的。

用法：python -I -X utf8 decline_source.py <模组目录>
"""
import json
import os
import sys


def D(**values):
    return values


DECLINE = {
    "roman_empire": {
        # 图拉真盛世：安敦尼王朝立国二十年
        "rome": D(age=21, strain=12, graft=15, annexation=25, bloat=15, military=10, factionalism=10, levies=20),
        # 东汉安帝：邓太后临朝、外戚宦官渐起，豪强兼并，羌乱连年
        "han": D(age=92, strain=45, graft=50, annexation=60, bloat=35, military=45, factionalism=50, levies=40),
        # 帕提亚：王位内争，图拉真东征
        "parthia": D(strain=60, graft=40, annexation=45, bloat=30, military=45, factionalism=65, levies=40),
    },
    "three_kingdoms": {
        # 塞维鲁王朝末年，三世纪危机前夜
        "rome": D(age=37, strain=50, graft=45, annexation=50, bloat=40, military=55, factionalism=50, levies=55),
        "kushan": D(strain=60, graft=45, annexation=45, bloat=40, military=50, factionalism=55, levies=45),
    },
    "high_tang": {
        # 天宝九载：均田崩坏、逃户日多，府兵废、边镇坐大，李林甫专权
        "tang": D(strain=50, graft=45, annexation=65, bloat=40, military=55, factionalism=45, levies=35),
        "byzantium": D(age=33),
    },
    "charlemagne": {
        # 贞元十六年：藩镇割据，宦官掌禁军，宫市进奉
        "tang": D(strain=70, graft=55, annexation=70, bloat=55, military=75, factionalism=45, levies=60),
        # 伊琳娜女皇：伊苏里亚王朝末年，宫廷倾轧
        "byzantium": D(age=83, strain=55, graft=45, annexation=40, bloat=40, military=45, factionalism=60, levies=45),
    },
    "crusades": {
        # 绍熙元年：三冗积重，经总制钱，兼并
        "song": D(strain=60, graft=50, annexation=70, bloat=75, military=60, factionalism=50, levies=60),
        # 安格洛斯王朝：吏治败坏，军区瓦解
        "byzantium": D(age=5, strain=55, graft=60, annexation=55, bloat=45, military=60, factionalism=60, levies=55),
        # 霍亨施陶芬
        "hre": D(age=52),
    },
    "lingao": {
        # 崇祯元年：阉党东林党争，卫所废弛、边兵欠饷，宗藩禄米，辽饷加派，土地兼并(用户 10-29 "明朝的积弊太高了调成65吧"：
        # 王朝积弊 65，六弊平均也压到 65 上下，免得六弊高过积弊、年年把积弊往上推)
        "ming": D(strain=65, graft=60, annexation=70, bloat=65, military=65, factionalism=70, levies=60),
        # 哈布斯堡
        "hre": D(age=190),
    },
}

KEYS = {"age", "strain", "graft", "annexation", "bloat", "military", "factionalism", "levies"}


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records  # noqa: E402
    for era_id, items in DECLINE.items():
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        polities = {p["id"]: p for p in data["polities"]}
        for pid, values in items.items():
            if pid not in polities:
                raise SystemExit("%s：没有 %s" % (era_id, pid))
            if set(values) - KEYS:
                raise SystemExit("%s：未知键 %s" % (era_id, set(values) - KEYS))
            polities[pid]["decline"] = values
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
