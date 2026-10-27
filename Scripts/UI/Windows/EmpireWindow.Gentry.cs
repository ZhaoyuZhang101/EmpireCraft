using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 世家页(见 GentrySystem)：主指标 → 世家名录(郡望、门第、族人、出仕、爵位头衔、田产，点一行打开那一族) → 规则说明
    public partial class EmpireWindow
    {
        private const int GentryRows = 30;

        public void ShowGentry(WindowMetaTab pArg0)
        {
            Clear();
            List<SpecificClan> clans = GentrySystem.Of(_empire);
            if (clans.Count == 0)
            {
                var none = BeginPage("empire_gentry");
                none.AddEmptyCard(MainWidth, "gentry_page_none_title", LM.Get("gentry_page_none_hint"));
                StartCoroutine(RefreshScrollLayout(resetToTop: true));
                return;
            }
            var stats = clans.Select(clan => (clan, row: Stats(clan))).ToList();
            List<Actor> officials = _empire.getUnits().Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                                      actor.IsOnOffice() && actor.id != _empire.Emperor?.id).ToList();
            int fromGentry = officials.Count(GentrySystem.IsGentry);
            float officialShare = officials.Count == 0 ? 0f : fromGentry / (float)officials.Count;
            var page = BeginPage("empire_gentry", string.Format(LM.Get("gentry_chip"), clans.Count));
            page.AddKpiRow(MainWidth,
                (LM.Get("gentry_kpi_count"), clans.Count.ToString(), WideKit.Gold),
                (LM.Get("gentry_kpi_first"), clans.Count(clan => GentrySystem.Grade(clan.gentry_pedigree) == 1).ToString(), WideKit.Orange),
                (LM.Get("gentry_kpi_officials"), $"{officialShare * 100f:0}%", WideKit.Grade(officialShare * 100f, 60, 30, higherIsBetter: false)),
                (LM.Get("gentry_kpi_land"), $"{stats.Sum(entry => entry.row.land):0.#}", WideKit.Cyan),
                (LM.Get("gentry_kpi_favor"), FavorLabel(GentrySystem.Favor(_empire)), FavorHex(GentrySystem.Favor(_empire))));

            var table = page.BeginCard(MainWidth, "gentry_table_title", WideKit.Gold, clans.Count.ToString(), "TabGentry");
            const float NameWidth = 120f;
            table.AddTableRow(MainWidth, -1, null, LM.Get("gentry_col_name"), NameWidth,
                (LM.Get("gentry_col_grade"), null), (LM.Get("gentry_col_members"), null), (LM.Get("gentry_col_office"), null),
                (LM.Get("gentry_col_titles"), null), (LM.Get("gentry_col_land"), null), (LM.Get("gentry_col_stance"), null));
            for (int i = 0; i < stats.Count && i < GentryRows; i++)
            {
                (SpecificClan clan, (int members, int office, int titles, float land) row) = stats[i];
                int grade = GentrySystem.Grade(clan.gentry_pedigree);
                string gradeHex = grade == 1 ? WideKit.Orange : grade == 2 ? WideKit.Gold : grade == 3 ? WideKit.Value : WideKit.Muted;
                var line = table.AddTableRow(MainWidth, i, null, GentrySystem.JunWang(clan), NameWidth,
                    ($"{GentrySystem.GradeName(clan)} {Mathf.RoundToInt(clan.gentry_pedigree)}", gradeHex),
                    (row.members.ToString(), null), (row.office.ToString(), row.office > 0 ? WideKit.Green : WideKit.Muted),
                    (row.titles.ToString(), null), ($"{row.land:0.#}", null), StanceCell(clan));
                SpecificClan target = clan;
                UIHelper.MakeClickable(line.gameObject, () => OpenClan(target));
            }
            if (stats.Count > GentryRows)
                table.AddTextIntoVertLayout(string.Format(LM.Get("gentry_table_more"), stats.Count - GentryRows).ColorString(WideKit.Muted),
                    true, TextAnchor.MiddleLeft, new Vector2(MainWidth - 12f, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);

            page.AddTextIntoVertLayout(LM.Get("gentry_rules_hint").ColorString(WideKit.Muted), true, TextAnchor.UpperLeft,
                new Vector2(MainWidth - 4f, 30f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);
            page.AddTextIntoVertLayout(LM.Get("gentry_rules_hint_more").ColorString(WideKit.Muted), true, TextAnchor.UpperLeft,
                new Vector2(MainWidth - 4f, 30f)).UseFixedFontSize(6, HorizontalWrapMode.Wrap);
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }

        // 名录一行：在世族人、在任官员(含城主)、爵位与头衔、田产(各城所占分数之和)
        private static (int members, int office, int titles, float land) Stats(SpecificClan clan)
        {
            List<PersonalClanIdentity> living = clan.LivingPeople;
            int office = 0, titles = 0;
            foreach (PersonalClanIdentity person in living)
            {
                Actor actor = person.is_virtual ? null : person._actor;
                if (actor?.data != null && !actor.isRekt())
                {
                    if (actor.IsOnOffice() || actor.isCityLeader() || actor.isKing()) office++;
                    titles += (actor.GetOwnedTitle()?.Count ?? 0) + (actor.HasHonoraryPeerage() ? 1 : 0);
                }
                else
                {
                    if (!string.IsNullOrEmpty(person.officeName)) office++;
                    titles += person.ownedTitleNames?.Count ?? 0;
                }
            }
            float land = LandEconomySystem.GetSpecificClanHoldings(clan).Sum(holding => holding.Share);
            return (living.Count, office, titles, land);
        }

        // 朝廷与世家：亲睦(40 以上)、猜忌(-40 以下)、平平
        private static string FavorLabel(float favor) => string.Format(LM.Get(favor >= GentrySystem.FavorHigh ? "gentry_favor_close"
            : favor <= GentrySystem.FavorLow ? "gentry_favor_tense" : "gentry_favor_neutral"), Mathf.RoundToInt(favor));

        private static string FavorHex(float favor) =>
            favor >= GentrySystem.FavorHigh ? WideKit.Green : favor <= GentrySystem.FavorLow ? WideKit.Orange : WideKit.Value;

        // 立场(亲附、中立、离心)，筑了坞堡的标出
        private (string, string) StanceCell(SpecificClan clan)
        {
            int stance = GentrySystem.Stance(clan, _empire);
            string text = LM.Get(stance > 0 ? "gentry_stance_loyal" : stance < 0 ? "gentry_stance_estranged" : "gentry_stance_neutral");
            City seat = GentrySystem.Seat(clan);
            if (seat != null && GentrySystem.FortressOf(seat) == clan) text += "·" + LM.Get("gentry_fortress_mark");
            return (text, stance > 0 ? WideKit.Green : stance < 0 ? WideKit.Orange : null);
        }

        private static void OpenClan(SpecificClan clan)
        {
            SpecificClanWindow.ShowClan(clan);
        }
    }
}
