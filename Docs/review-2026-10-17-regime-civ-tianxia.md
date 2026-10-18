# 政体、文明、文明互动、天下与周期律、平衡 复查与待办(2026-10-17)

用户要求："你看看现在的政体和文明变化以及文明之间的互动还有没有需要优化和完善的，以及天下概念""还有平衡性问题""以及天下周期律"。
五路只读复查(政体、文明变化、文明互动、天下与周期律、平衡)后，用户选：**四批全做**，顺序 ①先修确定的毛病 → ②平衡 → ③天下与周期律理顺 → ④政体与文明大项；③④是大工程，动手前要再用问答定细节。

以下行号以复查时(提交 436f108 之后)为准，动手前先核对。

## 进度(随做随记)

- [x] 第一批 1、2、4、5、6、10、12、15 项 —— 提交 85e4292(另：迫使朝贡规则补阿拉伯人丁税、游牧驿传，即第二批第 5 项)
- [x] 第一批 8、14 项 —— 918ab36(易俗改制同步帝国默认文化与统治文化；走出原始阶段后旧称呼重取)
- [x] 第一批 3 项 —— eb0549e(主核心销毁交接天下、每年理顺失效归属、退出天下按城最多交接、清状态复位阶段)
- [x] 第一批 13 项 —— 正统定立与废弛只由本文化最大帝国判(玉玺按天下 id 未做)
- [x] 第一批 9 项 —— 8277917(新 WarGate.CanDeclare；对外扩张/游牧扩张/劫掠/迫使朝贡/扩张地盘/帝国开战计划都过它；劫掠加五年冷却、军费、战力，即第二批第 4 项)
- [x] 第一批 11 项 —— 174f5ab(CultureService.SameKin：同文化或同天下标识；情绪、羁縻、周夷狄、清除移民用它)
- [x] 第一批 7 项 —— CompositeEmpireService.AdoptableRegime(按制度树实际政体，拒原始/城邦/现代)；"整合完成后清复合状态"未做
- 第一批未做的零碎：玉玺按天下 id；EmpireCraftPlotsAddition.cs:1384 的 culture=="Huaxia"；死代码清理
- [x] 第二批 1、2、3 项 —— 9fc3fe6(日本军制税制；罗马常备军、高税、中央集权后整国吞并三城以内小国、教权诉求开给整条罗马线；游牧千户百户要赋税簿记且天命 40、骑射本位要常备军、分治 ×1.25)
- [x] 第二批 4 项(劫掠) —— 8277917；5 项(朝贡死配置) —— 85e4292；7 项(畜牧) —— 85e4292
- [ ] 第二批 6 项(非华夏白拿天命)：并入第三批一起定
- [ ] 第二批 8 项：需要一个改后的五线混合测试世界验证
- [x] 日本补继承法解锁(庄园制→分割继承、武士团→强者继承)；EmpireCraftPlotsAddition 的 culture=="Huaxia" 改 SameKin
- [x] 第三批 第 2 条一部分：统一完成即受天命(TianxiaSystem.Grant)、已受天命不再起统一战、本国受天命时改朝换代不重给开国气象、分治概率按天命阶段(治世 ×0.25/动荡 ×1/乱世 ×1.5)
- [x] 第三批 第 1、3 条 —— fc1d89c(UnificationSystem.SameRealm 按天下判、谋求统一诉求改用并受 BarsContender 约束；据有四分之一以上的外族算天下一家；外族受华夏天命可用玉玺；受被征服文化天下的天命触发采纳中原制度)
- [x] 第三批 第 2 条余下 —— 1bc4356(开国气象要受着天命才算；起事 治世 ×0.75/动荡 ×1.25；动荡积弊 ×1.5；怀玺者按六成判动摇)
- [x] 第三批 第 4 条 —— 7d15d26(普世帝国 Roma/乌玛 Umma/大汗 Steppe/天下人 Tenka 标识与文案；明写标识才合并的判断修正；不到 8 城为偏安不评天命；别的天下共主不来朝；多天命分开算)，即第二批第 6 项
- [x] 第三批零碎 —— 天命得失上世界公告、周制统一给开国气象并受天命、Sovereign 不再新建集合
- [ ] 第三批未做的零碎：TianxiaWindow 显示统一者/开国气象剩余/动荡原因；文化连片在图层上看不见；怀玺者的规则提示仍写 75%；界面占比含野生国与结算不一致；Borders、AutoArrange 的性能；玉玺按天下 id 存；周制 RealmOfCulture 按文化不按天下 id；旧档第一年批量受命；夹在核心之间的无核心华夏城；配色相邻同色
- [x] 第四批 第 1 条 政体逐国跟进 —— 48a4622(TryAdvanceRegimeTransition 各线通用；SyncForeignRegime 认本线声明的政体；成员国按制度线跟核心)
- [x] 第四批 第 3 条 同线各文化分开 —— 6910d3e(political_traits 加 nomad/raider：草原 nomad+raider、维京 raider；劫掠、游牧扩张按特性(ClaimRules.json traits，复合帝国看统治族群)，劫掠也给领主派、部落派，劫异族不看政体；牧帐、放牧、IsNomadicCulture 按特性；DynastyNaming.CultureVariant：键加 `_<文化小写>` 另起称谓——印度不称哈里发/苏丹、派系刹帝利派/婆罗门派，阿兹特克城邦同盟/大王，林地部族议火同盟/大酋长，制度节点名与说明同理；节点可按 cultures/exclude_cultures/requires_traits 过滤(机制已有，暂未给哪个节点用)
- [x] 第四批 第 2 条 同化 —— b8f58b4(官方文化保底=人口与教化占比+25、至多 55，可和平翻城；背景人口每个文化留一成；文化同化给中央/僭主/尊王/同化派，文化转化给自治/血脉/绥靖/诸侯/共和派，派系目录版本 5；被征服文化更先进(时代更晚或文明等级更高)时任何征服者可采纳制度建复合帝国；叛乱、据城自立取当地主文化)。易俗改制仍只给游牧(用户 2026-10-17 定)，没动
- [x] 第四批 第 1 条 缺的阶段 —— af9a35d 明治废藩(大政奉还改律令时藩改为选任的县)；925f744 游牧行省(provincial_administration、YouMu_province、平章)；aba7bdf 苏丹架空哈里发(sultanate：苏丹入朝坐君位、哈里发记为虚君，同幕府做法)；9ef2d32 罗马元首制(western_principate、principate：元首家族相传，核心打下的城设行省 ClassicalRepublic_province、不再一城一国，共和国改称帝国)
- [x] 第四批 第 4 条 文明互动四项 —— 083c75a 朝贡双向与称臣和约(TributaryRelations：宗主出兵保护朝贡国、回赐三成与正统 +1、朝贡国科技互通；不行封建附庸的胜者"迫使称臣"，战后称臣纳贡)；986022b 和亲互市与降部戍边(FrontierRelations：和亲接在岁币上、宗室女出嫁；游牧与定居互市、caravan_trade 商路；游牧战败向有羁縻的帝国内附为羁縻州)；0d9de70 宗教影响稳定(ReligiousTension：异教之城按宽容/人头税/不宽容扣稳定与加情绪，religious_tolerance、poll_tax 特性，不宽容的宗教叛乱，圣战参战上限六国、距离、同帝国不参)；dba793e 跨文明大国外交(GreatPowerDiplomacy：不同文明的两帝按远近相忌，遣使通好互认称号、三十年互不侵犯、依约相助)
- [ ] 第四批文明互动未做的零碎：和约里的改宗、开港通商、扶立君主；贸易按海路连通与好感；移民的外交面；联姻与外国继承人只给封建；圣战只认罗马/阿拉伯线、阿拉伯无教权；攘夷标签误用；欧、阿属国称呼(目前西方是封建附庸，阿拉伯走朝贡)
- [x] 第四批政体零碎 —— 4174b69(游牧立宪、逃人法改认"多元帝国"；忽里勒台 set_succession_law 强者继承；复辟回本文化最晚的君主政体；游牧化只改游牧文化成员；攘夷标签只给华夏、周制讨伐僭越记伐不臣、蛮夷之战按天下)；343de01(削藩 push 施行后仍可提；兀鲁思正统 60 削藩；自主外交的世袭成员与自治城邦算间接统治；幕府设天领)；371de99(补记制度链处理 replaces；政体解析同级取后施行的)；a3c6611(手动换政体走幕府、苏丹钩子；读档重建君主国成员不成现代)
- [x] 第三批零碎 —— 08af7ad(天下窗口：动荡缘由、开国气象余年、偏安说明、规则提示补怀玺六成)；4076262(窗口占比与结算同口径；周室按天下认)；1370b66(传国玉玺按天下存)；83865be(读档头一年天命不公告)；2365679(未立核心的天下画上图层)；f1013f9(相邻天下不撞色)
- [x] 文明零碎 —— d57bc6b(圣城神权自立按罗马线；强统一文化的近代统一按天下；城市风格回落按制度线)；0a6ff41(姓氏文化圈按天下)；93b8f23(海上商路只配同一片海、锁国不通商)
- [ ] 仍未做(多是要定方向的)：写死的政体开关挪到 SystemConfig 标志；圣战开给哪些文明、阿拉伯教权；联姻与外国继承人开给非封建；和约里的改宗、开港通商、扶立君主；移民的外交面；China/Corporate 的起步节点；各线制度树加深(罗马军事支、阿拉伯与游牧 adv 5 以后、工业时代)；Borders/AutoArrange 性能；第二批 8 的五线混合测试世界

## 已定的设计(用户问答 2026-10-17，全选推荐)

**第三批 天下与周期律**
1. 天下统一按帝国核心：天下 = 帝国核心(可联合)；统一战争、天命、图层都按它；还没有核心的地方按文化连片临时算，等有了核心就归入。
2. 治世/乱世以天命为准：受天命 = 治世(开国气象算治世的开头)；统一完成即受天命；分裂、叛乱、积弊速度都看天下阶段；分裂集中在动荡、末世(不再开国气象一过就分)；篡位不重给开国气象。
3. 外族入主可受天命、本族可光复：外族据有天下七成五即受天命、能用玉玺、触发采纳中原制度(CompositeEmpireService 以 MandateOf 为触发)；都城在草原也算天下一家；本族可发光复之战。
4. 各文明一个共同天下：罗马一系共用"普世帝国"、阿拉伯一系"乌玛"、草原"大汗"、日本"天下人"(CultureRule 的 tianxia 标识)，文案按文明换；单核心天下要够大(最小规模)才给天命——同时解决第二批第 6 项"非华夏白拿天命"。另：两个天下的共主互不朝贡；一国持多个天命时分开计算。

**第四批 政体与文明**
1. 政体：各线通用逐国跟进——本文化走到新政体后，各国按本国采纳的科技分批过渡(推广 TryAdvanceFeudalTransition)；成员国按制度线跟核心换(不再要求文化一字不差)；补罗马元首制、游牧行省、苏丹架空哈里发、明治废藩。
2. 同化：可和平同化、留少数族群——人口与辐射压倒性偏向另一文化时可慢慢翻城(放宽 55% 门槛)；背景人口不再每年全改主文化；同化、转化手段开给各政体；任何征服者都可采纳更先进的被征服文化制度(不只游牧)；叛乱新国取当地主文化。
3. 同线各文化分开：文化配置加特性(游牧、海寇、征服同化者……)代替按线判断；制度节点可按文化过滤，让同线各文化有各自的政体、起步与诉求(阿兹特克不劫掠、维京能劫掠、印度不成哈里发国)。
4. 文明互动四项都做：
   - 朝贡双向与称臣和约(宗主保护朝贡国、给贸易与声望回报、算科技接触；加"称臣纳贡"和约，非封建胜者不再得西方式附庸；西方、阿拉伯各有属国形式)；
   - 和亲互市与降部戍边(中原与游牧的和亲、边境互市/陆路商路、投降部落安置为边军)；
   - 宗教影响稳定(异教徒影响稳定与情绪、人头税与宽容的取舍、宗教叛乱真正发生、圣战加上限与距离)；
   - 跨文明大国外交(帝国可结盟或互不侵犯、皇帝/可汗/哈里发称号互认、用远近与文明差异替代一律 -999 好感)。

---

## 工作方式(接手须知)

- 构建：`dotnet build EmpireCraft.csproj -nologo -v q`。csproj 用 `..\..\worldbox_Data\Managed\*.dll` 相对路径，临时 worktree 必须与 `Mods/EmpireCraft` 同深度，例如 `D:/Application/Steam/steamapps/common/worldbox/_ec_build_check/EmpireCraft`。
- 提交前：只暂存自己的改动；工作区里 `Scripts/GeneralSystems/Warfare/NavySystem.cs`(用户的)和 `default_config.json`(war_end_year 999，用户本地)**永不提交**；暂存后在临时 worktree 编 HEAD+暂存确认能单独编过。
- 语言键：新键插在 `"capital_loss_restored"` 前；用 scratchpad 的 `set_keys.py`(暂存区与工作区一起改，ch 由 cz 经 zhconv 转繁)。另一会话同改语言文件时只把自己的键进暂存区。
- 游戏文字不写死史名(周室、陈桥、成吉思汗…)，不出现"玩家/你/神之手"；手机版 NML 兼容(不用 General.Event、不 using 非核心程序集命名空间)；图标走 Tools/IconForge。
- 提交信息结尾：`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；推送、发布要用户明说"推送""发布"。
- 本文档放在 `Docs/`(`Tasks/` 被 .gitignore 忽略，传不到 GitHub)。
- 截至写本文档时本地未推送的提交(后来又加了第一、二批的十来个提交，见 git log)：cc7f578 ecd993d fa9a6b8 0c59910 8f748db eaa772e(另一会话) 85384f1 21edd9b d8eedf5 f61361a fc2c0d0 436f108。

---

## 第一批：确定的毛病(改动小，先修)

1. **幕府内阁报错**：`Scripts/AI/EmpireAI/EmpireCraftEmpireBehCheckCabinet.cs:40-66` 的 switch 没有 `RegimeType.Bakufu`，落到 `default: throw ArgumentOutOfRangeException`，幕府帝国每次都抛错、三席内阁不维护。补 Bakufu 分支(照朝堂/内阁维护)。
2. **天下锁定合并写反**：`TianxiaSystem.cs:236-240` 一方锁定时仍把未锁的一方并进锁定方；`Leave(manual)` 给离开的核心设 `tianxia_locked`(:153)，第二年大天下反被它吞(`Join(b,a)` → `ClearState` 抹掉大天下天命)。改为任一方锁定就跳过这一对。
3. **天下首领核心被销毁后成员成孤儿**：`CoreEmpireTitleManager.DestroyEmpireCore`(:586-608，7 处调用)不管天下；成员留着失效的 `tianxia_lead_id`，`MandateOf`/`LegitimacyItems` 要求 `tianxia_lead_id < 0`(:307、:400)，结果孤儿首领拿不到正统、封禅、独尊官学、造玺；天命、史册、名字无记录丢失。加清理钩子：销毁时与读档时把成员改挂到继任核心，迁移天命/名字/历代共主/朝贡标记，`ClearState` 同时复位 `tianxia_phase`；Join/Leave 也要处理朝贡国标记(`Tributaries` :317-324)、Leave 的继任挑大的不是 `members[1]`。
4. **乱世卡死**(UnificationSystem)：
   - :389-390 最大一家 `unified_at ≥ 0` 时整个天下跳过；`unified_at` 只在改朝换代时清——旧王朝崩了残部仍最大，谁都发不起统一战，永久乱世；停滞规则(:374-381)让三成占比的"统一者"也被标为已统一、永远挡住。改：开国气象过了、或失了天命，就不再认 `unified_at`；最大一家被挡时让第二家争。
   - `MinContenders = 3`(:388)：两强对峙(南北朝、分治双胞胎、绝嗣两家)永远不打统一战。允许两家。
5. **日本不在诉求规则里**：`ClaimRules.json` 没有任何 `lines:Japan` 条目，`ClaimRules.cs:133-138` 对声明了 lines 的诉求一律拒绝——日本 30 多项诉求(削藩、分封、设置行政区、转天朝制度、转世袭、迫使朝贡、确立国教、土地改革、输出革命、拓展金融霸权…)全被挡。按日本节点补条目(平衡复查建议：削藩 push japan_ritsuryo_state/japan_sankin_kotai；行政区、转天朝 any/push ritsuryo；分封、转世袭、允许诸侯开战 any japan_shoen_estates/japan_bakufu；土改、输出革命、禁党、金融霸权按日本理念节点；确立国教、供养宗室不限)。
6. **日本部落卡死**：`TribalFissionService.Migrate`(:440-441)只放开 Huaxia/Youmu/Arabic，日本的原始部落永远升不出 Origin；`Enfeoff`(:383)只在共主是周制时收编共事部落。改为"本线有自己的原始节点"的都放开(含 Japan)；共主换成第一个非原始政体时把共事部落收为成员(不只周制)。
7. **外族王朝采纳被征服者制度套错政体**：`CompositeEmpireService.cs:197-226、286-298、473、645` 用文化配置的静态 `regime`(CultureRule)，不看制度树实际阶段——征服日本/印度/阿兹特克/奥吉布瓦 → Origin(变部落、每月拆城、禁分封)；"中国"文化配置是 Modern → 变共和国(RepublicSystem.cs:1256-1267)；西方 → ClassicalRepublic 被 `CityStateService.EnforceOneCity` 拆城；每年同步与制度树打架(山海=周 vs 树 LvLing)。改：用 `InstitutionSystem.TryResolveCultureRegime` + `RealmCanAdoptRegime`，拒绝原始/城邦/现代目标；整合完成后清掉复合状态(现在 stage≠None 永远算复合，:532、:551 永远跳过)。
8. **易俗改制后帝国仍用旧文化**：`CultureReformSystem.cs:110-117` 只改国家 realm_culture 与 conquest_founder_culture，不改 `EmpireCore.default_culture`(尊重锁定)与 `data.ruling_culture`，也不清 RulingMajority 缓存、不改头衔文化(:303 的 CultureService 与界面才改)；`GetPrimaryCulture` 读 default_culture，于是制度、政体、天下 id、文化同化目标都按旧游牧文化。另 `CultureService.ConvertKingdomRegime`(:253-264)只改 realm_culture，`RefreshCultureDependents`(:1609-1620)会按头衔改回。
9. **宣战绕过外交约束**：对外扩张、游牧扩张、劫掠、迫使朝贡、扩张地盘、圣战、统一都直接 `newWar/startWar`，不走 `IsValidModWarTarget`(停战年限、婚约、岁币、部落盟誓，EmpireCraftPlotsAddition.cs:2784-2831)与 `CanDeclareExternalWar`(FeudalVassalService.cs:458-491)。做一个统一的 `CanDeclare(attacker, target, reason)`(停战、婚约、岁币双向、朝贡国、同盟、军费)，诉求战争都过它(统一战争是否豁免停战按原设计"不休战"保留)。`IsValidImperialWarTarget` 不查 `initiator.HasGivenAlliance()`(收岁币的帝国能立刻打送岁币的)。劫掠另：可能打到自己的朝贡国/岁币国/姻亲/盟友；`CoreKingdom` 为空会空引用。
10. **迫使朝贡永不触发、大国称臣落空**：
    - `TempFac_迫使朝贡.cs:46` 比 `countTotalWarriors()` 与 `empire.countWarriors()`(原版小人数，军团/无小人模式下为 0)→ 改用 `WarSituation.Power`。
    - 我上一版(d8eedf5)游牧扩张攻下大国都城改 `JoinTakenAlliance`，但没有 `youmu_post_relay_network` 的汗国收不了朝贡(KingdomExtension.cs:1432 静默返回)→ 打下来什么都没有。迫使朝贡攻下都城(CityPatch.cs:488-493)若中途变共和也同样落空(:1436)。要有兜底(收不了朝贡就赔款或收附庸)。
    - 迫使朝贡、游牧扩张在 `WarGoalSystem.OwnEnding`(:44-50)→ `FightingForSurvival` 为真、`TrySueForPeace` 拒绝，守方不能求和称臣。
11. **"外族"认得太死**：国民情绪与入侵判断(NationalSentimentSystem.cs:117-123、175-180)、羁縻自动判定(JimiSystem.cs:48-52)、周制夷狄判定(ZhouEraSystem.cs:222-228)都按文化字符串完全相等——华夏统治"中国"文化的城算异族统治，每年情绪 +6、出独立运动，而天下把它们当一家。复合帝国里情绪用统治文化、羁縻用默认文化、清除移民用制度文化 → 元朝式帝国"清除移民"把自家游牧族人赶走(ClaimPolicySystem.cs:116)。加一个 `IsForeign(a,b)`(天下 id → 制度线 → 物种)统一用。
12. **恢复圣地白拿别国城**：`TempFac_恢复圣地.cs:52` 接受任何帝国里的圣城，分出来强行并入自己帝国；religion/religionCity 可能为空抛错。限本帝国(或本国在打的敌方)的城、加空值判断。
13. **定于一尊翻来覆去**：`AncientSchoolsSystem.cs:117-129` 按帝国逐个查、对的却是整个文化的状态——同文化任一帝国的天下乱世(或都城不在任何核心 → PhaseOf=乱世，TianxiaSystem.cs:310-314)就让治世的共主那边也废弛，两边来回"百家复起/定于一尊"刷史册。改为按持有天命者所在天下判。玉玺同理按文化存，华夏/中国/山海一个联合天下可能有三方真玺(改为按天下 id)。
14. **换政体后帝国类别名残留**：`InstitutionSystem.ApplyRegimeChange`(:2497-2519)不清 `empire_type_key`(只在原始阶段 :611-614 复位)，部落/部落联盟的称呼可能留在汗国、哈里发国、周制上(中等把握)。换政体时清掉；游牧由部落联盟长成汗国时补"大"前缀(Empire.cs:1078 只在开国即游牧时设)。
15. **畜牧加成整条游牧线都有**：`AnimalHusbandrySystem.IsNomadic`(:74-79)按制度线，阿兹特克、奥吉布瓦也牧群×2、繁殖×1.5、多许两座牧场(IndustryBuildingPatch.cs:109、LivelihoodSystem.cs:264 描述用到)。改为按文化(Youmu)或游牧营地(`CityStyleSystem.Nomad`)。

另顺带：`EmpireCraftPlotsAddition.cs:1384` 用 `culture == "Huaxia"`，漏了 China/Shanhai；`IsHuaxiaMonarchy`(InstitutionSystem.cs:2480)是死代码；`RegimeType.Republic` 无配置、相关分支(Empire.cs:4349、4451；OfficeSelector.cs:23；TreasurySystem.cs:175)是死的；RegimeSystem.cs:103 顺序编号的枚举标了 `[Flags]`。

---

## 第二批：平衡

1. **日本太弱**：除上面补诉求规则外，`Japan.json` 没有任何 army_* 与税率特性(`japan_samurai_bands` 特性为空)。建议：samurai_bands 加 army_regional + army_mobilization 0.15；bakufu 加 army_standing；handen 税→中；land_tax_reform 税→高；Japan 树补继承法解锁(其余四线都有分割继承、强者继承)。
2. **罗马整合太慢**：城邦阶段只能一城(CityStateService.cs:14)；封建化后攻下都城永远只收附庸(FeudalConquestService.cs:59、93)，中央集权君主制后政体仍是 Feudalism 也不变；税最高只到中；无常备军；24 项诉求里 9 项只给日耳曼/法兰克/罗马/西方(ClaimRules.json:306-451)，斯拉夫、维京等只有 15 项。建议：有 direct_administration 后攻下都城吞并三城以内小国；western_centralized_monarchy 加 army_standing；western_parliamentary_taxation 中→高；神权强化、君权神授、恢复圣地、叙任权之争开给整条罗马线(金玺诏书、帝国治安令、恢复世袭、确认诸侯特权仍限神罗)。
3. **游牧削弱比预想软**：制度的 advancement 只影响 AI"先基础后进阶"的排序(余量 2，InstitutionSystem.cs:1295、1327-1328)，不是硬门槛——千户百户真正门槛仍是 military_register(第 2 档)与天命 28(Youmu.json:179)；骑射本位第 2 档就设支援中央(:321)。建议：千户百户加 taxation 科技、天命 28→40；骑射本位要 standing_army；YouMu 分治倍数 ×2→×1.25(兀鲁思已经会拆)。马穆鲁克已改要 standing_army(d8eedf5)。
4. **劫掠**：d8eedf5 已加"同一国五年内不再遭劫"(KingdomExtraData.raided_at)与三成；仍缺：诉求自身 CountDown(5)、TooBusyForWar、CannotFundWar、战力比较；可再加封顶 min(三成国库, 劫掠者年收入×2)。
5. **朝贡配置是死的**：阿拉伯 `arabic_jizya_tax` 的 enable_claim 迫使朝贡(Arabic.json:227/265)、`Arabic.json:506` 的神授君权、游牧 `youmu_post_relay_network`(Youmu.json:458)都因 ClaimRules 没有对应条目而无效；游牧条目 `special: sinicized_nomad` 永远到不了(IsAllowed 用 GetPrimaryCulture，复合帝国解析成华夏线)。补条目或删掉死效果；朝贡是否开给西方/阿拉伯(附庸王国/埃米尔纳贡)在第四批定。
6. **非华夏白拿天命**：非华夏文化没有天下 id，一核心一天下；核心按已控制的头衔建、只吸收同文化头衔，开国就占 ≥75% → 白拿正统 +8 与朝贡吸引，衰落比华夏慢(ProvincialCollapseSystem.cs:60 以正统 <50 为危机)。建议：单核心天下要有最小规模或多核心才给天命(第三批一起定)。
7. **畜牧**：见第一批第 15 项。
8. 存档实证：本地最近存档都是纯华夏世界，无法比；九月混合世界里阿拉伯每国国库约华夏 65 倍(旧中税默认)，d8eedf5 已改低税。需要一个改后的五线混合测试世界确认。若华夏仍滚雪球，可把 LvLing 分治倍数 ×0.5→×0.75。

---

## 第三批：天下与周期律理顺(动手前问细节)

- **两套"天下"**：`UnificationSystem.Survey`(:185-244)有核心的城归核心天下(≥5 城)，无核心的归文化连片(≥8 城、48 格)；文化连片没有天命、阶段、图层(`TianxiaMap.Shown` 只画有核心的城，:56-57)。`TempFac_谋求统一`(:60)用 `SameSphere` 比文化、不看天下归属与 `BarsContender`——治世里非共主的皇帝照打"谋求统一"，还跨 AutoArrange 不肯合并的海峡。→ 统一成一个定义。
- **两套"治世/乱世"**：TianxiaSystem 的 phase(:256-303)与 UnificationSystem 的 InGrace(诊断直接打印"治世(开国气象)"，:567)；叛乱、分治、篡位、积弊速度都只看 InGrace(Empire.InFoundingGrace :123-127、RebellionSystem.cs:26、EmpirePartitionSystem.cs:72)，只有正统、CliqueSystem(:438-458)、AncientSchools(:117)、封禅/独尊官学看 phase。统一"完成"在 30 年过半(:354-360)或停滞三年(:374-381)时给 30 年开国气象，但没到 75% → 天下显示乱世 → 开国期间百家复起、正统废弛。→ 一个真相来源(按天下阶段驱动，或统一完成即给天命/对齐占比 90/75/50)。
- **分裂时机**：`EmpirePartitionSystem.Eligible`(:66-75)只看 InGrace 与冷却，遗诏/权臣概率(:433-445、535)不看王朝积弊与阶段 → 开国气象一过就分。→ 按积弊/动荡阶段加权，集中在末世。
- **篡位重给开国气象**：`UnificationSystem.OnDynastyChanged`(:137-144)清 `unified_at`，篡了 ≥75% 共主的人马上算统一者(`BarsContender` 豁免共主，:397)→ 又 30 年开国气象(不叛乱、积弊不涨)，连"保留积弊"的权臣篡位也是。同国改朝换代也不记新的 MandateRecord，历代共主仍显示旧朝(:296)。
- **元清式入主中原走不通**：`Survey` 要都城在天下里——都城在草原的征服者当不了统一者，本族也打不了光复战(不在 Opponents :428-435)，本族靠"只剩自己"(:343-345)就算统一了而征服者仍占大半；玉玺 `HeldBy` 要本帝国文化是华夏(ImperialSealSystem.cs:92-102)，外族抢到的玺记为遗落；`CompositeEmpireService.GetAdoptionStatus`(:143-189)不看 `MandateOf`(天命本该是汉化/入主的触发)。
- **非华夏的天下**：只有华夏/中国/山海有天下 id；罗马/法兰克/日耳曼/西方各自一天下，形不成普世帝国；无乌玛、草原大汗、日本天下人等对应与文案(全是"天命/天下共主")；教廷、神罗、埃米尔、兀鲁思各有正统但不连天下。两个天下相遇无竞争/承认；`Tribute`(:340-351)不排除别的天下的共主(弱罗马共主能当华夏共主的朝贡国还留 +8)，且 `pForce: true` 绕过自愿与国力检查、战争中也强拉。一国可持多个天命，`shaken |=` 一个动荡就全降为 +2。
- **周制**：`ZhouEraSystem.RealmOfCulture` 按文化不按天下 id(:101-107)，山海周天子与华夏皇帝可同在一个天下；取周/称帝不设 `unified_at`(秦式统一者没有开国气象)；周的门槛读 `empire.Mandate`(正统，已含天下 ±)——"天命"一词指三样东西。
- **反馈与性能**：天命得失只记本国史册(:436-440)，无世界公告；TianxiaWindow 不显示统一者、统一年数、开国气象剩余、动荡原因；文化连片看不见；规则提示对怀玺者仍写 75%；界面占比用 `CountHolders`(含野生国，EmpireCoreControl.cs:29-39)与结算(UnificationSystem.cs:191 排除野生)不一致。`Sovereign` 每次新建 HashSet(EmpireCoreControl.cs:21)、`Borders` 扫全城×邻城(:274-284，每年每对手 :371、每月 Declare :526)；`AutoArrange` 每对调 `Members()` 与 O(n²) 的 `GetCities()`(CoreEmpireTitleManager.cs:140-151)。
- **其他边角**：怀玺 60% 受命后第二年按 0.75 判动荡(:372)；受命一律设治世不跑动荡判定(:300)；旧档第一年批量受命刷史册、一次合并所有相邻华夏核心；核心之间夹着无核心的华夏城就永远连不上；`CreateCoreFromTitle` 在游牧占都城时建出的核心带游牧文化(:542)被永久踢出华夏天下；配色 `id % length` 相邻可能同色。
- 周期律总体：衰落大体能闭合(积弊按 0.15/0.35/0.6 每年 × Pace，常年打仗约 220–260 年到危机 60)，但土崩瓦解要有成员省(ProvincialCollapseSystem.cs:76-80)，全直辖的帝国只靠苛政起义(HarshRuleSystem.cs:263-280)。

---

## 第四批：政体与文明大项(动手前问细节)

**政体**
- 只有罗马线有"逐国跟上本文化政体"(`TryAdvanceFeudalTransition`，InstitutionSystem.cs:431-475，写死城邦→封建与 western_feudalization)；别处改革只换主持的那个帝国(:946-951)，每年漂移遇到政体节点且有帝国时跳过(:1226、:1254)，节点"已施行"后别国再也采纳不了 → 第二个华夏帝国永远周制，别的日本国永远没有幕府/明治。→ 通用、分批、科技门槛的逐国跟进；`SyncForeignRegime`(:559-560 只认 YouMu/Arabic/LvLing/Zhou)改为"本线是否声明了这个政体"。
- 成员国按完全相同的文化键跟核心换政体(`EnumerateAffectedKingdoms` :2292-2303)：阿拉伯哈里发里的埃及、法兰克帝国里的日耳曼、华夏里的中国/山海、由本地官员治理的省都留旧政体(例：共和国里留着 LvLing 的省)。→ 按制度线或按政体策略跟，至少核心变现代、离开幕府时转换成员。
- 恢复帝制 `previous_regime` 为 -1 时落到封建(RepublicSystem.cs:1010、1045)；联军建立的共和(:778-784)不记前一个君主政体 → 华夏复辟成封建。
- `EnactNodeChain` 不处理 `replaces`(:996-1006，`ForceEnactChain` :1055 处理)；`TryResolveCultureRegime` 只在 advancement 严格更高时替换(:424)，部落/汗国/哈里发/周制节点都是 1 → 靠列表顺序(目前靠 replaces 兜着)。→ 提高这些节点的 advancement 或按 replaces 链判。
- 绕过科技门槛：TribalFissionService.Migrate 用 `ChangeRegimeForTransition`(:444)、周制统一(ZhouEraSystem.cs:961-967)、转周制(TempFac_转周制.cs:30-32)、游牧化(TempFac_游牧化.cs:35-40，不看文化、IsRegimeLocked、手选、科技、也不同步制度树——华夏文化国被改成游牧政体)；手动改政体(RegimeWindow.cs:491-505)跳过幕府钩子与成员；读档重建官职(DataManager.cs:102-116)跳过君主国调整与复合处理。
- 缺阶段：罗马无元首制/君主专制(城邦直接封建，ClassicalRepublic 不做征服处理 Empire.cs:1736；维京、日耳曼、斯拉夫也从古典城邦起步)；游牧"行省制度"不建行省(YouMu 无 *_province)；阿拉伯苏丹官僚描述"哈里发成傀儡"无实现(只有幕府有架空逻辑)；日本明治不废藩(KingdomExtension.cs:2148)、幕府天领不生成、无继承法解锁；游牧宪政要复合帝国(Youmu.json:854)而复合帝国按被征服文化的线算 → 永远到不了，`youmu_fugitive_household_law`(:694)同理；忽里勒台选汗设"军队推举"但 `set_regime_option` 跳过世袭国(InstitutionSystem.cs:2328)→ 对大汗无效；兀鲁思无收回(埃米尔有正统 60 削藩)；藩国自立放开 YouMu 时没区分兀鲁思与普通成员；`IsIndirect`(AdministrativeControlSystem.cs:104-116)把兀鲁思、世袭埃米尔、自治城邦都算直辖；幕府藩与城邦成员不能自立；分治倍数只调了 LvLing/YouMu/Feudalism(EmpirePartitionSystem.cs:435-437)。
- 写死的政体开关挪到 SystemConfig 标志：能否自立、间接统治成员、征服处理(行省/封国/无)、保留成员角色(KingdomExtension.cs:2148-2149、CheckKing.cs:181)、分治倍数；朝堂阶段只有周/律令(CourtSystem.Dynasty.cs:46-57)；继承人/后宫等华夏线判断(CheckHeir.cs:221、237；Empire.cs:3595；CliqueSystem.cs:60；ClaimRules.cs:205)。

**文明**
- 制度树深度：自有节点 华夏 37、罗马 18、日本 16、游牧 16、阿拉伯 14(另各有 14 个公共模板与 3 个生产节点)。罗马军事支只到公民军团(adv 2)，无骑士/常备军/火药/征兵、无专制/重商/启蒙/废农奴；阿拉伯止于 adv 5(无火药帝国、坦齐马特)，`arabic_lift_party_ban` 挂在 adv 3 的贝特马尔下，神权支到不了宪政行政基础；游牧止于 adv 5、没有定居化出路；华夏明代与预备立宪之间是空的；工业时代各线只有公共模板；第六档(火药)制度只有华夏、日本。advancement 在各线含义不同(EraLag 当时代用，InstitutionSystem.cs:1281)：解除党禁都要 constitutionalism(第 7 档)却在阿拉伯/游牧 adv 5、罗马 6、华夏日本 8。文明等级加到 14 就封顶(Settings.json culture_levels)，等级 4 解锁科技到第 11 档；吸收检查拿节点 advancement(1–14)比文明等级(1–4)(InstitutionRules.cs:72-76、InstitutionSystem.cs:2082、2217)，adv≥5 的节点谁都能吸收；同线各文化之间不交流制度(:2076)。
- 同化：主文化保底 55%(CultureShareRules.cs:23)，挑战者要 ≥55% 且领先 15(:12-13)→ 迁徙、辐射永远翻不了城；征服时才加压(CityPatch.cs:1016-1017)，约 25 年 30→55 再稳 5 年；背景人口每年 2% 改成主文化(CityPopulationSystem.cs:1084-1103)→ 没有稳定的少数族群；征服当场把征服者文化设为该城三成(CultureService.cs:790-826)。文化同化只有攘夷派(律令、共和)与法西斯党有，文化转化、制度融入只有融入派(只有游牧)——封建(整个罗马线)、阿拉伯、周、幕府、城邦帝国都不能同化或转化成员头衔(只有独立国王有计划，EmpireCraftPlotsAddition.cs:76-96)。易俗改制与复合帝国只认 `setting.regime==YouMu`(CultureReformSystem.cs:49、CompositeEmpireService.cs:160-164、279-284)——阿拉伯征服波斯、诺曼式采纳都不行，阿兹特克、奥吉布瓦也被排除。叛乱新国取领头人的个人文化(CultureService.cs:60-76、KingdomPatch.cs:340)，官员留原文化(OfficeSystem.cs:309-312)。不会产生新文化(CulturePatch.cs:97-108 把原版文化演化映射回母文化，固定 21 个)。宗教与文化基本脱钩：圣城神权自立查配置 regime==Feudalism(EmpireCraftCityBehCheckReligion.cs:23-26)漏了西方；教廷绑罗马线(PapacySystem.cs:28、59)。
- 同一制度线下多文化(游牧线=游牧/阿兹特克/奥吉布瓦；阿拉伯线=阿拉伯/埃及/波斯/印度；罗马线=维京/斯拉夫/法兰克/日耳曼/犹太/精灵/公司…)：阿兹特克、奥吉布瓦得游牧政体、劫掠、游牧扩张；印度配置是 Origin 却成哈里发国、也成圣战对象；维京等从城邦起步、不能劫掠；同一线比较(SurnameInheritanceSystem.cs:179-185 SameSphere、WarCommandSystem.cs:207 蛮夷战标签按线——法兰克打维京不算蛮夷)；民族统一按确切文化(NationalUnificationSystem.cs:42)而天下按天下 id；China 与 Corporate 配置 regime=Modern 但 start_institution 空 → 从原始起步；城市风格写死 switch(CityStyleSystem.cs:21-34)，Emperor、Corporate 回落成华夏风格。→ 用显式的文化特性(nomadic、conquest_assimilator 等)代替 `setting.regime` 判断；节点加 `cultures` 过滤，让同线各文化有各自政体、劫掠、起步节点。

**文明互动**
- 和约只有吞并/割地/赔款/收复/迫使附庸(WarType.cs:10-19)，无"称臣纳贡""改宗""开港通商""承认称号""扶立君主"；迫使附庸一律是西方封建附庸，`ForceBind`(FeudalVassalService.cs:103-109)跳过 `CanBind` 的 western_feudalization 检查——华夏、游牧胜者得到封建附庸而不是朝贡国。
- 无大国外交：帝国不能结盟(ModAllianceService.cs:39-55)；反帝联盟只在同文化之间(EmpireFormationService.cs:464-465)；WorldWarSystem 只是标签；跨文明"两个皇帝"只有同物种帝国一律 -999 好感(EmpireCraftOpinionAddition.cs:229-239)，不论文明与远近，无可汗/皇帝/哈里发称号的承认与位次。
- 朝贡单向：朝贡国每年交人口/2(KingdomExtension.cs:1220-1265)，宗主无义务(MemberWarAid 不管朝贡国)、无贸易/声望回报，除羁縻外无册封。科技接触(TechnologySystem.cs:1180-1221)只算邻国、辐射、帝国成员，不算贸易伙伴、电报广播(注释说含朝贡国但代码只用 GetEmpireID)。
- 宗教不影响稳定与情绪(CityStabilitySystem.cs:195-226、NationalSentimentSystem.cs:136-160)；"民族叛乱""宗教叛乱"战争类型从没被设过；无宽容/齐米的取舍；人头税只是税率改中。圣战只在圣城持有者是罗马或阿拉伯线时(PapacySystem.cs:239、262)，印度、波斯也打圣战，犹太/精灵/公司得教皇加冕，华夏/游牧/日本宗教永无圣战；阿拉伯有圣战没有教权立场、什一税、加冕，成员缺 +10% 集结加成(PapacySystem.Crusade.cs:147-148)；参加圣战无上限、无距离、无同帝国判断(EmpireCraftKingdomBehCheckPlots.cs:147-171)；教皇国圣战计划(EmpireCraftPlotsAddition.cs:1302-1349)不共用 15 年冷却。
- 贸易(TradeRouteSystem.cs:73-109)按直线距离配对、不查海路连通，文化、宗教、朝贡、锁国(closed_country 只用于思潮，MediaSystem.cs:60-65)、好感都不影响；无陆路商队(阿拉伯商队贸易无贸易效果)；无贸易依赖；拓展金融霸权只 +15% 本国收入(ClaimPolicySystem.cs:47-51)。
- 移民无外交面：开放边境不经邻国同意抽人、驱逐把人倒到国外(ClaimPolicySystem.cs:93-147)，都不影响好感、没有难民问题。
- 联姻、继承宣称、共主联邦只由封建政体发起(RoyalMarriageSystem.cs:27-28)；无和亲(只出现在书目 CSV)；外国继承人只对华夏禁止(EmpireCraftKingdomBehCheckHeir.cs:213-221)。羁縻只有华夏(jimi_system 只在 Huaxia.json:526)，游牧双轨制、阿拉伯齐米/埃米尔、罗马附庸王没有对应。天下 id 只有华夏系 → 无基督教世界、伊斯兰世界。
- 游牧—定居工具缺：互市、和亲、安置降部为边军(可借羁縻戍边)、杂居边地。
- 攘夷标签被用在"物种不同"的对外扩张(TempFac_对外扩张.cs:47)与讨伐同文化僭主(ZhouEraSystem.cs:719-720)上。

---

## 建议的推进顺序

1. 第一批 15 项可拆三组并行(各在同深度临时 worktree 里改、编、提交后挑回)：
   - 天下组：第 2、3、4、13 项(TianxiaSystem、CoreEmpireTitleManager、UnificationSystem、AncientSchoolsSystem、ImperialSealSystem)；
   - 日本与政体组：第 1、5、6、7、8、14 项(Cabinet、ClaimRules.json、TribalFissionService、CompositeEmpireService、CultureReformSystem、InstitutionSystem)；
   - 战争外交组：第 9、10、11、12 项(新 CanDeclare、TempFac_* 诉求、CityPatch、WarGoalSystem、NationalSentiment/Jimi/Zhou/ClaimPolicy、TempFac_恢复圣地)；第 15 项随手改。
   语言键由主会话统一加。
2. 第二批平衡(与第一批第 5、15 项、劫掠相关，等第一批合进来再做)。
3. 第三批、第四批先用问答定方向(例如：天下统一成按核心还是按文化连片；元清式入主的触发；非华夏天下的称号与 id；同化速度与少数族群；同线各文化分开的方式；朝贡双向与和亲互市的规则)，再分步做。
