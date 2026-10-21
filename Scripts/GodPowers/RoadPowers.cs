using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GodPowers;

// 道路神力(用户 2026-10-22 "加一个绘制道路按钮和点击一个城市再点击另一个城市自动把道路连起来的两个按钮")：
//   · 绘制道路 / 擦除道路：按住拖动的笔刷(大小用原版笔刷大小，拖快了中间自动补上)，见 RoadPaintSystem；
//   · 连路：先点一座城、再点另一座城，沿相邻城之间的干道当场修通一串(见 RoadBuildingSystem.ConnectByPower)；
//     再点同一座城取消；隔了一分钟没点第二座城的从头选。
public static class RoadPowers
{
    public const string Paint = "road_paint", Erase = "road_erase", Connect = "road_connect";
    public static readonly string[] Ids = { Paint, Erase, Connect };
    private const float PickSeconds = 60f;
    private static City _first;
    private static float _pickedAt = -1000f;

    public static void Init()
    {
        foreach (string id in new[] { Paint, Erase })
        {
            if (AssetManager.powers.get(id) != null) continue;
            AssetManager.powers.add(new GodPower
            {
                id = id,
                name = id,
                hold_action = true,
                show_tool_sizes = true,
                unselect_when_window = true,
                draw_lines = true,
                surprises_units = false,
                click_power_action = Brush,
                click_power_brush_action = Stroke
            });
        }
        if (AssetManager.powers.get(Connect) == null)
            AssetManager.powers.add(new GodPower
            {
                id = Connect,
                name = Connect,
                unselect_when_window = true,
                click_action = ConnectClick
            });
    }

    private static bool Brush(WorldTile tile, GodPower power) =>
        power?.id == Erase ? RoadPaintSystem.Erase(tile) : RoadPaintSystem.Paint(tile);

    private static bool Stroke(WorldTile center, GodPower power)
    {
        if (center == null || power == null || World.world == null) return false;
        World.world.loopWithBrush(center, global::Brush.get(Config.current_brush), Brush, power);
        return true;
    }

    private static bool ConnectClick(WorldTile tile, string power)
    {
        City city = tile?.zone_city;
        float now = Time.unscaledTime;
        if (city?.data == null || city.isRekt())
        {
            WorldTip.showNow(LM.Get("road_connect_no_city"), false, "top", 1.5f);
            return false;
        }
        if (_first?.data == null || _first.isRekt() || now - _pickedAt > PickSeconds)
        {
            _first = city;
            _pickedAt = now;
            WorldTip.showNow(string.Format(LM.Get("road_connect_first"), city.GetCityName()), false, "top", 2f);
            return true;
        }
        City first = _first;
        _first = null;
        if (first == city)
        {
            WorldTip.showNow(LM.Get("road_connect_cancel"), false, "top", 1.5f);
            return true;
        }
        int segments = RoadBuildingSystem.ConnectByPower(first, city, out int built);
        string from = first.GetCityName(), to = city.GetCityName();
        string text = segments < 0
            ? RoadNetwork.PlansComplete
                ? string.Format(LM.Get("road_connect_none"), from, to)
                : LM.Get("road_connect_planning")
            : built == 0
                ? string.Format(LM.Get("road_connect_already"), from, to)
                : string.Format(LM.Get("road_connect_done"), from, to, built, segments);
        WorldTip.showNow(text, false, "top", 3f);
        return segments >= 0;
    }
}
