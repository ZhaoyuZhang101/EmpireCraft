using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国内行政区飞地的内部划界修正(与国家海外飞地的维护、放弃分开，见 ExclaveMaintenanceSystem)：
//   行政区(受托管理的道、军、省等，不含世袭封国)有城市和本区首府不连成一片、
//   却被帝国其它成员包住时，划给与它边界最长的那个成员(中央或其它行政区)。
//   只调整帝国内部归属，不丢国家领土；首都、王畿等受保护的直辖城、战时代管的城不动。
//   每个帝国每年最多改划一座城，和平时才做。
public static class InternalBoundarySystem
{
    private static double _lastScan = -1d;

    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            try
            {
                FixOne(empire);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][行政区划界] 检查失败: {exception.Message}");
            }
        }
    }

    private static void FixOne(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.HasTerritorialWar() ||
            AdministrationReorganizationSystem.IsRunning(empire)) return;
        Kingdom core = empire.CoreKingdom;
        if (core == null || core.isRekt() || core.hasEnemies()) return;
        HashSet<long> protectedCities = null;
        foreach (Kingdom region in empire.kingdoms_list.ToArray())
        {
            if (region == null || region == core || region.isRekt() || region.capital == null ||
                !region.IsAdministrativeKingdomType() || region.cities == null || region.cities.Count < 2) continue;
            HashSet<City> connected = Connected(region);
            foreach (City city in region.cities.ToArray())
            {
                if (city == null || city.isRekt() || connected.Contains(city) || WartimeCustodySystem.InCustody(city)) continue;
                protectedCities ??= empire.GetProtectedDirectCityIds();
                if (protectedCities.Contains(city.id)) continue;
                Kingdom target = BestNeighbour(city, region, empire, core);
                if (target == null) continue;
                city.joinAnotherKingdom(target);
                TranslateHelper.LogEventMessage(string.Format(LM.Get("internal_boundary_fixed"),
                    city.GetCityName(), region.GetKingdomName(), target.GetKingdomName()), core);
                return;
            }
        }
    }

    // 与首府连成一片的城市(按原版相邻城市，陆上相接)
    private static HashSet<City> Connected(Kingdom region)
    {
        var result = new HashSet<City> { region.capital };
        var queue = new Queue<City>();
        queue.Enqueue(region.capital);
        while (queue.Count > 0)
        {
            City current = queue.Dequeue();
            foreach (City next in current.neighbours_cities)
                if (next != null && next.kingdom == region && result.Add(next)) queue.Enqueue(next);
        }
        return result;
    }

    // 与这座城边界最长、且能接收的帝国成员；本城完全被外国包围(没有成员接壤)则不动
    private static Kingdom BestNeighbour(City city, Kingdom region, Empire empire, Kingdom core)
    {
        var lengths = new Dictionary<Kingdom, int>();
        if (city.zones == null) return null;
        foreach (TileZone zone in city.zones)
        {
            if (zone?.neighbours == null || zone.city != city) continue;
            foreach (TileZone neighbour in zone.neighbours)
            {
                Kingdom other = neighbour?.city?.kingdom;
                if (other == null || other == region || !EmpireMembershipService.BelongsTo(empire, other)) continue;
                if (other != core && !other.IsAdministrativeKingdomType()) continue;
                if (other.IsFactionRebelling() || other.IsLocalRebelling()) continue;
                lengths.TryGetValue(other, out int count);
                lengths[other] = count + 1;
            }
        }
        Kingdom best = null;
        int bestLength = 0;
        foreach (KeyValuePair<Kingdom, int> pair in lengths)
            if (pair.Value > bestLength || pair.Value == bestLength && pair.Key == core)
            {
                best = pair.Key;
                bestLength = pair.Value;
            }
        return best;
    }
}
