using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
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
        if (pKingdom?.data == null || pKingdom.isRekt()) return BehResult.Continue;
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null) return BehResult.Continue;
        List<FixedFaction> factions = regime.GetPlayerFactions();
        foreach (var ff in factions)
        {
            ff.FixMissedTemporaryFactions();
            ff.Update();
        }
        pKingdom.ApplyAnnualFactionLeaderGrowth(factions);
        bool parliament = ParliamentSystem.HasParliament(empire);
        // 议会召开后普通内阁撤销，由议会与总理大臣取代；选帝侯团性质的内阁(如西方封建)照常维护
        if (parliament && !ParliamentSystem.KeepsCabinet(empire))
        {
            ParliamentSystem.RetireCabinet(empire);
        }
        else switch (regime.type)
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
            case RegimeType.ClassicalRepublic:
            case RegimeType.Modern:
            case RegimeType.Origin:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (parliament)
        {
            empire.Additions.cabinet_acc = ParliamentSystem.HasWorkingMajority(empire)
                ? InstitutionDefinitionRegistry.Global.constitution.government_majority_acceleration
                : 0;
        }
        else if (regime.has_cabinet)
        {
            empire.Additions.cabinet_acc = IsCabinetControlEmpire(pKingdom) ? 30 : 0;
        }
        return base.execute(pKingdom);
    }

    public static bool IsCabinetControlEmpire(Kingdom pKingdom)
    {
        //todo: 派系完全控制内閣
        Regime regime = pKingdom?.GetRegime();
        Empire empire = pKingdom?.GetEmpire();
        if (regime == null || empire == null) return false;
        if (ParliamentSystem.HasParliament(empire)) return ParliamentSystem.HasWorkingMajority(empire);
        var dominate = regime.GetDominateFaction();
        List<Actor> members = empire.GetCabinetMembers();
        return members != null && members.All(m=>m?.GetFaction()?.GetID()==dominate?.GetID());
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

        Actor cabinetLeader = dominateFaction.Members
            .Select(id => world.units.get(id))
            .Where(actor => actor != null && !actor.isRekt() && actor.HasOfficeIdentity())
            .OrderByDescending(actor => OfficeSelector.IsPreferredOfficeCandidate(actor, empire.CoreKingdom))
            .ThenByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? double.MinValue)
            .FirstOrDefault();
        if (cabinetLeader != null && cabinetLeader.id != empire.GetCabinetLeader()?.id)
        {
            empire.SetCabinetLeader(cabinetLeader);
        }

        if (empire.data.CabinetMembers.Count > cabinetSize)
        {
            empire.data.CabinetMembers.Remove(empire.data.CabinetMembers.Last());
        }
        else if  (empire.data.CabinetMembers.Count < cabinetSize)
        {
            Actor newFactionMember = regime.GetAllFactionMembers()
                .Where(actor => actor != null && !actor.isRekt() && actor.HasOfficeIdentity() &&
                                !empire.data.CabinetMembers.Contains(actor.id))
                .OrderByDescending(actor => OfficeSelector.IsPreferredOfficeCandidate(actor, empire.CoreKingdom))
                .ThenByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? double.MinValue)
                .FirstOrDefault();
            if (newFactionMember != null) empire.AddCabinetMember(newFactionMember);
        }
        
    }

    public void SetCabinetForFeudalism(Empire empire)
    {
        if (empire?.data == null || empire.kingdoms_list == null) return;
        List<long> previousElectors = empire.data.CabinetMembers?.ToList() ?? new List<long>();
        long previousChiefElector = previousElectors.FirstOrDefault(id => id > 0);
        List<long> religionLeaderList = new();
        List<Kingdom> activeKingdoms = empire.kingdoms_list
            .Where(k => k?.data != null && !k.isRekt())
            .ToList();
        List<long> normalKingList = activeKingdoms.FindAll(k => k.hasKing() && k.king?.data != null && !k.IsEmpire()
                && k.GetRegime()?.GetReligionLevel() != ReligionLevel.High)
            .OrderByDescending(k => k.countTotalWarriors()).Select(k => k.king.id).Take(4).ToList();
        if (empire.Religion != null && !empire.Religion.isRekt())
        {
            foreach (var kingdom in activeKingdoms)
            {
                if (kingdom.IsEmpire()) continue;
                var regime = kingdom.GetRegime();
                if (regime == null || regime.GetReligionLevel() != ReligionLevel.High) continue;
                if (religionLeaderList.Count >= 3) continue;
                var religionAreas = kingdom.cities
                    .Where(c => c?.data != null && !c.isRekt())
                    .OrderByDescending(c => c.countWarriors());
                religionLeaderList.AddRange(from area in religionAreas
                    where area.hasLeader() && area.leader?.data != null
                    select area.leader.id);
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
