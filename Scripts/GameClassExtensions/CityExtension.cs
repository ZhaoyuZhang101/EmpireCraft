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
    }
    public static CityExtraData GetOrCreate(this City a, bool isSave=false)
    {
        var ed = ExtensionManager < City, CityExtraData>.GetOrCreate(a, isSave);
        return ed;
    } 
    public static bool syncData(this City a, CityExtraData actorExtraData)
    {
        var ed = GetOrCreate(a);
        ed.id = actorExtraData.id;
        ed.kingdom_names = actorExtraData.kingdom_names;
        ed.title_id = actorExtraData.title_id;
        return true;
    }
    public static CityExtraData getExtraData(this City a, bool isSave = false)
    {
        if (GetOrCreate(a, isSave) == null) return null;
        CityExtraData data = new CityExtraData();
        data.id = a.getID();
        data.kingdom_names = a.GetKingdomNames();
        data.title_id = a.GetTitleID();
        return data;
    }

    public static bool hasTitle(this City c)
    {
        if (c == null) return false;
        if (GetOrCreate(c)==null) return false; 
        return GetOrCreate(c).title_id!=-1L;
    }
    public static void Clear()
    {
        ExtensionManager<City, CityExtraData>.Clear();
    }

    public static long GetTitleID(this City c)
    {
        return GetOrCreate(c).title_id;
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

    public static void SetTitle(this City c, KingdomTitle title)
    {
        var ed = GetOrCreate(c);
        ed.title_id = title.getID();
    }

    public static void RemoveTitle(this City c)
    {
        GetOrCreate(c).title_id = -1L;
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