using System;
using ai.behaviours;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckArmy:GameAICityBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(City pCity)
    {
        if (!pCity.hasKingdom()) return BehResult.Continue;
        Regime regime = pCity.kingdom.GetRegime();
        if (regime == null) return BehResult.Continue;
        if (!regime.IsAllowArmy())
        {
            pCity.disbandArmy();
        }
        return BehResult.Continue;
    }
}