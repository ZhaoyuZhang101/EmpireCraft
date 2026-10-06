using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 正统崩坏时革命滚雪球：帝国天命低于 SnowballMandate 时，与叛军接壤的帝国城市每年有机会倒戈投向叛军。
//   · 天命越低越猛(0 时满额)，忠诚越低的城越容易倒戈；
//   · 每年倒戈城数与叛军现有规模成正比——叛军越大涨得越快；
//   · 每座城倒戈再扣 1 点天命，形成正反馈；新得的城就地武装义军(RebellionStartupService)。
// 帝国核心王国的首都不会这样倒戈，只能打下来。军队也会倒戈(DefectSoldiers)，同样滚雪球。
// 每年由 ConstitutionalEconomySystem 的年度结算调用。
public static class RebellionSnowballSystem
{
    private const int SnowballMandate = 40;
    private const float DefectChance = 0.25f;
    private const float GrowthPerYear = 0.5f;

    private static readonly HashSet<EmpireWarType> RebellionTypes = new()
    {
        EmpireWarType.地方叛乱, EmpireWarType.派系叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱,
        EmpireWarType.地方独立
    };

    public static void Update(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt() || empire.Legitimacy >= SnowballMandate) return;
        float intensity = Mathf.Clamp01((SnowballMandate - empire.Legitimacy) / (float)SnowballMandate);
        foreach (Kingdom rebel in FindRebels(empire, core).ToList())
        {
            try
            {
                Snowball(empire, core, rebel, intensity);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 革命滚雪球失败: {exception.Message}");
            }
        }
    }

    // 正在与帝国打叛乱/革命战争的叛军(进攻方主帅)
    internal static IEnumerable<Kingdom> FindRebels(Empire empire, Kingdom core) => core.getWars()
        .Where(war => war != null && !war.hasEnded() && war.getMainDefender() != null &&
                      war.getMainDefender().GetEmpire() == empire &&
                      (RebellionTypes.Contains(war.GetEmpireWarType()) || war.getAsset() == WarTypeLibrary.rebellion))
        .Select(war => war.getMainAttacker())
        .Where(rebel => rebel != null && !rebel.isRekt() && rebel.GetEmpire() != empire)
        .Distinct();

    private static void Snowball(Empire empire, Kingdom core, Kingdom rebel, float intensity)
    {
        List<City> rebelCities = rebel.cities?.Where(city => city != null && !city.isRekt()).ToList() ?? new List<City>();
        if (rebelCities.Count == 0) return;
        int budget = Math.Max(1, Mathf.CeilToInt(rebelCities.Count * GrowthPerYear * intensity));
        List<City> frontier = rebelCities
            .SelectMany(city => (IEnumerable<City>)city.neighbours_cities ?? Enumerable.Empty<City>())
            .Where(city => city != null && !city.isRekt() && city.kingdom != null && city.kingdom.GetEmpire() == empire &&
                           city != core.capital)
            .Distinct().OrderBy(_ => UnityEngine.Random.value).ToList();
        var defected = new List<string>();
        foreach (City city in frontier)
        {
            if (defected.Count >= budget) break;
            float loyaltyFactor = 1f + Mathf.Max(0f, 50f - city.getLoyalty()) / 50f;
            if (UnityEngine.Random.value >= DefectChance * intensity * loyaltyFactor) continue;
            string name = city.GetCityName();
            city.joinAnotherKingdom(rebel, pCaptured: true, pRebellion: true);
            if (city.kingdom != rebel) continue;
            defected.Add(name);
            empire.AddMandate(-1);
        }
        if (defected.Count > 0)
        {
            RebellionStartupService.RaiseUprisingMilitia(rebel, intensity);
            EventRecorder.Record(empire, actor: rebel.king, logKingdom: rebel, text: string.Format(LM.Get("rebellion_snowball_history"), string.Join("、", defected),
                rebel.GetKingdomFullName(), empire.GetEmpireFullName()));
        }
        DefectSoldiers(empire, rebel, intensity, new HashSet<City>(frontier));
    }

    // 军队倒戈也滚雪球：叛军在双方兵力里占比越大，倒戈的官兵越多。
    // 先是认同叛军理念的，其次是驻在与叛军接壤城市的，最后才是其他人。每 10 人倒戈再扣 1 点天命。
    private const float ArmyDefectBase = 0.03f;
    private const float ArmyDefectMomentum = 0.25f;
    private const float ArmyDefectMax = 0.2f;

    private static void DefectSoldiers(Empire empire, Kingdom rebel, float intensity, HashSet<City> frontier)
    {
        if (rebel.capital == null || rebel.capital.isRekt()) return;
        List<Actor> loyal = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt())
            .SelectMany(kingdom => kingdom.units ?? new List<Actor>())
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isWarrior() && !actor.isKing())
            .ToList();
        if (loyal.Count == 0) return;
        int rebelSoldiers = rebel.units?.Count(actor => actor != null && actor.isAlive() && actor.isWarrior()) ?? 0;
        float balance = rebelSoldiers / (float)(rebelSoldiers + loyal.Count);
        float fraction = Mathf.Min(ArmyDefectMax, intensity * (ArmyDefectBase + ArmyDefectMomentum * balance));
        int count = Mathf.RoundToInt(loyal.Count * fraction);
        if (count <= 0) return;
        PartyIdeology rebelIdeology = IdeologyFamilies.StateIdeology(rebel);
        List<Actor> defectors = loyal
            .OrderBy(actor => IdeologyPopulationSystem.Get(actor) == rebelIdeology ? 0 : 1)
            .ThenBy(actor => actor.city != null && frontier.Contains(actor.city) ? 0 : 1)
            .ThenBy(_ => UnityEngine.Random.value)
            .Take(count).ToList();
        int moved = defectors.GroupBy(actor => actor.kingdom)
            .Sum(group => InstitutionSystem.TransferDefectingSoldiers(group.Key, rebel, group));
        if (moved <= 0) return;
        empire.AddMandate(-Math.Max(1, moved / 10));
        EventRecorder.Record(empire, actor: rebel.king, logKingdom: rebel, text: string.Format(LM.Get("rebellion_army_defection_history"), moved,
            empire.GetEmpireFullName(), rebel.GetKingdomFullName()));
    }

}
