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

public static class ConstitutionalEconomySystem
{
    private const int TradeWindowYears = 10;
    private const int BuddingYears = 5;
    private const int StableCultureYears = 50;

    private static ConstitutionalEconomyState Ensure(Empire empire)
    {
        if (empire?.data == null) return null;
        empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        state.recent_trade ??= new List<CompletedTradeVoyage>();
        state.stable_culture ??= "";
        return state;
    }

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
        int gold = Math.Min(5, Math.Max(0, delivery.city.getResourcesAmount("gold") - delivery.gold_before));
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

    private static void PruneTrade(ConstitutionalEconomyState state)
    {
        state.recent_trade.RemoveAll(voyage => voyage == null || voyage.timestamp < 0 ||
            Date.getYearsSince(voyage.timestamp) >= TradeWindowYears);
    }

    public static void Update(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || World.world == null ||
            empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return;
        ConstitutionalEconomyState state = Ensure(empire);
        SyncCulture(empire, state);
        SyncRegime(empire, state);
        if (HasResponsibleCabinet(empire) &&
            (empire.CoreKingdom.GetRegime()?.has_cabinet != true || empire.GetCabinetLeader() == null))
            EnsureCabinet(empire);
        if (state.last_economy_update >= 0 && Date.getYearsSince(state.last_economy_update) < 1) return;
        state.last_economy_update = World.world.getCurWorldTime();
        PruneTrade(state);
        CountHouseholds(empire, out int households, out int merchants, out int merchantCities);
        bool viable = merchants >= 2 && households > 0 && merchants * 20 >= households &&
                      (state.recent_trade.Count > 0 || merchants >= 4 && merchantCities >= 2);
        if (viable)
        {
            state.capitalist_decline_since = -1d;
            if (state.capitalist_candidate_since < 0)
                state.capitalist_candidate_since = World.world.getCurWorldTime();
            if (!state.capitalist_budding && Date.getYearsSince(state.capitalist_candidate_since) >= BuddingYears)
            {
                state.capitalist_budding = true;
                empire.RecordHistory(directContent: LM.Get("constitution_budding_history"),
                    actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
            }
        }
        else
        {
            state.capitalist_candidate_since = -1d;
            if (state.capitalist_decline_since < 0)
                state.capitalist_decline_since = World.world.getCurWorldTime();
            if (Date.getYearsSince(state.capitalist_decline_since) >= 3)
                state.capitalist_budding = false;
        }

        if (state.constitutional_reform_active) AdvanceReform(empire, state);
        if (HasResponsibleCabinet(empire)) EnsureCabinet(empire);
        if (state.constitutional_monarchy && GetParliamentarySupport(empire) < 50f &&
            (state.last_deadlock_notice < 0 || Date.getYearsSince(state.last_deadlock_notice) >= 5))
        {
            state.last_deadlock_notice = World.world.getCurWorldTime();
            empire.RecordHistory(directContent: LM.Get("constitution_deadlock_history"),
                actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        }
        if (!state.constitutional_reform_active && !state.constitutional_monarchy &&
            InstitutionDefinitionRegistry.Global.ai_enabled &&
            (state.last_ai_constitution_attempt < 0 ||
             Date.getYearsSince(state.last_ai_constitution_attempt) >= 3))
        {
            state.last_ai_constitution_attempt = World.world.getCurWorldTime();
            if (GetParliamentarySupport(empire) >= 55f) StartReform(empire);
        }
    }

    public static void ResetCultureStability(Empire empire)
    {
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || World.world == null) return;
        state.stable_culture = CultureService.GetRealmCulture(empire.CoreKingdom);
        state.stable_culture_since = World.world.getCurWorldTime();
    }

    private static void SyncCulture(Empire empire, ConstitutionalEconomyState state)
    {
        string culture = CultureService.GetRealmCulture(empire.CoreKingdom);
        if (!CultureService.IsValidCulture(culture)) return;
        if (string.Equals(state.stable_culture, culture, StringComparison.Ordinal) &&
            state.stable_culture_since >= 0) return;
        state.stable_culture = culture ?? "";
        state.stable_culture_since = World.world.getCurWorldTime();
        if (state.constitutional_reform_active)
        {
            state.constitutional_reform_active = false;
            state.constitutional_reform_progress = 0f;
            state.constitutional_reform_stage = 0;
            RestoreCabinetConfiguration(empire);
            empire.RecordHistory(directContent: LM.Get("constitution_culture_changed_history"),
                actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        }
    }

    private static void SyncRegime(Empire empire, ConstitutionalEconomyState state)
    {
        Regime regime = empire.CoreKingdom.GetRegime();
        if (regime == null || IsMonarchy(regime.type)) return;
        bool hadResponsibleCabinet = state.constitutional_reform_active &&
                                     state.constitutional_reform_stage >= 3;
        state.constitutional_reform_active = false;
        state.constitutional_reform_progress = 0f;
        state.constitutional_reform_stage = 0;
        if (!state.constitutional_monarchy)
        {
            if (hadResponsibleCabinet) RestoreCabinetConfiguration(empire);
            return;
        }
        state.constitutional_monarchy = false;
        RestoreCabinetConfiguration(empire);
        empire.RecordHistory(directContent: LM.Get("constitution_ended_history"),
            actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
    }

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

    public static ConstitutionalEconomyView GetView(Empire empire)
    {
        var view = new ConstitutionalEconomyView();
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || World.world == null) return view;
        SyncCulture(empire, state);
        SyncRegime(empire, state);
        CountHouseholds(empire, out view.total_households, out view.merchant_households,
            out view.merchant_cities);
        foreach (CompletedTradeVoyage voyage in state.recent_trade.Where(voyage => voyage != null &&
                     Date.getYearsSince(voyage.timestamp) < TradeWindowYears))
        {
            view.voyages++;
            if (voyage.foreign_kingdom) view.foreign_voyages++;
            view.delivered_gold += voyage.delivered_gold;
        }
        view.culture_years = state.stable_culture_since < 0 ? 0 :
            Math.Max(0, Date.getYearsSince(state.stable_culture_since));
        view.budding_years = state.capitalist_candidate_since < 0 ? 0 :
            Math.Max(0, Date.getYearsSince(state.capitalist_candidate_since));
        view.budding = state.capitalist_budding;
        view.constitutional = state.constitutional_monarchy;
        view.reforming = state.constitutional_reform_active;
        view.reform_progress = state.constitutional_reform_progress;
        view.reform_stage = state.constitutional_reform_stage;
        view.parliamentary_support = GetParliamentarySupport(empire);
        view.reform_stalled = view.reforming && (!view.budding || view.parliamentary_support < 50f);
        CanStartReform(empire, out view.blocker);
        if (view.blocker == "constitution_requires_institutions")
        {
            string line = InstitutionSystem.GetCultureLine(InstitutionSystem.GetPrimaryCulture(empire));
            view.missing_institutions = string.Join(", ", RequiredNodes(line)
                .Where(node => !InstitutionSystem.IsEnacted(empire, node))
                .Select(node => InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(node))));
        }
        return view;
    }

    public static bool CanStartReform(Empire empire, out string reason)
    {
        reason = "constitution_unavailable";
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || empire.CoreKingdom == null || World.world == null) return false;
        SyncCulture(empire, state);
        if (state.constitutional_monarchy) { reason = "constitution_already_enacted"; return false; }
        if (state.constitutional_reform_active) { reason = "constitution_reforming"; return false; }
        if (!IsMonarchy(empire.CoreKingdom.GetRegime()?.type))
        { reason = "constitution_requires_monarchy"; return false; }
        if (empire.data.institution_state?.active_reform != null)
        { reason = "constitution_other_reform"; return false; }
        if (!state.capitalist_budding)
        { reason = "constitution_requires_budding"; return false; }
        if (state.stable_culture_since < 0 ||
            Date.getYearsSince(state.stable_culture_since) < StableCultureYears)
        { reason = "constitution_requires_stability"; return false; }
        string line = InstitutionSystem.GetCultureLine(InstitutionSystem.GetPrimaryCulture(empire));
        foreach (string node in RequiredNodes(line))
        {
            if (InstitutionSystem.IsEnacted(empire, node)) continue;
            reason = "constitution_requires_institutions";
            return false;
        }
        if (line == "YouMu" &&
            empire.data.composite_integration_stage < CompositeEmpireIntegrationStage.CompositeEmpire)
        { reason = "constitution_requires_composite"; return false; }
        if (GetParliamentarySupport(empire) < 50f)
        { reason = "constitution_requires_support"; return false; }
        reason = "";
        return true;
    }

    private static bool IsMonarchy(RegimeType? regime) => regime is RegimeType.LvLing or
        RegimeType.Feudalism or RegimeType.ZhouFeudalism or RegimeType.Arabic or RegimeType.YouMu;

    private static IEnumerable<string> RequiredNodes(string line) => line switch
    {
        "Roma" => new[] { "western_great_charter", "western_parliamentary_taxation" },
        "Huaxia" => new[] { "huaxia_prefecture_county_bureaucracy", "huaxia_single_whip_reform" },
        "Arabic" => new[] { "arabic_caravan_trade", "arabic_sultanate_bureaucracy", "arabic_bayt_al_mal" },
        "YouMu" => new[] { "youmu_dual_administration", "youmu_steppe_law" },
        _ => new[] { "__unavailable__" }
    };

    public static bool StartReform(Empire empire)
    {
        if (!CanStartReform(empire, out _)) return false;
        ConstitutionalEconomyState state = Ensure(empire);
        state.constitutional_reform_active = true;
        state.constitutional_reform_started = World.world.getCurWorldTime();
        state.constitutional_reform_progress = 0f;
        state.constitutional_reform_stage = 0;
        empire.RecordHistory(directContent: LM.Get("constitution_started_history"),
            actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        return true;
    }

    private static void AdvanceReform(Empire empire, ConstitutionalEconomyState state)
    {
        if (!IsMonarchy(empire.CoreKingdom.GetRegime()?.type))
        {
            state.constitutional_reform_active = false;
            return;
        }
        if (!state.capitalist_budding || GetParliamentarySupport(empire) < 50f) return;
        int minimumYears = InstitutionSystem.GetReformEnvironment(empire).EffectiveMinimumYears;
        state.constitutional_reform_progress = Math.Min(100f,
            state.constitutional_reform_progress + 100f / Math.Max(1, minimumYears));
        int stage = Math.Min(4, (int)(state.constitutional_reform_progress / 25f));
        if (stage > state.constitutional_reform_stage)
        {
            if (state.constitutional_reform_stage < 1 && stage >= 1)
            {
                empire.AddMandate(-5);
                InstitutionEmpireState institution = empire.data.institution_state;
                if (institution != null)
                {
                    InstitutionStateNormalizer.Normalize(institution);
                    institution.class_grievances[SocialClass.Noble] = Math.Min(100f,
                        institution.class_grievances[SocialClass.Noble] + 8f);
                    institution.class_grievance_causes[SocialClass.Noble] = "constitution";
                }
            }
            state.constitutional_reform_stage = stage;
            empire.RecordHistory(directContent: string.Format(LM.Get("constitution_stage_history"),
                    LM.Get($"constitution_stage_{stage}")),
                actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        }
        if (state.constitutional_reform_progress < 100f ||
            Date.getYearsSince(state.constitutional_reform_started) < minimumYears) return;
        state.constitutional_reform_active = false;
        state.constitutional_monarchy = true;
        EnsureCabinet(empire);
        empire.RecordHistory(directContent: LM.Get("constitution_enacted_history"),
            actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
    }

    public static float GetParliamentarySupport(Empire empire)
    {
        List<FixedFaction> factions = empire?.CoreKingdom?.GetRegime()?.GetPlayerFactions()
            ?.Where(faction => faction != null && !faction.Ban).ToList();
        if (factions == null || factions.Count == 0) return 0f;
        bool budding = Ensure(empire)?.capitalist_budding == true;
        float total = 0f;
        float support = 0f;
        foreach (FixedFaction faction in factions)
        {
            float seats = Math.Max(0f, faction.CentralRatio);
            total += seats;
            if (SupportsConstitution(faction, budding)) support += seats;
        }
        return total <= 0f ? 0f : support / total * 100f;
    }

    public static bool CanChangeTax(Empire empire) =>
        !HasAssemblyTaxPower(empire) || GetParliamentarySupport(empire) >= 50f;

    public static bool HasConstitution(Empire empire) =>
        Ensure(empire)?.constitutional_monarchy == true && IsMonarchy(empire?.CoreKingdom?.GetRegime()?.type);

    public static bool HasAssemblyTaxPower(Empire empire)
    {
        ConstitutionalEconomyState state = Ensure(empire);
        return IsMonarchy(empire?.CoreKingdom?.GetRegime()?.type) &&
               (state?.constitutional_monarchy == true ||
                state?.constitutional_reform_active == true && state.constitutional_reform_stage >= 2);
    }

    public static bool HasResponsibleCabinet(Empire empire)
    {
        ConstitutionalEconomyState state = Ensure(empire);
        return IsMonarchy(empire?.CoreKingdom?.GetRegime()?.type) &&
               (state?.constitutional_monarchy == true ||
                state?.constitutional_reform_active == true && state.constitutional_reform_stage >= 3);
    }

    public static void EnsureCabinet(Empire empire)
    {
        if (!HasResponsibleCabinet(empire)) return;
        Regime regime = empire.CoreKingdom?.GetRegime();
        if (regime == null) return;
        regime.has_cabinet = true;
        if (regime.cabinet_number < 3) regime.cabinet_number = 5;
        if (GetParliamentarySupport(empire) < 50f) return;
        bool budding = Ensure(empire)?.capitalist_budding == true;
        List<FixedFaction> coalition = regime.GetPlayerFactions()
            ?.Where(faction => faction != null && !faction.Ban && faction.CentralRatio > 0)
            .OrderByDescending(faction => faction.CentralRatio)
            .Where(faction => SupportsConstitution(faction, budding)).ToList();
        if (coalition == null || coalition.Count == 0) return;
        var coalitionIds = new HashSet<string>(coalition.Select(faction => faction.GetID()));
        foreach (long memberId in empire.data.CabinetMembers.ToList())
        {
            Actor member = World.world.units.get(memberId);
            if (member == null || member.isRekt())
            {
                empire.data.CabinetMembers.Remove(memberId);
                continue;
            }
            if (!coalitionIds.Contains(member.GetFaction()?.GetID()))
                empire.RemoveCabinetMember(member);
        }
        List<Actor> candidates = coalition.SelectMany(faction => faction.AllMembers)
            .Where(actor => actor != null && !actor.isRekt() && actor.HasOfficeIdentity())
            .Distinct()
            .OrderByDescending(actor => actor.GetIdentity()?.TotalPerformance ?? double.MinValue)
            .ToList();
        if (candidates.Count == 0) return;
        empire.SetCabinetLeader(candidates[0]);
        foreach (Actor actor in candidates.Skip(1).Take(Math.Max(0, regime.cabinet_number - 1)))
            empire.AddCabinetMember(actor);
    }

    private static void RestoreCabinetConfiguration(Empire empire)
    {
        Regime regime = empire.CoreKingdom?.GetRegime();
        if (regime == null || RegimeManager.regimes == null ||
            !RegimeManager.regimes.TryGetValue(regime.type, out Regime template)) return;
        regime.has_cabinet = template.has_cabinet;
        regime.cabinet_number = template.cabinet_number;
        if (regime.has_cabinet) return;
        foreach (long memberId in empire.data.CabinetMembers.ToList())
        {
            Actor member = World.world.units.get(memberId);
            if (member != null && !member.isRekt()) empire.RemoveCabinetMember(member);
            else empire.data.CabinetMembers.Remove(memberId);
        }
    }

    private static bool SupportsConstitution(FixedFaction faction, bool budding)
    {
        FactionClassSystem.EnsureProfile(faction);
        float ideology = faction.Type switch
        {
            FactionType.自治 or FactionType.绥靖 or FactionType.共和 or FactionType.民主 or
                FactionType.融入 or FactionType.诸侯 => 20f,
            FactionType.僭主 => -10f,
            FactionType.中央 or FactionType.神权 or FactionType.攘夷 or FactionType.尊王 or
                FactionType.血脉 or FactionType.同化 => -20f,
            _ => 0f
        };
        float marketPressure = budding && faction.ClassAffinities[SocialClass.Merchant] >= 0f ? 12f : 0f;
        return ideology + marketPressure + faction.ClassAffinities[SocialClass.Merchant] * 0.15f +
               faction.ClassFavor[SocialClass.Merchant] - 50f > 0f;
    }
}
