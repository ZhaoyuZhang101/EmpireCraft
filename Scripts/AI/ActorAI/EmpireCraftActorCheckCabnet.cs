using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckCabinet:GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    //依据绩效值选择内阁官员
    public override BehResult execute(Actor pActor)
    {
        if (pActor.IsEmperor())
        {
            
        }
        return BehResult.Continue;
    }
}