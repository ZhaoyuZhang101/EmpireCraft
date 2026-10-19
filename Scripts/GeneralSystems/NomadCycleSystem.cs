using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
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
public static class NomadCycleSystem
{
    private const float CrisisMalady = 50f, BaseSplit = 0.15f, MaladySplit = 0.5f;
    private const int CrisisLegitimacy = 50, AnnexedYears = 30;
    private const int BaseGate = 40, GatePerTenth = 10, MaxGate = 70;
    private const float HomesickGraceYears = 2f, HomesickStability = 5f, HomesickStabilityCap = 20f;
    private const float HomesickLevyPerYear = 0.1f, HomesickLevyFloor = 0.6f;
    public const string SuccessionReason = "rebellion_reason_khan_succession";

    public static bool IsKhanate(Empire empire) =>
        empire != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom?.GetRegime()?.type == RegimeType.YouMu;

    #region 汗位更替之乱

    // 新汗即位(先于即位分封兀鲁思，新分出去的兄弟这一回不自立)
    public static void OnKhanSucceeded(Empire empire)
    {
        try
        {
            if (!IsKhanate(empire) || World.world == null) return;
            float average = MaladySystem.Average(empire);
            int legitimacy = empire.Legitimacy;
            if (average < CrisisMalady && legitimacy >= CrisisLegitimacy) return;
            float chance = BaseSplit + MaladySplit * average / 100f;
            var split = new List<string>();
            foreach (Kingdom member in empire.kingdoms_list.ToList())
            {
                if (!Restive(empire, member) || Random.value >= chance) continue;
                string name = member.GetKingdomName();
                if (DeclareIndependence(member, empire, SuccessionReason)) split.Add(name);
            }
            if (split.Count > 0)
                LogService.LogInfo($"[EmpireCraft][汗位更替] {empire.GetEmpireName()} 新汗即位(积弊均{average:0}，正统{legitimacy})，" +
                                   $"{string.Join("、", split)} 据地自立(每部机会 {chance:0.00})");
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][汗位更替] 结算失败: {exception.Message}");
        }
    }

    // 会趁汗位更替自立的成员：兀鲁思(游牧政体的成员国，行省、羁縻除外)，或新近并入的别种政体成员
    private static bool Restive(Empire empire, Kingdom member)
    {
        if (member?.data == null || member.isRekt() || member == empire.CoreKingdom || member.GetEmpire() != empire ||
            !member.hasKing() || member.capital == null || member.cities == null || member.cities.Count == 0 ||
            member.IsFactionRebelling() || member.IsLocalRebelling() || !RebellionSystem.CanAttempt(member)) return false;
        if (member.GetKingdomType() == KingdomType.YouMu_province || JimiSystem.IsJimiAdministration(member) ||
            JimiSystem.IsJimiRegion(member, empire)) return false;
        if (member.GetRegime()?.type == RegimeType.YouMu) return true;
        double joined = member.GetTimestampEmpire();
        return joined >= 0d && World.world.getCurWorldTime() >= joined && Date.getYearsSince(joined) < AnnexedYears;
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
