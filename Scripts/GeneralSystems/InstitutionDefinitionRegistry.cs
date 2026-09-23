using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems;

// 制度定义表。载入器直接扫 InstitutionTrees/ 目录：
//   InstitutionTrees/Settings.json   全局设置（文明等级门槛 + 吸收规则）
//   InstitutionTrees/<线 id>.json    一条科技线，文件名即线 id
//
// 目录里每多一个 json 就多一条线，不需要改任何代码；文化通过 CultureRulesConfig.json 里
// setting.institution_line 认领自己属于哪条线。
//
// 这里刻意不再从 Regimes/Configs/*/SystemConfig.json 里读树：制度归属文化（进而归属线），
// 跟政体不是一回事，挂在政体配置里会导致"改了政体就换了一棵树"，也没法表达一条线内部
// 本来就包含的政体演进（比如华夏线的郡县制会把周制政体改成律令政体）。
public static class InstitutionDefinitionRegistry
{
    public const string SettingsFileName = "Settings.json";
    public const string FolderName = "InstitutionTrees";

    private static readonly Dictionary<string, InstitutionNodeConfig> Definitions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, InstitutionTreeConfig> Trees = new(StringComparer.Ordinal);
    private static readonly HashSet<string> InvalidNodes = new(StringComparer.Ordinal);

    private static readonly HashSet<string> SupportedEffects = new(StringComparer.Ordinal)
    {
        "add_mandate", "change_tax_level", "set_regime_option", "modify_faction_power", "enable_claim",
        "change_regime", "unlock_succession_law", "grant_trait",
        "open_private_land_market", "enact_law"
    };

    // 可以在同文化的其他政权身上幂等重放的"持久状态"效果。
    // 剩下的（add_mandate / modify_faction_power / change_regime）是一次性的，只作用于
    // 真正推完这次改革的那个政权——否则"同文化共享"会变成每个同文化帝国都被扣一次正统、
    // 都被强行改一次政体。
    // unlock_succession_law 写的是 CultureInstitutionState（按文化，不按帝国），幂等 add，
    // 同样适合归进共享效果。grant_trait 给士兵发特质、不给已经有的人重发，也是幂等的，
    // 而且要覆盖同文化的其它政权(不只是推完改革的那一个)，同样归进共享效果。
    private static readonly HashSet<string> SharedEffects = new(StringComparer.Ordinal)
    {
        "change_tax_level", "set_regime_option", "enable_claim", "unlock_succession_law", "grant_trait",
        "open_private_land_market", "enact_law"
    };

    public static InstitutionGlobalConfig Global { get; private set; } = new();

    public static IEnumerable<InstitutionNodeConfig> All => Definitions.Values.Where(IsValid);

    public static IReadOnlyList<string> Lines => Trees.Keys.OrderBy(line => line, StringComparer.Ordinal).ToList();

    public static bool IsSharedEffect(string type) =>
        !string.IsNullOrWhiteSpace(type) && SharedEffects.Contains(type);

    public static bool HasLine(string line) =>
        !string.IsNullOrWhiteSpace(line) && Trees.ContainsKey(line);

    // 文化配的线不存在时的兜底线：优先用 Settings.json 的 default_line，再退化成目录里的第一条
    public static string ResolveLine(string line)
    {
        if (HasLine(line)) return line;
        if (HasLine(Global.default_line)) return Global.default_line;
        return Trees.Count > 0 ? Lines[0] : "";
    }

    public static void Load()
    {
        Definitions.Clear();
        Trees.Clear();
        InvalidNodes.Clear();
        Global = new InstitutionGlobalConfig();
        InstitutionConfigNormalizer.Normalize(Global);

        string folder = Path.Combine(ModClass._declare.FolderPath, FolderName);
        if (!Directory.Exists(folder))
        {
            LogService.LogWarning($"未发现制度配置目录: {folder}，制度系统将没有任何可研究节点。");
            return;
        }

        string settingsPath = Path.Combine(folder, SettingsFileName);
        if (File.Exists(settingsPath))
        {
            try
            {
                InstitutionGlobalConfig parsed =
                    JsonConvert.DeserializeObject<InstitutionGlobalConfig>(File.ReadAllText(settingsPath));
                if (parsed != null)
                {
                    Global = parsed;
                    InstitutionConfigNormalizer.Normalize(Global);
                }
            }
            catch (Exception exception)
            {
                LogService.LogError($"制度全局设置读取失败，改用内置默认值: {exception}");
            }
        }
        else
        {
            LogService.LogWarning($"未发现 {settingsPath}，制度全局设置改用内置默认值。");
        }

        foreach (string path in Directory.GetFiles(folder, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(path);
            if (string.Equals(fileName, SettingsFileName, StringComparison.OrdinalIgnoreCase)) continue;
            string lineId = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(lineId)) continue;
            try
            {
                InstitutionTreeConfig tree =
                    JsonConvert.DeserializeObject<InstitutionTreeConfig>(File.ReadAllText(path));
                if (tree == null) continue;
                // 文件名就是线 id，配置里写的 line 字段一律忽略，免得两处对不上
                tree.line = lineId;
                InstitutionConfigNormalizer.Normalize(tree);
                Trees[lineId] = tree;
                foreach (InstitutionNodeConfig node in tree.nodes)
                {
                    if (node == null || string.IsNullOrWhiteSpace(node.id))
                    {
                        LogService.LogWarning($"{lineId} 线存在无 id 的制度节点，已跳过。");
                        continue;
                    }
                    if (Definitions.ContainsKey(node.id))
                    {
                        LogService.LogWarning($"制度节点 id 重复: {node.id}，已保留首个定义。");
                        continue;
                    }
                    Definitions[node.id] = node;
                }
                LogService.LogInfo($"加载制度线完成: {lineId}，节点 {tree.nodes.Count} 个。");
            }
            catch (Exception exception)
            {
                LogService.LogError($"制度线 {lineId} 读取失败: {exception}");
            }
        }

        Validate();
    }

    private static void Validate()
    {
        foreach (InstitutionNodeConfig node in Definitions.Values)
        {
            bool dangling = false;
            foreach (string reference in node.requires.Concat(node.requires_any).Concat(node.replaces)
                         .Concat(node.exclusive_with))
            {
                if (!Definitions.TryGetValue(reference, out InstitutionNodeConfig target))
                {
                    LogService.LogWarning($"制度节点 {node.id} 引用了不存在的节点 {reference}，已禁用该节点。");
                    dangling = true;
                    continue;
                }
                if (!string.Equals(target.line, node.line, StringComparison.Ordinal))
                {
                    // 跨线引用是设计上的错误：每条线必须自成体系，外来制度只能靠"吸收"进来，
                    // 而吸收是直接获得、不查前置的，所以前置永远不该指向别的线。
                    LogService.LogWarning(
                        $"制度节点 {node.id}({node.line} 线) 引用了 {target.line} 线的 {reference}，" +
                        "线之间不应有前置/替代/互斥关系，已禁用该节点。");
                    dangling = true;
                }
            }
            if (dangling) InvalidNodes.Add(node.id);

            foreach (string required in GetAllPrerequisites(node))
            {
                if (Definitions.TryGetValue(required, out InstitutionNodeConfig prerequisite) &&
                    prerequisite.advancement >= node.advancement)
                {
                    // 只是警告：树状图按等级分层布点，前置等级不低于自己会让连线往回画。
                    LogService.LogWarning(
                        $"制度节点 {node.id}(等级 {node.advancement}) 的前置 {required}" +
                        $"(等级 {prerequisite.advancement}) 等级不低于它，树状图分层会显示异常。");
                }
            }

            if (!string.IsNullOrWhiteSpace(node.regime) && !TryParseRegime(node.regime, out _))
            {
                LogService.LogWarning($"制度节点 {node.id} 的 regime = {node.regime} 不是有效的政体类型，已忽略。");
            }

            foreach (InstitutionEffectConfig effect in node.effects_on_start.Concat(node.effects_on_complete)
                         .Where(effect => effect != null && !string.IsNullOrWhiteSpace(effect.type) &&
                                          !SupportedEffects.Contains(effect.type)))
            {
                LogService.LogWarning($"制度节点 {node.id} 配置了未知效果 {effect.type}，运行时会跳过。");
            }
        }

        foreach (string nodeId in Definitions.Keys.ToList())
        {
            if (HasCycle(nodeId, new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal)))
            {
                InvalidNodes.Add(nodeId);
                LogService.LogWarning($"制度节点 {nodeId} 存在循环前置条件，已禁用。");
            }
        }

        // 被禁用节点的后代也一并禁用，否则玩家会看到一个永远无法满足前置的节点
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (InstitutionNodeConfig node in Definitions.Values)
            {
                if (InvalidNodes.Contains(node.id)) continue;
                bool anyBranchLost = node.requires_any.Count > 0 &&
                                     node.requires_any.All(required => InvalidNodes.Contains(required));
                if (!node.requires.Any(required => InvalidNodes.Contains(required)) && !anyBranchLost) continue;
                InvalidNodes.Add(node.id);
                LogService.LogWarning($"制度节点 {node.id} 的前置已被禁用，一并禁用。");
                changed = true;
            }
        }

        foreach (KeyValuePair<string, InstitutionTreeConfig> pair in Trees)
        {
            foreach (string rootId in pair.Value.root_nodes)
            {
                InstitutionNodeConfig root = Get(rootId);
                if (root == null)
                {
                    LogService.LogWarning($"{pair.Key} 线的根节点 {rootId} 不存在或已被禁用。");
                    continue;
                }
                if (!string.Equals(root.line, pair.Key, StringComparison.Ordinal))
                    LogService.LogWarning($"{pair.Key} 线的根节点 {rootId} 实际属于 {root.line} 线。");
                if (root.requires.Count > 0 || root.requires_any.Count > 0)
                    LogService.LogWarning($"{pair.Key} 线的根节点 {rootId} 带有前置条件，根节点应当没有前置。");
            }
            if (pair.Value.root_nodes.Count == 0)
                LogService.LogWarning($"{pair.Key} 线没有配置 root_nodes，该线文化开局会是完全空白的树。");
            else if (!TryGetRootRegime(pair.Key, out _))
                LogService.LogWarning($"{pair.Key} 线的根节点没有声明 regime，该线文化的初始政体只能退回" +
                                      "CultureRulesConfig.json 里的 setting.regime。");
        }

        if (Trees.Count == 0) LogService.LogWarning("一条制度线都没加载到，制度窗口会是空的。");
        else if (!HasLine(Global.default_line))
            LogService.LogWarning($"Settings.json 里的 default_line = {Global.default_line} 不存在，" +
                                  $"兜底线改用 {(Lines.Count > 0 ? Lines[0] : "(无)")}。");
    }

    // requires(全部满足) 与 requires_any(任一满足) 合在一起：用于连线、校验等只关心"有哪些前置"的场合
    public static IEnumerable<string> GetAllPrerequisites(InstitutionNodeConfig node) =>
        node == null ? Enumerable.Empty<string>() : node.requires.Concat(node.requires_any).Distinct();

    public static bool ArePrerequisitesMet(InstitutionNodeConfig node, Func<string, bool> isEnacted)
    {
        if (node == null) return false;
        return node.requires.All(isEnacted) &&
               (node.requires_any.Count == 0 || node.requires_any.Any(isEnacted));
    }

    // 缺少的前置：必需的逐项列出；"任一满足"的一组如果一个都没有，合成一项"A或B"
    public static List<List<string>> GetMissingPrerequisites(InstitutionNodeConfig node, Func<string, bool> isEnacted)
    {
        var result = new List<List<string>>();
        if (node == null) return result;
        foreach (string required in node.requires.Where(required => !isEnacted(required)))
            result.Add(new List<string> { required });
        if (node.requires_any.Count > 0 && !node.requires_any.Any(isEnacted))
            result.Add(node.requires_any.ToList());
        return result;
    }

    public static bool TryParseRegime(string value, out RegimeType regime)
    {
        return Enum.TryParse(value, true, out regime) && Enum.IsDefined(typeof(RegimeType), regime);
    }

    // 该节点声明的政体形态；没声明就返回 false
    public static bool TryGetNodeRegime(InstitutionNodeConfig node, out RegimeType regime)
    {
        regime = default;
        return node != null && !string.IsNullOrWhiteSpace(node.regime) &&
               TryParseRegime(node.regime, out regime);
    }

    // 一条线的初始政体 = 它根节点声明的政体。多个根节点时取第一个声明了政体的。
    public static bool TryGetRootRegime(string line, out RegimeType regime)
    {
        regime = default;
        foreach (string rootId in GetRootNodes(line))
        {
            if (TryGetNodeRegime(Get(rootId), out regime)) return true;
        }
        return false;
    }

    private static bool HasCycle(string nodeId, HashSet<string> visiting, HashSet<string> visited)
    {
        if (visited.Contains(nodeId)) return false;
        if (!visiting.Add(nodeId)) return true;
        if (Definitions.TryGetValue(nodeId, out InstitutionNodeConfig node))
        {
            foreach (string required in GetAllPrerequisites(node))
            {
                if (Definitions.ContainsKey(required) && HasCycle(required, visiting, visited)) return true;
            }
        }
        visiting.Remove(nodeId);
        visited.Add(nodeId);
        return false;
    }

    public static InstitutionNodeConfig Get(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && Definitions.TryGetValue(id, out InstitutionNodeConfig node) &&
               IsValid(node)
            ? node
            : null;
    }

    public static bool IsValid(InstitutionNodeConfig node) =>
        node != null && !string.IsNullOrWhiteSpace(node.id) && !InvalidNodes.Contains(node.id);

    public static InstitutionTreeConfig GetTree(string line) =>
        !string.IsNullOrWhiteSpace(line) && Trees.TryGetValue(line, out InstitutionTreeConfig tree) ? tree : null;

    public static IReadOnlyList<string> GetRootNodes(string line) =>
        GetTree(line)?.root_nodes ?? (IReadOnlyList<string>)Array.Empty<string>();

    public static string GetLineNameKey(string line)
    {
        InstitutionTreeConfig tree = GetTree(line);
        return string.IsNullOrWhiteSpace(tree?.name_key)
            ? $"institution_line_{(line ?? "").ToLower()}"
            : tree.name_key;
    }

    // 一条线的全部节点，按等级、分支、id 稳定排序（树状图布点直接吃这个顺序）
    public static IReadOnlyList<InstitutionNodeConfig> GetForLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return Array.Empty<InstitutionNodeConfig>();
        return Definitions.Values
            .Where(node => IsValid(node) && string.Equals(node.line, line, StringComparison.Ordinal))
            .OrderBy(node => node.advancement)
            .ThenBy(node => node.branch, StringComparer.Ordinal)
            .ThenBy(node => node.id, StringComparer.Ordinal)
            .ToList();
    }
}
