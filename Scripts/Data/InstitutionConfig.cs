using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Regimes;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.Data;

// 全局制度配置，来自 InstitutionTrees/Settings.json。
//
// 文明等级门槛做成全局共用（而不是每条线各定一套）的原因有两个：一是排行榜要把不同线的
// 文化横向摆在一起比，门槛不一致的话"等级"这个数没有可比性；二是等级同时还是"能否吸收
// 外来制度"的判定基准（只有节点等级高于本文化文明等级才能吸收），基准必须是同一把尺子。
public sealed class InstitutionGlobalConfig
{
    public int schema_version = 3;
    public bool ai_enabled = true;
    // 文化没配 institution_line、或者配了个不存在的线时，兜底用这条线
    public string default_line = "Roma";
    public List<InstitutionCultureLevelConfig> culture_levels = new();
    public InstitutionAbsorptionRuleConfig absorption = new();
    public InstitutionSocialUnrestConfig social_unrest = new();
    public InstitutionReformCompetitionConfig reform_competition = new();
    // 君主立宪 / 资本主义萌芽的全局规则。每条线可在自己的 json 里用 constitution 覆盖其中的线相关项。
    public ConstitutionConfig constitution = new();
}

// 资本主义萌芽与君主立宪。
//
// 这里只写"规则"，不写"哪条线的哪个节点"：立宪需要的制度基础用 required_features 表达，
// 任何节点只要在 features 里声明了对应的特性（本线自研的、从别的线吸收的、或者从公共模板
// 实例化出来的都算）就满足条件。所以加一条新线、或者让别的文明也能立宪，只需要改配置。
public sealed class ConstitutionConfig
{
    public bool enabled = true;
    // 发起立宪改革所需的制度特性，全部具备才行。线的 json 可以整体覆盖这张表。
    // Replace：JSON 里写了就整体替换默认值，而不是追加在默认值后面
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> required_features = new() { "constitution_basis_administration", "constitution_basis_fiscal" };
    // 是否要求先完成多元帝国整合。线的 json 可以覆盖。
    public bool requires_composite_empire = false;
    public int stable_culture_years = 50;

    // —— 资本主义萌芽 ——
    public int trade_window_years = 10;
    public int max_gold_per_voyage = 5;
    public int budding_years = 5;
    public int budding_decline_years = 3;
    public int minimum_merchant_households = 2;
    // 商人家户占全部家户的最低比例
    public float minimum_merchant_ratio = 0.05f;
    // 没有近期海贸时，需要更多商人家户、并且分布在多座城市
    public int no_trade_merchant_households = 4;
    public int no_trade_merchant_cities = 2;

    // —— 议会与改革 ——
    public float support_threshold = 50f;
    public float ai_start_support = 55f;
    public int ai_attempt_interval_years = 3;
    public int deadlock_notice_years = 5;
    public int start_mandate_cost = 5;
    public float noble_grievance_on_start = 8f;
    // 达到这个改革阶段(1~4)后议会取得征税同意权 / 内阁向议会负责
    public int assembly_tax_power_stage = 2;
    public int responsible_cabinet_stage = 3;
    public int minimum_cabinet_size = 3;
    public int default_cabinet_size = 5;

    // —— 派系对立宪的态度 ——
    // 派系意识形态的基础倾向；没列出的派系为 0。
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<FactionType, float> faction_stances = new()
    {
        [FactionType.自治] = 20f, [FactionType.绥靖] = 20f, [FactionType.共和] = 20f,
        [FactionType.民主] = 20f, [FactionType.融入] = 20f, [FactionType.诸侯] = 20f,
        [FactionType.僭主] = -10f,
        [FactionType.中央] = -20f, [FactionType.神权] = -20f, [FactionType.攘夷] = -20f,
        [FactionType.尊王] = -20f, [FactionType.血脉] = -20f, [FactionType.同化] = -20f
    };
    // 萌芽出现后，不敌视商人的派系额外获得的支持倾向
    public float budding_merchant_bonus = 12f;
    public float merchant_affinity_weight = 0.15f;
    // 倾向 + 商人亲和 + 商人好感 超过这个值才算支持立宪
    public float support_baseline = 50f;
}

// 线级别的立宪覆盖项。字段为 null 表示沿用 Settings.json 的全局值。
public sealed class InstitutionLineConstitutionConfig
{
    public bool? enabled;
    public List<string> required_features;
    public bool? requires_composite_empire;
}

// 改革扩散速度：封闭的单一帝国最慢；同文明国家增多会带来模仿，多帝国并立会形成强烈
// 制度竞争；与更先进的异文化帝国接壤则会产生额外的外部示范压力。
public sealed class InstitutionReformCompetitionConfig
{
    public bool enabled = true;
    public int isolated_duration_years = 50;
    public float country_bonus_per_additional = 0.025f;
    public float country_bonus_cap = 0.25f;
    public float empire_bonus_per_additional = 0.35f;
    public float empire_bonus_cap = 1.05f;
    public float advanced_border_bonus = 1.0f;
    public float maximum_speed_multiplier = 3.0f;
}

// 已施行制度不会只在改革当年产生反对。受损阶层会长期积累怨气，制度倒退时原受益阶层
// 也会受到一次冲击；数值放在全局配置里，方便模组包按自己的年代节奏调整。
public sealed class InstitutionSocialUnrestConfig
{
    public bool enabled = true;
    public float opposition_target_multiplier = 40f;
    public float support_relief_multiplier = 3f;
    public float annual_adjustment_rate = 0.16f;
    public float reform_completion_shock = 14f;
    public float regression_shock = 24f;
    public float rebellion_threshold = 75f;
    public int rebellion_cooldown_years = 12;
}

public sealed class InstitutionAbsorptionRuleConfig
{
    public bool enabled = true;
    // 0 = 不限制跨级幅度（按字面规则：只要节点等级高于本文化文明等级就能吸收）。
    // 设成 1 就变成"最多只能吸收比自己文明等级高一级的制度"，避免 1 级文化直接嫁接 5 级制度。
    public int max_tier_gap = 0;
    // 每年每条接触渠道累计的接触度（会乘上该渠道的权重）
    public float exposure_per_year = 10f;
    public float minimum_exposure = 30f;
    public int minimum_contact_years = 3;
    // 四种接触渠道的权重
    public float same_empire_weight = 1.0f;            //同属一个帝国
    public float land_border_weight = 0.5f;            //陆地边境相邻
    public float alliance_or_tributary_weight = 0.7f;  //同盟 / 朝贡、岁币关系
    public float occupied_city_weight = 1.2f;          //城市被异文化政权统治（占领融合）
}

public sealed class InstitutionCultureLevelConfig
{
    public int level = 1;
    public string name_key = "institution_culture_level_1";
    public int minimum_advancement = 0;
}

// 一条科技线，来自 InstitutionTrees/<线 id>.json。
//
// 线 id 就是文件名（不含扩展名），是个纯字符串而不是 C# 枚举——目的是让加一条新线
// 完全不用改代码：往 InstitutionTrees/ 里丢一个新的 json，再在 CultureRulesConfig.json
// 里把某些文化的 setting.institution_line 指到这个 id 上，就多出一条可玩的线。
public sealed class InstitutionTreeConfig
{
    [JsonIgnore] public string line = "";
    public string name_key = "";
    // 该线所有文化开局即持有的根节点。根节点不带任何效果，只用来当树根：
    // 一是画树状图时有个统一的起点，二是让文明等级从 1 级起步而不是 0 级。
    // 除根节点以外，开局一律是初始状态，任何制度都得自己研究或从别的线吸收。
    public List<string> root_nodes = new();
    public List<InstitutionNodeConfig> nodes = new();
    // 本线节点没有逐项配置 resistance.reason_key、也匹配不到分支/派系兜底时使用的反对理由。
    public string default_resistance_reason_key = "";
    // 本线的立宪规则覆盖（可选）
    public InstitutionLineConstitutionConfig constitution;
}

// 公共制度模板，来自 InstitutionTrees/Common/*.json。
//
// 模板本身不属于任何线、也不会单独出现在树上；线的 json 里写
//     { "template": "模板 id", "id": "本线实例 id", "requires": [...], ... }
// 就会以模板为底、用这里写出的字段覆盖（对象逐字段合并，数组整体替换），得到本线的一个节点。
// 同一个模板可以被任意多条线实例化，各线可以有不同的前置、等级和分支；
// 同源实例互相视为"同一项制度"：已掌握其中之一的文化不会再从别的线吸收另一个。
public sealed class InstitutionTemplateFileConfig
{
    public List<Newtonsoft.Json.Linq.JObject> templates = new();
}

public sealed class InstitutionNodeConfig
{
    public string id = "";
    public string name_key = "";
    public string description_key = "";
    public string branch = "administration";
    // 节点等级（1~5）。既是树状图的层级，也是吸收判定的基准。
    public int advancement = 1;
    // 某些游牧制度只有完成多元帝国整合后才具备实施条件。
    public bool requires_composite_empire = false;
    // 这个制度对应的政体形态（RegimeType 名，留空表示"不改变政体"）。
    //
    // 两个用途：①一条线的**根节点**（等级 1）声明的政体，就是该线所有文化的**初始政体**
    // —— 所以华夏开局是周制而不是律令制，律令要等郡县官僚（等级 5）研究出来；
    // ②任何节点施行完成时，如果它声明了政体且跟当前政体不同，就顺势完成政体变更
    // （不必再额外配一个 change_regime 效果，两处配置也就不会互相矛盾）。
    public string regime = "";
    // 前置：requires 里的必须全部施行；requires_any 不为空时，其中至少施行一个
    public List<string> requires = new();
    public List<string> requires_any = new();
    public List<string> replaces = new();
    public List<string> exclusive_with = new();
    public InstitutionResearchConfig research = new();
    public InstitutionPoliticsConfig politics = new();
    public InstitutionReformConfig reform = new();
    public InstitutionResistanceConfig resistance = new();
    public InstitutionAbsorbConfig absorb = new();
    public List<InstitutionEffectConfig> effects_on_start = new();
    public List<InstitutionEffectConfig> effects_on_complete = new();
    // 制度特性：key -> 数值。系统代码只查询特性，不认具体节点 id，
    // 因此一项效果可以由任意线、任意节点（包括公共模板）提供。
    // 同一特性在已掌握的多个节点上取最大值。已知特性见 InstitutionFeatures。
    public Dictionary<string, float> features = new();
    // 引用的公共模板 id（见 InstitutionTemplateFileConfig）。留空表示普通节点。
    public string template = "";

    // 由载入器按所在文件回填，不从 JSON 读
    [JsonIgnore] public string line = "";
    // 等价分组：模板实例为模板 id，普通节点为自身 id
    [JsonIgnore] public string equivalence_key = "";
}

// 代码里会查询的制度特性。新增一个系统级效果时在这里登记 key，并在节点 json 的 features 里声明。
public static class InstitutionFeatures
{
    // 立宪所需的制度基础（具体需要哪几项由 constitution.required_features 决定）
    public const string ConstitutionBasisAdministration = "constitution_basis_administration";
    public const string ConstitutionBasisFiscal = "constitution_basis_fiscal";
    public const string ConstitutionBasisLaw = "constitution_basis_law";
    public const string ConstitutionBasisCommerce = "constitution_basis_commerce";
    // 宗主对附庸的集权权威（0~100，取最高）
    public const string VassalAuthority = "vassal_authority";
    // 附庸自行继承（请封/自立）
    public const string VassalSelfSuccession = "vassal_self_succession";
    // 推恩令
    public const string GraceEdict = "grace_edict";
    // 郡国并行
    public const string CommanderyKingdom = "commandery_kingdom";
    // 新君即位时兄弟按法理裂土受封；被 SiblingEnfeoffmentAbolished 取消
    public const string SiblingEnfeoffment = "sibling_enfeoffment";
    public const string SiblingEnfeoffmentAbolished = "sibling_enfeoffment_abolished";
    // 城邦共和的首领由元老院推举
    public const string SenateElection = "senate_election";
    // 封建制下设立辖区/教区等直辖行政区
    public const string DirectAdministration = "direct_administration";
    // 神权国家（教宗国等神权诉求的前提）
    public const string TheocraticState = "theocratic_state";
    // 派系诉求"开科取士""转天朝制度"要推动的改革目标
    public const string ClaimReformPrefix = "claim_reform:";
}

public sealed class InstitutionResearchConfig
{
    public int mandate_required = 0;
}

public sealed class InstitutionPoliticsConfig
{
    public Dictionary<SocialClass, float> support_classes = new();
    public Dictionary<SocialClass, float> oppose_classes = new();
    public Dictionary<FactionType, float> support_factions = new();
    public Dictionary<FactionType, float> oppose_factions = new();
    // 非空时，只有这些类型的派系主导朝政才能发起这项改革，发起者也固定是该派系
    public List<FactionType> sponsor_only = new();
}

public sealed class InstitutionReformConfig
{
    public float base_progress_per_year = 8f;
    public int minimum_years = 5;
    public bool allow_force = true;
    public float force_radicalism = 20f;
}

public sealed class InstitutionResistanceConfig
{
    public float base_radicalism = 5f;
    public float vested_interest_radicalism = 15f;
    public float rebellion_threshold = 75f;
    public int opposition_faction_growth = 1;
    // 改革反对派发动叛乱时写入历史的具体理由。节点未配置时使用通用理由。
    public string reason_key = "institution_resistance_reason_generic";
}

// 这个节点能不能被别的线的文化吸收。-1 表示沿用 Settings.json 里的全局门槛。
public sealed class InstitutionAbsorbConfig
{
    public bool enabled = true;
    public float minimum_exposure = -1f;
    public int minimum_contact_years = -1;
}

public sealed class InstitutionEffectConfig
{
    public string type = "";
    public string value = "";
    public string option = "";
    public int int_value = 0;
    public FactionType faction = FactionType.无;
}

public static class InstitutionConfigNormalizer
{
    public static void Normalize(InstitutionGlobalConfig config)
    {
        if (config == null) return;
        config.default_line = string.IsNullOrWhiteSpace(config.default_line) ? "Roma" : config.default_line.Trim();
        config.absorption ??= new InstitutionAbsorptionRuleConfig();
        config.social_unrest ??= new InstitutionSocialUnrestConfig();
        config.reform_competition ??= new InstitutionReformCompetitionConfig();
        config.constitution ??= new ConstitutionConfig();
        Normalize(config.constitution);
        config.absorption.max_tier_gap = global::System.Math.Max(0, config.absorption.max_tier_gap);
        config.absorption.exposure_per_year = global::System.Math.Max(0.1f, config.absorption.exposure_per_year);
        config.absorption.minimum_exposure = global::System.Math.Max(0f, config.absorption.minimum_exposure);
        config.absorption.minimum_contact_years =
            global::System.Math.Max(0, config.absorption.minimum_contact_years);
        config.social_unrest.opposition_target_multiplier =
            global::System.Math.Max(0f, config.social_unrest.opposition_target_multiplier);
        config.social_unrest.support_relief_multiplier =
            global::System.Math.Max(0f, config.social_unrest.support_relief_multiplier);
        config.social_unrest.annual_adjustment_rate = global::System.Math.Max(0.01f,
            global::System.Math.Min(1f, config.social_unrest.annual_adjustment_rate));
        config.social_unrest.reform_completion_shock =
            global::System.Math.Max(0f, config.social_unrest.reform_completion_shock);
        config.social_unrest.regression_shock = global::System.Math.Max(0f, config.social_unrest.regression_shock);
        config.social_unrest.rebellion_threshold = global::System.Math.Max(1f,
            global::System.Math.Min(100f, config.social_unrest.rebellion_threshold));
        config.social_unrest.rebellion_cooldown_years =
            global::System.Math.Max(1, config.social_unrest.rebellion_cooldown_years);
        config.reform_competition.isolated_duration_years =
            global::System.Math.Max(1, config.reform_competition.isolated_duration_years);
        config.reform_competition.country_bonus_per_additional =
            global::System.Math.Max(0f, config.reform_competition.country_bonus_per_additional);
        config.reform_competition.country_bonus_cap =
            global::System.Math.Max(0f, config.reform_competition.country_bonus_cap);
        config.reform_competition.empire_bonus_per_additional =
            global::System.Math.Max(0f, config.reform_competition.empire_bonus_per_additional);
        config.reform_competition.empire_bonus_cap =
            global::System.Math.Max(0f, config.reform_competition.empire_bonus_cap);
        config.reform_competition.advanced_border_bonus =
            global::System.Math.Max(0f, config.reform_competition.advanced_border_bonus);
        config.reform_competition.maximum_speed_multiplier = global::System.Math.Max(1f,
            config.reform_competition.maximum_speed_multiplier);
        config.culture_levels ??= new List<InstitutionCultureLevelConfig>();
        config.culture_levels.RemoveAll(level => level == null);
        foreach (InstitutionCultureLevelConfig level in config.culture_levels)
        {
            level.level = global::System.Math.Max(1, level.level);
            level.minimum_advancement = global::System.Math.Max(0, level.minimum_advancement);
            level.name_key ??= $"institution_culture_level_{level.level}";
        }
        if (config.culture_levels.Count == 0) config.culture_levels.AddRange(DefaultCultureLevels());
    }

    public static void Normalize(ConstitutionConfig config)
    {
        if (config == null) return;
        config.required_features ??= new List<string>();
        config.required_features.RemoveAll(string.IsNullOrWhiteSpace);
        config.faction_stances ??= new Dictionary<FactionType, float>();
        config.stable_culture_years = global::System.Math.Max(0, config.stable_culture_years);
        config.trade_window_years = global::System.Math.Max(1, config.trade_window_years);
        config.max_gold_per_voyage = global::System.Math.Max(1, config.max_gold_per_voyage);
        config.budding_years = global::System.Math.Max(0, config.budding_years);
        config.budding_decline_years = global::System.Math.Max(0, config.budding_decline_years);
        config.minimum_merchant_households = global::System.Math.Max(1, config.minimum_merchant_households);
        config.minimum_merchant_ratio = global::System.Math.Max(0f, global::System.Math.Min(1f, config.minimum_merchant_ratio));
        config.no_trade_merchant_households = global::System.Math.Max(1, config.no_trade_merchant_households);
        config.no_trade_merchant_cities = global::System.Math.Max(1, config.no_trade_merchant_cities);
        config.support_threshold = global::System.Math.Max(0f, global::System.Math.Min(100f, config.support_threshold));
        config.ai_start_support = global::System.Math.Max(config.support_threshold,
            global::System.Math.Min(100f, config.ai_start_support));
        config.ai_attempt_interval_years = global::System.Math.Max(1, config.ai_attempt_interval_years);
        config.deadlock_notice_years = global::System.Math.Max(1, config.deadlock_notice_years);
        config.assembly_tax_power_stage = global::System.Math.Max(1, global::System.Math.Min(4, config.assembly_tax_power_stage));
        config.responsible_cabinet_stage = global::System.Math.Max(1, global::System.Math.Min(4, config.responsible_cabinet_stage));
        config.minimum_cabinet_size = global::System.Math.Max(1, config.minimum_cabinet_size);
        config.default_cabinet_size = global::System.Math.Max(config.minimum_cabinet_size, config.default_cabinet_size);
    }

    public static List<InstitutionCultureLevelConfig> DefaultCultureLevels()
    {
        return new List<InstitutionCultureLevelConfig>
        {
            new() { level = 1, name_key = "institution_culture_level_1", minimum_advancement = 0 },
            new() { level = 2, name_key = "institution_culture_level_2", minimum_advancement = 5 },
            new() { level = 3, name_key = "institution_culture_level_3", minimum_advancement = 9 },
            new() { level = 4, name_key = "institution_culture_level_4", minimum_advancement = 14 }
        };
    }

    public static void Normalize(InstitutionTreeConfig tree)
    {
        if (tree == null) return;
        tree.line ??= "";
        tree.name_key = string.IsNullOrWhiteSpace(tree.name_key)
            ? $"institution_line_{tree.line.ToLower()}"
            : tree.name_key;
        tree.root_nodes ??= new List<string>();
        tree.nodes ??= new List<InstitutionNodeConfig>();
        foreach (InstitutionNodeConfig node in tree.nodes)
        {
            if (node == null) continue;
            node.line = tree.line;
            node.id ??= "";
            node.name_key ??= "";
            node.description_key ??= "";
            node.branch = string.IsNullOrWhiteSpace(node.branch) ? "administration" : node.branch;
            node.advancement = global::System.Math.Max(1, node.advancement);
            node.regime = (node.regime ?? "").Trim();
            node.requires ??= new List<string>();
            node.requires_any ??= new List<string>();
            node.replaces ??= new List<string>();
            node.exclusive_with ??= new List<string>();
            node.research ??= new InstitutionResearchConfig();
            node.politics ??= new InstitutionPoliticsConfig();
            node.politics.support_classes ??= new Dictionary<SocialClass, float>();
            node.politics.oppose_classes ??= new Dictionary<SocialClass, float>();
            node.politics.support_factions ??= new Dictionary<FactionType, float>();
            node.politics.oppose_factions ??= new Dictionary<FactionType, float>();
            node.politics.sponsor_only ??= new List<FactionType>();
            node.reform ??= new InstitutionReformConfig();
            node.resistance ??= new InstitutionResistanceConfig();
            node.resistance.reason_key = string.IsNullOrWhiteSpace(node.resistance.reason_key)
                ? "institution_resistance_reason_generic"
                : node.resistance.reason_key.Trim();
            node.absorb ??= new InstitutionAbsorbConfig();
            node.effects_on_start ??= new List<InstitutionEffectConfig>();
            node.effects_on_complete ??= new List<InstitutionEffectConfig>();
            node.features ??= new Dictionary<string, float>();
            node.template = (node.template ?? "").Trim();
            node.equivalence_key = string.IsNullOrWhiteSpace(node.template) ? node.id : node.template;
        }
        tree.default_resistance_reason_key = (tree.default_resistance_reason_key ?? "").Trim();
    }
}
