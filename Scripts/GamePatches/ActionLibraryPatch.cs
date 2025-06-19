using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;
public class ActionLibraryPatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(Inspect_Empire)).Patch(AccessTools.Method(typeof(ActionLibrary), nameof(ActionLibrary.inspectKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(Inspect_Empire)));
    }

    public static bool Inspect_Empire(WorldTile pTile, string pPower, ref bool __result)
    {
        //if (pTile == null)
        //{
        //    __result = false;
        //    return false;
        //}
        //City city = pTile.zone.city;
        //if (city.isRekt())
        //{
        //    __result = false;
        //    return false;
        //}
        //Kingdom kingdom = city.kingdom;
        //if (kingdom.isRekt())
        //{
        //    __result = false;
        //    return false;
        //}
        //if (kingdom.isNeutral())
        //{
        //    __result = false;
        //    return false;
        //}
        //if (kingdom.isInEmpire()&& OverallHelperFunc.IsEmpireLayerOn())
        //{
        //    ConfigData.CURRENT_SELECTED_EMPIRE = kingdom.GetEmpire();
        //    kingdom.GetEmpire().SelectAndInspect();
        //    __result = true;
        //    return false;
        //}
        //MetaType.Kingdom.getAsset().selectAndInspect(kingdom);
        //__result = true;
        //return false;
        return true;
    }
}
