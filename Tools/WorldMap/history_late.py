"""1190 年以后到 1628 年(临高剧本)的前史(history_source.py 引入)：南宋、金、西夏的后半，元、明，后金、朝鲜、德川，
帖木儿、德里苏丹、马穆鲁克、伊儿汗，奥斯曼、萨法维、莫卧儿，拜占庭、阿拔斯、神罗的后半，法兰西、英格兰、丹麦、瑞典、
波兰、俄罗斯、西班牙的历代君主。已有朝代的后续直接接在原朝代后面(按在位起年排)。"""


def add(D, R, P, find):
    def rows(d, items):
        for item in items:
            n, a, b = item[0], item[1], item[2]
            p = item[3] if len(item) > 3 else ""
            f = item[4] if len(item) > 4 else ""
            dyn = item[5] if len(item) > 5 else ""
            t = item[6] if len(item) > 6 else ""
            R(d, n, a, b, p=p, f=f, dyn=dyn, t=t)

    def han(d, items):
        for n, a, b, shi, miao, era, p, f, t in items:
            R(d, n, a, b, shi=shi, miao=miao, era=era, p=p, f=f, t=t)

    # ---------------- 华夏 ----------------
    sg = find("song")
    han(sg, [
        ("赵惇|Zhao Dun", 1189, 1194, "循道宪仁明功茂德温文顺武圣哲慈孝", "光宗", "绍熙", "sg12", "sg11", ""),
        ("赵扩|Zhao Kuo", 1194, 1224, "法天备道纯德茂功仁文哲武圣睿恭孝", "宁宗", "庆元", "sg13", "sg12", ""),
        ("赵昀|Zhao Yun", 1224, 1264, "建道备德大功复兴烈文仁武圣明安孝", "理宗", "宝庆", "sg14", "sg_xilu", ""),
        ("赵禥|Zhao Qi", 1264, 1274, "端文明武景孝", "度宗", "咸淳", "sg15", "sg_yurui", ""),
        ("赵显|Zhao Xian", 1274, 1276, "恭", "", "德祐", "sg16", "sg15", ""),
        ("赵昰|Zhao Shi", 1276, 1278, "裕文昭武愍孝", "端宗", "景炎", "sg17", "sg15", ""),
        ("赵昺|Zhao Bing", 1278, 1279, "", "", "祥兴", "sg18", "sg15", "帝昺|Emperor Bing"),
    ])
    P(sg, "sg_xilu", "赵希瓐|Zhao Xilu")
    P(sg, "sg_yurui", "赵与芮|Zhao Yurui", "sg_xilu")

    jj = find("jurchen_jin")
    han(jj, [
        ("完颜璟|Wanyan Jing", 1189, 1208, "宪天光运仁文义武神圣英孝", "章宗", "明昌", "jj06", "jj_yungong", ""),
        ("完颜永济|Wanyan Yongji", 1208, 1213, "", "", "大安", "jj07", "jj05", "卫绍王|Prince Shao of Wei"),
        ("完颜珣|Wanyan Xun", 1213, 1224, "继天兴统述道勤仁英武圣孝", "宣宗", "贞祐", "jj08", "jj_yungong", ""),
        ("完颜守绪|Wanyan Shouxu", 1224, 1234, "", "", "正大", "jj09", "jj08", "金哀宗|Emperor Aizong of Jin"),
    ])

    xx = find("xixia")
    han(xx, [
        ("嵬名仁孝|Weiming Renxiao", 1139, 1193, "圣德", "仁宗", "大庆", "xx05", "xx04", ""),
        ("嵬名纯祐|Weiming Chunyou", 1193, 1206, "昭简", "桓宗", "天庆", "xx06", "xx05", ""),
        ("嵬名安全|Weiming Anquan", 1206, 1211, "敬穆", "襄宗", "应天", "xx07", "xx_renyou", ""),
        ("嵬名遵顼|Weiming Zunxu", 1211, 1223, "英文", "神宗", "光定", "xx08", "", ""),
        ("嵬名德旺|Weiming Dewang", 1223, 1226, "", "", "乾定", "xx09", "xx08", "夏献宗|Emperor Xianzong of Xia"),
        ("嵬名睍|Weiming Xian", 1226, 1227, "", "", "宝义", "xx10", "", "夏末主|Last Emperor of Xia"),
    ])
    P(xx, "xx_renyou", "嵬名仁友|Weiming Renyou", "xx04")

    yu = D("yuan", "元|Yuan", "Youmu", ["youyan", "mobei"], "孛儿只斤|Borjigin", True)
    P(yu, "yu_yesugei", "也速该|Yesügei")
    P(yu, "yu_tolui", "拖雷|Tolui", "yu01")
    P(yu, "yu_zhenjin", "真金|Zhenjin", "yu05")
    P(yu, "yu_gamala", "甘麻剌|Gammala", "yu_zhenjin")
    P(yu, "yu_darmabala", "答剌麻八剌|Darmabala", "yu_zhenjin")
    han(yu, [
        ("铁木真|Temüjin", 1206, 1227, "法天启运圣武", "太祖", "", "yu01", "yu_yesugei", ""),
        ("窝阔台|Ögedei", 1229, 1241, "英文", "太宗", "", "yu02", "yu01", ""),
        ("贵由|Güyük", 1246, 1248, "简平", "定宗", "", "yu03", "yu02", ""),
        ("蒙哥|Möngke", 1251, 1259, "桓肃", "宪宗", "", "yu04", "yu_tolui", ""),
        ("忽必烈|Kublai", 1260, 1294, "圣德神功文武", "世祖", "中统", "yu05", "yu_tolui", ""),
        ("铁穆耳|Temür", 1294, 1307, "钦明广孝", "成宗", "元贞", "yu06", "yu_zhenjin", ""),
        ("海山|Külüg", 1307, 1311, "仁惠宣孝", "武宗", "至大", "yu07", "yu_darmabala", ""),
        ("爱育黎拔力八达|Ayurbarwada", 1311, 1320, "圣文钦孝", "仁宗", "皇庆", "yu08", "yu_darmabala", ""),
        ("硕德八剌|Shidebala", 1320, 1323, "睿圣文孝", "英宗", "至治", "yu09", "yu08", ""),
        ("也孙铁木儿|Yesün Temür", 1323, 1328, "", "", "泰定", "yu10", "yu_gamala", "泰定帝|Taiding Emperor"),
        ("阿速吉八|Ragibagh", 1328, 1328, "", "", "天顺", "yu11", "yu10", "天顺帝|Tianshun Emperor"),
        ("图帖睦尔|Tugh Temür", 1328, 1332, "圣明元孝", "文宗", "天历", "yu12", "yu07", ""),
        ("和世剌|Khutughtu", 1329, 1329, "翼献景孝", "明宗", "", "yu13", "yu07", ""),
        ("懿璘质班|Rinchinbal", 1332, 1332, "冲圣嗣孝", "宁宗", "", "yu14", "yu13", ""),
        ("妥懽帖睦尔|Toghon Temür", 1333, 1368, "宣仁普孝", "惠宗", "元统", "yu15", "yu13", ""),
    ])

    mg = D("ming", "明|Ming", "Huaxia", ["youyan", "jiangdong"], "朱|Zhu")
    P(mg, "mg_shizhen", "朱世珍|Zhu Shizhen")
    P(mg, "mg_biao", "朱标|Zhu Biao", "mg01")
    P(mg, "mg_youyuan", "朱祐杬|Zhu Youyuan", "mg08")
    han(mg, [
        ("朱元璋|Zhu Yuanzhang", 1368, 1398, "高", "太祖", "洪武", "mg01", "mg_shizhen", ""),
        ("朱允炆|Zhu Yunwen", 1398, 1402, "", "", "建文", "mg02", "mg_biao", "建文帝|Jianwen Emperor"),
        ("朱棣|Zhu Di", 1402, 1424, "文", "成祖", "永乐", "mg03", "mg01", ""),
        ("朱高炽|Zhu Gaochi", 1424, 1425, "昭", "仁宗", "洪熙", "mg04", "mg03", ""),
        ("朱瞻基|Zhu Zhanji", 1425, 1435, "章", "宣宗", "宣德", "mg05", "mg04", ""),
        ("朱祁镇|Zhu Qizhen", 1435, 1449, "睿", "英宗", "正统", "mg06", "mg05", ""),
        ("朱祁钰|Zhu Qiyu", 1449, 1457, "景", "", "景泰", "mg07", "mg05", ""),
        ("朱祁镇|Zhu Qizhen", 1457, 1464, "睿", "英宗", "天顺", "mg06", "mg05", ""),
        ("朱见深|Zhu Jianshen", 1464, 1487, "纯", "宪宗", "成化", "mg08", "mg06", ""),
        ("朱祐樘|Zhu Youtang", 1487, 1505, "敬", "孝宗", "弘治", "mg09", "mg08", ""),
        ("朱厚照|Zhu Houzhao", 1505, 1521, "毅", "武宗", "正德", "mg10", "mg09", ""),
        ("朱厚熜|Zhu Houcong", 1521, 1567, "肃", "世宗", "嘉靖", "mg11", "mg_youyuan", ""),
        ("朱载坖|Zhu Zaiji", 1567, 1572, "庄", "穆宗", "隆庆", "mg12", "mg11", ""),
        ("朱翊钧|Zhu Yijun", 1572, 1620, "显", "神宗", "万历", "mg13", "mg12", ""),
        ("朱常洛|Zhu Changluo", 1620, 1620, "贞", "光宗", "泰昌", "mg14", "mg13", ""),
        ("朱由校|Zhu Youjiao", 1620, 1627, "悊", "熹宗", "天启", "mg15", "mg14", ""),
    ])

    ag = D("aisin_gioro", "金|Later Jin", "Youmu", ["manchuria"], "爱新觉罗|Aisin Gioro")
    P(ag, "ag_giocangga", "觉昌安|Giocangga")
    P(ag, "ag_taksi", "塔克世|Taksi", "ag_giocangga")
    han(ag, [("努尔哈赤|Nurhaci", 1616, 1626, "武", "太祖", "天命", "ag01", "ag_taksi", "")])

    js = D("joseon", "朝鲜|Joseon", "Huaxia", ["korea_s"], "李|Yi")
    P(js, "js_dowon", "李暲|Yi Jang", "js07")
    P(js, "js_deokheung", "李岹|Yi Cho", "js11")
    P(js, "js_jeongwon", "李琈|Yi Bu", "js14")
    rows(js, [
        ("李成桂|Yi Seong-gye", 1392, 1398, "js01", "", "", "朝鲜太祖|Taejo of Joseon"),
        ("李芳果|Yi Bang-gwa", 1398, 1400, "js02", "js01", "", "朝鲜定宗|Jeongjong of Joseon"),
        ("李芳远|Yi Bang-won", 1400, 1418, "js03", "js01", "", "朝鲜太宗|Taejong of Joseon"),
        ("李祹|Yi Do", 1418, 1450, "js04", "js03", "", "朝鲜世宗|Sejong of Joseon"),
        ("李珦|Yi Hyang", 1450, 1452, "js05", "js04", "", "朝鲜文宗|Munjong of Joseon"),
        ("李弘暐|Yi Hong-wi", 1452, 1455, "js06", "js05", "", "鲁山君|Nosan-gun"),
        ("李瑈|Yi Yu", 1455, 1468, "js07", "js04", "", "朝鲜世祖|Sejo of Joseon"),
        ("李晄|Yi Hwang", 1468, 1469, "js08", "js07", "", "朝鲜睿宗|Yejong of Joseon"),
        ("李娎|Yi Hyeol", 1469, 1494, "js09", "js_dowon", "", "朝鲜成宗|Seongjong of Joseon"),
        ("燕山君|Yeonsan-gun", 1494, 1506, "js10", "js09", "", "燕山君|Yeonsan-gun"),
        ("李怿|Yi Yeok", 1506, 1544, "js11", "js09", "", "朝鲜中宗|Jungjong of Joseon"),
        ("李峼|Yi Ho", 1544, 1545, "js12", "js11", "", "朝鲜仁宗|Injong of Joseon"),
        ("李峘|Yi Hwan", 1545, 1567, "js13", "js11", "", "朝鲜明宗|Myeongjong of Joseon"),
        ("李昖|Yi Yeon", 1567, 1608, "js14", "js_deokheung", "", "朝鲜宣祖|Seonjo of Joseon"),
        ("李珲|Yi Hon", 1608, 1623, "js15", "js14", "", "光海君|Gwanghae-gun"),
    ])

    tk = D("tokugawa", "日本|Japan", "Japan", ["japan_kanto"], "德川|Tokugawa")
    P(tk, "tk_hirotada", "松平广忠|Matsudaira Hirotada")
    rows(tk, [("德川家康|Tokugawa Ieyasu", 1603, 1605, "tk01", "tk_hirotada", "", "征夷大将军|Shogun"),
              ("德川秀忠|Tokugawa Hidetada", 1605, 1623, "tk02", "tk01", "", "征夷大将军|Shogun")])

    # ---------------- 草原、波斯、印度 ----------------
    il = D("ilkhanate", "伊儿汗国|Ilkhanate", "Persepolis", ["media", "persis"], "孛儿只斤|Borjigin", True)
    rows(il, [("旭烈兀|Hulagu", 1256, 1265, "il01", "yu_tolui"), ("阿八哈|Abaqa", 1265, 1282, "il02", "il01"),
              ("帖古迭儿|Tekuder", 1282, 1284, "il_tekuder", "il01"), ("阿鲁浑|Arghun", 1284, 1291, "il03", "il02"),
              ("乞合都|Gaykhatu", 1291, 1295, "il_gaykhatu", "il02"), ("合赞|Ghazan", 1295, 1304, "il04", "il03"),
              ("完者都|Öljaitü", 1304, 1316, "il05", "il03"), ("不赛因|Abu Sa'id", 1316, 1335, "il06", "il05")])

    ti = D("timurid", "帖木儿|Timurid", "Persepolis", ["sogdiana", "khorasan"], "帖木儿|Timurid", True)
    P(ti, "ti_miranshah", "米兰沙|Miran Shah", "ti01")
    P(ti, "ti_sultan_muhammad", "苏丹·穆罕默德|Sultan Muhammad", "ti_miranshah")
    rows(ti, [("帖木儿|Timur", 1370, 1405, "ti01"), ("哈里|Khalil Sultan", 1405, 1409, "ti_khalil", "ti_miranshah"),
              ("沙哈鲁|Shah Rukh", 1409, 1447, "ti02", "ti01"), ("兀鲁伯|Ulugh Beg", 1447, 1449, "ti03", "ti02"),
              ("卜撒因|Abu Sa'id", 1451, 1469, "ti05", "ti_sultan_muhammad"), ("忽辛·拜哈拉|Husayn Bayqara", 1469, 1506)])

    dl = D("delhi", "德里|Delhi", "India", ["hindustan"], "", True)
    rows(dl, [("库特布丁·艾伊拜克|Qutb ud-Din Aibak", 1206, 1210, "", "", "奴隶|Mamluk"),
              ("伊勒图特米什|Iltutmish", 1211, 1236, "", "", "奴隶|Mamluk"), ("巴勒班|Balban", 1266, 1287, "", "", "奴隶|Mamluk"),
              ("阿拉丁·卡尔吉|Alauddin Khalji", 1296, 1316, "", "", "卡尔吉|Khalji"),
              ("穆罕默德·本·图格鲁克|Muhammad bin Tughluq", 1325, 1351, "", "", "图格鲁克|Tughluq"),
              ("菲鲁兹·沙|Firuz Shah", 1351, 1388, "", "", "图格鲁克|Tughluq"), ("希兹尔汗|Khizr Khan", 1414, 1421, "", "", "赛义德|Sayyid"),
              ("巴赫卢尔·洛迪|Bahlul Lodi", 1451, 1489, "", "", "洛迪|Lodi"), ("西坎德尔·洛迪|Sikandar Lodi", 1489, 1517, "", "", "洛迪|Lodi"),
              ("易卜拉欣·洛迪|Ibrahim Lodi", 1517, 1526, "", "", "洛迪|Lodi")])

    mu = D("mughal", "莫卧儿|Mughal", "India", ["hindustan"], "帖木儿|Timurid", True)
    P(mu, "mu_umar", "乌马尔·谢赫|Umar Shaikh", "ti05")
    rows(mu, [("巴布尔|Babur", 1526, 1530, "mu01", "mu_umar"), ("胡马雍|Humayun", 1530, 1540, "mu02", "mu01"),
              ("胡马雍|Humayun", 1555, 1556, "mu02", "mu01"), ("阿克巴|Akbar", 1556, 1605, "mu03", "mu02"),
              ("贾汉吉尔|Jahangir", 1605, 1627, "mu04", "mu03")])

    sf = D("safavid", "波斯|Persia", "Persepolis", ["persis", "media"], "萨法维|Safavid", True)
    rows(sf, [("伊斯玛仪一世|Ismail I", 1501, 1524, "sf01"), ("塔赫马斯普一世|Tahmasp I", 1524, 1576, "sf02", "sf01"),
              ("伊斯玛仪二世|Ismail II", 1576, 1577, "sf03", "sf02"), ("穆罕默德·胡达班德|Mohammad Khodabanda", 1578, 1587, "sf04", "sf02")])

    # ---------------- 地中海、西亚 ----------------
    byz = find("byzantium")
    rows(byz, [("伊萨克二世|Isaac II", 1185, 1195, "", "", "安格洛斯|Angelos"), ("阿莱克修斯三世|Alexios III", 1195, 1203, "", "", "安格洛斯|Angelos"),
               ("阿莱克修斯四世|Alexios IV", 1203, 1204, "", "", "安格洛斯|Angelos"), ("阿莱克修斯五世|Alexios V", 1204, 1204),
               ("狄奥多尔一世|Theodore I", 1205, 1221, "", "", "拉斯卡里斯|Laskaris"), ("约翰三世|John III", 1221, 1254, "", "", "拉斯卡里斯|Laskaris"),
               ("狄奥多尔二世|Theodore II", 1254, 1258, "", "", "拉斯卡里斯|Laskaris"), ("约翰四世|John IV", 1258, 1261, "", "", "拉斯卡里斯|Laskaris"),
               ("米海尔八世|Michael VIII", 1259, 1282, "", "", "巴列奥略|Palaiologos"), ("安德洛尼卡二世|Andronikos II", 1282, 1328, "", "", "巴列奥略|Palaiologos"),
               ("安德洛尼卡三世|Andronikos III", 1328, 1341, "", "", "巴列奥略|Palaiologos"), ("约翰五世|John V", 1341, 1391, "", "", "巴列奥略|Palaiologos"),
               ("约翰六世|John VI", 1347, 1354, "", "", "坎塔库泽努斯|Kantakouzenos"), ("曼努埃尔二世|Manuel II", 1391, 1425, "", "", "巴列奥略|Palaiologos"),
               ("约翰八世|John VIII", 1425, 1448, "", "", "巴列奥略|Palaiologos"), ("君士坦丁十一世|Constantine XI", 1449, 1453, "", "", "巴列奥略|Palaiologos")])

    abb = find("abbasid")
    rows(abb, [("纳绥尔|al-Nasir", 1180, 1225), ("扎希尔|al-Zahir", 1225, 1226), ("穆斯坦绥尔|al-Mustansir", 1226, 1242),
               ("穆斯台绥姆|al-Musta'sim", 1242, 1258)])

    ml = D("mamluk", "马穆鲁克|Mamluk", "Arab", ["egypt"], "", True)
    rows(ml, [("艾伊贝克|Aybak", 1250, 1257, "", "", "伯海里|Bahri"), ("忽都思|Qutuz", 1259, 1260, "", "", "伯海里|Bahri"),
              ("拜巴尔斯|Baybars", 1260, 1277, "", "", "伯海里|Bahri"), ("嘉拉温|Qalawun", 1279, 1290, "", "", "嘉拉温|Qalawunid"),
              ("纳绥尔·穆罕默德|al-Nasir Muhammad", 1310, 1341, "", "", "嘉拉温|Qalawunid"), ("巴尔库克|Barquq", 1382, 1399, "", "", "布尔吉|Burji"),
              ("拜尔斯拜|Barsbay", 1422, 1438, "", "", "布尔吉|Burji"), ("盖特贝伊|Qaitbay", 1468, 1496, "", "", "布尔吉|Burji"),
              ("坎苏·高里|Qansuh al-Ghawri", 1501, 1516, "", "", "布尔吉|Burji"), ("图曼贝伊二世|Tuman Bay II", 1516, 1517, "", "", "布尔吉|Burji")])

    ot = D("ottoman", "奥斯曼|Ottoman", "Arab", ["thrace", "asia_minor"], "奥斯曼|Osman", True)
    P(ot, "ot_ertugrul", "埃尔图鲁尔|Ertuğrul")
    rows(ot, [("奥斯曼一世|Osman I", 1299, 1324, "ot01", "ot_ertugrul"), ("奥尔汗|Orhan", 1324, 1362, "ot02", "ot01"),
              ("穆拉德一世|Murad I", 1362, 1389, "ot03", "ot02"), ("巴耶济德一世|Bayezid I", 1389, 1402, "ot04", "ot03"),
              ("穆罕默德一世|Mehmed I", 1413, 1421, "ot05", "ot04"), ("穆拉德二世|Murad II", 1421, 1444, "ot06", "ot05"),
              ("穆罕默德二世|Mehmed II", 1444, 1446, "ot07", "ot06"), ("穆拉德二世|Murad II", 1446, 1451, "ot06", "ot05"),
              ("穆罕默德二世|Mehmed II", 1451, 1481, "ot07", "ot06"), ("巴耶济德二世|Bayezid II", 1481, 1512, "ot08", "ot07"),
              ("塞利姆一世|Selim I", 1512, 1520, "ot09", "ot08"), ("苏莱曼一世|Suleiman I", 1520, 1566, "ot10", "ot09"),
              ("塞利姆二世|Selim II", 1566, 1574, "ot11", "ot10"), ("穆拉德三世|Murad III", 1574, 1595, "ot12", "ot11"),
              ("穆罕默德三世|Mehmed III", 1595, 1603, "ot13", "ot12"), ("艾哈迈德一世|Ahmed I", 1603, 1617, "ot14", "ot13"),
              ("穆斯塔法一世|Mustafa I", 1617, 1618, "ot15", "ot13"), ("奥斯曼二世|Osman II", 1618, 1622, "ot16", "ot14"),
              ("穆斯塔法一世|Mustafa I", 1622, 1623, "ot15", "ot13")])

    # ---------------- 欧洲 ----------------
    hre = find("hre")
    P(hre, "hb_alb2duke", "阿尔布雷希特二世(奥地利公爵)|Albert II of Austria", "hb02")
    P(hre, "hb_alb3", "阿尔布雷希特三世(奥地利公爵)|Albert III of Austria", "hb_alb2duke")
    P(hre, "hb_alb4", "阿尔布雷希特四世(奥地利公爵)|Albert IV of Austria", "hb_alb3")
    P(hre, "hb_leo3", "利奥波德三世(奥地利公爵)|Leopold III of Austria", "hb_alb2duke")
    P(hre, "hb_ernest", "铁腕恩斯特|Ernest the Iron", "hb_leo3")
    P(hre, "hb_philip1", "美男子腓力|Philip the Handsome", "hb06")
    P(hre, "hb_charles2", "内奥地利的卡尔二世|Charles II of Inner Austria", "hb08")
    rows(hre, [("亨利六世|Henry VI", 1191, 1197, "", "", "霍亨施陶芬|Hohenstaufen"), ("奥托四世|Otto IV", 1209, 1215, "", "", "韦尔夫|Welf"),
               ("腓特烈二世|Frederick II", 1220, 1250, "", "", "霍亨施陶芬|Hohenstaufen"),
               ("鲁道夫一世|Rudolf I", 1273, 1291, "hb01", "", "哈布斯堡|Habsburg"), ("阿道夫|Adolf", 1292, 1298, "", "", "拿骚|Nassau"),
               ("阿尔布雷希特一世|Albert I", 1298, 1308, "hb02", "hb01", "哈布斯堡|Habsburg"),
               ("亨利七世|Henry VII", 1312, 1313, "", "", "卢森堡|Luxembourg"), ("路易四世|Louis IV", 1328, 1347, "", "", "维特尔斯巴赫|Wittelsbach"),
               ("查理四世|Charles IV", 1355, 1378, "", "", "卢森堡|Luxembourg"), ("文策尔|Wenceslaus", 1378, 1400, "", "", "卢森堡|Luxembourg"),
               ("鲁普雷希特|Rupert", 1400, 1410, "", "", "维特尔斯巴赫|Wittelsbach"), ("西吉斯蒙德|Sigismund", 1433, 1437, "", "", "卢森堡|Luxembourg"),
               ("阿尔布雷希特二世|Albert II", 1438, 1439, "hb_a2", "hb_alb4", "哈布斯堡|Habsburg"),
               ("腓特烈三世|Frederick III", 1452, 1493, "hb05", "hb_ernest", "哈布斯堡|Habsburg"),
               ("马克西米利安一世|Maximilian I", 1508, 1519, "hb06", "hb05", "哈布斯堡|Habsburg"),
               ("查理五世|Charles V", 1519, 1556, "hb07", "hb_philip1", "哈布斯堡|Habsburg"),
               ("斐迪南一世|Ferdinand I", 1556, 1564, "hb08", "hb_philip1", "哈布斯堡|Habsburg"),
               ("马克西米利安二世|Maximilian II", 1564, 1576, "hb09", "hb08", "哈布斯堡|Habsburg"),
               ("鲁道夫二世|Rudolf II", 1576, 1612, "hb10", "hb09", "哈布斯堡|Habsburg"),
               ("马蒂亚斯|Matthias", 1612, 1619, "hb11", "hb09", "哈布斯堡|Habsburg")])

    sp = D("spain", "西班牙|Spain", "Frankish", ["hispania_s"], "哈布斯堡|Habsburg", True)
    rows(sp, [("伊莎贝拉一世|Isabella I", 1474, 1504, "", "", "特拉斯塔马拉|Trastámara"),
              ("斐迪南二世|Ferdinand II", 1479, 1516, "", "", "特拉斯塔马拉|Trastámara"),
              ("查理一世|Charles I", 1516, 1556, "hb07", "hb_philip1"), ("腓力二世|Philip II", 1556, 1598, "sp02", "hb07"),
              ("腓力三世|Philip III", 1598, 1621, "sp03", "sp02")])

    cp = find("capetian")
    for p, n, f in [("cp_charles_valois", "瓦卢瓦的查理|Charles of Valois", "cp10"),
                    ("cp_louis_orleans", "奥尔良的路易|Louis of Orléans", "cp18"),
                    ("cp_charles_orleans", "奥尔良的查理|Charles of Orléans", "cp_louis_orleans"),
                    ("cp_john_angouleme", "昂古莱姆的约翰|John of Angoulême", "cp_louis_orleans"),
                    ("cp_charles_angouleme", "昂古莱姆的查理|Charles of Angoulême", "cp_john_angouleme"),
                    ("cp_robert_clermont", "克莱蒙的罗贝尔|Robert of Clermont", "cp09"),
                    ("cp_louis_bourbon", "波旁的路易|Louis I of Bourbon", "cp_robert_clermont"),
                    ("cp_jacques", "拉马什的雅克|James I of La Marche", "cp_louis_bourbon"),
                    ("cp_jean1", "拉马什的让|John I of La Marche", "cp_jacques"),
                    ("cp_louis_vendome", "旺多姆的路易|Louis of Vendôme", "cp_jean1"),
                    ("cp_jean8", "旺多姆的让|John VIII of Vendôme", "cp_louis_vendome"),
                    ("cp_francois", "旺多姆的弗朗索瓦|Francis of Vendôme", "cp_jean8"),
                    ("cp_charles_vendome", "旺多姆的查理|Charles of Vendôme", "cp_francois"),
                    ("cp_antoine", "安托万|Antoine of Navarre", "cp_charles_vendome")]:
        P(cp, p, n, f)
    rows(cp, [("腓力|Philip II", 1180, 1223, "cp07", "cp06"), ("路易|Louis VIII", 1223, 1226, "cp08", "cp07"),
              ("路易|Louis IX", 1226, 1270, "cp09", "cp08"), ("腓力|Philip III", 1270, 1285, "cp10", "cp09"),
              ("腓力|Philip IV", 1285, 1314, "cp11", "cp10"), ("路易|Louis X", 1314, 1316, "cp12", "cp11"),
              ("约翰|John I", 1316, 1316, "cp13", "cp12"), ("腓力|Philip V", 1316, 1322, "cp14", "cp11"),
              ("查理|Charles IV", 1322, 1328, "cp15", "cp11"),
              ("腓力|Philip VI", 1328, 1350, "cp16", "cp_charles_valois", "瓦卢瓦|Valois"),
              ("约翰|John II", 1350, 1364, "cp17", "cp16", "瓦卢瓦|Valois"), ("查理|Charles V", 1364, 1380, "cp18", "cp17", "瓦卢瓦|Valois"),
              ("查理|Charles VI", 1380, 1422, "cp19", "cp18", "瓦卢瓦|Valois"), ("查理|Charles VII", 1422, 1461, "cp20", "cp19", "瓦卢瓦|Valois"),
              ("路易|Louis XI", 1461, 1483, "cp21", "cp20", "瓦卢瓦|Valois"), ("查理|Charles VIII", 1483, 1498, "cp22", "cp21", "瓦卢瓦|Valois"),
              ("路易|Louis XII", 1498, 1515, "cp23", "cp_charles_orleans", "瓦卢瓦|Valois"),
              ("弗朗索瓦|Francis I", 1515, 1547, "cp24", "cp_charles_angouleme", "瓦卢瓦|Valois"),
              ("亨利|Henry II", 1547, 1559, "cp25", "cp24", "瓦卢瓦|Valois"), ("弗朗索瓦|Francis II", 1559, 1560, "cp26", "cp25", "瓦卢瓦|Valois"),
              ("查理|Charles IX", 1560, 1574, "cp27", "cp25", "瓦卢瓦|Valois"), ("亨利|Henry III", 1574, 1589, "cp28", "cp25", "瓦卢瓦|Valois"),
              ("亨利|Henry IV", 1589, 1610, "cp29", "cp_antoine", "波旁|Bourbon")])

    en = find("england")
    for p, n, f in [("en_black_prince", "黑太子爱德华|Edward the Black Prince", "en_e3"), ("en_gaunt", "冈特的约翰|John of Gaunt", "en_e3"),
                    ("en_langley", "兰利的埃德蒙|Edmund of Langley", "en_e3"), ("en_conisburgh", "科尼斯堡的理查|Richard of Conisburgh", "en_langley"),
                    ("en_richard_york", "约克公爵理查|Richard of York", "en_conisburgh"), ("en_edmund_tudor", "埃德蒙·都铎|Edmund Tudor", ""),
                    ("en_lennox", "伦诺克斯伯爵马修|Matthew Stewart", ""), ("en_darnley", "达恩利勋爵亨利|Henry Stuart, Lord Darnley", "en_lennox")]:
        P(en, p, n, f)
    rows(en, [("理查|Richard I", 1189, 1199, "en_r1", "en_henry2", "金雀花|Plantagenet"), ("约翰|John", 1199, 1216, "en_john", "en_henry2", "金雀花|Plantagenet"),
              ("亨利|Henry III", 1216, 1272, "en_h3", "en_john", "金雀花|Plantagenet"), ("爱德华|Edward I", 1272, 1307, "en_e1", "en_h3", "金雀花|Plantagenet"),
              ("爱德华|Edward II", 1307, 1327, "en_e2", "en_e1", "金雀花|Plantagenet"), ("爱德华|Edward III", 1327, 1377, "en_e3", "en_e2", "金雀花|Plantagenet"),
              ("理查|Richard II", 1377, 1399, "en_r2", "en_black_prince", "金雀花|Plantagenet"),
              ("亨利|Henry IV", 1399, 1413, "en_h4", "en_gaunt", "兰开斯特|Lancaster"), ("亨利|Henry V", 1413, 1422, "en_h5", "en_h4", "兰开斯特|Lancaster"),
              ("亨利|Henry VI", 1422, 1461, "en_h6", "en_h5", "兰开斯特|Lancaster"), ("爱德华|Edward IV", 1461, 1483, "en_e4", "en_richard_york", "约克|York"),
              ("理查|Richard III", 1483, 1485, "en_r3", "en_richard_york", "约克|York"), ("亨利|Henry VII", 1485, 1509, "en_h7", "en_edmund_tudor", "都铎|Tudor"),
              ("亨利|Henry VIII", 1509, 1547, "en_h8", "en_h7", "都铎|Tudor"), ("爱德华|Edward VI", 1547, 1553, "en_e6", "en_h8", "都铎|Tudor"),
              ("玛丽|Mary I", 1553, 1558, "", "", "都铎|Tudor"), ("伊丽莎白|Elizabeth I", 1558, 1603, "", "", "都铎|Tudor"),
              ("詹姆斯|James I", 1603, 1625, "en_james1", "en_darnley", "斯图亚特|Stuart")])

    dk = find("denmark")
    rows(dk, [("克努特六世|Cnut VI", 1182, 1202), ("瓦尔德马二世|Valdemar II", 1202, 1241), ("埃里克四世|Eric IV", 1241, 1250),
              ("阿贝尔|Abel", 1250, 1252), ("克里斯托弗一世|Christopher I", 1252, 1259), ("埃里克五世|Eric V", 1259, 1286),
              ("埃里克六世|Eric VI", 1286, 1319), ("克里斯托弗二世|Christopher II", 1320, 1332), ("瓦尔德马四世|Valdemar IV", 1340, 1375),
              ("奥拉夫二世|Olaf II", 1376, 1387), ("玛格丽特一世|Margaret I", 1387, 1412), ("波美拉尼亚的埃里克|Eric of Pomerania", 1396, 1439),
              ("巴伐利亚的克里斯托弗|Christopher of Bavaria", 1440, 1448),
              ("克里斯蒂安一世|Christian I", 1448, 1481, "dk_c1", "", "奥尔登堡|Oldenburg"), ("汉斯|Hans", 1481, 1513, "dk_hans", "dk_c1", "奥尔登堡|Oldenburg"),
              ("克里斯蒂安二世|Christian II", 1513, 1523, "dk_c2", "dk_hans", "奥尔登堡|Oldenburg"),
              ("腓特烈一世|Frederick I", 1523, 1533, "dk_f1", "dk_c1", "奥尔登堡|Oldenburg"),
              ("克里斯蒂安三世|Christian III", 1534, 1559, "dk_c3", "dk_f1", "奥尔登堡|Oldenburg"),
              ("腓特烈二世|Frederick II", 1559, 1588, "dk_frederick2", "dk_c3", "奥尔登堡|Oldenburg")])

    sw = find("sweden")
    rows(sw, [("马格努斯·埃里克松|Magnus Eriksson", 1319, 1364), ("阿尔布雷希特|Albert of Mecklenburg", 1364, 1389),
              ("古斯塔夫一世|Gustav I", 1523, 1560, "sw_g1", "", "瓦萨|Vasa"), ("埃里克十四世|Eric XIV", 1560, 1568, "sw_e14", "sw_g1", "瓦萨|Vasa"),
              ("约翰三世|John III", 1568, 1592, "sw_john3", "sw_g1", "瓦萨|Vasa"), ("西吉斯蒙德|Sigismund", 1592, 1599, "sw_sig", "sw_john3", "瓦萨|Vasa"),
              ("查理九世|Charles IX", 1604, 1611, "sw_charles9", "sw_g1", "瓦萨|Vasa")])

    pl = D("poland", "波兰|Poland", "Slavonic", ["poland"], "", True)
    rows(pl, [("瓦迪斯瓦夫二世|Władysław II Jagiełło", 1386, 1434, "pl_j1", "", "雅盖隆|Jagiellon"),
              ("瓦迪斯瓦夫三世|Władysław III", 1434, 1444, "pl_j2", "pl_j1", "雅盖隆|Jagiellon"),
              ("卡齐米日四世|Casimir IV", 1447, 1492, "pl_c4", "pl_j1", "雅盖隆|Jagiellon"),
              ("约翰一世·阿尔布雷希特|John I Albert", 1492, 1501, "pl_ja", "pl_c4", "雅盖隆|Jagiellon"),
              ("亚历山大|Alexander", 1501, 1506, "pl_al", "pl_c4", "雅盖隆|Jagiellon"),
              ("西吉斯蒙德一世|Sigismund I", 1507, 1548, "pl_s1", "pl_c4", "雅盖隆|Jagiellon"),
              ("西吉斯蒙德二世|Sigismund II Augustus", 1548, 1572, "pl_s2", "pl_s1", "雅盖隆|Jagiellon"),
              ("亨利|Henry of Valois", 1573, 1574, "cp28", "cp25", "瓦卢瓦|Valois"),
              ("斯特凡·巴托里|Stephen Báthory", 1576, 1586, "", "", "巴托里|Báthory")])

    ru = D("tsardom", "俄罗斯|Russia", "Slavonic", ["rus_c"], "", True)
    rows(ru, [("伊凡三世|Ivan III", 1462, 1505, "ru_i3", "", "留里克|Rurik"), ("瓦西里三世|Vasili III", 1505, 1533, "ru_v3", "ru_i3", "留里克|Rurik"),
              ("伊凡四世|Ivan IV", 1533, 1584, "ru_i4", "ru_v3", "留里克|Rurik"), ("费奥多尔一世|Feodor I", 1584, 1598, "ru_f1", "ru_i4", "留里克|Rurik"),
              ("鲍里斯·戈杜诺夫|Boris Godunov", 1598, 1605, "", "", "戈杜诺夫|Godunov"), ("费奥多尔二世|Feodor II", 1605, 1605, "", "", "戈杜诺夫|Godunov"),
              ("伪德米特里一世|False Dmitry I", 1605, 1606), ("瓦西里四世|Vasili IV", 1606, 1610, "", "", "舒伊斯基|Shuisky")])

    # 已有朝代的君主按在位起年重排(后补的接在前面补的后面)
    for d in [sg, jj, xx, byz, abb, hre, cp, en, dk, sw]:
        d["rulers"].sort(key=lambda r: r["a"])
