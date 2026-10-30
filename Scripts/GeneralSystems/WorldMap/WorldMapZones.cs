using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 世界地图开局用的区块地形代价(一区块 8×8 格)，口径同 TerrainResistance：平原 1、林地 1.3、丘陵 1.6、沙漠冻土 2(整块荒漠 3)、
// 丛林沼泽 2.5、山地 6；海上只在分城时算(跨海 6)。先按各城的预算铺(山、海、整块荒漠都铺不过去，国界顺着山脊、大漠、海峡走)，
// 剩下离谁都远的空地补立随机小国(Gaps)，最后把山地、荒漠、沿海零碎地块也分给走得到的最近的城(FillAll)，开局整片陆地铺满。
// 国界要顺着地形走(用户 2026-10-29："你这个国土怎么完全没按照地形来")：
//   · 区块在"弹出"时才定归属(真正的多源最短路；以前一推进队列就归先到的那家，同代价的大漠、高原分成横平竖直、45 度的直线)；
//   · 走一步的代价按要进的那块算，越往山里越贵(山脉腹地 ×2)，过河(河湖占一成多的内陆区块)另加；
//   · 代价上叠一层平滑起伏(几块区块一个起伏，±25%)，均匀的大漠、高原上国界也弯弯曲曲；
//   · 铺满分两轮：头一轮山脉只领不越，两边都有城时山脊就是国界，第二轮再把剩下的分完
// 铺满也守地形(用户 2026-10-29："预设地图的地形完全没有对国土造成限制"，开局按地形铺了两千块，铺满又不论地形分了一万多块，
// 青藏、大漠都归了最近的明城)：整块荒漠、冻土哪座城都不进也不穿过去，留空，进世界后连同山腹划成无人区(见 WildernessSystem；
// 以前归收这种地的游牧、阿拉伯、斯拉夫城，用户 2026-10-29 "之前的沙漠和冻土什么的地块扩展就算了"撤掉)；
// 山里至多往里领两块(头一轮一块)，大山腹地留作无主之地。
// 长城、边墙作农牧分界(用户 10-30 "秦和晋的版图都延伸到哪儿去了""历史上的地块可不是这样子的"：中原的城顺着漠南的平地一路领到
// 大漠边上，帝国法理秦、晋整片伸进草原)：剧本这一时代的边墙(见 SetWalls)两侧的区块之间不通——铺辖地、铺满、补空都不跨过去，
// 墙外由草原那边的城与补空小国分；补空小国的文化、帝国法理也按不过墙走得到的最近的城定(见 NearestOf)
internal static class WorldMapZones
{
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    private static TileZone[] _zones = new TileZone[0];
    private static float[] _travel = new float[0];
    private static bool[] _claimable = new bool[0];
    private static int[] _land = new int[0];
    private static float[] _noise = new float[0];
    private static bool[] _river = new bool[0];
    // 整块荒漠(BarrenLandSystem.Desert)、冻土(Frozen)，别的为 0
    private static int[] _barren = new int[0];

    // 边墙线段(格坐标)与被它隔开的相邻区块对(见 SetWalls、BuildBlocked)
    private static readonly List<(Vector2 a, Vector2 b)> _walls = new();
    private static readonly HashSet<long> _blocked = new();
    private static readonly (int dx, int dy)[] HalfNeighbours = { (1, 0), (0, 1), (1, 1), (1, -1) };

    public const float SeaCost = 6f;
    private const float Mountain = 6f, RiverCost = 1.5f, NoiseCell = 4f, NoiseAmplitude = 0.25f;

    public static void Build()
    {
        // 路口按这张图重判(上一张图、立城平谷以前算的不作数，见 Open)
        TerrainSystem.Invalidate(null);
        Width = MapBox.width / 8;
        Height = MapBox.height / 8;
        BuildBlocked();
        int count = Width * Height;
        _zones = new TileZone[count];
        _travel = new float[count];
        _claimable = new bool[count];
        _land = new int[count];
        _river = new bool[count];
        _barren = new int[count];
        var water = new int[count];
        for (int zy = 0; zy < Height; zy++)
        for (int zx = 0; zx < Width; zx++)
        {
            int index = zy * Width + zx;
            TileZone zone = World.world.GetTileSimple(zx * 8 + 4, zy * 8 + 4)?.zone;
            _zones[index] = zone;
            Classify(zone, out _travel[index], out _claimable[index], out _land[index], out water[index], out _barren[index]);
        }
        // 河湖：内陆(四周都是陆地区块)里水面占一成多到四成多的区块，过去要多费点事，河常常成了国界
        for (int i = 0; i < count; i++)
        {
            int total = _land[i] + water[i];
            if (_land[i] < 32 || total == 0) continue;
            float share = water[i] / (float)total;
            if (share < 0.12f || share > 0.45f) continue;
            bool inland = true;
            foreach (int next in Neighbours(i))
                if (_land[next] < 32)
                {
                    inland = false;
                    break;
                }
            _river[i] = inland;
        }
        BuildNoise(count);
    }

    // 平滑起伏(值噪声：每 NoiseCell 块一个格点，格点间双线性插值)
    private static void BuildNoise(int count)
    {
        _noise = new float[count];
        var random = new global::System.Random(Environment.TickCount);
        int gw = Mathf.CeilToInt(Width / NoiseCell) + 2, gh = Mathf.CeilToInt(Height / NoiseCell) + 2;
        var grid = new float[gw * gh];
        for (int i = 0; i < grid.Length; i++) grid[i] = (float)random.NextDouble() * 2f - 1f;
        for (int zy = 0; zy < Height; zy++)
        for (int zx = 0; zx < Width; zx++)
        {
            float fx = zx / NoiseCell, fy = zy / NoiseCell;
            int x0 = (int)fx, y0 = (int)fy;
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Mathf.Lerp(grid[y0 * gw + x0], grid[y0 * gw + x0 + 1], tx);
            float b = Mathf.Lerp(grid[(y0 + 1) * gw + x0], grid[(y0 + 1) * gw + x0 + 1], tx);
            _noise[zy * Width + zx] = 1f + NoiseAmplitude * Mathf.Lerp(a, b, ty);
        }
    }

    // 剧本开局前设这一时代的边墙(格坐标的线段；null 清掉)，之后每次 Build 按它重算哪些相邻区块被隔开
    public static void SetWalls(IEnumerable<(Vector2 a, Vector2 b)> segments)
    {
        _walls.Clear();
        if (segments != null) _walls.AddRange(segments);
        _blocked.Clear();
    }

    // 区块中心连线与边墙线段相交的相邻区块对(八方向，记一半的邻居、两向通用)
    private static void BuildBlocked()
    {
        _blocked.Clear();
        foreach ((Vector2 p, Vector2 q) in _walls)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p.x, q.x) / 8f) - 1), x1 = Mathf.Min(Width - 1, Mathf.FloorToInt(Mathf.Max(p.x, q.x) / 8f) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p.y, q.y) / 8f) - 1), y1 = Mathf.Min(Height - 1, Mathf.FloorToInt(Mathf.Max(p.y, q.y) / 8f) + 1);
            for (int zy = y0; zy <= y1; zy++)
            for (int zx = x0; zx <= x1; zx++)
            {
                var center = new Vector2(zx * 8 + 4, zy * 8 + 4);
                foreach ((int dx, int dy) in HalfNeighbours)
                {
                    int nx = zx + dx, ny = zy + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    if (Crosses(center, new Vector2(nx * 8 + 4, ny * 8 + 4), p, q)) _blocked.Add(PairKey(zy * Width + zx, ny * Width + nx));
                }
            }
        }
    }

    private static long PairKey(int a, int b) => a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;

    private static bool Blocked(int a, int b) => _blocked.Count > 0 && _blocked.Contains(PairKey(a, b));

    // 两条线段相交
    private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
        Side(c, d, a) > 0f != Side(c, d, b) > 0f && Side(a, b, c) > 0f != Side(a, b, d) > 0f;

    private static float Side(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);

    // 按起点分地(州府治所锚点与各城，见 WorldMapScenario.AnchorTerritory)：各起点同时按地形代价往外走——只走能领的陆地、
    // 交界要有路口、不过边墙(同 Fill)，每块地归先到的起点，各起点至多走 budget。返回各区块的标签，没走到为 -1
    public static int[] Partition(List<(int index, int label, float budget)> seeds)
    {
        var label = new int[_travel.Length];
        var cost = new float[_travel.Length];
        var limit = new float[_travel.Length];
        for (int i = 0; i < cost.Length; i++)
        {
            cost[i] = float.PositiveInfinity;
            label[i] = -1;
        }
        var heap = new MinHeap();
        foreach ((int index, int seedLabel, float budget) in seeds)
        {
            if (index < 0 || index >= cost.Length || cost[index] == 0f) continue;
            cost[index] = 0f;
            label[index] = seedLabel;
            limit[index] = budget;
            heap.Push(0f, index);
        }
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int index);
            if (at > cost[index]) continue;
            foreach (int next in Neighbours(index))
            {
                if (!_claimable[next] || _land[next] < MinLand || _barren[next] != BarrenLandSystem.None || !Open(index, next)) continue;
                float total = at + Step(index, next);
                if (total > limit[index] || total >= cost[next]) continue;
                cost[next] = total;
                label[next] = label[index];
                limit[next] = limit[index];
                heap.Push(total, next);
            }
        }
        return label;
    }

    // 各区块走得到的最近的起点(按地形代价，含跨海，不过边墙)：返回起点在 sources 里的序号，走不到为 -1
    public static int[] NearestOf(IList<int> sources)
    {
        var cost = new float[_travel.Length];
        var label = new int[_travel.Length];
        for (int i = 0; i < cost.Length; i++)
        {
            cost[i] = float.PositiveInfinity;
            label[i] = -1;
        }
        var heap = new MinHeap();
        for (int s = 0; s < sources.Count; s++)
        {
            int index = sources[s];
            if (index < 0 || index >= cost.Length || cost[index] == 0f) continue;
            cost[index] = 0f;
            label[index] = s;
            heap.Push(0f, index);
        }
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int index);
            if (at > cost[index]) continue;
            foreach (int next in Neighbours(index))
            {
                if (Blocked(index, next)) continue;
                float total = at + Step(index, next);
                if (total >= cost[next]) continue;
                cost[next] = total;
                label[next] = label[index];
                heap.Push(total, next);
            }
        }
        return label;
    }

    // 从 a 走到相邻的 b：按要进的那块的地形，山脉腹地(从山里再往山里走)加倍，过河另加，再乘平滑起伏
    private static float Step(int a, int b)
    {
        float travel = _travel[b];
        if (travel >= Mountain && _travel[a] >= Mountain) travel *= 2f;
        else if (!_claimable[b] && !_claimable[a]) travel *= 1.4f;
        float step = (travel * 0.7f + _travel[a] * 0.3f) * (Diagonal(a, b) ? 1.414f : 1f);
        if (_river[b] && !_river[a]) step += RiverCost;
        return step * (b < _noise.Length ? _noise[b] : 1f);
    }

    private static void Classify(TileZone zone, out float travel, out bool claimable, out int land, out int water, out int barrenKind)
    {
        travel = SeaCost;
        claimable = false;
        land = 0;
        water = 0;
        barrenKind = BarrenLandSystem.None;
        if (zone?.tiles == null) return;
        int mountain = 0, hills = 0, desert = 0, frozen = 0, wet = 0, forest = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            TileTypeBase type = tile?.Type;
            TileType main = tile?.main_type;
            if (type == null) continue;
            if (type.liquid || main != null && main.liquid)
            {
                water++;
                continue;
            }
            land++;
            // 雪盖的山(山顶雪)、雪丘：主地块仍是山、丘陵；雪丘算冻土(青藏高原面)。冻土只记一次(雪丘的生物群系也是永冻土，以前记了两次)
            if (type.mountains || type.summit || main != null && (main.mountains || main.summit)) mountain++;
            else if (type.rocks || type.id == "hills" || type.id == "snow_hills" || main?.id == "hills") hills++;
            if (type.id == "snow_hills" || type.id == "snow_block" || type.biome_id == "biome_permafrost") frozen++;
            switch (type.biome_id)
            {
                case "biome_desert":
                case "biome_sand":
                    desert++;
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
        // 整块荒漠冻土：同无人区的判定(WildernessSystem.ClassifyFresh)。以前这里另有一套口径(分母扣掉山地)，
        // 约三百多块开局留空、进世界后又不够无人区的门槛，成了既不归城、也不是无人区的空地
        WildKind wild = WildernessSystem.ClassifyFresh(zone);
        bool barren = wild is WildKind.Desert or WildKind.Tundra;
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
        if (barren && !mountainous) barrenKind = wild == WildKind.Desert ? BarrenLandSystem.Desert : BarrenLandSystem.Frozen;
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
                float total = at + Step(index, next);
                if (total >= cost[next]) continue;
                cost[next] = total;
                heap.Push(total, next);
            }
        }
        return cost;
    }

    // 铺辖地：各城同时从自己的城区往外扩，按地形代价谁近归谁(弹出时才定)；budget 为每城能走的地形代价，cap 为每城最多几块
    public static void Fill(List<(City city, float budget, int cap)> seeds, Action<City, TileZone> claim)
    {
        var owner = new int[_travel.Length];
        var tentative = new int[_travel.Length];
        var cost = new float[_travel.Length];
        for (int i = 0; i < owner.Length; i++)
        {
            owner[i] = -1;
            tentative[i] = -1;
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
            if (s < 0)
            {
                s = tentative[index];
                TileZone zone = _zones[index];
                if (s < 0 || zone == null || zone.city != null || counts[s] >= seeds[s].cap) continue;
                owner[index] = s;
                counts[s]++;
                claim(seeds[s].city, zone);
            }
            foreach (int next in Neighbours(index))
            {
                if (!_claimable[next] || owner[next] >= 0) continue;
                TileZone zone = _zones[next];
                if (zone == null || zone.city != null || !Open(index, next)) continue;
                float total = at + Step(index, next);
                if (total > seeds[s].budget || total >= cost[next]) continue;
                cost[next] = total;
                tentative[next] = s;
                heap.Push(total, next);
            }
        }
    }

    // 补空：离已有辖地(按地形代价，含跨海)最远的无主宜居区块，一块块挑出来立新城，直到处处离辖地都不到 minCost；
    // 挑一块就从它往外重算一遍远近。usable 判断这块能不能立城(开头判一次)
    // seeds：还没建成城、但已占定的区块(原始部落圈立的部落)，补空时离它们也要够远
    public static List<int> Gaps(float minCost, int max, Func<int, bool> usable, IEnumerable<int> seeds = null)
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
        foreach (int seed in seeds ?? Array.Empty<int>())
        {
            if (seed < 0 || seed >= cost.Length || cost[seed] == 0f) continue;
            cost[seed] = 0f;
            heap.Push(0f, seed);
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
                // 墙外按"离辖地远"算，补空小国补在墙外
                if (Blocked(index, next)) continue;
                float total = at + Step(index, next);
                if (total >= cost[next]) continue;
                cost[next] = total;
                heap.Push(total, next);
            }
        }
    }

    // 铺满：剩下有陆地的区块(山地、整块荒漠、沿海零碎地块都算)归走得到的最近的城，只走陆路(隔海的岛靠 Gaps 补的城)，
    // 每一步都要交界有路口(见 Open)，走不进去的山腹不分；
    // allow 判断这座城还能不能再领这块(地块上限)。头一轮山脉只领不越(山脊作国界)，之后几轮把剩下的分完；
    // 哪一轮都不许从山里往下进平地、高原(用户 2026-10-29 "依旧还有跨地形国土")：以前山这边的城领满了上限，
    // 留下的平地第二轮被山那边的城翻过山脊领走(吐蕃翻过喜马拉雅占了恒河边)。最后一轮放宽上限(relaxed)，让同侧的城把剩下的领完
    public static int FillAll(Func<City, TileZone, bool> allow, Action<City, TileZone> claim, Func<City, TileZone, bool> relaxed = null)
    {
        int claimed = 0;
        for (int round = 0; round < 4; round++)
        {
            int got = FillRound(round == 3 && relaxed != null ? relaxed : allow, claim, round == 0 ? 1 : MountainDepth);
            claimed += got;
            if (got == 0 && round > 0 && round < 2) round = 2;
        }
        return claimed;
    }

    // 山里至多往里领几块(从平地算起)
    private const int MountainDepth = 2;

    private static int FillRound(Func<City, TileZone, bool> allow, Action<City, TileZone> claim, int mountainDepth)
    {
        int count = _travel.Length;
        var owner = new City[count];
        var tentative = new City[count];
        var cost = new float[count];
        // 进山第几块(城自己的地块算 0)
        var depth = new int[count];
        var heap = new MinHeap();
        for (int i = 0; i < count; i++)
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
            if (at > cost[index]) continue;
            City from = owner[index];
            if (from == null)
            {
                City candidate = tentative[index];
                TileZone zone = _zones[index];
                // 这家领满了：这一块留给下一轮别家来分
                if (candidate == null || zone == null || zone.city != null || !allow(candidate, zone)) continue;
                owner[index] = candidate;
                claim(candidate, zone);
                claimed++;
                from = candidate;
            }
            foreach (int next in Neighbours(index))
            {
                if (owner[next] != null || _land[next] < MinLand) continue;
                // 从山里只往山里走，不翻过山脊下到另一边的平地、高原；山里至多往里领 mountainDepth 块
                if (depth[index] > 0 && _travel[next] < Mountain) continue;
                int inward = _travel[next] >= Mountain ? depth[index] + 1 : 0;
                if (inward > mountainDepth) continue;
                // 整块荒漠、冻土哪座城都不进也不穿过去(进世界后划成无人区)
                if (_barren[next] != BarrenLandSystem.None) continue;
                TileZone zone = _zones[next];
                if (zone == null || zone.city != null) continue;
                // 进哪块地都要交界有路口(用户 10-29 "为什么你这个城市边界总是会跨山""印度都管到西藏去了，这么大个喜马拉雅")：
                // 以前进山不看路口，山两边的城各往山里领两块，在山脊上碰头，喜马拉雅整片分给了南北两边的城；
                // 现在走不进去的山腹留空，成了天然的界
                if (!Open(index, next)) continue;
                float total = at + Step(index, next);
                if (total >= cost[next]) continue;
                cost[next] = total;
                tentative[next] = from;
                depth[next] = inward;
                heap.Push(total, next);
            }
        }
        return claimed;
    }

    // 交界有没有路口(用户 10-29 "军队不应该跨越"山、"不仅国土跨越了军队也跨越了"，口径同军团寻路、战线、城市扩张，
    // 见 TerrainSystem.Crossable)：铺辖地、铺满都只从有路口的交界扩过去，翻过山脊才到的地不领(留给山那边的城或空着)；
    // 城心那块也照交界算(seatOpen:false)，紧挨城心的一圈同样不隔脊领地
    // 边墙两侧也不通(见 SetWalls)
    private static bool Open(int a, int b) => !Blocked(a, b) && TerrainSystem.Crossable(_zones[a], _zones[b], seatOpen: false);

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
    internal sealed class MinHeap
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
