using System.Collections.Generic;
using EmpireCraft.Scripts.GamePatches;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 城市规划按格(用户 2026-10-16 选定"规划决定城市画法、按格刷有笔刷大小、加刷街道广场城墙、自动规划按时代"，
// 演示版看过说"可以，移植进游戏")：每格可刷一种用途，另可画一种设施，随存档保存(SaveData.tile_plans)。
//   · 用途：住宅、商业、工业、军事、官署、寺庙、绿地、农田、保护；设施：街道、广场、城墙(笔刷见 PlanBrushPowers)；
//   · 只刷在城市的领地上，格所在的区块归哪座城，规划就算哪座城的；
//   · 画法(纯净地图的城市精细层与平面图，见 PureMapPatch)：刷了城区用途的格并入城区，宅基地照刷的用途盖房；
//     画了城墙就照画的修，不再沿城边自动修墙、挖护城河；城外刷的农田、绿地、保护区画成田、草坪、树林；
//     近代新城区一块地按刷得最多的用途定；
//   · 玩法：一块地(区块)里同一类用途刷满一半，这块地就划为相应的功能区(见 ZonePlanSystem，限制能盖的原版建筑)；
//     一块地里的规划全擦掉，取消这块地的功能区。
public enum PlanUse : byte
{
    None = 0,
    Residential = 1,
    Commerce = 2,
    Industry = 3,
    Military = 4,
    Civic = 5,
    Temple = 6,
    Park = 7,
    Farm = 8,
    Keep = 9
}

public enum PlanFeature : byte
{
    None = 0,
    Street = 1,
    Plaza = 2,
    Wall = 3
}

public static class TilePlanSystem
{
    // 格编号 → 低四位用途、高四位设施
    private static readonly Dictionary<int, byte> Cells = new();
    // 各城规划改动的次数(算进平面图指纹，见 PureMapPatch.Stamp)
    private static readonly Dictionary<long, int> Revisions = new();

    // 各城规划的汇总：画没画城墙、哪些区块有规划；按改动次数与区块数缓存
    private sealed class Summary
    {
        public int revision = -1, zones = -1;
        public bool walls;
        public readonly HashSet<int> planned = new();
    }

    private static readonly Dictionary<long, Summary> Summaries = new();

    // 刷过还没结算的城(重画、同步功能区)：至多每 FlushSeconds 秒结算一次，按住拖动时不必每帧重画整座城
    private const float FlushSeconds = 0.2f;
    private static readonly Dictionary<long, (City city, float at)> Pending = new();
    private static readonly Dictionary<long, float> LastFlush = new();
    private static readonly HashSet<TileZone> TouchedZones = new(), ErasedZones = new();

    // 一块地里同一类用途刷满这么多格(半块)，就划为功能区
    private const int ZoneShare = 32;

    public static bool Any => Cells.Count > 0;

    public static void ResetWorldState()
    {
        Cells.Clear();
        Revisions.Clear();
        Summaries.Clear();
        Pending.Clear();
        LastFlush.Clear();
        TouchedZones.Clear();
        ErasedZones.Clear();
    }

    public static void Load(Dictionary<int, int> saved)
    {
        ResetWorldState();
        if (saved == null) return;
        foreach (KeyValuePair<int, int> pair in saved)
            if ((pair.Value & 0xFF) != 0) Cells[pair.Key] = (byte)pair.Value;
    }

    public static Dictionary<int, int> Save()
    {
        var result = new Dictionary<int, int>(Cells.Count);
        foreach (KeyValuePair<int, byte> pair in Cells) result[pair.Key] = pair.Value;
        return result;
    }

    #region 查询

    public static PlanUse UseAt(WorldTile tile) =>
        Cells.Count > 0 && tile?.data != null && Cells.TryGetValue(tile.data.tile_id, out byte cell) ? (PlanUse)(cell & 15) : PlanUse.None;

    public static PlanFeature FeatureAt(WorldTile tile) =>
        Cells.Count > 0 && tile?.data != null && Cells.TryGetValue(tile.data.tile_id, out byte cell) ? (PlanFeature)(cell >> 4) : PlanFeature.None;

    // 城区用途(刷到空地上就并入城区)
    public static bool IsUrban(PlanUse use) => use is PlanUse.Residential or PlanUse.Commerce or PlanUse.Industry or
        PlanUse.Military or PlanUse.Civic or PlanUse.Temple;

    // 这一格刷了城区用途，而且归这座城(设施不算：城外画的街、广场照画，城墙不跟着它们绕出去)
    public static bool UrbanAt(WorldTile tile, City city) =>
        Cells.Count > 0 && tile?.data != null && city != null && tile.zone?.city == city &&
        Cells.TryGetValue(tile.data.tile_id, out byte cell) && IsUrban((PlanUse)(cell & 15));

    public static int Revision(City city) =>
        city?.data != null && Revisions.TryGetValue(city.data.id, out int revision) ? revision : 0;

    private static Summary SummaryOf(City city)
    {
        if (city?.data == null || city.zones == null) return null;
        if (!Summaries.TryGetValue(city.data.id, out Summary summary)) Summaries[city.data.id] = summary = new Summary();
        int revision = Revision(city);
        if (summary.revision == revision && summary.zones == city.zones.Count) return summary;
        summary.revision = revision;
        summary.zones = city.zones.Count;
        summary.walls = false;
        summary.planned.Clear();
        if (Cells.Count == 0) return summary;
        foreach (TileZone zone in city.zones)
        {
            if (zone?.tiles == null) continue;
            foreach (WorldTile tile in zone.tiles)
            {
                if (tile?.data == null || !Cells.TryGetValue(tile.data.tile_id, out byte cell)) continue;
                summary.planned.Add(zone.id);
                if ((PlanFeature)(cell >> 4) == PlanFeature.Wall) summary.walls = true;
            }
        }
        return summary;
    }

    // 这座城画了城墙(照画的修，不再沿城边自动修)
    public static bool HasWalls(City city) => Cells.Count > 0 && SummaryOf(city)?.walls == true;

    // 这块地里有没有刷过规划
    public static bool Planned(City city, TileZone zone) =>
        Cells.Count > 0 && zone != null && SummaryOf(city)?.planned.Contains(zone.id) == true;

    // 一块地里刷得最多的用途(至少 min 格)
    public static PlanUse Majority(TileZone zone, int min)
    {
        if (Cells.Count == 0 || zone?.tiles == null) return PlanUse.None;
        int[] counts = null;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile?.data == null || !Cells.TryGetValue(tile.data.tile_id, out byte cell) || (cell & 15) == 0) continue;
            counts ??= new int[16];
            counts[cell & 15]++;
        }
        if (counts == null) return PlanUse.None;
        int best = 0;
        for (int i = 1; i < counts.Length; i++)
            if (counts[i] > counts[best]) best = i;
        return counts[best] >= min ? (PlanUse)best : PlanUse.None;
    }

    // 规划用途对应的功能区(限制能盖的原版建筑)：官署、寺庙算居住区(那里许建神庙、会堂、图书馆)，绿地算保护区
    public static ZoneUse ZoneUseOf(PlanUse use) => use switch
    {
        PlanUse.Residential or PlanUse.Civic or PlanUse.Temple => ZoneUse.Residential,
        PlanUse.Commerce => ZoneUse.Commerce,
        PlanUse.Industry => ZoneUse.Industry,
        PlanUse.Military => ZoneUse.Military,
        PlanUse.Park or PlanUse.Keep => ZoneUse.Reserve,
        PlanUse.Farm => ZoneUse.Farm,
        _ => ZoneUse.None
    };

    // 规划图层的颜色(选中规划笔刷时，见 PureMapPatch.ShadeTerrain)
    public static Color32 ColorOf(PlanUse use) => use switch
    {
        PlanUse.Residential => new Color32(236, 196, 84, 255),
        PlanUse.Commerce => new Color32(232, 96, 70, 255),
        PlanUse.Industry => new Color32(150, 112, 196, 255),
        PlanUse.Military => new Color32(118, 138, 64, 255),
        PlanUse.Civic => new Color32(70, 132, 214, 255),
        PlanUse.Temple => new Color32(196, 120, 204, 255),
        PlanUse.Park => new Color32(88, 186, 92, 255),
        PlanUse.Farm => new Color32(206, 222, 112, 255),
        PlanUse.Keep => new Color32(46, 104, 62, 255),
        _ => new Color32(160, 160, 160, 255)
    };

    public static Color32 ColorOf(PlanFeature feature) => feature switch
    {
        PlanFeature.Street => new Color32(250, 250, 250, 255),
        PlanFeature.Plaza => new Color32(214, 206, 188, 255),
        PlanFeature.Wall => new Color32(110, 100, 92, 255),
        _ => new Color32(160, 160, 160, 255)
    };

    #endregion

    #region 刷

    // 刷一格：use / feature 为 null 不改；erase 去掉这一格的规划。返回这一格改了没有(不归哪座城、水面不刷)
    public static bool Paint(WorldTile tile, PlanUse? use, PlanFeature? feature, bool erase)
    {
        City city = tile?.zone?.city;
        if (tile?.data == null || tile.Type == null || tile.Type.liquid || city?.data == null || city.isRekt()) return false;
        int id = tile.data.tile_id;
        Cells.TryGetValue(id, out byte old);
        byte cell = old;
        if (erase) cell = 0;
        else
        {
            if (use != null) cell = (byte)(cell & 0xF0 | (byte)use.Value);
            if (feature != null) cell = (byte)(cell & 0x0F | (byte)feature.Value << 4);
        }
        if (cell == old) return false;
        if (cell == 0) Cells.Remove(id);
        else Cells[id] = cell;
        TouchedZones.Add(tile.zone);
        if (erase) ErasedZones.Add(tile.zone);
        if (!Pending.ContainsKey(city.data.id)) Pending[city.data.id] = (city, Time.unscaledTime);
        return true;
    }

    // 每帧：刷过的城隔一会儿结算一次(规划改动次数 +1、重画、同步功能区)
    public static void Tick()
    {
        if (Pending.Count == 0) return;
        float now = Time.unscaledTime;
        List<long> due = null;
        foreach (KeyValuePair<long, (City city, float at)> pair in Pending)
        {
            if (LastFlush.TryGetValue(pair.Key, out float last) && now - last < FlushSeconds && now >= last) continue;
            (due ??= new List<long>()).Add(pair.Key);
        }
        if (due == null) return;
        foreach (long id in due)
        {
            City city = Pending[id].city;
            Pending.Remove(id);
            LastFlush[id] = now;
            if (city?.data == null || city.isRekt()) continue;
            Revisions[id] = Revision(city) + 1;
            SyncZones(city);
            PureMapPatch.Redraw(city);
        }
        if (Pending.Count == 0)
        {
            TouchedZones.Clear();
            ErasedZones.Clear();
        }
        if (LastFlush.Count > 256) LastFlush.Clear();
    }

    // 刷过的地块按规划划定功能区：同一类用途刷满半块就划；擦得一格不剩就取消
    private static void SyncZones(City city)
    {
        var done = new List<TileZone>();
        foreach (TileZone zone in TouchedZones)
        {
            if (zone?.city != city) continue;
            done.Add(zone);
            if (ZonePlanSystem.IsCenter(city, zone)) continue;
            ZoneUse current = ZonePlanSystem.Get(city, zone);
            PlanUse majority = Majority(zone, ZoneShare);
            ZoneUse wanted = ZoneUseOf(majority);
            if (wanted == ZoneUse.None)
            {
                if (ErasedZones.Contains(zone) && current != ZoneUse.None && Majority(zone, 1) == PlanUse.None)
                    ZonePlanSystem.Set(city, zone, ZoneUse.None);
                continue;
            }
            if (wanted == current || !ZonePlanSystem.Set(city, zone, wanted)) continue;
            // 划成农田：当即开出第一批田(以前点"规划农田"也是这样)
            if (wanted == ZoneUse.Farm && CityPopulationSystem.AbstractPopulationEnabled)
                FarmlandSystem.CultivateZone(city, zone, 64);
        }
        foreach (TileZone zone in done)
        {
            TouchedZones.Remove(zone);
            ErasedZones.Remove(zone);
        }
    }

    #endregion
}
