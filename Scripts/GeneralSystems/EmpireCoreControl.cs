using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国核心的控制权统一口径(军阀时期、核心国号、党禁的核心民心都用这一份)：
// 城市归谁算——附庸(如易帜归附的军阀)算到最上层宗主名下，帝国成员算到帝国核心王国名下。
// 以前军阀时期算附庸、核心国号和核心民心不算附庸，同一个核心在不同系统里"控制比例"对不上。
public static class EmpireCoreControl
{
    private const float CacheSeconds = 5f;
    private static readonly Dictionary<long, (Dictionary<Kingdom, int> held, int total, float at)> Cache = new();
    private static object _world;

    public static Kingdom Sovereign(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return null;
        var visited = new HashSet<long>();
        while (FeudalVassalService.GetOverlord(kingdom) is Kingdom lord && visited.Add(kingdom.id)) kingdom = lord;
        Empire empire = kingdom.GetEmpire();
        return empire != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null
            ? empire.CoreKingdom
            : kingdom;
    }

    public static Dictionary<Kingdom, int> CountHolders(List<City> cities)
    {
        var held = new Dictionary<Kingdom, int>();
        foreach (City city in cities)
        {
            Kingdom holder = Sovereign(city.kingdom);
            if (holder == null) continue;
            held[holder] = (held.TryGetValue(holder, out int count) ? count : 0) + 1;
        }
        return held;
    }

    // 各政权控制的核心城市数与核心城市总数；同一核心会被频繁询问(华夏军阀 AI、年度扫描)，缓存几秒
    public static (Dictionary<Kingdom, int> held, int total) GetHolders(EmpireCore core)
    {
        if (core == null) return (new Dictionary<Kingdom, int>(), 0);
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Cache.Clear();
        }
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(core.id, out var cached) && now - cached.at < CacheSeconds)
            return (cached.held, cached.total);
        List<City> cities = EmpireCoreManager.GetCities(core);
        Dictionary<Kingdom, int> held = CountHolders(cities);
        Cache[core.id] = (held, cities.Count, now);
        return (held, cities.Count);
    }

    public static void Invalidate(EmpireCore core = null)
    {
        if (core == null) Cache.Clear();
        else Cache.Remove(core.id);
    }

    public static float GetControlShare(Empire empire, EmpireCore core)
    {
        if (empire?.CoreKingdom == null) return 0f;
        (Dictionary<Kingdom, int> held, int total) = GetHolders(core);
        return total == 0 ? 0f : (held.TryGetValue(empire.CoreKingdom, out int count) ? count : 0) / (float)total;
    }

    // 核心城市中归该帝国(含其附庸)控制的那些
    public static List<City> HeldCities(Empire empire, EmpireCore core) => core == null || empire?.CoreKingdom == null
        ? new List<City>()
        : EmpireCoreManager.GetCities(core).Where(city => Sovereign(city.kingdom) == empire.CoreKingdom).ToList();
}
