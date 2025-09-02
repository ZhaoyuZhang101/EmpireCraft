using EmpireCraft.Scripts.Data;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches
{
    public interface GamePatch
    {
        public ModDeclare declare { get; set; }
        public void Initialize();
    }

    public static class HelperFunc
    {
        public static string getFamilyName(this Family family)
        {

            var nameParts = family.name.Split('\u200A');
            if (family.data.custom_data_bool==null)
            {
                family.data.custom_data_bool = new CustomDataContainer<bool>();
            }
            bool has_city_pre = false;
            if (family.data.custom_data_bool.Keys.Contains("has_city_pre"))
            {
                has_city_pre = family.data.custom_data_bool["has_city_pre"];
            }
            if (has_city_pre)
            {
                nameParts = nameParts.Skip(1).ToArray();
            }
            return nameParts[0].Split(' ').Last();
        }
    }
}
