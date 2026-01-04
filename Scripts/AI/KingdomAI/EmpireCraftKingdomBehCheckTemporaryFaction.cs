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
        CheckTf(pKingdom);
        return BehResult.Continue;
    }

    public static void CheckTf(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return;
        if (pKingdom.GetEmpire()==null) return;
        Regime regime = pKingdom.GetRegime();
        regime.GetPlayerFactions().ForEach(f=>f.EmpireId = pKingdom.GetEmpire().getID());
        FixedFaction dominateFaction = regime.GetDominateFaction();
        if (dominateFaction == null) return;
        foreach (var ff in regime.GetPlayerFactions().Where(ff => ff != dominateFaction))
        {
            ff.TemporaryFactions.ForEach(tf => tf.End());
        }

        var run = dominateFaction.GetAnyTFactionRuns();
        if (run?.ShowAsPlot??false)
        {
            if ((pKingdom.king?.plot?.name ?? "") != (run?.type.ToString() ?? "-"))
            {
                run?.End();
                return;
            }
        }
        if (dominateFaction.IsAnyTFactionRuns()) return;
        if (dominateFaction.GetLeader() == null && regime.type!= RegimeType.Feudalism) return;
        if (regime.has_cabinet)
        {
            if (regime.type != RegimeType.Feudalism)
            {
                if (pKingdom.GetEmpire().GetCabinetLeader()?.GetFaction() != dominateFaction) return;
            }
        }
        var shuffledTf = dominateFaction.TemporaryFactions.ToList();
        shuffledTf.Shuffle();
        foreach (var tf in shuffledTf)
        {
            if (tf.CheckCondition())
            {
                tf.Start();
                return;
            }
        }
        return;
    }
}