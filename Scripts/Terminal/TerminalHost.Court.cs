using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.Decisions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 手机上的朝局(用户 2026-11-15 "我在手机端也看不到派系和权臣等""也没法与他们互动""毕竟朝堂博弈也得有")：
//   · 看：各派占比、首领、圣眷、怨望、资本、掣肘、盟友、今年的动作；权臣是谁、到了哪一步、削权的天平；罢朝危机几年了；
//   · 做(见 CourtActions，照政令一个月后办)：恩赏、申饬、罢其首领；安抚权臣、削权。
// 本朝的事，君主看得见(不受眼界遮掩)；只有帝国的皇帝亲自掌着时才有(王国没有朝堂)
public static partial class TerminalHost
{
    private static readonly string[] FactionActs = { "reward", "rebuke", "dismiss" };

    private static JObject CourtJson(Seat seat)
    {
        Empire empire = OwnEmpire(seat);
        if (empire?.data == null) return null;
        var factions = new JArray();
        foreach (FixedFaction faction in CourtSystem.Factions(empire).OrderByDescending(faction => faction.CentralRatio))
        {
            CourtFactionState state = CourtSystem.FactionState(empire, faction);
            string id = faction.GetID();
            Actor leader = faction.GetLeader();
            var acts = new JArray();
            foreach (string kind in FactionActs)
            {
                int wait = CourtActions.Wait(empire, id, kind);
                var act = new JObject { ["id"] = "faction:" + id + ":" + kind, ["label"] = LM.Get("court_act_" + kind) };
                if (kind == "reward") act["note"] = string.Format(LM.Get("court_act_cost"), CourtActions.RewardCost(empire));
                if (wait > 0) act["off"] = string.Format(LM.Get("court_act_wait"), wait);
                else if (kind == "dismiss" && CourtActions.LeaderOffices(empire, faction).Count == 0) act["off"] = LM.Get("court_act_no_office");
                acts.Add(act);
            }
            FixedFaction ally = string.IsNullOrEmpty(state.ally) ? null : CourtSystem.FindFaction(empire, state.ally);
            factions.Add(new JObject
            {
                ["id"] = id, ["name"] = TerminalIntel.Strip(faction.Name ?? ""), ["ratio"] = faction.CentralRatio,
                ["leader"] = leader != null && leader.isAlive() ? leader.getName() : null,
                ["favor"] = Mathf.RoundToInt(state.favor), ["grievance"] = Mathf.RoundToInt(state.grievance),
                ["capital"] = Mathf.RoundToInt(state.capital), ["obstruction"] = Mathf.RoundToInt(state.obstruction * 100f),
                ["ally"] = ally == null ? null : TerminalIntel.Strip(ally.Name ?? ""),
                ["action"] = string.IsNullOrEmpty(state.last_action) ? null : LM.Has("court_action_" + state.last_action)
                    ? LM.Get("court_action_" + state.last_action) : state.last_action,
                ["acts"] = acts
            });
        }
        var result = new JObject
        {
            ["title"] = LM.Get("court_card_title"), ["note"] = LM.Get("court_card_note"), ["factions"] = factions,
            ["labels"] = new JObject
            {
                ["ratio"] = LM.Get("court_lbl_ratio"), ["leader"] = LM.Get("court_lbl_leader"), ["favor"] = LM.Get("court_lbl_favor"),
                ["grievance"] = LM.Get("court_lbl_grievance"), ["capital"] = LM.Get("court_lbl_capital"),
                ["obstruction"] = LM.Get("court_lbl_obstruction"), ["ally"] = LM.Get("court_lbl_ally"), ["action"] = LM.Get("court_lbl_action"),
                ["minister"] = LM.Get("court_lbl_minister"), ["noMinister"] = LM.Get("court_lbl_no_minister"),
                ["push"] = LM.Get("decision_scale_push"), ["pull"] = LM.Get("decision_scale_pull"),
                ["more"] = LM.Get("court_lbl_more")
            }
        };
        int crisis = CourtSystem.State(empire)?.crisis_years ?? 0;
        if (crisis > 0) result["crisis"] = string.Format(LM.Get("court_lbl_crisis"), crisis);
        Actor minister = empire.GetPowerfulMinister();
        if (minister != null)
        {
            var weights = new List<ScaleWeight>();
            float ratio = CourtActions.CurbScale(empire, weights);
            FixedFaction side = CourtActions.MinisterFaction(empire, minister);
            var acts = new JArray();
            int appeaseWait = CourtActions.Wait(empire, "minister", "appease"), curbWait = CourtActions.Wait(empire, "minister", "curb");
            var appease = new JObject
            {
                ["id"] = "minister:appease", ["label"] = LM.Get("court_act_appease"),
                ["note"] = string.Format(LM.Get("court_act_cost"), CourtActions.AppeaseCost(empire))
            };
            if (appeaseWait > 0) appease["off"] = string.Format(LM.Get("court_act_wait"), appeaseWait);
            var curb = new JObject
            {
                ["id"] = "minister:curb", ["label"] = LM.Get("court_act_curb"),
                ["note"] = string.Format(LM.Get(ratio >= CourtActions.CurbHigh ? "court_curb_dismiss" : ratio >= 1f ? "court_curb_ok" : "court_curb_fail"), ratio)
            };
            if (curbWait > 0) curb["off"] = string.Format(LM.Get("court_act_wait"), curbWait);
            acts.Add(appease);
            acts.Add(curb);
            result["minister"] = new JObject
            {
                ["name"] = minister.getName(), ["faction"] = side == null ? null : TerminalIntel.Strip(side.Name ?? ""),
                ["status"] = TerminalIntel.Strip(empire.GetPowerfulMinisterStatusText() ?? "").Replace("\n", " · "),
                ["progress"] = empire.data.powerful_minister_progress,
                ["ratio"] = string.Format(LM.Get("decision_scale_ratio"), ratio),
                ["push"] = WeightsJson(weights, true), ["pull"] = WeightsJson(weights, false), ["acts"] = acts
            };
        }
        return result;
    }

    // 政令：下到这里记一道命令(一个月后办)；认不得的返回 false
    private static bool CourtOrder(Seat seat, string id, Order made)
    {
        Empire empire = OwnEmpire(seat);
        if (empire == null) return false;
        if (id.StartsWith("faction:", StringComparison.Ordinal))
        {
            string[] parts = id.Split(':');
            if (parts.Length != 3 || !FactionActs.Contains(parts[2])) return false;
            FixedFaction faction = CourtSystem.FindFaction(empire, parts[1]);
            if (faction == null) return false;
            made.Kind = "faction";
            made.Arg = parts[1] + ":" + parts[2];
            made.Text = string.Format(LM.Get("terminal_order_faction_" + parts[2]), TerminalIntel.Strip(faction.Name ?? ""));
            return true;
        }
        if (id == "minister:appease" || id == "minister:curb")
        {
            Actor minister = empire.GetPowerfulMinister();
            if (minister == null) return false;
            made.Kind = "minister";
            made.Arg = id.Substring(9);
            made.Target = minister.getID();
            made.Text = string.Format(LM.Get("terminal_order_minister_" + made.Arg), minister.getName());
            return true;
        }
        return false;
    }

    // 办：成了返回 null，否则返回缘由
    private static string CourtExecute(Seat seat, Order order)
    {
        Empire empire = OwnEmpire(seat);
        if (empire == null) return LM.Get("terminal_fail_generic");
        string failed;
        if (order.Kind == "faction")
        {
            string[] parts = order.Arg.Split(':');
            FixedFaction faction = parts.Length == 2 ? CourtSystem.FindFaction(empire, parts[0]) : null;
            if (faction == null) return LM.Get("court_act_fail");
            failed = parts[1] switch
            {
                "reward" => CourtActions.Reward(empire, faction),
                "rebuke" => CourtActions.Rebuke(empire, faction),
                "dismiss" => CourtActions.DismissLeader(empire, faction),
                _ => "court_act_fail"
            };
            return failed == null ? null : LM.Get(failed);
        }
        if (empire.GetPowerfulMinister()?.getID() != order.Target) return LM.Get("court_act_no_minister");
        if (order.Arg == "appease") failed = CourtActions.Appease(empire);
        else
        {
            failed = CourtActions.Curb(empire, out bool dismissed);
            if (failed == null && dismissed) order.Note = LM.Get("court_curb_dismissed");
        }
        return failed == null ? null : LM.Get(failed);
    }
}
