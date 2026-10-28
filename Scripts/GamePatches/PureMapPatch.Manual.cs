using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 手动规划(城池全图，见 CityPlanEditing；用户 2026-10-28 要一个全屏的城池界面，"随意改城的画法")：
// 存档里记着玩家的改动(CityExtraData.plan_*)，自动规划算完之后、铺城市肌理之前照改(见 Layout)：
//   · 手动城区(见 CityZoningSystem；用户 2026-10-28 "要显示出城市的实际面积形状")：城区就是城区表里本城的地块连同城心，
//     整块画成城区(不看城的轮廓，见 UrbanFabric.InTown)，城长大不再自己往外画；建筑按实际所在的地块画(见 PlaceActual)；
//   · 旧档只有并入、划出两张表(还没进手动城区)的照旧：划出的地块不画成城区(城心、宫城不划)，并入的本城地块整块画成城区；
//   · 宫城挪到指定的地块(并入城区；不是都城也照画——上帝工具)，大城照旧连北面一块(手动城区要北面那块也在城区里)；
//   · 城墙(这一系没有城墙画法的强开无效)、护城河(要有城墙)、中轴开关；
//   · 中轴交点：城门中线跟着交点走，四门开在中轴与城墙相交处；城的轮廓仍以城心那块地为中，不跟着挪；
//   · 原始聚落照旧不画城墙、中轴、宫城；营地(游牧、林地)没有中轴。
// 城市以后长大、改建不覆盖这些改动，交还规矩才清掉；领地不变，原版建筑跟着城区表盖拆(见 CityZoningSystem.Sync)
public partial class PureMapPatch
{
    private static void ApplyManual(CityPlan plan, City city, CityExtension.CityExtraData extra)
    {
        plan.blocks = extra.plan_blocks is 1 or 2 ? extra.plan_blocks : 0;
        if (!CityPlanEditing.HasOverrides(extra)) return;
        ZoneCalculator calc = World.world?.zone_calculator;
        if (calc == null) return;
        TileZone Own(int id)
        {
            TileZone zone = id >= 0 ? calc.getZoneByID(id) : null;
            return zone != null && zone.city == city ? zone : null;
        }
        HashSet<int> urban = CityZoningSystem.UrbanSet(city);
        HashSet<int> excluded = urban == null && extra.plan_zones_out != null && extra.plan_zones_out.Count > 0
            ? new HashSet<int>(extra.plan_zones_out) : null;
        bool tribal = plan.tribal >= 0;
        // 手动城区：城区按表(连同城心)整块画；自动定的宫城北面那块不在城区里就不连(埃及城外的金字塔照旧画在城外)
        if (urban != null)
        {
            plan.built.Clear();
            plan.forced.Clear();
            foreach (int id in urban)
            {
                plan.built.Add(id);
                plan.forced.Add(id);
            }
            if (plan.family == "egypt")
                foreach (int id in plan.landmark) plan.built.Add(id);
            else plan.landmark.RemoveWhere(id => !urban.Contains(id));
        }
        // 宫城挪到指定的地块：原来只为宫城才算建成的地块(都城大城北面那块、埃及城外的金字塔)退回去
        if (!tribal && Own(extra.plan_palace_zone) is TileZone palace)
        {
            foreach (int id in plan.landmark)
                if (id != plan.centerZone && !plan.zones.ContainsKey(id) && urban?.Contains(id) != true) plan.built.Remove(id);
            plan.landmark.Clear();
            plan.landmark.Add(palace.id);
            plan.built.Add(palace.id);
            TileZone north = calc.getZone(palace.x, palace.y + 1);
            if (Arranged(plan.family) && plan.built.Count >= 9 && north != null && north.city == city && excluded?.Contains(north.id) != true &&
                urban?.Contains(north.id) != false)
            {
                plan.landmark.Add(north.id);
                plan.built.Add(north.id);
            }
        }
        // 划出(城心、宫城不划)，再并入(只认本城的地块，划出的不并)；手动城区上面已按表画过
        if (excluded != null)
            foreach (int id in excluded)
                if (id != plan.centerZone && !plan.landmark.Contains(id)) plan.built.Remove(id);
        if (urban == null && extra.plan_zones_in != null)
            foreach (int id in extra.plan_zones_in)
                if (excluded?.Contains(id) != true && Own(id) != null)
                {
                    plan.built.Add(id);
                    plan.forced.Add(id);
                }
        if (tribal) return;
        if (extra.plan_walls == 0) plan.walled = false;
        else if (extra.plan_walls == 1 && WallOf(plan.family) != null) plan.walled = true;
        if (extra.plan_moat == 0) plan.moat = false;
        else if (extra.plan_moat == 1) plan.moat = true;
        if (!plan.walled) plan.moat = false;
        // 营地没有中轴；没有城心那块地的城没有城门中线
        if (Camp(plan.family) || plan.gateX == int.MinValue) return;
        if (extra.plan_axis == 0) plan.axis = false;
        else if (extra.plan_axis == 1) plan.axis = true;
        // 中轴交点落在本城画成城区的地块上才算(地块后来划出去、丢了的照自动)
        if (extra.plan_axis_x != int.MinValue && World.world.GetTile(extra.plan_axis_x, extra.plan_axis_y) is WorldTile cross &&
            cross.zone != null && cross.zone.city == city && plan.built.Contains(cross.zone.id))
        {
            plan.gateX = cross.x;
            plan.gateY = cross.y;
        }
        if (plan.axis)
        {
            plan.axisX = plan.gateX;
            plan.axisY = plan.gateY;
        }
    }

    // ───────── 城池全图(见 CityPlanEditing)用的平面图要点 ─────────
    internal sealed class PlanFacts
    {
        // WallsDown：近代城墙已拆成环路(城墙开着时画的是环路，见 UrbanFabric.Geometry.WallsDown)
        public bool Walled, Moat, Axis, Tribal, Camp, CanWall, WallsDown;
        // 城门中线交点(中轴开着时就是中轴交点；没有为 int.MinValue)、宫城地块(-1 没有)、城心地块、画成城区的地块数
        public int GateX = int.MinValue, GateY = int.MinValue, PalaceZone = -1, CenterZone = -1, TownZones;
        public string Family;
    }

    // 按已算好的平面图，没有就现算一份
    internal static PlanFacts FactsOf(City city)
    {
        if (city?.data == null || city.isRekt() || city.zones == null) return null;
        CityPlan plan = CachedPlan(city) ?? PlanFor(city);
        var facts = new PlanFacts
        {
            Walled = plan.walled && plan.tribal < 0, Moat = plan.moat && plan.walled && plan.tribal < 0, Axis = plan.axis && plan.tribal < 0,
            Tribal = plan.tribal >= 0, Camp = Camp(plan.family), CanWall = WallOf(plan.family) != null && plan.tribal < 0,
            WallsDown = plan.fabric?.WallsDown == true,
            GateX = plan.gateX, GateY = plan.gateY, CenterZone = plan.centerZone, TownZones = plan.built.Count, Family = plan.family
        };
        // 宫城：拼块式的大宫城连北面一块，取南边那块(宫城本身所在)
        ZoneCalculator calc = World.world?.zone_calculator;
        int lowest = int.MaxValue;
        foreach (int id in plan.landmark)
        {
            TileZone zone = calc?.getZoneByID(id);
            if (zone == null || zone.y >= lowest) continue;
            lowest = zone.y;
            facts.PalaceZone = id;
        }
        return facts;
    }

    // 这块地眼下画成城区(有一格画成城区就算；宫城、手动并入的整块算)
    internal static bool ZoneInTown(City city, TileZone zone)
    {
        if (city?.data == null || city.isRekt() || zone?.tiles == null || zone.city != city) return false;
        CityPlan plan = CachedPlan(city) ?? PlanFor(city);
        if (!plan.built.Contains(zone.id)) return false;
        if (plan.landmark.Contains(zone.id) || plan.forced.Contains(zone.id)) return true;
        foreach (WorldTile tile in zone.tiles)
            if (tile != null && Urban(tile, plan)) return true;
        return false;
    }

    // 眼下画成城区的地块(城池全图头一回改规划时记成手动城区，见 CityZoningSystem.Enter)：同 ZoneInTown；埃及城外的金字塔不算
    internal static List<int> TownZoneIds(City city)
    {
        var ids = new List<int>();
        if (city?.data == null || city.isRekt() || city.zones == null) return ids;
        CityPlan plan = CachedPlan(city) ?? PlanFor(city);
        foreach (TileZone zone in city.zones)
        {
            if (zone?.tiles == null || !plan.built.Contains(zone.id)) continue;
            if (plan.landmark.Contains(zone.id))
            {
                if (plan.family != "egypt" || zone.id == plan.centerZone) ids.Add(zone.id);
                continue;
            }
            bool town = plan.forced.Contains(zone.id);
            foreach (WorldTile tile in zone.tiles)
            {
                if (town) break;
                town = tile != null && Urban(tile, plan);
            }
            if (town) ids.Add(zone.id);
        }
        return ids;
    }

    // 这块地是宫城(都城地标)的一部分
    internal static bool LandmarkZone(City city, TileZone zone)
    {
        if (city?.data == null || city.isRekt() || zone == null || zone.city != city) return false;
        return (CachedPlan(city) ?? PlanFor(city)).landmark.Contains(zone.id);
    }

    // 这一格画成本城的城区
    internal static bool TileInTown(City city, WorldTile tile)
    {
        if (city?.data == null || city.isRekt() || tile?.zone == null || tile.zone.city != city) return false;
        return Urban(tile, CachedPlan(city) ?? PlanFor(city));
    }

    // 一格的地形底色(城池全图的预览打底用)：原版的地表本色，原版铺的路、田、墙格当底下的地面(同 TerrainSystem)；
    // 峰顶、山地压暗，按高差打一点光(同 Shade)
    internal static Color32 TerrainColor(WorldTile tile)
    {
        TileTypeBase type = tile?.Type;
        if (type == null) return new Color32(110, 150, 90, 255);
        TileTypeBase ground = (type.road || type.farm_field || type.wall) && tile.main_type != null ? tile.main_type : type;
        Color32 color = ground.color;
        color.a = 255;
        if (ground.liquid) return color;
        int height = tile.Height;
        int north = tile.tile_up?.Height ?? height, west = tile.tile_left?.Height ?? height;
        float light = Light(ground) + Mathf.Clamp((height * 2 - north - west) * SlopeLight, -SlopeCap, SlopeCap);
        return Scale(color, light);
    }
}
