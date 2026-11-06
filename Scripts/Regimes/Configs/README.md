# 政体配置补充字段

中央官职 `bureau_config.cores` 和 `division` 都可能是部门主官；列表层级不能用来判断其是否为职业文官。

- `political_appointment`：可选布尔值。省略或 `true` 表示政治主官（部长、部门负责人），责任政府下由执政联盟任命。
- `false` 表示常任事务岗位，可用于副职和执行人员；混合制与职业文官制下按一般选官规则补缺，任满三年不自动轮换。政党分肥制仍可轮换此类岗位。

旧配置及旧存档中没有此字段的中央职位按政治主官处理，修正现有部长被误标为职业文官的问题。后宫、地方长官及未成立责任政府的国家不适用这项政党任命规则。政体编辑器在官职卡片的“高级 → 任命属性”里编辑该字段，不选则不写(按政治主官)。当前默认配置未新增独立副职。

# 城市后缀配置

城市名称读取当前政体 `SystemConfig.json` 中 `bureau_config.kingdoms` 的规则。

- `city_type`：选择城市职官配置；没有名称覆盖时，该枚举在 `OfficialType.csv` 的译文也是城市后缀。
- `city_suffix_key`：可选，覆盖本国家类别下普通城市的名称后缀，值为已经加载的本地化 key。
- `capital_city_suffix_key`：可选，仅覆盖本国家类别当前首府的名称后缀。未配置时，首府沿用普通城市规则。
- `city_suffix_keys_by_culture`：可选，文化 ID 到后缀本地化 key 的对象；用于同一政体模板下的文化差异。
- `capital_city_suffix_keys_by_culture`：可选，首府的文化专属后缀对象。

例如律令制的行政区：

```json
"LvLing_province": {
  "city_type": "LvLing_city",
  "city_suffix_key": "city_suffix_county",
  "capital_city_suffix_key": "city_suffix_prefecture"
}
```

以上只是名称相关字段；保留该行政区原有的官职、条件等配置。`city_suffix_county` 显示为“县”，`city_suffix_prefecture` 显示为“府”，英文分别为 County、Prefecture。其他自定义 key 需提供相应译文。

例如共享的原始制模板保留 `Origin_city` 的“部落”，仅为华夏配置县：

```json
"city_type": "Origin_city",
"city_suffix_keys_by_culture": { "Huaxia": "city_suffix_county", "China": "city_suffix_county" }
```

优先级：首府的文化专属覆盖 → 首府通用覆盖 → 普通城市的文化专属覆盖 → 普通城市通用覆盖 → `city_type` 译文。文化取国家已经绑定的 `realm_culture`，不按当地个别移民或君主的新文化即时更名。没有文化覆盖的配置不额外查询文化。

当前默认配置：原始制一般为部落、华夏为县；周制县；律令普通城市县，道、节度使及都护府首府府；西式封建沿用堡、帝国伯爵领、教区；古典城邦共和城；阿拉伯城；游牧帐；现代市。后缀随实际政体改变，不能把地图年份当成世界各文化同步发展的唯一依据。共享模板仍是游戏抽象，新增文化专属称呼应通过上述配置表达。

后缀不再按固定政体名单强制替换。改制、城市易主、迁都及月度同步时刷新名称，保留核心地名；旧存档的城市类型缓存不能覆盖当前配置。政体编辑器在国家类别卡片的“高级”里编辑全部可选后缀字段，文化覆盖按“文化 + 后缀 key”逐行填写。

# 政体行为开关

以前写死在代码里，现在各政体 `SystemConfig.json` 写明(读政体模板，不随存档里旧的政体副本)：

| 字段 | 含义 | 现有取值 |
|---|---|---|
| `members_can_secede` | 成员能否发起"拥兵自立"(还要世袭、有外交权和军队、不支援中央军、对中央好感不好、帝国正统 40 以下) | 封建、阿拉伯、律令、游牧 |
| `secede_member_types` | 只有这些国家类别的成员能自立；空 = 都能 | 律令：节度使、都护府 |
| `secede_ignores_central_army` | 支援中央军的成员也能自立 | 游牧(兀鲁思) |
| `indirect_rule` | 成员算间接统治，不因鞭长莫及失稳(羁縻、自主外交的世袭成员、自治城邦另按代码一律算) | 封建、原始、华夏原始、周制、幕府 |
| `direct_member_types` | 间接统治里按直辖算的国家类别 | 周制：郡；幕府：天领 |
| `partition_conquests` | 打下的法理划成行政区或分封(律令另有自己的流程；游牧要施行行省制度、古典共和要施行元首制，按特性另判) | 封建、周制、阿拉伯、幕府、律令 |
| `keep_member_roles` | 换成这个政体时成员保留原来的外交权与选任方式；成员君主入继大统时封国传给他的子女 | 律令、周制 |
| `partition_chance_factor` | 遗诏分治的机会倍数 | 律令 0.5、游牧 1.25、封建 1.5，其余 1 |

政体编辑器的“成员与分封”页编辑这些开关，以及 `enfeoff_only_royal`、`enfeoff_virtual_only`、`enable_auto_honorary_peerages`、`virtual_honorary_peerages` 等分封字段。

# 政体编辑器

`RegimeEditor` 的政体页能打开这里的每个目录(一个文件里有几个政体时分开打开，八旗这类只有 `OfficialType.csv` 的目录只编辑译名表)。写入时只重写改动过的值，注释、排版、编辑器不认识的字段和目录里的其他文件(如 `Naturalization.json`、`Senators.csv`)原样保留；`OfficialType.csv` 只改动过的行。新建政体可从 `RegimeEditor/RegimeTemplates/` 的常用原型、现有政体或空白模板开始，键名与类别名自动生成，不覆盖原有目录。新键名写入后编辑器自动同步代码枚举(编辑器自带的 `regime-enum-sync.js`，不用装 Python)：只在 `RegimeSystem.cs` 的枚举末尾追加，不排序、不删除，旧存档的枚举值不变；之后要重新编译模组。

模组根目录的 `IdeologyBureaus.json`(共和国按执政理念成套的中央机构，见 `RepublicSystem.GetBureauOverride`)在政体页的“共和国部委(按执政理念)”里编辑，单独写入；机构名写进 Locales。某个理念的 `cores` 或 `division` 写成 `null` 时整套退回现代政体的默认机构，不写或写空表则那一类机构为空。

# 军制配置(Formations.json)

建制是谁组织、谁出钱、怎么补、去哪打，和兵种(`CorpsArmSystem.Arm`)分开存。`formations` 定义各建制，`regimes` 按政体把军镇类别映射到建制，`fallback` 给没写的政体。读取失败时日志记 `[军制]`，一律按默认值的常备军。

建制字段(都可省略，省略即默认值，默认值与没有建制时的行为一样)：

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| `template` | `formation_template_corps` | 番号模板的语言键；`{0}` 番号、`{1}` 建制名、`{2}` 驻地城名、`{3}` 编制层级、`{4}` 成员国名前缀(中央和独立国为空) |
| `numeral` | 空 | 番号的数字写法：空为第几；`banner` 满洲正黄旗……(八旗，二十四旗一轮)；`stem` 天干字(甲字、乙字……癸字，用完接甲字二，英文用序数)；`wing` 左翼第几、右翼第几、中军第几 |
| `modern_template` | 空 | 文化进工业时代后改用的模板语言键；空表示不变 |
| `scope` | `formation` | 编号范围：`kingdom` 全国一套、`formation` 本建制一套、`region` 每座驻地一套 |
| `echelons` | 无 | 按满编取层级，`below` 是上限，最后一项不写 `below`；层级随满编变，番号不变 |
| `source` | `any` | 兵源：`any` 各城、`local` 只从驻地附近、`trained` 只收归化台账里的合格军人(花钱募的不受限)、`banner` 只收旗人和统治族群(别的军不收旗人) |
| `raise` | 1 | 补员速度倍数 |
| `training` | 1 | 新兵冲淡老兵的倍数，越大训练越差 |
| `veteran_cap` | 100 | 老兵值上限 |
| `upkeep` | 1 | 军饷倍数 |
| `denied_arms` | 无 | 不能列装的兵种，写 `Arm` 的名字，不看枚举大小 |
| `task` | `field` | `field` 野战先走、`mixed` 其次、`garrison` 最后走且不渡海 |
| `reach` | 不限 | 只打离驻地这么多地块以内的城；受攻的城去守不受限 |
| `command` | `central` | `local` 举国总动员、出征也不调到动员范围以外；`lord`、`tribe` 目前只用于显示 |

建制名的语言键是 `formation_<id>`，三份语言文件都要有。`regimes` 的键是 `RegimeType` 的名字，四个槽位 `central`、`district`、`prefecture`、`manual`；穿越政体另有 `early`、`early_below_stage`、`merge_at_stage`，阶段不到前者时新军用 `early`，到后者时把 `early` 的部队并入 `central`，只并一次。

例子：

```json
"regional_guard": { "template": "formation_template_local", "scope": "region", "source": "local", "upkeep": 0.7, "task": "garrison", "reach": 12, "command": "local" }
```

部队的 `formation` 存的是 id，切语言不改内部 id；改了 id 的旧部队找不到定义时按默认值算，番号退回旧写法。`Arm` 的存档数值固定，不能插入或重排。

# 归化民配置(Transmigrant/Naturalization.json)

归化身份记在每座城的台账上，只给已有的人贴标签，不产生人。读取失败时日志记 `[归化]`，按代码里的默认值。

| 字段 | 含义 |
| --- | --- |
| `max_share` | 一座城进台账的人至多占可入流程人口的比例，其余留在本地社会 |
| `registry_per_cadre`、`quarantine_per_nurse`、`school_per_teacher` | 一名干部每年登记、一名护理同时检疫、一名教师同时教的人数 |
| `base_registry`、`base_quarantine`、`base_school` | 元老亲自抓的底子，人手为零时也有这么多 |
| `cost_registry`、`cost_quarantine`、`cost_school` | 每人花费；上学按年交 |
| `budget_share` | 一年至多花可支配资金的这个比例 |
| `school_years` | 基础教育年数 |
| `worker_floor` | 结业的人至少这么多去做工 |
| `focus_share` | 定了培养重点时，分配时这么多先给重点 |
| `retrain_share` | 岗位还缺人时，每年工人里转岗的比例 |
| `soldier_reserve` | 军人岗位按伏波军满编多留的预备 |
| `cadre_base`、`cadre_per_people` | 一座城要的干部：底数加每多少人一名 |
| `cadre_min_yield` | 一个干部都没有时赋税、征兵能收上来几成 |
| `dispatch_share` | 富余的干部每年至多调走这么多 |
| `workers_per_factory`、`staffing_floor` | 一座工厂要的工人；配员不足时工业加成至少保留这么多 |
| `culture_shift`、`assimilation` | 已归化的人每年改文化的比例；城市文化占比的推动 |
| `landing_*`、`migrate_*` | 降临带来的人、旧档里澳洲人文化的人分到各岗位的比例 |

# 文武等级配置(OfficialRanks.json)

存档里仍是 `OfficeIdentity.honoraryOfficial`(0~8，0 最高)和文武类别。每套称谓都是九档，切政体、读旧档只换名字，不换算，数值越界按边界算。称谓表在 `Locales/OfficialRanks.csv`，键是 `official_rank_<套名>_<档>`，三种语言同一行。

| 字段 | 含义 |
| --- | --- |
| `civilizations.cultures`、`lines` | 文化归哪个文明组：先看文化，再看制度线，都没有用 `default` |
| `fallback` | 所有形态都没配时用的文武两套 |
| `forms.<形态>.civilizations.<文明组>` | `civil`、`military` 是套名；空字符串表示不设等级；`@metropole` 表示跟宗主国；`grade_suffix` 带"几品"；`merit` 显示勋级 |
| `forms.<形态>.civil_label`、`military_label` | 人物窗口那一行的标题键 |
| `forms.<形态>.modern` | 职务、官制图的现代叫法 |
| `forms.<形态>.performance_ladder` | 按绩效直接升降、不加岗位底线(律令制) |
| `floors.<政体>.<官职档>` | 岗位对应的档位，任职就到这一档；没配时看官职的 `honorary` |
| `unranked.<政体>` | 不列官阶的官职档(世袭封君)；君主、后宫由代码判断 |
| `military_offices.<政体>` | 算武职的官职档；军镇官职一律算武职 |
| `general_floors` | 军团统帅按满编人数定底，`below` 是上限，最后一项不写 |
| `captain_floor`、`soldier_cap` | 军官的底线；普通士兵靠战功最高到哪档 |
| `capital_bonus` | 城主所在城是本国首府时高几档 |
| `forms.<形态>.general_floors`、`captain_floor` | 本形态自己的军团定底和军官底线，不写用全局的 |
| `forms.<形态>.civilizations.<文明组>.soldier_ladder` | 没有军职的军人单走的一段军衔，指向 `soldier_ladders` |
| `soldier_ladders.<名字>` | 按绩效从低到高列语言键，`performance` 是够得上这一级的绩效；杀一个敌人 100 |
| `named_ranks` | 只给某位原班元老的军衔：名册上的名字 → 语言键，继承席位的子孙没有 |

形态是政体本身，另加三种：现代政体的 `one_party`(一党或民主集中制)、`republic`(其余)，君主国开了议会后的 `constitutional`。殖民地、特许公司看自己的政体，穿越政权看自己，其余看帝国核心。缺称谓的一档先退到 `fallback` 那一套，再不行显示数字。律令制以外的政体：岗位定底、只升不降、卸任保留、换岗不清零；原始政体和奥吉布瓦不设等级。

原著军衔比九档多(澳宋)：上将到上尉进九档；列兵到中尉走 `soldier_ladders`，带过兵或任过高职的不走这一段；元帅走 `named_ranks`。

例子：给维京文化单配一套，在 `cultures` 加 `"Viking": "viking"`，在要改的形态下加 `"viking": { "civil": "court_viking", "military": "army_viking" }`，再往称谓表加这两套各九行。
