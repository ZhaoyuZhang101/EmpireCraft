using System.Collections.Generic;
using System.Text.RegularExpressions;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.System;
public static class HistoryRecordSystem
{
    public static void RecordHistory(this Empire empire, EmpireHistoryType type, Dictionary<string, string> recordInfo)
    {
        if (empire == null || empire.isRekt()) return;
        if (empire.data == null) return;
        recordInfo ??= new Dictionary<string, string>();
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
            case EmpireHistoryType.kingdom_join_empire_history:
                id = "history_kingdom_join_empire";
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
            case EmpireHistoryType.kingdom_attack_for_title_history:
                id = "history_kingdom_attack_for_title";
                break;
            case EmpireHistoryType.kingdom_change_capital_to_title_history:
                id = "history_kingdom_change_capital_to_title";
                break;
            case EmpireHistoryType.destroy_title_history:
                id = "history_destroy_title";
                break;
            case EmpireHistoryType.create_title_history:
                id = "history_create_title";
                break;
            case EmpireHistoryType.city_add_to_title_history:
                id = "history_city_add_to_title";
                break;
            case EmpireHistoryType.king_take_title_history:
                id = "history_king_take_title";
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
            case EmpireHistoryType.war_join_attacker_history:
                id = "history_war_join_attacker";
                break;
            case EmpireHistoryType.war_join_defender_history:
                id = "history_war_join_defender";
                break;
            case EmpireHistoryType.city_captured_history:
                id = "history_city_captured";
                break;
            case EmpireHistoryType.city_lost_history:
                id = "history_city_lost";
                break;
            case EmpireHistoryType.city_plundered_history:
                id = "history_city_plundered";
                break;
            case EmpireHistoryType.join_taken_alliance_history:
                id = "history_join_taken_alliance";
                break;
            case EmpireHistoryType.leave_taken_alliance_history:
                id = "history_leave_taken_alliance";
                break;
            case EmpireHistoryType.join_given_alliance_history:
                id = "history_join_given_alliance";
                break;
            case EmpireHistoryType.leave_given_alliance_history:
                id = "history_leave_given_alliance";
                break;
            case EmpireHistoryType.officer_join_faction_history:
                id = "history_officer_join_faction";
                break;
            case EmpireHistoryType.officer_become_faction_leader_history:
                id = "history_officer_become_faction_leader";
                break;
            case EmpireHistoryType.officer_build_specific_clan_history:
                id = "history_officer_build_specific_clan";
                break;
            case EmpireHistoryType.king_choose_heir_history:
                id = "history_king_choose_heir";
                break;
            case EmpireHistoryType.province_change_to_kingdom_history:
                id = "history_province_change_to_kingdom";
                break;
            case EmpireHistoryType.new_jingshi_history:
                id = "history_new_jingshi";
                break;
            case EmpireHistoryType.emperor_new_year_name_history:
                id = "history_emperor_new_year_name";
                break;
            case EmpireHistoryType.minister_acquire_empire_history:
                id = "history_minister_acquire_empire";
                break;
            case EmpireHistoryType.restore_historical_empire_history:
                id = "history_restore_historical_empire";
                break;
            case EmpireHistoryType.emperor_posthumous_name_history:
                id = "history_emperor_posthumous_name";
                break;
            case EmpireHistoryType.become_kingdom_history:
                id = "history_become_kingdom";
                break;
            case EmpireHistoryType.combine_kingdom_history:
                id = "history_combine_kingdom";
                break;
            case EmpireHistoryType.religion_war_transfer_history:
                id = "history_religion_war_transfer";
                break;
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
                    dynasty_name = empire.GetEmpireName(),
                    royal_surname = empire.Emperor?.GetSpecificClan()?.name ?? "",
                    year_name = empire.data.year_name,
                    emperor = empire.Emperor?.getName() ?? "",
                    descriptions = new List<string>(),
                    cities = new List<string>(),
                    is_first = false
                };
            }
            empire.data.currentHistory.descriptions.Add(empire.GetYearNameWithTime()+ "_" + replacedText);
        }
    }
    
    public static void RecordNewEmperorHistory(this Empire empire, bool isNew)
    {
        //记录历史
        empire.data.currentHistory = new EmpireCraftHistory
        {
            id = empire.Emperor.data.id,
            empire_name = empire.GetEmpireName(),
            dynasty_name = empire.GetEmpireName(),
            royal_surname = empire.Emperor.GetSpecificClan()?.name??"",
            year_name = empire.data.year_name,
            emperor = empire.Emperor.getName(),
            miaohao_name = "",
            shihao_name = "",
            descriptions = new List<string>(),
            cities = new List<string>(),
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
