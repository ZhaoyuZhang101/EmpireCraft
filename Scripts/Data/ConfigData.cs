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

        public static Dictionary<FactionType, List<TemporaryFactionType>> FactionConfig =
            new()
            {
                {
                    FactionType.僭主,
                    new List<TemporaryFactionType> { TemporaryFactionType.强者继承法, TemporaryFactionType.宗教同化 }
                },
                {
                    FactionType.血脉,
                    new List<TemporaryFactionType> { TemporaryFactionType.转世袭, TemporaryFactionType.宗教同化 }
                },
                {
                    FactionType.尊王,
                    new List<TemporaryFactionType> { TemporaryFactionType.削藩, TemporaryFactionType.夺取诸侯开战权 }
                },
                {
                    FactionType.自治,
                    new List<TemporaryFactionType> { TemporaryFactionType.分割继承, TemporaryFactionType.允许诸侯自由开战 }
                },
                {
                    FactionType.攘夷,
                    new List<TemporaryFactionType>
                        { TemporaryFactionType.对外扩张, TemporaryFactionType.汉化, TemporaryFactionType.转军府, TemporaryFactionType.谋求统一 }
                },
                {
                    FactionType.绥靖,
                    new List<TemporaryFactionType>
                        { TemporaryFactionType.撤销军府, TemporaryFactionType.提供岁币, TemporaryFactionType.割让城池 }
                },
                {
                    FactionType.共和,
                    new List<TemporaryFactionType>
                    {
                        TemporaryFactionType.提高赋税, TemporaryFactionType.提高福利, TemporaryFactionType.清除移民,
                        TemporaryFactionType.缩减金融霸权
                    }
                },
                {
                    FactionType.民主,
                    new List<TemporaryFactionType>
                    {
                        TemporaryFactionType.开放移民, TemporaryFactionType.降低赋税, TemporaryFactionType.提高福利,
                        TemporaryFactionType.拓展金融霸权
                    }
                },
                {
                    FactionType.革命,
                    new List<TemporaryFactionType> { TemporaryFactionType.输出革命, TemporaryFactionType.扶持革命党 }
                }
            };
        public static List<string> yearNameSubspecies = new() 
        {
            "Huaxia", "Japan"
        };
        // this part bind culture and species together.

        public static Dictionary<string, string> speciesCulturePair { get; set; } = new()
        {
            // 法兰克
            { "human", "Frankish" }, // 人类

            // 华夏
            { "elf", "Huaxia" }, // 精灵

            // 斯拉夫
            { "dwarf", "Slavonic" }, // 矮人

            // 蒙古
            { "orc", "Youmu" }, // 兽人

            // 罗马
            { "civ_wolf", "Roma" }, // 嚎利尔狼
            { "civ_cow", "Roma" }, // 哞陶尔牛
            { "civ_liliar", "Roma" }, // 露莉娅
            { "civ_crystal_golem", "Roma" }, // 水晶戈仑

            // 日本
            { "civ_lemon_man", "Japan" }, // 柠檬人
            { "civ_acid_gentleman", "Japan" }, // 史莱姆绅士
            { "civ_turtle", "Japan" }, // 龟托克
            { "civ_frog", "Japan" }, // 狡爪狐

            // 阿拉伯
            { "civ_cat", "Arab" }, // 喵莫夫
            { "civ_scorpion", "Arab" }, // 斯克普蝎
            { "civ_goat", "Arab" }, // 赫多山羊
            { "civ_crab", "Arab" }, // 蟹布林

            // 日耳曼
            { "civ_rabbit", "Germanic" }, // 霍珀兔
            { "civ_chicken", "Germanic" }, // 喙爵鸡
            { "civ_dog", "Germanic" }, // 巴克狗
            { "civ_piranha", "Germanic" }, // 格纳珀鱼

            // 犹太
            { "civ_sheep", "Kosher" }, // 巴阿金山羊
            { "civ_hyena", "Kosher" }, // 戮爪鬣狗
            { "civ_rat", "Kosher" }, // 尼布鼠
            { "civ_snake", "Kosher" }, // 嘶诺克蛇

            // 维京
            { "civ_penguin", "Viking" }, // 酷喙企鹅
            { "civ_bear", "Viking" }, // 格兰特熊
            { "civ_garlic_man", "Viking" }, // 蒜头人
            { "civ_candy_man", "Viking" }, // 糖果人
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
