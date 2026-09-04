using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.AI.EmpireAI;

// Uses the same empire AI update path as the office system so vacant peerages
// are filled without a separate global scan.
public class EmpireCraftEmpireBehCheckPeerages : GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom kingdom)
    {
        kingdom.CheckEmpire();
        if (!kingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return BehResult.Continue;
        empire.update();
        return base.execute(kingdom);
    }
}
