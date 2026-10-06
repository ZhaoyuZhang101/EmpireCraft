using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
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
    private static void SetAge(Actor actor, int target)
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
        double now = World.world.getCurWorldTime();
        if (_lastDeathPass >= 0d && now >= _lastDeathPass && Date.getYearsSince(_lastDeathPass) < 1) return;
        _lastDeathPass = now;
        if (VirtualIds.Count == 0) return;
        using var timing = new PerfTimer("虚拟族谱年度身故");
        foreach (long id in VirtualIds.ToList())
        {
            PersonalClanIdentity person = SpecificClanManager.getPerson(id);
            if (person == null || !person.is_alive || !person.is_virtual)
            {
                VirtualIds.Remove(id);
                continue;
            }
            if (person.virtual_death_age < 0)
                person.virtual_death_age = UnityEngine.Random.Range(MinDeathAge, MaxDeathAge + 1);
            if (person.age >= person.virtual_death_age) Die(person);
        }
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
