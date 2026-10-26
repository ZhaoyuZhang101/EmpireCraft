using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using EmpireCraft.Scripts.GeneralSystems;
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
    // 其余门槛与 check_is_possible 照原版 PlotAsset.checkIsPossible 的顺序查。
    // 原版创教谋划在无小人模式下停用，改按背景人口创教(见 BackgroundReligionSystem)；有小人时创教者的天下已有本土宗教也不创(见 ReligionSphere)
    public static bool checkIsPossible(PlotAsset __instance, Actor pActor, bool pWorldLawsCheck, ref bool __result)
    {
        if (VanillaIdentitySystem.BlocksPlot(__instance?.id) ||
            BackgroundReligionSystem.ReplacesVanillaPlot(__instance?.id) ||
            __instance?.id == "new_religion" && ReligionSphere.BlocksFounding(pActor) ||
            __instance?.id == "new_language" && VanillaIdentitySystem.TryReuseLanguage(pActor))
        {
            __result = false;
            return false;
        }
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
