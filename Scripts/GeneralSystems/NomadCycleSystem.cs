using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 游牧来得快去得快(用户 2026-10-20 选"游牧来得快去得快"；以前汗国积弊满格、连吃败仗也不散，照样压着农耕帝国打)：只管游牧政体的汗国——
//   · 汗位更替之乱：新汗即位(KingdomPatch 的 setKing 补丁，见 OnKhanSucceeded)时(2026-10-27 起每次换汗都判，不再要积弊或正统先过线)，
//     各兀鲁思(游牧政体的成员国；行省、羁縻除外)，以及 AnnexedYears 年内并入的别种政体成员，
//     各按 BaseSplit + MaladySplit×积弊平均/100 + 汗国大小(SizeSplit) 的机会
//     据地自立、与汗廷开战(地方独立，同"拥兵自立"决议，见 DeclareIndependence)；开国雄主、开国气象里不发生(RebellionSystem.CanAttempt)；
//   · 兵败离心：汗国成员"拥兵自立"的正统门槛(平时 BaseGate)按汗国这些仗里丢城的比例抬高——每丢一成城 +GatePerTenth，最多 MaxGate；
//   · 部众思归：汗国一场仗打满 HomesickGraceYears 年后，各城自然稳定度每年少 HomesickStability(至多 HomesickStabilityCap，见 FrontCollapse.WarWeariness)，
//     兵源每年打九折(至多六折，见 CorpsSystem.WarLevyShare)；开国雄主在位不思归(同久战疲敝)。
//   · 雄主之后第一次汗位更替(用户 2026-10-20："只要出现雄主快速统一，只要一死就会开始动荡"，见 UnificationSystem 游牧难一统)：
//     记下的游牧雄主(领着统一天下的；用户 2026-10-21 选"游牧雄主死后真分裂"后，袭掠成名的也记，见 RememberHero；
//     称汗开国的同日也记过，玩家随即反馈"帝国容易突然碎一地"，用户选回调后不再记——每个汗国开国之君身后都必乱，太频)
//     不在位后的第一回换汗，先分兀鲁思再判(见 Empire.CompleteSuccession)，不看积弊、正统，必乱——各兀鲁思(汗国分封的部族领主、
//     这一回新分的兄弟也算)与新近并入的别种政体成员各按 HeroBaseSplit + MaladySplit×积弊平均/100(至多 HeroSplitCap)的机会据地自立，
//     入伙不满十年的也在内(见 CanRiseAfterHero)；汗国分封的帝国平时没有汗位更替之乱，这一回也有；
//     之后的换汗照常。新汗若是新朝的开国雄主，登基头 15 年境内不起事(RulerTraitSystem.FounderShield)，这一回也就无人自立。
// 哈里发更替之乱(阿拉伯政体，用户 2026-10-20 选"阿拉伯也照游牧那样修"，数字比汗国轻)：新哈里发即位(OnCaliphSucceeded)、
//   苏丹(权臣)身故由新人承袭(OnSultanSucceeded，见 MilitaryRegency)时，积弊六条平均 ≥ ArabicCrisisMalady 或正统 < ArabicCrisisLegitimacy，
//   世袭的埃米尔(见 EmirateSystem)与 AnnexedYears 年内并入的别种政体成员，各按 ArabicBaseSplit + ArabicMaladySplit×积弊平均/100
//   的机会举兵自立(行省、羁縻、苏丹自己的国与同族不在此列)。
// 以上几路都归大分裂冷却管(见 UpheavalGuard，用户 2026-10-21 选)：上次大分裂 15 年内、土崩瓦解收束期内、已有分裂进程时不判
//   (雄主身后的必乱当次作废)；一次选出的城不超过帝国总城数的三分之一(雄主身后那一回二分之一)，按成员次序逐个选，下一家放不下就停。
// 分裂进程(用户 2026-10-21 反馈"突然裂开这个设定很突兀"，选"预警前兆、先割据后独立、争位内战代替集体出走"，见 SchismSystem)：
//   上面说的"据地自立""举兵自立"不再当月发生——选出的成员先列为不稳成员、进入预警(汗位、哈里发、苏丹 12~36 个月，雄主身后 6~12 个月)，
//   再割据 6~24 个月(不缴税、不随征)，然后其中最强的一家举兵争位；争位者胜即为新君(新苏丹)，败则诸部复服，相持五年才各自自立。
//   期间正统回升、打赢一仗、新君在位满三年都能化解(雄主身后只认打赢一仗)。史书各阶段各记一句(见 SchismSystem.Chronicle)。
public static class NomadCycleSystem
{
    // 汗位更替之乱的门槛：积弊 50 → 35、正统 50 → 70(用户 2026-10-21 定"汗位更替之乱的门槛"：汗国正统少有跌破 50，
    // 以前换汗几乎从不乱)→ 积弊 45、正统 62(同日玩家反馈"帝国容易突然碎一地"，用户选回调：正统 70 落在汗国的正常区间，
    // 新汗即位又先扣一截天命，几乎每次换汗都中)；哈里发更替之乱的积弊门槛另见 ArabicCrisisMalady，没跟着降
    private const float CrisisMalady = 45f, BaseSplit = 0.15f, MaladySplit = 0.5f;
    // 汗国大小加的争位机会：SizeFrom 城以上起算，再多 SizeSpan 城加满 SizeSplit
    private const float SizeSplit = 0.1f;
    private const int SizeFrom = 8, SizeSpan = 32;
    private const int CrisisLegitimacy = 62, AnnexedYears = 30;
    private const int BaseGate = 40, GatePerTenth = 10, MaxGate = 70;
    private const float HomesickGraceYears = 2f, HomesickStability = 5f, HomesickStabilityCap = 20f;
    private const float HomesickLevyPerYear = 0.1f, HomesickLevyFloor = 0.6f;
    public const string SuccessionReason = "rebellion_reason_khan_succession";
    // 雄主之后第一次汗位更替：每部起码的机会(平时 BaseSplit)、上限。0.4、至多 0.75(用户 2026-10-21 选"游牧雄主死后真分裂")
    // → 0.25、至多 0.5(同日玩家反馈"帝国容易突然碎一地"，用户选回调：加上兀鲁思 5 城就分、入伙不满十年也反，雄主一死汗国散掉大半)
    private const float HeroBaseSplit = 0.25f, HeroSplitCap = 0.5f;
    public const string HeroSuccessionReason = "rebellion_reason_hero_khan_succession";
    // 哈里发更替之乱：正统门槛、积弊门槛、机会(比汗国轻)。正统门槛 55 → 70(用户 2026-10-21 选"平常年景也坐大"：正常的哈里发
    // 正统七八十，以前从不发作)→ 62(同日选"哈里发能衰也能复兴")：判定就在新哈里发即位之后，即位已扣天命 20(正统约 −4)，
    // 受册封的 +8 也要重新求(见 PapacySystem.Caliphate)，门槛 70 实际相当于平时的 82；62 约合平时的 74
    private const int ArabicCrisisLegitimacy = 62;
    private const float ArabicCrisisMalady = 50f, ArabicBaseSplit = 0.1f, ArabicMaladySplit = 0.4f;
    public const string FitnaReason = "rebellion_reason_emirate_fitna";

    public static bool IsKhanate(Empire empire) =>
        empire != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom?.GetRegime()?.type == RegimeType.YouMu &&
        CultureService.GetRealmCulture(empire.CoreKingdom).IsNomadCulture();

    #region 汗位更替之乱

    // 新汗即位：平常先于即位分封兀鲁思，新分出去的兄弟这一回不自立；雄主之后的那一回先分后判(见 Empire.CompleteSuccession)
    public static void OnKhanSucceeded(Empire empire)
    {
        try
        {
            if (empire?.data == null || World.world == null) return;
            // 雄主之后第一次汗位更替：不看积弊、正统，必乱(见 UnificationSystem.TakeHeroTurmoil)
            bool heroic = UnificationSystem.TakeHeroTurmoil(empire, out string hero);
            if (!heroic && !IsKhanate(empire)) return;
            float average = MaladySystem.Average(empire);
            int legitimacy = empire.Legitimacy;
            // 每次换汗都可能争位(用户 2026-10-27 选；以前要积弊均值 ≥ CrisisMalady 或正统 < CrisisLegitimacy 才乱，
            // 汗国积弊十来点、正统七十出头是常态，平常换汗从来不乱)：叔伯兄弟据兀鲁思争位本是草原常态，机会另加汗国大小(SizeSplit)
            string khan = empire.Emperor?.getName() ?? "";
            // 大分裂冷却里、土崩瓦解收束期内不判(见 UpheavalGuard)；雄主身后的必乱上面 TakeHeroTurmoil 已取走，当次作废、不往后顺延
            if (UpheavalGuard.Blocked(empire, out string why))
            {
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} {(heroic ? $"雄主{hero}之后第一次换汗" : "新汗即位")}" +
                                   $"(新汗{khan}，积弊均{average:0}，正统{legitimacy})，{why}，不判汗位更替之乱{(heroic ? "，必乱当次作废" : "")}");
                return;
            }
            float chance = heroic ? Mathf.Min(HeroSplitCap, HeroBaseSplit + MaladySplit * average / 100f)
                : BaseSplit + MaladySplit * average / 100f + SizeSplit * Mathf.Clamp01((UpheavalGuard.Cities(empire) - SizeFrom) / (float)SizeSpan);
            // 一次选出的城至多帝国总城数的三分之一，雄主身后那一回二分之一；按成员次序逐个选，下一家放不下就停
            var budget = new UpheavalGuard.Budget(empire, heroic ? UpheavalGuard.HeroCapShare : UpheavalGuard.CapShare);
            var unstable = new List<Kingdom>();
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!Restive(empire, member, heroic) || Random.value >= chance) continue;
                int cities = member.cities.Count;
                if (!budget.Fits(cities)) break;
                unstable.Add(member);
                budget.Take(cities);
            }
            string detail = $"{(heroic ? $"雄主{hero}之后第一次换汗，" : "")}新汗{khan}，积弊均{average:0}，正统{legitimacy}，" +
                            $"每部机会 {chance:0.00}{(budget.Capped ? $"，已到上限 {budget.Cap}/{budget.Total} 城" : "")}";
            if (unstable.Count == 0)
            {
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} {detail}，无部离心");
                return;
            }
            // 分裂进程(用户 2026-10-21 选，见 SchismSystem)：以前选中的部当月据地自立(地方独立)；现在先列为不稳之部、进入预警，
            // 割据之后由其中最强的一家举兵争汗位。雄主身后的必乱开了进程即算兑现(上面 TakeHeroTurmoil 已取走)，只能靠打赢一仗化解
            // 化解门槛：正统回到门槛 +8 且积弊低于门槛(见 SchismSystem.Resolution)。平常年景正统本就高过 CrisisLegitimacy，
            // 照它算开了当月就化解；改按眼下的正统，要比换汗时再回升一截才算人心安定
            SchismSystem.Begin(empire, heroic ? SchismSystem.Hero : SchismSystem.Khan, unstable,
                heroic ? CrisisLegitimacy : Mathf.Max(CrisisLegitimacy, legitimacy), CrisisMalady, empire.Emperor, detail);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][汗位更替] 结算失败: {exception.Message}");
        }
    }

    // 新哈里发即位：新君还没受册封，哈里发国与印度(没有册封)正统同在一条线上，门槛不分
    public static void OnCaliphSucceeded(Empire empire) => Fitna(empire, null, "新哈里发即位", 0);

    // 苏丹身故，新苏丹承袭(见 MilitaryRegency.Succeed)：新苏丹的国与同族的埃米尔不反。
    // 哈里发照旧在位、多已受册封，印度没有那 +8，门槛对它减 8(见 EmirateSystem.InvestitureGap)
    public static void OnSultanSucceeded(Empire empire, Actor sultan) =>
        Fitna(empire, sultan, "新苏丹承袭", EmirateSystem.InvestitureGap(empire));

    private static void Fitna(Empire empire, Actor sultan, string occasion, int gateOffset)
    {
        try
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || World.world == null ||
                !EmirateSystem.Applies(empire)) return;
            float average = MaladySystem.Average(empire);
            int legitimacy = empire.Legitimacy;
            if (average < ArabicCrisisMalady && legitimacy >= ArabicCrisisLegitimacy - gateOffset) return;
            // 大分裂冷却里、土崩瓦解收束期内不判(见 UpheavalGuard)
            if (UpheavalGuard.Blocked(empire, out string why))
            {
                LogService.LogInfo($"[EmpireCraft][哈里发更替] {empire.GetEmpireName()} {occasion}(积弊均{average:0}，正统{legitimacy})，{why}，不判更替之乱");
                return;
            }
            float chance = ArabicBaseSplit + ArabicMaladySplit * average / 100f;
            Actor regent = sultan ?? MilitaryRegency.Regent(empire);
            // 一次选出的城至多帝国总城数的三分之一；按成员次序逐个选，下一家放不下就停
            var budget = new UpheavalGuard.Budget(empire, UpheavalGuard.CapShare);
            var unstable = new List<Kingdom>();
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!RestiveEmir(empire, member, regent) || Random.value >= chance) continue;
                int cities = member.cities.Count;
                if (!budget.Fits(cities)) break;
                unstable.Add(member);
                budget.Take(cities);
            }
            string detail = $"{occasion}，积弊均{average:0}，正统{legitimacy}，每家机会 {chance:0.00}" +
                            $"{(budget.Capped ? $"，已到上限 {budget.Cap}/{budget.Total} 城" : "")}";
            if (unstable.Count == 0)
            {
                LogService.LogInfo($"[EmpireCraft][哈里发更替] {empire.GetEmpireName()} {detail}，无家离心");
                return;
            }
            // 分裂进程(用户 2026-10-21 选，见 SchismSystem)：以前选中的埃米尔当月举兵自立(地方独立)；现在先列为不稳之家、进入预警，
            // 割据之后由其中最强的一家举兵争位(哈里发更替争哈里发之位，先挑能称对立哈里发的；苏丹更替争苏丹之位)。
            // 门槛照上面(苏丹更替对印度减去册封那 +8)，化解看它
            SchismSystem.Begin(empire, sultan != null ? SchismSystem.Sultan : SchismSystem.Caliph, unstable,
                ArabicCrisisLegitimacy - gateOffset, ArabicCrisisMalady, sultan ?? empire.Emperor, detail);
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][哈里发更替] 结算失败: {exception.Message}");
        }
    }

    // 会趁哈里发、苏丹更替自立的成员：世袭的埃米尔，或新近并入的别种政体成员；苏丹自己的国、苏丹同族的不反
    private static bool RestiveEmir(Empire empire, Kingdom member, Actor regent)
    {
        if (member?.data == null || member.isRekt() || member == empire.CoreKingdom || member.GetEmpire() != empire ||
            !member.hasKing() || member.capital == null || member.cities == null || member.cities.Count == 0 ||
            member.IsFactionRebelling() || member.IsLocalRebelling() || member.isInWarWith(empire.CoreKingdom) ||
            !RebellionSystem.CanAttempt(member)) return false;
        if (JimiSystem.IsJimiAdministration(member) || JimiSystem.IsJimiRegion(member, empire)) return false;
        if (regent?.data != null)
        {
            var clan = regent.GetSpecificClan();
            if (member.king == regent || member == regent.kingdom ||
                clan != null && SpecificClanManager.SameLineage(member.king.GetSpecificClan(), clan)) return false;
        }
        if (EmirateSystem.IsHereditaryEmir(empire, member)) return true;
        if (member.GetRegime()?.type == RegimeType.Arabic) return false;
        double joined = member.GetTimestampEmpire();
        return joined >= 0d && World.world.getCurWorldTime() >= joined && Date.getYearsSince(joined) < AnnexedYears;
    }

    // 会趁汗位更替自立的成员：兀鲁思(游牧政体的成员国，行省、羁縻除外)，或新近并入的别种政体成员；
    // 雄主之后第一次换汗(heroic)汗国分封的部族领主也算兀鲁思，入伙不满十年的也算(见 CanRiseAfterHero)
    private static bool Restive(Empire empire, Kingdom member, bool heroic = false)
    {
        if (member?.data == null || member.isRekt() || member == empire.CoreKingdom || member.GetEmpire() != empire ||
            !member.hasKing() || member.capital == null || member.cities == null || member.cities.Count == 0 ||
            member.IsFactionRebelling() || member.IsLocalRebelling() ||
            !(heroic ? CanRiseAfterHero(member) : RebellionSystem.CanAttempt(member))) return false;
        if (member.GetKingdomType() == KingdomType.YouMu_province || JimiSystem.IsJimiAdministration(member) ||
            JimiSystem.IsJimiRegion(member, empire)) return false;
        RegimeType? type = member.GetRegime()?.type;
        if (type == RegimeType.YouMu || heroic && type == RegimeType.NomadFeudalism) return true;
        double joined = member.GetTimestampEmpire();
        return joined >= 0d && World.world.getCurWorldTime() >= joined && Date.getYearsSince(joined) < AnnexedYears;
    }

    // 雄主之后第一次换汗能否起事：同 RebellionSystem.CanAttempt，只是不看入伙十年的宽限(GraceYearsRemaining)——雄主袭掠时收拢的部众、
    // 这一回新分的兀鲁思正是雄主一死要散的人(用户 2026-10-21 选"游牧雄主死后真分裂"：以前它们都在宽限里，雄主死了只丢一成)；
    // 新朝开国雄主登基头 15 年、开国气象里、神力拉入的照旧不起事
    private static bool CanRiseAfterHero(Kingdom member) =>
        !RulerTraitSystem.FounderShield(member) && !UnificationSystem.InGrace(member) && !MembershipCooldown.ManualJoinActive(member);

    // 记下在位的游牧雄主(用户 2026-10-21 选"游牧雄主死后真分裂")：以前只有发动统一战争时才记(见 UnificationSystem)，
    // 袭掠成名的雄主(NomadRaidSystem.PromoteHero)死了只按积弊、正统判，汗国很少散。
    // 称汗开国的大汗同日也在 EmpireManager.NewEmpire 收尾时记过(OnKhanateFounded)，玩家随即反馈"帝国容易突然碎一地"，
    // 用户选回调：称汗开国不再自动记为雄主(每个汗国的开国之君身后都必乱，太频)，那个入口已删；袭掠成名、发动统一的照旧记。
    // 只在开国雄主、中兴雄主在位时记(否则下个月就被当成不在位、凭空留下"必乱")；已记着另一位还没收场的不改记，
    // 免得吞掉他身后那一回必乱(他不在位的事由每月的 UnificationSystem.WatchHeroes 收场)
    public static void RememberHero(Empire empire, string occasion)
    {
        Kingdom core = empire?.CoreKingdom;
        Actor khan = empire?.Emperor;
        if (empire?.data == null || empire.isRekt() || core == null || khan == null || !khan.isAlive() ||
            !RulerTraitSystem.FounderReigns(core)) return;
        long recorded = empire.data.nomad_hero_id;
        if (recorded >= 0L && recorded != khan.id) return;
        UnificationSystem.RememberHero(core);
        if (recorded < 0L && empire.data.nomad_hero_id == khan.id)
            LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} {occasion}，{khan.getName()}记为雄主(他不在位后第一回换汗必乱)");
    }

    // 拥兵自立：退出帝国、与中央开战(地方独立)，记下起因(拥兵自立决议与汗位更替之乱共用)
    public static bool DeclareIndependence(Kingdom kingdom, Empire empire, string reasonKey)
    {
        if (kingdom?.data == null || kingdom.isRekt() || empire?.CoreKingdom == null || kingdom.capital == null) return false;
        RebellionCauseData cause = RebellionSystem.Capture(kingdom, kingdom.capital, reasonKey);
        War war = DiplomacyHelpers.wars.newWar(kingdom, empire.CoreKingdom, WarTypeLibrary.normal);
        if (war == null) return false;
        kingdom.FinishedSelfPlot();
        empire.leave(kingdom, true, true);
        war.SetEmpireWarType(EmpireWarType.地方独立);
        RebellionSystem.Record(kingdom, war, cause);
        return true;
    }

    #endregion

    #region 兀鲁思渐行渐远

    // 兀鲁思渐行渐远(用户 2026-10-27 选；汗国"如此稳定，不符合历史常态")：分出去的兀鲁思每过一代(DriftGenerationYears)自主一档，
    // 离汗廷远的(每 DriftDistanceRef 格多算一倍)、城多的(每多一城多 DriftPerCity)更快。自主满 DriftTop 档、大汗又弱——
    // 不是开国雄主在位，且正统不到 DriftLegitimacy、或新汗即位不满 DriftNewKhanYears 年、或这些仗里丢了一成城以上——
    // 时，每年 DriftChance 和平转为独立汗国(如金帐、伊儿之于大汗)，每个汗国每年至多走一部；分裂进程里、大分裂冷却里不判。
    // 每年一次(Empire 年度更新里游牧政体那一支)
    private const float DriftGenerationYears = 25f, DriftDistanceRef = 150f, DriftPerCity = 0.1f, DriftTop = 3f, DriftChance = 0.2f;
    private const int DriftLegitimacy = 70, DriftNewKhanYears = 5;
    // 离汗廷远：都城相距超过各成员的中位数，且至少 FarMinTiles 格
    private const float FarMinTiles = 60f;
    private static readonly Dictionary<long, double> DriftYear = new();

    public static void UlusDrift(Empire empire)
    {
        if (!IsKhanate(empire) || World.world == null) return;
        double now = World.world.getCurWorldTime();
        if (DriftYear.TryGetValue(empire.id, out double last) && now >= last && Date.getYearsSince(last) < 1) return;
        DriftYear[empire.id] = now;
        Kingdom core = empire.CoreKingdom;
        if (core?.capital == null || RulerTraitSystem.FounderReigns(core) || empire.data.schism_kind != SchismSystem.None ||
            UpheavalGuard.Blocked(empire, out _)) return;
        double since = empire.data.newEmperor_timestamp;
        bool newKhan = since >= 0d && now >= since && Date.getYearsSince(since) < DriftNewKhanYears;
        if (empire.Legitimacy >= DriftLegitimacy && !newKhan && LostShare(core) < 0.1f) return;
        Kingdom leaver = null;
        float best = DriftTop;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (!Ulus(empire, member)) continue;
            float autonomy = Autonomy(core, member);
            if (autonomy < best) continue;
            best = autonomy;
            leaver = member;
        }
        if (leaver == null || Random.value >= DriftChance) return;
        string name = leaver.GetKingdomName(), khan = leaver.king?.getName() ?? "", realm = empire.GetEmpireName();
        leaver.FinishedSelfPlot();
        empire.leave(leaver, true, false);
        if (leaver.IsInEmpire()) return;
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(leaver);
        string content = string.Format(LM.Get("ulus_drift_history"), name, khan, realm);
        empire.RecordHistory(directContent: content, kingdomId: leaver.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, leaver);
        LogService.LogInfo($"[EmpireCraft][兀鲁思自主] {content}(自主{best:0.0}档，正统{empire.Legitimacy}{(newKhan ? "，新汗" : "")})");
    }

    // 兀鲁思：游牧政体的成员国(行省、羁縻除外)，有君有都、没在起事、不在入伙宽限里
    private static bool Ulus(Empire empire, Kingdom member) =>
        member?.data != null && !member.isRekt() && member != empire.CoreKingdom && member.GetEmpire() == empire && member.hasKing() &&
        member.capital != null && member.cities?.Count > 0 && member.GetRegime()?.type == RegimeType.YouMu &&
        member.GetKingdomType() != KingdomType.YouMu_province && !JimiSystem.IsJimiAdministration(member) &&
        !JimiSystem.IsJimiRegion(member, empire) && !member.IsFactionRebelling() && !member.IsLocalRebelling() &&
        !member.isInWarWith(empire.CoreKingdom) && RebellionSystem.CanAttempt(member);

    // 自主几档：入伙几代 × (1 + 离汗廷几个 DriftDistanceRef) × (1 + 多出的城 × DriftPerCity)
    private static float Autonomy(Kingdom core, Kingdom member)
    {
        double joined = member.GetTimestampEmpire();
        if (joined < 0d || World.world.getCurWorldTime() < joined) return 0f;
        float generations = Date.getYearsSince(joined) / DriftGenerationYears;
        return generations * (1f + Distance(core, member) / DriftDistanceRef) * (1f + (member.cities.Count - 1) * DriftPerCity);
    }

    private static float Distance(Kingdom core, Kingdom member) =>
        core?.capital == null || member?.capital == null ? 0f : (core.capital.city_center - member.capital.city_center).magnitude;

    // 离汗廷远(争位兵败时远的不复归，见 SchismSystem.Settle)：都城相距超过各成员的中位数，且至少 FarMinTiles 格
    public static bool FarFromCourt(Empire empire, Kingdom member)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.capital == null || member?.capital == null || member == core) return false;
        List<float> distances = empire.kingdoms_list
            .Where(other => other?.data != null && !other.isRekt() && other != core && other.capital != null)
            .Select(other => Distance(core, other)).OrderBy(distance => distance).ToList();
        float own = Distance(core, member);
        return own >= FarMinTiles && (distances.Count == 0 || own > distances[distances.Count / 2]);
    }

    #endregion

    #region 兵败离心

    // 成员"拥兵自立"的正统门槛：正统不高于它才能自立。汗国的成员按这些仗里丢城的比例抬高
    public static int SecessionGate(Kingdom member)
    {
        Empire empire = member?.GetEmpire();
        if (!IsKhanate(empire)) return BaseGate;
        return Mathf.Min(MaxGate, BaseGate + GatePerTenth * Mathf.FloorToInt(LostShare(empire.CoreKingdom) * 10f + 0.0001f));
    }

    // 汗国(连同各成员)在眼下这些仗里丢了开战时几成的城(取丢得最多的一场)
    public static float LostShare(Kingdom core)
    {
        if (core?.data == null || core.isRekt()) return 0f;
        float share = 0f;
        foreach (War war in core.getWars())
        {
            if (war?.data == null || war.hasEnded()) continue;
            int lost = WarSituation.CitiesLost(war, core);
            if (lost <= 0) continue;
            int before = lost + WarSituation.SideCities(core);
            if (before > 0) share = Mathf.Max(share, lost / (float)before);
        }
        return share;
    }

    #endregion

    #region 部众思归

    private static bool Homesick(Kingdom realm) =>
        realm?.data != null && !realm.isRekt() && IsKhanate(realm.GetEmpire()) && !RulerTraitSystem.FounderReigns(realm);

    // 各城自然稳定度少多少(见 FrontCollapse.WarWeariness)：按打得最久的那场仗
    public static float StabilityLoss(Kingdom realm)
    {
        if (!Homesick(realm)) return 0f;
        float years = 0f;
        foreach (War war in realm.getWars())
            if (war?.data != null && !war.hasEnded()) years = Mathf.Max(years, WarSituation.WarYears(war));
        return years <= HomesickGraceYears ? 0f : Mathf.Min(HomesickStabilityCap, (years - HomesickGraceYears) * HomesickStability);
    }

    // 兵源打几折(见 CorpsSystem.WarLevyShare)：months 是本国(连同所在帝国的核心)打得最久的那场仗的月数
    public static float HomesickLevy(Kingdom kingdom, int months)
    {
        if (!Homesick(FrontSystem.Realm(kingdom))) return 1f;
        float years = months / 12f;
        return years <= HomesickGraceYears ? 1f
            : Mathf.Max(HomesickLevyFloor, 1f - HomesickLevyPerYear * (years - HomesickGraceYears));
    }

    // 诊断用：按本国与所在帝国核心打得最久的那场仗
    public static float HomesickLevy(Kingdom kingdom)
    {
        int months = 0;
        foreach (Kingdom side in new[] { kingdom, FrontSystem.Realm(kingdom) })
        {
            if (side?.data == null || side.isRekt()) continue;
            foreach (War war in side.getWars())
                if (war?.data != null && !war.hasEnded()) months = Mathf.Max(months, Date.getMonthsSince(war.data.created_time));
        }
        return HomesickLevy(kingdom, months);
    }

    #endregion
}
