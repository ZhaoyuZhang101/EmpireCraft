using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 王国推制度(简化朝堂)。以前只有帝国推制度，部落时代只好把每个独立部落都建成帝国：
//   · 独立王国、部落(见 InstitutionRealm.IsSovereignKingdom)每年随城市钩子的年度扫描推一次(登记为"制度·王国")，
//     分帧排队，每帧至多 KingdomScanBudgetMs 毫秒；
//   · 朝堂从简：没有奏议、内阁与诉求，君主一人提议定夺(AI 用 TryStartAiReform)；倡议者取支持派里占比最高的，没有就是君主；
//   · 支持度与帝国同一套(CalculatePoliticalBalance)：阶层按本国人口，派系按本国占比；占比全为零时按阶层势力播种(合计六成)，
//     称帝后照旧沿用；
//   · 君威代替正统(见 RealmAuthority)；门槛同帝国：支持不少于反对，AI 不强推；
//   · 阻力满了不起兵(王国没有可起兵的成员国)：改革作罢、反对派 +6、君主声望 −20；
//   · 规则一：本文化有帝国时，王国不推改政体、废君主的制度——那要帝国来推；部落时代没有帝国，哪个部落都能推部落联合之类；
//   · AI：与帝国同样的间隔，按国家编号错开年份；叛乱未平不推；同文化别的政权在推的不重复推；每个文化至多三个王国同时在推；
//   · 称帝时进行中的改革带进帝国(落地记录不带)，帝国解散时交回核心王国。
public static partial class InstitutionSystem
{
    // 总开关(模组设置，见 ConfigActions.KingdomInstitutionReformsCallBack)
    public static bool KingdomReformsEnabled => ModClass.KINGDOM_INSTITUTION_REFORMS;

    private const long KingdomScanBudgetMs = 3;
    private const int SeededFactionRatio = 60;
    private const int CollapseOppositionGain = 6;
    private const int CollapseRenownLoss = 20;
    private const int MaxKingdomReformsPerCulture = 3;

    private static readonly Queue<Kingdom> KingdomScanQueue = new();
    private static object _kingdomScanWorld;
    private static double _lastKingdomScan = -1d;

    #region 年度扫描

    // 到期后把国家排成队，每次至多用 KingdomScanBudgetMs 毫秒，没算完的下一轮接着算(仿 HarshRuleSystem.TryYearlyKingdomScan)
    public static void TryYearlyKingdomScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_kingdomScanWorld, World.world) || now < _lastKingdomScan)
        {
            _kingdomScanWorld = World.world;
            _lastKingdomScan = now;
            KingdomScanQueue.Clear();
            return;
        }
        if (KingdomScanQueue.Count == 0)
        {
            if (Date.getYearsSince(_lastKingdomScan) < 1) return;
            _lastKingdomScan = now;
            if (!KingdomReformsEnabled) return;
            foreach (Kingdom kingdom in World.world.kingdoms.list.ToList()) KingdomScanQueue.Enqueue(kingdom);
        }
        var budget = global::System.Diagnostics.Stopwatch.StartNew();
        while (KingdomScanQueue.Count > 0 && budget.ElapsedMilliseconds < KingdomScanBudgetMs)
        {
            Kingdom kingdom = KingdomScanQueue.Dequeue();
            if (kingdom?.data == null || kingdom.isRekt() || !KingdomReformsEnabled) continue;
            try
            {
                UpdateKingdom(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 王国制度结算失败({kingdom.data?.name}): {exception.Message}");
            }
        }
    }

    private static void UpdateKingdom(Kingdom kingdom)
    {
        InstitutionEmpireState existing = kingdom.GetOrCreate().institution_state;
        // 入了帝国、成了附庸、归了部落共主：不再自己推制度，进行中的改革作罢
        if (!InstitutionRealm.IsSovereignKingdom(kingdom))
        {
            if (existing?.active_reform != null)
                DropKingdomReform(kingdom, existing, CultureService.GetRealmCulture(kingdom), "institution_realm_not_sovereign");
            return;
        }
        InstitutionRealm realm = InstitutionRealm.Of(kingdom);
        string culture = realm.Culture;
        CultureInstitutionState cultureState = GetOrCreateCultureState(culture);
        InstitutionEmpireState state = EnsureRealmState(realm);
        if (cultureState == null || state == null) return;
        double now = World.world.getCurWorldTime();
        if (state.last_update_timestamp < 0)
        {
            // 第一次：本文化当前已施行的记作已落地，只作底，不重放效果(这些制度本国早就在用)
            state.applied_node_ids = new List<string>(cultureState.enacted_node_ids);
            state.last_update_timestamp = now;
            return;
        }
        // 一年一扫；分帧排队的先后不一，用半年防同一年里扫两遍
        if (Date.getMonthsSince(state.last_update_timestamp) < 6) return;
        state.last_update_timestamp = now;

        Dictionary<SocialClass, float> shares = BuildClassShares(realm);
        TickKingdomCourt(realm, shares);
        SyncSharedEffects(realm);
        SyncGrantedTraits(realm);
        if (state.active_reform != null &&
            InstitutionDefinitionRegistry.Get(state.active_reform.node_id) is InstitutionNodeConfig active &&
            ChangesRegime(active) && HasCultureEmpire(culture))
        {
            // 推到一半本文化出了帝国：改政体的事交给帝国(规则一)
            DropKingdomReform(kingdom, state, culture, "institution_reform_requires_paramount");
            return;
        }
        if (state.active_reform != null) AdvanceReform(realm, shares);
        else TryStartAiReform(realm, shares);
    }

    // 简化朝堂的年度结算：阶层支持演变、派系占比对上本国政体的派系；全零时按阶层势力播种
    private static void TickKingdomCourt(InstitutionRealm realm, Dictionary<SocialClass, float> shares)
    {
        Kingdom kingdom = realm.Kingdom;
        List<FixedFaction> factions = kingdom.GetRegime()?.GetPlayerFactions()?.Where(faction => faction != null).ToList();
        if (factions == null || factions.Count == 0) return;
        FactionClassSystem.EnsureRealmProfiles(realm, factions, shares);
        FactionClassSystem.UpdateRealm(realm, factions, shares);
        kingdom.ReconcileFactionRatios(factions);
        if (kingdom.GetFactionRatioTotal() <= 0) SeedFactionRatios(kingdom, factions);
    }

    // 王国派系占比第一次全为零：按各派系的阶层势力(ClassPower)分掉六成，余下四成留给以后的消长
    private static void SeedFactionRatios(Kingdom kingdom, List<FixedFaction> factions)
    {
        List<FixedFaction> active = factions.Where(faction => !faction.Ban).ToList();
        if (active.Count == 0) return;
        Dictionary<FixedFaction, int> ratios = kingdom.GetOrCreate().FactionRatio;
        if (ratios == null) return;
        float power = active.Sum(faction => Math.Max(0f, faction.ClassPower));
        int assigned = 0;
        foreach (FixedFaction faction in active.OrderByDescending(faction => faction.ClassPower))
        {
            int share = power > 0f
                ? (int)Math.Round(SeededFactionRatio * Math.Max(0f, faction.ClassPower) / power)
                : SeededFactionRatio / active.Count;
            share = Math.Max(0, Math.Min(share, SeededFactionRatio - assigned));
            ratios[faction] = share;
            assigned += share;
        }
        kingdom.ClampFactionRatio();
    }

    #endregion

    #region 规则与门槛

    // 王国能不能推这项制度(帝国另有一套)：总开关、是否独立、规则一
    private static bool CanKingdomTakeUp(InstitutionRealm realm, InstitutionNodeConfig node, string culture,
        out string reason)
    {
        reason = "";
        if (!KingdomReformsEnabled)
        {
            reason = "institution_kingdom_reforms_off";
            return false;
        }
        if (!realm.IsSovereign)
        {
            reason = "institution_realm_not_sovereign";
            return false;
        }
        if (ChangesRegime(node) && HasCultureEmpire(culture))
        {
            reason = "institution_reform_requires_paramount";
            return false;
        }
        return true;
    }

    // 本文化有没有现存的帝国(按制度文化算，同时代潮流)
    public static bool HasCultureEmpire(string culture) =>
        CultureService.IsValidCulture(culture) && ModClass.EMPIRE_MANAGER != null &&
        ModClass.EMPIRE_MANAGER.Any(empire => empire?.data != null && !empire.IsArchived() && !empire.isRekt() &&
                                              empire.CoreKingdom != null && !empire.CoreKingdom.isRekt() &&
                                              string.Equals(GetPrimaryCulture(empire), culture, StringComparison.Ordinal));

    // 本文化自己推制度的独立王国(有城)
    private static List<Kingdom> CultureKingdomRealms(string culture)
    {
        var result = new List<Kingdom>();
        if (!KingdomReformsEnabled || World.world?.kingdoms == null || !CultureService.IsValidCulture(culture)) return result;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || !(kingdom.cities?.Count > 0)) continue;
            if (!string.Equals(CultureService.GetRealmCulture(kingdom), culture, StringComparison.Ordinal)) continue;
            if (InstitutionRealm.IsSovereignKingdom(kingdom)) result.Add(kingdom);
        }
        return result;
    }

    // 本文化各王国正在推的改革(国家, 节点)
    private static IEnumerable<(Kingdom kingdom, string nodeId)> CultureKingdomReforms(string culture)
    {
        if (World.world?.kingdoms == null) yield break;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            string nodeId = kingdom.GetOrCreate().institution_state?.active_reform?.node_id;
            if (string.IsNullOrEmpty(nodeId) ||
                !string.Equals(CultureService.GetRealmCulture(kingdom), culture, StringComparison.Ordinal)) continue;
            yield return (kingdom, nodeId);
        }
    }

    // 王国 AI 这一年动不动手(间隔已由 TryStartAiReform 判过)：第一次按国家编号错开年份，免得同文化的王国同一年一齐动手；
    // 叛乱未平不推；本文化已有三个王国在推就等着
    private static bool KingdomAiMayTry(InstitutionRealm realm, InstitutionEmpireState state, string culture, int interval)
    {
        Kingdom kingdom = realm.Kingdom;
        if (kingdom == null) return false;
        if (state.last_ai_reform_attempt_timestamp < 0 && interval > 1 &&
            (Date.getCurrentYear() + kingdom.id) % interval != 0) return false;
        if (RealmAuthority.InRebellion(kingdom)) return false;
        return CultureKingdomReforms(culture).Count(entry => entry.kingdom != kingdom) < MaxKingdomReformsPerCulture;
    }

    // 同文化别的政权(帝国、王国)正在推的节点：王国 AI 不跟着重复推
    private static HashSet<string> NodesReformedElsewhere(InstitutionRealm realm, string culture)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
        {
            if (empire?.data == null || empire.IsArchived() || empire.isRekt() || empire == realm.Empire) continue;
            string nodeId = empire.data.institution_state?.active_reform?.node_id;
            if (!string.IsNullOrEmpty(nodeId) &&
                string.Equals(GetPrimaryCulture(empire), culture, StringComparison.Ordinal)) result.Add(nodeId);
        }
        foreach ((Kingdom kingdom, string nodeId) in CultureKingdomReforms(culture))
            if (kingdom != realm.Kingdom) result.Add(nodeId);
        return result;
    }

    #endregion

    #region 改革作罢、改政体

    // 王国不再能推这项改革(不再独立、本文化出了帝国)：作罢，记一笔
    private static void DropKingdomReform(Kingdom kingdom, InstitutionEmpireState state, string culture, string reasonKey)
    {
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(state.active_reform?.node_id);
        state.active_reform = null;
        if (node == null) return;
        string content = string.Format(LM.Get("institution_kingdom_reform_dropped_history"), kingdom.GetKingdomName(),
            GetNodeName(node, culture), LM.Get(reasonKey));
        LogService.LogInfo($"[EmpireCraft][制度] {content}");
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, kingdom);
    }

    // 阻力满了：王国没有成员国可以起兵，改革当场作罢；反对最力的派系 +6，君主声望 −20
    private static void CollapseKingdomReform(InstitutionRealm realm, InstitutionNodeConfig node)
    {
        InstitutionEmpireState state = EnsureRealmState(realm);
        Kingdom kingdom = realm.Kingdom;
        if (state == null || kingdom == null) return;
        FixedFaction opposition = (kingdom.GetRegime()?.GetPlayerFactions() ?? Enumerable.Empty<FixedFaction>())
            .Where(faction => faction != null && !faction.Ban && node.politics.oppose_factions.ContainsKey(faction.Type))
            .OrderByDescending(faction => kingdom.GetFactionRatioValue(faction)).FirstOrDefault();
        if (opposition != null) kingdom.TryIncreaseFactionRatio(opposition, CollapseOppositionGain);
        AddRulerRenown(realm.Ruler, -CollapseRenownLoss);
        RealmAuthority.Invalidate(kingdom);
        state.active_reform = null;
        string content = string.Format(LM.Get("institution_kingdom_reform_collapsed_history"), realm.Name,
            GetNodeName(node, realm.Culture), GetResistanceReason(node));
        realm.Record(content, actorId: realm.Ruler?.id ?? -1L, kingdomId: kingdom.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(content, kingdom);
    }

    // 王国推成了改政体的制度(仿 ApplyRegimeChangeCore)：手选过、被锁定的政体不动；君主国不随文化直接跳到现代(走 RepublicSystem)
    private static void ApplyKingdomRegimeChange(InstitutionRealm realm, RegimeType regimeType)
    {
        Kingdom kingdom = realm?.Kingdom;
        if (kingdom?.data == null || kingdom.isRekt() || RegimeManager.regimes == null ||
            !RegimeManager.regimes.ContainsKey(regimeType)) return;
        RegimeType? current = kingdom.GetRegime()?.type;
        if (current == regimeType) return;
        if (kingdom.GetOrCreate().regime_manually_selected || RepublicSystem.IsRegimeLocked(kingdom)) return;
        if (regimeType == RegimeType.Modern && RegimeManager.IsMonarchy(current)) return;
        LogService.LogInfo($"[EmpireCraft][制度] {kingdom.GetKingdomName()} 推成制度，政体 {current} → {regimeType}");
        ReligionLevel? piety = PapacySystem.PietyBeforeReload(kingdom);
        kingdom.SetRegimeType(regimeType);
        kingdom.LoadRegime();
        kingdom.SystemChange();
        // LoadRegime 只给帝国核心对派系占比；王国换了一套派系对象，当即按编号、类别对上，免得这一年的支持度算不到派系
        List<FixedFaction> factions = kingdom.GetRegime()?.GetPlayerFactions();
        if (factions != null) kingdom.ReconcileFactionRatios(factions);
        // LoadRegime 从模板重新克隆政体，改过的选项都回到默认：清掉落地记录，整体重放一遍持久效果(同帝国)
        InstitutionEmpireState state = EnsureRealmState(realm);
        if (state == null) return;
        state.applied_node_ids.Clear();
        SyncSharedEffects(realm);
        // 圣城之主的政教合一不是制度给的(圣城神权自立)，换政体后照旧
        PapacySystem.KeepTheocracy(kingdom, piety);
    }

    #endregion

    #region 称帝与解散：改革交接

    // 称帝(EmpireManager.NewEmpire)：王国进行中的改革带进帝国；落地记录不带(帝国按自己的记录补持久效果)
    public static void OnEmpireProclaimed(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt()) return;
        InstitutionEmpireState kingdomState = core.GetOrCreate().institution_state;
        InstitutionReformState reform = kingdomState?.active_reform;
        if (reform == null) return;
        kingdomState.active_reform = null;
        InstitutionEmpireState empireState = EnsureEmpireState(empire);
        if (empireState == null || empireState.active_reform != null) return;
        empireState.active_reform = reform;
        LogService.LogInfo($"[EmpireCraft][制度] {empire.GetEmpireName()} 称帝，{core.GetKingdomName()} 进行中的改革 {reform.node_id} 带进帝国");
    }

    // 帝国解散(EmpireManager.dissolveEmpire 之前)：进行中的改革交回核心王国；帝国落地过的持久效果也落在核心王国身上，照帝国的记录记底
    public static void OnEmpireDissolving(Empire empire)
    {
        InstitutionEmpireState empireState = empire?.data?.institution_state;
        Kingdom core = empire?.CoreKingdom;
        if (empireState == null || core?.data == null || core.isRekt() || World.world == null) return;
        KingdomExtension.KingdomExtraData extra = core.GetOrCreate();
        InstitutionEmpireState kingdomState = extra.institution_state ??= new InstitutionEmpireState();
        InstitutionStateNormalizer.Normalize(kingdomState);
        kingdomState.applied_node_ids = new List<string>(empireState.applied_node_ids);
        kingdomState.last_update_timestamp = empireState.last_update_timestamp >= 0
            ? empireState.last_update_timestamp
            : World.world.getCurWorldTime();
        kingdomState.last_ai_reform_attempt_timestamp = empireState.last_ai_reform_attempt_timestamp;
        InstitutionReformState reform = empireState.active_reform;
        if (reform == null || kingdomState.active_reform != null) return;
        // 反改革的叛乱是帝国的仗，交回王国后不再挂着等它打完
        reform.resistance_war_id = -1L;
        kingdomState.active_reform = reform;
        empireState.active_reform = null;
        LogService.LogInfo($"[EmpireCraft][制度] {empire.GetEmpireName()} 解散，进行中的改革 {reform.node_id} 交回 {core.GetKingdomName()}");
    }

    #endregion

    #region 文化级循环带上王国

    // 文化级制度变动(强制点亮、回退)后同步本文化的王国：作废已不成立的改革、补持久效果。
    // 还没扫过(没记底)的王国不在这里补，第一次年度扫描时按本文化现状记底
    private static void SyncCultureKingdoms(string culture, Func<string, bool> reformVoid)
    {
        foreach (Kingdom kingdom in CultureKingdomRealms(culture))
        {
            InstitutionEmpireState state = kingdom.GetOrCreate().institution_state;
            if (state == null || state.last_update_timestamp < 0) continue;
            if (state.active_reform != null && reformVoid(state.active_reform.node_id)) state.active_reform = null;
            SyncSharedEffects(InstitutionRealm.Of(kingdom));
        }
    }

    #endregion
}
