using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 通达层(用户 2026-11-06 "国家交流、文化制度的传播都要考虑距离和地形的隔离程度，越符合现实越好""和通信技术、科技挂钩"；
// 问答选"标准""视距可渡""先信息类")：各机制共用的一张"城与城之间走多远"的图，按出行一方的科技算成本，分两种口径——
//   · 人员(People；货物 Goods 暂同人员，二档再加运费)：使节、移民、商旅，无人区按习性、海按航海档、路按等级与驿传、摩托化；
//   · 消息(Ideas)：技术、制度、理念、教化、外交消息，沙漠冻土无人区 ×2 能过，驿传对消息折得更多，另有电报、广播、互联网的捷径。
//   数值与规则见 ConnectivityRules(调参在 Technology/TechTree.json 的 connectivity 一节)，计算见 ConnectivityJob。
// 怎么算：每轮(一年一次；城增减、无人区、路网、城墙变了最早三个月后、二十秒后提前一轮)——
//   ① 主线程按毫秒切片取快照：地块(地形类、路等级、城墙关门、没修路的无人区)、城(城心地块、政权、文化、码头、海岸)、
//      各文化与政权的出行本事(航海档、驿传、工业化、摩托化、航空、通信档、习性、列岛、衰减长度)、要寻路的城对
//      (原版邻城、只隔无人区的邻城、同一块陆地上 64 格内的城；地块没变、两头城心没挪的沿用上一轮的地形剖面)；
//   ② 后台线程一口气算完(开不起来、或卡住半分钟以上时，改在主线程每帧算两毫秒)；
//   ③ 算完在主线程换上新结果，Version 加一。不存档：读档后从头算，几秒内 Ready 为假，各机制照旧(接壤、二值接触)。
// 接口：Strength/LearnStrength/Isolation/Contact(政权之间)、CultureStrength/CultureLearn/CultureContact(文化之间)、
//   Cost/NeighbourStrength/Near(城与城)、DecayLength/InfoLength、Describe(悬停文字)、EdgeCostsFor(教化用)。
//   "Learn"(学的一方 ← 教的一方)：两边谁走得过去都算，只有列岛视距放宽渡过去的那段只算列岛那一边(保住 D2"日本能出岛"的口径)。
// 一档接入：技术扩散(TechnologySystem.RebuildContacts)、制度吸收(InstitutionSystem.BuildCultureContacts，新增远播渠道)、
//   理念传播与外国理念压力(IdeologySpreadSystem、PublicOpinionSystem、IdeologyPopulationSystem.ContactCity、LandmarkBookSystem)、
//   教化(CultureRadiation：海路按航海档不再看战舰，路分等级，驿传，沙漠冻土无人区对消息 ×2)。
//   朝贡只认接壤、宣战闸(WarReach)不变；宗教二档再接。
// 二档接入(用户 2026-11-06 同意"先信息类、再贸易迁徙")：GoodsRoute/GoodsRoutes/Freight(商品市场连通、运费、过境、封路)、
//   NearCities/NearCost/ClosestOf/StrengthOf(迁徙、国内市场、经济圈、难民、空城安置、开放移民与驱逐、市易传教)，
//   都查本轮算好的表，主线程不按次寻路(难民、殖民这类偶发的才按需算一次)。
// 三档接入(外交与战争)：CanContact/ContactStrength(议婚、通好、结盟、外交界面)、PeopleCost/WarTargetOrder(挑战争目标排远近)、
//   NearLandCost(宣战闸的陆上近邻，见 WarReach.LandNear)；都是查表。
public static class ConnectivitySystem
{
    private enum Phase { Idle, Zones, Cities, Links, Groups, Compute }

    private const string LogTag = "[EmpireCraft][通达]";
    // 读档后等路网规划最多几秒(真实时间)；提前一轮至少隔几个游戏月、几秒真实时间；后台卡住多久改主线程
    private const float RoadWaitSeconds = 20f, EarlyRealSeconds = 20f, StallSeconds = 30f;
    private const int EarlyMonths = 3;
    private const int ZoneBatch = 64;
    private const int CityCacheLimit = 256;

    private static Phase _phase = Phase.Idle;
    private static object _world;
    private static float _worldAt;
    private static ConnectivityResult _result;
    private static ConnectivityJob _job;
    private static ConnectivityZoneGrid _grid = new();
    private static int _generation;
    // 地块快照内容的全局版本(换一份快照数组也不重号，城际剖面沿用表据此判断)
    private static int _revisionCounter;
    private static double _lastPass = -1d;
    private static float _lastPassReal = float.NegativeInfinity;
    private static int _wildVersion = int.MinValue, _roadVersion = int.MinValue, _wallVersion = int.MinValue, _devVersion = int.MinValue;
    private static int _cityCount = -1;
    private static bool _forcePass;
    // 快照的工作状态
    private static int _cursor;
    private static List<TileZone> _zones;
    private static bool _zonesChanged, _needNeighbours;
    private static int[][] _nbrBuild;
    private static readonly List<City> PassCities = new();
    private static readonly List<ConnectivityNode> PassNodes = new();
    private static readonly List<City> PassNodeCities = new();
    private static readonly List<string> PassCultures = new();
    private static readonly Dictionary<string, int> PassCultureIndex = new(StringComparer.Ordinal);
    private static readonly List<Kingdom> PassRealms = new();
    private static readonly Dictionary<long, int> PassRealmIndex = new();
    private static readonly List<ConnectivityLandLink> PassLinks = new();
    private static readonly HashSet<(long, long)> PassPairs = new();
    private static readonly Dictionary<(int, int), List<int>> PassGrid = new();
    private static ConnectivityTravel[] _passCultureIdeas, _passRealmIdeas, _passRealmPeople;
    private static int[][] _passRealmEnemies;
    // 本轮图上各点对应的城(换上结果时记下，查表时直接取)
    private static City[] _nodeCities = Array.Empty<City>();
    private static readonly List<ConnectivityNeighbour> NearBuffer = new();
    private static double _snapshotMs;
    private static float _computeStarted;
    private static int _lastProgress;
    private static float _lastProgressAt;
    // 城际剖面沿用表：两城 → (算的时候的地块版本、两头城心地块、剖面)
    private static readonly Dictionary<(long, long), (int revision, int zoneA, int zoneB, ConnectivityEdge edge)> EdgeCache = new();
    // 主线程按需算的单城最短路(每轮清空)
    private static readonly Dictionary<(int node, int mode), Dictionary<int, float>> CityPaths = new();

    // 后台线程：开不起来或卡住就改主线程切片
    private static Thread _thread;
    private static readonly AutoResetEvent Wake = new(false);
    private static ConnectivityJob _queued;
    private static bool _inline;

    public static bool Ready => _result != null && Config.enabled;
    public static int Version { get; private set; }

    public static ConnectivityConfig Config
    {
        get
        {
            ConnectivityConfig config = TechnologySystem.Config?.connectivity;
            return config ?? (_fallback ??= new ConnectivityConfig());
        }
    }

    private static ConnectivityConfig _fallback;

    public static void ResetWorldState()
    {
        _job?.Cancel();
        _job = null;
        _result = null;
        _grid = new ConnectivityZoneGrid();
        _phase = Phase.Idle;
        _world = null;
        _lastPass = -1d;
        _lastPassReal = float.NegativeInfinity;
        _wildVersion = _roadVersion = _wallVersion = _devVersion = int.MinValue;
        _cityCount = -1;
        _forcePass = false;
        _zones = null;
        _nbrBuild = null;
        ClearPass();
        EdgeCache.Clear();
        CityPaths.Clear();
        _nodeCities = Array.Empty<City>();
        Volatile.Write(ref _queued, null);
        Version++;
    }

    private static void ClearPass()
    {
        PassCities.Clear();
        PassNodes.Clear();
        PassNodeCities.Clear();
        PassCultures.Clear();
        PassCultureIndex.Clear();
        PassRealms.Clear();
        PassRealmIndex.Clear();
        PassLinks.Clear();
        PassPairs.Clear();
        PassGrid.Clear();
        _passCultureIdeas = _passRealmIdeas = _passRealmPeople = null;
        _passRealmEnemies = null;
        _cityNode = null;
        _cursor = 0;
    }

    #region 结算

    // 每帧(EmpireCraftStrategicScheduler)
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.cities?.list == null || world.zone_calculator?.zones == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
            _worldAt = UnityEngine.Time.realtimeSinceStartup;
        }
        if (!Config.enabled) return;
        try
        {
            using (FrameProfiler.Measure("通达层"))
            {
                if (_phase == Phase.Compute)
                {
                    Compute();
                    return;
                }
                if (!HelperFunc.SimulationFrameBudget.HasTime) return;
                if (_phase == Phase.Idle)
                {
                    if (!PassDue(world)) return;
                    BeginPass(world);
                }
                var clock = Stopwatch.StartNew();
                double budget = Math.Max(0.25d, Config.frame_ms);
                while (_phase is Phase.Zones or Phase.Cities or Phase.Links or Phase.Groups &&
                       clock.Elapsed.TotalMilliseconds < budget)
                    SnapshotStep(clock, budget);
                _snapshotMs += clock.Elapsed.TotalMilliseconds;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 结算失败: {exception.Message}");
            _job?.Cancel();
            _job = null;
            // 后台线程可能还在读这份快照：另起一份
            _grid = new ConnectivityZoneGrid();
            _phase = Phase.Idle;
            _lastPass = world.getCurWorldTime();
            _lastPassReal = UnityEngine.Time.realtimeSinceStartup;
        }
    }

    private static bool PassDue(MapBox world)
    {
        float real = UnityEngine.Time.realtimeSinceStartup;
        double now = world.getCurWorldTime();
        if (_result == null && _lastPass < 0d)
            return RoadNetwork.RoadPlansComplete || real - _worldAt >= RoadWaitSeconds;
        if (_forcePass) return true;
        if (_lastPass < 0d || now < _lastPass || Date.getYearsSince(_lastPass) >= Config.pass_years) return true;
        bool changed = WildernessSystem.Version != _wildVersion || RoadNetwork.Version != _roadVersion ||
                       MapWallSystem.Version != _wallVersion || WildernessDevelopmentSystem.Version != _devVersion ||
                       world.cities.list.Count != _cityCount;
        return changed && Date.getMonthsSince(_lastPass) >= EarlyMonths && real - _lastPassReal >= EarlyRealSeconds;
    }

    private static void BeginPass(MapBox world)
    {
        _forcePass = false;
        _lastPass = world.getCurWorldTime();
        _lastPassReal = UnityEngine.Time.realtimeSinceStartup;
        _wildVersion = WildernessSystem.Version;
        _roadVersion = RoadNetwork.Version;
        _wallVersion = MapWallSystem.Version;
        _devVersion = WildernessDevelopmentSystem.Version;
        _cityCount = world.cities.list.Count;
        ClearPass();
        _snapshotMs = 0d;
        _zones = world.zone_calculator.zones;
        int size = 0;
        foreach (TileZone zone in _zones)
            if (zone != null && zone.id >= size) size = zone.id + 1;
        _needNeighbours = _grid.Resize(size) || _grid.Nbr.Length == 0;
        _nbrBuild = _needNeighbours ? new int[size][] : null;
        _zonesChanged = false;
        _phase = Phase.Zones;
    }

    private static void SnapshotStep(Stopwatch clock, double budget)
    {
        switch (_phase)
        {
            case Phase.Zones:
                for (int i = 0; i < ZoneBatch && _cursor < _zones.Count; i++) SnapshotZone(_zones[_cursor++]);
                if (_cursor < _zones.Count) return;
                if (_needNeighbours) BuildNeighbours();
                if (_zonesChanged) _grid.Revision = ++_revisionCounter;
                PassCities.AddRange(World.world.cities.list);
                _cursor = 0;
                _phase = Phase.Cities;
                return;
            case Phase.Cities:
                for (int i = 0; i < 16 && _cursor < PassCities.Count; i++) SnapshotCity(PassCities[_cursor++]);
                if (_cursor < PassCities.Count) return;
                _cityNode = new Dictionary<long, int>(PassNodes.Count);
                for (int i = 0; i < PassNodes.Count; i++) _cityNode[PassNodes[i].City] = i;
                _cursor = 0;
                _phase = Phase.Links;
                return;
            case Phase.Links:
                for (int i = 0; i < 16 && _cursor < PassNodes.Count; i++) CollectLinks(_cursor++);
                if (_cursor < PassNodes.Count) return;
                _cursor = 0;
                _passCultureIdeas = new ConnectivityTravel[PassCultures.Count];
                _passRealmIdeas = new ConnectivityTravel[PassRealms.Count];
                _passRealmPeople = new ConnectivityTravel[PassRealms.Count];
                _passRealmEnemies = new int[PassRealms.Count][];
                _phase = Phase.Groups;
                return;
            case Phase.Groups:
                // 先各文化、再各政权，每步 8 个(查科技不便宜，大图几百个政权分几帧取)
                for (int i = 0; i < 8 && _cursor < PassCultures.Count + PassRealms.Count; i++, _cursor++)
                {
                    if (_cursor < PassCultures.Count)
                    {
                        _passCultureIdeas[_cursor] = TravelOf(PassCultures[_cursor], ConnectivityMode.Ideas);
                        continue;
                    }
                    int realm = _cursor - PassCultures.Count;
                    _passRealmIdeas[realm] = TravelOf(PassRealms[realm], ConnectivityMode.Ideas);
                    _passRealmPeople[realm] = TravelOf(PassRealms[realm], ConnectivityMode.People);
                    _passRealmEnemies[realm] = EnemiesOf(PassRealms[realm]);
                }
                if (_cursor < PassCultures.Count + PassRealms.Count) return;
                StartCompute();
                return;
        }
    }

    private static void SnapshotZone(TileZone zone)
    {
        if (zone == null || zone.id < 0 || zone.id >= _grid.Count) return;
        int id = zone.id;
        byte cls;
        if (zone.world_edge || SeaRegionSystem.IsSea(zone)) cls = ConnectivityRules.Blocked;
        else
        {
            FrontTerrain terrain = TerrainSystem.Of(zone, out bool impassable);
            cls = impassable ? (byte)ConnectivityRules.Ridge : TerrainClass(terrain);
        }
        byte road = (byte)Math.Max(0, Math.Min(ConnectivityRules.RoadGrades - 1, RoadNetwork.RoadGrade(zone)));
        int wallLevel = TerrainResistance.WallLevel(zone);
        byte wall = wallLevel == 2 ? (byte)2 : wallLevel == 1 ? (byte)1 : (byte)0;
        byte wild = 0;
        int region = -1;
        if (zone.city == null && !WildernessPassage.PassableByRoad(zone))
        {
            WildKind kind = WildernessSystem.KindOf(zone);
            if (kind != WildKind.None)
            {
                wild = (byte)kind;
                region = WildernessSystem.RegionOf(zone)?.Id ?? -1;
            }
        }
        if (_grid.Class[id] != cls || _grid.Road[id] != road || _grid.Wall[id] != wall || _grid.Wild[id] != wild ||
            _grid.WildRegion[id] != region)
        {
            _grid.Class[id] = cls;
            _grid.Road[id] = road;
            _grid.Wall[id] = wall;
            _grid.Wild[id] = wild;
            _grid.WildRegion[id] = region;
            _zonesChanged = true;
        }
        _grid.X[id] = (short)zone.x;
        _grid.Y[id] = (short)zone.y;
        if (_nbrBuild == null) return;
        TileZone[] neighbours = zone.neighbours ?? Array.Empty<TileZone>();
        var list = new int[neighbours.Length];
        int count = 0;
        foreach (TileZone next in neighbours)
            if (next != null && next.id >= 0 && next.id < _grid.Count) list[count++] = next.id;
        if (count != list.Length) Array.Resize(ref list, count);
        _nbrBuild[id] = list;
    }

    private static byte TerrainClass(FrontTerrain terrain) => terrain switch
    {
        FrontTerrain.Forest => ConnectivityRules.Forest,
        FrontTerrain.Hills => ConnectivityRules.Hills,
        FrontTerrain.Desert => ConnectivityRules.Desert,
        FrontTerrain.Snow => ConnectivityRules.Snow,
        FrontTerrain.Swamp => ConnectivityRules.Swamp,
        FrontTerrain.Jungle => ConnectivityRules.Jungle,
        FrontTerrain.Mountain => ConnectivityRules.Mountain,
        FrontTerrain.River => ConnectivityRules.River,
        FrontTerrain.Urban => ConnectivityRules.Urban,
        _ => ConnectivityRules.Plains
    };

    private static void BuildNeighbours()
    {
        int count = _grid.Count;
        var start = new int[count + 1];
        for (int i = 0; i < count; i++) start[i + 1] = start[i] + (_nbrBuild[i]?.Length ?? 0);
        var flat = new int[start[count]];
        for (int i = 0; i < count; i++)
            if (_nbrBuild[i] != null) Array.Copy(_nbrBuild[i], 0, flat, start[i], _nbrBuild[i].Length);
        _grid.NbrStart = start;
        _grid.Nbr = flat;
        _nbrBuild = null;
        _needNeighbours = false;
        _zonesChanged = true;
    }

    private static bool ValidCity(City city) =>
        city?.data != null && !city.isRekt() && city.kingdom?.data != null && !city.kingdom.isRekt() && !city.kingdom.wild &&
        city.zones != null && city.zones.Count > 0 && city.getTile() != null && !AncientWarfareCompatibility.OwnsObject(city);

    private static void SnapshotCity(City city)
    {
        if (!ValidCity(city)) return;
        TileZone center = AdministrativeControlSystem.CenterZone(city);
        if (center == null || center.id < 0 || center.id >= _grid.Count) return;
        Kingdom realm = RealmOf(city.kingdom);
        if (realm == null) return;
        if (!PassRealmIndex.TryGetValue(realm.id, out int realmIndex))
        {
            realmIndex = PassRealms.Count;
            PassRealmIndex[realm.id] = realmIndex;
            PassRealms.Add(realm);
        }
        string culture = TechnologySystem.GetCultureOf(city);
        int cultureIndex = -1;
        if (CultureService.IsValidCulture(culture) && !PassCultureIndex.TryGetValue(culture, out cultureIndex))
        {
            cultureIndex = PassCultures.Count;
            PassCultureIndex[culture] = cultureIndex;
            PassCultures.Add(culture);
        }
        var node = new ConnectivityNode
        {
            City = city.id, Zone = center.id, Realm = realmIndex, Culture = cultureIndex,
            X = city.city_center.x, Y = city.city_center.y,
            PortLevel = NavySystem.Port(city)?.asset?.upgrade_level ?? -1
        };
        if (node.PortLevel > Config.max_port_level) node.PortLevel = Config.max_port_level;
        SeaRegionSystem.Shore shore = SeaRegionSystem.ShoreOf(city);
        int points = shore.Points.Count;
        if (points > 0)
        {
            node.ShoreX = new float[points];
            node.ShoreY = new float[points];
            node.ShoreBasin = new int[points];
            for (int i = 0; i < points; i++)
            {
                node.ShoreX[i] = shore.Points[i].at.x;
                node.ShoreY[i] = shore.Points[i].at.y;
                node.ShoreBasin[i] = shore.Points[i].basin;
            }
        }
        PassNodes.Add(node);
        PassNodeCities.Add(city);
        WorldTile tile = city.getTile();
        float cell = Math.Max(8f, Config.link_tiles);
        var key = ((int)(tile.x / cell), (int)(tile.y / cell));
        if (!PassGrid.TryGetValue(key, out List<int> list)) PassGrid[key] = list = new List<int>();
        list.Add(PassNodes.Count - 1);
    }

    // 要寻路的城对：原版邻城、只隔无人区的邻城、同一块陆地上 link_tiles 格内的城
    private static void CollectLinks(int index)
    {
        City city = PassNodeCities[index];
        if (!ValidCity(city)) return;
        if (city.neighbours_cities != null)
            foreach (City other in city.neighbours_cities) AddLink(index, other);
        foreach (City other in WildernessNeighbours.Of(city)) AddLink(index, other);
        WorldTile tile = city.getTile();
        float cell = Math.Max(8f, Config.link_tiles), limit = Config.link_tiles * Config.link_tiles;
        int cx = (int)(tile.x / cell), cy = (int)(tile.y / cell);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (!PassGrid.TryGetValue((cx + dx, cy + dy), out List<int> list)) continue;
            foreach (int other in list)
            {
                if (other <= index) continue;
                City near = PassNodeCities[other];
                WorldTile at = near.getTile();
                if (at == null) continue;
                float ox = at.x - tile.x, oy = at.y - tile.y;
                if (ox * ox + oy * oy <= limit && tile.isSameIsland(at)) AddLink(index, near);
            }
        }
    }

    private static Dictionary<long, int> _cityNode;

    private static void AddLink(int index, City other)
    {
        if (other?.data == null || other.isRekt() || _cityNode == null || !_cityNode.TryGetValue(other.id, out int to) || to == index)
            return;
        ConnectivityNode a = PassNodes[Math.Min(index, to)], b = PassNodes[Math.Max(index, to)];
        var key = a.City < b.City ? (a.City, b.City) : (b.City, a.City);
        if (!PassPairs.Add(key)) return;
        var link = new ConnectivityLandLink { A = Math.Min(index, to), B = Math.Max(index, to), Key = key };
        int zoneA = key.Item1 == a.City ? a.Zone : b.Zone, zoneB = key.Item1 == a.City ? b.Zone : a.Zone;
        if (EdgeCache.TryGetValue(key, out var cached) && cached.revision == _grid.Revision && cached.zoneA == zoneA &&
            cached.zoneB == zoneB)
            link.Reuse = cached.edge;
        PassLinks.Add(link);
    }

    // 与这个政权交战的政权(下标)：货物路线绕开(战争封路)
    private static int[] EnemiesOf(Kingdom realm)
    {
        var list = new List<int>();
        try
        {
            foreach (Kingdom enemy in realm.getEnemiesKingdoms())
            {
                Kingdom root = RealmOf(enemy);
                if (root != null && PassRealmIndex.TryGetValue(root.id, out int index) && !list.Contains(index)) list.Add(index);
            }
        }
        catch (Exception)
        {
            // 敌国表取不到：不绕
        }
        return list.Count == 0 ? null : list.ToArray();
    }

    private static void StartCompute()
    {
        var realms = new long[PassRealms.Count];
        for (int i = 0; i < realms.Length; i++) realms[i] = PassRealms[i].id;
        _job = new ConnectivityJob(++_generation, Config, _grid, PassNodes.ToArray(), new List<ConnectivityLandLink>(PassLinks),
            PassCultures.ToArray(), _passCultureIdeas, realms, _passRealmIdeas, _passRealmPeople, _passRealmEnemies);
        _computeStarted = _lastProgressAt = UnityEngine.Time.realtimeSinceStartup;
        _lastProgress = 0;
        _phase = Phase.Compute;
        _cityNode = null;
        if (!_inline && Config.worker) StartWorker();
        if (!_inline && Config.worker && _thread != null)
        {
            Volatile.Write(ref _queued, _job);
            Wake.Set();
        }
    }

    private static void StartWorker()
    {
        if (_thread != null || _inline) return;
        try
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "EmpireCraft connectivity", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }
        catch (Exception exception)
        {
            _thread = null;
            _inline = true;
            LogService.LogWarning($"{LogTag} 后台线程开不起来，改在主线程分帧计算: {exception.Message}");
        }
    }

    private static void Run()
    {
        for (;;)
        {
            Wake.WaitOne();
            ConnectivityJob job = Interlocked.Exchange(ref _queued, null);
            if (job == null || job.Cancelled) continue;
            job.Advance(0d);
        }
    }

    private static void Compute()
    {
        ConnectivityJob job = _job;
        if (job == null)
        {
            _phase = Phase.Idle;
            return;
        }
        bool inline = _inline || !Config.worker || _thread == null;
        if (inline)
        {
            if (!HelperFunc.SimulationFrameBudget.HasTime) return;
            job.Advance(Math.Max(0.5d, Config.inline_frame_ms));
        }
        else if (!job.Finished)
        {
            float real = UnityEngine.Time.realtimeSinceStartup;
            int progress = job.Progress;
            if (progress != _lastProgress)
            {
                _lastProgress = progress;
                _lastProgressAt = real;
            }
            else if (real - _lastProgressAt > StallSeconds)
            {
                // 后台线程不干活(手机等平台)：这一轮作废，改在主线程从头算；快照数组另起一份，免得和卡住的线程抢
                _inline = true;
                job.Cancel();
                _job = null;
                _grid = new ConnectivityZoneGrid();
                _phase = Phase.Idle;
                _forcePass = true;
                LogService.LogWarning($"{LogTag} 后台线程 {StallSeconds:0} 秒没有进展，改在主线程分帧计算");
            }
            return;
        }
        if (!job.Finished) return;
        Commit(job);
    }

    private static void Commit(ConnectivityJob job)
    {
        _job = null;
        _phase = Phase.Idle;
        if (job.Cancelled || job.Generation != _generation) return;
        if (job.Error != null)
        {
            LogService.LogWarning($"{LogTag} 计算失败: {job.Error}");
            return;
        }
        var keep = new HashSet<(long, long)>(PassPairs);
        var stale = new List<(long, long)>();
        foreach ((long, long) key in EdgeCache.Keys)
            if (!keep.Contains(key)) stale.Add(key);
        foreach ((long, long) key in stale) EdgeCache.Remove(key);
        foreach (((long, long) key, ConnectivityEdge edge) in job.NewLand)
        {
            ConnectivityResult result = job.Result;
            int a = result.NodeOfCity.TryGetValue(key.Item1, out int ia) ? result.Nodes[ia].Zone : -1;
            int b = result.NodeOfCity.TryGetValue(key.Item2, out int ib) ? result.Nodes[ib].Zone : -1;
            EdgeCache[key] = (_grid.Revision, a, b, edge);
        }
        _result = job.Result;
        _nodeCities = PassNodeCities.Count == job.Result.Nodes.Length ? PassNodeCities.ToArray() : ResolveCities(job.Result);
        CityPaths.Clear();
        Version++;
        ConnectivityResult r = _result;
        LogService.LogInfo($"{LogTag} {r.Nodes.Length} 城、陆路 {r.LandEdges}(新寻路 {r.AStarRuns}、沿用 {r.Reused}、不通 {r.AStarFailed})、" +
                           $"海路 {r.SeaEdges}、文化 {r.Cultures.Length}、政权 {r.Realms.Length}、记下的往来 {r.Pairs}、货物路线 {r.GoodsPairs}；" +
                           $"取快照 {_snapshotMs:0.0} 毫秒(分帧)、计算 {r.ComputeMs:0.0} 毫秒({(_inline || !Config.worker || _thread == null ? "主线程分帧" : "后台线程")})");
        LogSummary(r);
        ClearPass();
    }

    private static City[] ResolveCities(ConnectivityResult result)
    {
        var cities = new City[result.Nodes.Length];
        for (int i = 0; i < cities.Length; i++) cities[i] = World.world?.cities?.get(result.Nodes[i].City);
        return cities;
    }

    // 日志：政权之间(消息口径)的往来等级、主障碍分布，及城最多的政权往来最密的几家
    private static void LogSummary(ConnectivityResult r)
    {
        try
        {
            var barriers = new List<string>();
            for (int i = 0; i < r.BarrierCounts.Length; i++)
                if (r.BarrierCounts[i] > 0) barriers.Add($"{BarrierText((ConnectivityBarrier)i)} {r.BarrierCounts[i]}");
            string levels = string.Join("、", new[]
            {
                $"{LM.Get("connectivity_level_close")} {r.LevelCounts[(int)ConnectivityLevel.Close]}",
                $"{LM.Get("connectivity_level_contact")} {r.LevelCounts[(int)ConnectivityLevel.Contact]}",
                $"{LM.Get("connectivity_level_heard")} {r.LevelCounts[(int)ConnectivityLevel.Heard]}"
            });
            LogService.LogInfo($"{LogTag} 政权之间(消息)：{levels}；主障碍：{string.Join("、", barriers)}");
            if (r.LargestRealm < 0) return;
            Kingdom largest = World.world.kingdoms.get(r.Realms[r.LargestRealm]);
            if (largest?.data == null) return;
            var top = new List<KeyValuePair<int, ConnectivityPair>>(r.RealmIdeasOut[r.LargestRealm]);
            top.Sort((a, b) => b.Value.Strength.CompareTo(a.Value.Strength));
            var lines = new List<string>();
            foreach (KeyValuePair<int, ConnectivityPair> pair in top)
            {
                Kingdom other = World.world.kingdoms.get(r.Realms[pair.Key]);
                if (other?.data == null) continue;
                lines.Add($"{other.GetKingdomName()} {Describe(ConnectivityContact.Of(pair.Value, r.Config))}");
                if (lines.Count >= 6) break;
            }
            if (lines.Count > 0) LogService.LogInfo($"{LogTag} {largest.GetKingdomName()} 往来最密的：{string.Join("；", lines)}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 日志失败: {exception.Message}");
        }
    }

    #endregion

    #region 出行本事

    public static Kingdom RealmOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom?.data != null && !empire.CoreKingdom.isRekt())
            return empire.CoreKingdom;
        return kingdom;
    }

    // 科技树没开(或这个文化还没起用)时：按中古的样子(近海引航、驿传、文字印刷)，不把海、路一概当成石器时代
    private static readonly HashSet<string> InactiveTechs = new(StringComparer.Ordinal)
        { "writing", "printing", "masonry", "architecture", "postal_roads", "coastal_pilotage" };

    public static ConnectivityTravel TravelOf(string culture, ConnectivityMode mode)
    {
        Func<string, bool> has = TechnologySystem.IsActiveFor(culture)
            ? tech => TechnologySystem.HasTech(culture, tech)
            : tech => InactiveTechs.Contains(tech);
        int habit = !CultureService.IsValidCulture(culture) ? ConnectivityRules.Settled
            : culture.IsNomadCulture() ? ConnectivityRules.Nomadic
            : InstitutionSystem.GetCultureLine(culture) == "Arabic" ? ConnectivityRules.DesertWise
            : ConnectivityRules.Settled;
        bool open = !CultureService.IsValidCulture(culture) || InstitutionSystem.GetFeature(culture, "closed_country") <= 0f;
        return Build(mode, has, habit, CultureService.IsValidCulture(culture) && TianxiaSystem.IsArchipelago(culture), open, -1);
    }

    public static ConnectivityTravel TravelOf(Kingdom kingdom, ConnectivityMode mode)
    {
        Kingdom realm = RealmOf(kingdom);
        if (realm == null) return TravelOf("", mode);
        string culture = TechnologySystem.GetCultureOf(realm);
        Func<string, bool> has = TechnologySystem.IsActiveFor(culture)
            ? tech => TechnologySystem.RealmHasTech(realm, tech)
            : tech => InactiveTechs.Contains(tech);
        int habit = KingdomExtension.IsTrueNomad(realm) ? ConnectivityRules.Nomadic
            : CultureService.IsValidCulture(culture) && InstitutionSystem.GetCultureLine(culture) == "Arabic" ? ConnectivityRules.DesertWise
            : ConnectivityRules.Settled;
        bool archipelago = CultureService.IsValidCulture(culture) && TianxiaSystem.IsArchipelago(culture);
        return Build(mode, has, habit, archipelago, MediaSystem.IsOpen(realm), MediaSystem.CommLevel(realm));
    }

    private static ConnectivityTravel Build(ConnectivityMode mode, Func<string, bool> has, int habit, bool archipelago, bool open, int comm)
    {
        ConnectivityConfig config = Config;
        if (comm < 0)
        {
            comm = 0;
            foreach (string tech in new[] { "telegraph", "radio", "internet" })
            {
                if (!has(tech)) break;
                comm++;
            }
        }
        bool ideas = mode == ConnectivityMode.Ideas;
        return new ConnectivityTravel
        {
            Mode = ideas ? ConnectivityMode.Ideas : ConnectivityMode.People,
            Band = ConnectivityRules.Band(config.navigation_bands, has),
            Archipelago = archipelago,
            Habit = habit,
            Postal = has("postal_roads"),
            Industrial = has("industrialization"),
            Motor = has("motorization"),
            Aviation = has("aviation"),
            FreightFactor = ConnectivityRules.FreightTech(config.freight_techs, has),
            Comm = comm,
            Open = open,
            Length = ideas
                ? ConnectivityRules.Length(config.info_length_base, config.info_length_techs, has)
                : ConnectivityRules.Length(config.people_length_base, config.people_length_techs, has)
        };
    }

    // 消息的衰减长度(教化用；同教化原来的口径：本文化研究了的科技)
    public static float InfoLength(string culture)
    {
        ConnectivityConfig config = Config;
        if (string.IsNullOrEmpty(culture)) return config.info_length_base;
        return ConnectivityRules.Length(config.info_length_base, config.info_length_techs, tech => TechnologySystem.HasTech(culture, tech));
    }

    public static float DecayLength(Kingdom kingdom, ConnectivityMode mode) => TravelOf(kingdom, mode).Length * Config.length_scale;

    #endregion

    #region 查询：政权之间

    private static Dictionary<int, ConnectivityPair>[] RealmOuts(ConnectivityMode mode) =>
        mode == ConnectivityMode.Ideas ? _result.RealmIdeasOut : _result.RealmPeopleOut;

    private static bool TryRealms(Kingdom from, Kingdom to, out int a, out int b, out bool same)
    {
        a = b = -1;
        same = false;
        if (!Ready) return false;
        Kingdom x = RealmOf(from), y = RealmOf(to);
        if (x == null || y == null) return false;
        if (x == y)
        {
            same = true;
            return true;
        }
        return _result.RealmIndex.TryGetValue(x.id, out a) && _result.RealmIndex.TryGetValue(y.id, out b);
    }

    // 这个国(按所在政权)这一轮算进了通达层
    public static bool Knows(Kingdom kingdom)
    {
        Kingdom realm = Ready ? RealmOf(kingdom) : null;
        return realm != null && _result.RealmIndex.ContainsKey(realm.id);
    }

    // from 一方(按它的本事)到 to 的强度 0~1；还没算(Ready 为假、新立的国)返回 -1。telecom 为假时不算电报、广播、航空
    public static float Strength(Kingdom from, Kingdom to, ConnectivityMode mode = ConnectivityMode.Ideas, bool telecom = true)
    {
        if (!TryRealms(from, to, out int a, out int b, out bool same)) return -1f;
        if (same) return 1f;
        return RealmOuts(mode)[a].TryGetValue(b, out ConnectivityPair pair) ? telecom ? pair.Strength : pair.PathStrength : 0f;
    }

    // 学的一方 ← 教的一方(两边谁过得去都算；只因列岛视距放宽才渡得过的，只算列岛那一边)
    public static float LearnStrength(Kingdom learner, Kingdom source, bool telecom = true, ConnectivityMode mode = ConnectivityMode.Ideas)
    {
        if (!TryRealms(learner, source, out int a, out int b, out bool same)) return -1f;
        if (same) return 1f;
        Dictionary<int, ConnectivityPair>[] outs = RealmOuts(mode);
        return Learn(outs[a], outs[b], a, b, telecom);
    }

    private static float Learn(Dictionary<int, ConnectivityPair> learnerOut, Dictionary<int, ConnectivityPair> sourceOut, int learner,
        int source, bool telecom)
    {
        float best = 0f;
        if (learnerOut.TryGetValue(source, out ConnectivityPair own)) best = telecom ? own.Strength : own.PathStrength;
        if (sourceOut.TryGetValue(learner, out ConnectivityPair theirs) && !theirs.Has(ConnectivityPair.ArchipelagoOnly))
            best = Math.Max(best, telecom ? theirs.Strength : theirs.PathStrength);
        return best;
    }

    // 隔绝度 0~1(1 完全隔绝)；还没算返回 -1
    public static float Isolation(Kingdom a, Kingdom b, ConnectivityMode mode = ConnectivityMode.Ideas)
    {
        float x = Strength(a, b, mode), y = Strength(b, a, mode);
        if (x < 0f || y < 0f) return -1f;
        return 1f - Math.Max(x, y);
    }

    public static ConnectivityContact Contact(Kingdom from, Kingdom to, ConnectivityMode mode = ConnectivityMode.Ideas)
    {
        if (!TryRealms(from, to, out int a, out int b, out bool same)) return default;
        if (same) return ConnectivityContact.Same;
        Dictionary<int, ConnectivityPair>[] outs = RealmOuts(mode);
        return outs[a].TryGetValue(b, out ConnectivityPair pair) ? ConnectivityContact.Of(pair, Config) : ConnectivityContact.Unreachable;
    }

    #endregion

    #region 查询：文化之间

    private static bool TryCultures(string from, string to, out int a, out int b)
    {
        a = b = -1;
        return Ready && from != null && to != null && _result.CultureIndex.TryGetValue(from, out a) &&
               _result.CultureIndex.TryGetValue(to, out b);
    }

    // 文化 from(按它的本事)到 to 的强度；同一文化 1，还没算(或这一轮地图上没有它的城)返回 -1
    public static float CultureStrength(string from, string to, bool telecom = true)
    {
        if (Ready && from != null && from == to) return 1f;
        if (!TryCultures(from, to, out int a, out int b)) return -1f;
        return _result.CultureOut[a].TryGetValue(b, out ConnectivityPair pair) ? telecom ? pair.Strength : pair.PathStrength : 0f;
    }

    // 文化 learner 从 source 学的通达(见 LearnStrength)
    public static float CultureLearn(string learner, string source, bool telecom = true)
    {
        if (Ready && learner != null && learner == source) return 1f;
        if (!TryCultures(learner, source, out int a, out int b)) return -1f;
        return Learn(_result.CultureOut[a], _result.CultureOut[b], a, b, telecom);
    }

    public static ConnectivityContact CultureContact(string from, string to)
    {
        if (Ready && from != null && from == to) return ConnectivityContact.Same;
        if (!TryCultures(from, to, out int a, out int b)) return default;
        return _result.CultureOut[a].TryGetValue(b, out ConnectivityPair pair) ? ConnectivityContact.Of(pair, Config) : ConnectivityContact.Unreachable;
    }

    #endregion

    #region 查询：城与城

    private static bool TryNode(City city, out int node)
    {
        node = -1;
        return Ready && city?.data != null && _result.NodeOfCity.TryGetValue(city.id, out node);
    }

    private static ConnectivityTravel TravelAt(int node, ConnectivityMode mode)
    {
        int realm = _result.Nodes[node].Realm;
        if (realm >= 0 && realm < _result.Realms.Length)
            return mode == ConnectivityMode.Ideas ? _result.RealmIdeas[realm] : _result.RealmPeople[realm];
        return TravelOf("", mode);
    }

    private static Dictionary<int, float> PathsFrom(int node, ConnectivityMode mode)
    {
        int modeKey = mode == ConnectivityMode.Ideas ? 1 : 0;
        if (CityPaths.TryGetValue((node, modeKey), out Dictionary<int, float> paths)) return paths;
        ConnectivityTravel travel = TravelAt(node, mode);
        float cutoff = ConnectivityRules.CostFor(Config.min_strength, travel.Length, Config);
        paths = _result.From(node, travel, cutoff);
        if (CityPaths.Count >= CityCacheLimit) CityPaths.Clear();
        CityPaths[(node, modeKey)] = paths;
        return paths;
    }

    // a 城(按所在政权的本事)走到 b 城的成本；+∞ 为走不到或远得不必算，-1 为还没算
    public static float Cost(City a, City b, ConnectivityMode mode = ConnectivityMode.People)
    {
        if (!TryNode(a, out int x) || !TryNode(b, out int y)) return -1f;
        if (x == y) return 0f;
        return PathsFrom(x, mode).TryGetValue(y, out float cost) ? cost : float.PositiveInfinity;
    }

    // 相邻两城之间的强度(直接相连的边，不必做最短路；没有直接相连时按最短路)；还没算返回 -1
    public static float NeighbourStrength(City a, City b, ConnectivityMode mode = ConnectivityMode.Ideas)
    {
        if (!TryNode(a, out int x) || !TryNode(b, out int y)) return -1f;
        if (x == y) return 1f;
        ConnectivityTravel travel = TravelAt(x, mode);
        float cost;
        int edge = _result.EdgeBetween(x, y);
        if (edge >= 0)
        {
            cost = _result.Costs(travel).cost[edge];
            if (!float.IsPositiveInfinity(cost) && _result.Nodes[x].Realm != _result.Nodes[y].Realm) cost += Config.border_cost;
            if (float.IsPositiveInfinity(cost)) cost = PathsFrom(x, mode).TryGetValue(y, out float path) ? path : cost;
        }
        else cost = PathsFrom(x, mode).TryGetValue(y, out float path) ? path : float.PositiveInfinity;
        return ConnectivityRules.Strength(cost, travel.Length, Config);
    }

    // 附近的城(按成本由近到远，至多 count 座，不含自己)；人员、货物口径直接查本轮记下的表
    public static List<(City city, float cost)> Near(City city, ConnectivityMode mode = ConnectivityMode.People, int count = 12)
    {
        var list = new List<(City, float)>();
        if (!TryNode(city, out int node)) return list;
        if (mode != ConnectivityMode.Ideas && node < _result.NearNodes.Length && _result.NearNodes[node] != null)
        {
            if (NearCities(city, NearBuffer) > 0)
                foreach (ConnectivityNeighbour near in NearBuffer)
                {
                    list.Add((near.City, near.Cost));
                    if (list.Count >= count) break;
                }
            return list;
        }
        var order = new List<KeyValuePair<int, float>>(PathsFrom(node, mode));
        order.Sort((a, b) => a.Value.CompareTo(b.Value));
        foreach (KeyValuePair<int, float> pair in order)
        {
            if (pair.Key == node) continue;
            City other = World.world?.cities?.get(_result.Nodes[pair.Key].City);
            if (other?.data == null || other.isRekt()) continue;
            list.Add((other, pair.Value));
            if (list.Count >= count) break;
        }
        return list;
    }

    #region 二档：查表(主线程不按次寻路)

    // 本城最近的城(人员口径，按成本由近到远，maxCost 以内)填进 into；返回个数，还没算(或这座城不在本轮图上)返回 -1
    public static int NearCities(City city, List<ConnectivityNeighbour> into, float maxCost = float.MaxValue)
    {
        into?.Clear();
        if (into == null || !TryNode(city, out int node) || node >= _result.NearNodes.Length || _result.NearNodes[node] == null) return -1;
        int[] ids = _result.NearNodes[node];
        float[] costs = _result.NearCosts[node];
        byte[] flags = _result.NearFlags[node];
        for (int i = 0; i < ids.Length; i++)
        {
            if (costs[i] > maxCost) break;
            City other = ids[i] < _nodeCities.Length ? _nodeCities[ids[i]] : null;
            if (other?.data == null || other.isRekt()) continue;
            into.Add(new ConnectivityNeighbour(other, costs[i], (flags[i] & 1) != 0));
        }
        return into.Count;
    }

    // from 的近邻表里 to 的成本：不在表里(远过 near_max_cost)为 +∞，还没算为 -1
    public static float NearCost(City from, City to, out bool bySea)
    {
        bySea = false;
        if (!TryNode(from, out int a) || !TryNode(to, out int b)) return -1f;
        if (a == b) return 0f;
        if (a >= _result.NearNodes.Length || _result.NearNodes[a] == null) return -1f;
        int[] ids = _result.NearNodes[a];
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] != b) continue;
            bySea = (_result.NearFlags[a][i] & 1) != 0;
            return _result.NearCosts[a][i];
        }
        return float.PositiveInfinity;
    }

    // 这些城里离 from 成本最低、走得到的一座(先查近邻表，没有再按需算一次最短路——只给难民、殖民这类偶发的用)；
    // 一座也走不到返回 null；还没算返回 null 且 cost 为 -1
    public static City ClosestOf(City from, IEnumerable<City> candidates, ConnectivityMode mode, out float cost)
    {
        cost = -1f;
        if (!TryNode(from, out int node) || candidates == null) return null;
        var wanted = new Dictionary<int, City>();
        foreach (City city in candidates)
            if (city?.data != null && city != from && !city.isRekt() && _result.NodeOfCity.TryGetValue(city.id, out int other))
                wanted[other] = city;
        cost = float.PositiveInfinity;
        if (wanted.Count == 0) return null;
        City best = null;
        if (mode != ConnectivityMode.Ideas && node < _result.NearNodes.Length && _result.NearNodes[node] != null)
        {
            int[] ids = _result.NearNodes[node];
            for (int i = 0; i < ids.Length; i++)
                if (wanted.TryGetValue(ids[i], out City city))
                {
                    cost = _result.NearCosts[node][i];
                    return city;
                }
        }
        foreach (KeyValuePair<int, float> pair in PathsFrom(node, mode))
            if (pair.Value < cost && wanted.TryGetValue(pair.Key, out City city))
            {
                cost = pair.Value;
                best = city;
            }
        return best;
    }

    // 从 from 出发走了 cost 的强度(按 from 所在政权的衰减长度)
    public static float StrengthOf(float cost, City from, ConnectivityMode mode = ConnectivityMode.People)
    {
        if (cost < 0f) return -1f;
        float length = TryNode(from, out int node) ? TravelAt(node, mode).Length : TravelOf(from?.kingdom, mode).Length;
        return ConnectivityRules.Strength(cost, length, Config);
    }

    // 本城所在政权人员的衰减长度、航海档(本轮记下的；不在图上的现算)
    public static float PeopleLength(City city) =>
        TryNode(city, out int node) ? TravelAt(node, ConnectivityMode.People).Length : TravelOf(city?.kingdom, ConnectivityMode.People).Length;

    public static int Band(City city) =>
        TryNode(city, out int node) ? TravelAt(node, ConnectivityMode.People).Band : TravelOf(city?.kingdom, ConnectivityMode.People).Band;

    // 运输科技给的运费系数(工业化 0.4、摩托化 0.25；没有为 1)
    public static float FreightFactor(Kingdom kingdom)
    {
        Kingdom realm = RealmOf(kingdom);
        if (realm != null && Ready && _result.RealmIndex.TryGetValue(realm.id, out int index)) return _result.RealmPeople[index].FreightFactor;
        return TravelOf(kingdom, ConnectivityMode.People).FreightFactor;
    }

    public static float Freight(float cost, string tier, float techFactor) => ConnectivityRules.Freight(cost, tier, techFactor, Config);

    // from 一方(按它的本事、绕开它的敌国)运货到 to 的路线；没有记下(远过 goods_max_cost、或被战争封死)时 Known 为假
    public static ConnectivityRoute GoodsRoute(Kingdom from, Kingdom to)
    {
        if (!TryRealms(from, to, out int a, out int b, out bool same)) return default;
        if (same) return new ConnectivityRoute(RealmOf(to), 0f, false, null);
        return _result.RealmGoodsOut[a].TryGetValue(b, out ConnectivityPair pair) ? Route(b, pair) : default;
    }

    // from 一方的全部货物路线(商品市场每年连通时用)
    public static List<ConnectivityRoute> GoodsRoutes(Kingdom from)
    {
        var list = new List<ConnectivityRoute>();
        Kingdom realm = RealmOf(from);
        if (realm == null || !Ready || !_result.RealmIndex.TryGetValue(realm.id, out int index)) return list;
        foreach (KeyValuePair<int, ConnectivityPair> pair in _result.RealmGoodsOut[index])
        {
            ConnectivityRoute route = Route(pair.Key, pair.Value);
            if (route.Known) list.Add(route);
        }
        return list;
    }

    private static ConnectivityRoute Route(int target, in ConnectivityPair pair)
    {
        Kingdom to = World.world?.kingdoms?.get(_result.Realms[target]);
        if (to?.data == null || to.isRekt()) return default;
        long[] third = null;
        if (pair.Third != null)
        {
            third = new long[pair.Third.Length];
            for (int i = 0; i < third.Length; i++) third[i] = _result.Realms[pair.Third[i]];
        }
        return new ConnectivityRoute(to, pair.Cost, pair.Has(ConnectivityPair.BySea), third);
    }

    #endregion

    #region 三档：外交与战争(查表)

    // 发起一方(按它的本事：航海档、路、驿传，不要战舰)派使节到对方的强度；还没算返回 -1
    public static float ContactStrength(Kingdom initiator, Kingdom target) => Strength(initiator, target, ConnectivityMode.People);

    // 外交够得着(议婚、通好、结盟)：强度不低于 diplomacy_min_strength；还没算返回 null(调用方照旧)
    public static bool? CanContact(Kingdom initiator, Kingdom target)
    {
        float strength = ContactStrength(initiator, target);
        return strength < 0f ? null : strength >= Config.diplomacy_min_strength;
    }

    // 两边谁派使节都行(通好这类没有发起一方的)：两个方向取好的
    public static bool? CanContactEither(Kingdom a, Kingdom b)
    {
        float x = ContactStrength(a, b), y = ContactStrength(b, a);
        if (x < 0f || y < 0f) return null;
        return Math.Max(x, y) >= Config.diplomacy_min_strength;
    }

    // from 一方人员走到 to 的成本(政权之间各取最近的一对城)：同一政权 0；远得不必算(强度不到 min_strength)为 +∞；还没算 -1
    public static float PeopleCost(Kingdom from, Kingdom to)
    {
        if (!TryRealms(from, to, out int a, out int b, out bool same)) return -1f;
        if (same) return 0f;
        return _result.RealmPeopleOut[a].TryGetValue(b, out ConnectivityPair pair) ? pair.Cost : float.PositiveInfinity;
    }

    // 挑战争目标排远近用：通达层算好后按人员成本(远得不必算的排在后面、再按直线)，没算好时照旧按直线距离 fallback
    public static float WarTargetOrder(Kingdom from, Kingdom to, float fallback)
    {
        float cost = PeopleCost(from, to);
        if (cost < 0f) return Ready ? 1e5f + fallback : fallback;
        return float.IsPositiveInfinity(cost) ? 1e6f + fallback : cost;
    }

    // 两城之间的陆路成本(近邻表里不走海的那一项，两边都查，取低的)：都不在表里为 +∞；
    // 近邻表满了、表里最远的还不到 within(说明对方可能只是被更近的城挤出了表)或还没算，返回 -1(调用方照旧估算)
    public static float NearLandCost(City a, City b, float within)
    {
        if (!TryNode(a, out int x) || !TryNode(b, out int y)) return -1f;
        if (x == y) return 0f;
        float best = float.PositiveInfinity;
        bool saturated = false;
        foreach ((int from, int to) in new[] { (x, y), (y, x) })
        {
            if (from >= _result.NearNodes.Length || _result.NearNodes[from] == null) return -1f;
            int[] ids = _result.NearNodes[from];
            float[] costs = _result.NearCosts[from];
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == to && (_result.NearFlags[from][i] & 1) == 0 && costs[i] < best) best = costs[i];
            if (ids.Length >= Config.near_count && ids.Length > 0 && costs[ids.Length - 1] < within) saturated = true;
        }
        return float.IsPositiveInfinity(best) && saturated ? -1f : best;
    }

    #endregion

    // 教化(CultureRadiation)用：本轮的图、某文化(消息口径)走各条边的成本
    internal static ConnectivityResult Graph => Ready ? _result : null;

    internal static float[] EdgeCostsFor(ConnectivityResult result, string culture)
    {
        ConnectivityTravel travel = culture != null && result.CultureIndex.TryGetValue(culture, out int index)
            ? result.CultureIdeas[index]
            : TravelOf(culture ?? "", ConnectivityMode.Ideas);
        return result.Costs(travel).cost;
    }

    #endregion

    #region 文字

    // 悬停、日志：如"隔葱岭·往来(0.31)""电讯相通·紧密(0.92)"
    public static string Describe(ConnectivityContact contact)
    {
        if (!contact.Known) return LM.Get("connectivity_unknown");
        string reason;
        if (contact.ViaTelecom) reason = LM.Get("connectivity_via_telecom");
        else if (contact.ViaAir) reason = LM.Get("connectivity_via_air");
        else reason = BarrierText(contact.Main, contact.WildRegion);
        string level = LM.Get("connectivity_level_" + contact.Level.ToString().ToLowerInvariant());
        return string.Format(LM.Get("connectivity_contact_format"), reason, level, contact.Strength.ToString("0.00"));
    }

    public static string BarrierText(ConnectivityBarrier barrier, int wildRegion = -1)
    {
        if (wildRegion >= 0 && barrier is ConnectivityBarrier.Mountain or ConnectivityBarrier.Desert or ConnectivityBarrier.Tundra or
                ConnectivityBarrier.Jungle or ConnectivityBarrier.Swamp)
        {
            string name = WildernessSystem.DisplayName(WildernessSystem.ById(wildRegion));
            if (!string.IsNullOrEmpty(name)) return string.Format(LM.Get("connectivity_barrier_named"), name);
        }
        return LM.Get("connectivity_barrier_" + barrier.ToString().ToLowerInvariant());
    }

    #endregion
}

// 近邻表的一项(二档)
public readonly struct ConnectivityNeighbour
{
    public readonly City City;
    public readonly float Cost;
    public readonly bool BySea;

    public ConnectivityNeighbour(City city, float cost, bool bySea)
    {
        City = city;
        Cost = cost;
        BySea = bySea;
    }
}

// 一条货物路线(二档)：到哪个政权、成本、是否走海、沿途第三国(政权编号；没有为 null)
public readonly struct ConnectivityRoute
{
    public readonly Kingdom To;
    public readonly float Cost;
    public readonly bool BySea;
    public readonly long[] Third;
    public bool Known => To != null;
    public int ThirdCount => Third?.Length ?? 0;

    public ConnectivityRoute(Kingdom to, float cost, bool bySea, long[] third)
    {
        To = to;
        Cost = cost;
        BySea = bySea;
        Third = third;
    }
}

// 两方之间的通达(查询结果)
public readonly struct ConnectivityContact
{
    public readonly bool Known;
    public readonly float Cost, Strength;
    public readonly ConnectivityLevel Level;
    public readonly ConnectivityBarrier Main, Second;
    public readonly int WildRegion;
    public readonly bool BySea, Ocean, ViaThird, ViaTelecom, ViaAir, ArchipelagoOnly;

    private ConnectivityContact(bool known, float cost, float strength, ConnectivityLevel level, ConnectivityBarrier main,
        ConnectivityBarrier second, int wildRegion, byte flags)
    {
        Known = known;
        Cost = cost;
        Strength = strength;
        Level = level;
        Main = main;
        Second = second;
        WildRegion = wildRegion;
        BySea = (flags & ConnectivityPair.BySea) != 0;
        Ocean = (flags & ConnectivityPair.Ocean) != 0;
        ViaThird = (flags & ConnectivityPair.ViaThird) != 0;
        ViaTelecom = (flags & ConnectivityPair.ViaTelecom) != 0;
        ViaAir = (flags & ConnectivityPair.ViaAir) != 0;
        ArchipelagoOnly = (flags & ConnectivityPair.ArchipelagoOnly) != 0;
    }

    public static ConnectivityContact Same =>
        new(true, 0f, 1f, ConnectivityLevel.Close, ConnectivityBarrier.None, ConnectivityBarrier.None, -1, 0);

    public static ConnectivityContact Unreachable =>
        new(true, float.PositiveInfinity, 0f, ConnectivityLevel.Isolated, ConnectivityBarrier.Unreachable, ConnectivityBarrier.None, -1, 0);

    public static ConnectivityContact Of(in ConnectivityPair pair, ConnectivityConfig config) =>
        new(true, pair.Cost, pair.Strength, ConnectivityRules.Level(pair.Strength, config), pair.Main, pair.Second, pair.WildRegion, pair.Flags);
}
