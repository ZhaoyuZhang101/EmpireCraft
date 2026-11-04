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
        var card = _root.BeginCard(_contentWidth, LM.Get("banner_section"), WideKit.Gold, LM.Get("regime_type_Banner"), "ui/icons/iconKingdom");
        pool.Add(card.gameObject);
        float share = summary.people > 0f ? summary.bannermen / summary.people : 0f;
        int row = 0;
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_people"),
            $"{UIHelper.ShortCount(summary.bannermen)}  {share:P0}", WideKit.Gold);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_vigor"), $"{summary.vigor:0}",
            WideKit.Grade(summary.vigor, 70f, 45f));
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_stipend"),
            summary.unpaidYears > 0 ? string.Format(LM.Get("banner_stipend_unpaid"), summary.stipend, summary.unpaidYears) : summary.stipend.ToString(),
            summary.unpaidYears > 0 ? WideKit.Red : WideKit.Value);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_enclosed"), summary.enclosed.ToString(),
            summary.enclosed > 0 ? WideKit.Orange : WideKit.Muted);
        card.AddValueRow(_contentWidth, row++, LM.Get("banner_row_law"),
            LM.Get(summary.fugitiveLaw ? "banner_law_on" : "banner_law_off"), summary.fugitiveLaw ? WideKit.Red : WideKit.Muted);
        card.AddValueRow(_contentWidth, row, LM.Get("banner_row_garrisons"),
            summary.garrisons.Count == 0 ? LM.Get("label_none") : string.Join(LM.Get("list_separator"), summary.garrisons.Take(8)),
            summary.garrisons.Count == 0 ? WideKit.Muted : WideKit.Cyan);
        card.AddParagraph(_contentWidth, LM.Get("banner_section_desc"), 6, WideKit.Muted);
    }
}
