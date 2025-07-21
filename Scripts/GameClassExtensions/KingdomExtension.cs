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
using UnityEngine;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class KingdomExtension
{
    public class KingdomExtraData: ExtraDataBase
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public countryLevel country_level = countryLevel.countrylevel_4;
        public long vassaled_kingdom_id = -1L;
        public long empireID = -1L;
        public double timestamp_empire = -1L;
        public int loyalty = 0;
        public string KingdomNamePre = "";
        public double timestamp_beFeifed = -1L;
        public double taxtRate = 0.1;
        public long main_title_id = -1L;
        public List<long> OwnedTitle = new List<long>();
        public long provinceID = -1L;
    }

    public static void SetMainTitle(this Kingdom k, KingdomTitle title)
    {
        title.main_kingdom = k;
        GetOrCreate(k).main_title_id = title.id;
    }

    public static void RemoveMainTitle(this Kingdom k)
    {
        if (ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(GetOrCreate(k).main_title_id))
        {
            ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).main_title_id).main_kingdom = null;
        }
        GetOrCreate(k).main_title_id = -1L;
    }

    public static void SetProvince(this Kingdom k, Province province)
    {
        GetOrCreate(k).provinceID = province.id;
    }

    public static Province GetProvince(this Kingdom k)
    {
        return ModClass.PROVINCE_MANAGER.get(GetOrCreate(k).provinceID);
    }

    public static long GetProvinceID(this Kingdom k)
    {
        return GetOrCreate(k).provinceID;
    }

    public static KingdomTitle GetMainTitle(this Kingdom k)
    {
        if (k == null) return null;
        if (GetOrCreate(k) == null) return null;
        if (GetOrCreate(k).main_title_id == -1L) return null;
        return ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).main_title_id);
    }

    public static bool HasMainTitle(this Kingdom k)
    {
        return GetOrCreate(k).main_title_id != -1L;
    }

    public static bool canBecomeEmpire(this Kingdom k)
    {
        // 基本条件检查
        if (k.isRekt() || k.isEmpire()) return false;

        // 可能需要满足最小城市数量
        if (k.cities.Count < 2) return false;

        // 检查是否是同物种中最强大的
        int allEmpireNumInSameSpecies = World.world.kingdoms.ToList().FindAll(p => p.species_id == k.species_id && p.isEmpire()).Count();
        return IsStrongestOfSameSpecies(k) && allEmpireNumInSameSpecies<1;
    }

    private static bool IsStrongestOfSameSpecies(Kingdom k)
    {
        return !World.world.kingdoms.Any(other =>
            other != k &&
            other.species_id == k.species_id &&
            !other.isRekt() &&
            !other.isEmpire() &&
            IsStronger(other, k));
    }

    private static bool IsStronger(Kingdom a, Kingdom b)
    {
        return a.countTotalWarriors() > b.countTotalWarriors();
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
    public static KingdomExtraData GetOrCreate(this Kingdom a, bool isSave = false)
    {
        var ed = a.GetOrCreate<Kingdom, KingdomExtraData>(isSave);
        return ed;
    }

    public static double GetFiedTimestamp(this Kingdom k)
    {
        return GetOrCreate(k).timestamp_beFeifed;
    }

    public static void SetFiedTimestamp(this Kingdom k, double v)
    {
        GetOrCreate(k).timestamp_beFeifed = v;
    }

    public static string GetKingdomName(this Kingdom kingdom)
    {
        if (kingdom == null) return null;
        if (kingdom.name == null||kingdom.name == "") return null;

        string[] nameParts = kingdom.name.Split('\u200A');
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

        string[] nameParts = kingdom.name.Split('\u200A');
        if (nameParts.Length <= 1)
        {
            kingdom.data.name = kingdom_name;
            kingdom.SetCountryLevel(GetOrCreate(kingdom).country_level);
        }
        else if (nameParts.Length == 2)
        {
            kingdom.data.name = kingdom_name + "\u200A" + nameParts[1];
        } else if (nameParts.Length == 3)
        {
            kingdom.data.name = String.Join("\u200A", nameParts[0], kingdom_name, nameParts[2]);
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

    public static List<Empire> GetEmpiresCanbeJoined(this Kingdom kingdom)
    {
        List<Empire> empires = new List<Empire>();
        if (kingdom == null) return empires;
        if (ModClass.EMPIRE_MANAGER == null) return empires;
        foreach(City city in kingdom.cities)
        {
            foreach(Kingdom k in city.neighbours_kingdoms)
            {
                if (k != kingdom)
                {
                    if (k.isInEmpire())
                    {
                        Empire empire = k.GetEmpire();
                        if ((double)kingdom.cities.Count()<=((double)empire.AllCities().Count())/5)
                        {
                            if (kingdom.isOpinionTowardsKingdomGood(k.GetEmpire().empire))
                                empires.Add(k.GetEmpire());
                        }
                    }
                }
            }
        }
        return empires;
    }
    public static void empireJoin(this Kingdom kingdom, Empire pEmpire)
    {
        kingdom.SetVassaledKingdomID(pEmpire.empire.id);
        GetOrCreate(kingdom).empireID = pEmpire.data.id;
        GetOrCreate(kingdom).timestamp_empire = World.world.getCurWorldTime();
    }

    public static bool isEmpire(this Kingdom kingdom)
    {
        try {
            if (kingdom == null) return false;
            if (kingdom.data == null) return false;
            var extraData = GetOrCreate(kingdom);
            if (extraData == null) return false;

            return extraData.vassaled_kingdom_id == kingdom.getID();
        } catch (Exception e) {
            LogService.LogInfo(e.Message);
            return false; 
        }
    }

    public static void empireLeave (this Kingdom kingdom, bool isLeave = true)
    {
        if (kingdom==null) return;
        if (GetOrCreate(kingdom) == null) return;
        countryLevel country_level = GetOrCreate(kingdom).country_level;
        if ((country_level != countryLevel.countrylevel_1||country_level!=countryLevel.countrylevel_0)&&isLeave)
        {
            string province_level_name = "provincelevel";
            string province_level_string = "";
            string level = "6";
            if (ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out string culture))
            {
                province_level_string = String.Join("_", culture, province_level_name, level);
            } else
            {
                province_level_string = String.Join("_", "Western", province_level_name, level);
            }
            kingdom.SetCountryLevel(countryLevel.countrylevel_5);
            kingdom.data.name = kingdom.GetKingdomName() + "\u200A" + LM.Get(province_level_string);
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
                    kingdom.SetMainTitle(kingdom.capital.GetTitle());
                }
            }
        }
        if (kingdomName == null || kingdomName == "")
        {
            kingdom.data.name = kingdom.GetKingdomName() + "\u200A" + LM.Get(country_level_string);
        }
        else
        {
            kingdom.data.name = kingdomName + "\u200A" + LM.Get(country_level_string);
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

    public static Kingdom FindClosestKingdom (this Kingdom kingdom)
    {
        return World.world.kingdoms
            .Where(k => k != kingdom && !k.isRekt())
            .OrderBy(k => Vector3.Distance(kingdom.location, k.location))
            .FirstOrDefault();
    }

    public static bool isNeighbourWith(this Kingdom kingdom, Kingdom target)
    {
        if(kingdom.isEmpire())
        {
            Empire empire = kingdom.GetEmpire();
            return empire.isNeighbourWith(target);
        }
        foreach(City city in kingdom.cities)
        {
            if (city.neighbours_kingdoms.Contains(target))
            {
                return true;
            }
        }
        return false;
    }

    public static bool isBorder(this Kingdom kingdom)
    {
        if(kingdom.isEmpire()) return false;
        foreach(City city in kingdom.cities)
        {
            if (city.neighbours_kingdoms.Count > 0)
            {
                foreach(Kingdom kingdom2 in city.neighbours_kingdoms)
                {
                    if (!kingdom2.isInSameEmpire(kingdom))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
    public static void RemoveProvince(this Kingdom k)
    {
        GetOrCreate(k).provinceID = -1L;
    }

    public static bool isProvince(this Kingdom k)
    {
        return ModClass.PROVINCE_MANAGER.get(GetOrCreate(k).provinceID) != null;
    }

    public static void checkLostProvince(this Kingdom k)
    {
        Province province = k.GetProvince();
        if (province == null) return;
        bool flag = false;
        foreach(City city in province.city_list)
        {
            if (k.cities.Contains(city))
            {
                flag = true;
                break;
            }
        }
        if (!flag)
        {
            province.data.is_set_to_country = false;
            k.RemoveProvince();
        }
        
    }

    public static void ChangeToProvince(this Kingdom kingdom, Empire empire)
    {
        Province province = null;
        if (kingdom.isProvince())
        {
            province = kingdom.GetProvince();
            if (province != null) 
            {
                province.data.is_set_to_country = false;
            }
        }
        ListPool<City> listPool = new ListPool<City>();
        foreach(City city in kingdom.cities)
        {
            if (city.isRekt()) continue;
            if (!city.hasProvince())
            {
                if (province!=null)
                {
                    province.addCity(city);
                } else
                {
                    listPool.Add(city);
                }
            }
        }
        if (listPool.Count > 0)
        {
            Province province2 = ModClass.PROVINCE_MANAGER.newProvince(listPool[0]);
            province2.data.is_set_to_country = false;
            province2.data.name = kingdom.GetKingdomName();
            province2.SetProvinceLevel(provinceLevel.provincelevel_3);
            foreach (City city in listPool)
            {
                if (city != listPool[0])
                {
                    province2.addCity(city);
                }
                city.joinAnotherKingdom(empire.empire);
            }
        }

    }

    public static void SetCountryLevel(this Kingdom kingdom, countryLevel value)
    {
        string kingdomOriginalName = kingdom.GetKingdomName();
        string culture = ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out var a) ? a : "Western";
        string kingdomBack = LM.Get($"{culture}_" + value.ToString());

        kingdom.data.name = String.Join("\u200A", kingdomOriginalName, kingdomBack);

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
            string preName;
            string postName;
            if (ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out string culture))
            {
                preName = String.Join("_", culture, "capital");
                postName = String.Join("_", culture, "provincelevel", "0");
                province_level_string = String.Join("_", culture, province_level_name, level);
            }
            else
            {
                preName = String.Join("_", "Western", "capital");
                postName = String.Join("_", "Western", "provincelevel", "0");
                province_level_string = String.Join("_", "Western", province_level_name, level);
            }
            foreach (City city in kingdom.cities)
            {
                city.AddKingdomName(kingdom.GetKingdomName());
            }
            string province_name = kingdom.data.name;
            if (country_level != countryLevel.countrylevel_0)
            {
                province_name = kingdom.capital.GetCityName() + "\u200A" + LM.Get(province_level_string);
            }
            kingdom.data.name = province_name;
            GetOrCreate(kingdom).vassaled_kingdom_id = value;
        } else
        {
            kingdom.empireLeave(false);
        }

    }
}