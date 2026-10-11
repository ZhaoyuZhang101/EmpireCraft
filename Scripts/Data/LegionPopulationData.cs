using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.Data;

// 只记录军团中代表士兵以外的人员；代表士兵本人仍由 Actor 承载。
public sealed class LegionPopulationContribution
{
    public long city_id = -1L;
    public SocialClass social_class = SocialClass.Peasant;
    public string culture = "";
    public string species = "";
    public PartyIdeology ideology = PartyIdeology.Conservatism;
    public float size;
}

public static class LegionPopulationRules
{
    public static void Add(List<LegionPopulationContribution> origins, long cityId, PopGroup group, float amount)
    {
        if (origins == null || group == null || amount <= 0f) return;
        foreach (LegionPopulationContribution origin in origins)
        {
            if (origin == null) continue;
            if (origin.city_id != cityId || origin.social_class != group.social_class ||
                origin.culture != (group.culture ?? "") || origin.species != (group.species ?? "") ||
                origin.ideology != group.ideology) continue;
            origin.size += amount;
            return;
        }
        origins.Add(new LegionPopulationContribution
        {
            city_id = cityId, social_class = group.social_class, culture = group.culture ?? "",
            species = group.species ?? "", ideology = group.ideology, size = amount
        });
    }

    public static float Total(List<LegionPopulationContribution> origins)
    {
        float total = 0f;
        if (origins != null)
            foreach (LegionPopulationContribution origin in origins)
                if (origin != null && origin.size > 0f) total += origin.size;
        return total;
    }

    // 减员只改变人数，不改变来源、文化、物种和理念。各组按同一比例承担损失。
    public static void ReduceTo(List<LegionPopulationContribution> origins, float survivors)
    {
        float total = Total(origins);
        if (total <= 0f || survivors >= total) return;
        float ratio = Math.Max(0f, survivors) / total;
        foreach (LegionPopulationContribution origin in origins)
            if (origin != null) origin.size *= ratio;
        origins.RemoveAll(origin => origin == null || origin.size <= 0f);
    }
}
