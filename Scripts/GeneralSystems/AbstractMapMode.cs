using System.Collections.Generic;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 抽象地图模式(模组设置，下次启动生效，见 ConfigActions.AbstractMapCallBack)：
//   · 全图没有植物：树、灌木、花草、菌菇、庄稼分帧清掉，原版的植物蔓延、随机播种、生物群系蔓生停掉；
//     木头改由伐木场按产能直接产出(见 IndustryBuildingSystem.HarvestTrees)，粮食按田里的种植位折算(见 FarmlandSystem)；
//   · 地图上不画任何实体人物(单位、船、血条、头像、物品、旗帜、状态图标)，人物要去哪里直接到位、不走路
//     (见 AbstractMapPatch)，君主、官员、家族、谋划照常作为数据运作；
//   · 国家色块完全不透明，任何缩放都填满(水面、无主地照常显示地形)。
// 建筑照旧保留(仓储、住房、岗位仍靠建筑)，全面虚拟化建筑另行排期。
public static class AbstractMapMode
{
    public static bool Enabled => ModClass.ABSTRACT_MAP_MODE;

    // 原版会长出、散播植物的世界法则
    private static readonly string[] FloraLaws =
    {
        "world_law_vegetation_random_seeds", "world_law_biome_overgrowth", "world_law_spread_trees",
        "world_law_spread_fungi", "world_law_spread_plants", "world_law_spread_fast_trees",
        "world_law_spread_fast_fungi", "world_law_spread_fast_plants"
    };

    // 画实体人物的精灵层：抽象模式下一律不画
    private static readonly string[] UnitSprites =
    {
        "draw_units", "draw_healthbars", "draw_units_avatars", "unit_items", "draw_unit_hit_effect",
        "draw_parabolic_unload", "draw_unit_highlight_effect", "shadows_unit", "unit_banners", "selected_units",
        "unit_metas", "happiness_icons", "task_icons", "family_species_icons", "status_effects"
    };

    private const int SweepPerFrame = 400;
    private static bool _spritesOff;
    private static Building[] _sweep;
    private static int _cursor;
    private static float _lawCheckAt;

    public static void Tick()
    {
        if (!Enabled || World.world == null || !Config.game_loaded || SmoothLoader.isLoading()) return;
        if (!_spritesOff) TurnOffUnitSprites();
        if (UnityEngine.Time.unscaledTime >= _lawCheckAt)
        {
            _lawCheckAt = UnityEngine.Time.unscaledTime + 2f;
            StopFloraLaws();
        }
        SweepFlora();
    }

    public static bool IsFlora(Building building) =>
        building?.asset != null && (building.asset.flora || building.asset.wheat);

    private static void TurnOffUnitSprites()
    {
        _spritesOff = true;
        foreach (string id in UnitSprites)
        {
            QuantumSpriteAsset asset = AssetManager.quantum_sprites?.get(id);
            if (asset != null) asset.draw_call = _ => { };
        }
        LogService.LogInfo("[EmpireCraft][抽象地图] 不再绘制实体人物");
    }

    private static void StopFloraLaws()
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
        if (changed) laws.updateCaches();
    }

    // 分帧扫一遍全图建筑，把植物清掉；扫完一遍再取新的快照(新长出的、地图生成的都会被扫到)
    private static void SweepFlora()
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
            if (!IsFlora(building) || building.isRekt() || building.isOnRemove()) continue;
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
