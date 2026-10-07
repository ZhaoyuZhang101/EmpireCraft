using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 市场与交换(无小人模式，参考维多利亚 3)：原版靠小人一船一车运货，没有平民实体后贸易就停了。
// 这里按数据结算：
//   市场：同一国家(帝国则是整个帝国)的城市组成国内市场；有市场或码头的城还能和相邻、未交战的外国城市通商。
//   商品：粮食、木材、石料、金属。每城按户数算出应有储备(粮食一年口粮；木石按建设；金属按打造装备，交战时多备)。
//   交换：每月，储备不足的城向市场里有余量(超出储备一半以上)的城买进，按价格付钱
//        (粮食由民间存款付，木石金属由城市国库付，卖方的货是百姓的，见 PopulationEconomySystem.Savings)，
//        卖方收九成(一成是商人的利润，归卖方城里的背景商人；
//        卖粮的钱里租地收成那部分归地主，见 LandEconomySystem.AddBackgroundIncome)；每月能买卖多少受商贸能力限制(商人越多、有市场码头越多)。
//   价格：按市场总需求 ÷ 总供给在基础价的 0.25~4 倍之间浮动。
//   传播：买方受卖方主流理念影响；卖方的宗教(买方没有自建神庙时)、跨国时卖方的语言有小概率传入。
//   每次交换记作一次商队往来(见 ConstitutionalEconomySystem.RecordTrade)，城市就业的"近期航运"等都会计入。
public static class MarketSystem
{
    public enum Good { Food, Wood, Stone, Metal }
    public static readonly Good[] Goods = { Good.Food, Good.Wood, Good.Stone, Good.Metal };

    private static readonly Dictionary<Good, float> BasePrice = new()
        { [Good.Food] = 1f, [Good.Wood] = 1f, [Good.Stone] = 1.5f, [Good.Metal] = 3f };

    private const float SurplusCushion = 1.5f;
    private const float SellerShare = 0.9f;
    private const float MinPriceFactor = 0.25f;
    private const float MaxPriceFactor = 4f;
    private const int BaseCapacity = 5;
    private const int CapacityPerMerchantHousehold = 2;
    private const int MarketCapacity = 20;
    private const int DocksCapacity = 20;
    private const float IdeologyInfluencePerTrade = 0.002f;
    private const float MaxReligionChance = 0.01f;
    private const float ReligionChancePerVolume = 0.0002f;

    private static List<ResourceAsset> _food;

    private static List<ResourceAsset> FoodAssets()
    {
        if (_food != null) return _food;
        _food = new List<ResourceAsset>();
        if (AssetManager.resources?.list != null)
            foreach (ResourceAsset asset in AssetManager.resources.list)
                if (asset != null && asset.type == ResType.Food) _food.Add(asset);
        return _food;
    }

    public static int Stock(City city, Good good)
    {
        switch (good)
        {
            case Good.Food:
                int food = 0;
                foreach (ResourceAsset asset in FoodAssets()) food += city.getResourcesAmount(asset.id);
                return food;
            case Good.Wood: return city.getResourcesAmount("wood");
            case Good.Stone: return city.getResourcesAmount("stone");
            default: return city.getResourcesAmount("common_metals");
        }
    }

    public static float Reserve(City city, Good good)
    {
        int households = CityPopulationSystem.Households(city);
        return good switch
        {
            Good.Food => households,
            Good.Wood => 15f + households * 0.2f,
            Good.Stone => 10f + households * 0.1f,
            _ => 5f + households * 0.05f + (CityPopulationSystem.IsAtWar(city) ? 10f : 0f)
        };
    }

    private static float Surplus(City city, Good good) => Mathf.Max(0f, Stock(city, good) - Reserve(city, good) * SurplusCushion);
    private static float Deficit(City city, Good good) => Mathf.Max(0f, Reserve(city, good) - Stock(city, good));

    public static int Capacity(City city)
    {
        int capacity = BaseCapacity;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups != null)
        {
            float merchants = 0f;
            foreach (PopGroup group in data.groups)
                if (group.social_class == SocialClass.Merchant) merchants += group.Background;
            capacity += Mathf.RoundToInt(merchants / CityPopulationSystem.PeoplePerSlot(city)) * CapacityPerMerchantHousehold;
        }
        if (city.hasBuildingType("type_market")) capacity += MarketCapacity;
        if (city.hasBuildingType("type_docks")) capacity += DocksCapacity;
        return capacity;
    }

    // ---- 禁运与封锁 ----
    // 交战双方互不通商；双方的盟友也跟着禁运(本国盟友不和本国的敌人做生意，敌人的盟友也不和本国做生意)；
    // 正在被攻打、或一半以上区块被敌军占领的城被围封锁，买不进也卖不出
    public static bool Embargoed(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b) return false;
        if (a.isEnemy(b)) return true;
        if (a.hasAlliance())
            foreach (Kingdom ally in a.getAlliance().kingdoms_list)
                if (ally != null && ally != a && !ally.isRekt() && ally.isEnemy(b)) return true;
        if (b.hasAlliance())
            foreach (Kingdom ally in b.getAlliance().kingdoms_list)
                if (ally != null && ally != b && !ally.isRekt() && ally.isEnemy(a)) return true;
        return false;
    }

    public static bool Blockaded(City city)
    {
        if (city == null || city.isRekt()) return true;
        if (city.isGettingCaptured()) return true;
        Dictionary<int, long> occupied = ScorchedEarthSystem.OccupiedZones(city);
        return occupied != null && city.zones != null && city.zones.Count > 0 && occupied.Count * 2 >= city.zones.Count;
    }

    // ---- 围城断粮开城 ----
    // 被围封锁又闹饥荒，每月累计一次；满 SiegeFamineMonths 个月后每月有 (月数 - 2) × 10% 的概率守将开城投降，
    // 降给正在攻城的一方(没有就是占着本城区块最多的敌国)，按城破处理(伤亡、逃难、抢粮)
    private const int SiegeFamineMonths = 3;
    private const float SurrenderChancePerMonth = 0.1f;

    public static void CheckSiegeSurrender(City city, CityPopulationData data)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.kingdom == null || data == null) return;
        bool famine = data.last_food_shortage > 0f || CityPopulationSystem.GetGrowthFactors(city, data).Famine;
        if (!Blockaded(city) || !famine)
        {
            data.siege_famine_months = 0;
            return;
        }
        data.siege_famine_months++;
        if (data.siege_famine_months < SiegeFamineMonths) return;
        float chance = Mathf.Min(0.9f, SurrenderChancePerMonth * (data.siege_famine_months - 2));
        if (UnityEngine.Random.value >= chance) return;
        Kingdom besieger = city.being_captured_by;
        if (besieger == null || besieger.isRekt() || !besieger.isEnemy(city.kingdom))
        {
            besieger = null;
            Dictionary<int, long> occupied = ScorchedEarthSystem.OccupiedZones(city);
            if (occupied != null)
            {
                var counts = new Dictionary<long, int>();
                foreach (long id in occupied.Values) counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
                int best = 0;
                foreach (KeyValuePair<long, int> pair in counts)
                {
                    Kingdom candidate = World.world.kingdoms.get(pair.Key);
                    if (candidate == null || candidate.isRekt() || !candidate.isEnemy(city.kingdom) || pair.Value <= best)
                        continue;
                    best = pair.Value;
                    besieger = candidate;
                }
            }
        }
        if (besieger == null) return;
        string cityName = city.GetCityName();
        Kingdom oldOwner = city.kingdom;
        data.siege_famine_months = 0;
        city.joinAnotherKingdom(besieger, true);
        if (city.kingdom == oldOwner) return;
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(
            string.Format(NeoModLoader.General.LM.Get("siege_famine_surrender"), cityName, besieger.GetKingdomName()),
            besieger);
    }

    // ---- 市场范围与价格 ----

    private static List<City> DomesticMarket(City city)
    {
        Empire empire = city.kingdom?.GetEmpire();
        if (empire != null && !empire.isRekt() && !empire.IsArchived()) return empire.AllCities();
        return city.kingdom?.cities ?? new List<City>();
    }

    // 能做生意的卖家：国内市场，加上有市场或码头时相邻、未交战的外国城市
    private static List<City> Sellers(City city)
    {
        var sellers = new List<City>();
        foreach (City other in DomesticMarket(city))
            if (other != null && other != city && !other.isRekt() && !Blockaded(other) &&
                !Embargoed(city.kingdom, other.kingdom)) sellers.Add(other);
        if (!city.hasBuildingType("type_market") && !city.hasBuildingType("type_docks")) return sellers;
        if (city.neighbours_cities == null) return sellers;
        foreach (City other in city.neighbours_cities)
        {
            if (other?.kingdom == null || other.isRekt() || sellers.Contains(other) || other.kingdom.wild) continue;
            if (other.kingdom == city.kingdom || Embargoed(city.kingdom, other.kingdom) || Blockaded(other)) continue;
            sellers.Add(other);
        }
        return sellers;
    }

    private static readonly Dictionary<(object market, Good good), (double at, float price)> PriceCache = new();

    // 市场价格：国内市场总需求 ÷ 总供给(每个游戏月重算)
    public static float Price(City city, Good good)
    {
        List<City> market = DomesticMarket(city);
        object key = city.kingdom?.GetEmpire() ?? (object)city.kingdom;
        double now = World.world?.getCurWorldTime() ?? 0d;
        if (key != null && PriceCache.TryGetValue((key, good), out var cached) && now >= cached.at &&
            Date.getMonthsSince(cached.at) < 1) return cached.price;
        float demand = 0f, supply = 0f;
        foreach (City other in market)
        {
            if (other == null || other.isRekt()) continue;
            demand += Reserve(other, good);
            supply += Stock(other, good);
        }
        float factor = supply <= 0f ? MaxPriceFactor : Mathf.Clamp(demand / supply, MinPriceFactor, MaxPriceFactor);
        float price = BasePrice[good] * factor;
        if (key != null) PriceCache[(key, good)] = (now, price);
        return price;
    }

    // ---- 交换(每月随无小人模式的城市结算调用) ----

    public static void Settle(City buyer, CityPopulationData data)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || buyer?.kingdom == null || buyer.kingdom.wild || data == null)
            return;
        // 被围封锁：买不进
        if (Blockaded(buyer))
        {
            data.last_market_bought = 0f;
            return;
        }
        try
        {
            int capacity = Capacity(buyer);
            float bought = 0f;
            // 本月已经用民间存款买粮花掉的钱
            float foodSpent = 0f;
            List<City> sellers = null;
            foreach (Good good in Goods)
            {
                if (capacity <= 0) break;
                float need = Deficit(buyer, good);
                if (need < 1f) continue;
                sellers ??= Sellers(buyer);
                float price = Price(buyer, good);
                // 余量最多的卖家先卖(每种商品只算一次各卖家的余量)
                var offers = new List<(City seller, float surplus)>(sellers.Count);
                foreach (City candidate in sellers)
                {
                    float available = Surplus(candidate, good);
                    if (available >= 1f) offers.Add((candidate, available));
                }
                offers.Sort((a, b) => b.surplus.CompareTo(a.surplus));
                foreach ((City seller, float surplus) in offers)
                {
                    if (need < 1f || capacity <= 0) break;
                    // 粮食是百姓自己买，看民间存款；木石金属是公家买，看城市国库
                    float purse = good == Good.Food
                        ? Mathf.Max(0f, PopulationEconomySystem.Savings(buyer, data) - foodSpent)
                        : Mathf.Max(0, buyer.GetMoney());
                    int affordable = price <= 0f ? 0 : Mathf.FloorToInt(purse / price);
                    int amount = Mathf.Min(Mathf.FloorToInt(Mathf.Min(need, surplus)), Mathf.Min(capacity, affordable));
                    amount = Mathf.Min(amount, Capacity(seller));
                    if (amount <= 0) continue;
                    int moved;
                    using (PopulationEconomySystem.PrivateUse()) moved = Move(seller, buyer, good, amount);
                    if (moved <= 0) continue;
                    int value = Mathf.CeilToInt(moved * price);
                    // 买粮：钱换粮，民间家底不变；买木石金属：城市国库付钱，买来的算公家的
                    if (good != Good.Food)
                    {
                        buyer.SubMoney(value);
                        PopulationEconomySystem.AddPublicStock(buyer, MaterialId(good), moved);
                    }
                    else foodSpent += value;
                    // 卖方：先卖公家的存货，九成价钱归城市国库
                    int fromPublic = good == Good.Food ? 0 : PopulationEconomySystem.TakePublicStock(seller, MaterialId(good), moved);
                    int publicValue = Mathf.FloorToInt(value * (float)fromPublic / moved);
                    if (publicValue > 0) seller.AddMoney(Mathf.FloorToInt(publicValue * SellerShare));
                    // 其余是百姓的货，九成价卖掉，家底少一成(商人的利润)；卖粮的钱里租地收成那部分是地主的
                    int privateValue = value - publicValue;
                    int sellerGets = Mathf.FloorToInt(privateValue * SellerShare);
                    int rent = good == Good.Food
                        ? Mathf.FloorToInt(sellerGets * LandEconomySystem.BackgroundRentShare(seller)) : 0;
                    LandEconomySystem.AddBackgroundIncome(seller, privateValue - sellerGets, rent);
                    need -= moved;
                    capacity -= moved;
                    bought += value;
                    RecordSale(seller, value);
                    bool foreign = seller.kingdom != buyer.kingdom &&
                                   (seller.kingdom?.GetEmpire() == null || seller.kingdom.GetEmpire() != buyer.kingdom.GetEmpire());
                    ConstitutionalEconomySystem.RecordTrade(seller, buyer, foreign, value);
                    Spread(seller, buyer, moved, foreign);
                }
            }
            data.last_market_bought = bought;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][市场] 交换失败({buyer.data?.name}): {exception.Message}");
        }
    }

    private static readonly Dictionary<City, float> SoldThisMonth = new();

    private static void RecordSale(City seller, int value)
    {
        CityPopulationData data = CityPopulationSystem.Get(seller);
        if (data == null) return;
        double now = World.world.getCurWorldTime();
        if (data.market_sold_since < 0d || Date.getMonthsSince(data.market_sold_since) >= 1)
        {
            data.last_market_sold = data.market_sold_month;
            data.market_sold_month = 0f;
            data.market_sold_since = now;
        }
        data.market_sold_month += value;
    }

    private static string MaterialId(Good good) =>
        good == Good.Wood ? "wood" : good == Good.Stone ? "stone" : "common_metals";

    // 从卖方仓库搬到买方仓库，返回实际搬运的数量
    private static int Move(City seller, City buyer, Good good, int amount)
    {
        int moved = 0;
        if (good == Good.Food)
        {
            foreach (ResourceAsset asset in FoodAssets())
            {
                if (moved >= amount) break;
                int have = seller.getResourcesAmount(asset.id);
                int take = Mathf.Min(have, amount - moved);
                if (take <= 0) continue;
                seller.takeResource(asset.id, take);
                int added = buyer.addResourcesToRandomStockpile(asset.id, take);
                moved += added;
                if (added < take) seller.addResourcesToRandomStockpile(asset.id, take - added);
            }
            return moved;
        }
        string id = MaterialId(good);
        int available = Mathf.Min(amount, seller.getResourcesAmount(id));
        if (available <= 0) return 0;
        seller.takeResource(id, available);
        moved = buyer.addResourcesToRandomStockpile(id, available);
        if (moved < available) seller.addResourcesToRandomStockpile(id, available - moved);
        return moved;
    }

    // 商路传播：理念、宗教、(跨国时)语言
    private static void Spread(City seller, City buyer, int volume, bool foreign)
    {
        PartyIdeology ideology = IdeologyPopulationSystem.GetDominant(seller);
        if (IdeologyPopulationSystem.GetDominant(buyer) != ideology)
            IdeologyPopulationSystem.InfluenceCity(buyer, ideology, Mathf.Min(0.05f, IdeologyInfluencePerTrade * volume));
        float chance = Mathf.Min(MaxReligionChance, ReligionChancePerVolume * volume);
        if (seller.religion != null && buyer.religion != seller.religion && !buyer.hasBuildingType("type_temple") &&
            UnityEngine.Random.value < chance)
            buyer.setReligion(seller.religion);
        if (foreign && seller.language != null && buyer.language != seller.language && UnityEngine.Random.value < chance)
            buyer.setLanguage(seller.language);
    }

    public static void ResetWorldState()
    {
        PriceCache.Clear();
        SoldThisMonth.Clear();
    }
}
