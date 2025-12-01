using System;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckTemporaryFaction: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Regime regime = pKingdom.GetRegime();
        FixedFaction dominateFaction = regime.GetDominateFaction();
        if (dominateFaction == null) return BehResult.Continue;
        foreach (var tf in dominateFaction.TemporaryFactions)
        {
            tf.CheckNeedToUpdate();
        }

        foreach (var ff in regime.Factions)
        {
            if (ff==dominateFaction) continue;
            ff.TemporaryFactions.ForEach(tf => tf.End());
        } 
        if (dominateFaction.IsAnyTFactionRuns()) return BehResult.Continue;
        if (dominateFaction.GetLeader() == null) return BehResult.Continue;
        if (pKingdom.GetEmpire().GetCabinetLeader()?.GetFaction() != dominateFaction) return BehResult.Continue;
        foreach (var tf in dominateFaction.TemporaryFactions)
        {
            if (tf.CheckCondition())
            {
                if (!tf.IsStarted())
                {
                    tf.Start(pKingdom);
                    break;
                }
            }
        }
        return BehResult.Continue;
    }
}