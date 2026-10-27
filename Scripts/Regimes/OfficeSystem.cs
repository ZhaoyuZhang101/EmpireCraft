using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.GeneralSystems;
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
    // Central offices are department heads by default; false explicitly marks a permanent deputy/administrative post.
    public bool? political_appointment;
    public CityType city_type;
    // Optional display suffix overrides; city_type continues to select city offices.
    public string city_suffix_key;
    public string capital_city_suffix_key;
    // Optional culture-specific overrides for shared government templates.
    public Dictionary<string, string> city_suffix_keys_by_culture;
    public Dictionary<string, string> capital_city_suffix_keys_by_culture;
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

    // 清掉没有主人的官职：存活国家、存活城市、未解散帝国的中央机构(含后宫)、仍在的原版军队之外的官职都删掉，
    // 挂在上面的人一并卸任。以前国家灭亡、城市被毁、帝国解散、军队解散后旧官职一直留着；
    // 军队官职的归属不进存档，读档后认不回来又新建一份；中央机构每次重建还把每个官职登记两次。
    // 本地存档里官职表有 11157 条，真正有人在任的只有 92 条。读档后、存档前与游戏中每分钟各清一次
    public static int Prune()
    {
        if (World.world?.kingdoms == null || Offices.Count == 0) return 0;
        var live = new HashSet<long>();
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom?.data != null && !kingdom.isRekt()) live.Add(kingdom.GetOfficeID());
        foreach (City city in World.world.cities)
            if (city?.data != null && !city.isRekt()) live.Add(city.GetOfficeID());
        if (ModClass.EMPIRE_MANAGER != null)
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                CenterOffice center = empire?.data?.centerOffice;
                if (center == null || empire.IsArchived() || empire.isRekt()) continue;
                foreach (long id in center.CoreOffices ?? new List<long>()) live.Add(id);
                foreach (long id in center.Divisions ?? new List<long>()) live.Add(id);
                foreach (long id in center.Harems ?? new List<long>()) live.Add(id);
            }
        var stale = new List<long>();
        foreach (KeyValuePair<long, OfficeObject> pair in Offices)
        {
            if (live.Contains(pair.Key)) continue;
            if (pair.Value?.meta_object is Army army && !army.isRekt()) continue;
            stale.Add(pair.Key);
        }
        foreach (long id in stale)
        {
            Actor holder = Offices[id]?.GetActor();
            if (holder?.data != null && !holder.isRekt() && holder.GetOrCreate().OfficeId == id)
            {
                holder.EndOffice();
                if (holder.HasOfficeIdentity() && holder.GetIdentity()?.GetOfficeId() == id) holder.GetIdentity().RemoveOffice();
            }
            Offices.Remove(id);
        }
        if (stale.Count > 0) LogService.LogInfo($"[EmpireCraft][官职] 清掉没有主人的官职 {stale.Count} 条，剩 {Offices.Count} 条");
        return stale.Count;
    }

    private static float _pruneAt;

    // 游戏中每分钟清一次
    public static void Tick()
    {
        if (World.world == null || !Config.game_loaded || SmoothLoader.isLoading() || UnityEngine.Time.unscaledTime < _pruneAt) return;
        _pruneAt = UnityEngine.Time.unscaledTime + 60f;
        Prune();
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
    public bool? political_appointment { get; set; }
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
        string post = LM.Get(EmpireCraft.Scripts.GeneralSystems.DynastyNaming.RankKey(regimeType, officeType,
            pNano ?? meta_object)) ?? "";
        if (flag) return post;
        // 封国国君的官称：单字国名补“国”(湘国侯)
        return pNano?.meta_type == MetaType.Kingdom
            ? EmpireCraft.Scripts.GameClassExtensions.ActorExtension.JoinRealmRank(preX, post)
            : preX + post;
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
                        // 原版几岁就算成年：君主与女官都到生养的年纪(见 RealisticAgeSystem.MinParentAge)
                        if (RealisticAgeSystem.OldEnoughToParent(officer) && RealisticAgeSystem.OldEnoughToParent(kingdom.king) &&
                            RealisticAgeSystem.YoungEnoughToParent(officer) && RealisticAgeSystem.YoungEnoughToParent(kingdom.king) &&
                            OverallHelperFunc.HasChangeToGiveBirth(officer, kingdom.king) && RoyalHouseSystem.CanBirth(kingdom.king))
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
        var preX = LM.Get(EmpireCraft.Scripts.GeneralSystems.DynastyNaming.RankKey(regimeType, officeType,
            pNano ?? meta_object));
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
        political_appointment = config.political_appointment;
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
        // 神圣罗马式选帝制：选出别的成员国的君主时，他带着自家领国成为帝国中央，不调进原中央(见 HolyRomanSystem)
        if (is_local && meta_object is Kingdom electedSeat &&
            EmpireCraft.Scripts.GeneralSystems.HolyRomanSystem.TryElectHouse(electedSeat, actor)) return;
        if (is_local && meta_object is Kingdom constitutionalCore && constitutionalCore.IsEmpire() &&
            ConstitutionalSuccessionSystem.IsProtected(constitutionalCore.GetEmpire()) &&
            !ConstitutionalSuccessionSystem.CanInherit(constitutionalCore.GetEmpire(), actor)) return;
        if (actor == null || !actor.CanServeOffice(GetOfficeKingdom(actor))) return;
        // 君主之位以外的官职(城主、朝官)只任成年人；君主可以是幼主(用户 2026-10-17 选"国君可以是幼主，城主不行")
        if (!actor.isAdult() && !(meta_object is Kingdom ruler && ruler.GetOffice() == this)) return;
        // 后宫以外的官职不任用妃嫔与在位君主的配偶；君主之位(本国的君主官职)不受影响
        if (leader_select_method != LeaderSelectMethod.Harem && !(meta_object is Kingdom ruled && ruled.GetOffice() == this) &&
            actor.IsHaremOrConsort()) return;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(actor) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(meta_object)) return;
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
            CultureService.SyncActorToCityMainCulture(actor, actor.city ?? actor.kingdom?.capital);
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
        // 本地官职还没挂回所属的城、国(meta_object 不进存档，读档后要等各自的行为检查挂回)：只记任职，不就位
        if(!is_local || meta_object == null) return;
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
        if (res != null && res.IsWarMachine())
        {
            ClearInvalidHolder(res);
            return null;
        }
        if (res != null)
        {
            if (res.isUnitFitToRule() && res.isAdult())
            {
                return res;
            }
        }
        return null;
    }

    private Kingdom GetOfficeKingdom(Actor fallback = null)
    {
        if (meta_object is Kingdom kingdom) return kingdom;
        if (meta_object is City city) return city.kingdom;
        if (meta_object is Army army) return army._city?.kingdom;
        return fallback?.kingdom;
    }

    private void ClearInvalidHolder(Actor actor)
    {
        if (actor != null)
        {
            actor.EndOffice();
            if (actor.hasTrait("officer")) actor.removeTrait("officer");
            if (actor.hasTrait("officerLeave")) actor.removeTrait("officerLeave");
            actor.RemoveIdentity();
        }
        actor_id = -1L;
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
        // 共和国的核心国：中央机构按执政理念成套(见 RepublicSystem / IdeologyBureaus.json)
        // 华夏律令制在秦汉阶段设三公九卿(见 CourtSystem.Dynasty)
        BureauConfig bureau = RepublicSystem.GetBureauOverride(pKingdom, pRegime.bureau_config) ??
                              CourtSystem.GetBureauOverride(pKingdom, pRegime.bureau_config) ?? pRegime.bureau_config;
        foreach (var core in bureau.cores)
        {

            var o = new OfficeObject();
            // 下面自己取 id 登记；这里再登记一次的话，第一个 id 没人记得，永远留在官职表里
            o.InitialOffice(core, isNew: false);
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
        foreach (var div in bureau.division)
        {

            var o = new OfficeObject();
            o.InitialOffice(div, isNew: false);
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
        if (bureau.harems == null) return;
        foreach (var div in bureau.harems)
        {
            var o = new OfficeObject();
            o.InitialOffice(div, isNew: false);
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

        SyncFactionRatios(pKingdom);
    }

    // 派系占比挂回核心国现在的派系：读档时占比表的键只是记着编号的占位派系(见 FactionRatioConverter)，
    // 要按编号对回政体里真正的派系；成员国不设中央派系。以前每次存读档都整套重建官署、顺带在这里对一次，
    // 官署完好不重建以后(见 Empire.TryRepairState)也要照对，否则读档后核心国各派占比都按 0 算
    public void SyncFactionRatios(Kingdom pKingdom)
    {
        if (pKingdom == null || !pKingdom.IsEmpire()) return;
        var empire = pKingdom.GetEmpire();
        empire?.kingdoms_list.ForEach(k =>
        {
            if (k == null || k.isRekt()) return;
            if (k == empire.CoreKingdom)
                k.ReconcileFactionRatios(pKingdom.GetRegime()?.GetPlayerFactions());
            else
                k.GetOrCreate().FactionRatio?.Clear();
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
        // 后宫也要挂回核心国：meta_object 不进存档，读档后不挂回的话选妃(TryGetHarem)取 meta_object 时是空的。
        // 以前读档时整套中央官署都重建一遍，后宫跟着新建，没露出来
        foreach (var office_id in Harems ?? new List<long>())
        {
            OfficeObject office = OfficeManager.Offices.TryGetValue(office_id, out OfficeObject o)? o: null;
            if (office != null)
            {
                office.meta_object = pkingdom;
                office.is_local = false;
            }
        }
    }

    // 官署完好：登记的官职都还在官职表里、按核心国现在的政体设立，而且不是一个官职都没有。
    // 完好的不必重建(重建会把在任者全部撤掉，见 Empire.TryRepairState)
    public bool IsIntact(Kingdom core)
    {
        RegimeType? type = core?.GetRegime()?.type;
        if (type == null || CoreOffices == null || Divisions == null || Harems == null) return false;
        int count = 0;
        foreach (long id in CoreOffices.Concat(Divisions).Concat(Harems))
        {
            if (!OfficeManager.Offices.TryGetValue(id, out OfficeObject office) || office == null ||
                office.regimeType != type) return false;
            count++;
        }
        return count > 0;
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
