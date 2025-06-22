using db;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class MapBoxPatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(clear_world)).Patch(AccessTools.Method(typeof(MapBox), nameof(MapBox.generateNewMap)),
        prefix: new HarmonyMethod(GetType(), nameof(clear_world)));
        LogService.LogInfo("世界数据模板加载成功");
    }

    public static void clear_world(MapBox __instance)
    {
        ModClass.IS_CLEAR = true;
        DBManagerPatch.AllClear();
    }

}
