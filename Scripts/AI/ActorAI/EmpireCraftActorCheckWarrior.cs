using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
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
        if (pActor.isKing())
        {
            pActor.stopBeingWarrior();
        }
        if (EmpireCraftWorldLawLibrary.empirecraft_law_prevent_building_destroy.isEnabled())
        {
            pActor.asset.can_attack_buildings = false;
        }
        if (!pActor.isUnitFitToRule()) return BehResult.Continue;
        if (!pActor.hasKingdom())  return BehResult.Continue;
        if (pActor.isKing())  return BehResult.Continue;
        if (pActor.isCityLeader())  return BehResult.Continue;
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
                try
                {
                    var c = pActor?.city;
                    if (c != null) c.makeWarrior(pActor);
                }
                catch
                {
                    // 跳过异常，避免旧存档或边界态导致崩溃
                }
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
                        try
                        {
                            city.makeWarrior(pActor);
                        }
                        catch
                        {
                            // 跳过异常，避免旧存档或边界态导致崩溃
                        }
                    }
                } 
            }
        } 
        return BehResult.Continue;
    }

    public static List<Army> GetAllCenterArmy(Empire empire)
    {
        List<Army> res = new List<Army>();
        var ks = empire.kingdoms_list;
        for (int i = 0; i < ks.Count; i++)
        {
            var a = ks[i].GetCenterArmy();
            if (a != null && !a.isRekt())
            {
                res.Add(a);
            }
        }
        return res;
    }

    public static int CountAllCenterArmy(Empire empire)
    {
        var armies = GetAllCenterArmy(empire);
        int total = 0;
        for (int i = 0; i < armies.Count; i++)
        {
            total += armies[i].units.Count;
        }
        return total;
    }
}
