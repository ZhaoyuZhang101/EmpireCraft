using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using UnityEngine;
namespace EmpireCraft.Scripts.GameClassExtensions;
public static class ClanExtension
{
    public class ClanExtraData:ExtraDataBase
    {
        public string historical_empire_name;
        //Historical empire position
        public float x = -1L;
        public float y = -1L;
        public long original_capital;
        public double specific_clan_id = -1L;
    }


    public static ClanExtraData GetOrCreate(this Clan a, bool isSave = false)
    {
        var ed = a.GetOrCreate<Clan, ClanExtraData>(isSave);
        return ed;
    }
    public static bool HasSpecificClan(this Clan a)
    {
        if (a == null) return false;
        return a.GetOrCreate().specific_clan_id != -1L;
    }
    public static SpecificClan GetSpecificClan(this Clan a)
    {
        if (a == null) return null;
        var ed = a.GetOrCreate();
        return SpecificClanManager.Get(ed.specific_clan_id);
    }
    public static void SetSpecificClan(this Clan a, SpecificClan specificClan)
    {
        if (a == null) return;
        a.GetOrCreate().specific_clan_id = specificClan.id;
    }
    public static void RemoveSpecificClan(this Clan a)
    {
        if (a == null) return;
        a.GetOrCreate().specific_clan_id = -1L;
    }

    public static string GetClanName(this Clan clan, ActorSex sex = ActorSex.None, bool hasSexPost = false)
    {
        var nameParts = clan.name.Split('\u200A');
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
        GetOrCreate(__instance).x = empire.original_capital.city_center.x;
        GetOrCreate(__instance).y = empire.original_capital.city_center.y;
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
        return new Vector2(GetOrCreate(__instance).x, GetOrCreate(__instance).y);
    }    
    public static void SetHistoryEmpirePos(this Clan __instance, Vector2 pos)
    {
        GetOrCreate(__instance).x = pos.x;
        GetOrCreate(__instance).y = pos.y;
    }

}