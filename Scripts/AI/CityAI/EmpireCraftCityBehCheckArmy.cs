using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckArmy:GameAICityBase
{
    public override Type OriginalBeh => typeof(CityBehCheckArmy);
    public override BehResult execute(City pCity)
    {
        if (!pCity.hasKingdom()) return BehResult.Continue;
        if (!WorldLawLibrary.world_law_civ_army.isEnabled()) return BehResult.Continue;
        Regime regime = pCity.kingdom.GetRegime();
        if (regime == null || !regime.IsAllowArmy())
        {
            pCity.disbandArmy();
            LogService.LogInfo("禁用军队");
            return BehResult.Continue;
        };
        pCity.checkArmyExistence();
        if (pCity.hasArmy())
        {
            Army army = pCity.army;
            if (pCity.isCapitalCity())
            {
                Kingdom k = pCity.kingdom;
                if (k.IsInEmpire())
                {
                    Empire empire = k.GetEmpire();
                    if (army == k.GetCenterArmy())
                    {
                        army._captain.setKingdom(empire.CoreKingdom);
                        army.units.ForEach(a => a.setKingdom(empire.CoreKingdom));
                        army.name = $"{k.GetEmpire().GetEmpireName()}-{k.GetKingdomName()}驻军";
                        CreateNewArmy(pCity);
                        return BehResult.Continue;
                    }
                }
            }
            return BehResult.Continue;
        }
        CreateNewArmy(pCity);

        return BehResult.Continue;
    }

    public static void CreateNewArmy(City pCity)
    {
        Actor randomWarrior = pCity.hasAnyWarriors()?pCity.getRandomWarrior():null;
        if (pCity.kingdom.GetRegime().GetLeaderSelectMethod() == LeaderSelectMethod.Exam)
        {
            randomWarrior = pCity.kingdom.units.Find(a => a.hasTrait("juren") || a.hasTrait("gongshi"));
        } 
        if (randomWarrior == null)
        {
            return;
        }
        randomWarrior.setProfession(UnitProfession.Warrior);
        world.armies.newArmy(randomWarrior, pCity);
    }
}