using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// Personal conviction is independent of party membership and of the state's current policy.
public static class IdeologyPopulationSystem
{
    public const float PartyFoundingShare = 0.10f;
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

    // 存档后清理：两个表都只是加速用，清掉后按需重建；SyncedTraits 对单位是强引用，
    // 不清的话死去的单位会一直留在内存里
    public static void ClearCaches()
    {
        DominantCache.Clear();
        SyncedTraits.Clear();
    }

    // 读档可能复用 World.world 实例，因此不能只靠对象引用判断世界是否变化。
    public static void ResetWorldState()
    {
        ClearCaches();
        _cachedWorld = World.world;
        _lastContactScan = -1d;
        _contactFrame = -1;
        ContactQueue.Cancel();
        EconomyCache.Clear();
        LandmarkBookSystem.ResetRuntimeState();
        IdeologySpreadSystem.ResetWorldState();
    }

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
            ResetWorldState();
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
        if (city == null) return PartyIdeology.Conservatism;
        int[] counts = new int[ParsedIdeologies.Count];
        if (city.units != null)
            foreach (Actor actor in city.units)
            {
                if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
                counts[(int)Get(actor)]++;
            }
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            var background = new Dictionary<PartyIdeology, int>();
            CityPopulationSystem.AddBackgroundCounts(city, background, group => group.ideology);
            foreach (KeyValuePair<PartyIdeology, int> pair in background)
                if ((int)pair.Key >= 0 && (int)pair.Key < counts.Length) counts[(int)pair.Key] += pair.Value;
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

    // 各处(年度交往、藏书影响、提示框、核心民心)频繁调用：用循环计数，不走 LINQ GroupBy 的分配
    public static Dictionary<PartyIdeology, int> GetCityCounts(City city)
    {
        var counts = new Dictionary<PartyIdeology, int>();
        if (city == null) return counts;
        if (city.units != null)
            foreach (Actor actor in city.units)
            {
                if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
                PartyIdeology ideology = Get(actor);
                counts[ideology] = counts.TryGetValue(ideology, out int count) ? count + 1 : 1;
            }
        // 无小人模式：加上背景人口的理念构成
        CityPopulationSystem.AddBackgroundCounts(city, counts, group => group.ideology);
        return counts;
    }

    public static Dictionary<PartyIdeology, int> GetEmpireCounts(Empire empire)
    {
        if (empire == null) return new Dictionary<PartyIdeology, int>();
        Dictionary<PartyIdeology, int> counts = empire.getUnits()
            .Where(actor => actor != null && !actor.isRekt() && actor.isAlive())
            .GroupBy(Get).ToDictionary(group => group.Key, group => group.Count());
        CityPopulationSystem.AddBackgroundCounts(CityPopulationSystem.CitiesOf(empire.kingdoms_list), counts,
            group => group.ideology);
        return counts;
    }

    public static float GetEmpireShare(Empire empire, PartyIdeology ideology)
    {
        Dictionary<PartyIdeology, int> counts = GetEmpireCounts(empire);
        int total = counts.Values.Sum();
        return total == 0 ? 0f : (float)(counts.TryGetValue(ideology, out int count) ? count : 0) / total;
    }

    public static float GetKingdomShare(Kingdom kingdom, PartyIdeology ideology)
    {
        if (kingdom == null) return 0f;
        int population = 0;
        int supporters = 0;
        if (kingdom.units != null)
            foreach (Actor actor in kingdom.units)
            {
                if (actor == null || actor.isRekt() || !actor.isAlive()) continue;
                population++;
                if (Get(actor) == ideology) supporters++;
            }
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            var background = new Dictionary<PartyIdeology, int>();
            CityPopulationSystem.AddBackgroundCounts(CityPopulationSystem.CitiesOf(new[] { kingdom }), background,
                group => group.ideology);
            foreach (KeyValuePair<PartyIdeology, int> pair in background)
            {
                population += pair.Value;
                if (pair.Key == ideology) supporters += pair.Value;
            }
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

    public static int IntroduceToCulture(string culture, PartyIdeology ideology, float intensity = 0.3f)
    {
        if (World.world?.cities == null) return 0;
        int changed = 0;
        foreach (City city in World.world.cities)
        {
            if (city?.units == null || city.isRekt() ||
                CultureService.GetMainCulture(city) != culture) continue;
            changed += Introduce(city.units, ideology, intensity);
            changed += IntroduceToBackground(city, ideology, intensity);
            DominantCache.Remove(city);
        }
        if (changed > 0) RegisterIdea(culture, ideology);
        return changed;
    }

    // 外力(如城中藏书)对一座城的持续说服：成年人按阶级亲和度折算后以 intensity 的概率改信
    public static int InfluenceCity(City city, PartyIdeology ideology, float intensity)
    {
        if (city?.units == null || city.isRekt() || intensity <= 0f) return 0;
        if (!TechnologySystem.AreFeatureTechsMet(CultureService.GetMainCulture(city),
                IdeologySpreadSystem.FeatureKey(ideology))) return 0;
        // 与民间交往一样受城市经济形态影响：繁荣城市更易接受自由主义、难被传统理念说服；工业城市更易接受社会主义
        intensity *= EconomyFactor(ideology, CachedEconomy(city));
        int changed = Introduce(city.units.ToList(), ideology, intensity) +
                      IntroduceToBackground(city, ideology, intensity);
        if (changed > 0)
        {
            DominantCache.Remove(city);
            RegisterIdea(CultureService.GetMainCulture(city), ideology);
        }
        return changed;
    }

    public static void TryYearlyContact()
    {
        if (World.world?.cities == null) return;
        double now = World.world.getCurWorldTime();
        if (_lastContactScan >= 0 && now >= _lastContactScan && Date.getYearsSince(_lastContactScan) < 1) return;
        _lastContactScan = now;
        EnsureUnlockedIdeologiesSeeded();
        // 全图一次处理完会在那一帧卡住(每个成年人都要算)，改成排队、每帧只处理一部分城市(见 TickContact)
        ContactQueue.Start(World.world.cities);
        EconomyCache.Clear();
    }

    private static readonly FrameBudgetQueue<City> ContactQueue = new(2d, ContactCity, "理念交往");
    private static int _contactFrame = -1;

    // 每帧最多花约 2 ms 处理排队的城市；一轮处理完后开始结算藏书的理念影响(同样分帧)
    public static void TickContact()
    {
        if (Time.frameCount == _contactFrame) return;
        _contactFrame = Time.frameCount;
        LandmarkBookSystem.TickCityInfluence();
        if (ContactQueue.Tick()) LandmarkBookSystem.StartCityInfluence();
    }

    private static void ContactCity(City city)
    {
        if (city?.units == null || city.isRekt()) return;
        Dictionary<PartyIdeology, int> localCounts = GetCityCounts(city);
        if (localCounts.Count == 0) return;
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
        string culture = CultureService.GetMainCulture(city);
        // 旧存档、移民和手动赋予的理念也属于当地已经接触过的思想。
        foreach (PartyIdeology ideology in localCounts.Keys) RegisterIdea(culture, ideology);
        HashSet<PartyIdeology> available = GetAvailableIdeologies(culture, empire);
        // 外国理念压力(见 PublicOpinionSystem)：一部分"外来接触"换成压力最大的外国理念
        PartyIdeology? pressured = empire == null ? null : PublicOpinionSystem.PickPressured(empire);
        PartyIdeology? founding = empire == null ? null : ConstitutionSystem.GetClauses(empire)?.founding_ideology;
        float externalSusceptibility = empire == null ? 1f : PublicOpinionSystem.ExternalSusceptibility(empire);
        float fatigue = IdeologyDynamicsSystem.GetFatigue(empire);
        float economicTrend = IdeologyDynamicsSystem.GetTrend(empire);
        // 思想解放期(见 IdeologyDynamicsSystem)：1 = 刚开始，0 = 没有
        float liberation = IdeologyDynamicsSystem.GetLiberation(empire);
        // 现代化侵蚀传统：开放政党政治后，城市化、学校、工业逐年削弱传统理念的吸引力(60 年后最多减六成)
        double modernSince = string.IsNullOrEmpty(culture) ? -1d
            : InstitutionSystem.GetFeatureEnactedTime(culture, PartySystem.FeaturePartyPolitics);
        float traditionDecay = modernSince < 0d ? 0f : 0.6f * Mathf.Clamp01(Date.getYearsSince(modernSince) / 60f);
        // 经济越繁荣(商人、市民多，有高级民居)，自由主义越盛行、传统理念越式微；
        // 工人越多的工业城市，社会主义越盛行
        CityEconomyProfile economy = CachedEconomy(city);
        List<PartyIdeology> liberal = available.Where(IdeologyFamilies.IsLiberal).ToList();
        List<PartyIdeology> socialist = available.Where(IdeologyFamilies.IsLabourMovement).ToList();
        List<PartyIdeology> agrarianTraditional = available.Where(IdeologyFamilies.IsAgrarianTraditional).ToList();
        List<PartyIdeology> agrarianRadical = available.Where(IdeologyFamilies.IsAgrarianRadical).ToList();
        // 自由主义是中间态：一旦在城里占优(过半)，言论结社自由让各种主义都冒出来
        int localTotal = localCounts.Values.Sum();
        float liberalShare = localTotal == 0 ? 0f
            : localCounts.Where(pair => IdeologyFamilies.IsLiberal(pair.Key)).Sum(pair => pair.Value) / (float)localTotal;
        float pluralism = Mathf.Clamp01((liberalShare - 0.5f) * 2f);
        List<PartyIdeology> otherIsms = available.Where(ideology => !IdeologyFamilies.IsLiberal(ideology)).ToList();
        foreach (Actor actor in city.units)
        {
            if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult()) continue;
            ActorExtension.ActorExtraData data = actor.GetOrCreate();
            SocialClass socialClass = data.socialClass;
            bool classChanged = !string.IsNullOrEmpty(data.last_ideology_social_class) &&
                                data.last_ideology_social_class != socialClass.ToString();
            data.last_ideology_social_class = socialClass.ToString();
            if (Decide(socialClass, Get(actor), actor.getAge(), classChanged, out PartyIdeology target))
                Set(actor, target);
        }
        ContactBackground(city, Decide);

        // 一个人(或背景人口里的一个"代表")这一年的思想交往：接触到哪种理念、会不会改信
        bool Decide(SocialClass socialClass, PartyIdeology current, int age, bool classChanged, out PartyIdeology target)
        {
            float grievance = grievances != null && grievances.TryGetValue(socialClass,
                out float value) ? value / 100f : 0f;
            float contact = UnityEngine.Random.value;
            bool classResponse = UnityEngine.Random.value < 0.15f + 0.25f * grievance;
            target = classResponse
                ? PickClassResponse(socialClass, city.kingdom, available, grievance)
                : contact < 0.15f ? PickInitial(socialClass, city, city.kingdom)
                : contact < 0.3f ? (pressured.HasValue && available.Contains(pressured.Value) && contact < 0.24f
                    ? pressured.Value : available.Contains(foreign) ? foreign : PickInitial(socialClass, city, city.kingdom))
                // 思想解放期：不再一味随大流，一部分接触改为按自身阶层自由选择
                : liberation > 0f && UnityEngine.Random.value < 0.5f * liberation ? PickInitial(socialClass, city, city.kingdom)
                : SampleLocal(localCounts, organizers, socialClass, city, available);
            // 繁荣之地更常接触到自由主义思想
            if (otherIsms.Count > 0 && UnityEngine.Random.value < pluralism * PluralismContact)
                target = otherIsms[UnityEngine.Random.Range(0, otherIsms.Count)];
            else if (liberal.Count > 0 && UnityEngine.Random.value < economy.Prosperity * ProsperityLiberalContact)
                target = WeightedChoice(liberal, socialClass, null);
            // 工业城市更常接触到社会主义思想
            else if (socialist.Count > 0 && UnityEngine.Random.value < economy.Industry * IndustrySocialistContact)
                target = WeightedChoice(socialist, socialClass, null);
            // 农村：无地农民接触激进左翼，有地小农守着传统
            else if (agrarianRadical.Count > 0 && UnityEngine.Random.value < economy.Landless * AgrarianRadicalContact)
                target = WeightedChoice(agrarianRadical, socialClass, null);
            else if (agrarianTraditional.Count > 0 &&
                     UnityEngine.Random.value < economy.Settled * AgrarianTraditionalContact * (1f - traditionDecay))
                target = WeightedChoice(agrarianTraditional, socialClass, null);
            if (current == target) return false;
            float classAffinity = PartySystem.GetAffinity(target, socialClass);
            float advantage = Mathf.Clamp01((classAffinity -
                PartySystem.GetAffinity(current, socialClass) + 100f) / 200f);
            float chance = (0.003f + 0.019f * advantage +
                (governing.HasValue && target != governing.Value ? 0.012f * grievance : 0f)) *
                (organizers.TryGetValue(target, out int organized) ? 1f + Mathf.Min(0.5f, organized * 0.05f) : 1f);
            if (classResponse) chance += 0.012f + 0.045f * grievance;
            if (classChanged) chance += 0.05f;
            if (city.kingdom?.GetOrCreate().is_peasant_revolutionary_government == true &&
                socialClass is SocialClass.Peasant or SocialClass.Labour && IdeologyFamilies.IsLeft(target))
                chance += 0.08f;
            if (!string.IsNullOrEmpty(culture) && InstitutionSystem.GetFeature(culture,
                    IdeologyInstitutionPaths.StageFeature(target, 1)) > 0f)
                chance *= IdeologyInstitutionPaths.GetProfile(target).MovementMultiplier;
            // 压力越大越容易被说服(最多翻倍)
            if (pressured.HasValue && target == pressured.Value)
                chance *= 1f + Mathf.Min(1f, PublicOpinionSystem.GetPressure(empire, target) / 50f);
            // 宪法的意识形态强度：来自外国的思潮(邻国主流理念、外国理念压力)改变立国理念信徒的难易——
            // 高强度宣传不易被外部侵蚀，放开意识形态则容易被渗透
            if (empire != null && current == founding && target != founding &&
                (pressured.HasValue && target == pressured.Value || foreignCity != null && target == foreign))
                chance *= externalSusceptibility;
            // 代际更替：年轻人更容易接受新思想(思想解放期最多 ×3)，老人守着旧观念
            if (age < 30) chance *= 1.5f * (1f + liberation);
            else if (age > 55) chance *= 0.6f;
            // 现代化侵蚀传统
            if (traditionDecay > 0f && IdeologyFamilies.IsTraditional(target)) chance *= 1f - traditionDecay;
            // 意识形态疲劳：立国理念的信徒越来越不当真，满疲劳时改信的概率翻倍(见 IdeologyDynamicsSystem)
            if (founding.HasValue && current == founding.Value && target != founding.Value)
                chance *= 1f + fatigue / 100f;
            // 经济趋势：繁荣时自由主义更易传播，衰退(含金融危机)时激进左右翼更易传播
            if (economicTrend >= IdeologyDynamicsSystem.GrowthThreshold && IdeologyFamilies.IsLiberal(target))
                chance *= 1.3f;
            else if (economicTrend <= IdeologyDynamicsSystem.DeclineThreshold &&
                     (IdeologyFamilies.IsAgrarianRadical(target) || IdeologyFamilies.IsAuthoritarian(target)))
                chance *= 1.4f;
            chance *= EconomyFactor(target, economy);
            if (!IdeologyFamilies.IsLiberal(target)) chance *= 1f + PluralismBoost * pluralism;
            return UnityEngine.Random.value < chance;
        }
    }

    private delegate bool ContactDecision(SocialClass socialClass, PartyIdeology current, int age, bool classChanged,
        out PartyIdeology target);

    // 背景人口的思想交往(无小人模式)：每个人口组抽几名"代表"走和实体单位一样的接触、改信判定，
    // 每名代表改信就把这一组成年人的 1/代表数 改信过去。这样城里的理念占比由全体居民的交往决定，
    // 不再只看留在地图上的少数名人
    private const int BackgroundContactSamples = 8;
    private const float BackgroundAdultShare = 0.7f;

    private static void ContactBackground(City city, ContactDecision decide)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        Data.CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return;
        var moves = new List<(Data.PopGroup from, PartyIdeology to, float amount)>();
        foreach (Data.PopGroup group in data.groups)
        {
            float adults = group.Background * BackgroundAdultShare;
            if (adults < 1f) continue;
            int samples = Mathf.Clamp(Mathf.CeilToInt(adults), 1, BackgroundContactSamples);
            float each = adults / samples;
            for (int i = 0; i < samples; i++)
            {
                int age = UnityEngine.Random.Range(16, 70);
                if (decide(group.social_class, group.ideology, age, false, out PartyIdeology target) &&
                    target != group.ideology)
                    moves.Add((group, target, each));
            }
        }
        foreach ((Data.PopGroup from, PartyIdeology to, float amount) in moves)
        {
            float removed = CityPopulationSystem.RemoveBackground(from, amount);
            if (removed > 0f)
                CityPopulationSystem.AddBackground(city, from.social_class, from.culture, from.species, to, removed);
        }
        if (moves.Count > 0) DominantCache.Remove(city);
    }

    // 繁荣度对理念的影响：繁荣城市里接触自由主义思想的概率、改信自由主义的加成、改信传统理念的折扣
    private const float ProsperityLiberalContact = 0.2f;
    private const float ProsperityLiberalBoost = 1.5f;
    private const float ProsperityTraditionalDamp = 0.6f;



    public static float CityProsperity(City city) => CachedEconomy(city).Prosperity;

    // 一轮年度交往/藏书影响里同一座城会被反复问(本城、作为邻城……)，经济形态一年内变化不大，每轮只算一次
    private static readonly Dictionary<City, CityEconomyProfile> EconomyCache = new();

    public static CityEconomyProfile CachedEconomy(City city)
    {
        if (city == null) return default;
        if (EconomyCache.TryGetValue(city, out CityEconomyProfile cached)) return cached;
        CityEconomyProfile profile = CityEconomy(city);
        EconomyCache[city] = profile;
        return profile;
    }

    public readonly struct CityEconomyProfile
    {
        public readonly float Prosperity;   // → 自由主义
        public readonly float Industry;     // → 社会主义
        public readonly float Settled;      // 有地农民 → 传统理念
        public readonly float Landless;     // 无地农民 → 激进左翼(农民革命)

        public CityEconomyProfile(float prosperity, float industry, float settled, float landless)
        {
            Prosperity = prosperity;
            Industry = industry;
            Settled = settled;
            Landless = landless;
        }
    }

    // 城市经济形态(各 0~1)：
    //   繁荣度 = 商人与市民占成年人口的比例(三成即满)占七成 + 有高级民居(能容纳市民)占三成 → 自由主义；
    //   工业化 = 工人占成年人口的比例(三成即满)占七成 + 有工厂占三成 → 社会主义；
    //   农业化 = 农民占成年人口的比例(一半即满)，再按无地农民比例(五成即满)拆成：
    //     有地的小农 → 保守主义、教派民主等传统理念；无地农民 → 共产主义、社会主义、无政府主义(农民革命)。
    public static CityEconomyProfile CityEconomy(City city)
    {
        if (city?.units == null) return default;
        int adults = 0, burghers = 0, workers = 0, peasants = 0;
        foreach (Actor actor in city.units)
        {
            if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult()) continue;
            adults++;
            ActorExtension.ActorExtraData data = actor.GetOrCreate();
            if (data.is_economic_merchant || data.socialClass == SocialClass.Citizen) burghers++;
            else if (data.socialClass == SocialClass.Labour) workers++;
            else if (data.socialClass == SocialClass.Peasant) peasants++;
        }
        // 无小人模式：背景人口的成年人按阶层计入
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            Data.CityPopulationData population = CityPopulationSystem.Get(city);
            if (population?.groups != null)
                foreach (Data.PopGroup group in population.groups)
                {
                    int count = Mathf.RoundToInt(group.Background * 0.7f);
                    if (count <= 0) continue;
                    adults += count;
                    if (group.social_class is SocialClass.Merchant or SocialClass.Citizen) burghers += count;
                    else if (group.social_class == SocialClass.Labour) workers += count;
                    else if (group.social_class == SocialClass.Peasant) peasants += count;
                }
        }
        if (adults == 0) return default;
        float commerce = Mathf.Clamp01(burghers / (adults * 0.3f));
        float housing = UrbanCitizenSystem.GetCapacity(city, adults) > 0 ? 1f : 0f;
        float labour = Mathf.Clamp01(workers / (adults * 0.3f));
        float factories = UrbanEmploymentSystem.CountFactories(city) > 0 ? 1f : 0f;
        float agrarian = Mathf.Clamp01(peasants / (adults * 0.5f));
        float landless = agrarian > 0f ? Mathf.Clamp01(LandEconomySystem.GetLandlessRatio(city) * 2f) : 0f;
        // 土地收归国有(政体不允许地主阶层，如共和)：农民没有"无地"可言，但也不再是守着自家田地的小农——
        // 集体化的农民只保留三成"有地小农守传统"的倾向，否则国有化反而把整个农村推向保守主义
        Regimes.Regime regime = city.kingdom?.GetRegime();
        float settledFactor = regime != null && !Regimes.RegimeManager.AllowsLandlordClass(regime.type) ? 0.3f : 1f;
        return new CityEconomyProfile(0.7f * commerce + 0.3f * housing, 0.7f * labour + 0.3f * factories,
            agrarian * (1f - landless) * settledFactor, agrarian * landless);
    }




    // 按城市经济形态调整改信某理念的概率/强度
    private static float EconomyFactor(PartyIdeology ideology, CityEconomyProfile economy)
    {
        float factor = 1f;
        if (IdeologyFamilies.IsLiberal(ideology)) factor *= 1f + ProsperityLiberalBoost * economy.Prosperity;
        else if (IdeologyFamilies.IsTraditional(ideology)) factor *= 1f - ProsperityTraditionalDamp * economy.Prosperity;
        if (IdeologyFamilies.IsLabourMovement(ideology)) factor *= 1f + IndustrySocialistBoost * economy.Industry;
        if (IdeologyFamilies.IsAgrarianTraditional(ideology)) factor *= 1f + AgrarianTraditionalBoost * economy.Settled;
        if (IdeologyFamilies.IsAgrarianRadical(ideology)) factor *= 1f + AgrarianRadicalBoost * economy.Landless;
        return factor;
    }

    // 农村：有地小农倾向传统理念，无地农民倾向激进左翼(接触概率、改信加成)
    private const float AgrarianTraditionalContact = 0.15f;
    private const float AgrarianTraditionalBoost = 1f;
    private const float AgrarianRadicalContact = 0.2f;
    private const float AgrarianRadicalBoost = 1.5f;

    private const float IndustrySocialistContact = 0.2f;
    // 自由主义占优城市里接触各种主义的概率、改信非自由主义理念的加成(城内自由主义占比 50%→100% 线性增至满额)
    private const float PluralismContact = 0.25f;
    private const float PluralismBoost = 1f;
    private const float IndustrySocialistBoost = 1.5f;

    private static PartyIdeology SampleLocal(Dictionary<PartyIdeology, int> counts,
        Dictionary<PartyIdeology, int> organizers, SocialClass socialClass, City city, HashSet<PartyIdeology> available)
    {
        List<KeyValuePair<PartyIdeology, int>> candidates = counts
            .Where(pair => available.Contains(pair.Key)).ToList();
        if (candidates.Count == 0) return PickInitial(socialClass, city, city?.kingdom);
        float total = 0f;
        foreach (KeyValuePair<PartyIdeology, int> pair in candidates)
            total += pair.Value * (0.65f + 0.7f * Mathf.Clamp01((PartySystem.GetAffinity(pair.Key,
                socialClass) + 100f) / 200f)) +
                (organizers.TryGetValue(pair.Key, out int members) ? members * 2f : 0f);
        float roll = UnityEngine.Random.value * total;
        foreach (KeyValuePair<PartyIdeology, int> pair in candidates)
        {
            roll -= pair.Value * (0.65f + 0.7f * Mathf.Clamp01((PartySystem.GetAffinity(pair.Key,
                socialClass) + 100f) / 200f)) +
                (organizers.TryGetValue(pair.Key, out int members) ? members * 2f : 0f);
            if (roll <= 0f) return pair.Key;
        }
        return candidates[0].Key;
    }

    private static PartyIdeology PickInitial(Actor actor) =>
        PickInitial(actor.GetOrCreate().socialClass, actor.city, actor.kingdom);

    private static PartyIdeology PickInitial(SocialClass socialClass, City city, Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        string culture = city == null ? CultureService.GetRealmCulture(kingdom) : CultureService.GetMainCulture(city);
        PartyIdeology[] choices = GetAvailableIdeologies(culture, empire).ToArray();
        if (choices.Length == 0) return PartyIdeology.Conservatism;
        float[] weights = choices.Select(ideology =>
            Mathf.Max(1f, PartySystem.GetAffinity(ideology, socialClass) + 25f)).ToArray();
        float roll = UnityEngine.Random.value * weights.Sum();
        for (int index = 0; index < choices.Length; index++)
            if ((roll -= weights[index]) <= 0f) return choices[index];
        return choices[choices.Length - 1];
    }

    public static void RegisterIdea(string culture, PartyIdeology ideology)
    {
        if (!CultureService.IsValidCulture(culture) ||
            !TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology))) return;
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        if (state == null) return;
        state.discovered_ideologies ??= new List<string>();
        string key = ideology.ToString();
        if (!state.discovered_ideologies.Contains(key)) state.discovered_ideologies.Add(key);
    }

    public static bool IsIdeaAvailable(string culture, PartyIdeology ideology) =>
        CultureService.IsValidCulture(culture) &&
        TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology)) &&
        (PartySystem.IsResearched(culture, ideology) ||
         InstitutionSystem.GetOrCreateCultureState(culture)?.discovered_ideologies?.Contains(ideology.ToString()) == true ||
         LandmarkBookSystem.FoundingBook(ideology) != null && LandmarkBookSystem.HasFoundingBook(culture, ideology));

    internal static HashSet<PartyIdeology> GetAvailableIdeologies(string culture, Empire empire,
        bool includeEmerging = false)
    {
        var choices = new HashSet<PartyIdeology>();
        if (CultureService.IsValidCulture(culture) &&
            InstitutionSystem.GetFeature(culture, PartySystem.FeaturePartyPolitics) > 0f)
        {
            bool emergence = includeEmerging || IdeologyDynamicsSystem.GetLiberation(empire) > 0f;
            foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)))
                if ((emergence || IsIdeaAvailable(culture, ideology)) &&
                    TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology)))
                    choices.Add(ideology);
        }
        if (choices.Count == 0)
            foreach (PartyIdeology ideology in PartySystem.BaseIdeologies) choices.Add(ideology);
        return choices;
    }

    // 解锁节点只给尚无人信奉的新思想补充传播起点，不再次改写已经出现过的思想分布。
    private static void EnsureUnlockedIdeologiesSeeded()
    {
        foreach (IGrouping<string, City> cultureCities in World.world.cities
                     .Where(city => city != null && !city.isRekt() && city.units != null)
                     .GroupBy(city => CultureService.GetMainCulture(city))
                     .Where(group => CultureService.IsValidCulture(group.Key)))
        {
            string culture = cultureCities.Key;
            if (InstitutionSystem.GetFeature(culture, PartySystem.FeaturePartyPolitics) <= 0f) continue;
            CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
            state.seeded_ideologies ??= new List<string>();
            List<PartyIdeology> pending = Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
                .Where(ideology => PartySystem.IsResearched(culture, ideology) &&
                    TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology)) &&
                    !state.seeded_ideologies.Contains(ideology.ToString())).ToList();
            if (pending.Count == 0) continue;
            List<Actor> adults = cultureCities.SelectMany(city => city.units)
                .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult())
                .Distinct().ToList();
            if (adults.Count == 0) continue; // 暂时没有读者时，下次年度检查继续尝试。
            HashSet<PartyIdeology> present = new(adults.Select(Get));
            List<PartyIdeology> missing = pending.Where(ideology => !present.Contains(ideology)).ToList();
            List<Actor> candidates = adults.Where(actor => !actor.isKing() && !actor.isCityLeader() &&
                actor.GetFaction()?.IsParty != true).ToList();
            int quota = missing.Count == 0 ? 0 : Mathf.Min(candidates.Count,
                Mathf.Max(missing.Count, Mathf.RoundToInt(candidates.Count * 0.4f)));
            List<PartyIdeology> unseeded = new(missing);
            foreach (Actor actor in candidates.OrderBy(_ => UnityEngine.Random.value).Take(quota))
            {
                PartyIdeology target = WeightedChoice(unseeded.Count > 0 ? unseeded : missing, actor, null);
                Set(actor, target);
                unseeded.Remove(target);
                present.Add(target);
                RegisterIdea(culture, target);
            }
            foreach (PartyIdeology ideology in pending.Where(present.Contains))
                state.seeded_ideologies.Add(ideology.ToString());
        }
        DominantCache.Clear();
    }

    private static PartyIdeology PickClassResponse(SocialClass socialClass, Kingdom kingdom,
        HashSet<PartyIdeology> available, float grievance)
    {
        bool revolutionary = kingdom?.GetOrCreate().is_peasant_revolutionary_government == true;
        return WeightedChoice(available.ToList(), socialClass, ideology =>
        {
            float multiplier = 1f;
            if (socialClass is SocialClass.Peasant or SocialClass.Labour && IdeologyFamilies.IsLeft(ideology))
                multiplier += 1.5f * grievance;
            if (socialClass == SocialClass.Citizen && ideology is PartyIdeology.SocialLiberalism or
                    PartyIdeology.Centrism or PartyIdeology.ConservativeLiberalism or PartyIdeology.Libertarianism)
                multiplier += 0.8f;
            if (revolutionary && socialClass is SocialClass.Peasant or SocialClass.Labour && IdeologyFamilies.IsLeft(ideology))
                multiplier += 2.5f;
            return multiplier;
        });
    }

    internal static PartyIdeology WeightedChoice(IList<PartyIdeology> choices, Actor actor,
        Func<PartyIdeology, float> multiplier) =>
        WeightedChoice(choices, actor.GetOrCreate().socialClass, multiplier);

    internal static PartyIdeology WeightedChoice(IList<PartyIdeology> choices, SocialClass socialClass,
        Func<PartyIdeology, float> multiplier)
    {
        if (choices == null || choices.Count == 0) return PartyIdeology.Conservatism;
        float total = choices.Sum(ideology => Math.Max(1f,
            PartySystem.GetAffinity(ideology, socialClass) + 35f) *
            Math.Max(0f, multiplier?.Invoke(ideology) ?? 1f));
        float roll = UnityEngine.Random.value * total;
        foreach (PartyIdeology ideology in choices)
        {
            roll -= Math.Max(1f, PartySystem.GetAffinity(ideology, socialClass) + 35f) *
                    Math.Max(0f, multiplier?.Invoke(ideology) ?? 1f);
            if (roll <= 0f) return ideology;
        }
        return choices[choices.Count - 1];
    }

    // 与 Introduce 相同的改信概率，按期望值作用在背景人口上(无小人模式)
    private static int IntroduceToBackground(City city, PartyIdeology ideology, float intensity) =>
        CityPopulationSystem.ConvertBackground(city, ideology, socialClass =>
            Mathf.Clamp01(intensity) * Mathf.Clamp01((PartySystem.GetAffinity(ideology, socialClass) + 100f) / 200f));

    private static int Introduce(IEnumerable<Actor> actors, PartyIdeology ideology, float intensity)
    {
        int changed = 0;
        foreach (Actor actor in actors ?? Enumerable.Empty<Actor>())
        {
            if (actor == null || actor.isRekt() || !actor.isAlive() || !actor.isAdult() || Get(actor) == ideology)
                continue;
            float affinity = PartySystem.GetAffinity(ideology, actor.GetOrCreate().socialClass);
            if (UnityEngine.Random.value >= Mathf.Clamp01(intensity) *
                Mathf.Clamp01((affinity + 100f) / 200f)) continue;
            Set(actor, ideology);
            changed++;
        }
        return changed;
    }


    public static int TriggerLandRevolution(Kingdom kingdom)
    {
        if (kingdom?.units == null) return 0;
        string culture = CultureService.GetRealmCulture(kingdom);
        PartyIdeology[] priority =
        {
            PartyIdeology.Communism, PartyIdeology.Socialism,
            PartyIdeology.Anarchism, PartyIdeology.SocialDemocracy
        };
        PartyIdeology? ideology = null;
        foreach (PartyIdeology candidate in priority)
        {
            if (!PartySystem.IsResearched(culture, candidate) ||
                !TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(candidate))) continue;
            ideology = candidate;
            break;
        }
        if (!ideology.HasValue) return 0;
        int changed = Introduce(kingdom.units, ideology.Value, 0.55f);
        foreach (City city in kingdom.cities ?? Enumerable.Empty<City>()) DominantCache.Remove(city);
        return changed;
    }
}
