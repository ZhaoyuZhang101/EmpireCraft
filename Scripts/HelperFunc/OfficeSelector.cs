using System.Collections.Generic;
using System.Linq;
using ai;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.HelperFunc;

public static class OfficeSelector
{
    public static void Select(this OfficeObject office, Kingdom pKingdom)
    {
        Actor actor = null;
        Regime regime = pKingdom.GetRegime();
        LeaderSelectMethod method = regime.GetLeaderSelectMethod();
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
        }

        if (actor != null)
        {
            office.SetActor(actor);
        }
    }

    private static Actor TryGetExamOfficer(OfficeObject pOffice, Kingdom  pKingdom)
    {
        LogService.LogInfo("开始选择官员");
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
            if (unit.isUnitFitToRule() && !unit.isKing() && !unit.isCityLeader() && unit.hasClan() && !unit.isOfficer())
            {
                if (unit.HasOfficeIdentity())
                {
                    var flag1 = false;
                    var flag2 = false;
                    OfficeIdentity identity = unit.GetIdentity();
                    if (identity.honoraryOfficial >= pOffice.honorary)
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
            if (unit.isKing() || unit.isCityLeader() || unit.isOfficer())
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
                if (unit.isUnitFitToRule() && !unit.isKing() && !unit.isCityLeader() && unit.hasClan()&&!unit.isOfficer())
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