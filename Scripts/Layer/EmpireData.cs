using EmpireCraft.Scripts;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using UnityEngine;

// Token: 0x0200023D RID: 573
public class EmpireData : MetaObjectData
{
    public string motto { get; set; }
    public int banner_background_id { get; set; }
    public long empire_specific_clan { get; set; } = -1L;
    public float TaxRate = 0.2f;
    public int banner_icon_id { get; set; }
    public int Mandate { get; set; } = 100; //正统
    public List<long> CabinetMembers { get; set; } = new List<long>();
    public long Religion { get; set; } = -1L;
    public int Prestige { get; set; } = 100; //威望
    public EmpireHeirLawType heir_type { get; set; }
    public int max_province_city_num { get; set; } = 3;
    public List<int> PreviousYearsMoney = new();
    public double MilitaryExpenditureRate { get; set; } = 0.2;
    public int MilitaryExpenditure = 0;
    public bool original_royal_been_changed { get; set; } = false;
    public bool feed_royal = false;
    public double original_royal_been_changed_timestamp { get; set; }
    public string founder_actor_name { get; set; }
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;
    public string founder_kingdom_name { get; set; }
    public string year_name = "";
    public List<EmpireCraftHistory> history = new List<EmpireCraftHistory>();
    public EmpireCraftHistory currentHistory {  get; set; }
    public EmpirePeriod empirePeriod {  get; set; }
    public bool is_been_controlled { get; set; } = false;
    //岁币国
    public List<long> given_Kingdoms = new List<long>();
    //朝贡国
    public List<long> taken_Kingdoms = new List<long>();
    public EmpireAddition additions = new EmpireAddition();
    [DefaultValue(-1L)]
    public long founder_kingdom_id { get; set; } = -1L;

    public string directPre = "";
    public long emperor { get; set; } = -1L;
    public long empire_clan { get; set; } = -1L;

    public List<long> kingdoms;
    public List<long> cities;
    public List<string> history_emperrors = new List<string>();

    public bool is_allow_normal_to_exam = true; 
    public bool has_year_name = false;

    public long empire;
    public long original_capital;

    public double timestamp_member_joined;
    public double timestamp_established_time;
    public double timestamp_invite_war_cool_down;
    //岁币时间戳
    public double timestamp_given_time;
    public CenterOffice centerOffice { get; set; }
    public double newEmperor_timestamp { get; set; }

    public double last_exam_timestamp { get; set; } = -1L;
    public double last_select_lovers_timestamp { get; set; } = -1L;

    public double last_office_exam_timestamp { get; set; } = -1L;
    
    public double last_educate_timestamp { get; set; } = -1L;

}

public enum RoyalFeedLevel
{
    None,
    Low,
    Middle,
    High,
    Extreme
}
public class EmpireAddition
{
    public Dictionary<OfficerPowerType, int> addition =  OfficeManager.AllPower.ToDictionary(t => t, t => 0);
    //内阁加成
    public float cabinet_acc = 0;

    public void Add(EmpireAddition other)
    {
        foreach (var kv in other.addition)
            this.addition[kv.Key] += kv.Value;

        this.cabinet_acc += other.cabinet_acc;
    }

    public string toString()
    {
        var content = "";
        foreach (var kv in addition)
        {
            content += $"{kv.Key}: {kv.Value.ToString().ColorString(pColor:kv.Value>=0? Color.green : Color.red)}\n";
        }
        return content;
    }
}
public class EmpireCraftHistory
{
    public long id { get; set; }
    public string empire_name { get; set; }
    public bool is_first { get; set; } = false;
    public string year_name { get; set; }
    public string emperor { get; set; }
    public string miaohao_name { get; set; }
    public string miaohao_suffix { get; set; }
    public string shihao_name { get; set; }
    public int total_time { get; set; }

    public List<string> descriptions;
    public List<string> cities;
}

public class EmpireCore
{
    public long id { get; set; }
    public string culture { get; set; }
    public string name { get; set; }
    public bool hasPostHumous { get; set; }
    public long create_timestamp { get; set; }
    public List<long> cities;
}
