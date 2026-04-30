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
        public static BureauSetting CURRENT_SELECTED_BUREAU_SETTING;
        [JsonIgnore]
        public static string CURRENT_SELECTED_BUREAU_CTX;
        [JsonIgnore]
        public static City selected_cityA;
        [JsonIgnore]
        public static City selected_cityB;
        [JsonIgnore]
        public static FixedFaction CURRENT_SELECTED_FACTION;
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
            {"Cultiway.EasternHuman", "Huaxia" }, // 东方人族

            // 山海经
            {"Cultiway.Ming", "Shanhai" }, // 冥族
            {"civ_rabbit", "Shanhai" }, // 兔族
            {"civ_fox", "Shanhai" }, // 狐族

            // 精灵幻想
            {"elf","ElfFancy"}, // 精灵

            // 法兰克
            {"human", "Frankish" }, // 人类
            {"civ_chicken","Frankish"}, // 鸡族
            {"civ_acid_sentleman", "Frankish" }, // 酸液绅士

            // 维京
            {"dwarf", "Viking" }, // 矮人
            {"civ_crystal_golem", "Viking" }, // 水晶魔像
            {"civ_candy_man", "Viking" }, // 糖果人

            // 蒙古
            {"orc", "Youmu" }, // 兽人
            {"civ_sheep", "Youmu" }, // 羊族
            {"civ_demon", "Youmu" }, // 恶魔

            // 巨企
            {"alien", "Corporate" }, // 外星族
            {"civ_seal", "Corporate" }, // 突击海豹
            {"civ_unicorn", "Corporate" }, // 摇滚独角

            // 罗马
            {"civ_wolf", "Roma" }, // 狼族
            {"civ_angle", "Roma" }, // 天使族
            {"white_mage", "Roma" }, // 白法师

            // 日本
            {"civ_lemon_man", "Japan" }, // 柠檬人
            {"civ_turtle", "Japan" }, // 龟族
            {"civ_dog", "Japan" }, // 犬族

            // 波斯
            {"civ_rhino", "Persepolis" }, // 犀牛族
            {"civ_garlic_man", "Persepolis" }, // 大蒜人
            {"civ_crab", "Persepolis" }, // 蟹族

            // 阿拉伯
            {"civ_armadillo", "Arab" }, // 犰狳族
            {"civ_scorpion", "Arab" }, // 蝎族
            {"civ_buffalo", "Arab" }, // 水牛族

            // 日耳曼
            {"civ_cow", "Germanic" }, // 牛族
            {"civ_druid", "Germanic" }, // 德鲁伊
            {"bandit", "Germanic" }, // 盗匪

            // 犹太
            {"civ_hyena", "Kosher" }, // 鬣狗族
            {"civ_goat", "Kosher" }, // 山羊族
            {"evil_mage", "Kosher" }, // 邪恶法师

            // 斯拉夫
            {"civ_penguin", "Slavonic" }, // 企鹅族
            {"civ_bear", "Slavonic" }, // 熊族
            {"civ_snowman", "Slavonic" }, // 雪人族

            // 埃及
            {"civ_cat", "Egypt" }, // 猫族
            {"necromancer", "Egypt" }, // 死灵法师
            {"civ_beetle", "Egypt" }, // 甲虫族

            // 阿兹特克
            {"civ_crocodile", "Aztec" }, // 鳄族
            {"civ_frog", "Aztec" }, // 蛙族
            {"civ_liliar", "Aztec" }, // 莉莉安族

            // 奥吉布瓦
            {"civ_alpaca", "Ojibwe" }, // 羊驼族
            {"civ_capybara", "Ojibwe" }, // 水豚族
            {"civ_snake", "Ojibwe" }, // 蛇族

            // 印度
            {"civ_rat", "India" }, // 鼠族
            {"civ_piranha", "India" }, // 食人鱼族
            {"civ_monkey", "India" }, // 猴族
        };
        //Already Prepared Cultures
        public static List<string> currentExistCulture => OnomasticsRule.ALL_CULTURE_RULE.Keys.ToList();
        [JsonIgnore]
        public static Empire EMPIRE = null;
        public static bool IS_ORIGINAL_WAR_LOGIC = false;
    }
}
