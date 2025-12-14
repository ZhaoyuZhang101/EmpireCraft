using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.api.attributes;
using UnityEngine;

namespace EmpireCraft.Scripts.Data
{
    public static class ConfigData
    {
        [JsonIgnore]
        public static KingdomTitle CURRENT_SELECTED_TITLE;
        [JsonIgnore]
        public static EmpireCraftHistory CURRENT_SELECTED_HISTORY;
        [JsonIgnore]
        public static OfficeObject CURRENT_SELECTED_OFFICE;
        [JsonIgnore]
        public static City selected_cityA;
        [JsonIgnore]
        public static City selected_cityB;

        public static List<ActorAsset> AllCivSpecies => AssetManager.actor_library.list.FindAll(a => a.civ);
        //this coverd all civ species in the game
        public static List<string> yearNameSubspecies = new() 
        {
            "Huaxia", "Japan"
        };
        // this part bind culture and species together.

        public static Dictionary<string, string> speciesCulturePair { get; set; } = new()
        {
            // 华夏
            {"human", "Huaxia" }, // 人类
            {"civ_rabbit", "Huaxia" }, // 兔族

            // 法兰克
            {"elf","Frankish"}, // 精灵
            {"civ_chicken","Frankish"}, // 鸡族

            // 维京
            {"dwarf", "Viking" }, // 矮人
            {"civ_crystal_golem", "Viking" }, // 水晶魔像

            // 蒙古
            {"orc", "Youmu" }, // 兽人
            {"bandit", "Youmu" }, // 盗匪

            // 巨企
            {"alien", "Corporate" }, // 外星族
            {"civ_acid_sentleman", "Corporate" }, // 酸液绅士
            {"civ_seal", "Corporate" }, // 突击海豹
            {"civ_unicorn", "Corporate" }, // 摇滚独角

            // 罗马
            {"civ_wolf", "Roma" }, // 狼族
            {"civ_angle", "Roma" }, // 天使族
            {"white_mage", "Roma" }, // 白法师
            {"civ_demon", "Roma" }, // 恶魔

            // 日本
            {"civ_lemon_man", "Japan" }, // 柠檬人
            {"civ_fox", "Japan" }, // 狐族
            {"civ_turtle", "Japan" }, // 龟族
            {"civ_dog", "Japan" }, // 犬族

            // 阿拉伯
            {"civ_rhino", "Arab" }, // 犀牛族
            {"civ_armadillo", "Arab" }, // 犰狳族
            {"civ_scorpion", "Arab" }, // 蝎族
            {"civ_buffalo", "Arab" }, // 水牛族

            // 日耳曼
            {"civ_sheep", "Germanic" }, // 羊族
            {"civ_cow", "Germanic" }, // 牛族
            {"civ_druid", "Germanic" }, // 德鲁伊
            {"civ_garlic_man", "Germanic" }, // 大蒜人

            // 犹太
            {"civ_snake", "Kosher" }, // 蛇族
            {"civ_hyena", "Kosher" }, // 鬣狗族
            {"civ_goat", "Kosher" }, // 山羊族
            {"evil_mage", "Kosher" }, // 邪恶法师

            // 斯拉夫
            {"civ_penguin", "Slavonic" }, // 企鹅族
            {"civ_bear", "Slavonic" }, // 熊族
            {"civ_snowman", "Slavonic" }, // 雪人族
            {"civ_candy_man", "Slavonic" }, // 糖果人

            // 埃及
            {"civ_cat", "Egypt" }, // 猫族
            {"necromancer", "Egypt" }, // 死灵法师
            {"civ_beetle", "Egypt" }, // 甲虫族
            {"civ_crab", "Egypt" }, // 蟹族

            // 阿兹特克
            {"civ_alpaca", "Aztec" }, // 羊驼族
            {"civ_capybara", "Aztec" }, // 水豚族
            {"civ_crocodile", "Aztec" }, // 鳄族
            {"civ_frog", "Aztec" }, // 蛙族

            // 印度
            {"civ_liliar", "India" }, // 莉莉安族
            {"civ_rat", "India" }, // 鼠族
            {"civ_piranha", "India" }, // 食人鱼族
            {"civ_monkey", "India" }, // 猴族
        };

        //Already Prepared Cultures
        public static List<string> currentExistCulture = new List<string>()
        {
            "Western","Huaxia","Youmu","Frankish","Slavonic", "Roma", "Japan", "Arab", "Germanic","Kosher","Kosher","Viking"
        };
        [JsonIgnore]
        public static Empire EMPIRE = null;
        public static bool IS_ORIGINAL_WAR_LOGIC = false;
    }
}
