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

// Token: 0x0200023D RID: 573
public class EmpireData : MetaObjectData
{
    public string motto { get; set; }
    public int banner_background_id { get; set; }
    public int banner_icon_id { get; set; }
    public int Mandate { get; set; } = 100; //正统
    public int Prestige { get; set; } = 100; //威望
    public long Heir { get; set; } = -1L;
    public EmpireHeirLawType heir_type { get; set; }
    public int max_province_city_num { get; set; } = 3;
    public bool original_royal_been_changed { get; set; } = false;
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
    public bool has_year_name = false;

    public long empire;
    public long original_capital;

    public double timestamp_member_joined;
    public double timestamp_established_time;
    public CenterOffice centerOffice { get; set; } = new CenterOffice();
    public double newEmperor_timestamp { get; set; }

    public double last_exam_timestamp { get; set; } = -1L;

    public double last_office_exam_timestamp { get; set; } = -1L;

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

public class OfficeObject
{
    public double timestamp { get; set; }
    public OfficialLevel level { get; set; }
    public long actor_id { get; set; }
    public string pre { get; set; } = "";
    public bool is_place { get; set; } = false;

    public List<string> history_officers = new List<string>();
    public OfficeObject(OfficialLevel level, long actor_id, string pre="")
    {
        this.level = level;
        this.timestamp = World.world.getCurWorldTime();
        this.actor_id = actor_id;
        this.pre = pre;
        history_officers = new List<string> {};
    }

    public void SetActor (Actor actor)
    {
        this.actor_id = actor.getID();
        this.timestamp = World.world.getCurWorldTime();

    }

    public Actor GetActor()
    {
        return World.world.units.get(actor_id);
    }
    public int GetOnTime()
    {
        if (this.actor_id == -1L)
        {
            return 0;
        }
        if (GetActor() == null)
        {
            return 0;
        }
        return Date.getYearsSince(this.timestamp);
    }

    public void RemoveActor()
    {
        Actor actor = World.world.units.get(actor_id);
        if (actor!=null)
        {
            actor.addTrait("officerLeave");
            this.history_officers.Add(actor.data.name);
        }
        this.actor_id = -1L;
    }
}

public class CenterOffice
{
    public OfficeObject GreaterGeneral { get; set; } = new OfficeObject(OfficialLevel.officiallevel_1, -1L);//天策上将
    public OfficeObject Minister { get; set; } = new OfficeObject(OfficialLevel.officiallevel_2, -1L);//宰相
    public OfficeObject General { get; set; } = new OfficeObject(OfficialLevel.officiallevel_3, -1L);//大将军

    public Dictionary<string, OfficeObject> CoreOffices { get; set; } = new Dictionary<string, OfficeObject>();

    public Dictionary<string, OfficeObject> Divisions { get; set; } = new Dictionary<string, OfficeObject>();
    public CenterOffice(string species_id="Huaxia")
    {
        BeareauConfig config = OnomasticsRule.ALL_CULTURE_CONFIG[species_id];
        foreach(OfficeConfig oc in config.CoreOffice)
        {
            this.CoreOffices[oc.name] = new OfficeObject(oc.type, -1L, oc.pre);
        }
        foreach(OfficeConfig oc in config.Divisions)
        {
            this.Divisions[oc.name] = new OfficeObject(oc.type, -1L, oc.pre);
        }
    }

    public List<Actor> GetAllOfficers(Empire empire)
    {
        List<Actor> officers = new List<Actor>();
        foreach(Kingdom kingdom in empire.kingdoms_list)
        {
            officers.AddRange(kingdom.units.ToList().FindAll(a=>a.hasTrait("officer")));
        }
        return officers;
    }

    public void add(ref List<long> list, long content)
    {
        if (content != -1L)
        {
            if (!list.Contains(content))
            {
                list.Add(content);
            }
        }
    }
}
