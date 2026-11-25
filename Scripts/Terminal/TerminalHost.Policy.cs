using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 手机上的施政与营建(用户 2026-11-25 "玩家可以进行的操作也多一点，大方向的和君臣博弈的""甚至是建设国家""让国家一步步繁荣的")：
//   · 施政页头一签「国策」(state.govern，只给一国之主)：税率滑块(白线是苛政线)、通商政策、移民与钱法、产业归属、工会、劳动立法、
//     国民教育、言论、党禁——都是游戏里已有的国策，这里只是给了一处统一摆弄的地方：先摆弄，点「颁行」才一道道下政令(一个月后办，到时再验)；
//   · 营建页(state.build，君主都有)：国运(时代轨道、进下一时代还差什么、在研的科技、定主攻方向)、诸城(繁荣、政令、安定三条读数，
//     点「营建」进这座城的营建格)、在修的官道、百业产需(本国所在市场各货的产与需、短缺的货)；
//   · 一座城的营建格(/api/firms，id 为 city:城编号)：立村、设镇、各类厂区。国库出钱(立村 30、设镇 120、厂区 200)，建成的厂区是官营；
//     地方由游戏挑(城郊没盖过的地块，由近到远试)，放不下不扣钱。
// 自家的事不打折。面板一份备好用几秒(手机几秒一问)，免得回回重算
public static partial class TerminalHost
{
    private const int PolicyTaxMin = 5, PolicyTaxMax = 60, PolicyTaxStep = 5, MaxBuildCities = 6, MaxBuildTypes = 7, FactoryBuildCost = 200;
    private const float PanelSeconds = 3f;

    private sealed class PanelCache
    {
        public readonly Dictionary<string, (float at, JObject json)> Made = new();
    }

    private static readonly ConditionalWeakTable<Seat, PanelCache> Panels = new();

    private static JObject CachedPanel(Seat seat, string kind, Func<JObject> make)
    {
        PanelCache cache = Panels.GetOrCreateValue(seat);
        float now = Time.realtimeSinceStartup;
        if (cache.Made.TryGetValue(kind, out (float at, JObject json) held) && now - held.at < PanelSeconds && now >= held.at) return held.json;
        JObject json = make();
        cache.Made[kind] = (now, json);
        return json;
    }

    // 下了令、办了事：这位君主的面板下回重新备
    private static void DropPanels(Seat seat)
    {
        if (seat != null && Panels.TryGetValue(seat, out PanelCache cache)) cache.Made.Clear();
    }

    // ───────── 施政 · 国策 ─────────
    private static JObject GovernJson(Seat seat) => CachedPanel(seat, "govern", () => BuildGovern(seat));

    private static JObject BuildGovern(Seat seat)
    {
        Empire empire = OwnEmpire(seat);
        Kingdom core = empire?.CoreKingdom;
        if (empire?.data == null || core?.data == null) return null;
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        var cards = new List<JObject>();

        // 赋税与民力：白线是苛政线(实收税率过二成五起算苛政，政体自带的加征已经折进去)
        JObject tax = XCard(LM.Get("terminal_policy_tax_card"), "k_treasury");
        int rate = Mathf.Clamp(Mathf.RoundToInt(empire.data.TaxRate * 100f / PolicyTaxStep) * PolicyTaxStep, PolicyTaxMin, 100);
        int line = Mathf.RoundToInt(25f - ((float)core.GetTaxRate() - empire.data.TaxRate) * 100f);
        tax.XAdd(XSlider("policy:tax", LM.Get("terminal_policy_tax"), rate, PolicyTaxMin, Math.Max(PolicyTaxMax, rate), PolicyTaxStep, "%", "x_wealth",
            null, line > PolicyTaxMin && line < PolicyTaxMax ? line : int.MinValue,
            LM.Get(line <= PolicyTaxMin ? "terminal_policy_tax_sub_over" : "terminal_policy_tax_sub"),
            ConstitutionalEconomySystem.CanChangeTax(empire) ? null : LM.Get("fiscal_tax_locked")));
        float grievance = PopularGrievance.Index(core);
        tax.XAdd(XGauge(LM.Get("terminal_policy_grievance"), grievance, 100f, Mathf.RoundToInt(grievance).ToString(),
            grievance >= 60f ? "b" : grievance >= 30f ? null : "g", "x_people", 60f));
        int authority = Authority(core);
        tax.XAdd(XGauge(LM.Get("terminal_policy_authority"), authority, 100f, authority.ToString(), "i", "k_mandate"));
        int waiting = seat.Orders.Count(order => order.Kind == "policy" && order.State == "sent");
        if (waiting > 0) tax["note"] = string.Format(LM.Get("terminal_policy_waiting"), waiting);
        cards.Add(tax);

        // 通商、移民、钱法
        JObject trade = XCard(LM.Get("terminal_policy_trade_card"), "x_market");
        TradePolicy policy = CommodityMarketSystem.PolicyOf(core);
        Kingdom leader = CommodityMarketSystem.MarketOf(core)?.leader;
        string follows = leader != null && leader != core ? string.Format(LM.Get("terminal_policy_trade_member"), TerminalIntel.Strip(leader.GetKingdomFullName())) : null;
        trade.XAdd(XSeg(LM.Get("terminal_policy_trade"), "d_trade",
            new[] { TradePolicy.Free, TradePolicy.Mercantile, TradePolicy.Protective, TradePolicy.Closed }.Select(option =>
            {
                string blocked = option == policy ? null : follows ?? LM.Get(CommodityMarketSystem.CanSetPolicy(core, option) ?? "");
                return XOpt("policy:trade:" + option, LM.Get("terminal_policy_trade_" + option.ToString().ToLowerInvariant()), option == policy,
                    LM.Get("terminal_policy_trade_" + option.ToString().ToLowerInvariant() + "_tip"), string.IsNullOrEmpty(blocked) ? null : blocked);
            })));
        if (state != null)
        {
            trade.XAdd(XSeg(LM.Get("terminal_policy_migration"), "x_people", new[] { -1, 0, 1 }.Select(level =>
                XOpt("policy:migration:" + level, ClaimPolicySystem.MigrationPolicyText(level), level == state.migration_policy,
                    LM.Get("terminal_policy_migration_" + (level + 1))))));
            trade.XAdd(XSeg(LM.Get("terminal_policy_finance"), "x_wealth", new[] { -1, 0, 1 }.Select(level =>
                XOpt("policy:finance:" + level, ClaimPolicySystem.FinancePolicyText(level), level == state.finance_policy,
                    LM.Get("terminal_policy_finance_" + (level + 1))))));
        }
        cards.Add(trade);

        // 产业与劳工(工业时代以后)
        if (state != null && ModernPolicySystem.Industrial(empire))
        {
            JObject industry = XCard(LM.Get("terminal_policy_industry_card"), "x_factory");
            industry.XAdd(XSeg(LM.Get("terminal_modern_ownership"), "x_factory", new[] { -1, 0, 1 }.Select(level =>
                XOpt("policy:own:" + level, ModernPolicySystem.NationalizationName(level), level == state.nationalization,
                    LM.Get("terminal_policy_own_" + (level + 1))))));
            industry.XAdd(XSeg(LM.Get("terminal_modern_union"), "x_works", new[] { LabourSystem.Banned, LabourSystem.Tolerated, LabourSystem.Legal }.Select(level =>
                XOpt("policy:union:" + level, LabourSystem.UnionStatusName(level), level == state.union_status,
                    LM.Get("terminal_policy_union_" + (level + 1))))));
            JObject labour = XSlider("policy:labour", LM.Get("terminal_policy_labour"), state.labour_law, 0, LabourSystem.MaxLabourLaw, 1, null, "g_law", "g",
                sub: LM.Get("terminal_policy_labour_sub"));
            labour["lo"] = state.labour_law;
            industry.XAdd(labour);
            JObject school = XSlider("policy:education", LM.Get("terminal_modern_education"), state.education_level, 0, ModernPolicySystem.MaxEducation, 1,
                null, "x_edify", "i", subs: Enumerable.Range(0, ModernPolicySystem.MaxEducation + 1).Select(level => level == 0
                    ? LM.Get("terminal_policy_education_none")
                    : string.Format(LM.Get("terminal_policy_education_sub"), ModernPolicySystem.EducationCost(empire, level))));
            school["lo"] = state.education_level;
            industry.XAdd(school);
            cards.Add(industry);
        }

        // 法度：言论(要有正式的宪法)、党禁(要有党派政治)
        JObject law = XCard(LM.Get("terminal_policy_law_card"), "g_law");
        if (ConstitutionSystem.Options(empire, ConstitutionSystem.ClauseSpeech).Count > 0)
        {
            ConstitutionSpeech speech = ConstitutionSystem.GetSpeech(empire);
            law.XAdd(XSeg(LM.Get("constitution_clause_speech"), "a_history", new[] { ConstitutionSpeech.Strict, ConstitutionSpeech.Limited, ConstitutionSpeech.Free }
                .Select(option => XOpt("policy:speech:" + option, LM.Get("terminal_policy_speech_" + option), option == speech,
                    ConstitutionSystem.ValueText(ConstitutionSystem.ClauseSpeech, option.ToString())))));
        }
        if (PartySystem.IsActive(empire))
        {
            bool closed = PartyBanSystem.IsClosed(empire);
            law.XAdd(XSwitch("policy:ban:" + (closed ? "open" : "close"), LM.Get("terminal_policy_ban"), closed, LM.Get("terminal_policy_ban_sub"),
                closed || PartyBanSystem.CanCloseManually(empire, out _, out _) ? null : LM.Get("terminal_policy_ban_off"), "g_court"));
        }
        cards.Add(law);
        return XPanelOf(LM.Get("terminal_policy_title"), cards);
    }

    private static bool PolicyOrder(Seat seat, string id, Order made)
    {
        if (!id.StartsWith("policy:", StringComparison.Ordinal)) return false;
        Empire empire = OwnEmpire(seat);
        if (empire?.data == null) return false;
        string[] parts = id.Split(':');
        if (parts.Length != 3) return false;
        string what, value;
        switch (parts[1])
        {
            case "tax":
                if (!int.TryParse(parts[2], out int rate) || rate < PolicyTaxMin || rate > 100) return false;
                what = LM.Get("terminal_policy_tax");
                value = rate + "%";
                break;
            case "trade":
                if (!Enum.TryParse(parts[2], out TradePolicy policy)) return false;
                what = LM.Get("terminal_policy_trade");
                value = CommodityMarketSystem.PolicyName(policy);
                break;
            case "migration":
            case "finance":
            case "own":
            case "union":
                if (!int.TryParse(parts[2], out int level) || level < -1 || level > 1) return false;
                what = LM.Get(parts[1] == "migration" ? "terminal_policy_migration" : parts[1] == "finance" ? "terminal_policy_finance"
                    : parts[1] == "own" ? "terminal_modern_ownership" : "terminal_modern_union");
                value = parts[1] == "migration" ? ClaimPolicySystem.MigrationPolicyText(level) : parts[1] == "finance" ? ClaimPolicySystem.FinancePolicyText(level)
                    : parts[1] == "own" ? ModernPolicySystem.NationalizationName(level) : LabourSystem.UnionStatusName(level);
                break;
            case "labour":
            case "education":
                if (!int.TryParse(parts[2], out int step) || step < 1 || step > 3) return false;
                what = LM.Get(parts[1] == "labour" ? "terminal_policy_labour" : "terminal_modern_education");
                value = string.Format(LM.Get("terminal_policy_level"), step);
                break;
            case "speech":
                if (!Enum.TryParse(parts[2], out ConstitutionSpeech speech)) return false;
                what = LM.Get("constitution_clause_speech");
                value = LM.Get("terminal_policy_speech_" + speech);
                break;
            case "ban":
                if (parts[2] is not ("open" or "close")) return false;
                what = LM.Get("terminal_policy_ban");
                value = LM.Get(parts[2] == "close" ? "terminal_policy_ban_on" : "terminal_policy_ban_lift");
                break;
            default:
                return false;
        }
        made.Kind = "policy";
        made.Arg = id.Substring(7);
        made.Text = string.Format(LM.Get("terminal_order_policy"), what, value);
        DropPanels(seat);
        return true;
    }

    private static string PolicyExecute(Seat seat, Order order)
    {
        DropPanels(seat);
        Empire empire = OwnEmpire(seat);
        Kingdom core = empire?.CoreKingdom;
        string[] parts = (order.Arg ?? "").Split(':');
        if (empire?.data == null || core == null || parts.Length != 2) return LM.Get("terminal_fail_generic");
        ConstitutionalEconomyState state = empire.data.constitutional_economy;
        int.TryParse(parts[1], out int level);
        switch (parts[0])
        {
            case "tax":
            {
                if (!ConstitutionalEconomySystem.CanChangeTax(empire)) return LM.Get("fiscal_tax_locked");
                float delta = level / 100f - empire.data.TaxRate;
                if (delta > 0.001f) empire.AddTaxRate(delta);
                else if (delta < -0.001f) empire.SubTaxRate(-delta);
                return null;
            }
            case "trade":
            {
                if (!Enum.TryParse(parts[1], out TradePolicy policy)) return LM.Get("terminal_fail_generic");
                Kingdom leader = CommodityMarketSystem.MarketOf(core)?.leader;
                if (leader != null && leader != core)
                    return string.Format(LM.Get("terminal_policy_trade_member"), TerminalIntel.Strip(leader.GetKingdomFullName()));
                string blocked = CommodityMarketSystem.SetPolicy(core, policy);
                return blocked == null ? null : LM.Get(blocked);
            }
            case "migration":
                if (state == null) return LM.Get("terminal_fail_generic");
                ClaimPolicySystem.SetMigrationPolicy(empire, level);
                return null;
            case "finance":
                if (state == null) return LM.Get("terminal_fail_generic");
                ClaimPolicySystem.SetFinancePolicy(empire, level);
                return null;
            case "own":
            {
                if (state == null || !ModernPolicySystem.Industrial(empire)) return LM.Get("terminal_fail_generic");
                int income = 0;
                for (int guard = 0; guard < 2 && state.nationalization != level; guard++)
                    income += ModernPolicySystem.ShiftOwnership(empire, level > state.nationalization ? 1 : -1);
                if (income > 0) order.Note = string.Format(LM.Get("terminal_policy_own_income"), income);
                return null;
            }
            case "union":
                if (state == null || !ModernPolicySystem.Industrial(empire)) return LM.Get("terminal_fail_generic");
                LabourSystem.SetUnionStatus(empire, level);
                return null;
            case "labour":
                if (state == null || !ModernPolicySystem.Industrial(empire)) return LM.Get("terminal_fail_generic");
                while (state.labour_law < level && LabourSystem.RaiseLabourLaw(empire))
                {
                }
                return null;
            case "education":
                if (state == null || !ModernPolicySystem.Industrial(empire)) return LM.Get("terminal_fail_generic");
                while (state.education_level < level && ModernPolicySystem.RaiseEducation(empire))
                {
                }
                return null;
            case "speech":
                return ConstitutionSystem.SetByPlayer(empire, ConstitutionSystem.ClauseSpeech, parts[1]) ? null : LM.Get("terminal_policy_speech_fail");
            case "ban":
                if (parts[1] == "close") return PartyBanSystem.CloseManually(empire) ? null : LM.Get("terminal_policy_ban_off");
                return PartyBanSystem.OpenManually(empire) ? null : LM.Get("terminal_fail_generic");
        }
        return LM.Get("terminal_fail_generic");
    }

    // ───────── 营建 ─────────
    private static JObject BuildJson(Seat seat) => CachedPanel(seat, "build", () => BuildBuild(seat));

    private static JObject BuildBuild(Seat seat)
    {
        Kingdom realm = seat.Realm;
        if (realm?.data == null) return null;
        string culture = TechnologySystem.GetCultureOf(realm);
        return XPanelOf(LM.Get("terminal_build_title"), new[]
        {
            string.IsNullOrEmpty(culture) ? null : EraCard(seat, culture), CitiesCard(realm), WorksCard(realm), GoodsCard(realm)
        });
    }

    // 国运：时代轨道(前后各看两格)、进下一时代还差什么、在研的科技、定主攻
    private static JObject EraCard(Seat seat, string culture)
    {
        int tier = TechnologySystem.GetEraTier(culture), top = TechnologySystem.MaxEraTier;
        JObject card = XCard(LM.Get("terminal_build_era_card"), "i_era", "o");
        card["tag"] = TechnologySystem.EraName(tier);
        card["tagTone"] = "o";
        int from = Math.Max(0, Math.Min(tier - 2, top - 4));
        var steps = new List<string>();
        for (int step = from; step <= Math.Min(top, from + 4); step++) steps.Add(TechnologySystem.EraName(step));
        card.XAdd(XTrack(steps, tier - from));
        TechEraConfig next = TechnologySystem.GetNextEra(culture, out int have, out int need, out bool nodeDone);
        CultureTechState techs = TechnologySystem.GetState(culture);
        if (next != null)
        {
            card.XAdd(XGauge(LM.Get("terminal_build_era_stock"), have, Math.Max(1, need), have + " / " + need, have >= need ? "g" : null, "x_tech"));
            string nodes = string.Join(" / ", next.node.Select(TechnologySystem.GetTechName));
            card.XAdd(XTodo(new[]
            {
                (string.Format(LM.Get("terminal_build_era_node"), nodes), nodeDone),
                (string.Format(LM.Get("terminal_build_era_prior"), need), have >= need),
                (string.Format(LM.Get("terminal_build_era_level"), TechnologySystem.GetRequiredCultureLevel(next.tier)),
                    TechnologySystem.GetMaxTierForCulture(culture) >= next.tier)
            }));
        }
        else card.XAdd(XNote(LM.Get("terminal_build_era_last")));
        if (techs?.current_techs != null && techs.current_techs.Count > 0)
        {
            card.XAdd(XHead(LM.Get("terminal_build_research")));
            Dictionary<string, TechNodeConfig> known = TechnologySystem.GetResearchTargets(culture).ToDictionary(tech => tech.id, tech => tech);
            foreach (string id in techs.current_techs.Take(3))
            {
                if (!known.TryGetValue(id, out TechNodeConfig tech)) continue;
                float cost = Math.Max(1f, TechnologySystem.GetCost(culture, tech)), done = TechnologySystem.GetProgress(culture, id);
                card.XAdd(XGauge(TechnologySystem.GetTechName(id), done, cost, Mathf.RoundToInt(Mathf.Clamp01(done / cost) * 100f) + "%",
                    "i", null, -1f, techs.player_targets != null && techs.player_targets.Contains(id) ? LM.Get("terminal_build_research_aimed") : null));
            }
        }
        if (next != null && !nodeDone)
        {
            string node = next.node.FirstOrDefault(id => !TechnologySystem.HasTech(culture, id));
            if (node != null)
                card.XAdd(XActs(new[]
                {
                    ModAct("build:research:" + node, string.Format(LM.Get("terminal_build_aim"), TechnologySystem.GetTechName(node)),
                        LM.Get("terminal_build_aim_tip"),
                        techs?.player_targets != null && techs.player_targets.Contains(node) ? LM.Get("terminal_build_research_aimed") : null)
                }));
        }
        return card;
    }

    private static IEnumerable<City> OwnCities(Kingdom realm) => realm.cities == null
        ? Enumerable.Empty<City>()
        : realm.cities.Where(city => city?.data != null && !city.isRekt())
            .OrderByDescending(city => city == realm.capital).ThenByDescending(city => city.getPopulationPeople());

    // 诸城：繁荣、政令、安定三条读数，点「营建」进这座城的营建格
    private static JObject CitiesCard(Kingdom realm)
    {
        JObject card = XCard(LM.Get("terminal_build_cities_card"), "x_city");
        foreach (City city in OwnCities(realm).Take(MaxBuildCities))
        {
            float wealth = CityConstructionSystem.Wealth(city) * 100f, reach = AdministrativeControlSystem.Get(city) * 100f,
                calm = CityStabilitySystem.Effective(city);
            var bars = new JArray
            {
                new JObject { ["label"] = LM.Get("terminal_build_prosperity"), ["v"] = Mathf.RoundToInt(wealth), ["tone"] = "g", ["icon"] = "x_flourish" },
                new JObject { ["label"] = LM.Get("terminal_build_reach"), ["v"] = Mathf.RoundToInt(reach), ["tone"] = "i", ["icon"] = "x_govern" },
                new JObject { ["label"] = LM.Get("terminal_build_calm"), ["v"] = Mathf.RoundToInt(calm), ["tone"] = calm < 40f ? "b" : null, ["icon"] = "k_trust" }
            };
            JObject open = XChip(null, LM.Get("terminal_build_go"), "tab_build");
            open["open"] = "city:" + city.data.id;
            card.XAdd(new JObject
            {
                ["t"] = "who", ["icon"] = city == realm.capital ? "i_capital" : "i_city", ["name"] = city.GetCityName(),
                ["sub"] = string.Format(LM.Get("terminal_build_city_sub"), TerminalIntel.RoundSig(city.getPopulationPeople(), 2),
                    FactorySystem.Count(city), RuralSettlementSystem.InCity(city).Count),
                ["tag"] = city == realm.capital ? LM.Get("terminal_build_capital") : null, ["tagTone"] = "o",
                ["bars"] = bars, ["chips"] = new JArray(open)
            });
        }
        return card;
    }

    // 在修的官道(本国出钱的)
    private static JObject WorksCard(Kingdom realm)
    {
        JObject card = XCard(LM.Get("terminal_build_works_card"), "x_road");
        int shown = 0;
        foreach (KeyValuePair<(long, long), RoadLink> pair in RoadBuildingSystem.All)
        {
            if (pair.Value == null || pair.Value.building <= 0) continue;
            City from = World.world.cities.get(pair.Key.Item1), to = World.world.cities.get(pair.Key.Item2);
            if (from?.data == null || to?.data == null || from.kingdom != realm && to.kingdom != realm) continue;
            if (++shown > 5) break;
            card.XAdd(XRow(from.GetCityName() + " — " + to.GetCityName(), string.Format(LM.Get("terminal_build_road_building"), pair.Value.building),
                "a", null, "x_road"));
        }
        if (shown == 0) card.XAdd(XNote(LM.Get("terminal_build_works_none")));
        return card;
    }

    // 百业：本国所在市场里要得最多的几样货，产多少、要多少；短缺的单列
    private static JObject GoodsCard(Kingdom realm)
    {
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(realm);
        if (market == null) return null;
        JObject card = XCard(LM.Get("terminal_build_goods_card"), "x_market");
        card["tag"] = CommodityMarketSystem.PolicyName(market.policy);
        float Of(Dictionary<string, float> book, string id) => book.TryGetValue(id, out float value) ? value : 0f;
        List<CommodityConfig> goods = CommodityMarketSystem.AllGoods.Where(good => Of(market.demand, good.id) > 0f || Of(market.supply, good.id) > 0f)
            .OrderByDescending(good => Of(market.demand, good.id)).Take(6).ToList();
        if (goods.Count == 0) return null;
        card.XAdd(XBars(goods.Select(good => (DiplomacyLedger.GoodName(good), (string)null, new[] { Of(market.supply, good.id), Of(market.demand, good.id) })),
            new[] { (LM.Get("terminal_build_made"), "g"), (LM.Get("terminal_build_wanted"), "o") }));
        foreach (CommodityConfig good in CommodityMarketSystem.AllGoods.Where(good => Of(market.shortage, good.id) >= 0.15f)
                     .OrderByDescending(good => Of(market.shortage, good.id)).Take(3))
            card.XAdd(XRow(DiplomacyLedger.GoodName(good), string.Format(LM.Get("terminal_build_short"), Mathf.RoundToInt(Of(market.shortage, good.id) * 100f)), "b"));
        return card;
    }

    // ───────── 一座城的营建格(/api/firms，id 为 city:城编号) ─────────
    private static City OwnCity(Seat seat, string raw) =>
        long.TryParse(raw, out long id) && World.world.cities.get(id) is { } city && city.data != null && !city.isRekt() && city.kingdom == seat.Realm
            ? city : null;

    private static int BuildCost(string kind) => kind == "village" ? RuralSettlementSystem.VillageCost
        : kind == "town" ? RuralSettlementSystem.TownCost : FactoryBuildCost;

    private static string BuildName(string kind) => kind.StartsWith("factory:", StringComparison.Ordinal)
        ? FactorySystem.TypeName(kind.Substring(8)) : LM.Get("terminal_build_" + kind);

    private static JObject CityBuildPage(Seat seat, string what)
    {
        var rows = new JArray();
        var page = new JObject { ["ready"] = true, ["rows"] = rows, ["acts"] = new JArray(), ["title"] = "" };
        City city = OwnCity(seat, what.Substring(5));
        if (city == null) return page;
        Kingdom realm = seat.Realm;
        int money = realm.GetMoney();
        page["title"] = city.GetCityName();
        page["note"] = string.Format(LM.Get("terminal_build_sheet_note"), TerminalIntel.RoundSig(money, 3));
        var list = new JArray();
        var kinds = new List<string> { "village", "town" };
        // 厂种：有了近代的门类就只列近代的；按眼下这座城办哪种厂更有赚头排
        List<string> types = FactorySystem.Unlocked(city);
        if (types.Any(FactorySystem.IsModern)) types = types.Where(FactorySystem.IsModern).ToList();
        kinds.AddRange(types.OrderByDescending(type => CommodityMarketSystem.FactoryAppeal(city, type)).Take(MaxBuildTypes).Select(type => "factory:" + type));
        foreach (string kind in kinds)
        {
            int cost = BuildCost(kind);
            bool factory = kind.StartsWith("factory:", StringComparison.Ordinal);
            JObject option = XChip("build:" + city.data.id + ":" + kind, BuildName(kind), factory ? "f_" + kind.Substring(8) : "x_" + kind,
                string.Format(LM.Get(factory ? "terminal_build_factory_note" : "terminal_build_" + kind + "_note"), cost),
                !PureMapMode.Enabled ? LM.Get("settlement_need_pure_map") : money < cost ? LM.Get("terminal_build_poor") : null);
            option["cost"] = cost.ToString();
            list.Add(option);
        }
        rows.Add(new JObject { ["t"] = "build", ["name"] = city.GetCityName(), ["list"] = list });
        List<FactorySite> sites = FactorySystem.InCity(city);
        if (sites.Count > 0) rows.Add(new JObject { ["head"] = LM.Get("terminal_build_existing") });
        foreach (FactorySite site in sites.Take(8))
            rows.Add(ModLine(TerminalIntel.Strip(FactorySystem.Label(site)), string.Join("、", site.units.GroupBy(unit => unit.type)
                .Select(group => FactorySystem.TypeName(group.Key) + (group.Count() > 1 ? "×" + group.Count() : ""))),
                LM.Get(site.state ? "terminal_build_state_run" : "terminal_build_private_run"), site.state ? "i" : ""));
        return page;
    }

    private static bool BuildOrder(Seat seat, string id, Order made)
    {
        if (!id.StartsWith("build:", StringComparison.Ordinal) || seat.Realm == null) return false;
        string[] parts = id.Split(new[] { ':' }, 3);
        if (parts.Length != 3) return false;
        if (parts[1] == "research")
        {
            if (!LM.Has("tech_" + parts[2])) return false;
            made.Text = string.Format(LM.Get("terminal_build_aim"), TechnologySystem.GetTechName(parts[2]));
        }
        else
        {
            City city = OwnCity(seat, parts[1]);
            bool factory = parts[2].StartsWith("factory:", StringComparison.Ordinal);
            if (city == null || !(parts[2] is "village" or "town" || factory && FactorySystem.TypeIndex(parts[2].Substring(8)) >= 0)) return false;
            made.Target = city.data.id;
            made.Text = string.Format(LM.Get("terminal_order_build"), city.GetCityName(), BuildName(parts[2]));
        }
        made.Kind = "build";
        made.Arg = id.Substring(6);
        DropPanels(seat);
        return true;
    }

    private static string BuildExecute(Seat seat, Order order)
    {
        DropPanels(seat);
        Kingdom realm = seat.Realm;
        string[] parts = (order.Arg ?? "").Split(new[] { ':' }, 2);
        if (realm?.data == null || parts.Length != 2) return LM.Get("terminal_fail_generic");
        if (parts[0] == "research")
        {
            string culture = TechnologySystem.GetCultureOf(realm);
            if (string.IsNullOrEmpty(culture) || TechnologySystem.HasTech(culture, parts[1])) return LM.Get("terminal_fail_generic");
            CultureTechState techs = TechnologySystem.GetState(culture);
            if (techs?.player_targets == null || !techs.player_targets.Contains(parts[1])) TechnologySystem.SetPlayerTarget(culture, parts[1]);
            return null;
        }
        City city = OwnCity(seat, parts[0]);
        if (city == null) return LM.Get("terminal_fail_generic");
        if (!PureMapMode.Enabled) return LM.Get("settlement_need_pure_map");
        string kind = parts[1];
        int cost = BuildCost(kind);
        if (realm.GetMoney() < cost) return LM.Get("terminal_build_poor");
        bool done;
        if (kind.StartsWith("factory:", StringComparison.Ordinal))
        {
            string type = kind.Substring(8);
            if (!FactorySystem.Unlocked(city).Contains(type)) return LM.Get("terminal_build_locked");
            FactorySite placed = FactorySystem.FoundBranch(city, type);
            done = placed != null;
            // 国库出的钱：官营
            if (done) placed.state = true;
        }
        else done = FoundSettlement(city, kind == "town");
        if (!done) return LM.Get("terminal_build_no_room");
        TreasurySystem.TrySpend(realm, cost, TreasuryCategory.Construction);
        return null;
    }

    // 给这座城在城郊挑一处立村设镇：没盖过的地块由近到远试，离别的村镇、厂区太近的不要
    private static bool FoundSettlement(City city, bool town)
    {
        WorldTile center = city.getTile();
        if (center == null || city.zones == null) return false;
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        List<RuralSettlement> have = RuralSettlementSystem.InCity(city);
        int tries = 0;
        foreach (TileZone zone in city.zones.Where(zone => zone?.centerTile != null && zone != center.zone &&
                         (built != null ? !built.Contains(zone.id) : Toolbox.SquaredDistTile(zone.centerTile, center) >= 200))
                     .OrderBy(zone => Toolbox.SquaredDistTile(zone.centerTile, center)).ToList())
        {
            WorldTile tile = zone.centerTile;
            if (have.Any(held => Math.Abs(held.x - tile.x) + Math.Abs(held.y - tile.y) < 10) || FactorySystem.Near(tile.x, tile.y, 2)) continue;
            if (++tries > 16) break;
            RuralSettlementSystem.Place(tile, town);
            if (RuralSettlementSystem.At(tile.x, tile.y) != null) return true;
        }
        return false;
    }
}
