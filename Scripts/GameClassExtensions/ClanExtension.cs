using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;

namespace EmpireCraft.Scripts.GameClassExtensions;
public static class ClanExtension
{
    public class ClanExtraData
    {
        public long id;
        //历史国家
        public string historical_empire_name;
        //历史国家首都地
        public Vector2 position;
        public long original_capital;
    }
    public static ClanExtraData GetOrCreate(Clan a, bool isSave = false)
    {
        var ed = ExtensionManager<Clan, ClanExtraData>.GetOrCreate(a, isSave);
        return ed;
    }

    public static string GetClanName(this Clan clan, ActorSex sex = ActorSex.None, bool hasSexPost = false)
    {
        var nameParts = clan.name.Split(' ');
        if (ConfigData.speciesCulturePair.TryGetValue(clan.species_id, out var culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (nameParts.Length - 1 >= setting.Clan.name_pos)
                {
                    return hasSexPost ? nameParts[setting.Clan.name_pos] + LM.Get($"{culture}_sex_post_{sex.ToString()}"): nameParts[setting.Clan.name_pos];
                }
            }
        }
        return hasSexPost ? nameParts[0]+LM.Get($"{culture}_sex_post_{sex.ToString()}"): nameParts[0];
    }

    public static void RemoveExtraData(this Clan a)
    {
        if (a == null) return;
        ExtensionManager<Clan, ClanExtraData>.Remove(a);
    }

    public static void Clear()
    {
        ExtensionManager<Clan, ClanExtraData>.Clear();
    }
    public static bool syncData(this Clan a, ClanExtraData actorExtraData)
    {
        var ed = GetOrCreate(a);
        ed.id = actorExtraData.id;
        ed.historical_empire_name = actorExtraData.historical_empire_name;
        ed.position = actorExtraData.position;
        return true;
    }

    public static ClanExtraData getExtraData(this Clan a, bool isSave = false)
    {
        if (GetOrCreate(a, isSave) == null) return null;
        ClanExtraData data = new ClanExtraData();
        data.id = a.getID();
        data.historical_empire_name = a.GetHistoryEmpireName();
        data.position = a.GetHistoryEmpirePos();
        return data;
    }

    public static bool HasHistoryEmpire(this Clan a)
    {
        string name = GetOrCreate(a).historical_empire_name;
        return name != null && name!="";
    }

    public static void RecordHistoryEmpire(this Clan __instance, Empire empire)
    {
        if (empire == null) return;
        Kingdom kingdom = empire.empire;
        if (kingdom == null) return;
        if (!kingdom.hasCapital()) return;
        kingdom.capital.updateCityCenter();
        GetOrCreate(__instance).id = __instance.getID();
        GetOrCreate(__instance).position = empire.original_capital.city_center;
        GetOrCreate(__instance).historical_empire_name = empire.GetEmpireName();
        GetOrCreate(__instance).original_capital = empire.original_capital.isAlive() ? empire.original_capital.data.id : -1L;
    }

    public static void ClearHistoricalName(this Clan __instance)
    {
        GetOrCreate(__instance).id = __instance.getID();
        GetOrCreate(__instance).historical_empire_name = "";
    }

    public static string GetHistoryEmpireName(this Clan __instance)
    {
        return GetOrCreate(__instance).historical_empire_name;
    }   

    public static City GetHistoryCapital(this Clan __instance)
    {
        return World.world.cities.get(GetOrCreate(__instance).original_capital);
    }    
    public static void SetHistoryEmpireName(this Clan __instance, string name)
    {
        GetOrCreate(__instance).historical_empire_name = name;
    }

    public static Vector2 GetHistoryEmpirePos(this Clan __instance)
    {
        return GetOrCreate(__instance).position;
    }    
    public static void SetHistoryEmpirePos(this Clan __instance, Vector2 pos)
    {
        GetOrCreate(__instance).position = pos;
    }

}