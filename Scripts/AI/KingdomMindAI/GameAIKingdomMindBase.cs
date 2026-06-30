using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.KingdomMindAI;

public abstract class GameAIKingdomMindBase: BehaviourActionKingdom
{
    public abstract Type OriginalBeh { get; }

    public virtual bool Detect(Kingdom pKingdom)
    {
        return execute(pKingdom) != BehResult.Stop;
    }
}
