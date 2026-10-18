using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 跨文明大国外交(用户 2026-10-19 选"跨文明大国外交"；以前同物种的两个帝国一律 -999 好感、帝国之间不能结好)：
//   · 两个皇帝：同一文明(同天下，见 CultureService.SameKin)的两个帝国照旧势不两立；不同文明的帝国按远近相忌——
//     都城相距远的几乎无碍(-5)，中等(-20)，近的(-50)；
//   · 通好：不同文明的两个帝国不在打仗、好感不太坏时，每年有机会遣使通好(有共同的敌人时更容易)——互认对方的称号
//     (皇帝、大可汗、哈里发、元首……)，三十年内互不侵犯(见 WarGate、WarTargetPatch)、好感 +40；
//     一方遭第三国攻打，另一方没在打仗、过得了开战闸门时，每月有机会依约出兵相助。
public static class GreatPowerDiplomacy
{
    private const int PactYears = 30, PactOpinion = 40;
    private const float PactChance = 0.08f, CommonEnemyChance = 0.4f, AidChance = 0.15f, MinOpinion = -60f;
    private static double _lastScan = -1d;

    public static void ResetWorldState() => _lastScan = -1d;

    private static Dictionary<long, double> Pacts(Empire empire) => empire.data.diplomatic_pacts ??= new Dictionary<long, double>();

    private static bool Active(Empire a, Empire b) =>
        a?.data != null && b?.data != null && Pacts(a).TryGetValue(b.id, out double since) && since >= 0d &&
        Date.getYearsSince(since) < PactYears;

    // 两国(或所在的帝国)通好之中：互不侵犯
    public static bool Bound(Kingdom a, Kingdom b)
    {
        Empire ea = a?.GetEmpire(), eb = b?.GetEmpire();
        return ea != null && eb != null && ea != eb && Active(ea, eb);
    }

    private static bool SameCivilization(Kingdom a, Kingdom b) =>
        CultureService.SameKin(CultureService.GetRealmCulture(a), CultureService.GetRealmCulture(b));

    // 两个皇帝之间的好感(见 EmpireCraftOpinionAddition)
    public static int EmperorRivalry(Kingdom a, Kingdom b)
    {
        if (Bound(a, b)) return 0;
        if (SameCivilization(a, b)) return -999;
        if (a.capital == null || b.capital == null) return -20;
        float distance = Vector2.Distance(a.capital.city_center, b.capital.city_center);
        return distance > 300f ? -5 : distance > 150f ? -20 : -50;
    }

    // 通好之国的好感
    public static int PactOpinionOf(Kingdom main, Kingdom target) =>
        main != null && target != null && main.IsEmpire() && target.IsEmpire() && Bound(main, target) ? PactOpinion : 0;

    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        List<Empire> empires = ModClass.EMPIRE_MANAGER.Where(empire => empire?.data != null && !empire.isRekt() &&
            !empire.IsArchived() && empire.CoreKingdom?.data != null && !empire.CoreKingdom.isRekt()).ToList();
        for (int i = 0; i < empires.Count; i++)
        for (int j = i + 1; j < empires.Count; j++)
        {
            try
            {
                Consider(empires[i], empires[j]);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][通好] {empires[i].GetEmpireName()}、{empires[j].GetEmpireName()} 结算失败: {exception.Message}");
            }
        }
    }

    private static void Consider(Empire a, Empire b)
    {
        Kingdom ca = a.CoreKingdom, cb = b.CoreKingdom;
        if (Active(a, b) || SameCivilization(ca, cb) || ca.isInWarWith(cb)) return;
        float opinion = World.world.diplomacy?.getOpinion(ca, cb)?.total ?? 0f;
        if (opinion < MinOpinion) return;
        bool common = ca.getEnemiesKingdoms().Intersect(cb.getEnemiesKingdoms()).Any();
        if (UnityEngine.Random.value >= (common ? CommonEnemyChance : PactChance)) return;
        double now = World.world.getCurWorldTime();
        Pacts(a)[b.id] = now;
        Pacts(b)[a.id] = now;
        string text = string.Format(LM.Get("great_power_pact_history"), a.GetEmpireName(), b.GetEmpireName(), Title(ca), Title(cb));
        a.RecordHistory(directContent: text, kingdomId: ca.id);
        b.RecordHistory(directContent: text, kingdomId: cb.id);
        TranslateHelper.LogEventMessage(text, ca);
    }

    private static string Title(Kingdom core)
    {
        Regimes.RegimeType? regime = core.GetRegime()?.type;
        return regime == null ? LM.Get("actor_emperor_B") : LM.Get(DynastyNaming.RankKey(regime.Value, 0, core));
    }

    // 依约相助(王国 AI 每月一查，帝国核心自己查)
    public static void Aid(Kingdom core)
    {
        Empire empire = core?.GetEmpire();
        if (empire?.data?.diplomatic_pacts == null || empire.CoreKingdom != core || core.hasEnemies() ||
            UnityEngine.Random.value >= AidChance) return;
        foreach (long partnerId in empire.data.diplomatic_pacts.Keys.ToList())
        {
            Empire partner = ModClass.EMPIRE_MANAGER.get(partnerId);
            Kingdom ally = partner?.CoreKingdom;
            if (ally?.data == null || ally.isRekt() || !Active(empire, partner)) continue;
            War war = ally.getWars().FirstOrDefault(candidate =>
                candidate?.data != null && candidate.isAlive() && !candidate.hasEnded() && candidate.isDefender(ally) &&
                !candidate.hasKingdom(core) && candidate.main_attacker?.GetEmpire() != empire);
            Kingdom attacker = war?.main_attacker;
            if (attacker?.data == null || attacker.isRekt() || EmpirePeacePatch.HasLeft(core, war, joiningAttackers: false) ||
                WarSituation.TooBusyForWar(core) || CorpsSystem.CannotFundWar(core) || !WarGate.CanDeclare(core, attacker)) continue;
            war.joinDefenders(core);
            if (!war._list_defenders.Contains(core)) continue;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("great_power_aid"), empire.GetEmpireName(),
                partner.GetEmpireName(), attacker.GetKingdomName()), core);
            return;
        }
    }
}
