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
//   · 施行「元老院」后画元老院：每个成员城邦一席(城邦之主出任，按城邦旗色)，执政官出身的城邦加金框；
//     右栏是执政官出缺时的推举次序(与 OfficeSelector.RankElectiveCandidates 同一套规则)；
//   · 之前画城邦同盟：同样的城邦席位，右栏按兵力排接任次序(最强者为首)。
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
        var (card, left, right, column) = BeginTopCard(LM.Get(senate ? "senate_title" : "city_league_title"),
            string.Format(LM.Get(senate ? "senate_header" : "city_league_header"), states.Count), "ui/icons/iconKingdom");
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

        // 议事厅：一城邦一席
        var seats = states.Select(kingdom => new HemicycleChart.Seat
        {
            Color = StateColor(kingdom), Governing = kingdom == home, Vacant = !kingdom.hasKing()
        }).ToList();
        GameObject chamber = HemicycleChart.Create(left.transform, new Vector2(column, Mathf.Round(column * 0.5f)), seats);
        UIHelper.AttachTextTooltip(chamber, $"senate_chamber_{_empire.id}", LM.Get(senate ? "senate_title" : "city_league_title"),
            string.Join("\n", states.Select(kingdom =>
                $"{kingdom.GetKingdomName().ColorString(pColor: StateColor(kingdom))} · " +
                $"{(kingdom.hasKing() ? kingdom.king.getName() : LM.Get("label_none"))} · {FactionName(kingdom.king)}")) +
            "\n" + LM.Get("senate_chamber_legend").ColorString("#8FA0A8"));

        // 右栏：出缺时的推举/接任次序
        List<(Actor candidate, long metric, string value)> ranking;
        string rankTitle;
        if (senate)
        {
            List<(Actor candidate, int votes)> elective = OfficeSelector.RankElectiveCandidates(core);
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

        // 通栏：推举规则与派系构成
        AddFooterSpacer(card, inner);
        AddFooterLine(card, inner, LM.Get(senate ? "senate_rule" : "city_league_rule").ColorString("#7FD8EA"));
        string factions = string.Join(" · ", states.Where(kingdom => kingdom.hasKing())
            .GroupBy(kingdom => FactionName(kingdom.king)).OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} {group.Count()}"));
        if (!string.IsNullOrEmpty(factions))
            AddFooterLine(card, inner, string.Format(LM.Get("senate_factions"), factions).ColorString("#8FA0A8"), 6);
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
