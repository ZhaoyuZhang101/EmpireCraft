using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 律法页：本朝(核心国)现行律条、宽严与司法、全境近来的卷宗与在押之人，内容见 LawPanel(独立王国的律法窗口共用)
    public partial class EmpireWindow
    {
        // 直接打开某帝国的律法页(独立王国的律法窗口遇到帝国治下的国家时转到这里)
        public static void OpenLaw(Empire empire)
        {
            if (empire?.CoreKingdom == null || empire.isRekt()) return;
            SelectedMetas.selected_kingdom = empire.CoreKingdom;
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            _pendingTab = "empire_law";
            ScrollWindow.showWindow(nameof(EmpireWindow));
        }

        // 天平页(国务窗口对帝国中央转到这里，见 KingdomAffairsWindow.Open)
        public static void OpenScales(Empire empire)
        {
            if (empire?.CoreKingdom == null || empire.isRekt()) return;
            SelectedMetas.selected_kingdom = empire.CoreKingdom;
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            _pendingTab = "empire_scales";
            ScrollWindow.showWindow(nameof(EmpireWindow));
        }

        public void ShowLaw(WindowMetaTab pArg0)
        {
            Clear();
            Kingdom core = _empire?.CoreKingdom;
            if (core == null || core.isRekt()) return;
            AutoVertLayoutGroup page = BeginPage("empire_law", LawPanel.Chips(core));
            try
            {
                LawPanel.Render(page, core, MainWidth);
            }
            catch (global::System.Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国窗口律法页绘制失败({_empire?.data?.name}): {exception}");
            }
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }

        // 量刑天平页并进「天平」页签(见 EmpireWindow.ClaimScale.ShowScales)
    }
}
