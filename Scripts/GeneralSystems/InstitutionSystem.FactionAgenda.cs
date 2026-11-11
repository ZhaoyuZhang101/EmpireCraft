using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 派系的制度倾向与回退(用户 2026-11-11 "这里的诉求建议可以加上文化制度节点，派系可以有自己倾向的文化制度节点，议题就是优先走这个节点"
// "或者回退，但是回退阻力应当巨大""推制度节点和派系个人议题可以并行")：
//   · 每个派系有一个倾向推行的节点：神力固定的(FixedFaction.PreferredInstitution)优先，否则在本文化制度线上本派支持、尚未施行的节点里
//     挑支持权重最高的(前置齐的在前，同权重先推基础的)。AI 挑改革时按派系占比从大到小，先走各派倾向的节点(TryStartFactionPreference)；
//   · 回退：本派反对、已施行的一项制度(只退末端：没有别的已施行制度以它为前置，不退定政体与理念车道的)，走同一条改革线(每国同时一项)，
//     和朝堂的奏议并行。阻力：拥护它的一方按一倍半算，再加祖制阻力(施行越久越大)；要支持达到反对的两倍才发得起，进度减半、最短年限加倍、
//     激进度涨得快，过了起事线当场作罢；推完撤下这项(它取代的旧制度恢复)，正统 -5
public static partial class InstitutionSystem
{
    private const float RevertSupportRatio = 2f;
    private const float RevertTraditionBase = 20f;
    private const float RevertTraditionPerYear = 0.5f;
    private const float RevertTraditionMax = 45f;
    private const float RevertSupporterScale = 1.5f;
    private const float RevertRadicalismBase = 25f;
    private const float RevertRadicalismScale = 1.5f;
    private const float RevertProgressScale = 0.5f;
    private const int RevertDurationScale = 2;
    private const int RevertAuthorityExtra = 20;
    private const int RevertMandateCost = 5;
    // AI：占比至少这么多的派系才按倾向挑改革；回退要更大的派系
    private const int PreferenceMinRatio = 10;
    private const int RevertMinRatio = 30;

    public sealed class RevertBalanceView
    {
        public float Support;
        public float Opposition;
        public float Tradition;
        public float Needed => Opposition * RevertSupportRatio;
    }

    #region 倾向

    // 本派倾向推行的节点，按先后：神力固定的、前置齐的、支持权重高的、基础的
    public static List<InstitutionNodeConfig> PreferenceOrder(InstitutionRealm realm, FixedFaction faction)
    {
        var result = new List<InstitutionNodeConfig>();
        if (realm == null || faction == null) return result;
        string culture = realm.Culture;
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null) return result;
        string line = GetCultureLine(culture);
        return InstitutionDefinitionRegistry.GetForLine(line)
            .Where(node => node.politics.support_factions.TryGetValue(faction.Type, out float weight) && weight > 0f &&
                           !state.enacted_node_ids.Contains(node.id) && !IsSuperseded(state, node.id) &&
                           InstitutionDefinitionRegistry.FitsCulture(node, culture))
            .OrderByDescending(node => node.id == faction.PreferredInstitution)
            .ThenByDescending(node => InstitutionDefinitionRegistry.ArePrerequisitesMet(node, id => IsPrerequisiteHeld(state, id)))
            .ThenByDescending(node => node.politics.support_factions[faction.Type])
            .ThenBy(node => node.advancement)
            .ToList();
    }

    public static InstitutionNodeConfig PreferredNode(InstitutionRealm realm, FixedFaction faction) =>
        PreferenceOrder(realm, faction).FirstOrDefault();

    public static InstitutionNodeConfig PreferredNode(Empire empire, FixedFaction faction) =>
        empire?.CoreKingdom == null ? null : PreferredNode(InstitutionRealm.Of(empire), faction);

    // 本派想回退的：神力固定的优先，否则本派反对权重最高、可回退的已施行制度
    public static InstitutionNodeConfig RevertTarget(InstitutionRealm realm, FixedFaction faction)
    {
        if (realm == null || faction == null) return null;
        string culture = realm.Culture;
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null) return null;
        return state.enacted_node_ids.Select(InstitutionDefinitionRegistry.Get)
            .Where(node => node != null && node.politics.oppose_factions.TryGetValue(faction.Type, out float weight) && weight > 0f &&
                           IsRevertable(culture, node, out _))
            .OrderByDescending(node => node.id == faction.RevertInstitution)
            .ThenByDescending(node => node.politics.oppose_factions[faction.Type])
            .FirstOrDefault();
    }

    public static InstitutionNodeConfig RevertTarget(Empire empire, FixedFaction faction) =>
        empire?.CoreKingdom == null ? null : RevertTarget(InstitutionRealm.Of(empire), faction);

    // 改革线空着时，派系按占比从大到小：先看要不要回退，再走本派倾向的节点(candidates 是这一轮能推、支持也够的)
    private static bool TryStartFactionPreference(InstitutionRealm realm,
        List<(InstitutionNodeConfig node, float score)> candidates, Dictionary<SocialClass, float> shares)
    {
        List<FixedFaction> factions = realm?.Core?.GetRegime()?.GetPlayerFactions()?
            .Where(faction => faction != null && !faction.Ban).OrderByDescending(faction => RatioOf(realm, faction)).ToList();
        if (factions == null || factions.Count == 0) return false;
        var ready = new HashSet<string>(candidates.Select(entry => entry.node.id));
        foreach (FixedFaction faction in factions)
        {
            int ratio = RatioOf(realm, faction);
            if (ratio < PreferenceMinRatio) break;
            if (ratio >= RevertMinRatio && RevertTarget(realm, faction) is InstitutionNodeConfig revert &&
                CanStartRevert(realm, revert, out _, false, RevertBalance(realm, revert, shares)))
                return StartRevert(realm, revert.id, false, faction.GetID());
            InstitutionNodeConfig push = PreferenceOrder(realm, faction).FirstOrDefault(node => ready.Contains(node.id));
            if (push != null) return StartReform(realm, push.id, false, faction.GetID());
        }
        return false;
    }

    #endregion

    #region 回退

    // 只退末端：别的已施行制度以它为前置的不退(要退得先退后面的)；定政体的、理念车道的、外线吸收来的不走这条路
    public static bool IsRevertable(string culture, InstitutionNodeConfig node, out string reason)
    {
        reason = "";
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null || node == null || !state.enacted_node_ids.Contains(node.id))
        {
            reason = "institution_revert_not_enacted";
            return false;
        }
        if (!IsSameLine(node.line, GetCultureLine(culture)))
        {
            reason = "institution_revert_foreign";
            return false;
        }
        if (IdeologyInstitutionPaths.TryGetIdeology(node.branch, out _))
        {
            reason = "institution_revert_ideology";
            return false;
        }
        if (InstitutionDefinitionRegistry.TryGetNodeRegime(node, out _))
        {
            reason = "institution_revert_regime";
            return false;
        }
        foreach (string other in state.enacted_node_ids)
        {
            InstitutionNodeConfig dependent = InstitutionDefinitionRegistry.Get(other);
            if (dependent == null || dependent.id == node.id) continue;
            bool needs = dependent.requires.Contains(node.id) ||
                         dependent.requires_any.Contains(node.id) &&
                         !dependent.requires_any.Any(alternative => alternative != node.id && state.enacted_node_ids.Contains(alternative));
            if (!needs) continue;
            reason = "institution_revert_has_dependents";
            return false;
        }
        return true;
    }

    // 回退的天平：想废它的(原本反对它的阶层与派系)对拥护它的(原本支持者 ×1.5，连同时代潮流) + 祖制阻力
    public static RevertBalanceView RevertBalance(InstitutionRealm realm, InstitutionNodeConfig node,
        Dictionary<SocialClass, float> shares = null)
    {
        var view = new RevertBalanceView();
        if (realm == null || node == null) return view;
        InstitutionPoliticalBalance balance = CalculatePoliticalBalance(realm, node, shares);
        view.Tradition = TraditionResistance(realm.Culture, node);
        view.Support = InstitutionRules.Clamp100(balance.Opposition);
        view.Opposition = InstitutionRules.Clamp100(balance.Support * RevertSupporterScale + view.Tradition);
        return view;
    }

    public static RevertBalanceView RevertBalance(Empire empire, InstitutionNodeConfig node) =>
        empire?.CoreKingdom == null ? new RevertBalanceView() : RevertBalance(InstitutionRealm.Of(empire), node);

    // 祖制阻力：施行越久越难改(20 起，每年 +0.5，至多 45)
    private static float TraditionResistance(string culture, InstitutionNodeConfig node)
    {
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        float years = state?.enacted_timestamps != null && state.enacted_timestamps.TryGetValue(node.id, out double at)
            ? Math.Max(0, Date.getYearsSince(at)) : 0f;
        return Math.Min(RevertTraditionMax, RevertTraditionBase + years * RevertTraditionPerYear);
    }

    public static bool CanStartRevert(InstitutionRealm realm, InstitutionNodeConfig node, out string reason,
        bool force = false, RevertBalanceView balance = null)
    {
        reason = "";
        InstitutionEmpireState state = EnsureRealmState(realm);
        if (state == null || realm.Core == null || realm.Core.isRekt() || node == null)
        {
            reason = "institution_reform_invalid";
            return false;
        }
        if (state.active_reform != null || realm.IsEmpire && realm.Empire.data.constitutional_economy?.constitutional_reform_active == true)
        {
            reason = "institution_reform_already_active";
            return false;
        }
        if (!IsRevertable(realm.Culture, node, out reason)) return false;
        if (realm.Authority < node.research.mandate_required + RevertAuthorityExtra)
        {
            reason = "institution_revert_low_authority";
            return false;
        }
        if (force && !node.reform.allow_force)
        {
            reason = "institution_reform_force_disabled";
            return false;
        }
        balance ??= RevertBalance(realm, node);
        if (!force && balance.Support < balance.Needed)
        {
            reason = "institution_revert_opposition";
            return false;
        }
        return true;
    }

    public static bool CanStartRevert(Empire empire, InstitutionNodeConfig node, out string reason, bool force = false) =>
        CanStartRevert(InstitutionRealm.Of(empire), node, out reason, force);

    public static bool StartRevert(Empire empire, string nodeId, bool force = false, string sponsorFactionId = "") =>
        StartRevert(InstitutionRealm.Of(empire), nodeId, force, sponsorFactionId);

    public static bool StartRevert(InstitutionRealm realm, string nodeId, bool force = false, string sponsorFactionId = "")
    {
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
        RevertBalanceView balance = RevertBalance(realm, node);
        if (!CanStartRevert(realm, node, out _, force, balance)) return false;
        List<FixedFaction> factions = realm.Core?.GetRegime()?.GetPlayerFactions() ?? new List<FixedFaction>();
        FixedFaction sponsor = factions.FirstOrDefault(faction => faction?.GetID() == sponsorFactionId) ??
                               factions.Where(faction => faction != null && !faction.Ban && node.politics.oppose_factions.ContainsKey(faction.Type))
                                   .OrderByDescending(faction => RatioOf(realm, faction)).FirstOrDefault();
        EnsureRealmState(realm).active_reform = new InstitutionReformState
        {
            node_id = node.id,
            sponsor_faction_id = sponsor?.GetID() ?? "",
            support = balance.Support,
            opposition = balance.Opposition,
            radicalism = InstitutionRules.Clamp100(RevertRadicalismBase + (force ? node.reform.force_radicalism : 0f)),
            stage = InstitutionReformStage.Debate,
            started_timestamp = World.world.getCurWorldTime(),
            forced = force,
            revert = true
        };
        string content = string.Format(LM.Get("institution_revert_started_history"), realm.Name, GetNodeName(node, realm.Culture),
            sponsor?.Name ?? LM.Get("institution_reform_origin_ruler"));
        realm.Record(content, actorId: realm.Ruler?.id ?? -1L, kingdomId: realm.Core?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, realm.Core);
        return true;
    }

    // 回退的年度推进(见 AdvanceReform)：进度减半、最短年限加倍、激进度涨一倍半；过了起事线作罢
    private static void AdvanceRevert(InstitutionRealm realm, InstitutionReformState reform, InstitutionNodeConfig node,
        Dictionary<SocialClass, float> shares, float years)
    {
        InstitutionEmpireState state = EnsureRealmState(realm);
        string culture = realm.Culture;
        if (!IsEnacted(culture, node.id))
        {
            // 同文化别的政权先退掉了
            state.active_reform = null;
            return;
        }
        RevertBalanceView balance = RevertBalance(realm, node, shares);
        reform.support = balance.Support;
        reform.opposition = balance.Opposition;
        int minimumYears = Math.Max(2, GetReformEnvironment(realm, node).EffectiveMinimumYears * RevertDurationScale);
        float advance = InstitutionRules.CalculateDurationScaledAnnualProgress(node.reform.base_progress_per_year * RevertProgressScale,
            balance.Support, balance.Opposition, minimumYears) * years;
        reform.progress = Math.Min(100f, reform.progress + advance);
        reform.radicalism = InstitutionRules.Clamp100(reform.radicalism +
            InstitutionRules.CalculateAnnualRadicalism(balance.Support, balance.Opposition, true, reform.forced) * RevertRadicalismScale * years);
        reform.stage = InstitutionRules.ResolveStage(reform.progress);
        if (reform.radicalism >= node.resistance.rebellion_threshold)
        {
            CollapseRevert(realm, node, reform);
            return;
        }
        if (reform.progress >= 100f && Date.getYearsSince(reform.started_timestamp) >= minimumYears)
            CompleteRevert(realm, node);
    }

    // 众怒难犯：回退作罢，倡议的派系在朝堂上折了本钱
    private static void CollapseRevert(InstitutionRealm realm, InstitutionNodeConfig node, InstitutionReformState reform)
    {
        EnsureRealmState(realm).active_reform = null;
        FixedFaction sponsor = GetReformSponsor(realm, reform);
        if (realm.IsEmpire && sponsor != null && CourtSystem.Applies(realm.Empire))
        {
            CourtSystem.AdjustFaction(realm.Empire, sponsor, "capital", -20f);
            CourtSystem.AdjustFaction(realm.Empire, sponsor, "favor", -10f);
        }
        string content = string.Format(LM.Get("institution_revert_collapsed_history"), realm.Name, GetNodeName(node, realm.Culture),
            sponsor?.Name ?? LM.Get("institution_reform_origin_ruler"));
        realm.Record(content, actorId: realm.Ruler?.id ?? -1L, kingdomId: realm.Core?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, realm.Core);
    }

    // 推完：撤下这项(它取代的旧制度恢复，同文化各政权一并同步)，正统 -5，拥护它的阶层怨气上涨
    private static void CompleteRevert(InstitutionRealm realm, InstitutionNodeConfig node)
    {
        InstitutionEmpireState state = EnsureRealmState(realm);
        string culture = realm.Culture;
        state.active_reform = null;
        ForceRevokeNodes(culture, new[] { node.id }, false);
        state.last_reform_completed_timestamp = World.world.getCurWorldTime();
        if (realm.IsEmpire)
        {
            realm.Empire.AddMandate(-RevertMandateCost);
            foreach (SocialClass socialClass in node.politics.support_classes.Keys)
                ClaimPolicySystem.AddGrievance(realm.Empire, socialClass, 6f);
        }
        string content = string.Format(LM.Get("institution_revert_completed_history"), realm.Name, GetNodeName(node, culture),
            culture.GetCultureTranslate());
        realm.Record(content, actorId: realm.Ruler?.id ?? -1L, kingdomId: realm.Core?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, realm.Core);
    }

    // 显示用：回退的改革写成"回退「某制」"
    public static string ReformName(Empire empire, InstitutionReformState reform)
    {
        if (reform == null) return LM.Get("label_none");
        string name = GetNodeName(empire, InstitutionDefinitionRegistry.Get(reform.node_id));
        return reform.revert ? string.Format(LM.Get("institution_revert_label"), name) : name;
    }

    public static string ReformName(InstitutionReformState reform, string culture)
    {
        if (reform == null) return LM.Get("label_none");
        string name = GetNodeName(InstitutionDefinitionRegistry.Get(reform.node_id), culture);
        return reform.revert ? string.Format(LM.Get("institution_revert_label"), name) : name;
    }

    #endregion

    #region 强制推进(用户 2026-11-11 "如果有正在推的议题也应该允许玩家强制推进")

    public const float GodReformStep = 25f;

    // 神力推进改革线上眼下这一项(推行或回退)：加 amount 进度；推满或 complete 时当场完成，不等最短年限
    public static bool GodAdvanceReform(InstitutionRealm realm, float amount, bool complete)
    {
        InstitutionEmpireState state = EnsureRealmState(realm);
        InstitutionReformState reform = state?.active_reform;
        InstitutionNodeConfig node = reform == null ? null : InstitutionDefinitionRegistry.Get(reform.node_id);
        if (node == null) return false;
        reform.progress = Math.Min(100f, reform.progress + Math.Max(0f, amount));
        reform.stage = InstitutionRules.ResolveStage(reform.progress);
        if (!complete && reform.progress < 100f) return true;
        if (reform.revert)
        {
            if (IsEnacted(realm.Culture, node.id)) CompleteRevert(realm, node);
            else state.active_reform = null;
        }
        else CompleteReform(realm, node);
        return true;
    }

    #endregion
}
