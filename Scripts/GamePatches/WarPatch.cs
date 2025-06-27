using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.TipAndLog;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UnityEngine.UI.CanvasScaler;

namespace EmpireCraft.Scripts.GamePatches;
public class WarPatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        // UnitWindow类的补丁
        new Harmony(nameof(start_new_war)).Patch(
            AccessTools.Method(typeof(DiplomacyManager), nameof(DiplomacyManager.startWar)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(start_new_war))
        );
        // UnitWindow类的补丁
        new Harmony(nameof(end_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.endWar)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(end_war))
        );
        // UnitWindow类的补丁
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(War), nameof(War.Dispose)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(removeData))
        );
        LogService.LogInfo("战争补丁加载成功");
    }

    public static void removeData(War __instance)
    {
        __instance.RemoveExtraData();
    }

    public static bool start_new_war(DiplomacyManager __instance, Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset, bool pLog, ref War __result)
    {
        pLog = false;
        if (pAsset.total_war)
        {
            __result = __instance.startTotalWar(pAttacker, pAsset);
            return false;
        }
        if (pAttacker == pDefender)
        {
            __result = null;
            return false;
        }
        if (World.world.wars.getWar(pAttacker, pDefender) != null)
        {
            __result = null;
            return false;
        }
        War war = World.world.wars.newWar(pAttacker, pDefender, pAsset);
        if (pAttacker.isEmpire())
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_war, pAttacker.GetEmpire().name, pDefender.name)
            {
                location = pAttacker.location,
                color_special1 = pAttacker.kingdomColor.getColorText(),
                color_special2 = pDefender.kingdomColor.getColorText()
            }.add();
            foreach (var kingdom in pAttacker.GetEmpire().kingdoms_hashset)
            {
                if (kingdom.isOpinionTowardsKingdomGood(pAttacker) && kingdom != pDefender)
                {
                    war.joinAttackers(kingdom);
                    if (kingdom.hasAlliance())
                    {
                        if (kingdom.getAlliance().hasKingdom(pDefender))
                        {
                            kingdom.allianceLeave(kingdom.getAlliance());
                        }
                    }
                }
            }
        }
        if (pDefender.isInEmpire()) 
        {
            foreach (var kingdom in pDefender.GetEmpire().kingdoms_hashset)
            {
                if (kingdom.isOpinionTowardsKingdomGood(pDefender)&& kingdom != pAttacker)
                {
                    war.joinDefenders(kingdom);
                }
            }
        }
        return true;
    }

    public static bool end_war(WarManager __instance, War pWar, WarWinner pWinner = WarWinner.Nobody)
    {
        if (pWar.isAlive() && !pWar.hasEnded())
        {
            World.world.game_stats.data.peacesMade++;
            World.world.map_stats.peacesMade++;
            pWar.setWinner(pWinner);
            __instance.warStateChanged();
            pWar.endForSides(pWinner);
            pWar.data.died_time = World.world.getCurWorldTime();
            if (pWar.GetEmpireWarType() == EmpireWarType.AquireEmpire)
            {
                if (pWinner == WarWinner.Attackers)
                {
                    Kingdom kingdom = pWar.getMainAttacker();
                    kingdom.GetEmpire().replaceEmpire(kingdom);
                    TranslateHelper.LogMinistorAqcuireEmpire(kingdom.king, kingdom.GetEmpire());
                    return false;
                }
            }
            WorldLog.logWarEnded(pWar);
        }
        return false;
    }
}
