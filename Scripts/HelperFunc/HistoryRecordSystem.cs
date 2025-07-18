﻿using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc;
public static class HistoryRecordSystem
{
    public static void RecordHistory(this Empire empire, EmpireHistoryType type, Dictionary<string, string> recordInfo)
    {
        string id = "";
        switch (type) 
        {
            case EmpireHistoryType.new_empire_history:
                id = "history_new_empire";
                break;
            case EmpireHistoryType.new_emperor_history:
                id = "history_new_emperor";
                break;
            case EmpireHistoryType.emperor_die_history:
                id = "histroy_empire_die";
                break;
            case EmpireHistoryType.emperor_left_history:
                id = "histroy_empire_left";
                break;
            case EmpireHistoryType.powerful_minister_history:
                id = "history_powerful_ministor";
                break;
            case EmpireHistoryType.give_posthumous_to_previous_emperor_history:
                id = "history_name_previous_emperor";
                break;
            case EmpireHistoryType.join_empire_history:
                id = "histroy_be_vassal";
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
                break;
        }
        if (id!=""||id!=null)
        {
            string template = LM.Get(id);
            string replacedText = Regex.Replace(template, @"\$(\w+)\$", m =>
            {
                var key = m.Groups[1].Value;
                return recordInfo.TryGetValue(key, out var v) ? v : m.Value;
            });
            empire.data.currentHistory.descriptions.Add(empire.getYearNameWithTime()+ "_" + replacedText);
        }
    }
}
