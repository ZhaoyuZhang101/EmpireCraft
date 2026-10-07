using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;
using UnityEngine;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.System;
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
    [JsonIgnore]
    // 整族虚拟时没有实体族人，按族谱里在世族人记的物种取
    public ActorAsset asset => AllAliveMembers?.FirstOrDefault()?.asset ??
                               AssetManager.actor_library.get(SnapshotPeople()
                                   .FirstOrDefault(person => person != null && person.is_alive &&
                                                             !string.IsNullOrEmpty(person.species))?.species ?? "");
    // 在世族人(含虚拟族人)
    [JsonIgnore]
    public List<PersonalClanIdentity> LivingPeople =>
        CityPopulationSystem.AbstractPopulationEnabled ? LivingSnapshot().ToList() :
            SnapshotPeople().Where(person => person != null && person.is_alive).ToList();
    public long founder { get; set; }
    public SpecificClanType clan_sex_priority { get; set; }
    public string color { get; set; } = (new Color(0.7f, 0.8f, 0.7f)).ToHexString();
    public long ancestral_city_id { get; set; } = -1L;
    // 族长死于战乱或饥荒(无小人模式，见 VirtualGenealogySystem)：不再从虚拟族人里补族长；之后又有实体族人时清除
    public bool head_lost_to_calamity { get; set; }
    // 整族都已过世的时间(-1 = 还有在世族人)；绝嗣久了的小宗族会被族谱瘦身清掉，见 VirtualGenealogySystem
    public double extinct_since { get; set; } = -1d;
    // 平民宗族整族成为虚拟族人的时间(-1 = 还有实体族人)；沉寂太久就销户，见 VirtualGenealogySystem
    public double virtual_since { get; set; } = -1d;
    // 本宗族作为皇族统治的帝国灭亡的时间(-1 表示没有记录)，用于判定"宗室复国"的时效。
    public double empire_fall_timestamp { get; set; } = -1d;
    public long capital_city_id { get; set; }
    public string empire_name { get; set; }
    public float capital_city_pos_x { get; set; }
    public float capital_city_pos_y { get; set; }
    // —— 分家(见 ClanBranchSystem) ——
    // 从哪个宗族分出来的(-1 表示本身就是始祖宗族)；分支与原宗族同属一个世系
    public long parent_clan_id { get; set; } = -1L;
    // 分支的称号，如"越国支"/"苏城支"/"谈华房"
    public string branch_label { get; set; } = "";
    public string branch_reason { get; set; } = "";
    public double last_branch_timestamp { get; set; } = -1d;
    // 族人在各城最早定居的时间，用来判定"迁徙开基"
    public Dictionary<long, double> city_settle_since { get; set; } = new();

    // 显示名：始祖宗族是"谈宗族"，分支是"谈宗族·越国支"
    public string GetDisplayName()
    {
        string clanName = JoinNameParts(name, LM.Get("specific_clan"));
        return parent_clan_id < 0 || string.IsNullOrWhiteSpace(branch_label)
            ? clanName
            : string.Format(LM.Get("clan_branch_display"), clanName, branch_label);
    }

    // 顺着 parent_clan_id 找到始祖宗族(分支链断了就停在能找到的最上面一支)
    public SpecificClan GetRootClan()
    {
        SpecificClan current = this;
        var visited = new HashSet<long>();
        while (current.parent_clan_id >= 0 && visited.Add(current.id))
        {
            SpecificClan parent = SpecificClanManager.Get(current.parent_clan_id);
            if (parent == null) break;
            current = parent;
        }
        return current;
    }

    // 分家时把人从本宗族的名册里拿走(全局索引不动，身份只是换个宗族)
    public bool TakePerson(long personId)
    {
        lock (_cacheLock)
        {
            bool removed = _cache.Remove(personId);
            if (removed) _peopleRevision++;
            return removed;
        }
    }
    [JsonIgnore]
    public List<PersonalClanIdentity> all_valid_members => CityPopulationSystem.AbstractPopulationEnabled
        ? LivingSnapshot().Where(i => i.CanHeir()).ToList() : SnapshotPeople().ToList().FindAll(i=>i.CanHeir());
    [JsonIgnore] 
    // 有实体的在世族人(虚拟族人不在其中：他们没有单位，需要时用 PersonalClanIdentity.Realize() 落成)
    public List<Actor> AllAliveMembers => (CityPopulationSystem.AbstractPopulationEnabled ? LivingSnapshot() : SnapshotPeople())
        .Where(i => i.is_alive && !i.is_virtual)
        .Select(i=>i._actor).Where(actor => actor != null).ToList();
    [JsonIgnore]
    private readonly object _cacheLock = new();
    public Dictionary<long, PersonalClanIdentity> _cache = new();
    private Dictionary<long, PersonalClanIdentity> _peopleSource;
    private PersonalClanIdentity[] _livingSnapshot;
    private long _peopleRevision, _livingRevision = -1;
    private int _peopleCount = -1;
    [JsonIgnore]
    public int Count => CityPopulationSystem.AbstractPopulationEnabled ? LivingSnapshot().Length : SnapshotPeople().ToList().FindAll(i=>i.is_alive).Count;
    public int CountTotal { get { if (!CityPopulationSystem.AbstractPopulationEnabled) return SnapshotPeople().ToList().Count;
        lock (_cacheLock) return _cache.Count; } }

    public void InvalidatePeople() { lock (_cacheLock) _peopleRevision++; }

    private PersonalClanIdentity[] LivingSnapshot()
    {
        lock (_cacheLock)
        {
            if (_livingSnapshot == null || !ReferenceEquals(_peopleSource, _cache) || _peopleCount != _cache.Count ||
                _livingRevision != _peopleRevision)
            {
                _livingSnapshot = _cache.Values.Where(person => person != null && person.is_alive).ToArray();
                _peopleSource = _cache; _peopleCount = _cache.Count; _livingRevision = _peopleRevision;
            }
            return _livingSnapshot;
        }
    }
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
            _peopleRevision++;
        }
    }
    public void RecordHistoryEmpire(Empire empire, City capital)
    {
        if (empire == null || empire.isRekt() || capital == null || capital.isRekt()) return;
        empire_name = empire.GetEmpireName();
        capital_city_id = capital.getID();
        capital_city_pos_x = capital.city_center.x;
        capital_city_pos_y = capital.city_center.y;
    }
    public void RecordAncestralCity(City city)
    {
        if (city == null || city.isRekt()) return;
        ancestral_city_id = city.getID();
    }
    public City GetAncestralCity()
    {
        var city = World.world.cities.get(ancestral_city_id);
        if (city != null && !city.isRekt())
        {
            return city;
        }
        city = World.world.cities.get(capital_city_id);
        return city != null && !city.isRekt() ? city : null;
    }
    public bool HasAncestralCity()
    {
        return GetAncestralCity() != null;
    }
    public (string name, Vector2 pos, City city) GetHistoryEmpire()
    {
        var empireName = empire_name;
        var pos = new  Vector2(capital_city_pos_x, capital_city_pos_y);
        var city = World.world.cities.get(capital_city_id);
        return (empireName, pos, city);
    }
    public bool HasHistoryEmpire()
    {
        return !string.IsNullOrEmpty(empire_name);
    }
    public List<(ClanRelation, PersonalClanIdentity)> GetChildren(PersonalClanIdentity identity)
    {
        var children = new List<(ClanRelation, PersonalClanIdentity)>();
        if (identity == null) return children;
        foreach (var childId in identity.children)
        {
            var child = SpecificClanManager.getPerson(childId);
            if (child != null && child.specific_clan_id == this.id)
            {
                ClanRelation rel = child.isMale() ? ClanRelation.CHILDS : ClanRelation.CHILDD;
                children.Add((rel, child));
            }
        }
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
        RecordAncestralCity(SpecificClanManager.GetOriginCity(actor));
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
        if (actor == null || actor.IsWarMachine()) return;
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
                        pci.addChild(child, true, recordHistory: false);
                    }
                }
            }
        }
    }
    public void removeActor(PersonalClanIdentity identity)
    {
        lock (_cacheLock)
        {
            _cache.Remove(identity.id);
            _peopleRevision++;
        }
        SpecificClanManager._globalPersonLookup.Remove(identity.id);
        SpecificClanManager._actorToPersonLookup.Remove(identity.actor_id);

        if (identity.is_alive)
        {
            identity._actor?.RemoveSpecificClan();
        }
    }

    public void checkDispose()
    {
        // if (!SnapshotPeople().ToList().FindAll(i=>i.is_alive).Any())
        // {
        //     dispose();
        //     SpecificClanManager.RemoveClan(id);
        // }
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

        lock (_cacheLock)
        {
            foreach (var id in _cache.Keys)
            {
                SpecificClanManager._globalPersonLookup.Remove(id);
            }
            foreach (var pci in _cache.Values)
            {
                SpecificClanManager._actorToPersonLookup.Remove(pci.actor_id);
            }
            _cache.Clear();
            _peopleRevision++;
        }
    }
}

public static class SpecificClanManager
{
    private static readonly object _clansLock = new();
    public static List<SpecificClan> _specificClans = new List<SpecificClan>();
    public static Dictionary<long, PersonalClanIdentity> _globalPersonLookup = new Dictionary<long, PersonalClanIdentity>();
    public static Dictionary<long, PersonalClanIdentity> _actorToPersonLookup = new Dictionary<long, PersonalClanIdentity>();

    public static void RebuildCache()
    {
        lock (_clansLock)
        {
            _globalPersonLookup.Clear();
            _actorToPersonLookup.Clear();
            foreach (var sc in _specificClans)
            {
                foreach (var pci in sc.SnapshotPeople())
                {
                    _globalPersonLookup[pci.id] = pci;
                    if (pci.is_alive)
                    {
                        _actorToPersonLookup[pci.actor_id] = pci;
                    }
                    pci.children.Clear();
                }
            }
            foreach (var sc in _specificClans)
            {
                foreach (var pci in sc.SnapshotPeople())
                {
                    if (pci.father != -1L && _globalPersonLookup.TryGetValue(pci.father, out var fatherPci))
                    {
                        if (!fatherPci.children.Contains(pci.id))
                            fatherPci.children.Add(pci.id);
                    }
                    if (pci.mother != -1L && _globalPersonLookup.TryGetValue(pci.mother, out var motherPci))
                    {
                        if (!motherPci.children.Contains(pci.id))
                            motherPci.children.Add(pci.id);
                    }
                }
            }
            foreach (PersonalClanIdentity pci in _globalPersonLookup.Values)
            {
                pci.related_history_records = new List<PersonalHistoryRecord>();
            }
            foreach (PersonalClanIdentity owner in _globalPersonLookup.Values)
            {
                foreach (PersonalHistoryRecord record in owner.personal_history ?? new List<PersonalHistoryRecord>())
                {
                    record.owner_personal_identity_id = owner.id;
                    PersonalClanIdentity related = record.related_personal_identity_id > 0
                        ? getPerson(record.related_personal_identity_id)
                        : record.related_actor_id > 0 && _actorToPersonLookup.TryGetValue(record.related_actor_id, out var actorIdentity)
                            ? actorIdentity
                            : null;
                    related?.AddRelatedHistoryRecord(record);
                }
            }
        }
    }

    public static SpecificClan newSpecificClan(Actor actor, bool show_log = false)
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

    public static City GetOriginCity(Actor actor)
    {
        if (actor == null) return null;
        if (actor.hasCity()) return actor.city;
        if (actor.current_tile != null && actor.current_tile.hasCity()) return actor.current_tile.zone_city;
        if (actor.getParents().Any())
        {
            foreach (var parent in actor.getParents())
            {
                if (parent != null && parent.hasCity())
                {
                    return parent.city;
                }
            }
        }
        return null;
    }

    public static List<(ClanRelation, PersonalClanIdentity)> FindAllRelations(PersonalClanIdentity self)
    {
        var relations = new List<(ClanRelation, PersonalClanIdentity)>();
        if (self == null) return relations;

        // Parents
        var father = getPerson(self.father);
        if (father != null) relations.Add((ClanRelation.FAT, father));
        var mother = getPerson(self.mother);
        if (mother != null) relations.Add((ClanRelation.MOM, mother));
        
        // In-laws
        var fil = getPerson(self.father_in_law);
        if (fil != null) relations.Add((ClanRelation.FIL, fil));
        var mil = getPerson(self.mother_in_law);
        if (mil != null) relations.Add((ClanRelation.MIL, mil));

        // Lover
        if (self.hasLover())
        {
            var lover = getPerson(self.lover.identity);
            if (lover != null) relations.Add((ClanRelation.LOV, lover));
        }
        
        // Concubines
        foreach (var c in self.concubines)
        {
            var cob = getPerson(c.identity);
            if (cob != null) relations.Add((ClanRelation.COB, cob));
        }

        // Children
        relations.AddRange(getChildren(self));

        // Siblings
        relations.AddRange(GetSiblingsWithRelation(self));

        // Grandparents
        relations.AddRange(GetFatherGrandGeneration(self));
        relations.AddRange(GetMotherGrandGeneration(self));

        // Uncles/Aunts
        relations.AddRange(GetFatherGreatGeneration(self));
        relations.AddRange(GetMotherGreatGeneration(self));

        // Cousins
        relations.AddRange(GetFatherSameGeneration(self));
        relations.AddRange(GetMotherSameGeneration(self));

        // Nephews/Nieces
        relations.AddRange(GetSiblingChildGeneration(self));

        // Grandchildren
        relations.AddRange(GetGrandChildren(self));

        return relations;
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
                        siblings.Add((ClanRelation.SFDM, sibling)); // 同父异母
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
                        siblings.Add((ClanRelation.DFSM, sibling)); // 同母异父
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
    // 读档修复配偶记录(以前嫁娶时会覆盖对方原有的配偶，留下双方对不上的记录)，返回修正的条数：
    //   · 妾室名单里，记录的丈夫不是此人的，移出名单；
    //   · 妾：丈夫的妾室名单里没有她——其实是正妻就改正标记，否则清掉这条失效记录；
    //   · 正妻/正夫：双方互指则无误；对方没有配偶就补上回指；对方与第三人互指，说明这边是被覆盖的旧记录，清掉；
    //     对方指向的第三人没有回指，说明对方的记录失效，改为与这边互指。
    public static int RepairSpouseRecords()
    {
        List<PersonalClanIdentity> people;
        lock (_clansLock) people = _globalPersonLookup.Values.ToList();
        int fixes = 0;
        foreach (PersonalClanIdentity husband in people)
        {
            if (husband.concubines == null || husband.concubines.Count == 0) continue;
            fixes += husband.concubines.RemoveAll(entry =>
            {
                PersonalClanIdentity concubine = getPerson(entry.identity);
                return concubine == null || concubine.id == husband.id || concubine.lover.identity != husband.id;
            });
        }
        foreach (PersonalClanIdentity person in people)
        {
            if (!person.hasLover()) continue;
            PersonalClanIdentity partner = getPerson(person.lover.identity);
            if (partner == null || partner.id == person.id)
            {
                ClearLover(person);
                fixes++;
                continue;
            }
            bool listedAsConcubine = partner.concubines?.Any(entry => entry.identity == person.id) == true;
            if (person.is_concubine)
            {
                if (listedAsConcubine) continue;
                if (partner.lover.identity == person.id) person.is_concubine = false;
                else ClearLover(person);
                fixes++;
                continue;
            }
            if (partner.lover.identity == person.id) continue;
            fixes++;
            if (listedAsConcubine)
            {
                person.is_concubine = true;
                continue;
            }
            if (!partner.hasLover())
            {
                partner.lover = (person.specific_clan_id, person.id);
                continue;
            }
            PersonalClanIdentity third = getPerson(partner.lover.identity);
            if (third != null && third.lover.identity == partner.id) ClearLover(person);
            else partner.lover = (person.specific_clan_id, person.id);
        }
        return fixes;
    }

    private static void ClearLover(PersonalClanIdentity person)
    {
        person.lover = (-1L, -1L);
        person.is_concubine = false;
    }

    public static PersonalClanIdentity getPerson(long identity_id)
    {
        if (_globalPersonLookup.TryGetValue(identity_id, out var pci))
        {
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

    // 一次删掉一批宗族(族谱瘦身用，逐个 RemoveClan 是平方复杂度)
    public static void RemoveClans(HashSet<long> ids)
    {
        if (ids == null || ids.Count == 0) return;
        lock (_clansLock) _specificClans.RemoveAll(c => c == null || ids.Contains(c.id));
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
        if (identity == null) return identityWithRelation;
        foreach (var childId in identity.children)
        {
            var child = getPerson(childId);
            if (child != null)
            {
                ClanRelation rel = child.isMale() ? ClanRelation.CHILDS : ClanRelation.CHILDD;
                identityWithRelation.Add((rel, child));
            }
        }
        return identityWithRelation;
    }

    public static void CheckSpecificClan(this Actor actor, bool show_log = false)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(actor)) return;
        if (actor.isRekt()||actor.hasDied()) return;
        if (!actor.hasClan())
        {
            try
            {
                World.world.clans.newClan(actor, true);
            }
            catch
            {
                return;
            }
        }
        PersonalClanIdentity currentIdentity = actor.GetPersonalIdentity();
        if (currentIdentity != null) return;
        if (actor.HasSpecificClan()) actor.RemoveSpecificClan();

        // Reuse saved identity and clan links before creating a new specific clan. This
        // prevents uninitialized children from splitting away from their parent's house.
        if (_actorToPersonLookup.TryGetValue(actor.getID(), out var existingPci))
        {
            actor.SetPersonalIdentity(existingPci);
            return;
        }

        SpecificClan actorClan = actor.clan?.GetSpecificClan();
        if (actorClan != null)
        {
            actorClan.addActor(actor);
            var clanParent = actor.getParents().ToList().Find(a => a.GetPersonalIdentity()?.is_main ?? false);
            if (clanParent != null)
            {
                actor.GetPersonalIdentity()?.setParent(clanParent.GetPersonalIdentity(), recordHistory: false);
            }
            return;
        }

        var parent = actor.getParents().ToList().Find(a => a.GetPersonalIdentity()?.is_main ?? false);
        if (parent != null)
        {
            parent.GetSpecificClan()?.addActor(actor);
            actor.GetPersonalIdentity()?.setParent(parent.GetPersonalIdentity(), recordHistory: false);
            if (actor.GetPersonalIdentity() != null) return;
        }

        newSpecificClan(actor, show_log);
    }

    // "是不是同一家"：同一个宗族，或者同出一个始祖宗族的分支(别子为祖的王子、迁居他乡的一支仍是宗室)。
    // 两边都为空时跟原来的 == 一样算相同。
    public static bool SameLineage(SpecificClan a, SpecificClan b)
    {
        if (a == b) return true;
        if (a == null || b == null) return false;
        if (a.parent_clan_id < 0 && b.parent_clan_id < 0) return false;
        return a.GetRootClan() == b.GetRootClan();
    }

    // 按 id 找宗族：以前每次都在整个宗族列表里线性查找(几万个宗族时，每个族人取一次宗族就要扫几万遍，
    // 分户、族长、宗族分支这些逐人处理的地方因此每帧卡十几毫秒)。改用字典索引；列表的增删有好几处
    // 直接改 _specificClans，索引在数量变化或查到的对不上时整体重建
    private static readonly Dictionary<long, SpecificClan> _clanIndex = new();
    private static int _clanIndexCount = -1;
    private static List<SpecificClan> _clanIndexList;

    public static SpecificClan Get(long id)
    {
        lock (_clansLock)
        {
            if (_clanIndexCount != _specificClans.Count || !ReferenceEquals(_clanIndexList, _specificClans))
                RebuildClanIndex();
            if (!_clanIndex.TryGetValue(id, out SpecificClan clan)) return null;
            if (clan != null && clan.id == id) return clan;
            // 索引过期(宗族改过 id 或被原地替换)：重建一次再查
            RebuildClanIndex();
            return _clanIndex.TryGetValue(id, out clan) ? clan : null;
        }
    }

    private static void RebuildClanIndex()
    {
        _clanIndex.Clear();
        foreach (SpecificClan clan in _specificClans)
            if (clan != null) _clanIndex[clan.id] = clan;
        _clanIndexCount = _specificClans.Count;
        _clanIndexList = _specificClans;
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
    private const string UnresolvedChildNameMarker = "§";
    public long id { get; set; }
    public long specific_clan_id { get; set; }
    public long actor_id { get; set; }
    public string merit = "";
    public string honoraryOfficial = "";
    public string PeeragesLevel = "";
    public string officialLevel = "";
    public string kingdomName = "";
    public string cityName = "";
    public string educationLevel = "";
    public string culture = "";
    public string officeName = "";
    public string fullOfficeName = "";
    public string clanName = "";
    public string familyName = "";
    public string factionName = "";
    public List<string> ownedTitleNames = new List<string>();
    public string name { get; set; }
    [JsonIgnore]
    public SpecificClan _specificClan => SpecificClanManager.Get(specific_clan_id);
    public ActorSex sex { get; set; }
    public int recordedAge { get; set; } = -1;
    public string birthday { get; set; }
    public string deathday { get; set; }
    public string species { get; set; }
    [JsonIgnore]
    public Actor _actor => World.world.units.get(actor_id);
    public int rank = 1;
    private bool _isAlive;
    public bool is_alive
    {
        get => _isAlive;
        set { if (_isAlive == value) return; _isAlive = value; _specificClan?.InvalidatePeople(); }
    }
    public bool is_concubine {  get; set; } = false; //是否是小妾/男宠（当小妾/男宠无自身宗族时，会加入丈夫/妻子氏族并标记为小妾/男宠身份）
    public bool is_main { get; set; } = true; //在婚姻关系中是否为主要角色（对于爱人来说是嫁/入赘，还是娶/招亲）
    [JsonIgnore]
    public int age
    {
        get
        {
            Actor actor = _actor;
            if (is_alive && actor != null) return actor.getAge();
            if (is_alive && is_virtual && virtual_since >= 0d && World.world != null)
                return recordedAge + Date.getYearsSince(virtual_since);
            return recordedAge;
        }
    }
    [JsonIgnore] public string isMainText => hasLover()?(is_main ? "i_first" : "i_second"):"i_none_lover";
    public int generation { get; set; }
    public long mother { get; set; } = -1L; //母亲
    public long father { get; set; } = -1L; //父亲
    public long father_in_law { get; set; } = -1L; //义父
    public long mother_in_law { get; set; } = -1L; //义母
    public (long specific_clan, long identity) lover = (-1L, -1L); //正妻/正夫
    public List<(long specific_clan, long identity)> concubines = new(); //小妾/情人
    public List<long> children = new List<long>();
    public List<CrimeRecord> crime_records = new List<CrimeRecord>();
    public List<PersonalHistoryRecord> personal_history = new List<PersonalHistoryRecord>();
    [JsonIgnore]
    public List<PersonalHistoryRecord> related_history_records = new List<PersonalHistoryRecord>();
    public List<long> pending_child_birth_history_parents = new List<long>();
    public bool death_history_recorded = false;

    // —— 虚拟族谱(无小人模式，见 VirtualGenealogySystem) ——
    // 并入人口数据后没有实体单位、但仍在世的族人：只记人名、出生地、受封(爵位/封号)，需要时再落成实体
    public bool is_virtual { get; set; }
    public string birthplace { get; set; } = "";
    // 落成实体时生成在哪座城(并入时所在的城)
    public long virtual_city_id { get; set; } = -1L;
    // 成为虚拟族人的时间，用来推算年龄
    public double virtual_since { get; set; } = -1d;
    // 预定的寿数(60~80 随机)，到了就以病逝/遇刺/寿终之一身故
    public int virtual_death_age { get; set; } = -1;

    // 需要这个人(继承、分封、作乱……)时调用：虚拟族人当场落成实体，否则返回现有实体
    public Actor Realize() => is_virtual && is_alive ? VirtualGenealogySystem.Realize(this) : _actor;

    public void newPersonalClanIdentity(SpecificClan specificClan, Actor a)
    {
        id = IdGenerator.NextId();
        is_alive = true;
        actor_id = a.getID();
        specific_clan_id = specificClan.id;
        name = a.getName();
        birthday = a.getBirthday();
        sex = a.data.sex;
        recordedAge = a.getAge();
        species = a.asset.id;
        is_main = true;
        culture = CultureService.GetActorCulture(a);
        if (!CultureService.IsValidCulture(culture)) culture = "Western";
        generation = 0;
    }

    public void SetOfficeName(string name)
    {
        this.officeName = name;
    }
    public void recordAllInfo()
    {
        Actor actor = _actor;
        if (actor == null) return;
        culture = CultureService.GetActorCulture(actor);
        if (!CultureService.IsValidCulture(culture)) culture = "Western";
        recordedAge = actor.getAge();
        OfficeIdentity identity = null;
        if (actor.hasCity())
        {
            identity = actor.GetIdentity(); 
        }
        kingdomName = actor.kingdom.name;
        cityName = actor.hasCity()?actor.city.name:"";
        if (identity!=null)
        {
            merit = string.Join("_", culture, "meritlevel", identity.peerageType.ToString(), identity.meritLevel);
            honoraryOfficial = string.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
            officialLevel = string.Join("_", actor.kingdom.GetRegime().type, "officiallevel", identity.officialLevel.ToString());
        }
        if (actor.GetOffice() != null)
        {
            officeName = actor.GetOffice().GetOfficeName();
            fullOfficeName = actor.GetOffice().GetName(actor.GetOffice().meta_object);
        }
        clanName = actor.hasClan() ? actor.clan.data.name : "";
        familyName = actor.hasFamily() ? actor.family.data.name : "";
        factionName = actor.GetFaction()?.Name ?? "";
        ownedTitleNames = (actor.GetOwnedTitle() ?? new List<long>())
            .Select(titleId => ModClass.KINGDOM_TITLE_MANAGER.get(titleId)?.data?.name)
            .Where(titleName => !string.IsNullOrWhiteSpace(titleName))
            .Distinct()
            .ToList();
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

    // 能否与 partnerId 结为夫妻/纳为妾：没有配偶，或配偶已去世(丧偶)，或本来就是这个人
    public bool IsFreeToMarry(long partnerId)
    {
        if (!hasLover() || lover.identity == partnerId) return true;
        PersonalClanIdentity current = SpecificClanManager.getPerson(lover.identity);
        return current == null || !current.is_alive;
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

    public bool CanHeir(PersonalClanIdentity identity=null)
    {
        return is_main&&IsHeirPriority()&&is_alive&&identity?.id!=id;
    }

    public void setLover(Actor actor, bool isCus = false, bool recordHistory = true)
    {
        if (actor == null) return;
        if (!isCus)
        {
            if (this.lover!= (-1L, -1L)) return;
        }
        actor.CheckSpecificClan(false);
        PersonalClanIdentity lpci = actor.GetPersonalIdentity();
        if (lpci == null || lpci.id == id) return;
        // 对方已有在世的配偶、或已是别人的妾：不能再嫁娶(以前直接覆盖对方的配偶记录，
        // 原配偶那边还指着他，于是双方的配偶名字对不上)。丧偶的可以再婚
        if (!lpci.IsFreeToMarry(id)) return;
        lpci.lover.specific_clan = specific_clan_id;
        lpci.lover.identity = id;
        lpci.is_main = !IsHeirPriority();
        if (!isCus)
        {
            lover.specific_clan = lpci.specific_clan_id;
            lover.identity = lpci.id;
            is_main = IsHeirPriority();
            if (recordHistory)
            {
                string partnerName = actor.getName();
                if (string.IsNullOrWhiteSpace(partnerName)) partnerName = lpci.name;
                string selfName = _actor?.getName();
                if (string.IsNullOrWhiteSpace(selfName)) selfName = name;
                _actor?.RecordPersonalHistory(string.Format(LM.Get("personal_history_married"), partnerName),
                    "personal_history_married", actor.id, lpci.id);
                actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_married"), selfName),
                    "personal_history_married", _actor?.id ?? -1L, id);
            }
        }
        else
        {
            if (!concubines.Contains((lpci._specificClan.id, lpci.id)))
            {
                _actor.RecordAddLoverTime();
                lpci.is_main = false;
                is_main = true;
                lpci.is_concubine = true;
                concubines.Add((lpci._specificClan.id, lpci.id));
                if (recordHistory)
                {
                    string concubineName = actor.getName();
                    if (string.IsNullOrWhiteSpace(concubineName)) concubineName = lpci.name;
                    string selfName = _actor?.getName();
                    if (string.IsNullOrWhiteSpace(selfName)) selfName = name;
                    _actor?.RecordPersonalHistory(string.Format(LM.Get("personal_history_took_concubine"), concubineName),
                        "personal_history_took_concubine", actor.id, lpci.id);
                    actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_became_concubine"), selfName),
                        "personal_history_became_concubine", _actor?.id ?? -1L, id);
                }
            }
        }
    }

    public void setParent(PersonalClanIdentity identity, bool recordHistory = true)
    {
        if (identity==null) return;
        if (identity.sex==ActorSex.Male)
        {
            father = identity.id;
        } else
        {
            mother = identity.id;
        }

        bool addedChild = !identity.children.Contains(this.id);
        if (addedChild)
        {
            identity.children.Add(this.id);
            rank = identity.children.Count;
            if (recordHistory)
            {
                string childName = _actor?.GetModName()?.has_whole_name(_actor) == true
                    ? _actor.getName()
                    : UnresolvedChildNameMarker;
                identity._actor?.RecordPersonalHistory(string.Format(LM.Get("personal_history_child_born"), childName),
                    "personal_history_child_born", _actor?.id ?? -1L, id);
            }
        }

        if (identity.is_main&&identity.is_alive)
        {
            _actor.setClan(identity._actor.clan);
        }

        sex = _actor.data.sex;
        generation = identity.generation + 1;
    }

    public void RecordPendingChildBirthHistory()
    {
        if (pending_child_birth_history_parents == null || pending_child_birth_history_parents.Count == 0) return;
        if (_actor?.GetModName()?.has_whole_name(_actor) != true) return;
        string childName = _actor.getName();

        name = childName;
        foreach (long parentId in pending_child_birth_history_parents.ToList())
        {
            SpecificClanManager.getPerson(parentId)?._actor?.RecordPersonalHistory(
                string.Format(LM.Get("personal_history_child_born"), childName), "personal_history_child_born",
                _actor?.id ?? -1L, id);
        }
        pending_child_birth_history_parents.Clear();
    }

    public void AddRelatedHistoryRecord(PersonalHistoryRecord record)
    {
        if (record == null) return;
        related_history_records ??= new List<PersonalHistoryRecord>();
        if (!related_history_records.Contains(record))
        {
            related_history_records.Add(record);
        }
    }

    public void BackfillRelatedHistoryRecords()
    {
        Actor actor = _actor;
        if (actor == null || actor.GetModName()?.has_whole_name(actor) != true) return;
        string fullName = actor.getName();
        if (string.IsNullOrWhiteSpace(fullName)) return;

        foreach (PersonalHistoryRecord record in related_history_records ?? new List<PersonalHistoryRecord>())
        {
            if (record?.event_key == "personal_history_child_born" &&
                record.content?.Contains(UnresolvedChildNameMarker) == true)
            {
                record.content = string.Format(LM.Get(record.event_key), fullName);
            }
        }
    }

    public void BackfillCurrentEraNameHistory()
    {
        Actor actor = _actor;
        Empire empire = actor?.GetEmpire();
        if (actor == null || empire == null || empire.Emperor != actor || !empire.HasYearName() ||
            empire.data.newEmperor_timestamp < 0) return;

        string content = string.Format(LM.Get("personal_history_new_year_name"), empire.data.year_name);
        if (personal_history?.Any(record => record?.content == content) == true) return;

        personal_history ??= new List<PersonalHistoryRecord>();
        personal_history.Add(new PersonalHistoryRecord
        {
            timestamp = empire.data.newEmperor_timestamp,
            date = empire.data.year_name + "\u200A1" + LM.Get("Year"),
            content = content,
            kingdom_name = actor.kingdom?.GetKingdomName() ?? "",
            empire_id = empire.id,
            event_key = "personal_history_new_year_name",
            owner_personal_identity_id = id
        });
    }

    public void BackfillChildBirthHistoryNames()
    {
        if (personal_history == null || children == null || children.Count == 0) return;
        var namedChildren = children
            .Select(SpecificClanManager.getPerson)
            .Where(child => child != null && !string.IsNullOrWhiteSpace(GetChildName(child)))
            .ToList();
        if (namedChildren.Count == 0) return;

        var recordsByDay = personal_history
            .Where(IsUnnamedChildBirthRecord)
            .GroupBy(record => GetHistoryDay(record.timestamp));
        foreach (var recordGroup in recordsByDay)
        {
            List<PersonalClanIdentity> candidates = namedChildren
                .Where(child => GetHistoryDay(child.birthday) == recordGroup.Key)
                .ToList();
            if (candidates.Count == 0) continue;

            var assignedChildren = new HashSet<long>();
            foreach (PersonalHistoryRecord knownRecord in personal_history.Where(record => GetHistoryDay(record.timestamp) == recordGroup.Key))
            {
                foreach (PersonalClanIdentity child in candidates)
                {
                    string childName = GetChildName(child);
                    if (IsChildBirthRecord(knownRecord) && !IsUnnamedChildBirthRecord(knownRecord) &&
                        knownRecord.content?.Contains(childName) == true)
                    {
                        assignedChildren.Add(child.id);
                    }
                }
            }

            foreach (PersonalHistoryRecord record in recordGroup.OrderBy(record => record.timestamp))
            {
                PersonalClanIdentity child = candidates.FirstOrDefault(candidate => !assignedChildren.Contains(candidate.id));
                if (child == null) break;
                string childName = GetChildName(child);
                record.content = string.Format(LM.Get("personal_history_child_born"), childName);
                record.event_key = "personal_history_child_born";
                record.related_actor_id = child.actor_id;
                record.related_personal_identity_id = child.id;
                child.AddRelatedHistoryRecord(record);
                assignedChildren.Add(child.id);
            }
        }
    }

    private static bool IsUnnamedChildBirthRecord(PersonalHistoryRecord record)
    {
        if (record == null) return false;
        string emptyRecord = string.Format(LM.Get("personal_history_child_born"), "");
        return record.content == emptyRecord || record.content == "诞下子嗣。" || record.content == "誕下子嗣。" ||
               record.content?.Contains(UnresolvedChildNameMarker) == true;
    }

    private static bool IsChildBirthRecord(PersonalHistoryRecord record)
    {
        if (record?.content == null) return false;
        string template = LM.Get("personal_history_child_born");
        int placeholderIndex = template.IndexOf("{0}", StringComparison.Ordinal);
        string prefix = placeholderIndex >= 0 ? template.Substring(0, placeholderIndex) : template;
        return record.content.StartsWith(prefix, StringComparison.Ordinal) || record.content.StartsWith("诞下子嗣", StringComparison.Ordinal) ||
               record.content.StartsWith("誕下子嗣", StringComparison.Ordinal);
    }

    private static string GetChildName(PersonalClanIdentity child)
    {
        Actor actor = child?._actor;
        return actor?.GetModName()?.has_whole_name(actor) == true ? actor.getName() : "";
    }

    private static string GetHistoryDay(double timestamp)
    {
        return HistoryDateFormatter.GetMonthDay(timestamp);
    }

    private static string GetHistoryDay(string formattedDate)
    {
        return HistoryDateFormatter.GetMonthDay(-1, formattedDate);
    }

    public void addChild(Actor actor, bool isNeedSetParentBoth=true, bool recordHistory = true)
    {
        PersonalClanIdentity existingIdentity = actor.GetPersonalIdentity();
        bool isNewIdentity = existingIdentity == null;
        PersonalClanIdentity pci = existingIdentity ?? actor.InitialPersonalIdentity(_specificClan);

        if (isNeedSetParentBoth)
        {
            if (actor.getParents().Any())
            {
                foreach (var parent in actor.getParents().ToList())
                {
                    parent.CheckSpecificClan(false);
                    PersonalClanIdentity pIdentity = parent.GetPersonalIdentity();
                    pci.setParent(pIdentity, recordHistory);
                }
            }
            else
            {
                pci.setParent(this, recordHistory);
            }
        }
        else
        {
            pci.setParent(this, recordHistory);
        }
        pci.generation = this.generation + 1;
        _specificClan.addActor(actor);
        if (recordHistory && isNewIdentity)
        {
            actor.RecordPersonalHistory(LM.Get("personal_history_born"));
        }
        actor.kingdom?.StartToChooseHeir();
        actor.kingdom?.RemoveCalcHeirStatus();
    }
}

public class PersonalHistoryRecord
{
    public double timestamp { get; set; } = -1L;
    public string date { get; set; } = "";
    public string content { get; set; } = "";
    public string kingdom_name { get; set; } = "";
    public long empire_id { get; set; } = -1L;
    public string event_key { get; set; } = "";
    public long related_actor_id { get; set; } = -1L;
    public long related_personal_identity_id { get; set; } = -1L;
    public long owner_personal_identity_id { get; set; } = -1L;
}

public class CrimeRecord
{
    public int law_type { get; set; }
    public long kingdom_id { get; set; } = -1L;
    public long empire_id { get; set; } = -1L;
    public double crime_timestamp { get; set; } = -1L;
    public string crime_date { get; set; } = "";
    public bool resolved { get; set; } = false;
    public bool discovered { get; set; } = false;
    public double discovered_timestamp { get; set; } = -1L;
    public string discovered_date { get; set; } = "";
}
