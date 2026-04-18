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
using EmpireCraft.Scripts.AI.KingdomAI;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using HarmonyLib;
using NCMS.Extensions;
using UnityEngine;
using Random = System.Random;

namespace EmpireCraft.Scripts.GameClassExtensions;

public class TemporaryPushProgress
{
    public bool StartToPushTf = false;
    public TemporaryFactionType CurrentPushTfType = TemporaryFactionType.供养宗室;
    public int Progress = 0;
    public static int CalcPolicyProgressAdd(double p, int currentProgress)
    {
        int add;

        if (p < 200) add = 1;
        else if (p < 400) add = 2;
        else if (p < 700) add = 3;
        else if (p < 1000) add = 4;
        else add = 5;

        // 接近封顶时减速，防止后期涨太快
        if (currentProgress >= 90) add = Math.Min(add, 1);
        else if (currentProgress >= 75) add = Math.Min(add, 2);
        else if (currentProgress >= 60) add = Math.Min(add, 3);

        return add;
    }
    /// <summary>
    /// 推动诉求
    /// </summary>
    /// <param name="pActor"></param>
    public void Push(Actor pActor)
    {
        var identity = pActor.GetIdentity();
        if (identity == null)
        {
            Progress = 0;
            StartToPushTf = false;
            return;
        }
        int add = CalcPolicyProgressAdd(identity.TotalPerformance, Progress);
        Progress = Math.Min(100, Progress + add);
    }
}
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
        [JsonIgnore]
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
        public double corruption_rate = 0.0f;
        public double last_exam_timestamp = -1L;
        public bool isFactionRebelling = false;
        public double last_tax_timestamp = -1L;
        public double last_office_exam_timestamp = -1L;
        public double corruption_timestamp = -1L;
        public EmpireHeirLawType HeirLaw = EmpireHeirLawType.eldest_child;
        public EmpireHeirLawType DefaultHeirLaw = EmpireHeirLawType.eldest_child;
        public double last_system_change_timestamp = -1L;
        //上一次加入岁币联盟的时间
        public double last_given_alliance_timestamp = -1L;
        //上一次加入朝贡国的时间
        public double last_taken_alliance_timestamp = -1L;
        public double last_kingdom_status_ts = -1L;
        public double last_tf_check_ts = -1L;
        public double last_plots_check_ts = -1L;
        public double last_cabinet_check_ts = -1L;
        public double last_religion_check_ts = -1L;
        [JsonIgnore]
        public List<War> cached_wars = new List<War>();
        public double last_wars_ts = -1L;

        public int selfChangeRegimePlotsCountDown = 0;
        //岁币国
        public long given_empire = -1L;
        //宗主国
        public long taken_empire = -1L;
        //上一次朝贡时间
        public long last_taken_time = -1L;
        //退出朝贡国倾向
        public float leave_taken_alliance_preference = 0.0f;
        public long office_id = -1L;
        public bool isEmpire = false;
        public int cached_warriors = 0;
        public int cached_population = 0;
        public double last_cached_timestamp = -1L;
    }

    public static void SystemChange(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().last_system_change_timestamp = World.world.getCurWorldTime();
    }

    public static int GetSystemChangeYear(this Kingdom kingdom)
    {
        return Date.getYearsSince(kingdom.GetOrCreate().last_system_change_timestamp);
    }

    public static void CacheData(this Kingdom kingdom)
    {
        var ed = kingdom.GetOrCreate();
        if (ed.last_cached_timestamp <= 0 || Date.getYearsSince(ed.last_cached_timestamp) >= 1)
        {
            ed.last_cached_timestamp = -1L;
            ed.cached_population = kingdom.getPopulationPeople();
            ed.cached_warriors = kingdom.countTotalWarriors();
            ed.last_cached_timestamp = World.world.getCurWorldTime();
        }
    }
    public static void FinishedSelfPlot(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().selfChangeRegimePlotsCountDown = 10;
    }
    public static void CountingFinishedSelfPlot(this Kingdom kingdom)
    {
        if (kingdom.GetOrCreate().selfChangeRegimePlotsCountDown > 0)
        {
            kingdom.GetOrCreate().selfChangeRegimePlotsCountDown -= 1;  
        }
    }
    public static bool IsCountingSelfPlot(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().selfChangeRegimePlotsCountDown > 0;
    }
    public static void StartCorrupting(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().corruption_timestamp = World.world.getCurWorldTime();
    }
    public static bool IsStartCorrupting(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().corruption_timestamp > 0;
    }

    public static void EndCorrupting(this Kingdom kingdom)
    {
        kingdom.GetOrCreate().corruption_timestamp = -1L;
    }

    public static int GetCorruptionTime(this Kingdom kingdom)
    {
        if (kingdom.GetOrCreate().corruption_timestamp < 0)
        {
            return -1;
        }
        return Date.getYearsSince(kingdom.GetOrCreate().corruption_timestamp);
    }
    public static void AddCorruptionRate(this Kingdom kingdom, double addition)
    {
        if (kingdom.GetCorruptionRate() < 1.0f||kingdom.GetCorruptionRate()>0.0f)
        {
            kingdom.GetOrCreate().corruption_rate += addition;
            if (kingdom.GetCorruptionRate() > 1.0f)
            {
                kingdom.SetCorruptionRate(1.0f);
            }

            if (kingdom.GetCorruptionRate() < 0.0f)
            {
                kingdom.SetCorruptionRate(0.0f);
            }
        }
        
    }

    public static double GetCorruptionRate(this Kingdom kingdom)
    {
        return kingdom.GetOrCreate().corruption_rate;
    }
    

    public static void SetCorruptionRate(this Kingdom kingdom, Double value)
    {
        kingdom.GetOrCreate().corruption_rate = value;
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
                k.SetHeirLaw(!k.IsEmpire() ? EmpireHeirLawType.officer : k.GetOrCreate().DefaultHeirLaw);
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
        if (empire == null || empire.isRekt() || empire.IsArchived()) return;
        var core = empire.CoreKingdom;
        if (core == null || core.isRekt()) return;
        var value = k.units != null ? k.units.Count / 2 : 0;
        k.SubMoney(value);
        core.AddMoney(value);
        if (k.GetMoney()<=0)
        {
            if ((k.units?.Count ?? 0) / 3 > empire.getUnits().Count())
            {
                k.GetOrCreate().leave_taken_alliance_preference += 0.05f;
            }
        }

        if (!(k.GetOrCreate().leave_taken_alliance_preference >= 1.0)) return;
        k.RemoveTakenAlliance();
        Random random = new Random();
        var possibility = random.NextDouble();
        if (core.GetMoney() <= 0) return;
        if (k.countTotalWarriors() > empire.countWarriors()) return;
        if (!(possibility < 0.2f)) return;
        var war = DiplomacyHelpers.wars.newWar(core, k, WarTypeLibrary.normal);
        war.SetEmpireWarType(EmpireWarType.伐不臣);
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
        if (k == null || empire == null || empire.IsArchived())
        {
            return;
        }

        k.GetOrCreate().last_taken_alliance_timestamp = World.world.getCurWorldTime();
        k.GetOrCreate().taken_empire = empire.id;
        k.GetOrCreate().leave_taken_alliance_preference = 0.0f;
        empire.taken_Kingdoms ??= new List<Kingdom>();
        if (!empire.taken_Kingdoms.Contains(k))
        {
            empire.taken_Kingdoms.Add(k);
        }
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
        army.name = $"{k?.GetEmpire()?.GetEmpireName()}-{k?.GetKingdomName()}驻军";
        k.GetOrCreate().CenterArmID = army.getID();
    }

    public static void StartFactionRebelling(this Kingdom k, FixedFaction faction)
    {
        k.data.name = faction?.Name??k.name + "叛乱";
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
            return null;
        } 
        return res;
    }

    public static void RemoveCenterArmy(this Kingdom k)
    {
        k.GetOrCreate().CenterArmID = -1L;
    }
    public static int GetMoney(this Kingdom k)
    {
        if (k.isRekt()) return 0;
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
        if (k != null && k.IsInEmpire())
        {
            Empire empire = k.GetEmpire();
            if (empire != null && !empire.isRekt() && !empire.IsArchived())
            {
                baseTax = empire.data.TaxRate;
            }
        }

        var regime = k?.GetRegime();
        if (regime != null)
        {
            switch (regime.GetTaxLevel())
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
        return baseTax;
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
        if (k == null) return null;
        var ed = k.GetOrCreate();
        if (ed == null) return null;
        var reg = ed.regime;
        if (reg == null)
        {
            k.LoadRegime();
            reg = ed.regime;
        }
        return reg;
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
        var ed = k.GetOrCreate();
        var type = ed.regimeType;
        Regime baseRegime = null;
        if (RegimeManager.regimes != null)
        {
            RegimeManager.regimes.TryGetValue(type, out baseRegime);
            if (baseRegime == null)
            {
                RegimeManager.regimes.TryGetValue(RegimeType.Feudalism, out baseRegime);
            }
        }
        Regime regime = baseRegime?.Clone(k);
        k.SetRegime(regime);
        if (k.IsEmpire())
        {
            var empire = k.GetEmpire();
            if (empire != null)
            {
                if (empire.data.centerOffice == null)
                {
                    empire.data.centerOffice = new CenterOffice();
                    var core = empire.CoreKingdom ?? k;
                    empire.data.centerOffice.Init(core);
                }
            }
        }
        var factions = regime?.GetPlayerFactions();
        if (factions != null)
        {
            factions.ForEach(f =>
            {
                f.EmpireId = k.IsEmpire() ? k.GetEmpireID() : -1L;
                f.FixMissedTemporaryFactions();
                if (f.TemporaryFactions != null)
                {
                    f.TemporaryFactions.ForEach(tf => tf.Init(f));
                }
            });
        }
    }

    public static void InitialRegime(this Kingdom k)
    {
        var culture = ConfigData.speciesCulturePair.TryGetValue(k.asset.id, out string speciesCulture)? speciesCulture : "Western";
        RegimeType regimeType = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
            ? setting.regime
            : RegimeType.Feudalism;
        k.SetRegimeType(regimeType);
        k.LoadRegime();
        k.GetRegime().SetAllowDiplomacy(true);
        var regime = k.GetRegime();
        var type = EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(k);
        BureauSetting set = null;
        if (regime?.bureau_config?.kingdoms != null)
        {
            regime.bureau_config.kingdoms.TryGetValue(type, out set);
        }
        if (set == null)
        {
            set = new BureauSetting
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
                city_type = CityType.Feudalism_city
            };
        }
        OfficeObject officeObject = new OfficeObject();
        officeObject.InitialOffice(set);
        officeObject.regimeType = regime.type;
        officeObject.meta_object = k;
        officeObject.is_local = true;
        k.SetOffice(officeObject);
        foreach (var city in k.cities)
        {
            city.InitialRegime();
        }
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
        if (k == null || title == null || title.isRekt()) return;
        if (title.title_capital == null || title.title_capital.isRekt()) return;
        var current = ModClass.KINGDOM_TITLE_MANAGER.get(k.GetOrCreate().MainTitle);
        if (current != null && current != title && current.main_kingdom == k)
        {
            current.main_kingdom = null;
        }
        if (title.main_kingdom != null && title.main_kingdom != k)
        {
            title.main_kingdom.RemoveMainTitle();
        }
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
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(GetOrCreate(k).MainTitle);
        if (title == null || title.isRekt() || title.title_capital == null || title.title_capital.isRekt())
        {
            k.GetOrCreate().MainTitle = -1L;
            return null;
        }

        var hasValidCity = title.city_list_hash != null && title.city_list_hash.Any(c => c != null && !c.isRekt() && c.kingdom == k);
        if (!hasValidCity)
        {
            if (title.main_kingdom == k)
            {
                title.main_kingdom = null;
            }
            k.GetOrCreate().MainTitle = -1L;
            return null;
        }

        if (title.main_kingdom != null && title.main_kingdom != k)
        {
            k.GetOrCreate().MainTitle = -1L;
            return null;
        }

        if (title.main_kingdom == null)
        {
            title.main_kingdom = k;
        }

        return title;
    }

    public static bool HasMainTitle(this Kingdom k)
    {
        return k.GetMainTitle() != null;
    }

    public static bool TrySetPreferredMainTitle(this Kingdom k, KingdomTitle title)
    {
        if (k == null || title == null || title.isRekt()) return false;
        if (title.title_capital == null || title.title_capital.isRekt()) return false;

        var current = k.GetMainTitle();
        bool shouldReplace = current == null;

        if (!shouldReplace && current != title)
        {
            bool currentInvalid = current.title_capital == null || current.title_capital.isRekt();
            bool currentFallbackCapitalTitle = k.capital != null && current.title_capital == k.capital;
            bool currentDetachedFromKingdom = !current.getCities().Any(c => c != null && !c.isRekt() && c.kingdom == k);

            shouldReplace = currentInvalid || currentFallbackCapitalTitle || currentDetachedFromKingdom;
        }

        if (!shouldReplace) return false;

        k.SetMainTitle(title);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(k);
        return true;
    }

    public static KingdomTitle GetCapitalMainTitleCandidate(this Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.hasKing() || !kingdom.hasCapital()) return null;
        if (!kingdom.capital.hasTitle()) return null;
        var title = kingdom.capital.GetTitle();
        if (title == null || title.isRekt()) return null;
        if (title.owner != kingdom.king && !(kingdom.king.GetOwnedTitle()?.Contains(title.id) ?? false)) return null;
        return title;
    }

    public static KingdomTitle GetAncestralMainTitleCandidate(this Kingdom kingdom)
    {
        if (kingdom == null || !kingdom.hasKing()) return null;
        var clan = kingdom.king.GetSpecificClan();
        var city = clan?.GetAncestralCity();
        if (city == null || city.isRekt() || !city.hasTitle()) return null;
        var title = city.GetTitle();
        if (title == null || title.isRekt()) return null;
        if (title.owner != kingdom.king && !(kingdom.king.GetOwnedTitle()?.Contains(title.id) ?? false)) return null;
        return title;
    }

    public static KingdomTitle GetPreferredMainTitleCandidate(this Kingdom kingdom)
    {
        var capitalTitle = kingdom.GetCapitalMainTitleCandidate();
        if (capitalTitle != null) return capitalTitle;

        var ancestralTitle = kingdom.GetAncestralMainTitleCandidate();
        if (ancestralTitle != null) return ancestralTitle;

        return null;
    }

    public static bool ChangeMainTitle(this Kingdom kingdom, KingdomTitle title, bool writeLog = true)
    {
        if (kingdom == null || title == null || title.isRekt()) return false;
        if (title.title_capital == null || title.title_capital.isRekt()) return false;

        var current = kingdom.GetMainTitle();
        if (current == title) return false;

        kingdom.SetMainTitle(title);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        if (writeLog)
        {
            TranslateHelper.LogKingdomChangeMainTitle(kingdom, current, title);
        }
        return true;
    }

    public static bool CanBecomeEmpire(this Kingdom k)
    {
        if (!k.hasKing()) return false;
        if (k.IsInEmpire()) return false;
        // 基本条件检查
        if (k.isRekt() || k.IsEmpire()) return false;
        if (k.countUnits()<200) return false;
        // 检查是否是同物种中最强大的
        int allEmpireNumInSameSpecies = World.world.kingdoms.ToList().FindAll(p => p.getSpecies() == k.getSpecies() && p.IsEmpire()).Count;
        return IsStrongestOfSameSpecies(k) && allEmpireNumInSameSpecies<1;
    }

    private static bool IsStrongestOfSameSpecies(Kingdom k)
    {
        return !World.world.kingdoms.Any(other =>
            other != k &&
            other.getSpecies() == k.getSpecies() &&
            !other.isRekt() &&
            IsStronger(other, k));
    }

    private static bool IsStronger(Kingdom a, Kingdom b)
    {
        if (a.IsEmpire())
        {
            var emp = a.GetEmpire();
            var empUnits = emp?.getUnits();
            if (emp == null || emp.isRekt() || emp.IsArchived() || empUnits == null) return false;
            return empUnits.Count() > (b?.units?.Count ?? 0);
        }
        return a.countUnits() > (b?.countUnits() ?? 0);
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
        if (string.IsNullOrWhiteSpace(kingdom.name))
        {
            return GetKingdomFrontFallback(kingdom);
        }

        string[] nameParts = kingdom.name.Split('\u200A');
        string result;
        if (nameParts.Length <= 2)
        {
            result = nameParts[0];
        }
        else
        {
            result = nameParts[nameParts.Length - 2];
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return GetKingdomFrontFallback(kingdom);
        }

        var suffix = LM.Get(kingdom.GetKingdomType().ToString());
        if (!string.IsNullOrWhiteSpace(suffix) && string.Equals(result, suffix, StringComparison.Ordinal))
        {
            return GetKingdomFrontFallback(kingdom);
        }

        return result;
    }

    private static string GetKingdomFrontFallback(Kingdom kingdom)
    {
        var title = kingdom.GetMainTitle();
        if (!string.IsNullOrWhiteSpace(title?.data?.name))
        {
            return title.data.name;
        }

        if (kingdom.hasCapital() && kingdom.capital != null && !kingdom.capital.isRekt())
        {
            var selectedName = kingdom.capital.SelectKingdomName();
            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                return selectedName;
            }

            var cityName = kingdom.capital.GetCityName();
            if (!string.IsNullOrWhiteSpace(cityName))
            {
                return cityName;
            }
        }

        return kingdom.name ?? "";
    }

    public static bool IsInSameEmpire(this Kingdom kingdom, Kingdom pKingdomTaget)
    {
        if (kingdom == null || pKingdomTaget == null) return false;
        if (!kingdom.IsInEmpire() || !pKingdomTaget.IsInEmpire()) return false;
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

    public static void CheckEmpire(this Kingdom kingdom)
    {
        if (!kingdom.IsInEmpire()) return;
        var emp = kingdom.GetEmpire();
        if (emp == null || emp.isRekt() || emp.IsArchived())
        {
            kingdom.EmpireLeave();
            return;
        }
        if (kingdom == emp.CoreKingdom)
        {
            kingdom.GetOrCreate().isEmpire = true;
        }
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
        if (!ed.isEmpire) return false;

        var empire = kingdom.GetEmpire();
        if (empire == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null || empire.CoreKingdom != kingdom)
        {
            ed.isEmpire = false;
            return false;
        }

        return true;
    }

    public static void EmpireLeave (this Kingdom kingdom, bool isLeave = true)
    {
        if (kingdom==null) return;
        if (GetOrCreate(kingdom) == null) return;
        kingdom.generateColor();
        GetOrCreate(kingdom).EmpireID = -1L;
        kingdom.GetOrCreate().isEmpire = false;
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
        if (kingdom == null || target == null) return false;
        if(kingdom.IsEmpire())
        {
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.isRekt() || empire.IsArchived()) return false;
            return empire.IsNeighbourWith(target);
        }
        var cities = kingdom.cities;
        if (cities == null || cities.Count == 0) return false;
        foreach(City city in cities)
        {
            if (city == null) continue;
            var neighbours = city.neighbours_kingdoms;
            if (neighbours == null) continue;
            if (neighbours.Contains(target))
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
        if (kingdom == null || kingdom.isRekt()) return false;
        var ed = kingdom.GetOrCreate();
        if (ed == null) return false;
        var id = ed.EmpireID;
        if (id == -1L) return false;
        if (ModClass.EMPIRE_MANAGER == null) return false;
        var emp = ModClass.EMPIRE_MANAGER.get(id);
        return emp != null && !emp.IsArchived() && !emp.isRekt();
    }
    public static void EndWarWith(this Kingdom kingdom, Kingdom kingdom2)
    {
        var wars = kingdom.GetWarsCached(false);
        for (int i = 0; i < wars.Count; i++)
        {
            var w = wars[i];
            var attackers = w.getAttackers().ToArray();
            var defenders = w.getDefenders().ToArray();
            bool has = false;
            for (int a = 0; a < attackers.Length; a++) { if (attackers[a] == kingdom2) { has = true; break; } }
            if (!has)
            {
                for (int d = 0; d < defenders.Length; d++) { if (defenders[d] == kingdom2) { has = true; break; } }
            }
            if (has) w.lostWar(kingdom2);
        }
    }

    public static List<War> GetWarsCached(this Kingdom kingdom, bool pRandom = false)
    {
        var ed = GetOrCreate(kingdom);
        if (ed.last_wars_ts > 0 && Date.getMonthsSince(ed.last_wars_ts) < 1 && ed.cached_wars != null)
        {
            return ed.cached_wars;
        }
        var warsEnum = kingdom.getWars(pRandom);
        List<War> list;
        if (warsEnum is List<War> lw)
        {
            list = lw;
        }
        else
        {
            list = warsEnum.ToList();
        }
        ed.cached_wars = list;
        ed.last_wars_ts = World.world.getCurWorldTime();
        return list;
    }
}
