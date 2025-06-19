using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

public class ScrollWindowPtach : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(checkWindowExist)).Patch(
            AccessTools.Method(typeof(ScrollWindow), nameof(ScrollWindow.checkWindowExist)),
            prefix: new HarmonyMethod(GetType(), nameof(checkWindowExist))
            );
    }

    public static bool checkWindowExist(ScrollWindow __instance, string pWindowID, ref bool __result)
    {
        //if (pWindowID == "kingdom")
        //{
        //    if (Config.selected_kingdom.isEmpire())
        //    {
        //        showEmpire(__instance);
        //        __result = true;
        //        return false;
        //    }
        //}
        //if ( pWindowID == "empire" )
        //{
        //    showEmpire(__instance);
        //    __result = true;
        //    return false;
        //}
        return true;
    }

    public static void showEmpire(ScrollWindow w)
    {
        string path = "windows/kingdom";
        ScrollWindow tScrollWindow = (ScrollWindow)Resources.Load(path, typeof(ScrollWindow));

        if (!ScrollWindow._all_windows.ContainsKey("empire"))
        {
            ScrollWindow._all_windows.Add("empire", tScrollWindow);
        }
        tScrollWindow.screen_id = "empire";
        tScrollWindow.name = "empire";
        tScrollWindow.create();
    }

}

