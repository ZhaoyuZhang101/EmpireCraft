using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 无人区按格贴地形(用户 2026-10-29 "平滑模式下图层要完全贴合地形")：平滑国界开着时，无人区不再按地块整块填色(撒哈拉、青藏外沿
// 一级级的 8×8 台阶，国、城的边却是圆的、伸进无人区)，而是一格一格定范围；无人区图层(WildernessOverlay)与整图画布
// (SmoothBorders.Canvas)照同一份画，同一格不会既是国色又是无人区色，也不留缝：
//   · 外沿：每格按周围 3×3 块做 B 样条加噪声(同国界，见 SmoothBorders.CityAt)，候选三类——各片无人区、有城的地、无主地；
//     只定无人区与别的地之间那条线(城与城之间画布自己分)，台阶变成和国界一个样子的圆弧、起伏；
//   · 贴地形：判给无人区的格子里，地形属于这一类的才算(WildernessSystem.GroundKind，同归类的口径：山脉看山地山巅连同挨着山的丘陵，
//     沙漠冻原看沙地荒漠冻土雪地、雨林大泽看丛林沼泽——这两对归类时合算，这里也合算)。不属于的(边上的草地、绿洲、山间谷地)：
//       - 离外面(不是无人区的地)ReachSteps 步以内连得出去的(Claim)：画布照常交给邻城，没有城的露地形——国界沿沙漠、山脉的真实轮廓走；
//         并进国土的无人区里，邻城不要的归这片的持有国；
//       - 连不出去的(Hole)：包在里面的洞，露地形、不描外沿(并进国土的照样涂国色)；单格的洞(八邻里 FillNeighbours 格是这片的)补上，
//         免得戳成筛子；
//   · 往外伸：B 样条判给无主地的格子里，与无人区连着、同一类地形的再伸三到六格(GrowMin~GrowSteps，按低频噪声起伏)，
//     边沿着沙漠、山脉的真实轮廓收住；沙漠一直连下去的地方伸到哪儿随噪声起伏，不是离地块边固定几格的一道直线。
//     无主地上没有国界要对齐，地形的边就是最自然的边；至多六格(不到一块地宽)，伸出去的格周围 3×3 块里还得有这片的地块，
//     不会顺着沙地把远处另一小片(不够连片的)荒地也吞进来。
//     判给有城的地的不伸：城、国的地盘照平滑国界画，城界就是无人区的边；沙漠里只有一块地的小城也不会被吃掉；
//   · 何时重算：区域变了(WildernessSystem.Version，只比各块归哪片、有没有城；挂接、归属国变了不算)、无人区两块以内的地块
//     有没有城变了(每帧对一遍)、地形变了(ZonePaintBudget.TerrainChanged，攒 TerrainDelay 秒一起算)：只重算变了的地块
//     周围几块(一格的结果至多看到十三格外：外伸六格、连出去四格、补洞一格、丘陵看两格；地块候选变了重算三圈、地形变了两圈，
//     中间态多算 Margin 圈)，算完让画布重算这几块的城的轮廓(SmoothBorders.WildReshaped)、无人区图层重画；
//     整张算只在开平滑国界、进图、换世界时，日志记用时。
// 平滑国界关着、无人区关着或没有区域时不算(Active 为假)，各处照按地块画。
public static class WildernessShape
{
    private const int ZoneSize = Config.CITY_ZONE_SIZE;
    private const int GrowMin = 3, GrowSteps = 6, ReachSteps = 4, FillNeighbours = 6;
    // 要写回的地块外面多算几圈中间态；地块候选变了、地形变了各重算几圈(见类头)
    private const int Margin = 2, KeyRadius = 3, TerrainRadius = 2;
    private const float TerrainDelay = 0.5f, RetrySeconds = 30f;

    // 一格的结果：低位槽号(0 不是无人区)；Claim 判给了这片、地形不对、交给画布的；Hole 包在这片里面的洞
    private const int SlotMask = 0x1FFF, ClaimBit = 0x2000, HoleBit = 0x4000;
    // 算的中间态(只在这一轮的范围里有效)：低 16 位槽号，高位是哪种
    private const int WaterRaw = -1, Core = 1 << 16, Miss = 2 << 16, Grow = 3 << 16, StateMask = 3 << 16;
    // 块的候选：有城的地、无主地；无人区是槽号(≥1)
    private const int CityKey = -1, EmptyKey = 0;
    private const int CitySeed = 1013904223, GrowSeed = 4241;
    // 一格的地形(缓存在 _cls)：水面、山地、丘陵，低三位是地面属哪一类(WildKind)
    private const byte ClsWater = 0x80, ClsRock = 0x40, ClsHills = 0x20, ClsGround = 0x07;

    private static object _world;
    private static int _width, _height, _zonesX, _zonesY, _zoneCount, _version = int.MinValue, _frame = -1;
    private static bool _active, _fullPass;
    private static float _due = float.MaxValue, _retryAt;
    private static TileZone[] _zones = Array.Empty<TileZone>();
    // 各格的结果；各块：候选、重算一次加一的记号(画布城的轮廓、图层重画据此比对)、有没有无人区的格、两块以内有没有无人区
    private static ushort[] _tile = Array.Empty<ushort>();
    private static int[] _key = Array.Empty<int>(), _stamp = Array.Empty<int>();
    private static bool[] _touched = Array.Empty<bool>(), _near = Array.Empty<bool>();
    // 两块以内有无人区的地块(连同无人区自己)：每帧对一遍有没有城
    private static readonly List<int> Watch = new();
    // 槽号 ↔ 区域(按区域编号沿用槽号)；各槽的噪声种子
    private static readonly List<WildRegion> Slots = new() { null };
    private static readonly List<int> SlotSeeds = new() { 0 };
    private static readonly Dictionary<int, int> SlotOfId = new();
    // 等着重算的地块；是不是无人区变了的地块(周围的城的轮廓要重算)
    private static readonly HashSet<int> Dirty = new(), Flipped = new();

    // 这一轮的范围(按轮次记)：要写回的(D)、连同外面 Margin 圈算中间态、分地形的(A)；各格的中间态、地形
    private static int[] _inD = Array.Empty<int>(), _inA = Array.Empty<int>();
    private static int _pass;
    private static int[] _raw = Array.Empty<int>();
    private static byte[] _cls = Array.Empty<byte>();
    private static readonly List<int> DList = new(), AList = new(), Frontier = new(), NextFrontier = new(), Promote = new();
    private static readonly int[] Keys = new int[9], CandKeys = new int[9];
    private static readonly float[] CandWeights = new float[9];
    private static int _candCount;

    // ───────── 查询 ─────────

    internal static bool Active => _active;

    // 这一格是无人区(连同包在里面的洞)：哪片(槽号)，不是为 0。画布让开(不归城)、图层按它填色
    internal static int AreaAt(int tileId)
    {
        if (!_active || (uint)tileId >= (uint)_tile.Length) return 0;
        int value = _tile[tileId];
        return (value & ClaimBit) != 0 ? 0 : value & SlotMask;
    }

    // 这一格判给了哪片(无人区、洞、地形不对交给画布的都算)：并进国土时按这片的持有国涂
    internal static int SlotAt(int tileId) =>
        _active && (uint)tileId < (uint)_tile.Length ? _tile[tileId] & SlotMask : 0;

    internal static bool HoleAt(int tileId) => _active && (uint)tileId < (uint)_tile.Length && (_tile[tileId] & HoleBit) != 0;

    internal static WildRegion RegionOfSlot(int slot) => slot > 0 && slot < Slots.Count ? Slots[slot] : null;

    // 这块地是无人区(画布算城的轮廓时不把它当候选：城的地盘伸到无人区的格子为止)
    internal static bool ZoneWild(int zoneId) => _active && (uint)zoneId < (uint)_key.Length && _key[zoneId] > 0;

    // 这块地是哪片无人区(槽号，不是为 0)
    internal static int ZoneSlot(int zoneId) =>
        _active && (uint)zoneId < (uint)_key.Length && _key[zoneId] > 0 ? _key[zoneId] : 0;

    // 这块地里有无人区的格(含洞)
    internal static bool ZoneTouched(int zoneId) => _active && (uint)zoneId < (uint)_touched.Length && _touched[zoneId];

    // 这块地重算过几次(画布的城的轮廓指纹、图层画过的样子据此比对；关掉后清零，同样对不上)
    internal static int ZoneStamp(int zoneId) => (uint)zoneId < (uint)_stamp.Length ? _stamp[zoneId] : 0;

    internal static IEnumerable<int> TouchedZones()
    {
        if (!_active) yield break;
        for (int id = 0; id < _touched.Length; id++)
            if (_touched[id]) yield return id;
    }

    // ───────── 每帧 ─────────

    // 每帧(无人区图层更新、整图画布画之前都调，一帧只做一次)
    internal static void Tick()
    {
        int frame = Time.frameCount;
        if (frame == _frame) return;
        _frame = frame;
        MapBox world = World.world;
        ZoneCalculator calc = world?.zone_calculator;
        float now = Time.realtimeSinceStartup;
        bool want = SmoothBorders.Enabled && WildernessSystem.Enabled && WildernessSystem.Regions.Count > 0 &&
                    calc?.zones != null && Config.game_loaded && !SmoothLoader.isLoading() && MapBox.width > 0 &&
                    now >= _retryAt;
        if (!want)
        {
            if (_active) Deactivate(true);
            return;
        }
        if (!ReferenceEquals(_world, world) || _width != MapBox.width || _height != MapBox.height ||
            _zonesX != calc.zones_total_x || _zonesY != calc.zones_total_y)
        {
            // 换了世界：画布、图层自己也会整张重来，不必一块块通知
            if (_active) Deactivate(false);
            Allocate(world, calc);
        }
        try
        {
            if (!_active || WildernessSystem.Version != _version) Sync(calc);
            WatchCities(calc);
            if (Dirty.Count > 0 && now >= _due) Process();
        }
        catch (Exception exception)
        {
            // 算错了：退回按地块画，过一会儿整张重来
            LogService.LogWarning($"[EmpireCraft][无人区贴地形] 出错，暂按地块画：{exception}");
            _retryAt = now + RetrySeconds;
            Deactivate(true);
        }
    }

    // 一格的样子变了(见 ZonePaintBudget.TerrainChanged)：无人区两块以内的，攒一会儿后重算周围两块(刷一片地只算一回)
    internal static void TerrainChanged(WorldTile tile)
    {
        TileZone zone = tile?.zone;
        if (!_active || zone == null || zone.id < 0 || zone.id >= _near.Length || !_near[zone.id]) return;
        MarkDirty(zone.x, zone.y, TerrainRadius, false);
    }

    private static void Allocate(MapBox world, ZoneCalculator calc)
    {
        _world = world;
        _width = MapBox.width;
        _height = MapBox.height;
        _zonesX = calc.zones_total_x;
        _zonesY = calc.zones_total_y;
        _zoneCount = _zonesX * _zonesY;
        _zones = new TileZone[_zoneCount];
        int tiles = _width * _height;
        _tile = new ushort[tiles];
        _raw = new int[tiles];
        _cls = new byte[tiles];
        _key = new int[_zoneCount];
        _stamp = new int[_zoneCount];
        _touched = new bool[_zoneCount];
        _near = new bool[_zoneCount];
        _inD = new int[_zoneCount];
        _inA = new int[_zoneCount];
        _pass = 0;
        ResetSlots();
        Watch.Clear();
        Dirty.Clear();
        Flipped.Clear();
        _due = float.MaxValue;
    }

    private static void ResetSlots()
    {
        Slots.Clear();
        Slots.Add(null);
        SlotSeeds.Clear();
        SlotSeeds.Add(0);
        SlotOfId.Clear();
    }

    // 关掉(平滑国界、无人区关了，没有区域了，出错)：清空；notify 时让画过的地块按地块画法重画
    private static void Deactivate(bool notify)
    {
        _active = false;
        if (notify && _zones.Length == _zoneCount)
            for (int id = 0; id < _zoneCount; id++)
            {
                bool wild = _key[id] > 0;
                if (!wild && !_touched[id]) continue;
                TileZone zone = _zones[id];
                if (zone == null) continue;
                if (_touched[id]) WildernessOverlay.MarkAround(zone);
                // 画布算城的轮廓不再让开这几块、不再跳过它们这个候选
                int zx = id % _zonesX, zy = id / _zonesX;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = zx + dx, ny = zy + dy;
                    if (nx >= 0 && nx < _zonesX && ny >= 0 && ny < _zonesY) SmoothBorders.WildReshaped(ny * _zonesX + nx);
                }
            }
        Array.Clear(_tile, 0, _tile.Length);
        Array.Clear(_key, 0, _key.Length);
        Array.Clear(_stamp, 0, _stamp.Length);
        Array.Clear(_touched, 0, _touched.Length);
        Array.Clear(_near, 0, _near.Length);
        Watch.Clear();
        Dirty.Clear();
        Flipped.Clear();
        ResetSlots();
        _version = int.MinValue;
        _due = float.MaxValue;
        _fullPass = false;
    }

    // ───────── 对区域、对城 ─────────

    // 区域变了：各块的候选重对一遍，变了的周围三块重算(归属国变了 Version 也加一，候选不变就不算)；头一回整张算
    private static void Sync(ZoneCalculator calc)
    {
        _version = WildernessSystem.Version;
        bool full = !_active;
        if (Slots.Count + WildernessSystem.Regions.Count > SlotMask)
        {
            // 槽号攒满了(区域编号一直往上长)：整张重来
            ResetSlots();
            full = true;
        }
        if (full)
        {
            // 读档换图可能还是同一个 MapBox(同样大小)：地块对象每回整张重取
            Array.Clear(_zones, 0, _zones.Length);
            foreach (TileZone zone in calc.zones)
                if (zone != null && zone.id >= 0 && zone.id < _zoneCount) _zones[zone.id] = zone;
        }
        var present = new HashSet<int>();
        foreach (WildRegion region in WildernessSystem.Regions)
        {
            if (region == null || region.Kind == WildKind.None) continue;
            present.Add(region.Id);
            if (!SlotOfId.TryGetValue(region.Id, out int slot))
            {
                slot = Slots.Count;
                Slots.Add(region);
                SlotSeeds.Add(unchecked(region.Id * 374761393 + 668265263));
                SlotOfId[region.Id] = slot;
            }
            else Slots[slot] = region;
        }
        // 没了的区域：槽号留着(还没重算的格子在用)，区域对象放掉
        foreach (KeyValuePair<int, int> pair in SlotOfId)
            if (!present.Contains(pair.Key)) Slots[pair.Value] = null;
        bool flipped = false;
        for (int id = 0; id < _zoneCount; id++)
        {
            TileZone zone = _zones[id];
            if (zone == null) continue;
            int key = KeyOf(zone);
            int was = _key[id];
            if (!full && key == was) continue;
            _key[id] = key;
            if (full) continue;
            bool wild = key > 0, wasWild = was > 0;
            if (wild != wasWild)
            {
                flipped = true;
                Flipped.Add(id);
            }
            // 有城、无主之间变了：只有两块以内有无人区的才要紧
            if (wild || wasWild || _near[id]) MarkDirty(zone.x, zone.y, KeyRadius, true);
        }
        if (full || flipped) RebuildNear();
        if (!full) return;
        // 整张算：两块以内有无人区的都算(更远的格子看不到无人区，都是 0)，原先画过的也重写；各片无人区四周的城的轮廓都要重算
        _active = true;
        _fullPass = true;
        for (int id = 0; id < _zoneCount; id++)
        {
            if (_near[id] || _touched[id]) Dirty.Add(id);
            if (_key[id] > 0) Flipped.Add(id);
        }
        _due = 0f;
    }

    private static int KeyOf(TileZone zone)
    {
        City city = zone.city;
        if (city != null) return city.isAlive() ? CityKey : EmptyKey;
        WildRegion region = WildernessSystem.RegionOf(zone);
        return region != null && region.Kind != WildKind.None && SlotOfId.TryGetValue(region.Id, out int slot) ? slot : EmptyKey;
    }

    private static int KeyAt(int zx, int zy) =>
        zx < 0 || zx >= _zonesX || zy < 0 || zy >= _zonesY ? EmptyKey : _key[zy * _zonesX + zx];

    // 两块以内有无人区的地块(连同无人区自己)：每帧对有没有城
    private static void RebuildNear()
    {
        Array.Clear(_near, 0, _near.Length);
        for (int id = 0; id < _zoneCount; id++)
        {
            if (_key[id] <= 0) continue;
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = zx + dx, ny = zy + dy;
                if (nx >= 0 && nx < _zonesX && ny >= 0 && ny < _zonesY) _near[ny * _zonesX + nx] = true;
            }
        }
        Watch.Clear();
        for (int id = 0; id < _zoneCount; id++)
            if (_near[id]) Watch.Add(id);
    }

    // 无人区周围的地块有没有城(建城、扩张、城没了)：变了周围三块重算。先只看有没有城(每帧几千块，不逐块查区域)；
    // 无人区的地块有了城(放行给城，见 WildernessSystem.OnZoneTaken，Version 也会加一)、城没了露出无人区的：整张对一遍候选
    private static void WatchCities(ZoneCalculator calc)
    {
        bool resync = false;
        foreach (int id in Watch)
        {
            TileZone zone = _zones[id];
            if (zone == null) continue;
            int key = _key[id];
            City city = zone.city;
            if (key > 0 ? city == null : (key == CityKey) == (city != null && city.isAlive())) continue;
            int now = KeyOf(zone);
            if ((now > 0) != (key > 0))
            {
                resync = true;
                continue;
            }
            if (now == key) continue;
            _key[id] = now;
            MarkDirty(zone.x, zone.y, KeyRadius, true);
        }
        if (resync) Sync(calc);
    }

    private static void MarkDirty(int zx, int zy, int radius, bool urgent)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int nx = zx + dx, ny = zy + dy;
            if (nx >= 0 && nx < _zonesX && ny >= 0 && ny < _zonesY) Dirty.Add(ny * _zonesX + nx);
        }
        _due = urgent ? 0f : Mathf.Min(_due, Time.realtimeSinceStartup + TerrainDelay);
    }

    // ───────── 重算 ─────────

    private static void Process()
    {
        var watch = global::System.Diagnostics.Stopwatch.StartNew();
        // 整张算的那一回记日志(平时建城、扩张、刷地形只重算几十块，不记)
        bool full = _fullPass;
        _fullPass = false;
        _due = float.MaxValue;
        _pass++;
        DList.Clear();
        AList.Clear();
        foreach (int id in Dirty)
        {
            if (id < 0 || id >= _zoneCount) continue;
            _inD[id] = _pass;
            DList.Add(id);
        }
        Dirty.Clear();
        // 外面 Margin 圈只算中间态(一格的结果至多看到十三格外，一块地宽八格)
        foreach (int id in DList)
        {
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int dy = -Margin; dy <= Margin; dy++)
            for (int dx = -Margin; dx <= Margin; dx++)
            {
                int nx = zx + dx, ny = zy + dy;
                if (nx < 0 || nx >= _zonesX || ny < 0 || ny >= _zonesY) continue;
                int near = ny * _zonesX + nx;
                if (_inA[near] == _pass) continue;
                _inA[near] = _pass;
                AList.Add(near);
            }
        }
        using (FrameProfiler.Measure("无人区·贴地形"))
        {
            foreach (int id in AList) ClassifyZone(id);
            foreach (int id in AList) RawZone(id);
            CloseHoles();
            GrowOut();
            ReachOut();
            int changed = WriteBack();
            if (full)
                LogService.LogInfo($"[EmpireCraft][无人区贴地形] 重算 {DList.Count} 块(变了 {changed} 块)，用时 {watch.Elapsed.TotalMilliseconds:0} ms");
        }
        Flipped.Clear();
    }

    // 各格的地形(这一轮的范围里)
    private static void ClassifyZone(int id)
    {
        int zx = id % _zonesX, zy = id / _zonesX;
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize && y < _height; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize && x < _width; x++)
            _cls[y * _width + x] = ClassOf(x, y);
    }

    private static byte ClassOf(int x, int y)
    {
        WorldTile tile = World.world.GetTile(x, y);
        TileTypeBase type = tile?.Type;
        if (type == null) return ClsWater;
        // 水面同画布(SmoothBorders.IsWater)：岩浆不算水
        if (type.ocean || type.liquid && !type.lava) return ClsWater;
        WildKind ground = WildernessSystem.GroundKind(tile, out bool rock, out bool hills);
        return (byte)((int)ground & ClsGround | (rock ? ClsRock : 0) | (hills ? ClsHills : 0));
    }

    private static bool InArea(int x, int y) =>
        x >= 0 && y >= 0 && x < _width && y < _height && _inA[y / ZoneSize * _zonesX + x / ZoneSize] == _pass;

    // 山地：这一轮范围外的现查
    private static bool Rock(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height) return false;
        byte cls = InArea(x, y) ? _cls[y * _width + x] : ClassOf(x, y);
        return (cls & ClsRock) != 0 && (cls & ClsWater) == 0;
    }

    // 丘陵挨着山(两格以内有山地)：山脉连同夹在里面的丘陵
    private static bool RockNear(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            if ((dx != 0 || dy != 0) && Rock(x + dx, y + dy)) return true;
        return false;
    }

    // 地形算不算这一类；strict：往外伸时只认本类(沙漠只伸沙地，不伸雪地)
    private static bool Fits(int cls, WildKind kind, bool strict, int x, int y)
    {
        if ((cls & ClsWater) != 0) return false;
        int ground = cls & ClsGround;
        switch (kind)
        {
            case WildKind.Mountain:
                return (cls & ClsRock) != 0 || (cls & ClsHills) != 0 && RockNear(x, y);
            case WildKind.Desert:
                return ground == (int)WildKind.Desert || !strict && ground == (int)WildKind.Tundra;
            case WildKind.Tundra:
                return ground == (int)WildKind.Tundra || !strict && ground == (int)WildKind.Desert;
            case WildKind.Jungle:
                return ground == (int)WildKind.Jungle || !strict && ground == (int)WildKind.Swamp;
            case WildKind.Swamp:
                return ground == (int)WildKind.Swamp || !strict && ground == (int)WildKind.Jungle;
        }
        return false;
    }

    private static WildKind KindOfSlot(int slot) => slot > 0 && slot < Slots.Count && Slots[slot] != null ? Slots[slot].Kind : WildKind.None;

    // 一块地各格的中间态：B 样条判给哪类(同 SmoothBorders.CityAt)，判给无人区的看地形对不对(Core/Miss)，
    // 判给无主地、地形同邻近某片的记为可伸(Grow)
    private static void RawZone(int id)
    {
        int zx = id % _zonesX, zy = id / _zonesX;
        int center = _key[id];
        bool any = false, uniform = true;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int key = KeyAt(zx + dx, zy + dy);
            Keys[(dy + 1) * 3 + dx + 1] = key;
            if (key > 0) any = true;
            if (key != center) uniform = false;
        }
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize && y < _height; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize && x < _width; x++)
        {
            int t = y * _width + x;
            int cls = _cls[t];
            if ((cls & ClsWater) != 0)
            {
                _raw[t] = WaterRaw;
                continue;
            }
            if (!any)
            {
                _raw[t] = 0;
                continue;
            }
            int winner = uniform ? center : Winner(x, y, zx, zy);
            if (winner > 0)
            {
                _raw[t] = (Fits(cls, KindOfSlot(winner), false, x, y) ? Core : Miss) | winner;
                continue;
            }
            _raw[t] = 0;
            if (winner != EmptyKey || uniform) continue;
            // 判给无主地：权重最大、地形对得上(只认本类)的那片可以伸过来
            int grow = 0;
            float best = float.MinValue;
            for (int k = 0; k < _candCount; k++)
            {
                int slot = CandKeys[k];
                if (slot <= 0 || CandWeights[k] <= best || !Fits(cls, KindOfSlot(slot), true, x, y)) continue;
                best = CandWeights[k];
                grow = slot;
            }
            if (grow > 0) _raw[t] = Grow | grow;
        }
    }

    // B 样条加噪声取胜者(候选在 Keys：本块周围 3×3 块；权重、噪声同 SmoothBorders.CityAt)，各候选的权重留在 CandKeys/CandWeights
    private static int Winner(int x, int y, int zx, int zy)
    {
        float tx = (x + 0.5f) / ZoneSize - zx - 0.5f, ty = (y + 0.5f) / ZoneSize - zy - 0.5f;
        float ax0 = 0.5f * (0.5f - tx) * (0.5f - tx), ax1 = 0.75f - tx * tx, ax2 = 0.5f * (0.5f + tx) * (0.5f + tx);
        float ay0 = 0.5f * (0.5f - ty) * (0.5f - ty), ay1 = 0.75f - ty * ty, ay2 = 0.5f * (0.5f + ty) * (0.5f + ty);
        int count = 0;
        for (int j = 0; j < 3; j++)
        {
            float wy = j == 0 ? ay0 : j == 1 ? ay1 : ay2;
            for (int i = 0; i < 3; i++)
            {
                int key = Keys[j * 3 + i];
                float w = wy * (i == 0 ? ax0 : i == 1 ? ax1 : ax2);
                int k = 0;
                while (k < count && CandKeys[k] != key) k++;
                if (k == count)
                {
                    CandKeys[count] = key;
                    CandWeights[count] = 0f;
                    count++;
                }
                CandWeights[k] += w;
            }
        }
        _candCount = count;
        if (count == 1) return CandKeys[0];
        // 领先第二名超过噪声的全幅(两方各 ±一半)：噪声翻不过来，不必逐个算(块心附近的格大多如此)
        int top = 0;
        float first = float.MinValue, second = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            float w = CandWeights[k];
            if (w > first)
            {
                second = first;
                first = w;
                top = k;
            }
            else if (w > second) second = w;
        }
        if (first - second > SmoothBorders.BorderNoiseAmplitude) return CandKeys[top];
        int winner = EmptyKey;
        float bestScore = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            int key = CandKeys[k];
            int seed = key > 0 ? SlotSeeds[key] : key == CityKey ? CitySeed : 0;
            float score = CandWeights[k] + SmoothBorders.BorderNoiseAmplitude * (SmoothBorders.BorderNoise(x, y, seed) - 0.5f);
            if (score <= bestScore) continue;
            bestScore = score;
            winner = key;
        }
        return winner;
    }

    private static int RawAt(int x, int y) => InArea(x, y) ? _raw[y * _width + x] : WaterRaw;

    // 补单格的洞：地形不对的格八邻里 FillNeighbours 格以上是这片的
    private static void CloseHoles()
    {
        Promote.Clear();
        foreach (int id in AList)
        {
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize && y < _height; y++)
            for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize && x < _width; x++)
            {
                int raw = _raw[y * _width + x];
                if ((raw & StateMask) != Miss || raw < 0) continue;
                int core = Core | (raw & 0xFFFF), count = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && RawAt(x + dx, y + dy) == core) count++;
                if (count >= FillNeighbours) Promote.Add(y * _width + x);
            }
        }
        foreach (int t in Promote) _raw[t] = Core | (_raw[t] & 0xFFFF);
        Promote.Clear();
    }

    // 往外伸：从无人区的格起，四邻里可伸(同一片)的格一步步收进来；每格伸得到几步按低频噪声在 GrowMin~GrowSteps 之间起伏
    private static void GrowOut()
    {
        Frontier.Clear();
        foreach (int id in AList)
        {
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize && y < _height; y++)
            for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize && x < _width; x++)
            {
                int raw = _raw[y * _width + x];
                if (raw < 0 || (raw & StateMask) != Core) continue;
                int grow = Grow | (raw & 0xFFFF);
                if (RawAt(x + 1, y) == grow || RawAt(x - 1, y) == grow || RawAt(x, y + 1) == grow || RawAt(x, y - 1) == grow)
                    Frontier.Add(y * _width + x);
            }
        }
        for (int step = 1; step <= GrowSteps && Frontier.Count > 0; step++)
        {
            NextFrontier.Clear();
            foreach (int t in Frontier)
            {
                int x = t % _width, y = t / _width;
                int slot = _raw[t] & 0xFFFF;
                TryGrow(x + 1, y, slot, step);
                TryGrow(x - 1, y, slot, step);
                TryGrow(x, y + 1, slot, step);
                TryGrow(x, y - 1, slot, step);
            }
            Frontier.Clear();
            Frontier.AddRange(NextFrontier);
        }
        Frontier.Clear();
        NextFrontier.Clear();
    }

    private static void TryGrow(int x, int y, int slot, int step)
    {
        if (RawAt(x, y) != (Grow | slot) || step > GrowMin && step > GrowReach(x, y)) return;
        int t = y * _width + x;
        _raw[t] = Core | slot;
        NextFrontier.Add(t);
    }

    private static int GrowReach(int x, int y) =>
        GrowMin + Math.Min(GrowSteps - GrowMin, (int)(SmoothBorders.BorderNoise(x, y, GrowSeed) * (GrowSteps - GrowMin + 1)));

    // 地形不对的格：离外面(不是无人区的陆地)ReachSteps 步以内连得出去的交给画布(记 0，写回时记 Claim)，连不出去的是洞
    private static void ReachOut()
    {
        Frontier.Clear();
        foreach (int id in AList)
        {
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize && y < _height; y++)
            for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize && x < _width; x++)
            {
                int raw = _raw[y * _width + x];
                if (raw < 0 || raw != 0 && (raw & StateMask) != Grow) continue;
                if (IsMiss(x + 1, y) || IsMiss(x - 1, y) || IsMiss(x, y + 1) || IsMiss(x, y - 1)) Frontier.Add(y * _width + x);
            }
        }
        for (int step = 0; step < ReachSteps && Frontier.Count > 0; step++)
        {
            NextFrontier.Clear();
            foreach (int t in Frontier)
            {
                int x = t % _width, y = t / _width;
                TryReach(x + 1, y);
                TryReach(x - 1, y);
                TryReach(x, y + 1);
                TryReach(x, y - 1);
            }
            Frontier.Clear();
            Frontier.AddRange(NextFrontier);
        }
        Frontier.Clear();
        NextFrontier.Clear();
    }

    private static bool IsMiss(int x, int y)
    {
        int raw = RawAt(x, y);
        return raw >= 0 && (raw & StateMask) == Miss;
    }

    // 连得出去的：留着槽号、加这个记号(写回时记 Claim)
    private const int Claimed = 1 << 18;

    private static void TryReach(int x, int y)
    {
        if (!IsMiss(x, y)) return;
        int t = y * _width + x;
        _raw[t] = Claimed | (_raw[t] & 0xFFFF);
        NextFrontier.Add(t);
    }

    // 写回要写的地块；变了的地块(连同周围是不是无人区变了的)让画布重算城的轮廓、图层重画。返回变了几块
    private static int WriteBack()
    {
        int changedZones = 0;
        foreach (int id in DList)
        {
            int zx = id % _zonesX, zy = id / _zonesX;
            bool changed = false, touched = false;
            for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize && y < _height; y++)
            for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize && x < _width; x++)
            {
                int t = y * _width + x;
                int raw = _raw[t];
                int value = 0;
                if (raw > 0)
                {
                    int slot = raw & 0xFFFF;
                    if ((raw & Claimed) != 0) value = ClaimBit | slot;
                    else if ((raw & StateMask) == Core) value = slot;
                    else if ((raw & StateMask) == Miss) value = HoleBit | slot;
                }
                if (value != 0) touched = true;
                if (_tile[t] == value) continue;
                _tile[t] = (ushort)value;
                changed = true;
            }
            _touched[id] = touched;
            bool reshape = changed || FlippedNear(zx, zy);
            if (!reshape) continue;
            _stamp[id] = unchecked(_stamp[id] + 1);
            SmoothBorders.WildReshaped(id);
            if (!changed) continue;
            changedZones++;
            TileZone zone = _zones[id];
            if (zone != null) WildernessOverlay.MarkAround(zone);
        }
        return changedZones;
    }

    // 周围 3×3 块有是不是无人区变了的(画布算城的轮廓时跳过无人区这个候选，跟着变)
    private static bool FlippedNear(int zx, int zy)
    {
        if (Flipped.Count == 0) return false;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = zx + dx, ny = zy + dy;
            if (nx >= 0 && nx < _zonesX && ny >= 0 && ny < _zonesY && Flipped.Contains(ny * _zonesX + nx)) return true;
        }
        return false;
    }
}
