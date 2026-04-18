using System;
using ai.behaviours;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckEmpireLaw : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom == null || pKingdom.isRekt()) return BehResult.Continue;
        EmpireLawSystem.CheckMercenaryOvermightyLaw(pKingdom);
        return BehResult.Continue;
    }
}
