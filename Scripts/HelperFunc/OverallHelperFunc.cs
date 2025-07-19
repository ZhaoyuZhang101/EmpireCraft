using EmpireCraft.Scripts.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class OverallHelperFunc
    {
        public static bool IsEmpireLayerOn()
        {
            return PlayerConfig.dict["map_empire_layer"].boolVal;
        }

        public static class IdGenerator
        {
            private static long _lastId = DateTime.UtcNow.Ticks;

            public static long NextId()
            {
                return Interlocked.Increment(ref _lastId);
            }
        }
        public static string GetCultureFromSpecies(string species)
        {
            if (ConfigData.speciesCulturePair.TryGetValue(species, out var insertCulture))
            {
                return insertCulture;
            }
            else
            {
                return "Western";
            }
        }
        public static void SetFamilyCityPre(this Family family, bool has_pre = true)
        {
            if (family.data.custom_data_bool == null)
            {
                family.data.custom_data_bool = new CustomDataContainer<bool>();
            }
            family.data.custom_data_bool.dict["has_city_pre"] = has_pre;
        }
        public static bool HasBeenSetBefored(this Family family)
        {
            if (family.data.custom_data_bool == null)
            {
                family.data.custom_data_bool = new CustomDataContainer<bool>();
            }
            return family.data.custom_data_bool.dict.ContainsKey("has_city_pre");
        }
    }
}
