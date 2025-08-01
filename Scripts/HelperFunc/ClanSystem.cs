using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;

namespace EmpireCraft.Scripts.HelperFunc;
public enum SpecificClanType
{
    MalePriority,
    FemalePriority
}
public enum ClanRelation
{
    SFSM, //同父同母
    DFSM, //异父同母
    SFDM, //同父异母
    SELF, //自己
    MOM,  //直系母
    FAT,  //直系父
    MIL,  //义母
    FIL,  //义母
    CHILD,  //孩子
    NONE  //无
}
public class SpecificClan
{
    public double id { get; set; }
    public string name { get; set; }
    public double established_timestamp { get; set; }
    public string founder { get; set; }
    public SpecificClanType clan_sex_priority { get; set; }

    private Dictionary<double, PersonalClanIdentity> _cache = new();
    public int Count => _cache.Count;
    public PersonalClanIdentity GetPerson(double id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;
        return null;
    }
    public List<(ClanRelation, PersonalClanIdentity)> GetSiblingsWithRelation(PersonalClanIdentity identity)
    {
        var siblings = new List<(ClanRelation, PersonalClanIdentity)>();
        PersonalClanIdentity father = GetPerson(identity.father);
        PersonalClanIdentity mother = GetPerson(identity.mother);
        var selfId = identity.id;

        if (father != null)
        {
            foreach (var childId in father.children)
            {
                var sibling = GetPerson(childId);
                if (sibling != null && sibling.id != selfId)
                {
                    if (sibling.mother == identity.mother)
                        siblings.Add((ClanRelation.SFSM, sibling)); // 同父同母
                    else
                        siblings.Add((ClanRelation.DFSM, sibling)); // 同父异母
                }
            }
        }
        if (mother != null)
        {
            foreach (var childId in mother.children)
            {
                var sibling = GetPerson(childId);
                if (sibling != null && sibling.id != selfId)
                {
                    if (sibling.father != identity.father)
                        siblings.Add((ClanRelation.SFDM, sibling)); // 同母异父
                }
            }
        }
        siblings.Add((ClanRelation.SELF, identity));
        return siblings;
    }
    public SpecificClan(Actor actor) 
    {
        _cache = new Dictionary<double, PersonalClanIdentity>();
        Clan clan = actor.clan;
        id = IdGenerator.NextId();
        name = clan.GetClanName();
        established_timestamp = World.world.getCurWorldTime();
        founder = actor.name;
        clan_sex_priority = judgeMalePriority(actor)? SpecificClanType.MalePriority:SpecificClanType.FemalePriority;
        addActor(actor);
        LogService.LogInfo("1");
        if (actor.hasClan())
        {
            foreach (Actor member in actor.clan.units.ToList())
            {
                if (member.getID() == actor.getID()) continue;
                addActor(actor);
                LogService.LogInfo("2");
            }
        }
        clan.SetSpecificClan(this);
    }

    public bool judgeMalePriority(Actor actor)
    {
        if (actor.hasCulture())
        {
            if (actor.culture.hasTrait("patriarchy"))
            {
                return true;
            } else if (actor.culture.hasTrait("matriarchy"))
            {
                return false;
            }
        }
        return true;
    }

    public bool isMalePriority()
    {
        return clan_sex_priority == SpecificClanType.MalePriority;
    }
    public void addActor(Actor actor, bool is_concubines= false)
    {
        bool flag = false;
        PersonalClanIdentity pci = null;
        if (!actor.HasSpecificClan())
        {
            LogService.LogInfo($"无{this.name}氏族身份，正在初始化{actor.name}的角色氏族身份");
            pci = actor.InitialPersonalIdentity(this);
            flag = true;
        }
        else
        {
            SpecificClan specificClan = actor.GetSpecificClan();
            if (specificClan.id != id)
            {
                LogService.LogInfo($"角色{actor.name}已从{specificClan.name}氏族移除，并纳入{this.name}氏族");
                specificClan.removeActor(actor);
                pci = actor.InitialPersonalIdentity(this);
                flag = true;
            } else
            {
                LogService.LogInfo($"角色{actor.name}已纳入{this.name}氏族，无需加入");
                pci = actor.GetPersonalIdentity();
                flag = false;
            }
        }
        pci.is_concubine = is_concubines;
        if (!flag) return;
        if (pci != null)
        {
            if (_cache.ContainsKey(pci.id)) return;
            _cache.Add(pci.id, pci);
        } else
        {
            LogService.LogInfo("添加角色到族谱失败");
        }
    }
    public void Initialize(Actor actor)
    {

    }
    public void removeActor(Actor actor) 
    {
        var identity = actor.GetPersonalIdentity();
        if (identity != null) 
        {
            _cache.Remove(identity.id);
            actor.RemoveSpecificClan();
        }
    }
    public void dispose()
    {
        foreach (var actor in _cache.Values) 
        {
            if (actor.is_alive)
            {
                Actor iActor = World.world.units.get(actor.actor_id);
                if (iActor != null)
                {
                    iActor.RemoveSpecificClan();
                }
            }
        }
        _cache.Clear();
    }
}

public static class SpecificClanManager
{
    public static List<SpecificClan> _specificClans = new List<SpecificClan>();
    public static SpecificClan newSpecificClan(Actor actor, bool show_log = true)
    {
        SpecificClan specificClan = new SpecificClan(actor);
        Add(specificClan);
        if (show_log)
        {
            TranslateHelper.LogOfficerBuildSpecificClan(actor, specificClan);
        }
        return specificClan;
    }
    public static PersonalClanIdentity getPerson(double identity_id)
    {
        foreach (var sc in _specificClans)
        {
            var pci = sc.GetPerson(identity_id);
            if (pci != null && pci.specific_clan_id == sc.id)
                return pci;
        }
        return null;
    }

    public static void CheckSpecificClan(Actor actor, bool show_log = true)
    {
        if (actor == null) return;
        if (!actor.hasClan())
        {
            World.world.clans.newClan(actor, true);
        }
        if (!actor.HasSpecificClan())
        {
            newSpecificClan(actor, show_log);
        }
    }
    public static void Add(SpecificClan SC)
    {
        if (!_specificClans.Contains(SC))
        {
            _specificClans.Add(SC);
            LogService.LogInfo($"族谱加入成功{SC.name}目前存在的族谱:{_specificClans.Count}");
        } else
        {
            LogService.LogInfo($"族谱{SC.name}已存在:{_specificClans.Count}");
        }
        
    }

    public static SpecificClan Get(double id)
    {
        SpecificClan clan = _specificClans.Find(sc => sc.id == id);
        if (clan == null)
        {
            LogService.LogInfo("未发现氏族，获取氏族失败");
            return null;
        } else
        {
            return clan;
        }

    }
    public static void Remove(SpecificClan SC)
    {
        foreach (SpecificClan sc in _specificClans.ToList())
        {
            if (sc.id == SC.id)
            {
                _specificClans.Remove(sc);
                return;
            }
        }
        LogService.LogInfo("未发现氏族，移除氏族失败");
    }
    public static void Dispose(SpecificClan specificClan) 
    {
        foreach(Clan clan in World.world.clans)
        {
            if (clan.HasSpecificClan())
            {
                SpecificClan sc = clan.GetSpecificClan();
                if (sc.id == specificClan.id)
                {
                    sc.dispose();
                    Remove(specificClan);
                    clan.RemoveSpecificClan();
                }
            }
        }
    }
}

public class PersonalClanIdentity
{
    public double id { get; set; }
    public double specific_clan_id { get; set; }
    public long actor_id { get; set; }
    public string name { get; set; }
    [JsonIgnore]
    public SpecificClan _specificClan;
    public ActorSex sex { get; set; }
    public double birthday { get; set; }
    public double death_day { get; set; }
    public bool is_alive { get; set; }
    public bool is_concubine {  get; set; } = false; //是否是小妾/男宠（当小妾/男宠无自身宗族时，会加入丈夫/妻子氏族并标记为小妾/男宠身份）
    public bool is_main { get; set; } = false; //在婚姻关系中是否为主要角色（对于爱人来说是嫁/入赘，还是娶/招亲）
    public int age { get; set; }
    public int generation { get; set; }
    public double mother { get; set; } = -1L; //母亲
    public double father { get; set; } = -1L; //父亲
    public double father_in_law { get; set; } = -1L; //义父
    public double mother_in_law { get; set; } = -1L; //义母
    public (double specific_clan, double identity) lover = (-1L, -1L); //正妻/正夫
    public List<(double specific_clan, double identity)> concubines = new();
    public List<double> children { get; set; } = new List<double>();
    
    public PersonalClanIdentity (SpecificClan specificClan, Actor a)
    {
        this.id = IdGenerator.NextId();
        this.is_alive = true;
        this.actor_id = a.getID();
        this.specific_clan_id = specificClan.id;
        this.age = a.getAge();
        this.name = a.getName();
        this.birthday = a.data.created_time;
        this.sex = a.data.sex;
        this.generation = 0;
        this._specificClan = specificClan;
        a.SetPersonalIdentity (this);
    }

    public void setLover(Actor actor, bool is_main_in_marriage)
    {
        SpecificClanManager.CheckSpecificClan(actor, false);
        SpecificClan lsc = actor.GetSpecificClan();
        PersonalClanIdentity lpci = actor.GetPersonalIdentity();
        this.lover.specific_clan = lsc.id;
        this.lover.identity = lpci.id;
        this.is_main = is_main_in_marriage;

        lpci.lover.specific_clan = this.specific_clan_id;
        lpci.lover.identity = this.id;
        lpci.is_main = !is_main_in_marriage;
    }
    public void addConcubines(Actor actor)
    {
        _specificClan.addActor(actor, is_concubines:true);
        LogService.LogInfo("4");
        this.concubines.Add((this.specific_clan_id, actor.GetPersonalIdentity().id));
    }

    public void setParent(Actor actor)
    {
        
        if (!actor.HasSpecificClan())
        {
            _specificClan.addActor(actor);
            LogService.LogInfo("5");
        }
        PersonalClanIdentity pci = actor.GetPersonalIdentity();
        if (actor.isSexMale())
        {
            this.father = pci.id;
        } else
        {
            this.mother = pci.id;
        }
        this.generation = pci.generation + 1;
    }

    public void addChild(Actor actor)
    {
        if (!actor.HasSpecificClan())
        {
            actor.InitialPersonalIdentity(_specificClan);
        }
        PersonalClanIdentity pci = actor.GetPersonalIdentity();
        if (!children.Contains(pci.id))
        {
            children.Add(pci.id);
        }
        pci.generation = this.generation + 1;
        _specificClan.addActor(actor);
        LogService.LogInfo("5");
    }
}
