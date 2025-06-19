using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class UnitWindowPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        // UnitWindow类的补丁
        new Harmony(nameof(set_stats_rows)).Patch(
            AccessTools.Method(typeof(UnitWindow), nameof(UnitWindow.showStatsRows)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(set_stats_rows))
        );
        LogService.LogInfo("角色窗口补丁加载成功");
    }

    private static void set_stats_rows(UnitWindow __instance)
    {
        PeeragesLevel peeragesLevel = __instance.actor.GetPeeragesLevel();
        __instance.showStatRow("Peerages", LM.Get("default_" + peeragesLevel.ToString()), MetaType.None, -1L, null, LM.Get("Peerages"), null);
        if (__instance.actor.HasTitle())
        {
            __instance.showStatRow("EmpireTitle", __instance.actor.GetTitle() , MetaType.None, -1L, null, LM.Get("Peerages"), null);
        }
    }
}
