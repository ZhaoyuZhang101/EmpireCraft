using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 诉求立下的长期国策(存在 ConstitutionalEconomyState)，每年结算一次：
//   · 移民：开放移民 → 邻国比本地穷的城市每年有一部分平民迁入边城(带着原来的文化)；
//           清除移民 → 非主体文化的平民每年被逐出一部分，送回同文化的邻国，没有就流散；
//   · 金融：拓展金融霸权 → 城市公营贸易收入 +15%，商人得利、劳工农民不满；缩减 → 收入 -10%，反之；
//   · 革命：输出革命 → 邻国怨气上升、边城平民转向革命理念(诉求施行时一次性结算，见 ExportRevolution)。
public static class ClaimPolicySystem
{
    private const float OpenInflowShare = 0.015f;
    private const float OpenInflowCap = 25f;
    private const float ExpelShare = 0.03f;

    private static ConstitutionalEconomyState StateOf(Empire empire) => empire?.data?.constitutional_economy;

    public static int MigrationPolicy(Empire empire) => StateOf(empire)?.migration_policy ?? 0;
    public static int FinancePolicy(Empire empire) => StateOf(empire)?.finance_policy ?? 0;

    public static void SetMigrationPolicy(Empire empire, int level)
    {
        ConstitutionalEconomyState state = StateOf(empire);
        if (state != null) state.migration_policy = Math.Max(-1, Math.Min(1, level));
    }

    public static void SetFinancePolicy(Empire empire, int level)
    {
        ConstitutionalEconomyState state = StateOf(empire);
        if (state != null) state.finance_policy = Math.Max(-1, Math.Min(1, level));
    }

    public static string MigrationPolicyText(int level) =>
        LM.Get(level > 0 ? "migration_policy_open" : level < 0 ? "migration_policy_closed" : "migration_policy_normal");

    public static string FinancePolicyText(int level) =>
        LM.Get(level > 0 ? "finance_policy_hegemony" : level < 0 ? "finance_policy_curbed" : "finance_policy_normal");

    // 城市公营贸易收入的倍率(MarketSystem 卖方入账时乘)
    public static float TradeIncomeFactor(Kingdom kingdom)
    {
        int level = FinancePolicy(kingdom?.GetEmpire());
        return level > 0 ? 1.15f : level < 0 ? 0.9f : 1f;
    }

    public static void UpdateYear(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire == null || state == null) return;
        if (state.migration_policy > 0) OpenBorders(empire);
        else if (state.migration_policy < 0) ExpelForeigners(empire);
        if (state.finance_policy > 0)
        {
            AddGrievance(empire, SocialClass.Merchant, -2f);
            AddGrievance(empire, SocialClass.Labour, 1f);
            AddGrievance(empire, SocialClass.Peasant, 1f);
        }
        else if (state.finance_policy < 0)
        {
            AddGrievance(empire, SocialClass.Merchant, 2f);
            AddGrievance(empire, SocialClass.Labour, -1f);
        }
    }

    public static IEnumerable<City> Cities(Empire empire) =>
        empire?.kingdoms_list == null ? Enumerable.Empty<City>()
            : empire.kingdoms_list.Where(kingdom => kingdom?.cities != null && !kingdom.isRekt())
                .SelectMany(kingdom => kingdom.cities).Where(city => city?.data != null && !city.isRekt());

    // 与外国接壤的城市：本国城市 → 相邻的外国城市
    public static IEnumerable<(City own, City foreign)> ForeignBorders(Empire empire)
    {
        foreach (City city in Cities(empire).ToList())
        {
            if (city.neighbours_cities == null) continue;
            foreach (City neighbour in city.neighbours_cities)
            {
                if (neighbour?.data == null || neighbour.isRekt() || neighbour.kingdom == null) continue;
                if (neighbour.kingdom.GetEmpire() == empire) continue;
                yield return (city, neighbour);
            }
        }
    }

    public static bool HasForeignBorder(Empire empire) => ForeignBorders(empire).Any();

    private static void OpenBorders(Empire empire)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        float moved = 0f;
        foreach ((City own, City foreign) in ForeignBorders(empire).ToList())
        {
            // 人往高处走：只从不比本城富裕的邻国城市迁入
            if (IdeologyPopulationSystem.CityProsperity(foreign) > IdeologyPopulationSystem.CityProsperity(own) + 0.1f)
                continue;
            float amount = Mathf.Min(CityPopulationSystem.CivilianBackground(foreign) * OpenInflowShare, OpenInflowCap);
            if (amount < 1f) continue;
            float transferred = CityPopulationSystem.TransferCivilianBackground(foreign, own, amount);
            moved += transferred;
            // 被抽走人口的邻国记下这笔(见 MigrationRelations)
            if (transferred > 0f) MigrationRelations.Drained(foreign.kingdom, empire.CoreKingdom);
        }
        // 外来人口压低工钱：劳工不满，商人得利
        if (moved > 0f)
        {
            AddGrievance(empire, SocialClass.Labour, 1f);
            AddGrievance(empire, SocialClass.Merchant, -1f);
        }
    }

    private static void ExpelForeigners(Empire empire)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        // 谁算外人按族类，照风俗文化(见 InstitutionSystem.GetCustomsCulture)
        string main = InstitutionSystem.GetCustomsCulture(empire);
        if (!CultureService.IsValidCulture(main)) return;
        // 统治民族(征服王朝的族人)与同一族类的不算外人(以前元朝式帝国会把自家族人赶走)
        string ruling = CompositeEmpireService.GetRulingCulture(empire);
        bool expelled = false;
        foreach (City city in Cities(empire).ToList())
        {
            CityPopulationData data = CityPopulationSystem.Get(city);
            if (data?.groups == null) continue;
            List<PopGroup> foreigners = data.groups.Where(group => group != null && group.Background > 0f &&
                    group.social_class != SocialClass.Army && group.social_class != SocialClass.Officer &&
                    !string.IsNullOrEmpty(group.culture) && !CultureService.SameKin(group.culture, main) &&
                    !CultureService.SameKin(group.culture, ruling)).ToList();
            if (foreigners.Count == 0) continue;
            List<City> abroad = city.neighbours_cities?.Where(other => other?.data != null && !other.isRekt() &&
                other.kingdom != null && other.kingdom.GetEmpire() != empire).ToList() ?? new List<City>();
            PopulationParallelSystem.Discard(city);
            foreach (PopGroup group in foreigners)
            {
                float removed = CityPopulationSystem.RemoveBackground(group, group.Background * ExpelShare);
                if (removed <= 0f) continue;
                expelled = true;
                // 送回同文化的邻国，没有就涌入接壤的外国城做难民(见 MigrationRelations)
                City home = abroad.FirstOrDefault(other => CultureService.GetRealmCulture(other.kingdom) == group.culture);
                City refuge = home ?? abroad.FirstOrDefault();
                if (refuge == null) continue;
                PopulationParallelSystem.Discard(refuge);
                CityPopulationSystem.AddBackground(refuge, group.social_class, group.culture, group.species,
                    group.ideology, removed);
                if (home == null) MigrationRelations.TakeRefugees(refuge);
                MigrationRelations.Expelled(refuge.kingdom, empire.CoreKingdom);
            }
        }
        if (!expelled) return;
        CityPopulationSystem.InvalidateHouseholdCaches();
        AddGrievance(empire, SocialClass.Merchant, 1f);
        AddGrievance(empire, SocialClass.Labour, -1f);
    }

    // 输出革命：邻国(另一个帝国或独立王国)边城平民转向革命理念，所属帝国劳工农民怨气上升。返回受影响的国家
    public static Kingdom ExportRevolution(Empire empire, Kingdom target, PartyIdeology ideology)
    {
        if (empire == null || target == null || target.isRekt()) return null;
        List<City> cities = ForeignBorders(empire).Where(pair => pair.foreign.kingdom == target)
            .Select(pair => pair.foreign).Distinct().Take(3).ToList();
        if (cities.Count == 0) cities = target.cities?.Where(city => city != null && !city.isRekt()).Take(1).ToList()
                                        ?? new List<City>();
        foreach (City city in cities)
            CityPopulationSystem.ConvertBackground(city, ideology, socialClass =>
                socialClass is SocialClass.Labour or SocialClass.Peasant ? 0.06f : 0.015f);
        Empire targetEmpire = target.GetEmpire();
        if (targetEmpire != null && targetEmpire != empire)
        {
            AddGrievance(targetEmpire, SocialClass.Labour, 12f);
            AddGrievance(targetEmpire, SocialClass.Peasant, 8f);
        }
        return target;
    }

    public static void AddGrievance(Empire empire, SocialClass socialClass, float delta)
    {
        InstitutionEmpireState institutions = empire?.data?.institution_state;
        if (institutions == null || delta == 0f) return;
        institutions.class_grievances ??= new Dictionary<SocialClass, float>();
        institutions.class_grievances.TryGetValue(socialClass, out float current);
        institutions.class_grievances[socialClass] = Mathf.Clamp(current + delta, 0f, 100f);
    }
}
