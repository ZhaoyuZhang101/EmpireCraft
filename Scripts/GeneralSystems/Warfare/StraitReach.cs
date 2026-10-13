using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 隔海相望的近邻：宣战、扩张、统一、迫使朝贡原本只挑接壤的国家，近海的小岛永远打不到。
// 本国(或所在帝国)到了有水师的时代(见 NavySystem.NavyEra)且有港口，对方有临海的城，
// 两边最近的港口与临海城相距不超过 StraitDistance 格时，也算可以宣战的近邻；
// 真打起来照跨海登陆的规矩，要有不经敌方制海海区的航线与足够的运输船(见 FrontSystem、NavySystem)。
public static class StraitReach
{
    private const float StraitDistance = 64f;
    // 各国(帝国按核心)的水师港口，缓存一秒(真实时间)：宣战、诉求挑目标时要对每个国家问一遍
    private const float CacheSeconds = 1f;
    private static readonly Dictionary<long, (float at, List<City> ports)> PortCache = new();

    public static bool Across(Kingdom initiator, Kingdom target)
    {
        if (initiator?.data == null || target?.data == null || initiator == target || initiator.isRekt() || target.isRekt())
            return false;
        List<City> ports = Ports(initiator);
        if (ports.Count == 0 || target.cities == null) return false;
        float limit = StraitDistance * StraitDistance;
        foreach (City shore in target.cities)
        {
            if (shore?.data == null || shore.isRekt()) continue;
            if (!ports.Any(port => (port.city_center - shore.city_center).sqrMagnitude <= limit)) continue;
            if (SeaRegionSystem.RegionsAround(shore).Count > 0) return true;
        }
        return false;
    }

    private static List<City> Ports(Kingdom initiator)
    {
        Empire empire = initiator.GetEmpire();
        bool imperial = empire != null && !empire.isRekt() && !empire.IsArchived();
        long key = imperial ? -empire.id : initiator.id;
        float now = Time.unscaledTime;
        if (PortCache.TryGetValue(key, out var cached) && now >= cached.at && now - cached.at < CacheSeconds) return cached.ports;
        IEnumerable<Kingdom> realm = imperial ? empire.kingdoms_list : new[] { initiator };
        List<City> ports = realm.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && NavySystem.NavyEra(kingdom))
            .SelectMany(NavySystem.Ports).Where(city => city?.data != null && !city.isRekt()).ToList();
        if (PortCache.Count > 512) PortCache.Clear();
        PortCache[key] = (now, ports);
        return ports;
    }

    // 接壤或隔海相望(帝国按全体成员算)
    public static bool Near(Kingdom initiator, Kingdom target) =>
        initiator != null && target != null &&
        (initiator.IsNeighbourWith(target) || initiator.GetEmpire()?.IsNeighbourWith(target) == true || Across(initiator, target));
}
