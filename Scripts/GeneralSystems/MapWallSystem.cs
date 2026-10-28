using System.Collections.Generic;
using System.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 图层城墙(纯净地图，用户 2026-10-14 要求)：原版地图上画的城墙(城墙类顶层格，神力画的)在纯净地图下转成模组的数据——
// 记下是哪一格、哪种城墙，再把原版的城墙格清掉(纯净地图不画建筑与城墙实体)；之后神力新画的城墙也当场转换。
// 图层城墙按像素画成长城的样子(见 MapWallOverlay)，路网与城郊的路绕开它(见 RoadNetwork、UrbanFabric)。
// 关掉纯净地图后读档，图层城墙还原成原版的城墙格，玩家画的城墙不会丢。
// 关城压着的城墙格是关门(见 WallGateSystem)：数据照旧留着，路从这里过墙；普通地图下这几格的原版城墙让开，关城没了再合上。
// 城池全图开了城墙的城(见 CityPlanWorks.SyncWalls)沿城区外沿真筑一圈图层城墙(Add)，与神力画的一样挡兵、关上普通地图后还原成原版城墙；
// 四门是关门；这一圈由城市平面图画(MapWallOverlay 不再画成长城)。
public static class MapWallSystem
{
    // 格编号 → 原版城墙种类(顶层格 id)，随存档保存(SaveData.map_walls)
    private static Dictionary<int, string> _walls = new();
    private static readonly Queue<WorldTile> Pending = new();
    private static object _world;
    private static bool _synced, _converting, _dirty;
    // 普通地图下这一帧画了原版城墙(下一帧看要不要给关门让开)
    private static bool _vanillaDrawn;

    // 城墙每变一次加一(MapWallOverlay 据此重画)
    public static int Version { get; private set; }

    // 城墙格每增删一次加一(关门据此重算，见 WallGateSystem；Version 关门变了也加，不能拿来当关门的键，否则自己追自己)
    public static int Revision { get; private set; }

    public static IReadOnlyDictionary<int, string> Walls => _walls;

    // 读档后已同步过(纯净地图下原版城墙都转成了图层城墙)：这之前城墙数据不全，城池全图不对账(见 CityPlanWorks)
    public static bool Ready => _synced && ReferenceEquals(_world, World.world);

    public static bool Contains(int tileId) => _walls.Count > 0 && _walls.ContainsKey(tileId);

    public static bool Contains(WorldTile tile) => tile?.data != null && Contains(tile.data.tile_id);

    // 挡路的城墙格(原版城墙或图层城墙)：关城压着的关门不挡(见 WallGateSystem)
    public static bool Blocks(WorldTile tile) =>
        tile?.Type != null && (tile.Type.wall || Contains(tile)) && !WallGateSystem.IsGate(tile);

    // 斜着走一步夹在两格城墙中间：城墙只斜着相连时墙身照样画成连着的(见 MapWallOverlay 的转角砖)，
    // 从这里斜插过去就是穿墙(以前路、小路都能从斜接的城墙缝里钻过去)
    public static bool CutsCorner(int x0, int y0, int x1, int y1) =>
        x0 != x1 && y0 != y1 && Blocks(World.world?.GetTile(x1, y0)) && Blocks(World.world?.GetTile(x0, y1));

    public static void ResetWorldState()
    {
        _walls = new Dictionary<int, string>();
        Pending.Clear();
        _world = null;
        _synced = false;
        Version++;
        Revision++;
        CityPlanWorks.ResetWorldState();
    }

    public static void Load(Dictionary<int, string> saved)
    {
        _walls = saved ?? new Dictionary<int, string>();
        Pending.Clear();
        _world = null;
        _synced = false;
        Version++;
        Revision++;
        CityPlanWorks.ResetWorldState();
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
            // 各城城池全图筑的城门按城的存档重收(读档途中收的可能还缺城)，再转换、还原(还原时城门要让开)
            CityPlanWorks.ResetWorldState();
            if (PureMapMode.Enabled) ConvertAll();
            else RestoreAll();
            return;
        }
        if (_dirty)
        {
            _dirty = false;
            Changed();
        }
        // 城池全图开了城墙的城：城区、城墙开关变了就重筑、拆(隔几秒对一次账，见 CityPlanWorks)
        CityPlanWorks.Tick();
        // 关门变了(筑关、毁关、升级、画墙穿过关城、关城那片地易主、城池全图的城门)：重画城墙、重新找路；普通地图下原版城墙跟着让开、合上
        // (关门没变、只是在关门上又画了一道原版城墙的，也要让开)
        bool gatesChanged = WallGateSystem.Refresh();
        if (!PureMapMode.Enabled && (gatesChanged || _vanillaDrawn)) SyncVanillaGates();
        _vanillaDrawn = false;
        if (gatesChanged) Changed();
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
        Remove(tile);
    }

    // 拆除工具看不到已转为图层数据的实体墙，需直接删除存档数据并作废寻路缓存。
    public static bool Remove(WorldTile tile)
    {
        if (_converting || tile?.data == null || !_walls.Remove(tile.data.tile_id)) return false;
        Revision++;
        _dirty = true;
        if (tile.zone != null)
        {
            TerrainSystem.Invalidate(tile.zone);
            TerrainResistance.InvalidateWall(tile.zone);
        }
        return true;
    }

    // 筑一格图层城墙(城池全图筑城墙，见 CityPlanWorks.SyncWalls)：只在纯净地图下(普通地图下城墙是原版的格子，读档时才转换)；
    // 水、岩浆上不筑，已有城墙(图层的或原版的)的格不动；筑上了返回 true。同 Remove，路网、关门、城墙阻力与军团过墙下一帧跟着重算
    public static bool Add(WorldTile tile, string type)
    {
        if (_converting || !PureMapMode.Enabled || tile?.data == null || tile.Type == null || tile.Type.liquid || tile.Type.lava ||
            tile.Type.wall || string.IsNullOrEmpty(type) || _walls.ContainsKey(tile.data.tile_id)) return false;
        _walls[tile.data.tile_id] = type;
        Revision++;
        _dirty = true;
        if (tile.zone != null)
        {
            TerrainSystem.Invalidate(tile.zone);
            TerrainResistance.InvalidateWall(tile.zone);
        }
        return true;
    }

    // 神力新画的城墙：原版设好顶层格之后排队，下一帧转换(不在原版设格的过程中改格子)；
    // 普通地图下不转换，但画过关城的原版城墙要让出关门(见 SyncVanillaGates)，关门跟着重算
    public static void OnTopTileSet(WorldTile tile, TopTileType type)
    {
        if (_converting || type?.wall != true || tile == null) return;
        Revision++;
        if (PureMapMode.Enabled) Pending.Enqueue(tile);
        else _vanillaDrawn = true;
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
        Revision++;
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

    // 关门那几格(见 WallGateSystem)不还原：原版城墙是挡路的格子，还原了人马就过不了关；数据留着，关城没了再合上
    private static void RestoreAll()
    {
        if (_walls.Count == 0) return;
        int restored = RestoreExceptGates();
        LogService.LogInfo($"[EmpireCraft][图层城墙] 关闭纯净地图，图层城墙还原成原版城墙 {restored} 格，留作关门 {_walls.Count} 格");
        Changed();
    }

    // 普通地图：关门那几格的原版城墙让开(记进城墙数据，存档照旧)；不再是关门的还原成原版城墙
    private static void SyncVanillaGates()
    {
        WorldTile[] tiles = World.world?.tiles_list;
        if (tiles == null) return;
        _converting = true;
        try
        {
            foreach (int id in new List<int>(WallGateSystem.GateTiles))
            {
                WorldTile tile = id >= 0 && id < tiles.Length ? tiles[id] : null;
                if (tile?.top_type == null || !tile.top_type.wall) continue;
                _walls[id] = tile.top_type.id;
                tile.setTileTypes(tile.main_type, null);
            }
        }
        finally
        {
            _converting = false;
        }
        RestoreExceptGates();
    }

    // 城墙数据里不是关门的格还原成原版城墙，从数据里去掉；返回还原了几格
    private static int RestoreExceptGates()
    {
        WorldTile[] tiles = World.world.tiles_list;
        int restored = 0, removed = 0;
        _converting = true;
        try
        {
            foreach (var pair in new List<KeyValuePair<int, string>>(_walls))
            {
                if (WallGateSystem.IsGate(pair.Key)) continue;
                _walls.Remove(pair.Key);
                removed++;
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
        // 原版城墙与数据里的城墙都算城墙，格子还是那些格子，关门不因此变；Revision 照样加，关门按新数据重算一遍(结果相同)
        if (removed > 0) Revision++;
        return restored;
    }

    private static void Changed()
    {
        Version++;
        RoadNetwork.Replan();
        World.world?.resetRedrawTimer();
    }
}
