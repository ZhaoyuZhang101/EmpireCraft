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
        "traits": ["patriarchy"],    // 文化特质 id
        "political_traits": ["strong_unification"], // 模组政治特质；强统一诉求会启用唯一中央、易帜、临时政府和统一继承
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
- Kingdom：`english_type_prefix` 英文界面用 “The {类别} of {国名}”，否则 “{国名} {类别}”。

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
