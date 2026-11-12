using System;
using System.Linq;
using ai.behaviours;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.CityAI;

// 造船：沿用原版码头造船(渔船、商船、运输船)，但运输船要本城文化研究了远洋航海才造，之前改造渔船——
// 早期不能乘运输船跨海殖民(群友反馈"铜器时代跨州殖民")。
// 留在原版"produce_boat"任务里(InPlace)：并进模组大任务时排在"城市参与叛乱"后面，那一步对帝国都城、无主城返回 Stop，
// 这些城就永远轮不到造船。仓里金子不够时缺的由城库付(见 BoatFunding)
public class EmpireCraftBehCheckBoat: GameAICityBase
{
    public override bool InPlace => true;

    private static bool OceanGoing(City city)
    {
        string culture = EmpireCraft.Scripts.GeneralSystems.TechnologySystem.GetCultureOf(city);
        return culture != null && EmpireCraft.Scripts.GeneralSystems.TechnologySystem.HasTech(culture,
            EmpireCraft.Scripts.GeneralSystems.SettlementSeaSearch.OceanNavigationTech);
    }

    public override Type OriginalBeh => typeof(CityBehProduceBoat);
    public override BehResult execute(City pCity)
    {
        if (pCity == null) return BehResult.Continue;
        if (pCity.timer_build_boat > 0f)
        {
            return BehResult.Continue;
        }
        Building buildingOfType = pCity.getBuildingOfType("type_docks", pCountOnlyFinished: true, pRandom: true);
        if (buildingOfType == null)
        {
            return BehResult.Continue;
        }
        Actor actor = BuildBoatFromHere(buildingOfType.component_docks, pCity);
        if (actor == null)
        {
            return BehResult.Continue;
        }
        actor.joinKingdom(pCity.kingdom);
        actor.joinCity(pCity);
        pCity.timer_build_boat = 10f;
        return BehResult.Continue;
    }

    private Actor BuildBoatFromHere(Docks dock, City pCity)
    {
        ActorAsset boatAssetToBuild = null;
        try
        {

            boatAssetToBuild = dock.building.asset.getRandomBoatAssetToBuild(pCity);
        }
        catch
        {
            // Passengers are unloaded by ActorPatch.Die before the boat is destroyed.
            foreach (Actor boat in pCity._boats.ToList()) boat.dieAndDestroy(AttackType.Other);
            return null;
        }
        if (boatAssetToBuild == null)
            return (Actor) null;
        // 运输船(载人跨海建城、运兵)要研究远洋航海；之前码头只造渔船
        if (boatAssetToBuild.boat_type == "boat_type_transport" && !OceanGoing(pCity))
        {
            // 有的码头(如部分文化的)没有渔船类型：直接不造(以前拿空键查表，每次抛异常刷日志)
            string fishing = dock.building.asset.boat_type_fishing;
            boatAssetToBuild = string.IsNullOrEmpty(fishing) ? null : AssetManager.actor_library.get(fishing);
            if (boatAssetToBuild == null) return (Actor) null;
        }
        if (dock.countBoatTypes(boatAssetToBuild.boat_type) >= 1)
            return (Actor) null;
        int money = EmpireCraft.Scripts.GeneralSystems.BoatFunding.Plan(pCity, boatAssetToBuild.cost);
        if (money < 0)
            return (Actor) null;
        if (dock.tiles_ocean.Count() == 0)
        {
            dock.recalculateOceanTiles();
            return (Actor) null;
        }
        WorldTile random = dock.tiles_ocean.GetRandom<WorldTile>();
        if (!random.region.island.goodForDocks())
            return (Actor) null;
        Actor newUnit = World.world.units.createNewUnit(boatAssetToBuild.id, random);
        if (newUnit == null)
            return (Actor) null;
        dock.addBoatToDock(newUnit);
        EmpireCraft.Scripts.GeneralSystems.BoatFunding.Pay(pCity, boatAssetToBuild.cost, money);
        return newUnit;
    }
}
