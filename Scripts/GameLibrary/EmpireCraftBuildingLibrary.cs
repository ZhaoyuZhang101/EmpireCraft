namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftBuildingLibrary
{
    public static void init()
    {
        var lib = AssetManager.buildings;
        lib.clone("city_huaxia_base", "$building_civ_human$");
        lib.t.type = "type_house";
        lib.t.cost = new ConstructionCost(200);
        lib.t.fundament = new BuildingFundament(4, 4, 4, 0);
        lib.t.can_be_upgraded = true;
        lib.t.setHousingSlots(200);
        lib.t.loot_generation = 1;
        lib.t.housing_happiness = 20;
        lib.t.burnable = true;
        lib.t.upgrade_to = "city_huaxia_1";
        lib.t.base_stats["health"] = 300f;
        lib.t.build_place_batch = true;
        lib.t.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingGeneric";
        lib.t.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingGeneric";
    }
}