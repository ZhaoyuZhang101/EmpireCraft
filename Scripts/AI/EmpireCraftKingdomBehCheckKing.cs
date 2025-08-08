using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.HelperFunc;

namespace EmpireCraft.Scripts.AI;

public class EmpireCraftKingdomBehCheckKing : BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.data.timer_new_king > 0f)
        {
            return BehResult.Continue;
        }

        bool successEmpire = CheckEmpireKing(pKingdom);
        if (successEmpire)
        {
            return BehResult.Continue;
        }
        
        if (pKingdom.hasKing())
        {
            Actor king = pKingdom.king;
            if (king.isAlive())
            {
                TryToGiveGoldenTooth(king);
                king.CheckSpecificClan();
                return BehResult.Continue;
            }
        }
        pKingdom.clearKingData();
        if (pKingdom.HasHeir())
        {
            var heir = pKingdom.GetHeir();
            MakeKingAndMoveToCapital(pKingdom, heir);
            return BehResult.Continue;
        }
        
        // if (pKingdom.data.royal_clan_id != -1)
        // {
        //     Clan clan = world.clans.get(pKingdom.data.royal_clan_id);
        //     bool flag = !clan.isRekt();
        //     Actor actor = null;
        //     actor = FindKingFromRoyalClan(pKingdom);
        //     if (actor == null)
        //     {
        //         if (pKingdom.countCities() == 1)
        //         {
        //             if (pKingdom.capital != null && pKingdom.capital.hasLeader())
        //             {
        //                 Actor leader = pKingdom.capital.leader;
        //                 pKingdom.capital.removeLeader();
        //                 pKingdom.setKing(leader);
        //             }
        //         }
        //         else
        //         {
        //             CheckKingdomChaos(pKingdom);
        //         }
        //     }
        //     else if (pKingdom.hasCulture() && pKingdom.culture.hasTrait("shattered_crown") && flag)
        //     {
        //         CheckShatteredCrownEvent(pKingdom, actor, clan);
        //     }
        //
        //     if (!flag)
        //     {
        //         pKingdom.data.royal_clan_id = -1L;
        //     }
        // }
        // else
        // {
        //     Actor kingFromLeaders = SuccessionTool.getKingFromLeaders(pKingdom);
        //     if (kingFromLeaders != null)
        //     {
        //         MakeKingAndMoveToCapital(pKingdom, kingFromLeaders);
        //     }
        //     else
        //     {
        //         CheckKingdomChaos(pKingdom);
        //     }
        // }

        return BehResult.Continue;
    }

    private bool CheckEmpireKing(Kingdom pKingdom)
    {
        if (pKingdom.hasKing()) return false;
        if (!pKingdom.isEmpire()) return false;
        var empire = pKingdom.GetEmpire();
        if (!empire.HasHeir()) return true;
        var actor = empire.Heir;
        pKingdom.setKing(actor);
        return true;
    }

    public void CheckKingdomChaos(Kingdom pMainKingdom)
    {
        bool flag = false;
        using ListPool<City> listPool = new ListPool<City>(pMainKingdom.cities.Count);
        foreach (City city in pMainKingdom.cities)
        {
            if (city != pMainKingdom.capital && city.hasLeader())
            {
                listPool.Add(city);
            }
        }
        if (!listPool.Any())
        {
            return;
        }

        foreach (City item in listPool.LoopRandom())
        {
            if (!item.isRekt()) continue;
            Actor leader = item.leader;
            if (leader != null && leader.isAlive())
            {
                item.makeOwnKingdom(leader, pRebellion: false, pFellApart: true).setKing(leader);
                flag = true;
            }
        }

        if (flag)
        {
            if (pMainKingdom.hasAlliance())
            {
                pMainKingdom.getAlliance().leave(pMainKingdom);
            }

            WorldLog.logFracturedKingdom(pMainKingdom);
        }
    }

    public void CheckShatteredCrownEvent(Kingdom pMainKingdom, Actor pMainKing, Clan pRoyalClan)
    {
        if (pMainKingdom == null) return;
        if (!pMainKingdom.isAlive()) return;
        if (!IsRebellionsEnabled() || pRoyalClan == null)
        {
            return;
        }

        using ListPool<Actor> listPool = new ListPool<Actor>(pRoyalClan.units.Count);
        using ListPool<City> listPool2 = new ListPool<City>(pMainKingdom.cities.Count);
        foreach (Actor unit in pRoyalClan.units)
        {
            if (!unit.isRekt() && unit != pMainKing && !unit.isKing())
            {
                listPool.Add(unit);
            }
        }

        foreach (City city3 in pMainKingdom.cities)
        {
            if (city3 != pMainKingdom.capital)
            {
                listPool2.Add(city3);
            }
        }

        if (!listPool.Any() || !listPool2.Any())
        {
            return;
        }

        Dictionary<long, int> dictionary = UnsafeCollectionPool<Dictionary<long, int>, KeyValuePair<long, int>>.Get();
        using ListPool<Kingdom> listPool3 = new ListPool<Kingdom>();
        dictionary[pMainKingdom.id] = pMainKingdom.cities.Count;
        listPool2.Shuffle();
        listPool.Shuffle();
        bool flag = false;
        while (listPool2.Any() && listPool.Any())
        {
            Actor actor = listPool.Pop();
            City city = listPool2.Pop();
            if (actor.isCityLeader())
            {
                actor.city.removeLeader();
            }

            Kingdom kingdom = city.makeOwnKingdom(actor, pRebellion: false, pFellApart: true);
            listPool3.Add(kingdom);
            dictionary[kingdom.id] = 1;
            dictionary[pMainKingdom.id]--;
            actor.removeFromArmy();
            actor.joinCity(city);
            flag = true;
        }

        while (listPool2.Any())
        {
            City city2 = listPool2.Pop();
            Kingdom random = listPool3.GetRandom();
            if (dictionary[random.id] < dictionary[pMainKingdom.id])
            {
                dictionary[random.id]++;
                dictionary[pMainKingdom.id]--;
                city2.joinAnotherKingdom(random, pCaptured: false, pRebellion: true);
            }
        }

        UnsafeCollectionPool<Dictionary<long, int>, KeyValuePair<long, int>>.Release(dictionary);
        if (flag)
        {
            if (pMainKingdom.hasAlliance())
            {
                pMainKingdom.getAlliance().leave(pMainKingdom);
            }

            WorldLog.logShatteredCrown(pMainKingdom);
        }
    }

    public void CheckClanCreation(Actor pActor)
    {
        if (!pActor.hasClan())
        {
            BehaviourActionBase<Kingdom>.world.clans.newClan(pActor, pAddDefaultTraits: true);
        }
    }

    public void TryToGiveGoldenTooth(Actor pActor)
    {
        if (pActor.getAge() > 45 && Randy.randomChance(0.05f))
        {
            pActor.addTrait("golden_tooth");
        }
    }

    public bool IsRebellionsEnabled()
    {
        return WorldLawLibrary.world_law_rebellions.isEnabled();
    }

    public Actor FindKingFromRoyalClan(Kingdom pKingdom)
    {
        Actor actor = SuccessionTool.getKingFromRoyalClan(pKingdom);
        if (actor == null && pKingdom.hasCulture() && (pKingdom.culture.hasTrait("unbroken_chain") || !IsRebellionsEnabled()))
        {
            actor = SuccessionTool.getKingFromLeaders(pKingdom);
        }

        if (actor == null)
        {
            return null;
        }

        MakeKingAndMoveToCapital(pKingdom, actor);
        return actor;
    }

    public void MakeKingAndMoveToCapital(Kingdom pKingdom, Actor pNewKing)
    {
        if (pNewKing.hasCity())
        {
            pNewKing.removeFromArmy();
            if (pNewKing.isCityLeader())
            {
                pNewKing.city.removeLeader();
            }
        }

        if (pKingdom.hasCapital() && pNewKing.city != pKingdom.capital)
        {
            pNewKing.joinCity(pKingdom.capital);
        }

        pKingdom.setKing(pNewKing);
        WorldLog.logNewKing(pKingdom);
    }
}