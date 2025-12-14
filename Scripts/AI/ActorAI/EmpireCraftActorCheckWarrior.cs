using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckWarrior:GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (pActor.isRekt()) return BehResult.Continue;
        if (!pActor.hasKingdom())  return BehResult.Continue;
        if (!pActor.isAdult()) return BehResult.Continue;
        if (!pActor.hasCity()) return BehResult.Continue;
        if (pActor.isWarrior()) return BehResult.Continue;
        Kingdom pKingdom = pActor.kingdom;
        if (pKingdom.GetRegime() == null) return BehResult.Continue;
        if (!pKingdom.GetRegime().IsAllowArmy()) return BehResult.Continue;
        if (!WorldLawLibrary.world_law_civ_army.isEnabled()) return BehResult.Continue;
        if (!pKingdom.IsInEmpire())
        {
            if (pActor.city.checkCanMakeWarrior(pActor))
            {
                pActor.city?.makeWarrior(pActor);
            }
        }
        else
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire.isRekt()) return BehResult.Continue;
            if (CountAllCenterArmy(empire) < empire.data.MilitaryExpenditure * 25&&pKingdom.GetMoney() > 0)
            {
                if (pActor?.city?.checkCanMakeWarrior(pActor)??false)
                {
                    pActor?.city?.makeWarrior(pActor);
                }
                if (pKingdom.GetRegime().IsAllowSupportCenterArmy())
                {
                    var armies =  GetAllCenterArmy(empire);
                    foreach (var a in armies)
                    {
                        if (!a.hasCaptain()) continue;
                        if (a.units.Count<a._captain.warfare)
                        {
                            var city = a._captain.city;
                            pActor.setArmy(a);
                            pActor.setCity(city);
                            pActor.setKingdom(empire.CoreKingdom);
                            break;
                        }
                    }
                }
                
            }
            else
            {
                City city = pActor.city;
                if (city != null)
                {
                    if (city.checkCanMakeWarrior(pActor))
                    {
                        city.makeWarrior(pActor);
                    }
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