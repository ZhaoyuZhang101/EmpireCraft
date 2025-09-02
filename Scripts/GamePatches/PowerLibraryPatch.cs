using EmpireCraft.Scripts.Data;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class PowerLibraryPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(disableAllOtherMapModes)).Patch(AccessTools.Method(typeof(PowerLibrary), nameof(PowerLibrary.disableAllOtherMapModes)),
            prefix: new HarmonyMethod(GetType(), nameof(disableAllOtherMapModes)));
    }


    public static void disableAllOtherMapModes(PowerLibrary __instance, string pMainPower)
    {
        ModClass.CURRENT_MAP_MOD = Enums.EmpireCraftMapMode.None;
        PlayerConfig.dict["map_title_layer"].boolVal = false;
        PlayerConfig.dict["map_empire_layer"].boolVal = false;
    }
}
