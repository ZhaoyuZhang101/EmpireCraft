using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Regimes;
public enum OfficerPowerType
{
    宗教,     //宗教扩张加成
    天子智教,  //增加天子智力
    天子政教,  //添加皇帝diplomacy
    天子护理,  //天子健康
    备选,     //国王候补
    拟定,     //拟定开战，制度转换
    审核,     //同意或驳回政策
    执行,     //开战，外交执行
    人事,     //官员任免
    财政,     //增加基础财政收入
    礼仪,     //增加周边国家好感度
    军事,     //加强军队攻击力
    司法,     //减少叛乱和腐败
    建设,     //加快建设速度
    教育      //国民基础智力增加
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

public static class OfficeManager
{
    public static Dictionary<long, OfficeObject> Offices = new();
    public static bool Remove(long pOfficeID)
    {
        var res = Offices.Remove(pOfficeID);
        return res;
    }
}
public class OfficeObject
{
    public long OfficeID { get; set; }
    public double timestamp { get; set; }
    public int officeType { get; set; }
    public long actor_id { get; set; } = -1L;
    public string pre { get; set; } = "";
    public int merit { get; set; }
    public List<OfficerPowerType> powers { get; set; }
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
    public void InitialOffice(BureauSetting config, Action action = null, bool isNew = true)
    {
        officeType = config.type;
        timestamp = World.world.getCurWorldTime();
        pre = config.pre;
        merit = config.merit;
        honorary = config.honorary;
        require_traits = config.require_traits;
        select_from_local = config.select_from_local;
        leader_select_method = config.leader_select_method;
        powers = config.powers;
        history_officers = new List<string> {};
        if (isNew)
        {
            OfficeID = OverallHelperFunc.IdGenerator.NextId();
            LogService.LogInfo("创建官位ID: " +　OfficeID);
            OfficeManager.Offices.Add(OfficeID, this);
        }
    }

    public void SetActor (Actor actor)
    {
        if(!actor.hasCulture())
        {
            actor.setCulture(actor.kingdom.culture);
        }
        actor.CheckSpecificClan();
        if (actor.HasOfficeIdentity())
        {
            var identity = actor.GetIdentity();
            identity.SetOfficeId(OfficeID);
            actor.addTrait("officer");
            actor.ChangeOfficialLevel(officeType);
        }
        actor_id = actor.getID();
        timestamp = World.world.getCurWorldTime();
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
                actor.joinCity(kingdom.capital);
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
            if (actor.hasTrait("officer"))
            {
                actor.addTrait("officerLeave");
            }
            history_officers.Add(actor.data.name);
        }
        actor_id = -1L;
    }
}

public class CenterOffice
{
    public List<long> CoreOffices { get; set; } = new List<long>();
    public List<long> Divisions { get; set; } = new List<long>();
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
            o.OfficeID = OverallHelperFunc.IdGenerator.NextId();
            OfficeManager.Offices.Add(o.OfficeID, o);
            CoreOffices.Add(o.OfficeID);
        }
        foreach (var div in pRegime.bureau_config.division)
        {

            var o = new OfficeObject();
            o.InitialOffice(div);
            o.regimeType = pKingdom.GetRegime().type;
            o.meta_object = pKingdom;
            o.is_local = false;
            o.OfficeID = OverallHelperFunc.IdGenerator.NextId();
            OfficeManager.Offices.Add(o.OfficeID, o);
            Divisions.Add(o.OfficeID);
        }
    }

    public void SyncMetaObject(Kingdom pkingdom)
    {
        foreach (var office_id in CoreOffices)
        {
            OfficeObject office = OfficeManager.Offices.TryGetValue(office_id, out OfficeObject o)? o: null;
            if (office != null)
            {
                office.meta_object = pkingdom;
                office.is_local = false;
            }
        }
        foreach (var office_id in Divisions)
        {
            OfficeObject office = OfficeManager.Offices.TryGetValue(office_id, out OfficeObject o)? o: null;
            if (office != null)
            {
                office.meta_object = pkingdom;
                office.is_local = false;
            }
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