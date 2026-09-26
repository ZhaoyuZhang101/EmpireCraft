using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 共主联盟(西方古典共和城邦 + 西方封建制)：一个人可以同时是好几个王国的君主。
//   · 同一君主名下、都不在帝国里的王国自动结成原版联盟，以盟主国的名字命名(帝国和联盟不兼容，
//     帝国里的王国只共用君主、不结盟)；
//   · 城邦：分割继承法解锁前，只有影响力达到500的子嗣能续任共主，否则各城邦另立本地君主；
//     解锁后，其余入盟城邦跟随盟主城邦选出的新君主；
//   · 封建：共主去世后按长幼每人一国(不分男女)，长子女得最主要的王国，王国比子女多时多出的归长子女；
//   · 封建王国的继承人本就是别国君主时，不再把两国合并，而是由他兼领(共主)；
//   · 这两种政体下都不能用"统合治下王国"决议把几个王国并成一个。
public static class PersonalUnionService
{
    public static bool IsUnionRegime(Kingdom kingdom) =>
        kingdom != null && !kingdom.isRekt() &&
        kingdom.GetRegime()?.type is RegimeType.Feudalism or RegimeType.ClassicalRepublic;

    private static bool IsFeudal(Kingdom kingdom) => kingdom?.GetRegime()?.type == RegimeType.Feudalism;

    public static bool AllowsFemaleSuccession(Kingdom kingdom) => IsFeudal(kingdom);

    public static List<Kingdom> GetRealms(Actor ruler) =>
        ruler == null
            ? new List<Kingdom>()
            : World.world.kingdoms.Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom.king == ruler)
                .ToList();

    public static bool IsAtFeudalRealmLimit(Actor ruler) => ruler != null &&
        IsFeudal(ruler.kingdom) &&
        InstitutionSystem.IsEnacted(CultureService.GetRealmCulture(ruler.kingdom), "western_feudalization") &&
        GetRealms(ruler).Count(IsFeudal) >= ModClass.FEUDAL_UNION_REALM_LIMIT;

    public static bool CrownLocalVassal(Kingdom realm, Kingdom overlord)
    {
        Actor local = (realm?.units ?? new List<Actor>()).ToList()
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                            !actor.isKing() && actor.isUnitFitToRule())
            .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        if (local == null || !FeudalVassalService.CanBind(overlord, realm)) return false;
        GraceEdictService.Crown(realm, local);
        return realm.king == local && FeudalVassalService.Bind(overlord, realm);
    }

    // 共主兼领：直接 setKing，不走官职任命(任命会把他迁到对方都城，离开自己的国家)
    public static void CrownInUnion(Kingdom kingdom, Actor ruler)
    {
        if (kingdom == null || kingdom.isRekt() || ruler == null || ruler.isRekt()) return;
        if (kingdom.king == ruler) return;
        if (kingdom.hasKing()) kingdom.removeKing();
        kingdom.setKing(ruler);
    }

    // 由 KingdomPatch 的 setKing 补丁调用：某人名下有两个以上不在帝国里的共主政体王国时，保证它们同盟
    public static void OnKingCrowned(Kingdom kingdom, Actor king)
    {
        if (!IsUnionRegime(kingdom) || king == null || king.isRekt()) return;
        if (kingdom.hasAlliance())
        {
            Alliance existing = kingdom.getAlliance();
            if (kingdom.GetOrCreate().union_alliance_leader_kingdom_id < 0)
            {
                foreach (Kingdom member in existing.kingdoms_hashset.ToList())
                {
                    if (member == null || member == kingdom ||
                        member.GetOrCreate().union_alliance_leader_kingdom_id != kingdom.id || member.king == king ||
                        member.GetOrCreate().union_leader_kingdom_id == kingdom.id)
                        continue;
                    member.GetOrCreate().union_alliance_leader_kingdom_id = -1L;
                    existing.leave(member);
                }
            }
            else
            {
                Kingdom leader = World.world.kingdoms.get(kingdom.GetOrCreate().union_alliance_leader_kingdom_id);
                if (leader?.king != king)
                {
                    kingdom.GetOrCreate().union_alliance_leader_kingdom_id = -1L;
                    existing.leave(kingdom);
                }
            }
        }
        List<Kingdom> realms = GetRealms(king)
            .Where(realm => IsUnionRegime(realm) && !realm.IsEmpire() && !realm.IsInEmpire()).ToList();
        if (realms.Count < 2) return;
        Kingdom primary = realms.Contains(king.kingdom) ? king.kingdom : realms[0];
        foreach (Kingdom realm in realms.Where(realm => realm != primary)) JoinAlliance(primary, realm);
        NameAllianceAfterLeader(primary);
    }

    // 由 ActorPatch.Die 前缀在君主去世时调用(此时还能查到他名下的王国)
    public static void OnRulerDying(Actor ruler)
    {
        List<Kingdom> realms = GetRealms(ruler);
        if (realms.Count < 2) return;
        Kingdom home = realms.Contains(ruler.kingdom) ? ruler.kingdom : null;

        List<Kingdom> cityRealms = realms.Where(CityStateService.IsCityState).ToList();
        Kingdom cityHome = CityStateService.IsCityState(home) ? home : cityRealms.OrderBy(realm => realm.id).FirstOrDefault();
        if (cityHome != null && cityRealms.Count >= 2)
        {
            bool partitionUnlocked = SuccessionLawSystem.IsSuccessionLawUnlocked(cityHome,
                SuccessionLawType.分割继承法);
            Actor qualifiedChild = null;
            if (!partitionUnlocked && ruler.GetPersonalIdentity() is PersonalClanIdentity cityIdentity)
                qualifiedChild = SpecificClanManager.getChildren(cityIdentity)
                    .Select(item => item.Item2)
                    .Where(person => person != null && person.is_alive)
                    .OrderBy(person => person.rank)
                    .Select(person => person._actor)
                    .FirstOrDefault(child => child != null && !child.isRekt() && child.isAlive() &&
                                             child.isUnitFitToRule() && (child.data?.renown ?? 0) >= 500);

            if (!partitionUnlocked && qualifiedChild == null)
                DissolveCityStateUnion(cityRealms);
            else
            {
                if (qualifiedChild != null)
                    cityHome.GetOrCreate().union_city_state_heir_id = qualifiedChild.id;
                foreach (Kingdom realm in cityRealms.Where(realm => realm != cityHome))
                    realm.GetOrCreate().union_leader_kingdom_id = cityHome.id;
            }
        }

        // 封建：按长幼每人一国，不分男女；主要王国(本国，其次城多的)排在前面
        List<Kingdom> feudalRealms = realms.Where(IsFeudal)
            .OrderByDescending(realm => realm == home)
            .ThenByDescending(realm => realm.cities?.Count ?? 0).ThenBy(realm => realm.id)
            .ToList();
        if (feudalRealms.Count < 2) return;
        PersonalClanIdentity identity = ruler.GetPersonalIdentity();
        if (identity == null) return;
        List<Actor> children = SpecificClanManager.getChildren(identity)
            .Select(item => item.Item2)
            .Where(person => person != null && person.is_alive)
            .OrderBy(person => person.rank)
            .Select(person => person._actor)
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isUnitFitToRule())
            .ToList();
        if (children.Count == 0) return;
        for (int i = 0; i < feudalRealms.Count; i++)
            feudalRealms[i].GetOrCreate().union_partition_heir_id = (i < children.Count ? children[i] : children[0]).id;
    }

    // 由 EmpireCraftKingdomBehCheckKing 在王国没有在世国王时调用；返回 true 表示已处理(或需等待)。
    // 注意原版君主死亡时只清空他本国的国王，他兼领的其他王国里仍挂着这位已故君主，所以按"在世"判断。
    public static bool TryHandleVacancy(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return false;
        if (kingdom.hasKing() && kingdom.king != null && !kingdom.king.isRekt() && kingdom.king.isAlive()) return false;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();

        if (data.union_city_state_local_succession)
        {
            if (CityStateService.TryCrownLocalRuler(kingdom))
                data.union_city_state_local_succession = false;
            return true;
        }

        if (data.union_city_state_heir_id >= 0)
        {
            Actor heir = World.world.units.get(data.union_city_state_heir_id);
            data.union_city_state_heir_id = -1L;
            if (heir != null && !heir.isRekt() && heir.isAlive() && heir.isUnitFitToRule())
            {
                if (heir.isKing()) CrownInUnion(kingdom, heir);
                else GraceEdictService.Crown(kingdom, heir);
                if (kingdom.king == heir) return true;
            }
            DissolveCityStateUnion(World.world.kingdoms.Where(realm => realm == kingdom ||
                realm != null && !realm.isRekt() && realm.GetOrCreate().union_leader_kingdom_id == kingdom.id));
            if (CityStateService.TryCrownLocalRuler(kingdom))
                data.union_city_state_local_succession = false;
            return true;
        }

        if (data.union_partition_heir_id >= 0)
        {
            Actor heir = World.world.units.get(data.union_partition_heir_id);
            data.union_partition_heir_id = -1L;
            if (heir != null && !heir.isRekt() && heir.isAlive())
            {
                if (IsFeudal(kingdom) && heir.isKing() && IsAtFeudalRealmLimit(heir))
                {
                    if (CrownLocalVassal(kingdom, heir.kingdom)) return true;
                    data.union_partition_heir_id = heir.id;
                    return true;
                }
                if (heir.isKing()) CrownInUnion(kingdom, heir);
                else GraceEdictService.Crown(kingdom, heir);
                return kingdom.king == heir;
            }
        }

        if (data.union_leader_kingdom_id >= 0)
        {
            Kingdom leader = World.world.kingdoms.get(data.union_leader_kingdom_id);
            if (leader == null || leader.isRekt() || !CityStateService.IsCityState(leader) ||
                !CityStateService.IsCityState(kingdom))
            {
                data.union_leader_kingdom_id = -1L;
                return false;
            }
            // 盟主城邦还没选出新君主：先等着，不自行选举
            if (!leader.hasKing() || leader.king == null || !leader.king.isAlive()) return true;
            data.union_leader_kingdom_id = -1L;
            CrownInUnion(kingdom, leader.king);
            kingdom.updateColor(leader.getColor());
            return true;
        }
        return false;
    }

    private static void DissolveCityStateUnion(IEnumerable<Kingdom> realms)
    {
        List<Kingdom> members = realms.Where(CityStateService.IsCityState).Distinct().ToList();
        foreach (Kingdom realm in members)
        {
            KingdomExtension.KingdomExtraData data = realm.GetOrCreate();
            data.union_leader_kingdom_id = -1L;
            data.union_city_state_heir_id = -1L;
            data.union_city_state_local_succession = true;
            data.union_alliance_leader_kingdom_id = -1L;
        }
        foreach (Kingdom realm in members)
        {
            if (realm.hasAlliance()) realm.getAlliance().leave(realm);
            realm.generateColor();
        }
    }

    // 西方封建制"一法理一国"：索取到的法理不并进获胜国，而是在法理首府另立一国(该法理原属战败国的城市
    // 一并划入)，由获胜国君主兼领。首府已在获胜国手里的法理无法另立，返回在 remaining 里交给原逻辑处理。
    public static List<KingdomTitle> InheritTitlesAsUnion(Kingdom successor, Kingdom defeated,
        IEnumerable<long> titleIds, out List<long> remaining)
    {
        var inherited = new List<KingdomTitle>();
        remaining = new List<long>();
        Actor ruler = successor?.king;
        if (!IsFeudal(successor) || ruler == null || ruler.isRekt() || titleIds == null)
        {
            remaining.AddRange(titleIds ?? Enumerable.Empty<long>());
            return inherited;
        }
        foreach (long titleId in titleIds.Distinct().ToList())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(titleId);
            City seat = title?.title_capital;
            if (title == null || title.isRekt() || seat == null || seat.isRekt() || seat.kingdom == null ||
                seat.kingdom == successor)
            {
                remaining.Add(titleId);
                continue;
            }
            Kingdom holder = seat.kingdom;
            Actor founder = (seat.units ?? new List<Actor>()).ToList()
                .Where(actor => actor != null && actor != holder.king && !actor.isRekt() && actor.isAlive() &&
                                actor.isAdult() && !actor.isKing() && actor.isUnitFitToRule())
                .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
            if (founder == null)
            {
                remaining.Add(titleId);
                continue;
            }
            if (holder.king?.city == seat) holder.kingFledCity();
            Kingdom realm = seat.makeOwnKingdom(founder);
            if (realm == null)
            {
                remaining.Add(titleId);
                continue;
            }
            foreach (City city in title.getCities().ToList())
                if (city != null && !city.isRekt() && city != seat && city.kingdom == holder)
                    city.joinAnotherKingdom(realm);
            realm.SetRegimeType(RegimeType.Feudalism);
            realm.LoadRegime();
            realm.InheritRealmTitles(holder, new[] { titleId });
            if (IsAtFeudalRealmLimit(ruler))
            {
                FeudalVassalService.Bind(successor, realm);
                inherited.Add(title);
                continue;
            }
            CrownInUnion(realm, ruler);
            inherited.Add(title);
        }
        return inherited;
    }

    public static void JoinAlliance(Kingdom leader, Kingdom member)
    {
        if (leader == null || member == null || leader == member ||
            leader.IsInEmpire() || member.IsInEmpire() ||
            FeudalVassalService.GetOverlord(member) == leader ||
            !FeudalVassalService.CanJoinAlliance(member, leader.hasAlliance() ? leader.getAlliance() : null)) return;
        Alliance leaderAlliance = leader.hasAlliance() ? leader.getAlliance() : null;
        if (member.hasAlliance())
        {
            if (member.getAlliance() == leaderAlliance)
            {
                if (leader.king != null && leader.king == member.king)
                    member.GetOrCreate().union_alliance_leader_kingdom_id = leader.id;
                return;
            }
            member.getAlliance().leave(member);
        }
        if (leaderAlliance != null) leaderAlliance.join(member, true, true);
        else leaderAlliance = World.world.alliances.newAlliance(leader, member);
        ModAllianceService.Mark(leaderAlliance);
        if (leader.king != null && leader.king == member.king)
            member.GetOrCreate().union_alliance_leader_kingdom_id = leader.id;
    }

    // 同盟直接以盟主国的名字命名(城邦即主城名)，不带"城邦"等后缀
    public static void NameAllianceAfterLeader(Kingdom leader)
    {
        Alliance alliance = leader != null && leader.hasAlliance() ? leader.getAlliance() : null;
        if (alliance == null) return;
        string name = CityStateService.IsCityState(leader) && leader.capital != null && !leader.capital.isRekt()
            ? leader.capital.GetCityName()
            : leader.data?.name;
        if (!string.IsNullOrWhiteSpace(name)) alliance.setName(name);
    }
}
