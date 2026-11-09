using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace EmpireCraft.Scripts.GeneralSystems;

// 通达层的计算(见 ConnectivitySystem)：只读主线程取好的快照(地块数组、城、政权与文化的出行本事)，不碰游戏对象，
// 所以既能放在后台线程一口气算完，也能在主线程按毫秒切片续算(后台线程开不起来时)。分几段，每段按一个单位推进：
//   ① 陆地连片(电报要同一块陆地)；② 城际陆路：每对城在地块上寻一次路(A*，消息口径：无人区的山脉丛林沼泽熔岩过不去、
//      沙漠冻土 ×2)，记下地形剖面(没变的沿用上一轮的)；③ 海路：每座临海城连同一水域最近的几座、别的陆地上最近的几座，
//      各块陆地之间再连最近的一对，量海岸最近处；④ 拼成图；⑤ 每个文化(消息)、每个政权(消息、人员)从自己所有的城出发
//      做有界最短路，记下到别的文化、政权的成本、强度与沿途障碍；⑥ 通信捷径(电报、广播、互联网)与航空。
//   二档另算两样：货物路线(每个政权按人员的路走到 goods_max_cost，绕开与它交战的政权，记下沿途第三国，贸易据此算运费、过境税、封路)；
//   每座城最近的 near_count 座城(人员口径，near_max_cost 以内，记成本与是否走海)，迁徙、国内市场、经济圈、难民、空城安置直接查表。
public sealed class ConnectivityZoneGrid
{
    public int Count;
    // 地形类(ConnectivityRules.Plains……Ridge；Blocked 为海面、地图边)、路等级、城墙(1 墙、2 关门)、没修路的无人区类型(WildKind 的值)
    public byte[] Class = Array.Empty<byte>(), Road = Array.Empty<byte>(), Wall = Array.Empty<byte>(), Wild = Array.Empty<byte>();
    public int[] WildRegion = Array.Empty<int>();
    public short[] X = Array.Empty<short>(), Y = Array.Empty<short>();
    // 四方向邻接(CSR)
    public int[] NbrStart = new int[1], Nbr = Array.Empty<int>();
    // 内容的版本(主线程每次真变了给一个新号，城际剖面据此沿用或重算)
    public int Revision;

    public bool Resize(int count)
    {
        if (Count == count && Class.Length == count) return false;
        Count = count;
        Class = new byte[count];
        Road = new byte[count];
        Wall = new byte[count];
        Wild = new byte[count];
        WildRegion = new int[count];
        for (int i = 0; i < count; i++) WildRegion[i] = -1;
        X = new short[count];
        Y = new short[count];
        NbrStart = new int[count + 1];
        Nbr = Array.Empty<int>();
        return true;
    }
}

// 图上的一个点：一座城
public sealed class ConnectivityNode
{
    public long City;
    public int Zone, Realm, Culture;
    public float X, Y;
    public int PortLevel = -1;
    public float[] ShoreX = Array.Empty<float>(), ShoreY = Array.Empty<float>();
    public int[] ShoreBasin = Array.Empty<int>();
    public bool Coastal => ShoreX.Length > 0;
}

// 要寻路的一对城(Reuse 不为空就沿用上一轮的剖面)
public sealed class ConnectivityLandLink
{
    public int A, B;
    public (long, long) Key;
    public ConnectivityEdge Reuse;
}

public struct ConnectivityPair
{
    public const byte BySea = 1, Ocean = 2, ViaThird = 4, ViaTelecom = 8, ArchipelagoOnly = 16, ViaAir = 32;
    // 只走路(不算通信捷径、航空)的成本与强度；算上之后的成本与强度
    public float PathCost, PathStrength, Cost, Strength;
    public ConnectivityBarrier Main, Second;
    public int WildRegion;
    public byte Flags;
    // 货物路线沿途的第三国(政权下标；没有为 null)
    public int[] Third;

    public bool Has(byte flag) => (Flags & flag) != 0;
}

// 一轮的结果：算完交给主线程后只读(主线程另有成本表缓存)
public sealed class ConnectivityResult
{
    public ConnectivityConfig Config;
    public ConnectivityNode[] Nodes = Array.Empty<ConnectivityNode>();
    public readonly Dictionary<long, int> NodeOfCity = new();
    public int[] NodeLand = Array.Empty<int>();
    public int[] EdgeA = Array.Empty<int>(), EdgeB = Array.Empty<int>();
    public ConnectivityEdge[] Edges = Array.Empty<ConnectivityEdge>();
    public int[] AdjStart = new int[1], Adj = Array.Empty<int>();
    public string[] Cultures = Array.Empty<string>();
    public readonly Dictionary<string, int> CultureIndex = new(StringComparer.Ordinal);
    public long[] Realms = Array.Empty<long>();
    public readonly Dictionary<long, int> RealmIndex = new();
    public ConnectivityTravel[] CultureIdeas = Array.Empty<ConnectivityTravel>();
    public ConnectivityTravel[] RealmIdeas = Array.Empty<ConnectivityTravel>(), RealmPeople = Array.Empty<ConnectivityTravel>();
    public Dictionary<int, ConnectivityPair>[] CultureOut = Array.Empty<Dictionary<int, ConnectivityPair>>();
    public Dictionary<int, ConnectivityPair>[] RealmIdeasOut = Array.Empty<Dictionary<int, ConnectivityPair>>();
    public Dictionary<int, ConnectivityPair>[] RealmPeopleOut = Array.Empty<Dictionary<int, ConnectivityPair>>();
    // 二档：货物路线(政权 → 政权)；每座城最近的几座城(点下标、成本、1 = 走了海路)，按成本由近到远
    public Dictionary<int, ConnectivityPair>[] RealmGoodsOut = Array.Empty<Dictionary<int, ConnectivityPair>>();
    public int[][] NearNodes = Array.Empty<int[]>();
    public float[][] NearCosts = Array.Empty<float[]>();
    public byte[][] NearFlags = Array.Empty<byte[]>();
    public int LandEdges, SeaEdges, AStarRuns, AStarFailed, Reused, Pairs, Units, GoodsPairs;
    public double ComputeMs;
    // 日志用：政权之间(消息口径)各往来等级、各主障碍的对数；城最多的政权
    public readonly int[] LevelCounts = new int[4];
    public readonly int[] BarrierCounts = new int[ConnectivityRules.BarrierCount];
    public int LargestRealm = -1;
    private readonly Dictionary<int, (float[] cost, bool[] arch)> _costs = new();

    public int EdgeCount => Edges.Length;

    // 出行一方走各条边的成本(按 CostKey 缓存；一次只在一个线程里用：算的时候归后台，交出后归主线程)
    public (float[] cost, bool[] arch) Costs(in ConnectivityTravel travel)
    {
        int key = travel.CostKey;
        if (_costs.TryGetValue(key, out var cached)) return cached;
        var cost = new float[Edges.Length];
        var arch = new bool[Edges.Length];
        for (int e = 0; e < Edges.Length; e++)
        {
            cost[e] = ConnectivityRules.EdgeCost(Edges[e], travel, Config, out bool only);
            arch[e] = only;
        }
        var result = (cost, arch);
        _costs[key] = result;
        return result;
    }

    // 两点之间直接相连的边(没有为 -1)
    public int EdgeBetween(int a, int b)
    {
        if (a < 0 || b < 0 || a >= Nodes.Length || b >= Nodes.Length) return -1;
        for (int i = AdjStart[a]; i < AdjStart[a + 1]; i++)
        {
            int e = Adj[i];
            if (EdgeA[e] == b || EdgeB[e] == b) return e;
        }
        return -1;
    }

    // 从一点出发的有界最短路(主线程按需算，调用方缓存)：点 → 成本
    public Dictionary<int, float> From(int source, in ConnectivityTravel travel, float cutoff)
    {
        var dist = new Dictionary<int, float>();
        if (source < 0 || source >= Nodes.Length) return dist;
        (float[] cost, _) = Costs(travel);
        var heap = new ConnectivityHeap();
        heap.Push(0f, source);
        while (heap.Count > 0)
        {
            heap.Pop(out float d, out int u);
            if (dist.ContainsKey(u)) continue;
            dist[u] = d;
            for (int i = AdjStart[u]; i < AdjStart[u + 1]; i++)
            {
                int e = Adj[i];
                int v = EdgeA[e] == u ? EdgeB[e] : EdgeA[e];
                if (dist.ContainsKey(v) || float.IsPositiveInfinity(cost[e])) continue;
                float next = d + cost[e] + (Nodes[u].Realm != Nodes[v].Realm ? Config.border_cost : 0f);
                if (next <= cutoff) heap.Push(next, v);
            }
        }
        return dist;
    }
}

// 小顶堆(浮点键、整数项)，不分配对象
public sealed class ConnectivityHeap
{
    private float[] _keys = new float[64];
    private int[] _items = new int[64];
    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public void Push(float key, int item)
    {
        if (Count == _keys.Length)
        {
            Array.Resize(ref _keys, Count * 2);
            Array.Resize(ref _items, Count * 2);
        }
        int i = Count++;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_keys[parent] <= key) break;
            _keys[i] = _keys[parent];
            _items[i] = _items[parent];
            i = parent;
        }
        _keys[i] = key;
        _items[i] = item;
    }

    public void Pop(out float key, out int item)
    {
        key = _keys[0];
        item = _items[0];
        int last = --Count;
        if (last == 0) return;
        float lastKey = _keys[last];
        int lastItem = _items[last];
        int i = 0;
        while (true)
        {
            int left = i * 2 + 1;
            if (left >= last) break;
            int right = left + 1;
            int child = right < last && _keys[right] < _keys[left] ? right : left;
            if (_keys[child] >= lastKey) break;
            _keys[i] = _keys[child];
            _items[i] = _items[child];
            i = child;
        }
        _keys[i] = lastKey;
        _items[i] = lastItem;
    }
}

public sealed class ConnectivityJob
{
    private enum Stage { Land, Edges, Sea, SeaLands, Graph, Paths, Near, Telecom, Done }
    private const int NearChunk = 32;

    public readonly int Generation;
    public readonly ConnectivityResult Result = new();
    // 本轮新寻路得出的剖面(主线程据此更新沿用表)
    public readonly List<((long, long) key, ConnectivityEdge edge)> NewLand = new();
    public Exception Error { get; private set; }
    public bool Finished => _finished;
    public bool Cancelled => _cancelled;
    public int Progress => Volatile.Read(ref _progress);
    // 现在算到哪一段(日志、测量用)
    public string StageName => _stage.ToString();

    private volatile bool _finished, _cancelled;
    private int _progress;
    private Stage _stage = Stage.Land;
    private int _cursor;
    private readonly ConnectivityConfig _config;
    private readonly ConnectivityZoneGrid _grid;
    private readonly List<ConnectivityLandLink> _links;
    private readonly List<int> _edgeA = new(), _edgeB = new();
    private readonly List<ConnectivityEdge> _edges = new();
    private readonly HashSet<long> _seaPairs = new();
    private readonly List<int> _coastal = new();
    private readonly List<(bool culture, int index, ConnectivityMode mode)> _tasks = new();
    private readonly int[][] _realmEnemies;
    private int[] _blockedStamp = Array.Empty<int>();
    private bool[] _seaPath = Array.Empty<bool>();
    private List<int>[] _cultureNodes, _realmNodes;
    private HashSet<int>[] _cultureLands, _realmLands;
    private bool[] _cultureCoast, _realmCoast;

    // 寻路、最短路的工作数组(按需分配，这一轮内反复用)
    private float[] _g;
    private int[] _prev, _open, _closed;
    private int _stamp;
    private readonly ConnectivityHeap _heap = new();
    private float[] _dist;
    private int[] _prevEdge, _seen, _done, _targetStamp, _targetNode;
    private bool[] _hop;
    private float[] _targetDist;
    private readonly float[] _sums = new float[ConnectivityRules.BarrierCount];
    private readonly Dictionary<int, float> _regionWeight = new();

    public ConnectivityJob(int generation, ConnectivityConfig config, ConnectivityZoneGrid grid, ConnectivityNode[] nodes,
        List<ConnectivityLandLink> links, string[] cultures, ConnectivityTravel[] cultureIdeas, long[] realms,
        ConnectivityTravel[] realmIdeas, ConnectivityTravel[] realmPeople, int[][] realmEnemies = null)
    {
        Generation = generation;
        _realmEnemies = realmEnemies;
        _config = config;
        _grid = grid;
        _links = links;
        Result.Config = config;
        Result.Nodes = nodes;
        for (int i = 0; i < nodes.Length; i++) Result.NodeOfCity[nodes[i].City] = i;
        Result.Cultures = cultures;
        for (int i = 0; i < cultures.Length; i++) Result.CultureIndex[cultures[i]] = i;
        Result.CultureIdeas = cultureIdeas;
        Result.Realms = realms;
        for (int i = 0; i < realms.Length; i++) Result.RealmIndex[realms[i]] = i;
        Result.RealmIdeas = realmIdeas;
        Result.RealmPeople = realmPeople;
    }

    public void Cancel() => _cancelled = true;

    // 推进到做完或用完 budgetMs 毫秒(≤0 不限)；做完返回真
    public bool Advance(double budgetMs)
    {
        if (_finished) return true;
        var clock = Stopwatch.StartNew();
        try
        {
            do
            {
                if (_cancelled)
                {
                    _finished = true;
                    return true;
                }
                Step();
                Interlocked.Increment(ref _progress);
                Result.Units++;
            } while (_stage != Stage.Done && (budgetMs <= 0d || clock.Elapsed.TotalMilliseconds < budgetMs));
        }
        catch (Exception exception)
        {
            Error = exception;
            _stage = Stage.Done;
        }
        Result.ComputeMs += clock.Elapsed.TotalMilliseconds;
        if (_stage == Stage.Done) _finished = true;
        return _finished;
    }

    private void Step()
    {
        switch (_stage)
        {
            case Stage.Land:
                FloodLand();
                _stage = Stage.Edges;
                _cursor = 0;
                return;
            case Stage.Edges:
                if (_cursor < _links.Count)
                {
                    LandLink(_links[_cursor++]);
                    return;
                }
                for (int i = 0; i < Result.Nodes.Length; i++)
                    if (Result.Nodes[i].Coastal) _coastal.Add(i);
                _stage = Stage.Sea;
                _cursor = 0;
                return;
            case Stage.Sea:
                if (_cursor < _coastal.Count)
                {
                    SeaNeighbours(_coastal[_cursor++]);
                    return;
                }
                _stage = Stage.SeaLands;
                return;
            case Stage.SeaLands:
                SeaBetweenLands();
                _stage = Stage.Graph;
                return;
            case Stage.Graph:
                BuildGraph();
                _stage = Stage.Paths;
                _cursor = 0;
                return;
            case Stage.Paths:
                if (_cursor < _tasks.Count)
                {
                    var task = _tasks[_cursor++];
                    if (task.mode == ConnectivityMode.Goods) Goods(task.index);
                    else Paths(task.culture, task.index, task.mode);
                    return;
                }
                _stage = Stage.Near;
                _cursor = 0;
                return;
            case Stage.Near:
                if (_cursor < Result.Nodes.Length)
                {
                    int end = Math.Min(Result.Nodes.Length, _cursor + NearChunk);
                    for (; _cursor < end; _cursor++) Near(_cursor);
                    return;
                }
                _stage = Stage.Telecom;
                return;
            case Stage.Telecom:
                Telecom();
                _stage = Stage.Done;
                return;
        }
    }

    #region 陆地连片

    private void FloodLand()
    {
        int count = _grid.Count;
        var land = new int[count];
        for (int i = 0; i < count; i++) land[i] = -1;
        var queue = new Queue<int>();
        int next = 0;
        for (int start = 0; start < count; start++)
        {
            if (land[start] >= 0 || _grid.Class[start] == ConnectivityRules.Blocked) continue;
            land[start] = next;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int z = queue.Dequeue();
                for (int i = _grid.NbrStart[z]; i < _grid.NbrStart[z + 1]; i++)
                {
                    int n = _grid.Nbr[i];
                    if (land[n] >= 0 || _grid.Class[n] == ConnectivityRules.Blocked) continue;
                    land[n] = next;
                    queue.Enqueue(n);
                }
            }
            next++;
        }
        var nodeLand = new int[Result.Nodes.Length];
        for (int i = 0; i < nodeLand.Length; i++)
        {
            int zone = Result.Nodes[i].Zone;
            nodeLand[i] = zone >= 0 && zone < count ? land[zone] : -1;
        }
        Result.NodeLand = nodeLand;
    }

    #endregion

    #region 城际陆路

    private void LandLink(ConnectivityLandLink link)
    {
        ConnectivityEdge edge = link.Reuse;
        if (edge != null) Result.Reused++;
        else
        {
            Result.AStarRuns++;
            edge = AStar(Result.Nodes[link.A].Zone, Result.Nodes[link.B].Zone);
            if (edge == null)
            {
                Result.AStarFailed++;
                return;
            }
            NewLand.Add((link.Key, edge));
        }
        _edgeA.Add(link.A);
        _edgeB.Add(link.B);
        _edges.Add(edge);
        Result.LandEdges++;
    }

    // 寻路时走进一块地的成本(消息口径、不看科技的参考值)；走不了返回 -1
    private float ZoneStep(int z)
    {
        byte cls = _grid.Class[z];
        if (cls == ConnectivityRules.Blocked) return -1f;
        byte wild = _grid.Wild[z];
        if (wild == (byte)WildKindCode.Mountain || wild == (byte)WildKindCode.Jungle || wild == (byte)WildKindCode.Swamp ||
            wild == (byte)WildKindCode.Lava) return -1f;
        float baseCost = _config.terrain_base[cls];
        int road = _grid.Road[z];
        float cost = baseCost * (road > 0 ? _config.road_grade_factors[Math.Min(road, ConnectivityRules.RoadGrades - 1)] : 1f);
        if (cls == ConnectivityRules.River && road == 0) cost += _config.river_crossing;
        if (_grid.Wall[z] == 1) cost += _config.wall_crossing;
        else if (_grid.Wall[z] == 2) cost += _config.gate_crossing;
        if (wild == (byte)WildKindCode.Desert || wild == (byte)WildKindCode.Tundra)
            cost += baseCost * Math.Max(0f, _config.ideas_wild_factor - 1f);
        return cost;
    }

    private ConnectivityEdge AStar(int from, int to)
    {
        int count = _grid.Count;
        if (from < 0 || to < 0 || from >= count || to >= count) return null;
        if (_g == null || _g.Length != count)
        {
            _g = new float[count];
            _prev = new int[count];
            _open = new int[count];
            _closed = new int[count];
            _stamp = 0;
        }
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_open, 0, count);
            Array.Clear(_closed, 0, count);
            _stamp = 1;
        }
        float h = 1f;
        for (int g = 1; g < ConnectivityRules.RoadGrades; g++) h = Math.Min(h, _config.road_grade_factors[g]);
        h = Math.Max(0.05f, h * Math.Min(1f, _config.terrain_base[ConnectivityRules.Plains]));
        int tx = _grid.X[to], ty = _grid.Y[to];
        _heap.Clear();
        _g[from] = 0f;
        _prev[from] = -1;
        _open[from] = _stamp;
        _heap.Push((Math.Abs(_grid.X[from] - tx) + Math.Abs(_grid.Y[from] - ty)) * h, from);
        int expanded = 0;
        bool found = false;
        while (_heap.Count > 0 && expanded < _config.astar_max_expand)
        {
            _heap.Pop(out _, out int z);
            if (_closed[z] == _stamp) continue;
            _closed[z] = _stamp;
            expanded++;
            if (z == to)
            {
                found = true;
                break;
            }
            float gz = _g[z];
            for (int i = _grid.NbrStart[z]; i < _grid.NbrStart[z + 1]; i++)
            {
                int n = _grid.Nbr[i];
                if (_closed[n] == _stamp) continue;
                float step = n == to ? Math.Max(0f, ZoneStepTarget(n)) : ZoneStep(n);
                if (step < 0f) continue;
                float total = gz + step;
                if (_open[n] == _stamp && _g[n] <= total) continue;
                _open[n] = _stamp;
                _g[n] = total;
                _prev[n] = z;
                _heap.Push(total + (Math.Abs(_grid.X[n] - tx) + Math.Abs(_grid.Y[n] - ty)) * h, n);
            }
        }
        if (!found) return null;
        var edge = new ConnectivityEdge();
        _regionWeight.Clear();
        for (int z = to; z != from && z >= 0; z = _prev[z]) Accumulate(edge, z);
        foreach (KeyValuePair<int, float> pair in _regionWeight)
            if (pair.Value > edge.WildRegionWeight)
            {
                edge.WildRegion = pair.Key;
                edge.WildRegionWeight = pair.Value;
            }
        return edge;
    }

    // 目的地(城心那块)总进得去：按地形算，不看无人区
    private float ZoneStepTarget(int z)
    {
        byte cls = _grid.Class[z];
        if (cls == ConnectivityRules.Blocked) cls = ConnectivityRules.Plains;
        return _config.terrain_base[cls];
    }

    private void Accumulate(ConnectivityEdge edge, int z)
    {
        byte cls = _grid.Class[z];
        if (cls == ConnectivityRules.Blocked) cls = ConnectivityRules.Plains;
        float baseCost = _config.terrain_base[cls];
        int road = Math.Min((int)_grid.Road[z], ConnectivityRules.RoadGrades - 1);
        if (road > 0) edge.Road[road] += baseCost;
        else edge.Off[cls] += baseCost;
        if (cls == ConnectivityRules.River && road == 0) edge.Rivers++;
        if (_grid.Wall[z] == 1) edge.Walls++;
        else if (_grid.Wall[z] == 2) edge.Gates++;
        byte wild = _grid.Wild[z];
        if (wild == (byte)WildKindCode.Desert || wild == (byte)WildKindCode.Tundra)
        {
            if (wild == (byte)WildKindCode.Desert) edge.WildDesert += baseCost;
            else edge.WildTundra += baseCost;
            int region = _grid.WildRegion[z];
            if (region >= 0) _regionWeight[region] = (_regionWeight.TryGetValue(region, out float w) ? w : 0f) + baseCost;
        }
        edge.Zones++;
    }

    #endregion

    #region 海路

    private void SeaNeighbours(int node)
    {
        ConnectivityNode own = Result.Nodes[node];
        var near = new List<(float d, int other)>();
        foreach (int other in _coastal)
        {
            if (other == node) continue;
            ConnectivityNode o = Result.Nodes[other];
            float dx = o.X - own.X, dy = o.Y - own.Y;
            near.Add((dx * dx + dy * dy, other));
        }
        near.Sort((a, b) => a.d.CompareTo(b.d));
        int sameLand = 0, otherLand = 0;
        int sameCap = Math.Max(0, _config.sea_neighbours), otherCap = Math.Max(0, _config.sea_other_land);
        // 按城心远近挑候选，再量海岸：同一块陆地上的取前 3 倍、别的陆地上的取前 3 倍
        var picked = new List<(float tiles, int other, bool same)>();
        foreach ((_, int other) in near)
        {
            bool same = Result.NodeLand[other] >= 0 && Result.NodeLand[other] == Result.NodeLand[node];
            if (same ? sameLand >= sameCap * 3 : otherLand >= otherCap * 3) continue;
            if (same) sameLand++;
            else otherLand++;
            float tiles = ShoreDistance(own, Result.Nodes[other]);
            if (!float.IsPositiveInfinity(tiles)) picked.Add((tiles, other, same));
            if (sameLand >= sameCap * 3 && otherLand >= otherCap * 3) break;
        }
        picked.Sort((a, b) => a.tiles.CompareTo(b.tiles));
        int takenAll = 0, takenOther = 0;
        foreach ((float tiles, int other, bool same) in picked)
        {
            if (takenAll < sameCap)
            {
                takenAll++;
                if (!same) takenOther++;
                AddSea(node, other, tiles);
                continue;
            }
            if (!same && takenOther < otherCap)
            {
                takenOther++;
                AddSea(node, other, tiles);
            }
        }
    }

    // 每两块有临海城的陆地之间，连城心最近的一对(跨洋的远航也有一条可走的边)
    private void SeaBetweenLands()
    {
        var best = new Dictionary<long, (float d, int a, int b)>();
        for (int i = 0; i < _coastal.Count; i++)
        {
            int a = _coastal[i];
            int la = Result.NodeLand[a];
            for (int j = i + 1; j < _coastal.Count; j++)
            {
                int b = _coastal[j];
                int lb = Result.NodeLand[b];
                if (la == lb || la < 0 || lb < 0) continue;
                long key = la < lb ? (long)la << 32 | (uint)lb : (long)lb << 32 | (uint)la;
                float dx = Result.Nodes[a].X - Result.Nodes[b].X, dy = Result.Nodes[a].Y - Result.Nodes[b].Y;
                float d = dx * dx + dy * dy;
                if (!best.TryGetValue(key, out var old) || d < old.d) best[key] = (d, a, b);
            }
        }
        foreach ((float _, int a, int b) in best.Values)
        {
            float tiles = ShoreDistance(Result.Nodes[a], Result.Nodes[b]);
            if (!float.IsPositiveInfinity(tiles)) AddSea(a, b, tiles);
        }
    }

    // 两城海岸最近处相距几格(要同一片水域；没有返回 +∞)
    private static float ShoreDistance(ConnectivityNode a, ConnectivityNode b)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < a.ShoreX.Length; i++)
        for (int j = 0; j < b.ShoreX.Length; j++)
        {
            if (a.ShoreBasin[i] != b.ShoreBasin[j]) continue;
            float dx = a.ShoreX[i] - b.ShoreX[j], dy = a.ShoreY[i] - b.ShoreY[j];
            float d = dx * dx + dy * dy;
            if (d < best) best = d;
        }
        return float.IsPositiveInfinity(best) ? best : (float)Math.Sqrt(best);
    }

    private void AddSea(int a, int b, float tiles)
    {
        long key = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;
        if (!_seaPairs.Add(key)) return;
        var edge = new ConnectivityEdge
        {
            Sea = true,
            SeaTiles = tiles,
            PortLevel = Math.Max(Result.Nodes[a].PortLevel, Result.Nodes[b].PortLevel)
        };
        _edgeA.Add(a);
        _edgeB.Add(b);
        _edges.Add(edge);
        Result.SeaEdges++;
    }

    #endregion

    #region 图与最短路

    private void BuildGraph()
    {
        int n = Result.Nodes.Length, m = _edges.Count;
        Result.EdgeA = _edgeA.ToArray();
        Result.EdgeB = _edgeB.ToArray();
        Result.Edges = _edges.ToArray();
        var degree = new int[n + 1];
        for (int e = 0; e < m; e++)
        {
            degree[Result.EdgeA[e] + 1]++;
            degree[Result.EdgeB[e] + 1]++;
        }
        for (int i = 0; i < n; i++) degree[i + 1] += degree[i];
        var adj = new int[degree[n]];
        var fill = (int[])degree.Clone();
        for (int e = 0; e < m; e++)
        {
            adj[fill[Result.EdgeA[e]]++] = e;
            adj[fill[Result.EdgeB[e]]++] = e;
        }
        Result.AdjStart = degree;
        Result.Adj = adj;

        int cultures = Result.Cultures.Length, realms = Result.Realms.Length;
        _cultureNodes = Lists(cultures);
        _realmNodes = Lists(realms);
        _cultureLands = Sets(cultures);
        _realmLands = Sets(realms);
        _cultureCoast = new bool[cultures];
        _realmCoast = new bool[realms];
        for (int i = 0; i < n; i++)
        {
            ConnectivityNode node = Result.Nodes[i];
            if (node.Culture >= 0 && node.Culture < cultures)
            {
                _cultureNodes[node.Culture].Add(i);
                if (Result.NodeLand[i] >= 0) _cultureLands[node.Culture].Add(Result.NodeLand[i]);
                if (node.Coastal) _cultureCoast[node.Culture] = true;
            }
            if (node.Realm >= 0 && node.Realm < realms)
            {
                _realmNodes[node.Realm].Add(i);
                if (Result.NodeLand[i] >= 0) _realmLands[node.Realm].Add(Result.NodeLand[i]);
                if (node.Coastal) _realmCoast[node.Realm] = true;
            }
        }
        Result.CultureOut = Maps(cultures);
        Result.RealmIdeasOut = Maps(realms);
        Result.RealmPeopleOut = Maps(realms);
        Result.RealmGoodsOut = Maps(realms);
        Result.NearNodes = new int[n][];
        Result.NearCosts = new float[n][];
        Result.NearFlags = new byte[n][];
        for (int c = 0; c < cultures; c++)
            if (_cultureNodes[c].Count > 0) _tasks.Add((true, c, ConnectivityMode.Ideas));
        for (int r = 0; r < realms; r++)
        {
            if (_realmNodes[r].Count == 0) continue;
            _tasks.Add((false, r, ConnectivityMode.Ideas));
            _tasks.Add((false, r, ConnectivityMode.People));
            _tasks.Add((false, r, ConnectivityMode.Goods));
        }
        _dist = new float[n];
        _prevEdge = new int[n];
        _seen = new int[n];
        _done = new int[n];
        _hop = new bool[n];
        _seaPath = new bool[n];
        _blockedStamp = new int[realms];
        int targets = Math.Max(cultures, realms);
        _targetStamp = new int[targets];
        _targetNode = new int[targets];
        _targetDist = new float[targets];
        _stamp = 0;
    }

    private static List<int>[] Lists(int count)
    {
        var lists = new List<int>[count];
        for (int i = 0; i < count; i++) lists[i] = new List<int>();
        return lists;
    }

    private static HashSet<int>[] Sets(int count)
    {
        var sets = new HashSet<int>[count];
        for (int i = 0; i < count; i++) sets[i] = new HashSet<int>();
        return sets;
    }

    private static Dictionary<int, ConnectivityPair>[] Maps(int count)
    {
        var maps = new Dictionary<int, ConnectivityPair>[count];
        for (int i = 0; i < count; i++) maps[i] = new Dictionary<int, ConnectivityPair>();
        return maps;
    }

    // 从一个文化(或政权)所有的城出发的有界最短路，记下到别的文化(政权)的结果
    private void Paths(bool culture, int index, ConnectivityMode mode)
    {
        ConnectivityTravel travel = culture ? Result.CultureIdeas[index]
            : mode == ConnectivityMode.People ? Result.RealmPeople[index] : Result.RealmIdeas[index];
        (float[] cost, bool[] arch) = Result.Costs(travel);
        float cutoff = ConnectivityRules.CostFor(_config.min_strength, travel.Length, _config);
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen, 0, _seen.Length);
            Array.Clear(_done, 0, _done.Length);
            Array.Clear(_targetStamp, 0, _targetStamp.Length);
            _stamp = 1;
        }
        _heap.Clear();
        List<int> sources = culture ? _cultureNodes[index] : _realmNodes[index];
        foreach (int s in sources)
        {
            _seen[s] = _stamp;
            _dist[s] = 0f;
            _prevEdge[s] = -1;
            _hop[s] = false;
            _heap.Push(0f, s);
        }
        ConnectivityNode[] nodes = Result.Nodes;
        while (_heap.Count > 0)
        {
            _heap.Pop(out float d, out int u);
            if (_done[u] == _stamp || d > _dist[u]) continue;
            _done[u] = _stamp;
            int target = culture ? nodes[u].Culture : nodes[u].Realm;
            if (target >= 0 && target < _targetStamp.Length && (_targetStamp[target] != _stamp || d < _targetDist[target]))
            {
                _targetStamp[target] = _stamp;
                _targetDist[target] = d;
                _targetNode[target] = u;
            }
            for (int i = Result.AdjStart[u]; i < Result.AdjStart[u + 1]; i++)
            {
                int e = Result.Adj[i];
                float c = cost[e];
                if (float.IsPositiveInfinity(c)) continue;
                int v = Result.EdgeA[e] == u ? Result.EdgeB[e] : Result.EdgeA[e];
                if (_done[v] == _stamp) continue;
                float next = d + c + (nodes[u].Realm != nodes[v].Realm ? _config.border_cost : 0f);
                if (next > cutoff) continue;
                if (_seen[v] == _stamp && _dist[v] <= next) continue;
                _seen[v] = _stamp;
                _dist[v] = next;
                _prevEdge[v] = e;
                _hop[v] = _hop[u] || arch[e];
                _heap.Push(next, v);
            }
        }
        Dictionary<int, ConnectivityPair> output = culture ? Result.CultureOut[index]
            : mode == ConnectivityMode.People ? Result.RealmPeopleOut[index] : Result.RealmIdeasOut[index];
        int count = culture ? Result.Cultures.Length : Result.Realms.Length;
        for (int t = 0; t < count; t++)
        {
            if (t == index || _targetStamp[t] != _stamp) continue;
            float dist = _targetDist[t];
            float strength = ConnectivityRules.Strength(dist, travel.Length, _config);
            if (strength < _config.min_strength) continue;
            output[t] = Describe(_targetNode[t], dist, strength, culture, index, t, travel);
            Result.Pairs++;
        }
    }

    // 沿最优路径倒着走一遍：分类障碍、海路、隔着第三方、列岛视距
    private ConnectivityPair Describe(int node, float dist, float strength, bool culture, int source, int target, in ConnectivityTravel travel)
    {
        Array.Clear(_sums, 0, _sums.Length);
        var pair = new ConnectivityPair
        {
            PathCost = dist, Cost = dist, PathStrength = strength, Strength = strength, WildRegion = -1
        };
        if (_hop[node]) pair.Flags |= ConnectivityPair.ArchipelagoOnly;
        float wildWeight = 0f;
        int guard = Result.Nodes.Length;
        for (int u = node; _prevEdge[u] >= 0 && guard-- > 0;)
        {
            int e = _prevEdge[u];
            int v = Result.EdgeA[e] == u ? Result.EdgeB[e] : Result.EdgeA[e];
            ConnectivityEdge edge = Result.Edges[e];
            ConnectivityRules.AddBarriers(edge, travel, _config, _sums);
            if (edge.Sea)
            {
                pair.Flags |= ConnectivityPair.BySea;
                if (edge.SeaTiles > _config.coastal_tiles) pair.Flags |= ConnectivityPair.Ocean;
            }
            else if (edge.WildRegion >= 0 && edge.WildRegionWeight > wildWeight)
            {
                wildWeight = edge.WildRegionWeight;
                pair.WildRegion = edge.WildRegion;
            }
            if (Result.Nodes[u].Realm != Result.Nodes[v].Realm)
            {
                int key = culture ? Result.Nodes[u].Culture : Result.Nodes[u].Realm;
                bool third = key != source && key != target;
                if (third) pair.Flags |= ConnectivityPair.ViaThird;
                _sums[(int)(third ? ConnectivityBarrier.Border : ConnectivityBarrier.Distance)] += _config.border_cost;
            }
            u = v;
        }
        ConnectivityRules.PickBarriers(_sums, out pair.Main, out pair.Second);
        // 只是"道远"、又明显绕了路的陆路：多半是直线上横着过不去的无人区(山脉、丛林、沼泽)，取直线上最多的那一片
        if (pair.Main == ConnectivityBarrier.Distance && !pair.Has(ConnectivityPair.BySea))
        {
            int start = Origin(node);
            if (start >= 0 && Detoured(Result.Nodes[start].Zone, Result.Nodes[node].Zone, dist) &&
                LineWild(Result.Nodes[start].Zone, Result.Nodes[node].Zone, out int region, out byte kind))
            {
                pair.Second = ConnectivityBarrier.Distance;
                pair.Main = kind == (byte)WildKindCode.Jungle ? ConnectivityBarrier.Jungle
                    : kind == (byte)WildKindCode.Swamp ? ConnectivityBarrier.Swamp
                    : ConnectivityBarrier.Mountain;
                pair.WildRegion = region;
            }
        }
        return pair;
    }

    private int Origin(int node)
    {
        int guard = Result.Nodes.Length;
        int u = node;
        while (_prevEdge[u] >= 0 && guard-- > 0)
        {
            int e = _prevEdge[u];
            u = Result.EdgeA[e] == u ? Result.EdgeB[e] : Result.EdgeA[e];
        }
        return u;
    }

    // 走的路比直线(按平原算)长出一半以上、至少多 6：算绕了路
    private bool Detoured(int from, int to, float cost)
    {
        if (from < 0 || to < 0 || from >= _grid.Count || to >= _grid.Count) return false;
        float dx = _grid.X[to] - _grid.X[from], dy = _grid.Y[to] - _grid.Y[from];
        float straight = (float)Math.Sqrt(dx * dx + dy * dy) * _config.terrain_base[ConnectivityRules.Plains];
        return cost >= Math.Max(straight * 1.5f, straight + 6f);
    }

    private readonly Dictionary<int, (int count, byte kind)> _lineCounts = new();

    // 两块地连线上横着的、过不去的无人区：取块数最多的一片
    private bool LineWild(int from, int to, out int region, out byte kind)
    {
        region = -1;
        kind = 0;
        if (from < 0 || to < 0 || from >= _grid.Count || to >= _grid.Count) return false;
        float x0 = _grid.X[from], y0 = _grid.Y[from], dx = _grid.X[to] - x0, dy = _grid.Y[to] - y0;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)));
        if (steps > 512) return false;
        Dictionary<int, (int count, byte kind)> counts = _lineCounts;
        counts.Clear();
        int lastZone = -1;
        for (int i = 1; i < steps; i++)
        {
            float t = (float)i / steps;
            int z = ZoneAt((int)Math.Round(x0 + dx * t), (int)Math.Round(y0 + dy * t));
            if (z < 0 || z == lastZone) continue;
            lastZone = z;
            byte wild = _grid.Wild[z];
            if (wild != (byte)WildKindCode.Mountain && wild != (byte)WildKindCode.Jungle && wild != (byte)WildKindCode.Swamp &&
                wild != (byte)WildKindCode.Lava) continue;
            int id = _grid.WildRegion[z];
            counts[id] = (counts.TryGetValue(id, out var old) ? old.count + 1 : 1, wild);
        }
        int best = 0;
        foreach (KeyValuePair<int, (int count, byte kind)> pair in counts)
            if (pair.Value.count > best)
            {
                best = pair.Value.count;
                region = pair.Key;
                kind = pair.Value.kind;
            }
        return kind != 0;
    }

    // 地块坐标 → 编号(按坐标外框铺成数组，查一次 O(1))
    private int[] _zoneAt;
    private int _zoneMinX, _zoneMinY, _zoneWidth, _zoneHeight;

    private int ZoneAt(int x, int y)
    {
        if (_zoneAt == null)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int z = 0; z < _grid.Count; z++)
            {
                minX = Math.Min(minX, _grid.X[z]);
                minY = Math.Min(minY, _grid.Y[z]);
                maxX = Math.Max(maxX, _grid.X[z]);
                maxY = Math.Max(maxY, _grid.Y[z]);
            }
            if (_grid.Count == 0) minX = minY = maxX = maxY = 0;
            _zoneMinX = minX;
            _zoneMinY = minY;
            _zoneWidth = maxX - minX + 1;
            _zoneHeight = maxY - minY + 1;
            _zoneAt = new int[Math.Max(1, _zoneWidth * _zoneHeight)];
            for (int i = 0; i < _zoneAt.Length; i++) _zoneAt[i] = -1;
            for (int z = 0; z < _grid.Count; z++) _zoneAt[(_grid.Y[z] - minY) * _zoneWidth + _grid.X[z] - minX] = z;
        }
        int cx = x - _zoneMinX, cy = y - _zoneMinY;
        return cx < 0 || cy < 0 || cx >= _zoneWidth || cy >= _zoneHeight ? -1 : _zoneAt[cy * _zoneWidth + cx];
    }

    #endregion

    #region 二档：货物路线与最近的城

    // 一个政权的货物路线：按它人员的本事走，绕开与它交战的政权(战争封路)，走到 goods_max_cost；记沿途第三国
    private void Goods(int realm)
    {
        ConnectivityTravel travel = Result.RealmPeople[realm];
        (float[] cost, _) = Result.Costs(travel);
        float cutoff = Math.Max(1f, _config.goods_max_cost);
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen, 0, _seen.Length);
            Array.Clear(_done, 0, _done.Length);
            Array.Clear(_targetStamp, 0, _targetStamp.Length);
            Array.Clear(_blockedStamp, 0, _blockedStamp.Length);
            _stamp = 1;
        }
        if (_realmEnemies != null && realm < _realmEnemies.Length && _realmEnemies[realm] != null)
            foreach (int enemy in _realmEnemies[realm])
                if (enemy >= 0 && enemy < _blockedStamp.Length && enemy != realm) _blockedStamp[enemy] = _stamp;
        _heap.Clear();
        foreach (int s in _realmNodes[realm])
        {
            _seen[s] = _stamp;
            _dist[s] = 0f;
            _prevEdge[s] = -1;
            _seaPath[s] = false;
            _heap.Push(0f, s);
        }
        ConnectivityNode[] nodes = Result.Nodes;
        while (_heap.Count > 0)
        {
            _heap.Pop(out float d, out int u);
            if (_done[u] == _stamp || d > _dist[u]) continue;
            _done[u] = _stamp;
            int target = nodes[u].Realm;
            if (target >= 0 && target < _targetStamp.Length && (_targetStamp[target] != _stamp || d < _targetDist[target]))
            {
                _targetStamp[target] = _stamp;
                _targetDist[target] = d;
                _targetNode[target] = u;
            }
            for (int i = Result.AdjStart[u]; i < Result.AdjStart[u + 1]; i++)
            {
                int e = Result.Adj[i];
                float c = cost[e];
                if (float.IsPositiveInfinity(c)) continue;
                int v = Result.EdgeA[e] == u ? Result.EdgeB[e] : Result.EdgeA[e];
                if (_done[v] == _stamp) continue;
                int vr = nodes[v].Realm;
                if (vr >= 0 && vr < _blockedStamp.Length && _blockedStamp[vr] == _stamp) continue;
                float next = d + c + (nodes[u].Realm != vr ? _config.border_cost : 0f);
                if (next > cutoff) continue;
                if (_seen[v] == _stamp && _dist[v] <= next) continue;
                _seen[v] = _stamp;
                _dist[v] = next;
                _prevEdge[v] = e;
                _seaPath[v] = _seaPath[u] || Result.Edges[e].Sea;
                _heap.Push(next, v);
            }
        }
        Dictionary<int, ConnectivityPair> output = Result.RealmGoodsOut[realm];
        var third = new List<int>();
        for (int t = 0; t < Result.Realms.Length; t++)
        {
            if (t == realm || _targetStamp[t] != _stamp) continue;
            int node = _targetNode[t];
            float dist = _targetDist[t];
            third.Clear();
            int guard = nodes.Length;
            for (int u = node; _prevEdge[u] >= 0 && guard-- > 0;)
            {
                int e = _prevEdge[u];
                u = Result.EdgeA[e] == u ? Result.EdgeB[e] : Result.EdgeA[e];
                int r = nodes[u].Realm;
                if (r != realm && r != t && r >= 0 && !third.Contains(r)) third.Add(r);
            }
            float strength = ConnectivityRules.Strength(dist, travel.Length, _config);
            Result.GoodsPairs++;
            output[t] = new ConnectivityPair
            {
                PathCost = dist, Cost = dist, PathStrength = strength, Strength = strength, WildRegion = -1,
                Main = ConnectivityBarrier.None, Second = ConnectivityBarrier.None,
                Flags = _seaPath[node] ? ConnectivityPair.BySea : (byte)0,
                Third = third.Count > 0 ? third.ToArray() : null
            };
        }
    }

    // 一座城最近的几座城(人员口径，按所在政权的本事)：有界最短路，弹出 near_count 座或超过 near_max_cost 就停
    private void Near(int source)
    {
        ConnectivityNode[] nodes = Result.Nodes;
        int realm = nodes[source].Realm;
        ConnectivityTravel travel = realm >= 0 && realm < Result.RealmPeople.Length ? Result.RealmPeople[realm]
            : new ConnectivityTravel { Mode = ConnectivityMode.People, Length = _config.people_length_base, FreightFactor = 1f };
        (float[] cost, _) = Result.Costs(travel);
        float cutoff = Math.Max(1f, _config.near_max_cost);
        int want = Math.Max(1, _config.near_count);
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen, 0, _seen.Length);
            Array.Clear(_done, 0, _done.Length);
            Array.Clear(_targetStamp, 0, _targetStamp.Length);
            Array.Clear(_blockedStamp, 0, _blockedStamp.Length);
            _stamp = 1;
        }
        _heap.Clear();
        _seen[source] = _stamp;
        _dist[source] = 0f;
        _seaPath[source] = false;
        _heap.Push(0f, source);
        var found = new List<(int node, float cost, bool sea)>(want);
        while (_heap.Count > 0 && found.Count < want)
        {
            _heap.Pop(out float d, out int u);
            if (_done[u] == _stamp || d > _dist[u]) continue;
            _done[u] = _stamp;
            if (u != source) found.Add((u, d, _seaPath[u]));
            for (int i = Result.AdjStart[u]; i < Result.AdjStart[u + 1]; i++)
            {
                int e = Result.Adj[i];
                float c = cost[e];
                if (float.IsPositiveInfinity(c)) continue;
                int v = Result.EdgeA[e] == u ? Result.EdgeB[e] : Result.EdgeA[e];
                if (_done[v] == _stamp) continue;
                float next = d + c + (nodes[u].Realm != nodes[v].Realm ? _config.border_cost : 0f);
                if (next > cutoff) continue;
                if (_seen[v] == _stamp && _dist[v] <= next) continue;
                _seen[v] = _stamp;
                _dist[v] = next;
                _seaPath[v] = _seaPath[u] || Result.Edges[e].Sea;
                _heap.Push(next, v);
            }
        }
        var ids = new int[found.Count];
        var costs = new float[found.Count];
        var flags = new byte[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            ids[i] = found[i].node;
            costs[i] = found[i].cost;
            flags[i] = found[i].sea ? (byte)1 : (byte)0;
        }
        Result.NearNodes[source] = ids;
        Result.NearCosts[source] = costs;
        Result.NearFlags[source] = flags;
    }

    #endregion

    #region 通信捷径与航空

    private void Telecom()
    {
        Shortcut(Result.CultureIdeas, Result.CultureOut, _cultureLands, _cultureCoast, air: false);
        Shortcut(Result.RealmIdeas, Result.RealmIdeasOut, _realmLands, _realmCoast, air: false);
        Shortcut(Result.RealmPeople, Result.RealmPeopleOut, _realmLands, _realmCoast, air: true);
        int largest = 0;
        for (int r = 0; r < Result.RealmIdeasOut.Length; r++)
        {
            if (_realmNodes[r].Count > largest)
            {
                largest = _realmNodes[r].Count;
                Result.LargestRealm = r;
            }
            foreach (ConnectivityPair pair in Result.RealmIdeasOut[r].Values)
            {
                Result.LevelCounts[(int)ConnectivityRules.Level(pair.Strength, _config)]++;
                Result.BarrierCounts[(int)pair.Main]++;
            }
        }
    }

    private void Shortcut(ConnectivityTravel[] travels, Dictionary<int, ConnectivityPair>[] outs, HashSet<int>[] lands, bool[] coast, bool air)
    {
        for (int a = 0; a < travels.Length; a++)
        {
            ConnectivityTravel from = travels[a];
            if (air ? !from.Aviation : from.Comm <= 0) continue;
            if (lands[a].Count == 0) continue;
            for (int b = 0; b < travels.Length; b++)
            {
                if (a == b || lands[b].Count == 0) continue;
                ConnectivityTravel to = travels[b];
                float cap;
                if (air) cap = _config.aviation_people_cap > 0f ? _config.aviation_people_cap : float.PositiveInfinity;
                else
                {
                    if (to.Comm <= 0) continue;
                    bool sameLand = lands[a].Overlaps(lands[b]);
                    bool cable = from.Industrial && to.Industrial && coast[a] && coast[b];
                    cap = ConnectivityRules.TelecomCap(from.Comm, to.Comm, to.Open, sameLand, cable, _config);
                }
                if (float.IsPositiveInfinity(cap)) continue;
                bool known = outs[a].TryGetValue(b, out ConnectivityPair pair);
                if (known && pair.Cost <= cap) continue;
                if (!known)
                    pair = new ConnectivityPair
                    {
                        PathCost = float.PositiveInfinity, PathStrength = 0f, Main = ConnectivityBarrier.None,
                        Second = ConnectivityBarrier.None, WildRegion = -1
                    };
                pair.Cost = cap;
                pair.Strength = ConnectivityRules.Strength(cap, from.Length, _config);
                pair.Flags |= air ? ConnectivityPair.ViaAir : ConnectivityPair.ViaTelecom;
                if (pair.Strength < _config.min_strength) continue;
                if (!known) Result.Pairs++;
                outs[a][b] = pair;
            }
        }
    }

    #endregion
}

// 无人区类型的数值(同 WildKind，计算这边不引用游戏的枚举)
public enum WildKindCode : byte { None = 0, Mountain = 1, Desert = 2, Tundra = 3, Jungle = 4, Swamp = 5, Lava = 6 }
