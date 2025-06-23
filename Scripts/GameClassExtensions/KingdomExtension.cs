using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class KingdomExtension
{
    public class KingdomExtraData
    {
        public long id = -1L;
        [JsonConverter(typeof(StringEnumConverter))]
        public countryLevel country_level = countryLevel.countrylevel_4;
        public long vassaled_kingdom_id = -1L;
        public long empireID = -1L;
        public double timestamp_empire = -1L;
        public int loyalty = 0;
        public string KingdomNamePre = "";
        public double timestamp_beFeifed = -1L;
        public double taxtRate = 0.1;
        public List<long> OwnedTitle = new List<long>();
    }

    public static double GetTaxtRate(this Kingdom k)
    {
        return GetOrCreate(k).taxtRate;
    }

    public static void SetTaxtRate(this Kingdom k, double value)
    {
        GetOrCreate(k).taxtRate = value;
    }
    public static void IncreaseTaxtRate (this Kingdom k)
    {
        var t = GetOrCreate(k).taxtRate;
        if (t < 1.0)
        {
            t += 0.1;
            k.SetLoyalty(k.GetLoyalty() - 50);
        }
    }
    public static void DecreaseTaxtRate(this Kingdom k)
    {
        var t = GetOrCreate(k).taxtRate;
        if (t > 0.1)
        {
            t -= 0.1;
            k.SetLoyalty(k.GetLoyalty() + 50);
        }
    }
    public static KingdomExtraData GetOrCreate(this Kingdom a)
    {
        var ed = ExtensionManager<Kingdom, KingdomExtraData>.GetOrCreate(a);
        return ed;
    }
    public static bool syncData(this Kingdom a, KingdomExtraData actorExtraData)
    {
        var ed = ExtensionManager<Kingdom, KingdomExtraData>.GetOrCreate(a);
        ed.id = actorExtraData.id;
        ed.country_level = actorExtraData.country_level;
        ed.vassaled_kingdom_id = actorExtraData.vassaled_kingdom_id;
        ed.empireID = actorExtraData.empireID;
        ed.timestamp_empire = actorExtraData.timestamp_empire;
        ed.loyalty = actorExtraData.loyalty;
        ed.KingdomNamePre = actorExtraData.KingdomNamePre;
        ed.OwnedTitle = actorExtraData.OwnedTitle;
        return true;
    }

    public static double GetFiedTimestamp(this Kingdom k)
    {
        return GetOrCreate(k).timestamp_beFeifed;
    }

    public static void SetFiedTimestamp(this Kingdom k, double v)
    {
        GetOrCreate(k).timestamp_beFeifed = v;
    }

    public static KingdomExtraData getExtraData(this Kingdom a)
    {
        KingdomExtraData ed = new KingdomExtraData();
        ed.id = a.getID();
        ed.country_level = a.GetCountryLevel();
        ed.vassaled_kingdom_id = a.GetVassaledKingdomID();
        ed.empireID = a.GetEmpireID();
        ed.timestamp_empire = a.GetTimestampEmpire();
        ed.loyalty = a.GetLoyalty();
        ed.KingdomNamePre = GetOrCreate(a).KingdomNamePre;
        ed.OwnedTitle = GetOrCreate(a).OwnedTitle;
        return ed;
    }

    public static string GetKingdomName(this Kingdom kingdom)
    {
        if (kingdom == null) return null;
        if (kingdom.name == null||kingdom.name == "") return null;

        string[] nameParts = kingdom.name.Split(' ');
        if (nameParts.Length <= 2)
        {
            return nameParts[0];
        }
        else
        {
            return nameParts[nameParts.Length - 2];
        }
    }    
    public static void SetKingdomName(this Kingdom kingdom, string kingdom_name)
    {
        if (kingdom == null) return;
        if (kingdom.name == null||kingdom.name == "") return;

        string[] nameParts = kingdom.name.Split(' ');
        if (nameParts.Length <= 1)
        {
            kingdom.data.name = kingdom_name;
            kingdom.SetCountryLevel(GetOrCreate(kingdom).country_level);
        }
        else if (nameParts.Length == 2)
        {
            kingdom.data.name = kingdom_name + " " + nameParts[1];
        } else if (nameParts.Length == 3)
        {
            kingdom.data.name = String.Join(" ", nameParts[0], kingdom_name, nameParts[2]);
        }
    }

    public static void SetKingdomNamePre(this Kingdom kingdom, string name_pre)
    {
        kingdom.data.name = String.Join(name_pre, kingdom.name);
        GetOrCreate(kingdom).KingdomNamePre = name_pre;
        kingdom.SetCountryLevel(GetOrCreate(kingdom).country_level);
    }

    public static bool isInSameEmpire(this Kingdom kingdom, Kingdom pKingdomTaget)
    {
        if (kingdom == null) return false;
        if (!kingdom.isInEmpire()||!pKingdomTaget.isInEmpire()) return false;
        return kingdom.GetEmpireID() == pKingdomTaget.GetEmpireID();
    }
    public static void Clear()
    {
        ExtensionManager<Kingdom, KingdomExtraData>.Clear();
    }

    public static void SetLoyalty(this Kingdom kingdom, int value)
    {
        GetOrCreate(kingdom).id = kingdom.getID();
        GetOrCreate(kingdom).loyalty = value;
        if (value > 999)
        {
            GetOrCreate(kingdom).loyalty = 999;
        }
        if (value < 0)
        {
            GetOrCreate(kingdom).loyalty = 0;
        }
    }

    public static bool IsLoyal(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).loyalty >= 200;
    }

    public static int GetLoyalty(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).loyalty;
    } 

    public static void SetEmpireID(this Kingdom kingdom, long value)
    {
        GetOrCreate(kingdom).empireID = value;
    }
    public static long GetEmpireID(this Kingdom kingdom)
    {
        if (kingdom == null) return -1L;
        return GetOrCreate(kingdom).empireID;
    }    
    public static Empire GetEmpire(this Kingdom kingdom)
    {
        if (ModClass.EMPIRE_MANAGER == null) return null;
        if (kingdom == null) return null;
        return ModClass.EMPIRE_MANAGER.get(kingdom.GetEmpireID());
    }

    public static void SetTimestampEmpire(this Kingdom kingdom, double value)
    {
        GetOrCreate(kingdom).timestamp_empire = value;
    }
    public static double GetTimestampEmpire(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).timestamp_empire;
    }

    public static void empireJoin(this Kingdom kingdom, Empire pEmpire)
    {
        kingdom.SetVassaledKingdomID(pEmpire.empire.id);
        GetOrCreate(kingdom).empireID = pEmpire.data.id;
        GetOrCreate(kingdom).timestamp_empire = World.world.getCurWorldTime();
    }

    public static bool isEmpire(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).vassaled_kingdom_id == kingdom.id;
    }

    public static void empireLeave (this Kingdom kingdom, bool isLeave = true)
    {
        LogService.LogInfo("离开帝国");
        countryLevel country_level = GetOrCreate(kingdom).country_level;
        if ((country_level != countryLevel.countrylevel_1||country_level!=countryLevel.countrylevel_0)&&isLeave)
        {
            string province_level_name = "provincelevel";
            string province_level_string = "";
            string level = "6";
            if (ConfigData.speciesCulturePair.TryGetValue(kingdom.species_id, out string culture))
            {
                province_level_string = String.Join("_", culture, province_level_name, level);
            } else
            {
                province_level_string = String.Join("_", "Western", province_level_name, level);
            }
            kingdom.SetCountryLevel(countryLevel.countrylevel_5);
            kingdom.data.name = kingdom.GetKingdomName() + " " + LM.Get(province_level_string);
        }
        else
        {
            kingdom.becomeKingdom(isNew:true);
        }
        ColorAsset ca = kingdom.getColorLibrary().getNextColor();
        kingdom.updateColor(ca);
        GetOrCreate(kingdom).empireID = -1L;
        GetOrCreate(kingdom).vassaled_kingdom_id = -1L;
    }

    public static string becomeKingdom(this Kingdom kingdom, bool isPlot=false, bool isNew=false)
    {
        countryLevel country_level = GetOrCreate(kingdom).country_level;
        if (isPlot) 
        {
            country_level = countryLevel.countrylevel_2;
            kingdom.SetCountryLevel(country_level);
        }
        string culture = ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out var name)?name:"default";
        string kingdomName = "";
        string country_level_string = $"{culture}_" + country_level.ToString();
        if (!isNew)
        {
            kingdomName = kingdom.king.GetTitle();
            if (kingdom.capital.hasTitle())
            {
                if (kingdom.capital.GetTitle().owner == kingdom.king)
                {
                    kingdomName = kingdom.capital.GetTitle().data.name;
                }
            }
        }
        if (kingdomName == null || kingdomName == "")
        {
            kingdom.data.name = kingdom.GetKingdomName() + " " + LM.Get(country_level_string);
        }
        else
        {
            kingdom.data.name = kingdomName + " " + LM.Get(country_level_string);
            LogService.LogInfo("存在历史国家名称" + kingdomName);
        }
        return kingdomName;
    }

    public static bool needToBecomeKingdom(this Kingdom k)
    {
        countryLevel cl = k.GetCountryLevel();
        if (cl==countryLevel.countrylevel_0||cl==countryLevel.countrylevel_1||cl==countryLevel.countrylevel_2) return false;
        if (k.isInEmpire())
        {
            Empire empire = k.GetEmpire();
            if (empire.getEmpirePeriod() != EmpirePeriod.逐鹿群雄)
            {
                return false;
            }
        }
        return true;
    }

    public static countryLevel GetCountryLevel(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).country_level;
    }

    public static List<long> GetOwnedTitle(this Kingdom k)
    {
        return GetOrCreate(k).OwnedTitle;
    }

    public static bool HasTitle(this Kingdom k) 
    {
        if (k == null) return false;
        if (GetOrCreate(k)==null) return false;
        return GetOrCreate(k).OwnedTitle.Count()>0; 
    }

    public static void SetOwnedTitle(this Kingdom k, List<long> value)
    {
        GetOrCreate(k).OwnedTitle.Intersect(value).Select(t => ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(t) ? ModClass.KINGDOM_TITLE_MANAGER.get(t).data.timestamp_been_controled = World.world.getCurWorldTime():t);
        GetOrCreate(k).OwnedTitle = GetOrCreate(k).OwnedTitle.Union(value).ToList();
    } 

    public static bool hasAnyControledTitle(this Kingdom kingdom)
    {
        return kingdom.getControledTitle().Count()>0;
    }

    public static List<KingdomTitle> getControledTitle(this Kingdom kingdom)
    {
        List<KingdomTitle> controledTitles = new List<KingdomTitle>();
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            var title_cities = title.city_list;
            int commonCount = title_cities.Intersect(kingdom.cities).Count();
            if (commonCount >= Math.Ceiling(title_cities.Count * title.data.title_controled_rate))
            {
                controledTitles.Add(title);
            }
        }
        return controledTitles;
    }

    public static void SetCountryLevel(this Kingdom kingdom, countryLevel value)
    {
        string kingdomOriginalName = kingdom.GetKingdomName();
        string culture = ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out var a) ? a : "default";
        string kingdomBack = LM.Get($"{culture}_" + value.ToString());
        kingdom.data.name = String.Join(" ", kingdomOriginalName, kingdomBack) ;
        GetOrCreate(kingdom).country_level = value;
    }    
    public static long GetVassaledKingdomID(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).vassaled_kingdom_id;
    }

    public static bool isInEmpire(this Kingdom kingdom)
    {
        if (kingdom == null) return false;
        if (GetOrCreate(kingdom) == null) return false;
        return GetOrCreate(kingdom).empireID != -1L;
    }

    public static void SetVassaledKingdomID(this Kingdom kingdom, long value)
    {
        if (value !=-1L)
        {
            //设置国家归属后，将原国家标记为省份， 并依据王国等级决定省份等级
            countryLevel country_level = GetOrCreate(kingdom).country_level;
            string level = country_level.ToString().Split('_').Last();
            string province_level_name = "provincelevel";
            string province_level_string = "";
            if (ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out string culture))
            {
                province_level_string = String.Join("_", culture, province_level_name, level);
            }
            else
            {
                province_level_string = String.Join("_", "Western", province_level_name, level);
            }
            foreach (City city in kingdom.cities)
            {
                city.AddKingdomName(kingdom.name.Split(' ')[0]);
            }
            string province_name = kingdom.capital.GetCityName() + " " + LM.Get(province_level_string);
            LogService.LogInfo(String.Format("国家{0}已转变为省份：{1}", kingdom.name, province_name));
            kingdom.data.name = province_name;
            GetOrCreate(kingdom).vassaled_kingdom_id = value;
        } else
        {
            kingdom.empireLeave(false);
        }

    }
}