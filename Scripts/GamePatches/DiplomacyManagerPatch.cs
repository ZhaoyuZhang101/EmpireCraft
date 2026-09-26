using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
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
        Kingdom best = null;
        float bestDistance = float.MaxValue;
        if (pInitiatorKingdom?.capital == null)
        {
            __result = null;
            return false;
        }
        using (ListPool<Kingdom> neutral = DiplomacyHelpers.wars.getNeutralKingdoms(pInitiatorKingdom, false, false))
        {
            foreach (Kingdom target in neutral)
            {
                if (target == null || !target.hasCities() || !target.hasCapital() ||
                    target.getAge() < SimGlobals.m.minimum_kingdom_age_for_attack ||
                    !pInitiatorKingdom.capital.reachableFrom(target.capital) ||
                    Date.getYearsSince(DiplomacyHelpers.diplomacy.getRelation(pInitiatorKingdom, target)
                        .data.timestamp_last_war_ended) < SimGlobals.m.minimum_years_between_wars ||
                    !FeudalVassalService.CanDeclareExternalWar(pInitiatorKingdom, target)) continue;
                float distance = Kingdom.distanceBetweenKingdom(pInitiatorKingdom, target);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = target;
            }
        }
        __result = best;
        return false;
    }

}
