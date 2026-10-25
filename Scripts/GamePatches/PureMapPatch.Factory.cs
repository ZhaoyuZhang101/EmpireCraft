using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 工厂区的画法(见 FactorySystem)：一整片成品——围墙、南面大门、厂内道路，路两边一排排厂房，按门类画：
//   火药厂是土堤隔开的几间小库房；钢铁厂是高炉加长厂房；轻工厂是锯齿屋顶的车间；船厂是临水的滑道和船体(重型的船大、有行车)；
//   重工厂是大厂房、两根烟囱、门式起重机；飞机厂是拱顶机库加停机坪。等级越高厂房占的地越满；布局按厂区自己的种子随机。
//   粗图(一格一像素)：围墙、厂内道路、屋顶、烟囱点。
//   区里没有近代厂(只有古代手工工场、火药坊)的画成作坊院(用户 2026-10-22 "工厂区在古代为什么这么现代的感觉"，
//   问答选"照各城的文化系")：夯土墙(平顶土屋的文化是土坯墙)、夯土地坪、土路，屋顶用本城文化系的瓦色，粗图不画烟囱点、不通铁路。
public partial class PureMapPatch
{
    private static readonly Color32 FactoryFence = new(70, 70, 74, 255), FactoryRoad = new(92, 92, 96, 255),
        FactoryYard = new(150, 148, 140, 255), FactoryRoofA = new(118, 124, 132, 255), FactoryRoofB = new(150, 156, 164, 255),
        FactoryChimney = new(52, 46, 44, 255);
    // 作坊院：夯土墙(土坯墙)、夯土地坪、土路
    private static readonly Color32 WorkshopWall = new(138, 116, 88, 255), WorkshopMudWall = new(198, 176, 136, 255),
        WorkshopYard = new(178, 158, 122, 255), WorkshopRoad = new(160, 138, 104, 255);

    private static long FactoryKey(FactorySite site) => site.seed + site.id * 7919L + UrbanFabric.PosKey(site.x, site.y);

    // 区里没有近代厂：画成作坊院
    private static bool IsWorkshopYard(FactorySite site) => !site.units.Exists(unit => FactorySystem.IsModern(unit.type));

    // 本城文化系的画法(瓦色、是不是平顶土屋)；营地、没有的按华夏
    private static DetailStyle WorkshopStyle(FactorySite site)
    {
        City city = FactorySystem.CityOf(site);
        DetailStyle style = city?.data == null ? null : StyleOf(CityStyleSystem.Established(city));
        return style == null || style.Camp ? Styles["huaxia"] : style;
    }

    // 粗图上的一格
    private static Color32? FactoryCoarse(WorldTile tile, Color32 color, CityPlan plan)
    {
        foreach (FactorySite site in plan.factories)
        {
            if (!FactorySystem.Covers(site, tile.x, tile.y)) continue;
            (int x0, int y0, int side) = FactorySystem.Footprint(site);
            int lx = tile.x - x0, ly = tile.y - y0;
            if (IsWorkshopYard(site))
            {
                DetailStyle style = WorkshopStyle(site);
                Color32 wall = style.FlatRoofs ? WorkshopMudWall : WorkshopWall;
                if (lx == 0 || ly == 0 || lx == side - 1 || ly == side - 1)
                    return lx == side / 2 && ly == 0 ? Lerp(color, WorkshopRoad, 0.85f) : Lerp(color, wall, 0.85f);
                if (ly == side / 2 || lx == side / 2 && ly < side / 2) return Lerp(color, WorkshopRoad, 0.85f);
                Color32[] roofs = style.Roofs;
                return Lerp(color, roofs[(int)(DetailHash(FactoryKey(site), lx, ly, 803) * roofs.Length) % roofs.Length], 0.85f);
            }
            if (lx == 0 || ly == 0 || lx == side - 1 || ly == side - 1)
                return lx == side / 2 && ly == 0 ? Lerp(color, FactoryRoad, 0.85f) : Lerp(color, FactoryFence, 0.85f);
            if (ly == side / 2 || lx == side / 2 && ly < side / 2) return Lerp(color, FactoryRoad, 0.85f);
            if (DetailHash(FactoryKey(site), lx, ly, 801) < 0.1f) return Lerp(color, FactoryChimney, 0.9f);
            return Lerp(color, (lx + ly) % 2 == 0 ? FactoryRoofA : FactoryRoofB, 0.85f);
        }
        return null;
    }

    // 精细图
    private static void DrawFactories(Canvas c, CityPlan plan)
    {
        int s = DetailScale;
        foreach (FactorySite site in plan.factories)
        {
            (int tx0, int ty0, int side) = FactorySystem.Footprint(site);
            int x0 = (tx0 - c.X0) * s, y0 = (ty0 - c.Y0) * s, w = side * s;
            long key = FactoryKey(site);
            bool Dry(int px, int py)
            {
                WorldTile tile = c.Tile(px, py);
                return c.In(px, py) && tile?.Type != null && !tile.Type.liquid;
            }
            void Put(int px, int py, Color32 color)
            {
                if (Dry(px, py)) c.Put(px, py, color, KFixed);
            }
            // 作坊院(区里没有近代厂)：夯土墙、夯土地坪、土路，屋顶照本城文化系；近代厂区是水泥地、铁栅、柏油路
            bool workshop = IsWorkshopYard(site);
            DetailStyle style = workshop ? WorkshopStyle(site) : null;
            Color32 yard = workshop ? WorkshopYard : FactoryYard;
            Color32 fence = workshop ? style.FlatRoofs ? WorkshopMudWall : WorkshopWall : FactoryFence;
            Color32 road = workshop ? WorkshopRoad : FactoryRoad;
            // 地坪(水泥地或夯土，一点斑驳)
            for (int py = y0; py < y0 + w; py++)
            for (int px = x0; px < x0 + w; px++)
                Put(px, py, DetailHash(key, px, py, 802) < 0.15f ? Darker(yard, 0.92f) : yard);
            // 围墙，南面正中开大门
            int gate = x0 + w / 2;
            for (int k = 0; k < w; k++)
            {
                Put(x0 + k, y0 + w - 1, fence);
                Put(x0, y0 + k, fence);
                Put(x0 + w - 1, y0 + k, fence);
                if (Mathf.Abs(x0 + k - gate) > 1) Put(x0 + k, y0, fence);
            }
            // 厂内道路：大门往北一条，正中横贯一条
            int roadY = y0 + w / 2;
            for (int px = x0 + 1; px < x0 + w - 1; px++)
            {
                Put(px, roadY, road);
                Put(px, roadY - 1, road);
            }
            for (int py = y0; py < roadY; py++)
            {
                Put(gate, py, road);
                Put(gate - 1, py, road);
            }
            // 有钢铁厂、重工厂的通一条铁路专用线(沿北墙内侧)
            if (site.units.Exists(unit => unit.type is "steel_mill" or "heavy_factory"))
                for (int px = x0 + 1; px < x0 + w - 1; px++)
                {
                    Put(px, y0 + w - 3, px % 2 == 0 ? Rgb(96, 80, 64) : Rgb(60, 60, 64));
                    Put(px, y0 + w - 2, Rgb(150, 148, 140));
                }
            // 厂房：路北、路南各一排，按厂的多少等分
            int count = site.units.Count;
            if (count == 0) continue;
            int perRow = (count + 1) / 2;
            int slotWidth = Mathf.Max(3, (w - 4 - (perRow - 1)) / perRow);
            for (int i = 0; i < count; i++)
            {
                int row = i % 2, column = i / 2;
                int sx0 = x0 + 2 + column * (slotWidth + 1), sx1 = sx0 + slotWidth - 1;
                int sy0 = row == 0 ? roadY + 2 : y0 + 2, sy1 = row == 0 ? y0 + w - 5 : roadY - 3;
                if (row == 0 && !site.units.Exists(unit => unit.type is "steel_mill" or "heavy_factory")) sy1 = y0 + w - 3;
                if (sx1 <= sx0 || sy1 <= sy0) continue;
                DrawWorks(c, site.units[i], sx0, sy0, sx1, sy1, key + i * 131L, Put, style);
            }
        }
    }

    private delegate void PutPixel(int px, int py, Color32 color);

    // 一座厂：在 [x0, x1]×[y0, y1] 这一格位里画；等级越高占得越满
    // style：作坊院的文化系画法(铸钱局、印书坊的屋顶用它的瓦色)；近代厂区为空
    private static void DrawWorks(Canvas c, FactoryUnit unit, int x0, int y0, int x1, int y1, long key, PutPixel put,
        DetailStyle style = null)
    {
        Color32 tile = style == null ? Rgb(108, 104, 98) : style.Roofs[(int)(DetailHash(key, 3) * style.Roofs.Length) % style.Roofs.Length];
        float fill = 0.55f + 0.15f * Mathf.Clamp(unit.level, 1, 3);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        int bw = Mathf.Max(2, Mathf.RoundToInt(w * fill)), bh = Mathf.Max(2, Mathf.RoundToInt(h * fill));
        bool left = DetailHash(key, 1) < 0.5f;
        int bx0 = left ? x0 : x1 - bw + 1, by0 = y0 + (h - bh) / 2;
        void Rect(int ax0, int ay0, int aw, int ah, Color32 color)
        {
            for (int py = ay0; py < ay0 + ah; py++)
            for (int px = ax0; px < ax0 + aw; px++)
                if (px >= x0 && px <= x1 && py >= y0 && py <= y1) put(px, py, color);
        }
        void Smoke(int px, int py)
        {
            for (int k = 1; k <= 3; k++)
                if (c.In(px + (k > 1 ? 1 : 0), py + k)) c.Put(px + (k > 1 ? 1 : 0), py + k, new Color32(230, 230, 230, (byte)(170 - k * 40)), KFixed);
        }
        void Chimney(int px, int py)
        {
            Rect(px, py, 1, 3, FactoryChimney);
            Smoke(px, py + 2);
        }
        switch (unit.type)
        {
            case "kiln":
            {
                // 馒头窑：一两座土红的窑包，窑口一点火光，窑顶冒烟；旁边码着青瓦
                int kilns = unit.level >= 3 && bw >= 5 ? 2 : 1;
                int kw = Mathf.Max(2, (bw - (kilns - 1)) / kilns);
                for (int k = 0; k < kilns; k++)
                {
                    int kx = bx0 + k * (kw + 1);
                    Rect(kx, by0, kw, bh, Rgb(156, 96, 66));
                    Rect(kx, by0 + bh - 1, kw, 1, Rgb(178, 116, 80));
                    put(kx + kw / 2, by0, Rgb(236, 128, 40));
                    Smoke(kx + kw / 2, by0 + bh - 1);
                }
                Rect(left ? x1 : x0, y0, 1, Mathf.Min(2, h), Rgb(84, 92, 100));
                break;
            }
            case "mint":
            {
                // 铸钱局：瓦顶院落(瓦色照本城文化系)，院里几点铜色(钱范、钱串)
                Rect(bx0, by0, bw, bh, tile);
                Rect(bx0, by0 + bh - 1, bw, 1, Darker(tile, 0.8f));
                for (int k = 0; k < Mathf.Clamp(unit.level, 1, 3); k++)
                    put(bx0 + 1 + k * 2, by0 + Mathf.Max(0, bh / 2 - 1), Rgb(214, 176, 60));
                break;
            }
            case "paper_mill":
            {
                // 造纸坊：一道水渠，渠边一排排晾着的白纸
                Rect(x0, y0, w, 1, Rgb(108, 150, 196));
                for (int py = by0 + 1; py < by0 + bh; py += 2)
                    Rect(bx0, py, bw, 1, Rgb(232, 226, 206));
                Rect(left ? x1 : x0, y0 + 1, 1, Mathf.Max(1, h - 1), Rgb(126, 92, 62));
                break;
            }
            case "iron_foundry":
            {
                // 冶铁坊：一座矮炉(炉口通红)冒烟，旁边一间棚子
                int fx = left ? x1 - 1 : x0;
                Rect(fx, y0, 2, Mathf.Max(2, h - 1), Rgb(70, 64, 62));
                put(fx, y0 + Mathf.Max(1, h - 2), Rgb(236, 128, 40));
                Smoke(fx + 1, y0 + Mathf.Max(1, h - 2));
                Rect(left ? x0 : x0 + 3, by0, Mathf.Max(2, w - 4), bh, Rgb(126, 92, 62));
                break;
            }
            case "wooden_shipyard":
            {
                // 木船坞：一条船槽，槽里一具木船体，旁边码着木料
                Rect(x0, y0, w, h, Rgb(108, 150, 196));
                int hw = Mathf.Max(2, w - 3), hh = Mathf.Max(2, Mathf.RoundToInt(h * 0.4f));
                int hx = x0 + (w - hw) / 2, hy = y0 + (h - hh) / 2;
                Rect(hx, hy, hw, hh, Rgb(150, 108, 70));
                for (int px = hx + 1; px < hx + hw - 1; px += 2) put(px, hy + hh - 1, Rgb(110, 76, 48));
                Rect(left ? x1 : x0, y1, 1, 1, Rgb(126, 92, 62));
                break;
            }
            case "print_shop":
            {
                // 印书坊：瓦顶长屋(瓦色照本城文化系)，屋脊一道亮线，门前几摞白书
                Rect(bx0, by0, bw, bh, Darker(tile, 0.8f));
                Rect(bx0, by0 + bh - 1, bw, 1, tile);
                for (int k = 0; k < Mathf.Clamp(unit.level, 1, 3); k++)
                    put(bx0 + k * 2, by0 > y0 ? by0 - 1 : by0, Rgb(232, 226, 206));
                break;
            }
            case "gunpowder_mill":
            {
                // 土堤隔开的几间小库房，门前几只桶
                int sheds = 1 + Mathf.Clamp(unit.level, 1, 3);
                int sw = Mathf.Max(2, (w - sheds) / sheds);
                for (int k = 0; k < sheds; k++)
                {
                    int sx = x0 + k * (sw + 1);
                    Rect(sx, y0 + 1, sw, Mathf.Max(2, h - 3), Rgb(126, 92, 62));
                    Rect(sx, y0 + h - 2, sw, 1, Rgb(108, 132, 76));
                    if (DetailHash(key, k, 2) < 0.6f) put(sx, y0, Rgb(84, 60, 40));
                }
                break;
            }
            case "steel_mill":
            {
                // 高炉(暗色塔，顶上一点炉火)加一座长厂房
                int fx = left ? x1 - 2 : x0;
                Rect(fx, y0, 2, h, Rgb(58, 54, 56));
                put(fx, y0 + h - 1, Rgb(236, 128, 40));
                Smoke(fx + 1, y0 + h - 1);
                Rect(left ? x0 : x0 + 3, by0, Mathf.Max(2, w - 4), bh, Rgb(96, 100, 108));
                for (int px = left ? x0 : x0 + 3; px < (left ? x0 : x0 + 3) + w - 4; px += 2)
                    Rect(px, by0 + bh - 1, 1, 1, Rgb(140, 146, 154));
                break;
            }
            case "light_factory":
            {
                // 锯齿屋顶：明暗相间的竖条
                for (int px = bx0; px < bx0 + bw; px++)
                    Rect(px, by0, 1, bh, (px - bx0) % 3 == 0 ? Rgb(176, 182, 190) : Rgb(120, 126, 136));
                Chimney(left ? bx0 + bw : bx0 - 1, by0 + bh - 1);
                break;
            }
            case "light_shipyard":
            case "heavy_shipyard":
            {
                // 滑道：一条水槽，槽里一艘船体；重型的船大，旁边一座行车
                bool heavy = unit.type == "heavy_shipyard";
                Rect(x0, y0, w, h, Rgb(108, 150, 196));
                int hw = Mathf.Max(2, w - (heavy ? 2 : 3)), hh = Mathf.Max(2, Mathf.RoundToInt(h * (heavy ? 0.6f : 0.4f)));
                int hx = x0 + (w - hw) / 2, hy = y0 + (h - hh) / 2;
                Rect(hx, hy, hw, hh, heavy ? Rgb(110, 114, 122) : Rgb(140, 104, 70));
                if (heavy)
                {
                    Rect(x0, y1, w, 1, Rgb(214, 176, 60));
                    Rect(hx + hw / 2, hy + hh, 1, 1, Rgb(196, 60, 52));
                }
                break;
            }
            case "heavy_factory":
            {
                // 大厂房、两根烟囱、门式起重机
                Rect(bx0, by0, bw, bh, Rgb(84, 88, 96));
                for (int px = bx0; px < bx0 + bw; px += 3) Rect(px, by0, 1, bh, Rgb(110, 114, 124));
                Chimney(bx0 + 1, by0 + bh);
                Chimney(bx0 + bw - 2, by0 + bh);
                int gx = left ? x1 : x0;
                Rect(gx, y0, 1, h, Rgb(214, 176, 60));
                break;
            }
            default:
            {
                // 飞机厂：拱顶机库(由暗到亮的横条)加停机坪，坪上一架飞机
                bool heavy = unit.type == "heavy_aircraft_factory";
                int hangar = Mathf.Max(2, Mathf.RoundToInt(h * 0.55f));
                for (int py = 0; py < hangar; py++)
                {
                    float t = py / (float)Mathf.Max(1, hangar - 1);
                    Rect(bx0, y0 + py, bw, 1, Color32.Lerp(Rgb(90, 96, 104), Rgb(176, 182, 190), Mathf.Sin(t * Mathf.PI)));
                }
                int apron = y0 + hangar + 1;
                Rect(x0, apron, w, Mathf.Max(1, y1 - apron + 1), Rgb(120, 120, 124));
                int cx = x0 + w / 2, cy = apron + Mathf.Max(0, (y1 - apron) / 2);
                Color32 plane = heavy ? Rgb(196, 200, 206) : Rgb(226, 222, 210);
                Rect(cx - (heavy ? 2 : 1), cy, heavy ? 5 : 3, 1, plane);
                Rect(cx, cy - 1, 1, 3, plane);
                break;
            }
        }
    }
}
