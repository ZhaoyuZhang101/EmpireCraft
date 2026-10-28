using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国主页的"朝局"：当前奏议(五步进度、廷议条、批红者与预估、神力批红按钮) + 各派形势(势力/圣眷/怨望/资本/今年动作) + 朝局纪事
public static class CourtPanel
{
    private static readonly Color Pending = new(0.36f, 0.38f, 0.33f);

    public static void Render(AutoVertLayoutGroup parent, Empire empire, float width, Action refresh)
    {
        if (parent == null || !CourtSystem.Applies(empire)) return;
        CourtState state = CourtSystem.State(empire);
        List<FixedFaction> factions = CourtSystem.Factions(empire);
        RenderSuccession(parent, empire, state, factions, width, refresh);
        RenderMemorial(parent, empire, state, factions, width, refresh);
        RenderFactions(parent, empire, state, factions, width);
    }

    private static Color FactionColor(FixedFaction faction)
    {
        Color color = faction == null ? Pending : UIHelper.IdeologyColor(PartySystem.LeaningOf(faction));
        color.a = 1f;
        return color;
    }

    #region 议立新君

    // 皇位空缺、群臣议立期间：人选(立亲/立长/立贤/立幼或劝进)、各派拥立与廷议支持、定策者，每位人选一个钦定按钮
    private static void RenderSuccession(AutoVertLayoutGroup parent, Empire empire, CourtState state,
        List<FixedFaction> factions, float width, Action refresh)
    {
        CourtSuccession council = state.succession;
        if (council == null || council.gave_up || council.stage < 0 || council.candidates == null) return;
        float inner = width - 12f;
        string header = string.Format(LM.Get(council.outsider ? "court_succession_header_acclaim" : "court_succession_header"),
            LM.Get("court_succession_stage_" + council.stage));
        var card = parent.BeginCard(width, LM.Get("court_succession_title"), "#E05A4F", header, "ui/icons/iconKings");
        for (int i = 0; i < council.candidates.Count; i++)
        {
            CourtSuccessionCandidate candidate = council.candidates[i];
            bool chosen = council.stage >= 2 && council.chosen == i;
            var row = card.BeginHoriGroup(new Vector2(inner, 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            string name = SuccessionCouncilSystem.Describe(candidate);
            if (chosen) name = name.ColorString("#F3C34A");
            float textWidth = council.stage < 2 ? inner - 40f : inner;
            SimpleText text = row.AddTextIntoHoriLayout(name, true, TextAnchor.MiddleLeft, new Vector2(textWidth, 12f));
            text.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(text);
            if (council.stage < 2)
            {
                int index = i;
                row.AddButtonIntoHoriLayout($"court_succession_god_{i}", LM.Get("court_succession_god"), () =>
                {
                    if (SuccessionCouncilSystem.GodChoose(empire, index)) refresh?.Invoke();
                }, size: new Vector2(36f, 11f));
            }
            if (council.stage < 1) continue;
            var backers = factions.Where(faction => council.backing.TryGetValue(faction.GetID(), out int backed) && backed == i)
                .Select(faction => faction.Name.ColorString(pColor: FactionColor(faction))).ToList();
            string line = backers.Count == 0 ? LM.Get("court_succession_nobody")
                : string.Format(LM.Get("court_succession_backers"), string.Join("、", backers),
                    Mathf.RoundToInt(SuccessionCouncilSystem.Support(council, i)));
            AddLine(card, inner, "    " + line.ColorString("#C8C2B4"), 6);
        }
        (Actor decider, string role) = council.stage >= 2
            ? (null, council.decider_role)
            : SuccessionCouncilSystem.Decider(empire);
        string deciderName = council.stage >= 2 ? council.decider : decider?.getName() ?? "";
        AddLine(card, inner, string.Format(LM.Get("court_succession_decider"),
            LM.Get("court_succession_decider_" + role), deciderName).ColorString("#8FA0A8"), 6);
    }

    #endregion

    #region 当前奏议

    private static void RenderMemorial(AutoVertLayoutGroup parent, Empire empire, CourtState state,
        List<FixedFaction> factions, float width, Action refresh)
    {
        float inner = width - 12f;
        CourtMemorial memorial = state.memorial;
        FixedFaction proposer = CourtSystem.FindFaction(empire, memorial?.proposer);
        string title = memorial == null ? LM.Get("court_title")
            : string.Format(LM.Get("court_memorial_title"), CourtSystem.IssueName(memorial.claim, empire));
        string header = memorial == null ? LM.Get("court_no_memorial")
            : string.Format(LM.Get("court_memorial_header"), proposer?.Name ?? "", LM.Get("court_status_" + memorial.stage));
        // 施行经费(见 ClaimTuning)：准奏施行时从国库支出
        var memorialClaim = memorial == null ? null : CourtSystem.FindClaim(empire, memorial);
        int cost = memorialClaim == null ? 0 : memorialClaim.BudgetPaid ? memorialClaim.PaidBudget : memorialClaim.Budget;
        if (cost > 0) header += " · " + string.Format(LM.Get("court_memorial_cost"), cost);
        var card = parent.BeginCard(width, title, "#F3C34A", header, "ui/icons/iconKingdom");

        // 五步进度
        var steps = card.BeginHoriGroup(new Vector2(inner, 11f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
        int current = memorial == null ? -1 : memorial.stage switch { 0 => 1, 1 => 2, 2 or 3 => 3, _ => 4 };
        float stepWidth = (inner - 2f * 4f) / 5f;
        for (int i = 0; i < 5; i++)
        {
            string hex = current < 0 ? "#5C6153" : i < current ? "#65D66E" : i == current ? "#F3C34A" : "#5C6153";
            string text = $"{i + 1}. {LM.Get(CourtSystem.StageKey(empire, i))}";
            SimpleText step = steps.AddTextIntoHoriLayout(text.ColorString(hex), false, TextAnchor.MiddleCenter,
                new Vector2(stepWidth, 11f));
            step.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        }

        if (memorial != null && memorial.stances.Count > 0) RenderDebate(card, empire, memorial, factions, inner);
        else if (memorial != null)
            AddLine(card, inner, LM.Get("court_debate_pending").ColorString("#8FA0A8"), 6);

        RenderDecider(card, empire, state, memorial, proposer, inner, refresh);

        SimpleText hint = AddLine(card, inner, LM.Get("court_how_hint").ColorString("#8FA0A8"), 6);
        UIHelper.AttachTextTooltip(hint.gameObject, $"court_how_{empire.id}", LM.Get("court_title"),
            string.Format(LM.Get("court_how_body"), CourtSystem.ProposalCost, CourtSystem.DebateThreshold));
    }

    private static void RenderDebate(AutoVertLayoutGroup card, Empire empire, CourtMemorial memorial,
        List<FixedFaction> factions, float inner)
    {
        var known = factions.Where(faction => memorial.stances.ContainsKey(faction.GetID())).ToList();
        float Weight(FixedFaction faction) => memorial.weights.TryGetValue(faction.GetID(), out float weight) ? weight : 0f;
        int Stance(FixedFaction faction) => memorial.stances[faction.GetID()];
        var segments = new List<(Color color, int seats)>();
        segments.AddRange(known.Where(faction => Stance(faction) > 0)
            .Select(faction => (FactionColor(faction), Mathf.RoundToInt(Weight(faction) * 10f))));
        int neutral = Mathf.RoundToInt(known.Where(faction => Stance(faction) == 0).Sum(Weight) * 10f);
        if (neutral > 0) segments.Add((Pending, neutral));
        segments.AddRange(known.Where(faction => Stance(faction) < 0)
            .Select(faction => (Color.Lerp(FactionColor(faction), new Color(0.85f, 0.25f, 0.2f), 0.55f),
                Mathf.RoundToInt(Weight(faction) * 10f))));
        string support = memorial.support < 0f ? "—" : memorial.support.ToString("0");
        AddLine(card, inner, string.Format(LM.Get("court_debate_label"), support, CourtSystem.DebateThreshold)
            .ColorString("#8FA0A8"), 6);
        if (segments.Sum(item => item.seats) > 0)
            LegislatureCharts.CreateControlBar(card.transform, new Vector2(inner, 8f), segments, majorityLine: false);
        string Names(int stance) => string.Join("、", known.Where(faction => Stance(faction) == stance)
            .Select(faction => faction.Name.ColorString(pColor: FactionColor(faction))));
        string sides = $"{LM.Get("court_stance_support")}：{Names(1)}";
        string opposed = Names(-1);
        if (!string.IsNullOrEmpty(opposed)) sides += $"  {LM.Get("court_stance_oppose")}：{opposed}";
        string neutralNames = Names(0);
        if (!string.IsNullOrEmpty(neutralNames)) sides += $"  {LM.Get("court_stance_neutral")}：{neutralNames}";
        AddLine(card, inner, sides, 6);
    }

    private static void RenderDecider(AutoVertLayoutGroup card, Empire empire, CourtState state, CourtMemorial memorial,
        FixedFaction proposer, float inner, Action refresh)
    {
        (Actor decider, string role) = CourtSystem.Decider(empire);
        var row = card.BeginHoriGroup(new Vector2(inner, 32f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        row.transform.localPosition = Vector3.zero;
        row.AddActorViewIntoHoriLayout(decider?.data != null && !decider.isRekt() ? decider : null);
        float textWidth = inner - 34f;
        var text = row.BeginVertGroup(new Vector2(textWidth, 30f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        text.transform.localPosition = Vector3.zero;
        // 穿越政权的首领是执委会主席(元老院主席)，不是皇帝
        string roleName = role == "emperor" && GeneralSystems.TransmigrantSystem.Governs(empire)
            ? LM.Get(GeneralSystems.DynastyNaming.RankKey(Regimes.RegimeType.Transmigrant, 0, empire.CoreKingdom))
            : role == "emperor"
            ? LM.Get(decider?.isSexFemale() == true ? "actor_emperor_G" : "actor_emperor_B")
            : LM.Get("court_decider_" + role);
        string name = decider?.getName() ?? LM.Get("label_none");
        AddLine(text, textWidth, $"{roleName.ColorString("#FFCC00")} {name}", 7);
        string strategy = string.IsNullOrEmpty(state.strategy) ? "" : LM.Get("court_strategy_" + state.strategy);
        string target = CourtSystem.FindFaction(empire, state.strategy_target)?.Name;
        if (!string.IsNullOrEmpty(target)) strategy += $"({target})";
        // 权臣、太后代批时显示的是辅政者的权势(挟制进度)，不是皇帝的威权
        bool regency = role == "minister" && empire.GetPowerfulMinister() is Actor minister && minister == decider;
        SimpleText strategyLine = AddLine(text, textWidth, string.Format(LM.Get(regency ? "court_decider_line_minister" : "court_decider_line"),
            strategy, regency ? empire.data.powerful_minister_progress : empire.Legitimacy).ColorString("#C8C2B4"), 6);
        if (!string.IsNullOrEmpty(state.strategy))
            UIHelper.AttachTextTooltip(strategyLine.gameObject, $"court_strategy_{empire.id}",
                LM.Get("court_strategy_" + state.strategy), LM.Get("court_strategy_desc_" + state.strategy));
        if (memorial != null && memorial.stage is 2 or 3 && proposer != null &&
            Enum.TryParse(memorial.claim, out Regimes.TemporaryFactions.TemporaryFactionType issue))
        {
            float chance = CourtSystem.ApprovalChance(empire, memorial, issue, proposer);
            AddLine(text, textWidth, string.Format(LM.Get("court_assent_estimate"), Mathf.RoundToInt(chance * 100f))
                .ColorString("#65D66E"), 6);
        }

        if (memorial == null || memorial.stage >= 4) return;
        var buttons = card.BeginHoriGroup(new Vector2(inner, 13f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        void Button(string decision)
        {
            buttons.AddButtonIntoHoriLayout($"court_god_{decision}", LM.Get("court_god_" + decision), () =>
            {
                if (CourtSystem.GodDecide(empire, decision)) refresh?.Invoke();
            }, size: new Vector2(36f, 12f));
        }
        Button("approve");
        Button("reject");
        Button("shelve");
    }

    #endregion

    #region 各派形势与纪事

    private static void RenderFactions(AutoVertLayoutGroup parent, Empire empire, CourtState state,
        List<FixedFaction> factions, float width)
    {
        float inner = width - 12f;
        var card = parent.BeginCard(width, LM.Get("court_factions_title"), "#F3C34A",
            LM.Get("court_factions_header"), "ui/icons/iconFamily");
        if (state.crisis_years > 0)
            AddLine(card, inner, string.Format(LM.Get("court_crisis_status"), state.crisis_years,
                Mathf.RoundToInt(CourtSystem.BoycottShare(empire))).ColorString("#E05A4F"), 6);
        float[] columns = { 58f, 28f, 54f, 54f, 26f };
        float actionWidth = inner - columns.Sum() - 2f * columns.Length;
        var head = card.BeginHoriGroup(new Vector2(inner, 9f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
        string[] titles = { "court_col_faction", "court_col_power", "court_col_favor", "court_col_grievance",
            "court_col_capital", "court_col_action" };
        for (int i = 0; i < titles.Length; i++)
            head.AddTextIntoHoriLayout(LM.Get(titles[i]).ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                new Vector2(i < columns.Length ? columns[i] : actionWidth, 9f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);

        foreach (FixedFaction faction in factions.OrderByDescending(faction => faction.CentralRatio))
        {
            CourtFactionState data = CourtSystem.FactionState(empire, faction);
            var row = card.BeginHoriGroup(new Vector2(inner, 11f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            string name = faction.Name.ColorString(pColor: FactionColor(faction));
            if (CourtSystem.IsBoycotting(data)) name += LM.Get("court_boycotting").ColorString("#E05A4F");
            SimpleText nameText = row.AddTextIntoHoriLayout(name, true, TextAnchor.MiddleLeft, new Vector2(columns[0], 11f));
            nameText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(nameText);
            AttachFactionTip(nameText.gameObject, empire, faction, data);
            row.AddTextIntoHoriLayout($"{faction.CentralRatio}%", true, TextAnchor.MiddleLeft, new Vector2(columns[1], 11f))
                .UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            Meter(row, columns[2], data.favor, new Color(0.95f, 0.76f, 0.29f));
            Meter(row, columns[3], data.grievance, new Color(0.88f, 0.35f, 0.31f));
            row.AddTextIntoHoriLayout($"{data.capital:0}", true, TextAnchor.MiddleLeft, new Vector2(columns[4], 11f))
                .UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            string action = string.IsNullOrEmpty(data.last_action) ? "—" : LM.Get("court_action_" + data.last_action);
            if (!string.IsNullOrEmpty(data.last_action_detail)) action += $" {data.last_action_detail}";
            SimpleText actionText = row.AddTextIntoHoriLayout(action.ColorString("#C8C2B4"), true, TextAnchor.MiddleLeft,
                new Vector2(actionWidth, 11f));
            actionText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(actionText);
        }

        if (state.log.Count == 0) return;
        AddLine(card, inner, "", 4);
        AddLine(card, inner, LM.Get("court_log_title").ColorString("#8FA0A8"), 6);
        foreach (CourtLogEntry entry in state.log.Take(6))
        {
            string when = Date.getYearsSince(entry.time) <= 0 ? LM.Get("court_log_this_year")
                : string.Format(LM.Get("court_log_years_ago"), Date.getYearsSince(entry.time));
            AddLine(card, inner, $"{when.ColorString("#8FA0A8")} {entry.text.ColorString("#C8C2B4")}", 6);
        }
    }

    private static void Meter(AutoHoriLayoutGroup row, float width, float value, Color color)
    {
        UIHelper.AddShareBar(row.transform, new Vector2(width - 20f, 3f), Mathf.Clamp01(value / 100f), color);
        row.AddTextIntoHoriLayout($"{value:0}", true, TextAnchor.MiddleLeft, new Vector2(18f, 11f))
            .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
    }

    // 悬停派系名：独特议题、对当前奏议的立场、盟友、在议事会的席位
    private static void AttachFactionTip(GameObject target, Empire empire, FixedFaction faction, CourtFactionState data)
    {
        var lines = new List<string>();
        List<Regimes.TemporaryFactions.TemporaryFactionType> unique = CourtIssues.UniqueIssues(faction);
        if (unique.Count > 0)
            lines.Add($"{LM.Get("court_unique_issues")}：{string.Join("、", unique.Select(CourtSystem.IssueName))}");
        lines.Add($"{LM.Get("court_public_issues")}：" + string.Join("、", CourtIssues.PublicIssuesFor(faction).Select(issue =>
            $"{CourtSystem.IssueName(issue, empire)}{StanceMark(CourtIssues.StanceLevel(faction, issue))}")));
        FixedFaction ally = CourtSystem.FindFaction(empire, data.ally);
        if (ally != null) lines.Add(string.Format(LM.Get("court_ally_mark"), ally.Name));
        int seats = CourtSystem.Council(empire).Count(actor => actor.GetFaction() == faction);
        if (seats > 0) lines.Add(string.Format(LM.Get("court_council_seats"), seats));
        UIHelper.AttachTextTooltip(target, $"court_faction_{faction.GetID()}", faction.Name, string.Join("\n", lines));
    }

    public static string StanceMark(int stance) => stance > 0 ? LM.Get("court_mark_support").ColorString("#65D66E")
        : stance < 0 ? LM.Get("court_mark_oppose").ColorString("#E05A4F")
        : LM.Get("court_mark_neutral").ColorString("#8FA0A8");

    #endregion

    private static SimpleText AddLine(AutoVertLayoutGroup card, float width, string text, int fontSize)
    {
        SimpleText line = card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(width, fontSize + 3f));
        line.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(line);
        return line;
    }
}
