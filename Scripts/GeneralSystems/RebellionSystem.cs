using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class RebellionSystem
{
    public static int GraceYearsRemaining(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (World.world == null || empire == null || empire.isRekt() || kingdom == empire.CoreKingdom) return 0;
        double joinedAt = kingdom.GetFiedTimestamp();
        double now = World.world.getCurWorldTime();
        return RebellionRules.GraceYearsRemaining(true, joinedAt, now,
            joinedAt >= 0d && now >= joinedAt ? Date.getYearsSince(joinedAt) : 0);
    }

    // 只在自动起事入口、改动归属之前调用。已拆出的叛军没有原帝国信息，不能据它重新判断。
    // 统一战争期间、统一天下后三十年(开国气象)不起事(见 UnificationSystem.InGrace)；
    // 神力拉入的成员十五年内不起事、不自立(见 MembershipCooldown.ManualJoinActive)
    public static bool CanAttempt(Kingdom kingdom) => kingdom != null && !kingdom.isRekt() &&
        GraceYearsRemaining(kingdom) == 0 && !RulerTraitSystem.FounderShield(kingdom) && !UnificationSystem.InGrace(kingdom) &&
        !MembershipCooldown.ManualJoinActive(kingdom);

    // 拥兵自立的向背(用户 2026-10-20 定"拥兵自立失效要修")：对朝廷的好感里去掉"同帝国 +100""受封五十年内 +100"两项名分——
    // 它们只说明是成员、不说明忠心；"正统 (正统−50)×5"一项改按离自立门槛的远近算 (正统 − 门槛)×5，门槛照
    // NomadCycleSystem.SecessionGate(平时 40，汗国兵败每丢一成城抬 10、至多 70)。其余好感(同文化同宗教、宗族、
    // 法理被占、派系不同……)照旧。合计低于 SecessionThreshold 才肯拥兵自立。以前名分两项就有 +200，正统掉到个位数好感仍为正，自立从来过不了
    public const int SecessionLegitimacyWeight = 5;
    // 自立的向背门槛(玩家 2026-10-21 反馈"除了阿拉伯和游牧，其他国家很容易分裂"，用户选"拥兵自立加忠诚余量")：
    // 以前合计小于 0 就自立，正统一跌到门槛，好感平平的封地几年内几乎全走；改为明显离心(−50 以下)才自立——
    // 正统每低于门槛 1 点抵 5 点好感，好感平平(0)的封地要正统跌到 30 以下才走
    public const int SecessionThreshold = -50;
    private static readonly HashSet<string> SecessionIgnoredOpinions = new()
        { "opinion_in_same_empire", "opinion_just_enfeoff", "opinion_empire_loyalty" };

    public static int SecessionAllegiance(Kingdom kingdom, Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (kingdom?.data == null || core?.data == null || kingdom == core || World.world?.diplomacy == null) return 0;
        KingdomOpinion opinion = World.world.diplomacy.getOpinion(kingdom, core);
        if (opinion == null) return 0;
        int total = opinion.total;
        if (opinion.results != null)
            foreach (KeyValuePair<OpinionAsset, int> pair in opinion.results)
                if (pair.Key?.id != null && SecessionIgnoredOpinions.Contains(pair.Key.id)) total -= pair.Value;
        return total + (empire.Legitimacy - NomadCycleSystem.SecessionGate(kingdom)) * SecessionLegitimacyWeight;
    }

    public static float ChanceFactor(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return RebellionRules.LegitimacyFactor(empire != null && !empire.isRekt() ? empire.Legitimacy : -1) *
               (ModernStability.IsModern(kingdom) ? ModernStability.RebellionFactor : 1f) * PhaseFactor(kingdom);
    }

    // 天命阶段(用户 2026-10-19 选"以天命为准")：治世起事少两成五，动荡多两成五；乱世天下无主照常
    private static float PhaseFactor(Kingdom kingdom) => TianxiaSystem.PhaseOf(kingdom) switch
    {
        TianxiaSystem.Peace => 0.75f,
        TianxiaSystem.Unrest => 1.25f,
        _ => 1f
    };

    public static bool CanContinueCityPlot(Actor actor, bool forced = false)
    {
        City city = actor?.city;
        Kingdom kingdom = actor?.kingdom;
        if (actor == null || actor.isKing() || city == null || city.isRekt() || city.kingdom != kingdom ||
            !CanAttempt(kingdom) || !CityStabilitySystem.CanRise(city) ||
            city == kingdom.capital && (kingdom.GetEmpire() == null || kingdom.IsEmpire()))
            return false;
        if (forced || !city.isHappy()) return true;
        CityValueSnapshot value = city.GetCityStrategicValue();
        return CityValueRules.CanRebelWhileContent(value) &&
               city.getLoyalty() <= CityValueRules.GetIsolationRebellionLoyaltyThreshold(value);
    }

    public static RebellionCauseData Capture(Kingdom origin, City city, string reasonKey, string detail = "")
    {
        Empire empire = origin?.GetEmpire();
        return new RebellionCauseData
        {
            reason_key = reasonKey,
            detail = detail ?? "",
            origin_name = empire?.GetEmpireFullName() ?? origin?.GetKingdomFullName() ?? "",
            city_name = city?.GetCityName() ?? "",
            origin_empire_id = empire?.id ?? -1L,
            legitimacy = empire?.Legitimacy ?? -1,
            loyalty = city?.getLoyalty(),
            stability = city != null && TreasurySystem.Enabled(city) ? CityStabilitySystem.Effective(city) : null,
            timestamp = World.world?.getCurWorldTime() ?? -1d
        };
    }

    public static string CityLoyaltyCauses(City city)
    {
        if (city?.kingdom == null || AssetManager.loyalty_library == null) return "";
        return string.Join(" / ", AssetManager.loyalty_library.list.Where(asset => asset.calc != null)
            .Select(asset => (asset.translation_key, value: asset.calc(city)))
            .Where(item => item.value < 0).OrderBy(item => item.value).Take(3)
            .Select(item => LM.Get(item.translation_key) + " " + item.value));
    }

    public static string Metrics(RebellionCauseData cause)
    {
        var items = new List<string>();
        if (cause?.legitimacy >= 0)
            items.Add(string.Format(LM.Get("rebellion_legitimacy_format"), cause.legitimacy));
        if (cause?.loyalty != null)
            items.Add(string.Format(LM.Get("rebellion_loyalty_format"), cause.loyalty.Value));
        if (cause?.stability != null)
            items.Add(string.Format(LM.Get("rebellion_stability_format"), cause.stability.Value));
        return string.Join(" / ", items);
    }

    // 成功开战之后才记录，避免失败的尝试在史书里变成已发生的叛乱。
    public static void Record(Kingdom rebel, War war, RebellionCauseData cause)
    {
        if (rebel == null || cause == null) return;
        rebel.GetOrCreate().last_rebellion_cause = cause;
        if (war != null) war.GetOrCreate().rebellion_cause = cause;
        string reason = LM.Get(cause.reason_key);
        string details = string.IsNullOrWhiteSpace(cause.detail) ? reason : reason + "：" + cause.detail;
        string text = string.Format(LM.Get("rebellion_cause_history"), rebel.GetKingdomFullName(), details,
            cause.origin_name, Metrics(cause));
        Empire origin = cause.origin_empire_id >= 0 ? ModClass.EMPIRE_MANAGER?.get(cause.origin_empire_id) : null;
        EventRecorder.Record(origin, text, rebel.king, rebel);
    }

    // 原版提示的左右列互不避让；说明放左列并短行换行，数值单独放右列。
    public static IEnumerable<string> DetailLines(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) yield break;
        string line = "";
        int width = 0;
        foreach (char c in detail)
        {
            if (c == '\r') continue;
            int units = c > 127 ? 2 : 1;
            if (c == '\n' || width + units > 22)
            {
                if (line.Length > 0) yield return line;
                line = "";
                width = 0;
                if (c == '\n') continue;
            }
            line += c;
            width += units;
        }
        if (line.Length > 0) yield return line;
    }
}
