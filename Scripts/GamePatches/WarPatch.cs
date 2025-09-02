using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using static UnityEngine.UI.CanvasScaler;

namespace EmpireCraft.Scripts.GamePatches;
public class WarPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        // UnitWindow类的补丁
        new Harmony(nameof(start_new_war)).Patch(
            AccessTools.Method(typeof(DiplomacyManager), nameof(DiplomacyManager.startWar)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(start_new_war))
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
        new Harmony(nameof(update)).Patch(
            AccessTools.Method(typeof(War), nameof(War.update)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(update))
        );
        LogService.LogInfo("战争补丁加载成功");
    }
    public static bool update(War __instance)
    {
        if (__instance.main_attacker == null || __instance.main_defender == null)
        {
            foreach (var attacker in __instance._hashset_attackers)
            {
                if (!attacker.isRekt())
                {
                    attacker.madePeace(__instance);
                }
            }
            __instance._hashset_attackers.Clear();
            foreach (var defender in __instance._hashset_defenders)
            {
                if (!defender.isRekt())
                {
                    defender.madePeace(__instance);
                }
            }
            __instance._hashset_defenders.Clear();
            __instance.endForSides(WarWinner.Nobody);
            return false;
        }
        if (__instance.hasEnded())
        {
            return false;
        }
        if (!__instance.main_attacker.isAlive())
        {
            __instance.lostWar(__instance.main_attacker);
            return false;
        }
        if (__instance.isTotalWar())
        {
            if (World.world.kingdoms.Count <= 1)
            {
                World.world.wars.endWar(__instance, WarWinner.Attackers);
                return false;
            }
        }
        else if (!__instance.main_defender.isAlive())
        {
            __instance.lostWar(__instance.main_defender);
            return false;
        }
        if (__instance.getAge() > 10 && !__instance.isTotalWar())
        {
            if (__instance.main_attacker.countCities() == 0)
            {
                __instance.lostWar(__instance.main_attacker);
                return false;
            }
            if (__instance.main_defender.countCities() == 0)
            {
                __instance.lostWar(__instance.main_defender);
                return false;
            }
        }
        for (int i = 0; i < __instance._list_attackers.Count; i++)
        {
            Kingdom kingdom = __instance._list_attackers[i];
            if (!kingdom.isAlive())
            {
                __instance.lostWar(kingdom);
                return false;
            }
        }
        if (!__instance.isTotalWar())
        {
            for (int i = 0; i < __instance._list_defenders.Count; i++)
            {
                Kingdom kingdom2 = __instance._list_defenders[i];
                if (!kingdom2.isAlive())
                {
                    __instance.lostWar(kingdom2);
                    return false;
                }
            }
        }
        if (__instance.isTotalWar())
        {
            if (__instance._list_attackers.Count == 0 || World.world.kingdoms.Count == 1)
            {
                Debug.LogError("[1] should never happen here");
            }
        }
        else if (__instance._list_attackers.Count == 0 || __instance._list_defenders.Count == 0)
        {
            Debug.LogError("[2] should never happen here");
        }

        return false;
    }
    public static void removeData(War __instance)
    {
        __instance.RemoveExtraData<War, WarExtraData>();
    }

    public static void start_new_war(DiplomacyManager __instance, Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset, bool pLog, ref War __result)
    {
        if (pDefender.isInEmpire() || pAttacker.isInEmpire())
        {
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
                        if (kingdom.getWars().Count()>=0)
                        {
                            foreach(War w in kingdom.getWars())
                            {
                                w.endForSides(WarWinner.Nobody);
                            }
                        }
                        __result.joinAttackers(kingdom);
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
                    if (kingdom.isOpinionTowardsKingdomGood(pDefender) && kingdom != pAttacker)
                    {
                        if (kingdom.getWars().Count() >= 0)
                        {
                            foreach (War w in kingdom.getWars())
                            {
                                w.endForSides(WarWinner.Nobody);
                            }
                        }
                        __result.joinDefenders(kingdom);
                    }
                }
            }
        }
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
            Kingdom aKingdom = null;
            Kingdom dKingdom = null;
            aKingdom = pWar.getMainAttacker();
            dKingdom = pWar.getMainDefender();
            if (pWinner == WarWinner.Attackers)
            {
                if (aKingdom.isEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(30);
                    }
                    empire.AddRenown(30);
                }
                if (dKingdom.isEmpire())
                {
                    Empire empire = dKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(-50);
                    }
                    empire.AddRenown(-50);
                }
            } else if (pWinner == WarWinner.Defenders)
            {
                if (dKingdom.isEmpire())
                {
                    Empire empire = dKingdom.GetEmpire();
                    if (empire.Emperor!=null)
                    {
                        empire.Emperor.editRenown(30);

                    }
                    empire.AddRenown(30);
                }
                if (aKingdom.isEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(-50);
                    }
                    empire.AddRenown(-50);
                }
            }
            if (pWar.GetEmpireWarType() == EmpireWarType.AquireEmpire)
            {
                if (pWinner == WarWinner.Attackers)
                {
                    Kingdom kingdom = pWar.getMainAttacker();
                    kingdom.GetEmpire().ReplaceEmpire(kingdom);
                    TranslateHelper.LogministerAqcuireEmpire(kingdom.king, kingdom.GetEmpire());
                    return false;
                }
            }
            WorldLog.logWarEnded(pWar);
        }
        return false;
    }
}
