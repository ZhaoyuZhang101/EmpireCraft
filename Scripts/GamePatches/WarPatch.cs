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
        new Harmony(nameof(join_war_side)).Patch(
            AccessTools.Method(typeof(War), nameof(War.joinAttackers), new[] { typeof(Kingdom) }),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(join_war_side))
        );
        new Harmony(nameof(join_war_side) + "_defenders").Patch(
            AccessTools.Method(typeof(War), nameof(War.joinDefenders), new[] { typeof(Kingdom) }),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(join_war_side))
        );
        LogService.LogInfo("战争补丁加载成功");
    }
    
    public static void update(War __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        RecordWarDeclared(__instance);
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        __instance.RemoveExtraData<War, WarExtraData>();
    }

    public static bool end_war(WarManager __instance, War pWar, WarWinner pWinner = WarWinner.Nobody)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pWar)) return true;
        if (pWar.isAlive() && !pWar.hasEnded())
        {
            RecordWarDeclared(pWar);
            CaptureWarRoyalHouses(pWar);
            RememberDefeatedWarHouses(pWar, pWinner);
            RecordWarEnded(pWar, pWinner);
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
            foreach (var a in pWar._list_attackers)
            {
                a.cities.ForEach(c=>c.ClearOccupiedStatus());
            }
            foreach (var d in pWar._list_defenders)
            {
                d.cities.ForEach(c=>c.ClearOccupiedStatus());
            }
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
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__result)) return;
        if (__result == null) return;
        RecordWarDeclared(__result);
        DetachHostileTributaries(__result);
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

    public static void join_war_side(War __instance)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        CaptureWarRoyalHouses(__instance);
        DetachHostileTributaries(__instance);
    }

    private static void CaptureWarRoyalHouses(War war)
    {
        if (war == null || war.getMainAttacker() == null || war.getMainDefender() == null) return;
        WarExtraData extra = war.GetOrCreate();
        extra.attacker_empire_houses ??= new List<DefeatedEmpireHouse>();
        extra.defender_empire_houses ??= new List<DefeatedEmpireHouse>();
        CaptureSideRoyalHouses(war._list_attackers, extra.attacker_empire_houses);
        CaptureSideRoyalHouses(war._list_defenders, extra.defender_empire_houses);
        extra.royal_houses_recorded = true;
    }

    private static void CaptureSideRoyalHouses(IEnumerable<Kingdom> side, List<DefeatedEmpireHouse> houses)
    {
        if (side == null) return;
        foreach (Kingdom kingdom in side)
        {
            Empire empire = kingdom?.GetEmpire();
            // A provincial war alone is not proof that its entire empire was defeated.
            if (empire == null || empire.CoreKingdom != kingdom || empire.IsArchived()) continue;
            long emperorId = empire.Emperor?.id ?? -1L;
            long clanId = empire.EmpireSpecificClan?.id ?? -1L;
            if (houses.Any(h => h != null && h.empire_id == empire.id &&
                h.emperor_id == emperorId && h.royal_clan_id == clanId)) continue;
            houses.Add(new DefeatedEmpireHouse
            {
                empire_id = empire.id, emperor_id = emperorId, royal_clan_id = clanId
            });
        }
    }

    private static void RememberDefeatedWarHouses(War war, WarWinner winner)
    {
        if (winner != WarWinner.Attackers && winner != WarWinner.Defenders) return;
        WarExtraData extra = war.GetOrCreate();
        var winners = winner == WarWinner.Attackers ? extra.attacker_empire_houses : extra.defender_empire_houses;
        var defeated = winner == WarWinner.Attackers ? extra.defender_empire_houses : extra.attacker_empire_houses;
        var winningSide = winner == WarWinner.Attackers ? war._list_attackers : war._list_defenders;
        var winningIds = new HashSet<long>(winners.Where(h => h != null).Select(h => h.empire_id));
        foreach (long empireId in winningIds)
        {
            Empire empire = ModClass.EMPIRE_MANAGER.get(empireId);
            if (empire == null || empire.IsArchived() || empire.isRekt() ||
                !winningSide.Contains(empire.CoreKingdom)) continue;
            foreach (DefeatedEmpireHouse house in defeated)
            {
                if (house != null && !winningIds.Contains(house.empire_id))
                    empire.RememberDefeatedEmpireHouse(house);
            }
        }
    }

    private static void DetachHostileTributaries(War war)
    {
        if (war == null || war.hasEnded()) return;
        DetachTributariesOnSide(war._list_attackers, war._list_defenders);
        DetachTributariesOnSide(war._list_defenders, war._list_attackers);
    }

    private static void DetachTributariesOnSide(IEnumerable<Kingdom> tributarySide, IEnumerable<Kingdom> enemySide)
    {
        if (tributarySide == null || enemySide == null) return;
        foreach (Kingdom tributary in tributarySide)
        {
            if (tributary == null || tributary.isRekt() || !tributary.HasTakenAlliance()) continue;
            Empire overlord = tributary.GetTakenAllianceEmpire();
            if (overlord == null || overlord.IsArchived())
            {
                tributary.RemoveTakenAlliance();
                continue;
            }

            foreach (Kingdom enemy in enemySide)
            {
                if (enemy == null || enemy.isRekt()) continue;
                if (enemy == overlord.CoreKingdom || enemy.GetEmpire() == overlord)
                {
                    tributary.RemoveTakenAlliance();
                    break;
                }
            }
        }
    }

    private static void RecordWarDeclared(War war)
    {
        if (war == null || war.hasEnded()) return;
        WarExtraData extraData = war.GetOrCreate();
        if (!extraData.royal_houses_recorded) CaptureWarRoyalHouses(war);
        if (extraData.history_declaration_recorded) return;
        if (war.getMainAttacker() == null || war.getMainDefender() == null) return;

        extraData.history_declaration_recorded = true;
        RecordWarHistory(war, EmpireHistoryType.war_declared_history);
    }

    private static void RecordWarEnded(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtraData extraData = war.GetOrCreate();
        if (extraData.history_end_recorded) return;

        extraData.history_end_recorded = true;
        EmpireHistoryType historyType = winner switch
        {
            WarWinner.Attackers => EmpireHistoryType.war_ended_attacker_victory_history,
            WarWinner.Defenders => EmpireHistoryType.war_ended_defender_victory_history,
            _ => EmpireHistoryType.war_ended_peace_history
        };
        RecordWarHistory(war, historyType);
    }

    private static void RecordWarHistory(War war, EmpireHistoryType historyType)
    {
        Kingdom mainAttacker = war.getMainAttacker();
        Kingdom mainDefender = war.getMainDefender();
        if (mainAttacker == null || mainDefender == null) return;

        var recordInfo = new Dictionary<string, string>
        {
            ["attacker"] = GetPolityHistoryName(mainAttacker),
            ["defender"] = GetPolityHistoryName(mainDefender),
            ["type"] = GetWarHistoryType(war)
        };
        Actor attackerLeader = GetSideLeader(war._list_attackers, mainAttacker);
        Actor defenderLeader = GetSideLeader(war._list_defenders, mainDefender);
        var recordedEmpires = new HashSet<long>();
        RecordWarHistoryForSide(war._list_attackers, recordInfo, defenderLeader, mainDefender, historyType,
            recordedEmpires);
        RecordWarHistoryForSide(war._list_defenders, recordInfo, attackerLeader, mainAttacker, historyType,
            recordedEmpires);
    }

    private static void RecordWarHistoryForSide(IEnumerable<Kingdom> side, Dictionary<string, string> recordInfo,
        Actor enemyLeader, Kingdom enemyKingdom, EmpireHistoryType historyType, HashSet<long> recordedEmpires)
    {
        if (side == null) return;
        foreach (Kingdom kingdom in side)
        {
            if (kingdom == null || kingdom.isRekt()) continue;
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.IsArchived() || empire.isRekt() || !recordedEmpires.Add(empire.id)) continue;
            empire.RecordHistory(historyType, recordInfo, actorId: enemyLeader?.id ?? -1L,
                kingdomId: enemyKingdom?.id ?? -1L);
        }
    }

    private static Actor GetSideLeader(IEnumerable<Kingdom> side, Kingdom preferredKingdom)
    {
        Actor preferredLeader = GetKingdomLeader(preferredKingdom);
        if (preferredLeader != null) return preferredLeader;
        if (side == null) return null;
        foreach (Kingdom kingdom in side)
        {
            Actor leader = GetKingdomLeader(kingdom);
            if (leader != null) return leader;
        }
        return null;
    }

    private static Actor GetKingdomLeader(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return null;
        if (kingdom.king != null) return kingdom.king;
        if (kingdom.capital != null && kingdom.capital.hasLeader()) return kingdom.capital.leader;
        if (kingdom.cities == null) return null;
        foreach (City city in kingdom.cities)
        {
            if (city != null && city.hasLeader()) return city.leader;
        }
        return null;
    }

    private static string GetPolityHistoryName(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        string name = empire?.GetEmpireFullName();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        name = kingdom?.GetKingdomFullName();
        return string.IsNullOrWhiteSpace(name) ? kingdom?.name ?? "" : name;
    }

    private static string GetWarHistoryType(War war)
    {
        EmpireWarType type = war.GetEmpireWarType();
        if (type != EmpireWarType.None)
        {
            string localizedType = LocalizedTextManager.getText(type.ToString());
            if (!string.IsNullOrWhiteSpace(localizedType) && localizedType != type.ToString()) return localizedType;
            return type.ToString();
        }

        return string.IsNullOrWhiteSpace(war.data?.name) ? "战争" : war.data.name;
    }
}
