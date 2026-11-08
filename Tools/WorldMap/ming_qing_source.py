"""明清鼎革(1638 明崇祯十一年、清崇德三年)：以 eras/lingao.json 为底稿改写，写成 eras/ming_qing.json。

版图照通行的 1638 年明朝全图：清有辽东、白山黑水、漠南(皇太极 1636 称帝、1637 降朝鲜)，明守关宁一线；
卫拉特分准噶尔一带的厄鲁特与青海的和硕特(固始汗 1637 据青海)，叶尔羌在塔里木，藏巴汗据卫藏，索伦在黑龙江上游。
事件走到甲申之变、清军入关与南明三帝：南明君臣的结局看城丢没丢，不按年写死，清军没到就不会死；别的 1644 之后交给 AI。

新城 from 写 1629：锦州、宁远、山海关实际早有，只是临高剧本(1628)没带这几座城，开局版图和存档都不动它；
要给临高也加，把 from 改成真实年份，再把它们点名给明(不然辽东两城会归后金)。

用法：python -I -X utf8 ming_qing_source.py <模组目录>，之后跑 check_eras.py、check_scenario.py。
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from preset_tools import T, death, event, famous, load_era, person, polity, rel_step, relation, update_cities, write_era  # noqa: E402

NEW_CITIES = [
    {"id": "shanhaiguan", "zh": "山海关", "en": "Shanhaiguan", "x": 119.72, "y": 39.98, "r": "youyan", "k": 3, "from": 1629},
    {"id": "ningyuan", "zh": "宁远", "en": "Ningyuan", "x": 120.72, "y": 40.6, "r": "liaodong", "k": 3, "from": 1629},
    {"id": "jinzhou", "zh": "锦州", "en": "Jinzhou", "x": 121.13, "y": 41.1, "r": "liaodong", "k": 3, "from": 1629},
    {"id": "hobok", "zh": "和布克赛尔", "en": "Hobok Sair", "x": 85.73, "y": 46.79, "r": "dzungaria", "k": 2, "from": 1629},
    {"id": "kokonor", "zh": "察罕托罗亥", "en": "Chaghan Tolgoi", "x": 100.2, "y": 37.2, "r": "qinghai", "k": 2, "from": 1629},
    {"id": "yaksa", "zh": "雅克萨", "en": "Albazin", "x": 124.2, "y": 53.4, "r": "manchuria", "k": 2, "from": 1629},
    {"id": "ninguta", "zh": "宁古塔", "en": "Ninguta", "x": 129.46, "y": 44.33, "r": "manchuria", "k": 2, "from": 1629},
]
# 1634 年盛京、兴京定名
ALTS = {
    "shenyang": ["1633|沈阳|Shenyang", "1911|盛京|Mukden"],
    "hetuala": ["1633|赫图阿拉|Hetu Ala", "1911|兴京|Xingjing"],
}

MING_FAMOUS = [
    famous("杨|Yang", "嗣昌|Sichang", 50, "minister", 1, "chief"),
    famous("史|Shi", "可法|Kefa", 37, "minister", 1, "nanjing"),
    famous("刘|Liu", "宗周|Zongzhou", 60, "minister", 0, "div"),
    famous("黄|Huang", "道周|Daozhou", 53, "minister", 0, "div"),
    famous("倪|Ni", "元璐|Yuanlu", 45, "minister", 0, "div"),
    famous("卢|Lu", "象升|Xiangsheng", 38, "general", 2, "general"),
    famous("洪|Hong", "承畴|Chengchou", 45, "general", 1, "general"),
    famous("孙|Sun", "传庭|Chuanting", 45, "general", 1, "xian"),
    famous("祖|Zu", "大寿|Dashou", 59, "general", 1, "jinzhou"),
    famous("吴|Wu", "三桂|Sangui", 26, "general", 1, "shanhaiguan"),
    famous("秦|Qin", "良玉|Liangyu", 64, "general", 1, "chongqing", sex="f"),
    famous("左|Zuo", "良玉|Liangyu", 39, "general", 1, "general"),
    famous("郑|Zheng", "芝龙|Zhilong", 34, "general", 1, "quanzhou"),
    famous("曹|Cao", "变蛟|Bianjiao", 29, "general", 0, "general"),
    famous("黄|Huang", "得功|Degong", 44, "general", 0, "general"),
    # 流寇：开局还没坐大，不任官，到时候由事件起兵
    famous("李|Li", "自成|Zicheng", 32, "general", 1, "none"),
    famous("刘|Liu", "宗敏|Zongmin", 31, "general", 0, "none"),
    famous("牛|Niu", "金星|Jinxing", 43, "minister", 0, "none"),
    famous("张|Zhang", "献忠|Xianzhong", 32, "general", 1, "none"),
    famous("李|Li", "定国|Dingguo", 18, "general", 0, "none"),
]

QING = polity(
    "qing", "清", "Qing", "Manzhou", "shenyang",
    r=["liaodong", "manchuria", "monan"], cities=["baotou"], emp=True, grand=True, banner=True, hero=True, core="manchuria",
    era_name="崇德|Chongde", era_year=3, dynasty="aisin_gioro",
    ruler=person("爱新觉罗|Aisin Gioro", "皇太极|Hong Taiji", 46, f="ag01"),
    kin=[
        person("博尔济吉特|Borjigit", "哲哲|Jerjer", 38, rel="wife"),
        person("博尔济吉特|Borjigit", "海兰珠|Harjol", 29, rel="consort", **{"in": 0}),
        person("博尔济吉特|Borjigit", "布木布泰|Bumbutai", 25, rel="consort", **{"in": 0}),
        person(None, "豪格|Hooge", 29, rel="son"),
        person("爱新觉罗|Aisin Gioro", "福临|Fulin", 0, rel="son", heir=True, mo=2),
        person(None, "代善|Daisan", 55, rel="brother"),
        person(None, "阿济格|Ajige", 33, rel="brother"),
        person(None, "多尔衮|Dorgon", 26, rel="brother"),
        person(None, "多铎|Dodo", 24, rel="brother"),
    ],
    famous=[
        famous("范|Fan", "文程|Wencheng", 41, "minister", 1, "chief"),
        famous("宁|Ning", "完我|Wanwo", 45, "minister", 0, "core"),
        famous("爱新觉罗|Aisin Gioro", "济尔哈朗|Jirgalang", 39, "general", 1, "general"),
        famous("孔|Kong", "有德|Youde", 36, "general", 1, "general"),
        famous("尚|Shang", "可喜|Kexi", 34, "general", 1, "general"),
        famous("耿|Geng", "仲明|Zhongming", 34, "general", 0, "general"),
        famous("瓜尔佳|Gūwalgiya", "鳌拜|Oboi", 28, "general", 0, "general"),
    ],
)

# 东北亚与卫拉特诸部(青海、准噶尔、漠北)
INNER_ASIA = [
    polity("oirat", "厄鲁特", "Oirat", "Youmu", "hobok", r=["dzungaria"], cities=["khovd"],
           ruler=person("绰罗斯|Choros", "巴图尔|Erdeni Batur", 40)),
    polity("khoshut", "和硕特", "Khoshut", "Youmu", "kokonor", r=["qinghai"], cities=["dunhuang"],
           ruler=person("", "固始汗|Güshi Khan", 56)),
    polity("solon", "索伦", "Solon", "Youmu", "yaksa",
           ruler=person("", "博穆博果尔|Bombogor", 30)),
]

# 其余各国十年间换了人的：整份换人物；只老了十岁的也在这里写明，别让死人上场
PEOPLE = {
    "joseon": dict(
        ruler=person("李|Yi", "倧|Jong", 43, f="js_jeongwon"),
        kin=[person(None, "溰|Wang", 26, rel="son", heir=True), person(None, "淏|Ho", 19, rel="son"),
             person("赵|Jo", "氏|Shi", 14, rel="wife")],
        famous=[famous("崔|Choe", "鸣吉|Myeong-gil", 52, "minister", 1, "chief"),
                famous("金|Kim", "尚宪|Sang-heon", 68, "minister", 1, "div"),
                famous("林|Im", "庆业|Gyeong-eop", 44, "general", 1, "general")]),
    "japan": dict(
        ruler=person("", "兴子|Okiko", 14, sex="f"), era_name="宽永|Kan'ei", era_year=15,
        kin=[person("", "绍仁|Tsuguhito", 5, rel="brother", heir=True)],
        famous=[famous("土井|Doi", "利胜|Toshikatsu", 65, "minister", 1, "chief"),
                famous("松平|Matsudaira", "信纲|Nobutsuna", 42, "minister", 1, "core")]),
    "tokugawa": dict(
        ruler=person("德川|Tokugawa", "家光|Iemitsu", 34, f="tk02"),
        kin=[person("鹰司|Takatsukasa", "孝子|Takako", 36, rel="wife"), person("", "保科正之|Hoshina Masayuki", 27, rel="brother")]),
    "ryukyu": dict(ruler=person("尚|Sho", "丰|Ho", 48)),
    "khalkha": dict(ruler=person("孛儿只斤|Borjigin", "衮布|Gombodorj", 44)),
    "tsang": dict(ruler=person("", "噶玛丹迥旺布|Karma Tenkyong Wangpo", 32), r=["tibet"]),
    "yarkent": dict(ruler=person("察合台|Chagatai", "阿不都剌|Abdullah", 29)),
    "kazakh": dict(ruler=person("", "杨吉尔|Jangir", 28)),
    "bukhara": dict(ruler=person("", "伊玛目·库里汗|Imam Quli Khan", 56)),
    "persia": dict(
        ruler=person("萨法维|Safavid", "萨菲|Safi", 27), kin=[],
        famous=[famous("", "萨鲁·塔基|Saru Taqi", 60, "minister", 1, "chief"),
                famous("", "鲁斯塔姆汗|Rustam Khan", 45, "general", 1, "general")]),
    "mughal": dict(
        ruler=person("帖木儿|Timurid", "沙贾汗|Shah Jahan", 46, f="mu04"),
        kin=[person(None, "达拉·希科|Dara Shikoh", 23, rel="son", heir=True), person(None, "沙·舒贾|Shah Shuja", 22, rel="son"),
             person(None, "奥朗则布|Aurangzeb", 20, rel="son"), person(None, "穆拉德·巴克什|Murad Bakhsh", 14, rel="son")],
        famous=[famous("", "阿萨夫汗|Asaf Khan", 69, "minister", 2, "chief")]),
    "bijapur": dict(ruler=person("", "穆罕默德·阿迪勒·沙|Mohammed Adil Shah", 25)),
    "golconda": dict(ruler=person("", "阿卜杜拉·库特卜·沙|Abdullah Qutb Shah", 24)),
    "kandy": dict(ruler=person("", "拉贾辛哈|Rajasinha", 30)),
    "toungoo": dict(cap="ava", ruler=person("", "他隆|Thalun", 54)),
    "lan_xang": dict(ruler=person("", "苏里亚·旺萨|Sourigna Vongsa", 20)),
    "ayutthaya": dict(ruler=person("", "巴沙通|Prasat Thong", 39)),
    "dai_viet": dict(ruler=person("黎|Le", "维祺|Duy Ky", 31), era_name="阳和|Duong Hoa", era_year=4),
    "quang_nam": dict(ruler=person("阮|Nguyen", "福澜|Phuc Lan", 37)),
    "aceh": dict(ruler=person("", "伊斯坎达尔·塔尼|Iskandar Thani", 28)),
    "mataram": dict(ruler=person("", "苏丹阿贡|Sultan Agung", 45)),
    "ottoman": dict(
        ruler=person("奥斯曼|Osman", "穆拉德|Murad", 26, f="ot14"),
        kin=[person(None, "易卜拉欣|Ibrahim", 23, rel="brother", heir=True)],
        famous=[famous("", "塔亚尔·穆罕默德帕夏|Tayyar Mehmed Pasha", 40, "general", 1, "chief")]),
    "crimea": dict(ruler=person("格莱|Giray", "巴哈迪尔|Bahadir", 36)),
    "wallachia": dict(ruler=person("", "马泰·巴萨拉布|Matei Basarab", 50)),
    "moldavia": dict(ruler=person("", "瓦西里·卢普|Vasile Lupu", 43)),
    "transylvania": dict(ruler=person("拉科奇|Rakoczi", "捷尔吉|George", 45), kin=[]),
    "ethiopia": dict(ruler=person("所罗门|Solomonic", "法西利达斯|Fasilides", 35), kin=[]),
    "hre": dict(
        ruler=person("哈布斯堡|Habsburg", "斐迪南|Ferdinand", 29, f="hb12"),
        kin=[person("", "西班牙的玛丽亚·安娜|Maria Anna of Spain", 32, rel="wife", house="spain"),
             person(None, "斐迪南|Ferdinand", 5, rel="son", heir=True, mo=0),
             person(None, "利奥波德·威廉|Leopold William", 24, rel="brother")],
        famous=[famous("", "皮科洛米尼|Piccolomini", 38, "general", 2, "general"),
                famous("", "加拉斯|Gallas", 54, "general", 1, "general")]),
    "bavaria": dict(
        ruler=person("维特尔斯巴赫|Wittelsbach", "马克西米利安|Maximilian", 65),
        kin=[person("", "奥地利的玛丽亚·安娜|Maria Anna of Austria", 28, rel="wife", house="hre"),
             person(None, "斐迪南·玛丽亚|Ferdinand Maria", 2, rel="son", heir=True, mo=0)],
        famous=[famous("", "梅西|Franz von Mercy", 41, "general", 1, "general")]),
    "saxony": dict(ruler=person("韦廷|Wettin", "约翰·格奥尔格|John George", 53),
                   kin=[person("", "马格达莱妮·西比勒|Magdalene Sibylle", 52, rel="wife")]),
    "brandenburg": dict(ruler=person("霍亨索伦|Hohenzollern", "格奥尔格·威廉|George William", 43),
                        kin=[person("", "伊丽莎白·夏洛特|Elizabeth Charlotte", 41, rel="wife"),
                             person(None, "腓特烈·威廉|Frederick William", 18, rel="son", heir=True, mo=0)]),
    "dutch": dict(ruler=person("奥兰治-拿骚|Orange-Nassau", "弗雷德里克·亨德里克|Frederick Henry", 54),
                  kin=[person("", "阿玛利亚|Amalia", 36, rel="wife"), person(None, "威廉|William", 12, rel="son", heir=True, mo=0)],
                  famous=[famous("", "特罗姆普|Maarten Tromp", 40, "general", 2, "general"),
                          famous("", "范迪门|Antonio van Diemen", 45, "minister", 1, "batavia")]),
    "spain": dict(ruler=person("哈布斯堡|Habsburg", "腓力|Philip", 33, f="sp03"),
                  kin=[person("", "法国的伊丽莎白|Elisabeth of France", 36, rel="wife", house="france"),
                       person(None, "巴尔塔萨尔·卡洛斯|Balthasar Charles", 9, rel="son", heir=True, mo=0),
                       person(None, "斐迪南|Ferdinand", 29, rel="brother")],
                  famous=[famous("", "奥利瓦雷斯|Olivares", 51, "minister", 2, "chief"),
                          famous("布拉干萨|Braganza", "若昂|John", 34, "general", 0, "lisbon")]),
    "france": dict(ruler=person("波旁|Bourbon", "路易|Louis", 37, f="cp29"),
                   kin=[person("", "奥地利的安妮|Anne of Austria", 37, rel="wife", house="spain"),
                        person(None, "路易|Louis", 0, rel="son", heir=True, mo=0),
                        person(None, "加斯东|Gaston", 30, rel="brother")],
                   famous=[famous("", "黎塞留|Richelieu", 53, "minister", 2, "chief"),
                           famous("", "马扎然|Mazarin", 36, "minister", 1, "core"),
                           famous("", "蒂雷纳|Turenne", 27, "general", 1, "general"),
                           famous("", "昂吉安公爵|Duke of Enghien", 17, "general", 1, "general")]),
    "england": dict(ruler=person("斯图亚特|Stuart", "查理|Charles", 38, f="en_james1"),
                    kin=[person("", "亨丽埃塔·玛丽亚|Henrietta Maria", 29, rel="wife", house="france"),
                         person(None, "查理|Charles", 8, rel="son", heir=True, mo=0),
                         person(None, "詹姆斯|James", 4, rel="son", mo=0)],
                    famous=[famous("", "温特沃斯|Strafford", 45, "minister", 1, "chief"),
                            famous("", "克伦威尔|Oliver Cromwell", 39, "general", 0, "none")]),
    "denmark": dict(ruler=person("奥尔登堡|Oldenburg", "克里斯蒂安|Christian", 61, f="dk_frederick2"),
                    kin=[person("", "基尔斯滕·蒙克|Kirsten Munk", 40, rel="wife"),
                         person(None, "克里斯蒂安|Christian", 35, rel="son", heir=True),
                         person(None, "腓特烈|Frederick", 29, rel="son")]),
    "sweden": dict(ruler=person("瓦萨|Vasa", "克里斯蒂娜|Christina", 12, sex="f", f="sw_g2"), kin=[],
                   famous=[famous("", "乌克森谢纳|Axel Oxenstierna", 55, "minister", 2, "chief"),
                           famous("", "巴内尔|Johan Banér", 42, "general", 2, "general"),
                           famous("", "托尔斯滕森|Lennart Torstensson", 35, "general", 1, "general")]),
    "poland": dict(ruler=person("瓦萨|Vasa", "瓦迪斯瓦夫|Wladyslaw", 43, f="sw_sig"),
                   kin=[person("", "奥地利的采奇莉亚·雷娜塔|Cecilia Renata", 27, rel="wife", house="hre"),
                        person(None, "约翰·卡齐米日|John Casimir", 29, rel="brother", heir=True)],
                   famous=[famous("", "科涅茨波尔斯基|Koniecpolski", 47, "general", 2, "general")]),
    "russia": dict(ruler=person("罗曼诺夫|Romanov", "米哈伊尔|Michael", 42),
                   kin=[person("", "叶夫多基娅·斯特列什涅娃|Eudoxia Streshneva", 30, rel="wife"),
                        person(None, "阿列克谢|Alexis", 9, rel="son", heir=True, mo=0)],
                   famous=[famous("", "切尔卡斯基|Ivan Cherkassky", 58, "minister", 1, "chief")]),
    "venice": dict(ruler=person("埃里佐|Erizzo", "弗朗切斯科|Francesco", 72)),
    "papacy": dict(ruler=person("巴尔贝里尼|Barberini", "马费奥|Maffeo", 70)),
    "tuscany": dict(ruler=person("美第奇|Medici", "斐迪南多|Ferdinando", 28)),
    "savoy": dict(ruler=person("萨伏依|Savoy", "卡洛·埃马努埃莱|Charles Emmanuel", 4)),
}

RELATIONS = [
    relation("qing", "ming", "war", goal="夺城"),
    relation("qing", "ming", "feud", value=60),
    relation("joseon", "qing", "tribute"),
    relation("joseon", "ming", "friend", value=40),
    relation("joseon", "qing", "feud", value=30),
    relation("ryukyu", "ming", "tribute"),
    relation("ayutthaya", "ming", "tribute"),
    relation("dai_viet", "ming", "tribute"),
    relation("khalkha", "qing", "feud", value=20),
    relation("qing", "solon", "feud", value=20),
    relation("oirat", "khoshut", "alliance"),
    relation("khoshut", "tsang", "feud", value=40),
    relation("oirat", "kazakh", "feud", value=40),
    relation("france", "hre", "war", goal="夺城"),
    relation("france", "spain", "war", goal="夺城"),
    relation("sweden", "hre", "war", goal="夺城"),
    relation("dutch", "spain", "war", goal="夺城"),
    relation("spain", "hre", "alliance"),
    relation("france", "sweden", "alliance"),
    relation("france", "dutch", "alliance"),
    relation("ottoman", "persia", "war", goal="夺城"),
    relation("mughal", "persia", "feud"),
    relation("ottoman", "crimea", "vassal"),
    relation("ottoman", "wallachia", "vassal"),
    relation("ottoman", "moldavia", "vassal"),
    relation("ottoman", "transylvania", "vassal"),
    relation("bijapur", "mughal", "tribute"),
    relation("golconda", "mughal", "tribute"),
    relation("russia", "poland", "feud"),
    relation("kandy", "dutch", "alliance"),
    relation("kandy", "spain", "feud", value=40),
    relation("aceh", "spain", "feud", value=40),
]


def jia_shen():
    """甲申(after 6 = 1644)：京城失守、南京另立。顺序要紧：城一易手城里的人全归新国，
    所以先迁都留住朝廷、把福王接到南京立储，宫中的人和皇帝身故之后才献城。"""
    return event(
        "甲申之变", T("三月十九日，闯军攻破北京，崇祯帝自缢于煤山，遗诏“任贼分裂朕尸，勿伤百姓一人”；南京诸臣拥立福王",
                     "On the nineteenth day of the third month the rebels storm Beijing. The Chongzhen emperor hangs himself on Coal Hill, "
                     "leaving word: \"Let the rebels tear my body apart, but harm not one of my people.\" At Nanjing the ministers raise the Prince of Fu"), 6,
        [{"kind": "alive", "a": "shun"}, {"kind": "holds_city", "a": "ming", "city": "beijing"}],
        [{"kind": "move_capital", "a": "ming", "city": "nanjing"},
         {"kind": "heir", "a": "ming", "person": "朱聿键", "city": "fuzhou"},
         {"kind": "heir", "a": "ming", "person": "朱由榔", "city": "guangzhou"},
         {"kind": "heir", "a": "ming", "person": "朱由崧"},
         {"kind": "dies", "a": "ming", "person": "朱慈烺、朱慈炯、朱慈炤", "name": T("城破后落入闯军之手，下落不明", "taken by the rebels when the capital fell, fate unknown")},
         {"kind": "dies", "a": "ming", "person": "周氏", "name": T("城破，自缢于坤宁宫", "hanged herself in the Kunning Palace as the capital fell")},
         {"kind": "dies", "a": "ming", "person": "朱由检", "name": T("自缢于煤山", "hanged himself on Coal Hill")},
         {"kind": "cede", "a": "ming", "b": "shun", "city": "beijing", "cities": ["zhuozhou", "taiyuan", "datong", "changzhi", "linfen", "anyi"]},
         {"kind": "rally", "a": "qing", "b": "ming"}])


EVENTS = [
    event("巴格达易手", T("奥斯曼军收复巴格达，两国议和", "The Ottomans retake Baghdad and make peace with Persia"), 1,
          [{"kind": "holds_city", "a": "persia", "city": "ctesiphon"}, {"kind": "at_war", "a": "ottoman", "b": "persia"}],
          [{"kind": "cede", "a": "persia", "b": "ottoman", "city": "ctesiphon", "cities": ["kufa"]},
           rel_step("ottoman", "persia", "truce")]),
    event("贾庄之战", T("清军分路破墙子岭、青山口入塞，宣大总督卢象升力战，死于巨鹿贾庄",
                       "Qing columns break through the Wall at Qiangziling and Qingshankou; Supreme Commander Lu Xiangsheng fights on and falls at Jiazhuang near Julu"), 1,
          [{"kind": "at_war", "a": "qing", "b": "ming"}],
          [{"kind": "dies", "a": "ming", "person": "卢象升", "name": T("战死于巨鹿贾庄", "fell in battle at Jiazhuang near Julu")}]),
    # 只出文字的史事：用 before 卡住年份，条件晚了凑齐也不再出
    event("济南之陷", T("清军深入山东，攻破济南，德王朱由枢被掳北去", "The Qing drive deep into Shandong and storm Jinan; the Prince of De is carried off north"), 1,
          [{"kind": "at_war", "a": "qing", "b": "ming"}, {"kind": "before", "value": 3}], []),
    event("中原大饥", T("北方连年大旱蝗灾，赤地千里，人相食，饥民蜂起", "Drought and locusts year after year leave the north bare; people eat one another and the starving rise in swarms"), 2,
          [{"kind": "strain_above", "a": "ming", "value": 60}, {"kind": "before", "value": 5}], []),
    death("田贵妃之死", ("崇祯帝宠妃田秀英病逝于承乾宫", "Consort Tian, the Chongzhen emperor's favourite, dies in the Chengqian Palace"), 4, "ming", "田秀英", ("病逝于承乾宫", "died of illness in the Chengqian Palace")),
    event("和硕特入藏", T("和硕特南下卫藏", "The Khoshut ride south into Tibet"), 1,
          [{"kind": "alive", "a": "tsang"}], [rel_step("khoshut", "tsang", "war", goal="吞并")]),
    event("清征索伦", T("清军北征索伦", "The Qing march north against the Solon"), 1,
          [{"kind": "alive", "a": "solon"}], [rel_step("qing", "solon", "war", goal="吞并")]),
    event("葡萄牙复国", T("里斯本拥立新王，葡萄牙脱离西班牙", "Lisbon acclaims a new king and Portugal breaks from Spain"), 2,
          [{"kind": "holds_city", "a": "spain", "city": "lisbon"}],
          [{"kind": "rebel", "a": "spain", "b": "portugal", "city": "lisbon", "cities": ["porto", "coimbra"],
            "name": T("葡萄牙", "Portugal"), "person": "布拉干萨若昂"}]),
    event("张献忠复起", T("张献忠于谷城复叛，奇袭襄阳，襄王朱翊铭遇害", "Zhang Xianzhong rises again at Gucheng and takes Xiangyang by surprise; the Prince of Xiang is killed"), 3,
          [{"kind": "holds_city", "a": "ming", "city": "xiangyang"}, {"kind": "strain_above", "a": "ming", "value": 40}],
          [{"kind": "rebel", "a": "ming", "b": "xi", "city": "xiangyang", "name": T("西", "Xi"), "person": "张献忠"},
           {"kind": "cede", "a": "ming", "b": "xi", "person": "李定国"}]),
    event("李自成入河南", T("李自成破洛阳，福王朱常洵遇害，开仓散粮，饥民归附者如潮", "Li Zicheng takes Luoyang and kills the Prince of Fu; he opens the granaries and the hungry flock to him"), 3,
          [{"kind": "holds_city", "a": "ming", "city": "luoyang"}, {"kind": "strain_above", "a": "ming", "value": 40}],
          [{"kind": "rebel", "a": "ming", "b": "shun", "city": "luoyang", "cities": ["nanyang", "xinzheng"], "name": T("顺", "Shun"), "person": "李自成"},
           {"kind": "cede", "a": "ming", "b": "shun", "person": "刘宗敏、牛金星"}]),
    event("开封水灌", T("闯军三围开封，黄河决堤，开封城没于洪水，城中数十万人罹难",
                       "Kaifeng, besieged a third time by the rebels, is drowned when the Yellow River dikes are breached; hundreds of thousands perish"), 4,
          [{"kind": "alive", "a": "shun"}, {"kind": "holds_city", "a": "ming", "city": "kaifeng"}, {"kind": "before", "value": 7}], []),
    event("马六甲易手", T("荷兰人夺取马六甲", "The Dutch seize Malacca"), 3,
          [{"kind": "holds_city", "a": "spain", "city": "malacca"}, {"kind": "alive", "a": "dutch"}],
          [{"kind": "cede", "a": "spain", "b": "dutch", "city": "malacca"}]),
    event("松锦之战", T("松山城破，蓟辽总督洪承畴被俘降清；锦州守将祖大寿开城出降，关外只剩宁远孤城", "Songshan falls and Supreme Commander Hong Chengchou is taken and goes over to the Qing; Zu Dashou surrenders Jinzhou, leaving Ningyuan alone beyond the pass"), 4,
          [{"kind": "holds_city", "a": "qing", "city": "shenyang"}, {"kind": "at_war", "a": "qing", "b": "ming"}],
          [{"kind": "cede", "a": "ming", "b": "qing", "city": "jinzhou", "person": "祖大寿、洪承畴"}]),
    event("清帝驾崩", T("清太宗皇太极暴卒于盛京清宁宫，诸王议立六岁的福临，郑亲王济尔哈朗、睿亲王多尔衮辅政",
                       "Hong Taiji dies suddenly in the Qingning Palace at Mukden; the princes raise six-year-old Fulin, with Jirgalang and Dorgon as regents"), 5,
          [{"kind": "alive", "a": "qing"}],
          # 诸王议立福临：先定继承名单，不然按长幼是豪格继位
          [{"kind": "heir", "a": "qing", "person": "爱新觉罗福临"},
           {"kind": "dies", "a": "qing", "person": "爱新觉罗皇太极", "name": T("暴卒于盛京清宁宫", "died suddenly in the Qingning Palace at Mukden")}]),
    event("闯军入关中", T("督师孙传庭兵败，战死潼关，闯军长驱入西安", "Supreme Commander Sun Chuanting is defeated and dies at Tong Pass; the rebels march into Xi'an"), 5,
          [{"kind": "alive", "a": "shun"}, {"kind": "holds_city", "a": "ming", "city": "xian"}],
          [{"kind": "cede", "a": "ming", "b": "shun", "city": "xian", "cities": ["baoji", "hanzhong", "tianshui"]},
           {"kind": "move_capital", "a": "shun", "city": "xian"},
           {"kind": "dies", "a": "ming", "person": "孙传庭", "name": T("战死于潼关", "fell at Tong Pass")}]),
    event("大顺建国", T("李自成于西安建国，国号大顺", "Li Zicheng founds his state at Xi'an and names it Great Shun"), 6,
          [{"kind": "holds_city", "a": "shun", "city": "xian"}],
          [{"kind": "proclaim", "a": "shun", "name": T("大顺", "Great Shun")}]),
    jia_shen(),
    event("吴三桂降清", T("山海关总兵吴三桂开关迎清军，受封平西王，关宁之地尽归于清", "Wu Sangui, commander at Shanhai Pass, opens the gate to the Qing and is made Prince Pingxi; the Guanning lands pass to the Qing"), 6,
          [{"kind": "holds_city", "a": "shun", "city": "beijing"}, {"kind": "holds_city", "a": "ming", "city": "shanhaiguan"}],
          [{"kind": "cede", "a": "ming", "b": "qing", "city": "shanhaiguan", "cities": ["ningyuan"], "person": "吴三桂"},
           {"kind": "grant_title", "a": "qing", "person": "吴三桂", "name": T("平西王", "Prince Pingxi")}]),
    # 清军入关打的是"为明复仇、讨伐流寇"的旗号：先与明罢兵，只打顺(史可法等还想"借虏平寇")；次年清军南下再与南明开战
    event("清军入关", T("多尔衮以为明复仇、讨伐流寇为名，率八旗入山海关，与吴三桂合兵，大破李自成于一片石",
                       "Proclaiming vengeance for the Ming against the rebels, Dorgon leads the banners through Shanhai Pass and, with Wu Sangui, routs Li Zicheng at Yipianshi"), 6,
          [{"kind": "holds_city", "a": "shun", "city": "beijing"}, {"kind": "alive", "a": "qing"}],
          [rel_step("qing", "ming", "truce"), rel_step("qing", "shun", "war", goal="夺城")]),
    event("西军入蜀", T("张献忠入蜀，攻破成都，称帝建国，国号大西", "Zhang Xianzhong enters Sichuan, takes Chengdu and proclaims the state of Great Xi"), 6,
          [{"kind": "alive", "a": "xi"}, {"kind": "holds_city", "a": "ming", "city": "chengdu"}],
          [{"kind": "cede", "a": "ming", "b": "xi", "city": "chengdu", "cities": ["langzhong"]},
           {"kind": "move_capital", "a": "xi", "city": "chengdu"},
           {"kind": "proclaim", "a": "xi", "name": T("大西", "Great Xi")}]),
    event("顺军西撤", T("李自成于武英殿匆匆即皇帝位，次日焚宫西走，清军入据北京", "Li Zicheng hurriedly takes the throne in the Wuying Hall, burns the palace the next day and flees west; the Qing occupy Beijing"), 7,
          [{"kind": "holds_city", "a": "shun", "city": "beijing"}, {"kind": "at_war", "a": "qing", "b": "shun"}],
          [{"kind": "cede", "a": "shun", "b": "qing", "city": "beijing", "cities": ["zhuozhou"]}]),
    event("清迁都北京", T("顺治帝自盛京迁都北京，于紫禁城再行登基之礼", "The Shunzhi emperor moves the court from Mukden to Beijing and is enthroned again in the Forbidden City"), 7,
          [{"kind": "holds_city", "a": "qing", "city": "beijing"}],
          [{"kind": "move_capital", "a": "qing", "city": "beijing"}]),
    event("跑马圈地", T("清廷下令圈占京畿田地分给东来八旗，名曰圈地，百姓流离失所", "The Qing court seizes farmland around the capital for the banners, driving villagers from their homes"), 7,
          [{"kind": "holds_city", "a": "qing", "city": "beijing"}],
          [{"kind": "banner_city", "a": "qing", "city": "beijing"},
           {"kind": "banner_enclosure", "a": "qing", "city": "beijing", "cities": ["zhuozhou", "dingzhou", "zhending"]}]),
    event("逃人法", T("清廷严立逃人法，窝藏逃人者重罪，百姓不得迁徙", "The Qing court enacts the Fugitive Law: harbouring runaways is a grave crime and villagers may not move"), 8,
          [{"kind": "holds_city", "a": "qing", "city": "beijing"}],
          [{"kind": "banner_law", "a": "qing"}]),
    event("再行圈地", T("清廷再度圈地，畿辅民田多入旗下", "The Qing court encloses land again and much of the capital region passes to the banners"), 9,
          [{"kind": "holds_city", "a": "qing", "city": "beijing"}],
          [{"kind": "banner_enclosure", "a": "qing", "city": "beijing", "cities": ["zhuozhou", "dingzhou", "zhending", "handan", "ye"]}]),
    event("清军南下", T("清廷撕去为明复仇的名义，清军南下，兵锋直指江南", "The Qing drop the pretence of avenging the Ming and march south toward the Yangtze"), 7,
          [{"kind": "holds_city", "a": "qing", "city": "beijing"}, {"kind": "alive", "a": "ming"}],
          [rel_step("qing", "ming", "war", goal="夺城")], repeat=True),
    event("南京失守", T("清军渡江，南京开城出降，弘光帝出奔芜湖被俘；郑芝龙、黄道周等于福州奉唐王即位，改元隆武", "The Qing cross the Yangtze and Nanjing opens its gates; the Hongguang emperor flees to Wuhu and is taken. At Fuzhou Zheng Zhilong and Huang Daozhou raise the Prince of Tang as the Longwu emperor"), 7,
          [{"kind": "holds_city", "a": "qing", "city": "nanjing"}, {"kind": "alive", "a": "ming"}],
          [{"kind": "move_capital", "a": "ming", "city": "fuzhou"},
           {"kind": "dies", "a": "ming", "person": "朱由崧", "name": T("被俘后押至北京处死", "taken to Beijing and executed")}]),
    event("福州失守", T("清军入闽，郑芝龙降清，隆武帝于汀州被俘殉国；桂王即位于肇庆，改元永历", "The Qing enter Fujian and Zheng Zhilong goes over; the Longwu emperor is taken at Tingzhou and dies. The Prince of Gui is enthroned at Zhaoqing as the Yongli emperor"), 8,
          [{"kind": "holds_city", "a": "qing", "city": "fuzhou"}, {"kind": "alive", "a": "ming"}],
          [{"kind": "move_capital", "a": "ming", "city": "guangzhou"},
           {"kind": "dies", "a": "ming", "person": "朱聿键", "name": T("于汀州被俘殉国", "taken at Tingzhou and died")}]),
    event("广州失守", T("清军攻破广州，永历朝廷西奔，辗转迁于云南", "The Qing storm Guangzhou and the Yongli court flees west, at last settling in Yunnan"), 9,
          [{"kind": "holds_city", "a": "qing", "city": "guangzhou"}, {"kind": "alive", "a": "ming"}],
          [{"kind": "move_capital", "a": "ming", "city": "kunming"}]),
    event("昆明失守", T("清军三路入滇，永历帝出奔缅甸，后被缅人献出，为吴三桂缢杀于昆明", "Three Qing armies enter Yunnan; the Yongli emperor flees to Burma, is handed over by the Burmese and strangled at Kunming by Wu Sangui"), 10,
          [{"kind": "holds_city", "a": "qing", "city": "kunming"}],
          [{"kind": "dies", "a": "ming", "person": "朱由榔", "name": T("被缅人献出，缢杀于昆明", "handed over by the Burmese and strangled at Kunming")}]),
    death("杨嗣昌之死", ("督师杨嗣昌闻洛阳、襄阳相继失陷，二王遇害，自尽于沙市", "Hearing that Luoyang and Xiangyang have fallen and two princes are dead, the commander Yang Sichang takes his own life at Shashi"), 3, "ming", "杨嗣昌", ("兵败自尽于沙市", "took his own life at Shashi")),
    death("曹变蛟之死", ("松山城破，总兵曹变蛟被俘不降，遇害", "Songshan falls; the general Cao Bianjiao is taken, refuses to submit and is killed"), 4, "ming", "曹变蛟", ("松山城破，不降遇害", "killed at Songshan after refusing to submit")),
    death("李自成之死", ("大顺皇帝李自成南走湖广，殁于通山九宫山", "Li Zicheng, emperor of Great Shun, flees south into Huguang and dies at Mount Jiugong"), 7, "shun", "李自成", ("殁于九宫山", "died at Jiugong Mountain"), when=[{"kind": "holds_city", "a": "qing", "city": "beijing"}]),
    # 联明抗清：清军入关以后，大顺余部(1645)、大西军(1649 起)先后奉明正朔，成了南明抗清的主力
    event("顺军归明", T("大顺余部李过、高一功等奉表归明，号忠贞营，合兵抗清", "Li Guo, Gao Yigong and the rest of the Shun army submit to the Ming as the Loyal and Steadfast Camp and fight the Qing"), 7,
          [{"kind": "alive", "a": "shun"}, {"kind": "alive", "a": "ming"}, {"kind": "holds_city", "a": "qing", "city": "beijing"}],
          [{"kind": "submit", "a": "shun", "b": "ming", "c": "qing"}]),
    death("史可法之死", ("清军攻破扬州，督师史可法不屈被杀，城中屠戮十日", "The Qing storm Yangzhou; the commander Shi Kefa refuses to yield and is killed, and the city is put to the sword for ten days"), 7, "ming", "史可法", ("扬州城破，不屈殉国", "died unbroken when Yangzhou fell"), when=[{"kind": "holds_city", "a": "qing", "city": "yangzhou"}]),
    death("左良玉之死", ("左良玉以“清君侧”为名举兵东下，病死于九江舟中", "Zuo Liangyu turns his army east to \"purge the throne of traitors\" and dies aboard ship at Jiujiang"), 7, "ming", "左良玉", ("病死于九江舟中", "died aboard ship at Jiujiang")),
    death("黄得功之死", ("弘光帝出奔芜湖，黄得功护驾迎战，中箭自刎于荻港", "Guarding the fleeing Hongguang emperor, Huang Degong is struck by an arrow at Digang and cuts his own throat"), 7, "ming", "黄得功", ("中箭自刎于荻港", "took his own life at Digang after an arrow wound"), when=[{"kind": "holds_city", "a": "qing", "city": "nanjing"}]),
    event("江南抗剃发", T("剃发令下，“留头不留发，留发不留头”；江阴士民据城八十一日，嘉定三遭屠城",
                         "The head-shaving edict goes out: \"keep your hair and lose your head.\" Jiangyin holds out for eighty-one days and Jiading is massacred three times"), 7,
          [{"kind": "holds_city", "a": "qing", "city": "nanjing"}, {"kind": "before", "value": 10}], []),
    death("张献忠之死", ("清军入川，大西皇帝张献忠于西充凤凰山中箭身亡", "The Qing enter Sichuan; Zhang Xianzhong, emperor of Great Xi, is killed by an arrow at Mount Fenghuang in Xichong"), 9, "xi", "张献忠", ("于西充凤凰山中箭身亡", "killed by an arrow at Mount Fenghuang in Xichong")),
    event("联明抗清", T("大西余部孙可望、李定国奉永历正朔，联明抗清", "Sun Kewang and Li Dingguo lead the remnants of Great Xi to accept the Yongli calendar and fight the Qing for the Ming"), 11,
          [{"kind": "alive", "a": "xi"}, {"kind": "alive", "a": "ming"}, {"kind": "alive", "a": "qing"}],
          [{"kind": "submit", "a": "xi", "b": "ming", "c": "qing"}]),
    death("秦良玉之死", ("女将秦良玉病逝于石柱，终身奉明正朔", "The woman general Qin Liangyu dies at Shizhu, loyal to the Ming to the end"), 10, "ming", "秦良玉", ("病逝于石柱", "died of illness at Shizhu")),
    death("豪格之死", ("肃亲王豪格被摄政王多尔衮下狱，幽死狱中", "Hooge, Prince Su, is jailed by the regent Dorgon and dies in prison"), 10, "qing", "豪格", ("幽死狱中", "died in prison")),
    death("多尔衮之死", ("摄政王多尔衮出猎坠马，卒于喀喇城；顺治帝亲政，旋追夺其爵", "The regent Dorgon falls from his horse while hunting and dies at Kalacheng; the Shunzhi emperor takes power and soon strips him of his titles"), 12, "qing", "多尔衮", ("出猎坠马，卒于喀喇城", "died at Kalacheng after a fall while hunting")),
    death("祖大寿之死", ("祖大寿病逝于北京", "Zu Dashou dies at Beijing"), 18, "qing", "祖大寿", ("病逝于北京", "died of illness at Beijing")),
    death("福临之死", ("顺治帝崩于养心殿，八岁的玄烨即位，索尼、苏克萨哈、遏必隆、鳌拜四大臣辅政", "The Shunzhi emperor dies in the Hall of Mental Cultivation; eight-year-old Xuanye succeeds with four regents: Sonin, Suksaha, Ebilun and Oboi"), 23, "qing", "爱新觉罗福临", ("病逝于养心殿", "died of illness in the Hall of Mental Cultivation")),
    death("郑芝龙之死", ("郑芝龙因其子郑成功据海抗清，被斩于北京", "Because his son Zheng Chenggong still fights the Qing from the sea, Zheng Zhilong is beheaded at Beijing"), 23, "qing", "郑芝龙", ("被杀于北京", "executed at Beijing")),
    death("李定国之死", ("晋王李定国闻永历帝死讯，忧愤病逝于景线", "Li Dingguo, Prince of Jin, hears of the Yongli emperor's death and dies of grief at Jingxian"), 24, "xi", "李定国", ("忧愤病逝于景线", "died of grief at Jingxian")),
    death("洪承畴之死", ("洪承畴病逝于北京", "Hong Chengchou dies at Beijing"), 27, "qing", "洪承畴", ("病逝于北京", "died of illness at Beijing")),
    death("吴三桂之死", ("平西王吴三桂病死于衡州", "Wu Sangui, Prince Pingxi, dies at Hengzhou"), 40, "qing", "吴三桂", ("病死于衡州", "died of illness at Hengzhou")),
    event("威斯特伐利亚和约", T("威斯特伐利亚诸约签订，三十年战争结束", "The Peace of Westphalia ends the Thirty Years' War"), 10,
          [{"kind": "alive", "a": "hre"}],
          [rel_step("france", "hre", "truce"), rel_step("sweden", "hre", "truce"), rel_step("dutch", "spain", "truce")]),
]

WILD_RENAME = {"later_jin": "qing", "chahar": "qing"}
WILD_EXTRA = {
    "horqin": "qing", "greater_khingan": "qing", "lesser_khingan": "qing", "mu_us": "qing", "ulan_buh": "qing",
    "stanovoy": "solon", "altai": "oirat", "dzungarian_alatau": "oirat", "gurbantunggut": "oirat", "bogda": "yarkent",
    "qilian": "khoshut", "qaidam": "khoshut", "altun": "khoshut", "kumtag": "khoshut", "badain_jaran": "khoshut", "tengger": "khoshut",
    "helan": "ming",
}


def main():
    root = sys.argv[1]
    update_cities(root, NEW_CITIES, ALTS)
    era = copy.deepcopy(load_era(root, "lingao"))
    era.pop("transmigrant", None)
    era.update(
        id="ming_qing", year=1638, focus="east", order=4,
        zh="明清鼎革", en="Ming-Qing Transition",
        dzh="1638年，大明崇祯十一年、大清崇德三年。皇太极已称帝建清，朝鲜新降，漠南诸部尽归于清，清军屡次破边南下；"
            "关内连年旱蝗，李自成、张献忠时降时起。祖大寿守锦州，吴三桂在关宁，从松锦之战一路走到甲申城破、山海关开，清军入关。"
            "西北固始汗刚据青海，西方三十年战争尚未结束，荷兰人占着台湾南部。",
        den="AD 1638, the eleventh year of Chongzhen and the third of Chongde. Hong Taiji has founded the Qing, Joseon has just submitted "
            "and the Inner Mongols follow him, while drought and locusts keep the rebels Li Zicheng and Zhang Xianzhong alive. "
            "From the fall of Songshan and Jinzhou to the sack of Beijing and the opening of Shanhai Pass, the Qing come through the wall. "
            "Güshi Khan has just taken Kokonor, the Thirty Years' War still burns in the West, and the Dutch hold southern Taiwan.")
    # 清有了红夷大炮(1631 仿造)：科技按文化算，游牧诸部一起带上
    era["techExtra"] = dict(era["techExtra"])
    era["techExtra"]["Youmu"] = ["gunpowder", "matchlock", "hongyi_cannon"]
    ranks = dict(era["ranks"])
    for cid in ("lingao", "chaghan", "shenyang"):
        ranks.pop(cid, None)
    ranks.update(shenyang=1, chaghan=2, shanhaiguan=2, jinzhou=2, ava=1, kandahar=2, dunhuang=2)
    era["ranks"] = ranks

    realms = {p["id"]: p for p in era["polities"]}
    old_jin = realms["later_jin"]
    # 1634 后广宁、义州一带与漠南都归清；开局辖地按这些治所铺开
    QING["seats"] = old_jin["seats"] + [[121.65, 41.53], [121.22, 41.55], [111.65, 40.82], [109.8, 40.6], [109.5, 39.5],
                                        [118.6, 43.95], [122.0, 45.0], [117.0, 45.0], [113.5, 42.5],
                                        # 黑龙江下游到海(宁古塔管着)，索伦只在上游
                                        [126.6, 45.8], [130.3, 46.8], [127.5, 49.0], [134.5, 48.3], [136.5, 51.5], [140.0, 53.0]]
    ming = realms["ming"]
    ming["ruler"] = person("朱|Zhu", "由检|Youjian", 27, f="mg14")
    ming["era_year"] = 11
    ming["cities"] = ming["cities"] + ["shanhaiguan", "ningyuan", "jinzhou"]
    ming["kin"] = [
        person("周|Zhou", "氏|Shi", 27, rel="wife"),
        person("田|Tian", "秀英|Xiuying", 28, rel="consort"),
        person("袁|Yuan", "氏|Shi", 27, rel="consort"),
        person("朱|Zhu", "慈烺|Cilang", 9, rel="son", heir=True, mo=0),
        person("朱|Zhu", "慈炯|Cijiong", 5, rel="son", mo=0),
        person("朱|Zhu", "慈炤|Cizhao", 3, rel="son", mo=1),
        person(None, "媺娖|Meichuo", 9, rel="daughter", mo=0),
        person("朱|Zhu", "由崧|Yousong", 31, rel="cousin"),
        person("朱|Zhu", "聿键|Yujian", 36, rel="cousin"),
        person("朱|Zhu", "由榔|Youlang", 15, rel="cousin"),
    ]
    ming["famous"] = MING_FAMOUS
    ming["decline"] = {"strain": 78, "graft": 70, "annexation": 78, "bloat": 70, "military": 72, "factionalism": 74, "levies": 78}
    # 嘉峪关外已弃：沙州归和硕特
    for division in ming["divisions"]:
        division["cities"] = [cid for cid in division["cities"] if cid != "dunhuang"]
        division["seats"] = [seat for seat in division.get("seats", []) if seat[0] >= 97.2]

    persia, mughal = realms["persia"], realms["mughal"]
    # 1638 坎大哈守将献城于莫卧儿；1636 艾哈迈德讷格尔亡，德干北部归莫卧儿
    persia["cities"] = [cid for cid in persia["cities"] if cid != "kandahar"]
    persia["divisions"] = [d for d in persia["divisions"] if "kandahar" not in d["cities"]]
    mughal["cities"] = mughal["cities"] + ["kandahar", "devagiri", "ahmednagar", "paithan"]
    for division in mughal["divisions"]:
        if division["name"].startswith("喀布尔"):
            division["cities"] = division["cities"] + ["kandahar"]
    mughal["divisions"].append({"name": "德干|Deccan", "cities": ["devagiri", "ahmednagar", "paithan"]})
    realms["khalkha"]["cities"] = []

    polities = []
    for item in era["polities"]:
        pid = item["id"]
        if pid == "later_jin":
            polities.append(QING)
            continue
        if pid == "chahar" or pid == "ahmadnagar":
            continue
        if pid in PEOPLE:
            item.update(PEOPLE[pid])
        polities.append(item)
        if pid == "khalkha":
            polities.extend(INNER_ASIA)
    era["polities"] = polities
    era["relations"] = RELATIONS
    era["events"] = EVENTS
    wild = {unit: WILD_RENAME.get(owner, owner) for unit, owner in era["wilderness"].items()}
    wild.update(WILD_EXTRA)
    era["wilderness"] = wild
    write_era(root, era)
    print("ming_qing", era["year"], len(era["polities"]), "国", len(era["events"]), "件事")


if __name__ == "__main__":
    main()
