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
        // todo: 当边界缩小时触发
        return true;
    }
}
