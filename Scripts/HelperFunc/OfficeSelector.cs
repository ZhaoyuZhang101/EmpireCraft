using System.Collections.Generic;
using System.Linq;
using ai;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.HelperFunc;

public static class OfficeSelector
{
    public static bool IsPreferredOfficeCandidate(Actor actor, Kingdom kingdom)
    {
        if (actor == null) return false;
        if (kingdom == null) return true;
        // 现代、共和政体任官不分性别
        if (kingdom.GetRegime()?.type is RegimeType.Modern or RegimeType.Republic) return true;
        string culture = CultureService.GetRealmCulture(kingdom);
        if (string.IsNullOrEmpty(culture)) return true;
        if (!OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) || setting?.traits == null)
            return true;
        if (setting.traits.Contains("patriarchy")) return actor.isSexMale();
        if (setting.traits.Contains("matriarchy")) return actor.isSexFemale();
        return true;
    }

    public static void Select(this OfficeObject office, Kingdom pKingdom, string debugType = "中央")
    {
        Actor actor = null;
        Regime regime = pKingdom.GetRegime();
        LeaderSelectMethod method = office.leader_select_method;
        if (office.leader_select_method == LeaderSelectMethod.Default)
        {
            method =  regime.GetLeaderSelectMethod();
        }
        // 古典共和帝国首领：施行「元老院」之前谁强谁上，之后由元老院推举；施行「元首制」后元首在家族里相传
        if (regime?.type == RegimeType.ClassicalRepublic && pKingdom.IsEmpire() && office == pKingdom.GetOffice())
            method = pKingdom.GetEmpire()?.IsPrincipate() == true ? LeaderSelectMethod.Succession
                : CityStateService.HasSenate(pKingdom.GetEmpire()) ? LeaderSelectMethod.Vote : LeaderSelectMethod.Army;
        // 殖民地总督府：总督由宗主国派(见 ColonySystem.PickGovernor)；特许公司照投票由董事会选
        if (office == pKingdom.GetOffice() && ColonySystem.PickGovernor(pKingdom) is Actor governor)
        {
            office.SetActor(governor);
            return;
        }
        switch (method)
        {
            case LeaderSelectMethod.Exam:
                actor = TryGetExamOfficer(office, pKingdom)??TryGetClanOfficer(pKingdom)??TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Succession:
                // 世家子弟入仕(九品中正、察举、世卿世禄，见 GentrySystem)
                actor = GentrySystem.Candidate(pKingdom, unit => unit.isUnitFitToRule() && unit.CanServeOffice(pKingdom) &&
                                                                 IsPreferredOfficeCandidate(unit, pKingdom))
                        ?? TryGetClanOfficer(pKingdom)??TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Vote:
                // 穿越政体：执委会主席由元老院全体大会从元老里选(见 TransmigrantSystem.ElectChairman)，选不出也不找外人
                if (office == pKingdom.GetOffice() && TransmigrantSystem.IsTransmigrant(pKingdom))
                {
                    actor = TransmigrantSystem.ElectChairman(pKingdom);
                    if (actor == null) return;
                    break;
                }
                actor = TryGetFeudalElectiveRuler(office, pKingdom) ?? TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Army:
                actor = TryGetStrongerLeader(office, pKingdom)??TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Harem:
                if (!EmpireCraftWorldLawLibrary.empirecraft_law_allow_harem.isEnabled())
                {
                    break;
                }
                actor = TryGetHarem(office, pKingdom);
                break;
            default:
                actor = TryGetProfessionOfficer(pKingdom);
                break;
        }

        // 前现代父系(母系)文化：选出的人不合择官偏好时，无小人模式先从人口里补一名合偏好的人，补不出才用原人选
        // (选帝侯预先选立的储君不换人)
        if (actor != null && method != LeaderSelectMethod.Harem && regime?.type != RegimeType.Transmigrant &&
            !IsPreferredOfficeCandidate(actor, pKingdom) &&
            actor != HolyRomanSystem.KingOfRomans(pKingdom.GetEmpire()))
            actor = CityPopulationSystem.SpawnForOffice(office, pKingdom, preferredOnly: true) ?? actor;

        // 无小人模式：没有合适人选就从人口里生成一人补上(后宫除外)
        if (actor == null && method != LeaderSelectMethod.Harem)
            actor = CityPopulationSystem.SpawnForOffice(office, pKingdom);

        if (actor != null)
        {
            // 选帝制：选帝侯推举的皇帝即位(含带着家族领国入主中央)不按普通即位扣正统，见 Empire.NewEmperor
            bool election = method == LeaderSelectMethod.Vote && office == pKingdom.GetOffice() && pKingdom.IsEmpire() &&
                            HolyRomanSystem.IsElective(pKingdom.GetEmpire());
            if (election) HolyRomanSystem.Electing = true;
            try
            {
                office.SetActor(actor);
            }
            finally
            {
                if (election) HolyRomanSystem.Electing = false;
            }
        }
    }

    public static Actor TryGetHarem(OfficeObject pOffice, Kingdom pKingdom)
    {
        if (pOffice.meta_object.isRekt()) return null;
        if (pKingdom.king.isRekt()) return null;
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire != null)
            {
                var emperor = empire.Emperor;
                var concubines = emperor.GetPersonalIdentity().concubines;
                var lives = concubines.Select(pValueTuple => SpecificClanManager.getPerson(pValueTuple.identity)).ToList()
                    .FindAll(a => a.is_alive && a._actor.CanServeOffice(pKingdom) && !a._actor.IsSkeleton())
                    .Select(a=>a._actor).ToList();
                if (pOffice.officeType != 13)
                {
                    var lover = empire.getUnits().ToList().Find(a =>
                        a.CanServeOffice(pKingdom) && a.isSexFemale() && a.isAdult() && a.age <= 25 &&
                        (a.clan == null || a.clan != emperor.clan) &&
                        !a.hasLover() && !lives.Contains(a) && !KinshipRules.AreCloseKin(emperor, a));
                    if (lover != null)
                    {
                        lover.lover = emperor;
                        emperor.GetPersonalIdentity().setLover(lover, isCus:true);
                        lover.joinCity(emperor.city);
                        return lover;
                    }
                }
                else
                {
                    if (emperor.hasLover() && emperor.lover.CanServeOffice(pKingdom))
                    {
                        return emperor.lover;
                    }

                    var lover = empire.getUnits().ToList().Find(a =>
                        a.CanServeOffice(pKingdom) && a.isSexFemale() && a.isAdult() && a.age <= 25 &&
                        (a.clan == null || a.clan != emperor.clan) &&
                        !a.hasLover() && !a.IsSkeleton() && !KinshipRules.AreCloseKin(emperor, a));
                    if (lover != null)
                    {
                        lover.lover = emperor;
                        emperor.setLover(lover);
                        emperor.GetPersonalIdentity().setLover(lover, isCus:false);
                        lover.joinCity(emperor.city);
                        return lover;
                    }
                }
            }
        }

        return null;
    }
    private static Actor TryGetFeudalElectiveRuler(OfficeObject office, Kingdom pKingdom)
    {
        if (office == null || pKingdom == null || !pKingdom.IsEmpire() ||
            pKingdom.GetRegime()?.type is not (RegimeType.Feudalism or RegimeType.ClassicalRepublic) ||
            office != pKingdom.GetOffice())
        {
            return null;
        }
        // 选立的储君直接即位，不再另选
        Actor designated = HolyRomanSystem.KingOfRomans(pKingdom.GetEmpire());
        if (designated != null) return designated;
        return RankElectiveCandidates(pKingdom).Select(item => item.candidate).FirstOrDefault();
    }

    // 选举制首领(封建选帝、古典共和元老院)的推举次序：同派系的选帝侯/元老票数 → 偏好人选 → 威望 → 兵力。
    // 候选人是帝国各成员国(不含中央)的君主。帝国窗口的元老院示意图也按这个次序画
    public static List<(Actor candidate, int votes)> RankElectiveCandidates(Kingdom pKingdom)
    {
        Empire empire = pKingdom?.GetEmpire();
        if (empire == null) return new List<(Actor, int)>();
        List<Actor> electors = empire.GetCabinetMembers()
            .Where(elector => elector != null && !elector.isRekt())
            .ToList();

        // 帝国自由城市的市长、主教领的主教不是诸侯，不当选皇帝(以前自由城市的市长当选，整座城成了帝国中央，见 HolyRomanSystem.Members)
        return empire.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != pKingdom && kingdom.hasKing() &&
                              HolyRomanSystem.Estate(kingdom) == HolyRomanSystem.EstateNone)
            .Select(kingdom => kingdom.king)
            .Where(candidate => candidate != null && !candidate.isRekt() && candidate.isAdult() &&
                                candidate.isUnitFitToRule() && candidate.CanServeOffice(pKingdom) &&
                                !candidate.IsSkeleton())
            .Select(candidate => (candidate, votes: electors.Count(elector => elector.GetFaction() == candidate.GetFaction())))
            .OrderByDescending(item => item.votes)
            .ThenByDescending(item => IsPreferredOfficeCandidate(item.candidate, pKingdom))
            .ThenByDescending(item => item.candidate.renown)
            .ThenByDescending(item => item.candidate.kingdom?.countTotalWarriors() ?? 0)
            .ToList();
    }

    // 没有元老院的古典共和：偏好人选优先，其次兵力最强的成员国君主(同 TryGetStrongerLeader)
    public static List<Actor> RankStrongestCandidates(Kingdom pKingdom)
    {
        Empire empire = pKingdom?.GetEmpire();
        if (empire == null) return new List<Actor>();
        return empire.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom.hasKing() &&
                              kingdom.king.CanServeOffice(pKingdom))
            .OrderByDescending(kingdom => IsPreferredOfficeCandidate(kingdom.king, pKingdom))
            .ThenByDescending(kingdom => kingdom.countTotalWarriors())
            .Select(kingdom => kingdom.king).ToList();
    }

    private static Actor TryGetStrongerLeader(OfficeObject pOffice, Kingdom pKingdom)
    {
        if (pOffice.meta_object.isRekt()) return null;
        if (pKingdom.IsEmpire()&&pOffice.meta_object.meta_type == MetaType.Kingdom)
        {
            Kingdom kingdom =  null;
            int currentWarriors = 0;
            Empire empire = pKingdom.GetEmpire();
            foreach (var k in empire.kingdoms_list)
            {
                if (!k.hasKing() || !k.king.CanServeOffice(pKingdom)) continue;
                if (k.countTotalWarriors() >= currentWarriors)
                {
                    kingdom = k;
                    currentWarriors = k.countTotalWarriors();
                }
            }
            Actor preferred = empire.kingdoms_list
                .Where(k => k != null && !k.isRekt() && k.hasKing() && k.king.CanServeOffice(pKingdom) &&
                            IsPreferredOfficeCandidate(k.king, pKingdom))
                .OrderByDescending(k => k.countTotalWarriors())
                .Select(k => k.king).FirstOrDefault();
            return preferred ?? kingdom?.king;
        }
        return null;
    }

    // 已经在别处主政的人(别城城主、别国君主)不调来补地方主官：调走了原处又空缺，下一轮又按同样的分数被调回，
    // 城主在两座城之间来回跳(无小人模式补位出来的城主不算在任官员，以前挡不住)。中央官职不挪人，不受此限
    private static bool LeadsElsewhere(Actor unit, OfficeObject office)
    {
        if (unit == null || office == null || !office.is_local) return false;
        object target = office.meta_object;
        if (unit.isKing()) return !ReferenceEquals(target, unit.kingdom);
        return unit.isCityLeader() && unit.city != null && unit.city.leader == unit && !ReferenceEquals(target, unit.city);
    }

    private static Actor TryGetExamOfficer(OfficeObject pOffice, Kingdom  pKingdom)
    {
        IEnumerable<Actor> targetPool;
        if (pOffice.meta_object.isRekt()) return null;
        using ListPool<Actor> pool = new ListPool<Actor>();
        if (pOffice.select_from_local)
        {
            targetPool = pOffice.meta_object.meta_type == MetaType.City
                ? ((City)pOffice.meta_object).units
                : ((Kingdom)pOffice.meta_object).units;
        }
        else
        {
            Kingdom kingdom = pOffice.meta_object.meta_type == MetaType.City
                ? ((City)pOffice.meta_object).kingdom
                : (Kingdom)pOffice.meta_object;
            if (kingdom.IsInEmpire())
            {
                Empire empire = kingdom.GetEmpire();
                targetPool = CityPopulationSystem.AbstractPopulationEnabled ? OfficeCandidateRoster.Empire(empire.kingdoms_list) : empire.getUnits().ToList();
            }
            else
            {
                targetPool = pOffice.meta_object.meta_type == MetaType.City
                    ? ((City)pOffice.meta_object).units
                    : ((Kingdom)pOffice.meta_object).units;
            }
        }

        foreach (Actor unit in targetPool)
        {
            if (LeadsElsewhere(unit, pOffice)) continue;
            if (unit.isUnitFitToRule() && unit.isAdult() && unit.CanServeOffice(pKingdom) && !unit.IsEmperor() && !unit.IsOnOffice() && unit.hasClan()&& (!unit.isKing()||(unit.isKing()&&unit.kingdom.GetRegime().GetLeaderSelectMethod()!=LeaderSelectMethod.Succession)))
            {
                if (unit.IsSkeleton()) continue;
                if (unit.HasOfficeIdentity()&&unit.isActor()&&unit.hasCulture())
                {
                    var flag1 = false;
                    var flag2 = false;
                    OfficeIdentity identity = unit.GetIdentity();
                    if (identity.honoraryOfficial <= pOffice.honorary)
                    {
                        flag1 = true;
                    }
                    if (CityPopulationSystem.AbstractPopulationEnabled ? pOffice.require_traits.Any(t => unit.hasTrait(t)) :
                        pOffice.require_traits.FindAll(t => unit.hasTrait(t)).Any())
                    {
                        flag2 = true;
                    }

                    if (flag1 || flag2)
                    {
                        pool.Add(unit);
                    }
                }
            }
        }
        return SelectBestCandidate(pool, pKingdom);
    }
    private static Actor TryGetProfessionOfficer(Kingdom pKingdom)
    {
        if (pKingdom == null || pKingdom.isRekt() || pKingdom.units == null)
        {
            return null;
        }

        Actor actor = null;
        int num = 0;
        Actor fallback = null;
        int fallbackScore = 0;
        foreach (Actor unit in pKingdom.units)
        {
            if (unit == null || unit.isRekt())
            {
                continue;
            }

            if (unit.isKing() || unit.isCityLeader() || unit.IsOnOffice() || !unit.isAdult() || !unit.CanServeOffice(pKingdom) || unit.IsSkeleton())
            {
                continue;
            }
            int num2 = 1;
            if (unit.is_profession_citizen)
            {
                if (unit.isFavorite())
                {
                    num2 += 2;
                }
                int num3 = ActorTool.attributeDice(unit, num2);
                if (IsPreferredOfficeCandidate(unit, pKingdom))
                {
                    if (actor == null || num3 > num)
                    {
                        actor = unit;
                        num = num3;
                    }
                }
                else if (fallback == null || num3 > fallbackScore)
                {
                    fallback = unit;
                    fallbackScore = num3;
                }
            }
        }
        return actor ?? fallback;
    }
    private static Actor TryGetClanOfficer(Kingdom pKingdom)
    {
        Clan clan = null;
        if (pKingdom.data.royal_clan_id.hasValue())
        {
            clan = World.world.clans.get(pKingdom.data.royal_clan_id);
        }
        using ListPool<Actor> listPool = new ListPool<Actor>();
        using ListPool<Actor> listPool2 = new ListPool<Actor>();
        foreach (City city in pKingdom.cities)
        {
            foreach (Actor unit in city.units)
            {
                if (unit.isUnitFitToRule() && unit.isAdult() && unit.CanServeOffice(pKingdom) && !unit.isKing() && !unit.isCityLeader() && unit.hasClan()&&!unit.IsOnOffice() && !unit.IsSkeleton())
                {
                    if (clan != null && unit.clan == clan)
                    {
                        listPool.Add(unit);
                    }
                    else
                    {
                        listPool2.Add(unit);
                    }
                }
            }
        }
        using ListPool<Actor> preferredRoyal = new ListPool<Actor>();
        using ListPool<Actor> preferredOther = new ListPool<Actor>();
        foreach (Actor unit in listPool)
            if (IsPreferredOfficeCandidate(unit, pKingdom)) preferredRoyal.Add(unit);
        foreach (Actor unit in listPool2)
            if (IsPreferredOfficeCandidate(unit, pKingdom)) preferredOther.Add(unit);

        return SelectBestCandidate(preferredRoyal, pKingdom, alreadyPreferred: true) ??
               SelectBestCandidate(preferredOther, pKingdom, alreadyPreferred: true) ??
               SelectBestCandidate(listPool, pKingdom) ??
               SelectBestCandidate(listPool2, pKingdom);
    }

    private static Actor SelectBestCandidate(ListPool<Actor> pool, Kingdom kingdom, bool alreadyPreferred = false)
    {
        if (!pool.Any()) return null;
        using ListPool<Actor> preferred = new ListPool<Actor>();
        bool ready = alreadyPreferred && CityPopulationSystem.AbstractPopulationEnabled;
        if (!ready)
            foreach (Actor unit in pool)
                if (IsPreferredOfficeCandidate(unit, kingdom)) preferred.Add(unit);
        ListPool<Actor> candidates = ready ? pool : preferred.Any() ? preferred : pool;
        if (kingdom.hasCulture())
            return ListSorters.getUnitSortedByAgeAndTraits(candidates, kingdom.culture);
        candidates.Sort(ListSorters.sortUnitByAgeOldFirst);
        return candidates.ElementAt(0);
    }
}
