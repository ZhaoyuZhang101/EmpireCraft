using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityCombineHouses: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(City pCity)
    {
        if (pCity?.kingdom == null) return BehResult.Continue;
        if (!pCity.kingdom.hasEnemies()) pCity.ClearOccupiedStatus();
        if (!pCity.buildings.ToList().Any(b => b.asset.id.Contains("city_")))
        {
            // 世界法则关掉合并时保留原版民居(modernmod 的 6~11 级民居也是民居，合并会把它们全拆掉)；
            // 已经住进 modernmod 高级民居的城市也不再合并
            if (EmpireCraftWorldLawLibrary.empirecraft_law_combine_houses?.isEnabled() == false) return BehResult.Continue;
            if (pCity.buildings.Any(b => b.asset.type == "type_house" && b.asset.id.EndsWith("_modernmod")))
                return BehResult.Continue;
            // 无小人模式下城里实体单位很少，按户数判断
            int population = EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.AbstractPopulationEnabled
                ? EmpireCraft.Scripts.GeneralSystems.CityPopulationSystem.Households(pCity)
                : pCity.countUnits();
            if (population > 80)
            {
                var loc = pCity.buildings.Find(b => b.asset.type == "type_house")?.current_tile;
                if (loc == null) return BehResult.Continue;
                pCity.buildings.ForEach(b=>
                {
                    if (b.asset.type == "type_house")
                    {
                        b.startRemove();
                    }
                });
                var culture = pCity.kingdom?.GetEmpireCraftCulture();
                culture = EmpireCraftBuildingLibrary.VALID_CULTURE_BUILDING.Contains(culture) ? culture : "Western";
                BuildingHelper.tryToBuildNear(loc, $"city_{culture?.ToLower()}_0");
            }
        }
        else
        {
            var building = pCity.buildings.Find(b=>b.asset.id.Contains("city_"));
            if (building != null)
            {
                if (pCity.countUnits() >= 100)
                {
                    if (building.asset.upgrade_level == 1)
                    {
                        building.upgradeBuilding();
                    }
                }

                if (pCity.countUnits() >= 150 || (pCity.isCapitalCity() && pCity.kingdom.IsEmpire()))
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
}