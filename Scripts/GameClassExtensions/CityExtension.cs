using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Prefabs;
using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;
using Newtonsoft.Json;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class CityExtension
{
    public class CityExtraData: ExtraDataBase
    {
        public string kingdom_names = "";
        public long title_id = -1L;
        public long empire_core_id = -1L;
        public List<long> exam_pass_person = new List<long>();
        public int MAX_POPULATION = 100;
        public bool MAX_POPULATION_LIMIT = false;
        public double last_tax_timestamp = -1L;
        public int Money = 0;
        [JsonIgnore]
        public TextInput limitationNumber { get; set; }

        public double corruption_rate = 0.0f;
        public long personalIdentityId = -1L;
        public bool is_choosing_heir = false;
        [JsonIgnore]
        public SimpleButton limitToggle { get; set; }
        public CityType cityType { get; set; }
        public long office_id { get; set; } = -1L;
        public int cached_warriors = 0;
        public int cached_population = 0;
        public double last_cached_timestamp = -1L;
        public double last_army_check_ts = -1L;
        public double last_law_scan_ts = -1L;
    }

    public static void AddCorruptionRate(this City city, double addition)
    {
        if (city.GetCorruptionRate() < 1.0f||city.GetCorruptionRate()>0.0f)
        {
            city.GetOrCreate().corruption_rate += addition;
            if (city.GetCorruptionRate() > 1.0f)
            {
                city.SetCorruptionRate(1.0f);
            }
            if (city.GetCorruptionRate() < 0.0f)
            {
                city.SetCorruptionRate(0.0f);
            }
        }
    }

    public static void InitialRegime(this City city)
    {
        if (!city.hasKingdom()) return;
        if (city.kingdom.GetRegime()==null) return;
        CityType cityType = EmpireCraftKingdomBehCheckKingdomType.CalcCityType(city.kingdom);
        city.SetCityType(cityType);
        BureauSetting citySetting = null;
        var bc = city.kingdom.GetRegime().bureau_config;
        if (bc != null && bc.cities != null)
        {
            bc.cities.TryGetValue(cityType, out citySetting);
        }
        if (citySetting == null)
        {
            citySetting = new BureauSetting
            {
                type = 0,
                pre = "",
                description = "",
                powers = new List<OfficerPowerType>(),
                merit = 0,
                honorary = 0,
                select_from_local = false,
                leader_select_method = LeaderSelectMethod.Default,
                require_traits = new List<string>(),
                condition = new List<string>(),
                city_type = cityType
            };
        }
        OfficeObject officeObject2 = new OfficeObject();
        officeObject2.InitialOffice(citySetting);
        officeObject2.regimeType = city.kingdom.GetRegime().type;
        officeObject2.meta_object = city;
        officeObject2.is_local = true;
        if (city.hasLeader())
        {
            officeObject2.SetActor(city.leader);
        }
        city.SetOffice(officeObject2);
    }
    public static double GetCorruptionRate(this City city)
    {
        return city.GetOrCreate().corruption_rate;
    }

    public static void SetCorruptionRate(this City city, Double value)
    {
        city.GetOrCreate().corruption_rate = value;
    }
    public static void SetCityType(this City c, CityType type)
    {
        c.GetOrCreate().cityType = type;
    }
    public static double GetLastTaxTime(this City k)
    {
        return k.GetOrCreate().last_tax_timestamp;
    }
    public static void RecordTaxTime(this City k)
    {
        k.GetOrCreate().last_tax_timestamp = World.world.getCurWorldTime();
    }

    public static bool IsLawScanDue(this City city, float years = 1f)
    {
        if (city == null) return false;
        double value = city.GetOrCreate().last_law_scan_ts;
        if (value < 0) return true;
        return Date.getYearsSince(value) >= years;
    }

    public static void RecordLawScan(this City city)
    {
        if (city == null) return;
        city.GetOrCreate().last_law_scan_ts = World.world.getCurWorldTime();
    }

    public static bool IsNeedToSubmitTax(this City k)
    {
        if (!k.hasKingdom()) return false;
        return Date.getYearsSince(k.GetLastTaxTime()) >= 1;
    }
    
    public static int GetMoney(this City c)
    {
        return c.GetOrCreate().Money;
    }

    public static void AddMoney(this City c, int money)
    {
        c.GetOrCreate().Money += money;
    }

    public static void SubMoney(this City c, int money)
    {
        c.GetOrCreate().Money -= money; 
    }

    public static CityType GetCityType(this City c)
    {
        return c.GetOrCreate().cityType;
    }
    public static void SetOffice(this City c, OfficeObject off)
    {
        OfficeManager.Remove(c.GetOfficeID());
        c.GetOrCreate().office_id = off.OfficeID;
    }

    public static OfficeObject GetOffice(this City c)
    {
        return OfficeManager.Offices.TryGetValue(c.GetOrCreate().office_id, out OfficeObject office) ? office : null;
    }

    public static long GetOfficeID(this City c)
    {
        return c.GetOrCreate().office_id;
    }
    public static CityExtraData GetOrCreate(this City a, bool isSave=false)
    {
        var ed = a.GetOrCreate< City, CityExtraData>(isSave);
        return ed;
    }

    public static int CountLivingPopulation(this City city)
    {
        if (city == null || city.units == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < city.units.Count; i++)
        {
            Actor actor = city.units[i];
            if (actor == null || actor.isRekt()) continue;
            if (!actor.isAlive()) continue;
            count++;
        }

        return count;
    }

    public static int CountLivingWarriors(this City city)
    {
        if (city == null || city.units == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < city.units.Count; i++)
        {
            Actor actor = city.units[i];
            if (actor == null || actor.isRekt()) continue;
            if (!actor.isAlive()) continue;
            if (!actor.isWarrior()) continue;
            count++;
        }

        return count;
    }

    public static bool IsBorderCity(this City city)
    {
        return city != null && city.neighbours_kingdoms != null && city.neighbours_kingdoms.Count > 0;
    }

    public static City FindExileCity(this Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || kingdom.cities == null || kingdom.cities.Count <= 0)
        {
            return null;
        }

        City borderEmptyCity = null;
        City leastPopulatedCity = null;
        int leastPopulation = int.MaxValue;

        for (int i = 0; i < kingdom.cities.Count; i++)
        {
            City city = kingdom.cities[i];
            if (city == null || city.isRekt()) continue;

            int population = city.CountLivingPopulation();
            if (population <= 0 && city.IsBorderCity())
            {
                borderEmptyCity = city;
                break;
            }

            if (population < leastPopulation)
            {
                leastPopulation = population;
                leastPopulatedCity = city;
            }
        }

        if (borderEmptyCity != null)
        {
            return borderEmptyCity;
        }

        if (leastPopulatedCity != null)
        {
            return leastPopulatedCity;
        }

        return kingdom.capital;
    }

    public static void StartChoosingHeir(this City c)
    {
        c.GetOrCreate().is_choosing_heir = true;
    }

    public static bool IsChoosingHeir(this City c)
    {
        return c.GetOrCreate().is_choosing_heir;
    }

    public static void EndChoosingHeir(this City c)
    {
        c.GetOrCreate().is_choosing_heir = false;
    }
    public static PersonalClanIdentity GetPersonalIdentity(this City a)
    {
        return SpecificClanManager.getPerson(a.GetOrCreate().personalIdentityId);
    }

    public static void SetPersonalIdentity(this City a, PersonalClanIdentity personalId)
    {
        a.GetOrCreate().personalIdentityId = personalId?.id??-1L;
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
        if (c == null) return true;
        var ed = c.GetOrCreate();
        if (ed == null) return true;
        if (c.getPopulationPeople()>ed.MAX_POPULATION&&ed.MAX_POPULATION_LIMIT)
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

    public static string GetCityName(this City city)
    {
        if (city == null) return null;
        if (string.IsNullOrEmpty(city.name)) return null;
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
        return string.IsNullOrEmpty(GetOrCreate(city).kingdom_names);
    }

}
