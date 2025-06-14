using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class CityExtension
{
    private static readonly ConditionalWeakTable<City, ExtraData> _data
        = new ConditionalWeakTable<City, ExtraData>();
    private class ExtraData
    {
        public string kingdom_names = "";
    }

    public static string GetKingdomNames(this City city)
    {
        return _data.GetOrCreateValue(city).kingdom_names;
    }
    public static void SetKingdomNames(this City city, string value)
    {
        _data.GetOrCreateValue(city).kingdom_names = value;
    }

    public static Empire GetEmpire(this City city)
    {
        if (city == null) return null;
        if (city.kingdom == null) return null;
        return ModClass.EMPIRE_MANAGER.get(city.kingdom.GetEmpireID());
    }
    
    public static void AddKingdomName(this City city, string kingdomName)
    {
        if (!_data.GetOrCreateValue(city).kingdom_names.Contains(kingdomName))
        {
            _data.GetOrCreateValue(city).kingdom_names = String.Join(" ", _data.GetOrCreateValue(city).kingdom_names,kingdomName);
            LogService.LogInfo("添加历史帝国名称"+ _data.GetOrCreateValue(city).kingdom_names);
        }
    }
    public static string SelectKingdomName(this City city)
    {
        return _data.GetOrCreateValue(city).kingdom_names.Split(' ').GetRandom();
    }

    public static void removeExtensionData(this City city)
    {
        LogService.LogInfo($"{city.name}" + "移除，正在清除数据");
        _data.Remove(city);
    }

}