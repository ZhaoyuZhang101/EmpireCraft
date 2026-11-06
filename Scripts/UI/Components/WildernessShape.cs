using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 平滑国界下无人区的轮廓(用户 2026-11-06 "要把无人区当作特殊的无人城市来看待……他和城市之间是平级""无人区分割只是单纯的行政的分割,
// 它本身还是属于一个整体的, 可以参考战线的占领模式"，对照的样图国界是干净的圆滑曲线)：
//   · 一片无人区同一座城一样是一个整体：每格按周围 3×3 块做 B 样条加噪声(同城界，见 SmoothBorders.CityAt)，候选三类——各片无人区、
//     有城的地、无主地，定出这片无人区的轮廓；无人区与城之间、两片无人区之间都是和城界一个样子的圆弧、起伏；
//   · 不再逐格贴地形(10-29 起按山地、沙地、雪地逐格判，边沿跟着原版地形的像素走，国界一圈圈锯齿、轮廓里戳满小洞、
//     伸出去的碎格子，剧本版图看着乱)；地形只决定哪些地块划成无人区(WildernessSystem 归类)，不再决定一格归谁；
//   · 轮廓里归谁(行政上分给几座城、几个国家)由画布按各块的归属平滑(见 SmoothBorders.WildFillAt)，同战线被占的城：
//     轮廓不动，里面按块的实际控制方分出圆滑的分界；
//   · 何时重算：区域变了(WildernessSystem.Version，只比各块归哪片、有没有城)、无人区周围一圈的地块有没有城变了(每帧对一遍)、
//     水陆变了(ZonePaintBudget.TerrainChanged，攒 TerrainDelay 秒一起算)：一格只看周围 3×3 块，只重算变了的地块连同周围一圈，
//     算完让画布重算这几块的城的轮廓(SmoothBorders.WildReshaped)、无人区图层重画；整张算只在开平滑国界、进图、换世界时，日志记用时。
// 平滑国界关着、无人区关着或没有区域时不算(Active 为假)，各处照按地块画。
public static class WildernessShape
{
    private const int ZoneSize = Config.CITY_ZONE_SIZE;
    // 地块候选变了重算几圈(一格看周围 3×3 块)；水陆变了只算这一块
    private const int KeyRadius = 1, TerrainRadius = 0;
    private const float TerrainDelay = 0.5f, RetrySeconds = 30f;

    // 一格的结果：槽号(0 不是无人区)
    private const int SlotMask = 0x1FFF;
    // 块的候选：有城的地、无主地；无人区是槽号(≥1)
    private const int CityKey = -1, EmptyKey = 0;
    private const int CitySeed = 1013904223;

    private static object _world;
    private static int _width, _height, _zonesX, _zonesY, _zoneCount, _version = int.MinValue, _frame = -1;
    private static bool _active, _fullPass;
    private static float _due = float.MaxValue, _retryAt;
    private static TileZone[] _zones = Array.Empty<TileZone>();
    // 各格的结果；各块：候选、重算一次加一的记号(画布城的轮廓、图层重画据此比对)、有没有无人区的格、一圈以内有没有无人区
    private static ushort[] _tile = Array.Empty<ushort>();
    private static int[] _key = Array.Empty<int>(), _stamp = Array.Empty<int>();
    private static bool[] _touched = Array.Empty<bool>(), _near = Array.Empty<bool>();
    // 一圈以内有无人区的地块(连同无人区自己)：每帧对一遍有没有城
    private static readonly List<int> Watch = new();
    // 槽号 ↔ 区域(按区域编号沿用槽号)；各槽的噪声种子
    private static readonly List<WildRegion> Slots = new() { null };
    private static readonly List<int> SlotSeeds = new() { 0 };
    private static readonly Dictionary<int, int> SlotOfId = new();
    // 等着重算的地块；是不是无人区变了的地块(周围的城的轮廓要重算)
    private static readonly HashSet<int> Dirty = new(), Flipped = new();
    private static readonly List<int> DList = new();
    private static readonly int[] Keys = new int[9], CandKeys = new int[9];
    private static readonly float[] CandWeights = new float[9];

    // ───────── 查询 ─────────

    internal static bool Active => _active;

    // 这一格在哪片无人区的轮廓里(槽号，不是为 0)。画布让开(不归城)、图层按它填色
    internal static int AreaAt(int tileId) =>
        _active && (uint)tileId < (uint)_tile.Length ? _tile[tileId] & SlotMask : 0;

    // 同 AreaAt(以前贴地形时还有"地形不对、交给画布"的格，两样不同；现在轮廓里的格都算这片)
    internal static int SlotAt(int tileId) => AreaAt(tileId);

    internal static WildRegion RegionOfSlot(int slot) => slot > 0 && slot < Slots.Count ? Slots[slot] : null;

    // 这块地是无人区(画布算城的轮廓时不把它当候选：城的地盘伸到无人区的轮廓为止)
    internal static bool ZoneWild(int zoneId) => _active && (uint)zoneId < (uint)_key.Length && _key[zoneId] > 0;

    // 这块地是哪片无人区(槽号，不是为 0)
    internal static int ZoneSlot(int zoneId) =>
        _active && (uint)zoneId < (uint)_key.Length && _key[zoneId] > 0 ? _key[zoneId] : 0;

    // 这块地里有无人区的格
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
            LogService.LogWarning($"[EmpireCraft][无人区轮廓] 出错，暂按地块画：{exception}");
            _retryAt = now + RetrySeconds;
            Deactivate(true);
        }
    }

    // 一格的样子变了(见 ZonePaintBudget.TerrainChanged)：挨着无人区的，攒一会儿后重算这块(水陆变了，刷一片地只算一回)
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
        _tile = new ushort[_width * _height];
        _key = new int[_zoneCount];
        _stamp = new int[_zoneCount];
        _touched = new bool[_zoneCount];
        _near = new bool[_zoneCount];
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

    // 区域变了：各块的候选重对一遍，变了的连同周围一圈重算(归属国变了 Version 也加一，候选不变就不算)；头一回整张算
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
            // 有城、无主之间变了：只有挨着无人区的才要紧
            if (wild || wasWild || _near[id]) MarkDirty(zone.x, zone.y, KeyRadius, true);
        }
        if (full || flipped) RebuildNear();
        if (!full) return;
        // 整张算：挨着无人区的都算(更远的格子看不到无人区，都是 0)，原先画过的也重写；各片无人区四周的城的轮廓都要重算
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

    // 一圈以内有无人区的地块(连同无人区自己)：每帧对有没有城
    private static void RebuildNear()
    {
        Array.Clear(_near, 0, _near.Length);
        for (int id = 0; id < _zoneCount; id++)
        {
            if (_key[id] <= 0) continue;
            int zx = id % _zonesX, zy = id / _zonesX;
            for (int dy = -KeyRadius; dy <= KeyRadius; dy++)
            for (int dx = -KeyRadius; dx <= KeyRadius; dx++)
            {
                int nx = zx + dx, ny = zy + dy;
                if (nx >= 0 && nx < _zonesX && ny >= 0 && ny < _zonesY) _near[ny * _zonesX + nx] = true;
            }
        }
        Watch.Clear();
        for (int id = 0; id < _zoneCount; id++)
            if (_near[id]) Watch.Add(id);
    }

    // 无人区周围的地块有没有城(建城、扩张、城没了)：变了连同周围一圈重算。先只看有没有城(每帧几千块，不逐块查区域)；
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
        DList.Clear();
        foreach (int id in Dirty)
            if (id >= 0 && id < _zoneCount) DList.Add(id);
        Dirty.Clear();
        int changed = 0;
        using (FrameProfiler.Measure("无人区·轮廓"))
            foreach (int id in DList)
                if (ShapeZone(id)) changed++;
        Flipped.Clear();
        if (full)
            LogService.LogInfo($"[EmpireCraft][无人区轮廓] 重算 {DList.Count} 块(变了 {changed} 块)，用时 {watch.Elapsed.TotalMilliseconds:0} ms");
        DList.Clear();
    }

    // 一块地各格属于哪片无人区(B 样条判给哪类，水面不算)；变了的(连同周围是不是无人区变了的)让画布重算城的轮廓、图层重画。
    // 返回格子变了没有
    private static bool ShapeZone(int id)
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
        bool changed = false, touched = false;
        for (int y = zy * ZoneSize; y < zy * ZoneSize + ZoneSize && y < _height; y++)
        for (int x = zx * ZoneSize; x < zx * ZoneSize + ZoneSize && x < _width; x++)
        {
            int t = y * _width + x;
            int value = 0;
            if (any && !IsWater(x, y))
            {
                int winner = uniform ? center : Winner(x, y, zx, zy);
                if (winner > 0) value = winner & SlotMask;
            }
            if (value != 0) touched = true;
            if (_tile[t] == value) continue;
            _tile[t] = (ushort)value;
            changed = true;
        }
        _touched[id] = touched;
        if (!changed && !FlippedNear(zx, zy)) return false;
        _stamp[id] = unchecked(_stamp[id] + 1);
        SmoothBorders.WildReshaped(id);
        if (!changed) return false;
        TileZone zone = _zones[id];
        if (zone != null) WildernessOverlay.MarkAround(zone);
        return true;
    }

    // 水面同画布(SmoothBorders.IsWater)：岩浆不算水
    private static bool IsWater(int x, int y)
    {
        TileTypeBase type = World.world.GetTile(x, y)?.Type;
        return type == null || type.ocean || type.liquid && !type.lava;
    }

    // B 样条加噪声取胜者(候选在 Keys：本块周围 3×3 块；权重、噪声同 SmoothBorders.CityAt)
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
