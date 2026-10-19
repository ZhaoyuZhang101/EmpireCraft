# 制度不绑帝国、称帝设门槛 改造方案

起因：部落时代满图帝国。制度改革只在帝国里跑(Empire.update → InstitutionSystem.Update)，所以 TribalFissionService.FormTribes 把每个独立的原始政权都建成帝国(某某部落)，称帝门槛也很低。

已定：
- 制度改为"政权"推：帝国，或独立王国/部落联盟(简化朝堂)
- 称帝门槛：脱离部落时代(国家阶段)、直辖 8 城或附庸 3 国、一个文化圈(culture.GetTianxiaId())同时一个正统帝国
- 战国时脱离周室的诸侯算独立王国(做成常量)
- 旧存档：读档解散只有一国的部落帝国；2 城以下、没有成员的小帝国连续 10 年不达标才去帝号；其余保留
- 节奏：先做第 0~2 步，进游戏确认制度推进速度正常，再做第 3~5 步

## 第 0 步：重构为"政权"，玩法不变
- 新文件 Scripts/GeneralSystems/InstitutionRealm.cs：`InstitutionRealm`(Empire 或 Kingdom；Core、IsEmpire、Culture、Scope、State、Authority、Name、Ruler、Key、Record)；`Of(Empire)`、`Of(Kingdom)`、`IsSovereignKingdom(k)`(开化、非野、有都城、king.kingdom==k、不是帝国也不在帝国里、无封建宗主、无部落共主、不归古战；朝贡国算；战国脱离周室的诸侯按常量算)
- InstitutionSystem 的核心方法改为接受 realm(EnsureEmpireState、CanStartReform、StartReform、AdvanceReform、GetReformEnvironment、CountAdvancedBorderEmpires、CompleteReform、TryStartAiReform、CalculatePoliticalBalance、GetClassContribution、GetFactionContribution、Resolve/GetReformSponsor、ApplyOppositionFactionGrowth、SyncSharedEffects、ApplyEffects/ApplyEffect、EnumerateAffectedKingdoms、SyncGrantedTraits、BuildLineView；BuildClassShares 改为按 scope)，原 Empire 签名保留为一行包装，外部调用不动
- 只给帝国的步骤用 `if (realm.IsEmpire)` 包住(宪法、执政党与理念、复合帝国、改革冲击、社会动荡、阻力升级、外来政体同步、国名改换、教廷偏好、多数派)
- SyncEnactedNodeForRegime 加 `(string culture, RegimeType)` 重载
- 年度跨文化接触挪进 `TryYearlyWorldPass()`，登记到 CityPatch 的 WorldYearlyScans/WorldYearlyScanNames("制度接触"，两数组对齐)
- FactionClassSystem 加 UpdateRealm/EnsureRealmProfiles/ApplyInstitutionOutcome(realm)，UpdateEmpire 改为包装
- KingdomExtraData 加 `institution_state`(可空，读档时 Normalize)
- 验收：行为不变、编译过

## 第 1 步：王国推制度(简化朝堂)
- `InstitutionSystem.TryYearlyKingdomScan()`(仿 HarshRuleSystem.TryYearlyKingdomScan 的分帧队列)，登记"制度·王国"
  - 不再独立的(入了帝国、成了附庸)：丢掉进行中的改革并记一笔
  - 首次：applied_node_ids 记为本文化当前已施行的(只作底，不重放效果)
  - 之后每年：KingdomCourt.Tick → SyncSharedEffects → SyncGrantedTraits → AdvanceReform 或 TryStartAiReform
- 简化朝堂：君主一人提议(AI 用 TryStartAiReform)，无奏议/内阁/诉求；倡议者取支持派里占比最高的，没有就显示"君主"
- 支持度同一套 CalculatePoliticalBalance：阶层按本国人口，派系用 kingdom.GetFactionRatioValue；每年 EnsureRealmProfiles → UpdateRealm({k}) → ReconcileFactionRatios；总占比为 0 时按 ClassPower 播种(合计 60)；KingdomExtension ~596 无帝国时改调 UpdateRealm；占比称帝后沿用
- Regime.GetDominateFaction：无帝国时按王国占比排
- 君威 RealmAuthority(代替正统，比 mandate_required)：基数 55 + 君主资质(无君 −20) + 国库 + 腐败 + 苛政 + 声望(≤8) + 天命 8 − 叛乱 10；纯算式放 RealmAuthorityRules 便于测试
- 门槛：支持 ≥ 反对；AI 不强推
- 阻力满：改革作罢、反对派 +6、君主声望 −20、记一笔(新键 institution_kingdom_reform_collapsed_history)
- 规则 R1：本文化有帝国在时，王国不能推改政体/废君主的节点(新理由键 institution_reform_requires_paramount)；部落时代无帝国，任一部落都能推部落联合之类
- 完成：写进文化；改政体的节点调 ApplyKingdomRegimeChange(仿 ApplyRegimeChangeCore，跳过锁定/手选/君主制直接到现代)
- 效果：add_mandate→君主声望；relieve_malady、enable_claim 跳过；modify_faction_power 作用于王国；税、政体选项、法律、土地市场、特质、继承法作用于王国；只对成员国有意义的选项跳过
- AI：同帝国间隔，按 kingdom.id 错开；叛乱中不推；同文化别的政权在推的节点不重复；每文化最多 3 个王国同时在推
- 帝国↔王国交接：称帝时(EmpireManager.NewEmpire)把进行中的改革带进帝国(applied 不带)；帝国解散时(dissolveEmpire 前)交回核心王国
- 文化级循环纳入王国：GetReformEnvironment 按"同文化政权"数(帝国+独立王国)加速，保持部落时代速度；CountAdvancedBorderEmpires 按政权；UpdateCultureDrift 有政权在推时跳过、有政权时半速；BuildCultureLineView、AfterForceEnact/Revoke 带上王国
- 总开关 ModClass.KINGDOM_INSTITUTION_REFORMS(默认开，default_config.json + ConfigActions)

## 第 2 步：界面
- InstitutionWindow：`Open(Empire)`/`Open(Kingdom)`(仿 TechWindow 41-64)；_empire 换 _realm；王国模式：状态栏显示政体、"君主亲裁"、君威(悬停明细)，宪法卡换成君威卡，不显示舆论、社会动荡、街头、理念行；成员国打开所在帝国；附庸只读(新键 institution_realm_not_sovereign)
- 入口：EmpireWindow 照旧；KingdomWindowPatch 加"制度"页签；KingdomOverviewWindow 加按钮
- 新文本 cz/ch/en

## 第 3~5 步(第 0~2 步验证后再做)
- 3 部落：拿掉 FormTribes；单国部落帝国解散；部落联盟标 tribal_origin，定居后够 3 成员或 8 城且文化圈无别的帝国才保留帝号
- 4 称帝门槛：EmpireProclamationRules(国家阶段、规模、文化圈唯一)接进 EmpireFormationService.MeetsBaseRequirements/GetBestRoute/Diagnose、称帝谋划、正统挑战；诉求战输了去帝号；STRICT_EMPIRE_PROCLAMATION/EMPIRE_MIN_CITIES/EMPIRE_MIN_SUBJECTS 设置
- 5 旧存档：一次性迁移(SaveData.realm_reform_migration)；微帝国(≤2 城无成员)连续 10 年去帝号(EmpireDemotionService，保留法理、政体、王朝，不开宗室复国)

## 风险
- 推进速度：第 1 步必须按政权数算环境加速，否则部落时代慢一倍
- 性能：王国每年扫一遍人口，分帧队列 + 每文化 3 个上限
- tests/*.Tests.ps1 里有按源码字符串检查的，重构前先记基线
