using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 村镇生计(用户 2026-10-17 "原来的城市只是行政规划，下面会有好多的村和镇"，问答选"变成村镇的生计")：
// 纯净地图下城只是行政单位，出产落在城外各村镇，每处一种生计——
//   · 农村：种田产粮(见 FarmlandSystem.RuralHarvest；田只属于农村，四周画条田)；
//   · 林场村：伐木出木头(原伐木场五级随科技换代：伐木营 → 锯木坊 → 水力 → 蒸汽 → 现代木材加工厂)；
//   · 矿村：开矿出石头、金属、金子，连同本城的矿藏(金银铜煤……，见 MineralResourceSystem；原矿场五级随科技换代)；
//   · 牧村：放牧(牧群容量、产肉产皮，见 AnimalHusbandrySystem)，研究了制革再宰杀、狩猎；
//   · 渔村：打鱼出鱼，不看农时。
// 一份(村 1、镇 3，有墙大镇也是 3)折成半座原来的矿场、伐木场、牧场，乘科技等级的倍数，本城有厂区再多两成五；
// 田、牧按农具，鱼按渔船，也随科技换代(见 FarmlandSystem.FarmTools)。
// 生计按村镇四周的地形定(地形归类见 LandKinds)：山多为矿；本城务农的不到四成时能耕的地先务农；本城有眼下开得了的矿藏
// 却还没有矿村时先开一处矿；靠海为渔；密林(桦、枫、魔法林、丛林)为林；荒漠、稀树草原、荒原为牧(这些地方不算能耕，
// 先牧后农，只在本城几乎没有产粮的镇村时才旱作)；草地、橡树松树疏林也能放牧、伐木，但宜农，本城产粮的够六成才轮到；
// 定了存进存档(RuralSettlement.trade)，小窗口里可以手动改(手动改过的不再自动改)。
// 原版的矿场、伐木场、牧场、屠宰场在纯净地图下不再建，旧有的清掉(见 Replaces)；非纯净地图(开小人)照旧走原版建筑。
// 林场、矿、牧是产业，分官营民营(见 RuralSettlement.state、EnterpriseSystem.CountEnterprises)。
public static class LivelihoodSystem
{
    public const int Farm = 0, Forest = 1, Mine = 2, Pasture = 3, Fish = 4, Count = 5;
    private static readonly string[] Keys = { "farm", "forest", "mine", "pasture", "fish" };
    // 一份折成几座原来的矿场、伐木场、牧场
    private const float PerShare = 0.5f;
    // 城心那 6 份(见 RuralSettlementSystem.CityWeight)
    private const float CityWeight = 6f;
    // 每个干活的人折几个岗位(同 FarmlandSystem.RuralHarvest 的农田岗位)
    private const float JobsPerWorker = 0.6f;
    // 渔户一年打的鱼(种田是 1.2，见 FarmlandSystem.RuralYield；打鱼不看农时)
    private const float FishYield = 1f;
    // 看地形的半径(格)
    private const int Radius = 7;
    // 游牧城没有镇村实体(营地，见 UrbanFabric.PlaceCamps)：照原来一座牧场加游牧多许的两座
    private const float NomadPastures = 3f;

    public static bool Active => PureMapMode.Enabled;

    public static string Key(int trade) => Keys[Mathf.Clamp(trade, 0, Count - 1)];
    public static string Name(int trade) => LM.Get("livelihood_" + Key(trade));

    // 是产业(林、矿、牧)：分官营民营，官营的利润上缴(见 EnterpriseSystem)
    public static bool IsEnterprise(int trade) => trade is Forest or Mine or Pasture;

    // 生计规则的版本：旧档按旧规则定的镇村是 0(见 RecheckForcedMines)
    public const int RulesVersion = 1;

    // 这处镇村的生计(关城没有，-1)；还没定的按地形定下、存进存档
    public static int Of(RuralSettlement settlement)
    {
        if (settlement == null || settlement.fort) return -1;
        if (settlement.trade >= 0 && settlement.trade < Count) return settlement.trade;
        settlement.trade = Classify(settlement);
        settlement.trade_rules = RulesVersion;
        return settlement.trade;
    }

    public static int At(int x, int y) => Of(RuralSettlementSystem.At(x, y));

    // 本城产粮的镇村够这么多成，宜农的草地、疏林才轮得到放牧、伐木
    private const float ArableSpareFood = 0.6f;
    // 本城产粮的镇村不到这么多成，旱地(荒漠、稀树草原、荒原)也先旱作
    private const float DryFarmFood = 0.25f;

    // 四周(半径 Radius)的地形构成：山石、密林、疏林、草地、旱地、能耕地各占陆地几成，海面占几成
    private readonly struct Surroundings
    {
        public readonly float Rough, Forest, Woodland, Grass, Dry, Arable, Sea;

        public Surroundings(float rough, float forest, float woodland, float grass, float dry, float arable, float sea)
        {
            Rough = rough;
            Forest = forest;
            Woodland = woodland;
            Grass = grass;
            Dry = dry;
            Arable = arable;
            Sea = sea;
        }
    }

    private static Surroundings Survey(RuralSettlement settlement)
    {
        int land = 0, rough = 0, forest = 0, woodland = 0, grass = 0, dry = 0, arable = 0, sea = 0;
        for (int dy = -Radius; dy <= Radius; dy++)
        for (int dx = -Radius; dx <= Radius; dx++)
        {
            if (dx * dx + dy * dy > Radius * Radius) continue;
            WorldTile tile = World.world?.GetTile(settlement.x + dx, settlement.y + dy);
            TileTypeBase type = tile?.Type;
            if (type == null) continue;
            int kinds = LandKinds.Of(type);
            // 海面(连同冰面)算渔场；岩浆不算水也不算地
            if ((kinds & LandKinds.Water) != 0)
            {
                if (type.ocean) sea++;
                continue;
            }
            if ((kinds & LandKinds.Lava) != 0) continue;
            land++;
            if ((kinds & LandKinds.Rough) != 0) rough++;
            if ((kinds & LandKinds.Forest) != 0) forest++;
            if ((kinds & LandKinds.Woodland) != 0) woodland++;
            if ((kinds & LandKinds.Grass) != 0) grass++;
            if ((kinds & LandKinds.Dry) != 0) dry++;
            if ((kinds & LandKinds.Arable) != 0) arable++;
        }
        float total = Mathf.Max(1, land);
        return new Surroundings(rough / total, forest / total, woodland / total, grass / total, dry / total, arable / total,
            sea / (float)Mathf.Max(1, sea + land));
    }

    private static int Classify(RuralSettlement settlement)
    {
        City city = RuralSettlementSystem.CityOf(settlement);
        Surroundings s = Survey(settlement);
        float food = FoodShare(city, settlement);
        bool needFood = food < 0.4f;
        // 本城有眼下开得了的矿藏(金银铜……)却还没有矿村：先开一处，不然矿藏白放着；要更高等级的矿等科技到了再说
        bool needMine = city != null && !HasTrade(city, Mine, settlement) && MineralResourceSystem.HasMinableDeposit(city);
        if (s.Rough >= 0.4f) return Mine;
        if (needFood && s.Arable >= 0.5f) return Farm;
        // 旱地不算能耕：本城几乎没有产粮的镇村时才旱作(绿洲、河谷)
        if (food < DryFarmFood && s.Arable + s.Dry >= 0.5f) return Farm;
        if (needMine) return Mine;
        if (s.Sea >= 0.3f) return Fish;
        if (s.Forest >= 0.45f) return Forest;
        // 荒漠、稀树草原、荒原：先牧后农
        if (s.Dry >= 0.5f) return Pasture;
        // 草地、橡树松树疏林宜农：本城产粮的够六成才放牧、伐木
        if (food >= ArableSpareFood)
        {
            if (s.Forest + s.Woodland >= 0.45f) return Forest;
            if (s.Dry + s.Grass >= 0.5f) return Pasture;
        }
        if (s.Rough >= 0.25f) return Mine;
        return Farm;
    }

    // 旧档：以前本城有任何矿藏(连同还开不了的铀、石油)就硬派一处矿村，平地上也是。按新规则(地形归类、只看开得了的矿)
    // 重查一次：没在窗口里手动改过、四周山石不到两成半、本城眼下没有开得了的矿藏的矿村，改按地形重定生计。
    // 由人口经济结算调用(那时科技已读好)，每处镇村只查一次
    public static void RecheckForcedMines(City city)
    {
        if (!Active || city?.data == null) return;
        bool? minable = null;
        bool changed = false;
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
        {
            if (settlement.trade_rules >= RulesVersion) continue;
            settlement.trade_rules = RulesVersion;
            if (settlement.fort || settlement.trade_manual || settlement.trade != Mine) continue;
            minable ??= MineralResourceSystem.HasMinableDeposit(city);
            if (minable.Value || Survey(settlement).Rough >= 0.25f) continue;
            settlement.trade = Classify(settlement);
            if (settlement.trade != Mine) changed = true;
        }
        if (changed) RuralSettlementSystem.TradeChanged(city);
    }

    // 本城已定了生计的镇村里，产粮(农、渔)的份数占几成；还没有的算零
    private static float FoodShare(City city, RuralSettlement except)
    {
        if (city == null) return 1f;
        float food = 0f, all = 0f;
        foreach (RuralSettlement other in RuralSettlementSystem.InCity(city))
        {
            if (other == except || other.fort || other.trade < 0) continue;
            float weight = RuralSettlementSystem.Weight(other);
            all += weight;
            if (other.trade is Farm or Fish) food += weight;
        }
        return all <= 0f ? 0f : food / all;
    }

    private static bool HasTrade(City city, int trade, RuralSettlement except)
    {
        foreach (RuralSettlement other in RuralSettlementSystem.InCity(city))
            if (other != except && !other.fort && other.trade == trade) return true;
        return false;
    }

    // ---- 本城各生计的份数与产出 ----

    // 本城完好的镇村里做这一行的份数(村 1、镇 3)
    public static float Shares(City city, int trade)
    {
        if (city == null) return 0f;
        float shares = 0f;
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
            if (settlement.ruined_at < 0d && Of(settlement) == trade) shares += RuralSettlementSystem.Weight(settlement);
        return shares;
    }

    // 本文化这一行做到第几级(原矿场、伐木场五级，农具、渔船，都随科技换代，见 FarmlandSystem.FarmTools)
    public static int Level(City city, int trade) => trade switch
    {
        Mine => Mathf.Max(1, IndustryBuildingSystem.UnlockedTier(city, IndustryBuildingSystem.MineChain)),
        Forest => Mathf.Max(1, IndustryBuildingSystem.UnlockedTier(city, IndustryBuildingSystem.LumberChain)),
        Farm => FarmlandSystem.FarmToolTier(city),
        Fish => FarmlandSystem.FishToolTier(city),
        _ => 1
    };

    // 小窗口里生计后面跟的：矿、林第几级，农、渔用什么农具渔船
    public static string LevelText(City city, int trade) => city == null ? "" : trade switch
    {
        Mine or Forest => string.Format(LM.Get("livelihood_level"), Level(city, trade)),
        Farm => " · " + LM.Get("farm_tool_" + Level(city, trade)),
        Fish => " · " + LM.Get("fish_tool_" + Level(city, trade)),
        _ => ""
    };

    // 折成几座原来的矿场、伐木场、牧场
    public static float Equivalent(City city, int trade) => Active ? Shares(city, trade) * PerShare : 0f;

    // 矿、林的产出倍数(代替原来各座矿场、伐木场的等级倍数之和，见 IndustryBuildingSystem.OutputFactor)
    public static float Factor(City city, int trade)
    {
        float equivalent = Equivalent(city, trade);
        if (equivalent <= 0f) return 0f;
        return equivalent * IndustryBuildingSystem.TierFactor(Level(city, trade)) * IndustryBuildingSystem.FactoryBonus(city);
    }

    // 牧场、屠宰场折几座(见 AnimalHusbandrySystem)：游牧营地(没有镇村，见 CityStyleSystem.Nomad)照原来三座牧场——
    // 只看城是不是营地，不看文化线(游牧一系也有阿兹特克、林地部族，他们是农耕、渔猎)；研究了制革，牧村、林场村的一半折成屠宰场
    public static float Pastures(City city) =>
        CityStyleSystem.Nomad(city) ? Mathf.Max(NomadPastures, Equivalent(city, Pasture)) : Equivalent(city, Pasture);

    public static float Slaughterhouses(City city)
    {
        if (city == null || !TechnologySystem.CanBuild(city, "ec_slaughterhouse")) return 0f;
        float game = (Shares(city, Pasture) + Shares(city, Forest)) * PerShare * 0.5f;
        return CityStyleSystem.Nomad(city) ? Mathf.Max(1f, game) : game;
    }

    // 原版建筑类型折几座(科技加速"本文化有几座矿场"等，见 TechnologySystem.IsBoostMet)
    public static int BuildingEquivalent(City city, string type)
    {
        if (!Active || city == null) return 0;
        float count = type switch
        {
            "type_mine" => Equivalent(city, Mine),
            "type_lumber" => Equivalent(city, Forest),
            "type_pasture" => Pastures(city),
            "type_slaughterhouse" => Slaughterhouses(city),
            _ => 0f
        };
        return Mathf.RoundToInt(count);
    }

    // 本城做这一行的户数(按份数分背景户数，同 FarmlandSystem.RuralHarvest)
    public static float Workers(City city, int trade)
    {
        if (city == null) return 0f;
        (float living, float ruined) = RuralSettlementSystem.Weights(city);
        return CityPopulationSystem.BackgroundHouseholds(city) * Shares(city, trade) / (CityWeight + living + ruined);
    }

    // 林场、矿、牧的岗位(农、渔的岗位记在农田岗位里，见 FarmlandSystem.RuralHarvest)
    public static float Jobs(City city) =>
        Active ? JobsPerWorker * (Workers(city, Forest) + Workers(city, Mine) + Workers(city, Pasture)) : 0f;

    // 渔村的鱼(由人口经济按经过的年数调用，见 PopulationEconomySystem.Settle)
    public static void Produce(City city, CityPopulationData data, float years)
    {
        if (!Active || city == null || data == null || years <= 0f) return;
        float fish = Workers(city, Fish) * FishYield * FarmlandSystem.FishProductivity(city) * years;
        if (fish > 0f && AssetManager.resources?.get("fish") != null) PopulationEconomySystem.Deposit(city, data, "fish", fish);
    }

    // ---- 官营民营 ----

    // 本城的产业：林场、矿、牧的镇村按份数，厂区里的厂按等级；官营的占几份(见 EnterpriseSystem.TakePublicProfit)
    public static void CountEnterprises(City city, out int total, out int publicCount)
    {
        total = publicCount = 0;
        if (city == null) return;
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
        {
            if (settlement.ruined_at >= 0d || !IsEnterprise(Of(settlement))) continue;
            int weight = Mathf.RoundToInt(RuralSettlementSystem.Weight(settlement));
            total += weight;
            if (settlement.state) publicCount += weight;
        }
        foreach (FactorySite site in FactorySystem.InCity(city))
            foreach (FactoryUnit unit in site.units)
            {
                int level = Math.Max(1, unit.level);
                total += level;
                if (site.state) publicCount += level;
            }
    }

    // ---- 小窗口 ----

    // 这处镇村一年的出产(大约)
    public static string Describe(RuralSettlement settlement, City city)
    {
        int trade = Of(settlement);
        if (trade < 0 || city == null) return "";
        if (settlement.ruined_at >= 0d) return LM.Get("livelihood_output_ruined");
        (float living, float ruined) = RuralSettlementSystem.Weights(city);
        float weight = RuralSettlementSystem.Weight(settlement);
        float households = CityPopulationSystem.BackgroundHouseholds(city) * weight / (CityWeight + living + ruined);
        float productivity = FarmlandSystem.Productivity(city);
        float equivalent = weight * PerShare;
        switch (trade)
        {
            case Farm:
                return string.Format(LM.Get("livelihood_output_farm"), Mathf.RoundToInt(households * FarmlandSystem.RuralYield * productivity));
            case Fish:
                return string.Format(LM.Get("livelihood_output_fish"), Mathf.RoundToInt(households * FishYield * FarmlandSystem.FishProductivity(city)));
            case Forest:
            {
                float factor = equivalent * IndustryBuildingSystem.TierFactor(Level(city, Forest)) * IndustryBuildingSystem.FactoryBonus(city);
                return string.Format(LM.Get("livelihood_output_forest"), Mathf.RoundToInt(IndustryBuildingSystem.WoodPerFactorYear * factor));
            }
            case Mine:
            {
                float factor = equivalent * IndustryBuildingSystem.TierFactor(Level(city, Mine)) * IndustryBuildingSystem.FactoryBonus(city);
                return string.Format(LM.Get("livelihood_output_mine"), Mathf.RoundToInt(6f * factor), Mathf.RoundToInt(4f * factor),
                    Mathf.RoundToInt(factor));
            }
            default:
                return string.Format(LM.Get("livelihood_output_pasture"),
                    Mathf.RoundToInt(equivalent * AnimalHusbandrySystem.HerdPerPasture * (AnimalHusbandrySystem.IsNomadic(city.kingdom) ? 2f : 1f) *
                                     (0.4f + 1.2f * AnimalHusbandrySystem.Rangeland(city))));
        }
    }

    // ---- 纯净地图下不再建的原版建筑 ----

    public static bool Replaces(BuildingAsset asset) =>
        Active && asset != null && (IndustryBuildingSystem.IsMine(asset) || IndustryBuildingSystem.IsLumber(asset) ||
                                    asset.type == AnimalHusbandrySystem.PastureType || asset.type == AnimalHusbandrySystem.SlaughterhouseType);
}
