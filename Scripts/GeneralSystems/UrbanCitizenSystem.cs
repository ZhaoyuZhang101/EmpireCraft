using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.ActorAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;

namespace EmpireCraft.Scripts.GeneralSystems;

// High-level housing creates a limited urban citizen stratum instead of converting an
// entire city at once. Workers, merchants and privileged classes keep their own identity.
public static class UrbanCitizenSystem
{
    // 原版房屋等级 0~5；3 级起提供市民名额：3 级 1 个、4 级 2 个、5 级 3 个
    private const int MinimumHouseLevel = 3;

    public static bool IsCitizen(Actor actor) => actor?.city != null && actor.city.id >= 0 &&
        !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
        actor.GetOrCreate().urban_citizen_city_id == actor.city.id;

    public static void UpdateCity(City city, List<Actor> residents)
    {
        if (city == null || city.isRekt() || residents == null) return;
        int capacity = GetCapacity(city, residents.Count);
        List<Actor> citizens = residents.Where(IsCitizen).ToList();
        foreach (Actor actor in citizens.Where(actor => !IsEligible(actor)).ToList())
        {
            Release(actor);
            citizens.Remove(actor);
        }
        foreach (Actor actor in citizens.OrderBy(actor => actor.stats["intelligence"])
                     .ThenBy(actor => actor.money).Take(Math.Max(0, citizens.Count - capacity)).ToList())
        {
            Release(actor);
            citizens.Remove(actor);
        }
        int annualLimit = Math.Max(1, (int)Math.Ceiling(residents.Count * 0.03d));
        int openings = Math.Min(capacity - citizens.Count, annualLimit);
        if (openings <= 0) return;
        foreach (Actor actor in residents.Where(actor => !IsCitizen(actor) && IsEligible(actor))
                     .OrderByDescending(actor => actor.stats["intelligence"])
                     .ThenByDescending(actor => actor.money).ThenBy(actor => actor.id).Take(openings))
        {
            actor.GetOrCreate().urban_citizen_city_id = city.id;
            actor.SetSocialClass(SocialClass.Citizen);
        }
    }

    public static int GetCapacity(City city, int adultPopulation)
    {
        if (city?.buildings == null || adultPopulation <= 0) return 0;
        int housing = city.buildings.Where(building => building?.asset != null &&
                !building.isUnderConstruction() && building.asset.type == "type_house" &&
                building.asset.upgrade_level >= MinimumHouseLevel)
            .Sum(building => building.asset.upgrade_level - MinimumHouseLevel + 1);
        if (housing <= 0) return 0;
        int stage = Math.Max(0, Math.Min(3, (int)InstitutionSystem.GetFeature(
            CultureService.GetMainCulture(city), InstitutionFeatures.UrbanProductionStage)));
        float populationCap = stage >= 3 ? 0.45f : 0.35f;
        return Math.Min(housing, Math.Max(1, (int)Math.Ceiling(adultPopulation * populationCap)));
    }

    private static bool IsEligible(Actor actor)
    {
        if (actor == null || actor.IsOnOffice() || actor.isWarrior() ||
            EmpireCaftActorJudgeClass.IsManualWorkerJob(actor) || UrbanEmploymentSystem.IsEmployed(actor) ||
            LandEconomySystem.IsLandlord(actor) || actor.GetOrCreate().is_economic_merchant) return false;
        return !EmpireCaftActorJudgeClass.IsNoble(actor);
    }

    private static void Release(Actor actor)
    {
        actor.GetOrCreate().urban_citizen_city_id = -1L;
        actor.SetSocialClass(EmpireCaftActorJudgeClass.JudgeClass(actor));
    }
}
