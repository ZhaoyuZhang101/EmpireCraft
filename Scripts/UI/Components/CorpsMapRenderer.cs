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
//   · 战线：交战双方控制区相接的地块边，画成朱红粗线(金色留给帝国、周室的外围边框)；
//   · 兵牌：帝国卷册小框(Tools/IconForge/frame_forge.py 的 CorpsFrame_*)，边色表示任务——
//     进攻朱漆、防守石青、溃退铁灰、驻守橄榄青铜；左边国色印面上一枚棋子(中央禁军为后、军区为车、
//     军府为马、义军为兵)，右边兵力与兵力条(绿)、组织度条(青)；有统领实体时跟在统领头顶；
//     同一地块同一国的几支军合并成一块(标"×N"、兵力相加)，不同国并排；镜头拉远、同一国的兵牌在屏幕上挤在一起时
//     再折叠成一块(拉近分开后各自显示)，悬停详情与点击打开军事页都包括折进来的全部军；
//     镜头越远并得越宽：近处只并同一国、在屏幕上互相压住的；拉远后范围放宽到约三块兵牌、行军途中的也并进去；
//     再远，互不交战的各国并成一块(兵力最大那国的颜色，×N 是合计几支，悬停逐支列出)，敌对的始终分开；舰队同理。拉近后各自分开；
//     行军不再一格一格跳：每支军、每支舰队记着自己画在哪，换了地块就在一个游戏秒(一次行军结算)里匀速滑过去，
//     暂停时不动；途中的兵牌单独画在上层(同路同步的几支并成一块)，到位后再并进所在地块的那一块；
//   · 交战标记：正在交战的城，朱漆框绛红底，两侧是攻守双方国色，中间是本月攻方胜算；
//   · 舰队兵牌(见 NavySystem)：画在海上，船形图标按舰种(楼船/帆舰/铁甲/现代)，数字是战舰数(+运输船)，
//     条是战舰编制与组织度；海战标记同交战标记，写本月胜算；
//   · 渡海中的军团(运兵船)：海青边、深海底的框，印面是本军棋子站在船上，与舰队兵牌一眼分开；
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

    private const int HoverNone = 0, HoverCounter = 1, HoverMarker = 2, HoverFleet = 3, HoverNaval = 4;

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
    private static readonly object FleetTipOwner = new();
    private static readonly object NavalTipOwner = new();
    private static PointerEventData _pointer;
    private static readonly List<RaycastResult> RaycastHits = new();

    public const string CounterTooltip = "empirecraft_corps_counter";
    public const string BattleTooltip = "empirecraft_corps_battle";
    public const string FleetTooltip = "empirecraft_navy_fleet";
    public const string NavalTooltip = "empirecraft_navy_battle";

    // 悬停兵牌时原版地图提示让路(见 CorpsPatch)
    public static bool Hovering => _hoverKind != HoverNone;

    private sealed class CounterGroup
    {
        public Kingdom Kingdom;
        public TileZone Zone;
        public Vector3 World;
        public Vector3 Screen;
        public string Task;
        public int Count, Rank;
        public float Strength, Full, Org;
        // 行军途中(还没滑到所在地块)：镜头近时不与别的兵牌折叠，不按地块错开
        public bool Moving;
        public readonly List<int> Zones = new();
        // 并进来的各国(第一个是兵力最大、颜色所用的那国)
        public readonly List<Kingdom> Realms = new();
        // 最差的补给(断绝时灰边)；有军在船上(换船形图标)
        public float MinSupply = 1f;
        public bool Embarked;
    }

    private sealed class FleetGroup
    {
        public Kingdom Kingdom;
        public TileZone Zone;
        public FleetData Lead;
        public Vector3 World, Screen;
        public int Warships, Transports, Count;
        public float Org;
        public bool Sailing;
        public string Task;
    }

    private static readonly List<FleetGroup> FleetGroups = new();
    private static readonly List<FleetGroup> FoldedFleets = new();
    // 悬停舰队时：这块合并了几支舰队(键：领头舰队)
    private static readonly Dictionary<long, int> FoldedFleetCounts = new();
    // 悬停兵牌时：这块并进来哪些国(键：代表地块 + 领头国)
    private static readonly Dictionary<(long zone, long kingdom), List<Kingdom>> FoldedRealms = new();

    // 一块地块在屏幕上不小于这么多像素时算"近"(只并互相压住的同国兵牌)，不大于后者时算"远"
    private const float NearZonePixels = 40f, FarZonePixels = 12f;
    // 远近程度到这里时行军途中的也并、互不交战的各国也并
    private const float FoldMoversFar = 0.3f, MixRealmsFar = 0.45f;

    // 镜头远近：0 近、1 远(按一块地块在屏幕上的像素宽，不随分辨率与界面缩放变)
    private static float FarFactor(Camera camera)
    {
        float pixels = Mathf.Abs(camera.WorldToScreenPoint(new Vector3(Config.CITY_ZONE_SIZE, 0f)).x -
                                 camera.WorldToScreenPoint(Vector3.zero).x);
        return Mathf.InverseLerp(NearZonePixels, FarZonePixels, pixels);
    }

    private static bool Mergeable(Kingdom a, Kingdom b, bool mixRealms) =>
        a == b || mixRealms && a != null && b != null && !FrontSystem.Hostile(a, b);

    // 兵牌画在哪：换了目标就定一个速度，在 GlideSeconds 个游戏秒里匀速滑过去
    private struct Glider
    {
        public Vector3 At, Target;
        public float Speed;
    }

    // 军团每游戏秒行军一次(见 CorpsSystem.SecondTick)，舰队同样
    private const float GlideSeconds = 1f;
    // 慢于这个速度(格/游戏秒)就太拖沓了
    private const float MinGlideSpeed = 4f;
    // 一下子挪得太远(新征召、读档、跨海直接到位)就直接放过去，不滑
    private const float SnapDistance = 160f;
    // 途中几支军相距这么近(格)就并成一块画
    private const float MoverMerge = 1.5f;

    private static Dictionary<long, Glider> _corpsGliders = new(), _nextCorpsGliders = new();
    private static Dictionary<long, Glider> _fleetGliders = new(), _nextFleetGliders = new();
    private static readonly List<CounterGroup> Movers = new();
    private static double _lastWorldTime = -1d;
    private static MapBox _glideWorld;

    private static float WorldDelta()
    {
        if (!ReferenceEquals(_glideWorld, World.world))
        {
            _glideWorld = World.world;
            _corpsGliders.Clear();
            _fleetGliders.Clear();
            _lastWorldTime = -1d;
        }
        double now = World.world.getCurWorldTime();
        double delta = _lastWorldTime < 0d || now < _lastWorldTime ? 0d : now - _lastWorldTime;
        _lastWorldTime = now;
        return (float)Math.Min(delta, 5d);
    }

    private static Glider Glide(Dictionary<long, Glider> gliders, long id, Vector3 target, float delta)
    {
        if (!gliders.TryGetValue(id, out Glider glider) || (target - glider.At).sqrMagnitude > SnapDistance * SnapDistance)
            return new Glider { At = target, Target = target };
        if (glider.Target != target)
        {
            glider.Target = target;
            glider.Speed = Mathf.Max(MinGlideSpeed, (target - glider.At).magnitude / GlideSeconds);
        }
        glider.At = Vector3.MoveTowards(glider.At, target, glider.Speed * delta);
        return glider;
    }

    private static void Add(CounterGroup group, CorpsData corps, TileZone zone)
    {
        group.Count++;
        group.Rank = Mathf.Max(group.Rank, Rank(corps));
        group.Strength += corps.strength;
        group.Full += corps.full;
        group.Org += corps.org;
        // 进攻优先显示，其次防守
        if (corps.task == "attack" || corps.task == "defend" && group.Task != "attack") group.Task = corps.task;
        if (group.Moving && !group.Zones.Contains(zone.id)) group.Zones.Add(zone.id);
        group.MinSupply = Mathf.Min(group.MinSupply, corps.supply);
        if (corps.embarked) group.Embarked = true;
    }

    // 航线虚线
    private const float DashLength = 7f, DashGap = 6f, RouteWidth = 2f;
    private const int MaxDashes = 600;

    private static void DrawDashes(Vector3 from, Vector3 to, Color color, ref int lineCount, float scale)
    {
        if (!OnScreen(from, 200f) && !OnScreen(to, 200f)) return;
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 1f) return;
        Vector2 direction = delta / length;
        Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        for (float travelled = 0f; travelled < length && lineCount < MaxDashes; travelled += DashLength + DashGap)
        {
            float dash = Mathf.Min(DashLength, length - travelled);
            Image line = GetLine(lineCount++);
            line.gameObject.SetActive(true);
            line.color = color;
            RectTransform rect = line.rectTransform;
            rect.position = from + (Vector3)(direction * (travelled + dash * 0.5f));
            rect.sizeDelta = new Vector2(dash / scale, RouteWidth / scale);
            rect.localRotation = rotation;
        }
    }

    // 折叠后每块兵牌包括哪些地块(键：代表地块 + 国)，悬停详情与点击用
    private static readonly Dictionary<(long zone, long kingdom), List<int>> FoldedZones = new();
    private static readonly List<CounterGroup> Folded = new();

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

    private static readonly Sprite[] Troopships = new Sprite[4];

    // 运兵船：本军棋子站在船上
    private static Sprite Troopship(int rank)
    {
        rank = Mathf.Clamp(rank, 0, 3);
        return Troopships[rank] ??= SpriteTextureLoader.getSprite("ui/icons/corps/troopship_" + PieceNames[rank]) ?? Piece(rank);
    }

    private static readonly Sprite[] Ships = new Sprite[4];

    private static Sprite Ship(Kingdom kingdom)
    {
        float tier = NavySystem.Tier(kingdom);
        int index = tier >= 4f ? 3 : tier >= 2.5f ? 2 : tier >= 1.5f ? 1 : 0;
        return Ships[index] ??= SpriteTextureLoader.getSprite("ui/icons/corps/ship_" + index) ?? Piece(0);
    }

    private static Sprite FleetFrame(string task) => Frame(task switch
    {
        "escort" or "blockade" => "CorpsFrame_lacquer",
        "patrol" => "CorpsFrame_azure",
        "retreat" => "CorpsFrame_ash",
        _ => "CorpsFrame_olive"
    });

    private static readonly Dictionary<(int zone, long kingdom), CounterGroup> Groups = new();

    private static readonly Color PieceColor = new Color32(240, 232, 210, 255);
    private static readonly Color InkColor = new Color32(20, 22, 15, 255);
    private static readonly Color LabelColor = new Color32(236, 226, 198, 255);
    private static readonly Color StrengthColor = new Color32(101, 214, 110, 255);
    private static readonly Color OrgColor = new Color32(127, 216, 234, 255);
    private static readonly Color OrgLowColor = new Color32(232, 88, 79, 255);
    // 战线朱红：帝国、周室外围边框是金色，战线不能同色
    private static readonly Color FrontColor = new Color32(226, 64, 52, 235);

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
            line.color = FrontColor;
            Vector2 delta = p2 - p1;
            RectTransform rect = line.rectTransform;
            rect.position = (p1 + p2) * 0.5f;
            rect.sizeDelta = new Vector2((delta.magnitude + LineWidth) / scale, LineWidth / scale);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        // 跨海运兵的航线：从所在海区经剩下的海区到登陆点，画成本国颜色的虚线(同一支船队只画一次)
        var drawnRoutes = new HashSet<(long kingdom, int step, int first, int last)>();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (!corps.embarked || corps.sea_route == null || corps.sea_route.Count == 0) continue;
            if (!drawnRoutes.Add((kingdom.id, corps.sea_step, corps.sea_route[0], corps.sea_route[corps.sea_route.Count - 1])))
                continue;
            Color routeColor = kingdom.getColor()?.getColorMain() ?? Color.white;
            routeColor.a = 0.9f;
            Vector3? previous = null;
            for (int i = Mathf.Max(0, corps.sea_step); i <= corps.sea_route.Count; i++)
            {
                TileZone stop = i < corps.sea_route.Count ? SeaRegionSystem.Get(corps.sea_route[i])?.Center : FrontSystem.TargetZone(corps);
                if (stop?.centerTile == null) continue;
                Vector3 point = camera.WorldToScreenPoint(stop.centerTile.posV);
                if (previous is Vector3 from) DrawDashes(from, point, routeColor, ref lineCount, scale);
                previous = point;
            }
        }

        // 以屏幕像素定大小：抵消画布缩放，镜头拉远时最多缩到八成五、拉近时最多放大一成五
        float zoom = Mathf.Clamp(40f / Mathf.Max(1f, camera.orthographicSize), 0.85f, 1.15f);
        Vector3 pixelScale = Vector3.one * (zoom / scale);

        // 兵牌：同一地块同一国、已经到位的军合并成一块，不同国并排；行军途中的按画的位置滑过去
        float glideDelta = WorldDelta();
        Groups.Clear();
        Movers.Clear();
        _nextCorpsGliders.Clear();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (!ShowsCorps(kingdom, corps)) continue;
            TileZone zone = CorpsSystem.Zone(corps);
            if (zone?.centerTile == null) continue;
            // 有统领实体时跟在统领头顶
            Actor general = corps.general_id >= 0 ? World.world.units.get(corps.general_id) : null;
            bool follows = general?.data != null && !general.isRekt() && general.isAlive() &&
                           general.current_tile?.zone != null && CorpsSystem.ZoneDistance(general.current_tile.zone, zone) <= 2f;
            Vector3 target = follows ? (Vector3)general.current_position + new Vector3(0f, 3f) : zone.centerTile.posV;
            // 统领实体自己在走，兵牌直接跟着，不再另外滑
            Glider glider = follows ? new Glider { At = target, Target = target } : Glide(_corpsGliders, corps.id, target, glideDelta);
            _nextCorpsGliders[corps.id] = glider;
            CounterGroup group;
            if (follows || glider.At == target)
            {
                var key = (zone.id, kingdom.id);
                if (!Groups.TryGetValue(key, out group))
                {
                    group = new CounterGroup { Kingdom = kingdom, Zone = zone, World = glider.At, Task = corps.task };
                    Groups[key] = group;
                }
            }
            else
            {
                group = null;
                foreach (CounterGroup mover in Movers)
                    if (mover.Kingdom == kingdom && (mover.World - glider.At).sqrMagnitude < MoverMerge * MoverMerge)
                    {
                        group = mover;
                        break;
                    }
                if (group == null)
                {
                    group = new CounterGroup { Kingdom = kingdom, Zone = zone, World = glider.At, Task = corps.task, Moving = true };
                    Movers.Add(group);
                }
            }
            Add(group, corps, zone);
        }
        (_corpsGliders, _nextCorpsGliders) = (_nextCorpsGliders, _corpsGliders);
        // 折叠(先放兵力大的，挨着它的并进去)：镜头越远并得越宽，拉近后各自分开——
        // 近处只并同一国、在屏幕上互相压住的；拉远后范围放宽到约三块兵牌，行军途中的也并进去；
        // 再远，互不交战的各国并成一块(兵力最大那国的颜色)，敌对的始终分开；船上的(运兵船)不与岸上的并
        float far = FarFactor(camera);
        bool foldMovers = far >= FoldMoversFar, mixRealms = far >= MixRealmsFar;
        float foldX = CounterWidth * zoom * Mathf.Lerp(0.9f, 3f, far), foldY = CounterHeight * zoom * Mathf.Lerp(1.3f, 4.5f, far);
        Folded.Clear();
        FoldedZones.Clear();
        FoldedRealms.Clear();
        IEnumerable<CounterGroup> candidates = foldMovers ? Groups.Values.Concat(Movers) : Groups.Values;
        foreach (CounterGroup group in candidates.OrderByDescending(group => group.Strength).ToList())
        {
            group.Screen = camera.WorldToScreenPoint(group.World);
            if (!OnScreen(group.Screen, 80f)) continue;
            CounterGroup into = null;
            foreach (CounterGroup other in Folded)
                if (other.Embarked == group.Embarked && Mergeable(other.Kingdom, group.Kingdom, mixRealms) &&
                    Mathf.Abs(other.Screen.x - group.Screen.x) < foldX &&
                    Mathf.Abs(other.Screen.y - group.Screen.y) < foldY)
                {
                    into = other;
                    break;
                }
            if (into == null)
            {
                // 途中的兵牌已记下所经地块；到位的只算自己那块
                if (!group.Moving)
                {
                    group.Zones.Clear();
                    group.Zones.Add(group.Zone.id);
                }
                group.Realms.Clear();
                group.Realms.Add(group.Kingdom);
                Folded.Add(group);
                continue;
            }
            into.Count += group.Count;
            into.Rank = Mathf.Max(into.Rank, group.Rank);
            into.Strength += group.Strength;
            into.Full += group.Full;
            into.Org += group.Org;
            if (group.Task == "attack" || group.Task == "defend" && into.Task != "attack") into.Task = group.Task;
            if (group.Moving)
            {
                foreach (int zoneId in group.Zones)
                    if (!into.Zones.Contains(zoneId)) into.Zones.Add(zoneId);
            }
            else if (!into.Zones.Contains(group.Zone.id)) into.Zones.Add(group.Zone.id);
            if (!into.Realms.Contains(group.Kingdom)) into.Realms.Add(group.Kingdom);
            into.MinSupply = Mathf.Min(into.MinSupply, group.MinSupply);
            into.Embarked |= group.Embarked;
        }
        foreach (CounterGroup group in Folded)
        {
            FoldedZones[(group.Zone.id, group.Kingdom.id)] = group.Zones;
            FoldedRealms[(group.Zone.id, group.Kingdom.id)] = group.Realms;
        }
        // 镜头近时途中的兵牌画在已到位的上面，不折叠
        if (!foldMovers)
            foreach (CounterGroup group in Movers)
            {
                group.Screen = camera.WorldToScreenPoint(group.World);
                if (!OnScreen(group.Screen, 80f)) continue;
                Folded.Add(group);
                var key = (group.Zone.id, group.Kingdom.id);
                if (!FoldedZones.ContainsKey(key)) FoldedZones[key] = group.Zones;
            }

        StackOffsets.Clear();
        foreach (CounterGroup group in Folded)
        {
            Vector3 screen = group.Screen;
            Counter counter = GetCounter(counterCount++);
            counter.Root.gameObject.SetActive(true);
            counter.Root.localScale = pixelScale;
            // 在船上(运兵船)换海青框、棋子站在船上；否则补给断绝换灰边
            SetFrame(counter.Frame, group.Embarked ? Frame("CorpsFrame_sea")
                : group.MinSupply < 0.25f ? Frame("CorpsFrame_ash") : TaskFrame(group.Task));
            counter.Piece.sprite = group.Embarked ? Troopship(group.Rank) : Piece(group.Rank);
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
            // 同一地块的第二国、第三国往右错开(途中的不错开)
            float offset = 0f;
            if (!group.Moving)
            {
                StackOffsets.TryGetValue(group.Zone.id, out offset);
                StackOffsets[group.Zone.id] = offset + width + 3f;
            }
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

        // 舰队兵牌(折叠规则同上)
        _nextFleetGliders.Clear();
        FleetGroups.Clear();
        foreach ((Kingdom kingdom, FleetData fleet) in NavySystem.All())
        {
            if (fleet.warships + fleet.transports <= 0) continue;
            TileZone zone = CorpsSystem.ZoneById(fleet.zone_id);
            if (zone?.centerTile == null) continue;
            Glider glider = Glide(_fleetGliders, fleet.id, zone.centerTile.posV, glideDelta);
            _nextFleetGliders[fleet.id] = glider;
            FleetGroups.Add(new FleetGroup
            {
                Kingdom = kingdom, Zone = zone, Lead = fleet, World = glider.At, Warships = fleet.warships,
                Transports = fleet.transports, Count = 1, Org = fleet.org, Sailing = glider.At != glider.Target, Task = fleet.task
            });
        }
        (_fleetGliders, _nextFleetGliders) = (_nextFleetGliders, _fleetGliders);
        FoldedFleets.Clear();
        FoldedFleetCounts.Clear();
        foreach (FleetGroup group in FleetGroups.OrderByDescending(group => group.Warships + group.Transports))
        {
            group.Screen = camera.WorldToScreenPoint(group.World);
            if (!OnScreen(group.Screen, 80f)) continue;
            FleetGroup into = null;
            if (foldMovers || !group.Sailing)
                foreach (FleetGroup other in FoldedFleets)
                    if ((foldMovers || !other.Sailing) && Mergeable(other.Kingdom, group.Kingdom, mixRealms) &&
                        Mathf.Abs(other.Screen.x - group.Screen.x) < foldX && Mathf.Abs(other.Screen.y - group.Screen.y) < foldY)
                    {
                        into = other;
                        break;
                    }
            if (into == null)
            {
                FoldedFleets.Add(group);
                continue;
            }
            into.Warships += group.Warships;
            into.Transports += group.Transports;
            into.Count += group.Count;
            into.Org += group.Org;
        }
        foreach (FleetGroup group in FoldedFleets)
        {
            FoldedFleetCounts[group.Lead.id] = group.Count;
            Kingdom kingdom = group.Kingdom;
            TileZone zone = group.Zone;
            bool sailing = group.Sailing;
            Vector3 screen = group.Screen;
            Counter counter = GetCounter(counterCount++);
            counter.Root.gameObject.SetActive(true);
            counter.Root.localScale = pixelScale;
            SetFrame(counter.Frame, FleetFrame(group.Task));
            counter.Piece.sprite = Ship(kingdom);
            counter.Plate.color = kingdom.getColor()?.getColorMain() ?? Color.gray;
            string label = (group.Transports > 0
                ? $"{group.Warships}<color=#C8C2B4>+{group.Transports}</color>"
                : group.Warships.ToString()) + (group.Count > 1 ? $"<color=#F3C34A>×{group.Count}</color>" : "");
            if (counter.Label.text != label)
            {
                counter.Label.text = label;
                counter.LabelWidth = counter.Label.preferredWidth;
            }
            float width = Mathf.Max(CounterWidth, 29f + counter.LabelWidth);
            counter.Root.sizeDelta = new Vector2(width, CounterHeight);
            float barLength = width - 30f;
            float ships = Mathf.Clamp01(group.Warships / (6f * group.Count));
            float org = Mathf.Clamp01(group.Org / group.Count / 100f);
            Place(counter.Strength.rectTransform, Vector2.zero, Vector2.zero, new Vector2(25f, 8f),
                new Vector2(25f + barLength * ships, 10f));
            Place(counter.Org.rectTransform, Vector2.zero, Vector2.zero, new Vector2(25f, 6f),
                new Vector2(25f + barLength * org, 8f));
            counter.Org.color = org < 0.3f ? OrgLowColor : OrgColor;
            float offset = 0f;
            if (!sailing)
            {
                StackOffsets.TryGetValue(zone.id, out offset);
                StackOffsets[zone.id] = offset + width + 3f;
            }
            Vector3 center = screen + new Vector3(offset * zoom, 0f);
            counter.Root.position = center;
            Hits.Add(new Hit
            {
                Kind = HoverFleet, Index = counterCount - 1, A = group.Lead.id, B = kingdom.id,
                Center = center, Half = new Vector2(width, CounterHeight) * (0.5f * zoom)
            });
        }

        // 海战标记
        foreach (NavySystem.BattleView battle in NavySystem.Battles.Values)
        {
            TileZone zone = CorpsSystem.ZoneById(battle.zone_id);
            if (zone?.centerTile == null) continue;
            Vector3 screen = camera.WorldToScreenPoint(zone.centerTile.posV);
            if (!OnScreen(screen, 80f)) continue;
            Marker marker = GetMarker(markerCount++);
            marker.Root.gameObject.SetActive(true);
            marker.Root.localScale = pixelScale;
            SetFrame(marker.Frame, Frame("CorpsBattle"));
            string label = string.Format(LM.Get("navy_battle_marker"), Mathf.RoundToInt(battle.ratio * 100f));
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
                Kind = HoverNaval, Index = markerCount - 1, A = battle.region_id,
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

    // 悬停的兵牌折叠了哪些地块(没折叠就是它自己那块)
    private static List<int> HoverZones(long kingdomId) =>
        FoldedZones.TryGetValue((_hoverA, kingdomId), out List<int> zones) ? zones : new List<int> { (int)_hoverA };

    // 悬停的兵牌并进来哪些国(镜头远时互不交战的各国并成一块)
    private static List<Kingdom> HoverRealms(Kingdom kingdom) =>
        FoldedRealms.TryGetValue((_hoverA, kingdom.id), out List<Kingdom> realms) && realms.Count > 0
            ? realms
            : new List<Kingdom> { kingdom };

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
            for (int pass = HoverCounter; pass <= HoverNaval && kind == HoverNone; pass++)
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
        if (kind is HoverCounter or HoverFleet)
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
        else if (kind == HoverFleet)
            Tooltip.show(FleetTipOwner, FleetTooltip, new TooltipData
            {
                kingdom = World.world.kingdoms.get(b), tooltip_scale = 0.7f, is_sim_tooltip = true, sound_allowed = false
            });
        else if (kind == HoverNaval)
            Tooltip.show(NavalTipOwner, NavalTooltip, new TooltipData
            {
                tooltip_scale = 0.7f, is_sim_tooltip = true, sound_allowed = false
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
            List<int> zones = HoverZones(kingdom.id);
            corps = CorpsSystem.Of(kingdom).Where(entry => zones.Contains(entry.zone_id) && ShowsCorps(kingdom, entry))
                .Select(entry => entry.id).ToList();
        }
        else if (_hoverKind == HoverFleet)
        {
            kingdom = World.world.kingdoms.get(_hoverB);
            if (kingdom?.data == null || kingdom.isRekt()) return false;
        }
        else if (_hoverKind == HoverNaval && NavySystem.Battles.TryGetValue((int)_hoverA, out var naval))
        {
            kingdom = PickSide(naval.attacker_id, naval.defender_id);
            if (kingdom == null) return false;
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

    // 攻方优先；攻方不在帝国里而守方在，就开守方帝国的军事页
    private static Kingdom PickSide(long attackerId, long defenderId)
    {
        Kingdom attacker = World.world.kingdoms.get(attackerId);
        Kingdom defender = World.world.kingdoms.get(defenderId);
        bool attackerAlive = attacker?.data != null && !attacker.isRekt();
        bool defenderAlive = defender?.data != null && !defender.isRekt();
        return attackerAlive && (attacker.GetEmpire() != null || !defenderAlive || defender.GetEmpire() == null)
            ? attacker : defenderAlive ? defender : null;
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
        List<int> zones = HoverZones(kingdom.id);
        List<Kingdom> realms = HoverRealms(kingdom);
        var owners = new Dictionary<CorpsData, Kingdom>();
        foreach (Kingdom owner in realms)
        {
            if (owner?.data == null || owner.isRekt()) continue;
            foreach (CorpsData corps in CorpsSystem.Of(owner))
                if (zones.Contains(corps.zone_id) && ShowsCorps(owner, corps)) owners[corps] = owner;
        }
        List<CorpsData> list = owners.Keys.OrderByDescending(Rank).ThenByDescending(corps => corps.strength).ToList();
        if (list.Count == 0) return;
        tooltip.clear();
        string colorHex = kingdom.getColor()?.color_text ?? "#FFFFFF";
        string task = list.Any(corps => corps.task == "attack") ? "attack"
            : list.Any(corps => corps.task == "defend") ? "defend" : list[0].task;
        string title = list.Count == 1 ? CorpsSystem.Name(list[0]) : string.Format(LM.Get("corps_chip_count"), list.Count);
        string realmName = owners.Values.Distinct().Count() > 1
            ? string.Format(LM.Get("corps_chip_realms"), kingdom.GetKingdomName(), owners.Values.Distinct().Count())
            : kingdom.GetKingdomName();
        // 渡海中的军团副标题写"渡海"，与舰队区分
        if (list.Any(corps => corps.embarked)) task = "embarked";
        tooltip.setTitle($"{realmName} · {title}", "corps_task_" + task, colorHex);

        float strength = list.Sum(corps => corps.strength), full = list.Sum(corps => corps.full);
        float org = list.Average(corps => corps.org);
        tooltip.addLineText(LM.Get("corps_col_strength"),
            $"{CorpsSystem.ShortStrength(strength)}/{CorpsSystem.ShortStrength(full)}", "#65D66E",
            pPercent: false, pLocalize: false, pLimitValue: 40);
        tooltip.addLineText(LM.Get("corps_col_org"), Mathf.RoundToInt(org).ToString(), org < 30f ? "#E8584F" : "#7FD8EA",
            pPercent: false, pLocalize: false);
        // 后勤(见 LogisticsSystem)：补给、口粮、海上航行
        float supply = list.Average(corps => corps.supply);
        string supplyState = LM.Get(supply >= 0.75f ? "corps_supply_good" : supply >= 0.3f ? "corps_supply_tight" : "corps_supply_cut");
        tooltip.addLineText(LM.Get("corps_tip_supply"), string.Format(LM.Get("corps_tip_supply_format"),
                Mathf.RoundToInt(supply * 100f), supplyState), supply >= 0.75f ? "#9BD27A" : supply >= 0.3f ? "#E8C35A" : "#E8584F",
            pPercent: false, pLocalize: false, pLimitValue: 40);
        float rations = list.Min(corps => corps.rations);
        if (rations < 0.99f)
            tooltip.addLineText(LM.Get("corps_tip_rations"), $"{Mathf.RoundToInt(rations * 100f)}%", "#E8584F",
                pPercent: false, pLocalize: false);
        CorpsData sailing = list.FirstOrDefault(corps => corps.embarked && corps.sea_route != null);
        if (sailing != null)
            tooltip.addLineText(LM.Get("corps_tip_at_sea"), string.Format(LM.Get("corps_tip_at_sea_format"),
                sailing.sea_step + 1, sailing.sea_route.Count), "#8FC3E8", pPercent: false, pLocalize: false);
        TileZone zone = CorpsSystem.ZoneById((int)_hoverA);
        City at = zone?.city;
        if (at?.data != null && !at.isRekt())
            tooltip.addLineText(LM.Get("corps_tip_location"), at.GetCityName(), "#C8C2B4", pPercent: false,
                pLocalize: false, pLimitValue: 40);
        if (zone != null)
            tooltip.addLineText(LM.Get("corps_tip_terrain"), TerrainSystem.Name(TerrainSystem.OfBattle(at, zone)), "#C9E6A8",
                pPercent: false, pLocalize: false, pLimitValue: 40);

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
        bool mixed = owners.Values.Distinct().Count() > 1;
        foreach (CorpsData corps in list.Take(shown))
        {
            string name = CorpsSystem.Name(corps);
            if (mixed) name = $"{owners[corps].GetKingdomName()} {name}";
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

    // 舰队：所属国与舰队名、任务，舰种、战舰、运输船(运力)、组织度、所在海区的制海权
    public static void ShowFleetTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        Kingdom kingdom = data?.kingdom;
        if (kingdom?.data == null || kingdom.isRekt() || _hoverKind != HoverFleet) return;
        FleetData fleet = NavySystem.Of(kingdom).FirstOrDefault(entry => entry.id == _hoverA);
        if (fleet == null) return;
        tooltip.clear();
        tooltip.setTitle($"{kingdom.GetKingdomName()} · {NavySystem.Name(fleet)}", "navy_task_" + fleet.task,
            kingdom.getColor()?.color_text ?? "#FFFFFF");
        tooltip.addLineText(LM.Get("navy_tip_tier"), LM.Get(NavySystem.TierKey(kingdom)), "#F3C34A", pPercent: false,
            pLocalize: false, pLimitValue: 40);
        tooltip.addLineText(LM.Get("navy_tip_warships"), fleet.warships.ToString(), "#65D66E", pPercent: false, pLocalize: false);
        float capacity = fleet.transports * 1500f * NavySystem.Tier(kingdom);
        tooltip.addLineText(LM.Get("navy_tip_transports"),
            string.Format(LM.Get("navy_tip_capacity"), fleet.transports, CorpsSystem.ShortStrength(capacity)), "#C8C2B4",
            pPercent: false, pLocalize: false, pLimitValue: 40);
        tooltip.addLineText(LM.Get("corps_col_org"), Mathf.RoundToInt(fleet.org).ToString(),
            fleet.org < 30f ? "#E8584F" : "#7FD8EA", pPercent: false, pLocalize: false);
        Kingdom controller = NavySystem.Controller(SeaRegionSystem.Get(fleet.region_id));
        tooltip.addLineText(LM.Get("navy_tip_control"), controller?.GetKingdomName() ?? LM.Get("navy_tip_none"),
            controller?.getColor()?.color_text ?? "#8FA0A8", pPercent: false, pLocalize: false, pLimitValue: 40);
        // 镜头远时几支舰队并成一块：这里只列领头的一支
        string folded = FoldedFleetCounts.TryGetValue(fleet.id, out int count) && count > 1
            ? string.Format(LM.Get("navy_tip_folded"), count) + "\n"
            : "";
        tooltip.addBottomDescription(folded + LM.Get("corps_tip_click"));
    }

    // 海战：交战双方、胜算、双方舰只与击沉
    public static void ShowNavalTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        if (_hoverKind != HoverNaval || !NavySystem.Battles.TryGetValue((int)_hoverA, out var battle)) return;
        tooltip.clear();
        Kingdom attacker = World.world.kingdoms.get(battle.attacker_id);
        Kingdom defender = World.world.kingdoms.get(battle.defender_id);
        tooltip.setTitle(LM.Get("navy_tip_battle"), "", "#FFB3A8");
        tooltip.addLineText(attacker?.GetKingdomName() ?? "?",
            string.Format(LM.Get("navy_tip_ships_sunk"), battle.attacker_ships, battle.sunk_attacker),
            attacker?.getColor()?.color_text ?? "#FFB3A8", pPercent: false, pLocalize: false, pLimitValue: 40);
        tooltip.addLineText(defender?.GetKingdomName() ?? "?",
            string.Format(LM.Get("navy_tip_ships_sunk"), battle.defender_ships, battle.sunk_defender),
            defender?.getColor()?.color_text ?? "#A8CCFF", pPercent: false, pLocalize: false, pLimitValue: 40);
        int odds = Mathf.RoundToInt(battle.ratio * 100f);
        tooltip.addLineText(LM.Get("navy_tip_odds"), odds + "%", odds >= 50 ? "#FFB3A8" : "#A8CCFF", pPercent: false,
            pLocalize: false);
        tooltip.addBottomDescription(LM.Get("navy_tip_battle_hint") + "\n" + LM.Get("corps_tip_click"));
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
        tooltip.addLineText(LM.Get("corps_tip_terrain"), TerrainSystem.Name(battle.terrain), "#C9E6A8",
            pPercent: false, pLocalize: false, pLimitValue: 40);
        tooltip.addBottomDescription(TerrainSystem.Describe(battle.terrain) + "\n" + LM.Get("corps_tip_battle_hint") +
                                     "\n" + LM.Get("corps_tip_click"));
    }

    #endregion
}

[DefaultExecutionOrder(10001)]
public sealed class CorpsMapRendererHost : MonoBehaviour
{
    private void LateUpdate() => CorpsMapRenderer.HostLateUpdate();
}
