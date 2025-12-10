using System;
using ai.behaviours;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckCentreArmy:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        
        return BehResult.Continue;
    }
    
}