using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 族谱树单独开一个宽窗口，不再是 SpecificClanWindow 内部的一个页签——宗族主窗口(卡片视图/
// 历史/搜索)保持原来的窄窗口布局不变，只有树状图这一种看法需要更宽的可视区域才铺得开。
// 树节点直接复用 SpecificClanWindow.ShowPersonalInfo 建出来的卡片(边框/头像/信息栏一个字
// 没改)，卡片建好之后立刻从 SpecificClanWindow 底下摘出来，重新挂到这个窗口自己的
// SpecificClanGraphView.ContentTransform 下面，两边窗口互不干扰。
public class SpecificClanTreeWindow : AbstractWideWindow<SpecificClanTreeWindow>
{
    private const float PanelWidth = 460f;
    private static PersonalClanIdentity _pendingIdentity;

    private AutoVertLayoutGroup _root;
    private SpecificClanGraphView _clanGraph;
    private PersonalClanIdentity _identity;

    public static void ShowFor(PersonalClanIdentity identity)
    {
        if (identity == null) return;
        _pendingIdentity = identity;
        ScrollWindow.showWindow(nameof(SpecificClanTreeWindow));
    }

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 2f, new RectOffset(3, 3, 3, 3));
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        if (_pendingIdentity != null) _identity = _pendingIdentity;
        Rebuild();
        StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        Clear();
    }

    private void Clear()
    {
        _root.transform.ClearChildren();
        _clanGraph = null;
    }

    private void Rebuild()
    {
        Clear();
        if (_identity == null) return;
        var treeSpace = _root.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.UpperCenter);
        var header = treeSpace.BeginHoriGroup(new Vector2(PanelWidth, 13), TextAnchor.MiddleCenter, 2);
        header.AddButtonIntoHoriLayout("window_back", LM.Get("window_back"),
            () => ScrollWindow.showWindow(nameof(SpecificClanWindow)), size: new Vector2(34, 11));
        _clanGraph = SpecificClanGraphView.Create(treeSpace.transform, new Vector2(PanelWidth, 320));
        _clanGraph.Rebuild(_identity, PlaceClanCard);
        _root.AddChild(treeSpace.gameObject);
    }

    private void ChangeActorInTree(PersonalClanIdentity actorIdentity)
    {
        if (actorIdentity == null) return;
        _identity = actorIdentity;
        Rebuild();
    }

    private void PlaceClanCard(PersonalClanIdentity identity, ClanRelation relation, Vector2 position)
    {
        bool isFocus = _identity != null && identity.id == _identity.id;
        if (isFocus)
        {
            // 高亮框是额外叠加的一层，不动 ShowPersonalInfo 那张卡片本身——卡片布局原样保留，
            // 只是在它正下方多铺一块更大、更亮的底板，把"当前是谁"标出来。
            var highlightObject = new GameObject("FocusHighlight", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            RectTransform highlightRect = highlightObject.GetComponent<RectTransform>();
            highlightRect.SetParent(_clanGraph.ContentTransform, false);
            highlightRect.anchorMin = highlightRect.anchorMax = new Vector2(0.5f, 0.5f);
            highlightRect.pivot = new Vector2(0.5f, 0.5f);
            highlightRect.sizeDelta = new Vector2(SpecificClanGraphView.CardWidth + 10f,
                SpecificClanGraphView.CardHeight + 10f);
            highlightRect.anchoredPosition = position;
            Image highlightImage = highlightObject.GetComponent<Image>();
            highlightImage.sprite = SpriteTextureLoader.getSprite("ui/FactionFrame_dominate");
            highlightImage.type = Image.Type.Sliced;
            highlightImage.color = new Color(1f, 0.82f, 0.25f, 1f);
            highlightImage.raycastTarget = false;
        }

        AutoHoriLayoutGroup card = SpecificClanWindow.Instance.ShowPersonalInfo(null, identity, relation,
            ChangeActorInTree);
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.SetParent(_clanGraph.ContentTransform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(SpecificClanGraphView.CardWidth, SpecificClanGraphView.CardHeight);
        rect.anchoredPosition = position;
    }
}
