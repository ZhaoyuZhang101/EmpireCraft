using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GeneralSystems;

// 公司与商号的规则(用户 2026-11-28 "可是古代的公司不是什么股份有限公司什么的""能不能模拟历史，做出更符合的显示"
// "目前已经有的我希望都能和我的政体编辑器兼容")。都在模组根目录 CompanyRules.json，政体编辑器「杂项配置 → 公司与商号」可改：
//   · forms 企业形态：按本国科技时代逐档改组(古代商号、工业时代的公司与局、电气时代公司法下的有限公司、现代集团)，
//     每档能不能上市、结行会、得皇商特许、国库入股；本国进到新的一档那年记一笔(reform)；
//   · families 文化分系、names 各系各形态的起名格式、tables 起字号的字表；
//   · company / guild / charter / stock / conglomerate 各段数值(公司竞争与外资、行会与皇商、特许公司、股市、财阀)。
// 文件缺项按这里的默认值；读不进整份按默认(日志记 [公司])。名字格式是"简体|English|繁體"三段(同剧本地名，见 WorldMapData.PickParts)，
// 占位：{head} 字号、{trade} 行业、{shop} 店名(按行业)、{city} 城名、{city1} 城名头一个字、{realm} 国名。
public sealed class CompanyForm
{
    public string id = "";
    // 本国科技时代到这一档起用这种形态(见 TechnologySystem.GetEraTier)；公司规模至少这么大才用(集团)
    public int era_min;
    public int min_scale;
    // 能不能上市、同业结行会定价、官府特许皇商专营、国库入股
    public bool listed, guild, license, stake;
    // 形态的名字与本国进到这一档那年记的一笔
    public string label = "";
    public string reform = "";
}

public sealed class CompanyFormNames
{
    // 字号怎么取(按权重)：hao 两个吉字、surname 东家的姓、city 总部的城名、prefix 通用名头、yago 屋号吉字
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> heads = new();
    // 私营的全名；东家的姓作字号时另用的(空 = 同 private)；地图上标的简称(空 = 全名)
    public string @private = "", private_surname = "", brief = "";
    // 官办的全名；君主国以外另用的(空 = 同 state)；简称(空 = 全名)；按行业另给的全名
    public string state = "", state_republic = "", state_brief = "";
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, string> state_sectors = new();
    // 店名 {shop}(按行业，default 是别的行业)
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, string> shops = new();
}

// 公司的账、竞争、并购、外资、国家持股
public sealed class CompanyNumbers
{
    // 从第几档时代起厂区归商号、公司经营；第几档起兼并与垄断(托拉斯、财阀、外资)
    public int min_era = 0, trust_era = 8;
    public float margin = 0.1f, start_reputation = 25f;
    public float merge_chance = 0.25f, early_merge_chance = 0.05f, price_war_chance = 0.15f, fail_chance = 0.2f, group_buy_chance = 0.15f;
    public float gouge_from = 0.5f, gouge_per_share = 0.6f, gouge_base = 0.05f, max_gouge = 0.3f, price_war_cut = -0.08f;
    public float foreign_bid_chance = 0.15f, foreign_remit = 0.4f, weaker_share = 0.8f;
    public float branch_min_cost = 200f, branch_cost_share = 1f;
    public int max_branches = 3;
    public float influence_at = 0.3f, control_at = 0.6f;
    public int seizure_grudge_years = 20, min_group_scale = 4, beholden_opinion = 20, seizure_opinion = -40;
    public float stake_step = 0.1f, max_stake = 0.9f, control_stake = 0.5f, shield_stake = 0.3f;
}

// 古代：同业行会定价、官府特许皇商专营
public sealed class GuildNumbers
{
    // 同业的商号到几家结行会、行会定价加几成
    public int guild_min_firms = 3;
    public float guild_markup = 0.04f;
    // 每年有几成机会把一个行业特许给最大的商号；皇商多得几成价、每年把盈利的几成交官府、特许几年
    public float license_chance = 0.1f, license_markup = 0.12f, license_fee = 0.3f;
    public int license_years = 30;
    // 同业别家每年掉几点声誉
    public float license_squeeze = 2f;
}

// 特许公司(东印度公司这类殖民地政权)
public sealed class CharterNumbers
{
    // 特许状几年一续；续一回交几成年入、至少多少；国库占几成股；头两家大股东各几成(余下散户)
    public int years = 20;
    public float renew_fee_share = 0.5f, renew_fee_min = 100f;
    public float state_share = 0.4f, first_holder = 0.25f, second_holder = 0.1f;
    // 国库超出储备的余钱每年几成分红；做到几座城收归国有；特许状到期时续的机会
    public float dividend_share = 0.5f;
    public int nationalize_cities = 6;
    public float renew_chance = 0.7f;
}

public sealed class StockNumbers
{
    public string exchange_tech = "banking";
    public int shares = 100, min_firms = 3, years = 30;
    public float list_value = 120f, list_reputation = 40f, ipo_raise = 0.15f, owner_stake = 0.6f;
}

public sealed class ConglomerateNumbers
{
    public int min_industry = 6, industry_per_house = 12, max_houses = 4;
    public float found_chance = 0.25f, start_share = 0.08f, growth = 0.02f, max_share = 0.35f, max_monopoly = 0.8f;
    public float donation_scale = 300f, house_vote = 1.08f, output_per_monopoly = 0.15f;
    public int house_terms = 3, scandal_years = 8, antitrust_cooldown_years = 15;
    public float scandal_chance = 0.15f, bailout_share = 0.15f;
}

public sealed class CompanyRulesConfig
{
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<CompanyForm> forms = CompanyRules.DefaultForms();
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, List<string>> families = CompanyRules.DefaultFamilies();
    public string default_family = "west";
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, Dictionary<string, CompanyFormNames>> names = CompanyRules.DefaultNames();
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, string> tables = CompanyRules.DefaultTables();
    public CompanyNumbers company = new();
    public GuildNumbers guild = new();
    public CharterNumbers charter = new();
    public StockNumbers stock = new();
    public ConglomerateNumbers conglomerate = new();
}

public static class CompanyRules
{
    public const string FileName = "CompanyRules.json";
    // 特许公司的"行业"(不是厂区，见 CompanySystem 特许公司一段)
    public const string CharterSector = "charter";

    private static CompanyRulesConfig _config;

    public static CompanyRulesConfig Config
    {
        get
        {
            if (_config != null) return _config;
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, FileName);
                _config = File.Exists(path) ? JsonConvert.DeserializeObject<CompanyRulesConfig>(File.ReadAllText(path)) : null;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][公司] {FileName} 读取失败，按默认规则：{exception.Message}");
                _config = null;
            }
            _config ??= new CompanyRulesConfig();
            Normalize(_config);
            return _config;
        }
    }

    // 编辑器改完、读档时重读
    public static void Reload() => _config = null;

    public static CompanyNumbers Numbers => Config.company;
    public static GuildNumbers Guild => Config.guild;
    public static CharterNumbers Charter => Config.charter;
    public static StockNumbers Stock => Config.stock;
    public static ConglomerateNumbers Houses => Config.conglomerate;

    private static void Normalize(CompanyRulesConfig config)
    {
        config.forms = (config.forms ?? new List<CompanyForm>()).Where(form => form != null && !string.IsNullOrEmpty(form.id)).ToList();
        if (config.forms.Count == 0) config.forms = DefaultForms();
        // 按时代排，同一时代先排规模门槛低的
        config.forms = config.forms.OrderBy(form => form.era_min).ThenBy(form => form.min_scale).ToList();
        config.families ??= DefaultFamilies();
        config.names ??= DefaultNames();
        config.tables ??= DefaultTables();
        config.company ??= new CompanyNumbers();
        config.guild ??= new GuildNumbers();
        config.charter ??= new CharterNumbers();
        config.stock ??= new StockNumbers();
        config.conglomerate ??= new ConglomerateNumbers();
        if (config.stock.shares <= 0) config.stock.shares = 100;
        if (string.IsNullOrEmpty(config.default_family)) config.default_family = "west";
    }

    #region 形态

    public static IReadOnlyList<CompanyForm> Forms => Config.forms;

    public static CompanyForm Form(int index) => index >= 0 && index < Forms.Count ? Forms[index] : Forms[0];

    public static int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < Forms.Count; i++)
            if (Forms[i].id == id) return i;
        return -1;
    }

    // 公司的形态；没定的(旧档)、编辑器里删掉了的按头一档算，下一年对账时改正(见 CompanySystem.Refit)
    public static CompanyForm FormOf(CompanyData company) => Form(IndexOf(company?.form));

    // 这个时代、这样规模的公司该是哪一档(取够得着的最后一档)
    public static int FormFor(int era, int scale)
    {
        int best = 0;
        for (int i = 0; i < Forms.Count; i++)
            if (era >= Forms[i].era_min && scale >= Forms[i].min_scale) best = i;
        return best;
    }

    // 一国眼下的形态(不看规模)：界面里"商号"还是"公司"的叫法跟它走
    public static int RealmForm(int era)
    {
        int best = 0;
        for (int i = 0; i < Forms.Count; i++)
            if (era >= Forms[i].era_min && Forms[i].min_scale <= 0) best = i;
        return best;
    }

    public static string Label(CompanyForm form) => WorldMapData.PickParts(form?.label);

    #endregion

    #region 起名

    public static string Family(string culture)
    {
        if (!string.IsNullOrEmpty(culture))
            foreach (KeyValuePair<string, List<string>> pair in Config.families)
                if (pair.Value != null && pair.Value.Contains(culture)) return pair.Key;
        return Config.default_family;
    }

    // 某系某形态的起名格式：文件里没有的退回默认，默认里也没有的退回 west
    public static CompanyFormNames Names(string family, string form)
    {
        if (Lookup(Config.names, family, form) is { } names) return names;
        Dictionary<string, Dictionary<string, CompanyFormNames>> fallback = DefaultNames();
        return Lookup(fallback, family, form) ?? Lookup(Config.names, Config.default_family, form) ??
               Lookup(fallback, "west", form) ?? Lookup(fallback, "west", "corp") ?? new CompanyFormNames();
    }

    private static CompanyFormNames Lookup(Dictionary<string, Dictionary<string, CompanyFormNames>> table, string family, string form) =>
        table != null && family != null && table.TryGetValue(family, out Dictionary<string, CompanyFormNames> forms) && forms != null &&
        form != null && forms.TryGetValue(form, out CompanyFormNames names) ? names : null;

    // 字表：文件 tables 里有的按它(三段)，没有的看语言文件 company_<名字>
    public static string[] Table(string name)
    {
        string text = Config.tables != null && Config.tables.TryGetValue(name, out string packed) && !string.IsNullOrWhiteSpace(packed)
            ? WorldMapData.PickParts(packed)
            : NeoModLoader.General.LM.Get("company_" + name);
        return string.IsNullOrEmpty(text) || text == "company_" + name
            ? Array.Empty<string>()
            : text.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
    }

    #endregion

    #region 默认

    // 三段字："简体|English|繁體"
    private static string T(string cz, string en, string ch) => cz + "|" + en + "|" + ch;

    public static List<CompanyForm> DefaultForms() => new()
    {
        new CompanyForm
        {
            id = "shop", era_min = 0, guild = true, license = true, label = T("商号", "Merchant house", "商號")
        },
        new CompanyForm
        {
            id = "firm", era_min = 7, listed = true, stake = true, label = T("公司", "Company", "公司"),
            reform = T("{realm}兴办实业，旧式商号纷纷改称公司、局", "{realm} takes up modern industry; the old merchant houses restyle themselves as companies",
                "{realm}興辦實業，舊式商號紛紛改稱公司、局")
        },
        new CompanyForm
        {
            id = "corp", era_min = 8, listed = true, stake = true, label = T("有限公司", "Limited company", "有限公司"),
            reform = T("{realm}颁行公司法，各公司改组为有限责任的股份公司", "{realm} enacts a companies act; firms reorganise with limited liability",
                "{realm}頒行公司法，各公司改組為有限責任的股份公司")
        },
        new CompanyForm
        {
            id = "group", era_min = 9, min_scale = 8, listed = true, stake = true, label = T("集团", "Group", "集團"),
            reform = T("{realm}的大公司纷纷改组为集团", "The great firms of {realm} regroup as conglomerates", "{realm}的大公司紛紛改組為集團")
        }
    };

    public static Dictionary<string, List<string>> DefaultFamilies() => new()
    {
        ["huaxia"] = new List<string> { "Huaxia", "China", "Aosong", "Shanhai" },
        ["japan"] = new List<string> { "Japan" }
    };

    public static Dictionary<string, string> DefaultTables() => new()
    {
        ["yago_japan"] = T("大黑,惠比寿,丸,万,鹤,龟,升,大和,三河,近江", "Daikoku,Ebisu,Maru,Yorozu,Tsuru,Kame,Masu,Yamato,Mikawa,Omi",
            "大黑,惠比壽,丸,萬,鶴,龜,升,大和,三河,近江")
    };

    private static Dictionary<string, string> D(params (string key, string value)[] items) =>
        items.ToDictionary(item => item.key, item => item.value);

    private static Dictionary<string, int> W(params (string key, int value)[] items) =>
        items.ToDictionary(item => item.key, item => item.value);

    public static Dictionary<string, Dictionary<string, CompanyFormNames>> DefaultNames()
    {
        var modernHeads = W(("hao", 50), ("city", 30), ("prefix", 20));
        var huaxia = new Dictionary<string, CompanyFormNames>
        {
            ["shop"] = new()
            {
                heads = W(("hao", 70), ("city", 30)),
                @private = T("{head}{shop}", "{head} {shop}", "{head}{shop}"),
                state = T("{city}官{trade}局", "{city} {trade} Bureau", "{city}官{trade}局"),
                state_sectors = D(
                    ("kiln", T("{city}官窑", "{city} Imperial Kilns", "{city}官窯")),
                    ("mint", T("宝{city1}局", "{city} Mint", "寶{city1}局")),
                    ("iron_foundry", T("{city}铁冶务", "{city} Iron Bureau", "{city}鐵冶務")),
                    ("wooden_shipyard", T("{city}船厂", "{city} Shipyard", "{city}船廠")),
                    ("gunpowder_mill", T("{city}火药局", "{city} Powder Bureau", "{city}火藥局")),
                    ("print_shop", T("{city}官书局", "{city} Official Press", "{city}官書局"))),
                shops = D(
                    ("default", T("号", "& Co.", "號")),
                    ("kiln", T("窑", "Kilns", "窯")),
                    ("mint", T("钱庄", "Bank", "錢莊")),
                    ("paper_mill", T("纸坊", "Paper House", "紙坊")),
                    ("print_shop", T("书坊", "Press", "書坊")),
                    ("iron_foundry", T("冶坊", "Ironworks", "冶坊")),
                    ("wooden_shipyard", T("船坞", "Shipwrights", "船塢")),
                    ("gunpowder_mill", T("硝坊", "Powder House", "硝坊")))
            },
            ["firm"] = new()
            {
                heads = new Dictionary<string, int>(modernHeads),
                @private = T("{head}{trade}公司", "{head} {trade} Company", "{head}{trade}公司"),
                brief = T("{head}{trade}", "{head} {trade}", "{head}{trade}"),
                state = T("{city}{trade}局", "{city} {trade} Bureau", "{city}{trade}局"),
                state_sectors = D(
                    ("steel_mill", T("{city}铁厂", "{city} Ironworks", "{city}鐵廠")),
                    ("light_factory", T("{city}织布局", "{city} Cotton Mill", "{city}織布局")),
                    ("light_shipyard", T("{city}船政局", "{city} Navy Yard", "{city}船政局")),
                    ("heavy_shipyard", T("{city}船政局", "{city} Navy Yard", "{city}船政局")),
                    ("heavy_factory", T("{city}机器制造局", "{city} Arsenal", "{city}機器製造局")),
                    ("gunpowder_mill", T("{city}火药局", "{city} Powder Bureau", "{city}火藥局")))
            },
            ["corp"] = new()
            {
                heads = new Dictionary<string, int>(modernHeads),
                @private = T("{head}{trade}股份有限公司", "{head} {trade} Co., Ltd.", "{head}{trade}股份有限公司"),
                brief = T("{head}{trade}", "{head} {trade}", "{head}{trade}"),
                state = T("国营{city}{trade}总厂", "{city} State {trade} Works", "國營{city}{trade}總廠"),
                state_brief = T("国营{city}{trade}", "State {city} {trade}", "國營{city}{trade}")
            },
            ["group"] = new()
            {
                heads = new Dictionary<string, int>(modernHeads),
                @private = T("{head}集团", "{head} Group", "{head}集團"),
                state = T("{city}{trade}集团", "{city} {trade} Group", "{city}{trade}集團")
            }
        };
        var japan = new Dictionary<string, CompanyFormNames>
        {
            ["shop"] = new()
            {
                heads = W(("city", 60), ("yago", 40)),
                @private = T("{head}{shop}", "{head}{shop}", "{head}{shop}"),
                state = T("{city}御用{trade}所", "{city} Official {trade} Works", "{city}御用{trade}所"),
                state_sectors = D(
                    ("mint", T("{city}钱座", "{city} Mint", "{city}錢座")),
                    ("kiln", T("{city}藩窑", "{city} Domain Kiln", "{city}藩窯"))),
                shops = D(
                    ("default", T("屋", "-ya", "屋")),
                    ("kiln", T("窑", " Kiln", "窯")),
                    ("mint", T("两替屋", " Money Changers", "兩替屋")))
            },
            ["firm"] = new()
            {
                heads = W(("surname", 45), ("city", 30), ("prefix", 25)),
                @private = T("{head}{trade}会社", "{head} {trade} Kaisha", "{head}{trade}會社"),
                private_surname = T("{head}组", "{head}-gumi", "{head}組"),
                brief = T("{head}{trade}", "{head} {trade}", "{head}{trade}"),
                state = T("官营{city}{trade}所", "State {city} {trade} Works", "官營{city}{trade}所")
            },
            ["corp"] = new()
            {
                heads = W(("surname", 45), ("city", 30), ("prefix", 25)),
                @private = T("{head}{trade}株式会社", "{head} {trade} K.K.", "{head}{trade}株式會社"),
                brief = T("{head}{trade}", "{head} {trade}", "{head}{trade}"),
                state = T("国营{city}{trade}所", "National {city} {trade} Works", "國營{city}{trade}所"),
                state_brief = T("国营{city}{trade}", "National {city} {trade}", "國營{city}{trade}")
            },
            ["group"] = new()
            {
                heads = W(("surname", 45), ("city", 30), ("prefix", 25)),
                @private = T("{head}集团", "{head} Group", "{head}集團"),
                state = T("{city}{trade}公社", "{city} {trade} Public Corp.", "{city}{trade}公社")
            }
        };
        var west = new Dictionary<string, CompanyFormNames>
        {
            ["shop"] = new()
            {
                heads = W(("surname", 60), ("city", 40)),
                @private = T("{head}{shop}", "{head} {shop}", "{head}{shop}"),
                private_surname = T("{head}父子{shop}", "{head} & Sons {shop}", "{head}父子{shop}"),
                brief = T("{head}{shop}", "{head} {shop}", "{head}{shop}"),
                state = T("皇家{city}{trade}工场", "Royal {city} {trade} Manufactory", "皇家{city}{trade}工場"),
                state_republic = T("{city}官办{trade}工场", "{city} Public {trade} Manufactory", "{city}官辦{trade}工場"),
                state_sectors = D(
                    ("mint", T("{city}铸币厂", "{city} Mint", "{city}鑄幣廠")),
                    ("wooden_shipyard", T("{city}海军船坞", "{city} Naval Dockyard", "{city}海軍船塢"))),
                shops = D(
                    ("default", T("商行", "Trading House", "商行")),
                    ("kiln", T("陶坊", "Pottery", "陶坊")),
                    ("mint", T("银号", "Bank", "銀號")),
                    ("paper_mill", T("纸坊", "Paper Mill", "紙坊")),
                    ("print_shop", T("印书馆", "Press", "印書館")),
                    ("iron_foundry", T("铁坊", "Ironworks", "鐵坊")),
                    ("wooden_shipyard", T("船坞", "Shipyard", "船塢")),
                    ("gunpowder_mill", T("火药坊", "Powder Mill", "火藥坊")))
            },
            ["firm"] = new()
            {
                heads = W(("surname", 35), ("city", 35), ("prefix", 30)),
                @private = T("{head}{trade}公司", "{head} {trade} & Co.", "{head}{trade}公司"),
                brief = T("{head}{trade}", "{head} {trade}", "{head}{trade}"),
                state = T("皇家{city}{trade}厂", "Royal {city} {trade} Works", "皇家{city}{trade}廠"),
                state_republic = T("{city}国立{trade}厂", "{city} National {trade} Works", "{city}國立{trade}廠")
            },
            ["corp"] = new()
            {
                heads = W(("surname", 35), ("city", 35), ("prefix", 30)),
                @private = T("{head}{trade}有限公司", "{head} {trade} Ltd.", "{head}{trade}有限公司"),
                brief = T("{head}{trade}", "{head} {trade}", "{head}{trade}"),
                state = T("{city}国营{trade}公司", "{city} State {trade} Company", "{city}國營{trade}公司"),
                state_brief = T("国营{city}{trade}", "State {city} {trade}", "國營{city}{trade}")
            },
            ["group"] = new()
            {
                heads = W(("surname", 35), ("city", 35), ("prefix", 30)),
                @private = T("{head}集团", "{head} Group", "{head}集團"),
                state = T("{city}国营{trade}集团", "{city} State {trade} Group", "{city}國營{trade}集團")
            }
        };
        return new Dictionary<string, Dictionary<string, CompanyFormNames>> { ["huaxia"] = huaxia, ["japan"] = japan, ["west"] = west };
    }

    #endregion
}
