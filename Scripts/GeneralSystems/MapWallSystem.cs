using System.Collections.Generic;
using System.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 图层城墙(纯净地图，用户 2026-10-14 要求)：原版地图上画的城墙(城墙类顶层格，神力画的)在纯净地图下转成模组的数据——
// 记下是哪一格、哪种城墙，再把原版的城墙格清掉(纯净地图不画建筑与城墙实体)；之后神力新画的城墙也当场转换。
// 图层城墙按像素画成长城的样子(见 MapWallOverlay)，路网与城郊的路绕开它(见 RoadNetwork、UrbanFabric)。
// 关掉纯净地图后读档，图层城墙还原成原版的城墙格，玩家画的城墙不会丢。
public static class MapWallSystem
{
    // 格编号 → 原版城墙种类(顶层格 id)，随存档保存(SaveData.map_walls)
    private static Dictionary<int, string> _walls = new();
    private static readonly Queue<WorldTile> Pending = new();
    private static object _world;
    private static bool _synced, _converting, _dirty;

    // 城墙每变一次加一(MapWallOverlay 据此重画)
    public static int Version { get; private set; }

    public static IReadOnlyDictionary<int, string> Walls => _walls;

    public static bool Contains(int tileId) => _walls.Count > 0 && _walls.ContainsKey(tileId);

    public static bool Contains(WorldTile tile) => tile?.data != null && Contains(tile.data.tile_id);

    public static void ResetWorldState()
    {
        _walls = new Dictionary<int, string>();
        Pending.Clear();
        _world = null;
        _synced = false;
        Version++;
    }

    public static void Load(Dictionary<int, string> saved)
    {
        _walls = saved ?? new Dictionary<int, string>();
        Pending.Clear();
        _world = null;
        _synced = false;
        Version++;
    }

    public static Dictionary<int, string> Save() => _walls;

    // 每帧(ModClass.Update)：读档后同步一次——纯净地图把全图的原版城墙转成图层城墙，否则把图层城墙还原；之后转换新画的
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.tiles_list == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _synced = false;
            Pending.Clear();
        }
        if (!_synced)
        {
            _synced = true;
            if (PureMapMode.Enabled) ConvertAll();
            else RestoreAll();
            return;
        }
        if (_dirty)
        {
            _dirty = false;
            Changed();
        }
        if (!PureMapMode.Enabled || Pending.Count == 0) return;
        int converted = 0;
        while (Pending.Count > 0)
            if (Convert(Pending.Dequeue())) converted++;
        if (converted > 0) Changed();
    }

    // 图层城墙被毁(玩家反馈"我手绘的城墙在国家城市图层上生成相应的图像的时候，我又城墙摧毁，但是国家城市图层上的城墙不会销毁")：
    // 纯净地图下原版的城墙格早已清掉，炸弹、地震、擦除、画别的地形、灌水灌岩浆落在这一格时，原版找不到城墙可毁，
    // 这里跟着把图层城墙去掉(见 MapWallPatch)。草木自己长回来不算；神力再画城墙照常转换。一帧里毁掉的一起重画
    public static void OnTerraform(WorldTile tile, TileType main, TopTileType top, TerraformOptions options)
    {
        if (_converting || _walls.Count == 0 || tile?.data == null || !_walls.ContainsKey(tile.data.tile_id)) return;
        if (top != null && top.wall) return;
        bool destroy = options != null && (options.destroy_buildings || options.remove_top_tile || options.explode_tile ||
                                           options.damage_buildings || options.transform_to_wasteland || options.id == "draw");
        if (!destroy && !(main != null && (main.liquid || main.lava))) return;
        _walls.Remove(tile.data.tile_id);
        _dirty = true;
    }

    // 神力新画的城墙：原版设好顶层格之后排队，下一帧转换(不在原版设格的过程中改格子)
    public static void OnTopTileSet(WorldTile tile, TopTileType type)
    {
        if (_converting || !PureMapMode.Enabled || type?.wall != true || tile == null) return;
        Pending.Enqueue(tile);
    }

    private static void ConvertAll()
    {
        int converted = 0;
        foreach (TopTileType type in AssetManager.top_tiles?.list ?? new List<TopTileType>())
        {
            if (type?.wall != true) continue;
            foreach (WorldTile tile in type.getCurrentTiles().ToList())
                if (Convert(tile)) converted++;
        }
        if (converted <= 0) return;
        LogService.LogInfo($"[EmpireCraft][图层城墙] 原版城墙转成图层城墙 {converted} 格");
        Changed();
    }

    private static bool Convert(WorldTile tile)
    {
        if (tile?.data == null || tile.top_type == null || !tile.top_type.wall) return false;
        _walls[tile.data.tile_id] = tile.top_type.id;
        _converting = true;
        try
        {
            tile.setTileTypes(tile.main_type, null);
        }
        finally
        {
            _converting = false;
        }
        return true;
    }

    private static void RestoreAll()
    {
        if (_walls.Count == 0) return;
        WorldTile[] tiles = World.world.tiles_list;
        int restored = 0;
        _converting = true;
        try
        {
            foreach (var pair in _walls)
            {
                WorldTile tile = pair.Key >= 0 && pair.Key < tiles.Length ? tiles[pair.Key] : null;
                TopTileType type = AssetManager.top_tiles?.get(pair.Value);
                if (tile == null || type == null || tile.top_type != null || tile.Type?.liquid == true) continue;
                tile.setTileTypes(tile.main_type, type);
                restored++;
            }
        }
        finally
        {
            _converting = false;
        }
        LogService.LogInfo($"[EmpireCraft][图层城墙] 关闭纯净地图，图层城墙还原成原版城墙 {restored} 格");
        _walls.Clear();
        Changed();
    }

    private static void Changed()
    {
        Version++;
        RoadNetwork.Replan();
        World.world?.resetRedrawTimer();
    }
}
