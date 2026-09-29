using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// Personal conviction is independent of party membership and of the state's current policy.
public static class IdeologyPopulationSystem
{
    public const float PartyFoundingShare = 0.20f;
    private const string TraitPrefix = "empire_ideology_";
    private static double _lastContactScan = -1d;
    private static object _cachedWorld;
    private static readonly Dictionary<City, (PartyIdeology ideology, double at)> DominantCache = new();
    private sealed class ActorReferenceComparer : IEqualityComparer<Actor>
    {
        public bool Equals(Actor left, Actor right) => ReferenceEquals(left, right);
        public int GetHashCode(Actor actor) => RuntimeHelpers.GetHashCode(actor);
    }

    private static readonly HashSet<Actor> SyncedTraits = new(new ActorReferenceComparer());
    private static bool _traitsReady;

    public static string TraitId(PartyIdeology ideology) => TraitPrefix + ideology;

    public static void SetTraitsReady()
    {
        _traitsReady = true;
        SyncedTraits.Clear();
    }

    public static void Forget(Actor actor)
    {
        if (actor != null) SyncedTraits.Remove(actor);
    }

    public static bool OnTraitAdded(NanoObject target, BaseAugmentationAsset trait)
    {
        if (target is not Actor actor || trait?.id == null ||
            !trait.id.StartsWith(TraitPrefix, StringComparison.Ordinal) ||
            !Enum.TryParse(trait.id.Substring(TraitPrefix.Length), out PartyIdeology ideology) ||
            !Enum.IsDefined(typeof(PartyIdeology), ideology)) return true;
        actor.GetOrCreate().personal_ideology = ideology.ToString();
        foreach (PartyIdeology other in Enum.GetValues(typeof(PartyIdeology)))
            if (other != ideology && actor.hasTrait(TraitId(other))) actor.removeTrait(TraitId(other));
        if (actor.city != null) DominantCache.Remove(actor.city);
        SyncedTraits.Add(actor);
        return true;
    }

    public static void Set(Actor actor, PartyIdeology ideology)
    {
        if (actor == null || actor.isRekt()) return;
        actor.GetOrCreate().personal_ideology = ideology.ToString();
        SyncTrait(actor, ideology);
        if (actor.city != null) DominantCache.Remove(actor.city);
    }

    private static void SyncTrait(Actor actor, PartyIdeology ideology)
    {
        if (!_traitsReady || actor == null || actor.isRekt()) return;
        foreach (PartyIdeology other in Enum.GetValues(typeof(PartyIdeology)))
            if (other != ideology && actor.hasTrait(TraitId(other))) actor.removeTrait(TraitId(other));
        if (!actor.hasTrait(TraitId(ideology))) actor.addTrait(TraitId(ideology));
        SyncedTraits.Add(actor);
    }

    public static PartyIdeology Get(Actor actor)
    {
        if (actor == null) return PartyIdeology.Conservatism;
        if (!ReferenceEquals(_cachedWorld, World.world))
        {
            DominantCache.Clear();
            SyncedTraits.Clear();
            _cachedWorld = World.world;
        }
        ActorExtension.ActorExtraData data = actor.GetOrCreate();
        // 查表代替每次 Enum.TryParse：统计全城人口时每个人都要走这里
        if (data.personal_ideology != null && ParsedIdeologies.TryGetValue(data.personal_ideology, out PartyIdeology ideology))
        {
            if (!SyncedTraits.Contains(actor) || _traitsReady && !actor.hasTrait(TraitId(ideology)))
                SyncTrait(actor, ideology);
            return ideology;
        }
        if (_traitsReady)
            foreach (PartyIdeology existing in Enum.GetValues(typeof(PartyIdeology)))
                if (actor.hasTrait(TraitId(existing)))
                {
                    Set(actor, existing);
                    return existing;
                }
        ideology = PickInitial(actor);
        Set(actor, ideology);
        return ideology;
    }

    public static void Inherit(Actor child, Actor parent)
    {
        if (child == null || parent == null || child == parent) return;
        ActorExtension.ActorExtraData data = child.GetOrCreate();
        if (!string.IsNullOrEmpty(data.personal_ideology) && UnityEngine.Random.value >= 0.8f) return;
        Set(child, Get(parent));
    }

    public static void OnCityEntered(Actor actor, City city)
    {
        if (actor == null || city == null) return;
        DominantCache.Remove(city);
        if (!string.IsNullOrEmpty(actor.GetOrCreate().personal_ideology)) return;
        // A newborn may inherit through setParent later; migrants keep their existing conviction.
        Set(actor, UnityEngine.Random.value < 0.7f ? GetDominant(city) : PickInitial(actor));
    }

    private static readonly Dictionary<string, PartyIdeology> ParsedIdeologies =
        Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
            .ToDictionary(ideology => ideology.ToString(), ideology => ideology, StringComparer.Ordinal);

    // 现算城市主理念(不走一个月的缓存)，给地图图层的轮换缓存用；用数组计数，不分配 LINQ 分组
    public static PartyIdeology ComputeDominant(City city)
    {
        if (city?.units == null || city.units.Count == 0) return PartyIdeology.Conservatism;
        int[] counts = new int[ParsedIdeologies.Count];
        foreach (Actor actor in city.units)
        {
            if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
            counts[(int)Get(actor)]++;
        }
        int best = 0;
        for (int i = 1; i < counts.Length; i++)
            if (counts[i] > counts[best]) best = i;
        return counts[best] == 0 ? PartyIdeology.Conservatism : (PartyIdeology)best;
    }

    public static PartyIdeology GetDominant(City city)
    {
        if (city == null) return PartyIdeology.Conservatism;
        if (World.world == null) return GetCityCounts(city).OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key).DefaultIfEmpty(PartyIdeology.Conservatism).First();
        double now = World.world.getCurWorldTime();
        if (DominantCache.TryGetValue(city, out var cached) && now >= cached.at &&
            Date.getMonthsSince(cached.at) < 1) return cached.ideology;
        Dictionary<PartyIdeology, int> counts = GetCityCounts(city);
        PartyIdeology result = counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)
            .Select(pair => pair.Key).DefaultIfEmpty(PartyIdeology.Conservatism).First();
        DominantCache[city] = (result, now);
        return result;
    }

    public static float GetCityShare(City city, PartyIdeology ideology)
    {
        Dictionary<PartyIdeology, int> counts = GetCityCounts(city);
        int total = counts.Values.Sum();
        return total == 0 ? 0f : (float)(counts.TryGetValue(ideology, out int count) ? count : 0) / total;
    }

    public static Dictionary<PartyIdeology, int> GetCityCounts(City city) =>
        city?.units == null ? new Dictionary<PartyIdeology, int>() :
        city.units.Where(actor => actor != null && !actor.isRekt() && actor.isAlive())
            .GroupBy(Get).ToDictionary(group => group.Key, group => group.Count());

    public static Dictionary<PartyIdeology, int> GetEmpireCounts(Empire empire) =>
        empire == null ? new Dictionary<PartyIdeology, int>() :
        empire.getUnits().Where(actor => actor != null && !actor.isRekt() && actor.isAlive())
            .GroupBy(Get).ToDictionary(group => group.Key, group => group.Count());

    public static float GetEmpireShare(Empire empire, PartyIdeology ideology)
    {
        Dictionary<PartyIdeology, int> counts = GetEmpireCounts(empire);
        int total = counts.Values.Sum();
        return total == 0 ? 0f : (float)(counts.TryGetValue(ideology, out int count) ? count : 0) / total;
    }

    public static float GetKingdomShare(Kingdom kingdom, PartyIdeology ideology)
    {
        if (kingdom?.units == null) return 0f;
        int population = 0;
        int supporters = 0;
        foreach (Actor actor in kingdom.units)
        {
            if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
            population++;
            if (Get(actor) == ideology) supporters++;
        }
        return population == 0 ? 0f : (float)supporters / population;
    }

    public static float GetMilitaryShare(Empire empire, PartyIdeology ideology)
    {
        if (empire == null) return 0f;
        int soldiers = 0;
        int supporters = 0;
        foreach (Actor actor in empire.getUnits())
        {
            if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isWarrior()) continue;
            soldiers++;
            if (Get(actor) == ideology) supporters++;
        }
        return soldiers == 0 ? 0f : (float)supporters / soldiers;
    }

    public static void SeedNewPartyPolitics(Empire empire)
    {
        if (empire == null) return;
        foreach (Actor actor in empire.getUnits().Where(actor => actor != null && !actor.isRekt() && actor.isAlive()))
        {
            ActorExtension.ActorExtraData data = actor.GetOrCreate();
            if (string.IsNullOrEmpty(data.personal_ideology)) Set(actor, PickInitial(actor));
            else Get(actor);
        }
        DominantCache.Clear();
    }

    public static void IntroduceToCulture(string culture, PartyIdeology ideology)
    {
        if (World.world?.cities == null) return;
        foreach (City city in World.world.cities)
        {
            if (city?.units == null || city.isRekt() ||
                CultureService.GetMainCulture(city) != culture) continue;
            foreach (Actor actor in city.units)
            {
                if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() ||
                    Get(actor) == ideology) continue;
                float affinity = PartySystem.GetAffinity(ideology, actor.GetOrCreate().socialClass);
                if (UnityEngine.Random.value < 0.3f * Mathf.Clamp01((affinity + 100f) / 200f))
                    Set(actor, ideology);
            }
            DominantCache.Remove(city);
        }
    }

    public static void TryYearlyContact()
    {
        if (World.world?.cities == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastContactScan >= 0 && now >= _lastContactScan && Date.getYearsSince(_lastContactScan) < 1) return;
        _lastContactScan = now;
        foreach (City city in World.world.cities)
        {
            if (city?.units == null || city.isRekt()) continue;
            Dictionary<PartyIdeology, int> localCounts = GetCityCounts(city);
            if (localCounts.Count == 0) continue;
            var organizers = city.units.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
                    actor.GetFaction()?.IsParty == true)
                .GroupBy(actor => actor.GetFaction().Ideology)
                .ToDictionary(group => group.Key, group => group.Count());
            City foreignCity = city.neighbours_cities?.FirstOrDefault(neighbour => neighbour != null &&
                neighbour.kingdom != city.kingdom && neighbour.units?.Count > 0);
            PartyIdeology foreign = foreignCity == null ? GetDominant(city) : GetDominant(foreignCity);
            Empire empire = city.kingdom?.GetEmpire();
            var grievances = empire == null ? null : InstitutionSystem.GetClassGrievances(empire);
            PartyIdeology? governing = PartySystem.GetGovernmentParty(empire)?.Ideology;
            string culture = empire == null ? "" : InstitutionSystem.GetPrimaryCulture(empire);
            // 外国理念压力(见 PublicOpinionSystem)：一部分"外来接触"换成压力最大的外国理念
            PartyIdeology? pressured = empire == null ? null : PublicOpinionSystem.PickPressured(empire);
            foreach (Actor actor in city.units)
            {
                if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult()) continue;
                float contact = UnityEngine.Random.value;
                PartyIdeology target = contact < 0.15f ? PickInitial(actor) :
                    contact < 0.3f ? (pressured.HasValue && contact < 0.24f ? pressured.Value : foreign)
                    : SampleLocal(localCounts, organizers, actor);
                PartyIdeology current = Get(actor);
                if (current == target) continue;
                float classAffinity = PartySystem.GetAffinity(target, actor.GetOrCreate().socialClass);
                float advantage = Mathf.Clamp01((classAffinity -
                    PartySystem.GetAffinity(current, actor.GetOrCreate().socialClass) + 100f) / 200f);
                float grievance = grievances != null && grievances.TryGetValue(actor.GetOrCreate().socialClass,
                    out float value) ? value / 100f : 0f;
                float chance = (0.003f + 0.019f * advantage +
                    (governing.HasValue && target != governing.Value ? 0.012f * grievance : 0f)) *
                    (organizers.TryGetValue(target, out int organized) ? 1f + Mathf.Min(0.5f, organized * 0.05f) : 1f);
                if (!string.IsNullOrEmpty(culture) && InstitutionSystem.GetFeature(culture,
                        IdeologyInstitutionPaths.StageFeature(target, 1)) > 0f)
                    chance *= IdeologyInstitutionPaths.GetProfile(target).MovementMultiplier;
                // 压力越大越容易被说服(最多翻倍)
                if (pressured.HasValue && target == pressured.Value)
                    chance *= 1f + Mathf.Min(1f, PublicOpinionSystem.GetPressure(empire, target) / 50f);
                if (UnityEngine.Random.value < chance) Set(actor, target);
            }
        }
    }

    private static PartyIdeology SampleLocal(Dictionary<PartyIdeology, int> counts,
        Dictionary<PartyIdeology, int> organizers, Actor actor)
    {
        float total = 0f;
        foreach (KeyValuePair<PartyIdeology, int> pair in counts)
            total += pair.Value * (0.65f + 0.7f * Mathf.Clamp01((PartySystem.GetAffinity(pair.Key,
                actor.GetOrCreate().socialClass) + 100f) / 200f)) +
                (organizers.TryGetValue(pair.Key, out int members) ? members * 2f : 0f);
        float roll = UnityEngine.Random.value * total;
        foreach (KeyValuePair<PartyIdeology, int> pair in counts)
        {
            roll -= pair.Value * (0.65f + 0.7f * Mathf.Clamp01((PartySystem.GetAffinity(pair.Key,
                actor.GetOrCreate().socialClass) + 100f) / 200f)) +
                (organizers.TryGetValue(pair.Key, out int members) ? members * 2f : 0f);
            if (roll <= 0f) return pair.Key;
        }
        return counts.Keys.First();
    }

    private static PartyIdeology PickInitial(Actor actor)
    {
        Empire empire = actor.kingdom?.GetEmpire();
        PartyIdeology[] choices = empire == null || !PartySystem.IsActive(empire)
            ? PartySystem.BaseIdeologies.ToArray()
            : Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>().ToArray();
        if (choices.Length == 0) return PartyIdeology.Conservatism;
        float[] weights = choices.Select(ideology =>
            Mathf.Max(1f, PartySystem.GetAffinity(ideology, actor.GetOrCreate().socialClass) + 25f) *
            (empire == null || PartySystem.IsIdeologyUnlocked(empire, ideology) ? 1f : 0.3f)).ToArray();
        float roll = UnityEngine.Random.value * weights.Sum();
        for (int index = 0; index < choices.Length; index++)
            if ((roll -= weights[index]) <= 0f) return choices[index];
        return choices[choices.Length - 1];
    }
}
