# 帝国盒子（EmpireCraft）终端核心「政治版图 300 年几乎不变」诊断

> 仓库 `/home/user/EmpireCraft`，分支 Preview。本文件只做诊断，未改任何代码。
> 所有 file:line 均为当前 Preview 分支代码行号。给的改法只是方向，供不了解本仓库的人动手。

---

## 先回答协调者追加的问题：服务器版有没有「根本没跑模拟 / 把 AI 整段跳过」？—— **没有**

结论：**终端核心（服务器版）照常跑完整的 WorldBox 模拟与模组 AI，不是「服务器没推进」导致的冻结。** 证据：

1. **轻量主机模式只停画面声音，世界照跑。**
   `Scripts/Terminal/LightweightHostMode.cs:15-18` 类头明写：「停掉本机的画面和声音……**世界照常走——原版主循环、月度结算、自动存档都不动**」。`LightweightHostPatch.cs` 只拦渲染入口，不碰模拟。

2. **独立服务器（.server）只是一个外壳，世界仍在游戏进程里跑。**
   `Scripts/Terminal/TerminalHost.Standalone.cs` 把独立服务器接进「终端主机」，主机本身就是跑着的游戏；服务器只负责账号、席位、上传剧本。没有另一套精简主循环。

3. **模组的战略调度器两种人口模式都跑 AI 谋划，没有 server 分支。**
   `Scripts/GeneralSystems/EmpireCraftStrategicScheduler.cs:291-350` 的 `ProcessKingdom`：
   - 非无小人模式：`:319` 调 `PlotCheck.execute(kingdom)`；
   - 无小人模式：`:342 case 3` 同样调 `PlotCheck.execute(kingdom)`。
   `UnificationSystem.Tick`、`SchismSystem.Tick`、各 System.Tick 都在 `:70/:135` 等处无条件调用。没有 `if (server) return;` / `if (!AbstractPopulationEnabled) return;` 把这些整段跳过。

4. **无小人模式（服务器默认开）不让名人「睡觉」，君主照跑原版 AI 的开战谋划。**
   `Scripts/GamePatches/NoCommonersPatch.cs:729-734 BeforeAddStatusEffect`：无小人模式下，有城的名人（含君主、城主、官员）被拦下「sleeping」状态，不睡觉 → 君主仍是活的实体 Actor，原版 Actor AI 照常给它跑 `new_war` 谋划（谋划接线见 `EmpireCraftPlotsAddition.cs:2455-2508`）。模组甚至专门为无小人模式加了 `SkipsWarPlotRulerGate`（见下）来放宽君主门槛——说明设计本意就是「无小人也要打仗」，不是关掉。

5. **无小人模式是终端核心的默认。** `Scripts/GameLibrary/EmpireCraftWorldLawLibrary.cs:228` `empirecraft_law_no_commoners` **`default_state = true`**。所以服务器世界默认跑抽象人口；但如上，抽象人口并不关掉开战/叛乱 AI。

> 因此协调者的二选一答案是：**「规则太严 + 世界结构性一家独大」，不是「服务器没跑模拟」。** 下面逐条给根因。
> 但要强调一个结构性结论（见最后一节）：近期大量「降频/加闸」只压了**离心力**（开战、叛乱、自立），**向心力**（统一、归附、称帝、附庸）仍满速运行，二者不对称，世界先被吸成几个大帝国，然后锁死。

---

## 症状一：各国不主动发动战争 / 版图冻结

### 根因（最关键）
**所有开战路径都强制过「法理相连且走得通」的可达闸（`WarReach.WarNear`），而「走得通」依赖无人区里修了路；无人区默认开、把大陆切块，新图又几乎没有跨无人区的路，于是大量本可开战的邻国被判「够不着」，谋划挑不出目标。** 叠加「帝国成员根本不能自己开战」+「只打比自己弱的」+ 多道冷却/军饷闸，世界一旦被少数大帝国占住就没仗可打。

### 证据与机制

**（A）可达闸是所有开战路径的公共前置，判得很严。**
- 独立王国：`Scripts/GamePatches/WarTargetPatch.cs:74-80`、`:93`、`:100` —— 挑宣战目标必须 `IsNeighbour=WarReach.WarNear`，不接壤/走不通就 `__result = NearestNeighbourTarget(...)`，挑不到就不宣战。
- 帝国/成员/附庸：`Scripts/AI/EmpireCraftPlotsAddition.cs:2880`（`IsValidModWarTarget` 里 `WarReach.WarNear`）、`:2894`（`IsValidImperialWarTarget` 里 `WarReach.WarNear`）。
- `WarReach.WarNear` 定义 `Scripts/GeneralSystems/Warfare/WarReach.cs:157-161`：`Borders(...)` 或隔海 `StraitReach`；`Borders`（`:174-199`）要求邻城之间 `WildernessPassage.Walks` 走得通。
- `WildernessPassage.Walks`（`Scripts/GeneralSystems/WildernessPassage.cs:137-141`）：军队要能真走到，连高山、山脊、过不去的无人区一起算。无人区里**没修路**就过不去（定居之国；`Blocking` 见 `:197-213`，只有 `RoadNetwork.IsRoad / IsTrail` 或丘陵放行）。

**（B）无人区默认开，专门用来「阻隔国家军队」，把连通性切碎。**
- `Scripts/GeneralSystems/WildernessSystem.cs:204 Enabled`，类头 `:47` 明写「模组设置『无人区』**默认开**」。
- 类头缘由 `WildernessPassage.cs:8-9` 引用户原话：无人区「主要是为了把地图填满，同时能更有效地**阻隔国家军队**」。
- 代码自己给的量化（`WarReach.cs:22-23` 类头注释）：三国剧本 90 年存档里，真接壤的 201 对国家里 **59 对一处都走不通**；20 场仗有 6 场两边走不到一起。这还只是旧图；新开局路网更稀，比例只会更高。
- 通达层 `ConnectivitySystem` 读档后要几秒~几十秒才 Ready（`ConnectivitySystem.cs:205-219 PassDue`，首轮等 `RoadWaitSeconds=20s` 或路网规划完成），没算好时 `WarReach.LandCostWithin`（`WarReach.cs:289-294`）回退到沿直线估阻力——对切块地图偏保守。

**（C）帝国成员根本不能自己对外开战 → 大帝国世界里绝大多数国家不是开战主体。**
- `Scripts/AI/EmpireCraftPlotsAddition.cs:2822-2826`：
  `UsesVanillaWarPlot = !isEmpire && !inEmpire && !hasOverlord`；
  `CanInitiateNormalWar(isEmpire, inEmpire) = isEmpire || !inEmpire`。
  → 只要 `inEmpire`（是某帝国成员）且自己不是帝国核心，`CanInitiateNormalWar` 为假，`CanStartModWar`（`:2744-2746`）当场返回 false。成员对外的仗只能靠「随中央参战」（`EmpireCraftKingdomBehCheckPlots.CheckJoinWar`）。
- 300 年后世界多半被几个大帝国吸收（见最后一节），能当「开战发起方」的只剩**帝国核心 + 少数完全独立的王国**，发起方池子本就很小。

**（D）这少数发起方，再被一串闸层层筛。**
- 帝国开战要过**君主意愿闸** `RulerQualitySystem.WillsWarThisYear`（`EmpireCraftPlotsAddition.cs:2748`）。意愿按资质：`RulerQualitySystem.cs:194-204` —— 明君/暴君/开国/中兴=1.0，中庸=0.7，暗弱=0.4，昏淫=0.2。一个由「人口里新补出来、武略只有三四点」的平庸皇帝在位时，一年只有 0.2~0.7 概率会谋划开战。
- 只打比自己弱的：`IsValidModWarTarget :2881`、`IsValidImperialWarTarget :2899` 都要 `WarSituation.Power(initiator) > Power(target)`。一家独大时，够得着的对象要么是更强的霸主（不敢打），要么已经是附庸（不能打）。
- `IsWarNeededForPlot`（`:2904-2923`）：`TooBusyForWar`（手上 ≥2 场仗，`WarSituation.cs:54-56`）、距上次战争 `diplomacy_years_war_timeout` 内不打、兵力要过 `diplomacy_years_war_min_warriors`。
- 停战冷却被**调高**：`EmpireCraftPlotsAddition.cs:2443-2444` 把 `diplomacy_years_war_timeout=3`、`minimum_years_between_wars=5`（原版默认更短）。`IsValidModWarTarget :2864-2866` 与 `WarTargetPatch.cs:101-102`、`WarGate.CanDeclare`（`WarGate.cs:28`）都查这个 5 年。
- 军饷闸：`WarTargetPatch.cs:47 CorpsSystem.CannotFundWar`、`EmpireCraftPlotsAddition.cs:2793 ActModWar` 同样查。`CorpsSystem.CannotFundWar`（`Scripts/GeneralSystems/Warfare/CorpsSystem.cs:434-441`）：欠饷、或国库不足安全储备 1/4 且经常性亏损，就不主动开战。
- `WarGate.Forbidden`（`WarGate.cs:34-60`）：姻亲、通好、分治兄弟、共主与共事部落、同盟、朝贡两头、封建上下级、保护约、同君——一长串不打的关系。大帝国体系里这些关系密布，等于把残存的邻接也大量划掉。
- 世界法则闸（默认关，但要排查玩家有没有手开）：`empirecraft_law_ban_ai_war`（默认 `false`，`EmpireCraftWorldLawLibrary.cs:293-299`）。若被手开，`WarReach.LawForbids`（`WarReach.cs:81-90`）在开战总入口一律拦下，`WarTargetPatch.cs:28-33` 直接返回 null。**诊断时先确认该世界此法则是否被打开**——一旦开了，就是「一刀切没仗打」，与可达闸无关。

**（E）残留用原版邻接的点（影响面小，但记一笔）。**
- `EmpireCraftPlotsAddition.cs:336` 朝贡、`:2820 HolyWarNeighbour`（圣战）仍用原版 `IsNeighbourWith`——只放宽这两条，不改开战冻结的主因。

### 判断（症状一）
主因排序：**① 没对象**——可达闸 `WarReach.WarNear` + 无人区默认开把大陆切块 + 新图缺路，使大量邻国被判「够不着」；**② 发起主体少**——帝国成员不能自开战，一家独大后发起方池子塌缩；**③ 残存发起方被一串「只打更弱 + 5 年冷却 + 君主意愿 + 军饷」闸筛掉**。三者叠加，而不是单一闸。**最卡的一两道是「可达闸 × 无人区切块」与「成员不能开战 × 一家独大」。** （若玩家开了 `ban_ai_war`，那它单独就能解释，优先排除。）

### 建议改法方向
- 给可达闸一个「宽限/回退」：通达层未 Ready、或路网稀疏时，放宽到原版接壤（`WarReach.WarNear` 在无人区未充分探路前退回 `IsNeighbourWith`）；或把跨无人区的「可走」判定对 AI 挑目标放宽（丘陵/小径以外也允许沿直线阻力估算）。
- 让无人区默认关、或在终端核心世界默认不切块，至少让主干道更早铺（`RoadNetwork` 规划提前）。
- 复核「帝国成员不能开战」：允许有自主外交权的诸侯/附庸在一定条件下对**外**（非本帝国）开战，别让成员沦为纯随征。
- 把 `minimum_years_between_wars` 从 5 适当下调、`WillsWarThisYear` 的昏淫档从 0.2 适当上调，别让平庸君主几乎不谋划。

---

## 症状二：各国不叛乱 / 行政区不脱离

### 根因（最关键）
**叛乱/自立的触发条件被多轮「降频/加闸」同时抬高（向背门槛由 0 抬到 −50、现代政体概率×0.5、分封小乱钉死 ≤2 城、各种宽限期），而无小人+一家独大又让叛乱的驱动力（民怨、正统跌破）难以累积到新门槛。于是要么根本不触发，要么触发了也推不翻国家。**

### 证据与机制

**（A）拥兵自立「加忠诚余量」：门槛从 0 抬到 −50，等于绝大多数成员永远不自立。**
- `Scripts/GeneralSystems/RebellionSystem.cs:44 SecessionThreshold = -50`，类头 `:41-43` 原话：以前合计 <0 就自立，现在要「明显离心（−50 以下）才自立」。
- `:40 SecessionLegitimacyWeight = 5`、`:54-65 SecessionAllegiance`：好感里**去掉了**「同帝国 +100」「受封 50 年内 +100」两项，但门槛整体抬了。好感平平（0）的封地要正统跌到 ~30 以下才走（类头 `:43`）。
- 汗国兀鲁思宽一些（`:46 UlusSecessionThreshold = -20`），但只对游牧。

**（B）叛乱触发被一串宽限期/概率闸拦。**
- `RebellionSystem.CanAttempt`（`:32-34`）：`RebellionsBanned()` + `GraceYearsRemaining==0` + 非开国君主护盾 + 非统一宽限 + 非神力手动加入冷却，全要满足。
  - `GraceYearsRemaining`（`:13-21`）：受封后若干年内不反。
  - `UnificationSystem.InGrace`：统一战争期间 + 统一后 30 年（开国气象）不起事（`RebellionSystem.cs:30-33` 注释）。
  - `MembershipCooldown.ManualJoinActive`：神力/手动拉入的成员 15 年不反。
- 现代政体硬压：`Scripts/GeneralSystems/ModernStability.cs:13-14` `RebellionFactor = 0.5`（现代国家每次满足叛乱条件只有 **50%** 真起事）、`Loyalty = 15`（忠诚 +15）。类头 `:7`。300 年后多数国家现代化 → 叛乱率直接腰斩、忠诚垫高。`RebellionSystem.ChanceFactor :67-72` 把 `LegitimacyFactor × ModernFactor × PhaseFactor` 连乘。
- 天命阶段：`PhaseFactor :75-80` 治世 ×0.75。
- 自立总闸：`RebellionsBanned()`（`:26`）= `SecessionGate.Forbids()`。`SecessionGate`（`Scripts/GeneralSystems/SecessionGate.cs`）只要「禁止AI外交/禁止AI宣战/禁止叛乱」任一开、或原版「外交/叛乱」任一关，就**一切自立一律拦**（立国、叛离帝国、起事）。这些模组法则默认都关（`EmpireCraftWorldLawLibrary.cs:281/289/298`），**但务必确认该终端世界玩家没手开**——一旦开了，叛乱会「一个都不起」，而分裂进程（割据/土崩瓦解）因为走 `Sanctioned` 仍会发生（见下），正好造成「不叛乱、却偶有行政区独立」的错觉。

**（C）分封/周制世界：小乱被 `PettyRebellion` 钉死，推不翻国家。**
- `Scripts/GeneralSystems/PettyRebellion.cs:14 MaxCities = 2`、`:17-19 Feudal`（只对周室天下或 `ZhouFeudalism` 政体）、`:46-52 Blocks`（满 2 城或攻原主都城就不给）。
- `EmpireCraftKingdomBehCheckPlots.CheckRebellionWar :103`：`PettyRebellion.IsPetty` 为真时，别的诸侯不来投（`:108-127` 的自动扩张被跳过），义军减半（`RebellionStartupService`）。
- 所以周制/分封占多数的世界里，叛乱即便触发，也被钉成「≤2 城、攻不下原主都城、无人来投」，版图自然不变。

**（D）无小人 + 驱动力累积问题（结构性）。**
- 叛乱驱动力（城市忠诚、民怨、帝国正统 `Legitimacy`、积弊）本身仍由调度器里的系统每月结算（`HarshRuleSystem`、`CityStabilitySystem`、`DynasticCycleSystem` 等都在 `EmpireCraftStrategicScheduler`/`CityPatch` 年度扫描里跑），**不是没跑**；但无小人模式下没有「不满的平民」这条原版最直接的叛乱推手，城市忠诚更稳，叠加 (A)(B) 抬高的新门槛，实际很难跌到触发线。
- 触发后还有可达闸收尾：`WarReach.SweepRebellion`（`WarReach.cs:419-431`）叛军与原主够不着连 `RebelClearMonths=24` 个月就判和自立——若叛军被无人区隔开，叛乱会被「和平化」而非扩大。

### 判断（症状二）
是「**既难触发、触发了也改变不了版图**」两头都有：门槛（−50、现代×0.5、各宽限期）抬高使大多数国家压根到不了触发线；少数触发的（尤其周制世界）被 `PettyRebellion` 钉成一两城小乱、推不翻国家。**最卡的是 `SecessionThreshold=-50` 与现代政体 `RebellionFactor=0.5`+忠诚+15 这两处降频。** （同样先排除玩家是否手开了 `ban_rebellion`/`ban_diplomacy`。）

### 建议改法方向
- 把 `RebellionSystem.SecessionThreshold`（−50）上调回接近 0，或按正统跌破幅度做平滑，而不是硬门槛。
- `ModernStability.RebellionFactor`（0.5）别对所有现代国家一刀切减半，至少对正统低、积弊高的现代国家恢复。
- 分封世界给 `PettyRebellion` 一个「做大」出口：叛军连下数城、原主已弱时解除 ≤2 城封顶，允许升级为真正的割据。
- 无小人模式补一条等价「民怨驱动」：让背景人口的不满真实压低城市忠诚，驱动触发。

---

## 症状三：行政区独立后连名字都不改（明确 bug）

### 根因（确定）
**成员国脱离帝国时的改名只在 `admin_name` 非空时才触发，而 `admin_name` 只有「世界地图剧本」生成的汉化行政区（都护府等）才会被赋值；终端新设的行政区、普通成员国当作行政区的，都没有 `admin_name`，于是独立时 `RenameAfterLeaving` 根本不被调用，新国沿用原来的「道/省/行政区」地名。**

### 证据与机制（追到确切断点）

1. **行政区就是帝国里的成员国，名字是地名（道/省/行政区名），并被标记 `place_name`。**
   - 终端新设行政区：`Scripts/Terminal/TerminalHost.Division.cs:285` `seatCity.makeOwnKingdom(head)` 建国，`:294-297` `court.join(...)` + `AsProvince`（`:48-68`）转成流官行政区。`AsProvince` **只设** `jimi_mode`、`SetAllowDiplomacy(false)` 等，**从不设 `admin_name`**。
   - 法理地名标记：`Scripts/Layer/KingdomTitle.cs:63` `data.province_name = title_capital.GetCityName()`；`:67` `data.place_name = ... RealmNamingSystem.IsPlaceName(this.data.name)`。
   - 「行政区名算地名」的判定：`Scripts/GeneralSystems/RealmNamingSystem.Place.cs:16-28`（现存城名、`IsAdministrativeKingdomType` 的国名、`place_name` 的法理名都算地名）。

2. **独立走 `Empire.leave(..., isLeave:true)`，它本身不改名。**
   - `Scripts/Layer/Empire.cs:2794-2841 leave`：移除成员、`pKingdom.EmpireLeave(isLeave)`（`:2819`）、记冷却、重算——**没有任何改名**。
   - 各条独立路径都走它：分裂进程 `Scripts/GeneralSystems/SchismSystem.cs:573`、`:606`（且包在 `SecessionGate.Sanctioned` 里，所以即便开了禁叛乱法则，割据独立照样发生）；土崩瓦解/民族独立 `Scripts/GeneralSystems/ProvincialCollapseSystem.cs:505`、`:531`；神罗衰朽 `HolyRomanSystem.Decline.cs:188`；亏空自立 `LandEconomySystem.cs:338`。

3. **改名逻辑存在，但条件卡死在 `admin_name`。**
   - `Scripts/GameClassExtensions/KingdomExtension.cs:3864-3873 EmpireLeave`：
     ```
     // 汉化的行政区(剧本的都护府)离了帝国：照自己的法理改名
     if (!string.IsNullOrWhiteSpace(kingdom.GetOrCreate().admin_name)) RenameAfterLeaving(kingdom);
     ```
     **只有 `admin_name` 非空才改名。**
   - `RenameAfterLeaving`（`:3735-3750`）本身是对的：`AdministrativeFront(...)` + `FormatCountryTypeName(...)` 给出按政体/文化的正常国号，并 `SetKingdomCoreName`。
   - 但 `admin_name` 的赋值**只在世界地图剧本里**：`Scripts/GeneralSystems/WorldMap/WorldMapScenario.cs:2385`、`:2968`、`:2979`。字段默认空串（`KingdomExtension.cs:552 public string admin_name = ""`）。
   - → 终端新设的行政区、游戏过程中自然形成的行政区/成员省，`admin_name` 全是空 → `RenameAfterLeaving` 不触发 → 独立后 `kingdom.data.name` 仍是原来的「关内道」这类地名（终端显示名 `TerminalHost.cs:1319-1335 NameOf` 直接读 `kingdom.data.name`）。

> 旁证：最近还专门修过「唐制行政区重名『关内道 道』」（commit 30c3f784），说明行政区普遍就是「关内道」这种地名命名；独立后不改名正是因为上面这道 `admin_name` 的窄条件。

### 判断（症状三）
**是明确 bug：改名入口存在（`RenameAfterLeaving`），但被 `admin_name` 非空这一过窄条件挡住，只覆盖剧本都护府，不覆盖终端/自然形成的行政区。** 独立路径（`Empire.leave`）本身也没有兜底改名。

### 建议改法方向
- 把 `KingdomExtension.cs:3872` 的触发条件放宽：对**所有行政区类型/地名国号**的成员国在独立时都改名，例如改成
  `if (!string.IsNullOrWhiteSpace(admin_name) || kingdom.IsAdministrativeKingdomType() || 其主法理 place_name) RenameAfterLeaving(kingdom);`
  （独立时清掉 `place_name` 标记、按都城法理或 `SelectKingdomName`/美号起正常国号）。
- 或在终端 `AsProvince`/`AsGarrison`（`TerminalHost.Division.cs:48-98`）落地行政区时顺手写 `admin_name`，让现有 `EmpireLeave` 的改名条件自然命中。
- 注意独立路径多条（SchismSystem、ProvincialCollapseSystem、HolyRomanSystem、LandEconomySystem 都调 `Empire.leave(isLeave:true)`），改名放在 `EmpireLeave` 这一处即可全覆盖，不必逐路径改。

---

## 总体判断：300 年版图不变，按影响排序

> 一句话：**终端核心照常跑模拟，问题是「近期降频/加闸把离心力压死，而向心力满速运行」造成的结构性锁死 + 一个明确命名 bug。** 不是服务器没推进。

1. **（最大）版图冻结主因 = 开战被双重掐死：可达闸 `WarReach.WarNear` × 无人区默认切块（没对象）＋ 帝国成员不能自开战 × 世界一家独大（没发起方）。**
   - 向心力（`UnificationSystem` 天下大势 `EmpireCraftStrategicScheduler.cs:70`、`SmallStateSubmissionSystem` 小国归附 `:86`、`FeudalVassalService` 附庸、称帝 `EmpireFormationService`）全程满速，把世界吸成几个大帝国；
   - 离心力（开战）被 `WarReach.WarNear`（`WarReach.cs:157`）+ 无人区（`WildernessSystem` 默认开）+ 「只打更弱」+ `minimum_years_between_wars=5`（`EmpireCraftPlotsAddition.cs:2444`）+ 君主意愿 0.2~0.7（`RulerQualitySystem.cs:194-204`）+ 军饷闸（`CorpsSystem.cs:434`）层层筛。
   - 净效果：没仗打 → 没有征服 → 边界不动。

2. **（次大）内部分裂也被掐死：自立门槛 −50（`RebellionSystem.cs:44`）+ 现代政体叛乱×0.5、忠诚+15（`ModernStability.cs:13-14`）+ 各宽限期 + 分封小乱 ≤2 城钉死（`PettyRebellion.cs:14`）。** 没有叛乱/割据做大 → 大帝国不会从内部碎裂 → 版图继续冻结。

3. **（命名 bug，独立存在）行政区独立不改名：`EmpireLeave` 的改名只在 `admin_name` 非空时触发（`KingdomExtension.cs:3872`），而 `admin_name` 只有剧本都护府才有；终端/自然行政区独立后沿用地名国号。** 这条本身不影响版图是否变，但它恰好暴露「独立确实偶尔发生、只是既稀少又没正确呈现」。

4. **务必先排除的配置项（可能单独解释前两条）：** 该终端世界是否手开了 `empirecraft_law_ban_ai_war` / `empirecraft_law_ban_rebellion` / `empirecraft_law_ban_diplomacy`，或关了原版「外交」「叛乱」。任一成立 → `SecessionGate.Forbids`（`SecessionGate.cs`）+ `WarReach.LawForbids`（`WarReach.cs:81`）会把 AI 开战与自立**一刀切全停**，届时上面 1、2 的细闸都无关紧要。默认这些法则是关的（`EmpireCraftWorldLawLibrary.cs:281/289/298`），但服务器世界请实地确认开关状态。

### 验证建议（给动手的人）
- 先看该世界的世界法则面板：`ban_ai_war` / `ban_rebellion` / `ban_diplomacy` / 原版外交·叛乱 的开关。
- 看日志里 `[EmpireCraft][远战]` 的「拦下 X 对 Y 宣战：两边够不着」条数（`WarReach.cs:71`）与 `[EmpireCraft][不许自立]`（`SecessionGate.cs`）——条数多就坐实「可达闸/法则闸」是主因。
- 看通达层日志 `[EmpireCraft][通达]`（`ConnectivitySystem.cs:601`）里「不通 AStarFailed」比例，判断无人区切块严重程度。
- 统计世界里「帝国成员国 vs 独立王国/帝国核心」的比例——成员占比越高，能开战的主体越少，越印证「一家独大 + 成员不能开战」。
