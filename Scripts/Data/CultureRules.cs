using EmpireCraft.Scripts.Enums;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using UnityEngine;

namespace EmpireCraft.Scripts.Data;
public class CultureRule
{
    public string name;
    public string translate_ch;
    public string translate_cz;
    public string translate_en;
    // 文化地图图层（图层染色/悬停高亮）使用的预制颜色，"#RRGGBB" 十六进制格式。
    // 不配置时 GetCultureColor 会退化成按文化名哈希取一个固定但随意的颜色，
    // 不会报错，但同名文化每次读表颜色都一样（哈希是确定性的）。
    public string color;
    // 哪些物种默认属于这个文化(物种 id，比如 "human"、"civ_fox")。可选；
    // 会覆盖代码里 ConfigData.speciesCulturePair 的默认映射，玩家的 CultureSpeciesPairPlayerConfig.json 仍然优先。
    public List<string> species;
    public Setting setting;
}

public class Setting
{
    public List<string> traits = new List<string>();
    // 只供 EmpireCraft 政治系统读取，不会注册成原版 CultureTrait。
    // 例如 strong_unification 会启用严格的军阀时期、易帜与统一继承逻辑。
    public List<string> political_traits = new List<string>();
    public RegimeType regime;
    // 这个文化归属哪条制度科技线（对应 InstitutionTrees/<线 id>.json 的文件名）。
    // 制度归属文化而不是政体：政体会被制度节点的 change_regime 效果改掉（华夏线的郡县制
    // 会把周制改成律令制），但文化所属的线终身不变。
    // 留空或写了个目录里不存在的 id，会退化成 InstitutionTrees/Settings.json 里的 default_line。
    public string institution_line = "";
    // 天下标识(见 TianxiaSystem)：写了同一个标识的文化同属一个天下——相邻的帝国法理自动并成一个天下，
    // 还没有帝国法理的地方也按它连片。留空 = 本文化自成一个天下
    public string tianxia = "";
    // 列岛天下(见 TianxiaSystem.IsArchipelago，用户 2026-10-21 选"列岛天下")：标了的文化(日本)，隔着海峡、在水师辐射里的
    // 相邻天下也自动并成一个，帝国核心也收隔海的同文化法理；不标的照旧只认同一块陆地
    public bool archipelago;
    // 开局阶段：写本线的一个制度节点，文化开局时这个节点及其全部前置都已施行(被它取代的不算)；
    // 留空从本线的根节点起步(华夏线是原始群)
    public string start_institution = "";
    // 本文化创的宗教的类型(见 ReligionFaith：Folk/Church/Islam/Judaism/Dharmic/Sanjiao/Shinto/Tengri)；
    // 留空按天下标识取(罗马 → Church、乌玛 → Islam……，其余 Folk)
    public string faith = "";
    // 姓氏演变(只对华夏线有效，见 PrehistoricClanSystem)：原始群无姓、宗法时男子称氏女子称姓、平民无氏，郡县后姓氏合一；
    // false 时一直正常称姓
    public bool surname_evolution;
    public CitySetting City;
    public KingdomSetting Kingdom;
    public ClanSetting Clan;
    public FamilySetting Family;
    public UnitSetting Unit;
    public string Religion = "";
    // 政党命名：理念 → 词库(见 PartySystem)
    public PartySetting Party;
    // 宪法：名称词库与本文化的制宪倾向(见 ConstitutionSystem)
    public ConstitutionSetting Constitution;
    // 村镇命名(见 SettlementNaming)：命名方式的分量与各词库；没配的项用 Locales/Cultures/Settlements/ 里的通用词库
    public SettlementSetting Settlement;
    // 无人区命名(见 WildernessNaming，用户 2026-10-29 "依据最近的文化进行命名")：各类型的地名词库与后缀；没配的用 Locales/Cultures/Wilderness/ 的通用词库
    public EmpireCraft.Scripts.GeneralSystems.WildernessSetting Wilderness;
    // 美号(见 RealmNamingSystem.Founding)：词库与前缀
    public GrandNameSetting GrandName;
}

// 美号：称帝没有王国法理可用、篡位者没有封国、外族入主改行本文化制度而没有本文化的主法理时，取美号作国号
// (用户 2026-10-27："我的政体编辑里面也要把这套规则加进去允许玩家自定义")
public class GrandNameSetting
{
    // 美号词库(本文件夹 <文化><词库名>.csv，可写"别的文化:词库名")；没配用 GrandNames，本文化没有这个词库用华夏的
    public string group = "";
    // 前缀(帝国名前缀：great 大、Former 前、Later 后……)；不写默认 great，写 none 不加前缀。
    // 词库里的词以前缀字开头的，有效国号去掉那个字(大乾 → 前缀"大" + 国号"乾")
    public string prefix;
}

// 村镇命名(用户 2026-10-19："按照 Locales/Cultures 里面的名词库来，依据自己的文化；文化配置里加几种村镇命名方式，
// 比如按照姓氏(罗家村)、按照地势(什么什么山村)、按照字库，文化配置里可以绑定独有的村镇字库")。
// 词库名同其它词库：本文件夹 <文化><词库名>.csv，可写"别的文化:词库名"；没写的用 Locales/Cultures/Settlements/<默认名>.csv。
// 词库里英文一列以"_"开头表示前面加空格(如"_Farm")，单写"_"表示空(没有后缀)。
public class SettlementSetting
{
    // 命名方式 → 分量：surname 姓氏(本城宗族的姓 + 姓氏后缀，如罗家村)、terrain 地势(前缀 + 地势词 + 后缀，如青石山村)、
    // words 字库(字库词 + 后缀，如桃花村)。没配：都按姓氏
    public Dictionary<string, float> village_modes;
    public Dictionary<string, float> town_modes;
    public Dictionary<string, float> fort_modes;
    // 字库方式的词(默认 VillageWords / TownWords)
    public string village_words = "";
    public string town_words = "";
    public string fort_words = "";
    // 后缀(默认 VillageSuffix / TownSuffix / FortSuffix)
    public string village_suffix = "";
    public string town_suffix = "";
    public string fort_suffix = "";
    // 姓氏方式的后缀(默认 SurnameSuffix)；姓氏词库(本城没有宗族时用，默认取本文化 Family 的第一个词库)
    public string surname_suffix = "";
    public string surname_words = "";
    // 地势方式：地势前面的词(默认 TerrainPrefix)、地势之后的后缀(默认 TerrainSuffix)；
    // 各地势的地势词：mountain 山、hill 丘岗、water 河湖、coast 海边、forest 林、plain 平原、desert 沙漠、swamp 沼泽
    // (默认 Terrain<地势首字母大写>，如 TerrainMountain)
    public string terrain_prefix = "";
    public string terrain_suffix = "";
    public Dictionary<string, string> terrain;
    // 毡帐村镇(游牧营地的城立的，见 RuralSettlement.yurt)：各方式的分量(没配用上面村、镇的分量)、字库方式的词(没配用村、镇的字库)、
    // 后缀(没配用通用的 YurtVillageSuffix / YurtTownSuffix)；姓氏、地势方式也接毡帐的后缀，名字与农耕村镇分得开
    public Dictionary<string, float> yurt_village_modes;
    public Dictionary<string, float> yurt_town_modes;
    public string yurt_words = "";
    public string yurt_village_suffix = "";
    public string yurt_town_suffix = "";
}

public class ConstitutionSetting
{
    // 正式宪法名称词库(本文件夹 <文化><词库名>.csv，可写"别的文化:词库名")，{0} 代入国号；没配用"{0}宪法"
    public string name_group = "";
    // 临时约法名称词库，规则同上；没配用"{0}临时约法"
    public string provisional_name_group = "";
    // 本文化的制宪倾向：条款名 → 方案名(如 "territory": "Federal")，
    // 制宪会议起草时覆盖各党的主张(条款与方案名见 ConstitutionTemplates.json)
    public Dictionary<string, string> clauses;
}

public class PartySetting
{
    // key 为理念(PartyIdeology 的名字，如 "Socialism")，value 为词库名；
    // 词库文件放在 Locales/Cultures/Culture_<文化>/<文化><词库名>.csv，格式与其他命名词库相同(key,cz,en,ch)。
    // 没配的理念用 Locales/Cultures/PartyNames/Party<理念>.csv 里的通用党名。
    public Dictionary<string, string> groups;
    // 改制共和后的国号后缀词库：理念 → 词库名(同上的放置规则)；没配用 PartyNames/Suffix<理念>.csv
    public Dictionary<string, string> suffix_groups;
    // 未组建政府的现代势力(军阀、武装)称呼词库：理念 → 词库名；没配用 PartyNames/Untitled<理念>.csv。
    // 称呼里写 {0} 时代入势力名号(如"{0}系军阀")，否则接在名号后面(如 晋 + 工农红军)。
    public Dictionary<string, string> untitled_groups;
    // 已组建政府但尚非中央的临时政府称呼词库：理念 → 词库名；没配用 PartyNames/Provisional<理念>.csv，{0} 规则同上。
    public Dictionary<string, string> provisional_groups;
    // 以上各项的词库名都可以写成"别的文化:词库名"(如 "Huaxia:UntitledCommunism")，直接引用那个文化的词库，
    // 同一套称呼给几个文化共用时不必复制文件。
}

public class UnitSetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool is_invert;
    // 上古人名词库(男、女)：本文化还在原始阶段(宗法以前)时，新生儿取这里的古朴名字(弃、契、禹、嫄)；不配照常起名
    public string ancient_male_group = "";
    public string ancient_female_group = "";
}

public class FamilySetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool has_sex_post;
    public bool use_local_as_lastname;
}

public class ClanSetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool has_sex_post;
    public bool use_local_as_lastname;
    // 古姓词库(本文件夹 <文化><词库名>.csv，可写"别的文化:词库名")：原始群、母系氏族阶段新立的氏族从这里取姓
    // (华夏是姬、姜、姒、嬴这类女字旁古姓)；不配就照常起姓
    public string ancient_group = "";
    // 氏的词库：分出支脉起氏时，做官的族人可能以官为氏(司马、司徒)，平民可能以职业为氏(陶、巫、匠)；不配只按国、邑、祖名取
    public string shi_office_group = "";
    public string shi_craft_group = "";
    public string[] sex_post_Male;
    public string[] sex_post_Female;
}

public class KingdomSetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
    public bool english_type_prefix = true;
    // 部落、方国国号词库：本文化还在原始阶段(宗法以前)时，新立的国取这里的国号(有熊、有虞、陶唐、鬼方)；不配照常起名
    public string ancient_group = "";
}

public class CitySetting
{
    public Dictionary<string, string> groups;
    public OnomasticsType[] rule;
    public int name_pos;
}

public static class OnomasticsRule
{
    public static Dictionary<string, Setting> ALL_CULTURE_RULE = new Dictionary<string, Setting>();
    public static Dictionary<string, (string ch, string cz, string en)> ALL_CULTURE_TRANSLATE = new Dictionary<string, (string ch, string cz, string en)>();
    // 每种文化的预制染色（来自 CultureRule.jsonc 的 color 字段），供地图上
    // 文化图层的地块底色/悬停高亮使用，避免继续用原版自带的、跟模组文化实体对不上
    // 的颜色，也避免像铭牌图标那样只是"看着还行"的哈希取色（那个是给旗帜图标用的，
    // 玩家不会拿它跟别的文化的颜色反复比较；地图底色不一样，需要真正稳定可配置）。
    public static Dictionary<string, Color> ALL_CULTURE_COLOR = new Dictionary<string, Color>();
    // 每个文化一个文件夹：Locales/Cultures/Culture_<文化>/CultureRule.jsonc 放规则，同文件夹放各种词库 CSV。
    // 加一个文化只要新建这个文件夹，不用改代码也不用动别的文件。
    // 旧版的根目录 CultureRulesConfig.json(一个数组)如果还在也照读，文件夹里的同名文化覆盖它。
    public const string RuleFileName = "CultureRule.jsonc";

    public static void ReadSetting()
    {
        var rules = new List<CultureRule>();
        string legacyPath = Path.Combine(ModClass._declare.FolderPath, "CultureRulesConfig.json");
        if (File.Exists(legacyPath))
        {
            try
            {
                rules.AddRange(JsonConvert.DeserializeObject<List<CultureRule>>(File.ReadAllText(legacyPath)) ?? new List<CultureRule>());
            }
            catch (Exception e)
            {
                LogService.LogWarning($"[EmpireCraft] 读取 {legacyPath} 失败: {e.Message}");
            }
        }
        foreach (string file in CultureRuleFiles())
        {
            try
            {
                CultureRule rule = JsonConvert.DeserializeObject<CultureRule>(File.ReadAllText(file));
                if (rule == null) continue;
                // 没写 name 就用文件夹名(Culture_Xxx → Xxx)
                if (string.IsNullOrWhiteSpace(rule.name))
                    rule.name = Path.GetFileName(Path.GetDirectoryName(file))!.Substring("Culture_".Length);
                rules.Add(rule);
            }
            catch (Exception e)
            {
                LogService.LogWarning($"[EmpireCraft] 文化配置 {file} 格式有误，已跳过: {e.Message}");
            }
        }
        foreach (CultureRule cultureRule in rules)
        {
            if (string.IsNullOrWhiteSpace(cultureRule?.name) || cultureRule.setting == null) continue;
            ALL_CULTURE_TRANSLATE[cultureRule.name] = (cultureRule.translate_ch, cultureRule.translate_cz, string.IsNullOrEmpty(cultureRule.translate_en)?cultureRule.name:cultureRule.translate_en);
            ALL_CULTURE_RULE[cultureRule.name] = cultureRule.setting;
            if (!string.IsNullOrEmpty(cultureRule.color) && ColorUtility.TryParseHtmlString(cultureRule.color, out Color parsedColor))
            {
                ALL_CULTURE_COLOR[cultureRule.name] = parsedColor;
            }
            if (cultureRule.species != null)
                foreach (string species in cultureRule.species.Where(id => !string.IsNullOrWhiteSpace(id)))
                    ConfigData.speciesCulturePair[species.Trim()] = cultureRule.name;
            LogService.LogInfo("载入文化配置"+cultureRule.name);
        }
    }

    public static IEnumerable<string> CultureRuleFiles()
    {
        string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        if (!Directory.Exists(root)) yield break;
        foreach (string dir in Directory.EnumerateDirectories(root, "Culture_*").OrderBy(d => d, StringComparer.Ordinal))
        {
            string file = Path.Combine(dir, RuleFileName);
            if (File.Exists(file)) yield return file;
        }
    }

    // 文化地图图层用的预制颜色。配置里没填 color、或者填的十六进制解析失败时，
    // 退化成按文化名哈希出一个稳定（同一次读表内每次都一样，但换个文化名字就会
    // 变别的颜色）的柔和色，而不是直接报错/给纯黑——这样即使漏填颜色，地图上
    // 也不会出现一块诡异的纯黑/纯白区域，只是不一定好看，需要回去补配置。
    public static Color GetCultureColor(this string culture)
    {
        if (!string.IsNullOrEmpty(culture) && ALL_CULTURE_COLOR.TryGetValue(culture, out Color color))
        {
            return color;
        }
        int hash = Math.Abs((culture ?? "").GetHashCode());
        float hue = (hash % 360) / 360f;
        Color fallback = Color.HSVToRGB(hue, 0.55f, 0.85f);
        fallback.a = 1f;
        return fallback;
    }

    // 文化归属的制度科技线 id。返回空串表示没配/配错，由 InstitutionDefinitionRegistry.ResolveLine
    // 兜底到 Settings.json 的 default_line —— 线是纯字符串而不是枚举，就是为了让加一条新线
    // 只需要往 InstitutionTrees/ 里丢一个 json，不用改代码。
    public static string GetInstitutionLine(this string culture)
    {
        return !string.IsNullOrEmpty(culture) && ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) &&
               !string.IsNullOrWhiteSpace(setting?.institution_line)
            ? setting.institution_line.Trim()
            : "";
    }

    // 文化所属的天下标识；没写就是文化自己
    public static string GetTianxiaId(this string culture)
    {
        if (string.IsNullOrEmpty(culture)) return "";
        return ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) && !string.IsNullOrWhiteSpace(setting?.tianxia)
            ? setting.tianxia.Trim()
            : culture;
    }

    // 文化配置里明写了天下标识
    public static bool HasExplicitTianxia(this string culture) =>
        !string.IsNullOrEmpty(culture) && ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) &&
        !string.IsNullOrWhiteSpace(setting?.tianxia);

    public static bool HasPoliticalTrait(this string culture, string trait)
    {
        if (string.IsNullOrWhiteSpace(culture) || string.IsNullOrWhiteSpace(trait) ||
            !ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)) return false;
        return setting?.political_traits?.Any(value =>
            string.Equals(value?.Trim(), trait, StringComparison.OrdinalIgnoreCase)) == true;
    }

    // 游牧(逐水草而居：游牧扩张、牧帐、放牧加倍)、劫掠传统(派系可提劫掠)：按文化特性判断，不按制度线——游牧一线也含阿兹特克、
    // 林地部族，他们不游牧也不劫掠；维京在罗马一线，却以劫掠闻名(用户 2026-10-19 选"加文化特性与节点过滤")
    public const string NomadTrait = "nomad", RaiderTrait = "raider";
    // 旱地故土文化适应旱地与农地；制度线本身不代表气候(印度也用阿拉伯线)。
    public const string DryNativeTrait = "dry_native";

    public static bool IsNomadCulture(this string culture) => culture.HasPoliticalTrait(NomadTrait);

    public static string GetCultureTranslate(this string culture)
    {
        var language = PlayerConfig.dict["language"].stringVal;
        var tc = ALL_CULTURE_TRANSLATE[culture];
        var translate = "";
        switch (language)
        {
            case "ch":
                translate = tc.ch;
                break;
            case "en":
                translate = tc.en;
                break;
            case "cz":
                translate = tc.cz;
                break;
            default:
                translate = tc.en;
                break;
        }
        translate = string.IsNullOrEmpty(translate) ? tc.en : translate;
        return translate;
    }
}
