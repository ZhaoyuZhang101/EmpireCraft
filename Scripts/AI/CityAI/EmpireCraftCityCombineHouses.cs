using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityCombineHouses: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    // 城里找不到空地放城池的，一年内不再每次都扫全城
    private static readonly Dictionary<long, double> FailedAt = new();

    public override BehResult execute(City pCity)
    {
        if (pCity?.kingdom == null) return BehResult.Continue;
        if (!pCity.kingdom.hasEnemies()) pCity.ClearOccupiedStatus();
        // 无小人模式下城里实体单位很少，按户数判断(一户占一个住房位)
        int population = CityPopulationSystem.AbstractPopulationEnabled
            ? CityPopulationSystem.Households(pCity)
            : pCity.countUnits();
        if (!pCity.buildings.ToList().Any(b => b.asset.id.Contains("city_")))
        {
            // 世界法则关掉合并时保留原版民居(modernmod 的 6~11 级民居也是民居，合并会把它们全拆掉)；
            // 已经住进 modernmod 高级民居的城市也不再合并
            if (EmpireCraftWorldLawLibrary.empirecraft_law_combine_houses?.isEnabled() == false) return BehResult.Continue;
            if (pCity.buildings.Any(b => b.asset.type == "type_house" && b.asset.id.EndsWith("_modernmod")))
                return BehResult.Continue;
            if (population > 80) Combine(pCity, population);
        }
        else
        {
            var building = pCity.buildings.Find(b=>b.asset.id.Contains("city_"));
            if (building != null)
            {
                if (population >= 100)
                {
                    if (building.asset.upgrade_level == 1)
                    {
                        building.upgradeBuilding();
                    }
                }

                if (population >= 150 || (pCity.isCapitalCity() && pCity.kingdom.IsEmpire()))
                {
                    if (building.asset.upgrade_level <= 1)
                    {
                        building.upgradeBuilding();
                        building.upgradeBuilding();
                    }
                    else
                    {
                        building.upgradeBuilding();
                    }

                }
            }
        }
        return BehResult.Continue;
    }

    // 先在城里找空地放下城池，放成了才拆民居，而且只拆到剩下的住房仍够住为止。
    // 以前先拆光民居再在原处放城池，放不下时全城住房归零，之后新造的民居(包括手动造的)也会被再拆一遍
    private static void Combine(City city, int households)
    {
        if (FailedAt.TryGetValue(city.id, out double failed) && Date.getYearsSince(failed) < 1) return;
        var culture = city.kingdom?.GetEmpireCraftCulture();
        culture = EmpireCraftBuildingLibrary.VALID_CULTURE_BUILDING.Contains(culture) ? culture : "Western";
        BuildingAsset asset = AssetManager.buildings.get($"city_{culture?.ToLower()}_0");
        if (asset == null) return;
        Building placed = Place(city, asset);
        if (placed == null)
        {
            FailedAt[city.id] = World.world.getCurWorldTime();
            return;
        }
        FailedAt.Remove(city.id);
        if (placed.isUnderConstruction()) return;
        int spare = city.status.housing_total + asset.housing_slots - households;
        foreach (Building house in city.buildings.ToList())
        {
            if (house == null || house == placed || house.isOnRemove() || house.asset.type != "type_house" ||
                house.asset.id.Contains("city_")) continue;
            if (spare - house.asset.housing_slots < 0) continue;
            spare -= house.asset.housing_slots;
            house.startRemove();
        }
    }

    // 从城中心往外找：不放进规划农田(见 FarmlandSystem，风车范围里不盖民居)
    private static Building Place(City city, BuildingAsset asset)
    {
        if (city.zones == null) return null;
        Vector2 center = city.city_center;
        foreach (TileZone zone in city.zones.ToList()
                     .Where(zone => zone?.tiles != null && zone.city == city && !FarmlandSystem.IsPlanned(city, zone))
                     .OrderBy(zone => Vector2.SqrMagnitude(new Vector2(zone.centerTile.x, zone.centerTile.y) - center)))
        {
            foreach (WorldTile tile in zone.tiles)
                if (World.world.buildings.canBuildFrom(tile, asset, city))
                    return World.world.buildings.addBuilding(asset, tile);
        }
        return null;
    }
}
