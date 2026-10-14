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
//   · 饥民起义：连荒两年以上的城(都城除外)每年有 8%×(年数-1) 的机会揭竿而起(至多四成，一国一年至多一处)，
//     走农民起义的流程(见 LandEconomySystem.TryStartFamineRebellion)，打仗时照样会起；
//   · 大饥荒：一国(帝国按全体成员)三成以上的城连荒两年以上，记入史书，每年天命正统 -3，
//     王朝积弊按连荒城的比例加重(见 StrainDelta、DynasticCycleSystem)；饥荒退到一成以下再记一笔。
public static class FamineUnrestSystem
{
    private const int ProlongedYears = 2;
    private const float GreatFamineShare = 0.3f, RecoveredShare = 0.1f;
    private const float UprisingPerYear = 0.08f, UprisingCap = 0.4f;

    private static double _lastMonth = -1d;
    private static int _months;
    private static object _world;

    public static void Tick()
    {
        if (World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastMonth = -1d;
            _months = 0;
        }
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        bool first = _lastMonth < 0d;
        _lastMonth = now;
        if (first || ++_months % 12 != 0) return;
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

    private static void Yearly()
    {
        var byEmpire = new Dictionary<Empire, (int prolonged, int total)>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.cities == null) continue;
            int prolonged = 0, total = 0;
            bool uprising = false;
            foreach (City city in kingdom.cities.ToList())
            {
                if (city?.data == null || city.isRekt()) continue;
                total++;
                var data = city.GetOrCreate();
                data.famine_years = InFamine(city) ? data.famine_years + 1 : 0;
                if (data.famine_years < ProlongedYears) continue;
                prolonged++;
                CityStabilityData state = CityStabilitySystem.State(city);
                if (state != null) state.stability = Mathf.Max(0f, state.stability - Mathf.Min(20f, 4f * (data.famine_years - 1)));
                if (uprising || city == kingdom.capital) continue;
                float chance = Mathf.Min(UprisingCap, UprisingPerYear * (data.famine_years - 1));
                if (UnityEngine.Random.value < chance) uprising = LandEconomySystem.TryStartFamineRebellion(city, data.famine_years);
            }
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
