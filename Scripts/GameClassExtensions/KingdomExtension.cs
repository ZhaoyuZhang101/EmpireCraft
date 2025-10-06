﻿using EmpireCraft.Scripts.Enums;
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
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using UnityEngine;

namespace EmpireCraft.Scripts.GameClassExtensions;
public static class KingdomExtension
{
    public static readonly SemaphoreSlim _sem = new SemaphoreSlim(Environment.ProcessorCount);
    public class KingdomExtraData: ExtraDataBase
    {
        public long EmpireID = -1L;
        public double TimestampEmpire = -1L;
        public int loyalty = 0;
        public double TimestampBeFeifed = -1L;
        public double TaxRate = 0.1;
        public long HeirID = -1L;
        public int Level = 2;
        public Regime regime;
        public RegimeType regimeType;
        public KingdomType kingdomType;
        public SpecificClan kingdomSpecificClan;
        [JsonIgnore]
        public Task<(Actor, string)> CalcTask;
        //拥有法理
        public List<long> OwnedTitle = new List<long>();
        //想要索取的法理
        public List<long> WantedTitle = new List<long>();
        public int IndependentValue = 100;
        public bool is_need_to_choose_heir = false;
        public double last_exam_timestamp = -1L;
        public int Authority = 0;
        public int Legitimate = 0;
        public double last_office_exam_timestamp = -1L;
        public OfficeObject office;
    }

    public static void SetKingdomType(this Kingdom k, KingdomType type)
    {
        k.GetOrCreate().kingdomType = type;
    }

    public static KingdomType GetKingdomType(this Kingdom k)
    {
        return k.GetOrCreate().kingdomType;
    }
    public static void SetOffice(this Kingdom k, OfficeObject office)
    {
        k.GetOrCreate().office = office;
    }

    public static OfficeObject GetOffice(this Kingdom k)
    {
        return k.GetOrCreate().office;
    }

    public static SpecificClan GetSpecificClan(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().kingdomSpecificClan;
    }

    public static void UpdateExamTime(this Kingdom k)
    { 
        k.GetOrCreate().last_exam_timestamp = World.world.getCurWorldTime();
    }

    public static void UpdateOfficeExamTime(this Kingdom k)
    { 
        k.GetOrCreate().last_office_exam_timestamp = World.world.getCurWorldTime();
    }
    public static bool IsNeedToExam(this Kingdom k)
    {
        var time = k.GetOrCreate().last_exam_timestamp;
        if (time <= 0) 
        {
            return true;
        }

        if (Date.getYearsSince(time)>=4)
        {
            return true;
        }
        return false;
    }
    public static bool IsNeedToOfficeExam(this Kingdom k)
    {
        var exam_time = k.GetOrCreate().last_exam_timestamp;
        var office_exam_time = k.GetOrCreate().last_office_exam_timestamp;
        if (exam_time <= 0) return true;
        if (Date.getYearsSince(office_exam_time)>=1)
        {
            return true;
        }
        return false;
    }
    public static void SetSpecificClan(this Kingdom kingdom, SpecificClan sc)
    {
        kingdom.GetOrCreate().kingdomSpecificClan = sc;
    }
    public static Regime GetRegime(this Kingdom k)
    {
        return k.GetOrCreate().regime;
    }
    public static void SetRegimeType(this Kingdom k, RegimeType type)
    {
        k.GetOrCreate().regimeType = type;
    }
    public static void SetRegime(this Kingdom k, Regime regime)
    {
        k.GetOrCreate().regime = regime;
    }

    public static void LoadRegime(this Kingdom k)
    {
        Regime regime = RegimeManager.regimes[k.GetOrCreate().regimeType].Clone();
        k.SetRegime(regime);
    }
    public static void AddAuthority(this Kingdom k, int value)
    {
        k.GetOrCreate().Authority += value;
        if (k.GetOrCreate().Authority < 0)
        {
            k.GetOrCreate().Authority = 0;
        }
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

    public static List<Actor> allGongshi(this Kingdom k)
    {
        return k.units.FindAll(a => a.hasTrait("gongshi"));
    }
    public static bool CalcHeirFinished(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed.CalcTask == null;
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
        var ed = k.GetOrCreate();
        return World.world.units.get(ed.HeirID);
    }
    public static void RemoveHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.HeirID = -1L;
    }
    public static bool HasHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        if (ed.HeirID == -1L) return false;
        return !World.world.units.get(ed.HeirID).isRekt();
    }

    public static void SetHeir(this Kingdom k, Actor pActor)
    {
        var ed = k.GetOrCreate();
        ed.HeirID = pActor.getID();
    }    

    public static bool IsIndependent(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        return ed.IndependentValue >= 100;
    }

    public static void StartToChooseHeir(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        ed.is_need_to_choose_heir = true;
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
    }
    public static bool CanBeTaken(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        return ed.IndependentValue <= 0;
    }

    public static bool IsOwnedTitle(this Kingdom k, KingdomTitle title)
    {
        var ed = k.GetOrCreate();
        return ed.OwnedTitle.Contains(title.id);
    }

    public static void SetMainTitle(this Kingdom k, KingdomTitle title)
    {
        title.main_kingdom = k;
        if (k.IsOwnedTitle(title))
        {
            k.GetOrCreate().OwnedTitle.Remove(title.id);
            
        }
        k.GetOrCreate().OwnedTitle.Insert(0, title.id);
    }

    public static void RemoveMainTitle(this Kingdom k)
    {
        if (GetOrCreate(k).OwnedTitle.Any())
        {
            k.GetOrCreate().OwnedTitle.RemoveAt(0);
        }
    }
    
    public static KingdomTitle GetMainTitle(this Kingdom k)
    {
        if (k == null) return null;
        if (GetOrCreate(k) == null) return null;
        if (!k.GetOrCreate().OwnedTitle.Any()) return null;
        return ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).OwnedTitle.First());
    }

    public static bool HasMainTitle(this Kingdom k)
    {
        return GetOrCreate(k).OwnedTitle.Any();
    }

    public static bool canBecomeEmpire(this Kingdom k)
    {
        if (!k.hasKing()) return false;
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

    public static bool isInSameEmpire(this Kingdom kingdom, Kingdom pKingdomTaget)
    {
        if (kingdom == null) return false;
        if (!kingdom.IsInEmpire()||!pKingdomTaget.IsInEmpire()) return false;
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
        if (!ModClass.EMPIRE_MANAGER.Any()) return empires;
        foreach(City city in kingdom.cities)
        {
            foreach(Kingdom k in city.neighbours_kingdoms)
            {
                if (k != kingdom)
                {
                    if (k.IsInEmpire())
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
        GetOrCreate(kingdom).EmpireID = pEmpire.data.id;
        GetOrCreate(kingdom).TimestampEmpire = World.world.getCurWorldTime();
    }

    public static bool isEmpire(this Kingdom kingdom)
    {
        if (kingdom == null) return false;
        if (kingdom.data == null) return false;
        var ed = GetOrCreate(kingdom);
        if (ed == null) return false;

        return ModClass.EMPIRE_MANAGER.get(ed.EmpireID)?.CoreKingdom==kingdom;
    }

    public static void empireLeave (this Kingdom kingdom, bool isLeave = true)
    {
        if (kingdom==null) return;
        if (GetOrCreate(kingdom) == null) return;
        kingdom.generateColor();
        GetOrCreate(kingdom).EmpireID = -1L;
    }
    public static int GetLevel(this Kingdom kingdom)
    {
        return GetOrCreate(kingdom).Level;
    }

    public static List<long> GetOwnedTitle(this Kingdom k)
    {
        return GetOrCreate(k).OwnedTitle;
    }

    public static bool HasTitle(this Kingdom k) 
    {
        if (k == null) return false;
        if (GetOrCreate(k)==null) return false;
        return GetOrCreate(k).OwnedTitle.Any(); 
    }

    public static void SetOwnedTitle(this Kingdom k, List<long> value)
    {
        GetOrCreate(k).OwnedTitle = GetOrCreate(k).OwnedTitle.Union(value).ToList();
    } 

    public static bool hasAnycontrolledTitle(this Kingdom kingdom)
    {
        return kingdom.GetcontrolledTitle().Any();
    }

    public static List<KingdomTitle> GetcontrolledTitle(this Kingdom kingdom)
    {
        List<KingdomTitle> controlledTitles = new List<KingdomTitle>();
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            var titleCities = title.city_list;
            int commonCount = titleCities.Intersect(kingdom.cities).Count();
            if (commonCount >= Math.Ceiling(titleCities.Count * title.data.title_controlled_rate))
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

    public static void SetLevel(this Kingdom kingdom, int value)
    {

        GetOrCreate(kingdom).Level = value;
    }    

    public static bool IsInEmpire(this Kingdom kingdom)
    {
        if (kingdom == null) return false;
        if (GetOrCreate(kingdom) == null) return false;
        return ModClass.EMPIRE_MANAGER.get(GetOrCreate(kingdom).EmpireID)!=null;
    }
}