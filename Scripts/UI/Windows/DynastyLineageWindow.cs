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
//   · 方框上行是庙号(没有庙号写谥号，在位的写"今上"，非君主的祖先写其封号、官职)，中行是名字，
//     君主下行写怎么继的位——对上一位君主是长子、第几子、孙、兄弟、侄、旁支入继、易姓，废立、兵变、争位、宫变上台的
//     另标红字(见 PrinceStruggleSystem)；君主金框，祖先灰框；
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
    // 继位线：前后两代君主查不到血缘(外姓入继、篡位、族谱缺人)时也连起来，淡一些
    private static readonly Color SuccessionColor = new(0.78f, 0.74f, 0.62f, 0.35f);
    private readonly List<(Node from, Node to)> _succession = new();

    private static Empire _pendingEmpire;
    private Empire _empire;
    private string _dynasty;
    private AutoVertLayoutGroup _root;
    private GraphView _graph;

    private sealed class Node
    {
        public PersonalClanIdentity identity;
        public EmpireCraftHistory history;
        public string relation = "";
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
        var reignNodes = new List<Node>();
        PersonalClanIdentity previous = null;
        for (int i = 0; i < reigns.Count; i++)
        {
            EmpireCraftHistory history = reigns[i];
            PersonalClanIdentity identity = history.id > 0 && byActor.TryGetValue(history.id, out var found) ? found
                : !string.IsNullOrWhiteSpace(history.emperor) && byName.TryGetValue(history.emperor, out found) ? found : null;
            string relation = Relation(previous, identity, history);
            previous = identity;
            if (identity == null)
            {
                var looseNode = new Node { history = history, reign = i, relation = relation };
                loose.Add(looseNode);
                reignNodes.Add(looseNode);
                continue;
            }
            Node node = Get(nodes, identity);
            if (node.history == null) { node.history = history; node.reign = i; node.relation = relation; }
            reignNodes.Add(node);
            // 沿帝系往上(一般是父亲；母亲是已画的君主、或与本人同宗时接母亲)，接上已画的人为止
            Node child = node;
            for (int step = 0; step < 40 && child.parent == null; step++)
            {
                PersonalClanIdentity father = LineageParent(child.identity, nodes);
                if (father == null) break;
                bool existed = nodes.ContainsKey(father.id);
                Node parent = Get(nodes, father);
                child.parent = parent;
                if (!parent.children.Contains(child)) parent.children.Add(child);
                if (existed) break;
                child = parent;
            }
        }
        // 前后两代君主不在同一棵树上：沿父母两边溯源到共同祖先接起来
        for (int i = 1; i < reignNodes.Count; i++)
            if (RootOf(reignNodes[i - 1]) != RootOf(reignNodes[i])) Connect(reignNodes[i - 1], reignNodes[i], nodes);
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
        // 前后两代君主不在同一棵树上(查不到血缘)：画一条继位线，继位顺序一路追得下去
        _succession.Clear();
        for (int i = 1; i < reignNodes.Count; i++)
            if (RootOf(reignNodes[i - 1]) != RootOf(reignNodes[i])) _succession.Add((reignNodes[i - 1], reignNodes[i]));
        Layout(roots);
    }

    // 两代君主隔得远(堂亲、叔侄、宗室入继)：从两人沿父母两边往上找最近的共同祖先(至多 MaxTraceGenerations 代)，
    // 把两条路上的人都画成卡片接上(用户 2026-10-15："把他们之间有关系的所有角色卡片都放上去直到连上")
    private const int MaxTraceGenerations = 12;

    private static void Connect(Node a, Node b, Dictionary<long, Node> nodes)
    {
        if (a?.identity == null || b?.identity == null) return;
        (Dictionary<long, int> distA, Dictionary<long, long> downA) = Ancestry(a.identity);
        (Dictionary<long, int> distB, Dictionary<long, long> downB) = Ancestry(b.identity);
        long best = -1L;
        int bestDistance = int.MaxValue;
        foreach (var pair in distA)
            if (distB.TryGetValue(pair.Key, out int other) && pair.Value + other < bestDistance)
            {
                best = pair.Key;
                bestDistance = pair.Value + other;
            }
        if (best < 0) return;
        Link(b, best, downB, nodes);
        Link(a, best, downA, nodes);
    }

    // 往上的祖先：到本人隔几代，以及路上往下一步是谁(还原路径用)
    private static (Dictionary<long, int> distance, Dictionary<long, long> down) Ancestry(PersonalClanIdentity start)
    {
        var distance = new Dictionary<long, int> { [start.id] = 0 };
        var down = new Dictionary<long, long>();
        var queue = new Queue<PersonalClanIdentity>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            PersonalClanIdentity person = queue.Dequeue();
            int depth = distance[person.id];
            if (depth >= MaxTraceGenerations) continue;
            foreach (long parentId in new[] { person.father, person.mother })
            {
                if (parentId <= 0 || distance.ContainsKey(parentId)) continue;
                PersonalClanIdentity parent = SpecificClanManager.getPerson(parentId);
                if (parent == null) continue;
                distance[parentId] = depth + 1;
                down[parentId] = person.id;
                queue.Enqueue(parent);
            }
        }
        return (distance, down);
    }

    // 从共同祖先一路往下接到这位君主(路上已画的人换到这条线上)
    private static void Link(Node start, long ancestorId, Dictionary<long, long> down, Dictionary<long, Node> nodes)
    {
        var path = new List<long>();
        for (long id = ancestorId; ; id = down[id])
        {
            path.Add(id);
            if (id == start.identity.id || !down.ContainsKey(id) || path.Count > MaxTraceGenerations + 2) break;
        }
        if (path[path.Count - 1] != start.identity.id) return;
        for (int i = 1; i < path.Count; i++)
        {
            PersonalClanIdentity upper = SpecificClanManager.getPerson(path[i - 1]), lower = SpecificClanManager.getPerson(path[i]);
            if (upper == null || lower == null) return;
            Node parent = Get(nodes, upper), child = Get(nodes, lower);
            if (child.parent == parent) continue;
            // 防环：要接上的上一代不能是这人自己的后代
            if (Contains(child, node => node == parent)) return;
            child.parent?.children.Remove(child);
            child.parent = parent;
            if (!parent.children.Contains(child)) parent.children.Add(child);
        }
    }

    private static Node RootOf(Node node)
    {
        for (int guard = 0; guard < 200 && node?.parent != null; guard++) node = node.parent;
        return node;
    }

    // 帝系那一边的父母：一般是父亲；母亲是已画在树上的君主(父亲不是)，或与本人同宗而父亲不同宗时接母亲
    // (允许女主的文化里女君所生的子女是皇族，以前沿父亲接到配偶身上，与先帝断开)
    private static PersonalClanIdentity LineageParent(PersonalClanIdentity person, Dictionary<long, Node> nodes)
    {
        PersonalClanIdentity father = person.father > 0 ? SpecificClanManager.getPerson(person.father) : null;
        PersonalClanIdentity mother = person.mother > 0 ? SpecificClanManager.getPerson(person.mother) : null;
        if (mother == null) return father;
        if (father == null) return null;
        bool motherDrawn = nodes.ContainsKey(mother.id), fatherDrawn = nodes.ContainsKey(father.id);
        if (motherDrawn != fatherDrawn) return motherDrawn ? mother : father;
        return mother.specific_clan_id == person.specific_clan_id && father.specific_clan_id != person.specific_clan_id
            ? mother : father;
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
        foreach ((Node from, Node to) in _succession)
            if (all.Contains(from) && all.Contains(to)) _graph.CreateElbow(Position(from), Position(to), NodeHalf, SuccessionColor, 1f);
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
        Label(rect, Title(node).ColorString(ruler ? "#F3C34A" : "#A8B8BE"), ruler ? 9f : 6f, 6, FontStyle.Bold);
        Label(rect, (node.identity?.name ?? node.history?.emperor ?? "?").ColorString("#F2EEE2"), ruler ? -1f : -5f, 6, FontStyle.Normal);
        // 左上角：本朝第几位君主(按在位先后)，图上分支多时看得出继位顺序
        if (ruler && node.reign != int.MaxValue) Order(rect, node.reign + 1);
        if (ruler && !string.IsNullOrEmpty(node.relation))
            Label(rect, node.relation.ColorString(string.IsNullOrEmpty(node.history?.succession_note) ? "#9FB4BE" : "#E0705A"),
                -10f, 5, FontStyle.Normal);
        if (node.identity == null) return;
        PersonalClanIdentity identity = node.identity;
        Button button = nodeObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => SpecificClanTreeWindow.ShowFor(identity));
    }

    // 怎么继的位：对上一位君主的亲属关系，废立、兵变、争位、宫变另标
    private static string Relation(PersonalClanIdentity previous, PersonalClanIdentity current, EmpireCraftHistory history)
    {
        string relation;
        if (history.is_first) relation = LM.Get("lineage_rel_founder");
        else if (previous == null || current == null) relation = "";
        // 同一世系(分出去的诸王各支)不算易姓
        else if (current.specific_clan_id != previous.specific_clan_id &&
                 !SpecificClanManager.SameLineage(current._specificClan, previous._specificClan))
            relation = LM.Get("lineage_rel_other_clan");
        // 对先君的亲属称谓(父母两边都算，见 LineageKinship)；子女写第几子；受封过王的写上王号
        else
        {
            relation = current.father == previous.id || current.mother == previous.id
                ? ChildOrder(previous, current)
                : LineageKinship.Term(previous, current);
            if (!string.IsNullOrWhiteSpace(current.prince_title)) relation += "·" + current.prince_title;
        }
        string note = history.succession_note;
        if (string.IsNullOrEmpty(note)) return relation;
        string mark = LM.Get("lineage_note_" + note);
        return string.IsNullOrEmpty(relation) ? mark : relation + "·" + mark;
    }

    private static PersonalClanIdentity Father(PersonalClanIdentity person) =>
        person?.father > 0 ? SpecificClanManager.getPerson(person.father) : null;

    // 第几子(按出生先后，同性别排)
    private static string ChildOrder(PersonalClanIdentity parent, PersonalClanIdentity child)
    {
        int order = SpecificClanManager.getChildren(parent).Select(pair => pair.Item2)
            .Where(person => person != null && person.sex == child.sex)
            .OrderBy(Birth).ThenBy(person => person.id).ToList().FindIndex(person => person.id == child.id) + 1;
        bool daughter = child.sex == ActorSex.Female;
        if (order == 1) return LM.Get(daughter ? "lineage_rel_daughter_first" : "lineage_rel_son_first");
        if (order <= 0) return LM.Get(daughter ? "lineage_rel_daughter" : "lineage_rel_son");
        return string.Format(LM.Get(daughter ? "lineage_rel_daughter_n" : "lineage_rel_son_n"), Numeral(order));
    }

    private static readonly string[] Digits = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    private static string Numeral(int n)
    {
        string language = LocalizedTextManager.instance?.language;
        if (language != "cz" && language != "ch" || n >= 100) return n.ToString();
        if (n < 10) return Digits[n];
        return (n / 10 == 1 ? "" : Digits[n / 10]) + "十" + Digits[n % 10];
    }

    private static long Birth(PersonalClanIdentity person)
    {
        var match = global::System.Text.RegularExpressions.Regex.Match(person?.birthday ?? "", @"([\d,]+)年(\d+)月(\d+)日");
        if (!match.Success || !long.TryParse(match.Groups[1].Value.Replace(",", ""), out long year))
            return long.MaxValue / 2 + (person?.id ?? 0) % 1000000;
        return year * 10000 + int.Parse(match.Groups[2].Value) * 100 + int.Parse(match.Groups[3].Value);
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
            if (!string.IsNullOrEmpty(history.shihao_name)) return PosthumousNameGenerator.ShiTitle(history, _empire);
            return string.Format(LM.Get("lineage_reign_n"), node.reign + 1);
        }
        PersonalClanIdentity identity = node.identity;
        return identity?.ownedTitleNames?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ??
               (!string.IsNullOrWhiteSpace(identity?.officeName) ? identity.officeName : "");
    }

    private static void Order(RectTransform parent, int order)
    {
        var badge = new GameObject("Order", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = badge.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        string text = Numeral(order);
        rect.sizeDelta = new Vector2(Mathf.Max(9f, 4f + 4.5f * text.Length), 8f);
        rect.anchoredPosition = new Vector2(-NodeWidth / 2f + rect.sizeDelta.x / 2f + 1.5f, NodeHeight / 2f - 5.5f);
        Image image = badge.GetComponent<Image>();
        image.color = new Color(0.55f, 0.36f, 0.12f, 0.95f);
        image.raycastTarget = false;
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        Text label = textObject.GetComponent<Text>();
        label.font = LocalizedTextManager.current_font;
        label.fontSize = 5;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.color = new Color(1f, 0.93f, 0.75f, 1f);
        label.text = text;
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
