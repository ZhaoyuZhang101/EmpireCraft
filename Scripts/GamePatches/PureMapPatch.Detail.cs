using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市精细层(用户 2026-10-16："把城市像素变得更加精细""现在的城市分辨率不足以显示出城市的细节"
// "太规整了，没有一般城市的随意中带着一丝规划""周边还要生成随机小村镇")：
// 拉近时给镜头里最近的几座城单独画一张一格 4×4 像素的图，盖在一格一像素的平面图上(见 UI/Components/CityDetailOverlay)。
// 用的是同一份平面图(城的轮廓、城墙、四门与中轴、各坊用途与建筑、城外的路与村镇)，只是画得细：
//   · 骨架规划：城墙、护城河、通城门的大街与中轴、城心的标志性建筑，规整；
//   · 肌理自然：街巷错位、弯折，宅基地一刀刀切得大小不一，留胡同，宅子临街开门，夹着菜园、空地、树；
//     坊里有什么建筑(见 Fill)决定画宅子、铺面、衙署、庙、营房还是作坊，坊里建筑少就多空地；
//   · 城外：关厢、镇(十字街、集市、沿街房子、小庙、矮墙)、村(打谷场、人家、池塘、菜园、小路)、格子田。
// 各文化系各有画法(第二步，用户 2026-10-16 看过演示版定下)：街(方格 / 放射弯街 / 迷宫 / 水渠 / 营地)、
// 房子(华夏宅院、日本町家、西欧尖顶排屋、罗马公寓、维京长屋、阿拉伯与波斯平顶天井、埃及泥砖房、印度大宅)、
// 城心(钟鼓楼、寺社、教堂广场、罗马广场、议事大厅、清真寺、神庙、塔庙、阶梯金字塔)、城墙塔楼(方塔、圆塔、木栅)。
// 近代以后的新城区(工厂、铁路车站、环路、板楼、高楼大厦)见 PureMapPatch.Modern(第三步)。
public partial class PureMapPatch
{
    internal const int DetailScale = 4;
    // 一张精细图最多画多大(格)：太大的城只画城心附近
    private const int MaxDetailTiles = 128;

    // 像素种类
    private const byte KEmpty = 0, KGround = 1, KStreet = 2, KAlley = 3, KBuilding = 4, KWall = 5, KWater = 6, KPath = 7,
        KPlaza = 8, KAvenue = 9, KFixed = 10, KCenter = 11, KOff = 255;

    private static Color32 Rgb(int r, int g, int b) => new((byte)r, (byte)g, (byte)b, 255);

    private static readonly Color32 Clear = new(0, 0, 0, 0), Court = Rgb(222, 214, 196), Door = Rgb(150, 60, 50),
        TreeDark = Rgb(92, 146, 80), TreeLight = Rgb(128, 178, 106), Palm = Rgb(86, 140, 70), Dirt = Rgb(214, 196, 160),
        Thatch = Rgb(156, 132, 92), Threshing = Rgb(232, 220, 190), Stall = Rgb(200, 120, 70), StallLight = Rgb(220, 150, 90),
        TempleRoof = Rgb(82, 70, 64), TempleYard = Rgb(226, 216, 196), Garden = Rgb(170, 196, 130), Workshop = Rgb(132, 110, 90),
        WaterBlue = Rgb(140, 185, 235), Ripple = Rgb(176, 210, 240),
        White = Rgb(255, 255, 255), Fence = Rgb(124, 96, 64);
    private static readonly Color32[] SlateRoofs = { Rgb(96, 98, 108), Rgb(104, 102, 108), Rgb(90, 94, 104), Rgb(110, 106, 100), Rgb(98, 92, 96) };

    // 各文化系的画法
    private sealed class DetailStyle
    {
        public string Layout = "grid", House = "courtyard", Center = "drum", Temple = "compound", Market = "shops", Tower = "square";
        public float Jitter = 2.2f, Skip = 0.18f, Half = 0.35f, AlleyP = 0.45f;
        public Color32[] Roofs = SlateRoofs;
        public Color32 Dome = Rgb(90, 140, 120);
        public bool Gardens, Palms;
        public bool FlatRoofs => House is "flat" or "mudbrick" or "haveli";
        public bool Camp => Layout is "yurt" or "hut";
    }

    private static readonly Dictionary<string, DetailStyle> Styles = new()
    {
        ["huaxia"] = new DetailStyle(),
        ["japan"] = new DetailStyle
        {
            Jitter = 0.8f, Skip = 0.06f, Half = 0.15f, AlleyP = 0.6f, House = "machiya", Center = "shrine", Temple = "shrine",
            Roofs = new[] { Rgb(70, 72, 80), Rgb(84, 84, 92), Rgb(64, 66, 74), Rgb(92, 88, 84) }
        },
        ["western"] = new DetailStyle
        {
            Layout = "organic", House = "gabled", Center = "church", Temple = "chapel", Tower = "round",
            Roofs = new[] { Rgb(172, 92, 72), Rgb(152, 82, 66), Rgb(186, 114, 86), Rgb(110, 106, 114), Rgb(160, 100, 80) }
        },
        ["roma"] = new DetailStyle
        {
            Jitter = 0.5f, Skip = 0.04f, Half = 0.08f, AlleyP = 0.25f, House = "insula", Center = "forum", Temple = "roman_temple",
            Roofs = new[] { Rgb(192, 112, 82), Rgb(202, 126, 94), Rgb(178, 102, 76) }
        },
        ["viking"] = new DetailStyle
        {
            Layout = "organic", House = "longhouse", Center = "hall", Temple = "longhall", Market = "stalls", Tower = "palisade",
            Roofs = new[] { Rgb(110, 120, 80), Rgb(124, 116, 82), Rgb(96, 104, 70) }
        },
        ["arab"] = new DetailStyle
        {
            Layout = "maze", House = "flat", Center = "mosque", Temple = "small_mosque", Market = "souk",
            Roofs = new[] { Rgb(226, 210, 176), Rgb(216, 198, 162), Rgb(232, 218, 188) }, Dome = Rgb(90, 140, 120)
        },
        ["persia"] = new DetailStyle
        {
            Layout = "maze", House = "flat", Center = "dome_mosque", Temple = "small_mosque", Market = "souk", Tower = "round",
            Roofs = new[] { Rgb(220, 204, 172), Rgb(208, 192, 160), Rgb(226, 212, 182) }, Dome = Rgb(64, 116, 176), Gardens = true
        },
        ["egypt"] = new DetailStyle
        {
            Jitter = 2.5f, Skip = 0.2f, Half = 0.3f, AlleyP = 0.5f, House = "mudbrick", Center = "pylon", Temple = "pylon_small",
            Market = "stalls", Palms = true, Roofs = new[] { Rgb(206, 186, 146), Rgb(196, 174, 134), Rgb(214, 196, 158) }
        },
        ["india"] = new DetailStyle
        {
            Layout = "organic", House = "haveli", Center = "shikhara", Temple = "shikhara_small", Market = "stalls", Tower = "round",
            Roofs = new[] { Rgb(214, 152, 122), Rgb(222, 182, 132), Rgb(202, 124, 112), Rgb(232, 202, 162), Rgb(196, 160, 190) }
        },
        ["aztec"] = new DetailStyle
        {
            Layout = "canal", House = "flat", Center = "pyramid", Temple = "pyramid_small", Market = "stalls",
            Roofs = new[] { Rgb(238, 234, 224), Rgb(228, 222, 208), Rgb(220, 214, 200) }
        },
        ["nomad"] = new DetailStyle { Layout = "yurt" },
        ["woodland"] = new DetailStyle { Layout = "hut" }
    };

    private static DetailStyle StyleOf(string family) => family != null && Styles.TryGetValue(family, out DetailStyle style) ? style : null;

    internal sealed class DetailImage
    {
        public int X0, Y0, W, H, Key;
        public Color32[] Pixels;
        public List<DetailLabel> Labels;
    }

    // 街区级标注的落点(见 UI/Components/MapLabelOverlay)：精细图画到哪里就记到哪里——街取实际画出来的最长一段，
    // 大街取城门大道被宫城、水面隔开的各段，建筑取实际盖了房的宅基地(同一街区同一种只留最大的一块)。
    // 以前标注按平面图的地块边、街区角摆，精细图里那条街没修、错了位，那块地是宫城、是河，字照样标上去，对不上
    internal sealed class DetailLabel
    {
        public Vector2 Tile;     // 世界格坐标
        public string Icon;      // "street"、"avenue"，或平面图里的建筑种类(house_N、market_N、civic_N、temple……)
        public bool Vertical;
        public long Key;         // 起名用的种子(同一处每次一样)
        public float Weight;     // 长度或面积：同一处只留最大的
    }

    // 各城最近一次画出来的标注落点(按平面图指纹对得上才用)
    private static readonly Dictionary<long, (int stamp, List<DetailLabel> labels)> DrawnLabels = new();

    internal static List<DetailLabel> DrawnLabelsOf(City city)
    {
        if (city?.data == null || !DrawnLabels.TryGetValue(city.data.id, out var entry)) return null;
        return entry.stamp == StampOf(city) ? entry.labels : null;
    }

    // 同一街区同一种建筑只留最大的一块，住宅每个地块只留一处
    private static List<DetailLabel> ReduceLabels(List<DetailLabel> labels)
    {
        var best = new Dictionary<string, DetailLabel>();
        foreach (DetailLabel label in labels)
        {
            string key = label.Icon is "street" or "avenue" ? label.Icon + label.Key
                : label.Icon.StartsWith("house_") ? "house" + label.Key / 4
                : label.Icon.Split('_')[0] + label.Key;
            if (!best.TryGetValue(key, out DetailLabel kept) || label.Weight > kept.Weight) best[key] = label;
        }
        return new List<DetailLabel>(best.Values);
    }

    // 画这座城的精细图吗：纯净地图画城市时(改建中的城照旧)
    internal static bool DetailEligible(City city)
    {
        if (city?.data == null || city.isRekt() || !PureMapMode.Enabled || !DetailShown || Layer.CityViewMap.Active) return false;
        CityPlan plan = PlanFor(city);
        UrbanFabric.Geometry g = plan.fabric;
        return g != null && StyleOf(plan.family) != null && plan.target == null && plan.built.Count > 0;
    }

    // 精细图的指纹：平面图变了、底色(图层)变了就重画
    internal static int DetailKey(City city, Color32 tint) => unchecked(StampOf(city) * 31 + (tint.r << 16 | tint.g << 8 | tint.b));

    // 底色：当前图层里城心那块地的颜色(国家、帝国……)，没有就用本国颜色
    internal static Color32 DetailTint(City city)
    {
        Color32 tint = new(160, 160, 160, 255);
        try
        {
            TileZone center = city.getTile()?.zone;
            ColorAsset color = SmoothBorders.DisplayColor(center) ?? CustomColorSystem.Of(city.kingdom) ?? city.kingdom?.getColor();
            if (color != null) tint = color.getColorBorderInsideAlpha32();
        }
        catch
        {
            // 取不到就用中性灰
        }
        tint.a = 255;
        return tint;
    }

    #region 画布

    private sealed class Canvas
    {
        public readonly int X0, Y0, W, H, PW, PH;
        public readonly Color32[] Pixels;
        public readonly byte[] Kinds;
        public readonly City City;
        public Color32 Block, Street;
        public readonly List<DetailLabel> Labels = new();

        // 画布像素 → 世界格坐标
        public Vector2 TileAt(float px, float py) => new(X0 + (px + 0.5f) / DetailScale, Y0 + (py + 0.5f) / DetailScale);

        public Canvas(City city, int x0, int y0, int w, int h)
        {
            City = city;
            X0 = x0;
            Y0 = y0;
            W = w;
            H = h;
            PW = w * DetailScale;
            PH = h * DetailScale;
            Pixels = new Color32[PW * PH];
            Kinds = new byte[PW * PH];
        }

        public bool In(int px, int py) => px >= 0 && py >= 0 && px < PW && py < PH;

        public byte Kind(int px, int py) => In(px, py) ? Kinds[py * PW + px] : KOff;

        // 不透明的颜色直接写；半透明的(田垄、影子)与已有的颜色叠起来
        public void Put(int px, int py, Color32 color, byte kind)
        {
            if (!In(px, py)) return;
            int i = py * PW + px;
            if (color.a < 255 && color.a > 0 && Pixels[i].a > 0)
            {
                float t = color.a / 255f;
                Color32 under = Pixels[i];
                color = new Color32((byte)(under.r + (color.r - under.r) * t), (byte)(under.g + (color.g - under.g) * t),
                    (byte)(under.b + (color.b - under.b) * t), under.a);
            }
            Pixels[i] = color;
            Kinds[i] = kind;
        }

        public WorldTile Tile(int px, int py) => World.world.GetTile(X0 + Mathf.FloorToInt(px / (float)DetailScale), Y0 + Mathf.FloorToInt(py / (float)DetailScale));

        // 画村镇、小路、废墟时不看归属(见 DrawCountryside)
        public bool Spill;

        // 这一像素落在能盖房子的地上(不是水、山、城墙，属于本城或无主)；关城压着的关门算地，关城画在上面(见 WallGateSystem)
        public bool Land(int px, int py)
        {
            WorldTile tile = Tile(px, py);
            if (tile?.Type == null || tile.Type.liquid || tile.Type.mountains || tile.Type.block || MapWallSystem.Blocks(tile))
                return false;
            City owner = tile.zone?.city;
            return Spill || owner == null || owner == City;
        }

        // 关城落笔的地(见 Fort)：不是水就画——关城筑在山口、山脊的城墙上(见 WallGateSystem.SnapToWall)，山格、墙格照画
        public bool FortLand(int px, int py)
        {
            WorldTile tile = Tile(px, py);
            if (tile?.Type == null || tile.Type.liquid) return false;
            City owner = tile.zone?.city;
            return Spill || owner == null || owner == City;
        }
    }

    private static float DetailHash(long seed, long a, long b = 0, long c = 0)
    {
        unchecked
        {
            ulong x = 0x9E3779B97F4A7C15UL ^ (ulong)seed;
            x = (x ^ (ulong)a) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (ulong)b ^ (x >> 31)) * 0x94D049BB133111EBUL;
            x = (x ^ (ulong)c ^ (x >> 29)) * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 31;
            return (x >> 40) / 16777216f;
        }
    }

    private static float DetailNoise(float t, float cell, long seed)
    {
        float f = t / cell;
        int i = Mathf.FloorToInt(f);
        float s = f - i;
        s = s * s * (3f - 2f * s);
        return Mathf.Lerp(DetailHash(seed, i), DetailHash(seed, i + 1), s);
    }

    private static Color32 Darker(Color32 color, float k) =>
        new((byte)Mathf.Clamp(color.r * k, 0f, 255f), (byte)Mathf.Clamp(color.g * k, 0f, 255f), (byte)Mathf.Clamp(color.b * k, 0f, 255f), 255);

    private static Color32 Pick(Color32[] colors, float r) => colors[Mathf.Clamp((int)(r * colors.Length), 0, colors.Length - 1)];

    // 城心建筑能压在哪些像素上(不压城墙、水、地标)
    private static bool Paintable(byte kind) => kind is KGround or KStreet or KAvenue or KAlley or KPlaza or KBuilding or KCenter;

    #endregion

    // 画一座城的精细图(连同城外的关厢、镇、村)
    internal static DetailImage BuildDetail(City city, Color32 tint)
    {
        CityPlan plan = PlanFor(city);
        UrbanFabric.Geometry g = plan.fabric;
        DetailStyle style = StyleOf(plan.family);
        if (g == null || style == null) return null;
        int size = Config.CITY_ZONE_SIZE;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        void Grow(int x, int y, int r)
        {
            x0 = Mathf.Min(x0, x - r);
            y0 = Mathf.Min(y0, y - r);
            x1 = Mathf.Max(x1, x + r);
            y1 = Mathf.Max(y1, y + r);
        }
        foreach (TileZone zone in city.zones)
        {
            if (zone == null || !plan.built.Contains(zone.id)) continue;
            // 城郊过渡带伸出城边七格(见 PureMapPatch.Fringe)，图要画到那里，否则过渡带在图边上被齐齐切断
            Grow(zone.x * size, zone.y * size, FringeReach + 1);
            Grow(zone.x * size + size - 1, zone.y * size + size - 1, FringeReach + 1);
        }
        if (x0 == int.MaxValue) return null;
        if (g.Modern)
            foreach (TileZone zone in city.zones)
                if (zone != null && UrbanFabric.ModernZone(g, zone))
                {
                    Grow(zone.x * size, zone.y * size, FringeReach + 1);
                    Grow(zone.x * size + size - 1, zone.y * size + size - 1, FringeReach + 1);
                }
        foreach ((int x, int y) in g.Villages) Grow(x, y, 8);
        // 原始聚落连同城外的田、墓地、陶窑(见 PureMapPatch.Tribal)
        if (plan.tribal >= 0)
        {
            Vector2 at = TribalCenter(plan);
            Grow(Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y), Mathf.CeilToInt(TribalRadius(plan) / DetailScale) + 14);
        }
        foreach ((int x, int y, int townSize, _) in g.Towns) Grow(x, y, townSize + 4);
        foreach ((int x, int y, _) in g.Forts) Grow(x, y, RuralSettlementSystem.FortReach + 3);
        foreach (FactorySite site in plan.factories) Grow(site.x, site.y, FactorySystem.Side(site.size) / 2 + 2);
        // 出城的大路、岔路一直画到路的尽头(连同关厢)，不在图边上从精细的路断成粗图上一格一格的路
        foreach (UrbanFabric.Road road in g.Roads)
            for (int d = 0; d <= road.Length; d += 2)
            {
                (int rx, int ry) = UrbanFabric.RoadPoint(road, d);
                Grow(rx, ry, road.Gate && d <= 2 + g.Suburb ? 4 : 3);
            }
        // 太大就以城心为中心截一块
        int cx = g.Cx, cy = g.Cy;
        x0 = Mathf.Max(Mathf.Max(x0, cx - MaxDetailTiles / 2), 0);
        y0 = Mathf.Max(Mathf.Max(y0, cy - MaxDetailTiles / 2), 0);
        x1 = Mathf.Min(Mathf.Min(x1, cx + MaxDetailTiles / 2 - 1), MapBox.width - 1);
        y1 = Mathf.Min(Mathf.Min(y1, cy + MaxDetailTiles / 2 - 1), MapBox.height - 1);
        if (x1 < x0 || y1 < y0) return null;
        var c = new Canvas(city, x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        long seed = UrbanFabric.SeedOf(city);
        if (style.Camp) DrawCamp(c, plan, g, style, tint, seed);
        else if (plan.tribal >= 0)
        {
            DrawTribal(c, plan, g, style, tint, seed);
            // 原始聚落城外不画镇、路(那时还没有)；手动筑的关城、废墟照画
            DrawOutposts(c, g, tint);
        }
        else
        {
            DrawUrban(c, plan, style, tint, seed);
            if (g.Modern) DrawModern(c, plan, g, style, tint, seed);
            // 城郊过渡带(见 PureMapPatch.Fringe)：画在村镇、田块、小路之前
            DrawFringe(c, style, tint, seed);
            DrawCountryside(c, g, style, tint, seed);
        }
        // 厂区：营地、原始聚落里手动放的也画(见 FactorySystem.Place)
        DrawFactories(c, plan);
        List<DetailLabel> labels = ReduceLabels(c.Labels);
        DrawnLabels[city.data.id] = (StampOf(city), labels);
        if (DrawnLabels.Count > 64)
            foreach (long stale in new List<long>(DrawnLabels.Keys))
                if (World.world.cities.get(stale) == null) DrawnLabels.Remove(stale);
        return new DetailImage { X0 = x0, Y0 = y0, W = c.W, H = c.H, Pixels = c.Pixels, Key = DetailKey(city, tint), Labels = labels };
    }

    #region 城里

    private static void DrawUrban(Canvas c, CityPlan plan, DetailStyle style, Color32 tint, long seed)
    {
        int s = DetailScale;
        Color32 block = Lerp(tint, Block, BlockShare), avenue = Lerp(tint, Arterial, StreetShare),
            axis = Lerp(tint, AxisOf(plan.family) ?? Arterial, StreetShare), moat = Lerp(tint, Moat, 0.9f),
            street = Lerp(tint, Street, StreetShare);
        c.Block = block;
        c.Street = street;
        Color32? wallColor = WallOf(plan.family);
        bool wallsUp = wallColor != null && plan.walled && !(plan.fabric?.WallsDown ?? false);
        bool maze = style.Layout == "maze", canal = style.Layout == "canal";
        UrbanFabric.Geometry fabric = plan.fabric;
        bool modern = fabric != null && fabric.Modern;
        bool palace = DetailedPalace(plan);
        // 其余文化系的都城地标也画精细版，只占城心正中一块(见 PureMapPatch.Landmark)
        LandmarkArea site = default;
        bool landmarked = !palace && LandmarkSite(plan, out site);
        int size = Config.CITY_ZONE_SIZE;
        // ① 逐格铺底：城里的地、地标(宫城、精细地标先占住，没有精细画法的照一格一像素的画法放大)、护城河、水渠、大街；城墙、城心建筑后画
        for (int ty = 0; ty < c.H; ty++)
        for (int tx = 0; tx < c.W; tx++)
        {
            WorldTile tile = World.world.GetTile(c.X0 + tx, c.Y0 + ty);
            if (tile?.Type == null || tile.zone == null) continue;
            if (tile.Type.liquid)
            {
                Fill(c, tx, ty, Clear, KWater);
                continue;
            }
            if (!Urban(tile, plan))
            {
                if (plan.moat && wallsUp && BesideWall(tile, plan))
                {
                    bool bridge = tile.x == plan.gateX || tile.y == plan.gateY;
                    for (int sy = 0; sy < s; sy++)
                    for (int sx = 0; sx < s; sx++)
                    {
                        int px = tx * s + sx, py = ty * s + sy;
                        bool rail = tile.x == plan.gateX ? sx == 0 || sx == s - 1 : sy == 0 || sy == s - 1;
                        Color32 color = bridge ? rail ? Rgb(120, 104, 88) : avenue
                            : (px + 2 * py) % 8 == 0 ? Lerp(moat, White, 0.35f) : moat;
                        c.Put(px, py, color, bridge ? KAvenue : KWater);
                    }
                }
                continue;
            }
            if (plan.landmark.Contains(tile.zone.id))
            {
                // 华夏都城宫城画精细版(见 PureMapPatch.Palace)，先占住
                if (palace || landmarked && site.Contains(tile))
                {
                    Fill(c, tx, ty, block, KFixed);
                    continue;
                }
                // 埃及金字塔在城外，那块地其余的格是沙地
                if (landmarked && plan.family == "egypt")
                {
                    Fill(c, tx, ty, Lerp(tint, Rgb(222, 200, 146), 0.8f), KPlaza);
                    continue;
                }
                // 没有精细画法的文化系照粗图放大
                if (!landmarked)
                {
                    Slot[] slots = plan.zones.TryGetValue(tile.zone.id, out Slot[] found) ? found : null;
                    Color32 coarse = Plan(tile, tint, FarmlandSystem.IsPlanned(c.City, tile.zone), slots,
                        tile.x - tile.zone.x * size, tile.y - tile.zone.y * size, plan, tile.zone);
                    Fill(c, tx, ty, coarse, KFixed);
                    continue;
                }
                // 地标那块地其余的格往下照常画(大街、城墙、街坊)
            }
            if (modern)
            {
                // 近代：有城墙的城拆了城墙，原址修成柏油环路；开发了的地块、干道、铁路留给新城区的画法(见 DrawModern)
                if (plan.walled && fabric.WallsDown && UrbanEdge(tile, plan) && plan.gateX != int.MinValue)
                {
                    AsphaltTile(c, tx, ty, Mathf.Abs(tile.x - plan.gateX) > Mathf.Abs(tile.y - plan.gateY));
                    continue;
                }
                if (UrbanFabric.PartAt(fabric, tile, out _) != UrbanFabric.Part.None) continue;
            }
            if (wallsUp && UrbanEdge(tile, plan))
            {
                Fill(c, tx, ty, wallColor.Value, KWall);
                continue;
            }
            bool onAxis = plan.axis && (tile.x == plan.axisX || tile.y == plan.axisY);
            bool onGate = tile.x == plan.gateX || tile.y == plan.gateY;
            int lx = tile.x - tile.zone.x * size, ly = tile.y - tile.zone.y * size;
            // 阿兹特克：地块之间是水渠(堤道、中轴除外)
            if (canal && (lx == 0 || ly == 0) && !onGate && !onAxis)
            {
                for (int sy = 0; sy < s; sy++)
                for (int sx = 0; sx < s; sx++)
                {
                    int px = tx * s + sx, py = ty * s + sy;
                    c.Put(px, py, (px + py) % 7 == 0 ? Ripple : WaterBlue, KWater);
                }
                continue;
            }
            // 迷宫式的城没有笔直的大街(主街另画成弯的)
            if (onAxis || onGate && !maze)
            {
                bool vertical = onAxis ? tile.x == plan.axisX : tile.x == plan.gateX;
                Color32 road = onAxis ? axis : avenue;
                for (int sy = 0; sy < s; sy++)
                for (int sx = 0; sx < s; sx++)
                {
                    bool edge = vertical ? sx == 0 || sx == s - 1 : sy == 0 || sy == s - 1;
                    c.Put(tx * s + sx, ty * s + sy, edge ? Lerp(road, Rgb(150, 120, 70), 0.35f) : road, KAvenue);
                }
                continue;
            }
            Fill(c, tx, ty, block, KGround);
        }
        // 城门大道的标注落点：通城门的那一列、一行上连成一段的大道(被宫城、水面隔开就各算一段)
        if (plan.gateX != int.MinValue)
        {
            AvenueLabels(c, true, plan.gateX - c.X0);
            AvenueLabels(c, false, plan.gateY - c.Y0);
        }
        // ② 街：方格(沿地块边错位微弯)、放射弯街、迷宫主街；阿兹特克靠水渠
        switch (style.Layout)
        {
            case "grid":
                for (int line = c.X0 / size + 1; line <= (c.X0 + c.W) / size; line++) StreetLine(c, true, line * size - c.X0, plan, style, seed, line, street);
                for (int line = c.Y0 / size + 1; line <= (c.Y0 + c.H) / size; line++) StreetLine(c, false, line * size - c.Y0, plan, style, seed, line, street);
                if (DetailHash(seed, 7) < 0.4f) Diagonal(c, seed, street);
                break;
            case "organic":
                OrganicStreets(c, plan, style, seed, street);
                break;
            case "maze":
                MazeStreets(c, plan, seed, street);
                break;
        }
        // ③ 城心的标志性建筑(两条中线交口)；城心立着都城地标的不画
        if (plan.gateX != int.MinValue && plan.built.Count >= 2 && !(modern && World.world.GetTile(plan.gateX, plan.gateY) is WorldTile center &&
                UrbanFabric.PartAt(fabric, center, out _) != UrbanFabric.Part.None) &&
            !(World.world.GetTile(plan.gateX, plan.gateY)?.zone is TileZone gateZone && plan.landmark.Contains(gateZone.id)))
            DrawCenter(c, style, wallColor, tint, (plan.gateX - c.X0) * s + s / 2, (plan.gateY - c.Y0) * s + s / 2, seed);
        // ④ 街坊：被街围出的一片片，逐片切宅基地、盖房子
        Blocks(c, plan, style, seed);
        // ⑤ 行道树(迷宫式的窄巷没有)
        if (!maze)
            for (int py = 0; py < c.PH; py++)
            for (int px = 0; px < c.PW; px++)
            {
                if (c.Kind(px, py) != KGround || DetailHash(seed, px, py, 77) >= 0.05f) continue;
                if (c.Kind(px + 1, py) is KStreet or KAvenue || c.Kind(px - 1, py) is KStreet or KAvenue ||
                    c.Kind(px, py + 1) is KStreet or KAvenue || c.Kind(px, py - 1) is KStreet or KAvenue)
                    c.Put(px, py, TreeDark, KGround);
            }
        // ⑥ 都城宫城、地标
        if (palace) DrawPalace(c, plan, tint, seed);
        else if (landmarked) DrawLandmark(c, plan, style, tint, seed, site);
        // ⑦ 城墙
        if (wallsUp) Walls(c, plan, style, wallColor.Value);
    }

    // 农田一律画成条田(用户 2026-10-17 "农田不要用奇怪的纹路了，就用这个绿黄色条纹的""一小块一小块就行""不要大片")：
    // 庄稼色与垄沟色相间，每三行(列)一道垄沟，横条或竖条；村边、城郊、农耕区都是一小块一小块，块间留空地
    private static readonly Color32 FieldCropColor = Rgb(186, 196, 110), FieldFurrowColor = Rgb(150, 128, 84);

    private static Color32 FieldColor(Color32 tint, int px, int py, bool vertical) =>
        Lerp(tint, ((vertical ? px : py) % 3 + 3) % 3 == 0 ? FieldFurrowColor : FieldCropColor, 0.85f);

    private static void Fill(Canvas c, int tx, int ty, Color32 color, byte kind)
    {
        for (int sy = 0; sy < DetailScale; sy++)
        for (int sx = 0; sx < DetailScale; sx++)
            c.Put(tx * DetailScale + sx, ty * DetailScale + sy, color, kind);
    }

    // 一条次级街道(格坐标 at 处的地块边)：不修、只修半截(到中线为止)、错位、宽 2(偶尔 3)、沿途微弯——各系比例不同
    private static void StreetLine(Canvas c, bool vertical, int at, CityPlan plan, DetailStyle style, long seed, int line, Color32 street)
    {
        long id = seed * 7 + (vertical ? 1000 : 2000) + line;
        if (DetailHash(id, 1) < style.Skip) return;
        int s = DetailScale;
        int length = vertical ? c.PH : c.PW;
        int from = 0, to = length - 1;
        int gate = vertical ? plan.gateY : plan.gateX;
        if (gate != int.MinValue && DetailHash(id, 2) < style.Half)
        {
            int middle = Mathf.Clamp((gate - (vertical ? c.Y0 : c.X0)) * s, 0, length - 1);
            if (DetailHash(id, 3) < 0.5f) to = middle;
            else from = middle;
        }
        int offset = Mathf.RoundToInt((DetailHash(id, 4) - 0.5f) * 6f * style.Jitter / 2.2f);
        int width = DetailHash(id, 5) < 0.75f ? 2 : 3;
        // 标注落在实际修出来的最长一段的中间
        int run = 0, bestLength = 0, bestEnd = -1;
        for (int t = from; t <= to; t++)
        {
            int wob = Mathf.RoundToInt(style.Jitter * (DetailNoise(t, 26f, id) - 0.5f)) + offset;
            bool drawn = false;
            for (int d = 0; d < width; d++)
            {
                int px = vertical ? at * s + wob + d - 1 : t, py = vertical ? t : at * s + wob + d - 1;
                if (c.Kind(px, py) != KGround) continue;
                c.Put(px, py, street, KStreet);
                drawn = true;
            }
            run = drawn ? run + 1 : 0;
            if (run > bestLength)
            {
                bestLength = run;
                bestEnd = t;
            }
        }
        if (bestLength < 3 * s) return;
        float labelAt = bestEnd - bestLength / 2f, across = at * s + offset;
        c.Labels.Add(new DetailLabel
        {
            Tile = vertical ? c.TileAt(across, labelAt) : c.TileAt(labelAt, across), Icon = "street", Vertical = vertical,
            Key = id, Weight = bestLength
        });
    }

    // 城门大道上连成一段(至少三格)的各段，各记一个标注落点
    private static void AvenueLabels(Canvas c, bool vertical, int line)
    {
        int s = DetailScale, length = vertical ? c.H : c.W;
        if (line < 0 || line >= (vertical ? c.W : c.H)) return;
        int start = -1;
        for (int t = 0; t <= length; t++)
        {
            bool on = t < length && c.Kind(vertical ? line * s + 1 : t * s + 1, vertical ? t * s + 1 : line * s + 1) == KAvenue;
            if (on && start < 0) start = t;
            if (on || start < 0) continue;
            if (t - start >= 3)
            {
                float middle = (start + t) / 2f * s, across = line * s + s / 2f;
                c.Labels.Add(new DetailLabel
                {
                    Tile = vertical ? c.TileAt(across, middle) : c.TileAt(middle, across), Icon = "avenue", Vertical = vertical,
                    Key = (vertical ? 1L : 2L) * 1000003L + (vertical ? c.X0 + line : c.Y0 + line) * 7919L +
                          (vertical ? c.Y0 : c.X0) + start,
                    Weight = t - start
                });
            }
            start = -1;
        }
    }

    // 斜街一条(顺着旧河道、旧路)
    private static void Diagonal(Canvas c, long seed, Color32 street)
    {
        int ax = (int)(c.PW * (0.2f + 0.3f * DetailHash(seed, 8))), ay = (int)(c.PH * (0.25f + 0.3f * DetailHash(seed, 9)));
        int length = (int)(Mathf.Min(c.PW, c.PH) * 0.35f);
        float slope = 0.35f + 0.5f * DetailHash(seed, 10);
        for (int t = 0; t < length; t++)
        {
            int px = ax + t, py = ay + (int)(t * slope + 2f * Mathf.Sin(t / 9f));
            for (int d = 0; d < 2; d++)
                if (c.Kind(px, py + d) == KGround) c.Put(px, py + d, street, KStreet);
        }
    }

    // 弯曲的路：从 (x, y) 朝 angle 走，围着这个方向左右摆(不累积，不会绕回来)；半像素一步、方笔连续画，
    // 只画在街坊空地上，出城几步就停。drift 是摆幅(1 约 ±25°)
    private static void Wander(Canvas c, float x, float y, float angle, float steps, int width, Color32 color, long seed, float drift)
    {
        int outside = 0;
        for (int i = 0; i < steps * 2; i++)
        {
            float a = angle + (DetailNoise(i, 44f, seed) - 0.5f) * drift * 0.9f;
            x += Mathf.Cos(a) * 0.5f;
            y += Mathf.Sin(a) * 0.5f;
            bool inside = false;
            for (int dy = 0; dy < width; dy++)
            for (int dx = 0; dx < width; dx++)
            {
                int px = Mathf.FloorToInt(x) + dx, py = Mathf.FloorToInt(y) + dy;
                byte k = c.Kind(px, py);
                if (k == KGround)
                {
                    c.Put(px, py, color, KStreet);
                    inside = true;
                }
                else if (k is KStreet or KAvenue or KAlley) inside = true;
            }
            outside = inside ? 0 : outside + 1;
            if (outside > 6 && i > 16) return;
        }
    }

    // 弧街：以城心为圆心的一段弧，半径随噪声起伏
    private static void Arc(Canvas c, float cx, float cy, float r0, float a0, float span, Color32 color, long seed)
    {
        int n = Mathf.CeilToInt(r0 * span * 2f);
        for (int t = 0; t <= n; t++)
        {
            float a = a0 + span * t / n, rr = r0 * (1f + 0.1f * (DetailNoise(t, 50f, seed) - 0.5f));
            float x = cx + Mathf.Cos(a) * rr, y = cy + Mathf.Sin(a) * rr;
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int px = Mathf.FloorToInt(x) + dx, py = Mathf.FloorToInt(y) + dy;
                if (c.Kind(px, py) == KGround) c.Put(px, py, color, KStreet);
            }
        }
    }

    // 城的大致半径(像素)：建成地块数折成圆
    private static float UrbanRadius(CityPlan plan) =>
        Mathf.Sqrt(plan.built.Count * Config.CITY_ZONE_SIZE * Config.CITY_ZONE_SIZE / Mathf.PI) * DetailScale;

    // 西欧、印度：从城心放射出去的弯街，外圈几段弧街，再岔出几条短街；维京只有几条一像素的木板小路
    private static void OrganicStreets(Canvas c, CityPlan plan, DetailStyle style, long seed, Color32 street)
    {
        if (plan.gateX == int.MinValue) return;
        float cx = (plan.gateX - c.X0) * DetailScale + 2f, cy = (plan.gateY - c.Y0) * DetailScale + 2f, R = UrbanRadius(plan);
        bool viking = style.House == "longhouse";
        int n = viking ? 4 : 4 + (int)(DetailHash(seed, 30) * 3), width = viking ? 1 : 2;
        Color32 path = viking ? Lerp(c.Block, Rgb(176, 140, 96), 0.7f) : street;
        for (int i = 0; i < n; i++)
        {
            float a = (i + 0.5f + (DetailHash(seed, i, 31) - 0.5f) * 0.5f) / n * Mathf.PI * 2f;
            Wander(c, cx + Mathf.Cos(a) * 10f, cy + Mathf.Sin(a) * 10f, a, R * 1.2f, width, path, seed + i * 17, 1f);
        }
        if (viking) return;
        foreach (float f in new[] { 0.45f, 0.75f })
        {
            int pieces = 2 + (int)(DetailHash(seed, (long)(f * 100), 32) * 2);
            for (int k = 0; k < pieces; k++)
                Arc(c, cx, cy, R * f, (k + DetailHash(seed, (long)(f * 100), k, 33)) / pieces * Mathf.PI * 2f,
                    0.6f + 0.6f * DetailHash(seed, (long)(f * 100), k, 34), street, seed + (long)(f * 1000) + k);
        }
        for (int i = 0; i < 3; i++)
        {
            float a = DetailHash(seed, i, 35) * Mathf.PI * 2f, r = R * (0.3f + 0.55f * DetailHash(seed, i, 36));
            Wander(c, cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, a + Mathf.PI / 2f * (DetailHash(seed, i, 37) < 0.5f ? 1f : -1f),
                18f + DetailHash(seed, i, 38) * 24f, 2, street, seed + 500 + i, 1.5f);
        }
    }

    // 阿拉伯、波斯：几条弯曲的土路主街从城心通四门，其余是切宅基地切出的窄巷、死胡同
    private static void MazeStreets(Canvas c, CityPlan plan, long seed, Color32 street)
    {
        if (plan.gateX == int.MinValue) return;
        float cx = (plan.gateX - c.X0) * DetailScale + 2f, cy = (plan.gateY - c.Y0) * DetailScale + 2f, R = UrbanRadius(plan);
        Color32 dusty = Lerp(street, Dirt, 0.35f);
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI / 2f;
            Wander(c, cx + Mathf.Cos(a) * 12f, cy + Mathf.Sin(a) * 12f, a, R * 1.3f, 2, dusty, seed + 40 + k, 2.2f);
        }
        for (int i = 0; i < 5; i++)
        {
            float a = DetailHash(seed, i, 41) * Mathf.PI * 2f, r = R * (0.25f + 0.5f * DetailHash(seed, i, 42));
            Wander(c, cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, a + 1.4f, 16f + DetailHash(seed, i, 43) * 24f, 1, dusty, seed + 600 + i, 3f);
        }
    }

    #region 画屋顶、地面的小工具

    // 一块地面(城心建筑用)：只压在街坊、街、地标以外的地方
    private static void Area(Canvas c, int x0, int y0, int x1, int y1, Color32 color, byte kind = KCenter)
    {
        for (int py = Mathf.Min(y0, y1); py <= Mathf.Max(y0, y1); py++)
        for (int px = Mathf.Min(x0, x1); px <= Mathf.Max(x0, x1); px++)
            if (Paintable(c.Kind(px, py))) c.Put(px, py, color, kind);
    }

    private static void Disc(Canvas c, float cx, float cy, float r, Color32 color, byte kind)
    {
        for (int py = Mathf.FloorToInt(cy - r); py <= cy + r; py++)
        for (int px = Mathf.FloorToInt(cx - r); px <= cx + r; px++)
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= r * r && c.Kind(px, py) is KGround or KStreet or KAvenue)
                c.Put(px, py, color, kind);
    }

    // 只在还是街坊空地的像素上画(切出来的宅基地可能越出不规则的街坊)
    private static void Paint(Canvas c, int px, int py, Color32 color, byte kind = KBuilding)
    {
        byte k = c.Kind(px, py);
        if (k == KGround || k == KBuilding && kind == KBuilding) c.Put(px, py, color, kind);
    }

    private static bool CanRoof(byte k, bool force) => force ? Paintable(k) : k is KGround or KBuilding or KPlaza;

    // 有屋脊的屋顶：屋脊一道亮线、檐口暗
    private static void Roof(Canvas c, int x0, int y0, int x1, int y1, bool horizontal, Color32 roof, byte kind = KBuilding, bool force = false)
    {
        Color32 high = Darker(roof, 1.32f), low = Darker(roof, 0.72f);
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            if (!CanRoof(c.Kind(px, py), force)) continue;
            float m = horizontal ? (y0 + y1) / 2f : (x0 + x1) / 2f, at = horizontal ? py : px;
            bool rim = horizontal ? py == y0 || py == y1 : px == x0 || px == x1;
            c.Put(px, py, Mathf.Abs(at - m) < 0.6f ? high : rim ? low : roof, kind);
        }
    }

    // 平顶：一圈矮墙(女儿墙)暗一点
    private static void Flat(Canvas c, int x0, int y0, int x1, int y1, Color32 roof, byte kind = KBuilding, bool force = false)
    {
        Color32 low = Darker(roof, 0.84f);
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            if (!CanRoof(c.Kind(px, py), force)) continue;
            c.Put(px, py, px == x0 || px == x1 || py == y0 || py == y1 ? low : roof, kind);
        }
    }

    // 穹顶：受光的一侧亮、边上暗
    private static void Dome(Canvas c, float cx, float cy, float r, Color32 color, byte kind = KCenter)
    {
        for (int py = Mathf.FloorToInt(cy - r); py <= cy + r; py++)
        for (int px = Mathf.FloorToInt(cx - r); px <= cx + r; px++)
        {
            float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            if (d > r || !Paintable(c.Kind(px, py))) continue;
            float light = px - cx - (py - cy) < -r * 0.3f ? 1.3f : d > r - 1f ? 0.75f : 1f;
            c.Put(px, py, Darker(color, light), kind);
        }
    }

    // 东南一侧落一道影子
    private static void Shadow(Canvas c, int x0, int y0, int x1, int y1)
    {
        Color32 shade = Darker(c.Block, 0.84f);
        for (int px = x0 + 1; px <= x1 + 1; px++)
            if (c.Kind(px, y0 - 1) == KGround) c.Put(px, y0 - 1, shade, KGround);
        for (int py = y0 - 1; py < y1; py++)
            if (c.Kind(x1 + 1, py) == KGround) c.Put(x1 + 1, py, shade, KGround);
    }

    // 宅基地哪一边临街(门朝那边)：0 南 1 北 2 西 3 东
    private static int StreetSide(Canvas c, int x0, int y0, int x1, int y1)
    {
        int[] counts = new int[4];
        for (int px = x0; px <= x1; px++)
        {
            if (c.Kind(px, y0 - 2) is KStreet or KAlley or KAvenue or KPlaza) counts[0]++;
            if (c.Kind(px, y1 + 2) is KStreet or KAlley or KAvenue or KPlaza) counts[1]++;
        }
        for (int py = y0; py <= y1; py++)
        {
            if (c.Kind(x0 - 2, py) is KStreet or KAlley or KAvenue or KPlaza) counts[2]++;
            if (c.Kind(x1 + 2, py) is KStreet or KAlley or KAvenue or KPlaza) counts[3]++;
        }
        int side = 0;
        for (int i = 1; i < 4; i++)
            if (counts[i] > counts[side]) side = i;
        return side;
    }

    private static void DoorAt(Canvas c, int side, int x0, int y0, int x1, int y1)
    {
        switch (side)
        {
            case 0: Paint(c, (x0 + x1) / 2, y0, Door); break;
            case 1: Paint(c, (x0 + x1) / 2, y1, Door); break;
            case 2: Paint(c, x0, (y0 + y1) / 2, Door); break;
            default: Paint(c, x1, (y0 + y1) / 2, Door); break;
        }
    }

    #endregion

    #region 城心

    private static void DrawCenter(Canvas c, DetailStyle style, Color32? wallColor, Color32 tint, int cx, int cy, long seed)
    {
        Color32 paving = Lerp(tint, Rgb(236, 232, 224), 0.88f), marble = Rgb(244, 240, 230);
        switch (style.Center)
        {
            case "drum":
                // 钟鼓楼(3×3 格)：红墙台基、黑瓦重檐
                for (int py = cy - 6; py < cy + 6; py++)
                for (int px = cx - 6; px < cx + 6; px++)
                {
                    float d = Mathf.Max(Mathf.Abs(px - cx + 0.5f), Mathf.Abs(py - cy + 0.5f));
                    Area(c, px, py, px, py, d > 4.6f ? PalaceWall : d > 2.2f ? DrumRoof : d > 0.9f ? Rgb(120, 108, 112) : Rgb(156, 146, 126));
                }
                break;
            case "shrine":
                // 寺社：一片庭园，池子，黑瓦大殿，朱红鸟居
                Area(c, cx - 9, cy - 9, cx + 9, cy + 9, Rgb(120, 160, 100));
                for (int py = cy - 9; py <= cy + 9; py++)
                for (int px = cx - 9; px <= cx + 9; px++)
                    if (DetailHash(seed, px, py, 90) < 0.3f) Area(c, px, py, px, py, Rgb(92, 140, 84));
                for (int py = cy - 6; py <= cy - 2; py++)
                for (int px = cx - 7; px <= cx - 2; px++)
                    if ((px - cx + 4.5f) * (px - cx + 4.5f) / 7f + (py - cy + 4f) * (py - cy + 4f) / 4f <= 1f) Area(c, px, py, px, py, WaterBlue);
                Roof(c, cx - 6, cy + 1, cx + 7, cy + 7, true, Rgb(56, 58, 64), KCenter, true);
                Area(c, cx, cy - 8, cx + 1, cy - 8, Rgb(190, 60, 40));
                break;
            case "church":
                // 集市广场，北面十字形教堂(带钟楼)，广场上散着摊子
                Disc(c, cx, cy, 13f, paving, KPlaza);
                Roof(c, cx - 3, cy + 6, cx + 3, cy + 20, false, Rgb(96, 98, 112), KCenter, true);
                Roof(c, cx - 8, cy + 12, cx + 8, cy + 15, true, Rgb(96, 98, 112), KCenter, true);
                Area(c, cx - 3, cy + 3, cx + 3, cy + 6, Rgb(130, 126, 120));
                Area(c, cx - 1, cy + 4, cx + 1, cy + 5, Rgb(96, 98, 112));
                for (int i = 0; i < 10; i++)
                {
                    float a = DetailHash(seed, i, 91) * Mathf.PI * 2f, r = 6f + DetailHash(seed, i, 92) * 5f;
                    int px = Mathf.RoundToInt(cx + Mathf.Cos(a) * r), py = Mathf.RoundToInt(cy - 4 + Mathf.Sin(a) * r * 0.5f);
                    if (c.Kind(px, py) == KPlaza) c.Put(px, py, DetailHash(seed, i, 93) < 0.5f ? Stall : StallLight, KPlaza);
                }
                break;
            case "forum":
                // 大理石铺地的广场，四周柱廊，一端神庙
                Area(c, cx - 10, cy - 12, cx + 10, cy + 16, marble);
                for (int py = cy - 12; py <= cy + 16; py += 2)
                {
                    Area(c, cx - 9, py, cx - 9, py, Rgb(200, 196, 186));
                    Area(c, cx + 9, py, cx + 9, py, Rgb(200, 196, 186));
                }
                for (int px = cx - 9; px <= cx + 9; px += 2) Area(c, px, cy - 11, px, cy - 11, Rgb(200, 196, 186));
                Area(c, cx - 5, cy + 6, cx + 5, cy + 15, Rgb(234, 228, 214));
                Roof(c, cx - 4, cy + 8, cx + 4, cy + 14, false, Rgb(192, 112, 82), KCenter, true);
                break;
            case "hall":
                // 议事大厅：一圈栅栏围的场子，当中一座大长屋
                Area(c, cx - 12, cy - 9, cx + 12, cy + 9, Lerp(tint, Rgb(200, 186, 140), 0.6f), KPlaza);
                Roof(c, cx - 10, cy - 3, cx + 10, cy + 3, true, Rgb(104, 112, 74), KCenter, true);
                for (int px = cx - 12; px <= cx + 12; px += 2)
                {
                    Area(c, px, cy - 9, px, cy - 9, Rgb(120, 90, 60));
                    Area(c, px, cy + 9, px, cy + 9, Rgb(120, 90, 60));
                }
                break;
            case "mosque":
            case "dome_mosque":
            {
                // 清真寺：一圈院墙，礼拜殿(穹顶)，院中水池，一角宣礼塔
                Color32 rim = Darker(wallColor ?? Rgb(200, 180, 140), 0.9f);
                Area(c, cx - 11, cy - 11, cx + 11, cy + 11, Rgb(232, 222, 196));
                Area(c, cx - 11, cy - 11, cx + 11, cy - 11, rim);
                Area(c, cx - 11, cy + 11, cx + 11, cy + 11, rim);
                Area(c, cx - 11, cy - 11, cx - 11, cy + 11, rim);
                Area(c, cx + 11, cy - 11, cx + 11, cy + 11, rim);
                Flat(c, cx - 10, cy + 2, cx + 10, cy + 10, Rgb(214, 200, 168), KCenter, true);
                Dome(c, cx, cy + 6, style.Center == "dome_mosque" ? 5f : 4f, style.Dome);
                if (style.Center == "dome_mosque")
                {
                    Dome(c, cx - 7, cy + 6, 2f, style.Dome);
                    Dome(c, cx + 7, cy + 6, 2f, style.Dome);
                }
                Area(c, cx, cy - 5, cx + 1, cy - 5, Rgb(120, 170, 220));
                Area(c, cx + 9, cy - 11, cx + 11, cy - 9, Rgb(244, 238, 222));
                Area(c, cx + 10, cy - 10, cx + 10, cy - 10, Rgb(200, 180, 140));
                break;
            }
            case "pylon":
                // 神庙：塔门、柱厅、圣所
                Area(c, cx - 9, cy - 10, cx + 9, cy + 14, Rgb(222, 206, 170));
                Flat(c, cx - 9, cy - 10, cx - 2, cy - 7, Rgb(200, 176, 128), KCenter, true);
                Flat(c, cx + 2, cy - 10, cx + 9, cy - 7, Rgb(200, 176, 128), KCenter, true);
                for (int py = cy - 3; py <= cy + 4; py += 2)
                for (int px = cx - 6; px <= cx + 6; px += 2)
                    Area(c, px, py, px, py, Rgb(180, 156, 112));
                Flat(c, cx - 4, cy + 7, cx + 4, cy + 13, Rgb(206, 184, 140), KCenter, true);
                break;
            case "shikhara":
                // 塔庙：层层收进的塔，旁边一方阶梯水池
                for (int sq = 9; sq >= 1; sq -= 2) Area(c, cx - sq, cy - sq, cx + sq, cy + sq, Darker(Rgb(226, 182, 140), 0.8f + (9 - sq) * 0.05f));
                Area(c, cx, cy, cx, cy, Rgb(250, 230, 180));
                Area(c, cx + 12, cy - 4, cx + 24, cy + 8, Rgb(176, 160, 130));
                for (int sq = 5; sq >= 1; sq--)
                    Area(c, cx + 18 - sq, cy + 2 - sq, cx + 18 + sq, cy + 2 + sq, sq == 1 ? WaterBlue : Darker(Rgb(176, 160, 130), 0.85f + sq * 0.03f));
                break;
            case "pyramid":
                // 神庙区：一圈白墙，当中阶梯金字塔，顶上神殿，一道台阶
                Area(c, cx - 14, cy - 14, cx + 14, cy + 14, Rgb(236, 232, 222));
                for (int t = -14; t <= 14; t++)
                {
                    Area(c, cx + t, cy - 14, cx + t, cy - 14, Rgb(200, 196, 180));
                    Area(c, cx + t, cy + 14, cx + t, cy + 14, Rgb(200, 196, 180));
                    Area(c, cx - 14, cy + t, cx - 14, cy + t, Rgb(200, 196, 180));
                    Area(c, cx + 14, cy + t, cx + 14, cy + t, Rgb(200, 196, 180));
                }
                for (int sq = 9; sq >= 2; sq--) Area(c, cx - sq, cy - sq, cx + sq, cy + sq, sq % 2 == 1 ? Rgb(190, 180, 160) : Rgb(214, 206, 186));
                Area(c, cx - 2, cy - 2, cx + 2, cy + 2, Rgb(170, 70, 50));
                Area(c, cx, cy - 9, cx, cy - 2, Rgb(240, 236, 228));
                break;
        }
    }

    #endregion

    #region 街坊与宅基地

    private static void Blocks(Canvas c, CityPlan plan, DetailStyle style, long seed)
    {
        var seen = new bool[c.PW * c.PH];
        var stack = new Stack<int>();
        int index = 0;
        int pad = style.Layout == "maze" || style.House == "flat" ? 0 : 1;
        for (int start = 0; start < seen.Length; start++)
        {
            if (seen[start] || c.Kinds[start] != KGround) continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, count = 0;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int px = i % c.PW, py = i / c.PW;
                count++;
                minX = Mathf.Min(minX, px);
                minY = Mathf.Min(minY, py);
                maxX = Mathf.Max(maxX, px);
                maxY = Mathf.Max(maxY, py);
                void Visit(int nx, int ny)
                {
                    if (!c.In(nx, ny)) return;
                    int j = ny * c.PW + nx;
                    if (seen[j] || c.Kinds[j] != KGround) return;
                    seen[j] = true;
                    stack.Push(j);
                }
                Visit(px + 1, py);
                Visit(px - 1, py);
                Visit(px, py + 1);
                Visit(px, py - 1);
            }
            if (count < 20) continue;
            // 街坊外沿留一像素(人行道；平顶房挤着街盖，不留)，里面再切
            Subdivide(c, plan, style, minX + pad, minY + pad, maxX - pad, maxY - pad, 0, seed * 131 + index++);
        }
    }

    // 一刀刀切：沿长边在 35%~65% 处切开，大块偶尔留一条胡同(迷宫式的城胡同多、常常是死胡同)；够小或随机停下就盖房
    private static void Subdivide(Canvas c, CityPlan plan, DetailStyle style, int x0, int y0, int x1, int y1, int depth, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 3 || h < 3) return;
        int area = w * h;
        bool maze = style.Layout == "maze";
        if (area <= (maze ? 60 : 90) || area <= 200 && DetailHash(seed, depth, 1) < 0.45f || depth > 9)
        {
            Lot(c, plan, style, x0, y0, x1, y1, seed);
            return;
        }
        bool alley = area > (maze ? 140 : 260) && DetailHash(seed, depth, 2) < (maze ? 0.85f : style.AlleyP);
        bool deadEnd = maze && DetailHash(seed, depth, 4) < 0.5f;
        float cut = 0.35f + 0.3f * DetailHash(seed, depth, 3);
        Color32 alleyColor = Lerp(c.Block, White, maze ? 0.55f : 0.45f);
        if (w >= h)
        {
            int m = x0 + (int)(w * cut);
            if (alley)
            {
                int end = deadEnd ? y0 + (int)(h * (0.4f + 0.4f * DetailHash(seed, depth, 5))) : y1;
                for (int py = y0; py <= end; py++)
                    if (c.Kind(m, py) == KGround) c.Put(m, py, alleyColor, KAlley);
            }
            Subdivide(c, plan, style, x0, y0, alley ? m - 1 : m, y1, depth + 1, seed * 3 + 1);
            Subdivide(c, plan, style, m + 1, y0, x1, y1, depth + 1, seed * 3 + 2);
        }
        else
        {
            int m = y0 + (int)(h * cut);
            if (alley)
            {
                int end = deadEnd ? x0 + (int)(w * (0.4f + 0.4f * DetailHash(seed, depth, 5))) : x1;
                for (int px = x0; px <= end; px++)
                    if (c.Kind(px, m) == KGround) c.Put(px, m, alleyColor, KAlley);
            }
            Subdivide(c, plan, style, x0, y0, x1, alley ? m - 1 : m, depth + 1, seed * 3 + 1);
            Subdivide(c, plan, style, x0, m + 1, x1, y1, depth + 1, seed * 3 + 2);
        }
    }

    // 这块宅基地盖什么：看它落在哪个地块、哪一角，那一角排的是什么建筑(见 Fill)；没排建筑的多半空着
    private static void Lot(Canvas c, CityPlan plan, DetailStyle style, int x0, int y0, int x1, int y1, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 4 || h < 4) return;
        WorldTile tile = c.Tile((x0 + x1) / 2, (y0 + y1) / 2);
        TileZone zone = tile?.zone;
        if (zone == null) return;
        bool farm = FarmlandSystem.IsPlanned(c.City, zone);
        int size = Config.CITY_ZONE_SIZE, lx = tile.x - zone.x * size, ly = tile.y - zone.y * size;
        int quarter = (ly >= size / 2 ? 2 : 0) + (lx >= size / 2 ? 1 : 0);
        Slot[] slots = plan.zones.TryGetValue(zone.id, out Slot[] found) ? found : null;
        string icon = slots != null && quarter < slots.Length && slots[quarter].used ? slots[quarter].icon : null;
        float r = DetailHash(seed, 11);
        if (icon == null && r < 0.3f)
        {
            if (style.Gardens && w >= 9 && h >= 9) Charbagh(c, x0, y0, x1, y1);
            else Trees(c, style, x0, y0, x1, y1, seed);
            return;
        }
        if (farm || icon == null && r < 0.55f)
        {
            Plots(c, x0, y0, x1, y1, w > h, seed);
            return;
        }
        if (plan.ruin > 0f && DetailHash(seed, 12) < plan.ruin)
        {
            Ruin(c, x0, y0, x1, y1, seed);
            return;
        }
        Color32 roof = Pick(style.Roofs, DetailHash(seed, 13));
        string kind = icon ?? "house_1";
        // 下面各分支都盖房：记标注落点(街区编号 = 地块×4 + 哪一角)
        c.Labels.Add(new DetailLabel
        {
            Tile = c.TileAt((x0 + x1) / 2f, (y0 + y1) / 2f), Icon = kind, Key = zone.id * 4L + quarter, Weight = w * h
        });
        if ((kind == "temple" || kind.StartsWith("civic")) && w >= 10 && h >= 10)
        {
            TempleLot(c, style, x0, y0, x1, y1, kind == "temple");
            return;
        }
        // 小块宅基地上的官署：一排公廨
        if (kind.StartsWith("civic"))
        {
            Halls(c, x0, y0, x1, y1, roof);
            return;
        }
        if (kind.StartsWith("market"))
        {
            if (style.Market == "souk") Souk(c, style, x0, y0, x1, y1);
            else Shops(c, style, x0, y0, x1, y1, roof, seed);
            return;
        }
        if (kind == "barracks" || kind.StartsWith("industry") || kind == "docks")
        {
            Halls(c, x0, y0, x1, y1, kind == "barracks" ? roof : Workshop);
            return;
        }
        int tier = 1;
        if (kind.StartsWith("house_") && int.TryParse(kind.Substring(6), out int parsed)) tier = parsed;
        switch (style.House)
        {
            case "machiya": RowHouses(c, style, x0, y0, x1, y1, seed, 3, 4, 8, true); break;
            case "gabled": RowHouses(c, style, x0, y0, x1, y1, seed, 3, 5, 6, false); break;
            case "insula": Insula(c, x0, y0, x1, y1, roof); break;
            case "longhouse": Longhouse(c, x0, y0, x1, y1, roof, seed); break;
            case "flat": FlatHouse(c, x0, y0, x1, y1, roof, seed); break;
            case "mudbrick": Mudbrick(c, x0, y0, x1, y1, roof, seed); break;
            case "haveli": Haveli(c, style, x0, y0, x1, y1, tier, seed); break;
            default: Courtyard(c, x0, y0, x1, y1, tier, roof, seed); break;
        }
    }

    // 华夏宅子：小户两排(或一排)平房，中户三合院(背街正房、两侧厢房)，大户两进四合院；临街开门
    private static void Courtyard(Canvas c, int x0, int y0, int x1, int y1, int tier, Color32 roof, long seed)
    {
        int ax0 = x0 + (DetailHash(seed, 21) < 0.5f ? 1 : 0), ay0 = y0 + (DetailHash(seed, 22) < 0.5f ? 1 : 0);
        int ax1 = x1 - (DetailHash(seed, 23) < 0.4f ? 1 : 0), ay1 = y1 - (DetailHash(seed, 24) < 0.4f ? 1 : 0);
        int w = ax1 - ax0 + 1, h = ay1 - ay0 + 1;
        if (w < 4 || h < 4) return;
        Shadow(c, ax0, ay0, ax1, ay1);
        int side = StreetSide(c, ax0, ay0, ax1, ay1);
        bool horizontal = side <= 1;
        if (tier >= 5 && w >= 9 && h >= 9 || tier >= 3 && w >= 11 && h >= 11)
        {
            int d = Mathf.Min(w, h) < 12 ? 2 : 3;
            for (int py = ay0; py <= ay1; py++)
            for (int px = ax0; px <= ax1; px++)
                Paint(c, px, py, Court);
            Roof(c, ax0, ay0, ax1, ay0 + d - 1, true, roof);
            Roof(c, ax0, ay1 - d + 1, ax1, ay1, true, roof);
            Roof(c, ax0, ay0 + d, ax0 + d - 1, ay1 - d, false, roof);
            Roof(c, ax1 - d + 1, ay0 + d, ax1, ay1 - d, false, roof);
            if (h >= 14)
            {
                int m = (ay0 + ay1) / 2;
                Roof(c, ax0 + d, m, ax1 - d, m + 1, true, roof);
            }
            if (DetailHash(seed, 25) < 0.6f) Paint(c, (ax0 + ax1) / 2 + 1, (ay0 + ay1) / 2 - 2, TreeDark);
        }
        else if (tier >= 3 && w >= 6 && h >= 6)
        {
            for (int py = ay0; py <= ay1; py++)
            for (int px = ax0; px <= ax1; px++)
                Paint(c, px, py, Court);
            if (horizontal)
            {
                (int b0, int b1) = side == 0 ? (ay1 - 1, ay1) : (ay0, ay0 + 1);
                Roof(c, ax0, b0, ax1, b1, true, roof);
                Roof(c, ax0, ay0, ax0 + 1, ay1, false, roof);
                Roof(c, ax1 - 1, ay0, ax1, ay1, false, roof);
            }
            else
            {
                (int b0, int b1) = side == 2 ? (ax1 - 1, ax1) : (ax0, ax0 + 1);
                Roof(c, b0, ay0, b1, ay1, false, roof);
                Roof(c, ax0, ay0, ax1, ay0 + 1, true, roof);
                Roof(c, ax0, ay1 - 1, ax1, ay1, true, roof);
            }
        }
        else if (Mathf.Min(w, h) >= 7)
        {
            if (horizontal)
            {
                Roof(c, ax0, ay0, ax1, ay0 + 2, true, roof);
                Roof(c, ax0, ay1 - 2, ax1, ay1, true, roof);
                for (int py = ay0 + 3; py <= ay1 - 3; py++)
                for (int px = ax0; px <= ax1; px++)
                    Paint(c, px, py, Court);
            }
            else
            {
                Roof(c, ax0, ay0, ax0 + 2, ay1, false, roof);
                Roof(c, ax1 - 2, ay0, ax1, ay1, false, roof);
                for (int py = ay0; py <= ay1; py++)
                for (int px = ax0 + 3; px <= ax1 - 3; px++)
                    Paint(c, px, py, Court);
            }
        }
        else Roof(c, ax0, ay0, ax1, ay1, w >= h, roof);
        DoorAt(c, side, ax0, ay0, ax1, ay1);
    }

    // 日本町家、西欧尖顶排屋：沿街一户挨一户，门面窄、进深长，屋后一点园子(町家屋脊与街平行，排屋山墙朝街)
    private static void RowHouses(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1, long seed, int minW, int maxW, int maxDepth, bool ridgeAlong)
    {
        int side = StreetSide(c, x0, y0, x1, y1);
        bool horizontal = side <= 1;
        int length = horizontal ? x1 - x0 + 1 : y1 - y0 + 1, deep = horizontal ? y1 - y0 + 1 : x1 - x0 + 1;
        int depth = Mathf.Min(deep, maxDepth);
        for (int t = 0, k = 0; t < length; k++)
        {
            int width = minW + (int)(DetailHash(seed, k, 1) * (maxW - minW + 1));
            int a = (horizontal ? x0 : y0) + t, b = Mathf.Min(a + width - 1, horizontal ? x1 : y1);
            Color32 roof = Pick(style.Roofs, DetailHash(seed, k, 2));
            int ax0, ay0, ax1, ay1;
            if (horizontal)
            {
                ax0 = a;
                ax1 = b;
                if (side == 0) { ay0 = y0; ay1 = y0 + depth - 1; }
                else { ay1 = y1; ay0 = y1 - depth + 1; }
            }
            else
            {
                ay0 = a;
                ay1 = b;
                if (side == 2) { ax0 = x0; ax1 = x0 + depth - 1; }
                else { ax1 = x1; ax0 = x1 - depth + 1; }
            }
            Roof(c, ax0, ay0, ax1, ay1, ridgeAlong ? horizontal : !horizontal, roof);
            if (DetailHash(seed, k, 3) < 0.5f)
            {
                int gx = horizontal ? (ax0 + ax1) / 2 : side == 2 ? ax1 + 2 : ax0 - 2;
                int gy = horizontal ? side == 0 ? ay1 + 2 : ay0 - 2 : (ay0 + ay1) / 2;
                Paint(c, gx, gy, TreeDark, KGround);
            }
            t += width;
        }
    }

    // 罗马公寓街区：一圈陶红瓦房围着中庭(大的中庭里有绿地、水池)
    private static void Insula(Canvas c, int x0, int y0, int x1, int y1, Color32 roof)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        Shadow(c, x0, y0, x1, y1);
        if (w >= 9 && h >= 9)
        {
            int d = w >= 14 && h >= 14 ? 3 : 2;
            Roof(c, x0, y0, x1, y0 + d - 1, true, roof);
            Roof(c, x0, y1 - d + 1, x1, y1, true, roof);
            Roof(c, x0, y0 + d, x0 + d - 1, y1 - d, false, roof);
            Roof(c, x1 - d + 1, y0 + d, x1, y1 - d, false, roof);
            for (int py = y0 + d; py <= y1 - d; py++)
            for (int px = x0 + d; px <= x1 - d; px++)
                Paint(c, px, py, w >= 14 && (px + py) % 3 == 0 ? Rgb(120, 170, 100) : Rgb(236, 230, 216));
            if (w >= 12 && h >= 12)
            {
                int mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
                Paint(c, mx, my, Rgb(130, 180, 225));
                Paint(c, mx + 1, my, Rgb(130, 180, 225));
            }
        }
        else Roof(c, x0, y0, x1, y1, w >= h, roof);
    }

    // 维京长屋：两头圆、草皮顶；大院子围一圈篱笆
    private static void Longhouse(Canvas c, int x0, int y0, int x1, int y1, Color32 roof, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        bool horizontal = w >= h;
        if (w >= 10 && h >= 10)
        {
            for (int px = x0; px <= x1; px += 2)
            {
                Paint(c, px, y0, Fence, KGround);
                Paint(c, px, y1, Fence, KGround);
            }
            for (int py = y0; py <= y1; py += 2)
            {
                Paint(c, x0, py, Fence, KGround);
                Paint(c, x1, py, Fence, KGround);
            }
        }
        int length = Mathf.Max(5, (horizontal ? w : h) - 4), width = Mathf.Min(4, (horizontal ? h : w) - 3);
        if (width < 2) return;
        int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        int ax0 = horizontal ? cx - length / 2 : cx - width / 2, ay0 = horizontal ? cy - width / 2 : cy - length / 2;
        int ax1 = horizontal ? ax0 + length - 1 : ax0 + width - 1, ay1 = horizontal ? ay0 + width - 1 : ay0 + length - 1;
        Roof(c, ax0, ay0, ax1, ay1, horizontal, roof);
        foreach ((int px, int py) in new[] { (ax0, ay0), (ax1, ay0), (ax0, ay1), (ax1, ay1) })
            if (c.Kind(px, py) == KBuilding) c.Put(px, py, c.Block, KGround);
        if (DetailHash(seed, 26) < 0.5f)
        {
            int sx = horizontal ? ax1 + 2 : ax0, sy = horizontal ? ay0 : ay1 + 2;
            Roof(c, sx, sy, sx + 1, sy + 1, true, Darker(roof, 1.1f));
        }
    }

    // 平顶房(阿拉伯、波斯、阿兹特克)：一家挨一家，各有小天井
    private static void FlatHouse(Canvas c, int x0, int y0, int x1, int y1, Color32 roof, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        Color32 tone = Darker(roof, 0.94f + 0.1f * DetailHash(seed, 27));
        Flat(c, x0, y0, x1, y1, tone);
        if (w >= 7 && h >= 7)
        {
            int cw = Mathf.Max(2, w / 3), ch = Mathf.Max(2, h / 3);
            int cx0 = x0 + (w - cw) / 2 + Mathf.RoundToInt((DetailHash(seed, 28) - 0.5f) * 2f), cy0 = y0 + (h - ch) / 2;
            for (int py = cy0; py < cy0 + ch; py++)
            for (int px = cx0; px < cx0 + cw; px++)
                Paint(c, px, py, Darker(tone, 0.8f));
            if (DetailHash(seed, 29) < 0.5f) Paint(c, cx0, cy0, TreeDark);
        }
        if (w >= 10 && h >= 4) Paint(c, x0 + (int)(w * DetailHash(seed, 30)), y0 + 1, Darker(tone, 0.7f));
    }

    // 埃及泥砖房：平顶，院里椰枣树
    private static void Mudbrick(Canvas c, int x0, int y0, int x1, int y1, Color32 roof, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        Shadow(c, x0 + 1, y0 + 1, x1 - 1, y1 - 1);
        if (w >= 8 && h >= 7)
        {
            Flat(c, x0 + 1, y0 + 1, x1 - 1, y0 + h / 2, Darker(roof, 0.95f + 0.1f * DetailHash(seed, 27)));
            for (int py = y0 + h / 2 + 1; py < y1; py++)
            for (int px = x0 + 1; px < x1; px++)
                Paint(c, px, py, Rgb(226, 210, 172));
            Paint(c, x0 + 2, y1 - 2, Palm);
            Paint(c, x1 - 3, y1 - 2, Palm);
        }
        else Flat(c, x0 + 1, y0 + 1, x1 - 1, y1 - 1, roof);
    }

    // 印度大宅：彩色平顶围方院，房顶四角小亭
    private static void Haveli(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1, int tier, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        Color32 tone = Pick(style.Roofs, DetailHash(seed, 27));
        Shadow(c, x0, y0, x1, y1);
        Flat(c, x0, y0, x1, y1, tone);
        if (w < 8 || h < 8) return;
        for (int py = y0 + 3; py <= y1 - 3; py++)
        for (int px = x0 + 3; px <= x1 - 3; px++)
            Paint(c, px, py, Rgb(236, 224, 200));
        foreach ((int px, int py) in new[] { (x0 + 1, y0 + 1), (x1 - 1, y0 + 1), (x0 + 1, y1 - 1), (x1 - 1, y1 - 1) })
            Paint(c, px, py, Darker(tone, 1.3f));
        if (tier >= 4) Paint(c, (x0 + x1) / 2, (y0 + y1) / 2, TreeDark);
    }

    // 庙、衙署：一圈墙，院里铺地，各系的殿
    private static void TempleLot(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1, bool temple)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        Color32 wall = style.FlatRoofs ? Darker(style.Roofs[0], 0.82f) : temple ? PalaceWall : Rgb(150, 60, 50);
        Shadow(c, x0, y0, x1, y1);
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
            Paint(c, px, py, px == x0 || px == x1 || py == y0 || py == y1 ? wall : TempleYard);
        switch (style.Temple)
        {
            case "chapel":
                Roof(c, cx - 2, y0 + 2, cx + 2, y1 - 2, false, Rgb(100, 100, 112));
                for (int py = y0 + 1; py <= y0 + 3; py++)
                for (int px = cx - 2; px <= cx + 2; px++)
                    if (c.Kind(px, py) == KBuilding) c.Put(px, py, Rgb(130, 126, 120), KBuilding);
                break;
            case "roman_temple":
                Roof(c, cx - 3, y0 + 3, cx + 3, y1 - 2, false, Rgb(192, 112, 82));
                for (int px = cx - 3; px <= cx + 3; px += 2) Paint(c, px, y0 + 2, Rgb(244, 240, 230));
                break;
            case "longhall":
                Roof(c, x0 + 2, cy - 2, x1 - 2, cy + 2, true, Rgb(104, 112, 74));
                break;
            case "small_mosque":
                Flat(c, x0 + 1, cy, x1 - 1, y1 - 1, Darker(style.Roofs[0], 0.95f));
                Dome(c, cx, (cy + y1) / 2f, 3f, style.Dome, KBuilding);
                if (c.Kind(x1 - 1, y0 + 1) == KBuilding) c.Put(x1 - 1, y0 + 1, Rgb(244, 238, 222), KBuilding);
                break;
            case "pylon_small":
                Flat(c, x0 + 1, y0 + 1, cx - 1, y0 + 3, Rgb(200, 176, 128));
                Flat(c, cx + 1, y0 + 1, x1 - 1, y0 + 3, Rgb(200, 176, 128));
                Flat(c, cx - 3, cy, cx + 3, y1 - 2, Rgb(206, 184, 140));
                break;
            case "shikhara_small":
                for (int sq = Mathf.Min(w, h) / 2 - 2; sq >= 1; sq -= 2)
                for (int py = cy - sq; py <= cy + sq; py++)
                for (int px = cx - sq; px <= cx + sq; px++)
                    if (c.Kind(px, py) == KBuilding) c.Put(px, py, Darker(Rgb(226, 182, 140), 0.85f + sq * 0.02f), KBuilding);
                break;
            case "pyramid_small":
                for (int sq = Mathf.Min(w, h) / 2 - 1; sq >= 1; sq--)
                for (int py = cy - sq; py <= cy + sq; py++)
                for (int px = cx - sq; px <= cx + sq; px++)
                    if (c.Kind(px, py) == KBuilding) c.Put(px, py, sq % 2 == 1 ? Rgb(190, 180, 160) : Rgb(214, 206, 186), KBuilding);
                if (c.Kind(cx, cy) == KBuilding) c.Put(cx, cy, Rgb(170, 70, 50), KBuilding);
                break;
            default:
                Roof(c, x0 + 2, cy + 1, x1 - 2, y1 - 2, true, TempleRoof);
                if (h >= 12) Roof(c, x0 + 3, y0 + 2, x1 - 3, y0 + 3, true, TempleRoof);
                break;
        }
        DoorAt(c, StreetSide(c, x0, y0, x1, y1), x0, y0, x1, y1);
    }

    // 铺面：一排窄门面临街，门前支着摊棚
    private static void Shops(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1, Color32 roof, long seed)
    {
        int side = StreetSide(c, x0, y0, x1, y1);
        bool horizontal = side <= 1;
        int length = horizontal ? x1 - x0 + 1 : y1 - y0 + 1;
        for (int t = 0, k = 0; t < length; k++)
        {
            int width = 3 + (int)(DetailHash(seed, k, 41) * 2);
            int a = (horizontal ? x0 : y0) + t, b = Mathf.Min(a + width - 1, horizontal ? x1 : y1);
            Color32 tone = Darker(roof, 0.9f + 0.25f * DetailHash(seed, k, 42));
            if (style.FlatRoofs)
            {
                if (horizontal) Flat(c, a, y0 + 1, b, y1 - 1, tone);
                else Flat(c, x0 + 1, a, x1 - 1, b, tone);
            }
            else if (horizontal) Roof(c, a, y0 + 1, b, y1 - 1, false, tone);
            else Roof(c, x0 + 1, a, x1 - 1, b, true, tone);
            int stall = (a + b) / 2;
            Color32 awning = DetailHash(seed, k, 43) < 0.5f ? Stall : StallLight;
            switch (side)
            {
                case 0: Paint(c, stall, y0, awning); break;
                case 1: Paint(c, stall, y1, awning); break;
                case 2: Paint(c, x0, stall, awning); break;
                default: Paint(c, x1, stall, awning); break;
            }
            t += width + 1;
        }
    }

    // 巴扎：有顶的长条集市，两侧一格格摊位
    private static void Souk(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1)
    {
        bool horizontal = x1 - x0 >= y1 - y0;
        int middle = horizontal ? (y0 + y1) / 2 : (x0 + x1) / 2;
        Color32 tone = Darker(style.Roofs[0], 0.95f);
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            int along = horizontal ? px : py, across = horizontal ? py : px;
            bool covered = Mathf.Abs(across - middle) <= 1;
            Paint(c, px, py, covered ? along % 4 == 0 ? Rgb(196, 170, 120) : Rgb(214, 192, 146) : along % 3 == 0 ? Stall : tone);
        }
    }

    // 营房、作坊、仓库：一院几排长屋
    private static void Halls(Canvas c, int x0, int y0, int x1, int y1, Color32 roof)
    {
        Shadow(c, x0, y0, x1, y1);
        bool horizontal = x1 - x0 >= y1 - y0;
        int span = horizontal ? y1 - y0 + 1 : x1 - x0 + 1;
        for (int t = 1; t + 2 < span; t += 4)
        {
            if (horizontal) Roof(c, x0 + 1, y0 + t, x1 - 1, y0 + t + 2, true, roof);
            else Roof(c, x0 + t, y0 + 1, x0 + t + 2, y1 - 1, false, roof);
        }
    }

    private static void Trees(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1, long seed)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            float r = DetailHash(seed, px, py, 51);
            if (style.Palms && r < 0.12f) Paint(c, px, py, Palm, KGround);
            else if (r < 0.35f) Paint(c, px, py, TreeDark, KGround);
            else if (r < 0.5f) Paint(c, px, py, TreeLight, KGround);
        }
    }

    // 波斯园林：十字水渠把园子分成四块
    private static void Charbagh(Canvas c, int x0, int y0, int x1, int y1)
    {
        int mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            bool water = px == mx || py == my, rim = px == x0 || px == x1 || py == y0 || py == y1;
            Paint(c, px, py, water ? Rgb(120, 170, 220) : rim ? Rgb(200, 186, 150) : (px + py) % 3 != 0 ? Rgb(110, 160, 90) : Rgb(140, 186, 110), KGround);
        }
    }

    // 菜园：一畦一畦
    // 农耕区的地块：几块条田(见 FieldColor)，块间、四周留空地，约三成的块不种
    private static void Plots(Canvas c, int x0, int y0, int x1, int y1, bool horizontal, long seed)
    {
        for (int by = y0 + 1; by < y1 - 3; by += 9)
        for (int bx = x0 + 1; bx < x1 - 4; bx += 12)
        {
            if (DetailHash(seed, bx, by, 611) > 0.7f) continue;
            int w = Mathf.Min(x1 - bx, 8 + (int)(DetailHash(seed, bx, by, 612) * 3));
            int h = Mathf.Min(y1 - by, 5 + (int)(DetailHash(seed, bx, by, 613) * 2));
            bool vertical = DetailHash(seed, bx, by, 614) < (horizontal ? 0.3f : 0.7f);
            for (int py = by; py < by + h; py++)
            for (int px = bx; px < bx + w; px++)
                Paint(c, px, py, FieldColor(c.Block, px, py, vertical), KGround);
        }
    }

    // 废墟：残墙断瓦
    private static void Ruin(Canvas c, int x0, int y0, int x1, int y1, long seed)
    {
        Color32 rubble = Rgb(120, 108, 96), burnt = Rgb(70, 62, 58);
        for (int py = y0 + 1; py < y1; py++)
        for (int px = x0 + 1; px < x1; px++)
        {
            float r = DetailHash(seed, px, py, 61);
            bool edge = px == x0 + 1 || px == x1 - 1 || py == y0 + 1 || py == y1 - 1;
            if (edge && r < 0.6f) Paint(c, px, py, rubble);
            else if (r < 0.15f) Paint(c, px, py, burnt);
        }
    }

    #endregion

    #region 城墙

    // 方塔(华夏、罗马、阿拉伯、埃及)：外沿垛口一明一暗、内侧马道，四角、门两侧与每隔几格一座敌楼，城门楼红墙黑瓦；
    // 圆塔(西欧、波斯、印度)：墙身同上，四角与每隔几格一座圆塔；木栅(维京)：只占靠外两像素，尖桩一明一暗
    private static void Walls(Canvas c, CityPlan plan, DetailStyle style, Color32 wall)
    {
        int s = DetailScale;
        Color32 light = Lerp(wall, White, 0.28f), shade = Darker(wall, 0.74f), walk = Lerp(wall, White, 0.4f),
            tower = TowerOf(wall), towerTop = Darker(tower, 1.2f);
        var rounds = new List<(int x, int y)>();
        for (int ty = 0; ty < c.H; ty++)
        for (int tx = 0; tx < c.W; tx++)
        {
            if (c.Kind(tx * s, ty * s) != KWall) continue;
            WorldTile tile = World.world.GetTile(c.X0 + tx, c.Y0 + ty);
            if (tile == null) continue;
            bool gate = tile.x == plan.gateX || tile.y == plan.gateY;
            bool corner = Corner(tile, plan);
            bool squareTower = style.Tower == "square" &&
                               (Mathf.Abs(tile.x - plan.gateX) == 1 || Mathf.Abs(tile.y - plan.gateY) == 1 || corner || (tile.x + tile.y * 3) % 7 == 0);
            if (style.Tower == "round" && !gate && (corner || (tile.x + tile.y * 3) % 6 == 0)) rounds.Add((tx * s + 2, ty * s + 2));
            bool outUp = !Urban(tile.tile_up, plan), outDown = !Urban(tile.tile_down, plan),
                outLeft = !Urban(tile.tile_left, plan), outRight = !Urban(tile.tile_right, plan);
            for (int sy = 0; sy < s; sy++)
            for (int sx = 0; sx < s; sx++)
            {
                int px = tx * s + sx, py = ty * s + sy;
                if (gate)
                {
                    c.Put(px, py, style.Tower == "palisade" ? Rgb(150, 120, 84)
                        : sx == 0 || sx == s - 1 || sy == 0 || sy == s - 1 ? Rgb(180, 70, 55) : Rgb(60, 54, 58), KWall);
                    continue;
                }
                bool outer = outLeft && sx == 0 || outRight && sx == s - 1 || outDown && sy == 0 || outUp && sy == s - 1;
                if (style.Tower == "palisade")
                {
                    bool stake = outLeft && sx <= 1 || outRight && sx >= s - 2 || outDown && sy <= 1 || outUp && sy >= s - 2;
                    if (stake) c.Put(px, py, (px + py) % 2 == 1 ? Rgb(150, 116, 76) : Rgb(108, 82, 54), KWall);
                    else c.Put(px, py, c.Block, KGround);
                    continue;
                }
                Color32 color;
                if (squareTower) color = sx > 0 && sx < s - 1 && sy > 0 && sy < s - 1 ? towerTop : tower;
                else
                {
                    bool inner = !outLeft && sx == 0 || !outRight && sx == s - 1 || !outDown && sy == 0 || !outUp && sy == s - 1;
                    color = outer ? (px + py) % 2 == 0 ? light : shade : inner ? walk : wall;
                }
                c.Put(px, py, color, KWall);
            }
        }
        foreach ((int x, int y) in rounds)
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 3.2f || c.Kind(x + dx, y + dy) == KOff) continue;
                c.Put(x + dx, y + dy, d > 2.2f ? tower : towerTop, KWall);
            }
    }

    #endregion

    #endregion

    #region 城外

    private static void DrawCountryside(Canvas c, UrbanFabric.Geometry g, DetailStyle style, Color32 tint, long seed)
    {
        int s = DetailScale;
        Color32 dirt = Lerp(tint, Dirt, 0.8f), ground = Lerp(tint, Threshing, 0.85f), townGround = Lerp(tint, Rgb(238, 228, 200), 0.85f);
        DrawRoads(c, g, tint);
        // 村镇、小路、废墟画全：压到邻城地块上的那一截照画，不在城界上截掉(用户 2026-10-17 "而且村镇会被截掉")；田块照旧只种在本城地上
        c.Spill = true;
        // 村镇的小路：沿折线一像素宽的土路(弯法见 UrbanFabric.PlaceTrails)，只铺在空地上，过水、过房子就断开
        foreach (Vector2[] trail in g.Paths)
            for (int k = 0; k + 1 < trail.Length; k++)
            {
                Vector2 a = trail[k], b = trail[k + 1];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * s * 1.5f));
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)n);
                    int px = Mathf.RoundToInt((p.x - c.X0 + 0.5f) * s), py = Mathf.RoundToInt((p.y - c.Y0 + 0.5f) * s);
                    if (c.Kind(px, py) == KEmpty && c.Land(px, py)) c.Put(px, py, dirt, KPath);
                }
            }
        // 关厢：城门外沿大路两旁一户挨一户，越远越稀
        if (g.Suburb > 0)
            foreach (UrbanFabric.Road road in g.Roads)
            {
                if (!road.Gate) continue;
                for (int d = 2; d <= 1 + g.Suburb && d <= road.Length; d++)
                {
                    (int rx, int ry) = UrbanFabric.RoadPoint(road, d);
                    for (int sign = -1; sign <= 1; sign += 2)
                    for (int k = 0; k < 2; k++)
                    {
                        if (DetailHash(seed, d * 4 + k, sign, 71) > 0.85f - d * 0.04f) continue;
                        float offset = 2.4f + DetailHash(seed, d, k, 72) * 1.2f;
                        int px = Mathf.RoundToInt((rx - c.X0 + 0.5f - road.Dy * sign * offset) * s + road.Dx * k * 2f);
                        int py = Mathf.RoundToInt((ry - c.Y0 + 0.5f + road.Dx * sign * offset) * s + road.Dy * k * 2f);
                        SmallHouse(c, style, px - 2, py - 1, seed + d * 10 + k + (sign > 0 ? 5 : 0), DetailHash(seed, d, k, 73) < 0.3f);
                    }
                }
            }
        int index = 0;
        bool Swallowed(int px, int py) => g.Modern && c.Kind(px, py) is not (KEmpty or KPath);
        // 毡帐村镇(见 RuralSettlement.yurt)：踩出来的空地上一圈圈毡帐，同游牧营地
        Color32 trampled = Lerp(tint, Rgb(196, 186, 140), 0.6f);
        foreach ((int x, int y, int townSize, bool walled) in g.Towns)
        {
            int px = (x - c.X0) * s + s / 2, py = (y - c.Y0) * s + s / 2;
            index++;
            if (Swallowed(px, py)) continue;
            if (UrbanFabric.IsYurt(g, x, y)) YurtTown(c, px, py, townSize, walled, trampled, UrbanFabric.SettlementKey(x, y) + 1000);
            else Town(c, OwnStyle(g, x, y, style), px, py, townSize, walled, dirt, townGround, UrbanFabric.SettlementKey(x, y) + 1000);
        }
        index = 0;
        foreach ((int x, int y) in g.Villages)
        {
            int px = (x - c.X0) * s + s / 2, py = (y - c.Y0) * s + s / 2;
            index++;
            if (Swallowed(px, py)) continue;
            if (UrbanFabric.IsYurt(g, x, y)) YurtVillage(c, px, py, trampled, UrbanFabric.SettlementKey(x, y) + 3000);
            else Village(c, OwnStyle(g, x, y, style), tint, px, py, dirt, ground, UrbanFabric.SettlementKey(x, y) + 3000);
        }
        // 关城(见 RuralSettlementSystem)：一圈城墙、四角墩台、南门，里面营房校场、一杆旗
        foreach ((int x, int y, int level) in g.Forts)
        {
            Fort(c, (x - c.X0) * s + s / 2, (y - c.Y0) * s + s / 2, level, UrbanFabric.SettlementKey(x, y) + 7000);
            GateStubs(c, x, y, level);
        }
        // 毁于兵火的镇村(见 RuralSettlementSystem)：一片焦土，残垣断瓦
        // 毡帐烧了只剩焦土，没有残墙
        foreach ((int x, int y, bool town) in g.Ruins)
            RuralRuin(c, (x - c.X0) * s + s / 2, (y - c.Y0) * s + s / 2, town ? 16f : 7f, UrbanFabric.SettlementKey(x, y) + 5000, tint,
                !UrbanFabric.IsYurt(g, x, y));
        c.Spill = false;
        // 村镇四周按生计画(见 LivelihoodSystem)，一块块之间至少留三像素空地，只落在本城的空地上：
        //   · 农村：几块条田(见 FieldColor)，一块八到十三像素宽、六到九像素高，能种的不到七成就换个地方(阿兹特克是水上浮田)；
        //   · 林场村：几片林子(深浅两色的树冠，边缘参差)，村边两三垛原木；
        //   · 矿村：一两处矿坑(外圈土黄、里面一层深过一层)，坑边灰白的碴堆，山地上也开；
        //   · 牧村：几块围着篱笆的草场，草场里几只羊；
        //   · 渔村：一两块菜地，近处水面上几条小船。
        var fields = new List<(int x0, int y0, int x1, int y1)>();
        bool Own(int px, int py, bool rough)
        {
            if (c.Kind(px, py) != KEmpty) return false;
            WorldTile tile = c.Tile(px, py);
            if (tile?.Type == null || tile.Type.liquid || tile.Type.block || !rough && tile.Type.mountains) return false;
            return tile.zone?.city == c.City;
        }
        bool Farmable(int px, int py)
        {
            if (c.Kind(px, py) != KEmpty) return false;
            WorldTile tile = c.Tile(px, py);
            return tile?.Type != null && (tile.Type.can_be_farm || tile.Type.farm_field) && tile.zone?.city == c.City;
        }
        // 在 (cx, cy) 四周 reach 像素内找 count 块 w×h 的空地(能用的像素够七成)，找到一块画一块
        void Patches(int cx, int cy, int count, float reach, long key, int wMin, int wSpan, int hMin, int hSpan,
            Func<int, int, bool> usable, Action<int, int, int, int, int> paint)
        {
            int placed = 0;
            for (int attempt = 0; attempt < count * 4 && placed < count; attempt++)
            {
                float angle = DetailHash(key, attempt, 621) * Mathf.PI * 2f, dist = reach * (0.35f + 0.65f * DetailHash(key, attempt, 622));
                int w = wMin + (int)(DetailHash(key, attempt, 623) * wSpan), h = hMin + (int)(DetailHash(key, attempt, 624) * hSpan);
                int x0 = Mathf.RoundToInt(cx + Mathf.Cos(angle) * dist) - w / 2, y0 = Mathf.RoundToInt(cy + Mathf.Sin(angle) * dist) - h / 2;
                int x1 = x0 + w - 1, y1 = y0 + h - 1;
                if (fields.Exists(f => x0 <= f.x1 + 3 && x1 >= f.x0 - 3 && y0 <= f.y1 + 3 && y1 >= f.y0 - 3)) continue;
                int ok = 0;
                for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                    if (usable(px, py)) ok++;
                if (ok * 10 < w * h * 7) continue;
                fields.Add((x0, y0, x1, y1));
                placed++;
                paint(x0, y0, x1, y1, attempt);
            }
        }
        void Fields(int cx, int cy, int count, float reach, long key) =>
            Patches(cx, cy, count, reach, key, 8, 6, 6, 4, Farmable, (x0, y0, x1, y1, attempt) =>
            {
                bool vertical = DetailHash(key, attempt, 625) < 0.4f;
                for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    if (!Farmable(px, py)) continue;
                    if (style.Layout == "canal")
                    {
                        if (py % 4 == 0) c.Put(px, py, new Color32(140, 185, 235, 160), KEmpty);
                        continue;
                    }
                    c.Put(px, py, FieldColor(tint, px, py, vertical), KEmpty);
                }
            });
        // 一块里的椭圆(边缘按哈希参差)
        bool InBlob(int px, int py, int x0, int y0, int x1, int y1, long key)
        {
            float rx = (x1 - x0 + 1) / 2f, ry = (y1 - y0 + 1) / 2f;
            float dx = (px - x0 + 0.5f - rx) / rx, dy = (py - y0 + 0.5f - ry) / ry;
            return dx * dx + dy * dy <= 0.75f + 0.25f * DetailHash(key, px, py, 630);
        }
        void Woods(int cx, int cy, int count, float reach, long key)
        {
            Patches(cx, cy, count, reach, key, 9, 6, 8, 5, (px, py) => Own(px, py, false), (x0, y0, x1, y1, _) =>
            {
                for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    if (!InBlob(px, py, x0, y0, x1, y1, key) || !Own(px, py, false)) continue;
                    bool light = DetailHash(key, px * 3 + 1, py * 5 + 2, 634) < 0.3f;
                    c.Put(px, py, Lerp(tint, light ? TreeLight : TreeDark, 0.88f), KEmpty);
                }
            });
            // 伐木场：村边两三垛原木
            for (int n = 0; n < 3; n++)
            {
                int lx = cx + 4 + n * 3, ly = cy + 5 + (int)(DetailHash(key, n, 635) * 3);
                for (int k = 0; k < 2; k++)
                    if (Own(lx + k, ly, false)) c.Put(lx + k, ly, Rgb(150, 112, 72), KEmpty);
            }
        }
        void Pits(int cx, int cy, int count, float reach, long key)
        {
            Patches(cx, cy, count, reach, key, 7, 4, 6, 3, (px, py) => Own(px, py, true), (x0, y0, x1, y1, attempt) =>
            {
                float rx = (x1 - x0 + 1) / 2f, ry = (y1 - y0 + 1) / 2f;
                for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    if (!Own(px, py, true)) continue;
                    float dx = (px - x0 + 0.5f - rx) / rx, dy = (py - y0 + 0.5f - ry) / ry, d = dx * dx + dy * dy;
                    if (d > 1f) continue;
                    Color32 color = d > 0.55f ? Rgb(178, 158, 120) : d > 0.2f ? Rgb(130, 116, 98) : Rgb(92, 84, 76);
                    c.Put(px, py, Lerp(tint, color, 0.9f), KEmpty);
                }
                // 碴堆：坑边两三点灰白
                for (int k = 0; k < 3; k++)
                {
                    int hx = DetailHash(key, attempt, k, 636) < 0.5f ? x0 - 1 : x1 + 1;
                    int hy = y0 + (int)(DetailHash(key, attempt, k, 637) * (y1 - y0 + 1));
                    if (Own(hx, hy, true)) c.Put(hx, hy, Rgb(196, 190, 178), KEmpty);
                }
            });
        }
        void Paddocks(int cx, int cy, int count, float reach, long key)
        {
            Patches(cx, cy, count, reach, key, 9, 5, 7, 3, (px, py) => Own(px, py, false), (x0, y0, x1, y1, attempt) =>
            {
                for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    if (!Own(px, py, false)) continue;
                    bool edge = px == x0 || px == x1 || py == y0 || py == y1;
                    c.Put(px, py, edge ? Lerp(tint, Fence, 0.9f) : Lerp(tint, Rgb(184, 210, 132), 0.85f), KEmpty);
                }
                // 羊
                int sheep = 2 + (int)(DetailHash(key, attempt, 638) * 3);
                for (int k = 0; k < sheep; k++)
                {
                    int sx = x0 + 1 + (int)(DetailHash(key, attempt, k, 639) * (x1 - x0 - 1));
                    int sy = y0 + 1 + (int)(DetailHash(key, attempt, k, 640) * (y1 - y0 - 1));
                    c.Put(sx, sy, Rgb(244, 240, 230), KEmpty);
                }
            });
        }
        // 毡帐的畜栏：一圈木栏围着几只羊，不铺绿草场(毡帐都在旱地上，见 RuralSettlementSystem.YurtGround)，同游牧营地里的畜栏
        void Corrals(int cx, int cy, int count, float reach, long key)
        {
            Patches(cx, cy, count, reach, key, 8, 4, 6, 3, (px, py) => Own(px, py, false), (x0, y0, x1, y1, attempt) =>
            {
                for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    bool rim = px == x0 || px == x1 || py == y0 || py == y1;
                    if (rim && (px + py) % 2 == 0 && Own(px, py, false)) c.Put(px, py, Lerp(tint, Fence, 0.9f), KEmpty);
                }
                int sheep = 2 + (int)(DetailHash(key, attempt, 643) * 3);
                for (int k = 0; k < sheep; k++)
                {
                    int sx = x0 + 1 + (int)(DetailHash(key, attempt, k, 644) * (x1 - x0 - 1));
                    int sy = y0 + 1 + (int)(DetailHash(key, attempt, k, 645) * (y1 - y0 - 1));
                    if (Own(sx, sy, false)) c.Put(sx, sy, Rgb(244, 240, 230), KEmpty);
                }
            });
        }
        void Boats(int cx, int cy, int count, float reach, long key)
        {
            bool Water(int px, int py)
            {
                if (c.Kind(px, py) is not (KEmpty or KWater)) return false;
                WorldTile tile = c.Tile(px, py);
                return tile?.Type != null && tile.Type.liquid && !tile.Type.lava;
            }
            int placed = 0;
            for (int attempt = 0; attempt < count * 16 && placed < count; attempt++)
            {
                float angle = DetailHash(key, attempt, 641) * Mathf.PI * 2f, dist = reach * (0.4f + 1.2f * DetailHash(key, attempt, 642));
                int bx = Mathf.RoundToInt(cx + Mathf.Cos(angle) * dist), by = Mathf.RoundToInt(cy + Mathf.Sin(angle) * dist);
                if (!Water(bx - 1, by) || !Water(bx, by) || !Water(bx + 1, by) || !Water(bx, by - 1) || !Water(bx + 2, by)) continue;
                for (int k = -1; k <= 1; k++) c.Put(bx + k, by, Rgb(118, 84, 54), KFixed);
                c.Put(bx, by - 1, Rgb(246, 242, 232), KFixed);
                placed++;
            }
        }
        void Surround(int x, int y, bool town, int townSize)
        {
            long key = UrbanFabric.SettlementKey(x, y);
            int cx = (x - c.X0) * s + 2, cy = (y - c.Y0) * s + 2;
            int many = town ? 6 + (int)(DetailHash(key, 627) * 4) : 4 + (int)(DetailHash(key, 626) * 3);
            float reach = town ? (townSize + 6) * s : 7f * s;
            long salt = key + (town ? 9000 : 7000);
            switch (LivelihoodSystem.At(x, y))
            {
                case LivelihoodSystem.Forest:
                    Woods(cx, cy, many - 1, reach, salt + 100);
                    Fields(cx, cy, 1, reach * 0.6f, salt);
                    break;
                case LivelihoodSystem.Mine:
                    Pits(cx, cy, town ? 3 : 2, reach * 0.8f, salt + 200);
                    Fields(cx, cy, 1, reach * 0.6f, salt);
                    break;
                case LivelihoodSystem.Pasture:
                    if (UrbanFabric.IsYurt(g, x, y)) Corrals(cx, cy, town ? 4 : 2, reach * 0.9f, salt + 300);
                    else Paddocks(cx, cy, many - 1, reach, salt + 300);
                    break;
                case LivelihoodSystem.Fish:
                    Fields(cx, cy, 2, reach * 0.6f, salt);
                    Boats(cx, cy, town ? 5 : 3, reach * 1.6f, salt + 400);
                    break;
                default:
                    Fields(cx, cy, many, reach, salt);
                    break;
            }
        }
        foreach ((int x, int y) in g.Villages) Surround(x, y, false, 0);
        foreach ((int x, int y, int townSize, _) in g.Towns) Surround(x, y, true, townSize);
    }

    // 出城的大路、岔路(粗图上一格一像素的白路，见 UrbanFabric.Countryside)：照同一条线画成连续的路——古代是浅色土路、
    // 两侧一道暗边，近代以后是柏油路(路肩暗、中线黄虚线)；只铺在空地、小路上，城里的街和房子不压。
    // 用户 2026-10-16 "这个马路也是到了边缘就没有过渡了"：以前精细图不画城外的路，出了城就是粗图上一格一格的路
    private static void DrawRoads(Canvas c, UrbanFabric.Geometry g, Color32 tint)
    {
        int s = DetailScale;
        Color32 core = Lerp(tint, Rgb(244, 238, 222), 0.85f), rim = Lerp(tint, Rgb(186, 170, 136), 0.7f);
        // 粗图上这条路一格宽的那些格先还成地面(按地形调过的国色)，免得连续的路两旁露出一格一格的白边
        foreach (UrbanFabric.Road road in g.Roads)
            for (int d = 0; d <= road.Length; d++)
            {
                (int rx, int ry) = UrbanFabric.RoadPoint(road, d);
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int x = rx + ox, y = ry + oy, tx = x - c.X0, ty = y - c.Y0;
                    if (tx < 0 || ty < 0 || tx >= c.W || ty >= c.H || !UrbanFabric.OnRoadTile(road, x, y)) continue;
                    WorldTile tile = World.world.GetTile(x, y);
                    if (tile?.Type == null || tile.Type.liquid) continue;
                    Color32 ground = Shade(tile, tint);
                    for (int sy = 0; sy < s; sy++)
                    for (int sx = 0; sx < s; sx++)
                    {
                        int px = tx * s + sx, py = ty * s + sy;
                        if (c.Kind(px, py) == KEmpty && c.Pixels[py * c.PW + px].a == 0 && c.Land(px, py)) c.Put(px, py, ground, KEmpty);
                    }
                }
            }
        var best = new Dictionary<int, (float d, float along)>();
        foreach (UrbanFabric.Road road in g.Roads)
        {
            bool wide = road.Gate || road.Link;
            float half = g.Modern ? wide ? 1.9f : 1.4f : wide ? 1.1f : 0.8f, reach = half + 0.8f;
            int steps = Mathf.Max(1, road.Length * s * 2);
            for (int k = 0; k <= steps; k++)
            {
                float t = k / (float)steps * road.Length;
                Vector2 p = UrbanFabric.RoadPointF(road, t);
                float fx = (p.x - c.X0 + 0.5f) * s, fy = (p.y - c.Y0 + 0.5f) * s;
                int r = Mathf.CeilToInt(reach);
                for (int py = Mathf.FloorToInt(fy) - r; py <= Mathf.CeilToInt(fy) + r; py++)
                for (int px = Mathf.FloorToInt(fx) - r; px <= Mathf.CeilToInt(fx) + r; px++)
                {
                    if (!c.In(px, py)) continue;
                    float d = Mathf.Sqrt((px + 0.5f - fx) * (px + 0.5f - fx) + (py + 0.5f - fy) * (py + 0.5f - fy)) / half;
                    if (d > reach / half) continue;
                    int i = py * c.PW + px;
                    if (!best.TryGetValue(i, out var old) || d < old.d) best[i] = (d, t * s);
                }
            }
        }
        foreach (KeyValuePair<int, (float d, float along)> pair in best)
        {
            int px = pair.Key % c.PW, py = pair.Key / c.PW;
            if (c.Kind(px, py) is not (KEmpty or KPath) || !c.Land(px, py)) continue;
            (float d, float along) = pair.Value;
            Color32 color = g.Modern
                ? d > 1f ? AsphaltEdge : d < 0.3f && Mathf.FloorToInt(along) % 10 < 4 ? LaneMark : Asphalt
                : d > 1f ? rim : core;
            c.Put(px, py, color, KAvenue);
        }
    }

    // 城外的废墟：焦土一圈，残墙一段段(断开)，碎瓦、焦黑点点；毡帐的废墟没有残墙(walls 为 false)
    private static void RuralRuin(Canvas c, int cx, int cy, float r, long seed, Color32 tint, bool walls = true)
    {
        Color32 scorched = Lerp(tint, Rgb(112, 100, 84), 0.6f), rubble = Rgb(132, 118, 102), burnt = Rgb(66, 58, 54),
            wall = Rgb(150, 138, 120);
        int reach = Mathf.CeilToInt(r);
        for (int py = cy - reach; py <= cy + reach; py++)
        for (int px = cx - reach; px <= cx + reach; px++)
        {
            float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) /
                      (r * (0.85f + 0.3f * DetailHash(seed, px - cx, py - cy, 1)));
            if (d > 1f || c.Kind(px, py) != KEmpty || !c.Land(px, py)) continue;
            float h = DetailHash(seed, px, py, 2);
            Color32 color = h < 0.16f ? burnt : h < 0.34f ? rubble : scorched;
            // 残墙：几段方方的墙基，断断续续
            int lx = px - cx + reach, ly = py - cy + reach, cell = Mathf.Max(4, reach / 2);
            bool edgeX = lx % cell == 0, edgeY = ly % cell == 0;
            if (walls && (edgeX || edgeY) && d < 0.8f && DetailHash(seed, lx / cell, ly / cell, edgeX ? 3 : 4) < 0.45f) color = wall;
            c.Put(px, py, color, KEmpty);
        }
    }

    // 一户人家：草顶或本系的屋顶(平顶的系是平顶)，只盖在空地上，四周不贴着别的房子
    private static bool SmallHouse(Canvas c, DetailStyle style, int x, int y, long seed, bool thatch, int w = 0, int h = 0)
    {
        if (w <= 0)
        {
            w = 3 + (int)(DetailHash(seed, 1) * 3);
            h = 2 + (int)(DetailHash(seed, 2) * 2);
            if (DetailHash(seed, 3) < 0.5f) (w, h) = (h, w);
        }
        for (int py = y - 1; py <= y + h; py++)
        for (int px = x - 1; px <= x + w; px++)
        {
            byte k = c.Kind(px, py);
            bool footprint = px >= x && px < x + w && py >= y && py < y + h;
            if (footprint && (k != KEmpty && k != KPlaza || !c.Land(px, py))) return false;
            if (!footprint && (k == KBuilding || k == KWall || k == KOff)) return false;
        }
        Color32 roof = thatch && !style.FlatRoofs ? Thatch : Pick(style.Roofs, DetailHash(seed, 4));
        if (style.FlatRoofs) Flat(c, x, y, x + w - 1, y + h - 1, roof, KBuilding, true);
        else
        {
            Color32 high = Darker(roof, 1.3f), low = Darker(roof, 0.72f);
            bool horizontal = w >= h;
            for (int py = y; py < y + h; py++)
            for (int px = x; px < x + w; px++)
            {
                float m = horizontal ? y + (h - 1) / 2f : x + (w - 1) / 2f, at = horizontal ? py : px;
                bool rim = horizontal ? py == y || py == y + h - 1 : px == x || px == x + w - 1;
                c.Put(px, py, Mathf.Abs(at - m) < 0.6f ? high : rim ? low : roof, KBuilding);
            }
        }
        for (int px = x + 1; px <= x + w; px++)
            if (c.Kind(px, y - 1) == KEmpty && c.Land(px, y - 1)) c.Put(px, y - 1, new Color32(0, 0, 0, 60), KEmpty);
        return true;
    }

    // 镇：一片地面，十字街、街口集市(摊棚)、沿街房子一户挨一户、背街再几户；富的镇围一圈矮墙
    // 关城：方城一圈(画布的行往北增，南门在 cy - r)，四角墩台；城里几排营房，正中校场、一杆红旗。
    // 方城一律 5×5 格(见 RuralSettlementSystem.FortReach)，等级看画法：关隘一排营房，关城两排、南门两侧起门楼，
    // 雄关三排、墩台加大一圈
    private static void Fort(Canvas c, int cx, int cy, int level, long seed)
    {
        int r = RuralSettlementSystem.FortReach * DetailScale, towerSize = level >= 3 ? 3 : 2;
        Color32 wall = Rgb(150, 140, 124), tower = Rgb(108, 100, 88), yard = Rgb(200, 188, 160), roof = Rgb(92, 84, 80);
        for (int py = cy - r; py <= cy + r; py++)
        for (int px = cx - r; px <= cx + r; px++)
        {
            if (!c.In(px, py) || !c.FortLand(px, py)) continue;
            int dx = px - cx, dy = py - cy, d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            bool corner = Mathf.Abs(dx) >= r - towerSize && Mathf.Abs(dy) >= r - towerSize;
            if (d >= r - 1)
            {
                // 南门
                if (dy <= -r + 1 && Mathf.Abs(dx) <= 1) c.Put(px, py, yard, KPlaza);
                else c.Put(px, py, corner ? tower : (px + py) % 2 == 0 ? wall : Darker(wall, 0.9f), KWall);
                continue;
            }
            if (corner) { c.Put(px, py, tower, KWall); continue; }
            c.Put(px, py, yard, KPlaza);
        }
        // 门楼(关城起)：南门两侧各一座墩台，门里一道门楼屋顶
        if (level >= 2)
        {
            for (int py = cy - r; py <= cy - r + 1; py++)
            for (int side = -1; side <= 1; side += 2)
                if (c.In(cx + side * 2, py) && c.FortLand(cx + side * 2, py)) c.Put(cx + side * 2, py, tower, KWall);
            for (int px = cx - 2; px <= cx + 2; px++)
                if (c.In(px, cy - r + 2) && c.Kind(px, cy - r + 2) == KPlaza) c.Put(px, cy - r + 2, Darker(roof, 1.15f), KBuilding);
        }
        // 营房：东西两侧各几排长屋(关隘一排、关城两排、雄关三排)，正中留校场
        int rows = Mathf.Clamp(level, 1, 3);
        for (int k = 0; k < rows; k++)
        {
            int py0 = cy - r + 4 + k * 4;
            if (py0 + 1 >= cy + r - 2) break;
            for (int side = -1; side <= 1; side += 2)
            {
                int x0 = side < 0 ? cx - r + 3 : cx + 3, x1 = side < 0 ? cx - 3 : cx + r - 3;
                for (int py = py0; py <= py0 + 1; py++)
                for (int px = x0; px <= x1; px++)
                    if (c.In(px, py) && c.Kind(px, py) == KPlaza) c.Put(px, py, py == py0 + 1 ? Darker(roof, 1.15f) : roof, KBuilding);
            }
        }
        // 旗杆、红旗
        for (int k = 0; k < 5; k++)
            if (c.In(cx, cy + k)) c.Put(cx, cy + k, Rgb(70, 60, 50), KBuilding);
        if (c.In(cx + 1, cy + 4)) c.Put(cx + 1, cy + 4, Rgb(196, 52, 44), KBuilding);
        if (c.In(cx + 2, cy + 4)) c.Put(cx + 2, cy + 4, Rgb(196, 52, 44), KBuilding);
        if (c.In(cx + 1, cy + 3)) c.Put(cx + 1, cy + 3, Rgb(176, 44, 38), KBuilding);
    }

    // 城墙接进关城(见 WallGateSystem)：精细图的关城方城四边落在边上那一格的中间，城墙图层一格一像素、到关城方城外一格为止，
    // 中间差半格；城墙进关的那几格(方城最外一圈上的关门)把方城外的半格补成墙色，城墙与关墙连上不留缝
    private static void GateStubs(Canvas c, int x, int y, int level)
    {
        int reach = RuralSettlementSystem.FortReach, s = DetailScale;
        int cx = (x - c.X0) * s + s / 2, cy = (y - c.Y0) * s + s / 2, r = reach * s;
        Color32 wall = Rgb(150, 140, 124);
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != reach || !WallGateSystem.IsGate(World.world.GetTile(x + dx, y + dy))) continue;
            int px0 = (x + dx - c.X0) * s, py0 = (y + dy - c.Y0) * s;
            for (int py = py0; py < py0 + s; py++)
            for (int px = px0; px < px0 + s; px++)
            {
                if (!c.In(px, py) || Mathf.Abs(px - cx) <= r && Mathf.Abs(py - cy) <= r) continue;
                c.Put(px, py, (px + py) % 2 == 0 ? wall : Darker(wall, 0.9f), KWall);
            }
        }
    }

    private static void Town(Canvas c, DetailStyle style, int cx, int cy, int townSize, bool walled, Color32 dirt, Color32 ground, long seed)
    {
        int r = townSize * DetailScale + 2;
        for (int py = cy - r - 2; py <= cy + r + 2; py++)
        for (int px = cx - r - 2; px <= cx + r + 2; px++)
        {
            float edge = r + 2.5f * (DetailNoise(Mathf.Atan2(py - cy, px - cx) * 20f + 20f, 3f, seed) - 0.5f);
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) > edge * edge || c.Kind(px, py) != KEmpty || !c.Land(px, py)) continue;
            c.Put(px, py, ground, KPlaza);
        }
        for (int t = -r; t <= r; t++)
        for (int d = 0; d < 2; d++)
        {
            if (c.Kind(cx + t, cy + d) == KPlaza) c.Put(cx + t, cy + d, dirt, KPath);
            if (c.Kind(cx + d, cy + t) == KPlaza) c.Put(cx + d, cy + t, dirt, KPath);
        }
        for (int py = cy - 4; py <= cy + 5; py++)
        for (int px = cx - 4; px <= cx + 5; px++)
            if (c.Kind(px, py) is KPlaza or KPath) c.Put(px, py, Lerp(ground, Rgb(255, 240, 200), 0.4f), KPlaza);
        for (int k = 0; k < 12; k++)
        {
            int px = cx - 3 + (int)(DetailHash(seed, k, 1) * 8), py = cy - 3 + (int)(DetailHash(seed, k, 2) * 8);
            if (Mathf.Abs(px - cx) <= 1 || Mathf.Abs(py - cy) <= 1) continue;
            c.Put(px, py, DetailHash(seed, k, 3) < 0.5f ? Stall : StallLight, KBuilding);
        }
        for (int dir = 0; dir < 4; dir++)
        for (int sign = -1; sign <= 1; sign += 2)
        for (int t = 7, k = 0; t < r - 2; k++)
        {
            int w = 3 + (int)(DetailHash(seed, dir * 50 + k, sign, 4) * 3), depth = 3 + (int)(DetailHash(seed, dir * 50 + k, sign, 5) * 2);
            int along = dir % 2 == 0 ? t : -t - w + 1;
            bool thatch = DetailHash(seed, k, dir, 6) < 0.2f;
            if (dir < 2) SmallHouse(c, style, cx + along, sign > 0 ? cy + 3 : cy - 1 - depth, seed + dir * 1000 + k * 7 + sign, thatch, w, depth);
            else SmallHouse(c, style, sign > 0 ? cx + 3 : cx - 1 - depth, cy + along, seed + dir * 1000 + k * 7 + sign, thatch, depth, w);
            t += w + (DetailHash(seed, dir, k, 7) < 0.25f ? 2 : 1);
        }
        for (int k = 0; k < townSize * 14; k++)
        {
            int px = cx + (int)((DetailHash(seed, k, 8) - 0.5f) * 2f * (r - 3)), py = cy + (int)((DetailHash(seed, k, 9) - 0.5f) * 2f * (r - 3));
            SmallHouse(c, style, px, py, seed + 5000 + k, DetailHash(seed, k, 10) < 0.35f);
        }
        if (!walled) return;
        Color32 rammed = Lerp(Rgb(124, 112, 100), Rgb(210, 190, 150), 0.5f);
        for (int deg = 0; deg < 360; deg++)
        {
            float a = deg * Mathf.Deg2Rad, rr = r + 1 + 1.5f * Mathf.Sin(a * 3f);
            int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * rr), py = cy + Mathf.RoundToInt(Mathf.Sin(a) * rr);
            if (Mathf.Abs(px - cx) <= 2 || Mathf.Abs(py - cy) <= 2) continue;
            if (c.Kind(px, py) is KEmpty or KPlaza && c.Land(px, py)) c.Put(px, py, rammed, KWall);
        }
    }

    // 村：打谷场居中，十几户人家沿村里一条弯路、围着打谷场挤在一起(维京是小长屋)；树、池塘、菜园
    private static void Village(Canvas c, DetailStyle style, Color32 tint, int cx, int cy, Color32 dirt, Color32 ground, long seed)
    {
        if (!c.In(cx, cy) || !c.Land(cx, cy)) return;
        for (int py = cy - 3; py <= cy + 3; py++)
        for (int px = cx - 3; px <= cx + 3; px++)
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= 7 && c.Kind(px, py) is KEmpty or KPath && c.Land(px, py))
                c.Put(px, py, ground, KPlaza);
        float angle = DetailHash(seed, 1) * Mathf.PI;
        var along = new List<(int x, int y)>();
        for (int t = -16; t <= 16; t++)
        {
            float sway = 2.5f * (DetailNoise(t + 20, 9f, seed) - 0.5f);
            int px = cx + Mathf.RoundToInt(Mathf.Cos(angle) * t - Mathf.Sin(angle) * sway);
            int py = cy + Mathf.RoundToInt(Mathf.Sin(angle) * t * 0.8f + Mathf.Cos(angle) * sway);
            along.Add((px, py));
            if (c.Kind(px, py) == KEmpty && c.Land(px, py)) c.Put(px, py, dirt, KPath);
        }
        if (DetailHash(seed, 2) < 0.6f)
        {
            int ox = cx + (DetailHash(seed, 3) < 0.5f ? 9 : -10), oy = cy + (DetailHash(seed, 4) < 0.5f ? 6 : -7);
            for (int py = oy - 2; py <= oy + 2; py++)
            for (int px = ox - 3; px <= ox + 3; px++)
                if ((px - ox) * (px - ox) / 10f + (py - oy) * (py - oy) / 4f <= 1f && c.Kind(px, py) == KEmpty && c.Land(px, py))
                    c.Put(px, py, (px + py) % 5 == 0 ? Ripple : WaterBlue, KWater);
        }
        int want = 10 + (int)(DetailHash(seed, 5) * 10), placed = 0;
        bool horizontalRoad = Mathf.Abs(Mathf.Cos(angle)) > 0.5f, longhouse = style.House == "longhouse";
        for (int k = 0; k < want * 10 && placed < want; k++)
        {
            (int ax, int ay) = along[(int)(DetailHash(seed, k, 6) * along.Count) % along.Count];
            int sign = DetailHash(seed, k, 7) < 0.5f ? 1 : -1, offset = 2 + (int)(DetailHash(seed, k, 8) * 3);
            int jitter = (int)((DetailHash(seed, k, 9) - 0.5f) * 3f);
            int px = horizontalRoad ? ax + jitter : ax + sign * offset - (sign < 0 ? 3 : 0);
            int py = horizontalRoad ? ay + sign * offset - (sign < 0 ? 2 : 0) : ay + jitter;
            bool ok = longhouse
                ? SmallHouse(c, style, px, py, seed + 100 + k, true, horizontalRoad ? 6 : 3, horizontalRoad ? 3 : 6)
                : SmallHouse(c, style, px, py, seed + 100 + k, DetailHash(seed, k, 10) < 0.55f);
            if (ok) placed++;
        }
        for (int k = 0; k < 18; k++)
        {
            float a = DetailHash(seed, k, 11) * Mathf.PI * 2f, rr = 6f + DetailHash(seed, k, 12) * 14f;
            int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * rr), py = cy + Mathf.RoundToInt(Mathf.Sin(a) * rr * 0.8f);
            if (c.Kind(px, py) == KEmpty && c.Land(px, py))
                c.Put(px, py, style.Palms && k % 3 == 0 ? Palm : DetailHash(seed, k, 13) < 0.6f ? TreeDark : TreeLight, KBuilding);
        }
        for (int k = 0; k < 3; k++)
        {
            int ox = cx + (int)((DetailHash(seed, k, 14) - 0.5f) * 26f), oy = cy + (int)((DetailHash(seed, k, 15) - 0.5f) * 22f);
            for (int py = oy; py < oy + 4; py++)
            for (int px = ox; px < ox + 5; px++)
                if (c.Kind(px, py) == KEmpty && c.Land(px, py))
                    c.Put(px, py, px % 2 == 0 ? Lerp(tint, Rgb(150, 196, 110), 0.7f) : Lerp(tint, Rgb(120, 160, 90), 0.6f), KPlaza);
        }
    }

    #endregion

    #region 营地(游牧、林地)

    // 游牧：不修街、不筑城，蒙古包一圈圈围着正中的大汗帐，畜栏里散着羊群；城外营地也是一圈蒙古包。
    // 林地：林间空地上的圆形树皮屋、议事长屋，木栅围一圈，空地四周是树林；城外一处处小营地
    private static void DrawCamp(Canvas c, CityPlan plan, UrbanFabric.Geometry g, DetailStyle style, Color32 tint, long seed)
    {
        int s = DetailScale;
        bool yurt = style.Layout == "yurt";
        // 冬营地(游牧城的城心不在旱地上，见 RuralSettlementSystem.DryLand)：毡帐换成木土小屋，畜栏照旧
        bool winter = yurt && plan.winter;
        Color32 trampled = Lerp(tint, Rgb(196, 186, 140), 0.6f);
        c.Block = trampled;
        for (int ty = 0; ty < c.H; ty++)
        for (int tx = 0; tx < c.W; tx++)
        {
            WorldTile tile = World.world.GetTile(c.X0 + tx, c.Y0 + ty);
            if (tile?.Type == null || tile.zone == null) continue;
            if (tile.Type.liquid)
            {
                Fill(c, tx, ty, Clear, KWater);
                continue;
            }
            if (!Urban(tile, plan)) continue;
            for (int sy = 0; sy < s; sy++)
            for (int sx = 0; sx < s; sx++)
            {
                int px = tx * s + sx, py = ty * s + sy;
                c.Put(px, py, DetailHash(seed, px, py, 5) < 0.06f ? Darker(trampled, 0.92f) : trampled, KGround);
            }
        }
        if (plan.gateX == int.MinValue) return;
        float cx = (plan.gateX - c.X0) * s + 2f, cy = (plan.gateY - c.Y0) * s + 2f, R = UrbanRadius(plan);
        if (!yurt)
        {
            // 空地四周一圈树林，越往外越稀；一圈木栅
            for (int py = 0; py < c.PH; py++)
            for (int px = 0; px < c.PW; px++)
            {
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                if (d < R * 0.95f || d > R * 1.22f || c.Kind(px, py) != KEmpty || !c.Land(px, py)) continue;
                if (DetailHash(seed, px, py, 6) < 0.5f - (d - R * 0.95f) / (R * 0.27f) * 0.3f)
                    c.Put(px, py, DetailHash(seed, px, py, 7) < 0.5f ? Rgb(72, 120, 64) : Rgb(96, 146, 80), KBuilding);
            }
            for (int t = 0; t < 1440; t++)
            {
                float a = t / 1440f * Mathf.PI * 2f;
                int px = Mathf.RoundToInt(cx + Mathf.Cos(a) * R * 0.9f), py = Mathf.RoundToInt(cy + Mathf.Sin(a) * R * 0.9f);
                if (c.Kind(px, py) == KGround) c.Put(px, py, t % 2 == 1 ? Rgb(150, 116, 76) : Rgb(108, 82, 54), KWall);
            }
        }
        void Tent(float x, float y, int r, bool main)
        {
            if (winter)
            {
                WinterHut(c, x, y, r, main);
                return;
            }
            if (yurt)
            {
                Yurt(c, x, y, r, main);
                return;
            }
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                int px = Mathf.RoundToInt(x) + dx, py = Mathf.RoundToInt(y) + dy;
                if (d > r + 0.3f || c.Kind(px, py) is not (KGround or KEmpty)) continue;
                if (c.Kind(px, py) == KEmpty && !c.Land(px, py)) continue;
                c.Put(px, py, d > r - 0.8f ? Rgb(96, 72, 50) : d < 0.8f ? Rgb(70, 52, 38) : Rgb(140, 108, 74), KBuilding);
            }
        }
        Tent(cx, cy, yurt ? 5 : 4, true);
        if (!yurt) Roof(c, (int)cx - 14, (int)cy - 12, (int)cx + 2, (int)cy - 9, true, Rgb(130, 100, 70), KBuilding, true);
        for (int ring = 1; ring * 9 < R * 0.9f; ring++)
        {
            float rr = ring * 9 + DetailHash(seed, ring, 1) * 3f;
            int n = Mathf.FloorToInt(rr * Mathf.PI * 2f / 8f);
            for (int i = 0; i < n; i++)
            {
                if (DetailHash(seed, ring, i, 2) < 0.25f) continue;
                float a = (i + DetailHash(seed, ring, 3)) / n * Mathf.PI * 2f;
                float x = cx + Mathf.Cos(a) * rr, y = cy + Mathf.Sin(a) * rr;
                WorldTile tile = c.Tile(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
                if (tile == null || !Urban(tile, plan)) continue;
                Tent(x, y, 2, false);
            }
        }
        // 畜栏与羊群(游牧)
        if (yurt)
            for (int k = 0; k < 5; k++)
            {
                float a = DetailHash(seed, k, 20) * Mathf.PI * 2f, rr = R * (0.6f + 0.5f * DetailHash(seed, k, 21));
                int x = Mathf.RoundToInt(cx + Mathf.Cos(a) * rr), y = Mathf.RoundToInt(cy + Mathf.Sin(a) * rr);
                for (int dy = 0; dy < 8; dy++)
                for (int dx = 0; dx < 10; dx++)
                {
                    byte k0 = c.Kind(x + dx, y + dy);
                    if (k0 != KGround && (k0 != KEmpty || !c.Land(x + dx, y + dy))) continue;
                    bool rim = dx == 0 || dy == 0 || dx == 9 || dy == 7;
                    if (rim && (dx + dy) % 2 == 0) c.Put(x + dx, y + dy, Fence, KBuilding);
                    else if (!rim && DetailHash(seed, k, dx * 16 + dy, 22) < 0.3f) c.Put(x + dx, y + dy, Rgb(244, 240, 228), KBuilding);
                }
            }
        // 游牧城外：存档里的毡帐村镇连同畜栏，落在本城地上的别的村镇、废墟、关城一并画(见 DrawCountryside；不出大路、不修小路，
        // 那里自然没有路)。以前只画几处随手散的营地
        if (yurt)
        {
            DrawCountryside(c, g, style, tint, seed);
            return;
        }
        // 林地城外的村子画成一小圈营地
        int index = 0;
        foreach ((int vx, int vy) in g.Villages)
        {
            index++;
            float x0 = (vx - c.X0) * s + 2f, y0 = (vy - c.Y0) * s + 2f;
            for (int py = (int)y0 - 7; py <= y0 + 7; py++)
            for (int px = (int)x0 - 7; px <= x0 + 7; px++)
                if ((px - x0) * (px - x0) + (py - y0) * (py - y0) <= 49f && c.Kind(px, py) == KEmpty && c.Land(px, py))
                    c.Put(px, py, trampled, KGround);
            Tent(x0, y0, 3, true);
            for (int j = 0; j < 6; j++)
            {
                float a = j / 6f * Mathf.PI * 2f + DetailHash(seed, index, 30);
                Tent(x0 + Mathf.Cos(a) * 6f, y0 + Mathf.Sin(a) * 6f, 2, false);
            }
        }
        // 林地城外的镇：一大片林间空地，正中长屋，外面一圈圈棚屋(以前精细图上不画，粗图上有)
        index = 0;
        foreach ((int tx, int ty, int townSize, _) in g.Towns)
        {
            index++;
            float x0 = (tx - c.X0) * s + 2f, y0 = (ty - c.Y0) * s + 2f, r = townSize * DetailScale + 2;
            for (int py = (int)(y0 - r); py <= y0 + r; py++)
            for (int px = (int)(x0 - r); px <= x0 + r; px++)
                if ((px - x0) * (px - x0) + (py - y0) * (py - y0) <= r * r && c.Kind(px, py) == KEmpty && c.Land(px, py))
                    c.Put(px, py, trampled, KGround);
            Tent(x0, y0, 4, true);
            for (float rr = 6f; rr <= r - 3f; rr += 5f)
            {
                int n = Mathf.Max(5, Mathf.FloorToInt(rr * Mathf.PI * 2f / 7f));
                for (int j = 0; j < n; j++)
                {
                    float a = (j + DetailHash(seed, index, (int)rr, 31)) / n * Mathf.PI * 2f;
                    Tent(x0 + Mathf.Cos(a) * rr, y0 + Mathf.Sin(a) * rr, 2, false);
                }
            }
        }
        DrawOutposts(c, g, tint);
    }

    // 关城与废墟(林地营地、原始聚落城外用；见 DrawCountryside 里的同一段)：关城筑在哪座城的地上都画
    private static void DrawOutposts(Canvas c, UrbanFabric.Geometry g, Color32 tint)
    {
        int s = DetailScale;
        bool spill = c.Spill;
        c.Spill = true;
        foreach ((int x, int y, int level) in g.Forts)
        {
            Fort(c, (x - c.X0) * s + s / 2, (y - c.Y0) * s + s / 2, level, UrbanFabric.SettlementKey(x, y) + 7000);
            GateStubs(c, x, y, level);
        }
        foreach ((int x, int y, bool town) in g.Ruins)
            RuralRuin(c, (x - c.X0) * s + s / 2, (y - c.Y0) * s + s / 2, town ? 16f : 7f, UrbanFabric.SettlementKey(x, y) + 5000, tint,
                !UrbanFabric.IsYurt(g, x, y));
        c.Spill = spill;
    }

    // 村镇自己的画法(小窗口里换了样式的，见 RuralSettlementSystem.SetStyle)；没换的随所在的城
    private static DetailStyle OwnStyle(UrbanFabric.Geometry g, int x, int y, DetailStyle city)
    {
        DetailStyle own = StyleOf(UrbanFabric.StyleAt(g, x, y));
        return own == null || own.Camp ? city : own;
    }

    // 冬营地的一座木土小屋(俯视)：土黄屋顶、正中一道屋脊、屋檐一圈压暗，南墙正中一个门洞；议事的大屋(main)宽些。
    // 横竖按位置定，免得一圈屋子全朝一个方向
    private static void WinterHut(Canvas c, float x, float y, int r, bool main)
    {
        int cx = Mathf.RoundToInt(x), cy = Mathf.RoundToInt(y);
        bool across = main || DetailHash(cx, cy, 51) < 0.6f;
        int halfLong = main ? r + 1 : r, halfShort = main ? r - 1 : Mathf.Max(1, r - 1);
        int hw = across ? halfLong : halfShort, hh = across ? halfShort : halfLong;
        Color32 roof = Rgb(178, 142, 94), eave = Rgb(150, 118, 78), ridge = Rgb(122, 94, 62), door = Rgb(84, 64, 44);
        for (int dy = -hh; dy <= hh; dy++)
        for (int dx = -hw; dx <= hw; dx++)
        {
            int px = cx + dx, py = cy + dy;
            if (c.Kind(px, py) is not (KGround or KEmpty) || c.Kind(px, py) == KEmpty && !c.Land(px, py)) continue;
            bool onRidge = across ? dy == 0 : dx == 0;
            bool rim = Mathf.Abs(dx) == hw || Mathf.Abs(dy) == hh;
            c.Put(px, py, onRidge ? ridge : rim ? eave : roof, KBuilding);
        }
        // 门洞：南墙(画布的行往北增，南面是 cy - hh)外一格
        int doorY = cy - hh - 1;
        if (c.Kind(cx, doorY) is KGround or KEmpty && c.Land(cx, doorY)) c.Put(cx, doorY, door, KBuilding);
    }

    // 一顶毡帐：白毡圆顶，边上一圈浅影，正中天窗深色；大帐顶上一点红
    private static void Yurt(Canvas c, float x, float y, int r, bool main)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            int px = Mathf.RoundToInt(x) + dx, py = Mathf.RoundToInt(y) + dy;
            if (d > r + 0.3f || c.Kind(px, py) is not (KGround or KEmpty)) continue;
            if (c.Kind(px, py) == KEmpty && !c.Land(px, py)) continue;
            c.Put(px, py, d > r - 0.8f ? Rgb(200, 192, 172) : d < 0.8f ? Rgb(150, 130, 100) : Rgb(240, 234, 218), KBuilding);
        }
        if (main) c.Put(Mathf.RoundToInt(x), Mathf.RoundToInt(y) - r, Rgb(190, 60, 40), KBuilding);
    }

    // 毡帐村：踩出一小片空地，正中一顶大帐，四到六顶小帐围一圈(以前游牧城外的营地也是这样)
    private static void YurtVillage(Canvas c, int cx, int cy, Color32 trampled, long seed)
    {
        if (!c.In(cx, cy) || !c.Land(cx, cy)) return;
        for (int py = cy - 7; py <= cy + 7; py++)
        for (int px = cx - 7; px <= cx + 7; px++)
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= 49 && c.Kind(px, py) is KEmpty or KPath && c.Land(px, py))
                c.Put(px, py, trampled, KGround);
        Yurt(c, cx, cy, 3, true);
        int count = 4 + (int)(DetailHash(seed, 31) * 3);
        float turn = DetailHash(seed, 30) * Mathf.PI * 2f;
        for (int j = 0; j < count; j++)
        {
            float a = j / (float)count * Mathf.PI * 2f + turn, rr = 5.5f + DetailHash(seed, j, 32) * 1.2f;
            Yurt(c, cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr, 2, false);
        }
    }

    // 毡帐镇：踩出一大片空地(同镇的大小，边缘参差)，正中大帐，外面一圈圈毡帐，偶有几顶大些的
    private static void YurtTown(Canvas c, int cx, int cy, int townSize, bool walled, Color32 trampled, long seed)
    {
        int r = townSize * DetailScale + 2;
        for (int py = cy - r - 2; py <= cy + r + 2; py++)
        for (int px = cx - r - 2; px <= cx + r + 2; px++)
        {
            float edge = r + 2.5f * (DetailNoise(Mathf.Atan2(py - cy, px - cx) * 20f + 20f, 3f, seed) - 0.5f);
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) > edge * edge || c.Kind(px, py) is not (KEmpty or KPath) ||
                !c.Land(px, py)) continue;
            c.Put(px, py, DetailHash(seed, px, py, 40) < 0.06f ? Darker(trampled, 0.92f) : trampled, KGround);
        }
        Yurt(c, cx, cy, 4, true);
        // 一圈隔五像素：镇三格见方的两圈十来顶，四格的三圈二十多顶
        for (int ring = 1; 2 + ring * 5 <= r - 1; ring++)
        {
            float rr = 2 + ring * 5 + DetailHash(seed, ring, 41) * 0.8f;
            int n = Mathf.Max(4, Mathf.FloorToInt(rr * Mathf.PI * 2f / 6.5f));
            float turn = DetailHash(seed, ring, 42);
            for (int i = 0; i < n; i++)
            {
                if (DetailHash(seed, ring, i, 43) < 0.2f) continue;
                float a = (i + turn) / n * Mathf.PI * 2f;
                bool big = ring > 1 && DetailHash(seed, ring, i, 44) < 0.1f;
                Yurt(c, cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr, big ? 3 : 2, false);
            }
        }
        // 有墙的毡帐大营(小窗口里升的，见 RuralSettlementSystem.SetTier)：一圈木栅，四面留口
        if (!walled) return;
        for (int t = 0; t < 1440; t++)
        {
            float a = t / 1440f * Mathf.PI * 2f, rr = r + 1.5f;
            int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * rr), py = cy + Mathf.RoundToInt(Mathf.Sin(a) * rr);
            if (Mathf.Abs(px - cx) <= 2 || Mathf.Abs(py - cy) <= 2) continue;
            if (c.Kind(px, py) is KEmpty or KGround or KPath && c.Land(px, py))
                c.Put(px, py, t % 2 == 1 ? Rgb(150, 116, 76) : Rgb(108, 82, 54), KWall);
        }
    }

    #endregion
}
