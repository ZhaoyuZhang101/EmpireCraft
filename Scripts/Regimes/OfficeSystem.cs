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
    public List<string> require_traits;
}

public class BureauConfig
{
    public List<BureauSetting> cores;
    public List<BureauSetting> division;
}
public class OfficeObject
{
    public double timestamp { get; set; }
    public int officeType { get; set; }
    public long actor_id { get; set; }
    public string pre { get; set; } = "";
    public int merit { get; set; }
    public int honorary { get; set; }
    public bool is_local { get; set; } = false;
    public List<string> require_traits { get; set; } = new List<string>();
    public List<string> history_officers = new List<string>();
    public Regime regime { get; set; }
    public string GetName(NanoObject pNano = null)
    {
        pre = string.IsNullOrEmpty(pre) ? pre : LM.Get(pre);
        switch (pNano?.getType())
        {
            case nameof(Kingdom):
                pre = ((Kingdom)pNano).GetKingdomName();
                break;
            case nameof(City):
                pre = ((City)pNano).GetCityName();
                break;
        }
        var post = LM.Get(string.Join("_", regime.type, "officiallevel", officeType));
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
        timestamp = World.world.getCurWorldTime();
        actor.ChangeOfficialLevel(officeType);
        actor.CheckSpecificClan();
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
    public List<OfficeObject> Ministers { get; set; } //内阁
    public OfficeObject General { get; set; } //大将军
    public List<OfficeObject> CoreOffices { get; set; } = new List<OfficeObject>();
    public List<OfficeObject> Divisions { get; set; } = new List<OfficeObject>();
    public CenterOffice(Kingdom pKingdom)
    {
        Regime pRegime = pKingdom.GetRegime();
        foreach (var core in pRegime.bureau_config.cores)
        {

            var o = new OfficeObject();
            o.InitialOffice(core);
            o.regime = pRegime;
            CoreOffices.Add(o);
        }
        foreach (var div in pRegime.bureau_config.division)
        {

            var o = new OfficeObject();
            o.InitialOffice(div);
            o.regime = pRegime;
            Divisions.Add(o);
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