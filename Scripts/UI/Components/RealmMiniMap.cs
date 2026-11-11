using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 天下简图的底子：把地图按格取样成一张小图(每格归哪国、哪座城)，宽窗口里按国上色的简化地图都从这里取
// (发起诉求选目标 ClaimTargetWindow；外交界面的关系图、两国位置、宗主一系，用户 2026-11-11 "对了外交界面也可以加这个缩略图模式"
// "这样会很清晰")。画到界面上用 ClaimTargetMap(点采样的小图，点哪一格、指着哪一格回调给窗口，滚轮放大、拖动平移)。
// 可以取整张图，也可以只取一片(Snapshot(区域))：世界地图上两个小国在整图里只有几格，看不清(用户 2026-11-11 "这个图我希望能放大"
// "看更多细节""还有缩小")——窗口里的图先对准相关国家所在的那一片(Region)，滚轮每放大、缩小一档就按新的范围重新取样
// (同样大的图上格子更细，小国、城都显出来)，最远退到包住整张图的那一片(Whole)；区域伸出地图之外的部分当水。
// 快照过 Seconds 秒再用就重取；取样只读地块的归属，不碰原版的地图图层
public static class RealmMiniMap
{
    public const int MaxWidth = 592, MaxHeight = 300;
    private const float Seconds = 20f;
    // 对准相关国家：它们的范围四周留的边(占范围的比例、至少几格地)
    private const float RegionMargin = 0.3f;
    private const int RegionMinMargin = 24;

    // 每格归哪国(-1 无主陆地、-2 水)、哪座城(-1 没有)；Kingdoms / Cities 是图上出现的国与城，Owner / City 里存的是它们的下标。
    // 第 (x, y) 格取的是地图上 (X0 + x × Step, Y0 + y × Step) 起的那一小块
    public sealed class MapData
    {
        public MapBox World;
        public float At;
        public int Width, Height, Step, X0, Y0;
        public RectInt Tiles;
        public int[] Owner, City;
        public readonly List<Kingdom> Kingdoms = new();
        public readonly List<City> Cities = new();
        public readonly Dictionary<long, int> KingdomIndex = new(), CityIndex = new();
        // 各国在图上占的范围(格)：第 i 国是 [4i] 最左、[4i+1] 最下、[4i+2] 最右、[4i+3] 最上；一格都没有的最左大于最右
        private int[] _bounds;

        public int[] Bounds
        {
            get
            {
                if (_bounds != null) return _bounds;
                var bounds = new int[Kingdoms.Count * 4];
                for (int i = 0; i < Kingdoms.Count; i++)
                {
                    bounds[i * 4] = bounds[i * 4 + 1] = int.MaxValue;
                    bounds[i * 4 + 2] = bounds[i * 4 + 3] = int.MinValue;
                }
                for (int at = 0; at < Owner.Length; at++)
                {
                    int owner = Owner[at];
                    if (owner < 0) continue;
                    int x = at % Width, y = at / Width, k = owner * 4;
                    if (x < bounds[k]) bounds[k] = x;
                    if (y < bounds[k + 1]) bounds[k + 1] = y;
                    if (x > bounds[k + 2]) bounds[k + 2] = x;
                    if (y > bounds[k + 3]) bounds[k + 3] = y;
                }
                return _bounds = bounds;
            }
        }
    }

    // 按国上色的结果：像素，各国在图上占的格数与格坐标之和(放名字用：Sum ÷ Count = 重心)
    public sealed class Painted
    {
        public Color32[] Pixels;
        public float[] SumX, SumY;
        public int[] Count;
    }

    private static MapData _map, _region;

    // 下回 Snapshot 重取(窗口刚打开时)
    public static void Invalidate() => _map = _region = null;

    private static bool Fresh(MapData map, MapBox world, float now) =>
        map != null && ReferenceEquals(map.World, world) && now - map.At < Seconds && now >= map.At;

    // 整张图
    public static MapData Snapshot()
    {
        MapBox world = World.world;
        float now = Time.realtimeSinceStartup;
        if (Fresh(_map, world, now)) return _map;
        return _map = Build(world, now, new RectInt(0, 0, MapBox.width, MapBox.height));
    }

    // 只取一片(地块坐标，可以伸出地图之外)；同一片在 Seconds 秒内复用
    public static MapData Snapshot(RectInt tiles)
    {
        MapBox world = World.world;
        float now = Time.realtimeSinceStartup;
        tiles = new RectInt(tiles.x, tiles.y, Mathf.Max(1, tiles.width), Mathf.Max(1, tiles.height));
        if (Fresh(_region, world, now) && _region.Tiles.Equals(tiles)) return _region;
        return _region = Build(world, now, tiles);
    }

    // 形状是 aspect(宽 ÷ 高)、包得住整张地图的那一片：缩到最小时的视野
    public static RectInt Whole(float aspect)
    {
        int width = Mathf.Max(MapBox.width, Mathf.CeilToInt(MapBox.height * aspect)), height = Mathf.Max(MapBox.height, Mathf.CeilToInt(width / aspect));
        return new RectInt((MapBox.width - width) / 2, (MapBox.height - height) / 2, width, height);
    }

    private static MapData Build(MapBox world, float now, RectInt tiles)
    {
        int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(tiles.width / (float)MaxWidth, tiles.height / (float)MaxHeight)));
        var map = new MapData
        {
            World = world, At = now, Step = step, X0 = tiles.x, Y0 = tiles.y, Tiles = tiles,
            Width = Mathf.Max(1, tiles.width / step), Height = Mathf.Max(1, tiles.height / step)
        };
        map.Owner = new int[map.Width * map.Height];
        map.City = new int[map.Width * map.Height];
        // 每块地归哪国、哪座城只判一回(城的地块按城，并进国土的无人区按主人)
        var zoneOwner = new Dictionary<TileZone, (int kingdom, int city)>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            int at = y * map.Width + x;
            int tileX = tiles.x + x * step + step / 2, tileY = tiles.y + y * step + step / 2;
            WorldTile tile = tileX < 0 || tileY < 0 || tileX >= MapBox.width || tileY >= MapBox.height ? null : world.GetTile(tileX, tileY);
            map.City[at] = -1;
            if (tile?.Type == null || tile.Type.liquid || tile.zone == null)
            {
                map.Owner[at] = -2;
                continue;
            }
            if (!zoneOwner.TryGetValue(tile.zone, out (int kingdom, int city) owner))
            {
                City city = tile.zone.city;
                if (city?.data == null || city.isRekt()) city = null;
                Kingdom kingdom = city?.kingdom ?? WildernessSystem.OwnerOf(tile.zone);
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild) kingdom = null;
                int kingdomIndex = -1, cityIndex = -1;
                if (kingdom != null && !map.KingdomIndex.TryGetValue(kingdom.id, out kingdomIndex))
                {
                    map.KingdomIndex[kingdom.id] = kingdomIndex = map.Kingdoms.Count;
                    map.Kingdoms.Add(kingdom);
                }
                if (city != null && kingdom != null && !map.CityIndex.TryGetValue(city.id, out cityIndex))
                {
                    map.CityIndex[city.id] = cityIndex = map.Cities.Count;
                    map.Cities.Add(city);
                }
                zoneOwner[tile.zone] = owner = (kingdomIndex, cityIndex);
            }
            map.Owner[at] = owner.kingdom;
            map.City[at] = owner.city;
        }
        return map;
    }

    // 这些国家在整张图(full)上占的那一片，四周留边、凑成 aspect(宽 ÷ 高)的形状，换成地块坐标。
    // 它们不在图上、或这一片比整张图还大：给 Whole
    public static RectInt Region(MapData full, IEnumerable<Kingdom> kingdoms, float aspect)
    {
        RectInt whole = Whole(aspect);
        if (full == null || kingdoms == null) return whole;
        int[] bounds = full.Bounds;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Kingdom kingdom in kingdoms)
        {
            if (kingdom == null || !full.KingdomIndex.TryGetValue(kingdom.id, out int index) || bounds[index * 4] > bounds[index * 4 + 2]) continue;
            minX = Math.Min(minX, bounds[index * 4]);
            minY = Math.Min(minY, bounds[index * 4 + 1]);
            maxX = Math.Max(maxX, bounds[index * 4 + 2]);
            maxY = Math.Max(maxY, bounds[index * 4 + 3]);
        }
        if (minX > maxX) return whole;
        float width = (maxX - minX + 1) * full.Step, height = (maxY - minY + 1) * full.Step;
        float centerX = full.X0 + minX * full.Step + width / 2f, centerY = full.Y0 + minY * full.Step + height / 2f;
        width += Mathf.Max(width * RegionMargin, RegionMinMargin) * 2f;
        height += Mathf.Max(height * RegionMargin, RegionMinMargin) * 2f;
        if (width / height < aspect) width = height * aspect;
        else height = width / aspect;
        if (width >= whole.width) return whole;
        int w = Mathf.Max(1, Mathf.RoundToInt(width)), h = Mathf.Max(1, Mathf.RoundToInt(height));
        int x0 = Mathf.Clamp(Mathf.RoundToInt(centerX - width / 2f), whole.x, whole.xMax - w);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(centerY - height / 2f), whole.y, whole.yMax - h);
        return new RectInt(x0, y0, w, h);
    }

    public static readonly Color32 Water = new(28, 38, 44, 255), Wild = new(52, 57, 46, 255), Grey = new(98, 104, 96, 255),
        OwnGold = new(201, 164, 74, 255), OwnGoldDark = new(184, 145, 58, 255), Dim = new(70, 75, 68, 255),
        Cyan = new(127, 216, 234, 255), MarkerGrey = new(138, 143, 128, 255), Ink = new(17, 18, 14, 255), White = new(255, 255, 255, 255);

    public static Color32 Mix(Color32 a, Color32 b, float t) => new(
        (byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), 255);

    public static Color32 KingdomColor(Kingdom kingdom)
    {
        try
        {
            ColorAsset color = kingdom?.getColor();
            if (color != null)
            {
                Color32 main = color.getColorMain32();
                main.a = 255;
                return main;
            }
        }
        catch (Exception)
        {
            // 国色还没备好：用灰
        }
        return Grey;
    }

    // 按国上色的整图。color[i] 第 i 国的颜色；group[i] 画边线时它算哪一组(填组里某一国的下标，自成一组填 i)：
    // 同组之间不画线，不同组之间描深线；white[g] 为真的组外沿描白框(已选、对方)
    public static Painted Paint(MapData map, Color32[] color, int[] group, bool[] white)
    {
        int width = map.Width, height = map.Height, kingdoms = map.Kingdoms.Count;
        var painted = new Painted
        {
            Pixels = new Color32[width * height], SumX = new float[kingdoms], SumY = new float[kingdoms], Count = new int[kingdoms]
        };
        Color32[] pixels = painted.Pixels;
        var cell = new int[width * height];   // 水 -2、无主 -1、其余是组
        for (int at = 0; at < pixels.Length; at++)
        {
            int owner = map.Owner[at];
            if (owner < 0)
            {
                pixels[at] = owner == -2 ? Water : Wild;
                cell[at] = owner;
                continue;
            }
            pixels[at] = color[owner];
            cell[at] = group[owner];
            painted.SumX[owner] += at % width;
            painted.SumY[owner] += at / width;
            painted.Count[owner]++;
        }
        var lit = new bool[pixels.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int at = y * width + x, g = cell[at];
            for (int side = 0; side < 2; side++)
            {
                int other = side == 0 ? (x + 1 < width ? at + 1 : -1) : (y + 1 < height ? at + width : -1);
                if (other < 0 || cell[other] == g) continue;
                int h = cell[other];
                if (g >= 0 && white[g] || h >= 0 && white[h])
                {
                    pixels[at] = White;
                    pixels[other] = White;
                    lit[at] = lit[other] = true;
                }
                else if ((g >= 0 || h >= 0) && !lit[at]) pixels[at] = Mix(pixels[at], Ink, 0.55f);
            }
        }
        return painted;
    }
}
