using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国民教育：以某理念为宪法国体(立国理念)的国家，按政府推行文化同化的办法在国内扩散该理念。
// 力度由宪法的"意识形态强度"决定，三档各有利弊(外部侵蚀与民众反弹见 PublicOpinionSystem)：
//   · 高：每年每城转化 10% 居民，直到该理念占 75%；外部思潮压力减半，不易被颠覆；
//         但负面事件(罢工、示威、民怨)的反弹 ×1.5，容易反噬政权；
//   · 中：每年每城转化 5%，到 55% 为止，交给自然传播；外部侵蚀与反弹都照常；
//   · 低：不推广立国理念；外部思潮压力 ×1.5，容易被侵蚀；但负面事件的反弹只有 ×0.6。
// 先教化年轻人；其他政党的党员与各级统治者不在此列，免得党籍与理念对不上。
// 每年由 ConstitutionalEconomySystem 的年度结算调用；没有宪法的国家不推行。
public static class IdeologyEducationSystem
{
    public static void Update(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.isRekt() || empire.IsArchived()) return;
        var clauses = ConstitutionSystem.GetClauses(empire);
        if (clauses == null) return;
        ConstitutionIdeologyIntensity intensity = clauses.ideology_intensity;
        if (intensity == ConstitutionIdeologyIntensity.Low) return;
        float push = intensity == ConstitutionIdeologyIntensity.High ? 0.10f : 0.05f;
        // 意识形态疲劳(见 IdeologyDynamicsSystem)：宣传越空洞越没人听，满疲劳时只剩三成效果
        push *= 1f - 0.7f * IdeologyDynamicsSystem.GetFatigue(empire) / 100f;
        float consolidated = intensity == ConstitutionIdeologyIntensity.High ? 0.75f : 0.55f;
        PartyIdeology ideology = clauses.founding_ideology;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.cities == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities.ToList())
            {
                if (city == null || city.isRekt()) continue;
                try
                {
                    Educate(city, ideology, push, consolidated);
                }
                catch (Exception exception)
                {
                    NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 国民教育失败: {exception.Message}");
                    return;
                }
            }
        }
    }

    private static void Educate(City city, PartyIdeology ideology, float push, float consolidated)
    {
        Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetCityCounts(city);
        int total = counts.Values.Sum();
        if (total == 0) return;
        float share = counts.TryGetValue(ideology, out int own) ? (float)own / total : 0f;
        if (share >= consolidated) return;
        int quota = Math.Max(1, Mathf.RoundToInt(total * push));
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
