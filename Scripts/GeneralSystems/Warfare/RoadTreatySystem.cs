using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 通路之约(用户 2026-10-17 "如果两国有过友好的时期的话可以发起决议连通道路")：
//   · 两国接壤(两国的城之间规划了路，见 RoadNetwork)、不在交战、彼此好感都在 20 以上，每年记一年交好；
//     交好满五年即"有过友好的时期"，记下不忘(后来交恶也算有过)；
//   · 有过友好时期、眼下不在交战、对方对本国好感不低于 0 的，国君可发起决议"通路之约"(见 EmpireCraftPlotsAddition)，
//     议成后两国之间规划的路可以修：哪一国开工都行，造价两国各出一半(对方出不起就由开工的一方垫上，见 RoadBuildingSystem)；
//     养路照旧由路一头的国家出；
//   · 两国开战即作废(已修的路照旧在，交战照常毁路)。
public static class RoadTreatySystem
{
    private const float FriendlyOpinion = 20f, FriendlyYearsNeeded = 5f;
    private static RoadTreatyData _data = new();
    // 两国之间规划了路的国对(每年重算一次；决议的 AI 判断要频繁问，不每次都扫全图的路)
    private static List<(Kingdom a, Kingdom b)> _neighbours;

    public static void ResetWorldState()
    {
        _data = new RoadTreatyData();
        _neighbours = null;
    }

    public static void Load(RoadTreatyData saved)
    {
        _neighbours = null;
        _data = saved ?? new RoadTreatyData();
        _data.friendly_years ??= new Dictionary<string, float>();
        _data.friendly ??= new List<string>();
        _data.treaties ??= new Dictionary<string, double>();
    }

    public static RoadTreatyData Save() => _data;

    private static string Key(Kingdom a, Kingdom b) => a.id < b.id ? $"{a.id}_{b.id}" : $"{b.id}_{a.id}";

    private static bool Valid(Kingdom a, Kingdom b) =>
        a?.data != null && b?.data != null && a != b && !a.isRekt() && !b.isRekt() && !a.wild && !b.wild;

    public static bool Has(Kingdom a, Kingdom b) => Valid(a, b) && _data.treaties.ContainsKey(Key(a, b));

    public static bool WereFriendly(Kingdom a, Kingdom b) => Valid(a, b) && _data.friendly.Contains(Key(a, b));

    private static float Opinion(Kingdom from, Kingdom to) => World.world.diplomacy?.getOpinion(from, to)?.total ?? 0;

    // 两国的城之间规划了路的国对(不同国、不在同一帝国)
    private static List<(Kingdom a, Kingdom b)> Neighbours()
    {
        var seen = new HashSet<string>();
        var result = new List<(Kingdom, Kingdom)>();
        foreach (var pair in RoadNetwork.Plans)
        {
            if (pair.Value.zones.Count == 0) continue;
            Kingdom a = World.world.cities.get(pair.Key.Item1)?.kingdom, b = World.world.cities.get(pair.Key.Item2)?.kingdom;
            if (!Valid(a, b) || a.GetEmpire() != null && a.GetEmpire() == b.GetEmpire()) continue;
            if (seen.Add(Key(a, b))) result.Add((a, b));
        }
        return result;
    }

    // 每年(RoadBuildingSystem 的年结)：记交好的年数；开战的作废通路之约
    public static void Yearly()
    {
        var alive = new HashSet<long>(World.world.kingdoms.list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt())
            .Select(kingdom => kingdom.id));
        bool Gone(string key)
        {
            string[] ids = key.Split('_');
            return ids.Length != 2 || !long.TryParse(ids[0], out long a) || !long.TryParse(ids[1], out long b) ||
                   !alive.Contains(a) || !alive.Contains(b);
        }
        foreach (string key in _data.treaties.Keys.Where(Gone).ToList()) _data.treaties.Remove(key);
        foreach (string key in _data.friendly_years.Keys.Where(Gone).ToList()) _data.friendly_years.Remove(key);
        _data.friendly.RemoveAll(Gone);
        _neighbours = Neighbours();
        foreach ((Kingdom a, Kingdom b) in _neighbours)
        {
            string key = Key(a, b);
            if (a.isInWarWith(b))
            {
                if (_data.treaties.Remove(key))
                    TranslateHelper.LogEventMessage(string.Format(LM.Get("road_treaty_void"), a.GetKingdomName(), b.GetKingdomName()), a);
                continue;
            }
            if (Opinion(a, b) < FriendlyOpinion || Opinion(b, a) < FriendlyOpinion) continue;
            float years = (_data.friendly_years.TryGetValue(key, out float old) ? old : 0f) + 1f;
            _data.friendly_years[key] = years;
            if (years >= FriendlyYearsNeeded && !_data.friendly.Contains(key)) _data.friendly.Add(key);
        }
    }

    // 决议"通路之约"可以向谁发起：有过友好时期、还没议定、不在交战、对方对本国好感不低于 0
    public static Kingdom Partner(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || _data.friendly.Count == 0) return null;
        _neighbours ??= Neighbours();
        foreach ((Kingdom a, Kingdom b) in _neighbours)
        {
            Kingdom other = a == kingdom ? b : b == kingdom ? a : null;
            if (other == null || Has(kingdom, other) || !WereFriendly(kingdom, other) || kingdom.isInWarWith(other) ||
                Opinion(other, kingdom) < 0f) continue;
            return other;
        }
        return null;
    }

    public static bool Sign(Kingdom kingdom)
    {
        Kingdom other = Partner(kingdom);
        if (other == null) return false;
        _data.treaties[Key(kingdom, other)] = World.world.getCurWorldTime();
        TranslateHelper.LogEventMessage(string.Format(LM.Get("road_treaty_signed"), kingdom.GetKingdomName(), other.GetKingdomName()),
            kingdom);
        return true;
    }
}
