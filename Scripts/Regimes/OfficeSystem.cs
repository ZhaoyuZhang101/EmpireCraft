using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
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
    教育,     //国民基础智力增加
    生育      //为皇帝诞下子嗣
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
    public List<string> condition;
    public LeaderSelectMethod leader_select_method;
    public List<string> require_traits;
    public CityType city_type;
}

public class BureauConfig
{
    public List<BureauSetting> cores;
    public List<BureauSetting> division;
    public List<BureauSetting> harems;
    public Dictionary<KingdomType, BureauSetting> kingdoms;
    public Dictionary<CityType, BureauSetting> cities;
    public Dictionary<ArmyOfficialType, BureauSetting> armies;
}

public static class OfficeManager
{
    public static Dictionary<long, OfficeObject> Offices = new();
    public static bool Remove(long pOfficeID)
    {
        var res = Offices.Remove(pOfficeID);
        return res;
    }
    public static List<OfficerPowerType> AllPower = new List<OfficerPowerType>
    {
        OfficerPowerType.财政, OfficerPowerType.人事, OfficerPowerType.军事, OfficerPowerType.审核, 
        OfficerPowerType.宗教, OfficerPowerType.执行, OfficerPowerType.教育, OfficerPowerType.礼仪,
        OfficerPowerType.司法, OfficerPowerType.天子护理, OfficerPowerType.天子政教, OfficerPowerType.天子智教,
        OfficerPowerType.建设, OfficerPowerType.生育
    };
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
    public int honorary { get; set; }
    [JsonIgnore]
    public NanoObject meta_object { get; set; } = null;

    public double last_pregnant_timestamp { get; set; } = -1L;

    public bool select_from_local { get; set; } = false;
    public LeaderSelectMethod leader_select_method { get; set; }
    public bool is_local { get; set; } = false;
    public RegimeType regimeType { get; set; }
    public PeerageType peerageType { get; set; } = PeerageType.Civil;
    public List<string> require_traits { get; set; } = new List<string>();
    public List<string> history_officers = new List<string>();
    
    public string GetName(NanoObject pNano = null)
    {
        string officePrefix = pre ?? "";
        bool flag = officePrefix.Contains("_all");
        string preX = string.IsNullOrEmpty(officePrefix) ? "" : LM.Get(officePrefix);
        if (pNano is Army army && army._city?.data != null)
        {
            string cityName = army._city.GetCityName();
            if (!string.IsNullOrWhiteSpace(cityName)) preX = cityName;
        }
        else switch (pNano?.meta_type)
        {
            case MetaType.Kingdom:
                Kingdom kingdom = (Kingdom)pNano;
                if (kingdom.data != null)
                {
                    string kingdomName = kingdom.GetKingdomName();
                    if (!string.IsNullOrWhiteSpace(kingdomName)) preX = kingdomName;
                }
                break;
            case MetaType.City:
                City city = (City)pNano;
                if (city.data != null)
                {
                    string cityName = city.GetCityName();
                    if (!string.IsNullOrWhiteSpace(cityName)) preX = cityName;
                }
                break;
        }
        string post = LM.Get(string.Join("_", regimeType, "officiallevel", officeType)) ?? "";
        return flag? post: preX + post;
    }
    public void DetectPower(Empire empire)
    {
        var officer = GetActor();
        foreach (var power in powers)
        {
            var newAddition = officer.CalcPower(power, empire);
            empire.data.additions.Add(newAddition);
            if (power == OfficerPowerType.生育)
            {
                if (officer.isRekt()) return;
                if (GetPregnantYear() > 2)
                {
                    var kingdom = (Kingdom) meta_object;
                    if (kingdom != null)
                    {
                        if (OverallHelperFunc.HasChangeToGiveBirth(officer, kingdom.king))
                        {
                            BabyMaker.makeBaby(kingdom.king, officer); 
                        } 
                    }
                    last_pregnant_timestamp = World.world.getCurWorldTime();
                }
            }
        }
    }

    public int GetPregnantYear()
    {
        if (last_pregnant_timestamp == -1L)
        {
            return 999;
        }

        return Date.getYearsSince(last_pregnant_timestamp);
    }
    public string GetOfficeName(NanoObject pNano = null)
    {
        string officePrefix = pre ?? "";
        var flag = officePrefix.Contains("full")||officePrefix.Contains("all");
        var preX = LM.Get(string.Join("_", regimeType, "officiallevel", officeType));
        if (flag) preX = LM.Get(officePrefix);
        switch (pNano?.meta_type)
        {
            case MetaType.Kingdom:
                Kingdom kingdom = (Kingdom)pNano;
                if (kingdom.data != null && !string.IsNullOrWhiteSpace(kingdom.data.name))
                    preX = kingdom.data.name;
                break;
            case MetaType.City:
                City city = (City)pNano;
                if (city.data != null && !string.IsNullOrWhiteSpace(city.data.name))
                    preX = city.data.name;
                break;
        }
        return preX ?? "";
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
            OfficeManager.Offices.Add(OfficeID, this);
        }
    }

    public void SetActor (Actor actor)
    {
        var originalActor = GetActor();
        if (originalActor != null && !originalActor.isRekt())
        {
            originalActor.EndOffice();
            if (originalActor.HasOfficeIdentity())
            {
                var id = originalActor.GetIdentity();
                id.RemoveOffice();
            }
            else
            {
                RemoveActor();
            }
        }

        if (actor.isRekt())
        {
            return;
        }
        bool isReappointment = history_officers.Contains(actor.data.name);
        if(!actor.hasCulture())
        {
            actor.setCulture(actor.kingdom.culture);
        }
        actor.CheckSpecificClan();
        //初始化角色官职
        if (actor.kingdom.GetRegime().GetLeaderSelectMethod() != LeaderSelectMethod.Exam)
        {
            actor.InitialIdentity();
        }
        if (actor.HasOfficeIdentity())
        {
            var identity = actor.GetIdentity();
            if (identity != null)
            {
                if (actor.IsOnOffice())
                {
                    identity.RemoveOffice();
                }
                identity.SetOfficeId(OfficeID, actor);
                actor.addTrait("officer");
                actor.ChangeOfficialLevel(officeType);
            }
        }
        actor.StartOffice(this);
        actor_id = actor.getID();
        timestamp = World.world.getCurWorldTime();
        var empireName = "";
        if (actor?.kingdom?.IsInEmpire() ?? false)
        {
            if (actor?.kingdom?.GetEmpire()!=null)
            {
                empireName = actor?.kingdom?.GetEmpire().GetEmpireName()+" | ";
            }
        }
        var personalId = actor.GetPersonalIdentity();
        personalId?.SetOfficeName(empireName+GetName());
        // Kings and city leaders are also offices. Record their appointment here
        // once instead of creating a second king/leader-specific history entry.
        string officeHistoryKey = isReappointment ? "personal_history_office_reappointed" : "personal_history_office_started";
        actor.RecordPersonalHistory(string.Format(LM.Get(officeHistoryKey), GetName(meta_object)));
        if(!is_local) return;
        switch (meta_object.meta_type)
        {
            case MetaType.City:
                City city =  (City)meta_object;
                city.setLeader(actor, true);
                actor.joinCity(city);
                if (city?._city_tile != null)
                {
                    actor.goTo(city._city_tile);
                }
                personalId?.SetOfficeName(GetName(city));
                break;
            case MetaType.Kingdom:
                Kingdom kingdom = (Kingdom)meta_object;
                if (kingdom.IsInEmpire() && !kingdom.IsEmpire())
                {
                    personalId?.SetOfficeName(kingdom.GetEmpire().GetEmpireName() + "| ");
                }
                personalId?.SetOfficeName(GetName(kingdom));
                kingdom.setKing(actor); 
                actor.joinCity(kingdom.capital);
                if (kingdom?.capital?._city_tile != null)
                {
                    actor.goTo(kingdom.capital._city_tile);
                }
                break;
        }
    }
    public Actor GetActor()
    {
        var res = World.world.units.get(actor_id);
        if (res != null)
        {
            if (res.isUnitFitToRule() && res.isAdult())
            {
                return res;
            }
        }
        return null;
    }
    public int GetOnTime()
    {
        if (this.actor_id == -1L)
        {
            return -1;
        }
        if (GetActor() == null)
        {
            return -1;
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
            PersonalClanIdentity personalIdentity = actor.GetPersonalIdentity();
            if (personalIdentity != null && !personalIdentity.death_history_recorded && actor.isAlive() && !actor.isRekt())
            {
                actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_office_left"), GetName(meta_object)));
            }
            actor.EndOffice();
            if (officeType is > 13 and <= 22)
            {
                actor.lover = null;
            }
        }


        actor_id = -1L;
        
    }
}

public class CenterOffice
{
    public List<long> CoreOffices { get; set; } = new List<long>();
    public List<long> Divisions { get; set; } = new List<long>();
    public List<long> Harems { get; set; } = new List<long>();
    public void Init(Kingdom pKingdom)
    {
        CoreOffices.ForEach(id=>OfficeManager.Remove(id));
        CoreOffices.Clear();
        Regime pRegime = pKingdom.GetRegime();
        foreach (var core in pRegime.bureau_config.cores)
        {

            var o = new OfficeObject();
            o.InitialOffice(core);
            o.regimeType = pKingdom.GetRegime().type;
            o.meta_object = pKingdom;
            o.is_local = false;
            o.select_from_local = false;
            o.OfficeID = OverallHelperFunc.IdGenerator.NextId();
            o.actor_id = -1L;
            o.history_officers.Clear();
            OfficeManager.Offices.Add(o.OfficeID, o);
            CoreOffices.Add(o.OfficeID);
        }
        Divisions.ForEach(id=>OfficeManager.Remove(id));
        Divisions.Clear();
        foreach (var div in pRegime.bureau_config.division)
        {

            var o = new OfficeObject();
            o.InitialOffice(div);
            o.regimeType = pKingdom.GetRegime().type;
            o.select_from_local = false;
            o.meta_object = pKingdom;
            o.is_local = false;
            o.OfficeID = OverallHelperFunc.IdGenerator.NextId();
            o.actor_id = -1L;
            o.history_officers.Clear();
            OfficeManager.Offices.Add(o.OfficeID, o);
            Divisions.Add(o.OfficeID);
        }
        Harems.ForEach(id=>OfficeManager.Remove(id));
        Harems.Clear();
        if (pRegime.bureau_config.harems == null) return;
        foreach (var div in pRegime.bureau_config.harems)
        {
            var o = new OfficeObject();
            o.InitialOffice(div);
            o.regimeType = pKingdom.GetRegime().type;
            o.select_from_local = false;
            o.meta_object = pKingdom;
            o.is_local = false;
            o.OfficeID = OverallHelperFunc.IdGenerator.NextId();
            o.actor_id = -1L;
            o.history_officers.Clear();
            OfficeManager.Offices.Add(o.OfficeID, o);
            Harems.Add(o.OfficeID);
        }

        if (!pKingdom.IsEmpire()) return;
        var empire = pKingdom.GetEmpire();
        empire?.kingdoms_list.ForEach(k =>
        {
            k.RemoveFactionRatio();
        });
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

    public List<OfficeObject> GetAllOffices(Empire empire)
    {
        List<OfficeObject> offices = new List<OfficeObject>();
        foreach (Kingdom kingdom in empire.kingdoms_list)
        {
            if (kingdom.GetOffice() != null)
            {
                offices.Add(kingdom.GetOffice());
            }
        }

        foreach (var city in empire.AllCities())
        {
            if (city.GetOffice() != null)
            {
                offices.Add(city.GetOffice());
            }
        }

        foreach (var coo in empire.data.centerOffice.CoreOffices)
        {
            var officeObject = OfficeManager.Offices.TryGetValue(coo, out OfficeObject o) ? o : null;
            if (officeObject != null)
            {
                offices.Add(officeObject);
            }
        }

        foreach (var dio in empire.data.centerOffice.Divisions)
        {
            var officeObject = OfficeManager.Offices.TryGetValue(dio, out OfficeObject o) ? o : null;
            if (officeObject != null)
            {
                offices.Add(officeObject);
            }
        }

        foreach (var dio in empire.data.centerOffice.Harems)
        {
            var officeObject = OfficeManager.Offices.TryGetValue(dio, out OfficeObject o) ? o : null;
            if (officeObject != null)
            {
                offices.Add(officeObject);
            }
        }
        return offices;
    }
}
