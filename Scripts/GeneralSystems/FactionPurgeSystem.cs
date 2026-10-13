using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 清君侧(派系平衡的一环，见 FactionBalanceSystem)：一派独大、把持朝政时，被排挤的派系以"清君侧"为名起兵，讨伐独大派系的首领。
//   起兵条件(每年)：失衡度 ≥ 60，独大的是朝中文臣、外戚、教士或其他把持朝政的派系(武人跋扈另走禁军政变，见 PalaceArmySystem)；
//     被排挤的派系怨望 ≥ 60，且有本派的人主政某省(省的长官属于该派)；帝国眼下没有别的清君侧之战。
//     机会 = 10% + (失衡度 - 60) / 200 + (怨望 - 60) / 200，明君在位减半。
//   起兵：本派主政的各省一同起兵(改称本派名号、就地武装义军，见 RebellionStartupService)，向中央开战，战争名"某派清君侧，讨某某"；
//     中央照叛乱那样要先集结(见 MusterSystem)。
//   胜负(每月看)：起兵一方拿下京城 → 清君侧功成；起兵各省的都城都丢了 → 兵败；打满五年未决，按双方兵力判。
//   功成：独大派系的首领伏诛，该派让出四成占比给起兵的派系；独大派系的人若是权臣，权臣罢黜、天子不再被挟制；
//     失衡度只留三成，起兵派系怨望平复，失势的一派怨望大涨。
//   兵败：起兵派系怨望压到三成，独大派系更加专横(失衡度 +10)。
public static class FactionPurgeSystem
{
    private const float PurgeImbalance = 60f, PurgeGrievance = 60f;
    private const int RatioTransfer = 40;
    private const int StalemateYears = 5;

    // 每年(FactionBalanceSystem.Yearly 末尾)
    public static void TryRaise(Empire empire, List<FixedFaction> factions, FixedFaction top)
    {
        if (FactionBalanceSystem.Imbalance(empire) < PurgeImbalance || factions.Count < 2) return;
        FactionBalanceSystem.Direction direction = FactionBalanceSystem.DirectionOf(top);
        if (direction is FactionBalanceSystem.Direction.Military or FactionBalanceSystem.Direction.None) return;
        Kingdom core = empire.CoreKingdom;
        if (core == null || core.capital == null || Active(empire) != null) return;
        FixedFaction rebels = factions.Where(faction => faction != top)
            .OrderByDescending(faction => CourtSystem.FactionState(empire, faction).grievance).First();
        float grievance = CourtSystem.FactionState(empire, rebels).grievance;
        if (grievance < PurgeGrievance) return;
        List<Kingdom> provinces = empire.kingdoms_list.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                kingdom != core && kingdom.capital != null && kingdom.king?.GetFaction() == rebels &&
                !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling() && !kingdom.getWars().Any() &&
                RebellionSystem.CanAttempt(kingdom))
            .OrderByDescending(kingdom => kingdom.countTotalWarriors()).ToList();
        if (provinces.Count == 0) return;
        float chance = 0.1f + (FactionBalanceSystem.Imbalance(empire) - PurgeImbalance) / 200f + (grievance - PurgeGrievance) / 200f;
        if (RulerQualitySystem.Of(empire.Emperor) == RulerQualitySystem.Grade.Wise) chance *= 0.5f;
        if (UnityEngine.Random.value >= chance) return;
        Raise(empire, rebels, top, provinces, grievance);
    }

    private static void Raise(Empire empire, FixedFaction rebels, FixedFaction top, List<Kingdom> provinces, float grievance)
    {
        Kingdom core = empire.CoreKingdom;
        Kingdom leader = provinces[0];
        RebellionCauseData cause = RebellionSystem.Capture(leader, leader.capital, "rebellion_reason_purge", top.Name);
        leader.StartFactionRebelling(rebels);
        War war = World.world.diplomacy.startWar(leader, core, WarTypeLibrary.normal);
        if (war == null)
        {
            leader.EndFactionRebelling();
            return;
        }
        war.SetEmpireWarType(EmpireWarType.清君侧, belongingFaction: rebels);
        Actor villain = top.GetLeader();
        war.data.name = string.Format(LM.Get("purge_war_name"), rebels.Name, villain?.getName() ?? top.Name);
        var data = war.GetOrCreate();
        data.purge_faction_id = rebels.GetID();
        data.purge_target_faction_id = top.GetID();
        data.purge_capital_city_id = core.capital.data.id;
        foreach (Kingdom province in provinces.Skip(1))
        {
            province.StartFactionRebelling(rebels);
            war.joinAttackers(province);
        }
        // 郡县多不许养兵，起兵各省就地武装义军
        foreach (Kingdom province in provinces) RebellionStartupService.RaiseUprisingMilitia(province, grievance / 100f);
        RebellionSystem.Record(leader, war, cause);
        string text = string.Format(LM.Get("history_purge_raised"), rebels.Name, villain?.getName() ?? top.Name,
            string.Join("、", provinces.Select(province => province.GetKingdomName())));
        empire.RecordHistory(directContent: text, actorId: leader.king?.id ?? -1L, kingdomId: core.id);
        TranslateHelper.LogEventMessage(text, core);
    }

    // 帝国眼下的清君侧之战(本系统发起的)
    public static War Active(Empire empire) =>
        empire?.CoreKingdom?.getWars().FirstOrDefault(war => war != null && !war.hasEnded() && IsPurge(war));

    public static bool IsPurge(War war) =>
        war?.data != null && war.GetEmpireWarType() == EmpireWarType.清君侧 &&
        !string.IsNullOrEmpty(war.GetOrCreate().purge_target_faction_id);

    // 每月(FrontSystem.MonthTick)：判胜负
    public static void MonthTick()
    {
        foreach (War war in World.world.wars.ToList())
        {
            if (!IsPurge(war) || !war.isAlive() || war.hasEnded()) continue;
            try
            {
                WarWinner? winner = Decide(war);
                if (winner != null) DiplomacyHelpers.wars.endWar(war, winner.Value);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][清君侧] 判定失败: {exception.Message}");
            }
        }
    }

    private static WarWinner? Decide(War war)
    {
        var data = war.GetOrCreate();
        City capital = World.world.cities.get(data.purge_capital_city_id);
        List<Kingdom> attackers = war._list_attackers.Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList();
        if (capital?.kingdom != null && attackers.Contains(capital.kingdom)) return WarWinner.Attackers;
        if (attackers.Count == 0 || attackers.All(kingdom => kingdom.capital == null || !kingdom.hasCities())) return WarWinner.Defenders;
        if (Date.getYearsSince(war.data.created_time) < StalemateYears) return null;
        float attack = attackers.Sum(Strength);
        float defend = war._list_defenders.Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).Sum(Strength);
        return attack > defend ? WarWinner.Attackers : WarWinner.Defenders;
    }

    private static float Strength(Kingdom kingdom) =>
        CorpsSystem.Enabled ? CorpsSystem.Of(kingdom).Sum(corps => corps.strength) : kingdom.countTotalWarriors();

    // 战争结束(WarPatch)：结算清君侧
    public static void Resolve(War war, WarWinner winner)
    {
        if (!IsPurge(war)) return;
        var data = war.GetOrCreate();
        Kingdom core = war.main_defender;
        Empire empire = core?.GetEmpire();
        foreach (Kingdom kingdom in war._list_attackers.ToList())
            if (kingdom?.data != null && !kingdom.isRekt()) kingdom.EndFactionRebelling();
        if (empire == null || empire.isRekt()) return;
        FixedFaction rebels = CourtSystem.FindFaction(empire, data.purge_faction_id);
        FixedFaction target = CourtSystem.FindFaction(empire, data.purge_target_faction_id);
        if (rebels == null || target == null) return;
        string text;
        if (winner == WarWinner.Attackers)
        {
            Actor villain = target.GetLeader();
            string villainName = villain?.getName() ?? target.Name;
            Actor minister = empire.GetPowerfulMinister();
            if (minister?.data != null && minister.GetFaction() == target)
            {
                empire.ResetPowerfulMinister();
                empire.data.is_been_controlled = false;
            }
            if (villain?.data != null && villain.isAlive() && villain != empire.Emperor)
                villain.die(false, AttackType.Other, true, true);
            core.TryIncreaseFactionRatio(rebels, RatioTransfer);
            empire.data.faction_imbalance *= 0.3f;
            empire.data.faction_imbalance_warned = false;
            CourtSystem.FactionState(empire, rebels).grievance = 20f;
            CourtSystem.FactionState(empire, target).grievance =
                Mathf.Min(100f, CourtSystem.FactionState(empire, target).grievance + 40f);
            text = string.Format(LM.Get("history_purge_success"), rebels.Name, villainName, target.Name);
        }
        else
        {
            CourtSystem.FactionState(empire, rebels).grievance = 30f;
            empire.data.faction_imbalance = Mathf.Min(100f, empire.data.faction_imbalance + 10f);
            text = string.Format(LM.Get("history_purge_failed"), rebels.Name, target.Name);
        }
        empire.RecordHistory(directContent: text, kingdomId: core.id);
        TranslateHelper.LogEventMessage(text, core);
    }
}
