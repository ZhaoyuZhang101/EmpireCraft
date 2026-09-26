using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

public sealed class TradeDeliverySnapshot
{
    public City city;
    public long destination_city_id;
    public bool foreign_kingdom;
    public int gold_before;
    public double trade_timestamp;
}

public sealed class ConstitutionalEconomyView
{
    public int culture_years;
    public int merchant_households;
    public int total_households;
    public int merchant_cities;
    public int voyages;
    public int foreign_voyages;
    public int delivered_gold;
    public int budding_years;
    public bool budding;
    public bool constitutional;
    public bool reforming;
    public bool reform_stalled;
    public float reform_progress;
    public int reform_stage;
    public float parliamentary_support;
    public string blocker = "";
    public string missing_institutions = "";
}

// 资本主义萌芽与君主立宪。
//
// 本系统不认识任何具体的文明、科技线、节点或政体：
//   · 立宪需要的制度基础 = constitution.required_features（可按线覆盖），由节点 features 提供；
//   · 是否君主制 = 政体 SystemConfig.json 的 is_monarchy；
//   · 派系对立宪的态度、各项门槛 = Settings.json 的 constitution。
public static class ConstitutionalEconomySystem
{
    private static ConstitutionConfig Config => InstitutionDefinitionRegistry.Global.constitution;

    private static ConstitutionalEconomyState Ensure(Empire empire)
    {
        if (empire?.data == null) return null;
        empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        state.recent_trade ??= new List<CompletedTradeVoyage>();
        state.stable_culture ??= "";
        return state;
    }

    private static string GetLine(Empire empire) =>
        InstitutionSystem.GetCultureLine(InstitutionSystem.GetPrimaryCulture(empire));

    private static bool IsMonarchy(Empire empire) =>
        RegimeManager.IsMonarchy(empire?.CoreKingdom?.GetRegime()?.type);

    private static void Record(Empire empire, string key)
    {
        empire.RecordHistory(directContent: LM.Get(key),
            actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
    }

    #region 海贸记录

    public static void RecordArrival(Actor boat)
    {
        if (boat == null || boat.isRekt() || boat.asset?.is_boat != true || World.world == null) return;
        City origin = boat.getHomeBuilding()?.city;
        City destination = boat.beh_building_target?.city;
        if (origin == null || destination == null || origin == destination ||
            origin.isRekt() || destination.isRekt()) return;
        ActorExtension.ActorExtraData data = boat.GetOrCreate();
        data.pending_trade_origin_city_id = origin.id;
        data.pending_trade_destination_city_id = destination.id;
        data.pending_trade_timestamp = World.world.getCurWorldTime();
        data.pending_trade_foreign_kingdom = origin.kingdom != destination.kingdom;
    }

    public static TradeDeliverySnapshot BeforeUnload(Actor boat)
    {
        if (boat?.asset?.is_boat != true || World.world == null) return null;
        ActorExtension.ActorExtraData data = boat.GetOrCreate();
        if (data.pending_trade_origin_city_id < 0 || data.pending_trade_timestamp < 0) return null;
        City home = boat.getHomeBuilding()?.city;
        if (home == null || home.isRekt() || home.id != data.pending_trade_origin_city_id ||
            boat.city != home) return null;
        return new TradeDeliverySnapshot
        {
            city = home,
            destination_city_id = data.pending_trade_destination_city_id,
            foreign_kingdom = data.pending_trade_foreign_kingdom,
            gold_before = home.getResourcesAmount("gold"),
            trade_timestamp = data.pending_trade_timestamp
        };
    }

    public static void AfterUnload(Actor boat, TradeDeliverySnapshot delivery)
    {
        if (boat == null || delivery?.city == null || World.world == null) return;
        ActorExtension.ActorExtraData data = boat.GetOrCreate();
        if (data.pending_trade_origin_city_id != delivery.city.id ||
            data.pending_trade_destination_city_id != delivery.destination_city_id ||
            data.pending_trade_timestamp != delivery.trade_timestamp) return;
        ClearPendingTrade(data);
        if (boat.kingdom != delivery.city.kingdom) return;
        int gold = Math.Min(Config.max_gold_per_voyage,
            Math.Max(0, delivery.city.getResourcesAmount("gold") - delivery.gold_before));
        if (gold <= 0) return;
        Empire empire = delivery.city.kingdom?.GetEmpire();
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || empire.IsArchived() || empire.isRekt()) return;
        state.recent_trade.Add(new CompletedTradeVoyage
        {
            timestamp = World.world.getCurWorldTime(),
            origin_city_id = delivery.city.id,
            destination_city_id = delivery.destination_city_id,
            foreign_kingdom = delivery.foreign_kingdom,
            delivered_gold = gold
        });
        PruneTrade(state);
    }

    private static void ClearPendingTrade(ActorExtension.ActorExtraData data)
    {
        data.pending_trade_origin_city_id = -1L;
        data.pending_trade_destination_city_id = -1L;
        data.pending_trade_timestamp = -1d;
        data.pending_trade_foreign_kingdom = false;
    }

    private static bool IsRecent(CompletedTradeVoyage voyage) =>
        voyage != null && voyage.timestamp >= 0 && Date.getYearsSince(voyage.timestamp) < Config.trade_window_years;

    private static void PruneTrade(ConstitutionalEconomyState state)
    {
        state.recent_trade.RemoveAll(voyage => !IsRecent(voyage));
    }

    #endregion

    #region 年度更新（唯一会改动状态的入口）

    public static void Update(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || World.world == null ||
            empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return;
        ConstitutionalEconomyState state = Ensure(empire);
        SyncCulture(empire, state);
        SyncRegime(empire, state);
        // 议会召开/改选/补选/解散。每次更新都检查，议会阶段一到立即召开，不必等年度结算
        ParliamentSystem.Update(empire);
        if (state.last_economy_update >= 0 && Date.getYearsSince(state.last_economy_update) < 1) return;
        state.last_economy_update = World.world.getCurWorldTime();
        PruneTrade(state);
        UpdateBudding(empire, state);

        if (state.constitutional_reform_active) AdvanceReform(empire, state);
        if (state.constitutional_monarchy && GetParliamentarySupport(empire) < Config.support_threshold &&
            (state.last_deadlock_notice < 0 ||
             Date.getYearsSince(state.last_deadlock_notice) >= Config.deadlock_notice_years))
        {
            state.last_deadlock_notice = World.world.getCurWorldTime();
            Record(empire, "constitution_deadlock_history");
        }
        if (!state.constitutional_reform_active && !state.constitutional_monarchy &&
            InstitutionDefinitionRegistry.Global.ai_enabled &&
            (state.last_ai_constitution_attempt < 0 ||
             Date.getYearsSince(state.last_ai_constitution_attempt) >= Config.ai_attempt_interval_years))
        {
            state.last_ai_constitution_attempt = World.world.getCurWorldTime();
            if (GetParliamentarySupport(empire) >= Config.ai_start_support) StartReform(empire);
        }
    }

    private static void UpdateBudding(Empire empire, ConstitutionalEconomyState state)
    {
        ConstitutionConfig config = Config;
        CountHouseholds(empire, out int households, out int merchants, out int merchantCities);
        bool viable = merchants >= config.minimum_merchant_households && households > 0 &&
                      // 留一点余量，避免 0.05f 这类比例的浮点误差把恰好达标的情况判成不达标
                      merchants + 0.001f >= households * config.minimum_merchant_ratio &&
                      (state.recent_trade.Count > 0 ||
                       merchants >= config.no_trade_merchant_households &&
                       merchantCities >= config.no_trade_merchant_cities);
        if (viable)
        {
            state.capitalist_decline_since = -1d;
            if (state.capitalist_candidate_since < 0)
                state.capitalist_candidate_since = World.world.getCurWorldTime();
            if (!state.capitalist_budding &&
                Date.getYearsSince(state.capitalist_candidate_since) >= config.budding_years)
            {
                state.capitalist_budding = true;
                Record(empire, "constitution_budding_history");
            }
        }
        else
        {
            state.capitalist_candidate_since = -1d;
            if (state.capitalist_decline_since < 0)
                state.capitalist_decline_since = World.world.getCurWorldTime();
            if (Date.getYearsSince(state.capitalist_decline_since) >= config.budding_decline_years)
                state.capitalist_budding = false;
        }
    }

    // 主体文化变更（同化、归化、换朝）时由 CultureService 调用。走与年度同步相同的逻辑，
    // 所以进行中的立宪改革会被正确中止——而不是只把稳定计时器清零、让改革在新文化下继续。
    public static void ResetCultureStability(Empire empire)
    {
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || World.world == null || empire.CoreKingdom == null) return;
        SyncCulture(empire, state);
    }

    private static void SyncCulture(Empire empire, ConstitutionalEconomyState state)
    {
        string culture = CultureService.GetRealmCulture(empire.CoreKingdom);
        if (!CultureService.IsValidCulture(culture)) return;
        if (string.Equals(state.stable_culture, culture, StringComparison.Ordinal) &&
            state.stable_culture_since >= 0) return;
        // 第一次记录（新帝国/旧存档）不算"文化变更"
        bool changed = state.stable_culture_since >= 0;
        state.stable_culture = culture;
        state.stable_culture_since = World.world.getCurWorldTime();
        if (!changed || !state.constitutional_reform_active) return;
        CancelReform(state);
        // 议会随制宪一起中止；原内阁由内阁 AI 按原政体重新组建
        ParliamentSystem.Update(empire);
        Record(empire, "constitution_culture_changed_history");
    }

    private static void SyncRegime(Empire empire, ConstitutionalEconomyState state)
    {
        if (empire.CoreKingdom.GetRegime() == null || IsMonarchy(empire)) return;
        CancelReform(state);
        if (!state.constitutional_monarchy) return;
        state.constitutional_monarchy = false;
        Record(empire, "constitution_ended_history");
    }

    private static void CancelReform(ConstitutionalEconomyState state)
    {
        state.constitutional_reform_active = false;
        state.constitutional_reform_progress = 0f;
        state.constitutional_reform_stage = 0;
    }

    #endregion

    private static void CountHouseholds(Empire empire, out int households, out int merchants,
        out int merchantCities)
    {
        var seen = new HashSet<string>();
        var merchantKeys = new HashSet<string>();
        merchantCities = 0;
        foreach (City city in (empire.kingdoms_list ?? new List<Kingdom>())
                     .Where(kingdom => kingdom != null && !kingdom.isRekt())
                     .SelectMany(kingdom => kingdom.cities ?? Enumerable.Empty<City>())
                     .Where(city => city != null && !city.isRekt()))
        {
            bool foundMerchant = false;
            foreach (Actor actor in city.units ?? Enumerable.Empty<Actor>())
            {
                if (actor == null || actor.isRekt() || !actor.isAlive() || actor.city != city) continue;
                string key = actor.hasFamily() ? $"f:{actor.family.id}" : $"a:{actor.id}";
                seen.Add(key);
                if (!actor.GetOrCreate().is_economic_merchant) continue;
                merchantKeys.Add(key);
                foundMerchant = true;
            }
            if (foundMerchant) merchantCities++;
        }
        households = seen.Count;
        merchants = merchantKeys.Count;
    }

    #region 查询（只读，UI 可以随便调用）

    // 当前主体文化已经稳定了多少年。文化刚变、年度同步还没跑到时按 0 计。
    private static int GetStableCultureYears(Empire empire, ConstitutionalEconomyState state)
    {
        if (state == null || state.stable_culture_since < 0) return 0;
        string culture = CultureService.GetRealmCulture(empire.CoreKingdom);
        if (CultureService.IsValidCulture(culture) &&
            !string.Equals(state.stable_culture, culture, StringComparison.Ordinal)) return 0;
        return Math.Max(0, Date.getYearsSince(state.stable_culture_since));
    }

    // 立宪所需、而本文化尚未具备的制度特性
    public static List<string> GetMissingFeatures(Empire empire)
    {
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        return InstitutionDefinitionRegistry.GetConstitutionRequiredFeatures(GetLine(empire))
            .Where(feature => !InstitutionSystem.HasFeature(culture, feature)).ToList();
    }

    // 缺失特性的显示文本：列出本线能提供该特性的节点名；本线没有的话显示特性本身的名称
    private static string DescribeMissingFeatures(Empire empire, List<string> missing)
    {
        string line = GetLine(empire);
        return string.Join(", ", missing.Select(feature =>
        {
            List<string> names = InstitutionDefinitionRegistry.GetForLine(line)
                .Where(node => node.features.ContainsKey(feature))
                .Select(InstitutionSystem.GetNodeName).ToList();
            if (names.Count > 0) return string.Join("/", names);
            string key = $"institution_feature_{feature}";
            string text = LM.Get(key);
            return string.IsNullOrWhiteSpace(text) || text == key ? feature : text;
        }));
    }

    public static ConstitutionalEconomyView GetView(Empire empire)
    {
        var view = new ConstitutionalEconomyView();
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || World.world == null || empire.CoreKingdom == null) return view;
        CountHouseholds(empire, out view.total_households, out view.merchant_households,
            out view.merchant_cities);
        foreach (CompletedTradeVoyage voyage in state.recent_trade.Where(IsRecent))
        {
            view.voyages++;
            if (voyage.foreign_kingdom) view.foreign_voyages++;
            view.delivered_gold += voyage.delivered_gold;
        }
        bool monarchy = IsMonarchy(empire);
        view.culture_years = GetStableCultureYears(empire, state);
        view.budding_years = state.capitalist_candidate_since < 0 ? 0 :
            Math.Max(0, Date.getYearsSince(state.capitalist_candidate_since));
        view.budding = state.capitalist_budding;
        view.constitutional = state.constitutional_monarchy && monarchy;
        view.reforming = state.constitutional_reform_active && monarchy;
        view.reform_progress = view.reforming ? state.constitutional_reform_progress : 0f;
        view.reform_stage = view.reforming ? state.constitutional_reform_stage : 0;
        view.parliamentary_support = GetParliamentarySupport(empire);
        view.reform_stalled = view.reforming &&
                              (!view.budding || view.parliamentary_support < Config.support_threshold);
        CanStartReform(empire, out view.blocker);
        if (view.blocker == "constitution_requires_institutions")
            view.missing_institutions = DescribeMissingFeatures(empire, GetMissingFeatures(empire));
        return view;
    }

    public static bool CanStartReform(Empire empire, out string reason)
    {
        reason = "constitution_unavailable";
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || empire.CoreKingdom == null || World.world == null) return false;
        string line = GetLine(empire);
        if (!InstitutionDefinitionRegistry.IsConstitutionEnabled(line)) return false;
        if (state.constitutional_monarchy) { reason = "constitution_already_enacted"; return false; }
        if (state.constitutional_reform_active) { reason = "constitution_reforming"; return false; }
        if (!IsMonarchy(empire)) { reason = "constitution_requires_monarchy"; return false; }
        if (empire.data.institution_state?.active_reform != null)
        { reason = "constitution_other_reform"; return false; }
        if (!state.capitalist_budding) { reason = "constitution_requires_budding"; return false; }
        if (GetStableCultureYears(empire, state) < Config.stable_culture_years ||
            state.stable_culture_since < 0)
        { reason = "constitution_requires_stability"; return false; }
        if (GetMissingFeatures(empire).Count > 0) { reason = "constitution_requires_institutions"; return false; }
        if (InstitutionDefinitionRegistry.IsConstitutionRequiringCompositeEmpire(line) &&
            empire.data.composite_integration_stage < CompositeEmpireIntegrationStage.CompositeEmpire)
        { reason = "constitution_requires_composite"; return false; }
        if (GetParliamentarySupport(empire) < Config.support_threshold)
        { reason = "constitution_requires_support"; return false; }
        reason = "";
        return true;
    }

    #endregion

    public static bool StartReform(Empire empire)
    {
        if (!CanStartReform(empire, out _)) return false;
        ConstitutionalEconomyState state = Ensure(empire);
        state.constitutional_reform_active = true;
        state.constitutional_reform_started = World.world.getCurWorldTime();
        state.constitutional_reform_progress = 0f;
        state.constitutional_reform_stage = 0;
        Record(empire, "constitution_started_history");
        return true;
    }

    private static void AdvanceReform(Empire empire, ConstitutionalEconomyState state)
    {
        ConstitutionConfig config = Config;
        if (!IsMonarchy(empire))
        {
            CancelReform(state);
            return;
        }
        if (!state.capitalist_budding || GetParliamentarySupport(empire) < config.support_threshold) return;
        int minimumYears = InstitutionSystem.GetReformEnvironment(empire).EffectiveMinimumYears;
        state.constitutional_reform_progress = Math.Min(100f,
            state.constitutional_reform_progress + 100f / Math.Max(1, minimumYears));
        int stage = Math.Min(4, (int)(state.constitutional_reform_progress / 25f));
        if (stage > state.constitutional_reform_stage)
        {
            if (state.constitutional_reform_stage < 1 && stage >= 1)
            {
                empire.AddMandate(-config.start_mandate_cost);
                InstitutionEmpireState institution = empire.data.institution_state;
                if (institution != null)
                {
                    InstitutionStateNormalizer.Normalize(institution);
                    institution.class_grievances[SocialClass.Noble] = Math.Min(100f,
                        institution.class_grievances[SocialClass.Noble] + config.noble_grievance_on_start);
                    institution.class_grievance_causes[SocialClass.Noble] = "constitution";
                }
            }
            state.constitutional_reform_stage = stage;
            empire.RecordHistory(directContent: string.Format(LM.Get("constitution_stage_history"),
                    LM.Get($"constitution_stage_{stage}")),
                actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
            ParliamentSystem.Update(empire);
        }
        if (state.constitutional_reform_progress < 100f ||
            Date.getYearsSince(state.constitutional_reform_started) < minimumYears) return;
        state.constitutional_reform_active = false;
        state.constitutional_monarchy = true;
        ParliamentSystem.Update(empire);
        Record(empire, "constitution_enacted_history");
    }

    public static float GetParliamentarySupport(Empire empire)
    {
        List<FixedFaction> factions = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.Where(faction => faction != null && !faction.Ban).ToList();
        if (factions == null || factions.Count == 0) return 0f;
        bool budding = Ensure(empire)?.capitalist_budding == true;
        // 议会召开后按实际议席计票；议会召开前按各派系的中央占比估算
        ParliamentView parliament = ParliamentSystem.GetView(empire);
        if (parliament.Exists && parliament.TotalSeats > 0)
            return parliament.Seats.Count(seat => seat.Constitutionalist) * 100f / parliament.TotalSeats;
        float total = 0f;
        float support = 0f;
        foreach (FixedFaction faction in factions)
        {
            float seats = Math.Max(0f, faction.CentralRatio);
            total += seats;
            if (IsConstitutionalist(faction, budding)) support += seats;
        }
        return total <= 0f ? 0f : support / total * 100f;
    }

    public static bool CanChangeTax(Empire empire) =>
        !HasAssemblyTaxPower(empire) || GetParliamentarySupport(empire) >= Config.support_threshold;

    public static bool HasConstitution(Empire empire) =>
        Ensure(empire)?.constitutional_monarchy == true && IsMonarchy(empire);

    // 议会取得征税同意权
    public static bool HasAssemblyTaxPower(Empire empire) => ParliamentSystem.HasParliament(empire);

    // 派系是否站在宪制一边：决定议会支持率，也决定议会选举中宪政联盟的组成
    public static bool IsConstitutionalist(FixedFaction faction, bool budding)
    {
        ConstitutionConfig config = Config;
        FactionClassSystem.EnsureProfile(faction);
        float ideology = config.faction_stances.TryGetValue(faction.Type, out float stance) ? stance : 0f;
        float merchantAffinity = faction.ClassAffinities[SocialClass.Merchant];
        float marketPressure = budding && merchantAffinity >= 0f ? config.budding_merchant_bonus : 0f;
        return ideology + marketPressure + merchantAffinity * config.merchant_affinity_weight +
               faction.ClassFavor[SocialClass.Merchant] - config.support_baseline > 0f;
    }
}
