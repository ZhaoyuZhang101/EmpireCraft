using EmpireCraft.Scripts.GameClassExtensions;
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
            if (item.isInEmpire()) continue;
            if (!item.isNeighbourWith(pKingdomStarter)) continue;
            if (item.hasKing() && !item.isSupreme() && !item.king.hasPlot() && pKingdomStarter.isOpinionTowardsKingdomGood(item) && item.getRenown() >= PlotsLibrary.alliance_create.min_renown_kingdom)
            {
                if (item == null) continue;
                bool flag = false;
                if (pKingdomStarter.cities.Count <= 2 && item.cities.Count <= 2 && !pKingdomStarter.hasNearbyKingdoms() && !item.hasNearbyKingdoms())
                {
                    flag = true;
                }
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
        }
        __result = null;
        return false;
    }

    static bool get_war_target(Kingdom pInitiatorKingdom, ref Kingdom __result)
    {
        Kingdom tBestTarget = null;
        float tBestFastDist = float.MaxValue;
        int tCurrentArmy = pInitiatorKingdom.countTotalWarriors();
        if (pInitiatorKingdom.hasAlliance())
        {
            tCurrentArmy = pInitiatorKingdom.getAlliance().countWarriors();
        } else if (pInitiatorKingdom.isEmpire())
        {
            tCurrentArmy = pInitiatorKingdom.GetEmpire().countWarriors();
        }
        Kingdom result;
        using (ListPool<Kingdom> tPossibleKingdomsList = DiplomacyHelpers.wars.getNeutralKingdoms(pInitiatorKingdom, false, false))
        {
            foreach (Kingdom ptr in tPossibleKingdomsList)
            {
                Kingdom tTargetKingdom = ptr;
                if (tTargetKingdom.hasCities() && tTargetKingdom.hasCapital() && tTargetKingdom.getAge() >= SimGlobals.m.minimum_kingdom_age_for_attack)
                {
                    int tTargetArmy;
                    if (tTargetKingdom.hasAlliance())
                    {
                        tTargetArmy = tTargetKingdom.getAlliance().countWarriors();
                    } else if (tTargetKingdom.isEmpire())
                    {
                        tTargetArmy = tTargetKingdom.GetEmpire().countWarriors();
                    }
                    else
                    {
                        tTargetArmy = tTargetKingdom.countTotalWarriors();
                    }
                    if (tCurrentArmy >= tTargetArmy && 
                        pInitiatorKingdom.capital.reachableFrom(tTargetKingdom.capital) && 
                        (float)Date.getYearsSince(DiplomacyHelpers.diplomacy.getRelation(pInitiatorKingdom, tTargetKingdom).data.timestamp_last_war_ended) >= (float)SimGlobals.m.minimum_years_between_wars && 
                        !pInitiatorKingdom.isOpinionTowardsKingdomGood(tTargetKingdom))
                    {
                        float tFastDist = Kingdom.distanceBetweenKingdom(pInitiatorKingdom, tTargetKingdom);
                        if (tFastDist < tBestFastDist)
                        {
                            tBestFastDist = tFastDist;
                            tBestTarget = tTargetKingdom;
                        }
                    }
                }
            }
            result = tBestTarget;
        }
        __result = result;
        return false;
    }

}
