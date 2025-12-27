namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftActorAssetLibrary
{
    public static void init()
    {
        var assetLib = AssetManager.actor_library;
        assetLib.clone("easternHuman", "$civ_advanced_unit$");
        assetLib.t.name_template_sets = AssetLibrary<ActorAsset>.a<string>("human_default_set", "human_slavic_set", "human_germanic_set", "human_rus_set", "human_posh_set", "human_folk_set", "human_pomeranian_set", "human_frankish_set", "human_rome_set", "human_iberian_set", "human_monolux_set");
        assetLib.t.addPreferredColors("blue", "navy", "teal", "cyan");
        assetLib.t.build_order_template_id = "build_order_advanced";
        assetLib.t.music_theme = "Humans_Neutral";
        assetLib.t.kingdom_id_wild = "nomads_human";
        assetLib.t.kingdom_id_civilization = "easternHuman";
        assetLib.t.banner_id = "easternHuman";
        assetLib.t.architecture_id = "easternHuman";
        assetLib.t.name_locale = "EasternHuman";
        assetLib.t.name_taxonomic_kingdom = "animalia";
        assetLib.t.name_taxonomic_phylum = "chordata";
        assetLib.t.name_taxonomic_class = "mammalia";
        assetLib.t.name_taxonomic_order = "primates";
        assetLib.t.name_taxonomic_family = "hominidae";
        assetLib.t.name_taxonomic_genus = "homo";
        assetLib.t.name_taxonomic_species = "sapiens";
        assetLib.t.icon = "iconEasternHuman";
        assetLib.t.color_hex = "#005E72";
        assetLib.t.zombie_color_hex = "#00AD2C";
        assetLib.t.disable_jump_animation = true;
        assetLib.t.base_stats["mass_2"] = 65f;
        assetLib.t.addGenome(("health", 100f), ("stamina", 100f), ("mutation", 1f), ("bonus_sex_random", 2f), ("bad", 2f), ("lifespan", 70f), ("damage", 15f), ("speed", 15f), ("offspring", 5f), ("diplomacy", 3f), ("warfare", 3f), ("stewardship", 3f), ("intelligence", 3f));
        assetLib.t.addSubspeciesTrait("reproduction_strategy_viviparity");
        assetLib.t.addSubspeciesTrait("gestation_long");
        assetLib.t.addSubspeciesTrait("reproduction_sexual");
        assetLib.t.addSubspeciesTrait("bad_genes");
        assetLib.t.addSubspeciesTrait("advanced_hippocampus");
        assetLib.t.addSubspeciesTrait("stomach");
        assetLib.t.addSubspeciesTrait("amygdala");
        assetLib.t.addSubspeciesTrait("wernicke_area");
        assetLib.t.addSubspeciesTrait("diet_omnivore");
        assetLib.t.addSubspeciesTrait("polyphasic_sleep");
        assetLib.t.addSubspeciesTrait("nocturnal_dormancy");
        assetLib.t.addClanTrait("divine_dozen");
        assetLib.t.addCultureTrait("city_layout_the_grand_arrangement");
        assetLib.t.addCultureTrait("city_layout_stone_garden");
        assetLib.t.addCultureTrait("roads");
        assetLib.t.addCultureTrait("statue_lovers");
        assetLib.t.addCultureTrait("pep_talks");
        assetLib.t.addCultureTrait("youth_reverence");
        assetLib.t.addCultureTrait("expansionists");
        assetLib.t.addLanguageTrait("nicely_structured_grammar");
        assetLib.t.addReligionTrait("bloodline_bond");
        assetLib.t.addReligionTrait("rite_of_roaring_skies");
        assetLib.t.addReligionTrait("cast_shield");
        assetLib.t.production = new string[2]{ "bread", "pie" };
        assetLib.addPhenotype("skin_light");
        assetLib.addPhenotype("skin_dark");
        assetLib.addPhenotype("skin_mixed");
        assetLib.clone("elf", "$civ_advanced_unit$");
        assetLib.t.name_template_sets = AssetLibrary<ActorAsset>.a<string>("elf_default_set");
        assetLib.t.addPreferredColors("green", "lime", "lavender");
        assetLib.t.kingdom_id_wild = "nomads_elf";
        assetLib.t.kingdom_id_civilization = "elf";
        assetLib.t.banner_id = "elf";
        assetLib.t.architecture_id = "elf";
        assetLib.t.build_order_template_id = "build_order_advanced";
        assetLib.t.music_theme = "Elves_Neutral";
        assetLib.t.name_locale = "Elf";
        assetLib.t.name_taxonomic_kingdom = "animalia";
        assetLib.t.name_taxonomic_phylum = "chordata";
        assetLib.t.name_taxonomic_class = "mammalia";
        assetLib.t.name_taxonomic_order = "primates";
        assetLib.t.name_taxonomic_family = "hominidae";
        assetLib.t.name_taxonomic_genus = "elvus";
        assetLib.t.name_taxonomic_species = "elegance";
        assetLib.t.collective_term = "group_quiver";
        assetLib.t.icon = "iconElves";
        assetLib.t.color_hex = "#005D00";
        assetLib.t.zombie_color_hex = "#2C8D98";
        assetLib.t.civ_base_cities = 3;
        assetLib.t.family_limit = 20;
        assetLib.t.base_stats["mass_2"] = 25f;
        assetLib.t.addGenome(("health", 70f), ("bonus_sex_random", 1f), ("stamina", 200f), ("lifespan", 500f), ("mutation", 2f), ("damage", 10f), ("speed", 20f), ("offspring", 2f), ("diplomacy", 5f), ("warfare", 2f), ("stewardship", 2f), ("intelligence", 6f));
        assetLib.t.addCultureTrait("bow_lovers");
        assetLib.t.addCultureTrait("spear_lovers");
        assetLib.t.addCultureTrait("solitude_seekers");
        assetLib.t.addCultureTrait("youth_reverence");
        assetLib.t.addCultureTrait("reading_lovers");
        assetLib.t.addCultureTrait("attentive_readers");
        assetLib.t.addCultureTrait("animal_whisperers");
        assetLib.t.addCultureTrait("true_roots");
        assetLib.t.addCultureTrait("legacy_keepers");
        assetLib.t.addCultureTrait("unbroken_chain");
        assetLib.t.addCultureTrait("city_layout_pillars");
        assetLib.t.addClanTrait("blood_pact");
        assetLib.t.addClanTrait("divine_dozen");
        assetLib.t.addClanTrait("witchs_vein");
        assetLib.t.addLanguageTrait("melodic");
        assetLib.t.addLanguageTrait("magic_words");
        assetLib.t.addSubspeciesTrait("reproduction_strategy_viviparity");
        assetLib.t.addSubspeciesTrait("gestation_very_long");
        assetLib.t.addSubspeciesTrait("reproduction_sexual");
        assetLib.t.addSubspeciesTrait("death_grow_tree");
        assetLib.t.addSubspeciesTrait("long_lifespan");
        assetLib.t.addSubspeciesTrait("advanced_hippocampus");
        assetLib.t.addSubspeciesTrait("stomach");
        assetLib.t.addSubspeciesTrait("amygdala");
        assetLib.t.addSubspeciesTrait("wernicke_area");
        assetLib.t.addSubspeciesTrait("diet_frugivore");
        assetLib.t.addSubspeciesTrait("diet_granivore");
        assetLib.t.addSubspeciesTrait("diet_florivore");
        assetLib.t.addSubspeciesTrait("diet_folivore");
        assetLib.t.addSubspeciesTrait("pure");
        assetLib.t.addKingdomTrait("tax_rate_local_low");
        assetLib.t.addKingdomTrait("tax_rate_tribute_low");
        assetLib.t.addReligionTrait("rite_of_living_harvest");
        assetLib.t.addReligionTrait("rite_of_entanglement");
        assetLib.t.addReligionTrait("cast_grass_seeds");
        assetLib.addTrait("weightless");
        assetLib.addTrait("moonchild");
        assetLib.addTrait("soft_skin");
        assetLib.t.disable_jump_animation = true;
        assetLib.t.production = new string[4]
        {
          "bread",
          "jam",
          "sushi",
          "cider"
        };
        assetLib.addPhenotype("skin_light");
        assetLib.addPhenotype("skin_mixed");
        assetLib.addPhenotype("mid_gray", "biome_corrupted");
        assetLib.addPhenotype("skin_purple", "biome_celestial");
        assetLib.t.addResource("meat", 1, true);
        assetLib.t.addResource("bones", 1);
    }
}