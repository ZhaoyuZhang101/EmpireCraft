using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.GeneralSystems;

// 派系决议与文化制度树、科技时代挂钩(数据在模组根目录 ClaimRules.json)。
// 以前派系决议各写各的条件，大半不看本文化走的是哪条制度路线，于是没有朝贡体系也能"迫使朝贡"、
// 石器时代也能"拓展金融霸权"、日本也会"颁布金玺诏书"。现在每个决议可以声明：
//   · cultures      只有这些文化才有(如神圣罗马式的决议只给日耳曼、法兰克、罗马、西方)；
//   · min_era / max_era  科技时代(TechTree.json 的 tier，0 = 不限)；
//   · lines         各文化制度线(Huaxia/Roma/Arabic/Youmu)的要求；声明了 lines 而本文化的线不在其中 = 没有这条路线，不出现。
//                   每条线可写：
//                     any   已施行其中任一制度才会提出(前提)；
//                     none  已施行其中任一制度就不再提出；
//                     push  推动：决议执行成功后，对其中第一个尚未施行的制度发起改革(走制度改革的政治过程)；
//                           push 里还有未施行的制度，或满足 any，即可提出；
//                     cultures  这条线里只有这些文化才有；
//                     special   额外条件，目前有 sinicized_nomad(已采纳中原制度的游牧政权，元、清式)。
// 没写进 ClaimRules.json 的决议不受限制。检查在派系挑选决议的统一入口(ClaimAgendaSystem.Evaluate)
// 与地方推动(TemporaryFaction.CheckLocalContinue)两处；推动的改革在决议执行后发起(见 AfterExecute)。
public static class ClaimRules
{
    private sealed class LineRule
    {
        public List<string> Any = new();
        public List<string> None = new();
        public List<string> Push = new();
        public List<string> Cultures = new();
        public string Special = "";
    }

    private sealed class Rule
    {
        public List<string> Cultures = new();
        public int MinEra;
        public int MaxEra;
        public Dictionary<string, LineRule> Lines;
    }

    private static Dictionary<string, Rule> _rules;

    // 自己会发起制度改革的决议(用 claim_reform 声明)：执行后不再重复发起
    private static readonly HashSet<TemporaryFactionType> SelfReforming = new()
    {
        TemporaryFactionType.开放党禁, TemporaryFactionType.推行普选, TemporaryFactionType.开科取士,
        TemporaryFactionType.转天朝制度
    };

    private static Dictionary<string, Rule> Rules
    {
        get
        {
            if (_rules != null) return _rules;
            _rules = new Dictionary<string, Rule>();
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, "ClaimRules.json");
                if (!File.Exists(path)) return _rules;
                JObject root = JObject.Parse(File.ReadAllText(path));
                foreach (JProperty claim in root.Properties())
                {
                    if (claim.Name.StartsWith("_") || claim.Value is not JObject body) continue;
                    var rule = new Rule
                    {
                        Cultures = Strings(body["cultures"]),
                        MinEra = body.Value<int?>("min_era") ?? 0,
                        MaxEra = body.Value<int?>("max_era") ?? 0
                    };
                    if (body["lines"] is JObject lines)
                    {
                        rule.Lines = new Dictionary<string, LineRule>(StringComparer.Ordinal);
                        foreach (JProperty line in lines.Properties())
                        {
                            JObject entry = line.Value as JObject ?? new JObject();
                            rule.Lines[line.Name] = new LineRule
                            {
                                Any = Strings(entry["any"]),
                                None = Strings(entry["none"]),
                                Push = Strings(entry["push"]),
                                Cultures = Strings(entry["cultures"]),
                                Special = entry.Value<string>("special") ?? ""
                            };
                        }
                    }
                    _rules[claim.Name] = rule;
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] ClaimRules.json 读取失败: {exception.Message}");
            }
            return _rules;
        }
    }

    private static List<string> Strings(JToken token) =>
        token is JArray array ? array.Select(item => item.ToString()).Where(item => item.Length > 0).ToList()
            : new List<string>();

    // 能否提出；不能时 blocker 是给玩家看的原因(已格式化)
    public static bool IsAllowed(Empire empire, TemporaryFactionType type, out string blocker)
    {
        blocker = "";
        if (empire?.CoreKingdom == null || !Rules.TryGetValue(type.ToString(), out Rule rule)) return true;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        if (rule.Cultures.Count > 0 && !rule.Cultures.Contains(culture))
        {
            blocker = LM.Get("claim_rule_no_tradition");
            return false;
        }
        if (TechnologySystem.IsEnabled && CultureService.IsValidCulture(culture))
        {
            int era = TechnologySystem.GetEraTier(culture);
            if (rule.MinEra > 0 && era < rule.MinEra)
            {
                blocker = string.Format(LM.Get("claim_rule_min_era"), EraName(rule.MinEra));
                return false;
            }
            if (rule.MaxEra > 0 && era > rule.MaxEra)
            {
                blocker = LM.Get("claim_rule_outdated");
                return false;
            }
        }
        if (rule.Lines == null) return true;
        string line = InstitutionSystem.GetCultureLine(culture);
        if (line == null || !rule.Lines.TryGetValue(line, out LineRule entry))
        {
            blocker = LM.Get("claim_rule_no_route");
            return false;
        }
        if (entry.Cultures.Count > 0 && !entry.Cultures.Contains(culture))
        {
            blocker = LM.Get("claim_rule_no_tradition");
            return false;
        }
        if (entry.Special == "sinicized_nomad" && !IsSinicizedNomad(empire))
        {
            blocker = LM.Get("claim_rule_sinicized_nomad");
            return false;
        }
        string forbidden = entry.None.FirstOrDefault(node => InstitutionSystem.IsEnacted(culture, node));
        if (forbidden != null)
        {
            blocker = string.Format(LM.Get("claim_rule_forbidden"), NodeName(forbidden));
            return false;
        }
        if (entry.Any.Count == 0 && entry.Push.Count == 0) return true;
        if (entry.Any.Any(node => InstitutionSystem.IsEnacted(culture, node))) return true;
        if (FindPushNode(empire, culture, entry) != null) return true;
        List<string> wanted = entry.Any.Count > 0 ? entry.Any : entry.Push;
        blocker = entry.Any.Count == 0
            ? LM.Get("institution_reform_already_enacted")
            : string.Format(LM.Get("claim_rule_requires"), string.Join(LM.Get("claim_rule_or"), wanted.Select(NodeName)));
        return false;
    }

    // 推动的目标制度：第一个尚未施行、且(按常规或强推)能发起改革的
    public static InstitutionNodeConfig FindPushTarget(Empire empire, TemporaryFactionType type)
    {
        if (empire?.CoreKingdom == null || !Rules.TryGetValue(type.ToString(), out Rule rule) || rule.Lines == null)
            return null;
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        string line = InstitutionSystem.GetCultureLine(culture);
        return line != null && rule.Lines.TryGetValue(line, out LineRule entry) ? FindPushNode(empire, culture, entry) : null;
    }

    private static InstitutionNodeConfig FindPushNode(Empire empire, string culture, LineRule entry)
    {
        foreach (string id in entry.Push)
        {
            if (InstitutionSystem.IsEnacted(culture, id)) continue;
            InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(id);
            if (node == null) continue;
            if (InstitutionSystem.CanStartReform(empire, node, out _, false) ||
                InstitutionSystem.CanStartReform(empire, node, out _, true)) return node;
        }
        return null;
    }

    // 决议执行后：推动类决议对目标制度发起改革(push 目标在执行前取好，见 EmpireCraftPlotsAddition)
    public static void AfterExecute(Empire empire, TemporaryFactionType type, InstitutionNodeConfig target,
        string factionId)
    {
        if (empire == null || target == null || SelfReforming.Contains(type)) return;
        if (InstitutionSystem.IsEnacted(empire, target.id)) return;
        bool force = !InstitutionSystem.CanStartReform(empire, target, out _, false) &&
                     InstitutionSystem.CanStartReform(empire, target, out _, true);
        InstitutionSystem.StartReform(empire, target.id, force, factionId);
    }

    // 已采纳中原制度的游牧政权(元、清式)：复合帝国，统治文化属游牧线、制度文化属华夏线
    private static bool IsSinicizedNomad(Empire empire)
    {
        if (!CompositeEmpireService.IsComposite(empire)) return false;
        return InstitutionSystem.GetCultureLine(CompositeEmpireService.GetRulingCulture(empire)) == "Youmu" &&
               InstitutionSystem.GetCultureLine(CompositeEmpireService.GetInstitutionalCulture(empire)) == "Huaxia";
    }

    private static string NodeName(string id)
    {
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(id);
        return node == null ? id : InstitutionSystem.GetNodeName(node);
    }

    private static string EraName(int tier)
    {
        string key = TechnologySystem.Config?.eras?.FirstOrDefault(era => era.tier == tier)?.key;
        return key == null ? tier.ToString() : LM.Get(key);
    }
}
