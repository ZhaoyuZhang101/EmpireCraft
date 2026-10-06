using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class UrbanEmploymentReport
{
    public int Stage;
    public int Capacity;
    public int Employed;
    public int Workers;
    public int AvailableResidents;
    public int Buildings;
    public int Merchants;
    public int RecentVoyages;
    public int Factories;
}

public static class UrbanEmploymentSystem
{
    public static bool IsEmployed(Actor actor) => actor?.city != null && actor.city.id >= 0 &&
        !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
        actor.GetOrCreate().urban_employment_city_id == actor.city.id;

    public static UrbanEmploymentReport GetReport(City city)
    {
        var report = new UrbanEmploymentReport();
        if (city == null || city.isRekt()) return report;
        List<Actor> residents = GetAdults(city);
        report.Stage = GetStage(city);
        report.Buildings = city.buildings?.Count ?? 0;
        report.Merchants = residents.Count(actor => actor.GetOrCreate().is_economic_merchant);
        report.RecentVoyages = GetRecentVoyages(city);
        report.Factories = CountFactories(city);
        report.Capacity = GetCapacity(city, residents, report.Stage);
        report.Employed = residents.Count(IsEmployed);
        report.Workers = residents.Count(actor => EmpireCaftActorJudgeClass.JudgeClass(actor) == SocialClass.Labour);
        report.AvailableResidents = residents.Count(actor => !IsEmployed(actor) && IsEligible(actor));
        // 无小人模式：背景人口按阶层计入(真实人数)。工场岗位按户算出后折成人数
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            int perSlot = CityPopulationSystem.PeoplePerSlot(city);
            Dictionary<SocialClass, float> classes = BackgroundAdults(city);
            classes.TryGetValue(SocialClass.Merchant, out float merchants);
            classes.TryGetValue(SocialClass.Labour, out float labour);
            classes.TryGetValue(SocialClass.Peasant, out float peasants);
            classes.TryGetValue(SocialClass.Citizen, out float citizens);
            report.Merchants += (int)merchants;
            report.Capacity = HouseholdCapacity(city) * perSlot;
            report.Workers += (int)labour;
            report.Employed = Math.Min(report.Capacity, report.Employed + (int)labour);
            report.AvailableResidents += (int)(peasants + citizens);
        }
        return report;
    }

    // 背景人口里各阶层的成年人(真实人数)
    private static Dictionary<SocialClass, float> BackgroundAdults(City city)
    {
        var result = new Dictionary<SocialClass, float>();
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return result;
        foreach (PopGroup group in data.groups)
        {
            float adults = group.Background * 0.7f;
            if (adults <= 0f) continue;
            result.TryGetValue(group.social_class, out float sum);
            result[group.social_class] = sum + adults;
        }
        return result;
    }

    // 无小人模式：按户计的工场岗位(公式与实体居民相同，城市人口与商人数换成"实体 + 背景"的户数)，
    // 背景人口里工人的比例由它决定(见 CityPopulationSystem.TargetClassShares)
    public static int HouseholdCapacity(City city)
    {
        if (city == null || city.isRekt()) return 0;
        int stage = GetStage(city);
        int perSlot = CityPopulationSystem.PeoplePerSlot(city);
        List<Actor> residents = GetAdults(city);
        BackgroundAdults(city).TryGetValue(SocialClass.Merchant, out float merchants);
        int adults = residents.Count + (int)(CityPopulationSystem.GetBackgroundTotal(city) * 0.7f / perSlot);
        int merchantHouseholds = residents.Count(actor => actor.GetOrCreate().is_economic_merchant) +
                                 (int)(merchants / perSlot);
        return Capacity(city, stage, adults, merchantHouseholds);
    }

    public static void UpdateCity(City city)
    {
        if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isRekt()) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        double now = World.world?.getCurWorldTime() ?? -1d;
        if (now < 0d) return;
        // 各城错峰(见 YearlyStagger)
        if (data.last_urban_employment_timestamp < 0d)
        {
            data.last_urban_employment_timestamp = EmpireCraft.Scripts.HelperFunc.YearlyStagger.Initial(now);
            return;
        }
        if (Date.getYearsSince(data.last_urban_employment_timestamp) < 1) return;
        data.last_urban_employment_timestamp = EmpireCraft.Scripts.HelperFunc.YearlyStagger.Next(now);

        List<Actor> residents = GetAdults(city);
        int capacity = GetCapacity(city, residents, GetStage(city));
        List<Actor> employed = residents.Where(IsEmployed).ToList();
        foreach (Actor actor in employed.Where(actor => !IsEligibleWorker(actor)).ToList())
        {
            Release(actor);
            employed.Remove(actor);
        }
        foreach (Actor actor in employed.OrderByDescending(actor => actor.money)
                     .Take(Math.Max(0, employed.Count - capacity)).ToList())
        {
            Release(actor);
            employed.Remove(actor);
        }

        int annualLimit = Math.Max(1, (int)Math.Ceiling(residents.Count * 0.02d));
        int openings = Math.Min(capacity - employed.Count, annualLimit);
        if (openings > 0)
        {
            foreach (Actor actor in residents.Where(actor => !IsEmployed(actor) && IsEligible(actor))
                         .OrderByDescending(LandEconomySystem.IsLandlessResident)
                         .ThenBy(actor => actor.money).ThenBy(actor => actor.id).Take(openings))
            {
                actor.GetOrCreate().urban_employment_city_id = city.id;
                actor.GetOrCreate().urban_citizen_city_id = -1L;
                actor.SetSocialClass(SocialClass.Labour);
            }
        }
        UrbanCitizenSystem.UpdateCity(city, residents);
    }

    private static int GetStage(City city) => Math.Max(0, Math.Min(3,
        (int)InstitutionSystem.GetFeature(CultureService.GetMainCulture(city),
            InstitutionFeatures.UrbanProductionStage)));

    private static int GetCapacity(City city, List<Actor> residents, int stage) =>
        Capacity(city, stage, residents.Count, residents.Count(actor => actor.GetOrCreate().is_economic_merchant));

    private static int Capacity(City city, int stage, int adults, int merchants)
    {
        if (stage <= 0 || adults <= 0) return 0;
        int buildings = city.buildings?.Count ?? 0;
        int voyages = GetRecentVoyages(city);
        // 工厂(WarBox 的工厂/钢铁厂/火药厂等，类型在科技树 industry.factory_types 里配置)是最大的雇主
        int factoryJobs = CountFactories(city) * TechnologySystem.Config.industry.jobs_per_factory * stage;
        int demand = stage * 2 + buildings / (stage == 1 ? 8 : 5) + merchants * stage +
                     Math.Min(4, voyages) * stage + factoryJobs;
        float populationCap = stage switch { 1 => 0.08f, 2 => 0.18f, _ => 0.30f };
        // 工业技术让更多人口能进厂
        populationCap = Math.Min(0.6f, populationCap + TechnologySystem.GetEmploymentCapBonus(
            TechnologySystem.GetCultureOf(city)));
        return Math.Min(demand, Math.Max(1, (int)Math.Ceiling(adults * populationCap)));
    }

    public static int CountFactories(City city) =>
        city.buildings?.Count(building => building != null && !building.isUnderConstruction() &&
                                          TechnologySystem.IsFactory(building.asset)) ?? 0;

    private static int GetRecentVoyages(City city)
    {
        List<CompletedTradeVoyage> voyages = city.kingdom?.GetEmpire()?.data?.constitutional_economy?.recent_trade;
        return voyages?.Count(voyage => voyage != null && voyage.timestamp >= 0d &&
            Date.getYearsSince(voyage.timestamp) < 10 &&
            (voyage.origin_city_id == city.id || voyage.destination_city_id == city.id)) ?? 0;
    }

    private static List<Actor> GetAdults(City city) => city.units?
        .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                        actor.city == city).ToList() ?? new List<Actor>();

    private static bool IsEligibleWorker(Actor actor) => actor != null && !actor.IsOnOffice() &&
        !actor.isWarrior() && !EmpireCaftActorJudgeClass.IsManualWorkerJob(actor) &&
        !LandEconomySystem.IsLandlord(actor) &&
        !actor.GetOrCreate().is_economic_merchant && !EmpireCaftActorJudgeClass.IsNoble(actor);

    private static bool IsEligible(Actor actor)
    {
        if (!IsEligibleWorker(actor)) return false;
        SocialClass socialClass = EmpireCaftActorJudgeClass.JudgeClass(actor);
        return socialClass is SocialClass.Peasant or SocialClass.Citizen;
    }

    private static void Release(Actor actor)
    {
        actor.GetOrCreate().urban_employment_city_id = -1L;
        actor.SetSocialClass(EmpireCaftActorJudgeClass.JudgeClass(actor));
    }
}
