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

    public static readonly WorldMapFrame[] Frames = { Eurasia, OldWorld };

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

    // 经纬度处的地块写法(超出网格按深海)
    public static string TypeAt(float lon, float lat)
    {
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
        if (!EnsureRivers()) return;
        frame.Bounds(size, out float bottom, out float degrees);
        int width = MapBox.width, height = MapBox.height;
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
                        for (int t = 0; t < thickness; t++) Wet(x0 + (dy == 0 ? 0 : t), y0 + (dy == 0 ? t : 0), width, height);
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
