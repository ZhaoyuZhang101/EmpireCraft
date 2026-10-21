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
//     记下的游牧雄主(领着统一天下的；用户 2026-10-21 选"游牧雄主死后真分裂"后，袭掠成名的、称汗开国的也记，见 RememberHero)
//     不在位后的第一回换汗，先分兀鲁思再判(见 Empire.CompleteSuccession)，不看积弊、正统，必乱——各兀鲁思(汗国分封的部族领主、
//     这一回新分的兄弟也算)与新近并入的别种政体成员各按 HeroBaseSplit + MaladySplit×积弊平均/100(至多 HeroSplitCap)的机会据地自立，
//     入伙不满十年的也在内(见 CanRiseAfterHero)；汗国分封的帝国平时没有汗位更替之乱，这一回也有；
//     之后的换汗照常。新汗若是新朝的开国雄主，登基头 15 年境内不起事(RulerTraitSystem.FounderShield)，这一回也就无人自立。
// 哈里发更替之乱(阿拉伯政体，用户 2026-10-20 选"阿拉伯也照游牧那样修"，数字比汗国轻)：新哈里发即位(OnCaliphSucceeded)、
//   苏丹(权臣)身故由新人承袭(OnSultanSucceeded，见 MilitaryRegency)时，积弊六条平均 ≥ ArabicCrisisMalady 或正统 < ArabicCrisisLegitimacy，
//   世袭的埃米尔(见 EmirateSystem)与 AnnexedYears 年内并入的别种政体成员，各按 ArabicBaseSplit + ArabicMaladySplit×积弊平均/100
//   的机会举兵自立(行省、羁縻、苏丹自己的国与同族不在此列)。
public static class NomadCycleSystem
{
    // 汗位更替之乱的门槛：积弊 50 → 35、正统 50 → 70(用户 2026-10-21 定"汗位更替之乱的门槛"：汗国正统少有跌破 50，
    // 以前换汗几乎从不乱)；哈里发更替之乱的积弊门槛另见 ArabicCrisisMalady，没跟着降
    private const float CrisisMalady = 35f, BaseSplit = 0.15f, MaladySplit = 0.5f;
    private const int CrisisLegitimacy = 70, AnnexedYears = 30;
    private const int BaseGate = 40, GatePerTenth = 10, MaxGate = 70;
    private const float HomesickGraceYears = 2f, HomesickStability = 5f, HomesickStabilityCap = 20f;
    private const float HomesickLevyPerYear = 0.1f, HomesickLevyFloor = 0.6f;
    public const string SuccessionReason = "rebellion_reason_khan_succession";
    // 雄主之后第一次汗位更替：每部起码的机会(平时 BaseSplit)、上限
    private const float HeroBaseSplit = 0.4f, HeroSplitCap = 0.75f;
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
            float chance = heroic ? Mathf.Min(HeroSplitCap, HeroBaseSplit + MaladySplit * average / 100f)
                : BaseSplit + MaladySplit * average / 100f;
            var split = new List<string>();
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!Restive(empire, member, heroic) || Random.value >= chance) continue;
                string name = member.GetKingdomName();
                if (DeclareIndependence(member, empire, heroic ? HeroSuccessionReason : SuccessionReason)) split.Add(name);
            }
            if (heroic)
            {
                string khan = empire.Emperor?.getName() ?? "";
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} 雄主{hero}之后第一次换汗(新汗{khan}，积弊均{average:0}，正统{legitimacy})，" +
                                   $"{(split.Count > 0 ? string.Join("、", split) + " 据地自立" : "无人自立")}(每部机会 {chance:0.00})");
                if (split.Count > 0)
                    EventRecorder.Record(empire, string.Format(LM.Get("khan_hero_turmoil_history"), empire.GetEmpireName(), hero, khan, split.Count));
            }
            else if (split.Count > 0)
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} 新汗即位(积弊均{average:0}，正统{legitimacy})，" +
                                   $"{string.Join("、", split)} 据地自立(每部机会 {chance:0.00})");
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
            float chance = ArabicBaseSplit + ArabicMaladySplit * average / 100f;
            Actor regent = sultan ?? MilitaryRegency.Regent(empire);
            var split = new List<string>();
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!RestiveEmir(empire, member, regent) || Random.value >= chance) continue;
                string name = member.GetKingdomName();
                if (DeclareIndependence(member, empire, FitnaReason)) split.Add(name);
            }
            if (split.Count > 0)
                LogService.LogInfo($"[EmpireCraft][哈里发更替] {empire.GetEmpireName()} {occasion}(积弊均{average:0}，正统{legitimacy})，" +
                                   $"{string.Join("、", split)} 举兵自立(每家机会 {chance:0.00})");
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
    // 袭掠成名的雄主(NomadRaidSystem.PromoteHero)、称汗开国的大汗(OnKhanateFounded)死了只按积弊、正统判，汗国很少散。
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

    // 称汗开国(EmpireManager.NewEmpire 收尾时调用)：游牧政体、游牧文化的汗国，开国之君记为雄主
    public static void OnKhanateFounded(Empire empire)
    {
        try
        {
            if (IsKhanate(empire)) RememberHero(empire, "称汗开国");
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][汗位更替] 开国记雄主失败: {exception.Message}");
        }
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
