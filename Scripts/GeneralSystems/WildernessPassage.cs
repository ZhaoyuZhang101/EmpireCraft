using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 无人区通行(用户 2026-10-29："作出无人区地块……主要是为了吧地图填满，同时能更有效地阻隔国家军队和更容易计算控制力"；
// 问答选军队"只能沿路过：没修路的地方过不去，修了路的地方(山口、商道)能走；游牧过沙漠冻土、阿拉伯过沙漠照旧不受限")：
//   · 无人区(见 WildernessSystem)里有修通的路的地块能走——各国修通的干道、神力画的路(都并在路网里，见 RoadNetwork.IsRoad)；
//   · 没路的：山脉、丛林、沼泽谁都过不去；沙漠真游牧(游牧政体加游牧文化，见 KingdomExtension.IsTrueNomad)与阿拉伯制度线
//     过得去，冻土雪原真游牧过得去(同以前天堑的规矩，按走的一方判；游牧又是阿拉伯的按游牧算)；
//   · 不在无人区里的地块都能走(山、沙照旧只按地形阻力，见 TerrainResistance)；本国的无人区(挂在本国法理上)也一样按这个判。
// 用的地方：行政控制力与飞地、开战够不够得着、开战定目标(AdministrativeControlSystem、WarReach，替掉以前量天堑深浅的那套)，
//   教化的路(TerrainResistance.Step 走不了)，军团寻路、战线、补给线(CorpsSystem、FrontSystem、LogisticsSystem；飞机、海运不受限)。
// 缓存：按地块编号记哪几种走法过不去(一字节)，无人区(WildernessSystem.Version)或路网(RoadNetwork.Version，神力画路也经它换版本)
//   变了才整张重算，查询 O(1)。绕不绕得过去(Reachable)：各走法把不隔无人区的陆地连成片(八方向，同军团寻路)，用到时才算，
//   过不去的地块真变了才重算。开关(WildernessSystem.Enabled)关掉时处处能走。不存档：读档后现算。
public static class WildernessPassage
{
    // 走的一方惯走哪几样(见 Habit)：定居、惯走沙漠(阿拉伯)、惯走沙漠冻土(真游牧)；连片按这三种走法各一份，另一份(Land)不看无人区
    private const int Settled = 0, DesertWise = 1, Nomadic = 2, Land = 3;
    // 各类无人区没路时哪几种走法过不去(按 1 << 走法 记位)
    private const byte DesertBlocks = 1 << Settled, TundraBlocks = (1 << Settled) | (1 << DesertWise),
        AllBlocks = (1 << Settled) | (1 << DesertWise) | (1 << Nomadic);
    // 连片的工作值：还没走到、过不去
    private const int Unvisited = -2, Blocked = -1;

    // 按地块编号：哪几种走法过不去(0 处处能走)
    private static byte[] _block = new byte[0];
    private static object _world;
    private static int _wildVersion = int.MinValue, _roadVersion = int.MinValue, _zoneCount = -1;
    private static bool _enabled, _any;
    // _block 真变了一次加一(连片据此作废)；陆地的连片只在无人区、世界变了时作废(路网变了不影响)
    private static int _blockRevision, _landRevision;
    // 连片(走法 0~2、陆地 3)：地块编号 → 片号，过不去的为 -1
    private static readonly int[][] _parts = new int[4][];
    private static readonly int[] _partRevision = { -1, -1, -1, -1 };
    private static readonly Queue<TileZone> Fill = new();

    public static void ResetWorldState()
    {
        _block = new byte[0];
        _world = null;
        _wildVersion = _roadVersion = int.MinValue;
        _zoneCount = -1;
        _enabled = _any = false;
        _blockRevision++;
        _landRevision++;
        for (int i = 0; i < _parts.Length; i++) _parts[i] = null;
        HabitCache.Clear();
        _habitFrame = -1;
        _lastKingdom = null;
    }

    // 地图上有过不去的无人区(开关关掉、还没划区时为假)：为假时处处能走，寻路里不必逐块查
    public static bool Active => Ensure();

    // traveller 走不走得进这块地：不是无人区、无人区里有路、或这国惯走这一类(见 Habit)。
    // 归了城的地块不是无人区(同 WildernessSystem：神力立村设镇放行给城的那一块，到下次重划之前也算城的)
    public static bool Passable(TileZone zone, Kingdom traveller)
    {
        if (zone == null || !Ensure()) return true;
        int id = zone.id;
        if (id < 0 || id >= _block.Length) return true;
        byte block = _block[id];
        return block == 0 || zone.city != null || (block & (1 << Habit(traveller))) == 0;
    }

    // 不分国家：不是无人区，或无人区里有路
    public static bool PassableByRoad(TileZone zone)
    {
        if (zone == null || !Ensure()) return true;
        int id = zone.id;
        return id < 0 || id >= _block.Length || _block[id] == 0 || zone.city != null;
    }

    // 寻路里逐邻查用：先取一次走法(Way)，确认过 Active 后用 Open，省掉每块的版本比对与判习性
    public static int Way(Kingdom traveller) => 1 << Habit(traveller);

    public static bool Open(TileZone zone, int way) =>
        zone == null || zone.id < 0 || zone.id >= _block.Length || (_block[zone.id] & way) == 0 || zone.city != null;

    // 两块地之间绕得过、或沿路穿得过 traveller 过不去的无人区：同一片陆地上被这种无人区隔成两片的为假；
    // 不在同一片陆地上(隔海)的不归这里管，算真。自己就是过不去的无人区的地块，按挨着的能走的地块算
    public static bool Reachable(TileZone a, TileZone b, Kingdom traveller)
    {
        if (a == null || b == null || a == b || !Ensure()) return true;
        int[] land = Parts(Land);
        if (!SharesPart(land, a, b)) return true;
        return SharesPart(Parts(Habit(traveller)), a, b);
    }

    private static bool Ensure()
    {
        MapBox world = World.world;
        List<TileZone> zones = world?.zone_calculator?.zones;
        if (zones == null) return false;
        bool enabled = WildernessSystem.Enabled;
        if (ReferenceEquals(_world, world) && enabled == _enabled && _wildVersion == WildernessSystem.Version &&
            _roadVersion == RoadNetwork.Version && _zoneCount == zones.Count) return _any;
        Rebuild(world, zones, enabled);
        return _any;
    }

    // 整张重算：每块地是什么无人区、有没有路(都是 O(1) 的查询)；只有真变了才作废连片
    private static void Rebuild(MapBox world, List<TileZone> zones, bool enabled)
    {
        bool changed = false;
        if (!ReferenceEquals(_world, world) || _zoneCount != zones.Count || enabled != _enabled ||
            _wildVersion != WildernessSystem.Version)
        {
            _landRevision++;
            changed = !ReferenceEquals(_world, world) || _zoneCount != zones.Count;
        }
        _world = world;
        _enabled = enabled;
        _wildVersion = WildernessSystem.Version;
        _roadVersion = RoadNetwork.Version;
        _zoneCount = zones.Count;
        int size = zones.Count;
        foreach (TileZone zone in zones)
            if (zone != null && zone.id >= size) size = zone.id + 1;
        if (_block.Length != size)
        {
            _block = new byte[size];
            changed = true;
        }
        bool any = false;
        foreach (TileZone zone in zones)
        {
            if (zone == null || zone.id < 0) continue;
            byte block = enabled ? Blocking(zone) : (byte)0;
            if (block != 0) any = true;
            if (_block[zone.id] == block) continue;
            _block[zone.id] = block;
            changed = true;
        }
        _any = any;
        if (changed) _blockRevision++;
    }

    private static byte Blocking(TileZone zone)
    {
        WildKind kind = WildernessSystem.KindOf(zone);
        if (kind == WildKind.None || RoadNetwork.IsRoad(zone)) return 0;
        return kind switch
        {
            WildKind.Desert => DesertBlocks,
            WildKind.Tundra => TundraBlocks,
            _ => AllBlocks
        };
    }

    // 走的一方惯走哪些无人区：真游牧(见 KingdomExtension.IsTrueNomad)沙漠冻土，阿拉伯制度线沙漠，别的(连同不知是谁的)都不惯；
    // 同一帧里同一国只判一次(寻路、控制力一趟会问很多块地，战线里几国交替着问)
    private static readonly Dictionary<long, int> HabitCache = new();
    private static int _habitFrame = -1, _lastHabit;
    private static Kingdom _lastKingdom;

    private static int Habit(Kingdom kingdom)
    {
        if (kingdom?.data == null) return Settled;
        int frame = Time.frameCount;
        if (frame != _habitFrame)
        {
            HabitCache.Clear();
            _habitFrame = frame;
            _lastKingdom = null;
        }
        if (ReferenceEquals(kingdom, _lastKingdom)) return _lastHabit;
        if (!HabitCache.TryGetValue(kingdom.id, out int habit))
            HabitCache[kingdom.id] = habit = KingdomExtension.IsTrueNomad(kingdom) ? Nomadic
                : InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) == "Arabic" ? DesertWise
                : Settled;
        _lastKingdom = kingdom;
        _lastHabit = habit;
        return habit;
    }

    // 连片：way 为走法(Land 不看无人区)，海面、地图边缘不算陆地；八方向连通
    private static int[] Parts(int way)
    {
        int[] parts = _parts[way];
        int revision = way == Land ? _landRevision : _blockRevision;
        if (parts != null && parts.Length == _block.Length && _partRevision[way] == revision) return parts;
        if (parts == null || parts.Length != _block.Length) parts = new int[_block.Length];
        for (int i = 0; i < parts.Length; i++) parts[i] = Unvisited;
        int mask = way == Land ? 0 : 1 << way, next = 0;
        foreach (TileZone start in World.world.zone_calculator.zones)
        {
            if (start == null || start.id < 0 || start.id >= parts.Length || parts[start.id] != Unvisited) continue;
            if (!Walkable(start, mask))
            {
                parts[start.id] = Blocked;
                continue;
            }
            int part = next++;
            parts[start.id] = part;
            Fill.Clear();
            Fill.Enqueue(start);
            while (Fill.Count > 0)
            {
                TileZone zone = Fill.Dequeue();
                foreach (TileZone neighbour in zone.neighbours_all ?? Array.Empty<TileZone>())
                {
                    if (neighbour == null || neighbour.id < 0 || neighbour.id >= parts.Length || parts[neighbour.id] != Unvisited)
                        continue;
                    if (!Walkable(neighbour, mask))
                    {
                        parts[neighbour.id] = Blocked;
                        continue;
                    }
                    parts[neighbour.id] = part;
                    Fill.Enqueue(neighbour);
                }
            }
        }
        Fill.Clear();
        _parts[way] = parts;
        _partRevision[way] = revision;
        return parts;
    }

    private static bool Walkable(TileZone zone, int mask) =>
        !zone.world_edge && !SeaRegionSystem.IsSea(zone) && (_block[zone.id] & mask) == 0;

    private static int PartOf(int[] parts, TileZone zone) =>
        zone != null && zone.id >= 0 && zone.id < parts.Length ? parts[zone.id] : Blocked;

    // a、b 在同一片(走不了的一头按挨着的几片算)
    private static bool SharesPart(int[] parts, TileZone a, TileZone b)
    {
        int own = PartOf(parts, a);
        if (own >= 0) return Touches(parts, b, own);
        foreach (TileZone neighbour in a.neighbours_all ?? Array.Empty<TileZone>())
        {
            int part = PartOf(parts, neighbour);
            if (part >= 0 && Touches(parts, b, part)) return true;
        }
        return false;
    }

    // zone 在 part 这一片，或自己走不了而挨着这一片
    private static bool Touches(int[] parts, TileZone zone, int part)
    {
        int own = PartOf(parts, zone);
        if (own >= 0) return own == part;
        foreach (TileZone neighbour in zone.neighbours_all ?? Array.Empty<TileZone>())
            if (PartOf(parts, neighbour) == part) return true;
        return false;
    }
}
