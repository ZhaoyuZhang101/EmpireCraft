using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckCabinet : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
    
    public override BehResult execute(Kingdom pKingdom)
    {
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        Regime regime = pKingdom.GetRegime();
        foreach (var ff in regime.Factions)
        {
            ff.FixMissedTemporaryFactions();
        }
        var dominateFaction = regime.GetDominateFaction();
        if (dominateFaction.Members.Count<=0) return BehResult.Continue;
        // —— 1) 计算内阁规模：0~15 → 1~5 ——
        int S = empire.Emperor?.stewardship??0;        // 组织能力
        if (S < 3) S = 0; if (S > 15) S = 15;      // 手动 clamp
        int cabinetSize = 1 + (S * 4) / 15;        // 线性映射到 1..5，最多 5 个

        var cabinetLeader =
            dominateFaction.Members.OrderByDescending(a => world.units.get(a)?.GetIdentity()?.TotalPerformance ?? 0).First();
        if (cabinetLeader != empire.GetCabinetLeader()?.id)
        {
            empire.SetCabinetLeader(world.units.get(cabinetLeader));  
        }

        if (empire.data.CabinetMembers.ToList().Count > cabinetSize)
        {
            empire.data.CabinetMembers.Remove(empire.data.CabinetMembers.Last());
        }
        else if  (empire.data.CabinetMembers.Count < cabinetSize)
        {
            var newFactionMember = regime.GetAllFactionMembers().
                OrderByDescending(a=>a?.GetIdentity()?.TotalPerformance ?? 0).
                ToList().Find(a=>!empire.data.CabinetMembers.Contains(a.id));
            empire.AddCabinetMember(newFactionMember);
        }
        return BehResult.Continue;
    }
}