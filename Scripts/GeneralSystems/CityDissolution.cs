using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameLibrary;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 撤城与弃城(无小人模式下城里只要还记着人口就一直受保护，见 CityTerritoryProtection)：
//   · 撤城(神力 dissolve_city)：百姓与城里的人物迁到本国最近的城，城市当即废弃，建筑成为无主废墟；
//     本国只剩这一座城时，第一次点只提示会灭国，几秒内再点同一座城就彻底清除：百姓、本国所有人物(含船)一并清掉，
//     城市废弃，国家随之灭亡(原版在国家没有城、没有人、没有建筑时移除它)；无主城(野外、中立)直接废弃；
//   · 弃城：关闭「防止城市被摧毁」时，住不了人(住房上限为 0、没有在建的房子)、建城满三年的城，
//     百姓每月四成逃往本国最近的城，走光后人物也迁走，城市按原版规则废弃。
public static class CityDissolution
{
    public static bool IsLastCity(City city)
    {
        Kingdom kingdom = city?.kingdom;
        return kingdom != null && !kingdom.wild && !kingdom.isRekt() &&
               (kingdom.cities?.Count(other => other != null && !other.isRekt()) ?? 0) <= 1;
    }

    // 撤城：成功返回 null，否则返回提示文本键；wipeKingdom = 本国最后一座城也撤，连同国家彻底清除
    public static string Dissolve(City city, bool wipeKingdom = false)
    {
        if (city?.data == null || city.isRekt()) return "dissolve_city_none";
        Kingdom kingdom = city.kingdom;
        bool owned = kingdom != null && !kingdom.wild && !kingdom.isRekt();
        if (owned && IsLastCity(city))
        {
            if (!wipeKingdom) return "dissolve_city_last";
            Wipe(city, kingdom);
            return null;
        }
        City destination = owned ? Nearest(city) : null;
        CityTerritoryProtection.Dissolving.Add(city.id);
        if (destination != null)
        {
            CityPopulationSystem.TransferBackground(city, destination, 1f);
            MoveUnits(city, destination);
            if (kingdom.capital == city) kingdom.setCapital(destination);
        }
        else CityPopulationSystem.ClearBackground(city);
        city.turnCityToNeutral();
        foreach (TileZone zone in city.zones.ToList()) city.removeZone(zone);
        CityPopulationSystem.InvalidateHouseholdCaches();
        return null;
    }

    // 彻底清除本国最后一座城：百姓、本国所有人物(含船)清掉，城市废弃
    private static void Wipe(City city, Kingdom kingdom)
    {
        CityTerritoryProtection.Dissolving.Add(city.id);
        CityPopulationSystem.ClearBackground(city);
        foreach (Actor unit in kingdom.units.ToList())
            if (unit?.data != null && !unit.isRekt() && unit.isAlive()) unit.die(true, AttackType.Other, false, false);
        city.turnCityToNeutral();
        foreach (TileZone zone in city.zones.ToList()) city.removeZone(zone);
        CityPopulationSystem.InvalidateHouseholdCaches();
    }

    // 弃城(每月，见 CityPopulationSystem 的迁徙一步)：返回 true 表示本月已经在逃散
    public static bool TryAbandon(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.data == null || city.isRekt() ||
            city.kingdom == null || city.kingdom.wild) return false;
        if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_city_destroy?.isEnabled() == true) return false;
        if (city.getPopulationMaximum() > 0 || Date.getYearsSince(city.data.created_time) < 3) return false;
        if (city.buildings?.Any(building => building != null && building.isUnderConstruction()) == true) return false;
        if (StateSettlementSystem.AwaitingPopulation(city)) return false;
        City destination = Nearest(city);
        if (destination == null) return false;
        float total = CityPopulationSystem.GetBackgroundTotal(city);
        if (total >= 1f) CityPopulationSystem.TransferBackground(city, destination, total < 20f ? 1f : 0.4f);
        if (CityPopulationSystem.GetBackgroundTotal(city) < 1f) MoveUnits(city, destination);
        return true;
    }

    private static City Nearest(City city)
    {
        Vector2 center = city.city_center;
        return city.kingdom?.cities?.Where(other => other != null && other != city && !other.isRekt() &&
                                                    !CityTerritoryProtection.Dissolving.Contains(other.id))
            .OrderBy(other => Vector2.SqrMagnitude(other.city_center - center)).FirstOrDefault();
    }

    private static void MoveUnits(City city, City destination)
    {
        foreach (Actor unit in city.units.ToList())
        {
            if (unit?.data == null || unit.isRekt() || !unit.isAlive()) continue;
            if (unit.isCityLeader() && unit.city == city) city.removeLeader();
            unit.joinCity(destination);
        }
    }
}
