using System;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国主页的"天下大势"(周制，见 ZhouEraSystem)：时代、霸主、诸侯数、天命；列国国力与爵位、与周室的关系；
// 神力按钮(召集会盟、赐胙立霸主、推进时代)；列国纪事
public static class ZhouPanel
{
    private const int MaxRows = 12;

    public static void Render(AutoVertLayoutGroup parent, Empire empire, float width, Action refresh)
    {
        if (parent == null || !ZhouEraSystem.Applies(empire)) return;
        ZhouEraState state = ZhouEraSystem.State(empire);
        float inner = width - 12f;
        int years = state.era_since < 0d ? 0 : Date.getYearsSince(state.era_since);
        string eraName = LM.Get("zhou_era_" + state.era);
        var card = parent.BeginCard(width, "zhou_title", "#F3C34A",
            string.Format(LM.Get("zhou_header"), eraName, years), "ui/icons/iconKingdom");

        var kpis = card.BeginHoriGroup(new Vector2(inner, 24f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        float kpi = (inner - 9f) / 4f;
        UIHelper.AddKpiTile(kpis, kpi, LM.Get("zhou_kpi_era"), eraName);
        Kingdom hegemon = World.world.kingdoms.get(state.hegemon_id);
        Kingdom rival = World.world.kingdoms.get(state.coalition_target);
        if (state.era >= ZhouEraSystem.Warring)
            UIHelper.AddKpiTile(kpis, kpi, LM.Get("zhou_kpi_coalition"),
                rival == null ? "—" : rival.GetKingdomName(), "#E8584F");
        else
            UIHelper.AddKpiTile(kpis, kpi, LM.Get("zhou_kpi_hegemon"), hegemon == null ? "—" : hegemon.GetKingdomName());
        var vassals = ZhouEraSystem.Vassals(empire).OrderByDescending(ZhouEraSystem.Power).ToList();
        UIHelper.AddKpiTile(kpis, kpi, LM.Get("zhou_kpi_vassals"), vassals.Count.ToString(), "#F2EEE2");
        UIHelper.AddKpiTile(kpis, kpi, LM.Get("zhou_kpi_mandate"), empire.Mandate.ToString(),
            empire.Mandate < 30 ? "#E8584F" : empire.Mandate < 60 ? "#E9A85B" : "#65D66E");

        // 列国表：诸侯 | 爵 | 国力 | 城·兵 | 与周室 | (赐胙)
        float[] columns = { 0.17f, 0.07f, 0.2f, 0.16f, 0.28f, 0.12f };
        float W(int i) => inner * columns[i] - 2f;
        var header = card.BeginHoriGroup(new Vector2(inner, 10f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
        string[] keys = { "zhou_col_state", "zhou_col_rank", "zhou_col_power", "zhou_col_size", "zhou_col_relation", "" };
        for (int i = 0; i < keys.Length; i++)
            header.AddTextIntoHoriLayout(keys[i].Length == 0 ? "" : LM.Get(keys[i]).ColorString("#8FA0A8"), true,
                TextAnchor.MiddleLeft, new Vector2(W(i), 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        int strongest = Mathf.Max(1, vassals.Select(ZhouEraSystem.Power).DefaultIfEmpty(1).Max());
        foreach (Kingdom vassal in vassals.Take(MaxRows))
        {
            var row = card.BeginHoriGroup(new Vector2(inner, 12f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            var name = row.AddTextIntoHoriLayout(vassal.GetKingdomName(), true, TextAnchor.MiddleLeft, new Vector2(W(0), 12f));
            name.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(name);
            row.AddTextIntoHoriLayout(Rank(vassal).ColorString("#E9A85B"), true, TextAnchor.MiddleLeft, new Vector2(W(1), 12f))
                .UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            var powerCell = row.BeginHoriGroup(new Vector2(W(2), 12f), TextAnchor.MiddleLeft, 0, new RectOffset(0, 0, 0, 0));
            Color color = vassal.getColor()?.getColorMain() ?? Color.gray;
            UIHelper.AddShareBar(powerCell.transform, new Vector2(W(2) - 4f, 5f),
                ZhouEraSystem.Power(vassal) / (float)strongest, color);
            row.AddTextIntoHoriLayout(string.Format(LM.Get("zhou_size"), vassal.cities.Count, vassal.countTotalWarriors())
                    .ColorString("#C8C2B4"), true, TextAnchor.MiddleLeft, new Vector2(W(3), 12f))
                .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            row.AddTextIntoHoriLayout(Relation(empire, state, vassal), true, TextAnchor.MiddleLeft, new Vector2(W(4), 12f))
                .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            if (state.era == ZhouEraSystem.Spring && state.hegemon_id != vassal.id)
            {
                Kingdom target = vassal;
                row.AddButtonIntoHoriLayout($"zhou_bestow_{vassal.id}", LM.Get("zhou_god_bestow"), () =>
                {
                    ZhouEraSystem.HoldConference(empire, state, target);
                    refresh?.Invoke();
                }, size: new Vector2(W(5), 10f));
            }
        }
        if (vassals.Count > MaxRows)
            card.AddTextIntoVertLayout(string.Format(LM.Get("zhou_more"), vassals.Count - MaxRows).ColorString("#8FA0A8"),
                true, TextAnchor.MiddleLeft, new Vector2(inner, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);

        card.AddTextIntoVertLayout(LM.Get("zhou_rule_" + state.era).ColorString("#7FD8EA"), true, TextAnchor.UpperLeft,
            new Vector2(inner, 18f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);

        // 神力
        if (state.era < ZhouEraSystem.Unified)
        {
            var buttons = card.BeginHoriGroup(new Vector2(inner, 13f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
            if (state.era == ZhouEraSystem.Spring)
                buttons.AddButtonIntoHoriLayout("zhou_god_conference", LM.Get("zhou_god_conference"), () =>
                {
                    ZhouEraSystem.HoldConference(empire, state, null);
                    refresh?.Invoke();
                }, size: new Vector2(46f, 12f));
            buttons.AddButtonIntoHoriLayout("zhou_god_advance", LM.Get("zhou_god_advance_" + state.era), () =>
            {
                ZhouEraSystem.GodAdvance(empire);
                refresh?.Invoke();
            }, size: new Vector2(56f, 12f));
        }

        // 列国纪事
        var log = parent.BeginCard(width, "zhou_log_title", "#7FD8EA", null, "ui/icons/iconBook");
        foreach (CourtLogEntry entry in state.log.Take(8))
        {
            string when = Date.getYearsSince(entry.time) <= 0 ? LM.Get("court_log_this_year")
                : string.Format(LM.Get("court_log_years_ago"), Date.getYearsSince(entry.time));
            log.AddTextIntoVertLayout($"{when.ColorString("#8FA0A8")} {entry.text}", true, TextAnchor.UpperLeft,
                new Vector2(inner, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);
        }
        if (state.log.Count == 0)
            log.AddTextIntoVertLayout(LM.Get("zhou_log_none").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
                new Vector2(inner, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);
    }

    private static string Rank(Kingdom kingdom)
    {
        var office = kingdom.GetOffice();
        return office == null ? "—" : LM.Get(DynastyNaming.RankKey(office.regimeType, office.officeType, kingdom));
    }

    // 与周室的关系：霸主、称王/僭越、变法、合纵、正在征伐谁
    private static string Relation(Empire empire, ZhouEraState state, Kingdom vassal)
    {
        var tags = new global::System.Collections.Generic.List<string>();
        if (state.hegemon_id == vassal.id) tags.Add(LM.Get("zhou_tag_hegemon").ColorString("#F3C34A"));
        if (state.kings.Contains(vassal.id))
            tags.Add(LM.Get(state.era == ZhouEraSystem.Spring ? "zhou_tag_usurper" : "zhou_tag_king").ColorString("#E8584F"));
        if (state.reforms.Contains(vassal.id)) tags.Add(LM.Get("zhou_tag_reform").ColorString("#65D66E"));
        if (state.coalition_target == vassal.id) tags.Add(LM.Get("zhou_tag_dominant").ColorString("#E8584F"));
        else if (state.coalition.Contains(vassal.id)) tags.Add(LM.Get("zhou_tag_coalition").ColorString("#7FA8E8"));
        foreach (War war in vassal.getWars().Take(2))
        {
            if (war == null || war.hasEnded()) continue;
            Kingdom foe = war.isAttacker(vassal) ? war.main_defender : war.main_attacker;
            if (foe == null) continue;
            string key = ZhouEraSystem.IsBarbarian(empire, foe) ? "zhou_tag_repel" : "zhou_tag_war";
            tags.Add(string.Format(LM.Get(key), foe.GetKingdomName()).ColorString("#FFB3A8"));
        }
        if (tags.Count == 0)
            tags.Add(LM.Get(state.era >= ZhouEraSystem.Warring ? "zhou_tag_quiet" : "zhou_tag_loyal").ColorString("#8FA0A8"));
        return string.Join(" ", tags);
    }
}
