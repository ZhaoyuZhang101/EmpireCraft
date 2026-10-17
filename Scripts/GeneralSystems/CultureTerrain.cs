using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

// 地利(用户 2026-10-17："可以给地形一点加成，比如平原农耕文明难以被异族转化，游牧在沙漠难以被异族转化等")：
// 文化配置写本文化的故土地形(setting.home_terrain：Plains 平原、River 水网、Steppe 草原(稀树草原)、Desert 沙漠、
// Forest 森林、Hills 丘陵、Mountain 山地、Jungle 丛林、Swamp 沼泽、Snow 雪原)；城的地形取它各地块地形(见 TerrainSystem)
// 里最多的一种，平原地块中心是稀树草原的算草原。本文化在故土地形的城里难被异族(不同天下标识)转化——
//   · 城的文化影响力被异族夺走的速度减半(征服后的占领压力、人口迁徙、教化，见 CultureService.ApplyPopulationPressure)；
//   · 异族征服时的文化冲击减半(见 CultureService.ApplyForeignOccupationCulture)；
//   · 背景人口被异族主流文化同化的速度减半(见 CityPopulationSystem.AssimilateBackground)。
public static class CultureTerrain
{
    public const float Resistance = 0.5f;
    public const string Steppe = "Steppe";
    private static readonly Dictionary<long, (string terrain, double at)> Cache = new();
    private static object _world;

    // 城的地形，一年重判一次
    public static string Of(City city)
    {
        if (city?.data == null || city.isRekt() || city.zones == null || city.zones.Count == 0 || World.world == null) return "";
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Cache.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (Cache.TryGetValue(city.id, out var hit) && now >= hit.at && Date.getYearsSince(hit.at) < 1) return hit.terrain;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TileZone zone in city.zones)
        {
            if (zone == null) continue;
            FrontTerrain front = TerrainSystem.Of(zone);
            string terrain = front == FrontTerrain.Plains && zone.centerTile?.Type?.biome_id == "biome_savanna"
                ? Steppe
                : front.ToString();
            counts[terrain] = (counts.TryGetValue(terrain, out int count) ? count : 0) + 1;
        }
        string result = counts.Count == 0 ? "" : counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
        Cache[city.id] = (result, now);
        return result;
    }

    // culture 在这座城算不算故土
    public static bool AtHome(string culture, City city)
    {
        if (!CultureService.IsValidCulture(culture) || !OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) ||
            setting?.home_terrain == null || setting.home_terrain.Count == 0) return false;
        string terrain = Of(city);
        return terrain != "" && setting.home_terrain.Any(home => string.Equals(home?.Trim(), terrain, StringComparison.OrdinalIgnoreCase));
    }

    // 这座城里 native 的影响力、人口要被 rising 夺去时的快慢：rising 是异族、native 在故土为 Resistance，否则 1
    public static float Factor(City city, string native, string rising) =>
        CultureService.IsValidCulture(native) && CultureService.IsValidCulture(rising) && !CultureService.SameKin(native, rising) &&
        AtHome(native, city)
            ? Resistance
            : 1f;
}
