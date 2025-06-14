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
public static class EmpireLayerToggle
{
    public static void init()
    {
        PowerLibrary powerLib = AssetManager.powers;
        powerLib.add(new GodPower
        {
            id = "empire_layer",
            name = "empire_layer",
            unselect_when_window = true,
            toggle_name = "map_empire_layer",
            force_map_mode = MetaType.Kingdom,
            toggle_action = toggleAction
        });
    }

    [Hotfixable]
    private static void toggleAction(string pPower)
    {
       

        LogService.LogInfo("点击了帝国层级开关");
        WorldTip.instance.showToolbarText(pPower);
        GodPower godPower = AssetManager.powers.get(pPower);
        PlayerOptionData playerOptionData = PlayerConfig.dict[godPower.toggle_name];
        if (godPower.map_modes_switch)
        {
            if (playerOptionData.boolVal)
            {
            }
            else
            {
                WorldTip.instance.startHide();
            }
        }
    }
}
