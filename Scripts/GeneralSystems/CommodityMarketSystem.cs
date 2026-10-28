using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class CommodityConfig
{
    public string id = "";
    public string tier = "";
    public float price = 1f;
    public Dictionary<string, float> resources = new();
    public Dictionary<string, float> biomes = new();
    public List<string> craft_cultures = new();
    public float craft_per_thousand;
    public Dictionary<string, float> needs = new();
    public float per_soldier;
}

public sealed class CommodityRecipe
{
    public Dictionary<string, float> inputs = new();
    public Dictionary<string, float> outputs = new();
}

public sealed class CommodityFileConfig
{
    public int great_powers = 8;
    public float tariff = 0.2f;
    public List<CommodityConfig> goods = new();
    public Dictionary<string, CommodityRecipe> factories = new();
    public Dictionary<string, List<string>> techs = new();
    public Dictionary<string, List<string>> arms = new();
}

// 商品市场(用户 2026-10-28："市场经济，参考维多利亚三，允许大国之间组建市场，物资进行交易流通，缺少物资无法生产和发展
// 对应的科技和商品，还可以做奢侈品""这样子殖民就会有动力"；问答定：四档二十来种物资、大国领衔加关税同盟、缺货卡科技、
// 工厂与兵种、奢侈品与民心，市场窗口并进外交界面一起做)。
// 现有的 MarketSystem 照旧管粮木石金属四样实物在城与城之间调拨；这里是它之上一层按年结算的抽象市场：
//   · 市场：每个独立国(帝国按核心，附庸、殖民地往上算到宗主，见 EmpireCoreControl.Sovereign；朝贡国算进宗主帝国)领一个市场；
//     国力前 great_powers 名是大国，小国可以签关税同盟并入接壤大国的市场(KingdomExtraData.market_union_leader_id)；
//   · 产出(每城每年，见 Technology/Commodities.json)：物资账上一年的产出折算、城辖区内的群系(热带的香料、糖、烟草、棉、橡胶)
//     乘人手、本文化的手艺(丝绸、瓷器、茶)、工厂(投入不足按比例减产)；
//   · 需求：各阶层每千人的用度(奢侈品主要是贵族、官僚、商人、市民)、工厂投入、军队(打仗翻倍)；
//   · 市场之间：陆上接壤或商路相连的市场互通有无，进口方付价款(另加关税)给出口方，记"公家贸易"；
//   · 缺口：科技研究按离不开的物资的缺口变慢(见 TechnologySystem.AddResearch)，工厂减产(见 FactorySystem.Produce)，
//     兵种换装受阻(见 CorpsArmSystem.UpgradeOf)，奢侈品满足与否加减城的自然稳定、税收(见 CityStabilitySystem、PopulationEconomySystem)；
//   · 殖民的动力：本国市场缺、别的岛上的地出的物资，殖民船优先去那里(见 ColonySystem.FindSite)。
// 每年换年后分帧算各城的账，算完一次性结算各市场；读档后头一年没算出来之前，所有效果都按不缺货处理。
public static class CommodityMarketSystem
{
    private static CommodityFileConfig _config;
    private static Dictionary<string, CommodityConfig> _goods;

    private static CommodityFileConfig Config
    {
        get
        {
            if (_config != null) return _config;
            try
            {
                string path = Path.Combine(ModClass._declare.FolderPath, "Technology", "Commodities.json");
                _config = JsonConvert.DeserializeObject<CommodityFileConfig>(File.ReadAllText(path)) ?? new CommodityFileConfig();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][市场] 读商品表失败: {exception.Message}");
                _config = new CommodityFileConfig();
            }
            _goods = _config.goods.Where(good => !string.IsNullOrEmpty(good.id)).ToDictionary(good => good.id);
            return _config;
        }
    }

    private static Dictionary<string, CommodityConfig> Goods
    {
        get
        {
            _ = Config;
            return _goods;
        }
    }

    #region 市场

    public sealed class Market
    {
        public Kingdom leader;
        public readonly List<Kingdom> members = new();
        public readonly Dictionary<string, float> supply = new(), demand = new(), imports = new(), exports = new();
        public readonly Dictionary<string, float> shortage = new(), price = new();
        public bool great;
        public double power;
        public float trade_value;
        // 市场汇总窗口用(见 MarketWindow)：共有几城；各成员国一年的产出、需求(按底价折钱)；
        // 与各市场的买卖(对方领衔国编号 → 买进付的钱、卖出收的钱)；陆上接壤或商路相连、能通商的市场(领衔国编号)
        public int cities;
        public readonly Dictionary<long, (float made, float wanted)> memberValue = new();
        public readonly Dictionary<long, float> boughtFrom = new(), soldTo = new();
        public readonly HashSet<long> linked = new();
    }

    // 一样物资的底价(没有这样物资按 1)
    public static float BasePrice(string good) => Goods.TryGetValue(good ?? "", out CommodityConfig config) ? config.price : 1f;

    private static readonly Dictionary<long, Market> Markets = new();
    private static readonly Dictionary<long, long> MarketOfKingdom = new();
    private static bool _ready;

    // 这个国家归哪个市场：宗主一系往上(帝国核心、封建宗主、殖民宗主)，朝贡国归宗主帝国，签了关税同盟的归同盟大国
    public static Kingdom RootOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        Kingdom root = EmpireCoreControl.Sovereign(kingdom) ?? kingdom;
        if (root.HasTakenAlliance() && root.GetTakenAllianceEmpire()?.CoreKingdom is Kingdom suzerain && !suzerain.isRekt())
            root = EmpireCoreControl.Sovereign(suzerain) ?? suzerain;
        Kingdom union = UnionLeader(root);
        return union != null ? EmpireCoreControl.Sovereign(union) ?? union : root;
    }

    private static Kingdom UnionLeader(Kingdom kingdom)
    {
        long id = kingdom.GetOrCreate().market_union_leader_id;
        if (id < 0) return null;
        Kingdom leader = World.world.kingdoms.get(id);
        return leader != null && leader != kingdom && !leader.isRekt() ? leader : null;
    }

    public static Market MarketOf(Kingdom kingdom)
    {
        if (!_ready || kingdom == null) return null;
        if (MarketOfKingdom.TryGetValue(kingdom.id, out long id) && Markets.TryGetValue(id, out Market market)) return market;
        Kingdom root = RootOf(kingdom);
        return root != null && Markets.TryGetValue(root.id, out market) ? market : null;
    }

    public static IEnumerable<Market> AllMarkets => Markets.Values;

    // 外交界面的市场页用(见 DiplomacyWindow)
    public static IEnumerable<CommodityConfig> AllGoods => Goods.Values;
    public static bool Ready => _ready;

    // 关税同盟的盟主(没有返回 null)
    public static Kingdom UnionOf(Kingdom kingdom) => kingdom?.data == null ? null : UnionLeader(kingdom);

    // 签关税同盟、退出(外交界面的操作；AI 每年自己签退见 Unions)：返回没办成的原因键，办成返回 null
    public static string JoinUnion(Kingdom kingdom, Kingdom leader)
    {
        if (kingdom?.data == null || leader?.data == null || kingdom.isRekt() || leader.isRekt() || kingdom == leader)
            return "diplomacy_reason_invalid";
        if (kingdom.isEnemy(leader)) return "diplomacy_reason_at_war";
        if (RootOf(kingdom) == RootOf(leader)) return "diplomacy_reason_same_market";
        kingdom.GetOrCreate().market_union_leader_id = leader.id;
        TranslateHelper.LogEventOnly(string.Format(LM.Get("market_union_joined"), kingdom.GetKingdomFullName(),
            leader.GetKingdomFullName()), kingdom);
        return null;
    }

    public static string LeaveUnion(Kingdom kingdom)
    {
        Kingdom leader = UnionOf(kingdom);
        if (leader == null) return "diplomacy_reason_no_union";
        kingdom.GetOrCreate().market_union_leader_id = -1L;
        TranslateHelper.LogEventOnly(string.Format(LM.Get("market_union_left"), kingdom.GetKingdomFullName(),
            leader.GetKingdomFullName()), kingdom);
        return null;
    }

    // 一国各城奢侈品满足度的平均(0~1)
    public static float LuxurySatisfaction(Kingdom kingdom)
    {
        if (kingdom?.cities == null || kingdom.cities.Count == 0) return 1f;
        float total = 0f;
        foreach (City city in kingdom.cities) total += LuxurySatisfaction(city);
        return total / kingdom.cities.Count;
    }

    // 这个国家所在市场某样物资的缺口(0 不缺 ~ 1 全无)；还没算出来按不缺
    public static float Shortage(Kingdom kingdom, string good) =>
        MarketOf(kingdom) is Market market && market.shortage.TryGetValue(good, out float value) ? value : 0f;

    #endregion

    #region 效果

    // 科研：离不开的物资缺得越多越慢(本文化最大的国家所在的市场)；最慢三成
    private static readonly Dictionary<string, (int year, Kingdom realm)> CultureRealm = new();

    public static float ResearchFactor(string culture, string techId)
    {
        if (!_ready || string.IsNullOrEmpty(culture) || !Config.techs.TryGetValue(techId ?? "", out List<string> needs) || needs.Count == 0)
            return 1f;
        int year = Date.getCurrentYear();
        if (!CultureRealm.TryGetValue(culture, out var cached) || cached.year != year)
        {
            Kingdom biggest = World.world.kingdoms.list
                .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && CultureService.GetRealmCulture(kingdom) == culture)
                .OrderByDescending(kingdom => kingdom.cities.Count).FirstOrDefault();
            CultureRealm[culture] = cached = (year, biggest);
        }
        if (cached.realm == null) return 1f;
        float worst = needs.Max(good => Shortage(cached.realm, good));
        return 1f - 0.7f * worst;
    }

    // 工厂：投入缺得越多减产越多(最多减八成)
    public static float FactoryFactor(City city, string unitType)
    {
        if (!_ready || city?.kingdom == null || !Config.factories.TryGetValue(unitType ?? "", out CommodityRecipe recipe) ||
            recipe.inputs.Count == 0) return 1f;
        float worst = recipe.inputs.Keys.Max(good => Shortage(city.kingdom, good));
        return 1f - 0.8f * worst;
    }

    // 兵种换装：离不开的物资缺过一半就换不了
    public static bool CanEquip(Kingdom kingdom, string arm)
    {
        if (!_ready || kingdom == null || !Config.arms.TryGetValue(arm ?? "", out List<string> needs)) return true;
        return needs.All(good => Shortage(kingdom, good) < 0.5f);
    }

    // 奢侈品满足度(0~1)：所在市场各样奢侈品按需求加权的满足程度；还没算出来按一半
    public static float LuxurySatisfaction(City city)
    {
        if (!_ready || !(MarketOf(city?.kingdom) is Market market)) return 0.5f;
        float need = 0f, met = 0f;
        foreach (CommodityConfig good in Goods.Values)
        {
            if (good.tier != "luxury" || !market.demand.TryGetValue(good.id, out float demand) || demand <= 0f) continue;
            market.shortage.TryGetValue(good.id, out float shortage);
            need += demand;
            met += demand * (1f - shortage);
        }
        return need <= 0f ? 0.5f : met / need;
    }

    // 城的自然稳定：奢侈品满足度一半为平，满足 +4、全缺 -4
    public static float LuxuryStability(City city) => (LuxurySatisfaction(city) - 0.5f) * 8f;

    // 税收：奢侈品买得到，商人、市民手头活络，税多收一点(±5%)
    public static float LuxuryTaxFactor(City city) => 0.95f + 0.1f * LuxurySatisfaction(city);

    // 殖民的去处：本国市场缺、这块地出的物资(0~1)
    public static float ColonyDemand(Kingdom realm, TileZone zone)
    {
        if (!_ready || zone == null || !(MarketOf(realm) is Market market)) return 0f;
        var biomes = new Dictionary<string, int>();
        foreach (WorldTile tile in zone.tiles)
        {
            string biome = BiomeOf(tile);
            if (biome != null) biomes[biome] = biomes.TryGetValue(biome, out int count) ? count + 1 : 1;
        }
        float score = 0f;
        foreach (CommodityConfig good in Goods.Values)
        {
            if (good.biomes.Count == 0 || !market.shortage.TryGetValue(good.id, out float shortage) || shortage < 0.2f) continue;
            float share = good.biomes.Sum(pair => biomes.TryGetValue(pair.Key, out int count) ? count / 64f * Mathf.Min(1f, pair.Value * 10f) : 0f);
            score += shortage * share * (good.tier == "luxury" ? 1.5f : 1f);
        }
        return Mathf.Clamp01(score);
    }

    #endregion

    #region 年度结算

    private sealed class CityBook
    {
        public readonly Dictionary<string, float> made = new(), wanted = new();
    }

    private static object _world;
    private static int _year = int.MinValue;
    private static readonly Queue<City> Pending = new();
    private static readonly Dictionary<long, CityBook> Books = new();
    private const int CitiesPerFrame = 24;

    // 读档、换地图时清掉上一张图的市场(原版读档不换 MapBox，Tick 里按世界对象判断不出换了图：
    // 以前新图的头一轮结算前，市场图层、外交界面拿旧图的市场按国家编号对上新图的国家)
    public static void ResetWorldState()
    {
        _world = null;
        _year = int.MinValue;
        _ready = false;
        Markets.Clear();
        MarketOfKingdom.Clear();
        Pending.Clear();
        Books.Clear();
        BiomeCache.Clear();
        CultureRealm.Clear();
    }

    // 当场把全图结算一轮(剧本开局各城房子盖好、兵编好之后调，见 WorldMapScenario)：
    // 平时按帧分批记账、一年一结；剧本开局要市场图层、外交界面的市场页一进世界就有数
    public static void SettleNow()
    {
        if (World.world?.cities == null) return;
        _world = World.world;
        _year = Date.getCurrentYear();
        Pending.Clear();
        Books.Clear();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.kingdom == null || !city.kingdom.isCiv()) continue;
            try
            {
                Books[city.id] = Book(city);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][市场] {city.GetCityName()} 记账失败: {exception.Message}");
            }
        }
        try
        {
            Settle();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][市场] 开局结算失败: {exception.Message}");
        }
    }

    public static void Tick()
    {
        if (World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _year = int.MinValue;
            _ready = false;
            Markets.Clear();
            MarketOfKingdom.Clear();
            Pending.Clear();
            Books.Clear();
            BiomeCache.Clear();
            CultureRealm.Clear();
        }
        int year = Date.getCurrentYear();
        if (year != _year && Pending.Count == 0)
        {
            _year = year;
            Books.Clear();
            foreach (City city in World.world.cities.list) Pending.Enqueue(city);
            if (Pending.Count == 0) return;
        }
        if (Pending.Count == 0) return;
        for (int i = 0; i < CitiesPerFrame && Pending.Count > 0; i++)
        {
            City city = Pending.Dequeue();
            if (city?.data == null || city.isRekt() || city.kingdom == null || !city.kingdom.isCiv()) continue;
            try
            {
                Books[city.id] = Book(city);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][市场] {city.GetCityName()} 记账失败: {exception.Message}");
            }
        }
        if (Pending.Count > 0) return;
        try
        {
            Settle();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][市场] 年度结算失败: {exception}");
        }
    }

    private static CityBook Book(City city)
    {
        var book = new CityBook();
        CityPopulationData data = CityPopulationSystem.Get(city);
        Dictionary<string, float> ledger = data?.goods_made;
        Dictionary<string, int> biomes = BiomesOf(city);
        float people = CityPopulationSystem.GetTotal(city);
        float hands = Mathf.Clamp(people / 2000f, 0.1f, 1.5f);
        string culture = CultureService.GetMainCulture(city);
        foreach (CommodityConfig good in Goods.Values)
        {
            float made = FromLedger(ledger, good);
            foreach (KeyValuePair<string, float> pair in good.biomes)
                if (biomes.TryGetValue(pair.Key, out int tiles)) made += tiles * pair.Value * hands;
            if (good.craft_per_thousand > 0f && (good.craft_cultures.Count == 0 || good.craft_cultures.Contains(culture)))
                made += people / 1000f * good.craft_per_thousand;
            if (made > 0f) Add(book.made, good.id, made);
        }
        // 基础物资(粮、木、石、金属)的需求按物资账本的消耗加缺口(见 GoodsLedger)，与产出同一本账：
        // 以前按各阶层每千人的定额算(每千人每年吃粮 900)，产出却是账本上的实际收成，两边差两百多倍，
        // 各市场都缺粮九成以上、谁也没有余粮可卖(用户 10-29 "既然什么都缺为什么不去买入卖出呢")。
        // 没有账本(原版小人自己采集吃饭，不记账)的城基础物资不记产需
        bool ledgered = ledger != null;
        if (ledgered)
            foreach (CommodityConfig good in Goods.Values)
                if (Basic(good)) Add(book.wanted, good.id, FromLedger(data.goods_used, good) + FromLedger(data.goods_short, good));
        if (data?.groups != null)
            foreach (PopGroup group in data.groups)
            {
                if (group == null || group.size <= 0f) continue;
                string cls = group.social_class.ToString();
                foreach (CommodityConfig good in Goods.Values)
                    if (!Basic(good) && good.needs.TryGetValue(cls, out float need)) Add(book.wanted, good.id, group.size / 1000f * need);
            }
        // 穿越政权的工业(见 TransmigrantSystem.IndustryFactor)：厂区产量翻倍，归化民按人头另出布匹、工具、军械、钢材、糖
        float industry = TransmigrantSystem.IndustryFactor(city);
        foreach (FactorySite site in FactorySystem.InCity(city))
            foreach (FactoryUnit unit in site.units)
            {
                if (!Config.factories.TryGetValue(unit.type ?? "", out CommodityRecipe recipe)) continue;
                float level = Mathf.Max(1, unit.level);
                foreach (KeyValuePair<string, float> input in recipe.inputs) Add(book.wanted, input.Key, input.Value * level);
                foreach (KeyValuePair<string, float> output in recipe.outputs) Add(book.made, output.Key, output.Value * level * industry);
            }
        if (industry > 1f)
            foreach ((string good, float amount) in TransmigrantSystem.IndustryOutput(city))
                if (Goods.ContainsKey(good)) Add(book.made, good, amount);
        return book;
    }

    // 基础物资：粮、木、石、金属(商品表里 tier 为 basic、按账本资源折算的)
    private static bool Basic(CommodityConfig good) => good.tier == "basic" && good.resources.Count > 0;

    // 账本(产出、消耗或缺口)按这样物资的资源折算(各种粮食都算粮)
    private static float FromLedger(Dictionary<string, float> ledger, CommodityConfig good)
    {
        if (ledger == null || good.resources.Count == 0) return 0f;
        float total = 0f;
        foreach (KeyValuePair<string, float> pair in ledger)
        {
            if (good.resources.TryGetValue(pair.Key, out float rate)) total += pair.Value * rate;
            else if (good.resources.TryGetValue("food", out float foodRate) && IsFood(pair.Key)) total += pair.Value * foodRate;
        }
        return total;
    }

    private static void Add(Dictionary<string, float> map, string key, float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        map[key] = map.TryGetValue(key, out float value) ? value + amount : amount;
    }

    private static readonly Dictionary<string, bool> FoodIds = new();

    private static bool IsFood(string resource)
    {
        if (FoodIds.TryGetValue(resource, out bool food)) return food;
        food = AssetManager.resources.get(resource)?.type == ResType.Food;
        FoodIds[resource] = food;
        return food;
    }

    // 城辖区的群系直方图(每格看底下的地，路、田、城墙不算)，一年算一次
    private static readonly Dictionary<long, (int year, Dictionary<string, int> map)> BiomeCache = new();

    private static Dictionary<string, int> BiomesOf(City city)
    {
        int year = Date.getCurrentYear();
        if (BiomeCache.TryGetValue(city.id, out var cached) && cached.year == year) return cached.map;
        var map = new Dictionary<string, int>();
        foreach (TileZone zone in city.zones)
            foreach (WorldTile tile in zone.tiles)
            {
                string biome = BiomeOf(tile);
                if (biome != null) map[biome] = map.TryGetValue(biome, out int count) ? count + 1 : 1;
            }
        BiomeCache[city.id] = (year, map);
        return map;
    }

    private static string BiomeOf(WorldTile tile)
    {
        TileTypeBase type = tile?.Type;
        if (type == null) return null;
        TileTypeBase ground = (type.road || type.farm_field || type.wall) && tile.main_type != null ? tile.main_type : type;
        return string.IsNullOrEmpty(ground.biome_id) ? null : ground.biome_id;
    }

    // 结算各步出错各记一次(连调用栈)，不让一步出错连累后面：以前贸易一步抛空引用，缺口、价格、进出口整年都没算，
    // 市场页缺口全 0%、价格是底价、进出口全空(用户 10-29 截图"进口缺口呢"，日志里年年"年度结算失败")
    private static readonly HashSet<string> WarnedSteps = new();

    private static void Warn(string step, Exception exception)
    {
        if (WarnedSteps.Add(step)) LogService.LogWarning($"[EmpireCraft][市场] {step}出错(本局只记一次): {exception}");
    }

    private static void Settle()
    {
        Markets.Clear();
        MarketOfKingdom.Clear();
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv()) continue;
            try
            {
                Enter(kingdom);
            }
            catch (Exception exception)
            {
                Warn("记国家账", exception);
            }
        }
        foreach (Market market in Markets.Values.OrderByDescending(market => market.power).Take(Mathf.Max(1, Config.great_powers)))
            market.great = true;
        try
        {
            Trade();
        }
        catch (Exception exception)
        {
            Warn("市场间贸易", exception);
        }
        foreach (Market market in Markets.Values)
            foreach (CommodityConfig good in Goods.Values)
            {
                market.supply.TryGetValue(good.id, out float supply);
                market.demand.TryGetValue(good.id, out float demand);
                market.imports.TryGetValue(good.id, out float bought);
                market.exports.TryGetValue(good.id, out float sold);
                float available = Mathf.Max(0f, supply + bought - sold);
                market.shortage[good.id] = demand <= 0f ? 0f : Mathf.Clamp01(1f - available / demand);
                market.price[good.id] = good.price * Mathf.Clamp(demand / Mathf.Max(0.01f, available), 0.25f, 4f);
            }
        _ready = true;
        CultureRealm.Clear();
        try
        {
            Unions();
        }
        catch (Exception exception)
        {
            Warn("关税同盟", exception);
        }
        try
        {
            Log();
        }
        catch (Exception exception)
        {
            Warn("结算日志", exception);
        }
    }

    // 一国记进它所在的市场：产量、需求、军需
    private static void Enter(Kingdom kingdom)
    {
        Kingdom root = RootOf(kingdom) ?? kingdom;
        if (!Markets.TryGetValue(root.id, out Market market)) Markets[root.id] = market = new Market { leader = root };
        market.members.Add(kingdom);
        MarketOfKingdom[kingdom.id] = root.id;
        market.power += kingdom.GetNationalPower();
        float madeValue = 0f, wantedValue = 0f;
        if (kingdom.cities != null)
        {
            market.cities += kingdom.cities.Count;
            foreach (City city in kingdom.cities)
            {
                if (city == null || !Books.TryGetValue(city.id, out CityBook book)) continue;
                foreach (KeyValuePair<string, float> pair in book.made)
                {
                    Add(market.supply, pair.Key, pair.Value);
                    madeValue += pair.Value * BasePrice(pair.Key);
                }
                foreach (KeyValuePair<string, float> pair in book.wanted)
                {
                    Add(market.demand, pair.Key, pair.Value);
                    wantedValue += pair.Value * BasePrice(pair.Key);
                }
            }
        }
        market.memberValue[kingdom.id] = (madeValue, wantedValue);
        float soldiers = (CorpsSystem.Of(kingdom)?.Sum(corps => Mathf.Max(0f, corps?.strength ?? 0f)) ?? 0f) * (kingdom.hasEnemies() ? 2f : 1f);
        foreach (CommodityConfig good in Goods.Values)
            if (good.per_soldier > 0f) Add(market.demand, good.id, soldiers * good.per_soldier);
    }

    // 市场之间：陆上接壤或商路相连的，缺的向有余的买(按价差、余量)，付价款加关税，记公家贸易
    // 技术差(用户 10-29 "临高政体应当靠着技术差赚着比这个世界任何国家的钱都要多")：卖方科技比买方高几个时代，
    // 每高一代价钱加 EdgePremium(至多 EdgePremiumMax 倍)，本地有货也能挤进去卖掉需求的 EdgeDisplace(至多 EdgeDisplaceMax)——
    // 机器货又好又便宜；领先的卖方先卖。穿越政权另有船队海路(见 TransmigrantSystem.SeaTradePartners)
    private const float EdgePremium = 0.5f, EdgePremiumMax = 3f, EdgeDisplace = 0.15f, EdgeDisplaceMax = 0.6f;
    private static readonly Dictionary<long, int> EraTiers = new();

    private static int EraTier(Kingdom kingdom)
    {
        if (kingdom?.data == null) return 0;
        if (EraTiers.TryGetValue(kingdom.id, out int tier)) return tier;
        string culture = CultureService.GetRealmCulture(kingdom);
        tier = string.IsNullOrEmpty(culture) ? 0 : TechnologySystem.GetEraTier(culture);
        EraTiers[kingdom.id] = tier;
        return tier;
    }

    // 基础物资(粮、木、石、金属)跨市场买卖另加的运费(占底价)
    private const float BasicFreight = 0.5f;

    private static int TechEdge(Market seller, Market buyer) => Math.Max(0, EraTier(seller.leader) - EraTier(buyer.leader));

    private static void Trade()
    {
        EraTiers.Clear();
        var links = new Dictionary<long, HashSet<long>>();
        void Link(long a, long b)
        {
            if (a == b) return;
            (links.TryGetValue(a, out var set) ? set : links[a] = new HashSet<long>()).Add(b);
            (links.TryGetValue(b, out set) ? set : links[b] = new HashSet<long>()).Add(a);
        }
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.kingdom?.data == null || !MarketOfKingdom.TryGetValue(city.kingdom.id, out long own)) continue;
            try
            {
                if (city.neighbours_kingdoms != null)
                    foreach (Kingdom neighbour in city.neighbours_kingdoms)
                        if (neighbour?.data != null && !neighbour.isRekt() && MarketOfKingdom.TryGetValue(neighbour.id, out long other) &&
                            !neighbour.isEnemy(city.kingdom))
                            Link(own, other);
                long partnerId = TradeRouteSystem.PartnerOf(city);
                City partner = partnerId >= 0 ? World.world.cities.get(partnerId) : null;
                if (partner?.kingdom?.data != null && MarketOfKingdom.TryGetValue(partner.kingdom.id, out long overseas)) Link(own, overseas);
            }
            catch (Exception exception)
            {
                Warn("商路连线", exception);
            }
        }
        Kingdom lingao = TransmigrantSystem.Active();
        if (lingao != null && MarketOfKingdom.TryGetValue(lingao.id, out long home))
            foreach (Kingdom partner in TransmigrantSystem.SeaTradePartners(lingao))
                if (MarketOfKingdom.TryGetValue(partner.id, out long other) && !partner.isEnemy(lingao))
                    Link(home, other);
        foreach (Market market in Markets.Values)
            if (market.leader != null && links.TryGetValue(market.leader.id, out HashSet<long> set))
                market.linked.UnionWith(set);
        foreach (CommodityConfig good in Goods.Values)
        {
            // 基础物资也买卖(以前不许，缺粮的市场买不到邻国的余粮)，又重又贱，另加运费
            float freight = good.tier == "basic" ? BasicFreight : 0f;
            foreach (Market buyer in Markets.Values)
            {
                buyer.supply.TryGetValue(good.id, out float supply);
                buyer.demand.TryGetValue(good.id, out float demand);
                float need = Mathf.Max(0f, demand - supply);
                if (demand <= 0f || buyer.leader?.data == null || buyer.leader.isRekt() || !links.TryGetValue(buyer.leader.id, out var partners))
                    continue;
                float displaced = 0f;
                foreach (long id in partners.Where(Markets.ContainsKey)
                             .OrderByDescending(id => TechEdge(Markets[id], buyer)).ThenByDescending(id => Surplus(Markets[id], good.id)))
                {
                    Market seller = Markets[id];
                    if (seller.leader?.data == null || seller.leader.isRekt()) continue;
                    int edge = TechEdge(seller, buyer);
                    float room = need + Mathf.Max(0f, demand * Mathf.Min(EdgeDisplaceMax, edge * EdgeDisplace) - displaced);
                    float spare = Surplus(seller, good.id);
                    if (spare <= 0f || room <= 0f) continue;
                    float markup = Mathf.Min(EdgePremiumMax, 1f + edge * EdgePremium);
                    float unit = good.price * markup * (1f + Config.tariff + freight);
                    float affordable = Mathf.Max(0f, buyer.leader.GetMoney()) / Mathf.Max(0.01f, unit);
                    float amount = Mathf.Min(room, Mathf.Min(spare, affordable));
                    if (amount < 0.5f) continue;
                    int paid = Mathf.RoundToInt(amount * unit);
                    int earned = Mathf.RoundToInt(amount * good.price * markup);
                    buyer.leader.SubMoney(paid, TreasuryCategory.PublicTrade);
                    seller.leader.AddMoney(earned, TreasuryCategory.PublicTrade);
                    Add(buyer.imports, good.id, amount);
                    Add(seller.exports, good.id, amount);
                    buyer.trade_value += paid;
                    seller.trade_value += earned;
                    buyer.boughtFrom[seller.leader.id] = (buyer.boughtFrom.TryGetValue(seller.leader.id, out float spent) ? spent : 0f) + paid;
                    seller.soldTo[buyer.leader.id] = (seller.soldTo.TryGetValue(buyer.leader.id, out float got) ? got : 0f) + earned;
                    float filled = Mathf.Min(amount, need);
                    need -= filled;
                    displaced += amount - filled;
                }
            }
        }
    }

    private static float Surplus(Market market, string good)
    {
        market.supply.TryGetValue(good, out float supply);
        market.demand.TryGetValue(good, out float demand);
        market.exports.TryGetValue(good, out float sold);
        return supply - demand - sold;
    }

    // 关税同盟：接壤大国的市场比自家缺得少、对它好感好、不交战的小国，每年有机会并入；交战、反目的退出
    private const float JoinChance = 0.2f, LeaveChance = 0.3f;

    private static void Unions()
    {
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv()) continue;
            var data = kingdom.GetOrCreate();
            Kingdom leader = UnionLeader(kingdom);
            if (leader != null)
            {
                bool stays = MarketOf(leader)?.great == true && !kingdom.isEnemy(leader) && kingdom.isOpinionTowardsKingdomGood(leader);
                if (!stays && UnityEngine.Random.value < LeaveChance)
                {
                    data.market_union_leader_id = -1L;
                    TranslateHelper.LogEventOnly(string.Format(LM.Get("market_union_left"), kingdom.GetKingdomFullName(),
                        leader.GetKingdomFullName()), kingdom);
                }
                continue;
            }
            Market own = MarketOf(kingdom);
            if (own == null || own.great || own.leader != kingdom || UnityEngine.Random.value >= JoinChance) continue;
            Market best = null;
            float gain = 0.05f;
            foreach (Kingdom neighbour in kingdom.cities.SelectMany(city => city.neighbours_kingdoms).Distinct())
            {
                Market other = MarketOf(neighbour);
                if (other == null || other == own || !other.great || other.power < own.power * 3d) continue;
                if (kingdom.isEnemy(other.leader) || !kingdom.isOpinionTowardsKingdomGood(other.leader)) continue;
                float better = AverageShortage(own) - AverageShortage(other);
                if (better > gain)
                {
                    gain = better;
                    best = other;
                }
            }
            if (best == null) continue;
            data.market_union_leader_id = best.leader.id;
            TranslateHelper.LogEventOnly(string.Format(LM.Get("market_union_joined"), kingdom.GetKingdomFullName(),
                best.leader.GetKingdomFullName()), kingdom);
        }
    }

    private static float AverageShortage(Market market)
    {
        float total = 0f;
        int count = 0;
        foreach (KeyValuePair<string, float> pair in market.shortage)
        {
            if (!Goods.TryGetValue(pair.Key, out CommodityConfig good) || good.tier == "basic") continue;
            if (!market.demand.TryGetValue(pair.Key, out float demand) || demand <= 0f) continue;
            total += pair.Value;
            count++;
        }
        return count == 0 ? 0f : total / count;
    }

    private static void Log()
    {
        foreach (Market market in Markets.Values.OrderByDescending(market => market.power).Take(5))
        {
            string shortages = string.Join("、", market.shortage.Where(pair => pair.Value >= 0.3f && Goods[pair.Key].tier != "basic")
                .OrderByDescending(pair => pair.Value).Take(5).Select(pair => $"{pair.Key}{Mathf.RoundToInt(pair.Value * 100f)}%"));
            LogService.LogInfo($"[EmpireCraft][市场] {market.leader.GetKingdomFullName()}{(market.great ? "(大国)" : "")} " +
                               $"成员 {market.members.Count} 国，外贸 {Mathf.RoundToInt(market.trade_value)}，缺：{(shortages.Length == 0 ? "无" : shortages)}");
        }
    }

    #endregion
}
