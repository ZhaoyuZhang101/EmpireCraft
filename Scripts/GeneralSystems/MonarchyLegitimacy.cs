using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 前现代正统与宪政合法性采用相同的来源式模型：当前治理决定稳定性，旧天命只作为有限的法统贡献。
public static class MonarchyLegitimacy
{
    private const float CacheSeconds = 1f;
    private static readonly Dictionary<long, (bool applies, int value, float at)> Cache = new();
    private static object _cacheWorld;

    private static (bool applies, int value) Cached(Empire empire)
    {
        if (!ReferenceEquals(_cacheWorld, World.world))
        {
            _cacheWorld = World.world;
            Cache.Clear();
        }
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(empire.id, out var cached) && now >= cached.at && now - cached.at < CacheSeconds)
            return (cached.applies, cached.value);
        bool applies = UsesTraditionalModel(empire);
        int value = applies ? Mathf.Clamp(Breakdown(empire).Sum(item => item.value), 0, 100) : empire.data.Mandate;
        Cache[empire.id] = (applies, value, now);
        return (applies, value);
    }

    public static bool Applies(Empire empire) => empire?.data != null && Cached(empire).applies;
    // 独立于数值计算，供年度积弊/苛政选择模型，不经正统来源列表，避免互相递归。
    public static bool UsesTraditionalModel(Empire empire) => empire?.data != null && World.world != null &&
        empire.CoreKingdom != null && !empire.CoreKingdom.isRekt() && !empire.IsArchived() &&
        !ModernLegitimacy.Applies(empire) && !WarlordEraSystem.IsProvisionalGovernment(empire) &&
        !RepublicSystem.IsTransitioning(empire);
    public static int Get(Empire empire) => empire?.data == null ? 0 : Cached(empire).value;
    public static void Invalidate(Empire empire = null)
    {
        if (empire == null) Cache.Clear();
        else Cache.Remove(empire.id);
    }

    // 只读取已结算的苛政与财政数据，不在城市忠诚/外交频繁读取时扫描人口、地块或单位。
    public static List<(string key, int value)> Breakdown(Empire empire)
    {
        var items = new List<(string key, int value)>();
        if (empire?.data == null || empire.CoreKingdom == null) return items;
        items.Add(("mandate_governance_base", MonarchyLegitimacyRules.Base));
        bool ruler = empire.Emperor != null && !empire.Emperor.isRekt() && empire.Emperor.isAlive();
        items.Add((ruler ? "mandate_ruler_order" : "mandate_interregnum", ruler ? 10 : -20));
        if (empire.InFoundingGrace) items.Add(("mandate_founding_order", 10));
        else if (DynasticCycleSystem.GetAge(empire) >= 10)
            items.Add(("mandate_established_order", 5));
        double debtSince = empire.data.constitutional_economy?.bankrupt_since ?? -1d;
        int debtYears = debtSince >= 0d ? Date.getYearsSince(debtSince) : 0;
        int fiscal = MonarchyLegitimacyRules.Treasury(empire.CurrentMoney, debtYears);
        if (fiscal != 0) items.Add((fiscal > 0 ? "mandate_solvent_treasury" : "mandate_debt", fiscal));
        int corruption = MonarchyLegitimacyRules.Corruption(CorruptionSystem.GetRate(empire));
        if (corruption != 0) items.Add(("mandate_corruption", corruption));
        int burden = MonarchyLegitimacyRules.Misrule(HarshRuleSystem.OtherGovernanceBurden(empire));
        if (burden != 0) items.Add(("mandate_misrule", burden));
        int dynasty = MonarchyLegitimacyRules.Dynasty(empire.data.Mandate);
        if (dynasty != 0) items.Add(("mandate_dynastic_events", dynasty));
        int strain = DynasticCycleRules.LegitimacyPenalty(DynasticCycleSystem.GetPressure(empire));
        if (strain != 0) items.Add(("mandate_dynastic_strain", strain));
        // 以下读取年度快照(InstitutionSystem.UpdateSocialUnrest)，不在这里扫描城市
        var institutions = empire.data.institution_state;
        if (institutions != null)
        {
            // 地方治理：各城平均稳定度，50 为平，最多 +6 / -10
            if (institutions.avg_city_stability >= 0f)
            {
                int local = Mathf.Clamp(Mathf.RoundToInt((institutions.avg_city_stability - 50f) / 5f), -10, 6);
                if (local != 0) items.Add((local > 0 ? "mandate_local_order" : "mandate_local_disorder", local));
            }
            // 社会危机：阶层怨气超过六成开始损及正统，阶层起义进行中再 -5
            float grievance = institutions.class_grievances?.Values.DefaultIfEmpty(0f).Max() ?? 0f;
            int social = grievance > 60f ? -Mathf.Min(10, Mathf.RoundToInt((grievance - 60f) / 4f)) : 0;
            if (institutions.social_rebellion_war_id > 0) social -= 5;
            if (social != 0) items.Add(("mandate_social_crisis", social));
            // 欠饷欠俸：累计拖欠超过半年开支
            if (institutions.arrears_months >= 6f)
                items.Add(("mandate_arrears", -Mathf.Min(8, Mathf.RoundToInt(institutions.arrears_months / 3f))));
        }
        // 国债：债务超过一年经常收入损正统，一年内违约再损
        Kingdom core = empire.CoreKingdom;
        long debt = StateDebtSystem.Outstanding(core);
        if (debt > 0L)
        {
            long income = StateDebtSystem.AnnualStableIncome(core);
            if (income > 0L && debt > income) items.Add(("mandate_state_debt", -Mathf.Min(6, (int)(debt / income) * 2)));
            if (StateDebtSystem.DefaultedRecently(core)) items.Add(("mandate_debt_default", -5));
        }
        return items;
    }
}
