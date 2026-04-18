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
    public static void RecordHistory(this Empire empire, EmpireHistoryType type = default, Dictionary<string, string> recordInfo = null, string directContent=null)
    {
        LogService.LogInfo("开始记录历史");
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
                description = directContent
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
                description = replacedText
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
            dynasty_name = empire.GetEmpireName(),
            royal_surname = empire.Emperor.GetSpecificClan()?.name??"",
            year_name = empire.data.year_name,
            emperor = empire.Emperor.getName(),
            miaohao_name = "",
            shihao_name = "",
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
