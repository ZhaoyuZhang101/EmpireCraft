using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图开局用的区块地形代价(一区块 8×8 格)，口径同 TerrainResistance：平原 1、林地 1.3、丘陵 1.6、沙漠冻土 2(整块荒漠 3)、
// 丛林沼泽 2.5、山地 6；海上只在分城时算(跨海 6)。先按各城的预算铺(山、海、整块荒漠都铺不过去，国界顺着山脊、大漠、海峡走)，
// 剩下离谁都远的空地补立随机小国(Gaps)，最后把山地、荒漠、沿海零碎地块也分给走得到的最近的城(FillAll)，开局整片陆地铺满
internal static class WorldMapZones
{
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    private static TileZone[] _zones = new TileZone[0];
    private static float[] _travel = new float[0];
    private static bool[] _claimable = new bool[0];
    private static int[] _land = new int[0];

    public const float SeaCost = 6f;

    public static void Build()
    {
        Width = MapBox.width / 8;
        Height = MapBox.height / 8;
        int count = Width * Height;
        _zones = new TileZone[count];
        _travel = new float[count];
        _claimable = new bool[count];
        _land = new int[count];
        for (int zy = 0; zy < Height; zy++)
        for (int zx = 0; zx < Width; zx++)
        {
            int index = zy * Width + zx;
            TileZone zone = World.world.GetTileSimple(zx * 8 + 4, zy * 8 + 4)?.zone;
            _zones[index] = zone;
            Classify(zone, out _travel[index], out _claimable[index], out _land[index]);
        }
    }

    private static void Classify(TileZone zone, out float travel, out bool claimable, out int land)
    {
        travel = SeaCost;
        claimable = false;
        land = 0;
        if (zone?.tiles == null) return;
        int mountain = 0, hills = 0, desert = 0, frozen = 0, wet = 0, forest = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            TileTypeBase type = tile?.Type;
            TileType main = tile?.main_type;
            if (type == null || type.liquid || main != null && main.liquid) continue;
            land++;
            // 雪盖的山(山顶雪)、雪丘：主地块仍是山、丘陵；雪丘算冻土(青藏高原面)
            if (type.mountains || type.summit || main != null && (main.mountains || main.summit)) mountain++;
            else if (type.rocks || type.id == "hills" || type.id == "snow_hills" || main?.id == "hills") hills++;
            if (type.id == "snow_hills" || type.id == "snow_block") frozen++;
            switch (type.biome_id)
            {
                case "biome_desert":
                case "biome_sand":
                    desert++;
                    break;
                case "biome_permafrost":
                    frozen++;
                    break;
                case "biome_jungle":
                case "biome_swamp":
                    wet++;
                    break;
                case "biome_birch":
                case "biome_maple":
                    forest++;
                    break;
            }
        }
        if (land < 32) return;
        float total = land;
        float share(int n) => n / total;
        bool barren = desert + frozen > 0 && desert + frozen >= 0.85f * (land - mountain);
        bool mountainous = share(mountain) >= 0.2f;
        // 山地两成以上：山脉(走不通、不铺辖地)；整块荒漠冻土：天堑腹地；山丘过四成五：高地(秦岭、太行、大巴山一类)，铺得到但难走
        if (mountainous) travel = 6f;
        else if (barren) travel = 3f;
        else if (share(mountain + hills) >= 0.45f) travel = 3f;
        else if (share(wet) >= 0.5f) travel = 2.5f;
        else if (share(desert) >= 0.5f || share(frozen) >= 0.5f) travel = 2f;
        else if (share(hills) >= 0.25f) travel = 1.6f;
        else if (share(forest) >= 0.5f) travel = 1.3f;
        else travel = 1f;
        claimable = !mountainous && !barren;
    }

    public static int IndexOf(Vector2Int tile)
    {
        int zx = Mathf.Clamp(tile.x / 8, 0, Width - 1), zy = Mathf.Clamp(tile.y / 8, 0, Height - 1);
        return zy * Width + zx;
    }

    public static int IndexOf(TileZone zone)
    {
        if (zone?.centerTile == null) return -1;
        return IndexOf(new Vector2Int(zone.centerTile.x, zone.centerTile.y));
    }

    public static TileZone Zone(int index) => index >= 0 && index < _zones.Length ? _zones[index] : null;

    // 分城用：从一座都城出发走到各区块的地形代价(含跨海)
    public static float[] CostFrom(int source, float limit)
    {
        var cost = new float[_travel.Length];
        for (int i = 0; i < cost.Length; i++) cost[i] = float.PositiveInfinity;
        if (source < 0 || source >= cost.Length) return cost;
        var heap = new MinHeap();
        cost[source] = 0f;
        heap.Push(0f, source);
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int index);
            if (at > cost[index] || at > limit) continue;
            foreach (int next in Neighbours(index))
            {
                float step = (_travel[index] + _travel[next]) * 0.5f * (Diagonal(index, next) ? 1.414f : 1f);
                float total = at + step;
                if (total >= cost[next]) continue;
                cost[next] = total;
                heap.Push(total, next);
            }
        }
        return cost;
    }

    // 铺辖地：各城同时从自己的城区往外扩，先到先得；budget 为每城能走的地形代价，cap 为每城最多几块
    public static void Fill(List<(City city, float budget, int cap)> seeds, Action<City, TileZone> claim)
    {
        var owner = new int[_travel.Length];
        var cost = new float[_travel.Length];
        for (int i = 0; i < owner.Length; i++)
        {
            owner[i] = -1;
            cost[i] = float.PositiveInfinity;
        }
        var counts = new int[seeds.Count];
        var heap = new MinHeap();
        for (int s = 0; s < seeds.Count; s++)
        {
            City city = seeds[s].city;
            if (city?.zones == null) continue;
            foreach (TileZone zone in city.zones)
            {
                int index = IndexOf(zone);
                if (index < 0) continue;
                owner[index] = s;
                cost[index] = 0f;
                counts[s]++;
                heap.Push(0f, index);
            }
        }
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int index);
            if (at > cost[index]) continue;
            int s = owner[index];
            if (s < 0) continue;
            foreach (int next in Neighbours(index))
            {
                if (!_claimable[next] || owner[next] >= 0 && cost[next] <= at) continue;
                TileZone zone = _zones[next];
                if (zone == null || zone.city != null) continue;
                float total = at + (_travel[index] + _travel[next]) * 0.5f * (Diagonal(index, next) ? 1.414f : 1f);
                if (total > seeds[s].budget || counts[s] >= seeds[s].cap || total >= cost[next]) continue;
                if (owner[next] >= 0) continue;
                owner[next] = s;
                cost[next] = total;
                counts[s]++;
                claim(seeds[s].city, zone);
                heap.Push(total, next);
            }
        }
    }

    // 补空：离已有辖地(按地形代价，含跨海)最远的无主宜居区块，一块块挑出来立新城，直到处处离辖地都不到 minCost；
    // 挑一块就从它往外重算一遍远近。usable 判断这块能不能立城(开头判一次)
    public static List<int> Gaps(float minCost, int max, Func<int, bool> usable)
    {
        var cost = new float[_travel.Length];
        var heap = new MinHeap();
        for (int i = 0; i < cost.Length; i++)
        {
            cost[i] = float.PositiveInfinity;
            if (_zones[i]?.city == null) continue;
            cost[i] = 0f;
            heap.Push(0f, i);
        }
        Relax(cost, heap);
        var candidates = new List<int>();
        for (int i = 0; i < cost.Length; i++)
            if (_claimable[i] && _zones[i]?.city == null && usable(i)) candidates.Add(i);
        var picks = new List<int>();
        while (picks.Count < max)
        {
            int best = -1;
            float bestCost = minCost;
            foreach (int i in candidates)
                if (cost[i] >= bestCost && (best < 0 || cost[i] > bestCost))
                {
                    best = i;
                    bestCost = cost[i];
                }
            if (best < 0) break;
            picks.Add(best);
            cost[best] = 0f;
            heap.Push(0f, best);
            Relax(cost, heap);
        }
        return picks;
    }

    private static void Relax(float[] cost, MinHeap heap)
    {
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int index);
            if (at > cost[index]) continue;
            foreach (int next in Neighbours(index))
            {
                float total = at + (_travel[index] + _travel[next]) * 0.5f * (Diagonal(index, next) ? 1.414f : 1f);
                if (total >= cost[next]) continue;
                cost[next] = total;
                heap.Push(total, next);
            }
        }
    }

    // 铺满：剩下有陆地的区块(山地、整块荒漠、沿海零碎地块都算)归走得到的最近的城，只走陆路(隔海的岛靠 Gaps 补的城)；
    // allow 判断这座城还能不能再领这块(地块上限)
    public static int FillAll(Func<City, TileZone, bool> allow, Action<City, TileZone> claim)
    {
        var owner = new City[_travel.Length];
        var cost = new float[_travel.Length];
        var heap = new MinHeap();
        for (int i = 0; i < owner.Length; i++)
        {
            cost[i] = float.PositiveInfinity;
            City city = _zones[i]?.city;
            if (city == null || city.isRekt()) continue;
            owner[i] = city;
            cost[i] = 0f;
            heap.Push(0f, i);
        }
        int claimed = 0;
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int index);
            if (at > cost[index] || owner[index] == null) continue;
            foreach (int next in Neighbours(index))
            {
                if (owner[next] != null || _land[next] < MinLand) continue;
                TileZone zone = _zones[next];
                if (zone == null || zone.city != null || !allow(owner[index], zone)) continue;
                owner[next] = owner[index];
                cost[next] = at + (_travel[index] + _travel[next]) * 0.5f * (Diagonal(index, next) ? 1.414f : 1f);
                claim(owner[index], zone);
                claimed++;
                heap.Push(cost[next], next);
            }
        }
        return claimed;
    }

    // 区块里至少这么多格陆地才算陆地(一格两格的礁石、沙洲不领)
    private const int MinLand = 6;

    private static bool Diagonal(int a, int b) => a % Width != b % Width && a / Width != b / Width;

    private static IEnumerable<int> Neighbours(int index)
    {
        int zx = index % Width, zy = index / Width;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int x = zx + dx, y = zy + dy;
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            yield return y * Width + x;
        }
    }

    // 二叉堆(运行库里没有现成的优先队列)
    private sealed class MinHeap
    {
        private readonly List<(float key, int value)> _items = new();
        public int Count => _items.Count;

        public void Push(float key, int value)
        {
            _items.Add((key, value));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_items[parent].key <= _items[i].key) break;
                (_items[parent], _items[i]) = (_items[i], _items[parent]);
                i = parent;
            }
        }

        public void Pop(out float key, out int value)
        {
            (key, value) = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < _items.Count && _items[left].key < _items[smallest].key) smallest = left;
                if (right < _items.Count && _items[right].key < _items[smallest].key) smallest = right;
                if (smallest == i) break;
                (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                i = smallest;
            }
        }
    }
}
