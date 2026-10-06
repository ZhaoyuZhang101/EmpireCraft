using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// Legacy save compatibility for the old four-route selector. Governments now follow
// the ideology of their governing party; these groups no longer choose a regime.
public enum IdeologyRoute
{
    Conservative, // 保守：保守主义 → 教派民主主义
    Liberal,      // 自由：保守自由主义 → 资本主义 → 自由意志主义
    Center,       // 中间：社会自由主义 / 中间派 → 权威主义 → 法西斯主义
    Left          // 左翼：社会民主主义 / 社会主义 → 共产主义 / 无政府主义
}

// 政党意识形态，按政治光谱(经济 左—右 × 权力 自由—极权)排布
public enum PartyIdeology
{
    Anarchism,              // 无政府主义        左·自由
    SocialDemocracy,        // 社会民主主义      中左·自由
    Libertarianism,         // 自由意志主义      中·极自由
    SocialLiberalism,       // 社会自由主义      中·自由
    Capitalism,             // 资本主义          右·自由
    ConservativeLiberalism, // 保守自由主义      中右·自由
    Centrism,               // 中间派
    Socialism,              // 社会主义          中左·威权
    Communism,              // 共产主义          极左·极权
    ReligiousDemocracy,     // 教派民主主义      中·威权
    Authoritarianism,       // 权威主义          中·极权
    Conservatism,           // 保守主义          中右·威权
    Fascism                 // 法西斯主义        极右·极权
}

// 政党。开放党禁(制度特性 party_politics)且议会已召开后：
//   · 改组：朝中每个未被取缔的派系改组为政党，原班人马全部转入，按派系原来的阶层倾向落到光谱上最近的理念；
//   · 政党仍是 FixedFaction(IsParty=true)：派系诉求、制度立场、中央占比、议席、组阁这些沿用派系的机制照常运转，
//     只是诉求清单换成本理念的诉求，阶层倾向由理念在光谱上的位置决定；
//   · 选举：议席按选票分配，选票 = 有选举权的各阶层人口 × 各党理念与该阶层立场的接近程度；
//     未推行普选(制度特性 universal_suffrage)前只有地主、商人、贵族、官僚有选举权；选后中央占比按议席比例重算；
//   · 每年：有声望的人可以为没有政党代表的理念建党；党员会流向离自己阶层立场更近的党；
//     同理念或光谱上很近的小党合并；大党内部理念分化时分裂；连续两届没有议席的党解散。
// 党名从文化配置 setting.Party.groups[理念] 指向的词库随机取；没配就用 Locales/Cultures/PartyNames/Party<理念>.csv。
public static class PartySystem
{
    public const string FeaturePartyPolitics = "party_politics";
    public const string FeatureUniversalSuffrage = "universal_suffrage";

    private const int MaxParties = 7;
    private const float FoundChance = 0.65f;
    private const int FoundRecruits = 6;
    private const float SwitchMargin = 25f;
    private const int MaxSwitchesPerYear = 5;
    private const float SwitchChance = 0.35f;
    // 弱势政党：议席与得票都不到一成二；理念相距 70 以内(如社会自由与保守自由、社会主义与共产主义)算相近
    private const float MergeSeatShare = 0.12f;
    private const float MergeDistance = 70f;
    private const float MergeBaseChance = 0.3f;
    private const float SplitSeatShare = 0.4f;
    private const float SplitChance = 0.1f;
    private const float SplitDistance = 60f;
    private const int DissolveAfterZeroSeatElections = 2;
    private const int MinimumParties = 2;
    private const int MinYearsBeforeMerge = 3;
    private const float OrganisationVotesPerMember = 0.3f;

    // 光谱坐标：x 经济(-100 左 ~ 100 右)，y 权力(100 自由 ~ -100 极权)
    private static readonly Dictionary<PartyIdeology, Vector2> IdeologyPositions = new()
    {
        { PartyIdeology.Anarchism, new Vector2(-80f, 85f) },
        { PartyIdeology.SocialDemocracy, new Vector2(-50f, 40f) },
        { PartyIdeology.Libertarianism, new Vector2(0f, 80f) },
        { PartyIdeology.SocialLiberalism, new Vector2(0f, 40f) },
        { PartyIdeology.Capitalism, new Vector2(75f, 70f) },
        { PartyIdeology.ConservativeLiberalism, new Vector2(50f, 35f) },
        { PartyIdeology.Centrism, new Vector2(0f, 0f) },
        { PartyIdeology.Socialism, new Vector2(-50f, -35f) },
        { PartyIdeology.Communism, new Vector2(-85f, -75f) },
        { PartyIdeology.ReligiousDemocracy, new Vector2(0f, -30f) },
        { PartyIdeology.Authoritarianism, new Vector2(0f, -70f) },
        { PartyIdeology.Conservatism, new Vector2(45f, -30f) },
        { PartyIdeology.Fascism, new Vector2(85f, -80f) }
    };

    // 各阶层在光谱上的立场
    private static readonly Dictionary<SocialClass, Vector2> ClassPositions = new()
    {
        { SocialClass.Labour, new Vector2(-70f, 20f) },
        { SocialClass.Peasant, new Vector2(-45f, -10f) },
        { SocialClass.Merchant, new Vector2(55f, 60f) },
        { SocialClass.Landlord, new Vector2(60f, -20f) },
        { SocialClass.Noble, new Vector2(50f, -55f) },
        { SocialClass.Army, new Vector2(20f, -65f) },
        { SocialClass.Officer, new Vector2(0f, -20f) },
        { SocialClass.Citizen, new Vector2(0f, 25f) }
    };

    // 政党沿用派系类型(制度立场、立宪态度按类型算)，理念 → 最接近的派系类型
    private static readonly Dictionary<PartyIdeology, FactionType> IdeologyFactionTypes = new()
    {
        { PartyIdeology.Anarchism, FactionType.革命 },
        { PartyIdeology.SocialDemocracy, FactionType.民主 },
        { PartyIdeology.Libertarianism, FactionType.自治 },
        { PartyIdeology.SocialLiberalism, FactionType.民主 },
        { PartyIdeology.Capitalism, FactionType.共和 },
        { PartyIdeology.ConservativeLiberalism, FactionType.共和 },
        { PartyIdeology.Centrism, FactionType.绥靖 },
        { PartyIdeology.Socialism, FactionType.革命 },
        { PartyIdeology.Communism, FactionType.共产 },
        { PartyIdeology.ReligiousDemocracy, FactionType.神权 },
        { PartyIdeology.Authoritarianism, FactionType.中央 },
        { PartyIdeology.Conservatism, FactionType.尊王 },
        { PartyIdeology.Fascism, FactionType.攘夷 }
    };

    // 各理念的诉求：沿用原派系诉求，另加推行普选/土地改革
    private static readonly Dictionary<PartyIdeology, TemporaryFactionType[]> IdeologyClaims = new()
    {
        { PartyIdeology.Anarchism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.推行普选, TemporaryFactionType.自由信仰, TemporaryFactionType.降低赋税, TemporaryFactionType.渴望共和 } },
        { PartyIdeology.SocialDemocracy, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.提高福利, TemporaryFactionType.推行普选, TemporaryFactionType.开放移民 } },
        { PartyIdeology.Libertarianism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.降低赋税, TemporaryFactionType.自由信仰, TemporaryFactionType.开放移民 } },
        { PartyIdeology.SocialLiberalism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.推行普选, TemporaryFactionType.开放移民, TemporaryFactionType.自由信仰, TemporaryFactionType.开科取士 } },
        { PartyIdeology.Capitalism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.降低赋税, TemporaryFactionType.拓展金融霸权, TemporaryFactionType.开放移民 } },
        { PartyIdeology.ConservativeLiberalism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.降低赋税, TemporaryFactionType.拓展金融霸权, TemporaryFactionType.自由信仰 } },
        { PartyIdeology.Centrism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.推行普选, TemporaryFactionType.开科取士, TemporaryFactionType.设置行政区, TemporaryFactionType.谋求统一 } },
        { PartyIdeology.Socialism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.土地改革, TemporaryFactionType.提高福利, TemporaryFactionType.提高赋税, TemporaryFactionType.推行普选 } },
        { PartyIdeology.Communism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.土地改革, TemporaryFactionType.输出革命, TemporaryFactionType.扶持革命党, TemporaryFactionType.禁党, TemporaryFactionType.提高福利 } },
        { PartyIdeology.ReligiousDemocracy, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.确立国教, TemporaryFactionType.提高福利, TemporaryFactionType.神授君权 } },
        { PartyIdeology.Authoritarianism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.削藩, TemporaryFactionType.夺取诸侯开战权, TemporaryFactionType.颁布帝国治安令, TemporaryFactionType.谋求统一 } },
        { PartyIdeology.Conservatism, new[] { TemporaryFactionType.复辟帝制, TemporaryFactionType.恢复世袭皇权, TemporaryFactionType.确立国教, TemporaryFactionType.清除移民 } },
        { PartyIdeology.Fascism, new[] { TemporaryFactionType.建立共和, TemporaryFactionType.对外扩张, TemporaryFactionType.文化同化, TemporaryFactionType.迫使朝贡, TemporaryFactionType.清除移民, TemporaryFactionType.禁党 } }
    };

    private static readonly SocialClass[] RestrictedFranchise =
        { SocialClass.Landlord, SocialClass.Merchant, SocialClass.Noble, SocialClass.Officer, SocialClass.Citizen };

    #region 查询

    // 开放党禁 + 本文化至少解锁了一种理念(理念由制度树"意识形态"车道的节点解锁)。
    // 不必等议会：没有议会时政党像派系一样按中央占比在朝中博弈，开了议会(立宪/共和)才按选举分议席。
    public static bool IsActive(Empire empire) =>
        !TechnologySystem.PremodernLocked &&
        empire?.CoreKingdom != null && InstitutionSystem.GetFeature(empire, FeaturePartyPolitics) > 0f &&
        UnlockedIdeologies(empire).Count > 0;

    // 民间已接触到理念就能组织政党；理念制度由执政党推进，不能反过来作为建党的前提。
    public static bool IsIdeologyUnlocked(Empire empire, PartyIdeology ideology)
    {
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        return IdeologyPopulationSystem.IsIdeaAvailable(culture, ideology);
    }

    #region 理念路线

    private static readonly Dictionary<PartyIdeology, IdeologyRoute> Routes = new()
    {
        { PartyIdeology.Conservatism, IdeologyRoute.Conservative },
        { PartyIdeology.ReligiousDemocracy, IdeologyRoute.Conservative },
        { PartyIdeology.ConservativeLiberalism, IdeologyRoute.Liberal },
        { PartyIdeology.Capitalism, IdeologyRoute.Liberal },
        { PartyIdeology.Libertarianism, IdeologyRoute.Liberal },
        { PartyIdeology.SocialLiberalism, IdeologyRoute.Center },
        { PartyIdeology.Centrism, IdeologyRoute.Center },
        { PartyIdeology.Authoritarianism, IdeologyRoute.Center },
        { PartyIdeology.Fascism, IdeologyRoute.Center },
        { PartyIdeology.SocialDemocracy, IdeologyRoute.Left },
        { PartyIdeology.Socialism, IdeologyRoute.Left },
        { PartyIdeology.Communism, IdeologyRoute.Left },
        { PartyIdeology.Anarchism, IdeologyRoute.Left }
    };

    public static IdeologyRoute RouteOf(PartyIdeology ideology) => Routes[ideology];

    // 理念在光谱上的坐标：x 经济(-100 左 ~ 100 右)，y 权力(100 自由 ~ -100 极权)
    public static Vector2 GetPosition(PartyIdeology ideology) => IdeologyPositions[ideology];

    public static string GetRouteName(IdeologyRoute route) => LM.Get($"ideology_route_{route}");

    // 开放党禁本身就带来最基本的几种理念(保守、保守自由、社会自由、中间)，党禁一开就能组党；
    // 其余理念通过名著、外来传播或思想爆发进入民间；对应制度仍由执政党推动。
    public static readonly HashSet<PartyIdeology> BaseIdeologies = new()
    {
        PartyIdeology.Conservatism, PartyIdeology.ConservativeLiberalism,
        PartyIdeology.SocialLiberalism, PartyIdeology.Centrism
    };

    public static bool IsResearched(string culture, PartyIdeology ideology) =>
        InstitutionSystem.GetFeature(culture, IdeologySpreadSystem.FeatureKey(ideology)) > 0f ||
        BaseIdeologies.Contains(ideology) && InstitutionSystem.GetFeature(culture, FeaturePartyPolitics) > 0f;

    public static bool HasResearchedRoute(string culture, IdeologyRoute route) =>
        Routes.Any(pair => pair.Value == route && IsResearched(culture, pair.Key));

    // 未手动选路线时优先让开放党禁后的改革派可用；保守路线仍可手动选择。
    public static IdeologyRoute? GetActiveRoute(string culture)
    {
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        if (state == null) return null;
        if (Enum.TryParse(state.active_ideology_route, out IdeologyRoute active)) return active;
        if (HasResearchedRoute(culture, IdeologyRoute.Liberal))
        {
            state.active_ideology_route = IdeologyRoute.Liberal.ToString();
            return IdeologyRoute.Liberal;
        }
        foreach (IdeologyRoute route in Enum.GetValues(typeof(IdeologyRoute)).Cast<IdeologyRoute>())
        {
            if (!HasResearchedRoute(culture, route)) continue;
            state.active_ideology_route = route.ToString();
            return route;
        }
        return null;
    }

    // The old route button must not rewrite parties or change a government by fiat.
    public static bool SwitchRoute(string culture, IdeologyRoute route)
    {
        return false;
    }

    #endregion

    public static HashSet<PartyIdeology> UnlockedIdeologies(Empire empire) =>
        new(Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
            .Where(ideology => IsIdeologyUnlocked(empire, ideology)));

    // 实际是否普选：宪法手定了选举权时按宪法(普选仍需本文化推行过普选制度)，否则按制度
    public static bool HasUniversalSuffrage(Empire empire) =>
        ConstitutionSystem.TryGetLockedSuffrage(empire, out Data.ConstitutionSuffrage suffrage)
            ? suffrage == Data.ConstitutionSuffrage.Universal && HasUniversalSuffragePolicy(empire)
            : HasUniversalSuffragePolicy(empire);

    // 本文化是否推行过普选制度(宪法能否规定普选的前提)
    public static bool HasUniversalSuffragePolicy(Empire empire) =>
        InstitutionSystem.GetFeature(empire, FeatureUniversalSuffrage) > 0f;

    public static string GetIdeologyName(PartyIdeology ideology) => LM.Get($"party_ideology_{ideology}");

    public static List<FixedFaction> GetParties(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.Where(faction => faction != null && faction.IsParty && !faction.Ban).ToList() ?? new List<FixedFaction>();

    public static FixedFaction GetGovernmentParty(Empire empire)
    {
        if (empire?.CoreKingdom == null) return null;
        string onePartyId = empire.data?.constitutional_economy?.one_party_id;
        if (!string.IsNullOrEmpty(onePartyId))
        {
            FixedFaction ruling = GetParties(empire).FirstOrDefault(candidate => candidate.GetID() == onePartyId);
            if (ruling != null) return ruling;
        }
        string primeMinisterParty = ParliamentSystem.HasParliament(empire)
            ? empire.data?.constitutional_economy?.prime_minister_faction_id : null;
        FixedFaction party = GetParties(empire).FirstOrDefault(candidate => candidate.GetID() == primeMinisterParty);
        if (party != null) return party;
        party = empire.CoreKingdom.GetRegime()?.GetDominateFaction();
        return party?.IsParty == true && !party.Ban ? party : null;
    }

    // 理念对某阶层的吸引力(-100~100)：两者在光谱上越近越高
    public static float GetAffinity(PartyIdeology ideology, SocialClass socialClass) =>
        Mathf.Clamp(100f - Vector2.Distance(IdeologyPositions[ideology], ClassPositions[socialClass]) * 0.9f,
            -100f, 100f);

    public static float IdeologyDistance(PartyIdeology a, PartyIdeology b) => Distance(a, b);

    private static float Distance(PartyIdeology a, PartyIdeology b) =>
        Vector2.Distance(IdeologyPositions[a], IdeologyPositions[b]);

    private static PartyIdeology NearestIdeology(Vector2 position, Func<PartyIdeology, bool> filter = null) =>
        IdeologyPositions.Where(pair => filter == null || filter(pair.Key))
            .OrderBy(pair => Vector2.Distance(pair.Value, position)).First().Key;

    private static SocialClass ClassOf(Actor actor) => actor.GetOrCreate().socialClass;

    private static float MemberAffinity(Actor actor, FixedFaction party) =>
        (100f - Distance(IdeologyPopulationSystem.Get(actor), party.Ideology) * 0.9f) * 0.75f +
        GetAffinity(party.Ideology, ClassOf(actor)) * 0.25f;

    #endregion

    #region 年度更新(由 ParliamentSystem.Update 在议会存续时调用)

    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (!IsActive(empire) || World.world == null) return;
        Regime regime = empire.CoreKingdom.GetRegime();
        // 旧存档：以前政党不进存档，读档后退回了模板派系，却还标着"已改组"——重新改组一次
        if (state.parties_reorganized && !regime.GetPlayerFactions().Any(faction => faction != null && faction.IsParty))
            state.parties_reorganized = false;
        if (!state.parties_reorganized)
        {
            IdeologyPopulationSystem.SeedNewPartyPolitics(empire);
            Reorganize(empire, regime);
            state.parties_reorganized = true;
            RepublicSystem.ApplyFoundingPartyRule(empire);
            state.last_party_update = World.world.getCurWorldTime();
            return;
        }
        if (RepublicSystem.IsTransitioning(empire) || RepublicSystem.HasActiveRevolutionWar(empire)) return;
        if (state.last_party_update >= 0d && Date.getYearsSince(state.last_party_update) < 1) return;
        state.last_party_update = World.world.getCurWorldTime();

        // 一党制：不再有建党、流动、分合，只看执政党领袖有没有换人
        if (RepublicSystem.IsOneParty(empire))
        {
            // 统一战线：政协友党占比超过上限的(含旧存档)压回去，超出部分归领导党
            empire.CoreKingdom.ClampFactionRatio();
            RepublicSystem.UpdateHeadOfState(empire);
            return;
        }
        List<Actor> citizens = GetCitizens(empire);
        TryMerge(empire, regime, state);
        TryDissolveDefunct(empire, state);
        TryFoundParty(empire, regime, citizens);
        DriftMembers(empire, citizens);
        TrySplit(empire, regime, state);
        if (!RepublicSystem.TryMassPoliticsTransition(empire))
            RepublicSystem.TryRegionalRepublicCoalition(empire);
    }

    // 开放党禁：朝中派系改组为政党，原班人马随之转入
    private static void Reorganize(Empire empire, Regime regime)
    {
        var lines = new List<string>();
        Dictionary<PartyIdeology, int> population = IdeologyPopulationSystem.GetEmpireCounts(empire);
        int totalPopulation = population.Values.Sum();
        HashSet<PartyIdeology> supported = new(population
            .Where(pair => totalPopulation > 0 &&
                           pair.Value >= totalPopulation * IdeologyPopulationSystem.PartyFoundingShare)
            .Select(pair => pair.Key));
        HashSet<PartyIdeology> available = UnlockedIdeologies(empire);
        if (!available.Overlaps(supported))
            supported.Add(population.Where(pair => available.Contains(pair.Key))
                .OrderByDescending(pair => pair.Value).Select(pair => pair.Key)
                .DefaultIfEmpty(PartyIdeology.Conservatism).First());
        foreach (FixedFaction faction in regime.GetPlayerFactions().Where(faction => faction != null && !faction.Ban && !faction.IsParty).ToList())
        {
            FactionClassSystem.EnsureProfile(faction);
            // 派系原来偏向哪些阶层，就落到光谱上这些阶层立场的加权中心附近
            Vector2 weighted = Vector2.zero;
            float total = 0f;
            foreach (KeyValuePair<SocialClass, float> pair in faction.ClassAffinities.Where(pair => pair.Value > 0f))
            {
                weighted += ClassPositions[pair.Key] * pair.Value;
                total += pair.Value;
            }
            PartyIdeology ideology = NearestIdeology(total > 0f ? weighted / total : Vector2.zero,
                candidate => available.Contains(candidate) && supported.Contains(candidate));
            string originName = faction.Name;
            ConvertToParty(empire, faction, ideology, NewPartyName(empire, ideology));
            faction.OriginFactionName = originName;
            lines.Add(string.Format(LM.Get("party_reorganized_entry"), originName, faction.Name,
                GetIdeologyName(ideology)));
        }
        if (lines.Count == 0) return;
        Record(empire, string.Format(LM.Get("party_reorganized_history"), string.Join("、", lines)), null);
    }

    // 派系/政党代表的理念：政党就是它的理念；党禁前的派系按它偏向的阶层落到光谱上最近的理念(与开放党禁时改组的算法一致)
    public static PartyIdeology LeaningOf(FixedFaction faction)
    {
        if (faction == null) return PartyIdeology.Centrism;
        if (faction.IsParty) return faction.Ideology;
        if (faction.ClassAffinities == null || faction.ClassAffinities.Count == 0) FactionClassSystem.EnsureProfile(faction);
        Vector2 weighted = Vector2.zero;
        float total = 0f;
        foreach (KeyValuePair<SocialClass, float> pair in faction.ClassAffinities.Where(pair => pair.Value > 0f))
        {
            weighted += ClassPositions[pair.Key] * pair.Value;
            total += pair.Value;
        }
        return NearestIdeology(total > 0f ? weighted / total : Vector2.zero);
    }

    private static void ConvertToParty(Empire empire, FixedFaction faction, PartyIdeology ideology, string name)
    {
        faction.IsParty = true;
        faction.Ideology = ideology;
        faction.Name = name;
        faction.Type = IdeologyFactionTypes[ideology];
        faction.PartyFoundedAt = World.world.getCurWorldTime();
        faction.EmpireId = empire.getID();
        FactionClassSystem.EnsureProfile(faction);
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>())
            faction.ClassAffinities[socialClass] = GetAffinity(ideology, socialClass);
        // 诉求换成本理念的诉求
        faction.TemporaryFactionTypesRecord = IdeologyClaims[ideology].ToList();
        faction.TemporaryFactions = faction.ConvertToObjectFromFactionType();
        foreach (TemporaryFaction claim in faction.TemporaryFactions)
        {
            claim.Init(faction);
            claim.ShowAsPlot = false;
        }
        faction.FixMissedTemporaryFactions();
    }

    // A new party needs a real social base and an ideology already available to the culture.
    private static void TryFoundParty(Empire empire, Regime regime, List<Actor> citizens)
    {
        List<FixedFaction> parties = GetParties(empire);
        if (parties.Count >= MaxParties || Random.value > FoundChance) return;
        var represented = new HashSet<PartyIdeology>(parties.Select(party => party.Ideology));
        Dictionary<PartyIdeology, int> counts = IdeologyPopulationSystem.GetEmpireCounts(empire);
        int population = counts.Values.Sum();
        if (population == 0) return;
        Actor founder = citizens
            .Where(actor => actor.GetFaction()?.GetLeader() != actor)
            .Where(actor => !represented.Contains(IdeologyPopulationSystem.Get(actor)) &&
                            IsIdeologyUnlocked(empire, IdeologyPopulationSystem.Get(actor)) &&
                            counts.TryGetValue(IdeologyPopulationSystem.Get(actor), out int supporters) &&
                            supporters >= population * IdeologyPopulationSystem.PartyFoundingShare)
            .OrderByDescending(actor => actor.renown).FirstOrDefault();
        if (founder == null && CityPopulationSystem.AbstractPopulationEnabled)
        {
            // 无小人模式：取支持率达到组党门槛、还没有政党代表的理念里支持者最多的一个，从京师推举发起人
            PartyIdeology? candidate = counts
                .Where(pair => !represented.Contains(pair.Key) && IsIdeologyUnlocked(empire, pair.Key) &&
                               pair.Value >= population * IdeologyPopulationSystem.PartyFoundingShare)
                .OrderByDescending(pair => pair.Value).Select(pair => (PartyIdeology?)pair.Key).FirstOrDefault();
            if (candidate.HasValue) founder = SpawnFounder(empire, candidate.Value);
        }
        if (founder == null) return;
        PartyIdeology ideology = IdeologyPopulationSystem.Get(founder);
        FixedFaction party = CreateParty(empire, regime, ideology, founder);
        Recruit(party, citizens, FoundRecruits +
            (InstitutionSystem.GetFeature(InstitutionSystem.GetPrimaryCulture(empire),
                IdeologyInstitutionPaths.StageFeature(ideology, 2)) > 0f ? 2 : 0));
        Record(empire, string.Format(LM.Get("party_founded_history"), founder.getName(), party.Name,
            GetIdeologyName(ideology)), founder);
    }

    private static FixedFaction CreateParty(Empire empire, Regime regime, PartyIdeology ideology, Actor founder)
    {
        var party = new FixedFaction
        {
            _id = Guid.NewGuid().ToString(),
            EmpireId = empire.getID(),
            Members = new List<long>(),
            TemporaryFactions = new List<TemporaryFaction>(),
            TemporaryFactionTypesRecord = new List<TemporaryFactionType>(),
            ClaimCatalogVersion = FixedFaction.CurrentClaimCatalogVersion,
            PartyFounderId = founder?.id ?? -1L
        };
        ConvertToParty(empire, party, ideology, NewPartyName(empire, ideology));
        regime.GetPlayerFactions().Add(party);
        empire.CoreKingdom.ReconcileFactionRatios(regime.GetPlayerFactions());
        if (founder != null)
        {
            founder.SetFaction(party);
            party.SetLeader(founder);
        }
        return party;
    }

    // 把立场离新党明显更近的人拉过来
    private static void Recruit(FixedFaction party, List<Actor> citizens, int limit)
    {
        foreach (Actor actor in citizens.Where(actor => actor.GetFaction() != party)
                     .OrderByDescending(actor => actor.renown).ToList())
        {
            if (limit <= 0) break;
            FixedFaction current = actor.GetFaction();
            if (current != null && current.GetLeader() == actor) continue;
            float here = MemberAffinity(actor, party);
            float there = current?.IsParty == true ? MemberAffinity(actor, current) : -100f;
            if (here - there < SwitchMargin) continue;
            actor.SetFaction(party);
            limit--;
        }
    }

    // 党员流向离自己阶层立场更近的党(党魁不走)
    private static void DriftMembers(Empire empire, List<Actor> citizens)
    {
        List<FixedFaction> parties = GetParties(empire);
        if (parties.Count < 2) return;
        int switches = 0;
        foreach (Actor actor in citizens.OrderBy(_ => Random.value))
        {
            if (switches >= MaxSwitchesPerYear) break;
            FixedFaction current = actor.GetFaction();
            if (current == null || !current.IsParty || current.GetLeader() == actor) continue;
            FixedFaction best = parties.OrderByDescending(party => MemberAffinity(actor, party)).First();
            if (best == current || MemberAffinity(actor, best) - MemberAffinity(actor, current) < SwitchMargin)
                continue;
            if (Random.value > SwitchChance) continue;
            actor.SetFaction(best);
            switches++;
        }
    }

    // 同理念的党必定合并。弱势政党(议席与得票都不到一成二、成立满三年)每年有机会并入理念相近的较大政党：
    //   基础三成；得票比上届下滑 +两成；与对方同在执政联盟 +一成五；按行政区选举(小党被挤压) +一成；
    //   理念越远越难谈拢(每 10 点距离 -3%)。每年每国最多合并一次
    private static void TryMerge(Empire empire, Regime regime, ConstitutionalEconomyState state)
    {
        List<FixedFaction> parties = GetParties(empire);
        if (parties.Count <= MinimumParties) return;
        float ShareOf(FixedFaction party) => Share(empire, state, party);
        float VoteOf(FixedFaction party) =>
            state?.vote_shares != null && state.vote_shares.TryGetValue(party.GetID(), out float vote) ? vote : ShareOf(party);
        bool Declining(FixedFaction party) =>
            state?.previous_vote_shares != null &&
            state.previous_vote_shares.TryGetValue(party.GetID(), out float before) && VoteOf(party) < before - 0.01f;
        var coalition = new HashSet<string>(ParliamentSystem.GetCoalitionIds(empire));
        bool districts = HasUniversalSuffrage(empire);

        foreach (FixedFaction small in parties.OrderBy(ShareOf).ThenBy(party => party.Count))
        {
            if (PartyBanSystem.IsLeadingParty(empire, small)) continue;
            // 小党至少要存在几年才会被并掉，免得刚成立就被吞
            bool weak = ShareOf(small) < MergeSeatShare && VoteOf(small) < MergeSeatShare &&
                        small.PartyFoundedAt >= 0d && Date.getYearsSince(small.PartyFoundedAt) >= MinYearsBeforeMerge;
            FixedFaction target = parties
                .Where(other => other != small && !other.Ban &&
                                (ShareOf(other) > ShareOf(small) ||
                                 ShareOf(other) == ShareOf(small) && other.Count > small.Count))
                .Where(other => other.Ideology == small.Ideology ||
                                weak && Distance(other.Ideology, small.Ideology) <= MergeDistance)
                .OrderBy(other => Distance(other.Ideology, small.Ideology))
                .ThenByDescending(ShareOf).FirstOrDefault();
            if (target == null) continue;
            if (target.Ideology != small.Ideology)
            {
                float chance = MergeBaseChance + (Declining(small) ? 0.2f : 0f) +
                               (coalition.Contains(small.GetID()) && coalition.Contains(target.GetID()) ? 0.15f : 0f) +
                               (districts ? 0.1f : 0f) - Distance(target.Ideology, small.Ideology) / 330f;
                if (Random.value >= chance) continue;
            }
            bool governing = ParliamentSystem.IsGoverningFaction(empire, small.GetID());
            Absorb(empire, regime, target, small);
            // 并入的党原有的议席随人一起归到新党名下
            foreach (ParliamentSeat seat in state.parliament_seats.Where(seat => seat.faction_id == small.GetID()))
                seat.faction_id = target.GetID();
            empire.CoreKingdom.ClampFactionRatio();
            if (governing) ParliamentSystem.ReelectGovernment(empire);
            Record(empire, string.Format(LM.Get("party_merged_history"), small.Name, target.Name), target.GetLeader());
            return;
        }
    }

    // 名存实亡的政党自行解散(党员各投立场最近的党，见 DissolveParty)：
    //   · 党员走光了：当即解散；
    //   · 成立满三年、党员不足三人，或得票跌到 3% 以下且没有议席：每年四成机会解散。
    // 执政一方不解散；全国至少保留 MinimumParties 个政党；每年每国最多解散一个
    private const int DefunctMembers = 3;
    private const float DefunctVoteShare = 0.03f;
    private const float DefunctDissolveChance = 0.4f;

    private static void TryDissolveDefunct(Empire empire, ConstitutionalEconomyState state)
    {
        List<FixedFaction> parties = GetParties(empire);
        if (parties.Count <= MinimumParties) return;
        foreach (FixedFaction party in parties.OrderBy(party => party.Count).ToList())
        {
            if (party == null || party.Ban || ParliamentSystem.IsGoverningFaction(empire, party.GetID()) ||
                PartyBanSystem.IsLeadingParty(empire, party)) continue;
            float members = OrganizationSize(empire, party);
            bool settled = party.PartyFoundedAt >= 0d && Date.getYearsSince(party.PartyFoundedAt) >= MinYearsBeforeMerge;
            int seats = state?.parliament_seats?.Count(seat => seat.faction_id == party.GetID()) ?? 0;
            float vote = state?.vote_shares != null && state.vote_shares.TryGetValue(party.GetID(), out float share)
                ? share
                : 1f;
            bool empty = members < 1f;
            bool defunct = settled && (members < DefunctMembers || seats == 0 && vote < DefunctVoteShare);
            if (!empty && (!defunct || Random.value >= DefunctDissolveChance)) continue;
            if (DissolveParty(empire, party)) return;
        }
    }

    private static void Absorb(Empire empire, Regime regime, FixedFaction target, FixedFaction absorbed)
    {
        foreach (Actor member in absorbed.AllMembers.Where(actor => actor != null && !actor.isRekt()).ToList())
            member.SetFaction(target);
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        if (ratios != null && ratios.TryGetValue(absorbed, out int ratio))
            ratios[target] = (ratios.TryGetValue(target, out int current) ? current : 0) + ratio;
        RemoveParty(empire, regime, absorbed, target);
    }

    // 大党里有声望的人立场与党的理念相去太远 → 率众出走另组新党
    private static void TrySplit(Empire empire, Regime regime, ConstitutionalEconomyState state)
    {
        List<FixedFaction> parties = GetParties(empire);
        if (parties.Count >= MaxParties || Random.value > SplitChance) return;
        FixedFaction big = parties.Where(party => Share(empire, state, party) >= SplitSeatShare)
            .OrderByDescending(party => Share(empire, state, party)).FirstOrDefault();
        if (big == null) return;
        Actor rebel = big.AllMembers
            .Where(actor => actor != null && !actor.isRekt() && actor != big.GetLeader())
            .Where(actor => IdeologyPopulationSystem.Get(actor) != big.Ideology &&
                            Distance(IdeologyPopulationSystem.Get(actor), big.Ideology) >= SplitDistance)
            .OrderByDescending(actor => actor.renown).FirstOrDefault();
        if (rebel == null) return;
        PartyIdeology ideology = IdeologyPopulationSystem.Get(rebel);
        if (parties.Any(party => party.Ideology == ideology)) return;
        if (IdeologyPopulationSystem.GetEmpireShare(empire, ideology) <
            IdeologyPopulationSystem.PartyFoundingShare) return;
        FixedFaction party = CreateParty(empire, regime, ideology, rebel);
        // 带走党内立场离新党更近的人
        foreach (Actor member in big.AllMembers.Where(actor => actor != null && !actor.isRekt() && actor != big.GetLeader()).ToList())
        {
            if (MemberAffinity(member, party) > MemberAffinity(member, big)) member.SetFaction(party);
        }
        // 出走的议员带着议席走(跨党)，不留给原党补选
        bool governing = ParliamentSystem.IsGoverningFaction(empire, big.GetID());
        foreach (ParliamentSeat seat in state.parliament_seats.Where(seat => seat.faction_id == big.GetID() && seat.actor_id > 0))
            if (World.world.units.get(seat.actor_id)?.GetFaction() == party) seat.faction_id = party.GetID();
        // 执政党分裂：重新确认政府(多数可能已经不保，需要重新组阁)
        if (governing) ParliamentSystem.ReelectGovernment(empire);
        Record(empire, string.Format(LM.Get("party_split_history"), rebel.getName(), big.Name, party.Name,
            GetIdeologyName(ideology)), rebel);
    }

    #endregion

    #region 玩家组党 / 解散(按现实逻辑：组党要有发起人和一批同道，解散后党员各投立场最近的党)

    private const int PlayerMaxParties = 10;

    // The founder must share the ideology of the prospective party.
    private static Actor FindFounder(Empire empire, PartyIdeology ideology) =>
        GetCitizens(empire)
            .Where(actor => actor.GetFaction()?.GetLeader() != actor &&
                            IdeologyPopulationSystem.Get(actor) == ideology)
            .OrderByDescending(actor => actor.renown).FirstOrDefault();

    public static bool CanPlayerFound(Empire empire, PartyIdeology ideology, out string reason)
    {
        reason = "";
        if (!IsActive(empire)) { reason = "party_found_inactive"; return false; }
        if (RepublicSystem.IsOneParty(empire)) { reason = "party_found_one_party"; return false; }
        if (!IsIdeologyUnlocked(empire, ideology)) { reason = "party_found_locked"; return false; }
        if (IdeologyPopulationSystem.GetEmpireShare(empire, ideology) < IdeologyPopulationSystem.PartyFoundingShare)
        { reason = "party_found_insufficient_support"; return false; }
        if (GetParties(empire).Count >= PlayerMaxParties) { reason = "party_found_too_many"; return false; }
        if (FindFounder(empire, ideology) == null && !CityPopulationSystem.AbstractPopulationEnabled)
        { reason = "party_found_no_founder"; return false; }
        return true;
    }

    public static FixedFaction PlayerFoundParty(Empire empire, PartyIdeology ideology)
    {
        if (!CanPlayerFound(empire, ideology, out _)) return null;
        Actor founder = FindFounder(empire, ideology) ?? SpawnFounder(empire, ideology);
        if (founder == null) return null;
        Regime regime = empire.CoreKingdom.GetRegime();
        FixedFaction party = CreateParty(empire, regime, ideology, founder);
        Recruit(party, GetCitizens(empire), FoundRecruits +
            (InstitutionSystem.GetFeature(InstitutionSystem.GetPrimaryCulture(empire),
                IdeologyInstitutionPaths.StageFeature(ideology, 2)) > 0f ? 2 : 0));
        Record(empire, string.Format(LM.Get("party_founded_history"), founder.getName(), party.Name,
            GetIdeologyName(ideology)), founder);
        return party;
    }

    // 理念相近的小党自由合并：双方都不超过两成(议席，没有议会看中央占比)、光谱距离在合并范围内
    private const float PlayerMergeMaxShare = 0.2f;

    public static FixedFaction FindMergePartner(Empire empire, FixedFaction party)
    {
        if (party == null || !party.IsParty || party.Ban || RepublicSystem.IsOneParty(empire)) return null;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        if (Share(empire, state, party) > PlayerMergeMaxShare) return null;
        return GetParties(empire)
            .Where(other => other != party && Distance(other.Ideology, party.Ideology) <= MergeDistance &&
                            Share(empire, state, other) <= PlayerMergeMaxShare)
            .OrderBy(other => Distance(other.Ideology, party.Ideology))
            .ThenByDescending(other => Share(empire, state, other)).FirstOrDefault();
    }

    // 合并：小的并进大的(沿用大党党名)，党员、议席、中央占比随之转过去；涉及执政党时重新组阁
    public static bool MergeParties(Empire empire, FixedFaction party)
    {
        FixedFaction partner = FindMergePartner(empire, party);
        if (partner == null) return false;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        bool partyLarger = Share(empire, state, party) > Share(empire, state, partner) ||
                           Share(empire, state, party) == Share(empire, state, partner) && party.Count >= partner.Count;
        FixedFaction target = partyLarger ? party : partner;
        FixedFaction absorbed = partyLarger ? partner : party;
        bool governing = state != null && (ParliamentSystem.IsGoverningFaction(empire, target.GetID()) ||
                                           ParliamentSystem.IsGoverningFaction(empire, absorbed.GetID()));
        string absorbedName = absorbed.Name;
        Absorb(empire, empire.CoreKingdom.GetRegime(), target, absorbed);
        if (state?.parliament_seats != null)
            foreach (ParliamentSeat seat in state.parliament_seats.Where(seat => seat.faction_id == absorbed.GetID()))
                seat.faction_id = target.GetID();
        empire.CoreKingdom.ClampFactionRatio();
        if (governing) ParliamentSystem.ReelectGovernment(empire);
        Record(empire, string.Format(LM.Get("party_merged_history"), absorbedName, target.Name), target.GetLeader());
        return true;
    }

    public static bool CanDissolve(Empire empire, FixedFaction party) =>
        party != null && party.IsParty && !party.Ban && GetParties(empire).Count(other => other != party) >= 1;

    // 自行解散：党员按阶层立场加入剩下的政党中最接近的一个(对所有党都没好感的就成无党派)，
    // 议员带着议席转党，原中央占比按转入人数分给接收的党；执政党解散则重新组阁
    public static bool DissolveParty(Empire empire, FixedFaction party)
    {
        if (!CanDissolve(empire, party)) return false;
        Regime regime = empire.CoreKingdom.GetRegime();
        List<FixedFaction> others = GetParties(empire).Where(other => other != party).ToList();
        var received = others.ToDictionary(other => other, _ => 0);
        var movedTo = new Dictionary<long, FixedFaction>();
        foreach (Actor member in party.AllMembers.Where(actor => actor != null && !actor.isRekt()).ToList())
        {
            SocialClass socialClass = ClassOf(member);
            FixedFaction best = others.OrderByDescending(other => other.ClassAffinities[socialClass]).First();
            if (best.ClassAffinities[socialClass] < 0f)
            {
                party.RemoveMember(member);
                member.RemoveFaction();
                continue;
            }
            member.SetFaction(best);
            received[best]++;
            movedTo[member.id] = best;
        }
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        bool wasGoverning = state != null && ParliamentSystem.IsGoverningFaction(empire, party.GetID());
        if (state?.parliament_seats != null)
            foreach (ParliamentSeat seat in state.parliament_seats.Where(seat => seat.faction_id == party.GetID()))
            {
                if (movedTo.TryGetValue(seat.actor_id, out FixedFaction target)) seat.faction_id = target.GetID();
                else seat.actor_id = -1L; // 成了无党派的议员：席位出缺，年度补选
            }
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        int total = received.Values.Sum();
        if (ratios != null && ratios.TryGetValue(party, out int ratio) && ratio > 0 && total > 0)
            foreach (KeyValuePair<FixedFaction, int> pair in received.Where(pair => pair.Value > 0))
                ratios[pair.Key] = (ratios.TryGetValue(pair.Key, out int current) ? current : 0) +
                                   Mathf.RoundToInt(ratio * (float)pair.Value / total);
        string partyName = party.Name;
        RemoveParty(empire, regime, party);
        empire.CoreKingdom.ClampFactionRatio();
        if (wasGoverning) ParliamentSystem.ReelectGovernment(empire);
        Record(empire, string.Format(LM.Get("party_self_dissolved_history"), partyName), null);
        return true;
    }

    #endregion

    #region 选举(由 ParliamentSystem 调用)

    // 各党得票：有选举权的各阶层人口按理念接近程度分票，再加一点党组织本身的动员力
    public static Dictionary<FixedFaction, float> CountVotes(Empire empire, List<FixedFaction> parties) =>
        ApplyOpinionSwing(empire, CountVotes(parties, GetCitizens(empire), HasUniversalSuffrage(empire),
            InstitutionSystem.GetPrimaryCulture(empire), CityPopulationSystem.CitiesOf(empire.kingdoms_list)));

    // 一个行政区里各党的得票(地方选举、省级政治倾向)：只算住在本省的选民
    public static Dictionary<FixedFaction, float> CountProvinceVotes(Empire empire, Kingdom province,
        List<FixedFaction> parties)
    {
        List<Actor> voters = province?.units?.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
                                                            actor.isAdult() && actor.id != empire.Emperor?.id)
            .ToList() ?? new List<Actor>();
        return ApplyOpinionSwing(empire, CountVotes(parties, voters, HasUniversalSuffrage(empire),
            InstitutionSystem.GetPrimaryCulture(empire), CityPopulationSystem.CitiesOf(new[] { province })));
    }

    // 民意影响选票(见 PublicOpinionSystem)：民意越差执政党越丢票、百姓想要的理念越得票；外国压力撑腰的理念也多拿票
    private static Dictionary<FixedFaction, float> ApplyOpinionSwing(Empire empire,
        Dictionary<FixedFaction, float> votes)
    {
        int level = PublicOpinionSystem.GetLevel(empire);
        FixedFaction governing = GetGovernmentParty(empire);
        bool hasPreferred = PublicOpinionSystem.TryGetPreferred(empire, out PartyIdeology preferred);
        foreach (FixedFaction party in votes.Keys.ToList())
        {
            float factor = 1f;
            if (party == governing) factor *= 1f - 0.1f * level;
            if (hasPreferred && party.Ideology == preferred) factor *= 1f + 0.1f * level;
            factor *= 1f + Mathf.Min(0.2f, PublicOpinionSystem.GetPressure(empire, party.Ideology) / 100f);
            // 民族情绪高涨时，民族主义色彩浓的政党多拿票(见 NationalSentimentSystem)
            factor *= NationalSentimentSystem.VoteFactor(empire, party.Ideology);
            votes[party] *= factor;
        }
        return votes;
    }

    private static Dictionary<FixedFaction, float> CountVotes(List<FixedFaction> parties, List<Actor> citizens,
        bool universal, string culture, IEnumerable<City> cities = null)
    {
        var votes = parties.ToDictionary(party => party, _ => 0f);
        if (parties.Count == 0) return votes;
        AddBackgroundVotes(votes, parties, cities, universal);
        foreach (Actor citizen in citizens)
        {
            if (!universal && !RestrictedFranchise.Contains(ClassOf(citizen))) continue;
            Dictionary<FixedFaction, float> weights = parties.ToDictionary(party => party,
                party => Mathf.Max(0f, MemberAffinity(citizen, party)) + 1f);
            float sum = weights.Values.Sum();
            foreach (KeyValuePair<FixedFaction, float> pair in weights)
                votes[pair.Key] += pair.Value / sum;
        }
        var voterIds = new HashSet<long>(citizens.Select(actor => actor.id));
        foreach (FixedFaction party in parties)
        {
            votes[party] += party.AllMembers.Count(actor => actor != null && voterIds.Contains(actor.id)) *
                            OrganisationVotesPerMember;
            if (InstitutionSystem.GetFeature(culture,
                    IdeologyInstitutionPaths.StageFeature(party.Ideology, 1)) > 0f)
                votes[party] *= IdeologyInstitutionPaths.GetProfile(party.Ideology).VoteMultiplier;
        }
        return votes;
    }

    // 无小人模式：背景人口也投票。每个人口组的成年人按本组的理念与阶层，用和实体选民一样的亲和度在各党间分票；
    // 限制选举时只有有选举权的阶层投票
    private const float BackgroundAdultShare = 0.7f;

    private static void AddBackgroundVotes(Dictionary<FixedFaction, float> votes, List<FixedFaction> parties,
        IEnumerable<City> cities, bool universal)
    {
        if (cities == null || !CityPopulationSystem.AbstractPopulationEnabled) return;
        var weights = new float[parties.Count];
        foreach (City city in cities)
        {
            Data.CityPopulationData data = CityPopulationSystem.Get(city);
            if (data?.groups == null) continue;
            foreach (Data.PopGroup group in data.groups)
            {
                float adults = group.Background * BackgroundAdultShare;
                if (adults <= 0f || !universal && !RestrictedFranchise.Contains(group.social_class)) continue;
                float sum = 0f;
                for (int i = 0; i < parties.Count; i++)
                {
                    FixedFaction party = parties[i];
                    float affinity = (100f - Distance(group.ideology, party.Ideology) * 0.9f) * 0.75f +
                                     GetAffinity(party.Ideology, group.social_class) * 0.25f;
                    weights[i] = Mathf.Max(0f, affinity) + 1f;
                    sum += weights[i];
                }
                for (int i = 0; i < parties.Count; i++) votes[parties[i]] += adults * weights[i] / sum;
            }
        }
    }

    // ---- 政党组织规模(无小人模式) ----
    // 实体党员很少(只有名人)，按实体党员算，政党会被误判"名存实亡"、代表大会席位近乎随机。
    // 组织规模 = 实体党员 + 背景人口中本党理念支持者户数的 VirtualMemberShare(同理念的几个党平分)
    private const float VirtualMemberShare = 0.05f;

    public static float OrganizationSize(Empire empire, FixedFaction party)
    {
        float members = party.AllMembers.Count(actor => actor != null && !actor.isRekt() && actor.isAlive());
        if (!CityPopulationSystem.AbstractPopulationEnabled || empire == null) return members;
        float supporters = 0f;
        foreach (City city in CityPopulationSystem.CitiesOf(empire.kingdoms_list))
        {
            Data.CityPopulationData data = CityPopulationSystem.Get(city);
            if (data?.groups == null) continue;
            float perSlot = CityPopulationSystem.PeoplePerSlot(city);
            foreach (Data.PopGroup group in data.groups)
                if (group.ideology == party.Ideology) supporters += group.Background * 0.7f / perSlot;
        }
        int sameIdeology = Math.Max(1, GetParties(empire).Count(other => other.Ideology == party.Ideology));
        return members + supporters * VirtualMemberShare / sameIdeology;
    }

    // 找不到实体发起人时(无小人模式)，从京师人口里推举一位该理念的读书人
    public static Actor SpawnFounder(Empire empire, PartyIdeology ideology)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return null;
        City capital = empire?.CoreKingdom?.capital;
        Actor founder = capital == null ? null : CityPopulationSystem.SpawnScholar(capital);
        if (founder != null) IdeologyPopulationSystem.Set(founder, ideology);
        return founder;
    }

    // 普选后按行政区选举：每个行政区(帝国内的一国)按人口分得议席(至少 1 席)，区内按得票用顿特法分给各党。
    // 返回每一席的 (政党, 行政区 id)。
    public static List<(FixedFaction party, long district)> AllocateDistrictSeats(Empire empire,
        List<FixedFaction> parties, int seats)
    {
        var result = new List<(FixedFaction, long)>();
        if (parties.Count == 0 || seats <= 0) return result;
        List<Actor> citizens = GetCitizens(empire);
        // 行政区人口：实体选民 + 背景人口(无小人模式)
        List<(Kingdom kingdom, List<Actor> voters, float population)> districts = empire.kingdoms_hashset
            .Where(kingdom => kingdom != null && !kingdom.isRekt())
            .Select(kingdom =>
            {
                List<Actor> voters = citizens.Where(actor => actor.kingdom == kingdom).ToList();
                float background = CityPopulationSystem.AbstractPopulationEnabled
                    ? CityPopulationSystem.CitiesOf(new[] { kingdom }).Sum(CityPopulationSystem.GetBackgroundTotal)
                    : 0f;
                return (kingdom, voters, population: voters.Count + background);
            })
            .Where(district => district.population > 0f)
            .OrderByDescending(district => district.population).ThenBy(district => district.kingdom.id).ToList();
        if (districts.Count == 0) return result;

        // 每区先给 1 席(区比席多时只给人口最多的那几个区)，剩下的按人口最大余额法分
        var districtSeats = districts.ToDictionary(district => district.kingdom, _ => 0);
        foreach (var district in districts.Take(seats)) districtSeats[district.kingdom] = 1;
        int remaining = seats - districtSeats.Values.Sum();
        if (remaining > 0)
        {
            float population = districts.Sum(district => district.population);
            var remainders = new List<(Kingdom kingdom, float remainder)>();
            int assigned = 0;
            foreach (var district in districts)
            {
                float quota = district.population / population * remaining;
                int whole = (int)Math.Floor(quota);
                districtSeats[district.kingdom] += whole;
                assigned += whole;
                remainders.Add((district.kingdom, quota - whole));
            }
            foreach (var item in remainders.OrderByDescending(item => item.remainder).Take(remaining - assigned))
                districtSeats[item.kingdom]++;
        }

        foreach (var district in districts)
        {
            int count = districtSeats[district.kingdom];
            if (count <= 0) continue;
            Dictionary<FixedFaction, float> votes = ApplyOpinionSwing(empire, CountVotes(parties, district.voters,
                true, InstitutionSystem.GetPrimaryCulture(empire), CityPopulationSystem.CitiesOf(new[] { district.kingdom })));
            // 顿特法：每次把一席给"得票 / (已得席位 + 1)"最大的党
            var won = parties.ToDictionary(party => party, _ => 0);
            for (int i = 0; i < count; i++)
            {
                FixedFaction winner = parties.OrderByDescending(party => votes[party] / (won[party] + 1))
                    .ThenBy(party => party.GetID()).First();
                won[winner]++;
                result.Add((winner, district.kingdom.id));
            }
        }
        return result;
    }

    // 改制后的国号后缀：本文化给这个理念配的后缀词库 → 通用词库
    public static string PickCountrySuffix(Empire empire, PartyIdeology ideology)
    {
        List<string> pool = CountrySuffixPool(InstitutionSystem.GetPrimaryCulture(empire), ideology);
        return pool.Count == 0 ? LM.Get("republic_default_suffix") : pool[Random.Range(0, pool.Count)];
    }

    // 国号后缀词库：本文化 Party.suffix_groups 配的词库(可写"别的文化:词库名") → PartyNames/Suffix<理念>.csv
    public static List<string> CountrySuffixPool(string culture, PartyIdeology ideology) =>
        GetCultureNamePool(culture, ideology, party => party.suffix_groups, "Suffix");

    // 选后：中央占比按议席比例重算；连续两届没有议席的党解散
    public static void AfterElection(Empire empire, Dictionary<FixedFaction, int> allocation, int totalSeats)
    {
        Regime regime = empire.CoreKingdom.GetRegime();
        List<FixedFaction> parties = GetParties(empire);
        Dictionary<FixedFaction, int> ratios = empire.CoreKingdom.GetOrCreate().FactionRatio;
        if (ratios != null && totalSeats > 0)
        {
            foreach (FixedFaction party in parties)
                ratios[party] = Mathf.RoundToInt((allocation.TryGetValue(party, out int seats) ? seats : 0) * 100f / totalSeats);
            empire.CoreKingdom.ClampFactionRatio();
        }
        foreach (FixedFaction party in parties)
        {
            party.ZeroSeatElections = allocation.TryGetValue(party, out int seats) && seats > 0 ? 0 : party.ZeroSeatElections + 1;
            if (party.ZeroSeatElections < DissolveAfterZeroSeatElections || GetParties(empire).Count <= MinimumParties ||
                PartyBanSystem.IsLeadingParty(empire, party)) continue;
            Record(empire, string.Format(LM.Get("party_dissolved_history"), party.Name), null);
            RemoveParty(empire, regime, party);
        }
    }

    #endregion

    #region 工具

    // 一党制下被解散的政党(见 PartyBanSystem.Close)：党员成为无党派，政党从格局中移除
    public static void DisbandParty(Empire empire, FixedFaction party)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null || party == null) return;
        RemoveParty(empire, regime, party);
    }

    // successor：并入的目标党(一党制下领导党被并入时由它接任领导党)
    private static void RemoveParty(Empire empire, Regime regime, FixedFaction party, FixedFaction successor = null)
    {
        foreach (long memberId in party.Members.ToList())
            World.world.units.get(memberId)?.RemoveFaction();
        party.Members.Clear();
        regime.GetPlayerFactions().Remove(party);
        empire.CoreKingdom.ReconcileFactionRatios(regime.GetPlayerFactions());
        PartyBanSystem.OnPartyRemoved(empire, party, successor);
    }

    private static List<Actor> GetCitizens(Empire empire) =>
        empire.getUnits().Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                         actor.id != empire.Emperor?.id).ToList();

    // 政党实力占比(0~1)：有议会按议席，没有议会按中央占比
    private static float Share(Empire empire, ConstitutionalEconomyState state, FixedFaction party)
    {
        int total = state?.parliament_seats?.Count ?? 0;
        if (ParliamentSystem.HasParliament(empire) && total > 0)
            return state.parliament_seats.Count(seat => seat.faction_id == party.GetID()) / (float)total;
        return party.CentralRatio / 100f;
    }

    private static void Record(Empire empire, string content, Actor actor)
    {
        empire.RecordHistory(directContent: content, actorId: actor?.id ?? empire.Emperor?.id ?? -1L,
            kingdomId: empire.CoreKingdom.id);
        actor?.RecordPersonalHistory(content, "party_politics");
        TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }

    // 党名：本文化给这个理念配的词库 → 通用词库 → 理念名+党；已被占用的名字不重复用
    private static string NewPartyName(Empire empire, PartyIdeology ideology)
    {
        var used = new HashSet<string>(empire.CoreKingdom.GetRegime().GetPlayerFactions()
            .Where(faction => faction != null).Select(faction => faction.Name));
        List<string> pool = GetNamePool(InstitutionSystem.GetPrimaryCulture(empire), ideology);
        List<string> free = pool.Where(name => !used.Contains(name)).ToList();
        if (free.Count > 0) return free[Random.Range(0, free.Count)];
        string baseName = pool.Count > 0 ? pool[Random.Range(0, pool.Count)]
            : string.Format(LM.Get("party_name_fallback"), GetIdeologyName(ideology));
        string name = baseName;
        for (int i = 2; used.Contains(name) && i < 100; i++)
            name = string.Format(LM.Get("party_name_duplicate"), baseName, i);
        return name;
    }

    private static List<string> GetNamePool(string culture, PartyIdeology ideology)
    {
        string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        if (!string.IsNullOrEmpty(culture) &&
            OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) &&
            setting?.Party?.groups != null &&
            setting.Party.groups.TryGetValue(ideology.ToString(), out string group) &&
            !string.IsNullOrWhiteSpace(group))
        {
            List<string> names = ReadNames(Path.Combine(root, $"Culture_{culture}", $"{culture}{group}.csv"));
            if (names.Count > 0) return names;
        }
        return ReadNames(Path.Combine(root, "PartyNames", $"Party{ideology}.csv"));
    }

    // 按文化取某类理念称呼的词库：本文化 Party 配置里 select 指向的词库(可写"别的文化:词库名"引用别的文化)，
    // 没配或词库为空时用 PartyNames/<fallbackPrefix><理念>.csv
    public static List<string> GetCultureNamePool(string culture, PartyIdeology ideology,
        Func<PartySetting, Dictionary<string, string>> select, string fallbackPrefix)
    {
        string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        List<string> pool = new List<string>();
        if (!string.IsNullOrEmpty(culture) &&
            OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) && setting?.Party != null &&
            select(setting.Party) is Dictionary<string, string> groups &&
            groups.TryGetValue(ideology.ToString(), out string group))
            pool = GetCultureWordPool(culture, group);
        if (pool.Count == 0) pool = ReadNames(Path.Combine(root, "PartyNames", $"{fallbackPrefix}{ideology}.csv"));
        return pool;
    }

    // 文化包里某个词库的全部词条：词库名写"别的文化:词库名"时读那个文化文件夹里的词库
    public static List<string> GetCultureWordPool(string culture, string group)
    {
        if (string.IsNullOrWhiteSpace(culture) || string.IsNullOrWhiteSpace(group)) return new List<string>();
        string owner = culture;
        int split = group.IndexOf(':');
        if (split > 0)
        {
            owner = group.Substring(0, split).Trim();
            group = group.Substring(split + 1).Trim();
        }
        string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
        return ReadNames(Path.Combine(root, $"Culture_{owner}", $"{owner}{group}.csv"));
    }

    private static List<string> ReadNames(string path)
    {
        if (!File.Exists(path)) return new List<string>();
        return (OnomasticsHelper.getKeysFromPath(path) ?? new List<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => LM.Get(key)).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
    }

    #endregion
}
