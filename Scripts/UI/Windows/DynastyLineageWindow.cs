using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 世系图(宽窗口)：一个王朝的历代君主连同把他们连起来的祖先，画成自上而下的方框连线图(像史书后附的"某朝世系图")。
//   · 取本帝国史书里这一朝(按朝代名分)的历代君主，在宗族里沿父系往上找，直到接上已画的人；只画君主和连接他们的祖先，
//     开国之君之上至多留三代(追尊的祖先)；不同宗族(篡位、易姓)的君主各成一棵树并排；
//   · 方框上行是庙号(没有庙号写谥号，在位的写"今上"，非君主的祖先写其封号、官职)，下行是名字；君主金框，祖先灰框；
//   · 可拖拽、滚轮缩放；点方框打开这个人的族谱树(见 SpecificClanTreeWindow)；顶上一排按钮切换朝代。
// 从历代君主窗口(EmpireHistoryWindow)左栏的"世系图"按钮打开。
public class DynastyLineageWindow : AbstractWideWindow<DynastyLineageWindow>
{
    private const float PanelWidth = 460f, GraphHeight = 318f;
    // 方框至少 36 高，边框九宫格才不压扁(见 UIHelper.SlicedFrame)
    private const float NodeWidth = 64f, NodeHeight = 36f, ColGap = 8f, RowGap = 18f;
    private const int AncestorsAboveFounder = 3;
    private static readonly Vector2 NodeHalf = new(NodeWidth / 2f, NodeHeight / 2f);
    private static readonly Color EdgeColor = new(0.78f, 0.74f, 0.62f, 1f);

    private static Empire _pendingEmpire;
    private Empire _empire;
    private string _dynasty;
    private AutoVertLayoutGroup _root;
    private GraphView _graph;

    private sealed class Node
    {
        public PersonalClanIdentity identity;
        public EmpireCraftHistory history;
        public int reign = int.MaxValue;
        public Node parent;
        public readonly List<Node> children = new();
        public float x;
        public int depth;
    }

    public static void Open(Empire empire)
    {
        if (empire?.data == null) return;
        _pendingEmpire = empire;
        ScrollWindow.showWindow(nameof(DynastyLineageWindow));
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
        if (_pendingEmpire != null && _pendingEmpire != _empire)
        {
            _empire = _pendingEmpire;
            _dynasty = null;
        }
        Rebuild();
        StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        _root.transform.ClearChildren();
        _graph = null;
    }

    // 本帝国史书里的历代君主(按在位先后)，含今上
    private List<EmpireCraftHistory> Histories()
    {
        var result = new List<EmpireCraftHistory>();
        if (_empire?.data?.history != null) result.AddRange(_empire.data.history.Where(history => history != null));
        EmpireCraftHistory current = _empire?.data?.currentHistory;
        if (current != null && !result.Contains(current)) result.Add(current);
        return result.Where(history => !history.is_republic).ToList();
    }

    private static string DynastyOf(EmpireCraftHistory history) =>
        !string.IsNullOrWhiteSpace(history.dynasty_name) ? history.dynasty_name
        : !string.IsNullOrWhiteSpace(history.royal_surname) ? history.royal_surname
        : history.empire_name ?? "";

    private void Rebuild()
    {
        _root.transform.ClearChildren();
        _graph = null;
        List<EmpireCraftHistory> all = Histories();
        List<string> dynasties = all.Select(DynastyOf).Distinct().ToList();
        if (_dynasty == null || !dynasties.Contains(_dynasty))
            _dynasty = _empire?.data?.currentHistory != null ? DynastyOf(_empire.data.currentHistory) : dynasties.LastOrDefault();
        var space = _root.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.UpperCenter);
        DrawHeader(space, dynasties);
        List<EmpireCraftHistory> reigns = all.Where(history => DynastyOf(history) == _dynasty).ToList();
        _graph = GraphView.Create(space.transform, new Vector2(PanelWidth, GraphHeight), GraphOrientation.Vertical,
            objectName: "DynastyLineageViewport");
        _graph.SetClampMargin(NodeHalf);
        Draw(reigns);
    }

    // 顶栏：标题(朝代 · 几帝)与朝代切换按钮
    private void DrawHeader(AutoVertLayoutGroup space, List<string> dynasties)
    {
        var header = space.BeginHoriGroup(new Vector2(PanelWidth, 14f), TextAnchor.MiddleLeft, 3, new RectOffset(2, 2, 0, 0));
        string title = string.Format(LM.Get("lineage_chart_title"), _dynasty ?? "");
        var text = header.AddTextIntoHoriLayout(title.ColorString("#F3C34A"), true, TextAnchor.MiddleLeft,
            new Vector2(120f, 13f));
        text.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        foreach (string dynasty in dynasties.Skip(Mathf.Max(0, dynasties.Count - 8)))
        {
            string target = dynasty;
            bool selected = dynasty == _dynasty;
            header.AddButtonIntoHoriLayout($"lineage_dynasty_{dynasty}", (selected ? "【" + dynasty + "】" : dynasty),
                () =>
                {
                    if (_dynasty == target) return;
                    _dynasty = target;
                    Rebuild();
                }, size: new Vector2(Mathf.Clamp(UIHelper.EstimateTextWidth(dynasty, 6) + 14f, 30f, 70f), 12f));
        }
    }

    private void Draw(List<EmpireCraftHistory> reigns)
    {
        // 君主对应的宗族人物
        var byActor = new Dictionary<long, PersonalClanIdentity>();
        var byName = new Dictionary<string, PersonalClanIdentity>();
        var actorIds = new HashSet<long>(reigns.Where(history => history.id > 0).Select(history => history.id));
        var names = new HashSet<string>(reigns.Where(history => !string.IsNullOrWhiteSpace(history.emperor)).Select(history => history.emperor));
        foreach (PersonalClanIdentity identity in SpecificClanManager._globalPersonLookup.Values)
        {
            if (identity == null) continue;
            if (actorIds.Contains(identity.actor_id) && !byActor.ContainsKey(identity.actor_id)) byActor[identity.actor_id] = identity;
            if (identity.name != null && names.Contains(identity.name))
                byName[identity.name] = byName.ContainsKey(identity.name) ? null : identity;
        }
        var nodes = new Dictionary<long, Node>();
        var loose = new List<Node>();
        for (int i = 0; i < reigns.Count; i++)
        {
            EmpireCraftHistory history = reigns[i];
            PersonalClanIdentity identity = history.id > 0 && byActor.TryGetValue(history.id, out var found) ? found
                : !string.IsNullOrWhiteSpace(history.emperor) && byName.TryGetValue(history.emperor, out found) ? found : null;
            if (identity == null)
            {
                loose.Add(new Node { history = history, reign = i });
                continue;
            }
            Node node = Get(nodes, identity);
            if (node.history == null) { node.history = history; node.reign = i; }
            // 沿父系往上，接上已画的人为止
            Node child = node;
            for (int step = 0; step < 40 && child.parent == null && child.identity.father > 0; step++)
            {
                PersonalClanIdentity father = SpecificClanManager.getPerson(child.identity.father);
                if (father == null) break;
                bool existed = nodes.ContainsKey(father.id);
                Node parent = Get(nodes, father);
                child.parent = parent;
                if (!parent.children.Contains(child)) parent.children.Add(child);
                if (existed) break;
                child = parent;
            }
        }
        // 树根：开国之君之上至多留几代
        var roots = nodes.Values.Where(node => node.parent == null).ToList();
        for (int i = 0; i < roots.Count; i++)
        {
            Node root = roots[i];
            while (root.history == null && root.children.Count == 1 && GenerationsToRuler(root) > AncestorsAboveFounder)
            {
                Node next = root.children[0];
                next.parent = null;
                root = next;
            }
            roots[i] = root;
        }
        roots = roots.Distinct().Where(root => Contains(root, node => node.history != null)).OrderBy(MinReign).ToList();
        foreach (Node node in loose) roots.Add(node);
        Layout(roots);
    }

    private static Node Get(Dictionary<long, Node> nodes, PersonalClanIdentity identity)
    {
        if (!nodes.TryGetValue(identity.id, out Node node)) nodes[identity.id] = node = new Node { identity = identity };
        return node;
    }

    // 从这人往下沿独子一路到君主隔了几代
    private static int GenerationsToRuler(Node node)
    {
        int generations = 0;
        while (node != null && node.history == null && node.children.Count == 1)
        {
            node = node.children[0];
            generations++;
        }
        return generations;
    }

    private static bool Contains(Node node, global::System.Func<Node, bool> predicate) =>
        predicate(node) || node.children.Any(child => Contains(child, predicate));

    private static int MinReign(Node node) => Mathf.Min(node.reign, node.children.Select(MinReign).DefaultIfEmpty(int.MaxValue).Min());

    // 布局：一棵棵树并排，叶子各占一列，父亲居子女正中，一代一行
    private void Layout(List<Node> roots)
    {
        if (roots.Count == 0)
        {
            _graph.SetContent(new Vector2(PanelWidth, GraphHeight), 1f, Vector2.zero);
            return;
        }
        float cursor = 0f;
        int maxDepth = 0;
        foreach (Node root in roots)
        {
            Place(root, 0, ref cursor, ref maxDepth);
            cursor += 1f;
        }
        float columns = Mathf.Max(1f, cursor - 1f);
        float width = columns * (NodeWidth + ColGap) + NodeWidth;
        float height = (maxDepth + 1) * (NodeHeight + RowGap);
        var all = new List<Node>();
        foreach (Node root in roots) Collect(root, all);
        Vector2 Position(Node node) => new(node.x * (NodeWidth + ColGap) - width / 2f + NodeWidth / 2f,
            height / 2f - NodeHeight / 2f - node.depth * (NodeHeight + RowGap));
        foreach (Node node in all)
            if (node.parent != null) _graph.CreateElbow(Position(node.parent), Position(node), NodeHalf, EdgeColor, 1.5f);
        foreach (Node node in all) DrawNode(node, Position(node));
        Vector2 content = new(width + NodeWidth, height + NodeHeight);
        float scale = Mathf.Clamp(Mathf.Min(PanelWidth / content.x, 1f), 0.35f, 1f);
        // 整张图宽装得下就居中，太高就从顶上看起
        float y = content.y * scale > GraphHeight ? GraphHeight / 2f - content.y * scale / 2f : 0f;
        _graph.SetContent(content, scale, new Vector2(0f, y));
    }

    private static void Place(Node node, int depth, ref float cursor, ref int maxDepth)
    {
        node.depth = depth;
        maxDepth = Mathf.Max(maxDepth, depth);
        if (node.children.Count == 0)
        {
            node.x = cursor;
            cursor += 1f;
            return;
        }
        // 子女按在位先后排，没在位的支脉排后
        node.children.Sort((a, b) => MinReign(a).CompareTo(MinReign(b)));
        foreach (Node child in node.children) Place(child, depth + 1, ref cursor, ref maxDepth);
        node.x = (node.children.First().x + node.children.Last().x) / 2f;
    }

    private static void Collect(Node node, List<Node> all)
    {
        all.Add(node);
        foreach (Node child in node.children) Collect(child, all);
    }

    private void DrawNode(Node node, Vector2 position)
    {
        bool ruler = node.history != null;
        var nodeObject = new GameObject("LineageNode", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = nodeObject.GetComponent<RectTransform>();
        rect.SetParent(_graph.ContentTransform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth, NodeHeight);
        rect.anchoredPosition = position;
        Image frame = nodeObject.GetComponent<Image>();
        frame.sprite = UIHelper.SlicedFrame(ruler ? "FactionFrame_dominate" : "FactionFrame");
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = true;
        Label(rect, Title(node).ColorString(ruler ? "#F3C34A" : "#A8B8BE"), 6f, 6, FontStyle.Bold);
        Label(rect, (node.identity?.name ?? node.history?.emperor ?? "?").ColorString("#F2EEE2"), -5f, 6, FontStyle.Normal);
        if (node.identity == null) return;
        PersonalClanIdentity identity = node.identity;
        Button button = nodeObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => SpecificClanTreeWindow.ShowFor(identity));
    }

    // 方框上行：君主写庙号(没有庙号写谥号，在位的写今上)，祖先写封号、官职
    private string Title(Node node)
    {
        EmpireCraftHistory history = node.history;
        if (history != null)
        {
            if (history == _empire?.data?.currentHistory && string.IsNullOrEmpty(history.miaohao_name) &&
                string.IsNullOrEmpty(history.shihao_name)) return LM.Get("lineage_reigning");
            if (!string.IsNullOrEmpty(history.miaohao_name)) return LM.Get(history.miaohao_name) + LM.Get(history.miaohao_suffix);
            if (!string.IsNullOrEmpty(history.shihao_name)) return PosthumousNameGenerator.ShiTitle(history);
            return string.Format(LM.Get("lineage_reign_n"), node.reign + 1);
        }
        PersonalClanIdentity identity = node.identity;
        return identity?.ownedTitleNames?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ??
               (!string.IsNullOrWhiteSpace(identity?.officeName) ? identity.officeName : "");
    }

    private static void Label(RectTransform parent, string text, float y, int fontSize, FontStyle style)
    {
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(NodeWidth - 6f, 9f);
        rect.anchoredPosition = new Vector2(0f, y);
        Text label = textObject.GetComponent<Text>();
        label.font = LocalizedTextManager.current_font;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.supportRichText = true;
        label.raycastTarget = false;
        label.color = Color.white;
        label.text = text;
    }
}
