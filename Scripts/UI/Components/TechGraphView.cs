using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 科技树图形视图：材料一列放最左边，其余按分支分列，纵向是时代(tier)。
// 连线表示"需要"：材料 → 用到它的技术，前置技术 → 后续技术，技术 → 靠它才能发现的材料。
// 拖拽/缩放/连线都交给通用的 GraphView，这里只管节点长什么样。
public class TechGraphView
{
    public const string MaterialPrefix = "mat:";
    private const float NodeWidth = 76f;
    private const float NodeHeight = 36f;
    private const float LaneGap = 8f;
    private const float RowGap = 16f;
    private const float MinFitScale = 0.7f;
    private static readonly Vector2 NodeHalfSize = new(NodeWidth / 2f, NodeHeight / 2f);
    private static readonly Dictionary<string, Sprite> SlicedFrames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Sprite> IconCache = new(StringComparer.Ordinal);

    public readonly struct NodeInfo
    {
        public readonly string Id;
        public readonly string Lane;
        public readonly int Tier;
        public readonly string Name;
        public readonly string StatusLine;
        public readonly string InfoLine;
        public readonly TechNodeStatus Status;
        public readonly float Progress;
        public readonly Sprite Icon;
        public readonly IReadOnlyList<string> Requires;
        public readonly string TooltipTitle;
        public readonly string TooltipBody;

        public NodeInfo(string id, string lane, int tier, string name, string statusLine, string infoLine,
            TechNodeStatus status, float progress, Sprite icon, IReadOnlyList<string> requires, string tooltipTitle,
            string tooltipBody)
        {
            Id = id;
            Lane = lane;
            Tier = tier;
            Name = name;
            StatusLine = statusLine;
            InfoLine = infoLine;
            Status = status;
            Progress = progress;
            Icon = icon;
            Requires = requires;
            TooltipTitle = tooltipTitle;
            TooltipBody = tooltipBody;
        }
    }

    private GraphView _engine;
    private Action<string> _onSelect;
    private string _selectedId = "";
    private readonly Dictionary<string, (Image frame, TechNodeStatus status)> _frames = new(StringComparer.Ordinal);

    public static TechGraphView Create(Transform parent, Vector2 size)
    {
        var view = new TechGraphView();
        view._engine = GraphView.Create(parent, size, GraphOrientation.Vertical, objectName: "TechGraphViewport");
        view._engine.SetClampMargin(NodeHalfSize);
        return view;
    }

    public void ResetView() => _engine.ResetView();

    public void Rebuild(IReadOnlyList<NodeInfo> nodes, IList<string> laneOrder, string selectedId,
        Action<string> onSelect)
    {
        _onSelect = onSelect;
        _selectedId = selectedId ?? "";
        _engine.ClearNodes();
        _engine.ClearEdges();
        _frames.Clear();

        GraphLayout.Result layout = GraphLayout.TieredLanes(
            nodes.Select(node => new GraphLayout.Node(node.Id, node.Lane, node.Tier)).ToList(), laneOrder,
            NodeWidth, NodeHeight, LaneGap, RowGap, rootTier: 0);
        Dictionary<string, Vector2> positions = layout.Positions;
        int maxTier = nodes.Count > 0 ? nodes.Max(node => node.Tier) : 1;
        float verticalOffset = (maxTier + 1) * (NodeHeight + RowGap) / 2f;
        foreach (string id in positions.Keys.ToList()) positions[id] += new Vector2(0f, verticalOffset);

        // 车道标题
        float headerY = verticalOffset - (NodeHeight + RowGap) * 0.35f;
        foreach (KeyValuePair<string, float> lane in layout.LaneCenters)
            CreateLabel(TechnologySystem.GetBranchName(lane.Key), new Vector2(lane.Value, headerY),
                new Color(0.50f, 0.85f, 0.92f), 6);

        var statusById = nodes.ToDictionary(node => node.Id, node => node.Status, StringComparer.Ordinal);
        foreach (NodeInfo node in nodes)
        {
            if (!positions.TryGetValue(node.Id, out Vector2 to)) continue;
            foreach (string required in node.Requires)
            {
                if (!positions.TryGetValue(required, out Vector2 from)) continue;
                bool done = statusById.TryGetValue(required, out TechNodeStatus status) &&
                            status is TechNodeStatus.Researched or TechNodeStatus.MaterialDiscovered;
                Color color = done ? new Color(0.53f, 0.78f, 0.86f, 0.9f) : new Color(1f, 1f, 1f, 0.2f);
                _engine.CreateElbow(from, to, NodeHalfSize, color, done ? 1.6f : 1f);
            }
        }
        foreach (NodeInfo node in nodes)
            if (positions.TryGetValue(node.Id, out Vector2 position)) CreateNode(node, position);

        Vector2 contentSize = new(layout.TotalWidth + NodeWidth, (maxTier + 1) * (NodeHeight + RowGap) + NodeHeight * 2f);
        Rect viewportRect = _engine.ViewportTransform.rect;
        float fitScale = Mathf.Clamp(viewportRect.width / Math.Max(1f, contentSize.x) * 0.98f, MinFitScale, 1f);
        Vector2 fitPosition = new(0f, viewportRect.height / 2f - (headerY + 8f) * fitScale);
        _engine.SetContent(contentSize, fitScale, fitPosition);
    }

    public void SetSelected(string id)
    {
        _selectedId = id ?? "";
        foreach (KeyValuePair<string, (Image frame, TechNodeStatus status)> pair in _frames)
            ApplyFrame(pair.Value.frame, pair.Value.status, pair.Key == _selectedId);
    }

    private void CreateLabel(string text, Vector2 position, Color color, int fontSize)
    {
        var labelObject = new GameObject("LaneHeader", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(_engine.ContentTransform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth + LaneGap, 10f);
        rect.anchoredPosition = position;
        Text label = labelObject.GetComponent<Text>();
        label.font = LocalizedTextManager.current_font;
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.color = color;
        label.text = text;
    }

    private void CreateNode(NodeInfo node, Vector2 position)
    {
        var nodeObject = new GameObject($"Tech_{node.Id}", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(TipButton), typeof(CanvasGroup));
        var rect = nodeObject.GetComponent<RectTransform>();
        rect.SetParent(_engine.ContentTransform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth, NodeHeight);
        rect.anchoredPosition = position;

        Image frame = nodeObject.GetComponent<Image>();
        ApplyFrame(frame, node.Status, node.Id == _selectedId);
        _frames[node.Id] = (frame, node.Status);
        string id = node.Id;
        nodeObject.GetComponent<Button>().onClick.AddListener(() => _onSelect?.Invoke(id));
        nodeObject.GetComponent<CanvasGroup>().alpha =
            node.Status is TechNodeStatus.Locked or TechNodeStatus.MaterialUndiscovered ? 0.6f : 1f;

        string safeId = new string(node.Id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        TipButton tip = nodeObject.GetComponent<TipButton>();
        string titleKey = $"tech_tip_title_{safeId}";
        string bodyKey = $"tech_tip_body_{safeId}";
        LM.AddToCurrentLocale(titleKey, node.TooltipTitle ?? node.Name);
        LM.AddToCurrentLocale(bodyKey, node.TooltipBody ?? "");
        tip.type = "normal";
        tip.textOnClick = titleKey;
        tip.textOnClickDescription = bodyKey;
        tip.text_description_2 = "";
        tip.hoverAction = tip.showTooltipDefault;
        tip.enabled = true;

        var stripeObject = new GameObject("StatusStripe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var stripeRect = stripeObject.GetComponent<RectTransform>();
        stripeRect.SetParent(rect, false);
        stripeRect.anchorMin = new Vector2(0f, 0f);
        stripeRect.anchorMax = new Vector2(0f, 1f);
        stripeRect.offsetMin = new Vector2(4f, 6f);
        stripeRect.offsetMax = new Vector2(6f, -6f);
        Image stripe = stripeObject.GetComponent<Image>();
        stripe.color = GetStatusColor(node.Status);
        stripe.raycastTarget = false;

        CreateIcon(rect, node.Icon, new Vector2(8f, -3f), 8f);
        SimpleText name = UnityEngine.Object.Instantiate(SimpleText.Prefab, rect);
        name.Setup(node.Name, TextAnchor.MiddleCenter, new Vector2(50f, 10f));
        RectTransform nameRect = name.GetComponent<RectTransform>();
        nameRect.anchorMin = nameRect.anchorMax = nameRect.pivot = new Vector2(0.5f, 0.5f);
        nameRect.anchoredPosition = new Vector2(3f, NodeHeight / 2f - 8f);
        nameRect.localScale = Vector3.one;
        name.text.fontStyle = FontStyle.Bold;
        name.text.color = new Color(0.97f, 0.94f, 0.84f);
        name.text.resizeTextMinSize = 4;
        name.text.resizeTextMaxSize = 7;
        foreach (Graphic graphic in name.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

        CreateText(rect, 14f, 22f, 5, Color.white).text = node.StatusLine ?? "";
        CreateText(rect, 22f, 30f, 5, new Color(0.72f, 0.78f, 0.80f)).text = node.InfoLine ?? "";
        if (node.Progress >= 0f) CreateProgressBar(rect, Mathf.Clamp01(node.Progress));
    }

    private static Text CreateText(RectTransform parent, float top, float bottom, int fontSize, Color color)
    {
        var textObject = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
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

    private static void CreateIcon(RectTransform parent, Sprite sprite, Vector2 topLeft, float size)
    {
        if (sprite == null) return;
        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.SetParent(parent, false);
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0f, 1f);
        iconRect.sizeDelta = new Vector2(size, size);
        iconRect.anchoredPosition = topLeft;
        Image image = iconObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    public static Sprite Icon(params string[] paths)
    {
        string key = string.Join("|", paths);
        if (IconCache.TryGetValue(key, out Sprite cached)) return cached;
        Sprite found = null;
        foreach (string path in paths)
        {
            found = SpriteTextureLoader.getSprite(path);
            if (found != null) break;
        }
        IconCache[key] = found;
        return found;
    }

    public static Sprite GetBranchIcon(string branch) => branch switch
    {
        "metallurgy" => Icon("ui/icons/iconResCommonMetals", "ui/icons/iconResStone"),
        "military" => Icon("ui/icons/iconArmy", "ui/icons/iconSoldier", "ui/icons/iconWar"),
        "construction" => Icon("ui/icons/iconHoused", "ui/icons/iconBuildings", "ui/icons/iconCity"),
        "science" => Icon("ui/icons/iconBooks", "ui/icons/iconKnowledge"),
        _ => Icon("ui/icons/iconKnowledge")
    };

    public static Color GetStatusColor(TechNodeStatus status) => status switch
    {
        TechNodeStatus.Researched => new Color(0.62f, 1f, 0.68f),
        TechNodeStatus.Researching => new Color(0.62f, 0.95f, 1f),
        TechNodeStatus.Available => new Color(1f, 0.95f, 0.78f),
        TechNodeStatus.MaterialDiscovered => new Color(1f, 0.82f, 0.45f),
        _ => new Color(0.62f, 0.62f, 0.68f)
    };

    private static void ApplyFrame(Image frame, TechNodeStatus status, bool selected)
    {
        bool strong = status is TechNodeStatus.Researched or TechNodeStatus.Researching or
            TechNodeStatus.MaterialDiscovered;
        Sprite sprite = GetSlicedFrame(selected ? "regimeFrame" : strong ? "FactionFrame_dominate" : "FactionFrame");
        frame.sprite = sprite;
        frame.type = Image.Type.Sliced;
        frame.color = sprite != null ? Color.white : GetStatusColor(status) * 0.35f;
    }

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
}
