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
        // 城名标在城北沿上方(格坐标)；城外的镇(中心、大小)、村(中心)，见 UrbanFabric
        public Vector2 NameAt = new(float.NaN, float.NaN);
        public readonly List<(Vector2 at, int size)> Towns = new();
        public readonly List<Vector2> Villages = new();
        // 各处镇村所在的那一格(名字记在存档里，见 RuralSettlementSystem)；废墟(标注处、所在格)
        public readonly List<(int x, int y)> TownTiles = new(), VillageTiles = new();
        // 关城(标注处、所在格)
        public readonly List<(Vector2 at, int x, int y)> Forts = new();
        public readonly List<(Vector2 at, int x, int y)> Ruins = new();
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
        // 城名：城心那一列上、建成地块北沿再往上两格
        if (result.Built.Count > 0)
        {
            int top = int.MinValue;
            foreach (TileZone zone in result.Built) top = Mathf.Max(top, zone.y * size + size);
            float x = plan.gateX != int.MinValue ? plan.gateX + 0.5f : city.getTile()?.x ?? result.Built[0].x * size + half;
            result.NameAt = new Vector2(x, top + 2f);
        }
        if (plan.fabric != null)
        {
            foreach ((int x, int y, int townSize, _) in plan.fabric.Towns)
            {
                result.Towns.Add((new Vector2(x + 0.5f, y + townSize + 2.5f), townSize));
                result.TownTiles.Add((x, y));
            }
            foreach ((int x, int y) in plan.fabric.Villages)
            {
                result.Villages.Add(new Vector2(x + 0.5f, y + 3f));
                result.VillageTiles.Add((x, y));
            }
            foreach ((int x, int y, int level) in plan.fabric.Forts)
                result.Forts.Add((new Vector2(x + 0.5f, y + level + 4.5f), x, y));
            foreach ((int x, int y, bool town) in plan.fabric.Ruins)
                result.Ruins.Add((new Vector2(x + 0.5f, y + (town ? 5.5f : 3f)), x, y));
        }
        return result;
    }
}
