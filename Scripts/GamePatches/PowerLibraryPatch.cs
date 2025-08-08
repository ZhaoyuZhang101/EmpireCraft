using EmpireCraft.Scripts.Data;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Enums;

namespace EmpireCraft.Scripts.GamePatches;
public class PowerLibraryPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(DisableAllOtherMapModes)).Patch(AccessTools.Method(typeof(PowerLibrary), nameof(PowerLibrary.disableAllOtherMapModes)),
            prefix: new HarmonyMethod(GetType(), nameof(DisableAllOtherMapModes)));
    }


    public static void DisableAllOtherMapModes(PowerLibrary __instance, string pMainPower)
    {
        ModClass.CURRENT_MAP_MOD = ModMapMode.None;
        PlayerConfig.dict["map_ModLayer_layer"].boolVal = false;
    }
}
