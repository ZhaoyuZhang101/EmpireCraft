using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;

public class CityPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {

        new Harmony(nameof(update_dirty_patch)).Patch(
            AccessTools.Method(typeof(City), nameof(City.setDirty)),
            prefix: new HarmonyMethod(GetType(), nameof(update_dirty_patch))
        );

        new Harmony(nameof(destroy_city)).Patch(
            AccessTools.Method(typeof(City), nameof(City.destroyCity)),
            prefix: new HarmonyMethod(GetType(), nameof(destroy_city))
        );

    }

    public static void destroy_city(City __instance)
    {
        if (__instance.hasTitle())
        {
            __instance.GetTitle().removeCity(__instance);
        }
    }

    public static void update_dirty_patch(City __instance)
    {
        if (__instance.kingdom == null) 
        {
            return;
        }
        if (__instance.kingdom.GetEmpire() != null)
        {
            __instance.kingdom.GetEmpire().setDirty();
        }
        if (__instance.hasTitle())
        {
            __instance.GetTitle().setDirty();
        }
    }

}
