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

    private const int MaxCourtFactions = 6;
    private static readonly string[] FactionColors = { "o", "i", "p", "g", "a", "u", "w", "b" };
    private static readonly Dictionary<string, string> FactionActIcons = new() { ["reward"] = "x_wealth", ["rebuke"] = "g_law", ["dismiss"] = "i_dead" };

    // 像素面板(见 TerminalHost.Panels)：一张「权柄」卡(各派占比的堆叠条、权威、失衡、积弊)，有权臣时一张红框的权臣卡，再一张各派的人物卡
    //(首领的脸、圣眷与怨望两条读数、能动用的筹码)
    private static JObject CourtJson(Seat seat) => CachedPanel(seat, "court", () => BuildCourt(seat));

    private static JObject BuildCourt(Seat seat)
    {
        Empire empire = OwnEmpire(seat);
        if (empire?.data == null) return null;
        List<FixedFaction> factions = CourtSystem.Factions(empire).OrderByDescending(faction => faction.CentralRatio).ToList();
        Actor minister = empire.GetPowerfulMinister();
        if (factions.Count == 0 && minister == null) return null;
        var cards = new List<JObject>();

        JObject power = XCard(LM.Get("terminal_court_power_card"), "g_court");
        int crisis = CourtSystem.State(empire)?.crisis_years ?? 0;
        if (crisis > 0)
        {
            power["tag"] = string.Format(LM.Get("court_lbl_crisis"), crisis);
            power["tagTone"] = "b";
        }
        var parts = new List<(string, float, string)>();
        float total = 0f;
        for (int index = 0; index < factions.Count; index++)
        {
            float ratio = Convert.ToSingle(factions[index].CentralRatio);
            parts.Add((TerminalIntel.Strip(factions[index].Name ?? ""), ratio, FactionColors[index % FactionColors.Length]));
            total += ratio;
        }
        if (total < 100f) parts.Add((LM.Get("terminal_court_unaligned"), 100f - total, "k"));
        power.XAdd(XStack(parts));
        int authority = Authority(empire.CoreKingdom);
        power.XAdd(XGauge(LM.Get("terminal_policy_authority"), authority, 100f, authority.ToString(), "i", "k_mandate"));
        float imbalance = FactionBalanceSystem.Imbalance(empire);
        if (imbalance >= 1f)
            power.XAdd(XGauge(LM.Get("terminal_court_imbalance"), imbalance, 100f, Mathf.RoundToInt(imbalance).ToString(), imbalance >= 60f ? "b" : null, null, 60f));
        float decay = DynasticCycleSystem.GetPressure(empire);
        if (decay >= 1f)
            power.XAdd(XGauge(LM.Get("terminal_court_decay"), decay, 100f, Mathf.RoundToInt(decay).ToString(), "b"));
        cards.Add(power);

        if (minister != null)
        {
            JObject card = XCard(LM.Get("court_lbl_minister"), "x_minister", "b");
            var weights = new List<ScaleWeight>();
            float ratio = CourtActions.CurbScale(empire, weights);
            FixedFaction side = CourtActions.MinisterFaction(empire, minister);
            int appeaseWait = CourtActions.Wait(empire, "minister", "appease"), curbWait = CourtActions.Wait(empire, "minister", "curb");
            float progress = Convert.ToSingle(empire.data.powerful_minister_progress);
            card.XAdd(new JObject
            {
                ["t"] = "who", ["look"] = Look(minister), ["pid"] = minister.getID().ToString(), ["name"] = minister.getName(),
                ["sub"] = side == null ? null : TerminalIntel.Strip(side.Name ?? ""), ["tag"] = Mathf.RoundToInt(progress) + "%", ["tagTone"] = "b",
                ["tone"] = "b",
                ["bars"] = new JArray(new JObject { ["label"] = LM.Get("terminal_court_minister_progress"), ["v"] = Mathf.RoundToInt(progress), ["tone"] = "b" }),
                ["note"] = TerminalIntel.Strip(empire.GetPowerfulMinisterStatusText() ?? "").Replace("\n", " · ") + " · " +
                           string.Format(LM.Get("decision_scale_ratio"), ratio),
                ["chips"] = new JArray(
                    XChip("minister:appease", LM.Get("court_act_appease"), "x_wealth", string.Format(LM.Get("court_act_cost"), CourtActions.AppeaseCost(empire)),
                        appeaseWait > 0 ? string.Format(LM.Get("court_act_wait"), appeaseWait) : null, "g"),
                    XChip("minister:curb", LM.Get("court_act_curb"), "g_law",
                        string.Format(LM.Get(ratio >= CourtActions.CurbHigh ? "court_curb_dismiss" : ratio >= 1f ? "court_curb_ok" : "court_curb_fail"), ratio) +
                        " " + string.Join("、", WeightsJson(weights, true).Concat(WeightsJson(weights, false)).Select(token => token.ToString())),
                        curbWait > 0 ? string.Format(LM.Get("court_act_wait"), curbWait) : null, "b"))
            });
            cards.Add(card);
        }

        if (factions.Count > 0)
        {
            JObject card = XCard(LM.Get("terminal_court_factions_card"), "g_staff");
            card["note"] = LM.Get("terminal_court_factions_note");
            foreach (FixedFaction faction in factions.Take(MaxCourtFactions))
            {
                CourtFactionState state = CourtSystem.FactionState(empire, faction);
                string id = faction.GetID();
                Actor leader = faction.GetLeader();
                bool alive = leader != null && leader.isAlive();
                var chips = new JArray();
                foreach (string kind in FactionActs)
                {
                    int wait = CourtActions.Wait(empire, id, kind);
                    chips.Add(XChip("faction:" + id + ":" + kind, LM.Get("court_act_" + kind), FactionActIcons[kind],
                        kind == "reward" ? string.Format(LM.Get("court_act_cost"), CourtActions.RewardCost(empire)) : LM.Get("terminal_court_" + kind + "_tip"),
                        wait > 0 ? string.Format(LM.Get("court_act_wait"), wait)
                        : kind == "dismiss" && CourtActions.LeaderOffices(empire, faction).Count == 0 ? LM.Get("court_act_no_office") : null,
                        kind == "reward" ? "g" : kind == "dismiss" ? "b" : null));
                }
                var notes = new List<string> { LM.Get("court_lbl_capital") + " " + Mathf.RoundToInt(state.capital) };
                if (state.obstruction > 0f) notes.Add(LM.Get("court_lbl_obstruction") + " " + Mathf.RoundToInt(state.obstruction * 100f) + "%");
                FixedFaction ally = string.IsNullOrEmpty(state.ally) ? null : CourtSystem.FindFaction(empire, state.ally);
                if (ally != null) notes.Add(LM.Get("court_lbl_ally") + " " + TerminalIntel.Strip(ally.Name ?? ""));
                if (!string.IsNullOrEmpty(state.last_action))
                    notes.Add(LM.Has("court_action_" + state.last_action) ? LM.Get("court_action_" + state.last_action) : state.last_action);
                card.XAdd(new JObject
                {
                    ["t"] = "who", ["look"] = alive ? Look(leader) : null, ["pid"] = alive ? leader.getID().ToString() : null, ["icon"] = "g_court",
                    ["name"] = TerminalIntel.Strip(faction.Name ?? ""), ["sub"] = alive ? LM.Get("court_lbl_leader") + " " + leader.getName() : null,
                    ["tag"] = Mathf.RoundToInt(Convert.ToSingle(faction.CentralRatio)) + "%", ["tagTone"] = "o",
                    ["tone"] = state.grievance >= 60f ? "b" : null,
                    ["bars"] = new JArray(
                        new JObject { ["label"] = LM.Get("court_lbl_favor"), ["v"] = Mathf.RoundToInt(state.favor), ["tone"] = "g" },
                        new JObject { ["label"] = LM.Get("court_lbl_grievance"), ["v"] = Mathf.RoundToInt(state.grievance), ["tone"] = "b", ["mark"] = 60 }),
                    ["note"] = string.Join(" · ", notes), ["chips"] = chips
                });
            }
            cards.Add(card);
        }
        return XPanelOf(LM.Get("court_card_title"), cards);
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
