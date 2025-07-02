using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GodPowers;
public static class PreventCityDestroyToggle
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "prevent_city_destroy",
            name = "prevent_city_destroy",
            toggle_name = "prevent_city_destroy",
            toggle_action = toggleAction
        });
    }
    private static void toggleAction(string pPower)
    {
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        if (!playerOptionData.boolVal)
        {
            ConfigData.PREVENT_CITY_DESTROY = true;
            LogService.LogInfo("已开启防止城市被摧毁");
        }
        else
        {
            ConfigData.PREVENT_CITY_DESTROY = false;
            LogService.LogInfo("已关闭防止城市被摧毁");
        }
    }
}
