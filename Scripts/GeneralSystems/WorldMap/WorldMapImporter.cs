using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧本的导入地图(scenario.json 的 map 一节；图片是剧本文件夹里的 map.png)
public sealed class WorldMapImport
{
    // color：按颜色认地形(对照表见 map_colors.txt，颜色同剧本编辑器的地形预览)；height：灰度高程(越亮越高)
    public string mode = "color";
    // 灰度高程的海平面(0~1，低于它是海)
    public float sea = 0.4f;
}

// 导入地图(剧本编辑器第五期，见 Docs/plan-scenario-editor.md；用户 10-30 "地图也可以玩家自己导入")：
//   把剧本文件夹里的 map.png 读成地表网格(一像素一格，每格 Step 度；城的经纬度按左下角为 0 记)，开局时换掉地球网格铺地形
//   (EarthTerrain.UseGrid)，不画地球的大河、不按地球地理给无人区起名；三档大小按一像素一格、一格半、两格。
//   颜色图按最近的颜色认成地形；灰度图按海平面分海陆，陆地按高低分沙滩、平原、高原、丘陵、山地、雪山。图太大的按比例缩到 MaxSide 以内。
public static class WorldMapImporter
{
    public const string FileName = "map.png", LegendFile = "map_colors.txt";
    public const float Step = 0.1f;
    private const int MaxSide = 2048, BlankWidth = 1024, BlankHeight = 512;
    // 三档大小：一像素合几格
    private static readonly float[] Scales = { 1f, 1.5f, 2f };
    private const int MinChunks = 4, MaxChunks = 48;

    public sealed class Loaded
    {
        public byte[] grid;
        public int cols, rows;
        public WorldMapFrame frame;
        public string key;
    }

    private static Loaded _cache;

    public static bool Uses(WorldMapEra era) => era?.map != null && era.Custom;

    public static string PathOf(WorldMapEra era) => era?.Custom == true ? Path.Combine(era.folder, FileName) : null;

    // 读 map.png(按文件修改时间、读法缓存)；读不出来返回 null，error 为文本键
    public static Loaded Load(WorldMapEra era, out string error)
    {
        error = null;
        string path = PathOf(era);
        if (era?.map == null || path == null || !File.Exists(path))
        {
            error = "scenario_import_missing";
            return null;
        }
        if (!EarthTerrain.EnsureLoaded())
        {
            error = "world_map_data_missing";
            return null;
        }
        string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + era.map.mode + "|" + era.map.sea;
        if (_cache?.key == key) return _cache;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
            {
                error = "scenario_import_bad";
                return null;
            }
            int width = texture.width, height = texture.height;
            int shrink = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(width, height) / (float)MaxSide));
            int cols = Mathf.Max(64, width / shrink), rows = Mathf.Max(64, height / shrink);
            Color32[] pixels = texture.GetPixels32();
            var grid = new byte[cols * rows];
            bool byHeight = era.map.mode == "height";
            List<(Color32 color, byte code)> palette = byHeight ? null : Palette();
            float sea = Mathf.Clamp(era.map.sea, 0.02f, 0.98f);
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int sx = Mathf.Min(width - 1, x * shrink), sy = Mathf.Min(height - 1, y * shrink);
                Color32 color = pixels[sy * width + sx];
                grid[y * cols + x] = byHeight ? FromHeight(color, sea) : Nearest(palette, color);
            }
            _cache = new Loaded { grid = grid, cols = cols, rows = rows, frame = FrameOf(cols, rows), key = key };
            LogService.LogInfo($"[EmpireCraft][导入地图] {path}：{width}×{height} → {cols}×{rows}，{era.map.mode}");
            return _cache;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][导入地图] 读不出 {path}: {exception.Message}");
            error = "scenario_import_bad";
            return null;
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    // 还没放图时编辑器里显示的一片海
    public static Loaded Blank()
    {
        int deep = Math.Max(0, EarthTerrain.CodeOf("deep_ocean"));
        var grid = new byte[BlankWidth * BlankHeight];
        for (int i = 0; i < grid.Length; i++) grid[i] = (byte)deep;
        return new Loaded { grid = grid, cols = BlankWidth, rows = BlankHeight, frame = FrameOf(BlankWidth, BlankHeight), key = "blank" };
    }

    // 范围：经度 0 起、纬度 0 起(左下角)；三档大小按一像素合几格，宽按区块取整，高随宽的比例
    private static WorldMapFrame FrameOf(int cols, int rows)
    {
        var sizes = new Vector2Int[Scales.Length];
        for (int i = 0; i < Scales.Length; i++)
        {
            int x = Mathf.Clamp(Mathf.CeilToInt(cols * Scales[i] / 64f), MinChunks, MaxChunks);
            int y = Mathf.Clamp(Mathf.CeilToInt(rows * x / (float)cols), MinChunks, MaxChunks);
            sizes[i] = new Vector2Int(x, y);
        }
        return new WorldMapFrame { id = "imported", west = 0f, east = cols * Step, top = rows * Step, sizes = sizes };
    }

    private static List<(Color32 color, byte code)> Palette()
    {
        var list = new List<(Color32, byte)>();
        string[] types = EarthTerrain.Types;
        for (int i = 0; i < types.Length && i < 256; i++) list.Add((EarthTerrain.PreviewColorOf(types[i]), (byte)i));
        return list;
    }

    private static byte Nearest(List<(Color32 color, byte code)> palette, Color32 color)
    {
        int best = int.MaxValue;
        byte found = 0;
        foreach ((Color32 entry, byte code) in palette)
        {
            int dr = entry.r - color.r, dg = entry.g - color.g, db = entry.b - color.b;
            int distance = dr * dr + dg * dg + db * db;
            if (distance >= best) continue;
            best = distance;
            found = code;
        }
        return found;
    }

    // 灰度：海平面以下按深浅分深海、近海、浅滩；以上按高低分沙滩、平原、高原、丘陵、山地、雪丘、雪山
    private static byte FromHeight(Color32 color, float sea)
    {
        float value = (color.r * 0.299f + color.g * 0.587f + color.b * 0.114f) / 255f;
        string type;
        if (value < sea)
        {
            float depth = (sea - value) / sea;
            type = depth > 0.35f ? "deep_ocean" : depth > 0.08f ? "close_ocean" : "shallow_waters";
        }
        else
        {
            float rise = (value - sea) / Mathf.Max(0.02f, 1f - sea);
            type = rise < 0.04f ? "sand" : rise < 0.35f ? "soil_low:grass_low" : rise < 0.55f ? "soil_high:grass_high"
                : rise < 0.7f ? "hills" : rise < 0.85f ? "mountains" : rise < 0.92f ? "hills:snow_hills" : "mountains:snow_block";
        }
        return (byte)Math.Max(0, EarthTerrain.CodeOf(type));
    }

    // 颜色对照表(写进剧本文件夹)：每种地形一行，名与 RGB
    public static void WriteLegend(string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !EarthTerrain.EnsureLoaded()) return;
            var text = new StringBuilder();
            text.AppendLine(LM.Get("scenario_import_legend_head"));
            foreach (string type in EarthTerrain.Types)
            {
                Color32 color = EarthTerrain.PreviewColorOf(type);
                text.AppendLine($"{color.r},{color.g},{color.b}\t{type}");
            }
            File.WriteAllText(Path.Combine(folder, LegendFile), text.ToString());
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][导入地图] 写对照表失败: {exception.Message}");
        }
    }
}
