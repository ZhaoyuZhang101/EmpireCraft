using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class AlliancePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(can_join)).Patch(
            AccessTools.Method(typeof(Alliance), nameof(Alliance.canJoin)),
            prefix: new HarmonyMethod(GetType(), nameof(can_join))
        );
    }

    public static bool can_join(Alliance __instance, Kingdom pKingdom, ref bool __result)
    {
        if (pKingdom.isInEmpire())
        {
            __result = false;
            return false; // 阻止原方法执行
        }
        foreach (Kingdom targetKingdom in __instance.kingdoms_hashset)
        {
            if (targetKingdom.isNeighbourWith(pKingdom) && pKingdom.isOpinionTowardsKingdomGood(targetKingdom))
            {
                __result = true;
                return true;
            }
        }
        return false; // 允许原方法执行
    }
}
