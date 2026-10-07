using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 虚拟族谱(无小人模式)：族谱里的人被并入人口数据时不记为死亡，而是转为"虚拟族人"——
// 仍算在世，只保留人名、出生地、受封(爵位/封号)；继承、分封、作乱等需要这个人时，
// 调用 PersonalClanIdentity.Realize() 当场在原来所在的城里落成实体(从该城背景人口里扣一人，总人口不变)。
// 虚拟族人不会自己变老死去，所以每年按年龄结算：到了预定寿数(60~80 岁随机)就以病逝、遇刺或寿终之一身故，
// 写入个人经历，并从所在城的背景人口里减去这一人。
public static class VirtualGenealogySystem
{
    private const int MinDeathAge = 60;
    private const int MaxDeathAge = 80;
    // 身故方式的概率：病逝、遇刺(其余为寿终)；有爵位或封号的人遇刺的概率加倍
    private const float IllnessChance = 0.4f;
    private const float AssassinationChance = 0.08f;

    private static readonly HashSet<long> VirtualIds = new();
    private static object _world;
    private static double _lastDeathPass = -1d;

    public static void ResetWorldState()
    {
        VirtualIds.Clear();
        ClanHeads.Clear();
        Prominent.Clear();
        Magnates.Clear();
        MagnatesByCity.Clear();
        HeadQueue.Cancel();
        _world = null;
        _lastDeathPass = -1d;
    }

    // 读档后从全局族谱索引里重建虚拟族人名单
    private static void EnsureIndex()
    {
        if (ReferenceEquals(_world, World.world)) return;
        _world = World.world;
        VirtualIds.Clear();
        foreach (PersonalClanIdentity person in SpecificClanManager._globalPersonLookup.Values)
            if (person != null && person.is_alive && person.is_virtual) VirtualIds.Add(person.id);
    }

    public static int Count
    {
        get
        {
            EnsureIndex();
            return VirtualIds.Count;
        }
    }

    // 并入前调用：族谱里的人转为虚拟族人，并断开与即将移除的单位的联系(之后单位移除不会把他记为死亡)
    public static void Virtualize(Actor actor, City city)
    {
        PersonalClanIdentity person = actor?.GetPersonalIdentity();
        if (person == null || !person.is_alive || World.world == null) return;
        EnsureIndex();
        try
        {
            person.recordAllInfo();
        }
        catch
        {
            // 记录官职、爵位等附加信息失败不影响转为虚拟族人
        }
        if (string.IsNullOrWhiteSpace(person.birthplace))
            person.birthplace = city?.GetCityName() ?? actor.city?.GetCityName() ?? "";
        person.recordedAge = actor.getAge();
        person.is_virtual = true;
        person.virtual_city_id = city?.data?.id ?? actor.city?.data?.id ?? -1L;
        person.virtual_since = World.world.getCurWorldTime();
        if (person.virtual_death_age < 0)
            person.virtual_death_age = UnityEngine.Random.Range(MinDeathAge, MaxDeathAge + 1);
        actor.RemovePersonalIdentity();
        VirtualIds.Add(person.id);
    }

    // 落成实体：在原来所在的城(城没了就找同文化、同物种还有人的城)生成一人，套上族人的名字、性别、文化，
    // 并把族谱身份接回这个单位
    public static Actor Realize(PersonalClanIdentity person)
    {
        if (person == null || !person.is_alive || !person.is_virtual || World.world == null) return person?._actor;
        EnsureIndex();
        City city = FindHomeCity(person);
        if (city == null) return null;
        Actor actor = CityPopulationSystem.SpawnPerson(city, person.species, person.culture);
        if (actor?.data == null) return null;
        if (!string.IsNullOrWhiteSpace(person.name)) actor.data.name = person.name;
        actor.data.sex = person.sex;
        int age = person.age;
        long previousActorId = person.actor_id;
        actor.SetPersonalIdentity(person);
        person.actor_id = actor.id;
        person.is_virtual = false;
        person.recordedAge = age;
        person.virtual_since = -1d;
        SpecificClanManager._actorToPersonLookup[actor.id] = person;
        VirtualIds.Remove(person.id);
        // 年龄与族谱卡片一致；与有实体的亲人重新接上原版的家庭关系
        SetAge(actor, age);
        RestoreFamilyTies(actor, person, previousActorId);
        return actor;
    }

    #region 落成后的年龄与家庭关系(原版字段/方法用反射查找，找不到的部分跳过)

    private static bool _vanillaResolved;
    private static global::System.Reflection.FieldInfo _createdTime;
    private static global::System.Reflection.FieldInfo _ageOvergrowth;
    private static global::System.Reflection.FieldInfo _parent1;
    private static global::System.Reflection.FieldInfo _parent2;
    private static global::System.Reflection.MethodInfo _setFamily;
    private static global::System.Reflection.MethodInfo _setLover;

    private static void ResolveVanilla(Actor actor)
    {
        if (_vanillaResolved) return;
        _vanillaResolved = true;
        Type dataType = actor.data.GetType();
        _createdTime = HarmonyLib.AccessTools.Field(dataType, "created_time");
        _ageOvergrowth = HarmonyLib.AccessTools.Field(dataType, "age_overgrowth");
        _parent1 = HarmonyLib.AccessTools.Field(dataType, "parent_id_1");
        _parent2 = HarmonyLib.AccessTools.Field(dataType, "parent_id_2");
        _setFamily = HarmonyLib.AccessTools.GetDeclaredMethods(typeof(Actor))
            .FirstOrDefault(method => method.Name == "setFamily" && method.GetParameters().Length >= 1 &&
                                      method.GetParameters()[0].ParameterType == typeof(Family) &&
                                      method.GetParameters().Skip(1).All(parameter => parameter.HasDefaultValue));
        _setLover = HarmonyLib.AccessTools.GetDeclaredMethods(typeof(Actor))
            .FirstOrDefault(method => method.Name == "setLover" && method.GetParameters().Length >= 1 &&
                                      method.GetParameters()[0].ParameterType == typeof(Actor) &&
                                      method.GetParameters().Skip(1).All(parameter => parameter.HasDefaultValue));
        LogService.LogInfo($"[EmpireCraft][虚拟族谱] 同步年龄={(_ageOvergrowth != null || _createdTime != null ? "可用" : "不可用")} " +
                           $"亲子={(_parent1 != null ? "可用" : "不可用")} 家庭={(_setFamily != null ? "可用" : "不可用")} " +
                           $"配偶={(_setLover != null ? "可用" : "不可用")}");
    }

    private static object[] Args(global::System.Reflection.MethodInfo method, object first) =>
        method.GetParameters().Select((parameter, index) => index == 0 ? first : parameter.DefaultValue).ToArray();

    // 把单位的年龄调到 target：优先改"额外年龄"，否则把出生时间往前推(按实际 getAge() 结果二分查找，不依赖每年秒数)
    public static void SetAge(Actor actor, int target)
    {
        if (actor?.data == null || target <= 0) return;
        ResolveVanilla(actor);
        try
        {
            if (_ageOvergrowth != null && _ageOvergrowth.FieldType == typeof(int))
            {
                _ageOvergrowth.SetValue(actor.data, (int)_ageOvergrowth.GetValue(actor.data) + target - actor.getAge());
                if (actor.getAge() == target) return;
            }
            if (_createdTime == null || _createdTime.FieldType != typeof(double)) return;
            double born = (double)_createdTime.GetValue(actor.data);
            double step = 1d;
            int guard = 0;
            _createdTime.SetValue(actor.data, born - step);
            while (actor.getAge() < target && guard++ < 60)
            {
                step *= 2d;
                _createdTime.SetValue(actor.data, born - step);
            }
            double low = 0d, high = step;
            for (int i = 0; i < 50; i++)
            {
                double mid = (low + high) / 2d;
                _createdTime.SetValue(actor.data, born - mid);
                if (actor.getAge() >= target) high = mid;
                else low = mid;
            }
            _createdTime.SetValue(actor.data, born - high);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][虚拟族谱] 同步年龄失败: {exception.Message}");
        }
    }

    private static Actor RealActor(long identityId)
    {
        PersonalClanIdentity relative = identityId > 0 ? SpecificClanManager.getPerson(identityId) : null;
        if (relative == null || !relative.is_alive || relative.is_virtual) return null;
        Actor actor = relative._actor;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() ? actor : null;
    }

    // 只接回与有实体的人之间的关系：父母、子女、配偶，以及他们所在的原版氏族与家庭
    private static void RestoreFamilyTies(Actor actor, PersonalClanIdentity person, long previousActorId)
    {
        try
        {
            Actor father = RealActor(person.father);
            Actor mother = RealActor(person.mother);
            Actor spouse = RealActor(person.lover.identity);
            List<Actor> children = (person.children ?? new List<long>()).Select(RealActor)
                .Where(child => child != null).ToList();

            // 亲子：新单位记上父母(父母已不在世也按原来的单位 id 记，族谱照样能查到)；子女那边原来指向旧单位的改指新单位
            long fatherId = SpecificClanManager.getPerson(person.father)?.actor_id ?? -1L;
            long motherId = SpecificClanManager.getPerson(person.mother)?.actor_id ?? -1L;
            if (_parent1 != null && _parent1.FieldType == typeof(long) && fatherId > 0) _parent1.SetValue(actor.data, fatherId);
            if (_parent2 != null && _parent2.FieldType == typeof(long) && motherId > 0) _parent2.SetValue(actor.data, motherId);
            if (previousActorId > 0)
                foreach (Actor child in children)
                {
                    if (_parent1 != null && _parent1.FieldType == typeof(long) &&
                        (long)_parent1.GetValue(child.data) == previousActorId) _parent1.SetValue(child.data, actor.id);
                    if (_parent2 != null && _parent2.FieldType == typeof(long) &&
                        (long)_parent2.GetValue(child.data) == previousActorId) _parent2.SetValue(child.data, actor.id);
                }

            // 氏族：随有实体的父母/配偶/子女，否则随本宗族里有实体的族人
            Clan clan = father?.clan ?? mother?.clan ?? (person.is_main ? null : spouse?.clan) ??
                        children.Select(child => child.clan).FirstOrDefault(found => found != null) ??
                        person._specificClan?.AllAliveMembers.Select(member => member.clan)
                            .FirstOrDefault(found => found != null);
            if (clan != null && actor.clan != clan) actor.setClan(clan);

            // 配偶：对方有实体、还没有别的伴侣时重新结为伴侣，并进入对方的家庭
            if (spouse != null && _setLover != null && (spouse.lover == null || spouse.lover.isRekt()))
            {
                _setLover.Invoke(actor, Args(_setLover, spouse));
                if (spouse.lover != actor) _setLover.Invoke(spouse, Args(_setLover, actor));
            }
            // 家庭：配偶的家庭，其次(未成年时)父母的家庭，再次子女的家庭
            Family family = spouse?.family ?? (actor.isAdult() ? null : father?.family ?? mother?.family) ??
                            children.Select(child => child.family).FirstOrDefault(found => found != null);
            if (family != null && _setFamily != null && actor.family != family)
                _setFamily.Invoke(actor, Args(_setFamily, family));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][虚拟族谱] 接回家庭关系失败: {exception.Message}");
        }
    }

    #endregion

    private static City FindHomeCity(PersonalClanIdentity person)
    {
        City home = person.virtual_city_id > 0 ? World.world.cities.get(person.virtual_city_id) : null;
        if (home?.data != null && !home.isRekt() && home.kingdom != null) return home;
        // 原来的城没了：找同文化、同物种的城，人口最多的优先
        City best = null;
        float bestScore = -1f;
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null) continue;
            float score = CityPopulationSystem.GetTotal(city);
            if (CultureService.GetMainCulture(city) == person.culture) score += 10000f;
            if (score > bestScore)
            {
                bestScore = score;
                best = city;
            }
        }
        return best;
    }

    // 每年结算一次虚拟族人的身故(由 CityPopulationSystem 每帧调用，年度到期才做)
    public static void Tick()
    {
        if (World.world == null) return;
        EnsureIndex();
        // 上一轮还没检查完：接着分帧做(以前一帧检查全部虚拟族人，族谱大时一次卡几百毫秒)
        if (DeathQueue.Active)
        {
            // 身故检查做完就接着检查各宗族的族长
            if (DeathQueue.Tick() && CityPopulationSystem.AbstractPopulationEnabled)
            {
                ComputeMagnates();
                HeadQueue.Start(SpecificClanManager._specificClans.Where(clan => clan != null).Select(clan => clan.id).ToList());
            }
            return;
        }
        if (HeadQueue.Active)
        {
            HeadQueue.Tick();
            return;
        }
        double now = World.world.getCurWorldTime();
        if (_lastDeathPass >= 0d && now >= _lastDeathPass && Date.getYearsSince(_lastDeathPass) < 1) return;
        _lastDeathPass = now;
        if (VirtualIds.Count == 0) return;
        DeathQueue.Start(VirtualIds.ToList());
        DeathQueue.Tick();
    }

    #region 继承人(无小人模式)

    // 王位空缺、实体族人里找不到继承人时(皇族多半已成了虚拟族人)：按继承顺序——子女 → 孙辈 → 其他族人——
    // 在虚拟族人里挑一位能继承的(优先成年人)，当场落成实体。皇族连可继承的虚拟族人也没有时，
    // 从都城人口中生成一位宗室旁支编入皇族、改用皇族的姓，入继大统——封建王朝很少真正绝嗣
    private static readonly ClanRelation[][] HeirPriority =
    {
        new[] { ClanRelation.CHILD },
        new[] { ClanRelation.SSGB, ClanRelation.SSGG }
    };

    public static Actor RealizeHeir(Kingdom kingdom)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || kingdom?.data == null || kingdom.isRekt()) return null;
        PersonalClanIdentity last = SpecificClanManager.getPerson(kingdom.GetOrCreate().last_ruler_identity_id);
        SpecificClan clan = (kingdom.IsEmpire() ? kingdom.GetEmpire()?.EmpireSpecificClan : null) ?? last?._specificClan;
        PersonalClanIdentity heir = null;
        if (last != null)
        {
            List<(ClanRelation rel, PersonalClanIdentity id)> relations = SpecificClanManager.FindAllRelations(last);
            foreach (ClanRelation[] group in HeirPriority)
            {
                heir = PickHeir(relations.Where(pair => group.Contains(pair.rel)).Select(pair => pair.id), last);
                if (heir != null) break;
            }
            heir ??= PickHeir(relations.Select(pair => pair.id), last);
        }
        if (heir == null && clan != null) heir = PickHeir(clan.SnapshotPeople(), null);
        Actor actor = heir?.Realize();
        if (actor != null || clan == null || kingdom.capital == null) return actor;

        // 宗室旁支入继
        actor = CityPopulationSystem.SpawnCivilian(kingdom.capital);
        if (actor == null) return null;
        try
        {
            if (!string.IsNullOrWhiteSpace(clan.name))
            {
                actor.GetModName().familyName = clan.name;
                actor.GetModName().SetName(actor);
            }
            clan.addActor(actor);
            string text = string.Format(LM.Get("virtual_heir_cadet_history"), kingdom.GetKingdomName(), actor.getName());
            Layer.Empire empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
            if (empire != null) EventRecorder.Record(empire, text, actor);
            else EventRecorder.Record(kingdom, text, actor);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][虚拟族谱] 宗室旁支入继失败: {exception.Message}");
        }
        return actor;
    }

    private static PersonalClanIdentity PickHeir(IEnumerable<PersonalClanIdentity> people, PersonalClanIdentity last) =>
        people.Where(person => person != null && person.is_alive && person.is_virtual && person.CanHeir(last))
            .OrderByDescending(person => person.age >= HeadMinAge)
            .ThenByDescending(person => person.age)
            .FirstOrDefault();

    // 正在统治某个王国或帝国的宗族(永不销户)
    private static bool IsRulingClan(SpecificClan clan)
    {
        if (clan == null || World.world?.kingdoms == null) return false;
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            if (kingdom.IsEmpire() && kingdom.GetEmpire()?.EmpireSpecificClan == clan) return true;
            if (kingdom.king != null && !kingdom.king.isRekt() && kingdom.king.GetSpecificClan() == clan) return true;
        }
        return false;
    }

    #endregion

    #region 族长(无小人模式)

    // 显赫宗族(统治家族，或有族人做官、有爵位封地、是地主、入了党派、有功名、当君主城主)保留一名实体族长：
    // 族长不会被并入虚拟人口；整个宗族都成了虚拟族人时，每年从中挑一位成年人落成实体当族长。
    // 地方豪强(每城族人最多的两个宗族)也保留实体族长；
    // 其余平民宗族不留实体族长：族长和其他平民一样每月并回虚拟人口(每城每月最多并 10 人，几个月就清完)，
    // 宗族照样留在族谱里，族人以后做了官就又成为显赫宗族；整族虚拟、沉寂 DormantYears 年仍无人出仕的平民宗族销户。
    // 族长死于战乱或饥荒的宗族不再补族长；这样的宗族一个实体族人都不剩时销户——剩下的虚拟族人从族谱里注销，
    // 变成所在城的普通百姓(人数不变)，宗族记录留在族谱里
    private static readonly Dictionary<long, long> ClanHeads = new();
    private static readonly EmpireCraft.Scripts.HelperFunc.FrameBudgetQueue<long> HeadQueue =
        new(1.5d, CheckClanHead, "宗族族长检查");
    private const int HeadMinAge = 16;
    private const int DormantYears = 30;
    // 宗族是否显赫(每年检查族长时重算)；没算过的按显赫处理，读档后第一轮检查前不会误并族长
    private static readonly Dictionary<long, bool> Prominent = new();

    // 地方豪强：每座城族人最多的 MagnatesPerCity 个宗族，即使没人做官也保留实体族长
    private const int MagnatesPerCity = 2;
    private static readonly HashSet<long> Magnates = new();
    private static readonly Dictionary<long, List<long>> MagnatesByCity = new();

    // 某城的地方豪强宗族(族人多的在前)
    public static List<SpecificClan> MagnatesOf(City city)
    {
        var list = new List<SpecificClan>();
        if (city?.data == null || !MagnatesByCity.TryGetValue(city.data.id, out List<long> ids)) return list;
        foreach (long id in ids)
        {
            SpecificClan clan = SpecificClanManager.Get(id);
            if (clan != null) list.Add(clan);
        }
        return list;
    }

    private static void ComputeMagnates()
    {
        Magnates.Clear();
        MagnatesByCity.Clear();
        var byCity = new Dictionary<long, List<(long clan, int size)>>();
        foreach (SpecificClan clan in SpecificClanManager._specificClans)
        {
            if (clan == null) continue;
            int size = 0;
            long cityId = clan.ancestral_city_id;
            foreach (PersonalClanIdentity person in clan.SnapshotPeople())
            {
                if (person == null || !person.is_alive) continue;
                size++;
                if (person._actor?.city?.data != null && !person.is_virtual) cityId = person._actor.city.data.id;
            }
            if (size == 0 || cityId < 0) continue;
            if (!byCity.TryGetValue(cityId, out List<(long, int)> list)) byCity[cityId] = list = new List<(long, int)>();
            list.Add((clan.id, size));
        }
        foreach (KeyValuePair<long, List<(long clan, int size)>> pair in byCity)
            foreach ((long clan, int _) in pair.Value.OrderByDescending(entry => entry.size).Take(MagnatesPerCity))
            {
                Magnates.Add(clan);
                if (!MagnatesByCity.TryGetValue(pair.Key, out List<long> ids)) MagnatesByCity[pair.Key] = ids = new List<long>();
                ids.Add(clan);
            }
    }

    public static bool IsMagnate(SpecificClan clan) => clan != null && Magnates.Contains(clan.id);

    public static bool IsProminent(SpecificClan clan) =>
        clan != null && (!Prominent.TryGetValue(clan.id, out bool prominent) || prominent);

    // 族长人选：在世的实体族人里，符合本族继承性别的优先，其次正支，再取年长者
    private static PersonalClanIdentity PickHead(IEnumerable<PersonalClanIdentity> people) =>
        people.OrderByDescending(person => person.IsHeirPriority())
            .ThenByDescending(person => person.is_main)
            .ThenByDescending(person => person.age)
            .FirstOrDefault();

    public static bool IsClanHead(Actor actor)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return false;
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        SpecificClan clan = identity?._specificClan;
        if (clan == null || !IsProminent(clan)) return false;
        if (ClanHeads.TryGetValue(clan.id, out long headId))
        {
            PersonalClanIdentity head = SpecificClanManager.getPerson(headId);
            if (head != null && head.is_alive && !head.is_virtual && head._actor != null) return headId == identity.id;
        }
        PersonalClanIdentity chosen = PickHead(clan.SnapshotPeople()
            .Where(person => person.is_alive && !person.is_virtual && person._actor != null));
        if (chosen == null) return false;
        ClanHeads[clan.id] = chosen.id;
        return chosen.id == identity.id;
    }

    // 实体族长死于战乱(战死、所在城正在打仗)或饥荒(饿死、所在城缺粮)：记下来，之后不再补族长
    public static void OnActorDied(Actor actor, AttackType type)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || actor?.data == null || !IsClanHead(actor)) return;
        SpecificClan clan = actor.GetPersonalIdentity()?._specificClan;
        if (clan == null) return;
        City city = actor.city;
        Data.CityPopulationData data = city == null ? null : CityPopulationSystem.Get(city);
        bool calamity = type == AttackType.Weapon || type == AttackType.Starvation ||
                        city != null && CityPopulationSystem.IsAtWar(city) || data != null && data.last_food_shortage > 0f;
        if (calamity) clan.head_lost_to_calamity = true;
        ClanHeads.Remove(clan.id);
    }

    private static void CheckClanHead(long clanId)
    {
        SpecificClan clan = SpecificClanManager.Get(clanId);
        if (clan == null) return;
        PersonalClanIdentity[] people = clan.SnapshotPeople();
        List<PersonalClanIdentity> entities = people
            .Where(person => person.is_alive && !person.is_virtual && person._actor != null && !person._actor.isRekt())
            .ToList();
        bool ruling = IsRulingClan(clan);
        Prominent[clan.id] = ruling || IsMagnate(clan) ||
                             entities.Any(person => CityPopulationSystem.IsNotable(person._actor, includeClanHead: false));
        if (entities.Count > 0)
        {
            clan.head_lost_to_calamity = false;
            clan.virtual_since = -1d;
            return;
        }
        List<PersonalClanIdentity> virtuals = people.Where(person => person.is_alive && person.is_virtual).ToList();
        if (virtuals.Count == 0) return;
        // 统治家族永不销户
        if (clan.head_lost_to_calamity && !ruling)
        {
            Disperse(clan, virtuals);
            return;
        }
        // 平民宗族：不落成族长；整族虚拟沉寂太久就销户(族人编入所在城的普通百姓)。地方豪强照常落成族长
        if (!ruling && !IsMagnate(clan))
        {
            double now = World.world.getCurWorldTime();
            if (clan.virtual_since < 0d || now < clan.virtual_since) clan.virtual_since = now;
            else if (Date.getYearsSince(clan.virtual_since) >= DormantYears) Disperse(clan, virtuals, dormant: true);
            return;
        }
        PersonalClanIdentity heir = PickHead(virtuals.Where(person => person.age >= HeadMinAge)) ?? PickHead(virtuals);
        Actor head = heir?.Realize();
        if (head != null) ClanHeads[clan.id] = heir.id;
    }

    private static void Disperse(SpecificClan clan, List<PersonalClanIdentity> virtuals, bool dormant = false)
    {
        string date = Date.getDate(World.world.getCurWorldTime());
        foreach (PersonalClanIdentity person in virtuals)
        {
            person.recordedAge = person.age;
            person.is_alive = false;
            person.is_virtual = false;
            person.deathday = date;
            if (!person.death_history_recorded)
            {
                person.death_history_recorded = true;
                person.RecordPersonalHistory(LM.Get(dormant ? "virtual_person_clan_dormant" : "virtual_person_clan_dispersed"));
            }
            VirtualIds.Remove(person.id);
        }
        ClanHeads.Remove(clan.id);
        Prominent.Remove(clan.id);
        LogService.LogInfo(dormant
            ? $"[EmpireCraft][虚拟族谱] 宗族 {clan.name} 沉寂 {DormantYears} 年无人出仕，销户编入民籍({virtuals.Count} 人)"
            : $"[EmpireCraft][虚拟族谱] 宗族 {clan.name} 族长死于战乱饥荒、已无实体族人，销户({virtuals.Count} 人)");
    }

    #endregion

    private static readonly EmpireCraft.Scripts.HelperFunc.FrameBudgetQueue<long> DeathQueue = new(1.5d, CheckDeath, "虚拟族谱年度身故");

    private static void CheckDeath(long id)
    {
        PersonalClanIdentity person = SpecificClanManager.getPerson(id);
        if (person == null || !person.is_alive || !person.is_virtual)
        {
            VirtualIds.Remove(id);
            return;
        }
        if (person.virtual_death_age < 0)
            person.virtual_death_age = UnityEngine.Random.Range(MinDeathAge, MaxDeathAge + 1);
        if (person.age >= person.virtual_death_age)
        {
            Die(person);
            return;
        }
        TryBirth(person);
    }

    // ---- 虚拟族人生育 ----
    // 虚拟族人也成家生子：宗族里继承一方性别(男系宗族看男子，女系看女子)的正支成年人，
    // 18~40 岁每年有 BirthChance 的机会添一名虚拟子女(在世子女不超过 MaxChildren)，记入族谱；
    // 孩子算在所在城的背景人口里(人口的生育已由人口数据层结算，这里只是给族谱记上名字)
    private const int BirthMinAge = 18;
    private const int BirthMaxAge = 40;
    private const float BirthChance = 0.09f;
    private const int MaxChildren = 4;
    // 宗族在世人数(含虚拟族人)达到这个数就不再添丁，免得一代代按指数增长
    private const int MaxClanAlive = 30;

    private static void TryBirth(PersonalClanIdentity parent)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || !parent.is_main || !parent.IsHeirPriority()) return;
        int age = parent.age;
        if (age < BirthMinAge || age > BirthMaxAge || UnityEngine.Random.value >= BirthChance) return;
        SpecificClan clan = parent._specificClan;
        if (clan == null) return;
        int living = parent.children.Count(id => SpecificClanManager.getPerson(id)?.is_alive == true);
        if (living >= MaxChildren) return;
        if (clan.SnapshotPeople().Count(person => person.is_alive) >= MaxClanAlive) return;
        double now = World.world.getCurWorldTime();
        var child = new PersonalClanIdentity
        {
            id = OverallHelperFunc.IdGenerator.NextId(),
            specific_clan_id = clan.id,
            actor_id = -1L,
            is_alive = true,
            is_virtual = true,
            virtual_since = now,
            virtual_city_id = parent.virtual_city_id,
            recordedAge = 0,
            birthday = Date.getDate(now),
            sex = UnityEngine.Random.value < 0.5f ? ActorSex.Male : ActorSex.Female,
            species = parent.species,
            culture = parent.culture,
            generation = parent.generation + 1,
            is_main = true
        };
        if (parent.isMale()) child.father = parent.id;
        else child.mother = parent.id;
        child.name = ChildName(clan, child, parent);
        lock (clan)
        {
            clan._cache[child.id] = child;
        }
        SpecificClanManager._globalPersonLookup[child.id] = child;
        parent.children.Add(child.id);
        VirtualIds.Add(child.id);
    }

    private static string ChildName(SpecificClan clan, PersonalClanIdentity child, PersonalClanIdentity parent)
    {
        string generated = CultureService.GenerateCulturalName(MetaType.Unit, child.id, parent.culture);
        string[] parts = (generated ?? "").SplitNameParts();
        string given = parts.Length > 0 ? parts[parts.Length - 1] : "";
        if (string.IsNullOrWhiteSpace(given)) given = generated ?? "";
        return string.IsNullOrWhiteSpace(clan.name) ? given : OverallHelperFunc.JoinNameParts(clan.name, given);
    }

    private static void Die(PersonalClanIdentity person)
    {
        bool titled = person.ownedTitleNames?.Count > 0 || !string.IsNullOrWhiteSpace(person.officeName);
        float roll = UnityEngine.Random.value;
        float assassination = AssassinationChance * (titled ? 2f : 1f);
        string cause = roll < assassination ? "assassinated" : roll < assassination + IllnessChance ? "illness" : "natural";
        person.recordedAge = person.age;
        person.is_alive = false;
        person.is_virtual = false;
        person.deathday = Date.getDate(World.world.getCurWorldTime());
        if (!person.death_history_recorded)
        {
            person.death_history_recorded = true;
            person.RecordPersonalHistory(string.Format(LM.Get($"virtual_person_death_{cause}"), person.recordedAge));
        }
        VirtualIds.Remove(person.id);
        // 这个人原本算在所在城的背景人口里
        City city = person.virtual_city_id > 0 ? World.world.cities.get(person.virtual_city_id) : null;
        if (city != null) CityPopulationSystem.RemovePerson(city, person.species, person.culture);
        try
        {
            person._specificClan?.checkDispose();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 虚拟族人身故后整理宗族失败: {exception.Message}");
        }
    }
}
