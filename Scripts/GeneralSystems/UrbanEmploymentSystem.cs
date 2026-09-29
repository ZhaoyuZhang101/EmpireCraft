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
        report.Capacity = GetCapacity(city, residents, report.Stage);
        report.Employed = residents.Count(IsEmployed);
        report.Workers = residents.Count(actor => EmpireCaftActorJudgeClass.JudgeClass(actor) == SocialClass.Labour);
        report.AvailableResidents = residents.Count(actor => !IsEmployed(actor) && IsEligible(actor));
        return report;
    }

    public static void UpdateCity(City city)
    {
        if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isRekt()) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        double now = World.world?.getCurWorldTime() ?? -1d;
        if (now < 0d) return;
        if (data.last_urban_employment_timestamp >= 0d &&
            Date.getYearsSince(data.last_urban_employment_timestamp) < 1) return;
        data.last_urban_employment_timestamp = now;

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
        if (openings <= 0) return;
        foreach (Actor actor in residents.Where(actor => !IsEmployed(actor) && IsEligible(actor))
                     .OrderByDescending(LandEconomySystem.IsLandlessResident)
                     .ThenBy(actor => actor.money).ThenBy(actor => actor.id).Take(openings))
        {
            actor.GetOrCreate().urban_employment_city_id = city.id;
            actor.SetSocialClass(SocialClass.Labour);
        }
    }

    private static int GetStage(City city) => Math.Max(0, Math.Min(3,
        (int)InstitutionSystem.GetFeature(CultureService.GetMainCulture(city),
            InstitutionFeatures.UrbanProductionStage)));

    private static int GetCapacity(City city, List<Actor> residents, int stage)
    {
        if (stage <= 0 || residents.Count == 0) return 0;
        int merchants = residents.Count(actor => actor.GetOrCreate().is_economic_merchant);
        int buildings = city.buildings?.Count ?? 0;
        int voyages = GetRecentVoyages(city);
        int demand = stage * 2 + buildings / (stage == 1 ? 8 : 5) + merchants * stage +
                     Math.Min(4, voyages) * stage;
        float populationCap = stage switch { 1 => 0.08f, 2 => 0.18f, _ => 0.30f };
        return Math.Min(demand, Math.Max(1, (int)Math.Ceiling(residents.Count * populationCap)));
    }

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
        !actor.GetOrCreate().is_economic_merchant && actor.GetOrCreate().socialClass != SocialClass.Noble;

    private static bool IsEligible(Actor actor) => IsEligibleWorker(actor) &&
        EmpireCaftActorJudgeClass.JudgeClass(actor) == SocialClass.Peasant;

    private static void Release(Actor actor)
    {
        actor.GetOrCreate().urban_employment_city_id = -1L;
        actor.SetSocialClass(EmpireCaftActorJudgeClass.JudgeClass(actor));
    }
}
