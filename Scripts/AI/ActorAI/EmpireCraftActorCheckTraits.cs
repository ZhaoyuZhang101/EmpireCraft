using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckTraits: GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (!pActor.hasKingdom()) return BehResult.Continue;
        if (!pActor.hasArmy()) return BehResult.Continue;
        Kingdom kingdom = pActor.kingdom;
        Regime regime = kingdom.GetRegime();
        if (kingdom.GetLevel()==0)
        {
            // todo: 同步军人特质
        }
        return BehResult.Continue;
    }
}