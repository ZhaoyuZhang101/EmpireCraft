using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 禁军(帝国中央驻都城的军)腐败与不听调：
//   · 空饷：账面兵力照领军饷，能打的按实兵算——空饷比例每月向"中央腐败 × 0.6"靠拢(最多五成)，交战战力扣掉空饷；
//   · 忠诚(0~100)：每月向目标靠拢——基准 85；明君 +10，庸弱 -5，昏聩、荒淫 -15，暴虐 -20；
//     外戚或权臣专权 -15；欠饷按欠的比例最多 -40；同一统领掌军超过 15 年(私兵化) -15；
//   · 不听调：忠诚不到 40 时，每月按 (40 − 忠诚)/40 的概率不奉诏出征(只守都城，不去攻城)；
//   · 宫廷政变：忠诚不到 25 时，每年按 (25 − 忠诚)/25 × 0.25 的概率哗变(与兵变共用二十年冷却)——
//     废黜皇帝，改立储君或别的成年宗室；宗室里没人可立时，禁军统领自立(照兵变的易姓流程)。
public static class PalaceArmySystem
{
    private const float PhantomShare = 0.6f, PhantomCap = 0.5f, PhantomDrift = 0.1f;
    private const float BaseLoyalty = 85f, LoyaltyDrift = 5f;
    private const float RefuseLine = 40f, CoupLine = 25f, CoupFactor = 0.25f;
    private const int PrivateArmyYears = 15;

    // 禁军：帝国中央驻都城的军
    public static bool IsPalace(Kingdom kingdom, CorpsData corps)
    {
        Empire empire = kingdom?.GetEmpire();
        return corps != null && !corps.militia && corps.order_id < 0 && empire?.CoreKingdom == kingdom &&
               kingdom.capital != null && corps.home_city_id == kingdom.capital.id;
    }

    public static IEnumerable<CorpsData> Palace(Kingdom core) => CorpsSystem.Of(core).Where(corps => IsPalace(core, corps));

    // 实兵比例(交战战力用)
    public static float RealShare(CorpsData corps) => 1f - Mathf.Clamp(corps?.phantom ?? 0f, 0f, PhantomCap);

    // 本月不奉诏出征
    public static bool Refuses(CorpsData corps) => corps != null && corps.refusing;

    #region 每月

    public static void Monthly(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire?.CoreKingdom != kingdom) return;
        List<CorpsData> palace = Palace(kingdom).ToList();
        if (palace.Count == 0) return;
        double now = World.world.getCurWorldTime();
        float phantomTarget = Mathf.Min(PhantomCap, (float)CorruptionSystem.GetRate(empire) * PhantomShare);
        float baseTarget = LoyaltyTarget(empire, kingdom);
        bool refusedAny = false;
        foreach (CorpsData corps in palace)
        {
            corps.phantom = Mathf.MoveTowards(corps.phantom, phantomTarget, PhantomDrift * Mathf.Max(0.01f, Mathf.Abs(phantomTarget - corps.phantom)) + 0.002f);
            if (corps.general_id != corps.commander_seen)
            {
                corps.commander_seen = corps.general_id;
                corps.commander_since = now;
            }
            float target = baseTarget;
            if (corps.general_id >= 0 && corps.commander_since >= 0d && Date.getYearsSince(corps.commander_since) >= PrivateArmyYears)
                target -= 15f;
            corps.loyalty = Mathf.Clamp(Mathf.MoveTowards(corps.loyalty, Mathf.Clamp(target, 0f, 100f), LoyaltyDrift), 0f, 100f);
            corps.refusing = corps.loyalty < RefuseLine && UnityEngine.Random.value < (RefuseLine - corps.loyalty) / RefuseLine;
            refusedAny |= corps.refusing;
        }
        var data = kingdom.GetOrCreate();
        if (refusedAny && !data.palace_refusal_logged && CorpsSystem.IsAtWar(kingdom))
        {
            data.palace_refusal_logged = true;
            FrontSystem.Log(kingdom, string.Format(LM.Get("palace_army_refuses"), kingdom.GetKingdomName()));
        }
        else if (!refusedAny) data.palace_refusal_logged = false;
    }

    private static float LoyaltyTarget(Empire empire, Kingdom core)
    {
        float target = BaseLoyalty;
        target += RulerQualitySystem.Of(empire.Emperor) switch
        {
            RulerQualitySystem.Grade.Wise => 10f,
            RulerQualitySystem.Grade.Weak => -5f,
            RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => -15f,
            RulerQualitySystem.Grade.Tyrant => -20f,
            _ => 0f
        };
        if (empire.data.is_been_controlled || empire.data.powerful_minister_is_consort_kin && empire.GetPowerfulMinister() != null)
            target -= 15f;
        target -= Mathf.Clamp01(core.GetOrCreate().corps_pay_shortfall) * 40f;
        return target;
    }

    #endregion

    #region 每年：宫廷政变

    public static void Yearly(Empire empire)
    {
        try
        {
            Kingdom core = empire?.CoreKingdom;
            ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
            if (core?.data == null || state == null || empire.Emperor == null || !RebellionSystem.CanAttempt(core)) return;
            List<CorpsData> palace = Palace(core).ToList();
            if (palace.Count == 0) return;
            double now = World.world.getCurWorldTime();
            if (state.last_coup_at >= 0d && now >= state.last_coup_at && Date.getYearsSince(state.last_coup_at) < 20) return;
            float loyalty = palace.Sum(corps => corps.loyalty * Mathf.Max(1f, corps.strength)) /
                            Mathf.Max(1f, palace.Sum(corps => Mathf.Max(1f, corps.strength)));
            if (loyalty >= CoupLine || UnityEngine.Random.value >= (CoupLine - loyalty) / CoupLine * CoupFactor) return;
            Coup(empire, state, core, palace);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][禁军] 宫廷政变失败: {exception.Message}");
        }
    }

    private static void Coup(Empire empire, ConstitutionalEconomyState state, Kingdom core, List<CorpsData> palace)
    {
        Actor former = empire.Emperor;
        CorpsData biggest = palace.OrderByDescending(corps => corps.strength).First();
        Actor commander = biggest.general_id >= 0 ? World.world.units.get(biggest.general_id) : null;
        Actor leader = empire.GetPowerfulMinister() ?? commander;
        Actor successor = Successor(empire, former);
        string leaderName = leader?.getName() ?? LM.Get("palace_army_mutineers");
        if (successor != null)
        {
            state.last_coup_at = World.world.getCurWorldTime();
            core.removeKing();
            OfficeObject office = core.GetOffice();
            if (office != null)
            {
                office.meta_object = core;
                office.SetActor(successor);
            }
            else core.setKing(successor);
            foreach (CorpsData corps in palace) corps.loyalty = Mathf.Max(corps.loyalty, 50f);
            string text = string.Format(LM.Get("palace_coup_history"), leaderName, former.getName(), successor.getName(),
                empire.GetEmpireFullName());
            empire.RecordHistory(directContent: text, actorId: successor.id, kingdomId: core.id);
            former.RecordPersonalHistory(text, relatedActorId: successor.id);
            successor.RecordPersonalHistory(text, relatedActorId: former.id);
            TranslateHelper.LogEventMessage(text, core);
            return;
        }
        // 宗室无人可立：禁军统领自立(照兵变易姓)
        if (commander?.data != null && commander.isAlive() && commander.isAdult())
            MilitaryCoupSystem.PalaceSeize(empire, state, commander, CorpsSystem.Home(biggest));
    }

    // 改立：储君，否则成年的皇族宗亲(名望最高)
    private static Actor Successor(Empire empire, Actor former)
    {
        Kingdom core = empire.CoreKingdom;
        Actor heir = core.HasHeir() ? core.GetHeir() : null;
        if (heir?.data != null && heir != former && heir.isAlive() && !heir.isRekt()) return heir;
        return empire.EmpireSpecificClan?.AllAliveMembers?
            .Where(actor => actor?.data != null && actor != former && actor.isAlive() && !actor.isRekt() && actor.isAdult() &&
                            !actor.isKing() && actor.kingdom?.GetEmpire() == empire)
            .OrderByDescending(actor => actor.renown).FirstOrDefault();
    }

    #endregion
}
