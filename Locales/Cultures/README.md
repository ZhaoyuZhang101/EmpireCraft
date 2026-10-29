# 文化包说明

每个文化就是这里的一个文件夹 `Culture_<文化id>/`，**加文化、改词库都不需要改代码**。
推荐直接用 `RegimeEditor/启动桌面版.bat` 里的「文化编辑器」，下面是文件格式，手写也可以。

```
Locales/Cultures/
├─ Culture_Huaxia/
│  ├─ CultureRule.jsonc            规则：名称、颜色、物种、政体、命名规则……
│  ├─ HuaxiaCityNames1.csv        各种词库，文件名 = 文化id + 词库名
│  ├─ HuaxiaBookTemplatesHistoryBook.csv
│  ├─ icon.png                     (可选)文化图标，文化配置窗口里显示；没有就用模组内置图标或默认白旗
│  └─ ...
├─ Books/                         书名的通用词库(文化没配的类型用这里)
├─ Wilderness/                    无人区(山脉、沙漠、冻原、雨林、大泽)命名的通用词库
└─ PartyNames/                    党名、共和国号后缀、非政府势力称呼、临时政府称呼的默认词库
```

文化图标建议 28×28 像素、透明背景，风格参照 `Tools/IconForge/STYLE.md`(深褐描边、右下投影、左上受光)。
模组内置的文化图标在 `GameResources/ui/icons/cultures/`，由 `Tools/IconForge/icon_forge.py` 生成。

## CultureRule.jsonc

允许 `//` 注释。只有 `name` 和 `setting` 是必须的，其余都可以省略。

```jsonc
{
    "name": "Korea",                 // 文化 id，和文件夹名 Culture_Korea 一致
    "color": "#3A7CA5",              // 文化图层颜色
    "translate_cz": "朝鲜",           // 简体中文名
    "translate_en": "Korean",
    "translate_ch": "朝鮮",           // 繁体中文名
    "species": ["civ_crab"],         // 默认属于本文化的物种 id(可选；玩家的 CultureSpeciesPairPlayerConfig.json 仍优先)
    "setting": {
        "regime": "LvLing",          // 默认政体，对应 Scripts/Regimes/Configs/<政体>/
        "institution_line": "Huaxia",// 制度科技线，对应 InstitutionTrees/<线>.json；留空用默认线
        "tianxia": "Huaxia",// 天下标识：写同一标识的文化同属一个天下，相邻的帝国法理自动并成一个天下；留空 = 本文化自成一个天下
        "archipelago": false,// 列岛天下(可省略)：true 时隔着海峡、在水师辐射里的同标识天下也自动相并，帝国法理也收隔海的同文化法理(日本用)
        "start_institution": "",     // 开局阶段：本线的一个制度节点，开局即施行它和它的全部前置；留空从根节点起步(华夏线是原始群)
        "faith": "",                 // 本文化创的宗教的类型(可省略，见 Scripts/GeneralSystems/ReligionFaith.cs)：Folk 多神民间、Church 普世教会、
                                     // Islam 伊斯兰、Judaism 犹太、Dharmic 法教、Sanjiao 华夏三教、Shinto 神道、Tengri 草原萨满；
                                     // 留空按天下标识取(Roma→Church、Umma→Islam、Kosher→Judaism、India→Dharmic、Huaxia→Sanjiao、Tenka→Shinto、Steppe→Tengri，其余 Folk)
        "surname_evolution": true,   // 姓氏演变(只对华夏线有效)：原始群无姓→母系有姓→贵族分氏→周制男子称氏女子称姓、平民无氏→郡县后姓氏合一
        "traits": ["patriarchy"],    // 文化特质 id
        "political_traits": ["strong_unification"], // 模组政治特质：strong_unification 强统一诉求(唯一中央、易帜、临时政府、统一继承)；
                                     // nomad 游牧(游牧扩张、牧帐、放牧加倍)；raider 劫掠传统(派系可提劫掠)；
                                     // dharmic 法教传统(不发起圣战、宗教宽容)；dry_native 旱地故土(适应旱地与农地，不按制度线推定)
        "Religion": "ReligionNames", // 宗教名词库
        "City":    { "groups": { "group_1": "CityNames1" }, "rule": ["group_1"], "name_pos": 0 },
        "Kingdom": { "groups": { "group_1": "CountryNames" }, "rule": ["group_1"], "name_pos": 0, "english_type_prefix": false },
        "Clan":    { ... }, "Family": { ... }, "Unit": { ... },
        "Party":   { "groups": { "Socialism": "PartySocialism" }, "suffix_groups": {},
                     "untitled_groups": { "Communism": "UntitledCommunism", "Centrism": "UntitledWarlord" },
                     "provisional_groups": {} }
    }
}
```

### 命名规则(City / Kingdom / Clan / Family / Unit)

- `groups`：组名 → 词库名。词库文件是本文件夹里的 `<文化id><词库名>.csv`。值写成细空格 `" "` 表示分隔符。
- `rule`：按顺序拼接。可以写组名，也可以写 `sex_male` / `sex_female`(按性别过滤)、`coin_flip`(随机二选一)、`space` 等，
  完整列表见 `Scripts/Enums/OnomasticsType.cs`。
- `name_pos`：名字按空格拆开后，第几段是“有效名字”(从 0 数)。比如 “XX 市” 是 0，“钢铁 德莱尔” 取“德莱尔”是 1。
- Clan / Family：`has_sex_post` 姓氏有阴阳词性，`use_local_as_lastname` 名在前姓在后，
  `sex_post_Male` / `sex_post_Female` 为 [简体, 英文, 繁体] 的后缀。Unit：`is_invert` 名在前(西式)。
- Clan：`ancient_group` 古姓词库(可写“别的文化:词库名”)，原始群、母系氏族阶段新立的氏族从这里取姓，华夏配的是姬、姜、姒、嬴这类女字旁古姓
  (`HuaxiaAncientSurnames.csv`)；不配就照常起姓。
- Clan：`shi_office_group` / `shi_craft_group` 氏的词库：支脉起氏时做官的族人一半以官为氏(司马、司徒)，平民三成以职业为氏(陶、巫、匠)，
  其余以受封的国、所居的邑、房祖的名的首字为氏。
- Unit：`ancient_male_group` / `ancient_female_group` 上古人名词库；Kingdom：`ancient_group` 部落、方国国号词库。
  只在华夏线还没到宗法(原始群至家天下)时使用，不配就照常起名。
- Kingdom：`english_type_prefix` 英文界面用 “The {类别} of {国名}”，否则 “{国名} {类别}”。

### 村镇(Settlement)

城外的村、镇、关城按**所在城的主流文化**起名，起好记进存档(改过名的不再动)。三种命名方式按分量随机挑：

| 方式 | 拼法 | 例子 |
|---|---|---|
| `surname` 姓氏 | 本城宗族的姓(没有就取本文化 Family 的姓氏词库) + 姓氏后缀 | 罗家庄、伊万诺夫卡、Luojiazhuang |
| `terrain` 地势 | 前缀 + 地势词 + 后缀；地势看村子四周：mountain 山、hill 丘岗、water 河湖、coast 海边、forest 林、plain 平原、desert 沙漠、swamp 沼泽 | 青石山村、柳河村、Takayama-mura |
| `words` 字库 | 字库词 + 后缀(字库可以是整名，后缀写 `_` 表示不加) | 桃花村、阿什福德村、Ashford |

```jsonc
"Settlement": {
    "village_modes": { "surname": 3, "terrain": 2, "words": 1 },   // 村：各方式的分量
    "town_modes": { "terrain": 1, "words": 2 },                    // 镇
    "fort_modes": { "terrain": 1, "words": 1 },                    // 关城
    "village_words": "VillageWords",     // 字库方式的词(可写"别的文化:词库名")
    "village_suffix": "VillageSuffix",   // 村的后缀(村、庄、湾……)
    "surname_suffix": "SurnameSuffix",   // 姓氏方式的后缀(家庄、家村……)
    "surname_words": "",                 // 本城没有宗族时的姓氏词库(留空用 Family 的第一个词库)
    "town_words": "TownWords", "town_suffix": "TownSuffix",
    "fort_words": "TownWords", "fort_suffix": "FortSuffix",
    "terrain_prefix": "TerrainPrefix",   // 地势前面的词(青、白石、Ash……)
    "terrain_suffix": "TerrainSuffix",   // 地势村的后缀
    "terrain": { "mountain": "TerrainMountain", "water": "TerrainWater" },  // 各地势的地势词，没写的地势用通用词库
    "yurt_village_modes": { "surname": 1, "words": 2 },             // 毡帐村：各方式的分量(没配照村的)
    "yurt_town_modes": { "words": 1 },                             // 毡帐镇(没配照镇的)
    "yurt_words": "YurtWords",                   // 毡帐字库方式的词(没配照村、镇的字库)
    "yurt_village_suffix": "YurtVillageSuffix",  // 毡帐村的后缀(营、帐……)，姓氏、地势方式也接它
    "yurt_town_suffix": "YurtTownSuffix"         // 毡帐镇的后缀(大营、营盘……)
}
```

- 词库同其它词库：本文件夹 `<文化id><词库名>.csv`(key,cz,en,ch)，直接读当前语言那一列；
  英文一列以 `_` 开头表示前面加空格(如 `_Farm`)，单写 `_` 表示空(不加后缀)。
- 没配的项用 `Settlements/` 下的通用词库(汉字与英文雅名)；整个 `Settlement` 都没配的文化，村、镇、关一律按姓氏起(某某村、某某镇)。
- 毡帐村镇(游牧营地的城在旱地上立的)单起一套：分量、字库、后缀都可以另配，没配的照村、镇的分量与字库，后缀用通用的
  `Settlements/YurtVillageSuffix`(营、帐、营子)、`YurtTownSuffix`(大营、营盘、大帐)，名字与农耕村镇分得开。
- 现成的配置：华夏(中国、山海引用华夏的词库，英文是拼音)、日本(罗马字)、西方、法兰克、日耳曼(整名字库)、斯拉夫(姓 + 卡)、维京(姓 + 比)；
  游牧只配了毡帐村镇(察罕、乌兰这类词 + 营、帐、艾里，镇是波罗、大营、营盘)。

### 无人区(Wilderness)

连片的山脉、沙漠、冻原、雨林、大泽划成的无人区，按**离区域最近的城的主流文化**起名：名字 = 地名 + 类型后缀，
如 太行 + 山脉、撒哈拉 + 大沙漠、北海 + 冰原、云梦 + 泽，英文 Taihang Mountains、Sahara Desert(代码见 `Scripts/GeneralSystems/WildernessNaming.cs`)。

```jsonc
"Wilderness": {
    // 各类型的地名词库：mountain 山脉、desert 沙漠、tundra 冻原、jungle 雨林、swamp 大泽(可写"别的文化:词库名")
    "words": { "mountain": "WildMountain", "desert": "WildDesert", "tundra": "WildTundra", "jungle": "WildJungle", "swamp": "WildSwamp" },
    "suffix": { "mountain": "WildMountainSuffix" },   // 类型后缀(山脉、山……)，没配的用通用的
    "great_suffix": { "swamp": "WildSwampGreat" },    // 很大的区域用的后缀(大泽、大沙漠……)，没配的照本文化的 suffix
    "directions": ""                                   // 方位字头(北{0}、南{0}……)，没配用通用的
}
```

- 词库同其它词库：本文件夹 `<文化id><词库名>.csv`(key,cz,en,ch)，直接读当前语言那一列；英文后缀以 `_` 开头表示前面加空格(`_Mountains`)。
- 后缀里写 `{0}` 的代入地名：`{0}的旷野` → 寻的旷野、`Wilderness of {0}`、`Mount {0}`、`Great {0} Desert`；不写就接在地名后面。
- 地名末尾写 `!` 表示整名、不接后缀(`秦岭!`、`瀚海!`、`Alps!`)；地名已以本类型的某个后缀结尾的也不再接(燕山 + 山 → 燕山)。
  每一列各算各的：简体 `秦岭!` 是整名，英文 `Qinling` 照样接成 Qinling Mountains。
- 很大的区域(100 个区块以上)用 `great_suffix`：本文化没配的照本文化的 `suffix`，两样都没配用通用的 `Wild<类型>Great`
  (撒哈拉大沙漠、西伯利亚大冰原、Great Sahara Desert)；山脉没有大字头，照常用 `山脉`。
- 不重名：本世界已用过的名字避开换一个地名(同一地名换个后缀也算用过，太行山脉用了不再起太行山)；地名用完了加方位字头
  (北、南、东、西、大，字头已在名字里的不加，如北海冰原不再加北)，再不够加序号(太行山脉二、Taihang Mountains II)。
- 同一个区域每次起出同一个名字(按区域的种子洗牌、取后缀，不用随机数)。
- 整个 `Wilderness` 没配、或某个类型没配的，用 `Wilderness/` 下的通用词库：`Wild<类型>.csv` 是世界各地的真实地名
  (安第斯、撒哈拉、西伯利亚、亚马孙、潘塔纳尔……)，`Wild<类型>Suffix.csv` / `Wild<类型>Great.csv` 是后缀，`WildDirections.csv` 是方位字头。
- 现成的配置：华夏(太行、昆仑、塔克拉玛干、瀚海、北海、交趾、云梦……；中国、澳洲人引用华夏)、山海(招摇之山、大荒之野、幽都冰原、扶桑之林、雷泽)、
  日本(飞驒山脉、鸟取沙丘、钏路湿原)、阿拉伯(希贾兹、鲁卜哈利、内夫得)、埃及、波斯、印度、西方、法兰克、日耳曼、罗马(拉丁地名，Mons Apenninus)、
  斯拉夫(乌拉尔、西伯利亚、乌苏里泰加林)、维京、游牧(杭爱、阿拉善戈壁、查干淖尔)、阿兹特克(马德雷、尤卡坦、米克特兰)、奥吉布瓦、
  犹太(圣经地名：黑门山、寻的旷野、以法莲树林)、精灵幻想(星落山脉、永冬冻原)；
  帝皇引用日耳曼(山、沼泽)、埃及(沙漠)、斯拉夫(冻原)、阿兹特克(雨林)，企业引用西方；没有合适地名的类型也引用相近文化(埃及、波斯的冻原用阿拉伯的)。

### 政党(Party)

理念 → 词库名。理念可选：Anarchism, SocialDemocracy, Libertarianism, SocialLiberalism, Capitalism,
ConservativeLiberalism, Centrism, Socialism, Communism, ReligiousDemocracy, Authoritarianism, Conservatism, Fascism。
| 字段 | 用途 | 没配时的默认词库 |
|---|---|---|
| `groups` | 党名 | `PartyNames/Party<理念>.csv` |
| `suffix_groups` | 改制共和后的国号后缀(民国、共和国……) | `PartyNames/Suffix<理念>.csv` |
| `untitled_groups` | 未组建政府的现代势力称呼(军阀、红军、护法军……) | `PartyNames/Untitled<理念>.csv` |
| `provisional_groups` | 已组建政府但不是中央的临时政府称呼 | `PartyNames/Provisional<理念>.csv` |

- 词库名也可以写成 `别的文化:词库名`(如 `"Huaxia:UntitledCommunism"`)，直接引用那个文化文件夹里的词库，
  几个文化共用一套称呼时不必复制文件(现代中国、山海都这样引用华夏的称呼)。
- 称呼里写 `{0}` 时代入势力名号(有王国法理用法理名，否则用国名)，如 `{0}系军阀` → 晋系军阀；
  不写 `{0}` 就接在名号后面，如 晋 + 工农红军 → 晋工农红军。
- 词库里有多条时，每个势力随机抽一条并记住，理念变了才重抽。

### 宪法(Constitution)

```jsonc
"Constitution": {
    "name_group": "ConstitutionNames",            // 正式宪法名称词库，每条写 {0} 代表国号(如 "{0}宪章")；没配用 "{0}宪法"
    "provisional_name_group": "CharterNames",     // 临时约法名称词库；没配用 "{0}临时约法"
    "clauses": { "territory": "Federal" }         // 本文化的制宪倾向，制宪会议起草时覆盖各党主张
}
```

- 词库名同样可以写 `别的文化:词库名` 引用别的文化的词库。
- `clauses` 可写的条款与方案：`territory`(Unitary / Federal)、`economy`(PrivateProperty / Mixed / Planned)、
  `religion`(Secular / StateReligion)、`emergency`(Prohibited / Allowed)、`ideology_intensity`(High / Medium / Low)、`speech`(Free / Limited / Strict)、`nation`(Pluralist / Moderate / Nationalist)、`civil_service`(Mixed / Professional / Spoils)、
  `amendment`(ParliamentSupermajority / Referendum / PartyCongress)、`term_years`(5~20)、`max_terms`(0 = 不限)。
- 各理念政党的默认主张在模组根目录的 `ConstitutionTemplates.json`，按 默认 → 理念 → 君主立宪 → 一党制 → 文化倾向 的顺序覆盖。

## 词库 CSV

```
key,cz,en,ch
huaxia_city_names1_1,长,Chang,長
```

- 第一列 key 在文件里不能重复，建议带上文化 id 前缀，避免和别的文化撞车。
- 命名词库请把 cz / en / ch 都填上。

## 书名

书写出来时按书的类型取名，每种类型四个文件(类型见下表)，本文化没有的文件用 `Books/` 下的通用词库：

| 文件 | 作用 |
|---|---|
| `<文化>Books<类型>.csv` | 固定书名(史记、源氏物语……)，全世界每个只用一次 |
| `<文化>BookTemplates<类型>.csv` | 书名模板，用占位符组合，用不完 |
| `<文化>BookWords<类型>.csv` | 模板里 `$word$` 随机取的词 |
| `<文化>BookTopics<类型>.csv` | 模板里 `$topic$` 随机取的主题词 |

类型：HistoryBook 史书、BadStoryAboutKing 讽刺君王、Fable 志怪寓言、FamilyStory 家族、FriendshipStory 友情、
LoveStory 爱情、BiologyBook 博物、Mathbook 算学、DiplomacyManual 外交、EconomyManual 经济、StewardshipManual 治理、WarfareManual 兵书。

占位符：

| 占位符 | 含义 |
|---|---|
| `$word$` / `$topic$` | 从 BookWords / BookTopics 随机取 |
| `$author$` `$city$` `$kingdom$` `$clan$` | 作者、所在城市、国家、氏族 |
| `$era$` | 本文化的科技时代 |
| `$dynasty$` / `$dynasty_short$` | 朝代(帝国名) / 去掉“朝、国”的朝代名(宋朝 → 宋，用来写“宋书”) |
| `$year_name$` `$emperor$` | 当朝年号、当朝皇帝 |
| `$past_emperor$` `$past_year_name$` | 随机一位先帝(有庙号用庙号)和他的年号 |

取不到值的模板(比如没有帝国时的 `$dynasty$`)会自动换一个。书名词库只写一种语言也行，空着的语言会退回 English，再退回简体。
中文界面下人名、国名里的空格会自动去掉。

另外两个文件：`<文化>LandmarkBooks.csv` 是本文化的传世名著书名(key 为 `landmark_book_<书id>`，书的定义在
`Technology/LandmarkBooks.json`)；`<文化>LandmarkBookLogs.csv` 是名著问世时的世界播报，`{0}` 文化、`{1}` 作者、`{2}` 书名、`{3}` 城市。

理念书库 `<文化>IdeologyBooks.csv`(通用的是 `Books/IdeologyBooks.csv`，两者合并使用)：国内读书人每年写的理念著作的书名，
数量与理念分布由宪法的"言论自由"决定(见 `Scripts/GeneralSystems/SpeechFreedomSystem.cs`)。
key 以 `<理念>_` 开头(如 `Socialism_3`、`Communism_Huaxia_1`)的是该理念专属书名；以 `Any_` 开头的是各理念通用的体裁模板。
占位符：`{0}` 理念名、`{1}` 作者、`{2}` 城市、`{3}` 该理念奠基名著的书名(资本论、国富论……)。
理念名：Anarchism、SocialDemocracy、Libertarianism、SocialLiberalism、Capitalism、ConservativeLiberalism、Centrism、
Socialism、Communism、ReligiousDemocracy、Authoritarianism、Conservatism、Fascism。
一个理念要本文化已有它的奠基名著(`Technology/LandmarkBooks.json` 里标了该理念的名著)才会有人写它的著作。
