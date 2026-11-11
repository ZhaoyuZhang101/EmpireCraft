using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
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
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.GeneralSystems;

// 可由神力指定的目标的一个候选(见 ClaimTargetRules.Options、ClaimTargetWindow)。
// 决议自带的候选(TemporaryFaction.BuiltinCandidates)也用它：可以自带缘由、中间一列的数，也可以自带"不可选"与原因
public sealed class ClaimTargetOption
{
    public MetaType Type;
    public Kingdom Kingdom;
    public City City;
    public Actor Actor;
    public KingdomTitle Title;
    public Religion Religion;
    public bool Selectable = true;
    // 可选的写缘由(有法理宣称、宿怨……)，不可选的写哪条没过；已是当前语言的文字
    public string Why = "";
    // 名单中间那一列(国力比、城数、人口、年岁、信众占比)与它的颜色
    public string Figure = "";
    public string FigureHex = "#F2EEE2";
    // 朝廷自己挑时的分
    public float Score;

    public long Id => Type == MetaType.Kingdom ? Kingdom?.id ?? -1L
        : Type == MetaType.City ? City?.id ?? -1L
        : Type == MetaType.Unit ? Actor?.id ?? -1L
        : Type == MetaType.Religion ? Religion?.id ?? -1L
        : Title?.id ?? -1L;

    public string Name => Type == MetaType.Kingdom ? Kingdom?.GetKingdomName()
        : Type == MetaType.City ? City?.GetCityName()
        : Type == MetaType.Unit ? Actor?.getName()
        : Type == MetaType.Religion ? Religion?.data?.name
        : Title?.data?.name;

    public static ClaimTargetOption Of(Kingdom kingdom, string why = "") =>
        new() { Type = MetaType.Kingdom, Kingdom = kingdom, Why = why ?? "" };

    public static ClaimTargetOption Of(City city, string why = "") => new() { Type = MetaType.City, City = city, Why = why ?? "" };

    public static ClaimTargetOption Of(Actor actor, string why = "") => new() { Type = MetaType.Unit, Actor = actor, Why = why ?? "" };

    public static ClaimTargetOption Of(KingdomTitle title, string why = "") =>
        new() { Type = MetaTypeExtension.KingdomTitle, Title = title, Why = why ?? "" };

    public static ClaimTargetOption Of(Religion religion, string why = "") =>
        new() { Type = MetaType.Religion, Religion = religion, Why = why ?? "" };
}

// 诉求的目标规则(数据在模组根目录 ClaimRules.json 各决议的 target；用户 2026-11-11 "这里在发起诉求时应该给个目标"
// "记得给政体编辑器留诉求编辑接口，而不是写死""覆盖全部需要目标的决议")。target 可以是一条，也可以是按先后的几条
// (朝廷自己挑时先看前一条，没有可选的再看后一条)，每条：
//   kind    kingdom 国家 / city 城 / actor 人物 / title 法理头衔 / religion 宗教
//   pool    候选从哪来——
//             kingdom：foreign 本朝之外的国家；enemies 交战的敌国(属于帝国的记到它的朝廷)；enemy_kingdoms 交战的敌国本身；
//                      war_opponents 本朝作主战方的战争里对面的主战国；members 本朝的成员国(行政区、封国)；
//                      realm 本朝各国连同朝廷；border_foreign 与本朝接壤的外国
//             city：   core_cities 朝廷直辖的城
//             actor：  heir_candidates 储君人选；emperor_siblings 皇帝的兄弟(已在图上的)
//             各种类： builtin 决议自带的候选(判断太专门、写在各决议代码里的，见 TemporaryFaction.BuiltinCandidates；
//                      数据里照样可以再叠规则、加分)
//   rules   逐条都要过(写在前面的先看)；过不了的在选择窗口里灰着、写哪条没过。名字见 PassKingdom / PassCity / PassActor
//   our_power_min 本朝国力 ÷ 对方 至少；their_power_min 对方国力 ÷ 本朝 至少；min_cities 对方至少几城；
//   army_share_min / army_share_max 对方兵力 ÷ 本朝兵力 至少 / 至多(0 或不写 = 不看)
//   prefer  朝廷自己挑时各项的分(按权重相加，分最高的当选，一样高取靠前的；不写 = 取第一个过关的)。名字见 Score*
//   auto_rules  只在朝廷自己挑时多看的几条(神力指定不受它限)
//   auto    builtin = 朝廷自己挑时走决议自带的写法(分封：会把族谱里的兄弟落成实体)；不写 = 照上面的规则挑
// 各决议自己的前提(冷却、忙不过来、国库……)还在各自的 TargetReady 里。候选来源、规则、加分项的名字连同中文说明另列在
// ClaimRules.json 的 _target_vocabulary 里，政体编辑器的决议页照它列出可选项——这里加了新名字要一并写进去
// (Tools/check_claim_targets.py 查代码、名字表、语言文件三处对不对得上)。
// 没写 target 的决议：目标由局面唯一确定的(恢复圣地、逼冕……)照旧在自己的 CheckCondition 里定，不能由神力指定；
// CheckCondition 已经改成只照 target 挑(PickRuleTarget)的那些(名单在 _target_vocabulary.required)，target 不能去掉(见 WarnMissing)
public static class ClaimTargetRules
{
    private sealed class Spec
    {
        public MetaType Kind = MetaType.Kingdom;
        public string Pool = "";
        public List<string> Rules = new(), AutoRules = new();
        public float OurPowerMin, TheirPowerMin, ArmyShareMin, ArmyShareMax;
        public int MinCities;
        public List<(string by, float weight)> Prefer = new();
        public bool BuiltinAuto;
    }

    private sealed class Context
    {
        public TemporaryFaction Claim;
        public Empire Empire;
        public Kingdom Core;
        public FixedFaction Faction;
        public string Culture;
        private float _ours = -1f, _army = -1f;
        private HashSet<Kingdom> _near;
        private List<ClaimTargetOption> _builtin;
        public float Ours => _ours >= 0f ? _ours : _ours = (float)WarSituation.Power(Empire);
        public float Army => _army >= 0f ? _army : _army = Math.Max(1, Empire.countWarriors());
        // 陆上走得到的邻国(法理相连且走得通，见 WarReach)
        public HashSet<Kingdom> Near => _near ??= WarReach.WalkableNeighbourKingdoms(Core);
        // 决议自带的候选(一轮只问一回)
        public List<ClaimTargetOption> Builtin => _builtin ??= Claim.BuiltinCandidates() ?? new List<ClaimTargetOption>();
    }

    private const string Green = "#65D66E", Gold = "#F3C34A", Red = "#FF6666";
    private static Dictionary<string, List<Spec>> _specs;
    private static readonly HashSet<string> Reported = new();

    private static Dictionary<string, List<Spec>> Specs
    {
        get
        {
            if (_specs != null) return _specs;
            _specs = new Dictionary<string, List<Spec>>();
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, "ClaimRules.json");
                if (!File.Exists(path)) return _specs;
                JObject root = JObject.Parse(File.ReadAllText(path));
                foreach (JProperty claim in root.Properties())
                {
                    if (claim.Name.StartsWith("_") || claim.Value is not JObject body) continue;
                    JToken target = body["target"];
                    var list = new List<Spec>();
                    if (target is JObject single) Add(list, single, claim.Name);
                    else if (target is JArray several)
                        foreach (JToken item in several)
                            if (item is JObject entry) Add(list, entry, claim.Name);
                    if (list.Count > 0) _specs[claim.Name] = list;
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] ClaimRules.json 的目标规则读取失败: {exception.Message}");
            }
            return _specs;
        }
    }

    private static void Add(List<Spec> list, JObject entry, string claim)
    {
        string kind = entry.Value<string>("kind") ?? "kingdom";
        var spec = new Spec
        {
            Kind = kind switch
            {
                "city" => MetaType.City,
                "actor" => MetaType.Unit,
                "religion" => MetaType.Religion,
                "title" => MetaTypeExtension.KingdomTitle,
                _ => MetaType.Kingdom
            },
            Pool = entry.Value<string>("pool") ?? "",
            Rules = Strings(entry["rules"]),
            AutoRules = Strings(entry["auto_rules"]),
            OurPowerMin = entry.Value<float?>("our_power_min") ?? 0f,
            TheirPowerMin = entry.Value<float?>("their_power_min") ?? 0f,
            ArmyShareMin = entry.Value<float?>("army_share_min") ?? 0f,
            ArmyShareMax = entry.Value<float?>("army_share_max") ?? 0f,
            MinCities = entry.Value<int?>("min_cities") ?? 0,
            BuiltinAuto = entry.Value<string>("auto") == "builtin"
        };
        if (kind is not ("kingdom" or "city" or "actor" or "title" or "religion"))
            LogService.LogWarning($"[EmpireCraft] ClaimRules.json「{claim}」的 target.kind 不认识：{kind}，按国家算");
        if (entry["prefer"] is JObject prefer)
            foreach (JProperty item in prefer.Properties())
                if (item.Value.Type is JTokenType.Float or JTokenType.Integer) spec.Prefer.Add((item.Name, item.Value.Value<float>()));
        list.Add(spec);
    }

    private static List<string> Strings(JToken token) =>
        token is JArray array ? array.Select(item => item.ToString()).Where(item => item.Length > 0).ToList() : new List<string>();

    // 这条决议的目标能不能由神力指定(ClaimRules.json 里写了 target)
    public static bool Has(TemporaryFactionType type) => Specs.ContainsKey(type.ToString());

    // 决议只照 target 挑目标(TemporaryFaction.PickRuleTarget)，数据里却没写或写坏了：这条决议永远提不出来，记一条日志
    public static void WarnMissing(TemporaryFactionType type)
    {
        if (Reported.Add("missing:" + type))
            LogService.LogWarning($"[EmpireCraft][诉求目标] ClaimRules.json 里没有「{type}」的 target(或写坏了)：这条决议挑不出目标、提不出来。" +
                                  "从模组自带的 ClaimRules.json 把这一条的 target 拷回来");
    }

    // 朝廷自己挑时走决议自带的写法
    public static bool BuiltinAuto(TemporaryFactionType type) =>
        Specs.TryGetValue(type.ToString(), out List<Spec> specs) && specs.Any(spec => spec.BuiltinAuto);

    // 全部候选(连同不可选的与原因)：选择窗口用
    public static List<ClaimTargetOption> Options(TemporaryFaction claim)
    {
        var result = new List<ClaimTargetOption>();
        Context context = Build(claim, out List<Spec> specs);
        if (context == null) return result;
        foreach (Spec spec in specs) Collect(context, spec, ModeAll, result);
        return result;
    }

    // 有没有可选的(神力能不能指定)：探到一个就停，不算名单上的数
    public static bool AnySelectable(TemporaryFaction claim)
    {
        Context context = Build(claim, out List<Spec> specs);
        if (context == null) return false;
        var found = new List<ClaimTargetOption>();
        foreach (Spec spec in specs)
        {
            Collect(context, spec, ModeProbe, found);
            if (found.Count > 0) return true;
        }
        return false;
    }

    // 朝廷自己挑：先看前一条 target，没有可选的再看后一条；都没有返回 null
    public static ClaimTargetOption Auto(TemporaryFaction claim)
    {
        Context context = Build(claim, out List<Spec> specs);
        if (context == null) return null;
        var found = new List<ClaimTargetOption>();
        foreach (Spec spec in specs)
        {
            found.Clear();
            Collect(context, spec, ModeAuto, found);
            ClaimTargetOption best = null;
            foreach (ClaimTargetOption option in found)
                if (best == null || option.Score > best.Score) best = option;
            if (best != null) return best;
        }
        return null;
    }

    private static Context Build(TemporaryFaction claim, out List<Spec> specs)
    {
        specs = null;
        Empire empire = claim?.GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt() || World.world?.kingdoms == null ||
            !Specs.TryGetValue(claim.type.ToString(), out specs)) return null;
        return new Context
        {
            Claim = claim, Empire = empire, Core = core, Faction = claim.ProposerFaction,
            Culture = CultureService.GetEmpireDefaultCulture(empire)
        };
    }

    // ModeAll：全部候选连同不可选的；ModeAuto：只收过关的(连同 auto_rules)，没有加分项时收到第一个就停；
    // ModeProbe：只收过关的(不看 auto_rules)，收到第一个就停
    private const int ModeAll = 0, ModeAuto = 1, ModeProbe = 2;

    private static void Collect(Context c, Spec spec, int mode, List<ClaimTargetOption> result)
    {
        try
        {
            if (spec.Kind == MetaType.Kingdom) CollectKingdoms(c, spec, mode, result);
            else if (spec.Kind == MetaType.City) CollectCities(c, spec, mode, result);
            else if (spec.Kind == MetaType.Unit) CollectActors(c, spec, mode, result);
            else CollectOthers(c, spec, mode, result);
        }
        catch (Exception exception)
        {
            if (Reported.Add("collect:" + c.Claim.type))
                LogService.LogWarning($"[EmpireCraft][诉求目标] {c.Claim.type} 列候选出错: {exception}");
        }
    }

    // 决议自带的候选里这一种类的(见 TemporaryFaction.BuiltinCandidates)
    private static IEnumerable<ClaimTargetOption> BuiltinOf(Context c, MetaType kind) =>
        c.Builtin.Where(option => option != null && option.Type == kind && option.Id >= 0);

    // 决议自带的候选自己带的缘由、中间一列、不可选的原因并到算好的候选上
    private static void Inherit(ClaimTargetOption option, ClaimTargetOption source)
    {
        if (source == null) return;
        if (!source.Selectable)
        {
            option.Selectable = false;
            option.Why = source.Why;
        }
        else if (option.Selectable && string.IsNullOrEmpty(option.Why)) option.Why = source.Why;
        if (string.IsNullOrEmpty(source.Figure)) return;
        option.Figure = source.Figure;
        option.FigureHex = source.FigureHex;
    }

    #region 国家

    private static IEnumerable<(Kingdom subject, Kingdom target, ClaimTargetOption source)> KingdomPool(Context c, string pool)
    {
        switch (pool)
        {
            case "builtin":
                foreach (ClaimTargetOption option in BuiltinOf(c, MetaType.Kingdom).ToList())
                    if (option.Kingdom?.data != null && !option.Kingdom.isRekt()) yield return (option.Kingdom, option.Kingdom, option);
                break;
            case "enemies":
                foreach (Kingdom enemy in c.Core.getEnemiesKingdoms())
                {
                    if (enemy == null || enemy.isRekt() || enemy.IsInSameEmpire(c.Core)) continue;
                    Kingdom target = enemy.IsInEmpire() ? enemy.GetEmpire()?.CoreKingdom : enemy;
                    if (target?.data != null && !target.isRekt()) yield return (enemy, target, null);
                }
                break;
            case "enemy_kingdoms":
                foreach (Kingdom enemy in c.Core.getEnemiesKingdoms())
                    if (enemy?.data != null && !enemy.isRekt() && !enemy.IsInSameEmpire(c.Core)) yield return (enemy, enemy, null);
                break;
            case "war_opponents":
                foreach (War war in c.Core.getWars())
                {
                    if (war == null) continue;
                    Kingdom other = war.main_attacker == c.Core ? war.main_defender : war.main_defender == c.Core ? war.main_attacker : null;
                    if (other?.data != null && !other.isRekt()) yield return (other, other, null);
                }
                break;
            case "members":
                foreach (Kingdom member in (c.Empire.kingdoms_list ?? new List<Kingdom>())
                         .Where(member => member?.data != null && !member.isRekt() && member != c.Core && !member.IsEmpire())
                         .OrderBy(member => member.id).ToList())
                    yield return (member, member, null);
                break;
            case "realm":
                foreach (Kingdom member in (c.Empire.kingdoms_list ?? new List<Kingdom>())
                         .Where(member => member?.data != null && !member.isRekt()).ToList())
                    yield return (member, member, null);
                break;
            case "border_foreign":
                foreach (Kingdom kingdom in ClaimPolicySystem.ForeignBorders(c.Empire).Select(pair => pair.foreign?.kingdom)
                             .Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).Distinct().ToList())
                    yield return (kingdom, kingdom, null);
                break;
            default:
                if (pool != "foreign" && Reported.Add("pool:" + pool))
                    LogService.LogWarning($"[EmpireCraft][诉求目标] 不认识的候选来源 {pool}，按 foreign 算");
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom?.data == null || kingdom.isRekt() || kingdom == c.Core || kingdom.GetEmpire() == c.Empire) continue;
                    yield return (kingdom, kingdom, null);
                }
                break;
        }
    }

    private static void CollectKingdoms(Context c, Spec spec, int mode, List<ClaimTargetOption> result)
    {
        bool auto = mode != ModeAll, autoRules = mode == ModeAuto;
        var seen = new HashSet<long>();
        foreach ((Kingdom subject, Kingdom target, ClaimTargetOption source) in KingdomPool(c, spec.Pool))
        {
            string failed = source != null && !source.Selectable ? "builtin" : FirstFailed(spec, autoRules, rule => PassKingdom(c, rule, subject));
            float theirs = -1f;
            if (failed == null && (spec.OurPowerMin > 0f || spec.TheirPowerMin > 0f))
            {
                theirs = (float)WarSituation.Power(subject);
                if (spec.OurPowerMin > 0f && c.Ours < theirs * spec.OurPowerMin) failed = "our_power";
                else if (spec.TheirPowerMin > 0f && theirs < c.Ours * spec.TheirPowerMin) failed = "their_power";
            }
            if (failed == null && (spec.ArmyShareMin > 0f || spec.ArmyShareMax > 0f))
            {
                float share = subject.countTotalWarriors() / c.Army;
                if (spec.ArmyShareMin > 0f && share < spec.ArmyShareMin) failed = "army_share_min";
                else if (spec.ArmyShareMax > 0f && share > spec.ArmyShareMax) failed = "army_share_max";
            }
            if (failed == null && spec.MinCities > 0 && (subject.cities?.Count ?? 0) < spec.MinCities) failed = "min_cities";
            if (auto && failed != null) continue;
            // 同一个目标(同一帝国的几个敌国记到同一个朝廷)只列一回：有过关的那个算数
            if (!seen.Add(target.id))
            {
                ClaimTargetOption listed = result.FirstOrDefault(option => option.Type == MetaType.Kingdom && option.Kingdom == target);
                if (failed != null || listed == null || listed.Selectable) continue;
                result.Remove(listed);
            }
            var option = new ClaimTargetOption { Type = MetaType.Kingdom, Kingdom = target, Selectable = failed == null };
            if (failed != null) option.Why = FailText(spec, failed);
            else
            {
                var tags = new List<string>();
                option.Score = ScoreKingdom(c, spec, subject, ref theirs, tags);
                option.Why = string.Join(" · ", tags);
            }
            if (!auto)
            {
                // 本朝的成员写城数，外国写国力比
                if (subject.GetEmpire() == c.Empire) option.Figure = string.Format(LM.Get("claim_target_cities"), subject.cities?.Count ?? 0);
                else
                {
                    if (theirs < 0f) theirs = (float)WarSituation.Power(subject);
                    float ratio = c.Ours / Math.Max(1f, theirs);
                    option.Figure = string.Format(LM.Get("claim_target_ratio"), ratio);
                    option.FigureHex = ratio >= 1.2f ? Green : ratio >= 0.8f ? Gold : Red;
                }
            }
            Inherit(option, source);
            result.Add(option);
            if (mode == ModeProbe || autoRules && spec.Prefer.Count == 0) return;
        }
    }

    private static bool IsMilitaryCommand(Kingdom k) =>
        k.GetKingdomType() is KingdomType.LvLing_jiedushi or KingdomType.LvLing_duhufu;

    private static bool PassKingdom(Context c, string rule, Kingdom k)
    {
        switch (rule)
        {
            case "neighbour": return c.Empire.IsNeighbourWith(k);
            case "neighbour_or_strait": return c.Empire.IsNeighbourWith(k) || StraitReach.Across(c.Core, k);
            case "war_reach": return c.Near.Contains(k) || StraitReach.Across(c.Core, k);
            case "not_at_war": return !k.isInWarWith(c.Core);
            case "not_deterred": return !EmpireCraft.Scripts.Compatibility.NuclearDoctrineSystem.IsDeterred(c.Core, k);
            case "can_declare": return WarGate.CanDeclare(c.Core, k);
            case "opinion_not_good": return !c.Core.isOpinionTowardsKingdomGood(k);
            case "steppe_peace_allowed": return SteppeOrderSystem.PeacetimeWarAllowed(c.Core, k);
            case "not_in_empire": return !k.IsInEmpire();
            case "not_tributary": return !c.Empire.taken_Kingdoms.Contains(k);
            case "not_overlord": return !c.Empire.given_Kingdoms.Contains(k);
            case "other_species": return k.species_id != c.Core.species_id;
            case "not_zhou_vassal": return !ZhouEraSystem.IsDetachedVassal(k);
            case "tribute_reach": return TributaryRelations.InReach(k, c.Empire);
            case "no_pact": return c.Empire.data?.diplomatic_pacts == null || !c.Empire.data.diplomatic_pacts.ContainsKey(k.id);
            case "no_market": return !NomadRaidSystem.MarketTreaty(c.Core, k);
            case "not_tribute_payer": return !k.HasGivenAlliance();
            case "losing_war": return LosingWar(c.Core, k);
            case "losing_war_badly": return LosingWarBadly(c.Core, k);
            case "has_city_to_cede": return CedeCity(c.Empire, k) != null;
            case "negotiable_war": return NegotiableWarWith(c.Core, k) != null;
            case "has_admin_title": return k.GetAdministrativeTitle() != null;
            case "no_assimilation_duty":
                CultureService.UpdateCulturalAssimilationDuty(k);
                return !k.GetOrCreate().cultural_assimilation_duty;
            case "needs_assimilation": return CultureService.AdministrationNeedsCulturalAssimilation(k, c.Culture);
            case "other_religion": return k.religion != c.Empire.Religion;
            // 封建帝国的诸侯
            case "has_king": return k.hasKing();
            case "secular_prince": return HolyRomanSystem.SecularPrince(k);
            case "diplomacy_locked": return k.GetRegime() is Regime locked && !locked.IsAllowDiplomacy();
            case "diplomacy_free": return k.GetRegime() is Regime free && free.IsAllowDiplomacy();
            case "needs_privilege":
                return k.GetRegime() is Regime regime && (!regime.IsAllowDiplomacy() || regime.IsAllowSupportCenterArmy());
            // 削藩、撤销军府
            case "has_crime": return c.Claim.TargetCrimeOk(k);
            case "not_faction_rebelling": return !k.IsFactionRebelling();
            case "at_peace": return !k.getWars().Any();
            case "no_enemies": return !k.hasEnemies();
            case "renown_enough": return c.Claim.OwnEnoughRenown(k);
            case "king_defiant": return (k.king?.GetViolateValue() ?? 0) >= 90;
            // 政教合一的成员(教会领地)不动；世袭的埃米尔照动，圣城之主除外
            case "not_church_land":
                return k.GetRegime()?.GetReligionLevel() != ReligionLevel.High ||
                       EmirateSystem.IsHereditaryEmir(c.Empire, k) && !PapacySystem.HoldsHolyCity(k);
            case "hereditary_ruler": return k.GetRegime()?.GetLeaderSelectMethod() == LeaderSelectMethod.Succession;
            case "is_military_command": return IsMilitaryCommand(k);
            case "not_military_command": return !IsMilitaryCommand(k);
            case "not_kingdom_fief": return k.GetKingdomType() != KingdomType.LvLing_kingdom;
            case "is_border": return k.IsBorder();
            default: return Unknown(rule);
        }
    }

    private static float ScoreKingdom(Context c, Spec spec, Kingdom k, ref float theirs, List<string> tags)
    {
        float score = 0f;
        foreach ((string by, float weight) in spec.Prefer)
        {
            switch (by)
            {
                case "dejure_claim":
                    if (!DeJureAgenda.HasClaimOn(c.Core, k)) break;
                    score += weight;
                    tags.Add(LM.Get("claim_target_tag_dejure_claim"));
                    break;
                case "grudge":
                    int grudge = Math.Max(0, -DiplomacyActions.HistoryOpinion(c.Core, k));
                    score += weight * grudge;
                    if (grudge > 0) tags.Add(string.Format(LM.Get("claim_target_tag_grudge"), grudge));
                    break;
                case "power_ratio":
                    if (theirs < 0f) theirs = (float)WarSituation.Power(k);
                    score += weight * c.Ours / Math.Max(1f, theirs);
                    break;
                case "their_power":
                    if (theirs < 0f) theirs = (float)WarSituation.Power(k);
                    score += weight * theirs / Math.Max(1f, c.Ours);
                    break;
                case "at_war":
                    if (!k.isInWarWith(c.Core)) break;
                    score += weight;
                    tags.Add(LM.Get("claim_target_tag_at_war"));
                    break;
                case "nomad":
                    if (!NomadRaidSystem.IsNomad(k)) break;
                    score += weight;
                    tags.Add(LM.Get("claim_target_tag_nomad"));
                    break;
                case "other_culture":
                    if (CultureService.GetRealmCulture(k) == CultureService.GetRealmCulture(c.Core)) break;
                    score += weight;
                    tags.Add(LM.Get("claim_target_tag_other_culture"));
                    break;
                case "cities":
                    score += weight * (k.cities?.Count ?? 0);
                    break;
                case "army":
                    score += weight * k.countTotalWarriors();
                    break;
                case "population_total":
                    score += weight * k.getPopulationTotal();
                    break;
                case "appointed":
                    if (k.GetRegime()?.GetLeaderSelectMethod() == LeaderSelectMethod.Succession) break;
                    score += weight;
                    tags.Add(LM.Get("claim_target_tag_appointed"));
                    break;
                case "war_years":
                    War war = NegotiableWarWith(c.Core, k) ?? WarSituation.FindWar(c.Core, k);
                    if (war == null) break;
                    float years = WarSituation.WarYears(war);
                    score += weight * years;
                    tags.Add(string.Format(LM.Get("claim_target_tag_war_years"), years));
                    break;
                default:
                    Unknown(by);
                    break;
            }
        }
        return score;
    }

    // 战事吃紧(提供岁币)：打了一年以上，丢了城且敌强两成，或敌强八成，或敌强五成又打了三年
    public static bool LosingWar(Kingdom core, Kingdom enemy)
    {
        War war = WarSituation.FindWar(core, enemy);
        if (war == null) return false;
        float years = WarSituation.WarYears(war);
        if (years < 1f) return false;
        int lost = WarSituation.CitiesLost(war, core);
        float ratio = WarSituation.StrengthRatio(core, enemy);
        return lost >= 1 && ratio >= 1.2f || ratio >= 1.8f || ratio >= 1.5f && years >= 3f;
    }

    // 战事危急(割让城池)：比岁币的门槛高——打了一年以上，丢两城且敌强两成，或丢一城且敌强八成，或敌强三倍
    public static bool LosingWarBadly(Kingdom core, Kingdom enemy)
    {
        War war = WarSituation.FindWar(core, enemy);
        if (war == null) return false;
        float years = WarSituation.WarYears(war);
        if (years < 1f) return false;
        int lost = WarSituation.CitiesLost(war, core);
        float ratio = WarSituation.StrengthRatio(core, enemy);
        if (lost < 0) return ratio >= 2.5f && years >= 2f;
        return lost >= 2 && ratio >= 1.2f || lost >= 1 && ratio >= 1.8f || ratio >= 3f;
    }

    // 割给敌国的城(割让城池)：与敌国接壤、人口最少的非都城城市
    public static City CedeCity(Empire empire, Kingdom enemy) => empire?.cities_list?
        .Where(city => city != null && !city.isRekt() && !city.isCapitalCity() && city.neighbours_cities != null &&
                       city.neighbours_cities.Any(other => other != null && other.kingdom == enemy))
        .OrderBy(city => city.getPopulationPeople())
        .FirstOrDefault();

    // 两国作主战方、眼下谈得了的那场仗(罢兵议和)：议和台用得上，不是剧本定死的仗、总体战、统一战争、内战，打满了起码的年头
    public static War NegotiableWarWith(Kingdom core, Kingdom other)
    {
        if (core == null || other == null) return null;
        War best = null;
        foreach (War war in core.getWars())
        {
            if (war == null || !(war.main_attacker == core && war.main_defender == other || war.main_defender == core && war.main_attacker == other) ||
                !Negotiable(war)) continue;
            if (best == null || WarSituation.WarYears(war) > WarSituation.WarYears(best)) best = war;
        }
        return best;
    }

    public static bool Negotiable(War war) =>
        war != null && PeaceTable.Usable(war) && !war.GetOrCreate().scripted_war && !war.isTotalWar() && !UnificationSystem.IsUnificationWar(war) &&
        !RulerTraitSystem.IsFounderUnificationWar(war) && !WarlordEraSystem.IsCivilWar(war) &&
        WarSituation.WarYears(war) >= WarPeaceRules.MinWarYears;

    #endregion

    #region 城

    private static void CollectCities(Context c, Spec spec, int mode, List<ClaimTargetOption> result)
    {
        bool auto = mode != ModeAll, autoRules = mode == ModeAuto;
        var pool = new List<(City city, ClaimTargetOption source)>();
        if (spec.Pool == "builtin")
            pool.AddRange(BuiltinOf(c, MetaType.City).Select(option => (option.City, option)));
        else
        {
            if (spec.Pool != "core_cities" && Reported.Add("pool:" + spec.Pool))
                LogService.LogWarning($"[EmpireCraft][诉求目标] 不认识的候选来源 {spec.Pool}，按 core_cities 算");
            if (c.Core.cities != null) pool.AddRange(c.Core.cities.OrderBy(city => city?.id ?? 0L).Select(city => (city, (ClaimTargetOption)null)));
        }
        if (spec.Rules.Contains("no_assimilation_duty")) CultureService.UpdateCulturalAssimilationDuty(c.Core);
        foreach ((City city, ClaimTargetOption source) in pool)
        {
            if (city?.data == null || city.isRekt()) continue;
            string failed = source != null && !source.Selectable ? "builtin" : FirstFailed(spec, autoRules, rule => PassCity(c, rule, city));
            if (auto && failed != null) continue;
            var option = new ClaimTargetOption { Type = MetaType.City, City = city, Selectable = failed == null };
            if (failed != null) option.Why = FailText(spec, failed);
            else
            {
                foreach ((string by, float weight) in spec.Prefer)
                {
                    if (by == "population") option.Score += weight * city.getPopulationPeople();
                    else Unknown(by);
                }
                if (!auto)
                {
                    string culture = CultureService.GetMainCulture(city, initialize: false);
                    Kingdom papacy = spec.Rules.Contains("borders_papacy") ? PapacyNear(c.Empire, city) : null;
                    option.Why = papacy != null ? string.Format(LM.Get("claim_target_tag_papacy"), papacy.GetKingdomName())
                        : source == null && CultureService.IsValidCulture(culture) ? string.Format(LM.Get("claim_target_tag_culture"), culture.GetCultureTranslate()) : "";
                }
            }
            if (!auto) option.Figure = string.Format(LM.Get("claim_target_population"), city.getPopulationPeople());
            Inherit(option, source);
            result.Add(option);
            if (mode == ModeProbe || autoRules && spec.Prefer.Count == 0) return;
        }
    }

    private static bool PassCity(Context c, string rule, City city)
    {
        switch (rule)
        {
            case "not_capital": return city != c.Core.capital && !city.isCapitalCity();
            case "borders_papacy": return PapacyNear(c.Empire, city) != null;
            case "no_assimilation_duty": return !city.GetOrCreate().cultural_assimilation_duty;
            case "needs_assimilation": return CultureService.CityNeedsCulturalAssimilation(city, c.Culture);
            default: return Unknown(rule);
        }
    }

    // 与这座城接壤、与本朝同教的教宗国(划地给教廷)
    public static Kingdom PapacyNear(Empire empire, City city)
    {
        if (empire?.Religion == null || empire.Religion.isRekt() || city?.neighbours_cities == null) return null;
        foreach (City other in city.neighbours_cities)
        {
            Kingdom kingdom = other?.kingdom;
            if (kingdom != null && !kingdom.isRekt() && kingdom != city.kingdom &&
                kingdom.GetKingdomType() == KingdomType.Feudalism_papal_state && kingdom.religion == empire.Religion) return kingdom;
        }
        return null;
    }

    #endregion

    #region 人物

    private static void CollectActors(Context c, Spec spec, int mode, List<ClaimTargetOption> result)
    {
        bool auto = mode != ModeAll, autoRules = mode == ModeAuto;
        var pool = new List<(Actor actor, ClaimTargetOption source)>();
        if (spec.Pool == "builtin")
            pool.AddRange(BuiltinOf(c, MetaType.Unit).Select(option => (option.Actor, option)));
        else if (spec.Pool == "emperor_siblings")
        {
            PersonalClanIdentity emperor = c.Empire.Emperor?.GetPersonalIdentity();
            if (emperor != null)
                pool.AddRange(SpecificClanManager.GetSiblingsWithRelation(emperor)
                    .Select(item => item.Item2).Where(identity => identity != null).OrderBy(identity => identity.rank)
                    .Select(identity => (identity._actor, (ClaimTargetOption)null)));
        }
        else
        {
            if (spec.Pool != "heir_candidates" && Reported.Add("pool:" + spec.Pool))
                LogService.LogWarning($"[EmpireCraft][诉求目标] 不认识的候选来源 {spec.Pool}，按 heir_candidates 算");
            pool.AddRange(EmpireCraftKingdomBehCheckHeir.HeirCandidates(c.Core).Select(actor => (actor, (ClaimTargetOption)null)));
        }
        var seen = new HashSet<long>();
        foreach ((Actor actor, ClaimTargetOption source) in pool)
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !seen.Add(actor.id)) continue;
            string failed = source != null && !source.Selectable ? "builtin" : FirstFailed(spec, autoRules, rule => PassActor(c, rule, actor));
            if (auto && failed != null) continue;
            var option = new ClaimTargetOption { Type = MetaType.Unit, Actor = actor, Selectable = failed == null };
            if (failed != null) option.Why = FailText(spec, failed);
            else
            {
                var tags = new List<string>();
                foreach ((string by, float weight) in spec.Prefer)
                {
                    switch (by)
                    {
                        case "own_faction":
                            if (c.Faction == null || actor.GetFaction() != c.Faction) break;
                            option.Score += weight;
                            tags.Add(LM.Get("claim_target_tag_own_faction"));
                            break;
                        case "age":
                            option.Score += weight * actor.getAge();
                            break;
                        default:
                            Unknown(by);
                            break;
                    }
                }
                option.Why = string.Join(" · ", tags);
            }
            if (!auto) option.Figure = string.Format(LM.Get("claim_target_age"), actor.getAge());
            Inherit(option, source);
            result.Add(option);
            if (mode == ModeProbe || autoRules && spec.Prefer.Count == 0) return;
        }
    }

    private static bool PassActor(Context c, string rule, Actor actor)
    {
        switch (rule)
        {
            case "not_current_heir":
                return actor != c.Core.GetHeir() && c.Core.GetOrCreate().designated_heir_id != actor.getID();
            case "enfeoff_eligible": return EnfeoffmentHelper.IsEligibleSibling(actor, c.Empire);
            // 本派的；或没有派系、而现储君是别派的人(朝臣只推自己人，或借无党的皇子换下别派的储君)
            case "own_or_unaligned_vs_rival":
                FixedFaction faction = actor.GetFaction();
                if (faction != null) return faction == c.Faction;
                FixedFaction current = c.Core.GetHeir()?.GetFaction();
                return current != null && current != c.Faction;
            default: return Unknown(rule);
        }
    }

    #endregion

    #region 法理头衔、宗教(只有决议自带的候选)

    private static void CollectOthers(Context c, Spec spec, int mode, List<ClaimTargetOption> result)
    {
        if (spec.Pool != "builtin" && Reported.Add("pool:" + spec.Pool))
            LogService.LogWarning($"[EmpireCraft][诉求目标] 头衔、宗教只有决议自带的候选(builtin)，{spec.Pool} 不认识");
        foreach (string rule in spec.Rules.Concat(spec.AutoRules).Concat(spec.Prefer.Select(item => item.by))) Unknown(rule);
        foreach (ClaimTargetOption source in BuiltinOf(c, spec.Kind).ToList())
        {
            if (mode != ModeAll && !source.Selectable) continue;
            result.Add(source);
            if (mode != ModeAll) return;
        }
    }

    #endregion

    #region 小件

    private static string FirstFailed(Spec spec, bool auto, Func<string, bool> pass)
    {
        foreach (string rule in spec.Rules)
            if (!pass(rule)) return rule;
        if (auto)
            foreach (string rule in spec.AutoRules)
                if (!pass(rule)) return rule;
        return null;
    }

    private static string FailText(Spec spec, string failed)
    {
        switch (failed)
        {
            case "our_power": return string.Format(LM.Get("claim_target_fail_our_power"), spec.OurPowerMin);
            case "their_power": return string.Format(LM.Get("claim_target_fail_their_power"), spec.TheirPowerMin);
            case "army_share_min": return string.Format(LM.Get("claim_target_fail_army_share_min"), spec.ArmyShareMin);
            case "army_share_max": return string.Format(LM.Get("claim_target_fail_army_share_max"), spec.ArmyShareMax);
            case "min_cities": return string.Format(LM.Get("claim_target_fail_min_cities"), spec.MinCities);
            default:
                string key = "claim_target_fail_" + failed;
                return LM.Has(key) ? LM.Get(key) : failed;
        }
    }

    // 不认识的规则、加分项：记一条日志，当它不存在(规则算过关)
    private static bool Unknown(string name)
    {
        if (Reported.Add("name:" + name))
            LogService.LogWarning($"[EmpireCraft][诉求目标] ClaimRules.json 里有不认识的规则或加分项：{name}(当它没写)");
        return true;
    }

    #endregion
}
