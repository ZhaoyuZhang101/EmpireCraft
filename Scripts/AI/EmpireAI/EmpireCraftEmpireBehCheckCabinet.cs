using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.AI.EmpireAI;

public class EmpireCraftEmpireBehCheckCabinet : GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();
    
    public override BehResult execute(Kingdom pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Stop;
        var ked = KingdomExtension.GetOrCreate(pKingdom);
        if (ked != null && ked.last_cabinet_check_ts > 0)
        {
            if (Date.getMonthsSince(ked.last_cabinet_check_ts) < 1)
            {
                return BehResult.Continue;
            }
        }
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived()) return BehResult.Continue;
        var core = empire.CoreKingdom;
        if (core == null) return BehResult.Continue;
        Regime regime = core.GetRegime();
        if (regime == null) return BehResult.Continue;
        var factions = regime.GetPlayerFactions() ?? new List<FixedFaction>();
        for (int i = 0; i < factions.Count; i++)
        {
            factions[i].FixMissedTemporaryFactions();
        }
        var kRegime = pKingdom.GetRegime();
        if (kRegime == null) return BehResult.Continue;
        switch (kRegime.type)
        {
            case RegimeType.LvLing:
                SetCabinetForLvLing(empire);
                break;
            case RegimeType.Feudalism:
                SetCabinetForFeudalism(empire);
                break;
            case RegimeType.ZhouFeudalism:
                break;
            case RegimeType.Modern:
                break;
            case RegimeType.Arabic:
                break;
            case RegimeType.YouMu:
                break;
            default:
                break;
        }

        if (kRegime.has_cabinet)
        {
            empire.Additions.cabinet_acc = IsCabinetControlEmpire(pKingdom) ? 30 : 0;
        }
        if (ked != null) ked.last_cabinet_check_ts = World.world.getCurWorldTime();
        return base.execute(pKingdom);
    }

    public static bool IsCabinetControlEmpire(Kingdom pKingdom)
    {
        //todo: 派系完全控制内閣
        var reg = pKingdom.GetRegime();
        if (reg == null) return false;
        var dominate = reg.GetDominateFaction();
        if (dominate == null) return false;
        var emp = pKingdom.GetEmpire();
        if (emp == null || emp.isRekt() || emp.IsArchived()) return false;
        var members = emp.GetCabinetMembers();
        if (members == null || members.Count == 0) return false;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m == null) return false;
            if (m.GetFaction()?.GetID() != dominate?.GetID()) return false;
        }
        return true;
    }

    public void SetCabinetForLvLing(Empire empire)
    {
        if (empire == null || empire.isRekt() || empire.IsArchived()) return;
        var core = empire.CoreKingdom;
        if (core == null) return;
        Regime regime = core.GetRegime();
        if (regime == null) return;
        var dominateFaction = regime.GetDominateFaction();
        if (dominateFaction==null) return;
        if (dominateFaction.Members.Count<=0) return;
        // —— 1) 计算内阁规模：0~15 → 1~5 ——
        int S = empire.Emperor?.stewardship??0;        // 组织能力
        if (S < 3) S = 0; if (S > 15) S = 15;      // 手动 clamp
        int cabinetSize = 1 + (S * regime.cabinet_number-1) / 15;        // 线性映射到 1..5，最多 5 个

        long cabinetLeader = -1L;
        double bestPerf = int.MinValue;
            var ms = dominateFaction.Members;
        for (int i = 0; i < ms.Count; i++)
        {
            var actor = world.units.get(ms[i]);
            var perf = actor?.GetIdentity()?.TotalPerformance ?? 0;
            if (perf > bestPerf)
            {
                bestPerf = perf;
                cabinetLeader = ms[i];
            }
        }
        if (cabinetLeader != empire.GetCabinetLeader()?.id)
        {
            empire.SetCabinetLeader(world.units.get(cabinetLeader));  
        }

        while (empire.data.CabinetMembers.Count > cabinetSize)
        {
            empire.data.CabinetMembers.RemoveAt(empire.data.CabinetMembers.Count - 1);
        }
        while (empire.data.CabinetMembers.Count < cabinetSize)
        {
            var allMembers = regime.GetAllFactionMembers();
            Actor best = null;
            double bestP = int.MinValue;
            for (int i = 0; i < allMembers.Count; i++)
            {
                var a = allMembers[i];
                var id = a?.id ?? -1L;
                if (id == -1L) continue;
                if (empire.data.CabinetMembers.Contains(id)) continue;
                var p = a?.GetIdentity()?.TotalPerformance ?? 0;
                if (p > bestP)
                {
                    bestP = p;
                    best = a;
                }
            }
            empire.AddCabinetMember(best);
        }
        
    }

    public void SetCabinetForFeudalism(Empire empire)
    {
        List<long> religionLeaderList = new();
        List<long> normalKingList = new List<long>();
        var ks = empire.kingdoms_list;
        int topCount = 4;
        int[] topsVal = new int[topCount];
        long[] topsId = new long[topCount];
        for (int i = 0; i < topCount; i++) { topsVal[i] = int.MinValue; topsId[i] = -1L; }
        for (int i = 0; i < ks.Count; i++)
        {
            var k = ks[i];
            if (!k.hasKing() || k.IsEmpire()) continue;
            var r = k.GetRegime();
            if (r == null) continue;
            if (r.GetReligionLevel() == ReligionLevel.High) continue;
            int v = k.countTotalWarriors();
            for (int j = 0; j < topCount; j++)
            {
                if (v > topsVal[j])
                {
                    for (int shift = topCount - 1; shift > j; shift--)
                    {
                        topsVal[shift] = topsVal[shift - 1];
                        topsId[shift] = topsId[shift - 1];
                    }
                    topsVal[j] = v;
                    topsId[j] = k.king.id;
                    break;
                }
            }
        }
        for (int i = 0; i < topCount; i++) { normalKingList.Add(topsId[i]); }
        if (!empire.Religion.isRekt())
        {
            for (int i = 0; i < ks.Count; i++)
            {
                var kingdom = ks[i];
                if (kingdom.IsEmpire()) continue;
                var regime = kingdom.GetRegime();
                if (regime == null) continue;
                if (regime.GetReligionLevel() != ReligionLevel.High) continue;
                if (religionLeaderList.Count >= 3) continue;
                var cities = kingdom.cities;
                int need = 3 - religionLeaderList.Count;
                int[] cTopVal = new int[need];
                long[] cTopId = new long[need];
                for (int t = 0; t < need; t++) { cTopVal[t] = int.MinValue; cTopId[t] = -1L; }
                for (int c = 0; c < cities.Count; c++)
                {
                    var city = cities[c];
                    int v = city.countWarriors();
                    long id = city.hasLeader() ? city.leader.id : -1L;
                    if (id == -1L) continue;
                    for (int j = 0; j < need; j++)
                    {
                        if (v > cTopVal[j])
                        {
                            for (int shift = need - 1; shift > j; shift--)
                            {
                                cTopVal[shift] = cTopVal[shift - 1];
                                cTopId[shift] = cTopId[shift - 1];
                            }
                            cTopVal[j] = v;
                            cTopId[j] = id;
                            break;
                        }
                    }
                }
                for (int t = 0; t < need; t++)
                {
                    if (cTopId[t] != -1L) religionLeaderList.Add(cTopId[t]);
                }
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
