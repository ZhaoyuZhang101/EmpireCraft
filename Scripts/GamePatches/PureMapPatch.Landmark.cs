using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 城市精细层：华夏以外各文化系的都城地标(用户 2026-10-21 转玩家反馈城心的文化地标"把城市群给遮住了"，选定"画精细版、占地缩小")。
// 以前城心那块地(8×8 格，大城连北面一块成 8×16 格)整块照一格一像素的粗图放大，一片粗马赛克压在精细的街坊中间；
// 现在只占城心正中 5×5 格(大城 7×9 格，以城门两条中线的交口为中)，画精细版，那块地其余的格照常铺大街、城墙、街坊。远景粗图不变。
//   · 西欧城堡：一圈幕墙(雉堞)、四角圆塔，南门两座门楼塔；北边主堡(雉堞围着石板顶，东北角一座角楼)，西侧大厅、东侧礼拜堂；
//   · 日本天守：一圈护城河(有街通过来的那几面架木桥)，石垣、白墙，碎石铺的本丸，两重檐的天守(脊端金鯱)，四角橹，园里松树；
//   · 罗马广场：大理石铺地，东西两侧柱廊，北端神庙(前排柱子、台阶朝南)，南口一座凯旋门，广场上一座纪念柱，大城西南加一座会堂；
//   · 维京王者大厅：一圈木栅，踩实的场院，正中偏北一座船形长屋大殿(脊端金龙头)，南边两座小长屋，场院当中火塘、两块符文石；
//   · 阿拉伯大清真寺：院墙，北边礼拜殿(平顶上一排排小穹顶，正中大穹顶)，南边庭院(三面回廊，当中水池)，东南角宣礼塔(大城两座)；
//   · 波斯百柱殿：北边台基上一座柱网方殿(四角塔楼，南面大台阶)，南边四分园(十字水渠、当中水池)；
//   · 印度门塔神庙：院墙，南面门塔(脊上一排金顶)，正中层层收进的塔殿(金顶)，前面柱厅，东北角阶梯水池，大城北面再一座门塔；
//   · 埃及金字塔：在城外那块地，占 6×6 格，北面最亮、西面次之，东、南两面背阴，塔尖金顶，四周沙地；
//   · 阿兹特克大神庙：蛇墙围着的广场，偏东一座阶梯金字塔(西面并排两道台阶)，顶上北蓝(雨神)南红(战神)两座神殿，西边圆形风神庙；
//   · 教廷驻地大教堂(不论文化系，见 PapalCathedral)：北面拉丁十字大殿，南边柱廊环抱的椭圆广场。
// 下面的 y 是从地标北沿往南数的行(同宫城)，落到画布上翻过来。营地(游牧、林地)另画(见 DrawCamp)，不走这里
public partial class PureMapPatch
{
    private struct LandmarkArea
    {
        public int X0, Y0, X1, Y1;
        public bool Contains(WorldTile tile) => tile.x >= X0 && tile.x <= X1 && tile.y >= Y0 && tile.y <= Y1;
    }

    private static bool DetailedLandmark(string family) =>
        family is "japan" or "western" or "roma" or "viking" or "arab" or "persia" or "india" or "egypt" or "aztec";

    // 地标精细版的占地(世界格，含两端)
    private static bool LandmarkSite(CityPlan plan, out LandmarkArea area)
    {
        area = default;
        if (plan.landmark.Count == 0 || !(plan.papal || DetailedLandmark(plan.family)) || plan.city?.zones == null) return false;
        int size = Config.CITY_ZONE_SIZE;
        int zx = int.MaxValue, zy0 = int.MaxValue, zy1 = int.MinValue;
        foreach (TileZone zone in plan.city.zones)
        {
            if (zone == null || !plan.landmark.Contains(zone.id)) continue;
            zx = Mathf.Min(zx, zone.x);
            zy0 = Mathf.Min(zy0, zone.y);
            zy1 = Mathf.Max(zy1, zone.y);
        }
        if (zx == int.MaxValue) return false;
        int cx = zx * size + 4, cy = zy0 * size + 4;
        if (plan.family == "egypt" && !plan.papal) area = new LandmarkArea { X0 = cx - 3, Y0 = cy - 3, X1 = cx + 2, Y1 = cy + 2 };
        else if (zy1 > zy0) area = new LandmarkArea { X0 = cx - 3, Y0 = cy - 1, X1 = cx + 3, Y1 = cy + 7 };
        else area = new LandmarkArea { X0 = cx - 2, Y0 = cy - 2, X1 = cx + 2, Y1 = cy + 2 };
        return true;
    }

    private sealed class LandmarkBrush
    {
        public Canvas C;
        public int L, Top, W, H;
        // 东西两面城门大道那一行(从北沿数，占 4 像素)；大道不经过占地为 -1
        public int GateRow;
        public Color32 Tint;
        public long Seed;

        // 南北两面城门大道那一列(占 4 像素)：占地以大道为中
        public int Gx => W / 2 - 2;

        public void Put(int x, int y, Color32 color, byte kind = KFixed)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            C.Put(L + x, Top - y, Lerp(Tint, color, 0.93f), kind);
        }

        public byte Kind(int x, int y) => C.Kind(L + x, Top - y);

        public void Box(int x0, int y0, int x1, int y1, Color32 color, byte kind = KFixed)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Put(x, y, color, kind);
        }

        public void Frame(int x0, int y0, int x1, int y1, Color32 color, byte kind = KFixed)
        {
            for (int x = x0; x <= x1; x++)
            {
                Put(x, y0, color, kind);
                Put(x, y1, color, kind);
            }
            for (int y = y0 + 1; y < y1; y++)
            {
                Put(x0, y, color, kind);
                Put(x1, y, color, kind);
            }
        }

        // 某一面城门处的一格：d 从外沿往里数，i 沿外沿数(0~3 是门洞)；0 北 1 南 2 西 3 东
        public void Side(int side, int d, int i, Color32 color, byte kind = KFixed)
        {
            switch (side)
            {
                case 0: Put(Gx + i, d, color, kind); break;
                case 1: Put(Gx + i, H - 1 - d, color, kind); break;
                case 2: Put(d, GateRow + i, color, kind); break;
                default: Put(W - 1 - d, GateRow + i, color, kind); break;
            }
        }

        // 这一面有街通过来就开门(南面总开)
        public bool Open(int side)
        {
            if (side == 1) return true;
            if (side >= 2 && GateRow < 0) return false;
            int px = side switch { 0 => L + W / 2, 2 => L - 1, _ => L + W };
            int py = side == 0 ? Top + 1 : Top - GateRow - 1;
            return C.Kind(px, py) is KAvenue or KStreet or KAlley or KPlaza;
        }

        // 门洞：从外沿往里 depth 像素铺成地面
        public void Gate(int side, int depth, Color32 floor)
        {
            for (int d = 0; d < depth; d++)
            for (int i = 0; i < 4; i++)
                Side(side, d, i, floor, KPlaza);
        }

        public void Gates(int depth, Color32 floor)
        {
            for (int side = 0; side < 4; side++)
                if (Open(side)) Gate(side, depth, floor);
        }

        // 影子：东南一侧落在铺地上
        public void Shade(int x0, int y0, int x1, int y1)
        {
            void Dim(int x, int y)
            {
                if (x < 0 || y < 0 || x >= W || y >= H || Kind(x, y) != KPlaza) return;
                C.Put(L + x, Top - y, new Color32(40, 28, 20, 56), KPlaza);
            }
            for (int x = x0 + 1; x <= x1 + 1; x++) Dim(x, y1 + 1);
            for (int y = y0 + 1; y <= y1; y++) Dim(x1 + 1, y);
        }

        // 四坡顶：太阳在西北，北坡、西头亮，南坡、东头暗，正脊一道亮线(同宫城)
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

        // 两坡顶：屋脊顺长边(或按 alongX 定)，一道亮线；北(西)坡亮，南(东)坡暗，两头山墙略暗
        public void Gable(int x0, int y0, int x1, int y1, Color32 color, bool? alongX = null, bool cast = true)
        {
            bool horizontal = alongX ?? x1 - x0 >= y1 - y0;
            float mid = horizontal ? (y0 + y1) / 2f : (x0 + x1) / 2f;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float at = horizontal ? y : x;
                float k = Mathf.Abs(at - mid) < 0.6f ? 1.28f : at < mid ? 1.1f : 0.78f;
                if (horizontal ? x == x0 || x == x1 : y == y0 || y == y1) k *= 0.9f;
                Put(x, y, Darker(color, k));
            }
            if (cast) Shade(x0, y0, x1, y1);
        }

        // 平顶：一圈女儿墙暗一点
        public void Flat(int x0, int y0, int x1, int y1, Color32 color, bool cast = true)
        {
            Color32 low = Darker(color, 0.84f);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Put(x, y, x == x0 || x == x1 || y == y0 || y == y1 ? low : color);
            if (cast) Shade(x0, y0, x1, y1);
        }

        // 穹顶、圆塔：西北受光，边上暗
        public void Dome(float cx, float cy, float r, Color32 color)
        {
            for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
            for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
            {
                float dx = x - cx, dy = y - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r) continue;
                float k = d > r - 0.9f ? 0.78f : dx + dy < -r * 0.25f ? 1.25f : dx + dy > r * 0.6f ? 0.88f : 1f;
                Put(x, y, Darker(color, k));
            }
        }

        // 树：一像素暗、偶尔带亮面
        public void Tree(int x, int y)
        {
            Put(x, y, PalaceTree);
            if (DetailHash(Seed, x, y, 5) < 0.5f) Put(x + 1, y, PalaceTreeLit);
        }

        // 铺地上随手点几处(草、杂色)：只点还是铺地的像素
        public void Speckle(int x0, int y0, int x1, int y1, float chance, Color32 color, int salt)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Kind(x, y) == KPlaza && DetailHash(Seed, x, y, salt) < chance) Put(x, y, color, KPlaza);
        }
    }

    private static void DrawLandmark(Canvas c, CityPlan plan, DetailStyle style, Color32 tint, long seed, LandmarkArea site)
    {
        int s = DetailScale;
        var p = new LandmarkBrush
        {
            C = c, L = (site.X0 - c.X0) * s, Top = (site.Y1 - c.Y0) * s + s - 1,
            W = (site.X1 - site.X0 + 1) * s, H = (site.Y1 - site.Y0 + 1) * s, Tint = tint, Seed = seed,
            GateRow = plan.gateX != int.MinValue && plan.gateY >= site.Y0 && plan.gateY <= site.Y1 ? (site.Y1 - plan.gateY) * s : -1
        };
        // 街区级标注(名字按各文化系的词库，见 UrbanLabelNaming)：宫室叫宫，庙宇叫庙；金字塔是陵墓，不标
        string icon = null;
        if (plan.papal)
        {
            PapalCathedral(p);
            icon = "temple";
        }
        else
        switch (plan.family)
        {
            case "western": WesternCastle(p); icon = "palace"; break;
            case "japan": JapanCastle(p); icon = "palace"; break;
            case "roma": RomanForum(p); icon = "temple"; break;
            case "viking": VikingHall(p); icon = "palace"; break;
            case "arab": GreatMosque(p, style); icon = "temple"; break;
            case "persia": Apadana(p); icon = "palace"; break;
            case "india": GopuramTemple(p); icon = "temple"; break;
            case "egypt": GreatPyramid(p); break;
            case "aztec": TemploMayor(p); icon = "temple"; break;
        }
        if (icon != null)
            c.Labels.Add(new DetailLabel
            {
                Tile = c.TileAt(p.L + p.W / 2f, p.Top - p.H / 2f), Icon = icon, Key = site.X0 * 7919L + site.Y0, Weight = p.W * p.H
            });
    }

    // 教廷驻地大教堂(用户 10-29 "给教堂做一个专属建筑"，选"教廷驻地大教堂")，不论文化系都画这个：
    //   · 北面拉丁十字大殿：正殿南北向(铅灰两坡顶)，北端半圆后殿，东西伸出横厅，交叉处一座大穹顶(铜绿，顶上金色采光亭)；
    //   · 南立面浅色石墙、正中大门，两侧各一座钟楼(四坡顶、金色尖)，门前一道台阶；
    //   · 南边椭圆广场：两道柱廊像双臂环抱(外圈石顶、内圈一根根柱子)，北面接大殿、南口敞开接城门大道；
    //     铺地按八个方向放射(小的只有纵横两道)，正中一座方尖碑(影子落在东南)，东西两侧各一座喷泉；后殿两旁几棵树
    private static void PapalCathedral(LandmarkBrush p)
    {
        int W = p.W, H = p.H;
        bool big = H >= 32;
        Color32 paving = Rgb(232, 226, 212), travertine = Rgb(208, 198, 176), column = Rgb(246, 242, 232),
            lead = Rgb(118, 122, 136), copper = Rgb(96, 150, 128), front = Rgb(238, 230, 210), gold = Rgb(236, 196, 84),
            obelisk = Rgb(170, 150, 118);
        p.Box(0, 0, W - 1, H - 1, paving, KPlaza);
        float fx = (W - 1) / 2f;
        int cx = W / 2;
        // 正殿(半宽)、北端、南立面；横厅中线、半长、半宽
        int nave = big ? 4 : 3, ny0 = big ? 5 : 4, ny1 = H * 56 / 100;
        int ty = ny0 + (ny1 - ny0) / 3, arm = W / 2 - (big ? 3 : 2), tw = big ? 3 : 2;
        // 后殿两旁的树
        p.Tree(1, 1);
        p.Tree(W - 3, 1);
        if (big)
        {
            p.Tree(3, 2);
            p.Tree(W - 5, 2);
        }
        // 后殿：正殿北端的半圆
        for (int y = ny0 - nave; y <= ny0; y++)
        for (int x = cx - nave; x < cx + nave; x++)
        {
            float dx = x - fx, dy = y - ny0;
            if (dx * dx + dy * dy <= nave * nave) p.Put(x, y, Darker(lead, dx + dy < 0f ? 1.1f : 0.85f));
        }
        p.Gable(cx - nave, ny0, cx + nave - 1, ny1, lead, false);
        p.Gable(cx - arm, ty - tw, cx + arm - 1, ty + tw - 1, lead, true);
        // 交叉处的大穹顶与采光亭
        p.Dome(fx, ty - 0.5f, nave + (big ? 0.2f : -0.4f), copper);
        p.Put(cx - 1, ty - 1, gold);
        p.Put(cx, ty - 1, gold);
        if (big)
        {
            p.Put(cx - 1, ty, gold);
            p.Put(cx, ty, gold);
        }
        // 南立面、大门、两座钟楼、台阶
        int ts = big ? 3 : 2;
        p.Box(cx - nave, ny1 - 1, cx + nave - 1, ny1, front);
        p.Put(cx - 1, ny1, Door);
        p.Put(cx, ny1, Door);
        foreach (int x0 in new[] { cx - nave - ts, cx + nave })
        {
            p.Hip(x0, ny1 - ts, x0 + ts - 1, ny1, lead);
            p.Put(x0 + ts / 2, ny1 - ts / 2 - (big ? 1 : 0), gold);
        }
        p.Box(cx - nave - ts, ny1 + 1, cx + nave + ts - 1, ny1 + 1, travertine, KPlaza);
        // 椭圆广场与两道柱廊
        int py0 = ny1 + 2;
        float pcy = (py0 + H - 1) / 2f, rx = W / 2f - 1f, ry = (H - 1 - py0) / 2f + 0.5f;
        for (int y = py0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float dx = x - fx, dy = y - pcy, e = Mathf.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
            if (e > 1f) continue;
            // 北面接大殿、南口接大道：柱廊在这两处断开
            bool opening = dy < -ry * 0.55f && Mathf.Abs(dx) <= nave + ts || dy > ry * 0.55f && Mathf.Abs(dx) <= rx * 0.4f;
            if (e >= 0.86f && !opening) p.Put(x, y, travertine);
            else if (e >= 0.74f && !opening) p.Put(x, y, (x + y) % 2 == 0 ? column : paving, KPlaza);
            else if (Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f || big && Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy) * rx / ry) < 0.6f)
                p.Put(x, y, travertine, KPlaza);
        }
        int oy = Mathf.RoundToInt(pcy);
        p.Put(cx - 1, oy, obelisk);
        p.Put(cx, oy, obelisk);
        p.Put(cx, oy + 1, new Color32(40, 28, 20, 56), KPlaza);
        foreach (float side in new[] { -1f, 1f })
        {
            int wx = Mathf.RoundToInt(fx + side * rx * 0.48f);
            p.Put(wx, oy, WaterBlue);
            p.Put(wx - 1, oy, column);
            p.Put(wx + 1, oy, column);
            if (big)
            {
                p.Put(wx, oy - 1, column);
                p.Put(wx, oy + 1, column);
            }
        }
        p.Gates(2, paving);
    }

    private static void WesternCastle(LandmarkBrush p)
    {
        int W = p.W, H = p.H, gx = p.Gx;
        bool big = H >= 32;
        Color32 stone = Rgb(146, 140, 130), stoneLit = Rgb(176, 170, 160), stoneDark = Rgb(112, 108, 100),
            bailey = Rgb(190, 184, 170), berm = Rgb(226, 220, 206), slate = Rgb(96, 98, 112), hall = Rgb(152, 82, 66),
            chapel = Rgb(118, 114, 126), grass = Rgb(132, 168, 104);
        // 雉堞墙：外一圈一明一暗，里一圈暗(墙上走道的影)
        void Wall(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (x == x0 || x == x1 || y == y0 || y == y1) p.Put(x, y, (x + y) % 2 == 0 ? stoneLit : stone);
                else if (x == x0 + 1 || x == x1 - 1 || y == y0 + 1 || y == y1 - 1) p.Put(x, y, stoneDark);
            }
        }
        p.Box(0, 0, W - 1, H - 1, berm, KPlaza);
        p.Box(3, 3, W - 4, H - 4, bailey, KPlaza);
        p.Speckle(3, 3, W - 4, H - 4, big ? 0.06f : 0.1f, grass, 11);
        Wall(1, 1, W - 2, H - 2);
        // 主堡
        int ks = W * 2 / 5, kx0 = (W - ks) / 2, kx1 = kx0 + ks - 1, ky0 = 4, ky1 = ky0 + (big ? ks + 2 : ks - 1);
        Wall(kx0, ky0, kx1, ky1);
        p.Hip(kx0 + 2, ky0 + 2, kx1 - 2, ky1 - 2, slate, false);
        p.Shade(kx0, ky0, kx1, ky1);
        p.Dome(kx1 - 0.5f, ky0 + 0.5f, 2f, slate);
        // 西侧大厅、东侧礼拜堂
        int hw = big ? 3 : 2;
        p.Gable(3, ky0, 3 + hw, ky1 - 1, hall, false);
        p.Gable(W - 4 - hw, ky0 + 3, W - 4, ky0 + (big ? 10 : 7), chapel, false);
        // 大城：南边场院两排营房
        if (big)
        {
            p.Gable(4, H - 10, gx - 2, H - 8, hall, true);
            p.Gable(gx + 5, H - 10, W - 5, H - 8, hall, true);
        }
        // 水井
        p.Box(W / 2 - 2, H - 7, W / 2, H - 5, stoneDark);
        p.Put(W / 2 - 1, H - 6, WaterBlue);
        p.Gates(3, berm);
        // 四角圆塔(石板尖顶)、南门两座门楼塔
        p.Dome(1.5f, 1.5f, 2f, slate);
        p.Dome(W - 2.5f, 1.5f, 2f, slate);
        p.Dome(1.5f, H - 2.5f, 2f, slate);
        p.Dome(W - 2.5f, H - 2.5f, 2f, slate);
        p.Dome(gx - 2.5f, H - 2.5f, 2f, stone);
        p.Dome(gx + 5.5f, H - 2.5f, 2f, stone);
    }

    private static void JapanCastle(LandmarkBrush p)
    {
        int W = p.W, H = p.H;
        bool big = H >= 32;
        Color32 stone = Rgb(150, 145, 135), plaster = Rgb(244, 242, 234), gravel = Rgb(216, 210, 194),
            roof = Rgb(70, 72, 80), wood = Rgb(150, 110, 70);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            p.Put(x, y, (x + 2 * y) % 7 == 0 ? Ripple : WaterBlue);
        p.Box(2, 2, W - 3, H - 3, stone);
        p.Frame(3, 3, W - 4, H - 4, plaster);
        p.Box(4, 4, W - 5, H - 5, gravel, KPlaza);
        // 天守：石台上两重檐(上层亮一点)，脊端金鯱
        int tw = W * 2 / 5, th = W * 7 / 20, tx0 = (W - tw) / 2, tx1 = tx0 + tw - 1, ty0 = big ? 8 : 5, ty1 = ty0 + th - 1;
        p.Box(tx0 - 1, ty0 - 1, tx1 + 1, ty1 + 1, stone);
        p.Hip(tx0, ty0, tx1, ty1, roof, false);
        int ux0 = tx0 + 2, uy0 = ty0 + 2, ux1 = tx1 - 2, uy1 = ty1 - 2;
        p.Hip(ux0, uy0, ux1, uy1, Darker(roof, 1.22f), false);
        int ridge = uy0 + (uy1 - uy0) / 2;
        p.Put(ux0, ridge, PalaceRoof);
        p.Put(ux1, ridge, PalaceRoof);
        p.Shade(tx0 - 1, ty0 - 1, tx1 + 1, ty1 + 1);
        // 大城：西边连一座小天守
        if (big)
        {
            p.Hip(tx0 - 4, ty1 - 3, tx0 - 2, ty1, roof);
        }
        // 四角橹
        p.Hip(3, 3, 5, 5, roof, false);
        p.Hip(W - 6, 3, W - 4, 5, roof, false);
        p.Hip(3, H - 6, 5, H - 4, roof, false);
        p.Hip(W - 6, H - 6, W - 4, H - 4, roof, false);
        // 园里松树
        for (int y = 4; y <= H - 5; y++)
        for (int x = 4; x <= W - 5; x++)
            if (p.Kind(x, y) == KPlaza && DetailHash(p.Seed, x, y, 21) < (big ? 0.05f : 0.1f)) p.Tree(x, y);
        // 木桥过护城河，石垣开口，城门橹
        for (int side = 0; side < 4; side++)
        {
            if (!p.Open(side)) continue;
            for (int d = 0; d < 2; d++)
            for (int i = 1; i <= 2; i++)
                p.Side(side, d, i, wood);
            for (int i = 1; i <= 2; i++) p.Side(side, 2, i, gravel, KPlaza);
            for (int i = 0; i < 4; i++) p.Side(side, 3, i, roof);
        }
    }

    private static void RomanForum(LandmarkBrush p)
    {
        int W = p.W, H = p.H, gx = p.Gx;
        bool big = H >= 32;
        Color32 marble = Marble, paving = Rgb(230, 224, 210), column = Rgb(204, 198, 186), tile = Rgb(192, 112, 82),
            podium = Rgb(226, 220, 206), arch = Rgb(220, 214, 202), bronze = Rgb(150, 120, 70);
        p.Box(0, 0, W - 1, H - 1, paving, KPlaza);
        p.Box(4, 1, W - 5, H - 2, marble, KPlaza);
        // 东西柱廊
        p.Gable(0, 1, 2, H - 2, tile, false, false);
        p.Gable(W - 3, 1, W - 1, H - 2, tile, false, false);
        for (int y = 1; y <= H - 2; y += 2)
        {
            p.Put(3, y, column);
            p.Put(W - 4, y, column);
        }
        // 北端神庙：台基、坡顶(屋脊南北向)、前排柱子、台阶
        int tx0 = gx - 2, tx1 = gx + 5, ty1 = H * 2 / 5;
        p.Box(tx0, 1, tx1, ty1, podium);
        p.Gable(tx0 + 1, 1, tx1 - 1, ty1 - 3, tile, false, false);
        for (int x = tx0 + 1; x <= tx1 - 1; x += 2) p.Put(x, ty1 - 2, column);
        for (int x = tx0; x <= tx1; x++)
        {
            p.Put(x, ty1 - 1, marble, KPlaza);
            p.Put(x, ty1, paving, KPlaza);
        }
        p.Shade(tx0, 1, tx1, ty1 - 2);
        // 大城：西南一座会堂
        if (big) p.Gable(5, H - 11, gx - 3, H - 6, Darker(tile, 0.92f), true);
        // 纪念柱
        int my = H * 3 / 5;
        p.Box(W / 2 - 1, my, W / 2, my + 1, column);
        p.Put(W / 2 - 1, my, bronze);
        p.Shade(W / 2 - 1, my, W / 2, my + 1);
        // 南口凯旋门
        p.Flat(gx - 1, H - 3, gx + 4, H - 2, arch);
        // 东西有街通过来的，穿过柱廊
        for (int side = 2; side < 4; side++)
            if (p.Open(side)) p.Gate(side, 4, marble);
    }

    private static void VikingHall(LandmarkBrush p)
    {
        int W = p.W, H = p.H, gx = p.Gx;
        bool big = H >= 32;
        Color32 yard = Rgb(176, 156, 122), stake = Rgb(120, 90, 60), stakeLit = Rgb(150, 116, 76), shingle = Rgb(112, 86, 58),
            turf = Rgb(110, 120, 80), rune = Rgb(150, 145, 135), fire = Rgb(222, 124, 58);
        p.Box(0, 0, W - 1, H - 1, yard, KPlaza);
        p.Speckle(0, 0, W - 1, H - 1, 0.08f, Darker(yard, 0.9f), 12);
        // 木栅
        for (int x = 0; x < W; x++)
        {
            p.Put(x, 0, x % 2 == 0 ? stake : stakeLit);
            p.Put(x, H - 1, x % 2 == 0 ? stake : stakeLit);
        }
        for (int y = 1; y < H - 1; y++)
        {
            p.Put(0, y, y % 2 == 0 ? stake : stakeLit);
            p.Put(W - 1, y, y % 2 == 0 ? stake : stakeLit);
        }
        p.Gates(1, yard);
        // 大城：北边一座小屋
        if (big) p.Gable(4, 2, W - 5, 4, turf);
        // 长屋大殿：船形(四角削掉)，脊端金龙头
        int hx0 = 2, hx1 = W - 3, hy0 = H / 4, hy1 = hy0 + (big ? 7 : 5);
        p.Gable(hx0, hy0, hx1, hy1, shingle, true);
        p.Put(hx0, hy0, yard, KPlaza);
        p.Put(hx0, hy1, yard, KPlaza);
        p.Put(hx1, hy0, yard, KPlaza);
        p.Put(hx1, hy1, yard, KPlaza);
        int ridge = (hy0 + hy1) / 2;
        p.Put(hx0, ridge, PalaceRoof);
        p.Put(hx1, ridge, PalaceRoof);
        // 南边两座小长屋
        p.Gable(2, H - 6, gx - 2, H - 4, turf, true);
        p.Gable(gx + 5, H - 6, W - 3, H - 4, turf, true);
        // 火塘、符文石
        int fy = (hy1 + H - 6) / 2;
        p.Box(W / 2 - 2, fy - 1, W / 2 + 1, fy + 1, rune);
        p.Box(W / 2 - 1, fy, W / 2, fy, fire);
        p.Put(3, hy1 + 2, rune);
        p.Shade(3, hy1 + 2, 3, hy1 + 2);
        p.Put(W - 4, hy0 - 2, rune);
        p.Shade(W - 4, hy0 - 2, W - 4, hy0 - 2);
    }

    private static void GreatMosque(LandmarkBrush p, DetailStyle style)
    {
        int W = p.W, H = p.H;
        bool big = H >= 32;
        Color32 court = Rgb(244, 240, 228), wall = Rgb(206, 188, 150), roof = Rgb(220, 206, 174), arcade = Rgb(212, 196, 162),
            minaret = Rgb(246, 240, 226), rim = Rgb(200, 190, 170);
        p.Box(0, 0, W - 1, H - 1, court, KPlaza);
        p.Frame(0, 0, W - 1, H - 1, wall);
        // 礼拜殿：平顶上一排排小穹顶，正中大穹顶
        int hh = H * 2 / 5;
        p.Flat(1, 1, W - 2, hh, roof);
        for (int y = 3; y < hh; y += 3)
        for (int x = 3; x < W - 3; x += 3)
            p.Put(x, y, Darker(roof, 1.12f));
        p.Dome((W - 1) / 2f, (1 + hh) / 2f, W / 6f + 0.2f, style.Dome);
        // 三面回廊(拱下一明一暗)
        p.Flat(1, hh + 1, 2, H - 2, arcade, false);
        p.Flat(W - 3, hh + 1, W - 2, H - 2, arcade, false);
        p.Flat(1, H - 3, W - 2, H - 2, arcade, false);
        Color32 arch = Darker(court, 0.86f);
        for (int y = hh + 2; y <= H - 4; y += 2)
        {
            p.Put(3, y, arch, KPlaza);
            p.Put(W - 4, y, arch, KPlaza);
        }
        for (int x = 4; x <= W - 5; x += 2) p.Put(x, H - 4, arch, KPlaza);
        // 庭院当中水池
        int fx = W / 2 - 1, fy = (hh + H - 3) / 2;
        p.Box(fx - 1, fy - 1, fx + 2, fy + 2, rim);
        p.Box(fx, fy, fx + 1, fy + 1, WaterBlue);
        // 南门(两侧门柱)；东西有街通过来、又在礼拜殿以南的，也开门
        p.Gate(1, 3, court);
        for (int d = 0; d < 3; d++)
        {
            p.Side(1, d, -1, Darker(wall, 0.85f));
            p.Side(1, d, 4, Darker(wall, 0.85f));
        }
        if (p.GateRow > hh)
            for (int side = 2; side < 4; side++)
                if (p.Open(side)) p.Gate(side, 3, court);
        // 宣礼塔
        p.Dome(W - 2.5f, H - 2.5f, 2f, minaret);
        if (big) p.Dome(1.5f, H - 2.5f, 2f, minaret);
    }

    private static void Apadana(LandmarkBrush p)
    {
        int W = p.W, H = p.H, gx = p.Gx;
        Color32 terrace = Rgb(204, 194, 174), edge = Rgb(168, 158, 138), floor = Rgb(218, 208, 188), column = Rgb(128, 116, 98),
            tower = Rgb(184, 172, 150), garden = Rgb(124, 164, 100), hedge = Rgb(92, 134, 80), stair = Rgb(230, 222, 206);
        int ty = H * 11 / 20;
        // 台基
        p.Box(0, 0, W - 1, ty, terrace, KPlaza);
        p.Frame(0, 0, W - 1, ty, edge);
        // 柱网方殿、四角塔楼
        int hx0 = 3, hx1 = W - 4, hy0 = 2, hy1 = ty - 3;
        p.Box(hx0, hy0, hx1, hy1, floor);
        p.Frame(hx0, hy0, hx1, hy1, Darker(floor, 0.86f));
        for (int y = hy0 + 1; y < hy1; y += 2)
        for (int x = hx0 + 1; x < hx1; x += 2)
            p.Put(x, y, column);
        p.Shade(hx0, hy0, hx1, hy1);
        p.Flat(hx0 - 1, hy0 - 1, hx0 + 2, hy0 + 2, tower, false);
        p.Flat(hx1 - 2, hy0 - 1, hx1 + 1, hy0 + 2, tower, false);
        p.Flat(hx0 - 1, hy1 - 2, hx0 + 2, hy1 + 1, tower);
        p.Flat(hx1 - 2, hy1 - 2, hx1 + 1, hy1 + 1, tower);
        // 南面大台阶
        for (int y = ty - 2; y <= ty; y++)
        for (int x = gx - 2; x <= gx + 5; x++)
            p.Put(x, y, y % 2 == 0 ? stair : Darker(stair, 0.9f), KPlaza);
        // 四分园：一圈绿篱，十字水渠(渠边小路)，当中水池，园里柏树
        p.Box(0, ty + 1, W - 1, H - 1, garden, KPlaza);
        p.Frame(0, ty + 1, W - 1, H - 1, hedge);
        int gy = (ty + H) / 2;
        for (int y = ty + 2; y <= H - 2; y++)
        {
            p.Put(gx, y, stair, KPlaza);
            p.Put(gx + 1, y, WaterBlue);
            p.Put(gx + 2, y, WaterBlue);
            p.Put(gx + 3, y, stair, KPlaza);
        }
        for (int x = 1; x <= W - 2; x++)
        {
            p.Put(x, gy - 1, stair, KPlaza);
            p.Put(x, gy, WaterBlue);
            p.Put(x, gy + 1, stair, KPlaza);
        }
        p.Box(gx - 1, gy - 2, gx + 4, gy + 2, stair);
        p.Box(gx, gy - 1, gx + 3, gy + 1, WaterBlue);
        for (int y = ty + 2; y <= H - 2; y++)
        for (int x = 1; x <= W - 2; x++)
            if (p.Kind(x, y) == KPlaza && DetailHash(p.Seed, x, y, 23) < 0.2f && (x + y) % 2 == 0) p.Put(x, y, hedge);
        p.Gate(1, 1, stair);
    }

    private static void GopuramTemple(LandmarkBrush p)
    {
        int W = p.W, H = p.H, gx = p.Gx;
        bool big = H >= 32;
        Color32 floor = Rgb(216, 202, 178), wall = Rgb(196, 146, 88), gopuram = Rgb(222, 170, 120), vimana = Rgb(226, 182, 140),
            gold = Rgb(236, 200, 96), hall = Rgb(206, 184, 150), step = Rgb(176, 160, 130);
        p.Box(0, 0, W - 1, H - 1, floor, KPlaza);
        p.Frame(0, 0, W - 1, H - 1, wall);
        for (int side = 2; side < 4; side++)
            if (p.Open(side)) p.Gate(side, 1, floor);
        // 塔殿：层层收进，金顶
        int vs = W / 5, cxL = W / 2 - 1, cxR = W / 2, cyT = H * 2 / 5 - 1, cyB = cyT + 1;
        for (int k = vs; k >= 0; k--)
            p.Box(cxL - k, cyT - k, cxR + k, cyB + k, Darker(vimana, 0.74f + (vs - k) * 0.28f / Mathf.Max(1, vs)));
        p.Box(cxL, cyT, cxR, cyB, gold);
        p.Shade(cxL - vs, cyT - vs, cxR + vs, cyB + vs);
        // 前面柱厅
        int my0 = cyB + vs + 1;
        p.Flat(cxL - 2, my0, cxR + 2, my0 + 2, hall);
        // 门塔：南面(大城北面也有一座)，脊上一排金顶
        void Gopuram(int y0, int y1)
        {
            p.Hip(gx - 2, y0, gx + 5, y1, gopuram, false);
            int ridge = y0 + (y1 - y0) / 2;
            for (int x = gx - 1; x <= gx + 4; x += 2) p.Put(x, ridge, gold);
        }
        Gopuram(H - 3, H - 1);
        if (big) Gopuram(0, 2);
        // 东北角阶梯水池
        p.Box(W - 5, 1, W - 2, 4, step);
        p.Box(W - 4, 2, W - 3, 3, WaterBlue);
    }

    private static void GreatPyramid(LandmarkBrush p)
    {
        int W = p.W, H = p.H;
        Color32 sand = Rgb(222, 200, 146), north = Rgb(238, 214, 152), west = Rgb(224, 198, 136), east = Rgb(194, 164, 106),
            south = Rgb(176, 146, 92), cap = Rgb(242, 216, 120);
        p.Box(0, 0, W - 1, H - 1, sand, KPlaza);
        p.Speckle(0, 0, W - 1, H - 1, 0.06f, Darker(sand, 0.94f), 13);
        float cx = (W - 1) / 2f, cy = (H - 1) / 2f, half = Mathf.Min(W, H) / 2f - 1.5f;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float dx = x - cx, dy = y - cy, d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            if (d > half) continue;
            Color32 face = Mathf.Abs(dx) >= Mathf.Abs(dy) ? dx < 0 ? west : east : dy < 0 ? north : south;
            float k = Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy)) < 0.6f ? 0.92f : 1f;
            if (Mathf.FloorToInt(d) % 2 == 0) k *= 0.97f;
            p.Put(x, y, d < 1.6f ? cap : Darker(face, k));
        }
        int e0 = Mathf.CeilToInt(cx - half), e1 = Mathf.FloorToInt(cx + half);
        p.Shade(e0, Mathf.CeilToInt(cy - half), e1, Mathf.FloorToInt(cy + half));
    }

    private static void TemploMayor(LandmarkBrush p)
    {
        int W = p.W, H = p.H;
        bool big = H >= 32;
        Color32 plaza = Rgb(236, 232, 222), rim = Rgb(200, 196, 180), head = Rgb(170, 160, 140), tierA = Rgb(196, 186, 166),
            tierB = Rgb(216, 208, 190), stair = Rgb(242, 238, 230), red = Rgb(176, 70, 50), blue = Rgb(70, 112, 170),
            round = Rgb(206, 196, 176);
        p.Box(0, 0, W - 1, H - 1, plaza, KPlaza);
        // 蛇墙：隔几像素一个蛇头
        for (int x = 0; x < W; x++)
        {
            p.Put(x, 0, x % 3 == 0 ? head : rim);
            p.Put(x, H - 1, x % 3 == 0 ? head : rim);
        }
        for (int y = 1; y < H - 1; y++)
        {
            p.Put(0, y, y % 3 == 0 ? head : rim);
            p.Put(W - 1, y, y % 3 == 0 ? head : rim);
        }
        p.Gates(1, plaza);
        // 阶梯金字塔：一层层台(东南半边暗一点)，顶上平台
        int ps = Mathf.Min(W, H) * 3 / 5 & ~1;
        int x1 = W - 3, x0 = x1 - ps + 1, y0 = (H - ps) / 2 - (big ? 2 : 1), y1 = y0 + ps - 1;
        float pcx = (x0 + x1) / 2f, pcy = (y0 + y1) / 2f;
        int tiers = (ps - 4) / 2;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            int k = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(y - y0, y1 - y));
            Color32 color = k >= tiers ? plaza : k % 2 == 0 ? tierA : tierB;
            p.Put(x, y, Darker(color, x - pcx + y - pcy > 0f ? 0.92f : 1.04f));
        }
        p.Shade(x0, y0, x1, y1);
        // 顶上两座神殿：北蓝(雨神)、南红(战神)；西面并排两道台阶
        int tx0 = x0 + tiers, tx1 = x1 - tiers, ty0 = y0 + tiers, ty1 = y1 - tiers, tm = (ty0 + ty1) / 2;
        p.Box(tx0 + 1, ty0, tx1, tm, blue);
        p.Box(tx0 + 1, tm + 1, tx1, ty1, red);
        for (int x = x0; x <= tx0; x++)
        {
            Color32 riser = (x - x0) % 2 == 0 ? stair : Darker(stair, 0.9f);
            p.Put(x, ty0, riser);
            p.Put(x, ty1, riser);
        }
        // 西边圆形风神庙
        p.Dome(x0 - 3f, pcy, 2.2f, round);
        p.Put(Mathf.RoundToInt(x0 - 3f), Mathf.RoundToInt(pcy), red);
    }
}
