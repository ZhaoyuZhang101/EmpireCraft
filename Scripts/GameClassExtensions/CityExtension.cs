using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Prefabs;
using System;
using System.Collections.Generic;
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
}