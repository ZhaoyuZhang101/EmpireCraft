using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftCheckWarrior:GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (!pActor.hasKingdom())  return BehResult.Continue;
        if (pActor.age < 18) return BehResult.Continue;
        Kingdom pKingdom = pActor.kingdom;
        if (pKingdom.GetRegime() == null) return BehResult.Continue;
        if (!pKingdom.GetRegime().IsAllowSupportCenterArmy()) return BehResult.Continue;
        if (!WorldLawLibrary.world_law_civ_army.isEnabled()) return BehResult.Continue;
        if (!pKingdom.IsInEmpire())
        {
            pActor.city.makeWarrior(pActor);
        }
        else
        {
            Empire empire = pKingdom.GetEmpire();
            if (CountAllCenterArmy(empire) < empire.data.MilitaryExpenditure * 25)
            {
                pActor.city.makeWarrior(pActor);
                if (pKingdom.GetRegime().IsAllowSupportCenterArmy())
                {
                    if (empire.CoreKingdom.capital.hasArmy())
                    {
                        var armies =  GetAllCenterArmy(empire);
                        if (armies.Count < empire.kingdoms_list.Count)
                        {
                            foreach (var ek in empire.kingdoms_list)
                            {
                                if (ek.GetCenterArmy().isRekt())
                                {
                                    var newArmy = world.armies.newArmy(pActor, ek.capital);
                                    newArmy._kingdom = empire.CoreKingdom;
                                    ek.SetCenterArmy(newArmy);
                                    pActor.setCity(ek.capital);
                                    pActor.setKingdom(empire.CoreKingdom);
                                    pActor.goTo(ek.capital._city_tile);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            foreach (var a in armies)
                            {
                                if (!a.hasCaptain()) continue;
                                if (a.units.Count<a._captain.warfare)
                                {
                                    var city = a._captain.city;
                                    pActor.setArmy(a);
                                    pActor.setCity(city);
                                    pActor.setKingdom(empire.CoreKingdom);
                                    pActor.goTo(city._city_tile);
                                    break;
                                }
                            }
                        }
                    }
                }
                
            }
            else
            {
                City city = pActor.city;
                if (city.countWarriors() < city.getMaxWarriors())
                {
                   city.makeWarrior(pActor);
                }
            }
        } 
        return BehResult.Continue;
    }

    public static List<Army> GetAllCenterArmy(Empire empire)
    {
        return empire.kingdoms_list.FindAll(k => !k.GetCenterArmy().isRekt()).Select(k => k.GetCenterArmy()).ToList();
    }

    public static int CountAllCenterArmy(Empire empire)
    {
        return GetAllCenterArmy(empire).Select(a=>a.units.Count).Sum();
    }
}