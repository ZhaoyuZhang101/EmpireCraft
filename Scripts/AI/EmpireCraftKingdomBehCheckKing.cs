using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.AI;

public class EmpireCraftKingdomBehCheckKing : BehaviourActionKingdom
{
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.data.timer_new_king > 0f)
        {
            return BehResult.Continue;
        }

        if (pKingdom.hasKing())
        {
            Actor king = pKingdom.king;
            if (king.isAlive())
            {
                tryToGiveGoldenTooth(king);
                checkClanCreation(king);
                return BehResult.Continue;
            }
        }

        pKingdom.clearKingData();
        if (pKingdom.data.royal_clan_id != -1)
        {
            Clan clan = BehaviourActionBase<Kingdom>.world.clans.get(pKingdom.data.royal_clan_id);
            bool flag = !clan.isRekt();
            Actor actor;
            if (pKingdom.isEmpire())
            {
                Empire empire = pKingdom.GetEmpire();
                if (empire.Heir!=null)
                {
                    actor = empire.Heir;
                    if (actor.isCityLeader())
                    {
                        actor.city.removeLeader();
                    }
                    pKingdom.setKing(actor);
                    empire.setEmperor(actor);
                } else
                {
                    actor = findEmprorFromRoyalClan(pKingdom);
                }
            } else
            {
                actor = findKingFromRoyalClan(pKingdom);
            }
            if (actor == null)
            {
                if (!pKingdom.isEmpire())
                {
                    if (pKingdom.countCities() == 1)
                    {
                        if (pKingdom.capital != null && pKingdom.capital.hasLeader())
                        {
                            Actor leader = pKingdom.capital.leader;
                            pKingdom.capital.removeLeader();
                            pKingdom.setKing(leader);
                        }
                    }
                    else
                    {
                        checkKingdomChaos(pKingdom);
                    }
                }
            }
            else if (pKingdom.hasCulture() && pKingdom.culture.hasTrait("shattered_crown") && flag)
            {
                checkShatteredCrownEvent(pKingdom, actor, clan);
            }

            if (!flag)
            {
                pKingdom.data.royal_clan_id = -1L;
            }
        }
        else
        {
            Actor kingFromLeaders = SuccessionTool.getKingFromLeaders(pKingdom);
            if (kingFromLeaders != null)
            {
                makeKingAndMoveToCapital(pKingdom, kingFromLeaders);
            }
            else
            {
                checkKingdomChaos(pKingdom);
            }
        }

        return BehResult.Continue;
    }

    public Actor findEmprorFromRoyalClan(Kingdom pKingdom)
    {
        Actor actor = null;
        Clan clan = BehaviourActionBase<Kingdom>.world.clans.get(pKingdom.data.royal_clan_id);
        if (clan == null) return actor;
        Empire empire = pKingdom.GetEmpire();
        LogService.LogInfo("当前氏族人数：" + clan.units.Count().ToString());
        List<Actor> maleRoyals = clan.units.FindAll(a => a != null && a.isSexMale());
        List<Actor> femaleRoyals = clan.units.FindAll(a => a != null && a.isSexFemale());
        List<Actor> child = pKingdom.units.FindAll(a => a != null && !a.isAdult() && a.getParents().Count() <= 0);
        if (maleRoyals.Count() > 0)
        {
            actor = maleRoyals.FirstOrDefault();
            pKingdom.setKing(actor);
        }
        else if (child.Count() > 0)
        {
            actor = child.FirstOrDefault();
            actor.setClan(clan);
            pKingdom.setKing(actor);
            LogService.LogInfo($"{empire.data.name}皇族没有子嗣，于是大臣们从民间找来一个孤儿，自称是某个皇族的私生子，遂继承皇位");
            if (child.Count() > 0)
            {
                child.FirstOrDefault().setClan(clan);
            }
        }
        else if (femaleRoyals.Count() > 0)
        {
            actor = femaleRoyals.FirstOrDefault();
            pKingdom.setKing(actor);
            if (actor.hasLover())
            {
                actor.lover.setClan(clan);
                LogService.LogInfo($"{actor.data.name}继承皇位，其丈夫入赘 {clan.name}");
            }
        }
        return actor;
    }

    public void checkKingdomChaos(Kingdom pMainKingdom)
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

        if (listPool.Count == 0)
        {
            return;
        }

        foreach (City item in listPool.LoopRandom())
        {
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

    public void checkShatteredCrownEvent(Kingdom pMainKingdom, Actor pMainKing, Clan pRoyalClan)
    {
        if (!isRebellionsEnabled() || pRoyalClan == null)
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

        if (listPool.Count == 0 || listPool2.Count == 0)
        {
            return;
        }

        Dictionary<long, int> dictionary = UnsafeCollectionPool<Dictionary<long, int>, KeyValuePair<long, int>>.Get();
        using ListPool<Kingdom> listPool3 = new ListPool<Kingdom>();
        dictionary[pMainKingdom.id] = pMainKingdom.cities.Count;
        listPool2.Shuffle();
        listPool.Shuffle();
        bool flag = false;
        while (listPool2.Count > 0 && listPool.Count > 0)
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

        while (listPool2.Count > 0)
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

    public void checkClanCreation(Actor pActor)
    {
        if (!pActor.hasClan())
        {
            BehaviourActionBase<Kingdom>.world.clans.newClan(pActor, pAddDefaultTraits: true);
        }
    }

    public void tryToGiveGoldenTooth(Actor pActor)
    {
        if (pActor.getAge() > 45 && Randy.randomChance(0.05f))
        {
            pActor.addTrait("golden_tooth");
        }
    }

    public bool isRebellionsEnabled()
    {
        return WorldLawLibrary.world_law_rebellions.isEnabled();
    }

    public Actor findKingFromRoyalClan(Kingdom pKingdom)
    {
        Actor actor = SuccessionTool.getKingFromRoyalClan(pKingdom);
        if (actor == null && pKingdom.hasCulture() && (pKingdom.culture.hasTrait("unbroken_chain") || !isRebellionsEnabled()))
        {
            actor = SuccessionTool.getKingFromLeaders(pKingdom);
        }

        if (actor == null)
        {
            return null;
        }

        makeKingAndMoveToCapital(pKingdom, actor);
        return actor;
    }

    public void makeKingAndMoveToCapital(Kingdom pKingdom, Actor pNewKing)
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