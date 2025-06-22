using EmpireCraft.Scripts.Enums;
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
    public List<long> want_acuired_title { get; set; }
    public List<long> owned_title { get; set; }
    public string founder_actor_name { get; set; }
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;
    public string founder_kingdom_name { get; set; }
    public string year_name = "";
    public List<EmpireCraftHistory> history = new List<EmpireCraftHistory>();
    public EmpireCraftHistory currentHistory {  get; set; }
    public EmpirePeriod empirePeriod {  get; set; }

    [DefaultValue(-1L)]
    public long founder_kingdom_id { get; set; } = -1L;
    public long emperor { get; set; } = -1L;
    public long empire_clan { get; set; } = -1L;

    public List<long> kingdoms;
    public List<string> history_emperrors;

    public long empire;

    public double timestamp_member_joined;
    public double timestamp_established_time;
    public double newEmperor_timestamp { get; set; }

}

public class EmpireCraftHistory
{
    public long id { get; set; }
    public string yea_name { get; set; }
    public string emperor { get; set; }
    public string miaohao_name { get; set; }
    public string shihao_name { get; set; }

    public List<string> descriptions;
    public List<string> cities;
}
