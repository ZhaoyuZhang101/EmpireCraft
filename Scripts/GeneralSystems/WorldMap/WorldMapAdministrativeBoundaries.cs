using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

public sealed class WorldMapAdministrativeBoundary
{
    public string era = "", polity = "", division = "", source = "";
    public List<List<List<float[]>>> polygons = new();
    // 来源图的年代与精度随数据保存；不把近似历史图当作现代测绘边界。
    public int year;
    public string precision = "approximate";
}

internal static class WorldMapAdministrativeBoundaries
{
    private sealed class FileData { public List<WorldMapAdministrativeBoundary> boundaries = new(); }
    private static List<WorldMapAdministrativeBoundary> _boundaries;

    public static List<WorldMapAdministrativeBoundary> For(string era, int year, string polity, string division)
    {
        if (_boundaries == null)
        {
            _boundaries = new();
            string path = Path.Combine(WorldMapData.Folder, "administrative_boundaries.json");
            try
            {
                if (File.Exists(path))
                    _boundaries = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path))?.boundaries ?? new();
            }
            catch (Exception e) { LogService.LogWarning($"[EmpireCraft][行政辖境] 历史面数据读不出来: {e.Message}"); }
        }
        return _boundaries.Where(item => item != null && item.era == era && item.year == year && item.polity == polity &&
            item.division == division && item.polygons is { Count: > 0 }).ToList();
    }
}
