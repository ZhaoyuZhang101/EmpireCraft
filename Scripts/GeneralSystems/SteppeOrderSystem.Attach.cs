using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 3. 依附强部(不需要雄主)：弱小的游牧政权(独立的国，走出部落阶段，不在打仗，不是谁的朝贡国)自愿依附接壤的最强汗国
//    (核心是游牧政权的帝国)，并入为兀鲁思——国力不到汗国的 AttachPowerShare、对汗国核心好感不坏，每年 SteppeRules.AttachChance
//    (草原压力 30 以上更想投靠)；自己离开、神力放出不久的不回去(见 MembershipCooldown)。
//    若即若离，每年看一回自愿依附来的兀鲁思：
//      · 大汗换了人(老汗死了、被废)：这一回按 LeaveOnSuccession 的机会散去(每换一位汗掷一次)；
//      · 汗国衰落——附从长到汗国国力的 OutgrowShare 以上、汗国正统不到 LowLegitimacy、或汗国这些仗里丢了两成以上的城
//        (见 NomadCycleSystem.LostShare)：每年按 LeaveOnDecline 的机会散去；
//      · 灾年过去(用户 2026-10-27："都悲怆纪元了为什么不解散"——悲怆也是寒冬纪元，压力没降；寒冬过去以后以前也不散)：
//        附从自己的草原压力回落到 AttachPressure 以下、依附满 ReliefYears 年，每年按 LeaveOnRelief 的机会散去。
//    散去是和平离开汗国(不开战)；汗位更替之乱里据地自立的照旧(见 NomadCycleSystem)。
public static partial class SteppeOrderSystem
{
    private const float AttachPowerShare = 0.35f, OutgrowShare = 0.6f, LeaveOnSuccession = 0.35f, LeaveOnDecline = 0.25f,
        DeclineLostShare = 0.2f, LeaveOnRelief = 0.15f;
    private const int ReliefYears = 10;
    private const int LowLegitimacy = 40, AttachOpinion = 0;

    // 依附来的兀鲁思(汗国主页草原形势卡用，见 SteppePanel)
    public static int AttachedCount(Empire empire) =>
        empire?.kingdoms_list?.Count(kingdom => kingdom?.data != null && !kingdom.isRekt() &&
                                                NomadRaidSystem.State(kingdom).attached_empire_id == empire.id) ?? 0;

    // 汗国：核心是走出部落阶段的游牧政权的帝国
    private static bool IsKhanate(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && Alive(empire.CoreKingdom) &&
        NomadRaidSystem.IsNomad(empire.CoreKingdom) && !TribalFissionService.IsTribal(empire.CoreKingdom) &&
        !RepublicSystem.IsRepublic(empire);

    private static void AttachYearly(List<Kingdom> nomads)
    {
        foreach (Kingdom kingdom in nomads.ToList())
        {
            if (!Alive(kingdom)) continue;
            SteppeState state = NomadRaidSystem.State(kingdom);
            if (state.attached_empire_id >= 0) Drift(kingdom, state);
            else TryAttach(kingdom, state);
        }
    }

    // 投靠：接壤的最强汗国
    private static void TryAttach(Kingdom kingdom, SteppeState state)
    {
        if (kingdom.GetEmpire() != null || kingdom.IsEmpire() || !NomadRaidSystem.IsNomad(kingdom) ||
            TribalFissionService.IsTribal(kingdom) || !kingdom.hasKing() || kingdom.hasEnemies() || kingdom.HasTakenAlliance() ||
            kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()) return;
        Empire best = null;
        double bestPower = 0d;
        foreach (Kingdom neighbour in Neighbours(kingdom))
        {
            Empire empire = neighbour.GetEmpire();
            if (!IsKhanate(empire) || empire == best) continue;
            double power = WarSituation.Power(empire);
            if (power <= bestPower) continue;
            best = empire;
            bestPower = power;
        }
        if (best == null) return;
        double own = kingdom.GetNationalPower();
        Kingdom core = best.CoreKingdom;
        if (own >= bestPower * AttachPowerShare || kingdom.isInWarWith(core) ||
            (World.world.diplomacy?.getOpinion(kingdom, core)?.total ?? 0) < AttachOpinion ||
            MembershipCooldown.BlocksVoluntary(kingdom, best)) return;
        if (UnityEngine.Random.value >= SteppeRules.AttachChance(state.pressure)) return;
        if (kingdom.hasAlliance()) kingdom.getAlliance().leave(kingdom);
        best.join(kingdom, true, pForce: true);
        if (kingdom.GetEmpire() != best) return;
        EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        state.attached_empire_id = best.id;
        state.attached_at = World.world.getCurWorldTime();
        state.attached_khan_id = best.Emperor?.id ?? -1L;
        Chronicle(string.Format(LM.Get("steppe_attach_history"), kingdom.GetKingdomName(), best.GetEmpireName()), kingdom, core);
        LogService.LogInfo($"{LogTag} 依附强部：{kingdom.GetKingdomName()}(国力{own:0}，压力{state.pressure:0})投靠" +
                           $"{best.GetEmpireName()}(国力{bestPower:0})为兀鲁思");
    }

    // 若即若离：大汗换人、汗国衰落时散去
    private static void Drift(Kingdom kingdom, SteppeState state)
    {
        Empire empire = ModClass.EMPIRE_MANAGER?.get(state.attached_empire_id);
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || kingdom.GetEmpire() != empire || empire.CoreKingdom == kingdom)
        {
            state.attached_empire_id = state.attached_khan_id = -1L;
            state.attached_at = -1d;
            return;
        }
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling() || kingdom.hasEnemies()) return;
        long khan = empire.Emperor?.id ?? -1L;
        string key = null;
        float chance = 0f;
        if (khan != state.attached_khan_id)
        {
            state.attached_khan_id = khan;
            key = "steppe_detach_succession_history";
            chance = LeaveOnSuccession;
        }
        else if (kingdom.GetNationalPower() >= WarSituation.Power(empire) * OutgrowShare || empire.Legitimacy < LowLegitimacy ||
                 NomadCycleSystem.LostShare(empire.CoreKingdom) >= DeclineLostShare)
        {
            key = "steppe_detach_decline_history";
            chance = LeaveOnDecline;
        }
        else if (state.pressure_at >= 0d && state.pressure < SteppeRules.AttachPressure && state.attached_at >= 0d &&
                 World.world.getCurWorldTime() >= state.attached_at && Date.getYearsSince(state.attached_at) >= ReliefYears)
        {
            key = "steppe_detach_relief_history";
            chance = LeaveOnRelief;
        }
        if (key == null || UnityEngine.Random.value >= chance) return;
        empire.leave(kingdom, true, true);
        if (kingdom.GetEmpire() == empire) return;
        state.attached_empire_id = state.attached_khan_id = -1L;
        state.attached_at = -1d;
        Chronicle(string.Format(LM.Get(key), kingdom.GetKingdomName(), empire.GetEmpireName()), kingdom, empire.CoreKingdom);
        LogService.LogInfo($"{LogTag} 附从散去：{kingdom.GetKingdomName()} 离开{empire.GetEmpireName()}" +
                           $"({(key == "steppe_detach_succession_history" ? "易汗" : key == "steppe_detach_relief_history" ? $"灾年已过，压力{state.pressure:0}" : $"汗国势衰，正统{empire.Legitimacy}")})");
    }
}
