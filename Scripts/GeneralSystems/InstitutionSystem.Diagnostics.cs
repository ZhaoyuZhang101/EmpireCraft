using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 制度进度诊断(群友："真的只有游牧在攀制度，其他文化搁那岁月静好了")：每年每个文化一行 [EmpireCraft][制度进度]，
// 进游戏看日志就知道各文化在推什么、卡在哪：
//   · 政权：帝国几个(其中部落联盟几个；单个部落 10-26 起算王国)、自己推制度的王国几个、帝国里同文化成员国几个；王国扫描一轮用了几年
//     (王国多、游戏开得快时不止一年)；改革环境：折算的政权数、提速倍数、最短年数；王国、部落同推名额；
//   · 在推：谁在推哪项、进度、推了几年(至多列四项)；
//   · 前沿：前置已齐、还没施行的节点几项，其中几项本文化还没发明所需科技(列出缺的科技)；
//   · 今年 AI 试推几次、开推几项；王国、部落没动手的缘故(错开年份、叛乱未平、同推名额已满)；
//     候选节点受阻的缘故(缺科技、本国未采纳、正统/君威不足、反对占上风、支持没多出五分、须由帝国推、
//     须某派当权、别家在推)各几次；
//   · 时代潮流推到哪项、几成，或为何停着。
// 只在 AI 本来就要算的地方顺手记数，年度汇总时每个文化再扫一遍本线节点，不另扫人口。
public static partial class InstitutionSystem
{
    private const string ReasonTakenElsewhere = "taken_elsewhere";
    private const string ReasonSupportMargin = "support_margin";
    private const string ReasonTechUninvented = "tech_uninvented";
    private const string ReasonTechUnadopted = "tech_unadopted";

    private enum AiSkip
    {
        Stagger,
        Rebellion,
        Busy
    }

    private sealed class ReformDiagnostics
    {
        public int Tries, Starts, Stagger, Rebellion, Busy;
        public readonly Dictionary<string, int> Blocks = new(StringComparer.Ordinal);
        // 每种缘故记第一个受阻的节点，日志里举例
        public readonly Dictionary<string, string> Samples = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> UnadoptedTechs = new(StringComparer.Ordinal);
    }

    private static readonly Dictionary<string, ReformDiagnostics> Diagnostics = new(StringComparer.Ordinal);
    // 王国扫描上一轮：排了几国、用了几年(-1 = 还没扫完过一轮)
    private static int _kingdomPassSize;
    private static double _kingdomPassYears = -1d;

    // 不算"前沿受阻"的缘故：已施行、被取代、前置未齐、不是本线/本文化的、要执政党或复合帝国的、互斥的
    private static readonly HashSet<string> NotFrontierReasons = new(StringComparer.Ordinal)
    {
        "institution_reform_invalid",
        "institution_reform_wrong_line",
        "institution_reform_no_tradition",
        "ideology_reform_requires_governing_party",
        "institution_reform_superseded",
        "institution_reform_already_active",
        "institution_reform_already_enacted",
        "institution_reform_missing_requirement",
        "institution_reform_premodern",
        "institution_reform_requires_composite_empire",
        "institution_reform_requires_cities",
        "institution_reform_exclusive"
    };

    private static readonly Dictionary<string, string> ReasonLabels = new(StringComparer.Ordinal)
    {
        { ReasonTechUninvented, "缺科技" },
        { ReasonTechUnadopted, "本国未采纳科技" },
        { "institution_reform_low_mandate", "正统不足" },
        { "institution_reform_low_authority", "君威不足" },
        { "institution_reform_opposition_dominant", "反对占上风" },
        { ReasonSupportMargin, "支持没多出五分" },
        { "institution_reform_requires_paramount", "须由帝国推(规则一)" },
        { "institution_reform_sponsor_required", "须某派当权" },
        { ReasonTakenElsewhere, "别家在推" },
        { "institution_kingdom_reforms_off", "王国推制度已关" },
        { "institution_realm_not_sovereign", "不是独立政权" },
        { "institution_reform_force_disabled", "不许强推" }
    };

    private static void ResetDiagnostics()
    {
        Diagnostics.Clear();
        _kingdomPassSize = 0;
        _kingdomPassYears = -1d;
    }

    private static ReformDiagnostics DiagnosticsFor(string culture)
    {
        culture ??= "";
        if (!Diagnostics.TryGetValue(culture, out ReformDiagnostics diagnostics))
            Diagnostics[culture] = diagnostics = new ReformDiagnostics();
        return diagnostics;
    }

    private static void NoteAiSkip(string culture, AiSkip skip)
    {
        ReformDiagnostics diagnostics = DiagnosticsFor(culture);
        switch (skip)
        {
            case AiSkip.Stagger:
                diagnostics.Stagger++;
                break;
            case AiSkip.Rebellion:
                diagnostics.Rebellion++;
                break;
            default:
                diagnostics.Busy++;
                break;
        }
    }

    private static void NoteAiTry(string culture) => DiagnosticsFor(culture).Tries++;

    private static void NoteAiStart(string culture) => DiagnosticsFor(culture).Starts++;

    // AI 挑候选时某个节点推不了：只记前沿节点(前置已齐、未施行)的缘故；缺科技再分本文化没发明、本国没采纳
    private static void NoteCandidateBlock(InstitutionRealm realm, string culture, InstitutionNodeConfig node, string reason)
    {
        if (node == null || string.IsNullOrEmpty(reason) || NotFrontierReasons.Contains(reason)) return;
        ReformDiagnostics diagnostics = DiagnosticsFor(culture);
        if (reason == "institution_reform_missing_tech")
        {
            if (!TechnologySystem.AreInstitutionTechsMet(culture, node, out _)) reason = ReasonTechUninvented;
            else
            {
                reason = ReasonTechUnadopted;
                if (!TechnologySystem.AreRealmInstitutionTechsMet(realm?.Core, node, out List<string> missing))
                    foreach (string tech in missing)
                        diagnostics.UnadoptedTechs[tech] = diagnostics.UnadoptedTechs.TryGetValue(tech, out int seen) ? seen + 1 : 1;
            }
        }
        diagnostics.Blocks[reason] = diagnostics.Blocks.TryGetValue(reason, out int count) ? count + 1 : 1;
        if (!diagnostics.Samples.ContainsKey(reason)) diagnostics.Samples[reason] = GetNodeName(node, culture);
    }

    // 王国扫描排空一轮(见 TryYearlyKingdomScan)：这一轮从排队到扫完用了几年
    private static void NoteKingdomPassDone(double now)
    {
        if (_lastKingdomScan < 0) return;
        _kingdomPassYears = Math.Max(0d, (now - _lastKingdomScan) / SecondsPerYear);
    }

    // 前沿：前置已齐、本文化还没施行、也没被取代的本线节点(理念车道、要复合帝国的、互斥的、禁止近代化挡住的不算)
    private static bool IsFrontierNode(string culture, CultureInstitutionState state, InstitutionNodeConfig node) =>
        node != null && InstitutionDefinitionRegistry.IsValid(node) && node.branch != IdeologyBranch &&
        !IdeologyInstitutionPaths.TryGetIdeology(node.branch, out _) && !node.requires_composite_empire &&
        InstitutionDefinitionRegistry.FitsCulture(node, culture) &&
        !state.enacted_node_ids.Contains(node.id) && !IsSuperseded(state, node.id) &&
        InstitutionDefinitionRegistry.ArePrerequisitesMet(node, id => IsPrerequisiteHeld(state, id)) &&
        !node.exclusive_with.Any(state.enacted_node_ids.Contains) &&
        !(TechnologySystem.PremodernLocked && TechnologySystem.IsModernInstitution(node));

    // 每年每个文化一行(见 TryYearlyCultureDrift)，记过的数随即清零
    private static void LogCultureProgress(string culture)
    {
        CultureInstitutionState state = GetOrCreateCultureState(culture);
        if (state == null || World.world == null) return;
        Diagnostics.TryGetValue(culture, out ReformDiagnostics diagnostics);
        Diagnostics.Remove(culture);
        double now = World.world.getCurWorldTime();

        List<Empire> empires = (ModClass.EMPIRE_MANAGER ?? Enumerable.Empty<Empire>())
            .Where(empire => empire?.data != null && !empire.IsArchived() && !empire.isRekt() &&
                             empire.CoreKingdom != null && !empire.CoreKingdom.isRekt() &&
                             string.Equals(GetPrimaryCulture(empire), culture, StringComparison.Ordinal)).ToList();
        int tribes = empires.Count(IsTribalEmpire);
        List<Kingdom> kingdoms = CultureKingdomRealms(culture);

        var pushing = new List<string>();
        foreach (Empire empire in empires)
            if (empire.data.institution_state?.active_reform is InstitutionReformState reform)
                pushing.Add(DescribeReform(empire.GetEmpireName(), reform, culture, now));
        foreach (Kingdom kingdom in kingdoms)
            if (kingdom.GetOrCreate().institution_state?.active_reform is InstitutionReformState reform)
                pushing.Add(DescribeReform(kingdom.GetKingdomName(), reform, culture, now));

        List<InstitutionNodeConfig> frontier = GetForLine(culture).Where(node => IsFrontierNode(culture, state, node)).ToList();
        var lacking = new List<string>();
        int lackingCount = 0;
        foreach (InstitutionNodeConfig node in frontier)
        {
            if (TechnologySystem.AreInstitutionTechsMet(culture, node, out List<string> missing)) continue;
            lackingCount++;
            if (lacking.Count < 3)
                lacking.Add(GetNodeName(node, culture) + "缺" + string.Join("、", missing.Select(TechnologySystem.GetTechName)));
        }

        var text = new StringBuilder();
        text.Append("[EmpireCraft][制度进度] ").Append(CultureLabel(culture)).Append('(').Append(culture).Append('·')
            .Append(GetCultureLine(culture)).Append("线) 文明").Append(GetCultureLevel(culture)).Append("级 科技")
            .Append(TechnologySystem.GetEraTier(culture)).Append("代 已施行").Append(state.enacted_node_ids.Count).Append("项");
        text.Append("｜政权：帝国").Append(empires.Count).Append("(部落联盟").Append(tribes).Append(") 王国").Append(kingdoms.Count);
        if (kingdoms.Count > 0 && _kingdomPassYears >= 0)
            text.Append("，王国扫描一轮").Append(_kingdomPassYears.ToString("0.0")).Append("年(").Append(_kingdomPassSize).Append("国)");
        // 改革环境按成员最多的帝国看(各政权只差接壤先进异族那一项)，没有帝国就按第一个王国
        InstitutionRealm sample = empires.OrderByDescending(empire => empire.kingdoms_list?.Count ?? 0)
                                      .Select(InstitutionRealm.Of).FirstOrDefault()
                                  ?? (kingdoms.Count > 0 ? InstitutionRealm.Of(kingdoms[0]) : null);
        if (sample != null)
        {
            InstitutionReformEnvironment environment = GetReformEnvironment(sample);
            text.Append("｜环境：成员国").Append(environment.SameCultureMembers).Append(" 折算政权")
                .Append(environment.SameCultureRealms).Append(" 提速×").Append(environment.SpeedMultiplier.ToString("0.00"))
                .Append(" 最短").Append(environment.EffectiveMinimumYears).Append("年");
        }
        int minorPushing = kingdoms.Count(kingdom => kingdom.GetOrCreate().institution_state?.active_reform != null) +
                           empires.Count(empire => IsTribalEmpire(empire) && empire.data.institution_state?.active_reform != null);
        text.Append("｜王国部落同推").Append(minorPushing).Append('/').Append(ConcurrentReformLimit(kingdoms.Count + tribes));
        text.Append("｜在推").Append(pushing.Count);
        if (pushing.Count > 0)
            text.Append("：").Append(string.Join("；", pushing.Take(4))).Append(pushing.Count > 4 ? $" 等{pushing.Count}项" : "");
        text.Append("｜前沿").Append(frontier.Count).Append("项：可推").Append(frontier.Count - lackingCount)
            .Append("，缺科技").Append(lackingCount);
        if (lacking.Count > 0) text.Append("(").Append(string.Join("；", lacking)).Append(")");
        text.Append("｜今年AI试推").Append(diagnostics?.Tries ?? 0).Append("次、开推").Append(diagnostics?.Starts ?? 0);
        if (diagnostics != null && diagnostics.Stagger + diagnostics.Rebellion + diagnostics.Busy > 0)
        {
            var skips = new List<string>();
            if (diagnostics.Stagger > 0) skips.Add("错开年份" + diagnostics.Stagger);
            if (diagnostics.Rebellion > 0) skips.Add("叛乱未平" + diagnostics.Rebellion);
            if (diagnostics.Busy > 0) skips.Add("同推名额已满" + diagnostics.Busy);
            text.Append("；未动手：").Append(string.Join("、", skips));
        }
        if (diagnostics != null && diagnostics.Blocks.Count > 0)
        {
            text.Append("；候选受阻：").Append(string.Join("、", diagnostics.Blocks.OrderByDescending(pair => pair.Value)
                .Select(pair => (ReasonLabels.TryGetValue(pair.Key, out string label) ? label : pair.Key) + "×" + pair.Value +
                                (diagnostics.Samples.TryGetValue(pair.Key, out string sample) ? "(如" + sample + ")" : ""))));
            if (diagnostics.UnadoptedTechs.Count > 0)
                text.Append("；未采纳：").Append(string.Join("、", diagnostics.UnadoptedTechs.OrderByDescending(pair => pair.Value)
                    .Take(4).Select(pair => TechnologySystem.GetTechName(pair.Key))));
        }
        text.Append("｜潮流：");
        if (pushing.Count > 0) text.Append("停(有政权在推)");
        else if (InstitutionDefinitionRegistry.Get(state.drift_node_id) is InstitutionNodeConfig drift)
            text.Append(GetNodeName(drift, culture)).Append(' ').Append(state.drift_progress.ToString("0")).Append('%')
                .Append(empires.Count + kingdoms.Count > 0 ? "(有政权，半速)" : "");
        else text.Append("无可推");
        LogService.LogInfo(text.ToString());
    }

    private static string DescribeReform(string realmName, InstitutionReformState reform, string culture, double now)
    {
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(reform.node_id);
        string nodeName = node != null ? GetNodeName(node, culture) : reform.node_id;
        int years = reform.started_timestamp >= 0 ? Math.Max(0, (int)((now - reform.started_timestamp) / SecondsPerYear)) : 0;
        return $"{realmName}·{nodeName} {reform.progress:0}%/{years}年";
    }

    private static string CultureLabel(string culture)
    {
        try
        {
            string name = culture.GetCultureTranslate();
            return string.IsNullOrWhiteSpace(name) ? culture : name;
        }
        catch (Exception)
        {
            return culture;
        }
    }
}
