using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class AddModObjectButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "add_province",
            name = "add_province",
            click_action = add_create_action
        });
    }

    private static bool add_create_action(WorldTile pTile, string pPower)
    {
        City city = pTile.zone.city;
        if (city.isRekt())
        {
            return false;
        }
        if (city.isRekt())
        {
            return false;
        }
        if (city.isNeutral())
        {
            return false;
        }
        if (ConfigData.selected_cityA == null)
        {
            if (!city.kingdom.isEmpire())
            {
                ActionLibrary.showWhisperTip("city_need_to_belong_empire");
                return false;
            }
            ConfigData.selected_cityA = city;
            ActionLibrary.showWhisperTip("city_selected_first");
            return false;
        }
        if (ConfigData.selected_cityA == city)
        {
            ActionLibrary.showWhisperTip("city_cancelled");
            ConfigData.selected_cityA = null;
            ConfigData.selected_cityB = null;
            return false;
        }
        if (ConfigData.selected_cityA.GetProvinceID() == city.GetProvinceID())
        {
            ActionLibrary.showWhisperTip("city_cancelled");
            ConfigData.selected_cityA = null;
            ConfigData.selected_cityB = null;
            return false;
        }
        if (ConfigData.selected_cityB == null)
        {
            ConfigData.selected_cityB = city;
        }
        if (ConfigData.selected_cityB == ConfigData.selected_cityA)
        {
            return false;
        }
        if (ConfigData.selected_cityA.hasProvince())
        {
            if (ConfigData.selected_cityA.getTile() == ConfigData.selected_cityB.getTile())
            {
                ActionLibrary.showWhisperTip("city_cancelled");
                ConfigData.selected_cityB = null;
                return false;
            }
            if (ConfigData.selected_cityB.hasProvince())
            {
                ModClass.ModObjectManager.AddCityToProvince(ConfigData.selected_cityB.GetProvince(), ConfigData.selected_cityA);
            }
        }
        if (ModClass.ModObjectManager.forceProvince(ConfigData.selected_cityA, ConfigData.selected_cityB))
        {
            ActionLibrary.showWhisperTip("create_new_province");
        }
        else
        {
            ActionLibrary.showWhisperTip("city_add_to_province");
        }
        ConfigData.selected_cityA = null;
        ConfigData.selected_cityB = null;
        World.world.zone_calculator.dirtyAndClear();
        return true;
    }
}
