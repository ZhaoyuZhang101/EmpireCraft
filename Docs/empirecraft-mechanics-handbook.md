# EmpireCraft 机制手册

> 依据 README、Docs 与 C# 代码结构整理；推断处已标注。

# EmpireCraft 帝国盒子 机制手册（群友检索版）

> 依据 README、代码结构清单整理。清单只有类型名和方法名，没有实现，所以所有效果描述都带来源标记。

---

## 0. 阅读约定

| 标记 | 含义 |
|---|---|
| **[R]** | README 明确写了 |
| **[推]** | 据代码结构推断：类名、方法名、文件位置只能证明"存在"，行为是合理猜测 |
| **[未]** | 文档/代码未说明（数值、公式、触发条件、操作步骤等） |

通用提醒：

- 清单只证明某个类或方法存在，不证明它在当前版本启用，也不证明效果。
- 本手册**没有任何数值**（概率、阈值、税率、年限）。要查数值请看游戏内窗口、`Locales` 和各类配置文件。
- 清单里有大量 README 没提的子系统（教廷、神圣罗马、游牧、网页终端、世界地图剧本、核威慑、第三方模组兼容等），下文标注"README 未提及"。
- README 的仓库结构链接是作者本机绝对路径（`D:/Application/Steam/...`），点不开，请以 GitHub 仓库为准。清单只覆盖 `Scripts`，`Locales`、`RegimeEditor` 的内容这里看不到。
- 代码里"Law"一词同时出现在"法理"（DeJure）和"法律"（EmpireLaw 司法）两个语境。看到 `EmpireLaw` 开头的类时，不要想当然归到某一类（见第 2、6 章）。

**项目定位 [R]**：EmpireCraft 是 WorldBox 的大型政治与历史模拟模组，围绕帝国、正统、法理、派系、官僚、地方治理、战争占领、历史叙事构建比原版更长期的国家演化系统。
技术栈：C#，依赖 NeoModLoader、Harmony、Newtonsoft.Json、DOTween。
安装：装 WorldBox → 确保支持 NeoModLoader → 放入 `Mods` 目录 → 启动并启用。项目仍在开发，持续平衡玩法、优化性能、补完 UI。

---

## 1. 帝国与正统

### 核心概念
- **帝国** [R]：支持建立、继承、瓦解、修复、重建。内部可含王国、附庸、岁币国、朝贡国。
- **正统值** [R]：README 列为核心机制，与中央地方关系、皇帝更替、帝国历史并列。计算方式 [未]。
- **天命 / Mandate** [推]：`Empire.AddMandate`、`IsNeedToIncreaseMandate`、`MandateRecord`、`EmpireFormationService.GetInitialMandate`、`EmpireCraftEmpireBehCheckMandate` 表明帝国有"天命"类数值。它与"正统值"是否同一个量 [未]。
- **合法性口径** [推]：`MonarchyLegitimacy`（含 `UsesTraditionalModel`）、`ModernLegitimacy`、`PopeLegitimacy`、`ImperialLegitimacyRules`、`RealmAuthority` 按政体类型提供不同的合法性/权威计算。
- **继承** [推]：枚举 `SuccessionLawType`、`EmpireHeirLawType`；`EmpireCraftKingdomBehCheckHeir`（`ReplaceBarredHeir`、`HeirCandidates`、`MaleOnlyThrone`）；`EmpireCraftKingdomBehCheckKing`（`NeedSuccession`、`ChooseKingFromHeir`）。
  相关系统：`HaremSuccessionSystem`、`SuccessionCouncilSystem`、`ConstitutionalSuccessionSystem`、`InterregnumSystem`、`SuccessionToolPatch.findNextHeir`、`HeirFinderHelper`。

### 关键流程（据代码结构推断，细节 [未]）
1. **称帝**：`EmpireCraftKingdomBehCheckEmpire`（`CanStartEmpireFormation`、`CheckPossible`）判断资格 → `EmpireFormationService`（`GetBestRoute`、`GetRouteName`、`OnEmpireCreated`）选路线 → `EmpireManager.NewEmpire` 建立对象。`IsInFailureCooldown` 暗示称帝失败有冷却。
   `EmpireCraftPlotsAddition.BecomeEmpireAndStartEnfeoff` 表明称帝可经"阴谋"触发，并伴随分封 [推]。
2. **更替**：`KingdomPatch.new_emperor / before_new_emperor / emperor_left`，`RegnalNameService.OnEmperorCrowned`（称号显示、罗马数字 `ToRoman`），`ReignRecordSystem`（在位记录），`PosthumousNameGenerator`（谥号）。
3. **瓦解与归档**：`EmpireManager.dissolveEmpire`、`Archive`、`FallenEmpireRecords`（`Record`、`AdoptArchived`、`PurgeOlderThanYears`）。`EmpireLossDiagnostics` 用于排查丢失。
4. **修复与复辟**：`RestorationSystem`、`CoreCapitalRestorationSystem`、`DefeatedEmpireHouse`、诉求 `TempFac_复辟帝制`。
5. **挑战正统**：`ImperialLegitimacyChallengeService`（`StartChallenge`、`DeclareUsurpation`、`TryStartRivalryWar`）+ `WarExtension.InitializeLegitimacyChallenge`。`ImperialLegitimacyRules.RequiredOccupiedZones / HasOccupiedThird` 说明与占领区块数量挂钩，门槛值 [未]。
6. **权臣与周期**：`PowerfulMinisterRules`（`IsMandateCollapsed`、`ShouldPreferEmpressDowager`、`IsVulnerableNewEmperor`）、`DynasticCycleSystem/Rules`、`MaladySystem`（弊病）、`ImperialSealSystem`（玉玺）、`EmperorTourSystem`（巡游）、`EmpirePartitionSystem`（分裂）、`EmpireBankruptcySystem`（破产）、`ProvincialCollapseSystem`。

### 相关代码
`Layer/Empire.cs`、`EmpireData.cs`、`EmpireManager.cs`、`EmpireMembershipService.cs`、`FallenEmpireRecords.cs`、`ImperialLegitimacyRules.cs`、`PowerfulMinisterRules.cs`；`AI/KingdomAI/EmpireCraftKingdomBehCheckEmpire.cs`；`AI/EmpireAI/*`；`GeneralSystems/EmpireFormationService.cs`、`ImperialAuthorityRules.cs`、`RegnalNameService.cs`；`GodPowers/CreateEmpireButton`、`RemoveEmpireButton`、`EmpireFormButton`、`EmpireEnfeoffButton`。

### 联动
- 法理（第 2 章）：称帝资格与帝国法理有关 [R]。
- 官僚（第 4 章）：内阁、内廷、后宫官职由 `EmpireAI` 下的 Cabinet / InnerOffice 检查驱动 [推]。
- 战争（第 7 章）：挑战正统会引发战争。
- 历史（第 13 章）：更替写入皇帝历史、帝国历史。

### 常见误解
- "帝国 = 很大的王国"：README 里的帝国是独立对象，有成员关系、历史、归档。
- "正统值就是天命"：关系 [未]，别当同一个数。
- "帝国灭亡记录就没了"：有归档，但保留多久 [未]（`PurgeOlderThanYears` 说明存在按年清理的机制）。

---

## 2. 法理

### 核心概念
- **王国法理** [R]：王国围绕**主法理、首都法理、祖籍法理**进行命名与合法性运作。
  代码里对应 `Layer/KingdomTitle.cs`、`KingdomTtitleManager.cs`（文件名拼写如此）、`KingdomTitleData`（含 `KingdomTitleJurisdictionRecord`、`dejure_last_core`）。"Title"在代码中似乎就是法理单位 [推]。
  城市侧有 `CityExtraData.shared_titles / dejure_last_title / contested_claims` 相关字段 [推]。
- **帝国法理** [R]：可包含多个王国法理。
  代码对应 `EmpireCore`（`Layer/CoreEmpireTitle.cs`：`AddTitle`、`RemoveTitle`、`SetCoreCapital`、`GetCoreCapital`）和 `EmpireCoreManager`（`newEmpireCore`、`SyncCitiesFromTitles`、`GetCities`）[推：EmpireCore ≈ 帝国法理]。
- **帝国法理的作用** [R]：影响称帝资格、统一战争目标、历史归属、地图图层展示。
- **共享法理** [推]：`DeJureShareSystem`（`ShareTitle`、`SharedCores`、`RefreshShareLimits`）；`ConfigActions` 里有 `TitleShareLimitCallBack`、`CoreShareLimitCallBack`、`TianxiaShareLimitCallBack`，说明有可配置的共享上限，数值 [未]。
- **天下** [推]：`TianxiaSystem`、`TianxiaMap`、`TianxiaWindow`、`TianxiaPowers`，`EmpireCore` 带 `shared_tianxia`。天下与帝国法理的层级关系 [未]。
- **法理声索规则** [推]：`DeJureTitleClaimRules`（`CanDeclare`、`CanAcquireWithoutWar`、`ControlsRequiredShare`、`ControlsAll`）、`DeJureAgenda`（`HasClaimOn`、`HoldsCoreOf`）、`DeJureTitleBindingRules`、`TributaryTitlePetitionRules`（朝贡国请求头衔）、`KingdomTitleRelationResolver`。

### 关键流程
1. **编辑法理** [R]：有帝国法理编辑窗口、历史合集、名称修改与调试按钮。
   对应神力按钮 `CreateTitleButton`、`AddTitleButton`、`RemoveTitleButton`、`CreateProvinceButton`、`AddProvinceButton`、`RemoveProvinceButton`、`CreateEmpireCoreButton`、`AddTitleToEmpireCoreButton`、`RemoveTitleFromEmpireCoreButton`、`DestroyEmpireCoreButton`，以及窗口 `EmpireCoreWindow`、`KingdomTitleWindow` [推]。具体操作步骤 [未]。
2. **AI 维护主法理**：`EmpireCraftKingdomBehCheckPlots.CheckMainTitle`、`EmpireCraftCityBehCheckTitle` [推]。
3. **命名联动**：`RealmNamingSystem`（多个分部：Founding、Huaxia、Modern、Place、Universal、Contest、Display）、`EmpireCoreNamingSystem`、`EmpireFoundingNameRules`、`RealmNameRules`、`DynastyNaming`、`CapitalLossNamingSystem`（失都后临时命名）。命名规则细节 [未]。
4. **战争目标**：`WarExtension.GetTitleTarget`、`AreAllDeJureTitleZonesControlledByAttackers`；`WarGoal` 枚举 [推]。
5. **显示**：`DeJureMap`（图层）、`EmpireCraftQuantumSpriteLibrary.ShowEmpireZones`、`TerritoryLabelRenderer.SubmitEmpireCore`。

### 联动
称帝（第 1 章）；战争目标（第 7 章）；朝贡/附庸请求头衔（第 8 章）；历史合集（第 13 章）；文化的"固定法理文化"开关 `CultureService.IsFixedDeJureCultureEnabled`（第 10 章）[推]。

### 常见误解
- "法理 = 现在占着的地盘"：法理是名义归属，实际控制由占领和区块控制决定，两者可以不一致（战争目标里专门有"是否控制全部法理区块"的判断）[推]。
- "`EmpireLaw` 文件夹就是帝国法理"：该目录内容是司法体系（案件、监禁、大赦，见第 6 章），不是 DeJure 法理。

---

## 3. 派系与宫廷

### 核心概念
- **固定派系** [R]：`Regimes/FixedFaction.cs`（`FactionManager`、`FactionType`、`PlayerFactionConfig`、`FixedFaction`）；`RegimeSystem` 里有 `GetDominateFaction`、`RecoverFactions`、`IsFactionChangeBlocked` [推]。
- **派系领袖 / 成员 / 支持者** [R]：`FactionEditor`（`Seats`、`HasRoom`）、`EmpireCraftActorCheckFaction`（`EnrollOfficials`、`TryJoin`）、`KingdomExtension.AddSupporter / HasSupporter / GetPusher`。
- **派系能力** [R]：推动诉求、积累影响力、制造叛乱；派系间存在关系、竞争和吞并空间。"竞争/吞并"由哪个类实现 [未]。
- **阶层** [推]：`ClassSystem`（`SocialClass`）、`FactionClassSystem`（`GetClassPowerContribution`、`ModifyFavor`）、`FactionBalanceSystem`（`Imbalance`、`Dominant`）。
- **政治圈子** [推]：`CliqueSystem`（含宦官 `IsEunuch`、`PurgeEunuchs`、官学）、`ConsortKinSystem`（外戚）、`GentrySystem`（门第：`Pedigree`、`Grade`、`JunWang`）、`FavouriteSystem`（宠臣）、`PersonalTieSystem`（死敌/庇护）、`ThroneSuspicion`、`FactionPurgeSystem`。
- **宫廷** [推]：`CourtSystem`（分部 Bargain、Crisis、Divine、Dynasty、Year）、`CourtState`（含 `CourtDeal`、`CourtMemorial`、`CourtTerm`）、`CourtActions`（`Reward`、`Rebuke`、`Appease`、`DismissLeader`）、`CourtDoctrine`、`CourtIssues`（`Stance`）、`CourtCrisisRules`（`CanCoalition`）。具体议题、立场数值 [未]。
- **军事干政** [推]：`MilitaryCoupSystem`、`MilitaryRegency`、`PalaceArmySystem`。

### 关键流程
1. **入派**：官员每个周期被检查是否入派（`TryJoin`、`EnrollOfficials`）[推]。
2. **议事与派系博弈**：宫廷里各派系有立场，可做交易（`DealDraft`、`Evaluate`）、上奏（`CourtMemorial`）、抵制（`IsBoycotting`）[推]。
3. **派系推动诉求**：见第 5 章。
4. **叛乱链**：`RebellionSystem`（`CanAttempt`、`SecessionThresholdFor`）→ `RebellionStartupService.RaiseUprisingMilitia` → `RebellionSnowballSystem`（扩散）→ `EmpireCraftCityBehCheckJoinRebellion`（城市加入）→ `FactionRebelNaming`（叛军命名）。配套有 `SecessionGate`、`SecessionSpread`、`PettyRebellion`、`SectUprisingSystem`、`FamineUnrestSystem`、`SocialCrisisSystem`。触发条件 [未]。
5. **清洗**：`FactionPurgeSystem.TryRaise / Resolve`。

### 相关代码
`Regimes/FixedFaction.cs`、`Regimes/TemporaryFactions/*`；`GeneralSystems/Court*.cs`、`Clique*`、`Faction*`、`Gentry*`、`Rebellion*`；`UI/Windows/FactionDetailWindow(.Pages).cs`、`AddFactionWindow`、`AddFactionMemberWindow`；`UI/Components/FactionPieChart`、`CourtPanel`、`CourtActionsPanel`。
`FactionDetailWindow` 有成员、诉求、制度立场、阶层基础、政党事务等页面 [推]。

### 联动
官僚（官职任命受派系影响 [推]）；诉求（第 5 章）；法律（清洗、罪行）；宗教（宗教派系，见第 9 章）。

### 常见误解
- "派系 = 临时的党"：README 区分"固定派系"，而诉求载体 `TemporaryFaction` 是另一类对象 [推]。
- "派系能直接废掉皇帝"：README 只说能推动诉求、制造叛乱，直接废立 [未]。

---

## 4. 官僚与政体

### 核心概念
- **官职体系** [R]：中央与地方都有官职；国家、城市、军队、后宫支持不同类别官位配置。
  代码：`Regimes/OfficeSystem.cs`（`OfficeManager`、`OfficeObject`、`CenterOffice`、`BureauSetting`、`BureauConfig`、`OfficerPowerType`）、`OfficialRankSystem/Rules`（品级）、`OfficeBans`、`OfficeSelector`、`OfficeCandidateRoster`。
- **政体 Regime** [R]：政体、文化、制度、官制联动。`RegimeSystem.cs` 含 `KingdomType`、`CityType`、`ArmyOfficialType`、`TaxLevel`、`LeaderSelectMethod`、`RegimeType`、`ReligionLevel` 等枚举/类。各取值含义 [未]。
- **政体编辑器** [R]：独立工具 `RegimeEditor`，配置生成与编辑。使用方法 [未]。
- **制度树** [推]：`InstitutionSystem`（改革阶段、文化排名、`StartRevert`）、`InstitutionDefinitionRegistry`、`InstitutionConfig`、`InstitutionGraphView`、`InstitutionWindow`、`InstitutionCultureWindow`。
- **中央机构** [推]：内阁（`SetCabinetForFeudalism` 等）、内廷、三省制（`UsesThreeDepartments`）、`HuaxiaCourtStage`。
- **爵位与分封** [推]：`PeeragesLevel`、`PeerageType`、`EmpireCraftEmpireBehCheckPeerages`、`WesternPeerageRules`、`PartialEnfeoffment`、`PrinceLineage`、`EnfeoffmentHelper`。
- **现代政治** [推]：`ConstitutionSystem`（`ConstitutionData` 的国体、权力中心、元首产生、选举权、政党制等条款）、`ParliamentSystem`、`ParliamentBillSystem`、`RepublicSystem`、`PartySystem`、`PartyBanSystem`、`SenateSystem`、`ProvincialPoliticsSystem`、意识形态系列（`IdeologyDynamicsSystem`、`IdeologyFamilies`、`IdeologySpreadSystem`）。
- **其他政体形态** [推，README 未提及]：城邦（`CityStateService`）、幕府（`BakufuSystem`）、部落（`TribalFissionService`、`TribalCouncilSystem`）、酋长/苏丹国（`EmirateSystem`）、殖民地（`ColonySystem`）、人身联合（`PersonalUnionService`）、转生者议会（`TransmigrantSystem`）。
- **地方治理** [R]：`CityStabilitySystem/Rules/Math`（稳定度）、`AdministrativeControlSystem`、`AdministrationReorganizationSystem`、`MultiCapitalSystem`（陪都）、`ExclaveMaintenanceSystem`（飞地）。

### 关键流程
1. **官职更新**：`EmpireCraftCityBehCheckOffice.UpdateOffice`、`EmpireCraftActorCheckOffice`；帝国层 `CheckOfficePower`、`SelectOfficer` [推]。
2. **科举** [推]：`KejuSystem`（`TryRegular`、`HoldGrace`、`FraudOpen`、`Punish`）、`ExamSystem`（城/省/帝国三级，`EmpireExamLevel`）、`EmpireCraftKingdomBehCheckExam`。诉求 `开科取士 / 特开恩科 / 严查科场` 与之对应。
3. **国家类型判定**：`EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType / CalcCityType`，按政体配置给国家、城市归类并套用命名规则 [推]。
4. **制度改革**：由派系/诉求推动，改革有"阶段"与"回退"（`InstitutionReformStage`、`IsRevertable`）[推]。
5. **政体转换**：`OnRegimeChanged`（`BakufuSystem`、`EmirateSystem`）、`ModernStateFormationSystem.FormGovernment`、`StateModernization.TryRevolution` [推]。

### 联动
文化绑定政体（`Locales/Cultures/Culture_*/CultureRule.jsonc` [R]，见第 10 章）；科技解锁现代制度（`ModernInstitutionBlocked`，第 11 章）；诉求改变官制（第 5 章）；税制与财政（第 12 章）。

### 常见误解
- "官职就是头衔"：官职（Office）与法理头衔（Title）、爵位（Peerage）是三套东西 [推]。
- "改了政体官职立刻变"：官制由配置驱动、周期检查，延迟 [未]。
- "RegimeEditor 是游戏内窗口"：README 称其为独立编辑器 [R]。

---

## 5. 诉求

### 核心概念
- **定义** [R]：可由中央或地方推动；推动受政治身份、影响力、支持者、派系、条件限制。README 列举：削藩、税制、宗教政策、官制调整、统一战争、撤换官员。
- **载体** [推]：`Regimes/TemporaryFactions/Claims/TempFac_*.cs`（约百个文件，每个对应一项诉求）继承 `TemporaryFaction`。"诉求 ≈ TemporaryFaction"是推断。
- **推动机制** [推]：`KingdomExtension`（`StartToPush`、`Push`、`PushOneMonths`、`ConsumeInfluenceAndCalcAdd`、`CalcPolicyProgressAdd`、`Stop`、`Execute`、`TemporaryPushProgress`）。进度每月累计，消耗影响力。公式 [未]。
- **规则与评估** [推]：`ClaimRules`（`IsAllowed`、`FindPushTarget`、`AfterExecute`）、`ClaimTargetRules`（目标选项：割城、战败等）、`ClaimAgendaSystem`、`ClaimTuning`、`ClaimPolicySystem`（移民/金融政策）。
- **君主决议** [推]：`DecisionSystem`、`RulerDecrees`、`DecisionScale`（玩家/君主层面的决策）。

### 诉求分类（仅依据文件名，效果均 [未]）
| 类别 | 示例 |
|---|---|
| 税制与财政 | 降低赋税、提高赋税、通商减税、裁省用度、盐铁专营、重农抑商 |
| 官制/政体 | 增设官署、推行三省六部、设立内阁、设置行政区、转周制、转天朝制度、转世袭、转军府、撤销军府 |
| 吏治与科举 | 整顿吏治、开科取士、特开恩科、严查科场、重定律令 |
| 宗教 | 确立国教、自由信仰、宗教同化、宗教融入、加强神权、神授君权、争夺主教叙任权、划地给教廷、恢复圣地 |
| 战争与外交 | 谋求统一、对外扩张、扩张地盘、兴兵讨伐、劫掠、迫使朝贡、罢兵议和、割让城池、和亲、提供岁币 |
| 帝国诏令/封建 | 分封、削藩、夺取诸侯开战权、允许诸侯自由开战、确认诸侯特权、大赦天下、封禅、册立储君、颁布金玺诏书、颁布帝国治安令 |
| 宫廷斗争 | 诛宦官、宦官掌兵、党锢、禁党、开放党禁
