using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckArmy:GameAICityBase
{
    public override Type OriginalBeh => typeof(CityBehCheckArmy);
    public override BehResult execute(City pCity)
    {
        if (!pCity.hasKingdom()) return BehResult.Continue;
        Regime regime = pCity.kingdom.GetRegime();
        if (regime == null || !regime.IsAllowArmy())
        {
            pCity.disbandArmy();
            return BehResult.Continue;
        };
        pCity.checkArmyExistence();
        if (pCity.hasArmy() || !pCity.hasAnyWarriors())
        {
            if (pCity.hasArmy())
            {
                Army army = pCity.getArmy();
                if (regime.IsAllowSupportCenterArmy())
                {
                    if (pCity.kingdom.IsInEmpire() && !pCity.kingdom.IsEmpire())
                    {
                        army._city = pCity.kingdom.GetEmpire().CoreKingdom.capital;
                        army._kingdom = pCity.kingdom.GetEmpire().CoreKingdom;
                        army.units.ForEach(a => a.kingdom = pCity.kingdom.GetEmpire().CoreKingdom);
                    }
                }
                else
                {
                    if (pCity.kingdom.IsInEmpire() && !pCity.kingdom.IsEmpire())
                    {
                        army._city = pCity;
                        army._kingdom = pCity.kingdom;
                        army.units.ForEach(a => a.kingdom = pCity.kingdom);
                    } 
                }
            }
            return BehResult.Continue;
        }
        Actor randomWarrior = pCity.getRandomWarrior();
        if (randomWarrior == null)
        {
            return BehResult.Continue;
        }
        world.armies.newArmy(randomWarrior, pCity);

        return BehResult.Continue;
    }
}