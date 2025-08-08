using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Prefabs;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class CityExtension
{
    public class CityExtraData: ExtraDataBase
    {
        // todo: 添加需要存储的城市数据
    }
    public static CityExtraData GetOrCreate(this City a, bool isSave=false)
    {
        var ed = a.GetOrCreate< City, CityExtraData>(isSave);
        return ed;
    } 
    
    public static void Clear()
    {
        ExtensionManager<City, CityExtraData>.Clear();
    }
    // 城市是否属于自定义层级
    public static bool IsInModLayer(this City a)
    {
        return ModClass.ModLayer_MANAGER.ToList().Any(pModLayer => pModLayer.city_list_hash.Contains(a));
    }
    // 获取城市所在的自定义层级
    public static ModLayer GetModLayer(this City a)
    {
        return ModClass.ModLayer_MANAGER.ToList().FirstOrDefault(pModLayer => pModLayer.city_list_hash.Contains(a));
    }
}