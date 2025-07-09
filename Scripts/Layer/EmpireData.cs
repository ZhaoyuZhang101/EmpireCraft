using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.HelperFunc;
using System;
using System.Collections.Generic;
using System.ComponentModel;

// Token: 0x0200023D RID: 573
public class EmpireData : MetaObjectData
{
    public string motto { get; set; }
    public int banner_background_id { get; set; }
    public int banner_icon_id { get; set; }
    public bool has_year_name { get; set; }
    public int max_province_city_num { get; set; } = 3;
    public bool original_royal_been_changed { get; set; } = false;
    public Office office { get; set; }
    public double original_royal_been_changed_timestamp { get; set; }
    public string founder_actor_name { get; set; }
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;
    public string founder_kingdom_name { get; set; }
    public string year_name = "";
    public List<EmpireCraftHistory> history = new List<EmpireCraftHistory>();
    public EmpireCraftHistory currentHistory {  get; set; }
    public EmpirePeriod empirePeriod {  get; set; }

    public ArmySystemType armySystemType { get; set; }

    [DefaultValue(-1L)]
    public long founder_kingdom_id { get; set; } = -1L;
    public long emperor { get; set; } = -1L;
    public long empire_clan { get; set; } = -1L;

    public List<long> kingdoms;
    public List<string> history_emperrors;

    public bool is_allow_normal_to_exam = true; 

    public long empire;
    public long original_capital;

    public double timestamp_member_joined;
    public double timestamp_established_time;
    public double newEmperor_timestamp { get; set; }

    public double last_exam_timestamp { get; set; } = -1L;

    public List<long> exam_pass_persons = new List<long>();
    public List<long> province_list = new List<long>();

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
