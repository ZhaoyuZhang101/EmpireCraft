using EmpireCraft.Scripts.Data;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// 此处为通用补丁的基类，如果需要修改游戏原版方法，可以直接在该文件目录下创建新的类别，并继承此类，如此在游戏初始化的时候可自动导入补丁
// 目前已经提供了一些基础的常用补丁类，可以根据需要自行补全
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

            var nameParts = family.name.Split('\u200A');
            family.data.custom_data_bool ??= new CustomDataContainer<bool>();
            var has_city_pre = false;
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
