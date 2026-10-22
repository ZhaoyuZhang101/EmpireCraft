using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 华夏部落联盟设了诸部议事会(见 TribalCouncilSystem)时的帝国行政页顶部：
//   · 左栏共主与议事会半圆：每席一位议事，按出身的部落上色、同部坐在一起，共主之部加金框；
//   · 右栏还在推举(没施行家天下)时是共主出缺时议事会的计票次序；家天下以后改列各部席位(贵族议政)。
public partial class EmpireBeaurauWindow
{
    [Hotfixable]
    private void InitialTopPartInfoTribalCouncil()
    {
        if (_empire?.CoreKingdom?.data == null) return;
        topSpace = null;
        Kingdom core = _empire.CoreKingdom;
        bool elective = ShanrangSystem.Elective(_empire);
        List<Kingdom> tribes = TribalCouncilSystem.Tribes(_empire);
        List<Actor> members = TribalCouncilSystem.Members(_empire);
        var (card, left, right, column) = BeginTopCard(LM.Get("tribal_council_title"),
            string.Format(LM.Get(elective ? "tribal_council_header" : "tribal_council_header_hereditary"), members.Count,
                tribes.Count), "ui/icons/iconKingdom");
        float inner = _contentWidth - 12f;

        Actor ruler = _empire.Emperor;
        AddPersonCell(left, column, ruler, LM.Get(DynastyNaming.Term(_empire, "emperor")), "#FFCC00",
            string.Format(LM.Get("tribal_council_ruler_detail"), core.GetKingdomName()).ColorString("#C8C2B4"));

        // 议事会：同部坐在一起(共主之部在左)
        List<IGrouping<Kingdom, Actor>> blocs = members.GroupBy(actor => actor.kingdom)
            .Where(group => group.Key != null)
            .OrderBy(group => group.Key == core ? 0 : 1).ThenByDescending(group => group.Count()).ToList();
        var seats = blocs.SelectMany(group => group.Select(_ => new HemicycleChart.Seat
        {
            Color = StateColor(group.Key), Governing = group.Key == core
        })).ToList();
        if (seats.Count == 0)
            seats = tribes.Select(tribe => new HemicycleChart.Seat { Color = StateColor(tribe), Vacant = true }).ToList();
        GameObject chamber = HemicycleChart.Create(left.transform, new Vector2(column, Mathf.Round(column * 0.5f)), seats);
        UIHelper.AttachTextTooltip(chamber, $"tribal_council_chamber_{_empire.id}", LM.Get("tribal_council_title"),
            string.Join("\n", blocs.Select(group =>
                $"{group.Key.GetKingdomName().ColorString(pColor: StateColor(group.Key))}：" +
                string.Join("、", group.Select(actor => actor.getName())))) +
            "\n" + LM.Get("tribal_council_chamber_legend").ColorString("#8FA0A8"));

        // 右栏：推举次序(共主之外的候选，按议事会计票)或各部席位
        List<(Actor candidate, long metric, string value)> ranking;
        string rankTitle;
        if (elective)
        {
            var tally = TribalCouncilSystem.Tally(_empire, ShanrangSystem.Candidates(_empire, ruler));
            bool byVotes = tally.Any(item => item.votes > 0);
            rankTitle = LM.Get(byVotes ? "tribal_council_rank_votes" : "tribal_council_rank_renown");
            ranking = tally.Select(item => byVotes
                ? (item.actor, (long)item.votes, string.Format(LM.Get("senate_votes"), item.votes))
                : (item.actor, (long)item.actor.renown, item.actor.renown.ToString())).ToList();
        }
        else
        {
            rankTitle = LM.Get("tribal_council_rank_seats");
            ranking = tribes.Where(tribe => tribe.hasKing()).Select(tribe =>
            {
                int count = members.Count(actor => actor.kingdom == tribe);
                return (tribe.king, (long)count, string.Format(LM.Get("tribal_council_seat_count"), count));
            }).OrderByDescending(item => item.Item2).ToList();
        }
        AddColumnLabel(right, column, rankTitle);
        long max = ranking.Count == 0 ? 1 : global::System.Math.Max(1L, ranking.Max(item => item.metric));
        for (int i = 0; i < ranking.Count && i < SenateRankRows; i++)
            AddSenateRankRow(right, column, i + 1, ranking[i].candidate, ranking[i].metric / (float)max, ranking[i].value,
                ranking[i].candidate == ruler);

        // 通栏：推举规则、各部席位
        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner,
            LM.Get(elective ? "tribal_council_rule" : "tribal_council_rule_hereditary").ColorString("#7FD8EA"));
        if (members.Count == 0)
            AddFooterLine(card, inner, LM.Get("tribal_council_forming").ColorString("#E9A85B"), 6);
        else
            AddFooterLine(card, inner, string.Format(LM.Get("senate_seats_line"), string.Join(" · ", blocs
                .Select(group => $"{group.Key.GetKingdomName()}{group.Count()}"))).ColorString("#8FA0A8"), 6);
    }
}
