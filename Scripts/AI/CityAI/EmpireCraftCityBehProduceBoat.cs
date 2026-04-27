using System;
using System.Linq;
using ai.behaviours;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.CityAI;

public class EmpireCraftBehCheckBoat: GameAICityBase
{
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
            pCity._boats.ForEach(b=>b.die(true));
            return null;
        }
        if (boatAssetToBuild == null)
            return (Actor) null;
        if (dock.countBoatTypes(boatAssetToBuild.boat_type) >= 1)
            return (Actor) null;
        if (!pCity.hasEnoughResourcesFor(boatAssetToBuild.cost))
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
        pCity.spendResourcesForBuildingAsset(boatAssetToBuild.cost);
        return newUnit;
    }
}
