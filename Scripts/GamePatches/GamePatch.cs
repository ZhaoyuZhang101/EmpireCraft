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
        public static string GetFamilyName(this Family family)
        {
            if (family == null || string.IsNullOrWhiteSpace(family.name))
                return "";

            var nameParts = family.name.Split(new[] { '\u200A' }, StringSplitOptions.RemoveEmptyEntries);

            if (nameParts.Length == 0)
                return "";

            family.data.custom_data_bool ??= new CustomDataContainer<bool>();

            bool hasCityPre = family.data.custom_data_bool.Keys.Contains("has_city_pre")
                              && family.data.custom_data_bool["has_city_pre"];

            if (hasCityPre)
            {
                // 关键：Skip 后可能为空
                if (nameParts.Length <= 1) return "";
                nameParts = nameParts.Skip(1).ToArray();
            }

            if (nameParts.Length == 0 || string.IsNullOrWhiteSpace(nameParts[0]))
                return "";

            return nameParts[0]
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault() ?? "";
        }
    }
}
