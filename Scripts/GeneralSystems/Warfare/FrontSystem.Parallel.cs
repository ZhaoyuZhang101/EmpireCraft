using System;
using System.Collections.Generic;
using System.Diagnostics;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 陆上战线段多核算(用户 2026-11-14 "服务终端请一定要用多核优化，因为是纯数据"，选"先拆军团战线")：
// 大地图上每月逐国算陆上战线段(见 LandSectors：够不够得着、陆路搜索、可下手地块)占了军团战线的大半(实测第 2 档东亚每步 18 毫秒)。
// 现在分派到一国、这一帧还没算过它那份时，把队里接下来几国一起拿出来，各签名一份，几个工作线程同时算：
//   · 地形层(主线程滚动重建，每帧花一点)：每块地过不过得去、是不是海、与各邻块之间隔不隔山脊；
//   · 归属层(主线程每批取一次)：每块地眼下归谁控制，本方与各国的敌友、借道；本方的城、要看的敌城与它们的位置；
//   · 工作线程只读这两层与世界里不会变的结构(地块的邻块、城的地块表)，不碰懒加载的缓存、不调 Unity 的接口；
//   · 主线程落地：陆路缓存、借道记录照顺序写回；只靠水师辐射才够得着的城(见 StraitReach)与它的可下手地块仍在主线程照原样算。
// 结果与逐国算一样(同一帧里的世界；城心每批现取、路线现查)；地形层只看地形，一年重建一轮(第 2 档大图一轮三四十毫秒)，地形变了至多慢这么久。
// 核对：VerifyParallelSectors 打开时每份再照原样算一遍，逐城逐块比，对不上的记下(独立服务器 --verify-fronts)
public static partial class FrontSystem
{
    public static bool ParallelSectors = true;
    public static bool VerifyParallelSectors;
    // 核对与统计(诊断用)
    public static int ParallelBatches, ParallelSides, VerifiedSides, VerifyMismatches;

    // 已算好的陆上战线段(按敌国签名，见 CachedSectors)：只在算出来的那一帧用
    private static readonly Dictionary<string, List<Sector>> PrefetchedLand = new();
    // 本月在打仗的国(见 _atWar)，查起来快些
    private static readonly HashSet<Kingdom> AtWarSet = new();

    private const byte FlagImpassable = 1, FlagSea = 2;
    private const byte RelFriendly = 1, RelHostile = 2, RelGrants = 4, RelWild = 8;

    #region 地形层

    // 当前可用的一份(重建完整才换上)与正在建的一份
    private static byte[] _terrainFlags, _cross4, _cross8;
    private static byte[] _nextFlags, _nextCross4, _nextCross8;
    private static int _layerCursor, _layerSize;
    private static object _layerWorld;
    private static int _layerZoneCount = -1;
    private static double _layerTouchedAt = -1d, _layerDoneAt = -1d;
    // 停了一个多月(太平时不建)：旧的那份作废，从头建；建完一轮隔一年再建下一轮(山、路、城心变得慢；第 2 档大图一轮一百来毫秒)
    private const double LayerIdleSeconds = 5d, LayerRefreshSeconds = 60d;
    // 诊断：上一轮建了多少块、花了几帧几毫秒
    public static int LayerZones, LayerFrames;
    public static double LayerMilliseconds;
    private static int _cycleFrames;
    private static double _cycleMilliseconds;

    private static void ResetParallel()
    {
        _terrainFlags = _cross4 = _cross8 = null;
        _nextFlags = _nextCross4 = _nextCross8 = null;
        _layerCursor = 0;
        _layerWorld = null;
        _layerZoneCount = -1;
        _layerTouchedAt = _layerDoneAt = -1d;
        PrefetchedLand.Clear();
        PendingPrefetch.Clear();
        AtWarSet.Clear();
    }

    // 每帧(CorpsSystem.Update)：在打仗时滚动重建地形层，每帧至多花这么多毫秒
    public static void ParallelTick()
    {
        if (!ParallelSectors || World.world?.zone_calculator?.zones == null) return;
        if (_atWar.Count == 0) return;
        List<TileZone> zones = World.world.zone_calculator.zones;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_layerWorld, World.world) || _layerZoneCount != zones.Count ||
            _layerTouchedAt >= 0d && (now < _layerTouchedAt || now - _layerTouchedAt > LayerIdleSeconds))
        {
            _terrainFlags = _cross4 = _cross8 = null;
            _nextFlags = null;
            _layerWorld = World.world;
            _layerZoneCount = zones.Count;
            _layerDoneAt = -1d;
        }
        _layerTouchedAt = now;
        // 现成的一份还新，不建
        if (_nextFlags == null && _terrainFlags != null && _layerDoneAt >= 0d && now >= _layerDoneAt && now - _layerDoneAt < LayerRefreshSeconds) return;
        using (FrameProfiler.Measure("战线段·地形层")) StepTerrainLayer(zones, SimulationFrameBudget.ServerMilliseconds.HasValue ? 6d : 1.5d);
    }

    private static void StepTerrainLayer(List<TileZone> zones, double budget)
    {
        if (_nextFlags == null)
        {
            int size = 0;
            foreach (TileZone zone in zones)
                if (zone != null && zone.id >= size) size = zone.id + 1;
            _layerSize = size;
            _nextFlags = new byte[size];
            _nextCross4 = new byte[size];
            _nextCross8 = new byte[size];
            _layerCursor = 0;
        }
        var watch = Stopwatch.StartNew();
        _cycleFrames++;
        while (_layerCursor < zones.Count)
        {
            TileZone zone = zones[_layerCursor++];
            if (zone != null && zone.id >= 0 && zone.id < _layerSize)
            {
                int id = zone.id;
                byte flags = 0;
                if (TerrainSystem.ImpassableTerrain(zone)) flags |= FlagImpassable;
                if (SeaRegionSystem.IsSea(zone)) flags |= FlagSea;
                _nextFlags[id] = flags;
                _nextCross4[id] = CrossMask(zone, zone.neighbours);
                _nextCross8[id] = CrossMask(zone, zone.neighbours_all);
            }
            if ((_layerCursor & 63) == 0 && watch.Elapsed.TotalMilliseconds >= budget)
            {
                _cycleMilliseconds += watch.Elapsed.TotalMilliseconds;
                return;
            }
        }
        LayerZones = zones.Count;
        LayerFrames = _cycleFrames;
        LayerMilliseconds = _cycleMilliseconds + watch.Elapsed.TotalMilliseconds;
        _cycleFrames = 0;
        _cycleMilliseconds = 0d;
        _layerDoneAt = World.world.getCurWorldTime();
        // 建完一轮：换上，下一帧起再建下一轮
        _terrainFlags = _nextFlags;
        _cross4 = _nextCross4;
        _cross8 = _nextCross8;
        _nextFlags = _nextCross4 = _nextCross8 = null;
    }

    // 第 i 位：只看地形，邻块 i 走得过来(TerrainSystem.CrossableTerrain(邻块, 本块)，同 TargetZones、LandRoute 的问法)；城心每批另取(见 _seat)，路线现查；
    // 邻块多于 8 的不会有
    private static byte CrossMask(TileZone zone, TileZone[] around)
    {
        if (around == null) return 0;
        byte mask = 0;
        for (int i = 0; i < around.Length && i < 8; i++)
            if (around[i] != null && TerrainSystem.CrossableTerrain(around[i], zone)) mask |= (byte)(1 << i);
        return mask;
    }

    #endregion

    #region 一批

    private sealed class Candidate
    {
        public City City;
        public long CityId;
        public List<TileZone> Zones;
        public bool InHome, AdjacentHome, WildAdjacent, HasTile;
        public object Island;
        public int X, Y;
        // 结果：0 不算、1 算一段、2 要看水师辐射(主线程补)
        public byte State;
        public List<TileZone> Targets;
    }

    private sealed class SideJob
    {
        public Kingdom Side;
        public string Signature;
        public long RealmId, RouteSignature;
        public int Way;
        public byte[] Relation;
        public readonly List<Candidate> Candidates = new();
        public (object island, int x, int y)[] Home;
        // 算出来的陆路(城, 走得通)与走不通的城：主线程照顺序写回
        public readonly List<(Candidate city, bool found)> Routes = new();
        public Exception Error;
    }

    // 工作线程各自的搜索用表
    [ThreadStatic] private static int[] _kernelSeen;
    [ThreadStatic] private static int _kernelStamp;
    [ThreadStatic] private static Queue<TileZone> _kernelQueue;

    // 这一批共用的快照
    private static int[] _owner = Array.Empty<int>();
    // 各城的城心那块(同 TerrainSystem 的 Seat：城心总能进、与邻块总走得通)
    private static bool[] _seat = Array.Empty<bool>();
    private static readonly List<Kingdom> Slots = new();
    private static readonly Dictionary<Kingdom, int> SlotOf = new();
    // 各国所在帝国的核心(同 Realm)、是否已亡(同 isRekt)：一批里每国只问一次
    private static readonly List<Kingdom> SlotRealm = new();
    private static readonly List<bool> SlotGone = new();
    private static byte[] _wildBlock;
    private static double _kernelNow;

    // 这一帧多核算好了、还没分派的国：这一帧要分派完(留到下一帧就作废了，见 CorpsSystem.AdvanceMonth)
    private static readonly HashSet<Kingdom> PendingPrefetch = new();

    public static bool PrefetchPending => PendingPrefetch.Count > 0 && UnityEngine.Time.frameCount == _assignFrame;

    // 分派前：kingdom 这一帧还没有现成的陆上战线段，就连同队里接下来几国一起多核算好
    private static void PrefetchSectors(Kingdom kingdom, IEnumerable<Kingdom> upcoming)
    {
        // 只有一个核也照快照算(比逐国问各处缓存快得多)，只是不分线程
        if (!ParallelSectors || _terrainFlags == null || upcoming == null) return;
        if (!AtWarSet.Contains(kingdom)) return;
        string first = SectorSignature(kingdom);
        if (SectorCache.ContainsKey(first) || PrefetchedLand.ContainsKey(first)) return;
        var batch = new List<(Kingdom side, string signature)> { (kingdom, first) };
        var seen = new HashSet<string> { first };
        int limit = Math.Max(2, ParallelWork.MaxWorkers + 1);
        foreach (Kingdom next in upcoming)
        {
            if (batch.Count >= limit) break;
            if (next?.data == null || next.isRekt() || !AtWarSet.Contains(next)) continue;
            var corps = next.GetOrCreate().corps;
            if (corps == null || corps.Count == 0) continue;
            string signature = SectorSignature(next);
            if (!seen.Add(signature) || SectorCache.ContainsKey(signature) || PrefetchedLand.ContainsKey(signature)) continue;
            batch.Add((next, signature));
        }
        try
        {
            using (FrameProfiler.Measure("战线段·多核")) RunBatch(batch);
        }
        catch (Exception exception)
        {
            // 出了岔子：这一批照原样逐国算
            LogService.LogWarning($"[EmpireCraft][军团] 战线段多核算失败，改回逐国算: {exception}");
            foreach (var (side, signature) in batch)
            {
                PrefetchedLand.Remove(signature);
                PendingPrefetch.Remove(side);
            }
        }
    }

    private static void RunBatch(List<(Kingdom side, string signature)> batch)
    {
        var jobs = new List<SideJob>(batch.Count);
        using (FrameProfiler.Measure("战线段·取快照"))
        {
            _kernelNow = World.world.getCurWorldTime();
            _wildBlock = WildernessPassage.BlockSnapshot();
            using (FrameProfiler.Measure("快照·归属")) BuildOwners();
            foreach (var (side, signature) in batch) jobs.Add(PrepareJob(side, signature));
        }
        ParallelWork.For(jobs.Count, index =>
        {
            SideJob job = jobs[index];
            try
            {
                RunJob(job);
            }
            catch (Exception exception)
            {
                job.Error = exception;
            }
        });
        ParallelBatches++;
        using (FrameProfiler.Measure("战线段·落地"))
            foreach (SideJob job in jobs)
            {
                if (job.Error != null)
                {
                    LogService.LogWarning($"[EmpireCraft][军团] {job.Side.GetKingdomName()} 战线段多核算出错，改回逐国算: {job.Error}");
                    continue;
                }
                List<Sector> land = Assemble(job);
                if (VerifyParallelSectors) Verify(job, land);
                // 陆路缓存、借道记录照算出来的顺序写回(同逐国算时 LandRoute 里写的)
                foreach (var (candidate, found) in job.Routes)
                {
                    if (LandRoutes.Count > 20000) LandRoutes.Clear();
                    LandRoutes[(job.RealmId, candidate.CityId)] = (found, _kernelNow, job.RouteSignature);
                    if (!found) MilitaryAccessSystem.NoteBlocked(job.Side, candidate.City);
                }
                PrefetchedLand[job.Signature] = land;
                PendingPrefetch.Add(job.Side);
                ParallelSides++;
            }
    }

    // 每块地眼下归谁控制(同 Controller)：没被占的城整城归城主，有占领区的城逐块问
    private static void BuildOwners()
    {
        if (_owner.Length != _layerSize) _owner = new int[_layerSize];
        for (int i = 0; i < _owner.Length; i++) _owner[i] = -1;
        if (_seat.Length != _layerSize) _seat = new bool[_layerSize];
        else Array.Clear(_seat, 0, _seat.Length);
        foreach (City city in World.world.cities)
            if (city?.getTile()?.zone is TileZone center && center.city == city && center.id >= 0 && center.id < _seat.Length)
                _seat[center.id] = true;
        Slots.Clear();
        SlotOf.Clear();
        SlotRealm.Clear();
        SlotGone.Clear();
        foreach (Kingdom kingdom in World.world.kingdoms) Slot(kingdom);
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt() || city.zones == null) continue;
            var data = city.GetOrCreate();
            bool occupied = data.OccupiedZoneOwners?.Count > 0 || data.OccupiedStatus?.Count > 0;
            int owner = Slot(city.kingdom);
            foreach (TileZone zone in city.zones)
            {
                if (zone == null || zone.city != city || zone.id < 0 || zone.id >= _owner.Length) continue;
                _owner[zone.id] = occupied ? Slot(Controller(zone)) : owner;
            }
        }
    }

    private static int Slot(Kingdom kingdom)
    {
        if (kingdom == null) return -1;
        if (SlotOf.TryGetValue(kingdom, out int slot)) return slot;
        slot = Slots.Count;
        Slots.Add(kingdom);
        SlotOf[kingdom] = slot;
        SlotRealm.Add(Realm(kingdom));
        SlotGone.Add(kingdom.data == null || kingdom.isRekt());
        return slot;
    }

    // 主线程：本方的敌友、本方的城、要看的敌城(顺序同 LandSectors 的 Collect)
    private static SideJob PrepareJob(Kingdom side, string signature)
    {
        var job = new SideJob { Side = side, Signature = signature, RealmId = Realm(side).id, Way = WildernessPassage.Way(side) };
        using var measure = FrameProfiler.Measure("快照·本方");
        job.Relation = new byte[Slots.Count];
        Kingdom realm = Realm(side);
        bool sideGone = side.data == null || side.isRekt();
        for (int i = 0; i < Slots.Count; i++)
        {
            Kingdom other = Slots[i];
            byte relation = 0;
            // 同 Hostile、Friendly(各国的帝国核心先取好)
            bool hostile = false;
            if (side.data != null && other.data != null && side != other && !sideGone && !SlotGone[i])
            {
                Kingdom otherRealm = SlotRealm[i];
                hostile = side.isInWarWith(other) || realm != otherRealm &&
                    (realm.isInWarWith(otherRealm) || realm.isInWarWith(other) || side.isInWarWith(otherRealm));
            }
            if (hostile) relation |= RelHostile;
            if (side.data != null && other.data != null && (side == other || !hostile && (realm == SlotRealm[i] || side.isInWarOnSameSide(other))))
                relation |= RelFriendly;
            if (MilitaryAccessSystem.Grants(other, side)) relation |= RelGrants;
            if (other.wild) relation |= RelWild;
            job.Relation[i] = relation;
        }
        HashSet<City> home = HomeCities(side);
        var homeTiles = new List<(object, int, int)>(home.Count);
        foreach (City own in home)
            if (own.getTile() is WorldTile at) homeTiles.Add((IslandOf(at), at.x, at.y));
        job.Home = homeTiles.ToArray();
        var hostiles = new List<Kingdom>();
        foreach (Kingdom other in _atWar)
            if (Hostile(side, other)) hostiles.Add(other);
        long route = Realm(side).id;
        foreach (Kingdom hostile in hostiles) route = unchecked(route * 31 + hostile.id);
        job.RouteSignature = unchecked(route * 31 + MilitaryAccessSystem.Version);
        var seen = new HashSet<City>();
        void Add(City city)
        {
            if (city?.data == null || city.isRekt() || !seen.Add(city)) return;
            var candidate = new Candidate { City = city, CityId = city.data.id, Zones = city.zones, InHome = home.Contains(city) };
            if (!candidate.InHome && city.neighbours_cities != null)
                foreach (City next in city.neighbours_cities)
                    if (next != null && home.Contains(next))
                    {
                        candidate.AdjacentHome = true;
                        break;
                    }
            if (!candidate.InHome && !candidate.AdjacentHome)
            {
                IReadOnlyList<City> wild = WildernessNeighbours.Of(city);
                for (int i = 0; i < wild.Count; i++)
                    if (wild[i] != null && home.Contains(wild[i]))
                    {
                        candidate.WildAdjacent = true;
                        break;
                    }
                if (city.getTile() is WorldTile tile)
                {
                    candidate.HasTile = true;
                    candidate.Island = IslandOf(tile);
                    candidate.X = tile.x;
                    candidate.Y = tile.y;
                }
            }
            job.Candidates.Add(candidate);
        }
        foreach (Kingdom hostile in hostiles)
            if (hostile.cities != null)
                foreach (City city in hostile.cities)
                    Add(city);
        foreach (Kingdom friend in _atWar)
        {
            if (!Friendly(side, friend) || friend.cities == null) continue;
            foreach (City city in friend.cities)
                if (city?.data != null && city.GetOrCreate().OccupiedStatus?.Count > 0) Add(city);
        }
        return job;
    }

    // 同 WorldTile.isSameIsland 的比法(独立服务器上先让它把岛屿算好)
    private static object IslandOf(WorldTile tile)
    {
        tile.isSameIsland(tile);
        return tile.region?.island;
    }

    #endregion

    #region 工作线程

    private static void RunJob(SideJob job)
    {
        var routes = new Dictionary<Candidate, bool>();
        float limit = LandReachTiles * LandReachTiles;
        foreach (Candidate candidate in job.Candidates)
        {
            bool reach;
            if (candidate.InHome || candidate.AdjacentHome) reach = true;
            else
            {
                // 同 InReach：隔无人区相邻且走得通；同一片陆地离本方某城不远且走得通；都不是就看水师辐射(主线程补)
                reach = candidate.WildAdjacent && Route(job, candidate, routes);
                if (!reach)
                {
                    if (!candidate.HasTile)
                    {
                        candidate.State = 0;
                        continue;
                    }
                    bool near = false;
                    foreach (var (island, x, y) in job.Home)
                    {
                        int dx = x - candidate.X, dy = y - candidate.Y;
                        if (island != null && island == candidate.Island && dx * dx + dy * dy <= limit)
                        {
                            near = true;
                            break;
                        }
                    }
                    if (near && Route(job, candidate, routes)) reach = true;
                    else
                    {
                        candidate.State = 2;
                        continue;
                    }
                }
            }
            List<TileZone> targets = Targets(job, candidate.Zones);
            candidate.State = targets.Count > 0 ? (byte)1 : (byte)0;
            candidate.Targets = targets;
        }
    }

    private static int OwnerOf(TileZone zone) => zone.id >= 0 && zone.id < _owner.Length ? _owner[zone.id] : -1;

    private static bool Has(SideJob job, int slot, byte relation) => slot >= 0 && (job.Relation[slot] & relation) != 0;

    private static bool Seat(TileZone zone) => zone.id >= 0 && zone.id < _seat.Length && _seat[zone.id];

    // 同 TerrainSystem.Impassable(路线的地块表只在主线程改，这时主线程在等，读得)
    private static bool Blocked(TileZone zone) =>
        zone.id >= 0 && zone.id < _terrainFlags.Length && (_terrainFlags[zone.id] & FlagImpassable) != 0 && !Seat(zone) && !RoadNetwork.IsTrail(zone);

    private static bool Sea(TileZone zone) => zone.id >= 0 && zone.id < _terrainFlags.Length && (_terrainFlags[zone.id] & FlagSea) != 0;

    // 同 WildernessPassage.Passable
    private static bool WildOpen(TileZone zone, int way)
    {
        byte[] block = _wildBlock;
        if (block == null || zone.id < 0 || zone.id >= block.Length) return true;
        byte bits = block[zone.id];
        return bits == 0 || zone.city != null || (bits & way) == 0;
    }

    // 同 TerrainSystem.Crossable(邻块, 本块)
    private static bool CrossesInto(byte[] masks, TileZone zone, TileZone neighbour, int index) =>
        Seat(zone) || Seat(neighbour) || RoadNetwork.Spans(neighbour, zone) ||
        zone.id < 0 || zone.id >= masks.Length || index >= 8 || (masks[zone.id] & (1 << index)) != 0;

    // 同 Passable
    private static bool Walk(SideJob job, TileZone zone)
    {
        if (Blocked(zone) || Sea(zone) || !WildOpen(zone, job.Way)) return false;
        int owner = OwnerOf(zone);
        return owner < 0 || Has(job, owner, RelWild | RelFriendly | RelHostile | RelGrants);
    }

    // 同 TargetZones
    private static List<TileZone> Targets(SideJob job, List<TileZone> zones)
    {
        var result = new List<TileZone>();
        if (zones == null) return result;
        foreach (TileZone zone in zones)
        {
            if (zone == null || !Has(job, OwnerOf(zone), RelHostile) || Blocked(zone) || !WildOpen(zone, job.Way)) continue;
            TileZone[] around = zone.neighbours;
            if (around == null) continue;
            for (int i = 0; i < around.Length; i++)
            {
                TileZone neighbour = around[i];
                if (neighbour == null || neighbour.world_edge || Blocked(neighbour) || !CrossesInto(_cross4, zone, neighbour, i) ||
                    !WildOpen(neighbour, job.Way)) continue;
                if (!Has(job, OwnerOf(neighbour), RelFriendly)) continue;
                result.Add(zone);
                break;
            }
        }
        return result;
    }

    // 同 LandRoute：先看陆路缓存(主线程这时不写)，没有就广度搜索；算出来的记下，主线程写回
    private static bool Route(SideJob job, Candidate target, Dictionary<Candidate, bool> computed)
    {
        if (computed.TryGetValue(target, out bool done)) return done;
        if (target.Zones == null)
        {
            computed[target] = false;
            return false;
        }
        if (LandRoutes.TryGetValue((job.RealmId, target.CityId), out var known) && known.signature == job.RouteSignature &&
            _kernelNow >= known.at && (int)((_kernelNow - known.at) / 5.0) < LandRouteMonths)
        {
            computed[target] = known.found;
            return known.found;
        }
        int size = Math.Max(_layerSize, _owner.Length);
        if (_kernelSeen == null || _kernelSeen.Length < size)
        {
            _kernelSeen = new int[size];
            _kernelStamp = 0;
        }
        if (++_kernelStamp == int.MaxValue)
        {
            Array.Clear(_kernelSeen, 0, _kernelSeen.Length);
            _kernelStamp = 1;
        }
        int[] seen = _kernelSeen;
        int stamp = _kernelStamp, count = 0;
        Queue<TileZone> queue = _kernelQueue ??= new Queue<TileZone>();
        queue.Clear();
        bool Visit(int id)
        {
            if (id < 0) return false;
            if (id >= seen.Length)
            {
                Array.Resize(ref _kernelSeen, Math.Max(id + 1, seen.Length * 2));
                seen = _kernelSeen;
            }
            if (seen[id] == stamp) return false;
            seen[id] = stamp;
            count++;
            return true;
        }
        foreach (TileZone zone in target.Zones)
            if (zone != null && Visit(zone.id)) queue.Enqueue(zone);
        bool found = false;
        while (queue.Count > 0 && count < LandRouteSearch && !found)
        {
            TileZone zone = queue.Dequeue();
            TileZone[] around = zone.neighbours_all;
            if (around == null) continue;
            for (int i = 0; i < around.Length; i++)
            {
                TileZone next = around[i];
                if (next == null || !Visit(next.id)) continue;
                if (Has(job, OwnerOf(next), RelFriendly) && !Blocked(next) && CrossesInto(_cross8, zone, next, i) && WildOpen(next, job.Way))
                {
                    found = true;
                    break;
                }
                if (Walk(job, next)) queue.Enqueue(next);
            }
        }
        queue.Clear();
        computed[target] = found;
        job.Routes.Add((target, found));
        return found;
    }

    #endregion

    #region 落地与核对

    // 照 Collect 的顺序拼成战线段；只靠水师辐射够得着的城在这里照原样补
    private static List<Sector> Assemble(SideJob job)
    {
        var sectors = new List<Sector>();
        foreach (Candidate candidate in job.Candidates)
        {
            List<TileZone> targets = candidate.State switch
            {
                1 => candidate.Targets,
                2 when StraitReach.Reaches(job.Side, candidate.City) => TargetZones(job.Side, candidate.City),
                _ => null
            };
            if (targets == null || targets.Count == 0) continue;
            var sector = new Sector { City = candidate.City };
            sector.Targets.AddRange(targets);
            sectors.Add(sector);
        }
        return sectors;
    }

    private static int _mismatchLogged;

    private static void Verify(SideJob job, List<Sector> parallel)
    {
        List<Sector> serial = LandSectors(job.Side);
        VerifiedSides++;
        bool same = serial.Count == parallel.Count;
        for (int i = 0; same && i < serial.Count; i++)
        {
            Sector a = serial[i], b = parallel[i];
            if (a.City != b.City || a.Targets.Count != b.Targets.Count) same = false;
            else
                for (int j = 0; j < a.Targets.Count; j++)
                    if (a.Targets[j] != b.Targets[j])
                    {
                        same = false;
                        break;
                    }
        }
        if (same) return;
        VerifyMismatches++;
        if (_mismatchLogged++ >= 10) return;
        string Describe(List<Sector> list) => string.Join("、", list.ConvertAll(sector => sector.City.name + "×" + sector.Targets.Count));
        LogService.LogWarning($"[EmpireCraft][战线段核对] {job.Side.GetKingdomName()} 逐国算 {serial.Count} 段：{Describe(serial)}；多核算 {parallel.Count} 段：{Describe(parallel)}");
    }

    #endregion
}
