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

        // 量刑天平页(用户 2026-11-10 选"律法页加天平分页")：本朝要人若论寻常罪名的天平读数
        public void ShowLawScale(WindowMetaTab pArg0)
        {
            Clear();
            Kingdom core = _empire?.CoreKingdom;
            if (core == null || core.isRekt()) return;
            AutoVertLayoutGroup page = BeginPage("empire_law_scale", LawPanel.Chips(core));
            try
            {
                LawPanel.RenderScaleBoard(page, core, MainWidth);
            }
            catch (global::System.Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国窗口量刑天平页绘制失败({_empire?.data?.name}): {exception}");
            }
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }
    }
}
