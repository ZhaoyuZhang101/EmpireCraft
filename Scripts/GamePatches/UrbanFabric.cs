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
// 城郊按本城在经济圈里的档位(见 EconomicCircleSystem)：
//   · 主城(一方经济中心，古代多半是首都或临河临海的城)是都会：四门各一条出城大路，门外关厢一溜人家，
//     大路上两三个镇(十字街、小集市，富庶的有一圈矮墙)，大路之间散着村子，各有小路通到大路，田野林子围着；
//   · 卫星城与一般的城是县城：两三条或四条出城的路，一两个镇，几处村子；赤贫的城只有一两处村子；
//   · 田块离城镇村越远越淡，只画在能种地的平地上；
//   · 档位变化慢(几年才升降一档)，城的格局不会忽大忽小。
// 时代：
//   · 古代：城区照原来的画法(城墙、里坊、街格，见 PureMapPatch.Plan)，城外如上；
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
        // 城的轮廓(古代的城墙一线)：各方向的半径；最近、最远处
        public readonly float[] Foot = new float[EdgeBins];
        public float FootMin, FootMax;
        public float[] Rays = new float[0];
        public int Sectors;
        public float SectorOffset;
        public float[] SectorPace = new float[0];
        public float IndustryDir, RailDir;
        public int CbdX, CbdY;
        public bool Perimeter;
        // 城郊：档位、城门(城心两条中线)、四条出城大路(东北西南)是否修了、从城门到城外的距离、路长、关厢长
        public int Tier, GateX, GateY;
        // 城郊的路：出城大路(从城门出去)与岔路(从大路上分出去)
        public readonly List<Road> Roads = new();
        public int Suburb;
        public readonly List<(int x, int y, int size, bool walled)> Towns = new();
        // 村子与通到大路的小路(竖着走为 vertical：x 固定，y 从 from 到 to；否则 y 固定)
        public readonly List<(int x, int y)> Villages = new();
        public readonly List<(bool vertical, int fixedAt, int from, int to)> Paths = new();
        // 各地块上有哪些城郊景物(按地块算一次，逐格上色时只查这几样)
        internal readonly Dictionary<int, Rural> Rurals = new();
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
    // 平常淡淡地画在国色上；城市图层画在地形上，浓一些
    private static float Strength => Vivid ? 0.9f : 0.5f;
    public static bool Vivid;

    private static readonly Color32 Asphalt = new(80, 84, 94, 255), BridgeDeck = new(150, 150, 156, 255),
        Local = new(176, 178, 180, 255), Rail = new(66, 58, 54, 255), Tie = new(165, 145, 118, 255),
        Station = new(180, 90, 70, 255), Park = new(96, 162, 86, 255), Tree = new(56, 116, 58, 255),
        Shadow = new(96, 98, 104, 255), Yard = new(176, 190, 132, 255), Market = new(196, 160, 90, 255),
        TownStreet = new(226, 220, 206, 255), Chimney = new(60, 55, 55, 255), Parade = new(186, 176, 150, 255),
        Barrack = new(120, 130, 100, 255), White = new(255, 255, 255, 255), Dirt = new(196, 176, 136, 255),
        Lane = new(206, 190, 150, 255), TownWall = new(124, 112, 100, 255), Grove = new(80, 140, 70, 255);
    private static readonly Color32[] Crops =
    {
        new(160, 182, 104, 255), new(142, 170, 92, 255), new(178, 186, 110, 255), new(130, 160, 96, 255),
        new(190, 178, 112, 255)
    };
    private static readonly int[] AxisX = { 1, 0, -1, 0 }, AxisY = { 0, 1, 0, -1 };
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

    public static Geometry Build(City city, HashSet<int> built, HashSet<int> landmark, string family, string culture,
        int gateX, int gateY)
    {
        WorldTile center = city?.getTile();
        if (center == null || city.zones == null) return null;
        long seed = SeedOf(city);
        var g = new Geometry
        {
            Seed = seed, Cx = gateX == int.MinValue ? center.x : gateX, Cy = gateY == int.MinValue ? center.y : gateY,
            City = city, Built = built, Landmark = landmark,
            Perimeter = family is "western" or "roma" or "viking"
        };
        // 发展快慢：种子定一个底(0.7~1.3 倍)，城大的、都城快些
        float pace = (0.7f + 0.6f * H(seed, 50)) * (0.85f + 0.3f * Mathf.Clamp01(built.Count / 12f)) *
                     (city.isCapitalCity() ? 1.15f : 1f);
        g.Progress = ProgressOf(culture, pace);
        // 城的轮廓：面积是建成地块的一半(建筑挤在一起，城外留给田野)；建成地块之外的部分不算城里。
        // 种子定城的样子：长宽比(约 0.7~1.4)、往哪边偏(城区中心离城门中线至多四成半径)、边缘起伏多大；
        // 华夏、日本的城是方城(圆角，横平竖直)，其余是起伏的一团，还可以斜着长、起伏的疏密也不同
        float baseRadius = Mathf.Sqrt(Mathf.Max(1, built.Count) * Config.CITY_ZONE_SIZE * Config.CITY_ZONE_SIZE * TownDensity / Mathf.PI);
        bool squarish = family is "huaxia" or "japan";
        float aspect = Mathf.Sqrt(0.5f + 1.5f * H(seed, 70)), turn = squarish ? 0f : H(seed, 71) * Mathf.PI;
        float lean = 0.4f * H(seed, 72) * baseRadius, leanDir = H(seed, 73) * TwoPi;
        g.Cx += Mathf.RoundToInt(lean * Mathf.Cos(leanDir));
        g.Cy += Mathf.RoundToInt(lean * Mathf.Sin(leanDir));
        float rough = squarish ? 0.1f + 0.12f * H(seed, 74) : 0.25f + 0.45f * H(seed, 74);
        float lumps = squarish ? 0.9f + 0.6f * H(seed, 75) : 0.7f + 0.8f * H(seed, 75);
        float power = squarish ? 3.5f : 2f;
        float sum = 0f;
        g.FootMin = float.MaxValue;
        for (int i = 0; i < EdgeBins; i++)
        {
            float a = (i + 0.5f) / EdgeBins * TwoPi;
            float c = Mathf.Abs(Mathf.Cos(a - turn)) / aspect, s = Mathf.Abs(Mathf.Sin(a - turn)) * aspect;
            float shape = (squarish ? 0.9f : 1f) / Mathf.Pow(Mathf.Pow(c, power) + Mathf.Pow(s, power), 1f / power);
            float wobble = Noise(30f + 2.5f * Mathf.Cos(a), 30f + 2.5f * Mathf.Sin(a), lumps, seed + 5);
            g.Foot[i] = Mathf.Max(4f, baseRadius * shape * (1f - rough / 2f + rough * wobble));
            g.FootMin = Mathf.Min(g.FootMin, g.Foot[i]);
            g.FootMax = Mathf.Max(g.FootMax, g.Foot[i]);
            sum += g.Foot[i];
        }
        // 旧城：工业化那时的城区(记在城里，之后不再变)
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (g.Modern)
        {
            if (data.urban_old_radius <= 0f) data.urban_old_radius = Mathf.Max(5f, sum / EdgeBins * 0.9f);
            g.OldRadius = data.urban_old_radius;
        }
        else g.OldRadius = sum / EdgeBins;
        // 蔓延：工业化以后城区往外长，种子定哪几个方向长得远(沿一圈连续起伏，没有接缝)
        for (int i = 0; i < EdgeBins; i++)
        {
            float a = (i + 0.5f) / EdgeBins * TwoPi;
            float lobe = Noise(10f + 3f * Mathf.Cos(a), 10f + 3f * Mathf.Sin(a), 1.6f, seed + 3);
            float jitter = 0.92f + 0.16f * Noise(20f + 6f * Mathf.Cos(a), 20f + 6f * Mathf.Sin(a), 1.3f, seed + 4);
            g.Edge[i] = (g.Foot[i] + 2f) * jitter * (1f + Mathf.Min(g.Progress, 3f) * 0.15f * (0.3f + 1.4f * lobe));
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
        // 城郊档位：跟着经济圈的档位走，几年才升降一档
        CityExtension.CityExtraData extra = city.GetOrCreate();
        int wanted = EconomicCircleSystem.RegionTier(city);
        if (extra.urban_tier < 0 || extra.urban_tier_at < 0d)
        {
            extra.urban_tier = wanted;
            extra.urban_tier_at = World.world.getCurWorldTime();
        }
        else if (wanted != extra.urban_tier && Date.getYearsSince(extra.urban_tier_at) >= TierYears)
        {
            extra.urban_tier += wanted > extra.urban_tier ? 1 : -1;
            extra.urban_tier_at = World.world.getCurWorldTime();
        }
        g.Tier = Mathf.Clamp(extra.urban_tier, 0, 4);
        g.GateX = gateX == int.MinValue ? g.Cx : gateX;
        g.GateY = gateY == int.MinValue ? g.Cy : gateY;
        PlaceCountryside(g);
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

    private const float TierYears = 3f;
    // 城的轮廓占建成地块面积的比例
    private const float TownDensity = 0.5f;

    // 在城的轮廓里(建成地块里落在轮廓外的格子画成城郊)；没有城市肌理时整块算
    public static bool InTown(Geometry g, WorldTile tile)
    {
        if (g == null) return true;
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy, d2 = dx * dx + dy * dy;
        if (d2 <= g.FootMin * g.FootMin) return true;
        if (d2 > g.FootMax * g.FootMax) return false;
        float f = Angle(dx, dy) / TwoPi * EdgeBins - 0.5f;
        if (f < 0f) f += EdgeBins;
        int a = Mathf.FloorToInt(f) % EdgeBins, b = (a + 1) % EdgeBins;
        float foot = Mathf.Lerp(g.Foot[a], g.Foot[b], f - Mathf.Floor(f));
        return d2 <= foot * foot;
    }

    // 在城的轮廓线附近(护城河只在这一圈里找)
    public static bool NearOutline(Geometry g, WorldTile tile)
    {
        if (g == null) return true;
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy, d2 = dx * dx + dy * dy, lo = Mathf.Max(0f, g.FootMin - 2f), hi = g.FootMax + 2f;
        return d2 >= lo * lo && d2 <= hi * hi;
    }

    private static bool Urban(Geometry g, WorldTile tile) =>
        tile?.zone != null && tile.zone.city == g.City && g.Built.Contains(tile.zone.id) &&
        (g.Landmark.Contains(tile.zone.id) || InTown(g, tile));

    // 一条城郊的路：起点、方向(单位向量)、长度、弯的幅度与疏密；出城大路(Gate)两旁有关厢
    public sealed class Road
    {
        public float X, Y, Dx, Dy, Amp, Freq, Phase;
        public int Length;
        public bool Gate;
    }

    // 城郊：路、关厢、镇、村与小路，都只落在本城没建成的平地上。种子定：
    //   · 出城大路：四门里哪几门有(档位 0 没有、1 两门相对、2 起多数城门有)，各多长、弯得多大多密；
    //   · 岔路：从大路出城几格处分出去，斜着走，档位越高越多；
    //   · 镇落在大路、岔路上，村散在路网之间、各有一条小路(先竖后横)通到最近的路
    private static void PlaceCountryside(Geometry g)
    {
        int tier = g.Tier;
        g.Suburb = tier >= 2 ? 4 * (tier - 1) : 0;
        int first = (int)(H(g.Seed, 60) * 4);
        var gates = new List<Road>();
        for (int k = 0; k < 4; k++)
        {
            bool open = tier >= 2 ? k == first || H(g.Seed, k, 80) < 0.78f : tier == 1 && (k == first || k == (first + 2) % 4);
            if (!open) continue;
            // 城门到城外：顺着这个方向走出城的轮廓
            int step = 0;
            while (step < 80 && Urban(g, World.world.GetTile(g.GateX + AxisX[k] * step, g.GateY + AxisY[k] * step))) step++;
            var road = new Road
            {
                X = g.GateX + AxisX[k] * step, Y = g.GateY + AxisY[k] * step, Dx = AxisX[k], Dy = AxisY[k], Gate = true,
                Length = Mathf.RoundToInt((14 + 8 * tier) * (0.7f + 0.5f * H(g.Seed, k, 83))),
                Amp = 1f + 3f * H(g.Seed, k, 81), Freq = 0.05f + 0.13f * H(g.Seed, k, 82), Phase = H(g.Seed, k, 84) * TwoPi
            };
            g.Roads.Add(road);
            gates.Add(road);
        }
        int branches = gates.Count == 0 ? 0 : tier switch { 2 => (int)(H(g.Seed, 85) * 2f), 3 => 1 + (int)(H(g.Seed, 85) * 2f), 4 => 1 + (int)(H(g.Seed, 85) * 3f), _ => 0 };
        for (int b = 0; b < branches; b++)
        {
            Road parent = gates[(int)(H(g.Seed, b, 86) * gates.Count) % gates.Count];
            int from = 4 + (int)(H(g.Seed, b, 87) * Mathf.Max(1, Mathf.Min(12, parent.Length - 6)));
            (int px, int py) = Point(parent, from);
            float turn = (H(g.Seed, b, 88) < 0.5f ? -1f : 1f) * (0.6f + 0.45f * H(g.Seed, b, 89));
            float angle = Mathf.Atan2(parent.Dy, parent.Dx) + turn;
            g.Roads.Add(new Road
            {
                X = px, Y = py, Dx = Mathf.Cos(angle), Dy = Mathf.Sin(angle),
                Length = 10 + (int)(H(g.Seed, b, 90) * (6 + 4 * tier)),
                Amp = 0.8f + 2f * H(g.Seed, b, 91), Freq = 0.06f + 0.12f * H(g.Seed, b, 92), Phase = H(g.Seed, b, 93) * TwoPi
            });
        }
        // 镇：落在路上(大路出城十格以外、岔路五格以外)
        int towns = tier switch { 2 => 1, 3 => 2 + (H(g.Seed, 61) < 0.5f ? 1 : 0), 4 => 3 + (H(g.Seed, 61) < 0.5f ? 1 : 0), _ => 0 };
        for (int attempt = 0; attempt < 16 && g.Towns.Count < towns && g.Roads.Count > 0; attempt++)
        {
            Road road = g.Roads[(first + attempt) % g.Roads.Count];
            int start = road.Gate ? 10 : 5;
            if (road.Length <= start) continue;
            (int x, int y) = Point(road, start + (int)(H(g.Seed, attempt, 62) * (road.Length - start)));
            int size = tier >= 3 ? (H(g.Seed, attempt, 63) < 0.5f ? 4 : 5) : 3;
            if (!Open(g, x, y) || g.Towns.Exists(t => Mathf.Abs(t.x - x) + Mathf.Abs(t.y - y) < t.size + size + 6)) continue;
            g.Towns.Add((x, y, size, tier >= 3 && H(g.Seed, attempt, 64) < 0.45f));
        }
        // 村：散在路网之间，离城心十二格以外
        int reach = 14 + 8 * tier;
        int villages = tier switch { 0 => 1, 1 => 2, 2 => 4, 3 => 6, _ => 8 } + (int)(H(g.Seed, 65) * (tier >= 4 ? 4 : 3));
        for (int attempt = 0; attempt < villages * 5 && g.Villages.Count < villages; attempt++)
        {
            float a = H(g.Seed, attempt, 66) * TwoPi;
            float dist = 12f + H(g.Seed, attempt, 67) * (reach - 2f);
            int x = Mathf.RoundToInt(g.GateX + dist * Mathf.Cos(a)), y = Mathf.RoundToInt(g.GateY + dist * Mathf.Sin(a));
            if (!Open(g, x, y)) continue;
            if (g.Towns.Exists(t => Mathf.Abs(t.x - x) + Mathf.Abs(t.y - y) < t.size + 8)) continue;
            if (g.Villages.Exists(v => Mathf.Abs(v.x - x) + Mathf.Abs(v.y - y) < 9)) continue;
            // 离路太近的不要(会压在路上)
            int best = 15, bx = 0, by = 0;
            foreach (Road road in g.Roads)
                for (int d = 0; d <= road.Length; d++)
                {
                    (int rx, int ry) = Point(road, d);
                    int length = Mathf.Abs(rx - x) + Mathf.Abs(ry - y);
                    if (length >= best) continue;
                    best = length;
                    bx = rx;
                    by = ry;
                }
            if (best < 3) continue;
            g.Villages.Add((x, y));
            if (best >= 15) continue;
            // 小路：先竖着走到路的那一行，再横着走过去
            g.Paths.Add((true, x, y, by));
            if (bx != x) g.Paths.Add((false, by, x, bx));
        }
    }

    private static bool Open(Geometry g, int x, int y)
    {
        WorldTile tile = World.world.GetTile(x, y);
        return tile?.zone != null && tile.zone.city == g.City && !g.Built.Contains(tile.zone.id) && tile.Type != null &&
               !tile.Type.liquid && !Rough(tile);
    }

    // 路上第 d 格：起点后慢慢弯起来
    private static float Wobble(Road road, float d) => road.Amp * Mathf.Sin(d * road.Freq + road.Phase) * Mathf.Clamp01(d / 8f);

    private static (int x, int y) Point(Road road, int d)
    {
        float w = Wobble(road, d);
        return (Mathf.RoundToInt(road.X + road.Dx * d - road.Dy * w), Mathf.RoundToInt(road.Y + road.Dy * d + road.Dx * w));
    }

    #endregion

    #region 每格

    private static float Angle(float dx, float dy)
    {
        float a = Mathf.Atan2(dy, dx);
        return a < 0f ? a + TwoPi : a;
    }

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
        if (Urban(g, tile)) return true;
        if (!g.Modern) return false;
        (Kind kind, bool developed) = ZoneOf(g, tile.zone);
        if (!developed || kind == Kind.Farm) return false;
        if (InEdge(g, tile)) return true;
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy, edge = EdgeAt(g, Angle(dx, dy)) * 1.25f;
        return ZonePlanSystem.Get(g.City, tile.zone) != ZoneUse.None && dx * dx + dy * dy < edge * edge;
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
        if (!g.Modern) return !built && use != ZoneUse.Farm && use != ZoneUse.Reserve && Countryside(g, tile, color, out result);
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
        // 开发了的地块：画城区范围里的部分；规划过的可以稍稍越出城区
        if (developed && (inEdge || use != ZoneUse.None && r < edge * 1.25f))
        {
            result = Block(g, tile, color, kind, r, edge);
            return true;
        }
        if (built) return false;
        return Countryside(g, tile, color, out result);
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

    // 城郊一格：出城大路 > 关厢 > 镇 > 村 > 小路 > 林子 > 田块；其余照原样
    internal sealed class Rural
    {
        public readonly List<int> Roads = new(), Towns = new(), Villages = new(), Paths = new();
        // 地块里离主城、镇、村最近的距离(格)：远了既没有田也没有林子
        public float Near;
    }

    private const float FieldReach = 18.2f, WoodReach = 24f;

    private static Rural RuralOf(Geometry g, TileZone zone)
    {
        if (g.Rurals.TryGetValue(zone.id, out Rural rural)) return rural;
        rural = new Rural();
        int size = Config.CITY_ZONE_SIZE;
        int x0 = zone.x * size, y0 = zone.y * size, x1 = x0 + size - 1, y1 = y0 + size - 1;
        bool Overlaps(int ax0, int ay0, int ax1, int ay1) => ax0 <= x1 && ax1 >= x0 && ay0 <= y1 && ay1 >= y0;
        for (int i = 0; i < g.Roads.Count; i++)
        {
            // 路连两旁关厢占的范围(路会弯，两旁各再留几格)
            Road road = g.Roads[i];
            float ex = road.X + road.Dx * road.Length, ey = road.Y + road.Dy * road.Length;
            int margin = Mathf.CeilToInt(road.Amp) + 3;
            if (Overlaps(Mathf.FloorToInt(Mathf.Min(road.X, ex)) - margin, Mathf.FloorToInt(Mathf.Min(road.Y, ey)) - margin,
                    Mathf.CeilToInt(Mathf.Max(road.X, ex)) + margin, Mathf.CeilToInt(Mathf.Max(road.Y, ey)) + margin))
                rural.Roads.Add(i);
        }
        for (int i = 0; i < g.Towns.Count; i++)
        {
            (int x, int y, int s, _) = g.Towns[i];
            if (Overlaps(x - s, y - s, x + s, y + s)) rural.Towns.Add(i);
        }
        for (int i = 0; i < g.Villages.Count; i++)
        {
            (int x, int y) = g.Villages[i];
            if (Overlaps(x - 2, y - 2, x + 2, y + 2)) rural.Villages.Add(i);
        }
        for (int i = 0; i < g.Paths.Count; i++)
        {
            (bool vertical, int at, int from, int to) = g.Paths[i];
            int lo = Mathf.Min(from, to), hi = Mathf.Max(from, to);
            if (vertical ? Overlaps(at, lo, at, hi) : Overlaps(lo, at, hi, at)) rural.Paths.Add(i);
        }
        float cx = x0 + size / 2f - 0.5f, cy = y0 + size / 2f - 0.5f, dx = cx - g.Cx, dy = cy - g.Cy;
        float near = Mathf.Sqrt(dx * dx + dy * dy) - EdgeAt(g, Angle(dx, dy));
        foreach ((int x, int y, int s, _) in g.Towns) near = Mathf.Min(near, Mathf.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y)) - s);
        foreach ((int x, int y) in g.Villages) near = Mathf.Min(near, Mathf.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y)) - 2f);
        rural.Near = near - size * 0.71f;
        g.Rurals[zone.id] = rural;
        return rural;
    }

    // 城郊的路、关厢、镇、村、小路(规划视图画在黑底上；田块、林子不算)
    public static bool Feature(Geometry g, WorldTile tile)
    {
        if (g == null || tile?.zone == null || g.Built.Contains(tile.zone.id) && InTown(g, tile)) return false;
        Rural rural = RuralOf(g, tile.zone);
        if (rural.Roads.Count == 0 && rural.Towns.Count == 0 && rural.Villages.Count == 0 && rural.Paths.Count == 0) return false;
        return Countryside(g, tile, new Color32(128, 128, 128, 255), out _, true);
    }

    private static bool Countryside(Geometry g, WorldTile tile, Color32 color, out Color32 result, bool featuresOnly = false)
    {
        result = color;
        Rural rural = RuralOf(g, tile.zone);
        foreach (int index in rural.Roads)
        {
            Road road = g.Roads[index];
            float rx = tile.x - road.X, ry = tile.y - road.Y;
            float along = rx * road.Dx + ry * road.Dy;
            if (along < -0.5f || along > road.Length + 0.5f) continue;
            float side = -rx * road.Dy + ry * road.Dx - Wobble(road, along);
            // 一格宽：横平竖直的路正好一格，斜的路连成斜线
            if (Mathf.Abs(side) < 0.5f * Mathf.Max(Mathf.Abs(road.Dx), Mathf.Abs(road.Dy)) + 0.15f)
            {
                result = Paint(color, g.Modern ? Local : Dirt, 0.85f);
                return true;
            }
            // 关厢：门外大路两旁一户一户排开，后面是院子
            int offset = Mathf.Abs(Mathf.RoundToInt(side)), step = Mathf.RoundToInt(along);
            if (road.Gate && g.Suburb > 0 && offset <= 2 && step >= 2 && step <= 1 + g.Suburb && step % 3 != 0)
            {
                result = offset == 1 ? Paint(color, Roof(g, tile), 0.9f) : Paint(color, Yard, 0.4f);
                return true;
            }
        }
        foreach (int index in rural.Towns)
        {
            (int x, int y, int size, bool walled) = g.Towns[index];
            int dx = tile.x - x, dy = tile.y - y;
            float d = Mathf.Sqrt(dx * dx + dy * dy), reach = size + 0.4f + 0.8f * (H(g.Seed, x, y, (int)(Angle(dx, dy) * 2f)) - 0.5f);
            if (d > reach) continue;
            result = TownTile(g, tile, color, dx, dy, d, reach, walled);
            return true;
        }
        foreach (int index in rural.Villages)
        {
            (int x, int y) = g.Villages[index];
            int dx = tile.x - x, dy = tile.y - y;
            if (dx * dx + dy * dy > 6) continue;
            // 几户人家错落着，院落围着
            result = H(g.Seed, tile.x, tile.y, 22) < 0.4f ? Paint(color, Roof(g, tile), 0.9f) : Paint(color, Yard, 0.5f);
            return true;
        }
        foreach (int index in rural.Paths)
        {
            (bool vertical, int fixedAt, int from, int to) = g.Paths[index];
            int at = vertical ? tile.x : tile.y, run = vertical ? tile.y : tile.x;
            if (at != fixedAt || run < Mathf.Min(from, to) || run > Mathf.Max(from, to)) continue;
            result = Paint(color, Lane, 0.6f);
            return true;
        }
        if (featuresOnly || rural.Near > WoodReach || Rough(tile)) return false;
        // 林子：一小片一小片，围在城镇村近处
        if (Noise(tile.x, tile.y, 7f, g.Seed + 20) > 0.76f)
        {
            result = Paint(color, H(g.Seed, tile.x, tile.y, 21) < 0.3f ? Tree : Grove, 0.55f);
            return true;
        }
        // 田块：大小不一，每块一种作物色，围着城镇村，越远越淡；越富的地方田越多
        if (rural.Near > FieldReach || tile.Type == null || !tile.Type.can_be_farm && !tile.Type.farm_field) return false;
        float dxc = tile.x - g.Cx, dyc = tile.y - g.Cy;
        float near = Mathf.Sqrt(dxc * dxc + dyc * dyc) - EdgeAt(g, Angle(dxc, dyc));
        foreach ((int x, int y, int size, _) in g.Towns)
            near = Mathf.Min(near, Mathf.Sqrt((tile.x - x) * (tile.x - x) + (tile.y - y) * (tile.y - y)) - size);
        foreach ((int x, int y) in g.Villages)
            near = Mathf.Min(near, Mathf.Sqrt((tile.x - x) * (tile.x - x) + (tile.y - y) * (tile.y - y)) - 2f);
        float strength = 0.35f * Mathf.Clamp01((FieldReach - near) / 14f) * (0.6f + 0.1f * g.Tier);
        if (strength <= 0.01f) return false;
        float fx = tile.x + 4f * Noise(tile.x, tile.y, 13f, g.Seed + 1), fy = tile.y + 4f * Noise(tile.x, tile.y, 13f, g.Seed + 2);
        int by = Mathf.FloorToInt(fy / 6f);
        int bx = Mathf.FloorToInt(fx / (5f + (int)(H(g.Seed, by) * 4)));
        result = Paint(color, Crops[(int)(H(g.Seed, bx, by, 5) * 5) % 5], strength);
        return true;
    }

    // 农耕区(城市规划划的农田)：不是整块地齐刷刷的横条，而是大小不一的田块拼起来——每块一种作物色、
    // 垄沟横、竖、斜或是草场，田块之间有田埂；挨着不是农田的地方边缘参差，留出几格原野
    public static Color32 Farmland(Geometry g, WorldTile tile, Color32 color)
    {
        TileZone zone = tile.zone;
        if (zone == null) return color;
        int size = Config.CITY_ZONE_SIZE, lx = tile.x - zone.x * size, ly = tile.y - zone.y * size;
        float edge = float.MaxValue;
        if (!IsFarm(g, zone.x - 1, zone.y)) edge = Mathf.Min(edge, lx);
        if (!IsFarm(g, zone.x + 1, zone.y)) edge = Mathf.Min(edge, size - 1 - lx);
        if (!IsFarm(g, zone.x, zone.y - 1)) edge = Mathf.Min(edge, ly);
        if (!IsFarm(g, zone.x, zone.y + 1)) edge = Mathf.Min(edge, size - 1 - ly);
        if (edge < 3f * Noise(tile.x, tile.y, 4f, g.Seed + 30) - 0.5f) return color;
        // 田块：先把坐标扭一扭，再按行切、每行按不同宽度切
        float fx = tile.x + 3f * Noise(tile.x, tile.y, 11f, g.Seed + 31), fy = tile.y + 3f * Noise(tile.x, tile.y, 11f, g.Seed + 32);
        float rowHeight = 4f + 3f * H(g.Seed, Mathf.FloorToInt(fx / 16f), 33);
        int row = Mathf.FloorToInt(fy / rowHeight);
        float width = 4f + 5f * H(g.Seed, row, 34);
        int column = Mathf.FloorToInt((fx + row * 2.3f) / width);
        float inRow = fy - row * rowHeight, inColumn = fx + row * 2.3f - column * width;
        // 田埂
        if (inRow < 0.8f || inColumn < 0.8f) return Mix(color, Lane, 0.3f);
        float seed = H(g.Seed, column, row, 35);
        Color32 crop = Crops[(int)(seed * 5) % 5];
        int furrow = (int)(H(g.Seed, column, row, 36) * 4);
        bool line = furrow switch
        {
            0 => tile.y % 2 == 0,
            1 => tile.x % 2 == 0,
            2 => (tile.x + tile.y) % 3 == 0,
            _ => false
        };
        return Mix(color, line ? Mix(crop, new Color32(90, 80, 60, 255), 0.35f) : crop, line ? 0.42f : 0.3f);
    }

    private static bool IsFarm(Geometry g, int zx, int zy)
    {
        TileZone zone = World.world.zone_calculator.getZone(zx, zy);
        return zone != null && zone.city == g.City && ZonePlanSystem.Get(g.City, zone) == ZoneUse.Farm;
    }

    // 镇：圆圆的一片，十字街、中间小集市，屋舍错落；富庶的镇有一圈矮墙，四面开门
    private static Color32 TownTile(Geometry g, WorldTile tile, Color32 color, int dx, int dy, float d, float reach, bool walled)
    {
        if (walled && d > reach - 1f)
            return dx == 0 || dy == 0 ? Paint(color, TownStreet, 0.8f) : Paint(color, TownWall, 0.85f);
        if (dx == 0 || dy == 0) return Paint(color, Mathf.Abs(dx) + Mathf.Abs(dy) <= 1 ? Market : TownStreet, 0.85f);
        if (Mathf.Abs(dx) == 1 && Mathf.Abs(dy) == 1) return Paint(color, Market, 0.6f);
        return H(g.Seed, tile.x, tile.y, 23) < 0.62f ? Paint(color, Roof(g, tile), 0.9f) : Paint(color, Yard, 0.4f);
    }

    // 屋顶：工业化以前是瓦房，之后多是楼房
    private static Color32 Roof(Geometry g, WorldTile tile) =>
        g.Progress >= 1f && H(g.Seed, tile.x, tile.y, 13) < 0.6f
            ? Slab[(int)(H(g.Seed, tile.x, tile.y, 15) * 4) % 4]
            : HouseRoof[(int)(H(g.Seed, tile.x >> 1, tile.y >> 1, 14) * 4) % 4];

    #endregion
}
