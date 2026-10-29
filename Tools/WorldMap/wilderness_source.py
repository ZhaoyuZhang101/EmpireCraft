"""世界地图无人区的真实地名与史实归属：写出 WorldMaps/wilderness.json(地理单元表)，并把各时代的史实归属写进
WorldMaps/eras/*.json 的 "wilderness" 一节。游戏里由 Scripts/GeneralSystems/WorldMap/WorldMapWilderness.cs 用
(用户 2026-10-29 "然后要适配预设地图得历史设定""包括真实的地理名称和归属")。

地理单元(UNITS)：
  - k：能对上的无人区类型(M 山脉、D 沙漠、T 冻原、J 雨林、S 大泽；青藏一带的雪丘高原按地块构成常判成山脉或冻原，两样都写)；
  - 定位：box 经纬框 [西, 南, 东, 北](可几个)，pts 锚点 [经度, 纬度] 连同半径 r(度，经向按纬度折算)；
    一块地落在任一框里或离任一锚点 r 度以内就算这个单元盖住了它。撒哈拉、阿拉伯沙漠、西伯利亚、青藏高原这类大单元框得大，
    底下另有细的(利比亚沙漠、叙利亚沙漠、泰梅尔半岛、羌塘……)：一片区域被几个单元盖住时取重合得最多的
    (盖住的块数占"区域 + 单元范围"并起来的比例：一整片撒哈拉配撒哈拉，大框里拆出来的一小片配框得正好的细单元；
    盖住区域过半的单元优先，免得一个小圈只沾了边就把整片抢走)，一样多取范围小的；
  - alt：按年份的旧称，写法同 cities.json 的 alt("到某年为止|简体|英文|繁体"，也可写"某年~|…"自某年起、"某年~某年|…")，
    不论起名文化，史书上那个年代通行的叫法(长白山在唐叫太白山)；
  - c：按文化的叫法，文化 id → 若干条(写法同上，前面不带年份的不限年份)，命中起名文化的先用：
    华夏(瀚海、大漠、流沙、葱岭、雪山、大雪山、金山、燕然山、北海、五岭、莫贺延碛、白龙堆)、
    罗马(荒漠阿拉伯、海摩斯山、帕罗帕米苏斯山、伊茂斯山、埃莫杜斯山、帕里亚德雷斯山、奥拉修斯山、麦罗埃岛)、
    阿拉伯(沙姆荒漠、卡布克山、达兰山、提赫荒原、穆萨山、巴提哈沼泽)、印度(雪山 Himavat、萨希亚德里、摩亨德拉山、
    帕里亚特拉山、凯拉斯山、马鲁斯塔利)、游牧(金山 Altun Yish、于都斤山、不儿罕山)、犹太(巴兰的旷野、何烈山、南地、犹大旷野)、
    斯拉夫(石带、波列西耶)、维京(基阿连山脉、芬马克)、埃及(德什雷特)。拿不准的旧称、文化叫法一律不写。
繁体名照 add_traditional.py 的 tw()(zhconv)转，转错的在 TW_FIX 里改。

史实归属(OWNERS)：时代 id → {单元 id: 政权 id}；开局划区后，这个单元盖住的无人区地块挂到那个政权都城所在的王国法理上
(盖过"挂最近法理")；只写史实上清楚归某政权的，当时确属无主或几方分据、拿不准的不写。

用法：python -I -X utf8 wilderness_source.py <模组目录>   (要 zhconv，同 add_traditional.py)
"""
import json
import os
import sys

KIND = {"M": "mountain", "D": "desert", "T": "tundra", "J": "jungle", "S": "swamp"}
UNITS = []


def U(uid, kinds, zh, en, box=None, pts=None, r=None, alt=None, c=None):
    unit = {"id": uid, "k": [KIND[k] for k in kinds], "zh": zh, "en": en}
    if box:
        unit["box"] = box
    if pts:
        unit["pts"] = pts
        if r is not None:
            unit["r"] = r
    if alt:
        unit["alt"] = alt
    if c:
        unit["c"] = c
    UNITS.append(unit)


# 文化叫法里反复用到的
DESHRET = "德什雷特|Deshret|德什雷特"            # 古埃及人称尼罗河谷外的红土荒漠
ARABIA_DESERTA = "荒漠阿拉伯|Arabia Deserta|荒漠阿拉伯"   # 托勒密《地理学志》
BEIHAI = "-100~|北海|Beihai|北海"                 # 《汉书·苏武传》"徙武北海上无人处"，即贝加尔湖一带

# ---------------- 非洲 ----------------
U("sahara", "D", "撒哈拉沙漠", "Sahara Desert", box=[[-17, 15, 33, 34]])
U("tenere", "D", "泰内雷沙漠", "Ténéré", box=[[8.5, 16, 13.5, 20.5]])
U("tanezrouft", "D", "塔奈兹鲁夫特", "Tanezrouft", box=[[-2, 20, 3, 24.5]])
U("erg_chech", "D", "舍什沙海", "Erg Chech", box=[[-5, 23, -0.5, 26.5]])
U("djouf", "D", "朱夫沙漠", "El Djouf", box=[[-10, 18, -4.5, 22.5]])
U("adrar_plateau", "D", "阿德拉尔高原", "Adrar Plateau", box=[[-14, 19.5, -10, 22]])
U("grand_erg_occidental", "D", "大西部沙海", "Grand Erg Occidental", box=[[-1.5, 29, 2.5, 31.5]])
U("grand_erg_oriental", "D", "大东部沙海", "Grand Erg Oriental", box=[[5.5, 28, 10, 32]])
U("ubari", "D", "乌巴里沙海", "Ubari Sand Sea", box=[[10, 26, 14, 27.6]])
U("murzuq", "D", "迈尔祖格沙海", "Murzuq Sand Sea", box=[[11, 23.8, 15, 26]])
U("rebiana", "D", "雷比亚纳沙海", "Rebiana Sand Sea", box=[[19.5, 23.5, 23, 26]])
U("calanshio", "D", "卡兰舒沙海", "Calanshio Sand Sea", box=[[21, 26.5, 24.3, 30]])
U("great_sand_sea", "D", "大沙海", "Great Sand Sea", box=[[24.5, 23.5, 28.5, 29.5]])
U("qattara", "D", "盖塔拉洼地", "Qattara Depression", box=[[25.5, 28.8, 29.5, 30.4]])
U("gilf_kebir", "DM", "吉勒夫凯比尔高原", "Gilf Kebir", box=[[24.8, 22.6, 26.8, 24.4]])
U("djurab", "D", "朱拉卜沙漠", "Djurab Desert", box=[[16.5, 15.8, 20.5, 18]])
U("tibesti", "MD", "提贝斯提山脉", "Tibesti Mountains", box=[[16, 19.5, 19.5, 22]])
U("ahaggar", "MD", "阿哈加尔高原", "Ahaggar Mountains", box=[[4, 22, 7.5, 24.5]])
U("tassili", "MD", "阿杰尔高原", "Tassili n'Ajjer", box=[[6.5, 24.5, 10.5, 26.8]])
U("air", "MD", "阿伊尔山", "Aïr Mountains", box=[[7.5, 17, 9.8, 20.3]])
U("ennedi", "MD", "恩内迪高原", "Ennedi Plateau", box=[[21, 16, 23.8, 18]])
U("libyan_desert", "D", "利比亚沙漠", "Libyan Desert", box=[[24, 20, 31.5, 31]], c={"Egypt": [DESHRET]})
U("nubian_desert", "D", "努比亚沙漠", "Nubian Desert", box=[[30.5, 18, 37.5, 22.5]])
U("eastern_desert", "D", "东部沙漠", "Eastern Desert", box=[[31.3, 22, 35.5, 30.2]], c={"Egypt": [DESHRET]})
U("bayuda", "D", "拜尤达沙漠", "Bayuda Desert", box=[[31.8, 16.8, 34, 19]])
# 阿特巴拉河、青尼罗河、尼罗河围起的布塔纳：希腊罗马人称"麦罗埃岛"(希罗多德、斯特拉波)
U("butana", "D", "布塔纳", "Butana", box=[[33.3, 13.5, 36.5, 17.4]],
  c={"Roma": ["麦罗埃岛|Island of Meroë|麥羅埃島"]})
U("sinai", "D", "西奈沙漠", "Sinai Desert", box=[[32.5, 29, 34.6, 31.2]],
  c={"Arab": ["提赫荒原|Badiyat al-Tih|提赫荒原"], "Kosher": ["巴兰的旷野|Wilderness of Paran|巴蘭的曠野"]})
U("sinai_mountains", "M", "西奈山", "Mount Sinai", pts=[[33.97, 28.54]], r=0.8,
  c={"Kosher": ["何烈山|Mount Horeb|何烈山"], "Arab": ["穆萨山|Jabal Musa|穆薩山"]})
U("negev", "D", "内盖夫沙漠", "Negev Desert", box=[[34.3, 29.6, 35.4, 31.3]], c={"Kosher": ["南地|Negev|南地"]})
U("judaean_desert", "D", "犹地亚旷野", "Judaean Desert", box=[[35.15, 31.2, 35.55, 31.9]],
  c={"Kosher": ["犹大旷野|Wilderness of Judah|猶大曠野"]})
U("atlas", "M", "阿特拉斯山脉", "Atlas Mountains",
  pts=[[-9, 30.7], [-7.5, 31.2], [-5, 32.5], [-2.5, 33.2], [0.5, 34.2], [3, 35.2], [6, 35.3], [8.5, 35.6]], r=1.3)
# 伊德里西、比克里等称高阿特拉斯为达兰山(Jabal Daran，柏柏尔语 Adrar n Dern)
U("high_atlas", "M", "高阿特拉斯山脉", "High Atlas", pts=[[-9, 30.8], [-7.8, 31.1], [-6, 31.7], [-4.5, 32.3]], r=0.7,
  c={"Arab": ["达兰山|Jabal Daran|達蘭山"]})
U("middle_atlas", "M", "中阿特拉斯山脉", "Middle Atlas", pts=[[-5.3, 33.2], [-4.5, 33.6], [-3.8, 33.9]], r=0.6)
U("saharan_atlas", "M", "撒哈拉阿特拉斯山脉", "Saharan Atlas", pts=[[-1, 32.8], [1.5, 33.8], [3.5, 34.4]], r=0.8)
U("tell_atlas", "M", "泰勒阿特拉斯山脉", "Tell Atlas", pts=[[-1, 35.2], [1.5, 36.2], [3.5, 36.4], [4.8, 36.4], [7, 36.6]], r=0.6)
# 普罗柯比《汪达尔战争》的 Aurasium(Mons Aurasius)
U("aures", "M", "奥雷斯山脉", "Aurès Mountains", pts=[[6.5, 35.2]], r=0.6, c={"Roma": ["奥拉修斯山|Mons Aurasius|奧拉修斯山"]})
U("ethiopian_highlands", "M", "埃塞俄比亚高原", "Ethiopian Highlands", box=[[35.5, 6, 40.5, 15]])
U("simien", "M", "塞米恩山脉", "Simien Mountains", pts=[[38.2, 13.2]], r=0.7)
U("bale", "M", "贝尔山脉", "Bale Mountains", pts=[[39.7, 6.9], [39.3, 7.8]], r=0.7)
U("gamo_highlands", "M", "加莫高地", "Gamo Highlands", pts=[[37.5, 6.4]], r=0.7)
U("danakil", "D", "达纳基尔沙漠", "Danakil Desert", box=[[39.6, 11, 42.5, 15]])
U("ogaden", "D", "欧加登", "Ogaden", box=[[42, 3.5, 47.5, 7.5]])
U("haud", "D", "豪德高原", "Haud", box=[[44.5, 7.5, 51, 11]])
U("chalbi", "D", "查尔比沙漠", "Chalbi Desert", box=[[36.8, 2.3, 38.4, 3.6]])
U("kalahari", "D", "卡拉哈里沙漠", "Kalahari Desert", box=[[17.5, -28.5, 26, -17.5]])
U("namib", "D", "纳米布沙漠", "Namib Desert", box=[[12, -29, 16.3, -17]])
U("karoo", "D", "卡鲁高原", "Karoo", box=[[18.5, -33.5, 25.5, -30.3]])
U("drakensberg", "M", "德拉肯斯山脉", "Drakensberg", pts=[[27.5, -31], [29, -29.5], [29.8, -28.5], [30.5, -27.5]], r=0.8)
U("congo", "J", "刚果雨林", "Congo Rainforest", box=[[12, -5, 30.5, 5.5]])
U("ituri", "J", "伊图里雨林", "Ituri Rainforest", box=[[27.5, 0, 30.5, 3]])
U("lower_guinea", "J", "下几内亚森林", "Lower Guinean Forests", box=[[5.5, -1.5, 12, 7]])
U("upper_guinea", "J", "上几内亚森林", "Upper Guinean Forests", box=[[-13.5, 4, -3, 10]])
U("mabira", "J", "马比拉森林", "Mabira Forest", pts=[[33.0, 0.45]], r=1.8)
U("madagascar_rainforest", "J", "马达加斯加东部雨林", "Madagascar Rainforests", box=[[47, -25.5, 50.5, -12]])
U("rwenzori", "M", "鲁文佐里山脉", "Rwenzori Mountains", pts=[[29.9, 0.4]], r=0.6)
U("kilimanjaro", "M", "乞力马扎罗山", "Mount Kilimanjaro", pts=[[37.35, -3.07]], r=0.6)
U("sudd", "S", "苏德沼泽", "Sudd", box=[[29.5, 5.5, 32.2, 9.8]])
U("okavango", "S", "奥卡万戈三角洲", "Okavango Delta", box=[[21.5, -20.5, 24, -18.3]])
U("inner_niger_delta", "S", "尼日尔河内陆三角洲", "Inner Niger Delta", box=[[-5.5, 13.5, -3, 16.5]])

# ---------------- 西亚 ----------------
# 阿拉伯半岛：西北一框到波斯湾西岸，东南一框是阿曼(不框进伊朗南部)
U("arabian_desert", "D", "阿拉伯沙漠", "Arabian Desert", box=[[36, 15, 52, 32], [52, 16, 59.5, 24.5]],
  c={"Roma": [ARABIA_DESERTA]})
U("rub_al_khali", "D", "鲁卜哈利沙漠", "Rub' al Khali", box=[[44.5, 16.3, 56, 23.5]])
U("nafud", "D", "内夫得沙漠", "Nafud Desert", box=[[38, 26.5, 44.5, 29.8]], c={"Roma": [ARABIA_DESERTA]})
U("dahna", "D", "代赫纳沙漠", "Ad-Dahna Desert", pts=[[44.8, 27], [46.3, 25.5], [47.5, 24], [48, 22.2]], r=0.8)
U("hadramawt", "D", "哈德拉毛", "Hadhramaut", box=[[47, 14.5, 51.5, 16.5]])
U("tihamah", "D", "帖哈麦", "Tihamah", box=[[42.5, 13, 43.6, 17.5]])
# 早期伊斯兰以来阿拉伯人称叙利亚沙漠为沙姆荒漠(Badiyat al-Sham)
U("syrian_desert", "D", "叙利亚沙漠", "Syrian Desert", box=[[36.5, 30, 43.5, 35.5]],
  c={"Arab": ["沙姆荒漠|Badiyat al-Sham|沙姆荒漠"], "Roma": [ARABIA_DESERTA]})
U("sarawat", "M", "萨拉特山脉", "Sarawat Mountains", pts=[[38.5, 24], [39.8, 21.8], [41.2, 19.2], [42.5, 17.8], [43.5, 16.2]], r=0.9)
U("yemen_highlands", "M", "也门高原", "Yemen Highlands", pts=[[44, 15.3], [44.2, 14], [44.3, 13.3]], r=0.8)
U("hajar", "M", "哈杰尔山脉", "Al Hajar Mountains", pts=[[56.2, 24.8], [57.3, 23.3], [58.5, 22.9]], r=0.7)
# 阿拔斯时代的"大沼泽"(al-Bata'ih)，629 年河决以后成片
U("mesopotamian_marshes", "S", "美索不达米亚沼泽", "Mesopotamian Marshes", box=[[45.8, 30.5, 47.8, 32]],
  c={"Arab": ["630~|巴提哈沼泽|al-Bata'ih|巴提哈沼澤"]})
U("zagros", "M", "扎格罗斯山脉", "Zagros Mountains",
  pts=[[45.5, 35.8], [46.5, 34.5], [47.8, 33.5], [49.5, 32.3], [51, 31], [52.5, 29.8], [54, 29], [56, 28.3]], r=1.3)
U("hakkari", "M", "哈卡里山地", "Hakkâri Mountains", pts=[[43.7, 37.5]], r=1.0)
U("munzur", "M", "芒祖尔山脉", "Munzur Mountains", pts=[[38.7, 39.6], [39.5, 39.3]], r=0.7)
U("alborz", "M", "厄尔布尔士山脉", "Alborz Mountains", pts=[[49.5, 36.6], [51, 36.3], [52.5, 36], [54, 36.4], [55.5, 37], [57, 37.3]], r=0.9)
U("talysh", "M", "塔雷什山脉", "Talysh Mountains", pts=[[48.4, 38.3], [48.5, 37.5]], r=0.6)
U("kuh_e_hazar", "M", "哈扎尔山", "Kuh-e Hazar", pts=[[57.3, 29.4], [57.6, 28.8]], r=0.7)
U("dasht_e_kavir", "D", "卡维尔盐漠", "Dasht-e Kavir", box=[[51.5, 33.3, 57, 36]])
U("dasht_e_lut", "D", "卢特荒漠", "Dasht-e Lut", box=[[56.5, 28.5, 60, 32.8]])
U("dasht_e_margo", "D", "马尔戈荒漠", "Dasht-e Margo", box=[[61.5, 29.8, 64, 31]])
U("registan", "D", "雷吉斯坦沙漠", "Registan Desert", box=[[63.5, 29.5, 67, 31.6]])
U("kharan", "D", "哈兰沙漠", "Kharan Desert", box=[[62.5, 27.5, 66, 29.3]])
U("makran", "MD", "莫克兰山脉", "Makran Range", pts=[[58.5, 26.3], [60.5, 26.4], [62.5, 26.2], [64.5, 26], [65.5, 26.3]], r=0.7)
U("kopet_dag", "M", "科佩特山脉", "Kopet Dag", pts=[[55.3, 38.3], [57, 37.9], [58.5, 37.5], [60, 36.8]], r=0.7)
U("karakum", "D", "卡拉库姆沙漠", "Karakum Desert", box=[[53, 36.3, 65, 42]])
U("kyzylkum", "D", "克孜勒库姆沙漠", "Kyzylkum Desert", box=[[60.5, 39.5, 67.5, 45.3]])
U("ustyurt", "D", "乌斯秋尔特高原", "Ustyurt Plateau", box=[[53, 41.5, 59, 46]])
U("aral_karakum", "D", "咸海卡拉库姆沙漠", "Aral Karakum Desert", box=[[59.5, 45.3, 63, 47.5]])
U("moyynkum", "D", "莫因库姆沙漠", "Moyynkum Desert", box=[[68, 43.5, 73.5, 45.6]])
U("betpak_dala", "D", "别特帕克达拉荒漠", "Betpak-Dala", box=[[66, 45.5, 73, 47.5]])
U("saryesik_atyrau", "D", "萨雷耶西克-阿特劳沙漠", "Saryesik-Atyrau Desert", box=[[74.5, 44.5, 78.5, 46.5]])
U("ryn", "D", "雷恩沙漠", "Ryn Desert", box=[[46, 46, 49.8, 48.6]])
# 麦斯欧迪《黄金草原》等称高加索为卡布克山(Jabal al-Qabq)
U("greater_caucasus", "M", "大高加索山脉", "Greater Caucasus",
  pts=[[38, 44.2], [40, 43.6], [42, 43.3], [43.5, 42.9], [45, 42.5], [46.5, 42], [48, 41.4], [49.2, 40.9]], r=0.9,
  c={"Arab": ["卡布克山|Jabal al-Qabq|卡布克山"]})
U("lesser_caucasus", "M", "小高加索山脉", "Lesser Caucasus",
  pts=[[42.5, 41.3], [43.5, 41.1], [44.5, 41], [45.5, 40.7], [46.2, 40], [46, 39.3]], r=0.9)
U("armenian_highlands", "M", "亚美尼亚高原", "Armenian Highlands", box=[[38, 37.8, 46.5, 41.2]])
# 斯特拉波、托勒密：本都东段的 Paryadres
U("pontic", "M", "本都山脉", "Pontic Mountains", pts=[[34, 41.5], [36.5, 40.8], [38.5, 40.6], [40.3, 40.7], [41.2, 41]], r=0.6,
  c={"Roma": ["帕里亚德雷斯山|Paryadres|帕里亞德雷斯山"]})
U("taurus", "M", "托罗斯山脉", "Taurus Mountains",
  pts=[[29.5, 36.8], [31.5, 36.9], [33, 36.9], [34.8, 37.3], [36.5, 37.8], [38.5, 38.2], [40.5, 38.4], [42.5, 38.3]], r=0.9)
U("lebanon_mountains", "M", "黎巴嫩山脉", "Mount Lebanon", pts=[[35.9, 34], [36.1, 34.4]], r=0.5)

# ---------------- 中亚、南亚 ----------------
U("thar", "D", "塔尔沙漠", "Thar Desert", box=[[68.5, 24, 75.2, 30.2]],
  c={"India": ["马鲁斯塔利|Marusthali|馬魯斯塔利"]})       # 梵语"荒漠之地"(Marusthali、Marubhumi)
U("cholistan", "D", "乔利斯坦沙漠", "Cholistan Desert", box=[[70.5, 27.5, 73.5, 29.6]])
U("kachhi", "D", "卡奇平原", "Kachhi Plain", box=[[67.3, 27.8, 69.6, 30]])
U("rann_of_kutch", "SD", "卡奇沼泽", "Rann of Kutch", box=[[68.5, 23, 71.5, 24.6]])
# 玄奘《大唐西域记》的大雪山即兴都库什；亚历山大以来希腊人称 Paropamisus
U("hindu_kush", "M", "兴都库什山脉", "Hindu Kush",
  pts=[[67.8, 34.8], [69.3, 35.3], [70.5, 35.7], [71.5, 36.2], [72.8, 36.6]], r=1.2,
  c={"Huaxia": ["630~|大雪山|Great Snowy Mountains|大雪山"], "Roma": ["帕罗帕米苏斯山|Paropamisus|帕羅帕米蘇斯山"]})
U("safid_kuh", "M", "萨菲德山脉", "Safid Kuh", pts=[[62.5, 34.6], [64.5, 34.7]], r=0.8)
U("koh_i_baba", "M", "巴巴山脉", "Koh-i-Baba", pts=[[67.5, 34.6]], r=0.6)
U("spin_ghar", "M", "斯平加尔山脉", "Spin Ghar", pts=[[70.5, 34.1]], r=0.5)
U("sulaiman", "M", "苏莱曼山脉", "Sulaiman Mountains", pts=[[69.8, 30], [69.9, 31.3], [69.6, 32.4]], r=0.8)
U("kirthar", "M", "基尔塔尔山脉", "Kirthar Mountains", pts=[[67.4, 26.3], [67.3, 27.8]], r=0.6)
# 张骞通西域以后称葱岭；托勒密的 Imaus
U("pamir", "MT", "帕米尔高原", "Pamir Mountains", box=[[71, 36.8, 75.5, 39.6]],
  c={"Huaxia": ["-120~|葱岭|Congling|蔥嶺"], "Roma": ["伊茂斯山|Imaus|伊茂斯山"]})
U("karakoram", "MT", "喀喇昆仑山脉", "Karakoram", pts=[[74.5, 36.2], [76, 35.8], [77.3, 35.3], [78.5, 34.7]], r=0.9)
# 法显以来汉译佛典称雪山(梵语 Himavat)；托勒密的 Emodus
U("himalaya", "MT", "喜马拉雅山脉", "Himalayas",
  pts=[[73.5, 34.8], [75.5, 33.6], [77.5, 32.3], [79.3, 30.8], [81.5, 29.8], [83.5, 28.7], [85.5, 28.2], [87.3, 27.9],
       [89.3, 27.9], [91.3, 27.9], [93.3, 28.2], [95, 29]], r=1.1,
  c={"Huaxia": ["400~|雪山|Snowy Mountains|雪山"], "India": ["雪山|Himavat|雪山"], "Roma": ["埃莫杜斯山|Emodus|埃莫杜斯山"]})
U("pir_panjal", "M", "皮尔本贾尔山脉", "Pir Panjal Range", pts=[[74.3, 33.6], [75.2, 33.3]], r=0.6)
U("tibetan_plateau", "MT", "青藏高原", "Tibetan Plateau", box=[[78.5, 28, 103, 38.3]])
U("changtang", "TM", "羌塘高原", "Changtang", box=[[79.5, 31.5, 92.3, 36.2]])
U("hoh_xil", "TM", "可可西里", "Hoh Xil", box=[[89.5, 34.3, 94.5, 36.6]])
U("tanggula", "MT", "唐古拉山脉", "Tanggula Mountains", pts=[[90, 33.2], [91.8, 33], [93.5, 33.3]], r=0.6)
U("nyainqentanglha", "MT", "念青唐古拉山脉", "Nyenchen Tanglha", pts=[[89.5, 30.3], [91, 30.5], [92.8, 30.4], [94.5, 30]], r=0.7)
U("gangdise", "MT", "冈底斯山脉", "Gangdise Mountains",
  pts=[[80.5, 31.3], [82.5, 31], [84.5, 30.5], [86.5, 30.2], [88.5, 30]], r=0.7,
  c={"India": ["凯拉斯山|Kailasa|凱拉斯山"]})
U("bayan_har", "MT", "巴颜喀拉山脉", "Bayan Har Mountains", pts=[[95, 34.5], [97.3, 34.1], [99.5, 33.4]], r=1.0)
U("amne_machin", "MT", "阿尼玛卿山", "Amne Machin", pts=[[99.5, 34.8]], r=0.8)
U("kunlun", "MT", "昆仑山脉", "Kunlun Mountains",
  pts=[[75.5, 36.8], [77.5, 36.2], [79.5, 35.9], [81.5, 35.8], [83.5, 36], [85.5, 36.2], [87.5, 36.3], [89.5, 36.2],
       [91.5, 35.9], [93.5, 35.8], [95.5, 35.6], [97.5, 35.5]], r=1.0)
U("altun", "M", "阿尔金山脉", "Altun Mountains", pts=[[87.5, 37.7], [89.5, 38.2], [91.5, 38.7], [93.5, 39.1]], r=0.8)
U("qilian", "M", "祁连山脉", "Qilian Mountains",
  pts=[[94.5, 39.3], [96.5, 39.1], [98.5, 38.7], [100.3, 38.2], [102, 37.6], [103.2, 37.1]], r=0.8)
U("qaidam", "DT", "柴达木盆地", "Qaidam Basin", box=[[90.5, 35.8, 98.5, 39]])
U("tianshan", "M", "天山山脉", "Tian Shan",
  pts=[[69.5, 41.8], [71.5, 41.8], [73.5, 41.6], [75.5, 41.5], [77.5, 42], [79.5, 42.1], [81.5, 42.6], [83.5, 43.1],
       [85.5, 43.4], [87.5, 43.3], [89.5, 43.4], [91.5, 43.3], [93.5, 43.2], [94.5, 43]], r=1.1)
U("bogda", "M", "博格达山", "Bogda Shan", pts=[[88.3, 43.7], [89.5, 43.6], [90.3, 43.5]], r=0.6)
U("karlik", "M", "哈尔里克山", "Karlik Tagh", pts=[[93.7, 43.3]], r=0.7)
U("dzungarian_alatau", "M", "准噶尔阿拉套山", "Dzungarian Alatau", pts=[[79, 45.1], [80.5, 45.1], [82, 44.9]], r=0.7)
U("tarbagatai", "M", "塔尔巴哈台山", "Tarbagatai Mountains", pts=[[82.5, 47.2], [84.5, 47.4]], r=0.6)
# 《北史·突厥传》"居金山之阳"；突厥碑铭 Altun Yish(金山)。元以后称按台、阿尔泰
U("altai", "M", "阿尔泰山脉", "Altai Mountains",
  pts=[[85.5, 50], [87.5, 49.6], [89, 49.1], [90.5, 48.4], [91.8, 47.6], [93.3, 46.7], [94.8, 46]], r=1.2,
  c={"Huaxia": ["550~1250|金山|Jinshan|金山"], "Youmu": ["550~1250|金山|Altun Yish|金山"]})
U("gobi_altai", "M", "戈壁阿尔泰山脉", "Gobi Altai", pts=[[96, 45.4], [98, 45], [100, 44.6], [102, 44.2], [104, 43.8]], r=0.8)
# 《汉书》《后汉书》的燕然山(窦宪勒石)；突厥、回纥牙帐所在的于都斤山(Ötüken)
U("khangai", "M", "杭爱山脉", "Khangai Mountains", pts=[[97.5, 47.6], [99.3, 47.5], [101, 47.3], [102.2, 47]], r=1.0,
  c={"Huaxia": ["-100~|燕然山|Yanran Mountains|燕然山"], "Youmu": ["550~900|于都斤山|Ötüken|於都斤山"]})
# 《蒙古秘史》的不儿罕合勒敦
U("khentii", "M", "肯特山脉", "Khentii Mountains", pts=[[108.3, 48.5], [109.5, 49], [110.5, 49.4]], r=0.8,
  c={"Youmu": ["1100~|不儿罕山|Burkhan Khaldun|不兒罕山"]})
U("gurbantunggut", "D", "古尔班通古特沙漠", "Gurbantünggüt Desert", box=[[84, 44.2, 91, 46.8]])
# 《尚书·禹贡》"西被于流沙"，玄奘称大流沙
U("taklamakan", "D", "塔克拉玛干沙漠", "Taklamakan Desert", box=[[76, 36.5, 89, 41.8]], c={"Huaxia": ["流沙|Liusha|流沙"]})
U("lop_desert", "D", "罗布荒漠", "Lop Desert", box=[[88.3, 39.3, 91.8, 41.3]], c={"Huaxia": ["-100~|白龙堆|Bailongdui|白龍堆"]})
# 法显"度沙河"；《慈恩传》"莫贺延碛，长八百余里，古曰沙河"
U("kumtag", "D", "库姆塔格沙漠", "Kumtag Desert", box=[[89.5, 39, 94, 40.8]], c={"Huaxia": ["-100~629|沙河|Shahe|沙河"]})
U("gashun_gobi", "D", "噶顺戈壁", "Gashun Gobi", box=[[91, 40.5, 96, 42.6]], c={"Huaxia": ["630~|莫贺延碛|Moheyan Desert|莫賀延磧"]})
# 唐以后称瀚海(瀚海都护府、岑参"瀚海阑干百丈冰")，此前泛称大漠
U("gobi", "D", "戈壁", "Gobi Desert", box=[[96, 40.5, 116, 46.5]],
  c={"Huaxia": ["650~|瀚海|Hanhai|瀚海", "大漠|Damo|大漠"]})
U("badain_jaran", "D", "巴丹吉林沙漠", "Badain Jaran Desert", box=[[98.5, 39.3, 104, 42.3]])
U("tengger", "D", "腾格里沙漠", "Tengger Desert", box=[[102.3, 37.4, 106, 40.2]])
U("ulan_buh", "D", "乌兰布和沙漠", "Ulan Buh Desert", box=[[105.8, 39.4, 107.2, 40.9]])
U("kubuqi", "D", "库布齐沙漠", "Kubuqi Desert", box=[[107, 39.8, 111.5, 40.8]])
U("mu_us", "D", "毛乌素沙地", "Mu Us Desert", box=[[107, 37.4, 110.6, 39.7]])
U("hunshandake", "D", "浑善达克沙地", "Otindag Sandy Land", box=[[112, 42, 117.5, 43.9]])
U("horqin", "D", "科尔沁沙地", "Horqin Sandy Land", box=[[119, 42.5, 123.8, 44.5]])

# ---------------- 华夏、东北亚 ----------------
U("qinling", "M", "秦岭", "Qinling Mountains", pts=[[104.5, 34.2], [106.5, 33.9], [108.3, 33.9], [110.2, 33.8], [111.3, 33.7]], r=0.7)
U("daba", "M", "大巴山", "Daba Mountains", pts=[[106.5, 32.4], [108, 32.1], [109.3, 31.8], [110.4, 31.5]], r=0.6)
U("min_shan", "M", "岷山", "Min Mountains", pts=[[103.8, 32.8], [104.1, 33.4]], r=0.7)
U("qionglai", "M", "邛崃山", "Qionglai Mountains", pts=[[102.9, 30.9], [102.6, 31.4]], r=0.6)
U("daxue_shan", "M", "大雪山", "Daxue Mountains", pts=[[101.8, 29.6], [101.7, 30.6]], r=0.6)
U("shaluli", "M", "沙鲁里山", "Shaluli Mountains", pts=[[99.5, 30], [99.3, 31.5]], r=0.7)
U("hengduan", "M", "横断山脉", "Hengduan Mountains", box=[[97.5, 24.5, 103, 33]])
U("gaoligong", "M", "高黎贡山", "Gaoligong Mountains", pts=[[98.6, 24.7], [98.7, 25.8], [98.6, 27]], r=0.6)
U("ailao", "M", "哀牢山", "Ailao Mountains", pts=[[101.2, 24.6], [102, 23.8], [102.8, 23.2], [103.5, 22.7]], r=0.6)
U("wuliang", "M", "无量山", "Wuliang Mountains", pts=[[100.6, 24.5]], r=0.5)
U("taihang", "M", "太行山", "Taihang Mountains", pts=[[113.3, 35.4], [113.8, 36.5], [114, 37.6], [114.4, 38.8], [114.8, 39.8]], r=0.6)
U("luliang", "M", "吕梁山", "Lüliang Mountains", pts=[[111.3, 36.8], [111.4, 38], [111.6, 39]], r=0.6)
U("yanshan", "M", "燕山", "Yan Mountains", pts=[[115.8, 40.6], [117.3, 40.5], [118.7, 40.3]], r=0.6)
U("liupan", "M", "六盘山", "Liupan Mountains", pts=[[106.2, 35.6]], r=0.6)
U("helan", "M", "贺兰山", "Helan Mountains", pts=[[105.9, 38.7]], r=0.6)
U("yinshan", "M", "阴山", "Yin Mountains", pts=[[107.5, 41.2], [109.2, 41.1], [110.8, 41.1], [112.5, 41.2]], r=0.7)
U("dabie", "M", "大别山", "Dabie Mountains", pts=[[115.5, 31.2], [116.2, 31]], r=0.6)
U("wuyi", "M", "武夷山", "Wuyi Mountains", pts=[[116.8, 26.8], [117.6, 27.6], [118.2, 28.2]], r=0.6)
# 秦发兵五岭(《史记·张耳陈馀列传》"南有五岭之戍")
U("nanling", "M", "南岭", "Nanling Mountains", pts=[[110.2, 25.6], [111.8, 25.2], [113.3, 25], [114.8, 24.8]], r=0.7,
  c={"Huaxia": ["-220~|五岭|Five Ridges|五嶺"]})
U("taiwan_central", "MJ", "中央山脉", "Central Mountain Range", pts=[[121.1, 24], [121, 23.2]], r=0.6)
# 南方的雨林、大泽：地表网格是今天的草地，剧本按时代改回当年没开出来的丛林沼泽(WorldMaps/development.json，
# 见 WorldMapDevelopment.cs)，这里给它们起真实地名(不给的话会被近处的长山山脉、中央山脉吸过去)
U("yunmeng", "S", "云梦泽", "Yunmeng Marsh", box=[[112.3, 29.85, 114.2, 31.1]])
U("dongting", "S", "洞庭湖区", "Dongting Lake Wetlands", box=[[111.8, 28.5, 113.2, 29.85]])
# 《禹贡》"彭蠡既潴"；隋唐南扩，始称鄱阳湖
U("pengli", "S", "鄱阳湖区", "Poyang Lake Wetlands", box=[[115.6, 28.5, 117.0, 30.0]], alt=["600|彭蠡泽|Pengli Marsh|彭蠡澤"])
# 《禹贡》"震泽底定"
U("taihu_marsh", "S", "太湖沼泽", "Lake Tai Marshes", box=[[118.9, 30.3, 121.9, 32.0]], alt=["600|震泽|Zhenze Marsh|震澤"])
U("jianghuai_marsh", "S", "江淮湖荡", "Jianghuai Wetlands", box=[[116.6, 31.3, 121.0, 33.9]])
# 孙吴屡讨丹阳、新都山越(《三国志·诸葛恪传》)
U("wannan", "J", "皖南山地", "Southern Anhui Hills", box=[[117.2, 29.2, 120.0, 31.0]], alt=["-220~600|山越|Shanyue Highlands|山越"])
U("zhenan", "J", "浙南山地", "Southern Zhejiang Hills", box=[[118.2, 27.2, 121.9, 29.6]])
U("minzhong", "J", "闽中山地", "Fujian Uplands", box=[[115.9, 23.5, 120.6, 28.3]])
U("jiangnan_hills", "J", "江南丘陵", "Jiangnan Hills", box=[[109.5, 24.5, 118.3, 29.9]])
# 五溪蛮(《后汉书·南蛮传》)，雍正改土归流前多为土司
U("wuling", "J", "武陵山区", "Wuling Mountains", box=[[108.3, 26.8, 110.9, 30.6]], alt=["1700|五溪|Wuxi|五溪"])
U("lingnan_jungle", "J", "岭南丛林", "Lingnan Jungle", box=[[107.8, 20.2, 117.3, 26.3]])
U("zuoyoujiang", "J", "左右江山林", "Zuo-You River Forests", box=[[104.5, 21.6, 107.8, 25.6]])
U("qianzhong", "J", "黔中山林", "Guizhou Highland Forests", box=[[103.7, 24.8, 109.5, 29.5]])
U("hainan_rainforest", "J", "海南雨林", "Hainan Rainforest", box=[[108.5, 18.1, 111.1, 20.2]])
# 早期南方连成一大片丛林时的总名(百越之地，《汉书·地理志》颜注"自交阯至会稽七八千里，百越杂处")
U("southern_forests", "J", "南方山林", "Southern China Forests", box=[[104.5, 20.2, 122.0, 31.0]],
  alt=["600|百越山林|Baiyue Forests|百越山林"])
# 《魏书·序纪》鲜卑先世"国有大鲜卑山"，嘎仙洞石室在大兴安岭北段
U("greater_khingan", "MT", "大兴安岭", "Greater Khingan",
  pts=[[118, 44], [119.3, 45.6], [120.2, 47.3], [121, 49], [121.5, 50.8], [121.8, 52.5], [122.5, 53.3]], r=1.0,
  alt=["-100~1000|大鲜卑山|Great Xianbei Mountains|大鮮卑山"])
U("lesser_khingan", "MT", "小兴安岭", "Lesser Khingan", pts=[[126.5, 49.8], [128, 48.8], [129.3, 47.8], [130, 47]], r=0.8)
# 《山海经》《晋书·肃慎传》的不咸山；北魏徒太山、隋唐太白山(《新唐书·黑水靺鞨传》)；辽金以后称长白山
U("changbai", "M", "长白山", "Changbai Mountains", pts=[[128.1, 42], [127.5, 41.5], [129, 42.6], [130, 43.5]], r=0.9,
  alt=["400|不咸山|Buxian Mountain|不咸山", "1100|太白山|Taibai Mountain|太白山"])
# 《后汉书·东夷传》"东沃沮在高句骊盖马大山之东"
U("kaema", "MT", "盖马高原", "Kaema Plateau", box=[[126.8, 40.3, 129, 41.6]], alt=["600|盖马大山|Gaima Mountains|蓋馬大山"])
U("taebaek", "M", "太白山脉", "Taebaek Mountains", pts=[[128.3, 38.6], [128.8, 37.5], [129, 36.5]], r=0.6)
U("sikhote_alin", "MT", "锡霍特山脉", "Sikhote-Alin", pts=[[133.5, 43.8], [135.3, 45], [136.5, 46.3], [137.6, 47.6], [138.5, 49]], r=1.0)
U("bureya", "MT", "布列亚山脉", "Bureya Mountains", pts=[[132.6, 51], [133.3, 52.5], [134.3, 53.8], [135.5, 54.5]], r=1.0)
U("stanovoy", "MT", "外兴安岭", "Stanovoy Range", pts=[[119.5, 56.2], [122.5, 56], [125.5, 55.8], [128.5, 55.3], [131.5, 55]], r=1.0)
U("dzhugdzhur", "MT", "朱格朱尔山脉", "Dzhugdzhur Mountains", pts=[[136.5, 57.5], [138, 58.5], [139.5, 59.3]], r=0.9)
U("hida", "M", "飞驒山脉", "Hida Mountains", pts=[[137.6, 36.3], [137.7, 36.8]], r=0.5)
U("kiso", "M", "木曾山脉", "Kiso Mountains", pts=[[137.8, 35.7]], r=0.4)
U("akaishi", "M", "赤石山脉", "Akaishi Mountains", pts=[[138.2, 35.4]], r=0.5)
U("ou", "M", "奥羽山脉", "Ōu Mountains", pts=[[140.8, 40.5], [140.7, 39.5], [140.4, 38.3], [140.2, 37.5]], r=0.6)
U("kii", "M", "纪伊山地", "Kii Mountains", pts=[[135.8, 34.1]], r=0.5)
U("shikoku", "M", "四国山地", "Shikoku Mountains", pts=[[133.3, 33.8]], r=0.6)
U("kyushu", "M", "九州山地", "Kyushu Mountains", pts=[[131.2, 32.4]], r=0.5)
U("ishikari", "MT", "石狩山地", "Ishikari Mountains", pts=[[142.9, 43.6]], r=0.7)

# ---------------- 西伯利亚、北极 ----------------
U("siberia", "TMS", "西伯利亚", "Siberia", box=[[60, 50, 160, 78]], c={"Huaxia": [BEIHAI]})
U("west_siberian", "TS", "西西伯利亚平原", "West Siberian Plain", box=[[60, 55, 90, 73]])
U("vasyugan", "S", "瓦休甘沼泽", "Vasyugan Swamp", box=[[74, 55.5, 83, 58.5]])
U("yamal", "T", "亚马尔半岛", "Yamal Peninsula", box=[[66, 66.5, 72, 72.5]])
U("gydan", "T", "格达半岛", "Gydan Peninsula", box=[[72.5, 68.5, 79, 72.5]])
U("taymyr", "T", "泰梅尔半岛", "Taymyr Peninsula", box=[[80, 70.5, 114, 77.8]])
U("putorana", "TM", "普托拉纳高原", "Putorana Plateau", box=[[88, 66, 98, 70.5]])
U("central_siberian", "TM", "中西伯利亚高原", "Central Siberian Plateau", box=[[90, 57, 120, 70.5]])
U("yana_indigirka", "T", "亚纳-印迪吉尔卡低地", "Yana–Indigirka Lowland", box=[[133, 69, 150, 72.5]])
U("kolyma_lowland", "T", "科雷马低地", "Kolyma Lowland", box=[[150, 68, 163, 72]])
U("verkhoyansk", "MT", "上扬斯克山脉", "Verkhoyansk Range",
  pts=[[127, 71.5], [128.5, 69.5], [129, 67.5], [130.5, 65.5], [133, 63.8], [136, 62.5]], r=1.2)
U("chersky", "MT", "切尔斯基山脉", "Chersky Range", pts=[[139, 67.8], [141.5, 66.5], [144, 65.8], [146.5, 65], [149, 64.5], [152, 63.8]], r=1.2)
U("suntar_khayata", "MT", "苏恩塔尔-哈亚塔山脉", "Suntar-Khayata Range", pts=[[140.5, 62.5], [142, 62]], r=0.9)
U("kolyma_mountains", "MT", "科雷马山地", "Kolyma Mountains", pts=[[150, 62.5], [152, 61.5], [151.5, 60.3], [154, 61.5]], r=1.2)
U("patom", "TM", "帕托姆高原", "Patom Highlands", pts=[[115.5, 58.7]], r=1.0)
U("kodar", "MT", "科达尔山脉", "Kodar Range", pts=[[117.5, 56.8]], r=0.8)
U("stanovoy_highlands", "MT", "斯塔诺夫高原", "Stanovoy Highlands", pts=[[112.8, 56], [114.5, 56.4]], r=0.9)
U("barguzin", "MT", "巴尔古津山脉", "Barguzin Range", pts=[[110, 54.8], [110.8, 55.8]], r=0.7, c={"Huaxia": [BEIHAI]})
U("baikal_mountains", "MT", "贝加尔山脉", "Baikal Mountains", pts=[[107, 55], [108, 56.2], [108.8, 57]], r=0.8, c={"Huaxia": [BEIHAI]})
U("khamar_daban", "MT", "哈马尔达坂山", "Khamar-Daban", pts=[[103, 51.3], [104.8, 51.4], [106, 51.6]], r=0.7, c={"Huaxia": [BEIHAI]})
U("sayan", "MT", "萨彦岭", "Sayan Mountains", pts=[[89, 52.2], [92, 52.6], [95, 53.5], [98, 52.6], [101, 51.7]], r=1.2)
U("eastern_sayan", "MT", "东萨彦岭", "Eastern Sayan", pts=[[95.5, 54], [97.5, 53], [99, 52.2], [100.5, 51.8], [102, 51.6]], r=0.9)
U("western_sayan", "MT", "西萨彦岭", "Western Sayan", pts=[[89, 52.2], [91, 52.5], [92.8, 52.6], [94.5, 52.7]], r=0.8)
U("tannu_ola", "M", "唐努乌拉山", "Tannu-Ola Mountains", pts=[[90, 51], [92.5, 50.9], [95, 50.7]], r=0.6)
U("new_siberian_islands", "T", "新西伯利亚群岛", "New Siberian Islands", box=[[135, 73.5, 152, 76.5]])
U("severnaya_zemlya", "T", "北地群岛", "Severnaya Zemlya", box=[[90, 77.5, 108, 81.5]])
U("novaya_zemlya", "T", "新地岛", "Novaya Zemlya", box=[[51, 70.5, 69, 77.5]])
# 俄罗斯文献称乌拉尔为 Kamen(石)、Kamenny Poyas(石带)，16、17 世纪最通行
U("ural", "MT", "乌拉尔山脉", "Ural Mountains",
  pts=[[58.8, 51.8], [59, 54], [59.2, 56.5], [59.3, 59], [59.2, 61.5], [59.8, 63.8], [61.5, 65.8], [64.5, 67.5], [66, 68.5]], r=1.0,
  c={"Slavonic": ["1450~|石带|Stone Belt|石帶"]})
U("timan", "M", "季曼岭", "Timan Ridge", pts=[[50.5, 62.5], [51.5, 64], [53, 65.8]], r=0.8)
U("khibiny", "MT", "希比内山", "Khibiny Mountains", pts=[[33.6, 67.7]], r=0.6)
U("kola", "T", "科拉半岛", "Kola Peninsula", box=[[30, 66, 41.5, 70]])
# 古诺尔斯语 Finnmǫrk(萨米人之地)，九世纪奥特雷的航行记已有
U("lapland", "TM", "拉普兰", "Lapland", box=[[14, 66, 30, 71]], c={"Viking": ["800~|芬马克|Finnmork|芬馬克"]})
# 古诺尔斯语 Kjǫlr(龙骨)，萨迦里分隔挪威与瑞典的山
U("scandinavian", "MT", "斯堪的纳维亚山脉", "Scandinavian Mountains",
  pts=[[6.8, 59.3], [7.8, 60.5], [8.8, 61.8], [10.5, 62.8], [12.5, 63.8], [14, 65.3], [16, 67], [18, 68.3], [20, 69.2]], r=1.1,
  c={"Viking": ["基阿连山脉|Kjölen|基阿連山脈"]})
U("jotunheimen", "MT", "尤通黑门山", "Jotunheimen", pts=[[8.3, 61.6]], r=0.6)
U("iceland_highlands", "TM", "冰岛高地", "Icelandic Highlands", box=[[-24.5, 63.3, -13.5, 66.6]])
U("vatnajokull", "T", "瓦特纳冰川", "Vatnajökull", pts=[[-16.7, 64.4]], r=0.6)
U("greenland", "T", "格陵兰冰盖", "Greenland Ice Sheet", box=[[-35, 68, -10, 84]])
U("svalbard", "T", "斯瓦尔巴群岛", "Svalbard", box=[[10, 76, 34, 81]])

# ---------------- 欧洲 ----------------
U("alps", "M", "阿尔卑斯山脉", "Alps",
  pts=[[6.8, 44.2], [6.9, 45.3], [7.6, 45.9], [8.8, 46.4], [10.2, 46.5], [11.5, 46.9], [12.8, 47.1], [14.2, 47.2], [15.5, 47.4]], r=0.9)
U("pyrenees", "M", "比利牛斯山脉", "Pyrenees", pts=[[-1.6, 43], [-0.2, 42.8], [1, 42.7], [2.2, 42.5]], r=0.6)
U("cantabrian", "M", "坎塔布里亚山脉", "Cantabrian Mountains", pts=[[-6.8, 42.9], [-5.3, 43], [-4, 43.1]], r=0.6)
U("sistema_central", "M", "西班牙中央山脉", "Sistema Central", pts=[[-6, 40.2], [-5, 40.3], [-4, 40.8]], r=0.6)
U("sierra_nevada_es", "M", "内华达山脉", "Sierra Nevada", pts=[[-3.3, 37.05]], r=0.5)
U("massif_central", "M", "中央高原", "Massif Central", pts=[[2.9, 45.3], [3.5, 44.5]], r=0.9)
U("apennines", "M", "亚平宁山脉", "Apennines",
  pts=[[9.5, 44.5], [11, 44.1], [12.5, 43.5], [13.4, 42.6], [14.2, 41.8], [15.3, 40.9], [16, 40], [16.2, 39]], r=0.6)
U("dinaric", "M", "迪纳拉山脉", "Dinaric Alps", pts=[[15, 45], [16.3, 44.2], [17.5, 43.6], [18.7, 43], [19.8, 42.5]], r=0.7)
# 希腊语 Haimos、拉丁语 Haemus Mons
U("balkan_mountains", "M", "巴尔干山脉", "Balkan Mountains", pts=[[22.6, 43.5], [23.8, 43], [25, 42.8], [26.5, 42.7], [27.5, 42.7]], r=0.5,
  c={"Roma": ["海摩斯山|Haemus|海摩斯山"]})
U("rhodope", "M", "罗多彼山脉", "Rhodope Mountains", pts=[[24.2, 41.7], [25.2, 41.6]], r=0.6)
U("pindus", "M", "品都斯山脉", "Pindus Mountains", pts=[[20.8, 40.3], [21.3, 39.6], [21.6, 39]], r=0.6)
U("carpathians", "M", "喀尔巴阡山脉", "Carpathian Mountains",
  pts=[[17.5, 48.8], [19.5, 49.2], [21.5, 49.2], [23.5, 48.6], [24.8, 47.8], [25.5, 46.8], [26, 45.6], [24.5, 45.4],
       [23, 45.3], [22.3, 45.5]], r=0.7)
U("scottish_highlands", "M", "苏格兰高地", "Scottish Highlands", pts=[[-5.2, 57], [-4, 57.3], [-4.5, 58.2]], r=0.9)
U("pripet", "S", "平斯克沼泽", "Pripet Marshes", box=[[23.5, 51, 31, 53]], c={"Slavonic": ["波列西耶|Polesia|波列西耶"]})

# ---------------- 东南亚、大洋洲 ----------------
U("annamite", "MJ", "长山山脉", "Annamite Range",
  pts=[[103.8, 19.8], [105, 18.5], [106, 17.5], [106.8, 16.5], [107.6, 15.5], [108.2, 14.5], [108.5, 13]], r=0.9)
U("cardamom", "MJ", "豆蔻山脉", "Cardamom Mountains", pts=[[103.2, 11.9], [103.9, 11.5], [104.3, 11.2]], r=0.8)
U("tenasserim", "MJ", "丹那沙林山脉", "Tenasserim Hills", pts=[[98.6, 16], [98.9, 14.5], [99, 13], [99, 11.5], [98.8, 10]], r=0.9)
U("arakan_yoma", "MJ", "若开山脉", "Arakan Mountains", pts=[[93.8, 21.5], [94.2, 20], [94.5, 18.6], [94.6, 17.3]], r=0.8)
U("bago_yoma", "MJ", "勃固山脉", "Bago Yoma", pts=[[95.9, 19.2], [96, 18], [96.2, 17]], r=0.6)
U("naga_hills", "MJ", "那加山", "Naga Hills", pts=[[94.5, 25.5], [95, 26.3]], r=0.8)
U("patkai", "MJ", "巴特开山脉", "Patkai Range", pts=[[95.5, 27], [96.5, 27.3]], r=0.7)
U("malay_rainforest", "J", "马来半岛雨林", "Malayan Rainforest", box=[[100, 1.2, 104.5, 6.8]])
U("titiwangsa", "MJ", "蒂迪旺沙山脉", "Titiwangsa Mountains", pts=[[101.3, 5.5], [101.6, 4.3], [101.9, 3.3]], r=0.6)
U("sankalakhiri", "MJ", "山卡拉奇里山脉", "Sankalakhiri Range", pts=[[101.2, 6]], r=0.6)
U("sumatra_rainforest", "J", "苏门答腊雨林", "Sumatran Rainforest", box=[[95, -6, 106, 6]])
U("barisan", "MJ", "巴里桑山脉", "Barisan Mountains",
  pts=[[96.5, 4], [98.5, 2], [100.3, -0.5], [101.8, -2.5], [103.5, -4.2], [104.5, -5.3]], r=0.6)
U("borneo_rainforest", "J", "婆罗洲雨林", "Borneo Rainforest", box=[[108.5, -4.5, 119.5, 7.5]])
U("java_rainforest", "J", "爪哇雨林", "Javan Rainforest", box=[[105, -8.8, 115, -5.8]])
U("sulawesi_rainforest", "J", "苏拉威西雨林", "Sulawesi Rainforest", box=[[118.5, -6, 125.5, 2]])
U("mindanao_rainforest", "J", "棉兰老雨林", "Mindanao Rainforest", box=[[121.8, 5.5, 126.8, 10]])
U("luzon_sierra_madre", "MJ", "马德雷山脉", "Sierra Madre", pts=[[122, 17.8], [121.8, 16.8], [121.5, 15.8], [121.4, 15]], r=0.6)
U("zambales", "MJ", "三描礼士山脉", "Zambales Mountains", pts=[[120.1, 15.2], [120.2, 15.9]], r=0.6)
U("cordillera_central_ph", "MJ", "科迪勒拉中央山脉", "Cordillera Central", pts=[[120.9, 16.8], [121, 17.6]], r=0.6)
U("new_guinea_rainforest", "J", "新几内亚雨林", "New Guinea Rainforest", box=[[130.5, -10.5, 155, 0]])
U("maoke", "MTJ", "毛克山脉", "Maoke Mountains", pts=[[135.8, -3.8], [137.2, -4], [138.6, -4.2], [140.3, -4.6]], r=0.8)
U("new_guinea_highlands", "MJ", "新几内亚高地", "New Guinea Highlands",
  pts=[[141.5, -5.2], [143, -5.6], [144.5, -5.9], [145.8, -6.2], [147, -7]], r=0.9)
U("sepik", "JS", "塞皮克盆地", "Sepik Basin", box=[[141, -5, 144.6, -3]])
U("south_new_guinea", "JS", "南新几内亚低地", "Southern New Guinea Lowlands", box=[[136.5, -9.2, 144.5, -5]])
U("mamberamo", "JS", "曼伯拉莫盆地", "Mamberamo Basin", box=[[136.5, -3.6, 140.2, -1.6]])
U("vogelkop", "J", "鸟头半岛", "Bird's Head Peninsula", box=[[130.8, -2.7, 134.8, -0.3]])
U("outback", "D", "澳大利亚内陆", "Australian Outback", box=[[115, -33, 145, -18]])
U("great_victoria", "D", "维多利亚大沙漠", "Great Victoria Desert", box=[[123, -31.5, 135, -26.5]])
U("great_sandy", "D", "大沙沙漠", "Great Sandy Desert", box=[[120, -23, 128, -18.5]])
U("little_sandy", "D", "小沙沙漠", "Little Sandy Desert", box=[[119.5, -25.5, 123, -22.5]])
U("gibson", "D", "吉布森沙漠", "Gibson Desert", box=[[121, -26, 129, -22.5]])
U("tanami", "D", "塔纳米沙漠", "Tanami Desert", box=[[128, -22.5, 133, -18]])
U("simpson", "D", "辛普森沙漠", "Simpson Desert", box=[[134.5, -28, 139.5, -23.5]])
U("strzelecki", "D", "斯特泽莱茨基沙漠", "Strzelecki Desert", box=[[139, -30, 143, -27]])
U("sturt_stony", "D", "斯特尔特石漠", "Sturt Stony Desert", box=[[139.5, -28.5, 141.5, -26.5]])
U("tirari", "D", "蒂拉里沙漠", "Tirari Desert", box=[[137.5, -29.5, 139, -27.5]])
U("nullarbor", "D", "纳拉伯平原", "Nullarbor Plain", box=[[125, -32.5, 132, -30]])
U("great_dividing_range", "M", "大分水岭", "Great Dividing Range",
  pts=[[146.5, -36.8], [148.5, -35.8], [150, -33.5], [151.5, -30.5], [151.8, -28], [150.5, -25], [148.5, -22], [146.5, -19.5],
       [145.5, -17]], r=0.8)
U("wet_tropics", "J", "昆士兰湿热带雨林", "Wet Tropics of Queensland", box=[[144.8, -19.5, 146.5, -15.3]])

# ---------------- 印度次大陆 ----------------
U("western_ghats", "MJ", "西高止山脉", "Western Ghats",
  pts=[[73.7, 20], [73.7, 18.5], [73.9, 17], [74.2, 15.5], [74.8, 14], [75.4, 12.7], [76, 11.5], [76.8, 10.3], [77.2, 9.2], [77.3, 8.4]], r=0.6,
  c={"India": ["萨希亚德里|Sahyadri|薩希亞德里"]})        # 往世书七大山系之一
U("eastern_ghats", "M", "东高止山脉", "Eastern Ghats",
  pts=[[78.5, 13.5], [79.3, 15], [80.5, 16.6], [82, 17.8], [83.3, 18.6], [84.5, 19.8]], r=0.6,
  c={"India": ["摩亨德拉山|Mahendra|摩亨德拉山"]})          # 七大山系的摩亨德拉(羯陵伽一带)
U("vindhya", "M", "温迪亚山脉", "Vindhya Range", pts=[[75.5, 22.8], [77.3, 23.3], [79, 23.8], [80.8, 24.3], [82.5, 24.6]], r=0.6)
U("satpura", "M", "萨特普拉山脉", "Satpura Range", pts=[[74.5, 21.6], [76.3, 21.7], [78, 22.1], [80, 22.5], [81.5, 22.6]], r=0.6)
U("aravalli", "M", "阿拉瓦利山脉", "Aravalli Range", pts=[[72.8, 24.6], [73.8, 25.6], [74.6, 26.5], [75.5, 27.3], [76.5, 28.1]], r=0.6,
  c={"India": ["帕里亚特拉山|Pariyatra|帕里亞特拉山"]})     # 七大山系的帕里亚特拉
U("chota_nagpur", "MJ", "乔塔那格浦尔高原", "Chota Nagpur Plateau", box=[[83, 22.3, 87, 24.3]])
U("sundarbans", "SJ", "孙德尔本斯", "Sundarbans", box=[[88, 21.5, 90, 22.6]])
U("sri_lanka_highlands", "MJ", "斯里兰卡中央高地", "Central Highlands of Sri Lanka", pts=[[80.8, 7.0], [81.1, 7.5]], r=0.8)

# 繁体：zhconv 转错的(咸 → 鹹、巴尔干的"幹"被 add_traditional 的通用修正改成"干")；
# 音译的"里"不是"裏"，群岛、湿热、塔尔巴哈台照台湾通行写法
TW_FIX = {"不咸山": "不咸山", "巴尔干山脉": "巴爾幹山脈"}
TW_CHARS = {"裏": "里", "羣": "群", "溼": "濕", "臺": "台"}

# 史实归属：时代 → {单元: 政权}
OWNERS = {
    # 前 260 年：长平之战那一年。秦已有巴蜀汉中(前 316)、灭义渠置北地(前 272)；赵守吕梁；燕在燕山；楚据淮汉、大别；
    # 月氏居敦煌祁连间(《史记·大宛列传》)；东胡在西拉木伦一带的草原(大兴安岭北段是林中诸部，不写)；塞种在七河；塞琉古仍有伊朗高原(帕提亚前 247 才起)；
    # 托勒密埃及有西奈、东西两漠；库施(麦罗埃)据布塔纳、拜尤达；亚美尼亚(耶烈万德)、本都已立；罗马已统一亚平宁半岛；
    # 孔雀王朝(阿育王，前 261 刚征服羯陵伽)；示巴据也门高地
    "warring_states": {
        "qinling": "qin", "daba": "qin", "min_shan": "qin", "liupan": "qin", "luliang": "zhao", "yanshan": "yan",
        "dabie": "chu", "qilian": "yuezhi", "gobi": "xiongnu", "khangai": "xiongnu",
        "hunshandake": "donghu", "horqin": "donghu", "dzungarian_alatau": "saka",
        "dasht_e_kavir": "seleucid", "dasht_e_lut": "seleucid", "zagros": "seleucid", "alborz": "seleucid", "kopet_dag": "seleucid",
        "sinai": "ptolemy", "eastern_desert": "ptolemy", "libyan_desert": "ptolemy", "qattara": "ptolemy",
        "butana": "kush", "bayuda": "kush", "middle_atlas": "mauretania", "aures": "numidia",
        "armenian_highlands": "armenia", "pontic": "pontus", "apennines": "rome",
        "thar": "maurya", "vindhya": "maurya", "satpura": "maurya", "aravalli": "maurya", "eastern_ghats": "maurya",
        "yemen_highlands": "saba",
    },
    # 117 年：图拉真末年，帝国最大(亚美尼亚、美索不达米亚刚设行省)；帕提亚有伊朗高原；贵霜跨兴都库什；康居在锡尔河以北；
    # 乌孙在伊犁七河；汉有河西(西域都护 107 年已罢，不写塔里木)，羌乱中北地、安定、上郡内迁(不写河套、六盘)；
    # 鲜卑在北匈奴西迁后据漠北(大鲜卑山为其故地)；高句丽据盖马大山；喀里多尼亚据苏格兰高地；希木叶尔据也门高地
    "roman_empire": {
        "alps": "rome", "pyrenees": "rome", "apennines": "rome", "cantabrian": "rome", "sistema_central": "rome",
        "sierra_nevada_es": "rome", "massif_central": "rome", "balkan_mountains": "rome", "rhodope": "rome", "pindus": "rome",
        "dinaric": "rome", "aures": "rome", "tell_atlas": "rome", "libyan_desert": "rome", "eastern_desert": "rome",
        "qattara": "rome", "sinai": "rome", "sinai_mountains": "rome", "negev": "rome", "judaean_desert": "rome",
        "taurus": "rome", "pontic": "rome", "armenian_highlands": "rome",
        "scottish_highlands": "caledonia",
        "zagros": "parthia", "alborz": "parthia", "kopet_dag": "parthia", "dasht_e_kavir": "parthia", "dasht_e_lut": "parthia",
        "talysh": "parthia",
        "hindu_kush": "kushan", "koh_i_baba": "kushan", "spin_ghar": "kushan", "kyzylkum": "kangju", "dzungarian_alatau": "wusun",
        "qinling": "han", "daba": "han", "taihang": "han", "luliang": "han", "yanshan": "han", "dabie": "han", "wuyi": "han",
        "nanling": "han", "min_shan": "han", "qionglai": "han", "qilian": "han", "ailao": "han",
        "gobi": "xianbei", "khangai": "xianbei", "khentii": "xianbei", "greater_khingan": "xianbei", "horqin": "xianbei",
        "kaema": "goguryeo", "butana": "kush", "bayuda": "kush", "simien": "aksum", "yemen_highlands": "himyar",
    },
    # 230 年：诸葛亮北伐之间(秦岭为魏蜀之界，不写)；蜀有汉中巴蜀、229 年取武都阴平；魏有河西、关中、河北；吴有江南岭南；
    # 鲜卑(轲比能)据漠南漠北；萨珊阿尔达希尔已有伊朗高原；亚美尼亚(安息系)仍在；罗马、贵霜、康居、乌孙同前
    "three_kingdoms": {
        "alps": "rome", "pyrenees": "rome", "apennines": "rome", "cantabrian": "rome", "sistema_central": "rome",
        "sierra_nevada_es": "rome", "massif_central": "rome", "balkan_mountains": "rome", "rhodope": "rome", "pindus": "rome",
        "dinaric": "rome", "aures": "rome", "tell_atlas": "rome", "libyan_desert": "rome", "eastern_desert": "rome",
        "qattara": "rome", "sinai": "rome", "sinai_mountains": "rome", "negev": "rome", "judaean_desert": "rome",
        "taurus": "rome", "pontic": "rome",
        "armenian_highlands": "armenia", "scottish_highlands": "caledonia",
        "zagros": "sasan", "alborz": "sasan", "dasht_e_kavir": "sasan", "dasht_e_lut": "sasan", "talysh": "sasan",
        "hindu_kush": "kushan", "koh_i_baba": "kushan", "spin_ghar": "kushan", "kyzylkum": "kangju", "dzungarian_alatau": "wusun",
        "daba": "shu", "min_shan": "shu", "qionglai": "shu", "ailao": "shu",
        "taihang": "wei", "luliang": "wei", "yanshan": "wei", "liupan": "wei", "qilian": "wei",
        "wuyi": "wu", "nanling": "wu",
        "gobi": "xianbei", "khangai": "xianbei", "khentii": "xianbei", "greater_khingan": "xianbei", "horqin": "xianbei",
        "hunshandake": "xianbei",
        "kaema": "goguryeo", "butana": "kush", "bayuda": "kush", "simien": "aksum",
    },
    # 750 年：天宝末。吐蕃据整个高原(663 年并吐谷浑，有柴达木、巴颜喀拉)，喀喇昆仑大小勃律分属唐蕃(不写)；
    # 唐有安西北庭(葱岭守捉、四镇)、河西、灵夏、单于府；回纥(744 年立国)据漠北；契丹在西拉木伦；渤海据太白山；
    # 新罗统一半岛；奈良朝日本(奥羽为虾夷之地，不写)；阿拔斯刚取代倭马亚，有伊朗、两河、叙利亚、埃及、汉志、也门、亚美尼亚；
    # 拜占庭据本都(托罗斯为两国边疆，不写)；阿斯图里亚斯、安达卢斯、阿基坦、皮克特、马库里亚、阿克苏姆、迦湿弥罗、真腊
    "high_tang": {
        "tibetan_plateau": "tibet", "changtang": "tibet", "hoh_xil": "tibet", "tanggula": "tibet", "nyainqentanglha": "tibet",
        "gangdise": "tibet", "bayan_har": "tibet", "amne_machin": "tibet", "qaidam": "tibet", "himalaya": "tibet", "kunlun": "tibet",
        "pamir": "tang", "tianshan": "tang", "bogda": "tang", "karlik": "tang", "taklamakan": "tang", "lop_desert": "tang",
        "kumtag": "tang", "gashun_gobi": "tang", "gurbantunggut": "tang", "qilian": "tang", "badain_jaran": "tang",
        "tengger": "tang", "ulan_buh": "tang", "kubuqi": "tang", "mu_us": "tang", "helan": "tang", "yinshan": "tang",
        "qinling": "tang", "daba": "tang", "taihang": "tang", "luliang": "tang", "yanshan": "tang", "liupan": "tang",
        "dabie": "tang", "wuyi": "tang", "nanling": "tang", "min_shan": "tang", "qionglai": "tang",
        "ailao": "nanzhao", "wuliang": "nanzhao", "gaoligong": "nanzhao",
        "gobi": "uyghur", "khangai": "uyghur", "khentii": "uyghur", "gobi_altai": "uyghur",
        "horqin": "khitan", "changbai": "balhae", "kaema": "balhae", "taebaek": "silla",
        "hida": "japan", "kiso": "japan", "akaishi": "japan",
        "syrian_desert": "abbasid", "dasht_e_kavir": "abbasid", "dasht_e_lut": "abbasid", "zagros": "abbasid",
        "kopet_dag": "abbasid", "karakum": "abbasid", "talysh": "abbasid", "armenian_highlands": "abbasid",
        "lesser_caucasus": "abbasid", "hakkari": "abbasid", "sarawat": "abbasid", "yemen_highlands": "abbasid",
        "sinai": "abbasid", "sinai_mountains": "abbasid", "negev": "abbasid", "judaean_desert": "abbasid",
        "eastern_desert": "abbasid", "libyan_desert": "abbasid", "qattara": "abbasid", "mesopotamian_marshes": "abbasid",
        "pontic": "byzantium", "cantabrian": "asturias", "sistema_central": "andalus", "sierra_nevada_es": "andalus",
        "massif_central": "aquitaine", "scottish_highlands": "picts", "bayuda": "makuria", "simien": "aksum",
        "pir_panjal": "kashmir", "cardamom": "chenla",
    },
    # 800 年：吐蕃极盛，有河西陇右(764~786 陆续陷落)、沙州、伊州，塔里木与回纥相争(不写)；回纥据北庭、漠北；
    # 葛逻禄在七河；唐仍有关内河东、灵夏丰胜(鄂尔多斯)；卢龙据幽州燕山；南诏据洱海哀牢；查理曼据阿尔卑斯两侧(774 年并伦巴第、788 年并巴伐利亚)；
    # 伊德里斯据中阿特拉斯北麓；科尔多瓦后倭马亚；其余同 750 年
    "charlemagne": {
        "tibetan_plateau": "tibet", "changtang": "tibet", "hoh_xil": "tibet", "tanggula": "tibet", "nyainqentanglha": "tibet",
        "gangdise": "tibet", "bayan_har": "tibet", "amne_machin": "tibet", "qaidam": "tibet", "himalaya": "tibet", "kunlun": "tibet",
        "altun": "tibet", "qilian": "tibet", "lop_desert": "tibet", "kumtag": "tibet", "gashun_gobi": "tibet",
        "bogda": "uyghur", "gurbantunggut": "uyghur", "gobi": "uyghur", "khangai": "uyghur", "khentii": "uyghur",
        "gobi_altai": "uyghur", "dzungarian_alatau": "karluk",
        "kubuqi": "tang", "mu_us": "tang", "helan": "tang", "qinling": "tang", "daba": "tang", "luliang": "tang",
        "dabie": "tang", "wuyi": "tang", "nanling": "tang", "yanshan": "lulong",
        "ailao": "nanzhao", "wuliang": "nanzhao", "gaoligong": "nanzhao",
        "horqin": "khitan", "changbai": "balhae", "kaema": "balhae", "taebaek": "silla",
        "hida": "japan", "kiso": "japan", "akaishi": "japan",
        "syrian_desert": "abbasid", "dasht_e_kavir": "abbasid", "dasht_e_lut": "abbasid", "zagros": "abbasid",
        "kopet_dag": "abbasid", "karakum": "abbasid", "talysh": "abbasid", "armenian_highlands": "abbasid",
        "lesser_caucasus": "abbasid", "hakkari": "abbasid", "sarawat": "abbasid", "yemen_highlands": "abbasid",
        "sinai": "abbasid", "sinai_mountains": "abbasid", "negev": "abbasid", "judaean_desert": "abbasid",
        "eastern_desert": "abbasid", "libyan_desert": "abbasid", "qattara": "abbasid", "mesopotamian_marshes": "abbasid",
        "safid_kuh": "abbasid",
        "pontic": "byzantium", "alps": "francia", "massif_central": "francia", "cantabrian": "asturias",
        "sistema_central": "cordoba", "sierra_nevada_es": "cordoba", "middle_atlas": "idrisid",
        "scottish_highlands": "picts", "bayuda": "makuria", "simien": "aksum", "pir_panjal": "kashmir", "cardamom": "chenla",
    },
    # 1190 年：吐蕃分裂(高原不写)；西夏有河西、阿拉善、贺兰、鄂尔多斯南；高昌回鹘在别失八里；西辽宗主七河；
    # 乃蛮据阿尔泰；乞颜部在斡难怯绿连之源(不儿罕山)；金有河北河东、燕山、东北(1172 年封长白山神)；宋守淮南、川陕、闽粤；
    # 大理据滇西；高丽；日本(1189 年奥州藤原氏亡，奥羽归镰仓)；古尔据古尔山地(萨菲德山)、巴米扬、加兹尼；
    # 花剌子模据卡拉库姆两侧；格鲁吉亚(塔玛尔女王)据小高加索；阿尤布有叙利亚、埃及、西奈、也门(1174)；
    # 阿拔斯哈里发仍有巴格达与大沼泽；穆瓦希德据整个马格里布与安达卢斯南部；神罗有阿尔卑斯；第二保加利亚(1185)据巴尔干山；
    # 拜占庭有品都斯；苏格兰；乔汉(阿杰梅尔)据阿拉瓦利；犀那据孟加拉；波隆纳鲁瓦；吴哥；蒲甘；马库里亚
    "crusades": {
        "qilian": "xixia", "badain_jaran": "xixia", "tengger": "xixia", "ulan_buh": "xixia", "mu_us": "xixia", "helan": "xixia",
        "bogda": "qocho", "dzungarian_alatau": "qara_khitai", "altai": "naimans", "khentii": "khamag",
        "hunshandake": "jin", "horqin": "jin", "changbai": "jin", "lesser_khingan": "jin", "taihang": "jin", "luliang": "jin",
        "yanshan": "jin",
        "daba": "song", "qionglai": "song", "dabie": "song", "wuyi": "song", "nanling": "song",
        "ailao": "dali", "wuliang": "dali", "gaoligong": "dali", "taebaek": "goryeo",
        "hida": "japan", "kiso": "japan", "akaishi": "japan", "ou": "japan",
        "hindu_kush": "ghurid", "safid_kuh": "ghurid", "koh_i_baba": "ghurid", "karakum": "khwarazm",
        "lesser_caucasus": "georgia",
        "syrian_desert": "ayyubid", "sinai": "ayyubid", "sinai_mountains": "ayyubid", "negev": "ayyubid",
        "judaean_desert": "ayyubid", "eastern_desert": "ayyubid", "libyan_desert": "ayyubid", "qattara": "ayyubid",
        "yemen_highlands": "ayyubid", "mesopotamian_marshes": "abbasid",
        "atlas": "almohad", "high_atlas": "almohad", "middle_atlas": "almohad", "saharan_atlas": "almohad", "aures": "almohad",
        "tell_atlas": "almohad", "sierra_nevada_es": "almohad",
        "alps": "hre", "balkan_mountains": "bulgaria", "pindus": "byzantium", "scottish_highlands": "scotland",
        "aravalli": "chahamana", "sundarbans": "sena", "sri_lanka_highlands": "polonnaruwa", "cardamom": "khmer",
        "bago_yoma": "pagan", "bayuda": "makuria",
    },
    # 1628 年：藏巴汗据卫藏(1642 年才亡于固始汗)；叶尔羌汗国据塔里木；喀尔喀据杭爱肯特；察哈尔(林丹汗)在锡林郭勒；
    # 后金据建州故地长白山；朝鲜；明有两京十三省(河套、阿拉善为蒙古诸部，不写)；日本；柬埔寨；东吁；
    # 莫卧儿有拉贾斯坦、马尔瓦、克什米尔(1586)、信德、孟加拉；康提据中部山地；萨法维有伊朗高原；
    # 奥斯曼有安纳托利亚、叙利亚、埃及、汉志、巴尔干、阿尔及尔与突尼斯(也门 1597 年起扎伊德派叛乱，不写)；阿曼(亚里巴 1624)；
    # 摩洛哥(萨阿德)据高、中阿特拉斯；芬吉苏丹国据布塔纳、拜尤达；埃塞俄比亚；西班牙、法兰西、英格兰(共主苏格兰)；
    # 丹麦-挪威有尤通黑门；俄罗斯已越乌拉尔、有西西伯利亚与科拉；波兰立陶宛有波列西耶
    "lingao": {
        "tibetan_plateau": "tsang", "nyainqentanglha": "tsang", "taklamakan": "yarkent",
        "khangai": "khalkha", "khentii": "khalkha", "gobi_altai": "khalkha", "hunshandake": "chahar",
        "changbai": "later_jin", "kaema": "joseon", "taebaek": "joseon",
        "qinling": "ming", "daba": "ming", "taihang": "ming", "luliang": "ming", "yanshan": "ming", "liupan": "ming",
        "dabie": "ming", "wuyi": "ming", "nanling": "ming", "min_shan": "ming", "qionglai": "ming", "ailao": "ming",
        "wuliang": "ming", "gaoligong": "ming",
        "hida": "japan", "kiso": "japan", "akaishi": "japan", "ou": "japan",
        "cardamom": "cambodia", "bago_yoma": "toungoo",
        "thar": "mughal", "aravalli": "mughal", "vindhya": "mughal", "satpura": "mughal", "pir_panjal": "mughal",
        "sundarbans": "mughal", "kirthar": "mughal", "sri_lanka_highlands": "kandy",
        "zagros": "persia", "alborz": "persia", "dasht_e_kavir": "persia", "dasht_e_lut": "persia", "talysh": "persia",
        "pontic": "ottoman", "taurus": "ottoman", "syrian_desert": "ottoman", "sinai": "ottoman", "sinai_mountains": "ottoman",
        "negev": "ottoman", "judaean_desert": "ottoman", "eastern_desert": "ottoman", "libyan_desert": "ottoman",
        "qattara": "ottoman", "balkan_mountains": "ottoman", "rhodope": "ottoman", "pindus": "ottoman", "aures": "ottoman",
        "tell_atlas": "ottoman", "sarawat": "ottoman",
        "hajar": "oman", "high_atlas": "morocco", "middle_atlas": "morocco", "butana": "funj", "bayuda": "funj",
        "ethiopian_highlands": "ethiopia", "simien": "ethiopia",
        "cantabrian": "spain", "sistema_central": "spain", "sierra_nevada_es": "spain", "massif_central": "france",
        "scottish_highlands": "england", "jotunheimen": "denmark",
        "kola": "russia", "khibiny": "russia", "ural": "russia", "timan": "russia", "west_siberian": "russia",
        "yamal": "russia", "gydan": "russia", "pripet": "poland",
    },
}


def check_entry(entry, what):
    parts = entry.split("|")
    if len(parts) not in (3, 4):
        raise SystemExit("%s：写法不对 %s" % (what, entry))
    if len(parts) == 4:
        spec = parts[0]
        for piece in spec.split("~"):
            if piece and not piece.lstrip("-").isdigit():
                raise SystemExit("%s：年份不对 %s" % (what, entry))


def main():
    root = sys.argv[1]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from add_traditional import dump_records, tw  # noqa: E402

    def tw_name(text):
        if text in TW_FIX:
            return TW_FIX[text]
        result = tw(text)
        for wrong, right in TW_CHARS.items():
            result = result.replace(wrong, right)
        return result

    ids = set()
    for unit in UNITS:
        if unit["id"] in ids:
            raise SystemExit("单元 id 重复：" + unit["id"])
        ids.add(unit["id"])
        if not unit.get("box") and not unit.get("pts"):
            raise SystemExit(unit["id"] + " 没有定位")
        unit["tw"] = tw_name(unit["zh"])
        for entry in unit.get("alt", []):
            check_entry(entry, unit["id"])
        for culture, entries in unit.get("c", {}).items():
            for entry in entries:
                check_entry(entry, unit["id"] + "/" + culture)
    records = []
    for unit in UNITS:
        ordered = {key: unit[key] for key in ("id", "k", "zh", "en", "tw", "box", "pts", "r", "alt", "c") if key in unit}
        records.append(ordered)
    out = os.path.join(root, "WorldMaps", "wilderness.json")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('  "_note": %s,\n' % json.dumps(
            "世界地图无人区的真实地名(由 Tools/WorldMap/wilderness_source.py 生成，说明见该脚本)：k 类型，box 经纬框[西,南,东,北]，"
            "pts 锚点[经,纬]与半径 r(度)，alt 按年份的旧称，c 按文化的叫法；起名先按文化叫法，再按开局年份的旧称，最后用现代名",
            ensure_ascii=False))
        dump_records(f, "units", records, True)
        f.write("}\n")
    print("wilderness.json", len(records), "个单元")

    for era_id, owners in OWNERS.items():
        path = os.path.join(root, "WorldMaps", "eras", era_id + ".json")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        polities = {p["id"] for p in data["polities"]}
        for unit_id, polity in owners.items():
            if unit_id not in ids:
                raise SystemExit("%s：没有单元 %s" % (era_id, unit_id))
            if polity not in polities:
                raise SystemExit("%s：%s 归的 %s 不在本档" % (era_id, unit_id, polity))
        data["wilderness"] = owners
        head = {k: v for k, v in data.items() if k not in ("cores", "polities")}
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("{\n")
            for key, value in head.items():
                f.write('  "%s": %s,\n' % (key, json.dumps(value, ensure_ascii=False)))
            dump_records(f, "cores", data.get("cores", []), False)
            dump_records(f, "polities", data.get("polities", []), True)
            f.write("}\n")
        print(era_id, len(owners), "个单元写了归属")


if __name__ == "__main__":
    main()
