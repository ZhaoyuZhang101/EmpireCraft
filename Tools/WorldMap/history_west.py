"""西方及诸王国的历代君主(history_source.py 引入)：王国写进原版"历代君主"，帝国写进帝国史书，带 p/f 的接成族谱。"""


def add(D, R, P):
    def rows(d, items):
        for item in items:
            n, a, b = item[0], item[1], item[2]
            p = item[3] if len(item) > 3 else ""
            f = item[4] if len(item) > 4 else ""
            dyn = item[5] if len(item) > 5 else ""
            R(d, n, a, b, p=p, f=f, dyn=dyn)

    cp = D("capetian", "法兰西|France", "Frankish", ["gaul_n"], "卡佩|Capet", True)
    rows(cp, [("于格|Hugh", 987, 996, "cp01"), ("罗贝尔|Robert", 996, 1031, "cp02", "cp01"), ("亨利|Henry", 1031, 1060, "cp03", "cp02"),
              ("腓力|Philip", 1060, 1108, "cp04", "cp03"), ("路易|Louis VI", 1108, 1137, "cp05", "cp04"),
              ("路易|Louis VII", 1137, 1180, "cp06", "cp05")])
    en = D("england", "英格兰|England", "Germanic", ["britain_s"], "", True)
    rows(en, [("埃塞尔斯坦|Aethelstan", 927, 939, "", "", "威塞克斯|Wessex"), ("埃德加|Edgar", 959, 975, "", "", "威塞克斯|Wessex"),
              ("埃塞尔雷德|Aethelred", 978, 1016, "", "", "威塞克斯|Wessex"), ("克努特|Cnut", 1016, 1035, "", "", "克尼特林|Knýtlinga"),
              ("忏悔者爱德华|Edward the Confessor", 1042, 1066, "", "", "威塞克斯|Wessex"), ("哈罗德|Harold II", 1066, 1066, "", "", "戈德温|Godwin"),
              ("威廉|William I", 1066, 1087, "en_w1", "", "诺曼|Norman"), ("威廉|William II", 1087, 1100, "en_w2", "en_w1", "诺曼|Norman"),
              ("亨利|Henry I", 1100, 1135, "en_h1", "en_w1", "诺曼|Norman"), ("斯蒂芬|Stephen", 1135, 1154, "", "", "布卢瓦|Blois"),
              ("亨利|Henry II", 1154, 1189, "en_henry2", "en_geoffrey", "金雀花|Plantagenet")])
    P(en, "en_matilda", "玛蒂尔达|Matilda", "en_h1")
    P(en, "en_geoffrey", "若弗鲁瓦|Geoffrey Plantagenet")
    sc = D("scotland", "苏格兰|Scotland", "Frankish", ["britain_n"], "邓凯尔德|Dunkeld", True)
    rows(sc, [("肯尼思|Kenneth I", 843, 858), ("马尔科姆|Malcolm II", 1005, 1034), ("邓肯|Duncan I", 1034, 1040),
              ("麦克白|Macbeth", 1040, 1057), ("马尔科姆|Malcolm III", 1058, 1093, "sc_m3"), ("大卫|David I", 1124, 1153, "sc_d1", "sc_m3"),
              ("马尔科姆|Malcolm IV", 1153, 1165, "sc_m4", "sc_henry")])
    P(sc, "sc_henry", "亨利|Henry of Scotland", "sc_d1")
    ast = D("asturias", "阿斯图里亚斯|Asturias", "Frankish", ["hispania_n"], "佩拉约|Pelagian", True)
    rows(ast, [("佩拉约|Pelagius", 718, 737), ("法维拉|Favila", 737, 739), ("阿方索|Alfonso I", 739, 757), ("弗鲁埃拉|Fruela I", 757, 768),
               ("贝尔穆多|Bermudo I", 789, 791), ("阿方索|Alfonso II", 791, 842), ("拉米罗|Ramiro I", 842, 850),
               ("奥尔多尼奥|Ordoño I", 850, 866), ("阿方索|Alfonso III", 866, 910), ("阿方索|Alfonso VI", 1065, 1109),
               ("乌拉卡|Urraca", 1109, 1126), ("阿方索|Alfonso VII", 1126, 1157, "as_a7"),
               ("费尔南多|Ferdinand II", 1157, 1188, "as_ferdinand2", "as_a7")])
    cas = D("castile", "卡斯蒂利亚|Castile", "Frankish", ["hispania_s"], "伊夫雷亚|Ivrea", True)
    rows(cas, [("桑乔|Sancho III", 1157, 1158, "ca_sancho3", "as_a7")])
    por = D("portugal", "葡萄牙|Portugal", "Frankish", ["hispania_n"], "勃艮第|Burgundy", True)
    rows(por, [("阿方索|Afonso I", 1139, 1185, "pt01")])
    ara = D("aragon", "阿拉贡|Aragon", "Frankish", ["hispania_n"], "巴塞罗那|Barcelona", True)
    rows(ara, [("拉米罗|Ramiro I", 1035, 1063), ("佩德罗|Peter I", 1094, 1104), ("阿方索|Alfonso I", 1104, 1134),
               ("拉米罗|Ramiro II", 1134, 1137), ("佩特罗尼拉|Petronilla", 1137, 1164)])
    nav = D("navarre", "纳瓦拉|Navarre", "Frankish", ["hispania_n"], "希门尼斯|Jimenez", True)
    rows(nav, [("桑乔|Sancho III", 1004, 1035), ("加西亚|García III", 1035, 1054), ("加西亚|García Ramírez", 1134, 1150)])
    hav = D("hauteville", "西西里|Sicily", "Roma", ["sicily"], "欧特维尔|Hauteville", True)
    rows(hav, [("罗杰|Roger II", 1130, 1154, "ha_r2"), ("威廉|William I", 1154, 1166, "ha_w1", "ha_r2"),
               ("威廉|William II", 1166, 1189, "ha_w2", "ha_w1")])
    P(hav, "ha_roger3", "罗杰|Roger III", "ha_r2")
    arp = D("arpad", "匈牙利|Hungary", "Slavonic", ["pannonia"], "阿尔帕德|Arpad", True)
    rows(arp, [("斯蒂芬|Stephen I", 1000, 1038), ("安德烈|Andrew I", 1046, 1060), ("贝拉|Béla I", 1060, 1063),
               ("拉迪斯劳斯|Ladislaus I", 1077, 1095), ("卡尔曼|Coloman", 1095, 1116), ("斯蒂芬|Stephen II", 1116, 1131),
               ("贝拉|Béla II", 1131, 1141, "ar_b2"), ("盖萨|Géza II", 1141, 1162, "ar_geza2", "ar_b2"),
               ("斯蒂芬|Stephen III", 1162, 1172, "ar_s3", "ar_geza2")])
    pia = D("piast", "波兰|Poland", "Slavonic", ["poland"], "皮雅斯特|Piast", True)
    rows(pia, [("梅什科|Mieszko I", 960, 992), ("波列斯瓦夫|Bolesław I", 992, 1025), ("梅什科|Mieszko II", 1025, 1034),
               ("卡齐米日|Casimir I", 1039, 1058), ("波列斯瓦夫|Bolesław II", 1058, 1079), ("弗瓦迪斯瓦夫|Władysław I", 1079, 1102, "pi_w1"),
               ("波列斯瓦夫|Bolesław III", 1107, 1138, "pi_boleslaw3", "pi_w1"), ("弗瓦迪斯瓦夫|Władysław II", 1138, 1146, "", "pi_boleslaw3"),
               ("波列斯瓦夫|Bolesław IV", 1146, 1173, "", "pi_boleslaw3"), ("梅什科|Mieszko III", 1173, 1177, "", "pi_boleslaw3")])
    dk = D("denmark", "丹麦|Denmark", "Viking", ["denmark"], "埃斯特里德森|Estridsen", True)
    rows(dk, [("戈姆|Gorm", 936, 958, "", "", "耶灵|Jelling"), ("蓝牙哈拉尔|Harald Bluetooth", 958, 986, "", "", "耶灵|Jelling"),
              ("斯韦恩|Sweyn Forkbeard", 986, 1014, "", "", "耶灵|Jelling"), ("克努特|Cnut the Great", 1018, 1035, "", "", "耶灵|Jelling"),
              ("斯韦恩|Sweyn II", 1047, 1076), ("瓦尔德马|Valdemar I", 1157, 1182, "dk_valdemar1")])
    nw = D("norway", "挪威|Norway", "Viking", ["norway"], "", True)
    rows(nw, [("金发哈拉尔|Harald Fairhair", 872, 932), ("哈康|Haakon I", 934, 961), ("奥拉夫|Olaf I", 995, 1000),
              ("奥拉夫|Olaf II", 1015, 1028), ("哈拉尔|Harald Hardrada", 1046, 1066), ("马格努斯|Magnus III", 1093, 1103),
              ("西居尔|Sigurd I", 1103, 1130), ("马格努斯|Magnus V", 1161, 1184)])
    sw = D("sweden", "瑞典|Sweden", "Viking", ["sweden"], "埃里克|Eric", True)
    rows(sw, [("胜利者埃里克|Eric the Victorious", 970, 995), ("奥洛夫|Olof Skötkonung", 995, 1022), ("英格|Inge I", 1079, 1105),
              ("埃里克|Eric IX", 1156, 1160, "sw_eric9"), ("卡尔|Charles VII", 1161, 1167)])
    rk = D("rurikid", "基辅|Kiev", "Slavonic", ["rus_s"], "留里克|Rurik", True)
    rows(rk, [("留里克|Rurik", 862, 879, "rk01"), ("奥列格|Oleg", 879, 912), ("伊戈尔|Igor", 912, 945, "rk03", "rk01"),
              ("斯维亚托斯拉夫|Sviatoslav I", 945, 972, "rk04", "rk03"), ("弗拉基米尔|Vladimir I", 980, 1015, "rk05", "rk04"),
              ("雅罗斯拉夫|Yaroslav I", 1019, 1054, "rk06", "rk05"), ("伊贾斯拉夫|Iziaslav I", 1054, 1078, "", "rk06"),
              ("弗谢沃洛德|Vsevolod I", 1078, 1093, "rk08", "rk06"), ("斯维亚托波尔克|Sviatopolk II", 1093, 1113),
              ("弗拉基米尔·莫诺马赫|Vladimir Monomakh", 1113, 1125, "rk10", "rk08"), ("姆斯季斯拉夫|Mstislav I", 1125, 1132, "", "rk10"),
              ("尤里·多尔戈鲁基|Yuri Dolgoruky", 1155, 1157, "vs_yuri", "rk10")])
    vs = D("vladimir_suzdal", "弗拉基米尔|Vladimir", "Slavonic", ["rus_c"], "留里克|Rurik", True)
    rows(vs, [("安德烈·博戈柳布斯基|Andrey Bogolyubsky", 1157, 1174, "", "vs_yuri"), ("米哈伊尔|Mikhail", 1174, 1176, "", "vs_yuri")])
    bg = D("first_bulgaria", "保加利亚|Bulgaria", "Youmu", ["moesia"], "杜洛|Dulo", True)
    rows(bg, [("阿斯帕鲁赫|Asparukh", 681, 700), ("特尔维尔|Tervel", 700, 721), ("科尔梅西|Kormesiy", 721, 738),
              ("塞文|Sevar", 738, 753), ("泰莱茨|Telets", 762, 765), ("卡尔达姆|Kardam", 777, 803), ("克鲁姆|Krum", 803, 814),
              ("奥穆尔塔格|Omurtag", 814, 831), ("鲍里斯|Boris I", 852, 889), ("西美昂|Simeon I", 893, 927), ("彼得|Peter I", 927, 969),
              ("萨穆伊尔|Samuel", 997, 1014)])
    ge = D("georgia", "格鲁吉亚|Georgia", "Roma", ["caucasus"], "巴格拉季昂|Bagrationi", True)
    rows(ge, [("巴格拉特|Bagrat III", 1008, 1014), ("大卫|David IV", 1089, 1125, "ge01"), ("德米特里|Demetrius I", 1125, 1156, "ge02", "ge01"),
              ("格奥尔基|George III", 1156, 1184, "ge03", "ge02")])
    jr = D("jerusalem", "耶路撒冷|Jerusalem", "Frankish", ["palestine"], "", True)
    rows(jr, [("戈弗雷|Godfrey", 1099, 1100), ("鲍德温|Baldwin I", 1100, 1118), ("鲍德温|Baldwin II", 1118, 1131),
              ("富尔克|Fulk", 1131, 1143), ("鲍德温|Baldwin III", 1143, 1163), ("阿马里克|Amalric", 1163, 1174),
              ("鲍德温|Baldwin IV", 1174, 1185), ("鲍德温|Baldwin V", 1185, 1186), ("西比拉|Sibylla", 1186, 1190)])
    sj = D("seljuk", "塞尔柱|Seljuk", "Persepolis", ["media", "khorasan"], "塞尔柱|Seljuk", True)
    rows(sj, [("图格鲁勒|Tughril", 1037, 1063), ("阿尔普·阿尔斯兰|Alp Arslan", 1063, 1072), ("马立克沙|Malik-Shah I", 1072, 1092),
              ("巴尔基雅鲁克|Berkyaruq", 1094, 1105), ("穆罕默德|Muhammad I", 1105, 1118), ("桑贾尔|Sanjar", 1118, 1157),
              ("阿尔斯兰沙|Arslan Shah", 1161, 1176)])
    rm = D("seljuk_rum", "罗姆|Rum", "Youmu", ["cappadocia"], "塞尔柱|Seljuk", True)
    rows(rm, [("苏莱曼|Suleiman I", 1077, 1086, "rm01"), ("基利杰·阿尔斯兰|Kilij Arslan I", 1092, 1107, "rm02", "rm01"),
              ("马立克沙|Malik Shah", 1110, 1116, "", "rm02"), ("麦苏德|Mesud I", 1116, 1156, "rm04", "rm02")])
    kh = D("khwarazm", "花剌子模|Khwarazm", "Persepolis", ["khwarazm"], "阿努什特勤|Anushtegin", True)
    rows(kh, [("库特布丁·穆罕默德|Qutb al-Din Muhammad", 1097, 1127, "kh01"), ("阿即思|Atsiz", 1127, 1156, "kh02", "kh01"),
              ("伊勒·阿尔斯兰|Il-Arslan", 1156, 1172, "kh03", "kh02")])
    ay = D("ayyubid", "阿尤布|Ayyubid", "Arab", ["egypt"], "阿尤布|Ayyubid", True)
    P(ay, "ay_ayyub", "纳吉姆丁·阿尤布|Najm al-Din Ayyub")
    lb = D("lombards", "伦巴第|Lombards", "Roma", ["italy_n"], "", True)
    rows(lb, [("阿尔博因|Alboin", 568, 572), ("奥塔里|Authari", 584, 590), ("阿吉卢尔夫|Agilulf", 591, 616), ("罗萨里|Rothari", 636, 652),
              ("格里莫尔德|Grimoald", 662, 671), ("利乌特普兰德|Liutprand", 712, 744), ("拉奇斯|Ratchis", 744, 749)])
    mc = D("mercia", "麦西亚|Mercia", "Germanic", ["britain_s"], "伊克林|Iclingas", True)
    rows(mc, [("彭达|Penda", 626, 655), ("伍尔夫希尔|Wulfhere", 658, 675), ("埃塞尔雷德|Aethelred", 675, 704),
              ("切奥尔雷德|Ceolred", 709, 716), ("埃塞尔博尔德|Aethelbald", 716, 757), ("奥法|Offa", 757, 796)])
    wx = D("wessex", "威塞克斯|Wessex", "Germanic", ["britain_s"], "威塞克斯|Wessex", True)
    rows(wx, [("切尔迪克|Cerdic", 519, 534), ("卡德瓦拉|Caedwalla", 685, 688), ("伊内|Ine", 688, 726),
              ("埃塞尔赫德|Aethelheard", 726, 740), ("卡思雷德|Cuthred", 740, 756), ("贝奥赫特里克|Beorhtric", 786, 802)])
    cd = D("cordoba", "科尔多瓦|Cordoba", "Arab", ["hispania_s"], "倭马亚|Umayyad", True)
    rows(cd, [("阿卜杜·拉赫曼|Abd al-Rahman I", 756, 788, "cd01"), ("希沙姆|Hisham I", 788, 796, "cd02", "cd01")])
