using System.Collections.Generic;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 城市规划笔刷(用户 2026-10-16："城市规划的逻辑还有神力得改，改成更符合现在版本的"，选了按格刷、有笔刷大小、
// 加刷街道广场城墙)：按住在城市领地上拖动，笔刷盖到的格刷上用途或设施(大小用原版笔刷大小)，见 TilePlanSystem。
//   用途：住宅、商业、工业、军事、官署、寺庙、绿地、农田、保护；设施：街道、广场、城墙；擦除去掉刷的规划。
// 以前是点一下整块地(区块)划一种功能区；现在一块地里同一类用途刷满一半就划为相应的功能区。
public static class PlanBrushPowers
{
    public static readonly (string id, PlanUse? use, PlanFeature? feature)[] Brushes =
    {
        ("zone_residential", PlanUse.Residential, null),
        ("zone_commerce", PlanUse.Commerce, null),
        ("zone_industry", PlanUse.Industry, null),
        ("zone_military", PlanUse.Military, null),
        ("zone_civic", PlanUse.Civic, null),
        ("zone_temple", PlanUse.Temple, null),
        ("zone_park", PlanUse.Park, null),
        ("farm_planning", PlanUse.Farm, null),
        ("zone_reserve", PlanUse.Keep, null),
        ("plan_street", null, PlanFeature.Street),
        ("plan_plaza", null, PlanFeature.Plaza),
        ("plan_wall", null, PlanFeature.Wall),
        ("plan_erase", null, null)
    };

    private static readonly Dictionary<string, (PlanUse? use, PlanFeature? feature)> ById = new();
    private static int _painted, _outside;
    private static float _tipAt;

    public static void Init()
    {
        foreach ((string id, PlanUse? use, PlanFeature? feature) in Brushes)
        {
            ById[id] = (use, feature);
            if (AssetManager.powers.get(id) != null) continue;
            AssetManager.powers.add(new GodPower
            {
                id = id,
                name = id,
                hold_action = true,
                show_tool_sizes = true,
                unselect_when_window = true,
                click_power_action = PaintTile,
                click_power_brush_action = Stroke
            });
        }
    }

    // 笔刷盖到的一格
    private static bool PaintTile(WorldTile tile, GodPower power)
    {
        if (power == null || !ById.TryGetValue(power.id, out var brush)) return false;
        if (tile?.zone?.city == null)
        {
            if (tile?.Type != null && !tile.Type.liquid) _outside++;
            return false;
        }
        if (TilePlanSystem.Paint(tile, brush.use, brush.feature, brush.use == null && brush.feature == null)) _painted++;
        return true;
    }

    // 按住时每帧调一次：笔刷盖到的格逐一刷上(重画、划功能区由 TilePlanSystem 隔一会儿统一结算)
    private static bool Stroke(WorldTile center, GodPower power)
    {
        if (center == null || power == null || World.world == null) return false;
        _painted = _outside = 0;
        World.world.loopWithBrush(center, Brush.get(Config.current_brush), PaintTile, power);
        // 整笔都落在不归任何城市的地上：提示一下(隔两秒才再提示)
        if (_painted == 0 && _outside > 0 && Time.unscaledTime >= _tipAt)
        {
            _tipAt = Time.unscaledTime + 2f;
            WorldTip.showNow(LM.Get("farm_planning_no_city"), false, "top", 1.5f);
        }
        return _painted > 0;
    }
}
