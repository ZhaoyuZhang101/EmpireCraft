using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehCheckMandate:GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire?.Emperor != null)
        {
            if (empire.IsNeedToIncreaseMandate())
            {
                empire.AddMandate(2);
                empire.data.last_increase_mandate_timestamp = world.getCurWorldTime();
            }
        }
        return BehResult.Continue;
    }
}