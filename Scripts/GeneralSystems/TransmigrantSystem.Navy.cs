using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 伏波军海军(用户 2026-11-04 "临高的海军应该是及其发达的"，定额与代差见 AosongNavyRules，造舰、海战见 NavySystem)：
//   · 全军一支海军：元老院、各大区、殖民地都算，科技都按澳洲人的。只看本国和本国的帝国中央、宗主，
//     混在别国帝国里的澳宋(伪装成属国时)不把那个帝国也算进来；
//   · 沿海的城都设基地：原版的码头要小人盖、盖好了还可能被拆，存档里元老院自己的城常年没有码头，舰队无港可归，
//     每月折损两成，降临带的船几个月就没了。每月每国补一座，当场建成(同 BuildTown)；
//   · 前进基地：加煤站随身带来，辐射比只有蒸汽铁甲时远一截。旧档每年补上
public static partial class TransmigrantSystem
{
    private const string NavalBases = "coaling_stations";

    public static TransmigrantState NavalState(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        TransmigrantState own = Of(kingdom) ?? Of(kingdom.GetEmpire()?.CoreKingdom);
        if (own != null) return own;
        Kingdom metropole = ColonySystem.MetropoleOf(kingdom);
        return metropole == null ? null : Of(metropole) ?? Of(metropole.GetEmpire()?.CoreKingdom);
    }

    // 同一支海军的各国(元老院、大区、殖民地)；不是澳宋返回空
    public static List<Kingdom> NavalRealm(Kingdom kingdom)
    {
        var realm = new List<Kingdom>();
        TransmigrantState state = NavalState(kingdom);
        if (state == null || World.world?.kingdoms == null) return realm;
        foreach (Kingdom other in World.world.kingdoms)
            if (ReferenceEquals(NavalState(other), state)) realm.Add(other);
        return realm;
    }

    public static void EnsureBases(Kingdom kingdom)
    {
        if (NavalState(kingdom) == null || kingdom.cities == null) return;
        foreach (City city in kingdom.cities.ToList())
        {
            if (city?.data == null || city.isRekt() || NavySystem.Port(city) != null ||
                !city.zones.Any(zone => zone != null && zone.tiles_with_liquid > 0)) continue;
            try
            {
                Building dock = city.getBuildingOfType("type_docks", pCountOnlyFinished: false, pRandom: false);
                if (dock == null)
                {
                    BuildingAsset asset = city.getActorAsset()?.getBuildingDockAsset();
                    WorldTile tile = asset == null ? null : ai.behaviours.CityBehBuild.getDockTile(city);
                    if (tile == null) continue;
                    dock = World.world.buildings.addBuilding(asset, tile);
                }
                if (dock?.asset != null && dock.isUnderConstruction())
                {
                    dock.updateBuild(Mathf.Max(1, dock.asset.construction_progress_needed + 1 - dock.getConstructionProgress()));
                    if (city.under_construction_building == dock) city.under_construction_building = null;
                }
                LogService.LogInfo($"[EmpireCraft][穿越] {city.GetCityName()} 设海军基地");
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][穿越] {city.GetCityName()} 设海军基地失败: {exception.Message}");
            }
            return;
        }
    }

    private static void EnsureNavalTechs()
    {
        if (!TechnologySystem.HasTech(Culture, NavalBases)) TechnologySystem.ForceResearch(Culture, NavalBases);
    }
}
