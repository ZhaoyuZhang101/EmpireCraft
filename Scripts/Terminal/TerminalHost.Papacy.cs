using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 教廷与皇帝(用户 2026-11-18 "还有皇帝的位置和教宗界面, 之类的手机舆图都没做")，问答定"标记+详情+教廷页"：
//   · 舆图上的皇帝、教宗标记(同游戏纯净地图的 EmperorMarkerRenderer)：皇帝画在所在之城(出行时沿路走)，教宗画在圣地
//     (兼做皇帝的跟着行程走、不另画帝冠)。随军情一起报(crowns)，点开看的几行同游戏里悬停的详情。君主看得见本朝的、
//     迷雾外的；管理员全图都看得见；
//   · 教廷页(papacy，随状态发)：同游戏帝国窗口的教廷页那几项——教权、加冕、叙任权、什一税、教宗、圣地、状态、
//     叙任权之争、绝罚、圣战、骑士团；君主能办的事见 PapacySystem.Seat(请冕、悔罪；教宗绝罚赦免、号召圣战、创立骑士团)。
//     游戏里教廷页的按钮是上帝直接干预，手机上不给
public static partial class TerminalHost
{
    private const int MaxCrowns = 60;
    private const string RomaSet = "roma";

    // ───────── 舆图上的标记 ─────────
    // 一行：k 是 e 皇帝、p 教宗；x、y 画在哪；c 印面颜色；w 冠的样子(roma 西方帝冠，空是华夏冕；教宗是三重冕)；
    // n 名字、s 第二行(帝国、教会)、rows 点开看的几行 [名目, 值, 色调 g 好 b 坏 i 动向 n 平常]；me 本朝的
    internal static JArray SeeCrowns(Seat seat, bool god)
    {
        var crowns = new JArray();
        if (ModClass.EMPIRE_MANAGER == null || seat.MapCells == null) return crowns;
        Empire mine = god ? null : seat.Realm?.GetEmpire();
        var popes = new List<(Religion religion, Kingdom papacy, City holy, Empire throne)>();
        var thrones = new HashSet<long>();
        foreach (Religion religion in World.world.religions)
        {
            try
            {
                if (religion?.data == null || religion.isRekt()) continue;
                Actor pope = PapacySystem.Pope(religion);
                Kingdom papacy = PapacySystem.Papacy(religion);
                City holy = PapacySystem.HolyCity(religion);
                if (pope == null || papacy == null || holy == null || !WesternSee(holy, papacy)) continue;
                Empire throne = Throne(pope, papacy);
                if (throne != null) thrones.Add(throne.data.id);
                popes.Add((religion, papacy, holy, throne));
            }
            catch (Exception)
            {
                // 这一家一时算不出：不画
            }
        }
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (crowns.Count >= MaxCrowns) break;
            try
            {
                Actor emperor = empire?.data == null ? null : empire.Emperor;
                if (emperor?.data == null || emperor.isRekt() || !emperor.isAlive() || RepublicSystem.IsRepublic(empire)) continue;
                if (thrones.Contains(empire.data.id)) continue;
                Vector3? away = EmperorTourSystem.DrawPosition(empire);
                WorldTile home = away == null ? EmperorTourSystem.Home(empire)?.getTile() : null;
                if (away == null && home == null) continue;
                Vector3 at = away ?? home.posV;
                bool own = mine != null && mine == empire;
                if (!god && !own && Fogged(seat, at.x, at.y)) continue;
                (double x, double y) = CellOf(seat, at.x, at.y);
                string culture = CultureDoctrines.CultureOf(empire);
                var row = new JObject
                {
                    ["k"] = "e", ["id"] = empire.data.id.ToString(), ["x"] = x, ["y"] = y, ["c"] = HexOf(empire.getColor()) ?? "#c8a040",
                    ["w"] = CultureDoctrines.SetOf(culture) == RomaSet ? "roma" : "",
                    ["n"] = TerminalIntel.Strip(emperor.getName()), ["s"] = TerminalIntel.Strip(empire.GetEmpireFullName()),
                    ["rows"] = EmperorRows(empire, emperor, culture)
                };
                if (own) row["me"] = true;
                crowns.Add(row);
            }
            catch (Exception)
            {
                // 这一位一时算不出：不画
            }
        }
        foreach (var (religion, papacy, holy, throne) in popes)
        {
            if (crowns.Count >= MaxCrowns) break;
            try
            {
                Vector3? away = throne != null ? EmperorTourSystem.DrawPosition(throne) : null;
                WorldTile tile = away == null ? holy.getTile() : null;
                if (away == null && tile == null) continue;
                Vector3 at = away ?? tile.posV;
                bool own = !god && seat.Realm != null && (seat.Realm == papacy || seat.Realm.religion == religion);
                if (!god && !own && Fogged(seat, at.x, at.y)) continue;
                (double x, double y) = CellOf(seat, at.x, at.y);
                Actor pope = PapacySystem.Pope(religion);
                var row = new JObject
                {
                    ["k"] = "p", ["id"] = religion.data.id.ToString(), ["x"] = x, ["y"] = y, ["c"] = HexOf(papacy.getColor()) ?? "#c8a040",
                    ["n"] = TerminalIntel.Strip(pope.getName()), ["s"] = TerminalIntel.Strip(religion.name ?? ""),
                    ["rows"] = PopeRows(religion, papacy, holy, pope, throne)
                };
                if (own) row["me"] = true;
                crowns.Add(row);
            }
            catch (Exception)
            {
                // 这一位一时算不出：不画
            }
        }
        return crowns;
    }

    // 西方的教廷(同 EmperorMarkerRenderer.WesternSee)：圣地是罗马一线文化的城，或教廷所在国属罗马一线
    private static bool WesternSee(City holy, Kingdom papacy) =>
        CultureDoctrines.SetOf(CultureService.GetRealmCulture(papacy)) == RomaSet ||
        CultureDoctrines.SetOf(CultureService.GetMainCulture(holy, initialize: false)) == RomaSet;

    // 教宗兼做皇帝的帝国(圣座帝国，或教廷就在帝国核心国；共和的元首不算)
    private static Empire Throne(Actor pope, Kingdom papacy)
    {
        Empire empire = papacy?.GetEmpire();
        return empire?.data != null && !empire.isRekt() && empire.Emperor == pope && !RepublicSystem.IsRepublic(empire) ? empire : null;
    }

    private static string CT(string key, string culture) => LM.Get(CultureDoctrines.Key(key, culture));

    private static JArray Row(string label, string value, string tone = "n") => new(label, TerminalIntel.Strip(value ?? ""), tone);

    private static JArray EmperorRows(Empire empire, Actor emperor, string culture)
    {
        var rows = new JArray
        {
            Row(CT("emperor_tip_age", culture), string.Format(CT("emperor_tip_age_value", culture), emperor.getAge(), empire.GetEmperorYear())),
            GradeRow(emperor, culture),
            Row(CT("emperor_tip_state", culture), EmperorDoing(empire, culture), "i")
        };
        AddHeirAndMandate(rows, empire, culture);
        if (PapacySystem.IsWestern(empire) && PapacySystem.Applies(empire)) rows.Add(Row(LM.Get("papacy_coronation"), CrownText(empire, out string tone), tone));
        return rows;
    }

    private static JArray PopeRows(Religion religion, Kingdom papacy, City holy, Actor pope, Empire throne)
    {
        bool papalState = papacy.GetKingdomType() == KingdomType.Feudalism_papal_state;
        double since = papacy.data.timestamp_king_rule;
        int reign = since >= 0d ? Date.getYearsSince(since) + 1 : 1;
        string culture = CultureDoctrines.CultureOf(papacy);
        float authority = PapacySystem.Authority(religion);
        var rows = new JArray
        {
            Row(LM.Get("papacy_holy_city"), string.Format(LM.Get(papalState ? "pope_tip_see_papal" : "pope_tip_see_secular"), holy.GetCityName(), papacy.GetKingdomName())),
            Row(CT("emperor_tip_age", culture), string.Format(CT("emperor_tip_age_value", culture), pope.getAge(), reign)),
            GradeRow(pope, culture),
            Row(LM.Get("papacy_authority"), $"{authority:0}", authority >= 60f ? "g" : authority >= 35f ? "n" : "b")
        };
        if (PopeLegitimacy.Applies(religion))
        {
            int legitimacy = PopeLegitimacy.Get(religion);
            rows.Add(Row(LM.Get("religion_pope_legitimacy"), legitimacy.ToString(), legitimacy >= 60 ? "g" : legitimacy >= PopeLegitimacy.AntipopeBelow ? "n" : "b"));
        }
        bool captive = PapacySystem.IsCaptive(religion);
        rows.Add(Row(LM.Get("papacy_state"), SeeState(religion, papacy, captive), captive ? "b" : "g"));
        string throneCulture = throne != null ? CultureDoctrines.CultureOf(throne) : culture;
        if (throne != null)
        {
            rows.Add(Row(LM.Get("pope_tip_throne"), throne.GetEmpireFullName()));
            AddHeirAndMandate(rows, throne, throneCulture);
        }
        rows.Add(Row(CT("emperor_tip_state", culture), throne != null ? EmperorDoing(throne, throneCulture)
            : string.Format(LM.Get("pope_tip_state_see"), holy.GetCityName()), "i"));
        War crusade = PapacySystem.ActiveCrusade(religion);
        if (crusade?.data != null) rows.Add(Row(LM.Get("papacy_crusade_title"), crusade.data.name, "b"));
        int orders = PapacySystem.Orders(religion).Count();
        if (orders > 0) rows.Add(Row(LM.Get("papacy_orders_title"), orders.ToString()));
        return rows;
    }

    private static JArray GradeRow(Actor ruler, string culture)
    {
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(ruler);
        string key = grade switch
        {
            RulerQualitySystem.Grade.Wise => "trait_" + RulerQualitySystem.Wise,
            RulerQualitySystem.Grade.Minor => "emperor_tip_grade_minor",
            RulerQualitySystem.Grade.Weak => "trait_" + RulerQualitySystem.Weak,
            RulerQualitySystem.Grade.Fatuous => "trait_" + RulerQualitySystem.Fatuous,
            RulerQualitySystem.Grade.Dissolute => "trait_" + RulerQualitySystem.Dissolute,
            RulerQualitySystem.Grade.Tyrant => "trait_" + RulerQualitySystem.Tyrant,
            _ => "emperor_tip_grade_average"
        };
        return Row(CT("emperor_tip_grade", culture), CT(key, culture),
            grade == RulerQualitySystem.Grade.Wise ? "g" : grade is RulerQualitySystem.Grade.Average or RulerQualitySystem.Grade.Minor ? "n" : "b");
    }

    private static void AddHeirAndMandate(JArray rows, Empire empire, string culture)
    {
        Kingdom core = empire.CoreKingdom;
        Actor heir = core != null && core.HasHeir() ? core.GetHeir() : null;
        bool designated = core != null && core.GetOrCreate().designated_heir_id >= 0;
        rows.Add(Row(CT("emperor_tip_heir", culture), heir?.data == null ? CT("emperor_tip_no_heir", culture)
            : string.Format(CT(designated ? "emperor_tip_heir_designated" : "emperor_tip_heir_law", culture), heir.getName(), heir.getAge()),
            heir == null ? "b" : "n"));
        rows.Add(Row(CT("emperor_tip_mandate", culture), empire.Mandate.ToString(), "g"));
    }

    private static string SeeState(Religion religion, Kingdom papacy, bool captive)
    {
        if (!captive) return LM.Get("papacy_state_free");
        var data = religion.GetOrCreate();
        int years = data.CaptiveSince >= 0d ? Date.getYearsSince(data.CaptiveSince) : 0;
        return string.Format(LM.Get(data.Schism ? "pope_tip_schism" : "pope_tip_captive"),
            papacy.GetEmpire()?.GetEmpireFullName() ?? papacy.GetKingdomName(), years);
    }

    // 皇帝眼下在做什么(同 EmperorMarkerRenderer.State)
    private static string EmperorDoing(Empire empire, string culture)
    {
        var data = empire.data;
        if (string.IsNullOrEmpty(data.tour_kind))
            return string.Format(CT("emperor_tip_state_palace", culture), EmperorTourSystem.Home(empire)?.GetCityName() ?? "");
        if (data.tour_returning) return CT("emperor_tip_state_returning", culture);
        if (data.tour_kind == EmperorTourSystem.Campaign)
        {
            CorpsData corps = CorpsSystem.Find(empire.CoreKingdom, data.tour_corps);
            return string.Format(CT("emperor_tip_state_campaign", culture), corps == null ? "" : CorpsSystem.Name(corps));
        }
        City city = World.world.cities.get(data.tour_city);
        string where = data.tour_kind == EmperorTourSystem.Visit ? city?.kingdom?.data?.name ?? "" : city?.GetCityName() ?? "";
        return string.Format(CT(data.tour_kind == EmperorTourSystem.Visit ? "emperor_tip_state_visit" : "emperor_tip_state_tour", culture), where);
    }

    private static string CrownText(Empire empire, out string tone)
    {
        PapalRelationState state = PapacySystem.State(empire);
        tone = PapacySystem.IsExcommunicated(empire) ? "b" : PapacySystem.IsCrowned(empire) || PapacySystem.IsHolySee(empire) ? "g" : "b";
        return PapacySystem.IsHolySee(empire) ? LM.Get("papacy_crown_holy_see")
            : PapacySystem.IsExcommunicated(empire) ? LM.Get("papacy_crown_banned")
            : PapacySystem.IsCrowned(empire)
                ? LM.Get(state.self_crowned ? "papacy_crown_self" : state.crowned_by_rival ? "papacy_crown_antipope" : "papacy_crown_pope")
            : LM.Get("papacy_crown_none");
    }

    // ───────── 教廷页(随状态发) ─────────
    // rows 同上；acts 能办的事：id、target(绝罚赦免的帝国)、l 名目(手机照 id 出字时用不上)、no 办不了的缘由(写好的字)、cost 要花的钱
    private static void AddPapacy(JObject state, Seat seat)
    {
        try
        {
            Kingdom realm = seat.Realm;
            Empire empire = realm.GetEmpire();
            bool emperor = empire?.data != null && empire.CoreKingdom == realm && PapacySystem.IsWestern(empire);
            Religion asPope = PapacySystem.PopeOf(realm);
            Religion religion = asPope ?? (emperor ? PapacySystem.ReligionOf(empire) : realm.religion);
            if (religion?.data == null || PapacySystem.HolyCity(religion) == null) return;
            Kingdom papacy = PapacySystem.Papacy(religion);
            if (asPope == null && !emperor && (papacy == null || !WesternSee(PapacySystem.HolyCity(religion), papacy))) return;
            var page = new JObject { ["church"] = TerminalIntel.Strip(religion.name ?? "") };
            var rows = new JArray();
            float authority = PapacySystem.Authority(religion);
            rows.Add(Row(LM.Get("papacy_authority"), $"{authority:0}", authority >= 60f ? "g" : authority >= 35f ? "n" : "b"));
            Actor pope = PapacySystem.Pope(religion);
            rows.Add(Row(LM.Get("papacy_pope"), pope != null ? pope.getName() + " · " + (papacy?.GetKingdomName() ?? "") : LM.Get("label_none")));
            rows.Add(Row(LM.Get("papacy_holy_city"), PapacySystem.HolyCity(religion)?.GetCityName() ?? ""));
            if (papacy != null) rows.Add(Row(LM.Get("papacy_state"), SeeState(religion, papacy, PapacySystem.IsCaptive(religion)), PapacySystem.IsCaptive(religion) ? "b" : "g"));
            if (PopeLegitimacy.Applies(religion)) rows.Add(Row(LM.Get("religion_pope_legitimacy"), PopeLegitimacy.Get(religion).ToString()));
            War crusade = PapacySystem.ActiveCrusade(religion);
            rows.Add(Row(LM.Get("papacy_crusade_title"), crusade?.data != null ? crusade.data.name : LM.Get("label_none"), crusade?.data != null ? "b" : "n"));
            List<KnightOrderData> orders = PapacySystem.Orders(religion).ToList();
            rows.Add(Row(LM.Get("papacy_orders_title"), orders.Count == 0 ? LM.Get("label_none") : string.Join("、", orders.Select(order => order.name))));
            var acts = new JArray();
            if (emperor && PapacySystem.Applies(empire))
            {
                PapalRelationState relation = PapacySystem.State(empire);
                rows.Add(Row(LM.Get("papacy_coronation"), CrownText(empire, out string tone), tone));
                rows.Add(Row(LM.Get("papacy_investiture"), LM.Get("papacy_investiture_" + PapacySystem.Investiture(empire)), "i"));
                rows.Add(Row(LM.Get("papacy_tithe"), relation.tithe_paid.ToString()));
                if (PapacySystem.InContest(empire))
                    rows.Add(Row(LM.Get("papacy_contest"), string.Format(LM.Get("papacy_contest_years"), Date.getYearsSince(relation.investiture_contest_at)), "b"));
                if (PapacySystem.IsExcommunicated(empire))
                {
                    rows.Add(Row(LM.Get("papacy_ban"), LM.Get(relation.excommunication_reason), "b"));
                    acts.Add(new JObject
                    {
                        ["id"] = "papal_penance",
                        ["cost"] = Math.Min(Math.Max(0L, (long)realm.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(realm) * 0.5f))
                    });
                }
                else if (!PapacySystem.IsCrowned(empire) && !PapacySystem.IsHolySee(empire))
                {
                    var act = new JObject { ["id"] = "papal_crown" };
                    if (PapacySystem.CrownBlocker(empire) is { } blocked) act["no"] = string.Format(LM.Get(blocked), empire.GetEmpireFullName());
                    else if (HolyRomanSystem.Romzug(empire) == HolyRomanSystem.RomzugTravelling) act["no"] = LM.Get("terminal_papal_travelling");
                    acts.Add(act);
                }
            }
            if (asPope != null)
            {
                page["pope"] = true;
                foreach (Empire target in PapacySystem.SeatBannable(asPope))
                    acts.Add(new JObject
                    {
                        ["id"] = "papal_ban", ["target"] = target.data.id.ToString(), ["l"] = TerminalIntel.Strip(target.GetEmpireFullName()),
                        ["who"] = TerminalIntel.Strip(target.Emperor?.getName() ?? ""), ["banned"] = PapacySystem.IsExcommunicated(target)
                    });
                var call = new JObject { ["id"] = "papal_crusade" };
                if (PapacySystem.SeatCrusadeBlocker(asPope) is { } why) call["no"] = LM.Get("terminal_papal_" + why);
                acts.Add(call);
                var found = new JObject { ["id"] = "papal_order" };
                if (PapacySystem.SeatOrderBlocker(asPope) is { } whyNot) found["no"] = LM.Get("terminal_papal_" + whyNot);
                acts.Add(found);
            }
            page["rows"] = rows;
            page["acts"] = acts;
            state["papacy"] = page;
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][终端] 备教廷页出错: {exception.Message}");
        }
    }

    // ───────── 教廷的令(主线程) ─────────
    private static bool PapacyDecree(Seat seat, string id, string target)
    {
        if (id is not ("papal_crown" or "papal_penance" or "papal_ban" or "papal_crusade" or "papal_order")) return false;
        Kingdom realm = seat.Realm;
        Empire empire = realm.GetEmpire();
        Religion asPope = PapacySystem.PopeOf(realm);
        string result;
        string done = "terminal_papal_done_" + id.Substring(6);
        switch (id)
        {
            case "papal_crown":
            case "papal_penance":
                if (empire?.data == null || empire.CoreKingdom != realm)
                {
                    result = "regime";
                    break;
                }
                result = id == "papal_crown" ? PapacySystem.SeatCoronation(empire) : PapacySystem.SeatPenance(empire);
                if (result is PapacySystem.SeatRomzug or PapacySystem.SeatSelf)
                {
                    done = result == PapacySystem.SeatSelf ? "terminal_papal_done_self" : "terminal_papal_done_romzug";
                    result = PapacySystem.SeatOk;
                }
                break;
            case "papal_ban":
            {
                Empire victim = long.TryParse(target, out long empireId) ? ModClass.EMPIRE_MANAGER?.get(empireId) : null;
                bool wasBanned = victim != null && PapacySystem.IsExcommunicated(victim);
                result = asPope == null ? "not_pope" : PapacySystem.SeatToggleBan(asPope, victim);
                if (result == PapacySystem.SeatOk)
                {
                    Notify(seat, string.Format(LM.Get(wasBanned ? "terminal_papal_done_pardon" : "terminal_papal_done_ban"),
                        TerminalIntel.Strip(victim.GetEmpireFullName())));
                    seat.NextState = 0f;
                    return true;
                }
                break;
            }
            case "papal_crusade":
                result = asPope == null ? "not_pope" : PapacySystem.SeatCrusade(asPope);
                break;
            default:
                result = asPope == null ? "not_pope" : PapacySystem.SeatFoundOrder(asPope);
                break;
        }
        Notify(seat, result == PapacySystem.SeatOk ? LM.Get(done) : LM.Get("terminal_papal_" + result));
        seat.NextState = 0f;
        return true;
    }
}
