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
using HarmonyLib;
using NCMS.Extensions;
using UnityEngine;
using Random = System.Random;

namespace EmpireCraft.Scripts.GameClassExtensions;
public static class KingdomExtension
{
    public static readonly SemaphoreSlim _sem = new SemaphoreSlim(Environment.ProcessorCount);
    public class KingdomExtraData: ExtraDataBase
    {
        public long EmpireID = -1L;
        public double TimestampEmpire = -1L;
        public double TimestampBeFeifed = -1L;
        public long HeirID = -1L;
        public int Level = 2;
        public Regime regime;
        public RegimeType regimeType;
        public KingdomType kingdomType;
        public SpecificClan kingdomSpecificClan;
        public int Money = 0;
        public long CenterArmID = -1L;
        [JsonIgnore]
        public Task<(Actor, string)> CalcTask;
        //拥有法理
        public long MainTitle = -1L;
        //想要索取的法理
        public List<long> WantedTitle = new List<long>();
        public int IndependentValue = 50;
        public bool is_need_to_choose_heir = false;
        public double last_exam_timestamp = -1L;
        public bool isFactionRebelling = false;
        public double last_tax_timestamp = -1L;
        public double last_office_exam_timestamp = -1L;
        public EmpireHeirLawType HeirLaw = EmpireHeirLawType.eldest_child;
        public EmpireHeirLawType DefaultHeirLaw = EmpireHeirLawType.eldest_child;
        //上一次加入岁币联盟的时间
        public double last_given_alliance_timestamp = -1L;
        //上一次加入朝贡国的时间
        public double last_taken_alliance_timestamp = -1L;
        //岁币国
        public long given_empire = -1L;
        //宗主国
        public long taken_empire = -1L;
        //上一次朝贡时间
        public long last_taken_time = -1L;
        //退出朝贡国倾向
        public float leave_taken_alliance_preference = 0.0f;
        public long office_id = -1L;
    }

    public static void SetHeirLaw(this Kingdom k, EmpireHeirLawType type)
    {
        k.GetOrCreate().HeirLaw = type;
    }

    public static void SetDefaultHeirLaw(this Kingdom k, EmpireHeirLawType type)
    {
        k.GetOrCreate().DefaultHeirLaw = type;
    }
    public static EmpireHeirLawType GetHeirLaw(this Kingdom k)
    {
        return k.GetOrCreate().HeirLaw;
    }
    public static EmpireHeirLawType GetDefaultHeirLaw(this Kingdom k)
    {
        return k.GetOrCreate().DefaultHeirLaw;
    }

    public static void GoToNextHeirLaw(this Kingdom k)
    {
        switch (k.GetOrCreate().HeirLaw)
        {
            case EmpireHeirLawType.eldest_child:
                k.SetHeirLaw(EmpireHeirLawType.siblings);
                break;
            case EmpireHeirLawType.siblings:
                k.SetHeirLaw(EmpireHeirLawType.grand_child_generation);
                break;
            case EmpireHeirLawType.grand_child_generation:
                k.SetHeirLaw(EmpireHeirLawType.random);
                break;
            case EmpireHeirLawType.random:
                k.SetHeirLaw(EmpireHeirLawType.officer);
                break;
            case EmpireHeirLawType.officer:
                k.SetHeirLaw(k.GetOrCreate().DefaultHeirLaw);
                break;
            case EmpireHeirLawType.smallest_child:
                k.SetHeirLaw(EmpireHeirLawType.siblings);
                break;
        }
    }

    public static void RecoverToDefaultHeir(this Kingdom k)
    {
        k.SetHeirLaw(k.GetDefaultHeirLaw());
    }
    public static bool IsNeedToTaken(this Kingdom k)
    {
        return Date.getYearsSince(k.GetOrCreate().last_taken_time)>1&&k.HasTakenAlliance();
    }

    public static void StartToTaken(this Kingdom k)
    {
        Empire empire = k.GetTakenAllianceEmpire();
        if (empire == null) return;
        var value = k.units.Count / 2;
        k.SubMoney(value);
        empire.CoreKingdom.AddMoney(value);
        if (k.GetMoney()<=0)
        {
            k.GetOrCreate().leave_taken_alliance_preference += 0.1f;
        }

        if (k.GetOrCreate().leave_taken_alliance_preference >= 1.0)
        {
            k.RemoveTakenAlliance();
            Random random = new Random();
            var possibility = random.NextDouble();
            if (possibility < 0.3f)
            {
                var war = DiplomacyHelpers.wars.newWar(empire.CoreKingdom, k, WarTypeLibrary.normal);
                war.SetEmpireWarType(EmpireWarType.伐不臣);
            }
        }
    }
    /// <summary>
    /// 获取当前国家退出朝贡联盟的倾向
    /// </summary>
    /// <param name="k"></param>
    public static float GetLeaveTakenAlliancePreference(this Kingdom k)
    {
        return k.GetOrCreate().leave_taken_alliance_preference;
    }
    public static void JoinGivenAlliance(this Kingdom k, Empire empire)
    {
        k.GetOrCreate().last_given_alliance_timestamp = World.world.getCurWorldTime();
        k.GetOrCreate().given_empire = empire.id;
        empire.given_Kingdoms.Add(k);
    }
    public static void RemoveGivenAlliance(this Kingdom k)
    {
        Empire empire = k.GetGivenAllianceEmpire();
        if (empire != null)
        {
            empire.given_Kingdoms.Remove(k);
        }
        k.GetOrCreate().given_empire = -1L;
    }
    public static bool NeedToRemoveGivenAlliance(this Kingdom k)
    {
        Empire empire = k.GetGivenAllianceEmpire();
        if (empire == null||k.IsInEmpire())
        {
            return true;
        }

        if (Date.getYearsSince(k.GetOrCreate().last_given_alliance_timestamp) > 20)
        {
            return true;
        }
        return false;
    }

    public static void JoinTakenAlliance(this Kingdom k, Empire empire)
    {
        k.GetOrCreate().last_taken_alliance_timestamp = World.world.getCurWorldTime();
        k.GetOrCreate().taken_empire = empire.id;
        k.GetOrCreate().leave_taken_alliance_preference = 0.0f;
        empire.taken_Kingdoms.Add(k);
    }
    public static void RemoveTakenAlliance(this Kingdom k)
    {
        Empire empire = k.GetTakenAllianceEmpire();
        if (empire != null)
        {
            empire.taken_Kingdoms.Remove(k);
        }
        k.GetOrCreate().taken_empire = -1L;
    }
    public static bool NeedToRemoveTakenAlliance(this Kingdom k)
    {
        Empire empire = k.GetTakenAllianceEmpire();
        return empire == null||k.IsInEmpire();
    }
    public static bool HasGivenAlliance(this Kingdom k)
    {
        return k.GetOrCreate().given_empire != -1L;
    }
    public static bool HasTakenAlliance(this Kingdom k)
    {
        return k.GetOrCreate().taken_empire != -1L;
    }
    public static Empire GetGivenAllianceEmpire(this Kingdom k)
    {
        return ModClass.EMPIRE_MANAGER.get(k.GetOrCreate().given_empire);
    }
    public static Empire GetTakenAllianceEmpire(this Kingdom k)
    {
        return ModClass.EMPIRE_MANAGER.get(k.GetOrCreate().taken_empire);
    }

    public static void SetCenterArmy(this Kingdom k, Army army)
    {
        army.name = $"{k.GetEmpire().GetEmpireName()}-{k.GetKingdomName()}驻军";
        k.GetOrCreate().CenterArmID = army.getID();
    }

    public static void StartFactionRebelling(this Kingdom k, FixedFaction faction)
    {
        k.data.name = faction.Name + "叛乱";
        k.GetOrCreate().isFactionRebelling = true;
    }

    public static void EndFactionRebelling(this Kingdom k)
    {
        k.GetOrCreate().isFactionRebelling = false;
    }

    public static bool IsFactionRebelling(this Kingdom k)
    {
        return k.GetOrCreate().isFactionRebelling;
    }

    public static Army GetCenterArmy(this Kingdom k)
    {
        var res = World.world.armies.get(k.GetOrCreate().CenterArmID);
        if (res.isRekt())
        {
            k.RemoveCenterArmy();
        } 
        return res;
    }

    public static void RemoveCenterArmy(this Kingdom k)
    {
        k.GetOrCreate().CenterArmID = -1L;
    }
    public static int GetMoney(this Kingdom k)
    {
        return k.GetOrCreate().Money;
    }
    public static void AddMoney(this Kingdom k, int money)
    {
        k.GetOrCreate().Money += money;
    }
    public static void SubMoney(this Kingdom k, int money)
    {
        k.GetOrCreate().Money -= money; 
    }
    
    public static double GetLastTaxTime(this Kingdom k)
    {
        return k.GetOrCreate().last_tax_timestamp;
    }
    public static void RecordTaxTime(this Kingdom k)
    {
        k.GetOrCreate().last_tax_timestamp = World.world.getCurWorldTime();
    }

    public static bool IsNeedToSubmitTax(this Kingdom k)
    {
        if (!k.IsInEmpire() || k.IsEmpire()) return false;
        return Date.getYearsSince(k.GetLastTaxTime()) >= 1;
    }
    public static double GetTaxRate(this Kingdom k)
    {
        var baseTax = 0.1f;
        if (k.IsInEmpire())
        {
            Empire empire = k.GetEmpire();
            if (!empire.isRekt())
            {
                baseTax = empire.data.TaxRate;
            }
        }

        switch (k.GetRegime().GetTaxLevel())
        {
            case TaxLevel.None:
                return 0.0f;
            case TaxLevel.Low:
                return baseTax;
            case TaxLevel.Medium:
                return baseTax + 0.2f;
            case TaxLevel.High:
                return baseTax + 0.4f;
            default:
                return baseTax;
        } 
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
        var res = OfficeManager.Remove(k.GetOfficeID());
        k.GetOrCreate().office_id = office.OfficeID;
    }
    public static OfficeObject GetOffice(this Kingdom k)
    {
        return OfficeManager.Offices.TryGetValue(k.GetOrCreate().office_id, out var office) ? office : null;
    }
    
    public static long GetOfficeID(this Kingdom k)
    {
        return k.GetOrCreate().office_id;
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
        Regime regime = RegimeManager.regimes[k.GetOrCreate().regimeType].Clone(k);
        k.SetRegime(regime);
        if (k.IsEmpire())
        {
            k.GetEmpire().data.centerOffice.Init(k);
        }
        regime.Factions.ForEach(f=>
        {
            f.EmpireId = k.IsEmpire()?k.GetEmpireID():-1L;
            f.FixMissedTemporaryFactions();
            f.TemporaryFactions.ForEach(tf=>tf.Init(f));
        });
    }
    public static int GetIndependentValue(this Kingdom k)
    {
        var ed = k.GetOrCreate();
        return ed?.IndependentValue ?? 100;
    }

    public static List<Actor> AllGongshi(this Kingdom k)
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

    public static OfficeObject[] GetAllOffices(this Kingdom k)
    {
        OfficeObject[] officeObjects = Array.Empty<OfficeObject>();
        foreach (var c in k.cities)
        {
            OfficeObject o = c.GetOffice();
            officeObjects.AddItem(o);
        }

        OfficeObject kingObject = k.GetOffice();
        officeObjects.AddItem(kingObject);
        return officeObjects;
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

    public static void SetMainTitle(this Kingdom k, KingdomTitle title)
    {
        title.main_kingdom = k;
        k.GetOrCreate().MainTitle = title.getID();
    }

    public static void RemoveMainTitle(this Kingdom k)
    {
        KingdomTitle kt = ModClass.KINGDOM_TITLE_MANAGER.get(k.GetOrCreate().MainTitle);
        if (kt != null) kt.main_kingdom = null;
        k.GetOrCreate().MainTitle = -1L;
    }
    
    public static KingdomTitle GetMainTitle(this Kingdom k)
    {
        if (k == null) return null;
        if (GetOrCreate(k) == null) return null;
        return ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).MainTitle);
    }

    public static bool HasMainTitle(this Kingdom k)
    {
        return ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).MainTitle)!=null;
    }

    public static bool CanBecomeEmpire(this Kingdom k)
    {
        if (!k.hasKing()) return false;
        // 基本条件检查
        if (k.isRekt() || k.IsEmpire()) return false;

        // 可能需要满足最小城市数量
        if (k.cities.Count < 2) return false;

        // 检查是否是同物种中最强大的
        int allEmpireNumInSameSpecies = World.world.kingdoms.ToList().FindAll(p => p.species_id == k.species_id && p.IsEmpire()).Count;
        return IsStrongestOfSameSpecies(k) && allEmpireNumInSameSpecies<1;
    }

    private static bool IsStrongestOfSameSpecies(Kingdom k)
    {
        return !World.world.kingdoms.Any(other =>
            other != k &&
            other.species_id == k.species_id &&
            !other.isRekt() &&
            !other.IsEmpire() &&
            IsStronger(other, k));
    }

    private static bool IsStronger(Kingdom a, Kingdom b)
    {
        return a.countTotalWarriors() > b.countTotalWarriors();
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

    public static bool IsInSameEmpire(this Kingdom kingdom, Kingdom pKingdomTaget)
    {
        if (kingdom == null) return false;
        if (!kingdom.IsInEmpire()||!pKingdomTaget.IsInEmpire()) return false;
        return kingdom.GetEmpireID() == pKingdomTaget.GetEmpireID();
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

    public static List<Empire> GetEmpiresCanBeJoined(this Kingdom kingdom)
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
                        if (kingdom.cities.Count<=(double)empire.AllCities().Count/5)
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
    public static void EmpireJoin(this Kingdom kingdom, Empire pEmpire)
    {
        GetOrCreate(kingdom).EmpireID = pEmpire.data.id;
        GetOrCreate(kingdom).TimestampEmpire = World.world.getCurWorldTime();
    }

    public static bool IsEmpire(this Kingdom kingdom)
    {
        if (kingdom == null) return false;
        if (kingdom.data == null) return false;
        var ed = GetOrCreate(kingdom);
        if (ed == null) return false;

        return ModClass.EMPIRE_MANAGER.get(ed.EmpireID)?.CoreKingdom==kingdom;
    }

    public static void EmpireLeave (this Kingdom kingdom, bool isLeave = true)
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

    public static List<KingdomTitle> GetControlledTitles(this Kingdom kingdom)
    {
        return ModClass.KINGDOM_TITLE_MANAGER.ToList().FindAll(kt=>kt.main_kingdom==kingdom);
    }
    public static bool HasAnyControlledTitle(this Kingdom kingdom)
    {
        return kingdom.GetControlledTitle().Any();
    }

    public static List<KingdomTitle> GetControlledTitle(this Kingdom kingdom)
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

    public static bool IsNeighbourWith(this Kingdom kingdom, Kingdom target)
    {
        if(kingdom.IsEmpire())
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

    public static bool IsBorder(this Kingdom kingdom)
    {
        if(kingdom.IsEmpire()) return false;
        foreach(City city in kingdom.cities)
        {
            if (city.neighbours_kingdoms.Count > 0)
            {
                foreach(Kingdom kingdom2 in city.neighbours_kingdoms)
                {
                    if (!kingdom2.IsInSameEmpire(kingdom))
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
    public static void EndWarWith(this Kingdom kingdom, Kingdom kingdom2)
    {
        var wars = kingdom.getWars()
            .Where(w => w.getAttackers().Contains(kingdom2) 
                        || w.getDefenders().Contains(kingdom2));
        wars.ForEach(w=>w.lostWar(kingdom2));
    }
}