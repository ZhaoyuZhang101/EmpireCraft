using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本编辑器里预测无人区(用户 2026-10-30 "都做还有新的无人区在设置城市和刷地块的时候也要考虑到")：编辑器全程不生成世界，
// 这里在地形预览(地球网格叠刷的地形，或导入地图的网格)上照进世界后的划区预演一遍，放城、刷地块、看国界时据此避开或利用无人区：
//   · 取样：按剧本的地图范围(不定的按欧亚非，同世界地图窗口的默认)与编辑器里选的大小档(SizeIndex)，一块地 8×8 格，
//     每格取预览上最近的一像素(预览一像素 0.1°，各档一格 0.04°~0.2°，一块地合 3.5~16 像素见方)；地球地图另照
//     EarthTerrain.DrawRivers 画大河水道(留渡口，水面多了的地块不算荒地)；
//   · 归类、连片：门槛常量照抄 WildernessSystem(水面 MaxWater、陆地 MinLand、山脉的核心/两肩/坡丘、沙漠冻原 BarrenShare、
//     大泽 SwampShare、雨林 JungleShare、外沿放宽 EdgeShare 与山脉的 MountainEdgeShare、至少 MinZones 块，Python 版见
//     Tools/WorldMap/preview_wilderness.py)；开局没有上回的类型，不放宽(Hysteresis)。城心那块留给城(同 Regroup 的 anchored)，
//     小图只放一、二档城、离图边四格以内的城不放(同 WorldMapScenario.Plan)；
//   · 时代开发度(见 WorldMapDevelopment)：从预设复制来的剧本按来源时代(WorldMapEra.base)的表，把当年没开出来的草木平地算回
//     丛林、大泽再归类(离城越近开发度越高，开满的不改)；剧本刷的开发度(develop)盖过时代表，刷满 100 的雨林大泽开满、留给城；
//   · 归属(同 WildernessSystem.OwnerOf)：刷了永久中立(paint 的 "n")的那一片不归谁；否则看挂的法理——剧本刷的地块(p: 那国都城的
//     王国法理、r: 那块王国法理)优先，再是时代文件的史实归属(地球地图，见 WorldMapWilderness.HistoricalOwner)，再是按地块走
//     (四邻、不过海、至多 MaxAttachSteps 块)最先走到的城的王国法理；法理归开局持有它的国家(法理首都所在国，见
//     WorldMapScenario.BuildTitles；不称帝的国家都城不是法理首都时另立一块、归它自己，见 OwnTitles)；没挂上法理的，
//     周边(八邻)的城全属同一国的归那国，否则中立。城的辖地按地形代价从城心往外铺(无人区铺不进去)；离剧本的城太远
//     (FillerReach 外)的空地开局会补随机小国(见 WorldMapScenario.PlanFillers)，这里记作"补空小国"("f")；
//   · 只是预演：村镇厂区、立城时平出的河谷绿洲、补空小国的城都不知道，与开局划出来的会差几块。
// 三段缓存：取样(地形、地图范围、大小档变了才重算；最大一档四万多块地、近三百万格，几十毫秒)；归类、连片、辖地与挂法理的走法
// (再加城心、刷的开发度变了才重算，放城挪城只算这一段)；这两段刷地形、刷开发度拖着时不算，停手后至多每 HeavySeconds 秒一次；
// 归属(剧本一改就重算，只看无人区的地块，几毫秒)。在同一张 0.05° 网格上逐格取样时片数、块数与 Python 版一致；
// 编辑器的预览是 0.1°，差百分之一上下的边角块。
public static class ScenarioWilderness
{
    // 预览一像素的地形(见 CellOf)：低两位为起伏(平地、山地山巅、丘陵、水面)，往上三位为地面(别的、冻土雪地、沙地荒漠、丛林、沼泽、
    // 草木平地——草地、稀树草原、桦林、枫林的高低两种，时代开发度能改回丛林沼泽的，同 WorldMapDevelopment.Vegetated)
    private const int ReliefMask = 3, RockRelief = 1, HillRelief = 2, WaterRelief = 3;
    private const int GroundShift = 2, FrozenGround = 1, SandGround = 2, JungleGround = 3, SwampGround = 4, GreenGround = 5;
    public const byte WaterCell = WaterRelief;

    // 归类门槛(照抄 WildernessSystem，改了那边这里也要改)
    private const float MaxWater = 0.25f;
    private const int MinLand = 16;
    private const float MountainCore = 0.5f, MountainFlank = 0.35f, MountainRugged = 0.75f;
    private const float BarrenShare = 0.85f, SwampShare = 0.75f, JungleShare = 0.85f;
    private const float EdgeShare = 0.5f, MountainEdgeShare = 0.6f;
    private const int MaxAttachSteps = 64;
    // 时代开发度：陆地不到几格的不改、这一类格子(连草木)占陆地几成才改、离城多远(度)开发度翻倍的衰减(同 WorldMapDevelopment)
    private const float WetShare = 0.5f;
    // 辖地：走一块的代价(×10，平原 10、丘陵 16、沙漠冻土 20、整块荒漠 30、丛林沼泽 25、山地 60，同 WorldMapZones 的口径)；
    // 从城心铺到多远(平原约六块，WorldMapScenario.GapCost 再宽一点)之外算补空小国；陆地不到几格的当水
    private const int FillerReach = 60, TerritoryLand = 8;
    // 跨这条线算海(SeaRegionSystem.IsSea：陆地不过 16 格又有水)
    private const int SeaGround = 16;
    public const int Filler = -2;
    private const float HeavySeconds = 0.4f, OwnSeconds = 0.25f;

    // 归属从哪来(悬停提示里写)
    public enum Source : byte { None, Paint, History, Near, Surround, Neutral }

    // 一片无人区：类型、地块、中心经纬度、周边的城(辖地的标签：Seats 的下标或 Filler)
    public sealed class Piece
    {
        public WildKind Kind;
        public readonly List<int> Zones = new();
        public float Lon, Lat;
        public readonly HashSet<int> Around = new();
    }

    private sealed class Layout
    {
        public int Key;
        public WorldMapFrame Frame;
        public Vector2Int Size;
        public int SizeIndex;
        public float West, Bottom, ZoneDegrees;
        public int W, H;
        public byte[] Land;
        public byte[] Raw;
        public int[] Region;
        public float[] Dev;
        public int[] Terr, Label;
        public string[] History;
        public List<Piece> Pieces = new();
        // 放得下的城(编号、经纬)与城心所在的地块
        public readonly List<string> Seats = new();
        public readonly List<int> SeatZone = new();
        public readonly Dictionary<string, int> SeatOf = new();
    }

    // 取样的结果：各块的陆地、水面、山地、丘陵、冻土、沙地、丛林、沼泽、草木平地格数(只读；归类时丛林、沼泽、草木另拷一份再改)
    private sealed class Sampled
    {
        public int Key;
        public byte[] Land, Water, Rock, Hills, Frozen, Sand, Jungle, Swamp, Green;
    }

    private static Layout _layout;
    private static Sampled _sampled;
    // 归属(按地块；只有无人区的地块有值)、各片是否永久中立与周边同属的国家
    private static string[] _owner, _title;
    private static byte[] _source;
    private static bool[] _neutral;
    private static string[] _surround;
    // 预览网格(编辑器给的)：标记、宽高、左下角经纬度、每像素几度；导入地图的范围；网格改一次 +1
    private static byte[] _cells;
    private static int _w, _h, _gridRevision;
    private static float _lon0, _lat0, _step;
    private static WorldMapFrame _importFrame;
    private static int _seatKey, _checkedRevision = int.MinValue, _checkedGeometry, _ownedRevision = int.MinValue;
    private static float _heavyAt = -100f, _ownAt = -100f;
    // 大河水道(按范围与大小缓存)
    private static bool[] _river;
    private static string _riverKey;

    // 按哪档大小预测(编辑器里选，不存进剧本；世界地图窗口默认中号)
    public static int SizeIndex { get; set; } = 1;
    // 预测变一次 +1(编辑器据此重画)；地块的范围、大小变了 +1(编辑器据此重算图层粗格落在哪块地上)
    public static int Version { get; private set; }
    public static int GeometryVersion { get; private set; }
    public static bool Ready => _layout != null;
    public static WorldMapFrame Frame => _layout?.Frame;
    public static Vector2Int Size => _layout?.Size ?? Vector2Int.zero;
    public static float ZoneDegrees => _layout?.ZoneDegrees ?? 0f;
    public static IReadOnlyList<Piece> Pieces => _layout?.Pieces ?? new List<Piece>();

    public static void Reset()
    {
        _layout = null;
        _sampled = null;
        _owner = _title = _surround = null;
        _source = null;
        _neutral = null;
        _cells = null;
        _importFrame = null;
        _river = null;
        _riverKey = null;
        _checkedRevision = _ownedRevision = int.MinValue;
        Version++;
        GeometryVersion++;
    }

    #region 地形标记

    // 一种地块写法("主:顶"，如 "soil_low:jungle_low"、"mountains:snow_block")的标记，口径同 WildernessSystem.ComputeMarks：
    // 水面看主、顶两层；山地、丘陵看两层；地面(冻土、沙、丛林、沼泽、草木)看顶层
    public static byte CellOf(string type)
    {
        if (string.IsNullOrEmpty(type) || type.Contains("ocean") || type.Contains("waters") || type.Contains("lava")) return WaterCell;
        int split = type.IndexOf(':');
        string main = split < 0 ? type : type.Substring(0, split), top = split < 0 ? type : type.Substring(split + 1);
        int relief = main == "mountains" || top == "mountains" || top.Contains("summit") ? RockRelief
            : main == "hills" || top == "hills" ? HillRelief : 0;
        int ground = top.StartsWith("snow", StringComparison.Ordinal) || top.StartsWith("frozen", StringComparison.Ordinal) ||
                     top.Contains("permafrost") ? FrozenGround
            : top == "sand" || top.Contains("desert") ? SandGround
            : top.Contains("jungle") ? JungleGround
            : top.Contains("swamp") ? SwampGround
            : (top.EndsWith("_low", StringComparison.Ordinal) || top.EndsWith("_high", StringComparison.Ordinal)) &&
              (top.Contains("grass") || top.Contains("savanna") || top.Contains("birch") || top.Contains("maple")) ? GreenGround
            : 0;
        return (byte)(relief | ground << GroundShift);
    }

    // 地块编号(EarthTerrain.PreviewCodes)换成标记
    public static byte[] CellsOf(byte[] codes)
    {
        if (codes == null) return null;
        string[] types = EarthTerrain.Types;
        var lut = new byte[256];
        for (int i = 0; i < lut.Length; i++) lut[i] = i < types.Length ? CellOf(types[i]) : WaterCell;
        var cells = new byte[codes.Length];
        for (int i = 0; i < codes.Length; i++) cells[i] = lut[codes[i]];
        return cells;
    }

    // 编辑器的预览网格(同 EarthTerrain.Preview 的像素排法)；imported 为导入地图的范围(地球地图为 null)；revision 网格改一次 +1
    public static void SetGrid(byte[] cells, int width, int height, float lon0, float lat0, float step, WorldMapFrame imported, int revision)
    {
        _cells = cells;
        _w = width;
        _h = height;
        _lon0 = lon0;
        _lat0 = lat0;
        _step = step;
        _importFrame = imported;
        _gridRevision = revision;
    }

    // 各类的底色(同 WildernessSystem.ColorOf 的色系)
    public static Color32 KindColor(WildKind kind) => kind switch
    {
        WildKind.Mountain => new Color32(128, 112, 96, 255),
        WildKind.Desert => new Color32(214, 190, 132, 255),
        WildKind.Tundra => new Color32(200, 214, 224, 255),
        WildKind.Jungle => new Color32(70, 112, 66, 255),
        WildKind.Swamp => new Color32(104, 116, 82, 255),
        _ => new Color32(160, 160, 160, 255)
    };

    #endregion

    #region 每帧

    // 编辑器每帧调：该重算的重算。allowHeavy 为假(刷地形、刷开发度拖着)时取样那一段不算，归属那一段至多每 OwnSeconds 秒一次
    public static void Update(bool allowHeavy)
    {
        WorldMapEra era = ScenarioEditing.Era;
        if (era == null || _cells == null || _w <= 0 || _h <= 0 || _step <= 0f) return;
        WorldMapFrame frame = era.map != null ? _importFrame : EarthTerrain.Frames.FirstOrDefault(item => item.id == era.frame) ?? EarthTerrain.Eurasia;
        if (frame?.sizes == null || frame.sizes.Length == 0) return;
        int sizeIndex = Mathf.Clamp(SizeIndex, 0, frame.sizes.Length - 1);
        int geometry = Hash((frame.id ?? "").GetHashCode(), frame.west.GetHashCode(), frame.east.GetHashCode(), frame.sizes[sizeIndex].x,
            frame.sizes[sizeIndex].y, sizeIndex);
        if (_checkedRevision != ScenarioEditing.Revision || _checkedGeometry != geometry || _layout == null)
        {
            _seatKey = SeatKey(frame, frame.sizes[sizeIndex], sizeIndex);
            _checkedRevision = ScenarioEditing.Revision;
            _checkedGeometry = geometry;
        }
        string table = era.map == null ? WorldMapData.BaseOf(era) : null;
        int key = Hash(_gridRevision, geometry, _seatKey, ScenarioEditing.DevelopRevision, table?.GetHashCode() ?? 0, era.map == null ? 1 : 2,
            era.wilderness?.Count ?? 0);
        float now = Time.unscaledTime;
        if (_layout == null || _layout.Key != key && allowHeavy && now - _heavyAt >= HeavySeconds)
        {
            Layout old = _layout;
            try
            {
                _layout = Build(frame, sizeIndex, table, key, Hash(_gridRevision, geometry, era.map == null ? 1 : 2));
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][剧本编辑] 无人区预测失败: {exception}");
                _layout = old ?? new Layout { Key = key, Frame = frame, Size = frame.sizes[sizeIndex], ZoneDegrees = 1f };
                _layout.Key = key;
            }
            _heavyAt = now;
            _ownedRevision = int.MinValue;
            if (old == null || old.Frame != _layout.Frame || old.Size != _layout.Size || old.W != _layout.W) GeometryVersion++;
            Version++;
        }
        if (_ownedRevision != ScenarioEditing.Revision && (allowHeavy || now - _ownAt >= OwnSeconds))
        {
            Own();
            _ownedRevision = ScenarioEditing.Revision;
            _ownAt = now;
            Version++;
        }
    }

    private static int Hash(params int[] parts)
    {
        int hash = 17;
        unchecked
        {
            foreach (int part in parts) hash = hash * 31 + part;
        }
        return hash;
    }

    // 城心落在哪些地块(编号、位置、等级)：变了才重新取样(城挪进挪出无人区，城心那块留给城)
    private static int SeatKey(WorldMapFrame frame, Vector2Int size, int sizeIndex)
    {
        frame.Bounds(size, out float bottom, out float degrees);
        int width = size.x * 64, height = size.y * 64, hash = 17;
        unchecked
        {
            foreach (WorldMapCity city in ScenarioEditing.Cities)
            {
                if (!ScenarioEditing.Active(city)) continue;
                int tx = Mathf.FloorToInt((city.lon - frame.west) / degrees), ty = Mathf.FloorToInt((city.lat - bottom) / degrees);
                if (tx < 4 || ty < 4 || tx >= width - 4 || ty >= height - 4) continue;
                int rank = ScenarioEditing.RankOf(city);
                if (sizeIndex == 0 && rank > 2) continue;
                hash = (hash * 31 + (city.id ?? "").GetHashCode()) * 31 + (ty / 8 * (width / 8) + tx / 8);
                hash = hash * 7 + rank;
            }
        }
        return hash;
    }

    #endregion

    #region 取样、归类、连片

    private static Layout Build(WorldMapFrame frame, int sizeIndex, string table, int key, int sampleKey)
    {
        Vector2Int size = frame.sizes[sizeIndex];
        frame.Bounds(size, out float bottom, out float degrees);
        int tw = size.x * 64, th = size.y * 64, zw = tw / 8, zh = th / 8, n = zw * zh;
        // 一、取样(地形、范围、大小没变就沿用)
        if (_sampled == null || _sampled.Key != sampleKey) _sampled = Sample(frame, size, sampleKey);
        Sampled sampled = _sampled;
        var layout = new Layout
        {
            Key = key, Frame = frame, Size = size, SizeIndex = sizeIndex, West = frame.west, Bottom = bottom, ZoneDegrees = degrees * 8f,
            W = zw, H = zh, Land = sampled.Land, Raw = new byte[n], Region = new int[n], Dev = new float[n], Terr = new int[n],
            Label = new int[n]
        };
        byte[] water = sampled.Water, rock = sampled.Rock, hills = sampled.Hills, frozen = sampled.Frozen, sand = sampled.Sand;
        // 时代开发度要把草木平地算回丛林沼泽：这三样拷一份再改
        var jungle = (byte[])sampled.Jungle.Clone();
        var swamp = (byte[])sampled.Swamp.Clone();
        var green = (byte[])sampled.Green.Clone();

        // 二、城心(放得下的城)：那块留给城
        var anchored = new bool[n];
        var regions = new HashSet<string>(ScenarioEditing.Regions.Select(region => region.id));
        var seatPoints = new Dictionary<int, List<Vector2>>();
        foreach (WorldMapCity city in ScenarioEditing.Cities)
        {
            if (!ScenarioEditing.Active(city) || !regions.Contains(city.region ?? "") || layout.SeatOf.ContainsKey(city.id)) continue;
            int tx = Mathf.FloorToInt((city.lon - frame.west) / degrees), ty = Mathf.FloorToInt((city.lat - bottom) / degrees);
            if (tx < 4 || ty < 4 || tx >= tw - 4 || ty >= th - 4) continue;
            if (sizeIndex == 0 && ScenarioEditing.RankOf(city) > 2) continue;
            int z = ty / 8 * zw + tx / 8;
            layout.SeatOf[city.id] = layout.Seats.Count;
            layout.Seats.Add(city.id);
            layout.SeatZone.Add(z);
            anchored[z] = true;
            int cell = SeatCell(city.lon, city.lat);
            (seatPoints.TryGetValue(cell, out List<Vector2> list) ? list : seatPoints[cell] = new List<Vector2>()).Add(new Vector2(city.lon, city.lat));
        }

        // 三、时代开发度与刷的开发度(见 WorldMapDevelopment.Apply)：当年没开出来的草木平地算回丛林、大泽；刷满的开满、留给城
        IReadOnlyDictionary<int, string> develop = ScenarioEditing.DevelopCells;
        for (int z = 0; z < n; z++)
        {
            layout.Dev[z] = -1f;
            if (anchored[z]) continue;
            int land = layout.Land[z];
            float lon = frame.west + ((z % zw) * 8 + 4.5f) * degrees, lat = bottom + ((z / zw) * 8 + 4.5f) * degrees;
            string text = null;
            float mark = 0f;
            bool painted = develop.Count > 0 && develop.TryGetValue(ScenarioCells.Key(lon, lat, ScenarioCells.LandStep), out text) &&
                           WorldMapDevelopment.ParseValue(text, out mark);
            float value = -1f;
            bool areaSwamp = false;
            bool inArea = table != null && WorldMapDevelopment.AreaAt(table, lon, lat, out value, out areaSwamp);
            bool convertible = land >= MinLand && jungle[z] + swamp[z] + green[z] >= land * WetShare;
            float dev;
            bool toSwamp;
            if (painted)
            {
                dev = Mathf.Clamp(mark, 0f, WildernessDevelopmentSystem.Full);
                // 刷的不在哪一片里：按地块里原有的丛林、沼泽哪样多定类型；一格都没有的(草地)不管
                if (inArea) toSwamp = areaSwamp;
                else if (jungle[z] + swamp[z] > 0) toSwamp = swamp[z] > jungle[z];
                else continue;
                if (dev >= WildernessDevelopmentSystem.Full)
                {
                    layout.Dev[z] = WildernessDevelopmentSystem.Full;
                    continue;
                }
                if (!convertible) continue;
            }
            else
            {
                if (!inArea || value < 0f || value >= WildernessDevelopmentSystem.Full || !convertible) continue;
                dev = value <= 0f ? 0f : WorldMapDevelopment.NearCity(value, SeatDistance(seatPoints, lon, lat));
                if (dev >= WildernessDevelopmentSystem.Full) continue;
                toSwamp = areaSwamp;
            }
            if (toSwamp) swamp[z] = (byte)(swamp[z] + green[z]);
            else jungle[z] = (byte)(jungle[z] + green[z]);
            green[z] = 0;
            layout.Dev[z] = dev;
        }

        // 四、归类(同 WildernessSystem.Classify，开局没有上回的类型)；刷满开发度的雨林大泽开满、留给城
        for (int z = 0; z < n; z++)
        {
            layout.Raw[z] = (byte)Classify(layout.Land[z], water[z], rock[z], hills[z], frozen[z], sand[z], jungle[z], swamp[z]);
            if (layout.Dev[z] >= WildernessDevelopmentSystem.Full && WildernessDevelopmentSystem.Develops((WildKind)layout.Raw[z])) anchored[z] = true;
        }

        // 五、连片(同 WildernessSystem.Regroup)：同类型四邻连成核心；大的先长，往外收这一类地形过半的地块，长到同类的小片整片并进来；
        // 去掉不到 MinZones 块的
        var piece = new int[n];
        for (int z = 0; z < n; z++) piece[z] = -1;
        var groups = new List<(WildKind kind, List<int> zones)>();
        var queue = new Queue<int>();
        for (int start = 0; start < n; start++)
        {
            if (piece[start] >= 0 || layout.Raw[start] == 0 || anchored[start]) continue;
            byte kind = layout.Raw[start];
            int mark = groups.Count;
            var zones = new List<int>();
            piece[start] = mark;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int z = queue.Dequeue();
                zones.Add(z);
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(z, d, zw, zh);
                    if (next < 0 || piece[next] >= 0 || layout.Raw[next] != kind || anchored[next]) continue;
                    piece[next] = mark;
                    queue.Enqueue(next);
                }
            }
            groups.Add(((WildKind)kind, zones));
        }
        var grown = new bool[groups.Count];
        foreach (int g in Enumerable.Range(0, groups.Count).OrderByDescending(index => groups[index].zones.Count).ToList())
        {
            List<int> zones = groups[g].zones;
            if (zones.Count == 0) continue;
            grown[g] = true;
            WildKind kind = groups[g].kind;
            float edge = kind == WildKind.Mountain ? MountainEdgeShare : EdgeShare;
            queue.Clear();
            foreach (int z in zones) queue.Enqueue(z);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int next = Neighbour(current, d, zw, zh);
                    if (next < 0) continue;
                    int other = piece[next];
                    if (other >= 0)
                    {
                        if (other == g || grown[other] || groups[other].kind != kind) continue;
                        foreach (int z in groups[other].zones)
                        {
                            piece[z] = g;
                            zones.Add(z);
                            queue.Enqueue(z);
                        }
                        groups[other].zones.Clear();
                        grown[other] = true;
                        continue;
                    }
                    int land = layout.Land[next];
                    if (anchored[next] || land < MinLand) continue;
                    int share = kind switch
                    {
                        WildKind.Mountain => rock[next] + hills[next],
                        WildKind.Desert or WildKind.Tundra => sand[next] + frozen[next],
                        _ => jungle[next] + swamp[next]
                    };
                    if (share < land * edge) continue;
                    piece[next] = g;
                    zones.Add(next);
                    queue.Enqueue(next);
                }
            }
        }
        for (int z = 0; z < n; z++) layout.Region[z] = -1;
        foreach ((WildKind kind, List<int> zones) in groups.Where(group => group.zones.Count >= WildernessSystem.MinZones)
                     .OrderByDescending(group => group.zones.Count))
        {
            var region = new Piece { Kind = kind };
            float sumX = 0f, sumY = 0f;
            foreach (int z in zones)
            {
                layout.Region[z] = layout.Pieces.Count;
                region.Zones.Add(z);
                sumX += z % zw;
                sumY += z / zw;
            }
            region.Lon = frame.west + (sumX / zones.Count * 8f + 4.5f) * degrees;
            region.Lat = bottom + (sumY / zones.Count * 8f + 4.5f) * degrees;
            layout.Pieces.Add(region);
        }
        // 开发度只记在雨林大泽里(没刷、不在时代表里的天然丛林沼泽为零)
        for (int z = 0; z < n; z++)
        {
            int r = layout.Region[z];
            layout.Dev[z] = r >= 0 && WildernessDevelopmentSystem.Develops(layout.Pieces[r].Kind) ? Mathf.Max(0f, layout.Dev[z]) : -1f;
        }

        Territory(layout, rock, hills, frozen, sand, jungle, swamp);
        Attach(layout);
        History(layout);
        return layout;
    }

    // 取样：每格取最近的预览像素(同 EarthTerrain.TypeAt 就近取地球网格)，数各块各类格子；地球地图画大河水道
    private static Sampled Sample(WorldMapFrame frame, Vector2Int size, int key)
    {
        frame.Bounds(size, out float bottom, out float degrees);
        int tw = size.x * 64, th = size.y * 64, zw = tw / 8, zh = th / 8, n = zw * zh;
        var sampled = new Sampled
        {
            Key = key, Land = new byte[n], Water = new byte[n], Rock = new byte[n], Hills = new byte[n], Frozen = new byte[n], Sand = new byte[n],
            Jungle = new byte[n], Swamp = new byte[n], Green = new byte[n]
        };
        var col = new int[tw];
        var row = new int[th];
        for (int x = 0; x < tw; x++)
        {
            int px = Mathf.RoundToInt((frame.west + (x + 0.5f) * degrees - _lon0) / _step);
            col[x] = px >= 0 && px < _w ? px : -1;
        }
        for (int y = 0; y < th; y++)
        {
            int py = Mathf.RoundToInt((bottom + (y + 0.5f) * degrees - _lat0) / _step);
            row[y] = py >= 0 && py < _h ? py * _w : -1;
        }
        bool[] river = ScenarioEditing.Era?.map == null ? Rivers(frame, size) : null;
        byte[] water = sampled.Water, rock = sampled.Rock, hills = sampled.Hills, frozen = sampled.Frozen, sand = sampled.Sand,
            jungle = sampled.Jungle, swamp = sampled.Swamp, green = sampled.Green;
        for (int zy = 0; zy < zh; zy++)
        for (int zx = 0; zx < zw; zx++)
        {
            int z = zy * zw + zx;
            int cw = 0, cr = 0, ch = 0, cf = 0, cs = 0, cj = 0, csw = 0, cg = 0;
            for (int dy = 0; dy < 8; dy++)
            {
                int ty = zy * 8 + dy, offset = row[ty];
                for (int dx = 0; dx < 8; dx++)
                {
                    int tx = zx * 8 + dx, c = col[tx];
                    int cell = offset < 0 || c < 0 ? WaterCell : _cells[offset + c];
                    int relief = cell & ReliefMask;
                    if (relief != WaterRelief && river != null && river[ty * tw + tx]) relief = WaterRelief;
                    if (relief == WaterRelief)
                    {
                        cw++;
                        continue;
                    }
                    if (relief == RockRelief) cr++;
                    else if (relief == HillRelief) ch++;
                    switch (cell >> GroundShift)
                    {
                        case FrozenGround: cf++; break;
                        case SandGround: cs++; break;
                        case JungleGround: cj++; break;
                        case SwampGround: csw++; break;
                        case GreenGround: cg++; break;
                    }
                }
            }
            water[z] = (byte)cw;
            rock[z] = (byte)cr;
            hills[z] = (byte)ch;
            frozen[z] = (byte)cf;
            sand[z] = (byte)cs;
            jungle[z] = (byte)cj;
            swamp[z] = (byte)csw;
            green[z] = (byte)cg;
            sampled.Land[z] = (byte)(64 - cw);
        }
        return sampled;
    }

    private static WildKind Classify(int land, int water, int rock, int hills, int frozen, int sand, int jungle, int swamp)
    {
        if (land < MinLand || water > 64 * MaxWater) return WildKind.None;
        if (rock >= land * MountainCore || rock >= land * MountainFlank && rock + hills >= land * MountainRugged) return WildKind.Mountain;
        if (frozen + sand >= land * BarrenShare) return frozen > sand ? WildKind.Tundra : WildKind.Desert;
        if (swamp >= land * SwampShare) return WildKind.Swamp;
        if (jungle + swamp >= land * JungleShare) return jungle >= swamp ? WildKind.Jungle : WildKind.Swamp;
        return WildKind.None;
    }

    private static readonly int[] Dx4 = { 1, -1, 0, 0 }, Dy4 = { 0, 0, 1, -1 };
    private static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 }, Dy8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

    // 第 d 个四邻(出界为 -1；不用迭代器，几万块地连片时不生垃圾)
    private static int Neighbour(int z, int d, int zw, int zh)
    {
        int nx = z % zw + Dx4[d], ny = z / zw + Dy4[d];
        return nx >= 0 && ny >= 0 && nx < zw && ny < zh ? ny * zw + nx : -1;
    }

    // 大河水道(同 EarthTerrain.DrawRivers：图边一格不画)
    private static bool[] Rivers(WorldMapFrame frame, Vector2Int size)
    {
        string key = frame.id + "|" + size.x + "|" + size.y;
        if (_river != null && _riverKey == key) return _river;
        int tw = size.x * 64, th = size.y * 64;
        var river = new bool[tw * th];
        EarthTerrain.RiverTiles(frame, size, (x, y) =>
        {
            if (x >= 1 && y >= 1 && x < tw - 1 && y < th - 1) river[y * tw + x] = true;
        });
        _river = river;
        _riverKey = key;
        return river;
    }

    // 城心按 2° 一格分桶(开发度按离最近的城多远算，远过几度就当没有)
    private static int SeatCell(float lon, float lat) => Mathf.FloorToInt(lon / 2f) * 1000 + Mathf.FloorToInt(lat / 2f);

    private static float SeatDistance(Dictionary<int, List<Vector2>> seats, float lon, float lat)
    {
        float squeeze = Mathf.Cos(lat * Mathf.Deg2Rad), best = float.MaxValue;
        int cx = Mathf.FloorToInt(lon / 2f), cy = Mathf.FloorToInt(lat / 2f);
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
        {
            if (!seats.TryGetValue((cx + dx) * 1000 + cy + dy, out List<Vector2> list)) continue;
            foreach (Vector2 seat in list)
            {
                float x = (seat.x - lon) * squeeze, y = seat.y - lat;
                best = Mathf.Min(best, x * x + y * y);
            }
        }
        return best == float.MaxValue ? float.MaxValue : Mathf.Sqrt(best);
    }

    #endregion

    #region 辖地、挂法理的走法

    // 城的辖地：从城心按地形代价往外铺(无人区、水面铺不进去)，铺不到的陆地开局多半补随机小国
    private static void Territory(Layout layout, byte[] rock, byte[] hills, byte[] frozen, byte[] sand, byte[] jungle, byte[] swamp)
    {
        int n = layout.W * layout.H;
        var cost = new int[n];
        for (int z = 0; z < n; z++)
        {
            int land = layout.Land[z];
            cost[z] = land < TerritoryLand ? 0
                : rock[z] >= land * 0.5f ? 60
                : frozen[z] + sand[z] >= land * BarrenShare ? 30
                : jungle[z] + swamp[z] >= land * 0.5f ? 25
                : frozen[z] + sand[z] >= land * 0.5f ? 20
                : rock[z] + hills[z] >= land * 0.5f ? 16
                : 10;
        }
        var dist = new int[n];
        for (int z = 0; z < n; z++)
        {
            dist[z] = int.MaxValue;
            layout.Terr[z] = -1;
        }
        var buckets = new List<int>[FillerReach + 1];
        for (int i = 0; i < layout.Seats.Count; i++)
        {
            int z = layout.SeatZone[i];
            if (dist[z] == 0) continue;
            dist[z] = 0;
            layout.Terr[z] = i;
            (buckets[0] ??= new List<int>()).Add(z);
        }
        for (int d = 0; d <= FillerReach; d++)
        {
            List<int> bucket = buckets[d];
            if (bucket == null) continue;
            for (int k = 0; k < bucket.Count; k++)
            {
                int z = bucket[k];
                if (dist[z] != d) continue;
                for (int dir = 0; dir < 4; dir++)
                {
                    int next = Neighbour(z, dir, layout.W, layout.H);
                    if (next < 0 || layout.Region[next] >= 0 || cost[next] == 0) continue;
                    int nd = d + cost[next];
                    if (nd > FillerReach || nd >= dist[next]) continue;
                    dist[next] = nd;
                    layout.Terr[next] = layout.Terr[z];
                    (buckets[nd] ??= new List<int>()).Add(next);
                }
            }
        }
        for (int z = 0; z < n; z++)
            if (layout.Terr[z] < 0 && layout.Region[z] < 0 && layout.Land[z] >= TerritoryLand) layout.Terr[z] = Filler;

        // 各片周边的城(八邻、不是无人区的陆地)
        foreach (Piece region in layout.Pieces)
            foreach (int z in region.Zones)
            {
                int x = z % layout.W, y = z / layout.W;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Dx8[d], ny = y + Dy8[d];
                    if (nx < 0 || ny < 0 || nx >= layout.W || ny >= layout.H) continue;
                    int next = ny * layout.W + nx;
                    if (layout.Region[next] >= 0 || layout.Terr[next] == -1) continue;
                    region.Around.Add(layout.Terr[next]);
                }
            }
    }

    // 挂法理的走法(同 WildernessSystem.Attach)：从各城(连补空小国)的辖地按地块往外走(四邻、不过海，至多 MaxAttachSteps 块)，
    // 无人区的地块记最先走到它的那座城
    private static void Attach(Layout layout)
    {
        int n = layout.W * layout.H;
        var step = new int[n];
        var queue = new Queue<int>();
        for (int z = 0; z < n; z++)
        {
            step[z] = -1;
            layout.Label[z] = -1;
            if (layout.Terr[z] == -1) continue;
            layout.Label[z] = layout.Terr[z];
            step[z] = 0;
            queue.Enqueue(z);
        }
        while (queue.Count > 0)
        {
            int z = queue.Dequeue();
            if (step[z] >= MaxAttachSteps) continue;
            for (int d = 0; d < 4; d++)
            {
                int next = Neighbour(z, d, layout.W, layout.H);
                if (next < 0 || step[next] >= 0 || layout.Land[next] <= SeaGround) continue;
                step[next] = step[z] + 1;
                layout.Label[next] = layout.Label[z];
                queue.Enqueue(next);
            }
        }
    }

    // 时代文件的史实归属(地球地图；见 WorldMapWilderness.HistoricalOwner)：各无人区地块落在哪个写了归属的地理单元里
    private static void History(Layout layout)
    {
        WorldMapEra era = ScenarioEditing.Era;
        if (era?.map != null || era?.wilderness == null || era.wilderness.Count == 0) return;
        layout.History = new string[layout.W * layout.H];
        for (int r = 0; r < layout.Pieces.Count; r++)
        {
            Piece region = layout.Pieces[r];
            foreach (int z in region.Zones)
            {
                Center(layout, z, out float lon, out float lat);
                layout.History[z] = WorldMapWilderness.HistoricalOwner(era.wilderness, region.Kind, lon, lat);
            }
        }
    }

    private static void Center(Layout layout, int z, out float lon, out float lat)
    {
        float tile = layout.ZoneDegrees / 8f;
        lon = layout.West + ((z % layout.W) * 8 + 4.5f) * tile;
        lat = layout.Bottom + ((z / layout.W) * 8 + 4.5f) * tile;
    }

    #endregion

    #region 归属

    private static void Own()
    {
        Layout layout = _layout;
        WorldMapEra era = ScenarioEditing.Era;
        if (layout == null || era == null) return;
        int n = layout.W * layout.H;
        _owner = new string[n];
        _title = new string[n];
        _source = new byte[n];
        _neutral = new bool[layout.Pieces.Count];
        _surround = new string[layout.Pieces.Count];
        if (layout.Pieces.Count == 0) return;
        var cities = new Dictionary<string, WorldMapCity>();
        foreach (WorldMapCity city in ScenarioEditing.Cities)
            if (city?.id != null && !cities.ContainsKey(city.id)) cities[city.id] = city;
        WorldMapCity SeatCity(int index) => index >= 0 && index < layout.Seats.Count && cities.TryGetValue(layout.Seats[index], out WorldMapCity city)
            ? city : null;
        // 各城的国家(独立的城自成一国)
        string OwnerKey(WorldMapCity city) => city == null ? null
            : ScenarioEditing.OwnerOf(city) is WorldMapPolity polity ? "p:" + polity.id : "c:" + city.id;
        // 王国法理首都(同 WorldMapScenario.BuildTitles：剧本定的，再是地区里的国都，再是最大的城)；地区里没有放得下的城就没有这块法理
        var byRegion = new Dictionary<string, List<WorldMapCity>>();
        for (int i = 0; i < layout.Seats.Count; i++)
        {
            WorldMapCity city = SeatCity(i);
            if (city == null) continue;
            (byRegion.TryGetValue(city.region, out List<WorldMapCity> list) ? list : byRegion[city.region] = new List<WorldMapCity>()).Add(city);
        }
        var titleSeat = new Dictionary<string, WorldMapCity>();
        WorldMapCity TitleSeat(string region)
        {
            if (string.IsNullOrEmpty(region)) return null;
            if (titleSeat.TryGetValue(region, out WorldMapCity seat)) return seat;
            if (byRegion.TryGetValue(region, out List<WorldMapCity> members))
            {
                string chosen = ScenarioEditing.Region(region)?.capital;
                seat = (string.IsNullOrEmpty(chosen) ? null : members.FirstOrDefault(city => city.id == chosen)) ??
                       members.Where(city => ScenarioEditing.OwnerOf(city) is not WorldMapPolity owner || owner.capital == city.id)
                           .OrderBy(ScenarioEditing.RankOf).FirstOrDefault() ??
                       members.OrderBy(ScenarioEditing.RankOf).First();
            }
            titleSeat[region] = seat;
            return seat;
        }
        // 一座城的法理归谁：法理首都所在国；不称帝的国家都城不是法理首都时另立一块、归它自己(见 WorldMapScenario.OwnTitles)
        var holders = new Dictionary<WorldMapCity, string>();
        string HolderOfCity(WorldMapCity city)
        {
            if (city == null) return null;
            if (holders.TryGetValue(city, out string holder)) return holder;
            string seatKey = OwnerKey(TitleSeat(city.region));
            WorldMapPolity polity = ScenarioEditing.OwnerOf(city);
            holder = polity != null && !polity.empire && polity.capital == city.id && seatKey != "p:" + polity.id ? "p:" + polity.id : seatKey;
            holders[city] = holder;
            return holder;
        }
        // 国家的都城(放得下的才算)
        var capitals = new Dictionary<string, WorldMapCity>();
        WorldMapCity CapitalOf(string polityId)
        {
            if (polityId == null) return null;
            if (capitals.TryGetValue(polityId, out WorldMapCity capital)) return capital;
            WorldMapPolity polity = ScenarioEditing.Polity(polityId);
            capital = polity != null && layout.SeatOf.TryGetValue(polity.capital ?? "", out int index) ? SeatCity(index) : null;
            capitals[polityId] = capital;
            return capital;
        }

        IReadOnlyDictionary<int, string> paint = ScenarioEditing.LandCells;
        for (int r = 0; r < layout.Pieces.Count; r++)
        {
            Piece region = layout.Pieces[r];
            // 周边的城全属同一国
            string common = null;
            bool mixed = false;
            foreach (int label in region.Around)
            {
                string key = label == Filler ? null : OwnerKey(SeatCity(label));
                if (key == null || common != null && common != key)
                {
                    mixed = true;
                    break;
                }
                common = key;
            }
            _surround[r] = mixed ? null : common;
            if (paint.Count > 0)
                foreach (int z in region.Zones)
                {
                    Center(layout, z, out float lon, out float lat);
                    if (!paint.TryGetValue(ScenarioCells.Key(lon, lat, ScenarioCells.LandStep), out string key) || key != "n") continue;
                    _neutral[r] = true;
                    break;
                }
            foreach (int z in region.Zones)
            {
                string title = null, holder = null;
                Source source = Source.None;
                Center(layout, z, out float lon, out float lat);
                if (paint.Count > 0 && paint.TryGetValue(ScenarioCells.Key(lon, lat, ScenarioCells.LandStep), out string key) && key.Length > 2)
                {
                    string id = key.Substring(2);
                    WorldMapCity seat = key[0] == 'p' ? CapitalOf(id) : key[0] == 'r' ? TitleSeat(id) : null;
                    if (seat != null)
                    {
                        title = key[0] == 'p' ? seat.region : id;
                        holder = key[0] == 'p' ? HolderOfCity(seat) : OwnerKey(seat);
                        source = Source.Paint;
                    }
                }
                if (source == Source.None && layout.History?[z] is string historical && CapitalOf(historical) is WorldMapCity capital)
                {
                    title = capital.region;
                    holder = HolderOfCity(capital);
                    source = Source.History;
                }
                if (source == Source.None && layout.Label[z] != -1)
                {
                    WorldMapCity near = SeatCity(layout.Label[z]);
                    title = near?.region;
                    holder = layout.Label[z] == Filler ? "f" : HolderOfCity(near);
                    source = Source.Near;
                }
                if (holder == null && _surround[r] != null)
                {
                    holder = _surround[r];
                    if (source == Source.None) source = Source.Surround;
                }
                _title[z] = title;
                _owner[z] = _neutral[r] ? null : holder;
                _source[z] = (byte)(_neutral[r] ? Source.Neutral : source);
            }
        }
    }

    #endregion

    #region 查询

    // 经纬度落在哪块地(按预测的地图范围与大小；范围外为 -1)
    public static int ZoneAt(float lon, float lat)
    {
        Layout layout = _layout;
        if (layout == null || layout.ZoneDegrees <= 0f || layout.W <= 0) return -1;
        int zx = Mathf.FloorToInt((lon - layout.West) / layout.ZoneDegrees), zy = Mathf.FloorToInt((lat - layout.Bottom) / layout.ZoneDegrees);
        return zx >= 0 && zy >= 0 && zx < layout.W && zy < layout.H ? zy * layout.W + zx : -1;
    }

    private static bool Valid(int zone) => _layout?.Region != null && zone >= 0 && zone < _layout.Region.Length;

    // 所在的那一片(Pieces 的下标；不是无人区为 -1)
    public static int RegionOf(int zone) => Valid(zone) ? _layout.Region[zone] : -1;

    public static WildKind KindOf(int zone)
    {
        int region = RegionOf(zone);
        return region >= 0 ? _layout.Pieces[region].Kind : WildKind.None;
    }

    // 雨林大泽的开发度(不是为负)
    public static float DevOf(int zone) => Valid(zone) && _layout.Dev != null ? _layout.Dev[zone] : -1f;

    // 归谁："p:国家"、"c:独立的城"、"f" 补空小国；永久中立、无主为 null
    public static string OwnerOf(int zone) => Valid(zone) && _owner != null && zone < _owner.Length ? _owner[zone] : null;

    // 挂在哪块王国法理上(地区编号；没挂、挂在补空小国上为 null)
    public static string TitleOf(int zone) => Valid(zone) && _title != null && zone < _title.Length ? _title[zone] : null;

    public static Source SourceOf(int zone) => Valid(zone) && _source != null && zone < _source.Length ? (Source)_source[zone] : Source.None;

    public static bool Neutral(int region) => _neutral != null && region >= 0 && region < _neutral.Length && _neutral[region];

    public static string Surround(int region) => _surround != null && region >= 0 && region < _surround.Length ? _surround[region] : null;

    // 城放在这里会不会落在无人区里(城心那块按地形是荒地，或四邻有两块以上是无人区)；是哪一类
    public static WildKind KindNear(float lon, float lat)
    {
        int zone = ZoneAt(lon, lat);
        if (!Valid(zone) || _layout.Raw == null) return WildKind.None;
        if (_layout.Region[zone] >= 0) return _layout.Pieces[_layout.Region[zone]].Kind;
        if (_layout.Raw[zone] != 0) return (WildKind)_layout.Raw[zone];
        var counts = new Dictionary<WildKind, int>();
        for (int d = 0; d < 4; d++)
        {
            int next = Neighbour(zone, d, _layout.W, _layout.H);
            int region = next < 0 ? -1 : _layout.Region[next];
            if (region < 0) continue;
            WildKind kind = _layout.Pieces[region].Kind;
            counts[kind] = counts.TryGetValue(kind, out int count) ? count + 1 : 1;
        }
        return counts.Where(pair => pair.Value >= 2).OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
    }

    // 各类几片、几块；永久中立几片
    public static void Summary(int[] regions, int[] zones, out int neutral)
    {
        neutral = 0;
        if (_layout == null) return;
        for (int r = 0; r < _layout.Pieces.Count; r++)
        {
            int kind = (int)_layout.Pieces[r].Kind;
            if (kind < regions.Length) regions[kind]++;
            if (kind < zones.Length) zones[kind] += _layout.Pieces[r].Zones.Count;
            if (Neutral(r)) neutral++;
        }
    }

    #endregion
}
