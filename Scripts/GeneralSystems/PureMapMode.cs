using System.Collections.Generic;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 纯净地图模式(模组设置，下次启动生效，见 ConfigActions.PureMapCallBack)：地图上只留不透明的国家色块。
//   · 开启后自动打开无小人模式，关掉军团统领实体与小人社交(开着本模式时改回去也会被再改过来)；
//   · 全图没有植物：树、灌木、花草、菌菇、庄稼分帧清掉，原版的植物蔓延、随机播种、生物群系蔓生停掉；
//     木头改由伐木场按产能直接产出(见 IndustryBuildingSystem.HarvestTrees)，粮食按田里的种植位折算(见 FarmlandSystem)；
//   · 除船以外不画任何人物(血条、头像、物品、旗帜、状态图标也不画)，人物要去哪里直接到位、不走路，鼠标也不再选中人物
//     (见 PureMapPatch)；君主、官员、家族、谋划照常作为数据运作。船照常显示、航行，海军旗舰不随统领实体开关撤掉；
//   · 不画任何建筑(建筑本体、仓库堆货、建筑阴影、城墙)，建筑照常作为数据运作(仓储、住房、岗位、码头、寺庙……)；
//     雕像、训练木桩、花园、喷泉等纯装饰建筑清掉、不再建造(见 PureMapPatch)，野外粪便一并清掉；
//   · 国家色块完全不透明，任何缩放都填满(水面、无主地照常显示地形)。
public static class PureMapMode
{
    public static bool Enabled => ModClass.PURE_MAP_MODE;

    private const string NoCommonersLaw = "empirecraft_law_no_commoners";
    // 本模式下关掉的法则：军团统领实体、小人社交
    private static readonly string[] ForcedOffLaws = { "empirecraft_law_corps_general", "empirecraft_law_allow_social" };

    // 原版会长出、散播植物的世界法则
    private static readonly string[] FloraLaws =
    {
        "world_law_vegetation_random_seeds", "world_law_biome_overgrowth", "world_law_spread_trees",
        "world_law_spread_fungi", "world_law_spread_plants", "world_law_spread_fast_trees",
        "world_law_spread_fast_fungi", "world_law_spread_fast_plants"
    };

    // 不画的精灵层：人物的附属图层与建筑。人物本体与影子照常画(船要显示)，非船的人物由 PureMapPatch 设为不可见
    private static readonly string[] HiddenSprites =
    {
        "draw_healthbars", "draw_units_avatars", "unit_items", "draw_unit_hit_effect",
        "draw_parabolic_unload", "draw_unit_highlight_effect", "unit_banners", "selected_units",
        "unit_metas", "happiness_icons", "task_icons", "family_species_icons", "status_effects",
        "draw_buildings", "draw_building_stockpiles", "shadows_buildings", "draw_walls", "draw_light_walls_light_blobs"
    };

    // 纯装饰建筑(按类型或资源 id)
    private static readonly HashSet<string> DecorTypes = new() { "type_statue", "type_training_dummies", "type_poop" };
    private static readonly HashSet<string> DecorIds = new() { "garden", "fountain" };

    private const int SweepPerFrame = 400;
    private static bool _spritesOff;
    private static Building[] _sweep;
    private static int _cursor;
    private static float _lawCheckAt;

    public static void Tick()
    {
        if (!Enabled || World.world == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (!_spritesOff) TurnOffSprites();
        if (UnityEngine.Time.unscaledTime >= _lawCheckAt)
        {
            _lawCheckAt = UnityEngine.Time.unscaledTime + 2f;
            SyncLaws();
        }
        Sweep();
    }

    public static bool IsDecor(BuildingAsset asset) =>
        asset != null && (DecorTypes.Contains(asset.type ?? "") || DecorIds.Contains(asset.id ?? ""));

    private static bool ShouldClear(Building building) =>
        building?.asset != null && (building.asset.flora || building.asset.wheat || IsDecor(building.asset));

    private static void TurnOffSprites()
    {
        _spritesOff = true;
        foreach (string id in HiddenSprites)
        {
            QuantumSpriteAsset asset = AssetManager.quantum_sprites?.get(id);
            if (asset != null) asset.draw_call = _ => { };
        }
        LogService.LogInfo("[EmpireCraft][纯净地图] 不再绘制人物与建筑");
    }

    public static bool Visible(Actor actor) => actor?.asset != null && actor.asset.is_boat;

    // 停掉植物蔓延类法则，打开无小人模式，关掉军团统领实体与小人社交
    private static void SyncLaws()
    {
        var laws = World.world.world_laws;
        if (laws?.dict == null) return;
        bool changed = false;
        foreach (string id in FloraLaws)
        {
            if (!laws.dict.TryGetValue(id, out PlayerOptionData option) || !option.boolVal) continue;
            option.boolVal = false;
            changed = true;
        }
        if (laws.dict.TryGetValue(NoCommonersLaw, out PlayerOptionData noCommoners) && !noCommoners.boolVal)
        {
            noCommoners.boolVal = true;
            changed = true;
            LogService.LogInfo("[EmpireCraft][纯净地图] 已自动打开无小人模式");
        }
        var switched = new List<(WorldLawAsset law, PlayerOptionData option)>();
        foreach (string id in ForcedOffLaws)
        {
            if (!laws.dict.TryGetValue(id, out PlayerOptionData option) || !option.boolVal) continue;
            option.boolVal = false;
            changed = true;
            switched.Add((AssetManager.world_laws_library.get(id), option));
        }
        if (changed) laws.updateCaches();
        // 小人社交关掉时要把各人的社交行为撤掉(法则自带的切换处理)；统领实体的切换处理只是弹提示，不调
        foreach ((WorldLawAsset law, PlayerOptionData option) in switched)
            if (law?.id == "empirecraft_law_allow_social") law.on_state_change?.Invoke(option);
    }

    // 分帧扫一遍全图建筑，把植物与装饰建筑清掉；扫完一遍再取新的快照(新长出的、地图生成的都会被扫到)
    private static void Sweep()
    {
        if (_sweep == null || _cursor >= _sweep.Length)
        {
            _sweep = World.world.buildings.getSimpleArray();
            _cursor = 0;
            if (_sweep == null) return;
        }
        int end = global::System.Math.Min(_sweep.Length, _cursor + SweepPerFrame);
        for (; _cursor < end; _cursor++)
        {
            Building building = _sweep[_cursor];
            if (!ShouldClear(building) || building.isRekt() || building.isOnRemove()) continue;
            building.startRemove();
        }
    }

    public static void ResetWorldState()
    {
        _sweep = null;
        _cursor = 0;
        _lawCheckAt = 0f;
    }
}
