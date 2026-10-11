using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 军团在地图上的样子(借鉴钢铁雄心)：
//   · 战线：交战双方控制区相接的地块边，画成金色粗线；
//   · 兵牌：帝国卷册小框(Tools/IconForge/frame_forge.py 的 CorpsFrame_*)，边色表示任务——
//     进攻朱漆、防守石青、溃退铁灰、驻守橄榄青铜；左边国色印面上一枚棋子(中央禁军为后、军区为车、
//     军府为马、义军为兵)，右边兵力与兵力条(绿)、组织度条(青)；有统领实体时跟在统领头顶；
//     同一地块同一国的几支军合并成一块(标"×N"、兵力相加)，不同国并排；
//   · 交战标记：正在交战的城，朱漆框绛红底，两侧是攻守双方国色，中间是本月攻方胜算；
//   · 鼠标悬停兵牌或交战标记：放大、换金边、置顶，并显示详情提示；
//     点击打开军事页(属于帝国的国家开帝国窗口的军团页，独立王国开本国军事窗口)，点进来的那几支军、那处交战高亮。
// 画在地名画布上，按屏幕像素定大小(不随界面缩放放大)，镜头拉远时略缩小。
public static class CorpsMapRenderer
{
    // 以屏幕像素计(镜头远近再乘 0.85~1.15)；框的九宫格边距 6，见 frame_forge.py
    private const float CounterWidth = 74f, CounterHeight = 26f;
    private const float MarkerWidth = 64f, MarkerHeight = 18f;
    private const float FrameBorder = 6f;
    private const float HoverScale = 1.4f;
    private const float LineWidth = 2.5f;
    private const float TooltipRefresh = 1f;

    private sealed class Counter
    {
        public RectTransform Root;
        public Image Frame, Plate, Piece, Strength, Org;
        public Text Label;
        public float LabelWidth;
    }

    private sealed class Marker
    {
        public RectTransform Root;
        public Image Frame, Attacker, Defender;
        public Text Label;
        public float LabelWidth;
    }

    private struct Hit
    {
        public int Kind, Index;
        public long A, B;
        public Vector2 Center, Half;
    }

    private const int HoverNone = 0, HoverCounter = 1, HoverMarker = 2;

    private static RectTransform _root;
    private static Canvas _canvas;
    private static Font _font;
    private static Sprite _white;
    private static readonly List<Counter> Counters = new();
    private static readonly List<Image> Lines = new();
    private static readonly List<Marker> Markers = new();
    private static readonly Dictionary<int, float> StackOffsets = new();
    private static readonly List<Hit> Hits = new();
    private static readonly Dictionary<string, Sprite> Frames = new();

    // 悬停中的兵牌(A 地块 B 国)或交战标记(A 城)
    private static int _hoverKind;
    private static long _hoverA = -1L, _hoverB = -1L;
    private static float _tipAt = float.NegativeInfinity;
    private static readonly object CounterTipOwner = new();
    private static readonly object BattleTipOwner = new();
    private static PointerEventData _pointer;
    private static readonly List<RaycastResult> RaycastHits = new();

    public const string CounterTooltip = "empirecraft_corps_counter";
    public const string BattleTooltip = "empirecraft_corps_battle";

    // 悬停兵牌时原版地图提示让路(见 CorpsPatch)
    public static bool Hovering => _hoverKind != HoverNone;

    private sealed class CounterGroup
    {
        public Kingdom Kingdom;
        public TileZone Zone;
        public Vector3 World;
        public string Task;
        public int Count, Rank;
        public float Strength, Full, Org;
    }

    // 棋子：义军为兵、军府为马、军区为车、中央禁军为后
    private static int Rank(CorpsData corps) => corps.militia ? 0 : corps.post switch
    {
        (int)CityPopulationSystem.ArmyPost.Central => 3,
        (int)CityPopulationSystem.ArmyPost.District => 2,
        (int)CityPopulationSystem.ArmyPost.Prefecture => 1,
        _ => 0
    };

    private static readonly string[] PieceNames = { "pawn", "knight", "rook", "queen" };
    private static readonly Sprite[] Pieces = new Sprite[4];

    private static Sprite Piece(int rank)
    {
        rank = Mathf.Clamp(rank, 0, 3);
        return Pieces[rank] ??= SpriteTextureLoader.getSprite("ui/icons/corps/chess_" + PieceNames[rank]) ?? White;
    }

    private static readonly Dictionary<(int zone, long kingdom), CounterGroup> Groups = new();

    private static readonly Color PieceColor = new Color32(240, 232, 210, 255);
    private static readonly Color InkColor = new Color32(20, 22, 15, 255);
    private static readonly Color LabelColor = new Color32(236, 226, 198, 255);
    private static readonly Color StrengthColor = new Color32(101, 214, 110, 255);
    private static readonly Color OrgColor = new Color32(127, 216, 234, 255);
    private static readonly Color OrgLowColor = new Color32(232, 88, 79, 255);
    private static readonly Color FrontColor = new Color32(243, 195, 74, 235);

    private static bool ShowsCorps(Kingdom kingdom, CorpsData corps) =>
        corps.task != "reserve" || CorpsSystem.IsAtWar(kingdom);

    public static void EnsureHost()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (canvas == null) return;
        if (_root != null && _root.parent == canvas.transform) return;
        if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        Counters.Clear();
        Lines.Clear();
        Markers.Clear();
        ClearHover();
        var rootObject = new GameObject("EmpireCraftCorps", typeof(RectTransform), typeof(CorpsMapRendererHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();
        _canvas = canvas;
        _font = canvas.GetComponent<NameplateManager>()?.prefab?._text_name?.font;
    }

    private static Sprite White
    {
        get
        {
            if (_white != null) return _white;
            var texture = new Texture2D(2, 2) { filterMode = FilterMode.Point };
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            _white = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            return _white;
        }
    }

    // 九宫格小框(边距 6)；取不到图时退回纯色块
    private static Sprite Frame(string name)
    {
        if (Frames.TryGetValue(name, out Sprite sliced) && sliced != null) return sliced;
        Sprite source = SpriteTextureLoader.getSprite("ui/" + name);
        if (source == null) return White;
        source.texture.filterMode = FilterMode.Point;
        sliced = Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit, 0,
            SpriteMeshType.FullRect, new Vector4(FrameBorder, FrameBorder, FrameBorder, FrameBorder));
        Frames[name] = sliced;
        return sliced;
    }

    private static Sprite TaskFrame(string task) => Frame(task switch
    {
        "attack" => "CorpsFrame_lacquer",
        "defend" => "CorpsFrame_azure",
        "retreat" => "CorpsFrame_ash",
        _ => "CorpsFrame_olive"
    });

    private static void SetFrame(Image image, Sprite sprite)
    {
        if (image.sprite == sprite) return;
        image.sprite = sprite;
        // 框的一个像素对一个单位：九宫格边距按画布的参考像素换算
        float reference = _canvas == null ? 100f : _canvas.referencePixelsPerUnit;
        image.pixelsPerUnitMultiplier = sprite.pixelsPerUnit <= 0f ? 1f : reference / sprite.pixelsPerUnit;
    }

    private static Image NewImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = White;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image NewFrame(Transform parent, Sprite sprite)
    {
        Image image = NewImage(parent, "Frame", Color.white);
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        SetFrame(image, sprite);
        Place(image.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return image;
    }

    private static Text NewText(Transform parent, int size)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = _font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        text.color = LabelColor;
        text.raycastTarget = false;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    // 兵牌(单位：像素，左下为原点)：框 74×26；印面 (5,5)-(21,21) 外描 1 像素墨线；
    // 兵力字在 (25,11) 起；条槽 (24,4)-(右-4,11)，兵力条 (25,8)-(…,10)，组织度条 (25,6)-(…,8)
    private static Counter GetCounter(int index)
    {
        while (Counters.Count <= index)
        {
            var go = new GameObject("CorpsCounter", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var counter = new Counter { Root = go.GetComponent<RectTransform>() };
            counter.Root.sizeDelta = new Vector2(CounterWidth, CounterHeight);
            counter.Frame = NewFrame(go.transform, TaskFrame("reserve"));
            Image plateEdge = NewImage(go.transform, "PlateEdge", InkColor);
            Place(plateEdge.rectTransform, Vector2.zero, Vector2.zero, new Vector2(4f, 4f), new Vector2(22f, 22f));
            counter.Plate = NewImage(go.transform, "Plate", Color.gray);
            Place(counter.Plate.rectTransform, Vector2.zero, Vector2.zero, new Vector2(5f, 5f), new Vector2(21f, 21f));
            counter.Piece = NewImage(go.transform, "Piece", PieceColor);
            counter.Piece.preserveAspect = true;
            Place(counter.Piece.rectTransform, Vector2.zero, Vector2.zero, new Vector2(6f, 6f), new Vector2(20f, 20f));
            counter.Label = NewText(go.transform, 11);
            Place(counter.Label.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(25f, 11f), new Vector2(-4f, 23f));
            Image track = NewImage(go.transform, "Track", InkColor);
            Place(track.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 4f), new Vector2(-4f, 11f));
            counter.Strength = NewImage(go.transform, "Strength", StrengthColor);
            counter.Org = NewImage(go.transform, "Org", OrgColor);
            Counters.Add(counter);
        }
        return Counters[index];
    }

    // 交战标记：框 64×18(字长时加宽)；两侧国色小旗 (5,5)-(10,13)，中间字
    private static Marker GetMarker(int index)
    {
        while (Markers.Count <= index)
        {
            var go = new GameObject("CorpsBattle", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var marker = new Marker { Root = go.GetComponent<RectTransform>() };
            marker.Root.sizeDelta = new Vector2(MarkerWidth, MarkerHeight);
            marker.Frame = NewFrame(go.transform, Frame("CorpsBattle"));
            Image leftEdge = NewImage(go.transform, "AttackerEdge", InkColor);
            Place(leftEdge.rectTransform, Vector2.zero, Vector2.zero, new Vector2(4f, 4f), new Vector2(11f, 14f));
            marker.Attacker = NewImage(go.transform, "Attacker", Color.gray);
            Place(marker.Attacker.rectTransform, Vector2.zero, Vector2.zero, new Vector2(5f, 5f), new Vector2(10f, 13f));
            Image rightEdge = NewImage(go.transform, "DefenderEdge", InkColor);
            Place(rightEdge.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-11f, 4f), new Vector2(-4f, 14f));
            marker.Defender = NewImage(go.transform, "Defender", Color.gray);
            Place(marker.Defender.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 5f), new Vector2(-5f, 13f));
            marker.Label = NewText(go.transform, 10);
            marker.Label.alignment = TextAnchor.MiddleCenter;
            marker.Label.color = new Color32(255, 222, 204, 255);
            Place(marker.Label.rectTransform, Vector2.zero, Vector2.one, new Vector2(11f, 0f), new Vector2(-11f, 1f));
            Markers.Add(marker);
        }
        return Markers[index];
    }

    private static Image GetLine(int index)
    {
        while (Lines.Count <= index)
        {
            Image line = NewImage(_root, "FrontLine", FrontColor);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            Lines.Add(line);
        }
        return Lines[index];
    }

    internal static void HostLateUpdate()
    {
        int counters = 0, lines = 0, markers = 0;
        bool rendered = false;
        try
        {
            if (_root != null && CorpsSystem.Enabled && World.world?.camera != null && Config.game_loaded &&
                !SmoothLoader.isLoading())
            {
                Render(out counters, out lines, out markers);
                rendered = true;
            }
        }
        catch (Exception)
        {
            // 绘制出错时本帧不画，下一帧再试
        }
        if (!rendered) ClearHover();
        for (int i = counters; i < Counters.Count; i++) Counters[i].Root.gameObject.SetActive(false);
        for (int i = lines; i < Lines.Count; i++) Lines[i].gameObject.SetActive(false);
        for (int i = markers; i < Markers.Count; i++) Markers[i].Root.gameObject.SetActive(false);
    }

    private static bool OnScreen(Vector3 screen, float margin) =>
        screen.z >= 0f && screen.x > -margin && screen.x < Screen.width + margin &&
        screen.y > -margin && screen.y < Screen.height + margin;

    private static void Render(out int counterCount, out int lineCount, out int markerCount)
    {
        Camera camera = World.world.camera;
        Canvas canvas = CanvasMain.instance.canvas_map_names;
        float scale = Mathf.Max(0.01f, canvas.scaleFactor);
        counterCount = lineCount = markerCount = 0;
        Hits.Clear();

        // 战线
        const int zoneSize = Config.CITY_ZONE_SIZE;
        foreach ((TileZone a, TileZone b) in FrontSystem.ContestedEdges())
        {
            Vector3 from, to;
            if (b == a.zone_right)
            {
                float x = (a.x + 1) * zoneSize - 0.5f;
                from = new Vector3(x, a.y * zoneSize - 0.5f);
                to = new Vector3(x, (a.y + 1) * zoneSize - 0.5f);
            }
            else
            {
                float y = (a.y + 1) * zoneSize - 0.5f;
                from = new Vector3(a.x * zoneSize - 0.5f, y);
                to = new Vector3((a.x + 1) * zoneSize - 0.5f, y);
            }
            Vector3 p1 = camera.WorldToScreenPoint(from), p2 = camera.WorldToScreenPoint(to);
            if (!OnScreen(p1, 40f) && !OnScreen(p2, 40f)) continue;
            Image line = GetLine(lineCount++);
            line.gameObject.SetActive(true);
            Vector2 delta = p2 - p1;
            RectTransform rect = line.rectTransform;
            rect.position = (p1 + p2) * 0.5f;
            rect.sizeDelta = new Vector2((delta.magnitude + LineWidth) / scale, LineWidth / scale);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        // 以屏幕像素定大小：抵消画布缩放，镜头拉远时最多缩到八成五、拉近时最多放大一成五
        float zoom = Mathf.Clamp(40f / Mathf.Max(1f, camera.orthographicSize), 0.85f, 1.15f);
        Vector3 pixelScale = Vector3.one * (zoom / scale);

        // 兵牌：同一地块同一国的军合并成一块，不同国并排
        Groups.Clear();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (!ShowsCorps(kingdom, corps)) continue;
            TileZone zone = CorpsSystem.Zone(corps);
            if (zone?.centerTile == null) continue;
            var key = (zone.id, kingdom.id);
            if (!Groups.TryGetValue(key, out CounterGroup group))
            {
                group = new CounterGroup { Kingdom = kingdom, Zone = zone, World = zone.centerTile.posV, Task = corps.task };
                Groups[key] = group;
            }
            group.Count++;
            group.Rank = Mathf.Max(group.Rank, Rank(corps));
            group.Strength += corps.strength;
            group.Full += corps.full;
            group.Org += corps.org;
            // 进攻优先显示，其次防守
            if (corps.task == "attack" || corps.task == "defend" && group.Task != "attack") group.Task = corps.task;
            Actor general = corps.general_id >= 0 ? World.world.units.get(corps.general_id) : null;
            if (group.Count == 1 && general?.data != null && !general.isRekt() && general.isAlive() &&
                general.current_tile?.zone != null && CorpsSystem.ZoneDistance(general.current_tile.zone, zone) <= 2f)
                group.World = (Vector3)general.current_position + new Vector3(0f, 3f);
        }
        StackOffsets.Clear();
        foreach (CounterGroup group in Groups.Values)
        {
            Vector3 screen = camera.WorldToScreenPoint(group.World);
            if (!OnScreen(screen, 80f)) continue;
            Counter counter = GetCounter(counterCount++);
            counter.Root.gameObject.SetActive(true);
            counter.Root.localScale = pixelScale;
            SetFrame(counter.Frame, TaskFrame(group.Task));
            counter.Piece.sprite = Piece(group.Rank);
            counter.Plate.color = group.Kingdom.getColor()?.getColorMain() ?? Color.gray;
            string label = group.Count > 1
                ? $"{CorpsSystem.ShortStrength(group.Strength)}<color=#F3C34A>×{group.Count}</color>"
                : CorpsSystem.ShortStrength(group.Strength);
            if (counter.Label.text != label)
            {
                counter.Label.text = label;
                counter.LabelWidth = counter.Label.preferredWidth;
            }
            // 字长时加宽，条随之拉长
            float width = Mathf.Max(CounterWidth, 29f + counter.LabelWidth);
            counter.Root.sizeDelta = new Vector2(width, CounterHeight);
            float barLength = width - 30f;
            float strength = group.Full <= 0f ? 0f : Mathf.Clamp01(group.Strength / group.Full);
            float org = Mathf.Clamp01(group.Org / group.Count / 100f);
            Place(counter.Strength.rectTransform, Vector2.zero, Vector2.zero, new Vector2(25f, 8f),
                new Vector2(25f + barLength * strength, 10f));
            Place(counter.Org.rectTransform, Vector2.zero, Vector2.zero, new Vector2(25f, 6f),
                new Vector2(25f + barLength * org, 8f));
            counter.Org.color = org < 0.3f ? OrgLowColor : OrgColor;
            // 同一地块的第二国、第三国往右错开
            StackOffsets.TryGetValue(group.Zone.id, out float offset);
            StackOffsets[group.Zone.id] = offset + width + 3f;
            Vector3 center = screen + new Vector3(offset * zoom, 0f);
            counter.Root.position = center;
            Hits.Add(new Hit
            {
                Kind = HoverCounter, Index = counterCount - 1, A = group.Zone.id, B = group.Kingdom.id,
                Center = center, Half = new Vector2(width, CounterHeight) * (0.5f * zoom)
            });
        }

        // 交战标记(画在交战地块下方，不压住兵牌)
        foreach (FrontSystem.BattleView battle in FrontSystem.Battles.Values)
        {
            TileZone zone = CorpsSystem.ZoneById(battle.zone_id);
            if (zone?.centerTile == null) continue;
            Vector3 screen = camera.WorldToScreenPoint(zone.centerTile.posV);
            if (!OnScreen(screen, 80f)) continue;
            Marker marker = GetMarker(markerCount++);
            marker.Root.gameObject.SetActive(true);
            marker.Root.localScale = pixelScale;
            SetFrame(marker.Frame, Frame("CorpsBattle"));
            string label = string.Format(LM.Get("corps_battle_marker"), Mathf.RoundToInt(battle.ratio * 100f));
            if (marker.Label.text != label)
            {
                marker.Label.text = label;
                marker.LabelWidth = marker.Label.preferredWidth;
            }
            float width = Mathf.Max(MarkerWidth, 26f + marker.LabelWidth);
            marker.Root.sizeDelta = new Vector2(width, MarkerHeight);
            marker.Attacker.color = KingdomColor(battle.attacker_id);
            marker.Defender.color = KingdomColor(battle.defender_id);
            Vector3 center = screen - new Vector3(0f, (CounterHeight + MarkerHeight) * 0.5f * zoom + 3f);
            marker.Root.position = center;
            Hits.Add(new Hit
            {
                Kind = HoverMarker, Index = markerCount - 1, A = battle.city_id,
                Center = center, Half = new Vector2(width, MarkerHeight) * (0.5f * zoom)
            });
        }

        UpdateHover(pixelScale);
    }

    private static Color KingdomColor(long id) =>
        World.world.kingdoms.get(id)?.getColor()?.getColorMain() ?? Color.gray;

    #region 悬停

    private static void ClearHover()
    {
        if (_hoverKind == HoverNone) return;
        _hoverKind = HoverNone;
        _hoverA = _hoverB = -1L;
        Tooltip.hideTooltip(null, true, string.Empty);
    }

    private static bool CanHover() =>
        InputHelpers.mouseSupported && !ScrollWindow.isWindowActive() && !ScrollWindow.isAnimationActive();

    // 按住鼠标(点击、拖地图)时不换悬停对象，只看还在不在原来那块上：松开那一帧还能认出点的是谁
    private static bool MouseHeld => Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

    // 鼠标压在界面面板上(地名画布以外的 UI)时不算悬停
    private static bool PointerOverPanels()
    {
        if (EventSystem.current == null) return false;
        _pointer ??= new PointerEventData(EventSystem.current);
        _pointer.position = Input.mousePosition;
        RaycastHits.Clear();
        EventSystem.current.RaycastAll(_pointer, RaycastHits);
        foreach (RaycastResult hit in RaycastHits)
            if (hit.gameObject != null && (_canvas == null || !hit.gameObject.transform.IsChildOf(_canvas.transform)))
                return true;
        return false;
    }

    private static void UpdateHover(Vector3 pixelScale)
    {
        int kind = HoverNone, index = -1;
        long a = -1L, b = -1L;
        if (CanHover())
        {
            bool held = MouseHeld;
            Vector2 mouse = Input.mousePosition;
            // 兵牌优先；后画的在上面。已悬停的那块按放大后的范围算，免得边缘来回闪
            for (int pass = HoverCounter; pass <= HoverMarker && kind == HoverNone; pass++)
            {
                for (int i = Hits.Count - 1; i >= 0; i--)
                {
                    Hit hit = Hits[i];
                    if (hit.Kind != pass) continue;
                    bool current = hit.Kind == _hoverKind && hit.A == _hoverA && hit.B == _hoverB;
                    if (held && !current) continue;
                    float grow = current ? HoverScale : 1f;
                    if (Mathf.Abs(mouse.x - hit.Center.x) > hit.Half.x * grow ||
                        Mathf.Abs(mouse.y - hit.Center.y) > hit.Half.y * grow) continue;
                    kind = hit.Kind;
                    index = hit.Index;
                    a = hit.A;
                    b = hit.B;
                    break;
                }
            }
            if (kind != HoverNone && PointerOverPanels()) kind = HoverNone;
        }
        if (kind == HoverNone)
        {
            ClearHover();
            return;
        }
        if (kind != _hoverKind || a != _hoverA || b != _hoverB)
        {
            _hoverKind = kind;
            _hoverA = a;
            _hoverB = b;
            _tipAt = float.NegativeInfinity;
        }
        RectTransform root;
        if (kind == HoverCounter)
        {
            Counter counter = Counters[index];
            SetFrame(counter.Frame, Frame("CorpsFrame_gold"));
            root = counter.Root;
        }
        else
        {
            Marker marker = Markers[index];
            SetFrame(marker.Frame, Frame("CorpsBattle_gold"));
            root = marker.Root;
        }
        root.localScale = pixelScale * HoverScale;
        root.SetAsLastSibling();

        float now = Time.unscaledTime;
        if (MouseHeld || now - _tipAt < TooltipRefresh) return;
        _tipAt = now;
        if (kind == HoverCounter)
            Tooltip.show(CounterTipOwner, CounterTooltip, new TooltipData
            {
                kingdom = World.world.kingdoms.get(b), tooltip_scale = 0.7f, is_sim_tooltip = true, sound_allowed = false
            });
        else
            Tooltip.show(BattleTipOwner, BattleTooltip, new TooltipData
            {
                city = World.world.cities.get(a), tooltip_scale = 0.7f, is_sim_tooltip = true, sound_allowed = false
            });
    }

    // 点击(原版"点一下查看"的时机，见 CorpsPatch)：打开悬停的兵牌或交战所属国的军事页
    public static bool TryClick()
    {
        Kingdom kingdom;
        List<long> corps = null;
        long city = -1L;
        if (_hoverKind == HoverCounter)
        {
            kingdom = World.world.kingdoms.get(_hoverB);
            if (kingdom?.data == null || kingdom.isRekt()) return false;
            corps = CorpsSystem.Of(kingdom).Where(entry => entry.zone_id == _hoverA && ShowsCorps(kingdom, entry))
                .Select(entry => entry.id).ToList();
        }
        else if (_hoverKind == HoverMarker && FrontSystem.Battles.TryGetValue(_hoverA, out var battle))
        {
            Kingdom attacker = World.world.kingdoms.get(battle.attacker_id);
            Kingdom defender = World.world.kingdoms.get(battle.defender_id);
            bool attackerAlive = attacker?.data != null && !attacker.isRekt();
            bool defenderAlive = defender?.data != null && !defender.isRekt();
            // 攻方优先；攻方不在帝国里而守方在，就开守方帝国的军团页
            kingdom = attackerAlive && (attacker.GetEmpire() != null || !defenderAlive || defender.GetEmpire() == null)
                ? attacker : defenderAlive ? defender : null;
            if (kingdom == null) return false;
            city = battle.city_id;
        }
        else return false;
        ClearHover();
        Empire empire = kingdom.GetEmpire();
        if (empire?.CoreKingdom != null && !empire.isRekt()) EmpireWindow.OpenMilitary(empire, corps, city);
        else KingdomMilitaryWindow.Open(kingdom, corps, city);
        return true;
    }

    #endregion

    #region 详情提示

    private static string TaskLine(CorpsData corps)
    {
        string text = LM.Get("corps_task_" + corps.task);
        City sector = corps.sector_city_id >= 0 ? World.world.cities.get(corps.sector_city_id) : null;
        if (sector?.data != null && !sector.isRekt()) text += " · " + sector.GetCityName();
        return text;
    }

    private static string TaskHex(string task) => task switch
    {
        "attack" => "#FFB3A8",
        "defend" => "#A8CCFF",
        "march" => "#E6DCA8",
        "retreat" => "#E8584F",
        _ => "#C9E6A8"
    };

    // 兵牌：所属国与番号、任务，合计兵力、平均组织度、所在，单支时加统领、驻地，几支时逐支列出
    public static void ShowCounterTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        Kingdom kingdom = data?.kingdom;
        if (kingdom?.data == null || kingdom.isRekt() || _hoverKind != HoverCounter) return;
        List<CorpsData> list = CorpsSystem.Of(kingdom)
            .Where(corps => corps.zone_id == _hoverA && ShowsCorps(kingdom, corps))
            .OrderByDescending(Rank).ThenByDescending(corps => corps.strength).ToList();
        if (list.Count == 0) return;
        tooltip.clear();
        string colorHex = kingdom.getColor()?.color_text ?? "#FFFFFF";
        string task = list.Any(corps => corps.task == "attack") ? "attack"
            : list.Any(corps => corps.task == "defend") ? "defend" : list[0].task;
        string title = list.Count == 1 ? CorpsSystem.Name(list[0]) : string.Format(LM.Get("corps_chip_count"), list.Count);
        tooltip.setTitle($"{kingdom.GetKingdomName()} · {title}", "corps_task_" + task, colorHex);

        float strength = list.Sum(corps => corps.strength), full = list.Sum(corps => corps.full);
        float org = list.Average(corps => corps.org);
        tooltip.addLineText(LM.Get("corps_col_strength"),
            $"{CorpsSystem.ShortStrength(strength)}/{CorpsSystem.ShortStrength(full)}", "#65D66E",
            pPercent: false, pLocalize: false, pLimitValue: 40);
        tooltip.addLineText(LM.Get("corps_col_org"), Mathf.RoundToInt(org).ToString(), org < 30f ? "#E8584F" : "#7FD8EA",
            pPercent: false, pLocalize: false);
        TileZone zone = CorpsSystem.ZoneById((int)_hoverA);
        City at = zone?.city;
        if (at?.data != null && !at.isRekt())
            tooltip.addLineText(LM.Get("corps_tip_location"), at.GetCityName(), "#C8C2B4", pPercent: false,
                pLocalize: false, pLimitValue: 40);

        if (list.Count == 1)
        {
            CorpsData corps = list[0];
            tooltip.addLineText(LM.Get("corps_col_task"), TaskLine(corps), TaskHex(corps.task), pPercent: false,
                pLocalize: false, pLimitValue: 40);
            Actor general = corps.general_id >= 0 ? World.world.units.get(corps.general_id) : null;
            if (general?.data != null && !general.isRekt())
                tooltip.addLineText(LM.Get("corps_tip_general"), general.getName(), "#F3C34A", pPercent: false,
                    pLocalize: false, pLimitValue: 40);
            City home = CorpsSystem.Home(corps);
            if (home != null)
                tooltip.addLineText(LM.Get("corps_col_home"), home.GetCityName(), "#C8C2B4", pPercent: false,
                    pLocalize: false, pLimitValue: 40);
            if (corps.entrench > 0.01f)
                tooltip.addLineText(LM.Get("corps_tip_entrench"), $"+{Mathf.RoundToInt(corps.entrench * 100f)}%",
                    "#A8CCFF", pPercent: false, pLocalize: false);
            tooltip.addBottomDescription(LM.Get("corps_tip_click"));
            return;
        }

        const int shown = 8;
        tooltip.addLineBreak();
        foreach (CorpsData corps in list.Take(shown))
        {
            string name = CorpsSystem.Name(corps);
            Actor general = corps.general_id >= 0 ? World.world.units.get(corps.general_id) : null;
            if (general?.data != null && !general.isRekt()) name += " · " + general.getName();
            string value = $"{CorpsSystem.ShortStrength(corps.strength)} · {Mathf.RoundToInt(corps.org)} · " +
                           LM.Get("corps_task_" + corps.task);
            tooltip.addLineText(name, value, TaskHex(corps.task), pPercent: false, pLocalize: false, pLimitValue: 40);
        }
        tooltip.addBottomDescription((list.Count > shown
            ? string.Format(LM.Get("corps_tip_more"), list.Count - shown) + "\n"
            : "") + LM.Get("corps_tip_click"));
    }

    // 交战：攻守双方、攻方胜算、兵力对比、参战军数、已占地块
    public static void ShowBattleTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        if (city?.data == null || city.isRekt() || !FrontSystem.Battles.TryGetValue(city.id, out var battle)) return;
        tooltip.clear();
        Kingdom attacker = World.world.kingdoms.get(battle.attacker_id);
        Kingdom defender = World.world.kingdoms.get(battle.defender_id);
        tooltip.setTitle(city.GetCityName(), "corps_tip_battle", "#FFB3A8");
        tooltip.addLineText(LM.Get("corps_tip_attacker"), attacker?.GetKingdomName() ?? "?",
            attacker?.getColor()?.color_text ?? "#FFB3A8", pPercent: false, pLocalize: false, pLimitValue: 40);
        tooltip.addLineText(LM.Get("corps_tip_defender"), defender?.GetKingdomName() ?? "?",
            defender?.getColor()?.color_text ?? "#A8CCFF", pPercent: false, pLocalize: false, pLimitValue: 40);
        int odds = Mathf.RoundToInt(battle.ratio * 100f);
        tooltip.addLineText(LM.Get("corps_tip_odds"), odds + "%", odds >= 50 ? "#FFB3A8" : "#A8CCFF",
            pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("corps_col_strength"),
            string.Format(LM.Get("corps_tip_versus"), CorpsSystem.ShortStrength(battle.attack),
                CorpsSystem.ShortStrength(battle.defend)), "#E6DCA8", pPercent: false, pLocalize: false, pLimitValue: 40);
        int attacking = attacker == null ? 0
            : CorpsSystem.Of(attacker).Count(corps => corps.sector_city_id == city.id && corps.task == "attack");
        int defending = defender == null ? 0
            : CorpsSystem.Of(defender).Count(corps => corps.sector_city_id == city.id && corps.task == "defend");
        tooltip.addLineText(LM.Get("corps_tip_engaged"), string.Format(LM.Get("corps_tip_versus"), attacking, defending),
            "#E6DCA8", pPercent: false, pLocalize: false, pLimitValue: 40);
        int occupied = city.zones?.Count(zone => FrontSystem.Hostile(city.kingdom, FrontSystem.Controller(zone))) ?? 0;
        tooltip.addLineText(LM.Get("corps_tip_occupied"), $"{occupied}/{city.zones?.Count ?? 0}", "#C8C2B4",
            pPercent: false, pLocalize: false);
        tooltip.addBottomDescription(LM.Get("corps_tip_battle_hint") + "\n" + LM.Get("corps_tip_click"));
    }

    #endregion
}

[DefaultExecutionOrder(10001)]
public sealed class CorpsMapRendererHost : MonoBehaviour
{
    private void LateUpdate() => CorpsMapRenderer.HostLateUpdate();
}
