using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.System;
public static class HistoryRecordSystem
{
    private static long FindRecordActorId(Empire empire, Dictionary<string, string> recordInfo)
    {
        if (recordInfo != null && recordInfo.TryGetValue("actor_id", out string actorIdText) &&
            long.TryParse(actorIdText, out long actorId)) return actorId;
        if (recordInfo == null || !recordInfo.TryGetValue("actor", out string actorName) ||
            string.IsNullOrWhiteSpace(actorName)) return -1L;

        if (empire?.Emperor != null && (empire.Emperor.getName() == actorName || empire.Emperor.data.name == actorName))
        {
            return empire.Emperor.id;
        }
        Actor actor = World.world?.units?.FirstOrDefault(unit => unit != null &&
            (unit.getName() == actorName || unit.data.name == actorName));
        return actor?.id ?? -1L;
    }

    private static List<string> GetPreviousFinalCities(this Empire empire)
    {
        if (empire?.data?.history == null || empire.data.history.Count <= 0)
        {
            return new List<string>();
        }

        for (int i = empire.data.history.Count - 1; i >= 0; i--)
        {
            var history = empire.data.history[i];
            if (history?.descriptions == null || history.descriptions.Count <= 0)
            {
                continue;
            }

            for (int j = history.descriptions.Count - 1; j >= 0; j--)
            {
                var description = history.descriptions[j];
                if (description?.cities == null)
                {
                    continue;
                }

                return new List<string>(description.cities);
            }
        }

        return new List<string>();
    }

    public static void RecordHistory(this Empire empire, EmpireHistoryType type = default, Dictionary<string, string> recordInfo = null,
        string directContent = null, long actorId = -1L, long kingdomId = -1L)
    {
        if (empire == null || empire.isRekt()) return;
        if (empire.data == null) return;
        recordInfo ??= new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(directContent))
        {
            LogService.LogInfo(directContent);
            if (empire.data.currentHistory == null)
            {
                empire.data.currentHistory = new EmpireCraftHistory
                {
                    id = empire.Emperor?.data.id ?? -1L,
                    empire_name = empire.GetEmpireName(),
                    empire_full_name = empire.GetEmpireFullName(),
                    dynasty_name = empire.GetEmpireName(),
                    royal_surname = empire.Emperor?.GetSpecificClan()?.name ?? "",
                    year_name = empire.data.year_name,
                    emperor = empire.Emperor?.getName() ?? "",
                    descriptions = new List<HistoryDescription>(),
                    is_first = false
                };
            }
            var description = new HistoryDescription
            {
                time = empire.GetYearNameWithTime(),
                cities = empire.cities_list.Select(c=>c.name).ToList(),
                description = directContent,
                timestamp = World.world.getCurWorldTime(),
                actor_id = actorId,
                kingdom_id = kingdomId
            };
            empire.data.currentHistory.descriptions.Add(description);
            return;
        }
        string id = "";
        switch (type) 
        {
            case EmpireHistoryType.new_empire_history_west:
                id = "history_new_empire_west";
                break;
            case EmpireHistoryType.new_empire_history:
                id = "history_new_empire";
                break;
            case EmpireHistoryType.new_emperor_history_west:
                id = "history_new_emperor_west";
                break;
            case EmpireHistoryType.new_emperor_history:
                id = "history_new_emperor";
                break;
            case EmpireHistoryType.emperor_die_history:
                id = "history_empire_die";
                break;
            case EmpireHistoryType.emperor_left_history:
                id = "history_empire_left";
                break;
            case EmpireHistoryType.powerful_minister_history:
                id = "history_powerful_minister";
                break;
            case EmpireHistoryType.give_posthumous_to_previous_emperor_history:
                id = "history_name_previous_emperor";
                break;
            case EmpireHistoryType.join_empire_history:
                id = "history_be_vassal";
                break;
            case EmpireHistoryType.change_capital_history:
                id = "history_change_capital";
                break;
            case EmpireHistoryType.back_to_original_capital_history:
                id = "history_back_to_original_capital";
                break;
            case EmpireHistoryType.rebuild_empire_history:
                id = "history_refund_empire";
                break;
            case EmpireHistoryType.war_declared_history:
                id = "history_war_declared";
                break;
            case EmpireHistoryType.war_ended_attacker_victory_history:
                id = "history_war_ended_attacker_victory";
                break;
            case EmpireHistoryType.war_ended_defender_victory_history:
                id = "history_war_ended_defender_victory";
                break;
            case EmpireHistoryType.war_ended_peace_history:
                id = "history_war_ended_peace";
                break;
            case EmpireHistoryType.join_taken_alliance_history:
                id = "history_join_taken_alliance";
                break;
            case EmpireHistoryType.leave_taken_alliance_history:
                id = "history_leave_taken_alliance";
                break;
            default:
                return;
        }
        if (!string.IsNullOrEmpty(id))
        {
            string template = LM.Get(id) ?? "";
            string replacedText = Regex.Replace(template, @"\$(\w+)\$", m =>
            {
                var key = m.Groups[1].Value;
                return recordInfo.TryGetValue(key, out var v) ? v : m.Value;
            });
            if (empire.data.currentHistory == null)
            {
                empire.data.currentHistory = new EmpireCraftHistory
                {
                    id = empire.Emperor?.data.id ?? -1L,
                    empire_name = empire.GetEmpireName(),
                    empire_full_name = empire.GetEmpireFullName(),
                    dynasty_name = empire.GetEmpireName(),
                    royal_surname = empire.Emperor?.GetSpecificClan()?.name ?? "",
                    year_name = empire.data.year_name,
                    emperor = empire.Emperor?.getName() ?? "",
                    descriptions = new List<HistoryDescription>(),
                    is_first = false
                };
            }
            var description = new HistoryDescription
            {
                time = empire.GetYearNameWithTime(),
                cities = empire.cities_list.Select(c=>c.name).ToList(),
                description = replacedText,
                timestamp = World.world.getCurWorldTime(),
                actor_id = actorId > 0 ? actorId : FindRecordActorId(empire, recordInfo),
                kingdom_id = kingdomId
            };
            empire.data.currentHistory.descriptions.Add(description);
        }
    }
    
    public static void RecordNewEmperorHistory(this Empire empire, bool isNew)
    {
        //记录历史
        empire.data.currentHistory = new EmpireCraftHistory
        {
            id = empire.Emperor.data.id,
            empire_name = empire.GetEmpireName(),
            empire_full_name = empire.GetEmpireFullName(),
            dynasty_name = empire.GetEmpireName(),
            royal_surname = empire.Emperor.GetSpecificClan()?.name??"",
            year_name = empire.data.year_name,
            emperor = empire.Emperor.getName(),
            miaohao_name = "",
            shihao_name = "",
            initial_cities = empire.GetPreviousFinalCities(),
            descriptions = new List<HistoryDescription>(),
            is_first = isNew
        };
        if (empire.data.has_year_name)
        {
            empire.RecordHistory(EmpireHistoryType.new_emperor_history, new Dictionary<string, string>()
            {
                ["actor"] = empire.Emperor.getName(),
                ["place"] = empire.CoreKingdom.capital.GetCityName(),
                ["year_name"] = empire.data.year_name,
            });
        }
        else
        {
            empire.RecordHistory(EmpireHistoryType.new_emperor_history_west, new Dictionary<string, string>()
            {
                ["actor"] = empire.Emperor.getName(),
                ["place"] = empire.CoreKingdom.capital.GetCityName()
            });
        }
        
    }
}
