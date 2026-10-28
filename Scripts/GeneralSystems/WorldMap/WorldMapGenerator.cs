using System;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 生成世界地图：照原版 MapBox.generateNewMap 的加载步骤走一遍，但尺寸不必是正方形、地表从地球数据铺，
// 铺完(原版 lastGC 之后，模组各系统已清好重置)再按所选时代摆城建国(WorldMapScenario)。
// 每步都接住异常：SmoothLoader 里一步抛错会被当成卡死，整个世界重新随机生成
public static class WorldMapGenerator
{
    public static bool Generating { get; private set; }

    // 加载界面看到加载队列走完时调用(正常收尾已复位；中途出错被原版重开世界时靠这里复位)
    public static void MarkDone()
    {
        Generating = false;
        // 进了世界再做的安排：行政区、多京、神圣之名、殖民地、穿越者降临(见 WorldMapScenario.QueueAfterLoad)
        WorldMapScenario.RunAfterLoad();
    }

    public static void Start(WorldMapFrame frame, int sizeIndex, WorldMapEra era)
    {
        if (frame == null || era == null || Generating) return;
        if (!WorldMapData.EnsureLoaded() || !EarthTerrain.EnsureLoaded())
        {
            WorldTip.showNow("world_map_data_missing", true, "top", 4f);
            return;
        }
        sizeIndex = Mathf.Clamp(sizeIndex, 0, frame.sizes.Length - 1);
        Vector2Int size = frame.sizes[sizeIndex];
        Generating = true;
        ScrollWindow.hideAllEvent();
        EmpireCraft.Scripts.UI.Components.WorldMapLoadingScreen.Show(era, NeoModLoader.General.LM.Get("world_map_frame_" + frame.id),
            NeoModLoader.General.LM.Get("world_map_size_" + sizeIndex), size);
        World.world.transition_screen.startTransition(() => Generate(frame, size, sizeIndex, era));
    }

    private static void Step(Action action, string id, bool skipFrame = false)
    {
        SmoothLoader.add(delegate
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                LogService.LogError($"[EmpireCraft][世界地图] {id} 失败: {exception}");
            }
        }, id, skipFrame);
    }

    private static void Generate(WorldMapFrame frame, Vector2Int size, int sizeIndex, WorldMapEra era)
    {
        MapBox world = World.world;
        LogService.LogInfo($"[EmpireCraft][世界地图] 生成 {frame.id} {size.x}×{size.y} 区块，时代 {era.id}");
        SmoothLoader.prepare();
        Step(() => world.addClearWorld(size.x, size.y), "World Map: Clear");
        Step(() =>
        {
            Config.ZONE_AMOUNT_X = size.x;
            Config.ZONE_AMOUNT_Y = size.y;
            world.setMapSize(size.x, size.y);
        }, "World Map: Size");
        Step(() =>
        {
            AssetManager.tiles.setListTo(DepthGeneratorType.Generator);
            world.world_laws = new WorldLaws();
            world.world_laws.init();
            world.hotkey_tabs_data = new HotkeyTabsData();
        }, "World Map: Laws");
        Step(() =>
        {
            world.map_stats = new MapStats();
            world.map_stats.initNewWorld();
            world.map_stats.name = era.Name;
            Randy.resetSeed(Randy.randomInt(1, 555555555));
        }, "World Map: Stats");
        if (!Config.disable_db)
        {
            Step(db.DBManager.createDB, "World Map: Stats DB");
            db.DBTables.createOrMigrateTablesLoader();
        }
        WindowPreloader.addWaitForWindowResources();
        Step(() => world.era_manager.setDefaultAges(), "World Map: Ages");
        Step(() => EarthTerrain.Fill(frame, size), "World Map: Terrain", true);
        Step(() => EarthTerrain.DrawRivers(frame, size), "World Map: Rivers");
        Step(() =>
        {
            AssetManager.tiles.setListTo(DepthGeneratorType.Gameplay);
            world.map_chunk_manager.allDirty();
            world.map_chunk_manager.update(0f, true);
        }, "World Map: Chunks", true);
        // 纯净地图模式本就清植物；其余照原版撒一遍草木与浆果
        if (!PureMapMode.Enabled)
        {
            int spread = world.tiles_list.Length / 20;
            for (int i = 0; i < 12; i++) Step(() => MapGenerator.spawnVegetation(spread), "World Map: Vegetation");
            Step(MapGenerator.spawnResources, "World Map: Resources");
        }
        Step(() => world.cleanUpWorld(), "World Map: Clean Up", true);
        Step(world.redrawTiles, "World Map: Draw", true);
        Step(world.preloadRenderedSprites, "World Map: Sprites");
        Step(world.finishMakingWorld, "World Map: Finish", true);
        Step(world.lastGC, "World Map: GC", true);
        // 以下模组各系统已就绪(lastGC 的补丁把 IS_CLEAR 复位)
        WorldMapScenario.Schedule(frame, size, sizeIndex, era, Step);
        Step(() =>
        {
            Generating = false;
            FocusCamera(frame, size, era);
        }, "World Map: Camera");
        world.addLoadWorldCallbacks();
        Step(world.finishingUpLoading, "World Map: Done");
    }

    // 镜头对准本时代的主角：主东方看中原，主西方看地中海
    private static void FocusCamera(WorldMapFrame frame, Vector2Int size, WorldMapEra era)
    {
        Vector2Int tile = era.East
            ? EarthTerrain.TileOf(frame, size, 112f, 34f)
            : EarthTerrain.TileOf(frame, size, 14f, 43f);
        Camera camera = World.world.camera;
        if (camera == null) return;
        Vector3 position = camera.transform.position;
        position.x = Mathf.Clamp(tile.x, 0, MapBox.width);
        position.y = Mathf.Clamp(tile.y, 0, MapBox.height);
        camera.transform.position = position;
    }
}
