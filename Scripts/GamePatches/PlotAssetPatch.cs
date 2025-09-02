using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.PlayerLoop;

namespace EmpireCraft.Scripts.GamePatches;
public class PlotAssetPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(canBeDoneByRole)).Patch(AccessTools.Method(typeof(PlotAsset), nameof(PlotAsset.canBeDoneByRole)),
        prefix: new HarmonyMethod(GetType(), nameof(canBeDoneByRole)));
    }
    public static bool canBeDoneByRole(PlotAsset __instance, Actor pActor, ref bool __result)
    {
        if (__instance.can_be_done_by_king && pActor.isKing())
        {
            __result = true;
            return false;
        }
        if (__instance.can_be_done_by_leader && pActor.isCityLeader())
        {
            __result = true;
            return false;
        }
        if (__instance.can_be_done_by_clan_member && pActor.hasClan())
        {
            __result = true;
            return false;
        }
        if (__instance.can_be_done_by_leader && pActor.isOfficer())
        {
            __result = true;
            return false;
        }
        __result = false;
        return false;
    }
}
