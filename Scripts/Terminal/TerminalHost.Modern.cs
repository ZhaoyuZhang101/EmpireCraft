using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 近世国势(用户 2026-11-25 "手机终端的界面也接上这些新内容")：工业时代以后那几套新系统在手机上的一页。
// 只给一国之主(OwnEmpire)：
//   · 国势页多一节「近世」(state.modern)，五组——市面(景气、萧条与对策)、劳工(工会、工钱、工潮)、产业(归属、教育、垄断与财阀、
//     外国资本、公司入口)、革命形势、世界格局(阵营、冷战、万国公会、制裁、关税战、大萧条、航天、去殖民)。字都由游戏按语言给好，
//     手机只管排；每行 [名目, 现状, 色调, 可点开的去处]；
//   · 君主能办的照政令走(一个月后办，到时再验)：萧条的四条对策、工潮让步或弹压、革命形势镇压或让步、
//     对本国公司国库入股减持、收归国有、发卖民营；关税战在外交里(加征关税、议定关税，见 DiplomacyNeeds)；
//   · 公司、财富榜、股市是点进去才要的(/api/firms，主线程备好一份)：本国公司一览与各家的经营业务；财富榜、别国的交易所只列
//     本国与听说过的国家(同迷雾)，别国公司只看得到名号、行业与身价。
// 自家的事不打折；数取整，不给小数。
public static partial class TerminalHost
{
    private const int MaxFirmRows = 40, MaxRichRows = 12;
    private static readonly ConcurrentDictionary<string, string> FirmsWant = new(), FirmsReady = new();

    private static JObject ModAct(string id, string label, string sub, string off = null)
    {
        var act = new JObject { ["id"] = id, ["label"] = label, ["sub"] = sub ?? "", ["go"] = LM.Get("terminal_modern_go") };
        if (off != null) act["off"] = off;
        return act;
    }

    private static string ModPct(float share) => Mathf.RoundToInt(share * 100f) + "%";

    private static bool Heard(Seat seat, Empire empire) =>
        empire?.CoreKingdom != null && (empire.CoreKingdom == seat.Realm || seat.Known.Contains(empire.CoreKingdom.getID()));

    // ───────── 国势页「近世」一节(像素面板，见 TerminalHost.Panels) ─────────
    private static JObject ModernJson(Seat seat)
    {
        Empire empire = OwnEmpire(seat);
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null) return null;
        return XPanelOf(LM.Get("terminal_modern_title"), new[]
        {
            CycleGroup(empire, state), LabourGroup(empire, state), IndustryGroup(empire, state), RevolutionGroup(empire, state),
            WorldGroup(seat, empire)
        }.Where(card => card != null));
    }

    private static JObject CycleGroup(Empire empire, ConstitutionalEconomyState state)
    {
        int phase = state.cycle_phase;
        if (phase == EconomicCycleSystem.None) return null;
        bool crisis = EconomicCycleSystem.InCrisis(empire);
        JObject card = XCard(LM.Get("label_economic_cycle"), "x_market", crisis ? "b" : null);
        card["tag"] = EconomicCycleSystem.PhaseName(phase);
        card["tagTone"] = crisis ? "b" : phase == EconomicCycleSystem.Overheat ? "a" : "g";
        var steps = new List<string>();
        for (int step = EconomicCycleSystem.Recovery; step <= EconomicCycleSystem.Depression; step++) steps.Add(EconomicCycleSystem.PhaseName(step));
        card.XAdd(XTrack(steps, phase - EconomicCycleSystem.Recovery, crisis));
        string note;
        if (crisis)
        {
            card.XAdd(XTiles(2, new[]
            {
                XTile("k_years", LM.Get("terminal_modern_slump_left"),
                    string.Format(LM.Get("terminal_modern_years"), Math.Max(1, state.cycle_depression_years)), null, "b"),
                XTile("i_seal", LM.Get("terminal_modern_response"), LM.Get(string.IsNullOrEmpty(state.cycle_response)
                    ? "economic_cycle_response_none" : "economic_cycle_response_" + state.cycle_response))
            }));
            note = LM.Get("economic_cycle_cause_" + (string.IsNullOrEmpty(state.cycle_cause) ? "bubble" : state.cycle_cause));
            var acts = new List<JObject>();
            foreach (string kind in new[] { EconomicCycleSystem.Laissez, EconomicCycleSystem.Stimulus, EconomicCycleSystem.Austerity, EconomicCycleSystem.Tariff })
            {
                string reason = EconomicCycleSystem.CanPlayerRespond(empire, kind);
                string tip = kind == EconomicCycleSystem.Stimulus
                    ? string.Format(LM.Get("economic_cycle_response_stimulus_tip"), EconomicCycleSystem.StimulusCost(empire))
                    : LM.Get("economic_cycle_response_" + kind + "_tip");
                acts.Add(ModAct("modern:slump:" + kind, LM.Get("economic_cycle_response_" + kind), tip, reason == null ? null : LM.Get(reason)));
            }
            card.XAdd(XNote(note + (WorldOrderSystem.WorldDepression ? " " + LM.Get("economic_cycle_world_depression") : "")));
            card.XAdd(XActs(acts));
            return card;
        }
        float top = EconomicCycleSystem.OverheatAt;
        card.XAdd(XGauge(LM.Get("terminal_modern_heat"), state.cycle_heat, top * 1.3f, Mathf.RoundToInt(state.cycle_heat).ToString(),
            state.cycle_heat >= top ? "b" : null, "x_wealth", top, LM.Get("terminal_modern_heat_line")));
        note = LM.Get("economic_cycle_hint_" + Mathf.Clamp(phase, 1, 3));
        card.XAdd(XNote(note + (WorldOrderSystem.WorldDepression ? " " + LM.Get("economic_cycle_world_depression") : "")));
        return card;
    }

    private static JObject LabourGroup(Empire empire, ConstitutionalEconomyState state)
    {
        bool strike = state.strike_stage > LabourSystem.NoStrike;
        if (!strike && (!LabourSystem.Applies(empire) || state.union_strength < 5f && state.wage_level == 0 && state.labour_law == 0 &&
                        state.union_status == LabourSystem.Tolerated)) return null;
        JObject card = XCard(LM.Get("label_labour"), "x_works", strike ? "b" : null);
        card["tag"] = LabourSystem.UnionStatusName(state.union_status);
        card["tagTone"] = state.union_status == LabourSystem.Banned ? "b" : state.union_status == LabourSystem.Legal ? "g" : "a";
        card.XAdd(XGauge(LM.Get("terminal_modern_union_strength"), state.union_strength, 100f, Mathf.RoundToInt(state.union_strength).ToString(),
            state.union_strength >= LabourSystem.StrikeAt ? "b" : "i", null, LabourSystem.StrikeAt, LM.Get("terminal_modern_union_line")));
        card.XAdd(XTiles(3, new[]
        {
            XTile("x_wealth", LM.Get("terminal_modern_wage_tile"), state.wage_level.ToString()),
            XTile("g_law", LM.Get("terminal_modern_law_tile"), state.labour_law.ToString()),
            XTile("x_people", LM.Get("terminal_modern_welfare_tile"), state.welfare_level.ToString())
        }));
        if (strike)
        {
            bool general = state.strike_stage >= LabourSystem.GeneralStrike;
            card.XAdd(XRow(LM.Get("terminal_modern_strike"), LM.Get(general ? "terminal_modern_strike_2" : "terminal_modern_strike_1"), "b"));
            card.XAdd(XNote(LM.Get(general ? "labour_general_strike_line" : "labour_strike_wave_line")));
            card.XAdd(XActs(new[]
            {
                ModAct("modern:strike:concede", LM.Get("labour_response_concede"), LM.Get("labour_response_concede_tip")),
                ModAct("modern:strike:repress", LM.Get("labour_response_repress"), LM.Get("labour_response_repress_tip"))
            }));
        }
        else
            card.XAdd(XNote(string.IsNullOrEmpty(state.strike_outcome) || state.strike_resolved_at < 0d
                ? LM.Get("labour_quiet_line")
                : string.Format(LM.Get("labour_last_strike_line"), Date.getYearsSince(state.strike_resolved_at), LM.Get("labour_outcome_" + state.strike_outcome))));
        return card;
    }

    private static readonly string[] HouseColors = { "o", "p", "u", "i", "a" };

    private static JObject IndustryGroup(Empire empire, ConstitutionalEconomyState state)
    {
        IReadOnlyList<ConglomerateData> houses = ConglomerateSystem.Of(empire);
        List<(string name, int terms)> dynasties = ConglomerateSystem.Houses(empire);
        int firms = CompanySystem.All.Count(company => CompanySystem.RealmOf(company) == empire);
        if (firms == 0 && houses.Count == 0 && dynasties.Count == 0 && state.nationalization == ModernPolicySystem.Mixed &&
            state.education_level == 0) return null;
        JObject card = XCard(LM.Get("label_industry"), "x_factory");
        card["tag"] = ModernPolicySystem.NationalizationName(state.nationalization);
        card["tagTone"] = "o";
        float monopoly = ConglomerateSystem.Monopoly(empire);
        if (houses.Count > 0)
        {
            // 财阀各占几成，剩下的是散户
            var parts = new List<(string, float, string)>();
            float held = 0f;
            int color = 0;
            foreach (ConglomerateData house in houses.OrderByDescending(item => item.share))
            {
                parts.Add((house.name, house.share * 100f, HouseColors[color++ % HouseColors.Length]));
                held += house.share;
            }
            if (held < 1f) parts.Add((LM.Get("terminal_modern_small_firms"), (1f - held) * 100f, "k"));
            card.XAdd(XHead(LM.Get("terminal_modern_monopoly")));
            card.XAdd(XStack(parts));
        }
        else if (monopoly > 0f)
            card.XAdd(XGauge(LM.Get("terminal_modern_monopoly"), monopoly * 100f, 100f, ModPct(monopoly), monopoly >= 0.4f ? "b" : null, null, 40f));
        if (state.education_level > 0)
            card.XAdd(XRow(LM.Get("terminal_modern_education"), string.Format(LM.Get("terminal_modern_education_value"), state.education_level,
                LM.Get(state.education_funded ? "agenda_welfare_funded" : "agenda_welfare_unfunded")), state.education_funded ? null : "b", null, "x_edify"));
        if (dynasties.Count > 0)
            card.XAdd(XRow(LM.Get("political_house_label"), "", null, string.Join("、", dynasties.Select(house =>
                string.Format(LM.Get("political_house_entry"), house.name, house.terms))), "a_clan"));
        if (state.price_gouging >= 0.05f)
            card.XAdd(XRow(LM.Get("terminal_modern_gouging"), "+" + ModPct(state.price_gouging), "b"));
        if (state.foreign_control != null)
            foreach (KeyValuePair<long, float> pair in state.foreign_control.OrderByDescending(pair => pair.Value))
            {
                Empire foreign = ModClass.EMPIRE_MANAGER?.get(pair.Key);
                if (foreign?.data == null || pair.Value < 0.05f) continue;
                card.XAdd(XGauge(TerminalIntel.Strip(foreign.GetEmpireFullName()), pair.Value * 100f, 100f, ModPct(pair.Value),
                    pair.Value >= CompanySystem.InfluenceAt ? "b" : null, "x_diplo", CompanySystem.ControlAt * 100f,
                    string.Format(LM.Get("terminal_modern_foreign"), "") + LM.Get(pair.Value >= CompanySystem.ControlAt ? "company_foreign_master"
                        : pair.Value >= CompanySystem.InfluenceAt ? "company_foreign_beholden" : "company_foreign_minor")));
            }
        if (firms > 0)
            card.XAdd(XTiles(3, new[]
            {
                XTile("x_factory", LM.Get("company_window"), firms.ToString(), null, null, "list"),
                XTile("x_wealth", LM.Get("company_page_rich"), "›", null, null, "rich"),
                StockMarketSystem.Exchanges.Count > 0 ? XTile("x_market", LM.Get("company_page_stocks"), "›", null, null, "stocks") : null
            }));
        return card;
    }

    private static JObject RevolutionGroup(Empire empire, ConstitutionalEconomyState state)
    {
        bool aftermath = RevolutionSystem.InAftermath(empire);
        int stage = RevolutionSystem.Stage(empire);
        if (!aftermath && (!RevolutionSystem.Applies(empire) || stage == RevolutionSystem.Calm && state.revolution_tension < 20f)) return null;
        JObject card = XCard(LM.Get("label_revolution_situation"), "k_war", stage >= RevolutionSystem.Ripe ? "b" : null);
        if (aftermath)
        {
            card.XAdd(XRow(LM.Get("label_post_revolution"), RevolutionSystem.AftermathPhaseName(empire), "a"));
            card.XAdd(XNote(LM.Get("post_revolution_hint")));
            return card;
        }
        var steps = new List<string>();
        for (int step = RevolutionSystem.Calm; step <= RevolutionSystem.Uprising; step++) steps.Add(LM.Get("revolution_stage_" + step));
        card.XAdd(XTrack(steps, Mathf.Clamp(stage, 0, 3), stage >= RevolutionSystem.Undercurrent));
        card.XAdd(XGauge(LM.Get("terminal_modern_tension"), state.revolution_tension, RevolutionSystem.RipeAt * 1.25f,
            Mathf.RoundToInt(state.revolution_tension).ToString(), "b", null, RevolutionSystem.RipeAt, LM.Get("terminal_modern_tension_line")));
        card.XAdd(XGauge(LM.Get("terminal_modern_org"), state.revolution_org, RevolutionSystem.OrgNeeded * 1.25f,
            Mathf.RoundToInt(state.revolution_org).ToString(), "a", null, RevolutionSystem.OrgNeeded, LM.Get("terminal_modern_org_line")));
        card.XAdd(XRow(LM.Get("terminal_modern_movement"), TerminalIntel.Strip(RevolutionSystem.MovementName(empire)), null,
            string.IsNullOrEmpty(state.revolution_cause) ? null
                : LM.Get("terminal_modern_cause") + "：" + LM.Get("revolution_cause_" + state.revolution_cause)));
        card.XAdd(XNote(RevolutionSystem.InTalks(empire) ? LM.Get("revolution_talks_line")
            : stage == RevolutionSystem.Ripe ? LM.Get("revolution_waiting_spark") : ""));
        if (stage is >= RevolutionSystem.Undercurrent and < RevolutionSystem.Uprising)
        {
            string reason = RevolutionSystem.CanPlayerRespond(empire);
            string off = reason == null ? null : LM.Get(reason);
            card.XAdd(XActs(new[]
            {
                ModAct("modern:revolt:repress", LM.Get("revolution_response_repress"), LM.Get("revolution_response_repress_tip"), off),
                ModAct("modern:revolt:concede", LM.Get("revolution_response_concede"), LM.Get("revolution_response_concede_tip"), off)
            }));
        }
        return card;
    }

    private static JObject WorldGroup(Seat seat, Empire empire)
    {
        JObject card = XCard(LM.Get("terminal_modern_world"), "x_tianxia");
        BlocData bloc = WorldOrderSystem.BlocOf(empire);
        if (bloc != null)
        {
            card.XAdd(XRow(LM.Get("terminal_modern_bloc"), TerminalIntel.Strip(WorldOrderSystem.BlocName(bloc)) + " · " +
                LM.Get(WorldOrderSystem.LeaderOf(bloc) == empire ? "world_bloc_leader" : "terminal_modern_member"), "o"));
            foreach (ColdWarData war in WorldOrderSystem.ColdWars)
            {
                if (war.a != bloc.id && war.b != bloc.id) continue;
                BlocData rival = WorldOrderSystem.Bloc(war.a == bloc.id ? war.b : war.a);
                if (rival == null) continue;
                card.XAdd(XGauge(LM.Get("world_cold_war_title"), war.tension, 100f, Mathf.RoundToInt(war.tension).ToString(), "b", null,
                    WorldOrderSystem.IronCurtainAt, TerminalIntel.Strip(WorldOrderSystem.BlocName(rival))));
            }
        }
        if (WorldOrderSystem.League != null)
            card.XAdd(XRow(LM.Get("world_league_title"), LM.Get(WorldOrderSystem.IsLeagueMember(empire) ? "terminal_modern_league_in" : "terminal_modern_league_out")));
        foreach (SanctionData sanction in WorldOrderSystem.Sanctions)
            if (sanction.target_id == empire.id)
                card.XAdd(XRow(LM.Get("world_league_title"), string.Format(LM.Get("world_sanction_row"),
                    Math.Max(0, sanction.years - Date.getYearsSince(sanction.since))), "b"));
        foreach (TradeWarData war in TradeWarSystem.All)
        {
            if (war.a != empire.id && war.b != empire.id) continue;
            Empire other = TradeWarSystem.Other(war, empire);
            if (other == null) continue;
            card.XAdd(XRow(LM.Get("trade_war_title"), LM.Get(war.level >= TradeWarSystem.Embargo ? "trade_war_level_2" : "trade_war_level_1"),
                war.level >= TradeWarSystem.Embargo ? "b" : "a", TerminalIntel.Strip(other.GetEmpireFullName())));
        }
        if (WorldOrderSystem.WorldDepression)
            card.XAdd(XRow(LM.Get("terminal_modern_depression"), string.Format(LM.Get("terminal_modern_year_n"),
                Date.getYearsSince(WorldOrderSystem.DepressionSince)), "b"));
        int level = SpaceRaceSystem.Level(empire);
        float progress = SpaceRaceSystem.Progress(empire);
        if (level > 0 || progress > 0f)
        {
            var steps = new List<string> { "—" };
            for (int step = SpaceRaceSystem.Satellite; step <= SpaceRaceSystem.Moon; step++) steps.Add(SpaceRaceSystem.StepName(step));
            card.XAdd(XHead(LM.Get("space_race_title")));
            card.XAdd(XTrack(steps, Mathf.Clamp(level, 0, steps.Count - 1)));
            if (level < SpaceRaceSystem.Moon)
                card.XAdd(XGauge(SpaceRaceSystem.StepName(level + 1), progress, 100f, Mathf.RoundToInt(progress) + "%", "i"));
        }
        foreach (SpaceFirstData first in SpaceRaceSystem.Firsts)
            card.XAdd(XRow(SpaceRaceSystem.StepName(first.kind), string.Format(LM.Get("space_first_row"), TerminalIntel.Strip(first.name),
                Date.getYearsSince(first.at))));
        if (DecolonizationSystem.Active)
            card.XAdd(XRow(LM.Get("decolonization_title"), string.Format(LM.Get("terminal_modern_year_n"),
                Date.getYearsSince(DecolonizationSystem.Since)), "a"));
        return XEmpty(card) ? null : card;
    }

    // ───────── 政令 ─────────
    private static CompanyData OwnFirm(Empire empire, string raw) =>
        int.TryParse(raw, out int id) && CompanySystem.ById(id) is { } company && CompanySystem.RealmOf(company) == empire ? company : null;

    private static bool ModernOrder(Seat seat, string id, Order made)
    {
        if (!id.StartsWith("modern:", StringComparison.Ordinal)) return false;
        Empire empire = OwnEmpire(seat);
        if (empire?.data?.constitutional_economy == null) return false;
        string[] parts = id.Split(':');
        if (parts.Length < 3) return false;
        made.Kind = "modern";
        made.Arg = id.Substring(7);
        switch (parts[1])
        {
            case "slump":
                if (!LM.Has("economic_cycle_response_" + parts[2])) return false;
                made.Text = string.Format(LM.Get("terminal_order_slump"), LM.Get("economic_cycle_response_" + parts[2]));
                return true;
            case "strike":
                if (parts[2] is not ("concede" or "repress")) return false;
                made.Text = string.Format(LM.Get("terminal_order_strike"), LM.Get("labour_response_" + parts[2]));
                return true;
            case "revolt":
                if (parts[2] is not ("concede" or "repress")) return false;
                made.Text = string.Format(LM.Get("terminal_order_revolt"), LM.Get("revolution_response_" + parts[2]));
                return true;
            case "firm":
                CompanyData company = parts.Length < 4 ? null : OwnFirm(empire, parts[3]);
                if (company == null || parts[2] is not ("buy" or "sell" or "state" or "private")) return false;
                made.Target = company.id;
                made.Text = string.Format(LM.Get("terminal_order_firm_" + parts[2]), company.name);
                return true;
        }
        return false;
    }

    private static string ModernExecute(Seat seat, Order order)
    {
        Empire empire = OwnEmpire(seat);
        if (empire?.data?.constitutional_economy == null) return LM.Get("terminal_fail_generic");
        string[] parts = (order.Arg ?? "").Split(':');
        if (parts.Length < 2) return LM.Get("terminal_fail_generic");
        string reason;
        switch (parts[0])
        {
            case "slump":
                reason = EconomicCycleSystem.PlayerRespond(empire, parts[1]);
                break;
            case "strike":
                reason = LabourSystem.PlayerRespond(empire, parts[1] == "repress");
                break;
            case "revolt":
                reason = RevolutionSystem.PlayerRespond(empire, parts[1] == "repress");
                break;
            case "firm":
                CompanyData company = CompanySystem.ById((int)order.Target);
                if (company == null || CompanySystem.RealmOf(company) != empire) return LM.Get("terminal_fail_generic");
                if (parts[1] == "buy") reason = CompanySystem.BuyStake(company);
                else if (parts[1] == "sell") reason = CompanySystem.SellStake(company);
                else
                {
                    // 收归国有、发卖民营：旗下各片厂区一起改
                    bool toState = parts[1] == "state";
                    if (company.state == toState) return LM.Get("terminal_fail_generic");
                    foreach (FactorySite site in CompanySystem.Sites(company))
                    {
                        site.state = toState;
                        CompanySystem.Reassign(site);
                    }
                    reason = null;
                }
                break;
            default:
                return LM.Get("terminal_fail_generic");
        }
        return reason == null ? null : LM.Get(reason);
    }

    // ───────── 公司、财富榜、股市(点进去才要) ─────────
    private static TerminalServer.Reply FirmsReply(Seat seat, string body)
    {
        string what = Field(body, "id") ?? "";
        if (what.Length > 32) return TerminalServer.Reply.Error(400, "bad_request");
        string key = seat.Token + "|" + what;
        if (FirmsReady.TryRemove(key, out string built)) return TerminalServer.Reply.Json(built);
        if (FirmsWant.Count < 16) FirmsWant[key] = what;
        return TerminalServer.Reply.Json("{\"ready\":false}");
    }

    // 主线程(随典册一起，见 RunAnnals)
    private static void RunFirms()
    {
        if (FirmsWant.IsEmpty || !WorldReady()) return;
        if (FirmsReady.Count > 32) FirmsReady.Clear();
        foreach (string key in FirmsWant.Keys.ToList())
        {
            if (!FirmsWant.TryRemove(key, out string what)) continue;
            string token = key.Substring(0, key.IndexOf('|'));
            if (!SeatByToken.TryGetValue(token, out Seat seat) || seat.Ruler == null || seat.Realm == null) continue;
            try
            {
                FirmsReady[key] = FirmsBundle(seat, what).ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception exception)
            {
                NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 备公司页出错: {exception.Message}");
                FirmsReady[key] = "{\"ready\":true,\"rows\":[]}";
            }
        }
    }

    private static JObject ModLine(string label, string sub, string right, string tone = "", string open = null)
    {
        var line = new JObject { ["label"] = label ?? "", ["sub"] = sub ?? "", ["right"] = right ?? "", ["tone"] = tone ?? "" };
        if (open != null) line["open"] = open;
        return line;
    }

    private static string ModMoney(float value) => TerminalIntel.RoundSig((long)Math.Round(value), 3).ToString();

    private static string FirmTone(CompanyData company) =>
        company.state ? "i" : company.foreign_empire_id >= 0 ? "a" : company.group_clan_id >= 0 ? "o" : "";

    private static string FirmKind(CompanyData company) => TerminalIntel.Strip(CompanySystem.OwnershipName(company));

    private static JObject FirmsBundle(Seat seat, string what)
    {
        // 一座城的营建格(见 TerminalHost.Policy)
        if (what.StartsWith("city:", StringComparison.Ordinal)) return CityBuildPage(seat, what);
        Empire empire = OwnEmpire(seat) ?? seat.Realm?.GetEmpire();
        var rows = new JArray();
        var page = new JObject { ["ready"] = true, ["rows"] = rows, ["acts"] = new JArray() };
        if (what == "rich")
        {
            page["title"] = LM.Get("company_page_rich");
            rows.Add(new JObject { ["head"] = LM.Get("company_rich_firms") });
            int rank = 0;
            foreach (CompanyData company in StockMarketSystem.TopCompanies(200).Where(company => Heard(seat, CompanySystem.RealmOf(company))).Take(MaxRichRows))
                rows.Add(ModLine($"{++rank}. {company.short_name}", TerminalIntel.Strip(CompanySystem.RealmOf(company).GetEmpireFullName()) + " · " +
                    CompanyNaming.SectorName(company.sector), ModMoney(CompanySystem.Worth(company)), FirmTone(company), company.id.ToString()));
            rows.Add(new JObject { ["head"] = LM.Get("company_rich_families") + " · " + LM.Get("company_rich_families_hint") });
            rank = 0;
            foreach (StockMarketSystem.Fortune fortune in StockMarketSystem.RichList(200).Where(fortune => Heard(seat, fortune.realm)).Take(MaxRichRows))
                rows.Add(ModLine($"{++rank}. " + string.Format(LM.Get("company_owner_family"), fortune.name) + (fortune.group ? " ★" : ""),
                    TerminalIntel.Strip(fortune.realm.GetEmpireFullName()) + " · " + string.Format(LM.Get("company_count"), fortune.companies),
                    ModMoney(fortune.wealth), fortune.group ? "o" : ""));
            return page;
        }
        if (what == "stocks")
        {
            page["title"] = LM.Get("company_page_stocks");
            StockExchangeData mine = StockMarketSystem.ExchangeOf(empire);
            foreach (StockExchangeData exchange in StockMarketSystem.Exchanges.OrderByDescending(item => item == mine).ThenByDescending(item => item.index))
            {
                Empire realm = ModClass.EMPIRE_MANAGER?.get(exchange.empire_id);
                if (realm?.data == null || realm.isRekt() || realm.IsArchived() || !Heard(seat, realm)) continue;
                IList<float> history = exchange.history ?? new List<float>();
                float change = StockMarketSystem.Change(history);
                rows.Add(new JObject
                {
                    ["head"] = string.Format(LM.Get("stock_exchange_title"), TerminalIntel.Strip(realm.GetEmpireFullName())) + " · " +
                               string.Format(LM.Get("stock_exchange_header"), Mathf.RoundToInt(exchange.index), StockMarketSystem.ChangeText(change),
                                   Date.getYearsSince(exchange.since))
                });
                if (exchange != mine) continue;
                if (history.Count >= 2) rows.Add(new JObject { ["chart"] = new JArray(history.Select(value => Math.Round(value, 1))), ["tone"] = change >= 0f ? "g" : "b" });
                foreach (CompanyData company in CompanySystem.All.Where(company => company.listed && CompanySystem.RealmOf(company) == realm)
                             .OrderByDescending(company => company.price).Take(MaxFirmRows))
                {
                    float move = StockMarketSystem.Change(company.prices);
                    rows.Add(ModLine(company.short_name, string.Format(LM.Get("terminal_firm_stock_sub"), company.price.ToString("0.0"),
                            ModMoney(company.price * StockMarketSystem.Shares)), StockMarketSystem.ChangeText(move), move >= 0f ? "g" : "b", company.id.ToString()));
                }
            }
            if (rows.Count == 0) page["note"] = LM.Get("stock_none_hint");
            return page;
        }
        if (int.TryParse(what, out int id) && CompanySystem.ById(id) is { } firm && Heard(seat, CompanySystem.RealmOf(firm)))
            return FirmDetail(seat, empire, firm, page, rows);
        // 本国公司一览
        page["title"] = LM.Get("company_page_realm");
        if (empire == null) return page;
        foreach (CompanyData company in CompanySystem.All.Where(company => CompanySystem.RealmOf(company) == empire)
                     .OrderByDescending(CompanySystem.Worth).Take(MaxFirmRows))
            rows.Add(ModLine(company.short_name, CompanyNaming.SectorName(company.sector) + " · " + FirmKind(company),
                ModMoney(CompanySystem.Worth(company)), FirmTone(company), company.id.ToString()));
        if (rows.Count == 0) page["note"] = LM.Get("company_none_hint");
        return page;
    }

    private static JObject FirmDetail(Seat seat, Empire empire, CompanyData company, JObject page, JArray rows)
    {
        Empire realm = CompanySystem.RealmOf(company);
        bool own = realm == empire && OwnEmpire(seat) == empire;
        City home = CompanySystem.Home(company);
        page["title"] = company.name;
        rows.Add(ModLine(LM.Get("company_col_kind"), "", FirmKind(company), FirmTone(company)));
        rows.Add(ModLine(LM.Get("company_col_trade"), "", CompanyNaming.SectorName(company.sector)));
        rows.Add(ModLine(LM.Get("company_home"), "", (home?.GetCityName() ?? "") + " · " + TerminalIntel.Strip(realm.GetEmpireFullName())));
        rows.Add(ModLine(LM.Get("company_worth"), "", ModMoney(CompanySystem.Worth(company)), "o"));
        // 别国的公司：只知道名号、行业与身价
        if (realm != empire) return page;
        rows.Add(ModLine(LM.Get("company_founded"), "", string.Format(LM.Get("company_founded_years"), Date.getYearsSince(company.founded))));
        if (!company.state && !string.IsNullOrEmpty(company.owner))
            rows.Add(ModLine(LM.Get("company_owner"), "", string.Format(LM.Get("company_owner_family"), company.owner)));
        if (company.state_share > 0f) rows.Add(ModLine(LM.Get("company_stake"), "", ModPct(company.state_share), "i"));
        rows.Add(ModLine(LM.Get("company_reputation"), "", Mathf.RoundToInt(company.reputation).ToString(),
            company.reputation >= 60f ? "g" : company.reputation < 30f ? "b" : ""));
        if (!company.state) rows.Add(ModLine(LM.Get("company_capital"), "", ModMoney(company.capital), company.capital < 0f ? "b" : ""));
        rows.Add(ModLine(LM.Get("company_profit"), "", ModMoney(company.profit), company.profit < 0f ? "b" : "g"));
        float gouge = CompanySystem.Markup(realm.id, company.sector);
        if (gouge != 0f)
            page["note"] = string.Format(LM.Get(gouge > 0f ? "company_gouging_line" : "company_price_war_line"),
                CompanyNaming.SectorName(company.sector), Mathf.RoundToInt(Mathf.Abs(gouge) * 100f));
        if (company.listed)
        {
            IList<float> prices = company.prices ?? new List<float>();
            float change = StockMarketSystem.Change(prices);
            rows.Add(new JObject
            {
                ["head"] = LM.Get("company_card_stock") + " · " + string.Format(LM.Get("company_stock_header"), company.price.ToString("0.0"),
                    StockMarketSystem.ChangeText(change), ModMoney(company.price * StockMarketSystem.Shares))
            });
            if (prices.Count >= 2) rows.Add(new JObject { ["chart"] = new JArray(prices.Select(value => Math.Round(value, 1))), ["tone"] = change >= 0f ? "g" : "b" });
        }
        List<FactorySite> sites = CompanySystem.Sites(company);
        rows.Add(new JObject
        {
            ["head"] = LM.Get("company_card_business") + " · " + string.Format(LM.Get("company_business_header"), sites.Count,
                sites.Sum(site => site.units.Sum(FactorySystem.JobsOf)), Mathf.RoundToInt(company.value))
        });
        foreach (FactorySite site in sites.OrderByDescending(site => site.units.Sum(FactorySystem.ValueOf)).Take(12))
            rows.Add(ModLine(FactorySystem.CityOf(site)?.GetCityName() ?? "", string.Join("、", site.units.GroupBy(unit => unit.type)
                .Select(group => FactorySystem.TypeName(group.Key) + (group.Count() > 1 ? "×" + group.Count() : ""))), ""));
        if (company.notes != null && company.notes.Count > 0)
        {
            rows.Add(new JObject { ["head"] = LM.Get("company_card_notes") });
            foreach (CompanyNote note in company.notes.AsEnumerable().Reverse())
                rows.Add(ModLine(string.Format(LM.Get("company_founded_years"), Date.getYearsSince(note.at)), TerminalIntel.Strip(note.text), ""));
        }
        if (!own) return page;
        var acts = (JArray)page["acts"];
        if (company.state)
            acts.Add(ModAct("modern:firm:private:" + company.id, LM.Get("enterprise_to_private"), LM.Get("terminal_firm_private_tip")));
        else
        {
            if (company.foreign_empire_id < 0)
            {
                acts.Add(ModAct("modern:firm:buy:" + company.id, LM.Get("company_stake_buy"), LM.Get("terminal_firm_buy_tip")));
                if (company.state_share > 0f)
                    acts.Add(ModAct("modern:firm:sell:" + company.id, LM.Get("company_stake_sell"), LM.Get("terminal_firm_sell_tip")));
            }
            acts.Add(ModAct("modern:firm:state:" + company.id, LM.Get("enterprise_to_state"),
                LM.Get(company.foreign_empire_id >= 0 ? "terminal_firm_seize_tip" : "terminal_firm_state_tip")));
        }
        return page;
    }
}
