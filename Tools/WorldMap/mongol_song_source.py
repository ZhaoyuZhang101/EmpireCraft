"""蒙古灭宋(1258 宋宝祐六年、蒙哥汗八年)：以 eras/crusades.json 为底稿改写，写成 eras/mongol_song.json。

东方：金(1234)、西夏(1227)、大理(1254)已亡，大蒙古国据华北、云南，成都、汉中在蒙古手里，宋守重庆、钓鱼城、襄阳、两淮以南。
年初旭烈兀已破巴格达；秋天蒙哥亲征四川，忽必烈攻鄂州，兀良合台自云南北上。西边钦察、伊利、察合台各据一方，名义上还奉大汗。
西方：拉丁帝国还占着君士坦丁堡(1261 才亡)，马穆鲁克据埃及，神圣罗马大空位，西班牙只剩格拉纳达。

事件走到钓鱼城蒙哥之死、两汗相争、艾因贾鲁特、襄阳、大元建号、临安、崖山。襄阳不失、临安就不失，玩家干预得动。
新城都带真实的 from/to，只落在这一局的年份里，别的预设不变。

用法：python -I -X utf8 mongol_song_source.py <模组目录>，之后跑 check_eras.py、check_scenario.py。
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from preset_tools import T, death, event, famous, load_era, person, polity, rel_step, relation, update_cities, write_era  # noqa: E402

NEW_CITIES = [
    {"id": "shangdu", "zh": "开平", "en": "Kaiping", "x": 116.18, "y": 42.36, "r": "monan", "k": 2, "from": 1256, "to": 1430},
    {"id": "diaoyu", "zh": "钓鱼城", "en": "Diaoyu Fortress", "x": 106.28, "y": 30.0, "r": "bashu", "k": 3, "from": 1243, "to": 1279},
    {"id": "sarai", "zh": "萨莱", "en": "Sarai", "x": 47.9, "y": 46.6, "r": "volga", "k": 1, "from": 1250, "to": 1480},
    {"id": "granada", "zh": "格拉纳达", "en": "Granada", "x": -3.6, "y": 37.18, "r": "hispania_s", "k": 2, "from": 1238, "to": 1492},
]

DROP = {"jin", "xixia", "dali", "keraites", "khamag", "tatars", "naimans", "leon", "tripoli", "abbasid", "seljuk", "khwarazm", "ghurid",
        "qara_khitai", "west_karakhanid", "east_karakhanid", "chahamana", "gahadavala", "sena", "volga_bulgaria", "cumania",
        # 马蓬古布韦约 1220 年废弃，津巴布韦(大津巴布韦)代之
        "mapungubwe"}

SONG_CITIES = ["hangzhou", "suzhou", "shaoxing", "nanjing", "nanchang", "ganzhou", "yangzhou", "shouchun", "xiangyang", "jiangling", "wuchang",
               "changsha", "fuzhou", "quanzhou", "guangzhou", "guilin", "qiongzhou", "danzhou", "yazhou", "lingao", "chongqing", "diaoyu", "langzhong", "guiyang"]

MONGOL_KIN = [
    person("孛儿只斤|Borjigin", "忽必烈|Kublai", 43, rel="brother"),
    person("孛儿只斤|Borjigin", "阿里不哥|Ariq Boke", 39, rel="brother"),
    person("弘吉剌|Khongirad", "察必|Chabi", 31, rel="wife", of=0),
    person("孛儿只斤|Borjigin", "真金|Zhenjin", 15, rel="nephew", of=0, mo=2),
    person("弘吉剌|Khongirad", "忽都台|Qutuqtai", 45, rel="wife"),
    person(None, "玉龙答失|Urungtash", 25, rel="son"),
]

MONGOL_FAMOUS = [
    famous("刘|Liu", "秉忠|Bingzhong", 42, "minister", 2, "chief"),
    famous("姚|Yao", "枢|Shu", 55, "minister", 1, "core"),
    famous("耶律|Yelü", "铸|Zhu", 37, "minister", 1, "core"),
    famous("许|Xu", "衡|Heng", 49, "minister", 1, "div"),
    famous("郝|Hao", "经|Jing", 35, "minister", 0, "div"),
    famous("", "阿合马|Ahmad", 40, "minister", 0, "div"),
    famous("段|Duan", "兴智|Xingzhi", 28, "minister", 0, "dali"),
    famous("", "兀良合台|Uriyangkhadai", 59, "general", 2, "general"),
    famous("史|Shi", "天泽|Tianze", 56, "general", 2, "general"),
    famous("", "阿术|Aju", 31, "general", 1, "general"),
    famous("张|Zhang", "柔|Rou", 68, "general", 1, "general"),
    famous("张|Zhang", "弘范|Hongfan", 20, "general", 1, "general"),
    famous("", "阿里海牙|Ariq Qaya", 31, "general", 1, "general"),
    famous("汪|Wang", "德臣|Dechen", 35, "general", 0, "hanzhong"),
    famous("李|Li", "璮|Tan", 50, "general", 1, "linzi"),
]

SONG_FAMOUS = [
    famous("贾|Jia", "似道|Sidao", 45, "minister", 1, "chief"),
    famous("吴|Wu", "潜|Qian", 62, "minister", 1, "core"),
    famous("文|Wen", "天祥|Tianxiang", 22, "minister", 1, "div"),
    famous("陆|Lu", "秀夫|Xiufu", 22, "minister", 1, "div"),
    famous("谢|Xie", "枋得|Fangde", 32, "minister", 0, "div"),
    famous("李|Li", "曾伯|Zengbo", 60, "minister", 0, "guilin"),
    famous("蒲|Pu", "寿庚|Shougeng", 53, "minister", 0, "quanzhou"),
    famous("吕|Lu", "文德|Wende", 58, "general", 2, "wuchang"),
    famous("吕|Lu", "文焕|Wenhuan", 30, "general", 1, "xiangyang"),
    famous("王|Wang", "坚|Jian", 50, "general", 1, "diaoyu"),
    famous("张|Zhang", "珏|Jue", 33, "general", 1, "general"),
    famous("张|Zhang", "世杰|Shijie", 22, "general", 1, "general"),
    famous("李|Li", "庭芝|Tingzhi", 39, "general", 1, "yangzhou"),
    famous("夏|Xia", "贵|Gui", 61, "general", 1, "shouchun"),
    famous("刘|Liu", "整|Zheng", 46, "general", 1, "general"),
    famous("范|Fan", "文虎|Wenhu", 40, "general", 0, "general"),
]

NEW_POLITIES = {
    # 插在底稿哪一国后面
    "song": [
        polity("chagatai", "察合台", "Chagatai", "Youmu", "almaliq", r=["sogdiana", "ferghana", "kazakh", "dzungaria", "xiyu"],
               ruler=person("孛儿只斤|Borjigin", "兀鲁忽乃|Orghana", 40, sex="f")),
        polity("golden_horde", "钦察", "Golden Horde", "Youmu", "sarai", r=["volga", "pontic", "crimea", "khwarazm"],
               ruler=person("孛儿只斤|Borjigin", "别儿哥|Berke", 49)),
        polity("ilkhanate", "伊利", "Ilkhanate", "Youmu", "tabriz", r=["media", "persis", "khorasan", "mesopotamia", "bactria", "arachosia", "gandhara"],
               cities=["edessa"], dynasty="ilkhanate",
               ruler=person("孛儿只斤|Borjigin", "旭烈兀|Hulagu", 40, f="yu_tolui"),
               kin=[person(None, "阿八哈|Abaqa", 24, rel="son", heir=True)],
               famous=[famous("", "纳西尔丁·图西|Nasir al-Din Tusi", 57, "minister", 1, "chief"),
                       famous("", "怯的不花|Kitbuqa", 55, "general", 2, "general"),
                       famous("郭|Guo", "侃|Kan", 40, "general", 1, "general"),
                       famous("", "伯颜|Bayan", 22, "general", 1, "general")]),
        polity("sakya", "萨迦", "Sakya", "Xizang", "shigatse", r=["tibet"], ruler=person("", "八思巴|Phagpa", 23)),
    ],
    "almohad": [
        polity("marinid", "马林", "Marinid", "Arab", "fez", cities=["fez", "sijilmasa", "tingis"], ruler=person("", "阿布·优素福|Abu Yusuf", 45)),
        polity("zayyanid", "齐亚尼", "Zayyanid", "Arab", "tlemcen", cities=["tlemcen", "algiers", "tahert"], ruler=person("", "亚格穆拉森|Yaghmurasen", 52)),
        polity("hafsid", "哈夫斯", "Hafsid", "Arab", "carthage", r=["africa", "numidia"], ruler=person("", "穆斯坦绥尔|al-Mustansir", 30)),
        polity("granada", "格拉纳达", "Granada", "Arab", "granada", cities=["granada"], ruler=person("", "穆罕默德|Muhammad", 63)),
    ],
    "byzantium": [
        polity("latin", "拉丁帝国", "Latin Empire", "Frankish", "byzantium", cities=["byzantium"], ruler=person("库特奈|Courtenay", "鲍德温|Baldwin", 41)),
        polity("achaea", "亚该亚", "Achaea", "Frankish", "corinth", cities=["corinth", "sparta", "athens", "thebes_gr"],
               ruler=person("维尔阿杜安|Villehardouin", "威廉|William", 50)),
        polity("trebizond_empire", "特拉布宗", "Trebizond", "Roma", "trebizond", cities=["trebizond"], ruler=person("科穆宁|Komnenos", "曼努埃尔|Manuel", 40)),
    ],
    "ayyubid": [
        polity("mamluk", "马穆鲁克", "Mamluk", "Arab", "cairo", r=["egypt", "cyrenaica"], dynasty="mamluk",
               ruler=person("", "忽都思|Qutuz", 40),
               famous=[famous("", "拜巴尔斯|Baybars", 35, "general", 2, "general")]),
        polity("mecca", "麦加", "Sharifate of Mecca", "Arab", "mecca", cities=["mecca", "medina"]),
        polity("rasulid", "拉苏勒", "Rasulid", "Arab", "sanaa", cities=["sanaa", "aden"], ruler=person("", "穆扎法尔|al-Muzaffar", 40)),
    ],
    "paramara": [
        polity("delhi", "德里", "Delhi Sultanate", "India", "delhi", r=["punjab", "sindh", "hindustan", "magadha", "bengal"], dynasty="delhi",
               ruler=person("", "纳西尔丁·马哈茂德|Nasiruddin Mahmud", 30),
               famous=[famous("", "巴勒班|Balban", 42, "minister", 2, "chief")]),
    ],
    "khmer": [
        polity("champa_kingdom", "占城", "Champa", "India", "champa", r=["champa"], ruler=person("", "因陀罗跋摩|Indravarman", 30)),
    ],
    # 加奥的桑海(祖瓦王朝)到 13 世纪末才臣服马里
    "ghana": [
        polity("gao", "加奥", "Gao", "Swahili", "gao", cities=["gao"]),
    ],
    "mapungubwe": [
        polity("zimbabwe", "津巴布韦", "Zimbabwe", "Bantu", "great_zimbabwe", r=["zimbabwe"]),
    ],
}

PEOPLE = {
    "hre": dict(decline={"age": 1}, ruler=person("金雀花|Plantagenet", "理查|Richard", 49), kin=[]),
    "bohemia": dict(ruler=person("普热米斯尔|Premyslid", "奥托卡|Ottokar", 25)),
    "venice": dict(cities=["venice", "crete"], ruler=person("泽诺|Zeno", "雷涅罗|Reniero", 60)),
    "papacy": dict(ruler=person("孔蒂|Conti", "里纳尔多|Rinaldo", 59)),
    "sicily": dict(cities=["dyrrhachium"], ruler=person("霍亨施陶芬|Hohenstaufen", "曼弗雷德|Manfred", 26), kin=[]),
    "france": dict(r=["gaul_n", "gaul_s"], cities=["rouen", "tours", "poitiers"], ruler=person("卡佩|Capet", "路易|Louis", 44), kin=[
        person("", "普罗旺斯的玛格丽特|Margaret of Provence", 37, rel="wife"),
        person(None, "腓力|Philip", 13, rel="son", heir=True, mo=0)]),
    "toulouse": dict(ruler=person("卡佩|Capet", "阿方斯|Alphonse", 38)),
    "england": dict(cities=["bordeaux", "dublin"], ruler=person("金雀花|Plantagenet", "亨利|Henry", 51), kin=[
        person("", "普罗旺斯的埃莉诺|Eleanor of Provence", 35, rel="wife"),
        person(None, "爱德华|Edward", 19, rel="son", heir=True, mo=0)],
        famous=[famous("", "西蒙·德·蒙福尔|Simon de Montfort", 50, "general", 2, "general")]),
    "scotland": dict(ruler=person("邓凯尔德|Dunkeld", "亚历山大|Alexander", 16)),
    "connacht": dict(ruler=None),
    "castile": dict(cities=["toledo", "burgos", "leon", "oviedo", "cordoba", "seville", "cadiz", "merida", "cartagena"],
                    ruler=person("伊夫雷亚|Ivrea", "阿方索|Alfonso", 36), kin=[
        person("", "阿拉贡的比奥兰特|Violant of Aragon", 22, rel="wife", house="aragon"),
        person(None, "费尔南多|Ferdinand", 3, rel="son", heir=True, mo=0)]),
    "portugal": dict(cities=["coimbra", "porto", "lisbon"], ruler=person("勃艮第|Burgundy", "阿方索|Afonso", 47), kin=[]),
    "aragon": dict(cities=["saragossa", "barcelona", "tarragona", "valencia"], ruler=person("巴塞罗那|Barcelona", "海梅|James", 50),
                   kin=[person(None, "佩德罗|Peter", 18, rel="son", heir=True)]),
    "navarre": dict(ruler=person("香槟|Champagne", "特奥巴尔多|Theobald", 20), kin=[]),
    "almohad": dict(zh="穆瓦希德", r=[], cities=["marrakesh"], emp=None, divisions=None, ruler=person("穆米尼|Mu'minid", "穆尔塔达|al-Murtada", 40)),
    "hungary": dict(ruler=person("阿尔帕德|Arpad", "贝拉|Bela", 52), kin=[person(None, "斯蒂芬|Stephen", 19, rel="son", heir=True)]),
    "poland": dict(ruler=person("皮雅斯特|Piast", "波列斯瓦夫|Boleslaw", 32), kin=[]),
    "denmark": dict(cities=[], ruler=person("埃斯特里德森|Estridsen", "克里斯托弗|Christopher", 39), kin=[]),
    "sweden": dict(ruler=person("比尔格|Bjalbo", "瓦尔德马|Valdemar", 19),
                   famous=[famous("", "比尔格·雅尔|Birger Jarl", 58, "minister", 2, "chief")]),
    "norway": dict(ruler=person("", "哈康|Haakon", 54), kin=[person("", "马格努斯|Magnus", 20, rel="son", heir=True)]),
    "vladimir": dict(zh="弗拉基米尔", en="Vladimir", dynasty=None, ruler=person("留里克|Rurik", "亚历山大·涅夫斯基|Alexander Nevsky", 38), kin=[]),
    "kiev": dict(ruler=None),
    "galicia": dict(cities=["galich", "lviv"], ruler=person("留里克|Rurik", "丹尼洛|Daniel", 57)),
    "cyprus": dict(zh="塞浦路斯王国", en="Kingdom of Cyprus", ruler=person("吕西尼昂|Lusignan", "休|Hugh", 5)),
    "byzantium": dict(
        zh="尼西亚", en="Nicaea", cap="nicaea", r=["asia_minor", "macedonia", "thrace"], cities=["ephesus", "pergamon"], divisions=None,
        ruler=person("拉斯卡利斯|Laskaris", "狄奥多尔|Theodore", 36),
        kin=[person(None, "约翰|John", 7, rel="son", heir=True)],
        famous=[famous("巴列奥略|Palaiologos", "米海尔|Michael", 35, "general", 2, "general")],
        decline={"age": 54, "strain": 40, "graft": 40, "annexation": 45, "bloat": 40, "military": 40, "factionalism": 50, "levies": 45}),
    "bulgaria": dict(ruler=person("", "康斯坦丁·提赫|Constantine Tikh", 40)),
    "rum": dict(cities=["attalia"], ruler=person("塞尔柱|Seljuk", "基利杰·阿尔斯兰|Kilij Arslan", 20), kin=[]),
    "cilicia": dict(ruler=person("", "赫图姆|Hethum", 45)),
    "georgia": dict(ruler=person("巴格拉季昂|Bagrationi", "大卫|David", 33)),
    "jerusalem": dict(cap="acre", cities=["acre", "tyre", "tripoli_lb"], ruler=person("吕西尼昂|Lusignan", "休|Hugh", 23), kin=[], famous=[]),
    "antioch": dict(zh="安条克-的黎波里", en="Antioch-Tripoli", ruler=person("普瓦捷|Poitiers", "博希蒙德|Bohemond", 21), kin=[]),
    "ayyubid": dict(zh="阿尤布", cap="damascus", r=[], cities=["damascus", "aleppo", "jerusalem"], emp=None, dynasty=None, divisions=None,
                    ruler=person("阿尤布|Ayyubid", "纳西尔·优素福|an-Nasir Yusuf", 30), kin=[], famous=[]),
    "salghurid": dict(ruler=person("", "阿布·伯克尔|Abu Bakr", 50)),
    "qocho": dict(of=None, ruler=person("", "撒连的斤|Salindi", 30)),
    "song": dict(
        r=["jiangdong", "jiangxi", "jingchu", "minyue", "lingnan", "bashu"], cities=["shouchun", "yangzhou", "diaoyu", "guiyang", "langzhong"],
        era_name="宝祐|Baoyou", era_year=6,
        ruler=person("赵|Zhao", "昀|Yun", 53, f="sg_xilu"),
        kin=[person("谢|Xie", "道清|Daoqing", 48, rel="wife"), person("赵|Zhao", "禥|Qi", 18, rel="son", heir=True)],
        famous=SONG_FAMOUS,
        decline={"age": 131, "strain": 72, "graft": 75, "annexation": 82, "bloat": 80, "military": 62, "factionalism": 68, "levies": 72}),
    "goryeo": dict(ruler=person("王|Wang", "暾|Cheol", 66), kin=[person(None, "倎|Jeon", 39, rel="son", heir=True)]),
    "dai_viet": dict(zh="大越", ruler=person("陈|Tran", "晃|Hoang", 18), era_name="绍隆|Thieu Long", era_year=1,
                     famous=[famous("陈|Tran", "国峻|Quoc Tuan", 30, "general", 2, "general")]),
    "japan": dict(ruler=person("", "久仁|Hisahito", 15), era_name="正嘉|Shoka", era_year=2,
                  famous=[famous("北条|Hojo", "时赖|Tokiyori", 31, "minister", 2, "chief"),
                          famous("北条|Hojo", "时宗|Tokimune", 7, "general", 1, "none")]),
    "chaulukya": dict(ruler=person("", "维沙拉德瓦|Visaladeva", 45)),
    "paramara": dict(ruler=person("", "贾亚瓦尔曼|Jayavarman", 40)),
    "yadava": dict(ruler=person("", "克里希纳|Krishna", 35)),
    "ganga": dict(ruler=person("", "那罗辛哈|Narasimhadeva", 40)),
    "chola": dict(r=[], cities=["thanjavur"], ruler=person("", "拉金德拉|Rajendra", 40)),
    "pandya": dict(r=["tamil"], ruler=person("", "贾塔瓦尔曼|Jatavarman", 45)),
    "polonnaruwa": dict(ruler=person("", "波罗迦罗摩巴忽|Parakramabahu", 50)),
    "pagan": dict(ruler=person("", "那罗梯诃波帝|Narathihapate", 20)),
    "khmer": dict(r=["khmer", "dvaravati"], ruler=person("", "阇耶跋摩|Jayavarman", 50)),
    "kediri": dict(zh="新柯沙里", en="Singhasari", ruler=person("", "毗湿奴跋达那|Vishnuvardhana", 40)),
    # 马里：松迪亚塔(1255 卒)之子曼萨乌利，都尼亚尼；廷巴克图 1324 年才入马里，这里随萨赫勒的地区法理一起算
    "ghana": dict(zh="马里", en="Mali", cap="niani", cities=["niani"], r=["sahel"], ruler=person("", "瓦利|Wali", 30)),
}

RELATIONS = [
    relation("mongol", "song", "war", goal="吞并"),
    relation("mongol", "song", "feud", value=60),
    relation("mongol", "goryeo", "war", goal="迫使附庸"),
    relation("mongol", "dai_viet", "feud", value=30),
    relation("dai_viet", "song", "tribute"),
    relation("golden_horde", "mongol", "tribute"),
    relation("chagatai", "mongol", "tribute"),
    relation("ilkhanate", "mongol", "tribute"),
    relation("qocho", "mongol", "tribute"),
    relation("sakya", "mongol", "tribute"),
    relation("ilkhanate", "ayyubid", "war", goal="吞并"),
    relation("rum", "ilkhanate", "tribute"),
    relation("georgia", "ilkhanate", "tribute"),
    relation("salghurid", "ilkhanate", "tribute"),
    relation("cilicia", "ilkhanate", "alliance"),
    relation("antioch", "ilkhanate", "alliance"),
    relation("mamluk", "ayyubid", "feud", value=40),
    relation("mamluk", "ilkhanate", "feud", value=40),
    relation("vladimir", "golden_horde", "tribute"),
    relation("kiev", "golden_horde", "tribute"),
    relation("galicia", "golden_horde", "tribute"),
    relation("byzantium", "latin", "war", goal="收复"),
    relation("venice", "latin", "alliance"),
    relation("genoa", "byzantium", "alliance"),
    relation("castile", "granada", "feud", value=30),
    relation("marinid", "almohad", "war", goal="吞并"),
    relation("england", "france", "truce"),
    relation("sicily", "papacy", "feud", value=40),
]

EVENTS = [
    event("钓鱼城之战", T("蒙哥汗死于钓鱼城下，攻宋诸军北返，忽必烈与贾似道议和于鄂州", "The Great Khan dies before Diaoyu Fortress; the armies turn north and Kublai makes peace at Ezhou"), 1,
          [{"kind": "holds_city", "a": "song", "city": "diaoyu"}, {"kind": "at_war", "a": "mongol", "b": "song"}],
          [{"kind": "heir", "a": "mongol", "person": "孛儿只斤忽必烈"},
           {"kind": "dies", "a": "mongol", "person": "汪德臣", "name": T("战死于钓鱼城下", "fell before Diaoyu Fortress")},
           {"kind": "dies", "a": "mongol", "person": "孛儿只斤蒙哥", "name": T("伤殁于钓鱼城下", "died of wounds before Diaoyu Fortress")},
           rel_step("mongol", "song", "truce")]),
    event("高丽请和", T("高丽太子入朝请和，向蒙古称臣", "The crown prince of Goryeo comes to court and submits to the Mongols"), 1,
          [{"kind": "at_war", "a": "mongol", "b": "goryeo"}],
          [rel_step("mongol", "goryeo", "truce"), rel_step("goryeo", "mongol", "tribute")]),
    event("两汗相争", T("忽必烈即汗位于开平，阿里不哥称汗于和林，蒙古内战", "Kublai takes the throne at Kaiping and Ariq Boke at Karakorum; civil war breaks out among the Mongols"), 2,
          [{"kind": "holds_city", "a": "mongol", "city": "ordu"}, {"kind": "holds_city", "a": "mongol", "city": "shangdu"}],
          [{"kind": "move_capital", "a": "mongol", "city": "shangdu"},
           {"kind": "rebel", "a": "mongol", "b": "ariq", "city": "ordu", "cities": ["tuul", "onon", "kerulen", "khovd"],
            "name": T("和林", "Karakorum"), "person": "孛儿只斤阿里不哥"}]),
    event("艾因贾鲁特之战", T("旭烈兀破叙利亚，马穆鲁克败蒙古军于艾因贾鲁特，尽收其地", "Hulagu sweeps through Syria; the Mamluks crush the Mongols at Ain Jalut and take the land"), 2,
          [{"kind": "alive", "a": "mamluk"}, {"kind": "alive", "a": "ilkhanate"}],
          [{"kind": "dies", "a": "ilkhanate", "person": "怯的不花", "name": T("战死于艾因贾鲁特", "fell at Ain Jalut")},
           {"kind": "cede", "a": "ayyubid", "b": "mamluk", "city": "damascus", "cities": ["aleppo", "jerusalem"]},
           {"kind": "heir", "a": "mamluk", "person": "拜巴尔斯"},
           {"kind": "dies", "a": "mamluk", "person": "忽都思", "name": T("凯旋途中为部将所杀", "murdered by his officers on the way home")},
           rel_step("mamluk", "ilkhanate", "war", goal="夺城")]),
    event("光复君士坦丁堡", T("尼西亚军夺回君士坦丁堡，拉丁帝国灭亡", "Nicaean troops retake Constantinople and the Latin Empire falls"), 3,
          [{"kind": "alive", "a": "latin"}, {"kind": "holds_city", "a": "latin", "city": "byzantium"}],
          [{"kind": "cede", "a": "latin", "b": "byzantium", "city": "byzantium"},
           {"kind": "move_capital", "a": "byzantium", "city": "byzantium"},
           {"kind": "rename", "a": "byzantium", "name": T("罗马", "Rome")}]),
    event("刘整降蒙古", T("泸州守将降蒙古，献攻宋必先取襄阳之策", "A Song commander goes over to the Mongols, urging them to take Xiangyang first"), 3,
          [{"kind": "alive", "a": "song"}, {"kind": "alive", "a": "mongol"}],
          [{"kind": "cede", "a": "song", "b": "mongol", "city": "langzhong", "person": "刘整"}]),
    event("李璮之乱", T("益都守将据城叛蒙古，向宋称臣", "The commander at Yidu rebels against the Mongols and submits to Song"), 4,
          [{"kind": "holds_city", "a": "mongol", "city": "linzi"}],
          [{"kind": "rebel", "a": "mongol", "b": "litan", "city": "linzi", "cities": ["jinan", "jimo"], "name": T("齐", "Qi"), "person": "李璮"},
           rel_step("litan", "song", "tribute")]),
    event("别儿哥与旭烈兀之战", T("钦察与伊利为高加索开战，蒙古诸汗国自相攻伐", "The Golden Horde and the Ilkhanate go to war over the Caucasus"), 4,
          [{"kind": "alive", "a": "golden_horde"}, {"kind": "alive", "a": "ilkhanate"}],
          [rel_step("golden_horde", "ilkhanate", "war", goal="夺城"), rel_step("golden_horde", "mamluk", "alliance")]),
    event("李璮败亡", T("蒙古军围济南，李璮败死", "The Mongols besiege Jinan and the rebel commander dies in defeat"), 5,
          [{"kind": "alive", "a": "litan"}],
          [{"kind": "dies", "a": "litan", "person": "李璮", "name": T("兵败被杀", "killed in defeat")},
           {"kind": "cede", "a": "litan", "b": "mongol", "city": "linzi", "cities": ["jinan", "jimo"]}]),
    event("阿里不哥来降", T("阿里不哥势穷来降，蒙古内战结束", "Ariq Boke runs out of men and surrenders; the civil war ends"), 6,
          [{"kind": "alive", "a": "ariq"}],
          [{"kind": "cede", "a": "ariq", "b": "mongol", "city": "ordu", "cities": ["tuul", "onon", "kerulen", "khovd"]}]),
    event("理宗崩", T("宋帝驾崩，太子嗣位", "The Song emperor dies and the crown prince succeeds him"), 6,
          [{"kind": "alive", "a": "song"}],
          [{"kind": "dies", "a": "song", "person": "赵昀", "name": T("病逝", "died of illness")}]),
    event("旭烈兀卒", T("旭烈兀卒，阿八哈嗣位；伯颜东归，入忽必烈帐下", "Hulagu dies and Abaqa succeeds; Bayan travels east to serve Kublai"), 7,
          [{"kind": "alive", "a": "ilkhanate"}],
          [{"kind": "dies", "a": "ilkhanate", "person": "孛儿只斤旭烈兀", "name": T("病逝", "died of illness")},
           {"kind": "cede", "a": "ilkhanate", "b": "mongol", "person": "伯颜"}]),
    event("襄阳之围", T("蒙古军筑堡围襄阳、樊城，大举攻宋", "The Mongols ring Xiangyang and Fancheng with forts and resume the conquest of Song"), 10,
          [{"kind": "holds_city", "a": "song", "city": "xiangyang"}, {"kind": "alive", "a": "mongol"}],
          [rel_step("mongol", "song", "war", goal="吞并")]),
    event("大元建号", T("忽必烈建国号大元，定都大都", "Kublai proclaims the Great Yuan and makes Dadu his capital"), 13,
          [{"kind": "holds_city", "a": "mongol", "city": "beijing"}],
          [{"kind": "move_capital", "a": "mongol", "city": "beijing"},
           {"kind": "adopt_huaxia", "a": "mongol", "name": T("元", "Yuan")}]),
    event("襄阳失守", T("樊城陷落，襄阳守将力竭出降，长江门户洞开", "Fancheng falls and Xiangyang surrenders; the way to the Yangzi lies open"), 15,
          [{"kind": "holds_city", "a": "song", "city": "xiangyang"}, {"kind": "at_war", "a": "mongol", "b": "song"}, {"kind": "strain_above", "a": "song", "value": 50}],
          [{"kind": "cede", "a": "song", "b": "mongol", "city": "xiangyang", "person": "吕文焕"}]),
    event("度宗崩", T("宋帝驾崩，幼主嗣位", "The Song emperor dies and a child succeeds him"), 16,
          [{"kind": "alive", "a": "song"}],
          [{"kind": "dies", "a": "song", "person": "赵禥", "name": T("病逝", "died of illness")}]),
    event("文永之役", T("元军东征日本，遇风暴而还", "The Yuan fleet sails against Japan and is turned back by storms"), 16,
          [{"kind": "alive", "a": "japan"}, {"kind": "holds_city", "a": "goryeo", "city": "kaesong"}],
          [rel_step("mongol", "japan", "war", goal="侵略")]),
    event("丁家洲之败", T("宋军大败于丁家洲，主政大臣被贬身死", "The Song army is routed at Dingjiazhou and the chief minister is exiled to his death"), 17,
          [{"kind": "holds_city", "a": "mongol", "city": "xiangyang"}],
          [{"kind": "dies", "a": "song", "person": "贾似道", "name": T("贬死于漳州木绵庵", "killed on the road to exile at Zhangzhou")}]),
    event("临安陷落", T("元军入临安，宋主出降，诸臣奉宗室南走闽广", "Yuan troops enter Lin'an; the Song ruler surrenders and loyal ministers flee south with the princes"), 18,
          [{"kind": "holds_city", "a": "mongol", "city": "xiangyang"}, {"kind": "holds_city", "a": "song", "city": "hangzhou"}],
          [{"kind": "move_capital", "a": "song", "city": "fuzhou"},
           {"kind": "cede", "a": "song", "b": "mongol", "city": "hangzhou", "cities": ["suzhou", "shaoxing", "nanjing", "yangzhou", "shouchun", "wuchang", "jiangling"]}]),
    event("蒲寿庚降元", T("泉州舶主降元，宋室失了海上财赋", "The shipmaster of Quanzhou goes over to the Yuan and Song loses its sea revenue"), 19,
          [{"kind": "holds_city", "a": "song", "city": "quanzhou"}, {"kind": "holds_city", "a": "mongol", "city": "hangzhou"}],
          [{"kind": "cede", "a": "song", "b": "mongol", "city": "quanzhou", "person": "蒲寿庚"}]),
    event("崖山之战", T("崖山海战，宋军覆没，大臣负幼主投海，宋亡", "At Yamen the Song fleet is destroyed and a minister leaps into the sea with the boy emperor; Song is no more"), 21,
          [{"kind": "alive", "a": "song"}, {"kind": "holds_city", "a": "mongol", "city": "hangzhou"}],
          [{"kind": "dies", "a": "song", "person": "陆秀夫、张世杰", "name": T("崖山蹈海", "drowned at Yamen")},
           {"kind": "cede", "a": "song", "b": "mongol", "city": SONG_CITIES[0], "cities": SONG_CITIES[1:]}]),
    event("弘安之役", T("元军再征日本，大风毁舟，东征告终", "The Yuan invade Japan again; a great wind wrecks the fleet and the eastern war ends"), 23,
          [{"kind": "at_war", "a": "mongol", "b": "japan"}],
          [rel_step("mongol", "japan", "truce")]),
    event("文天祥就义", T("文天祥囚于大都不屈，就义于柴市", "Wen Tianxiang, unbroken in captivity, is executed at Dadu"), 25,
          [{"kind": "holds_city", "a": "mongol", "city": "hangzhou"}],
          [{"kind": "dies", "a": "mongol", "person": "文天祥", "name": T("不屈就义于大都柴市", "executed at Dadu, unbroken")}]),
]

LATE_DEATHS = [
    death("王坚之死", ("钓鱼城守将王坚郁郁而终", "Wang Jian, defender of Diaoyu Fortress, dies out of favour"), 6, "song", "王坚", ("郁郁而终", "died out of favour")),
    death("阿里不哥之死", ("阿里不哥病死于幽禁之中", "Ariq Boke dies in confinement"), 8, "mongol", "孛儿只斤阿里不哥", ("病死于幽禁之中", "died in confinement")),
    death("吕文德之死", ("吕文德病逝，京湖防务无人可继", "Lü Wende dies and no one can take over the Yangzi defences"), 11, "song", "吕文德", ("病逝", "died of illness")),
    death("史天泽之死", ("史天泽病逝", "Shi Tianze dies"), 17, "mongol", "史天泽", ("病逝", "died of illness")),
    death("刘整之死", ("刘整病逝", "Liu Zheng dies"), 17, "mongol", "刘整", ("病逝", "died of illness")),
    death("李庭芝之死", ("扬州失守，李庭芝不屈被杀", "Yangzhou falls and Li Tingzhi is killed, unbroken"), 18, "song", "李庭芝", ("不屈被杀于扬州", "killed at Yangzhou, unbroken")),
    death("拜巴尔斯之死", ("马穆鲁克苏丹拜巴尔斯病逝", "The Mamluk sultan Baybars dies"), 19, "mamluk", "拜巴尔斯", ("病逝于大马士革", "died at Damascus")),
    death("张弘范之死", ("张弘范病逝", "Zhang Hongfan dies"), 22, "mongol", "张弘范", ("病逝", "died of illness")),
    death("张珏之死", ("四川守将张珏被俘自尽", "Zhang Jue, the last defender of Sichuan, takes his own life in captivity"), 22, "song", "张珏", ("被俘后自尽", "took his own life in captivity")),
    death("忽必烈之死", ("元世祖忽必烈驾崩", "Kublai Khan dies"), 36, "mongol", "孛儿只斤忽必烈", ("病逝于大都", "died at Dadu")),
    death("伯颜之死", ("伯颜病逝", "Bayan dies"), 37, "mongol", "伯颜", ("病逝", "died of illness")),
]

WILD_RENAME = {"jin": "mongol", "xixia": "mongol", "naimans": "mongol", "khamag": "mongol", "keraites": "mongol", "tatars": "mongol", "dali": "mongol",
               "qara_khitai": "chagatai", "khwarazm": "golden_horde", "ghurid": "ilkhanate", "abbasid": "ilkhanate", "chahamana": "delhi", "sena": "delhi",
               "gahadavala": "delhi"}
WILD_EXACT = {"syrian_desert": "ayyubid", "sinai": "mamluk", "sinai_mountains": "mamluk", "negev": "mamluk", "judaean_desert": "ayyubid",
              "eastern_desert": "mamluk", "libyan_desert": "mamluk", "qattara": "mamluk", "yemen_highlands": "rasulid", "atlas": "almohad", "high_atlas": "almohad",
              "middle_atlas": "marinid", "saharan_atlas": "zayyanid", "aures": "hafsid", "tell_atlas": "hafsid", "sierra_nevada_es": "granada",
              "pindus": "achaea", "qionglai": "mongol", "tibetan_plateau": "sakya", "nyainqentanglha": "sakya", "taebaek": "goryeo"}


def mongol(base):
    """大蒙古国：都城和林，漠北为直辖；华北各路沿用底稿金的治所，另加西夏故地、四川、云南。"""
    jin, xixia, dali, song = base["jin"], base["xixia"], base["dali"], base["song"]
    rename = {"中都路": ("燕京路|Yanjing Circuit", ["beijing", "zhuozhou"]),
              "上京路": ("开元路|Kaiyuan Circuit", ["huining", "fuyu"]),
              "东京路": ("辽东路|Liaodong Circuit", ["liaoyang"]),
              "北京路": ("北京路|Beijing Circuit", ["chifeng", "shangjing", "shangdu"]),
              "西京路": ("西京路|Xijing Circuit", ["datong", "hohhot"]),
              "河东北路": ("太原路|Taiyuan Circuit", ["taiyuan"]),
              "河东南路": ("平阳路|Pingyang Circuit", ["linfen", "anyi", "changzhi"]),
              "河北西路": ("真定路|Zhending Circuit", ["zhending", "dingzhou", "ye", "handan"]),
              "山东东路": ("益都路|Yidu Circuit", ["linzi", "jinan", "jimo"]),
              "山东西路": ("东平路|Dongping Circuit", ["qufu", "xuzhou"]),
              "南京路": ("河南路|Henan Circuit", ["kaifeng", "luoyang", "xuchang", "chen", "xinzheng", "nanyang"]),
              "京兆府路": ("京兆路|Jingzhao Circuit", ["xian"]),
              "凤翔路": ("巩昌路|Gongchang Circuit", ["baoji", "tianshui", "lanzhou"])}
    divisions = []
    for division in jin["divisions"]:
        short = division["name"].split("|")[0]
        name, cities = rename[short]
        seats = division["seats"]
        if short == "中都路":
            seats = jin["seats"] + seats
        divisions.append({"name": name, "cities": cities, "seats": seats})
    divisions.append({"name": "中兴路|Zhongxing Circuit", "cities": ["yinchuan", "wuwei", "zhangye", "jiuquan", "dunhuang", "xining"], "seats": xixia["seats"]})
    song_div = {d["name"].split("|")[0]: d for d in song["divisions"]}
    north_lizhou = [seat for seat in song_div["利州路"]["seats"] if seat[1] >= 32.2]
    divisions.append({"name": "四川|Sichuan", "cities": ["chengdu", "hanzhong"], "seats": song_div["成都府路"]["seats"] + north_lizhou})
    divisions.append({"name": "云南|Yunnan", "cities": ["kunming", "dali"], "seats": dali["seats"]})
    return polity(
        "mongol", "蒙古", "Mongol", "Youmu", "ordu",
        r=["mobei", "monan", "manchuria", "liaodong", "youyan", "hebei", "hedong", "qilu", "henan", "guanzhong", "longxi", "hexi", "hetao", "qinghai", "nanzhong", "huaisi"],
        cities=["chengdu", "hanzhong", "shangdu"], emp=True, grand=True, core="mongolia", dynasty="yuan",
        ruler=person("孛儿只斤|Borjigin", "蒙哥|Mongke", 50, f="yu_tolui"),
        kin=MONGOL_KIN, famous=MONGOL_FAMOUS, divisions=divisions,
        seats=[[102.7, 47.3], [106.9, 47.9], [110.5, 48.5], [115.5, 47.3], [100.0, 49.5], [108.0, 45.5], [112.0, 45.0]])


def main():
    root = sys.argv[1]
    data = update_cities(root, NEW_CITIES, {})
    active = {c["id"] for c in data["cities"] if c.get("from", -99999) <= 1258 <= c.get("to", 99999)}
    era = copy.deepcopy(load_era(root, "crusades"))
    era.update(
        id="mongol_song", year=1258, focus="east", order=4,
        zh="蒙古灭宋", en="Mongol Conquest of Song",
        dzh="1258年，南宋宝祐六年，蒙哥汗八年。旭烈兀刚破巴格达，蒙哥亲征四川，忽必烈攻鄂州，兀良合台自云南北上，三路伐宋。"
            "从钓鱼城到襄阳、临安、崖山，二十一年间宋室一步步走向覆亡，文天祥、陆秀夫、张世杰、贾似道都在朝。"
            "大蒙古国也将分裂：忽必烈与阿里不哥争位，钦察、伊利、察合台各自为政。西方马穆鲁克据埃及，拉丁帝国还占着君士坦丁堡。",
        den="AD 1258. Hulagu has just sacked Baghdad, and the Great Khan Mongke leads three armies against Song: himself in Sichuan, Kublai at Ezhou, "
            "Uriyangkhadai from Yunnan. From Diaoyu Fortress to Xiangyang, Lin'an and Yamen, Song has twenty-one years left, with Wen Tianxiang, Lu Xiufu, "
            "Zhang Shijie and Jia Sidao at court. The Mongol Empire itself is about to split as Kublai and Ariq Boke fight for the throne.")
    era["techExtra"] = dict(era["techExtra"])
    era["techExtra"]["Youmu"] = list(era["techExtra"].get("Youmu", [])) + ["gunpowder"]
    # 女真：金末已用火器(震天雷)
    era["techExtra"]["Manzhou"] = list(era["techExtra"].get("Manzhou", [])) + ["gunpowder"]
    ranks = {cid: rank for cid, rank in era["ranks"].items() if cid in active}
    ranks.update(timbuktu=2, tabriz=2, langzhong=2, quanzhou=2, almaliq=2, shigatse=2, diaoyu=2, tuul=2, onon=2, kerulen=2, khovd=2, edessa=2, tripoli_lb=2, cirta=2, crete=2)
    era["ranks"] = ranks

    base = {p["id"]: p for p in era["polities"]}
    song = base["song"]
    keep = []
    for division in song["divisions"]:
        short = division["name"].split("|")[0]
        if short == "成都府路":
            continue
        if short == "利州路":
            division = dict(division, cities=["langzhong"], seats=[seat for seat in division["seats"] if seat[1] < 32.2])
        if short == "夔州路":
            division = dict(division, cities=["chongqing", "diaoyu", "guiyang"])
        keep.append(division)
    new_mongol = mongol(base)
    song["divisions"] = keep

    polities = []
    for item in era["polities"]:
        pid = item["id"]
        if pid == "jin":
            polities.append(new_mongol)
        if pid not in DROP:
            for key, value in PEOPLE.get(pid, {}).items():
                if value is None:
                    item.pop(key, None)
                else:
                    item[key] = value
            polities.append(item)
        polities.extend(NEW_POLITIES.get(pid, []))
    missing = set(NEW_POLITIES) - {p["id"] for p in era["polities"]}
    if missing:
        raise SystemExit("底稿里没有：%s" % sorted(missing))
    for p in polities:
        if "cities" in p:
            p["cities"] = [cid for cid in p["cities"] if cid in active]
        for division in p.get("divisions", []):
            division["cities"] = [cid for cid in division.get("cities", []) if cid in active]
    era["polities"] = polities
    era["relations"] = RELATIONS
    era["events"] = EVENTS + LATE_DEATHS
    ids = {p["id"] for p in polities}
    wild = {}
    for unit, owner in era["wilderness"].items():
        owner = WILD_EXACT.get(unit, WILD_RENAME.get(owner, owner))
        if owner in ids:
            wild[unit] = owner
    wild.update({unit: owner for unit, owner in WILD_EXACT.items() if owner in ids})
    era["wilderness"] = wild
    import check_eras  # noqa: E402
    from wilderness_administration_source import defaults  # noqa: E402
    era["wilderness_regions"] = defaults(check_eras.load(root)[0], era)
    write_era(root, era)
    print("mongol_song", era["year"], len(era["polities"]), "国", len(era["events"]), "件事")


if __name__ == "__main__":
    main()
