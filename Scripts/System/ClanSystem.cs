using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;

namespace EmpireCraft.Scripts.HelperFunc;
public enum SpecificClanType
{
    MalePriority,
    FemalePriority
}
public enum ClanRelation
{
    SFSM,  //同父同母
    DFSM,  //异父同母
    SFDM,  //同父异母
    SELF,  //自己
    MOM,   //直系母
    FAT,   //直系父
    MIL,   //义母
    FIL,   //义父
    CHILDS,//儿子
    CHILDD,//女儿
    CHILD, //子嗣
    LOV,   //爱人
    COB,   //小妾
    FSIBG, //堂姐妹
    FSIBB, //堂兄弟
    MSIBG, //表姐妹
    MSIBB, //表兄弟
    FUNC,  //伯伯
    FUNCL, //伯母
    FANT,  //姑姑
    FANTL, //姑父
    MUNC,  //舅舅
    MUNCL, //舅妈
    MANT,  //阿姨
    MANTL, //姨父
    FGF,   //爷爷
    FGM,   //奶奶
    MGF,   //公公
    MGM,   //婆婆
    SBG,   //侄女
    SBB,   //侄子
    SGG,   //外甥女
    SGB,   //外甥
    SDGB,  //外孙
    SDGG,  //外孙女
    SSGB,  //孙子
    SSGG,  //孙女
    FAR,   //远亲
    NONE   //无
}
public class SpecificClan
{
    public long id { get; set; }
    public string name { get; set; }
    public double established_timestamp { get; set; }
    public long founder { get; set; }
    public SpecificClanType clan_sex_priority { get; set; }
    public string color { get; set; } = (new Color(0.7f, 0.8f, 0.7f)).ToHexString();
    public long capital_city_id { get; set; }
    public string empire_name { get; set; }
    public float capital_city_pos_x { get; set; }
    public float capital_city_pos_y { get; set; }
    [JsonIgnore]
    public List<PersonalClanIdentity> all_valid_members => SnapshotPeople().ToList().FindAll(i=>i.CanHeir(i));
    [JsonIgnore]
    private readonly object _cacheLock = new();
    public Dictionary<long, PersonalClanIdentity> _cache = new();
    [JsonIgnore]
    public int Count => SnapshotPeople().ToList().FindAll(i=>i.is_alive).Count;
    public int CountTotal =>SnapshotPeople().ToList().Count;
    public PersonalClanIdentity GetPerson(long personId)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(personId, out var v) ? v : null;
        }
    }

    public PersonalClanIdentity[] SnapshotPeople()
    {
        lock (_cacheLock)
        {
            return _cache.Values.ToArray();
        }
    }

    public void Upsert(PersonalClanIdentity p)
    {
        lock (_cacheLock)
        {
            _cache[p.id] = p;
        }
    }
    public void RecordHistoryEmpire(Empire empire, City capital)
    {
        empire_name = empire.GetEmpireName();
        capital_city_id = capital.getID();
        capital_city_pos_x = capital.city_center.x;
        capital_city_pos_y = capital.city_center.y;
    }
    public List<(ClanRelation, PersonalClanIdentity)> GetChildren(PersonalClanIdentity identity)
    {
        var children = this
            .SnapshotPeople().ToList()
            .Where(pci => pci.father == identity.id || pci.mother == identity.id)
            .Select(pci => (pci.isMale()?ClanRelation.CHILDS:ClanRelation.CHILDD, pci))
            .ToList();

        return children;
    }

    public void newSpecificClan(Actor actor)
    {
        _cache = new Dictionary<long, PersonalClanIdentity>();
        Clan clan = actor.clan;
        id = IdGenerator.NextId();
        name = clan.GetClanName();
        established_timestamp = World.world.getCurWorldTime();
        clan_sex_priority = judgeMalePriority(actor) ? SpecificClanType.MalePriority : SpecificClanType.FemalePriority;
        clan.SetSpecificClan(this);
        color = ColorSelector.NextColor();
    }

    private bool judgeMalePriority(Actor actor)
    {
        if (actor.hasCulture())
        {
            if (actor.culture.hasTrait("patriarchy"))
            {
                return true;
            }
            if (actor.culture.hasTrait("matriarchy"))
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
            PersonalClanIdentity pci = actor.InitialPersonalIdentity(this);
            pci.is_concubine = is_concubines;
            if (pci.is_main)
            {
                if (actor.getChildren().Any())
                {
                    foreach (var child in actor.getChildren())
                    {
                        child.setClan(pci._actor.clan);
                        pci.addChild(child, true);
                    }
                }
            }
        }
    }
    public void removeActor(PersonalClanIdentity identity) 
    {
        if (_cache.ContainsKey(identity.id))
        {
            _cache.Remove(identity.id);
        }
        if (identity.is_alive)
        {
            identity._actor.RemoveSpecificClan();
        }
    }

    public void checkDispose()
    {
        if (Count <= 0)
        {
            SpecificClanManager.RemoveClan(id);
        }
    }
    public void dispose()
    {
        foreach (var actor in SnapshotPeople().ToList()) 
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
    private static readonly object _clansLock = new();
    public static List<SpecificClan> _specificClans = new List<SpecificClan>();
    public static SpecificClan newSpecificClan(Actor actor, bool show_log = true)
    {
        SpecificClan specificClan = new SpecificClan();
        specificClan.newSpecificClan(actor);
        AddClan(specificClan);
        specificClan.addActor(actor);
        if (actor.hasClan())
        {
            foreach (var member in actor.clan.units.ToList())
            {
                if (member != actor)
                {
                    specificClan.addActor(member);
                }
            }
        }
        specificClan.founder = actor.GetPersonalIdentity().id;
        if (show_log)
        {
            TranslateHelper.LogOfficerBuildSpecificClan(actor, specificClan);
        }
        return specificClan;
    }

    public static List<(ClanRelation, PersonalClanIdentity)> FindAllRelations(PersonalClanIdentity self)
    {
        var pre  = new List<(ClanRelation, PersonalClanIdentity)>();
        var post = new List<(ClanRelation, PersonalClanIdentity)>();
        if (self == null) return pre;
        
        var clans = _specificClans.ToArray();

        foreach (var sc in clans)
        {
            var identities = sc.SnapshotPeople();
            
            foreach (var identity in identities)
            {
                var relation = CalcRelation(self, target: identity);
                if (relation != ClanRelation.NONE)
                    pre.Add((relation, identity));
            }
            if (sc.id == self.specific_clan_id)
            {
                foreach (var identity in identities)
                {
                    if (identity.id == self.id) continue;
                    var relation = CalcRelation(self, target: identity);
                    post.Add((relation, identity));
                }
                break;
            }
        }

        return pre.Concat(post).ToList();
    }
    public static List<(ClanRelation, PersonalClanIdentity)> GetSiblingsWithRelation(PersonalClanIdentity identity)
    {
        var siblings = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return siblings;
        PersonalClanIdentity father = getPerson(identity.father);
        PersonalClanIdentity mother = getPerson(identity.mother);
        var selfId = identity.id;

        if (father != null)
        {
            foreach (var child in SpecificClanManager.getChildren(father))
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
            foreach (var child in SpecificClanManager.getChildren(mother))
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
    
    public static List<(ClanRelation, PersonalClanIdentity)> GetGrandChildren(PersonalClanIdentity identity)
    {
        var grandChildrenResult = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return grandChildrenResult;
        var children = SpecificClanManager.getChildren(identity);
        foreach (var child in children)
        {
            var grandChildren = SpecificClanManager.getChildren(child.Item2);
            foreach (var grandChild in grandChildren)
            {
                if (!grandChildrenResult.Contains(grandChild))
                {
                    if (child.Item2.is_main)
                    {
                        if (grandChild.Item2.isMale())
                        {
                            grandChildrenResult.Add((ClanRelation.SSGB, grandChild.Item2));
                        }
                        else
                        {
                            grandChildrenResult.Add((ClanRelation.SSGG, grandChild.Item2));
                        }
                    }
                    else
                    {
                        if (grandChild.Item2.isMale())
                        {
                            grandChildrenResult.Add((ClanRelation.SDGB, grandChild.Item2));
                        }
                        else
                        {
                            grandChildrenResult.Add((ClanRelation.SDGG, grandChild.Item2));
                        }
                    }
                }
            }
        }
        return grandChildrenResult;
    }

    public static List<(ClanRelation relation, PersonalClanIdentity pci)> GetFatherGrandGeneration(PersonalClanIdentity identity)
    {
        var relatives = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return relatives;
        // —— 1. 父系祖父母 ——
        var father = getPerson(identity.father);
        if (father != null)
        {
            if (father.father > 0)
            {
                var pgf = getPerson(father.father);
                if (pgf != null) relatives.Add((ClanRelation.FGF, pgf));
            }
            if (father.mother > 0)
            {
                var pgm = getPerson(father.mother);
                if (pgm != null) relatives.Add((ClanRelation.FGM, pgm));
            }
        }
        return relatives;
    }

    public static List<(ClanRelation relation, PersonalClanIdentity pci)> GetMotherGrandGeneration(PersonalClanIdentity identity)
    {
        var relatives = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return relatives;
        // —— 2. 母系祖父母 ——
        var mother = getPerson(identity.mother);
        if (mother != null)
        {
            if (mother.father > 0)
            {
                var mgf = getPerson(mother.father);
                if (mgf != null) relatives.Add((ClanRelation.MGF, mgf));
            }
            if (mother.mother > 0)
            {
                var mgm = getPerson(mother.mother);
                if (mgm != null) relatives.Add((ClanRelation.MGM, mgm));
            }
        }
        return relatives;
    }

    public static List<(ClanRelation relation, PersonalClanIdentity pci)> GetFatherGreatGeneration(PersonalClanIdentity identity)
    {
        var relatives = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return relatives;
        var clan = identity._specificClan;
        if (clan == null) return relatives;
        var father = getPerson(identity.father);
        // —— 3. 父系伯叔 & 姑（father的兄弟姐妹） ——
        if (father != null && father.father > 0)
        {
            var members = GetSiblingsWithRelation(father);
            foreach (var rPci in members)
            {
                // 男性 -> 伯叔（FUNC），女性 -> 姑（FANT）
                if (rPci.Item2.sex == ActorSex.Male) 
                    relatives.Add((ClanRelation.FUNC, rPci.Item2));
                else 
                    relatives.Add((ClanRelation.FANT, rPci.Item2));
            }
        }
        return relatives;
    }

    public static List<(ClanRelation relation, PersonalClanIdentity pci)> GetMotherGreatGeneration(PersonalClanIdentity identity)
    {
        var relatives = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return relatives;
        var clan = identity._specificClan;
        if (clan == null) return relatives;
        var mother = getPerson(identity.mother);
        // —— 4. 母系舅 & 姨（mother的兄弟姐妹） ——
        if (mother != null && mother.father > 0)
        {
            var members = GetSiblingsWithRelation(mother);
            foreach (var rPci in members)
            {
                // 男性 -> 舅舅（MUNC），女性 -> 姨（MANT）
                if (rPci.Item2.sex == ActorSex.Male) 
                    relatives.Add((ClanRelation.MUNC, rPci.Item2));
                else 
                    relatives.Add((ClanRelation.MANT, rPci.Item2));
            }
        }
        return relatives;
    }

    public static List<(ClanRelation relation, PersonalClanIdentity pci)> GetFatherSameGeneration(PersonalClanIdentity identity)
    {
        var relatives = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return relatives;
        var clan = identity._specificClan;
        if (clan == null) return relatives;
        var father = getPerson(identity.father);
        // —— 5. 堂兄弟／姐妹（父系哥哥弟弟的子女） ——
        if (father != null && father.father > 0)
        {
            var fSiblings = GetSiblingsWithRelation(father);
            foreach (var rPci in fSiblings)
            {
                var kids = SpecificClanManager.getChildren(rPci.Item2);
                foreach (var rPciKid in kids)
                {
                    // 性别决定男表/女表
                    if (rPciKid.Item2.sex == ActorSex.Male) 
                        relatives.Add((ClanRelation.FSIBB, rPciKid.Item2));
                    else 
                        relatives.Add((ClanRelation.FSIBG, rPciKid.Item2));
                }

            }
        }
        return relatives;
    }

    public static List<(ClanRelation relation, PersonalClanIdentity pci)> GetMotherSameGeneration(PersonalClanIdentity identity)
    {
        var relatives = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return relatives;
        var clan = identity._specificClan;
        if (clan == null) return relatives;
        var mother = getPerson(identity.mother);
        // —— 6. 表兄弟／姐妹（母系姐舅的子女） ——
        if (mother != null && mother.father > 0)
        {
            var mSiblings = GetSiblingsWithRelation(mother);
            foreach (var rPci in mSiblings)
            {
                var kids = SpecificClanManager.getChildren(rPci.Item2);
                foreach (var rPciKid in kids)
                {

                    if (rPciKid.Item2.sex == ActorSex.Male)
                        relatives.Add((ClanRelation.MSIBB, rPciKid.Item2));
                    else
                        relatives.Add((ClanRelation.MSIBG, rPciKid.Item2));
                }
            }
        }
        return relatives;
    }

    public static List<(ClanRelation relation, PersonalClanIdentity pci)> GetSiblingChildGeneration(PersonalClanIdentity identity)
    {
        var relatives = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return relatives;

        // 先拿到所有兄弟姐妹
        var siblings = GetSiblingsWithRelation(identity);
        foreach (var (sibRel, sibPci) in siblings)
        {
            if (sibPci == null) continue;

            // 再拿兄弟/姐妹的孩子
            var kids = SpecificClanManager.getChildren(sibPci);
            foreach (var (_, childPci) in kids)
            {
                if (childPci == null) continue;

                ClanRelation r;
                // 兄（Male） -> 侄子/侄女
                if (sibPci.sex == ActorSex.Male)
                    r = childPci.sex == ActorSex.Male ? ClanRelation.SBB : ClanRelation.SBG;
                // 姐（Female） -> 外甥/外甥女
                else
                    r = childPci.sex == ActorSex.Male ? ClanRelation.SGB : ClanRelation.SGG;

                relatives.Add((r, childPci));
            }
        }

        return relatives;
    }
    public static ClanRelation CalcRelation(PersonalClanIdentity self, PersonalClanIdentity target)
    {
        if (self == null || target == null) return ClanRelation.NONE;
        // 同一个人
        if (self.id == target.id) return ClanRelation.SELF;

        var clan = Get(self.specific_clan_id);
        if (clan == null) return ClanRelation.NONE;

        // 配偶
        if (self.hasLover() && self.lover.identity == target.id)
            return ClanRelation.LOV;
        // 小妾/男宠
        if (self.concubines.Any(c => c.identity == target.id))
            return ClanRelation.COB;

        // 父母
        if (self.father == target.id) return ClanRelation.FAT;
        if (self.mother == target.id) return ClanRelation.MOM;
        // 义父/义母
        if (self.father_in_law == target.id) return ClanRelation.FIL;
        if (self.mother_in_law == target.id) return ClanRelation.MIL;

        // 子女
        foreach (var (rel, pci) in getChildren(self))
            if (pci.id == target.id) return rel;

        // 兄弟姐妹
        foreach (var (rel, pci) in GetSiblingsWithRelation(self))
            if (pci.id == target.id) return rel;

        // 侄/外甥辈
        foreach (var (rel, pci) in GetSiblingChildGeneration(self))
            if (pci.id == target.id) return rel;

        // 祖父母辈
        foreach (var (rel, pci) in GetFatherGrandGeneration(self))
            if (pci.id == target.id) return rel;
        foreach (var (rel, pci) in GetMotherGrandGeneration(self))
            if (pci.id == target.id) return rel;

        // 叔伯（姑舅）辈
        foreach (var (rel, pci) in GetFatherGreatGeneration(self))
            if (pci.id == target.id) return rel;
        foreach (var (rel, pci) in GetMotherGreatGeneration(self))
            if (pci.id == target.id) return rel;

        // 堂/表兄弟姐妹辈
        foreach (var (rel, pci) in GetFatherSameGeneration(self))
            if (pci.id == target.id) return rel;
        foreach (var (rel, pci) in GetMotherSameGeneration(self))
            if (pci.id == target.id) return rel;
        //孙辈
        foreach (var (rel, pci) in GetGrandChildren(self))
            if (pci.id == target.id) return rel;

        // 最终默认无关系
        return ClanRelation.NONE;
    }

    public static void addSpecificClans(SpecificClan sc)
    {
        if (!_specificClans.Contains(sc))
        {
            _specificClans.Add(sc);
        }
    }
    public static PersonalClanIdentity getPerson(long identity_id)
    {
        SpecificClan[] clans;
        lock (_clansLock)
        {
            clans = _specificClans.ToArray(); // 在锁里做快照，避免复制过程也被并发写破坏
        }
        foreach (var sc in clans)
        {
            var pci = sc.GetPerson(identity_id);   // sc.GetPerson 自身也要线程安全（前面已给出两种实现）
            if (pci != null && pci.specific_clan_id == sc.id)
                return pci;
        }
        return null;
    }
    
    // 写：所有对 _specificClans 的 Add/Remove/Clear 必须同一把锁保护
    public static void AddClan(SpecificClan clan)
    {
        lock (_clansLock) _specificClans.Add(clan);
    }
    public static void RemoveClan(long id)
    {
        lock (_clansLock) _specificClans.RemoveAll(c => c.id == id);
    }
    public static void removePerson(PersonalClanIdentity pci)
    {
        foreach (var sc in _specificClans)
        {
            if (pci != null)
            {
                sc.removeActor(pci);
            }
        }
    }

    public static List<(ClanRelation, PersonalClanIdentity)> getChildren(PersonalClanIdentity identity)
    {
        var identityWithRelation = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity ==null)  return identityWithRelation;
        for (int i = 0; i < _specificClans.Count; i++)
        {
            var kids = _specificClans[i].GetChildren(identity);
            if (kids.Any())
            {
                return kids.ToList();
            }
        }
        return identityWithRelation;
    }

    public static SpecificClan CheckSpecificClan(this Actor actor, bool show_log = true)
    {
        if (actor == null) return null;
        if (!actor.hasClan())
        {
            World.world.clans.newClan(actor, true);
        }
        if (!actor.HasSpecificClan())
        {
            return newSpecificClan(actor, show_log);
        }
        return actor.GetSpecificClan();
    }

    public static SpecificClan Get(long id)
    {
        SpecificClan clan = _specificClans.Find(sc => sc.id == id);
        if (clan == null)
        {
            return null;
        } 
        return clan;
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
        
    }
}

public class PersonalClanIdentity
{
    public long id { get; set; }
    public long specific_clan_id { get; set; }
    public long actor_id { get; set; }
    public string merit = "";
    public string honoraryOfficial = "";
    public string PeeragesLevel = "";
    public string officialLevel = "";
    public string kingdomName = "";
    public string cityName = "";
    public string provinceName = "";
    public string educationLevel = "";
    public string culture = "";
    public string name { get; set; }
    [JsonIgnore]
    public SpecificClan _specificClan => SpecificClanManager.Get(specific_clan_id);
    public ActorSex sex { get; set; }
    public string birthday { get; set; }
    public string deathday { get; set; }
    public string species { get; set; }
    [JsonIgnore]
    public Actor _actor => World.world.units.get(actor_id);
    public bool is_alive { get; set; }
    public bool is_concubine {  get; set; } = false; //是否是小妾/男宠（当小妾/男宠无自身宗族时，会加入丈夫/妻子氏族并标记为小妾/男宠身份）
    public bool is_main { get; set; } = true; //在婚姻关系中是否为主要角色（对于爱人来说是嫁/入赘，还是娶/招亲）
    [JsonIgnore]
    public int age => is_alive?_actor.getAge():0;
    [JsonIgnore] public string isMainText => hasLover()?(is_main ? "i_first" : "i_second"):"i_none_lover";
    public int generation { get; set; }
    public long mother { get; set; } = -1L; //母亲
    public long father { get; set; } = -1L; //父亲
    public long father_in_law { get; set; } = -1L; //义父
    public long mother_in_law { get; set; } = -1L; //义母
    public (long specific_clan, long identity) lover = (-1L, -1L); //正妻/正夫
    public List<(long specific_clan, long identity)> concubines = new(); //小妾/情人

    public void newPersonalClanIdentity(SpecificClan specificClan, Actor a)
    {
        id = IdGenerator.NextId();
        is_alive = true;
        actor_id = a.getID();
        specific_clan_id = specificClan.id;
        name = a.getName();
        birthday = a.getBirthday();
        sex = a.data.sex;
        species = a.asset.id;
        is_main = true;
        culture = ConfigData.speciesCulturePair.TryGetValue(species, out string culturePair)? culturePair:"Western";
        generation = 0;
    }

    public void recordAllInfo()
    {
        culture = ConfigData.speciesCulturePair.TryGetValue(species, out string culturePair)? culturePair:"Western";
        Actor actor = _actor;
        if (actor == null) return;
        OfficeIdentity identity = null;
        if (actor.hasCity())
        {
            identity = actor.GetIdentity(actor.city.GetEmpire()); 
        }
        kingdomName = actor.kingdom.name;
        cityName = actor.hasCity()?actor.city.name:"";
        provinceName = actor.city.hasProvince() ? actor.city.GetProvince()?.name : "";
        if (identity!=null)
        {
            merit = string.Join("_", culture, "meritlevel", identity.peerageType.ToString(), identity.meritLevel);
            honoraryOfficial = string.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
            officialLevel = string.Join("_", culture, identity.officialLevel.ToString());
        }
        educationLevel = (actor.hasTrait("jingshi") ? "trait_jingshi" : "") +"/" +(actor.hasTrait("gongshi") ? "trait_gongshi" : "") +"/"+ (actor.hasTrait("juren")?"trait_juren":"");
        PeeragesLevel = string.Join("_", culture, actor.GetPeeragesLevel().ToString());
    }
    public bool isMale()
    {
        return sex == ActorSex.Male;
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
    
    public bool IsHeirPriority()
    {
        return (_specificClan.isMalePriority() && this.sex == ActorSex.Male) || (!_specificClan.isMalePriority() && this.sex == ActorSex.Female);
    }

    public bool CanHeir(PersonalClanIdentity identity)
    {
        if (identity == null) return false;
        return is_main && IsHeirPriority()&&is_alive&&identity.specific_clan_id==specific_clan_id&&identity.id!=id;
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
        is_main = IsHeirPriority();

        lpci.lover.specific_clan = specific_clan_id;
        lpci.lover.identity = id;
        lpci.is_main = !IsHeirPriority();
    }
    public void addConcubines(Actor actor)
    {
        _specificClan.addActor(actor, is_concubines:true);
        this.concubines.Add((this.specific_clan_id, actor.GetPersonalIdentity().id));
    }

    public void setParent(PersonalClanIdentity identity)
    {
        if (identity==null) return;
        if (identity.sex==ActorSex.Male)
        {
            father = identity.id;
        } else
        {
            mother = identity.id;
        }
        if (identity.is_main&&identity.is_alive)
        {
            _actor.setClan(identity._actor.clan);
        }

        sex = _actor.data.sex;
        generation = identity.generation + 1;
    }

    public void addChild(Actor actor, bool isNeedSetParentBoth=true)
    {
        PersonalClanIdentity pci = actor.GetPersonalIdentity() ?? actor.InitialPersonalIdentity(_specificClan);

        if (isNeedSetParentBoth)
        {
            if (actor.getParents().Any())
            {
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
        pci.generation = this.generation + 1;
        _specificClan.addActor(actor);
        actor.kingdom?.StartToChooseHeir();
    }
}
