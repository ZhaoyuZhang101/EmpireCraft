using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市精细层：华夏都城宫城(用户 2026-10-16 "先做宫城的演示给我看"，演示版定样子后移植)。
// 都城城心那块地(8×8 格，大城连北面一块成 8×16 格)以前照一格一像素的粗图放大(红墙、黄色御道、一格格的殿)，现在画精细版：
//   · 宫墙外沿黄瓦顶、里侧朱墙，四角角楼；北门、东西两侧门(正对城里的东西大街)；南门小宫城一座门楼，
//     大宫城是凹字形门楼(正楼重檐，两翼向北伸出，翼端方亭)；
//   · 中轴从南到北：金水河与石桥、前朝正门(大宫城)、工字形汉白玉台基上的前朝大殿、后寝正门、后寝两宫夹一座小殿、御花园；
//   · 两侧是宫院(红墙围着，北边正殿，南边小院)与长廊；宫城左边空出的几像素是宫墙外的街和一行树
//     (城心那块地的中轴在第 4 格，宫城左右对称，左边比右边多出四像素)；
//   · 庑殿顶：太阳在西北，北坡、西头亮，南坡、东头暗，正脊一道亮线；重檐的殿外圈多一道下檐；殿影落在东南的铺地上。
// 下面的 y 是从宫城北沿往南数的行(与演示版一致)，落到画布上翻过来(画布的行往北增)。其余文化系的都城地标见 PureMapPatch.Landmark。
public partial class PureMapPatch
{
    private static readonly Color32 PalaceRed = Rgb(176, 62, 46), PalaceCoping = Rgb(212, 164, 74), PalaceRoof = Rgb(226, 176, 64),
        PalaceGreen = Rgb(92, 148, 108), PalaceMarble = Rgb(238, 234, 224), PalaceRail = Rgb(200, 194, 184),
        PalacePaving = Rgb(188, 180, 168), PalacePath = Rgb(226, 220, 206), PalaceGarden = Rgb(124, 164, 100),
        PalaceTree = Rgb(64, 108, 62), PalaceTreeLit = Rgb(96, 144, 82), PalaceRock = Rgb(154, 150, 144),
        PalaceWater = Rgb(118, 170, 222), PalaceWaterLit = Rgb(170, 206, 240), PalaceDoor = Rgb(74, 42, 36),
        PalaceStreet = Rgb(246, 244, 240);

    // 这座城的都城地标画精细版宫城吗(先做了华夏)
    private static bool DetailedPalace(CityPlan plan) => plan.family == "huaxia" && plan.landmark.Count > 0 && !plan.papal;

    private sealed class PalaceBrush
    {
        public Canvas C;
        public int A0, Top, H2;
        public Color32 Tint;
        public long Seed;

        // 绝对像素 x、从北沿数的行 y
        public void Put(int x, int y, Color32 color, byte kind = KFixed) => C.Put(x, Top - y, Lerp(Tint, color, 0.93f), kind);
        public int R(int r) => A0 + 2 + r;
        public int L(int r) => A0 + 1 - r;

        public void Span(int r0, int r1, int y0, int y1, Color32 color, byte kind = KFixed)
        {
            for (int y = y0; y <= y1; y++)
            for (int r = r0; r <= r1; r++)
            {
                Put(R(r), y, color, kind);
                Put(L(r), y, color, kind);
            }
        }

        public void Box(int x0, int y0, int x1, int y1, Color32 color, byte kind = KFixed)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Put(x, y, color, kind);
        }

        // 左右对称各画一份：右边 r0..r1，左边镜像
        public void Mirror(int r0, int r1, global::System.Action<int, int> draw)
        {
            draw(R(r0), R(r1));
            draw(L(r1), L(r0));
        }

        // 影子：东南一侧落在铺地、台基、园子上
        private void Shade(int x0, int y0, int x1, int y1)
        {
            void Dim(int x, int y)
            {
                if (y >= H2 || C.Kind(x, Top - y) != KPlaza) return;
                C.Put(x, Top - y, new Color32(40, 28, 20, 56), KPlaza);
            }
            for (int x = x0 + 1; x <= x1 + 1; x++) Dim(x, y1 + 1);
            for (int y = y0 + 1; y <= y1; y++) Dim(x1 + 1, y);
        }

        // 庑殿顶
        public void Hip(int x0, int y0, int x1, int y1, Color32 color, bool cast = true)
        {
            bool horizontal = x1 - x0 >= y1 - y0;
            int half = (horizontal ? y1 - y0 : x1 - x0) / 2;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int t = y - y0, b = y1 - y, l = x - x0, r = x1 - x;
                int a0 = horizontal ? t : l, a1 = horizontal ? b : r, e0 = horizontal ? l : t, e1 = horizontal ? r : b;
                int across = Mathf.Min(a0, a1), along = Mathf.Min(e0, e1);
                float k;
                if (along < across) k = e0 <= e1 ? horizontal ? 1f : 1.12f : horizontal ? 0.74f : 0.82f;
                else if (a0 == half) k = 1.28f;
                else k = a0 < a1 ? horizontal ? 1.12f : 1f : horizontal ? 0.82f : 0.74f;
                if (t == 0 || b == 0 || l == 0 || r == 0) k *= 0.86f;
                Put(x, y, Darker(color, k));
            }
            if (cast) Shade(x0, y0, x1, y1);
        }

        // 重檐：外圈一道下檐，里面再一层
        public void Hip2(int x0, int y0, int x1, int y1, Color32 color)
        {
            Hip(x0, y0, x1, y1, Darker(color, 0.9f));
            Hip(x0 + 1, y0 + 1, x1 - 1, y1 - 1, color, false);
        }

        // 汉白玉台基：一圈栏杆(隔一像素一根望柱)
        public void Terrace(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                bool rim = x == x0 || x == x1 || y == y0 || y == y1;
                Put(x, y, rim && (x + y) % 2 == 0 ? PalaceRail : PalaceMarble, KPlaza);
            }
        }

        // 宫院：一圈红墙，北边正殿，南墙正中开门
        public void Court(int x0, int y0, int x1, int y1, Color32 roof, long seed)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                bool rim = x == x0 || x == x1 || y == y0 || y == y1;
                Put(x, y, rim ? PalaceRed : PalacePaving, rim ? KFixed : KPlaza);
            }
            Put((x0 + x1) / 2, y1, PalacePaving, KPlaza);
            int hall = Mathf.Max(1, Mathf.CeilToInt((y1 - y0 - 1) * 0.5f));
            Hip(x0 + 1, y0 + 1, x1 - 1, y0 + hall, roof);
            if (x1 - x0 >= 6 && y1 - y0 >= 6 && DetailHash(seed, 3) < 0.7f)
            {
                Hip(x0 + 1, y0 + hall + 2, x0 + 2, y1 - 1, Darker(roof, 0.96f));
                Hip(x1 - 2, y0 + hall + 2, x1 - 1, y1 - 1, Darker(roof, 0.96f));
            }
        }

        // 树：一像素暗、偶尔带亮面
        public void Tree(int x, int y)
        {
            Put(x, y, PalaceTree);
            if (DetailHash(Seed, x, y, 5) < 0.5f) Put(x + 1, y, PalaceTreeLit);
        }
    }

    private static void DrawPalace(Canvas c, CityPlan plan, Color32 tint, long seed)
    {
        int size = Config.CITY_ZONE_SIZE, s = DetailScale;
        int zx0 = int.MaxValue, zy0 = int.MaxValue, zy1 = int.MinValue;
        foreach (TileZone zone in c.City.zones)
        {
            if (zone == null || !plan.landmark.Contains(zone.id)) continue;
            zx0 = Mathf.Min(zx0, zone.x);
            zy0 = Mathf.Min(zy0, zone.y);
            zy1 = Mathf.Max(zy1, zone.y);
        }
        if (zx0 == int.MaxValue) return;
        // 宫城以所在地块第 4 格为中轴(左右对称，见上)：自动规划时宫城在城心、中轴正是这一列；
        // 手动挪了宫城或中轴(见 ApplyManual)，中轴不一定从这里过，宫城照样画在自己那块地正中，不跟着中轴挪出地块
        int axisX = zx0 * size + 4;
        int x0 = (zx0 * size - c.X0) * s, bottom = (zy0 * size - c.Y0) * s, top = ((zy1 * size + size - 1) - c.Y0) * s + s - 1;
        var p = new PalaceBrush { C = c, A0 = (axisX - c.X0) * s, Top = top, H2 = top - bottom + 1, Tint = tint, Seed = seed };
        int h = p.H2;
        bool big = h >= 64;
        // 标注落点：宫城正中
        c.Labels.Add(new DetailLabel
        {
            Tile = c.TileAt(p.A0 + 1.5f, top - h / 2f), Icon = "palace", Key = zx0 * 7919L + zy0, Weight = h
        });

        // ① 宫城左边空出来的那几像素(城心那块地比宫城宽)：宫墙外一条街，一行树
        for (int y = 0; y < h; y++)
        for (int x = x0; x < p.L(13); x++)
            p.Put(x, y, x == x0 + 1 && y % 3 == 1 ? PalaceTree : PalaceStreet, KPlaza);
        // ② 整片铺地，③ 中轴御路
        p.Span(0, 13, 0, h - 1, PalacePaving, KPlaza);
        p.Span(0, 1, 0, h - 1, PalacePath, KPlaza);
        int g0 = 2, g1 = big ? 11 : 6, innerWall = big ? 12 : 7, r0 = big ? 13 : 8, r1 = big ? 26 : 12, gateIn = big ? 28 : 14,
            o0 = big ? 30 : 15, o1 = big ? 47 : 26, gateOuter = big ? 49 : -1, river = big ? 51 : 27;

        // ④ 御花园：草地、树、假山、水池，正中一座殿(大宫城)或一座亭
        p.Span(2, 11, g0, g1, PalaceGarden, KPlaza);
        p.Span(0, 1, g0, g1, PalacePath, KPlaza);
        for (int y = g0; y <= g1; y++)
        for (int r = 3; r <= 11; r++)
        {
            if (DetailHash(seed, r, y, 31) < 0.3f) p.Tree(p.R(r), y);
            if (DetailHash(seed, r, y, 32) < 0.3f) p.Tree(p.L(r), y);
        }
        p.Box(p.R(7), g0 + 1, p.R(9), g0 + 2, PalaceWater, KPlaza);
        p.Put(p.R(7), g0 + 1, PalaceWaterLit, KPlaza);
        p.Box(p.L(9), g0 + 1, p.L(8), g0 + 2, PalaceRock);
        p.Put(p.L(9), g0 + 1, Darker(PalaceRock, 1.2f));
        if (big)
        {
            p.Hip(p.L(3), g0 + 2, p.R(3), g0 + 5, PalaceRoof);
            p.Mirror(6, 7, (a, b) => p.Hip(a, g1 - 2, b, g1 - 1, PalaceGreen));
        }
        else p.Hip(p.L(1), g0 + 1, p.R(1), g0 + 3, PalaceGreen);

        // ⑤ 后寝：一道宫墙隔开，中轴两宫夹一座小殿立在台基上(小宫城一座殿)，两侧宫院
        p.Span(2, 11, innerWall, innerWall, PalaceRed);
        p.Terrace(p.L(5), r0, p.R(5), r1);
        if (big)
        {
            p.Span(0, 1, r0, r1, PalacePath, KPlaza);
            p.Hip2(p.L(4), r0 + 1, p.R(4), r0 + 5, PalaceRoof);
            p.Hip(p.L(1), r0 + 7, p.R(1), r0 + 8, PalaceRoof);
            p.Hip(p.L(4), r0 + 10, p.R(4), r0 + 12, PalaceRoof);
            for (int i = 0; i < 2; i++)
            {
                int top0 = r0 + i * 7;
                long courtSeed = seed + i;
                p.Mirror(7, 11, (a, b) => p.Court(a, top0, b, top0 + 6, PalaceRoof, courtSeed));
            }
        }
        else
        {
            p.Hip(p.L(4), r0 + 1, p.R(4), r0 + 3, PalaceRoof);
            p.Mirror(7, 11, (a, b) => p.Court(a, r0, b, r1, PalaceRoof, seed));
        }

        // ⑥ 后寝正门：门殿横在一道宫墙上
        p.Span(2, 11, gateIn, gateIn, PalaceRed);
        p.Hip(p.L(2), gateIn - 1, p.R(2), gateIn + 1, PalaceRoof);

        // ⑦ 前朝：工字形汉白玉台基上的大殿(大宫城三殿)；两侧长廊
        p.Mirror(10, 11, (a, b) => p.Hip(a, o0, b, o1, Darker(PalaceRoof, 0.94f)));
        if (big)
        {
            p.Terrace(p.L(7), o0 + 1, p.R(7), o0 + 7);
            p.Terrace(p.L(5), o0 + 8, p.R(5), o0 + 10);
            p.Terrace(p.L(7), o0 + 11, p.R(7), o1 - 1);
            p.Span(0, 1, o1 - 1, o1, PalacePath, KPlaza);
            p.Hip2(p.L(6), o0 + 2, p.R(6), o0 + 6, PalaceRoof);
            p.Hip(p.L(2), o0 + 8, p.R(2), o0 + 10, PalaceRoof);
            p.Hip2(p.L(5), o0 + 12, p.R(5), o0 + 15, PalaceRoof);
        }
        else
        {
            p.Terrace(p.L(7), o0 + 1, p.R(7), o1 - 1);
            p.Span(0, 1, o1 - 1, o1, PalacePath, KPlaza);
            p.Hip2(p.L(5), o0 + 2, p.R(5), o0 + 6, PalaceRoof);
            p.Hip(p.L(2), o0 + 8, p.R(2), o0 + 9, PalaceRoof);
        }

        // ⑧ 前朝正门(大宫城)：宫墙一道，门殿重檐
        if (gateOuter > 0)
        {
            p.Span(2, 11, gateOuter, gateOuter, PalaceRed);
            p.Hip2(p.L(3), gateOuter - 1, p.R(3), gateOuter + 1, PalaceRoof);
        }
        // ⑨ 内金水河：一道弯河，中轴与两侧有桥
        for (int r = 2; r <= 9; r++)
        {
            int y = river + (r <= 3 ? 0 : r <= 7 ? 1 : 0);
            foreach (int x in new[] { p.R(r), p.L(r) }) p.Put(x, y, (x + y) % 3 != 0 ? PalaceWater : PalaceWaterLit, KPlaza);
        }
        for (int r = 0; r <= 1; r++)
            foreach (int x in new[] { p.R(r), p.L(r) })
            {
                p.Put(x, river, PalaceMarble, KPlaza);
                p.Put(x, river + 1, PalaceMarble, KPlaza);
            }
        if (big)
            foreach (int x in new[] { p.R(3), p.L(3) }) p.Put(x, river, PalaceMarble, KPlaza);

        // ⑩ 宫墙：外沿黄瓦顶，里侧朱墙
        for (int y = 0; y < h; y++)
        {
            p.Put(p.R(13), y, PalaceCoping);
            p.Put(p.L(13), y, PalaceCoping);
            p.Put(p.R(12), y, PalaceRed);
            p.Put(p.L(12), y, PalaceRed);
        }
        p.Span(0, 13, 0, 0, PalaceCoping);
        p.Span(0, 12, 1, 1, PalaceRed);
        p.Span(0, 13, h - 1, h - 1, PalaceCoping);
        p.Span(0, 12, h - 2, h - 2, PalaceRed);
        // ⑪ 四角角楼
        foreach ((int cx, int cy) in new[] { (p.R(11), 0), (p.L(13), 0), (p.R(11), h - 3), (p.L(13), h - 3) })
        {
            p.Hip(cx, cy, cx + 2, cy + 2, PalaceRoof, false);
            p.Put(cx + 1, cy + 1, Darker(PalaceRoof, 1.3f));
        }
        // ⑫ 北门；东西两侧门正对城里的东西大街
        p.Hip(p.L(2), 0, p.R(2), 2, PalaceRoof, false);
        if (plan.gateY != int.MinValue)
        {
            int ew = top - ((plan.gateY - c.Y0) * s + s - 1);
            if (ew >= 4 && ew + 3 < h - 3) p.Mirror(11, 13, (a, b) => p.Hip(a, ew, b, ew + 3, PalaceRoof, false));
        }
        // ⑬ 南门：大宫城是凹字形门楼(正楼重檐，两翼向北伸出，翼端方亭)；小宫城一座门楼
        if (big)
        {
            p.Hip2(p.L(4), h - 5, p.R(4), h - 2, PalaceRoof);
            p.Mirror(6, 8, (a, b) =>
            {
                p.Hip(a, h - 9, b, h - 2, PalaceRoof);
                p.Hip2(a, h - 11, b, h - 9, PalaceRoof);
            });
        }
        else p.Hip2(p.L(3), h - 4, p.R(3), h - 1, PalaceRoof);
        for (int r = 0; r <= 1; r++)
        {
            p.Put(p.R(r), h - 1, PalaceDoor);
            p.Put(p.L(r), h - 1, PalaceDoor);
        }
    }
}
