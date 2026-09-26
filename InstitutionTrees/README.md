# 制度科技树配置说明

## 目录结构

| 路径 | 作用 |
|---|---|
| `Settings.json` | 全局设置：文明等级、吸收规则、社会动荡、改革竞争、君主立宪 (`constitution`) |
| `<线 id>.json` | 一条科技线，文件名即线 id。文化在 `CultureRulesConfig.json` 的 `setting.institution_line` 中认领 |
| `Common/*.json` | 公共制度模板，可被任意线复用 |

## 公共模板（一个政策给多个文明用）

在 `Common/*.json` 中定义模板：

```json
{ "templates": [ { "id": "land_sale_decree", "name_key": "...", "politics": {...}, "effects_on_complete": [...] } ] }
```

在任意线的 `nodes` 里实例化，只写本线特有的位置信息，其余字段继承模板（对象逐字段合并、数组整体替换）：

```json
{ "template": "land_sale_decree", "id": "roma_land_sale_decree", "branch": "finance", "advancement": 5, "requires": ["western_burgher_charters"] }
```

- 不写 `id` 时实例 id 为 `模板id@线id`。
- 同一模板的各个实例视为同一项制度：已掌握其中之一的文化不会再从别的线吸收另一个；
  本线树上已有同源实例时，也不会吸收外线实例（应按本线前置自行研究）。

## 制度特性 `features`（代码只认特性，不认节点 id）

节点通过 `features` 声明它提供的系统级效果，多个节点声明同一特性时取最大值：

```json
"features": { "vassal_authority": 100, "constitution_basis_fiscal": 1 }
```

| 特性 | 含义 |
|---|---|
| `constitution_basis_administration` / `_fiscal` / `_law` / `_commerce` | 立宪所需的制度基础 |
| `vassal_authority` | 宗主集权权威 (0~100) |
| `vassal_self_succession` | 附庸自行继承（请封/自立） |
| `grace_edict` | 推恩令 |
| `commandery_kingdom` | 郡国并行 |
| `sibling_enfeoffment` / `sibling_enfeoffment_abolished` | 新君兄弟裂土受封 / 取消 |
| `senate_election` | 元老院推举首领 |
| `direct_administration` | 封建制下设辖区/教区 |
| `theocratic_state` | 神权国家 |
| `claim_reform:<诉求名>` | 派系诉求（如 `开科取士`、`转天朝制度`）推动的改革目标 |

政体配置的官职条件也可以用 `empire_feature:<特性>[|最低值]`。

## 君主立宪

- 全局规则见 `Settings.json` 的 `constitution`（门槛、派系态度 `faction_stances` 等）。
- 每条线可在自己的 json 中覆盖：

```json
"constitution": { "required_features": ["constitution_basis_administration", "constitution_basis_law"], "requires_composite_empire": true }
```

- 是否君主制由政体 `SystemConfig.json` 的 `is_monarchy` 决定；是否允许地主阶层由 `allows_landlord_class` 决定。
