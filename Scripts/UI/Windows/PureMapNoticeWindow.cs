using System.Collections.Generic;
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
public class PureMapNoticeWindow : AutoLayoutWindow<PureMapNoticeWindow>
{
    private const string HiddenKey = "EmpireCraft_PureMapNoticeHidden";
    private const float DelaySeconds = 2f;
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
        AutoVertLayoutGroup panel = this.BeginVertGroup(new Vector2(196, 200), pSpacing: 4,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 3, 3));
        _groups.Add(panel.gameObject);
        SimpleText text = panel.AddTextIntoVertLayout(LM.Get("pure_map_notice_text"), true, TextAnchor.UpperLeft,
            new Vector2(188, 150));
        text.UseFixedFontSize(8, HorizontalWrapMode.Wrap);
        AutoHoriLayoutGroup buttons = panel.BeginHoriGroup(new Vector2(188, 22), TextAnchor.MiddleCenter, 4);
        buttons.AddButtonIntoHoriLayout("pure_map_notice_ok", LM.Get("pure_map_notice_ok"), Close, size: new Vector2(88, 20));
        buttons.AddButtonIntoHoriLayout("pure_map_notice_never", LM.Get("pure_map_notice_never"), () =>
        {
            PlayerPrefs.SetInt(HiddenKey, 1);
            PlayerPrefs.Save();
            Close();
        }, size: new Vector2(88, 20));
    }

    private static void Close() => ScrollWindow.hideAllEvent();
}
