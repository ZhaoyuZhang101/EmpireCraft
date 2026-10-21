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
//   · 汗位更替之乱：新汗即位(KingdomPatch 的 setKing 补丁，见 OnKhanSucceeded)时，汗国朝政积弊六条平均 ≥ CrisisMalady 或正统 < CrisisLegitimacy，
//     各兀鲁思(游牧政体的成员国；行省、羁縻除外)，以及 AnnexedYears 年内并入的别种政体成员，各按 BaseSplit + MaladySplit×积弊平均/100 的机会
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
// 以上几路都归大分裂冷却管(见 UpheavalGuard，用户 2026-10-21 选)：上次大分裂 15 年内、土崩瓦解收束期内不判(雄主身后的必乱当次作废)；
//   一次带走的城不超过帝国总城数的三分之一(雄主身后那一回二分之一)，按成员次序逐个放走，下一家放不下就停；
//   平常的汗位、哈里发/苏丹更替之乱除各部起事的那句外，史书另记一条总述。
public static class NomadCycleSystem
{
    // 汗位更替之乱的门槛：积弊 50 → 35、正统 50 → 70(用户 2026-10-21 定"汗位更替之乱的门槛"：汗国正统少有跌破 50，
    // 以前换汗几乎从不乱)→ 积弊 45、正统 62(同日玩家反馈"帝国容易突然碎一地"，用户选回调：正统 70 落在汗国的正常区间，
    // 新汗即位又先扣一截天命，几乎每次换汗都中)；哈里发更替之乱的积弊门槛另见 ArabicCrisisMalady，没跟着降
    private const float CrisisMalady = 45f, BaseSplit = 0.15f, MaladySplit = 0.5f;
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
            if (!heroic && average < CrisisMalady && legitimacy >= CrisisLegitimacy) return;
            string khan = empire.Emperor?.getName() ?? "";
            // 大分裂冷却里、土崩瓦解收束期内不判(见 UpheavalGuard)；雄主身后的必乱上面 TakeHeroTurmoil 已取走，当次作废、不往后顺延
            if (UpheavalGuard.Blocked(empire, out string why))
            {
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} {(heroic ? $"雄主{hero}之后第一次换汗" : "新汗即位")}" +
                                   $"(新汗{khan}，积弊均{average:0}，正统{legitimacy})，{why}，不判汗位更替之乱{(heroic ? "，必乱当次作废" : "")}");
                return;
            }
            float chance = heroic ? Mathf.Min(HeroSplitCap, HeroBaseSplit + MaladySplit * average / 100f)
                : BaseSplit + MaladySplit * average / 100f;
            // 一次带走的城至多帝国总城数的三分之一，雄主身后那一回二分之一；按成员次序逐个放走，下一家放不下就停
            var budget = new UpheavalGuard.Budget(empire, heroic ? UpheavalGuard.HeroCapShare : UpheavalGuard.CapShare);
            var split = new List<string>();
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!Restive(empire, member, heroic) || Random.value >= chance) continue;
                int cities = member.cities.Count;
                if (!budget.Fits(cities)) break;
                string name = member.GetKingdomName();
                if (!DeclareIndependence(member, empire, heroic ? HeroSuccessionReason : SuccessionReason)) continue;
                split.Add(name);
                budget.Take(cities);
            }
            if (split.Count > 0) UpheavalGuard.Mark(empire, heroic ? $"雄主{hero}身后分裂" : "汗位更替之乱", budget);
            if (heroic)
            {
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} 雄主{hero}之后第一次换汗(新汗{khan}，积弊均{average:0}，正统{legitimacy})，" +
                                   $"{(split.Count > 0 ? string.Join("、", split) + " 据地自立" : "无人自立")}(每部机会 {chance:0.00}" +
                                   $"{(budget.Capped ? $"，已到上限 {budget.Cap} 城" : "")})");
                if (split.Count > 0)
                    EventRecorder.Record(empire, string.Format(LM.Get("khan_hero_turmoil_history"), empire.GetEmpireName(), hero, khan, split.Count));
            }
            else if (split.Count > 0)
            {
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} 新汗即位(积弊均{average:0}，正统{legitimacy})，" +
                                   $"{string.Join("、", split)} 据地自立(每部机会 {chance:0.00}{(budget.Capped ? $"，已到上限 {budget.Cap} 城" : "")})");
                // 史书总述(用户 2026-10-21 选：以前只有各部起事的那句，看不出是一场汗位更替之乱)
                EventRecorder.Record(empire, string.Format(LM.Get("khan_turmoil_history"), empire.GetEmpireName(), khan, split.Count));
            }
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
            // 一次带走的城至多帝国总城数的三分之一；按成员次序逐个放走，下一家放不下就停
            var budget = new UpheavalGuard.Budget(empire, UpheavalGuard.CapShare);
            var split = new List<string>();
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!RestiveEmir(empire, member, regent) || Random.value >= chance) continue;
                int cities = member.cities.Count;
                if (!budget.Fits(cities)) break;
                string name = member.GetKingdomName();
                if (!DeclareIndependence(member, empire, FitnaReason)) continue;
                split.Add(name);
                budget.Take(cities);
            }
            if (split.Count == 0) return;
            UpheavalGuard.Mark(empire, sultan != null ? "苏丹更替之乱" : "哈里发更替之乱", budget);
            LogService.LogInfo($"[EmpireCraft][哈里发更替] {empire.GetEmpireName()} {occasion}(积弊均{average:0}，正统{legitimacy})，" +
                               $"{string.Join("、", split)} 举兵自立(每家机会 {chance:0.00}{(budget.Capped ? $"，已到上限 {budget.Cap} 城" : "")})");
            // 史书总述(用户 2026-10-21 选：以前只有各家起事的那句)。苏丹的称呼按文化(印度称摄政王，见 MilitaryRegency.Establish)
            if (sultan != null)
            {
                string titleKey = MilitaryRegency.TitleKey(empire);
                string title = LM.Get(DynastyNaming.CultureVariant(empire.CoreKingdom, titleKey) ?? titleKey);
                EventRecorder.Record(empire, string.Format(LM.Get("sultan_fitna_history"), empire.GetEmpireName(), sultan.getName(), title,
                    split.Count), sultan);
            }
            else
                EventRecorder.Record(empire, string.Format(LM.Get("caliph_fitna_history"), empire.GetEmpireName(),
                    empire.Emperor?.getName() ?? "", split.Count));
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
