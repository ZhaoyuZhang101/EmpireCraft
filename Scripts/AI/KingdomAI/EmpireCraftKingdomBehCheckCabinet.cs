using System;
using System.Collections.Generic;
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
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        Regime regime = empire.CoreKingdom.GetRegime();
        foreach (var ff in regime.PlayerFactions)
        {
            ff.FixMissedTemporaryFactions();
        }
        switch (pKingdom.GetRegime().type)
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

        if (pKingdom.GetRegime().has_cabinet)
        {
            empire.Additions.cabinet_acc = IsCabinetControlEmpire(pKingdom) ? 30 : 0;
        }
        return BehResult.Continue;
    }

    public static bool IsCabinetControlEmpire(Kingdom pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        Regime regime = empire.CoreKingdom.GetRegime();
        FixedFaction firstFaction = null;
        var flag = true;
        foreach (var member in empire.GetCabinetMembers())
        {
            var memberFac = member?.GetFaction();
            if  (memberFac == null) continue;
            firstFaction ??= memberFac;
            if (memberFac == firstFaction) continue;
            if (firstFaction != memberFac)
            {
                flag = false;
                break;
            }
        }

        var dominate = regime.GetDominateFaction();
        if (flag&&dominate==firstFaction)
        {
            return true;
        }
        return false;
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
                ToList().Find(a=>!empire.data.CabinetMembers.Contains(a?.id??-1L));
            empire.AddCabinetMember(newFactionMember);
        }
        
    }

    public void SetCabinetForFeudalism(Empire empire)
    {
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
    }
}