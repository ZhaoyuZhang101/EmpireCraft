using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市精细层(用户 2026-10-16："把城市像素变得更加精细""现在的城市分辨率不足以显示出城市的细节"
// "太规整了，没有一般城市的随意中带着一丝规划""周边还要生成随机小村镇")：
// 拉近时给镜头里最近的几座城单独画一张一格 4×4 像素的图，盖在一格一像素的平面图上(见 UI/Components/CityDetailOverlay)。
// 用的是同一份平面图(城的轮廓、城墙、四门与中轴、各坊用途与建筑、城外的路与村镇)，只是画得细：
//   · 骨架规划：城墙(外沿垛口、内侧马道、敌楼、城门楼)、护城河、通城门的大街与中轴、城心钟鼓楼，笔直规整；
//   · 肌理自然：次级街道大致沿地块边，但位置错开、略有弯折、宽窄不一，有的只修半截(丁字路口)，有的没修(街坊连成一片)；
//     街坊里一刀刀切出大小不一的宅基地，大的留胡同；宅子临街开门——小户一字平房、中户三合院、大户两进四合院，
//     夹着菜园、空地、树；坊里有什么建筑(见 Fill)决定画宅子、铺面、衙署、庙、营房还是作坊，坊里建筑少就多空地；
//   · 城外：城门外沿大路一溜关厢人家；镇(十字街、集市摊棚、沿街房子、小庙，富的围一圈矮墙)；
//     村(打谷场、十几户人家、树、池塘、菜园，小路通到大路)；村镇周围的田块(压暗的垄沟)。
// 第一步只画古代的华夏城；别的文化系与近代以后的城照旧(见 DetailEligible)。
public partial class PureMapPatch
{
    internal const int DetailScale = 4;
    // 一张精细图最多画多大(格)：太大的城只画城心附近
    private const int MaxDetailTiles = 128;

    // 像素种类
    private const byte KEmpty = 0, KGround = 1, KStreet = 2, KAlley = 3, KBuilding = 4, KWall = 5, KWater = 6, KPath = 7,
        KPlaza = 8, KAvenue = 9, KFixed = 10, KOff = 255;

    private static readonly Color32 Clear = new(0, 0, 0, 0), Court = new(222, 214, 196, 255), Door = new(150, 60, 50, 255),
        TreeDark = new(92, 146, 80, 255), TreeLight = new(128, 178, 106, 255), Dirt = new(214, 196, 160, 255),
        Thatch = new(156, 132, 92, 255), Threshing = new(232, 220, 190, 255), Stall = new(200, 120, 70, 255),
        StallLight = new(220, 150, 90, 255), TempleRoof = new(82, 70, 64, 255), TempleYard = new(226, 216, 196, 255),
        Garden = new(170, 196, 130, 255), Workshop = new(132, 110, 90, 255), Furrow = new(0, 0, 0, 46),
        WaterBlue = new(140, 185, 235, 255);
    private static readonly Color32[] SlateRoofs =
    {
        new(96, 98, 108, 255), new(104, 102, 108, 255), new(90, 94, 104, 255), new(110, 106, 100, 255), new(98, 92, 96, 255)
    };

    internal sealed class DetailImage
    {
        public int X0, Y0, W, H, Key;
        public Color32[] Pixels;
    }

    // 画这座城的精细图吗：纯净地图画城市时、古代的华夏城
    internal static bool DetailEligible(City city)
    {
        if (city?.data == null || city.isRekt() || !PureMapMode.Enabled || !DetailShown || Layer.CityViewMap.Active ||
            ZonePlanSystem.SteadyOverlay) return false;
        CityPlan plan = PlanFor(city);
        UrbanFabric.Geometry g = plan.fabric;
        return g != null && !g.Nomad && !g.Modern && plan.family == "huaxia" && plan.target == null && plan.built.Count > 0;
    }

    // 精细图的指纹：平面图变了、底色(图层)变了就重画
    internal static int DetailKey(City city, Color32 tint) => unchecked(StampOf(city) * 31 + (tint.r << 16 | tint.g << 8 | tint.b));

    // 底色：当前图层里城心那块地的颜色(国家、帝国……)，没有就用本国颜色
    internal static Color32 DetailTint(City city)
    {
        Color32 tint = new(160, 160, 160, 255);
        try
        {
            ZoneCalculator zones = World.world.zone_calculator;
            TileZone center = city.getTile()?.zone;
            MetaTypeAsset mode = zones?._mode_asset;
            object meta = center != null && mode?.tile_get_metaobject_0 != null ? mode.tile_get_metaobject_0(center) : null;
            ColorAsset color = (meta as IMetaObject)?.getColor() ?? city.kingdom?.getColor();
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

        public void Put(int px, int py, Color32 color, byte kind)
        {
            if (!In(px, py)) return;
            int i = py * PW + px;
            Pixels[i] = color;
            Kinds[i] = kind;
        }

        public WorldTile Tile(int px, int py) => World.world.GetTile(X0 + px / DetailScale, Y0 + py / DetailScale);

        // 这一像素落在能盖房子的地上(不是水、山，属于本城或无主)
        public bool Land(int px, int py)
        {
            WorldTile tile = Tile(px, py);
            if (tile?.Type == null || tile.Type.liquid || tile.Type.mountains || tile.Type.block || MapWallSystem.Contains(tile))
                return false;
            City owner = tile.zone?.city;
            return owner == null || owner == City;
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

    #endregion

    // 画一座城的精细图(连同城外的关厢、镇、村)
    internal static DetailImage BuildDetail(City city, Color32 tint)
    {
        CityPlan plan = PlanFor(city);
        UrbanFabric.Geometry g = plan.fabric;
        if (g == null) return null;
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
            Grow(zone.x * size, zone.y * size, 3);
            Grow(zone.x * size + size - 1, zone.y * size + size - 1, 3);
        }
        if (x0 == int.MaxValue) return null;
        foreach ((int x, int y) in g.Villages) Grow(x, y, 7);
        foreach ((int x, int y, int townSize, _) in g.Towns) Grow(x, y, townSize + 3);
        if (g.Suburb > 0)
            foreach (UrbanFabric.Road road in g.Roads)
                if (road.Gate)
                    for (int d = 0; d <= 2 + g.Suburb && d <= road.Length; d++)
                    {
                        (int rx, int ry) = UrbanFabric.RoadPoint(road, d);
                        Grow(rx, ry, 4);
                    }
        // 太大就以城心为中心截一块
        int cx = g.Cx, cy = g.Cy;
        x0 = Mathf.Max(x0, cx - MaxDetailTiles / 2);
        y0 = Mathf.Max(y0, cy - MaxDetailTiles / 2);
        x1 = Mathf.Min(x1, cx + MaxDetailTiles / 2 - 1);
        y1 = Mathf.Min(y1, cy + MaxDetailTiles / 2 - 1);
        x0 = Mathf.Max(0, x0);
        y0 = Mathf.Max(0, y0);
        x1 = Mathf.Min(MapBox.width - 1, x1);
        y1 = Mathf.Min(MapBox.height - 1, y1);
        if (x1 < x0 || y1 < y0) return null;
        var c = new Canvas(city, x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        long seed = UrbanFabric.SeedOf(city);
        DrawUrban(c, plan, tint, seed);
        DrawCountryside(c, plan, g, tint, seed);
        return new DetailImage { X0 = x0, Y0 = y0, W = c.W, H = c.H, Pixels = c.Pixels, Key = DetailKey(city, tint) };
    }

    #region 城里

    private static void DrawUrban(Canvas c, CityPlan plan, Color32 tint, long seed)
    {
        int s = DetailScale;
        Color32 block = Lerp(tint, Block, BlockShare), avenue = Lerp(tint, Arterial, StreetShare),
            avenueEdge = Lerp(avenue, new Color32(150, 120, 70, 255), 0.35f),
            axis = Lerp(tint, AxisOf(plan.family) ?? Arterial, StreetShare), moat = Lerp(tint, Moat, 0.9f);
        Color32 wall = WallOf(plan.family) ?? new Color32(124, 112, 100, 255), tower = TowerOf(wall);
        bool wallsUp = plan.walled && !(plan.fabric?.WallsDown ?? false);
        // ① 逐格铺底：城里的地、地标(照一格一像素的画法放大)、护城河；城墙、钟鼓楼最后画
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
            bool urban = Urban(tile, plan);
            if (!urban)
            {
                if (plan.moat && wallsUp && BesideWall(tile, plan))
                {
                    bool bridge = tile.x == plan.gateX || tile.y == plan.gateY;
                    for (int sy = 0; sy < s; sy++)
                    for (int sx = 0; sx < s; sx++)
                    {
                        int px = tx * s + sx, py = ty * s + sy;
                        Color32 color = bridge ? (sx == 0 || sx == s - 1) && tile.x == plan.gateX || (sy == 0 || sy == s - 1) && tile.y == plan.gateY
                                ? new Color32(120, 104, 88, 255) : avenue
                            : (px + 2 * py) % 8 == 0 ? Lerp(moat, new Color32(255, 255, 255, 255), 0.35f) : moat;
                        c.Put(px, py, color, bridge ? KAvenue : KWater);
                    }
                }
                continue;
            }
            if (plan.landmark.Contains(tile.zone.id))
            {
                Slot[] slots = plan.zones.TryGetValue(tile.zone.id, out Slot[] found) ? found : null;
                Color32 coarse = Plan(tile, tint, ZonePlanSystem.Get(c.City, tile.zone), slots,
                    tile.x - tile.zone.x * Config.CITY_ZONE_SIZE, tile.y - tile.zone.y * Config.CITY_ZONE_SIZE, plan, tile.zone);
                Fill(c, tx, ty, coarse, KFixed);
                continue;
            }
            if (wallsUp && UrbanEdge(tile, plan))
            {
                Fill(c, tx, ty, wall, KWall);
                continue;
            }
            bool onAxis = plan.axis && (tile.x == plan.axisX || tile.y == plan.axisY);
            bool onGate = tile.x == plan.gateX || tile.y == plan.gateY;
            if (onAxis || onGate)
            {
                bool vertical = onAxis ? tile.x == plan.axisX : tile.x == plan.gateX;
                Color32 road = onAxis ? axis : avenue;
                for (int sy = 0; sy < s; sy++)
                for (int sx = 0; sx < s; sx++)
                {
                    bool edge = vertical ? sx == 0 || sx == s - 1 : sy == 0 || sy == s - 1;
                    c.Put(tx * s + sx, ty * s + sy, edge ? Lerp(road, new Color32(150, 120, 70, 255), 0.35f) : road, KAvenue);
                }
                continue;
            }
            Fill(c, tx, ty, block, KGround);
        }
        // ② 次级街道：大致沿地块边，错位、微弯、宽窄不一；有的只修半截，有的不修
        Color32 street = Lerp(tint, Street, StreetShare);
        int size = Config.CITY_ZONE_SIZE;
        for (int line = (c.X0 / size) + 1; line <= (c.X0 + c.W) / size; line++) StreetLine(c, true, line * size - c.X0, plan, seed, line, street);
        for (int line = (c.Y0 / size) + 1; line <= (c.Y0 + c.H) / size; line++) StreetLine(c, false, line * size - c.Y0, plan, seed, line, street);
        // 斜街一条(顺着旧河道、旧路)
        if (DetailHash(seed, 7) < 0.45f)
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
        // ③ 街坊：被街围出的一片片，逐片切宅基地、盖房子
        Blocks(c, plan, tint, block, seed);
        // ④ 行道树
        for (int py = 0; py < c.PH; py++)
        for (int px = 0; px < c.PW; px++)
        {
            if (c.Kind(px, py) != KGround || DetailHash(seed, px, py, 77) >= 0.05f) continue;
            if (c.Kind(px + 1, py) is KStreet or KAvenue || c.Kind(px - 1, py) is KStreet or KAvenue ||
                c.Kind(px, py + 1) is KStreet or KAvenue || c.Kind(px, py - 1) is KStreet or KAvenue)
                c.Put(px, py, TreeDark, KGround);
        }
        // ⑤ 城心钟鼓楼(两条中线交口，3×3 格)：红墙台基、黑瓦重檐
        if (plan.gateX != int.MinValue && plan.built.Count >= 2)
        {
            int ox = (plan.gateX - c.X0) * s + s / 2, oy = (plan.gateY - c.Y0) * s + s / 2;
            for (int py = oy - 6; py < oy + 6; py++)
            for (int px = ox - 6; px < ox + 6; px++)
            {
                float d = Mathf.Max(Mathf.Abs(px - ox + 0.5f), Mathf.Abs(py - oy + 0.5f));
                Color32 color = d > 4.6f ? PalaceWall : d > 2.2f ? DrumRoof : d > 0.9f ? new Color32(120, 108, 112, 255) : new Color32(156, 146, 126, 255);
                if (c.Kind(px, py) != KOff && c.Kind(px, py) != KWater) c.Put(px, py, color, KFixed);
            }
        }
        // ⑥ 城墙：外沿垛口一明一暗，内侧马道；四角、门两侧与每隔几格一座敌楼；城门楼红墙黑瓦
        if (wallsUp) Walls(c, plan, wall, tower);
    }

    private static void Fill(Canvas c, int tx, int ty, Color32 color, byte kind)
    {
        for (int sy = 0; sy < DetailScale; sy++)
        for (int sx = 0; sx < DetailScale; sx++)
            c.Put(tx * DetailScale + sx, ty * DetailScale + sy, color, kind);
    }

    // 一条次级街道(格坐标 at 处的地块边)：不修 18%，只修半截(到中线为止) 35%，错开 ±3 像素，宽 2(偶尔 3)，沿途微弯
    private static void StreetLine(Canvas c, bool vertical, int at, CityPlan plan, long seed, int line, Color32 street)
    {
        long id = seed * 7 + (vertical ? 1000 : 2000) + line;
        if (DetailHash(id, 1) < 0.18f) return;
        int s = DetailScale;
        int length = vertical ? c.PH : c.PW;
        int from = 0, to = length - 1;
        int gate = vertical ? plan.gateY : plan.gateX;
        if (gate != int.MinValue && DetailHash(id, 2) < 0.35f)
        {
            int middle = Mathf.Clamp((gate - (vertical ? c.Y0 : c.X0)) * s, 0, length - 1);
            if (DetailHash(id, 3) < 0.5f) to = middle;
            else from = middle;
        }
        int offset = Mathf.RoundToInt((DetailHash(id, 4) - 0.5f) * 6f);
        int width = DetailHash(id, 5) < 0.75f ? 2 : 3;
        for (int t = from; t <= to; t++)
        {
            int wob = Mathf.RoundToInt(2.2f * (DetailNoise(t, 26f, id) - 0.5f)) + offset;
            for (int d = 0; d < width; d++)
            {
                int px = vertical ? at * s + wob + d - 1 : t, py = vertical ? t : at * s + wob + d - 1;
                if (c.Kind(px, py) == KGround) c.Put(px, py, street, KStreet);
            }
        }
    }

    private static void Blocks(Canvas c, CityPlan plan, Color32 tint, Color32 block, long seed)
    {
        var seen = new bool[c.PW * c.PH];
        var stack = new Stack<int>();
        int index = 0;
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
            // 街坊外沿留一像素(人行道)，里面再切
            Subdivide(c, plan, block, minX + 1, minY + 1, maxX - 1, maxY - 1, 0, seed * 131 + index++);
        }
    }

    // 一刀刀切：沿长边在 35%~65% 处切开，大块偶尔留一条胡同；够小或随机停下就盖房
    private static void Subdivide(Canvas c, CityPlan plan, Color32 block, int x0, int y0, int x1, int y1, int depth, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 3 || h < 3) return;
        int area = w * h;
        if (area <= 90 || area <= 200 && DetailHash(seed, depth, 1) < 0.45f || depth > 8)
        {
            Lot(c, plan, block, x0, y0, x1, y1, seed);
            return;
        }
        bool alley = area > 260 && DetailHash(seed, depth, 2) < 0.45f;
        float cut = 0.35f + 0.3f * DetailHash(seed, depth, 3);
        Color32 alleyColor = Lerp(block, new Color32(255, 255, 255, 255), 0.45f);
        if (w >= h)
        {
            int m = x0 + (int)(w * cut);
            if (alley)
            {
                for (int py = y0; py <= y1; py++)
                    if (c.Kind(m, py) == KGround) c.Put(m, py, alleyColor, KAlley);
                Subdivide(c, plan, block, x0, y0, m - 1, y1, depth + 1, seed * 3 + 1);
                Subdivide(c, plan, block, m + 1, y0, x1, y1, depth + 1, seed * 3 + 2);
            }
            else
            {
                Subdivide(c, plan, block, x0, y0, m, y1, depth + 1, seed * 3 + 1);
                Subdivide(c, plan, block, m + 1, y0, x1, y1, depth + 1, seed * 3 + 2);
            }
        }
        else
        {
            int m = y0 + (int)(h * cut);
            if (alley)
            {
                for (int px = x0; px <= x1; px++)
                    if (c.Kind(px, m) == KGround) c.Put(px, m, alleyColor, KAlley);
                Subdivide(c, plan, block, x0, y0, x1, m - 1, depth + 1, seed * 3 + 1);
                Subdivide(c, plan, block, x0, m + 1, x1, y1, depth + 1, seed * 3 + 2);
            }
            else
            {
                Subdivide(c, plan, block, x0, y0, x1, m, depth + 1, seed * 3 + 1);
                Subdivide(c, plan, block, x0, m + 1, x1, y1, depth + 1, seed * 3 + 2);
            }
        }
    }

    // 这块宅基地盖什么：看它落在哪个地块、哪一角，那一角排的是什么建筑(见 Fill)；没排建筑的多半空着
    private static void Lot(Canvas c, CityPlan plan, Color32 block, int x0, int y0, int x1, int y1, long seed)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 4 || h < 4) return;
        WorldTile tile = c.Tile((x0 + x1) / 2, (y0 + y1) / 2);
        TileZone zone = tile?.zone;
        if (zone == null) return;
        ZoneUse use = ZonePlanSystem.Get(c.City, zone);
        int lx = tile.x - zone.x * Config.CITY_ZONE_SIZE, ly = tile.y - zone.y * Config.CITY_ZONE_SIZE;
        int quarter = (ly >= Config.CITY_ZONE_SIZE / 2 ? 2 : 0) + (lx >= Config.CITY_ZONE_SIZE / 2 ? 1 : 0);
        Slot[] slots = plan.zones.TryGetValue(zone.id, out Slot[] found) ? found : null;
        string icon = slots != null && quarter < slots.Length && slots[quarter].used ? slots[quarter].icon : null;
        float r = DetailHash(seed, 11);
        if (use == ZoneUse.Reserve || icon == null && r < 0.3f)
        {
            Trees(c, x0, y0, x1, y1, seed);
            return;
        }
        if (use == ZoneUse.Farm || icon == null && r < 0.55f)
        {
            Plots(c, block, x0, y0, x1, y1, w > h);
            return;
        }
        if (plan.ruin > 0f && DetailHash(seed, 12) < plan.ruin)
        {
            Ruin(c, block, x0, y0, x1, y1, seed);
            return;
        }
        Color32 roof = SlateRoofs[(int)(DetailHash(seed, 13) * SlateRoofs.Length) % SlateRoofs.Length];
        string kind = icon ?? "house_1";
        if ((kind == "temple" || kind.StartsWith("civic")) && w >= 10 && h >= 10)
        {
            Compound(c, block, x0, y0, x1, y1, kind == "temple" ? PalaceWall : new Color32(150, 60, 50, 255), seed);
            return;
        }
        if (kind.StartsWith("market"))
        {
            Shops(c, block, x0, y0, x1, y1, roof, seed);
            return;
        }
        if (kind == "barracks" || kind.StartsWith("industry") || kind == "docks")
        {
            Halls(c, block, x0, y0, x1, y1, kind == "barracks" ? roof : Workshop, seed);
            return;
        }
        int tier = 1;
        if (kind.StartsWith("house_") && int.TryParse(kind.Substring(6), out int parsed)) tier = parsed;
        House(c, block, x0, y0, x1, y1, tier, roof, seed);
    }

    // 只在还是街坊空地的像素上画(切出来的宅基地可能越出不规则的街坊)
    private static void Paint(Canvas c, int px, int py, Color32 color, byte kind = KBuilding)
    {
        byte k = c.Kind(px, py);
        if (k == KGround || k == KBuilding && kind == KBuilding) c.Put(px, py, color, kind);
    }

    private static void Roof(Canvas c, int x0, int y0, int x1, int y1, bool horizontal, Color32 roof)
    {
        Color32 high = Darker(roof, 1.32f), low = Darker(roof, 0.72f);
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            Color32 color;
            if (horizontal)
            {
                float m = (y0 + y1) / 2f;
                color = Mathf.Abs(py - m) < 0.6f ? high : py == y0 || py == y1 ? low : roof;
            }
            else
            {
                float m = (x0 + x1) / 2f;
                color = Mathf.Abs(px - m) < 0.6f ? high : px == x0 || px == x1 ? low : roof;
            }
            Paint(c, px, py, color);
        }
    }

    // 东南一侧落一道影子
    private static void Shadow(Canvas c, Color32 block, int x0, int y0, int x1, int y1)
    {
        Color32 shade = Darker(block, 0.84f);
        for (int px = x0 + 1; px <= x1 + 1; px++)
            if (c.Kind(px, y0 - 1) == KGround) c.Put(px, y0 - 1, shade, KGround);
        for (int py = y0 - 1; py < y1; py++)
            if (c.Kind(x1 + 1, py) == KGround) c.Put(x1 + 1, py, shade, KGround);
    }

    // 宅基地哪一边临街(门朝那边)：0 南 1 北 2 西 3 东
    private static int StreetSide(Canvas c, int x0, int y0, int x1, int y1)
    {
        int best = -1, side = 0;
        int[] counts = new int[4];
        for (int px = x0; px <= x1; px++)
        {
            if (c.Kind(px, y0 - 2) is KStreet or KAlley or KAvenue) counts[0]++;
            if (c.Kind(px, y1 + 2) is KStreet or KAlley or KAvenue) counts[1]++;
        }
        for (int py = y0; py <= y1; py++)
        {
            if (c.Kind(x0 - 2, py) is KStreet or KAlley or KAvenue) counts[2]++;
            if (c.Kind(x1 + 2, py) is KStreet or KAlley or KAvenue) counts[3]++;
        }
        for (int i = 0; i < 4; i++)
            if (counts[i] > best)
            {
                best = counts[i];
                side = i;
            }
        return side;
    }

    // 宅子：小户两排(或一排)平房，中户三合院(背街正房、两侧厢房)，大户两进四合院；临街开门
    private static void House(Canvas c, Color32 block, int x0, int y0, int x1, int y1, int tier, Color32 roof, long seed)
    {
        int ax0 = x0 + (DetailHash(seed, 21) < 0.5f ? 1 : 0), ay0 = y0 + (DetailHash(seed, 22) < 0.5f ? 1 : 0);
        int ax1 = x1 - (DetailHash(seed, 23) < 0.4f ? 1 : 0), ay1 = y1 - (DetailHash(seed, 24) < 0.4f ? 1 : 0);
        int w = ax1 - ax0 + 1, h = ay1 - ay0 + 1;
        if (w < 4 || h < 4) return;
        Shadow(c, block, ax0, ay0, ax1, ay1);
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
            // 小户：两排平房，中间一条小院
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
        // 临街开门
        switch (side)
        {
            case 0: Paint(c, (ax0 + ax1) / 2, ay0, Door); break;
            case 1: Paint(c, (ax0 + ax1) / 2, ay1, Door); break;
            case 2: Paint(c, ax0, (ay0 + ay1) / 2, Door); break;
            default: Paint(c, ax1, (ay0 + ay1) / 2, Door); break;
        }
    }

    // 庙、衙署：一圈红墙(或朱墙)，院里铺地，正殿大屋顶
    private static void Compound(Canvas c, Color32 block, int x0, int y0, int x1, int y1, Color32 wall, long seed)
    {
        Shadow(c, block, x0, y0, x1, y1);
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
            Paint(c, px, py, px == x0 || px == x1 || py == y0 || py == y1 ? wall : TempleYard);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        Roof(c, x0 + 2, y0 + h / 2, x1 - 2, y1 - 2, true, TempleRoof);
        if (h >= 12) Roof(c, x0 + 3, y0 + 2, x1 - 3, y0 + 3, true, TempleRoof);
        Paint(c, (x0 + x1) / 2, y0, Door);
        if (DetailHash(seed, 31) < 0.7f)
        {
            Paint(c, x0 + 2, y0 + 2, TreeDark);
            Paint(c, x1 - 2, y0 + 2, TreeDark);
        }
    }

    // 铺面：一排排窄门面临街，门前支着摊棚
    private static void Shops(Canvas c, Color32 block, int x0, int y0, int x1, int y1, Color32 roof, long seed)
    {
        int side = StreetSide(c, x0, y0, x1, y1);
        bool horizontal = side <= 1;
        int length = horizontal ? x1 - x0 + 1 : y1 - y0 + 1;
        for (int t = 0, k = 0; t < length; k++)
        {
            int width = 3 + (int)(DetailHash(seed, k, 41) * 2);
            int a = (horizontal ? x0 : y0) + t, b = Mathf.Min(a + width - 1, horizontal ? x1 : y1);
            Color32 tone = Darker(roof, 0.9f + 0.25f * DetailHash(seed, k, 42));
            if (horizontal) Roof(c, a, y0 + 1, b, y1 - 1, false, tone);
            else Roof(c, x0 + 1, a, x1 - 1, b, true, tone);
            // 摊棚(临街一侧)
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

    // 营房、作坊、仓库：一院几排长屋
    private static void Halls(Canvas c, Color32 block, int x0, int y0, int x1, int y1, Color32 roof, long seed)
    {
        Shadow(c, block, x0, y0, x1, y1);
        bool horizontal = x1 - x0 >= y1 - y0;
        int span = horizontal ? y1 - y0 + 1 : x1 - x0 + 1;
        for (int t = 1; t + 2 < span; t += 4)
        {
            if (horizontal) Roof(c, x0 + 1, y0 + t, x1 - 1, y0 + t + 2, true, roof);
            else Roof(c, x0 + t, y0 + 1, x0 + t + 2, y1 - 1, false, roof);
        }
    }

    private static void Trees(Canvas c, int x0, int y0, int x1, int y1, long seed)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            float r = DetailHash(seed, px, py, 51);
            if (r < 0.35f) Paint(c, px, py, TreeDark, KGround);
            else if (r < 0.5f) Paint(c, px, py, TreeLight, KGround);
        }
    }

    // 菜园：一畦一畦
    private static void Plots(Canvas c, Color32 block, int x0, int y0, int x1, int y1, bool horizontal)
    {
        Color32 dark = Lerp(block, new Color32(150, 180, 110, 255), 0.6f), light = Lerp(block, Garden, 0.7f);
        for (int py = y0 + 1; py < y1; py++)
        for (int px = x0 + 1; px < x1; px++)
            Paint(c, px, py, ((horizontal ? py : px) & 1) == 0 ? dark : light, KGround);
    }

    // 废墟：残墙断瓦
    private static void Ruin(Canvas c, Color32 block, int x0, int y0, int x1, int y1, long seed)
    {
        Color32 rubble = new(120, 108, 96, 255), burnt = new(70, 62, 58, 255);
        for (int py = y0 + 1; py < y1; py++)
        for (int px = x0 + 1; px < x1; px++)
        {
            float r = DetailHash(seed, px, py, 61);
            bool edge = px == x0 + 1 || px == x1 - 1 || py == y0 + 1 || py == y1 - 1;
            if (edge && r < 0.6f) Paint(c, px, py, rubble);
            else if (r < 0.15f) Paint(c, px, py, burnt);
        }
    }

    private static void Walls(Canvas c, CityPlan plan, Color32 wall, Color32 tower)
    {
        int s = DetailScale;
        Color32 light = Lerp(wall, new Color32(255, 255, 255, 255), 0.28f), dark = Darker(wall, 0.74f),
            walk = Lerp(wall, new Color32(255, 255, 255, 255), 0.4f), towerTop = Darker(tower, 1.2f);
        for (int ty = 0; ty < c.H; ty++)
        for (int tx = 0; tx < c.W; tx++)
        {
            if (c.Kind(tx * s, ty * s) != KWall) continue;
            WorldTile tile = World.world.GetTile(c.X0 + tx, c.Y0 + ty);
            if (tile == null) continue;
            bool gate = tile.x == plan.gateX || tile.y == plan.gateY;
            bool towerTile = Mathf.Abs(tile.x - plan.gateX) == 1 || Mathf.Abs(tile.y - plan.gateY) == 1 || Corner(tile, plan) ||
                             (tile.x + tile.y * 3) % 7 == 0;
            bool outUp = !Urban(tile.tile_up, plan), outDown = !Urban(tile.tile_down, plan),
                outLeft = !Urban(tile.tile_left, plan), outRight = !Urban(tile.tile_right, plan);
            for (int sy = 0; sy < s; sy++)
            for (int sx = 0; sx < s; sx++)
            {
                int px = tx * s + sx, py = ty * s + sy;
                Color32 color;
                if (gate)
                    color = sx == 0 || sx == s - 1 || sy == 0 || sy == s - 1 ? new Color32(180, 70, 55, 255) : new Color32(60, 54, 58, 255);
                else if (towerTile)
                    color = sx > 0 && sx < s - 1 && sy > 0 && sy < s - 1 ? towerTop : tower;
                else
                {
                    bool outer = outLeft && sx == 0 || outRight && sx == s - 1 || outDown && sy == 0 || outUp && sy == s - 1;
                    bool inner = !outLeft && sx == 0 || !outRight && sx == s - 1 || !outDown && sy == 0 || !outUp && sy == s - 1;
                    color = outer ? ((px + py) % 2 == 0 ? light : dark) : inner ? walk : wall;
                }
                c.Put(px, py, color, KWall);
            }
        }
    }

    #endregion

    #region 城外

    private static void DrawCountryside(Canvas c, CityPlan plan, UrbanFabric.Geometry g, Color32 tint, long seed)
    {
        int s = DetailScale;
        Color32 dirt = Lerp(tint, Dirt, 0.8f), ground = Lerp(tint, Threshing, 0.85f), town = Lerp(tint, new Color32(238, 228, 200, 255), 0.85f);
        // 村子的小路(先竖后横走到大路)
        foreach ((bool vertical, int fixedAt, int from, int to) in g.Paths)
        {
            int a = Mathf.Min(from, to), b = Mathf.Max(from, to);
            for (int t = a * s; t <= b * s + s - 1; t++)
            {
                int sway = Mathf.RoundToInt(1.5f * (DetailNoise(t, 18f, seed + fixedAt) - 0.5f));
                int px = vertical ? (fixedAt - c.X0) * s + s / 2 + sway : t - c.X0 * s;
                int py = vertical ? t - c.Y0 * s : (fixedAt - c.Y0) * s + s / 2 + sway;
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
                    for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                    for (int k = 0; k < 2; k++)
                    {
                        if (DetailHash(seed, d * 4 + k, sideSign, 71) > 0.85f - d * 0.04f) continue;
                        float offset = 2.4f + DetailHash(seed, d, k, 72) * 1.2f;
                        int px = Mathf.RoundToInt((rx - c.X0 + 0.5f - road.Dy * sideSign * offset) * s + (road.Dx * k * 2f));
                        int py = Mathf.RoundToInt((ry - c.Y0 + 0.5f + road.Dx * sideSign * offset) * s + (road.Dy * k * 2f));
                        SmallHouse(c, tint, px - 2, py - 1, seed + d * 10 + k + (sideSign > 0 ? 5 : 0), DetailHash(seed, d, k, 73) < 0.3f);
                    }
                }
            }
        // 镇
        int index = 0;
        foreach ((int x, int y, int townSize, bool walled) in g.Towns) Town(c, tint, (x - c.X0) * s + s / 2, (y - c.Y0) * s + s / 2, townSize, walled, dirt, town, seed + 1000 * ++index);
        // 村
        index = 0;
        foreach ((int x, int y) in g.Villages) Village(c, tint, (x - c.X0) * s + s / 2, (y - c.Y0) * s + s / 2, dirt, ground, seed + 3000 * ++index);
        // 村镇周围的田块：压暗的垄沟(半透明，底下的国色透出来)，每块一种垄向
        var centers = new List<(int x, int y, int r)>();
        foreach ((int x, int y) in g.Villages) centers.Add(((x - c.X0) * s, (y - c.Y0) * s, 7 * s));
        foreach ((int x, int y, int townSize, _) in g.Towns) centers.Add(((x - c.X0) * s, (y - c.Y0) * s, (townSize + 6) * s));
        foreach ((int x, int y, int r) in centers)
        for (int py = Mathf.Max(0, y - r); py <= Mathf.Min(c.PH - 1, y + r); py++)
        for (int px = Mathf.Max(0, x - r); px <= Mathf.Min(c.PW - 1, x + r); px++)
        {
            if (c.Kind(px, py) != KEmpty || (px - x) * (px - x) + (py - y) * (py - y) > r * r) continue;
            WorldTile tile = c.Tile(px, py);
            if (tile?.Type == null || !tile.Type.can_be_farm && !tile.Type.farm_field || tile.zone?.city != c.City) continue;
            int fx = (int)((px + 9f * DetailNoise(py, 17f, seed + 3)) / 22f), fy = (int)((py + 9f * DetailNoise(px, 19f, seed + 4)) / 18f);
            if (DetailHash(seed, fx, fy, 81) < 0.3f) continue;
            int orientation = (int)(DetailHash(seed, fx, fy, 82) * 3);
            bool line = orientation switch { 0 => py % 3 == 0, 1 => px % 3 == 0, _ => (px + py) % 4 == 0 };
            if (line) c.Put(px, py, Furrow, KEmpty);
        }
    }

    // 一户人家：瓦顶或草顶，宽 3~5、进深 2~3；只盖在空地上，四周不贴着别的房子
    private static bool SmallHouse(Canvas c, Color32 tint, int x, int y, long seed, bool thatch, int w = 0, int h = 0)
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
        Color32 roof = thatch ? Thatch : SlateRoofs[(int)(DetailHash(seed, 4) * SlateRoofs.Length) % SlateRoofs.Length];
        Color32 high = Darker(roof, 1.3f), low = Darker(roof, 0.72f);
        bool horizontal = w >= h;
        for (int py = y; py < y + h; py++)
        for (int px = x; px < x + w; px++)
        {
            float m = horizontal ? y + (h - 1) / 2f : x + (w - 1) / 2f;
            float at = horizontal ? py : px;
            bool rim = horizontal ? py == y || py == y + h - 1 : px == x || px == x + w - 1;
            c.Put(px, py, Mathf.Abs(at - m) < 0.6f ? high : rim ? low : roof, KBuilding);
        }
        Color32 shade = Darker(Lerp(tint, Threshing, 0.5f), 0.7f);
        for (int px = x + 1; px <= x + w; px++)
            if (c.Kind(px, y - 1) == KEmpty && c.Land(px, y - 1)) c.Put(px, y - 1, new Color32(shade.r, shade.g, shade.b, 150), KEmpty);
        return true;
    }

    // 镇：一片地面，十字街、街口集市(摊棚)、沿街房子一户挨一户、背街再一排、小庙；富的镇围一圈矮墙
    private static void Town(Canvas c, Color32 tint, int cx, int cy, int townSize, bool walled, Color32 dirt, Color32 ground, long seed)
    {
        int r = townSize * DetailScale + 2;
        for (int py = cy - r; py <= cy + r; py++)
        for (int px = cx - r; px <= cx + r; px++)
        {
            float edge = r + 2.5f * (DetailNoise(Mathf.Atan2(py - cy, px - cx) * 20f, 3f, seed) - 0.5f);
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) > edge * edge || c.Kind(px, py) != KEmpty || !c.Land(px, py)) continue;
            c.Put(px, py, ground, KPlaza);
        }
        // 十字街
        for (int t = -r; t <= r; t++)
        for (int d = 0; d < 2; d++)
        {
            if (c.Kind(cx + t, cy + d) == KPlaza) c.Put(cx + t, cy + d, dirt, KPath);
            if (c.Kind(cx + d, cy + t) == KPlaza) c.Put(cx + d, cy + t, dirt, KPath);
        }
        // 集市：街口一片，摊棚错落
        for (int py = cy - 4; py <= cy + 5; py++)
        for (int px = cx - 4; px <= cx + 5; px++)
            if (c.Kind(px, py) is KPlaza or KPath) c.Put(px, py, Lerp(ground, new Color32(255, 240, 200, 255), 0.4f), KPlaza);
        for (int k = 0; k < 12; k++)
        {
            int px = cx - 3 + (int)(DetailHash(seed, k, 1) * 8), py = cy - 3 + (int)(DetailHash(seed, k, 2) * 8);
            if (Mathf.Abs(px - cx) <= 1 || Mathf.Abs(py - cy) <= 1) continue;
            c.Put(px, py, DetailHash(seed, k, 3) < 0.5f ? Stall : StallLight, KBuilding);
        }
        // 沿十字街两侧一户挨一户
        for (int dir = 0; dir < 4; dir++)
        for (int sideSign = -1; sideSign <= 1; sideSign += 2)
        {
            for (int t = 7, k = 0; t < r - 2; k++)
            {
                int w = 3 + (int)(DetailHash(seed, dir * 50 + k, sideSign, 4) * 3);
                int depth = 3 + (int)(DetailHash(seed, dir * 50 + k, sideSign, 5) * 2);
                int along = dir < 2 ? (dir == 0 ? t : -t - w + 1) : (dir == 2 ? t : -t - w + 1);
                int px, py, hw, hh;
                if (dir < 2)
                {
                    px = cx + along;
                    py = sideSign > 0 ? cy + 3 : cy - 1 - depth;
                    hw = w;
                    hh = depth;
                }
                else
                {
                    py = cy + along;
                    px = sideSign > 0 ? cx + 3 : cx - 1 - depth;
                    hw = depth;
                    hh = w;
                }
                SmallHouse(c, tint, px, py, seed + dir * 1000 + k * 7 + sideSign, DetailHash(seed, k, dir, 6) < 0.2f, hw, hh);
                t += w + (DetailHash(seed, dir, k, 7) < 0.25f ? 2 : 1);
            }
        }
        // 背街的人家
        for (int k = 0; k < townSize * 14; k++)
        {
            int px = cx + (int)((DetailHash(seed, k, 8) - 0.5f) * 2f * (r - 3)), py = cy + (int)((DetailHash(seed, k, 9) - 0.5f) * 2f * (r - 3));
            SmallHouse(c, tint, px, py, seed + 5000 + k, DetailHash(seed, k, 10) < 0.35f);
        }
        // 小庙(一角)
        int qx = DetailHash(seed, 11) < 0.5f ? 1 : -1, qy = DetailHash(seed, 12) < 0.5f ? 1 : -1;
        int tx0 = cx + qx * 6 - (qx < 0 ? 9 : 0), ty0 = cy + qy * 6 - (qy < 0 ? 7 : 0);
        bool free = true;
        for (int py = ty0; py < ty0 + 8 && free; py++)
        for (int px = tx0; px < tx0 + 10; px++)
            if (c.Kind(px, py) != KPlaza) { free = false; break; }
        if (free)
        {
            for (int py = ty0; py < ty0 + 8; py++)
            for (int px = tx0; px < tx0 + 10; px++)
                c.Put(px, py, px == tx0 || px == tx0 + 9 || py == ty0 || py == ty0 + 7 ? PalaceWall : TempleYard, KBuilding);
            for (int py = ty0 + 3; py <= ty0 + 5; py++)
            for (int px = tx0 + 2; px <= tx0 + 7; px++)
                c.Put(px, py, py == ty0 + 4 ? Darker(TempleRoof, 1.3f) : TempleRoof, KBuilding);
        }
        // 矮墙(夯土)一圈，街口留门
        if (!walled) return;
        Color32 rammed = Lerp(new Color32(124, 112, 100, 255), new Color32(210, 190, 150, 255), 0.5f);
        for (int deg = 0; deg < 360; deg++)
        {
            float a = deg * Mathf.Deg2Rad, rr = r + 1 + 1.5f * Mathf.Sin(a * 3f);
            int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * rr), py = cy + Mathf.RoundToInt(Mathf.Sin(a) * rr);
            if (Mathf.Abs(px - cx) <= 2 || Mathf.Abs(py - cy) <= 2) continue;
            if (c.Kind(px, py) is KEmpty or KPlaza && c.Land(px, py)) c.Put(px, py, rammed, KWall);
        }
    }

    // 村：打谷场居中，十几户人家沿村里一条弯路、围着打谷场挤在一起；树、池塘、菜园
    private static void Village(Canvas c, Color32 tint, int cx, int cy, Color32 dirt, Color32 ground, long seed)
    {
        if (!c.In(cx, cy) || !c.Land(cx, cy)) return;
        // 打谷场
        for (int py = cy - 3; py <= cy + 3; py++)
        for (int px = cx - 3; px <= cx + 3; px++)
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= 7 && c.Kind(px, py) is KEmpty or KPath && c.Land(px, py))
                c.Put(px, py, ground, KPlaza);
        // 村里一条弯路
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
        // 池塘
        if (DetailHash(seed, 2) < 0.6f)
        {
            int ox = cx + (DetailHash(seed, 3) < 0.5f ? 9 : -10), oy = cy + (DetailHash(seed, 4) < 0.5f ? 6 : -7);
            for (int py = oy - 2; py <= oy + 2; py++)
            for (int px = ox - 3; px <= ox + 3; px++)
                if ((px - ox) * (px - ox) / 10f + (py - oy) * (py - oy) / 4f <= 1f && c.Kind(px, py) == KEmpty && c.Land(px, py))
                    c.Put(px, py, (px + py) % 5 == 0 ? Lerp(WaterBlue, new Color32(255, 255, 255, 255), 0.35f) : WaterBlue, KWater);
        }
        // 人家：沿弯路两侧、围着打谷场
        int want = 10 + (int)(DetailHash(seed, 5) * 10), placed = 0;
        bool horizontalRoad = Mathf.Abs(Mathf.Cos(angle)) > 0.5f;
        for (int k = 0; k < want * 10 && placed < want; k++)
        {
            (int ax, int ay) = along[(int)(DetailHash(seed, k, 6) * along.Count) % along.Count];
            int sideSign = DetailHash(seed, k, 7) < 0.5f ? 1 : -1;
            int offset = 2 + (int)(DetailHash(seed, k, 8) * 3);
            int jitter = (int)((DetailHash(seed, k, 9) - 0.5f) * 3f);
            int px = horizontalRoad ? ax + jitter : ax + sideSign * offset - (sideSign < 0 ? 3 : 0);
            int py = horizontalRoad ? ay + sideSign * offset - (sideSign < 0 ? 2 : 0) : ay + jitter;
            if (SmallHouse(c, tint, px, py, seed + 100 + k, DetailHash(seed, k, 10) < 0.55f)) placed++;
        }
        // 树、菜园
        for (int k = 0; k < 18; k++)
        {
            float a = DetailHash(seed, k, 11) * Mathf.PI * 2f, rr = 6f + DetailHash(seed, k, 12) * 14f;
            int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * rr), py = cy + Mathf.RoundToInt(Mathf.Sin(a) * rr * 0.8f);
            if (c.Kind(px, py) == KEmpty && c.Land(px, py)) c.Put(px, py, DetailHash(seed, k, 13) < 0.6f ? TreeDark : TreeLight, KBuilding);
        }
        for (int k = 0; k < 3; k++)
        {
            int ox = cx + (int)((DetailHash(seed, k, 14) - 0.5f) * 26f), oy = cy + (int)((DetailHash(seed, k, 15) - 0.5f) * 22f);
            for (int py = oy; py < oy + 4; py++)
            for (int px = ox; px < ox + 5; px++)
                if (c.Kind(px, py) == KEmpty && c.Land(px, py))
                    c.Put(px, py, px % 2 == 0 ? Lerp(tint, new Color32(150, 196, 110, 255), 0.7f) : Lerp(tint, new Color32(120, 160, 90, 255), 0.6f), KPlaza);
        }
    }

    #endregion
}
