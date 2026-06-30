using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.CityAI;

public abstract class GameAICityBase : BehaviourActionCity
{
    public abstract Type OriginalBeh { get; }

    public virtual bool Detect(City pCity)
    {
        return execute(pCity) != BehResult.Stop;
    }
}
