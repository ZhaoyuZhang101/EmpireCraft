using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 亲属名册：判断"有几个皇子、兄弟、宗室"时连族谱里的虚拟族人一起数，真要用到人时才落成实体。
// 纯净地图/无小人模式下多数族人是虚拟族人(PersonalClanIdentity.is_virtual，地图上没有单位)：
// 以前写 actor.getChildren()、clan.getUnits()、identity._actor 只看得到地图上的单位，
// "两个成年皇子""有宗室可立"这类条件几乎永远凑不齐(用户 2026-11-18 "所有涉及到人物的虚拟人物都要算上")。
// 计数与判断只看族谱(is_alive、age、sex、所在的城)，不落成；落成走 VirtualGenealogySystem.RealizeAll/RealizeFirst，
// 落成后不合格的当场并回人口
public static class KinRoster
{
    // 虚拟族人按族谱年龄算成年(地图上的人照原版 isAdult)
    public const int AdultAge = 16;

    // 在世子女：族谱(含虚拟族人)，再补原版亲子关系里有族谱身份、模组索引漏了的(旧档)
    public static List<PersonalClanIdentity> Children(Actor parent) =>
        Children(parent?.GetPersonalIdentity(), parent);

    public static List<PersonalClanIdentity> Children(PersonalClanIdentity parent, Actor actor = null)
    {
        var result = new List<PersonalClanIdentity>();
        var seen = new HashSet<long>();
        if (parent != null)
            foreach (var pair in SpecificClanManager.getChildren(parent))
                if (pair.Item2 != null && pair.Item2.is_alive && seen.Add(pair.Item2.id)) result.Add(pair.Item2);
        actor ??= parent?._actor;
        if (actor?.data == null || actor.isRekt()) return result;
        foreach (Actor child in actor.getChildren(false) ?? Enumerable.Empty<Actor>())
        {
            if (child?.data == null || child.isRekt() || !child.isAlive()) continue;
            PersonalClanIdentity identity = child.GetPersonalIdentity();
            if (identity != null && identity.is_alive && seen.Add(identity.id)) result.Add(identity);
        }
        return result;
    }

    // 在世兄弟姐妹(族谱)
    public static List<PersonalClanIdentity> Siblings(PersonalClanIdentity person) =>
        person == null ? new List<PersonalClanIdentity>()
            : SpecificClanManager.GetSiblingsWithRelation(person).Select(pair => pair.Item2)
                .Where(sibling => sibling != null && sibling.is_alive).GroupBy(sibling => sibling.id)
                .Select(group => group.First()).ToList();

    // 世系里在世的族人(本宗和分出去的各支，见 SpecificClanManager.LineageClans)
    public static IEnumerable<PersonalClanIdentity> Lineage(SpecificClan clan) =>
        clan == null ? Enumerable.Empty<PersonalClanIdentity>()
            : SpecificClanManager.LineageClans(clan).SelectMany(member => member.LivingPeople)
                .Where(person => person != null && person.is_alive);

    // 在地图上(有活着的单位)
    public static bool OnMap(PersonalClanIdentity person) =>
        person != null && person.is_alive && !person.is_virtual && person._actor?.data != null &&
        !person._actor.isRekt() && person._actor.isAlive();

    // 虚拟族人(族谱里在世、地图上没有单位)
    public static bool Virtual(PersonalClanIdentity person) =>
        person != null && person.is_alive && person.is_virtual && (person._actor?.data == null || person._actor.isRekt() || !person._actor.isAlive());

    // 算得上的人：地图上的按单位判(fitActor)，虚拟族人按族谱判(fitPerson)；两样都不是(族谱说在世、单位却没了)的不算
    public static bool Counts(PersonalClanIdentity person, Func<PersonalClanIdentity, bool> fitPerson, Func<Actor, bool> fitActor)
    {
        if (person == null || !person.is_alive) return false;
        if (OnMap(person)) return fitActor == null || fitActor(person._actor);
        return Virtual(person) && (fitPerson == null || fitPerson(person));
    }

    public static bool Adult(PersonalClanIdentity person) =>
        OnMap(person) ? person._actor.isAdult() : person != null && person.age >= AdultAge;

    public static bool Male(PersonalClanIdentity person) =>
        OnMap(person) ? person._actor.isSexMale() : person?.sex == ActorSex.Male;

    // 所在的城：地图上的看单位，虚拟族人看记下的城(城没了为空)
    public static City CityOf(PersonalClanIdentity person)
    {
        if (person == null) return null;
        if (OnMap(person)) return person._actor.city;
        City city = person.virtual_city_id > 0 ? World.world?.cities?.get(person.virtual_city_id) : null;
        return city?.data == null || city.isRekt() ? null : city;
    }

    public static Kingdom KingdomOf(PersonalClanIdentity person) =>
        OnMap(person) ? person._actor.kingdom : CityOf(person)?.kingdom;

    // 落成：地图上的直接取单位，虚拟族人落成后再用 fitActor 复核，不合格的并回人口
    public static List<Actor> Realize(IEnumerable<PersonalClanIdentity> people, Func<Actor, bool> fitActor) =>
        VirtualGenealogySystem.RealizeAll(people, fitActor).Where(actor => actor != null).Distinct().ToList();

    // 改立、迎立宗亲：世系里在 inRealm 之内的成年虚拟族人，男子在先、年长的在前，落成第一个合格的(至多试 max 人)
    public static Actor RealizeAdultKin(SpecificClan clan, Func<Kingdom, bool> inRealm, Func<Actor, bool> fitActor, int max = 2) =>
        clan == null ? null
            : RealizeFirst(Lineage(clan).Where(person => Virtual(person) && person.age >= AdultAge && !person.is_concubine &&
                                                          !person.disinherited && inRealm(KingdomOf(person)))
                .OrderBy(person => person.sex == ActorSex.Male ? 0 : 1).ThenByDescending(person => person.age)
                .ThenBy(person => person.id), fitActor, max);

    public static Actor RealizeFirst(IEnumerable<PersonalClanIdentity> people, Func<Actor, bool> fitActor, int maxRealized = 3) =>
        VirtualGenealogySystem.RealizeFirst(people, fitActor, maxRealized);
}
