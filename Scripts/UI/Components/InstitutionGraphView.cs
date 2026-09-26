using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 制度科技树的图形视图。
//
// 布局规则：
//   · 根节点居中置顶，单独一行；
//   · 其余节点按 branch 分到固定的纵向车道里——一个分支一条列，同分支的链条就是一条直线；
//   · 车道顶部标分支名，所以"哪条分支走到哪一级"一眼能看出来；
//   · 跨分支的前置（比如行政的分封建国 → 军事的禁私战）用折线走：从父节点底部垂直下来、
//     在两行之间横过去、再垂直进子节点顶部。折线是科技树的通用画法，斜线才会让人觉得乱。
//
// 视口/拖拽/缩放/连线这些跟"这是一棵制度树"完全无关的活，都丢给通用的 GraphView
// (见 GraphView.cs)；车道+等级怎么换算成坐标用通用的 GraphLayout.TieredLanes；这个类
// 自己只留"制度节点该长什么样、状态怎么上色、外来制度那一列、顶部车道等级摘要"这些
// 制度树专属的东西。这个类本身不再是 MonoBehaviour——它只是 GraphView 的一层薄壳。
public class InstitutionGraphView
{
    // 节点做成跟族谱树一样的小卡片：名称 + 状态 + 一行关键信息(+改革进度条)。
    private const float NodeWidth = 76f;
    private const float NodeHeight = 36f;
    private const float LaneGap = 8f;
    private const float RowGap = 18f;
    private const float MinFitScale = 0.7f;
    private const float ForeignLaneGap = 22f;
    private const float FixedHeaderHeight = 13f;
    private static readonly Vector2 NodeHalfSize = new(NodeWidth / 2f, NodeHeight / 2f);

    // 分支车道的固定顺序。配置里出现的其他分支会按名字排在后面，不会丢。
    // 分支顺序来自配置（InstitutionDefinitionRegistry 汇总全部节点的分支），新分支不需要改代码
    private static string[] BranchOrder => InstitutionDefinitionRegistry.Branches.ToArray();

    private GraphView _engine;
    private Text _fixedHeaderText;
    private Action<string> _onSelect;
    private Func<InstitutionNodeView, (string title, string body)> _buildTooltip;
    private Func<InstitutionNodeView, NodeCardInfo> _buildCard;
    private string _selectedId = "";
    private readonly Dictionary<string, Image> _nodeFrames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Sprite> SlicedFrames = new(StringComparer.Ordinal);

    // 卡片上状态行/信息行的文字(可带富文本颜色)和改革进度(0~1，<0 表示不画进度条)，由窗口提供。
    public readonly struct NodeCardInfo
    {
        public readonly string StatusLine;
        public readonly string InfoLine;
        public readonly float Progress;

        public NodeCardInfo(string statusLine, string infoLine, float progress = -1f)
        {
            StatusLine = statusLine;
            InfoLine = infoLine;
            Progress = progress;
        }
    }

    public static InstitutionGraphView Create(Transform parent, Vector2 size)
    {
        var view = new InstitutionGraphView();
        view._engine = GraphView.Create(parent, size, GraphOrientation.Vertical,
            objectName: "InstitutionGraphViewport");
        view._engine.SetClampMargin(NodeHalfSize);

        // 四条分支的等级摘要固定贴在视口顶部：是 viewport 的直接子物体（不是内容层的
        // 子物体），所以拖拽/缩放这棵树的时候它不跟着动，字号也比车道标题大一号，一直看得清。
        var headerBarObject = new GameObject("FixedHeaderBar", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image));
        var headerBarRect = headerBarObject.GetComponent<RectTransform>();
        headerBarRect.SetParent(view._engine.ViewportTransform, false);
        headerBarRect.anchorMin = new Vector2(0f, 1f);
        headerBarRect.anchorMax = new Vector2(1f, 1f);
        headerBarRect.pivot = new Vector2(0.5f, 1f);
        headerBarRect.sizeDelta = new Vector2(0f, FixedHeaderHeight);
        headerBarRect.anchoredPosition = Vector2.zero;
        Image headerBarImage = headerBarObject.GetComponent<Image>();
        headerBarImage.color = new Color(0.05f, 0.06f, 0.08f, 1f);
        headerBarImage.raycastTarget = false;
        headerBarRect.SetAsLastSibling();

        var headerTextObject = new GameObject("FixedHeaderText", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Text));
        var headerTextRect = headerTextObject.GetComponent<RectTransform>();
        headerTextRect.SetParent(headerBarRect, false);
        headerTextRect.anchorMin = Vector2.zero;
        headerTextRect.anchorMax = Vector2.one;
        headerTextRect.offsetMin = Vector2.zero;
        headerTextRect.offsetMax = Vector2.zero;
        view._fixedHeaderText = headerTextObject.GetComponent<Text>();
        view._fixedHeaderText.font = LocalizedTextManager.current_font;
        view._fixedHeaderText.fontSize = 9;
        view._fixedHeaderText.alignment = TextAnchor.MiddleCenter;
        view._fixedHeaderText.raycastTarget = false;
        view._fixedHeaderText.color = new Color(0.62f, 0.92f, 0.98f);

        return view;
    }

    public void Rebuild(IReadOnlyList<InstitutionNodeView> lineNodes,
        IReadOnlyList<InstitutionNodeView> foreignNodes, string selectedId, Action<string> onSelect,
        Func<InstitutionNodeView, (string title, string body)> buildTooltip = null,
        Func<InstitutionNodeView, NodeCardInfo> buildCard = null)
    {
        _onSelect = onSelect;
        _buildTooltip = buildTooltip;
        _buildCard = buildCard;
        _selectedId = selectedId ?? "";
        _engine.ClearNodes();
        _engine.ClearEdges();
        _nodeFrames.Clear();

        List<GraphLayout.Node> layoutNodes = lineNodes
            .Select(view => new GraphLayout.Node(view.Node.id, view.Node.branch, view.Node.advancement)).ToList();
        GraphLayout.Result layout = GraphLayout.TieredLanes(layoutNodes, BranchOrder, NodeWidth, NodeHeight,
            LaneGap, RowGap);
        Dictionary<string, Vector2> positions = layout.Positions;

        // 每条分支已达的等级只在视口顶部固定的摘要栏里报一次数(拖拽/缩放都不会跟着挪走)，
        // 不再在车道上方重复画一份小字——之前两处都画，看着像同一个数字出现了两次。
        var summaryParts = new List<string>();
        foreach (string branch in layout.LaneCenters.Keys
                     .OrderBy(b => Array.IndexOf(BranchOrder, b) is var i && i >= 0 ? i : BranchOrder.Length)
                     .ThenBy(b => b, StringComparer.Ordinal))
        {
            int reached = lineNodes
                .Where(view => view.Node.branch == branch &&
                               view.Status is InstitutionNodeStatus.Enacted or InstitutionNodeStatus.Absorbed)
                .Select(view => view.Node.advancement).DefaultIfEmpty(0).Max();
            summaryParts.Add($"{GetBranchShortName(branch)} {reached}");
        }
        if (_fixedHeaderText != null) _fixedHeaderText.text = string.Join("  ·  ", summaryParts);

        // 先画连线，再建节点
        foreach (InstitutionNodeView view in lineNodes)
        {
            foreach (string required in InstitutionDefinitionRegistry.GetAllPrerequisites(view.Node))
            {
                if (!positions.TryGetValue(required, out Vector2 from) ||
                    !positions.TryGetValue(view.Node.id, out Vector2 to)) continue;
                bool satisfied = view.Status is InstitutionNodeStatus.Enacted
                    or InstitutionNodeStatus.Absorbed or InstitutionNodeStatus.Reforming
                    or InstitutionNodeStatus.Available or InstitutionNodeStatus.Forceable;
                Color color = satisfied ? new Color(0.53f, 0.78f, 0.86f, 0.9f) : new Color(1f, 1f, 1f, 0.2f);
                _engine.CreateElbow(from, to, NodeHalfSize, color, satisfied ? 1.6f : 1f);
            }
        }
        foreach (InstitutionNodeView view in lineNodes)
        {
            if (positions.TryGetValue(view.Node.id, out Vector2 position)) CreateNode(view, position, false);
        }

        // 外来制度挂在树右侧单独一列，不与本线连线（吸收不看前置，连了反而误导）
        float foreignX = layout.TotalWidth / 2f + ForeignLaneGap + NodeWidth / 2f;
        float RowY(int tier) => -tier * (NodeHeight + RowGap);
        if (foreignNodes.Count > 0)
            CreateLaneHeader(LM.Get("institution_foreign_title"), new Vector2(foreignX, RowY(1) + NodeHeight + RowGap));
        for (int i = 0; i < foreignNodes.Count; i++)
            CreateNode(foreignNodes[i], new Vector2(foreignX, RowY(1 + i)), true);

        int maxTier = lineNodes.Count > 0 ? lineNodes.Max(view => view.Node.advancement) : 1;
        int rows = Math.Max(maxTier, foreignNodes.Count) + 1;
        Vector2 contentSize = new Vector2(
            (foreignNodes.Count > 0 ? foreignX * 2f : layout.TotalWidth) + NodeWidth,
            rows * (NodeHeight + RowGap) + NodeHeight * 2f);

        // 打开时尽量缩到装得下整张图的宽度，并把树顶对齐到视口上沿(往下让出固定摘要栏的高度)。
        // 缩放不低于 MinFitScale——卡片上的小字再缩就看不清了，装不下的部分靠拖拽平移。
        Rect viewportRect = _engine.ViewportTransform.rect;
        float fitScale = Mathf.Clamp(viewportRect.width / Math.Max(1f, contentSize.x) * 0.98f, MinFitScale, 1f);
        Vector2 fitPosition = new Vector2(0f,
            contentSize.y / 2f * fitScale - viewportRect.height / 2f + (NodeHeight / 2f) * fitScale - FixedHeaderHeight);
        _engine.SetContent(contentSize, fitScale, fitPosition);
    }

    private void CreateLaneHeader(string text, Vector2 position)
    {
        var headerObject = new GameObject("LaneHeader", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Text));
        var rect = headerObject.GetComponent<RectTransform>();
        rect.SetParent(_engine.ContentTransform, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth + LaneGap, 10f);
        rect.anchoredPosition = position;
        Text label = headerObject.GetComponent<Text>();
        label.font = LocalizedTextManager.current_font;
        label.fontSize = 6;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.color = new Color(0.50f, 0.85f, 0.92f);
        label.text = text;
    }

    private void CreateNode(InstitutionNodeView view, Vector2 position, bool foreign)
    {
        var nodeObject = new GameObject($"Node_{view.Node.id}", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(TipButton), typeof(CanvasGroup));
        var rect = nodeObject.GetComponent<RectTransform>();
        rect.SetParent(_engine.ContentTransform, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth, NodeHeight);
        rect.anchoredPosition = position;

        // 卡片框体跟帝国界面的派系卡片是同一套素材，不再整体叠色压暗；状态靠左侧色条+状态文字区分。
        Image frame = nodeObject.GetComponent<Image>();
        ApplyFrame(frame, view.Status, view.Node.id == _selectedId);
        string nodeId = view.Node.id;
        nodeObject.GetComponent<Button>().onClick.AddListener(() => _onSelect?.Invoke(nodeId));
        // 还没解锁的节点整体淡一些，但文字仍然看得清(之前是深色字压在深色框上，基本看不见)。
        nodeObject.GetComponent<CanvasGroup>().alpha =
            view.Status is InstitutionNodeStatus.Locked or InstitutionNodeStatus.ForeignLocked ? 0.6f : 1f;

        // 悬浮就能看到详情，不用先点一下再往下翻——跟模组其它按钮用的是同一套 TipButton。
        if (_buildTooltip != null)
        {
            TipButton tip = nodeObject.GetComponent<TipButton>();
            (string title, string body) = _buildTooltip(view);
            string titleKey = $"institution_tip_title_{view.Node.id}";
            string bodyKey = $"institution_tip_body_{view.Node.id}";
            LM.AddToCurrentLocale(titleKey, title);
            LM.AddToCurrentLocale(bodyKey, body);
            tip.type = "normal";
            tip.textOnClick = titleKey;
            tip.textOnClickDescription = bodyKey;
            tip.text_description_2 = "";
            tip.hoverAction = tip.showTooltipDefault;
            tip.enabled = true;
        }

        // 左侧状态色条，颜色跟窗口顶部图例是同一份
        var stripeObject = new GameObject("StatusStripe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var stripeRect = stripeObject.GetComponent<RectTransform>();
        stripeRect.SetParent(rect, false);
        stripeRect.anchorMin = new Vector2(0f, 0f);
        stripeRect.anchorMax = new Vector2(0f, 1f);
        stripeRect.offsetMin = new Vector2(4f, 6f);
        stripeRect.offsetMax = new Vector2(6f, -6f);
        Image stripe = stripeObject.GetComponent<Image>();
        stripe.color = GetStatusTint(view.Status);
        stripe.raycastTarget = false;

        NodeCardInfo info = _buildCard?.Invoke(view) ?? new NodeCardInfo("", "");
        // 名称用模组自己的 SimpleText(自带 windowInnerSliced 黑底)，跟族谱卡片信息栏同一套样式。
        // 名称行占卡片上沿 3~13，左侧让出等级数字，右侧让出边框。
        SimpleText name = UnityEngine.Object.Instantiate(SimpleText.Prefab, rect);
        name.Setup(InstitutionSystem.GetNodeName(view.Node), TextAnchor.MiddleCenter, new Vector2(54f, 10f));
        RectTransform nameRect = name.GetComponent<RectTransform>();
        nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0.5f);
        nameRect.pivot = new Vector2(0.5f, 0.5f);
        nameRect.anchoredPosition = new Vector2(5f, NodeHeight / 2f - 8f);
        nameRect.localScale = Vector3.one;
        name.text.fontStyle = FontStyle.Bold;
        name.text.color = new Color(0.97f, 0.94f, 0.84f);
        name.text.resizeTextMinSize = 4;
        name.text.resizeTextMaxSize = 7;
        foreach (Graphic graphic in name.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        CreateCardText(rect, "Status", 14f, 22f, 5, Color.white).text = info.StatusLine ?? "";
        CreateCardText(rect, "Info", 22f, 30f, 5, new Color(0.72f, 0.78f, 0.80f)).text = info.InfoLine ?? "";

        var tierObject = new GameObject("Tier", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var tierRect = tierObject.GetComponent<RectTransform>();
        tierRect.SetParent(rect, false);
        tierRect.anchorMin = new Vector2(0f, 1f);
        tierRect.anchorMax = new Vector2(0f, 1f);
        tierRect.pivot = new Vector2(0f, 1f);
        tierRect.sizeDelta = new Vector2(10f, 8f);
        tierRect.anchoredPosition = new Vector2(8f, -3f);
        Text tierText = tierObject.GetComponent<Text>();
        tierText.font = LocalizedTextManager.current_font;
        tierText.fontSize = 5;
        tierText.alignment = TextAnchor.UpperLeft;
        tierText.raycastTarget = false;
        tierText.color = new Color(0.95f, 0.76f, 0.29f, 0.9f);
        tierText.text = view.Node.advancement.ToString();

        if (info.Progress >= 0f) CreateProgressBar(rect, Mathf.Clamp01(info.Progress));

        _nodeFrames[view.Node.id] = frame;
    }

    // 卡片内一行文字：左右留出状态色条和边框的空位，top/bottom 是距卡片上沿的距离。
    private static Text CreateCardText(RectTransform parent, string objectName, float top, float bottom,
        int fontSize, Color color)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, false);
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.offsetMin = new Vector2(9f, -bottom);
        textRect.offsetMax = new Vector2(-5f, -top);
        Text text = textObject.GetComponent<Text>();
        text.font = LocalizedTextManager.current_font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.color = color;
        return text;
    }

    private static void CreateProgressBar(RectTransform parent, float progress)
    {
        var trackObject = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var trackRect = trackObject.GetComponent<RectTransform>();
        trackRect.SetParent(parent, false);
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.offsetMin = new Vector2(10f, 4f);
        trackRect.offsetMax = new Vector2(-6f, 6f);
        Image track = trackObject.GetComponent<Image>();
        track.color = new Color(0f, 0f, 0f, 0.45f);
        track.raycastTarget = false;

        var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.SetParent(trackRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(progress, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillObject.GetComponent<Image>();
        fill.color = new Color(0.40f, 0.84f, 0.77f);
        fill.raycastTarget = false;
    }

    public void SetSelected(string nodeId, IReadOnlyList<InstitutionNodeView> allViews)
    {
        _selectedId = nodeId ?? "";
        foreach (InstitutionNodeView view in allViews)
        {
            if (_nodeFrames.TryGetValue(view.Node.id, out Image frame))
                ApplyFrame(frame, view.Status, view.Node.id == _selectedId);
        }
    }

    // 选中用帝国界面面板那张金边框(regimeFrame)，已施行/改革中用派系卡片的金边版，其余用普通派系卡片框。
    private static void ApplyFrame(Image frame, InstitutionNodeStatus status, bool selected)
    {
        bool strong = status is InstitutionNodeStatus.Enacted or InstitutionNodeStatus.Absorbed
            or InstitutionNodeStatus.Reforming;
        Sprite sprite = GetSlicedFrame(selected ? "regimeFrame" : strong ? "FactionFrame_dominate" : "FactionFrame");
        frame.sprite = sprite;
        frame.type = Image.Type.Sliced;
        // 没找到素材时退化成状态色块，至少还能看出状态
        frame.color = sprite != null ? Color.white : GetStatusTint(status) * 0.35f;
    }

    // 跟 UIHelper.AddStretchBackground 同一套九宫格切法，但缓存起来——每个节点、每次切换选中
    // 都新建 Sprite 太浪费。
    private static Sprite GetSlicedFrame(string name)
    {
        if (SlicedFrames.TryGetValue(name, out Sprite cached) && cached != null) return cached;
        Sprite source = SpriteTextureLoader.getSprite($"ui/{name}");
        if (source == null) return null;
        Sprite sliced = Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit,
            0, SpriteMeshType.FullRect, new Vector4(11, 11, 24, 24));
        SlicedFrames[name] = sliced;
        return sliced;
    }

    // 窗口顶部的图例直接取这里的颜色，保证跟树上的节点是同一套色，不会两处各画一份
    public static Color GetLegendColor(InstitutionNodeStatus status) => GetStatusTint(status);

    private static Color GetStatusTint(InstitutionNodeStatus status)
    {
        return status switch
        {
            InstitutionNodeStatus.Enacted => new Color(0.62f, 1f, 0.68f),
            InstitutionNodeStatus.Absorbed => new Color(1f, 0.88f, 0.55f),
            InstitutionNodeStatus.Reforming => new Color(0.62f, 0.95f, 1f),
            InstitutionNodeStatus.Available => new Color(1f, 0.95f, 0.78f),
            InstitutionNodeStatus.Forceable => new Color(1f, 0.72f, 0.68f),
            InstitutionNodeStatus.ForeignReady => new Color(0.82f, 0.78f, 1f),
            InstitutionNodeStatus.ForeignContacting => new Color(0.74f, 0.72f, 0.88f),
            InstitutionNodeStatus.ForeignLocked => new Color(0.62f, 0.62f, 0.68f),
            _ => new Color(0.68f, 0.68f, 0.72f)
        };
    }

    private static string GetBranchShortName(string branch)
    {
        string key = $"institution_branch_short_{branch}";
        string value = LM.Get(key);
        return string.IsNullOrWhiteSpace(value) || value == key ? branch : value;
    }

    public void ResetView() => _engine.ResetView();
}
