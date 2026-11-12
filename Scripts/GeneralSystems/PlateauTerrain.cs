using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 高原(用户 2026-11-10 "给地图加个高原地形""起码得挡住敌人吧"，问答定新做原版式地面格；第二轮"这样子就能模拟蒙古高原""国界线也能更合理"
// "不然东北这一块一点屏障都没有"，加第三种)：
//   · 高寒高原 alpine_plateau(青藏、帕米尔，随机图里偏冷偏高的)：牧业、不能耕、宜居约草地三成，算牧地(水土、毡帐、放牧)；
//     战线地形、教化政令阻力按高原(TerrainSystem 的 FrontTerrain.Plateau、TerrainResistance)；算国土，开局铺给城、哪国的城都能扩进去，
//     哪座城都没领的才连成高原无人区(WildernessSystem)；
//   · 黄土高原 loess_plateau：能耕(收成打八折，见 FarmlandSystem)、宜居约七成；战线地形、教化政令阻力同上，不算牧地、不划无人区；
//   · 草原高原 steppe_plateau(蒙古高原)：牧业为主、能少量耕(收成五折)、宜居约五成，算牧地(游牧在上面不算离草)；
//     高原面是开阔草原，战线地形、教化政令照平原，不划无人区——屏障全在外缘(大兴安岭、燕山、阴山)。
//   外缘山脊、坡地、山口见 PlateauShaper(生成地图时铺一次，之后擦掉山就是新山口)；外缘两边分属行政区、开局铺地与城市扩张不翻外缘，
//   法理照史实(同长城，见 PlateauSides)。
// 格子本身：高度在原版高地(soil_high 128)与丘陵(hills 199)之间——神力抬高高地得黄土高原，再抬得高寒高原，再抬是丘陵，压低反过来(草原高原压低是高地、抬高是高寒高原)；
//   没有生物群系(原版草木不长、生物群系蔓延不改它)，能走能建；冻上时盖原版冻土(frozen_high)，化了还是高原。
//   远看按格子颜色画像素，近看用 GameResources/tiles/<id> 的 8×8 贴图(Tools/IconForge/tile_forge.py 生成)，与高地同一层。
// 存档里地块按 id 存(原版 SavedMap.tileMap)，读档前这里就注册好；卸了模组读这种档，高原格会读不出来。
public static class PlateauTerrain
{
    public const string AlpineId = "alpine_plateau", LoessId = "loess_plateau", SteppeId = "steppe_plateau";
    private const string AlpineColor = "#8F9070", LoessColor = "#B99A68", SteppeColor = "#9DA455";
    private const int LoessHeight = 160, SteppeHeight = 170, AlpineHeight = 180;

    public static TileType Alpine { get; private set; }
    public static TileType Loess { get; private set; }
    public static TileType Steppe { get; private set; }

    // 地面格是不是高原(顶层盖了冻土、田、路也照样看底下)
    public static bool IsPlateau(TileTypeBase type) =>
        type != null && (ReferenceEquals(type, Alpine) || ReferenceEquals(type, Loess) || ReferenceEquals(type, Steppe));

    // 战线地形、教化政令按高原算的两种(草原高原面是开阔草原，不算)
    public static bool IsRugged(TileTypeBase type) => type != null && (ReferenceEquals(type, Alpine) || ReferenceEquals(type, Loess));

    public static bool IsSteppe(WorldTile tile) => tile != null && Steppe != null && ReferenceEquals(tile.main_type, Steppe);

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
        Steppe = Make(library, SteppeId, SteppeColor, SteppeHeight, soilHigh, farm: true);
        Alpine = Make(library, AlpineId, AlpineColor, AlpineHeight, soilHigh, farm: false);
        // 高度链：高地 ↔ 黄土高原 ↔ 高寒高原 ↔ 丘陵；草原高原只连出去(压低是高地、抬高是高寒高原)，不从别的格抬进来
        Link(soilHigh, Loess);
        Link(Loess, Alpine);
        Link(Alpine, hills);
        Steppe.decrease_to_id = soilHigh.id;
        Steppe.decrease_to = soilHigh;
        Steppe.increase_to_id = Alpine.id;
        Steppe.increase_to = Alpine;
        // 列表照高度从低到高排(原版按高度取格子时取列表里最后一个够得着的)：排到高地后面
        Place(library, Loess, soilHigh);
        Place(library, Steppe, Loess);
        Place(library, Alpine, Steppe);
        LogService.LogInfo("[EmpireCraft][高原] 已注册高寒高原、黄土高原、草原高原三种地面格");
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
