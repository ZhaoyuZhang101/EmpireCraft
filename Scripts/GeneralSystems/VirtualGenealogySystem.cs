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
        actor.SetPersonalIdentity(person);
        person.actor_id = actor.id;
        person.is_virtual = false;
        person.recordedAge = person.age;
        person.virtual_since = -1d;
        VirtualIds.Remove(person.id);
        return actor;
    }

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
