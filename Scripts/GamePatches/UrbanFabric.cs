using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市肌理(纯净地图)：城市从县城慢慢长成都市，城外散落村镇。每座城有自己的随机种子(默认是城的编号，
// 「另起格局」神力可以换一个)：干道几条、朝哪、城区往哪边蔓延、各片区街网的朝向、市中心偏在哪、工业区在哪一侧、
// 村镇落在哪、楼的形状高低颜色都随它，同样的规则长出各不相同的城。
//   · 古代：城区照原来的画法(城墙、里坊、街格，见 PureMapPatch.Plan)；城外的农田是大小不一的田块(离城越远越淡)，
//     出城的土路边、路旁零星人家，散落村子，少数村子慢慢长成乡镇(十字街、小集市)。
//   · 工业化(本文化研究出工业化后逐年推进)：拆掉城墙，旧城一线改成环路；土路铺成公路，修铁路与车站，
//     城外按规划(或工业那一侧)起工厂、储罐。
//   · 电气：城区外长出新城区，各片区的街网朝向不同；居住区是板楼小区(西欧、罗马、维京是围合街坊)，
//     外圈是低层住宅，沿公路往外长出一溜房子；城区大了加修二环。
//   · 现代城市规划：市中心(与商业区)逐块起塔楼——大小、形状(方、长、L 形、圆)、玻璃或石材屋顶各异，
//     东南侧投影；再修三环。
// 自然发展：
//   · 每座城发展快慢不一(种子定一个快慢，城大的、都城快些)，同一座城各片区也有先有后；
//   · 每个街区有自己的开发门槛：城越发达越多街区换成新样子，新旧混杂地过渡；规划过的地块先开发；
//   · 城区按种子往某几个方向蔓延得远些，沿公路先长出去；山地、岩地不起楼，留成原样；
//   · 村子各有大小，城区扩到村子跟前，村子先被包进城里，街区开发了才换掉。
// 地块的规划用途(城市规划神力)决定画什么：工业区画工厂，商业区画市场(现代是高楼)，居住区画住宅，军事区画营区，
// 保护区城里是公园、城外是原野，农耕区一直是农田(可有村子)。
// 只画在这座城自己的地块上；城区范围跟着实际建成的地块长。
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
        public float[] RibbonLength = new float[0];
        public int Sectors;
        public float SectorOffset;
        public float[] SectorRot = new float[0], SectorPace = new float[0];
        public float IndustryDir, RailDir;
        public int CbdX, CbdY;
        public bool Perimeter;
        public readonly List<(float x, float y, float size, bool town)> Villages = new();
        public City City;
        public HashSet<int> Built, Landmark;
        public bool WallsDown => Progress >= 0.2f;
        public bool Modern => Progress > 0f;
    }

    private const int EdgeBins = 48, MaxSeededRays = 7;
    private const float TwoPi = Mathf.PI * 2f;

    private static readonly Color32 Dirt = new(196, 176, 136, 255), Asphalt = new(80, 84, 94, 255),
        BridgeDeck = new(150, 150, 156, 255), Local = new(176, 178, 180, 255), Ground = new(204, 202, 194, 255),
        Rail = new(66, 58, 54, 255), Tie = new(165, 145, 118, 255), Station = new(180, 90, 70, 255),
        Park = new(96, 162, 86, 255), Tree = new(56, 116, 58, 255), Shadow = new(96, 98, 104, 255),
        Yard = new(176, 190, 132, 255), Market = new(196, 160, 90, 255), TownStreet = new(226, 220, 206, 255),
        Tank = new(220, 220, 214, 255), Chimney = new(60, 55, 55, 255), Parade = new(186, 176, 150, 255),
        Fence = new(110, 104, 92, 255), Barrack = new(120, 130, 100, 255), Stand = new(230, 230, 236, 255),
        Pitch = new(90, 160, 90, 255), Track = new(200, 80, 70, 255), White = new(255, 255, 255, 255);
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
    private static readonly Color32[] Crops =
    {
        new(160, 182, 104, 255), new(142, 170, 92, 255), new(178, 186, 110, 255), new(130, 160, 96, 255),
        new(190, 178, 112, 255)
    };

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

    #region 随机

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
            g.Edge[i] = smooth[i] * jitter * (1f + Mathf.Min(g.Progress, 3f) * 0.18f * (0.3f + 1.4f * lobe));
            g.MaxEdge = Mathf.Max(g.MaxEdge, g.Edge[i]);
        }
        // 干道：先按种子定几条(古代三条，工业化后四到七条)，再补上实际通往邻城、方向没重复的路
        var rays = new List<float>();
        int want = g.Modern ? 4 + (int)(H(seed, 1) * 4) : 3;
        for (int attempt = 0; rays.Count < Mathf.Min(want, MaxSeededRays) && attempt < 32; attempt++)
        {
            float a = H(seed, 2, attempt) * TwoPi;
            if (!Near(rays, a, 360f / (MaxSeededRays + 2))) rays.Add(a);
        }
        if (rays.Count > 0)
            g.RailDir = rays[(int)(H(seed, 4) * rays.Count) % rays.Count] + 0.25f;
        foreach (KeyValuePair<(long, long), RoadLink> pair in RoadBuildingSystem.All)
        {
            long other = pair.Key.Item1 == city.data.id ? pair.Key.Item2 : pair.Key.Item2 == city.data.id ? pair.Key.Item1 : -1L;
            WorldTile there = other < 0 ? null : World.world.cities.get(other)?.getTile();
            if (there == null) continue;
            float a = Angle(there.x - g.Cx, there.y - g.Cy);
            if (!Near(rays, a, 25f)) rays.Add(a);
        }
        rays.Sort();
        g.Rays = rays.ToArray();
        // 沿每条路往外长出的一溜房子有多长
        g.RibbonLength = new float[g.Rays.Length];
        for (int k = 0; k < g.Rays.Length; k++)
            g.RibbonLength[k] = (2f + 9f * H(seed, (long)(g.Rays[k] * 100f), 53)) * (0.4f + 0.3f * Mathf.Min(g.Progress, 2f));
        // 片区：按种子分四到七片，各片街网朝向、开发先后不同(与干道无关，修了新路片区不变)
        g.Sectors = 4 + (int)(H(seed, 7) * 4);
        g.SectorOffset = H(seed, 8) * TwoPi;
        g.SectorRot = new float[g.Sectors];
        g.SectorPace = new float[g.Sectors];
        for (int i = 0; i < g.Sectors; i++)
        {
            g.SectorRot[i] = (H(seed, i, 9) - 0.5f) * 0.9f;
            g.SectorPace[i] = (H(seed, i, 52) - 0.5f) * 0.6f;
        }
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

    // 村镇：离城心十到四十格，多在干道附近；只落在本城没建成的平地上。城的地盘大了村子多，
    // 各村大小不一，随时代慢慢长大，少数长成乡镇
    private static void PlaceVillages(Geometry g)
    {
        int count = 2 + (int)(H(g.Seed, 30) * 4) + g.City.zones.Count / 14;
        for (int attempt = 0; attempt < count * 4 && g.Villages.Count < count; attempt++)
        {
            float a = g.Rays.Length > 0 && H(g.Seed, attempt, 45) < 0.7f
                ? g.Rays[(int)(H(g.Seed, attempt, 40) * g.Rays.Length) % g.Rays.Length] + (H(g.Seed, attempt, 42) - 0.5f) * 0.35f
                : H(g.Seed, attempt, 46) * TwoPi;
            float dist = 10f + H(g.Seed, attempt, 41) * 30f;
            float vx = g.Cx + dist * Mathf.Cos(a), vy = g.Cy + dist * Mathf.Sin(a);
            WorldTile tile = World.world.GetTile((int)vx, (int)vy);
            if (tile?.zone == null || tile.zone.city != g.City || g.Built.Contains(tile.zone.id) ||
                tile.Type == null || tile.Type.liquid || Rough(tile)) continue;
            float growth = 0.5f + H(g.Seed, attempt, 47);
            bool town = H(g.Seed, attempt, 43) < 0.1f + 0.12f * g.Progress * growth;
            float size = (town ? 4f : 2.4f) * (0.8f + 0.4f * H(g.Seed, attempt, 44)) * (1f + 0.15f * g.Progress * growth);
            g.Villages.Add((vx, vy, size, town));
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
    private static float RayAt(Geometry g, int k, float r) => g.Rays[k] + 0.1f * Mathf.Sin(r * 0.07f + k * 1.7f + g.Seed % 7);

    // 离最近一条干道多远(格)
    private static float RayDistance(Geometry g, float angle, float r, out int nearest)
    {
        nearest = -1;
        float best = float.MaxValue;
        for (int k = 0; k < g.Rays.Length; k++)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, RayAt(g, k, r) * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
            if (d >= best) continue;
            best = d;
            nearest = k;
        }
        return best;
    }

    private static int SectorOf(Geometry g, float angle)
    {
        float a = angle - g.SectorOffset;
        if (a < 0f) a += TwoPi;
        return Mathf.Clamp((int)(a / TwoPi * g.Sectors), 0, g.Sectors - 1);
    }

    private static bool OnRing(Geometry g, float r, float edge)
    {
        if (g.WallsDown && Mathf.Abs(r - g.OldRadius) < 0.6f) return true;
        if (g.Progress >= 1.4f && g.MaxEdge > 18f && Mathf.Abs(r - edge * 0.62f) < 0.6f && edge * 0.62f > g.OldRadius + 3f)
            return true;
        return g.Progress >= 2.5f && Mathf.Abs(r - edge * 0.9f) < 0.6f;
    }

    // 山地、岩地：不起楼、不开田，留成原样
    private static bool Rough(WorldTile tile) => tile.Type != null && (tile.Type.mountains || tile.Type.rocks || tile.Type.block);

    // 河上过桥：干道、环路跨水处(ShadeTerrain 的水面分支调用)
    public static bool Bridge(Geometry g, WorldTile tile)
    {
        if (g == null || !g.Modern || tile == null) return false;
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy), angle = Angle(dx, dy), edge = EdgeAt(g, angle);
        return r < edge + 4f && (OnRing(g, r, edge) || r > g.OldRadius - 1f && RayDistance(g, angle, r, out _) < 0.6f);
    }

    public static Color32 BridgeColor => BridgeDeck;

    // 这一格画什么；返回 false 时照原来的画法(古代城区、还没换代的建成地块、都城地标、山地)
    public static bool Tile(WorldTile tile, Color32 color, Geometry g, bool built, ZoneUse use, out Color32 result)
    {
        result = color;
        if (g == null || tile?.zone == null || g.Landmark.Contains(tile.zone.id)) return false;
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy), angle = Angle(dx, dy), edge = EdgeAt(g, angle);
        float ray = RayDistance(g, angle, r, out int nearest);
        if (!g.Modern)
        {
            if (built) return false;
            if (r > g.OldRadius && r < edge + 30f && ray < 0.5f) { result = Mix(color, Dirt, 0.8f); return true; }
            if (use == ZoneUse.Reserve || Rough(tile)) return false;
            if (Ribbon(g, tile, color, r, edge, ray, nearest, out result)) return true;
            if (Village(g, tile, color, out result)) return true;
            result = Field(g, tile, color, r, edge, use);
            return true;
        }
        // 工业化以后：环路、干道、铁路
        if (OnRing(g, r, edge) || r > g.OldRadius - 1f && r < edge + 30f && ray < 0.6f)
        {
            result = Mix(color, r > edge + 2f && g.Progress < 0.6f ? Dirt : Asphalt, 0.9f);
            return true;
        }
        if (g.Progress >= 0.4f)
        {
            float rail = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, g.RailDir * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
            if (rail < 0.5f && r > g.OldRadius + 1f && r < edge + 8f)
            {
                result = Mix(color, (tile.x + tile.y) % 2 == 0 ? Tie : Rail, 0.9f);
                return true;
            }
            if (rail < 2f && r > g.OldRadius + 1f && r < g.OldRadius + 4f) { result = Mix(color, Station, 0.9f); return true; }
        }
        if (Rough(tile)) return false;
        // 旧城：现代城市规划后逐块起高楼，其余照原来的画法(城墙已拆)
        if (r <= g.OldRadius - 0.5f)
        {
            int bx = Mathf.FloorToInt((tile.x + 1000) / 5f), by = Mathf.FloorToInt((tile.y + 1000) / 5f);
            if (g.Progress >= 2f && use != ZoneUse.Reserve && use != ZoneUse.Farm &&
                H(g.Seed, bx, by, 61) < (g.Progress - 2f) * 0.8f)
            {
                result = Tower(g, tile, color);
                return true;
            }
            return false;
        }
        // 城区以外：郊区小房子、沿路的一溜房子、村镇、农田
        if (r > edge)
        {
            if (built) return false;
            if (use == ZoneUse.Reserve) return false;
            float fringe = r - edge;
            if (g.Progress >= 1f && fringe < 4f && use != ZoneUse.Farm &&
                H(g.Seed, tile.x / 3, tile.y / 3, 8) > 0.45f + fringe * 0.12f && tile.x % 3 != 0 && tile.y % 3 != 0)
            {
                result = Mix(color, HouseRoof[(int)(H(g.Seed, tile.x, tile.y, 9) * 4) % 4], 0.85f);
                return true;
            }
            if (Ribbon(g, tile, color, r, edge, ray, nearest, out result)) return true;
            if (Village(g, tile, color, out result)) return true;
            result = Field(g, tile, color, r, edge, use);
            return true;
        }
        // 新城区：各片区的街网朝向不同，街区大小随离城心远近
        int sector = SectorOf(g, angle);
        float rot = g.SectorRot[sector];
        float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
        float u = dx * cs + dy * sn + 600f, v = -dx * sn + dy * cs + 600f;
        int size = r < edge * 0.6f ? 6 : 7;
        int bu = Mathf.FloorToInt(u / size), bv = Mathf.FloorToInt(v / size);
        float fu = u - bu * size, fv = v - bv * size;
        float seed = H(g.Seed, bu, bv, sector);
        bool industrySide = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, g.IndustryDir * Mathf.Rad2Deg)) < 40f &&
                            r > g.OldRadius + 3f;
        float cbdX = tile.x - g.CbdX, cbdY = tile.y - g.CbdY;
        bool cbd = g.Progress >= 2f && Mathf.Sqrt(cbdX * cbdX + cbdY * cbdY) < edge * 0.36f;
        // 规划用途决定画什么；没规划的按种子安排
        string kind = use switch
        {
            ZoneUse.Industry => "industry",
            ZoneUse.Commerce => g.Progress >= 2f ? "tower" : "market",
            ZoneUse.Military => "military",
            ZoneUse.Reserve => "park",
            ZoneUse.Farm => "farm",
            ZoneUse.Residential => cbd ? "tower" : "housing",
            _ => industrySide ? "industry" : cbd ? "tower" : "housing"
        };
        // 开发门槛：工厂工业化就起，住宅电气以后，高楼现代规划以后；各片区有先有后，规划过的先开发
        float need = kind switch
        {
            "industry" => 0.3f + seed * 0.7f,
            "tower" => 2f + seed * 0.9f,
            "farm" => 99f,
            "park" => 0f,
            _ => 1f + seed * 0.9f
        } + g.SectorPace[sector];
        if (use != ZoneUse.None) need -= 0.3f;
        if (kind == "park") { result = Green(g, tile, color); return true; }
        if (g.Progress < need)
        {
            if (built) return false;
            if (Village(g, tile, color, out result)) return true;
            result = Field(g, tile, color, r, edge, use);
            return true;
        }
        if (fu < 1f || fv < 1f) { result = Mix(color, Local, 0.85f); return true; }
        float lu = fu - 1f, lv = fv - 1f, n = size - 1;
        // 街心公园(少数街区)
        if (kind != "industry" && kind != "military" && H(g.Seed, bu, bv, 77) < 0.07f)
        {
            result = Green(g, tile, color);
            return true;
        }
        switch (kind)
        {
            case "industry":
                result = IndustryTile(color, lu, lv, n, seed);
                return true;
            case "military":
                result = MilitaryTile(color, lu, lv, n);
                return true;
            case "tower":
                result = Tower(g, tile, color);
                return true;
            case "market":
                result = (int)lu % 2 == 0 && lv >= 0.5f && lv < n - 0.5f
                    ? Mix(color, HouseRoof[(int)(seed * 4) % 4], 0.85f) : Mix(color, Market, 0.7f);
                return true;
        }
        // 体育场(少数大街区)
        if (H(g.Seed, bu, bv, 88) > 0.985f && r > g.OldRadius + 4f)
        {
            float e = (lu - n / 2f) * (lu - n / 2f) / (n * n / 4f) + (lv - n / 2f) * (lv - n / 2f) / (n * n / 7f);
            result = Mix(color, e < 0.25f ? Pitch : e < 0.5f ? Stand : e < 1f ? Track : Ground, 0.9f);
            return true;
        }
        // 住宅：内圈板楼小区(西欧围合街坊)，外圈低层住宅
        if (r < edge * 0.75f && g.Progress >= 1.3f)
        {
            if (g.Perimeter)
            {
                bool ring = lu < 1.5f || lu >= n - 1.5f || lv < 1.5f || lv >= n - 1.5f;
                Color32 wall = seed < 0.6f
                    ? HouseRoof[(int)(H(g.Seed, bu, bv, (int)lu / 2 * 7 + (int)lv / 2) * 4) % 4]
                    : Slab[(int)(seed * 4) % 4];
                result = ring ? Mix(color, wall, 0.88f) : Mix(color, seed > 0.5f ? Park : Ground, 0.85f);
                return true;
            }
            result = (int)lv % 2 == 0 && lu >= 0.5f && lu < n - 0.5f
                ? Mix(color, Slab[(int)(seed * 4) % 4], 0.88f)
                : Mix(color, H(g.Seed, bu, bv, (int)lv) < 0.4f ? Park : Ground, 0.85f);
            return true;
        }
        int hx = (int)(lu / 2.5f), hy = (int)(lv / 2.5f);
        float q = H(g.Seed, bu * 31 + hx, bv * 17 + hy, 6);
        if (q < 0.75f && lu % 2.5f < 1.6f && lv % 2.5f < 1.6f)
            result = Mix(color, q < 0.45f ? HouseRoof[(int)(q * 13) % 4] : Slab[(int)(q * 7) % 4], 0.88f);
        else result = Mix(color, Mix(Ground, Park, 0.4f), 0.8f);
        return true;
    }

    // 塔楼：五格一个街区，楼的大小、形状(方、长、L 形、圆)、屋顶(玻璃或石材)各异，西侧受光，东南侧投影
    private static Color32 Tower(Geometry g, WorldTile tile, Color32 color)
    {
        const int size = 5;
        int bu = Mathf.FloorToInt((tile.x + 1000) / (float)size), bv = Mathf.FloorToInt((tile.y + 1000) / (float)size);
        int lu = (tile.x + 1000) % size, lv = (tile.y + 1000) % size;
        if (lu == 0 || lv == 0) return Mix(color, Local, 0.85f);
        float seed = H(g.Seed, bu, bv, 31);
        int n = size - 1, shape = (int)(seed * 5);
        int w = 1 + (int)(H(g.Seed, bu, bv, 1) * n), h = 1 + (int)(H(g.Seed, bu, bv, 2) * n);
        bool inside = lu <= w && lv <= h;
        if (shape == 3) inside = inside && !(lu > w / 2f + 0.5f && lv > h / 2f + 0.5f);
        if (shape == 4)
        {
            w = h = n - 1;
            inside = (lu - 2f) * (lu - 2f) + (lv - 2f) * (lv - 2f) <= 1.6f;
        }
        Color32 roof = seed > 0.35f ? Glass[(int)(H(g.Seed, bu, bv, 4) * 4) % 4] : Concrete[(int)(H(g.Seed, bu, bv, 4) * 4) % 4];
        if (inside) return Mix(color, lu == 1 ? Mix(roof, White, 0.2f) : roof, 0.92f);
        int tall = seed > 0.6f ? 2 : 1;
        if (lu >= 2 && lu <= w + tall && lv >= 1 - tall && lv <= h - 1) return Mix(color, Shadow, 0.85f);
        return Mix(color, Ground, 0.85f);
    }

    private static Color32 IndustryTile(Color32 color, float lu, float lv, float n, float seed)
    {
        if (seed > 0.75f)
        {
            float e = (lu - n / 2f) * (lu - n / 2f) + (lv - n / 2f) * (lv - n / 2f);
            return Mix(color, e < n * n / 5.8f ? Tank : Ground, 0.88f);
        }
        if (lu >= 0.5f && lu < n - 0.5f && lv >= 0.5f && lv < n - 1.5f)
        {
            if ((int)lu == 1 && (int)lv == (int)(n - 2)) return Mix(color, Chimney, 0.95f);
            Color32 roof = Factory[(int)(seed * 10) % 4];
            return Mix(color, (int)lu % 2 == 0 ? Mix(roof, White, 0.15f) : roof, 0.9f);
        }
        return Mix(color, Ground, 0.85f);
    }

    // 营区：围栏、操场、一排排营房
    private static Color32 MilitaryTile(Color32 color, float lu, float lv, float n)
    {
        if (lu < 0.5f || lv < 0.5f || lu >= n - 0.5f || lv >= n - 0.5f) return Mix(color, Fence, 0.8f);
        if (lu < n / 2f) return Mix(color, Parade, 0.8f);
        return Mix(color, (int)lv % 2 == 0 ? Barrack : Ground, 0.85f);
    }

    private static Color32 Green(Geometry g, WorldTile tile, Color32 color) =>
        Mix(color, H(g.Seed, tile.x, tile.y, 11) < 0.35f ? Tree : Park, 0.85f);

    // 沿路的一溜房子：出城的干道两旁，离城越远越稀，每条路长出去的长短不一
    private static bool Ribbon(Geometry g, WorldTile tile, Color32 color, float r, float edge, float ray, int nearest, out Color32 result)
    {
        result = color;
        if (nearest < 0 || ray < 0.7f || ray > 2.4f) return false;
        float reach = (r - edge) / Mathf.Max(0.1f, g.RibbonLength[nearest]);
        if (reach < 0f || reach > 1f || H(g.Seed, tile.x, tile.y, 16) < 0.35f + reach * 0.55f) return false;
        result = Mix(color, g.Progress < 1f || H(g.Seed, tile.x, tile.y, 17) < 0.7f
            ? HouseRoof[(int)(H(g.Seed, tile.x, tile.y, 18) * 4) % 4]
            : Slab[(int)(H(g.Seed, tile.x, tile.y, 19) * 4) % 4], 0.85f);
        return true;
    }

    // 农田：大小不一的田块，每块一种作物色，淡淡地盖在国色上，离城越远越淡；农耕区浓些；不能种地的地方不画
    private static Color32 Field(Geometry g, WorldTile tile, Color32 color, float r, float edge, ZoneUse use)
    {
        if (tile.Type == null || !tile.Type.can_be_farm && !tile.Type.farm_field) return color;
        float strength = use == ZoneUse.Farm ? 0.4f : 0.28f * Mathf.Clamp01(1.2f - (r - edge) / 30f);
        if (strength <= 0.01f) return color;
        float fx = tile.x + 4f * Noise(tile.x, tile.y, 13f, g.Seed + 1), fy = tile.y + 4f * Noise(tile.x, tile.y, 13f, g.Seed + 2);
        int by = Mathf.FloorToInt(fy / 6f);
        int bx = Mathf.FloorToInt(fx / (5f + (int)(H(g.Seed, by) * 4)));
        return Mix(color, Crops[(int)(H(g.Seed, bx, by, 5) * 5) % 5], strength);
    }

    // 村镇：零散的屋舍与院落；乡镇有十字街与小集市
    private static bool Village(Geometry g, WorldTile tile, Color32 color, out Color32 result)
    {
        result = color;
        foreach ((float x, float y, float size, bool town) in g.Villages)
        {
            float dx = tile.x - x, dy = tile.y - y;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > size + 0.5f) continue;
            if (town && (Mathf.RoundToInt(dx) == 0 || Mathf.RoundToInt(dy) == 0))
            {
                result = Mix(color, d < 1.2f ? Market : TownStreet, 0.85f);
                return true;
            }
            if (H(g.Seed, tile.x, tile.y, 12) < (town ? 0.6f : 0.45f))
            {
                result = Mix(color, g.Progress < 1f || H(g.Seed, tile.x, tile.y, 13) < 0.6f
                    ? HouseRoof[(int)(H(g.Seed, tile.x, tile.y, 14) * 4) % 4]
                    : Slab[(int)(H(g.Seed, tile.x, tile.y, 15) * 4) % 4], 0.88f);
                return true;
            }
            result = Mix(color, Yard, 0.5f);
            return true;
        }
        return false;
    }

    #endregion
}
