# 政体配置补充字段

中央官职 `bureau_config.cores` 和 `division` 都可能是部门主官；列表层级不能用来判断其是否为职业文官。

- `political_appointment`：可选布尔值。省略或 `true` 表示政治主官（部长、部门负责人），责任政府下由执政联盟任命。
- `false` 表示常任事务岗位，可用于副职和执行人员；混合制与职业文官制下按一般选官规则补缺，任满三年不自动轮换。政党分肥制仍可轮换此类岗位。

旧配置及旧存档中没有此字段的中央职位按政治主官处理，修正现有部长被误标为职业文官的问题。后宫、地方长官及未成立责任政府的国家不适用这项政党任命规则。政体编辑器支持该字段的导入、编辑和导出。当前默认配置未新增独立副职。

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

后缀不再按固定政体名单强制替换。改制、城市易主、迁都及月度同步时刷新名称，保留核心地名；旧存档的城市类型缓存不能覆盖当前配置。政体编辑器可保存与导出全部可选后缀字段，文化覆盖使用 JSON 对象填写。
