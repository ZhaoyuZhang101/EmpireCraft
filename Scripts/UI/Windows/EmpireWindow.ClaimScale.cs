using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 诉求天平页(用户 2026-11-10 "推动诉求也要加天平页面""这样才能知道成功还是没成功")，内容见 ClaimScalePanel
    public partial class EmpireWindow
    {
        public void ShowClaimScale(WindowMetaTab pArg0)
        {
            Clear();
            if (_empire?.data == null || _empire.isRekt()) return;
            AutoVertLayoutGroup page = BeginPage("empire_claim_scale");
            try
            {
                ClaimScalePanel.Render(page, _empire, MainWidth);
            }
            catch (global::System.Exception exception)
            {
                NeoModLoader.services.LogService.LogError($"帝国窗口诉求天平页绘制失败({_empire?.data?.name}): {exception}");
            }
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }
    }
}
