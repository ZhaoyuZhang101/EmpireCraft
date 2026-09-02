using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehCheckCabinet : GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();
    
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null) return BehResult.Continue;
        foreach (var ff in regime.GetPlayerFactions())
        {
            ff.FixMissedTemporaryFactions();
        }
        switch (regime.type)
        {
            case RegimeType.LvLing:
                SetCabinetForLvLing(empire);
                break;
            case RegimeType.Feudalism:
                SetCabinetForFeudalism(empire);
                break;
            case RegimeType.ZhouFeudalism:
                break;
            case RegimeType.Republic:
                break;
            case RegimeType.Arabic:
                break;
            case RegimeType.YouMu:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (regime.has_cabinet)
        {
            empire.Additions.cabinet_acc = IsCabinetControlEmpire(pKingdom) ? 30 : 0;
        }
        return base.execute(pKingdom);
    }

    public static bool IsCabinetControlEmpire(Kingdom pKingdom)
    {
        //todo: 派系完全控制内閣
        var dominate = pKingdom.GetRegime().GetDominateFaction();
        return pKingdom.GetEmpire().GetCabinetMembers().All(m=>m.GetFaction()?.GetID()==dominate?.GetID());
    }

    public void SetCabinetForLvLing(Empire empire)
    {
        Regime regime = empire.CoreKingdom.GetRegime();
        var dominateFaction = regime.GetDominateFaction();
        if (dominateFaction==null) return;
        if (dominateFaction.Members.Count<=0) return;
        // —— 1) 计算内阁规模：0~15 → 1~5 ——
        int S = empire.Emperor?.stewardship??0;        // 组织能力
        if (S < 3) S = 0; if (S > 15) S = 15;      // 手动 clamp
        int cabinetSize = 1 + (S * regime.cabinet_number-1) / 15;        // 线性映射到 1..5，最多 5 个

        long cabinetLeader = dominateFaction.Members[0];
        double bestPerformance = world.units.get(cabinetLeader)?.GetIdentity()?.TotalPerformance ?? double.MinValue;
        for (int i = 1; i < dominateFaction.Members.Count; i++)
        {
            long memberId = dominateFaction.Members[i];
            double performance = world.units.get(memberId)?.GetIdentity()?.TotalPerformance ?? double.MinValue;
            if (performance > bestPerformance)
            {
                bestPerformance = performance;
                cabinetLeader = memberId;
            }
        }
        if (cabinetLeader != empire.GetCabinetLeader()?.id)
        {
            empire.SetCabinetLeader(world.units.get(cabinetLeader));  
        }

        if (empire.data.CabinetMembers.Count > cabinetSize)
        {
            empire.data.CabinetMembers.Remove(empire.data.CabinetMembers.Last());
        }
        else if  (empire.data.CabinetMembers.Count < cabinetSize)
        {
            Actor newFactionMember = null;
            double bestCandidatePerformance = double.MinValue;
            var allFactionMembers = regime.GetAllFactionMembers();
            for (int i = 0; i < allFactionMembers.Count; i++)
            {
                var actor = allFactionMembers[i];
                long actorId = actor?.id ?? -1L;
                if (empire.data.CabinetMembers.Contains(actorId))
                {
                    continue;
                }
                double performance = actor?.GetIdentity()?.TotalPerformance ?? double.MinValue;
                if (performance > bestCandidatePerformance)
                {
                    bestCandidatePerformance = performance;
                    newFactionMember = actor;
                }
            }
            empire.AddCabinetMember(newFactionMember);
        }
        
    }

    public void SetCabinetForFeudalism(Empire empire)
    {
        List<long> previousElectors = empire.data.CabinetMembers?.ToList() ?? new List<long>();
        long previousChiefElector = previousElectors.FirstOrDefault(id => id > 0);
        List<long> religionLeaderList = new();
        List<long> normalKingList = empire.kingdoms_list.FindAll(k => k.hasKing()&&!k.IsEmpire()
                &&k.GetRegime()?.GetReligionLevel() != ReligionLevel.High)
            .OrderByDescending(k => k.countTotalWarriors()).Select(k => k.king.id).Take(4).ToList();
        if (!empire.Religion.isRekt())
        {
            foreach (var kingdom in empire.kingdoms_list)
            {
                if (kingdom.IsEmpire()) continue;
                var regime = kingdom.GetRegime();
                if (regime.GetReligionLevel() != ReligionLevel.High) continue;
                if (religionLeaderList.Count >= 3) continue;
                var religionAreas = kingdom.cities.OrderByDescending(c => c.countWarriors());
                religionLeaderList.AddRange(from area in religionAreas where area.hasLeader() select area.leader.id);
            }
        }
        while (religionLeaderList.Count < 3)
        {
            religionLeaderList.Add(-1L);
        }

        while (normalKingList.Count < 4)
        {
            normalKingList.Add(-1L);
        }
        religionLeaderList.AddRange(normalKingList);
        empire.data.CabinetMembers = religionLeaderList;

        foreach (long electorId in religionLeaderList.Where(id => id > 0 && !previousElectors.Contains(id)))
        {
            world.units.get(electorId)?.RecordPersonalHistory(LM.Get("personal_history_became_elector"));
        }

        long chiefElector = religionLeaderList.FirstOrDefault(id => id > 0);
        if (chiefElector > 0 && chiefElector != previousChiefElector)
        {
            world.units.get(chiefElector)?.RecordPersonalHistory(LM.Get("personal_history_became_chief_elector"));
        }
    }
}
