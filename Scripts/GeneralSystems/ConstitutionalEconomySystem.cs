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
    // 立宪还缺的科技(科技名，顿号隔开)
    public string missing_techs = "";
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

    // 市场交换(无小人模式，见 MarketSystem)：记作一次商队往来，城市就业的"近期航运"、资本主义萌芽都会计入
    public static void RecordTrade(City origin, City destination, bool foreign, int gold)
    {
        if (origin == null || destination == null || World.world == null || gold <= 0) return;
        Empire empire = destination.kingdom?.GetEmpire() ?? origin.kingdom?.GetEmpire();
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || empire.IsArchived() || empire.isRekt()) return;
        double now = World.world.getCurWorldTime();
        // 同一对城市一个月只记一次
        foreach (CompletedTradeVoyage voyage in state.recent_trade)
            if (voyage.origin_city_id == origin.id && voyage.destination_city_id == destination.id &&
                Date.getMonthsSince(voyage.timestamp) < 1)
            {
                voyage.delivered_gold = Math.Min(Config.max_gold_per_voyage, voyage.delivered_gold + gold);
                return;
            }
        state.recent_trade.Add(new CompletedTradeVoyage
        {
            timestamp = now,
            origin_city_id = origin.id,
            destination_city_id = destination.id,
            foreign_kingdom = foreign,
            delivered_gold = Math.Min(Config.max_gold_per_voyage, gold)
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

    // 一年约 60 秒世界时间(见 GoodsLedger.YearSeconds)
    private const double FirstYearSpreadSeconds = 60d;

    #region 年度更新（唯一会改动状态的入口）

    public static void Update(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || World.world == null ||
            empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return;
        ConstitutionalEconomyState state = Ensure(empire);
        SyncCulture(empire, state);
        SyncRegime(empire, state);
        // 玩家随时可能在政体窗口切换国家制度：共和标记跟着实际政体走
        RepublicSystem.SyncWithRegime(empire);
        // 本文化废除君主制之后才成立的君主国：强制改建现代国家
        RepublicSystem.TryModernizeLateMonarchy(empire);
        // 旧存档清理：已废除君主制的国家名下还挂着朝贡国(共和国不再接收新朝贡国，所以只会出现在旧存档里)
        if (RepublicSystem.IsRepublic(empire) && empire.taken_Kingdoms?.Count > 0)
            TributaryAbolitionService.OnMonarchyAbolished(empire);
        RepublicSystem.UpdateTransition(empire);
        RepublicSystem.EnsureIdeologyBureau(empire);
        RepublicSystem.EnsureHeadOfState(empire);
        // 现代国家的宪法：颁布/废止随国体与共和过渡阶段即时变化，条款同步每年一次
        ConstitutionSystem.Update(empire, state);
        // 分封制或时代未到的君主国：立宪作废(外力强加或上帝直接确立的除外)
        RevokeUnfitConstitution(empire, state);
        AdoptCultureConstitution(empire, state);
        // 开放党禁后：派系改组为政党、政党的年度分合(见 PartySystem)。不依赖议会，先于议会更新
        PartySystem.Update(empire, state);
        // 议会召开/改选/补选/解散。每次更新都检查，议会阶段一到立即召开，不必等年度结算
        ParliamentSystem.Update(empire);
        // 地方政治：省级政治倾向与地方选举(每年一次)
        ProvincialPoliticsSystem.Update(empire);
        // 党禁开关与选举制度(按帝国动态调整)；军阀时期(含帝国核心国号)
        PartyBanSystem.Update(empire);
        // 周制：春秋战国(每月结算一次，见 ZhouEraSystem)
        ZhouEraSystem.Update(empire);
        WarlordEraSystem.UpdateWorld();
        NationalSentimentSystem.UpdateWorld();
        // 土崩瓦解：各省响应(自带月度计时)
        ProvincialCollapseSystem.Monthly(empire);
        // 新国家的年度结算错开一年里的随机时点：现代一下子建政的一大批国家不再挤在同一时刻结算(以前一到年底就卡)
        if (state.last_economy_update < 0)
            state.last_economy_update = World.world.getCurWorldTime() - UnityEngine.Random.value * FirstYearSpreadSeconds;
        if (Date.getYearsSince(state.last_economy_update) < 1) return;
        state.last_economy_update = World.world.getCurWorldTime();
        PruneTrade(state);
        // 与别的帝国同色(分裂/延续出来的帝国沿用了原色)时换色；旧存档也在这里修正
        EmpireManager.EnsureDistinctColor(empire);
        // WarBox 罢工：怨气、正统(所有国家)，并为民意计算统计罢工占比
        PublicOpinionSystem.ApplyStrikeEffects(empire, state);
        // 苛政：前现代官逼民反，现代街头抗争逐级升级(先于民意结算，抗争推高异见)
        HarshRuleSystem.Update(empire);
        PublicOpinionSystem.Update(empire, state);
        // 国民教育：按宪法国体(立国理念)在国内扩散理念
        IdeologyDynamicsSystem.Update(empire, state);
        RestorationSystem.Update(empire, state);
        IdeologyEducationSystem.Update(empire);
        SpeechFreedomSystem.Update(empire);
        RepublicSystem.UpdateAbolitionPressure(empire);
        RepublicSystem.TryMandateCollapseCoalition(empire);
        RebellionSnowballSystem.Update(empire);
        // 内忧深重时一省首义；武官势大、正统低落时兵变
        ProvincialCollapseSystem.Yearly(empire);
        MilitaryCoupSystem.Yearly(empire);
        // 西方：教权、加冕、叙任权、什一税
        PapacySystem.Yearly(empire);
        // 天下之名：同号方位、普世之名、唯一合法政府国号、多京与直隶(见 RealmNamingSystem)
        RealmNamingSystem.Yearly(empire);
        // 伪法理转正：正统帝国已亡而据有正规核心过半
        ImperialLegitimacyChallengeService.TryLegitimizeFalseCore(empire);
        // 派系失衡：一派独大按其极端方向引发乱局(先于朝政积弊，见 FactionBalanceSystem)
        FactionBalanceSystem.Yearly(empire);
        // 朝政积弊滋生与后果
        MaladySystem.Yearly(empire);
        // 荒淫之君的宫廷开支；诛外戚
        RulerQualitySystem.Yearly(empire);
        ConsortKinSystem.Yearly(empire);
        // 禁军宫廷政变；后宫争储
        PalaceArmySystem.Yearly(empire);
        HaremSuccessionSystem.Yearly(empire);
        // 神圣罗马式选帝制：选立储君
        HolyRomanSystem.Yearly(empire);
        // 旧档修补：自治成员、军府税级为"无"(收不上税)的改回"低"；自由城市、主教领被误给的开战权收回
        TreasurySystem.RepairExemptTaxLevels(empire);
        CityStateService.RepairEstateAutonomy(empire);
        EmpireBankruptcySystem.Update(empire, state);
        ModernTitleCentralization.Update(empire);
        UpdateBudding(empire, state);
        UpdateWelfare(empire, state);
        ClaimPolicySystem.UpdateYear(empire, state);

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

    public static int GetWelfareAnnualCost(Empire empire, int level) =>
        empire?.CoreKingdom == null || level <= 0 ? 0 :
        Math.Max(5, (CityPopulationSystem.AbstractPopulationEnabled
            ? CityPopulationSystem.FiscalHouseholds(empire) : empire.CountPopulation()) / 100) * level;

    private static void UpdateWelfare(Empire empire, ConstitutionalEconomyState state)
    {
        int cost = GetWelfareAnnualCost(empire, state.welfare_level);
        state.welfare_funded = cost > 0 && empire.CoreKingdom.GetMoney() >= cost;
        if (state.welfare_funded) empire.CoreKingdom.SubMoney(cost, TreasuryCategory.Welfare);
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
                       merchantCities >= config.no_trade_merchant_cities) &&
                     // 资本主义萌芽也要有技术基础(科技树里配置，默认是印刷术)
                     TechnologySystem.CanCapitalistBud(InstitutionSystem.GetPrimaryCulture(empire));
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
            if (state.capitalist_budding_forced)
            {
                state.capitalist_decline_since = -1d;
                return;
            }
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

    // 君主立宪是文化层面的制度：本文化已经确立的话，同文化的君主制帝国直接随之确立(进行中的制宪就此结束)
    public static bool IsCultureConstitutional(Empire empire) =>
        empire != null && InstitutionSystem.GetOrCreateCultureState(InstitutionSystem.GetPrimaryCulture(empire))
            ?.constitutional_monarchy == true;

    public static CultureInstitutionState GetCultureConstitution(Empire empire) =>
        empire == null ? null : InstitutionSystem.GetOrCreateCultureState(InstitutionSystem.GetPrimaryCulture(empire));

    private static void AdoptCultureConstitution(Empire empire, ConstitutionalEconomyState state)
    {
        // 旧存档：本帝国早已立宪但文化还没记上 → 补记为本文化首开
        if (state.constitutional_monarchy && IsMonarchy(empire) && !IsCultureConstitutional(empire))
            MarkCultureConstitutional(empire);
        if (state.constitutional_monarchy || !IsMonarchy(empire) || !IsCultureConstitutional(empire)) return;
        if (!InstitutionDefinitionRegistry.IsConstitutionEnabled(GetLine(empire)) || EraBlocker(empire) != null) return;
        CancelReform(state);
        state.constitutional_monarchy = true;
        CultureInstitutionState culture = GetCultureConstitution(empire);
        string content = string.Format(LM.Get("constitution_culture_adopted_history"),
            InstitutionSystem.GetPrimaryCulture(empire).GetCultureTranslate(),
            string.IsNullOrWhiteSpace(culture?.constitutional_monarchy_pioneer_name)
                ? LM.Get("label_none") : culture.constitutional_monarchy_pioneer_name);
        empire.RecordHistory(directContent: content, actorId: empire.Emperor?.id ?? -1L,
            kingdomId: empire.CoreKingdom.id);
    }

    private static void MarkCultureConstitutional(Empire empire)
    {
        CultureInstitutionState culture = GetCultureConstitution(empire);
        if (culture == null || culture.constitutional_monarchy) return;
        culture.constitutional_monarchy = true;
        culture.constitutional_monarchy_timestamp = World.world.getCurWorldTime();
        culture.constitutional_monarchy_pioneer_empire_id = empire.getID();
        culture.constitutional_monarchy_pioneer_name = empire.GetEmpireName();
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(
            string.Format(LM.Get("constitution_culture_established_log"), empire.GetEmpireName(),
                InstitutionSystem.GetPrimaryCulture(empire).GetCultureTranslate()), empire.CoreKingdom);
    }

    // 立宪须到火药时代，且不行分封制(诸侯各自为政，君权分在列国，无从由一部宪法统一限制)；
    // 同文化别国已立宪也一样：不够格的不随之立宪，已经立了的作废。
    // 例外：战败后被战胜国强行改造(见 ImposeModernization)，或上帝模式直接确立。
    public const int ConstitutionEraTier = 6;

    public static bool IsForcedModernized(Empire empire) => Ensure(empire)?.forced_modernization == true;

    public static string ForcedByCulture(Empire empire) => Ensure(empire)?.forced_by_culture ?? "";

    // 立宪的时代门槛：不够格返回原因(本地化键)，够格返回 null
    public static string EraBlocker(Empire empire)
    {
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || empire.CoreKingdom == null || state.forced_modernization || state.constitution_gate_exempt)
            return null;
        if (empire.CoreKingdom.GetRegime()?.type == RegimeType.ZhouFeudalism) return "constitution_requires_centralization";
        if (TechnologySystem.GetEraTier(InstitutionSystem.GetPrimaryCulture(empire)) < ConstitutionEraTier)
            return "constitution_requires_era";
        return null;
    }

    private static void RevokeUnfitConstitution(Empire empire, ConstitutionalEconomyState state)
    {
        string blocker = EraBlocker(empire);
        if (blocker == null || !IsMonarchy(empire)) return;
        CancelReform(state);
        if (!state.constitutional_monarchy) return;
        state.constitutional_monarchy = false;
        Record(empire, blocker == "constitution_requires_centralization"
            ? "constitution_revoked_feudal_history" : "constitution_revoked_era_history");
    }

    // 被迫现代化(如战败后被战胜国改造)：大败于立宪、共和或现代国家之手，丢了都城或四分之一以上的城，
    // 战胜国就强行替它立宪、开放党禁——不受分封制与时代门槛限制，也不算本文化首开立宪
    public static bool ImposeModernization(Empire loser, Kingdom victor, int lostCities, bool lostCapital)
    {
        ConstitutionalEconomyState state = Ensure(loser);
        if (state == null || World.world == null || loser.CoreKingdom == null || victor?.data == null || victor.isRekt() ||
            state.constitutional_monarchy || state.forced_modernization || !IsMonarchy(loser) ||
            !InstitutionDefinitionRegistry.IsConstitutionEnabled(GetLine(loser))) return false;
        int cities = loser.CoreKingdom.cities?.Count ?? 0;
        if (!lostCapital && (lostCities <= 0 || lostCities * 4 < cities + lostCities)) return false;
        Empire master = victor.GetEmpire();
        Kingdom core = master?.CoreKingdom ?? victor;
        string culture = master != null ? InstitutionSystem.GetPrimaryCulture(master) : TechnologySystem.GetCultureOf(core);
        if (TechnologySystem.GetEraTier(culture) < ConstitutionEraTier) return false;
        bool modern = ModernStability.IsModern(core) ||
                      master != null && (HasConstitution(master) || RepublicSystem.IsRepublic(master));
        if (!modern) return false;
        CancelReform(state);
        state.constitutional_monarchy = true;
        state.forced_modernization = true;
        state.forced_by_name = master?.GetEmpireName() ?? core.GetKingdomName();
        state.forced_by_culture = culture ?? "";
        state.forced_at = World.world.getCurWorldTime();
        ParliamentSystem.Update(loser);
        loser.RecordHistory(directContent: string.Format(LM.Get("constitution_imposed_history"), state.forced_by_name),
            actorId: loser.Emperor?.id ?? -1L, kingdomId: loser.CoreKingdom.id);
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.LogEventMessage(
            string.Format(LM.Get("constitution_imposed_log"), state.forced_by_name, loser.GetEmpireName()), loser.CoreKingdom);
        return true;
    }

    private static void CancelReform(ConstitutionalEconomyState state)
    {
        state.constitutional_reform_active = false;
        state.constitutional_reform_progress = 0f;
        state.constitutional_reform_stage = 0;
    }

    #endregion

    // 无小人模式下背景人口折算户数时每户的人数
    private const int PeoplePerBackgroundHousehold = 4;

    private static void CountHouseholds(Empire empire, out int households, out int merchants,
        out int merchantCities)
    {
        var seen = new HashSet<string>();
        var merchantKeys = new HashSet<string>();
        float backgroundHouseholds = 0f;
        float backgroundMerchants = 0f;
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
            // 无小人模式：背景人口按每户 PeoplePerBackgroundHousehold 人折算成户数，商人阶层折算成商户
            if (CityPopulationSystem.AbstractPopulationEnabled)
            {
                var background = new Dictionary<SocialClass, int>();
                CityPopulationSystem.AddBackgroundCounts(city, background, group => group.social_class);
                int people = 0;
                foreach (int count in background.Values) people += count;
                backgroundHouseholds += people / (float)PeoplePerBackgroundHousehold;
                if (background.TryGetValue(SocialClass.Merchant, out int traders) && traders > 0)
                {
                    backgroundMerchants += traders / (float)PeoplePerBackgroundHousehold;
                    if (!foundMerchant) merchantCities++;
                }
            }
            if (foundMerchant) merchantCities++;
        }
        households = seen.Count + UnityEngine.Mathf.RoundToInt(backgroundHouseholds);
        merchants = merchantKeys.Count + UnityEngine.Mathf.RoundToInt(backgroundMerchants);
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

    // 立宪所需、而本文化尚未研究的科技(科技树没启用时不限制)
    public static List<string> GetMissingTechs(Empire empire)
    {
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        if (!TechnologySystem.IsActiveFor(culture) || empire?.CoreKingdom == null) return new List<string>();
        // 本文化发明、本国也采纳了才算；旧制犹存的旧制所属文化发明的也算(见 TechnologySystem.RealmHasInstitutionTech)
        return TechnologySystem.RealmMissingInstitutionTechs(empire.CoreKingdom,
            InstitutionDefinitionRegistry.GetConstitutionRequiredTechs(GetLine(empire)));
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
        if (view.blocker == "constitution_requires_tech")
            view.missing_techs = string.Join(LocalizedTextManager.instance?.language is "cz" or "ch" ? "、" : ", ",
                GetMissingTechs(empire).Select(TechnologySystem.GetTechName));
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
        if (IsMonarchy(empire) && EraBlocker(empire) is string blocker) { reason = blocker; return false; }
        // 本文化已确立君主立宪：下一次更新自动随之确立，不必再走一遍制宪
        if (IsCultureConstitutional(empire)) { reason = "constitution_culture_enacted"; return false; }
        if (state.constitutional_reform_active) { reason = "constitution_reforming"; return false; }
        if (!IsMonarchy(empire)) { reason = "constitution_requires_monarchy"; return false; }
        if (empire.data.institution_state?.active_reform != null)
        { reason = "constitution_other_reform"; return false; }
        if (!state.capitalist_budding) { reason = "constitution_requires_budding"; return false; }
        if (GetStableCultureYears(empire, state) < Config.stable_culture_years ||
            state.stable_culture_since < 0)
        { reason = "constitution_requires_stability"; return false; }
        if (GetMissingFeatures(empire).Count > 0) { reason = "constitution_requires_institutions"; return false; }
        if (GetMissingTechs(empire).Count > 0) { reason = "constitution_requires_tech"; return false; }
        if (InstitutionDefinitionRegistry.IsConstitutionRequiringCompositeEmpire(line) &&
            !CompositeEmpireService.IsMultiethnic(empire))
        { reason = "constitution_requires_composite"; return false; }
        if (GetParliamentarySupport(empire) < Config.support_threshold)
        { reason = "constitution_requires_support"; return false; }
        reason = "";
        return true;
    }

    #endregion

    // 上帝模式：跳过商人家庭/海贸/年限条件，直接出现资本主义萌芽。强制出现的萌芽带标记，
    // 之后即使商业条件不达标也不会消退(否则几年后又自己退回去，按钮等于白点)。
    public static bool ForceBudding(Empire empire)
    {
        ConstitutionalEconomyState state = Ensure(empire);
        if (state == null || World.world == null || state.capitalist_budding) return false;
        state.capitalist_budding = true;
        state.capitalist_budding_forced = true;
        state.capitalist_decline_since = -1d;
        Record(empire, "constitution_budding_history");
        return true;
    }

    // 上帝模式：跳过全部条件和制宪过程，直接确立君主立宪(同样记为本文化首开，同文化君主国随之立宪)
    public static bool CanForceConstitution(Empire empire)
    {
        ConstitutionalEconomyState state = Ensure(empire);
        return state != null && World.world != null && !state.constitutional_monarchy && IsMonarchy(empire) &&
               InstitutionDefinitionRegistry.IsConstitutionEnabled(GetLine(empire));
    }

    public static bool ForceConstitution(Empire empire)
    {
        if (!CanForceConstitution(empire)) return false;
        ConstitutionalEconomyState state = Ensure(empire);
        CancelReform(state);
        state.constitutional_monarchy = true;
        state.constitution_gate_exempt = true;
        MarkCultureConstitutional(empire);
        ParliamentSystem.Update(empire);
        Record(empire, "constitution_forced_history");
        return true;
    }

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
        MarkCultureConstitutional(empire);
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
