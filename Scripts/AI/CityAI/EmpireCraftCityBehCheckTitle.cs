using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckTitle: GameAICityBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(City pCity)
    {
        if (pCity.GetTitle() == null)
        {
            return BehResult.Continue;
        }
        KingdomTitle title = pCity.GetTitle();
        if (title.title_capital.isRekt())
        {
            title.title_capital = pCity;
        }
        return BehResult.Continue;
    }
}