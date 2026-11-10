using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 「天平」页签：量刑天平与诉求天平合在一页，顶上切换(用户 2026-11-10 "左侧按钮已经要超出了怎么办"，问答选"两个天平合成一页")。
    // 量刑见 LawPanel.RenderScaleBoard，诉求见 ClaimScalePanel(用户 2026-11-10 "推动诉求也要加天平页面""这样才能知道成功还是没成功")
    public partial class EmpireWindow
    {
        // 0 量刑、1 诉求；换帝国不重置(两边都看的人多半来回切)
        private static int _scalesPage;

        public void ShowScales(WindowMetaTab pArg0)
        {
            Clear();
            if (_empire?.data == null || _empire.isRekt()) return;
            var core = _empire.CoreKingdom;
            AutoVertLayoutGroup page = BeginPage("empire_scales", _scalesPage == 0 && core != null ? LawPanel.Chips(core) : new string[0]);
            page.AddChipRow(MainWidth,
                (LM.Get("empire_law_scale"), _scalesPage == 0, () => SwitchScales(0)),
                (LM.Get("empire_claim_scale"), _scalesPage == 1, () => SwitchScales(1)));
            try
            {
                if (_scalesPage == 0)
                {
                    if (core != null && !core.isRekt()) LawPanel.RenderScaleBoard(page, core, MainWidth);
                }
                else ClaimScalePanel.Render(page, _empire, MainWidth, () => ShowScales(null));
            }
            catch (global::System.Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国窗口天平页绘制失败({_empire?.data?.name}): {exception}");
            }
            StartCoroutine(RefreshScrollLayout(resetToTop: false));
        }

        private void SwitchScales(int page)
        {
            _scalesPage = page;
            ShowScales(null);
        }
    }
}
