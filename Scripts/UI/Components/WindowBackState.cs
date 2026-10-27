using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Components;

// 窗口自己记、自己还原返回前的状态(WideScrollWindow、EmpireWindow)
public interface IBackRestorable
{
    object CaptureBackState();
    void PrepareBackRestore(object state);
}

// 返回时停在原来那页(用户 2026-10-27："很多窗口我希望回退后还在之前的界面，而不是直接跳回默认的首页")。
// 原版的返回(WindowHistory.clickBack)是把上一个窗口重新打开，模组的窗口一打开就重置到首页、顶部，
// 原版也只还原原版的选中对象(国家、城、人……)，不管模组的帝国、帝国法理、王国法理。这里按窗口在历史里的位置记：
//   · 窗口进历史时(addIntoHistory)记下模组的选中对象(此时就是这个窗口在看的)；
//   · 打开下一个窗口前(ScrollWindow.show)记下它此刻的状态：IBackRestorable 交出的状态，没有就记当前页签；
//   · 返回时(clickBack → clickShowLeft)开窗前还原选中对象、把状态交给窗口；只认了页签的，开窗后下一帧切回那个页签。
// 只管模组的窗口(原版窗口照旧)；关掉全部窗口(WindowHistory.clear)时清空。
public static class WindowBackState
{
    public static bool Returning;

    private sealed class Snapshot
    {
        public string screen_id;
        public Empire empire;
        public EmpireCore core;
        public KingdomTitle title;
        public object state;
        public string tab;
    }

    private static readonly Dictionary<int, Snapshot> Snapshots = new();
    private static ScrollWindow _tabWindow;
    private static string _tab;

    private static bool Ours(ScrollWindow window) =>
        window != null && window.GetComponents<MonoBehaviour>().Any(component => component != null &&
            component.GetType().Namespace?.StartsWith("EmpireCraft", StringComparison.Ordinal) == true);

    private static WindowHistoryData? Last() =>
        WindowHistory.list.Count == 0 ? null : WindowHistory.list[WindowHistory.list.Count - 1];

    // 进历史之后：记下此刻模组的选中对象
    public static void OnAdded()
    {
        if (Last() is not WindowHistoryData entry || !Ours(entry.window)) return;
        Snapshots[entry.index] = new Snapshot
        {
            screen_id = entry.window.screen_id,
            empire = EmpireCraftMetaTypeLibrary.selected_empire,
            core = EmpireCraftMetaTypeLibrary.selected_empireCore,
            title = EmpireCraftMetaTypeLibrary.selected_kingdomTitle
        };
    }

    // 打开下一个窗口前：记下历史里上一个窗口此刻的状态(返回途中不记)
    public static void OnLeaving()
    {
        if (Returning || Last() is not WindowHistoryData entry || entry.window == null) return;
        if (!Snapshots.TryGetValue(entry.index, out Snapshot snapshot) || snapshot.screen_id != entry.window.screen_id) return;
        IBackRestorable restorable = entry.window.GetComponent<IBackRestorable>();
        snapshot.state = restorable?.CaptureBackState();
        snapshot.tab = restorable == null ? SimpleWindowTab.CurrentOf(entry.window) : null;
    }

    // 返回途中、开窗之前：还原选中对象，把状态交给窗口(原版已把这个窗口那一条弹出历史，它原来的位置是 Count + 1)
    public static void BeforeReturn(ScrollWindow window)
    {
        _tabWindow = null;
        _tab = null;
        if (!Returning || window == null) return;
        if (!Snapshots.TryGetValue(WindowHistory.list.Count + 1, out Snapshot snapshot) || snapshot.screen_id != window.screen_id) return;
        if (snapshot.empire != null && !snapshot.empire.isRekt()) EmpireCraftMetaTypeLibrary.selected_empire = snapshot.empire;
        if (snapshot.core != null) EmpireCraftMetaTypeLibrary.selected_empireCore = snapshot.core;
        if (snapshot.title != null) EmpireCraftMetaTypeLibrary.selected_kingdomTitle = snapshot.title;
        IBackRestorable restorable = window.GetComponent<IBackRestorable>();
        if (restorable != null && snapshot.state != null) restorable.PrepareBackRestore(snapshot.state);
        else if (!string.IsNullOrEmpty(snapshot.tab))
        {
            _tabWindow = window;
            _tab = snapshot.tab;
        }
    }

    // 开窗之后：只认了页签的窗口，等它自己的首页画完再切回那个页签
    public static void AfterReturn(ScrollWindow window)
    {
        if (_tabWindow == null || window != _tabWindow || !window.gameObject.activeInHierarchy) return;
        string tab = _tab;
        _tabWindow = null;
        _tab = null;
        window.StartCoroutine(SwitchTab(window, tab));
    }

    private static IEnumerator SwitchTab(ScrollWindow window, string name)
    {
        yield return CoroutineHelper.wait_for_next_frame;
        WindowMetaTab tab = window?.tabs?._tabs?.FirstOrDefault(item => item != null && item.name == name);
        tab?.doAction();
    }

    public static void Clear()
    {
        Snapshots.Clear();
        _tabWindow = null;
        _tab = null;
    }
}
