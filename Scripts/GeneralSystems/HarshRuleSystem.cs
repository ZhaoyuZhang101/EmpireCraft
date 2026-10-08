using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 苛政与民怨。每年按赋税、腐败、国库亏空、战争征兵、饥荒、土地兼并、言论管制算出"苛政"(0~100)，
// 帝国按整个帝国结算，没有加入帝国的独立王国按本国结算。前现代与现代国家的后果不同：
//
//   前现代(以及独立王国)——官逼民反：
//     · 各城忠诚随苛政下降(苛政超过 20 后每点 -0.5，最多 -30)；
//     · 苛政 ≥ 60 时每年有机会爆发民变：最不忠诚的城市揭竿而起，打起"某城民变"的旗号。
//       现代政体的独立王国(没有组建政府的地方势力)同样会民变，但要先过现代治理能力的门槛(ModernStability)。
//     · 与土地兼并打通(见 LandEconomySystem)：无地农民多的政权苛政更重；苛政越重，农民土地起义的门槛越低；
//       民变的城市若土地兼并严重，就打出"均田"的旗号，成为农民土地起义(胜利后重分土地)。
//       现代国家因土地兼并而让步或政府倒台时，在兼并最严重的城市重分土地。
//
//   现代——很难演变成叛乱，而是街头抗争逐级升级(每年最多升一级)：
//     游行示威(苛政 ≥ 30) → 冲突(≥ 50) → 大规模暴乱(≥ 70)。议会与人大吸纳一部分不满(压力 ×0.85)。
//     · 每一级都拉低宪政合法性并推高民意中的异见；
//     · 政府回应(每年一次)：玩家可以在制度窗口亲自选择镇压或让步(示威起即可)；玩家一年内没表态的话，
//       冲突及以上时由 AI 决定——言论严格的国家倾向镇压，威权/法西斯政府有把握时也镇压，
//       连续镇压两年无效就改为让步，其余国家让步。
//       镇压：六成把握平息一级，但两年内合法性再降、军方坐大；让步：减税一成、整顿腐败；
//     · 大规模暴乱持续两年，政局剧变：
//         合法性跌破 40 或连续镇压三年，且有政党掌握四成以上军队 → 军事政变，该党取缔他党掌权；
//         否则议会制政府倒台(能组成新的过半联盟就重组内阁，不能就提前大选)；
//         没有责任内阁的(一党制、总统制)执政党领导人引咎辞职、另立新人。
//       剧变后新政府减税整顿，抗争回落到示威。
public static class HarshRuleSystem
{
    public const int Calm = 0;
    public const int Demonstrations = 1;
    public const int Clashes = 2;
    public const int Riots = 3;

    private const float UprisingBurden = 60f;
    private const int UprisingCooldownYears = 10;
    private const int RiotYearsToCrisis = 2;
    private const float CoupMilitaryShare = 0.4f;
    private const int CoupLegitimacy = 40;
    private const int RepressionYearsToCoup = 3;
    private const float RepressionSuccess = 0.6f;

    private static ConstitutionalEconomyState State(Empire empire) => empire?.data?.constitutional_economy;

    public static float GetBurden(Empire empire) => State(empire)?.harsh_burden ?? 0f;
    public static int GetStreetStage(Empire empire) => State(empire)?.street_unrest_stage ?? Calm;

    public static bool RecentlyRepressed(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        return state != null && state.last_repression >= 0d && Date.getYearsSince(state.last_repression) < 2;
    }

    // 苛政各项构成：(本地化 key, 数值)，从大到小
    public static List<(string key, int value)> Breakdown(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt()) return new List<(string key, int value)>();
        List<Kingdom> realms = empire.kingdoms_list?.Where(kingdom => kingdom != null && !kingdom.isRekt()).ToList() ??
                               new List<Kingdom>();
        var items = Breakdown(core, realms, State(empire)?.bankrupt_since >= 0d,
            ModernLegitimacy.Applies(empire) && ConstitutionSystem.GetSpeech(empire) == ConstitutionSpeech.Strict,
            CorruptionSystem.GetRate(empire));
        AddDynasticBurden(empire, items);
        return items.OrderByDescending(item => item.value).ToList();
    }

    private static void AddDynasticBurden(Empire empire, List<(string key, int value)> items)
    {
        items.RemoveAll(item => item.key == DynasticCycleSystem.HarshSource);
        int strain = DynasticCycleRules.HarshBurden(DynasticCycleSystem.GetPressure(empire));
        if (strain > 0) items.Add((DynasticCycleSystem.HarshSource, strain));
    }

    // 独立王国的苛政构成(国库为负算欠饷)
    public static List<(string key, int value)> Breakdown(Kingdom kingdom) =>
        kingdom == null || kingdom.isRekt()
            ? new List<(string key, int value)>()
            : Breakdown(kingdom, new List<Kingdom> { kingdom }, kingdom.GetMoney() < 0, false);

    private static List<(string key, int value)> Breakdown(Kingdom core, List<Kingdom> realms, bool bankrupt,
        bool censorship, double? corruptionRate = null)
    {
        var items = new List<(string key, int value)>();
        void Add(string key, float value)
        {
            int rounded = Mathf.RoundToInt(value);
            if (rounded > 0) items.Add((key, rounded));
        }
        Add("harsh_tax", Mathf.Clamp(((float)core.GetTaxRate() - 0.25f) * 100f, 0f, 50f));
        Add("harsh_corruption", (float)(corruptionRate ?? CorruptionSystem.GetRate(core)) * 40f);
        if (bankrupt) Add("harsh_bankrupt", 15f);
        // 土地兼并：各城无地农民比例的平均(帝国按全帝国)，无地两成五约 +20，最多 +30
        Add("harsh_land", Mathf.Min(30f, LandEconomySystem.GetRealmLandlessRatio(core) * 80f));
        // 按户计(无小人模式下真实人数可达几百万，和士兵数、存粮不是一个量级)
        int population = realms.Sum(kingdom => CityPopulationSystem.Households(kingdom));
        if (core.getWars().Any() && population > 0)
        {
            float soldiers = realms.Sum(kingdom => kingdom.countTotalWarriors()) / (float)population;
            Add("harsh_war", Mathf.Clamp(5f + Mathf.Max(0f, soldiers - 0.1f) * 150f, 0f, 25f));
        }
        if (population > 0 && realms.Sum(kingdom => kingdom.countTotalFood()) < population * 0.5f)
            Add("harsh_famine", 15f);
        if (censorship) Add("harsh_speech", 8f);
        return items.OrderByDescending(item => item.value).ToList();
    }

    // 一座城所在政权的苛政：帝国成员按帝国，独立王国按本国
    public static float GetRealmBurden(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return 0f;
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        return empire != null ? GetBurden(empire) : GetBurden(kingdom);
    }

    // 腐败和王朝积弊已经有独立正统来源，不能再通过苛政把同一项重复扣一次。
    public static float OtherGovernanceBurden(Empire empire)
    {
        float settled = State(empire)?.harsh_other_burden ?? -1f;
        if (settled >= 0f) return Mathf.Clamp(settled, 0f, 100f);
        int corruption = State(empire)?.harsh_corruption_burden ?? -1;
        if (corruption < 0) corruption = Mathf.RoundToInt((float)CorruptionSystem.GetRate(empire) * 40f);
        return Mathf.Max(0f, GetBurden(empire) - corruption -
            DynasticCycleRules.HarshBurden(DynasticCycleSystem.GetPressure(empire)));
    }

    // 独立王国的苛政(帝国成员返回 0，按帝国看 GetBurden(Empire))
    public static float GetBurden(Kingdom kingdom) =>
        kingdom != null && !kingdom.IsInEmpire() ? kingdom.GetOrCreate().harsh_burden : 0f;

    #region 年度结算(由 ConstitutionalEconomySystem 年度结算调用)

    public static void Update(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || empire.CoreKingdom == null || World.world == null) return;
        CorruptionSystem.Update(empire);
        // 年度治理入口兜底；旧档的国家意志任务缺失时仍可整顿，同一年共用去重标记。
        CorruptionSystem.TryYearlyCampaign(empire.CoreKingdom);
        List<(string key, int value)> breakdown = Breakdown(empire);
        state.harsh_corruption_burden = breakdown.Where(item => item.key == "harsh_corruption").Sum(item => item.value);
        state.harsh_other_burden = Mathf.Clamp(breakdown.Where(item => item.key != "harsh_corruption" &&
            item.key != DynasticCycleSystem.HarshSource).Sum(item => item.value), 0f, 100f);
        float governanceBurden = Mathf.Clamp(breakdown.Where(item => item.key != DynasticCycleSystem.HarshSource)
            .Sum(item => item.value), 0f, 100f);
        DynasticCycleSystem.Update(empire, state, governanceBurden);
        AddDynasticBurden(empire, breakdown);
        breakdown = breakdown.OrderByDescending(item => item.value).ToList();
        state.harsh_burden = Mathf.Clamp(breakdown.Sum(item => item.value), 0f, 100f);
        state.harsh_main_cause = breakdown.Count > 0 ? breakdown[0].key : "";
        MonarchyLegitimacy.Invalidate(empire);
        if (ModernLegitimacy.Applies(empire)) UpdateStreetUnrest(empire, state);
        else
        {
            ResetStreet(state);
            TryUprising(empire.kingdoms_list, state.harsh_burden, state.harsh_main_cause, empire, false);
        }
    }

    // 独立王国：每年随城市年度更新扫一次(见 CityPatch)
    private static object _scanWorld;
    private static double _lastKingdomScan = -1d;

    public static void TryYearlyKingdomScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_scanWorld, World.world) || now < _lastKingdomScan)
        {
            _scanWorld = World.world;
            _lastKingdomScan = now;
            return;
        }
        if (Date.getYearsSince(_lastKingdomScan) < 1) return;
        _lastKingdomScan = now;
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv() || kingdom.IsEmpire() ||
                kingdom.IsInEmpire() || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom))
                continue;
            try
            {
                UpdateKingdom(kingdom);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft] 苛政结算失败({kingdom.name}): {exception.Message}");
            }
        }
    }

    private static void UpdateKingdom(Kingdom kingdom)
    {
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        List<(string key, int value)> breakdown = Breakdown(kingdom);
        data.harsh_burden = Mathf.Clamp(breakdown.Sum(item => item.value), 0f, 100f);
        data.harsh_main_cause = breakdown.Count > 0 ? breakdown[0].key : "";
        TryUprising(new List<Kingdom> { kingdom }, data.harsh_burden, data.harsh_main_cause, null,
            ModernStability.IsModern(kingdom));
    }

    private static void ResetStreet(ConstitutionalEconomyState state)
    {
        state.street_unrest_stage = Calm;
        state.street_unrest_years = 0;
        state.repression_years = 0;
    }

    private static string Cause(ConstitutionalEconomyState state) => Cause(state.harsh_main_cause);

    private static string Cause(string key) =>
        string.IsNullOrEmpty(key) ? LM.Get("harsh_cause_unknown") : LM.Get(key);

    #endregion

    #region 前现代：民变

    // 苛政的城市忠诚惩罚：前现代帝国按帝国，独立王国按本国；现代国家改为街头抗争
    public static int LoyaltyPenalty(City city)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null) return 0;
        float burden;
        if (kingdom.IsInEmpire())
        {
            Empire empire = kingdom.GetEmpire();
            if (empire == null || ModernLegitimacy.Applies(empire)) return 0;
            burden = GetBurden(empire);
        }
        else burden = GetBurden(kingdom);
        return burden <= 20f ? 0 : -Mathf.RoundToInt(Mathf.Min(30f, (burden - 20f) * 0.5f));
    }

    private static void TryUprising(IEnumerable<Kingdom> realms, float burden, string causeKey, Empire empire,
        bool modern)
    {
        if (burden < UprisingBurden) return;
        bool crisis = !modern && DynasticCycleSystem.InCrisis(empire);
        if (crisis)
        {
            ConstitutionalEconomyState state = State(empire);
            double now = World.world.getCurWorldTime();
            if (state.last_dynastic_uprising_attempt >= 0d && now >= state.last_dynastic_uprising_attempt &&
                Date.getYearsSince(state.last_dynastic_uprising_attempt) < 1) return;
            state.last_dynastic_uprising_attempt = now;
        }
        if (UnityEngine.Random.value >= (burden - 50f) / 100f) return;
        int waves = crisis ? DynasticCycleRules.UprisingWaves(DynasticCycleSystem.GetPressure(empire), burden) : 1;
        List<City> cities = (realms ?? Enumerable.Empty<Kingdom>())
            .Where(kingdom => RebellionSystem.CanAttempt(kingdom) && (crisis || !kingdom.getWars().Any()))
            .SelectMany(kingdom => kingdom.cities.Where(c => c != null && !c.isRekt() && c != kingdom.capital))
            .Where(c => c.getLoyalty() < (crisis ? 20 : 10) && CanRiseAgain(c) && CityStabilitySystem.CanRise(c))
            .OrderBy(c => c.getLoyalty()).Take(waves).ToList();
        foreach (City city in cities)
        {
            // 第一批起事的原版/模组回调可能改动后一座城，逐城重验归属和首都保护。
            if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isRekt() ||
                city == city.kingdom.capital || !CanRiseAgain(city) ||
                (empire != null && city.kingdom.GetEmpire() != empire)) continue;
            if (!RebellionSystem.CanAttempt(city.kingdom)) continue;
            // 土地兼并严重的城市：民变打出均田的旗号，成为农民土地起义
            if (LandEconomySystem.GetLandlessRatio(city) >= LandEconomySystem.EffectiveRebellionThreshold(city) &&
                LandEconomySystem.TryStartPeasantLandRebellion(city))
            {
                city.GetOrCreate().last_harsh_uprising = World.world.getCurWorldTime();
                continue;
            }
            if (!ModernStability.PassRebellionGate(city.kingdom)) continue;
            StartUprising(empire, city, burden, causeKey);
        }
    }

    private static bool CanRiseAgain(City city)
    {
        double last = city.GetOrCreate().last_harsh_uprising;
        return last < 0d || Date.getYearsSince(last) >= UprisingCooldownYears;
    }

    private static void StartUprising(Empire empire, City city, float burden, string causeKey)
    {
        Kingdom origin = city.kingdom;
        Actor leader = city.units?.Where(actor => actor != null && !actor.isRekt() && actor.isAlive() &&
                                                  actor.isAdult() && !actor.isKing() && actor.CanFoundCivKingdom())
            .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        // 无小人模式：民变由百姓带头，不由城里的官员、城主带头
        if (CityPopulationSystem.AbstractPopulationEnabled)
            leader = CityPopulationSystem.SpawnRebelLeader(city) ?? leader;
        if (leader == null) return;
        RebellionCauseData cause = RebellionSystem.Capture(origin, city, "rebellion_reason_harsh_rule",
            string.Format(LM.Get("rebellion_harsh_detail"), Cause(causeKey), Mathf.RoundToInt(burden)));
        Kingdom rebel = city.makeOwnKingdom(leader, pRebellion: true);
        if (rebel == null) return;
        if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱))
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return;
        }
        string cityName = city.GetCityName();
        rebel.data.name = string.Format(LM.Get("harsh_uprising_kingdom_name"), cityName);
        War war = World.world.diplomacy.startWar(rebel, origin, WarTypeLibrary.rebellion);
        if (war == null)
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        RebellionSystem.Record(rebel, war, cause);
        war.data.name = string.Format(LM.Get("harsh_uprising_war_name"), cityName);
        RebellionStartupService.RaiseUprisingMilitia(rebel, burden / 100f);
        city.GetOrCreate().last_harsh_uprising = World.world.getCurWorldTime();
        string history = string.Format(LM.Get("harsh_uprising_history"), cityName, Cause(causeKey),
            Mathf.RoundToInt(burden));
        if (empire != null) EventRecorder.Record(empire, history, leader, origin);
        else EventRecorder.Record(origin, history, leader);
    }

    #endregion

    #region 现代：街头抗争

    // 宪政合法性的扣减：示威 -5 / 冲突 -10 / 暴乱 -20
    public static int LegitimacyPenalty(Empire empire) => GetStreetStage(empire) switch
    {
        Demonstrations => -5,
        Clashes => -10,
        Riots => -20,
        _ => 0
    };

    // 民意异见的增加：每级 +0.05
    public static float DissentBonus(Empire empire) => ModernLegitimacy.Applies(empire) ? 0.05f * GetStreetStage(empire) : 0f;

    public static string GetStageName(int stage) => LM.Get($"street_unrest_stage_{Mathf.Clamp(stage, 0, 3)}");

    private static void UpdateStreetUnrest(Empire empire, ConstitutionalEconomyState state)
    {
        float pressure = state.harsh_burden * (ParliamentSystem.HasParliament(empire) ? 0.85f : 1f);
        int target = pressure >= 70f ? Riots : pressure >= 50f ? Clashes : pressure >= 30f ? Demonstrations : Calm;
        int previous = state.street_unrest_stage;
        if (target > previous) state.street_unrest_stage = previous + 1;
        else if (target < previous) state.street_unrest_stage = previous - 1;
        string country = empire.GetEmpireFullName();
        if (state.street_unrest_stage > previous)
            EventRecorder.Record(empire, string.Format(LM.Get($"street_unrest_log_{state.street_unrest_stage}"),
                country, Cause(state)));
        else if (state.street_unrest_stage == Calm && previous > Calm)
            EventRecorder.Record(empire, string.Format(LM.Get("street_unrest_calm_log"), country));

        // 玩家一年内亲自回应过的不再由 AI 重复回应
        bool responded = state.last_street_response >= 0d && Date.getYearsSince(state.last_street_response) < 1;
        if (state.street_unrest_stage >= Clashes && !responded) Respond(empire, state, AiChoosesRepression(empire, state), false);
        state.street_unrest_years = state.street_unrest_stage == Riots ? state.street_unrest_years + 1 : 0;
        if (state.street_unrest_years >= RiotYearsToCrisis) Crisis(empire, state);
    }

    // AI 的回应：言论严格倾向镇压，威权/法西斯政府在合法性尚可时也镇压；连续镇压两年无效改为让步
    public static bool AiChoosesRepression(Empire empire, ConstitutionalEconomyState state)
    {
        if (state.repression_years >= 2) return false;
        if (ConstitutionSystem.GetSpeech(empire) == ConstitutionSpeech.Strict) return true;
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        return governing != null && (governing.Ideology == PartyIdeology.Authoritarianism ||
                                     governing.Ideology == PartyIdeology.Fascism) && empire.Legitimacy >= 50;
    }

    // 玩家能否回应：现代国家、出现街头抗争、一年内还没回应过；返回不能的原因(本地化 key)，能则 null
    public static string CanPlayerRespond(Empire empire)
    {
        ConstitutionalEconomyState state = State(empire);
        if (state == null || !ModernLegitimacy.Applies(empire) || state.street_unrest_stage < Demonstrations)
            return "street_response_no_unrest";
        if (state.last_street_response >= 0d && Date.getYearsSince(state.last_street_response) < 1)
            return "street_response_cooldown";
        return null;
    }

    public static string PlayerRespond(Empire empire, bool repress)
    {
        string reason = CanPlayerRespond(empire);
        if (reason != null) return reason;
        Respond(empire, State(empire), repress, true);
        return null;
    }

    private static void Respond(Empire empire, ConstitutionalEconomyState state, bool repress, bool byPlayer)
    {
        string country = empire.GetEmpireFullName();
        state.last_street_response = World.world.getCurWorldTime();
        state.last_street_response_kind = repress ? "repress" : "concede";
        state.last_street_response_by_player = byPlayer;
        if (repress)
        {
            state.repression_years++;
            state.last_repression = World.world.getCurWorldTime();
            bool quelled = UnityEngine.Random.value < RepressionSuccess;
            if (quelled) state.street_unrest_stage--;
            EventRecorder.Record(empire, string.Format(LM.Get(quelled ? "street_repression_quelled_log"
                : "street_repression_failed_log"), country));
            return;
        }
        state.repression_years = 0;
        Concede(empire, 0.1f, 0.05);
        EventRecorder.Record(empire, string.Format(LM.Get("street_concession_log"), country, Cause(state)));
        // 民怨主要来自土地兼并：让步包括在兼并最严重的两座城市重分土地
        if (state.harsh_main_cause == "harsh_land") RedistributeWorstCities(empire, 2);
    }

    // 土地改革：无地农民比例达到起义门槛的城市里，按兼并程度从重到轻重分土地，最多 limit 座
    private static void RedistributeWorstCities(Empire empire, int limit)
    {
        List<City> cities = empire.AllCities()
            .Where(city => city != null && !city.isRekt() && LandEconomySystem.IsLandMarketOpen(city.kingdom) &&
                           LandEconomySystem.GetLandlessRatio(city) >= LandEconomySystem.RebellionLandlessThreshold)
            .OrderByDescending(LandEconomySystem.GetLandlessRatio).Take(limit).ToList();
        if (cities.Count == 0) return;
        foreach (City city in cities) LandEconomySystem.RedistributeLand(city);
        EventRecorder.Record(empire, string.Format(LM.Get("street_land_reform_log"), empire.GetEmpireFullName(),
            string.Join("、", cities.Take(5).Select(city => city.GetCityName())) + (cities.Count > 5 ? "…" : "")));
    }

    // 让步：减税、整顿腐败(街头压力下绕过议会的征税同意权)
    private static void Concede(Empire empire, float taxCut, double corruptionCut)
    {
        empire.data.TaxRate = Mathf.Max(0f, empire.data.TaxRate - taxCut);
        empire.CoreKingdom.AddCorruptionRate(-corruptionCut);
    }

    private static void Crisis(Empire empire, ConstitutionalEconomyState state)
    {
        string country = empire.GetEmpireFullName();
        FixedFaction governing = PartySystem.GetGovernmentParty(empire);
        bool resolved = false;
        if (empire.Legitimacy < CoupLegitimacy || state.repression_years >= RepressionYearsToCoup)
        {
            FixedFaction junta = PartySystem.GetParties(empire)
                .Where(party => party != null && party != governing && party.IsParty && !party.Ban &&
                                IdeologyPopulationSystem.GetMilitaryShare(empire, party.Ideology) >= CoupMilitaryShare)
                .OrderByDescending(party => IdeologyPopulationSystem.GetMilitaryShare(empire, party.Ideology))
                .FirstOrDefault();
            if (junta != null)
            {
                PartyBanSystem.Close(empire, junta, Enumerable.Empty<FixedFaction>(), "harsh_rule_coup_history");
                ParliamentSystem.ReelectGovernment(empire);
                RepublicSystem.UpdateHeadOfState(empire);
                resolved = true;
            }
        }
        if (!resolved && ParliamentSystem.ForceGovernmentCollapse(empire))
        {
            EventRecorder.Record(empire, string.Format(LM.Get("street_government_fell_log"), country));
            resolved = true;
        }
        if (!resolved && governing != null)
        {
            Actor old = governing.GetLeader();
            Actor successor = governing.AllMembers
                .Where(actor => actor != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                actor != old && actor.kingdom?.GetEmpire() == empire)
                .OrderByDescending(actor => actor.renown).FirstOrDefault();
            // 无小人模式：党内实体很少，推举一位本党理念的读书人接任
            if (successor == null && CityPopulationSystem.AbstractPopulationEnabled)
            {
                successor = PartySystem.SpawnFounder(empire, governing.Ideology);
                if (successor != null) governing.AddMember(successor);
            }
            if (successor != null)
            {
                governing.SetLeader(successor);
                RepublicSystem.UpdateHeadOfState(empire);
                ParliamentSystem.ReelectGovernment(empire);
                EventRecorder.Record(empire, string.Format(LM.Get("street_leader_resigned_log"), country,
                    old?.getName() ?? LM.Get("label_none"), successor.getName()), successor);
                resolved = true;
            }
        }
        if (!resolved) return;
        // 新政府上台先减税整顿(土地兼并严重的城市一并重分土地)，抗争回落到示威
        Concede(empire, 0.2f, 0.1);
        RedistributeWorstCities(empire, int.MaxValue);
        state.street_unrest_stage = Demonstrations;
        state.street_unrest_years = 0;
        state.repression_years = 0;
    }

    #endregion
}
