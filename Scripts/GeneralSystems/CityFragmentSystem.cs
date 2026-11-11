using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城地碎块交给四周的城(用户 2026-11-08 截图明清鼎革沙州一带还有飞地，问答选"交给四周挨得最多的城")：
// 剧本开局分的地本来就有不连着的(沙州县 18 块分成四片)，高原、荒漠划成无人区后又切开一些；原版"断开的地放弃"(CityZoneAbandon)
// 在剧本里被领土扩张法则拦着(见 CityExpansionPatch.BeforeAbandonZones)，这些碎地就一直陷在别国的地里。
// 做法：一座城的城地连同跟着它的无人区(WildernessSystem.OriginOf)按东西南北四邻连成片——画布按 3×3 块平滑，
// 只斜着碰一个角的两块画出来是分开的；城心那块所在的片是本体(城心不归它时取最大的一片)。别的片不到 WildernessSystem.MinZones 块、
// 片外四邻每一块都是有主的地(别的城的地，或显示上归了某国的无人区)，就把片里的城地交出去：
// 本国的城优先，否则挨得最多的那国(无人区按显示的归属算)、那国里挨得最多的城，一样多的编号小的先。
// 不动的：片外挨着水、无主地、永久中立区、地图边的(海岛、海外领地)；片里跟着本城的无人区(交完不挨本城了，按碎块并走，
// 见 WildernessSystem.RefreshFragments)；城市边界固定开着时(剧本开局、读档收尾 BordersLocked 为假，照做)；
// 被占的城不交也不收、正在被战线推进的地块不交(同 CityTerritoryProtection.PreserveWarLayout 护的那些)；不交给正跟本国打仗的国
// (陷在敌国地里的就留着，等打下来或停战)——只是交战不拦：11-08 那个存档三十多处碎地的城多半在打仗，拦了就一处也交不出；
// 一块地交过一次，还在接收的城手里就不再挪(防来回倒)。
// 收地照常过 CityExpansionPatch.CanAddZone(每城地块上限等)，这座城收不下换下一座，都收不下就留着。
// 时机：无人区每次划完(WildernessSystem.Build 之后)，平时每年再看一次(交战的城停战后再交)。
// 关掉无人区设置时(见 WildernessSystem.SetEnabled)交出去的碎块还回原城(用户 2026-11-12 问答选"碎块一起还")：碎块是无人区切出来的，
// 关掉就该回到原来的图；还在接收的城手里、原城还在的才还。交出去的记录随存档(SaveData.city_fragment_moves)。
public static class CityFragmentSystem
{
    // 交出去的地块 → 原来哪座城(连交几次的记最早那座)、眼下交给了哪座城
    private static readonly Dictionary<int, (City from, City to)> Moved = new();
    // 读档读进来、还没对上城的记录(进世界后头一回用到时对上，见 Resolve)
    private static List<CityFragmentMove> _loaded;
    private static double _lastRun = -1d;

    public static void ResetWorldState()
    {
        Moved.Clear();
        _loaded = null;
        _lastRun = -1d;
    }

    public static void Load(List<CityFragmentMove> saved) => _loaded = saved is { Count: > 0 } ? saved : null;

    public static List<CityFragmentMove> Save()
    {
        if (_loaded != null) return _loaded;
        var result = new List<CityFragmentMove>(Moved.Count);
        foreach (KeyValuePair<int, (City from, City to)> pair in Moved.OrderBy(pair => pair.Key))
        {
            (City from, City to) = pair.Value;
            if (from?.data == null || to?.data == null || from.isRekt() || to.isRekt()) continue;
            result.Add(new CityFragmentMove { zone = pair.Key, from = from.id, to = to.id });
        }
        return result;
    }

    private static void Resolve()
    {
        if (_loaded == null || World.world?.cities == null) return;
        List<CityFragmentMove> loaded = _loaded;
        _loaded = null;
        foreach (CityFragmentMove move in loaded)
        {
            City from = World.world.cities.get(move.from), to = World.world.cities.get(move.to);
            if (move.zone >= 0 && from != null && to != null && from != to) Moved[move.zone] = (from, to);
        }
    }

    // 一块地交出去后记什么：上回交进 giver 手里的接着记最早那座城，否则从 giver 起记
    internal static (T from, T to) Chain<T>((T from, T to)? before, T giver, T receiver) where T : class =>
        before is { } last && ReferenceEquals(last.to, giver) ? (last.from, receiver) : (giver, receiver);

    // 还不还：还在接收的城手里、原城还在的才还
    internal static bool Restorable(bool heldByReceiver, bool fromAlive) => heldByReceiver && fromAlive;

    private static void Note(int zoneId, City giver, City receiver)
    {
        (City from, City to)? before = Moved.TryGetValue(zoneId, out (City from, City to) last) ? last : null;
        (City from, City to) record = Chain(before, giver, receiver);
        if (record.from == record.to) Moved.Remove(zoneId);
        else Moved[zoneId] = record;
    }

    // 关掉无人区设置：交出去的碎块还回原城；返回还了几块。记录随即清掉
    public static int RestoreAll()
    {
        Resolve();
        ZoneCalculator calc = World.world?.zone_calculator;
        if (Moved.Count == 0 || calc == null)
        {
            Moved.Clear();
            return 0;
        }
        var touched = new HashSet<City>();
        int count = 0;
        foreach (KeyValuePair<int, (City from, City to)> pair in Moved.OrderBy(pair => pair.Key).ToList())
        {
            TileZone zone = calc.getZoneByID(pair.Key);
            (City from, City to) = pair.Value;
            if (zone == null || !Restorable(zone.city == to, from?.data != null && !from.isRekt())) continue;
            using (GamePatches.CityExpansionPatch.Manual()) from.addZone(zone);
            if (zone.city != from) continue;
            touched.Add(from);
            touched.Add(to);
            count++;
        }
        Moved.Clear();
        if (touched.Count == 0) return 0;
        CityDistricts.Reset();
        foreach (City city in touched)
            if (city?.data != null && !city.isRekt()) GamePatches.PureMapPatch.Redraw(city);
        calc.setDrawnZonesDirty();
        return count;
    }

    // 无人区划完(WildernessSystem.Build)：当场看一遍
    public static void AfterBuild()
    {
        try
        {
            Run();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][城地碎块] 出错: {exception}");
        }
    }

    // 每帧(WildernessSystem.Tick 里，划区不忙时)：一年看一次
    public static void Tick()
    {
        double now = World.world.getCurWorldTime();
        if (_lastRun >= 0d && now >= _lastRun && Date.getYearsSince(_lastRun) < 1) return;
        AfterBuild();
    }

    private static void Run()
    {
        MapBox world = World.world;
        if (world?.zone_calculator?.zones == null || world.cities == null) return;
        _lastRun = world.getCurWorldTime();
        if (CityTerritoryProtection.BordersLocked) return;
        Resolve();
        var report = new List<string>();
        int moved = Transfer(world.zone_calculator.zones, world.cities.list, report);
        if (moved <= 0) return;
        LogService.LogInfo($"[EmpireCraft][城地碎块] 交出 {report.Count} 处 {moved} 块：" + string.Join("；", report.Take(12)) +
                           (report.Count > 12 ? $" 等" : ""));
    }

    // 地块跟着哪座城：城的地块归那座城；无人区跟着 OriginOf 那座城(永久中立的不算)；别的为 null
    private static City HolderOf(TileZone zone)
    {
        if (zone == null) return null;
        City city = zone.city;
        if (city != null) return Alive(city) ? city : null;
        if (!WildernessSystem.IsWild(zone) || WildernessSystem.IsNeutral(zone)) return null;
        City origin = WildernessSystem.OriginOf(zone);
        return Alive(origin) ? origin : null;
    }

    private static bool Alive(City city) =>
        city?.data != null && !city.isRekt() && city.kingdom?.data != null && !city.kingdom.isRekt() && !city.kingdom.wild;

    // 交不交、收不收：古战争模组的城、正在撤的城、被占的城都不碰
    private static bool Usable(City city) =>
        Alive(city) && !AncientWarfareCompatibility.OwnsObject(city) && !CityTerritoryProtection.Dissolving.Contains(city.id) &&
        !Occupied(city);

    private static bool Occupied(City city) => city.GetOrCreate().OccupiedStatus?.Any(pair => pair.Value?.Count > 0) == true;

    // 这块地正被战线逐格推进
    private static bool Pushed(TileZone zone) => FrontSystem.SmoothPushOn(zone, out _, out _);

    private static bool AtWar(Kingdom a, Kingdom b) => a != b && a.isInWarWith(b);

    // 返回交出的城地块数；report 记每一处"城 → 城 几块"
    private static int Transfer(List<TileZone> zones, List<City> cities, List<string> report)
    {
        int size = 0;
        foreach (TileZone zone in zones)
            if (zone != null && zone.id >= size) size = zone.id + 1;
        var byId = new TileZone[size];
        var holder = new City[size];
        var ownWild = new Dictionary<City, List<TileZone>>();
        foreach (TileZone zone in zones)
        {
            if (zone == null || zone.id < 0) continue;
            byId[zone.id] = zone;
            City city = HolderOf(zone);
            holder[zone.id] = city;
            if (city == null || zone.city != null) continue;
            if (!ownWild.TryGetValue(city, out List<TileZone> list)) ownWild[city] = list = new List<TileZone>();
            list.Add(zone);
        }
        var mark = new int[size];
        int stamp = 0, total = 0;
        var pieces = new List<List<TileZone>>();
        var queue = new List<TileZone>();
        foreach (City city in cities.ToList())
        {
            if (city?.zones == null || city.zones.Count < 2 || !Usable(city)) continue;
            // 城地连同跟着它的无人区，按四邻连成片
            stamp++;
            pieces.Clear();
            IEnumerable<TileZone> starts = ownWild.TryGetValue(city, out List<TileZone> wild) ? city.zones.Concat(wild) : city.zones;
            foreach (TileZone start in starts.ToList())
            {
                if (start == null || start.id < 0 || start.id >= size || mark[start.id] == stamp || holder[start.id] != city) continue;
                var piece = new List<TileZone> { start };
                mark[start.id] = stamp;
                for (int i = 0; i < piece.Count; i++)
                {
                    TileZone[] near = piece[i].neighbours;
                    if (near == null) continue;
                    foreach (TileZone next in near)
                    {
                        if (next == null || next.id < 0 || next.id >= size || mark[next.id] == stamp || holder[next.id] != city) continue;
                        mark[next.id] = stamp;
                        piece.Add(next);
                    }
                }
                pieces.Add(piece);
            }
            if (pieces.Count < 2) continue;
            TileZone seat = city.getTile()?.zone;
            List<TileZone> body = seat != null && seat.city == city ? pieces.FirstOrDefault(piece => piece.Contains(seat)) : null;
            body ??= pieces.OrderByDescending(piece => piece.Count).First();
            foreach (List<TileZone> piece in pieces)
            {
                if (ReferenceEquals(piece, body) || piece.Count >= WildernessSystem.MinZones) continue;
                List<City> receivers = Receivers(city, piece, byId, holder, size);
                if (receivers == null || receivers.Count == 0) continue;
                int given = 0;
                City first = null;
                foreach (TileZone zone in piece)
                {
                    if (zone.city != city || Pushed(zone)) continue;
                    if (Moved.TryGetValue(zone.id, out (City from, City to) before) && before.to == city) continue;
                    foreach (City receiver in receivers)
                    {
                        receiver.addZone(zone);
                        if (zone.city != receiver) continue;
                        Note(zone.id, city, receiver);
                        holder[zone.id] = receiver;
                        first ??= receiver;
                        given++;
                        break;
                    }
                }
                if (given == 0) continue;
                total += given;
                report.Add($"{city.name}→{first.name} {given} 块");
            }
        }
        return total;
    }

    // 这一片交给谁(按先后试)：片外四邻每一块都得有主，否则 null(海岛、海外领地、挨着无主地的不动)
    private static List<City> Receivers(City city, List<TileZone> piece, TileZone[] byId, City[] holder, int size)
    {
        var inside = new HashSet<TileZone>(piece);
        var edges = new Dictionary<City, int>();
        var kingdoms = new Dictionary<Kingdom, int>();
        foreach (TileZone zone in piece)
        {
            TileZone[] near = zone.neighbours;
            // 地图边上的块(四邻不全)
            if (near == null || near.Length < 4) return null;
            foreach (TileZone next in near)
            {
                if (next == null || inside.Contains(next)) continue;
                City other = next.id >= 0 && next.id < size ? holder[next.id] : null;
                if (other == null || other == city) return null;
                // 无人区按显示的归属算哪国挨着；并成别国碎块的(显示的国不是它跟着的城所在的国)只算那国挨着，
                // 它跟着的城不收(收了又是一块飞地)
                Kingdom shown = next.city == null ? WildernessSystem.OwnerOf(next) : other.kingdom;
                if (shown == null) return null;
                kingdoms.TryGetValue(shown, out int sum);
                kingdoms[shown] = sum + 1;
                if (shown != other.kingdom) continue;
                edges.TryGetValue(other, out int count);
                edges[other] = count + 1;
            }
        }
        Kingdom home = city.kingdom;
        return edges.Keys.Where(other => Usable(other) && !AtWar(home, other.kingdom))
            .OrderByDescending(other => other.kingdom == home)
            .ThenByDescending(other => kingdoms[other.kingdom])
            .ThenBy(other => other.kingdom.id)
            .ThenByDescending(other => edges[other])
            .ThenBy(other => other.id)
            .ToList();
    }
}

// 存档里的一条城地碎块交接(见 CityFragmentSystem)：地块、原来的城、交给的城
public class CityFragmentMove
{
    public int zone;
    public long from;
    public long to;
}
