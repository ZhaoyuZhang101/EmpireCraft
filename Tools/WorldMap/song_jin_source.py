"""绍兴北伐(1140 宋绍兴十年、金天眷三年)：以 eras/crusades.json 为底稿改写，写成 eras/song_jin.json。

东方：金五月撕毁和约，完颜宗弼复取河南陕西，岳飞自鄂州北伐；金都仍在上京会宁(1153 才迁燕京)，宋都临安；
襄阳六郡(含唐、邓，即南阳)在宋手里，秦州、兰州归金，1142 年《绍兴和议》以淮水、大散关为界。
西方：第二次十字军前夜：埃德萨还在(1144 陷)，赞吉据摩苏尔阿勒颇，布里朝据大马士革，法蒂玛未亡，
穆拉比特仍领安达卢斯，英格兰正打内战，大塞尔柱桑贾尔未败(1141 卡特万)。
事件走到绍兴和议、岳飞之死、海陵南征。

用法：python -I -X utf8 song_jin_source.py <模组目录>，之后跑 check_eras.py、check_scenario.py。
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from preset_tools import T, death, event, famous, load_era, person, polity, rel_step, relation, update_cities, write_era  # noqa: E402

ALTS = {
    # 颍昌府(1105~金改许州)，岳飞颍昌之战在此
    "xuchang": ["-200|许|Xu", "580|许昌|Xuchang", "1105|许州|Xuzhou", "1141|颍昌|Yingchang"],
}

# 1190 年以后才有的国，或 1140 年已经没了的
DROP = {"leon", "bulgaria", "cilicia", "almohad", "ghurid", "cyprus", "ayyubid", "salghurid", "chahamana", "yadava"}

REPLACE = {
    "ayyubid": [
        polity("fatimid", "法蒂玛", "Fatimid", "Arab", "cairo", r=["egypt", "cyrenaica"], emp=True, dynasty="fatimid",
               ruler=person("法蒂玛|Fatimid", "哈菲兹|al-Hafiz", 66),
               divisions=[{"name": "埃及|Egypt", "cities": ["alexandria", "thebes", "aswan"]}, {"name": "巴尔卡|Barqa", "cities": ["barqa"]}]),
        polity("zengid", "赞吉", "Zengid", "Arab", "mosul", cities=["mosul", "nisibis", "aleppo"],
               ruler=person("", "伊马德丁·赞吉|Imad al-Din Zengi", 55),
               kin=[person("", "努尔丁|Nur al-Din", 22, rel="son", heir=True)],
               famous=[famous("", "谢尔库赫|Shirkuh", 40, "general", 1, "general")]),
        polity("burid", "布里", "Burid", "Arab", "damascus", cities=["damascus"], ruler=person("", "乌努尔|Mu'in ad-Din Unur", 50)),
        polity("mecca", "麦加", "Sharifate of Mecca", "Arab", "mecca", cities=["mecca", "medina"]),
    ],
    "jerusalem": [
        polity("edessa_county", "埃德萨伯国", "County of Edessa", "Frankish", "edessa", cities=["edessa"],
               ruler=person("库特奈|Courtenay", "若斯兰|Joscelin", 27)),
    ],
    "rum": [
        polity("danishmend", "达尼什曼德", "Danishmend", "Youmu", "caesarea", cities=["caesarea", "sinope"],
               ruler=person("", "穆罕默德|Melik Muhammad", 40)),
    ],
    "seljuk": [
        polity("great_seljuk", "大塞尔柱", "Great Seljuk", "Persepolis", "merv", r=["khorasan", "bactria"], emp=True, dynasty="seljuk",
               ruler=person("塞尔柱|Seljuk", "桑贾尔|Sanjar", 54)),
    ],
    "england": [
        polity("anjou", "安茹伯国", "County of Anjou", "Frankish", "tours", cities=["tours"],
               ruler=person("金雀花|Plantagenet", "若弗鲁瓦|Geoffrey", 27),
               kin=[person("", "玛蒂尔达皇后|Empress Matilda", 38, rel="wife"),
                    person(None, "亨利|Henry", 7, rel="son", heir=True, mo=0)]),
    ],
    "almohad": [
        polity("almoravid", "穆拉比特", "Almoravid", "Arab", "marrakesh", r=["mauretania", "hispania_s"],
               ruler=person("", "阿里·伊本·优素福|Ali ibn Yusuf", 56),
               kin=[person("", "塔舒芬|Tashfin", 35, rel="son", heir=True)],
               famous=[famous("", "阿卜杜勒·穆明|Abd al-Mu'min", 46, "general", 2, "none")]),
        polity("hammadid", "哈马德", "Hammadid", "Arab", "cirta", r=["numidia"], ruler=person("", "叶海亚|Yahya", 40)),
        polity("zirid", "齐里", "Zirid", "Arab", "kairouan", r=["africa"], ruler=person("", "哈桑|al-Hasan", 30)),
    ],
    "ghurid": [
        polity("ghaznavid", "伽色尼", "Ghaznavid", "Persepolis", "ghazni", r=["arachosia", "gandhara", "punjab", "sindh"],
               ruler=person("", "巴赫拉姆沙|Bahram Shah", 56)),
    ],
    "chahamana": [
        polity("tomara", "托马拉", "Tomara", "India", "delhi", cities=["delhi", "mathura"], ruler=person("", "阿南加帕拉|Anangpal", 40)),
    ],
    "yadava": [
        polity("chalukya", "西遮娄其", "Western Chalukya", "India", "kalyani", r=["deccan"], ruler=person("", "贾格德卡马拉|Jagadekamalla", 30)),
    ],
    "khmer": [
        polity("champa_kingdom", "占城", "Champa", "India", "champa", r=["champa"], ruler=person("", "阇耶因陀罗跋摩|Jaya Indravarman", 40)),
    ],
}

PEOPLE = {
    "hre": dict(decline={"age": 2}, ruler=person("霍亨施陶芬|Hohenstaufen", "康拉德|Conrad", 47), kin=[
        person("", "苏尔茨巴赫的格特鲁德|Gertrude of Sulzbach", 26, rel="wife"),
        person(None, "亨利|Henry", 3, rel="son", heir=True, mo=0),
        person(None, "腓特烈|Frederick", 18, rel="nephew")]),
    "bohemia": dict(ruler=person("普热米斯尔|Premyslid", "弗拉迪斯拉夫|Vladislaus", 30)),
    "venice": dict(ruler=person("波拉尼|Polani", "彼得罗|Pietro", 50)),
    "papacy": dict(ruler=person("帕帕雷斯基|Papareschi", "格雷戈里奥|Gregorio", 60)),
    "sicily": dict(ruler=person("欧特维尔|Hauteville", "罗杰|Roger", 45), kin=[
        person(None, "罗杰|Roger", 22, rel="son", heir=True), person(None, "威廉|William", 20, rel="son")]),
    "france": dict(r=["gaul_n", "gaul_s"], cities=["poitiers", "bordeaux"], ruler=person("卡佩|Capet", "路易|Louis", 20),
                   kin=[person("", "阿基坦的埃莉诺|Eleanor of Aquitaine", 18, rel="wife")]),
    "toulouse": dict(ruler=person("图卢兹|Toulouse", "阿方斯·茹尔丹|Alfonso Jordan", 37)),
    "england": dict(cities=["rouen"], dynasty="england", ruler=person("布卢瓦|Blois", "斯蒂芬|Stephen", 44), famous=[], kin=[
        person("", "布洛涅的玛蒂尔达|Matilda of Boulogne", 35, rel="wife"), person(None, "尤斯塔斯|Eustace", 10, rel="son", heir=True, mo=0)]),
    "scotland": dict(ruler=person("邓凯尔德|Dunkeld", "大卫|David", 56)),
    "connacht": dict(cities=["tara", "cashel", "dublin"], ruler=person("奥康纳|O'Connor", "特洛|Turlough", 52)),
    "castile": dict(zh="莱昂-卡斯蒂利亚", en="Leon-Castile", cities=["toledo", "burgos", "leon", "oviedo"], dynasty="asturias",
                    ruler=person("伊夫雷亚|Ivrea", "阿方索|Alfonso", 35), kin=[
        person("", "巴塞罗那的贝伦加丽亚|Berengaria of Barcelona", 24, rel="wife"),
        person(None, "桑乔|Sancho", 6, rel="son", heir=True, mo=0), person(None, "费尔南多|Ferdinand", 3, rel="son", mo=0)]),
    "portugal": dict(cities=["coimbra", "porto"], ruler=person("勃艮第|Burgundy", "阿方索|Afonso", 31), kin=[]),
    "aragon": dict(ruler=person("巴塞罗那|Barcelona", "拉蒙·贝伦格尔|Ramon Berenguer", 27), kin=[]),
    "navarre": dict(ruler=person("希门尼斯|Jimenez", "加西亚|Garcia", 28), kin=[person(None, "桑乔|Sancho", 7, rel="son", heir=True)]),
    "hungary": dict(ruler=person("阿尔帕德|Arpad", "贝拉|Bela", 31), kin=[
        person("", "拉斯卡的海伦娜|Helena of Raska", 31, rel="wife"), person(None, "格扎|Geza", 10, rel="son", heir=True, mo=0)]),
    "poland": dict(ruler=person("皮雅斯特|Piast", "弗瓦迪斯瓦夫|Wladyslaw", 35), kin=[person(None, "波列斯瓦夫|Boleslaw", 19, rel="brother", heir=True)]),
    "denmark": dict(ruler=person("埃斯特里德森|Estridsen", "埃里克|Eric", 20), cities=[], kin=[]),
    "sweden": dict(ruler=person("", "斯韦尔克|Sverker", 40)),
    "norway": dict(ruler=person("", "西居尔|Sigurd", 7), kin=[person("", "因吉|Inge", 5, rel="brother")]),
    "vladimir": dict(zh="罗斯托夫-苏兹达尔", en="Rostov-Suzdal", cap="suzdal", cities=["smolensk"], dynasty=None,
                     ruler=person("留里克|Rurik", "尤里·多尔戈鲁基|Yuri Dolgoruky", 41), kin=[]),
    "kiev": dict(ruler=person("留里克|Rurik", "弗谢沃洛德|Vsevolod", 56)),
    "galicia": dict(ruler=person("留里克|Rurik", "沃洛季米尔科|Volodymyrko", 36)),
    "byzantium": dict(
        r=["thrace", "greece", "macedonia", "asia_minor", "crimea", "moesia", "cyprus"], cities=["trebizond", "dyrrhachium", "philippopolis", "tarsus"],
        ruler=person("科穆宁|Komnenos", "约翰|John", 52),
        kin=[person(None, "曼努埃尔|Manuel", 22, rel="son", heir=True)],
        decline={"age": 59, "strain": 35, "graft": 40, "annexation": 50, "bloat": 45, "military": 40, "factionalism": 40, "levies": 45}),
    "rum": dict(r=["cappadocia"], cities=[], ruler=person("塞尔柱|Seljuk", "麦苏德|Mesud", 45), kin=[]),
    "georgia": dict(ruler=person("巴格拉季昂|Bagrationi", "德米特里|Demetrius", 47)),
    "jerusalem": dict(cap="jerusalem", cities=["jerusalem", "acre", "tyre"], ruler=person("安茹|Anjou", "富尔克|Fulk", 49), famous=[], kin=[
        person("", "梅利桑德|Melisende", 35, rel="wife"), person(None, "鲍德温|Baldwin", 10, rel="son", heir=True, mo=0)]),
    "tripoli": dict(ruler=person("图卢兹|Toulouse", "雷蒙|Raymond", 25)),
    "antioch": dict(ruler=person("普瓦捷|Poitiers", "雷蒙|Raymond", 25), kin=[person("", "安条克的康斯坦丝|Constance of Antioch", 12, rel="wife")]),
    "abbasid": dict(ruler=person("阿拔斯|Abbasid", "穆格塔菲|al-Muqtafi", 44)),
    "seljuk": dict(zh="伊拉克塞尔柱", en="Seljuks of Iraq", cities=["susa", "isfahan", "fars", "kerman"], dynasty=None,
                   ruler=person("塞尔柱|Seljuk", "马苏德|Mas'ud", 31)),
    "khwarazm": dict(r=["khwarazm"], of=None, ruler=person("阿努什特勤|Anushtegin", "阿即思|Atsiz", 43), kin=[]),
    "qara_khitai": dict(ruler=person("耶律|Yelü", "大石|Dashi", 53), era_name="康国|Kangguo", era_year=7, kin=[
        person("萧|Xiao", "塔不烟|Tabuyan", 40, rel="wife"), person(None, "夷列|Yilie", 12, rel="son", heir=True, mo=0)]),
    "west_karakhanid": dict(of=None, ruler=person("", "马哈茂德|Mahmud", 30)),
    "song": dict(
        cities=["shouchun", "yangzhou", "nanyang"], era_name="绍兴|Shaoxing", era_year=10,
        ruler=person("赵|Zhao", "构|Gou", 33, f="sg08"),
        kin=[person("吴|Wu", "氏|Shi", 25, rel="wife"), person(None, "瑗|Yuan", 13, rel="son", heir=True), person(None, "璩|Qu", 10, rel="son")],
        famous=[
            famous("秦|Qin", "桧|Hui", 50, "minister", 1, "chief"),
            famous("张|Zhang", "浚|Jun", 43, "minister", 1, "core"),
            famous("赵|Zhao", "鼎|Ding", 55, "minister", 1, "div"),
            famous("胡|Hu", "铨|Quan", 38, "minister", 0, "div"),
            famous("岳|Yue", "飞|Fei", 37, "general", 2, "wuchang"),
            famous("韩|Han", "世忠|Shizhong", 51, "general", 2, "yangzhou"),
            famous("张|Zhang", "俊|Jun", 54, "general", 1, "nanjing"),
            famous("刘|Liu", "锜|Qi", 42, "general", 1, "shouchun"),
            famous("吴|Wu", "璘|Lin", 38, "general", 1, "hanzhong"),
            famous("杨|Yang", "沂中|Yizhong", 38, "general", 1, "general"),
            famous("岳|Yue", "云|Yun", 21, "general", 1, "general"),
            famous("张|Zhang", "宪|Xian", 40, "general", 1, "general"),
            famous("牛|Niu", "皋|Gao", 53, "general", 0, "general"),
        ],
        decline={"age": 13, "strain": 40, "graft": 45, "annexation": 50, "bloat": 55, "military": 50, "factionalism": 60, "levies": 60}),
    "jin": dict(
        cap="huining", r=["hebei", "youyan", "qilu", "henan", "hedong", "guanzhong", "liaodong", "manchuria", "monan", "huaisi"],
        cities=["tianshui", "lanzhou"], era_name="天眷|Tianjuan", era_year=3, old_capital=None,
        ruler=person("完颜|Wanyan", "亶|Dan", 21, f="jj_zongjun"),
        kin=[person("裴满|Peiman", "氏|Shi", 20, rel="wife"),
             person("完颜|Wanyan", "亮|Liang", 18, rel="cousin"),
             person("完颜|Wanyan", "雍|Yong", 17, rel="cousin")],
        famous=[
            famous("完颜|Wanyan", "宗干|Zonggan", 60, "minister", 1, "chief"),
            famous("韩|Han", "企先|Qixian", 58, "minister", 1, "core"),
            famous("张|Zhang", "通古|Tonggu", 52, "minister", 0, "div"),
            famous("完颜|Wanyan", "宗弼|Zongbi", 50, "general", 2, "general"),
            famous("完颜|Wanyan", "撒离喝|Salihe", 45, "general", 1, "xian"),
            famous("李|Li", "成|Cheng", 52, "general", 0, "general"),
            famous("孔|Kong", "彦舟|Yanzhou", 47, "general", 0, "general"),
            famous("郦|Li", "琼|Qiong", 36, "general", 0, "general"),
        ]),
    "xixia": dict(cities=[], ruler=person("嵬名|Weiming", "仁孝|Renxiao", 16, f="xx04"), era_name="大庆|Daqing", era_year=1,
                  kin=[person("曹|Cao", "氏|Shi", 16, rel="wife")],
                  famous=[famous("任|Ren", "得敬|Dejing", 50, "general", 1, "general")]),
    "dali": dict(ruler=person("段|Duan", "和誉|Heyu", 57), kin=[person(None, "正兴|Zhengxing", 25, rel="son", heir=True)]),
    "dai_viet": dict(ruler=person("李|Ly", "天祚|Thien To", 4), era_name="大定|Dai Dinh", era_year=1,
                     famous=[famous("杜|Do", "英武|Anh Vu", 30, "minister", 1, "chief")]),
    "goryeo": dict(ruler=person("王|Wang", "楷|Hae", 31), kin=[person(None, "晛|Hyeon", 13, rel="son", heir=True)],
                   famous=[famous("金|Kim", "富轼|Busik", 65, "minister", 1, "chief")]),
    "japan": dict(ruler=person("", "显仁|Akihito", 21), era_name="保延|Hoen", era_year=6,
                  famous=[famous("藤原|Fujiwara", "忠通|Tadamichi", 43, "minister", 1, "chief"),
                          famous("平|Taira", "忠盛|Tadamori", 44, "general", 1, "general"),
                          famous("源|Minamoto", "为义|Tameyoshi", 44, "general", 1, "general")]),
    "keraites": dict(ruler=person("克烈|Kerait", "忽儿札胡思|Qurjaqus", 40)),
    "khamag": dict(zh="蒙兀", en="Khamag Mongol", ruler=person("孛儿只斤|Borjigin", "合不勒|Khabul", 50), kin=[], famous=[]),
    "naimans": dict(ruler=None),
    "gahadavala": dict(ruler=person("伽哈达伐拉|Gahadavala", "戈文达旃陀罗|Govindachandra", 50)),
    "sena": dict(ruler=person("", "维阇耶犀那|Vijaya Sena", 45)),
    "chaulukya": dict(ruler=person("", "胜狮|Jayasimha", 48)),
    "paramara": dict(ruler=person("", "耶输跋摩|Yashovarman", 40)),
    "ganga": dict(ruler=person("", "阿南塔跋摩|Anantavarman", 63)),
    "chola": dict(ruler=person("", "俱卢东伽|Kulothunga", 40)),
    "polonnaruwa": dict(ruler=person("", "伽阇巴忽|Gajabahu", 25)),
    "pagan": dict(ruler=person("", "阿隆悉都|Alaungsithu", 50)),
    "khmer": dict(r=["khmer", "dvaravati"], ruler=person("", "苏利耶跋摩|Suryavarman", 45)),
    "kediri": dict(ruler=person("", "阇耶巴耶|Jayabaya", 30)),
}

RELATIONS = [
    relation("jin", "song", "war", goal="夺城"),
    relation("jin", "song", "feud", value=60),
    relation("xixia", "jin", "tribute"),
    relation("goryeo", "jin", "tribute"),
    relation("dai_viet", "song", "tribute"),
    relation("khamag", "jin", "feud", value=40),
    relation("khamag", "tatars", "feud", value=40),
    relation("keraites", "naimans", "feud"),
    relation("west_karakhanid", "great_seljuk", "tribute"),
    relation("khwarazm", "great_seljuk", "tribute"),
    relation("ghaznavid", "great_seljuk", "tribute"),
    relation("qara_khitai", "great_seljuk", "feud", value=30),
    relation("jerusalem", "burid", "alliance"),
    relation("jerusalem", "antioch", "alliance"),
    relation("jerusalem", "tripoli", "alliance"),
    relation("jerusalem", "edessa_county", "alliance"),
    relation("zengid", "edessa_county", "feud", value=40),
    relation("zengid", "burid", "feud", value=30),
    relation("fatimid", "jerusalem", "feud", value=30),
    relation("antioch", "byzantium", "tribute"),
    relation("danishmend", "byzantium", "feud", value=30),
    relation("anjou", "england", "war", goal="夺城"),
    relation("hre", "sicily", "feud", value=30),
    relation("byzantium", "sicily", "feud", value=30),
    relation("byzantium", "hre", "pact"),
    relation("papacy", "sicily", "friend"),
    relation("castile", "almoravid", "war", goal="收复"),
    relation("portugal", "almoravid", "war", goal="收复"),
    relation("aragon", "almoravid", "feud", value=30),
    relation("hungary", "byzantium", "feud"),
]

LATE_DEATHS = [
    death("刘锜之死", ("刘锜呕血而死", "Liu Qi dies coughing blood"), 22, "song", "刘锜", ("呕血而死", "died coughing blood")),
    death("张浚之死", ("张浚病逝于北伐途中", "Zhang Jun the chancellor dies during the northern campaign"), 24, "song", "张浚", ("病逝于北伐途中", "died on the northern campaign")),
    death("吴璘之死", ("吴璘病逝于兴州", "Wu Lin dies at Xingzhou"), 27, "song", "吴璘", ("病逝于兴州", "died of illness at Xingzhou")),
]

EVENTS = [
    event("绍兴和议", T("绍兴和议：宋向金称臣，以淮水、大散关为界", "Peace of Shaoxing: Song submits to Jin, the border set at the Huai and Dasan Pass"), 1,
          [{"kind": "holds_city", "a": "jin", "city": "kaifeng"}, {"kind": "at_war", "a": "jin", "b": "song"}],
          [{"kind": "cede", "a": "song", "b": "jin", "city": "nanyang"},
           rel_step("song", "jin", "truce"),
           rel_step("song", "jin", "tribute")]),
    event("卡特万之战", T("西辽大败塞尔柱于卡特万", "Qara Khitai crushes the Seljuks at Qatwan"), 1,
          [{"kind": "alive", "a": "qara_khitai"}, {"kind": "alive", "a": "great_seljuk"}],
          [rel_step("qara_khitai", "great_seljuk", "war", goal="夺城"),
           rel_step("west_karakhanid", "qara_khitai", "tribute")]),
    event("岳飞之死", T("大将岳飞下狱被杀，天下冤之", "The great general Yue Fei is imprisoned and put to death; the realm mourns the injustice"), 2,
          [{"kind": "holds_city", "a": "jin", "city": "kaifeng"}],
          [{"kind": "dies", "a": "song", "person": "岳云、张宪、岳飞", "name": T("以莫须有之罪被杀", "put to death on a fabricated charge")}]),
    death("完颜宗干之死", ("金太师完颜宗干病逝", "The Jin Grand Preceptor Wanyan Zonggan dies"), 1, "jin", "完颜宗干", ("病逝", "died of illness")),
    death("耶律大石之死", ("西辽开国之主耶律大石病逝", "Yelü Dashi, founder of Qara Khitai, dies"), 3, "qara_khitai", "耶律大石", ("病逝", "died of illness")),
    event("埃德萨陷落", T("赞吉攻陷埃德萨", "Zengi takes Edessa"), 4,
          [{"kind": "holds_city", "a": "edessa_county", "city": "edessa"}, {"kind": "alive", "a": "zengid"}],
          [{"kind": "cede", "a": "edessa_county", "b": "zengid", "city": "edessa"}]),
    event("穆瓦希德起兵", T("穆瓦希德自阿特拉斯山起兵，夺取非斯", "The Almohads rise out of the Atlas and take Fez"), 5,
          [{"kind": "holds_city", "a": "almoravid", "city": "fez"}],
          [{"kind": "rebel", "a": "almoravid", "b": "almohad", "city": "fez", "cities": ["sijilmasa", "tingis"],
            "name": T("穆瓦希德", "Almohad"), "person": "阿卜杜勒·穆明"}]),
    death("赞吉之死", ("赞吉围城时为奴仆所杀", "Zengi is murdered by a slave during a siege"), 6, "zengid", "伊马德丁·赞吉", ("围城时为奴仆所杀", "murdered by a slave during a siege")),
    death("赵鼎之死", ("贬谪吉阳的旧相赵鼎绝食而死", "The exiled former chancellor Zhao Ding starves himself to death"), 7, "song", "赵鼎", ("绝食而死于吉阳军", "starved himself to death in exile")),
    event("第二次十字军东征", T("教宗号召十字军东征，法兰西与神圣罗马出兵", "The pope calls a crusade; France and the Empire march east"), 7,
          [{"kind": "holds_city", "a": "zengid", "city": "edessa"}],
          [rel_step("france", "zengid", "war", goal="收复"), rel_step("hre", "zengid", "war", goal="收复")]),
    death("完颜宗弼之死", ("金都元帅完颜宗弼病逝", "The Jin Marshal Wanyan Zongbi dies"), 8, "jin", "完颜宗弼", ("病逝", "died of illness")),
    event("海陵弑君", T("完颜亮弑君自立", "Wanyan Liang murders the emperor and takes the throne"), 9,
          [{"kind": "alive", "a": "jin"}],
          [{"kind": "heir", "a": "jin", "person": "完颜亮"},
           {"kind": "dies", "a": "jin", "person": "完颜亶", "name": T("为完颜亮所弑", "murdered by Wanyan Liang")}]),
    death("韩世忠之死", ("韩世忠闭门谢客，病逝临安", "Han Shizhong, long retired, dies at Lin'an"), 11, "song", "韩世忠", ("病逝于临安", "died of illness at Lin'an")),
    event("金迁都燕京", T("金迁都燕京，上京宫室尽毁", "Jin moves its capital to Yanjing and razes the old palaces"), 13,
          [{"kind": "holds_city", "a": "jin", "city": "beijing"}],
          [{"kind": "move_capital", "a": "jin", "city": "beijing"}]),
    death("张俊之死", ("张俊病逝", "Zhang Jun dies"), 14, "song", "张俊", ("病逝", "died of illness")),
    death("秦桧之死", ("秦桧病死，朝野相庆", "Qin Hui dies and the court breathes again"), 15, "song", "秦桧", ("病死", "died of illness")),
    event("海陵南征", T("金主大举南侵，败于采石，死于瓜洲兵变", "The Jin ruler invades in force, loses at Caishi and dies in a mutiny at Guazhou"), 21,
          [{"kind": "alive", "a": "jin"}, {"kind": "alive", "a": "song"}],
          [rel_step("jin", "song", "war", goal="夺城"),
           {"kind": "heir", "a": "jin", "person": "完颜雍"},
           {"kind": "dies", "a": "jin", "person": "完颜亮", "name": T("死于瓜洲兵变", "killed in a mutiny at Guazhou")}]),
]

WILD_RENAME = {"ayyubid": "fatimid", "almohad": "almoravid", "ghurid": "ghaznavid", "chahamana": "tomara"}
WILD_EXACT = {"syrian_desert": "burid", "negev": "jerusalem", "judaean_desert": "jerusalem", "balkan_mountains": "byzantium",
              "pindus": "byzantium", "khentii": "khamag"}
WILD_DROP = {"yemen_highlands"}


def main():
    root = sys.argv[1]
    data = update_cities(root, [], ALTS)
    active = {c["id"] for c in data["cities"] if c.get("from", -99999) <= 1140 <= c.get("to", 99999)}
    era = copy.deepcopy(load_era(root, "crusades"))
    era.update(
        id="song_jin", year=1140, focus="east", order=4,
        zh="绍兴北伐", en="Shaoxing Northern Expedition",
        dzh="1140年，南宋绍兴十年、金天眷三年。金人撕毁和约，完颜宗弼再度南下；岳飞自鄂州北伐，转战郾城、颍昌，兵锋直指汴京，"
            "韩世忠、刘锜、吴璘各守淮东、顺昌、川陕。临安的赵构与秦桧却一心议和，金牌正在路上。"
            "北边蒙兀诸部初盛，西夏、大理、高丽各守其疆，耶律大石的西辽即将东西称雄；西方第二次十字军东征前夜，赞吉虎视埃德萨。",
        den="AD 1140, the tenth year of Shaoxing. Jin has torn up the treaty and Wanyan Zongbi rides south again. Yue Fei marches north from Ezhou "
            "through Yancheng and Yingchang towards Bianjing, with Han Shizhong, Liu Qi and Wu Lin holding the Huai and Sichuan fronts. "
            "But at Lin'an, Zhao Gou and Qin Hui want only peace. The Mongols stir in the north, Yelü Dashi's Qara Khitai is about to win Central Asia, "
            "and in the West Zengi eyes Edessa on the eve of the Second Crusade.")
    ranks = dict(era["ranks"])
    ranks.update(huining=1, edessa=2, tours=2, cirta=2, kairouan=2, caesarea=2, kalyani=2, champa=1, nanyang=2, suzdal=2)
    era["ranks"] = ranks

    polities = []
    replaced = set()
    for item in era["polities"]:
        pid = item["id"]
        if pid not in DROP:
            changes = dict(PEOPLE.get(pid, {}))
            for key, value in changes.items():
                if value is None:
                    item.pop(key, None)
                else:
                    item[key] = value
            polities.append(item)
        if pid in REPLACE:
            polities.extend(REPLACE[pid])
            replaced.add(pid)
    missing = set(REPLACE) - replaced
    if missing:
        raise SystemExit("底稿里没有：%s" % sorted(missing))

    realms = {p["id"]: p for p in polities}
    # 金：都城在上京会宁，上京路就是直辖；燕京一带另为一路(1190 底稿的中都路)
    jin = realms["jin"]
    old_core = jin["seats"]
    new_divisions = []
    for division in jin["divisions"]:
        name = division["name"].split("|")[0]
        if name == "上京路":
            jin["seats"] = division["seats"]
            continue
        if name == "中都路":
            division = dict(division, name="燕京路|Yanjing Circuit", cities=["beijing", "zhuozhou"], seats=old_core + division["seats"])
        if name == "南京路":
            division = dict(division, name="河南路|Henan Circuit", cities=[cid for cid in division["cities"] if cid != "nanyang"])
        if name == "凤翔路":
            division = dict(division, cities=division["cities"] + ["lanzhou"])
        new_divisions.append(division)
    jin["divisions"] = new_divisions
    # 宋：襄阳六郡含唐、邓(南阳)
    for division in realms["song"]["divisions"]:
        if division["name"].startswith("京西南路"):
            division["cities"] = division["cities"] + ["nanyang"]
    # 拜占庭：1137 收回奇里乞亚；保加利亚仍是行省(1185 才复国)
    byz = realms["byzantium"]
    byz["divisions"] = byz["divisions"] + [
        {"name": "保加利亚|Bulgaria", "cities": ["serdica", "pliska", "durostorum"]},
        {"name": "奇里乞亚|Cilicia", "cities": ["tarsus"]},
        {"name": "塞浦路斯|Cyprus", "cities": ["cyprus"]},
    ]
    for p in polities:
        p["cities"] = [cid for cid in p.get("cities", []) if cid in active] if "cities" in p else p.get("cities")
        if p.get("cities") is None:
            p.pop("cities", None)
        for division in p.get("divisions", []):
            division["cities"] = [cid for cid in division.get("cities", []) if cid in active]
    era["polities"] = polities
    era["relations"] = RELATIONS
    era["events"] = EVENTS + LATE_DEATHS
    wild = {}
    for unit, owner in era["wilderness"].items():
        if unit in WILD_DROP:
            continue
        wild[unit] = WILD_EXACT.get(unit, WILD_RENAME.get(owner, owner))
    era["wilderness"] = wild
    write_era(root, era)
    print("song_jin", era["year"], len(era["polities"]), "国", len(era["events"]), "件事")


if __name__ == "__main__":
    main()
