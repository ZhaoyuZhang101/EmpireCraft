using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems.Decisions;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.Terminal;

// 手机上的决议天平(用户 2026-11-14 "玩家页面也是""客户端")：同帝国窗口的「决议」一栏(见 DecisionScalePanel)，
// 本国在办的决议(进度、每月推进、办了几个月、发起时的砝码)与最近一回议决(各候选的读数、结果、砝码)。
// 文字在这里按游戏的语言取好(手机只管摆)；本国的事，不受眼界与吏治遮掩
public static partial class TerminalHost
{
    private static JObject DecisionScaleJson(Seat seat)
    {
        Kingdom realm = seat.Realm;
        if (realm?.data == null) return null;
        var running = new JArray();
        foreach (DecisionCase item in DecisionSystem.CasesOf(realm).OrderByDescending(item => item.progress / Math.Max(1f, item.effort)))
        {
            running.Add(new JObject
            {
                ["name"] = DecisionName(item.def), ["share"] = Math.Round(item.progress / Math.Max(1f, item.effort), 3),
                ["line"] = string.Format(LM.Get("decision_scale_progress"), item.progress, item.effort, DecisionSystem.PaceOf(item),
                    Date.getMonthsSince(item.started), DecisionSystem.LapseMonthsOf(item)),
                ["ratio"] = item.weights == null ? null : string.Format(LM.Get("decision_scale_ratio"), item.ratio),
                ["named"] = item.personal ? LM.Get("decision_scale_personal") : item.weights == null ? LM.Get("decision_scale_named") : null,
                ["push"] = WeightsJson(item.weights, true), ["pull"] = WeightsJson(item.weights, false)
            });
        }
        var result = new JObject
        {
            ["title"] = LM.Get("decision_scale_title"), ["note"] = LM.Get("decision_scale_intro"),
            ["runningTitle"] = LM.Get("decision_scale_running"), ["runningNone"] = LM.Get("decision_scale_running_none"),
            ["lastTitle"] = LM.Get("decision_scale_last"), ["lastNone"] = LM.Get("decision_scale_last_none"),
            ["pushLabel"] = LM.Get("decision_scale_push"), ["pullLabel"] = LM.Get("decision_scale_pull"),
            ["lastNothing"] = LM.Get("decision_scale_member_none"),
            ["running"] = running
        };
        if (seat.Ruler != null)
        {
            if (seat.DecisionOptions == null || UnityEngine.Time.realtimeSinceStartup >= seat.NextDecisionOptions)
            {
                seat.NextDecisionOptions = UnityEngine.Time.realtimeSinceStartup + DecreeSeconds;
                seat.DecisionOptions = OptionsJson(seat.Ruler);
            }
            result["optionsTitle"] = LM.Get("decision_scale_options");
            result["optionsNote"] = LM.Get("decision_scale_options_note");
            result["launch"] = LM.Get("decision_scale_launch");
            result["busy"] = seat.Ruler.plot != null && seat.Ruler.plot.isActive() ? LM.Get("decision_fail_busy") : null;
            result["options"] = seat.DecisionOptions;
            result["noneFit"] = LM.Get("decision_scale_none_fit");
            result["unfitMore"] = LM.Get("decision_scale_unfit_more");
            result["unfitTitle"] = LM.Get("decision_scale_unfit_title");
        }
        DecisionSystem.Council council = DecisionSystem.LastCouncilOf(realm);
        if (council != null && council.options.Count > 0)
        {
            var options = new JArray();
            foreach (var option in council.options)
                options.Add(new JObject
                {
                    ["name"] = DecisionName(option.def), ["ratio"] = string.Format(LM.Get("decision_scale_ratio"), option.ratio),
                    ["outcome"] = LM.Get(option.outcome),
                    ["tone"] = option.outcome == "decision_outcome_started" ? "good" : option.outcome == "decision_outcome_skipped" ? "dim" : "bad",
                    ["push"] = WeightsJson(option.weights, true), ["pull"] = WeightsJson(option.weights, false)
                });
            result["last"] = new JObject
            {
                ["by"] = string.Format(LM.Get("decision_scale_last_by"), council.name, Date.getDate(council.at)), ["options"] = options
            };
        }
        return result;
    }

    // 可议之事：读数从高到低；办得成的里头读数最高的一项标"群臣建议"
    private static JArray OptionsJson(Actor ruler)
    {
        var list = new JArray();
        bool advised = false;
        foreach (DecisionSystem.Advice advice in DecisionSystem.AdviseFor(ruler))
        {
            bool fit = advice.blocked == null;
            var row = new JObject
            {
                ["id"] = "plot:" + advice.id, ["name"] = DecisionName(advice.id),
                ["ratio"] = string.Format(LM.Get("decision_scale_ratio"), advice.ratio), ["fit"] = fit,
                ["state"] = fit ? null : LM.Get(advice.blocked),
                ["cost"] = advice.cost > 0 ? string.Format(LM.Get("decision_scale_cost"), advice.cost) : null,
                ["push"] = WeightsJson(advice.weights, true), ["pull"] = WeightsJson(advice.weights, false)
            };
            if (fit && !advised && advice.ratio >= DecisionScale.Threshold)
            {
                row["advice"] = LM.Get("decision_scale_advice");
                advised = true;
            }
            list.Add(row);
        }
        return list;
    }

    private static bool SteeredBySeat(Actor actor)
    {
        foreach (Seat seat in SeatByToken.Values)
            if (seat.Ruler == actor) return true;
        return false;
    }

    private static JArray WeightsJson(List<ScaleWeight> weights, bool push)
    {
        var list = new JArray();
        if (weights == null) return list;
        foreach (ScaleWeight weight in weights.Where(weight => weight.push == push).OrderByDescending(weight => weight.factor))
            list.Add(LM.Get(weight.key) + "×" + weight.factor.ToString("0.00"));
        return list;
    }

    private static string DecisionName(string def) => LM.Has("plot_" + def) ? LM.Get("plot_" + def) : def;
}
