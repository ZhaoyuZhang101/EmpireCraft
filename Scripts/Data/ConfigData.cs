using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.Data
{
    public static class ConfigData
    {
        [JsonIgnore]
        public static Empire CURRENT_SELECTED_EMPIRE;
        [JsonIgnore]
        public static KingdomTitle CURRENT_SELECTED_TITLE;
        [JsonIgnore]
        public static Province CURRENT_SELECTED_PROVINCE;
        [JsonIgnore]
        public static EmpireCraftHistory CURRENT_SELECTED_HISTORY;
        [JsonIgnore]
        public static City selected_cityA;
        [JsonIgnore]
        public static City selected_cityB;

        //this coverd all civ species in the game
        public static List<string> AllCivSpecies = new List<string>()
    {
            "human", "orc","elf","dwarf","civ_necromancer","civ_alien",
            "civ_druid","civ_bee","civ_beetle","civ_evil_mage","civ_white_mage",
            "civ_bandit","civ_demon","civ_cold_one","civ_angle","civ_snowman",
            "civ_garlic_man","civ_lemon_man","civ_acid_gentleman","civ_crystal_golem",
            "civ_candy_man","civ_liliar","civ_greg","civ_cat","civ_dog","civ_chicken",
            "civ_rabbit","civ_monkey","civ_fox","civ_sheep","civ_cow","civ_armadillo",
            "civ_wolf","civ_bear","civ_rhino","civ_buffalo","civ_hyena","civ_rat",
            "civ_alpaca","civ_capybara","civ_goat","civ_scorpion","civ_crab",
            "civ_penguin","civ_turtle","civ_crocodile","civ_snake","civ_frog","civ_piranha",
    };
        public static List<string> yearNameSubspecies = new List<string>() 
        {
            "Human"
        };
        // this part bind culture and species togather.
        public static Dictionary<string, string> speciesCulturePair = new Dictionary<string, string>() 
        {
            {"human","Western"},
            {"elf", "Huaxia" },
            {"dwarf", "Western" },
            {"orc", "Youmu" }
        };
        //Already Prepared Cultures
        public static List<string> currentExistCulture = new List<string>()
        {
            "Western","Huaxia","Youmu"
        };
        [JsonIgnore]
        public static Empire EMPIRE = null;
        public static bool PREVENT_CITY_DESTROY = false;
    }
}
