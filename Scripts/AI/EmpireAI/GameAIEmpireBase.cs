using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public abstract class GameAIEmpireBase : BehaviourActionKingdom
{
    public abstract Type OriginalBeh { get; }

    public override BehResult execute(Kingdom pObject)
    {
        if (!pObject.IsEmpire()) return BehResult.Stop;
        return BehResult.Continue;
    }

    public virtual bool Detect(Kingdom pKingdom)
    {
        return execute(pKingdom) != BehResult.Stop;
    }
}
