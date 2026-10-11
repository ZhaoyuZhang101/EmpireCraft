# 通用系统：财政功能索引

财政继续使用现有目录，不另建一套平行框架。存档结构在 `Scripts/Data`，游戏对象接口在 `Scripts/GameClassExtensions`，窗口和补丁仍在原目录。

| 文件 | 职责 |
| --- | --- |
| `TreasuryRules.cs` | 纯数值规则：64位余额变动、整数报价适配、税款分成、最近12个月报表、账户汇总、预算预留及欠款分类。不能访问Unity对象。 |
| `TreasurySystem.cs` | 城市、国家、帝国的财政入口：记录流水、央地分税、腐败漏损、预算查询和报表文本。游戏对象操作在主线程。 |
| `SectorTaxRules.cs` | 现有部门税率和整数税零头。当前仍是原有简化税基，不等于完整的企业利润税。 |
| `PopulationEconomySystem.cs` / `PopulationEconomyAccounts.cs` | 虚拟人口生产、消费及滚动统计。产值、旧民间存款仍属于原有模型，不能直接转换成新现金。 |
| `MarketSystem.cs` / `CityResourceTransferSystem.cs` | 市场交换与实际货物交付。 |
| `ActorMoneyTransfers.cs` / `WalletReserveTransfers.cs` | 实体钱包的非所得转账、虚实转换资产保管。退款、产权本金不能冒充所得。 |
| `CityStabilitySystem.cs` | 地方治理、驻军、野战军的当期账单和历史欠款。实际扣款交给原城市、国家账户。 |
| `StateSettlementSystem.cs` | 建城项目和预留资金。科研、建设及历史欠款只能使用预留后的可用余额。 |

## 现金与报表约定

- 城市和国家扩展数据的 `Money` 是实际64位整数余额，旧JSON字段名不变。`GetTreasuryBalance()` 返回完整资产；`GetMoney()` 只供原版整数报价使用，不修改资产。
- `DiscretionaryBalance()` 返回完整可用余额供显示；`DiscretionaryFunds()` 限制一次整数报价。先扣实际安全储备和项目预留，再限制报价，不能先截断资产。
- 流水只记录实际余额变动。储备不是支出；偿还旧欠款影响现金，但不计为本期治理或军费，也不增加未来日常安全储备。
- 汇总城市和国家账户时，抵消总转入和总转出的匹配金额；保留汇总范围外的净划拨。不推断未记录的转账对手，也不补造旧账。
- 历史欠款使用 `GovernanceArrears`、`GarrisonArrears`、`MilitaryArrears`。旧流水没有足够信息，保持原分类直到自然滚出12个月窗口。
- 账本操作是有界月度记录；完整帝国报表按窗口或既有年度流程查询，不放入每帧遍历。

## 后续接入边界

完整企业财政仍需逐步接入真实交易、工资、存货成本、产权收益和明确税基。正常模式保留原版生产及消费，虚拟模式使用人口分组；不能重复生产、消费或征税。此前实验模型留在本地Tasks目录，不作为游戏运行逻辑。
