using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EmpireCraft.Scripts.UI.Components;

// 纯净地图模式的皇帝标记：原版的君主、城主头像不画了(人物到处瞬移，头像闪来闪去，见 PureMapMode)，
// 改为在皇帝所属的城(没有就都城)城心上方画一枚皇帝标记——帝国颜色的圆形印面、旧铜环、冠
// (Tools/IconForge/emperor_marker_forge.py，印面单独一层按帝国颜色染色)。不跟着实体走，所以不闪；
// 微服私访、出访、御驾亲征时标记像兵牌一样沿路走(见 EmperorTourSystem)，换了地块在一秒里匀速滑过去，暂停时不动。
// 画在地图浮层画布上(关掉「地图名称」也照画，见 MapOverlayCanvas)，按屏幕像素定大小。共和国的元首不画。
// 鼠标停在标记上：标记放大、置顶，显示皇帝的基本情况——帝国、年龄、在位几年、资质、眼下在做什么(在宫中、微服私访、
// 出访、御驾亲征、回銮途中)、储君、天命正统(兵牌悬停时让路)。
//   · 按文化取冠：帝国制度文化的谱系(CultureDoctrines.SetOf，与派系学说、教廷判西方同一口径)查 Crowns 表——罗马一线
//     (西方各族)画西方帝冠，没列的照旧画华夏冕；以后加汗、哈里发等，在表里加一行、在 IconForge 里画一枚冠即可；
//   · 教宗标记：西方宗教(圣地是罗马一线文化的城，或教廷所在国属罗马一线，同 PapacySystem 的年度诊断)有教宗时，
//     在圣地(教廷所在)城心上方画一枚同样印面、铜环的标记，冠换成三重冕，印面用教廷所在国的国色；教宗兼做皇帝
//     (圣座帝国、教廷就在帝国核心国)而皇帝出行时跟着行程走，否则不动。滑动、悬停放大置顶、兵牌让路都与皇帝标记一样，
//     悬停显示教宗的情况——教会、圣地与所在国(是否教宗国)、年龄与在位、资质、教权、独立还是受谁挟制、兼帝位、近况、
//     进行中的圣战、骑士团；
//   · 教宗与皇帝同在一城(或在屏幕上挨着)时左右错开：皇帝在左、教宗在右，挨得越近推得越开，重叠时正好并排。
public static class EmperorMarkerRenderer
{
    private const float Size = 43f, Lift = 31f, HoverScale = 1.3f, TooltipRefresh = 1f, Gap = 2f;
    private const float CrownRefresh = 2f, SeeRefresh = 1f;
    public const string TooltipType = "empirecraft_emperor_marker";
    public const string PopeTooltipType = "empirecraft_pope_marker";

    private const string DefaultCrown = "ui/icons/emperor/marker", PopeCrown = "ui/icons/emperor/marker_pope";
    private const string RomaSet = "roma";

    // 按文化取冠：谱系(CultureDoctrines.SetOf) → 冠图；没列的用华夏冕(DefaultCrown)
    private static readonly Dictionary<string, string> Crowns = new(StringComparer.Ordinal)
    {
        [RomaSet] = "ui/icons/emperor/marker_roma"
    };

    private enum Kind { Emperor, Pope }

    private sealed class Mark
    {
        public RectTransform Root;
        public Image Field, Overlay;
    }

    // 本帧要画的一枚：皇帝标记的 Id 是帝国 id，教宗标记的是宗教 id
    private struct Entry
    {
        public Kind Kind;
        public long Id;
        public Vector2 Screen;
        public Color Color;
        public Sprite Crown;
    }

    private static RectTransform _root;
    private static readonly List<Mark> Marks = new();
    // 各帝国(教宗按宗教)的标记画在哪(世界坐标)、滑向哪、多快：目标一变就定一个速度，在一个游戏秒里匀速滑过去
    private static readonly Dictionary<long, (Vector3 at, Vector3 target, float speed)> Drawn = new();
    private static readonly Dictionary<long, (Vector3 at, Vector3 target, float speed)> DrawnPope = new();
    private static Sprite _field;
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.Ordinal);
    // 各帝国取哪枚冠(隔两秒重查一次：制度文化要按治下多数城市算)
    private static readonly Dictionary<long, (Sprite crown, float at)> EmpireCrowns = new();
    // 有教宗的西方宗教(每秒重查一次)
    private static readonly List<Religion> Sees = new();
    private static float _seesAt = float.NegativeInfinity;
    private static object _seesWorld;
    private static readonly List<Entry> Entries = new();
    // 本帧画出的标记：屏幕中心、哪一种、帝国或宗教(悬停判定)
    private static readonly List<(Vector2 center, Kind kind, long id, Mark mark)> Hits = new();
    private static Kind _hoverKind;
    private static long _hover = -1L;
    private static float _tipAt = float.NegativeInfinity;
    private static readonly object TipOwner = new();
    private static PointerEventData _pointer;

    // 悬停皇帝、教宗标记时原版地图提示让路(见 CorpsPatch)
    public static bool Hovering => _hover >= 0;
    private static readonly List<RaycastResult> RaycastHits = new();

    // 画在地图浮层画布上(见 MapOverlayCanvas)：以前挂在地名画布下，原版关掉「地图名称」时跟着一起看不见
    public static void EnsureHost()
    {
        Canvas canvas = MapOverlayCanvas.Ensure();
        if (canvas == null) return;
        if (_root != null && _root.parent == canvas.transform) return;
        if (_root != null) Object.Destroy(_root.gameObject);
        Marks.Clear();
        var rootObject = new GameObject("EmpireCraftEmperors", typeof(RectTransform), typeof(EmperorMarkerHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();
    }

    internal static void HostLateUpdate()
    {
        using var timing = FrameProfiler.Measure("纯净地图·皇帝标记");
        // 保持皇帝标记在最上层，画在兵牌上面
        if (_root != null && _root.parent != null && _root.GetSiblingIndex() != _root.parent.childCount - 1)
            _root.SetAsLastSibling();
        int used = 0;
        Hits.Clear();
        Entries.Clear();
        Canvas canvas = MapOverlayCanvas.Current;
        if (PureMapMode.Enabled && World.world?.camera != null && canvas != null && ModClass.EMPIRE_MANAGER != null)
        {
            MapOverlayCanvas.Sync();
            Camera camera = World.world.camera;
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                Actor emperor = empire?.data == null ? null : empire.Emperor;
                if (emperor?.data == null || emperor.isRekt() || !emperor.isAlive() || RepublicSystem.IsRepublic(empire)) continue;
                // 出行途中按进度画在两块地之间，每个游戏秒往前挪一点、滑过去(见 EmperorTourSystem.DrawPosition)
                Vector3? away = EmperorTourSystem.DrawPosition(empire);
                WorldTile home = away == null ? EmperorTourSystem.Home(empire)?.getTile() : null;
                if (away == null && home == null) continue;
                Vector3 at = Glide(Drawn, empire.data.id, away ?? home.posV);
                if (!OnScreen(camera, at, out Vector2 screen)) continue;
                Color color = empire.getColor()?.getColorMain() ?? Color.white;
                Entries.Add(new Entry { Kind = Kind.Emperor, Id = empire.data.id, Screen = screen, Color = color, Crown = CrownOf(empire) });
            }
            RefreshSees();
            foreach (Religion religion in Sees)
            {
                if (religion?.data == null || religion.isRekt()) continue;
                Actor pope = PapacySystem.Pope(religion);
                Kingdom papacy = PapacySystem.Papacy(religion);
                City holy = PapacySystem.HolyCity(religion);
                if (pope == null || papacy == null || holy == null) continue;
                // 教宗兼做皇帝时跟着皇帝的行程走，否则在圣地不动
                Empire throne = Throne(pope, papacy);
                Vector3? away = throne != null ? EmperorTourSystem.DrawPosition(throne) : null;
                WorldTile seat = away == null ? holy.getTile() : null;
                if (away == null && seat == null) continue;
                Vector3 at = Glide(DrawnPope, religion.data.id, away ?? seat.posV);
                if (!OnScreen(camera, at, out Vector2 screen)) continue;
                Color color = papacy.getColor()?.getColorMain() ?? Color.white;
                Entries.Add(new Entry { Kind = Kind.Pope, Id = religion.data.id, Screen = screen, Color = color, Crown = Load(PopeCrown) });
            }
            Separate();
            foreach (Entry entry in Entries)
            {
                Mark mark = Get(used++);
                mark.Root.gameObject.SetActive(true);
                mark.Root.position = new Vector3(entry.Screen.x, entry.Screen.y + Lift);
                mark.Root.sizeDelta = new Vector2(Size, Size) / scale;
                mark.Root.localScale = Vector3.one;
                Hits.Add((new Vector2(entry.Screen.x, entry.Screen.y + Lift), entry.Kind, entry.Id, mark));
                Color color = entry.Color;
                color.a = 1f;
                mark.Field.color = color;
                Sprite crown = entry.Crown != null ? entry.Crown : Load(DefaultCrown);
                if (mark.Overlay.sprite != crown) mark.Overlay.sprite = crown;
            }
        }
        for (int i = used; i < Marks.Count; i++)
            if (Marks[i].Root.gameObject.activeSelf) Marks[i].Root.gameObject.SetActive(false);
        UpdateHover();
    }

    private static bool OnScreen(Camera camera, Vector3 at, out Vector2 screen)
    {
        Vector3 point = camera.WorldToScreenPoint(at);
        screen = new Vector2(point.x, point.y);
        return point.z >= 0f && point.x >= -40f && point.x <= Screen.width + 40f && point.y >= -40f &&
               point.y <= Screen.height + 40f;
    }

    // 教宗与皇帝(或两位教宗)在屏幕上挨着时左右错开：皇帝先入列，所以同一横坐标时皇帝在左、教宗在右；
    // 挨得越近推得越开，正好重叠时两枚并排、中间留两像素。皇帝与皇帝之间照旧不管
    private static void Separate()
    {
        for (int i = 0; i < Entries.Count; i++)
        for (int j = i + 1; j < Entries.Count; j++)
        {
            Entry a = Entries[i], b = Entries[j];
            if (a.Kind == Kind.Emperor && b.Kind == Kind.Emperor) continue;
            Vector2 delta = b.Screen - a.Screen;
            float push = (Size + Gap - delta.magnitude) * 0.5f;
            if (push <= 0f) continue;
            float side = delta.x >= 0f ? 1f : -1f;
            a.Screen.x -= side * push;
            b.Screen.x += side * push;
            Entries[i] = a;
            Entries[j] = b;
        }
    }

    // 帝国的冠：按帝国制度文化的谱系查表(隔两秒重查)
    private static Sprite CrownOf(Empire empire)
    {
        float now = Time.unscaledTime;
        long id = empire.data.id;
        if (EmpireCrowns.TryGetValue(id, out var cached) && cached.crown != null && now - cached.at < CrownRefresh)
            return cached.crown;
        string set = CultureDoctrines.SetOf(CultureDoctrines.CultureOf(empire));
        Sprite crown = (Crowns.TryGetValue(set, out string path) ? Load(path) : null) ?? Load(DefaultCrown);
        EmpireCrowns[id] = (crown, now);
        return crown;
    }

    // 有教宗的西方宗教，每秒(换了世界立即)重查一次
    private static void RefreshSees()
    {
        float now = Time.unscaledTime;
        if (ReferenceEquals(_seesWorld, World.world) && now - _seesAt < SeeRefresh) return;
        _seesAt = now;
        _seesWorld = World.world;
        Sees.Clear();
        if (World.world?.religions == null) return;
        foreach (Religion religion in World.world.religions)
        {
            if (religion?.data == null || religion.isRekt()) continue;
            Kingdom papacy = PapacySystem.Papacy(religion);
            City holy = PapacySystem.HolyCity(religion);
            if (papacy == null || holy == null || PapacySystem.Pope(religion) == null || !WesternSee(holy, papacy)) continue;
            Sees.Add(religion);
        }
    }

    // 西方的教廷(同 PapacySystem.TryYearlyDiagnose 的西方宗教)：圣地是罗马一线文化的城，或教廷所在国属罗马一线
    private static bool WesternSee(City holy, Kingdom papacy) =>
        CultureDoctrines.SetOf(CultureService.GetRealmCulture(papacy)) == RomaSet ||
        CultureDoctrines.SetOf(CultureService.GetMainCulture(holy, initialize: false)) == RomaSet;

    // 教宗兼做皇帝的帝国(圣座帝国，或教廷就在帝国核心国；共和的元首不算)
    private static Empire Throne(Actor pope, Kingdom papacy)
    {
        Empire empire = papacy?.GetEmpire();
        return empire?.data != null && !empire.isRekt() && empire.Emperor == pope && !RepublicSystem.IsRepublic(empire)
            ? empire
            : null;
    }

    private static void UpdateHover()
    {
        int found = -1;
        if (InputHelpers.mouseSupported && !ScrollWindow.isWindowActive() && !ScrollWindow.isAnimationActive() &&
            !CorpsMapRenderer.Hovering)
        {
            Vector2 mouse = Input.mousePosition;
            // 正悬停的那枚放大了，先看它(两枚并排时不来回跳)
            for (int i = Hits.Count - 1; i >= 0 && _hover >= 0; i--)
            {
                if (Hits[i].kind != _hoverKind || Hits[i].id != _hover) continue;
                float radius = Size * 0.5f * HoverScale;
                if ((mouse - Hits[i].center).sqrMagnitude <= radius * radius) found = i;
                break;
            }
            for (int i = Hits.Count - 1; i >= 0 && found < 0; i--)
            {
                float radius = Size * 0.5f;
                if ((mouse - Hits[i].center).sqrMagnitude <= radius * radius) found = i;
            }
            if (found >= 0 && PointerOverPanels()) found = -1;
        }
        if (found < 0)
        {
            if (_hover < 0) return;
            _hover = -1L;
            Tooltip.hideTooltip(null, true, string.Empty);
            return;
        }
        var hit = Hits[found];
        if (hit.kind != _hoverKind || hit.id != _hover)
        {
            _hoverKind = hit.kind;
            _hover = hit.id;
            _tipAt = float.NegativeInfinity;
        }
        hit.mark.Root.localScale = Vector3.one * HoverScale;
        hit.mark.Root.SetAsLastSibling();
        float now = Time.unscaledTime;
        if (now - _tipAt < TooltipRefresh) return;
        _tipAt = now;
        if (hit.kind == Kind.Pope)
        {
            Religion religion = World.world?.religions?.get(hit.id);
            Tooltip.show(TipOwner, PopeTooltipType, new TooltipData
            {
                religion = religion, kingdom = PapacySystem.Papacy(religion), tooltip_scale = 0.7f, is_sim_tooltip = true,
                sound_allowed = false
            });
            return;
        }
        Empire empire = ModClass.EMPIRE_MANAGER?.get(hit.id);
        Tooltip.show(TipOwner, TooltipType, new TooltipData
        {
            kingdom = empire?.CoreKingdom, tooltip_scale = 0.7f, is_sim_tooltip = true, sound_allowed = false
        });
    }

    // 鼠标压在界面面板上(地名画布、地图浮层以外的 UI)时不算悬停
    private static bool PointerOverPanels()
    {
        if (EventSystem.current == null) return false;
        _pointer ??= new PointerEventData(EventSystem.current);
        _pointer.position = Input.mousePosition;
        RaycastHits.Clear();
        EventSystem.current.RaycastAll(_pointer, RaycastHits);
        return RaycastHits.Any(hit => hit.gameObject != null && !MapOverlayCanvas.IsMap(hit.gameObject.transform));
    }

    private const string Plain = "#D8D2B8";

    // 悬停详情
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        Empire empire = data?.kingdom?.GetEmpire();
        Actor emperor = empire?.Emperor;
        if (emperor?.data == null || !emperor.isAlive()) return;
        tooltip.clear();
        string colorHex = empire.getColor()?.color_text ?? "#F3C34A";
        tooltip.setTitle(emperor.getName(), "emperor_tip_subtitle", colorHex);
        tooltip.addLineText(LM.Get("emperor_tip_empire"), empire.GetEmpireFullName(), colorHex, pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("emperor_tip_age"), string.Format(LM.Get("emperor_tip_age_value"), emperor.getAge(),
            empire.GetEmperorYear()), Plain, pPercent: false, pLocalize: false);
        AddGrade(tooltip, emperor);
        tooltip.addLineText(LM.Get("emperor_tip_state"), State(empire), "#9FD0E8", pPercent: false, pLocalize: false);
        Kingdom core = empire.CoreKingdom;
        Actor heir = core != null && core.HasHeir() ? core.GetHeir() : null;
        bool designated = core != null && core.GetOrCreate().designated_heir_id >= 0;
        tooltip.addLineText(LM.Get("emperor_tip_heir"), heir?.data == null
                ? LM.Get("emperor_tip_no_heir")
                : string.Format(LM.Get(designated ? "emperor_tip_heir_designated" : "emperor_tip_heir_law"), heir.getName(), heir.getAge()),
            heir == null ? "#E07A5F" : Plain, pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("emperor_tip_mandate"), empire.Mandate.ToString(), "#F3C34A", pPercent: false, pLocalize: false);
    }

    // 教宗标记的悬停详情
    public static void ShowPopeTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        Religion religion = data?.religion;
        if (religion?.data == null || religion.isRekt()) return;
        Actor pope = PapacySystem.Pope(religion);
        Kingdom papacy = PapacySystem.Papacy(religion);
        City holy = PapacySystem.HolyCity(religion);
        if (pope?.data == null || papacy == null || holy == null) return;
        tooltip.clear();
        string colorHex = papacy.getColor()?.color_text ?? "#F3C34A";
        tooltip.setTitle(pope.getName(), "pope_tip_subtitle", colorHex);
        tooltip.addLineText(LM.Get("pope_tip_church"), religion.name, colorHex, pPercent: false, pLocalize: false);
        bool papalState = papacy.GetKingdomType() == KingdomType.Feudalism_papal_state;
        tooltip.addLineText(LM.Get("papacy_holy_city"), string.Format(LM.Get(papalState ? "pope_tip_see_papal" : "pope_tip_see_secular"),
            holy.GetCityName(), papacy.GetKingdomName()), Plain, pPercent: false, pLocalize: false);
        double since = papacy.data.timestamp_king_rule;
        int reign = since >= 0d ? Date.getYearsSince(since) + 1 : 1;
        tooltip.addLineText(LM.Get("emperor_tip_age"), string.Format(LM.Get("emperor_tip_age_value"), pope.getAge(), reign),
            Plain, pPercent: false, pLocalize: false);
        AddGrade(tooltip, pope);
        float authority = PapacySystem.Authority(religion);
        tooltip.addLineText(LM.Get("papacy_authority"), $"{authority:0}",
            authority >= 60f ? "#7CD27C" : authority >= 35f ? Plain : "#E07A5F", pPercent: false, pLocalize: false);
        bool captive = PapacySystem.IsCaptive(religion);
        tooltip.addLineText(LM.Get("papacy_state"), SeeState(religion, papacy, captive), captive ? "#E07A5F" : "#7CD27C",
            pPercent: false, pLocalize: false);
        Empire throne = Throne(pope, papacy);
        if (throne != null)
            tooltip.addLineText(LM.Get("pope_tip_throne"), throne.GetEmpireFullName(), colorHex, pPercent: false, pLocalize: false);
        tooltip.addLineText(LM.Get("emperor_tip_state"), throne != null
            ? State(throne)
            : string.Format(LM.Get("pope_tip_state_see"), holy.GetCityName()), "#9FD0E8", pPercent: false, pLocalize: false);
        War crusade = PapacySystem.ActiveCrusade(religion);
        if (crusade?.data != null)
            tooltip.addLineText(LM.Get("papacy_crusade_title"), crusade.data.name, "#E07A5F", pPercent: false, pLocalize: false);
        int orders = PapacySystem.Orders(religion).Count();
        if (orders > 0)
            tooltip.addLineText(LM.Get("papacy_orders_title"), orders.ToString(), Plain, pPercent: false, pLocalize: false);
    }

    // 资质(皇帝、教宗共用)
    private static void AddGrade(Tooltip tooltip, Actor ruler)
    {
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(ruler);
        tooltip.addLineText(LM.Get("emperor_tip_grade"), LM.Get(grade switch
            {
                RulerQualitySystem.Grade.Wise => "trait_" + RulerQualitySystem.Wise,
                RulerQualitySystem.Grade.Weak => "trait_" + RulerQualitySystem.Weak,
                RulerQualitySystem.Grade.Fatuous => "trait_" + RulerQualitySystem.Fatuous,
                RulerQualitySystem.Grade.Dissolute => "trait_" + RulerQualitySystem.Dissolute,
                RulerQualitySystem.Grade.Tyrant => "trait_" + RulerQualitySystem.Tyrant,
                _ => "emperor_tip_grade_average"
            }), grade == RulerQualitySystem.Grade.Wise ? "#7CD27C" : grade == RulerQualitySystem.Grade.Average ? Plain : "#E07A5F",
            pPercent: false, pLocalize: false);
    }

    // 教廷独立，还是受谁挟制(教廷之囚；满二十年对立教宗并立，大分裂)
    private static string SeeState(Religion religion, Kingdom papacy, bool captive)
    {
        if (!captive) return LM.Get("papacy_state_free");
        var data = religion.GetOrCreate();
        int years = data.CaptiveSince >= 0d ? Date.getYearsSince(data.CaptiveSince) : 0;
        return string.Format(LM.Get(data.Schism ? "pope_tip_schism" : "pope_tip_captive"),
            papacy.GetEmpire()?.GetEmpireFullName() ?? papacy.GetKingdomName(), years);
    }

    // 眼下在做什么(见 EmperorTourSystem)
    private static string State(Empire empire)
    {
        var data = empire.data;
        if (string.IsNullOrEmpty(data.tour_kind))
            return string.Format(LM.Get("emperor_tip_state_palace"), EmperorTourSystem.Home(empire)?.GetCityName() ?? "");
        if (data.tour_returning) return LM.Get("emperor_tip_state_returning");
        if (data.tour_kind == EmperorTourSystem.Campaign)
        {
            CorpsData corps = CorpsSystem.Find(empire.CoreKingdom, data.tour_corps);
            return string.Format(LM.Get("emperor_tip_state_campaign"), corps == null ? "" : CorpsSystem.Name(corps));
        }
        City city = World.world.cities.get(data.tour_city);
        string where = data.tour_kind == EmperorTourSystem.Visit ? city?.kingdom?.data?.name ?? "" : city?.GetCityName() ?? "";
        return string.Format(LM.Get(data.tour_kind == EmperorTourSystem.Visit ? "emperor_tip_state_visit" : "emperor_tip_state_tour"), where);
    }

    // 换了位置就在一个游戏秒里匀速滑过去(太远就直接到，如换了皇帝)；暂停时不动
    private static Vector3 Glide(Dictionary<long, (Vector3 at, Vector3 target, float speed)> drawn, long id, Vector3 target)
    {
        if (!drawn.TryGetValue(id, out var glide) || (target - glide.at).magnitude > 40f)
        {
            drawn[id] = (target, target, 0f);
            return target;
        }
        if (glide.target != target) glide = (glide.at, target, Mathf.Max(0.5f, (target - glide.at).magnitude));
        float delta = Config.paused ? 0f : Time.deltaTime * Mathf.Max(1f, Config.time_scale_asset?.multiplier ?? 1f);
        glide.at = Vector3.MoveTowards(glide.at, glide.target, glide.speed * delta);
        drawn[id] = glide;
        return glide.at;
    }

    private static Mark Get(int index)
    {
        while (Marks.Count <= index)
        {
            var rootObject = new GameObject("EmperorMark", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(_root, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            Marks.Add(new Mark
            {
                Root = root,
                Field = Layer(root, "Field", _field ??= Load("ui/icons/emperor/marker_field")),
                Overlay = Layer(root, "Overlay", Load(DefaultCrown))
            });
        }
        return Marks[index];
    }

    private static Sprite Load(string path)
    {
        if (Sprites.TryGetValue(path, out Sprite cached) && cached != null) return cached;
        Sprite sprite = SpriteTextureLoader.getSprite(path);
        if (sprite != null) sprite.texture.filterMode = FilterMode.Point;
        Sprites[path] = sprite;
        return sprite;
    }

    private static Image Layer(RectTransform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }
}

public sealed class EmperorMarkerHost : MonoBehaviour
{
    private void LateUpdate() => EmperorMarkerRenderer.HostLateUpdate();
}
