using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NeoModLoader.api;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 「卷册」宽窗口基类(480×380)：左栏固定不随滚动，右栏滚动。子类只管画：
//   public class MyWindow : WideScrollWindow<MyWindow>
//   {
//       protected override void DrawRail(AutoVertLayoutGroup rail) { rail.AddRailTitle("标题", "副标题"); ... }
//       protected override void DrawPage(AutoVertLayoutGroup page) { page.AddPageHeader(PageWidth, "页名"); ... }
//   }
//   注册：MyWindow.CreateAndInit(nameof(MyWindow), new Vector2(480f, 380f))；标题用 "MyWindow Title" 本地化键。
//   状态变了(筛选、翻页、选中)调 Refresh()：先 Prepare() 取数据，再清空左右两栏重画；打开窗口时自动画一次并回到顶部。
// 部件见 WideKit(左栏选项、主指标排、卡片网格、国色条、人物行、指标行、列表行、空状态)与 UIHelper(卡片、页头、分节标题)。
//   返回(原版返回键)时停在原来那页：离开时记下子类的全部字段与滚动位置，返回时还原、不走 OnOpen(见 WindowBackState)。
public abstract class WideScrollWindow<T> : AbstractWideWindow<T>, IBackRestorable where T : WideScrollWindow<T>
{
    // 返回时还原用：子类各层声明的实例字段(不含只读字段——那些是每次重画都会重算的缓存——与界面物件)、滚动位置
    private sealed class BackState
    {
        public Dictionary<FieldInfo, object> fields;
        public Vector3 scroll;
    }

    private BackState _backRestore;
    private static List<FieldInfo> _stateFields;

    private static List<FieldInfo> StateFields()
    {
        if (_stateFields != null) return _stateFields;
        var fields = new List<FieldInfo>();
        for (Type type = typeof(T); type != null && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WideScrollWindow<>));
             type = type.BaseType)
            fields.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !field.IsInitOnly && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)));
        return _stateFields = fields;
    }

    public object CaptureBackState()
    {
        ScrollWindow window = GetComponent<ScrollWindow>();
        return new BackState
        {
            fields = StateFields().ToDictionary(field => field, field => field.GetValue(this)),
            scroll = window?.transform_content != null ? window.transform_content.localPosition : Vector3.zero
        };
    }

    public void PrepareBackRestore(object state) => _backRestore = state as BackState;

    protected const float PageWidth = UIHelper.PageMainWidth;

    protected AutoVertLayoutGroup Rail { get; private set; }
    private AutoVertLayoutGroup _root;
    private readonly List<GameObject> _blocks = new();
    private int _generation;

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
        Rail = UIHelper.CreateWideWindowRail(BackgroundTransform, UIHelper.RailWidth, UIHelper.RailInset);
        OnInit();
    }

    // 子类额外的初始化(输入框等)
    protected virtual void OnInit() { }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
        BackState restore = _backRestore;
        _backRestore = null;
        if (restore == null)
        {
            OnOpen();
            Refresh(resetScroll: true);
            return;
        }
        foreach (KeyValuePair<FieldInfo, object> pair in restore.fields) pair.Key.SetValue(this, pair.Value);
        ScrollWindow window = GetComponent<ScrollWindow>();
        if (window?.transform_content != null) window.transform_content.localPosition = restore.scroll;
        Refresh();
    }

    // 打开窗口时、画之前(读选中对象、重置筛选等)
    protected virtual void OnOpen() { }

    // 每次重画前(左右两栏共用的数据在这里取一遍)
    protected virtual void Prepare() { }

    protected abstract void DrawRail(AutoVertLayoutGroup rail);
    protected abstract void DrawPage(AutoVertLayoutGroup page);

    // 清空重画；resetScroll = 回到顶部，否则保持当前滚动位置
    public void Refresh(bool resetScroll = false)
    {
        ScrollWindow window = GetComponent<ScrollWindow>();
        Vector3? keep = resetScroll || window?.transform_content == null
            ? null : window.transform_content.localPosition;
        foreach (GameObject block in _blocks)
        {
            if (block == null) continue;
            block.SetActive(false);
            Destroy(block);
        }
        _blocks.Clear();
        UIHelper.ClearRail(Rail);
        Prepare();
        if (Rail != null) DrawRail(Rail);
        var page = _root.BeginVertGroup(pSpacing: UIHelper.CardGap, pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(0, 0, 0, 6));
        UIHelper.FixPanelWidth(page.transform, PageWidth);
        _blocks.Add(page.gameObject);
        DrawPage(page);
        if (gameObject.activeInHierarchy) StartCoroutine(Settle(++_generation, keep));
    }

    // 重画后的下一帧强制排版几遍，再定滚动位置(否则自动高的卡片要晚一帧才撑开)
    private IEnumerator Settle(int generation, Vector3? keep)
    {
        yield return CoroutineHelper.wait_for_next_frame;
        if (generation != _generation || !gameObject.activeInHierarchy) yield break;
        ScrollWindow window = GetComponent<ScrollWindow>();
        if (window?.transform_content == null) yield break;
        for (int pass = 0; pass < 3; pass++) LayoutRebuilder.ForceRebuildLayoutImmediate(window.transform_content);
        if (keep.HasValue) window.transform_content.localPosition = keep.Value;
        else if (window.scrollRect != null) window.scrollRect.verticalNormalizedPosition = 1f;
        window.scrollRect?.StopMovement();
    }
}
