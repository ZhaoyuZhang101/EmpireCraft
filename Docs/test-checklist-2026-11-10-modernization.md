# 封建→现代(开放党禁)三条路改造 游戏内测试清单

起因：玩家反馈贫瘠穷地方(中亚哈萨克等草原)竟自主革命、现代化成多党共和。
用户 2026-11-10 拍板：封建→现代(开放党禁=有政党政治/立宪)只能走三条路——①人民革命、②君主妥协立宪、
③外部战争强迫；核心繁荣不过硬门槛时①②自主路彻底开不了，只有③外力能逼；去掉无声自主开党禁。

## 改动摘要(file:line 见报告)
- 硬发展门槛：`PartyBanSystem.CoreProsperity(empire)`(核心法理内各城 `IdeologyPopulationSystem.CityProsperity` 均值，0~1)，
  阈值 `ModernizationProsperityFloor = 0.35f`；`PassesModernizationGate(empire)` 判断。
- ①人民革命：`TempFac_开放党禁.CheckCondition` 加门槛，穷核心连立项都不行；`ClaimAgendaSystem.Evaluate` 同步加受阻原因。
- 无声自主开：`PartyBanSystem.UpdateReopenPressure` 压力满 100 不再直接 `Open()`，改为导进②/①评估。
- ②君主妥协：`MonarchWouldConcede` 按君主资质判断；`ConstitutionalEconomySystem.ConcedeConstitutionalMonarchy` 保王朝转立宪。
- ③外部战争强迫：`ConstitutionalEconomySystem.ImposeModernization`(战败和约结算，`WarGoalSystem.Modernize` 调用)绕过门槛，
  一党闭关败方(君主国/共和国)一并 `PartyBanSystem.Open`。

## 测试用例

### 1. 穷核心守封建、不自主开(核心复现)
- [ ] 造一个贫瘠草原/纯农核心的封建君主国(工商市民极少、无高级民居，核心繁荣应 < 0.35)。
- [ ] 让民间接触到近代理念(有理念挑战，PartyOpening.HasChallenge=true)。
- [ ] 跑若干年：确认它**不会**自主开放党禁/现代化；制度窗口里"开放党禁"诉求显示受阻
      (原因键 `agenda_party_ban_requires_prosperity`)。
- [ ] 对照：把核心喂成工商繁荣城市(繁荣 ≥ 0.35)，确认①②自主路恢复可用。

### 2. 发达国 + 弱势/改革派君主 → 君主妥协立宪(②)
- [ ] 发达核心(繁荣 ≥ 0.35)的一党制君主国，君主资质为明君/庸弱/冲龄/昏聩之一。
- [ ] 制造高重开压力(核心民心差或理念挑战)，使 `party_ban_reopen_pressure` 到 100。
- [ ] 确认：和平开放党禁(史书 `party_ban_reopened_monarch_concession_history`)，
      随之确立君主立宪、保留王朝(史书 `constitution_monarch_concession_history`)，召开议会，君主仍在。

### 3. 发达国 + 强硬君主 → 人民革命推翻(①)
- [ ] 同上发达一党制君主国，但君主为暴君(或中主/荒淫且正统 ≥ 35)。
- [ ] 压力到 100：确认**不**自上而下开放；转由 `TempFac_开放党禁` 革命派系立项(需有理念挑战)。
- [ ] 革命取胜：按现有流程开放党禁(可能废君/亡朝)；革命失败则维持一党、压力保持满格。
- [ ] 一党制共和同理：压力满只走革命路，不会无声翻多党。

### 4. 战败被先进国和约逼开(③，绕过门槛)
- [ ] 贫瘠封建君主国(繁荣 < 0.35，自主开不了)邻接一个已开放党禁的先进国(立宪/共和/现代，火药时代以上)。
- [ ] 先进国打赢、在和约里让败方丢都城或四分之一以上的城(不吞并)。
- [ ] 确认：败方被强行立宪+开放党禁(史书 `constitution_imposed_history` / `party_ban_reopened_imposed_history`)，
      即便繁荣不达标也现代化。
- [ ] 一党闭关共和国败方：确认被逼开多党(`party_ban_reopened_imposed_history`)。
- [ ] 反例：战胜方是封建/一党闭关国，或败方只丢零星城，**不**触发逼开。

### 5. 旧档读档正常(存档安全)
- [ ] 读入改造前的旧存档：未新增任何持久化字段(复用 `party_ban_reopen_pressure`、`forced_modernization` 等)，
      一党制国家不应因读档被平白开放或打回封建。
- [ ] 已经现代化/多党的国家读档后保持现状，不受新门槛影响(门槛只拦"新的自主开放")。

### 6. 兼容路径不被误伤
- [ ] 周制(ZhouFeudalism)：仍按原逻辑，不被门槛逻辑干扰。
- [ ] 游牧/部落、AncientWarfare 接管的城：不参与自主开放党禁(本就不满足政党政治前提)。
- [ ] 澳宋穿越(TransmigrantSystem)等脚本化现代化storyline：照常推进，不受门槛限制。
- [ ] 社会主义政变、现代革命战争(RepublicSystem / PublicOpinionSystem)等已有"已现代化"国家内部路径：行为不变。

## 繁荣阈值依据
城市繁荣度 = 0.7×工商市民占成年人比(占三成即满) + 0.3×有高级民居，再按日用品短缺打折。
典型发达工商核心 ≥ 0.5；轻度城市化(工商两成、无高级民居)≈ 0.14；纯农/游牧核心 ≈ 0.05~0.1。
取 0.35：放行有像样工商阶层的发达核心，挡住贫瘠草原/纯农穷核心。如实测偏松/偏紧，调 `ModernizationProsperityFloor`。
