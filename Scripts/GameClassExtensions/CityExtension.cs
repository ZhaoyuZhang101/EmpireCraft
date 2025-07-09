using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using EpPathFinding.cs;
using HarmonyLib;
using NeoModLoader.services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class CityExtension
{
    public class CityExtraData
    {
        public long id;
        public string kingdom_names = "";
        public long title_id = -1L;
        public long province_id = -1L;
        public long empire_core_id = -1L;
        public List<long> exam_pass_person;
    }
    public static CityExtraData GetOrCreate(this City a, bool isSave=false)
    {
        var ed = ExtensionManager < City, CityExtraData>.GetOrCreate(a, isSave);
        return ed;
    } 
    public static bool syncData(this City a, CityExtraData cityExtraData)
    {
        var ed = GetOrCreate(a);
        ed.id = cityExtraData.id;
        ed.kingdom_names = cityExtraData.kingdom_names;
        ed.title_id = cityExtraData.title_id;
        ed.province_id = cityExtraData.province_id;
        ed.empire_core_id = cityExtraData.empire_core_id;
        ed.exam_pass_person = cityExtraData.exam_pass_person;
        return true;
    }
    public static CityExtraData getExtraData(this City a, bool isSave = false)
    {
        if (GetOrCreate(a, isSave) == null) return null;
        CityExtraData data = new CityExtraData();
        data.id = a.getID();
        data.kingdom_names = a.GetKingdomNames();
        data.title_id = a.GetTitleID();
        data.province_id = a.GetProvinceID();
        data.empire_core_id = a.GetEmpireCoreID();
        data.exam_pass_person = a.GetExamPassPersonIDs();
        return data;
    }
    public static void AddExamPassPerson(this City c, Actor a)
    {
        if (a == null) return;
        if (GetOrCreate(c).exam_pass_person == null)
        {
            GetOrCreate(c).exam_pass_person = new List<long>();
        }
        GetOrCreate(c).exam_pass_person.Add(a.getID());
    }

    public static List<Actor> GetExamPassPersons(this City a)
    {
        List<Actor> list = GetOrCreate(a).exam_pass_person.Select<long, Actor> (id=>World.world.units.get(id)).ToList();
        return list;
    }

    public static List<long> GetExamPassPersonIDs(this City c)
    {
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

    public static void RemoveExtraData(this City c)
    {
        if (c == null) return;
        ExtensionManager<City, CityExtraData>.Remove(c);
    }

    public static string GetCityName(this City city)
    {
        if (city == null) return null;
        if (city.name == null || city.name == "") return null;
        string[] nameParts = city.name.Split(' ');

        if (ConfigData.speciesCulturePair.TryGetValue(city.getSpecies(), out var culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (nameParts.Length-1 >= setting.City.name_pos)
                {
                    return nameParts[setting.City.name_pos];
                }
            }
        }
        if (nameParts.Length <= 2)
        {
            return nameParts[0];
        }
        else
        {
            return nameParts[nameParts.Length - 2];
        }

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
            GetOrCreate(city).kingdom_names = String.Join(" ", GetOrCreate(city).kingdom_names,kingdomName);
        }
    }
    public static string SelectKingdomName(this City city)
    {
        return GetOrCreate(city).kingdom_names.Split(' ').GetRandom();
    }

    public static bool HasKingdomName(this City city) 
    {
        return GetOrCreate(city).kingdom_names == null || GetOrCreate(city).kingdom_names == "";
    }

}