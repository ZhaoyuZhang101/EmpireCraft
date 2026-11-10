"""日军侵华(1937 民国二十六年、昭和十二年)：写成 eras/sino_japanese.json，并往城表补近代城市与改名。

用户 2026-11-10 "剧情模式加上日军侵华剧本""现代政体如果要做的符合历史但是受限于机制的话优先完善机制""流亡政府机制"。
不以旧时代为底稿(1638 以后三百年，各国几乎全换)，整份从头写。

开局七七事变前后：
  · 中华民国国民政府(蒋中正，国民党训政、一党)，地方实力派晋绥(阎锡山)、冀察(宋哲元)、山东(韩复榘)、四川(刘湘)、
    云南(龙云)、广西(李宗仁)、宁夏(马鸿逵)、青海(马步芳)、新疆(盛世才)是民国的成员，留兵留财政、随中央出征(autonomy)；
  · 陕甘宁边区(毛泽东，共产党执政)是另一个政府，与国民政府停战，日军压境时由统一战线机制共同抗日；
  · 大日本帝国(昭和天皇、近卫文麿内阁，君主立宪)，1910 年已吞并朝鲜(朝鲜是日本领土)，台湾总督府是它的殖民地，关东州的大连归日本；
    满洲国(溥仪，康德四年)、蒙古军政府(德王)、冀东防共自治政府是它的傀儡；
  · 大韩民国临时政府开局流亡中华民国(exile)，埃塞俄比亚皇帝流亡英国；
  · 苏联、蒙古人民共和国，英法荷葡比的亚非殖民地(自成一国，colony_of 宗主国)，欧洲各国，西班牙内战正酣。
剧情事件：迁都重庆、华北临时政府、维新政府、汪精卫出走与汪伪国民政府、蒙疆、皖南事变、太平洋战争、苏联出兵、日本投降
(献还东北、台湾，朝鲜南北分治)、国共内战、中华人民共和国；欧洲的德奥合并、吞并捷克、波兰之战、德苏战争。
条件按局面走(日军打到哪里才立哪个傀儡、日本还在和民国打才会投降)，别的交给 AI。

默认地图范围东亚半幅(frame eastasia)；全图时欧洲、非洲各国照样开局。
新城的 from 写 1639 以后(或真实建城年)：1638 以前的预设剧本开局不变。

用法：python -I -X utf8 sino_japanese_source.py <模组目录>，之后跑 check_eras.py、check_scenario.py。
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from preset_tools import T, death, event, famous, person, polity, rel_step, relation, update_cities, write_era  # noqa: E402
import sino_japanese_dejure  # noqa: E402

YEAR = 1937

# ---------------------------------------------------------------- 城表

NEW_CITIES = [
    # 中国：1638 以前就有的写 1639(不进明清鼎革)，以后建的写真实年份
    {"id": "shanghai", "zh": "上海", "en": "Shanghai", "x": 121.47, "y": 31.23, "r": "jiangdong", "k": 1, "from": 1639},
    {"id": "tianjin", "zh": "天津", "en": "Tianjin", "x": 117.2, "y": 39.13, "r": "youyan", "k": 1, "from": 1639},
    {"id": "zhangjiakou", "zh": "张家口", "en": "Kalgan", "x": 114.88, "y": 40.82, "r": "youyan", "k": 2, "from": 1639},
    {"id": "chengde", "zh": "承德", "en": "Chengde", "x": 117.93, "y": 40.97, "r": "youyan", "k": 2, "from": 1703},
    {"id": "dehua", "zh": "德化", "en": "Dehua", "x": 114.01, "y": 41.9, "r": "monan", "k": 2, "from": 1923},
    {"id": "yanan", "zh": "延安", "en": "Yan'an", "x": 109.49, "y": 36.6, "r": "guanzhong", "k": 2, "from": 1639},
    {"id": "yichang", "zh": "宜昌", "en": "Yichang", "x": 111.29, "y": 30.69, "r": "jingchu", "k": 3, "from": 1639},
    {"id": "hengyang", "zh": "衡阳", "en": "Hengyang", "x": 112.57, "y": 26.9, "r": "jingchu", "k": 3, "from": 1639},
    {"id": "liuzhou", "zh": "柳州", "en": "Liuzhou", "x": 109.41, "y": 24.33, "r": "guangxi", "k": 3, "from": 1639},
    {"id": "hongkong", "zh": "香港", "en": "Hong Kong", "x": 114.17, "y": 22.28, "r": "lingnan", "k": 2, "from": 1841},
    {"id": "dalian", "zh": "大连", "en": "Dalian", "x": 121.6, "y": 38.91, "r": "liaodong", "k": 2, "from": 1898},
    {"id": "harbin", "zh": "哈尔滨", "en": "Harbin", "x": 126.63, "y": 45.75, "r": "manchuria", "k": 1, "from": 1898},
    {"id": "qiqihar", "zh": "齐齐哈尔", "en": "Qiqihar", "x": 123.95, "y": 47.35, "r": "manchuria", "k": 2, "from": 1691},
    {"id": "jilin", "zh": "吉林", "en": "Jilin", "x": 126.55, "y": 43.84, "r": "manchuria", "k": 2, "from": 1673},
    {"id": "heihe", "zh": "黑河", "en": "Heihe", "x": 127.5, "y": 50.25, "r": "manchuria", "k": 3, "from": 1683},
    {"id": "hailar", "zh": "海拉尔", "en": "Hailar", "x": 119.76, "y": 49.21, "r": "manchuria", "k": 3, "from": 1734},
    {"id": "vladivostok", "zh": "海参崴", "en": "Vladivostok", "x": 131.9, "y": 43.12, "r": "manchuria", "k": 2, "from": 1860},
    {"id": "khabarovsk", "zh": "伯力", "en": "Khabarovsk", "x": 135.07, "y": 48.48, "r": "manchuria", "k": 2, "from": 1858},
    {"id": "urumqi", "zh": "乌鲁木齐", "en": "Urumqi", "x": 87.6, "y": 43.8, "r": "dzungaria", "k": 1, "from": 1763},
    {"id": "ili", "zh": "伊宁", "en": "Yining", "x": 81.3, "y": 43.9, "r": "dzungaria", "k": 2, "from": 1762},
    {"id": "hami", "zh": "哈密", "en": "Hami", "x": 93.5, "y": 42.82, "r": "xiyu", "k": 2, "from": 1639},
    # 朝鲜、日本
    {"id": "busan", "zh": "釜山", "en": "Busan", "x": 129.04, "y": 35.1, "r": "korea_s", "k": 2, "from": 1639},
    {"id": "nagoya", "zh": "名古屋", "en": "Nagoya", "x": 136.9, "y": 35.18, "r": "japan_kinai", "k": 2, "from": 1639},
    {"id": "hiroshima", "zh": "广岛", "en": "Hiroshima", "x": 132.46, "y": 34.39, "r": "japan_kinai", "k": 2, "from": 1639},
    {"id": "nagasaki", "zh": "长崎", "en": "Nagasaki", "x": 129.87, "y": 32.75, "r": "japan_kyushu", "k": 3, "from": 1639},
    {"id": "sapporo", "zh": "札幌", "en": "Sapporo", "x": 141.35, "y": 43.06, "r": "japan_tohoku", "k": 2, "from": 1869},
    # 西伯利亚、中亚
    {"id": "irkutsk", "zh": "伊尔库茨克", "en": "Irkutsk", "x": 104.3, "y": 52.29, "r": "siberia", "k": 2, "from": 1661},
    {"id": "chita", "zh": "赤塔", "en": "Chita", "x": 113.5, "y": 52.03, "r": "siberia", "k": 3, "from": 1653},
    {"id": "novosibirsk", "zh": "新西伯利亚", "en": "Novosibirsk", "x": 82.93, "y": 55.03, "r": "siberia", "k": 2, "from": 1893},
    {"id": "alma_ata", "zh": "阿拉木图", "en": "Alma-Ata", "x": 76.9, "y": 43.25, "r": "kazakh", "k": 2, "from": 1854},
    # 东南亚、南亚
    {"id": "saigon", "zh": "西贡", "en": "Saigon", "x": 106.7, "y": 10.78, "r": "khmer", "k": 1, "from": 1698},
    {"id": "singapore", "zh": "新加坡", "en": "Singapore", "x": 103.85, "y": 1.29, "r": "malaya", "k": 1, "from": 1819},
    {"id": "rangoon", "zh": "仰光", "en": "Rangoon", "x": 96.16, "y": 16.8, "r": "burma", "k": 1, "from": 1755},
    {"id": "surabaya", "zh": "泗水", "en": "Surabaya", "x": 112.75, "y": -7.25, "r": "java", "k": 2, "from": 1639},
    {"id": "calcutta", "zh": "加尔各答", "en": "Calcutta", "x": 88.36, "y": 22.57, "r": "bengal", "k": 1, "from": 1690},
    {"id": "bombay", "zh": "孟买", "en": "Bombay", "x": 72.88, "y": 19.07, "r": "deccan", "k": 1, "from": 1661},
    {"id": "madras", "zh": "马德拉斯", "en": "Madras", "x": 80.27, "y": 13.08, "r": "tamil", "k": 2, "from": 1639},
    # 欧洲、西亚、非洲
    {"id": "st_petersburg", "zh": "圣彼得堡", "en": "Saint Petersburg", "x": 30.3, "y": 59.94, "r": "rus_n", "k": 1, "from": 1703},
    {"id": "tsaritsyn", "zh": "伏尔加格勒", "en": "Volgograd", "x": 44.5, "y": 48.7, "r": "volga", "k": 2, "from": 1639},
    {"id": "riyadh", "zh": "利雅得", "en": "Riyadh", "x": 46.72, "y": 24.69, "r": "arabia", "k": 2, "from": 1639},
    {"id": "addis_ababa", "zh": "亚的斯亚贝巴", "en": "Addis Ababa", "x": 38.74, "y": 9.03, "r": "aksum", "k": 1, "from": 1886},
    {"id": "khartoum", "zh": "喀土穆", "en": "Khartoum", "x": 32.53, "y": 15.5, "r": "nubia", "k": 2, "from": 1821},
    {"id": "lagos", "zh": "拉各斯", "en": "Lagos", "x": 3.38, "y": 6.45, "r": "yoruba", "k": 2, "from": 1639},
    {"id": "leopoldville", "zh": "利奥波德维尔", "en": "Leopoldville", "x": 15.3, "y": -4.32, "r": "kongo", "k": 2, "from": 1881},
    {"id": "johannesburg", "zh": "约翰内斯堡", "en": "Johannesburg", "x": 28.05, "y": -26.2, "r": "nguni", "k": 1, "from": 1886},
]

# 近代改名：只加 1638 以后的旧称、改现今的名字，1638 以前各预设剧本里的叫法不变
ALTS = {
    "heihe": ["1912|瑷珲|Aigun"],
    "urumqi": ["1953|迪化|Dihua"],
    "ili": ["1913|宁远|Ningyuan"],
    "st_petersburg": ["1913|圣彼得堡|Saint Petersburg", "1923|彼得格勒|Petrograd", "1990|列宁格勒|Leningrad"],
    "tsaritsyn": ["1924|察里津|Tsaritsyn", "1960|斯大林格勒|Stalingrad"],
    "beijing": ["600|蓟|Ji", "937|幽州|Youzhou", "1152|燕京|Yanjing", "1271|中都|Zhongdu", "1367|大都|Dadu", "1402|北平|Beiping",
                "1927|北京|Beijing", "1948|北平|Peiping"],
    "shenyang": ["1633|沈阳|Shenyang", "1911|盛京|Mukden", "1928|奉天|Fengtian", "1931|沈阳|Shenyang", "1945|奉天|Mukden"],
    "fuyu": ["700|扶余城|Buyeo", "1799|黄龙府|Huanglong", "1931|长春|Changchun", "1945|新京|Hsinking"],
    "hohhot": ["1580|云中|Yunzhong", "1912|归化|Guihua", "1953|归绥|Guisui"],
    "baotou": ["400|九原|Jiuyuan", "1808|丰州|Fengzhou"],
    "yinchuan": ["1019|灵州|Lingzhou", "1227|兴庆|Xingqing", "1944|宁夏|Ningxia"],
    "ye": ["580|邺|Ye", "1193|相州|Xiangzhou", "1912|彰德|Zhangde"],
    "linfen": ["1911|平阳|Pingyang"],
    "changzhi": ["580|上党|Shangdang", "1528|潞州|Luzhou", "1911|潞安|Lu'an"],
    "xuchang": ["-200|许|Xu", "580|许昌|Xuchang", "1105|许州|Xuzhou", "1141|颍昌|Yingchang", "1912|许州|Xuzhou"],
    "chen": ["580|陈|Chen", "1912|陈州|Chenzhou"],
    "shouchun": ["600|寿春|Shouchun", "1911|寿州|Shouzhou"],
    "wuwei": ["-1|姑臧|Guzang", "500|武威|Wuwei", "1912|凉州|Liangzhou"],
    "zhangye": ["500|张掖|Zhangye", "1912|甘州|Ganzhou"],
    "jiuquan": ["500|酒泉|Jiuquan", "1912|肃州|Suzhou"],
    "baoji": ["1912|陈仓|Chencang"],
    "zhending": ["-200|东垣|Dongyuan", "1722|真定|Zhending"],
    "jimo": ["1890|即墨|Jimo"],
    "ninguta": ["1909|宁古塔|Ninguta"],
    "tuul": ["1638|土兀剌|Tuul", "1923|库伦|Urga"],
    "edo": ["1868|江户|Edo"],
    "osaka": ["1495|难波|Naniwa", "1868|大坂|Osaka"],
    "hanseong": ["475|慰礼城|Wiryeseong", "1067|汉州|Hanju", "1394|汉阳|Hanyang", "1910|汉城|Hanseong", "1945|京城|Keijo"],
    "tayouan": ["1661|大员|Tayouan", "1886|台湾府|Taiwan-fu"],
    "keelung": ["1874|鸡笼|Keelung"],
    "shuri": ["1878|首里|Shuri"],
    "hanoi": ["600|交趾|Jiaozhi", "1009|交州|Jiaozhou", "1830|升龙|Thang Long"],
    "oudong": ["1865|乌栋|Oudong"],
    "ava": ["1856|阿瓦|Ava"],
    "nakhon_pathom": ["1781|那空帕通|Nakhon Pathom"],
    "jambi": ["1400|末罗游|Melayu"],
    "batavia": ["1941|巴达维亚|Batavia"],
    "rayy": ["1785|雷伊|Rayy"],
    "debal": ["1728|提婆|Debal"],
}
# 改了现今名字的城(zh/en)：旧名进上面的 alt
RENAMED = {
    "fuyu": ("长春", "Changchun"), "hohhot": ("呼和浩特", "Hohhot"), "baotou": ("包头", "Baotou"), "yinchuan": ("银川", "Yinchuan"),
    "ye": ("安阳", "Anyang"), "linfen": ("临汾", "Linfen"), "changzhi": ("长治", "Changzhi"), "xuchang": ("许昌", "Xuchang"),
    "chen": ("淮阳", "Huaiyang"), "shouchun": ("寿县", "Shouxian"), "wuwei": ("武威", "Wuwei"), "zhangye": ("张掖", "Zhangye"),
    "jiuquan": ("酒泉", "Jiuquan"), "baoji": ("宝鸡", "Baoji"), "zhending": ("正定", "Zhengding"), "jimo": ("青岛", "Qingdao"),
    "ninguta": ("宁安", "Ning'an"), "tuul": ("乌兰巴托", "Ulaanbaatar"), "edo": ("东京", "Tokyo"), "osaka": ("大阪", "Osaka"),
    "tayouan": ("台南", "Tainan"), "keelung": ("台北", "Taipei"), "shuri": ("那霸", "Naha"), "hanoi": ("河内", "Hanoi"),
    "oudong": ("金边", "Phnom Penh"), "ava": ("曼德勒", "Mandalay"), "nakhon_pathom": ("曼谷", "Bangkok"), "jambi": ("占碑", "Jambi"),
    "batavia": ("雅加达", "Jakarta"), "rayy": ("德黑兰", "Tehran"), "debal": ("卡拉奇", "Karachi"),
}

# 本时代的城市等级：各国都城至少 2(最小地图只放 0~2 级)，近代大城升格
RANKS = {
    "nanjing": 1, "chongqing": 1, "wuchang": 1, "guangzhou": 1, "xian": 1, "beijing": 1, "taiyuan": 1, "jinan": 1, "chengdu": 1,
    "kunming": 1, "guilin": 2, "yinchuan": 2, "xining": 2, "lanzhou": 2, "guiyang": 2, "fuyu": 1, "shenyang": 1, "shanhaiguan": 2,
    "hanseong": 1, "pyongyang": 1, "keelung": 2, "tayouan": 2, "edo": 1, "osaka": 1, "lhasa": 1, "tuul": 1, "ordu": 3,
    "moscow": 1, "berlin": 1, "warsaw": 1, "prague": 1, "aquincum": 1, "bucharest": 1, "serdica": 1, "singidunum": 1, "madrid": 1,
    "valencia": 2, "burgos": 2, "lisbon": 1, "amsterdam": 1, "brussels": 1, "copenhagen": 1, "oslo": 2, "stockholm": 1, "turku": 2,
    "dublin": 2, "vindobona": 1, "danzig": 2, "ancyra": 1, "rayy": 1, "kabul": 2, "sanaa": 2, "muscat": 2, "ctesiphon": 1,
    "delhi": 1, "batavia": 2, "manila": 2, "hanoi": 1, "nakhon_pathom": 1, "colombo": 2, "mombasa": 2, "luanda": 2, "algiers": 2,
    "tripoli": 2, "jerusalem": 1, "damascus": 1, "cyprus": 2, "dyrrhachium": 2, "macau": 2, "sofala": 2, "timbuktu": 2,
    "mutapa": 2, "aden": 2, "goa": 2, "loango": 2, "tingis": 2, "kano": 2, "kedah": 2,
}

# ---------------------------------------------------------------- 文化、科技、制度

# 地区本时代的文化：中国本部(含东北、热河)写现代中国；新疆照旧
CHINA_REGIONS = ["guanzhong", "longxi", "hexi", "hanzhong", "bashu", "nanzhong", "hedong", "henan", "hebei", "youyan", "liaodong",
                 "qilu", "huaisi", "jingchu", "jiangdong", "jiangxi", "minyue", "lingnan", "guangxi", "qiongya", "manchuria", "taiwan"]
CULTURE = {region: "China" for region in CHINA_REGIONS}
CULTURE.update({
    "hetao": "China", "jiaozhi": "India", "champa": "India", "bohemia": "Slavonic", "crimea": "Slavonic", "pontic": "Slavonic",
    "volga": "Slavonic", "mesopotamia": "Arab", "syria": "Arab", "egypt": "Egypt", "cyrenaica": "Arab", "africa": "Arab",
    "numidia": "Arab", "mauretania": "Arab", "asia_minor": "Arab", "cappadocia": "Arab", "thrace": "Arab", "xiyu": "Youmu",
})

TECH = {"*": 7, "China": 8, "Huaxia": 8, "Japan": 8, "Frankish": 8, "Germanic": 8, "Roma": 8, "Slavonic": 8, "Viking": 8,
        "Kosher": 8, "Arab": 7, "Egypt": 7, "Persepolis": 7, "India": 7, "Youmu": 7, "Manzhou": 7, "Xizang": 6,
        "Swahili": 6, "Bantu": 5, "Zulu": 5}
# 1937 年各国手里的新式武器：日、英、法、德、意、苏有飞机坦克，日英有航母；中国没有飞机工业、汽车与石化
TECH_EXTRA = {
    "Japan": ["radio", "aviation", "naval_aviation", "armored_warfare", "modern_armor"],
    "Frankish": ["radio", "aviation", "naval_aviation", "armored_warfare", "modern_armor", "naval_radar"],
    "Germanic": ["radio", "aviation", "armored_warfare", "modern_armor"],
    "Roma": ["radio", "aviation", "armored_warfare"],
    "Slavonic": ["radio", "aviation", "armored_warfare", "modern_armor"],
    "Viking": ["radio", "aviation"],
}
TECH_SKIP = {"*": ["mythril_forging"], "China": ["motorization", "petrochemistry", "modern_warships"],
             "Huaxia": ["motorization", "petrochemistry", "modern_warships"], "Youmu": ["civil_examination"],
             "Xizang": ["civil_examination"]}

PARTY_IDEAS = ["lift_party_ban", "universal_suffrage", "ideology_capitalism", "ideology_social_democracy", "ideology_socialism",
               "ideology_communism", "ideology_authoritarianism", "ideology_fascism", "ideology_religious_democracy"]
WESTERN = ["western_feudalization", "western_centralized_monarchy", "western_mercantilism", "western_military_revolution",
           "western_enlightenment", "western_great_charter", "western_parliamentary_taxation"]
INST = {
    # 现代中国：废除君主制(连同党禁、各理念)，北洋以来的新军、自强、预备立宪
    "China": ["huaxia_abolish_monarchy", "huaxia_self_strengthening", "huaxia_new_army", "huaxia_constitutional_preparation"] +
             ["huaxia_" + idea for idea in PARTY_IDEAS],
    # 华夏(满洲国、朝鲜)：帝制官僚，不开党禁(免得同文化独立国被改现代政体)
    "Huaxia": ["huaxia_imperial_rule", "huaxia_prefecture_county_bureaucracy", "huaxia_registered_households", "huaxia_three_departments",
               "huaxia_examination_officialdom", "huaxia_grand_secretariat", "huaxia_single_whip_reform", "huaxia_land_poll_merger",
               "huaxia_self_strengthening", "huaxia_new_army", "huaxia_constitutional_preparation"],
    # 日本：明治维新以来的地租改正、征兵、殖产兴业、政党、普选；不废君
    "Japan": ["japan_meiji_restoration", "japan_land_tax_reform", "japan_conscription", "japan_industrial_promotion"] +
             ["japan_" + idea for idea in PARTY_IDEAS],
    "Frankish": WESTERN + ["roma_" + idea for idea in PARTY_IDEAS],
    "Germanic": WESTERN + ["roma_" + idea for idea in PARTY_IDEAS],
    "Roma": WESTERN + ["roma_" + idea for idea in PARTY_IDEAS],
    "Slavonic": WESTERN + ["roma_" + idea for idea in PARTY_IDEAS],
    "Viking": WESTERN + ["roma_" + idea for idea in PARTY_IDEAS],
    "Kosher": WESTERN,
    "Arab": ["arabic_sultanate_bureaucracy", "arabic_gunpowder_army"] + ["arabic_" + idea for idea in PARTY_IDEAS],
    "Egypt": ["arabic_sultanate_bureaucracy", "arabic_gunpowder_army"],
    "Persepolis": ["arabic_sultanate_bureaucracy", "arabic_gunpowder_army"],
    "Youmu": ["youmu_settled_bureaucracy"] + ["youmu_" + idea for idea in PARTY_IDEAS],
    "India": ["origins_royal_power", "origins_tax_reform", "origins_feudalization"],
}


def M(regime, ideology="", party=None, one=False, parties=None):
    """modern 块：政体、执政理念、执政党(简体, 英文)、一党专政、别的政党 [(理念, 简体, 英文, 党魁)]。"""
    item = {"regime": regime}
    if ideology:
        item["ideology"] = ideology
    if party:
        item["party"] = T(*party)
    if one:
        item["one_party"] = True
    if parties:
        item["parties"] = [dict({"ideology": i, "name": T(zh, en)}, **({"leader": lead} if lead else {}))
                           for i, zh, en, lead in parties]
    return item


# ---------------------------------------------------------------- 中国

ROC = polity(
    "roc", "中华民国", "Republic of China", "China", "nanjing",
    r=["jiangdong", "huaisi", "jiangxi", "minyue", "lingnan", "qiongya", "jingchu", "henan", "guanzhong", "hanzhong", "longxi"],
    cities=["guiyang", "shanghai", "xian", "baoji"], title=T("委员长", "Generalissimo"), emp=True, old_capital="beijing",
    modern=M("republic", "Centrism", ("中国国民党", "Kuomintang"), one=True),
    ruler=person("蒋|Chiang", "中正|Chung-cheng", 49),
    kin=[person("宋|Soong", "美龄|Mei-ling", 39, rel="wife"),
         person("蒋|Chiang", "经国|Ching-kuo", 27, rel="son"),
         person("蒋|Chiang", "纬国|Wei-kuo", 20, rel="son")],
    famous=[
        famous("林|Lin", "森|Sen", 69, "minister", 1, "chief"),
        famous("汪|Wang", "精卫|Jingwei", 54, "minister", 1, "core"),
        famous("孔|Kung", "祥熙|Hsiang-hsi", 56, "minister", 1, "core"),
        famous("宋|Soong", "子文|Tse-ven", 43, "minister", 1, "div"),
        famous("王|Wang", "宠惠|Chung-hui", 56, "minister", 0, "div"),
        famous("陈|Chen", "立夫|Li-fu", 37, "minister", 0, "div"),
        famous("陈|Chen", "公博|Gongbo", 45, "minister", 0, "div"),
        famous("周|Zhou", "佛海|Fohai", 40, "minister", 0, "div"),
        famous("戴|Tai", "笠|Li", 40, "minister", 0, "div"),
        famous("王|Wang", "克敏|Kemin", 61, "minister", 0, "none"),
        famous("梁|Liang", "鸿志|Hongzhi", 55, "minister", 0, "none"),
        famous("何|Ho", "应钦|Ying-chin", 47, "general", 1, "general"),
        famous("陈|Chen", "诚|Cheng", 39, "general", 1, "general"),
        famous("薛|Xue", "岳|Yue", 41, "general", 2, "changsha"),
        famous("张|Zhang", "治中|Zhizhong", 46, "general", 1, "shanghai"),
        famous("胡|Hu", "宗南|Zongnan", 41, "general", 1, "xian"),
        famous("卫|Wei", "立煌|Lihuang", 40, "general", 1, "general"),
        famous("顾|Gu", "祝同|Zhutong", 44, "general", 0, "general"),
        famous("汤|Tang", "恩伯|Enbo", 37, "general", 0, "general"),
        famous("张|Zhang", "发奎|Fakui", 41, "general", 1, "general"),
        famous("杜|Du", "聿明|Yuming", 33, "general", 0, "general"),
        famous("孙|Sun", "立人|Liren", 37, "general", 1, "general"),
        famous("冯|Feng", "玉祥|Yuxiang", 55, "general", 1, "general"),
    ],
)

# 地方实力派：民国的成员(of roc，autonomy)，留兵、留财政、随中央出征
WARLORDS = [
    polity("jinsui", "晋系军阀", "Shanxi Clique", "China", "taiyuan", r=["hedong"], cities=["baotou", "hohhot"],
           of="roc", autonomy=True, ruler=person("阎|Yan", "锡山|Xishan", 54),
           famous=[famous("傅|Fu", "作义|Zuoyi", 42, "general", 1, "hohhot")]),
    polity("jicha", "西北军", "Northwest Army", "China", "beijing", r=["hebei"],
           cities=["beijing", "tianjin", "zhuozhou", "zhangjiakou"], of="roc", autonomy=True,
           ruler=person("宋|Song", "哲元|Zheyuan", 52),
           famous=[famous("张|Zhang", "自忠|Zizhong", 46, "general", 2, "tianjin"),
                   famous("佟|Tong", "麟阁|Linge", 45, "general", 0, "beijing"),
                   famous("赵|Zhao", "登禹|Dengyu", 39, "general", 0, "general"),
                   famous("冯|Feng", "治安|Zhi'an", 41, "general", 0, "general")]),
    polity("shandong", "鲁系军阀", "Shandong Clique", "China", "jinan", r=["qilu"], of="roc", autonomy=True,
           ruler=person("韩|Han", "复榘|Fuju", 47)),
    polity("sichuan", "川系军阀", "Sichuan Clique", "China", "chengdu", r=["bashu"], of="roc", autonomy=True,
           ruler=person("刘|Liu", "湘|Xiang", 47),
           famous=[famous("刘|Liu", "文辉|Wenhui", 42, "general", 0, "general"),
                   famous("王|Wang", "缵绪|Zuanxu", 52, "general", 0, "general"),
                   famous("杨|Yang", "森|Sen", 53, "general", 0, "general")]),
    polity("yunnan", "滇系军阀", "Yunnan Clique", "China", "kunming", cities=["kunming", "dali"], of="roc", autonomy=True,
           ruler=person("龙|Long", "云|Yun", 53),
           famous=[famous("卢|Lu", "汉|Han", 42, "general", 0, "general")]),
    polity("guangxi", "桂系军阀", "Guangxi Clique", "China", "guilin", r=["guangxi"], of="roc", autonomy=True,
           ruler=person("李|Li", "宗仁|Zongren", 46),
           famous=[famous("白|Bai", "崇禧|Chongxi", 44, "general", 2, "general"),
                   famous("黄|Huang", "绍竑|Shaohong", 42, "minister", 0, "nanning")]),
    polity("ningxia", "宁马系军阀", "Ningxia Ma Clique", "China", "yinchuan", cities=["yinchuan"], of="roc", autonomy=True,
           ruler=person("马|Ma", "鸿逵|Hongkui", 45)),
    polity("qinghai", "青马系军阀", "Qinghai Ma Clique", "China", "xining", r=["qinghai", "hexi"], of="roc", autonomy=True,
           ruler=person("马|Ma", "步芳|Bufang", 34),
           famous=[famous("马|Ma", "步青|Buqing", 36, "general", 0, "wuwei")]),
    polity("xinjiang", "新疆军阀", "Xinjiang Warlord", "China", "urumqi", r=["xiyu", "dzungaria"], of="roc", autonomy=True,
           ruler=person("盛|Sheng", "世才|Shicai", 40)),
]

CCP = polity(
    "ccp", "陕甘宁边区", "Shaan-Gan-Ning Border Region", "China", "yanan", r=["guanzhong"], cities=["yanan"], title=T("主席", "Chairman"), emp=True,
    modern=M("republic", "Communism", ("中国共产党", "Communist Party of China"), one=True),
    ruler=person("毛|Mao", "泽东|Zedong", 43),
    famous=[
        famous("周|Zhou", "恩来|Enlai", 39, "minister", 2, "chief"),
        famous("刘|Liu", "少奇|Shaoqi", 39, "minister", 1, "core"),
        famous("张|Zhang", "闻天|Wentian", 37, "minister", 0, "core"),
        famous("朱|Zhu", "德|De", 50, "general", 2, "general"),
        famous("彭|Peng", "德怀|Dehuai", 39, "general", 1, "general"),
        famous("林|Lin", "彪|Biao", 29, "general", 1, "general"),
        famous("刘|Liu", "伯承|Bocheng", 44, "general", 1, "general"),
        famous("贺|He", "龙|Long", 41, "general", 1, "general"),
        famous("聂|Nie", "荣臻|Rongzhen", 37, "general", 0, "general"),
        famous("左|Zuo", "权|Quan", 31, "general", 0, "general"),
        famous("邓|Deng", "小平|Xiaoping", 32, "minister", 0, "div"),
        famous("叶|Ye", "挺|Ting", 41, "general", 1, "general"),
        famous("陈|Chen", "毅|Yi", 35, "general", 1, "general"),
        famous("金|Kim", "日成|Il-sung", 25, "general", 0, "none"),
    ],
)

TIBET = polity("tibet", "西藏噶厦政府", "Kashag Government of Tibet", "Xizang", "lhasa", r=["tibet"],
               ruler=person("", "热振活佛|Reting Rinpoche", 25))

# ---------------------------------------------------------------- 日本与傀儡

JAPAN = polity(
    "japan", "大日本帝国", "Empire of Japan", "Japan", "edo", abbr=T("日", "Japanese"),
    # 朝鲜：1910 年日韩合并，是日本的领土(朝鲜总督府治理)，不是另立的殖民地国
    r=["japan_kyushu", "japan_kinai", "japan_kanto", "japan_tohoku", "ryukyu", "korea_n", "korea_s"], emp=True, grand=True,
    old_capital="kyoto", era_name="昭和|Showa", era_year=12,
    modern=M("constitutional", "Authoritarianism", ("立宪政友会", "Rikken Seiyukai"),
             parties=[("ConservativeLiberalism", "立宪民政党", "Rikken Minseito", ""),
                      ("SocialDemocracy", "社会大众党", "Shakai Taishuto", "")]),
    ruler=person("", "裕仁|Hirohito", 36),
    kin=[person("", "良子|Nagako", 34, rel="wife"),
         person("", "明仁|Akihito", 3, rel="son", heir=True, mo=0),
         person("", "雍仁|Yasuhito", 35, rel="brother"),
         person("", "宣仁|Nobuhito", 32, rel="brother"),
         person("", "崇仁|Takahito", 21, rel="brother")],
    famous=[
        famous("近卫|Konoe", "文麿|Fumimaro", 45, "minister", 1, "pm"),
        famous("广田|Hirota", "弘毅|Koki", 59, "minister", 0, "core"),
        famous("米内|Yonai", "光政|Mitsumasa", 57, "minister", 1, "core"),
        famous("杉山|Sugiyama", "元|Hajime", 57, "general", 1, "general"),
        famous("山本|Yamamoto", "五十六|Isoroku", 53, "general", 2, "general"),
        famous("东条|Tojo", "英机|Hideki", 52, "general", 1, "general"),
        famous("石原|Ishiwara", "莞尔|Kanji", 48, "general", 1, "general"),
        famous("冈村|Okamura", "宁次|Yasuji", 53, "general", 1, "general"),
        famous("松井|Matsui", "石根|Iwane", 59, "general", 0, "general"),
        famous("畑|Hata", "俊六|Shunroku", 57, "general", 1, "general"),
        famous("板垣|Itagaki", "征四郎|Seishiro", 52, "general", 1, "general"),
        famous("寺内|Terauchi", "寿一|Hisaichi", 58, "general", 0, "general"),
        famous("山下|Yamashita", "奉文|Tomoyuki", 51, "general", 1, "general"),
        famous("土肥原|Doihara", "贤二|Kenji", 54, "general", 0, "general"),
    ],
)
TAIWAN_COLONY = polity("taiwan_gg", "日属台湾", "Japanese Taiwan", "Japan", "keelung", r=["taiwan"],
                       colony_of="japan", place="台湾|Taiwan", ruler=person("小林|Kobayashi", "跻造|Seizo", 60))
# 关东州：日本向民国租借的旅大(1898 俄租、1905 转日，1915 延到 1997)
KWANTUNG = polity("kwantung", "日属关东州", "Kwantung Leased Territory", "Japan", "dalian", cities=["dalian"],
                  colony_of="japan", place="关东州|Kwantung", dependency="lease", lessor="roc", lease_years=60)
MANCHUKUO = polity(
    "manchukuo", "大满洲帝国", "Empire of Manchukuo", "Huaxia", "fuyu", r=["manchuria", "liaodong"], cities=["chengde", "chaghan", "hetuala", "ninguta", "harbin", "qiqihar", "jilin", "heihe", "hailar"], emp=True,
    era_name="康德|Kangde", era_year=4, modern=M("monarchy"),
    ruler=person("爱新觉罗|Aisin Gioro", "溥仪|Puyi", 31),
    kin=[person("郭布罗|Gobulo", "婉容|Wanrong", 31, rel="wife"),
         person(None, "溥杰|Pujie", 30, rel="brother", heir=True)],
    famous=[famous("张|Zhang", "景惠|Jinghui", 66, "minister", 0, "chief"),
            famous("臧|Zang", "式毅|Shiyi", 52, "minister", 0, "shenyang")],
)
MENGJIANG = polity("mengjiang", "蒙古军政府", "Mongol Military Government", "Youmu", "dehua", cities=["dehua"],
                   modern=M("monarchy"),
                   ruler=person("孛儿只斤|Borjigin", "德穆楚克栋鲁普|Demchugdongrub", 35),
                   famous=[famous("李|Li", "守信|Shouxin", 45, "general", 0, "general")])
JIDONG = polity("jidong", "冀东防共自治政府", "East Hebei Autonomous Council", "China", "shanhaiguan", cities=["shanhaiguan"],
                ruler=person("殷|Yin", "汝耕|Rugeng", 48))
KOREA_EXILE = polity(
    "korea", "大韩民国临时政府", "Provisional Government of Korea", "Huaxia", "hanseong", r=["korea_n", "korea_s"],
    exile="roc", modern=M("republic", "Centrism", ("韩国独立党", "Korea Independence Party")),
    ruler=person("金|Kim", "九|Ku", 61),
    famous=[famous("金|Kim", "奎植|Kyu-sik", 56, "minister", 1, "core"),
            famous("赵|Jo", "素昂|So-ang", 50, "minister", 0, "core"),
            famous("李|Rhee", "承晚|Syngman", 62, "minister", 1, "none"),
            famous("池|Ji", "青天|Cheong-cheon", 49, "general", 1, "general"),
            famous("李|Lee", "范奭|Beom-seok", 37, "general", 0, "general"),
            famous("金|Kim", "元凤|Won-bong", 39, "general", 0, "general")],
)

# ---------------------------------------------------------------- 苏联、蒙古与亚洲诸国

USSR = polity(
    "ussr", "苏维埃社会主义共和国联盟", "Union of Soviet Socialist Republics", "Slavonic", "moscow", abbr=T("苏", "Soviet"),
    # manchuria：伯力、海参崴、雅克萨这一片(外东北)，与满洲国按最近的城分补空小国
    r=["rus_n", "rus_c", "pontic", "crimea", "volga", "siberia", "kazakh", "sogdiana", "ferghana", "khwarazm", "caucasus", "manchuria"],
    cities=["kiev", "chernigov", "artaxata", "termez", "merv", "yaksa", "vladivostok", "khabarovsk", "st_petersburg"],
    title=T("总书记", "General Secretary"), emp=True, modern=M("republic", "Communism", ("联共(布)", "All-Union Communist Party (Bolsheviks)"), one=True),
    ruler=person("", "斯大林|Joseph Stalin", 58),
    famous=[famous("", "莫洛托夫|Vyacheslav Molotov", 47, "minister", 1, "chief"),
            famous("", "加里宁|Mikhail Kalinin", 61, "minister", 0, "core"),
            famous("", "叶若夫|Nikolai Yezhov", 42, "minister", 0, "div"),
            famous("", "贝利亚|Lavrentiy Beria", 38, "minister", 0, "tbilisi"),
            famous("", "伏罗希洛夫|Kliment Voroshilov", 56, "general", 0, "general"),
            famous("", "布琼尼|Semyon Budyonny", 54, "general", 0, "general"),
            famous("", "铁木辛哥|Semyon Timoshenko", 42, "general", 1, "general"),
            famous("", "朱可夫|Georgy Zhukov", 40, "general", 2, "general"),
            famous("", "布柳赫尔|Vasily Blyukher", 47, "general", 1, "vladivostok"),
            famous("", "崔可夫|Vasily Chuikov", 37, "general", 0, "general")],
)
MPR = polity("mpr", "蒙古人民共和国", "Mongolian People's Republic", "Youmu", "tuul", r=["mobei"], title=T("元帅", "Marshal"), emp=True,
             modern=M("republic", "Communism", ("蒙古人民革命党", "Mongolian People's Revolutionary Party"), one=True),
             ruler=person("", "乔巴山|Khorloogiin Choibalsan", 42))
SIAM = polity("siam", "暹罗王国", "Kingdom of Siam", "India", "nakhon_pathom", abbr=T("暹", "Siamese"),
              cities=["nakhon_pathom", "lavo", "haripunjaya", "ayutthaya", "chiangmai", "patani", "tambralinga"],
              modern=M("monarchy"),
              ruler=person("", "阿南塔·玛希敦|Ananda Mahidol", 12),
              famous=[famous("", "銮披汶|Plaek Phibunsongkhram", 40, "general", 1, "general"),
                      famous("", "比里·帕侬荣|Pridi Banomyong", 37, "minister", 1, "core")])
PHILIPPINES = polity("philippines", "菲律宾自治邦", "Commonwealth of the Philippines", "Frankish", "manila", r=["luzon"], title=T("总统", "President"), emp=True,
                     modern=M("republic", "Centrism", ("国民党", "Nacionalista Party")),
                     ruler=person("", "曼努埃尔·奎松|Manuel Quezon", 59),
                     famous=[famous("", "麦克阿瑟|Douglas MacArthur", 57, "general", 2, "general")])
AFGHANISTAN = polity("afghanistan", "阿富汗王国", "Kingdom of Afghanistan", "Persepolis", "kabul",
                     r=["gandhara"], cities=["kabul", "kandahar", "ghazni", "herat", "balkh"], modern=M("monarchy"),
                     ruler=person("", "查希尔·沙|Mohammed Zahir Shah", 23))
IRAN = polity("iran", "伊朗帝国", "Imperial State of Iran", "Persepolis", "rayy", r=["persis", "media"], cities=["nishapur", "susa"], title=T("沙阿", "Shah"), emp=True,
              modern=M("monarchy"),
              ruler=person("", "礼萨·巴列维|Reza Shah Pahlavi", 59),
              kin=[person("", "穆罕默德·礼萨·巴列维|Mohammad Reza Pahlavi", 17, rel="son", heir=True)])

# ---------------------------------------------------------------- 殖民地(自成一国，colony_of 宗主国)

COLONIES = [
    polity("british_india", "英属印度", "British India", "India", "delhi",
           cities=["peshawar"], r=["gandhara", "punjab", "sindh", "hindustan", "magadha", "bengal", "gujarat", "malwa", "deccan", "kalinga", "tamil"],
           colony_of="uk", place="印度|India", ruler=person("", "林利思戈|Lord Linlithgow", 50)),
    polity("burma", "英属缅甸", "British Burma", "India", "rangoon", r=["burma"], colony_of="uk", place="缅甸|Burma"),
    polity("ceylon", "英属锡兰", "British Ceylon", "India", "colombo", r=["lanka"], colony_of="uk", place="锡兰|Ceylon"),
    polity("malaya", "英属马来亚", "British Malaya", "India", "singapore", cities=["singapore", "malacca", "kedah"], colony_of="uk", place="马来亚|Malaya"),
    polity("hongkong", "英属香港", "British Hong Kong", "China", "hongkong", cities=["hongkong"], colony_of="uk", place="香港|Hong Kong"),
    polity("aden", "英属亚丁", "British Aden", "Arab", "aden", cities=["aden"], colony_of="uk", place="亚丁|Aden"),
    polity("palestine", "英属巴勒斯坦", "Mandatory Palestine", "Kosher", "jerusalem", cities=["jerusalem", "acre"], colony_of="uk", place="巴勒斯坦|Palestine", dependency="mandate"),
    polity("cyprus", "英属塞浦路斯", "British Cyprus", "Roma", "cyprus", cities=["cyprus"], colony_of="uk", place="塞浦路斯|Cyprus"),
    polity("sudan", "英属苏丹", "Anglo-Egyptian Sudan", "Egypt", "khartoum", r=["nubia"], colony_of="uk", place="苏丹|Sudan"),
    polity("british_east_africa", "英属东非", "British East Africa", "Swahili", "mombasa",
           cities=["mombasa", "malindi", "kilwa", "zanzibar", "pate", "mubende", "buddo"], colony_of="uk", place="东非|East Africa"),
    polity("nigeria", "英属尼日利亚", "British Nigeria", "Bantu", "lagos", r=["hausa", "kanem", "benin"], cities=["lagos", "ile_ife"], colony_of="uk", place="尼日利亚|Nigeria"),
    polity("rhodesia", "英属南罗得西亚", "Southern Rhodesia", "Bantu", "mutapa", r=["zimbabwe"], colony_of="uk", place="南罗得西亚|Southern Rhodesia"),
    polity("indochina", "法属印度支那", "French Indochina", "India", "hanoi", r=["jiaozhi", "champa", "khmer"], cities=["vientiane"],
           colony_of="france", place="印度支那|Indochina", ruler=person("", "布雷维|Jules Brévié", 57)),
    polity("syria", "法属叙利亚", "Mandatory Syria", "Arab", "damascus", cities=["damascus", "aleppo", "antioch", "tyre", "tripoli_lb"],
           colony_of="france", place="叙利亚|Syria", dependency="mandate"),
    polity("algeria", "法属阿尔及利亚", "French Algeria", "Arab", "algiers", r=["numidia"], cities=["tlemcen"], colony_of="france", place="阿尔及利亚|Algeria"),
    polity("tunisia", "法属突尼斯", "French Tunisia", "Arab", "carthage", cities=["carthage", "kairouan"], colony_of="france", place="突尼斯|Tunisia", dependency="protectorate"),
    polity("morocco", "法属摩洛哥", "French Morocco", "Arab", "fez", cities=["fez", "marrakesh"], colony_of="france", place="摩洛哥|Morocco", dependency="protectorate"),
    polity("french_west_africa", "法属西非", "French West Africa", "Swahili", "timbuktu", r=["sahel"], cities=["allada"], colony_of="france", place="西非|West Africa"),
    polity("french_equatorial_africa", "法属赤道非洲", "French Equatorial Africa", "Bantu", "loango", cities=["loango"], colony_of="france", place="赤道非洲|Equatorial Africa"),
    polity("dutch_east_indies", "荷属东印度", "Dutch East Indies", "India", "batavia", r=["java", "sumatra"], colony_of="netherlands", place="东印度|East Indies",
           ruler=person("", "斯塔肯伯格|Tjarda van Starkenborgh", 49)),
    polity("belgian_congo", "比属刚果", "Belgian Congo", "Bantu", "leopoldville", r=["luba", "lunda"],
           cities=["leopoldville", "gasabo"], colony_of="belgium", place="刚果|Congo"),
    polity("angola", "葡属安哥拉", "Portuguese Angola", "Bantu", "luanda", r=["ndongo"], cities=["mbanza_kongo", "soyo"], colony_of="portugal", place="安哥拉|Angola"),
    polity("mozambique", "葡属莫桑比克", "Portuguese Mozambique", "Bantu", "sofala", cities=["sofala"], colony_of="portugal", place="莫桑比克|Mozambique"),
    polity("macau", "葡属澳门", "Portuguese Macau", "China", "macau", cities=["macau"], colony_of="portugal", place="澳门|Macau"),
    polity("goa", "葡属印度", "Portuguese India", "India", "goa", cities=["goa"], colony_of="portugal", place="印度|India"),
    polity("libya", "意属利比亚", "Italian Libya", "Arab", "tripoli", r=["cyrenaica"], cities=["tripoli"], colony_of="italy", place="利比亚|Libya"),
    polity("italian_east_africa", "意属东非", "Italian East Africa", "Egypt", "addis_ababa", r=["aksum"], cities=["mogadishu"],
           colony_of="italy", place="东非|East Africa", ruler=person("", "阿梅迪奥·迪萨伏依|Prince Amedeo, Duke of Aosta", 39)),
]
# 埃塞俄比亚皇帝 1936 年流亡英国
ETHIOPIA_EXILE = polity("ethiopia", "埃塞俄比亚帝国", "Ethiopian Empire", "Egypt", "addis_ababa", r=["aksum"], exile="uk",
                        modern=M("monarchy"),
                        ruler=person("", "海尔·塞拉西|Haile Selassie", 45),
                        kin=[person("", "门嫩|Menen Asfaw", 46, rel="wife"),
                             person("", "阿斯法·沃森|Asfaw Wossen", 21, rel="son", heir=True, mo=0)])

# ---------------------------------------------------------------- 西亚、非洲独立国

MIDEAST = [
    polity("turkey", "土耳其共和国", "Republic of Turkey", "Arab", "ancyra", r=["asia_minor", "cappadocia"], cities=["byzantium", "adrianople", "erzurum",
                                                                                                "edessa", "nisibis"],
           title=T("总统", "President"), emp=True, modern=M("republic", "Authoritarianism", ("共和人民党", "Republican People's Party"), one=True),
           ruler=person("", "穆斯塔法·凯末尔|Mustafa Kemal Atatürk", 56),
           famous=[famous("", "伊斯梅特·伊诺努|İsmet İnönü", 53, "minister", 1, "chief")]),
    polity("iraq", "伊拉克王国", "Kingdom of Iraq", "Arab", "ctesiphon", cities=["ctesiphon", "basra", "mosul", "kufa"], modern=M("monarchy"),
           ruler=person("", "加齐|Ghazi", 25)),
    polity("saudi", "沙特阿拉伯王国", "Kingdom of Saudi Arabia", "Arab", "riyadh", cities=["riyadh", "mecca", "medina"], modern=M("monarchy"),
           ruler=person("", "阿卜杜勒-阿齐兹·伊本·沙特|Ibn Saud", 61)),
    polity("yemen", "也门王国", "Kingdom of Yemen", "Arab", "sanaa", cities=["sanaa"], modern=M("monarchy"),
           ruler=person("", "叶海亚|Imam Yahya", 68)),
    polity("oman", "马斯喀特和阿曼苏丹国", "Sultanate of Muscat and Oman", "Arab", "muscat", cities=["muscat", "sohar"], modern=M("monarchy"),
           ruler=person("", "赛义德·本·泰穆尔|Said bin Taimur", 27)),
    polity("egypt", "埃及王国", "Kingdom of Egypt", "Egypt", "cairo", r=["egypt"], modern=M("monarchy"),
           ruler=person("", "法鲁克|Farouk", 17)),
    polity("south_africa", "南非联邦", "Union of South Africa", "Frankish", "johannesburg", r=["nguni"],
           colony_of="uk", place="南非|South Africa", dependency="dominion",
           ruler=person("", "赫尔佐格|J. B. M. Hertzog", 71)),
]

# ---------------------------------------------------------------- 欧洲

EUROPE = [
    polity("uk", "大不列颠帝国", "British Empire", "Frankish", "london", abbr=T("英", "British"), r=["britain_s", "britain_n"], title=T("国王", "King"), emp=True,
           modern=M("constitutional", "Conservatism", ("保守党", "Conservative Party"),
                    parties=[("SocialDemocracy", "工党", "Labour Party", "克莱门特·艾德礼"),
                             ("SocialLiberalism", "自由党", "Liberal Party", "")]),
           ruler=person("", "乔治六世|George VI", 41),
           kin=[person("", "伊丽莎白·鲍斯-莱昂|Elizabeth Bowes-Lyon", 36, rel="wife"),
                person("", "伊丽莎白|Princess Elizabeth", 11, rel="daughter", heir=True, mo=0)],
           famous=[famous("", "内维尔·张伯伦|Neville Chamberlain", 68, "minister", 1, "pm"),
                   famous("", "温斯顿·丘吉尔|Winston Churchill", 62, "minister", 2, "core"),
                   famous("", "克莱门特·艾德礼|Clement Attlee", 54, "minister", 1, "div"),
                   famous("", "伯纳德·蒙哥马利|Bernard Montgomery", 49, "general", 1, "general")]),
    polity("ireland", "爱尔兰自由邦", "Irish Free State", "Frankish", "dublin", r=["ireland"], title=T("总理", "Taoiseach"), emp=True,
           modern=M("republic", "Centrism", ("共和党", "Fianna Fáil")),
           ruler=person("", "埃蒙·德瓦莱拉|Éamon de Valera", 54)),
    polity("france", "法兰西共和国", "French Republic", "Frankish", "paris", abbr=T("法", "French"), r=["gaul_n", "gaul_s"], cities=["strasbourg"], title=T("总统", "President"), emp=True,
           modern=M("republic", "SocialLiberalism", ("激进社会党", "Radical Party"),
                    parties=[("SocialDemocracy", "工人国际法国支部", "SFIO", "莱昂·布鲁姆"),
                             ("Communism", "法国共产党", "French Communist Party", ""),
                             ("ConservativeLiberalism", "民主联盟", "Democratic Alliance", "")]),
           ruler=person("", "阿尔贝·勒布伦|Albert Lebrun", 66),
           famous=[famous("", "卡米耶·肖当|Camille Chautemps", 52, "minister", 0, "pm"),
                   famous("", "莱昂·布鲁姆|Léon Blum", 65, "minister", 1, "core"),
                   famous("", "爱德华·达拉第|Édouard Daladier", 53, "minister", 0, "div"),
                   famous("", "莫里斯·甘末林|Maurice Gamelin", 64, "general", 0, "general"),
                   famous("", "夏尔·戴高乐|Charles de Gaulle", 46, "general", 1, "general"),
                   famous("", "菲利普·贝当|Philippe Pétain", 81, "general", 1, "general")]),
    polity("netherlands", "荷兰王国", "Kingdom of the Netherlands", "Germanic", "amsterdam", abbr=T("荷", "Dutch"), cities=["amsterdam"], modern=M("monarchy"),
           ruler=person("", "威廉明娜|Wilhelmina", 56, sex="f")),
    polity("belgium", "比利时王国", "Kingdom of Belgium", "Frankish", "brussels", abbr=T("比", "Belgian"), cities=["brussels"], modern=M("monarchy"),
           ruler=person("", "利奥波德三世|Leopold III", 35)),
    polity("germany", "德意志国", "German Reich", "Germanic", "berlin", abbr=T("德", "German"), r=["germania"],
           cities=["cologne", "trier", "mainz", "aachen", "regensburg", "augsburg", "munich", "wroclaw"], title=T("元首", "Führer"), emp=True,
           modern=M("republic", "Fascism", ("国家社会主义德国工人党", "NSDAP"), one=True),
           ruler=person("", "阿道夫·希特勒|Adolf Hitler", 48),
           famous=[famous("", "赫尔曼·戈林|Hermann Göring", 44, "minister", 1, "chief"),
                   famous("", "约瑟夫·戈培尔|Joseph Goebbels", 39, "minister", 1, "core"),
                   famous("", "海因里希·希姆莱|Heinrich Himmler", 36, "minister", 0, "div"),
                   famous("", "约阿希姆·冯·里宾特洛甫|Joachim von Ribbentrop", 44, "minister", 0, "div"),
                   famous("", "海因茨·古德里安|Heinz Guderian", 48, "general", 2, "general"),
                   famous("", "埃里希·冯·曼施坦因|Erich von Manstein", 49, "general", 2, "general"),
                   famous("", "埃尔温·隆美尔|Erwin Rommel", 45, "general", 1, "general"),
                   famous("", "格尔德·冯·伦德施泰特|Gerd von Rundstedt", 61, "general", 1, "general")]),
    polity("austria", "奥地利联邦国", "Federal State of Austria", "Germanic", "vindobona", cities=["vindobona", "salzburg"], title=T("总理", "Chancellor"), emp=True,
           modern=M("republic", "Authoritarianism", ("祖国阵线", "Fatherland Front"), one=True),
           ruler=person("", "库尔特·许士尼格|Kurt Schuschnigg", 39)),
    polity("czechoslovakia", "捷克斯洛伐克共和国", "Czechoslovak Republic", "Slavonic", "prague", r=["bohemia"], cities=["pozsony"], title=T("总统", "President"), emp=True,
           modern=M("republic", "SocialLiberalism", ("民族社会党", "National Social Party")),
           ruler=person("", "爱德华·贝奈斯|Edvard Beneš", 53),
           famous=[famous("", "约瑟夫·蒂索|Jozef Tiso", 49, "minister", 0, "none")]),
    polity("poland", "波兰共和国", "Republic of Poland", "Slavonic", "warsaw", cities=["warsaw", "krakow", "gniezno", "poznan", "kalisz", "vilnius",
                                                                      "lviv", "galich"], title=T("总统", "President"), emp=True,
           modern=M("republic", "Authoritarianism", ("民族团结阵营", "Camp of National Unity")),
           ruler=person("", "伊格纳齐·莫希齐茨基|Ignacy Mościcki", 69),
           famous=[famous("", "爱德华·雷兹-希米格维|Edward Rydz-Śmigły", 51, "general", 1, "general"),
                   famous("", "约瑟夫·贝克|Józef Beck", 43, "minister", 0, "core")]),
    polity("danzig", "但泽自由市", "Free City of Danzig", "Germanic", "danzig", cities=["danzig"], modern=M("republic")),
    polity("hungary", "匈牙利王国", "Kingdom of Hungary", "Germanic", "aquincum", cities=["aquincum", "esztergom"], modern=M("monarchy"),
           ruler=person("", "霍尔蒂·米克洛什|Miklós Horthy", 69)),
    polity("romania", "罗马尼亚王国", "Kingdom of Romania", "Roma", "bucharest", r=["dacia"], cities=["durostorum"], modern=M("monarchy"),
           ruler=person("", "卡罗尔二世|Carol II", 43)),
    polity("bulgaria", "保加利亚王国", "Kingdom of Bulgaria", "Slavonic", "serdica", cities=["serdica", "tarnovo", "philippopolis"], modern=M("monarchy"),
           ruler=person("", "鲍里斯三世|Boris III", 43)),
    polity("yugoslavia", "南斯拉夫王国", "Kingdom of Yugoslavia", "Slavonic", "singidunum", cities=["singidunum", "salona"], modern=M("monarchy"),
           ruler=person("", "彼得二世|Peter II", 13),
           famous=[famous("", "保罗亲王|Prince Paul", 44, "minister", 1, "chief")]),
    polity("albania", "阿尔巴尼亚王国", "Kingdom of Albania", "Roma", "dyrrhachium", cities=["dyrrhachium"], modern=M("monarchy"),
           ruler=person("", "索古一世|Zog I", 41)),
    polity("greece", "希腊王国", "Kingdom of Greece", "Roma", "athens", r=["greece", "macedonia"], modern=M("monarchy"),
           ruler=person("", "乔治二世|George II", 47),
           famous=[famous("", "扬尼斯·梅塔克萨斯|Ioannis Metaxas", 66, "minister", 1, "chief")]),
    polity("italy", "意大利王国", "Kingdom of Italy", "Roma", "rome", abbr=T("意", "Italian"), r=["italy_n", "italy_c", "italy_s", "sicily", "sardinia"], cities=["rhodes"],
           title=T("国王", "King"), emp=True, modern=M("constitutional", "Fascism", ("国家法西斯党", "National Fascist Party"), one=True),
           ruler=person("", "维托里奥·埃马努埃莱三世|Victor Emmanuel III", 67),
           kin=[person("", "翁贝托|Umberto", 32, rel="son", heir=True)],
           famous=[famous("", "贝尼托·墨索里尼|Benito Mussolini", 53, "minister", 2, "pm"),
                   famous("", "加莱亚佐·齐亚诺|Galeazzo Ciano", 34, "minister", 0, "core"),
                   famous("", "伊塔洛·巴尔博|Italo Balbo", 41, "general", 1, "tripoli"),
                   famous("", "彼得罗·巴多格里奥|Pietro Badoglio", 65, "general", 1, "general"),
                   famous("", "鲁道夫·格拉齐亚尼|Rodolfo Graziani", 54, "general", 0, "general")]),
    polity("spain_republic", "西班牙共和国", "Spanish Republic", "Frankish", "valencia", abbr=T("西", "Spanish"),
           cities=["valencia", "madrid", "barcelona", "tarragona", "cartagena"], title=T("总统", "President"), emp=True,
           modern=M("republic", "SocialDemocracy", ("人民阵线", "Popular Front"),
                    parties=[("Communism", "西班牙共产党", "Communist Party of Spain", "")]),
           ruler=person("", "曼努埃尔·阿萨尼亚|Manuel Azaña", 57),
           famous=[famous("", "胡安·内格林|Juan Negrín", 45, "minister", 1, "pm")]),
    polity("spain_nationalist", "西班牙国", "Spanish State", "Frankish", "burgos", abbr=T("西", "Spanish"),
           cities=["burgos", "seville", "cordoba", "cadiz", "toledo", "merida", "saragossa", "leon", "oviedo", "pamplona", "tingis"],
           title=T("大元帅", "Generalissimo"), emp=True, modern=M("republic", "Fascism", ("长枪党", "FET y de las JONS"), one=True),
           ruler=person("", "弗朗西斯科·佛朗哥|Francisco Franco", 44),
           famous=[famous("", "埃米利奥·莫拉|Emilio Mola", 49, "general", 1, "general")]),
    polity("portugal", "葡萄牙共和国", "Portuguese Republic", "Frankish", "lisbon", abbr=T("葡", "Portuguese"), cities=["lisbon", "porto", "coimbra"], title=T("总理", "Prime Minister"), emp=True,
           modern=M("republic", "Authoritarianism", ("国民联盟", "National Union"), one=True),
           ruler=person("", "安东尼奥·萨拉查|António de Oliveira Salazar", 48)),
    polity("denmark", "丹麦王国", "Kingdom of Denmark", "Viking", "copenhagen", cities=["copenhagen", "roskilde", "ribe"], modern=M("monarchy"),
           ruler=person("", "克里斯蒂安十世|Christian X", 66)),
    polity("norway", "挪威王国", "Kingdom of Norway", "Viking", "oslo", r=["norway"], modern=M("monarchy"),
           ruler=person("", "哈康七世|Haakon VII", 65)),
    polity("sweden", "瑞典王国", "Kingdom of Sweden", "Viking", "stockholm", cities=["stockholm", "uppsala", "sigtuna", "lund"], modern=M("monarchy"),
           ruler=person("", "古斯塔夫五世|Gustaf V", 79)),
    polity("finland", "芬兰共和国", "Republic of Finland", "Viking", "turku", cities=["turku"], title=T("总统", "President"), emp=True,
           modern=M("republic", "ConservativeLiberalism", ("农民联盟", "Agrarian League")),
           ruler=person("", "基于斯蒂·卡利奥|Kyösti Kallio", 64)),
]

# ---------------------------------------------------------------- 开局外交

RELATIONS = [
    # 七七事变：日本对民国全面开战；傀儡随日本参战(见 PuppetStateSystem)
    relation("japan", "roc", "war", goal="吞并"),
    relation("japan", "ccp", "war", goal="夺城"),
    relation("japan", "manchukuo", "puppet"),
    relation("japan", "mengjiang", "puppet"),
    relation("japan", "jidong", "puppet"),
    # 西安事变后国共停战，统一战线(共御外侮见 NationalSentimentSystem)
    relation("roc", "ccp", "truce"),
    relation("roc", "ccp", "friend", value=10),
    relation("roc", "korea", "friend", value=60),
    relation("japan", "korea", "feud", value=80),
    relation("japan", "roc", "feud", value=80),
    relation("ussr", "roc", "friend", value=30),
    relation("ussr", "mpr", "alliance"),
    relation("ussr", "japan", "feud", value=40),
    relation("mpr", "manchukuo", "feud", value=40),
    relation("japan", "germany", "friend", value=40),
    relation("japan", "italy", "friend", value=30),
    relation("japan", "siam", "friend", value=30),
    relation("japan", "uk", "feud", value=20),
    # 欧洲：西班牙内战，德意援国民军、苏联援共和国；英法同盟、法波同盟
    relation("spain_nationalist", "spain_republic", "war", goal="吞并"),
    relation("germany", "spain_nationalist", "friend", value=50),
    relation("italy", "spain_nationalist", "friend", value=50),
    relation("ussr", "spain_republic", "friend", value=40),
    relation("germany", "italy", "pact"),
    relation("uk", "france", "alliance"),
    relation("france", "poland", "friend", value=40),
    relation("france", "czechoslovakia", "friend", value=40),
    relation("uk", "egypt", "alliance"),
    relation("uk", "iraq", "alliance"),
    relation("uk", "ethiopia", "friend", value=40),
    relation("italy", "ethiopia", "feud", value=80),
    relation("germany", "austria", "feud", value=20),
    relation("germany", "czechoslovakia", "feud", value=40),
    relation("germany", "poland", "feud", value=30),
    relation("italy", "albania", "feud", value=30),
]

# ---------------------------------------------------------------- 剧情事件

# 战后献还：日本连同殖民地、傀儡在这些地区手里的城归民国(见 ScenarioEventSystem.Controls)
# 日本投降献还的地区：剧本自带的中国各省王国法理(见 sino_japanese_dejure，港澳另算)
RETURN_TO_CHINA = [t["id"] for t in sino_japanese_dejure.TITLES
                   if t["id"] in {r for c in sino_japanese_dejure.CORES if c["tx"] == "Huaxia" and c["id"] != "dj_korea" for r in c["r"]}
                   and t["id"] not in ("dj_gang", "dj_ao")] + ["dj_tibet", "dj_qinghai", "dj_monan"]
# 日本在南洋打下的城还给原来的殖民地
RETURN_SOUTH = [("malaya", "malaya"), ("burma", "burma"), ("dutch_east_indies", "java"), ("dutch_east_indies", "sumatra"),
                ("philippines", "luzon"), ("indochina", "jiaozhi"), ("indochina", "champa"), ("indochina", "khmer")]


def cede(a, b, region=None, city=None, cities=None, all_=False, person=None):
    item = {"kind": "cede", "a": a, "b": b}
    if region:
        item["region"] = region
    if city:
        item["city"] = city
    if cities:
        item["cities"] = cities
    if all_:
        item["all"] = True
    if person:
        item["person"] = person
    return item


def puppet_step(a, b, name, person, city, cities=None, region=None, culture=None):
    item = {"kind": "puppet", "a": a, "b": b, "name": T(*name), "person": person, "city": city}
    if cities:
        item["cities"] = cities
    if region:
        item["region"] = region
    if culture:
        item["culture"] = culture
    return item


def holds(a, city):
    return {"kind": "holds_city", "a": a, "city": city}


def alive(a):
    return {"kind": "alive", "a": a}


def at_war(a, b):
    return {"kind": "at_war", "a": a, "b": b}


def before(years):
    return {"kind": "before", "value": years}


def move(a, city):
    return {"kind": "move_capital", "a": a, "city": city}


def rename(a, name):
    return {"kind": "rename", "a": a, "name": T(*name)}


NORTH_CHINA = ["beijing", "tianjin", "zhuozhou", "dingzhou", "zhending", "handan", "ye", "jinan", "linzi", "qufu", "jimo",
               "taiyuan", "datong", "kaifeng", "xinzheng", "xuzhou"]
CENTRAL_CHINA = ["nanjing", "shanghai", "suzhou", "hangzhou", "shaoxing", "yangzhou", "wuchang", "nanchang", "shouchun"]

EVENTS = [
    # —— 1937
    event("七七事变", T("卢沟桥事变，全面抗战爆发", "The Marco Polo Bridge Incident: the full-scale war begins"), 0,
          [at_war("japan", "roc"), before(1)], []),
    death("佟麟阁赵登禹殉国", ("南苑之战", "The battle of Nanyuan"), 0, "jicha", "佟麟阁、赵登禹",
          ("南苑之战殉国", "Fell defending Nanyuan")),
    event("迁都重庆", T("上海失陷，国民政府移驻重庆", "Shanghai falls; the Nationalist government moves to Chongqing"), 0,
          [holds("japan", "shanghai"), alive("roc")], [move("roc", "chongqing")]),
    event("南京沦陷", T("日军攻陷南京，屠戮军民", "Japanese troops take Nanjing and massacre its people"), 0,
          [holds("japan", "nanjing"), before(3)], []),
    event("华北临时政府", T("日军在北平扶植临时政府", "The Japanese set up a Provisional Government in Beiping"), 0,
          [holds("japan", "beijing")],
          [puppet_step("japan", "north_china", ("中华民国临时政府", "Provisional Government of the Republic of China"), "王克敏",
                       "beijing", NORTH_CHINA)]),
    event("蒙古联盟自治政府", T("日军占归绥，德王移驻改称蒙古联盟自治政府", "Prince De moves to Guisui as head of the Mongol United Autonomous Government"), 0,
          [holds("japan", "hohhot"), alive("mengjiang")],
          [cede("japan", "mengjiang", city="hohhot", cities=["baotou"]), move("mengjiang", "hohhot"),
           rename("mengjiang", ("蒙古联盟自治政府", "Mongol United Autonomous Government"))]),
    # —— 1938
    death("刘湘病逝", ("刘湘病逝", "Liu Xiang dies"), 1, "sichuan", "刘湘", ("病逝汉口", "Died of illness at Hankou")),
    death("韩复榘伏法", ("韩复榘弃守山东伏法", "Han Fuju is executed for abandoning Shandong"), 1, "shandong", "韩复榘",
          ("弃守山东被处决", "Executed for abandoning Shandong"), when=[holds("japan", "jinan")]),
    event("冀东并入临时政府", T("冀东防共自治政府并入临时政府", "The East Hebei council merges into the Provisional Government"), 1,
          [alive("north_china"), alive("jidong")], [cede("jidong", "north_china", all_=True)]),
    event("维新政府", T("日军在南京扶植维新政府", "The Japanese set up the Reformed Government at Nanjing"), 1,
          [holds("japan", "nanjing")],
          [puppet_step("japan", "weixin", ("中华民国维新政府", "Reformed Government of the Republic of China"), "梁鸿志",
                       "nanjing", CENTRAL_CHINA)]),
    event("武汉失守", T("武汉会战，武汉失守", "Wuhan falls after a long campaign"), 1,
          [holds("japan", "wuchang"), before(4)], []),
    event("汪精卫出走", T("汪精卫出走河内，投敌附逆", "Wang Jingwei flees Chongqing and goes over to the Japanese"), 1,
          [alive("roc"), alive("japan"), at_war("japan", "roc")],
          [cede("roc", "japan", person="汪精卫、陈公博、周佛海")]),
    death("布柳赫尔死于狱中", ("布柳赫尔死于狱中", "Blyukher dies in prison"), 1, "ussr", "布柳赫尔", ("死于狱中", "Died in prison")),
    death("凯末尔病逝", ("凯末尔病逝", "Atatürk dies"), 1, "turkey", "穆斯塔法·凯末尔", ("病逝", "Died of illness")),
    # 欧洲 1938-1939
    event("德奥合并", T("德国吞并奥地利", "Germany annexes Austria"), 1,
          [alive("germany"), alive("austria")], [cede("austria", "germany", all_=True)]),
    event("吞并捷克", T("德国占领捷克，扶植斯洛伐克", "Germany occupies Bohemia and sets up a Slovak state"), 2,
          [alive("germany"), alive("czechoslovakia")],
          [{"kind": "exile", "a": "czechoslovakia", "b": "uk", "c": "germany"},
           cede("czechoslovakia", "germany", all_=True),
           puppet_step("germany", "slovakia", ("斯洛伐克", "Slovakia"), "约瑟夫·蒂索", "nitra", ["pozsony"])]),
    event("意大利入侵阿尔巴尼亚", T("意大利出兵阿尔巴尼亚", "Italy invades Albania"), 2,
          [alive("italy"), alive("albania")], [cede("albania", "italy", all_=True)]),
    death("加齐车祸", ("伊拉克国王加齐死于车祸", "King Ghazi of Iraq dies in a car crash"), 2, "iraq", "加齐", ("死于车祸", "Died in a car crash")),
    event("德苏互不侵犯条约", T("德苏签订互不侵犯条约", "Germany and the Soviet Union sign a non-aggression pact"), 2,
          [alive("germany"), alive("ussr")], [rel_step("germany", "ussr", "pact")]),
    event("德国入侵波兰", T("德国进攻波兰，英法对德宣战", "Germany invades Poland; Britain and France declare war"), 2,
          [alive("germany"), alive("poland")],
          [rel_step("germany", "poland", "war", goal="吞并"), rel_step("uk", "germany", "war", goal="夺城"),
           rel_step("france", "germany", "war", goal="夺城"), rel_step("ussr", "poland", "war", goal="夺城", city="lviv")]),
    event("蒙古联合自治政府", T("德王移驻张家口，改称蒙古联合自治政府", "Prince De moves to Kalgan as head of the Mengjiang United Autonomous Government"), 2,
          [holds("japan", "zhangjiakou"), alive("mengjiang")],
          [cede("japan", "mengjiang", city="zhangjiakou", cities=["datong"]), move("mengjiang", "zhangjiakou"),
           rename("mengjiang", ("蒙疆联合自治政府", "Mengjiang United Autonomous Government"))]),
    # —— 1940
    event("汪伪国民政府", T("汪精卫在南京成立伪国民政府", "Wang Jingwei sets up a collaborationist National Government at Nanjing"), 3,
          [holds("japan", "nanjing")],
          [puppet_step("japan", "wang", ("中华民国国民政府", "Reorganized National Government of China"), "汪精卫、陈公博、周佛海",
                       "nanjing", CENTRAL_CHINA),
           cede("weixin", "wang", all_=True),
           rename("north_china", ("华北政务委员会", "North China Political Council"))]),
    death("张自忠殉国", ("枣宜会战，张自忠殉国", "Zhang Zizhong falls at Zaoyang-Yichang"), 3, "jicha", "张自忠",
          ("枣宜会战殉国", "Fell in the battle of Zaoyang-Yichang")),
    event("德国西线攻势", T("德军进攻荷比法", "Germany attacks the Low Countries and France"), 3,
          [alive("germany"), alive("france")],
          [rel_step("germany", "netherlands", "war", goal="吞并"), rel_step("germany", "belgium", "war", goal="吞并"),
           rel_step("germany", "france", "war", goal="夺城", city="paris"), rel_step("germany", "denmark", "war", goal="吞并"),
           rel_step("germany", "norway", "war", goal="吞并"), rel_step("italy", "france", "war", goal="夺城"),
           rel_step("italy", "uk", "war", goal="夺城")]),
    event("三国同盟", T("日德意缔结三国同盟", "Japan, Germany and Italy sign the Tripartite Pact"), 3,
          [alive("japan"), alive("germany")], [rel_step("japan", "germany", "pact"), rel_step("japan", "italy", "pact")]),
    # —— 1941
    event("皖南事变", T("皖南事变，国共关系破裂", "The New Fourth Army Incident sours the united front"), 4,
          [alive("roc"), alive("ccp")], [rel_step("roc", "ccp", "feud", value=50)]),
    event("日苏中立条约", T("日苏签订中立条约", "Japan and the Soviet Union sign a neutrality pact"), 4,
          [alive("japan"), alive("ussr")], [rel_step("japan", "ussr", "pact")]),
    event("德国入侵苏联", T("德军进攻苏联", "Germany invades the Soviet Union"), 4,
          [alive("germany"), alive("ussr")],
          [rel_step("germany", "ussr", "war", goal="吞并"), rel_step("italy", "ussr", "war", goal="夺城"),
           rel_step("uk", "ussr", "alliance")]),
    event("埃塞俄比亚复国", T("英军收复亚的斯亚贝巴，海尔·塞拉西还都", "British troops retake Addis Ababa and Haile Selassie returns"), 4,
          [alive("ethiopia"), at_war("uk", "italy")], [cede("italy", "ethiopia", region="aksum")]),
    event("太平洋战争", T("日军偷袭珍珠港，南进英美荷属地", "Japan strikes Pearl Harbor and moves south against Allied possessions"), 4,
          [alive("japan"), at_war("japan", "roc")],
          [rel_step("japan", "hongkong", "war", goal="夺城", city="hongkong"), rel_step("japan", "malaya", "war", goal="夺城", city="singapore"),
           rel_step("japan", "philippines", "war", goal="夺城", city="manila"),
           rel_step("japan", "dutch_east_indies", "war", goal="夺城", city="batavia"),
           rel_step("japan", "burma", "war", goal="夺城", city="rangoon"), rel_step("japan", "uk", "war", goal="夺城"),
           rel_step("japan", "netherlands", "war", goal="夺城"), rel_step("japan", "siam", "alliance")]),
    # —— 1942-1944
    death("左权殉国", ("十字岭突围，左权殉国", "Zuo Quan falls breaking out at Shiziling"), 5, "ccp", "左权", ("十字岭突围中殉国", "Fell at Shiziling")),
    death("山本五十六座机被击落", ("山本五十六座机被击落", "Yamamoto's plane is shot down"), 6, "japan", "山本五十六",
          ("座机在布干维尔上空被击落", "Shot down over Bougainville")),
    death("林森病逝", ("国民政府主席林森病逝", "Lin Sen dies"), 6, "roc", "林森", ("病逝", "Died of illness")),
    death("汪精卫病死", ("汪精卫病死名古屋", "Wang Jingwei dies in Nagoya"), 7, "wang", "汪精卫", ("病死名古屋", "Died of illness in Nagoya")),
    # —— 1945
    event("苏联出兵东北", T("苏联对日宣战，出兵东北", "The Soviet Union declares war on Japan and invades Manchuria"), 8,
          [alive("ussr"), alive("japan")],
          [rel_step("ussr", "japan", "war", goal="夺城"), rel_step("ussr", "manchukuo", "war", goal="夺城", city="fuyu"),
           rel_step("mpr", "mengjiang", "war", goal="夺城")]),
    event("日本投降",
          T("美国在广岛、长崎投下原子弹，日本宣布无条件投降", "Atomic bombs fall on Hiroshima and Nagasaki; Japan surrenders unconditionally"), 8,
          [alive("japan"), alive("roc"), at_war("japan", "roc")],
          [rel_step("japan", "roc", "truce"), rel_step("japan", "ccp", "truce"), rel_step("japan", "ussr", "truce"),
           rel_step("japan", "mpr", "truce"), rel_step("japan", "uk", "truce"), rel_step("japan", "netherlands", "truce")]
          + [cede("japan", "roc", region=region) for region in RETURN_TO_CHINA]
          + [cede("japan", "ussr", region="dj_joseon"), cede("japan", "korea", region="dj_jinhan")]
          + [cede("japan", colony, region=region) for colony, region in RETURN_SOUTH]
          + [cede("japan", "hongkong", city="hongkong"), cede("japan", "roc", city="dalian")]),
    event("希特勒自杀", T("苏军攻入柏林，希特勒自杀", "Soviet troops storm Berlin; Hitler takes his own life"), 8,
          [holds("ussr", "berlin")], [{"kind": "dies", "a": "germany", "person": "阿道夫·希特勒", "name": T("自杀", "Suicide")}]),
    # —— 战后
    death("戴笠坠机", ("戴笠坠机身亡", "Dai Li dies in a plane crash"), 9, "roc", "戴笠", ("坠机身亡", "Died in a plane crash")),
    event("国共内战", T("国共和谈破裂，内战全面爆发", "Peace talks collapse and civil war breaks out"), 9,
          [alive("roc"), alive("ccp")], [rel_step("roc", "ccp", "war", goal="吞并")]),
    event("日本国宪法", T("日本施行新宪法，天皇为国民统合之象征", "Japan's new constitution makes the emperor a symbol of the nation"), 10,
          [alive("japan"), {"kind": "holds_city", "a": "roc", "city": "tayouan"}],
          [{"kind": "ideology", "a": "japan", "name": "Conservatism", "party": T("日本自由党", "Liberal Party")}]),
    event("大韩民国", T("大韩民国成立", "The Republic of Korea is proclaimed"), 11,
          [holds("korea", "hanseong")], [rename("korea", ("大韩民国", "Republic of Korea"))]),
    event("朝鲜民主主义人民共和国", T("朝鲜民主主义人民共和国成立", "The Democratic People's Republic of Korea is proclaimed"), 11,
          [holds("ussr", "pyongyang")],
          [puppet_step("ussr", "dprk", ("朝鲜民主主义人民共和国", "Democratic People's Republic of Korea"), "金日成", "pyongyang",
                       region="dj_joseon")]),
    event("中华人民共和国", T("中华人民共和国成立，定都北京", "The People's Republic of China is founded with Beijing as its capital"), 12,
          [holds("ccp", "beijing")],
          [rename("ccp", ("中华人民共和国", "People's Republic of China")), move("ccp", "beijing")]),
    event("国民政府迁台", T("国民政府迁往台北", "The Nationalist government withdraws to Taipei"), 12,
          [holds("ccp", "nanjing"), holds("roc", "keelung")], [move("roc", "keelung")]),
]

# ---------------------------------------------------------------- 无人区归属

WILDERNESS = {
    "tibetan_plateau": "tibet", "nyainqentanglha": "tibet", "taklamakan": "xinjiang", "bogda": "xinjiang", "gurbantunggut": "xinjiang",
    "dzungarian_alatau": "xinjiang", "altai": "xinjiang", "kumtag": "xinjiang",
    "khangai": "mpr", "khentii": "mpr", "gobi_altai": "mpr",
    "hunshandake": "mengjiang", "horqin": "manchukuo", "greater_khingan": "manchukuo", "lesser_khingan": "manchukuo",
    "changbai": "manchukuo", "kaema": "japan", "taebaek": "japan",
    "qinling": "roc", "daba": "roc", "dabie": "roc", "wuyi": "roc", "nanling": "roc", "liupan": "roc",
    "taihang": "jinsui", "luliang": "jinsui", "yanshan": "jicha", "min_shan": "sichuan", "qionglai": "sichuan",
    "ailao": "yunnan", "wuliang": "yunnan", "gaoligong": "yunnan", "helan": "ningxia", "mu_us": "ningxia", "ulan_buh": "ningxia",
    "qilian": "qinghai", "qaidam": "qinghai", "altun": "qinghai",
    # 阿拉善、额济纳(巴丹吉林、腾格里)1929 年起属宁夏省，归马鸿逵；以前挂青海，隔着河西走廊成了飞地
    "badain_jaran": "ningxia", "tengger": "ningxia",
    "hida": "japan", "kiso": "japan", "akaishi": "japan", "ou": "japan",
    "cardamom": "indochina", "bago_yoma": "burma",
    "thar": "british_india", "aravalli": "british_india", "vindhya": "british_india", "satpura": "british_india",
    "pir_panjal": "british_india", "sundarbans": "british_india", "kirthar": "british_india", "sri_lanka_highlands": "ceylon",
    "zagros": "iran", "alborz": "iran", "dasht_e_kavir": "iran", "dasht_e_lut": "iran", "talysh": "iran",
    "pontic": "turkey", "taurus": "turkey", "syrian_desert": "iraq", "sinai": "egypt", "sinai_mountains": "egypt",
    "negev": "palestine", "judaean_desert": "palestine", "eastern_desert": "egypt", "libyan_desert": "libya", "qattara": "egypt",
    "balkan_mountains": "bulgaria", "rhodope": "bulgaria", "pindus": "greece", "aures": "algeria", "tell_atlas": "algeria",
    "sarawat": "saudi", "hajar": "oman", "high_atlas": "morocco", "middle_atlas": "morocco", "butana": "sudan", "bayuda": "sudan",
    "ethiopian_highlands": "italian_east_africa", "simien": "italian_east_africa",
    "cantabrian": "spain_nationalist", "sistema_central": "spain_republic", "sierra_nevada_es": "spain_nationalist",
    "massif_central": "france", "scottish_highlands": "uk", "jotunheimen": "norway",
    "kola": "ussr", "khibiny": "ussr", "ural": "ussr", "timan": "ussr", "west_siberian": "ussr", "yamal": "ussr", "gydan": "ussr",
    "stanovoy": "ussr", "pripet": "poland",
}
# 当地法理默认(由 wilderness_administration_source.py 算出：地区里的城全归同一个认领这片无人区的国)
WILDERNESS_REGIONS = {"luliang": "dj_jin", "taebaek": "dj_jinhan", "simien": "aksum", "scottish_highlands": "britain_n",
                      "massif_central": "gaul_s"}


def claim_regions(polities, data):
    """没有哪国写进 r 的地区：在那儿有城的各国都认领。开局补空的小国落在哪国认领的地区就并进哪国，
    几国都认领的归城离得最近的那国(见 WorldMapScenario.ClaimingPolity)；不认领的话河套、漠南、幽燕、南中这些
    地方补出来的小国开局就各自独立，成了一片"某某系军阀"。流亡政府不认领。"""
    region_of = {c["id"]: c["r"] for c in data["cities"]}
    claimed = {r for item in polities if not item.get("exile") for r in item.get("r", [])}
    for item in polities:
        if item.get("exile"):
            continue
        own = {region_of[cid] for cid in item.get("cities", []) + [item["cap"]] if cid in region_of}
        extra = sorted(r for r in own - claimed if r not in item.get("r", []))
        if extra:
            item["r"] = item.get("r", []) + extra


def main():
    root = sys.argv[1]
    cities = [dict(city) for city in NEW_CITIES]
    new_ids = {city["id"] for city in cities}
    # 先加新城、改旧城的旧称；新城的旧称等它进了城表再写(update_cities 按写之前的城表找城)
    update_cities(root, cities, {cid: alt for cid, alt in ALTS.items() if cid not in new_ids})
    data = update_cities(root, [], {cid: alt for cid, alt in ALTS.items() if cid in new_ids})
    # 改了现今名字的城：zh/en 换新名，旧名进 alt
    renamed = []
    by_id = {city["id"]: city for city in data["cities"]}
    for cid, (zh, en) in RENAMED.items():
        city = by_id[cid]
        if city["zh"] != zh or city["en"] != en:
            renamed.append({"id": cid, "zh": zh, "en": en})
    if renamed:
        data = update_cities(root, renamed, {})
    active = {c["id"] for c in data["cities"] if c.get("from", -99999) <= YEAR <= c.get("to", 99999)}

    polities = ([ROC] + WARLORDS + [CCP, TIBET, JAPAN, TAIWAN_COLONY, KWANTUNG, MANCHUKUO, MENGJIANG, JIDONG, KOREA_EXILE,
                                   USSR, MPR, SIAM, PHILIPPINES, AFGHANISTAN, IRAN] + COLONIES + [ETHIOPIA_EXILE] + MIDEAST + EUROPE)
    ids = set()
    for item in polities:
        # 属地地名补繁体
        if item.get("place") and item["place"].count("|") == 1:
            item["place"] = T(*item["place"].split("|"))
        assert item["id"] not in ids, item["id"]
        ids.add(item["id"])
        missing = [cid for cid in item.get("cities", []) + [item["cap"]] if cid not in active]
        assert not missing or item.get("exile"), (item["id"], missing)
    claim_regions(polities, data)
    era = {
        "id": "sino_japanese", "year": YEAR, "focus": "east", "order": 5, "frame": "eastasia",
        "zh": "日军侵华", "en": "Japanese Invasion of China",
        "dzh": "1937年，民国二十六年、昭和十二年。七月七日卢沟桥枪响，日本全面侵华。国民政府训政未久，晋绥、冀察、山东、四川、"
               "云南、广西、西北马家各据一方；陕甘宁边区新与国民政府停战，共御外侮。日本以满洲国、蒙古军政府、冀东为傀儡，"
               "吞并朝鲜，以台湾为殖民地，大韩民国临时政府寄居中国。从淞沪、南京、武汉一路打到太平洋战争、苏联出兵、日本投降，"
               "再到国共内战。欧洲西班牙内战正酣，德国将吞并奥地利、捷克，进攻波兰与苏联。",
        "den": "AD 1937, the twenty-sixth year of the Republic and the twelfth of Showa. Shots at the Marco Polo Bridge on 7 July open "
               "Japan's all-out war on China. The Nationalists rule under party tutelage while Shanxi, Hebei-Chahar, Shandong, "
               "Sichuan, Yunnan, Guangxi and the Ma clan hold their own; the Communist border region has just made peace with "
               "Nanjing to face the invader together. Japan keeps Manchukuo, Prince De's Mongols and East Hebei as puppets, has annexed Korea, "
               "holds Taiwan as a colony, and the Korean Provisional Government lives in exile in China. From Shanghai, Nanjing and Wuhan "
               "to the Pacific War, the Soviet invasion of Manchuria, Japan's surrender and the Chinese Civil War. In Europe the "
               "Spanish Civil War rages and Germany prepares to swallow Austria and Czechoslovakia and to strike Poland and Russia.",
        "tech": TECH, "techExtra": TECH_EXTRA, "techSkip": TECH_SKIP, "inst": INST, "culture": CULTURE, "ranks": RANKS,
        "relations": RELATIONS, "events": EVENTS, "wilderness": WILDERNESS, "wilderness_regions": WILDERNESS_REGIONS,
        # 王国法理与帝国法理照玩家的帝盒法理地图(见 sino_japanese_dejure)
        "titles": [{"id": t["id"], "zh": t["prov"], "en": t["prov_en"], "tw": T(t["prov"], t["prov_en"]).split("|")[2],
                    "t": T(t["zh"], t["en"]), "cap": t["cap"], "cities": t["cities"], "share": t["share"]}
                   for t in sino_japanese_dejure.TITLES if t["cities"]],
        "cores": [{"id": c["id"], "zh": c["zh"], "en": c["en"], "tw": T(c["zh"], c["en"]).split("|")[2], "cap": c["cap"],
                   "r": c["r"], "tx": c["tx"]} for c in sino_japanese_dejure.CORES],
        "polities": polities,
    }
    write_era(root, era)
    print("sino_japanese", era["year"], len(polities), "国", len(EVENTS), "件事", len(NEW_CITIES), "座新城", len(renamed), "城改名")


if __name__ == "__main__":
    main()
