// 地块表：服务器自己定的一份。名字同剧本、地形数据里用的一致(deep_ocean、soil_low、grass_high……)，
// 各项属性(是不是水、能不能种、起始高度、图上什么颜色)是这里给的。
//# hand TileLibrary: setListTo
using System;
using UnityEngine;

public partial class TileLibrary
{
    public void setListTo(DepthGeneratorType pVal)
    {
    }
}

public partial class TopTileLibrary
{
    // 没列在表里的表层，名字是"某某_low / 某某_high"的照陆地表层现配一个，归到同名的生物群系
    public override TopTileType get(string pID)
    {
        if (pID == null) return null;
        if (dict.TryGetValue(pID, out TopTileType found)) return found;
        bool low = pID.EndsWith("_low"), high = pID.EndsWith("_high");
        if (!low && !high) return base.get(pID);
        string biome = pID.Substring(0, pID.LastIndexOf('_'));
        return ShimTiles.Top(pID, "#7fa24f", "biome_" + biome, low ? 108 : 128, true);
    }
}

internal static class ShimTiles
{
    private static int _index;

    private static T Fill<T>(T type, string id, string color, int height) where T : TileTypeBase
    {
        type.id = id;
        type.index_id = _index++;
        type.color_hex = color;
        ColorUtility.TryParseHtmlString(color, out Color parsed);
        type.color = parsed;
        type.height_min = height;
        type.additional_height = Array.Empty<int>();
        type.used_in_generator = true;
        type.cost = 1;
        return type;
    }

    private static TileType Main(string id, string color, int height, Action<TileType> shape)
    {
        TileType type = Fill(new TileType(), id, color, height);
        shape(type);
        return AssetManager.tiles.add(type);
    }

    internal static TopTileType Top(string id, string color, string biome, int height, bool farm, Action<TopTileType> shape = null)
    {
        TopTileType type = Fill(new TopTileType(), id, color, height);
        type.ground = true;
        type.can_build_on = true;
        type.can_be_farm = farm;
        type.can_be_biome = true;
        type.biome_id = biome ?? "";
        shape?.Invoke(type);
        return AssetManager.top_tiles.add(type);
    }

    internal static void Init()
    {
        // 主层：从深海到雪顶
        Main("deep_ocean", "#2c5688", 0, t => t.liquid = t.ocean = true);
        Main("close_ocean", "#3a6ea5", 30, t => t.liquid = t.ocean = true);
        Main("shallow_waters", "#4f86bd", 70, t => t.liquid = t.ocean = true);
        Main("sand", "#d9c98a", 98, t =>
        {
            t.ground = t.sand = t.can_build_on = true;
            t.biome_id = "biome_sand";
        });
        Main("soil_low", "#8a7a52", 108, t => t.ground = t.can_build_on = t.can_be_farm = t.can_be_biome = true);
        Main("soil_high", "#7d6d48", 128, t => t.ground = t.can_build_on = t.can_be_farm = t.can_be_biome = true);
        TileLibrary.hills = Main("hills", "#8d8468", 199, t =>
        {
            t.ground = t.rocks = t.can_build_on = true;
            t.cost = 3;
            t.biome_id = "biome_hill";
        });
        TileLibrary.mountains = Main("mountains", "#6e6a66", 210, t =>
        {
            t.mountains = t.block = true;
            t.cost = 12;
        });
        TileLibrary.summit = Main("summit", "#9a9794", 230, t =>
        {
            t.mountains = t.summit = t.block = true;
            t.cost = 20;
        });
        foreach (string lava in new[] { "lava", "lava0", "lava1", "lava2", "lava3" })
            Main(lava, "#d8532a", 98, t => t.liquid = t.lava = true);
        for (int i = 0; i < AssetManager.tiles.list.Count; i++)
        {
            TileType type = AssetManager.tiles.list[i];
            if (type.lava) continue;
            TileType down = i > 0 ? AssetManager.tiles.list[i - 1] : null, up = i + 1 < AssetManager.tiles.list.Count ? AssetManager.tiles.list[i + 1] : null;
            if (down != null && !down.lava)
            {
                type.decrease_to = down;
                type.decrease_to_id = down.id;
            }
            if (up != null && !up.lava)
            {
                type.increase_to = up;
                type.increase_to_id = up.id;
            }
        }

        // 表层：各种地貌，低地高地各一
        (string name, string biome, string low, string high, bool farm)[] biomes =
        {
            ("grass", "biome_grass", "#7fb24a", "#6fa046", true), ("savanna", "biome_savanna", "#b5ad5a", "#a59d52", true),
            ("jungle", "biome_jungle", "#3f8f3a", "#377f34", true), ("swamp", "biome_swamp", "#5d7a4a", "#526d43", false),
            ("desert", "biome_desert", "#e2c98a", "#d4bb7c", false), ("frozen", "biome_permafrost", "#cfdde2", "#c1d0d6", false),
            ("permafrost", "biome_permafrost", "#cfdde2", "#c1d0d6", false), ("wasteland", "biome_wasteland", "#8b8a6b", "#7d7c60", false),
            ("enchanted", "biome_enchanted", "#8fc6a0", "#7fb690", true), ("corrupted", "biome_corrupted", "#6b5a78", "#5e4f6a", false),
            ("infernal", "biome_infernal", "#8a4a3a", "#7a4133", false), ("mushroom", "biome_mushroom", "#a07a8a", "#906c7c", false),
            ("birch", "biome_birch", "#9cc06a", "#8cb060", true), ("maple", "biome_maple", "#b98a4a", "#a97c42", true),
            ("pine", "biome_pine", "#4f7f4a", "#467243", true), ("forest", "biome_forest", "#4f8a3a", "#467c34", true),
            ("flower", "biome_flower", "#9fc46a", "#8fb460", true), ("clover", "biome_clover", "#74b85a", "#68a850", true),
            ("rocklands", "biome_rocklands", "#8f8a7a", "#817c6d", false), ("celestial", "biome_celestial", "#a8b8d8", "#98a8c8", false),
            ("crystal", "biome_crystal", "#8fd0d8", "#80c0c8", false), ("candy", "biome_candy", "#e0a0c0", "#d090b0", false),
            ("lemon", "biome_lemon", "#d8d060", "#c8c054", true), ("biomass", "biome_biomass", "#7a9a4a", "#6d8a42", false),
            ("tumor", "biome_tumor", "#a0607a", "#90546c", false), ("seed", "biome_seed", "#8ab05a", "#7ca050", true)
        };
        foreach ((string name, string biome, string low, string high, bool farm) in biomes)
        {
            Top(name + "_low", low, biome, 108, farm);
            Top(name + "_high", high, biome, 128, farm);
        }
        Top("snow_sand", "#e8eef0", "biome_permafrost", 98, false, t => t.sand = true);
        Top("snow_hills", "#dfe6e9", "biome_hill", 199, false, t =>
        {
            t.rocks = true;
            t.cost = 3;
        });
        TopTileLibrary.snow_block = Top("snow_block", "#eef2f4", "", 210, false, t =>
        {
            t.ground = t.can_build_on = false;
            t.mountains = t.block = true;
            t.cost = 12;
        });
        TopTileLibrary.snow_summit = Top("snow_summit", "#f7f9fa", "", 230, false, t =>
        {
            t.ground = t.can_build_on = false;
            t.mountains = t.summit = t.block = true;
            t.cost = 20;
        });
        TopTileLibrary.field = Top("field", "#c9b26a", "", 108, false, t => t.farm_field = true);
        Top("road", "#a89878", "", 108, false, t => t.road = true);
    }
}
