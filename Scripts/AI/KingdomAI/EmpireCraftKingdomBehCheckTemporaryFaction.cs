using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckTemporaryFaction: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        if (pKingdom.GetEmpire()==null) return BehResult.Continue;
        Regime regime = pKingdom.GetRegime();
        regime.GetPlayerFactions().ForEach(f=>f.EmpireId = pKingdom.GetEmpire().getID());
        FixedFaction dominateFaction = regime.GetDominateFaction();
        if (dominateFaction == null) return BehResult.Continue;
        foreach (var ff in regime.GetPlayerFactions().Where(ff => ff != dominateFaction))
        {
            ff.TemporaryFactions.ForEach(tf => tf.End());
        } 
        if (dominateFaction.IsAnyTFactionRuns()) return BehResult.Continue;
        if (dominateFaction.GetLeader() == null && regime.type!= RegimeType.Feudalism) return BehResult.Continue;
        if (regime.has_cabinet)
        {
            if (regime.type != RegimeType.Feudalism)
            {
                if (pKingdom.GetEmpire().GetCabinetLeader()?.GetFaction() != dominateFaction) return BehResult.Continue;
            }
        }
        var shuffledTf = dominateFaction.TemporaryFactions.ToList();
        shuffledTf.Shuffle();
        foreach (var tf in shuffledTf)
        {
            if (tf.CheckCondition())
            {
                tf.Start();
                return BehResult.Continue;
            }
        }
        return BehResult.Continue;
    }
}