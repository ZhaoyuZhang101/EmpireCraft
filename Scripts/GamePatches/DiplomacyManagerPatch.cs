using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.AI;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class DiplomacyManagerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {

        new Harmony(nameof(get_war_target)).Patch(
            AccessTools.Method(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getWarTarget)),
            prefix: new HarmonyMethod(GetType(), nameof(get_war_target))
        );

        new Harmony(nameof(get_alliance_target)).Patch(
            AccessTools.Method(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getAllianceTarget)),
            prefix: new HarmonyMethod(GetType(), nameof(get_alliance_target))
        );
    }

    public static bool get_alliance_target(Kingdom pKingdomStarter, ref Kingdom __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pKingdomStarter)) return true;
        if (!FeudalVassalService.CanJoinAlliance(pKingdomStarter, null))
        {
            __result = null;
            return false;
        }
        if (pKingdomStarter.isSupreme())
        {
            __result = null;
            return false;
        }
        using ListPool<Kingdom> listPool = World.world.wars.getNeutralKingdoms(pKingdomStarter, pOnlyWithoutWars: true, pOnlyWithoutAlliances: true);
        if (!listPool.Any())
        {
            __result = null;
            return false;
        }
        foreach (Kingdom item in listPool.LoopRandom())
        {
            if (!FeudalVassalService.CanJoinAlliance(item, null) ||
                FeudalVassalService.GetOverlord(item) == pKingdomStarter ||
                FeudalVassalService.GetOverlord(pKingdomStarter) == item) continue;
            if (item.IsInEmpire()) continue;
            if (!item.IsNeighbourWith(pKingdomStarter)) continue;
            if (!item.hasKing() || item.isSupreme() || item.king.hasPlot() ||
                !pKingdomStarter.isOpinionTowardsKingdomGood(item) ||
                item.getRenown() < PlotsLibrary.alliance_create.min_renown_kingdom) continue;
            bool flag = false || pKingdomStarter.cities.Count <= 2 && item.cities.Count <= 2 && !pKingdomStarter.hasNearbyKingdoms() && !item.hasNearbyKingdoms();
            if (!flag && DiplomacyHelpers.areKingdomsClose(item, pKingdomStarter))
            {
                flag = true;
            }

            if (flag)
            {
                __result= item;
                return false;
            }
        }
        __result = null;
        return false;
    }

    static bool get_war_target(Kingdom pInitiatorKingdom, ref Kingdom __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pInitiatorKingdom)) return true;
        if (pInitiatorKingdom != null && EmpireCraftPlotsAddition.UsesVanillaWarPlot(
                pInitiatorKingdom.IsEmpire(), pInitiatorKingdom.IsInEmpire(),
                FeudalVassalService.GetOverlord(pInitiatorKingdom) != null)) return true;
        __result = EmpireCraftPlotsAddition.GetWarTarget(pInitiatorKingdom);
        return false;
    }

}
