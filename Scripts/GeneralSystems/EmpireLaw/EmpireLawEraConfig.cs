using System;
using System.Collections.Generic;
using System.IO;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

// 律随时变、法因政异(EmpireLawEras.json，与 EmpireLawConfig.json 同目录)。文件里写了哪一项就覆盖哪一项，
// 没写、写错的项沿用这里的默认值(与随模组发布的 json 相同)；单条写错只跳过那一条并记日志。
//   · EraRules：本国文化进入某个科技时代后(档次见 Technology/TechTree.json 的 eras：6 火药、7 工业、8 电气、9 现代……)
//     某种刑罚改成另一种。默认工业时代废笞杖(笞刑→罚金、杖刑→监禁)、废连坐(夷三族→死刑)；
//   · ModernReplace：现代政体(ModernStability.IsModern，以及改制共和的帝国)一律废肉刑、连坐、流放，以监禁为主；
//   · 死刑：现代政体里只有 ModernDeathPenaltyIdeologies 理念执政、一党专政(ModernDeathPenaltyOneParty)或
//     独裁度不低于 ModernDeathPenaltyAutocracy 的国家保留死刑，其余(民主共和国)改为 ModernDeathReplacement
//     (长期监禁，刑期 LifeImprisonmentYears 年)；
//   · GraveCrimes：十恶之罪，不减等(八议、宽仁)、不许赎、不在大赦之列；
//   · IdeologySeverity / TraitSeverity / 暴君明君：定一国宽严(-1 宽仁 ~ +1 严苛)，见 EmpireLawSystem.GetLawStance；
//   · RansomCost：赎刑每种刑罚的赎金；PrisonYears：按本罪最重的原定刑罚定监禁的刑期(年)。
public class LawEraRule
{
    public int MinEraTier = 99;
    public Dictionary<PunishmentLevel, PunishmentLevel> Replace = new();
    public string NoteKey = "";
}

public class EmpireLawEraConfig
{
    public const string FileName = "EmpireLawEras.json";

    public List<LawEraRule> EraRules = new();
    public Dictionary<PunishmentLevel, PunishmentLevel> ModernReplace = new();
    public string ModernNoteKey = "law_note_modern_penal";
    public List<PartyIdeology> ModernDeathPenaltyIdeologies = new();
    public bool ModernDeathPenaltyOneParty = true;
    public float ModernDeathPenaltyAutocracy = 60f;
    public PunishmentLevel ModernDeathReplacement = PunishmentLevel.监禁;
    public string ModernDeathNoteKey = "law_note_death_abolished";
    public int LifeImprisonmentYears = 25;
    public HashSet<LawType> GraveCrimes = new();
    public Dictionary<PartyIdeology, float> IdeologySeverity = new();
    // 本朝官学(定于一尊的学说)占几成，余下按执政理念(没有官学时全按执政理念)
    public float OfficialSchoolWeight = 0.4f;
    public Dictionary<string, float> TraitSeverity = new();
    public float TyrantRulerSeverity = 0.4f;
    public float WiseRulerSeverity = -0.1f;
    public Dictionary<PunishmentLevel, int> RansomCost = new();
    public Dictionary<PunishmentLevel, int> PrisonYears = new();

    public static EmpireLawEraConfig Defaults()
    {
        var config = new EmpireLawEraConfig
        {
            EraRules = new List<LawEraRule>
            {
                new LawEraRule
                {
                    MinEraTier = 7, NoteKey = "law_note_era_corporal",
                    Replace = new Dictionary<PunishmentLevel, PunishmentLevel>
                    {
                        [PunishmentLevel.笞刑] = PunishmentLevel.罚金,
                        [PunishmentLevel.杖刑] = PunishmentLevel.监禁
                    }
                },
                new LawEraRule
                {
                    MinEraTier = 7, NoteKey = "law_note_era_collective",
                    Replace = new Dictionary<PunishmentLevel, PunishmentLevel>
                    {
                        [PunishmentLevel.夷三族] = PunishmentLevel.死刑
                    }
                }
            },
            ModernReplace = new Dictionary<PunishmentLevel, PunishmentLevel>
            {
                [PunishmentLevel.笞刑] = PunishmentLevel.罚金,
                [PunishmentLevel.杖刑] = PunishmentLevel.监禁,
                [PunishmentLevel.流放] = PunishmentLevel.监禁,
                [PunishmentLevel.夷三族] = PunishmentLevel.死刑
            },
            ModernDeathPenaltyIdeologies = new List<PartyIdeology>
            {
                PartyIdeology.Communism, PartyIdeology.Socialism, PartyIdeology.Fascism, PartyIdeology.Authoritarianism
            },
            GraveCrimes = new HashSet<LawType>
            {
                LawType.谋反, LawType.篡位, LawType.叛国, LawType.私通敌国, LawType.伪造诏令, LawType.煽动叛乱,
                LawType.杀人, LawType.投毒, LawType.亵渎神庙, LawType.虐待亲属
            },
            IdeologySeverity = new Dictionary<PartyIdeology, float>
            {
                [PartyIdeology.Legalism] = 0.8f,
                [PartyIdeology.Strategists] = 0.3f,
                [PartyIdeology.ImperialPower] = 0.3f,
                [PartyIdeology.MilitaryMerit] = 0.2f,
                [PartyIdeology.Eunuchs] = 0.2f,
                [PartyIdeology.Fascism] = 0.8f,
                [PartyIdeology.Authoritarianism] = 0.5f,
                [PartyIdeology.Communism] = 0.5f,
                [PartyIdeology.Socialism] = 0.2f,
                [PartyIdeology.Conservatism] = 0.2f,
                [PartyIdeology.ReligiousDemocracy] = 0.2f,
                [PartyIdeology.Confucianism] = -0.4f,
                [PartyIdeology.HuangLao] = -0.5f,
                [PartyIdeology.Mohism] = -0.2f,
                [PartyIdeology.Agrarianism] = -0.2f,
                [PartyIdeology.GreatClans] = -0.2f,
                [PartyIdeology.Anarchism] = -0.6f,
                [PartyIdeology.SocialDemocracy] = -0.4f,
                [PartyIdeology.SocialLiberalism] = -0.5f,
                [PartyIdeology.Libertarianism] = -0.4f,
                [PartyIdeology.ConservativeLiberalism] = -0.1f,
                [PartyIdeology.Capitalism] = -0.1f
            },
            TraitSeverity = new Dictionary<string, float>
            {
                ["evil"] = 0.25f, ["bloodlust"] = 0.2f, ["psychopath"] = 0.25f, ["madness"] = 0.15f,
                ["paranoid"] = 0.15f, ["savage"] = 0.15f, ["pacifist"] = -0.2f, ["content"] = -0.1f
            },
            RansomCost = new Dictionary<PunishmentLevel, int>
            {
                [PunishmentLevel.笞刑] = 30, [PunishmentLevel.杖刑] = 60, [PunishmentLevel.监禁] = 100,
                [PunishmentLevel.流放] = 160
            },
            PrisonYears = new Dictionary<PunishmentLevel, int>
            {
                [PunishmentLevel.罚金] = 2, [PunishmentLevel.笞刑] = 2, [PunishmentLevel.杖刑] = 3,
                [PunishmentLevel.监禁] = 4, [PunishmentLevel.流放] = 6, [PunishmentLevel.死刑] = 12,
                [PunishmentLevel.夷三族] = 15
            }
        };
        return config;
    }

    public static EmpireLawEraConfig Load(string path)
    {
        EmpireLawEraConfig config = Defaults();
        try
        {
            if (!File.Exists(path))
            {
                LogService.LogWarning($"[律法] 没有找到 {path}，时代与宽严按默认规则");
                return config;
            }
            JToken token = JToken.Parse(File.ReadAllText(path));
            if (token is not JObject root) return config;

            if (root["EraRules"] is JArray rules)
            {
                var parsed = new List<LawEraRule>();
                foreach (JToken item in rules)
                {
                    if (item is not JObject rule) continue;
                    var era = new LawEraRule
                    {
                        MinEraTier = rule.Value<int?>("MinEraTier") ?? 99,
                        NoteKey = rule.Value<string>("NoteKey") ?? "",
                        Replace = ParseReplace(rule["Replace"] as JObject, "EraRules")
                    };
                    if (era.Replace.Count > 0) parsed.Add(era);
                }
                config.EraRules = parsed;
            }
            if (root["ModernReplace"] is JObject modern) config.ModernReplace = ParseReplace(modern, "ModernReplace");
            config.ModernNoteKey = root.Value<string>("ModernNoteKey") ?? config.ModernNoteKey;
            if (root["ModernDeathPenaltyIdeologies"] is JArray ideologies)
                config.ModernDeathPenaltyIdeologies = ParseList<PartyIdeology>(ideologies, "ModernDeathPenaltyIdeologies");
            config.ModernDeathPenaltyOneParty = root.Value<bool?>("ModernDeathPenaltyOneParty") ?? config.ModernDeathPenaltyOneParty;
            config.ModernDeathPenaltyAutocracy = root.Value<float?>("ModernDeathPenaltyAutocracy") ?? config.ModernDeathPenaltyAutocracy;
            if (TryParseEnum(root.Value<string>("ModernDeathReplacement"), out PunishmentLevel replacement))
                config.ModernDeathReplacement = replacement;
            config.ModernDeathNoteKey = root.Value<string>("ModernDeathNoteKey") ?? config.ModernDeathNoteKey;
            config.LifeImprisonmentYears = Math.Max(1, root.Value<int?>("LifeImprisonmentYears") ?? config.LifeImprisonmentYears);
            if (root["GraveCrimes"] is JArray grave)
                config.GraveCrimes = new HashSet<LawType>(ParseList<LawType>(grave, "GraveCrimes"));
            if (root["IdeologySeverity"] is JObject ideologySeverity)
                config.IdeologySeverity = ParseMap<PartyIdeology, float>(ideologySeverity, "IdeologySeverity", v => v.Value<float>());
            config.OfficialSchoolWeight = Clamp01(root.Value<float?>("OfficialSchoolWeight") ?? config.OfficialSchoolWeight);
            if (root["TraitSeverity"] is JObject traits)
            {
                var map = new Dictionary<string, float>();
                foreach (JProperty property in traits.Properties())
                {
                    if (string.IsNullOrWhiteSpace(property.Name)) continue;
                    try { map[property.Name] = property.Value.Value<float>(); }
                    catch (Exception) { LogService.LogWarning($"[律法] TraitSeverity.{property.Name} 不是数字，跳过"); }
                }
                config.TraitSeverity = map;
            }
            config.TyrantRulerSeverity = root.Value<float?>("TyrantRulerSeverity") ?? config.TyrantRulerSeverity;
            config.WiseRulerSeverity = root.Value<float?>("WiseRulerSeverity") ?? config.WiseRulerSeverity;
            if (root["RansomCost"] is JObject ransom)
                config.RansomCost = ParseMap<PunishmentLevel, int>(ransom, "RansomCost", v => Math.Max(0, v.Value<int>()));
            if (root["PrisonYears"] is JObject years)
                config.PrisonYears = ParseMap<PunishmentLevel, int>(years, "PrisonYears", v => Math.Max(1, v.Value<int>()));
            LogService.LogInfo($"[律法] 读入 {FileName}：时代规则 {config.EraRules.Count} 条，十恶 {config.GraveCrimes.Count} 条");
        }
        catch (Exception exception)
        {
            LogService.LogError($"[律法] 读取 {FileName} 失败，时代与宽严按默认规则：{exception.Message}");
            return Defaults();
        }
        return config;
    }

    private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    private static bool TryParseEnum<T>(string text, out T value) where T : struct
    {
        value = default;
        return !string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), out value) && Enum.IsDefined(typeof(T), value);
    }

    private static Dictionary<PunishmentLevel, PunishmentLevel> ParseReplace(JObject map, string section)
    {
        var result = new Dictionary<PunishmentLevel, PunishmentLevel>();
        if (map == null) return result;
        foreach (JProperty property in map.Properties())
        {
            if (TryParseEnum(property.Name, out PunishmentLevel from) &&
                TryParseEnum(property.Value?.ToString(), out PunishmentLevel to))
                result[from] = to;
            else
                LogService.LogWarning($"[律法] {section} 里的 {property.Name} → {property.Value} 不是刑罚名，跳过");
        }
        return result;
    }

    private static List<T> ParseList<T>(JArray items, string section) where T : struct
    {
        var result = new List<T>();
        foreach (JToken item in items)
        {
            if (TryParseEnum(item?.ToString(), out T value)) result.Add(value);
            else LogService.LogWarning($"[律法] {section} 里的 {item} 认不出，跳过");
        }
        return result;
    }

    private static Dictionary<TKey, TValue> ParseMap<TKey, TValue>(JObject map, string section, Func<JToken, TValue> read)
        where TKey : struct
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (JProperty property in map.Properties())
        {
            if (!TryParseEnum(property.Name, out TKey key))
            {
                LogService.LogWarning($"[律法] {section} 里的 {property.Name} 认不出，跳过");
                continue;
            }
            try { result[key] = read(property.Value); }
            catch (Exception) { LogService.LogWarning($"[律法] {section}.{property.Name} 的值写错了，跳过"); }
        }
        return result;
    }
}
