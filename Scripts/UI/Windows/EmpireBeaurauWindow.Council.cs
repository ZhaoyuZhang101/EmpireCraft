using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 帝国行政页顶部的两种议事会卡(朝堂博弈的议事会，见 CourtSystem)：
//   · 内阁(律令制)：首辅单独突出(掌票拟)，右侧阁席构成条；阁臣两人一行，写派系与才干；底部吏治效果与皇帝方略；
//   · 选帝侯(封建)：首席选帝侯居首，教会三席、世俗四席分两栏；下面是皇位出缺时按选帝侯派系票数的选举次序。
public partial class EmpireBeaurauWindow
{
    [Hotfixable]
    private void InitialTopPartInfoLvLing()
    {
        if (_empire?.CoreKingdom?.data == null) return;
        topSpace = null;
        List<Actor> members = _empire.GetCabinetMembers() ?? new List<Actor>();
        int size = Mathf.Max(members.Count, CourtSystem.CabinetSize(_empire));
        var (card, left, right, column) = BeginTopCard(LM.Get("cabinet_title"),
            string.Format(LM.Get("cabinet_header"), members.Count, size), "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;

        // 首辅：掌票拟
        Actor leader = members.FirstOrDefault();
        AddPersonCell(left, column, leader, LM.Get("cabinet_leader"), "#FFCC00",
            JoinDetail(FactionTag(leader), CompetenceTag(leader)));
        SimpleText drafting = AddColumnLabel(left, column, LM.Get("cabinet_drafting_power"));
        UIHelper.AttachTextTooltip(drafting.gameObject, $"cabinet_drafting_{_empire.id}", LM.Get("cabinet_leader"),
            LM.Get("cabinet_drafting_body"));

        // 阁席构成
        AddColumnLabel(right, column, LM.Get("cabinet_composition"));
        List<IGrouping<FixedFaction, Actor>> blocs = members.GroupBy(actor => actor.GetFaction())
            .OrderByDescending(group => group.Count()).ToList();
        var segments = blocs.Select(group => (FactionColor(group.Key), group.Count())).ToList();
        if (size > members.Count) segments.Add((new Color(0.2f, 0.22f, 0.19f), size - members.Count));
        if (segments.Count > 0) LegislatureCharts.CreateControlBar(right.transform, new Vector2(column, 8f), segments,
            majorityLine: true);
        foreach (IGrouping<FixedFaction, Actor> group in blocs)
        {
            string name = group.Key?.Name ?? LM.Get("senate_no_faction");
            AddColumnLabel(right, column, $"{name.ColorString(pColor: FactionColor(group.Key))} " +
                                          string.Format(LM.Get("cabinet_seats"), group.Count()));
        }

        // 阁臣：两人一行
        if (size > 1)
        {
            var columns = card.BeginColumns(inner);
            var ministersLeft = TopColumn(columns, column);
            var ministersRight = TopColumn(columns, column);
            for (int i = 1; i < size; i++)
            {
                Actor minister = i < members.Count ? members[i] : null;
                AddPersonCell(i % 2 == 1 ? ministersLeft : ministersRight, column, minister,
                    LM.Get("cabinet_minister"), "#59D9FF",
                    minister == null ? LM.Get("cabinet_vacant").ColorString("#8FA0A8")
                        : JoinDetail(FactionTag(minister), CompetenceTag(minister)));
            }
        }

        // 吏治与方略
        AddFooterSpacer(card, inner);
        float efficiency = CourtSystem.CabinetEfficiency(_empire);
        float average = members.Count == 0 ? 0f : members.Average(CourtSystem.Competence);
        string corruption = (efficiency > 0f ? "-" : "+") + Mathf.Abs(efficiency * 2f).ToString("0.0") + "%";
        AddFooterLine(card, inner, string.Format(LM.Get("cabinet_efficiency"), average.ToString("0"), corruption)
            .ColorString(efficiency >= 0f ? "#65D66E" : "#E9A85B"));
        AddStrategyLine(card, inner);
    }

    [Hotfixable]
    private void InitialTopPartInfoFeudalism()
    {
        if (_empire?.CoreKingdom?.data == null) return;
        topSpace = null;
        List<Actor> electors = _empire.GetCabinetMembers() ?? new List<Actor>();
        var (card, left, right, column) = BeginTopCard(LM.Get("electors_title"),
            string.Format(LM.Get("electors_header"), electors.Count, 7), "ui/icons/iconCrown");
        float inner = _contentWidth - 12f;
        Actor chief = electors.FirstOrDefault();

        // 教会选帝侯是高神权国的城主，世俗选帝侯是最强的几位成员国君主
        List<Actor> ecclesiastical = electors.Where(actor => !actor.isKing()).ToList();
        List<Actor> secular = electors.Where(actor => actor.isKing()).ToList();
        AddColumnLabel(left, column, LM.Get("electors_ecclesiastical_group"));
        for (int i = 0; i < 3; i++)
            AddElectorRow(left, column, i < ecclesiastical.Count ? ecclesiastical[i] : null, chief, "#C9A7E8");
        AddColumnLabel(right, column, LM.Get("electors_secular_group"));
        for (int i = 0; i < 4; i++)
            AddElectorRow(right, column, i < secular.Count ? secular[i] : null, chief, "#FFCC00");

        // 皇位出缺时的选举次序
        AddFooterSpacer(card, inner);
        List<(Actor candidate, int votes)> ranking = OfficeSelector.RankElectiveCandidates(_empire.CoreKingdom);
        AddFooterLine(card, inner, LM.Get("electors_ranking").ColorString("#8FA0A8"), 6);
        int max = ranking.Count == 0 ? 1 : Mathf.Max(1, ranking.Max(item => item.votes));
        for (int i = 0; i < ranking.Count && i < 4; i++)
            AddSenateRankRow(card, inner, i + 1, ranking[i].candidate, ranking[i].votes / (float)max,
                string.Format(LM.Get("senate_votes"), ranking[i].votes), ranking[i].candidate == _empire.Emperor);

        AddFooterLine(card, inner, LM.Get("electors_rule").ColorString("#7FD8EA"));
        string factions = string.Join(" · ", electors.GroupBy(FactionName).OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} {group.Count()}"));
        if (!string.IsNullOrEmpty(factions))
            AddFooterLine(card, inner, string.Format(LM.Get("senate_factions"), factions).ColorString("#8FA0A8"), 6);
        AddStrategyLine(card, inner);
    }

    // 选帝侯一行：色条 | 头衔(首席标金) 姓名 · 所领之国 · 派系
    private void AddElectorRow(AutoVertLayoutGroup column, float width, Actor actor, Actor chief, string titleHex)
    {
        var row = column.BeginHoriGroup(new Vector2(width, 12f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        AddColorStrip(row, actor == null ? new Color(0.2f, 0.22f, 0.19f) : FactionColor(actor.GetFaction()));
        string text;
        if (actor == null)
        {
            text = LM.Get("cabinet_vacant").ColorString("#8FA0A8");
        }
        else
        {
            string title = actor == chief ? LM.Get("electors_chief").ColorString("#F3C34A") + " " : "";
            text = title + actor.getName().ColorString(titleHex) + " · " +
                   (actor.kingdom?.GetKingdomName() ?? "").ColorString("#C8C2B4") + " · " + FactionTag(actor);
        }
        SimpleText label = row.AddTextIntoHoriLayout(text, true, TextAnchor.MiddleLeft, new Vector2(width - 6f, 12f));
        label.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(label);
    }

    private static void AddColorStrip(AutoHoriLayoutGroup row, Color color)
    {
        var strip = new GameObject("FactionStrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        strip.transform.SetParent(row.transform, false);
        ((RectTransform)strip.transform).sizeDelta = new Vector2(3f, 9f);
        Image image = strip.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        LayoutElement element = strip.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = 3f;
        element.preferredHeight = element.minHeight = 9f;
    }

    private static string FactionTag(Actor actor)
    {
        FixedFaction faction = Serving(actor) ? actor.GetFaction() : null;
        return faction == null ? LM.Get("senate_no_faction").ColorString("#8FA0A8")
            : faction.Name.ColorString(pColor: FactionColor(faction));
    }

    private static string CompetenceTag(Actor actor) => !Serving(actor) ? ""
        : string.Format(LM.Get("cabinet_competence"), CourtSystem.Competence(actor).ToString("0")).ColorString("#C8C2B4");

    private void AddStrategyLine(AutoVertLayoutGroup card, float inner)
    {
        var state = CourtSystem.State(_empire);
        if (!CourtSystem.Applies(_empire) || string.IsNullOrEmpty(state?.strategy)) return;
        string strategy = LM.Get("court_strategy_" + state.strategy);
        string target = CourtSystem.FindFaction(_empire, state.strategy_target)?.Name;
        if (!string.IsNullOrEmpty(target)) strategy += $"({target})";
        SimpleText line = AddFooterLine(card, inner, string.Format(LM.Get("cabinet_strategy"), strategy)
            .ColorString("#C8C2B4"), 6);
        UIHelper.AttachTextTooltip(line.gameObject, $"cabinet_strategy_{_empire.id}", LM.Get("court_strategy_" + state.strategy),
            LM.Get("court_strategy_desc_" + state.strategy));
    }
}
