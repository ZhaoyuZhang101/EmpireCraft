using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 军团过城墙(用户 2026-10-21 定"军团只能走关口")：城墙是一格宽的线，军团按地块(8×8 格)走，这里判两块相邻地块之间隔没隔城墙。
//   · 地块归哪一侧：去掉城墙格(原版城墙、图层城墙，关门也算墙)后，地块里最大的一片连通格(四方向)是它的主体——
//     城墙从地块中间穿过时，地块算在格子多的那一边；整块涂满城墙的地块没有主体；
//   · 两块相邻地块：横竖相邻看这两块，斜着相邻看它们所在的 2×2 四块；从一块的主体只走非城墙格(四方向，
//     所以斜接的城墙缝钻不过去，同 MapWallSystem.CutsCorner)走得到另一块的主体——不隔墙(城墙尽头绕得过去也算)；
//     走不到、但打开某座关城的关门(关城压着的城墙格，见 WallGateSystem)就走得到——隔墙有关门，记下是哪座关城；
//     开哪座关城都走不到——隔墙无关门。没有主体的地块：两块里有关门算有关门，否则隔墙；
//   · 关门放行同 WallGateSystem.LetsThrough，按关城所在那块地：它的控制者(被敌军占着就是占领者)与同一势力、
//     并肩作战的友邦、同盟才放，别人(连同不知是谁的)不放——关城那块地易手，关门就归新主人；
//     城池全图筑的城墙上的城门(见 CityPlanWorks)同关门，按城门所在那块地放行。
// 用的地方：军团寻路(CorpsSystem.FindPath / Nearer / NextStep，飞机不受限，海上运兵不受影响)、
//   战线隔城墙攻坚(FrontSystem.WallBetween)。纯净地图与普通地图都一样判(普通地图下原版城墙格本来就挡人)。
// 每对地块算一次记下(地块编号 × 9 个方向的一张表，查表 O(1))；有城墙的地块先列一张表，窗口里都没有城墙的直接算不隔墙。
// 图层城墙、关门、新画的原版城墙(MapWallSystem.Version / Revision 变了)整张作废；原版城墙格被改、被毁
// (MapWallPatch → TerrainResistance.InvalidateWall)只作废那块地与四周八块。不存档：读档后现算。
public static class WallPassage
{
    // 0 没算过、1 不隔墙、2 隔墙无关门、3 隔墙有关门
    private const byte Unknown = 0, Open = 1, Walled = 2, Gated = 3;
    // 地块边长(格，Config.CITY_ZONE_SIZE)：一块地 64 格正好一个 ulong
    private const int Side = 8;
    private const float LogSeconds = 3f;

    private sealed class Mask
    {
        // 城墙格(含关门)、主体
        public ulong Wall, Main;
        // 各座关城(所在地块)压在这块地上的关门
        public List<(TileZone keeper, ulong gates)> Gates;
    }

    private static readonly Mask NoWall = new() { Wall = 0UL, Main = ulong.MaxValue };
    private static readonly Mask Outside = new() { Wall = ulong.MaxValue, Main = 0UL };

    // 地块对(地块编号 × 9 + (dx + 1) × 3 + dy + 1)
    private static byte[] _pairs = new byte[0];
    // 隔墙有关门的地块对：哪几座关城开门就过得去(按关城所在地块)
    private static readonly Dictionary<int, TileZone[]> Keepers = new();
    // 地块里有没有城墙格；待重查的地块
    private static bool[] _walled = new bool[0], _dirty = new bool[0];
    private static readonly List<TileZone> Dirty = new();
    private static readonly Dictionary<int, Mask> Masks = new();
    private static List<(int x, int y, int reach, City city, TileZone zone)> _forts;
    private static object _world;
    private static int _version = int.MinValue, _revision = int.MinValue, _walledZones = -1;
    private static bool _any;
    private static float _loggedAt = -100f;

    // 判定用的工作行(窗口最大 2×2 块 = 16×16 格，一行一个 uint)
    private static readonly uint[] OpenRows = new uint[16], TryRows = new uint[16], StartRows = new uint[16],
        GoalRows = new uint[16], ReachRows = new uint[16];
    private static readonly TileZone[] Window = new TileZone[4];
    private static readonly List<TileZone> Candidates = new(), Found = new();

    public static void ResetWorldState()
    {
        _pairs = new byte[0];
        Keepers.Clear();
        _walled = new bool[0];
        _dirty = new bool[0];
        Dirty.Clear();
        Masks.Clear();
        _forts = null;
        _world = null;
        _version = _revision = int.MinValue;
        _walledZones = -1;
        _any = false;
    }

    // 地图上有城墙(读档、改了城墙后按需重算)：没有就不必逐对查
    public static bool Active => Ensure();

    // 原版城墙格被改、被毁(见 TerrainResistance.InvalidateWall)：那块地原本有城墙才作废，下次查时重查
    // (新画的城墙经 MapWallSystem.Revision 整张重算)
    public static void InvalidateZone(TileZone zone)
    {
        if (zone == null || zone.id < 0 || zone.id >= _walled.Length || !_walled[zone.id] || _dirty[zone.id]) return;
        _dirty[zone.id] = true;
        Dirty.Add(zone);
    }

    // 从 from 走进相邻的 to 被城墙挡住：隔墙，且没有对 side 开着的关门(side 为空当外人，关门不开)
    public static bool Blocks(TileZone from, TileZone to, Kingdom side) => Ensure() && BlocksKnown(from, to, side);

    // 同 Blocks，调用方已确认过 Active(寻路里逐邻查，省掉每次的版本比对)
    public static bool BlocksKnown(TileZone from, TileZone to, Kingdom side)
    {
        byte state = State(from, to, out int slot);
        if (state == Open) return false;
        if (state == Walled) return true;
        return !Opens(slot, side);
    }

    private static bool Opens(int slot, Kingdom side)
    {
        if (side?.data == null || !Keepers.TryGetValue(slot, out TileZone[] keepers)) return false;
        foreach (TileZone keeper in keepers)
            if (WallGateSystem.LetsThrough(keeper, side)) return true;
        return false;
    }

    private static byte State(TileZone from, TileZone to, out int slot)
    {
        slot = -1;
        if (from == null || to == null || from == to) return Open;
        int dx = to.x - from.x, dy = to.y - from.y;
        if (dx < -1 || dx > 1 || dy < -1 || dy > 1) return Open;
        slot = from.id * 9 + (dx + 1) * 3 + dy + 1;
        int mirror = to.id * 9 + (1 - dx) * 3 + 1 - dy;
        if (slot < 0 || slot >= _pairs.Length || mirror < 0 || mirror >= _pairs.Length) return Open;
        byte state = _pairs[slot];
        if (state != Unknown) return state;
        state = Compute(from, to, dx, dy, out TileZone[] keepers);
        _pairs[slot] = state;
        _pairs[mirror] = state;
        if (state == Gated)
        {
            Keepers[slot] = keepers;
            Keepers[mirror] = keepers;
        }
        return state;
    }

    private static bool Ensure()
    {
        MapBox world = World.world;
        if (world?.zone_calculator?.zones == null || world.tiles_list == null) return false;
        if (ReferenceEquals(_world, world) && _version == MapWallSystem.Version && _revision == MapWallSystem.Revision)
        {
            if (Dirty.Count > 0) Refresh();
            return _any;
        }
        if (!Config.game_loaded || SmoothLoader.isLoading()) return false;
        Rebuild(world);
        return _any;
    }

    // 整张重算：按城墙格(图层城墙的数据、原版城墙顶层格)列出有城墙的地块，地块对等用到时再算
    private static void Rebuild(MapBox world)
    {
        _world = world;
        _version = MapWallSystem.Version;
        _revision = MapWallSystem.Revision;
        int size = world.zone_calculator.zones.Count;
        if (_walled.Length != size)
        {
            _walled = new bool[size];
            _dirty = new bool[size];
            _pairs = new byte[size * 9];
        }
        else
        {
            Array.Clear(_walled, 0, _walled.Length);
            Array.Clear(_dirty, 0, _dirty.Length);
            Array.Clear(_pairs, 0, _pairs.Length);
        }
        Dirty.Clear();
        Masks.Clear();
        Keepers.Clear();
        _forts = null;
        int count = 0;
        WorldTile[] tiles = world.tiles_list;
        foreach (int id in MapWallSystem.Walls.Keys)
            if (id >= 0 && id < tiles.Length) count += Mark(tiles[id]?.zone);
        foreach (TopTileType type in AssetManager.top_tiles?.list ?? new List<TopTileType>())
        {
            if (type?.wall != true) continue;
            foreach (WorldTile tile in type.getCurrentTiles()) count += Mark(tile?.zone);
        }
        _any = count > 0;
        if (count == _walledZones) return;
        _walledZones = count;
        float now = Time.unscaledTime;
        if (now < _loggedAt + LogSeconds && now >= _loggedAt) return;
        _loggedAt = now;
        LogService.LogInfo($"[EmpireCraft][城墙隔断] 有城墙的地块 {count} 块、关门 {WallGateSystem.GateTiles.Count} 格：" +
                           "军团寻路把城墙当障碍，只从对本方开着的关门过");
    }

    private static int Mark(TileZone zone)
    {
        if (zone == null || zone.id < 0 || zone.id >= _walled.Length || _walled[zone.id]) return 0;
        _walled[zone.id] = true;
        return 1;
    }

    // 原版城墙格改了的地块：重查有没有城墙，它与四周八块的地块对作废(斜向的 2×2 窗口也只落在这一圈里)
    private static void Refresh()
    {
        ZoneCalculator zones = World.world.zone_calculator;
        foreach (TileZone zone in Dirty)
        {
            if (zone.id < 0 || zone.id >= _walled.Length) continue;
            _dirty[zone.id] = false;
            bool walled = false;
            foreach (WorldTile tile in zone.tiles)
                if (IsWall(tile))
                {
                    walled = true;
                    break;
                }
            _walled[zone.id] = walled;
            if (walled) _any = true;
            Masks.Remove(zone.id);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (zones.getZone(zone.x + dx, zone.y + dy) is TileZone near && near.id >= 0 && near.id * 9 + 9 <= _pairs.Length)
                    Array.Clear(_pairs, near.id * 9, 9);
        }
        Dirty.Clear();
        _forts = null;
    }

    private static bool IsWall(WorldTile tile) => tile != null && (tile.Type?.wall == true || MapWallSystem.Contains(tile));

    // 两块相邻地块之间：窗口里先只走非城墙格，走不到再逐座关城开门试
    private static byte Compute(TileZone a, TileZone b, int dx, int dy, out TileZone[] keepers)
    {
        keepers = null;
        ZoneCalculator zones = World.world.zone_calculator;
        int ox = Math.Min(a.x, b.x), oy = Math.Min(a.y, b.y);
        int columns = dx != 0 ? 2 : 1, rows = dy != 0 ? 2 : 1, height = rows * Side;
        bool walled = false;
        for (int i = 0; i < Window.Length; i++) Window[i] = null;
        for (int zy = 0; zy < rows; zy++)
        for (int zx = 0; zx < columns; zx++)
        {
            TileZone zone = zones.getZone(ox + zx, oy + zy);
            Window[zy * 2 + zx] = zone;
            if (zone != null && zone.id >= 0 && zone.id < _walled.Length && _walled[zone.id]) walled = true;
        }
        if (!walled) return Open;
        // 窗口里有哪几座关城的关门
        Candidates.Clear();
        foreach (TileZone zone in Window)
        {
            List<(TileZone keeper, ulong gates)> gates = zone == null ? null : MaskOf(zone).Gates;
            if (gates == null) continue;
            foreach (var group in gates)
                if (!Candidates.Contains(group.keeper)) Candidates.Add(group.keeper);
        }
        Mask first = MaskOf(a), second = MaskOf(b);
        Found.Clear();
        if (first.Main == 0UL || second.Main == 0UL)
        {
            // 整块涂满城墙的地块：两块里有关门就算有关门
            foreach (Mask mask in new[] { first, second })
                if (mask.Gates != null)
                    foreach (var group in mask.Gates)
                        if (!Found.Contains(group.keeper)) Found.Add(group.keeper);
        }
        else
        {
            Array.Clear(OpenRows, 0, OpenRows.Length);
            Array.Clear(StartRows, 0, StartRows.Length);
            Array.Clear(GoalRows, 0, GoalRows.Length);
            for (int zy = 0; zy < rows; zy++)
            for (int zx = 0; zx < columns; zx++)
            {
                TileZone zone = Window[zy * 2 + zx];
                Mask mask = MaskOf(zone);
                Place(OpenRows, ~mask.Wall, zx, zy);
                if (zone == a) Place(StartRows, mask.Main, zx, zy);
                if (zone == b) Place(GoalRows, mask.Main, zx, zy);
            }
            if (Reaches(OpenRows, height)) return Open;
            foreach (TileZone keeper in Candidates)
            {
                Array.Copy(OpenRows, TryRows, OpenRows.Length);
                for (int zy = 0; zy < rows; zy++)
                for (int zx = 0; zx < columns; zx++)
                {
                    List<(TileZone keeper, ulong gates)> gates = MaskOf(Window[zy * 2 + zx]).Gates;
                    if (gates == null) continue;
                    foreach ((TileZone owner, ulong bits) in gates)
                        if (owner == keeper) Place(TryRows, bits, zx, zy);
                }
                if (Reaches(TryRows, height)) Found.Add(keeper);
            }
        }
        if (Found.Count == 0) return Walled;
        keepers = Found.ToArray();
        return Gated;
    }

    // 地块的 8×8 格(第 ly 行在 ulong 的第 ly×8 位起)放进窗口的行
    private static void Place(uint[] rows, ulong bits, int zx, int zy)
    {
        for (int ly = 0; ly < Side; ly++)
            rows[zy * Side + ly] |= (uint)((bits >> (ly * Side)) & 0xFFUL) << (zx * Side);
    }

    // 从 StartRows 只走 open 的格(四方向)走得到 GoalRows 吗
    private static bool Reaches(uint[] open, int height)
    {
        for (int r = 0; r < height; r++) ReachRows[r] = StartRows[r] & open[r];
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int r = 0; r < height; r++)
            {
                uint row = ReachRows[r];
                uint grown = row | row << 1 | row >> 1;
                if (r > 0) grown |= ReachRows[r - 1];
                if (r + 1 < height) grown |= ReachRows[r + 1];
                grown &= open[r];
                if (grown == row) continue;
                ReachRows[r] = grown;
                changed = true;
                if ((grown & GoalRows[r]) != 0U) return true;
            }
        }
        for (int r = 0; r < height; r++)
            if ((ReachRows[r] & GoalRows[r]) != 0U) return true;
        return false;
    }

    private static Mask MaskOf(TileZone zone)
    {
        if (zone == null) return Outside;
        if (zone.id < 0 || zone.id >= _walled.Length || !_walled[zone.id]) return NoWall;
        if (Masks.TryGetValue(zone.id, out Mask mask)) return mask;
        mask = new Mask();
        int left = zone.x * Side, bottom = zone.y * Side;
        ulong present = 0UL;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile == null) continue;
            int lx = tile.x - left, ly = tile.y - bottom;
            if (lx < 0 || lx >= Side || ly < 0 || ly >= Side) continue;
            ulong bit = 1UL << (ly * Side + lx);
            present |= bit;
            if (!IsWall(tile)) continue;
            mask.Wall |= bit;
            if (!WallGateSystem.IsGate(tile) || KeeperOf(tile) is not TileZone keeper) continue;
            mask.Gates ??= new List<(TileZone keeper, ulong gates)>();
            int index = mask.Gates.FindIndex(group => group.keeper == keeper);
            if (index < 0) mask.Gates.Add((keeper, bit));
            else mask.Gates[index] = (keeper, mask.Gates[index].gates | bit);
        }
        // 缺的格当墙(不会有，保险)
        mask.Wall |= ~present;
        mask.Main = MainOf(~mask.Wall);
        Masks[zone.id] = mask;
        return mask;
    }

    // 关门归哪座关城：方城(中心起 WallGateSystem.Reach 格)盖着它、同一座城的那座(同 WallGateSystem 的算法)；返回关城所在地块。
    // 城池全图筑的城门没有关城，返回城门所在的地块
    private static TileZone KeeperOf(WorldTile tile)
    {
        if (_forts == null)
        {
            _forts = new List<(int x, int y, int reach, City city, TileZone zone)>();
            foreach (RuralSettlement fort in RuralSettlementSystem.Forts())
            {
                if (fort.ruined_at >= 0d) continue;
                TileZone zone = World.world.GetTile(fort.x, fort.y)?.zone;
                City city = zone?.city;
                if (city?.data == null || city.isRekt()) continue;
                _forts.Add((fort.x, fort.y, WallGateSystem.Reach(fort), city, zone));
            }
        }
        City at = tile.zone?.city;
        if (at == null) return null;
        foreach ((int x, int y, int reach, City city, TileZone zone) in _forts)
            if (city == at && Math.Abs(tile.x - x) <= reach && Math.Abs(tile.y - y) <= reach) return zone;
        // 城池全图筑的城墙上的城门(见 CityPlanWorks)：守门的是城门所在那块地(被敌军占着就归占领者)
        return WallGateSystem.IsCityGate(tile) ? tile.zone : null;
    }

    // 地块的主体：非城墙格里最大的一片(四方向连通)；一样大取先找到的
    private static ulong MainOf(ulong open)
    {
        ulong best = 0UL, left = open;
        int bestCount = 0;
        while (left != 0UL)
        {
            ulong part = Grow(left & (~left + 1UL), open);
            int count = Count(part);
            if (count > bestCount)
            {
                best = part;
                bestCount = count;
            }
            left &= ~part;
        }
        return best;
    }

    // 8×8 格里从 seed 四方向长满(左右移一位时去掉跨行的那一列)
    private static ulong Grow(ulong seed, ulong open)
    {
        const ulong notFirstColumn = 0xFEFEFEFEFEFEFEFEUL, notLastColumn = 0x7F7F7F7F7F7F7F7FUL;
        ulong reach = seed & open, before;
        do
        {
            before = reach;
            reach |= ((reach << 1) & notFirstColumn) | ((reach >> 1) & notLastColumn) | (reach << Side) | (reach >> Side);
            reach &= open;
        } while (reach != before);
        return reach;
    }

    private static int Count(ulong bits)
    {
        bits -= (bits >> 1) & 0x5555555555555555UL;
        bits = (bits & 0x3333333333333333UL) + ((bits >> 2) & 0x3333333333333333UL);
        bits = (bits + (bits >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
        return (int)((bits * 0x0101010101010101UL) >> 56);
    }
}
