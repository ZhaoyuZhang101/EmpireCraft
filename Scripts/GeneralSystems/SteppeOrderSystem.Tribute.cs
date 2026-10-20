using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 4. 朝贡换赏赐(厚往薄来，游牧平时的重要财源；接现有朝贡体系，见 TributaryRelations、KingdomExtension.StartToTaken)：
//    · 称臣：独立的游牧政权(走出部落阶段、不在打仗、不是谁的朝贡国)，接壤的定居帝国强盛——国力是它的 SubmitPowerLead 倍以上、
//      君主制、施行了朝贡体系类制度、正统不低于 SubmitLegitimacy、不在末世——而自己手头紧(草原压力里国库一项 > 0 或压力 ≥ 20)，
//      每年 SubmitChance 的机会自愿称臣纳贡(仍照朝贡的老规矩：自愿称臣的门槛、改投要满二十年……)；朝贡期间两边不开战(见 WarGate)；
//    · 赏赐：游牧朝贡国每年纳贡一份(户数一半)，定居宗主回赐 SteppeRules.GiftShare 倍——正统 30 时一倍、70 起两倍，末世减半，
//      正统不到 30 就断了(别的朝贡国照旧回赐三成)；回赐以宗主国库现钱为限；
//    · 草原压力：一年半内得过赏赐的，国库一项减半；赏赐断了(断在最近一回赏赐之后，十年内)算一项压力(同岁币停付，见 SteppeRules.Market)，
//      每年 RenounceChance 的机会不再称臣，之后那家定居帝国就是劫掠的首选(见 NomadRaidSystem.Grievance)。
public static partial class SteppeOrderSystem
{
    private const float SubmitPowerLead = 2f, SubmitChance = 0.15f, RenounceChance = 0.5f, SubmitPressure = 20f;
    private const int SubmitLegitimacy = 50, GiftCutYears = 10;
    public const float GiftReliefYears = 1.5f;

    // 定居宗主给游牧朝贡国回赐几倍(TributaryRelations.AfterTribute 调用)；不是游牧朝贡定居宗主返回 null(照旧三成)
    public static float? TributeGiftShare(Kingdom tributary, Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (!Alive(tributary) || !Alive(core) || !NomadRaidSystem.IsNomad(tributary) || NomadRaidSystem.IsNomad(core)) return null;
        return SteppeRules.GiftShare(empire.Legitimacy, DynasticCycleSystem.InCrisis(empire));
    }

    // 回赐之后记下(断了也记)
    public static void NoteGift(Kingdom tributary, Empire empire, int gift)
    {
        if (TributeGiftShare(tributary, empire) == null || World.world == null) return;
        SteppeState state = NomadRaidSystem.State(tributary);
        double now = World.world.getCurWorldTime();
        if (gift > 0)
        {
            state.gift_at = now;
            state.gift_from = empire.CoreKingdom.id;
            return;
        }
        bool fresh = state.gift_cut_at < state.gift_at || state.gift_cut_at < 0d;
        state.gift_cut_at = now;
        state.gift_cut_from = empire.CoreKingdom.id;
        if (fresh)
            LogService.LogInfo($"{LogTag} {empire.GetEmpireName()}(正统{empire.Legitimacy}" +
                               $"{(DynasticCycleSystem.InCrisis(empire) ? "，末世" : "")})断了给{tributary.GetKingdomName()}的赏赐");
    }

    // 一年半内得过赏赐(草原压力里国库一项减半)
    public static bool Gifted(SteppeState state) => state.gift_at >= 0d && YearsSince(state.gift_at) < GiftReliefYears;

    // 赏赐断了：断在最近一回赏赐之后、十年内，宗主还在；返回那家宗主的核心
    public static Kingdom GiftCut(SteppeState state) =>
        state.gift_cut_at >= 0d && state.gift_cut_at >= state.gift_at && YearsSince(state.gift_cut_at) < GiftCutYears
            ? LiveKingdom(state.gift_cut_from)
            : null;

    private static void TributeYearly(List<Kingdom> nomads)
    {
        foreach (Kingdom kingdom in nomads.ToList())
        {
            if (!Alive(kingdom) || !NomadRaidSystem.IsNomad(kingdom)) continue;
            SteppeState state = NomadRaidSystem.State(kingdom);
            if (kingdom.HasTakenAlliance()) Renounce(kingdom, state);
            else TrySubmit(kingdom, state);
        }
    }

    // 赏赐断了：不再称臣
    private static void Renounce(Kingdom kingdom, SteppeState state)
    {
        Empire lord = kingdom.GetTakenAllianceEmpire();
        Kingdom cut = GiftCut(state);
        if (lord?.CoreKingdom == null || cut != lord.CoreKingdom || UnityEngine.Random.value >= RenounceChance) return;
        kingdom.RemoveTakenAlliance();
        if (kingdom.HasTakenAlliance()) return;
        Chronicle(string.Format(LM.Get("steppe_tribute_renounce_history"), kingdom.GetKingdomName(), lord.GetEmpireName()),
            kingdom, lord.CoreKingdom);
        LogService.LogInfo($"{LogTag} {kingdom.GetKingdomName()} 赏赐断绝，不再向{lord.GetEmpireName()}称臣");
    }

    // 手头紧的游牧政权向接壤的强盛定居帝国称臣
    private static void TrySubmit(Kingdom kingdom, SteppeState state)
    {
        if (kingdom.GetEmpire() != null || kingdom.IsEmpire() || TribalFissionService.IsTribal(kingdom) || !kingdom.hasKing() ||
            kingdom.hasEnemies() || kingdom.HasGivenAlliance() || state.treasury <= 0f && state.pressure < SubmitPressure) return;
        double own = global::System.Math.Max(1d, kingdom.GetNationalPower());
        Empire best = null;
        double bestPower = 0d;
        foreach (Kingdom neighbour in Neighbours(kingdom))
        {
            Empire empire = neighbour.GetEmpire();
            if (empire == null || empire == best || !Suzerain(empire)) continue;
            double power = WarSituation.Power(empire);
            if (power < own * SubmitPowerLead || power <= bestPower) continue;
            best = empire;
            bestPower = power;
        }
        if (best == null || kingdom.isInWarWith(best.CoreKingdom) || UnityEngine.Random.value >= SubmitChance) return;
        kingdom.JoinTakenAlliance(best);
        if (kingdom.GetTakenAllianceEmpire() != best) return;
        Chronicle(string.Format(LM.Get("steppe_tribute_submit_history"), kingdom.GetKingdomName(), best.GetEmpireName()),
            kingdom, best.CoreKingdom);
        LogService.LogInfo($"{LogTag} 朝贡换赏赐：{kingdom.GetKingdomName()}(国库压力{state.treasury:0}，草原压力{state.pressure:0})" +
                           $"向{best.GetEmpireName()}(正统{best.Legitimacy}，回赐{SteppeRules.GiftShare(best.Legitimacy, DynasticCycleSystem.InCrisis(best)):0.0}倍)称臣");
    }

    // 收得了游牧朝贡、给得起赏赐的定居帝国：君主制、施行了朝贡体系、正统够、不在末世
    private static bool Suzerain(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && Alive(empire.CoreKingdom) &&
        !NomadRaidSystem.IsNomad(empire.CoreKingdom) && RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) &&
        !RepublicSystem.IsRepublic(empire) && FeudalConquestService.HasTributeInstitution(empire) &&
        empire.Legitimacy >= SubmitLegitimacy && !DynasticCycleSystem.InCrisis(empire);
}
