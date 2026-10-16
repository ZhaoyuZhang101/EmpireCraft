using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
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
        // 游牧：蒙古包营地，不修路、不起楼、没有田(城外只散着营地)
        public bool Nomad;
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
        // 村镇的小路：一条条弯弯曲曲的折线(格坐标，见 PlaceTrails)
        public readonly List<Vector2[]> Paths = new();
        // 毁于兵火的镇村(见 RuralSettlementSystem)；镇村的指纹(平面图的指纹里算进去)
        public readonly List<(int x, int y, bool town)> Ruins = new();
        public int SettlementHash;
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
    // 高德式平面图：城区清楚地浮在国色上(仍带一点国色)；城市图层画在地形上，再浓一些
    private static float Strength => Vivid ? 0.9f : 0.85f;
    public static bool Vivid;

    // 高德式配色(2026-10-14 用户选定)：环路放射干道橙黄、街道白、街区浅灰、楼是灰色占地块、公园浅绿、水浅蓝；
    // 城外只留村镇(浅灰小片)与出城大路(白)，不画田块林子
    private static readonly Color32 Asphalt = new(255, 190, 96, 255), BridgeDeck = new(236, 236, 238, 255),
        Local = new(255, 255, 255, 255), Rail = new(150, 150, 156, 255), Tie = new(232, 232, 234, 255),
        Station = new(220, 120, 100, 255), Park = new(196, 228, 180, 255), Tree = new(170, 212, 152, 255),
        Shadow = new(196, 196, 200, 255), Yard = new(222, 234, 206, 255), Market = new(250, 224, 196, 255),
        TownStreet = new(255, 255, 255, 255), Chimney = new(150, 146, 146, 255), Parade = new(226, 230, 214, 255),
        Barrack = new(208, 216, 198, 255), White = new(255, 255, 255, 255), Dirt = new(255, 255, 255, 255),
        Lane = new(250, 249, 246, 255), TownWall = new(170, 160, 150, 255), Grove = new(170, 212, 152, 255),
        Settlement = new(238, 236, 231, 255), Arterial = new(255, 222, 128, 255), LotGrey = new(243, 241, 237, 255);
    // 规划农田：浅米色田块(绿色叠在红、紫的国色上会混成脏土色)，田埂白
    private static readonly Color32[] Crops =
    {
        new(236, 238, 222, 255), new(230, 236, 214, 255), new(240, 240, 228, 255), new(232, 238, 218, 255),
        new(238, 236, 220, 255)
    };
    private static readonly int[] AxisX = { 1, 0, -1, 0 }, AxisY = { 0, 1, 0, -1 };
    private static readonly Color32[] Glass =
        { new(176, 190, 206, 255), new(190, 202, 214, 255), new(166, 180, 196, 255), new(200, 210, 222, 255) };
    private static readonly Color32[] Concrete =
        { new(214, 210, 200, 255), new(190, 186, 178, 255), new(226, 222, 214, 255), new(170, 168, 164, 255) };
    private static readonly Color32[] Slab =
        { new(214, 212, 208, 255), new(206, 204, 200, 255), new(222, 220, 216, 255), new(210, 206, 200, 255) };
    private static readonly Color32[] HouseRoof =
        { new(226, 222, 216, 255), new(218, 214, 208, 255), new(232, 228, 222, 255), new(222, 216, 206, 255) };
    private static readonly Color32[] Factory =
        { new(226, 224, 232, 255), new(216, 216, 226, 255), new(206, 210, 222, 255), new(230, 228, 236, 255) };

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

    private static Color32 Darken(Color32 color, float k) =>
        new((byte)(color.r * k), (byte)(color.g * k), (byte)(color.b * k), color.a);

    // 空地：街区底色
    private static Color32 Lot(Color32 color) => Color32.Lerp(color, LotGrey, 0.72f);

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
            Perimeter = family is "western" or "roma" or "viking",
            Nomad = family == "nomad"
        };
        // 发展快慢：种子定一个底(0.7~1.3 倍)，城大的、都城快些
        float pace = (0.7f + 0.6f * H(seed, 50)) * (0.85f + 0.3f * Mathf.Clamp01(built.Count / 12f)) *
                     (city.isCapitalCity() ? 1.15f : 1f);
        // 游牧的城一直是营地：不拆墙修环路、不起楼、不通铁路
        g.Progress = g.Nomad ? 0f : ProgressOf(culture, pace);
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
        // 蔓延：工业化以后城区往外长，种子定哪几个方向长得远(沿一圈连续起伏，没有接缝)。
        // 长多远照演示版(工业化满了约一倍半，电气约一倍八，现代约两倍二)；以前只长到一倍一到一倍四，城小得放不下新城区
        float grow = 0.45f * Mathf.Clamp01(g.Progress) + 0.4f * Mathf.Clamp01(g.Progress - 1f) +
                     0.35f * Mathf.Clamp01(g.Progress - 2f);
        for (int i = 0; i < EdgeBins; i++)
        {
            float a = (i + 0.5f) / EdgeBins * TwoPi;
            float lobe = Noise(10f + 3f * Mathf.Cos(a), 10f + 3f * Mathf.Sin(a), 1.6f, seed + 3);
            float jitter = 0.92f + 0.16f * Noise(20f + 6f * Mathf.Cos(a), 20f + 6f * Mathf.Sin(a), 1.3f, seed + 4);
            g.Edge[i] = (g.Foot[i] + 2f) * jitter * (1f + grow * (0.3f + 1.4f * lobe));
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

    // 一条城郊的路：起点、方向(单位向量)、长度、弯的幅度与疏密；出城大路(Gate)两旁有关厢；
    // 官道(Link，两城之间共用的路，见 Links)与出城大路一样宽，两头都不弯
    public sealed class Road
    {
        public float X, Y, Dx, Dy, Amp, Freq, Phase;
        public int Length;
        public bool Gate, Link;
    }

    // 城郊：路、关厢、镇、村与小路，都只落在本城没建成的平地上。种子定：
    //   · 出城大路：四门里哪几门有(档位 0 没有、1 两门相对、2 起多数城门有)，各多长、弯得多大多密；
    //   · 岔路：从大路出城几格处分出去，斜着走，档位越高越多；
    //   · 镇落在大路、岔路上，村散在路网之间，小路弯弯曲曲通到最近的路、镇或城门，近处的村彼此也有小路(见 PlaceTrails)
    private static void PlaceCountryside(Geometry g)
    {
        int tier = g.Tier;
        if (g.Nomad)
        {
            PlaceCamps(g);
            return;
        }
        g.Suburb = tier >= 2 ? 4 * (tier - 1) : 0;
        int first = (int)(H(g.Seed, 60) * 4);
        // 路网的路从哪几个方向出城(修通的、在修的)：这些方向不再另画出城大路，免得两条路并排走
        var linked = new List<float>();
        foreach (KeyValuePair<(long, long), RoadLink> pair in RoadBuildingSystem.All)
        {
            if (pair.Value == null || pair.Value.grade <= 0 && pair.Value.building <= 0) continue;
            long other = pair.Key.Item1 == g.City.data.id ? pair.Key.Item2 : pair.Key.Item2 == g.City.data.id ? pair.Key.Item1 : -1L;
            WorldTile there = other < 0 ? null : World.world.cities.get(other)?.getTile();
            if (there != null) linked.Add(Angle(there.x - g.GateX, there.y - g.GateY));
        }
        // 镇、村的落点照旧按原来的出城大路、岔路定(官道是后加的，村镇不因此挪地方)；画出来的路在后面另算
        GateRoads(g, linked, false);
        // 镇村是固定的地方(见 RuralSettlementSystem)：先摆上存档里落在本城地上的；被城区盖住的不画、不算数，
        // 毁于兵火的画成废墟、照样算数；不够数才照下面原来的选址办法新建，建了就记进存档
        int townCount = 0, villageCount = 0;
        unchecked
        {
            foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(g.City))
            {
                if (!Open(g, settlement.x, settlement.y)) continue;
                g.SettlementHash = g.SettlementHash * 31 + settlement.id * 7 + (settlement.ruined_at >= 0d ? 3 : 1);
                if (settlement.town) townCount++;
                else villageCount++;
                if (settlement.ruined_at >= 0d) g.Ruins.Add((settlement.x, settlement.y, settlement.town));
                else if (settlement.town) g.Towns.Add((settlement.x, settlement.y, settlement.size, settlement.walled));
                else g.Villages.Add((settlement.x, settlement.y));
            }
        }
        bool Taken(int x, int y, int room) =>
            g.Towns.Exists(t => Mathf.Abs(t.x - x) + Mathf.Abs(t.y - y) < t.size + room) ||
            g.Villages.Exists(v => Mathf.Abs(v.x - x) + Mathf.Abs(v.y - y) < room) ||
            g.Ruins.Exists(r => Mathf.Abs(r.x - x) + Mathf.Abs(r.y - y) < room);
        // 镇：落在路上(大路出城十格以外、岔路五格以外)；离路口多远只看种子，不随路的长短(档位)变
        int towns = tier switch { 2 => 1, 3 => 2 + (H(g.Seed, 61) < 0.5f ? 1 : 0), 4 => 3 + (H(g.Seed, 61) < 0.5f ? 1 : 0), _ => 0 };
        for (int attempt = 0; attempt < 16 && townCount < towns && g.Roads.Count > 0; attempt++)
        {
            Road road = g.Roads[(first + attempt) % g.Roads.Count];
            int start = road.Gate ? 10 : 5, at = start + (int)(H(g.Seed, attempt, 62) * 30f);
            if (at > road.Length) continue;
            (int x, int y) = Point(road, at);
            int size = tier >= 3 ? (H(g.Seed, attempt, 63) < 0.5f ? 4 : 5) : 3;
            if (!Open(g, x, y) || g.Towns.Exists(t => Mathf.Abs(t.x - x) + Mathf.Abs(t.y - y) < t.size + size + 6) ||
                Taken(x, y, size + 4)) continue;
            bool walled = tier >= 3 && H(g.Seed, attempt, 64) < 0.45f;
            g.Towns.Add((x, y, size, walled));
            townCount++;
            RuralSettlementSystem.Add(x, y, true, size, walled);
        }
        // 村：散在路网之间，离城心十二格以外。村址是固定的一处处(每 11×11 格一处，随机偏一点，约四分之三有村)，
        // 取本城没建成的平地上、够得着的，由近及远取够数——城长大了、档位变了，原有的村不挪地方，只是近处的被城吞进去、
        // 远处的多出来几座(用户 2026-10-16 "为什么每次更新城市后这些村子的位置会变"：以前按随机方位一个个试，
        // 城的轮廓、建成地块、档位一变，前面试中的不一样，整组村子都换了地方)
        int reach = 14 + 8 * tier;
        int villages = tier switch { 0 => 1, 1 => 2, 2 => 4, 3 => 6, _ => 8 } + (int)(H(g.Seed, 65) * (tier >= 4 ? 4 : 3));
        const int SiteCell = 11;
        float far = 12f + reach - 2f;
        var sites = new List<(int x, int y, float d)>();
        for (int cy = Mathf.FloorToInt((g.GateY - far) / SiteCell); cy <= Mathf.FloorToInt((g.GateY + far) / SiteCell); cy++)
        for (int cx = Mathf.FloorToInt((g.GateX - far) / SiteCell); cx <= Mathf.FloorToInt((g.GateX + far) / SiteCell); cx++)
        {
            if (H(g.Seed, cx, cy, 201) >= 0.75f) continue;
            int sx = cx * SiteCell + 2 + (int)(H(g.Seed, cx, cy, 202) * (SiteCell - 4));
            int sy = cy * SiteCell + 2 + (int)(H(g.Seed, cx, cy, 203) * (SiteCell - 4));
            float d = Mathf.Sqrt((sx - g.GateX) * (sx - g.GateX) + (sy - g.GateY) * (sy - g.GateY));
            if (d >= 12f && d <= far) sites.Add((sx, sy, d));
        }
        sites.Sort((p, q) => p.d.CompareTo(q.d));
        foreach ((int x, int y, _) in sites)
        {
            if (villageCount >= villages) break;
            if (!Open(g, x, y)) continue;
            if (g.Towns.Exists(t => Mathf.Abs(t.x - x) + Mathf.Abs(t.y - y) < t.size + 8)) continue;
            if (g.Villages.Exists(v => Mathf.Abs(v.x - x) + Mathf.Abs(v.y - y) < 9)) continue;
            if (g.Ruins.Exists(r => Mathf.Abs(r.x - x) + Mathf.Abs(r.y - y) < 9)) continue;
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
            villageCount++;
            RuralSettlementSystem.Add(x, y, false, 0, false);
        }
        // 画出来的路：两城之间的官道(见 Links)；通往邻城有官道的方向不再另画出城大路，出城大路、岔路走到本城地界以前收住
        List<(Road road, City other)> links = Links(g);
        var drawn = new List<float>(linked);
        foreach ((Road _, City other) in links)
            if (other?.getTile() is WorldTile there) drawn.Add(Angle(there.x - g.GateX, there.y - g.GateY));
        g.Roads.Clear();
        GateRoads(g, drawn, true);
        foreach ((Road road, City other) in links)
        {
            g.Roads.Add(road);
            if (other != null) AddLinkSpur(g, road);
        }
        PlaceTrails(g);
    }

    // 出城大路与岔路：linked 方向上不出大路；ownLand 为真时走到本城地界以前收住(画出来的路)，否则只遇山、城墙收住(定村镇落点用)
    private static void GateRoads(Geometry g, List<float> linked, bool ownLand)
    {
        int tier = g.Tier;
        int first = (int)(H(g.Seed, 60) * 4);
        var gates = new List<Road>();
        for (int k = 0; k < 4; k++)
        {
            bool open = tier >= 2 ? k == first || H(g.Seed, k, 80) < 0.78f : tier == 1 && (k == first || k == (first + 2) % 4);
            if (!open || Near(linked, Angle(AxisX[k], AxisY[k]), 45f)) continue;
            // 城门到城外：顺着这个方向走出城的轮廓
            int step = 0;
            while (step < 80 && Urban(g, World.world.GetTile(g.GateX + AxisX[k] * step, g.GateY + AxisY[k] * step))) step++;
            var road = new Road
            {
                X = g.GateX + AxisX[k] * step, Y = g.GateY + AxisY[k] * step, Dx = AxisX[k], Dy = AxisY[k], Gate = true,
                Length = Mathf.RoundToInt((14 + 8 * tier) * (0.7f + 0.5f * H(g.Seed, k, 83))),
                Amp = 1f + 3f * H(g.Seed, k, 81), Freq = 0.05f + 0.13f * H(g.Seed, k, 82), Phase = H(g.Seed, k, 84) * TwoPi
            };
            road.Length = Clip(g, road, ownLand);
            if (road.Length < 4) continue;
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
            var branch = new Road
            {
                X = px, Y = py, Dx = Mathf.Cos(angle), Dy = Mathf.Sin(angle),
                Length = 10 + (int)(H(g.Seed, b, 90) * (6 + 4 * tier)),
                Amp = 0.8f + 2f * H(g.Seed, b, 91), Freq = 0.06f + 0.12f * H(g.Seed, b, 92), Phase = H(g.Seed, b, 93) * TwoPi
            };
            branch.Length = Clip(g, branch, ownLand);
            if (branch.Length >= 4) g.Roads.Add(branch);
        }
    }

    // 小路(用户 2026-10-16："城市和小镇之间也没有细道路相连，也没有很自然")：以前只有离路十五格以内的村有路，
    // 而且先竖后横走成直角。现在每个村通到最近的路(大路、岔路)、镇或城门(四十格以内)，路弯弯曲曲(两段起伏叠在一起，
    // 遇水换个方向绕)；相距不远的村两两之间约一半也连一条
    private const int TrailReach = 40, NeighbourTrail = 16;

    private static void PlaceTrails(Geometry g)
    {
        var roadPoints = new List<Vector2>();
        foreach (Road road in g.Roads)
            for (int d = 0; d <= road.Length; d += 2)
            {
                (int rx, int ry) = Point(road, d);
                // 官道穿过别城的那几段不算(小路通过去就出了本城的地，画不出来)
                if (road.Link && World.world.GetTile(rx, ry)?.zone?.city != g.City) continue;
                roadPoints.Add(new Vector2(rx, ry));
            }
        var gate = new Vector2(g.GateX, g.GateY);
        // 镇：原来落脚的出城大路不画了(通往邻城改走官道)的，修一条小路通到最近的路或城门
        for (int t = 0; t < g.Towns.Count; t++)
        {
            (int tx, int ty, int size, _) = g.Towns[t];
            var at = new Vector2(tx, ty);
            Vector2 to = gate;
            float best = Vector2.Distance(at, gate);
            foreach (Vector2 point in roadPoints)
            {
                float d = Vector2.Distance(at, point);
                if (d >= best) continue;
                best = d;
                to = point;
            }
            if (best > size + 2f && best <= TrailReach) AddTrail(g, at, to, 5003L + t * 13L);
        }
        foreach ((int tx, int ty, int size, _) in g.Towns) roadPoints.Add(new Vector2(tx, ty));
        for (int i = 0; i < g.Villages.Count; i++)
        {
            (int x, int y) = g.Villages[i];
            var from = new Vector2(x, y);
            // 通到最近的路、镇或城门；最近的那处走不通(三成以上落在水里、山上)就换下一处，最多试四处
            // (用户 2026-10-16 "都城到村庄没有小路"：以前只试最近的一处，绕不过去就不修)
            var targets = new List<(Vector2 at, float d)> { (gate, Vector2.Distance(from, gate)) };
            foreach (Vector2 point in roadPoints) targets.Add((point, Vector2.Distance(from, point)));
            targets.Sort((p, q) => p.d.CompareTo(q.d));
            int tried = 0;
            Vector2 last = new Vector2(float.NaN, float.NaN);
            foreach ((Vector2 to, float d) in targets)
            {
                if (d > TrailReach || tried >= 4) break;
                if (d < 2f) break;
                // 同一条路上挨着的点算一处
                if (!float.IsNaN(last.x) && Vector2.Distance(last, to) < 6f) continue;
                last = to;
                tried++;
                if (AddTrail(g, from, to, PosKey(x, y) * 31L + 1 + tried * 7L)) break;
            }
            // 近处的村：约一半连一条(每对只连一次)
            for (int j = i + 1; j < g.Villages.Count; j++)
            {
                (int ox, int oy) = g.Villages[j];
                var other = new Vector2(ox, oy);
                // 按两村的位置定(镇村从存档读出来的先后会变，按序号定的话小路会换)
                long ka = PosKey(x, y), kb = PosKey(ox, oy);
                if (ka > kb) (ka, kb) = (kb, ka);
                if (Vector2.Distance(from, other) > NeighbourTrail || H(g.Seed, ka, kb, 95) >= 0.5f) continue;
                AddTrail(g, from, other, ka * 977L + kb);
            }
        }
    }

    // 一条小路：从 a 走到 b，路线围着直线左右起伏(一大一小两段起伏)；整条有三成以上落在水里、山上就换一边绕，还不行就不修
    private static bool AddTrail(Geometry g, Vector2 a, Vector2 b, long key)
    {
        float length = Vector2.Distance(a, b);
        if (length < 1.5f) return false;
        Vector2 dir = (b - a) / length, normal = new Vector2(-dir.y, dir.x);
        int steps = Mathf.Max(2, Mathf.CeilToInt(length * 2f));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            float sign = (H(g.Seed, key, 96) < 0.5f ? -1f : 1f) * (attempt == 0 ? 1f : -1f);
            float bend = sign * Mathf.Min(4f, length * (0.08f + 0.12f * H(g.Seed, key, 97)));
            float wiggle = 0.6f + 0.6f * H(g.Seed, key, 98), freq = 2f + Mathf.Floor(H(g.Seed, key, 99) * 3f);
            var points = new Vector2[steps + 1];
            int blocked = 0;
            for (int k = 0; k <= steps; k++)
            {
                float t = k / (float)steps, taper = Mathf.Sin(t * Mathf.PI);
                float offset = bend * taper + wiggle * taper * Mathf.Sin(t * freq * Mathf.PI + key % 7);
                Vector2 p = a + dir * (length * t) + normal * offset;
                points[k] = p;
                WorldTile tile = World.world.GetTile(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                if (tile?.Type == null || tile.Type.liquid || tile.Type.mountains) blocked++;
            }
            if (blocked * 10 > steps * 3) continue;
            g.Paths.Add(points);
            return true;
        }
        return false;
    }

    // 游牧城外：不修路，一处处蒙古包营地散在草场上，档位越高营地越多、散得越远
    private static void PlaceCamps(Geometry g)
    {
        int tier = g.Tier;
        g.Suburb = 0;
        int camps = tier switch { 0 => 2, 1 => 3, 2 => 5, 3 => 7, _ => 9 } + (int)(H(g.Seed, 65) * 3);
        int range = 14 + 8 * tier;
        for (int attempt = 0; attempt < camps * 6 && g.Villages.Count < camps; attempt++)
        {
            float a = H(g.Seed, attempt, 66) * TwoPi;
            float dist = 8f + H(g.Seed, attempt, 67) * (range - 2f);
            int x = Mathf.RoundToInt(g.GateX + dist * Mathf.Cos(a)), y = Mathf.RoundToInt(g.GateY + dist * Mathf.Sin(a));
            if (!Open(g, x, y)) continue;
            if (g.Villages.Exists(v => Mathf.Abs(v.x - x) + Mathf.Abs(v.y - y) < 7)) continue;
            g.Villages.Add((x, y));
        }
    }

    private static readonly Color32 Felt = new(240, 234, 218, 255), Trampled = new(196, 186, 140, 255);

    // 一处营地：几顶蒙古包围成一圈，正中一顶大的，地上踩出一片
    private static Color32 YurtCamp(Geometry g, WorldTile tile, Color32 color, int dx, int dy, int cx, int cy)
    {
        if (dx == 0 && dy == 0) return Paint(color, Felt, 1f);
        bool ring = Mathf.Abs(dx) + Mathf.Abs(dy) == 2 && (dx == 0 || dy == 0 || Mathf.Abs(dx) == 1);
        if (ring && H(g.Seed, cx * 7 + dx, cy * 7 + dy, 24) < 0.7f) return Paint(color, Felt, 0.9f);
        return Paint(color, Trampled, 0.3f);
    }

    // 城郊的路遇到山(山、高峰)或原版城墙就到此为止，丘陵照走
    // 出城大路、岔路：遇山、城墙为止；画出来的路走出本城的地以前就收住(离地界留一格)，不再一头撞断在地界上
    private static int Clip(Geometry g, Road road, bool ownLand)
    {
        for (int d = 0; d <= road.Length; d++)
        {
            (int x, int y) = Point(road, d);
            WorldTile tile = World.world.GetTile(x, y);
            if (tile?.Type == null || tile.Type.mountains || tile.Type.block || tile.Type.wall || MapWallSystem.Contains(tile))
                return Mathf.Max(0, d - 1);
            if (ownLand && tile.zone?.city != g.City) return Mathf.Max(0, d - 2);
        }
        return road.Length;
    }

    #region 官道

    // 两城之间的官道(用户 2026-10-17 "为什么这些线跨过城市边界就连不上了"：以前城郊的路只是各城按自己的种子朝城门方向
    // 随手伸一段，又只画在本城的地上，出了地界就断，邻城之间并不相连)：
    //   · 哪两座城之间有官道只看全图的城心：相距不过 LinkReach 格，且没有第三座城离两头都比它们彼此更近(相对邻近图：
    //     近邻相连、不绕远、不叠成一团)；不过山、不穿长城，过水至多 LinkFord 格(渡口)；路网已修通或在修的两城不另画；
    //   · 路的几何(从编号小的城心到编号大的城心，弯法按两城的种子)与哪座城来算无关；每座城把经过自己地块的官道都收进来，
    //     谁的地谁画，地界两边是同一条线，正好接上；
    //   · 两头：从离官道出城处最近的城门拉一段到官道上(见 AddLinkSpur)。
    private const int LinkReach = 56, LinkFord = 3;

    private static (long, long) PairKey(long a, long b) => a < b ? (a, b) : (b, a);

    // 一处镇村的位置键(画法、小路的种子按位置定，与先后、归哪座城无关)
    internal static long PosKey(int x, int y) => x * 73856093L ^ y * 19349663L;

    // 全图的官道(城市平面图每几秒就重算一次，官道只按城心、地形、路网定，缓存起来，城数变了或过半分钟再算)
    private sealed class CachedLink
    {
        public Road Road;
        public long A, B;
        public int X0, Y0, X1, Y1;
    }

    private static List<CachedLink> _links;
    private static float _linksAt = -1000f;
    private static long _linksKey;
    private const float LinkRefreshSeconds = 30f;

    private static List<CachedLink> AllLinks()
    {
        float now = Time.unscaledTime;
        var centers = new List<(City city, int x, int y)>();
        long key = 17;
        unchecked
        {
            foreach (City city in World.world.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                WorldTile tile = city.getTile();
                if (tile == null) continue;
                centers.Add((city, tile.x, tile.y));
                key = key * 31 + city.data.id * 7 + tile.x * 3 + tile.y;
            }
        }
        if (_links != null && key == _linksKey && now >= _linksAt && now - _linksAt < LinkRefreshSeconds) return _links;
        _linksKey = key;
        _linksAt = now;
        var networked = new HashSet<(long, long)>();
        foreach (KeyValuePair<(long, long), RoadLink> pair in RoadBuildingSystem.All)
            if (pair.Value != null && (pair.Value.grade > 0 || pair.Value.building > 0))
                networked.Add(PairKey(pair.Key.Item1, pair.Key.Item2));
        // 按 LinkReach 分格，只比相邻九格里的城
        var buckets = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < centers.Count; i++)
        {
            var cell = (Mathf.FloorToInt(centers[i].x / (float)LinkReach), Mathf.FloorToInt(centers[i].y / (float)LinkReach));
            if (!buckets.TryGetValue(cell, out List<int> list)) buckets[cell] = list = new List<int>();
            list.Add(i);
        }
        IEnumerable<int> Around(int x, int y)
        {
            int cx = Mathf.FloorToInt(x / (float)LinkReach), cy = Mathf.FloorToInt(y / (float)LinkReach);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (buckets.TryGetValue((cx + dx, cy + dy), out List<int> list))
                    foreach (int index in list) yield return index;
        }
        var result = new List<CachedLink>();
        for (int i = 0; i < centers.Count; i++)
        foreach (int j in Around(centers[i].x, centers[i].y))
        {
            if (j <= i) continue;
            var (ca, ax, ay) = centers[i];
            var (cb, bx, by) = centers[j];
            if (ca.data.id > cb.data.id)
            {
                (ca, cb) = (cb, ca);
                (ax, ay, bx, by) = (bx, by, ax, ay);
            }
            float dx = bx - ax, dy = by - ay, d2 = dx * dx + dy * dy;
            if (d2 > LinkReach * LinkReach || d2 < 36f || networked.Contains(PairKey(ca.data.id, cb.data.id))) continue;
            // 相对邻近：有第三座城离两头都更近，两城就经由它相连
            bool between = false;
            foreach (int k in Around(ax, ay))
            {
                var (cx, xx, xy) = centers[k];
                if (cx == ca || cx == cb) continue;
                float da = (xx - ax) * (xx - ax) + (xy - ay) * (xy - ay), db = (xx - bx) * (xx - bx) + (xy - by) * (xy - by);
                if (da < d2 && db < d2)
                {
                    between = true;
                    break;
                }
            }
            if (between) continue;
            float length = Mathf.Sqrt(d2);
            long sa = SeedOf(ca), sb = SeedOf(cb);
            var road = new Road
            {
                X = ax, Y = ay, Dx = dx / length, Dy = dy / length, Length = Mathf.RoundToInt(length), Link = true,
                Amp = 0.8f + 2.2f * H(sa, sb, 301), Freq = 0.04f + 0.08f * H(sa, sb, 302), Phase = H(sa, sb, 303) * TwoPi
            };
            bool clear = true;
            int water = 0;
            for (int d = 0; d <= road.Length && clear; d++)
            {
                (int x, int y) = Point(road, d);
                WorldTile tile = World.world.GetTile(x, y);
                if (tile?.Type == null || tile.Type.mountains || tile.Type.block || tile.Type.wall || MapWallSystem.Contains(tile))
                    clear = false;
                else if (tile.Type.liquid) clear = ++water <= LinkFord;
                else water = 0;
            }
            if (!clear) continue;
            int margin = Mathf.CeilToInt(road.Amp) + 1;
            result.Add(new CachedLink
            {
                Road = road, A = ca.data.id, B = cb.data.id, X0 = Mathf.Min(ax, bx) - margin, Y0 = Mathf.Min(ay, by) - margin,
                X1 = Mathf.Max(ax, bx) + margin, Y1 = Mathf.Max(ay, by) + margin
            });
        }
        _links = result;
        return result;
    }

    // 经过本城地块的官道；other 为官道另一头的城(本城是一头时)，穿城而过的为 null
    private static List<(Road road, City other)> Links(Geometry g)
    {
        var result = new List<(Road, City)>();
        City self = g.City;
        if (self?.getTile() == null || self.zones == null || self.zones.Count == 0) return result;
        int size = Config.CITY_ZONE_SIZE;
        int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = int.MinValue, by1 = int.MinValue;
        foreach (TileZone zone in self.zones)
        {
            bx0 = Mathf.Min(bx0, zone.x * size);
            by0 = Mathf.Min(by0, zone.y * size);
            bx1 = Mathf.Max(bx1, zone.x * size + size - 1);
            by1 = Mathf.Max(by1, zone.y * size + size - 1);
        }
        long id = self.data.id;
        foreach (CachedLink link in AllLinks())
        {
            if (link.X1 < bx0 || link.X0 > bx1 || link.Y1 < by0 || link.Y0 > by1) continue;
            bool end = link.A == id || link.B == id, mine = end;
            for (int d = 0; d <= link.Road.Length && !mine; d++)
            {
                (int x, int y) = Point(link.Road, d);
                if (World.world.GetTile(x, y)?.zone?.city == self) mine = true;
            }
            if (!mine) continue;
            City other = end ? World.world.cities.get(link.A == id ? link.B : link.A) : null;
            result.Add((link.Road, other));
        }
        return result;
    }

    // 官道的一头在本城：从城心沿官道走出城的轮廓，那里离哪座城门近就从那座城门拉一段过去(城门就在出城处的不必拉)
    private static void AddLinkSpur(Geometry g, Road link)
    {
        bool fromStart = Mathf.RoundToInt(link.X) == g.City.getTile()?.x && Mathf.RoundToInt(link.Y) == g.City.getTile()?.y;
        int exitX = int.MinValue, exitY = 0;
        for (int k = 0; k <= link.Length; k++)
        {
            (int x, int y) = Point(link, fromStart ? k : link.Length - k);
            if (Urban(g, World.world.GetTile(x, y))) continue;
            exitX = x;
            exitY = y;
            break;
        }
        if (exitX == int.MinValue) return;
        float best = float.MaxValue;
        int gx = g.GateX, gy = g.GateY;
        for (int k = 0; k < 4; k++)
        {
            int step = 0;
            while (step < 80 && Urban(g, World.world.GetTile(g.GateX + AxisX[k] * step, g.GateY + AxisY[k] * step))) step++;
            int x = g.GateX + AxisX[k] * step, y = g.GateY + AxisY[k] * step;
            float d = (x - exitX) * (x - exitX) + (y - exitY) * (y - exitY);
            if (d >= best) continue;
            best = d;
            gx = x;
            gy = y;
        }
        float length = Mathf.Sqrt(best);
        if (length < 2.5f) return;
        g.Roads.Add(new Road
        {
            X = gx, Y = gy, Dx = (exitX - gx) / length, Dy = (exitY - gy) / length, Length = Mathf.RoundToInt(length), Link = true,
            Amp = 0.6f, Freq = 0.1f, Phase = H(g.Seed, link.Length, 304) * TwoPi
        });
    }

    #endregion

    private static bool Open(Geometry g, int x, int y)
    {
        WorldTile tile = World.world.GetTile(x, y);
        return tile?.zone != null && tile.zone.city == g.City && !g.Built.Contains(tile.zone.id) && tile.Type != null &&
               !tile.Type.liquid && !Rough(tile) && !MapWallSystem.Contains(tile);
    }

    // 路上第 d 格：起点后慢慢弯起来
    private static float Wobble(Road road, float d) => road.Amp * Mathf.Sin(d * road.Freq + road.Phase) * Mathf.Clamp01(d / 8f) *
                                                       (road.Link ? Mathf.Clamp01((road.Length - d) / 8f) : 1f);

    // 城市精细层用(见 PureMapPatch.Detail)：路上第 d 格
    internal static (int x, int y) RoadPoint(Road road, int d) => Point(road, d);

    // 粗图把这一格画成这条路(一格宽，与 Countryside 同一个判定；精细图先把这些格还成地面，再画连续的路)
    internal static bool OnRoadTile(Road road, int x, int y)
    {
        float rx = x - road.X, ry = y - road.Y;
        float along = rx * road.Dx + ry * road.Dy;
        if (along < -0.5f || along > road.Length + 0.5f) return false;
        float side = -rx * road.Dy + ry * road.Dx - Wobble(road, along);
        return Mathf.Abs(side) < 0.5f * Mathf.Max(Mathf.Abs(road.Dx), Mathf.Abs(road.Dy)) + 0.15f;
    }

    // 路上第 d 格的连续坐标(不取整，精细图沿它画连续的路)
    internal static Vector2 RoadPointF(Road road, float d)
    {
        float w = Wobble(road, d);
        return new Vector2(road.X + road.Dx * d - road.Dy * w, road.Y + road.Dy * d + road.Dx * w);
    }

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
        // 按格刷的规划：一块地刷得最多的用途(至少四分之一块)定这块地(见 TilePlanSystem)
        PlanUse painted = TilePlanSystem.Majority(zone, 16);
        if (painted != PlanUse.None)
        {
            kind = painted switch
            {
                PlanUse.Industry => Kind.Industry,
                PlanUse.Commerce or PlanUse.Civic => g.Progress >= 2f ? Kind.Tower : Kind.Market,
                PlanUse.Military => Kind.Military,
                PlanUse.Park or PlanUse.Keep => Kind.Park,
                PlanUse.Farm => Kind.Farm,
                _ => g.Progress >= 2.2f && cbd ? Kind.Tower : Kind.Housing
            };
            if (use == ZoneUse.None) use = TilePlanSystem.ZoneUseOf(painted);
        }
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
        // 老城外、轮廓里的本城地块是新长出来的城区：一进工业化就整片开发，田地也盖上房子(照演示版)。
        // 以前按用途分先后——工厂先起、住宅要到电气以后、田地永远不盖，城里东一块厂房西一块空地，露出底下的国色
        bool newTown = g.Modern && !g.Built.Contains(zone.id) && r < edge;
        if (newTown && kind == Kind.Farm) kind = industrySide ? Kind.Industry : Kind.Housing;
        var result = (kind, g.Modern && (newTown && kind != Kind.Tower || g.Progress >= need));
        g.Zones[zone.id] = result;
        return result;
    }

    // 这一格画什么；返回 false 时照原来的画法(古代城区、还没开发的建成地块、都城地标、农田、山地)
    public static bool Tile(WorldTile tile, Color32 color, Geometry g, bool built, ZoneUse use, out Color32 result)
    {
        result = color;
        if (g == null || tile?.zone == null || g.Landmark.Contains(tile.zone.id)) return false;
        // 农耕区平常不画田块(见 PureMapPatch.Plan)，出城的路、镇照常画过去，不然路一过农田就断
        if (!g.Modern) return !built && use != ZoneUse.Reserve && Countryside(g, tile, color, out result);
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
        if (kind == Kind.Farm) return !built && Countryside(g, tile, color, out result);
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

    #region 城市精细层用(见 PureMapPatch.Modern)

    // 近代以后这一格是新城区的什么：主干道(环路、放射干道)、铁路、车站、开发了的街区；都不是为 None(照古城或城外画)。
    // 与 Tile 的判定一致，拉近拉远看到的是同一座城
    internal enum Part { None, Road, Rail, Station, Block }

    internal static Part PartAt(Geometry g, WorldTile tile, ZoneUse use, out Kind kind)
    {
        kind = Kind.Housing;
        if (g == null || !g.Modern || tile?.zone == null || tile.zone.city != g.City || g.Landmark.Contains(tile.zone.id))
            return Part.None;
        float dx = tile.x - g.Cx, dy = tile.y - g.Cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy), angle = Angle(dx, dy), edge = EdgeAt(g, angle);
        if (OnRing(g, r, edge) || r > g.OldRadius - 1f && r < edge + 1f && RayDistance(g, angle, r) < 0.6f) return Part.Road;
        if (g.Progress >= 0.4f)
        {
            float rail = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, g.RailDir * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
            if (rail < 0.5f && r > g.OldRadius + 1f && r < edge) return Part.Rail;
            if (rail < 2f && r > g.OldRadius + 1f && r < g.OldRadius + 4f) return Part.Station;
        }
        if (Rough(tile)) return Part.None;
        (Kind zoneKind, bool developed) = ZoneOf(g, tile.zone);
        kind = zoneKind;
        if (kind == Kind.Farm) return Part.None;
        bool inEdge = r < edge;
        if (kind == Kind.Park) return inEdge ? Part.Block : Part.None;
        return developed && (inEdge || use != ZoneUse.None && r < edge * 1.25f) ? Part.Block : Part.None;
    }

    // 这块地开发成新城区了(精细图按它定画多大)
    internal static bool ModernZone(Geometry g, TileZone zone)
    {
        if (g == null || !g.Modern || zone == null || g.Landmark.Contains(zone.id)) return false;
        (Kind kind, bool developed) = ZoneOf(g, zone);
        if (kind == Kind.Farm || !developed && kind != Kind.Park) return false;
        float dx = zone.x * Config.CITY_ZONE_SIZE + 4f - g.Cx, dy = zone.y * Config.CITY_ZONE_SIZE + 4f - g.Cy;
        float edge = EdgeAt(g, Angle(dx, dy)) * 1.25f + 8f;
        return dx * dx + dy * dy < edge * edge;
    }

    // 连续坐标(格)处城区的外缘半径
    internal static float EdgeOf(Geometry g, float x, float y) => EdgeAt(g, Angle(x - g.Cx, y - g.Cy));

    // 连续坐标(格)离环路、放射干道中线多远(格)；along 是沿路走的长度(画中线虚线用)
    internal static float RoadDistance(Geometry g, float x, float y, out float along)
    {
        float dx = x - g.Cx, dy = y - g.Cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy), angle = Angle(dx, dy), edge = EdgeAt(g, angle);
        float best = float.MaxValue;
        along = r;
        if (r > g.OldRadius - 1f && r < edge + 1f && g.Rays.Length > 0) best = RayDistance(g, angle, r);
        float ring = float.MaxValue;
        if (g.Progress >= 1.4f && g.MaxEdge > 18f && edge * 0.62f > g.OldRadius + 3f) ring = Mathf.Abs(r - edge * 0.62f);
        if (g.Progress >= 2.5f) ring = Mathf.Min(ring, Mathf.Abs(r - edge * 0.9f));
        if (ring < best)
        {
            best = ring;
            along = angle * r;
        }
        return best;
    }

    // 连续坐标(格)在铁路线上的位置：across 离中线(带符号)、along 离城心沿线多远；还没修铁路为 false
    internal static bool RailAt(Geometry g, float x, float y, out float across, out float along)
    {
        across = along = 0f;
        if (g == null || g.Progress < 0.4f) return false;
        float dx = x - g.Cx, dy = y - g.Cy, c = Mathf.Cos(g.RailDir), s = Mathf.Sin(g.RailDir);
        along = dx * c + dy * s;
        across = -dx * s + dy * c;
        return along > 0f;
    }

    #endregion

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
    // 城外画不画田块林子(高德式不画)
    private const bool FieldsAndGroves = false;

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
            Vector2[] trail = g.Paths[i];
            float lx = float.MaxValue, ly = float.MaxValue, hx = float.MinValue, hy = float.MinValue;
            foreach (Vector2 p in trail)
            {
                lx = Mathf.Min(lx, p.x);
                ly = Mathf.Min(ly, p.y);
                hx = Mathf.Max(hx, p.x);
                hy = Mathf.Max(hy, p.y);
            }
            if (Overlaps(Mathf.FloorToInt(lx), Mathf.FloorToInt(ly), Mathf.CeilToInt(hx), Mathf.CeilToInt(hy))) rural.Paths.Add(i);
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
                result = Mix(color, White, road.Gate || road.Link ? 0.9f : 0.72f);
                return true;
            }
            // 关厢：门外大路两旁一条浅灰的街市
            int offset = Mathf.Abs(Mathf.RoundToInt(side)), step = Mathf.RoundToInt(along);
            if (road.Gate && g.Suburb > 0 && offset <= 2 && step >= 2 && step <= 1 + g.Suburb)
            {
                result = Mix(color, Settlement, 0.72f);
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
            if (g.Nomad)
            {
                if (dx * dx + dy * dy > 6) continue;
                result = YurtCamp(g, tile, color, dx, dy, x, y);
                return true;
            }
            // 村不画(每座城外好几处，满地小点，看着乱)
            continue;
        }
        // 高德式：城外不画田块、林子(以前每座城一大圈，远看是噪点)；游牧城外是草场
        if (featuresOnly || g.Nomad || rural.Near > WoodReach || Rough(tile) || !FieldsAndGroves) return false;
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

    // 农耕区(城市规划划的农田；用户 2026-10-17 "农田不要用奇怪的纹路了，就用这个绿黄色条纹的""一小块一小块就行""不要大片"，
    // 替掉 2026-10-14 的压暗垄沟条纹)：一小块一小块的条田——每行高四格(田三格、空一格)，每块宽五到七格(田占去一格空地，
    // 各行错开)，约六成的块种上；田里庄稼色与垄沟色相间(每三行或三列一道垄沟)；挨着不是农田的地方边缘参差，留出几格原野
    public static readonly Color32 FieldCrop = new(186, 196, 110, 255), FieldFurrow = new(150, 128, 84, 255);

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
        int row = Mathf.FloorToInt(tile.y / 4f), inRow = tile.y - row * 4;
        if (inRow == 3) return color;
        int width = 5 + (int)(H(g.Seed, row, 34) * 3), shift = (int)(H(g.Seed, row, 35) * width);
        int column = Mathf.FloorToInt((tile.x + shift) / (float)width), inColumn = tile.x + shift - column * width;
        if (inColumn == width - 1 || H(g.Seed, column, row, 36) > 0.6f) return color;
        bool vertical = H(g.Seed, column, row, 37) < 0.4f;
        bool furrow = ((vertical ? tile.x : tile.y) % 3 + 3) % 3 == 0;
        return Mix(color, furrow ? FieldFurrow : FieldCrop, 0.85f);
    }

    private static bool IsFarm(Geometry g, int zx, int zy)
    {
        TileZone zone = World.world.zone_calculator.getZone(zx, zy);
        return zone != null && zone.city == g.City && ZonePlanSystem.Get(g.City, zone) == ZoneUse.Farm;
    }

    // 镇：圆圆的一片，十字街、中间小集市，屋舍错落；富庶的镇有一圈矮墙，四面开门
    // 高德式：浅灰一片，正中一点集市(黄)；不画十字街、矮墙(城外只留这一点，不花)
    private static Color32 TownTile(Geometry g, WorldTile tile, Color32 color, int dx, int dy, float d, float reach, bool walled)
    {
        if (dx == 0 && dy == 0) return Mix(color, Arterial, 0.9f);
        return Mix(color, Settlement, 0.6f);
    }

    // 屋顶：工业化以前是瓦房，之后多是楼房
    private static Color32 Roof(Geometry g, WorldTile tile) =>
        g.Progress >= 1f && H(g.Seed, tile.x, tile.y, 13) < 0.6f
            ? Slab[(int)(H(g.Seed, tile.x, tile.y, 15) * 4) % 4]
            : HouseRoof[(int)(H(g.Seed, tile.x >> 1, tile.y >> 1, 14) * 4) % 4];

    #endregion
}
