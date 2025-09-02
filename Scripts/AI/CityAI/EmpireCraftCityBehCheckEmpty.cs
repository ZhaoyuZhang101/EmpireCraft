using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftCityBehCheckEmpty:GameAICityBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(City pCity)
    {
        return BehResult.Continue;
    }
}