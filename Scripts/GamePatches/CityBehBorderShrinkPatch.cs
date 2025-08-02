using ai.behaviours;
using EmpireCraft.Scripts.Data;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
internal class CityBehBorderShrinkPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(execute)).Patch(
            AccessTools.Method(typeof(CityBehBorderShrink), nameof(CityBehBorderShrink.execute)),
            prefix: new HarmonyMethod(GetType(), nameof(execute))
        );
    }

    public static bool execute(CityBehBorderShrink __instance, City pCity, ref BehResult __result)
    {
        if (BehaviourActionBase<City>.world.getWorldTimeElapsedSince(pCity.timestamp_shrink) < SimGlobals.m.empty_city_borders_shrink_time)
        {
            __result = BehResult.Stop;
            return false;
        }
        if (pCity.units.Count > 0||!ConfigData.PREVENT_CITY_DESTROY)
        {
            __result = BehResult.Stop;
            return false;
        }
        using ListPool<TileZone> listPool = new ListPool<TileZone>(pCity.border_zones);
        if (!listPool.Any())
        {
            __result = BehResult.Stop;
            return false;
        }
        TileZone random = listPool.GetRandom();
        pCity.removeZone(random);
        pCity.timestamp_shrink = BehaviourActionBase<City>.world.getCurWorldTime();
        __result = BehResult.Continue;

        return false;
    }
}
