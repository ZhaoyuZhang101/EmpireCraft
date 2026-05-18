using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.ActorAI;

public class EmpireCraftActorCheckFaction : GameAIActorBase
{
    private static readonly Random _rng = new Random();

    public override Type OriginalBeh => GetType();

    public override BehResult execute(Actor pActor)
    {
        if (pActor == null) return BehResult.Continue;

        if (!pActor.isAdult()) return BehResult.Continue;
        if (pActor.HasFaction()) return BehResult.Continue;
        if (!pActor.IsOnOffice()) return BehResult.Continue;
        if (!pActor.HasOfficeIdentity()) return BehResult.Continue;
        if (pActor.IsEmperor()) return BehResult.Continue;

        Kingdom pKingdom = pActor.kingdom;

        if (pKingdom == null) return BehResult.Continue;
        if (pKingdom.isRekt()) return BehResult.Continue;

        Empire empire = pKingdom.GetEmpire();

        if (empire == null)
        {
            return BehResult.Continue;
        }

        var coreKingdom = empire.CoreKingdom;

        if (coreKingdom == null)
        {
            empire.CheckDissolve(null);
            return BehResult.Continue;
        }

        Regime regime = coreKingdom.GetRegime();

        if (regime == null)
        {
            return BehResult.Continue;
        }

        var factions = regime.GetPlayerFactions();

        if (factions == null || factions.Count == 0)
        {
            return BehResult.Continue;
        }

        int kingdomFactionRatioTotal = pKingdom.GetFactionRatioTotal();

        // 如果当前国家已经存在任何派系影响力，那么官员必须加入一个已有影响力的派系
        bool mustJoinFaction = kingdomFactionRatioTotal > 0;

        var ps = new List<(FixedFaction fac, double p)>(factions.Count);

        foreach (var f in factions)
        {
            if (f == null)
            {
                continue;
            }

            int factionRatio = pKingdom.GetFactionRatioValue(f);

            if (factionRatio < 0)
            {
                factionRatio = 0;
            }

            if (factionRatio > 100)
            {
                factionRatio = 100;
            }

            // 如果国家已经有派系影响力，那么只从该国已有影响力的派系里抽
            if (mustJoinFaction && factionRatio <= 0)
            {
                continue;
            }

            double basePossibility = f.CalcPossibility(pActor);

            if (basePossibility < 0)
            {
                basePossibility = 0;
            }

            if (basePossibility > 1)
            {
                basePossibility = 1;
            }

            double factionInfluencePossibility = factionRatio / 100.0;

            double finalPossibility = basePossibility + factionInfluencePossibility;

            if (finalPossibility < 0)
            {
                finalPossibility = 0;
            }

            if (finalPossibility > 1)
            {
                finalPossibility = 1;
            }

            ps.Add((f, finalPossibility));
        }

        if (ps.Count == 0)
        {
            return BehResult.Continue;
        }

        double sum = ps.Sum(t => t.p);

        if (sum <= 0)
        {
            return BehResult.Continue;
        }

        // 国家已有派系影响力时，不能有“不加入”的概率
        // 国家没有派系影响力时，才允许不加入
        double noneWeight = mustJoinFaction ? 0.0 : Math.Max(0.0, 1.0 - sum);

        double r = _rng.NextDouble() * (sum + noneWeight);
        double acc = 0.0;

        FixedFaction chosen = null;

        foreach (var t in ps)
        {
            acc += t.p;

            if (r <= acc)
            {
                chosen = t.fac;
                break;
            }
        }

        if (chosen != null)
        {
            pActor.SetFaction(chosen);
            // TranslateHelper.LogOfficerJoinFaction(pActor.GetOffice(), pActor, chosen);
        }

        return BehResult.Continue;
    }
}