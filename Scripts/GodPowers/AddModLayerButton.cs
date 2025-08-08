using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

public static class AddModLayerButton
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "add_ModLayer",
            name = "add_ModLayer",
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
        if (ConfigData.selected_cityA.GetModLayer() == city.GetModLayer())
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
        if (!ConfigData.selected_cityA.IsInModLayer())
        {
            if (ConfigData.selected_cityA.getTile() == ConfigData.selected_cityB.getTile())
            {
                ActionLibrary.showWhisperTip("city_cancelled");
                ConfigData.selected_cityB = null;
                return false;
            }
            if (ConfigData.selected_cityB.IsInModLayer())
            {
                ModClass.ModLayer_MANAGER.AddCityToModLayer(ConfigData.selected_cityB.GetModLayer(), ConfigData.selected_cityA);
            }
        }
        ConfigData.selected_cityA = null;
        ConfigData.selected_cityB = null;
        World.world.zone_calculator.dirtyAndClear();
        return true;
    }
}
