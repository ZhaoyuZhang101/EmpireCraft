using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 高原(用户 2026-11-10 "给地图加个高原地形""起码得挡住敌人吧"，问答定新做原版式地面格、分两种)：
//   · 高寒高原 alpine_plateau(青藏、帕米尔，随机图里偏冷偏高的)：牧业、不能耕、宜居约草地三成；
//   · 黄土高原 loess_plateau：能耕(收成打八折，见 FarmlandSystem)、宜居约七成；
//   两种都按高原算教化政令阻力(见 TerrainResistance)、水土(见 ShuiTuSystem)、战线地形(TerrainSystem 的 FrontTerrain.Plateau)，
//   成片没城的划高原无人区(见 WildernessSystem)。外缘山脊、坡地、山口见 PlateauShaper(生成地图时铺一次，之后擦掉山就是新山口)。
// 格子本身：高度在原版高地(soil_high 128)与丘陵(hills 199)之间——神力抬高高地得黄土高原，再抬得高寒高原，再抬是丘陵，压低反过来；
//   没有生物群系(原版草木不长、生物群系蔓延不改它)，能走能建；冻上时盖原版冻土(frozen_high)，化了还是高原。
//   远看按格子颜色画像素，近看用 GameResources/tiles/<id> 的 8×8 贴图(Tools/IconForge/tile_forge.py 生成)，与高地同一层。
// 存档里地块按 id 存(原版 SavedMap.tileMap)，读档前这里就注册好；卸了模组读这种档，高原格会读不出来。
public static class PlateauTerrain
{
    public const string AlpineId = "alpine_plateau", LoessId = "loess_plateau";
    private const string AlpineColor = "#8F9070", LoessColor = "#B99A68";
    private const int LoessHeight = 160, AlpineHeight = 180;

    public static TileType Alpine { get; private set; }
    public static TileType Loess { get; private set; }

    // 地面格是不是高原(顶层盖了冻土、田、路也照样看底下)
    public static bool IsPlateau(TileTypeBase type) => type != null && (ReferenceEquals(type, Alpine) || ReferenceEquals(type, Loess));

    public static bool IsPlateau(WorldTile tile) => tile != null && IsPlateau(tile.main_type);

    public static bool IsAlpine(WorldTile tile) => tile != null && Alpine != null && ReferenceEquals(tile.main_type, Alpine);

    public static bool IsLoess(WorldTile tile) => tile != null && Loess != null && ReferenceEquals(tile.main_type, Loess);

    public static void Register()
    {
        TileLibrary library = AssetManager.tiles;
        if (library == null) return;
        if (!library.dict.TryGetValue("soil_high", out TileType soilHigh) || !library.dict.TryGetValue("hills", out TileType hills))
        {
            LogService.LogWarning("[EmpireCraft][高原] 原版没有 soil_high/hills 地面格，高原不注册");
            return;
        }
        Loess = Make(library, LoessId, LoessColor, LoessHeight, soilHigh, farm: true);
        Alpine = Make(library, AlpineId, AlpineColor, AlpineHeight, soilHigh, farm: false);
        // 高度链：高地 ↔ 黄土高原 ↔ 高寒高原 ↔ 丘陵
        Link(soilHigh, Loess);
        Link(Loess, Alpine);
        Link(Alpine, hills);
        // 列表照高度从低到高排(原版按高度取格子时取列表里最后一个够得着的)：排到高地后面
        Place(library, Loess, soilHigh);
        Place(library, Alpine, Loess);
        LogService.LogInfo("[EmpireCraft][高原] 已注册高寒高原、黄土高原两种地面格");
    }

    private static TileType Make(TileLibrary library, string id, string color, int height, TileType from, bool farm)
    {
        if (library.dict.TryGetValue(id, out TileType existing)) return existing;
        // 原版 clone 照抄公开字段(集合另建一份)，排进列表、编号、按编号的格子表都在 add 里做了
        TileType type = library.clone(id, from.id);
        type.hashset = new HashSetWorldTile();
        type.color_hex = color;
        type.color = Toolbox.makeColor(color);
        type.height_min = height;
        type.additional_height = from.additional_height == null ? null : (int[])from.additional_height.Clone();
        type.used_in_generator = false;
        type.can_be_biome = false;
        type.biome_tags = null;
        type.has_biome_tags = false;
        type.biome_build_check = false;
        type.biome_id = string.Empty;
        type.biome_asset = null;
        type.can_be_farm = farm;
        type.can_build_on = true;
        type.freeze_to_id = "frozen_high";
        type.cost = from.cost + 10;
        type.sprites = LoadSprites(id) ?? from.sprites;
        return type;
    }

    private static TileSprites LoadSprites(string id)
    {
        Sprite[] list = SpriteTextureLoader.getSpriteList("tiles/" + id);
        if (list == null || list.Length == 0)
        {
            LogService.LogWarning($"[EmpireCraft][高原] 找不到 tiles/{id} 的贴图，借用高地的");
            return null;
        }
        var sprites = new TileSprites();
        foreach (Sprite sprite in list) sprites.addVariation(sprite, id);
        return sprites;
    }

    private static void Link(TileType lower, TileType upper)
    {
        lower.increase_to_id = upper.id;
        lower.increase_to = upper;
        upper.decrease_to_id = lower.id;
        upper.decrease_to = lower;
    }

    private static void Place(TileLibrary library, TileType type, TileType after)
    {
        int at = library.list.IndexOf(after);
        if (at < 0) return;
        library.list.Remove(type);
        library.list.Insert(library.list.IndexOf(after) + 1, type);
    }
}
