using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 原始聚落(用户 2026-10-16："部落制度的建筑好高级，有点出戏"；演示版看过说"可以，部落聚落移植进游戏")：
// 原始政体(部落、方国)的城不按古城画(方城墙、方格街、钟鼓楼)，按本文化原始改革走到第几步分三段：
//   ① 环壕聚落(原始改革的头几步，华夏是原始群、母系氏族)：一圈环壕(留两三处通道)，里面圆形半地穴小屋按氏族一簇簇
//      围着中心广场、门朝广场，广场北边一座大房子、正中火塘；屋旁窖穴；壕外陶窑、墓地、粟田、猪圈；
//   ② 部落中心(中间几步，华夏是父系氏族、部落联盟)：夯土围垣一圈(外有壕沟、四面开口)，广场北边夯土台基上一座大殿，
//      东边三层祭坛；各氏族一片，围着自家小院，有一栋分间长屋；
//   ③ 早期城址(最后一步，华夏是世袭王权)：圆角方形夯土城垣、护城壕、四门；北部宫城(围墙、台基、前后两殿)，
//      南门大道直通宫城、东西一条横道，东南青铜作坊、西南陶窑，其余是一排排方形半地穴房。
// 城外的小村是几座圆屋围着一小片空地；小路照常(见 UrbanFabric.PlaceTrails)。游牧、林地照旧是营地(见 DrawCamp)。
// 平面图(一格一像素，拉远一些时)按同样的轮廓画：环壕或围垣一圈、里面一个个小屋点、正中广场。
// 画布的行往北增，下面按"往南为正"的演示版坐标写，落笔时翻过来(见 Put)。
public partial class PureMapPatch
{
    private static readonly Color32 TribeGround = Rgb(204, 188, 148), TribePlaza = Rgb(224, 210, 172), TribePath = Rgb(186, 166, 124),
        TribeThatch = Rgb(160, 124, 80), TribeThatchLit = Rgb(184, 148, 98), TribeThatchDark = Rgb(114, 86, 56), TribeHole = Rgb(70, 52, 38),
        TribeBigRoof = Rgb(140, 102, 62), TribeWallTop = Rgb(212, 186, 134), TribeWallSide = Rgb(172, 146, 100), TribeWallShade = Rgb(146, 122, 84),
        TribeDitch = Rgb(112, 146, 158), TribeDitchLit = Rgb(148, 178, 186), TribeBank = Rgb(158, 136, 100), TribePlatform = Rgb(222, 202, 154),
        TribePlatformRim = Rgb(188, 166, 118), TribePit = Rgb(98, 80, 60), TribeKiln = Rgb(180, 98, 58), TribeKilnHot = Rgb(232, 150, 70),
        TribeGrave = Rgb(194, 184, 164), TribeGraveDark = Rgb(150, 140, 122), TribeMillet = Rgb(208, 198, 122), TribeMilletLine = Rgb(176, 166, 96),
        TribeFence = Rgb(124, 96, 64), TribePig = Rgb(222, 196, 186), TribeAltar = Rgb(214, 196, 160), TribeAltarTop = Rgb(236, 222, 190),
        TribeSlag = Rgb(80, 70, 64), TribeShadow = new(60, 44, 30, 60);

    // 原始聚落的阶段(-1 = 不是原始政体的城)：本文化原始改革(树里原始政体的节点)走到第几步
    private static int TribalStageOf(City city, string culture)
    {
        if (!TribalFissionService.IsTribal(city?.kingdom) || !CultureService.IsValidCulture(culture)) return -1;
        return TribalFissionService.Stage(culture);
    }

    // 聚落的半径(像素)：按建成地块折成圆，至少五格半
    private static float TribalRadius(CityPlan plan) => Mathf.Max(22f, UrbanRadius(plan) * 0.95f);

    // 聚落的中心(格)
    private static Vector2 TribalCenter(CityPlan plan) =>
        plan.gateX != int.MinValue ? new Vector2(plan.gateX, plan.gateY) : (Vector2)(plan.city?.getTile()?.posV ?? Vector3.zero);

    // 轮廓：一、二段是圆(带一点起伏)，三段是圆角方；返回"多远"(与半径比)
    private static float TribalShape(int stage, float dx, float dy, long seed)
    {
        float angle = Mathf.Atan2(dy, dx), wobble = 1f + 0.12f * (DetailNoise((angle / (Mathf.PI * 2f) * 32f + 32f) % 32f, 4f, seed + 41) - 0.5f);
        if (stage < 2) return Mathf.Sqrt(dx * dx + dy * dy) / wobble;
        float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
        return Mathf.Pow(Mathf.Pow(ax, 5f) + Mathf.Pow(ay, 5f), 0.2f) / wobble;
    }

    private static void DrawTribal(Canvas c, CityPlan plan, UrbanFabric.Geometry g, DetailStyle style, Color32 tint, long seed)
    {
        int s = DetailScale, stage = Mathf.Max(0, plan.tribal), top = c.PH - 1;
        Color32 T(Color32 color) => Lerp(tint, color, 0.9f);
        // 演示版坐标(y 往南增) → 画布
        void Put(float x, float y, Color32 color, byte kind = KBuilding) => c.Put(Mathf.RoundToInt(x), top - Mathf.RoundToInt(y), T(color), kind);
        void ShadeAt(float x, float y) => c.Put(Mathf.RoundToInt(x), top - Mathf.RoundToInt(y), TribeShadow, KBuilding);
        byte KindAt(float x, float y) => c.Kind(Mathf.RoundToInt(x), top - Mathf.RoundToInt(y));
        bool Free(float x, float y) => KindAt(x, y) is KGround or KPlaza;
        bool Outside(float x, float y) => KindAt(x, y) == KEmpty && c.Land(Mathf.RoundToInt(x), top - Mathf.RoundToInt(y));
        bool LandAt(float x, float y) => c.Land(Mathf.RoundToInt(x), top - Mathf.RoundToInt(y));
        float H(long a, long b = 0, long d = 0) => DetailHash(seed, a, b, d);

        // 水面先占住(不画国色，露出底下的水)
        for (int ty = 0; ty < c.H; ty++)
        for (int tx = 0; tx < c.W; tx++)
        {
            WorldTile tile = World.world.GetTile(c.X0 + tx, c.Y0 + ty);
            if (tile?.Type != null && tile.Type.liquid) Fill(c, tx, ty, Clear, KWater);
        }
        Vector2 center = TribalCenter(plan);
        float cx = (center.x - c.X0) * s + 2, cy = top - ((center.y - c.Y0) * s + 2), R = TribalRadius(plan);
        float Dist(float x, float y) => TribalShape(stage, x - cx, y - cy, seed);
        float rin = R * (stage == 0 ? 0.82f : 0.86f);
        // ① 地面
        for (int y = 0; y < c.PH; y++)
        for (int x = 0; x < c.PW; x++)
            if (Dist(x, y) <= rin && LandAt(x, y) && KindAt(x, y) == KEmpty)
                Put(x, y, H(x, y, 5) < 0.05f ? Darker(TribeGround, 0.94f) : TribeGround, KGround);
        // ② 环壕 / 夯土围垣 + 壕沟；通道或城门
        int gateCount = stage == 0 ? 2 + (H(90) < 0.5f ? 1 : 0) : 4;
        var gates = new List<float>();
        for (int i = 0; i < gateCount; i++)
            gates.Add(stage == 2 ? i * Mathf.PI / 2f : (i + H(i, 91) * 0.35f) / gateCount * Mathf.PI * 2f + H(92) * Mathf.PI * 2f);
        bool GateGap(float angle, float r) => gates.Any(gate => Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, gate * Mathf.Rad2Deg)) *
            Mathf.Deg2Rad * r < (stage == 2 ? 5f : 4f));
        float wallOut = rin + (stage == 0 ? 0f : 4f), ditchOut = wallOut + 3f + (stage == 0 ? 1f : 0f);
        for (int y = 0; y < c.PH; y++)
        for (int x = 0; x < c.PW; x++)
        {
            float d = Dist(x, y);
            if (d < rin - 0.01f || d > ditchOut + 1f || !LandAt(x, y) || KindAt(x, y) == KWater) continue;
            float angle = Mathf.Atan2(y - cy, x - cx);
            if (GateGap(angle, d))
            {
                if (d > rin) Put(x, y, TribePath, KPath);
                continue;
            }
            if (stage > 0 && d <= wallOut)
            {
                float t = (d - rin) / (wallOut - rin);
                Put(x, y, t < 0.55f ? (x + y * 3) % 9 == 0 ? Darker(TribeWallTop, 0.95f) : TribeWallTop : t < 0.8f ? TribeWallSide : TribeWallShade, KWall);
            }
            else if (d > ditchOut) Put(x, y, TribeBank, KWater);
            else Put(x, y, (x + 2 * y) % 7 == 0 ? TribeDitchLit : TribeDitch, KWater);
        }
        // ③ 中心广场
        float rp = Mathf.Max(stage == 0 ? 6f : 7f, rin * (stage == 0 ? 0.24f : 0.2f)), plazaY = stage == 2 ? cy + rin * 0.15f : cy;
        for (int y = Mathf.FloorToInt(plazaY - rp); y <= plazaY + rp; y++)
        for (int x = Mathf.FloorToInt(cx - rp); x <= cx + rp; x++)
            if ((x - cx) * (x - cx) + (y - plazaY) * (y - plazaY) <= rp * rp && Free(x, y))
                Put(x, y, H(x, y, 6) < 0.04f ? Darker(TribePlaza, 0.93f) : TribePlaza, KPlaza);

        // 圆形半地穴小屋：草顶西北亮、东南暗，正中烟孔，门朝 (tx, ty)
        bool Hut(int x, int y, int r, float tx, float ty)
        {
            for (int dy = -r - 1; dy <= r + 1; dy++)
            for (int dx = -r - 1; dx <= r + 1; dx++)
                if (dx * dx + dy * dy <= (r + 0.4f) * (r + 0.4f) && !Free(x + dx, y + dy)) return false;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r + 0.3f) continue;
                bool lit = dx + dy < -0.5f, rim = d > r - 0.7f;
                Put(x + dx, y + dy, d < 0.7f ? TribeHole : rim ? TribeThatchDark : lit ? TribeThatchLit : TribeThatch);
            }
            foreach ((int dx, int dy) in new[] { (r + 1, 0), (r + 1, 1), (0, r + 1), (1, r + 1) })
                if (Free(x + dx, y + dy)) ShadeAt(x + dx, y + dy);
            float a = Mathf.Atan2(ty - y, tx - x);
            int gx = Mathf.RoundToInt(x + Mathf.Cos(a) * (r + 1)), gy = Mathf.RoundToInt(y + Mathf.Sin(a) * (r + 1));
            if (Free(gx, gy)) Put(gx, gy, TribeHole);
            return true;
        }
        // 方形房：两坡草顶，正脊一道亮线(画布坐标里画，行翻过来)
        bool House(int x0, int y0, int x1, int y1, Color32 roof)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (!Free(x, y)) return false;
            Roof(c, x0, top - y1, x1, top - y0, x1 - x0 >= y1 - y0, T(roof), KBuilding, true);
            for (int x = x0 + 1; x <= x1 + 1; x++)
                if (Free(x, y1 + 1)) ShadeAt(x, y1 + 1);
            return true;
        }
        void Pit(int x, int y)
        {
            if (Free(x, y)) Put(x, y, TribePit);
        }
        // 夯土台基：一圈台沿，东南落影
        void Platform(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!Free(x, y) && KindAt(x, y) != KBuilding) continue;
                bool rim = x == x0 || x == x1 || y == y0 || y == y1;
                Put(x, y, rim ? TribePlatformRim : TribePlatform, KPlaza);
            }
            for (int x = x0 + 1; x <= x1 + 1; x++) ShadeAt(x, y1 + 1);
            for (int y = y0 + 1; y <= y1; y++) ShadeAt(x1 + 1, y);
        }

        int icx = Mathf.RoundToInt(cx), icy = Mathf.RoundToInt(cy);
        if (stage == 0)
        {
            // 大房子在广场北边，广场正中火塘
            int bw = rin > 30f ? 12 : 9, bh = rin > 30f ? 8 : 6, bx0 = icx - bw / 2, by0 = Mathf.RoundToInt(cy - rp - bh - 1);
            House(bx0, by0, bx0 + bw - 1, by0 + bh - 1, TribeBigRoof);
            Put(icx, by0 + bh, TribeHole);
            Put(icx, icy, TribeKiln, KPlaza);
            Put(icx + 1, icy, TribeKilnHot, KPlaza);
        }
        else
        {
            int pw = stage == 2 ? (rin > 40f ? 26 : 18) : (rin > 30f ? 20 : 14), ph = stage == 2 ? (rin > 40f ? 16 : 12) : (rin > 30f ? 13 : 10);
            int platformTop = Mathf.RoundToInt(stage == 2 ? cy - rin * 0.62f : cy - rp - ph - 2);
            if (stage == 2)
            {
                // 宫城：夯土围墙，里面台基、前后两殿
                int gx0 = icx - pw / 2 - 5, gx1 = icx + pw / 2 + 5, gy0 = platformTop - 4, gy1 = platformTop + ph + 14;
                for (int y = gy0; y <= gy1; y++)
                for (int x = gx0; x <= gx1; x++)
                {
                    if (!Free(x, y)) continue;
                    bool rim = x <= gx0 + 1 || x >= gx1 - 1 || y <= gy0 + 1 || y >= gy1 - 1;
                    if (rim && !(y >= gy1 - 1 && Mathf.Abs(x - icx) <= 2)) Put(x, y, (x + y) % 2 == 1 ? TribeWallTop : TribeWallSide, KWall);
                    else Put(x, y, TribePlaza, KPlaza);
                }
                Platform(icx - pw / 2, platformTop, icx + pw / 2, platformTop + ph);
                House(icx - pw / 2 + 2, platformTop + 2, icx + pw / 2 - 2, platformTop + ph / 2 - 1, TribeBigRoof);
                House(icx - pw / 2 + 5, platformTop + ph / 2 + 1, icx + pw / 2 - 5, platformTop + ph - 2, Darker(TribeBigRoof, 1.06f));
            }
            else
            {
                Platform(icx - pw / 2, platformTop, icx + pw / 2, platformTop + ph);
                House(icx - pw / 2 + 2, platformTop + 2, icx + pw / 2 - 2, platformTop + ph - 3, TribeBigRoof);
                // 祭坛：三层方台
                int ax = Mathf.RoundToInt(cx + rp + 7), ay = icy - 4;
                for (int k = 0; k < 3; k++)
                for (int y = ay - 5 + k * 2; y <= ay + 5 - k * 2; y++)
                for (int x = ax - 5 + k * 2; x <= ax + 5 - k * 2; x++)
                    if (k > 0 || Free(x, y)) Put(x, y, k == 2 ? TribeAltarTop : k == 1 ? TribeAltar : TribePlatformRim, KPlaza);
            }
        }

        // ④ 居住区
        if (stage < 2)
        {
            int clans = stage == 0 ? 4 + (H(93) < 0.5f ? 1 : 0) : 5 + (H(94) < 0.5f ? 1 : 0);
            float offset = H(95) * Mathf.PI * 2f;
            for (int k = 0; k < clans; k++)
            {
                float a0 = offset + k / (float)clans * Mathf.PI * 2f + 0.12f, a1 = offset + (k + 1) / (float)clans * Mathf.PI * 2f - 0.12f;
                float am = (a0 + a1) / 2f, yardR = (rp + rin) * 0.55f;
                float yx = cx + Mathf.Cos(am) * yardR, yy = cy + Mathf.Sin(am) * yardR;
                if (stage == 1)
                {
                    // 氏族小院：扇区中间一方小院，北边一栋分间长屋
                    int ix = Mathf.RoundToInt(yx), iy = Mathf.RoundToInt(yy);
                    for (int y = iy - 4; y <= iy + 4; y++)
                    for (int x = ix - 5; x <= ix + 5; x++)
                        if (Free(x, y)) Put(x, y, TribePlaza, KPlaza);
                    if (House(ix - 7, iy - 9, ix + 7, iy - 6, TribeBigRoof))
                        for (int x = ix - 4; x <= ix + 4; x += 4)
                        for (int y = iy - 9; y <= iy - 6; y++)
                            Put(x, y, TribeThatchDark);
                }
                for (float rr = rp + 6f; rr < rin - 4f; rr += 7f)
                {
                    int n = Mathf.Max(1, Mathf.FloorToInt((a1 - a0) * rr / 7f));
                    for (int i = 0; i < n; i++)
                    {
                        if (H(k, Mathf.RoundToInt(rr), i * 7 + 1) < 0.22f) continue;
                        float a = a0 + (i + 0.5f + (H(k, i, Mathf.RoundToInt(rr)) - 0.5f) * 0.4f) / n * (a1 - a0);
                        int x = Mathf.RoundToInt(cx + Mathf.Cos(a) * rr), y = Mathf.RoundToInt(cy + Mathf.Sin(a) * rr);
                        // 门朝广场(部落中心朝本氏族的小院)
                        float tx = stage == 1 ? yx : cx, ty = stage == 1 ? yy : cy;
                        if (stage == 1 && H(k, i, 97) < 0.35f) House(x - 2, y - 2, x + 2, y + 1, TribeThatch);
                        else Hut(x, y, 2, tx, ty);
                        if (H(k, i, 96) < 0.4f) Pit(x + 3, y + 2);
                    }
                }
            }
        }
        else
        {
            // 南门大道、东西横道；一排排方形房，东南作坊、西南陶窑
            for (int y = 0; y < c.PH; y++)
            for (int x = 0; x < c.PW; x++)
            {
                if (!Free(x, y)) continue;
                bool main = Mathf.Abs(x - cx) <= 1 && y > cy - rin * 0.62f + 30f, cross = Mathf.Abs(y - (plazaY + 2)) <= 1;
                if (main || cross) Put(x, y, TribePath, KPath);
            }
            for (int y0 = Mathf.RoundToInt(cy - rin) + 4; y0 < cy + rin - 4; y0 += 7)
            for (int x0 = Mathf.RoundToInt(cx - rin) + 4; x0 < cx + rin - 4; x0 += 7)
            {
                float qx = x0 + 3 - cx, qy = y0 + 3 - cy;
                if (TribalShape(stage, qx, qy, seed) > rin - 6f) continue;
                if (qx > rin * 0.3f && qy > rin * 0.3f)
                {
                    if (Free(x0 + 2, y0 + 2))
                    {
                        Put(x0 + 2, y0 + 2, TribeKilnHot);
                        Put(x0 + 3, y0 + 2, TribeKiln);
                        Put(x0 + 2, y0 + 3, TribeKiln);
                    }
                    if (Free(x0 + 5, y0 + 4)) Put(x0 + 5, y0 + 4, TribeSlag);
                    House(x0, y0 + 5, x0 + 4, y0 + 6, TribeThatchDark);
                    continue;
                }
                if (qx < -rin * 0.3f && qy > rin * 0.3f)
                {
                    if (H(x0, y0, 98) < 0.6f && Hut(x0 + 3, y0 + 3, 2, x0 + 3, y0 + 10)) Put(x0 + 3, y0 + 3, TribeKilnHot);
                    continue;
                }
                if (H(x0, y0, 99) < 0.15f) continue;
                House(x0, y0, x0 + 4, y0 + 3, TribeThatch);
                if (H(x0, y0, 100) < 0.3f) Pit(x0 + 5, y0 + 4);
            }
        }

        // ⑤ 城外：通道外踩出来的小路、粟田、墓地、陶窑、猪圈
        float rout = rin + (stage == 0 ? 5f : 9f);
        foreach (float gate in gates)
        {
            float x = cx + Mathf.Cos(gate) * rout, y = cy + Mathf.Sin(gate) * rout, a = gate;
            for (int step = 0; step < 60; step++)
            {
                a += (H(Mathf.RoundToInt(gate * 100f), step, 101) - 0.5f) * 0.25f;
                x += Mathf.Cos(a);
                y += Mathf.Sin(a);
                if (Outside(x, y)) Put(x, y, TribePath, KPath);
            }
        }
        (int x, int y) Around(int k, float min, float max, int salt)
        {
            float a = H(k, salt) * Mathf.PI * 2f, r = rout + min + H(k, salt + 1) * (max - min);
            return (Mathf.RoundToInt(cx + Mathf.Cos(a) * r), Mathf.RoundToInt(cy + Mathf.Sin(a) * r));
        }
        for (int k = 0; k < 14 + stage * 6; k++)
        {
            (int x0, int y0) = Around(k, 8f, 46f, 110);
            int fw = 10 + (int)(H(k, 112) * 10f), fh = 6 + (int)(H(k, 113) * 6f);
            bool horizontal = H(k, 114) < 0.5f;
            for (int y = y0; y < y0 + fh; y++)
            for (int x = x0; x < x0 + fw; x++)
                if (Outside(x, y) && c.Tile(x, top - y)?.zone?.city == c.City)
                    Put(x, y, (horizontal ? y : x) % 3 == 0 ? TribeMilletLine : TribeMillet, KEmpty);
        }
        {
            (int x0, int y0) = Around(1, 3f, 10f, 120);
            for (int j = 0; j < 4; j++)
            for (int i = 0; i < 6; i++)
            {
                int x = x0 + i * 3, y = y0 + j * 4;
                if (H(i, j, 121) < 0.25f || !Outside(x, y) || !Outside(x, y + 1)) continue;
                Put(x, y, TribeGrave, KEmpty);
                Put(x, y + 1, TribeGraveDark, KEmpty);
            }
        }
        for (int k = 0; k < (stage == 0 ? 3 : 1); k++)
        {
            (int x, int y) = Around(k, 2f, 7f, 130);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (Outside(x + dx, y + dy)) Put(x + dx, y + dy, dx == 0 && dy == 0 ? TribeKilnHot : TribeKiln, KEmpty);
        }
        for (int k = 0; k < 2; k++)
        {
            (int x0, int y0) = Around(k, 3f, 12f, 140);
            for (int dy = 0; dy < 6; dy++)
            for (int dx = 0; dx < 8; dx++)
            {
                if (!Outside(x0 + dx, y0 + dy)) continue;
                bool rim = dx == 0 || dy == 0 || dx == 7 || dy == 5;
                if (rim && (dx + dy) % 2 == 0) Put(x0 + dx, y0 + dy, TribeFence, KEmpty);
                else if (!rim && H(k, dx * 16 + dy, 141) < 0.18f) Put(x0 + dx, y0 + dy, TribePig, KEmpty);
            }
        }
        // 小路(见 UrbanFabric.PlaceTrails)
        foreach (Vector2[] trail in g.Paths)
            for (int k = 0; k + 1 < trail.Length; k++)
            {
                Vector2 a = trail[k], b = trail[k + 1];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * s * 1.5f));
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)n);
                    float x = (p.x - c.X0 + 0.5f) * s, y = top - (p.y - c.Y0 + 0.5f) * s;
                    if (Outside(x, y)) Put(x, y, TribePath, KPath);
                }
            }
        // 城外的小村：几座圆屋围着一小片空地，没有壕
        int index = 0;
        foreach ((int vx, int vy) in g.Villages)
        {
            index++;
            float x0 = (vx - c.X0) * s + 2, y0 = top - ((vy - c.Y0) * s + 2);
            for (int y = Mathf.FloorToInt(y0 - 9); y <= y0 + 9; y++)
            for (int x = Mathf.FloorToInt(x0 - 9); x <= x0 + 9; x++)
                if ((x - x0) * (x - x0) + (y - y0) * (y - y0) <= 81f && KindAt(x, y) is KEmpty or KPath && LandAt(x, y))
                    Put(x, y, TribeGround, KGround);
            for (int y = Mathf.FloorToInt(y0 - 3); y <= y0 + 3; y++)
            for (int x = Mathf.FloorToInt(x0 - 3); x <= x0 + 3; x++)
                if ((x - x0) * (x - x0) + (y - y0) * (y - y0) <= 9f && Free(x, y)) Put(x, y, TribePlaza, KPlaza);
            int count = 5 + (int)(H(index, 150) * 3f);
            for (int j = 0; j < count; j++)
            {
                float a = j / (float)count * Mathf.PI * 2f + H(index, 151);
                // 小屋离中心五格半：屋连同一圈留空都落在踩出来的空地里
                Hut(Mathf.RoundToInt(x0 + Mathf.Cos(a) * 5.5f), Mathf.RoundToInt(y0 + Mathf.Sin(a) * 5.5f), 2, x0, y0);
            }
        }
    }

    // 平面图(一格一像素)的原始聚落：环壕或围垣一圈、里面一个个小屋点、正中广场；不在聚落里的格返回 null
    private static Color32? TribalCoarse(WorldTile tile, Color32 color, CityPlan plan)
    {
        if (plan == null || plan.tribal < 0) return null;
        Vector2 center = TribalCenter(plan);
        int stage = plan.tribal;
        float rin = TribalRadius(plan) * (stage == 0 ? 0.82f : 0.86f) / DetailScale;
        long seed = plan.fabric?.Seed ?? 0L;
        float dx = tile.x - center.x, dy = -(tile.y - center.y), d = TribalShape(stage, dx, dy, seed);
        if (d > rin + 1.6f) return null;
        if (d > rin) return Lerp(color, stage == 0 ? TribeDitch : d < rin + 0.9f ? TribeWallTop : TribeDitch, 0.85f);
        float rp = Mathf.Max(stage == 0 ? 6f : 7f, rin * DetailScale * (stage == 0 ? 0.24f : 0.2f)) / DetailScale;
        if (dx * dx + dy * dy <= rp * rp) return Lerp(color, TribePlaza, 0.85f);
        return DetailHash(seed, tile.x, tile.y, 160) < 0.35f ? Lerp(color, TribeThatch, 0.85f) : Lerp(color, TribeGround, 0.7f);
    }
}
