using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 手动城区(用户 2026-10-28 看了城池全图说"要实际改变实际领地的, 但是城市的领土是不会变得, 但是要显示出城市的实际面积形状,
// 只能在面积里面绘制, 修建的建筑都会改变实际人口也会影响城市功能的."；问答定"当场盖满或拆掉""加功能建筑工具")：
//   · 城一旦在城池全图里改过规划(见 CityPlanEditing)，就记一份城区地块表(CityExtraData.plan_urban)，头一回按当时画成城区的地块记
//     (连同以前手动并入、划出的结果)；城区 = 城里能盖房的地，整块画成城区；只在本城领地里，领土本身不变；
//     城以后长大不自己扩城区，交还规矩清掉回到自动；
//   · 每块城区地有固定的建筑位：照常 4、坊市密 6、疏 3(见 SlotsPerZone；平面图上怎么摆见 PureMapPatch.SlotIndex)；
//   · 神力式，不花钱：并入一块地，地上的村镇并进城里，当场盖满本城最高一级的民居(地形放不下就少盖)；划出一块地，地上的原版建筑当场拆掉；
//     对账(Sync)：城区外的民居拆掉、别的建筑挪进城区(挪不下拆掉)，城区里超出建筑位的民居拆掉，不满的补民居，城区里的村镇并进城里；
//   · 民居上限 = 城区建筑位 − 非民居建筑(不看富庶，由玩家定，见 HousesMax、NoCommonersPatch.AfterRecalculateMaxHouses)；
//     原版营建只在城区里还有空位的地块上放新建筑(见 AllowsBuilding)；人口上限随住房位走，同以前(见 CityPatch.getPopulationMaximum)；
//   · 功能建筑(市集、寺庙、书院、兵营、码头)：点城区里一块地当场盖一座本城建造单里科技许可的最高一级，不看钱和科技以外的限制
//     (用户 2026-10-20 "手动放置建筑时候不要有任何限制")；建筑位满了先拆一座民居腾位；「拆」拆掉这块地的功能建筑，空出的位补民居。
// 管的是城市建筑(Building_Civ，见 Managed)：纯装饰、就地生产的(矿场、伐木场、牧场、屠宰场)、仓库(货存在里面，拆了货就没了)、
// 合并民居的城池(见 EmpireCraftCityCombineHouses)不管，不占建筑位；城心的公所、篝火不拆，挪不下就留在原处。
// 拆建筑走原版 startRemove，地方要过一会儿才空出来：放不下的功能建筑等腾出地方再盖(Waits)，改完一秒后再对一次账(Later、Tick)
public static class CityZoningSystem
{
    public const int SlotsNormal = 4, SlotsDense = 6, SlotsSparse = 3;
    public const string HouseType = "type_house";
    // 拆了民居腾地方的功能建筑至多等多久(秒，真实时间)；改完多久再对一次账
    private const float WaitSeconds = 20f, SyncDelay = 1f;
    // 挪一座建筑至多试几块地；顺着升级链往上找至多几级
    private const int MoveTries = 8, MaxUpgradeSteps = 16;
    private static readonly HashSet<string> CoreTypes = new() { "type_hall", "type_bonfire" };

    // 功能建筑工具认的名字 → 原版建筑类型
    public static string TypeOf(string kind) => kind switch
    {
        "market" => "type_market",
        "temple" => "type_temple",
        "library" => "type_library",
        "barracks" => "type_barracks",
        "docks" => "type_docks",
        _ => null
    };

    #region 状态

    public static bool Manual(City city) => city?.data != null && !city.isRekt() && city.GetOrCreate().plan_urban != null;

    // 每块城区地的建筑位：照常 4、坊市密 6、疏 3
    public static int SlotsPerZone(City city) => city?.data == null ? SlotsNormal : SlotsPerZone(city.GetOrCreate());

    private static int SlotsPerZone(CityExtension.CityExtraData extra) =>
        extra.plan_blocks switch { 1 => SlotsDense, 2 => SlotsSparse, _ => SlotsNormal };

    // 本城的城区地块：表里归本城的(丢了的地不算，回来了又算)，连同城心那块；自动的城为 null
    public static HashSet<int> UrbanSet(City city)
    {
        List<int> list = city?.data != null && !city.isRekt() ? city.GetOrCreate().plan_urban : null;
        if (list == null) return null;
        var set = new HashSet<int>();
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc != null)
            foreach (int id in list)
                if (calc.getZoneByID(id) is TileZone zone && zone.city == city) set.Add(id);
        TileZone center = city.getTile()?.zone;
        if (center != null && center.city == city) set.Add(center.id);
        return set;
    }

    public static bool InUrban(City city, TileZone zone) => city?.data != null && InUrban(city, zone, city.GetOrCreate());

    private static bool InUrban(City city, TileZone zone, CityExtension.CityExtraData extra) =>
        zone != null && zone.city == city && extra.plan_urban != null && (extra.plan_urban.Contains(zone.id) || zone == city.getTile()?.zone);

    // 管不管这座建筑(占建筑位、城区外要挪要拆)
    public static bool Managed(Building building)
    {
        BuildingAsset asset = building?.asset;
        if (asset == null || building.isRekt() || building.isOnRemove() || building.isRuin() || building.current_tile?.zone == null)
            return false;
        if (asset.building_type != BuildingType.Building_Civ || PureMapMode.IsDecor(asset)) return false;
        string type = asset.type ?? "";
        return !IndustryBuildingSystem.IsMine(asset) && !IndustryBuildingSystem.IsLumber(asset) && type != AnimalHusbandrySystem.PastureType &&
               type != AnimalHusbandrySystem.SlaughterhouseType && type != "type_stockpile" && asset.id?.Contains("city_") != true;
    }

    public static bool IsHouse(BuildingAsset asset) => asset?.type == HouseType;

    private static bool IsCore(BuildingAsset asset) => asset?.type != null && CoreTypes.Contains(asset.type);

    // 这块地上本城管的建筑
    public static List<Building> InZone(City city, TileZone zone)
    {
        var result = new List<Building>();
        if (city?.buildings == null || zone == null) return result;
        foreach (Building building in city.buildings)
            if (Managed(building) && building.current_tile.zone == zone) result.Add(building);
        return result;
    }

    // 手动城区的民居上限：城区建筑位 − 非民居建筑；自动的城为 -1
    public static int HousesMax(City city)
    {
        HashSet<int> urban = UrbanSet(city);
        if (urban == null || city.buildings == null) return -1;
        int others = 0;
        foreach (Building building in city.buildings)
            if (Managed(building) && !IsHouse(building.asset)) others++;
        return Mathf.Max(0, urban.Count * SlotsPerZone(city) - others);
    }

    // 民居座数(不算正在拆的)
    public static int Houses(City city)
    {
        int count = 0;
        if (city?.buildings == null) return 0;
        foreach (Building building in city.buildings)
            if (Managed(building) && IsHouse(building.asset)) count++;
        return count;
    }

    // 原版营建(见 NoCommonersPatch.AfterPlanAllowsBuilding)：手动城区的城只在城区里、还有空位的地块上放新建筑。
    // 原版找地方时一块块地问，各地块占了几个位每帧按城数一次
    public static bool AllowsBuilding(City city, TileZone zone)
    {
        if (city?.data == null || zone == null) return true;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (extra.plan_urban == null) return true;
        if (!InUrban(city, zone, extra)) return false;
        return OccupiedIn(city, zone.id) + Reserved(city, zone.id) < SlotsPerZone(extra);
    }

    private static int _occupiedFrame = -1;
    private static readonly Dictionary<City, Dictionary<int, int>> Occupied = new();

    private static int OccupiedIn(City city, int zoneId)
    {
        int frame = Time.frameCount;
        if (frame != _occupiedFrame)
        {
            _occupiedFrame = frame;
            Occupied.Clear();
        }
        if (!Occupied.TryGetValue(city, out Dictionary<int, int> counts))
        {
            counts = new Dictionary<int, int>();
            foreach (Building building in city.buildings)
            {
                if (!Managed(building)) continue;
                int id = building.current_tile.zone.id;
                counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
            }
            Occupied[city] = counts;
        }
        return counts.TryGetValue(zoneId, out int count) ? count : 0;
    }

    #endregion

    #region 建筑

    private static int _assetFrame = -1;
    private static readonly Dictionary<(City, string), BuildingAsset> AssetCache = new();

    // 本城种族建造单里这一类建筑、本文化科技许可的最高一级(顺着升级链往上找，见 TechnologySystem.CanBuild)；没有为 null。
    // 界面高亮一帧里会问很多块地，每帧每城每类只找一次
    public static BuildingAsset BestAsset(City city, string type)
    {
        if (city?.data == null || string.IsNullOrEmpty(type)) return null;
        int frame = Time.frameCount;
        if (frame != _assetFrame)
        {
            _assetFrame = frame;
            AssetCache.Clear();
        }
        if (AssetCache.TryGetValue((city, type), out BuildingAsset cached)) return cached;
        BuildingAsset best = null;
        CityBuildOrderAsset orders = AssetManager.city_build_orders.get(city.getActorAsset()?.build_order_template_id ?? "");
        if (orders?.list != null)
            foreach (BuildOrder order in orders.list)
            {
                BuildingAsset asset = order?.getBuildingAsset(city);
                for (int step = 0; asset != null && step < MaxUpgradeSteps; step++)
                {
                    if (asset.type != type || asset.id == null || asset.id.Contains("city_")) break;
                    if ((best == null || asset.upgrade_level > best.upgrade_level) && TechnologySystem.CanBuild(city, asset.id)) best = asset;
                    asset = string.IsNullOrEmpty(asset.upgrade_to) ? null : AssetManager.buildings.get(asset.upgrade_to);
                }
            }
        AssetCache[(city, type)] = best;
        return best;
    }

    // 这块地有能停船的水(码头)
    public static bool HasWater(TileZone zone)
    {
        if (zone?.tiles == null) return false;
        foreach (WorldTile tile in zone.tiles)
            if (tile?.Type != null && tile.Type.ocean) return true;
        return false;
    }

    private static bool Fits(City city, WorldTile tile, BuildingAsset asset) =>
        tile?.Type != null && tile.canBuildOn(asset) && World.world.buildings.canBuildFrom(tile, asset, city);

    // 这块地里头一处放得下的格(同迁城搬篝火，见 CityRelocationSystem.MoveBonfire)
    private static WorldTile FindSpot(City city, TileZone zone, BuildingAsset asset)
    {
        if (zone?.tiles == null || asset == null) return null;
        foreach (WorldTile tile in zone.tiles)
            if (Fits(city, tile, asset)) return tile;
        return null;
    }

    // 当场盖一座(放下就建成，同繁荣神力推进施工的写法，见 CityConstructionSystem.Advance)
    private static Building Put(City city, BuildingAsset asset, WorldTile tile)
    {
        Building building = World.world.buildings.addBuilding(asset, tile);
        if (building?.asset == null) return null;
        if (building.isUnderConstruction())
        {
            building.updateBuild(Mathf.Max(1, building.asset.construction_progress_needed + 1 - building.getConstructionProgress()));
            if (city.under_construction_building == building) city.under_construction_building = null;
        }
        return building;
    }

    // 在这块地上盖民居，盖到 want 座或放不下为止；盖起的记进 here。一块地只顺着扫一遍(放下一座，前面放不下的格往后也放不下)。
    // 纯沙漠、纯冻土住不了人，盖了也会被拆(见 BarrenLandSystem)，不盖
    private static int FillHouses(City city, TileZone zone, BuildingAsset house, int want, List<Building> here)
    {
        if (want <= 0 || house == null || zone?.tiles == null || BarrenLandSystem.Barren(zone)) return 0;
        int built = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            if (built >= want) break;
            if (!Fits(city, tile, house) || Put(city, house, tile) is not Building fresh) continue;
            here?.Add(fresh);
            built++;
        }
        return built;
    }

    // 拆这块地上等级最低的一座民居
    private static bool RemoveHouse(List<Building> here)
    {
        Building lowest = null;
        foreach (Building building in here)
            if (IsHouse(building.asset) && !building.isOnRemove() && (lowest == null || building.asset.upgrade_level < lowest.asset.upgrade_level))
                lowest = building;
        if (lowest == null) return false;
        lowest.startRemove();
        here.Remove(lowest);
        return true;
    }

    private static bool HasHouse(List<Building> here)
    {
        foreach (Building building in here)
            if (IsHouse(building.asset)) return true;
        return false;
    }

    private static List<Building> Here(Dictionary<int, List<Building>> byZone, int zoneId)
    {
        if (!byZone.TryGetValue(zoneId, out List<Building> list)) byZone[zoneId] = list = new List<Building>();
        return list;
    }

    // 本城管的建筑按地块分：城区里的记进各地块，本城城区外的另列(别城地上的不动)
    private static Dictionary<int, List<Building>> ByZone(City city, HashSet<int> urban, out List<Building> outside)
    {
        var byZone = new Dictionary<int, List<Building>>();
        outside = new List<Building>();
        foreach (Building building in new List<Building>(city.buildings))
        {
            if (!Managed(building)) continue;
            TileZone zone = building.current_tile.zone;
            if (zone.city != city) continue;
            if (urban.Contains(zone.id)) Here(byZone, zone.id).Add(building);
            else outside.Add(building);
        }
        return byZone;
    }

    // 挪进城区(同迁城搬篝火：新地方盖一座一样的、拆旧的)：离它最近、还有空位的城区地块放得下就放；
    // 都满了就找放得下、又拆得出一座民居的地块，先放下再拆那座民居；纯沙漠、纯冻土不挪过去；挪不下返回 false
    private static bool Move(City city, Building building, List<TileZone> zones, Dictionary<int, List<Building>> byZone, int cap)
    {
        TileZone from = building.current_tile?.zone;
        BuildingAsset asset = building.asset;
        if (from == null || asset == null || zones.Count == 0) return false;
        var order = new List<TileZone>(zones);
        order.Sort((a, b) => Distance(a, from).CompareTo(Distance(b, from)));
        for (int pass = 0; pass < 2; pass++)
        {
            int tries = 0;
            foreach (TileZone zone in order)
            {
                if (tries >= MoveTries) break;
                if (zone == from || BarrenLandSystem.Barren(zone)) continue;
                List<Building> here = Here(byZone, zone.id);
                if (pass == 0 ? here.Count + Reserved(city, zone.id) >= cap : !HasHouse(here)) continue;
                tries++;
                WorldTile spot = FindSpot(city, zone, asset);
                if (spot == null || Put(city, asset, spot) is not Building fresh) continue;
                if (pass == 1) RemoveHouse(here);
                here.Add(fresh);
                building.startRemove();
                return true;
            }
        }
        return false;
    }

    private static int Distance(TileZone a, TileZone b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

    // 这块地上的村镇(关城除外)并进城里(同迁城，见 RuralSettlementSystem.Absorb)，记下名字
    private static void Absorb(TileZone zone, ref List<string> names)
    {
        List<RuralSettlement> here = RuralSettlementSystem.InZone(zone);
        if (here.Count == 0) return;
        var found = new List<string>();
        foreach (RuralSettlement settlement in here)
            if (settlement != null && !settlement.fort) found.Add(RuralSettlementSystem.Label(settlement));
        if (found.Count == 0) return;
        RuralSettlementSystem.Absorb(zone);
        names ??= new List<string>();
        names.AddRange(found);
    }

    // 住房、民居上限当场重算(城市面板、人口上限立刻跟着变)；拆掉的要等原版收走才不算，到时再对一次账
    private static void RefreshStatus(City city)
    {
        try
        {
            city.setStatusDirty();
            CityPopulationSystem.InvalidateHouseholdCaches();
            city.updateCityStatus();
            city.recalculateMaxHouses();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][手动城区] 重算住房失败({city.data?.name}): {exception.Message}");
        }
    }

    #endregion

    #region 改动

    // 改动的结果(提示用)：盖了几座民居、拆了几座、挪了几座；并进城里的村镇
    public struct Outcome
    {
        public int Built, Razed, Moved;
        public List<string> Absorbed;
        public bool Any => Built + Razed + Moved > 0 || Absorbed?.Count > 0;
    }

    // 进入手动城区：按眼下平面图画成城区的地块(含以前手动并入、划出的结果，连同城心)记下；已经是手动的返回 false。
    // 只记表；建筑由调用方随后对账(Sync)
    public static bool Enter(City city)
    {
        if (city?.data == null || city.isRekt()) return false;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (extra.plan_urban != null) return false;
        List<int> ids = PureMapPatch.TownZoneIds(city);
        TileZone center = city.getTile()?.zone;
        if (center != null && center.city == city && !ids.Contains(center.id)) ids.Add(center.id);
        extra.plan_urban = ids;
        // 以前的并入、划出已经算进表里
        extra.plan_zones_in = null;
        extra.plan_zones_out = null;
        return true;
    }

    // 对账：让原版建筑与城区表一致——
    //   ① 城区外的民居拆掉(城区里随后补满)；② 城区里超出建筑位的民居拆掉(等级低的先)；
    //   ③ 城区外的别的建筑挪进城区(挪不下拆掉；城心的公所、篝火挪不下留着)；④ 城区里不满的地块补民居；⑤ 城区里的村镇并进城里。
    // 自动的城什么都不做
    public static Outcome Sync(City city)
    {
        var outcome = new Outcome();
        HashSet<int> urban = UrbanSet(city);
        if (urban == null || city.zones == null || city.buildings == null) return outcome;
        try
        {
            int cap = SlotsPerZone(city);
            var zones = new List<TileZone>();
            foreach (TileZone zone in city.zones)
                if (zone != null && urban.Contains(zone.id)) zones.Add(zone);
            Dictionary<int, List<Building>> byZone = ByZone(city, urban, out List<Building> outside);
            var movers = new List<Building>();
            foreach (Building building in outside)
            {
                if (!IsHouse(building.asset))
                {
                    movers.Add(building);
                    continue;
                }
                building.startRemove();
                outcome.Razed++;
            }
            foreach (TileZone zone in zones)
            {
                List<Building> here = Here(byZone, zone.id);
                int over = here.Count + Reserved(city, zone.id) - cap;
                while (over > 0 && RemoveHouse(here))
                {
                    over--;
                    outcome.Razed++;
                }
            }
            foreach (Building building in movers)
            {
                if (Move(city, building, zones, byZone, cap)) outcome.Moved++;
                else if (!IsCore(building.asset))
                {
                    building.startRemove();
                    outcome.Razed++;
                }
            }
            BuildingAsset house = BestAsset(city, HouseType);
            foreach (TileZone zone in zones)
            {
                List<Building> here = Here(byZone, zone.id);
                outcome.Built += FillHouses(city, zone, house, cap - here.Count - Reserved(city, zone.id), here);
            }
            foreach (TileZone zone in zones) Absorb(zone, ref outcome.Absorbed);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][手动城区] 对账失败({city.data?.name}): {exception.Message}");
        }
        RefreshStatus(city);
        return outcome;
    }

    // 并入一块地(手动城区的城)：村镇并进城里，当场盖满民居(原来就有一堆建筑、超出建筑位的拆民居)
    public static Outcome ZoneIn(City city, TileZone zone)
    {
        var outcome = new Outcome();
        if (city?.data == null || zone == null || zone.city != city) return outcome;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (extra.plan_urban == null) return outcome;
        if (!extra.plan_urban.Contains(zone.id)) extra.plan_urban.Add(zone.id);
        try
        {
            Absorb(zone, ref outcome.Absorbed);
            List<Building> here = InZone(city, zone);
            int room = SlotsPerZone(extra) - here.Count - Reserved(city, zone.id);
            while (room < 0 && RemoveHouse(here))
            {
                room++;
                outcome.Razed++;
            }
            outcome.Built = FillHouses(city, zone, BestAsset(city, HouseType), room, here);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][手动城区] 并入失败({city.data?.name}): {exception.Message}");
        }
        RefreshStatus(city);
        Later(city);
        return outcome;
    }

    // 划出一块地(手动城区的城)：地上的原版建筑当场拆掉(按建筑所在格的地块算)；城心的公所、篝火挪进别的城区地块，挪不下留着
    public static Outcome ZoneOut(City city, TileZone zone)
    {
        var outcome = new Outcome();
        if (city?.data == null || zone == null) return outcome;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        if (extra.plan_urban == null) return outcome;
        extra.plan_urban.Remove(zone.id);
        Waits.RemoveAll(wait => wait.City == city && wait.Zone == zone.id);
        try
        {
            List<TileZone> zones = null;
            Dictionary<int, List<Building>> byZone = null;
            foreach (Building building in InZone(city, zone))
            {
                if (!IsCore(building.asset))
                {
                    building.startRemove();
                    outcome.Razed++;
                    continue;
                }
                if (zones == null)
                {
                    HashSet<int> urban = UrbanSet(city);
                    zones = new List<TileZone>();
                    foreach (TileZone other in city.zones)
                        if (other != null && urban.Contains(other.id)) zones.Add(other);
                    byZone = ByZone(city, urban, out _);
                }
                if (Move(city, building, zones, byZone, SlotsPerZone(extra))) outcome.Moved++;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][手动城区] 划出失败({city.data?.name}): {exception.Message}");
        }
        RefreshStatus(city);
        Later(city);
        return outcome;
    }

    public enum Placed { Built, Swapped, Waiting, NoRoom }

    // 在城区里这块地上当场盖一座(调用方已核过是城区、建筑是本城建造单里的)：放得下就盖，建筑位满了拆一座民居；
    // 放不下就拆一座民居腾地方，等原版收走了再盖(Waits)；没有民居可拆返回 NoRoom
    public static Placed Place(City city, TileZone zone, BuildingAsset asset)
    {
        if (city?.data == null || zone == null || asset == null) return Placed.NoRoom;
        Placed result = Placed.NoRoom;
        try
        {
            List<Building> here = InZone(city, zone);
            int used = here.Count + Reserved(city, zone.id);
            WorldTile spot = FindSpot(city, zone, asset);
            if (spot != null && Put(city, asset, spot) != null)
                result = used + 1 > SlotsPerZone(city) && RemoveHouse(here) ? Placed.Swapped : Placed.Built;
            else if (RemoveHouse(here))
            {
                Waits.Add(new Waiting { City = city, Zone = zone.id, Asset = asset, Until = Time.unscaledTime + WaitSeconds });
                result = Placed.Waiting;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][手动城区] 盖建筑失败({city.data?.name}): {exception.Message}");
        }
        if (result == Placed.NoRoom) return result;
        RefreshStatus(city);
        Later(city);
        return result;
    }

    // 拆掉这块地上的功能建筑(非民居；城心的公所、篝火不拆)，空出的位补民居(地方腾出来以前放不下的，一秒后对账时补)；返回拆掉的
    public static List<Building> Demolish(City city, TileZone zone)
    {
        var removed = new List<Building>();
        if (city?.data == null || zone == null) return removed;
        Waits.RemoveAll(wait => wait.City == city && wait.Zone == zone.id);
        try
        {
            List<Building> here = InZone(city, zone);
            foreach (Building building in here)
            {
                if (IsHouse(building.asset) || IsCore(building.asset)) continue;
                building.startRemove();
                removed.Add(building);
            }
            if (removed.Count == 0) return removed;
            foreach (Building building in removed) here.Remove(building);
            if (InUrban(city, zone))
                FillHouses(city, zone, BestAsset(city, HouseType), SlotsPerZone(city) - here.Count - Reserved(city, zone.id), here);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][手动城区] 拆建筑失败({city.data?.name}): {exception.Message}");
        }
        RefreshStatus(city);
        Later(city);
        return removed;
    }

    #endregion

    #region 随后

    // 拆了民居腾地方、等着盖的功能建筑
    private sealed class Waiting
    {
        public City City;
        public int Zone;
        public BuildingAsset Asset;
        public float Until;
    }

    private static readonly List<Waiting> Waits = new();
    // 改完以后什么时候再对一次账(拆掉的建筑那时多半已被原版收走，空出的地方补上)
    private static readonly Dictionary<City, float> Due = new();

    private static int Reserved(City city, int zoneId)
    {
        int count = 0;
        foreach (Waiting wait in Waits)
            if (wait.City == city && wait.Zone == zoneId) count++;
        return count;
    }

    public static void Later(City city)
    {
        if (city?.data != null) Due[city] = Time.unscaledTime + SyncDelay;
    }

    private static bool Alive(City city) => city?.data != null && !city.isRekt() && city.zones != null;

    // 每帧(纯净地图，见 PureMapPatch.RefreshUrbanSlice)：等着的功能建筑腾出地方就盖；到点的城对一次账，有变化就重画
    public static void Tick()
    {
        if (Due.Count == 0 && Waits.Count == 0) return;
        float now = Time.unscaledTime;
        for (int i = Waits.Count - 1; i >= 0; i--)
        {
            Waiting wait = Waits[i];
            TileZone zone = Alive(wait.City) ? World.world?.zone_calculator?.getZoneByID(wait.Zone) : null;
            bool done = zone == null || zone.city != wait.City || !Manual(wait.City);
            if (!done)
            {
                try
                {
                    WorldTile spot = FindSpot(wait.City, zone, wait.Asset);
                    done = spot != null && Put(wait.City, wait.Asset, spot) != null;
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][手动城区] 补盖建筑失败: {exception.Message}");
                    done = true;
                }
                if (done) CityPlanEditing.Touch(wait.City);
            }
            if (!done && now < wait.Until) continue;
            Waits.RemoveAt(i);
            if (Alive(wait.City)) Later(wait.City);
        }
        if (Due.Count == 0) return;
        List<City> ready = null;
        foreach (KeyValuePair<City, float> pair in Due)
            if (now >= pair.Value || !Alive(pair.Key)) (ready ??= new List<City>()).Add(pair.Key);
        if (ready == null) return;
        foreach (City city in ready)
        {
            Due.Remove(city);
            if (!Alive(city) || !Manual(city)) continue;
            if (Sync(city).Any) CityPlanEditing.Touch(city);
        }
    }

    #endregion
}
