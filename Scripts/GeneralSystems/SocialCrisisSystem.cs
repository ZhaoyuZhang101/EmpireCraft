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

// 社会危机的四个量分开算(阶层怨气在 InstitutionSystem.UpdateSocialUnrest 每年结算)：
//   怨气(不满)：制度损益 + 生活压力(重税、饥荒、无地、贫困) - 纾解(轻税、善治、福利)
//   组织动员：怨气超过一半后逐年积累，正统越低积累越快；驻军镇压只压动员，不消除怨气
//   稳定度：各城自己的治理与驻军(CityStabilitySystem)，决定哪座城能起事
//   爆发：怨气达到起义线且动员达到 MobilizationThreshold 才起事，不再掷正统骰子
// 没有爆发时记下原因，界面显示"为什么还没反"。
public static class SocialCrisisSystem
{
    public const float MobilizationThreshold = 60f;
    private const int SampleCities = 40;

    // ---- 生活压力 / 纾解：返回加到怨气目标上的值(正为加怨，负为纾解)与主因 ----
    public readonly struct Material
    {
        public readonly float Pressure, Relief;
        public readonly string Cause;
        public Material(float pressure, float relief, string cause)
        {
            Pressure = pressure;
            Relief = relief;
            Cause = cause;
        }
    }

    public sealed class Snapshot
    {
        public float Tax, Famine, Landless, Poverty, Governance, Suppression, Stability = -1f, ArrearsMonths;
    }

    // 每年每个帝国算一次全境快照(抽样不超过 SampleCities 座城)
    public static Snapshot Capture(Empire empire)
    {
        var snapshot = new Snapshot();
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt()) return snapshot;
        snapshot.Tax = Mathf.Clamp(((float)core.GetTaxRate() - 0.25f) * 100f, -10f, 50f);
        snapshot.Landless = Mathf.Min(1f, LandEconomySystem.GetRealmLandlessRatio(core));
        var cities = new List<City>();
        foreach (Kingdom kingdom in empire.kingdoms_list)
            if (kingdom?.cities != null)
                foreach (City city in kingdom.cities)
                    if (city != null && !city.isRekt()) cities.Add(city);
        if (cities.Count == 0) return snapshot;
        int step = Math.Max(1, cities.Count / SampleCities);
        int sampled = 0, famine = 0, poor = 0, stable = 0;
        float governance = 0f, suppression = 0f, stability = 0f;
        long arrears = 0, monthly = 0;
        for (int i = 0; i < cities.Count; i += step)
        {
            City city = cities[i];
            sampled++;
            if (CityPopulationSystem.AbstractPopulationEnabled)
            {
                CityPopulationSystem.GrowthFactors factors = CityPopulationSystem.GetGrowthFactors(city);
                if (factors.Famine) famine++;
                if (factors.Prosperity < 0.5f) poor++;
            }
            else if (city.getTotalFood() < CityPopulationSystem.Households(city) * 0.5f) famine++;
            CityStabilityData state = city.GetOrCreate().stability;
            if (state != null && state.owner_id == city.kingdom?.id)
            {
                governance += state.governance_quality;
                suppression += (state.withdrawn ? 0f : state.suppression) + state.fear;
                stability += state.stability;
                stable++;
                arrears += Math.Max(0L, state.arrears);
                monthly += Math.Max(0, state.governance_due) + Math.Max(0, state.garrison_due) + Math.Max(0, state.military_due);
            }
        }
        snapshot.Famine = famine / (float)sampled;
        snapshot.Poverty = poor / (float)sampled;
        snapshot.Governance = governance / sampled;
        snapshot.Suppression = suppression / sampled;
        snapshot.Stability = stable > 0 ? stability / stable : -1f;
        snapshot.ArrearsMonths = monthly > 0 ? (float)(arrears / (double)monthly) : 0f;
        return snapshot;
    }

    public static Material For(Snapshot s, SocialClass socialClass)
    {
        bool commoner = socialClass is SocialClass.Peasant or SocialClass.Labour;
        bool taxed = commoner || socialClass is SocialClass.Merchant or SocialClass.Citizen;
        var items = new List<(string key, float value)>();
        if (taxed && s.Tax > 0f) items.Add(("tax", s.Tax * 0.5f));
        if (commoner && s.Famine > 0.05f) items.Add(("famine", 30f * s.Famine));
        if (socialClass == SocialClass.Peasant && s.Landless > 0.1f) items.Add(("landless", 40f * (s.Landless - 0.1f)));
        if (commoner && s.Poverty > 0.1f) items.Add(("poverty", 20f * s.Poverty));
        float pressure = items.Sum(item => item.value);
        float relief = 0f;
        if (taxed && s.Tax < 0f) relief += -s.Tax * 0.5f;
        // 善治：各城治理质量平均(0~100)越高越能纾解，最多 -12
        relief += Mathf.Clamp(s.Governance, 0f, 100f) * 0.12f;
        string cause = items.Count == 0 ? "" : "material:" + items.OrderByDescending(item => item.value).First().key;
        return new Material(pressure, relief, cause);
    }

    // ---- 组织动员 ----
    public static float UpdateMobilization(InstitutionEmpireState state, SocialClass socialClass, float grievance,
        int legitimacy, float suppression)
    {
        state.class_mobilization ??= new Dictionary<SocialClass, float>();
        state.class_mobilization.TryGetValue(socialClass, out float mobilization);
        float legitimacyFactor = legitimacy < 0 ? 1f : 1.5f - Mathf.Clamp(legitimacy, 0, 100) / 100f;
        float change = grievance >= 50f ? (grievance - 50f) * 0.6f * legitimacyFactor : -15f;
        // 驻军镇压(每城最多约 20 + 恐惧)：只压动员
        change -= suppression * 0.5f;
        mobilization = Mathf.Clamp(mobilization + change, 0f, 100f);
        state.class_mobilization[socialClass] = mobilization;
        return mobilization;
    }

    public static float GetMobilization(Empire empire, SocialClass socialClass)
    {
        var state = empire?.data?.institution_state;
        return state?.class_mobilization != null && state.class_mobilization.TryGetValue(socialClass, out float value) ? value : 0f;
    }

    public static float GetSuppression(Empire empire) => empire?.data?.institution_state?.social_suppression ?? 0f;

    public static string QuietReason(Empire empire)
    {
        string key = empire?.data?.institution_state?.social_quiet_reason;
        return string.IsNullOrEmpty(key) ? "" : LM.Get("social_quiet_" + key);
    }

    // 无小人模式没有该阶层的实体人物时，从该阶层人数最多、能起事的非首都城市的虚拟人口里推举一人
    public static Actor SpawnClassLeader(IEnumerable<Kingdom> kingdoms, SocialClass socialClass)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return null;
        City best = null;
        float bestCount = 1f;
        foreach (Kingdom kingdom in kingdoms)
        {
            if (kingdom?.cities == null || kingdom.isRekt() || !RebellionSystem.CanAttempt(kingdom)) continue;
            foreach (City city in kingdom.cities)
            {
                if (city == null || city.isRekt() || city == kingdom.capital || !CityStabilitySystem.CanRise(city)) continue;
                float count = CityPopulationSystem.BackgroundOfClass(city, socialClass);
                if (count <= bestCount) continue;
                best = city;
                bestCount = count;
            }
        }
        return best == null ? null : CityPopulationSystem.SpawnCivilianOfClass(best, socialClass);
    }
}
