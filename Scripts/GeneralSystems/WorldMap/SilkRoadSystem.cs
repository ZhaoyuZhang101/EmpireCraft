using System;
using System.Collections.Generic;
using System.Linq;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本的一条丝路(时代文件的 silk 一节，见 Tools/WorldMap/silkroad_source.py)：名、海路与否、沿途各站的经纬度
public sealed class WorldMapSilkRoute
{
    public string name = "";
    public bool sea;
    public List<float[]> pts = new();
    public List<string> stops = new();

    public string Name => WorldMapData.PickParts(name);
}

// 存档里的一条丝路：沿途各站对上的城
public sealed class SilkRouteSave
{
    public string name = "";
    public bool sea;
    public List<long> cities = new();
}

// 丝绸之路(用户 10-30 "预设剧本里面的丝绸之路呢""要符合历史"，问答定"陆上和海上都做""修成路并连通沿途市场")：
//   · 史实走向按时代(汉、三国走河西—西域南北道—葱岭—安息—叙利亚；唐走河西—安西—康国—木鹿—两河；宋金时走西夏—高昌—中亚；
//     明末走嘉峪关—哈密—撒马尔罕—伊斯法罕；宋、唐、明末另有海上丝路)，各站写在时代文件里(见 Tools/WorldMap/silkroad_data.py)；
//   · 开局(进世界安排里，长城关隘之后)：陆路相邻两站之间沿地形找路(走平地，翻山走山口，城墙只从关门过，见 Route)修成路；
//     地表网格上没留出山口的(瓦罕走廊、别迭里山口、县度、兴都库什的哈瓦克山口……)第二回准翻山，翻过的山格平成丘陵，开出山口
//     (RoadPaintSystem.Lay)：等级照所在国家的科技，无主之地是土路，过河的架桥；这些路不因无人养护、战事荒掉，路网图层上画成丝路的颜色；
//   · 沿途各站对上最近的城(StopTiles 格以内)记下，随存档；市场互市(见 CommodityMarketSystem.Trade)时，各条丝路相邻两站的城
//     所属的国家不交战就连通——中间的站没有城、城没了就跳过，丝绸、茶、香料沿路一程程流通；海路只连港口的市场，不画路。
public static class SilkRoadSystem
{
    // 站对上城的远近(格)；找路时两站之间的走廊放宽多少
    private const int StopTiles = 40, CorridorMin = 24;
    private const float CorridorShare = 0.6f;
    private const float HillCost = 1.6f, SandCost = 1.2f, WetCost = 2f, WaterCost = 8f, CityCost = 3f;
    // 开山口时翻山的代价(只在走不通时才准翻，翻得越少越好)
    private const float MountainCost = 30f, SummitCost = 60f;

    private static List<SilkRouteSave> _routes = new();

    public static IReadOnlyList<SilkRouteSave> Routes => _routes;

    public static void ResetWorldState() => _routes = new List<SilkRouteSave>();

    public static void Load(List<SilkRouteSave> saved) =>
        _routes = saved?.Where(route => route?.cities != null && route.cities.Count >= 2).ToList() ?? new List<SilkRouteSave>();

    public static List<SilkRouteSave> Save() => _routes.Count == 0 ? null : _routes;

    // 剧本开局：修陆路、各站对上城
    public static void Build(WorldMapEra era, WorldMapFrame frame, Vector2Int size)
    {
        _routes = new List<SilkRouteSave>();
        if (era?.silk == null || frame == null || World.world == null) return;
        int laid = 0;
        foreach (WorldMapSilkRoute route in era.silk)
        {
            if (route?.pts == null) continue;
            var save = new SilkRouteSave { name = route.name ?? "", sea = route.sea };
            Vector2Int? last = null;
            foreach (float[] point in route.pts)
            {
                if (point == null || point.Length < 2) continue;
                Vector2Int tile = EarthTerrain.TileOf(frame, size, point[0], point[1]);
                if (!Inside(tile))
                {
                    last = null;
                    continue;
                }
                City city = NearestCity(tile);
                if (city?.data != null && (save.cities.Count == 0 || save.cities[save.cities.Count - 1] != city.data.id))
                    save.cities.Add(city.data.id);
                if (!route.sea && last is Vector2Int from) laid += Lay(Route(from, tile, false) is { Count: > 0 } path ? path : Pass(from, tile));
                last = tile;
            }
            if (save.cities.Count >= 2) _routes.Add(save);
        }
        if (laid > 0) RoadPaintSystem.RequestRedraw();
        LogService.LogInfo($"[EmpireCraft][丝绸之路] {era.silk.Count} 条，修了 {laid} 格路，{_routes.Count} 条连上了沿途的城");
    }

    // 市场互市：各条丝路相邻两站的城所属的国家(不同国、不交战)
    public static IEnumerable<(Kingdom a, Kingdom b)> Links()
    {
        if (World.world?.cities == null) yield break;
        foreach (SilkRouteSave route in _routes)
        {
            Kingdom previous = null;
            foreach (long id in route.cities)
            {
                City city = World.world.cities.get(id);
                Kingdom kingdom = city?.data != null && city.isAlive() ? city.kingdom : null;
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) continue;
                if (previous != null && previous != kingdom && !previous.isRekt() && !previous.isEnemy(kingdom)) yield return (previous, kingdom);
                previous = kingdom;
            }
        }
    }

    private static bool Inside(Vector2Int tile) => tile.x >= 1 && tile.y >= 1 && tile.x < MapBox.width - 1 && tile.y < MapBox.height - 1;

    private static City NearestCity(Vector2Int tile)
    {
        City best = null;
        long bestDistance = (long)StopTiles * StopTiles;
        foreach (City city in World.world.cities.list)
        {
            WorldTile seat = city?.data == null || city.isRekt() ? null : city.getTile();
            if (seat == null) continue;
            long dx = seat.x - tile.x, dy = seat.y - tile.y, distance = dx * dx + dy * dy;
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = city;
        }
        return best;
    }

    // 一段路铺上：等级照所在国家的科技(无主之地土路)
    private static int Lay(List<WorldTile> path)
    {
        int laid = 0;
        foreach (WorldTile tile in path)
        {
            Kingdom owner = tile?.zone_city?.kingdom;
            int grade = owner?.data != null && !owner.isRekt() && !owner.wild ? Mathf.Max(1, RoadBuildingSystem.GradeFor(owner)) : 1;
            if (RoadPaintSystem.Lay(tile, grade)) laid++;
        }
        return laid;
    }

    private static bool Passable(WorldTile tile, bool climb = false) =>
        tile?.Type != null && !tile.Type.ocean && !tile.Type.lava && (climb || !tile.Type.mountains && !tile.Type.summit) &&
        !(tile.Type.block && !tile.Type.wall && !tile.Type.mountains && !tile.Type.summit) && !MapWallSystem.Blocks(tile);

    // 开山口：准翻山再找一回，翻过的山格平成丘陵(雪山平成雪丘)，路铺得上、人马走得过
    private static List<WorldTile> Pass(Vector2Int a, Vector2Int b)
    {
        List<WorldTile> path = Route(a, b, true);
        int opened = 0;
        foreach (WorldTile tile in path)
        {
            TileTypeBase type = tile?.Type;
            if (type == null || !type.mountains && !type.summit) continue;
            TopTileType snow = type.summit ? AssetManager.top_tiles.get("snow_hills") : null;
            if (snow != null) tile.setTileTypes("hills", snow);
            else tile.setTileType("hills");
            tile.Height = tile.Type.height_min;
            opened++;
        }
        if (opened > 0) LogService.LogInfo($"[EmpireCraft][丝绸之路] 开山口 {opened} 格({a} → {b})");
        return path;
    }

    // 走一格的代价：平地 1，沙地、丘陵、林沼贵一些，过河(浅水)要架桥更贵；城心少穿；开山口时山、雪山很贵
    private static float Cost(WorldTile tile)
    {
        TileTypeBase type = tile.Type;
        float cost = type.summit ? SummitCost
            : type.mountains ? MountainCost
            : type.liquid ? WaterCost
            : type.id == "hills" || type.id == "snow_hills" || tile.main_type?.id == "hills" || type.rocks ? HillCost
            : type.id.Contains("swamp") || type.id.Contains("jungle") ? WetCost
            : type.id.Contains("sand") || type.id.Contains("desert") ? SandCost
            : 1f;
        return tile.zone?.city is City city && city.getTile()?.zone == tile.zone ? cost + CityCost : cost;
    }

    // 两站之间的走廊里找路(四邻，路连得上)：山、城墙(关门除外)、海不走；走不通的这一段不修
    // climb：准翻山(开山口用，见 Pass)
    private static List<WorldTile> Route(Vector2Int a, Vector2Int b, bool climb)
    {
        var path = new List<WorldTile>();
        int margin = Mathf.Max(CorridorMin, Mathf.CeilToInt(Vector2Int.Distance(a, b) * CorridorShare));
        int x0 = Mathf.Max(0, Mathf.Min(a.x, b.x) - margin), x1 = Mathf.Min(MapBox.width - 1, Mathf.Max(a.x, b.x) + margin);
        int y0 = Mathf.Max(0, Mathf.Min(a.y, b.y) - margin), y1 = Mathf.Min(MapBox.height - 1, Mathf.Max(a.y, b.y) + margin);
        int width = x1 - x0 + 1, count = width * (y1 - y0 + 1);
        int Index(int x, int y) => (y - y0) * width + (x - x0);
        WorldTile start = Snap(a), goal = Snap(b);
        if (start == null || goal == null) return path;
        var best = new float[count];
        var from = new int[count];
        for (int i = 0; i < count; i++)
        {
            best[i] = float.MaxValue;
            from[i] = -1;
        }
        int first = Index(start.x, start.y), last = Index(goal.x, goal.y);
        best[first] = 0f;
        var heap = new WorldMapZones.MinHeap();
        heap.Push(0f, first);
        while (heap.Count > 0)
        {
            heap.Pop(out float key, out int index);
            if (key > best[index]) continue;
            if (index == last) break;
            int x = x0 + index % width, y = y0 + index / width;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                WorldTile tile = World.world.GetTileSimple(nx, ny);
                if (!Passable(tile, climb)) continue;
                float next = key + Cost(tile);
                int neighbour = Index(nx, ny);
                if (next >= best[neighbour]) continue;
                best[neighbour] = next;
                from[neighbour] = index;
                heap.Push(next, neighbour);
            }
        }
        if (from[last] < 0 && last != first) return path;
        for (int index = last; index >= 0; index = from[index])
        {
            path.Add(World.world.GetTileSimple(x0 + index % width, y0 + index / width));
            if (index == first) break;
        }
        path.Reverse();
        return path;
    }

    // 站落在山上、水里时就近挪到走得了的格(至多 8 格)
    private static WorldTile Snap(Vector2Int at)
    {
        for (int radius = 0; radius <= 8; radius++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                WorldTile tile = World.world.GetTile(at.x + dx, at.y + dy);
                if (Passable(tile) && !tile.Type.liquid) return tile;
            }
        return null;
    }
}
