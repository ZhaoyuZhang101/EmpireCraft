"""1937 日军侵华剧本的王国法理与帝国法理：照玩家做的【帝盒法理地图】1937东亚抗日战争(32×23)提取(用户 2026-11-10
"东亚的也可以参考这个玩家做好的现成的""主要是法理划分")。省为王国法理(陕甘凉梁川滇……)，华北华东华中华南华西东北为帝国法理；
只收东亚，去掉了挂错城的龟兹、拿国名当法理的冀东、划进华南的越南。由 extract 脚本生成，改要手改这里。"""

TITLES = [
 {
  "id": "dj_shaan",
  "zh": "陕",
  "en": "Shaan",
  "prov": "陕西",
  "prov_en": "Shaanxi",
  "cities": [
   "baoji",
   "xian",
   "yanan"
  ],
  "share": [],
  "cap": "yanan"
 },
 {
  "id": "dj_gan",
  "zh": "甘",
  "en": "Gan",
  "prov": "甘肃",
  "prov_en": "Gansu",
  "cities": [
   "lanzhou",
   "tianshui"
  ],
  "share": [
   "jiuquan",
   "wuwei",
   "zhangye"
  ],
  "cap": "tianshui"
 },
 {
  "id": "dj_liang",
  "zh": "凉",
  "en": "Liang",
  "prov": "河西",
  "prov_en": "Hexi",
  "cities": [
   "dunhuang",
   "jiuquan",
   "wuwei",
   "zhangye"
  ],
  "share": [],
  "cap": "wuwei"
 },
 {
  "id": "dj_hanzhong",
  "zh": "梁",
  "en": "Liang",
  "prov": "汉中",
  "prov_en": "Hanzhong",
  "cities": [
   "hanzhong"
  ],
  "share": [],
  "cap": "hanzhong"
 },
 {
  "id": "dj_chuan",
  "zh": "川",
  "en": "Chuan",
  "prov": "四川",
  "prov_en": "Sichuan",
  "cities": [
   "chengdu",
   "chongqing",
   "langzhong"
  ],
  "share": [
   "hanzhong"
  ],
  "cap": "chengdu"
 },
 {
  "id": "dj_dian",
  "zh": "滇",
  "en": "Dian",
  "prov": "云南",
  "prov_en": "Yunnan",
  "cities": [
   "dali",
   "kunming"
  ],
  "share": [],
  "cap": "kunming"
 },
 {
  "id": "dj_jin",
  "zh": "晋",
  "en": "Jin",
  "prov": "山西",
  "prov_en": "Shanxi",
  "cities": [
   "anyi",
   "changzhi",
   "datong",
   "linfen",
   "taiyuan"
  ],
  "share": [],
  "cap": "taiyuan"
 },
 {
  "id": "dj_yu",
  "zh": "豫",
  "en": "Yu",
  "prov": "河南",
  "prov_en": "Henan",
  "cities": [
   "chen",
   "kaifeng",
   "luoyang",
   "nanyang",
   "xinzheng",
   "xuchang"
  ],
  "share": [
   "ye"
  ],
  "cap": "luoyang"
 },
 {
  "id": "dj_zhao",
  "zh": "赵",
  "en": "Zhao",
  "prov": "冀南",
  "prov_en": "Southern Hebei",
  "cities": [
   "dingzhou",
   "handan",
   "ye",
   "zhending"
  ],
  "share": [],
  "cap": "handan"
 },
 {
  "id": "dj_ji",
  "zh": "冀",
  "en": "Ji",
  "prov": "河北",
  "prov_en": "Hebei",
  "cities": [
   "beijing",
   "shanhaiguan",
   "tianjin",
   "zhangjiakou",
   "zhuozhou"
  ],
  "share": [
   "dingzhou",
   "handan",
   "zhending"
  ],
  "cap": "beijing"
 },
 {
  "id": "dj_liao",
  "zh": "辽",
  "en": "Liao",
  "prov": "辽宁",
  "prov_en": "Liaoning",
  "cities": [
   "dalian",
   "jinzhou",
   "liaoyang",
   "ningyuan",
   "shenyang"
  ],
  "share": [],
  "cap": "dalian"
 },
 {
  "id": "dj_lu",
  "zh": "鲁",
  "en": "Lu",
  "prov": "山东",
  "prov_en": "Shandong",
  "cities": [
   "jimo",
   "jinan",
   "linzi",
   "qufu"
  ],
  "share": [],
  "cap": "jinan"
 },
 {
  "id": "dj_xu",
  "zh": "徐",
  "en": "Xu",
  "prov": "江苏",
  "prov_en": "Jiangsu",
  "cities": [
   "nanjing",
   "xuzhou",
   "yangzhou"
  ],
  "share": [],
  "cap": "nanjing"
 },
 {
  "id": "dj_e",
  "zh": "鄂",
  "en": "E",
  "prov": "湖北",
  "prov_en": "Hubei",
  "cities": [
   "jiangling",
   "wuchang",
   "xiangyang",
   "yichang"
  ],
  "share": [],
  "cap": "xiangyang"
 },
 {
  "id": "dj_zhe",
  "zh": "浙",
  "en": "Zhe",
  "prov": "浙江",
  "prov_en": "Zhejiang",
  "cities": [
   "hangzhou",
   "shanghai",
   "shaoxing",
   "suzhou"
  ],
  "share": [],
  "cap": "suzhou"
 },
 {
  "id": "dj_gan2",
  "zh": "赣",
  "en": "Gan",
  "prov": "江西",
  "prov_en": "Jiangxi",
  "cities": [
   "ganzhou",
   "nanchang"
  ],
  "share": [],
  "cap": "nanchang"
 },
 {
  "id": "dj_min",
  "zh": "闽",
  "en": "Min",
  "prov": "福建",
  "prov_en": "Fujian",
  "cities": [
   "fuzhou",
   "quanzhou"
  ],
  "share": [],
  "cap": "fuzhou"
 },
 {
  "id": "dj_yue",
  "zh": "粤",
  "en": "Yue",
  "prov": "广东",
  "prov_en": "Guangdong",
  "cities": [
   "guangzhou",
   "hepu",
   "leizhou"
  ],
  "share": [],
  "cap": "guangzhou"
 },
 {
  "id": "dj_gui",
  "zh": "桂",
  "en": "Gui",
  "prov": "广西",
  "prov_en": "Guangxi",
  "cities": [
   "guilin",
   "liuzhou",
   "nanning",
   "wuzhou"
  ],
  "share": [],
  "cap": "guilin"
 },
 {
  "id": "dj_ning",
  "zh": "宁",
  "en": "Ning",
  "prov": "宁夏",
  "prov_en": "Ningxia",
  "cities": [
   "baotou",
   "yinchuan"
  ],
  "share": [],
  "cap": "yinchuan"
 },
 {
  "id": "dj_monan",
  "zh": "漠南",
  "en": "Monan",
  "prov": "漠南",
  "prov_en": "Inner Mongolia",
  "cities": [
   "dehua",
   "hohhot"
  ],
  "share": [],
  "cap": "dehua"
 },
 {
  "id": "dj_khalkha",
  "zh": "喀尔喀",
  "en": "Khalkha",
  "prov": "喀尔喀",
  "prov_en": "Khalkha",
  "cities": [
   "kerulen",
   "khovd",
   "onon",
   "ordu",
   "tuul"
  ],
  "share": [],
  "cap": "tuul"
 },
 {
  "id": "dj_sushen",
  "zh": "肃慎",
  "en": "Sushen",
  "prov": "外东北",
  "prov_en": "Outer Manchuria",
  "cities": [
   "khabarovsk",
   "vladivostok"
  ],
  "share": [],
  "cap": "vladivostok"
 },
 {
  "id": "dj_joseon",
  "zh": "朝鲜",
  "en": "Joseon",
  "prov": "朝鲜北部",
  "prov_en": "Northern Korea",
  "cities": [
   "pyongyang"
  ],
  "share": [],
  "cap": "pyongyang"
 },
 {
  "id": "dj_jinhan",
  "zh": "辰韩",
  "en": "Jinhan",
  "prov": "朝鲜南部",
  "prov_en": "Southern Korea",
  "cities": [
   "busan",
   "buyeo",
   "gyeongju",
   "hanseong",
   "kaesong"
  ],
  "share": [],
  "cap": "gyeongju"
 },
 {
  "id": "dj_tsukushi",
  "zh": "筑紫",
  "en": "Tsukushi",
  "prov": "九州",
  "prov_en": "Kyushu",
  "cities": [
   "dazaifu",
   "nagasaki"
  ],
  "share": [],
  "cap": "dazaifu"
 },
 {
  "id": "dj_yamato",
  "zh": "大和",
  "en": "Yamato",
  "prov": "畿内",
  "prov_en": "Kinai",
  "cities": [
   "hiroshima",
   "izumo",
   "kyoto",
   "nagoya",
   "nara",
   "osaka"
  ],
  "share": [],
  "cap": "nara"
 },
 {
  "id": "dj_musashi",
  "zh": "武藏",
  "en": "Musashi",
  "prov": "关东",
  "prov_en": "Kanto",
  "cities": [
   "edo",
   "kamakura"
  ],
  "share": [],
  "cap": "edo"
 },
 {
  "id": "dj_mutsu",
  "zh": "陆奥",
  "en": "Mutsu",
  "prov": "东北",
  "prov_en": "Tohoku",
  "cities": [
   "taga"
  ],
  "share": [],
  "cap": "taga"
 },
 {
  "id": "dj_ezo",
  "zh": "虾夷",
  "en": "Ezo",
  "prov": "北海道",
  "prov_en": "Hokkaido",
  "cities": [
   "sapporo"
  ],
  "share": [],
  "cap": "sapporo"
 },
 {
  "id": "dj_kyrgyz",
  "zh": "坚昆",
  "en": "Kyrgyz",
  "prov": "西伯利亚",
  "prov_en": "Siberia",
  "cities": [
   "chita",
   "irkutsk",
   "kyrgyz",
   "novosibirsk",
   "tobolsk"
  ],
  "share": [],
  "cap": "irkutsk"
 },
 {
  "id": "dj_tibet",
  "zh": "西藏",
  "en": "Tibet",
  "prov": "西藏",
  "prov_en": "Tibet",
  "cities": [
   "lhasa",
   "qonggyai",
   "shigatse"
  ],
  "share": [],
  "cap": "lhasa"
 },
 {
  "id": "dj_qinghai",
  "zh": "青海",
  "en": "Qinghai",
  "prov": "青海",
  "prov_en": "Qinghai",
  "cities": [
   "kokonor",
   "xining"
  ],
  "share": [
   "dunhuang"
  ],
  "cap": "xining"
 },
 {
  "id": "dj_xinjiang",
  "zh": "新疆",
  "en": "Xinjiang",
  "prov": "新疆",
  "prov_en": "Xinjiang",
  "cities": [
   "hami",
   "hobok",
   "ili",
   "kashgar",
   "khotan",
   "kucha",
   "turpan",
   "urumqi",
   "yarkand"
  ],
  "share": [],
  "cap": "urumqi"
 },
 {
  "id": "dj_gang",
  "zh": "港",
  "en": "Hong Kong",
  "prov": "香港",
  "prov_en": "Hong Kong",
  "cities": [
   "hongkong"
  ],
  "share": [],
  "cap": "hongkong"
 },
 {
  "id": "dj_xiang",
  "zh": "湘",
  "en": "Xiang",
  "prov": "湖南",
  "prov_en": "Hunan",
  "cities": [
   "changsha",
   "hengyang"
  ],
  "share": [],
  "cap": "changsha"
 },
 {
  "id": "dj_ao",
  "zh": "澳",
  "en": "Macau",
  "prov": "澳门",
  "prov_en": "Macau",
  "cities": [
   "macau"
  ],
  "share": [],
  "cap": "macau"
 },
 {
  "id": "dj_qian",
  "zh": "黔",
  "en": "Qian",
  "prov": "贵州",
  "prov_en": "Guizhou",
  "cities": [
   "guiyang"
  ],
  "share": [],
  "cap": "guiyang"
 },
 {
  "id": "dj_wan",
  "zh": "皖",
  "en": "Wan",
  "prov": "安徽",
  "prov_en": "Anhui",
  "cities": [
   "shouchun"
  ],
  "share": [],
  "cap": "shouchun"
 },
 {
  "id": "dj_cha",
  "zh": "察",
  "en": "Cha",
  "prov": "热察",
  "prov_en": "Rehe-Chahar",
  "cities": [
   "chaghan",
   "chengde",
   "hailar"
  ],
  "share": [],
  "cap": "chengde"
 },
 {
  "id": "dj_jilin",
  "zh": "吉",
  "en": "Ji",
  "prov": "吉林",
  "prov_en": "Jilin",
  "cities": [
   "fuyu",
   "hetuala",
   "jilin",
   "ninguta"
  ],
  "share": [],
  "cap": "fuyu"
 },
 {
  "id": "dj_hei",
  "zh": "黑",
  "en": "Hei",
  "prov": "黑龙江",
  "prov_en": "Heilongjiang",
  "cities": [
   "harbin",
   "heihe",
   "qiqihar",
   "yaksa"
  ],
  "share": [],
  "cap": "harbin"
 },
 {
  "id": "dj_qiong",
  "zh": "琼",
  "en": "Qiong",
  "prov": "海南",
  "prov_en": "Hainan",
  "cities": [
   "danzhou",
   "lingao",
   "qiongzhou",
   "yazhou"
  ],
  "share": [],
  "cap": "qiongzhou"
 },
 {
  "id": "dj_tai",
  "zh": "台",
  "en": "Tai",
  "prov": "台湾",
  "prov_en": "Taiwan",
  "cities": [
   "keelung",
   "tayouan"
  ],
  "share": [],
  "cap": "keelung"
 }
]

CORES = [
 {
  "id": "dj_huabei",
  "zh": "华北",
  "en": "North China",
  "tx": "Huaxia",
  "r": [
   "dj_ji",
   "dj_jin"
  ],
  "cap": "beijing"
 },
 {
  "id": "dj_huazhong",
  "zh": "华中",
  "en": "Central China",
  "tx": "Huaxia",
  "r": [
   "dj_yu",
   "dj_zhao",
   "dj_e",
   "dj_wan",
   "dj_xiang",
   "dj_gan2"
  ],
  "cap": "luoyang"
 },
 {
  "id": "dj_huadong",
  "zh": "华东",
  "en": "East China",
  "tx": "Huaxia",
  "r": [
   "dj_zhe",
   "dj_xu",
   "dj_lu"
  ],
  "cap": "nanjing"
 },
 {
  "id": "dj_huanan",
  "zh": "华南",
  "en": "South China",
  "tx": "Huaxia",
  "r": [
   "dj_yue",
   "dj_gui",
   "dj_qiong",
   "dj_ao",
   "dj_gang",
   "dj_min",
   "dj_tai"
  ],
  "cap": "guangzhou"
 },
 {
  "id": "dj_huaxi",
  "zh": "华西",
  "en": "West China",
  "tx": "Huaxia",
  "r": [
   "dj_shaan",
   "dj_gan",
   "dj_liang",
   "dj_ning",
   "dj_hanzhong",
   "dj_chuan",
   "dj_qian",
   "dj_dian"
  ],
  "cap": "yanan"
 },
 {
  "id": "dj_dongbei",
  "zh": "东北",
  "en": "Northeast",
  "tx": "Huaxia",
  "r": [
   "dj_jilin",
   "dj_liao",
   "dj_hei",
   "dj_cha"
  ],
  "cap": "fuyu"
 },
 {
  "id": "dj_xinjiang",
  "zh": "新疆",
  "en": "Xinjiang",
  "tx": "Huaxia",
  "r": [
   "dj_xinjiang"
  ],
  "cap": "urumqi"
 },
 {
  "id": "dj_mongolia",
  "zh": "蒙古",
  "en": "Mongolia",
  "tx": "Steppe",
  "r": [
   "dj_khalkha",
   "dj_monan",
   "dj_kyrgyz"
  ],
  "cap": "tuul"
 },
 {
  "id": "dj_tubo",
  "zh": "吐蕃",
  "en": "Tibet",
  "tx": "Steppe",
  "r": [
   "dj_tibet",
   "dj_qinghai"
  ],
  "cap": "lhasa"
 },
 {
  "id": "dj_waidongbei",
  "zh": "外东北",
  "en": "Outer Manchuria",
  "tx": "Steppe",
  "r": [
   "dj_sushen"
  ],
  "cap": "fuyu"
 },
 {
  "id": "dj_korea",
  "zh": "朝鲜",
  "en": "Korea",
  "tx": "Huaxia",
  "r": [
   "dj_joseon",
   "dj_jinhan"
  ],
  "cap": "pyongyang"
 },
 {
  "id": "dj_japan",
  "zh": "日本",
  "en": "Japan",
  "tx": "",
  "r": [
   "dj_yamato",
   "dj_tsukushi",
   "dj_musashi",
   "dj_mutsu",
   "dj_ezo"
  ],
  "cap": "edo"
 }
]
