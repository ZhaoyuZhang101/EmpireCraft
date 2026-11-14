// 世界本体：地块网格、区块(8×8 格一块，城的辖地以区块计)、陆海连通、世界时间。
// 只有数据：没有画面、没有区块贴图。读是随时可读的；写(改地形、换归属)都在主循环里。
//# hand MapBox: ctor GetTile GetTileSimple Update addClearWorld addLoadWorldCallbacks calcPath calculateCurElapsed checkEventBuildingsDestroy cleanUpWorld clearWorld finishMakingWorld finishingUpLoading getActorNearCursor getCachedMapMetaAsset getCivWorldPopulation getCurSessionTime getCurWorldTime getMousePos getMouseTilePosCachedFrame getWorldTimeElapsedSince isAnyPowerSelected isBusyWithUI isPaused isRenderGameplay isRenderMiniMap isSelectedPower lastGC preloadRenderedSprites redrawMiniMap redrawTiles resetRedrawTimer setMapSize startTheGame updateBuildings updateMapLayers
//# hand WorldTile: Type zone_city canBuildOn getColor hasBuilding hasCity isGoodForBoat isOnFire isSameIsland reachableFrom setTileType setTileTypes setTopTileType
//# hand TileZone: canBeClaimedByCity canStartCityHere checkCanSettleInThisBiomes checkShouldReRender getAllianceOnZone getHashset getKingdomOnZone hasCity resetRenderHelpers setCity
//# hand ZoneCalculator: getZone getZoneByID dirtyAndClear setDirty setDrawnZonesDirty clearAllRedrawTimers
//# hand MapChunkManager: allDirty update
//# hand WorldAgeManager: setDefaultAges shouldShowLights
//# hand LoadingScreen: startTransition
//# hand WorldLaws: init updateCaches
//# hand WorldLawAsset: isEnabled toggle
//# hand MapStats: initNewWorld
//# hand TileIsland: getTileCount goodForDocks
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EmpireCraft.Server;
using UnityEngine;

public partial class MapBox
{
    // 世界时间(秒)：五秒一月。主循环每一步把它往前拨
    public double world_time;
    private double _sessionTime;
    // 这一步现实里过了多久(主循环给)；calculateCurElapsed 把它交出去，模组的补丁在那里乘上终端流速
    internal float FrameSeconds;
    private TileZone[] _zoneGrid = Array.Empty<TileZone>();
    private int _zonesX, _zonesY;

    public MapBox()
    {
        kingdoms = new KingdomManager();
        cities = new CityManager();
        units = new ActorManager();
        wars = new WarManager();
        alliances = new AllianceManager();
        clans = new ClanManager();
        cultures = new CultureManager();
        religions = new ReligionManager();
        languages = new LanguageManager();
        families = new FamilyManager();
        plots = new PlotManager();
        buildings = new BuildingManager();
        books = new BookManager();
        items = new ItemManager();
        armies = new ArmyManager();
        diplomacy = new DiplomacyManager();
        zone_calculator = new ZoneCalculator();
        map_chunk_manager = new MapChunkManager();
        era_manager = new WorldAgeManager();
        transition_screen = new LoadingScreen();
        world_laws = new WorldLaws();
        map_stats = new MapStats();
        game_stats = new GameStats { data = new GameStatsData() };
        tiles_list = Array.Empty<WorldTile>();
        Shim.FillHelpers(this);
        Shim.FillHelpers(zone_calculator, 1);
        DiplomacyHelpers.wars = wars;
        DiplomacyHelpers.diplomacy = diplomacy;
        SimGlobals.m ??= new SimGlobalAsset { id = "main" };
    }

    public double getCurWorldTime() => world_time;
    public double getCurSessionTime() => _sessionTime;
    public float getWorldTimeElapsedSince(double pTime) => (float)(world_time - pTime);
    public bool isPaused() => Config.paused;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public float calculateCurElapsed() => FrameSeconds;

    // 主循环每一步调一回(模组挂在这上面的补丁照常跑)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Update()
    {
    }

    internal void Advance(float elapsed)
    {
        world_time += elapsed;
        _sessionTime += elapsed;
    }

    public WorldTile GetTileSimple(int pX, int pY) => tiles_list[pX + pY * width];
    public WorldTile GetTile(int pX, int pY) => pX < 0 || pY < 0 || pX >= width || pY >= height ? null : tiles_list[pX + pY * width];
    internal TileZone ZoneAt(int zoneX, int zoneY) => zoneX < 0 || zoneY < 0 || zoneX >= _zonesX || zoneY >= _zonesY ? null : _zoneGrid[zoneX + zoneY * _zonesX];

    public void addClearWorld(int pNextWidth, int pNextHeight) => clearWorld();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void clearWorld()
    {
        Config.game_loaded = false;
        kingdoms.clear();
        cities.clear();
        units.clear();
        wars.clear();
        alliances.clear();
        clans.clear();
        cultures.clear();
        religions.clear();
        languages.clear();
        families.clear();
        plots.clear();
        buildings.clear();
        books.clear();
        items.clear();
        armies.clear();
        world_time = 0d;
        tiles_list = Array.Empty<WorldTile>();
        zone_calculator.zones.Clear();
        zone_calculator._zones_dict_id.Clear();
        _zoneGrid = Array.Empty<TileZone>();
        width = height = 0;
    }

    // 定大小并建网格。参数是大块数(一大块 64×64 格)
    public void setMapSize(int pWidth, int pHeight)
    {
        width = pWidth * Config.MAP_BLOCK_SIZE;
        height = pHeight * Config.MAP_BLOCK_SIZE;
        var tiles = new WorldTile[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int at = x + y * width;
            tiles[at] = new WorldTile
            {
                x = x, y = y, pos = new Vector2Int(x, y), posV = new Vector3(x, y), posV3 = new Vector3(x, y),
                data = new WorldTileData { tile_id = at }, region = ShimIslands.Unsorted
            };
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            WorldTile tile = tiles[x + y * width];
            var four = new List<WorldTile>(4);
            var eight = new List<WorldTile>(8);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                WorldTile near = tiles[nx + ny * width];
                eight.Add(near);
                if (dx != 0 && dy != 0) continue;
                four.Add(near);
                if (dy == 1) tile.tile_up = near;
                else if (dy == -1) tile.tile_down = near;
                else if (dx == -1) tile.tile_left = near;
                else tile.tile_right = near;
            }
            tile.neighbours = four.ToArray();
            tile.neighboursAll = eight.ToArray();
        }
        tiles_list = tiles;

        int size = Config.CITY_ZONE_SIZE;
        _zonesX = width / size;
        _zonesY = height / size;
        _zoneGrid = new TileZone[_zonesX * _zonesY];
        ZoneCalculator calculator = zone_calculator;
        calculator.zones.Clear();
        calculator._zones_dict_id.Clear();
        calculator.zones_total_x = _zonesX;
        calculator.zones_total_y = _zonesY;
        for (int zy = 0; zy < _zonesY; zy++)
        for (int zx = 0; zx < _zonesX; zx++)
        {
            var zone = new TileZone { id = zx + zy * _zonesX, x = zx, y = zy, tiles = new WorldTile[size * size] };
            for (int ty = 0; ty < size; ty++)
            for (int tx = 0; tx < size; tx++)
            {
                WorldTile tile = tiles[zx * size + tx + (zy * size + ty) * width];
                zone.tiles[tx + ty * size] = tile;
                tile.zone = zone;
            }
            zone.centerTile = tiles[zx * size + size / 2 + (zy * size + size / 2) * width];
            zone.world_edge = zx == 0 || zy == 0 || zx == _zonesX - 1 || zy == _zonesY - 1;
            _zoneGrid[zone.id] = zone;
            calculator.zones.Add(zone);
            calculator._zones_dict_id[zone.id] = zone;
        }
        foreach (TileZone zone in _zoneGrid)
        {
            zone.zone_up = ZoneAt(zone.x, zone.y + 1);
            zone.zone_down = ZoneAt(zone.x, zone.y - 1);
            zone.zone_left = ZoneAt(zone.x - 1, zone.y);
            zone.zone_right = ZoneAt(zone.x + 1, zone.y);
            var four = new List<TileZone>(4);
            var eight = new List<TileZone>(8);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                TileZone near = ZoneAt(zone.x + dx, zone.y + dy);
                if (near == null) continue;
                eight.Add(near);
                if (dx == 0 || dy == 0) four.Add(near);
            }
            zone.neighbours = four.ToArray();
            zone.neighbours_all = eight.ToArray();
        }
        ShimIslands.MarkDirty();
    }

    public void addLoadWorldCallbacks()
    {
    }

    public void cleanUpWorld(bool pSetChunksDirty = true) => ShimIslands.MarkDirty();
    public void finishMakingWorld() => ShimIslands.Refresh();

    public void finishingUpLoading()
    {
        ShimIslands.Refresh();
        Config.game_loaded = true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void lastGC()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void startTheGame(bool pForceGenerate = false)
    {
    }

    public void preloadRenderedSprites()
    {
    }

    public void redrawTiles()
    {
    }

    public void resetRedrawTimer()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void redrawMiniMap(bool pForce = false)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void updateBuildings(float pElapsed)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void updateMapLayers(float pElapsed)
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void checkEventBuildingsDestroy()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Actor getActorNearCursor() => null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int getCivWorldPopulation() => units.Count;

    public MetaTypeAsset getCachedMapMetaAsset() => null;
    public Vector2 getMousePos() => default;
    public WorldTile getMouseTilePosCachedFrame() => null;
    public bool isAnyPowerSelected() => false;
    public bool isBusyWithUI() => false;
    public static bool isRenderGameplay() => false;
    public static bool isRenderMiniMap() => true;
    public bool isSelectedPower(string pPower) => false;

    // 逐格寻路服务器里没有(军队走的是模组自己按区块算的路)
    public bool calcPath(WorldTile pFrom, WorldTile pTargetTile, List<WorldTile> pSavePath) => Shim.Miss<bool>("MapBox::calcPath");
}

public partial class WorldTile
{
    public TileTypeBase Type => top_type != null ? top_type : main_type;
    public City zone_city => zone?.city;

    public void setTileTypes(string pType, TopTileType pTopTile) => setTileTypes(AssetManager.tiles.get(pType), pTopTile);

    public void setTileTypes(TileType pType, TopTileType pTopTile, bool pSetDirty = true)
    {
        TileTypeBase before = Type;
        main_type = pType;
        top_type = pTopTile;
        Changed(before);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void setTopTileType(TopTileType pTopTile, bool pUpdateStats = true)
    {
        TileTypeBase before = Type;
        top_type = pTopTile;
        Changed(before);
    }

    // 名字是主层的换主层(表层清掉)，是表层的只换表层
    public void setTileType(string pType)
    {
        if (AssetManager.tiles.has(pType)) setTileTypes(AssetManager.tiles.get(pType), null);
        else if (AssetManager.top_tiles.has(pType)) setTopTileType(AssetManager.top_tiles.get(pType));
    }

    private void Changed(TileTypeBase before)
    {
        TileTypeBase now = Type;
        if (zone != null)
        {
            if (before != null)
            {
                if (before.liquid) zone.tiles_with_liquid--;
                if (before.ground) zone.tiles_with_ground--;
            }
            if (now != null)
            {
                if (now.liquid) zone.tiles_with_liquid++;
                if (now.ground) zone.tiles_with_ground++;
            }
        }
        if (before == null || now == null || ShimIslands.KindOf(before) != ShimIslands.KindOf(now)) ShimIslands.MarkDirty();
    }

    public Color32 getColor() => Type?.color ?? default;
    public bool hasBuilding() => building != null;
    public bool hasCity() => zone?.city != null;
    public bool canBuildOn(BuildingAsset pNewTemplate) => building == null && Type != null && Type.can_build_on;
    public bool isOnFire() => false;
    public bool isGoodForBoat() => Type != null && Type.ocean;

    public bool isSameIsland(WorldTile pTile)
    {
        if (pTile == null) return false;
        ShimIslands.Refresh();
        return region?.island != null && region.island == pTile.region?.island;
    }

    public bool reachableFrom(WorldTile pFromTile) => isSameIsland(pFromTile);
}

public partial class TileZone
{
    public bool hasCity() => city != null;
    // 换主：城的邻接要重算(模组的划地补丁直接调这里)
    public void setCity(City pCity)
    {
        if (city == pCity) return;
        city = pCity;
        ShimCities.MarkDirty();
    }

    // 没主、有陆地的区块才能划给一座城(已是这座城的也算)
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool canBeClaimedByCity(City pCity) => tiles_with_ground > 0 && (city == null || city == pCity);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool canStartCityHere() => city == null && tiles_with_ground * 2 >= tiles.Length;

    public bool checkCanSettleInThisBiomes(Subspecies pSubspecies) => true;
    public IMetaObject getKingdomOnZone(int pZoneOption) => city?.kingdom;
    public Alliance getAllianceOnZone(int pZoneOption) => city?.kingdom?.getAlliance();
    private static readonly HashSet<Building> NoBuildings = new();
    public HashSet<Building> getHashset(BuildingList pType) => NoBuildings;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool checkShouldReRender(int pHashCode, int pID, int pColorAssetID, bool pLastAnimated) => false;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void resetRenderHelpers()
    {
    }
}

public partial class ZoneCalculator
{
    public TileZone getZone(int pX, int pY) => World.world?.ZoneAt(pX, pY);
    public TileZone getZoneByID(int pID) => _zones_dict_id.TryGetValue(pID, out TileZone zone) ? zone : null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void dirtyAndClear()
    {
    }

    public void setDirty()
    {
    }

    public void setDrawnZonesDirty()
    {
    }

    public void clearAllRedrawTimers()
    {
    }
}

public partial class MapChunkManager
{
    public void allDirty() => ShimIslands.MarkDirty();
    public void update(float pElapsed, bool pForce = false) => ShimIslands.Refresh();
}

public partial class WorldAgeManager
{
    public void setDefaultAges()
    {
    }

    public bool shouldShowLights() => false;
}

public partial class LoadingScreen
{
    // 没有过场画面：直接办
    public void startTransition(TransitionAction pAction) => pAction?.Invoke();
}

public partial class MapStats
{
    public void initNewWorld()
    {
    }
}

public partial class WorldLaws
{
    // 库里登记过的法则都按各自的默认值列进来
    public void init(bool pUpdateCaches = true)
    {
        foreach (WorldLawAsset law in AssetManager.world_laws_library.list)
            if (law?.id != null && !dict.ContainsKey(law.id)) dict[law.id] = new PlayerOptionData { boolVal = law.default_state };
    }

    public void updateCaches()
    {
    }
}

public partial class WorldLawAsset
{
    public bool isEnabled()
    {
        WorldLaws laws = World.world?.world_laws;
        return laws != null && id != null && laws.dict.TryGetValue(id, out PlayerOptionData option) ? option.boolVal : default_state;
    }

    public void toggle(bool pState)
    {
        WorldLaws laws = World.world?.world_laws;
        if (laws == null || id == null) return;
        if (!laws.dict.TryGetValue(id, out PlayerOptionData option)) laws.dict[id] = option = new PlayerOptionData();
        option.boolVal = pState;
    }
}

public partial class TileIsland
{
    internal int Count;
    internal byte Kind;
    public int getTileCount() => Count;
    public bool goodForDocks() => Kind == ShimIslands.Water && Count >= 64;
}

// 陆海连通：相邻(四向)又同一类(水、陆、山)的地块连成一片。地形一改就记脏，用到的时候再整张重算
internal static class ShimIslands
{
    internal const byte Water = 0, Land = 1, Rock = 2;
    internal static readonly MapRegion Unsorted = new() { island = new TileIsland() };
    private static bool _dirty = true;

    internal static byte KindOf(TileTypeBase type) => type == null || type.liquid ? Water : type.mountains || type.block ? Rock : Land;
    internal static void MarkDirty() => _dirty = true;

    internal static void Refresh()
    {
        if (!_dirty) return;
        _dirty = false;
        MapBox world = World.world;
        WorldTile[] tiles = world?.tiles_list;
        if (tiles == null || tiles.Length == 0) return;
        int width = MapBox.width, height = MapBox.height;
        var seen = new bool[tiles.Length];
        var queue = new int[tiles.Length];
        for (int start = 0; start < tiles.Length; start++)
        {
            if (seen[start]) continue;
            byte kind = KindOf(tiles[start].Type);
            var island = new TileIsland { Kind = kind };
            var region = new MapRegion { island = island };
            int head = 0, tail = 0;
            queue[tail++] = start;
            seen[start] = true;
            while (head < tail)
            {
                int at = queue[head++];
                tiles[at].region = region;
                int x = at % width, y = at / width;
                if (x > 0) Visit(at - 1);
                if (x < width - 1) Visit(at + 1);
                if (y > 0) Visit(at - width);
                if (y < height - 1) Visit(at + width);
            }
            island.Count = tail;

            void Visit(int next)
            {
                if (seen[next] || KindOf(tiles[next].Type) != kind) return;
                seen[next] = true;
                queue[tail++] = next;
            }
        }
    }
}
