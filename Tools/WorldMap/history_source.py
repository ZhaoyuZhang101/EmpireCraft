"""世界地图的前史数据源：生成 WorldMaps/history.json(再跑 add_traditional.py 补繁体)。

每个朝代：
  id、zh|en 名、c 文化、seat 都城所在地区(已亡朝代的史书挂到这些地区所属的帝国法理上)、fam 皇族姓(华夏为姓，西方为家族名)、
  west 为真时姓不拼在名前(西方人名本身就是全名)；
  rulers 历代君主：n 姓名、a/b 在位起止年(公元前为负)、shi 谥号一字或几字(拼成"汉武帝")、miao 庙号(拼成"汉世祖")、
         era 首个年号、t 称号(周天子、西方君主的号，不拼谥号时显示)、p 族谱编号、f 父亲的族谱编号、dyn 本朝内的家系名(罗马各王朝)；
  people 不在位的祖先(族谱里连接父子)：p、n、f。
开局时：本朝(某国 "dynasty" 指向的朝代)开局年以前的君主写进该国帝国史书，已经亡了的朝代做成已亡帝国档案，
族谱编号连成皇族世系，今上的父亲写在时代文件里该国 ruler 的 "f"。

用法：python -I -X utf8 history_source.py <模组目录>
"""
import json
import os
import sys

DYNASTIES = []


# 已亡朝代名录里的史家通称(北周、武周、后周……)；史书拼谥号仍用朝代名("周"武帝)
FULL = {"cao_wei": "曹魏|Cao Wei", "shu_han": "蜀汉|Shu Han", "sun_wu": "东吴|Eastern Wu", "liu_song": "刘宋|Liu Song",
        "nan_qi": "南齐|Southern Qi", "liang": "南梁|Southern Liang", "chen": "南陈|Southern Chen", "bei_wei": "北魏|Northern Wei",
        "bei_qi": "北齐|Northern Qi", "bei_zhou": "北周|Northern Zhou", "wu_zhou": "武周|Wu Zhou", "hou_liang": "后梁|Later Liang",
        "hou_tang": "后唐|Later Tang", "hou_jin": "后晋|Later Jin", "hou_han": "后汉|Later Han", "hou_zhou": "后周|Later Zhou",
        "xixia": "西夏|Western Xia", "byzantium": "东罗马|Eastern Rome", "hre": "神圣罗马|Holy Roman Empire",
        "qin_state": "秦国|State of Qin", "safavid": "萨法维|Safavid", "delhi": "德里苏丹国|Delhi Sultanate",
        "mamluk": "马穆鲁克苏丹国|Mamluk Sultanate", "tsardom": "俄罗斯沙皇国|Tsardom of Russia",
        "aisin_gioro": "后金|Later Jin", "ottoman": "奥斯曼帝国|Ottoman Empire", "mughal": "莫卧儿帝国|Mughal Empire",
        "timurid": "帖木儿帝国|Timurid Empire"}


def D(id, name, c, seat, fam, west=False):
    d = {"id": id, "zh": name.split("|")[0], "en": name.split("|")[1], "c": c, "seat": seat, "fam": fam,
         "west": west, "rulers": [], "people": []}
    if id in FULL:
        d["full"] = FULL[id]
    DYNASTIES.append(d)
    return d


def R(d, n, a, b, shi="", miao="", era="", t="", p="", f="", dyn=""):
    r = {"n": n, "a": a, "b": b}
    for k, v in (("shi", shi), ("miao", miao), ("era", era), ("t", t), ("p", p), ("f", f), ("dyn", dyn)):
        if v:
            r[k] = v
    d["rulers"].append(r)


def P(d, p, n, f="", t=""):
    e = {"p": p, "n": n}
    if f:
        e["f"] = f
    if t:
        e["t"] = t
    d["people"].append(e)


# ---------------- 华夏 ----------------
z = D("zhou", "周|Zhou", "Huaxia", ["henan", "guanzhong"], "姬|Ji")
P(z, "zhou_wen", "姬昌|Ji Chang")
for n, a, b, t, p, f in [
    ("姬发|Ji Fa", -1046, -1043, "周武王|King Wu", "zw01", "zhou_wen"),
    ("姬诵|Ji Song", -1042, -1021, "周成王|King Cheng", "zw02", "zw01"),
    ("姬钊|Ji Zhao", -1020, -996, "周康王|King Kang", "zw03", "zw02"),
    ("姬瑕|Ji Xia", -995, -977, "周昭王|King Zhao", "zw04", "zw03"),
    ("姬满|Ji Man", -976, -922, "周穆王|King Mu", "zw05", "zw04"),
    ("姬繄扈|Ji Yihu", -922, -900, "周共王|King Gong", "zw06", "zw05"),
    ("姬囏|Ji Jian", -899, -892, "周懿王|King Yi", "zw07", "zw06"),
    ("姬辟方|Ji Pifang", -891, -886, "周孝王|King Xiao", "zw08", "zw05"),
    ("姬燮|Ji Xie", -885, -878, "周夷王|King Yi", "zw09", "zw07"),
    ("姬胡|Ji Hu", -877, -841, "周厉王|King Li", "zw10", "zw09"),
    ("姬静|Ji Jing", -827, -782, "周宣王|King Xuan", "zw11", "zw10"),
    ("姬宫湦|Ji Gongsheng", -781, -771, "周幽王|King You", "zw12", "zw11"),
    ("姬宜臼|Ji Yijiu", -770, -720, "周平王|King Ping", "zw13", "zw12"),
    ("姬林|Ji Lin", -719, -697, "周桓王|King Huan", "zw14", "zhou_xiefu"),
    ("姬佗|Ji Tuo", -696, -682, "周庄王|King Zhuang", "zw15", "zw14"),
    ("姬胡齐|Ji Huqi", -681, -677, "周釐王|King Xi", "zw16", "zw15"),
    ("姬阆|Ji Lang", -676, -652, "周惠王|King Hui", "zw17", "zw16"),
    ("姬郑|Ji Zheng", -651, -619, "周襄王|King Xiang", "zw18", "zw17"),
    ("姬壬臣|Ji Renchen", -618, -613, "周顷王|King Qing", "zw19", "zw18"),
    ("姬班|Ji Ban", -612, -607, "周匡王|King Kuang", "zw20", "zw19"),
    ("姬瑜|Ji Yu", -606, -586, "周定王|King Ding", "zw21", "zw19"),
    ("姬夷|Ji Yi", -585, -572, "周简王|King Jian", "zw22", "zw21"),
    ("姬泄心|Ji Xiexin", -571, -545, "周灵王|King Ling", "zw23", "zw22"),
    ("姬贵|Ji Gui", -544, -520, "周景王|King Jing", "zw24", "zw23"),
    ("姬匄|Ji Gai", -519, -476, "周敬王|King Jing", "zw25", "zw24"),
    ("姬仁|Ji Ren", -475, -469, "周元王|King Yuan", "zw26", "zw25"),
    ("姬介|Ji Jie", -468, -441, "周贞定王|King Zhending", "zw27", "zw26"),
    ("姬嵬|Ji Wei", -440, -426, "周考王|King Kao", "zw28", "zw27"),
    ("姬午|Ji Wu", -425, -402, "周威烈王|King Weilie", "zw29", "zw28"),
    ("姬骄|Ji Jiao", -401, -376, "周安王|King An", "zw30", "zw29"),
    ("姬喜|Ji Xi", -375, -369, "周烈王|King Lie", "zw31", "zw30"),
    ("姬扁|Ji Bian", -368, -321, "周显王|King Xian", "zw32", "zw30"),
    ("姬定|Ji Ding", -320, -315, "周慎靓王|King Shenjing", "zw33", "zw32"),
    ("姬延|Ji Yan", -314, -256, "周赧王|King Nan", "zw34", "zw33"),
]:
    R(z, n, a, b, t=t, p=p, f=f)
P(z, "zhou_xiefu", "姬泄父|Ji Xiefu", "zw13")

q = D("qin", "秦|Qin", "Huaxia", ["guanzhong"], "嬴|Ying")
P(q, "qin_xiao", "嬴渠梁|Ying Quliang")
P(q, "qin_huiwen", "嬴驷|Ying Si", "qin_xiao")
P(q, "qin_zhaoxiang", "嬴稷|Ying Ji", "qin_huiwen")
P(q, "qin_xiaowen", "嬴柱|Ying Zhu", "qin_zhaoxiang")
P(q, "qin_zhuangxiang", "嬴异人|Ying Yiren", "qin_xiaowen")
R(q, "嬴政|Ying Zheng", -221, -210, shi="始皇", p="qin_shihuang", f="qin_zhuangxiang")
R(q, "嬴胡亥|Ying Huhai", -210, -207, shi="二世皇", p="qin_ershi", f="qin_shihuang")
R(q, "嬴子婴|Ying Ziying", -207, -207, t="秦王|King of Qin", p="qin_ziying")

h = D("han", "汉|Han", "Huaxia", ["guanzhong", "henan"], "刘|Liu")
P(h, "han_taigong", "刘太公|Liu Taigong")
for n, a, b, shi, miao, era, p, f in [
    ("刘邦|Liu Bang", -202, -195, "高", "太祖", "", "hl01", "han_taigong"),
    ("刘盈|Liu Ying", -195, -188, "惠", "", "", "hl02", "hl01"),
    ("刘恭|Liu Gong", -188, -184, "前少", "", "", "hl03", "hl02"),
    ("刘弘|Liu Hong", -184, -180, "后少", "", "", "hl04", "hl02"),
    ("刘恒|Liu Heng", -180, -157, "文", "太宗", "", "hl05", "hl01"),
    ("刘启|Liu Qi", -157, -141, "景", "", "", "hl06", "hl05"),
    ("刘彻|Liu Che", -141, -87, "武", "世宗", "建元", "hl07", "hl06"),
    ("刘弗陵|Liu Fuling", -87, -74, "昭", "", "始元", "hl08", "hl07"),
    ("刘询|Liu Xun", -74, -48, "宣", "中宗", "本始", "hl09", "han_jin"),
    ("刘奭|Liu Shi", -48, -33, "元", "高宗", "初元", "hl10", "hl09"),
    ("刘骜|Liu Ao", -33, -7, "成", "统宗", "建始", "hl11", "hl10"),
    ("刘欣|Liu Xin", -7, -1, "哀", "", "建平", "hl12", "han_kang"),
    ("刘衎|Liu Kan", -1, 5, "平", "元宗", "元始", "hl13", "han_xing"),
    ("刘婴|Liu Ying", 6, 8, "", "", "居摄", "hl14", "han_xian"),
    ("刘秀|Liu Xiu", 25, 57, "光武", "世祖", "建武", "hl15", "han_qin"),
    ("刘庄|Liu Zhuang", 57, 75, "明", "显宗", "永平", "hl16", "hl15"),
    ("刘炟|Liu Da", 75, 88, "章", "肃宗", "建初", "hl17", "hl16"),
    ("刘肇|Liu Zhao", 88, 105, "和", "穆宗", "永元", "hl18", "hl17"),
    ("刘隆|Liu Long", 105, 106, "殇", "", "延平", "hl19", "hl18"),
    ("刘祜|Liu Hu", 106, 125, "安", "恭宗", "永初", "hl20", "han_qing"),
    ("刘懿|Liu Yi", 125, 125, "少", "", "", "hl21", "han_shou"),
    ("刘保|Liu Bao", 125, 144, "顺", "敬宗", "永建", "hl22", "hl20"),
    ("刘炳|Liu Bing", 144, 145, "冲", "", "永嘉", "hl23", "hl22"),
    ("刘缵|Liu Zuan", 145, 146, "质", "", "本初", "hl24", "han_hong2"),
    ("刘志|Liu Zhi", 146, 167, "桓", "威宗", "建和", "hl25", "han_yi2"),
    ("刘宏|Liu Hong", 168, 189, "灵", "", "建宁", "hl26", "han_chang"),
    ("刘辩|Liu Bian", 189, 189, "少", "", "光熹", "hl27", "hl26"),
    ("刘协|Liu Xie", 189, 220, "献", "", "初平", "hl28", "hl26"),
]:
    R(h, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f)
for p, n, f in [
    ("han_ju", "刘据|Liu Ju", "hl07"), ("han_jin", "刘进|Liu Jin", "han_ju"),
    ("han_kang", "刘康|Liu Kang", "hl10"), ("han_xing", "刘兴|Liu Xing", "hl10"),
    ("han_xiao", "刘嚣|Liu Xiao", "hl09"), ("han_xun", "刘勋|Liu Xun", "han_xiao"), ("han_xian", "刘显|Liu Xian", "han_xun"),
    ("han_fa", "刘发|Liu Fa", "hl06"), ("han_mai", "刘买|Liu Mai", "han_fa"), ("han_wai", "刘外|Liu Wai", "han_mai"),
    ("han_hui", "刘回|Liu Hui", "han_wai"), ("han_qin", "刘钦|Liu Qin", "han_hui"),
    ("han_qing", "刘庆|Liu Qing", "hl17"), ("han_shou", "刘寿|Liu Shou", "hl17"),
    ("han_kang2", "刘伉|Liu Kang", "hl17"), ("han_chong", "刘宠|Liu Chong", "han_kang2"), ("han_hong2", "刘鸿|Liu Hong", "han_chong"),
    ("han_kai", "刘开|Liu Kai", "hl17"), ("han_yi2", "刘翼|Liu Yi", "han_kai"),
    ("han_shu", "刘淑|Liu Shu", "han_kai"), ("han_chang", "刘苌|Liu Chang", "han_shu"),
    ("han_sheng", "刘胜|Liu Sheng", "hl06"),
]:
    P(h, p, n, f)

x = D("xin", "新|Xin", "Huaxia", ["guanzhong"], "王|Wang")
R(x, "王莽|Wang Mang", 9, 23, era="始建国", t="新帝|Emperor of Xin", p="xin_wangmang")

cw = D("cao_wei", "魏|Wei", "Huaxia", ["henan"], "曹|Cao")
P(cw, "cw_song", "曹嵩|Cao Song")
P(cw, "cw_cao", "曹操|Cao Cao", "cw_song")
for n, a, b, shi, miao, era, p, f in [
    ("曹丕|Cao Pi", 220, 226, "文", "高祖", "黄初", "cw01", "cw_cao"),
    ("曹叡|Cao Rui", 226, 239, "明", "烈祖", "太和", "cw02", "cw01"),
    ("曹芳|Cao Fang", 239, 254, "", "", "正始", "cw03", "cw_kai"),
    ("曹髦|Cao Mao", 254, 260, "", "", "正元", "cw04", "cw_lin"),
    ("曹奂|Cao Huan", 260, 266, "元", "", "景元", "cw05", "cw_yu"),
]:
    R(cw, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f)
P(cw, "cw_kai", "曹楷|Cao Kai", "cw_cao")
P(cw, "cw_lin", "曹霖|Cao Lin", "cw01")
P(cw, "cw_yu", "曹宇|Cao Yu", "cw_cao")

sh = D("shu_han", "汉|Shu Han", "Huaxia", ["bashu"], "刘|Liu")
P(sh, "sh_hong", "刘弘|Liu Hong")
R(sh, "刘备|Liu Bei", 221, 223, shi="昭烈", miao="烈祖", era="章武", p="sh01", f="sh_hong")
R(sh, "刘禅|Liu Shan", 223, 263, shi="孝怀", era="建兴", p="sh02", f="sh01")

sw = D("sun_wu", "吴|Wu", "Huaxia", ["jiangdong"], "孙|Sun")
P(sw, "sw_jian", "孙坚|Sun Jian")
P(sw, "sw_ce", "孙策|Sun Ce", "sw_jian")
R(sw, "孙权|Sun Quan", 229, 252, shi="大", miao="太祖", era="黄龙", p="sw01", f="sw_jian")
R(sw, "孙亮|Sun Liang", 252, 258, era="建兴", p="sw02", f="sw01")
R(sw, "孙休|Sun Xiu", 258, 264, shi="景", era="永安", p="sw03", f="sw01")
R(sw, "孙皓|Sun Hao", 264, 280, era="元兴", p="sw04", f="sw_he")
P(sw, "sw_he", "孙和|Sun He", "sw01")

j = D("jin", "晋|Jin", "Huaxia", ["henan", "jiangdong"], "司马|Sima")
P(j, "jin_yi", "司马懿|Sima Yi")
P(j, "jin_zhao", "司马昭|Sima Zhao", "jin_yi")
for n, a, b, shi, miao, era, p, f in [
    ("司马炎|Sima Yan", 266, 290, "武", "世祖", "泰始", "jn01", "jin_zhao"),
    ("司马衷|Sima Zhong", 290, 307, "惠", "", "永熙", "jn02", "jn01"),
    ("司马炽|Sima Chi", 307, 311, "怀", "", "永嘉", "jn03", "jn01"),
    ("司马邺|Sima Ye", 313, 316, "愍", "", "建兴", "jn04", "jin_yan"),
    ("司马睿|Sima Rui", 317, 323, "元", "中宗", "建武", "jn05", "jin_jin"),
    ("司马绍|Sima Shao", 323, 325, "明", "肃宗", "太宁", "jn06", "jn05"),
    ("司马衍|Sima Yan", 325, 342, "成", "显宗", "咸和", "jn07", "jn06"),
    ("司马岳|Sima Yue", 342, 344, "康", "", "建元", "jn08", "jn06"),
    ("司马聃|Sima Dan", 344, 361, "穆", "孝宗", "永和", "jn09", "jn08"),
    ("司马丕|Sima Pi", 361, 365, "哀", "", "隆和", "jn10", "jn07"),
    ("司马奕|Sima Yi", 365, 371, "废", "", "太和", "jn11", "jn07"),
    ("司马昱|Sima Yu", 371, 372, "简文", "太宗", "咸安", "jn12", "jn05"),
    ("司马曜|Sima Yao", 372, 396, "孝武", "烈宗", "宁康", "jn13", "jn12"),
    ("司马德宗|Sima Dezong", 396, 418, "安", "", "隆安", "jn14", "jn13"),
    ("司马德文|Sima Dewen", 418, 420, "恭", "", "元熙", "jn15", "jn13"),
]:
    R(j, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f)
P(j, "jin_yan", "司马晏|Sima Yan", "jn01")
P(j, "jin_zhou", "司马伷|Sima Zhou", "jin_yi")
P(j, "jin_jin", "司马觐|Sima Jin", "jin_zhou")

ls = D("liu_song", "宋|Song", "Huaxia", ["jiangdong"], "刘|Liu")
for n, a, b, shi, miao, era, p, f in [
    ("刘裕|Liu Yu", 420, 422, "武", "高祖", "永初", "ls01", ""),
    ("刘义符|Liu Yifu", 422, 424, "少", "", "景平", "ls02", "ls01"),
    ("刘义隆|Liu Yilong", 424, 453, "文", "太祖", "元嘉", "ls03", "ls01"),
    ("刘骏|Liu Jun", 453, 464, "孝武", "世祖", "孝建", "ls04", "ls03"),
    ("刘子业|Liu Ziye", 464, 465, "前废", "", "永光", "ls05", "ls04"),
    ("刘彧|Liu Yu", 465, 472, "明", "太宗", "泰始", "ls06", "ls03"),
    ("刘昱|Liu Yu", 472, 477, "后废", "", "元徽", "ls07", "ls06"),
    ("刘准|Liu Zhun", 477, 479, "顺", "", "昇明", "ls08", "ls06"),
]:
    R(ls, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f)

nq = D("nan_qi", "齐|Qi", "Huaxia", ["jiangdong"], "萧|Xiao")
for n, a, b, shi, miao, era in [
    ("萧道成|Xiao Daocheng", 479, 482, "高", "太祖", "建元"), ("萧赜|Xiao Ze", 482, 493, "武", "世祖", "永明"),
    ("萧昭业|Xiao Zhaoye", 493, 494, "", "", "隆昌"), ("萧鸾|Xiao Luan", 494, 498, "明", "高宗", "建武"),
    ("萧宝卷|Xiao Baojuan", 498, 501, "", "", "永元"), ("萧宝融|Xiao Baorong", 501, 502, "和", "", "中兴"),
]:
    R(nq, n, a, b, shi=shi, miao=miao, era=era)

nl = D("liang", "梁|Liang", "Huaxia", ["jiangdong"], "萧|Xiao")
for n, a, b, shi, miao, era in [
    ("萧衍|Xiao Yan", 502, 549, "武", "高祖", "天监"), ("萧纲|Xiao Gang", 549, 551, "简文", "太宗", "大宝"),
    ("萧绎|Xiao Yi", 552, 555, "元", "世祖", "承圣"), ("萧方智|Xiao Fangzhi", 555, 557, "敬", "", "绍泰"),
]:
    R(nl, n, a, b, shi=shi, miao=miao, era=era)

nc = D("chen", "陈|Chen", "Huaxia", ["jiangdong"], "陈|Chen")
for n, a, b, shi, miao, era in [
    ("陈霸先|Chen Baxian", 557, 559, "武", "高祖", "永定"), ("陈蒨|Chen Qian", 559, 566, "文", "世祖", "天嘉"),
    ("陈伯宗|Chen Bozong", 566, 568, "废", "", "光大"), ("陈顼|Chen Xu", 569, 582, "宣", "高宗", "太建"),
    ("陈叔宝|Chen Shubao", 582, 589, "", "", "至德"),
]:
    R(nc, n, a, b, shi=shi, miao=miao, era=era)

bw = D("bei_wei", "魏|Northern Wei", "Huaxia", ["hedong", "henan"], "拓跋|Tuoba")
for n, a, b, shi, miao, era in [
    ("拓跋珪|Tuoba Gui", 386, 409, "道武", "太祖", "登国"), ("拓跋嗣|Tuoba Si", 409, 423, "明元", "太宗", "永兴"),
    ("拓跋焘|Tuoba Tao", 423, 452, "太武", "世祖", "始光"), ("拓跋濬|Tuoba Jun", 452, 465, "文成", "高宗", "兴安"),
    ("拓跋弘|Tuoba Hong", 465, 471, "献文", "显祖", "天安"), ("元宏|Yuan Hong", 471, 499, "孝文", "高祖", "延兴"),
    ("元恪|Yuan Ke", 499, 515, "宣武", "世宗", "景明"), ("元诩|Yuan Xu", 515, 528, "孝明", "肃宗", "熙平"),
    ("元子攸|Yuan Ziyou", 528, 530, "孝庄", "敬宗", "建义"), ("元修|Yuan Xiu", 532, 534, "孝武", "", "太昌"),
]:
    R(bw, n, a, b, shi=shi, miao=miao, era=era)

bq = D("bei_qi", "齐|Northern Qi", "Huaxia", ["hebei"], "高|Gao")
for n, a, b, shi, miao, era in [
    ("高洋|Gao Yang", 550, 559, "文宣", "显祖", "天保"), ("高殷|Gao Yin", 559, 560, "废", "", "乾明"),
    ("高演|Gao Yan", 560, 561, "孝昭", "肃宗", "皇建"), ("高湛|Gao Zhan", 561, 565, "武成", "世祖", "太宁"),
    ("高纬|Gao Wei", 565, 577, "", "", "天统"),
]:
    R(bq, n, a, b, shi=shi, miao=miao, era=era)

bz = D("bei_zhou", "周|Northern Zhou", "Huaxia", ["guanzhong"], "宇文|Yuwen")
for n, a, b, shi, miao, era in [
    ("宇文觉|Yuwen Jue", 557, 557, "孝闵", "", ""), ("宇文毓|Yuwen Yu", 557, 560, "明", "世宗", "武成"),
    ("宇文邕|Yuwen Yong", 560, 578, "武", "高祖", "保定"), ("宇文赟|Yuwen Yun", 578, 579, "宣", "", "宣政"),
    ("宇文阐|Yuwen Chan", 579, 581, "静", "", "大象"),
]:
    R(bz, n, a, b, shi=shi, miao=miao, era=era)

su = D("sui", "隋|Sui", "Huaxia", ["guanzhong"], "杨|Yang")
R(su, "杨坚|Yang Jian", 581, 604, shi="文", miao="高祖", era="开皇", p="su01")
R(su, "杨广|Yang Guang", 604, 618, shi="炀", era="大业", p="su02", f="su01")
R(su, "杨侑|Yang You", 617, 618, shi="恭", era="义宁", p="su03", f="su_zhao")
P(su, "su_zhao", "杨昭|Yang Zhao", "su02")

wz = D("wu_zhou", "周|Wu Zhou", "Huaxia", ["henan"], "武|Wu")
R(wz, "武曌|Wu Zhao", 690, 705, t="圣神皇帝|Emperor Shengshen", era="天授")

t = D("tang", "唐|Tang", "Huaxia", ["guanzhong"], "李|Li")
P(t, "tang_bing", "李昞|Li Bing")
for n, a, b, shi, miao, era, p, f in [
    ("李渊|Li Yuan", 618, 626, "神尧大圣大光孝皇", "高祖", "武德", "tg01", "tang_bing"),
    ("李世民|Li Shimin", 626, 649, "文武大圣大广孝皇", "太宗", "贞观", "tg02", "tg01"),
    ("李治|Li Zhi", 649, 683, "天皇大圣大弘孝皇", "高宗", "永徽", "tg03", "tg02"),
    ("李显|Li Xian", 684, 684, "大和大圣大昭孝皇", "中宗", "嗣圣", "tg04", "tg03"),
    ("李旦|Li Dan", 684, 690, "玄真大圣大兴孝皇", "睿宗", "文明", "tg05", "tg03"),
    ("李显|Li Xian", 705, 710, "大和大圣大昭孝皇", "中宗", "神龙", "tg04", "tg03"),
    ("李重茂|Li Chongmao", 710, 710, "殇", "", "唐隆", "tg06", "tg04"),
    ("李旦|Li Dan", 710, 712, "玄真大圣大兴孝皇", "睿宗", "景云", "tg05", "tg03"),
    ("李隆基|Li Longji", 712, 756, "至道大圣大明孝皇", "玄宗", "先天", "tg07", "tg05"),
    ("李亨|Li Heng", 756, 762, "文明武德大圣大宣孝皇", "肃宗", "至德", "tg08", "tg07"),
    ("李豫|Li Yu", 762, 779, "睿文孝武皇", "代宗", "宝应", "tg09", "tg08"),
    ("李适|Li Kuo", 779, 805, "神武孝文皇", "德宗", "建中", "tg10", "tg09"),
    ("李诵|Li Song", 805, 805, "至德大圣大安孝皇", "顺宗", "永贞", "tg11", "tg10"),
    ("李纯|Li Chun", 805, 820, "圣神章武孝皇", "宪宗", "元和", "tg12", "tg11"),
    ("李恒|Li Heng", 820, 824, "睿圣文惠孝皇", "穆宗", "长庆", "tg13", "tg12"),
    ("李湛|Li Zhan", 824, 826, "睿武昭愍孝皇", "敬宗", "宝历", "tg14", "tg13"),
    ("李昂|Li Ang", 826, 840, "元圣昭献孝皇", "文宗", "太和", "tg15", "tg13"),
    ("李炎|Li Yan", 840, 846, "至道昭肃孝皇", "武宗", "会昌", "tg16", "tg13"),
    ("李忱|Li Chen", 846, 859, "圣武献文孝皇", "宣宗", "大中", "tg17", "tg12"),
    ("李漼|Li Cui", 859, 873, "昭圣恭惠孝皇", "懿宗", "咸通", "tg18", "tg17"),
    ("李儇|Li Xuan", 873, 888, "惠圣恭定孝皇", "僖宗", "乾符", "tg19", "tg18"),
    ("李晔|Li Ye", 888, 904, "圣穆景文孝皇", "昭宗", "龙纪", "tg20", "tg18"),
    ("李柷|Li Zhu", 904, 907, "哀", "", "天祐", "tg21", "tg20"),
]:
    R(t, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f)

for did, name, seat, fam, rulers in [
    ("hou_liang", "梁|Later Liang", ["henan"], "朱|Zhu",
     [("朱温|Zhu Wen", 907, 912, "神武元圣孝皇", "太祖", "开平"), ("朱友珪|Zhu Yougui", 912, 913, "", "", "凤历"),
      ("朱友贞|Zhu Youzhen", 913, 923, "", "", "乾化")]),
    ("hou_tang", "唐|Later Tang", ["henan"], "李|Li",
     [("李存勖|Li Cunxu", 923, 926, "光圣神闵孝皇", "庄宗", "同光"), ("李嗣源|Li Siyuan", 926, 933, "圣德和武钦孝皇", "明宗", "天成"),
      ("李从厚|Li Conghou", 933, 934, "闵", "", "应顺"), ("李从珂|Li Congke", 934, 937, "", "", "清泰")]),
    ("hou_jin", "晋|Later Jin", ["henan"], "石|Shi",
     [("石敬瑭|Shi Jingtang", 936, 942, "圣文章武明德孝皇", "高祖", "天福"), ("石重贵|Shi Chonggui", 942, 947, "", "", "开运")]),
    ("hou_han", "汉|Later Han", ["henan"], "刘|Liu",
     [("刘知远|Liu Zhiyuan", 947, 948, "睿文圣武昭肃孝皇", "高祖", "天福"), ("刘承祐|Liu Chengyou", 948, 951, "隐", "", "乾祐")]),
    ("hou_zhou", "周|Later Zhou", ["henan"], "郭|Guo",
     [("郭威|Guo Wei", 951, 954, "圣神恭肃文武孝皇", "太祖", "广顺"), ("柴荣|Chai Rong", 954, 959, "睿武孝文皇", "世宗", "显德"),
      ("柴宗训|Chai Zongxun", 959, 960, "恭", "", "显德")]),
]:
    d = D(did, name, "Huaxia", seat, fam)
    for n, a, b, shi, miao, era in rulers:
        R(d, n, a, b, shi=shi, miao=miao, era=era)

li = D("liao", "辽|Liao", "Youmu", ["monan"], "耶律|Yelü")
for n, a, b, shi, miao, era in [
    ("耶律阿保机|Yelü Abaoji", 916, 926, "大圣大明神烈天皇", "太祖", "神册"),
    ("耶律德光|Yelü Deguang", 926, 947, "孝武惠文皇", "太宗", "天显"),
    ("耶律阮|Yelü Ruan", 947, 951, "孝和庄宪皇", "世宗", "天禄"),
    ("耶律璟|Yelü Jing", 951, 969, "孝安敬正皇", "穆宗", "应历"),
    ("耶律贤|Yelü Xian", 969, 982, "孝成康靖皇", "景宗", "保宁"),
    ("耶律隆绪|Yelü Longxu", 982, 1031, "文武大孝宣皇", "圣宗", "统和"),
    ("耶律宗真|Yelü Zongzhen", 1031, 1055, "神圣孝章皇", "兴宗", "景福"),
    ("耶律洪基|Yelü Hongji", 1055, 1101, "仁圣大孝文皇", "道宗", "清宁"),
    ("耶律延禧|Yelü Yanxi", 1101, 1125, "", "", "乾统"),
]:
    R(li, n, a, b, shi=shi, miao=miao, era=era)

sg = D("song", "宋|Song", "Huaxia", ["henan", "jiangdong"], "赵|Zhao")
P(sg, "sg_hongyin", "赵弘殷|Zhao Hongyin")
for n, a, b, shi, miao, era, p, f in [
    ("赵匡胤|Zhao Kuangyin", 960, 976, "英武圣文神德", "太祖", "建隆", "sg01", "sg_hongyin"),
    ("赵光义|Zhao Guangyi", 976, 997, "神功圣德文武", "太宗", "太平兴国", "sg02", "sg_hongyin"),
    ("赵恒|Zhao Heng", 997, 1022, "文明章圣元孝", "真宗", "咸平", "sg03", "sg02"),
    ("赵祯|Zhao Zhen", 1022, 1063, "体天法道极功全德神文圣武睿哲明孝", "仁宗", "天圣", "sg04", "sg03"),
    ("赵曙|Zhao Shu", 1063, 1067, "体乾膺历隆功盛德宪文肃武睿圣宣孝", "英宗", "治平", "sg05", "sg_yunrang"),
    ("赵顼|Zhao Xu", 1067, 1085, "体元显道法古立宪帝德王功英文烈武钦仁圣孝", "神宗", "熙宁", "sg06", "sg05"),
    ("赵煦|Zhao Xu", 1085, 1100, "宪元继道显德定功钦文睿武齐圣昭孝", "哲宗", "元祐", "sg07", "sg06"),
    ("赵佶|Zhao Ji", 1100, 1126, "体神合道骏烈逊功圣文仁德宪慈显孝", "徽宗", "建中靖国", "sg08", "sg06"),
    ("赵桓|Zhao Huan", 1126, 1127, "恭文顺德仁孝", "钦宗", "靖康", "sg09", "sg08"),
    ("赵构|Zhao Gou", 1127, 1162, "受命中兴全功至德圣神武文昭仁宪孝", "高宗", "建炎", "sg10", "sg08"),
    ("赵昚|Zhao Shen", 1162, 1189, "绍统同道冠德昭功哲文神武明圣成孝", "孝宗", "隆兴", "sg11", "sg_zicheng"),
]:
    R(sg, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f)
for p, n, f in [
    ("sg_yuanfen", "赵元份|Zhao Yuanfen", "sg02"), ("sg_yunrang", "赵允让|Zhao Yunrang", "sg_yuanfen"),
    ("sg_defang", "赵德芳|Zhao Defang", "sg01"), ("sg_weixian", "赵惟宪|Zhao Weixian", "sg_defang"),
    ("sg_congyu", "赵从郁|Zhao Congyu", "sg_weixian"), ("sg_shijiang", "赵世将|Zhao Shijiang", "sg_congyu"),
    ("sg_lingzhan", "赵令詹|Zhao Lingzhan", "sg_shijiang"), ("sg_zicheng", "赵子偁|Zhao Zicheng", "sg_lingzhan"),
]:
    P(sg, p, n, f)

jj = D("jurchen_jin", "金|Jin", "Youmu", ["youyan", "manchuria"], "完颜|Wanyan")
P(jj, "jj_helibo", "完颜劾里钵|Wanyan Helibo")
for n, a, b, shi, miao, era, p, f in [
    ("完颜阿骨打|Wanyan Aguda", 1115, 1123, "应乾兴运昭德定功仁明庄孝大圣武元", "太祖", "收国", "jj01", "jj_helibo"),
    ("完颜吴乞买|Wanyan Wuqimai", 1123, 1135, "体元应运世德昭功哲惠仁圣文烈", "太宗", "天会", "jj02", "jj_helibo"),
    ("完颜亶|Wanyan Dan", 1135, 1149, "弘基缵武庄靖孝成", "熙宗", "天眷", "jj03", "jj_zongjun"),
    ("完颜亮|Wanyan Liang", 1149, 1161, "", "", "天德", "jj04", "jj_zonggan"),
    ("完颜雍|Wanyan Yong", 1161, 1189, "光天兴运文德武功圣明仁孝", "世宗", "大定", "jj05", "jj_zongfu"),
]:
    R(jj, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f)
for p, n, f in [
    ("jj_zongjun", "完颜宗峻|Wanyan Zongjun", "jj01"), ("jj_zonggan", "完颜宗干|Wanyan Zonggan", "jj01"),
    ("jj_zongfu", "完颜宗辅|Wanyan Zongfu", "jj01"), ("jj_yungong", "完颜允恭|Wanyan Yungong", "jj05"),
]:
    P(jj, p, n, f)

xx = D("xixia", "夏|Western Xia", "Youmu", ["hetao"], "嵬名|Weiming")
R(xx, "嵬名元昊|Weiming Yuanhao", 1038, 1048, shi="武烈", miao="景宗", era="天授礼法延祚", p="xx01")
R(xx, "嵬名谅祚|Weiming Liangzuo", 1048, 1067, shi="昭英", miao="毅宗", era="延嗣宁国", p="xx02", f="xx01")
R(xx, "嵬名秉常|Weiming Bingchang", 1067, 1086, shi="康靖", miao="惠宗", era="乾道", p="xx03", f="xx02")
R(xx, "嵬名乾顺|Weiming Qianshun", 1086, 1139, shi="圣文", miao="崇宗", era="天仪治平", p="xx04", f="xx03")

# 春秋战国诸侯(战国档的今上世系)
qs = D("qin_state", "秦|Qin", "Huaxia", ["guanzhong"], "嬴|Ying")
R(qs, "嬴渠梁|Ying Quliang", -361, -338, t="秦孝公|Duke Xiao of Qin", p="qs01")
R(qs, "嬴驷|Ying Si", -338, -311, t="秦惠文王|King Huiwen of Qin", p="qs02", f="qs01")
R(qs, "嬴荡|Ying Dang", -311, -307, t="秦武王|King Wu of Qin", p="qs03", f="qs02")

# ---------------- 其余 ----------------
def west(d, rows):
    for row in rows:
        n, a, b = row[0], row[1], row[2]
        extra = row[3] if len(row) > 3 else {}
        R(d, n, a, b, **extra)


ach = D("achaemenid", "阿契美尼德|Achaemenid", "Persepolis", ["persis"], "阿契美尼德|Achaemenid", True)
west(ach, [("居鲁士二世|Cyrus II", -550, -530), ("冈比西斯二世|Cambyses II", -530, -522), ("大流士一世|Darius I", -522, -486),
           ("薛西斯一世|Xerxes I", -486, -465), ("阿尔塔薛西斯一世|Artaxerxes I", -465, -424), ("大流士二世|Darius II", -423, -405),
           ("阿尔塔薛西斯二世|Artaxerxes II", -405, -358), ("阿尔塔薛西斯三世|Artaxerxes III", -358, -338),
           ("大流士三世|Darius III", -336, -330)])
arg = D("argead", "马其顿|Macedon", "Roma", ["macedonia"], "阿吉德|Argead", True)
west(arg, [("腓力二世|Philip II", -359, -336), ("亚历山大三世|Alexander III", -336, -323), ("腓力三世|Philip III", -323, -317),
           ("亚历山大四世|Alexander IV", -317, -309)])
sel = D("seleucid", "塞琉古|Seleucid", "Persepolis", ["syria", "mesopotamia"], "塞琉古|Seleucid", True)
west(sel, [("塞琉古一世|Seleucus I", -305, -281), ("安条克一世|Antiochus I", -281, -261), ("安条克二世|Antiochus II", -261, -246),
           ("塞琉古二世|Seleucus II", -246, -225), ("塞琉古三世|Seleucus III", -225, -223), ("安条克三世|Antiochus III", -223, -187),
           ("塞琉古四世|Seleucus IV", -187, -175), ("安条克四世|Antiochus IV", -175, -164), ("德米特里一世|Demetrius I", -162, -150),
           ("德米特里二世|Demetrius II", -145, -138), ("安条克七世|Antiochus VII", -138, -129), ("安条克八世|Antiochus VIII", -125, -96),
           ("安条克十三世|Antiochus XIII", -69, -64)])
pto = D("ptolemaic", "托勒密|Ptolemaic", "Egypt", ["egypt"], "拉吉德|Lagid", True)
west(pto, [("托勒密一世|Ptolemy I", -305, -283), ("托勒密二世|Ptolemy II", -283, -246), ("托勒密三世|Ptolemy III", -246, -222),
           ("托勒密四世|Ptolemy IV", -222, -204), ("托勒密五世|Ptolemy V", -204, -180), ("托勒密六世|Ptolemy VI", -180, -145),
           ("托勒密八世|Ptolemy VIII", -145, -116), ("托勒密九世|Ptolemy IX", -116, -80), ("托勒密十二世|Ptolemy XII", -80, -51),
           ("克娄巴特拉七世|Cleopatra VII", -51, -30)])
mau = D("maurya", "孔雀|Maurya", "India", ["magadha"], "孔雀|Maurya", True)
west(mau, [("旃陀罗笈多|Chandragupta", -322, -298), ("宾头沙罗|Bindusara", -298, -273), ("阿育|Ashoka", -268, -232),
           ("十车|Dasharatha", -232, -224), ("三钵罗底|Samprati", -224, -215), ("巨车|Brihadratha", -187, -185)])
par = D("parthia", "帕提亚|Parthia", "Persepolis", ["media", "khorasan"], "阿尔沙克|Arsacid", True)
west(par, [("阿尔沙克一世|Arsaces I", -247, -217), ("米特里达梯一世|Mithridates I", -171, -132), ("弗拉特斯二世|Phraates II", -132, -127),
           ("米特里达梯二世|Mithridates II", -124, -88), ("奥罗德斯二世|Orodes II", -57, -37), ("弗拉特斯四世|Phraates IV", -37, -2),
           ("阿尔塔巴努斯二世|Artabanus II", 12, 38), ("沃洛吉斯一世|Vologases I", 51, 78), ("帕科鲁斯二世|Pacorus II", 78, 110),
           ("奥斯罗埃斯|Osroes I", 109, 129), ("沃洛吉斯三世|Vologases III", 110, 147), ("沃洛吉斯四世|Vologases IV", 147, 191),
           ("沃洛吉斯五世|Vologases V", 191, 208), ("阿尔塔巴努斯四世|Artabanus IV", 213, 224)])
sas = D("sasanian", "萨珊|Sasanian", "Persepolis", ["mesopotamia", "persis"], "萨珊|Sasan", True)
west(sas, [("阿尔达希尔一世|Ardashir I", 224, 242), ("沙普尔一世|Shapur I", 242, 270), ("沙普尔二世|Shapur II", 309, 379),
           ("巴赫拉姆五世|Bahram V", 420, 438), ("卑路斯一世|Peroz I", 459, 484), ("喀瓦德一世|Kavad I", 488, 531),
           ("霍斯劳一世|Khosrow I", 531, 579), ("霍斯劳二世|Khosrow II", 590, 628), ("伊嗣俟三世|Yazdegerd III", 632, 651)])
kus = D("kushan", "贵霜|Kushan", "India", ["gandhara"], "贵霜|Kushan", True)
west(kus, [("丘就却|Kujula Kadphises", 30, 80), ("维玛·塔克图|Vima Takto", 80, 105), ("维玛|Vima Kadphises", 105, 127),
           ("迦腻色伽|Kanishka", 127, 150), ("胡维色伽|Huvishka", 150, 190), ("韦苏提婆|Vasudeva", 190, 230)])

rom = D("rome", "罗马|Rome", "Roma", ["italy_c"], "", True)
west(rom, [
    ("奥古斯都|Augustus", -27, 14, {"dyn": "尤利乌斯-克劳狄|Julio-Claudian"}), ("提比略|Tiberius", 14, 37, {"dyn": "尤利乌斯-克劳狄|Julio-Claudian"}),
    ("卡利古拉|Caligula", 37, 41, {"dyn": "尤利乌斯-克劳狄|Julio-Claudian"}), ("克劳狄|Claudius", 41, 54, {"dyn": "尤利乌斯-克劳狄|Julio-Claudian"}),
    ("尼禄|Nero", 54, 68, {"dyn": "尤利乌斯-克劳狄|Julio-Claudian"}), ("加尔巴|Galba", 68, 69), ("奥托|Otho", 69, 69), ("维特里乌斯|Vitellius", 69, 69),
    ("韦斯巴芗|Vespasian", 69, 79, {"dyn": "弗拉维|Flavian"}), ("提图斯|Titus", 79, 81, {"dyn": "弗拉维|Flavian"}),
    ("图密善|Domitian", 81, 96, {"dyn": "弗拉维|Flavian"}), ("涅尔瓦|Nerva", 96, 98, {"dyn": "涅尔瓦-安敦尼|Nerva-Antonine"}),
    ("图拉真|Trajan", 98, 117, {"dyn": "涅尔瓦-安敦尼|Nerva-Antonine"}), ("哈德良|Hadrian", 117, 138, {"dyn": "涅尔瓦-安敦尼|Nerva-Antonine"}),
    ("安敦宁·毕尤|Antoninus Pius", 138, 161, {"dyn": "涅尔瓦-安敦尼|Nerva-Antonine"}),
    ("马可·奥勒留|Marcus Aurelius", 161, 180, {"dyn": "涅尔瓦-安敦尼|Nerva-Antonine"}), ("康茂德|Commodus", 180, 192, {"dyn": "涅尔瓦-安敦尼|Nerva-Antonine"}),
    ("佩蒂纳克斯|Pertinax", 193, 193), ("尤利安努斯|Didius Julianus", 193, 193),
    ("塞普蒂米乌斯·塞维鲁|Septimius Severus", 193, 211, {"dyn": "塞维鲁|Severan"}), ("卡拉卡拉|Caracalla", 211, 217, {"dyn": "塞维鲁|Severan"}),
    ("马克里努斯|Macrinus", 217, 218), ("埃拉伽巴路斯|Elagabalus", 218, 222, {"dyn": "塞维鲁|Severan"}),
    ("塞维鲁·亚历山大|Severus Alexander", 222, 235, {"dyn": "塞维鲁|Severan"}),
    ("马克西米努斯|Maximinus Thrax", 235, 238), ("戈尔迪安三世|Gordian III", 238, 244), ("阿拉伯人腓力|Philip the Arab", 244, 249),
    ("德基乌斯|Decius", 249, 251), ("瓦勒良|Valerian", 253, 260), ("加里恩努斯|Gallienus", 253, 268), ("奥勒良|Aurelian", 270, 275),
    ("普罗布斯|Probus", 276, 282), ("戴克里先|Diocletian", 284, 305), ("君士坦丁一世|Constantine I", 306, 337, {"dyn": "君士坦丁|Constantinian"}),
    ("君士坦提乌斯二世|Constantius II", 337, 361, {"dyn": "君士坦丁|Constantinian"}), ("尤利安|Julian", 361, 363, {"dyn": "君士坦丁|Constantinian"}),
    ("约维安|Jovian", 363, 364), ("瓦伦斯|Valens", 364, 378, {"dyn": "瓦伦提尼安|Valentinianic"}),
    ("狄奥多西一世|Theodosius I", 379, 395, {"dyn": "狄奥多西|Theodosian"})])
wro = D("west_rome", "西罗马|Western Rome", "Roma", ["italy_n"], "", True)
west(wro, [("霍诺留|Honorius", 395, 423), ("瓦伦提尼安三世|Valentinian III", 425, 455), ("马约里安|Majorian", 457, 461),
           ("安特米乌斯|Anthemius", 467, 472), ("尤利乌斯·尼波斯|Julius Nepos", 474, 475), ("罗慕路斯·奥古斯都|Romulus Augustulus", 475, 476)])
byz = D("byzantium", "罗马|Rome", "Roma", ["thrace"], "", True)
B = []
for n, a, b, dyn in [
    ("阿卡狄乌斯|Arcadius", 395, 408, "狄奥多西|Theodosian"), ("狄奥多西二世|Theodosius II", 408, 450, "狄奥多西|Theodosian"),
    ("马尔西安|Marcian", 450, 457, "狄奥多西|Theodosian"), ("利奥一世|Leo I", 457, 474, "利奥|Leonid"), ("芝诺|Zeno", 474, 491, "利奥|Leonid"),
    ("阿纳斯塔修斯一世|Anastasius I", 491, 518, "利奥|Leonid"), ("查士丁一世|Justin I", 518, 527, "查士丁尼|Justinian"),
    ("查士丁尼一世|Justinian I", 527, 565, "查士丁尼|Justinian"), ("查士丁二世|Justin II", 565, 578, "查士丁尼|Justinian"),
    ("提比略二世|Tiberius II", 578, 582, "查士丁尼|Justinian"), ("莫里斯|Maurice", 582, 602, "查士丁尼|Justinian"), ("福卡斯|Phocas", 602, 610, ""),
    ("希拉克略|Heraclius", 610, 641, "希拉克略|Heraclian"), ("君士坦斯二世|Constans II", 641, 668, "希拉克略|Heraclian"),
    ("君士坦丁四世|Constantine IV", 668, 685, "希拉克略|Heraclian"), ("查士丁尼二世|Justinian II", 685, 695, "希拉克略|Heraclian"),
    ("利昂提奥斯|Leontios", 695, 698, ""), ("提比略三世|Tiberios III", 698, 705, ""), ("查士丁尼二世|Justinian II", 705, 711, "希拉克略|Heraclian"),
    ("菲利皮库斯|Philippikos", 711, 713, ""), ("阿纳斯塔修斯二世|Anastasios II", 713, 715, ""), ("狄奥多西三世|Theodosios III", 715, 717, ""),
    ("利奥三世|Leo III", 717, 741, "伊苏里亚|Isaurian"), ("君士坦丁五世|Constantine V", 741, 775, "伊苏里亚|Isaurian"),
    ("利奥四世|Leo IV", 775, 780, "伊苏里亚|Isaurian"), ("君士坦丁六世|Constantine VI", 780, 797, "伊苏里亚|Isaurian"),
    ("伊琳娜|Irene", 797, 802, "伊苏里亚|Isaurian"), ("尼基弗鲁斯一世|Nikephoros I", 802, 811, "尼基弗鲁斯|Nikephorian"),
    ("米海尔一世|Michael I", 811, 813, "尼基弗鲁斯|Nikephorian"), ("利奥五世|Leo V", 813, 820, ""), ("米海尔二世|Michael II", 820, 829, "阿莫里亚|Amorian"),
    ("狄奥斐卢斯|Theophilos", 829, 842, "阿莫里亚|Amorian"), ("米海尔三世|Michael III", 842, 867, "阿莫里亚|Amorian"),
    ("巴西尔一世|Basil I", 867, 886, "马其顿|Macedonian"), ("利奥六世|Leo VI", 886, 912, "马其顿|Macedonian"),
    ("君士坦丁七世|Constantine VII", 913, 959, "马其顿|Macedonian"), ("罗曼努斯一世|Romanos I", 920, 944, "利卡潘努斯|Lekapenos"),
    ("罗曼努斯二世|Romanos II", 959, 963, "马其顿|Macedonian"), ("尼基弗鲁斯二世|Nikephoros II", 963, 969, "福卡斯|Phokas"),
    ("约翰一世|John I", 969, 976, ""), ("巴西尔二世|Basil II", 976, 1025, "马其顿|Macedonian"),
    ("君士坦丁八世|Constantine VIII", 1025, 1028, "马其顿|Macedonian"), ("罗曼努斯三世|Romanos III", 1028, 1034, ""),
    ("米海尔四世|Michael IV", 1034, 1041, ""), ("君士坦丁九世|Constantine IX", 1042, 1055, ""), ("狄奥多拉|Theodora", 1055, 1056, "马其顿|Macedonian"),
    ("伊萨克一世|Isaac I", 1057, 1059, "科穆宁|Komnenos"), ("君士坦丁十世|Constantine X", 1059, 1067, "杜卡斯|Doukas"),
    ("罗曼努斯四世|Romanos IV", 1068, 1071, ""), ("米海尔七世|Michael VII", 1071, 1078, "杜卡斯|Doukas"),
    ("尼基弗鲁斯三世|Nikephoros III", 1078, 1081, ""), ("阿莱克修斯一世|Alexios I", 1081, 1118, "科穆宁|Komnenos"),
    ("约翰二世|John II", 1118, 1143, "科穆宁|Komnenos"), ("曼努埃尔一世|Manuel I", 1143, 1180, "科穆宁|Komnenos"),
    ("阿莱克修斯二世|Alexios II", 1180, 1183, "科穆宁|Komnenos"), ("安德洛尼卡一世|Andronikos I", 1183, 1185, "科穆宁|Komnenos"),
]:
    R(byz, n, a, b, dyn=dyn)

rsh = D("rashidun", "正统哈里发|Rashidun", "Arab", ["arabia"], "", True)
west(rsh, [("阿布·伯克尔|Abu Bakr", 632, 634), ("欧麦尔|Umar", 634, 644), ("奥斯曼|Uthman", 644, 656), ("阿里|Ali", 656, 661)])
umy = D("umayyad", "倭马亚|Umayyad", "Arab", ["syria"], "倭马亚|Umayyad", True)
west(umy, [("穆阿维叶一世|Muawiya I", 661, 680), ("叶齐德一世|Yazid I", 680, 683), ("马尔万一世|Marwan I", 684, 685),
           ("阿卜杜勒·马利克|Abd al-Malik", 685, 705), ("瓦利德一世|al-Walid I", 705, 715), ("苏莱曼|Sulayman", 715, 717),
           ("欧麦尔二世|Umar II", 717, 720), ("叶齐德二世|Yazid II", 720, 724), ("希沙姆|Hisham", 724, 743),
           ("瓦利德二世|al-Walid II", 743, 744), ("马尔万二世|Marwan II", 744, 750)])
abb = D("abbasid", "阿拔斯|Abbasid", "Arab", ["mesopotamia"], "阿拔斯|Abbasid", True)
P(abb, "ab_ali", "穆罕默德·本·阿里|Muhammad ibn Ali")
R(abb, "萨法赫|al-Saffah", 750, 754, p="ab01", f="ab_ali")
R(abb, "曼苏尔|al-Mansur", 754, 775, p="ab02", f="ab_ali")
R(abb, "马赫迪|al-Mahdi", 775, 785, p="ab03", f="ab02")
R(abb, "哈迪|al-Hadi", 785, 786, p="ab04", f="ab03")
R(abb, "哈伦|Harun", 786, 809, p="ab05", f="ab03")
west(abb, [("阿明|al-Amin", 809, 813), ("马蒙|al-Ma'mun", 813, 833), ("穆阿台绥姆|al-Mu'tasim", 833, 842), ("瓦西格|al-Wathiq", 842, 847),
           ("穆塔瓦基勒|al-Mutawakkil", 847, 861), ("穆尔台迪德|al-Mu'tadid", 892, 902), ("穆格台迪尔|al-Muqtadir", 908, 932),
           ("拉迪|al-Radi", 934, 940), ("穆提|al-Muti", 946, 974), ("卡迪尔|al-Qadir", 991, 1031), ("卡伊姆|al-Qa'im", 1031, 1075),
           ("穆格台迪|al-Muqtadi", 1075, 1094), ("穆斯塔齐尔|al-Mustazhir", 1094, 1118), ("穆斯塔尔希德|al-Mustarshid", 1118, 1135),
           ("穆格塔菲|al-Muqtafi", 1136, 1160), ("穆斯坦吉德|al-Mustanjid", 1160, 1170), ("穆斯塔迪|al-Mustadi", 1170, 1180)])
fat = D("fatimid", "法蒂玛|Fatimid", "Arab", ["egypt"], "法蒂玛|Fatimid", True)
west(fat, [("马赫迪|al-Mahdi Billah", 909, 934), ("穆仪兹|al-Mu'izz", 953, 975), ("阿齐兹|al-Aziz", 975, 996),
           ("哈基姆|al-Hakim", 996, 1021), ("穆斯坦绥尔|al-Mustansir", 1036, 1094), ("阿迪德|al-Adid", 1160, 1171)])
mer = D("merovingian", "法兰克|Francia", "Frankish", ["gaul_n"], "墨洛温|Merovingian", True)
west(mer, [("克洛维一世|Clovis I", 481, 511), ("克洛泰尔一世|Chlothar I", 558, 561), ("克洛泰尔二世|Chlothar II", 613, 629),
           ("达戈贝尔特一世|Dagobert I", 629, 639), ("克洛维二世|Clovis II", 639, 657), ("提乌德里克三世|Theuderic III", 675, 691),
           ("希尔德里克三世|Childeric III", 743, 751)])
car = D("carolingian", "法兰克|Francia", "Frankish", ["rhine", "gaul_n"], "加洛林|Carolingian", True)
P(car, "car_martel", "查理·马特|Charles Martel")
R(car, "丕平|Pepin", 751, 768, p="cr01", f="car_martel")
R(car, "查理|Charles", 768, 814, p="cr02", f="cr01")
R(car, "路易|Louis", 814, 840, p="cr03", f="cr02")
R(car, "洛泰尔|Lothair", 840, 855, p="cr04", f="cr03")
R(car, "路易|Louis II", 855, 875, p="cr05", f="cr04")
R(car, "秃头查理|Charles the Bald", 875, 877, p="cr06", f="cr03")
R(car, "胖子查理|Charles the Fat", 881, 888, p="cr07", f="car_louis_german")
P(car, "car_louis_german", "日耳曼人路易|Louis the German", "cr03")
hre = D("hre", "罗马|Rome", "Germanic", ["germania"], "", True)
west(hre, [("奥托一世|Otto I", 962, 973, {"dyn": "奥托|Ottonian"}), ("奥托二世|Otto II", 973, 983, {"dyn": "奥托|Ottonian"}),
           ("奥托三世|Otto III", 983, 1002, {"dyn": "奥托|Ottonian"}), ("亨利二世|Henry II", 1002, 1024, {"dyn": "奥托|Ottonian"}),
           ("康拉德二世|Conrad II", 1024, 1039, {"dyn": "萨利安|Salian"}), ("亨利三世|Henry III", 1039, 1056, {"dyn": "萨利安|Salian"}),
           ("亨利四世|Henry IV", 1056, 1105, {"dyn": "萨利安|Salian"}), ("亨利五世|Henry V", 1105, 1125, {"dyn": "萨利安|Salian"}),
           ("洛泰尔三世|Lothair III", 1125, 1137, {"dyn": "苏普林堡|Supplinburg"}),
           ("康拉德三世|Conrad III", 1138, 1152, {"dyn": "霍亨施陶芬|Hohenstaufen"}),
           ("腓特烈一世|Frederick I", 1152, 1190, {"dyn": "霍亨施陶芬|Hohenstaufen"})])
tib = D("tibet", "吐蕃|Tibet", "Youmu", ["tibet"], "悉补野|Pugyal")
west(tib, [("松赞干布|Songtsen Gampo", 618, 650), ("芒松芒赞|Mangsong Mangtsen", 650, 676), ("赤都松赞|Tridu Songtsen", 676, 704),
           ("赤德祖赞|Tride Tsuktsen", 704, 754), ("赤松德赞|Trisong Detsen", 755, 797), ("牟尼赞普|Muné Tsenpo", 797, 799),
           ("赤德松赞|Tride Songtsen", 799, 815), ("赤祖德赞|Ralpacan", 815, 838), ("朗达玛|Langdarma", 838, 842)])
uyg = D("uyghur", "回鹘|Uyghur", "Youmu", ["mobei"], "药罗葛|Yaglakar")
west(uyg, [("骨力裴罗|Kutlug Bilge", 744, 747), ("磨延啜|Bayanchur", 747, 759), ("牟羽可汗|Bögü", 759, 779),
           ("顿莫贺|Alp Qutlugh", 779, 789), ("怀信可汗|Qutlugh Bilge", 795, 805)])
qk = D("qara_khitai", "西辽|Qara Khitai", "Youmu", ["kazakh"], "耶律|Yelü")
west(qk, [("耶律大石|Yelü Dashi", 1124, 1143), ("萧塔不烟|Xiao Tabuyan", 1143, 1150), ("耶律夷列|Yelü Yilie", 1150, 1163),
          ("耶律普速完|Yelü Pusuwan", 1163, 1177)])


sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import history_west  # noqa: E402

history_west.add(D, R, P)

import history_late  # noqa: E402

history_late.add(D, R, P, lambda id: next(d for d in DYNASTIES if d["id"] == id))


# 称帝的王朝：亡了才进"已亡帝国"名录、史书挂到帝国法理上；王国(伦巴第、麦西亚……)只进原版历代君主与族谱
IMPERIAL = {"zhou", "qin", "han", "xin", "cao_wei", "shu_han", "sun_wu", "jin", "liu_song", "nan_qi", "liang", "chen", "bei_wei",
            "bei_qi", "bei_zhou", "sui", "wu_zhou", "tang", "hou_liang", "hou_tang", "hou_jin", "hou_han", "hou_zhou", "liao",
            "song", "jurchen_jin", "xixia", "achaemenid", "argead", "seleucid", "ptolemaic", "maurya", "parthia", "sasanian",
            "kushan", "rome", "west_rome", "byzantium", "rashidun", "umayyad", "abbasid", "fatimid", "carolingian", "hre",
            "tibet", "uyghur", "qara_khitai", "first_bulgaria", "seljuk", "yuan", "ming", "ilkhanate", "timurid", "delhi",
            "mughal", "safavid", "mamluk", "ottoman", "tsardom"}


# 不在位的祖先的称号(族谱里显示，不写的显示"白身")
PEOPLE_TITLES = {
    "han_qing": "清河孝王|Prince Xiao of Qinghe", "han_ju": "戾太子|Crown Prince Li", "han_jin": "史皇孙|Imperial Grandson Shi",
    "han_fa": "长沙定王|Prince Ding of Changsha", "han_qin": "南顿君|Lord of Nandun", "han_taigong": "太上皇|Taishang Huang",
    "tang_bing": "唐世祖|Shizu of Tang", "sg_hongyin": "宋宣祖|Xuanzu of Song", "sg_yunrang": "濮安懿王|Prince Anyi of Pu",
    "sg_zicheng": "秀安僖王|Prince Anxi of Xiu", "jj_yungong": "金显宗|Xianzong of Jin", "yu_tolui": "元睿宗|Ruizong of Yuan",
    "yu_zhenjin": "元裕宗|Yuzong of Yuan", "yu_darmabala": "元顺宗|Shunzong of Yuan", "yu_gamala": "晋王|Prince of Jin",
    "yu_yesugei": "元烈祖|Liezu of Yuan", "js_jeongwon": "定远君|Grand Prince Jeongwon",
    "js_deokheung": "德兴大院君|Grand Internal Prince Deokheung", "hb_charles2": "内奥地利大公|Archduke of Inner Austria",
    "hb_philip1": "卡斯蒂利亚国王|King of Castile", "cp_antoine": "纳瓦拉国王|King of Navarre", "en_darnley": "达恩利勋爵|Lord Darnley",
    "en_black_prince": "威尔士亲王|Prince of Wales", "en_gaunt": "兰开斯特公爵|Duke of Lancaster", "car_martel": "宫相|Mayor of the Palace",
}


def finish():
    for d in DYNASTIES:
        for e in d["people"]:
            if e["p"] in PEOPLE_TITLES and not e.get("t"):
                e["t"] = PEOPLE_TITLES[e["p"]]
    for d in DYNASTIES:
        d["emp"] = d["id"] in IMPERIAL
    # 长谥号以"皇"收尾，显示时再拼"帝"(唐至道大圣大明孝皇帝、宋英武圣文神德皇帝)
    for d in DYNASTIES:
        for r in d["rulers"]:
            shi = r.get("shi", "")
            if len(shi) >= 4 and not shi.endswith("皇"):
                r["shi"] = shi + "皇"
    hl14 = next(r for r in next(d for d in DYNASTIES if d["id"] == "han")["rulers"] if r.get("p") == "hl14")
    hl14["t"] = "孺子|Ruzi"
    import history_events  # noqa: E402
    for key, year, text in history_events.attach(DYNASTIES):
        print("大事没挂上:", key, year, text)


def main():
    finish()
    root = sys.argv[1]
    out = {"_note": "世界地图的前史(由 Tools/WorldMap/history_source.py 生成，勿手改；改完跑 add_traditional.py)", "dynasties": DYNASTIES}
    path = os.path.join(root, "WorldMaps", "history.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("dynasties", len(DYNASTIES), "rulers", sum(len(d["rulers"]) for d in DYNASTIES),
          "people", sum(len(d["people"]) for d in DYNASTIES))


if __name__ == "__main__":
    main()
