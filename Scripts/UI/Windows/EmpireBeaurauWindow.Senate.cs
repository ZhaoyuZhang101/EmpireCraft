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

namespace EmpireCraft.Scripts.UI.Windows;

// 古典共和(城邦同盟)的帝国行政页顶部：
//   · 施行「元老院」后画元老院：每席一位元老(见 SenateSystem)，按派系上色、同派系坐在一起，
//     支持推举第一人选的派系加金框；右栏是执政官出缺时元老按派系投票的次序(与 OfficeSelector.RankElectiveCandidates 同一套)；
//   · 之前画城邦同盟：一城邦一席(城邦旗色，首领出身的城邦加金框)，右栏按兵力排接任次序(最强者为首)。
public partial class EmpireBeaurauWindow
{
    private const int SenateRankRows = 6;

    [Hotfixable]
    private void InitialTopPartInfoSenate()
    {
        if (_empire?.CoreKingdom?.data == null) return;
        topSpace = null;
        Kingdom core = _empire.CoreKingdom;
        bool senate = CityStateService.HasSenate(_empire);
        List<Kingdom> states = _empire.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != core)
            .OrderByDescending(kingdom => kingdom.countTotalWarriors()).ToList();
        List<Actor> senators = senate ? _empire.GetCabinetMembers() : new List<Actor>();
        var (card, left, right, column) = BeginTopCard(LM.Get(senate ? "senate_title" : "city_league_title"),
            string.Format(LM.Get(senate ? "senate_header" : "city_league_header"), senate ? senators.Count : states.Count),
            "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;

        // 执政官：出身的城邦
        Actor consul = _empire.Emperor;
        Kingdom home = Serving(consul) ? states.FirstOrDefault(kingdom => kingdom.king == consul) : null;
        string consulTitle = LM.Has("ClassicalRepublic_officiallevel_0")
            ? LM.Get("ClassicalRepublic_officiallevel_0")
            : LM.Get("legislature_head_of_state");
        AddPersonCell(left, column, consul, consulTitle, "#FFCC00",
            JoinDetail(home == null ? "" : string.Format(LM.Get("senate_leader_detail"), home.GetKingdomName())
                .ColorString("#C8C2B4"), PartyOf(consul)));

        // 右栏的次序先算出来(元老院的金框要标出支持第一人选的派系)
        List<(Actor candidate, long metric, string value)> ranking;
        string rankTitle;
        List<(Actor candidate, int votes)> elective = senate ? OfficeSelector.RankElectiveCandidates(core) : null;

        if (senate && senators.Count > 0)
        {
            // 元老院：每席一位元老，同派系坐在一起(人多的派系在左)
            FixedFaction frontRunner = elective.Count > 0 && elective[0].votes > 0 ? elective[0].candidate.GetFaction() : null;
            List<IGrouping<FixedFaction, Actor>> blocs = senators.GroupBy(actor => actor.GetFaction())
                .OrderByDescending(group => group.Count()).ToList();
            var seats = blocs.SelectMany(group => group.Select(_ => new HemicycleChart.Seat
            {
                Color = FactionColor(group.Key), Governing = elective.Count > 0 && elective[0].votes > 0 && group.Key == frontRunner
            })).ToList();
            GameObject chamber = HemicycleChart.Create(left.transform, new Vector2(column, Mathf.Round(column * 0.5f)), seats);
            UIHelper.AttachTextTooltip(chamber, $"senate_chamber_{_empire.id}", LM.Get("senate_title"),
                string.Join("\n", blocs.Select(group =>
                    $"{FactionName(group.First()).ColorString(pColor: FactionColor(group.Key))} {group.Count()}")) + "\n" +
                string.Join("\n", senators.GroupBy(actor => actor.kingdom).Where(group => group.Key != null).Select(group =>
                    $"{group.Key.GetKingdomName().ColorString(pColor: StateColor(group.Key))}：" +
                    string.Join("、", group.Select(actor => actor.getName())))) +
                "\n" + LM.Get("senate_chamber_legend").ColorString("#8FA0A8"));
        }
        else
        {
            // 城邦同盟(或元老院还没组成)：一城邦一席，首领出身的城邦加金框
            var seats = states.Select(kingdom => new HemicycleChart.Seat
            {
                Color = StateColor(kingdom), Governing = kingdom == home, Vacant = !kingdom.hasKing()
            }).ToList();
            GameObject chamber = HemicycleChart.Create(left.transform, new Vector2(column, Mathf.Round(column * 0.5f)), seats);
            UIHelper.AttachTextTooltip(chamber, $"senate_chamber_{_empire.id}", LM.Get(senate ? "senate_title" : "city_league_title"),
                string.Join("\n", states.Select(kingdom =>
                    $"{kingdom.GetKingdomName().ColorString(pColor: StateColor(kingdom))} · " +
                    $"{(kingdom.hasKing() ? kingdom.king.getName() : LM.Get("label_none"))} · {FactionName(kingdom.king)}")) +
                "\n" + LM.Get("city_league_chamber_legend").ColorString("#8FA0A8"));
        }

        if (senate)
        {
            bool byVotes = elective.Any(item => item.votes > 0);
            rankTitle = LM.Get(byVotes ? "senate_rank_votes" : "senate_rank_renown");
            ranking = elective.Select(item => byVotes
                ? (item.candidate, (long)item.votes, string.Format(LM.Get("senate_votes"), item.votes))
                : (item.candidate, (long)item.candidate.renown, item.candidate.renown.ToString())).ToList();
        }
        else
        {
            rankTitle = LM.Get("city_league_rank");
            ranking = OfficeSelector.RankStrongestCandidates(core).Select(candidate =>
            {
                long warriors = candidate.kingdom?.countTotalWarriors() ?? 0;
                return (candidate, warriors, UIHelper.ShortCount(warriors));
            }).ToList();
        }
        AddColumnLabel(right, column, rankTitle);
        long max = ranking.Count == 0 ? 1 : global::System.Math.Max(1L, ranking.Max(item => item.metric));
        for (int i = 0; i < ranking.Count && i < SenateRankRows; i++)
            AddSenateRankRow(right, column, i + 1, ranking[i].candidate, ranking[i].metric / (float)max, ranking[i].value,
                ranking[i].candidate == consul);

        // 通栏：推举规则、派系构成、各城邦席位
        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner, LM.Get(senate ? "senate_rule" : "city_league_rule").ColorString("#7FD8EA"));
        if (senate && senators.Count == 0)
            AddFooterLine(card, inner, LM.Get("senate_forming").ColorString("#E9A85B"), 6);
        IEnumerable<Actor> members = senate && senators.Count > 0
            ? senators
            : states.Where(kingdom => kingdom.hasKing()).Select(kingdom => kingdom.king);
        string factions = string.Join(" · ", members.GroupBy(FactionName).OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} {group.Count()}"));
        if (!string.IsNullOrEmpty(factions))
            AddFooterLine(card, inner, string.Format(LM.Get("senate_factions"), factions).ColorString("#8FA0A8"), 6);
        if (senate && senators.Count > 0)
            AddFooterLine(card, inner, string.Format(LM.Get("senate_seats_line"), string.Join(" · ", states
                .Select(kingdom => (kingdom, count: senators.Count(actor => actor.kingdom == kingdom)))
                .Where(item => item.count > 0)
                .Select(item => $"{item.kingdom.GetKingdomName()}{item.count}"))).ColorString("#8FA0A8"), 6);
    }

    private static Color FactionColor(FixedFaction faction)
    {
        if (faction == null) return new Color(0.55f, 0.58f, 0.52f);
        Color color = UIHelper.IdeologyColor(PartySystem.LeaningOf(faction));
        color.a = 1f;
        return color;
    }

    // 一行候选人："1. 雅典 · 某某(·现任)"，下面一条按次序指标(票数/威望/兵力)占最大者比例的条
    private void AddSenateRankRow(AutoVertLayoutGroup column, float width, int rank, Actor candidate, float fill,
        string value, bool incumbent)
    {
        Kingdom state = candidate?.kingdom;
        string name = $"{rank}. " + (state == null ? "" : state.GetKingdomName().ColorString(pColor: StateColor(state)) + " · ") +
                      candidate.getName().ColorString(incumbent ? "#F3C34A" : "#F2EEE2") +
                      (incumbent ? LM.Get("senate_incumbent").ColorString("#F3C34A") : "");
        SimpleText label = column.AddTextIntoVertLayout(name, true, TextAnchor.MiddleLeft, new Vector2(width, 10f));
        label.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(label);
        const float valueWidth = 34f;
        var bar = column.BeginHoriGroup(new Vector2(width, 6f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        UIHelper.AddShareBar(bar.transform, new Vector2(width - valueWidth - 3f, 3f), Mathf.Clamp01(fill),
            incumbent ? new Color(0.95f, 0.76f, 0.29f) : new Color(0.62f, 0.68f, 0.56f));
        bar.AddTextIntoHoriLayout(value.ColorString("#C8C2B4"), true, TextAnchor.MiddleRight, new Vector2(valueWidth, 6f))
            .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
    }

    private static Color StateColor(Kingdom kingdom)
    {
        Color color = kingdom?.getColor()?.getColorMain() ?? Color.gray;
        color.a = 1f;
        return color;
    }

    private static string FactionName(Actor actor)
    {
        string name = Serving(actor) ? actor.GetFaction()?.Name : null;
        return string.IsNullOrWhiteSpace(name) ? LM.Get("senate_no_faction") : name;
    }
}
