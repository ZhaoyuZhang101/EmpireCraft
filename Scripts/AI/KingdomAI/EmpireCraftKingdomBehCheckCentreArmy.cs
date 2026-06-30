using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckCentreArmy:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.GetRegime() == null) return BehResult.Continue;
        if (pKingdom.GetRegime().IsAllowDiplomacy() && !pKingdom.GetRegime().IsAllowSupportCenterArmy())
        {
            pKingdom?.GetCenterArmy()?.disband();
        }
        return BehResult.Continue;
    }
}
