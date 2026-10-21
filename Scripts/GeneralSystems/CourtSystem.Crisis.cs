using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public static partial class CourtSystem
{
    public static float BoycottShare(Empire empire) => Factions(empire)
        .Where(faction => IsBoycotting(FactionState(empire, faction))).Sum(faction => Mathf.Max(0, faction.CentralRatio));

    public static bool InCrisis(Empire empire) => empire?.Legitimacy < 30 && State(empire)?.crisis_years >= 2;

    private static void AdvanceCrisis(Empire empire, CourtState state, List<FixedFaction> factions)
    {
        ReleaseCoalitionRebels(state);
        long emperor = empire.Emperor.id;
        if (state.crisis_emperor_id != emperor) state.crisis_years = 0;
        state.crisis_emperor_id = emperor;
        float share = factions.Where(faction => IsBoycotting(FactionState(empire, faction)))
            .Sum(faction => Mathf.Max(0, faction.CentralRatio));
        state.crisis_years = CourtCrisisRules.AdvanceYears(empire.Legitimacy, share, state.crisis_years);
        if (state.crisis_years == 1 && empire.Legitimacy < 30 && share >= 40f)
            Log(state, "court_log_crisis", Mathf.RoundToInt(share).ToString());
    }

    // 各派可暂时联合逼退失去支持的君主，无须其中一派独占两成。
    // 地方兵谏与中枢废立沿用既有战争、继承和整城归属结算，不修改城市地块。
    private static bool TryCoalition(Empire empire, CourtState state, List<FixedFaction> factions)
    {
        Kingdom core = empire.CoreKingdom;
        if (!InCrisis(empire) || core?.capital == null || !RegimeManager.IsMonarchy(core.GetRegime()?.type) ||
            RepublicSystem.IsRepublic(empire) || !RebellionSystem.CanAttempt(core)) return false;
        double now = World.world.getCurWorldTime();
        if (state.last_escalation >= 0d && now >= state.last_escalation && Date.getYearsSince(state.last_escalation) < 5)
            return false;
        // 已经起兵就让战事决定胜负，不能每年再开一轮同类战争。
        if (core.getWars().Any(war => war != null && !war.hasEnded() &&
                war.GetEmpireWarType() is EmpireWarType.派系叛乱 or EmpireWarType.清君侧)) return false;
        var coalition = factions.Where(faction => IsBoycotting(FactionState(empire, faction)) &&
                FactionState(empire, faction).favor <= 35f && faction.CentralRatio > 0)
            .OrderByDescending(faction => faction.CentralRatio * FactionState(empire, faction).grievance).ToList();
        float power = coalition.Sum(faction => faction.CentralRatio);
        if (power <= 0f || !CourtCrisisRules.CanCoalition(empire.Legitimacy, state.crisis_years, power,
                coalition.Sum(faction => faction.CentralRatio * FactionState(empire, faction).grievance) / power,
                coalition.Sum(faction => faction.CentralRatio * FactionState(empire, faction).favor) / power,
                coalition.Sum(faction => FactionState(empire, faction).capital))) return false;

        var supporters = new HashSet<FixedFaction>(coalition);
        // 政治兵谏的组织基础是本派主政的成员国，不能再要求先发生民间动乱。
        List<Kingdom> provinces = empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                kingdom != core && kingdom.capital != null && kingdom.king != null && supporters.Contains(kingdom.king.GetFaction()) &&
                !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling() && RebellionSystem.CanAttempt(kingdom) &&
                !kingdom.getWars().Any(war => war != null && !war.hasEnded()))
            .OrderByDescending(kingdom => kingdom.countTotalWarriors()).ToList();
        if (provinces.Count > 0)
        {
            Kingdom leader = provinces[0];
            var cause = RebellionSystem.Capture(leader, leader.capital, "rebellion_reason_court",
                string.Join("、", coalition.Select(faction => faction.Name)));
            leader.StartFactionRebelling(leader.king.GetFaction());
            War war = World.world.diplomacy.startWar(leader, core, WarTypeLibrary.normal);
            if (war == null) { leader.EndFactionRebelling(); return false; }
            war.SetEmpireWarType(EmpireWarType.派系叛乱);
            // 派系写进战争名(国名不改，见 StartFactionRebelling)
            string banner = leader.king.GetFaction()?.Name;
            if (!string.IsNullOrWhiteSpace(banner)) war.data.name = string.Format(LM.Get("faction_rebellion_war_name"), banner);
            var risen = new List<Kingdom> { leader };
            foreach (Kingdom province in provinces.Skip(1))
            {
                province.StartFactionRebelling(province.king.GetFaction());
                war.joinAttackers(province);
                if (war._list_attackers.Contains(province)) risen.Add(province);
                else province.EndFactionRebelling();
            }
            foreach (Kingdom province in risen) RebellionStartupService.RaiseUprisingMilitia(province, power / 100f);
            (state.crisis_rebels ??= new List<long>()).AddRange(risen.Select(province => province.id));
            RebellionSystem.Record(leader, war, cause);
            string places = string.Join("、", risen.Select(province => province.GetKingdomName()));
            Log(state, "court_log_coalition", places);
            empire.RecordHistory(directContent: string.Format(LM.Get("court_history_coalition"),
                string.Join("、", coalition.Select(faction => faction.Name)), places), kingdomId: core.id);
            SettleCoalition(state, coalition, "mutiny", places, now);
            return true;
        }

        // 有成员国也不代表中枢永远不能逼宫，但必须掌握议事会、权臣或过半不忠禁军。
        List<Actor> council = Council(empire);
        List<CorpsData> palace = PalaceArmySystem.Palace(core).ToList();
        float troops = palace.Sum(corps => Mathf.Max(0f, corps.strength));
        float backing = palace.Where(corps => corps.loyalty < 40f && corps.general_id >= 0 &&
                supporters.Contains(World.world.units.get(corps.general_id)?.GetFaction()))
            .Sum(corps => Mathf.Max(0f, corps.strength));
        bool controlsCourt = council.Count > 0 && council.Count(actor => supporters.Contains(actor.GetFaction())) * 2 >= council.Count;
        bool controlsMinister = empire.GetPowerfulMinister() is Actor minister && supporters.Contains(minister.GetFaction());
        if (power < 50f || !controlsCourt && !controlsMinister && !(troops > 0f && backing >= troops * 0.5f)) return false;
        ConstitutionalEconomyState economy = empire.data.constitutional_economy;
        if (economy?.last_coup_at >= 0d && now >= economy.last_coup_at && Date.getYearsSince(economy.last_coup_at) < 20)
            return false;
        Actor installed = empire.CompleteFactionDeposition(coalition[0]);
        if (installed == null) return false;
        if (economy != null) economy.last_coup_at = now;
        Log(state, "court_log_palace_coup", string.Join("、", coalition.Select(faction => faction.Name)));
        SettleCoalition(state, coalition, "palace_coup", installed.getName(), now);
        return true;
    }

    // 联省兵谏的成员中途退出战争(城破求和、被打出战争)时不在停战名单里，叛乱状态与临时国名一直挂着、以后也选不进兵谏：
    // 每年把已不在任何派系叛乱战争里的撤掉
    private static void ReleaseCoalitionRebels(CourtState state)
    {
        if (state.crisis_rebels == null || state.crisis_rebels.Count == 0) return;
        foreach (long id in state.crisis_rebels.ToList())
        {
            Kingdom kingdom = World.world.kingdoms.get(id);
            bool fighting = kingdom != null && !kingdom.isRekt() && kingdom.getWars().Any(war =>
                war != null && !war.hasEnded() && war.GetEmpireWarType() == EmpireWarType.派系叛乱);
            if (fighting) continue;
            if (kingdom != null && !kingdom.isRekt() && kingdom.IsFactionRebelling()) kingdom.EndFactionRebelling();
            state.crisis_rebels.Remove(id);
        }
    }

    private static void SettleCoalition(CourtState state, List<FixedFaction> coalition, string action, string detail, double now)
    {
        foreach (FixedFaction faction in coalition)
        {
            CourtFactionState data = state.factions[faction.GetID()];
            data.capital = Mathf.Max(0f, data.capital - 20f);
            data.grievance = 50f;
            if (action == "palace_coup") data.favor = 60f;
            Act(data, action, detail);
        }
        state.crisis_years = 0;
        state.last_escalation = now;
    }
}
