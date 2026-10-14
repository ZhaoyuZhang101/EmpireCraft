using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市肌理(纯净地图)：城市从县城慢慢长成都市。画法主次分明：
//   · 主干：环路与放射干道(深色)，铁路一条、车站一处；城外的路交给路网图层；
//   · 次级：街道沿地块走——每块地(8×8)四边是街，正中一纵一横是小巷，切出四个街区，与城市规划的区块一一对应；
//   · 街区：一块地一种用途、一种样子，整块地一起开发，不东一点西一点；
//   · 一切都淡淡地画在国色上，城是本国颜色里浅一些、带纹理的一片。
// 每座城有自己的随机种子(默认是城的编号，「另起格局」神力可以换一个)：干道几条、朝哪、城区往哪边蔓延、
// 市中心偏在哪、工业区在哪一侧、各方向开发先后、村镇落在哪都随它。
// 时代：
//   · 古代：城区照原来的画法(城墙、里坊、街格，见 PureMapPatch.Plan)；城外几处整齐的小村，少数长成乡镇；
//   · 工业化(本文化研究出工业化后逐年推进)：拆城墙，修放射干道、铁路车站，工业区起厂房；
//   · 电气：城区往外长，住宅区一块块建起——内圈板楼(西欧、罗马、维京是围合街坊)，外圈低层住宅；城大了修二环；
//   · 现代城市规划：市中心与商业区一块块起塔楼(大小、玻璃或石材各异，带投影)，修三环。
// 自然发展：每座城快慢不一(城大的、都城快些)；离城心近的地块先开发、远的后开发，各方向有先有后，
// 所以城区是从里往外一圈圈长出去的；城区外缘随种子起伏。
// 规划用途(城市规划神力)决定一块地画什么：工业区厂房，商业区市场(现代是塔楼)，居住区住宅，军事区营区，
// 保护区城里是公园、城外是原野，农耕区照旧是农田。规划过的地块先开发，而且不受城区范围限制。
public static class UrbanFabric
{
    public sealed class Geometry
    {
        public long Seed;
        public int Cx, Cy;
        // 0 古代；0~1 工业化；1~2 电气；2~3 现代城市规划(各段按研究出来的年数推进，乘本城的快慢)
        public float Progress;
        public float OldRadius, MaxEdge;
        public readonly float[] Edge = new float[EdgeBins];
        public float[] Rays = new float[0];
        public int Sectors;
        public float SectorOffset;
        public float[] SectorPace = new float[0];
        public float IndustryDir, RailDir;
        public int CbdX, CbdY;
        public bool Perimeter;
        public readonly List<(float x, float y, int size, bool town)> Villages = new();
        public City City;
        public HashSet<int> Built, Landmark;
        // 各地块画成什么、开发了没有(按地块算一次)
        public readonly Dictionary<int, (Kind kind, bool developed)> Zones = new();
        public bool WallsDown => Progress >= 0.2f;
        public bool Modern => Progress > 0f;
    }

    public enum Kind { Housing, Tower, Industry, Market, Military, Park, Farm }

    private const int EdgeBins = 48, MaxSeededRays = 6;
    private const float TwoPi = Mathf.PI * 2f;
    private const float Strength = 0.5f;

    private static readonly Color32 Asphalt = new(80, 84, 94, 255), BridgeDeck = new(150, 150, 156, 255),
        Local = new(176, 178, 180, 255), Rail = new(66, 58, 54, 255), Tie = new(165, 145, 118, 255),
        Station = new(180, 90, 70, 255), Park = new(96, 162, 86, 255), Tree = new(56, 116, 58, 255),
        Shadow = new(96, 98, 104, 255), Yard = new(176, 190, 132, 255), Market = new(196, 160, 90, 255),
        TownStreet = new(226, 220, 206, 255), Chimney = new(60, 55, 55, 255), Parade = new(186, 176, 150, 255),
        Barrack = new(120, 130, 100, 255), White = new(255, 255, 255, 255);
    private static readonly Color32[] Glass =
        { new(92, 130, 170, 255), new(120, 160, 196, 255), new(70, 96, 128, 255), new(150, 182, 210, 255) };
    private static readonly Color32[] Concrete =
        { new(214, 210, 200, 255), new(190, 186, 178, 255), new(226, 222, 214, 255), new(170, 168, 164, 255) };
    private static readonly Color32[] Slab =
        { new(200, 196, 188, 255), new(178, 172, 164, 255), new(220, 214, 200, 255), new(190, 170, 150, 255) };
    private static readonly Color32[] HouseRoof =
        { new(176, 82, 64, 255), new(150, 92, 70, 255), new(120, 110, 104, 255), new(190, 120, 80, 255) };
    private static readonly Color32[] Factory =
        { new(206, 210, 214, 255), new(172, 180, 190, 255), new(110, 140, 170, 255), new(200, 200, 196, 255) };

    #region 种子

    // 本城的种子：没换过就是城的编号
    public static long SeedOf(City city)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        return data.urban_seed != 0L ? data.urban_seed : city.data.id;
    }

    // 换一个随机种子(「另起格局」神力)；旧城范围是历史，不变
    public static void Reseed(City city)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        long seed;
        do seed = ((long)Random.Range(1, int.MaxValue) << 20) ^ Random.Range(0, 1 << 20);
        while (seed == 0L || seed == data.urban_seed);
        data.urban_seed = seed;
    }

    #endregion

    #region 随机与上色

    private static float H(long a, long b = 0, long c = 0, long d = 0)
    {
        unchecked
        {
            ulong x = 0x9E3779B97F4A7C15UL;
            x = (x ^ (ulong)a) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (ulong)b ^ (x >> 31)) * 0x94D049BB133111EBUL;
            x = (x ^ (ulong)c ^ (x >> 29)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (ulong)d ^ (x >> 32)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (x >> 40) / 16777216f;
        }
    }

    private static float Noise(float x, float y, float cell, long seed)
    {
        float fx = x / cell, fy = y / cell;
        int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
        float sx = fx - ix, sy = fy - iy;
        sx = sx * sx * (3f - 2f * sx);
        sy = sy * sy * (3f - 2f * sy);
        float a = H(seed, ix, iy), b = H(seed, ix + 1, iy), c = H(seed, ix, iy + 1), d = H(seed, ix + 1, iy + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
    }

    private static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

    // 画在国色上：国色始终占上风
    private static Color32 Paint(Color32 color, Color32 over, float t) => Color32.Lerp(color, over, t * Strength);

    // 空地：只把国色提亮
    private static Color32 Lot(Color32 color) => Color32.Lerp(color, White, 0.16f);

    #endregion

    #region 每座城的格局(随平面图每五秒重算)

    public static Geometry Build(City city, HashSet<int> built, HashSet<int> landmark, string family, string culture)
    {
        WorldTile center = city?.getTile();
        if (center == null || city.zones == null) return null;
        long seed = SeedOf(city);
        var g = new Geometry
        {
            Seed = seed, Cx = center.x, Cy = center.y, City = city, Built = built, Landmark = landmark,
            Perimeter = family is "western" or "roma" or "viking"
        };
        // 发展快慢：种子定一个底(0.7~1.3 倍)，城大的、都城快些
        float pace = (0.7f + 0.6f * H(seed, 50)) * (0.85f + 0.3f * Mathf.Clamp01(built.Count / 12f)) *
                     (city.isCapitalCity() ? 1.15f : 1f);
        g.Progress = ProgressOf(culture, pace);
        // 城区外缘：各方向上建成地块的最远处，空的方向取两边，再平滑
        var raw = new float[EdgeBins];
        foreach (TileZone zone in city.zones)
        {
            if (zone == null || !built.Contains(zone.id)) continue;
            float dx = zone.x * Config.CITY_ZONE_SIZE + 4f - g.Cx, dy = zone.y * Config.CITY_ZONE_SIZE + 4f - g.Cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy) + 5f;
            int bin = Bin(Angle(dx, dy));
            for (int k = -2; k <= 2; k++)
            {
                int b = (bin + k + EdgeBins) % EdgeBins;
                raw[b] = Mathf.Max(raw[b], d - Mathf.Abs(k) * 1.5f);
            }
        }
        float sum = 0f;
        var smooth = new float[EdgeBins];
        for (int i = 0; i < EdgeBins; i++)
        {
            smooth[i] = Mathf.Max(5f, (raw[(i + EdgeBins - 1) % EdgeBins] + raw[i] * 2f + raw[(i + 1) % EdgeBins]) / 4f);
            sum += smooth[i];
        }
        // 旧城：工业化那时的城区(记在城里，之后不再变)
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (g.Modern)
        {
            if (data.urban_old_radius <= 0f) data.urban_old_radius = Mathf.Max(5f, sum / EdgeBins * 0.8f);
            g.OldRadius = data.urban_old_radius;
        }
        else g.OldRadius = sum / EdgeBins;
        // 蔓延：工业化以后城区往外长，种子定哪几个方向长得远(沿一圈连续起伏，没有接缝)
        for (int i = 0; i < EdgeBins; i++)
        {
            float a = (i + 0.5f) / EdgeBins * TwoPi;
            float lobe = Noise(10f + 3f * Mathf.Cos(a), 10f + 3f * Mathf.Sin(a), 1.6f, seed + 3);
            float jitter = 0.92f + 0.16f * Noise(20f + 6f * Mathf.Cos(a), 20f + 6f * Mathf.Sin(a), 1.3f, seed + 4);
            g.Edge[i] = smooth[i] * jitter * (1f + Mathf.Min(g.Progress, 3f) * 0.12f * (0.3f + 1.4f * lobe));
            g.MaxEdge = Mathf.Max(g.MaxEdge, g.Edge[i]);
        }
        // 干道：按种子定四到六条，再补上实际通往邻城、方向没重复的路
        var rays = new List<float>();
        int want = 4 + (int)(H(seed, 1) * 3);
        for (int attempt = 0; rays.Count < Mathf.Min(want, MaxSeededRays) && attempt < 32; attempt++)
        {
            float a = H(seed, 2, attempt) * TwoPi;
            if (!Near(rays, a, 360f / (MaxSeededRays + 2))) rays.Add(a);
        }
        if (rays.Count > 0) g.RailDir = rays[(int)(H(seed, 4) * rays.Count) % rays.Count] + 0.25f;
        foreach (KeyValuePair<(long, long), RoadLink> pair in RoadBuildingSystem.All)
        {
            long other = pair.Key.Item1 == city.data.id ? pair.Key.Item2 : pair.Key.Item2 == city.data.id ? pair.Key.Item1 : -1L;
            WorldTile there = other < 0 ? null : World.world.cities.get(other)?.getTile();
            if (there == null) continue;
            float a = Angle(there.x - g.Cx, there.y - g.Cy);
            if (!Near(rays, a, 30f)) rays.Add(a);
        }
        rays.Sort();
        g.Rays = rays.ToArray();
        // 各方向开发先后
        g.Sectors = 4 + (int)(H(seed, 7) * 4);
        g.SectorOffset = H(seed, 8) * TwoPi;
        g.SectorPace = new float[g.Sectors];
        for (int i = 0; i < g.Sectors; i++) g.SectorPace[i] = (H(seed, i, 52) - 0.5f) * 0.4f;
        g.IndustryDir = H(seed, 3) * TwoPi;
        g.CbdX = g.Cx + (int)((H(seed, 5) - 0.5f) * 6f);
        g.CbdY = g.Cy + (int)((H(seed, 6) - 0.5f) * 6f);
        PlaceVillages(g);
        return g;
    }

    private static bool Near(List<float> rays, float a, float degrees) =>
        rays.Exists(ray => Mathf.Abs(Mathf.DeltaAngle(ray * Mathf.Rad2Deg, a * Mathf.Rad2Deg)) < degrees);

    // 时代进度：工业化、电气、现代城市规划各推进一段(研究出来后分别约 25、25、20 年走完，乘本城快慢)
    private static float ProgressOf(string culture, float pace)
    {
        if (string.IsNullOrEmpty(culture)) return 0f;
        var state = TechnologySystem.GetState(culture);
        float Years(string tech) =>
            TechnologySystem.HasTech(culture, tech) && state?.timestamps != null &&
            state.timestamps.TryGetValue("tech:" + tech, out double at)
                ? Mathf.Max(0f, Date.getYearsSince(at)) * pace : -1f;
        float industrial = Years("industrialization");
        if (industrial < 0f) return 0f;
        float electric = Years("electricity"), planning = Years("modern_urban_planning");
        if (planning >= 0f) return 2f + Mathf.Clamp01(planning / 20f);
        if (electric >= 0f) return 1f + Mathf.Clamp01(electric / 25f);
        return Mathf.Max(0.01f, Mathf.Clamp01(industrial / 25f));
    }

    // 村镇：几处，多在干道方向上，离城心十二到三十五格，只落在本城没建成的平地上；少数长成乡镇
    private static void PlaceVillages(Geometry g)
    {
        int count = 1 + (int)(H(g.Seed, 30) * 3) + g.City.zones.Count / 20;
        for (int attempt = 0; attempt < count * 4 && g.Villages.Count < count; attempt++)
        {
            float a = g.Rays.Length > 0 && H(g.Seed, attempt, 45) < 0.75f
                ? g.Rays[(int)(H(g.Seed, attempt, 40) * g.Rays.Length) % g.Rays.Length] + (H(g.Seed, attempt, 42) - 0.5f) * 0.3f
                : H(g.Seed, attempt, 46) * TwoPi;
            float dist = 12f + H(g.Seed, attempt, 41) * 23f;
            float vx = Mathf.Round(g.Cx + dist * Mathf.Cos(a)), vy = Mathf.Round(g.Cy + dist * Mathf.Sin(a));
            WorldTile tile = World.world.GetTile((int)vx, (int)vy);
            if (tile?.zone == null || tile.zone.city != g.City || g.Built.Contains(tile.zone.id) ||
                tile.Type == null || tile.Type.liquid || Rough(tile)) continue;
            if (g.Villages.Exists(v => Mathf.Abs(v.x - vx) + Mathf.Abs(v.y - vy) < 10f)) continue;
            bool town = H(g.Seed, attempt, 43) < 0.1f + 0.1f * g.Progress;
            g.Villages.Add((vx, vy, town ? 3 : 2, town));
        }
    }

    #endregion

    #region 每格

    private static float Angle(float dx, float dy)
    {
        float a = Mathf.Atan2(dy, dx);
        return a < 0f ? a + TwoPi : a;
    }

    private static int Bin(float angle) => Mathf.Clamp((int)(angle / TwoPi * EdgeBins), 0, EdgeBins - 1);

    private static float EdgeAt(Geometry g, float angle)
    {
        float f = angle / TwoPi * EdgeBins - 0.5f;
        if (f < 0f) f += EdgeBins;
        int a = Mathf.FloorToInt(f) % EdgeBins, b = (a + 1) % EdgeBins;
        return Mathf.Lerp(g.Edge[a], g.Edge[b], f - Mathf.Floor(f));
    }

    // 干道略带弯
    private static float RayAt(Geometry g, int k, float r) => g.Rays[k] + 0.08f * Mathf.Sin(r * 0.07f + k * 1.7f + g.Seed % 7);

    private static float RayDistance(Geometry g, float angle, float r)
    {
        float best = float.MaxValue;
        for (int k = 0; k < g.Rays.Length; k++)
            best = Mathf.Min(best, Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, RayAt(g, k, r) * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r);
        return best;
    }

    private static int SectorOf(Geometry g, float angle)
    {
        float a = angle - g.SectorOffset;
        if (a < 0f) a += TwoPi;
        return Mathf.Clamp((int)(a / TwoPi * g.Sectors), 0, g.Sectors - 1);
    }

    // 环路：二环(城大了)、三环(现代城市规划后期)；旧城墙拆了不另画环路
    private static bool OnRing(Geometry g, float r, float edge)
    {
        if (g.Progress >= 1.4f && g.MaxEdge > 18f && Mathf.Abs(r - edge * 0.62f) < 0.6f && edge * 0.62f > g.OldRadius + 3f)
            return true;
        return g.Progress >= 2.5f && Mathf.Abs(r - edge * 0.9f) < 0.6f;
    }

    // 主干道(环路、放射干道)
    public static bool Primary(Geometry g, WorldTile tile)
    {
        if (g == null || !g.Modern || tile == null) return false;
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy), angle = Angle(dx, dy), edge = EdgeAt(g, angle);
        return OnRing(g, r, edge) || r > g.OldRadius - 1f && r < edge + 1f && RayDistance(g, angle, r) < 0.6f;
    }

    // 山地、岩地：不起楼，留成原样
    private static bool Rough(WorldTile tile) => tile.Type != null && (tile.Type.mountains || tile.Type.rocks || tile.Type.block);

    // 河上过桥：干道、环路跨水处(ShadeTerrain 的水面分支调用)
    public static bool Bridge(Geometry g, WorldTile tile) => Primary(g, tile);

    public static Color32 BridgeColor => BridgeDeck;

    // 在城区里(规划视图画城市轮廓用)：建成的地块，或开发了的地块落在城区范围里的部分
    public static bool Inside(Geometry g, WorldTile tile)
    {
        if (g == null || tile?.zone == null) return false;
        if (g.Built.Contains(tile.zone.id)) return true;
        if (!g.Modern) return false;
        (Kind kind, bool developed) = ZoneOf(g, tile.zone);
        return developed && kind != Kind.Farm && (InEdge(g, tile) || ZonePlanSystem.Get(g.City, tile.zone) != ZoneUse.None);
    }

    private static bool InEdge(Geometry g, WorldTile tile)
    {
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy, edge = EdgeAt(g, Angle(dx, dy));
        return dx * dx + dy * dy < edge * edge;
    }

    // 一块地画成什么、开发了没有：规划用途优先；没规划的，离市中心近的现代是塔楼，工业那一侧是厂房，其余住宅。
    // 开发门槛 = 用途的起点 + 离城心越远越晚 + 各方向先后 + 一点随机；规划过的早一些
    private static (Kind kind, bool developed) ZoneOf(Geometry g, TileZone zone)
    {
        if (g.Zones.TryGetValue(zone.id, out var cached)) return cached;
        ZoneUse use = ZonePlanSystem.Get(g.City, zone);
        float zx = zone.x * Config.CITY_ZONE_SIZE + 4f, zy = zone.y * Config.CITY_ZONE_SIZE + 4f;
        float dx = zx - g.Cx, dy = zy - g.Cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy), angle = Angle(dx, dy), edge = EdgeAt(g, angle);
        float cbdX = zx - g.CbdX, cbdY = zy - g.CbdY;
        bool cbd = Mathf.Sqrt(cbdX * cbdX + cbdY * cbdY) < Mathf.Max(8f, edge * 0.36f);
        bool industrySide = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, g.IndustryDir * Mathf.Rad2Deg)) < 35f &&
                            r > g.OldRadius + 3f;
        Kind kind = use switch
        {
            ZoneUse.Industry => Kind.Industry,
            ZoneUse.Commerce => g.Progress >= 2f ? Kind.Tower : Kind.Market,
            ZoneUse.Military => Kind.Military,
            ZoneUse.Reserve => Kind.Park,
            ZoneUse.Farm => Kind.Farm,
            ZoneUse.Residential => g.Progress >= 2f && cbd ? Kind.Tower : Kind.Housing,
            _ => g.Progress >= 2f && cbd ? Kind.Tower : industrySide ? Kind.Industry : Kind.Housing
        };
        float start = kind switch
        {
            Kind.Industry => 0.2f,
            Kind.Tower => 2f,
            Kind.Park => -9f,
            Kind.Farm => 99f,
            _ => 0.9f
        };
        float need = start + 0.6f * Mathf.Min(1.2f, r / Mathf.Max(1f, edge)) + g.SectorPace[SectorOf(g, angle)] +
                     0.1f * H(g.Seed, zone.id, 7) - (use != ZoneUse.None ? 0.3f : 0f);
        var result = (kind, g.Modern && g.Progress >= need);
        g.Zones[zone.id] = result;
        return result;
    }

    // 这一格画什么；返回 false 时照原来的画法(古代城区、还没开发的建成地块、都城地标、农田、山地)
    public static bool Tile(WorldTile tile, Color32 color, Geometry g, bool built, ZoneUse use, out Color32 result)
    {
        result = color;
        if (g == null || tile?.zone == null || g.Landmark.Contains(tile.zone.id)) return false;
        if (!g.Modern) return !built && use == ZoneUse.None && Village(g, tile, color, out result);
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy), angle = Angle(dx, dy), edge = EdgeAt(g, angle);
        bool inEdge = r < edge;
        // 主干：环路、放射干道、铁路与车站
        if (OnRing(g, r, edge) || r > g.OldRadius - 1f && r < edge + 1f && RayDistance(g, angle, r) < 0.6f)
        {
            result = Paint(color, Asphalt, 0.9f);
            return true;
        }
        if (g.Progress >= 0.4f)
        {
            float rail = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, g.RailDir * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
            if (rail < 0.5f && r > g.OldRadius + 1f && r < edge)
            {
                result = Paint(color, (tile.x + tile.y) % 2 == 0 ? Tie : Rail, 0.9f);
                return true;
            }
            if (rail < 2f && r > g.OldRadius + 1f && r < g.OldRadius + 4f) { result = Paint(color, Station, 0.9f); return true; }
        }
        if (Rough(tile)) return false;
        (Kind kind, bool developed) = ZoneOf(g, tile.zone);
        if (kind == Kind.Farm) return false;
        // 公园(保护区)：城里是公园，城外留原野
        if (kind == Kind.Park)
        {
            if (!inEdge) return false;
            result = Paint(color, H(g.Seed, tile.x, tile.y, 11) < 0.3f ? Tree : Park, 0.85f);
            return true;
        }
        // 开发了的地块：规划过的整块都画，没规划的只画城区范围里的部分
        if (developed && (inEdge || use != ZoneUse.None))
        {
            result = Block(g, tile, color, kind, r, edge);
            return true;
        }
        if (built) return false;
        return use == ZoneUse.None && !inEdge && Village(g, tile, color, out result);
    }

    // 一块地(8×8)：四边是街、正中一纵一横小巷，切出四个 3×3 的街区；街区按用途画，同一块地一个样子
    private static Color32 Block(Geometry g, WorldTile tile, Color32 color, Kind kind, float r, float edge)
    {
        int size = Config.CITY_ZONE_SIZE;
        int lx = tile.x - tile.zone.x * size, ly = tile.y - tile.zone.y * size;
        if (lx == 0 || ly == 0) return Paint(color, Local, 0.9f);
        if (lx == 4 || ly == 4) return Paint(color, Local, 0.45f);
        int cx = (lx - 1) % 4, cy = (ly - 1) % 4;
        int cellX = tile.x >> 2, cellY = tile.y >> 2;
        float q = H(g.Seed, cellX, cellY, 3);
        switch (kind)
        {
            case Kind.Industry:
            {
                // 厂房：整块的锯齿屋顶，一些街区一角有烟囱
                if (cx == 0 && cy == 2 && q > 0.5f) return Paint(color, Chimney, 0.95f);
                Color32 roof = Factory[(int)(q * 4) % 4];
                return Paint(color, cx % 2 == 0 ? Mix(roof, White, 0.15f) : roof, 0.9f);
            }
            case Kind.Market:
                return Paint(color, cx == 1 && cy == 1 ? Mix(Market, White, 0.4f) : Market, 0.85f);
            case Kind.Military:
                return Paint(color, cy == 1 ? Parade : Barrack, 0.85f);
            case Kind.Tower:
            {
                // 塔楼：一个街区一栋，两格或三格见方，玻璃或石材；西北受光，东南投影
                int span = q > 0.45f ? 3 : 2;
                Color32 roof = q > 0.3f
                    ? Glass[(int)(H(g.Seed, cellX, cellY, 4) * 4) % 4]
                    : Concrete[(int)(H(g.Seed, cellX, cellY, 4) * 4) % 4];
                if (cx < span && cy >= 3 - span) return Paint(color, cx == 0 ? Mix(roof, White, 0.2f) : roof, 0.92f);
                return Paint(color, Shadow, 0.85f);
            }
        }
        // 住宅：内圈板楼(一个街区两排，中间绿地)，西欧、罗马、维京是围合街坊(四周一圈楼、中间院子)；外圈低层住宅
        if (r < edge * 0.75f && g.Progress >= 1.3f)
        {
            if (g.Perimeter)
                return cx == 1 && cy == 1 ? Paint(color, q < 0.5f ? Park : Yard, 0.7f)
                    : Paint(color, HouseRoof[(int)(H(g.Seed, cellX, cellY, 5) * 4) % 4], 0.88f);
            return cy != 1 ? Paint(color, Slab[(int)(q * 4) % 4], 0.88f) : q < 0.4f ? Paint(color, Park, 0.85f) : Lot(color);
        }
        return cx != 1 && cy != 1 ? Paint(color, HouseRoof[(int)(H(g.Seed, tile.x >> 1, tile.y >> 1, 9) * 4) % 4], 0.88f) : Lot(color);
    }

    // 村子：几户人家隔一格排开，院落围着；乡镇有十字街、中间小集市
    private static bool Village(Geometry g, WorldTile tile, Color32 color, out Color32 result)
    {
        result = color;
        foreach ((float x, float y, int size, bool town) in g.Villages)
        {
            int dx = tile.x - (int)x, dy = tile.y - (int)y;
            if (Mathf.Abs(dx) > size || Mathf.Abs(dy) > size) continue;
            if (town && (dx == 0 || dy == 0))
            {
                result = Paint(color, dx == 0 && dy == 0 ? Market : TownStreet, 0.85f);
                return true;
            }
            if ((dx & 1) != 0 && (dy & 1) != 0)
            {
                result = Paint(color, g.Progress < 1f ? HouseRoof[(int)(H(g.Seed, tile.x, tile.y, 14) * 4) % 4]
                    : Slab[(int)(H(g.Seed, tile.x, tile.y, 15) * 4) % 4], 0.88f);
                return true;
            }
            result = Paint(color, Yard, 0.4f);
            return true;
        }
        return false;
    }

    #endregion
}
