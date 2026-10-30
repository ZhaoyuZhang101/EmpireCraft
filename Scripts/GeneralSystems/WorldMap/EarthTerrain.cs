using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 地图范围：经度跨度定宽，纬度跨度按格子方正(一格的经纬度相同)由宽高比算出
public sealed class WorldMapFrame
{
    public string id;
    public float west;
    public float east;
    // 北界(欧亚非)；为 NaN 时以 centerLat 居中(亚欧非全图)
    public float top = float.NaN;
    public float centerLat;
    // 三档大小(区块数，一区块 64 格)：小、中、大
    public Vector2Int[] sizes;

    public float DegreesPerTile(int chunksWide) => (east - west) / (chunksWide * 64f);

    public void Bounds(Vector2Int size, out float bottom, out float degreesPerTile)
    {
        degreesPerTile = DegreesPerTile(size.x);
        float span = size.y * 64f * degreesPerTile;
        float north = float.IsNaN(top) ? centerLat + span / 2f : top;
        bottom = north - span;
    }
}

// 地球地表网格(WorldMaps/earth.bytes，由 Tools/WorldMap/build_earth.py 从 ETOPO 高程、Natural Earth 海陆湖泊、
// 柯本气候分区烘出；2026-10-29 改用 AWS Terrarium 高程按相对起伏重烘山、丘陵、高地，气候沿用旧网格)：每格 0.05°，存原版存档写法的地块编号；生成时按所选范围和大小逐格就近取样
public static class EarthTerrain
{
    public static readonly WorldMapFrame Eurasia = new()
    {
        id = "eurasia", west = -25f, east = 153f, top = 77.5f,
        sizes = new[] { new Vector2Int(16, 7), new Vector2Int(32, 15), new Vector2Int(40, 18) }
    };

    public static readonly WorldMapFrame OldWorld = new()
    {
        id = "oldworld", west = -26f, east = 155f, centerLat = 21f,
        sizes = new[] { new Vector2Int(14, 9), new Vector2Int(28, 18), new Vector2Int(34, 22) }
    };

    // 半幅(用户 10-30 "仅东亚还是仅欧非西亚，可以把预设的地图从中间对半砍")：从中间(约东经 63°)劈开，同样的区块数看半边，细一倍。
    // 东亚：中亚东半、南亚、东亚、东南亚北半，北到贝加尔湖一线；欧非西亚：欧洲、全非洲、西亚到伊朗
    public static readonly WorldMapFrame EastAsia = new()
    {
        id = "eastasia", west = 62f, east = 152f, top = 62f,
        sizes = new[] { new Vector2Int(14, 10), new Vector2Int(28, 20), new Vector2Int(32, 23) }
    };

    public static readonly WorldMapFrame WestWorld = new()
    {
        id = "westworld", west = -26f, east = 64f, centerLat = 21f,
        sizes = new[] { new Vector2Int(10, 13), new Vector2Int(20, 25), new Vector2Int(24, 30) }
    };

    public static readonly WorldMapFrame[] Frames = { Eurasia, OldWorld, EastAsia, WestWorld };

    private static int _cols, _rows;
    private static float _lon0, _lat0, _step;
    private static byte[] _grid;
    private static string[] _types;

    public static bool EnsureLoaded()
    {
        if (_grid != null) return true;
        string path = Path.Combine(WorldMapData.Folder, "earth.bytes");
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(file);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ECEARTH1") throw new InvalidDataException("文件头不对");
            _cols = reader.ReadInt32();
            _rows = reader.ReadInt32();
            _lon0 = reader.ReadSingle();
            _lat0 = reader.ReadSingle();
            _step = reader.ReadSingle();
            int typeCount = reader.ReadInt32();
            _types = new string[typeCount];
            for (int i = 0; i < typeCount; i++) _types[i] = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadByte()));
            int length = reader.ReadInt32();
            byte[] packed = reader.ReadBytes(length);
            var grid = new byte[_cols * _rows];
            using (var deflate = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress))
            {
                int read = 0;
                while (read < grid.Length)
                {
                    int n = deflate.Read(grid, read, grid.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < grid.Length) throw new InvalidDataException("网格不完整");
            }
            _grid = grid;
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 地表数据读不出来 {path}: {exception.Message}");
            _grid = null;
            return false;
        }
    }

    // 剧本刷的地形(ScenarioCells.TerrainStep 一格 → 地块写法，见 ScenarioEditing)：铺地表时盖过网格
    private static Dictionary<int, string> _overrides;

    public static void SetOverrides(Dictionary<int, string> overrides) => _overrides = overrides is { Count: > 0 } ? overrides : null;

    // 导入地图(剧本编辑器第五期，见 WorldMapImporter)：换上导入的网格(经纬度从 0 起)，UseEarth 换回地球网格
    private static byte[] _earthGrid;
    private static int _earthCols, _earthRows;
    private static float _earthLon0, _earthLat0, _earthStep;

    public static bool Imported { get; private set; }

    public static string[] Types => _types ?? new string[0];

    public static int CodeOf(string type) => _types == null ? -1 : Array.IndexOf(_types, type);

    public static Color32 PreviewColorOf(string type) => PreviewColor(type ?? "");

    public static void UseGrid(byte[] grid, int cols, int rows, float step)
    {
        if (grid == null || !EnsureLoaded()) return;
        if (!Imported)
        {
            _earthGrid = _grid;
            _earthCols = _cols;
            _earthRows = _rows;
            _earthLon0 = _lon0;
            _earthLat0 = _lat0;
            _earthStep = _step;
        }
        _grid = grid;
        _cols = cols;
        _rows = rows;
        _lon0 = 0f;
        _lat0 = 0f;
        _step = step;
        Imported = true;
    }

    public static void UseEarth()
    {
        if (!Imported) return;
        _grid = _earthGrid;
        _cols = _earthCols;
        _rows = _earthRows;
        _lon0 = _earthLon0;
        _lat0 = _earthLat0;
        _step = _earthStep;
        Imported = false;
    }

    // 剧本编辑器的地形预览(见 ScenarioEditorScreen)：整张地表网格每 stride 格取一格，一格一像素(第 0 行在最下面)；
    // 另给陆地标记(铺国家颜色只铺陆地)。颜色同 Tools/WorldMap/build_earth.py 的预览图
    public static bool Preview(int stride, out Color32[] pixels, out bool[] land, out int width, out int height,
        out float lon0, out float lat0, out float step)
    {
        pixels = null;
        land = null;
        width = height = 0;
        lon0 = lat0 = step = 0f;
        if (!EnsureLoaded() || stride < 1) return false;
        width = _cols / stride;
        height = _rows / stride;
        lon0 = _lon0;
        lat0 = _lat0;
        step = _step * stride;
        var lut = new Color32[_types.Length];
        var dry = new bool[_types.Length];
        for (int i = 0; i < _types.Length; i++)
        {
            lut[i] = PreviewColor(_types[i]);
            dry[i] = !_types[i].Contains("ocean") && !_types[i].Contains("waters");
        }
        pixels = new Color32[width * height];
        land = new bool[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte code = _grid[y * stride * _cols + x * stride];
            int index = y * width + x;
            if (code >= lut.Length) code = 0;
            pixels[index] = lut[code];
            land[index] = dry[code];
        }
        return true;
    }

    // 与 Preview 同样取样的地块编号(下标即 Types；剧本编辑器预测无人区按它判格子，见 ScenarioWilderness)
    public static byte[] PreviewCodes(int stride)
    {
        if (!EnsureLoaded() || stride < 1) return null;
        int width = _cols / stride, height = _rows / stride;
        var codes = new byte[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            codes[y * width + x] = _grid[y * stride * _cols + x * stride];
        return codes;
    }

    // 地形大类(剧本编辑器铺图层颜色按它算代价)：0 水，1 平原，2 高原，3 丘陵，4 山地，5 雪山，6 荒漠冻土，7 雪丘
    public static byte ClassOf(string type)
    {
        if (string.IsNullOrEmpty(type) || type.Contains("ocean") || type.Contains("waters")) return 0;
        switch (type)
        {
            case "mountains": return 4;
            case "mountains:snow_block": return 5;
            case "hills": return 3;
            case "hills:snow_hills": return 7;
        }
        if (type.Contains("desert") || type.Contains("permafrost")) return 6;
        return type.EndsWith("_high") ? (byte)2 : (byte)1;
    }

    // 与 Preview 同样取样的地形大类
    public static byte[] PreviewClasses(int stride)
    {
        if (!EnsureLoaded() || stride < 1) return null;
        int width = _cols / stride, height = _rows / stride;
        var lut = new byte[_types.Length];
        for (int i = 0; i < _types.Length; i++) lut[i] = ClassOf(_types[i]);
        var classes = new byte[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte code = _grid[y * stride * _cols + x * stride];
            classes[y * width + x] = code < lut.Length ? lut[code] : (byte)0;
        }
        return classes;
    }

    private static Color32 PreviewColor(string type)
    {
        switch (type)
        {
            case "deep_ocean": return new Color32(40, 90, 190, 255);
            case "close_ocean": return new Color32(55, 120, 220, 255);
            case "shallow_waters": return new Color32(80, 170, 240, 255);
            case "sand": return new Color32(240, 220, 150, 255);
            case "mountains": return new Color32(90, 90, 90, 255);
            case "mountains:snow_block": return new Color32(245, 245, 250, 255);
            case "hills": return new Color32(130, 125, 110, 255);
            case "hills:snow_hills": return new Color32(215, 220, 230, 255);
        }
        bool high = type.EndsWith("_high");
        string biome = type.Contains(":") ? type.Substring(type.IndexOf(':') + 1) : type;
        if (biome.Contains("grass")) return high ? new Color32(70, 130, 45, 255) : new Color32(96, 160, 60, 255);
        if (biome.Contains("savanna")) return high ? new Color32(180, 160, 70, 255) : new Color32(200, 190, 90, 255);
        if (biome.Contains("jungle")) return high ? new Color32(30, 100, 30, 255) : new Color32(40, 120, 40, 255);
        if (biome.Contains("desert")) return high ? new Color32(225, 195, 110, 255) : new Color32(245, 225, 150, 255);
        if (biome.Contains("permafrost")) return high ? new Color32(180, 195, 210, 255) : new Color32(200, 215, 225, 255);
        if (biome.Contains("birch")) return high ? new Color32(90, 130, 75, 255) : new Color32(110, 150, 90, 255);
        if (biome.Contains("maple")) return high ? new Color32(110, 140, 60, 255) : new Color32(130, 160, 70, 255);
        if (biome.Contains("swamp")) return high ? new Color32(80, 100, 50, 255) : new Color32(90, 110, 60, 255);
        return new Color32(96, 160, 60, 255);
    }

    // 经纬度处的地块写法(超出网格按深海)
    public static string TypeAt(float lon, float lat)
    {
        if (_overrides != null && _overrides.TryGetValue(ScenarioCells.Key(lon, lat, ScenarioCells.TerrainStep), out string painted))
            return painted;
        int col = Mathf.RoundToInt((lon - _lon0) / _step);
        int row = Mathf.RoundToInt((lat - _lat0) / _step);
        if (_grid == null || col < 0 || row < 0 || col >= _cols || row >= _rows) return "deep_ocean";
        byte code = _grid[row * _cols + col];
        return code < _types.Length ? _types[code] : "deep_ocean";
    }

    // 逐格铺地表(照原版读档 SaveManager.loadTiles 的写法)
    public static void Fill(WorldMapFrame frame, Vector2Int size)
    {
        frame.Bounds(size, out float bottom, out float degrees);
        var resolved = new Dictionary<string, (string main, TopTileType top)>();
        int width = MapBox.width, height = MapBox.height;
        for (int x = 0; x < width; x++)
        {
            float lon = frame.west + (x + 0.5f) * degrees;
            for (int y = 0; y < height; y++)
            {
                float lat = bottom + (y + 0.5f) * degrees;
                string id = TypeAt(lon, lat);
                if (!resolved.TryGetValue(id, out var parts))
                {
                    int split = id.IndexOf(':');
                    parts = split < 0
                        ? (id, null)
                        : (id.Substring(0, split), AssetManager.top_tiles.get(id.Substring(split + 1)));
                    resolved[id] = parts;
                }
                WorldTile tile = World.world.GetTileSimple(x, y);
                if (parts.top == null) tile.setTileType(parts.main);
                else tile.setTileTypes(parts.main, parts.top);
                tile.Height = tile.Type.height_min;
            }
        }
    }

    // 大河折线(WorldMaps/rivers.bytes，Natural Earth 等级 0~5)：按地图大小画成水道——等级 0~3 两格宽、其余一格；
    // 每走 14 格留 2 格原地表作渡口：水道画实了会把大陆切成两岛，原版和本模组都按岛算陆路、开战够不够得着(见 WarReach)，
    // 留渡口两岸仍连成一片，军队要绕到渡口过河；河道所在区块水多，按本模组算"河流"地形(过河加阻力)
    private const int FordEvery = 14, FordWidth = 2;
    private static List<(int rank, Vector2[] points)> _rivers;

    private static bool EnsureRivers()
    {
        if (_rivers != null) return true;
        string path = Path.Combine(WorldMapData.Folder, "rivers.bytes");
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(file);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ECRIVER1") throw new InvalidDataException("文件头不对");
            int count = reader.ReadInt32();
            var rivers = new List<(int, Vector2[])>(count);
            for (int i = 0; i < count; i++)
            {
                int rank = reader.ReadByte();
                int points = reader.ReadUInt16();
                var line = new Vector2[points];
                for (int p = 0; p < points; p++) line[p] = new Vector2(reader.ReadInt16() / 100f, reader.ReadInt16() / 100f);
                rivers.Add((rank, line));
            }
            _rivers = rivers;
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 河流数据读不出来 {path}: {exception.Message}");
            _rivers = new List<(int, Vector2[])>();
            return false;
        }
    }

    public static void DrawRivers(WorldMapFrame frame, Vector2Int size)
    {
        int width = MapBox.width, height = MapBox.height;
        RiverTiles(frame, size, (x, y) => Wet(x, y, width, height));
    }

    // 大河水道落在哪些格上(DrawRivers 的走法，渡口已留开)：visit 收格坐标，出界、本来就是水面的由调用处判
    // (剧本编辑器预测无人区也照这个画水道，见 ScenarioWilderness)
    public static void RiverTiles(WorldMapFrame frame, Vector2Int size, Action<int, int> visit)
    {
        if (visit == null || !EnsureRivers()) return;
        frame.Bounds(size, out float bottom, out float degrees);
        foreach ((int rank, Vector2[] points) in _rivers)
        {
            int thickness = rank <= 3 ? 2 : 1;
            int walked = 0;
            for (int i = 1; i < points.Length; i++)
            {
                int x0 = Mathf.FloorToInt((points[i - 1].x - frame.west) / degrees), y0 = Mathf.FloorToInt((points[i - 1].y - bottom) / degrees);
                int x1 = Mathf.FloorToInt((points[i].x - frame.west) / degrees), y1 = Mathf.FloorToInt((points[i].y - bottom) / degrees);
                int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
                while (true)
                {
                    if (walked++ % FordEvery >= FordWidth)
                        for (int t = 0; t < thickness; t++) visit(x0 + (dy == 0 ? 0 : t), y0 + (dy == 0 ? t : 0));
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }
        }
    }

    private static void Wet(int x, int y, int width, int height)
    {
        if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1) return;
        WorldTile tile = World.world.GetTileSimple(x, y);
        if (tile?.Type == null || tile.Type.liquid) return;
        tile.setTileType("shallow_waters");
        tile.Height = tile.Type.height_min;
    }

    public static Vector2Int TileOf(WorldMapFrame frame, Vector2Int size, float lon, float lat)
    {
        frame.Bounds(size, out float bottom, out float degrees);
        return new Vector2Int(Mathf.FloorToInt((lon - frame.west) / degrees), Mathf.FloorToInt((lat - bottom) / degrees));
    }
}
