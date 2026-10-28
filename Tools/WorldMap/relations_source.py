"""各时代剧本开局的外交关系(照史实)：写进 WorldMaps/eras/*.json 的 "relations"，进世界后由 WorldMapScenario 逐条套
(DiplomacyActions.ApplyScenario，走外交界面同一套判定，做不了的记日志)。

kind：war(a 向 b 宣战，goal 战争目标，如 夺城/收复/争法理/劫掠/侵略)、truce、alliance、marriage(和亲联姻)、pact(通好，两边都是帝国)、
protect(a 保护 b)、tribute(a 向帝国 b 称臣纳贡)、vassal(a 立 b 为附庸)、union(b 并入 a 的市场)、friend/feud(旧谊/宿怨，value 默认 30)。
临高那家不写：降临时自动伪装成明的属国。

用法：python -I -X utf8 relations_source.py <模组目录>
"""
import json
import os
import sys


def R(a, b, kind, goal="", value=0):
    e = {"a": a, "b": b, "kind": kind}
    if goal:
        e["goal"] = goal
    if value:
        e["value"] = value
    return e


RELATIONS = {
    "warring_states": [
        R("qin", "zhao", "war", "夺城"),            # 长平之战
        R("qin", "zhao", "feud", value=50),
        R("qin", "qi", "friend"),                   # 远交近攻
        R("rome", "carthage", "war", "夺城"),       # 第一次布匿战争(争西西里)
        R("rome", "syracuse", "alliance"),          # 希伦二世与罗马结盟
        R("ptolemy", "rome", "friend"),
        R("seleucid", "ptolemy", "war", "争法理"),  # 第二次叙利亚战争(争柯里叙利亚)
        R("macedon", "ptolemy", "feud"),            # 克瑞摩尼德斯战争刚结束
        R("xiongnu", "yuezhi", "feud"),
        R("xiongnu", "donghu", "feud"),
    ],
    "roman_empire": [
        R("rome", "parthia", "war", "争法理"),      # 图拉真东征(美索不达米亚)
        R("rome", "bosporus", "protect"),
        R("rome", "iberia", "protect"),
        R("rome", "caledonia", "feud"),
        R("han", "qiang", "war", "夺城"),           # 永初羌乱
        R("buyeo", "han", "tribute"),
        R("han", "xianbei", "feud"),
        R("han", "goguryeo", "feud"),
    ],
    "three_kingdoms": [
        R("wei", "shu", "war", "夺城"),             # 曹真伐蜀、诸葛亮北伐
        R("shu", "wu", "alliance"),                 # 吴蜀同盟
        R("wei", "wu", "feud", value=40),
        R("wei", "shu", "feud", value=50),
        R("gongsun", "wei", "tribute"),             # 公孙渊名义称臣
        R("wei", "kushan", "friend"),               # 波调遣使(229)
        R("wei", "xianbei", "feud"),                # 轲比能
        R("sasan", "rome", "war", "争法理"),        # 阿尔达希尔攻罗马美索不达米亚
    ],
    "high_tang": [
        R("silla", "tang", "tribute"), R("balhae", "tang", "tribute"),
        R("ferghana", "tang", "tribute"), R("kashmir", "tang", "tribute"),
        R("japan", "tang", "friend"),               # 遣唐使
        R("tang", "nanzhao", "war", "夺城"),        # 天宝战争
        R("tibet", "nanzhao", "friend"),
        R("tang", "tibet", "feud", value=40),
        R("tang", "uyghur", "alliance"),
        R("tang", "khitan", "feud"),
        R("abbasid", "tang", "feud"),               # 怛罗斯
        R("abbasid", "byzantium", "war", "劫掠"),
        R("byzantium", "khazar", "marriage"),       # 君士坦丁五世娶可萨公主
    ],
    "charlemagne": [
        R("francia", "papacy", "protect"),
        R("francia", "abbasid", "friend"),          # 查理曼与哈伦互通使节
        R("francia", "byzantium", "feud"),          # 称帝之争
        R("francia", "asturias", "friend"),
        R("francia", "cordoba", "feud"),
        R("silla", "tang", "tribute"), R("balhae", "tang", "tribute"), R("nanzhao", "tang", "tribute"),
        R("tang", "uyghur", "marriage"),            # 咸安公主和亲回鹘
        R("japan", "tang", "friend"),
        R("tang", "tibet", "feud", value=40),
        R("abbasid", "byzantium", "war", "劫掠"),   # 哈伦屡伐拜占庭
        R("aghlabid", "abbasid", "tribute"),
        R("abbasid", "idrisid", "feud"),
        R("abbasid", "cordoba", "feud"),
        R("bulgaria", "byzantium", "feud"),
    ],
    "crusades": [
        R("jerusalem", "ayyubid", "war", "收复"),   # 阿卡之围
        R("england", "ayyubid", "war", "收复"),     # 第三次十字军
        R("france", "ayyubid", "war", "收复"),
        R("england", "france", "alliance"),
        R("hre", "rum", "war", "夺城"),             # 巴巴罗萨过罗姆(以哥念之战)
        R("song", "jin", "feud", value=50),
        R("xixia", "jin", "tribute"), R("goryeo", "jin", "tribute"), R("dai_viet", "song", "tribute"),
        R("khamag", "tatars", "feud", value=50),
        R("khamag", "keraites", "alliance"),        # 铁木真与王罕
        R("keraites", "naimans", "feud"),
        R("khwarazm", "qara_khitai", "tribute"), R("west_karakhanid", "qara_khitai", "tribute"),
        R("east_karakhanid", "qara_khitai", "tribute"), R("qocho", "qara_khitai", "tribute"),
        R("bulgaria", "byzantium", "war", "夺城"),  # 阿森兄弟起兵
        R("almohad", "portugal", "war", "夺城"),    # 1190 年穆瓦希德北伐葡萄牙
        R("castile", "leon", "feud"),
        R("ghurid", "chahamana", "feud"),
        R("chola", "pandya", "feud"),
    ],
    "lingao": [
        R("later_jin", "ming", "war", "夺城"),
        R("later_jin", "ming", "feud", value=50),
        R("joseon", "ming", "tribute"), R("ryukyu", "ming", "tribute"),
        R("ayutthaya", "ming", "tribute"), R("dai_viet", "ming", "tribute"),
        R("ming", "chahar", "friend"),              # 明给察哈尔市赏，共御后金
        R("chahar", "later_jin", "feud"),
        R("hre", "denmark", "war", "夺城"),         # 三十年战争丹麦阶段
        R("sweden", "poland", "war", "夺城"),       # 瑞波战争
        R("england", "france", "war", "夺城"),      # 拉罗谢尔
        R("england", "spain", "war", "夺城"),
        R("dutch", "spain", "war", "夺城"),         # 八十年战争
        R("england", "dutch", "alliance"),          # 南安普敦条约
        R("spain", "hre", "alliance"),              # 哈布斯堡两家
        R("ottoman", "persia", "war", "夺城"),      # 巴格达之争
        R("mughal", "persia", "feud"),              # 坎大哈之争
        R("ottoman", "crimea", "vassal"), R("ottoman", "wallachia", "vassal"),
        R("ottoman", "moldavia", "vassal"), R("ottoman", "transylvania", "vassal"),
        R("russia", "poland", "feud"),              # 斯摩棱斯克之争
    ],
}


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records  # noqa: E402
    goals = {"夺城", "收复", "争法理", "劫掠", "侵略", "争夺资源", "吞并", "迫使附庸"}
    for era_id, items in RELATIONS.items():
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        ids = {p["id"] for p in data["polities"]}
        for r in items:
            if r["a"] not in ids or r["b"] not in ids:
                raise SystemExit("%s：%s / %s 不在本档" % (era_id, r["a"], r["b"]))
            if r.get("goal") and r["goal"] not in goals:
                raise SystemExit("%s：未知战争目标 %s" % (era_id, r["goal"]))
        data["relations"] = items
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        print(era_id, len(items), "条")


if __name__ == "__main__":
    main()
