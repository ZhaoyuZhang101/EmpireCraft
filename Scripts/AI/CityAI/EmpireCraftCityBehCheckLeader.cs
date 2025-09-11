using System;
using System.Collections.Generic;
using System.Linq;
using ai;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;

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
            leader.CheckSpecificClan();
            pCity.SetPersonalIdentity(leader?.GetPersonalIdentity());
        }
    }

    private void checkFindLeader(City pCity)
    {
        if (pCity.units.Count < 3 || pCity.hasLeader() || pCity.isGettingCaptured())
        {
            return;
        }
        Actor actor = null;
        Kingdom  kingdom = pCity.kingdom;
        Regime regime = kingdom.GetRegime();
        switch (regime.GetLeaderSelectMethod())
        {
            case LeaderSelectMethod.Exam:
                // actor = TryGetPotentialOfficer(pCity)??TryGetClanLeader(pCity)??TryGetProfessionCitizen(pCity);;
                break;
            case LeaderSelectMethod.Succession:
                actor = TryGetHeir(pCity)??TryGetClanLeader(pCity)??TryGetProfessionCitizen(pCity);;
                break;
            case LeaderSelectMethod.Vote:
                actor = TryGetClanLeader(pCity)??TryGetProfessionCitizen(pCity);
                break;
        }
        if (actor != null)
        {
            if (actor.city != pCity)
            {
                actor.removeFromArmy();
            }
            actor.joinCity(pCity);
            pCity.setLeader(actor, pNew: true);
        }
    }
    public Actor TryGetProfessionCitizen(City pCity)
    {
        Actor actor = null;
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

        return actor;
    }

    private Actor TryGetHeir(City pCity)
    {
        PersonalClanIdentity personal = pCity.GetPersonalIdentity();
        if (personal == null) return null;
        List<(ClanRelation relation, PersonalClanIdentity identity)> relations = SpecificClanManager.FindAllRelations(personal);
        
        var heirs = relations.FindAll(r => r.relation == ClanRelation.CHILD&&r.identity.CanHeir(personal)).Select(r=>r.identity).ToList();
        if (!heirs.Any())
        {
            heirs = relations.FindAll(r => r.relation is ClanRelation.SSGB or ClanRelation.SSGG&&r.identity.CanHeir(personal)).Select(r=>r.identity).ToList();
        }

        if (!heirs.Any())
        {
            heirs = relations.FindAll(r => r.relation is ClanRelation.SBB or ClanRelation.SBG&&r.identity.CanHeir(personal)).Select(r=>r.identity).ToList();
        }

        if (!heirs.Any())
        {
            heirs = relations.FindAll(r => r.relation is ClanRelation.FUNC or ClanRelation.FANT&&r.identity.CanHeir(personal)).Select(r=>r.identity).ToList();
        }

        if (!heirs.Any())
        {
            heirs = relations.FindAll(r=>r.identity.CanHeir(personal)).Select(r=>r.identity).ToList();
        }
        if (heirs.Any()) return heirs.First()._actor;
        return null;
    }
    private Actor TryGetClanLeader(City pCity)
    {
        Kingdom kingdom = pCity.kingdom;
        Clan clan = null;
        if (kingdom.data.royal_clan_id.hasValue())
        {
            clan = world.clans.get(kingdom.data.royal_clan_id);
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