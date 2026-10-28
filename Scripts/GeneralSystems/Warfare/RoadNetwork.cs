using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 路网与航线(模拟的，不实际铺在地上)。
//   · 陆路规划：同一片陆地上相邻的城之间规划干道(游牧营地不修路，见 CityStyleSystem.Roadless；林地只修最低一档，
//     见 GradeCap)，但不是两两都通——
//       - 两城之间另有一座城比它们彼此都近(A 到 C、C 到 B 都比 A 到 B 近)，就经那座城走，不另规划直路；
//       - 先按地块寻路定走廊：绕开高山(六成以上是山的地块不走)与海面，山地、沼泽、丛林代价高，过河代价更高(架桥)，
//         已有的路代价低——所以路沿平地、谷地走，彼此并成干线网；地形代价是直线距离几倍(RoadPlan.terrain / straight)
//         决定要什么等级才修得通；
//       - 再在走廊(路线经过的地块及其一圈)里按格子寻路：山、高峰、原版城墙的格子一律不走，丘陵可以走(代价加倍)，
//         过河过湖算架桥(代价高，画成桥)，已有的路便宜；城墙是一格宽的线，横在走廊里绕不过去时把走廊放宽到三圈再找，
//         还绕不过(山挡住、城被墙围死)的不修；城墙上筑了关城的，路从关门过墙(见 WallGateSystem)；斜接的城墙缝钻不过去；
//         城池全图筑了城墙的城从城门进出，城门离走廊远的把两头城的地全算进走廊再找(见 CityPlanWorks)；
//     规划好的路要各国出钱修、每年出钱养(见 RoadBuildingSystem)，修通的路才画出来、才能走；
//     行军(见 CorpsSystem)：寻路时路上的地块代价低、路外按兵种加价，路外行军按兵种打折(见 CorpsArmSystem.OffRoad)；城心也算有路。
//   · 海路：港口城之间的航线——每座港口连最近的两座港口，另连一座别的陆地上最近的港口。按地块在海面上寻路，
//     绕开陆地，近岸与海峡代价高，已有的航线代价低，彼此并成航线网；
//     运兵船与舰队出海都按同样的代价寻路(见 SeaPath)，所以尽量沿航线走。
//   · 画出来：陆路把格子路线拉直成几段(拉直的线不能压到山、不能比原路贵)，再削角平滑两遍，平滑后压到山格就不平滑；
//     海路是航线经过的地块中心各偏开一两格(按地块散列，路线稳定)再削角平滑；
//     陆路是实线(过水处是桥)，海路隔几格断开成虚线(纯净地图见 RoadOverlay；运兵的航线见 CorpsMapRenderer)。
// 每条路线算一次就缓存，过期才重算；每秒只花几毫秒补算(刚进图时几秒内铺满)，不会卡顿。
public static class RoadNetwork
{
    // 没修的路线过 StaleSeconds 才重新找路(已有的路变了，路线可能更顺)；以前三分钟，大图上后台一直在重算，
    // 一条路两回格子寻路就几十上百毫秒(性能日志"路网·结算"每帧近 6 毫秒)
    private const float TickSeconds = 1f, ScanSeconds = 5f, StaleSeconds = 1200f, PortSeconds = 30f;
    // 每次 Tick 补算路线最多花几毫秒；寻路最多展开几块
    private const long BudgetMs = 6;
    private const int MaxSearch = 5000;
    private const float RoadCost = 0.6f, RoughCost = 3f, RiverCost = 4f, CoastCost = 3f, HarbourCost = 2f;
    // 格子寻路：丘陵、过水(桥)每格的代价；最多展开几格；拉直时一段最长几格；地块里山格占几成以上整块不走
    private const float HillTileCost = 2f, BridgeTileCost = 6f;
    private const int MaxTileSearch = 40000, MaxPull = 16;
    private const float MountainZoneShare = 0.6f;
    // 走廊绕不过去(城墙横在中间)时放宽到几圈地块
    private const int WideCorridor = 3;
    // 港口连几座最近的港口；航线最长几个地块；绕得比直线远太多(隔着整片大陆)的不连
    private const int LaneNeighbours = 2, LaneMaxZones = 72;
    private const float LaneDetour = 2.5f;
    // 海路虚线：每段几格画、几格空
    private const int DashOn = 3, DashOff = 2;

    // 一条路线(陆路或海路)：经过的地块、落到的格子；陆路另记地形代价与直线距离(地块)、两头的城(id 小的在前；航线不记)
    public sealed class RoadPlan
    {
        public float at;
        public float terrain, straight;
        public (long, long) cities = (-1L, -1L);
        public readonly List<int> zones = new();
        public readonly List<int> tiles = new();

        // 这个等级的路修得通吗(地形代价不超过直线距离的几倍，见 RoadBuildingSystem.DetourLimit)；
        // 超过两头城市风格允许的等级(林地只修土路，见 GradeCap)的也修不了
        public bool Buildable(int grade) => zones.Count > 0 && grade <= GradeCap(cities) &&
                                            terrain <= Mathf.Max(1f, straight) * RoadBuildingSystem.DetourLimit(grade);
    }

    // 一条路最高修到几等：两头城市风格的上限取小(用户 2026-10-21 选林地"只修最低一档的路"，见 CityStyleSystem.RoadGradeCap)。
    // 按眼下的风格现算，城改建了风格、已修的路不重新规划也认得出；没记两头的(航线)不限
    public static int GradeCap((long, long) key)
    {
        if (key.Item1 < 0 || key.Item2 < 0 || World.world?.cities == null) return RoadBuildingSystem.MaxGrade;
        return Math.Min(CityStyleSystem.RoadGradeCap(World.world.cities.get(key.Item1)),
            CityStyleSystem.RoadGradeCap(World.world.cities.get(key.Item2)));
    }

    private static readonly Dictionary<(long, long), RoadPlan> Roads = new(), Lanes = new();
    private static readonly HashSet<int> RoadZones = new(), RoadTiles = new(), LaneZones = new(), LaneTiles = new(),
        BuildingTiles = new();
    private static readonly Dictionary<int, int> TileGrades = new(), ZoneGrades = new();
    private static readonly Dictionary<int, float> MountainShares = new();
    private static readonly List<City> PortCities = new();
    private static float _nextTick, _nextPorts, _nextScan;
    private static bool _dirty;
    private static object _world;

    // 路网每变一次加一(RoadOverlay 据此重画)
    public static int Version { get; private set; }

    // 修通的路(格 → 等级)、在修的路段、航线
    public static IReadOnlyDictionary<int, int> RoadTileGrades => TileGrades;
    public static IReadOnlyCollection<int> BuildingTileIds => BuildingTiles;
    public static IReadOnlyCollection<int> LaneTileIds => LaneTiles;

    // 陆路规划(两城 id，小的在前)；规划还有没算完的
    public static IReadOnlyDictionary<(long, long), RoadPlan> Plans => Roads;
    public static bool PlansComplete { get; private set; }

    public static RoadPlan PlanOf((long, long) key) => Roads.TryGetValue(key, out RoadPlan plan) ? plan : null;

    public static void MarkDirty() => _dirty = true;

    // 神力修了、画了路(见 RoadBuildingSystem.ConnectByPower、RoadPaintSystem)：当场重收路格、换版本号，不等下一秒的结算
    public static void RefreshNow()
    {
        if (World.world?.tiles_list == null) return;
        _dirty = false;
        CollectRoads();
        Version++;
    }

    // 地形变了(图层城墙转换、还原)：路线全部重算(修了的路照旧保留，只是重新找路)
    public static void Replan()
    {
        Roads.Clear();
        PlansComplete = false;
        _nextScan = 0f;
        _dirty = true;
    }

    // 一座城的城心挪了(迁城，见 CityRelocationSystem)：连着它的陆路、航线丢掉，照新城心重新找路——修了的路也重找，
    // 修路的等级、进度记在 RoadBuildingSystem 里不动；马上接着算(不等五秒一次的全量扫描)
    public static void ReplanCity(City city)
    {
        if (city?.data == null) return;
        long id = city.data.id;
        foreach (var key in Roads.Keys.Where(key => key.Item1 == id || key.Item2 == id).ToList()) Roads.Remove(key);
        foreach (var key in Lanes.Keys.Where(key => key.Item1 == id || key.Item2 == id).ToList()) Lanes.Remove(key);
        PlansComplete = false;
        _nextTick = _nextScan = 0f;
        _dirty = true;
    }

    // 有路的地块：干道经过的，以及城心
    public static bool IsRoad(TileZone zone) =>
        zone != null && (RoadZones.Contains(zone.id) || zone.city?.data != null && zone.city.getTile()?.zone == zone);

    // 地块上最好的路的等级(没有路为 0；城心按驿道算)
    public static int RoadGrade(TileZone zone)
    {
        if (zone == null) return 0;
        if (ZoneGrades.TryGetValue(zone.id, out int grade)) return grade;
        return zone.city?.data != null && zone.city.getTile()?.zone == zone ? 2 : 0;
    }

    public static void ResetWorldState()
    {
        Roads.Clear();
        Lanes.Clear();
        RoadZones.Clear();
        RoadTiles.Clear();
        LaneZones.Clear();
        LaneTiles.Clear();
        BuildingTiles.Clear();
        TileGrades.Clear();
        ZoneGrades.Clear();
        MountainShares.Clear();
        PortCities.Clear();
        PlansComplete = false;
        RoadBuildingSystem.ResetWorldState();
        _nextTick = _nextPorts = _nextScan = 0f;
        _world = null;
        Version++;
    }

    public static void Tick()
    {
        if (World.world?.cities == null || World.world.zone_calculator == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            ResetWorldState();
            _world = World.world;
        }
        float now = Time.unscaledTime;
        if (now < _nextTick && _nextTick - now <= TickSeconds) return;
        _nextTick = now + TickSeconds;
        using var timing = FrameProfiler.Measure("路网·结算");
        try
        {
            var budget = global::System.Diagnostics.Stopwatch.StartNew();
            // 全量扫描(各城的路、规划、航线)每五秒一次；规划还没算完时每秒接着算
            if (!PlansComplete || now >= _nextScan || _nextScan - now > ScanSeconds)
            {
                _nextScan = now + ScanSeconds;
                RoadBuildingSystem.Refresh();
                TickRoads(now, budget);
                TickLanes(now, budget);
                RoadBuildingSystem.SeedIfOldWorld();
            }
            RoadBuildingSystem.Tick();
            if (!_dirty) return;
            _dirty = false;
            using (FrameProfiler.Measure("路网·收路格"))
            {
                CollectRoads();
                Collect(Lanes, LaneZones, LaneTiles);
            }
            Version++;
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][路网] 结算失败: {exception.Message}");
        }
    }

    private static void Collect(Dictionary<(long, long), RoadPlan> routes, HashSet<int> zones, HashSet<int> tiles)
    {
        zones.Clear();
        tiles.Clear();
        foreach (RoadPlan route in routes.Values)
        {
            zones.UnionWith(route.zones);
            tiles.UnionWith(route.tiles);
        }
    }

    // 只收修通的路(按等级)；在修的路从起点画到修到的地方
    private static void CollectRoads()
    {
        RoadZones.Clear();
        RoadTiles.Clear();
        BuildingTiles.Clear();
        TileGrades.Clear();
        ZoneGrades.Clear();
        foreach (var pair in Roads)
        {
            RoadLink link = RoadBuildingSystem.Get(pair.Key);
            if (link == null) continue;
            RoadPlan plan = pair.Value;
            if (link.grade > 0)
            {
                RoadZones.UnionWith(plan.zones);
                RoadTiles.UnionWith(plan.tiles);
                foreach (int tile in plan.tiles)
                    TileGrades[tile] = TileGrades.TryGetValue(tile, out int grade) ? Math.Max(grade, link.grade) : link.grade;
                foreach (int zone in plan.zones)
                    ZoneGrades[zone] = ZoneGrades.TryGetValue(zone, out int grade) ? Math.Max(grade, link.grade) : link.grade;
            }
            if (link.building > 0 && plan.terrain > 0f)
            {
                int built = Mathf.FloorToInt(plan.tiles.Count * Mathf.Clamp01(link.progress / plan.terrain));
                for (int i = 0; i < built; i++)
                    if (!TileGrades.ContainsKey(plan.tiles[i])) BuildingTiles.Add(plan.tiles[i]);
            }
        }
        // 神力画的路(见 RoadPaintSystem)：按格并进来
        foreach (var pair in RoadPaintSystem.Tiles)
        {
            WorldTile tile = RoadPaintSystem.TileOf(pair.Key);
            if (tile?.zone == null || pair.Value.grade <= 0) continue;
            int grade = pair.Value.grade, zone = tile.zone.id;
            RoadTiles.Add(pair.Key);
            RoadZones.Add(zone);
            BuildingTiles.Remove(pair.Key);
            TileGrades[pair.Key] = TileGrades.TryGetValue(pair.Key, out int had) ? Math.Max(had, grade) : grade;
            ZoneGrades[zone] = ZoneGrades.TryGetValue(zone, out int zoneHad) ? Math.Max(zoneHad, grade) : grade;
        }
        ConnectCorners();
    }

    // 只有一个角相接的两格路(格子寻路八方向、直线落格，斜着走的地方就是这样)：画出来是断的，路两侧还描着深色的边，
    // 一格一格像虚线(用户 2026-10-22 "无论是大路还是小路总是保证他们是连接的""不要有断开")。
    // 在两格共有的两个正邻格里补一格把它们接成边挨边的：先挑陆地，河湖要能架桥的等级(官道起)才补，海面、山、城墙不补；
    // 等级取两格较低的。大路、小路、神力画的路一视同仁。接头只管画(路格)，不算进有路的地块(不改行军、教化、后勤)
    private const int BridgeGrade = 3;

    private static void ConnectCorners()
    {
        if (World.world?.tiles_list == null || TileGrades.Count == 0) return;
        var joints = new Dictionary<int, int>();
        foreach (var pair in TileGrades)
        {
            WorldTile tile = World.world.tiles_list[pair.Key];
            if (tile == null) continue;
            // 每对斜邻只看一次(右上、右下)
            for (int dy = -1; dy <= 1; dy += 2)
            {
                WorldTile corner = World.world.GetTile(tile.x + 1, tile.y + dy);
                if (corner?.data == null || !TileGrades.TryGetValue(corner.data.tile_id, out int other)) continue;
                WorldTile side = World.world.GetTile(tile.x + 1, tile.y), across = World.world.GetTile(tile.x, tile.y + dy);
                if (IsRoadTile(side) || IsRoadTile(across) || side != null && joints.ContainsKey(side.data.tile_id) ||
                    across != null && joints.ContainsKey(across.data.tile_id)) continue;
                int grade = Math.Min(pair.Value, other);
                WorldTile joint = JointTile(side, grade) ?? JointTile(across, grade);
                if (joint == null && JointTile(side, grade, water: true) is WorldTile wet) joint = wet;
                else if (joint == null) joint = JointTile(across, grade, water: true);
                if (joint != null) joints[joint.data.tile_id] = grade;
            }
        }
        foreach (var pair in joints)
        {
            WorldTile tile = World.world.tiles_list[pair.Key];
            RoadTiles.Add(pair.Key);
            BuildingTiles.Remove(pair.Key);
            TileGrades[pair.Key] = pair.Value;
        }
    }

    private static bool IsRoadTile(WorldTile tile) => tile?.data != null && TileGrades.ContainsKey(tile.data.tile_id);

    // 能补成接头的格：走得了(不是山、城墙、岩浆)；water 为假时只要陆地，为真时水面要能架桥的等级
    private static WorldTile JointTile(WorldTile tile, int grade, bool water = false)
    {
        if (tile?.data == null || tile.zone == null || tile.Type.ocean || TileStep(tile) < 0f) return null;
        bool wet = tile.Type.liquid;
        if (!water) return wet ? null : tile;
        return wet && grade >= BridgeGrade ? tile : null;
    }

    private static bool Fresh(Dictionary<(long, long), RoadPlan> routes, (long, long) key, float now) =>
        routes.TryGetValue(key, out RoadPlan route) && now - route.at < StaleSeconds && now >= route.at;

    private static void Prune(Dictionary<(long, long), RoadPlan> routes, HashSet<(long, long)> wanted)
    {
        foreach (var key in routes.Keys.Where(key => !wanted.Contains(key)).ToList())
        {
            routes.Remove(key);
            _dirty = true;
        }
    }

    #region 陆路

    private static bool Spent(global::System.Diagnostics.Stopwatch budget) => budget.ElapsedMilliseconds >= BudgetMs;

    private static void TickRoads(float now, global::System.Diagnostics.Stopwatch budget)
    {
        var wanted = new HashSet<(long, long)>();
        bool pending = false;
        foreach (City city in World.world.cities.list)
        {
            WorldTile from = city?.getTile();
            if (from == null || city.isRekt() || CityStyleSystem.Roadless(city)) continue;
            foreach (City other in city.neighbours_cities)
            {
                WorldTile to = other?.getTile();
                if (to == null || other.isRekt() || other.data.id <= city.data.id || !from.isSameIsland(to) ||
                    CityStyleSystem.Roadless(other)) continue;
                if (Bypassed(city, other, from, to)) continue;
                pending |= Plan((city.data.id, other.data.id), from, to, now, budget, wanted);
            }
        }
        // 已经修了(或在修)的路照旧保留，即使后来两城之间又建了新城
        foreach (var key in RoadBuildingSystem.All.Keys.ToList())
        {
            if (wanted.Contains(key)) continue;
            City first = World.world.cities.get(key.Item1), second = World.world.cities.get(key.Item2);
            if (CityStyleSystem.Roadless(first) || CityStyleSystem.Roadless(second)) continue;
            WorldTile from = first?.getTile(), to = second?.getTile();
            if (from != null && to != null) pending |= Plan(key, from, to, now, budget, wanted);
        }
        Prune(Roads, wanted);
        PlansComplete = !pending;
    }

    // 规划一条路(已修的路线不再重算，免得路自己挪地方)；返回是不是因为本次时间用完还没算
    private static bool Plan((long, long) key, WorldTile from, WorldTile to, float now,
        global::System.Diagnostics.Stopwatch budget, HashSet<(long, long)> wanted)
    {
        wanted.Add(key);
        bool built = RoadBuildingSystem.Get(key) is { } link && (link.grade > 0 || link.building > 0);
        if (Roads.ContainsKey(key) && (built || Fresh(Roads, key, now))) return false;
        if (Spent(budget)) return true;
        RoadPlan plan;
        using (FrameProfiler.Measure("路网·陆路找路")) plan = BuildRoad(from, to, now);
        plan.cities = key;
        Roads[key] = plan;
        // 只有修了(在修)的路要重收路格；没修的路线不画，不必重收
        if (built) _dirty = true;
        return false;
    }

    // 两城之间另有一座城比它们彼此都近：经那座城走，不另修直路
    private static bool Bypassed(City a, City b, WorldTile ta, WorldTile tb)
    {
        float ab = Squared(ta, tb);
        foreach (City c in a.neighbours_cities.Concat(b.neighbours_cities))
        {
            WorldTile tc = c?.getTile();
            if (tc == null || c == a || c == b || c.isRekt() || !tc.isSameIsland(ta)) continue;
            if (Squared(ta, tc) < ab && Squared(tc, tb) < ab) return true;
        }
        return false;
    }

    private static float Squared(WorldTile a, WorldTile b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

    private static RoadPlan BuildRoad(WorldTile from, WorldTile to, float now)
    {
        var route = new RoadPlan { at = now };
        TileZone goal = to.zone;
        List<TileZone> path = FindZones(from.zone, goal, zone => RoadStep(zone, goal), RoadCost);
        if (path == null) return route;
        // 地形代价(翻山、穿沼泽丛林、大河、大绕路都高)：最高等级的路也修不通的不规划
        float terrain = 0f;
        for (int i = 1; i < path.Count; i++)
            terrain += Terrain(path[i]) * (path[i].x != path[i - 1].x && path[i].y != path[i - 1].y ? 1.41f : 1f);
        route.terrain = Mathf.Max(1f, terrain);
        route.straight = Mathf.Max(1f, Distance(from.zone, goal));
        if (route.terrain > route.straight * RoadBuildingSystem.DetourLimit(RoadBuildingSystem.MaxGrade)) return route;
        // 格子一级：走廊里绕不过山的不修。城池全图筑了城墙的城只从城门进出(见 CityPlanWorks)，大城的城门可能在走廊三圈以外，
        // 再把两头城的地全算进走廊找一回
        List<WorldTile> tiles = FindTiles(from, to, Corridor(path, 1)) ?? FindTiles(from, to, Corridor(path, WideCorridor));
        if (tiles == null && (CityPlanWorks.Walled(from.zone?.city) || CityPlanWorks.Walled(to.zone?.city)))
            tiles = FindTiles(from, to, WithCities(Corridor(path, WideCorridor), from, to));
        if (tiles == null) return new RoadPlan { at = now };
        foreach (WorldTile tile in tiles)
            if (tile.zone != null && (route.zones.Count == 0 || route.zones[route.zones.Count - 1] != tile.zone.id) &&
                !route.zones.Contains(tile.zone.id))
                route.zones.Add(tile.zone.id);
        DrawRoad(tiles, route.tiles);
        return route;
    }

    private static float RoadStep(TileZone zone, TileZone goal)
    {
        if (zone != goal && (TerrainSystem.Impassable(zone) || SeaRegionSystem.IsSea(zone) ||
                             MountainShare(zone) >= MountainZoneShare)) return -1f;
        return (RoadZones.Contains(zone.id) ? RoadCost : 1f) * Terrain(zone);
    }

    // 地块里山、高峰格子占几成(地形少变，算一次记下)
    private static float MountainShare(TileZone zone)
    {
        if (MountainShares.TryGetValue(zone.id, out float share)) return share;
        int mountains = 0, total = 0;
        foreach (WorldTile tile in zone.tiles ?? Array.Empty<WorldTile>())
        {
            if (tile?.Type == null) continue;
            total++;
            if (Mountain(tile) && !tile.Type.wall && !MapWallSystem.Contains(tile)) mountains++;
        }
        share = total == 0 ? 0f : mountains / (float)total;
        MountainShares[zone.id] = share;
        return share;
    }

    // 山、高峰、原版城墙与图层城墙(见 MapWallSystem)：路不走；丘陵可以。城墙不算进地块的山地占比(一格宽的线，绕过去就行)。
    // 关城压着的关门(见 WallGateSystem)照走：以前城墙格一律当山，关挨着墙筑也过不去
    private static bool Mountain(WorldTile tile) => tile.Type != null &&
        (tile.Type.mountains || tile.Type.block && !tile.Type.wall || MapWallSystem.Blocks(tile));

    // 走进这一格的代价：山不走，丘陵加倍，水面是桥，已有的路便宜
    private static float TileStep(WorldTile tile)
    {
        if (tile?.Type == null || Mountain(tile) || tile.Type.lava) return -1f;
        float cost = tile.Type.liquid ? BridgeTileCost : tile.Type.rocks ? HillTileCost : 1f;
        return RoadTiles.Contains(tile.data.tile_id) ? cost * RoadCost : cost;
    }

    // 走廊：地块路线经过的地块及其周围 ring 圈
    private static HashSet<int> Corridor(List<TileZone> path, int ring)
    {
        var corridor = new HashSet<int>();
        ZoneCalculator zones = World.world.zone_calculator;
        foreach (TileZone zone in path)
            for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                    if (zones.getZone(zone.x + dx, zone.y + dy) is TileZone next) corridor.Add(next.id);
        return corridor;
    }

    // 走廊加上两头城的全部地块(城门落在本城的地上)
    private static HashSet<int> WithCities(HashSet<int> corridor, WorldTile from, WorldTile to)
    {
        foreach (WorldTile end in new[] { from, to })
        {
            City city = end?.zone?.city;
            if (city?.zones == null) continue;
            foreach (TileZone zone in city.zones)
                if (zone != null) corridor.Add(zone.id);
        }
        return corridor;
    }

    // 格子一级的 A*(八方向，斜走乘 1.41)，只走走廊里的格子；两端是城心
    private static List<WorldTile> FindTiles(WorldTile from, WorldTile to, HashSet<int> corridor)
    {
        if (from == null || to == null) return null;
        int start = from.data.tile_id, goal = to.data.tile_id;
        var cost = new Dictionary<int, float> { [start] = 0f };
        var previous = new Dictionary<int, WorldTile>();
        var closed = new HashSet<int>();
        var open = new List<(float key, WorldTile tile)> { (0f, from) };
        while (open.Count > 0 && closed.Count < MaxTileSearch)
        {
            WorldTile tile = PopTile(open);
            int id = tile.data.tile_id;
            if (id == goal) break;
            if (!closed.Add(id)) continue;
            float here = cost[id];
            foreach (WorldTile next in tile.neighboursAll ?? Array.Empty<WorldTile>())
            {
                if (next?.zone == null || closed.Contains(next.data.tile_id) || !corridor.Contains(next.zone.id)) continue;
                float enter = next.data.tile_id == goal ? 1f : TileStep(next);
                if (enter < 0f || MapWallSystem.CutsCorner(tile.x, tile.y, next.x, next.y)) continue;
                if (next.x != tile.x && next.y != tile.y) enter *= 1.41f;
                float total = here + enter;
                if (cost.TryGetValue(next.data.tile_id, out float known) && known <= total) continue;
                cost[next.data.tile_id] = total;
                previous[next.data.tile_id] = tile;
                float dx = next.x - to.x, dy = next.y - to.y;
                PushTile(open, (total + Mathf.Sqrt(dx * dx + dy * dy) * RoadCost, next));
            }
        }
        if (!previous.ContainsKey(goal)) return null;
        var path = new List<WorldTile>();
        for (WorldTile tile = to; tile != null; tile = previous.TryGetValue(tile.data.tile_id, out WorldTile back) ? back : null)
        {
            path.Add(tile);
            if (tile.data.tile_id == start) break;
        }
        path.Reverse();
        return path;
    }

    private static void PushTile(List<(float key, WorldTile tile)> heap, (float key, WorldTile tile) item)
    {
        heap.Add(item);
        int i = heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heap[parent].key <= heap[i].key) break;
            (heap[parent], heap[i]) = (heap[i], heap[parent]);
            i = parent;
        }
    }

    private static WorldTile PopTile(List<(float key, WorldTile tile)> heap)
    {
        WorldTile top = heap[0].tile;
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        int i = 0;
        while (true)
        {
            int left = 2 * i + 1, right = left + 1, smallest = i;
            if (left < heap.Count && heap[left].key < heap[smallest].key) smallest = left;
            if (right < heap.Count && heap[right].key < heap[smallest].key) smallest = right;
            if (smallest == i) break;
            (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
            i = smallest;
        }
        return top;
    }

    // 格子路线落成路：拉直成几段(拉直的线不压山、不比原路贵)，削角平滑两遍；平滑后压到山就用拉直的线，
    // 再不行用原路。过水处照画(桥)
    private static void DrawRoad(List<WorldTile> path, List<int> tiles)
    {
        var prefix = new float[path.Count];
        for (int i = 1; i < path.Count; i++)
        {
            float step = Mathf.Max(0f, TileStep(path[i]));
            if (path[i].x != path[i - 1].x && path[i].y != path[i - 1].y) step *= 1.41f;
            prefix[i] = prefix[i - 1] + step;
        }
        var pulled = new List<Vector2> { new(path[0].x, path[0].y) };
        int anchor = 0;
        while (anchor < path.Count - 1)
        {
            int best = anchor + 1;
            for (int j = anchor + 2; j < path.Count && j - anchor <= MaxPull; j++)
            {
                float line = LineCost(path[anchor], path[j]);
                if (line >= 0f && line <= (prefix[j] - prefix[anchor]) * 1.1f + 0.5f) best = j;
            }
            pulled.Add(new Vector2(path[best].x, path[best].y));
            anchor = best;
        }
        List<WorldTile> drawn = Raster(Chaikin(Chaikin(pulled))) ?? Raster(pulled) ?? path;
        int last = -1;
        foreach (WorldTile tile in drawn)
        {
            int id = tile.data.tile_id;
            if (id == last) continue;
            tiles.Add(id);
            last = id;
        }
    }

    // 两格之间的直线(不含起点)的代价；压到山返回 -1
    private static float LineCost(WorldTile a, WorldTile b)
    {
        float total = 0f;
        int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        int px = x0, py = y0;
        for (int guard = 0; guard < 512 && (x0 != x1 || y0 != y1); guard++)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
            WorldTile tile = World.world.GetTile(x0, y0);
            float step = tile == b ? 1f : TileStep(tile);
            if (step < 0f || MapWallSystem.CutsCorner(px, py, x0, y0)) return -1f;
            total += x0 != px && y0 != py ? step * 1.41f : step;
            px = x0;
            py = y0;
        }
        return total;
    }

    // 折线落到格子上(不分水陆)；压到山返回 null
    private static List<WorldTile> Raster(List<Vector2> points)
    {
        var result = new List<WorldTile>();
        if (points.Count == 0) return result;
        WorldTile first = World.world.GetTile(Mathf.RoundToInt(points[0].x), Mathf.RoundToInt(points[0].y));
        WorldTile end = World.world.GetTile(Mathf.RoundToInt(points[points.Count - 1].x), Mathf.RoundToInt(points[points.Count - 1].y));
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int x0 = Mathf.RoundToInt(points[i].x), y0 = Mathf.RoundToInt(points[i].y);
            int x1 = Mathf.RoundToInt(points[i + 1].x), y1 = Mathf.RoundToInt(points[i + 1].y);
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 4096; guard++)
            {
                WorldTile tile = World.world.GetTile(x0, y0);
                if (tile == null) return null;
                if (result.Count == 0 || result[result.Count - 1] != tile)
                {
                    if (Mountain(tile) && tile != first && tile != end) return null;
                    WorldTile previous = result.Count == 0 ? null : result[result.Count - 1];
                    if (previous != null && MapWallSystem.CutsCorner(previous.x, previous.y, tile.x, tile.y)) return null;
                    result.Add(tile);
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
        return result;
    }

    // 地形代价：平地 1，山地沼泽丛林乘 RoughCost，多水的地块(大河、湖)乘 RiverCost
    private static float Terrain(TileZone zone)
    {
        float cost = 1f;
        if (TerrainSystem.SlowsMarch(zone)) cost *= RoughCost;
        if (zone.tiles_with_liquid * 4 > zone.tiles_with_ground + zone.tiles_with_liquid) cost *= RiverCost;
        return cost;
    }

    #endregion

    #region 海路

    private static void TickLanes(float now, global::System.Diagnostics.Stopwatch budget)
    {
        if (now >= _nextPorts || _nextPorts - now > PortSeconds)
        {
            _nextPorts = now + PortSeconds;
            PortCities.Clear();
            PortCities.AddRange(World.world.cities.list.Where(city =>
                city?.data != null && !city.isRekt() && city.getTile() != null && NavySystem.Port(city) != null));
        }
        var wanted = new HashSet<(long, long)>();
        foreach (City port in PortCities)
        {
            if (port?.data == null || port.isRekt()) continue;
            TileZone at = port.getTile()?.zone;
            if (at == null) continue;
            WorldTile island = port.getTile();
            var nearest = PortCities.Where(other => other != port && other?.data != null && !other.isRekt())
                .Select(other => (other, distance: CorpsSystem.ZoneDistance(at, other.getTile()?.zone)))
                .Where(entry => entry.distance <= LaneMaxZones).OrderBy(entry => entry.distance).ToList();
            var picks = nearest.Take(LaneNeighbours).Select(entry => entry.other).ToList();
            City overseas = nearest.Select(entry => entry.other).FirstOrDefault(other => !other.getTile().isSameIsland(island));
            if (overseas != null && !picks.Contains(overseas)) picks.Add(overseas);
            foreach (City other in picks)
            {
                var key = port.data.id < other.data.id ? (port.data.id, other.data.id) : (other.data.id, port.data.id);
                if (!wanted.Add(key) || Spent(budget) || Fresh(Lanes, key, now)) continue;
                using (FrameProfiler.Measure("路网·航线找路")) Lanes[key] = BuildLane(port, other, now);
                _dirty = true;
            }
        }
        Prune(Lanes, wanted);
    }

    private static RoadPlan BuildLane(City from, City to, float now)
    {
        var route = new RoadPlan { at = now };
        TileZone a = from.getTile().zone, b = to.getTile().zone;
        List<TileZone> path = SeaPath(a, b, from, to);
        if (path == null || path.Count > CorpsSystem.ZoneDistance(a, b) * LaneDetour + 6) return route;
        route.zones.AddRange(path.Select(zone => zone.id));
        Draw(Smooth(path, from.getTile(), to.getTile()), route.tiles, liquid: true, dashed: true);
        return route;
    }

    // 海上寻路(航线、运兵船、舰队共用)：海面代价 1，航线上 0.6，近岸与海峡 3，两端港口城(登陆城)的地块 2，陆地不走。
    // allowed：额外限制能走的海面(运兵只走航线上的海区)；ports：两端可以穿过的城
    public static List<TileZone> SeaPath(TileZone from, TileZone to, City fromCity = null, City toCity = null,
        Func<TileZone, bool> allowed = null)
    {
        if (from == null || to == null) return null;
        return FindZones(from, to, zone =>
        {
            if (zone == to || zone == from) return HarbourCost;
            City owner = zone.city;
            bool harbour = owner != null && (owner == fromCity || owner == toCity);
            if (SeaRegionSystem.IsSea(zone))
                return allowed != null && !allowed(zone) ? -1f : LaneZones.Contains(zone.id) ? RoadCost : 1f;
            if (zone.world_edge) return -1f;
            if (zone.tiles_with_liquid >= 16) return CoastCost;
            return harbour ? HarbourCost : -1f;
        }, RoadCost);
    }

    #endregion

    #region 平滑与落格

    // 地块中心各偏开一两格，两端是城心(或给定的格)，再削角平滑两遍；返回格坐标(世界坐标与格坐标一致)
    public static List<Vector2> Smooth(IReadOnlyList<TileZone> path, WorldTile start = null, WorldTile end = null)
    {
        var points = new List<Vector2>();
        if (path == null || path.Count == 0) return points;
        for (int i = 0; i < path.Count; i++)
        {
            if (i == 0 && start != null) { points.Add(new Vector2(start.x, start.y)); continue; }
            if (i == path.Count - 1 && end != null) { points.Add(new Vector2(end.x, end.y)); continue; }
            points.Add(Jittered(path[i]));
        }
        for (int pass = 0; pass < 2; pass++) points = Chaikin(points);
        return points;
    }

    public static Vector2 Jittered(TileZone zone)
    {
        uint hash = (uint)(zone.id * 2654435761L);
        return new Vector2(zone.x * Config.CITY_ZONE_SIZE + 4 + (int)(hash % 5) - 2,
            zone.y * Config.CITY_ZONE_SIZE + 4 + (int)((hash >> 8) % 5) - 2);
    }

    private static List<Vector2> Chaikin(List<Vector2> points)
    {
        if (points.Count < 3) return points;
        var result = new List<Vector2>(points.Count * 2) { points[0] };
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            result.Add(Vector2.Lerp(a, b, 0.25f));
            result.Add(Vector2.Lerp(a, b, 0.75f));
        }
        result.Add(points[points.Count - 1]);
        return result;
    }

    // 折线落到格子上：陆路只落在陆地格，海路只落在水格；虚线按走过的格数断开
    private static void Draw(List<Vector2> points, List<int> tiles, bool liquid, bool dashed)
    {
        int walked = 0, lastX = int.MinValue, lastY = int.MinValue;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int x0 = Mathf.RoundToInt(points[i].x), y0 = Mathf.RoundToInt(points[i].y);
            int x1 = Mathf.RoundToInt(points[i + 1].x), y1 = Mathf.RoundToInt(points[i + 1].y);
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 4096; guard++)
            {
                if (x0 != lastX || y0 != lastY)
                {
                    lastX = x0;
                    lastY = y0;
                    WorldTile tile = World.world.GetTile(x0, y0);
                    bool water = tile?.Type?.liquid == true;
                    if (tile != null && water == liquid)
                    {
                        if (!dashed || walked % (DashOn + DashOff) < DashOn) tiles.Add(tile.data.tile_id);
                        walked++;
                    }
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }

    #endregion

    #region 寻路

    // 地块一级的 A*(八方向，斜走乘 1.41)；step 返回进入这块的代价，负数为不可走；minStep 是最低的单步代价(估价用)
    private static List<TileZone> FindZones(TileZone from, TileZone to, Func<TileZone, float> step, float minStep)
    {
        if (from == null || to == null) return null;
        if (from == to) return new List<TileZone> { from };
        var cost = new Dictionary<int, float> { [from.id] = 0f };
        var previous = new Dictionary<int, TileZone>();
        var closed = new HashSet<int>();
        var open = new List<(float key, TileZone zone)> { (0f, from) };
        while (open.Count > 0 && closed.Count < MaxSearch)
        {
            TileZone zone = CorpsSystem.PopMin(open);
            if (zone == to) break;
            if (!closed.Add(zone.id)) continue;
            float here = cost[zone.id];
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (next == null || closed.Contains(next.id)) continue;
                float enter = step(next);
                if (enter < 0f) continue;
                if (next.x != zone.x && next.y != zone.y) enter *= 1.41f;
                float total = here + enter;
                if (cost.TryGetValue(next.id, out float known) && known <= total) continue;
                cost[next.id] = total;
                previous[next.id] = zone;
                CorpsSystem.PushHeap(open, (total + Distance(next, to) * minStep, next));
            }
        }
        if (!previous.ContainsKey(to.id)) return null;
        var path = new List<TileZone>();
        for (TileZone zone = to; zone != null; zone = previous.TryGetValue(zone.id, out TileZone back) ? back : null)
        {
            path.Add(zone);
            if (zone == from) break;
        }
        path.Reverse();
        return path;
    }

    private static float Distance(TileZone a, TileZone b) =>
        Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    #endregion
}
