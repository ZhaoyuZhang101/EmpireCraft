using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 天下简图的底子：整张图按格取样成一张小图(每格归哪国、哪座城)，宽窗口里按国上色的简化地图都从这里取
// (发起诉求选目标 ClaimTargetWindow；外交界面的关系图、两国位置、宗主一系，用户 2026-11-11 "对了外交界面也可以加这个缩略图模式"
// "这样会很清晰")。画到界面上用 ClaimTargetMap(点采样的小图，点哪一格、指着哪一格回调给窗口)。
// 快照打开窗口时取一回，过 Seconds 秒再用就重取；取样只读地块的归属，不碰原版的地图图层
public static class RealmMiniMap
{
    public const int MaxWidth = 592, MaxHeight = 300;
    private const float Seconds = 20f;

    // 每格归哪国(-1 无主陆地、-2 水)、哪座城(-1 没有)；Kingdoms / Cities 是图上出现的国与城，Owner / City 里存的是它们的下标
    public sealed class MapData
    {
        public MapBox World;
        public float At;
        public int Width, Height, Step;
        public int[] Owner, City;
        public readonly List<Kingdom> Kingdoms = new();
        public readonly List<City> Cities = new();
        public readonly Dictionary<long, int> KingdomIndex = new(), CityIndex = new();
    }

    // 按国上色的结果：像素，各国在图上占的格数与格坐标之和(放名字用：Sum ÷ Count = 重心)
    public sealed class Painted
    {
        public Color32[] Pixels;
        public float[] SumX, SumY;
        public int[] Count;
    }

    private static MapData _map;

    // 下回 Snapshot 重取(窗口刚打开时)
    public static void Invalidate() => _map = null;

    public static MapData Snapshot()
    {
        MapBox world = World.world;
        float now = Time.realtimeSinceStartup;
        if (_map != null && ReferenceEquals(_map.World, world) && now - _map.At < Seconds && now >= _map.At) return _map;
        int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(MapBox.width / (float)MaxWidth, MapBox.height / (float)MaxHeight)));
        var map = new MapData { World = world, At = now, Step = step, Width = Mathf.Max(1, MapBox.width / step), Height = Mathf.Max(1, MapBox.height / step) };
        map.Owner = new int[map.Width * map.Height];
        map.City = new int[map.Width * map.Height];
        // 每块地归哪国、哪座城只判一回(城的地块按城，并进国土的无人区按主人)
        var zoneOwner = new Dictionary<TileZone, (int kingdom, int city)>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            int at = y * map.Width + x;
            WorldTile tile = world.GetTile(Mathf.Min(MapBox.width - 1, x * step + step / 2), Mathf.Min(MapBox.height - 1, y * step + step / 2));
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
        return _map = map;
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
