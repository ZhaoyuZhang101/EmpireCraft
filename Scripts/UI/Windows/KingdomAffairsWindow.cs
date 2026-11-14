using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Windows;

// 一国的国务(「卷册」宽窗口，用户 2026-11-15 "王国窗口也加上这些")：帝国窗口天平页、财报页里的那几样，王国也有——
//   · 左栏：国名、国家类别 → 君主、内帑、国库 → 分页(决议、开源节流与内帑) → 去处(国情)；
//   · 右栏：决议页 = 决议天平(本国在办、最近一回议决) + 可议之事(发起)；财用页 = 开源节流(王国只能裁军) + 内帑。
// 帝国中央的转到帝国窗口(朝局、调税那边齐全)
public class KingdomAffairsWindow : WideScrollWindow<KingdomAffairsWindow>
{
    private static long _pending = -1L;
    private Kingdom _kingdom;
    private bool _purse;

    public static void Open(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.isRekt() && empire.CoreKingdom == kingdom)
        {
            SelectedMetas.selected_kingdom = kingdom;
            EmpireWindow.OpenScales(empire);
            return;
        }
        _pending = kingdom.id;
        ScrollWindow.showWindow(nameof(KingdomAffairsWindow));
    }

    protected override void OnOpen()
    {
        _kingdom = World.world.kingdoms.get(_pending) ?? SelectedMetas.selected_kingdom;
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    private void ShowPage(bool purse)
    {
        if (_purse == purse) return;
        _purse = purse;
        Refresh(resetScroll: true);
    }

    // ───────── 左栏 ─────────
    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (!Alive(_kingdom)) return;
        Kingdom kingdom = _kingdom;
        var identity = rail.BeginRailCard("FactionFrame_dominate");
        identity.AddRailText(kingdom.GetKingdomFullName().ColorString(kingdom.getColor().color_text), 10, 26f, wrap: true);
        identity.AddRailText(LM.Get(kingdom.GetKingdomTypeDisplayKey()).ColorString(WideKit.Cyan), 7, 10f);

        var stats = rail.BeginRailCard();
        float width = UIHelper.RailWidth - 8f;
        int row = 0;
        if (kingdom.hasKing())
        {
            stats.AddRailRow(width, LM.Get("village_statistics_king"), kingdom.king.getName(), WideKit.Gold, row++);
            stats.AddRailRow(width, LM.Get("purse_money"), MoneyDisplay.Format(kingdom.king.money), WideKit.Value, row++);
        }
        stats.AddRailRow(width, LM.Get("label_national_treasury"), MoneyDisplay.Format(kingdom.GetTreasuryBalance()), WideKit.Value, row);

        var pages = rail.BeginRailMenu(LM.Get("affairs_pages"));
        pages.AddRailOption(SpriteTextureLoader.getSprite("ui/icons/iconBooks"), LM.Get("empire_decision_scale"), null, !_purse,
            () => ShowPage(false), 0);
        pages.AddRailOption(SpriteTextureLoader.getSprite("ui/icons/iconMoney"), LM.Get("affairs_purse_page"), null, _purse,
            () => ShowPage(true), 1);

        var links = rail.BeginRailMenu(LM.Get("kov_links"));
        links.AddRailLink(SpriteTextureLoader.getSprite("ui/icons/iconKingdom"), LM.Get("KingdomOverviewWindow Title"),
            () => KingdomOverviewWindow.Open(kingdom));
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (!Alive(_kingdom))
        {
            page.AddEmptyCard(PageWidth, "kov_gone");
            return;
        }
        page.AddPageHeader(PageWidth, _purse ? "affairs_purse_page" : "empire_decision_scale", _kingdom.GetKingdomName());
        try
        {
            if (_purse)
            {
                CourtActionsPanel.RenderLevers(page, _kingdom, PageWidth, () => Refresh(resetScroll: false));
                CourtActionsPanel.RenderPurse(page, _kingdom, PageWidth, () => Refresh(resetScroll: false));
            }
            else
            {
                DecisionScalePanel.Render(page, _kingdom, PageWidth);
                CourtActionsPanel.RenderOptions(page, _kingdom, PageWidth, () => Refresh(resetScroll: false));
            }
        }
        catch (global::System.Exception exception)
        {
            NeoModLoader.services.LogService.LogError($"国务窗口绘制失败({_kingdom?.data?.name}): {exception}");
        }
    }
}
