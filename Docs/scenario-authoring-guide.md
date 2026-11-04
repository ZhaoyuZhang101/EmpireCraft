# 世界地图剧本编写指南(给人和 AI 写剧本用)

写于 2026-10-30，对应代码：`Scripts/GeneralSystems/WorldMap/WorldMapData.cs`(字段)、`WorldMapScenario.cs`(开局怎么摆)、
`ScenarioEventSystem.cs`(剧情事件)。字段名、取值以本文为准；本文没写的字段不要自己发明，游戏会忽略或读错。
写完先跑校验：`python -I -X utf8 Tools/WorldMap/check_scenario.py <模组目录> <剧本文件>`，没有"错"再进游戏。

## 一、放在哪、两种做法

1. **自定剧本(推荐，AI 写的放这里)**：NML 的 mods_config 目录下 `EmpireCraft/scenarios/<剧本名>/scenario.json`
   (确切位置以世界地图窗口「自定剧本」卡的"打开文件夹"为准)。
   世界地图窗口「自定剧本」卡里有"打开文件夹""刷新"；最省事的底稿是先在窗口里把相近的预设剧本"存为自定剧本"，再改这份文件。
   整个文件夹拷给别人就能用。剧本里的问题(都城、地区不在城表上，国家编号重复，关系里写了不存在的国家)在窗口里会列出来，开局时那几条跳过。
2. **内置时代**：`WorldMaps/eras/<id>.json`，由 `Tools/WorldMap/*_source.py` 生成或补写(名人 `famous_source.py`、后妃 `consorts_source.py`、
   事件 `events_source.py`、外交 `relations_source.py`……)，改完跑 `check_eras.py`。要动仓库，适合定稿后再并进来。

**JSON 必须严格合法**：不能有注释、不能有末尾多余的逗号、字符串用双引号。读不出来的剧本整个不显示(只在日志里记一行)。

## 二、文字的写法

- 国名、时代名分三个字段：`zh` 简体、`en` 英文、`tw` 繁体(`tw` 可省，省了繁体界面用简体)。
- 人名、年号、行政区名、事件文字、国号写成一个字符串 `"简体|英文|繁体"`，繁体可省：`"李|Li"`、`"自成|Zicheng"`。
- 华夏、日本、朝鲜人物姓名分开写：`fam` 姓、`giv` 名。西方、西亚、天竺人物 `fam` 写空串 `""`，全名写在 `giv`：`"giv": "黎塞留|Cardinal Richelieu"`。
- 名不传的后妃写"氏"：`"fam": "周|Zhou", "giv": "氏|Shi"` 显示为"周氏"。
- `fam` 不写(没有这个键)= 按本国文化随机起姓；`fam` 写 `""` = 没有姓。两者不同。

## 三、引用规则(最容易出错的地方)

- **城**：只能用 `WorldMaps/cities.json` 里的城 `id`(自带城表的剧本用自己的 `cities`)。城有 `from`/`to` 年份，开局年不在范围里的城不存在。
  城在当年的名字按 `alt` 取(`"937|幽州|Youzhou|幽州"` 表示 937 年以前叫幽州)。**关隘(山海关、居庸关……)不是城**，事件里不能当城用。
  地图选最小一档时只放等级 `k` ≤ 2 的城；内置剧本通过 `ranks` 默认配置保留各行政区的治所。
  自定剧本若要保留小城，也应配置 `ranks`；首都尤其应保证等级 ≤ 2，否则最小地图可能换都或丢掉整个国家。
  内置剧本由 `administrative_defaults_source.py` 检查首都和行政治所；其他小城(k=3)可能不生成，事件条件别只靠它们。
- **地区(王国法理)**：`cities.json` 的 `regions[].id`(如 `guanzhong`、`hebei`、`youyan`、`liaodong`、`manchuria`、`monan`)。
- **国**：剧本自己起的 `polities[].id`，全剧本唯一，只用小写英文、数字、下划线。
- **文化键**：`Huaxia`(华夏)、`Youmu`(游牧)、`Japan`、`Roma`、`Frankish`、`Germanic`、`Viking`、`Slavonic`、`Arab`、`Persepolis`、`India`、`Egypt`、`Kosher`。
- 查城：`python -X utf8 -c "import json;[print(c['id'],c['zh'],c['r'],c.get('k'),c.get('from'),c.get('to')) for c in json.load(open('WorldMaps/cities.json',encoding='utf-8'))['cities']]"`

## 四、文件顶层

| 键 | 含义 |
|---|---|
| `year` | 开局年，公元前写负数 |
| `focus` | `"east"` 或 `"west"`(窗口里排在东方还是西方) |
| `order` | 窗口里的排序 |
| `zh`/`en`/`tw`，`dzh`/`den`/`dtw` | 剧本名、剧本说明 |
| `author` | 作者(自定剧本) |
| `tech` | 文化 → 科技档(`Technology/TechTree.json` 的 tier)，`"*"` 为其余文化。明末华夏是 6 |
| `techExtra`/`techSkip` | 文化 → 额外点上/跳过的科技 id 列表 |
| `inst` | 文化 → 开局直接施行的制度节点 id 列表(连同前置)，抄相近时代的 |
| `culture` | 地区 id → 本时代文化(覆盖地区默认文化) |
| `ranks` | 城 id → 本时代的城市等级(0~3，0 最大) |
| `polities` | 国家列表，见第五节 |
| `relations` | 开局外交，见第七节 |
| `events` | 剧情事件，见第八节 |
| `walls`/`passes` | 长城折线与关隘(抄 `walls_source.py` 的格式，不确定就整段从相近时代拷) |
| `wilderness` | 无人区地理单元 → 政权 id(可不写) |
| `wilderness_regions` | 地理单元 → 当地地区 id；须有对应的 `wilderness` 国家声明及同政权治所，可不写 |
| `cores` | 内置城表不用写(帝国法理在 `cities.json`)；自带城表的剧本才写 |

`wilderness_regions` 例如 `{"luliang":"hedong"}`：让吕梁山明确采用河东法理；小地图缺少对应治所时沿用原挂接。
不要把跨多个地区的秦岭、祁连山等整条指定给一个地区。编辑器的 `r:地区` 地块笔刷仍优先于这些默认指定；`n` 中立笔刷只作用于命中的无人区地块。
内置当地法理默认由 `Tools/WorldMap/wilderness_administration_source.py` 维护；机制与存档说明见 [无人区完善记录](wilderness-refinement-2026-11-02.md)。
地方设置用于开局；自动无人区在城市改隶、换法理后继续跟随原来源城市，手工笔刷保持固定。见 [无人区行政跟随](wilderness-administrative-following-2026-11-02.md)。
具体城市的开局辖地按可达陆路的地形距离分配；明确历史范围仍可保存跨障碍的行政归属，不等于强迫城市实体跨山、跨海领地。见 [城市顺地形扩张](city-terrain-expansion-2026-11-02.md)。

## 五、国家 `polities[]`

| 键 | 含义 |
|---|---|
| `id`、`zh`、`en`、`tw` | 编号与国名 |
| `c` | 文化键 |
| `cap` | 都城(城 id) |
| `r` | 认领的地区列表：这些地区里没被别国点名的城，归"认领了它、都城最近"的国 |
| `cities` | 点名要的城(优先于认领) |
| `of` | 宗主帝国的国 id：开局即为其成员 |
| `emp` | `true` 开局即称帝(本模组的帝国) |
| `grand` | `true` 国号前加"大"(明、元) |
| `core` | 帝国法理核心 id(`cities.json` 的 `cores`，如明是 `wu`) |
| `dynasty` | 前史里的朝代 id(`WorldMaps/history.json`，如 `ming`、`aisin_gioro`)，开局前的君主进史书；不知道就不写 |
| `era_name`、`era_year` | 年号 `"崇祯|Chongzhen"` 与开局是第几年 |
| `old_capital` | 旧都(作留都、陪京) |
| `divisions` | 行政区：`[{"name": "陕西|Shaanxi", "cities": ["xian", ...]}]`，核心直辖以外的各省(可省，省了按法理自动设) |
| `seats` | 都城直辖区或没有行政区国家的治所坐标 `[[经度, 纬度], ...]`；`divisions[]` 内也可写本行政区的 `seats` |
| `boundary` | 都城直辖区的 GeoJSON MultiPolygon 坐标数组；`divisions[]` 内可写本行政区的 `boundary`，外圈后的环表示孔洞 |
| `decline` | 王朝积弊 `{"strain": 65, "graft": 60, "annexation": 70, "bloat": 65, "military": 65, "factionalism": 70, "levies": 60}`(0~100，可省) |
| `conquest`、`conquest_city` | 入主华夏时的国号与"据有即算入关"的城：后金写 `"清|Qing"`、`"beijing"` |
| `shogun` | 幕府将军家(江户德川)；`holy` 神圣罗马之名；`zhou`/`king` 只用于战国 |
| `ruler` | 君主(人物，见第六节)；不写随机起 |
| `kin` | 宗室与后妃列表 |
| `famous` | 名将名臣列表 |
| `colonies` | 殖民地 `[{"name": "...", "company": true, "cities": [...]}]` |

## 六、人物

**君主 `ruler`**：`{"fam": "朱|Zhu", "giv": "由检|Youjian", "age": 34}`；女主加 `"sex": "f"`；`"f"` 是前史族谱里父亲的编号(不知道不写)。

**宗室与后妃 `kin[]`**(按顺序编号，从 0 起；序号指的就是这个下标)：

| 键 | 含义 |
|---|---|
| `fam`、`giv`、`age`、`sex` | 宗室不写 `fam` 就随君主姓 |
| `rel` | `son` `daughter` `grandson` `granddaughter` `brother` `sister` `nephew`；后妃 `wife`(正妻：皇后、王后、福晋)、`consort`(妃嫔) |
| `of` | 子孙：父亲在 kin 里的序号；后妃：丈夫在 kin 里的序号。**不写(或 -1)= 君主** |
| `heir` | `true` 立为储君 |
| `mo` | 生母在 kin 里的序号(必须是 wife/consort) |
| `in` | 后妃：与 kin 里这一位同一娘家(布木布泰随哲哲) |
| `house` | 后妃：娘家是本剧本某国的君主家(国 id)，如德川和子 `"house": "tokugawa"` |

正妻开局就与丈夫成婚(君主的正妻就是皇后，外戚按她的娘家算)；妃嫔记为妾室、坐进后宫妃位(35 岁以上不坐)。
一个人只能有一位正妻；没有在世皇后的君主，可以把宠妃写成 `wife`(玄宗与杨贵妃)。

**名将名臣 `famous[]`**：

| 键 | 含义 |
|---|---|
| `fam`、`giv`、`age`、`sex` | 同上 |
| `kind` | `general` 名将 / `minister` 名臣 |
| `rank` | 0 良才、1 名、2 国柱 |
| `post` | `chief` 首席朝官、`core` 中枢要职、`div` 部寺、`general` 统一支军，或**城 id**(坐镇该城，当城主) |
| `in` | 与本国 kin 里这一位后妃同一家(杨国忠 `"in"` 指杨贵妃)，外戚就连得上 |

起兵割据要找的人(李自成、吴三桂)必须写进 `famous`(或 `ruler`、`kin`)，事件按姓名找人。

## 七、开局外交 `relations[]`

`{"a": "later_jin", "b": "ming", "kind": "war", "goal": "夺城"}`。`kind`：

- `war` a 向 b 宣战，`goal`：`夺城` `收复` `争法理` `劫掠` `侵略` `争夺资源` `吞并` `迫使附庸`
- `truce` 停战、`alliance` 结盟、`marriage` 和亲、`pact` 通好(两边都是帝国)
- `protect` a 保护 b、`tribute` a 向帝国 b 称臣纳贡、`vassal` **a 立 b 为附庸**、`union` b 并入 a 的市场
- `friend` 旧谊 / `feud` 宿怨，`value` 分量(默认 30)

## 八、剧情事件 `events[]`

```json
{"name": "日志里用的名字", "text": "史书和屏幕上的一句|English", "after": 15, "repeat": false,
 "when": [ ...条件，全部满足... ], "then": [ ...结果，按顺序做... ]}
```

- 开局后每年查一次；开局满 `after` 年、条件全部满足就触发；`repeat` 不写或 `false` 只触发一次。
- 条件里的国已亡、城已没，算不满足；结果做不了的跳过(日志 `[剧情事件] …没做成(原因)`)，其余照做。
- `text` 只写游戏里对得上的城名、国名(城名按当年的叫法)；人名可能对不上(那人先死了)，尽量不写进 `text`，起兵的人名游戏会自己写进史书。

**条件 `when`**：

| kind | 需要的键 | 含义 |
|---|---|---|
| `holds_city` | `a`、`city` | a 据有此城(a 是帝国时，成员国的也算) |
| `holds_region` | `a`、`region` | a 据有该地区法理过半的城 |
| `gone` | `a` | a 已亡 |
| `empire` | `a` | a 已称帝 |
| `at_war` | `a`、`b` | 两国交战中 |
| `strain_above` | `a`、`value` | a 的王朝积弊高于 value(0~100) |

**结果 `then`**：

| kind | 需要的键 | 含义 |
|---|---|---|
| `relation` | `a`、`b`、`relation`、`goal`(仅 war)、`value`(仅 friend/feud) | 两国关系，种类同第七节 |
| `proclaim` | `a`、`name`(可省) | a 称帝；`name` 写了就改用这个国号(照写，不自动加"大") |
| `rename` | `a`、`name` | 改国号 |
| `move_capital` | `a`、`city` | 迁都(城得在 a 手里) |
| `culture` | `a`、`culture` | 改文化(帝国连同成员) |
| `adopt_huaxia` | `a`、`name` | 入主中原、采纳华夏制度，国号照 `name` 定(写"清"成"大清") |
| `rebel` | `a`、`city`、`cities`、`name`、`person`、`b` | **起兵割据**，见下 |

**起兵割据 `rebel`**：

- `person`：领头的人与部将，顿号隔开：`"李自成、刘宗敏"`。第一位还活着、还在 a 国的人领头，其余为部将；名字是剧本里 `famous`/`kin`/`ruler` 的"简体姓+名"。
  都找不到时由治所(`city`)的城主领头。
- `city`：治所(新国都城)；`cities`：另带的城。只带走还在 a(或 a 所在帝国)手里的城，帝国京城不带。
- `name`：国号 `"顺|Shun"`；`b`：新国的编号(自己起，如 `"shun"`)，**后面的事件用这个编号**(称帝、迁都、外交)。
- 结果：新国与 a 所在的帝国开战；驻在这些城的军和领头、部将统的军改投新国，再按军制编满；战争名取领头与第一位部将的姓(安史之乱)。

**现在做不到的**(别写，写了会被跳过)：某城直接易手给另一国(献城)、立某人为君、"某人在世""某国还没称帝"这类否定条件、按月触发、改宗教。

## 九、例：明末(李自成、吴三桂、大清)

以 1628 年(临高同期)或自己写的 1640 年代开局为例。先在 `famous` 里放人：

```json
"famous": [
  {"fam": "李|Li", "giv": "自成|Zicheng", "age": 22, "kind": "general", "rank": 1, "post": "xian"},
  {"fam": "刘|Liu", "giv": "宗敏|Zongmin", "age": 20, "kind": "general", "rank": 0, "post": "xian"},
  {"fam": "吴|Wu", "giv": "三桂|Sangui", "age": 16, "kind": "general", "rank": 1, "post": "general"}
]
```

(李自成、刘宗敏都是明朝人，写进 `ming` 的 `famous`；1628 年李自成还只是驿卒，`rank` 给 0~1 即可。)

事件(1628 开局：`after` 8 = 1636，15 = 1643，16 = 1644)：

```json
"events": [
  {"name": "后金改国号大清", "text": "后金改国号|Later Jin takes a new dynastic name", "after": 8,
   "when": [{"kind": "holds_city", "a": "later_jin", "city": "shenyang"}],
   "then": [{"kind": "proclaim", "a": "later_jin", "name": "清|Qing"}]},
  {"name": "李自成据西安", "text": "关中兵起|Revolt breaks out in Guanzhong", "after": 15,
   "when": [{"kind": "holds_city", "a": "ming", "city": "xian"}, {"kind": "strain_above", "a": "ming", "value": 60}],
   "then": [{"kind": "rebel", "a": "ming", "b": "shun", "city": "xian", "cities": ["hanzhong", "taiyuan"],
             "name": "顺|Shun", "person": "李自成、刘宗敏"}]},
  {"name": "李自成称帝", "text": "顺主于西安称帝|The lord of Shun proclaims himself emperor at Xi'an", "after": 16,
   "when": [{"kind": "holds_city", "a": "shun", "city": "xian"}],
   "then": [{"kind": "proclaim", "a": "shun", "name": "大顺|Great Shun"}]},
  {"name": "顺军入北京", "text": "北京易手|Beijing changes hands", "after": 16,
   "when": [{"kind": "holds_city", "a": "shun", "city": "beijing"}],
   "then": [{"kind": "move_capital", "a": "shun", "city": "beijing"},
            {"kind": "relation", "a": "later_jin", "b": "shun", "relation": "war", "goal": "夺城"}]}
]
```

- "后金改国号"用 `proclaim`(皇太极 1636 年称帝改国号)；入关后叫"大清"、采纳华夏制度，靠国家上已有的 `"conquest": "清|Qing", "conquest_city": "beijing"`
  (后金一旦据有北京就自动入主)，或另写一条 `{"kind": "adopt_huaxia", "a": "later_jin", "name": "清|Qing"}`。
- 吴三桂"引清兵入关"：没有"献城"的结果，用"起兵 + 称臣"凑：等明亡或北京丢了，吴三桂据关外的城(`city` 写他坐镇的城，城必须在城表上)起兵，
  再写 `{"kind": "relation", "a": "later_jin", "b": "wu", "relation": "vassal"}`(清立吴为附庸)与对顺宣战。
  **山海关不是城**：要让吴三桂守山海关，得先在 `cities.json`(或自带城表)里加一座城，例如
  `{"id": "shanhaiguan", "zh": "山海关", "en": "Shanhaiguan", "x": 119.75, "y": 40.01, "r": "youyan", "k": 3, "from": 1381}`
  (`x` 经度、`y` 纬度；加进内置城表会让 1381 年以后的所有剧本都多这座城，未在游戏里验证过，关城可能挤在旁边)。
- 国号 `"大顺"` 照写就显示"大顺"；内置国家要"大"字前缀用 `"grand": true`，事件称帝没有这个开关。

## 十、自查清单(给 AI 的硬规则)

1. 只用本文列出的键；`kind`、`rel`、`relation`、`goal`、文化键只取本文列出的值。
2. 城 id 都在城表里、开局年在城的 `from`~`to` 里；地区 id 都在 `regions` 里；国 id 唯一。
3. kin 的 `of`/`mo`/`in` 是 kin 列表的下标(从 0 起)，指向的人必须存在；`mo`、`in` 指向的必须是 wife/consort；`house` 是本剧本有 `ruler` 的国。
4. 事件里用到的国：剧本国 id，或前面某条 `rebel` 的 `b`。
5. `person` 里的名字在剧本人物里找得到(简体姓+名连写，西方人写全名)。
6. 跑 `check_scenario.py` 没有"错"。
