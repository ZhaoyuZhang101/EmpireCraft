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
using EmpireCraft.Scripts.System;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using static UnityEngine.UI.CanvasScaler;

namespace EmpireCraft.Scripts.GamePatches;
public class WarPatch: GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(end_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.endWar)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(end_war))
        );
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(War), nameof(War.Dispose)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(removeData))
        );
        new Harmony(nameof(update)).Patch(
            AccessTools.Method(typeof(War), nameof(War.update)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(update))
        );
        new Harmony(nameof(new_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.newWar)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(new_war))
        );
        LogService.LogInfo("战争补丁加载成功");
    }
    
    public static void update(War __instance)
    {
        if (__instance.getDuration() > ModClass.WAR_END_YEAR)
        {
            var attacker = __instance.getMainAttacker()?.king;
            if (attacker != null)
            {
                var plot = AssetManager.plots_library.basic_plots.Find(p => p.id == "force_stop_war");
                if (!attacker.plot?.isSameType(plot) ?? true)
                {
                    
                    plot?.try_to_start_advanced(attacker, plot, true);
                } 
            }
            var defender = __instance.getMainDefender()?.king;
            if (defender != null)
            {
                var plot = AssetManager.plots_library.basic_plots.Find(p => p.id == "force_stop_war");
                if (!defender.plot?.isSameType(plot) ?? true)
                {
                    
                    plot?.try_to_start_advanced(defender, plot, true);
                } 
            }
        }
    }
    public static void removeData(War __instance)
    {
        __instance.RemoveExtraData<War, WarExtraData>();
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
                if (aKingdom.IsEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(30);
                    }
                    empire.AddMandate(10);
                    empire.AddRenown(100);
                }
                if (dKingdom.IsEmpire())
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
                if (dKingdom.IsEmpire())
                {
                    Empire empire = dKingdom.GetEmpire();
                    if (empire.Emperor!=null)
                    {
                        empire.Emperor.editRenown(30);

                    }
                    empire.AddRenown(30);
                }
                if (aKingdom.IsEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(-50);
                    }
                    empire.AddRenown(-50);
                }
            }

            switch (pWar.GetEmpireWarType())
            {
                case EmpireWarType.获取帝国:
                    if (pWinner == WarWinner.Attackers)
                    {
                        Kingdom kingdom = pWar.getMainAttacker();
                        if (kingdom != null)
                        {
                            kingdom.GetEmpire().ReplaceEmpire(kingdom);
                            TranslateHelper.LogministerAqcuireEmpire(kingdom.king, kingdom.GetEmpire());
                        }
                        return false;
                    }
                    break;
                case EmpireWarType.派系叛乱:
                    Kingdom attacker = pWar.getMainAttacker();
                    if (pWinner == WarWinner.Attackers)
                    {
                        attacker.GetEmpire().ReplaceEmpire(attacker);
                    }
                    attacker.EndFactionRebelling();
                    break;
                case EmpireWarType.地方叛乱:
                case EmpireWarType.地方独立:
                    Kingdom attacker1 = pWar.getMainAttacker();
                    attacker1.EndLocalRebelling();
                    break;
                case EmpireWarType.索取法理:
                    KingdomTitle title = pWar.GetTitleTarget();
                    if (pWinner == WarWinner.Attackers)
                    {
                        if (title != null)
                        {
                            Kingdom kingdom = pWar.getMainAttacker();
                            if (kingdom != null)
                            {
                                title.SetOwner(kingdom.king);
                                kingdom.king.AddOwnedTitle(title);
                                TranslateHelper.LogKingTakeTitle(kingdom, title);
                            }
                        }
                        return false;
                    }
                    break;
            }
            WorldLog.logWarEnded(pWar);
        }
        return false;
    }
    public static void new_war(War __result)
    {
        if (__result == null) return;
        Kingdom aKingdom = __result.getMainAttacker();
        Kingdom dKingdom = __result.getMainDefender();
        if (aKingdom != null && aKingdom.IsEmpire())
        {
            Empire empire = aKingdom.GetEmpire();
        }
        if (dKingdom != null && dKingdom.IsEmpire())
        {
            Empire empire = dKingdom.GetEmpire();
        }
    }
}
