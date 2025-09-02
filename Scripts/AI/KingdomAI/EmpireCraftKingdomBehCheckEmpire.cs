using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckEmpire:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.canBecomeEmpire())
        {
            
        }
        return BehResult.Continue;
    }
}