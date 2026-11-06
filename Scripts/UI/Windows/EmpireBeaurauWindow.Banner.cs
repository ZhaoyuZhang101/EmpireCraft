using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.UI.Windows;

// 八旗多元制的帝国行政页(见 BannerSystem)：旗人人数、八旗战力、旗饷、满城、旗地、逃人法
public partial class EmpireBeaurauWindow
{
    private void BuildBannerPart()
    {
        BannerSystem.Summary summary = BannerSystem.SummaryOf(_empire);
        var card = _root.BeginCard(_contentWidth, LM.Get("banner_section"), WideKit.Gold,
            LM.Get(summary.multi ? "regime_type_Banner" : "banner_section_khanate"), "ui/icons/iconKingdom");
        pool.Add(card.gameObject);
        float share = summary.people > 0f ? summary.bannermen / summary.people : 0f;
        int row = 0;
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_people"),
            $"{UIHelper.ShortCount(summary.bannermen)}  {share:P0}", WideKit.Gold);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_vigor"), $"{summary.vigor:0}",
            WideKit.Grade(summary.vigor, 70f, 45f));
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_leagues"),
            summary.leagues == 0 ? LM.Get("label_none") : string.Format(LM.Get("banner_leagues_value"), summary.leagues, summary.leaguesMarried),
            summary.leagues == 0 ? WideKit.Muted : WideKit.Value);
        // 入关前只编旗：没有旗饷、圈地、逃人法、满城
        if (!summary.multi)
        {
            card.AddParagraph(_contentWidth, LM.Get("banner_section_desc_khanate"), 6, WideKit.Muted);
            return;
        }
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_idle"),
            string.Format(LM.Get(summary.hanjunReleased ? "banner_idle_released" : "banner_idle_value"), summary.idle),
            WideKit.Grade(summary.idle, 0.2f, 0.4f, higherIsBetter: false));
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_yongying"),
            summary.yongying.Count == 0 ? LM.Get("label_none") : string.Join(LM.Get("list_separator"), summary.yongying.Take(6)),
            summary.yongying.Count == 0 ? WideKit.Muted : WideKit.Orange);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_stipend"),
            summary.unpaidYears > 0 ? string.Format(LM.Get("banner_stipend_unpaid"), summary.stipend, summary.unpaidYears) : summary.stipend.ToString(),
            summary.unpaidYears > 0 ? WideKit.Red : WideKit.Value);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_entry"),
            summary.enteredYears < 0f ? LM.Get("banner_entry_none") : string.Format(LM.Get("banner_entry_years"), (int)summary.enteredYears),
            summary.enteredYears < 0f ? WideKit.Muted : WideKit.Value);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_seal"), LM.Get("banner_seal_" + summary.seal),
            summary.seal == 1 ? WideKit.Cyan : summary.seal == 2 ? WideKit.Orange : WideKit.Muted);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_queue"),
            LM.Get(summary.queued ? "banner_law_on" : "banner_law_off"), summary.queued ? WideKit.Orange : WideKit.Muted);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_enclosed"),
            summary.enclosureStopped ? string.Format(LM.Get("banner_enclosed_stopped"), summary.enclosed) : summary.enclosed.ToString(),
            summary.enclosureStopped ? WideKit.Muted : summary.enclosed > 0 ? WideKit.Orange : WideKit.Muted);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_law"),
            LM.Get(summary.fugitiveLaw ? "banner_law_on" : summary.lawRelaxed ? "banner_law_relaxed" : "banner_law_off"),
            summary.fugitiveLaw ? WideKit.Red : WideKit.Muted);
        card.AddValueRow(_contentWidth, row, LM.Get("banner_row_garrisons"),
            summary.garrisons.Count == 0 ? LM.Get("label_none") : string.Join(LM.Get("list_separator"), summary.garrisons.Take(8)),
            summary.garrisons.Count == 0 ? WideKit.Muted : WideKit.Cyan);
        card.AddParagraph(_contentWidth, LM.Get("banner_section_desc"), 6, WideKit.Muted);
    }
}
