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
//   - 耕地：与原版一致，只在本城风车周围半径 9 格内；规划和缺粮都不能扩大这个范围。
//   - 自动耕作：每月替城市把可耕地开成田、给空田播种麦子、收割长熟的麦子(地图上照样看得到田和庄稼)，
//     粮食来自实际收割的麦子，每株产量 × 农业生产力；
//   - 规划农田区：只能划入风车范围内的可耕区；旧规划区超出范围的田地分批退耕。
//   - 耕地红线(宪法条款，见 ConstitutionFarmland)：规划目标为城市区块的 25% ÷ 农业生产力(现代约 8%)，
//     严守红线目标为 25%；目标受风车可耕范围限制。不设红线只在缺粮时规划，住房仍须为规划农田让位。
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
    private const int WindmillRangeSquared = 81;
    private const int MaxRetiredFieldsPerSettle = 64;
    // 一个区块至少有这么多可耕地才值得划为农田
    private const int MinFarmableTilesInZone = 8;

    public static float Productivity(City city) => ModernStability.IsModern(city?.kingdom) ? 3f : 1f;

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
        _ => RedLineBase / Productivity(city) * ZonePlanSystem.FarmFactor(city?.kingdom)
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

    // 原版 CityBehCheckFarms 使用本城的 type_windmill，距离平方不超过 81。
    public static bool IsWithinWindmillRange(City city, WorldTile tile) =>
        InRange(city, tile, city?.getBuildingOfType("type_windmill")?.current_tile);

    private static bool InRange(City city, WorldTile tile, WorldTile windmill)
    {
        if (city == null || tile?.zone?.city != city || windmill == null) return false;
        int dx = tile.x - windmill.x, dy = tile.y - windmill.y;
        return dx * dx + dy * dy <= WindmillRangeSquared;
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
        ZonePlanSystem.OnFarmPlanned(city, zone);
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
            if (best != null) MapAction.terraformTop(tile, best);
            else MapAction.terraformTile(tile, tile.main_type, null);
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
                ConstrainPlannedFields(city);
                foreach (TileZone zone in PlannedZones(city)) ZonePlanSystem.ClearHousing(city, zone, ZoneUse.Farm);
                break;
            case 1: CityBehCheckFarms.check(city); EnsureRedLine(city, data); break;
            case 2: return Harvest(city);
            case 3: Plant(city); break;
            case 4:
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

    // 隔行播种，保持原有每株折算两格田的产量；不缓存可被原版复用的 TileZone。
    private static bool IsCropSpot(WorldTile tile, TileZone zone) => (tile.y & 1) == 1;

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
            MapAction.terraformTop(tile, TopTileLibrary.field);
            count++;
        }
        return count;
    }

    // 原版算好的可耕地也再次核对范围，避免风车拆掉后使用过期缓存。
    private static void MakeVanillaFields(City city)
    {
        int count = 0;
        WorldTile windmill = city.getBuildingOfType("type_windmill")?.current_tile;
        foreach (WorldTile tile in city.calculated_place_for_farms)
        {
            if (count >= MaxFieldsPerSettle) break;
            if (!InRange(city, tile, windmill) || !CanFarmZone(city, tile.zone) || !CanMakeField(tile) || IsRetired(city, tile.zone) ||
                ScorchedEarthSystem.IsOccupiedZone(city, tile.zone)) continue;
            MapAction.terraformTop(tile, TopTileLibrary.field);
            count++;
        }
    }

    private static bool CanMakeField(WorldTile tile) =>
        tile?.Type != null && tile.Type.can_be_farm && !tile.Type.farm_field &&
        (!tile.hasBuilding() || tile.building.canRemoveForFarms());

    // 规划时住房可以让位；真正开田仍要等原版拆除释放地块。
    private static bool CanPlanField(WorldTile tile) => CanMakeField(tile) ||
        tile?.Type != null && tile.Type.can_be_farm && ZonePlanSystem.IsHousing(tile.building?.asset);

    private static bool CanFarmZone(City city, TileZone zone)
    {
        ZoneUse use = ZonePlanSystem.Get(city, zone);
        return use == ZoneUse.None || use == ZoneUse.Farm || use == ZoneUse.Residential;
    }

    // 无论农田来自原版还是规划，都核对风车范围和归属。
    private static IEnumerable<WorldTile> FarmTiles(City city)
    {
        WorldTile windmill = city.getBuildingOfType("type_windmill")?.current_tile;
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
        if (city.zones == null || city.zones.Count == 0) return;
        int planned = 0;
        foreach (TileZone zone in PlannedZones(city))
            if (HasFarmArea(city, zone)) planned++;
        int required = Mathf.Min(MaxAutoPlannedZones, Mathf.CeilToInt(city.zones.Count * RedLineShare(city)));
        bool hungry = data != null && data.last_food_shortage > 0f;
        if (planned >= required && !(hungry && planned < MaxAutoPlannedZones && planned * 2 < city.zones.Count)) return;
        TileZone best = null;
        int bestScore = MinFarmableTilesInZone - 1;
        WorldTile center = city.getTile();
        WorldTile windmill = city.getBuildingOfType("type_windmill")?.current_tile;
        if (windmill == null) return;
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
