# 神罗二期：帝国权威、私战与帝国法院、选帝侯位、陪臣化、帝国解体 改造方案

起因：2026-10-20 用户问"想要更好的模拟神罗的体制"。查 Player.log，议会表决、让步、自由市、禁令都在转，但帝国战争"通过，0 国应征"、治安令同一帝国连执行三次、自由市收入掉到 0、让步攒太快，主教领、帝国改革、分割继承、和约、罗马远征、选立储君一次都没出现。老毛病另有子代理在修(改 HolyRomanSystem 各分部)；本方案是修完之后的二期新机制。

已定(2026-10-20 问答)：
- 帝国权威：一根贯穿的皇权刻度。加冕、护佑诸侯(帮成员打退外敌)、太平、家族领地大会涨；成员被帝国外的人吞并、选举让步、选举波折、诸侯抗命会跌。帝国改革、治安令、禁令都要花它；帝国议会页显示
- 诸侯私战与帝国法院：帝国改革("永久和平")颁布之前，成员之间可以为法理打小仗(只争边城、不能吞并整国，规模受限)；颁布之后禁止私战，领土纠纷交帝国法院按法理裁决(有概率判城)，违者可遭禁令
- 选帝侯位可授可夺：皇帝可把选帝侯位授给亲近的诸侯、从被禁的诸侯手里夺走；选举时候选人拉票、花钱；票数僵持出对立国王或大空位(帝位空缺期间有帝国代理)
- 陪臣化与帝国直属：大诸侯可以经议会或法院和平吞并一城小邦、典押自由市；皇帝维护帝国直属，阻止小邦被吞(要花帝国权威)
- 帝国解体(要，慢慢来)：帝国权威长期很低时，帝国外的强国可做诸侯同盟的保护人拉走一批成员；再低下去皇帝退位、帝国解散，各家独立
- 节奏：帝国权威先做，其它都挂在它上面；每步单独提交、能编译

基线：行号以 58b5f7f 为准。修老 bug 的提交会改 HolyRomanSystem 各分部，每步开工前先 `git log -- Scripts/GeneralSystems/HolyRomanSystem*.cs` 看它改了什么、行号挪到哪；它已经修好的(如帝国战争应征、治安令重复执行、自由市收入、让步频率、帝国改革触发)以它为准，本方案只在上面加东西。

适用范围沿用现有两个判定：`HasDiet`(封建制帝国、非圣座帝国、至少两个成员，Diet.cs:45) 管权威、私战与法院、选帝侯席位、陪臣化、解体；`IsElective`(选帝制，HolyRomanSystem.cs:42) 另管选举、对立国王、大空位。

## 现有代码里能复用的、要改的

- 数据：`HolyRomanState`(Scripts/Data/HolyRomanData.cs:6) 挂在 `EmpireData.holy_roman`(Scripts/Layer/EmpireData.cs:36)，`HolyRomanSystem.State` 懒建(HolyRomanSystem.cs:36)。读档归一化 `DataManager.NormalizeEmpireData`(Scripts/Data/DataManager.cs:525) 现在没管 holy_roman，新字段的空列表要在这里补。存档是 Newtonsoft，缺键时保留字段初值，新字段一律给初值即可读旧档
- 年结入口：`ConstitutionalEconomySystem`(…:262) → `HolyRomanSystem.Yearly`(HolyRomanSystem.cs:129)，顺序 Members → Reform → Leagues → Diet。新机制都插在这里，不另开扫描
- 正统条目：`MonarchyLegitimacy.Breakdown` 调 `HolyRomanSystem.LegitimacyItems`(MonarchyLegitimacy.cs:75；HolyRomanSystem.cs:55)；没有君主时已有"mandate_interregnum −20"(MonarchyLegitimacy.cs:56)，大空位直接吃这一条
- 选帝侯团怎么定：`EmpireCraftEmpireBehCheckCabinet.SetCabinetForFeudalism`(Scripts/AI/EmpireAI/EmpireCraftEmpireBehCheckCabinet.cs:138-193)——3 个教会席(主教领的主教优先，Members.cs:280；不够从宗教等级高的国的城主里补)+ 4 个世俗席 = 兵力最强的 4 个普通诸侯(排除核心、被禁、自由市、主教领)。每次内阁检查都按兵力重排，所以席位不固定，谈不上授与夺。`ElectorRealms`(HolyRomanSystem.cs:226)、`Voters`(Diet.cs:62) 都从 `CabinetMembers` 读，下游不用改
- 选举怎么走：皇帝死 → `EmpireCraftKingdomBehCheckKing` 行 100-104 `office.Select` → `OfficeSelector.TryGetFeudalElectiveRuler`(OfficeSelector.cs:155) → `RankElectiveCandidates`(…:171) 按"与候选人同派系的选帝侯人数"排序取第一。没有过半、没有拉票、不会僵持；储君直接即位(…:164)。`OfficeObject.SetActor` 开头调 `TryElectHouse`(OfficeSystem.cs:276 → HolyRomanSystem.cs:88)，换家族用 `Empire.TransferCoreTo`(Empire.cs:1426)；`HolyRomanSystem.Electing` 免普通即位的 −20(OfficeSelector.cs:88-98，Empire.cs:646)
- 让步怎么存：`Concede`(HolyRomanSystem.cs:173) 四选一(降帝国税、某选帝侯国恢复开战权、自治派 +5、中央一城封给最强选帝侯)，只存本地化文本 `state.concessions`(≤20 条)，没有结构化记录，收不回来
- 开战权诉求：通用的"允许诸侯自由开战/夺取诸侯开战权"(TempFac_允许诸侯自由开战.cs、TempFac_夺取诸侯开战权.cs；ClaimRules.json:217/103 只挂华夏、日本线)；神罗专用的"颁布帝国治安令/确认诸侯特权"(TempFac_颁布帝国治安令.cs:22-46、TempFac_确认诸侯特权.cs:28-64；ClaimRules.json:420/438 挂罗马线)。它们只切 `Regime.AllowDiplomacy / AllowSupportCenterArmy`，管的是对外开战与出兵
- 同帝国成员不能互相宣战：`FeudalVassalService.CanDeclareExternalWar`(FeudalVassalService.cs:477-487，同帝国、同根都拦) 与 `WarGate.Forbidden`(WarGate.cs:36)。所以"永久和平"本来就成立，**私战只能由本系统直接 `startWar`**——同禁令战(Diet.cs:329)、宗教战(Leagues.cs:124)、周制战国兼并(ZhouEraSystem.cs:855 TryConquest)
- 同帝国交战已经能打：`CityPatch.FinishedCapture` 同帝国交战时由攻方接收(CityPatch.cs:388-401)；战线按两国自身记(FrontSystem.cs:122)；`FeudalConquestService.TryResolveCapitalConquest` 对同帝国不生效(FeudalConquestService.cs:63)。也就是说现在打下成员都城就真易手、一城小邦会被灭——私战必须另加限制
- 小仗限城的现成写法：`PettyRebellion.Blocks`(PettyRebellion.cs:46) 在 `FinishedCapture`(CityPatch.cs:361) 与 `joinAnotherKingdom`(CityPatch.cs:973) 两处拦"满两城/原主都城"，私战照抄
- 开战目标：`WarGoalSystem.收复`(Reclaim，WarGoalSystem.cs:452) 看起来合用，但 `Claimed` 按 `FrontSystem.Realm` 归到帝国核心(WarGoalSystem.cs:221-236)，同帝国两成员之间会把"第三家成员的法理城"也算成攻方的。私战要列进 `OwnEnding`(WarGoalSystem.cs:52) 自己结算；`PendingCessions`(…:60) 是私有的，要加一个公开的 `QueueCession(city, to)`
- 开战钩子：`WarPatch.new_war`(WarPatch.cs:433) 会拉姻亲(…:445 RoyalMarriageSystem.OnWarStarted)、封建扩边(…:456 ExpandFeudalWar → FeudalVassalService.ExpandSide，共主领国自动参战 FeudalVassalService.cs:617)、升级世界大战(…:459)。`startWar` 返回前这些就已经跑完，来不及先设战争类型——私战、争位之战要在 `startWar` 前后置一个静态标记(同 `Electing` 写法)让它们跳过。结束钩子 `WarPatch.end_war` → `HolyRomanSystem.OnWarEnded`(WarPatch.cs:216；Diet.cs:423)
- 成员求援：`MemberWarAid.AnswerRequest`(MemberWarAid.cs:118) 中央加入成员守方——"护佑诸侯"在这里记账；`Aidable` 排除攻方里有本帝国成员的仗(MemberWarAid.cs:222)，私战天然不会把中央拉下场
- 法院：`CourtRuling`(Reform.cs:89) 每年三成(CourtChance，Reform.cs:25) 自动把成员手里别家法理的城判还，没有原告、没有抗判、不看皇权
- 帝国改革：`ReformEnacted` = 文化级制度节点 `western_imperial_reform`(Reform.cs:29；InstitutionTrees/Roma.json:1026；`InstitutionSystem.IsEnacted(empire, …)` 实际查文化，InstitutionSystem.cs:212-218)，同文化所有帝国一起生效，没法"花权威颁行"
- 典押：`TryPawnFreeCity / PawnFreeCity`(Members.cs:139/146)：中央国库不足一成年收时，整座自由城市并入最富的诸侯，价三年城市收入；不留记录，不能赎回
- 一城小国降大国的现成写法：`ZhouEraSystem.TrySubmit`(ZhouEraSystem.cs:872)
- 同盟：帝国成员不能入同盟(AlliancePatch.cs:33)；同盟里有帝国就整盟解散(ModAllianceService.cs:47)；建模组同盟的现成写法 `FormAntiEmpireAlliances`(EmpireFormationService.cs:548：newAlliance + ModAllianceService.Mark + setName)
- 离开与解散：`Empire.leave`(Empire.cs:2519)、`EmpireManager.dissolveEmpire`(EmpireManager.cs:163)；称帝失败冷却 `empire_formation_failed_timestamp` 十年(EmpireFormationService.cs:42/91，KingdomExtension.cs:415)
- 空位先例：`SuccessionCouncilSystem` 议立期间核心无君约两个月(SuccessionCouncilSystem.cs:16-27)，它排除选帝制(…:40)；`TempFac_索取皇位` 对有内阁的封建制不触发(TempFac_索取皇位.cs:155)
- 加冕：`PapacySystem.Crown`(PapacySystem.cs:317)；远征 `StartRomzug / Romzug`(Leagues.cs:184-221)
- 诊断：`FiscalDiagnostics.Count(key)` 进年度"[事件统计]"(FiscalDiagnostics.cs:36)
- 宽窗：`EmpireWindow.Diet.cs` 现在是 KPI 四格(行 39) → 两栏(三院 | 皇位，行 45-83) → 议会纪事 → 选举让步 → 诸侯同盟 → 按钮(行 119-130)；部件 `WideKit.AddKpiRow / BeginColumns / BeginColumnCard / AddListRow / AddButtonRow`，比例条 `UIHelper.AddBarRow`(UIHelper.cs:444)
- 单测：纯算式放 `*Rules.cs`，`tests/*.Tests.ps1` 用 Add-Type 编(样板 RealmAuthorityRules.cs + tests/RealmAuthorityRules.Tests.ps1)。`tests/HolyRomanClaims.Tests.ps1` 按源码字符串查 OfficeSelector 里的 `TryGetFeudalElectiveRuler(office, pKingdom)` 与 `empire.GetCabinetMembers()`，改选举时这两处不能删
- 设置开关样板：`ModClass.KINGDOM_INSTITUTION_REFORMS` + default_config.json + `ConfigActions.KingdomInstitutionReformsCallBack`(ConfigActions.cs:80)

## 顺序与依赖

| 步 | 内容 | 依赖 | 提交可单独编译 |
|---|---|---|---|
| 1 | 帝国权威：刻度、涨跌、花费门槛、议会页显示 | 无 | 是 |
| 2 | 诸侯私战、帝国法院、违禁可遭禁令 | 1(几率、抗判看权威) | 是 |
| 3a | 选帝侯席位固定、可授可夺 | 1(授夺花权威) | 是 |
| 3b | 选举拉票花钱、对立国王、大空位 | 3a(在座选帝侯) | 是 |
| 4 | 陪臣化请愿、帝国直属、典押留底与赎回 | 1；法院路线用 2，没做 2 时只走议会 | 是 |
| 5a | 保护人拉走诸侯同盟 | 1 | 是 |
| 5b | 皇帝退位、帝国解散 | 1、5a | 是 |

2 与 3a 互不依赖，可以对调。每步结束：编译过、跑 tests/HolyRomanClaims.Tests.ps1 与新单测、cz/ch/en 三份语言文件都有新键。

估算口径(下文"多久一次"都按这个)：一个神罗式帝国，成员 10~16 国(一城小邦 3~5、自由城市 2~4、主教领 0~3)，皇帝平均在位 25 年，帝国存续约 300 年；"一局"指这样一个帝国的一生，时间都是游戏年。

## 第 1 步：帝国权威

刻度 0~100，整数，存帝国级。档位：80 以上"隆盛"、60~79"稳固"、40~59"平常"、20~39"式微"、20 以下"名存实亡"。

初值：
- 新帝国(第一次满足 HasDiet)：50
- 旧档(字段为 −1)：第一次年结按现状折算，不记事件：45 + 已加冕 10 + 帝国改革已施行 10 − 已订和约 30 + 家族领地(选帝制：两成五以上 +5、一成以下 −10) − 2×min(5, 让步条数)，夹 10~80。一般旧档落在 45~60

涨(事件)：
| 事件 | 变化 | 挂在哪 |
|---|---|---|
| 加冕(教宗加冕) | +15 | PapacySystem.Crown(PapacySystem.cs:317) 末尾调 `HolyRomanSystem.OnCrowned`；每位皇帝只计一次 |
| 自行加冕(无教廷、与罗马决裂) | +8 | 同上 |
| 护佑诸侯：中央应求援加入成员守方的仗，守方胜 | +6 | MemberWarAid.AnswerRequest(…:118) 记 `protect_wars`；OnWarEnded 结算 |
| 护佑之战议和、该成员没丢城 | +2 | 同上 |
| 帝国战争胜 / 负 | +8 / −8 | Diet.cs:437-448(与正统同处) |
| 禁令执行成功 / 失败 | +6 / −10 | Diet.cs:449-462 |
| 宗教战争皇帝胜 / 订立和约 | +10 / −30 | Leagues.cs:137-167 |
| 同族连任(house_terms ≥ 2)、储君即位 | +3 | Elected(HolyRomanSystem.cs:120)、ElectiveYearly(…:153) |

跌(事件)：
| 事件 | 变化 | 挂在哪 |
|---|---|---|
| 选举让步(含选立储君的让步) | −4 / 项 | Concede(HolyRomanSystem.cs:173) 记下一项后 |
| 诸侯抗命(拒缴摊派、拒不应征，第 2 步起含抗判、违禁私战) | −2 / 次，每年至多 −8 | MarkDefiance(Diet.cs:182) |
| 成员被帝国外的人吞并(国亡、城多半落到外人手里) | −8 / 国 | 年结快照比对(见下) |
| 城被帝国外的人拿走 | −1 / 城，每年至多 −6 | 同上 |
| 成员离开帝国(独立、投外；第 5a 步的保护同盟另算) | −5 / 国 | 同上 |
| 选举波折(第 3b 步)：双重选举 −10；大空位开始 −10、期间每年 −4 | | |

每年漂移(年结)：
- 太平 +1：本年帝国内没有私战、禁令战、宗教战、争位之战、成员叛乱，也没有城失于外人
- 家族领地(选帝制)：四成以上 +1，一成以下 −1
- 小邦林立：成员超过 12 国 −1
- 已订和约(sovereign_princes) −1("诸侯各自为政"，和约后很难回升，配合第 5 步)
- 到 80 以上，所有"涨"减半(向上取整)，防止长期太平顶到 100

花(门槛 = 不到就不能做；花 = 做成时扣)：
| 事项 | 门槛 | 花 | 说明 |
|---|---|---|---|
| 颁行帝国改革(永久和平、帝国法院、帝国大区) | 60 | 30 | 改成议会议案，见下 |
| 颁布帝国治安令 | 50 | 25 | TempFac_颁布帝国治安令：CheckCondition 加门槛，Execute 扣 |
| 确认诸侯特权 | — | 5 | 当作一次让步，Execute 扣 |
| 提请帝国禁令 | 10 | 10 | Convene(Diet.cs:236) 议禁令时扣；PickAgenda(Diet.cs:219) 不到 10 不提 |
| 授予选帝侯位 / 褫夺被禁者的选帝侯位 | 15 / — | 15 / 10 | 第 3a 步 |
| 维护帝国直属(驳回陪臣化) | 10 | 10 | 第 4 步 |
| 恢复世袭皇权 | 80 | 50 | 拍板 6 |

神力按钮(议会页的"召开议会""提请禁令"等)照扣权威，扣到 0 为止，不因权威不够灰掉(拍板 8)。

作用(被动)：
- 议会每票 +(权威−50)/200：权威 100 时 +0.25，0 时 −0.25(Stance，Diet.cs:108 附近，与家族领地那一项并列)
- 抗命几率 ×(1.5 − 权威/100)，夹 0.5~1.5(Refuses，Diet.cs:366)
- 正统：80 以上 +5("皇权隆盛")，20 以下 −5("皇权式微")(LegitimacyItems，HolyRomanSystem.cs:55)
- 第 2 步起：私战几率、抗判几率；第 4 步起：陪臣化请愿几率；第 5 步：解体条件

帝国改革改为议会颁行(拍板 1)：
- `ReformEnacted(empire)` 改为 `state.reform_at >= 0`(帝国级)；文化节点 `western_imperial_reform` 只当前提
- 新议程 `reform`(AgendaReform)：文化节点已施行、本帝国未颁行、权威 ≥ 60、距上次付议满 4 年时，PickAgenda 排在摊派之前；投票 选帝侯 +0.1、一两城的小诸侯 +0.2(永久和平护着他们)、五城以上的大诸侯 −0.2(没了私战)、自由城市 +0.3、教会诸侯 +0.1；通过扣 30，记"帝国议会颁行帝国改革"
- 旧档：文化节点已施行的，第一次年结直接记 reform_at = 当时(不扣权威)，行为不变
- 若修 bug 的提交已经改了帝国改革的触发方式，以它为准，只在"颁行"那一下加门槛与花费

成员损失怎么认(不挂 CityPatch，免得漏掉和约割地、法院判城这些不经占城的路)：每个 HasDiet 帝国年结时存一份静态快照(成员国 id 集合、城 id → 所属国 id；不进存档，读档后第一年只建底)。下一年比对：
- 原成员国已亡、它的城多半在帝国外的人手里 → 被吞并 −8
- 原成员国还在、已不在帝国 → 离开 −5(第 5a 步拉走的不重复扣)
- 原帝国城现属帝国外的国、且那国不是原主 → 城失于外人 −1

数据(HolyRomanState 新字段，全给初值)：
```
int authority = -1;                      // 帝国权威 0~100，-1 = 旧档还没折算
List<AuthorityEntry> authority_log = new();  // 最近 16 笔：at、delta、text(已本地化)
long authority_crowned_id = -1L;         // 已按加冕计过的皇帝
double authority_low_since = -1d;        // 低于 20 起算(第 5 步用，先记下)
int year_defiance_loss, year_city_loss;  // 本年已扣(封顶用)，年结清零
double reform_at = -1d;                  // 帝国改革在本帝国颁行的时间
double last_reform_agenda_at = -1d;
Dictionary<long, long> protect_wars = new(); // 护佑之战：战争 id → 被护佑的成员 id
```
`AuthorityEntry` 放 HolyRomanData.cs；DataManager.NormalizeEmpireData 补 `holy_roman ??= new()` 与各列表/字典的 `??= new()`。

文件：
- 新 Scripts/GeneralSystems/ImperialAuthorityRules.cs(纯算式：Initial、Tier、StanceBonus、RefusalFactor、ApplyGain(软顶)、Clamp；只依赖 System)
- 新 tests/ImperialAuthorityRules.Tests.ps1(照 RealmAuthorityRules.Tests.ps1：初值几例、软顶、夹值、系数两端)
- 新 Scripts/GeneralSystems/HolyRomanSystem.Authority.cs：`Authority(empire)`、`ChangeAuthority(empire, delta, textKey, args)`(唯一入口：软顶、夹值、记账、诊断)、`CanSpend / Spend`、`AuthorityYearly`(折算、快照比对、漂移、low_since)、`OnCrowned`、`NoteProtection`
- 改 HolyRomanSystem.cs(Yearly 里 DietYearly 之前调 AuthorityYearly；Concede、Elected、LegitimacyItems)；Diet.cs(Stance、Refuses、MarkDefiance、PickAgenda/Convene/Enact 的禁令与改革、OnWarEnded)；Reform.cs:29；Leagues.cs(OnLeagueWarEnded)；PapacySystem.cs:317；MemberWarAid.cs:118；TempFac_颁布帝国治安令.cs、TempFac_确认诸侯特权.cs；DataManager.cs:525；HolyRomanData.cs；EmpireWindow.Diet.cs；Locales/cz、ch、en

AI：权威是系统自动加减；花费由现有入口带出——治安令走派系诉求(权威不够就不立项)，禁令由抗命自动付议(权威不到 20 不提，留 10 余量)，改革由 PickAgenda 自动付议。

界面：KPI 由四格改五格，帝国权威放第一格("62·稳固"，`WideKit.Grade(权威, 60, 30)` 取色)；两栏下面加一排两栏——左"帝国权威"卡(AddBarRow 画刻度，下面 AddListRow 列最近 6 笔"+15 加冕 · 2 年前"，涨绿跌红)，右"皇权所费"卡(改革、治安令、禁令三行：门槛/花费，够的金色、不够灰)；第 3a 步起右卡换成选帝侯席位，所费挪进左卡的说明行。

估算(每百年，权威在 50 上下时)：涨约 +150(加冕 4 次 +60、太平 50 年 +50、护佑与帝国战争约 +25、家族领地与连任约 +15)，跌约 −110(让步 5 项 −20、抗命 10 次 −20、外人吞城灭国约 −20、私战约 −10、小邦林立约 −40)，AI 花约 −80~−120(改革一次 30、治安令 25、禁令两次 20、授位 15、驳回陪臣化两三次)。平时在 35~65 摆动；订和约后一次 −30 再每年 −1，二三十年内掉到 20 以下，进入第 5 步的轨道。

## 第 2 步：诸侯私战与帝国法院

### 私战(帝国改革颁行之前)
- 攻方：普通诸侯(Estate None，非核心、非被禁)，有成年君主，不在打仗，兵力(countTotalWarriors)≥ 守方 0.8 倍
- 守方：与攻方接壤的同帝国成员(非核心——皇帝家族领国不卷入私战，皇帝自有禁令与议会)
- 争的城：守方手里、属于攻方法理(`attacker.GetRealmTitleIds()` 与主法理)的非都城，至多 2 座，接壤的优先；没有这样的城就不能私战(所以一城小邦、自由城市只有一座都城，打不着)
- 治安令只管对外开战与出兵，不管私战(拍板 2)；顺手把"颁布帝国治安令"诉求说明里"禁止诸侯私战"改成"收回诸侯外交与对外开战之权，要求出兵"
- 几率：每年每个够格攻方 5% × 系数，系数 = 2 − 权威/50，夹 0.2~2(权威 50 → 5%，25 → 7.5%，80 → 2%)；每帝国同时至多 2 场，每年至多新开 1 场；同一对诸侯打完 10 年内不再私战
- 开战：置 `HolyRomanSystem.DeclaringFeud = true` → `startWar(attacker, defender, normal)` → 还原；`SetEmpireWarType(EmpireWarType.私战)`；战名"{攻方}争{城}之战"(两城时"{攻方}争{城}等地之战")；记 PrivateWarRecord；权威 −1；世界提示"{攻方}为{城}向{守方}兴起私战"
- 规模受限：
  - WarPatch.new_war 与 join_war_side 遇到 `DeclaringFeud` 或私战类型，跳过姻亲参战、封建扩边(含共主领国)、世界大战升级(WarPatch.cs:445/456/459、ExpandFeudalWar 行 520)
  - 只有攻守两家：中央不援(MemberWarAid.Aidable 已排除)，别的成员不随(EmpirePeacePatch/ExpandSide 都不会对私战下令)
  - 城只能在"争的城"里易手：`HolyRomanSystem.BlocksFeudCity(newKingdom, city)` 挂在 CityPatch.cs:361 与 :973 旁(同 PettyRebellion.Blocks)——私战进行中，双方之间不在争城清单里的城一律不易手；都城永远不在清单里
  - 打满 3 年未了，年结强制议和(WarWinner.Peace)
- 结算：`EmpireWarType.私战` 加入 WarGoalSystem.OwnEnding；HolyRomanSystem.OnWarEnded 自己结——攻方胜或议和时攻方占了一半以上地块的争城，用新开的 `WarGoalSystem.QueueCession` 割给攻方；守方胜则攻方赔款(`WarReparationsSystem.Impose(attacker, defender)`)；记一笔到 court_log

### 帝国法院(帝国改革颁行之后)
- 私战禁止；原来 Reform.CourtRuling 的"每年三成自动判城"改为诉讼
- 起诉：够格原告(同私战的法理条件，皇帝家族领国也可以当原告、当被告)每年 15% 起诉，一案一城；每帝国待裁至多 3 案；原告可花年收一成"打点"(钱进中央国库)
- 审理满 1 年后每年 50% 宣判(平均约 2 年)
- 判给几率 = 0.35 + 0.15(原告是选帝侯) − 0.10(被告是选帝侯) + 0.10(原告与皇帝同族) − 0.10(被告与皇帝同族) + (权威−50)/200 + 0.10(打点)，夹 0.1~0.8；否则驳回
- 判给后被告抗判几率 = 0.25 ×(1.5 − 权威/100)+ 0.2(被告兵力 ≥ 原告两倍)，夹 0~0.7
  - 遵判：城归原告(court_rulings +1)，权威 +1
  - 抗判：记抗命(MarkDefiance，−2)再 −1；下次议会优先议对它的禁令——这就是"违者可遭禁令"
- 违禁私战：颁行后，权威 < 30、攻方兵力 ≥ 守方 1.5 倍且对守方好感为负的够格攻方，每年 2% 照样兴兵(同上规则，记 illegal)；开战即记抗命、权威再 −2，PickAgenda 把它排在禁令第一位

数据：
```
List<PrivateWarRecord> private_wars = new(); // war_id、attacker_id、defender_id、city_ids、at、illegal
List<LawsuitRecord> lawsuits = new();        // plaintiff_id、defendant_id、city_id、city_name、filed_at、bribed
List<string> court_log = new();              // 最近 8 起私战结果与裁决(已本地化)
int private_war_count, lawsuits_granted, lawsuits_refused; // 累计(court_rulings 沿用)
```
`EmpireWarType` 在 `征伐不服` 之后追加 `私战`(Scripts/Enums/WarType.cs:49，追加不改旧值)；语言文件补战争类型名"私战"。旧档：列表为空，court_rulings 照旧。

文件：新 HolyRomanSystem.Feuds.cs(私战、诉讼、违禁)；改 Reform.cs(ReformYearly 改调诉讼，CourtChance 删)、Diet.cs(OnWarEnded 私战分支、PickAgenda 违禁者优先)、WarType.cs、WarGoalSystem.cs(OwnEnding、QueueCession)、CityPatch.cs(两处拦城)、WarPatch.cs(跳过标记)、TemporaryFactionType.cs 与语言文件(治安令说明)、EmpireWindow.Diet.cs、语言文件。

AI：全由年结驱动，不走原版宣战 AI(原版与诉求宣战在同帝国之间本来就被拦)。

界面：新一排两栏的左卡"诸侯纷争"——改革前列进行中的私战("{攻方}↔{守方} · 争{城} · 第 2 年")，改革后列待裁诉讼("{原告}诉{被告} · {城} · 待裁 1 年")；卡头值写累计"私战 12 · 判城 5"；空时一行"太平无事"。

估算：分割继承把诸侯领地分给诸子后，兄弟之间互有法理，够格攻方常有 2~4 家。改革前每年新私战约 0.15 起(权威 50)，约 6~7 年一起，每百年 10~15 起、实际易手 5~8 城；权威掉到 25 时约 4 年一起。改革后诉讼约每 2 年一起，判城约每 5 年一座，抗判约 30 年一次，违禁私战只在权威 30 以下出现，约 50 年以上一次。

## 第 3a 步：选帝侯席位固定、可授可夺

- 世俗四席绑定领国：`state.elector_realm_ids`(4 项，成员国 id，−1 = 出缺)。第一次(空列表)按当前 CabinetMembers 后四席君主所在的国填入，保持现状
- SetCabinetForFeudalism(EmpireCraftEmpireBehCheckCabinet.cs:148-151)：HasDiet 的帝国改从 `HolyRomanSystem.SecularElectorKings(empire)` 取(按席位顺序，出缺填 −1)，不再按兵力重排；教会三席照旧。席位所在国当选成了中央，席位照样留着(皇帝自家领国可以握一票)
- 出缺：席位所在国亡、离开帝国、成了自由城市或主教领 → 年结标出缺，记 elector_vacant_at
- 被禁：席位停权(Voters、ElectorRealms 已排除被禁者，Diet.cs:71、HolyRomanSystem.cs:230)；禁令执行成功(Diet.cs:452 那一支)后褫夺——花 10 权威，席位出缺，记"褫夺{国}的选帝侯位"；权威不到 10 就只停权不褫夺
- 授予：有出缺满 1 年、权威 ≥ 15 → 花 15 授给"亲近的诸侯"：得分 = 对皇帝好感/100 + 与皇帝同族 0.5 + 皇帝党 0.2 + 城数名次 0.1；排除核心、自由城市、主教领、被禁者、已在座的
- 兜底：权威不到 15、出缺满 5 年 → 按旧例补给兵力最强的普通诸侯(不花权威)，免得选帝侯越来越少
- 上限：世俗 4 席不变(拍板 4)
- 神力按钮："授予选帝侯位"(有出缺时，人选同 AI，花 15)、"褫夺选帝侯位"(只对被禁者，花 10)

数据：`List<long> elector_realm_ids = new(); double elector_vacant_at = -1d; List<string> elector_log = new();`(最近 10 条)。

文件：新 HolyRomanSystem.Electors.cs；改 EmpireCraftEmpireBehCheckCabinet.cs、Diet.cs(禁令执行后褫夺)、EmpireWindow.Diet.cs、语言文件。

界面：第 1 步那排的右卡改为"选帝侯"——七行(三教会 + 四世俗)：席位名(国名)、在座者、状态(在座金、停权橙、出缺灰)；卡头用 AddCardTitle 附小按钮"授予""褫夺"。

估算：出缺主要来自外敌灭国与禁令褫夺，约 40~80 年一次；授予同频；褫夺约每百年一次(禁令每百年约两起，执行成功约一半)。

## 第 3b 步：选举拉票花钱、对立国王、大空位

入口：EmpireCraftKingdomBehCheckKing 在 `office.Select`(行 100-104)之前加 `if (HolyRomanSystem.TryHoldElection(pKingdom)) return BehResult.Continue;`——只对选帝制帝国核心无君时接管，返回真 = 已处理(选出了，或进入/维持空位)。选出的人照旧 `office.SetActor`(置 Electing)，换家族仍走 TryElectHouse。OfficeSelector 的 Vote 分支保留给古典共和与兜底，测试查的字符串不动。

候选：成员国君主(成年、能任职、非被禁)，按基础票势取前 4。储君在世直接即位(现状)。

投票：每位在座选帝侯给每个候选打分，投最高者：
- 自己 +1.0；与候选同族 +0.5；同派系 +0.3；好感(选帝侯→候选)/100 夹 ±0.3；同宗教 +0.1、异宗教 −0.4
- 国力 0.25 × min(1, 候选国力/最强候选国力)
- 皇族惯例：候选与当今皇族同族且已连任两届以上 +0.15
- 皇帝党/教皇党同党 +0.1
- 收了钱 +0.4 × min(1, 收的钱 / max(100, 选帝侯年收))
- 随机 ±0.1

计票：首轮过半(在座票数一半以上)当选；否则前两名决选，过半当选；决选平票 → 双重选举；在座选帝侯不足 3、或没有候选 → 大空位；权威 < 15 时另有 40% "选帝侯不肯会选" → 大空位。

拉票花钱：皇帝年满 55(没有储君时 50)起，每年基础票势前两名的候选各拿出 min(国库两成, 年收一成五)，分给还没倒向自己的选帝侯(钱进选帝侯国库，类目 Gift)；记 `campaign["候选国 id:选帝侯国 id"]`，每年衰减一半；选完清空。

选立储君也过票(ElectiveYearly，HolyRomanSystem.cs:149)：照旧每年两成发起，改为储君 vs 票势最高的诸侯决选，过半才立；皇帝替储君出钱(原 KingOfRomansCost 那笔改为拉票)，让步照旧。

对立国王(双重选举)：
- 首席选帝侯所投者即位；另一位称对立国王，权威 −10，记"选帝侯分裂：{甲}与{乙}各得{n}票，{乙}自立为对立国王"
- 开战"{对立国王国名}争位之战"(EmpireWarType 追加 `对立国王`)：攻方对立国王 + 投他的选帝侯所在国(各六成)，守方中央 + 投皇帝的选帝侯国(各六成)；同样置跳过标记，不拉姻亲、不升级
- CityPatch 同帝国攻下核心都城那一下 −40 正统(CityPatch.cs:615)，争位之战改为 −10
- 打满 5 年强制议和
- 结局：攻方胜 → 对立国王的领国 `TransferCoreTo(keepName: true)` 成为中央(置 Electing，不扣正统)，让步一项，权威再 −5；守方胜或议和 → 对立国王放弃，权威 +5

大空位：
- 核心(先帝家族领国)暂不立君；记 interregnum_since；帝国代理 = 在座世俗选帝侯中国力最强、不是候选的一位(没有就最强诸侯)，记 vicar_id / vicar_realm_id(拍板 3)
- 期间每年重开会选，成功几率 35% + 每过一年 +15%；满 5 年由帝国代理定(投代理首选者，代理首选自己也行)
- 权威开头 −10、每年 −4；正统吃现成的"无君 −20"；议会只议帝国战争与禁令(由帝国代理主持)；不选储君、不加冕、不授席位；私战几率 ×1.5
- 记"选帝侯不能决，帝位虚悬，由{代理}代理帝国"；结束时"虚悬{n}年，选帝侯推举{某}为皇帝"

数据：
```
Dictionary<string, long> campaign = new();
List<ElectionRecord> elections = new();   // 最近 8 次：at、winner、tally("甲 4 · 乙 3")、outcome
long anti_king_id = -1L, anti_king_realm_id = -1L, anti_king_war_id = -1L;
double interregnum_since = -1d;
long vicar_id = -1L, vicar_realm_id = -1L;
```
`EmpireWarType` 再追加 `对立国王`。旧档：全空，不在空位。

文件：新 HolyRomanSystem.Election.cs；改 EmpireCraftKingdomBehCheckKing.cs(入口一行)、HolyRomanSystem.cs(ElectiveYearly)、Diet.cs(空位期议程、OnWarEnded 争位分支)、CityPatch.cs:615、WarPatch.cs(跳过标记)、WarType.cs、EmpireWindow.Diet.cs、语言文件。

界面：皇位卡加三行——"会选"(拉票中时"拉票：{甲} 3 · {乙} 2"，否则上次结果)、"对立国王"(有时)、"帝国代理"(空位时，页头标签同时显示"帝位虚悬·{代理}代理")；纪事区加"历次会选"卡(AddListRow：年代、结果、票数)。

估算：选举约 20~30 年一次(有储君时跳过)；双重选举在座票数为偶数(席位出缺、被禁、先帝家族领国那一票空着)时才可能平票，约每次 8~12%，每百年 0.3~0.5 次；大空位主要在权威 15 以下，约每次 2~4%，一局约一次；拉票每次选举前 5~10 年，两家各花年收一成五左右。

## 第 4 步：陪臣化与帝国直属

陪臣化请愿：
- 请愿者：大诸侯(4 城以上或国力前三，非被禁、非自由城市、非主教领)
- 对象：与之接壤的一城小邦(普通诸侯，非核心、非选帝侯国)；被禁的小邦也可以
- 几率：每年 8% ×(1.5 − 权威/100)，每帝国 3 年至多一次
- 路线：已颁行帝国改革且请愿者对那座城有法理 → 走法院(判给几率同第 2 步，另 +0.1)；否则走议会(新议程 `mediatize`)：一两城的小诸侯 −0.4、自由城市 −0.2、大诸侯 +0.2、与请愿者同族 +0.5、与小邦同族 −0.5
- 帝国直属：表决、裁决之前皇帝可花 10 直接驳回。AI：权威 ≥ 40、请愿者不是皇族、小邦对皇帝好感 ≥ 0 → 七成驳回；请愿者是皇族 → 不驳回，议会里中央投赞成
- 通过：小邦的城并入请愿者(仿 ZhouEraSystem.TrySubmit 的 joinAnotherKingdom 循环)，原君主留任该城城主(陪臣)，记"{小邦}陪臣化，并入{大诸侯}，{君主}降为陪臣"；权威 −3(皇族得益则不扣)
- 驳回：记"{皇帝}维护{小邦}的帝国直属，驳回{大诸侯}的请求"

典押留底与赎回(改 Members.cs:139-168)：
- 典押照旧(中央缺钱时)，但记 PawnRecord(城 id、城名、受押国 id、押价、时间)
- 赎回：30 年内中央国库 ≥ 押价×1.2 的三倍、权威 ≥ 20 时每年三成赎回——付押价×1.2 给受押国，城重新 SpinOff 为自由城市(Members.cs:99)，权威 +3；受押国已亡、城已不在帝国则作废
- 满 30 年未赎：转为陪臣化(记一笔，删记录)

数据：
```
List<PawnRecord> pawns = new();
List<MediatizationRecord> mediatizations = new(); // 最近 10 起：at、small、lord、route、blocked
double last_petition_at = -1d;
long petition_lord_id = -1L, petition_small_id = -1L; double petition_at = -1d; // 待决请愿(给按钮用，一年内有效)
```
旧档：之前典押掉的城没有记录，不能赎回(在页面说明里不提)。

文件：新 HolyRomanSystem.Immediacy.cs；改 Members.cs(典押)、Diet.cs(议程 mediatize 的投票、AgendaText)、EmpireWindow.Diet.cs、语言文件。

AI：请愿、驳回、赎回都由年结驱动。

界面：第 2 步那排的右卡"帝国直属"：待决请愿一行(带"维护帝国直属"小按钮)、近年陪臣化与驳回、典押中的自由城市("{城} · 押于{国} · 赎价{钱} · 余{n}年"，带"赎回"小按钮)。

估算：一城小邦多来自分割继承与自由城市；请愿约每 8~15 年一次，皇帝驳回约一半，陪臣化约 20 年一起；典押照旧约 10~30 年一次，赎回约三成。

## 第 5a 步：保护人拉走诸侯同盟

- 条件：权威低于 20 连续满 12 年(回到 25 以上才重计；authority_low_since 第 1 步已在记)；每年 10%；两次之间至少 20 年
- 保护人：帝国外、不与中央交战、与至少一个成员接壤、国力(GetNationalPower)≥ 中央的 1.2 倍；同宗教的优先；不是教廷
- 被拉走的：与保护人接壤(连同彼此接壤连成一片)的成员里，对皇帝好感 < 10、或改奉别教、或有自主外交的，2~5 国，合计城数不超过帝国三成五；不含核心、不含主教领(和约后可含)
- 做法：各国 `empire.leave(k)`；同盟名"{盟主都城}同盟"(复用 hre_league_name)；保护人不是帝国时，保护人做盟主建模组同盟(仿 FormAntiEmpireAlliances)；保护人是帝国中央时同盟会被 ModAllianceService 解散，改记"保护约"(KingdomExtraData.hre_protector_id、hre_protected_at)，被保护的几国自己结盟(拍板 5)
- 保护约 30 年：保护人与被保护者互不攻伐(WarGate.Forbidden 加一条，同 WarGoalSystem.Installed 的写法)；前帝国中央或其成员攻打被保护者时，保护人自动加入守方——挂在 WarPatch.new_war(行 445 RoyalMarriageSystem.OnWarStarted 旁，同一类"开战即援")
- 权威 −10、正统 −10；记"{盟}在{保护人}的保护下脱离{帝国}"
- 第 1 步的快照比对见到这几国离开不再另扣

数据：HolyRomanState `double last_protector_at = -1d; int peak_members; List<string> departed_log = new();`；KingdomExtraData `long hre_protector_id = -1L; double hre_protected_at = -1d;`

文件：新 HolyRomanSystem.Decline.cs；改 WarGate.cs、WarPatch.cs、KingdomExtension.cs(KingdomExtraData 两个字段)、EmpireWindow.Diet.cs、语言文件。

## 第 5b 步：皇帝退位、帝国解散

- 条件：权威低于 10 连续满 20 年(回到 15 以上重计)，且(已有保护同盟离去 或 已订和约 或 成员数不到历史最多的一半)，且帝国立国满 80 年；每年 10%
- 做法：记"{皇帝}退位，{帝国}解散，诸侯各自独立"(世界提示 + 帝国史)；`EMPIRE_MANAGER.dissolveEmpire(empire)`；皇帝仍是自家领国的君主；原中央记 `empire_formation_failed_timestamp`(十年内不再称帝，拍板 7)；各成员恢复外交与开战权；主教领出缺时 TryAppointBishop 见帝国没了自然清标记(Members.cs:220-224)
- 设置开关：`ModClass.HRE_DISSOLUTION`(默认开，default_config.json + ConfigActions，拍板 9)
- 神力按钮：权威不到 10 时议会页出现"皇帝退位"按钮(二次确认用现有按钮行，不另做弹窗)

文件：HolyRomanSystem.Decline.cs；ModClass.cs、default_config.json、ConfigActions.cs、MainTab.cs(设置项)；EmpireWindow.Diet.cs；语言文件。

界面(5a/5b)：权威低于 25 或已有同盟离去时，在纷争/直属那排下面加一张"帝国存续"卡：式微年数("权威低于 20 已 9 年，满 12 年外国可拉走诸侯")、已离去的同盟与保护人、退位条件还差什么。

估算：只在订了和约或连遭大败之后才走得到。和约后权威约二三十年掉到 20 以下，再 12 年起有保护人拉人(一局约一到两批)，再约 30~50 年满足退位条件；从和约到解散约 60~100 年，一局 0~1 次；中间打赢几仗权威回到 25 以上就重计，有回头的余地。

## 议会页最终排版(卷册宽窗)

```
页头：帝国议会 · [选帝侯选举] [已加冕] [帝位虚悬·某代理]
KPI ：帝国权威 62·稳固 | 家族领地 31% | 皇族届数 2 | 上次议会 1年前 | 帝国禁令 无
两栏：三院席位          | 皇位(帝位、储君、加冕、会选、对立国王、帝国代理、帝国改革、诸侯主权、帝国战争)
两栏：帝国权威(刻度+收支) | 选帝侯(七席，授予/褫夺)
两栏：诸侯纷争(私战/诉讼) | 帝国直属(请愿、陪臣化、典押)
整宽：帝国存续(只在式微时)
整宽：议会纪事 / 历次会选 / 选举让步 / 诸侯同盟
按钮：召开议会 · 提请/解除禁令 · 颁行帝国改革
      册立自由城市 · 典押自由城市 · 封立主教领
      (授予、褫夺、维护直属、赎回挂在各卡标题条上)
```
都用现有部件：AddKpiRow、BeginColumns + BeginColumnCard(HalfWidth)、AddCardTitle 附小按钮、AddListRow、UIHelper.AddBarRow、AddButtonRow；不手搓布局。每步只加自己那张卡，前一步没做的卡不画。

## 文字规则

- 不出现"玩家""你""神之手"；按钮只写动作("授予选帝侯位""维护帝国直属""赎回典押""皇帝退位")
- 名字一律从实际的城、国、头衔、人名拼："{国}争{城}之战""{国}争位之战""{城}同盟""{国}禁令之战"；不新加史名(不写具体王朝、和约、选帝侯国的历史名)，不用"胡同"
- 新键前缀：`hre_auth_*`(权威收支)、`hre_feud_*`、`hre_lawsuit_*`、`hre_elector_*`、`hre_election_*`、`hre_interregnum_*`、`hre_mediatize_*`、`hre_pawn_*`、`hre_protector_*`、`hre_dissolve_*`、`diet_*`(界面)；cz/ch/en 三份同时加

## 日志与诊断

- 权威每笔变动只走 `ChangeAuthority`：记 authority_log；`FiscalDiagnostics.Count("神罗·权威+"/"神罗·权威-", |delta|)`；单笔 ±10 以上另打 `[EmpireCraft][神罗] {帝国} 权威 {前}→{后}：{缘由}`
- 年结每个神罗式帝国一行：`[EmpireCraft][神罗] {帝国} 权威{A}({档}) 本年+{x}/-{y} 私战{n} 待裁{m} 选帝侯{在座}/7 {虚悬n年}`
- 事件统计键：神罗·私战、神罗·违禁私战、神罗·私战割城、神罗·诉讼、神罗·判城、神罗·抗判、神罗·授选帝侯、神罗·褫夺选帝侯、神罗·会选、神罗·双重选举、神罗·大空位、神罗·陪臣化、神罗·直属驳回、神罗·赎回典押、神罗·保护同盟、神罗·帝国解散
- 存档核对(EmpireCraftModData.json 的 empireDatas[].holy_roman)：authority、authority_log、reform_at、private_wars、lawsuits、elector_realm_ids、elections、interregnum_since、pawns、mediatizations
- 每步做完在 Docs/test-checklist 里补几条验收(例：私战三年内必收场、都城从不易手、空位不超过五年)

## 需要拍板

1. 帝国改革怎么"花权威"：①改成议会议案，文化节点只当前提，颁行时扣 30(推荐)；②保持文化节点自动生效，只在节点开推时要求同文化神罗权威 ≥ 60
2. 治安令管不管私战：①不管，私战只看永久和平，把治安令说明改成"收回外交与对外开战之权、要求出兵"(推荐，合"已定"原话)；②也管，治安令下的诸侯私战算违令
3. 大空位怎么做：①核心(先帝家族领国)暂不立君，至多 5 年，帝国代理是最强的世俗选帝侯，只挂名、主持议会(推荐，有议立新君的先例，正统 −20 现成)；②先帝继承人先接管家族领国，帝位只挂"虚悬"标记(不会无君，但即位史书会把他记成皇帝)
4. 选帝侯席位：①绑领国、世俗四席不增，只在出缺时授(推荐)；②皇帝可以另加第五个世俗席
5. 保护人是帝国时：①记"保护约"，开战时保护人自动援被保护者(推荐)；②只许非帝国的国家当保护人
6. 恢复世袭皇权也花权威：①门槛 80、花 50(推荐，世袭帝位是皇权顶点)；②不动
7. 解散后原中央多久不能再称帝：①沿用称帝失败冷却十年(推荐，现成字段)；②三十年
8. 神力按钮遇到权威不够：①照扣到 0，不拦(推荐)；②不够就不让点
9. 帝国解体加设置开关：①加，默认开(推荐)；②不加

## 风险

- 与修 bug 的子代理冲突：两边都改 Diet/Members/Reform；每步开工前看对方提交，行号以落地后为准；它修过的数值不要改回去
- 空位期核心多年无君：别的系统对 `Emperor == null` 的容错没在多年尺度上验过。上限 5 年；年结日志带"虚悬 n 年"；先用测试档触发看一遍
- 私战拉进外人：姻亲、共主领国、世界大战升级都在 new_war 里跑在设类型之前，必须用静态标记跳过；禁令战现在也有这个问题，可顺手一起加
- 私战灭国：都城豁免 + 争城清单 + 两城上限 + 三年强和；验收看事件统计里"私战割城"不超过两座/场、没有成员在私战中亡国
- WarGoalSystem.Claimed 归到帝国核心的问题只对私战绕开，不去改它(别处的对外战争依赖这个口径)
- 帝国改革改为帝国级：旧档按文化节点迁移；InstitutionSystem 那边节点照常推进，不受影响
- 权威通胀或通缩：数值都是首版，靠年度日志调；改动集中在 ImperialAuthorityRules 与各分部顶部常量
- 选帝侯席位固定后，教会三席仍按现在的规则动态取，两种口径并存，界面要写清
- 退位解散后立刻有人再称帝：用称帝失败冷却挡十年
- 测试：tests/HolyRomanClaims.Tests.ps1 的字符串检查；新增 ImperialAuthorityRules 单测
- 手机版 NML 在手机上编译：新文件只 using 现有核心命名空间(System、System.Linq、UnityEngine、NeoModLoader.General、NeoModLoader.services、本模组命名空间)，不用 General.Event
- 性能：年结每帝国 O(城数)；快照只在内存、不进存档；开战钩子只看两个 KingdomExtraData 字段
