using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes;
public enum OfficerPowerType
{
    宗教,
    天子智教,
    天子政教,
    天子护理,
    备选,
    拟定,
    审核,
    执行,
    人事,
    财政,
    礼仪,
    军事,
    司法,
    建设,
    教育
}
public class BureauSetting
{
    public string pre;
    public int type;
    public string description;
    public List<OfficerPowerType> powers;
    public int merit;
    public int honorary;
    public bool select_from_local;
    public LeaderSelectMethod leader_select_method;
    public List<string> require_traits;
}

public class BureauConfig
{
    public List<BureauSetting> cores;
    public List<BureauSetting> division;
    public Dictionary<KingdomType, BureauSetting> kingdoms;
    public Dictionary<CityType, BureauSetting> cities;
}
public class OfficeObject
{
    public double timestamp { get; set; }
    public int officeType { get; set; }
    public long actor_id { get; set; }
    public string pre { get; set; } = "";
    public int merit { get; set; }
    public bool is_cabinet { get; set; } = false;//内阁
    public int honorary { get; set; }
    [JsonIgnore]
    public NanoObject meta_object { get; set; } = null;

    public bool select_from_local { get; set; } = false;
    public LeaderSelectMethod leader_select_method { get; set; }
    public bool is_local { get; set; } = false;
    public RegimeType regimeType { get; set; }
    public List<string> require_traits { get; set; } = new List<string>();
    public List<string> history_officers = new List<string>();
    public string GetName(NanoObject pNano = null)
    {
        pre = string.IsNullOrEmpty(pre) ? pre : LM.Get(pre);
        switch (pNano?.getType())
        {
            case "kingdom":
                pre = ((Kingdom)pNano).GetKingdomName();
                LogService.LogInfo("国家");
                break;
            case "city":
                pre = ((City)pNano).GetCityName();
                LogService.LogInfo("城市");
                break;
        }
        LogService.LogInfo(pNano?.getType());
        var post = LM.Get(string.Join("_", regimeType, "officiallevel", officeType));
        return pre + post;
    }
    public void InitialOffice(BureauSetting config, Action action = null)
    {
        officeType = config.type;
        timestamp = World.world.getCurWorldTime();
        pre = config.pre;
        merit = config.merit;
        honorary = config.honorary;
        require_traits = config.require_traits;
        select_from_local = config.select_from_local;
        leader_select_method = config.leader_select_method;
        history_officers = new List<string> {};
    }

    public void SetActor (Actor actor)
    {
        var identity = new OfficeIdentity();
        identity.init(actor);
        identity.meritLevel = merit;
        identity.honoraryOfficial = honorary;
        actor.SetIdentity(identity, false);
        
        actor_id = actor.getID();
        timestamp = World.world.getCurWorldTime();
        
        if(!actor.hasCulture())
        {
            actor.setCulture(actor.kingdom.culture);
        }
        actor.ChangeOfficialLevel(officeType);
        actor.CheckSpecificClan();
        if (!is_local) return;
        switch (meta_object.meta_type)
        {
            case MetaType.City:
                City city =  (City)meta_object;
                city.setLeader(actor, true);
                actor.joinCity(city);
                actor.goTo(city._city_tile);
                break;
            case MetaType.Kingdom:
                Kingdom kingdom = (Kingdom)meta_object;
                kingdom.setKing(actor); 
                kingdom.capital.setLeader(actor, true);
                actor.goTo(kingdom.capital._city_tile);
                break;
        }
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
            history_officers.Add(actor.data.name);
        }
        actor_id = -1L;
    }
}

public class CenterOffice
{
    public OfficeObject General { get; set; } //大将军
    public List<OfficeObject> CoreOffices { get; set; } = new List<OfficeObject>();
    public List<OfficeObject> Divisions { get; set; } = new List<OfficeObject>();
    public void Init(Kingdom pKingdom)
    {
        Regime pRegime = pKingdom.GetRegime();
        foreach (var core in pRegime.bureau_config.cores)
        {

            var o = new OfficeObject();
            o.InitialOffice(core);
            o.regimeType = pKingdom.GetRegime().type;
            o.meta_object = pKingdom;
            o.is_local = false;
            CoreOffices.Add(o);
        }
        foreach (var div in pRegime.bureau_config.division)
        {

            var o = new OfficeObject();
            o.InitialOffice(div);
            o.regimeType = pKingdom.GetRegime().type;
            o.meta_object = pKingdom;
            o.is_local = false;
            Divisions.Add(o);
        }
    }

    public void SyncMetaObject(Kingdom pkingdom)
    {
        foreach (var office in CoreOffices)
        {
            office.meta_object =  pkingdom;
            office.is_local = false;
        }
        foreach (var office in Divisions)
        {
            office.meta_object =  pkingdom;
            office.is_local = false;
        }
    }

    public List<Actor> GetAllOfficers(Empire empire)
    {
        List<Actor> officers = new List<Actor>();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            officers.AddRange(kingdom.units.ToList().FindAll(a => a.hasTrait("officer")));
        }

        return officers;
    }
}