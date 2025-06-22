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
        public static City selected_cityA;
        [JsonIgnore]
        public static City selected_cityB;

        public static List<string> yearNameSubspecies = new List<string>() 
        {
            "Human"
        };
        public static Dictionary<string, string> speciesCulturePair = new Dictionary<string, string>() 
        {
            {"human","Huaxia"},
            {"elf", "Western" },
            {"dwarf", "Youmu" },
            {"orc", "Youmu" }
        };
        [JsonIgnore]
        public static Empire EMPIRE = null;
    }
}
