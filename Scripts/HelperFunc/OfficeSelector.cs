using System.Collections.Generic;
using System.Linq;
using ai;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.HelperFunc;

public static class OfficeSelector
{
    public static void Select(this OfficeObject office, Kingdom pKingdom, string debugType = "中央")
    {
        Actor actor = null;
        Regime regime = pKingdom.GetRegime();
        LeaderSelectMethod method = office.leader_select_method;
        if (office.leader_select_method == LeaderSelectMethod.Default)
        {
            LogService.LogInfo("触发默认");
            method =  regime.GetLeaderSelectMethod();
        }
        switch (method)
        {
            case LeaderSelectMethod.Exam:
                actor = TryGetExamOfficer(office, pKingdom)??TryGetClanOfficer(pKingdom)??TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Succession:
                actor = TryGetClanOfficer(pKingdom)??TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Vote:
                actor = TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Army:
                actor = TryGetStrongerLeader(office, pKingdom)??TryGetProfessionOfficer(pKingdom);
                break;
            case LeaderSelectMethod.Harem:
                if (!EmpireCraftWorldLawLibrary.empirecraft_law_allow_harem.isEnabled())
                {
                    break;
                }
                LogService.LogInfo($"选择后宫{LM.Get($"LvLing_officiallevel_{office.officeType}")}");
                LogService.LogInfo($"{debugType}: {pKingdom?.name??"无国家"}");
                actor = TryGetHarem(office, pKingdom);
                break;
            default:
                actor = TryGetProfessionOfficer(pKingdom);
                break;
        }

        if (actor != null)
        {
            office.SetActor(actor);
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
                    .FindAll(a => a.is_alive&&!a._actor.IsSkeleton()).Select(a=>a._actor).ToList();
                if (pOffice.officeType != 13)
                {
                    var lover = empire.getUnits().ToList().Find(a =>
                        a.isSexFemale() && a.isAdult() && a.age <= 25&&!a.hasLover()&&!lives.Contains(a));
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
                    if (emperor.hasLover())
                    {
                        return emperor.lover;
                    }

                    var lover = empire.getUnits().ToList().Find(a =>
                        a.isSexFemale() && a.isAdult() && a.age <= 25&&!a.hasLover() && !a.IsSkeleton());
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
                if (!k.hasKing()) continue;
                if (k.countTotalWarriors() >= currentWarriors)
                {
                    kingdom = k;
                    currentWarriors = k.countTotalWarriors();
                }
            }
            return kingdom?.king;
        }
        return null;
    }

    private static Actor TryGetExamOfficer(OfficeObject pOffice, Kingdom  pKingdom)
    {
        List<Actor> targetPool;
        if (pOffice.meta_object.isRekt()) return null;
        ListPool<Actor> pool = new ListPool<Actor>();
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
                targetPool = empire.getUnits().ToList();
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
            if (unit.isUnitFitToRule() && unit.CanServeOffice(pKingdom) && !unit.IsEmperor() && !unit.IsOnOffice() && unit.hasClan()&& (!unit.isKing()||(unit.isKing()&&unit.kingdom.GetRegime().GetLeaderSelectMethod()!=LeaderSelectMethod.Succession)))
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
                    if (pOffice.require_traits.FindAll(t => unit.hasTrait(t)).Any())
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
        if (pool.Any())
        {
            if (pKingdom.hasCulture())
            {
                return ListSorters.getUnitSortedByAgeAndTraits(pool, pKingdom.culture);
            }
            pool.Sort(ListSorters.sortUnitByAgeOldFirst);
            return pool.ElementAt(0);
        }
        return null;
    }
    private static Actor TryGetProfessionOfficer(Kingdom pKingdom)
    {
        Actor actor = null;
        int num = 0;
        foreach (Actor unit in pKingdom.units)
        {
            if (unit.isKing() || unit.isCityLeader() || unit.IsOnOffice() || !unit.CanServeOffice(pKingdom) || unit.IsSkeleton())
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
                if (actor == null || num3 > num)
                {
                    actor = unit;
                    num = num3;
                }
            }
        }
        return actor;
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
                if (unit.isUnitFitToRule() && unit.CanServeOffice(pKingdom) && !unit.isKing() && !unit.isCityLeader() && unit.hasClan()&&!unit.IsOnOffice() && !unit.IsSkeleton())
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
        Actor result = null;
        if (listPool.Any())
        {
            if (pKingdom.hasCulture())
            {
                return ListSorters.getUnitSortedByAgeAndTraits(listPool, pKingdom.culture);
            }
            listPool.Sort(ListSorters.sortUnitByAgeOldFirst);
            return listPool.ElementAt(0);
        }
        if (listPool2.Any())
        {
            if (pKingdom.hasCulture())
            {
                return ListSorters.getUnitSortedByAgeAndTraits(listPool2, pKingdom.culture);
            }
            listPool2.Sort(ListSorters.sortUnitByAgeOldFirst);
            return listPool2.ElementAt(0);
        }
        return result;
    }
}
