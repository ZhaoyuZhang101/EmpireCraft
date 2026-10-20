# 天下之名：华夏帝国法理国号与多京、西方普世之名、现代唯一合法政府 方案

起因：用户要国号跟着"据有什么法理、得了谁的认可"走——华夏据有帝国法理就以帝国法理为号，复国的冠方位用祖号，联合天下里的律令制帝国行多京、两直隶；西方的普世帝国之名要教宗认可才能用；现代华夏只有唯一合法政府才用天下之名。眼下国号几乎都在称帝那一刻按主法理(王国法理)定死(Empire.SelectFoundingEmpireName)，之后只有失都改称、北归、两朝分立、神授君权、现代统一核心几处零星改名；京城、第二个直隶、普世之名都没有。

## 已定

范围
- 只改华夏(制度线 Huaxia：华夏、中国、山海三文化)与西方(制度线 Roma，PapacySystem.IsWestern 的口径)两系；游牧、阿拉伯、印度、日本等其他文化的国号与天下之名照旧。
- 名字一律从实际的王国法理、帝国法理、天下、城、国名拼，不写死史名(下文的周、越、罗马、德意志只是举例)。

总框架："天下之名的取用条件"(三章共用)

| 名 | 存在哪 | 华夏古代 | 华夏现代 | 西方 |
|---|---|---|---|---|
| 王国法理名 | KingdomTitle.data.name | 有主法理即用(现状) | 同 | 同 |
| 帝国法理名 | EmpireCore.name | 据该帝国法理三分之二、称帝那一刻起用 | 不是唯一合法政府时用(现状) | 称帝即用本帝国的帝国法理名 |
| 天下之名 | 天下基名(天下名号的 {0}) | 不用 | 唯一合法政府才用 | 经教宗认可(三途之一)才用 |
| 例外 | — | 复国：方位 + 祖号 | — | — |

华夏(用户原话整理)
1. 有王国法理时用王国法理命名；据有某个帝国法理核心的三分之二时可以称帝(不论自己的主法理在不在这个帝国法理里、这个帝国法理眼下有没有主)，称帝那一刻改用这个帝国法理命名。
2. 复国例外：祖上以某帝国法理(如"周")建过帝国，这次称帝拿到的是另一个帝国法理(如南边的"越")，称帝那一刻国号为 方位 + 祖号("南周")，方位是新法理相对祖上法理。
3. 多京：天下里有多个帝国法理(联合天下)时，律令制帝国可行多京制——
   - 县道制度：最多四京，每京是所据帝国法理的首都；真正的都城在"用王国法理称帝的那个帝国核心"(即本帝国绑定的帝国法理，见默认 D1)；
   - 行省制度：设直隶，一般两个：一个皇帝直辖，一个归太子；没有太子就不设领导人，那个直隶无主。

西方(用户原话整理；第 5 条用户已定，不再拍板)
4. 西方(罗马一系)成为帝国后用本帝国的帝国法理名；经教宗加冕认可，可改用天下之名(普世帝国之名)。
5. 普世之名取自天下：天下名号的基名(西方天下默认"{0}普世帝国"里的 {0}，或玩家改的基名)，不取天下里最古老、最大的帝国法理；未获认可前只能用本帝国的帝国法理名。
6. 取得普世之名须满足其一：① 与教宗好感为正，正常请冕；② 施压或开战，逼迫/影响教宗加冕；③ 控制教宗国(打下、使成附庸或成员、教廷之囚)让教宗加冕。自行加冕(脱离罗马或无教廷，正统项 mandate_self_coronation)拿不到。

现代
7. 现代制度下华夏的唯一合法政府，国号用天下基名 + 现代政体后缀，不再用帝国法理名；失去唯一地位时各方另行称呼，重新统一后恢复。

拍板结果(用户 2026-10-20)
- 下文"拍板记录"26 处全按推荐 A，只有华夏第 6 点改 B：四方位；新法理就是祖上那个法理(同一位置)时称"后某"(方位词换成"后")。
- 默认 D1~D12 照本文。
- 实施顺序：第 0~2 步 → 第 5~6 步 → 第 7 步 → 第 3~4 步 → 第 8 步，每步单独提交、编译通过。
- 定下的要点一览：
  - 华夏：三分之二按城数、本国连同附庸；新增称帝路线「据有法理」走称帝剧情，称帝那一刻改号；免"国力须胜同文化各帝国"；旧朝还占着那个帝国法理时按僭越称帝，同名加方位；复国上溯世系、不限年数，四方位、同一法理称"后某"；复国方位定下后失都不再改方位(只显示行在)；四京含主京，陪京须控制该法理首都并据三分之二；京名按方位称东京、西京、南京、北京，主京称京师，城名不改；京当治所、稳定 +5、画宫城；第二直隶取那个京城所在的王国法理，无太子时真空君，行省阶段只留第二直隶那一京。
  - 西方：普世之名与"神圣"各自取得、可叠加，神授君权不再改国号；同一天下一位普世皇帝；绝罚、脱离罗马即收回(悔罪后恢复)，新君三年未由教宗加冕才收回；好感不正照旧加冕但不授名；加冕时兵临城下、国力三倍即算施压；新战争类别「逼冕」；控制教宗国(成员、附庸、教廷之囚)即授名，圣座帝国照旧；不另要求据有天下。
  - 现代：受本天下天命且天下里没有别的已组建政府才算唯一合法政府；只算现代政体与共和国；仍是本核心中央就留天下名，对立临时政府过半时以天下名自居并括注都城；天下加"基名"字段。

## 现状摸底

一、国号怎么来、怎么存
- 王国：每次同步类别都重算名字，前缀依次取 行政区省名 → 主法理名 → 无法理时的城名/随机名(AI/KingdomAI/EmpireCraftKingdomBehCheckKingdomType.cs:357-376)，后缀按类别，华夏律令制按朝代改称(GeneralSystems/DynastyNaming.cs:105-127)。"华夏有王国法理用王国法理命名"已是现状。
- 帝国：国号本名存在 EmpireData.core_name(Layer/EmpireData.cs:92)，SetEmpireName 同时写到核心王国的核心名上(Layer/Empire.cs:1273-1312，第 1309 行)；每月 SyncKingdomStatus 拿核心王国的自动名回写国号(EmpireCraftKingdomBehCheckKingdomType.cs:309-316)，所以写进 core_name 的名字是稳定的；换核心王国靠 KeepNameOnNewCore 把旧名写到新核心(Empire.cs:1317-1322，提交 05eddf0a)。
- 称帝定号：CreateNewEmpire 取 GetRiseCandidateCore 后调 SelectFoundingEmpireName(Empire.cs:1115-1119、441-484)：宗室复国路线用祖上国号并按 GetDir 加方位(463-466、479-482)；承续法统用家族发迹地的法理，其次都城法理(467-475)；其余按 EmpireFoundingNameRules.Select：主法理 → 祖上国号 → 祖地法理 → 君主持有法理 → 核心名 → 国名(Layer/EmpireFoundingNameRules.cs:45-60)。帝国法理名(核心名)只排第五，华夏称帝几乎总用王国法理名。
- 前缀只有一个槽 EmpireData.directPre(EmpireData.cs:87)，手选前缀 prefix_manual/prefix_choice 优先(EmpireData.cs:181-186，HelperFunc/OverallHelperFunc.cs:313-326)。写它的有九处：游牧"大"(Empire.cs:1080-1083)、宗室复国(481)、律令制篡位改号(700-712)、皇族接续 ReplaceEmpire(1469-1497)、失都改称(GeneralSystems/CapitalLossNamingSystem.cs:41-79)、北归(GeneralSystems/CompositeEmpireService.Conquest.cs:158-160)、两朝分立(GeneralSystems/EmpirePartitionSystem.cs:239-246、821)、神授君权"神圣"(Regimes/TemporaryFactions/Claims/TempFac_神授君权.cs:29)、西方第二帝国清掉方位(Empire.cs:1335-1347)。
- 显示：GetEmpireFullName 先看复辟帝制、军阀时期修饰(Empire.cs:377-383)；GetBaseEmpireFullName 里共和/现代有理念后缀时只拼"核心名 + 后缀"，不带方位前缀(401-417)；西方 western_ordinal ≥ 2 称"第二帝国"(428-432)。

二、帝国法理与"三分之二"
- 帝国法理 = EmpireCore(Layer/CoreEmpireTitle.cs:5-56)：名 name、首都 CoreCapital、下辖王国法理 titlesRecord，城自己记属哪个核心。新核心按称帝者的国号起名(Layer/CoreEmpireTitleManager.cs:113-134)，所以帝国法理名通常就是第一个据有它的帝国的国号；显示名 GetDisplayName(325)。
- 称帝绑哪个核心：GetRiseCandidateCore = 主法理首府所在核心 → 都城所在核心 → 任一持有法理所在核心(CoreEmpireTitleManager.cs:486-510)；EmpireManager.NewEmpire 里该核心已归别的现存帝国就新建核心(Layer/EmpireManager.cs:306-319)；僭越称帝走 forceNewCore + DeclareUsurpation(AI/EmpireCraftPlotsAddition.cs:2683-2695，GeneralSystems/ImperialLegitimacyChallengeService.cs:146)。
- 现有的三分之二只在王国法理一层，按地块算：宣称战与不战而得(Layer/DeJureTitleClaimRules.cs:7-31，GameClassExtensions/KingdomExtension.cs:2967-2980、3015-3059)。帝国法理一层：承续法统要核心无主、据 min(3, 总数) 个王国法理、都城法理在核心内、据过半城(GeneralSystems/EmpireFormationService.cs:135-151；GetRequiredRiseTitleCount 写死 3，CoreEmpireTitleManager.cs:519-526)。按城算控制的统一口径是 EmpireCoreControl(城归最上层宗主、帝国成员归核心王国，GeneralSystems/EmpireCoreControl.cs:9-77)，军阀时期、现代核心国号(GeneralSystems/EmpireCoreNamingSystem.cs:14-41)、天下都用它。KingdomExtension.CanBecomeEmpire(3118-3160)是没人调用的旧门槛。
- 称帝流程：王国行为里 CanStartEmpireFormation 通过就开称帝剧情(筹备期，失败冷却 10 年)(AI/KingdomAI/EmpireCraftKingdomBehCheckEmpire.cs:235-277)；剧情落定时 GetBestRoute 定路线(EmpireCraftPlotsAddition.cs:2675-2678)。基础门槛 MeetsBaseRequirements：独立、有君、主法理、国库不负、人口 200 城 3(无帝国 50/100 年放宽)(EmpireFormationService.cs:411-439)；GetBestRoute 先过"同盟里轮得到""国力胜过同文化每个帝国(连成员)"，再按 承续法统 → 宗室复国 → 怀玺 → 诸侯推戴 → 霸权 → 兜底 → 自立(97-132)。路线枚举 14-24，开国天命 49-60。Docs/plan-realm-institutions.md 第 4 步的 EmpireProclamationRules(国家阶段、直辖 8 城或附庸 3 国、文化圈唯一正统帝国)还没做，代码里没有这个类。周制另有限制(ZhouEraSystem.BlocksRivalEmpire、IsDetachedVassal，EmpireFormationService.cs:386-391)。

三、复国与方位
- 宗族只记最近一次以本族为皇族的帝国：国号 empire_name、都城 capital_city_id 与坐标、亡国时间(GeneralSystems/ClanSystem.cs:90-95，RecordHistoryEmpire 284-291，GetHistoryEmpire 311-321)；不记帝国法理(核心)编号；分家宗族(parent_clan_id，98)不继承这条记录；同世系判定 SameLineage(1316)。
- 宗室复国路线：国君本宗族有记录、亡国不满百年、同世系眼下没在统治帝国(EmpireFormationService.cs:154-163)；国号沿用祖号，方位 GetDir 按新帝国都城相对祖上都城坐标取四方位，恰好重合称"后"(Empire.cs:1250-1266)。
- DefeatedEmpireHouse 是征服者记下被灭皇族、用来封安乐公(Layer/DefeatedEmpireHouse.cs:7-33，Empire.cs:1845，GamePatches/WarPatch.cs:595)，不是复国判定。
- 失都改称：核心首都失陷满 50 年加方位(新都相对旧都四方位)，还都恢复(CapitalLossNamingSystem.cs:17-79)；记"原前缀"时凡原前缀是方位一律记成空(第 73 行)，所以带方位的国号失都再还都会丢掉原方位。"行在"：核心首都失陷期间核心王国类别改称行在(82-92，KingdomExtension.cs:2022-2026)。
- 北归：以旧中原核心首都为基准加方位，失都已加过的不改(CompositeEmpireService.Conquest.cs:124-160、205-210)。两朝分立：同号各加对立方位，合一时清空(EmpirePartitionSystem.cs:239-246、821-822)。

四、律令制的"县道"与"行省"
- 华夏制度树里没有叫"县道""行省"的节点(行省制度 provincial_administration 只在 InstitutionTrees/Youmu.json)。华夏律令制的区划称谓按朝廷阶段 CourtSystem.Stage(GeneralSystems/CourtSystem.Dynasty.cs:46-57)：施行三省六部制(节点 huaxia_three_departments，特性 three_departments，InstitutionTrees/Huaxia.json:918)为隋唐阶段——京畿"京畿道"、地方"道"、城"县"；施行内阁制(huaxia_grand_secretariat，特性 grand_secretariat，Huaxia.json:1025)为明阶段——京畿"直隶省"、地方"省"；更早的郡县阶段是司州/州/郡(DynastyNaming.cs:10-20、105-136，Locales/cz.json:617-622)。本方案把"县道制度"对应三省六部(Tang)阶段、"行省制度"对应内阁(Ming)阶段(默认 D5)。
- "直隶"在代码里就是帝国核心王国：律令制国家类别 LvLing_centre(条件 empire_center:true，Scripts/Regimes/Configs/LvLing/SystemConfig.json:416-430)，称谓键 dynasty_capital_*；地方 LvLing_province(考选、无外交，478-492)、军府、都护府、羁縻州、世袭封国 LvLing_kingdom；类别按配置条件逐条匹配(EmpireCraftKingdomBehCheckKingdomType.cs:43-240、442-474)。没有第二个直隶的概念。KingdomType 只能在末尾追加(Regimes/RegimeSystem.cs:17-67，旧档存的是枚举值)。
- 王畿：核心直辖都城、主法理、都城法理的城(Empire.GetProtectedDirectCityIds，Empire.cs:4348-4362)，行政区重组把其余按王国法理划成行政区(GeneralSystems/AdministrationReorganizationSystem.cs:80-160，保护判断 137)；建行政区见 Empire.EstablishTerritorialDivision(Empire.cs:4566-4578)、诉求设置行政区。
- 行政控制力：治所 = 本国都城(起点 0) + 各成员国都城(起点 4)，按地形最短路；满控路程 10 + 2×时代 + 科技，律令 ×1.15；直辖容量 4 + 4×时代 + 科技，律令 ×1.2(GeneralSystems/AdministrativeControlSystem.cs:15-46、49、276-303、377-410)；鞭长莫及扣自然稳定(GeneralSystems/CityStabilitySystem.cs:243-268)。

五、都城、宫殿、迁都
- 一国一都(Kingdom.capital)，帝国法理只记一个首都 CoreCapital；"还于旧都"每月把中央迁回本帝国法理首都并按新都改王畿(GeneralSystems/CoreCapitalRestorationSystem.cs:10-16、41-60)。营宫殿：帝国核心之国的城营宫殿 = 设为帝国法理首都并迁都(GeneralSystems/CityPlacementSystem.cs:122-150)，别国的城 = 迁本国都城并留宫城(153-180)。宫城画在都城、帝国法理首都，易主后、手营的留着(GamePatches/PureMapPatch.cs:830-851、898-930)。没有陪都、陪京。

六、太子与无主政区
- 储君 = 核心王国的 HeirID(KingdomExtension.cs:2384-2426)，钦定储君 designated_heir_id(283)，册立储君诉求只改这两项(TempFac_册立储君.cs:28-41)；皇子(储君除外)每年封王(GeneralSystems/PrinceLineage.cs:90-101)。储君本身是本帝国成员君主时可以入继，律令/周制的封国传给他的子女(EmpireCraftKingdomBehCheckKing.cs:130-196，keep_member_roles 185-196；ForeignRuler 见 EmpireCraftKingdomBehCheckHeir.cs:213)。
- 无君容错的先例：选帝制大空位时 TryHoldElection 返回 true，王国行为直接跳过选任，中央一直无君(GeneralSystems/HolyRomanSystem.Election.cs:122-140；调用处 AI/KingdomAI/EmpireCraftKingdomBehCheckKing.cs:101)；别的无君王国会被选任，无小人模式下从人口落成一人(102-111)；帝国核心无君满一年且无成员才去帝号(21-34、78-93)。只有帝国中央验证过长时间无君。

七、天下名号
- 天下 = 主核心 + 并进来的核心(CoreEmpireTitle.cs:37-56；GeneralSystems/TianxiaSystem.cs:77-106，IsJoint 106)；华夏、中国、山海标 Huaxia，罗马、法兰克、日耳曼标 Roma(Locales/Cultures/Culture_*/CultureRule.jsonc 第 8~9 行)，相邻同标识的核心每年自动并成联合天下(TianxiaSystem.cs:260-316)。
- 天下名 TianxiaSystem.Name(146-151)：有 tianxia_name 用它(整名)，否则 tianxia_default_name 格式化，{0} = 主核心显示名；按标识换词：Roma "{0}普世帝国"、Umma "{0}乌玛"、Steppe "{0}草原"(Term 54-65；cz.json:2457-2469、7302)。玩家改的是整名(tianxia_name_input，UI/Windows/TianxiaWindow.cs:68-70、UI/Windows/EmpireCoreWindow.cs:167-169)，没有单独存基名。
- 天命：据七成五受命，mandate_kingdom_id 记在主核心(TianxiaSystem.cs:33、407-408)；年度结算在 UnificationSystem.cs:550/674；读档首年天命得失只记史册不公告(TianxiaSystem.cs:559-574)。

八、西方：加冕、神圣、教宗国
- 教廷 = 控制本宗教圣地且奉本教的国家，其君主即教宗(GeneralSystems/PapacySystem.cs:69-86)；教廷之囚 = 圣地之主不是教宗国且在帝国里(89-93)；教宗国类别 Feudalism_papal_state(PapacySystem.PapalState.cs:11-24，5d94cc80 放宽到教权至上)；圣座帝国 = 教宗国自己是帝国中央，国号"某某圣座"(PapacySystem.Events.cs:34-39、169-177)；被挟满 20 年出对立教宗(大分裂，Events.cs:59-71)。
- 加冕(每年随 PapacySystem.Yearly，GeneralSystems/ConstitutionalEconomySystem.cs:248)：脱离罗马或无教廷 → 自行加冕；教廷就是本国、或在本帝国(无议会)→ 直接加冕；否则每年四成机会请冕，封建制帝国先远征一年(HolyRomanSystem.Leagues.cs:33-36、238-263)，与教廷交战不冕，献礼年收入一成(PapacySystem.cs:285-320)。Crown 记 crowned_emperor_id/self_crowned(322-341；Data/PapacyData.cs:19-46)，按位君主算，换君要重冕；正统：教宗冕、自冕、未冕满三年扣分、绝罚 −20(PapacySystem.cs:41、128-166)；神罗帝国权威教宗冕 +15、自冕 +8(HolyRomanSystem.Authority.cs:116-125)。没有好感门槛，没有施压、逼冕。上帝按钮 ActCrown/CrownBlocker(PapacySystem.Actions.cs:14-32，1d018b25)。
- 绝罚：与教廷交战按教权概率、叙任权之争、与挟持者交战(PapacySystem.Conflict.cs:42-91，Events.cs:73-82)；换君或教权 < 30 自动解除，可悔罪。
- 神圣：诉求「神授君权」——帝国里有教宗国(不是皇帝本国)、宗教等级政教合一时，directPre 设"神圣"并把国号改成教宗国都城所在王国法理的名字(原型"神圣罗马")(TempFac_神授君权.cs:22-62)；05eddf0a 起第二帝国、失都改称不冲掉"神圣"(OverallHelperFunc.cs:302-311)。
- 战争类别里没有逼冕的；诉求开战的写法见 TempFac_迫使朝贡(新开战争 + SetEmpireWarType)；EmpireWarType、TemporaryFactionType 都只能末尾追加(Enums/WarType.cs:25-53，Regimes/TemporaryFactions/TemporaryFactionType.cs:5)。

九、现代：中央政府与国号
- 军阀时期按帝国法理逐个算(GeneralSystems/WarlordEraSystem.cs:15-31、393-501)：现代政权据七成即统一该核心(UnifyShare 36，419-429；CompleteUnification 583-627，第 621 行 TryApplyCoreName 把国号本名改成核心名)；未统一时每种理念一个代表政府，最强为中央(Central)，华夏(strong_unification)中央始终是正常国家，其余代表政府为临时政府(Provisional)(482-492)，记在 core.central_empire_id/rival_central_empire_id(CoreEmpireTitle.cs:26-28)。
- 国号：中央 = 核心名 + 理念后缀(CentralStyleName 248-264)；临时政府 = 省份名 / 八方方位名(RegionName 210-230) / 据法统过半时以核心名自居并括注都城(ProvisionalSeat 173-204，ProvisionalGovernmentName 152-168)；未建政府的地方武装按理念称呼(92-109)。后缀词库 PartySystem.PickCountrySuffix(GeneralSystems/PartySystem.cs:1068-1077)。named_after_core_id 挡住月度同步把国号改回王国法理名(Data/ConstitutionalEconomyState.cs:148，EmpireCraftKingdomBehCheckKingdomType.cs:314)。
- 代码里没有"唯一合法政府"判定。最接近的：本核心中央且无对手(rival_central_empire_id < 0)、刚统一；天下一层是受天命(TianxiaSystem.MandateOf)。联合天下里各核心各算各的中央。
- 现代判定：RegimeType.Modern(GeneralSystems/ModernStability.cs:16)、改制共和 RepublicSystem.IsRepublic(GeneralSystems/RepublicSystem.cs:45)；复辟帝制国号"{国号}帝国"(GeneralSystems/RestorationSystem.cs:42-43)。

## 第一章 华夏：帝国法理国号与复国

### 第 0 步：公共算式与存档字段(玩法不变)
- 新文件 Scripts/Layer/RealmNameRules.cs(纯算式，配 tests/RealmNameRules.Tests.ps1)：`HoldsShare(held, total)`(常量 CoreNameShare = 2/3、CoreNameMinCities = 3)、`Direction4(from, to)`(东西南北取偏差大的轴；新代码统一用它，GetDir/DirectionFrom/Direction 三份旧写法不动)、`AssignJingDirections(...)`(多京按角度贪心分不重复的四方)、`SplitTianxiaBase(fullName, pattern)`(从"{0}天下""{0}普世帝国"整名拆基名)。
- 新文件 Scripts/GeneralSystems/RealmNamingSystem.cs：华夏/西方判定(InstitutionSystem.GetCultureLine(...) 为 Huaxia/Roma)、`DominionCore(Kingdom)`、`TianxiaBaseName(EmpireCore lead)`、`RestorationHouse(Actor)`。
- 存档字段(都有默认值，旧档读进来即"没有")：
  - SpecificClan.history_empire_core_id = -1：RecordHistoryEmpire 一并记 EmpireCoreManager.Get(empire)?.id；读档补录：核心名等于 empire_name 的正式核心 → capital_city_id 那座城眼下的核心 → 仍 -1(退回按坐标算方位)；
  - EmpireData：name_source(0 旧规则 / 1 王国法理 / 2 帝国法理 / 3 复国 / 4 天下之名)、named_core_id、founding_prefix(称帝时定下的复国方位)、pre_tianxia_name(改用天下之名前的本名)、jing(List<JingRecord>：core_id、city_id、方位、设立时间)、zhili_kingdom_id；
  - EmpireCore.tianxia_base_name(空 = 主核心名)；
  - PapalRelationState：universal_lead_id、universal_via、universal_at、universal_emperor_id；
  - ConstitutionalEconomyState.named_after_tianxia_lead_id；KingdomExtraData.zhili_empire_id。
- 设置：ModClass.REALM_NAME_RULES(「天下之名」，管四章的改名)、ModClass.MULTI_CAPITAL(「多京与直隶」)，默认开，default_config.json + ConfigActions。

### 第 1 步：据三分之二称帝、以帝国法理为号
- DominionCore(kingdom)：华夏；遍历正式核心(不含军阀临时子核心、伪核心)，总城 ≥ 3，按拍板 1 的口径据有 ≥ ⌈总城 × 2/3⌉；据两个以上时主法理所在核心优先，其次据城多的(默认 D2)。
- 称帝(拍板 2、3)：EmpireFormationRoute 末尾追加 `Dominion`(据有法理)，开国天命 80(同承续法统)；GetBestRoute 对华夏先判 Dominion，放在"国力须胜同文化帝国"之前；基础门槛、周制限制照旧；Diagnose 加一行"据有某帝国法理 x/y 城"；语言键 empire_formation_route_Dominion。
- 那个帝国法理还被旧朝占着(拍板 4 推荐)：照样称帝，按僭越(临时核心 + DeclareUsurpation)；与旧朝同名时新朝加方位(新都相对旧朝都城，Direction4)；正统之争胜出接管核心(ImperialLegitimacyChallengeService.cs:221、369)后去掉方位。
- 绑核心(默认 D1)：主法理所在核心空闲 → 绑它(都城留在本帝国法理，合第 3 条"真正定都")；否则据三分之二的核心空闲 → 绑它；都不空闲 → 临时核心。EmpireManager.NewEmpire(306-319)、BecomeEmpireAndStartEnfeoff(2683-2686)按此改。新建核心不与现有正式核心重名：newEmpireCore 加可选名参数，以帝国法理为号的帝国新建核心时用主法理名。
- 定号：SelectFoundingEmpireName 对华夏先看复国(第 2 步)，再看 Dominion → 核心显示名(来源 EmpireCore，新史书键 empire_name_source_dominion"据有{0}三分之二，以其名为国号")，否则照旧；承续法统据到三分之二同样改用核心名，不到照旧(家族发迹地法理)。name_source/named_core_id 记下。
- 已称帝的帝国后来据了别的核心三分之二：不改号，只进多京(默认 D3)；旧档不迁移。
- AI：沿用称帝剧情(EmpireCraftKingdomBehCheckEmpire.cs:235-247)，路线自动选，不另写。
- 日志：`[EmpireCraft][法理国号] {国} 据 {核心} {x}/{y} 城，称帝定号 {国号}(绑 {核心})`。

### 第 2 步：复国方位国号
- RestorationHouse(king)(拍板 5)：从国君宗族起沿 parent_clan_id 上溯，取第一个有帝国记录的宗族；那个帝国已亡(或已不由本世系统治)，本世系眼下没在别处称帝(同 MeetsRestoration 后半)。
- 定号(称帝那一刻，压过第 1 步与其余路线的王国法理名)：祖号 = 记录的国号本名(不含前缀)；方位 = 新法理(Dominion 核心，没有就本帝国绑定的核心)首都相对祖上帝国法理(history_empire_core_id)首都，旧档找不到核心就用记录的都城坐标；四方位，新法理就是祖上法理(或两个首都重合)时称"后某"(拍板 6 选 B)。directPre 与 founding_prefix 同写；史书"{君}复国，居祖法理{祖法理}之{方位}，称{全名}"。
- 宗室复国路线(百年内、天命 75)照旧只管天命，国号统一走本步。
- 前缀先后(默认 D4)：手选 > 两朝分立、北归(事件当时写入) > 失都改称 > 复国方位 > 游牧"大"；"神圣"与方位不冲突(西方另论)。复国方位与失都(拍板 7 推荐)：founding_prefix 非空时 CapitalLossNamingSystem.Check 不再加方位(只显示行在)；第 73 行改为原前缀等于 founding_prefix 时照记不清空。
- 律令制篡位改号(Empire.cs:700-712)、皇族接续(ReplaceEmpire 1475-1497)照旧——那是王朝内部更替，不是称帝。
- 日志：`[EmpireCraft][复国国号] {国} 祖号 {祖号}(祖法理 {核心})，新法理 {核心}，方位 {方位} → {全名}`。

## 第二章 华夏：多京与直隶

### 第 3 步：县道阶段的多京
- 适用：华夏、律令制核心(RegimeType.LvLing)、CourtSystem.Stage == Tang(默认 D5)、本帝国法理所在天下是联合天下(TianxiaSystem.IsJoint)。
- 新文件 Scripts/GeneralSystems/MultiCapitalSystem.cs，登记年度扫描"多京"(GamePatches/CityPatch.cs:1160-1192，WorldYearlyScans 与 WorldYearlyScanNames 两数组对齐)：
  - 主京 = 本帝国都城(还于旧都已保证在本帝国法理内)；
  - 陪京候选 = 同一天下里别的正式核心，按拍板 9 的条件据有；按据城数排，连主京最多四京(拍板 8)；
  - 京名(拍板 10 推荐)：按相对主京方位分东、西、南、北，各京不重复(角度最近的先挑)，主京称京师；城名不改，京名进城市提示、铭牌小字、帝国窗口；文本键 jing_name_east/west/south/north、jing_capital；
  - 撤京：京城离开本帝国、那个核心退出天下、离开县道阶段、改了政体；迁都到某京城后下次扫描重排(原主京若是别的核心首都转为陪京)。
- 效果(拍板 11 推荐)：各京当治所、起点 0(AdministrativeControlSystem.Compute 377-386 加入，京变动时 Refresh)；京城自然稳定 +5(CityStabilitySystem.cs:243-268 那串加一项"京师/陪京")；宫城(PureMapPatch.IsPalace 927 算上京城)；行政区重组照旧，京城不必归核心直辖，重组不动京号。
- 设、撤都自动(默认 D6)，记史书与世界日志"设{城}为{京名}""{京名}{城}陷，罢京"。
- 日志：年度 `[EmpireCraft][多京] {帝国} 京师 {城}；{京名} {城}(据 {核心} a/b)…`。

### 第 4 步：行省阶段的两直隶
- 适用：同上，但 CourtSystem.Stage == Ming(内阁制)。由县道进内阁、或之后年度扫描时转换。
- 主直隶 = 核心王国(现状，称直隶省)；第二直隶 = 据城最多的那个陪京，范围按拍板 12，归太子；其余陪京按拍板 14；更多核心也只设两个直隶(默认 D11)。
- 代码形态(默认 D7)：KingdomType 末尾追加 LvLing_zhili；LvLing/SystemConfig.json 的 kingdoms 加一项(新条件 zhili:true，排在 LvLing_province 之前；官职同省；本系统指定君主)；KingdomExtraData.zhili_empire_id 标记；称谓 = 京名方位 + "直隶"(新键 dynasty_zhili_ming"{0}直隶"，如"南直隶")。
- 建立：按王国法理把城划入新国(仿 TempFac_设置行政区 / EstablishTerritorialDivision，强制入帝国、无外交)；太子(core.GetHeir())setKing 就藩；册立储君、钦定储君后下次扫描换人。
- 无太子(拍板 13 推荐真空君)：EmpireCraftKingdomBehCheckKing.execute 在选任之前(仿第 101 行)对第二直隶直接返回，无小人模式也不落成；赋税全归中央(TreasurySystem.cs:186-196 加一条)；城官照常考选；无君不立军团。立了太子即就藩。
- 太子即位(默认 D8)：ChooseKingFromHeir 的 keep_member_roles 分支(185-196)对第二直隶不传太子子女，直隶空出等新太子；太子死、被废同理。
- 撤回：离开内阁阶段、失了那个京城、改政体 → 第二直隶改为普通省(LvLing_province，考选)，记日志。
- 日志：`[EmpireCraft][直隶] {帝国} 第二直隶 {国}({城数}城)：太子 {名} / 空缺`。

## 第三章 西方：普世帝国之名

### 第 5 步：西方帝国用本帝国法理名
- 西方称帝定号：绑了现成帝国法理的用它的名(承续法统不再用家族发迹地法理)；新建核心照旧(核心本就以国号为名)。换核心王国照旧 KeepNameOnNewCore。
- 神授君权按拍板 W1。
- 第二帝国：普世之名生效期间不显示代数(GetBaseEmpireFullName 428-432、SetEmpireName 1290-1295、ExtractEmpireCoreName 334-340 各加一判断)。

### 第 6 步：教宗认可取普世之名
- 普世之名 = 本帝国法理所在天下的基名 TianxiaBaseName(lead)：tianxia_base_name，空则主核心显示名；天下名号整名照旧("{0}普世帝国")。不是联合天下时基名就是本帝国法理名，授了也不改名，只记身份(默认 D9)。
- 授予：pre_tianxia_name = 现本名，core_name = 基名(SetEmpireName，"神圣"前缀保留)，记 universal_*；史书 + 世界公告"{教宗}于{圣城}为{皇帝}加冕，{旧号}改称{新号}"。
- 三途：
  - ① 正常请冕：Coronation 走教宗加冕(非自冕)时教廷对本帝国核心王国好感 > 0 → 授名；好感不正见拍板 W4。
  - ② 施压：见拍板 W5(推荐：加冕远征到达时本帝国连成员国力 ≥ 教廷连宗主三倍即授名，教权 −5，教廷记"挟冕之辱"好感 −30、二十年回复；没有远征的非封建帝国用新诉求「请冕」同样判)。
  - ② 开战：见拍板 W6(推荐：新诉求 TempFac_逼冕、新战争类别「逼冕」，均末尾追加；目标教廷；打赢由和约加冕授名并解除这场战争招来的绝罚，教权 −10；打输正统 −5、十年不再逼冕)。开战期间照旧可能被绝罚(Conflict.cs:62-67)。
  - ③ 控制教宗国：见拍板 W7(推荐：教廷是本帝国成员、本帝国连成员的附庸、或教廷之囚的挟持者在本帝国，加冕即授名，不看好感；神罗照旧要远征)。
  - 自行加冕不授。ActCrown 上帝按钮按当时实情判；教廷页另加"授普世之名 / 收回"两个上帝按钮。
- 唯一性见拍板 W2，收回见拍板 W3，是否另要求据有天下见拍板 W8。
- AI(PapacySystem.Yearly 里按序)：已授 → 无事；联合天下里未授：好感 > 0 → 照常请冕；好感 ≤ 0 的封建帝国 → 远征到达时自然判施压；否则国力 ≥ 三倍、接壤或水师可达、没在打大仗 → 推「逼冕」诉求(ClaimAgendaSystem 权重仿迫使朝贡，ClaimTuning 定费用与冷却)；教廷是附庸或成员 → 直接加冕。
- 读档(默认 D10)：第一年按新规核一次，已由教宗加冕(非自冕)且好感 > 0 或控制教廷的授名，只记史册不上公告。
- 日志：`[EmpireCraft][普世之名] {帝国} 经{途径}得 {基名} / 失({缘由})`；年度教廷诊断行加"普世皇帝 {帝国}"。

## 第四章 现代：唯一合法政府用天下之名

### 第 7 步
- SoleGovernment(empire)：华夏(强统一文化)、现代(拍板 M2)、按拍板 M1 判"唯一"；WarlordEraSystem 加 `GovernmentsIn(EmpireCore lead)` 从 Roles 缓存查天下各核心的中央与临时政府。
- 定号：天下基名(拍板 M4) + 理念后缀(ideology_country_suffix，没有按现行补)。在 ConstitutionalEconomySystem.cs:219 的 WarlordEraSystem.UpdateWorld 之后调 TianxiaNaming.Yearly：符合 → core_name = 基名，记 named_after_tianxia_lead_id、pre_tianxia_name；CentralStyleName 对已用天下名的直接用基名；EmpireCoreNamingSystem.TryApplyCoreName 对已用天下名的跳过。
- 失去唯一地位见拍板 M3(推荐：仍是本核心中央就留天下名；对立临时政府据法统过半时以天下名自居并括注都城，ProvisionalSeat 的 national 分支改取天下基名；丢了天命且中央之位被人取代才收回，改回 pre_tianxia_name)；重新成为唯一合法政府即恢复。
- 衔接：现代国号本就不带方位前缀(Empire.cs:401-417)，复国方位只在帝制时有；复辟帝制自动成"{天下基名}帝国"(RestorationSystem.cs:42)；改制共和那一刻已是唯一合法政府的直接用天下名。
- 日志：`[EmpireCraft][天下国号] {政府} 为 {天下} 唯一合法政府，{旧号} → {新号}` / 收回。

## 第 8 步：界面、文本、测试
- 帝国窗口国号行悬停说国号来源(王国法理 / 帝国法理 / 复国 / 天下之名)与取用条件；教廷页加"普世之名"栏(已授、未授及缘由、两按钮)；帝国窗口加"京"卡(按「卷册」宽窗口排版)：各京、所据核心、方位、第二直隶与太子。
- 城市提示加京名，地图铭牌京城加小字；天下窗口、帝国法理窗口加"天下基名"输入。
- FormationDiagnostics/Diagnose 加据有法理一行。新文本 cz/ch/en，不写史名。
- 测试：新增 tests/RealmNameRules.Tests.ps1；改动前先跑 EmpireFoundingName.Tests.ps1、DeJureTitleClaim.Tests.ps1 等按源码字符串检查的测试记基线；验收清单写进当天的 Docs/test-checklist-*.md。

## 节奏
- 建议：第 0~2 步(华夏国号) → 第 5~6 步(西方) → 第 7 步(现代) → 第 3~4 步(多京直隶，牵涉行政区与无君政区，风险最大)，每段进游戏看过再做下一段。

## 风险
- 前缀一个槽九处写：复国、失都、北归、分立、神圣、手选会互相覆盖；加 founding_prefix 并定先后后，九处仍要逐一核。
- 同名：以核心为号的新朝与旧朝同名、新建核心重名、西方第二帝国代数接到普世之名上。
- 无君的第二直隶：原版与模组里假定 kingdom.king 非空的地方(军团、外交、官职 SetActor、赋税、各 AI 行为)要逐个查；先例只在帝国中央大空位验证过。
- 太子在外就藩：继承(ChooseKingFromHeir)、ForeignRuler、封王、皇子争储都要认这种情况，太子成了"别国君主"可能触发既有的兼领逻辑。
- 多京当治所会让律令大帝国的控制力、过度扩张门槛明显放松，数值要进游戏看。
- 逼冕战与"与教廷交战即可能绝罚"叠加，可能打赢还背着绝罚，和约要一并处理。
- 读档首年追授、改名刷屏：首年只记史册。现代判定依赖 WarlordEraSystem 的年度缓存，读档后头一次扫描前判不了。
- 工作区里 ModClass.cs、default_config.json 有别的会话未提交的改动，加设置项时按"另一会话同改工作区"的做法分开提交；新文件不 using 非核心程序集命名空间(手机版 NML)。

## 拍板记录(推荐的放第一；2026-10-20 用户全选 A，只有华夏第 6 点选 B)

华夏
1. 三分之二怎么算：A 按城数，本国连同附庸(EmpireCoreControl 口径)；B 按城数，再算上同盟与共主(称帝 bloc 口径)；C 按该帝国法理下控制首府的王国法理个数。
2. 转名与称帝：A 新增称帝路线「据有法理」，走现有称帝剧情(筹备、失败冷却)，称帝那一刻改号，称帝前王国仍用王国法理名；B 满足即自动称帝改号(年度扫描，不走剧情)；C 王国据三分之二先改称帝国法理名，称帝照旧走剧情。
3. 与称帝门槛衔接：A 仍过基础门槛(独立、有君、国库不负、人口城数、失败冷却、周制限制)，免"国力须胜同文化各帝国"，第 4 步"文化圈唯一正统帝国"做了以后对它也免；B 现有门槛全照旧，只多一条路线；C 据三分之二即可称帝，基础门槛也免。
4. 那个帝国法理还被旧朝占着：A 照样称帝，按僭越(临时核心、互为正统对手)，与旧朝同名时新朝加方位，胜出接管核心后去掉；B 旧朝还在就不开这条路线；C 照样称帝但先用自己的王国法理名，夺得核心后再改。
5. 复国判定：A 国君本宗族或同一世系的上支(沿分家上溯)曾以某帝国法理称帝，那个帝国已亡，本世系眼下没在别处称帝，不限年数；B 同上但限亡国百年内(同宗室复国路线)；C 只认国君本宗族(现状)。
6. 复国方位：A 新法理首都相对祖上法理首都，四方位；新法理就是祖上法理时不加方位、直接复用祖号；B 四方位，同一法理称"后某"；C 八方位(如"东南某")。
7. 复国方位与失都改称：A 复国方位是本朝定号，失都不再改方位，只显示"行在"；B 失都照旧改方位，还都后恢复复国方位；C 照现状(失都覆盖，还都后复国方位丢失)。
8. "最多四京"含不含主京：A 含，主京加最多三陪京；B 不含，主京外最多四陪京。
9. 陪京条件("所据帝国法理")：A 控制该核心首都且据有其三分之二城(同第 1 条)；B 控制首都即可；C 控制首都且据过半。
10. 京名：A 按相对主京方位称东京、西京、南京、北京，主京称京师，城名不改，提示与铭牌注京名；B 帝国法理名 + 京(如"越京")；C 城名直接改成京名。
11. 京的效果：A 当行政治所(同都城起点)、京城稳定 +5、画宫城；B 只当治所；C 只是称号。
12. 第二直隶的范围：A 那个京城所在王国法理在本帝国的城(同王畿规格)；B 那个帝国法理在本帝国的全部城；C 京城周边按路程划若干城。
13. 无太子时第二直隶：A 真空君(仿大空位跳过选任)，城官照常、赋税全归中央、不立军团，立太子即就藩；B 暂由核心王国代管(城并回核心)，有太子再分出；C 中央考选一员署理，名义上无主。
14. 进入行省阶段时其余陪京：A 只留第二直隶那一京，其余撤京号；B 都留京号(效果照旧)，只一京设直隶。

西方
- W1. 普世之名与"神圣"：A 各自取得、可叠加(神圣 + 基名 + 帝国)，神授君权只加"神圣"、不再改成教宗国都城法理名；B 得普世之名即称神圣，撤掉神授君权诉求；C 神授君权照旧改名，普世之名另行(两次改名会互相覆盖)。
- W2. 同一天下几位普世皇帝：A 一位，已有未失格的，教宗不给别家授名(加冕照旧)；B 后授的顶掉先授的；C 大分裂(对立教宗)期间可两位。
- W3. 收回：A 绝罚、脱离罗马即收回，悔罪解除绝罚后恢复；换皇帝不收回，新君三年仍未由教宗加冕才收回；B 一经授予随帝国到亡，只有被顶替才收回；C 每位新君须重冕，未冕即收回。
- W4. 好感不正时请冕：A 加冕照旧(正统、帝国权威不变)，只是不授普世之名；B 教宗拒绝加冕。
- W5. 施压：A 加冕远征兵临城下，到达时国力 ≥ 教廷三倍即授名(教权 −5、教廷记恨)，没有远征的帝国用诉求「请冕」同样判；B 只做诉求施压，不出兵；C 不做施压，只做开战。
- W6. 开战：A 新战争类别「逼冕」+ 新诉求，打赢由和约加冕授名并解绝罚；B 复用「迫使朝贡」类别，打赢附带加冕。
- W7. 控制教宗国：A 教廷是本帝国成员、附庸或教廷之囚在本帝国都算，本国就是教宗国的圣座帝国照旧称"某某圣座"；B 只算成员与本国；C 圣座帝国也改用普世之名。
- W8. 另要求据有天下：A 不要求，只看教宗认可；B 须是天下里最强的帝国；C 须受天下天命。

现代
- M1. 唯一合法政府：A 受着本天下天命，且天下各核心里没有别的已组建政府(中央或临时政府)；B 本核心统一(军阀时期结束)的中央，不看天命；C 天下各核心的中央都是它。
- M2. 范围：A 现代政体与共和国；B 再加君主立宪(施行宪政后的帝制，称"{基名}帝国")。
- M3. 失去唯一地位：A 仍是本核心中央就留天下名，对立临时政府据法统过半时以天下名自居并括注都城，其余照旧，丢天命又失中央之位才改回核心名；B 一失唯一地位即改回核心名，双方照现行；C 双方都用天下名，一律括注都城。
- M4. 天下基名：A 天下加"基名"(空 = 主核心名)，天下窗口可改，旧档手改的整名能拆则拆；B 只用主核心名，不理手改的名号。

## 默认(不另问，写明以便日后改)
- D1 绑核心：主法理所在核心空闲先绑它，其次据三分之二的核心，都不空闲建临时核心；新建核心不与现有正式核心重名。
- D2 同时据两个核心三分之二：主法理所在核心优先，其次据城多的。
- D3 已称帝的帝国后来据别的核心三分之二不改号(只进多京)；旧档不迁移。
- D4 前缀先后：手选 > 两朝分立、北归 > 失都改称 > 复国方位 > 游牧"大"。
- D5 县道 = 三省六部(Tang)阶段，行省 = 内阁(Ming)阶段；秦汉郡县阶段不设京。
- D6 京自动设、自动撤，记史书与世界日志。
- D7 第二直隶用新国家类别 LvLing_zhili(仿神罗自由市、主教领的做法)。
- D8 太子即位后第二直隶空出等新太子，不传太子子女。
- D9 不是联合天下时普世之名就是本帝国法理名，授了不改名、只记身份。
- D10 读档首年按新规追授普世之名，只记史册不公告。
- D11 行省阶段一般只设两个直隶，核心再多也是两个。
- D12 数值：核心至少 3 城才算三分之二；据有法理开国天命 80；京城稳定 +5；施压国力三倍、教权 −5、教廷好感 −30 二十年回复；逼冕打赢教权 −10，打输正统 −5、冷却 10 年。
