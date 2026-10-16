using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 纯净地图模式的开局提示(用户 2026-10-16："老有人问为什么建筑和树木小人消失了""给两个选项一个是再也不提示一个是知道了")：
// 开着纯净地图时，每次启动游戏、读进第一个世界后弹一次，说明地图上不画建筑、植物、小人是这个模式，以及怎么关；
// "知道了"只关这一次，"不再提示"记在本机(PlayerPrefs)，以后都不弹。
// 版式用帝国窗口的像素框卡片(用户："美化用我的帝国frame")：金框标题卡 + 三张说明卡(看不到什么 / 照常运作 / 怎么恢复) + 按钮行
public class PureMapNoticeWindow : AutoLayoutWindow<PureMapNoticeWindow>
{
    private const string HiddenKey = "EmpireCraft_PureMapNoticeHidden";
    private const float DelaySeconds = 2f;
    private const float Width = 196f;
    private static bool _shownThisLaunch;
    private static float _readyAt = -1f;
    private readonly List<GameObject> _groups = new();

    // 每帧由 PureMapMode.Tick 调用(只在纯净地图开着、世界读完时)
    public static void TryShow()
    {
        if (_shownThisLaunch) return;
        if (PlayerPrefs.GetInt(HiddenKey, 0) == 1)
        {
            _shownThisLaunch = true;
            return;
        }
        if (_readyAt < 0f) _readyAt = Time.unscaledTime + DelaySeconds;
        if (Time.unscaledTime < _readyAt) return;
        _shownThisLaunch = true;
        ScrollWindow.showWindow(nameof(PureMapNoticeWindow));
    }

    protected override void Init()
    {
        layout.spacing = 4;
        layout.padding = new RectOffset(4, 4, 6, 4);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        foreach (GameObject group in _groups)
            if (group != null) Destroy(group);
        _groups.Clear();
        AutoVertLayoutGroup panel = this.BeginVertGroup(pSpacing: UIHelper.CardGap, pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(0, 0, 0, 0));
        UIHelper.FixPanelWidth(panel.transform, Width);
        _groups.Add(panel.gameObject);

        // 金框标题卡
        var head = panel.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(6, 6, 5, 5));
        UIHelper.FixPanelWidth(head.transform, Width);
        UIHelper.AddCardFrame(head.transform, "FactionFrame_dominate");
        Line(head, LM.Get("pure_map_notice_title").ColorString(WideKit.Gold), 11, 15f, TextAnchor.MiddleCenter);
        Line(head, LM.Get("pure_map_notice_subtitle").ColorString(WideKit.Muted), 7, 10f, TextAnchor.MiddleCenter);

        Section(panel, "pure_map_notice_hidden_title", WideKit.Cyan, "pure_map_notice_hidden_text", "ui/icons/iconCity");
        Section(panel, "pure_map_notice_running_title", WideKit.Green, "pure_map_notice_running_text", "ui/icons/iconKingdom");
        Section(panel, "pure_map_notice_restore_title", WideKit.Orange, "pure_map_notice_restore_text", "ui/icons/iconBook");

        var actions = panel.BeginCard(Width);
        actions.AddButtonRow(Width,
            ("pure_map_notice_ok", LM.Get("pure_map_notice_ok"), Close),
            ("pure_map_notice_never", LM.Get("pure_map_notice_never"), () =>
            {
                PlayerPrefs.SetInt(HiddenKey, 1);
                PlayerPrefs.Save();
                Close();
            }));
    }

    // 一张说明卡：带图标的彩色标题 + 自动换行的正文
    private static void Section(AutoVertLayoutGroup panel, string titleKey, string hex, string textKey, string icon)
    {
        var card = panel.BeginCard(Width, titleKey, hex, null, icon);
        string text = LM.Get(textKey);
        float inner = Width - 12f;
        int lines = 0;
        foreach (string paragraph in text.Split('\n'))
            lines += Mathf.Max(1, Mathf.CeilToInt(UIHelper.EstimateTextWidth(paragraph, 7) / (inner - 2f)));
        var body = card.AddTextIntoVertLayout(text.ColorString(WideKit.Value), true, TextAnchor.UpperLeft,
            new Vector2(inner, lines * 10f + 2f));
        body.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
    }

    private static void Line(AutoVertLayoutGroup card, string text, int fontSize, float height, TextAnchor anchor)
    {
        var label = card.AddTextIntoVertLayout(text, true, anchor, new Vector2(Width - 12f, height));
        label.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
    }

    private static void Close() => ScrollWindow.hideAllEvent();
}
