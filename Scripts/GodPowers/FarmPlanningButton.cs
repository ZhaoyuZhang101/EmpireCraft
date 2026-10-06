using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 规划农田：点地图上的区块，划为所在城市的规划农田区；再点一次取消(见 FarmlandSystem)。
// 划定后区块闪绿光，并立即在区块里开出第一批田；取消时闪灰光
public static class FarmPlanningButton
{
    private const int FieldsOnPlan = 64;

    public static void init()
    {
        AssetManager.powers.add(new GodPower
        {
            id = "farm_planning",
            name = "farm_planning",
            click_action = Toggle
        });
    }

    private static bool Toggle(WorldTile pTile, string pPower)
    {
        TileZone zone = pTile?.zone;
        City city = zone?.city;
        if (city == null || city.isRekt() || city.isNeutral())
        {
            WorldTip.showNow(LM.Get("farm_planning_no_city"), false, "top", 3f);
            return false;
        }
        bool planned = FarmlandSystem.TogglePlanned(city, zone);
        if (planned)
        {
            if (CityPopulationSystem.AbstractPopulationEnabled) FarmlandSystem.CultivateZone(city, zone, FieldsOnPlan);
            KingdomFrontLineHelper.HighlightZones(zone, new Color(0.45f, 0.85f, 0.35f));
        }
        else
        {
            KingdomFrontLineHelper.HighlightZones(zone, new Color(0.6f, 0.6f, 0.6f));
        }
        WorldTip.showNow(string.Format(LM.Get(planned ? "farm_planning_added" : "farm_planning_removed"),
            city.GetCityName()), false, "top", 3f);
        return true;
    }
}
