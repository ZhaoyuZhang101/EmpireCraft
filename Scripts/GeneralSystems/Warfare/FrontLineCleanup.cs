using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// 孤块归位(用户 2026-11-09 "为什么不能像钢铁雄心三一样保持战线或者维持战线"，选"全线推进，孤块自动归位")：
// 每月交战结算之后整理一遍有地块被占的城，国界保持一条线——
//   · 占领者的孤块：某国在一座城里占着的一片地，往外不穿对方控制的地，走不到己方或友方的城池(PocketZones 块以内走不出去)，
//     这片地和它四周也没有本方的军，就还给城主(断了后路守不住)；
//   · 城主的孤块：城主在一座部分被占的城里剩下的一片地(城池那片不算)，同样走不到己方的城池、四周全是同一个占领者，
//     上面没有城主一方的军，就归那个占领者。
//   有军在里面的是被围的军，归 EncirclementSystem 管，这里不动。
public static class FrontLineCleanup
{
    private const int PocketZones = 24;
    private static readonly HashSet<int> Seen = new();
    private static readonly Queue<TileZone> Frontier = new();

    public static void Monthly()
    {
        if (World.world?.cities == null || !CorpsSystem.Enabled) return;
        List<City> cities = World.world.cities.list.Where(city => city?.data != null && !city.isRekt() && city.HasOccupiedZones()).ToList();
        if (cities.Count == 0) return;
        // 各地块上有哪些国的军
        var troops = new Dictionary<int, List<Kingdom>>();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (corps == null || corps.strength < 1f || corps.embarked) continue;
            if (!troops.TryGetValue(corps.zone_id, out List<Kingdom> here)) troops[corps.zone_id] = here = new List<Kingdom>();
            here.Add(kingdom);
        }
        foreach (City city in cities)
        {
            try
            {
                Tidy(city, troops);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][孤块归位] {city.GetCityName()} 失败: {exception.Message}");
            }
        }
    }

    private static void Tidy(City city, Dictionary<int, List<Kingdom>> troops)
    {
        Kingdom owner = city.kingdom;
        if (owner?.data == null || owner.isRekt() || city.zones == null) return;
        TileZone seat = city.getTile()?.zone;
        // 各块地的控制者
        var byController = new Dictionary<Kingdom, List<TileZone>>();
        foreach (TileZone zone in city.zones)
        {
            if (zone == null) continue;
            Kingdom controller = FrontSystem.Controller(zone);
            if (controller?.data == null) continue;
            if (!byController.TryGetValue(controller, out List<TileZone> list)) byController[controller] = list = new List<TileZone>();
            list.Add(zone);
        }
        int returned = 0, taken = 0;
        // 占领者的孤块还给城主
        foreach (KeyValuePair<Kingdom, List<TileZone>> pair in byController.Where(pair => pair.Key != owner).ToList())
        {
            Kingdom occupier = pair.Key;
            foreach (List<TileZone> piece in Pieces(pair.Value))
            {
                if (!Cut(occupier, piece, troops)) continue;
                foreach (TileZone zone in piece)
                    if (city.RemoveOccupiedTileZone(occupier, zone)) returned++;
            }
        }
        // 城主的孤块(城池那片不算)归围住它的占领者
        if (byController.TryGetValue(owner, out List<TileZone> held))
        {
            foreach (List<TileZone> piece in Pieces(held))
            {
                if (seat != null && piece.Contains(seat)) continue;
                if (!Cut(owner, piece, troops)) continue;
                Kingdom besieger = Surrounder(piece, owner);
                if (besieger == null) continue;
                foreach (TileZone zone in piece)
                    if (city.AddOccupiedTileZone(besieger, zone)) taken++;
            }
        }
        if (returned + taken > 0)
            NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][孤块归位] {city.GetCityName()}：还给城主 {returned} 块，归占领者 {taken} 块");
    }

    // 同一座城里同一控制者的地块，按相邻连成几片
    private static List<List<TileZone>> Pieces(List<TileZone> zones)
    {
        var left = new HashSet<TileZone>(zones);
        var pieces = new List<List<TileZone>>();
        while (left.Count > 0)
        {
            TileZone start = left.First();
            left.Remove(start);
            var piece = new List<TileZone> { start };
            var queue = new Queue<TileZone>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                TileZone zone = queue.Dequeue();
                foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
                {
                    if (next == null || !left.Remove(next)) continue;
                    piece.Add(next);
                    queue.Enqueue(next);
                }
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    // 这片地断了后路：往外不穿敌控的地，走不到 side 一方的城池，PocketZones 块以内就走完了；走到的地方也没有 side 一方的军
    private static bool Cut(Kingdom side, List<TileZone> piece, Dictionary<int, List<Kingdom>> troops)
    {
        Seen.Clear();
        Frontier.Clear();
        foreach (TileZone zone in piece)
        {
            Seen.Add(zone.id);
            Frontier.Enqueue(zone);
        }
        bool enemy = false;
        while (Frontier.Count > 0)
        {
            TileZone zone = Frontier.Dequeue();
            if (troops.TryGetValue(zone.id, out List<Kingdom> here) && here.Any(kingdom => FrontSystem.Friendly(side, kingdom))) return false;
            City city = zone.city;
            if (city?.data != null && !city.isRekt() && city.getTile()?.zone == zone && FrontSystem.Friendly(side, FrontSystem.Controller(zone)))
                return false;
            foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
            {
                if (next == null || Seen.Contains(next.id) || SeaRegionSystem.IsSea(next) || TerrainSystem.Impassable(next) ||
                    !TerrainSystem.Crossable(zone, next)) continue;
                Kingdom controller = FrontSystem.Controller(next);
                if (controller != null && FrontSystem.Hostile(side, controller))
                {
                    enemy = true;
                    continue;
                }
                Seen.Add(next.id);
                if (Seen.Count > PocketZones) return false;
                Frontier.Enqueue(next);
            }
        }
        return enemy;
    }

    // 围住这片地的敌方：四周挨着的敌控地块最多的那一国
    private static Kingdom Surrounder(List<TileZone> piece, Kingdom owner)
    {
        var count = new Dictionary<Kingdom, int>();
        foreach (TileZone zone in piece)
        foreach (TileZone next in zone.neighbours_all ?? Array.Empty<TileZone>())
        {
            Kingdom controller = FrontSystem.Controller(next);
            if (controller?.data == null || !FrontSystem.Hostile(owner, controller)) continue;
            count[controller] = count.TryGetValue(controller, out int n) ? n + 1 : 1;
        }
        return count.Count == 0 ? null : count.OrderByDescending(pair => pair.Value).First().Key;
    }
}
