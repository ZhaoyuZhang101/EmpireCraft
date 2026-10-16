using System.Collections.Generic;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 街区级标注(见 UI/Components/MapLabelOverlay)要的平面图数据：建成地块上每个街区的建筑、城心与地标、街格线。
public partial class PureMapPatch
{
    internal readonly struct LabelSlot
    {
        public readonly Vector2 Tile;   // 街区中心(格坐标)
        public readonly string Icon;    // 建筑种类(house_N、market_N、civic_N、industry_N、temple、barracks、docks……)
        public readonly int Zone, Index;

        public LabelSlot(Vector2 tile, string icon, int zone, int index)
        {
            Tile = tile;
            Icon = icon;
            Zone = zone;
            Index = index;
        }
    }

    internal sealed class LabelPlan
    {
        public readonly List<LabelSlot> Slots = new();
        public readonly List<TileZone> Built = new();
        public float Modern;
        public string Family = "huaxia";
        public int Stamp;
    }

    // 这座城平面图上能标注的东西(纯净地图画城市时)
    internal static LabelPlan LabelsOf(City city)
    {
        if (city?.data == null || city.isRekt() || city.zones == null) return null;
        CityPlan plan = PlanFor(city);
        var result = new LabelPlan { Modern = plan.modern, Family = plan.family ?? "huaxia", Stamp = StampOf(city) };
        int size = Config.CITY_ZONE_SIZE, half = size / 2;
        foreach (TileZone zone in city.zones)
        {
            if (zone == null || !plan.built.Contains(zone.id)) continue;
            result.Built.Add(zone);
            if (plan.landmark.Contains(zone.id) || !plan.zones.TryGetValue(zone.id, out Slot[] slots)) continue;
            int ox = zone.x * size, oy = zone.y * size;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].used || string.IsNullOrEmpty(slots[i].icon)) continue;
                if (plan.ruin > 0f && Hash01(zone.id * 4L + i + 7L) < plan.ruin) continue;
                result.Slots.Add(new LabelSlot(new Vector2(ox + i % 2 * half + half / 2f, oy + i / 2 * half + half / 2f),
                    slots[i].icon, zone.id, i));
            }
        }
        return result;
    }
}
