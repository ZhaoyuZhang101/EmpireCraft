using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class InstitutionPoliticalBalance
{
    public float Support;
    public float Opposition;
}

public sealed class InstitutionCultureRanking
{
    public string Culture = "";
    public string Line = "";
    public int Advancement;
    public int Level;
    public string LevelName = "";
    public bool Manual;
    public int AbsorbedCount;
}

public sealed class InstitutionReformEnvironment
{
    public int SameCultureCountries;
    public int SameCultureEmpires;
    public int AdvancedBorderEmpires;
    public float SpeedMultiplier = 1f;
    public int EffectiveMinimumYears = 1;
}

public enum InstitutionNodeStatus
{
    Locked,             //本线节点，前置未满足 / 被互斥 / 正统不足
    Available,          //本线节点，可以正常推动
    Forceable,          //本线节点，反对压过支持，只能强制推行
    Reforming,          //本线节点，正在改革中
    Enacted,            //本文化已掌握（自研）
    Absorbed,           //本文化已掌握（从别的线吸收而来）
    ForeignLocked,      //外来节点，等级不够、还吸收不了
    ForeignContacting,  //外来节点，正在接触积累中
    ForeignReady        //外来节点，接触度已达标，下一次年度结算就会被吸收
}

public sealed class InstitutionNodeView
{
    public InstitutionNodeConfig Node;
    public InstitutionNodeStatus Status;
    public float Support;
    public float Opposition;
    public float Exposure;
    public int ContactYears;
    public float RequiredExposure;
    public int RequiredContactYears;
    public string Reason = "";
}

// 制度系统。
//
// 关键约定（跟上一版相反）：**制度的持有者是文化，不是国家。**
//   - 文化持有已掌握的制度（CultureInstitutionState.enacted_node_ids），全局唯一一份；
//   - 帝国只持有"正在推进的这一次改革"和"本政权已经落地过哪些节点的持久效果"；
//   - 某个帝国把改革推完之后，节点写进文化，同文化的所有政权在自己的年度更新里
//     把该节点的持久效果补上（SyncSharedEffects），于是"研究成功后所有同文化的都受益"。
//
// 四条线（华夏/阿拉伯/罗马/游牧）各自自成体系，线之间不存在前置关系。别的线的制度只能靠
// "接触 → 吸收"进来：接触渠道有同帝国、陆地边境、同盟/朝贡、异文化统治的城市四种，吸收
// 条件是节点等级高于本文化当前文明等级（可选再限制领先幅度），吸收成功即直接获得该节点，
// 不要求补它在原线上的前置。
public static class InstitutionSystem
{
    private static Dictionary<string, CultureInstitutionState> _cultureStates = new(StringComparer.Ordinal);
    // 世界级的接触/吸收结算每年只跑一次，由当年第一个更新的帝国触发
    private static double _lastWorldPassTimestamp = -1d;
    // set_regime_option 配了个该政体没有的选项键时，只警告一次，避免每年刷日志
    private static readonly HashSet<string> WarnedMissingOptions = new(StringComparer.Ordinal);

    public static void ResetWorldState()
    {
        _cultureStates = new Dictionary<string, CultureInstitutionState>(StringComparer.Ordinal);
        _lastWorldPassTimestamp = -1d;
        WarnedMissingOptions.Clear();
    }

    public static void ImportCultureStates(Dictionary<string, CultureInstitutionState> states)
    {
        ResetWorldState();
        foreach (KeyValuePair<string, CultureInstitutionState> pair in states ??
                 new Dictionary<string, CultureInstitutionState>())
        {
            if (!CultureService.IsValidCulture(pair.Key) || pair.Value == null) continue;
            InstitutionStateNormalizer.Normalize(pair.Value);
            _cultureStates[pair.Key] = pair.Value;
        }
    }

    public static Dictionary<string, CultureInstitutionState> ExportCultureStates()
    {
        return _cultureStates.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    #region 文化侧：持有、等级、排行

    // 文化所属的线（字符串 id）。配了不存在的线 id 时退化成 Settings.json 里的 default_line，
    // 这样玩家自己加线/改配置写错了也不会让整个制度系统瘫掉。
    public static string GetCultureLine(string culture) =>
        InstitutionDefinitionRegistry.ResolveLine(culture.GetInstitutionLine());

    public static bool IsSameLine(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    // 反查：某个派系类别(FactionType)在这条文化科技线里到底支持/反对哪些节点——用来在派系
    // UI 里显示"这个派系跟科技树是什么关系"，而不是只能单向地从节点详情看它牵扯哪些派系。
    public static (List<InstitutionNodeConfig> supports, List<InstitutionNodeConfig> opposes)
        GetFactionInstitutionStance(string culture, FactionType factionType)
    {
        List<InstitutionNodeConfig> nodes = InstitutionDefinitionRegistry.GetForLine(GetCultureLine(culture)).ToList();
        List<InstitutionNodeConfig> supports = nodes.Where(node => node.politics.support_factions.ContainsKey(factionType)).ToList();
        List<InstitutionNodeConfig> opposes = nodes.Where(node => node.politics.oppose_factions.ContainsKey(factionType)).ToList();
        return (supports, opposes);
    }

    public static CultureInstitutionState GetOrCreateCultureState(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return null;
        if (!_cultureStates.TryGetValue(culture, out CultureInstitutionState state))
        {
            state = new CultureInstitutionState();
            _cultureStates[culture] = state;
        }
        InstitutionStateNormalizer.Normalize(state);
        // 开局即初始状态：只给本线的根节点（无效果），其余一律自己研究或从别的线吸收。
        foreach (string rootId in InstitutionDefinitionRegistry.GetRootNodes(GetCultureLine(culture)))
        {
            if (InstitutionDefinitionRegistry.Get(rootId) != null && !state.enacted_node_ids.Contains(rootId))
                state.enacted_node_ids.Add(rootId);
        }
        return state;
    }

    public static bool IsEnacted(string culture, string nodeId)
    {
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        return state != null && state.enacted_node_ids.Contains(nodeId);
    }

    public static bool IsEnacted(Empire empire, string nodeId) => IsEnacted(GetPrimaryCulture(empire), nodeId);

    #region 制度特性

    // 特性值：文化已掌握的节点里声明该特性的最大值；没有任何节点声明时返回 0。
    // 系统代码应当查询特性而不是具体节点 id，这样任何线（自研、吸收、公共模板实例）都能提供同一效果。
    public static float GetFeature(string culture, string feature)
    {
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null || string.IsNullOrWhiteSpace(feature)) return 0f;
        float result = 0f;
        foreach (string nodeId in state.enacted_node_ids)
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
            if (node != null && node.features.TryGetValue(feature, out float value) && value > result)
                result = value;
        }
        return result;
    }

    public static float GetFeature(Empire empire, string feature) => GetFeature(GetPrimaryCulture(empire), feature);

    public static bool HasFeature(string culture, string feature) => GetFeature(culture, feature) > 0f;

    public static bool HasFeature(Empire empire, string feature) => GetFeature(empire, feature) > 0f;

    // 本线里声明了某特性的节点（按等级排序）。派系诉求这类"推动某项改革"的逻辑用它找目标，
    // 而不是写死节点 id。
    public static IEnumerable<InstitutionNodeConfig> GetLineNodesWithFeature(Empire empire, string feature) =>
        InstitutionDefinitionRegistry.GetForLine(GetCultureLine(GetPrimaryCulture(empire)))
            .Where(node => !string.IsNullOrWhiteSpace(feature) && node.features.ContainsKey(feature));

    // 派系诉求要推动的改革目标：本线声明了 claim_reform:<诉求名> 特性、且本文化尚未掌握的节点。
    // 诉求代码只认诉求名，具体推动哪项制度由各线配置决定。
    public static InstitutionNodeConfig FindClaimReformTarget(Empire empire, string claim)
    {
        if (empire == null || string.IsNullOrWhiteSpace(claim)) return null;
        string culture = GetPrimaryCulture(empire);
        if (!CultureService.IsValidCulture(culture)) return null;
        return GetLineNodesWithFeature(empire, InstitutionFeatures.ClaimReformPrefix + claim)
            .FirstOrDefault(node => !IsEnacted(culture, node.id));
    }

    // 已掌握同源制度（同一个公共模板在任意线上的实例都算）
    public static bool HasEquivalentEnacted(CultureInstitutionState state, InstitutionNodeConfig node) =>
        state != null && node != null && state.enacted_node_ids.Any(id =>
            string.Equals(InstitutionDefinitionRegistry.Get(id)?.equivalence_key, node.equivalence_key,
                StringComparison.Ordinal));

    // 外来节点能否进入本文化的吸收流程：别的线的、允许吸收、本文化还没有同源制度、
    // 而且本线自己的树上也没有同源实例（本线有的就该按本线前置自己研究）。
    private static bool IsForeignCandidate(InstitutionNodeConfig node, string targetLine,
        CultureInstitutionState target)
    {
        if (node == null || IsSameLine(node.line, targetLine) || node.absorb?.enabled != true) return false;
        if (HasEquivalentEnacted(target, node)) return false;
        return !InstitutionDefinitionRegistry.GetForLine(targetLine).Any(own =>
            string.Equals(own.equivalence_key, node.equivalence_key, StringComparison.Ordinal));
    }

    #endregion


    public static Dictionary<string, int> GetCultureBranchAdvancements(string culture)
    {
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        var result = InstitutionDefinitionRegistry.Branches.ToDictionary(branch => branch, _ => 0,
            StringComparer.Ordinal);
        foreach (InstitutionNodeConfig node in state?.enacted_node_ids.Select(InstitutionDefinitionRegistry.Get)
                     .Where(node => node != null) ?? Enumerable.Empty<InstitutionNodeConfig>())
        {
            if (!result.TryGetValue(node.branch, out int current) || node.advancement > current)
                result[node.branch] = node.advancement;
        }
        return result;
    }

    // 累计制度分 = 各分支已掌握节点的最高等级之和
    public static int GetCultureAdvancement(string culture) =>
        GetCultureBranchAdvancements(culture).Values.Sum();

    // 文明等级（1~4）。这个数既上排行榜，也是吸收外来制度的判定基准。
    public static int GetCultureLevel(string culture) => GetCultureRanking(culture).Level;

    public static InstitutionCultureRanking GetCultureRanking(string culture)
    {
        int advancement = GetCultureAdvancement(culture);
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        List<InstitutionCultureLevelConfig> levels = InstitutionDefinitionRegistry.Global.culture_levels;
        InstitutionCultureLevelConfig selected = state?.manual_level > 0
            ? InstitutionRules.ResolveManualLevel(levels, state.manual_level)
            : InstitutionRules.ResolveLevel(levels, advancement);
        string levelName = LM.Get(selected.name_key);
        if (string.IsNullOrWhiteSpace(levelName) || levelName == selected.name_key)
            levelName = string.Format(LM.Get("institution_culture_level_format"), selected.level);
        return new InstitutionCultureRanking
        {
            Culture = culture,
            Line = GetCultureLine(culture),
            Advancement = advancement,
            Level = selected.level,
            LevelName = levelName,
            Manual = state?.manual_level > 0,
            AbsorbedCount = state?.absorbed_node_ids.Count ?? 0
        };
    }

    public static void SetCultureLevel(string culture, int level)
    {
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null) return;
        int previousLevel = GetCultureRanking(culture).Level;
        if (level <= 0)
        {
            state.manual_level = 0;
            int automaticLevel = GetCultureRanking(culture).Level;
            if (automaticLevel < previousLevel)
                ApplyCultureLevelRegression(culture, previousLevel, automaticLevel);
            return;
        }
        List<int> levels = InstitutionDefinitionRegistry.Global.culture_levels.Select(item => item.level)
            .Distinct().OrderBy(item => item).ToList();
        if (levels.Count == 0) return;
        state.manual_level = levels.OrderBy(item => Math.Abs(item - level)).ThenBy(item => item).First();
        int currentLevel = GetCultureRanking(culture).Level;
        if (currentLevel < previousLevel) ApplyCultureLevelRegression(culture, previousLevel, currentLevel);
    }

    public static int GetMinimumCultureLevel() =>
        InstitutionDefinitionRegistry.Global.culture_levels.Select(item => item.level).DefaultIfEmpty(1).Min();

    public static int GetMaximumCultureLevel() =>
        InstitutionDefinitionRegistry.Global.culture_levels.Select(item => item.level).DefaultIfEmpty(1).Max();

    // 只排当前世界里实际存在的文化——配置文件里登记过、但本局没有任何王国在用的文化
    // (比如没开的模组文化)不该出现在这个排行榜里。
    public static IReadOnlyList<InstitutionCultureRanking> GetCultureRankings()
    {
        return CultureService.GetActiveCultureKeys().Select(GetCultureRanking)
            .OrderByDescending(item => item.Level).ThenByDescending(item => item.Advancement)
            .ThenBy(item => item.Culture, StringComparer.Ordinal).ToList();
    }

    // 文化当前的政体形态，由它已掌握的制度反推：取"本线已掌握、且声明了政体"的节点里等级
    // 最高的那个；一个都没有就用本线根节点声明的政体——也就是**一级制度决定初始政体**，
    // 所以华夏开局是周制而不是律令制，律令要等郡县官僚（等级 5）研究出来。
    // 只看本线节点：从别的线吸收来的制度不该把本文化的政体形态也换掉。
    public static bool TryResolveCultureRegime(string culture, out RegimeType regime)
    {
        regime = default;
        if (!CultureService.IsValidCulture(culture)) return false;
        string line = GetCultureLine(culture);
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        InstitutionNodeConfig best = null;
        foreach (string nodeId in state?.enacted_node_ids ?? Enumerable.Empty<string>())
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
            if (node == null || !IsSameLine(node.line, line)) continue;
            if (!InstitutionDefinitionRegistry.TryGetNodeRegime(node, out _)) continue;
            if (best == null || node.advancement > best.advancement) best = node;
        }
        if (best != null && InstitutionDefinitionRegistry.TryGetNodeRegime(best, out regime)) return true;
        return InstitutionDefinitionRegistry.TryGetRootRegime(line, out regime);
    }

    public static string GetPrimaryCulture(Empire empire)
    {
        string culture = CompositeEmpireService.IsComposite(empire)
            ? CompositeEmpireService.GetRulingCulture(empire)
            : CultureService.GetEmpireDefaultCulture(empire);
        return CultureService.IsValidCulture(culture)
            ? culture
            : CultureService.GetRealmCulture(empire?.CoreKingdom);
    }

    #endregion

    #region 帝国侧：年度更新

    public static void Update(Empire empire)
    {
        if (empire?.data == null || empire.IsArchived() || empire.isRekt() || World.world == null ||
            empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return;
        InstitutionEmpireState state = EnsureEmpireState(empire);
        if (state == null) return;
        double now = World.world.getCurWorldTime();
        if (state.last_update_timestamp < 0)
        {
            state.last_update_timestamp = now;
            return;
        }
        if (Date.getYearsSince(state.last_update_timestamp) < 1) return;
        state.last_update_timestamp = now;

        // 世界级的接触/吸收结算每年只跑一次，谁先更新谁触发
        if (_lastWorldPassTimestamp < 0 || Date.getYearsSince(_lastWorldPassTimestamp) >= 1)
        {
            _lastWorldPassTimestamp = now;
            try
            {
                ProcessWorldContactsAndAbsorption();
            }
            catch (Exception exception)
            {
                LogService.LogError($"制度接触/吸收结算失败: {exception}");
            }
        }

        // 同文化的其他政权研究出来的制度，在这里把持久效果补到本政权身上
        SyncSharedEffects(empire);
        SyncGrantedTraits(empire);
        UpdateSocialUnrest(empire);

        if (state.active_reform != null) AdvanceReform(empire);
        else TryStartAiReform(empire);
    }

    private static InstitutionEmpireState EnsureEmpireState(Empire empire)
    {
        if (empire?.data == null) return null;
        empire.data.institution_state ??= new InstitutionEmpireState();
        InstitutionStateNormalizer.Normalize(empire.data.institution_state);
        return empire.data.institution_state;
    }

    // 老存档 / 新建帝国的兜底：确保文化状态存在并带上本线根节点。
    // 刻意不再往帝国身上塞任何"已施行"节点——施行记录只存在文化里。
    public static void MigrateEmpire(Empire empire)
    {
        if (EnsureEmpireState(empire) == null) return;
        GetOrCreateCultureState(GetPrimaryCulture(empire));
    }

    #endregion

    #region 改革流程

    public static bool CanStartReform(Empire empire, InstitutionNodeConfig node, out string reason,
        bool force = false, InstitutionPoliticalBalance balance = null)
    {
        reason = "";
        InstitutionEmpireState state = EnsureEmpireState(empire);
        if (state == null || empire.CoreKingdom == null || empire.CoreKingdom.isRekt() || node == null ||
            !InstitutionDefinitionRegistry.IsValid(node))
        {
            reason = "institution_reform_invalid";
            return false;
        }
        string culture = GetPrimaryCulture(empire);
        CultureInstitutionState cultureState = GetOrCreateCultureState(culture);
        if (cultureState == null)
        {
            reason = "institution_reform_invalid";
            return false;
        }
        // 只能研究本文化所属线上的节点；别的线的制度只能靠接触吸收
        if (!IsSameLine(node.line, GetCultureLine(culture)))
        {
            reason = "institution_reform_wrong_line";
            return false;
        }
        if (state.active_reform != null)
        {
            reason = "institution_reform_already_active";
            return false;
        }
        if (empire.data.constitutional_economy?.constitutional_reform_active == true)
        {
            reason = "institution_reform_already_active";
            return false;
        }
        if (cultureState.enacted_node_ids.Contains(node.id))
        {
            reason = "institution_reform_already_enacted";
            return false;
        }
        if (!InstitutionDefinitionRegistry.ArePrerequisitesMet(node, cultureState.enacted_node_ids.Contains))
        {
            reason = "institution_reform_missing_requirement";
            return false;
        }
        if (node.requires_composite_empire &&
            empire.data.composite_integration_stage < CompositeEmpireIntegrationStage.CompositeEmpire)
        {
            reason = "institution_reform_requires_composite_empire";
            return false;
        }
        if (node.exclusive_with.Any(cultureState.enacted_node_ids.Contains))
        {
            reason = "institution_reform_exclusive";
            return false;
        }
        if (empire.Mandate < node.research.mandate_required)
        {
            reason = "institution_reform_low_mandate";
            return false;
        }
        if (node.politics.sponsor_only.Count > 0 &&
            !node.politics.sponsor_only.Contains(
                empire.CoreKingdom.GetRegime()?.GetDominateFaction()?.Type ?? FactionType.无))
        {
            reason = "institution_reform_sponsor_required";
            return false;
        }
        if (force && !node.reform.allow_force)
        {
            reason = "institution_reform_force_disabled";
            return false;
        }
        balance ??= CalculatePoliticalBalance(empire, node);
        if (!force && balance.Support < balance.Opposition)
        {
            reason = "institution_reform_opposition_dominant";
            return false;
        }
        return true;
    }

    public static bool StartReform(Empire empire, string nodeId, bool force = false,
        string sponsorFactionId = "")
    {
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
        if (!CanStartReform(empire, node, out _, force)) return false;
        InstitutionPoliticalBalance balance = CalculatePoliticalBalance(empire, node);
        FixedFaction sponsor = ResolveReformSponsor(empire, node, sponsorFactionId);
        string culture = GetPrimaryCulture(empire);
        bool replacesVestedInterest = node.replaces.Any(id => IsEnacted(culture, id));
        float radicalism = node.resistance.base_radicalism;
        if (replacesVestedInterest) radicalism += node.resistance.vested_interest_radicalism;
        if (force) radicalism += node.reform.force_radicalism;
        empire.data.institution_state.active_reform = new InstitutionReformState
        {
            node_id = node.id,
            sponsor_faction_id = sponsor?.GetID() ?? "",
            support = balance.Support,
            opposition = balance.Opposition,
            radicalism = InstitutionRules.Clamp100(radicalism),
            stage = InstitutionReformStage.Debate,
            started_timestamp = World.world.getCurWorldTime(),
            forced = force
        };
        ApplyOppositionFactionGrowth(empire, node);
        ApplyEffects(empire, node, node.effects_on_start, false);
        string content = string.Format(LM.Get("institution_reform_started_history"), empire.GetEmpireName(),
            GetNodeName(node));
        empire.RecordHistory(directContent: content, actorId: empire.Emperor?.id ?? -1L,
            kingdomId: empire.CoreKingdom?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
        return true;
    }

    private static void AdvanceReform(Empire empire)
    {
        InstitutionReformState reform = empire.data.institution_state.active_reform;
        if (reform.resistance_war_id > 0)
        {
            War resistanceWar = World.world?.wars?.get(reform.resistance_war_id);
            if (resistanceWar != null && !resistanceWar.hasEnded()) return;
            reform.resistance_war_id = -1L;
        }
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(reform.node_id);
        string culture = GetPrimaryCulture(empire);
        if (node == null || !IsSameLine(node.line, GetCultureLine(culture)))
        {
            // 节点被配置删掉了，或者这个帝国的主文化在改革期间变了（征服/同化导致）→ 本次改革作废
            empire.data.institution_state.active_reform = null;
            return;
        }
        if (IsEnacted(culture, node.id))
        {
            // 同文化的另一个政权先推完了，或者这个节点被吸收进来了 → 本次改革直接算完成
            empire.data.institution_state.active_reform = null;
            empire.data.institution_state.last_reform_completed_timestamp = World.world.getCurWorldTime();
            SyncSharedEffects(empire);
            return;
        }
        InstitutionPoliticalBalance balance = CalculatePoliticalBalance(empire, node);
        reform.support = balance.Support;
        reform.opposition = balance.Opposition;
        bool replacesVestedInterest = node.replaces.Any(id => IsEnacted(culture, id));
        InstitutionReformEnvironment environment = GetReformEnvironment(empire, node);
        reform.progress = Math.Min(100f, reform.progress + InstitutionRules.CalculateDurationScaledAnnualProgress(
            node.reform.base_progress_per_year, balance.Support, balance.Opposition,
            environment.EffectiveMinimumYears));
        reform.radicalism = InstitutionRules.Clamp100(reform.radicalism +
            InstitutionRules.CalculateAnnualRadicalism(balance.Support, balance.Opposition,
                replacesVestedInterest, reform.forced));
        reform.stage = InstitutionRules.ResolveStage(reform.progress);
        ApplyOppositionFactionGrowth(empire, node);
        if (!reform.resistance_triggered && reform.radicalism >= node.resistance.rebellion_threshold)
        {
            reform.resistance_triggered = TryEscalateResistance(empire, node);
        }
        int elapsedYears = Math.Max(0, Date.getYearsSince(reform.started_timestamp));
        if (reform.progress >= 100f && elapsedYears >= environment.EffectiveMinimumYears)
            CompleteReform(empire, node);
    }

    public static InstitutionReformEnvironment GetReformEnvironment(Empire empire,
        InstitutionNodeConfig node = null)
    {
        int nativeMinimum = Math.Max(1, node?.reform?.minimum_years ?? 1);
        var result = new InstitutionReformEnvironment { EffectiveMinimumYears = nativeMinimum };
        InstitutionReformCompetitionConfig config = InstitutionDefinitionRegistry.Global?.reform_competition;
        if (config?.enabled != true || empire == null || World.world == null) return result;

        string culture = GetPrimaryCulture(empire);
        if (!CultureService.IsValidCulture(culture)) return result;
        result.SameCultureCountries = World.world.kingdoms.Count(kingdom =>
            kingdom != null && !kingdom.isRekt() && kingdom.data != null && kingdom.cities?.Count > 0 &&
            string.Equals(CultureService.GetRealmCulture(kingdom), culture, StringComparison.Ordinal));
        if (ModClass.EMPIRE_MANAGER != null)
        {
            result.SameCultureEmpires = ModClass.EMPIRE_MANAGER.Count(candidate =>
                candidate != null && !candidate.isRekt() && !candidate.IsArchived() &&
                candidate.CoreKingdom != null && !candidate.CoreKingdom.isRekt() &&
                string.Equals(GetPrimaryCulture(candidate), culture, StringComparison.Ordinal));
        }
        result.SameCultureCountries = Math.Max(1, result.SameCultureCountries);
        result.SameCultureEmpires = Math.Max(1, result.SameCultureEmpires);
        result.AdvancedBorderEmpires = CountAdvancedBorderEmpires(empire, culture);

        float countryBonus = Math.Min(config.country_bonus_cap,
            Math.Max(0, result.SameCultureCountries - 1) * config.country_bonus_per_additional);
        float empireBonus = Math.Min(config.empire_bonus_cap,
            Math.Max(0, result.SameCultureEmpires - 1) * config.empire_bonus_per_additional);
        float borderBonus = result.AdvancedBorderEmpires > 0 ? config.advanced_border_bonus : 0f;
        result.SpeedMultiplier = Math.Max(1f, Math.Min(config.maximum_speed_multiplier,
            1f + countryBonus + empireBonus + borderBonus));
        result.EffectiveMinimumYears = Math.Max(nativeMinimum,
            (int)Math.Ceiling(config.isolated_duration_years / result.SpeedMultiplier));
        return result;
    }

    private static int CountAdvancedBorderEmpires(Empire empire, string culture)
    {
        if (empire?.kingdoms_list == null) return 0;
        int ownLevel = GetCultureRanking(culture).Level;
        var neighbours = new HashSet<Empire>();
        foreach (Kingdom member in empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt()))
        {
            foreach (City city in (member.cities ?? Enumerable.Empty<City>()).Where(city =>
                         city != null && !city.isRekt() && city.neighbours_kingdoms != null))
            {
                foreach (Kingdom neighbour in city.neighbours_kingdoms)
                {
                    if (neighbour == null || neighbour.isRekt() || neighbour == member) continue;
                    Empire neighbourEmpire = neighbour.GetEmpire();
                    if (neighbourEmpire == null || neighbourEmpire == empire || neighbourEmpire.isRekt() ||
                        neighbourEmpire.IsArchived() || neighbourEmpire.CoreKingdom == null ||
                        neighbourEmpire.CoreKingdom.isRekt()) continue;
                    string neighbourCulture = GetPrimaryCulture(neighbourEmpire);
                    if (!CultureService.IsValidCulture(neighbourCulture) ||
                        string.Equals(neighbourCulture, culture, StringComparison.Ordinal)) continue;
                    if (GetCultureRanking(neighbourCulture).Level > ownLevel) neighbours.Add(neighbourEmpire);
                }
            }
        }
        return neighbours.Count;
    }

    private static void CompleteReform(Empire empire, InstitutionNodeConfig node)
    {
        InstitutionEmpireState state = empire.data.institution_state;
        string culture = GetPrimaryCulture(empire);
        CultureInstitutionState cultureState = GetOrCreateCultureState(culture);
        if (cultureState == null)
        {
            state.active_reform = null;
            return;
        }
        foreach (string replaced in node.replaces.Where(cultureState.enacted_node_ids.Contains).ToList())
        {
            cultureState.enacted_node_ids.Remove(replaced);
            cultureState.absorbed_node_ids.Remove(replaced);
        }
        if (!cultureState.enacted_node_ids.Contains(node.id)) cultureState.enacted_node_ids.Add(node.id);
        cultureState.enacted_timestamps ??= new Dictionary<string, double>();
        cultureState.enacted_timestamps[node.id] = World.world.getCurWorldTime();
        ApplyReformCompletionShock(empire, node);
        FactionClassSystem.ApplyInstitutionOutcome(empire, node, GetReformSponsor(empire, state.active_reform));
        // 一次性效果只作用于推完这次改革的政权，持久效果同时记进 applied 里免得同步再补一遍
        ApplyEffects(empire, node, node.effects_on_complete, false);
        if (!state.applied_node_ids.Contains(node.id)) state.applied_node_ids.Add(node.id);
        // 节点自己声明了政体形态的（比如郡县官僚 = 律令制、封建化 = 封建制），施行完成就顺势
        // 完成政体变更，不必再额外配一个 change_regime 效果 —— 两处配置也就不会互相矛盾。
        if (InstitutionDefinitionRegistry.TryGetNodeRegime(node, out RegimeType declaredRegime) &&
            empire.CoreKingdom?.GetRegime()?.type != declaredRegime)
        {
            ChangeRegime(empire, declaredRegime);
        }
        state.active_reform = null;
        state.last_reform_completed_timestamp = World.world.getCurWorldTime();
        string content = string.Format(LM.Get("institution_reform_completed_history"), empire.GetEmpireName(),
            GetNodeName(node), culture.GetCultureTranslate());
        empire.RecordHistory(directContent: content, actorId: empire.Emperor?.id ?? -1L,
            kingdomId: empire.CoreKingdom?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }

    // 反方向的补丁：有些派系诉求(比如"转天朝制度"/"转周制")会直接把政体设成某个值，完全
    // 绕过科技树——政体已经变了，但科技树里对应的节点还显示"未解锁"，看着自相矛盾。这里把
    // 这条文化线上声明了这个政体的节点、连同它的前置链，一并补记成"已施行"，让科技树跟上
    // 真实的政体状态。只补状态，不重放一次性效果(比如正统性惩罚)——那些后果已经跟着诉求
    // 本身走了，重复触发不合理；节点的共享型效果(继承法解锁等)会在下次 SyncSharedEffects
    // 时自然补上，不用在这里手动重放一遍。
    public static void SyncEnactedNodeForRegime(Empire empire, RegimeType regimeType)
    {
        if (empire == null) return;
        string culture = GetPrimaryCulture(empire);
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null) return;
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.GetForLine(GetCultureLine(culture))
            .FirstOrDefault(candidate =>
                InstitutionDefinitionRegistry.TryGetNodeRegime(candidate, out RegimeType declared) &&
                declared == regimeType);
        if (node != null) EnactNodeChain(state, node);
    }

    private static void EnactNodeChain(CultureInstitutionState state, InstitutionNodeConfig node)
    {
        if (node == null || state.enacted_node_ids.Contains(node.id)) return;
        foreach (string requiredId in node.requires)
            EnactNodeChain(state, InstitutionDefinitionRegistry.Get(requiredId));
        // "任一满足"的一组一个都没施行时，补上第一个有效的分支
        if (node.requires_any.Count > 0 && !node.requires_any.Any(state.enacted_node_ids.Contains))
            EnactNodeChain(state, node.requires_any.Select(InstitutionDefinitionRegistry.Get)
                .FirstOrDefault(candidate => candidate != null));
        state.enacted_node_ids.Add(node.id);
    }

    private static void TryStartAiReform(Empire empire)
    {
        if (!InstitutionDefinitionRegistry.Global.ai_enabled) return;
        InstitutionEmpireState state = empire.data.institution_state;
        if (state.last_ai_reform_attempt_timestamp >= 0 &&
            Date.getYearsSince(state.last_ai_reform_attempt_timestamp) < 3) return;
        state.last_ai_reform_attempt_timestamp = World.world.getCurWorldTime();

        // 全人口只扫一遍，所有候选节点共用这份阶级构成
        Dictionary<SocialClass, float> shares = BuildClassShares(empire);
        InstitutionNodeConfig selected = null;
        float bestScore = float.MinValue;
        foreach (InstitutionNodeConfig node in InstitutionDefinitionRegistry.GetForLine(
                     GetCultureLine(GetPrimaryCulture(empire))))
        {
            InstitutionPoliticalBalance balance = CalculatePoliticalBalance(empire, node, shares);
            if (!CanStartReform(empire, node, out _, false, balance)) continue;
            if (balance.Support < balance.Opposition + 5f) continue;
            float score = balance.Support - balance.Opposition + node.advancement * 2f;
            if (score <= bestScore) continue;
            bestScore = score;
            selected = node;
        }
        if (selected != null) StartReform(empire, selected.id);
    }

    #endregion

    #region 政治平衡

    // 帝国的阶级构成。全人口遍历很贵，所以抽成一次调用、多节点复用。
    public static Dictionary<SocialClass, float> BuildClassShares(Empire empire)
    {
        var counts = new Dictionary<SocialClass, int>();
        foreach (SocialClass value in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()) counts[value] = 0;
        int total = 0;
        if (empire?.kingdoms_hashset != null)
        {
            foreach (Actor actor in EmpirePopulation.Enumerate(empire.kingdoms_hashset))
            {
                SocialClass socialClass = actor.GetOrCreate().socialClass;
                if (!counts.ContainsKey(socialClass)) continue;
                counts[socialClass]++;
                total++;
            }
        }
        var shares = new Dictionary<SocialClass, float>();
        foreach (KeyValuePair<SocialClass, int> pair in counts)
            shares[pair.Key] = total > 0 ? (float)pair.Value / total : 0f;
        return shares;
    }

    public static IReadOnlyDictionary<SocialClass, float> GetClassGrievances(Empire empire)
    {
        InstitutionEmpireState state = EnsureEmpireState(empire);
        return state?.class_grievances ?? new Dictionary<SocialClass, float>();
    }

    public static string GetSocialGrievanceCause(Empire empire, SocialClass socialClass)
    {
        InstitutionEmpireState state = EnsureEmpireState(empire);
        if (state == null || !state.class_grievance_causes.TryGetValue(socialClass, out string cause) ||
            string.IsNullOrWhiteSpace(cause))
            return LM.Get("institution_social_cause_general");
        if (cause.StartsWith("regression:", StringComparison.Ordinal))
        {
            string[] parts = cause.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out int oldLevel) &&
                int.TryParse(parts[2], out int newLevel))
                return string.Format(LM.Get("institution_social_cause_regression"), oldLevel, newLevel);
        }
        if (cause == "constitution") return LM.Get("constitution_noble_grievance");
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(cause);
        return node == null
            ? LM.Get("institution_social_cause_general")
            : string.Format(LM.Get("institution_social_cause_policy"), GetNodeName(node));
    }

    private static void ApplyReformCompletionShock(Empire empire, InstitutionNodeConfig node)
    {
        InstitutionSocialUnrestConfig config = InstitutionDefinitionRegistry.Global.social_unrest;
        InstitutionEmpireState state = EnsureEmpireState(empire);
        if (config?.enabled != true || state == null || node?.politics == null) return;
        Dictionary<SocialClass, float> shares = BuildClassShares(empire);
        foreach (KeyValuePair<SocialClass, float> pair in node.politics.oppose_classes)
        {
            if (!shares.TryGetValue(pair.Key, out float share) || share <= 0f) continue;
            state.class_grievances[pair.Key] = InstitutionRules.Clamp100(
                state.class_grievances[pair.Key] + config.reform_completion_shock * pair.Value);
            state.class_grievance_causes[pair.Key] = node.id;
        }
        foreach (KeyValuePair<SocialClass, float> pair in node.politics.support_classes)
        {
            if (!shares.TryGetValue(pair.Key, out float share) || share <= 0f) continue;
            state.class_grievances[pair.Key] = InstitutionRules.Clamp100(
                state.class_grievances[pair.Key] - config.reform_completion_shock * pair.Value * 0.5f);
        }
    }

    private static void ApplyCultureLevelRegression(string culture, int previousLevel, int currentLevel)
    {
        InstitutionSocialUnrestConfig config = InstitutionDefinitionRegistry.Global.social_unrest;
        if (config?.enabled != true || previousLevel <= currentLevel || ModClass.EMPIRE_MANAGER == null) return;
        int levelsLost = Math.Max(1, previousLevel - currentLevel);
        CultureInstitutionState cultureState = GetOrCreateCultureState(culture);
        List<InstitutionNodeConfig> disruptedNodes = (cultureState?.enacted_node_ids ?? new List<string>())
            .Select(InstitutionDefinitionRegistry.Get)
            .Where(node => node != null && node.advancement > currentLevel)
            .ToList();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.Where(empire => empire != null && !empire.isRekt() &&
                     !empire.IsArchived() && string.Equals(GetPrimaryCulture(empire), culture, StringComparison.Ordinal)))
        {
            InstitutionEmpireState state = EnsureEmpireState(empire);
            if (state == null) continue;
            Dictionary<SocialClass, float> shares = BuildClassShares(empire);
            foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
            {
                if (!shares.TryGetValue(socialClass, out float share) || share <= 0f)
                {
                    state.class_grievances[socialClass] = 0f;
                    state.class_grievance_causes[socialClass] = "";
                    continue;
                }
                float lostBenefit = disruptedNodes.Sum(node =>
                    node.politics.support_classes.TryGetValue(socialClass, out float weight) ? weight : 0f);
                if (lostBenefit <= 0f) continue;
                state.class_grievances[socialClass] = InstitutionRules.Clamp100(
                    state.class_grievances[socialClass] + config.regression_shock * levelsLost * lostBenefit);
                state.class_grievance_causes[socialClass] = $"regression:{previousLevel}:{currentLevel}";
            }
            string history = string.Format(LM.Get("institution_level_regression_history"),
                empire.GetEmpireName(), previousLevel, currentLevel);
            empire.RecordHistory(directContent: history, actorId: empire.Emperor?.id ?? -1L,
                kingdomId: empire.CoreKingdom?.id ?? -1L);
            EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(history, empire.CoreKingdom);
        }
    }

    private static void UpdateSocialUnrest(Empire empire)
    {
        InstitutionSocialUnrestConfig config = InstitutionDefinitionRegistry.Global.social_unrest;
        InstitutionEmpireState state = EnsureEmpireState(empire);
        if (config?.enabled != true || state == null) return;
        CultureInstitutionState cultureState = GetOrCreateCultureState(GetPrimaryCulture(empire));
        List<InstitutionNodeConfig> enacted = (cultureState?.enacted_node_ids ?? new List<string>())
            .Select(InstitutionDefinitionRegistry.Get).Where(node => node != null).ToList();
        Dictionary<SocialClass, float> classShares = BuildClassShares(empire);
        // 制度适应期：施行越久，反对阶层越习惯，每项制度造成的怨气压力每 40 年减半
        Dictionary<string, float> adaptation = GetOppositionAdaptation(cultureState, enacted);

        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
        {
            if (!classShares.TryGetValue(socialClass, out float classShare) || classShare <= 0f)
            {
                state.class_grievances[socialClass] = 0f;
                state.class_grievance_causes[socialClass] = "";
                continue;
            }
            float opposition = 0f;
            float support = 0f;
            float strongestPressure = 0f;
            string strongestCause = "";
            foreach (InstitutionNodeConfig node in enacted)
            {
                if (node.politics.oppose_classes.TryGetValue(socialClass, out float opposeWeight))
                {
                    opposeWeight *= adaptation.TryGetValue(node.id, out float factor) ? factor : 1f;
                    opposition += opposeWeight;
                    if (opposeWeight > strongestPressure)
                    {
                        strongestPressure = opposeWeight;
                        strongestCause = node.id;
                    }
                }
                if (node.politics.support_classes.TryGetValue(socialClass, out float supportWeight))
                    support += supportWeight;
            }
            float target = InstitutionRules.Clamp100(opposition * config.opposition_target_multiplier -
                                                       support * config.support_relief_multiplier);
            float current = state.class_grievances[socialClass];
            state.class_grievances[socialClass] = InstitutionRules.Clamp100(
                current + (target - current) * config.annual_adjustment_rate);
            bool regressionShockFaded = state.class_grievance_causes[socialClass]
                                            ?.StartsWith("regression:", StringComparison.Ordinal) == true &&
                                        state.class_grievances[socialClass] <= target + 5f;
            if (!string.IsNullOrWhiteSpace(strongestCause) &&
                (regressionShockFaded || !state.class_grievance_causes[socialClass]
                     .StartsWith("regression:", StringComparison.Ordinal)))
                state.class_grievance_causes[socialClass] = strongestCause;
            else if (target <= 0.5f && state.class_grievances[socialClass] <= 1f)
                state.class_grievance_causes[socialClass] = "";
        }

        if (state.social_rebellion_war_id > 0)
        {
            War activeWar = World.world?.wars?.get(state.social_rebellion_war_id);
            if (activeWar != null && !activeWar.hasEnded()) return;
            state.social_rebellion_war_id = -1L;
        }
        if (state.last_social_rebellion_timestamp >= 0 &&
            Date.getYearsSince(state.last_social_rebellion_timestamp) < config.rebellion_cooldown_years) return;
        KeyValuePair<SocialClass, float> mostAngry = state.class_grievances
            .Where(pair => classShares.TryGetValue(pair.Key, out float share) && share >= 0.03f)
            .OrderByDescending(pair => pair.Value).FirstOrDefault();
        if (mostAngry.Value >= config.rebellion_threshold)
            TryStartSocialRebellion(empire, mostAngry.Key, mostAngry.Value);
    }

    private const float OppositionAdaptationHalfLifeYears = 40f;

    private static Dictionary<string, float> GetOppositionAdaptation(CultureInstitutionState cultureState,
        List<InstitutionNodeConfig> enacted)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        if (cultureState == null) return result;
        cultureState.enacted_timestamps ??= new Dictionary<string, double>();
        double now = World.world.getCurWorldTime();
        foreach (InstitutionNodeConfig node in enacted)
        {
            if (!cultureState.enacted_timestamps.TryGetValue(node.id, out double since))
            {
                cultureState.enacted_timestamps[node.id] = now;
                since = now;
            }
            float years = Math.Max(0f, Date.getYearsSince(since));
            result[node.id] = (float)Math.Pow(0.5d, years / OppositionAdaptationHalfLifeYears);
        }
        return result;
    }

    private static float GetKingdomClassShare(Kingdom kingdom, SocialClass socialClass)
    {
        int total = 0;
        int matching = 0;
        foreach (Actor actor in EmpirePopulation.Enumerate(new[] { kingdom }))
        {
            total++;
            if (actor.GetOrCreate().socialClass == socialClass) matching++;
        }
        return total > 0 ? (float)matching / total : 0f;
    }

    private static bool TryStartSocialRebellion(Empire empire, SocialClass socialClass, float grievance)
    {
        Dictionary<SocialClass, float> shares = BuildClassShares(empire);
        if (!shares.TryGetValue(socialClass, out float empireShare) || empireShare < 0.03f) return false;
        // 分封制帝国在施行分封类制度之前，诸国基本自治，阶层起义只在天子直辖的核心王国里爆发，
        // 不会蔓延到其他国家
        bool coreOnly = FeudalConquestService.AppliesTo(empire) &&
                        !FeudalConquestService.HasEnfeoffmentInstitution(empire);
        Kingdom rebel = coreOnly ? null : empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt() &&
                                                              kingdom != empire.CoreKingdom &&
                                                              !kingdom.IsFactionRebelling() &&
                                                              !kingdom.IsLocalRebelling() &&
                                                              !kingdom.getWars().Any())
            .OrderByDescending(kingdom => GetKingdomClassShare(kingdom, socialClass))
            .ThenByDescending(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();
        // 单一王国的大帝国也可能爆发社会革命：没有现成封国可起兵时，由受损阶层成员
        // 在非首都城市建立叛军政权，避免“只有分封帝国才会有农民起义”的反直觉结果。
        if (rebel == null)
        {
            IEnumerable<Kingdom> sourceKingdoms = coreOnly
                ? new[] { empire.CoreKingdom }
                : (IEnumerable<Kingdom>)empire.kingdoms_hashset;
            Actor leader = EmpirePopulation.Enumerate(sourceKingdoms)
                .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.hasCity() &&
                                actor.city != empire.CoreKingdom.capital &&
                                actor.GetOrCreate().socialClass == socialClass)
                .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
            City seat = leader?.city;
            if (seat != null) rebel = seat.makeOwnKingdom(leader, pRebellion: true);
        }
        if (rebel == null) return false;

        if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱)) return false;
        string className = LM.Get($"class_{socialClass}");
        rebel.data.name = string.Format(LM.Get("institution_social_rebel_kingdom_name"), className);
        War war = World.world.diplomacy.startWar(rebel, empire.CoreKingdom, WarTypeLibrary.rebellion);
        if (war == null)
        {
            rebel.EndLocalRebelling();
            return false;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        string cause = GetSocialGrievanceCause(empire, socialClass);
        war.data.name = string.Format(LM.Get("institution_social_rebellion_war_name"), className);
        WarExtension.WarExtraData warData = war.GetOrCreate();
        warData.institution_social_rebellion_empire_id = empire.id;
        warData.institution_social_rebellion_class = socialClass;
        warData.institution_social_rebellion_cause = cause;
        warData.institution_social_rebellion_grievance = grievance;
        if (socialClass == SocialClass.Peasant)
        {
            City originCity = rebel.capital;
            float landlessRatio = LandEconomySystem.GetReport(originCity).LandlessPopulationRatio;
            LandEconomySystem.MarkPeasantSocialRebellion(war, rebel, empire.CoreKingdom, originCity,
                landlessRatio, cause);
        }
        InstitutionEmpireState state = EnsureEmpireState(empire);
        state.social_rebellion_war_id = war.id;
        state.last_social_rebellion_timestamp = World.world.getCurWorldTime();

        string history = string.Format(LM.Get("institution_social_rebellion_history"), className,
            rebel.GetKingdomName(), cause, grievance);
        empire.RecordHistory(directContent: history, actorId: rebel.king?.id ?? -1L, kingdomId: rebel.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(history, empire.CoreKingdom);

        int defected = DefectCoreSoldiers(empire, rebel, socialClass, grievance);
        if (defected > 0)
        {
            string defection = string.Format(LM.Get("institution_social_rebellion_defection_history"), defected,
                className, rebel.GetKingdomName());
            empire.RecordHistory(directContent: defection, kingdomId: rebel.id);
            EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(defection, empire.CoreKingdom);
        }
        return true;
    }

    // 阶层起义的叛军在占领扩散中以该阶层当前怨气(民心)作为天命
    public static bool TryGetSocialRebelMandate(Kingdom kingdom, out int mandate)
    {
        mandate = 0;
        if (kingdom == null || kingdom.isRekt()) return false;
        foreach (War war in kingdom.getWars())
        {
            if (war == null || war.hasEnded() || !war._list_attackers.Contains(kingdom)) continue;
            WarExtension.WarExtraData data = war.GetOrCreate();
            if (data.institution_social_rebellion_empire_id < 0) continue;
            Empire empire = ModClass.EMPIRE_MANAGER?.get(data.institution_social_rebellion_empire_id);
            float grievance = data.institution_social_rebellion_grievance;
            if (empire != null && GetClassGrievances(empire)
                    .TryGetValue(data.institution_social_rebellion_class, out float current))
                grievance = current;
            mandate = (int)Math.Round(grievance);
            return true;
        }
        return false;
    }

    // 中央倒戈：起义爆发时，核心王国军中属于该阶层的士兵按怨气比例投奔叛军(怨气 75%→约 53%)，
    // 贵族、军人起义时倒戈的更多。倒戈者原地转投叛军，不再听中央调遣。
    private static int DefectCoreSoldiers(Empire empire, Kingdom rebel, SocialClass socialClass, float grievance)
    {
        Kingdom core = empire?.CoreKingdom;
        City destination = rebel?.capital;
        if (core?.units == null || destination == null || destination.isRekt()) return 0;
        float fraction = Math.Max(0.3f, Math.Min(0.7f, grievance / 100f * 0.7f));
        if (socialClass is SocialClass.Noble or SocialClass.Army) fraction = Math.Min(0.85f, fraction * 1.3f);

        List<Actor> candidates = core.units.ToList().Where(actor =>
                actor != null && !actor.isRekt() && actor.isAlive() && actor.isWarrior() && !actor.isKing() &&
                actor.GetOrCreate().socialClass == socialClass)
            .ToList();
        int count = (int)Math.Round(candidates.Count * fraction);
        if (count <= 0) return 0;
        foreach (Actor soldier in candidates.OrderBy(_ => Randy.randomFloat(0f, 1f)).Take(count))
        {
            soldier.removeFromArmy();
            if (soldier.isCityLeader()) soldier.city.removeLeader();
            soldier.joinCity(destination);
        }
        return count;
    }

    public static void ResolveSocialRebellion(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        if (warData.institution_social_rebellion_empire_id < 0) return;
        Empire empire = ModClass.EMPIRE_MANAGER?.get(warData.institution_social_rebellion_empire_id);
        InstitutionEmpireState state = EnsureEmpireState(empire);
        if (empire == null || state == null) return;
        SocialClass socialClass = warData.institution_social_rebellion_class;
        bool rebelsWon = winner == WarWinner.Attackers;
        bool centralWon = winner == WarWinner.Defenders;
        state.social_rebellion_war_id = -1L;
        float current = state.class_grievances.TryGetValue(socialClass, out float grievance) ? grievance : 0f;
        state.class_grievances[socialClass] = rebelsWon ? 18f : centralWon ? current * 0.35f : current * 0.55f;
        if (rebelsWon) empire.AddMandate(-15);

        string className = LM.Get($"class_{socialClass}");
        string key = rebelsWon
            ? "institution_social_rebellion_rebel_victory_history"
            : centralWon
                ? "institution_social_rebellion_central_victory_history"
                : "institution_social_rebellion_compromise_history";
        string history = string.Format(LM.Get(key), className,
            string.IsNullOrWhiteSpace(warData.institution_social_rebellion_cause)
                ? LM.Get("institution_social_cause_general")
                : warData.institution_social_rebellion_cause);
        empire.RecordHistory(directContent: history, actorId: war.getMainAttacker()?.king?.id ?? -1L,
            kingdomId: war.getMainAttacker()?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(history, empire.CoreKingdom);
    }

    public static InstitutionPoliticalBalance CalculatePoliticalBalance(Empire empire,
        InstitutionNodeConfig node, Dictionary<SocialClass, float> classShares = null)
    {
        var result = new InstitutionPoliticalBalance();
        if (empire == null || node?.politics == null) return result;
        classShares ??= BuildClassShares(empire);
        foreach (KeyValuePair<SocialClass, float> pair in classShares)
        {
            // 阶级项乘 300 而不是 100：人口里贵族、官僚这类关键阶级的占比通常只有个位数百分点，
            // 按 100 折算的话阶级构成在总分里几乎看不出来，全被派系占比盖住了。
            if (node.politics.support_classes.TryGetValue(pair.Key, out float support))
                result.Support += pair.Value * support * 300f;
            if (node.politics.oppose_classes.TryGetValue(pair.Key, out float oppose))
                result.Opposition += pair.Value * oppose * 300f;
        }
        foreach (FixedFaction faction in empire.CoreKingdom?.GetRegime()?.GetPlayerFactions() ??
                 Enumerable.Empty<FixedFaction>())
        {
            float share = empire.CoreKingdom.GetFactionRatioValue(faction) / 100f;
            if (node.politics.support_factions.TryGetValue(faction.Type, out float support))
                result.Support += share * support * 100f;
            if (node.politics.oppose_factions.TryGetValue(faction.Type, out float oppose))
                result.Opposition += share * oppose * 100f;
        }
        result.Support = InstitutionRules.Clamp100(result.Support);
        result.Opposition = InstitutionRules.Clamp100(result.Opposition);
        return result;
    }

    // 单独一个阶层/派系对支持度或反对度的实际贡献分——跟 CalculatePoliticalBalance 里
    // 加总的每一项公式完全一样，只是不加总、只算这一项，供 UI 直接显示"贡献了多少"，
    // 而不是显示一个抽象的配置权重让玩家自己猜这个数到底意味着什么。
    public static float GetClassContribution(Empire empire, SocialClass socialClass, float weight,
        Dictionary<SocialClass, float> classShares = null)
    {
        if (empire == null) return 0f;
        classShares ??= BuildClassShares(empire);
        float share = classShares.TryGetValue(socialClass, out float value) ? value : 0f;
        return share * weight * 300f;
    }

    public static float GetFactionContribution(Empire empire, FactionType factionType, float weight)
    {
        FixedFaction faction = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.FirstOrDefault(f => f.Type == factionType);
        if (faction == null || empire.CoreKingdom == null) return 0f;
        float share = empire.CoreKingdom.GetFactionRatioValue(faction) / 100f;
        return share * weight * 100f;
    }

    private static FixedFaction ResolveReformSponsor(Empire empire, InstitutionNodeConfig node,
        string requestedFactionId = "")
    {
        List<FixedFaction> factions = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions() ??
                                      new List<FixedFaction>();
        if (node.politics.sponsor_only.Count > 0)
            return empire?.CoreKingdom?.GetRegime()?.GetDominateFaction();
        if (!string.IsNullOrWhiteSpace(requestedFactionId))
        {
            FixedFaction requested = factions.FirstOrDefault(faction => faction?.GetID() == requestedFactionId);
            if (requested != null && node.politics.support_factions.ContainsKey(requested.Type)) return requested;
        }

        return factions.Where(faction => faction != null && !faction.Ban &&
                                          node.politics.support_factions.ContainsKey(faction.Type))
            .OrderByDescending(faction => GetFactionContribution(empire, faction.Type,
                node.politics.support_factions[faction.Type]))
            .ThenByDescending(faction => faction.CentralRatio)
            .FirstOrDefault();
    }

    public static FixedFaction GetReformSponsor(Empire empire, InstitutionReformState reform)
    {
        if (empire == null || reform == null || string.IsNullOrWhiteSpace(reform.sponsor_faction_id)) return null;
        return empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.FirstOrDefault(faction => faction?.GetID() == reform.sponsor_faction_id);
    }

    public static string GetResistanceReasonKey(InstitutionNodeConfig node)
    {
        string key = string.IsNullOrWhiteSpace(node?.resistance?.reason_key)
            ? "institution_resistance_reason_generic"
            : node.resistance.reason_key;
        // 老节点没有逐项配置理由时，按制度分支与主要反对派给出比“既得利益受损”更具体的兜底。
        if (key == "institution_resistance_reason_generic" && node != null)
        {
            if (node.branch == "finance") key = "institution_resistance_reason_tax_register";
            else if (node.branch == "military") key = "institution_resistance_reason_standing_army";
            else if (node.politics.oppose_factions.ContainsKey(FactionType.神权))
                key = "institution_resistance_reason_religious_reform";
            else if (node.politics.oppose_factions.ContainsKey(FactionType.诸侯) ||
                     node.politics.oppose_factions.ContainsKey(FactionType.自治))
                key = "institution_resistance_reason_local_privilege";
            else
            {
                string lineKey = InstitutionDefinitionRegistry.GetTree(node.line)?.default_resistance_reason_key;
                if (!string.IsNullOrWhiteSpace(lineKey)) key = lineKey;
            }
        }
        return key;
    }

    public static string GetResistanceReason(InstitutionNodeConfig node) =>
        LM.Get(GetResistanceReasonKey(node));

    private static void ApplyOppositionFactionGrowth(Empire empire, InstitutionNodeConfig node)
    {
        if (node.resistance.opposition_faction_growth <= 0) return;
        IEnumerable<FixedFaction> factions = empire.CoreKingdom?.GetRegime()?.GetPlayerFactions() ??
                                             Enumerable.Empty<FixedFaction>();
        foreach (FixedFaction faction in factions.Where(faction =>
                     node.politics.oppose_factions.ContainsKey(faction.Type)))
        {
            empire.CoreKingdom.TryIncreaseFactionRatio(faction, node.resistance.opposition_faction_growth);
        }
    }

    private static bool TryEscalateResistance(Empire empire, InstitutionNodeConfig node)
    {
        FixedFaction opposition = (empire.CoreKingdom?.GetRegime()?.GetPlayerFactions() ??
                                   Enumerable.Empty<FixedFaction>())
            .Where(faction => node.politics.oppose_factions.ContainsKey(faction.Type))
            .OrderByDescending(faction => empire.CoreKingdom.GetFactionRatioValue(faction)).FirstOrDefault();
        Kingdom rebel = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt() &&
                                                              kingdom != empire.CoreKingdom &&
                                                              !kingdom.IsFactionRebelling() &&
                                                              !kingdom.getWars().Any())
            .OrderByDescending(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();
        if (opposition == null || rebel == null) return false;
        rebel.StartFactionRebelling(opposition);
        War war = World.world.diplomacy.startWar(rebel, empire.CoreKingdom, WarTypeLibrary.normal);
        if (war == null)
        {
            rebel.EndFactionRebelling();
            return false;
        }
        war.SetEmpireWarType(EmpireWarType.派系叛乱);
        InstitutionReformState reform = empire.data.institution_state.active_reform;
        FixedFaction sponsor = GetReformSponsor(empire, reform);
        WarExtension.WarExtraData warData = war.GetOrCreate();
        warData.institution_reform_empire_id = empire.id;
        warData.institution_reform_node_id = node.id;
        warData.institution_reform_reason_key = GetResistanceReasonKey(node);
        warData.institution_reform_sponsor_faction_id = sponsor?.GetID() ?? "";
        warData.institution_reform_sponsor_name = sponsor?.Name ?? LM.Get("institution_reform_origin_ruler");
        warData.institution_reform_opposition_faction_id = opposition.GetID();
        warData.institution_reform_opposition_name = opposition.Name;
        warData.institution_reform_support = reform?.support ?? 0f;
        warData.institution_reform_opposition = reform?.opposition ?? 0f;
        warData.institution_reform_radicalism = reform?.radicalism ?? 0f;
        warData.institution_reform_stage = reform?.stage ?? InstitutionReformStage.Debate;
        warData.institution_reform_forced = reform?.forced ?? false;
        if (reform != null) reform.resistance_war_id = war.id;

        string reason = GetResistanceReason(node);
        string stage = LM.Get($"institution_stage_{warData.institution_reform_stage}");
        string forceMode = LM.Get(warData.institution_reform_forced
            ? "institution_reform_forced_marker"
            : "institution_reform_normal_marker");
        war.data.name = string.Format(LM.Get("institution_reform_rebellion_war_name"), opposition.Name,
            GetNodeName(node));
        string content = string.Format(LM.Get("institution_reform_resistance_history_detailed"), opposition.Name,
            GetNodeName(node), rebel.GetKingdomName(), reason, stage, warData.institution_reform_support,
            warData.institution_reform_opposition, warData.institution_reform_radicalism, forceMode);
        empire.RecordHistory(directContent: content, actorId: opposition.GetLeader()?.id ?? -1L,
            kingdomId: rebel.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
        return true;
    }

    public static void ResolveReformRebellion(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        if (string.IsNullOrWhiteSpace(warData.institution_reform_node_id)) return;
        Empire empire = ModClass.EMPIRE_MANAGER?.get(warData.institution_reform_empire_id);
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(warData.institution_reform_node_id);
        if (empire?.data == null || node == null) return;

        InstitutionReformState reform = empire.data.institution_state?.active_reform;
        bool sameReform = reform != null && reform.node_id == node.id;
        bool rebelsWon = winner == WarWinner.Attackers;
        bool centralWon = winner == WarWinner.Defenders;
        FixedFaction sponsor = empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.FirstOrDefault(faction => faction?.GetID() == warData.institution_reform_sponsor_faction_id);
        FixedFaction opposition = empire.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.FirstOrDefault(faction => faction?.GetID() == warData.institution_reform_opposition_faction_id);

        if (rebelsWon)
        {
            if (sameReform) empire.data.institution_state.active_reform = null;
            if (opposition != null) empire.CoreKingdom.TryIncreaseFactionRatio(opposition, 6);
            empire.AddMandate(-10);
        }
        else if (centralWon && sameReform)
        {
            reform.resistance_war_id = -1L;
            reform.radicalism = InstitutionRules.Clamp100(reform.radicalism - 25f);
            if (sponsor != null) empire.CoreKingdom.TryIncreaseFactionRatio(sponsor, 4);
        }
        else if (sameReform)
        {
            reform.resistance_war_id = -1L;
            reform.progress = Math.Max(0f, reform.progress - 10f);
            reform.radicalism = InstitutionRules.Clamp100(reform.radicalism - 10f);
        }

        string oppositionName = string.IsNullOrWhiteSpace(warData.institution_reform_opposition_name)
            ? opposition?.Name ?? LM.Get("institution_unknown_faction")
            : warData.institution_reform_opposition_name;
        string historyKey = rebelsWon
            ? "institution_reform_rebellion_rebel_victory_history"
            : centralWon
                ? "institution_reform_rebellion_central_victory_history"
                : "institution_reform_rebellion_compromise_history";
        string sponsorName = string.IsNullOrWhiteSpace(warData.institution_reform_sponsor_name)
            ? sponsor?.Name ?? LM.Get("institution_reform_origin_ruler")
            : warData.institution_reform_sponsor_name;
        string content = string.Format(LM.Get(historyKey),
            oppositionName, GetNodeName(node), sponsorName);
        empire.RecordHistory(directContent: content,
            actorId: opposition?.GetLeader()?.id ?? -1L,
            kingdomId: war.getMainAttacker()?.id ?? -1L);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }

    #endregion

    #region 跨文化接触与吸收

    // 一年一次的世界级结算：先统计文化之间的接触强度，再判定谁能吸收谁的制度。
    private static void ProcessWorldContactsAndAbsorption()
    {
        InstitutionAbsorptionRuleConfig rule = InstitutionDefinitionRegistry.Global.absorption;
        if (rule == null || !rule.enabled) return;
        Dictionary<string, Dictionary<string, float>> contacts = BuildCultureContacts(rule);
        double now = World.world.getCurWorldTime();

        foreach (KeyValuePair<string, Dictionary<string, float>> targetPair in contacts)
        {
            string targetCulture = targetPair.Key;
            CultureInstitutionState target = GetOrCreateCultureState(targetCulture);
            if (target == null) continue;
            string targetLine = GetCultureLine(targetCulture);
            int targetLevel = GetCultureLevel(targetCulture);
            var touchedThisYear = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, float> sourcePair in targetPair.Value)
            {
                CultureInstitutionState source = GetOrCreateCultureState(sourcePair.Key);
                if (source == null || IsSameLine(GetCultureLine(sourcePair.Key), targetLine)) continue;
                foreach (string nodeId in source.enacted_node_ids)
                {
                    InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
                    if (!IsForeignCandidate(node, targetLine, target)) continue;
                    // 等级不够就完全不积累接触度，这样几张接触表只会装"真的有机会吸收"的节点
                    if (!InstitutionRules.IsTierAbsorbable(node.advancement, targetLevel, rule.max_tier_gap))
                        continue;
                    if (!touchedThisYear.Add(nodeId)) continue;
                    float gained = rule.exposure_per_year * Math.Max(0f, sourcePair.Value);
                    if (gained <= 0f) continue;
                    target.exposure[nodeId] = Math.Min(100f,
                        (target.exposure.TryGetValue(nodeId, out float current) ? current : 0f) + gained);
                    target.contact_years[nodeId] =
                        (target.contact_years.TryGetValue(nodeId, out int years) ? years : 0) + 1;
                    target.last_exposure_timestamp[nodeId] = now;
                }
            }

            TryAbsorb(targetCulture, target, targetLevel, rule);
        }
    }

    // 接触表：目标文化 → 来源文化 → 本年度权重（多条渠道之间取最大值，不累加）
    private static Dictionary<string, Dictionary<string, float>> BuildCultureContacts(
        InstitutionAbsorptionRuleConfig rule)
    {
        var contacts = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);

        void Link(string from, string to, float weight)
        {
            if (weight <= 0f || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) return;
            if (!CultureService.IsValidCulture(from) || !CultureService.IsValidCulture(to)) return;
            if (!contacts.TryGetValue(to, out Dictionary<string, float> sources))
            {
                sources = new Dictionary<string, float>(StringComparer.Ordinal);
                contacts[to] = sources;
            }
            if (!sources.TryGetValue(from, out float existing) || weight > existing) sources[from] = weight;
        }

        void LinkBoth(string a, string b, float weight)
        {
            Link(a, b, weight);
            Link(b, a, weight);
        }

        // 渠道一：同属一个帝国
        if (ModClass.EMPIRE_MANAGER != null)
        {
            foreach (Empire empire in ModClass.EMPIRE_MANAGER.Where(empire =>
                         empire != null && !empire.isRekt() && !empire.IsArchived()))
            {
                var cultures = new HashSet<string>(StringComparer.Ordinal);
                string primary = GetPrimaryCulture(empire);
                if (CultureService.IsValidCulture(primary)) cultures.Add(primary);
                foreach (Kingdom kingdom in empire.kingdoms_list.Where(kingdom =>
                             kingdom != null && !kingdom.isRekt()))
                {
                    string realm = CultureService.GetRealmCulture(kingdom);
                    if (CultureService.IsValidCulture(realm)) cultures.Add(realm);
                }
                List<string> list = cultures.ToList();
                for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    LinkBoth(list[i], list[j], rule.same_empire_weight);

                // 渠道三之一：朝贡国 / 岁币国
                foreach (long kingdomId in (empire.data.taken_Kingdoms ?? new List<long>())
                         .Concat(empire.data.given_Kingdoms ?? new List<long>()))
                {
                    Kingdom tributary = World.world.kingdoms.get(kingdomId);
                    if (tributary == null || tributary.isRekt()) continue;
                    LinkBoth(primary, CultureService.GetRealmCulture(tributary),
                        rule.alliance_or_tributary_weight);
                }
            }
        }

        // 渠道二：陆地边境相邻（用城市的邻国集合，不做逐格地形判断）
        // 渠道四：城市的主流文化跟统治它的政权的法理文化不同 —— 占领融合
        var allianceGroups = new Dictionary<Alliance, HashSet<string>>();
        foreach (City city in World.world.cities.list.Where(city => city != null && !city.isRekt()))
        {
            Kingdom owner = city.kingdom;
            if (owner == null || owner.isRekt()) continue;
            string ownerCulture = CultureService.GetRealmCulture(owner);
            string cityCulture = CultureService.GetMainCulture(city);
            LinkBoth(ownerCulture, cityCulture, rule.occupied_city_weight);
            if (city.neighbours_kingdoms != null)
            {
                foreach (Kingdom neighbour in city.neighbours_kingdoms)
                {
                    if (neighbour == null || neighbour.isRekt() || neighbour == owner) continue;
                    LinkBoth(ownerCulture, CultureService.GetRealmCulture(neighbour), rule.land_border_weight);
                }
            }
            if (owner.hasAlliance())
            {
                Alliance alliance = owner.getAlliance();
                if (alliance != null)
                {
                    if (!allianceGroups.TryGetValue(alliance, out HashSet<string> members))
                    {
                        members = new HashSet<string>(StringComparer.Ordinal);
                        allianceGroups[alliance] = members;
                    }
                    if (CultureService.IsValidCulture(ownerCulture)) members.Add(ownerCulture);
                }
            }
        }

        // 渠道三之二：同盟
        foreach (HashSet<string> members in allianceGroups.Values)
        {
            List<string> list = members.ToList();
            for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                LinkBoth(list[i], list[j], rule.alliance_or_tributary_weight);
        }

        return contacts;
    }

    private static void TryAbsorb(string culture, CultureInstitutionState state, int level,
        InstitutionAbsorptionRuleConfig rule)
    {
        string line = GetCultureLine(culture);
        foreach (string nodeId in state.exposure.Keys.ToList())
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
            if (!IsForeignCandidate(node, line, state))
            {
                state.exposure.Remove(nodeId);
                state.contact_years.Remove(nodeId);
                state.last_exposure_timestamp.Remove(nodeId);
                continue;
            }
            if (!InstitutionRules.IsTierAbsorbable(node.advancement, level, rule.max_tier_gap)) continue;
            float requiredExposure = node.absorb.minimum_exposure >= 0f
                ? node.absorb.minimum_exposure
                : rule.minimum_exposure;
            int requiredYears = node.absorb.minimum_contact_years >= 0
                ? node.absorb.minimum_contact_years
                : rule.minimum_contact_years;
            float exposure = state.exposure.TryGetValue(nodeId, out float value) ? value : 0f;
            int years = state.contact_years.TryGetValue(nodeId, out int contact) ? contact : 0;
            if (!InstitutionRules.IsExposureSatisfied(exposure, years, requiredExposure, requiredYears)) continue;

            // 吸收即直接获得：不要求补它在原线上的前置（那些前置属于别的线，本线永远长不到）
            state.enacted_node_ids.Add(nodeId);
            if (!state.absorbed_node_ids.Contains(nodeId)) state.absorbed_node_ids.Add(nodeId);
            state.exposure.Remove(nodeId);
            state.contact_years.Remove(nodeId);
            state.last_exposure_timestamp.Remove(nodeId);
            string absorbed = string.Format(LM.Get("institution_absorbed_tip"),
                culture.GetCultureTranslate(), GetNodeName(node),
                LM.Get(InstitutionDefinitionRegistry.GetLineNameKey(node.line)));
            // 吸收是文化层面的事件：发一条世界消息，并记入该文化所有帝国的史书
            Kingdom messageKingdom = null;
            foreach (Empire cultureEmpire in (ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>()).ToList())
            {
                if (cultureEmpire?.data == null || cultureEmpire.IsArchived() || cultureEmpire.isRekt() ||
                    !string.Equals(GetPrimaryCulture(cultureEmpire), culture, StringComparison.Ordinal)) continue;
                cultureEmpire.RecordHistory(directContent: absorbed, kingdomId: cultureEmpire.CoreKingdom?.id ?? -1L);
                messageKingdom ??= cultureEmpire.CoreKingdom;
            }
            EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(absorbed, messageKingdom);
            // 吸收会抬高文明等级，同一年内不再连续吸收下一个，留到明年重算
            break;
        }
    }

    #endregion

    #region 效果

    // 同文化其他政权的"补效果"：文化已掌握、但本政权还没落地过的节点，把持久效果补上。
    public static void SyncSharedEffects(Empire empire)
    {
        InstitutionEmpireState state = EnsureEmpireState(empire);
        CultureInstitutionState cultureState = GetOrCreateCultureState(GetPrimaryCulture(empire));
        if (state == null || cultureState == null) return;
        foreach (string nodeId in cultureState.enacted_node_ids.ToList())
        {
            if (state.applied_node_ids.Contains(nodeId)) continue;
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
            state.applied_node_ids.Add(nodeId);
            if (node == null) continue;
            ApplyEffects(empire, node, node.effects_on_complete, true);
        }
    }

    private static void ApplyEffects(Empire empire, InstitutionNodeConfig node,
        IEnumerable<InstitutionEffectConfig> effects, bool sharedOnly)
    {
        foreach (InstitutionEffectConfig effect in effects ?? Enumerable.Empty<InstitutionEffectConfig>())
        {
            if (effect == null || string.IsNullOrWhiteSpace(effect.type)) continue;
            if (sharedOnly && !InstitutionDefinitionRegistry.IsSharedEffect(effect.type)) continue;
            try
            {
                ApplyEffect(empire, node, effect);
            }
            catch (Exception exception)
            {
                LogService.LogError($"应用制度效果失败: {node.id}/{effect.type}: {exception}");
            }
        }
    }

    // 效果的作用范围：核心王国，外加法理文化跟帝国主文化一致的成员国。
    // 不再无差别地改"帝国内所有王国"——制度既然绑在文化上，异文化的附属国就不该被顺手改掉。
    private static List<Kingdom> EnumerateAffectedKingdoms(Empire empire)
    {
        var result = new List<Kingdom>();
        if (empire?.CoreKingdom != null && !empire.CoreKingdom.isRekt()) result.Add(empire.CoreKingdom);
        string culture = GetPrimaryCulture(empire);
        foreach (Kingdom kingdom in empire?.kingdoms_list ?? new List<Kingdom>())
        {
            if (kingdom == null || kingdom.isRekt() || result.Contains(kingdom)) continue;
            if (string.Equals(CultureService.GetRealmCulture(kingdom), culture, StringComparison.Ordinal))
                result.Add(kingdom);
        }
        return result;
    }

    private static void ApplyEffect(Empire empire, InstitutionNodeConfig node, InstitutionEffectConfig effect)
    {
        switch (effect.type)
        {
            case "add_mandate":
                empire.AddMandate(effect.int_value);
                break;
            case "change_tax_level":
                if (Enum.TryParse(effect.value, true, out TaxLevel taxLevel))
                    foreach (Kingdom kingdom in EnumerateAffectedKingdoms(empire))
                        kingdom.GetRegime()?.SetTaxLevel(taxLevel);
                break;
            case "set_regime_option":
                foreach (Kingdom kingdom in EnumerateAffectedKingdoms(empire))
                {
                    Regime regime = kingdom.GetRegime();
                    if (regime?.options == null) continue;
                    if (regime.options.TryGetValue(effect.option, out int[] option) && option?.Length > 0)
                    {
                        option[0] = effect.int_value;
                        // 政体选项变了要让月度的 KingdomType/官职重算认出来，否则得等下一次自然刷新
                        kingdom.SystemChange();
                    }
                    else if (WarnedMissingOptions.Add($"{regime.type}/{effect.option}"))
                    {
                        LogService.LogWarning(
                            $"制度节点 {node.id} 要改的政体选项 {effect.option} 在 {regime.type} 政体里不存在，已跳过。");
                    }
                }
                break;
            case "modify_faction_power":
                foreach (FixedFaction faction in empire.CoreKingdom.GetRegime()?.GetPlayerFactions() ??
                         Enumerable.Empty<FixedFaction>())
                    if (effect.faction == FactionType.无 || faction.Type == effect.faction)
                        empire.CoreKingdom.TryIncreaseFactionRatio(faction, effect.int_value);
                break;
            case "enable_claim":
                EnableClaim(empire, node, effect);
                break;
            case "unlock_succession_law":
                if (Enum.TryParse(effect.value, true, out SuccessionLawType succession))
                {
                    CultureInstitutionState cultureState = GetOrCreateCultureState(GetPrimaryCulture(empire));
                    if (cultureState != null && !cultureState.unlocked_succession_laws.Contains(succession))
                        cultureState.unlocked_succession_laws.Add(succession);
                }
                break;
            case "change_regime":
                if (Enum.TryParse(effect.value, true, out RegimeType regimeType)) ChangeRegime(empire, regimeType);
                break;
            case "grant_trait":
                GrantTraitToWarriors(empire, effect.value);
                break;
            case "open_private_land_market":
                foreach (Kingdom kingdom in EnumerateAffectedKingdoms(empire))
                    LandEconomySystem.OpenPrivateLandMarket(kingdom, redistribute: false, revolutionary: false);
                break;
            case "enact_law":
                if (node.requires_composite_empire &&
                    empire.data.composite_integration_stage < CompositeEmpireIntegrationStage.CompositeEmpire)
                    break;
                if (Enum.TryParse(effect.value, true, out GeneralSystems.EmpireLaw.LawType lawType))
                {
                    foreach (Kingdom kingdom in EnumerateAffectedKingdoms(empire))
                    {
                        Regime regime = kingdom.GetRegime();
                        if (regime == null) continue;
                        regime.laws ??= new List<GeneralSystems.EmpireLaw.LawType>();
                        if (!regime.laws.Contains(lawType)) regime.laws.Add(lawType);
                        if (lawType == GeneralSystems.EmpireLaw.LawType.逃人法)
                            kingdom.GetOrCreate().fugitive_household_law_enacted = true;
                    }
                }
                break;
            default:
                LogService.LogWarning($"未知制度效果 {effect.type}，节点 {node.id} 已跳过该效果。");
                break;
        }
    }

    // 给当前存量的士兵发特质(用于"战术研究"这类节点)——只覆盖研究完成那一刻已经在当兵的人，
    // 之后新入伍的靠 SyncGrantedTraits 每年补一遍，不用改动游戏自己的入伍逻辑。
    private static void GrantTraitToWarriors(Empire empire, string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId)) return;
        foreach (Actor actor in EmpirePopulation.Enumerate(EnumerateAffectedKingdoms(empire)))
        {
            if (actor.isWarrior() && !actor.hasTrait(traitId)) actor.addTrait(traitId);
        }
    }

    // 已经解锁的 grant_trait 效果要持续覆盖新入伍的士兵，不只是研究完成那一刻的存量部队——
    // 跟着 Update 的年度节奏顺便扫一遍，找到还没有对应特质的士兵补上。
    private static void SyncGrantedTraits(Empire empire)
    {
        CultureInstitutionState cultureState = GetOrCreateCultureState(GetPrimaryCulture(empire));
        if (cultureState == null) return;
        List<string> traitIds = cultureState.enacted_node_ids
            .Select(InstitutionDefinitionRegistry.Get)
            .Where(node => node != null)
            .SelectMany(node => node.effects_on_complete)
            .Where(effect => effect != null && effect.type == "grant_trait" && !string.IsNullOrWhiteSpace(effect.value))
            .Select(effect => effect.value)
            .Distinct()
            .ToList();
        if (traitIds.Count == 0) return;
        foreach (Actor actor in EmpirePopulation.Enumerate(EnumerateAffectedKingdoms(empire)))
        {
            if (!actor.isWarrior()) continue;
            foreach (string traitId in traitIds)
                if (!actor.hasTrait(traitId)) actor.addTrait(traitId);
        }
    }

    private static void EnableClaim(Empire empire, InstitutionNodeConfig node, InstitutionEffectConfig effect)
    {
        if (!Enum.TryParse(effect.value, true, out TemporaryFactionType claim)) return;
        IEnumerable<FixedFaction> factions = empire.CoreKingdom.GetRegime()?.GetPlayerFactions() ??
                                             Enumerable.Empty<FixedFaction>();
        if (effect.faction != FactionType.无) factions = factions.Where(faction => faction.Type == effect.faction);
        else if (node.politics.support_factions.Count > 0)
            factions = factions.Where(faction => node.politics.support_factions.ContainsKey(faction.Type));
        foreach (FixedFaction faction in factions.ToList())
        {
            faction.TemporaryFactionTypesRecord ??= new List<TemporaryFactionType>();
            if (!faction.TemporaryFactionTypesRecord.Contains(claim)) faction.TemporaryFactionTypesRecord.Add(claim);
            faction.TemporaryFactions = faction.ConvertToObjectFromFactionType();
            foreach (TemporaryFaction temporary in faction.TemporaryFactions)
            {
                temporary?.Init(faction);
                temporary?.SetEmpire(empire);
            }
        }
    }

    private static void ChangeRegime(Empire empire, RegimeType regimeType)
    {
        List<Kingdom> affected = EnumerateAffectedKingdoms(empire);
        foreach (Kingdom kingdom in affected)
        {
            if (kingdom.GetRegime()?.type == regimeType) continue;
            kingdom.SetRegimeType(regimeType);
            kingdom.LoadRegime();
            kingdom.SystemChange();
        }
        empire.data.centerOffice ??= new CenterOffice();
        empire.data.centerOffice.Init(empire.CoreKingdom);

        // LoadRegime 是从模板重新克隆 regime：之前所有 set_regime_option 改过的选项、
        // enable_claim 解锁过的诉求、派系占比全都被抹回模板默认值。所以这里清掉 applied
        // 再整体重放一遍本文化已掌握节点的持久效果，把被抹掉的部分补回来。
        empire.data.institution_state.applied_node_ids.Clear();
        SyncSharedEffects(empire);
    }

    #endregion

    #region UI 取数

    // 本文化所在线的全部节点 + 每个节点当前的状态，全人口只扫一遍。
    public static IReadOnlyList<InstitutionNodeView> BuildLineView(Empire empire)
    {
        var result = new List<InstitutionNodeView>();
        if (empire?.data == null || empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return result;
        string culture = GetPrimaryCulture(empire);
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null) return result;
        InstitutionEmpireState empireState = EnsureEmpireState(empire);
        string activeNode = empireState?.active_reform?.node_id;
        Dictionary<SocialClass, float> shares = BuildClassShares(empire);

        foreach (InstitutionNodeConfig node in InstitutionDefinitionRegistry.GetForLine(GetCultureLine(culture)))
        {
            InstitutionPoliticalBalance balance = CalculatePoliticalBalance(empire, node, shares);
            var view = new InstitutionNodeView
            {
                Node = node,
                Support = balance.Support,
                Opposition = balance.Opposition
            };
            if (state.enacted_node_ids.Contains(node.id))
                view.Status = state.absorbed_node_ids.Contains(node.id)
                    ? InstitutionNodeStatus.Absorbed
                    : InstitutionNodeStatus.Enacted;
            else if (node.id == activeNode)
                view.Status = InstitutionNodeStatus.Reforming;
            else if (CanStartReform(empire, node, out string reason, false, balance))
                view.Status = InstitutionNodeStatus.Available;
            else if (CanStartReform(empire, node, out _, true, balance))
            {
                view.Status = InstitutionNodeStatus.Forceable;
                view.Reason = reason;
            }
            else
            {
                view.Status = InstitutionNodeStatus.Locked;
                view.Reason = reason;
            }
            result.Add(view);
        }
        return result;
    }

    // 外来的可吸收 / 正在接触的节点（挂在树的旁边显示）
    public static IReadOnlyList<InstitutionNodeView> BuildForeignView(Empire empire) =>
        empire?.data == null
            ? new List<InstitutionNodeView>()
            : BuildCultureForeignView(GetPrimaryCulture(empire));

    // 文化本身（不针对某个帝国）的整条科技线：只看文化掌握了什么、前置是否满足、
    // 有没有同文化帝国正在推进。没有帝国上下文，所以不计算支持/反对。
    public static IReadOnlyList<InstitutionNodeView> BuildCultureLineView(string culture)
    {
        var result = new List<InstitutionNodeView>();
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null) return result;
        var reforming = new HashSet<string>(StringComparer.Ordinal);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
        {
            if (empire?.data == null || empire.IsArchived() || empire.isRekt()) continue;
            string nodeId = empire.data.institution_state?.active_reform?.node_id;
            if (!string.IsNullOrEmpty(nodeId) &&
                string.Equals(GetPrimaryCulture(empire), culture, StringComparison.Ordinal))
                reforming.Add(nodeId);
        }
        foreach (InstitutionNodeConfig node in InstitutionDefinitionRegistry.GetForLine(GetCultureLine(culture)))
        {
            var view = new InstitutionNodeView { Node = node };
            if (state.enacted_node_ids.Contains(node.id))
                view.Status = state.absorbed_node_ids.Contains(node.id)
                    ? InstitutionNodeStatus.Absorbed
                    : InstitutionNodeStatus.Enacted;
            else if (reforming.Contains(node.id))
                view.Status = InstitutionNodeStatus.Reforming;
            else if (!InstitutionDefinitionRegistry.ArePrerequisitesMet(node, state.enacted_node_ids.Contains))
            {
                view.Status = InstitutionNodeStatus.Locked;
                view.Reason = "institution_reform_missing_requirement";
            }
            else if (node.exclusive_with.Any(state.enacted_node_ids.Contains))
            {
                view.Status = InstitutionNodeStatus.Locked;
                view.Reason = "institution_reform_exclusive";
            }
            else view.Status = InstitutionNodeStatus.Available;
            result.Add(view);
        }
        return result;
    }

    public static IReadOnlyList<InstitutionNodeView> BuildCultureForeignView(string culture)
    {
        var result = new List<InstitutionNodeView>();
        InstitutionAbsorptionRuleConfig rule = InstitutionDefinitionRegistry.Global.absorption;
        if (rule == null || !rule.enabled) return result;
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null) return result;
        string line = GetCultureLine(culture);
        int level = GetCultureLevel(culture);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string nodeId in state.enacted_node_ids.Where(state.absorbed_node_ids.Contains))
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
            if (node == null || !seen.Add(nodeId)) continue;
            result.Add(new InstitutionNodeView { Node = node, Status = InstitutionNodeStatus.Absorbed });
        }
        foreach (string nodeId in state.exposure.Keys)
        {
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(nodeId);
            if (node == null || IsSameLine(node.line, line) || !seen.Add(nodeId)) continue;
            float exposure = state.exposure.TryGetValue(nodeId, out float value) ? value : 0f;
            int years = state.contact_years.TryGetValue(nodeId, out int contact) ? contact : 0;
            float requiredExposure = node.absorb.minimum_exposure >= 0f
                ? node.absorb.minimum_exposure
                : rule.minimum_exposure;
            int requiredYears = node.absorb.minimum_contact_years >= 0
                ? node.absorb.minimum_contact_years
                : rule.minimum_contact_years;
            result.Add(new InstitutionNodeView
            {
                Node = node,
                Exposure = exposure,
                ContactYears = years,
                RequiredExposure = requiredExposure,
                RequiredContactYears = requiredYears,
                Status = !InstitutionRules.IsTierAbsorbable(node.advancement, level, rule.max_tier_gap)
                    ? InstitutionNodeStatus.ForeignLocked
                    : InstitutionRules.IsExposureSatisfied(exposure, years, requiredExposure, requiredYears)
                        ? InstitutionNodeStatus.ForeignReady
                        : InstitutionNodeStatus.ForeignContacting
            });
        }
        return result.OrderBy(view => view.Node.line, StringComparer.Ordinal).ThenBy(view => view.Node.advancement)
            .ThenBy(view => view.Node.id, StringComparer.Ordinal).ToList();
    }

    public static string GetNodeName(InstitutionNodeConfig node)
    {
        if (node == null) return LM.Get("label_none");
        string value = LM.Get(node.name_key);
        return string.IsNullOrWhiteSpace(value) || value == node.name_key ? node.id : value;
    }

    #endregion
}
