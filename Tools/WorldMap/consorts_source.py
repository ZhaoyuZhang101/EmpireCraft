"""各时代剧本君主(及储君)的后妃(用户 2026-10-30："还有为什么妃子和配偶没按历史来")：
写进 WorldMaps/eras/*.json 各国 "kin" 的末尾，rel "wife" 正妻(皇后、王后：开局与丈夫结为夫妻，皇后位、外戚按她算)、
"consort" 妃嫔(记为丈夫的妾室，坐进后宫妃位)；子女加 "mo" 生母序号(后宫争储按生母算宠妃)。见 WorldMapScenario.CreateKin、Marry。
资料取维基百科、维基数据(可再分发)；只收开局那年在世、已成婚(入宫)的。

每人：fam 姓、giv 名("简体|英文"，脚本补繁体；名不传的写"氏"，如周皇后写 周|Zhou + 氏|Shi；西方人姓留空、名写全名)、age 开局年龄、
of 丈夫(不写为君主；写宗室的名，如太子"亨")、mother_of 所生子女(宗室的名)、
same 与哪位后妃同一娘家(写她的名，须在前面)、house 娘家是哪国的君主家(本档的国 id)、
kin 本国名臣里哪几位是她的娘家人(写名臣的姓名，如"杨国忠"：外戚就连得上)。
宗室 kin 是手写的；本脚本每次先删掉旧的后妃、生母、娘家序号再重写，可反复跑。famous_source.py 重写名臣时留着 "in"。

用法：python -I -X utf8 consorts_source.py <模组目录>
"""
import json
import os
import sys


def Wife(fam, giv, age, of=None, mother_of=(), same=None, house="", kin=()):
    return {"rel": "wife", "fam": fam, "giv": giv, "age": age, "of": of, "mother_of": list(mother_of), "same": same,
            "house": house, "kin": list(kin)}


def Consort(fam, giv, age, of=None, mother_of=(), same=None, house="", kin=()):
    item = Wife(fam, giv, age, of, mother_of, same, house, kin)
    item["rel"] = "consort"
    return item


CONSORTS = {
    "warring_states": {
        # 秦太子安国君：正夫人华阳夫人(芈姓，楚人，无子)，夏姬生异人(子楚)
        "qin": [Wife("芈|Mi", "氏|Shi", 35, of="柱"), Consort("夏|Xia", "姬|Ji", 40, of="柱", mother_of=["异人"])],
        "seleucid": [Wife("", "劳迪丝|Laodice", 25, mother_of=["塞琉古"])],
        # 阿育王：正后阿桑迪米特拉；帕德玛瓦蒂生鸠那罗；德维(毗底沙商人之女，留在毗底沙，未必是正式婚姻)生摩哂陀
        "maurya": [Wife("", "阿桑迪米特拉|Asandhimitra", 40), Consort("", "帕德玛瓦蒂|Padmavati", 30, mother_of=["鸠那罗"]),
                   Consort("", "德维|Devi", 45, mother_of=["摩哂陀"])],
        # 安提柯二世：王后菲拉是塞琉古一世之女，安条克二世的姑母
        "macedon": [Wife("", "菲拉|Phila", 40, mother_of=["德米特里"], house="seleucid")],
        # 希伦二世：王后菲利斯提斯(娶勒普提涅斯之女，据钱币、铭文认作她)；史书没写她是革隆的生母，不记
        "syracuse": [Wife("", "菲利斯提斯|Philistis", 40)],
    },
    "roman_empire": {
        "rome": [Wife("", "维比娅·萨宾娜|Vibia Sabina", 34)],
        # 汉安帝：阎皇后(元初二年立)；太子保生母李氏已为阎后所害
        "han": [Wife("阎|Yan", "姬|Ji", 20)],
    },
    "three_kingdoms": {
        # 魏明帝：毛皇后(太和元年立)；郭夫人(后为明元郭皇后)
        "wei": [Wife("毛|Mao", "氏|Shi", 22), Consort("郭|Guo", "氏|Shi", 18)],
        # 后主：张皇后(张飞长女，敬哀皇后)；王贵人生太子璿
        "shu": [Wife("张|Zhang", "氏|Shi", 22), Consort("王|Wang", "氏|Shi", 24, mother_of=["璿"])],
        # 孙权：步夫人宠冠后庭、宫内称皇后，与步骘同族；琅邪王夫人生孙和
        "wu": [Wife("步|Bu", "练师|Lianshi", 40, kin=["步骘"]), Consort("王|Wang", "氏|Shi", 30, mother_of=["和"])],
    },
    "high_tang": {
        # 玄宗：武惠妃死后不再立后，杨玉环为贵妃，礼数同皇后；杨国忠是她的从祖兄。
        # 太子亨：太子妃韦氏天宝五载已废，张良娣(太子妾)；广平王豫：正妃崔氏(天宝五载册，杨贵妃的外甥女)，沈氏(睿真皇后，妾)生适
        "tang": [Wife("杨|Yang", "玉环|Yuhuan", 31, kin=["杨国忠"]), Consort("张|Zhang", "氏|Shi", 25, of="亨"),
                 Wife("崔|Cui", "氏|Shi", 22, of="豫"), Consort("沈|Shen", "氏|Shi", 25, of="豫", mother_of=["适"])],
        "abbasid": [Wife("", "乌姆·萨拉玛|Umm Salama", 30)],
        "francia": [Wife("", "贝尔特拉达|Bertrada", 30, mother_of=["查理"])],
        "asturias": [Wife("", "埃尔梅辛达|Ermesinda", 50, mother_of=["弗鲁埃拉"])],
    },
    "charlemagne": {
        # 哈伦：祖贝达(曼苏尔之孙女)生阿明
        "abbasid": [Wife("", "祖贝达|Zubaida", 34, mother_of=["阿明"])],
        # 太子诵：太子妃萧氏已为德宗所杀，王良娣(庄宪皇后，太子妾)生纯
        "tang": [Consort("王|Wang", "氏|Shi", 37, of="诵", mother_of=["纯"])],
    },
    "crusades": {
        # 亨利六世：西西里的康斯坦丝(欧特维尔家，坦克雷德是她的侄子)
        "hre": [Wife("", "西西里的康斯坦丝|Constance of Sicily", 36, house="sicily")],
        "sicily": [Wife("", "阿切拉的西比拉|Sibylla of Acerra", 40, mother_of=["罗杰"])],
        "castile": [Wife("", "英格兰的埃莉诺|Eleanor of England", 29, mother_of=["费尔南多"], house="england")],
        "portugal": [Wife("", "阿拉贡的杜尔塞|Dulce of Aragon", 30, mother_of=["阿方索"], house="aragon")],
        "aragon": [Wife("", "卡斯蒂利亚的桑恰|Sancha of Castile", 35, mother_of=["佩德罗"], house="castile")],
        "hungary": [Wife("", "法兰西的玛格丽特|Margaret of France", 32, house="france")],
        "poland": [Wife("", "兹诺伊莫的海伦|Helen of Znojmo", 49, mother_of=["莱谢克"], house="bohemia")],
        "denmark": [Wife("", "巴伐利亚的格特鲁德|Gertrude of Bavaria", 35)],
        "norway": [Wife("", "瑞典的玛格丽特|Margaret of Sweden", 35, house="sweden")],
        "vladimir": [Wife("", "玛丽亚·什瓦尔诺夫娜|Maria Shvarnovna", 32, mother_of=["康斯坦丁"])],
        "byzantium": [Wife("", "匈牙利的玛格丽特|Margaret of Hungary", 15, house="hungary")],
        # 居伊是靠妻子西比拉女王称王的；她 1190 年秋死于阿卡的疫病
        "jerusalem": [Wife("", "耶路撒冷的西比拉|Sibylla of Jerusalem", 30)],
        "khwarazm": [Wife("", "秃儿罕可敦|Terken Khatun", 35, mother_of=["摩诃末"])],
        # 光宗：李皇后(凤娘)生宁宗扩
        "song": [Wife("李|Li", "凤娘|Fengniang", 46, mother_of=["扩"])],
        "xixia": [Wife("罗|Luo", "氏|Shi", 40, mother_of=["纯佑"])],
        # 铁木真：孛儿帖(弘吉剌部)生术赤、察合台、窝阔台
        "khamag": [Wife("弘吉剌|Khongirad", "孛儿帖|Börte", 29, mother_of=["术赤", "察合台", "窝阔台"])],
    },
    "lingao": {
        # 崇祯：周皇后；崇祯元年四月同日封田贵妃(秀英)、袁淑妃
        "ming": [Wife("周|Zhou", "氏|Shi", 17), Consort("田|Tian", "秀英|Xiuying", 18), Consort("袁|Yuan", "氏|Shi", 17)],
        # 皇太极：大福晋哲哲；布木布泰是她的侄女(豪格生母继妃乌拉那拉氏可能在天命年间已故，不收)
        "later_jin": [Wife("博尔济吉特|Borjigit", "哲哲|Jerjer", 28), Consort("博尔济吉特|Borjigit", "布木布泰|Bumbutai", 15, same="哲哲")],
        # 仁祖：仁烈王后韩氏生昭显世子溰、凤林大君淏
        "joseon": [Wife("韩|Han", "氏|Shi", 34, mother_of=["溰", "淏"])],
        # 后水尾天皇：中宫德川和子(家光之妹)；家光：御台所鹰司孝子
        "japan": [Wife("德川|Tokugawa", "和子|Masako", 21, house="tokugawa")],
        "tokugawa": [Wife("鹰司|Takatsukasa", "孝子|Takako", 26)],
        # 沙贾汗：慕塔芝·玛哈(阿萨夫汗之女)生四子
        "mughal": [Wife("", "慕塔芝·玛哈|Mumtaz Mahal", 35, mother_of=["达拉·希科", "沙·舒贾", "奥朗则布", "穆拉德·巴克什"],
                        kin=["阿萨夫汗"])],
        "hre": [Wife("", "埃莉奥诺拉·贡扎加|Eleonora Gonzaga", 30)],
        "spain": [Wife("", "法国的伊丽莎白|Elisabeth of France", 26, house="france")],
        "france": [Wife("", "奥地利的安妮|Anne of Austria", 27, house="spain")],
        "england": [Wife("", "亨丽埃塔·玛丽亚|Henrietta Maria", 19, house="france")],
        "denmark": [Wife("", "基尔斯滕·蒙克|Kirsten Munk", 30)],
        "sweden": [Wife("", "玛丽亚·埃莱奥诺拉|Maria Eleonora", 29, mother_of=["克里斯蒂娜"], house="brandenburg")],
        "poland": [Wife("", "奥地利的康斯坦丝|Constance of Austria", 40, mother_of=["约翰·卡齐米日"], house="hre")],
        "russia": [Wife("", "叶夫多基娅·斯特列什涅娃|Eudoxia Streshneva", 20)],
        "brandenburg": [Wife("", "伊丽莎白·夏洛特|Elizabeth Charlotte", 31)],
        "bavaria": [Wife("", "伊丽莎白·雷娜塔|Elisabeth Renata", 54)],
        "saxony": [Wife("", "马格达莱妮·西比勒|Magdalene Sibylle", 42)],
        "dutch": [Wife("", "阿玛利亚|Amalia of Solms-Braunfels", 26)],
        "transylvania": [Wife("", "勃兰登堡的卡塔琳娜|Catherine of Brandenburg", 26, house="brandenburg")],
    },
}


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records, triple  # noqa: E402
    for era_id, realms in CONSORTS.items():
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        by_id = {p["id"]: p for p in data["polities"]}
        # 先清掉各国旧的后妃、生母、娘家序号(这回删了的人不留在 json 里)
        for polity in data["polities"]:
            if "kin" in polity:
                polity["kin"] = [k for k in polity["kin"] if k.get("rel") not in ("wife", "consort")]
                for k in polity["kin"]:
                    k.pop("mo", None)
            for person in polity.get("famous", []):
                person.pop("in", None)
        count = 0
        for pid, spouses in realms.items():
            if pid not in by_id:
                raise SystemExit("%s 没有 %s" % (era_id, pid))
            polity = by_id[pid]
            if not polity.get("ruler"):
                raise SystemExit("%s %s 没有君主" % (era_id, pid))
            kin = polity.get("kin", [])
            famous = polity.get("famous", [])

            def find(given):
                hits = [i for i, k in enumerate(kin) if k.get("giv", "").split("|")[0] == given]
                if len(hits) != 1:
                    raise SystemExit("%s %s：宗室里找不到唯一的 %s" % (era_id, pid, given))
                return hits[0]

            base = len(kin)
            out = []
            for spouse in spouses:
                at = base + len(out)
                item = {"fam": triple(spouse["fam"]), "giv": triple(spouse["giv"]), "age": spouse["age"], "rel": spouse["rel"]}
                if spouse["of"]:
                    item["of"] = find(spouse["of"])
                if spouse["same"]:
                    earlier = [i for i, s in enumerate(spouses[:len(out)]) if s["giv"].split("|")[0] == spouse["same"]]
                    if len(earlier) != 1:
                        raise SystemExit("%s %s：%s 的同族 %s 要写在她前面" % (era_id, pid, spouse["giv"], spouse["same"]))
                    item["in"] = base + earlier[0]
                if spouse["house"]:
                    if spouse["house"] not in by_id or not by_id[spouse["house"]].get("ruler"):
                        raise SystemExit("%s %s：娘家 %s 不是本档有君主的国" % (era_id, pid, spouse["house"]))
                    item["house"] = spouse["house"]
                for child in spouse["mother_of"]:
                    kin[find(child)]["mo"] = at
                for name in spouse["kin"]:
                    hits = [p for p in famous if p.get("fam", "").split("|")[0] + p.get("giv", "").split("|")[0] == name]
                    if len(hits) != 1:
                        raise SystemExit("%s %s：名臣里找不到唯一的 %s" % (era_id, pid, name))
                    hits[0]["in"] = at
                out.append(item)
            polity["kin"] = kin + out
            count += len(out)
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        print(era_id, count, "人")


if __name__ == "__main__":
    main()
