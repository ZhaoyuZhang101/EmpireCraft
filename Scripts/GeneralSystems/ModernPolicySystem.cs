using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 现代议题立下的国策(用户 2026-11-24 第四批"政党换上现代议题")：产业归属与国民教育。
//   · 产业归属(-1 私有化 / 0 官民并举 / 1 国有化，见诉求"国有化""私有化")：
//       国有化——各厂区收归官营(利润按官营上缴，见 EnterpriseSystem)，新办产业国库先出资，财阀一并收归国有；
//                工厂产值少一成(官办不讲效率)，景气涨得慢；社会主义国家国有化以后是统制经济，不随行情起落(见 EconomicCycleSystem)；
//       私有化——各厂区发卖给民间，国库按厂的档次得一笔，产值多半成，景气涨得快，财阀坐大也更快；
//   · 国民教育(0~2 档，见诉求"普及教育")：每年照同档福利一半的开支拨款，拨得出时本文化的研究点按各国城数加权多出来
//     (每档百分之六)，市民怨气低一点；拨不出就没有效果。
// 数值都是模组自己的规则。
public static class ModernPolicySystem
{
    public const int Privatized = -1, Mixed = 0, Nationalized = 1;
    public const int MaxEducation = 2;
    private const float ResearchPerLevel = 0.06f;
    private const int SalePerLevel = 20;

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;
    private static ConstitutionalEconomyState State(Kingdom kingdom) => kingdom?.GetEmpire()?.data?.constitutional_economy;

    // 提得出现代议题的国家：工业时代以后(穿越政权自成一套)
    public static bool Industrial(Empire empire) =>
        empire?.CoreKingdom != null && !empire.isRekt() && State(empire) != null && !TransmigrantSystem.Governs(empire) &&
        TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire)) >= EconomicCycleSystem.IndustrialEra;

    public static int Nationalization(Empire empire) => State(empire)?.nationalization ?? Mixed;

    public static string NationalizationName(int level) =>
        LM.Get(level >= Nationalized ? "modern_policy_nationalized" : level <= Privatized ? "modern_policy_privatized" : "modern_policy_mixed");

    // 改产业归属一档(toward = 1 往国有化，-1 往私有化)：返回这回发卖得的钱(国有化为 0)
    public static int ShiftOwnership(Empire empire, int toward)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || empire.CoreKingdom == null) return 0;
        int next = Mathf.Clamp(state.nationalization + (toward > 0 ? 1 : -1), Privatized, Nationalized);
        if (next == state.nationalization) return 0;
        state.nationalization = next;
        int proceeds = 0;
        if (next == Mixed) return 0;
        bool nationalize = next == Nationalized;
        foreach (City city in ClaimPolicySystem.Cities(empire).ToList())
            foreach (FactorySite site in FactorySystem.InCity(city))
            {
                if (site.state == nationalize) continue;
                site.state = nationalize;
                if (!nationalize) proceeds += site.units.Sum(unit => Math.Max(1, unit.level)) * SalePerLevel;
            }
        if (nationalize)
        {
            ConglomerateSystem.Nationalize(empire);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, 15f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, -8f);
        }
        else
        {
            if (proceeds > 0) empire.CoreKingdom.AddMoney(proceeds, TreasuryCategory.PublicEnterprise);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Merchant, -10f);
            ClaimPolicySystem.AddGrievance(empire, SocialClass.Labour, 8f);
        }
        return proceeds;
    }

    // 社会主义、共产主义立国的国家(看立国理念)到了工业时代即国有化：只办一回，不记怨气(建政时已成定局)；此后照国策走
    public static void EnsureFounding(Empire empire, ConstitutionalEconomyState state)
    {
        if (state == null || state.founding_nationalized || !Industrial(empire)) return;
        if (!IdeologyFamilies.IsSocialist(WarlordEraSystem.GovernmentIdeology(empire))) return;
        state.founding_nationalized = true;
        if (state.nationalization >= Nationalized) return;
        state.nationalization = Nationalized;
        foreach (City city in ClaimPolicySystem.Cities(empire).ToList())
            foreach (FactorySite site in FactorySystem.InCity(city)) site.state = true;
        ConglomerateSystem.Nationalize(empire);
    }

    // 新办产业谁先出资(见 EnterpriseSystem.StatePreferred)：定了国有化、私有化的照国策，没定返回 null
    public static bool? StatePreferred(Kingdom kingdom) => State(kingdom)?.nationalization switch
    {
        Nationalized => true,
        Privatized => false,
        _ => null
    };

    // 工厂产值(见 FactorySystem.Produce)
    public static float OutputFactor(City city) => State(city?.kingdom)?.nationalization switch
    {
        Nationalized => 0.9f,
        Privatized => 1.05f,
        _ => 1f
    };

    #region 国民教育

    public static int EducationCost(Empire empire, int level) =>
        level <= 0 ? 0 : Math.Max(5, ConstitutionalEconomySystem.GetWelfareAnnualCost(empire, level) / 2);

    public static bool RaiseEducation(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || state.education_level >= MaxEducation) return false;
        state.education_level++;
        return true;
    }

    // 每年(ConstitutionalEconomySystem)：教育拨款
    public static void UpdateYear(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire?.CoreKingdom == null || state == null) return;
        int cost = EducationCost(empire, state.education_level);
        state.education_funded = cost > 0 && empire.CoreKingdom.GetMoney() >= cost;
        if (state.education_funded) empire.CoreKingdom.SubMoney(cost, TreasuryCategory.Research);
    }

    // 本文化研究点的加成(见 TechnologySystem)：各国教育档按城数加权
    public static float ResearchBonus(List<City> cities)
    {
        if (cities == null || cities.Count == 0) return 0f;
        float total = 0f, space = 0f;
        foreach (City city in cities)
        {
            if (State(city?.kingdom) is { education_funded: true } state) total += state.education_level;
            // 航天竞赛每走完一步再多一点(见 SpaceRaceSystem)
            space += SpaceRaceSystem.Level(city?.kingdom?.GetEmpire());
        }
        return total / cities.Count * ResearchPerLevel + space / cities.Count * SpaceRaceSystem.ResearchPerLevel;
    }

    #endregion

    // 阶层怨气的目标值加减(见 InstitutionSystem)
    public static float GrievanceShift(Empire empire, SocialClass socialClass)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null) return 0f;
        float shift = 0f;
        if (socialClass == SocialClass.Merchant) shift += state.nationalization >= Nationalized ? 8f : state.nationalization <= Privatized ? -4f : 0f;
        if (socialClass == SocialClass.Labour) shift += state.nationalization >= Nationalized ? -3f : state.nationalization <= Privatized ? 3f : 0f;
        if (socialClass == SocialClass.Citizen && state.education_funded) shift -= 2f * state.education_level;
        return shift;
    }
}
