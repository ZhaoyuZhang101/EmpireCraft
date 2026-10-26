using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 连年饥荒(饥荒的判定见 CityPopulationSystem.GetGrowthFactors)：以前饥荒只让人口减少、城市稳定低一些，
// 多少年大面积饥荒朝廷也照样稳如泰山。现在：
//   · 每城记连荒几年(CityExtraData.famine_years)：连荒两年起，每年稳定再降 4×(年数-1)，至多 20；
//   · 饥民起义：连荒两年以上的城(都城除外)每年有 8%×(年数-1) 的机会揭竿而起(至多四成)，
//     走农民起义的流程(见 LandEconomySystem.StartFamineRebellion)，打仗时照样会起；
//   · 大饥荒：一国(帝国按全体成员)三成以上的城连荒两年以上，记入史书，每年天命正统 -3，
//     王朝积弊按连荒城的比例加重(见 StrainDelta、DynasticCycleSystem)；饥荒退到一成以下再记一笔。
// 灾年饥民(用户 2026-10-26："赈灾不足生成饥民军。灾荒叛乱接壤的联合叛乱，不接壤各自起事。
// 饥民军规模 = 基础 × 民怨系数；民怨 40~80 起事处数 ×2，80~100 ×3")：
//   · 一国一年起事的处数按民怨(PopularGrievance.Index)：<40 一处、40~80 两处、≥80 三处；
//   · 受中灾、大灾(见 DisasterSystem，灾后一年内也算)、开仓赈灾后仍在饥荒的城，连荒满一年就可能起事，
//     机会 12%×连荒年数 + 8%(大灾再 +5%)，至多六成；与土地起义共用的冷却减为五年；
//   · 饥民军烈度 × 民怨系数 0.8 + 民怨/100(见 FervorFactor)，仍受每城义军上限；
//   · 合流：起事城接壤的同国连荒城(原版的相邻城)成片并入同一支饥民军，不接壤的各自起事；
//     以后每年土地经济结算时，连荒的城也会投奔接壤的饥民军(见 LandEconomySystem.JoinFamineRebellion)；
//   · 一国一年卷入饥民起义的城(起事加入伙)至多本国城数的两成、至少一座、至多六座(见 RoomLeft)，
//     免得与苛政民变波次、革命滚雪球的倒戈叠成死亡螺旋。
// 每月顺带推进灾年(见 DisasterSystem.Monthly)。
public static class FamineUnrestSystem
{
    private const int ProlongedYears = 2;
    private const float GreatFamineShare = 0.3f, RecoveredShare = 0.1f;
    private const float UprisingPerYear = 0.08f, UprisingCap = 0.4f;
    private const float DisasterUprisingPerYear = 0.12f, DisasterUprisingBase = 0.08f, MajorDisasterBonus = 0.05f,
        DisasterUprisingCap = 0.6f;
    private const float MaxRebelShare = 0.2f;
    private const int MaxRebelCities = 6;

    private static double _lastMonth = -1d;
    private static int _months;
    private static object _world;
    // 本年各国卷入饥民起义的城数(按原国，不存档；读档后从零算)
    private static readonly Dictionary<long, (double since, int cities)> RebelCities = new();

    public static void Tick()
    {
        if (World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastMonth = -1d;
            _months = 0;
            RebelCities.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        bool first = _lastMonth < 0d;
        _lastMonth = now;
        if (first) return;
        try { DisasterSystem.Monthly(); }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][灾年] 结算失败: {exception.Message}");
        }
        if (++_months % 12 != 0) return;
        try { Yearly(); }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][饥荒] 结算失败: {exception.Message}");
        }
    }

    public static bool InFamine(City city)
    {
        if (city?.data == null || city.isRekt()) return false;
        if (CityPopulationSystem.AbstractPopulationEnabled) return CityPopulationSystem.GetGrowthFactors(city).Famine;
        int households = CityPopulationSystem.FiscalHouseholds(city);
        return households > 0 && city.getTotalFood() < households * 0.5f;
    }

    // 王朝积弊：帝国里连荒的城越多加得越多(每年)
    public static float StrainDelta(Empire empire)
    {
        float share = empire?.data?.famine_share ?? 0f;
        return share >= 0.2f ? share * 8f : 0f;
    }

    // 饥民军烈度的民怨系数(用户 2026-10-26 "饥民军规模 = 基础 × 民怨系数")：0.8 + 民怨/100，即 0.8~1.8
    public static float FervorFactor(Kingdom kingdom) =>
        0.8f + Mathf.Clamp(PopularGrievance.Index(kingdom), 0f, 100f) / 100f;

    // 本国今年还能有几座城卷入饥民起义：上限按年初的城数(现有加上今年已经分出去的)算两成，至少一座、至多六座
    public static int RoomLeft(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || World.world == null) return 0;
        int used = 0;
        if (RebelCities.TryGetValue(kingdom.id, out var entry) && ThisYear(entry.since)) used = entry.cities;
        int cap = Mathf.Clamp(Mathf.CeilToInt(((kingdom.cities?.Count ?? 0) + used) * MaxRebelShare), 1, MaxRebelCities);
        return Mathf.Max(0, cap - used);
    }

    public static void CountRebelCities(Kingdom kingdom, int cities)
    {
        if (kingdom?.data == null || World.world == null || cities <= 0) return;
        if (!RebelCities.TryGetValue(kingdom.id, out var entry) || !ThisYear(entry.since))
            entry = (World.world.getCurWorldTime(), 0);
        RebelCities[kingdom.id] = (entry.since, entry.cities + cities);
    }

    private static bool ThisYear(double since)
    {
        double now = World.world.getCurWorldTime();
        return now >= since && Date.getYearsSince(since) < 1;
    }

    private static void Yearly()
    {
        var byEmpire = new Dictionary<Empire, (int prolonged, int total)>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.cities == null) continue;
            int prolonged = 0, total = 0;
            List<(City city, float chance, bool disaster)> candidates = null;
            foreach (City city in kingdom.cities.ToList())
            {
                if (city?.data == null || city.isRekt()) continue;
                total++;
                var data = city.GetOrCreate();
                data.famine_years = InFamine(city) ? data.famine_years + 1 : 0;
                if (data.famine_years <= 0) continue;
                // 受中灾、大灾，赈灾后仍在饥荒：连荒满一年就可能起事
                int level = DisasterSystem.RecentLevel(city);
                bool disaster = level >= DisasterSystem.Moderate;
                if (data.famine_years >= ProlongedYears)
                {
                    prolonged++;
                    CityStabilityData state = CityStabilitySystem.State(city);
                    if (state != null) state.stability = Mathf.Max(0f, state.stability - Mathf.Min(20f, 4f * (data.famine_years - 1)));
                }
                else if (!disaster) continue;
                if (city == kingdom.capital) continue;
                float chance = Mathf.Min(UprisingCap, UprisingPerYear * (data.famine_years - 1));
                if (disaster)
                    chance = Mathf.Max(chance, Mathf.Min(DisasterUprisingCap, DisasterUprisingPerYear * data.famine_years +
                        DisasterUprisingBase + (level >= DisasterSystem.Major ? MajorDisasterBonus : 0f)));
                if (chance > 0f) (candidates ??= new List<(City, float, bool)>()).Add((city, chance, disaster));
            }
            if (candidates != null) Uprisings(kingdom, candidates);
            Empire empire = kingdom.GetEmpire();
            if (empire?.data == null) Announce(kingdom, null, prolonged, total);
            else
            {
                byEmpire.TryGetValue(empire, out var sum);
                byEmpire[empire] = (sum.prolonged + prolonged, sum.total + total);
            }
        }
        foreach (var pair in byEmpire) Announce(pair.Key.CoreKingdom, pair.Key, pair.Value.prolonged, pair.Value.total);
    }

    // 一国一年的饥民起义：起事处数按民怨，次序打乱；起成一处就把接壤的连荒城成片拉进同一支饥民军
    private static void Uprisings(Kingdom kingdom, List<(City city, float chance, bool disaster)> candidates)
    {
        float grievance = PopularGrievance.Index(kingdom);
        int starts = grievance >= 80f ? 3 : grievance >= 40f ? 2 : 1;
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        foreach ((City city, float chance, bool disaster) in candidates)
        {
            if (starts <= 0 || RoomLeft(kingdom) <= 0) break;
            // 前一支饥民军合流时可能已经把这座城带走了
            if (city?.data == null || city.isRekt() || city.kingdom != kingdom || city == kingdom.capital) continue;
            if (UnityEngine.Random.value >= chance) continue;
            Kingdom rebel = LandEconomySystem.StartFamineRebellion(city, city.GetOrCreate().famine_years, disaster);
            if (rebel == null) continue;
            starts--;
            CountRebelCities(kingdom, 1);
            Rally(kingdom, rebel, city);
        }
    }

    // 合流：从起事城出发，沿相邻城一圈圈找本国连荒的城入伙(都城除外，到本年上限为止)
    private static void Rally(Kingdom origin, Kingdom rebel, City seat)
    {
        var seen = new HashSet<City> { seat };
        var frontier = new Queue<City>();
        frontier.Enqueue(seat);
        int joined = 0;
        while (frontier.Count > 0 && RoomLeft(origin) > 0)
        {
            City from = frontier.Dequeue();
            if (from?.neighbours_cities == null) continue;
            foreach (City next in from.neighbours_cities.ToList())
            {
                if (RoomLeft(origin) <= 0) break;
                if (next?.data == null || !seen.Add(next) || next.isRekt() || next.kingdom != origin || next == origin.capital ||
                    next.GetOrCreate().famine_years < 1) continue;
                if (!LandEconomySystem.JoinFamineRebellion(next, rebel, announce: false)) continue;
                CountRebelCities(origin, 1);
                joined++;
                frontier.Enqueue(next);
            }
        }
        if (joined <= 0 || rebel.isRekt()) return;
        string text = string.Format(LM.Get("famine_rebellion_rally_history"), seat.GetCityName(), joined, rebel.GetKingdomName());
        Empire empire = origin.GetEmpire();
        if (empire?.data != null && !empire.isRekt()) empire.RecordHistory(directContent: text);
        TranslateHelper.LogEventOnly(text, origin);
    }

    // 大饥荒：记入史书、正统下降；退去再记一笔
    private static void Announce(Kingdom kingdom, Empire empire, int prolonged, int total)
    {
        if (kingdom?.data == null || total <= 0) return;
        float share = (float)prolonged / total;
        if (empire?.data != null) empire.data.famine_share = share;
        var data = kingdom.GetOrCreate();
        string name = empire?.GetEmpireFullName() ?? kingdom.data.name;
        if (share >= GreatFamineShare)
        {
            empire?.AddMandate(-3);
            if (data.great_famine) return;
            data.great_famine = true;
            Record(kingdom, empire, string.Format(LM.Get("great_famine_history"), name, prolonged));
        }
        else if (share < RecoveredShare && data.great_famine)
        {
            data.great_famine = false;
            Record(kingdom, empire, string.Format(LM.Get("great_famine_end_history"), name), false);
        }
    }

    // 大饥荒报世界提示；饥荒退去只记入史书
    private static void Record(Kingdom kingdom, Empire empire, string text, bool announce = true)
    {
        if (empire?.data != null) empire.RecordHistory(directContent: text);
        if (announce) TranslateHelper.LogEventOnly(text, kingdom);
    }
}
