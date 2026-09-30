using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国民教育：以某理念为宪法国体(立国理念)的国家，按政府推行文化同化的办法在国内扩散该理念。
//   · 每年每座城推进一次，把城内 5% 的居民(至少一人)转为立国理念；
//   · 先教化年轻人；其他政党的党员与各级统治者不在此列，免得党籍与理念对不上；
//   · 城内该理念已达 55% 就停止推行，之后交给自然传播(与文化同化的"巩固阶段"一致)。
// 每年由 ConstitutionalEconomySystem 的年度结算调用；没有宪法的国家不推行。
public static class IdeologyEducationSystem
{
    private const float PushShare = 0.05f;
    private const float ConsolidatedShare = 0.55f;

    public static void Update(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived()) return;
        var clauses = ConstitutionSystem.GetClauses(empire);
        if (clauses == null) return;
        PartyIdeology ideology = clauses.founding_ideology;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities.ToList())
            {
                if (city == null || city.isRekt()) continue;
                try
                {
                    Educate(city, ideology);
                }
                catch (Exception exception)
                {
                    NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 国民教育失败: {exception.Message}");
                    return;
                }
            }
        }
    }

    private static void Educate(City city, PartyIdeology ideology)
    {
        Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetCityCounts(city);
        int total = counts.Values.Sum();
        if (total == 0) return;
        float share = counts.TryGetValue(ideology, out int own) ? (float)own / total : 0f;
        if (share >= ConsolidatedShare) return;
        int quota = Math.Max(1, Mathf.RoundToInt(total * PushShare));
        List<Actor> pupils = city.units?
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && !actor.isKing() &&
                            !actor.isCityLeader() && IdeologyPopulationSystem.Get(actor) != ideology &&
                            actor.GetFaction()?.IsParty != true)
            .OrderBy(actor => actor.getAge())
            .Take(quota).ToList() ?? new List<Actor>();
        if (pupils.Count == 0) return;
        foreach (Actor pupil in pupils) IdeologyPopulationSystem.Set(pupil, ideology);
        LayerCityCache.Invalidate(city);
    }
}
