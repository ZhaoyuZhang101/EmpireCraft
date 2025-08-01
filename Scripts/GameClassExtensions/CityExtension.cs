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
        public string kingdom_names = "";
        public long title_id = -1L;
        public long province_id = -1L;
        public long empire_core_id = -1L;
        public List<long> exam_pass_person;
        public int MAX_POPULATION = 100;
        public bool MAX_POPULATION_LIMIT = false;
        [JsonIgnore]
        public TextInput limitationNumber { get; set; }
        [JsonIgnore]
        public SimpleButton limitToggle { get; set; }
    }
    public static CityExtraData GetOrCreate(this City a, bool isSave=false)
    {
        var ed = a.GetOrCreate< City, CityExtraData>(isSave);
        return ed;
    } 

    public static void SetLimitInput(this City c, TextInput input)
    {
        var ed = c.GetOrCreate();
        ed.limitationNumber = input;
    }

    public static TextInput GetLimitInput(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.limitationNumber;
    }

    public static bool HasReachedPlayerPopLimit(this City c)
    {
        var ed = c.GetOrCreate(); 
        if (c.getPopulationPeople()>c.GetMaxPopulation()&&c.GetMaxPopulationLimitStats())
        {
            return true;
        }
        return false;
    }

    public static void SetLimitToggle(this City c, SimpleButton limitToggle)
    {
        var ed = c.GetOrCreate();
        ed.limitToggle = limitToggle;
    }

    public static SimpleButton GetLimitToggle(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.limitToggle;
    }

    public static int GetMaxPopulation(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.MAX_POPULATION;
    }
    public static void SetMaxPopulation(this City c, int value)
    {
        var ed = c.GetOrCreate();
        ed.MAX_POPULATION = value;
    }
    public static void OpenMaxPopulationLimit(this City c)
    {
        var ed = c.GetOrCreate();
        ed.MAX_POPULATION_LIMIT = true;
    }
    public static bool GetMaxPopulationLimitStats(this City c)
    {
        var ed = c.GetOrCreate();
        return ed.MAX_POPULATION_LIMIT;
    }
    public static void CloseMaxPopulationLimit(this City c)
    {
        var ed = c.GetOrCreate();
        ed.MAX_POPULATION_LIMIT = false;
    }
    public static List<long> GetExamPassPersonIDs(this City c)
    {
        if (GetOrCreate(c).exam_pass_person== null)
        {
            GetOrCreate(c).exam_pass_person = new List<long> {0};
        }
        return GetOrCreate(c).exam_pass_person;
    }
 
    public static long GetEmpireCoreID(this  City a)
    {
        return GetOrCreate(a).empire_core_id;
    }

    public static void SetEmpireCore(this City a, EmpireCore core)
    {
        if (core == null) return;
        GetOrCreate(a).empire_core_id = core.id;
    }

    public static bool hasTitle(this City c)
    {
        if (c == null) return false;
        if (GetOrCreate(c)==null) return false; 
        return GetOrCreate(c).title_id!=-1L;
    }

    public static bool hasProvince(this City c)
    {
        if (c == null) return false;
        if (GetOrCreate(c)==null) return false; 
        return GetOrCreate(c).province_id!=-1L;
    }
    public static void Clear()
    {
        ExtensionManager<City, CityExtraData>.Clear();
    }

    public static long GetTitleID(this City c)
    {
        return GetOrCreate(c).title_id;
    }

    public static long GetProvinceID(this City c)
    {
        return GetOrCreate(c).province_id;
    }

    public static void SetProvinceID(this City c, long id)
    {
        GetOrCreate(c).province_id = id;
    }

    public static void SetTitleID(this City c, long id)
    {
        GetOrCreate(c).title_id = id;
    }

    public static KingdomTitle GetTitle(this City c)
    {
        var ed = GetOrCreate(c);
        if (ed == null) return null;
        return ed.title_id==-1L?null:ModClass.KINGDOM_TITLE_MANAGER.get(ed.title_id);
    }

    public static Province GetProvince(this City c)
    {
        var ed = GetOrCreate(c);
        if (ed == null) return null;
        return ed.province_id == -1L ? null : ModClass.PROVINCE_MANAGER.get(ed.province_id);
    }

    public static void SetTitle(this City c, KingdomTitle title)
    {
        var ed = GetOrCreate(c);
        ed.title_id = title.getID();
    }
    public static void SetProvince(this City c, Province province)
    {
        var ed = GetOrCreate(c);
        ed.province_id = province.getID();
    }

    public static void RemoveTitle(this City c)
    {
        GetOrCreate(c).title_id = -1L;
    }

    public static void RemoveProvince(this City c)
    {
        GetOrCreate(c).province_id = -1L;
    }

    public static string GetCityName(this City city)
    {
        if (city == null) return null;
        if (city.name == null || city.name == "") return null;
        string[] nameParts = city.name.Split('\u200A');

        if (ConfigData.speciesCulturePair.TryGetValue(city.getSpecies(), out var culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (nameParts.Length-1 >= setting.City.name_pos)
                {
                    return nameParts[setting.City.name_pos].Split(' ').Last();
                }
            }
        }
        return nameParts[0].Split(' ').Last();
    }

    public static string GetKingdomNames(this City city)
    {
        return GetOrCreate(city).kingdom_names;
    }
    public static void SetKingdomNames(this City city, string value)
    {
        GetOrCreate(city).id = city.getID();
        GetOrCreate(city).kingdom_names = value;
    }

    public static Empire GetEmpire(this City city)
    {
        if (city == null) return null;
        if (city.kingdom == null) return null;
        return ModClass.EMPIRE_MANAGER.get(city.kingdom.GetEmpireID());
    }
    
    public static void AddKingdomName(this City city, string kingdomName)
    {
        if (!GetOrCreate(city).kingdom_names.Contains(kingdomName))
        {
            GetOrCreate(city).kingdom_names = String.Join("\u200A", GetOrCreate(city).kingdom_names,kingdomName);
        }
    }
    public static string SelectKingdomName(this City city)
    {
        return GetOrCreate(city).kingdom_names.Split('\u200A').GetRandom();
    }

    public static bool HasKingdomName(this City city) 
    {
        return GetOrCreate(city).kingdom_names == null || GetOrCreate(city).kingdom_names == "";
    }

}