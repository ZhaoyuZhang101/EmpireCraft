# 文化包说明

每个文化就是这里的一个文件夹 `Culture_<文化id>/`，**加文化、改词库都不需要改代码**。
推荐直接用 `RegimeEditor/启动桌面版.bat` 里的「文化编辑器」，下面是文件格式，手写也可以。

```
Locales/Cultures/
├─ Culture_Huaxia/
│  ├─ CultureRule.json            规则：名称、颜色、物种、政体、命名规则……
│  ├─ HuaxiaCityNames1.csv        各种词库，文件名 = 文化id + 词库名
│  ├─ HuaxiaBookTemplatesHistoryBook.csv
│  └─ ...
├─ Books/                         书名的通用词库(文化没配的类型用这里)
└─ PartyNames/                    党名、共和国号后缀的通用词库
```

## CultureRule.json

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
        "Religion": "ReligionNames", // 宗教名词库
        "City":    { "groups": { "group_1": "CityNames1" }, "rule": ["group_1"], "name_pos": 0 },
        "Kingdom": { "groups": { "group_1": "CountryNames" }, "rule": ["group_1"], "name_pos": 0, "english_type_prefix": false },
        "Clan":    { ... }, "Family": { ... }, "Unit": { ... },
        "Party":   { "groups": { "Socialism": "PartySocialism" }, "suffix_groups": {} }
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
没配的理念用 `PartyNames/Party<理念>.csv`；共和后的国号后缀 `suffix_groups` 没配用 `PartyNames/Suffix<理念>.csv`。

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
