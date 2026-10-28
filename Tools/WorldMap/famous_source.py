"""各时代剧本的名将名臣(用户 2026-10-29："我三国剧本里面的名人诸葛亮关羽张飞什么的呢""每个时代的名人你不能少啊")：
写进 WorldMaps/eras/*.json 各国的 "famous"，开局生在都城，进世界后任官、统兵、坐镇，并记成名将名臣(见 WorldMapScenario.SeatFamous)。

每人：fam 姓、giv 名("简体|英文"，西方人姓留空、名写全名)、age 开局年龄、sex("f" 女)、
kind general 名将 / minister 名臣、rank 0 良才 1 名 2 国柱、
post chief 首席朝官(丞相、首辅、维齐尔……) / core 中枢要职 / div 部寺 / general 统一支军 / 城 id 坐镇该城。
只收开局时在世的(三国 230 年关羽、张飞、赵云已故，不收)。改完跑 add_traditional.py 与 check_eras.py。

用法：python -I -X utf8 famous_source.py <模组目录>
"""
import json
import os
import sys


def F(fam, giv, age, kind, rank, post, sex=""):
    e = {"fam": fam, "giv": giv, "age": age, "kind": kind, "rank": rank, "post": post}
    if sex:
        e["sex"] = sex
    return e


def W(name, age, kind, rank, post, sex=""):
    """西方、西亚、天竺人物：不分姓名，全名写在名里"""
    return F("", name, age, kind, rank, post, sex)


G, M = "general", "minister"

FAMOUS = {
    "warring_states": {
        "qin": [F("白|Bai", "起|Qi", 72, G, 2, "general"), F("范|Fan", "雎|Ju", 46, M, 2, "chief"),
                F("王|Wang", "龁|He", 50, G, 1, "general"), F("蒙|Meng", "骜|Ao", 48, G, 1, "general"),
                F("王|Wang", "翦|Jian", 30, G, 0, "general")],
        "zhao": [F("廉|Lian", "颇|Po", 67, G, 2, "general"), F("蔺|Lin", "相如|Xiangru", 60, M, 2, "chief"),
                 F("赵|Zhao", "胜|Sheng", 48, M, 1, "core"), F("赵|Zhao", "括|Kuo", 30, G, 0, "general"),
                 F("李|Li", "牧|Mu", 30, G, 1, "datong")],
        "wei": [F("魏|Wei", "无忌|Wuji", 40, G, 2, "general")],
        "chu": [F("黄|Huang", "歇|Xie", 54, M, 2, "chief")],
        "han": [F("韩|Han", "非|Fei", 20, M, 0, "core")],
        "qi": [F("鲁|Lu", "仲连|Zhonglian", 45, M, 1, "core")],
        "rome": [W("盖乌斯·杜伊利乌斯|Gaius Duilius", 40, G, 1, "general"),
                 W("马库斯·阿提利乌斯·雷古卢斯|Marcus Atilius Regulus", 47, G, 1, "general")],
        "carthage": [W("大汉诺|Hanno the Great", 40, M, 1, "chief"), W("汉尼拔·吉斯戈|Hannibal Gisco", 50, G, 1, "general")],
        "syracuse": [W("阿基米德|Archimedes", 27, M, 2, "core")],
        "maurya": [W("罗陀笈多|Radhagupta", 55, M, 1, "chief")],
    },
    "roman_empire": {
        "han": [F("邓|Deng", "骘|Zhi", 50, G, 1, "chief"), F("杨|Yang", "震|Zhen", 58, M, 2, "core"),
                F("张|Zhang", "衡|Heng", 39, M, 1, "div"), F("蔡|Cai", "伦|Lun", 56, M, 1, "div"),
                F("虞|Yu", "诩|Xu", 45, G, 1, "wuwei"), F("班|Ban", "勇|Yong", 35, G, 1, "general")],
        "rome": [W("卢西乌斯·奎伊图斯|Lusius Quietus", 50, G, 2, "general"),
                 W("昆图斯·马尔基乌斯·图尔博|Quintus Marcius Turbo", 45, G, 1, "general"),
                 W("普布利乌斯·阿奇利乌斯·阿提安努斯|Publius Acilius Attianus", 55, M, 1, "chief"),
                 W("塔西佗|Tacitus", 61, M, 1, "core"), W("苏埃托尼乌斯|Suetonius", 48, M, 0, "div")],
    },
    "three_kingdoms": {
        "shu": [F("诸葛|Zhuge", "亮|Liang", 49, M, 2, "chief"), F("蒋|Jiang", "琬|Wan", 40, M, 1, "core"),
                F("费|Fei", "祎|Yi", 35, M, 1, "div"), F("董|Dong", "允|Yun", 35, M, 0, "div"),
                F("李|Li", "严|Yan", 45, M, 1, "core"), F("魏|Wei", "延|Yan", 55, G, 2, "hanzhong"),
                F("姜|Jiang", "维|Wei", 28, G, 1, "general"), F("王|Wang", "平|Ping", 45, G, 1, "general"),
                F("马|Ma", "岱|Dai", 45, G, 0, "general"), F("马|Ma", "忠|Zhong", 40, G, 1, "kunming")],
        "wei": [F("司马|Sima", "懿|Yi", 51, G, 2, "general"), F("曹|Cao", "真|Zhen", 45, G, 2, "general"),
                F("张|Zhang", "郃|He", 63, G, 2, "general"), F("郭|Guo", "淮|Huai", 43, G, 1, "general"),
                F("满|Man", "宠|Chong", 60, G, 1, "shouchun"), F("陈|Chen", "群|Qun", 45, M, 2, "chief"),
                F("华|Hua", "歆|Xin", 73, M, 1, "core"), F("刘|Liu", "晔|Ye", 60, M, 1, "div"),
                F("蒋|Jiang", "济|Ji", 42, M, 1, "div"), F("邓|Deng", "艾|Ai", 33, G, 0, "general")],
        "wu": [F("陆|Lu", "逊|Xun", 47, G, 2, "wuchang"), F("顾|Gu", "雍|Yong", 62, M, 2, "chief"),
               F("张|Zhang", "昭|Zhao", 74, M, 1, "core"), F("诸葛|Zhuge", "瑾|Jin", 56, G, 1, "general"),
               F("朱|Zhu", "然|Ran", 48, G, 1, "general"), F("全|Quan", "琮|Cong", 32, G, 0, "general"),
               F("步|Bu", "骘|Zhi", 50, M, 1, "div"), F("吕|Lü", "岱|Dai", 69, G, 1, "guangzhou"),
               F("诸葛|Zhuge", "恪|Ke", 27, M, 0, "div")],
        "rome": [W("卡西乌斯·狄奥|Cassius Dio", 75, M, 1, "core")],
    },
    "high_tang": {
        "tang": [F("李|Li", "林甫|Linfu", 67, M, 1, "chief"), F("杨|Yang", "国忠|Guozhong", 45, M, 0, "core"),
                 F("高|Gao", "仙芝|Xianzhi", 47, G, 2, "kucha"), F("哥舒|Geshu", "翰|Han", 51, G, 2, "lanzhou"),
                 F("安|An", "禄山|Lushan", 47, G, 1, "beijing"), F("郭|Guo", "子仪|Ziyi", 53, G, 1, "general"),
                 F("李|Li", "光弼|Guangbi", 42, G, 1, "general"), F("封|Feng", "常清|Changqing", 45, G, 1, "general"),
                 F("颜|Yan", "真卿|Zhenqing", 41, M, 0, "div"), F("王|Wang", "维|Wei", 49, M, 0, "div")],
        "abbasid": [W("阿布·穆斯林|Abu Muslim", 32, G, 2, "merv"), W("阿布·萨拉马|Abu Salama", 50, M, 1, "chief")],
    },
    "charlemagne": {
        "francia": [W("阿尔昆|Alcuin", 65, M, 2, "core"), W("艾因哈德|Einhard", 25, M, 0, "div"),
                    W("热罗纳的纪尧姆|William of Gellone", 45, G, 1, "toulouse")],
        "abbasid": [W("贾法尔·巴尔马克|Ja'far al-Barmaki", 33, M, 2, "chief"), W("叶海亚·巴尔马克|Yahya ibn Khalid", 62, M, 1, "core"),
                    W("法德勒·巴尔马克|al-Fadl ibn Yahya", 34, M, 1, "merv"), W("哈尔塔玛|Harthama ibn A'yan", 45, G, 1, "general")],
        "tang": [F("贾|Jia", "耽|Dan", 70, M, 1, "chief"), F("杜|Du", "佑|You", 65, M, 1, "yangzhou"),
                 F("韦|Wei", "皋|Gao", 54, G, 2, "chengdu")],
        "byzantium": [W("斯陶拉基奥斯|Staurakios", 50, M, 1, "chief")],
    },
    "crusades": {
        "song": [F("留|Liu", "正|Zheng", 61, M, 1, "chief"), F("赵|Zhao", "汝愚|Ruyu", 50, M, 1, "core"),
                 F("辛|Xin", "弃疾|Qiji", 50, G, 1, "general"), F("朱|Zhu", "熹|Xi", 60, M, 1, "quanzhou"),
                 F("陆|Lu", "游|You", 65, M, 0, "div"), F("叶|Ye", "适|Shi", 40, M, 0, "div"),
                 F("吴|Wu", "挺|Ting", 52, G, 1, "hanzhong")],
        "jin": [F("完颜|Wanyan", "襄|Xiang", 50, G, 2, "chief"), F("完颜|Wanyan", "宗浩|Zonghao", 45, G, 1, "general")],
        "ayyubid": [W("阿迪勒|al-Adil", 45, G, 2, "general"), W("塔奇丁|Taqi al-Din", 40, G, 1, "general"),
                    W("法迪勒|al-Qadi al-Fadil", 55, M, 2, "chief"), W("伊马德丁|Imad al-Din al-Isfahani", 65, M, 1, "div")],
        "england": [W("威廉·马歇尔|William Marshal", 44, G, 2, "general"), W("威廉·朗香|William Longchamp", 40, M, 1, "chief")],
        "jerusalem": [W("蒙费拉的康拉德|Conrad of Montferrat", 44, G, 1, "tyre")],
        "ghurid": [W("穆罕默德·古里|Muhammad of Ghor", 41, G, 2, "general"), W("库特布丁·艾巴克|Qutb ud-Din Aibak", 40, G, 1, "lahore")],
        "khamag": [F("", "木华黎|Muqali", 20, G, 1, "general"), F("", "博尔术|Bo'orchu", 28, G, 1, "general")],
    },
    "lingao": {
        "ming": [F("韩|Han", "爌|Kuang", 64, M, 1, "chief"), F("徐|Xu", "光启|Guangqi", 66, M, 1, "div"),
                 F("毕|Bi", "自严|Ziyan", 59, M, 0, "div"), F("袁|Yuan", "崇焕|Chonghuan", 44, G, 2, "general"),
                 F("孙|Sun", "承宗|Chengzong", 65, G, 1, "general"), F("秦|Qin", "良玉|Liangyu", 54, G, 1, "chongqing", "f"),
                 F("毛|Mao", "文龙|Wenlong", 52, G, 1, "general"), F("卢|Lu", "象升|Xiangsheng", 28, G, 0, "general"),
                 F("孙|Sun", "传庭|Chuanting", 35, G, 0, "general"), F("洪|Hong", "承畴|Chengchou", 35, G, 0, "general"),
                 F("祖|Zu", "大寿|Dashou", 49, G, 0, "general"), F("曹|Cao", "文诏|Wenzhao", 40, G, 0, "general")],
        "later_jin": [F("范|Fan", "文程|Wencheng", 31, M, 1, "chief"), F("", "扬古利|Yangguli", 56, G, 1, "general")],
        "joseon": [F("崔|Choe", "鸣吉|Myeong-gil", 42, M, 1, "chief"), F("林|Im", "庆业|Gyeong-eop", 34, G, 1, "general")],
        "japan": [F("土井|Doi", "利胜|Toshikatsu", 55, M, 1, "chief")],
        "ottoman": [W("胡斯雷夫帕夏|Hüsrev Pasha", 45, G, 1, "chief")],
        "persia": [W("伊玛目库利汗|Imam-Quli Khan", 50, G, 2, "fars")],
        "mughal": [W("阿萨夫汗|Asaf Khan", 59, M, 2, "chief"), W("马哈巴特汗|Mahabat Khan", 58, G, 2, "general")],
        "hre": [W("华伦斯坦|Albrecht von Wallenstein", 44, G, 2, "general")],
        "bavaria": [W("蒂利伯爵|Count of Tilly", 69, G, 2, "general")],
        "sweden": [W("阿克塞尔·乌克森谢纳|Axel Oxenstierna", 45, M, 2, "chief")],
        "france": [W("黎塞留|Cardinal Richelieu", 43, M, 2, "chief")],
        "spain": [W("奥利瓦雷斯|Count-Duke of Olivares", 41, M, 2, "chief"), W("斯皮诺拉|Ambrogio Spinola", 58, G, 2, "general")],
        "england": [W("白金汉公爵|Duke of Buckingham", 35, M, 1, "chief")],
        "dutch": [W("皮特·海因|Piet Hein", 50, G, 1, "general")],
        "russia": [W("菲拉列特牧首|Patriarch Filaret", 74, M, 2, "chief")],
        "poland": [W("科涅茨波尔斯基|Stanisław Koniecpolski", 37, G, 2, "general")],
    },
}


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records  # noqa: E402
    with open(os.path.join(root, "WorldMaps", "cities.json"), encoding="utf-8") as f:
        cities = {city["id"] for city in json.load(f)["cities"]}
    for era_id, realms in FAMOUS.items():
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        by_id = {p["id"]: p for p in data["polities"]}
        for pid, people in realms.items():
            if pid not in by_id:
                raise SystemExit("%s 没有 %s" % (era_id, pid))
            for person in people:
                post = person["post"]
                if post not in ("chief", "core", "div", "general") and post not in cities:
                    raise SystemExit("%s %s 的任所 %s 不在城表上" % (era_id, person["giv"], post))
            by_id[pid]["famous"] = people
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        print(era_id, sum(len(v) for v in realms.values()), "人")


if __name__ == "__main__":
    main()
