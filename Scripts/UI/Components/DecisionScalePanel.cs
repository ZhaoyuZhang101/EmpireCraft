using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.Decisions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国窗口「天平」页的「决议」一栏(用户 2026-11-14 "要不要发起决议也走博弈""我是说天平"，问答选"这次一起做页面")：
//   ① 本朝(中央)在办的决议：进度、每月推进多少、办了几个月(限几个月)、发起时天平的读数与砝码；
//   ② 本朝最近一回议决：谁、何时、各候选的读数与结果(发起、条件不合、用度不足、未轮到)，各自的推力与阻力砝码；
//   ③ 成员国：各国在办的决议与最近一回议决的结果，一行一国。
// 数都取自 DecisionSystem(见 DecisionScale 的砝码说明)
public static class DecisionScalePanel
{
    private const float CardPadding = 12f;

    // 王国(不在帝国中央)：只看本国在办的与最近一回议决
    public static void Render(AutoVertLayoutGroup page, Kingdom realm, float width)
    {
        if (realm?.data == null || realm.isRekt()) return;
        var intro = page.BeginCard(width, "decision_scale_title", WideKit.Gold, null, "ui/icons/iconKingdom");
        intro.AddParagraph(width, LM.Get("decision_scale_intro"), 7, WideKit.Soft);
        var running = page.BeginCard(width, "decision_scale_running", WideKit.Cyan);
        RenderCases(running, width, realm);
        var last = page.BeginCard(width, "decision_scale_last", WideKit.Cyan);
        RenderCouncil(last, width, DecisionSystem.LastCouncilOf(realm));
    }

    public static void Render(AutoVertLayoutGroup page, Empire empire, float width)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.data == null || core.isRekt()) return;
        var intro = page.BeginCard(width, "decision_scale_title", WideKit.Gold, null, "ui/icons/iconKingdom");
        intro.AddParagraph(width, LM.Get("decision_scale_intro"), 7, WideKit.Soft);

        var running = page.BeginCard(width, "decision_scale_running", WideKit.Cyan);
        RenderCases(running, width, core);

        var last = page.BeginCard(width, "decision_scale_last", WideKit.Cyan);
        RenderCouncil(last, width, DecisionSystem.LastCouncilOf(core));

        List<Kingdom> members = empire.kingdoms_list?.Where(member => member?.data != null && member != core && !member.isRekt()).ToList();
        if (members == null || members.Count == 0) return;
        var others = page.BeginCard(width, "decision_scale_members", WideKit.Cyan, members.Count.ToString());
        int index = 0;
        foreach (Kingdom member in members.OrderBy(member => member.GetKingdomName()))
        {
            List<DecisionCase> cases = DecisionSystem.CasesOf(member).ToList();
            DecisionSystem.Council council = DecisionSystem.LastCouncilOf(member);
            string now = cases.Count == 0 ? LM.Get("label_none") : string.Join("、", cases.Select(item => Name(item.def)));
            string tried = council == null ? "" : " · " + string.Format(LM.Get("decision_scale_member_last"),
                council.options.Where(option => option.outcome == "decision_outcome_started").Select(option => Name(option.def)).FirstOrDefault() ??
                LM.Get("decision_scale_member_none"));
            others.AddValueRow(width, index++, member.GetKingdomName(), now + tried, cases.Count > 0 ? WideKit.Value : WideKit.Muted);
        }
    }

    private static void RenderCases(AutoVertLayoutGroup card, float width, Kingdom realm)
    {
        List<DecisionCase> cases = DecisionSystem.CasesOf(realm).ToList();
        if (cases.Count == 0)
        {
            card.AddParagraph(width, LM.Get("decision_scale_running_none"), 7, WideKit.Muted);
            return;
        }
        int index = 0;
        foreach (DecisionCase item in cases.OrderByDescending(item => item.progress / Math.Max(1f, item.effort)))
        {
            float share = item.progress / Math.Max(1f, item.effort);
            string value = string.Format(LM.Get("decision_scale_progress"), item.progress, item.effort, DecisionSystem.PaceOf(item),
                Date.getMonthsSince(item.started), DecisionSystem.LapseMonthsOf(item));
            card.AddBarRow(width, index++, Name(item.def), share, value, WideKit.Value, WideKit.Green, "#3A3F44", labelWidth: 96f, barWidth: 80f);
            string reading = item.weights == null
                ? LM.Get("decision_scale_named")
                : item.personal
                ? LM.Get("decision_scale_personal") + "  " + string.Format(LM.Get("decision_scale_ratio"), item.ratio) + "  " + Weights(item.weights)
                : string.Format(LM.Get("decision_scale_ratio"), item.ratio) + "  " + Weights(item.weights);
            card.AddParagraph(width, reading, 6, WideKit.Muted);
        }
    }

    private static void RenderCouncil(AutoVertLayoutGroup card, float width, DecisionSystem.Council council)
    {
        if (council == null || council.options.Count == 0)
        {
            card.AddParagraph(width, LM.Get("decision_scale_last_none"), 7, WideKit.Muted);
            return;
        }
        card.AddParagraph(width, string.Format(LM.Get("decision_scale_last_by"), council.name, Date.getDate(council.at)), 7, WideKit.Soft);
        int index = 0;
        foreach (var option in council.options)
        {
            string hex = option.outcome switch
            {
                "decision_outcome_started" => WideKit.Green,
                "decision_outcome_skipped" => WideKit.Muted,
                _ => WideKit.Red
            };
            card.AddValueRow(width, index++, Name(option.def),
                string.Format(LM.Get("decision_scale_ratio"), option.ratio) + " · " + LM.Get(option.outcome), hex,
                labelWidth: (width - CardPadding) * 0.45f);
            card.AddParagraph(width, Weights(option.weights), 6, WideKit.Muted);
        }
    }

    // 砝码：推力绿、阻力红，各按倍数从大到小
    private static string Weights(List<ScaleWeight> weights)
    {
        if (weights == null || weights.Count == 0) return "";
        string Side(bool push) => string.Join("、", weights.Where(weight => weight.push == push).OrderByDescending(weight => weight.factor)
            .Select(weight => $"{LM.Get(weight.key)}×{weight.factor:0.00}"));
        string pushes = Side(true), pulls = Side(false);
        string text = LM.Get("decision_scale_push") + "：" + (pushes.Length == 0 ? LM.Get("label_none") : pushes);
        text = text.ColorString(WideKit.Green);
        string against = (LM.Get("decision_scale_pull") + "：" + (pulls.Length == 0 ? LM.Get("label_none") : pulls)).ColorString(WideKit.Red);
        return text + "  " + against;
    }

    private static string Name(string def) => LM.Has("plot_" + def) ? LM.Get("plot_" + def) : def;
}
