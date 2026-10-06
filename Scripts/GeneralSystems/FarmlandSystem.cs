using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 农田(无小人模式下自动耕作)：
//   - 耕地：风车周围的可耕地(原版农田范围)，加上城市的"规划农田区"(不受风车范围限制)；
//   - 自动耕作：每月替城市把可耕地开成田、给空田播种麦子、收割长熟的麦子(地图上照样看得到田和庄稼)，
//     粮食来自实际收割的麦子，每株产量 × 农业生产力；
//   - 规划农田区：神力"规划农田"点选区块划入/取消；缺粮或低于耕地红线时 AI 自动补划，已划定的不收回；
//   - 耕地红线(宪法条款，见 ConstitutionFarmland)：按生产力 = 每城至少 25% ÷ 农业生产力的区块是农田(古代 25%，
//     现代约 8%)；严守红线 = 固定 25%；不设红线 = 只在缺粮时规划，且规划区不受保护(可以在上面盖房)。
//     有红线时规划农田区受保护，城市不在上面盖建筑(见 NoCommonersPatch.AfterPlanAllowsBuilding)。
// 农业生产力：古代 1，现代 3。
public static class FarmlandSystem
{
    private const float RedLineBase = 0.25f;
    // 庄稼隔行种(见 IsCropSpot)，每株折算两格田的产量
    private const float FoodPerCrop = 4f;
    // AI 自动补划的规划农田区每城最多这么多块(地图上每株庄稼都是一个建筑，大城不设上限会有上千株)；
    // 神力手动划定的不受此限
    private const int MaxAutoPlannedZones = 6;
    private const int MaxFieldsPerSettle = 8;
    private const int MaxPlantsPerSettle = 16;
    private const int MaxHarvestsPerSettle = 40;
    // 一个区块至少有这么多可耕地才值得划为农田
    private const int MinFarmableTilesInZone = 8;

    public static float Productivity(City city) => ModernStability.IsModern(city?.kingdom) ? 3f : 1f;

    public static ConstitutionFarmland Policy(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire == null ? ConstitutionFarmland.Adaptive
            : ConstitutionSystem.GetClauses(empire)?.farmland ?? ConstitutionFarmland.Adaptive;
    }

    // 耕地红线：本城至少多大比例的区块是规划农田
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

    public static bool IsPlanned(City city, TileZone zone) =>
        city?.data != null && zone != null && zone.city == city && Zones(city).Contains(zone.id);

    // 划入/取消规划农田区，返回划入后的状态
    public static bool TogglePlanned(City city, TileZone zone)
    {
        if (city?.data == null || zone == null || zone.city != city) return false;
        List<int> zones = Zones(city);
        if (zones.Remove(zone.id)) return false;
        zones.Add(zone.id);
        return true;
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
            EnsureRedLine(city, data);
            food = Harvest(city);
            Plant(city);
            foreach (TileZone zone in PlannedZones(city)) CultivateZone(city, zone, MaxTilesPerSettle);
            MakeVanillaFields(city);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][农田] 结算失败({city.data?.name}): {exception.Message}");
        }
        return food;
    }

    // ---- 成片耕作：田块整齐、同季播种、同时收割 ----
    // 按区块整块经营：区块里的田大部分空着才整块一起播种，庄稼同时生长；全部成熟才整块一起收割。
    // 开田按区块内的局部坐标排成方田：第 0 行、第 0 列留作地头(相邻两块规划区之间就有一条小路)，
    // 每隔 FurrowEvery 列留一条田埂，形成几条整齐的长条田
    private const int FurrowEvery = 4;
    private const float PlantWhenEmptyShare = 0.8f;
    private const int MaxTilesPerSettle = 256;

    private static Dictionary<TileZone, List<WorldTile>> FieldsByZone(City city)
    {
        var result = new Dictionary<TileZone, List<WorldTile>>();
        foreach (WorldTile tile in FarmTiles(city))
        {
            if (tile?.zone == null) continue;
            if (!result.TryGetValue(tile.zone, out List<WorldTile> list)) result[tile.zone] = list = new List<WorldTile>();
            if (!list.Contains(tile)) list.Add(tile);
        }
        return result;
    }

    private static float Harvest(City city)
    {
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
        return food;
    }

    private static void Plant(City city)
    {
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

    // 区块内的局部坐标(相对区块左下角)
    private static (int x, int y) ZoneOrigin(TileZone zone)
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile == null) continue;
            if (tile.x < minX) minX = tile.x;
            if (tile.y < minY) minY = tile.y;
        }
        return (minX, minY);
    }

    // 庄稼只种在区块内的奇数行(偶数行是空着的田垄)
    private static readonly Dictionary<TileZone, (int x, int y)> _origins = new();

    private static bool IsCropSpot(WorldTile tile, TileZone zone)
    {
        if (zone?.tiles == null) return true;
        if (!_origins.TryGetValue(zone, out (int x, int y) origin)) _origins[zone] = origin = ZoneOrigin(zone);
        return (tile.y - origin.y) % 2 == 1;
    }

    private static bool IsFieldSpot(WorldTile tile, (int x, int y) origin)
    {
        int lx = tile.x - origin.x, ly = tile.y - origin.y;
        return ly != 0 && lx % FurrowEvery != 0;
    }

    // 规划农田区：按方田的格局把可耕地开成田(划定时也会立即开出第一批，地图上马上看得到)
    public static int CultivateZone(City city, TileZone zone, int limit)
    {
        if (zone?.tiles == null || zone.city != city) return 0;
        (int x, int y) origin = ZoneOrigin(zone);
        int count = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            if (count >= limit) break;
            if (tile == null || !IsFieldSpot(tile, origin) || !CanMakeField(tile)) continue;
            MapAction.terraformTop(tile, TopTileLibrary.field);
            count++;
        }
        return count;
    }

    // 风车周围原版算好的可耕地，同样按方田的格局开
    private static void MakeVanillaFields(City city)
    {
        int count = 0;
        var origins = new Dictionary<TileZone, (int x, int y)>();
        foreach (WorldTile tile in city.calculated_place_for_farms)
        {
            if (count >= MaxFieldsPerSettle) break;
            if (tile?.zone?.tiles == null || !CanMakeField(tile)) continue;
            if (!origins.TryGetValue(tile.zone, out (int x, int y) origin)) origins[tile.zone] = origin = ZoneOrigin(tile.zone);
            if (!IsFieldSpot(tile, origin)) continue;
            MapAction.terraformTop(tile, TopTileLibrary.field);
            count++;
        }
    }

    private static bool CanMakeField(WorldTile tile) =>
        tile?.Type != null && tile.Type.can_be_farm && !tile.Type.farm_field &&
        (!tile.hasBuilding() || tile.building.canRemoveForFarms());

    // 本城的农田地块：风车周围原版算好的农田 + 规划农田区里已经开成田的地块
    private static IEnumerable<WorldTile> FarmTiles(City city)
    {
        foreach (WorldTile tile in city.calculated_farm_fields)
            if (tile != null) yield return tile;
        foreach (TileZone zone in PlannedZones(city))
        {
            if (zone.tiles == null) continue;
            foreach (WorldTile tile in zone.tiles)
                if (tile?.Type != null && tile.Type.farm_field) yield return tile;
        }
    }

    // 耕地红线：规划农田低于红线，或者缺粮时，自动补划一块(每月最多一块)；已划定的不收回
    private static void EnsureRedLine(City city, CityPopulationData data)
    {
        if (city.zones == null || city.zones.Count == 0) return;
        int planned = 0;
        foreach (TileZone _ in PlannedZones(city)) planned++;
        int required = Mathf.Min(MaxAutoPlannedZones, Mathf.CeilToInt(city.zones.Count * RedLineShare(city)));
        bool hungry = data != null && data.last_food_shortage > 0f;
        if (planned >= required && !(hungry && planned < MaxAutoPlannedZones && planned * 2 < city.zones.Count)) return;
        TileZone best = null;
        int bestScore = MinFarmableTilesInZone - 1;
        WorldTile center = city.getTile();
        foreach (TileZone zone in city.zones)
        {
            if (zone?.tiles == null || IsPlanned(city, zone) || center != null && center.zone == zone) continue;
            int score = 0;
            foreach (WorldTile tile in zone.tiles)
                if (tile?.Type != null && (tile.Type.farm_field || CanMakeField(tile))) score++;
            if (score <= bestScore) continue;
            bestScore = score;
            best = zone;
        }
        if (best != null) Zones(city).Add(best.id);
    }
}
