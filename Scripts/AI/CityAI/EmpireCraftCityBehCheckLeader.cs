using System;
using System.Collections.Generic;
using System.Linq;
using ai;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckLeader : GameAICityBase
{
    public override Type OriginalBeh => typeof(CityBehCheckLeader);
    public override BehResult execute(City pCity)
    {
        CheckLeaderClan(pCity);
        checkFindLeader(pCity);
        return BehResult.Continue;
    }

    private void CheckLeaderClan(City pCity)
    {
        if (pCity.hasLeader())
        {
            Actor leader = pCity.leader;
            if (!leader.hasClan())
            {
                world.clans.newClan(leader, pAddDefaultTraits: true);
            }
        }
    }

    public Actor TryGetPotentialOfficer(City pCity)
    {
        List<Actor> officersPool = new List<Actor>();
        if (!pCity.kingdom.isEmpire()) return null;
        Empire empire = pCity.kingdom.GetEmpire();
        foreach (City city in empire.CoreKingdom.cities)
        {
            foreach (Actor unit in city.units)
            {
                if (unit.isUnitFitToRule() && !unit.isKing() && !unit.isCityLeader() && !unit.isOfficer())
                {
                    if (unit.hasTrait("gongshi")||unit.hasTrait("jingshi"))
                    {
                        officersPool.Add(unit);
                    }
                }
            }
        }
        if (officersPool.Count > 0)
        {
            return officersPool.First();
        } else
        {
            return null;
        }
    }

    private void checkFindLeader(City pCity)
    {
        if (pCity.units.Count < 3 || pCity.hasLeader() || pCity.isGettingCaptured())
        {
            return;
        }
        Actor actor = null;
        actor = TryGetPotentialOfficer(pCity);
        if (actor==null)
        {
            actor = TryGetClanLeader(pCity);
        }
        if (actor != null)
        {
            if (actor.city != pCity)
            {
                actor.removeFromArmy();
            }
            actor.joinCity(pCity);
            pCity.setLeader(actor, pNew: true);
            return;
        }
        int num = 0;
        foreach (Actor unit in pCity.units)
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
        if (actor != null)
        {
            pCity.setLeader(actor, pNew: true);
        }
    }

    private Actor TryGetClanLeader(City pCity)
    {
        Kingdom kingdom = pCity.kingdom;
        Clan clan = null;
        if (kingdom.data.royal_clan_id.hasValue())
        {
            clan = BehaviourActionBase<City>.world.clans.get(kingdom.data.royal_clan_id);
        }
        using ListPool<Actor> listPool = new ListPool<Actor>();
        using ListPool<Actor> listPool2 = new ListPool<Actor>();
        foreach (City city in kingdom.cities)
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
            if (pCity.hasCulture())
            {
                return ListSorters.getUnitSortedByAgeAndTraits(listPool, pCity.culture);
            }
            listPool.Sort(ListSorters.sortUnitByAgeOldFirst);
            return listPool.ElementAt(0);
        }
        if (listPool2.Any())
        {
            if (pCity.hasCulture())
            {
                return ListSorters.getUnitSortedByAgeAndTraits(listPool2, pCity.culture);
            }
            listPool2.Sort(ListSorters.sortUnitByAgeOldFirst);
            return listPool2.ElementAt(0);
        }
        return result;
    }
}