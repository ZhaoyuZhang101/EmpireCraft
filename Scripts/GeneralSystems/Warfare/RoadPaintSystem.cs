using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 神力画的一格路：等级、路况
public sealed class PaintedRoad
{
    public int grade;
    public float condition = 1f;
}

// 神力画的路(用户 2026-10-22 "加一个绘制道路按钮"，选"并进模组路网""照常养护")：按格存(格编号 → 等级、路况)，随存档保存。
//   · 画(笔刷，见 RoadPowers)：陆地格按所在国家科技能修的最高一级(无主之地画土路)，不超过所在城的风格上限
//     (游牧营地这类不修路的城领地上不画，见 CityStyleSystem.RoadGradeCap)；山、高峰、城墙(与路网寻路同口径)不画，
//     河湖浅水要能架桥的等级(官道起)才画得过去，海面不画；已有更高等级的不降；
//   · 擦：笔刷盖到的格擦掉(各国修的干道不受影响)；
//   · 并进路网(见 RoadNetwork.CollectRoads)：显示、军团寻路与修出来的路一样；画的时候至多每 RedrawSeconds 秒重画一次路网图层；
//   · 养护(每年，见 RoadBuildingSystem.YearTick)：每格按同等级干道造价的 5% 折算(一个地形单位约 TilesPerUnit 格)，
//     由所在国家出、统一了的帝国中央代付；付不起或无主之地没人养的路况变差，三年失修降一等，土路就荒了；
//     交战的城的领地上每月受损(同干道)。
public static class RoadPaintSystem
{
    public const int TilesPerUnit = 8;
    private const int BridgeGrade = 3;
    // 画的时候至多隔这么久整套重收一次路格、重画一次路网图层(整张贴图上传不便宜)
    private const float RedrawSeconds = 0.5f;

    private static Dictionary<int, PaintedRoad> _tiles = new();
    private static bool _pending;
    private static float _flushedAt = -10f;

    public static IReadOnlyDictionary<int, PaintedRoad> Tiles => _tiles;

    public static void ResetWorldState()
    {
        _tiles = new Dictionary<int, PaintedRoad>();
        _pending = true;
        RoadNetwork.MarkDirty();
    }

    public static void Load(Dictionary<int, PaintedRoad> saved)
    {
        _tiles = saved ?? new Dictionary<int, PaintedRoad>();
        _tiles = _tiles.Where(pair => pair.Value != null && pair.Value.grade > 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        _pending = true;
        RoadNetwork.MarkDirty();
    }

    public static Dictionary<int, PaintedRoad> Save() => _tiles;

    public static WorldTile TileOf(int id)
    {
        WorldTile[] list = World.world?.tiles_list;
        return list != null && id >= 0 && id < list.Length ? list[id] : null;
    }

    // 这一格画路画几等(画不上为 0)：画路与城池全图一键连路的寻路(见 CityPlanWorks.ConnectAll)同一个规矩
    public static int GradeAt(WorldTile tile)
    {
        if (tile?.data == null || tile.Type == null || tile.Type.ocean || Blocked(tile)) return 0;
        City city = tile.zone_city;
        // 不修路的城(游牧营地等)的领地上不画，同修路的规矩(见 RoadBuildingSystem.Refresh)
        if (city?.data != null && !city.isRekt() && CityStyleSystem.Roadless(city)) return 0;
        int cap = city?.data != null && !city.isRekt() ? CityStyleSystem.RoadGradeCap(city) : RoadBuildingSystem.MaxGrade;
        int grade = Mathf.Min(city?.kingdom != null && !city.kingdom.wild ? RoadBuildingSystem.GradeFor(city.kingdom) : 1, cap);
        if (grade <= 0) return 0;
        // 河湖浅水：能架桥的等级才画得过去
        return tile.Type.liquid && grade < BridgeGrade ? 0 : grade;
    }

    // 画一格；画上了(或升了等级)返回 true
    public static bool Paint(WorldTile tile)
    {
        int grade = GradeAt(tile);
        if (grade <= 0) return false;
        int id = tile.data.tile_id;
        if (_tiles.TryGetValue(id, out PaintedRoad road) && road.grade >= grade) return false;
        _tiles[id] = new PaintedRoad { grade = grade };
        _pending = true;
        RoadNetwork.MarkDirty();
        return true;
    }

    public static bool Erase(WorldTile tile)
    {
        if (tile?.data == null || !_tiles.Remove(tile.data.tile_id)) return false;
        _pending = true;
        RoadNetwork.MarkDirty();
        return true;
    }

    // 山、高峰、城墙(同路网寻路，见 RoadNetwork.Mountain；关城的关门照走)、岩浆
    private static bool Blocked(WorldTile tile) =>
        tile.Type.mountains || tile.Type.block && !tile.Type.wall || MapWallSystem.Blocks(tile) || tile.Type.lava;

    // 路网变了要马上重画(神力连路，见 RoadBuildingSystem.ConnectByPower)
    public static void RequestRedraw() => _pending = true;

    // 路网图层该重画了(画、擦过，至多每 RedrawSeconds 秒一次；见 RoadOverlay)：顺带把路格并进路网
    public static bool TakeRedraw(float now)
    {
        if (!_pending || now - _flushedAt < RedrawSeconds && now >= _flushedAt) return false;
        _pending = false;
        _flushedAt = now;
        RoadNetwork.RefreshNow();
        return true;
    }

    // 交战的城的领地上每月受损(同干道)
    internal static bool MonthlyWarDamage(float amount)
    {
        if (_tiles.Count == 0 || FrontSystem.Battles.Count == 0) return false;
        bool changed = false;
        foreach (int id in _tiles.Keys.ToList())
        {
            City city = TileOf(id)?.zone_city;
            if (city?.data != null && FrontSystem.Battles.ContainsKey(city.data.id)) changed |= Damage(id, amount);
        }
        return changed;
    }

    // 养护：按所在国家合计，出得起(或统一帝国中央代付)的路况回升，出不起与无主之地的变差
    internal static bool YearlyUpkeep(float decay)
    {
        if (_tiles.Count == 0) return false;
        var bills = new Dictionary<Kingdom, (List<int> tiles, float cost)>();
        var unkept = new List<int>();
        foreach (var pair in _tiles)
        {
            Kingdom owner = TileOf(pair.Key)?.zone_city?.kingdom;
            if (owner?.data == null || owner.isRekt() || owner.wild)
            {
                unkept.Add(pair.Key);
                continue;
            }
            bills.TryGetValue(owner, out var bill);
            bill.tiles ??= new List<int>();
            bill.tiles.Add(pair.Key);
            bill.cost += RoadBuildingSystem.UpkeepPerTile(pair.Value.grade);
            bills[owner] = bill;
        }
        bool changed = false;
        foreach (var pair in bills)
        {
            int cost = Mathf.CeilToInt(pair.Value.cost);
            Kingdom payer = pair.Key.GetMoney() >= cost ? pair.Key : RoadBuildingSystem.CentralPayer(pair.Key);
            if (payer != null && payer.GetMoney() >= cost)
            {
                if (cost > 0) payer.SubMoney(cost, TreasuryCategory.Maintenance);
                foreach (int id in pair.Value.tiles) _tiles[id].condition = Mathf.Min(1f, _tiles[id].condition + 0.25f);
                continue;
            }
            unkept.AddRange(pair.Value.tiles);
        }
        foreach (int id in unkept) changed |= Damage(id, decay);
        return changed;
    }

    // 路况变差；坏到底降一等，土路就荒了
    private static bool Damage(int id, float amount)
    {
        if (!_tiles.TryGetValue(id, out PaintedRoad road)) return false;
        road.condition -= amount;
        if (road.condition > 0f) return false;
        road.condition = 1f;
        if (--road.grade <= 0) _tiles.Remove(id);
        _pending = true;
        return true;
    }
}
