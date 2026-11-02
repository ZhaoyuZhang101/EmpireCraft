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
    // 从第几个科技时代起才有手工、地形产出与百姓、军队的需求(矿场、工厂实际出的货不看时代)
    public int era;
    // 从 reserve_era 时代起每千人每年要备的量：科技、换装离不开的原料，没这份需求就谈不上缺、也不会去进口
    public float reserve;
    public int reserve_era;
    // 这种地形容易勘出这样矿藏(殖民选址用，见 ColonyDemand)
    public Dictionary<string, float> prospect = new();
}

// 通商政策(用户 2026-11-02 问答选"贸易与通商政策")：市场领衔国定，全市场对外一个关税。数值追加在末尾，存档里记的是序号
public enum TradePolicy
{
    Mercantile,  // 重商：照常收关税
    Free,        // 自由贸易：关税低，缺口少，本地货价压得低
    Protective,  // 保护关税：关税高，外国机器货挤进来的少一半，本地货价高，工场好赚
    Closed       // 闭关：不与外国市场、港口通商
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
//   · 市场之间：陆上接壤或商路相连、不禁运不闭关的市场互通有无，到岸价低于本地价才买，进口方付价款(另加关税)给出口方(见 Trade)；
//   · 通商政策：领衔国定重商、自由贸易、保护关税或闭关(见 PolicyOf)，定关税与外国货能挤进来多少；
//   · 价格：一年只向供需定的价格走一段，手艺跟价增减，AI 建厂开矿看价格与缺口(见 FactoryAppeal、WantsMine)；
//   · 缺口：科技研究按离不开的物资的缺口变慢(见 TechnologySystem.AddResearch)，工厂减产(见 FactorySystem.Produce)，
//     兵种换装受阻(见 CorpsArmSystem.UpgradeOf)，奢侈品满足与否加减城的自然稳定、税收(见 CityStabilitySystem、PopulationEconomySystem)；
//   · 殖民的动力：本国市场缺、别的岛上的地出的物资，殖民船优先去那里(见 ColonySystem.FindSite)。
// 配方数量对着存档的物资账配(用户 2026-11-02 "市场系统接着优化"：三百城一年木材才两百来份，旧配方工厂一年要两千多，古代工厂全在减产八成)；
// 物资按科技时代出现(CommodityConfig.era)，石器时代不再缺钢材、军械；手工布匹、工具按阶层用度配到正常年景够用。
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
        // 各成员国一年的外贸(国家编号 → 出口收入、进口花费、关税收入)，财政页、国情窗口的市场卡用(见 MarketCard)
        public readonly Dictionary<long, float> memberExports = new(), memberImports = new(), memberCustoms = new();
        // 这一年的通商政策(见 PolicyOf)
        public TradePolicy policy;
        // 物资详情页用(见 MarketWindow)：需求里工厂、军队、储备各占多少(其余是民用)；各成员国各物资的产量；
        // 各物资从哪个市场买、卖给哪个市场多少(领衔国编号 → 数量)；民间存款花完还买不够的物资
        public readonly Dictionary<string, float> demandFactory = new(), demandArmy = new(), demandReserve = new();
        public readonly Dictionary<long, Dictionary<string, float>> memberMade = new();
        public readonly Dictionary<string, Dictionary<long, float>> importsFrom = new(), exportsTo = new();
        public readonly HashSet<string> outOfMoney = new();
        // 破交(见 LaneCut、Trade)：海路被截断的殖民地；港口有几成没被封锁；因此少进口的物资
        public readonly HashSet<long> raidedMembers = new();
        public float seaOpen = 1f;
        public readonly HashSet<string> seaLimited = new();
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

    // ───────── 通商政策 ─────────
    // 这个国家眼下实行的政策：被和约逼开了港的不能闭关、不能保护关税；制度定了锁国的只能闭关
    public static TradePolicy PolicyOf(Kingdom leader)
    {
        if (leader?.data == null) return TradePolicy.Mercantile;
        var stored = (TradePolicy)leader.GetOrCreate().market_policy;
        if (TradeRouteSystem.TreatyPartner(leader) != null)
            return stored is TradePolicy.Closed or TradePolicy.Protective ? TradePolicy.Mercantile : stored;
        return TradeRouteSystem.ClosedByLaw(leader) ? TradePolicy.Closed : stored;
    }

    // 改政策(外交界面、市场窗口的按钮；AI 每二十年自己定一次，见 Policies)：办不成返回原因键
    public static string SetPolicy(Kingdom leader, TradePolicy policy)
    {
        if (leader?.data == null || leader.isRekt()) return "diplomacy_reason_invalid";
        if (TradeRouteSystem.ClosedByLaw(leader) && TradeRouteSystem.TreatyPartner(leader) == null) return "market_policy_law";
        if (TradeRouteSystem.TreatyPartner(leader) != null && policy is TradePolicy.Closed or TradePolicy.Protective)
            return "market_policy_treaty_block";
        var data = leader.GetOrCreate();
        data.market_policy_at = World.world.getCurWorldTime();
        if (data.market_policy == (int)policy) return null;
        data.market_policy = (int)policy;
        LogPolicy(leader, policy);
        return null;
    }

    private static void LogPolicy(Kingdom leader, TradePolicy policy) =>
        TranslateHelper.LogEventOnly(string.Format(LM.Get("market_policy_changed"), leader.GetKingdomFullName(), PolicyName(policy)), leader);

    public static string PolicyName(TradePolicy policy) => LM.Get("market_policy_" + policy.ToString().ToLowerInvariant());

    // 关税率：重商照商品表，自由贸易四分之一，保护关税两倍；闭关没有外贸，按重商算
    public static float PolicyTariff(TradePolicy policy) => policy switch
    {
        TradePolicy.Free => Config.tariff * 0.25f,
        TradePolicy.Protective => Config.tariff * 2f,
        _ => Config.tariff
    };

    // 外国机器货能挤进来的份额的倍数(见 Trade 的技术差)
    private static float DisplaceFactor(TradePolicy policy) => policy switch
    {
        TradePolicy.Free => 1.25f,
        TradePolicy.Protective => 0.5f,
        _ => 1f
    };

    // 买方对这个卖方收多少关税：和约里的胜者只交自由贸易的关税(协定关税)
    public static float TariffBetween(Market buyer, Market seller)
    {
        float rate = PolicyTariff(buyer.policy);
        return TreatyBetween(buyer, seller) ? Mathf.Min(rate, PolicyTariff(TradePolicy.Free)) : rate;
    }

    private static bool TreatyBetween(Market a, Market b) =>
        TradeRouteSystem.TreatyPartner(a.leader) is Kingdom first && MarketOfKingdom.TryGetValue(first.id, out long own) && own == b.leader?.id ||
        TradeRouteSystem.TreatyPartner(b.leader) is Kingdom second && MarketOfKingdom.TryGetValue(second.id, out long other) && other == a.leader?.id;

    // 两个市场之间为什么不通商(界面用)：通的返回 null，否则返回原因键
    public static string TradeBlock(Market a, Market b)
    {
        if (a?.leader?.data == null || b?.leader?.data == null) return "market_status_far";
        if (MarketSystem.Embargoed(a.leader, b.leader)) return "market_status_war";
        if ((a.policy == TradePolicy.Closed || b.policy == TradePolicy.Closed) && !TreatyBetween(a, b)) return "market_status_closed";
        return a.linked.Contains(b.leader.id) ? null : "market_status_far";
    }

    // 价格走势(最近几十年，读档后从头记，见 Settle)
    public static IList<float> PriceHistory(Market market, string good)
    {
        if (market?.leader != null && PriceBook.TryGetValue(market.leader.id, out var book) && book.TryGetValue(good ?? "", out List<float> list))
            return list;
        return new List<float>();
    }

    // 一座城这一年出多少这样物资(产地图层用，见 MarketMap)
    public static float CityMade(City city, string good) =>
        city != null && good != null && _settledBooks.TryGetValue(city.id, out CityBook book) && book.made.TryGetValue(good, out float made) ? made : 0f;

    // 各城结算一次加一：产地图层按它重算分档
    public static int Revision { get; private set; }

    public static bool TryGetGood(string id, out CommodityConfig good) => Goods.TryGetValue(id ?? "", out good);

    // 物资详情页列来源与用途用(见 MarketWindow)
    public static CommodityFileConfig Table => Config;

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

    // ───────── 市价与国家经济(用户 10-29 "现在的市场系统无法真正影响国家的经济，甚至帝国的经济界面和这些市场完全无关") ─────────
    // 市价指数：本国所在市场某物资的价格 ÷ 底价(还没结算按 1)：缺货涨、过剩跌，城里的产出按它计产值、交税(见 PopulationEconomySystem.UnitValue、FactorySystem.Produce)
    public static float PriceIndex(Kingdom kingdom, string good)
    {
        if (string.IsNullOrEmpty(good) || MarketOf(kingdom) is not Market market || !market.price.TryGetValue(good, out float price))
            return 1f;
        float basePrice = BasePrice(good);
        return basePrice <= 0f ? 1f : Mathf.Clamp(price / basePrice, 0.25f, 4f);
    }

    // 原版资源 → 商品(粮木石金属由原来的实物市场定价，不在这里；同一资源出好几样的取第一样)
    private static Dictionary<string, string> _resourceGoods;

    private static string GoodOfResource(string resource)
    {
        if (_resourceGoods == null)
        {
            _resourceGoods = new Dictionary<string, string>();
            foreach (CommodityConfig good in Goods.Values)
            {
                if (Basic(good)) continue;
                foreach (string key in good.resources.Keys)
                    if (!_resourceGoods.ContainsKey(key)) _resourceGoods[key] = good.id;
            }
        }
        return _resourceGoods.TryGetValue(resource ?? "", out string id) ? id : null;
    }

    // 矿产总有人收(铸钱、打器具、囤货)，过剩最多跌到六成(以前古代没人用的铜、煤、硝石一律按两成五计产值)
    private const float ResourcePriceFloor = 0.6f;

    public static float ResourcePriceIndex(City city, string resource) =>
        GoodOfResource(resource) is string good ? Mathf.Max(ResourcePriceFloor, PriceIndex(city?.kingdom, good)) : 1f;

    // AI 建厂挑门类的分量(用户 2026-11-02 问答选"AI 按缺口建厂"，见 FactorySystem.Grow)：产品缺得多、卖得贵、原料便宜够用的分量大；
    // 原料缺得多打折；没有产品的门类(造纸、印书、船坞，另有用处)分量 1
    public static float FactoryAppeal(City city, string unitType)
    {
        if (!_ready || city?.kingdom == null || !Config.factories.TryGetValue(unitType ?? "", out CommodityRecipe recipe)) return 1f;
        float inputGap = recipe.inputs.Count == 0 ? 0f : recipe.inputs.Keys.Max(good => Shortage(city.kingdom, good));
        if (recipe.outputs.Count == 0) return Mathf.Max(0.05f, 1f - 0.8f * inputGap);
        float revenue = 0f, weight = 0f, gap = 0f;
        foreach (KeyValuePair<string, float> output in recipe.outputs)
        {
            float value = output.Value * BasePrice(output.Key);
            revenue += value * PriceIndex(city.kingdom, output.Key);
            gap += value * Shortage(city.kingdom, output.Key);
            weight += value;
        }
        if (weight <= 0f) return 1f;
        float cost = recipe.inputs.Sum(input => input.Value * BasePrice(input.Key) * PriceIndex(city.kingdom, input.Key));
        // 产品价格指数(过剩到底是 0.25)× 缺口加成 × 原料折扣；卖的不够本的再减半
        float appeal = revenue / weight * (1f + 4f * gap / weight) * (1f - 0.8f * inputGap);
        if (revenue <= cost) appeal *= 0.5f;
        return Mathf.Max(0.05f, appeal);
    }

    // 本城开得了的矿藏里有本市场缺的(新村镇优先做矿村，见 LivelihoodSystem.Classify)
    private const float MineShortage = 0.3f;

    public static bool WantsMine(City city)
    {
        if (!_ready || city?.kingdom == null) return false;
        int level = MineralResourceSystem.MiningLevel(city);
        foreach ((string id, int tier, _, float chance) in MineralResourceSystem.Minerals)
        {
            if (tier > level || !(GoodOfResource(id) is string good) || Shortage(city.kingdom, good) < MineShortage) continue;
            if (AssetManager.resources.get(id) != null && MineralResourceSystem.HasDeposit(city, id, chance)) return true;
        }
        return false;
    }

    // 厂区一年产值按产品的市价指数折算(几样产品按产量加权)
    public static float FactoryPriceIndex(City city, string unitType)
    {
        if (!Config.factories.TryGetValue(unitType ?? "", out CommodityRecipe recipe) || recipe.outputs.Count == 0) return 1f;
        float weighted = 0f, total = 0f;
        foreach (KeyValuePair<string, float> output in recipe.outputs)
        {
            float weight = output.Value * BasePrice(output.Key);
            weighted += weight * PriceIndex(city?.kingdom, output.Key);
            total += weight;
        }
        return total > 0f ? weighted / total : 1f;
    }

    // 物价水平：各物资市价指数按需求额加权(1 = 照底价)
    public static float PriceLevel(Kingdom kingdom)
    {
        if (MarketOf(kingdom) is not Market market) return 1f;
        float weighted = 0f, total = 0f;
        foreach (CommodityConfig good in Goods.Values)
        {
            if (!market.demand.TryGetValue(good.id, out float demand) || demand <= 0f) continue;
            float weight = demand * good.price;
            weighted += weight * PriceIndex(kingdom, good.id);
            total += weight;
        }
        return total > 0f ? weighted / total : 1f;
    }

    // 缺工具：各城产出打折，全缺 -ToolsPenalty(见 PopulationEconomySystem.PayTaxes)
    private const float ToolsPenalty = 0.25f;
    public static float OutputFactor(City city) => 1f - ToolsPenalty * Shortage(city?.kingdom, "tools");

    // 缺布匹、缺粮：民生用品买不到，稳定下降，全缺 -NecessityPenalty(见 CityStabilitySystem)
    private const float NecessityPenalty = 4f;
    public static float NecessityStability(City city) =>
        -NecessityPenalty * Mathf.Max(Shortage(city?.kingdom, "cloth"), Shortage(city?.kingdom, "grain"));

    // 一国(或一组国家)一年的出口收入、进口花费、关税收入
    public static (float exports, float imports, float customs) TradeOf(IEnumerable<Kingdom> realm)
    {
        float exports = 0f, imports = 0f, customs = 0f;
        foreach (Kingdom kingdom in realm ?? Enumerable.Empty<Kingdom>())
        {
            if (kingdom?.data == null || MarketOf(kingdom) is not Market market) continue;
            if (market.memberExports.TryGetValue(kingdom.id, out float sold)) exports += sold;
            if (market.memberImports.TryGetValue(kingdom.id, out float bought)) imports += bought;
            if (market.memberCustoms.TryGetValue(kingdom.id, out float duty)) customs += duty;
        }
        return (exports, imports, customs);
    }

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
            if (good.biomes.Count == 0 && good.prospect.Count == 0 || !market.shortage.TryGetValue(good.id, out float shortage) || shortage < 0.2f) continue;
            float share = good.biomes.Sum(pair => biomes.TryGetValue(pair.Key, out int count) ? count / 64f * Mathf.Min(1f, pair.Value * 10f) : 0f) +
                          good.prospect.Sum(pair => biomes.TryGetValue(pair.Key, out int count) ? count / 64f * pair.Value : 0f);
            score += shortage * share * (good.tier == "luxury" ? 1.5f : 1f);
        }
        return Mathf.Clamp01(score);
    }

    #endregion

    #region 年度结算

    private sealed class CityBook
    {
        public readonly Dictionary<string, float> made = new(), wanted = new();
        // wanted 里工厂投入、储备各占多少
        public readonly Dictionary<string, float> factory = new(), reserve = new();
    }

    private static object _world;
    private static int _year = int.MinValue;
    private static readonly Queue<City> Pending = new();
    // 本年在记的账，与上一轮结算用的账(产地图层读，记账期间不会空掉)
    private static Dictionary<long, CityBook> Books = new();
    private static Dictionary<long, CityBook> _settledBooks = new();
    private const int CitiesPerFrame = 24;

    // 各文化的科技时代(一年算一次：物资按时代出现，见 Book)
    private static readonly Dictionary<string, int> CultureEras = new();

    private static int CultureEra(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return 0;
        if (CultureEras.TryGetValue(culture, out int era)) return era;
        return CultureEras[culture] = TechnologySystem.GetEraTier(culture);
    }

    // 价格账(领衔国编号 → 物资 → 最近几十年的价格)：市场每年重建，价格要记着上年的，涨跌才有惯性、有走势；不随存档
    private static readonly Dictionary<long, Dictionary<string, List<float>>> PriceBook = new();
    private const int PriceYears = 30;
    // 一年只向供需定的价格走这么多，免得年年大起大落
    private const float PriceInertia = 0.35f;

    // 读档、换地图时清掉上一张图的市场(原版读档不换 MapBox，Tick 里按世界对象判断不出换了图：
    // 以前新图的头一轮结算前，市场图层、外交界面拿旧图的市场按国家编号对上新图的国家)
    public static void ResetWorldState()
    {
        _world = null;
        _year = int.MinValue;
        ClearAll();
    }

    private static void ClearAll()
    {
        _ready = false;
        Markets.Clear();
        MarketOfKingdom.Clear();
        Pending.Clear();
        Books = new Dictionary<long, CityBook>();
        _settledBooks = new Dictionary<long, CityBook>();
        BiomeCache.Clear();
        CultureRealm.Clear();
        CultureEras.Clear();
        PriceBook.Clear();
    }

    // 当场把全图结算一轮(剧本开局各城房子盖好、兵编好之后调，见 WorldMapScenario)：
    // 平时按帧分批记账、一年一结；剧本开局要市场图层、外交界面的市场页一进世界就有数
    public static void SettleNow()
    {
        if (World.world?.cities == null) return;
        _world = World.world;
        _year = Date.getCurrentYear();
        Pending.Clear();
        Books = new Dictionary<long, CityBook>();
        CultureEras.Clear();
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
            ClearAll();
        }
        int year = Date.getCurrentYear();
        if (year != _year && Pending.Count == 0)
        {
            _year = year;
            Books = new Dictionary<long, CityBook>();
            CultureEras.Clear();
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
        // 物资按时代出现(用户 11-02 问答选"修基线失真"：以前石器时代也要钢材、军械，各市场常年缺一百)；矿场、工厂实际出的货不看时代
        int era = CultureEra(TechnologySystem.GetCultureOf(city));
        foreach (CommodityConfig good in Goods.Values)
        {
            float made = FromLedger(ledger, good);
            if (era >= good.era)
            {
                foreach (KeyValuePair<string, float> pair in good.biomes)
                    if (biomes.TryGetValue(pair.Key, out int tiles)) made += tiles * pair.Value * hands;
                if (good.craft_per_thousand > 0f && (good.craft_cultures.Count == 0 || good.craft_cultures.Contains(culture)))
                    made += people / 1000f * good.craft_per_thousand * CraftResponse(city.kingdom, good.id);
            }
            if (made > 0f) Add(book.made, good.id, made);
            // 储备：科技、换装离不开的原料要备一点，没这份需求就谈不上缺，也不会去进口
            if (good.reserve > 0f && era >= good.reserve_era)
            {
                float reserve = people / 1000f * good.reserve;
                Add(book.reserve, good.id, reserve);
                Add(book.wanted, good.id, reserve);
            }
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
                    if (!Basic(good) && era >= good.era && good.needs.TryGetValue(cls, out float need)) Add(book.wanted, good.id, group.size / 1000f * need);
            }
        // 穿越政权的工业(见 TransmigrantSystem.IndustryFactor)：厂区产量翻倍，归化民按人头另出布匹、工具、军械、钢材、糖
        float industry = TransmigrantSystem.IndustryFactor(city);
        foreach (FactorySite site in FactorySystem.InCity(city))
            foreach (FactoryUnit unit in site.units)
            {
                if (!Config.factories.TryGetValue(unit.type ?? "", out CommodityRecipe recipe)) continue;
                float level = Mathf.Max(1, unit.level);
                foreach (KeyValuePair<string, float> input in recipe.inputs)
                {
                    Add(book.wanted, input.Key, input.Value * level);
                    Add(book.factory, input.Key, input.Value * level);
                }
                foreach (KeyValuePair<string, float> output in recipe.outputs) Add(book.made, output.Key, output.Value * level * industry);
            }
        if (industry > 1f)
            foreach ((string good, float amount) in TransmigrantSystem.IndustryOutput(city))
                if (Goods.ContainsKey(good)) Add(book.made, good, amount);
        return book;
    }

    // 手艺跟价：货贵了多做，货贱了少做(上一年价格指数开方，0.75~1.5 倍)，这样常年缺货的物资手工也能慢慢补上一些
    private static float CraftResponse(Kingdom kingdom, string good) => Mathf.Clamp(Mathf.Sqrt(PriceIndex(kingdom, good)), 0.75f, 1.5f);

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
        EraTiers.Clear();
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
                market.price[good.id] = SmoothedPrice(market, good, demand, available);
            }
        foreach (Market market in Markets.Values)
            foreach (long id in market.raidedMembers)
                if (World.world.kingdoms.get(id) is Kingdom colony && ColonySystem.MetropoleOf(colony) is Kingdom metropole)
                    FrontSystem.Log(metropole, string.Format(LM.Get("market_log_raided"), colony.GetKingdomName()));
        // 没了的市场不再留价格账
        if (PriceBook.Count > Markets.Count)
            foreach (long id in PriceBook.Keys.Where(id => !Markets.ContainsKey(id)).ToList()) PriceBook.Remove(id);
        _settledBooks = Books;
        Revision++;
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
            Policies();
        }
        catch (Exception exception)
        {
            Warn("通商政策", exception);
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
        // 破交：殖民地的海路被敌方水师截断，它的出产只有 RaidedKeep 运得回市场(见 LaneCut)
        float lane = 1f;
        if (LaneCut(kingdom))
        {
            lane = RaidedKeep;
            market.raidedMembers.Add(kingdom.id);
        }
        if (kingdom.cities != null)
        {
            market.cities += kingdom.cities.Count;
            foreach (City city in kingdom.cities)
            {
                if (city == null || !Books.TryGetValue(city.id, out CityBook book)) continue;
                foreach (KeyValuePair<string, float> pair in book.made)
                {
                    Add(market.supply, pair.Key, pair.Value * lane);
                    if (!market.memberMade.TryGetValue(kingdom.id, out Dictionary<string, float> own))
                        market.memberMade[kingdom.id] = own = new Dictionary<string, float>();
                    Add(own, pair.Key, pair.Value * lane);
                    madeValue += pair.Value * lane * BasePrice(pair.Key);
                }
                foreach (KeyValuePair<string, float> pair in book.wanted)
                {
                    Add(market.demand, pair.Key, pair.Value);
                    wantedValue += pair.Value * BasePrice(pair.Key);
                }
                foreach (KeyValuePair<string, float> pair in book.factory) Add(market.demandFactory, pair.Key, pair.Value);
                foreach (KeyValuePair<string, float> pair in book.reserve) Add(market.demandReserve, pair.Key, pair.Value);
            }
        }
        market.memberValue[kingdom.id] = (madeValue, wantedValue);
        float soldiers = (CorpsSystem.Of(kingdom)?.Sum(corps => Mathf.Max(0f, corps?.strength ?? 0f)) ?? 0f) * (kingdom.hasEnemies() ? 2f : 1f);
        int era = EraTier(kingdom);
        foreach (CommodityConfig good in Goods.Values)
        {
            if (good.per_soldier <= 0f || era < good.era) continue;
            Add(market.demand, good.id, soldiers * good.per_soldier);
            Add(market.demandArmy, good.id, soldiers * good.per_soldier);
        }
    }

    // ───────── 海上破交(用户 2026-11-02 "先做第一批"，借鉴钢铁雄心的袭击运输船队) ─────────
    // 敌方水师封住港口、控制海区：宗主与殖民地之间走不通，殖民地的货运不回来；只靠海路相连的市场之间，
    // 港口被封了几成，贸易就少几成，商路的航线被截断就断航。AI 的舰队也优先去封这类港口(见 NavySystem.Assign)
    private const float RaidedKeep = 0.25f;

    private static bool AtWar(Kingdom kingdom) => kingdom?.data != null && kingdom.hasEnemies();

    private static bool LaneCut(Kingdom colony)
    {
        Kingdom metropole = ColonySystem.MetropoleOf(colony);
        if (metropole?.data == null || !CorpsSystem.Enabled || !AtWar(metropole) && !AtWar(colony)) return false;
        List<SeaRegionSystem.Region> home = NavySystem.HomeWaters(metropole);
        List<City> ports = NavySystem.Ports(colony).ToList();
        if (home.Count == 0 || ports.Count == 0) return false;
        if (ports.All(NavySystem.IsBlockaded)) return true;
        List<SeaRegionSystem.Region> waters = ports.SelectMany(SeaRegionSystem.RegionsAround).Distinct().ToList();
        // 两边本来就不在一片海上的不算被截断
        if (waters.Count == 0 || SeaRegionSystem.Route(home, waters) == null) return false;
        return SeaRegionSystem.Route(home, waters, region => NavySystem.IsHostile(metropole, region)) == null;
    }

    // 两个港口之间的航线通不通(见 Trade)
    private static bool SeaLaneOpen(City from, City to)
    {
        if (NavySystem.IsBlockaded(from) || NavySystem.IsBlockaded(to)) return false;
        if (!AtWar(from.kingdom) && !AtWar(to.kingdom)) return true;
        List<SeaRegionSystem.Region> start = SeaRegionSystem.RegionsAround(from), goal = SeaRegionSystem.RegionsAround(to);
        if (start.Count == 0 || goal.Count == 0) return true;
        return SeaRegionSystem.Route(start, goal, region => NavySystem.IsHostile(from.kingdom, region) || NavySystem.IsHostile(to.kingdom, region)) != null;
    }

    // 这个市场的港口有几成没被封锁；没在打仗的不算
    private static float PortsOpen(Market market)
    {
        if (!CorpsSystem.Enabled || !market.members.Any(AtWar)) return 1f;
        int total = 0, blocked = 0;
        foreach (Kingdom member in market.members)
            foreach (City port in NavySystem.Ports(member))
            {
                total++;
                if (NavySystem.IsBlockaded(port)) blocked++;
            }
        return total == 0 ? 1f : 1f - blocked / (float)total;
    }

    // 供需定的价格：缺货涨、过剩跌，封顶四倍、保底四分之一；这个市场没人要的物资没有行情，按底价
    private static float Quote(float basePrice, float demand, float available) =>
        demand <= 0f ? basePrice : basePrice * Mathf.Clamp(demand / Mathf.Max(0.01f, available), 0.25f, 4f);

    private static float Quote(Market market, CommodityConfig good)
    {
        market.supply.TryGetValue(good.id, out float supply);
        market.demand.TryGetValue(good.id, out float demand);
        market.imports.TryGetValue(good.id, out float bought);
        market.exports.TryGetValue(good.id, out float sold);
        return Quote(good.price, demand, Mathf.Max(0f, supply + bought - sold));
    }

    // 记进价格账并返回今年的价格：一年只向供需定的价格走 PriceInertia(头一年直接按供需)
    private static float SmoothedPrice(Market market, CommodityConfig good, float demand, float available)
    {
        float target = Quote(good.price, demand, available);
        if (market.leader == null) return target;
        if (!PriceBook.TryGetValue(market.leader.id, out Dictionary<string, List<float>> book))
            PriceBook[market.leader.id] = book = new Dictionary<string, List<float>>();
        if (!book.TryGetValue(good.id, out List<float> history))
        {
            // 这个市场既不产也不要的物资不记账
            if (demand <= 0f && available <= 0f) return target;
            book[good.id] = history = new List<float>();
        }
        float price = history.Count == 0 ? target : history[history.Count - 1] + (target - history[history.Count - 1]) * PriceInertia;
        history.Add(price);
        if (history.Count > PriceYears) history.RemoveAt(0);
        return price;
    }

    // 市场之间：陆上接壤或商路相连、不禁运不闭关的，缺的向有余的买，付价款加关税，记公家贸易。
    // 成交看价差(用户 2026-11-02 问答选"贸易与通商政策")：到岸价(卖方本地价 × (1 + 关税 + 运费))低于买方本地价才买，
    // 买到两边相平为止，所以关税高的市场宁可留些缺口；成交价取卖方本地价与买方能出的价的中点，两边分利。
    // 最缺的市场先买，便宜的卖方先卖；货款、关税一年一起算，不按每笔摊到各城。
    // 技术差(用户 10-29 "临高政体应当靠着技术差赚着比这个世界任何国家的钱都要多")：卖方科技比买方高几个时代，
    // 每高一代价钱加 EdgePremium(至多 EdgePremiumMax 倍)，本地有货也能挤进去卖掉需求的 EdgeDisplace(至多 EdgeDisplaceMax)——
    // 机器货又好又便宜；领先的卖方先卖。穿越政权另有船队海路(见 TransmigrantSystem.SeaTradePartners)
    private const float EdgePremium = 0.5f, EdgePremiumMax = 3f, EdgeDisplace = 0.15f, EdgeDisplaceMax = 0.6f;
    private static readonly Dictionary<long, int> EraTiers = new();

    private static int EraTier(Kingdom kingdom)
    {
        if (kingdom?.data == null) return 0;
        if (EraTiers.TryGetValue(kingdom.id, out int tier)) return tier;
        tier = CultureEra(CultureService.GetRealmCulture(kingdom));
        EraTiers[kingdom.id] = tier;
        return tier;
    }

    // 成交不到这个量不算一笔(小国一年只要零点几份，以前一笔不到半份就不做，小市场的奢侈品永远缺一百)
    private const float MinLot = 0.01f;

    // 基础物资(粮、木、石、金属)跨市场买卖另加的运费(占底价)
    private const float BasicFreight = 0.5f;

    private static int TechEdge(Market seller, Market buyer) => Math.Max(0, EraTier(seller.leader) - EraTier(buyer.leader));

    private static void Trade()
    {
        var links = new Dictionary<long, HashSet<long>>();
        // 陆上相连的市场：不在这里的只靠海路(见下面的破交)
        var landLinks = new HashSet<(long, long)>();
        (long, long) Pair(long a, long b) => a < b ? (a, b) : (b, a);
        void Link(long a, long b, bool sea = false)
        {
            if (a == b) return;
            if (!sea) landLinks.Add(Pair(a, b));
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
                // 港口被封锁、航线被敌方制海截断就断航
                if (partner?.kingdom?.data != null && MarketOfKingdom.TryGetValue(partner.kingdom.id, out long overseas) &&
                    SeaLaneOpen(city, partner)) Link(own, overseas, sea: true);
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
                    Link(home, other, sea: true);
        // 丝绸之路(见 WorldMap.SilkRoadSystem)：沿途相邻两站的城所属的国家不交战就互市，中间隔着沙漠、别国也连得上
        try
        {
            foreach ((Kingdom a, Kingdom b) in EmpireCraft.Scripts.GeneralSystems.WorldMap.SilkRoadSystem.Links())
                if (MarketOfKingdom.TryGetValue(a.id, out long first) && MarketOfKingdom.TryGetValue(b.id, out long second)) Link(first, second);
        }
        catch (Exception exception)
        {
            Warn("丝路互市", exception);
        }
        foreach (Market market in Markets.Values)
        {
            market.policy = PolicyOf(market.leader);
            market.seaOpen = PortsOpen(market);
        }
        // 领衔国交战(连同盟友，同 MarketSystem.Embargoed)禁运、任一边闭关(和约通商的除外)的不通：
        // 以前只看接壤的那一对成员国，主战的两国之间仍照常买卖
        foreach (KeyValuePair<long, HashSet<long>> pair in links)
            if (Markets.TryGetValue(pair.Key, out Market near))
                pair.Value.RemoveWhere(id => !Markets.TryGetValue(id, out Market far) || MarketSystem.Embargoed(near.leader, far.leader) ||
                                             (near.policy == TradePolicy.Closed || far.policy == TradePolicy.Closed) && !TreatyBetween(near, far));
        foreach (Market market in Markets.Values)
            if (market.leader != null && links.TryGetValue(market.leader.id, out HashSet<long> set))
                market.linked.UnionWith(set);
        // 各市场本年能花在进口上的钱：民间存款的 ImportBudgetShare，以及按需求额摊付的各城；没有背景人口(原版小人)的市场由领衔国国库出
        var purses = new Dictionary<Market, float>();
        var privateTrade = new HashSet<Market>();
        var bills = new Dictionary<Market, (float paid, float duty)>();
        var payers = new Dictionary<Market, List<(City city, Kingdom kingdom, CityPopulationData data, float weight)>>();
        foreach (Market market in Markets.Values)
        {
            var list = new List<(City, Kingdom, CityPopulationData, float)>();
            float purse = 0f;
            foreach (Kingdom member in market.members)
            {
                if (member?.cities == null) continue;
                foreach (City city in member.cities)
                {
                    CityPopulationData data = city?.data == null ? null : CityPopulationSystem.Get(city);
                    if (data == null || !Books.TryGetValue(city.id, out CityBook book)) continue;
                    float weight = book.wanted.Sum(pair => pair.Value * BasePrice(pair.Key));
                    if (weight <= 0f) continue;
                    list.Add((city, member, data, weight));
                    purse += Mathf.Max(0f, data.private_savings) * ImportBudgetShare;
                }
            }
            if (purse > 0f) privateTrade.Add(market);
            purses[market] = privateTrade.Contains(market) ? purse : Mathf.Max(0f, market.leader?.GetMoney() ?? 0);
            payers[market] = list;
        }
        var sales = new Dictionary<Market, float>();
        foreach (CommodityConfig good in Goods.Values)
        {
            sales.Clear();
            // 基础物资也买卖(以前不许，缺粮的市场买不到邻国的余粮)，又重又贱，另加运费
            float freight = good.tier == "basic" ? BasicFreight : 0f;
            // 最缺的先买(以前按字典顺序，先轮到的把邻国余货买光)
            foreach (Market buyer in Markets.Values.Where(market => market.leader?.data != null && !market.leader.isRekt() && market.linked.Count > 0 && market.demand.ContainsKey(good.id))
                         .OrderByDescending(market => Quote(market, good)).ThenByDescending(market => market.power))
            {
                buyer.supply.TryGetValue(good.id, out float supply);
                buyer.demand.TryGetValue(good.id, out float demand);
                float need = Mathf.Max(0f, demand - supply);
                if (demand <= 0f) continue;
                float displaced = 0f;
                float displaceShare = DisplaceFactor(buyer.policy);
                foreach (long id in buyer.linked.Where(Markets.ContainsKey)
                             .OrderByDescending(id => TechEdge(Markets[id], buyer)).ThenBy(id => Quote(Markets[id], good)))
                {
                    Market seller = Markets[id];
                    if (seller.leader?.data == null || seller.leader.isRekt()) continue;
                    float spare = Surplus(seller, good.id);
                    if (spare <= 0f) continue;
                    int edge = TechEdge(seller, buyer);
                    float tariff = TariffBetween(buyer, seller);
                    float wedge = 1f + tariff + freight;
                    float room, deal;
                    if (edge > 0)
                    {
                        // 技术差：加价，也能挤占本地货的份额，缺的先补
                        deal = good.price * Mathf.Min(EdgePremiumMax, 1f + edge * EdgePremium);
                        room = need + Mathf.Max(0f, demand * Mathf.Min(EdgeDisplaceMax, edge * EdgeDisplace) * displaceShare - displaced);
                    }
                    else
                    {
                        float ask = Quote(seller, good), bid = Quote(buyer, good);
                        float landed = ask * wedge;
                        if (landed >= bid) continue;
                        deal = (ask + bid / wedge) * 0.5f;
                        // 买到本地价压到与到岸价相平为止
                        buyer.imports.TryGetValue(good.id, out float boughtSoFar);
                        room = Mathf.Min(need, demand / Mathf.Max(1f, landed / good.price) - (supply + boughtSoFar));
                    }
                    // 破交：只靠海路相连的，港口被封了几成，能运的就少几成
                    float open = Mathf.Min(buyer.seaOpen, seller.seaOpen);
                    if (open < 1f && !landLinks.Contains(Pair(buyer.leader.id, seller.leader.id)))
                    {
                        room *= open;
                        buyer.seaLimited.Add(good.id);
                    }
                    if (room <= 0f) continue;
                    // 进口是民间的买卖：货款(连关税、运费)从买方各城的民间存款出
                    float unit = deal * wedge;
                    purses.TryGetValue(buyer, out float purse);
                    float affordable = purse / Mathf.Max(0.01f, unit);
                    if (affordable < Mathf.Min(room, spare)) buyer.outOfMoney.Add(good.id);
                    float amount = Mathf.Min(room, Mathf.Min(spare, affordable));
                    if (amount < MinLot) continue;
                    float paid = amount * unit, earned = amount * deal;
                    purses[buyer] = purse - paid;
                    bills.TryGetValue(buyer, out var bill);
                    bills[buyer] = (bill.paid + paid, bill.duty + earned * tariff);
                    sales[seller] = (sales.TryGetValue(seller, out float sold) ? sold : 0f) + earned;
                    Add(buyer.imports, good.id, amount);
                    Add(seller.exports, good.id, amount);
                    AddTrade(buyer.importsFrom, good.id, seller.leader.id, amount);
                    AddTrade(seller.exportsTo, good.id, buyer.leader.id, amount);
                    buyer.trade_value += paid;
                    seller.trade_value += earned;
                    buyer.boughtFrom[seller.leader.id] = (buyer.boughtFrom.TryGetValue(seller.leader.id, out float spent) ? spent : 0f) + paid;
                    seller.soldTo[buyer.leader.id] = (seller.soldTo.TryGetValue(buyer.leader.id, out float got) ? got : 0f) + earned;
                    float filled = Mathf.Min(amount, need);
                    need -= filled;
                    displaced += amount - filled;
                }
            }
            foreach (KeyValuePair<Market, float> sale in sales) ReceiveExports(sale.Key, good, sale.Value);
        }
        foreach (KeyValuePair<Market, (float paid, float duty)> bill in bills)
        {
            Market buyer = bill.Key;
            if (privateTrade.Contains(buyer))
            {
                PayImports(buyer, payers[buyer], bill.Value.paid, bill.Value.duty);
                continue;
            }
            buyer.leader.SubMoney(Mathf.RoundToInt(bill.Value.paid), TreasuryCategory.PublicTrade);
            buyer.memberImports[buyer.leader.id] = (buyer.memberImports.TryGetValue(buyer.leader.id, out float before) ? before : 0f) + bill.Value.paid;
        }
    }

    private static void AddTrade(Dictionary<string, Dictionary<long, float>> book, string good, long partner, float amount)
    {
        if (!book.TryGetValue(good, out Dictionary<long, float> partners)) book[good] = partners = new Dictionary<long, float>();
        partners[partner] = (partners.TryGetValue(partner, out float before) ? before : 0f) + amount;
    }

    // 进口花在各城民间存款上的份额(本年)
    private const float ImportBudgetShare = 0.3f;

    // 进口货款按各城的需求额摊到各城民间存款；关税(货价 × 关税率)按同样的份额归各城所属的国库
    private static void PayImports(Market buyer, List<(City city, Kingdom kingdom, CityPopulationData data, float weight)> cities,
        float paid, float duty)
    {
        float total = cities.Sum(entry => entry.weight);
        if (total <= 0f) return;
        var dutyOf = new Dictionary<Kingdom, float>();
        foreach ((City city, Kingdom kingdom, CityPopulationData data, float weight) in cities)
        {
            float share = weight / total;
            float pay = paid * share;
            data.private_savings = Mathf.Max(0f, Mathf.Max(0f, data.private_savings) - pay);
            buyer.memberImports[kingdom.id] = (buyer.memberImports.TryGetValue(kingdom.id, out float before) ? before : 0f) + pay;
            dutyOf[kingdom] = (dutyOf.TryGetValue(kingdom, out float sum) ? sum : 0f) + duty * share;
        }
        foreach (KeyValuePair<Kingdom, float> pair in dutyOf)
        {
            int whole = Mathf.RoundToInt(pair.Value);
            if (whole <= 0 || pair.Key?.data == null || pair.Key.isRekt()) continue;
            pair.Key.AddMoney(whole, TreasuryCategory.Customs);
            buyer.memberCustoms[pair.Key.id] = (buyer.memberCustoms.TryGetValue(pair.Key.id, out float before) ? before : 0f) + whole;
        }
    }

    // 出口收入按各城这样物资的产量记进各城产值(交工商税、进民间收入，见 PopulationEconomySystem.PayTaxes)；
    // 没有背景人口的市场照旧进领衔国国库
    private static void ReceiveExports(Market seller, CommodityConfig good, float earned)
    {
        if (earned <= 0f) return;
        var makers = new List<(City city, Kingdom kingdom, CityPopulationData data, float made)>();
        foreach (Kingdom member in seller.members)
        {
            if (member?.cities == null) continue;
            foreach (City city in member.cities)
            {
                CityPopulationData data = city?.data == null ? null : CityPopulationSystem.Get(city);
                if (data == null || !Books.TryGetValue(city.id, out CityBook book) || !book.made.TryGetValue(good.id, out float made) || made <= 0f)
                    continue;
                makers.Add((city, member, data, made));
            }
        }
        float total = makers.Sum(entry => entry.made);
        if (total <= 0f)
        {
            seller.leader.AddMoney(Mathf.RoundToInt(earned), TreasuryCategory.PublicTrade);
            seller.memberExports[seller.leader.id] = (seller.memberExports.TryGetValue(seller.leader.id, out float before) ? before : 0f) + earned;
            return;
        }
        string sector = (good.tier == "basic" && good.id == "grain" ? TreasuryCategory.LandTax : TreasuryCategory.IndustrialTax).ToString();
        foreach ((City city, Kingdom kingdom, CityPopulationData data, float made) in makers)
        {
            float income = earned * made / total;
            data.produced_value += income;
            var sectors = data.produced_sector_values ??= new Dictionary<string, float>();
            sectors.TryGetValue(sector, out float previous);
            sectors[sector] = previous + income;
            seller.memberExports[kingdom.id] = (seller.memberExports.TryGetValue(kingdom.id, out float before) ? before : 0f) + income;
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

    // 通商政策：AI 领衔国每二十年看一次：技术领先又不入超的要自由贸易，技术落后、入超又有工厂的要保护关税，大国出超的要自由贸易，其余重商。
    // 神力改过的也算定过一次(见 SetPolicy)；制度定了锁国、和约逼开港的不改；AI 不会自己闭关
    private const int PolicyYears = 20;
    private const float PolicyChance = 0.2f;

    private static void Policies()
    {
        foreach (Market market in Markets.Values.ToList())
        {
            Kingdom leader = market.leader;
            if (leader?.data == null || leader.isRekt() || TradeRouteSystem.ClosedByLaw(leader) || TradeRouteSystem.TreatyPartner(leader) != null) continue;
            var data = leader.GetOrCreate();
            if (data.market_policy_at >= 0d && Date.getYearsSince(data.market_policy_at) < PolicyYears) continue;
            // 错开各国改政策的年份
            if (UnityEngine.Random.value >= PolicyChance) continue;
            data.market_policy_at = World.world.getCurWorldTime();
            TradePolicy wanted = WantedPolicy(market);
            if ((TradePolicy)data.market_policy == wanted) continue;
            data.market_policy = (int)wanted;
            LogPolicy(leader, wanted);
        }
    }

    private static TradePolicy WantedPolicy(Market market)
    {
        int own = EraTier(market.leader);
        bool ahead = false, behind = false;
        foreach (long id in market.linked)
        {
            if (!Markets.TryGetValue(id, out Market other)) continue;
            int era = EraTier(other.leader);
            ahead |= own > era;
            behind |= own < era;
        }
        float bought = market.boughtFrom.Values.Sum(), sold = market.soldTo.Values.Sum();
        if (ahead && !behind && sold >= bought) return TradePolicy.Free;
        if (behind && !ahead && bought > sold && market.demandFactory.Count > 0) return TradePolicy.Protective;
        return market.great && sold > bought * 1.5f ? TradePolicy.Free : TradePolicy.Mercantile;
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
                               $"成员 {market.members.Count} 国，{market.policy}，外贸 {Mathf.RoundToInt(market.trade_value)}，缺：{(shortages.Length == 0 ? "无" : shortages)}");
        }
    }

    #endregion
}
