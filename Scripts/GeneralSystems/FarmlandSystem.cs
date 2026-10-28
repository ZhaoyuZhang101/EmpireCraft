using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 农田(无小人模式下自动耕作)：
//   - 耕地：只在本城风车周围一定半径内；基础 9 格(与原版一致)，城市每 12 块领土 +1 格、最多 16 格。
//   - 自动耕作：每月替城市把可耕地开成田、给空田播种麦子、收割长熟的麦子(地图上照样看得到田和庄稼)，
//     粮食来自实际收割的麦子，每株产量 × 农业生产力；
//   - 规划农田区：只能划入风车范围内的可耕区；旧规划区超出范围的田地分批退耕。
//   - 耕地红线(宪法条款，见 ConstitutionFarmland)：规划目标为城市区块的 25% ÷ 农业生产力(现代约 8%)，
//     严守红线目标为 25%；目标受风车可耕范围限制。不设红线只在缺粮时规划，住房仍须为规划农田让位。
//     有红线时规划农田区受保护，城市不在上面盖建筑(见 NoCommonersPatch.AfterPlanAllowsBuilding)。
// 农业生产力：古代 1，现代 3。
// 纯净地图模式(用户 2026-10-17："农田要少，至少和村镇的数量保持一定比例且在周围""农田产出的系统可以改一改，不用按照原版的来")：
//   · 田是村镇的田：城外每个村、镇都在四周种着几块条田(画法见 PureMapPatch.Detail 的 Fields、UrbanFabric)，
//     AI 不再划农耕区开田——以前缺粮时把一城至多一半的地块划成农耕区，粗图上一片片大条田，拉近看着像放大了的贴图；
//     旧存档里划的农耕区逐月退掉；
//   · 产粮看种田的人：本城背景户数里住在城外镇村的那几份(村 1、镇 3，见 RuralSettlementSystem)，加上城里约四分之一的人
//     在城郊种地，每户每年产 RuralYield × 农业生产力；毁于兵火的镇村不种田；不宜耕作的时代减半；
//     城刚建、还没有镇村时只靠城郊田与自给口粮，镇村多起来粮就宽裕。
public static class FarmlandSystem
{
    private const float RedLineBase = 0.25f;
    // 庄稼隔行种(见 IsCropSpot)，每株折算两格田的产量
    private const float FoodPerCrop = 4f;
    // AI 自动补划的规划农田区每城最多这么多块(地图上每株庄稼都是一个建筑，大城不设上限会有上千株)；
    // 神力手动划定的不受此限
    private const int MaxAutoPlannedZones = 6;
    private const int MaxFieldsPerSettle = 8;
    private const int WindmillRangeSquared = 81;
    // 农田半径随城市规模扩大(无小人模式的自动耕作)：基础 9 格，每 12 块领土 +1 格，最多 16 格。
    // 大城不再被固定半径卡死粮食上限；普通小人模式沿用原版的 9 格耕作逻辑
    private static int RangeSquared(City city)
    {
        int radius = 9 + global::System.Math.Min(7, (city?.zones?.Count ?? 0) / 12);
        return radius * radius;
    }
    private const int MaxRetiredFieldsPerSettle = 64;
    // 一个区块至少有这么多可耕地才值得划为农田
    private const int MinFarmableTilesInZone = 8;
    // 纯净地图：种田的每户每年产粮(户数尺度；每户每年吃 1、自给 0.5)、城里种城郊田的份数、玩家刷的农田每格每年产粮
    public const float RuralYield = 1.2f;
    private const float CitySuburbFarmShare = 0.25f;

    // 农具(用户 2026-10-17 "那就升级科技让产量多点或者农耕工具")：照本国采纳了的科技换代，田里与牧业的产量随之倍增——
    //   耒耜 1 → 青铜农具 1.2(青铜铸造) → 铁犁牛耕 1.5(冶铁) → 钢制农具 1.8(炼钢) → 新式农具 2.2(工业化)
    //   → 化肥农机 3(内燃机或石油化工)；渔船另按航海、工业科技换代：独木舟 1 → 近海帆船 1.3(沿岸领航)
    //   → 远洋渔船 1.6(远洋航海) → 蒸汽渔船 2.2(工业化) → 机动渔船 3(内燃机)。
    //   现代政体至少按 3 算(原来"现代农业"的倍数，与农具不叠加)；没开科技树的文化按第一级。
    private static readonly (string[] techs, float factor)[] FarmTools =
    {
        (Array.Empty<string>(), 1f), (new[] { "bronze_casting" }, 1.2f), (new[] { "ironworking" }, 1.5f),
        (new[] { "steelmaking" }, 1.8f), (new[] { "industrialization" }, 2.2f), (new[] { "motorization", "petrochemistry" }, 3f)
    };

    private static readonly (string[] techs, float factor)[] FishTools =
    {
        (Array.Empty<string>(), 1f), (new[] { "coastal_pilotage" }, 1.3f), (new[] { "ocean_navigation" }, 1.6f),
        (new[] { "industrialization" }, 2.2f), (new[] { "motorization" }, 3f)
    };

    private static bool Knows(City city, string tech)
    {
        string culture = TechnologySystem.GetCultureOf(city);
        if (!TechnologySystem.IsActiveFor(culture)) return false;
        return city.kingdom != null && !city.kingdom.isRekt() ? TechnologySystem.CanUseIn(city.kingdom, tech) : TechnologySystem.HasTech(culture, tech);
    }

    private static int TierOf((string[] techs, float factor)[] ladder, City city)
    {
        int tier = 1;
        if (city == null) return tier;
        for (int i = 1; i < ladder.Length; i++)
            if (Array.Exists(ladder[i].techs, tech => Knows(city, tech))) tier = i + 1;
        return tier;
    }

    public static int FarmToolTier(City city) => TierOf(FarmTools, city);
    public static int FishToolTier(City city) => TierOf(FishTools, city);
    private static float ModernFloor(City city) => ModernStability.IsModern(city?.kingdom) ? 3f : 1f;

    public static float Productivity(City city) => Mathf.Max(ModernFloor(city), FarmTools[FarmToolTier(city) - 1].factor);
    public static float FishProductivity(City city) => Mathf.Max(ModernFloor(city), FishTools[FishToolTier(city) - 1].factor);

    public static ConstitutionFarmland Policy(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire == null ? ConstitutionFarmland.Adaptive
            : ConstitutionSystem.GetClauses(empire)?.farmland ?? ConstitutionFarmland.Adaptive;
    }

    // 耕地红线的规划目标比例，实际面积不能超过风车范围。
    public static float RedLineShare(City city) => Policy(city?.kingdom) switch
    {
        ConstitutionFarmland.None => 0f,
        ConstitutionFarmland.Strict => RedLineBase,
        _ => RedLineBase / Productivity(city)
    };

    public static bool IsProtected(City city) => city != null && Policy(city.kingdom) != ConstitutionFarmland.None;

    private static List<int> Zones(City city)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.farm_zones ??= new List<int>();
        return data.farm_zones;
    }

    // 纯净地图的田只属于农村(见 LivelihoodSystem)，原来划的农耕区不再起作用(护田、条田、不盖民居都不算)
    public static bool IsPlanned(City city, TileZone zone) =>
        !PureMapMode.Enabled && city?.data != null && zone != null && zone.city == city && Zones(city).Contains(zone.id);

    // 原版 CityBehCheckFarms 使用本城的 type_windmill，距离平方不超过 81。
    public static bool IsWithinWindmillRange(City city, WorldTile tile) =>
        InRange(city, tile, city?.getBuildingOfType("type_windmill")?.current_tile);

    private static bool InRange(City city, WorldTile tile, WorldTile windmill)
    {
        if (city == null || tile?.zone?.city != city) return false;
        if (PureMapMode.Enabled) return true;
        if (windmill == null) return false;
        int dx = tile.x - windmill.x, dy = tile.y - windmill.y;
        return dx * dx + dy * dy <= RangeSquared(city);
    }

    public static bool HasFarmArea(City city, TileZone zone)
    {
        if (zone?.tiles == null || zone.city != city) return false;
        WorldTile windmill = city?.getBuildingOfType("type_windmill")?.current_tile;
        foreach (WorldTile tile in zone.tiles)
            if (InRange(city, tile, windmill) && tile.Type != null &&
                (tile.Type.farm_field || CanPlanField(tile))) return true;
        return false;
    }

    private static List<int> RetiredZones(City city)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.retired_farm_zones ??= new List<int>();
        return data.retired_farm_zones;
    }

    public static bool IsRetired(City city, TileZone zone) =>
        city?.data != null && zone != null && RetiredZones(city).Contains(zone.id);

    // 划入/退耕，返回划入后的状态。退耕：取消规划、拆掉庄稼、田地恢复成周围的草地(见 RetireZone)
    public static bool TogglePlanned(City city, TileZone zone)
    {
        if (city?.data == null || zone == null || zone.city != city) return false;
        List<int> zones = Zones(city);
        if (zones.Remove(zone.id))
        {
            RetireZone(zone);
            if (!RetiredZones(city).Contains(zone.id)) RetiredZones(city).Add(zone.id);
            return false;
        }
        if (!HasFarmArea(city, zone)) return false;
        zones.Add(zone.id);
        RetiredZones(city).Remove(zone.id);
        ClearHousing(city, zone);
        return true;
    }

    // 退耕还草：区块里的庄稼拆掉，田地换回相邻地块最常见的地表(草地、林地等)；周围也都是田就退成裸土
    public static int RetireZone(TileZone zone)
        => RetireFields(zone, null, int.MaxValue);

    private static int RetireFields(TileZone zone, Func<WorldTile, bool> filter, int limit)
    {
        if (zone?.tiles == null) return 0;
        int count = 0;
        var counts = new Dictionary<TopTileType, int>();
        foreach (WorldTile tile in zone.tiles)
        {
            if (count >= limit) break;
            if (tile?.Type == null || !tile.Type.farm_field || filter != null && !filter(tile)) continue;
            Building crop = tile.building;
            if (crop?.asset != null && crop.asset.wheat) crop.startDestroyBuilding();
            counts.Clear();
            TopTileType best = null;
            int bestCount = 0;
            if (tile.neighboursAll != null)
                foreach (WorldTile neighbour in tile.neighboursAll)
                {
                    TopTileType top = neighbour?.top_type;
                    if (top == null || top.farm_field) continue;
                    int n = counts.TryGetValue(top, out int c) ? c + 1 : 1;
                    counts[top] = n;
                    if (n > bestCount)
                    {
                        bestCount = n;
                        best = top;
                    }
                }
            // 不闪白点(见 QuietTerraform)
            if (best != null) MapAction.terraformTop(tile, best, QuietTerraform);
            else MapAction.terraformTile(tile, tile.main_type, null, QuietTerraform);
            count++;
        }
        return count;
    }

    // 只回收本模组记录的规划区，保留风车内的田和其他来源的远处田地。
    private static void ConstrainPlannedFields(City city)
    {
        List<int> ids = Zones(city);
        if (city.zones == null || ids.Count == 0) return;
        WorldTile windmill = city.getBuildingOfType("type_windmill")?.current_tile;
        int remaining = MaxRetiredFieldsPerSettle;
        var owned = new HashSet<int>();
        foreach (TileZone zone in city.zones)
        {
            if (zone?.city != city) continue;
            owned.Add(zone.id);
            if (!ids.Contains(zone.id) || zone.tiles == null) continue;
            remaining -= RetireFields(zone, tile => !InRange(city, tile, windmill), remaining);
            bool outsideFields = false;
            foreach (WorldTile tile in zone.tiles)
                if (tile?.Type != null && tile.Type.farm_field && !InRange(city, tile, windmill))
                {
                    outsideFields = true;
                    break;
                }
            // 尚未退完的区块留在存档记录里，下一月继续，不丢失清理进度。
            if (!outsideFields && !HasFarmArea(city, zone)) ids.Remove(zone.id);
        }
        ids.RemoveAll(id => !owned.Contains(id));
    }

    // 本城现在还归它的规划农田区
    public static IEnumerable<TileZone> PlannedZones(City city)
    {
        if (city?.zones == null) yield break;
        List<int> ids = Zones(city);
        if (ids.Count == 0) yield break;
        foreach (TileZone zone in city.zones)
            if (zone != null && ids.Contains(zone.id)) yield return zone;
    }

    // 每月结算：补划红线、收割、播种、开田。返回收获的粮食(户数尺度)
    public static float Settle(City city, CityPopulationData data)
    {
        if (city?.data == null || city.isRekt() || World.world == null) return 0f;
        float food = 0f;
        try
        {
            for (int phase = 0; phase < 5; phase++) food += SettlePhase(city, data, phase);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][农田] 结算失败({city.data?.name}): {exception.Message}");
        }
        return food;
    }

    // 月度队列逐步执行；收获量由调用者保存，最后经济结算时一次入库。
    public static float SettlePhase(City city, CityPopulationData data, int phase)
    {
        if (city?.data == null || city.isRekt() || World.world == null) return 0f;
        switch (phase)
        {
            case 0:
                // 纯净地图的田只属于农村(见 LivelihoodSystem)：旧档划的农耕区一并退掉，不再清民居、不再整田
                if (PureMapMode.Enabled)
                {
                    RetirePlannedZones(city);
                    break;
                }
                ConstrainPlannedFields(city);
                foreach (TileZone zone in PlannedZones(city)) ClearHousing(city, zone);
                break;
            case 1: CityBehCheckFarms.check(city); EnsureRedLine(city, data); break;
            case 2: return Harvest(city, data);
            case 3: Plant(city); break;
            case 4:
                if (!PureMapMode.Enabled)
                    foreach (TileZone zone in PlannedZones(city)) CultivateZone(city, zone, MaxTilesPerSettle);
                MakeVanillaFields(city);
                break;
        }
        return 0f;
    }

    // ---- 成片耕作：田块整齐、同季播种、同时收割 ----
    // 按区块整块经营：区块里的田大部分空着才整块一起播种，庄稼同时生长；全部成熟才整块一起收割。
    // 开田围绕风车，不再按城市区块铺出大片长条方田。
    private const float PlantWhenEmptyShare = 0.8f;
    private const int MaxTilesPerSettle = 256;

    private static Dictionary<TileZone, List<WorldTile>> FieldsByZone(City city)
    {
        var result = new Dictionary<TileZone, List<WorldTile>>();
        var seen = new HashSet<WorldTile>();
        foreach (WorldTile tile in FarmTiles(city))
        {
            if (tile?.zone == null || !seen.Add(tile)) continue;
            if (!result.TryGetValue(tile.zone, out List<WorldTile> list)) result[tile.zone] = list = new List<WorldTile>();
            list.Add(tile);
        }
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population != null) population.farm_jobs = seen.Count / 8f;
        return result;
    }

    private static float Harvest(City city, CityPopulationData data)
    {
        // 纯净地图：按种植位折算的是一个月的收成，乘上距上次经济结算过了几个月(吃粮也是这样按月数补算的；
        // 每座城的结算常常隔一年多才轮到一次，以前只算一个月，田实际只出了设计产量的二十分之一左右)
        if (PureMapMode.Enabled)
        {
            int months = data == null || data.last_economy < 0d ? 1
                : Mathf.Clamp(Date.getMonthsSince(data.last_economy), 1, 24);
            return RuralHarvest(city) * months;
        }
        float food = 0f;
        int count = 0;
        foreach (KeyValuePair<TileZone, List<WorldTile>> pair in FieldsByZone(city))
        {
            if (count >= MaxTilesPerSettle) break;
            if (ScorchedEarthSystem.IsOccupiedZone(city, pair.Key)) continue;
            // 整块的庄稼都熟了才一起收
            bool anyCrop = false, allRipe = true;
            foreach (WorldTile tile in pair.Value)
            {
                Building crop = tile.building;
                if (crop?.asset == null || !crop.asset.wheat) continue;
                anyCrop = true;
                if (crop.component_wheat == null || !crop.component_wheat.isMaxLevel())
                {
                    allRipe = false;
                    break;
                }
            }
            if (!anyCrop || !allRipe) continue;
            foreach (WorldTile tile in pair.Value)
            {
                Building crop = tile.building;
                if (crop?.asset == null || !crop.asset.wheat) continue;
                crop.extractResources(null);
                food += FoodPerCrop * Productivity(city);
                count++;
                // 旧存档里种在田垄行上的庄稼收完就拆掉，以后只种奇数行
                if (!IsCropSpot(tile, pair.Key)) crop.startDestroyBuilding();
            }
        }
        // 灾年减产(见 DisasterSystem)
        return food > 0f ? food * DisasterSystem.HarvestFactor(city) : food;
    }

    // 纯净地图：一个月的收成——住在完好农村里的农户、城郊种地的城里人(林场、矿、牧、渔村不种田，见 LivelihoodSystem；
    // 渔户的岗位也记在这里，鱼另出，见 LivelihoodSystem.Produce)
    private static float RuralHarvest(City city)
    {
        (float living, float ruined) = RuralSettlementSystem.Weights(city);
        const float cityWeight = 6f;
        float households = CityPopulationSystem.BackgroundHouseholds(city);
        float farms = LivelihoodSystem.Shares(city, LivelihoodSystem.Farm);
        float farmers = households * (farms + cityWeight * CitySuburbFarmShare) / (cityWeight + living + ruined);
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population != null) population.farm_jobs = (farmers + LivelihoodSystem.Workers(city, LivelihoodSystem.Fish)) * 0.6f;
        float food = farmers * RuralYield * Productivity(city) * ArableFactor(city);
        if (World.world_era != null && !World.world_era.flag_crops_grow) food *= 0.5f;
        // 灾年减产(见 DisasterSystem)
        food *= DisasterSystem.HarvestFactor(city);
        return food / 12f;
    }

    // 可耕地的多少：看田在哪——各完好农村所在的地块连同本城的四邻地块，加上城郊(城心地块连同四邻)；这些地里可耕格占到六成以上照常收，
    // 越少收得越少，最少三成(沙漠、荒原的城靠绿洲、河谷一点地)。以前按全城陆地算，城一扩张多了山林，所有农村的收成都被拉低
    // (用户 2026-10-28 转群友"三个农田村的城市只有个位数人口")
    private static float ArableFactor(City city)
    {
        if (city?.zones == null) return 1f;
        var fields = new HashSet<TileZone>();
        void Around(TileZone zone)
        {
            if (zone == null) return;
            fields.Add(zone);
            foreach (TileZone next in zone.neighbours ?? Array.Empty<TileZone>())
                if (next != null && next.city == city) fields.Add(next);
        }
        Around(city.getTile()?.zone);
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
            if (settlement.ruined_at < 0d && !settlement.fort && LivelihoodSystem.Of(settlement) == LivelihoodSystem.Farm)
                Around(World.world.GetTile(settlement.x, settlement.y)?.zone);
        int land = 0, arable = 0;
        foreach (TileZone zone in fields)
        {
            (int zoneLand, int zoneArable) = TerrainSystem.ArableCounts(zone);
            land += zoneLand;
            arable += zoneArable;
        }
        return land <= 0 ? 1f : Mathf.Clamp(arable / (land * 0.6f), 0.3f, 1f);
    }

    // 旧存档里划的农耕区(AI 缺粮时划的、原来规划笔刷刷的)一并退掉(纯净地图的田是农村的田)
    private static void RetirePlannedZones(City city)
    {
        foreach (TileZone zone in PlannedZones(city).ToList()) TogglePlanned(city, zone);
    }

    // 纯净地图模式没有庄稼：按田里的种植位折算，每个种植位一轮成熟收 FoodPerCrop 份，
    // 一轮按原版麦子的成熟时间(每长一级 growth_time 秒，一个月 5 秒)；不宜耕作的时代减半
    private static float VirtualHarvest(City city)
    {
        int spots = 0;
        foreach (KeyValuePair<TileZone, List<WorldTile>> pair in FieldsByZone(city))
        {
            if (ScorchedEarthSystem.IsOccupiedZone(city, pair.Key)) continue;
            foreach (WorldTile tile in pair.Value)
                if (tile.Type == TopTileLibrary.field && IsCropSpot(tile, pair.Key)) spots++;
        }
        if (spots == 0) return 0f;
        float food = spots * FoodPerCrop * Productivity(city) / CropCycleMonths();
        if (World.world_era != null && !World.world_era.flag_crops_grow) food *= 0.5f;
        // 灾年减产(见 DisasterSystem)
        return food * DisasterSystem.HarvestFactor(city);
    }

    private static float CropCycleMonths()
    {
        BuildingAsset wheat = AssetManager.buildings.get("wheat");
        int levels = Math.Max(1, (wheat?.building_sprites?.animation_data?.Count ?? 4) - 1);
        return Math.Max(1f, (wheat?.growth_time ?? 50f) * levels / 5f);
    }

    private static void Plant(City city)
    {
        if (PureMapMode.Enabled) return;
        int count = 0;
        foreach (KeyValuePair<TileZone, List<WorldTile>> pair in FieldsByZone(city))
        {
            if (count >= MaxTilesPerSettle) break;
            // 敌占区块不能耕种
            if (ScorchedEarthSystem.IsOccupiedZone(city, pair.Key)) continue;
            int empty = 0, fields = 0;
            foreach (WorldTile tile in pair.Value)
            {
                if (tile.Type != TopTileLibrary.field || !IsCropSpot(tile, pair.Key)) continue;
                fields++;
                if (!tile.hasBuilding()) empty++;
            }
            // 大部分田空着才整块一起播种，庄稼同时生长
            if (fields == 0 || empty < fields * PlantWhenEmptyShare) continue;
            foreach (WorldTile tile in pair.Value)
            {
                if (tile.Type != TopTileLibrary.field || tile.hasBuilding() || !IsCropSpot(tile, pair.Key)) continue;
                World.world.buildings.addBuilding("wheat", tile);
                count++;
            }
        }
    }

    // 隔行播种，保持原有每株折算两格田的产量；不缓存可被原版复用的 TileZone。
    private static bool IsCropSpot(WorldTile tile, TileZone zone) => (tile.y & 1) == 1;

    // 开田、退耕不闪白点：原版 terraformTop 默认按"闪一下"处理，每格变地表都闪一个白点；每月结算各城一齐开几百格田，
    // 满地农田一片白点(用户 2026-10-16 "有时候整个地图都会出现这个白色雪花""他们只出现在农田")
    private static TerraformOptions QuietTerraform => TerraformLibrary.nothing;

    // 规划区也只能在原版风车范围内开田。
    public static int CultivateZone(City city, TileZone zone, int limit)
    {
        if (zone?.tiles == null || zone.city != city || !CanFarmZone(city, zone) ||
            ScorchedEarthSystem.IsOccupiedZone(city, zone)) return 0;
        WorldTile windmill = city?.getBuildingOfType("type_windmill")?.current_tile;
        int count = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            if (count >= limit) break;
            if (!InRange(city, tile, windmill) || !CanMakeField(tile)) continue;
            MapAction.terraformTop(tile, TopTileLibrary.field, QuietTerraform);
            count++;
        }
        return count;
    }

    // 原版算好的可耕地也再次核对范围，避免风车拆掉后使用过期缓存。
    private static void MakeVanillaFields(City city)
    {
        if (PureMapMode.Enabled) return;
        int count = 0;
        WorldTile windmill = city.getBuildingOfType("type_windmill")?.current_tile;
        foreach (WorldTile tile in city.calculated_place_for_farms)
        {
            if (count >= MaxFieldsPerSettle) break;
            if (!InRange(city, tile, windmill) || !CanFarmZone(city, tile.zone) || !CanMakeField(tile) || IsRetired(city, tile.zone) ||
                ScorchedEarthSystem.IsOccupiedZone(city, tile.zone)) continue;
            MapAction.terraformTop(tile, TopTileLibrary.field, QuietTerraform);
            count++;
        }
    }

    // 图层城墙下的格不开田(见 MapWallSystem)
    private static bool CanMakeField(WorldTile tile) =>
        tile?.Type != null && tile.Type.can_be_farm && !tile.Type.farm_field && !MapWallSystem.Contains(tile) &&
        (!tile.hasBuilding() || tile.building.canRemoveForFarms());

    // 规划时住房可以让位；真正开田仍要等原版拆除释放地块。
    private static bool CanPlanField(WorldTile tile) => CanMakeField(tile) ||
        tile?.Type != null && tile.Type.can_be_farm && IsHousing(tile.building?.asset);

    // 功能区规划已撤(只剩耕地)：本城的地块都能耕
    private static bool CanFarmZone(City city, TileZone zone) => zone != null && zone.city == city;

    private static bool IsHousing(BuildingAsset asset) => asset != null && (asset.type == "type_house" || asset.hasHousingSlots());

    // 规划农田区里风车范围内的民居让位给田(只拆到剩下的住房仍够住为止；不拆城心与外国建筑)
    private static int ClearHousing(City city, TileZone zone)
    {
        if (zone?.city != city || zone.tiles == null || city?.status == null) return 0;
        int spare = city.status.housing_total - CityPopulationSystem.Households(city);
        if (spare <= 0) return 0;
        var houses = new HashSet<Building>();
        foreach (WorldTile tile in zone.tiles)
        {
            Building building = tile?.building;
            if (building?.asset == null || building.city != city || building.isOnRemove() || !IsHousing(building.asset) ||
                building.asset.id.StartsWith("city_")) continue;
            if (!IsWithinWindmillRange(city, tile) || tile.Type == null || !tile.Type.can_be_farm && !tile.Type.farm_field) continue;
            houses.Add(building);
        }
        int cleared = 0;
        foreach (Building house in houses)
        {
            if (spare - house.asset.housing_slots < 0) continue;
            spare -= house.asset.housing_slots;
            house.startDestroyBuilding();
            cleared++;
        }
        return cleared;
    }

    // 无论农田来自原版还是规划，都核对风车范围和归属。
    private static IEnumerable<WorldTile> FarmTiles(City city)
    {
        WorldTile windmill = city.getBuildingOfType("type_windmill")?.current_tile;
        if (!PureMapMode.Enabled)
            foreach (WorldTile tile in city.calculated_farm_fields)
                if (InRange(city, tile, windmill) && CanFarmZone(city, tile.zone) && tile.Type?.farm_field == true) yield return tile;
        foreach (TileZone zone in PlannedZones(city))
        {
            if (zone.tiles == null) continue;
            foreach (WorldTile tile in zone.tiles)
                if (InRange(city, tile, windmill) && tile.Type?.farm_field == true) yield return tile;
        }
    }

    // 红线或缺粮每月最多补划一块，候选区仍严格受风车范围限制。
    private static void EnsureRedLine(City city, CityPopulationData data)
    {
        // 纯净地图：田是村镇的田，AI 不划农耕区
        if (PureMapMode.Enabled || city.zones == null || city.zones.Count == 0) return;
        int planned = 0;
        foreach (TileZone zone in PlannedZones(city))
            if (HasFarmArea(city, zone)) planned++;
        int cap = PureMapMode.Enabled ? Mathf.Max(MaxAutoPlannedZones, city.zones.Count / 2) : MaxAutoPlannedZones;
        int required = Mathf.Min(cap, Mathf.CeilToInt(city.zones.Count * RedLineShare(city)));
        bool hungry = data != null && data.last_food_shortage > 0f;
        if (planned >= required && !(hungry && planned < cap && planned * 2 < city.zones.Count)) return;
        TileZone best = null;
        int bestScore = MinFarmableTilesInZone - 1;
        WorldTile center = city.getTile();
        WorldTile windmill = city.getBuildingOfType("type_windmill")?.current_tile;
        if (windmill == null && !PureMapMode.Enabled) return;
        foreach (TileZone zone in city.zones)
        {
            if (zone?.tiles == null || !CanFarmZone(city, zone) || IsPlanned(city, zone) || IsRetired(city, zone) ||
                center != null && center.zone == zone) continue;
            int score = 0;
            foreach (WorldTile tile in zone.tiles)
                if (InRange(city, tile, windmill) && tile.Type != null &&
                    (tile.Type.farm_field || CanPlanField(tile))) score++;
            if (score <= bestScore) continue;
            bestScore = score;
            best = zone;
        }
        if (best != null) TogglePlanned(city, best);
    }
}
