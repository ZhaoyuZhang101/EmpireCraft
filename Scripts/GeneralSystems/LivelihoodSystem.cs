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
// 生计按村镇四周的地形定(山多为矿、林多为林、草原或游牧为牧、靠海为渔，其余务农)，本城务农的不到四成时能耕的地先务农，
// 本城有矿藏却还没有矿村时先开一处矿；定了存进存档(RuralSettlement.trade)，小窗口里可以手动改。
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

    // 这处镇村的生计(关城没有，-1)；还没定的按地形定下、存进存档
    public static int Of(RuralSettlement settlement)
    {
        if (settlement == null || settlement.fort) return -1;
        if (settlement.trade >= 0 && settlement.trade < Count) return settlement.trade;
        settlement.trade = Classify(settlement);
        return settlement.trade;
    }

    public static int At(int x, int y) => Of(RuralSettlementSystem.At(x, y));

    private static int Classify(RuralSettlement settlement)
    {
        City city = RuralSettlementSystem.CityOf(settlement);
        int land = 0, rough = 0, forest = 0, steppe = 0, arable = 0, sea = 0;
        for (int dy = -Radius; dy <= Radius; dy++)
        for (int dx = -Radius; dx <= Radius; dx++)
        {
            if (dx * dx + dy * dy > Radius * Radius) continue;
            WorldTile tile = World.world?.GetTile(settlement.x + dx, settlement.y + dy);
            TileTypeBase type = tile?.Type;
            if (type == null) continue;
            if (type.liquid || type.ocean)
            {
                if (type.ocean) sea++;
                continue;
            }
            land++;
            string biome = type.biome_id ?? "";
            if (type.mountains || type.rocks || type.block || type.id == "hills" || biome is "biome_hill" or "biome_rocklands") rough++;
            if (biome is "biome_birch" or "biome_maple" or "biome_forest" or "biome_pine" or "biome_enchanted" or "biome_jungle") forest++;
            if (biome is "biome_desert" or "biome_sand" or "biome_savanna") steppe++;
            if (type.can_be_farm || type.farm_field) arable++;
        }
        float total = Mathf.Max(1, land);
        float r = rough / total, f = forest / total, st = steppe / total, ar = arable / total;
        float w = sea / (float)Mathf.Max(1, sea + land);
        bool needFood = FoodShare(city, settlement) < 0.4f;
        bool needMine = city != null && MineralResourceSystem.HasAnyDeposit(city) && !HasTrade(city, Mine, settlement);
        if (r >= 0.4f) return Mine;
        if (needFood && ar >= 0.5f) return Farm;
        // 本城有矿藏(金银铜……)却还没有矿村：先开一处，不然矿藏白放着
        if (needMine) return Mine;
        if (w >= 0.3f) return Fish;
        if (f >= 0.45f) return Forest;
        if (st >= 0.5f || AnimalHusbandrySystem.IsNomadic(city?.kingdom)) return Pasture;
        if (r >= 0.25f) return Mine;
        return Farm;
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

    // 牧场、屠宰场折几座(见 AnimalHusbandrySystem)：游牧城照原来三座牧场；研究了制革，牧村、林场村的一半折成屠宰场(宰杀、狩猎)
    public static float Pastures(City city) =>
        AnimalHusbandrySystem.IsNomadic(city?.kingdom) ? Mathf.Max(NomadPastures, Equivalent(city, Pasture)) : Equivalent(city, Pasture);

    public static float Slaughterhouses(City city)
    {
        if (city == null || !TechnologySystem.CanBuild(city, "ec_slaughterhouse")) return 0f;
        float game = (Shares(city, Pasture) + Shares(city, Forest)) * PerShare * 0.5f;
        return AnimalHusbandrySystem.IsNomadic(city.kingdom) ? Mathf.Max(1f, game) : game;
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
