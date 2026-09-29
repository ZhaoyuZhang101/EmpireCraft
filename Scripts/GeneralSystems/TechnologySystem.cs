using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems;

#region 配置

public sealed class TechResearchConfig
{
    public float base_per_city = 1f;
    public float per_population = 0.04f;
    public float per_library = 2f;
    public float per_temple = 0.5f;
    // 旧字段：已改为按接触扩散计算，保留只为兼容旧配置
    public float known_elsewhere_cost_multiplier = 0.6f;
    public float per_university = 3f;
    public float per_factory = 1f;
    // 处于战争中的城市研究产出打折
    public float war_penalty = 0.25f;
    // 文化规模：每多一座城科技费用增加多少，封顶多少
    public float cost_per_extra_city = 0.02f;
    public float cost_city_scaling_max = 1f;
    // 技术扩散：每有一个接触中的文化(接壤/交战/同一帝国)已掌握，费用降低多少，封顶多少
    public float diffusion_per_contact = 0.2f;
    public float diffusion_max = 0.5f;
    // 加速事件一次给多少比例的研究进度
    public float boost_fraction = 0.4f;
    // 军队换装：每座城每年最多给多少名士兵换装、持续几年、每人最多由国库补贴多少钱
    public int modernize_per_city = 8;
    public int modernize_years = 3;
    public int modernize_subsidy = 60;
    // 世界首创：达到这个时代的技术才写入世界日志(开局的原始技术谁都会，不值得记)
    public int world_first_min_tier = 3;
}

// 加速事件(文明六的"尤里卡")：本文化做到了某件事，对应技术立刻获得一部分研究进度
//   building_type  城里有 amount 座该类型建筑        resource  任一城市储有 amount 个该资源
//   population     全文化人口达到 amount            cities    城市数达到 amount
//   material       已发现该材料                      tech      已掌握另一项技术
//   institution_feature  已推行带该特性的制度(特性值至少为 amount，比如城市生产阶段 ≥ 2)
//   contact_knows  接触中的文化(接壤/交战/同一帝国)有人掌握这项技术
//   war_knows      正在交战的文化掌握这项技术(在战场上见识过)
public sealed class TechBoostConfig
{
    public string type = "";
    public string value = "";
    public int amount = 1;
}

public sealed class TechEraConfig
{
    public int tier;
    public string key = "";
}

public sealed class TechResourceRequirement
{
    public string id = "";
    public int amount = 1;
}

public sealed class TechMaterialConfig
{
    public string id = "";
    public int tier = 1;
    // 满足其中任意一条即可发现
    public List<TechResourceRequirement> resources = new();
    public List<string> requires_techs = new();
}

public sealed class TechNodeConfig
{
    public string id = "";
    public string branch = "science";
    public int tier = 1;
    public float cost = 10f;
    public List<string> requires_techs = new();
    public List<string> requires_materials = new();
    // 解锁该材质(ItemAsset.material)的所有装备
    public List<string> unlock_materials = new();
    // 解锁指定物品 id(用于别的模组的枪械等)
    public List<string> unlock_items = new();
    // 解锁建筑，支持 * 通配(如 6house_*_modernmod)
    public List<string> unlock_buildings = new();
    // 解锁单位(别的模组的载具：坦克、飞机、炮艇等 ActorAsset id)
    public List<string> unlock_units = new();
    public float research_bonus;
    public List<TechBoostConfig> boosts = new();
}

// 别的模组里"能不能做某事"的布尔方法：本文化没研究对应技术时直接返回 false。
// type 是完整类型名(找不到就跳过，没装那个模组也不影响)，文化取自第一个 Actor/Kingdom/City 参数。
public sealed class TechModGateConfig
{
    public string type = "";
    public string method = "";
    public string tech = "";
}

// 别的模组绕过建造系统、直接把建筑换成更高级版本的方法(比如 WarBox 的民居升级器)。
// 在这些方法执行期间，换成本文化未研究的建筑会被拦下；defer_to_mod_building 对应的建筑存在时
// (说明装了更完整的建筑模组，如 modernmod)，这个方法里的换建筑全部拦下，把升级链交给那个模组。
public sealed class TechSwapGuardConfig
{
    public string type = "";
    public string method = "";
    public string defer_to_mod_building = "";
}

// 技术 → 文化制度：技术是动力，制度是结果。
//   requires_techs：提供该特性(且特性值 ≥ min_value)的制度节点，要先有这些技术才能推行；
//   drivers：每掌握一项技术，这类节点的改革进度加快多少(0.3 = +30%)，AI 也更愿意推它。
public sealed class TechInstitutionLink
{
    public string feature = "";
    public float min_value;
    public List<string> requires_techs = new();
    public Dictionary<string, float> drivers = new();
}

// 工厂岗位：这些类型的建筑每座给城市提供多少工人岗位(乘以城市生产阶段)
public sealed class TechIndustryConfig
{
    public List<string> factory_types = new();
    public int jobs_per_factory = 3;
    // 技术 → 城市可雇工人口比例上限的额外加成
    public Dictionary<string, float> employment_cap_bonus = new();
    // 资本主义萌芽需要的技术
    public List<string> capitalist_budding_techs = new();
}

public sealed class TechTreeConfig
{
    public List<TechInstitutionLink> institution_links = new();
    public TechIndustryConfig industry = new();
    public List<TechModGateConfig> mod_gates = new();
    public List<TechSwapGuardConfig> building_swap_guards = new();
    public int schema_version = 1;
    public TechResearchConfig research = new();
    public List<string> branches = new();
    public List<TechEraConfig> eras = new();
    public List<TechMaterialConfig> materials = new();
    public List<TechNodeConfig> techs = new();
}

#endregion

public enum TechNodeStatus
{
    Researched,
    Researching,
    Available,
    Locked,
    MaterialDiscovered,
    MaterialUndiscovered
}

// 文明科技树：材料 → 技术 → 武器/建筑。
//
// 进度归属文化(跟制度一样)，同文化的所有国家共享。每年结算一次：
//   1. 本文化城市的仓库里出现了某种资源(且满足前置技术)，就"发现"对应材料；
//   2. 按城市数、人口、图书馆、神庙算研究点，投到当前研究的技术上；
//   3. 研究完成后，本文化的工匠才能打造对应材质/型号的装备，城市才能把建筑升到对应等级。
//
// 限制只作用于"被某项技术登记过"的物品和建筑；没登记的(原版特殊武器、别的模组的东西)一律放行。
// 登记了但当前没装对应模组的物品/建筑(比如没装 ModernBox 时的枪)在加载时就被跳过，不影响别的。
public static class TechnologySystem
{
    public const string FolderName = "Technology";
    public const string MaterialBranch = "material";

    private static TechTreeConfig _config = new();
    private static Dictionary<string, TechNodeConfig> _techs = new(StringComparer.Ordinal);
    private static Dictionary<string, TechMaterialConfig> _materials = new(StringComparer.Ordinal);
    private static bool _loaded;

    // 受限清单：材质 → 解锁它的技术；物品 id → 技术；建筑匹配规则 → 技术
    private static readonly Dictionary<string, string> MaterialGate = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ItemGate = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> UnitGate = new(StringComparer.Ordinal);
    private static readonly List<(Regex pattern, string tech)> BuildingPatterns = new();
    private static readonly Dictionary<string, string> BuildingGateCache = new(StringComparer.Ordinal);

    private static Dictionary<string, CultureTechState> _states = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> ResearchedCache = new(StringComparer.Ordinal);
    private static double _lastScan = -1d;
    // 每年重算的文化接触关系(不存档，读档后第一次结算就会重建)
    private static readonly Dictionary<string, HashSet<string>> Contacts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> WarContacts = new(StringComparer.Ordinal);

    public static TechTreeConfig Config
    {
        get
        {
            EnsureLoaded();
            return _config;
        }
    }

    public static IEnumerable<TechNodeConfig> Techs => Config.techs;
    public static IEnumerable<TechMaterialConfig> Materials => Config.materials;

    public static bool IsEnabled =>
        EmpireCraftWorldLawLibrary.empirecraft_law_tech_tree == null ||
        EmpireCraftWorldLawLibrary.empirecraft_law_tech_tree.isEnabled();

    #region 加载

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            string path = global::System.IO.Path.Combine(ModClass._declare.FolderPath, FolderName, "TechTree.json");
            if (global::System.IO.File.Exists(path))
                _config = JsonConvert.DeserializeObject<TechTreeConfig>(global::System.IO.File.ReadAllText(path)) ??
                          new TechTreeConfig();
            else
                LogService.LogWarning($"[EmpireCraft] 没有找到科技树配置: {path}");
        }
        catch (Exception exception)
        {
            LogService.LogError($"[EmpireCraft] 科技树配置读取失败: {exception}");
            _config = new TechTreeConfig();
        }
        _config.techs ??= new List<TechNodeConfig>();
        _config.materials ??= new List<TechMaterialConfig>();
        _config.branches ??= new List<string>();
        _config.eras ??= new List<TechEraConfig>();
        _config.research ??= new TechResearchConfig();
        _config.mod_gates ??= new List<TechModGateConfig>();
        _config.institution_links ??= new List<TechInstitutionLink>();
        foreach (TechInstitutionLink link in _config.institution_links)
        {
            link.requires_techs ??= new List<string>();
            link.drivers ??= new Dictionary<string, float>();
        }
        _config.industry ??= new TechIndustryConfig();
        _config.industry.factory_types ??= new List<string>();
        _config.industry.employment_cap_bonus ??= new Dictionary<string, float>();
        _config.industry.capitalist_budding_techs ??= new List<string>();
        _config.building_swap_guards ??= new List<TechSwapGuardConfig>();
        _techs = new Dictionary<string, TechNodeConfig>(StringComparer.Ordinal);
        foreach (TechNodeConfig tech in _config.techs.Where(tech => !string.IsNullOrWhiteSpace(tech?.id)))
        {
            tech.requires_techs ??= new List<string>();
            tech.requires_materials ??= new List<string>();
            tech.unlock_materials ??= new List<string>();
            tech.unlock_items ??= new List<string>();
            tech.unlock_buildings ??= new List<string>();
            tech.unlock_units ??= new List<string>();
            tech.boosts ??= new List<TechBoostConfig>();
            _techs[tech.id] = tech;
        }
        _materials = new Dictionary<string, TechMaterialConfig>(StringComparer.Ordinal);
        foreach (TechMaterialConfig material in _config.materials.Where(m => !string.IsNullOrWhiteSpace(m?.id)))
        {
            material.resources ??= new List<TechResourceRequirement>();
            material.requires_techs ??= new List<string>();
            _materials[material.id] = material;
        }
        RebuildGates();
    }

    // 受限清单在资产加载完之后才建得准(别的模组的枪、建筑可能比本模组晚注册)，
    // 所以第一次用到的时候再建，世界加载时再刷新一次。
    private static void RebuildGates()
    {
        MaterialGate.Clear();
        ItemGate.Clear();
        UnitGate.Clear();
        BuildingPatterns.Clear();
        BuildingGateCache.Clear();
        foreach (TechNodeConfig tech in _config.techs.OrderBy(tech => tech.tier))
        {
            foreach (string material in tech.unlock_materials)
                if (!MaterialGate.ContainsKey(material)) MaterialGate[material] = tech.id;
            foreach (string item in tech.unlock_items)
                if (!ItemGate.ContainsKey(item)) ItemGate[item] = tech.id;
            foreach (string unit in tech.unlock_units)
                if (!UnitGate.ContainsKey(unit)) UnitGate[unit] = tech.id;
            foreach (string pattern in tech.unlock_buildings.Where(p => !string.IsNullOrWhiteSpace(p)))
                BuildingPatterns.Add((new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                    RegexOptions.CultureInvariant), tech.id));
        }
    }

    public static bool TryGetTech(string id, out TechNodeConfig tech)
    {
        EnsureLoaded();
        return _techs.TryGetValue(id ?? "", out tech);
    }

    public static bool TryGetMaterial(string id, out TechMaterialConfig material)
    {
        EnsureLoaded();
        return _materials.TryGetValue(id ?? "", out material);
    }

    #endregion

    #region 存档

    public static void ResetWorldState()
    {
        _states = new Dictionary<string, CultureTechState>(StringComparer.Ordinal);
        ResearchedCache.Clear();
        Contacts.Clear();
        WarContacts.Clear();
        _lastScan = -1d;
    }

    public static void ImportStates(Dictionary<string, CultureTechState> states)
    {
        ResetWorldState();
        if (states == null) return;
        foreach (KeyValuePair<string, CultureTechState> pair in states)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
            pair.Value.discovered_materials ??= new List<string>();
            pair.Value.researched_techs ??= new List<string>();
            pair.Value.timestamps ??= new Dictionary<string, double>();
            pair.Value.current_tech ??= "";
            pair.Value.player_target ??= "";
            pair.Value.tech_progress ??= new Dictionary<string, float>();
            pair.Value.boosted ??= new List<string>();
            if (pair.Value.progress > 0f && !string.IsNullOrEmpty(pair.Value.current_tech))
            {
                pair.Value.tech_progress[pair.Value.current_tech] = pair.Value.progress;
                pair.Value.progress = 0f;
            }
            _states[pair.Key] = pair.Value;
        }
    }

    public static Dictionary<string, CultureTechState> ExportStates() =>
        _states.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public static CultureTechState GetState(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return new CultureTechState();
        if (!_states.TryGetValue(culture, out CultureTechState state))
        {
            state = new CultureTechState();
            _states[culture] = state;
        }
        return state;
    }

    private static HashSet<string> Researched(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return new HashSet<string>();
        if (ResearchedCache.TryGetValue(culture, out HashSet<string> cached)) return cached;
        cached = new HashSet<string>(GetState(culture).researched_techs, StringComparer.Ordinal);
        ResearchedCache[culture] = cached;
        return cached;
    }

    #endregion

    #region 查询

    public static bool HasTech(string culture, string techId) => Researched(culture).Contains(techId);

    public static bool HasMaterial(string culture, string materialId) =>
        GetState(culture).discovered_materials.Contains(materialId);

    public static string GetCultureOf(City city) =>
        city?.kingdom == null || city.kingdom.isRekt() ? "" : CultureService.GetRealmCulture(city.kingdom);

    public static string GetCultureOf(Actor actor)
    {
        if (actor?.kingdom != null && !actor.kingdom.isRekt() && actor.kingdom.isCiv())
            return CultureService.GetRealmCulture(actor.kingdom);
        return GetCultureOf(actor?.city);
    }

    // 某个物品需要哪项技术；不受限返回 null
    public static string GetRequiredTechForItem(ItemAsset asset)
    {
        if (asset == null) return null;
        EnsureLoaded();
        if (ItemGate.TryGetValue(asset.id, out string byId)) return byId;
        if (!string.IsNullOrEmpty(asset.material) && MaterialGate.TryGetValue(asset.material, out string byMaterial))
            return byMaterial;
        return null;
    }

    public static string GetRequiredTechForUnit(string actorAssetId)
    {
        if (string.IsNullOrEmpty(actorAssetId)) return null;
        EnsureLoaded();
        return UnitGate.TryGetValue(actorAssetId, out string tech) ? tech : null;
    }

    // 本文化是否已经在用科技树(读档后第一次结算前还没按现有装备补齐，这段时间不拦，免得把旧装备扒掉)
    public static bool IsActiveFor(string culture) =>
        IsEnabled && CultureService.IsValidCulture(culture) &&
        _states.TryGetValue(culture, out CultureTechState state) && state.bootstrapped;

    public static bool CanUse(string culture, string techId) =>
        techId == null || !IsActiveFor(culture) || HasTech(culture, techId);

    public static string GetCultureOf(Kingdom kingdom) =>
        kingdom == null || kingdom.isRekt() || !kingdom.isCiv() ? "" : CultureService.GetRealmCulture(kingdom);

    public static string GetRequiredTechForBuilding(string buildingId)
    {
        if (string.IsNullOrEmpty(buildingId)) return null;
        EnsureLoaded();
        if (BuildingGateCache.TryGetValue(buildingId, out string cached)) return cached;
        string result = null;
        foreach ((Regex pattern, string tech) in BuildingPatterns)
        {
            if (!pattern.IsMatch(buildingId)) continue;
            result = tech;
            break;
        }
        BuildingGateCache[buildingId] = result;
        return result;
    }

    public static bool CanCraft(string culture, ItemAsset asset) => CanUse(culture, GetRequiredTechForItem(asset));

    public static bool CanBuild(City city, string buildingId)
    {
        if (city == null) return true;
        string tech = GetRequiredTechForBuilding(buildingId);
        return tech == null || CanUse(GetCultureOf(city), tech);
    }

    public static bool CanEquip(Actor actor, ItemAsset asset)
    {
        string tech = GetRequiredTechForItem(asset);
        return tech == null || CanUse(GetCultureOf(actor), tech);
    }

    public static bool CanSpawnUnit(string actorAssetId, City city)
    {
        string tech = GetRequiredTechForUnit(actorAssetId);
        return tech == null || city == null || CanUse(GetCultureOf(city), tech);
    }

    public static bool ArePrerequisitesMet(string culture, TechNodeConfig tech) =>
        tech.requires_techs.All(id => HasTech(culture, id)) &&
        tech.requires_materials.All(id => HasMaterial(culture, id));

    public static TechNodeStatus GetTechStatus(string culture, TechNodeConfig tech)
    {
        if (HasTech(culture, tech.id)) return TechNodeStatus.Researched;
        if (!ArePrerequisitesMet(culture, tech)) return TechNodeStatus.Locked;
        return GetState(culture).current_tech == tech.id ? TechNodeStatus.Researching : TechNodeStatus.Available;
    }

    public static TechNodeStatus GetMaterialStatus(string culture, TechMaterialConfig material) =>
        HasMaterial(culture, material.id) ? TechNodeStatus.MaterialDiscovered : TechNodeStatus.MaterialUndiscovered;

    public static int CountContactsKnowing(string culture, string techId) =>
        Contacts.TryGetValue(culture ?? "", out HashSet<string> contacts)
            ? contacts.Count(other => _states.TryGetValue(other, out CultureTechState state) &&
                                      state.researched_techs.Contains(techId))
            : 0;

    public static IEnumerable<string> GetContacts(string culture) =>
        Contacts.TryGetValue(culture ?? "", out HashSet<string> contacts) ? contacts : Enumerable.Empty<string>();

    public static float GetSizeCostFactor(string culture)
    {
        TechResearchConfig research = Config.research;
        int cities = GetState(culture).city_count;
        return 1f + Math.Min(research.cost_city_scaling_max, research.cost_per_extra_city * Math.Max(0, cities - 1));
    }

    public static float GetDiffusionDiscount(string culture, string techId)
    {
        TechResearchConfig research = Config.research;
        return Math.Min(research.diffusion_max, CountContactsKnowing(culture, techId) * research.diffusion_per_contact);
    }

    public static float GetProgress(string culture, string techId) =>
        GetState(culture).tech_progress.TryGetValue(techId ?? "", out float value) ? value : 0f;

    public static bool IsKnownElsewhere(string culture, string techId) => CountContactsKnowing(culture, techId) > 0;

    public static float GetCost(string culture, TechNodeConfig tech)
    {
        float cost = Math.Max(1f, tech.cost) * GetSizeCostFactor(culture);
        return cost * (1f - GetDiffusionDiscount(culture, tech.id));
    }

    public static float GetResearchBonus(string culture) =>
        Researched(culture).Select(id => _techs.TryGetValue(id, out TechNodeConfig tech) ? tech.research_bonus : 0f)
            .Sum();

    public static int GetEraTier(string culture)
    {
        HashSet<string> researched = Researched(culture);
        return researched.Count == 0
            ? 0
            : researched.Select(id => _techs.TryGetValue(id, out TechNodeConfig tech) ? tech.tier : 0).Max();
    }

    public static string GetEraName(string culture)
    {
        int tier = GetEraTier(culture);
        TechEraConfig era = Config.eras.Where(e => e.tier <= tier).OrderByDescending(e => e.tier).FirstOrDefault();
        return era == null ? LM.Get("tech_era_primitive") : LM.Get(era.key);
    }

    public static string GetTechName(string id) => LM.Get($"tech_{id}");
    public static string GetTechDescription(string id) => LM.Get($"tech_{id}_desc");
    public static string GetMaterialName(string id) => LM.Get($"tech_mat_{id}");

    public static string GetBranchName(string branch)
    {
        string key = $"tech_branch_{branch}";
        string value = LM.Get(key);
        return string.IsNullOrWhiteSpace(value) || value == key ? branch : value;
    }

    // 材料的发现条件，窗口里显示用
    public static string DescribeMaterialCondition(TechMaterialConfig material)
    {
        string resources = string.Join(LM.Get("tech_or"), material.resources.Select(r =>
        {
            ResourceAsset asset = AssetManager.resources.get(r.id);
            string name = asset != null ? asset.getTranslatedName() : r.id;
            return $"{name}×{r.amount}";
        }));
        string techs = string.Join("、", material.requires_techs.Select(GetTechName));
        return string.IsNullOrEmpty(techs)
            ? string.Format(LM.Get("tech_material_condition"), resources)
            : string.Format(LM.Get("tech_material_condition_tech"), resources, techs);
    }

    // 技术解锁的东西里，当前游戏里真正存在的(没装对应模组的会被列成"未安装")
    public static IEnumerable<(string label, bool present)> DescribeUnlocks(TechNodeConfig tech)
    {
        foreach (string material in tech.unlock_materials)
            yield return (string.Format(LM.Get("tech_unlock_material"), LM.Get($"tech_material_{material}")), true);
        foreach (string item in tech.unlock_items)
        {
            ItemAsset asset = AssetManager.items.get(item);
            yield return (asset != null ? asset.getTranslatedName() : item, asset != null);
        }
        foreach (string unit in tech.unlock_units)
        {
            ActorAsset asset = AssetManager.actor_library.get(unit);
            yield return (asset != null ? string.Format(LM.Get("tech_unlock_unit"), asset.getTranslatedName()) : unit,
                asset != null);
        }
        foreach (string pattern in tech.unlock_buildings)
        {
            Regex regex = new("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
            BuildingAsset sample = AssetManager.buildings.list.FirstOrDefault(asset => regex.IsMatch(asset.id));
            string label = sample != null
                ? string.Format(LM.Get("tech_unlock_building"), DescribeBuilding(sample))
                : pattern;
            yield return (label, sample != null);
        }
    }

    private static string DescribeBuilding(BuildingAsset asset)
    {
        // 别的模组的独立建筑(工厂、碉堡、大学)一般以 id 做本地化 key，有就直接用
        string ownName = LM.Get(asset.id);
        if (!string.IsNullOrWhiteSpace(ownName) && ownName != asset.id && !asset.id.EndsWith("_modernmod") &&
            !asset.id.StartsWith("house_"))
            return ownName;
        string type = asset.type?.Replace("type_", "") ?? "";
        string typeKey = $"tech_building_type_{type}";
        string typeName = LM.Get(typeKey);
        if (string.IsNullOrWhiteSpace(typeName) || typeName == typeKey) typeName = type;
        return string.Format(LM.Get("tech_building_level"), typeName, asset.upgrade_level);
    }

    #region 技术 → 制度

    private static IEnumerable<TechInstitutionLink> LinksFor(InstitutionNodeConfig node)
    {
        if (node?.features == null) yield break;
        foreach (TechInstitutionLink link in Config.institution_links)
            if (node.features.TryGetValue(link.feature, out float value) && value > 0f && value >= link.min_value)
                yield return link;
    }

    public static List<string> GetInstitutionRequiredTechs(InstitutionNodeConfig node) =>
        LinksFor(node).SelectMany(link => link.requires_techs).Distinct().ToList();

    public static bool AreInstitutionTechsMet(string culture, InstitutionNodeConfig node, out List<string> missing)
    {
        missing = new List<string>();
        if (!IsActiveFor(culture)) return true;
        missing = GetInstitutionRequiredTechs(node).Where(id => !HasTech(culture, id)).ToList();
        return missing.Count == 0;
    }

    // 按特性(不经过具体节点)判断技术基础是否具备，比如外来理念能否在本文化落地
    public static bool AreFeatureTechsMet(string culture, string feature, float value = 1f) =>
        !IsActiveFor(culture) || Config.institution_links
            .Where(link => link.feature == feature && value >= link.min_value)
            .SelectMany(link => link.requires_techs).All(id => HasTech(culture, id));

    // 已掌握的技术对这个制度的推动力(改革进度倍率 - 1)
    public static float GetInstitutionPush(string culture, InstitutionNodeConfig node)
    {
        if (!IsEnabled || !CultureService.IsValidCulture(culture)) return 0f;
        float push = 0f;
        foreach (TechInstitutionLink link in LinksFor(node))
        foreach (KeyValuePair<string, float> driver in link.drivers)
            if (HasTech(culture, driver.Key)) push += driver.Value;
        return push;
    }

    // 某项技术会推动/解锁哪些制度特性(科技窗口里显示)
    public static IEnumerable<(TechInstitutionLink link, bool gate, float push)> GetInstitutionEffects(string techId)
    {
        foreach (TechInstitutionLink link in Config.institution_links)
        {
            bool gate = link.requires_techs.Contains(techId);
            float push = link.drivers.TryGetValue(techId, out float value) ? value : 0f;
            if (gate || push > 0f) yield return (link, gate, push);
        }
    }

    public static string DescribeFeature(TechInstitutionLink link)
    {
        string key = $"tech_feature_{link.feature.Replace(':', '_')}";
        if (link.min_value > 1f) key += $"_{link.min_value:0}";
        string text = LM.Get(key);
        if (!string.IsNullOrWhiteSpace(text) && text != key) return text;
        if (IdeologySpreadSystem.TryParseFeature(link.feature, out PartyIdeology ideology))
            return string.Format(LM.Get("tech_feature_ideology"), PartySystem.GetIdeologyName(ideology));
        return link.feature;
    }

    // 制度节点提示里的一行："技术基础：✔印刷术 ✘工业化 · 技术推动 +30%"；跟技术无关的节点返回 null
    public static string DescribeInstitutionTechLine(string culture, InstitutionNodeConfig node)
    {
        if (!IsEnabled || node == null) return null;
        List<string> required = GetInstitutionRequiredTechs(node);
        float push = GetInstitutionPush(culture, node);
        if (required.Count == 0 && push <= 0f) return null;
        var parts = new List<string>();
        if (required.Count > 0)
            parts.Add(LM.Get("tech_institution_basis") + string.Join(" ", required.Select(id =>
                (HasTech(culture, id) ? "✔" : "✘") + GetTechName(id))));
        if (push > 0f) parts.Add(string.Format(LM.Get("tech_institution_push"), (push * 100f).ToString("0")));
        return string.Join("  ·  ", parts);
    }

    public static bool CanCapitalistBud(string culture) =>
        !IsActiveFor(culture) || Config.industry.capitalist_budding_techs.All(id => HasTech(culture, id));

    public static bool IsFactory(BuildingAsset asset) =>
        asset != null && Config.industry.factory_types.Contains(asset.type);

    public static float GetEmploymentCapBonus(string culture)
    {
        if (!IsEnabled || !CultureService.IsValidCulture(culture)) return 0f;
        return Config.industry.employment_cap_bonus.Where(pair => HasTech(culture, pair.Key)).Sum(pair => pair.Value);
    }

    #endregion

    public static bool ModernModDetected => AssetManager.buildings?.get("6house_human_modernmod") != null;
    public static bool WarBoxDetected => AssetManager.buildings?.get("heavy_factory") != null &&
                                         AssetManager.actor_library?.get("warbox_tank") != null;
    public static bool ModernBoxDetected => AssetManager.items?.get("M16") != null || AssetManager.items?.get("AK") != null;

    #endregion

    #region 年度结算

    // 挂在城市更新上，自己限流成一年一次
    public static void TryYearlyScan()
    {
        if (World.world == null || ModClass.IS_CLEAR) return;
        GamePatches.TechnologyPatch.EnsureModPatches();
        if (!IsEnabled) return;
        double now = World.world.getCurWorldTime();
        bool firstPass = _lastScan < 0d || now < _lastScan;
        if (!firstPass && Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            // 读档/开局后的第一次：只补齐和发现材料，不发研究点
            YearlyUpdate(!firstPass);
        }
        catch (Exception exception)
        {
            LogService.LogError($"[EmpireCraft] 科技年度结算失败: {exception}");
        }
    }

    private static void YearlyUpdate(bool addResearch)
    {
        EnsureLoaded();
        TechResearchConfig research = Config.research;
        var citiesByCulture = new Dictionary<string, List<City>>(StringComparer.Ordinal);
        foreach (City city in World.world.cities.list)
        {
            if (city == null || city.isRekt()) continue;
            if (Compatibility.AncientWarfareCompatibility.OwnsObject(city)) continue;
            string culture = GetCultureOf(city);
            if (!CultureService.IsValidCulture(culture)) continue;
            if (!citiesByCulture.TryGetValue(culture, out List<City> list))
                citiesByCulture[culture] = list = new List<City>();
            list.Add(city);
        }
        RebuildContacts(citiesByCulture);

        foreach (KeyValuePair<string, List<City>> pair in citiesByCulture)
        {
            string culture = pair.Key;
            CultureTechState state = GetState(culture);
            state.city_count = pair.Value.Count;
            if (!state.bootstrapped)
            {
                state.bootstrapped = true;
                Bootstrap(culture, pair.Value);
            }
            DiscoverMaterials(culture, pair.Value);
            if (state.last_era_tier < 0) state.last_era_tier = GetEraTier(culture);
            if (!addResearch) continue;

            float points = 0f;
            foreach (City city in pair.Value)
            {
                float cityPoints = research.base_per_city + city.getPopulationPeople() * research.per_population +
                                   city.countBuildingsType("type_library") * research.per_library +
                                   city.countBuildingsType("type_temple") * research.per_temple +
                                   city.countBuildingsType("type_university") * research.per_university +
                                   city.buildings.Count(b => b != null && IsFactory(b.asset)) * research.per_factory;
                // 兵荒马乱的城市读不了书
                if (city.kingdom != null && city.kingdom.hasEnemies()) cityPoints *= 1f - research.war_penalty;
                points += cityPoints;
            }
            points *= 1f + GetResearchBonus(culture);
            state.last_yearly_points = points;
            CheckBoosts(culture, pair.Value);
            AddResearch(culture, points, pair.Value);
            CheckEra(culture, pair.Value);
            if (state.modernize_years > 0)
            {
                state.modernize_years--;
                Modernize(culture, pair.Value, research.modernize_per_city);
            }
        }
    }

    // 文化之间的接触：城市接壤、正在交战、同属一个帝国(含朝贡国)。技术只沿着接触扩散
    private static void RebuildContacts(Dictionary<string, List<City>> citiesByCulture)
    {
        Contacts.Clear();
        WarContacts.Clear();
        var empireCultures = new Dictionary<long, HashSet<string>>();
        foreach (KeyValuePair<string, List<City>> pair in citiesByCulture)
        foreach (City city in pair.Value)
        {
            foreach (City neighbour in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
            {
                if (neighbour == null || neighbour.isRekt()) continue;
                string other = GetCultureOf(neighbour);
                if (!CultureService.IsValidCulture(other)) continue;
                Link(Contacts, pair.Key, other);
                Link(Contacts, other, pair.Key);
                if (city.kingdom != null && neighbour.kingdom != null && city.kingdom.isEnemy(neighbour.kingdom))
                {
                    Link(WarContacts, pair.Key, other);
                    Link(WarContacts, other, pair.Key);
                }
            }
            long empireId = city.kingdom?.GetEmpireID() ?? -1L;
            if (empireId < 0) continue;
            if (!empireCultures.TryGetValue(empireId, out HashSet<string> cultures))
                empireCultures[empireId] = cultures = new HashSet<string>(StringComparer.Ordinal);
            cultures.Add(pair.Key);
        }
        foreach (HashSet<string> cultures in empireCultures.Values)
        foreach (string a in cultures)
        foreach (string b in cultures)
            Link(Contacts, a, b);
    }

    private static void Link(Dictionary<string, HashSet<string>> map, string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b) return;
        if (!map.TryGetValue(a, out HashSet<string> set)) map[a] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(b);
    }

    #region 加速事件

    public static bool IsBoostMet(string culture, TechNodeConfig tech, TechBoostConfig boost, List<City> cities)
    {
        cities ??= CitiesOf(culture);
        int amount = Math.Max(1, boost.amount);
        switch (boost.type)
        {
            case "building_type":
                return cities.Sum(city => city.countBuildingsType(boost.value)) >= amount;
            case "resource":
                return cities.Any(city => city.getResourcesAmount(boost.value) >= amount);
            case "population":
                return cities.Sum(city => city.getPopulationPeople()) >= amount;
            case "cities":
                return cities.Count >= amount;
            case "material":
                return HasMaterial(culture, boost.value);
            case "tech":
                return HasTech(culture, boost.value);
            case "institution_feature":
                return InstitutionSystem.GetFeature(culture, boost.value) >= amount; // amount 当作特性的最低值
            case "contact_knows":
                return CountContactsKnowing(culture, tech.id) > 0;
            case "war_knows":
                return WarContacts.TryGetValue(culture, out HashSet<string> enemies) && enemies.Any(other =>
                    _states.TryGetValue(other, out CultureTechState state) && state.researched_techs.Contains(tech.id));
            default:
                return false;
        }
    }

    public static string DescribeBoost(TechBoostConfig boost)
    {
        string value = boost.type switch
        {
            "building_type" => DescribeBuildingType(boost.value),
            "resource" => AssetManager.resources.get(boost.value)?.getTranslatedName() ?? boost.value,
            "material" => GetMaterialName(boost.value),
            "tech" => GetTechName(boost.value),
            "institution_feature" => DescribeFeature(new TechInstitutionLink { feature = boost.value, min_value = boost.amount }),
            _ => boost.value
        };
        return string.Format(LM.Get($"tech_boost_{boost.type}"), value, boost.amount);
    }

    private static string DescribeBuildingType(string type)
    {
        string key = $"tech_building_type_{type?.Replace("type_", "")}";
        string name = LM.Get(key);
        return string.IsNullOrWhiteSpace(name) || name == key ? type : name;
    }

    private static List<City> CitiesOf(string culture) =>
        World.world?.cities?.list?.Where(city => city != null && !city.isRekt() && GetCultureOf(city) == culture)
            .ToList() ?? new List<City>();

    private static void CheckBoosts(string culture, List<City> cities)
    {
        CultureTechState state = GetState(culture);
        float fraction = Config.research.boost_fraction;
        foreach (TechNodeConfig tech in Config.techs)
        {
            if (tech.boosts.Count == 0 || state.boosted.Contains(tech.id) || HasTech(culture, tech.id)) continue;
            if (!tech.boosts.Any(boost => IsBoostMet(culture, tech, boost, cities))) continue;
            state.boosted.Add(tech.id);
            float cost = GetCost(culture, tech);
            state.tech_progress[tech.id] = Math.Min(cost, GetProgress(culture, tech.id) + cost * fraction);
        }
    }

    #endregion

    // 本文化的人已经拿在手里的装备、城里已经立着的建筑，说明这门手艺早就会了：直接记为已掌握(连同前置)
    private static void Bootstrap(string culture, List<City> cities)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (City city in cities)
        {
            foreach (Actor actor in city.units)
            {
                if (actor == null || !actor.isAlive()) continue;
                string unitTech = GetRequiredTechForUnit(actor.asset?.id);
                if (unitTech != null) needed.Add(unitTech);
                if (actor.equipment == null) continue;
                foreach (ActorEquipmentSlot slot in actor.equipment)
                {
                    string tech = GetRequiredTechForItem(slot?.getItem()?.asset);
                    if (tech != null) needed.Add(tech);
                }
            }
            foreach (Building building in city.buildings)
            {
                string tech = GetRequiredTechForBuilding(building?.asset?.id);
                if (tech != null) needed.Add(tech);
            }
        }
        // 已经推行的制度说明对应的技术基础早就有了(旧存档里已经工业化的文化不会被打回原形)
        CultureInstitutionState institutions = InstitutionSystem.GetOrCreateCultureState(culture);
        if (institutions?.enacted_node_ids != null)
            foreach (string nodeId in institutions.enacted_node_ids)
                foreach (string tech in GetInstitutionRequiredTechs(InstitutionDefinitionRegistry.Get(nodeId)))
                    needed.Add(tech);
        foreach (string tech in needed) ForceResearch(culture, tech);
    }

    private static void DiscoverMaterials(string culture, List<City> cities)
    {
        CultureTechState state = GetState(culture);
        foreach (TechMaterialConfig material in Config.materials)
        {
            if (state.discovered_materials.Contains(material.id)) continue;
            if (!material.requires_techs.All(id => HasTech(culture, id))) continue;
            bool found = material.resources.Count == 0 || material.resources.Any(requirement =>
                cities.Any(city => city.getResourcesAmount(requirement.id) >= requirement.amount));
            if (!found) continue;
            state.discovered_materials.Add(material.id);
            state.timestamps[$"mat:{material.id}"] = World.world.getCurWorldTime();
        }
    }

    private static void AddResearch(string culture, float points, List<City> cities)
    {
        CultureTechState state = GetState(culture);
        // 一年的点数可能够研究好几项低级技术，溢出的点数顺延给下一项
        for (int guard = 0; guard < 8 && points > 0f; guard++)
        {
            TechNodeConfig target = PickTarget(culture);
            if (target == null)
            {
                state.current_tech = "";
                return;
            }
            state.current_tech = target.id;
            float cost = GetCost(culture, target);
            float progress = GetProgress(culture, target.id);
            float needed = cost - progress;
            if (points < needed)
            {
                state.tech_progress[target.id] = progress + points;
                return;
            }
            points -= Math.Max(0f, needed);
            bool worldFirst = !_states.Any(pair => pair.Key != culture && pair.Value.researched_techs.Contains(target.id));
            CompleteTech(culture, target.id);
            OnResearched(culture, target, worldFirst, cities);
        }
    }

    // 研究出来之后：世界首创写日志；新武器 → 军队接下来几年陆续换装
    private static void OnResearched(string culture, TechNodeConfig tech, bool worldFirst, List<City> cities)
    {
        if (worldFirst && tech.tier >= Config.research.world_first_min_tier)
            Announce(culture, cities, string.Format(LM.Get("tech_world_first_log"),
                culture.GetCultureTranslate(), GetTechName(tech.id)));
        if (tech.unlock_items.Count > 0 || tech.unlock_materials.Count > 0)
        {
            CultureTechState state = GetState(culture);
            state.modernize_years = Math.Max(state.modernize_years, Config.research.modernize_years);
        }
    }

    private static void CheckEra(string culture, List<City> cities)
    {
        CultureTechState state = GetState(culture);
        int tier = GetEraTier(culture);
        if (tier <= state.last_era_tier) return;
        int previous = state.last_era_tier;
        state.last_era_tier = tier;
        if (Config.eras.Any(era => era.tier > previous && era.tier <= tier))
            Announce(culture, cities, string.Format(LM.Get("tech_new_era_log"),
                culture.GetCultureTranslate(), GetEraName(culture)));
    }

    // 世界日志一条 + 本文化各帝国的国史各记一条
    private static void Announce(string culture, List<City> cities, string text)
    {
        Kingdom kingdom = cities?.Select(city => city.kingdom).Where(k => k != null && !k.isRekt())
            .GroupBy(k => k).OrderByDescending(group => group.Count()).FirstOrDefault()?.Key;
        HelperFunc.TranslateHelper.LogEventMessage(text, kingdom);
        foreach (Layer.Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Layer.Empire>())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived()) continue;
            if (InstitutionSystem.GetPrimaryCulture(empire) != culture) continue;
            EmpireCraft.Scripts.System.HistoryRecordSystem.RecordHistory(empire, directContent: text,
                kingdomId: empire.CoreKingdom?.id ?? -1L);
        }
    }

    #region 军队换装

    // 让士兵用城市的材料重新打造装备(走原版打造流程，自然会挑本文化能造的最好的一件)；
    // 国库给每人补贴一点钱，没花完的退回国库
    public static int Modernize(string culture, List<City> cities, int perCity)
    {
        int upgraded = 0;
        int subsidy = Config.research.modernize_subsidy;
        foreach (City city in cities ?? CitiesOf(culture))
        {
            Kingdom kingdom = city.kingdom;
            if (kingdom == null || kingdom.isRekt()) continue;
            int done = 0;
            foreach (Actor actor in city.units.ToList())
            {
                if (done >= perCity) break;
                if (actor == null || !actor.isAlive() || !actor.isWarrior() || actor.equipment == null ||
                    actor.IsWarMachine()) continue;
                int grant = Math.Max(0, Math.Min(subsidy, kingdom.GetMoney()));
                int own = actor.money;
                if (grant > 0)
                {
                    kingdom.SubMoney(grant);
                    actor.addMoney(grant);
                }
                bool weapon = ItemCrafting.tryToCraftRandomWeapon(actor, city);
                bool armor = ItemCrafting.tryToCraftRandomArmor(actor, city);
                // 先花补贴、再花自己的钱：剩下的钱里超出本人原有部分的退回国库
                int refund = Math.Max(0, Math.Min(grant, actor.money - own));
                if (refund > 0)
                {
                    actor.spendMoney(refund);
                    kingdom.AddMoney(refund);
                }
                done++;
                if (weapon || armor) upgraded++;
            }
        }
        return upgraded;
    }

    public static int ModernizeNow(string culture) =>
        CultureService.IsValidCulture(culture) ? Modernize(culture, CitiesOf(culture), 20) : 0;

    #endregion

    // 玩家指定的目标优先(目标还没满足前置时，先研究它缺的那一项可研究前置)；否则 AI 选最便宜的
    private static TechNodeConfig PickTarget(string culture)
    {
        CultureTechState state = GetState(culture);
        if (!string.IsNullOrEmpty(state.player_target))
        {
            if (HasTech(culture, state.player_target)) state.player_target = "";
            else
            {
                TechNodeConfig step = FindNextStepToward(culture, state.player_target, new HashSet<string>());
                if (step != null) return step;
            }
        }
        if (!string.IsNullOrEmpty(state.current_tech) && _techs.TryGetValue(state.current_tech, out TechNodeConfig current) &&
            !HasTech(culture, current.id) && ArePrerequisitesMet(culture, current))
            return current;
        return Config.techs.Where(tech => !HasTech(culture, tech.id) && ArePrerequisitesMet(culture, tech))
            .OrderBy(tech => GetCost(culture, tech) / (tech.research_bonus > 0f ? 1.3f : 1f))
            .ThenBy(tech => tech.id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static TechNodeConfig FindNextStepToward(string culture, string techId, HashSet<string> visited)
    {
        if (!visited.Add(techId) || !_techs.TryGetValue(techId, out TechNodeConfig tech) || HasTech(culture, techId))
            return null;
        if (ArePrerequisitesMet(culture, tech)) return tech;
        foreach (string required in tech.requires_techs)
        {
            TechNodeConfig step = FindNextStepToward(culture, required, visited);
            if (step != null) return step;
        }
        return null;
    }

    private static void CompleteTech(string culture, string techId)
    {
        CultureTechState state = GetState(culture);
        if (!state.researched_techs.Contains(techId)) state.researched_techs.Add(techId);
        state.timestamps[$"tech:{techId}"] = World.world?.getCurWorldTime() ?? 0d;
        if (state.current_tech == techId) state.current_tech = "";
        state.tech_progress.Remove(techId);
        if (state.player_target == techId) state.player_target = "";
        ResearchedCache.Remove(culture);
    }

    #endregion

    #region 玩家操作

    public static void SetPlayerTarget(string culture, string techId)
    {
        if (!CultureService.IsValidCulture(culture)) return;
        CultureTechState state = GetState(culture);
        state.player_target = techId ?? "";
        // 换目标不丢进度：已经投进去的研究点按技术各自保存
        if (_techs.TryGetValue(techId ?? "", out TechNodeConfig tech) && ArePrerequisitesMet(culture, tech))
            state.current_tech = techId;
    }

    // 上帝模式：直接完成(连同缺的前置技术和材料)
    public static void ForceResearch(string culture, string techId)
    {
        if (!CultureService.IsValidCulture(culture) || !_techs.TryGetValue(techId ?? "", out TechNodeConfig tech)) return;
        ForceResearchRecursive(culture, tech, new HashSet<string>());
    }

    private static void ForceResearchRecursive(string culture, TechNodeConfig tech, HashSet<string> visited)
    {
        if (!visited.Add(tech.id) || HasTech(culture, tech.id)) return;
        foreach (string required in tech.requires_techs)
            if (_techs.TryGetValue(required, out TechNodeConfig parent)) ForceResearchRecursive(culture, parent, visited);
        foreach (string material in tech.requires_materials) ForceDiscover(culture, material);
        CompleteTech(culture, tech.id);
    }

    public static void ForceDiscover(string culture, string materialId)
    {
        if (!CultureService.IsValidCulture(culture) || !_materials.ContainsKey(materialId ?? "")) return;
        CultureTechState state = GetState(culture);
        if (state.discovered_materials.Contains(materialId)) return;
        state.discovered_materials.Add(materialId);
        state.timestamps[$"mat:{materialId}"] = World.world?.getCurWorldTime() ?? 0d;
    }

    public static void ForceResearchAll(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return;
        foreach (TechMaterialConfig material in Config.materials) ForceDiscover(culture, material.id);
        foreach (TechNodeConfig tech in Config.techs.OrderBy(tech => tech.tier)) ForceResearch(culture, tech.id);
    }

    public static void ResetCulture(string culture)
    {
        if (!_states.Remove(culture ?? "")) return;
        ResearchedCache.Remove(culture);
    }

    #endregion
}
