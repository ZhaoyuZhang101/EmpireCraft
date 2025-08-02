using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using NeoModLoader.General;
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
    LOV,  //爱人
    COB,  //小妾
    NONE  //无
}
public class SpecificClan
{
    public long id { get; set; }
    public string name { get; set; }
    public double established_timestamp { get; set; }
    public long founder { get; set; }
    public SpecificClanType clan_sex_priority { get; set; }

    public Dictionary<long, PersonalClanIdentity> _cache = new();
    [JsonIgnore]
    public int Count => _cache.Count;
    public PersonalClanIdentity GetPerson(long personId)
    {
        if (_cache.TryGetValue(personId, out var cached)) return cached;
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
            foreach (var child in GetChildren(father))
            {
                var sibling = child.Item2;
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
            foreach (var child in GetChildren(mother))
            {
                var sibling = child.Item2;
                if (sibling != null && sibling.id != selfId)
                {
                    if (sibling.father != identity.father)
                        siblings.Add((ClanRelation.SFDM, sibling)); // 同母异父
                }
            }
        }
        return siblings;
    }
    public SpecificClan(Actor actor) 
    {
        _cache = new Dictionary<long, PersonalClanIdentity>();
        Clan clan = actor.clan;
        id = IdGenerator.NextId();
        name = clan.GetClanName();
        established_timestamp = World.world.getCurWorldTime();
        clan_sex_priority = judgeMalePriority(actor)? SpecificClanType.MalePriority:SpecificClanType.FemalePriority;
        addActor(actor);
        if (actor.hasClan())
        {
            foreach (var member in actor.clan.units.ToList())
            {
                if (member != actor)
                {
                    addActor(member);
                }
            }
        }
        LogService.LogInfo("1");
        clan.SetSpecificClan(this);
        SpecificClanManager._specificClans.Add(this);
        founder = actor.GetPersonalIdentity().id;
        LogService.LogInfo($"族谱加入成功{this.name}目前存在的族谱:{SpecificClanManager._specificClans.Count}");
    }
    
    public List<(ClanRelation, PersonalClanIdentity)> GetChildren(PersonalClanIdentity identity)
    {
        var children = this
            ._cache.Values
            .Where(pci => pci.father == identity.id || pci.mother == identity.id)
            .Select(pci => (ClanRelation.CHILD, pci))
            .ToList();

        return children;
    }

    private bool judgeMalePriority(Actor actor)
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
        if (!actor.HasSpecificClan())
        {
            LogService.LogInfo($"无{this.name}氏族身份，正在初始化{actor.name}的角色氏族身份");
            PersonalClanIdentity pci = actor.InitialPersonalIdentity(this);
            pci.is_concubine = is_concubines;
            if (pci.is_main)
            {
                if (actor.getChildren().Any())
                {
                    foreach (var child in actor.getChildren())
                    {
                        LogService.LogInfo($"<UNK>{this.name}<UNK>{child.name}<UNK>");
                        pci.addChild(child, true);
                    }
                }
            }
        }
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
        if (show_log)
        {
            TranslateHelper.LogOfficerBuildSpecificClan(actor, specificClan);
        }
        return specificClan;
    }
    public static PersonalClanIdentity getPerson(long identity_id)
    {
        foreach (var sc in _specificClans)
        {
            var pci = sc.GetPerson(identity_id);
            if (pci != null && pci.specific_clan_id == sc.id)
                return pci;
        }
        return null;
    }

    public static List<(ClanRelation, PersonalClanIdentity)> getChildren(PersonalClanIdentity identity)
    {
        var identityWithRelation = new List<(ClanRelation, PersonalClanIdentity)>();
        foreach (var sc in _specificClans)
        {
            identityWithRelation.AddRange(sc.GetChildren(identity));
        }
        return identityWithRelation;
    }

    public static SpecificClan CheckSpecificClan(this Actor actor, bool show_log = true)
    {
        if (actor == null) return null;
        if (!actor.hasClan())
        {
            World.world.clans.newClan(actor, true);
            LogService.LogInfo("新建氏族");
        }
        if (!actor.HasSpecificClan())
        {
            LogService.LogInfo("新建宗族");
            return newSpecificClan(actor, show_log);
        }
        return actor.GetSpecificClan();
    }

    public static SpecificClan Get(long id)
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
    public static void Remove(SpecificClan sc)
    {
        foreach (SpecificClan sc2 in _specificClans.ToList())
        {
            if (sc2.id == sc.id)
            {
                _specificClans.Remove(sc2);
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
    public long id { get; set; }
    public long specific_clan_id { get; set; }
    public long actor_id { get; set; }
    public string name { get; set; }
    [JsonIgnore]
    public SpecificClan _specificClan;
    public ActorSex sex { get; set; }
    public string birthday { get; set; }
    public string deathday { get; set; }
    [JsonIgnore]
    public Actor _actor => World.world.units.get(actor_id);
    public bool is_alive { get; set; }
    public bool is_concubine {  get; set; } = false; //是否是小妾/男宠（当小妾/男宠无自身宗族时，会加入丈夫/妻子氏族并标记为小妾/男宠身份）
    public bool is_main { get; set; } = true; //在婚姻关系中是否为主要角色（对于爱人来说是嫁/入赘，还是娶/招亲）
    public int age { get; set; }
    public int generation { get; set; }
    public long mother { get; set; } = -1L; //母亲
    public long father { get; set; } = -1L; //父亲
    public long father_in_law { get; set; } = -1L; //义父
    public long mother_in_law { get; set; } = -1L; //义母
    public (long specific_clan, long identity) lover = (-1L, -1L); //正妻/正夫
    public List<(long specific_clan, long identity)> concubines = new(); //小妾/情人
    
    public PersonalClanIdentity (SpecificClan specificClan, Actor a)
    {
        this.id = IdGenerator.NextId();
        this.is_alive = true;
        this.actor_id = a.getID();
        this.specific_clan_id = specificClan.id;
        this.age = a.getAge();
        this.name = a.getName();
        this.birthday = a.getBirthday();
        this.sex = a.data.sex;
        this.generation = 0;
        this._specificClan = specificClan;
        a.SetPersonalIdentity (this);
        specificClan._cache.Add(this.id, this);
    }

    public bool hasLover()
    {
        return lover != (-1L, -1L);
    }

    public string getDeathday()
    {
        if ( String.IsNullOrEmpty(deathday))
        {
            return LM.Get("until_now");
        }
        return deathday;
    }

    private bool judgeMarriageMain(Actor actor)
    {
        return (_specificClan.isMalePriority() && actor.isSexMale()) || (!_specificClan.isMalePriority() && actor.isSexFemale());
    }

    public void setLover(Actor actor)
    {
        if (actor == null) return;
        if (this.lover!= (-1L, -1L)) return;
        actor.CheckSpecificClan(false);
        PersonalClanIdentity lpci = actor.GetPersonalIdentity();
        if (lpci == null) return;
        lover.specific_clan = lpci.specific_clan_id;
        lover.identity = lpci.id;
        is_main = judgeMarriageMain(_actor);

        lpci.lover.specific_clan = specific_clan_id;
        lpci.lover.identity = id;
        lpci.is_main = !judgeMarriageMain(_actor);
    }
    public void addConcubines(Actor actor)
    {
        _specificClan.addActor(actor, is_concubines:true);
        LogService.LogInfo("4");
        this.concubines.Add((this.specific_clan_id, actor.GetPersonalIdentity().id));
    }

    public void setParent(PersonalClanIdentity identity)
    {
        if (identity==null) return; 
        if (identity.sex==ActorSex.Male)
        {
            father = identity.id;
            LogService.LogInfo($"设置父{identity.name}");
        } else
        {
            mother = identity.id;
            LogService.LogInfo($"设置母{identity.name}");
        }
        generation = identity.generation + 1;
    }

    public void addChild(Actor actor, bool isNeedSetParentBoth=false)
    {
        PersonalClanIdentity pci = actor.GetPersonalIdentity();
        if (pci==null)
        {
            pci = actor.InitialPersonalIdentity(_specificClan);
        }

        if (isNeedSetParentBoth)
        {
            if (actor.getParents().Any())
            {
                LogService.LogInfo($"添加子嗣，找到父母{actor.getParents().Count()}");
                foreach (var parent in actor.getParents().ToList())
                {
                    parent.CheckSpecificClan(false);
                    PersonalClanIdentity pIdentity = parent.GetPersonalIdentity();
                    pci.setParent(pIdentity);
                }
            }
            else
            {
                pci.setParent(this);
            }
        }
        else
        {
            pci.setParent(this);
        }
        LogService.LogInfo($"子嗣名称{pci.name}");
        pci.generation = this.generation + 1;
        _specificClan.addActor(actor);
        LogService.LogInfo("5");
    }
}
