using System.Collections.Generic;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftBuildingLibrary
{
    public static List<string> VALID_CULTURE_BUILDING = new List<string> { "Huaxia", "Western", "Youmu", "Arabic" };
    public static void init()
    {
        var lib = AssetManager.buildings;
        foreach (var culture in VALID_CULTURE_BUILDING)
        {
            string main_path(int level) => $"buildings/{culture}/city_{culture.ToLower()}_{level}";
            
            string CultureBuildingName(int level) => $"city_{culture.ToLower()}_{level}";
            lib.clone(CultureBuildingName(0), "$building_civ_human$");
            lib.t.type = "type_house";
            lib.t.cost = new ConstructionCost(200);
            lib.t.fundament = new BuildingFundament(3, 3, 1, 0);
            lib.t.scale_base *= 0.6f;
            lib.t.can_be_upgraded = true;
            lib.t.remove_buildings_when_dropped  = true;
            lib.t.remove_civ_buildings = true;
            lib.t.build_road_to = true;
            lib.t.can_be_living_house = true;
            lib.t.ignore_other_buildings_for_upgrade = true;
            lib.t.max_houses = 0;
            lib.t.setHousingSlots(150);
            lib.t.has_kingdom_color = true;
            lib.t.upgrade_level = 1;
            lib.t.loot_generation = 1;
            lib.t.housing_happiness = 300;
            lib.t.burnable = false;
            lib.t.upgrade_to = CultureBuildingName(1);
            lib.t.base_stats["health"] = 5000;
            lib.t.build_place_batch = true;
            lib.t.has_sprite_construction = false;
            lib.t.has_ruins_graphics = false;
            lib.t.produce_biome_food = true;
            lib.t.has_ruin_state = false;
            lib.t.sprite_path = main_path(0);
            lib.t.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingGeneric";
            lib.t.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingGeneric";
            LogService.LogInfo("建筑地址=>"+main_path(0));
            
            lib.clone(CultureBuildingName(1), CultureBuildingName(0));
            lib.t.cost = new ConstructionCost(100);
            lib.t.setHousingSlots(300);
            lib.t.fundament = new BuildingFundament(4, 4, 2, 0);
            lib.t.upgrade_level = 2;
            lib.t.max_houses = 0;
            lib.t.loot_generation = 2;
            lib.t.housing_happiness = 500;
            lib.t.upgrade_to = CultureBuildingName(2);
            lib.t.upgraded_from = CultureBuildingName(1);
            lib.t.base_stats["health"] = 8000;
            lib.t.has_sprite_construction = false;
            lib.t.sprite_path = main_path(1);
            lib.t.sound_hit = "event:/SFX/HIT/HitWood";
            lib.t.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingWood";
            lib.t.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingWood";
            
            lib.clone(CultureBuildingName(2), CultureBuildingName(1));
            lib.t.cost = new ConstructionCost(100);
            lib.t.setHousingSlots(500);
            lib.t.fundament = new BuildingFundament(7, 7, 3, 0);
            lib.t.upgrade_level = 3;
            lib.t.max_houses = 0;
            lib.t.loot_generation = 3;
            lib.t.housing_happiness = 800;
            lib.t.can_be_upgraded = false;
            lib.t.upgrade_to = string.Empty;
            lib.t.upgraded_from = CultureBuildingName(1);
            lib.t.base_stats["health"] = 10000;
            lib.t.has_sprite_construction = false;
            lib.t.sprite_path = main_path(2);
            lib.t.sound_hit = "event:/SFX/HIT/HitWood";
            lib.t.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingWood";
            lib.t.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingWood";
        }
        lib.linkAssets();
    }
}