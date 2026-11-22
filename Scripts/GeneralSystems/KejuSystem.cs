using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 科举(群友 2026-11-22 的三件事"开科取士""特开恩科""科场舞弊")：
//   以前乡试、会试、殿试只在"本国按科举选任君主"时才开——帝国的核心都是世袭(选任方式被改回世袭，见
//   KingdomExtension.EnsureHereditaryThrone)，行了科举制度的朝廷其实一科都不开；开了也不花钱、不涨天命。现在：
//   · 例行开科：行了科举制度(华夏"科举取士"，或本国按科举选任)的朝廷每 IntervalYears 年一科，照旧三级考试、中榜的补官缺
//     (见 ExamSystem、OfficeSelector)；每科花年经常收入的 CostShare、天命 +1。死战之中、府库不到三科之费的停科，下回再开；
//   · 特开恩科(朝堂议题，见 TempFac_特开恩科)：新君即位 GraceYears 年内、府库尚可，朝议可请加开一科——准了耗一科之费、
//     天命 +2；这两年没开成的，士子怅然，天命 -1；
//   · 科场舞弊：每科放榜后按 FraudBase + 吏治败坏 × FraudGraft 的机会案发。朝堂议题"严查科场"(见 TempFac_严查科场)准了：
//     再耗一科之费、另开一科，天命 +2、吏治之弊减 PunishRelief；FraudYears 年里没人查办就从轻发落：天命 -1、吏治之弊加 LenientGraft。
//   两条议题走朝议五步(见 CourtSystem)，皇帝批红；例行开科是常例，不经朝议、不记史书(进士及第照旧各记一条)。
public static class KejuSystem
{
    private const string ExamNode = "huaxia_examination_officialdom";
    public const int IntervalYears = 3, GraceYears = 2, FraudYears = 2;
    private const float CostShare = 0.02f, FraudBase = 0.08f, FraudGraft = 0.25f, PunishRelief = 4f, LenientGraft = 3f;
    private const int MinCost = 20;

    public static bool HasKeju(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (empire?.data == null || empire.isRekt() || core?.data == null || core.isRekt()) return false;
        if (core.GetRegime()?.GetLeaderSelectMethod() == LeaderSelectMethod.Exam) return true;
        string culture = CultureService.GetInstitutionCulture(core);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.IsEnacted(culture, ExamNode);
    }

    private static int Cost(Empire empire) =>
        Math.Max(MinCost, (int)(StateDebtSystem.AnnualStableIncome(empire.CoreKingdom) * (double)CostShare));

    // 府库够不够开 rounds 科(不设国库的照开)
    private static bool Afford(Empire empire, float rounds) =>
        !TreasurySystem.Enabled(empire.CoreKingdom) || empire.CoreKingdom.GetTreasuryBalance() >= Cost(empire) * rounds;

    private static bool Calm(Empire empire) => !WarGoalSystem.FightingForSurvival(empire.CoreKingdom);

    private static bool Due(Empire empire)
    {
        double last = empire.data.last_exam_timestamp;
        return last <= 0d || World.world.getCurWorldTime() < last || Date.getYearsSince(last) >= IntervalYears;
    }

    // 例行开科(核心王国的 AI 轮到时问一次，见 EmpireCraftKingdomBehCheckExam)
    public static void TryRegular(Empire empire)
    {
        if (!HasKeju(empire) || !Due(empire)) return;
        // 停科：不记开科时间，缓过来就开
        if (!Calm(empire) || !Afford(empire, 3f)) return;
        Hold(empire, 1, regular: true);
    }

    // regular：常科——记开科时间(三年之期从这一科起算)、放榜后可能案发。恩科、案后重开的一科不在三年大比之列，都不算
    private static void Hold(Empire empire, int mandate, bool regular)
    {
        Kingdom core = empire.CoreKingdom;
        int cost = Cost(empire);
        if (TreasurySystem.Enabled(core)) core.SubMoney(Math.Min(cost, Math.Max(0, core.GetMoney())), TreasuryCategory.Administration);
        try
        {
            foreach (City city in empire.AllCities())
                if (city?.data != null && !city.isRekt()) ExamSystem.startExam(ExamSystem.ExamType.City, city);
            foreach (Kingdom province in empire.kingdoms_hashset)
                if (province?.data != null && !province.isRekt()) ExamSystem.startExam(ExamSystem.ExamType.Province, province);
            ExamSystem.startExam(ExamSystem.ExamType.Empire, empire);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][科举] {empire.GetEmpireName()} 开科出错: {exception.Message}");
        }
        if (regular) empire.data.last_exam_timestamp = World.world.getCurWorldTime();
        empire.AddMandate(mandate);
        // 科场舞弊：放榜后案发，等朝廷处置(见 Yearly、Punish)
        if (!regular || empire.data.keju_fraud_at >= 0d ||
            UnityEngine.Random.value >= FraudBase + FraudGraft * MaladySystem.Severity(empire, MaladySystem.Graft)) return;
        empire.data.keju_fraud_at = World.world.getCurWorldTime();
        EventRecorder.Record(empire, string.Format(LM.Get("keju_fraud_history"), empire.GetEmpireName()));
        LogService.LogInfo($"[EmpireCraft][科举] {empire.GetEmpireName()} 科场舞弊案发");
    }

    #region 特开恩科

    private static bool NewReign(Empire empire, out long ruler)
    {
        ruler = empire.Emperor?.id ?? -1L;
        double since = empire.data.newEmperor_timestamp;
        return ruler >= 0L && since >= 0d && World.world.getCurWorldTime() >= since && Date.getYearsSince(since) < GraceYears;
    }

    public static bool GraceOpen(Empire empire) =>
        HasKeju(empire) && NewReign(empire, out long ruler) && empire.data.keju_grace_ruler_id != ruler &&
        Calm(empire) && Afford(empire, 1.5f);

    public static bool HoldGrace(Empire empire)
    {
        if (!GraceOpen(empire)) return false;
        empire.data.keju_grace_ruler_id = empire.Emperor.id;
        Hold(empire, 2, regular: false);
        EventRecorder.Record(empire, string.Format(LM.Get("keju_grace_history"), empire.GetEmpireName(), empire.Emperor.getName()));
        return true;
    }

    #endregion

    #region 科场舞弊

    public static bool FraudOpen(Empire empire) => HasKeju(empire) && empire.data.keju_fraud_at >= 0d;

    // 严鞫此案、另开一科
    public static bool Punish(Empire empire)
    {
        if (!FraudOpen(empire)) return false;
        empire.data.keju_fraud_at = -1d;
        Hold(empire, 2, regular: false);
        MaladySystem.Relieve(empire, MaladySystem.Graft, PunishRelief);
        EventRecorder.Record(empire, string.Format(LM.Get("keju_fraud_punished_history"), empire.GetEmpireName()));
        return true;
    }

    #endregion

    // 每年(见 ConstitutionalEconomySystem.Update 的年度段)
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) return;
        EmpireData data = empire.data;
        if (!HasKeju(empire))
        {
            data.keju_fraud_at = -1d;
            return;
        }
        try
        {
            // 新君即位满了 GraceYears 年还没开恩科：士子怅然。头一回(旧档、刚行科举)只记下这位君主，不罚
            long ruler = empire.Emperor?.id ?? -1L;
            if (ruler >= 0L && data.keju_grace_ruler_id != ruler && !NewReign(empire, out _))
            {
                bool first = data.keju_grace_ruler_id < 0L;
                data.keju_grace_ruler_id = ruler;
                if (!first && data.newEmperor_timestamp >= 0d && Date.getYearsSince(data.newEmperor_timestamp) <= GraceYears + 1)
                {
                    empire.AddMandate(-1);
                    EventRecorder.RecordRoutine(empire, string.Format(LM.Get("keju_grace_missed_history"), empire.GetEmpireName()));
                }
            }
            // 舞弊案久拖不办：从轻发落
            if (data.keju_fraud_at >= 0d &&
                (World.world.getCurWorldTime() < data.keju_fraud_at || Date.getYearsSince(data.keju_fraud_at) >= FraudYears))
            {
                data.keju_fraud_at = -1d;
                empire.AddMandate(-1);
                MaladySystem.Aggravate(empire, MaladySystem.Graft, LenientGraft);
                EventRecorder.RecordRoutine(empire, string.Format(LM.Get("keju_fraud_lenient_history"), empire.GetEmpireName()));
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][科举] {empire.GetEmpireName()} 年度结算失败: {exception.Message}");
        }
    }
}
