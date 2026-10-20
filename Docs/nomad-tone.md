# 游牧基调对照

用户 2026-10-20 定的游牧整体基调：**税收低，可以劫掠，大汗死后会分裂，高扩张，低控制**。各条落在哪里：

| 基调 | 做法 | 代码 |
| --- | --- | --- |
| 税收低 | 游牧政体、汗国分封收税打对折(包税制七五折)，税级只有无/低两档 | `KingdomExtension.NomadTaxFactor`、`IsNomadTaxRegime`；各政体 `SystemConfig.json` 的 `option_tax_level` |
| 可以劫掠 | 草原压力(纪元、牲畜粮食、国库、互市岁币)到 40 结盟南下；只抢不占、抢够就退、可买和；长城关隘、屯田边城挡劫掠；单国劫掠诉求并入同一规矩 | `NomadRaidSystem`(四个分部文件)、`SteppeRules`、`TempFac_劫掠`、`CityPatch.FinishedCapture`(洗劫)、`WarPatch.end_war`(收场) |
| 大汗死后会分裂 | 积弊重或正统低时换汗，兀鲁思据地自立；雄主之后第一回换汗必乱 | `NomadCycleSystem.OnKhanSucceeded`、`UnificationSystem.TakeHeroTurmoil` |
| 高扩张 | 只有雄主能发动统一战争；雄主在位游牧扩张两年一次、可同时两场、不看过度扩张；劫掠屡胜的盟主成雄主、联盟转汗国 | `UnificationSystem`(游牧难一统)、`TempFac_游牧扩张`(HeroCooldownYears、HeroMaxWars)、`NomadRaidSystem.Hero` |
| 低控制 | 直辖容量 ×0.6；满控路程游牧 ×1.1、汗国分封 ×1；新打下的城控制力不到 0.45 就转间接统治 | `AdministrativeControlSystem`(NomadCapacity、NomadReach、NomadHardLimit) |

常态(没灾、没雄主)下的游牧(用户 2026-10-20 定的四项)：

| 常态 | 做法 | 代码 |
| --- | --- | --- |
| 平时不大扩张 | 游牧扩张诉求平时八年一次、只打两城以内不在帝国里弱一半的小国；平时主动开的一般战争同样只打两城以内的小国 | `TempFac_游牧扩张`、`SteppeOrderSystem.PeacetimeWarAllowed`(接 `WarTargetPatch`、`EmpireCraftPlotsAddition.IsValidModWarTarget`) |
| 部落争草场 | 相邻游牧政权(或同一汗国的兀鲁思)按牲畜粮食压力与强弱打小仗，只争一两座边城，两年强制议和，同一对十年不再打 | `SteppeOrderSystem.Feuds`、`EmpireWarType.草场之争`、`CityPatch`(拦城)、`WarPatch`(收场) |
| 依附强部 | 弱部投靠接壤最强汗国为兀鲁思；易汗、汗国衰落时按机会散去 | `SteppeOrderSystem.Attach` |
| 朝贡换赏赐 | 向强盛定居帝国称臣，回赐一到两倍(按正统，末世减半，正统不到 30 断)；赏赐减国库压力，断了成压力、可不再称臣 | `SteppeOrderSystem.Tribute`、`TributaryRelations.AfterTribute` |

诊断日志：`[EmpireCraft][游牧劫掠]`(每年各游牧政权的草原压力构成、结盟与目标、洗劫、买和、收场、雄主机会)、
`[EmpireCraft][游牧常态]`(争草场、依附与散去、称臣与赏赐断绝)、`[EmpireCraft][汗位更替]`、`[EmpireCraft][扩张上限]`。
