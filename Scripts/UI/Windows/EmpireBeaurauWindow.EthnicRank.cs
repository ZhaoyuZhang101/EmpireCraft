using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.UI.Windows;

// 族群等第的帝国行政页(见 EthnicRankSystem)：方案名、各等人数与占比
public partial class EmpireBeaurauWindow
{
    private void BuildEthnicRankPart()
    {
        EthnicRankSystem.Summary summary = EthnicRankSystem.SummaryOf(_empire);
        if (summary.scheme == EthnicRankRules.Scheme.None) return;
        var card = _root.BeginCard(_contentWidth, LM.Get("ethnic_rank_section"), WideKit.Gold,
            EthnicRankSystem.SchemeName(summary.scheme), "ui/icons/iconKingdom");
        pool.Add(card.gameObject);
        int row = 0;
        int ranks = summary.scheme == EthnicRankRules.Scheme.FourClasses ? EthnicRankRules.Count : EthnicRankRules.Late;
        for (int rank = 0; rank < ranks; rank++)
        {
            float share = summary.total > 0f ? summary.people[rank] / summary.total : 0f;
            card.AddValueRow(_contentWidth, row++, EthnicRankSystem.RankName(summary.scheme, rank, _empire),
                $"{UIHelper.ShortCount(summary.people[rank])}  {share:P0}",
                rank == EthnicRankRules.Ruling ? WideKit.Gold : rank == EthnicRankRules.Late ? WideKit.Red : WideKit.Value);
        }
        card.AddParagraph(_contentWidth, LM.Get($"ethnic_rank_scheme_{summary.scheme}_desc"), 6, WideKit.Muted);
    }
}
