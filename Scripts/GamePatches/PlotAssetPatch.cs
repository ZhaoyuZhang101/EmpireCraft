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
        new Harmony(nameof(checkIsPossible)).Patch(AccessTools.Method(typeof(PlotAsset), nameof(PlotAsset.checkIsPossible)),
            prefix: new HarmonyMethod(GetType(), nameof(checkIsPossible)));
    }

    // 开战谋划：无小人模式下独立王国不看君主本人的等级、武略、钱和国家声望(缘由见 EmpireCraftPlotsAddition.SkipsWarPlotRulerGate)，
    // 其余门槛与 check_is_possible 照原版 PlotAsset.checkIsPossible 的顺序查
    public static bool checkIsPossible(PlotAsset __instance, Actor pActor, bool pWorldLawsCheck, ref bool __result)
    {
        if (__instance == null || __instance != PlotsLibrary.new_war ||
            !EmpireCraft.Scripts.AI.EmpireCraftPlotsAddition.SkipsWarPlotRulerGate(pActor)) return true;
        __result = pActor.renown >= __instance.min_renown_actor &&
                   pActor.intelligence >= __instance.min_intelligence &&
                   pActor.diplomacy >= __instance.min_diplomacy &&
                   pActor.stewardship >= __instance.min_stewardship &&
                   __instance.canBeDoneByRole(pActor) &&
                   (!pWorldLawsCheck || __instance.isAllowedByWorldLaws()) &&
                   __instance.check_is_possible != null && __instance.check_is_possible(pActor);
        return false;
    }
    public static bool canBeDoneByRole(PlotAsset __instance, Actor pActor, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pActor)) return true;
        if (pActor != null && __instance.id == "faction_leader_influence_local_kingdom" &&
            pActor?.GetFaction()?.GetLeader()?.id == pActor.id)
        {
            __result = true;
            return false;
        }
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
