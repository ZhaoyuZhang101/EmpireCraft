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
    // 共用省界(polity 为 "*")属于哪一片：china 中国现行省级区划、ussr 苏联 1983 年州一级(见 Tools/WorldMap/modern_administration_source.py)
    public string realm = "";
}

internal static class WorldMapAdministrativeBoundaries
{
    private sealed class FileData { public List<WorldMapAdministrativeBoundary> boundaries = new(); }
    private static List<WorldMapAdministrativeBoundary> _boundaries;

    public static List<WorldMapAdministrativeBoundary> For(WorldMapEra era, string polity, string division)
    {
        if (era == null) return new();
        List<WorldMapAdministrativeBoundary> own = For(era.id, era.year, polity, division);
        if (own.Count > 0) return own;
        // 复制剧本沿用默认地块数据，但仍严格匹配年份；自定范围优先，不跨时代套省界。
        string basis = WorldMapData.BaseOf(era);
        return string.IsNullOrEmpty(basis) || basis == era.id ? own : For(basis, era.year, polity, division);
    }

    // 共用省界(不分哪一国，见 WorldMapScenario.ConformSharedBorders)：本剧本的，没有就照所本的剧本
    public static List<WorldMapAdministrativeBoundary> Shared(WorldMapEra era)
    {
        if (era == null) return new();
        For(era.id, era.year, "", "");
        List<WorldMapAdministrativeBoundary> own = _boundaries.Where(item => item != null && item.era == era.id && item.year == era.year &&
            item.polity == "*" && item.polygons is { Count: > 0 }).ToList();
        if (own.Count > 0) return own;
        string basis = WorldMapData.BaseOf(era);
        return string.IsNullOrEmpty(basis) || basis == era.id ? own
            : _boundaries.Where(item => item != null && item.era == basis && item.year == era.year && item.polity == "*" &&
                                        item.polygons is { Count: > 0 }).ToList();
    }

    public static List<WorldMapAdministrativeBoundary> For(string era, int year, string polity, string division)
    {
        if (_boundaries == null)
        {
            _boundaries = new();
            foreach (string file in new[] { "administrative_boundaries.json", "administrative_boundaries_ming.json", "administrative_boundaries_modern.json" })
                try
                {
                    string path = Path.Combine(WorldMapData.Folder, file);
                    if (File.Exists(path))
                        _boundaries.AddRange(JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path))?.boundaries ?? new());
                }
                catch (Exception e) { LogService.LogWarning($"[EmpireCraft][行政辖境] {file} 读不出来: {e.Message}"); }
        }
        return _boundaries.Where(item => item != null && item.era == era && item.year == year && item.polity == polity &&
            item.division == division && item.polygons is { Count: > 0 }).ToList();
    }
}
