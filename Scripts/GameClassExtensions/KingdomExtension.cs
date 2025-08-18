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
using System.Threading;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using UnityEngine;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class KingdomExtension
{
    public static readonly SemaphoreSlim _sem = new SemaphoreSlim(Environment.ProcessorCount);
    public class KingdomExtraData: ExtraDataBase
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public countryLevel CountryLevel = countryLevel.countrylevel_4;
        public long VassaledKingdomID = -1L;
        public long EmpireID = -1L;
        public double TimestampEmpire = -1L;
        public int loyalty = 0;
        public string KingdomNamePre = "";
        public double TimestampBeFeifed = -1L;
        public double TaxRate = 0.1;
        public long MainTitleID = -1L;
        public long HeirID = -1L;
        [JsonIgnore]
        public Actor Heir => World.world.units.get(HeirID);
        [JsonIgnore]
        public Task<(Actor, string)> CalcTask;
        public List<long> OwnedTitle = new List<long>();
        public long ProvinceID = -1L;
        public int IndependentValue = 100;
        public bool is_need_to_choose_heir = false;
    }
    public static int GetIndependentValue(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        if (ed != null)
        {
            return ed.IndependentValue;
        } else
        {
            return 100;
        }
    }

    public static bool CalcHeirFinished(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed.CalcTask == null;
    }

    public static Actor GetKing(this Kingdom k)
    {
        return k.king;
    }

    public static void SetCalcHeirTask(this Kingdom k, Task<(Actor, string)> calcTask)
    {
        var ed = k.GetOrCreate();
        ed.CalcTask = calcTask;
    }

    public static Task<(Actor pActor, string relation)> GetCalcHeirTask(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed.CalcTask;
    }
    public static void RemoveCalcHeirStatus(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.CalcTask = null;
    }
    public static void SetIndependentValue(this Kingdom k, int value)
    {
        var ed = k.GetOrCreate();
        ed.IndependentValue = value;
    }

    public static void AddIndependentValue(this Kingdom k, int addition)
    {
        var ed = k.GetOrCreate();
        ed.IndependentValue += addition;
        if (ed.IndependentValue < 0)
        {
            ed.IndependentValue = 0;
        } else if (ed.IndependentValue > 100)
        {
            ed.IndependentValue = 100;
        }
    }

    public static Actor GetHeir(this Kingdom k)
    {
        LogService.LogInfo("获得继承人");
        var ed = k.GetOrCreate();
        return ed.Heir;
    }
    public static void RemoveHeir(this Kingdom k)
    {
        LogService.LogInfo("移除继承人");
        var ed = k.GetOrCreate();
        ed.HeirID = -1L;
    }
    public static bool HasHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        if (ed.HeirID == -1L) return false;
        return !ed.Heir.isRekt();
    }

    public static void SetHeir(this Kingdom k, Actor pActor)
    {
        var ed = k.GetOrCreate();
        ed.HeirID = pActor.getID();
    }
    public static (Actor actor, string relation) CalcHeirCore(this Kingdom k)
    {
        var res = k.CheckHeir();
        if (res.actor == null)
        {
            res = k.CheckHeir(EmpireHeirLawType.siblings);
        }

        if (res.actor == null)
        {
            res = k.CheckHeir(EmpireHeirLawType.grand_child_generation);
        }

        if (res.actor == null)
        {
            res = k.CheckHeir(EmpireHeirLawType.random);
        }

        if (res.actor == null)
        {
            res = k.CheckHeir(EmpireHeirLawType.officer);
        }
        if (res.actor != null)
            return res;
        return (null, null);
    }

    private static (Actor actor, string relation) CheckHeir(this Kingdom k, EmpireHeirLawType secondSelection=EmpireHeirLawType.eldest_child, PersonalClanIdentity pActor = null)
    {
        LogService.LogInfo("检查继承人");
        if (k == null) return (null, "");
        Actor actor = null;
        var flag = k.isEmpire();
        var logPreText = flag ? "Empire: " : "Kingdom: ";
        PersonalClanIdentity pci = pActor??k.king?.GetPersonalIdentity();
        List<(ClanRelation, PersonalClanIdentity)> children = SpecificClanManager.GetChildren(pci).FindAll(a=>a.Item2.CanHeir(pci));
        children.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>.Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
        var relationText = secondSelection.ToString();
        switch (secondSelection)
        {
            case EmpireHeirLawType.eldest_child:
                if (children.Any())
                {
                    actor = children.Last().Item2._actor; // Assuming eldest is the last after sorting by age
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.9f, 0.3f, 0.2f));
                    LogService.LogInfo(logPreText + relationText);
                }
                break;
            case EmpireHeirLawType.smallest_child:
                if (children.Any())
                {
                    actor = children.First().Item2._actor; // Assuming youngest is the first after sorting by age
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.5f, 0.1f, 0.7f));
                    LogService.LogInfo(logPreText + relationText);
                }
                break;
            case EmpireHeirLawType.siblings:
                // Logic for selecting a brother heir can be added here
                List<(ClanRelation, PersonalClanIdentity)> brothers = SpecificClanManager.GetSiblingsWithRelation(pci).FindAll(a=>a.Item2.CanHeir(pci));
                brothers.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (brothers.Any())
                {
                    actor = brothers.Last().Item2._actor;
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.2f, 0.3f, 0.9f));
                    LogService.LogInfo(logPreText + relationText);
                }
                break;
            case EmpireHeirLawType.grand_child_generation:
                List<(ClanRelation, PersonalClanIdentity)> grandChildren = SpecificClanManager.GetGrandChildren(pci);
                grandChildren = grandChildren.FindAll(c=>c.Item2.CanHeir(pci));
                grandChildren.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (grandChildren.Any())
                {
                    actor = grandChildren.Last().Item2._actor;
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.9f, 0.1f, 0.9f));
                    LogService.LogInfo(logPreText + relationText);
                }
                break;
            case EmpireHeirLawType.random:
                List<(ClanRelation, PersonalClanIdentity)> randomClanMember = SpecificClanManager.FindAllRelations(pci);
                randomClanMember = randomClanMember.FindAll(c=>c.Item2.CanHeir(pci));
                randomClanMember.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (randomClanMember.Any())
                {
                    actor = randomClanMember.Last().Item2._actor;
                    relationText = LM.Get(relationText).ColorString(pColor:new Color(0.9f, 0.6f, 0.9f));
                    LogService.LogInfo(logPreText + relationText);
                }
                break;
            case EmpireHeirLawType.officer:
                if (flag)
                {
                    Empire empire = k.GetEmpire();
                    actor = empire.data.centerOffice.Minister.GetActor() 
                            ?? empire.data.centerOffice.General.GetActor()
                            ?? empire.data.centerOffice.CoreOffices?.ToList().Find(a=>a.Value?.GetActor()!=null).Value?.GetActor()
                            ?? empire.data.centerOffice.Divisions?.ToList().Find(a=>a.Value?.GetActor()!=null).Value?.GetActor()
                            ?? empire.ProvinceList?.ToList().Find(p=>p.HasOfficer())?.Officer
                            ?? k.capital?.leader;
                    OfficeIdentity identity = actor?.GetIdentity(empire);
                    var officeName = string.Join("_", actor?.GetPersonalIdentity()?.culture, identity?.officialLevel);
                    relationText = LM.Get(officeName).ColorString(pColor:new Color(1.0f, 1.0f, 1.0f));
                    LogService.LogInfo(logPreText + relationText);
                }
                else
                {
                    if (k.cities.Any())
                    {
                        actor = k.cities.ToList().Find(c => c?.hasLeader()??false)?.leader;
                        relationText = LM.Get(relationText).ColorString(pColor:new Color(1.0f, 1.0f, 1.0f));
                        LogService.LogInfo(logPreText + relationText);
                    }
                }
                break;
        }
        return (actor, relationText);
    }
    public static bool IsIndependent(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        return ed.IndependentValue >= 100;
    }

    public static void NeedToChooseHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.is_need_to_choose_heir = !k.HasHeir();
    }

    public static bool IsNeedToChooseHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed.is_need_to_choose_heir;
    }

    public static void ChooseHeirFinished(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.is_need_to_choose_heir = false;
        LogService.LogInfo("检查王国继承人完毕");
    }
    public static bool CanBeTaken(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        return ed.IndependentValue <= 0;
    }

    public static void SetMainTitle(this Kingdom k, KingdomTitle title)
    {
        title.main_kingdom = k;
        GetOrCreate(k).MainTitleID = title.id;
    }

    public static void RemoveMainTitle(this Kingdom k)
    {
        if (ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(GetOrCreate(k).MainTitleID))
        {
            ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).MainTitleID)!.main_kingdom = null;
        }
        GetOrCreate(k).MainTitleID = -1L;
    }

    public static void SetProvince(this Kingdom k, Province province)
    {
        GetOrCreate(k).ProvinceID = province.id;
    }

    public static Province GetProvince(this Kingdom k)
    {
        return ModClass.PROVINCE_MANAGER.get(GetOrCreate(k).ProvinceID);
    }

    public static long GetProvinceID(this Kingdom k)
    {
        return GetOrCreate(k).ProvinceID;
    }

    public static KingdomTitle GetMainTitle(this Kingdom k)
    {
        if (k == null) return null;
        if (GetOrCreate(k) == null) return null;
        if (GetOrCreate(k).MainTitleID == -1L) return null;
        return ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).MainTitleID);
    }

    public static bool HasMainTitle(this Kingdom k)
    {
        return GetOrCreate(k).MainTitleID != -1L;
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
        return GetOrCreate(k).TaxRate;
    }

    public static void SetTaxtRate(this Kingdom k, double value)
    {
        GetOrCreate(k).TaxRate = value;
    }
    public static void IncreaseTaxtRate (this Kingdom k)
    {
        var t = GetOrCreate(k).TaxRate;
        if (t < 1.0)
        {
            t += 0.1;
            k.SetLoyalty(k.GetLoyalty() - 50);
        }
    }
    public static void DecreaseTaxtRate(this Kingdom k)
    {
        var t = GetOrCreate(k).TaxRate;
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
        return GetOrCreate(k).TimestampBeFeifed;
    }

    public static void SetFiedTimestamp(this Kingdom k, double v)
    {
        GetOrCreate(k).TimestampBeFeifed = v;
    }

    public static string GetKingdomName(this Kingdom kingdom)
    {
        if (kingdom == null) return null;
        if (string.IsNullOrEmpty(kingdom.name)) return null;

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
            kingdom.SetCountryLevel(GetOrCreate(kingdom).CountryLevel);
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
        kingdom.SetCountryLevel(GetOrCreate(kingdom).CountryLevel);
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
        GetOrCreate(kingdom).EmpireID = value;
    }
    public static long GetEmpireID(this Kingdom kingdom)
    {
        if (kingdom == null) return -1L;
        return GetOrCreate(kingdom).EmpireID;
    }    
    public static Empire GetEmpire(this Kingdom kingdom)
    {
        if (ModClass.EMPIRE_MANAGER == null) return null;
        if (kingdom == null) return null;
        return ModClass.EMPIRE_MANAGER.get(kingdom.GetEmpireID());
    }

    public static void SetTimestampEmpire(this Kingdom kingdom, double value)
    {
        GetOrCreate(kingdom).TimestampEmpire = value;
    }
    public static double GetTimestampEmpire(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).TimestampEmpire;
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
                            if (kingdom.isOpinionTowardsKingdomGood(k.GetEmpire().CoreKingdom))
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
        kingdom.SetVassaledKingdomID(pEmpire.CoreKingdom.id);
        GetOrCreate(kingdom).EmpireID = pEmpire.data.id;
        GetOrCreate(kingdom).TimestampEmpire = World.world.getCurWorldTime();
    }

    public static bool isEmpire(this Kingdom kingdom)
    {
        if (kingdom == null) return false;
        if (kingdom.data == null) return false;
        var extraData = GetOrCreate(kingdom);
        if (extraData == null) return false;

        return extraData.VassaledKingdomID == kingdom.getID();
    }

    public static void empireLeave (this Kingdom kingdom, bool isLeave = true)
    {
        if (kingdom==null) return;
        if (GetOrCreate(kingdom) == null) return;
        countryLevel country_level = GetOrCreate(kingdom).CountryLevel;
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
        GetOrCreate(kingdom).EmpireID = -1L;
        GetOrCreate(kingdom).VassaledKingdomID = -1L;
    }

    public static string becomeKingdom(this Kingdom kingdom, bool isPlot=false, bool isNew=false)
    {
        countryLevel country_level = GetOrCreate(kingdom).CountryLevel;
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
            if (empire.GetEmpirePeriod() != EmpirePeriod.逐鹿群雄)
            {
                return false;
            }
        }
        return true;
    }

    public static countryLevel GetCountryLevel(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).CountryLevel;
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

    public static bool hasAnycontrolledTitle(this Kingdom kingdom)
    {
        return kingdom.getcontrolledTitle().Any();
    }

    public static List<KingdomTitle> getcontrolledTitle(this Kingdom kingdom)
    {
        List<KingdomTitle> controlledTitles = new List<KingdomTitle>();
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            var title_cities = title.city_list;
            int commonCount = title_cities.Intersect(kingdom.cities).Count();
            if (commonCount >= Math.Ceiling(title_cities.Count * title.data.title_controlled_rate))
            {
                controlledTitles.Add(title);
            }
        }
        return controlledTitles;
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
            return empire.IsNeighbourWith(target);
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
        GetOrCreate(k).ProvinceID = -1L;
    }

    public static bool isProvince(this Kingdom k)
    {
        return ModClass.PROVINCE_MANAGER.get(GetOrCreate(k).ProvinceID) != null;
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
        if (listPool.Any())
        {
            Province province2 = ModClass.PROVINCE_MANAGER.newProvince(listPool.ElementAt(0));
            province2.data.is_set_to_country = false;
            province2.data.name = kingdom.GetKingdomName();
            province2.SetProvinceLevel(provinceLevel.provincelevel_3);
            foreach (City city in listPool)
            {
                if (city != listPool.ElementAt(0))
                {
                    province2.addCity(city);
                }
                city.joinAnotherKingdom(empire.CoreKingdom);
            }
        }

    }

    public static void SetCountryLevel(this Kingdom kingdom, countryLevel value)
    {
        string kingdomOriginalName = kingdom.GetKingdomName();
        string culture = ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out var a) ? a : "Western";
        string kingdomBack = LM.Get($"{culture}_" + value.ToString());

        kingdom.data.name = String.Join("\u200A", kingdomOriginalName, kingdomBack);

        GetOrCreate(kingdom).CountryLevel = value;
    }    
    public static long GetVassaledKingdomID(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).VassaledKingdomID;
    }

    public static bool isInEmpire(this Kingdom kingdom)
    {
        if (kingdom == null) return false;
        if (GetOrCreate(kingdom) == null) return false;
        return GetOrCreate(kingdom).EmpireID != -1L;
    }

    public static void SetVassaledKingdomID(this Kingdom kingdom, long value)
    {
        if (value !=-1L)
        {
            //设置国家归属后，将原国家标记为省份， 并依据王国等级决定省份等级
            countryLevel country_level = GetOrCreate(kingdom).CountryLevel;
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
            GetOrCreate(kingdom).VassaledKingdomID = value;
        } else
        {
            kingdom.empireLeave(false);
        }

    }
}