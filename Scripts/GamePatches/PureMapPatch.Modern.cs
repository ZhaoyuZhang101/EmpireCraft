using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市精细层第三步：近代以后(用户 2026-10-16 "那工厂呢""高楼大厦呢"，看过演示版说"可以，移植进游戏")。
// 新城区的格局用平面图同一份(见 UrbanFabric：环路、放射干道、铁路车站、各地块的用途与开发先后)，只是画得细：
//   · 有城墙的城拆了城墙，原址修成柏油环路；环路、放射干道是柏油路，路肩深色、中线黄虚线，过河是桥；
//   · 铁路两条钢轨、枕木，老城边一座车站(站台、站房)；
//   · 工业区：水泥厂院一圈围墙，锯齿屋顶的厂房，烟囱冒烟，储罐，铁路支线；
//   · 住宅：外圈沿小巷两侧临巷排房(工业化时是连排工人住宅，往后越来越松)，屋后小院；
//     内圈(电气以后)西欧、罗马、维京是围合街坊(一块地切成几个小街坊，一圈楼围着院子)，其余是板楼小区
//     (一圈院墙，里面一排排长条楼)；现代以后内圈有的改成绿地里的高层住宅(板式、点式)；
//   · 商场(大平顶、停车场)、营区(营房、操场)、公园(树丛、小路、池塘)；
//   · 现代市中心起高楼大厦：地块一刀刀切成楼地，每块一栋(裙楼加主楼、方楼、退台楼、圆楼、L 形楼)，越近市中心越高；
//     玻璃幕墙有竖框与反光，石材楼顶有空调机组，全城最高那栋有停机坪；太阳在西北，楼影往东南拖，长短照楼高，
//     压在街道、矮房和更矮的楼顶上。
// 还没开发的老城地块照古城画(见 DrawUrban)；都城地标照旧。
public partial class PureMapPatch
{
    // 新城区的空地(新城区的画法只画在这上面，不碰古城)
    private const byte KLot = 12;

    private static readonly Color32 Asphalt = Rgb(104, 106, 112), AsphaltEdge = Rgb(84, 86, 92), LaneMark = Rgb(214, 204, 150),
        BridgeRail = Rgb(150, 140, 128), Steel = Rgb(150, 150, 158), Sleeper = Rgb(104, 82, 64), Ballast = Rgb(124, 116, 106),
        Platform = Rgb(214, 210, 200), Lawn = Rgb(160, 194, 128), LawnDark = Rgb(146, 182, 116), Yard = Rgb(178, 198, 150),
        LaneLight = Rgb(216, 212, 206), Paving = Rgb(216, 214, 208), PavingLine = Rgb(206, 204, 198), Smoke = Rgb(236, 236, 236);
    private static readonly Color32[] GlassTones =
        { Rgb(112, 148, 186), Rgb(132, 164, 198), Rgb(98, 128, 164), Rgb(120, 164, 172), Rgb(88, 112, 140) };
    private static readonly Color32[] StoneTones =
        { Rgb(214, 206, 192), Rgb(192, 188, 182), Rgb(222, 216, 208), Rgb(182, 170, 156), Rgb(166, 160, 158) };
    private static readonly Color32[] HomeTones =
        { Rgb(220, 210, 194), Rgb(206, 196, 184), Rgb(226, 220, 210), Rgb(200, 186, 172), Rgb(214, 200, 182) };

    private enum HighriseShape { Box, Stepped, Round, L, Cross }

    // 一栋高楼(写字楼、高层住宅、板楼)：占地、裙楼宽、形状、高(影子长多少像素)
    private sealed class Highrise
    {
        public int X, Y, W, H, Pad, Cut, Height;
        public HighriseShape Shape;
        public bool Glass, Home, Heli;
        public long Seed;
        public float Tone;
    }

    private static Color32 Rgba(int r, int g, int b, int a) => new((byte)r, (byte)g, (byte)b, (byte)a);

    // 只画在新城区空地上
    private static void OnLot(Canvas c, int px, int py, Color32 color, byte kind = KBuilding)
    {
        if (c.Kind(px, py) == KLot) c.Put(px, py, color, kind);
    }

    private static void LotBox(Canvas c, int x0, int y0, int x1, int y1, Color32 color, byte kind)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
            OnLot(c, px, py, color, kind);
    }

    private static bool ClearLot(Canvas c, int x0, int y0, int x1, int y1)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
            if (c.Kind(px, py) != KLot) return false;
        return true;
    }

    // 一格柏油路(古城墙拆了修的环路)：两边深色路肩，中间黄虚线
    private static void AsphaltTile(Canvas c, int tx, int ty, bool vertical)
    {
        int s = DetailScale;
        for (int sy = 0; sy < s; sy++)
        for (int sx = 0; sx < s; sx++)
        {
            int across = vertical ? sx : sy, along = vertical ? ty * s + sy : tx * s + sx;
            Color32 color = across == 0 || across == s - 1 ? AsphaltEdge : across == 1 && along % 10 < 4 ? LaneMark : Asphalt;
            c.Put(tx * s + sx, ty * s + sy, color, KAvenue);
        }
    }

    // 近代以后的新城区：① 铺底(地块之间两像素的街) ② 环路、放射干道 ③ 铁路车站 ④ 逐地块按用途画 ⑤ 高楼与楼影
    private static void DrawModern(Canvas c, CityPlan plan, UrbanFabric.Geometry g, DetailStyle style, Color32 tint, long seed)
    {
        int s = DetailScale, size = Config.CITY_ZONE_SIZE;
        Color32 local = Lerp(tint, Rgb(222, 222, 226), 0.9f);
        var modern = new bool[c.W * c.H];
        var zones = new Dictionary<int, (TileZone zone, UrbanFabric.Kind kind)>();
        // ① 新城区一格格铺底：古城、地标、旧城墙那圈环路已经画过的不动
        for (int ty = 0; ty < c.H; ty++)
        for (int tx = 0; tx < c.W; tx++)
        {
            WorldTile tile = World.world.GetTile(c.X0 + tx, c.Y0 + ty);
            if (tile?.Type == null || tile.zone == null || tile.zone.city != c.City || tile.Type.liquid) continue;
            if (c.Kind(tx * s, ty * s) != KEmpty) continue;
            UrbanFabric.Part part = UrbanFabric.PartAt(g, tile, ZonePlanSystem.Get(c.City, tile.zone), out UrbanFabric.Kind kind);
            if (part == UrbanFabric.Part.None) continue;
            modern[ty * c.W + tx] = true;
            int lx = tile.x - tile.zone.x * size, ly = tile.y - tile.zone.y * size;
            for (int sy = 0; sy < s; sy++)
            for (int sx = 0; sx < s; sx++)
            {
                bool street = lx == 0 && sx < 2 || ly == 0 && sy < 2;
                c.Put(tx * s + sx, ty * s + sy, street ? local : c.Block, street ? KStreet : KLot);
            }
            if (part == UrbanFabric.Part.Block) zones[tile.zone.id] = (tile.zone, kind);
        }
        // 能画路、铁路的像素：新城区的格子，或还空着的、水上(桥)；不碰古城、地标
        bool Open(int px, int py)
        {
            byte k = c.Kind(px, py);
            if (k is KOff or KWall or KFixed) return false;
            if (k is KEmpty or KWater) return true;
            int tx = px / s, ty = py / s;
            return modern[ty * c.W + tx] || k == KAvenue;
        }
        // ② 环路、放射干道：宽约一格多，路肩深色、中线黄虚线，过河是桥
        for (int ty = 0; ty < c.H; ty++)
        for (int tx = 0; tx < c.W; tx++)
        {
            int wx = c.X0 + tx, wy = c.Y0 + ty;
            WorldTile tile = World.world.GetTile(wx, wy);
            if (tile?.zone == null || tile.zone.city != c.City || plan.landmark.Contains(tile.zone.id)) continue;
            if (UrbanFabric.RoadDistance(g, wx, wy, out _) > 1.4f) continue;
            for (int sy = 0; sy < s; sy++)
            for (int sx = 0; sx < s; sx++)
            {
                int px = tx * s + sx, py = ty * s + sy;
                float d = UrbanFabric.RoadDistance(g, wx + (sx + 0.5f) / s - 0.5f, wy + (sy + 0.5f) / s - 0.5f, out float along);
                if (d >= 0.62f || !Open(px, py)) continue;
                bool rim = d >= 0.45f, water = c.Kind(px, py) == KWater;
                Color32 color = rim ? water ? BridgeRail : AsphaltEdge : d < 0.1f && Mathf.FloorToInt(along * s) % 10 < 4 ? LaneMark : Asphalt;
                c.Put(px, py, color, KAvenue);
            }
        }
        // ③ 铁路：两条钢轨、枕木，从老城边通到城区外缘；老城边一座车站(铁路一侧站房，两侧站台)
        if (g.Progress >= 0.4f)
            for (int ty = 0; ty < c.H; ty++)
            for (int tx = 0; tx < c.W; tx++)
            {
                int wx = c.X0 + tx, wy = c.Y0 + ty;
                WorldTile tile = World.world.GetTile(wx, wy);
                if (tile?.zone == null || tile.zone.city != c.City || plan.landmark.Contains(tile.zone.id)) continue;
                if (!UrbanFabric.RailAt(g, wx, wy, out float across0, out float along0) || Mathf.Abs(across0) > 2.6f ||
                    along0 < g.OldRadius) continue;
                for (int sy = 0; sy < s; sy++)
                for (int sx = 0; sx < s; sx++)
                {
                    int px = tx * s + sx, py = ty * s + sy;
                    float x = wx + (sx + 0.5f) / s - 0.5f, y = wy + (sy + 0.5f) / s - 0.5f;
                    if (!UrbanFabric.RailAt(g, x, y, out float across, out float along) || !Open(px, py)) continue;
                    int v = Mathf.RoundToInt(across * s);
                    if (along > g.OldRadius + 1.5f && along < g.OldRadius + 4f)
                    {
                        // 车站：站房在铁路一侧，站台夹着铁轨
                        if (v >= 3 && v <= 6)
                        {
                            c.Put(px, py, v == 3 || v == 6 ? Rgb(120, 70, 60) : v == 4 ? Rgb(196, 110, 90) : Rgb(168, 90, 72), KBuilding);
                            continue;
                        }
                        if (v == 2 || v == -2 || v == -3)
                        {
                            c.Put(px, py, Platform, KPlaza);
                            continue;
                        }
                    }
                    if (along <= g.OldRadius + 1f || along >= UrbanFabric.EdgeOf(g, x, y) || Mathf.Abs(v) > 2) continue;
                    bool tie = Mathf.FloorToInt(along * s) % 3 == 0, water = c.Kind(px, py) == KWater;
                    if (Mathf.Abs(v) == 1) c.Put(px, py, Steel, KAvenue);
                    else if (tie) c.Put(px, py, Sleeper, KAvenue);
                    else if (v == 0 && !water) c.Put(px, py, Ballast, KAvenue);
                }
            }
        // ④ 逐地块：工业区、商场、营区、公园、住宅；现代市中心起高楼
        var sky = new List<Highrise>();
        foreach ((TileZone zone, UrbanFabric.Kind kind) in zones.Values)
        {
            int x0 = (zone.x * size - c.X0) * s + 2, y0 = (zone.y * size - c.Y0) * s + 2, x1 = x0 + size * s - 3, y1 = y0 + size * s - 3;
            long zseed = seed * 977 + zone.id * 131L;
            float zx = zone.x * size + 4f, zy = zone.y * size + 4f, edge = UrbanFabric.EdgeOf(g, zx, zy);
            float r = Mathf.Sqrt((zx - g.Cx) * (zx - g.Cx) + (zy - g.Cy) * (zy - g.Cy));
            // 新城区地块的标注落点(地块中心)：工厂、商场、营区、写字楼；住宅按小区标
            string labelIcon = kind switch
            {
                UrbanFabric.Kind.Industry => "industry_1",
                UrbanFabric.Kind.Market => "market_1",
                UrbanFabric.Kind.Military => "barracks",
                UrbanFabric.Kind.Tower => "civic_1",
                UrbanFabric.Kind.Park => null,
                _ => "house_3"
            };
            if (labelIcon != null)
                c.Labels.Add(new DetailLabel
                {
                    Tile = c.TileAt((x0 + x1) / 2f, (y0 + y1) / 2f), Icon = labelIcon, Key = zone.id * 4L,
                    Weight = (x1 - x0) * (y1 - y0)
                });
            switch (kind)
            {
                case UrbanFabric.Kind.Industry: FactoryLot(c, x0, y0, x1, y1, zseed, g.Progress); break;
                case UrbanFabric.Kind.Market: MallLot(c, x0, y0, x1, y1); break;
                case UrbanFabric.Kind.Military: BarracksLot(c, x0, y0, x1, y1); break;
                case UrbanFabric.Kind.Park: ParkLot(c, x0, y0, x1, y1, zseed); break;
                case UrbanFabric.Kind.Tower:
                {
                    // 越近市中心越高
                    float cbd = Mathf.Sqrt((zx - g.CbdX) * (zx - g.CbdX) + (zy - g.CbdY) * (zy - g.CbdY)), reach = Mathf.Max(8f, edge * 0.36f);
                    sky.AddRange(LayoutTowers(c, x0, y0, x1, y1, zseed, Mathf.Clamp(1.15f - cbd / (reach * 1.1f), 0.15f, 1f)));
                    break;
                }
                default:
                    if (r < edge * 0.75f && g.Progress >= 1.3f)
                    {
                        if (g.Perimeter) PerimeterBlock(c, style, x0, y0, x1, y1, zseed);
                        else if (g.Progress >= 2.2f && DetailHash(zseed, 76) < 0.6f) sky.AddRange(LayoutResidential(c, x0, y0, x1, y1, zseed));
                        else sky.AddRange(LayoutSlabs(c, x0, y0, x1, y1, zseed));
                    }
                    else LowriseLots(c, style, x0, y0, x1, y1, zseed, g.Progress);
                    break;
            }
        }
        // ⑤ 全城最高那栋写字楼顶有停机坪；先画楼，再落影
        Highrise top = null;
        foreach (Highrise b in sky)
            if (!b.Home && b.Shape != HighriseShape.Stepped && b.W >= 6 && b.H >= 6 && (top == null || b.Height > top.Height)) top = b;
        if (top != null) top.Heli = true;
        foreach (Highrise b in sky) PaintTower(c, b);
        CastShadows(c, sky);
    }

    #region 新城区各地块

    // 工厂：水泥地的厂院，一圈围墙；一两座锯齿屋顶的厂房(亮暗相间的条纹)，烟囱冒烟，储罐，铁路支线
    private static void FactoryLot(Canvas c, int x0, int y0, int x1, int y1, long seed, float progress)
    {
        Color32 yard = Rgb(178, 176, 172), wall = Rgb(140, 126, 116);
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
            OnLot(c, px, py, px == x0 || px == x1 || py == y0 || py == y1 ? (px + py) % 2 == 1 ? wall : yard : yard, KPlaza);
        Color32[] roofs = progress >= 1f
            ? new[] { Rgb(150, 150, 158), Rgb(140, 146, 154), Rgb(166, 162, 156) }
            : new[] { Rgb(166, 118, 100), Rgb(150, 108, 94), Rgb(160, 140, 128) };
        bool horizontal = DetailHash(seed, 1) < 0.5f;
        int halls = DetailHash(seed, 2) < 0.5f ? 1 : 2, span = horizontal ? y1 - y0 - 3 : x1 - x0 - 3;
        for (int h = 0; h < halls; h++)
        {
            Color32 roof = Pick(roofs, DetailHash(seed, h, 3)), high = Darker(roof, 1.22f), low = Darker(roof, 0.78f);
            int a0 = (horizontal ? y0 : x0) + 2 + h * span / halls, a1 = (horizontal ? y0 : x0) + 1 + (h + 1) * span / halls - (halls > 1 ? 2 : 0);
            int b0 = (horizontal ? x0 : y0) + 2, b1 = (horizontal ? x1 : y1) - 2 - (DetailHash(seed, h, 4) < 0.5f ? 5 : 0);
            for (int a = a0; a <= a1; a++)
            for (int b = b0; b <= b1; b++)
            {
                int px = horizontal ? b : a, py = horizontal ? a : b;
                if (c.Kind(px, py) != KPlaza) continue;
                // 锯齿屋顶：每三像素一道齿(亮、中、暗)
                int t = (b - b0) % 3;
                c.Put(px, py, t == 0 ? high : t == 1 ? roof : low, KBuilding);
            }
            for (int b = b0 + 1; b <= b1 + 1; b++)
            {
                int px = horizontal ? b : a1 + 1, py = horizontal ? a1 + 1 : b;
                if (c.Kind(px, py) == KPlaza) c.Put(px, py, Darker(yard, 0.82f), KPlaza);
            }
        }
        // 烟囱与烟(越远越淡越散)
        int chimneys = 1 + (int)(DetailHash(seed, 5) * 2);
        for (int k = 0; k < chimneys; k++)
        {
            int x = x1 - 3 - k * 4, y = y0 + 3 + k * 3;
            byte under = c.Kind(x, y);
            if (under != KPlaza && under != KBuilding) continue;
            foreach ((int dx, int dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) }) c.Put(x + dx, y + dy, Rgb(116, 62, 50), KBuilding);
            c.Put(x, y, Rgb(70, 40, 34), KBuilding);
            for (int i = 1; i <= 9; i++)
            {
                float sx = x + i, sy = y + i * 0.7f + Mathf.Sin(i * 1.3f + k) * 0.8f;
                int spread = 1 + i / 3;
                for (int d = 0; d < spread; d++)
                {
                    int px = Mathf.RoundToInt(sx + (DetailHash(seed, k * 16 + i, d, 8) - 0.5f) * spread);
                    int py = Mathf.RoundToInt(sy + (DetailHash(seed, k * 16 + i, d + 9, 8) - 0.5f) * spread);
                    byte kind = c.Kind(px, py);
                    if (kind != KOff) c.Put(px, py, Rgba(Smoke.r, Smoke.g, Smoke.b, Mathf.Max(40, 170 - i * 14)), kind);
                }
            }
        }
        // 储罐
        if (DetailHash(seed, 6) < 0.6f)
        {
            int tx = x0 + 5, ty = y1 - 5;
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 3.2f || c.Kind(tx + dx, ty + dy) != KPlaza) continue;
                c.Put(tx + dx, ty + dy, d > 2.3f ? Rgb(150, 150, 156) : dx + dy < -1 ? Rgb(232, 232, 236) : Rgb(208, 208, 214), KBuilding);
            }
        }
        // 铁路支线(沿厂院一边)
        if (progress >= 0.4f && DetailHash(seed, 7) < 0.5f)
            for (int b = (horizontal ? x0 : y0) + 1; b <= (horizontal ? x1 : y1) - 1; b++)
            {
                int px = horizontal ? b : x0 + 1, py = horizontal ? y0 + 1 : b;
                if (c.Kind(px, py) == KPlaza) c.Put(px, py, b % 3 == 0 ? Sleeper : Steel, KPlaza);
            }
    }

    // 商场：大平顶(屋顶机组)，旁边停车场
    private static void MallLot(Canvas c, int x0, int y0, int x1, int y1)
    {
        int mid = (x0 + x1) / 2;
        for (int py = y0 + 1; py < y1; py++)
        for (int px = x0 + 1; px <= mid; px++)
            OnLot(c, px, py, px == x0 + 1 || px == mid || py == y0 + 1 || py == y1 - 1 ? Rgb(196, 196, 202)
                : (px * 7 + py * 3) % 17 == 0 ? Rgb(150, 150, 156) : Rgb(224, 224, 228));
        for (int py = y0; py <= y1; py++)
        for (int px = mid + 2; px <= x1; px++)
            OnLot(c, px, py, (px - mid) % 3 == 0 && (py - y0) % 7 != 0 ? Rgb(236, 236, 236) : Rgb(164, 164, 170), KPlaza);
    }

    // 营区：几排营房，一片操场
    private static void BarracksLot(Canvas c, int x0, int y0, int x1, int y1)
    {
        LotBox(c, x0, y0, x1, y1, Rgb(196, 184, 150), KPlaza);
        for (int py = y0 + 1; py + 2 <= (y0 + y1) / 2; py += 4)
        for (int px = x0 + 1; px <= x1 - 1; px++)
        for (int d = 0; d < 2; d++)
            if (c.Kind(px, py + d) == KPlaza) c.Put(px, py + d, d == 0 ? Rgb(140, 148, 112) : Rgb(118, 126, 94), KBuilding);
    }

    // 公园：草地、树丛、弯弯的小路、池塘
    private static void ParkLot(Canvas c, int x0, int y0, int x1, int y1, long seed)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            float r = DetailHash(seed, px, py, 1);
            OnLot(c, px, py, r < 0.3f ? Rgb(86, 140, 76) : r < 0.45f ? Rgb(116, 168, 98) : Rgb(156, 196, 126), KLot);
        }
        int ox = (x0 + x1) / 2 + 3, oy = (y0 + y1) / 2 - 2;
        for (int py = oy - 3; py <= oy + 3; py++)
        for (int px = ox - 5; px <= ox + 5; px++)
            if ((px - ox) * (px - ox) / 25f + (py - oy) * (py - oy) / 9f <= 1f && c.Kind(px, py) == KLot)
                c.Put(px, py, (px + py) % 5 != 0 ? WaterBlue : Ripple, KWater);
        for (int t = 0; t < 60; t++)
        {
            int px = Mathf.RoundToInt(x0 + 2 + t * (x1 - x0 - 4) / 60f), py = Mathf.RoundToInt((y0 + y1) / 2f + 6f * Mathf.Sin(t / 9f));
            if (c.Kind(px, py) == KLot) c.Put(px, py, Rgb(226, 214, 180), KLot);
        }
    }

    // 低层住宅：沿小巷两侧临巷排房，面宽不一，屋后小院；工业化时是连排工人住宅，往后越来越松
    private static void LowriseLots(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1, long seed, float progress)
    {
        bool flatRoof = style.FlatRoofs, horizontal = DetailHash(seed, 1) < 0.5f, terrace = progress < 1.3f;
        int a0 = horizontal ? y0 : x0, a1 = horizontal ? y1 : x1, b0 = horizontal ? x0 : y0, b1 = horizontal ? x1 : y1;
        float gapChance = terrace ? 0.08f : 0.35f;
        void Set(int a, int b, Color32 color, byte kind)
        {
            int px = horizontal ? b : a, py = horizontal ? a : b;
            if (c.Kind(px, py) == KLot) c.Put(px, py, color, kind);
        }
        int lane = 0;
        for (int a = a0 + 5 + (int)(DetailHash(seed, 2) * 2); a <= a1 - 4; a += 10 + (int)(DetailHash(seed, lane, 3) * 3), lane++)
        {
            int stop = DetailHash(seed, lane, 4) < 0.2f ? b0 + (int)((b1 - b0) * (0.4f + 0.4f * DetailHash(seed, lane, 5))) : b1;
            for (int b = b0; b <= stop; b++) Set(a, b, LaneLight, KAlley);
            for (int side = -1; side <= 1; side += 2)
            {
                int b = b0 + 1 + (int)(DetailHash(seed, lane, side, 6) * 2);
                while (b < stop - 2)
                {
                    float r = DetailHash(seed, lane * 7 + side, b, 7);
                    int width = 3 + (int)(DetailHash(seed, lane, b, side + 8) * (terrace ? 2 : 3));
                    int front = DetailHash(seed, b, lane, 9) < 0.3f ? 1 : 0;
                    int depth = terrace ? 3 : 2 + (int)(DetailHash(seed, b, side, 10) * 2);
                    if (r < 0.1f)
                    {
                        // 空一块：菜地或树
                        for (int u = 0; u < width; u++)
                        for (int d = 1; d <= 4; d++)
                            Set(a + side * d, b + u, (u + d) % 2 == 1 ? Rgb(150, 176, 108) : Rgb(124, 158, 92), KLot);
                        if (r < 0.05f) Set(a + side * 2, b + 1, Rgb(86, 140, 76), KLot);
                    }
                    else
                    {
                        Color32 roof = Pick(style.Roofs, DetailHash(seed, lane, b, side + 11));
                        for (int u = 0; u < width; u++)
                        for (int d = 0; d < depth; d++)
                        {
                            Color32 color = flatRoof
                                ? u == 0 || u == width - 1 || d == 0 || d == depth - 1 ? Darker(roof, 0.86f) : roof
                                : d == depth / 2 ? Darker(roof, 1.28f) : d > depth / 2f ? Darker(roof, 0.88f) : roof;
                            Set(a + side * (1 + front + d), b + u, color, KBuilding);
                        }
                        // 屋后小院，偶尔一棵树
                        for (int u = 0; u < width; u++)
                        for (int d = 0; d < 2; d++)
                            Set(a + side * (1 + front + depth + d), b + u, Yard, KLot);
                        if (DetailHash(seed, lane, b, 12) < (terrace ? 0.1f : 0.35f)) Set(a + side * (2 + front + depth), b + width / 2, TreeDark, KLot);
                    }
                    b += width + (DetailHash(seed, lane, b, 13) < gapChance ? 1 : 0);
                }
            }
        }
    }

    // 围合街坊(西欧、罗马、维京)：一块地切成两三个小街坊，每个一圈楼围着院子；楼一栋栋颜色不同，偶尔伸进院里一翼
    private static void PerimeterBlock(Canvas c, DetailStyle style, int x0, int y0, int x1, int y1, long seed)
    {
        int mx = Mathf.RoundToInt((x0 + x1) / 2f + (DetailHash(seed, 1) - 0.5f) * 6f), my = Mathf.RoundToInt((y0 + y1) / 2f + (DetailHash(seed, 2) - 0.5f) * 6f);
        bool splitY = DetailHash(seed, 3) < 0.75f;
        for (int py = y0; py <= y1; py++) OnLot(c, mx, py, LaneLight, KAlley);
        if (splitY)
            for (int px = x0; px <= x1; px++) OnLot(c, px, my, LaneLight, KAlley);
        (int, int, int, int)[] parts = splitY
            ? new[] { (x0, y0, mx - 1, my - 1), (mx + 1, y0, x1, my - 1), (x0, my + 1, mx - 1, y1), (mx + 1, my + 1, x1, y1) }
            : new[] { (x0, y0, mx - 1, y1), (mx + 1, y0, x1, y1) };
        const int depth = 3;
        for (int si = 0; si < parts.Length; si++)
        {
            (int a0, int b0, int a1, int b1) = parts[si];
            int wingAt = DetailHash(seed, si, 4) < 0.5f ? a0 + 3 + (int)(DetailHash(seed, si, 5) * Mathf.Max(1, a1 - a0 - 7)) : -99;
            for (int py = b0; py <= b1; py++)
            for (int px = a0; px <= a1; px++)
            {
                bool ring = px - a0 < depth || a1 - px < depth || py - b0 < depth || b1 - py < depth;
                bool wing = (px == wingAt || px == wingAt + 1) && py - b0 < depth + 3;
                if (!ring && !wing)
                {
                    OnLot(c, px, py, DetailHash(seed, px, py, 6) < 0.12f ? TreeDark : DetailHash(seed, px, py, 7) < 0.5f ? Rgb(176, 194, 150) : Rgb(196, 196, 180), KLot);
                    continue;
                }
                // 沿一圈按周长分成一栋栋
                int t = py - b0 < depth ? px - a0
                    : a1 - px < depth ? a1 - a0 + (py - b0)
                    : b1 - py < depth ? (a1 - a0) * 2 + (b1 - b0) - (px - a0)
                    : (a1 - a0) * 2 + (b1 - b0) * 2 - (py - b0);
                int segment = Mathf.FloorToInt(t / 5f + DetailHash(seed, si, 8) * 3f);
                Color32 roof = Pick(style.Roofs, DetailHash(seed, si, segment, 9));
                bool split = t % 5 == 0, outer = px == a0 || px == a1 || py == b0 || py == b1;
                bool inner = px - a0 == 1 || a1 - px == 1 || py - b0 == 1 || b1 - py == 1;
                OnLot(c, px, py, split ? Darker(roof, 0.78f) : outer ? roof : inner ? Darker(roof, 1.22f) : Darker(roof, 0.9f));
            }
        }
    }

    // 板楼小区：一圈院墙，里面一排排五六层的长条楼(多朝南)，楼间绿地，楼也落影
    private static List<Highrise> LayoutSlabs(Canvas c, int x0, int y0, int x1, int y1, long seed)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            bool rim = px == x0 || px == x1 || py == y0 || py == y1;
            bool gate = rim && (Mathf.Abs(px - (x0 + x1) / 2f) < 2f || Mathf.Abs(py - (y0 + y1) / 2f) < 2f);
            if (rim && !gate) OnLot(c, px, py, Rgb(150, 146, 140), KAlley);
            else
            {
                float r = DetailHash(seed, px, py, 1);
                OnLot(c, px, py, r < 0.08f ? TreeDark : r < 0.4f ? Rgb(150, 184, 120) : Rgb(164, 196, 132), KLot);
            }
        }
        bool horizontal = DetailHash(seed, 2) < 0.8f;
        int a0 = horizontal ? y0 : x0, a1 = horizontal ? y1 : x1, b0 = horizontal ? x0 : y0, b1 = horizontal ? x1 : y1;
        var result = new List<Highrise>();
        for (int a = a0 + 2, row = 0; a + 3 <= a1 - 2; a += 7, row++)
        {
            int n = DetailHash(seed, row, 3) < 0.5f ? 2 : 3;
            float span = (b1 - b0 - 3) / (float)n;
            for (int i = 0; i < n; i++)
            {
                if (DetailHash(seed, row, i, 4) < 0.06f) continue;
                int b = Mathf.RoundToInt(b0 + 2 + i * span), length = Mathf.RoundToInt(span) - 2;
                if (length < 5) continue;
                result.Add(new Highrise
                {
                    X = horizontal ? b : a, Y = horizontal ? a : b, W = horizontal ? length : 3, H = horizontal ? 3 : length,
                    Shape = HighriseShape.Box, Home = true, Height = 3 + (int)(DetailHash(seed, row, i, 5) * 2),
                    Seed = seed + row * 13 + i, Tone = DetailHash(seed, row, i, 6)
                });
            }
        }
        return result;
    }

    // 高层住宅：绿地里两排楼，板式(长条)与点式(方的、十字的)，楼间小路
    private static List<Highrise> LayoutResidential(Canvas c, int x0, int y0, int x1, int y1, long seed)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            float r = DetailHash(seed, px, py, 1);
            OnLot(c, px, py, r < 0.1f ? TreeDark : r < 0.4f ? LawnDark : Lawn, KLot);
        }
        int midY = (y0 + y1) / 2;
        for (int px = x0; px <= x1; px++) OnLot(c, px, midY, LaneLight, KAlley);
        var result = new List<Highrise>();
        bool slab = DetailHash(seed, 2) < 0.45f;
        for (int j = 0; j < 2; j++)
        {
            int ry0 = j == 1 ? midY + 2 : y0 + 1, ry1 = j == 1 ? y1 - 1 : midY - 2;
            int n = DetailHash(seed, j, 3) < 0.5f ? 2 : 3;
            float span = (x1 - x0 - 1) / (float)n;
            for (int i = 0; i < n; i++)
            {
                if (slab)
                {
                    // 板楼：一排两三栋长条楼
                    int bx = Mathf.RoundToInt(x0 + 1 + i * span), bw = Mathf.RoundToInt(span) - 2, bh = 5 + (int)(DetailHash(seed, i, j, 4) * 2);
                    int by = ry0 + (int)(DetailHash(seed, i, j, 5) * Mathf.Max(1, ry1 - ry0 - bh));
                    if (bw < 5 || !ClearLot(c, bx, by, bx + bw - 1, by + bh - 1)) continue;
                    result.Add(new Highrise
                    {
                        X = bx, Y = by, W = bw, H = bh, Shape = HighriseShape.Box, Home = true,
                        Height = 5 + (int)(DetailHash(seed, i, j, 6) * 3), Seed = seed + i * 17 + j * 5, Tone = DetailHash(seed, i, j, 7)
                    });
                }
                else
                {
                    if (DetailHash(seed, i, j, 8) < 0.1f) continue;
                    int side = Mathf.Min(Mathf.RoundToInt(span) - 2, 7 + (int)(DetailHash(seed, i, j, 4) * 2));
                    int bx = Mathf.RoundToInt(x0 + 1 + i * span + (span - side) / 2f), by = ry0 + (int)(DetailHash(seed, i, j, 5) * Mathf.Max(1, ry1 - ry0 - side + 1));
                    if (side < 5 || !ClearLot(c, bx, by, bx + side - 1, by + side - 1)) continue;
                    result.Add(new Highrise
                    {
                        X = bx, Y = by, W = side, H = side, Shape = DetailHash(seed, i, j, 9) < 0.5f ? HighriseShape.Cross : HighriseShape.Box,
                        Home = true, Height = 7 + (int)(DetailHash(seed, i, j, 6) * 5), Seed = seed + i * 17 + j * 5, Tone = DetailHash(seed, i, j, 7)
                    });
                }
            }
        }
        return result;
    }

    #endregion

    #region 高楼大厦

    // 地块一刀刀切成几块楼地(至多切三刀)
    private static void CutLots(int x0, int y0, int x1, int y1, long seed, int depth, List<(int, int, int, int)> result, int min)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        float r = DetailHash(seed, depth, x0, y0);
        if (depth >= 3 || w < min * 2 && h < min * 2 || depth >= 1 && r < 0.25f)
        {
            result.Add((x0, y0, x1, y1));
            return;
        }
        bool alongX = w >= min * 2 && (h < min * 2 || w > h || w == h && r < 0.5f);
        int cut = Mathf.RoundToInt((alongX ? w : h) * (0.38f + 0.24f * DetailHash(seed, depth, 9)));
        unchecked
        {
            if (alongX)
            {
                CutLots(x0, y0, x0 + cut - 1, y1, seed * 3 + 1, depth + 1, result, min);
                CutLots(x0 + cut, y0, x1, y1, seed * 3 + 2, depth + 1, result, min);
            }
            else
            {
                CutLots(x0, y0, x1, y0 + cut - 1, seed * 3 + 1, depth + 1, result, min);
                CutLots(x0, y0 + cut, x1, y1, seed * 3 + 2, depth + 1, result, min);
            }
        }
    }

    // 楼地里有干道、地标、铁路占着：顺着挡路最多的那一列(行)切开，绕过去
    private static void FitLots(Canvas c, int x0, int y0, int x1, int y1, List<(int, int, int, int)> result, int depth = 0)
    {
        if (x1 - x0 < 3 || y1 - y0 < 3) return;
        int[] cols = new int[x1 - x0 + 1], rows = new int[y1 - y0 + 1];
        bool any = false;
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            if (c.Kind(px, py) == KLot) continue;
            cols[px - x0]++;
            rows[py - y0]++;
            any = true;
        }
        if (!any)
        {
            result.Add((x0, y0, x1, y1));
            return;
        }
        if (depth > 5) return;
        int colMax = 0, rowMax = 0;
        foreach (int v in cols) colMax = Mathf.Max(colMax, v);
        foreach (int v in rows) rowMax = Mathf.Max(rowMax, v);
        bool byColumn = colMax / (float)rows.Length >= rowMax / (float)cols.Length;
        int[] line = byColumn ? cols : rows;
        int most = byColumn ? colMax : rowMax, from = 0;
        while (line[from] != most) from++;
        int to = from;
        while (from > 0 && line[from - 1] > 0) from--;
        while (to < line.Length - 1 && line[to + 1] > 0) to++;
        if (byColumn)
        {
            FitLots(c, x0, y0, x0 + from - 2, y1, result, depth + 1);
            FitLots(c, x0 + to + 2, y0, x1, y1, result, depth + 1);
        }
        else
        {
            FitLots(c, x0, y0, x1, y0 + from - 2, result, depth + 1);
            FitLots(c, x0, y0 + to + 2, x1, y1, result, depth + 1);
        }
    }

    // 写字楼区：铺地广场，一块块楼地各起一栋(裙楼加主楼、方楼、退台楼、圆楼、L 形楼)，偶尔留一块小广场(树、喷泉)
    private static List<Highrise> LayoutTowers(Canvas c, int x0, int y0, int x1, int y1, long seed, float level)
    {
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
            OnLot(c, px, py, px % 4 == 0 || py % 4 == 0 ? PavingLine : Paving, KLot);
        var cut = new List<(int, int, int, int)>();
        CutLots(x0, y0, x1, y1, seed, 0, cut, 8);
        var lots = new List<(int, int, int, int)>();
        foreach ((int a, int b, int a1, int b1) in cut) FitLots(c, a + 1, b + 1, a1 - 1, b1 - 1, lots);
        var result = new List<Highrise>();
        for (int i = 0; i < lots.Count; i++)
        {
            (int bx0, int by0, int bx1, int by1) = lots[i];
            float r = DetailHash(seed, i, 1);
            int bw = bx1 - bx0 + 1, bh = by1 - by0 + 1;
            if (r < 0.08f && lots.Count > 3 && bw >= 5 && bh >= 5)
            {
                int fx = (bx0 + bx1) / 2, fy = (by0 + by1) / 2;
                for (int py = by0; py <= by1; py++)
                for (int px = bx0; px <= bx1; px++)
                    if ((px == bx0 || px == bx1 || py == by0 || py == by1) && DetailHash(seed, px, py, 32) < 0.5f)
                        OnLot(c, px, py, DetailHash(seed, px, py, 33) < 0.5f ? Rgb(74, 128, 70) : TreeDark, KLot);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    OnLot(c, fx + dx, fy + dy, dx != 0 || dy != 0 ? Rgb(180, 210, 236) : Rgb(236, 244, 250), KLot);
                continue;
            }
            if (bw >= 9 && bh >= 9 && r < 0.45f)
            {
                // 裙楼铺满楼地，主楼坐在裙楼上
                const int inset = 2;
                int tw = bw - inset * 2 - (int)(DetailHash(seed, i, 2) * 3), th = bh - inset * 2 - (int)(DetailHash(seed, i, 3) * 3);
                HighriseShape shape = r < 0.2f && Mathf.Abs(tw - th) <= 3 ? HighriseShape.Round
                    : r < 0.3f && tw >= 7 && th >= 7 ? HighriseShape.Stepped : HighriseShape.Box;
                result.Add(NewTower(bx0 + inset, by0 + inset, tw, th, inset, shape, seed + i * 31, level));
            }
            else
            {
                HighriseShape shape = r < 0.6f && bw >= 9 && bh >= 9 ? HighriseShape.Stepped
                    : r < 0.72f && bw >= 7 && bh >= 7 ? HighriseShape.L
                    : r < 0.8f && Mathf.Abs(bw - bh) <= 2 && bw >= 5 ? HighriseShape.Round : HighriseShape.Box;
                result.Add(NewTower(bx0, by0, bw, bh, 0, shape, seed + i * 31, level * (bw * bh < 40 ? 0.55f : 1f)));
            }
        }
        return result;
    }

    private static Highrise NewTower(int x, int y, int w, int h, int pad, HighriseShape shape, long seed, float level) => new()
    {
        X = x, Y = y, W = w, H = h, Pad = pad, Shape = shape, Cut = (int)(DetailHash(seed, 9) * 4), Glass = DetailHash(seed, 7) < 0.6f,
        Tone = DetailHash(seed, 11), Seed = seed,
        Height = Mathf.Min(24, Mathf.Max(2, Mathf.RoundToInt((4f + level * 15f) * (0.5f + 0.7f * DetailHash(seed, 10)) * (w * h >= 60 ? 1.15f : 1f))))
    };

    // 这一像素在楼身上
    private static bool TowerCovers(Highrise b, int x, int y)
    {
        int lx = x - b.X, ly = y - b.Y;
        if (lx < 0 || ly < 0 || lx >= b.W || ly >= b.H) return false;
        switch (b.Shape)
        {
            case HighriseShape.Round:
            {
                float rx = b.W / 2f, ry = b.H / 2f, u = (lx + 0.5f - rx) / rx, v = (ly + 0.5f - ry) / ry;
                return u * u + v * v <= 1f;
            }
            case HighriseShape.L:
            {
                bool right = lx >= b.W / 2f, low = ly >= b.H / 2f;
                return !(right == ((b.Cut & 1) != 0) && low == ((b.Cut & 2) != 0));
            }
            case HighriseShape.Cross:
            {
                int t = b.W / 3;
                return lx >= t && lx < b.W - t || ly >= t && ly < b.H - t;
            }
        }
        return true;
    }

    private static void PaintTower(Canvas c, Highrise b)
    {
        bool Own(int px, int py) => c.Kind(px, py) is KLot or KBuilding or KAlley;
        if (b.Pad > 0)
        {
            int x0 = b.X - b.Pad, y0 = b.Y - b.Pad, x1 = b.X + b.W + b.Pad - 1, y1 = b.Y + b.H + b.Pad - 1;
            for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
                if (Own(px, py))
                    c.Put(px, py, px == x0 || py == y1 ? Rgb(226, 224, 220) : px == x1 || py == y0 ? Rgb(160, 158, 154)
                        : (px * 5 + py * 3) % 11 == 0 ? Rgb(150, 150, 156) : Rgb(198, 196, 192), KBuilding);
        }
        Color32 tone = Pick(b.Home ? HomeTones : b.Glass ? GlassTones : StoneTones, b.Tone);
        for (int py = b.Y; py < b.Y + b.H; py++)
        for (int px = b.X; px < b.X + b.W; px++)
        {
            if (!TowerCovers(b, px, py) || !Own(px, py)) continue;
            int lx = px - b.X, ly = py - b.Y;
            // 画布的行往北增：西沿、北沿受光，东沿、南沿背阴(以前照演示版的行往南增，成了西南受光)
            bool lit = !TowerCovers(b, px - 1, py) || !TowerCovers(b, px, py + 1), shaded = !TowerCovers(b, px + 1, py) || !TowerCovers(b, px, py - 1);
            Color32 color = tone;
            if (lit) color = Darker(tone, 1.22f);
            else if (shaded) color = Darker(tone, 0.68f);
            else if (b.Shape == HighriseShape.Stepped)
            {
                // 退台：一圈圈收进去
                int level = Mathf.Min(Mathf.Min(lx, ly), Mathf.Min(b.W - 1 - lx, b.H - 1 - ly));
                color = level == 2 || level == 4 ? Darker(tone, 0.8f) : level > 4 ? Darker(tone, 1.12f) : tone;
            }
            else if (b.Glass)
            {
                // 玻璃幕墙：竖框格子，斜着一道反光
                color = lx % 2 == 0 && ly % 2 == 0 ? Darker(tone, 0.84f) : tone;
                if ((lx + ly + (int)(b.Seed & 7)) % 9 < 2) color = Lerp(color, Rgb(236, 244, 255), 0.35f);
            }
            c.Put(px, py, color, KBuilding);
        }
        // 楼顶：最高那栋停机坪；住宅楼梯间；玻璃楼正中机房；石材楼空调机组
        int mx = b.X + b.W / 2, my = b.Y + b.H / 2;
        if (b.Heli)
        {
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (dx * dx + dy * dy <= 5 && TowerCovers(b, mx + dx, my + dy)) c.Put(mx + dx, my + dy, Rgb(70, 74, 80), KBuilding);
            foreach ((int dx, int dy) in new[] { (-1, -1), (-1, 0), (-1, 1), (0, 0), (1, -1), (1, 0), (1, 1) })
                if (TowerCovers(b, mx + dx, my + dy)) c.Put(mx + dx, my + dy, White, KBuilding);
        }
        else if (b.Home)
        {
            int n = b.W > b.H * 1.5f ? 2 : 1;
            for (int k = 0; k < n; k++)
            {
                int x = n == 1 ? mx : b.X + Mathf.RoundToInt(b.W * (k + 1) / 3f);
                if (!TowerCovers(b, x, my) || !TowerCovers(b, x + 1, my)) continue;
                c.Put(x, my, Darker(tone, 0.62f), KBuilding);
                c.Put(x + 1, my, Darker(tone, 0.72f), KBuilding);
            }
        }
        else if (b.Glass)
        {
            if (b.Shape != HighriseShape.Stepped)
                for (int dy = -1; dy <= 0; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (TowerCovers(b, mx + dx, my + dy)) c.Put(mx + dx, my + dy, dy != 0 ? Rgb(150, 152, 158) : Rgb(120, 122, 128), KBuilding);
        }
        else
            for (int k = 0; k < 3; k++)
            {
                int x = b.X + 2 + (int)(DetailHash(b.Seed, k, 1) * (b.W - 4)), y = b.Y + 2 + (int)(DetailHash(b.Seed, k, 2) * (b.H - 4));
                if (!TowerCovers(b, x, y) || !TowerCovers(b, x + 1, y)) continue;
                c.Put(x, y, Rgb(130, 132, 138), KBuilding);
                c.Put(x + 1, y, Rgb(104, 106, 112), KBuilding);
            }
    }

    // 落影：太阳在西北，影子往东南拖，长短照楼高；影子压在地面、街道、矮房和更矮的楼顶上
    private static void CastShadows(Canvas c, List<Highrise> list)
    {
        if (list.Count == 0) return;
        var top = new byte[c.PW * c.PH];
        var shade = new bool[c.PW * c.PH];
        var parts = new List<(Highrise b, int height)>();
        foreach (Highrise b in list)
        {
            if (b.Pad > 0) parts.Add((new Highrise { X = b.X - b.Pad, Y = b.Y - b.Pad, W = b.W + b.Pad * 2, H = b.H + b.Pad * 2 }, 2));
            parts.Add((b, b.Height));
        }
        foreach ((Highrise b, int height) in parts)
            for (int py = b.Y; py < b.Y + b.H; py++)
            for (int px = b.X; px < b.X + b.W; px++)
                if (c.In(px, py) && TowerCovers(b, px, py)) top[py * c.PW + px] = (byte)Mathf.Max(top[py * c.PW + px], height);
        foreach ((Highrise b, int height) in parts)
            for (int py = b.Y; py < b.Y + b.H; py++)
            for (int px = b.X; px < b.X + b.W; px++)
            {
                if (!TowerCovers(b, px, py)) continue;
                for (int d = 1; d <= height; d++)
                {
                    // 影子往东南拖(画布的行往北增，往南是减)
                    int x = px + d, y = py - Mathf.RoundToInt(d * 0.7f);
                    if (c.In(x, y) && top[y * c.PW + x] < height - d) shade[y * c.PW + x] = true;
                }
            }
        for (int i = 0; i < shade.Length; i++)
            if (shade[i] && c.Kinds[i] != KOff)
                c.Put(i % c.PW, i / c.PW, Rgba(18, 24, 48, top[i] > 0 ? 84 : 112), c.Kinds[i]);
    }

    #endregion
}
