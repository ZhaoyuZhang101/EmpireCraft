using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 纯沙漠、纯冻土(用户 2026-10-22 "沙漠无法生存的，纯沙漠地段没法建造任何建筑"，问答选：整块全是沙地或荒漠才算、
// 村镇荒废房屋拆掉)：
//   · 一块地里的陆地全是沙地、荒漠(沙漠)，或全是冻土(连冻住的沙地，见 TerrainSystem.BarrenKind)；城心那一块不算；
//   · 住不了人、建不了东西：宜居度为零(民居上限不算它，见 TerrainSystem.Livability)，原版建筑不往上盖
//     (见 IndustryBuildingPatch.AfterIsGoodTile)，村镇、厂区不往上立(见 UrbanFabric、RuralSettlementSystem.Place、FactorySystem.Place)；
//     原有的村镇荒废、房屋拆掉(每月一批，见 Clear)。
// 以前还有"归属"一节：游牧营地收沙漠与冻土、阿拉伯文化的城收沙漠、斯拉夫文化的城收冻土，挨着就领，地块上限与远近按模组设置
// (荒漠冻土地块倍数)放宽。用户 2026-10-29 "之前的沙漠和冻土什么的地块扩展就算了"，改成无人区(见 WildernessSystem)：
// 连片的沙漠、冻土连同山脉、丛林、沼泽整片划成无人区，不归任何城；那一节与那项设置撤掉，剧本开局也不再把整块荒漠冻土分给这几种城
// (见 WorldMapZones.FillAll)
public static class BarrenLandSystem
{
    public const int None = 0, Desert = 1, Frozen = 2;
    // 每月至多拆几座房屋、荒废几处村镇(读旧档时一片沙漠上的东西分几个月拆完，不卡一下)
    private const int ClearPerMonth = 40;
    private static double _lastScan = -1d;

    public static void ResetWorldState() => _lastScan = -1d;

    // 这块地是纯沙漠(1)、纯冻土(2)还是都不是(0)；城心那一块不算
    public static int KindOf(TileZone zone)
    {
        if (zone == null) return None;
        int kind = TerrainSystem.BarrenKind(zone);
        if (kind == None) return None;
        City city = zone.city;
        return city != null && city.getTile()?.zone == zone ? None : kind;
    }

    public static bool Barren(TileZone zone) => KindOf(zone) != None;

    public static bool BarrenAt(int x, int y) => Barren(World.world?.GetTile(x, y)?.zone);

    public static void TryMonthlyScan()
    {
        if (World.world?.cities == null || ModClass.IS_CLEAR || !Config.game_loaded || SmoothLoader.isLoading()) return;
        double now = World.world.getCurWorldTime();
        if (_lastScan >= 0d && now >= _lastScan && Date.getMonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            Clear();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][荒漠冻土] 月结失败: {exception.Message}");
        }
    }

    // 纯沙漠、纯冻土上原有的村镇荒废(关城不动)、房屋拆掉
    private static void Clear()
    {
        int budget = ClearPerMonth;
        var redraw = new HashSet<City>();
        foreach (RuralSettlement settlement in RuralSettlementSystem.Save())
        {
            if (budget <= 0) break;
            if (settlement == null || settlement.fort || settlement.cleared || settlement.ruined_at >= 0d ||
                !BarrenAt(settlement.x, settlement.y)) continue;
            RuralSettlementSystem.Raze(settlement);
            budget--;
        }
        foreach (City city in World.world.cities.list)
        {
            if (budget <= 0) break;
            if (city?.data == null || city.isRekt() || AncientWarfareCompatibility.OwnsObject(city)) continue;
            foreach (Building building in city.buildings.ToList())
            {
                if (budget <= 0) break;
                // 城心的公所不拆(城心那块本来就不算，这里再挡一下)
                if (building?.asset == null || building.isRekt() || building.current_tile == null ||
                    building.asset.type == "type_hall" || !Barren(building.current_tile.zone)) continue;
                building.startDestroyBuilding();
                budget--;
            }
        }
    }
}
