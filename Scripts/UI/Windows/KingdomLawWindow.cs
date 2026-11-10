using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Windows;

// 一国的律法(「卷册」宽窗口)：不在帝国里的王国也按本国政体的律法执法，从国情窗口左栏"去处"打开。
//   · 左栏：国名、国家类别 → 君主、政体、宽严、司法、在押 → 去处(国情、政体与制度)；
//   · 右栏：与帝国窗口律法页同一套内容(见 LawPanel)。
// 帝国治下的国家按帝国论处，转到帝国窗口的律法页
public class KingdomLawWindow : WideScrollWindow<KingdomLawWindow>
{
    private static long _pending = -1L;
    private Kingdom _kingdom;
    // 右栏看律法还是量刑天平(用户 2026-11-10 选"律法页加天平分页")
    private bool _scalePage;

    public static void Open(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return;
        Empire empire = EmpireLawSystem.RealmEmpire(kingdom);
        if (empire != null)
        {
            EmpireWindow.OpenLaw(empire);
            return;
        }
        _pending = kingdom.id;
        ScrollWindow.showWindow(nameof(KingdomLawWindow));
    }

    protected override void OnOpen()
    {
        _kingdom = World.world.kingdoms.get(_pending) ?? SelectedMetas.selected_kingdom;
    }

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    private void ShowPage(bool scale)
    {
        if (_scalePage == scale) return;
        _scalePage = scale;
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
            stats.AddRailRow(width, LM.Get("village_statistics_king"), kingdom.king.getName(), WideKit.Gold, row++);
        var regime = kingdom.GetRegime();
        if (regime != null)
            stats.AddRailRow(width, LM.Get("kov_regime"), BannerSystem.RegimeName(kingdom, LM.Get("regime_type_" + regime.type)),
                WideKit.Value, row++);
        LawStanceInfo stance = EmpireLawSystem.GetLawStance(kingdom, true);
        stats.AddRailRow(width, LM.Get("law_kpi_stance"), EmpireLawSystem.GetStanceName(stance.Tier),
            stance.Tier == LawStanceTier.严苛 ? WideKit.Red : stance.Tier == LawStanceTier.宽仁 ? WideKit.Green : WideKit.Value, row++);
        JudicialStrengthInfo judicial = EmpireLawSystem.GetJudicialStrength(kingdom, true);
        stats.AddRailRow(width, LM.Get("law_kpi_judicial"), $"×{judicial.Factor:0.00}", WideKit.Value, row++);
        int prisoners = LawPanel.RealmPrisoners(kingdom).Count;
        stats.AddRailRow(width, LM.Get("law_kpi_prisoners"), string.Format(LM.Get("law_people_count"), prisoners),
            prisoners > 0 ? WideKit.Orange : WideKit.Muted, row);

        var pages = rail.BeginRailMenu(LM.Get("law_pages"));
        pages.AddRailOption(SpriteTextureLoader.getSprite("ui/icons/iconWorldLaws"), LM.Get("empire_law"), null, !_scalePage,
            () => ShowPage(false), 0);
        pages.AddRailOption(SpriteTextureLoader.getSprite("TabSentencing"), LM.Get("empire_law_scale"), null, _scalePage,
            () => ShowPage(true), 1);

        var links = rail.BeginRailMenu(LM.Get("kov_links"));
        links.AddRailLink(SpriteTextureLoader.getSprite("ui/icons/iconKingdom"), LM.Get("KingdomOverviewWindow Title"),
            () => KingdomOverviewWindow.Open(kingdom));
        links.AddRailLink(SpriteTextureLoader.getSprite("ui/icons/iconBooks"), LM.Get("kov_open_regime"), () =>
        {
            SelectedMetas.selected_kingdom = kingdom;
            ScrollWindow.showWindow(nameof(RegimeWindow));
        });
    }

    // ───────── 右栏 ─────────
    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (!Alive(_kingdom))
        {
            page.AddEmptyCard(PageWidth, "kov_gone");
            return;
        }
        page.AddPageHeader(PageWidth, _scalePage ? "empire_law_scale" : "empire_law", LawPanel.Chips(_kingdom));
        try
        {
            if (_scalePage) LawPanel.RenderScaleBoard(page, _kingdom, PageWidth);
            else LawPanel.Render(page, _kingdom, PageWidth);
        }
        catch (global::System.Exception exception)
        {
            NeoModLoader.services.LogService.LogError($"律法窗口绘制失败({_kingdom?.data?.name}): {exception}");
        }
    }
}
