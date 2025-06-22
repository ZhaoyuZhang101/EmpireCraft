using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc;
public static class HistoryRecordSystem
{
    public static void record(EmpireHistoryType type, Empire empire, string entity1=null, string eneity2=null, string eneity3=null, string eneity4=null)
    {
        string id = "";
        switch (type) 
        {
            case EmpireHistoryType.称帝建业:
                id = "history_new_empire";
                break;
            case EmpireHistoryType.新皇登基:
                id = "history_new_emperor";
                break;
            case EmpireHistoryType.权臣摄政:
                id = "history_powerful_ministor";
                break;
            case EmpireHistoryType.追封先帝:
                id = "history_name_previous_emperor";
                break;
            case EmpireHistoryType.臣服于帝国:
                id = "histroy_be_vassal";
                break;
            case EmpireHistoryType.迁都:
                id = "history_change_capital";
                break;
            case EmpireHistoryType.还于旧都:
                id = "history_back_to_original_capital";
                break;
            case EmpireHistoryType.另立朝廷:
                id = "history_refund_empire";
                break;
            default:
                break;
        }
    }
}
