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

public class EmpireCraftActorCheckFaction:GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult execute(Actor pActor)
    {
        if (!pActor.isAdult()) return BehResult.Continue;
        if (pActor.HasFaction()) return BehResult.Continue;
        if (!pActor.IsOnOffice()) return BehResult.Continue;
        if (!pActor.HasOfficeIdentity()) return BehResult.Continue;
        if (pActor.IsEmperor()) return BehResult.Continue;
        Kingdom pKingdom = pActor.kingdom;
        Empire empire = pKingdom.GetEmpire();
        if (empire == null) return BehResult.Continue;
        var coreKingdom = empire.CoreKingdom;
        if (coreKingdom == null)
        {
            empire.CheckDissolve(null);
            return BehResult.Continue;
        }
        Regime regime = coreKingdom.GetRegime();
        if (regime == null) return BehResult.Continue;
        var factions = regime.GetPlayerFactions();
        
        Random _rng = new Random();
        // 1) 先算每个派系概率（可加上下限）
        var ps = new List<(FixedFaction fac, double p)>(factions.Count);
        foreach (var f in factions)
        {
            double p = f.CalcPossibility(pActor);
            if (p < 0) p = 0; if (p > 1) p = 1;  // 手动 clamp
            ps.Add((f, p));
        }

        // 2) 可选：保留一个“不加入”的权重（避免总和>1或一定要选一个）
        double sum = ps.Sum(t => t.p);
        double noneWeight = Math.Max(0.0, 1.0 - sum);    // 让“什么都不选”的概率 = 1 - Σp

        // 3) 轮盘赌抽一个
        double r = _rng.NextDouble() * (sum + noneWeight);
        double acc = 0.0;
        FixedFaction chosen = null;
        foreach (var t in ps)
        {
            acc += t.p;
            if (r <= acc) { chosen = t.fac; break; }
        }
        // 如果落在“noneWeight”区间，则不加入
        if (chosen != null)
        {
            pActor.SetFaction(chosen);
            // TranslateHelper.LogOfficerJoinFaction(pActor.GetOffice(), pActor, chosen);
        }
        return BehResult.Continue;
    }
}