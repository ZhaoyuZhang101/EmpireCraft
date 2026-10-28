using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 城池全图(用户 2026-10-28 "能不能在宽窗口直接显示这个城镇的形状和完整城市样式, 然后允许玩家随意修改"，确认示意后要
// "窗口全屏，整座城显示完整")：自建一块全屏画布(不用 ScrollWindow，那个窗口大小固定)，盖在原版界面之上，深色底板挡住地图点击——
//   左「城池全图」：打开时缩放到整座城连同城郊村镇田路都看得见(纹理见 CityPlanEditing.RenderPreview)；滚轮以光标为中心缩放，
//     右键、中键拖动平移(选「查看」时左键也拖)；上方工具栏：查看、城区画/擦、宫城、城墙、护城河、中轴·城门、坊市疏密、换个样子、
//     看全城、撤销、交还规矩(五秒内再点一次才交还)；城区按住拖着刷，一笔只记一步撤销，宫城工具下右键点一下宫城交还规矩。
//     改动都走 CityPlanEditing，返回的提示写在底部状态栏；改不了时工具栏变灰、状态栏写原因，右栏照常能用；
//   右窄栏(可收成一道边)：概况、营建、城貌，取数与按钮同城池小窗口(CityStyleWindow)；
//   纯净地图下街道一级点主城城区、「城貌」神力点城打开(见 MapLabelOverlay.HandleClick、SettlementPowers.ClickRestyle)，
//   城池小窗口里「全城规划」也打开；Esc、右上角关闭；城没了、换存档时自己关掉。
public static class CityPlanScreen
{
    private enum Tool { View, Zone, Palace, Axis }

    // 画布按 1600×900 参考、只照高度缩放：1080p 下一单位 1.2 像素，正文 15 号约 18 像素高；宽屏、窄屏高度都是 900 单位
    private const float RefWidth = 1600f, RefHeight = 900f;
    private const float HeaderHeight = 46f, StatusHeight = 30f, Margin = 10f, SideWidth = 320f, SideFolded = 30f;
    private const float CellWidth = (SideWidth - 20f - 8f) / 2f;
    private const float RenderSeconds = 5f, PanelSeconds = 1f, ConfirmSeconds = 5f, MessageSeconds = 6f;
    // 拖着刷城区时每块地都改一次，全图(一次几十到一百多毫秒)至多每 0.3 秒重画一次，松手立刻补画
    private const float StrokeRenderSeconds = 0.3f;
    // 按下到松开挪动不超过这么多屏幕像素算点一下(宫城、中轴)
    private const float ClickSlop = 6f;
    private const int FlagEditable = 1, FlagTown = 2;

    private static readonly Color Backdrop = new Color(0.06f, 0.07f, 0.07f, 0.92f);
    private static readonly Color PanelColor = new Color(0.14f, 0.15f, 0.14f, 0.97f);
    private static readonly Color FrameColor = new Color(0.78f, 0.62f, 0.26f, 0.9f);
    private static readonly Color MapBack = new Color(0.04f, 0.05f, 0.04f, 1f);
    private static readonly Color StatusBack = new Color(0.1f, 0.11f, 0.1f, 0.97f);
    private static readonly Color TipBack = new Color(0.03f, 0.03f, 0.03f, 0.9f);
    // 红：工具；橙：选中的工具、现城貌；蓝灰：看全城、撤销、交还规矩、关闭；褐灰：开关关着
    private static readonly Color ToolColor = new Color(0.66f, 0.2f, 0.16f, 1f);
    private static readonly Color ActiveColor = new Color(0.86f, 0.58f, 0.22f, 1f);
    private static readonly Color ViewColor = new Color(0.23f, 0.35f, 0.47f, 1f);
    private static readonly Color OffColor = new Color(0.36f, 0.31f, 0.29f, 1f);
    private static readonly Color TextColor = new Color(0.95f, 0.93f, 0.89f, 1f);
    private static readonly Color DimText = new Color(0.56f, 0.63f, 0.66f, 1f);
    private static readonly Color TitleColor = new Color(0.95f, 0.84f, 0.58f, 1f);
    private static readonly Color HeadColor = new Color(0.55f, 0.84f, 0.56f, 1f);
    private static readonly Color CyanText = new Color(0.5f, 0.85f, 0.92f, 1f);

    // 叠加层(每格一像素)：城区——可并入的淡绿、可划出的淡红、地块网格；宫城——本城的地淡金、现宫城红底金边；中轴——现中轴两条金线
    private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    private static readonly Color32 GridLine = new Color32(255, 255, 255, 34);
    private static readonly Color32 AddTint = new Color32(90, 220, 110, 46);
    private static readonly Color32 EraseTint = new Color32(235, 90, 70, 40);
    private static readonly Color32 OwnTint = new Color32(240, 200, 90, 34);
    private static readonly Color32 PalaceFill = new Color32(220, 60, 50, 110);
    private static readonly Color32 AxisLine = new Color32(255, 214, 90, 120);
    // 光标下：地块描边与底色、中轴十字
    private static readonly Color32 AddEdge = new Color32(110, 255, 130, 235), AddFill = new Color32(110, 255, 130, 70);
    private static readonly Color32 EraseEdge = new Color32(255, 110, 90, 235), EraseFill = new Color32(255, 110, 90, 70);
    private static readonly Color32 LockedEdge = new Color32(170, 170, 170, 200), LockedFill = new Color32(120, 120, 120, 50);
    private static readonly Color32 PalaceEdge = new Color32(255, 210, 90, 235), PalaceHover = new Color32(255, 210, 90, 70);
    private static readonly Color32 CrossLine = new Color32(255, 255, 255, 90), CrossCenter = new Color32(255, 230, 120, 255);

    private sealed class Btn
    {
        public Button Button;
        public Image Image;
        public Text Label;

        public void Set(string text, Color color, bool interactable)
        {
            if (Label.text != text) Label.text = text;
            Image.color = color;
            Button.interactable = interactable;
            Label.color = interactable ? TextColor : DimText;
        }
    }

    private static GameObject _root;
    private static RectTransform _canvasRect, _mapArea, _side, _strip, _viewport, _tip;
    private static RawImage _map, _over;
    private static Text _title, _subtitle, _status, _tipText, _empty, _overviewName, _overview, _mapHint;
    private static Btn _viewBtn, _zoneBtn, _palaceBtn, _wallsBtn, _moatBtn, _axisBtn, _blocksBtn, _rerollBtn, _fitBtn, _undoBtn, _resetBtn;
    private static readonly List<(string option, Btn button)> StyleButtons = new List<(string option, Btn button)>();

    private static City _city;
    private static long _cityId = -1L;
    private static bool _folded;
    private static Tool _tool = Tool.View;
    private static bool _editable;
    private static string _reason, _message, _option, _styleLine;
    private static float _messageAt = -100f, _renderedAt = -100f, _panelAt = -100f, _resetArmedAt = -100f;

    // 预览纹理与它对着的世界格范围(纹理第 0 行是最下面一格 y0)
    private static Texture2D _preview, _overlay;
    private static int _x0, _y0, _w, _h, _scale = 1, _revision = int.MinValue, _renderSerial, _planKey;
    // 营建、换城貌之后下一帧补画一次
    private static bool _renderDue;
    // 视图：视口中心对着的世界格坐标、每格多少画布单位；打开后头几帧跟着视口大小整图适配(画布头一帧还没量好尺寸)
    private static Vector2 _center;
    private static float _zoom = 1f;
    private static int _autoFit;

    // 叠加层：底层随工具、重渲变才重建，光标换地块(中轴换格)只在底层上补画
    private static Color32[] _base, _pixels;
    // 刷城区改了某块地：只把那块重问一遍、底层重铺(全图等限流的重画)
    private static bool _baseDirty;
    private static int _baseSerial = -1, _hoverKey = int.MinValue, _palaceZone = -1;
    private static Tool _baseTool = Tool.View;
    private static readonly Dictionary<int, int> ZoneFlags = new Dictionary<int, int>();

    // 鼠标：平移用哪个键、上一帧的视口坐标；左键按下的位置；城区拖着画时的模式与上一处
    private static WorldTile _hoverTile;
    private static int _dragButton = -1;
    private static Vector2 _dragLast, _pressAt, _rightAt, _paintWorld;
    private static bool _pressed, _rightPressed, _painting, _paintAdd;
    private static TileZone _paintZone;

    public static bool IsOpen => _root != null;

    public static void Open(City city)
    {
        if (city?.data == null || city.isRekt()) return;
        Close();
        _city = city;
        _cityId = city.data.id;
        _tool = Tool.View;
        try
        {
            Build();
            Fold(_folded);
            _autoFit = 3;
            Rerender();
            RefreshToolbar();
            RefreshPanel();
            UpdateStatus();
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 城池全图打开失败: {exception}");
            Close();
        }
    }

    public static void Close()
    {
        EndPaint();
        if (_root != null) Object.Destroy(_root);
        if (_preview != null) Object.Destroy(_preview);
        if (_overlay != null) Object.Destroy(_overlay);
        _root = null;
        _preview = _overlay = null;
        _map = _over = null;
        _city = null;
        _cityId = -1L;
        _base = _pixels = null;
        _w = _h = 0;
        _baseSerial = -1;
        _hoverKey = int.MinValue;
        _hoverTile = null;
        _dragButton = -1;
        _pressed = _rightPressed = _renderDue = _baseDirty = false;
        _paintZone = null;
        _message = null;
        _resetArmedAt = -100f;
        _revision = int.MinValue;
        StyleButtons.Clear();
        ZoneFlags.Clear();
    }

    #region 搭界面

    private static void Build()
    {
        _root = new GameObject("EmpireCraftCityPlanScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster), typeof(CityPlanScreenHost));
        Object.DontDestroyOnLoad(_root);
        Canvas canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 盖过原版各层界面(窗口、提示)
        canvas.sortingOrder = 30000;
        CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        _canvasRect = (RectTransform)_root.transform;

        // 底板：铺满全屏接住射线，原版 World.world.isOverUI() 为真，点不到地图、放不了神力
        RectTransform backdrop = Child("Backdrop", _canvasRect);
        Place(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Fill(backdrop, Backdrop, true);

        // 标题行：城池 · 城名，国家 · 现城貌 · 怎么关；右上角关闭
        RectTransform header = Child("Header", _canvasRect);
        Place(header, new Vector2(0f, 1f), Vector2.one, new Vector2(Margin, -HeaderHeight), new Vector2(-Margin, 0f));
        Row(header, 18f, new RectOffset(6, 0, 6, 6));
        _title = Label(header, "Title", 24, TitleColor, TextAnchor.MiddleLeft);
        _subtitle = Label(header, "Subtitle", 16, CyanText, TextAnchor.MiddleLeft);
        Spacer(header);
        MakeButton(header, LM.Get("city_plan_ui_close"), ViewColor, Close, 16, 32f);

        // 底部状态栏：操作提示、各操作返回的提示、改不了的原因
        RectTransform status = Child("Status", _canvasRect);
        Place(status, Vector2.zero, new Vector2(1f, 0f), new Vector2(Margin, 4f), new Vector2(-Margin, 4f + StatusHeight));
        Fill(status, StatusBack);
        _status = Label(status, "Text", 15, TextColor, TextAnchor.MiddleLeft);
        Place(_status.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));

        RectTransform body = Child("Body", _canvasRect);
        Place(body, Vector2.zero, Vector2.one, new Vector2(Margin, StatusHeight + 12f), new Vector2(-Margin, -HeaderHeight));
        BuildMap(body);
        BuildSide(body);

        // 光标旁的小提示(并入、划出、挪宫城……)
        _tip = Child("Tip", _canvasRect);
        _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0.5f);
        _tip.pivot = new Vector2(0f, 1f);
        Fill(_tip, TipBack);
        Row(_tip, 0f, new RectOffset(8, 8, 4, 4));
        var fitter = _tip.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _tipText = Label(_tip, "Text", 15, TextColor, TextAnchor.MiddleLeft);
        _tip.gameObject.SetActive(false);
    }

    private static void BuildMap(RectTransform body)
    {
        _mapArea = Child("MapArea", body);
        Place(_mapArea, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-(SideWidth + Margin), 0f));
        Fill(_mapArea, PanelColor);
        Frame(_mapArea);
        Text title = Label(_mapArea, "MapTitle", 18, HeadColor, TextAnchor.MiddleLeft);
        Place(title.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(10f, -32f), new Vector2(-10f, -4f));
        title.text = LM.Get("city_plan_ui_map");
        _mapHint = Label(_mapArea, "MapHint", 13, DimText, TextAnchor.MiddleRight);
        Place(_mapHint.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(120f, -32f), new Vector2(-10f, -4f));
        _mapHint.text = LM.Get("city_plan_ui_map_hint");

        // 工具栏：左边是改的，右边是看的与撤回的
        RectTransform bar = Child("Toolbar", _mapArea);
        Place(bar, new Vector2(0f, 1f), Vector2.one, new Vector2(8f, -68f), new Vector2(-8f, -36f));
        Row(bar, 6f, new RectOffset(0, 0, 0, 0));
        _viewBtn = MakeButton(bar, LM.Get("city_plan_ui_tool_view"), ToolColor, () => SetTool(Tool.View));
        _zoneBtn = MakeButton(bar, LM.Get("city_plan_ui_tool_zone"), ToolColor, () => SetTool(Tool.Zone));
        _palaceBtn = MakeButton(bar, LM.Get("city_plan_ui_tool_palace"), ToolColor, () => SetTool(Tool.Palace));
        _wallsBtn = MakeButton(bar, "", ToolColor, () => Act(CityPlanEditing.ToggleWalls(_city)));
        _moatBtn = MakeButton(bar, "", ToolColor, () => Act(CityPlanEditing.ToggleMoat(_city)));
        // 中轴·城门：头一下选这个工具(点格挪中轴交点)，已选着再点一下开关中轴
        _axisBtn = MakeButton(bar, "", ToolColor, () =>
        {
            if (_tool == Tool.Axis) Act(CityPlanEditing.ToggleAxis(_city));
            else SetTool(Tool.Axis);
        });
        _blocksBtn = MakeButton(bar, "", ToolColor, () => Act(CityPlanEditing.CycleBlocks(_city)));
        _rerollBtn = MakeButton(bar, LM.Get("city_plan_ui_reroll"), ToolColor, () => Act(CityPlanEditing.Reroll(_city)));
        Spacer(bar);
        _fitBtn = MakeButton(bar, LM.Get("city_plan_ui_fit"), ViewColor, Fit);
        _undoBtn = MakeButton(bar, LM.Get("city_plan_ui_undo"), ViewColor, () =>
        {
            if (_city == null) return;
            Say(LM.Get(CityPlanEditing.Undo(_city) ? "city_plan_ui_undo_done" : "city_plan_ui_undo_none"));
            RefreshToolbar();
        });
        _resetBtn = MakeButton(bar, LM.Get("city_plan_ui_reset"), ViewColor, ClickReset);

        // 视口：裁掉放大后伸出去的部分；预览与叠加层同尺寸叠在一起，按视图摆放(见 Layout)
        _viewport = Child("Viewport", _mapArea);
        Place(_viewport, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -74f));
        Fill(_viewport, MapBack, true);
        _viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform preview = Child("Preview", _viewport);
        preview.anchorMin = preview.anchorMax = preview.pivot = new Vector2(0.5f, 0.5f);
        _map = preview.gameObject.AddComponent<RawImage>();
        _map.raycastTarget = false;
        _map.enabled = false;
        RectTransform overlay = Child("Overlay", preview);
        Place(overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _over = overlay.gameObject.AddComponent<RawImage>();
        _over.raycastTarget = false;
        _over.enabled = false;
        _empty = Label(_viewport, "Empty", 18, DimText, TextAnchor.MiddleCenter, true);
        Place(_empty.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 40f), new Vector2(-40f, -40f));
    }

    private static void BuildSide(RectTransform body)
    {
        _side = Child("Side", body);
        Place(_side, new Vector2(1f, 0f), Vector2.one, new Vector2(-SideWidth, 0f), Vector2.zero);
        var column = _side.gameObject.AddComponent<VerticalLayoutGroup>();
        column.spacing = 8f;
        column.childAlignment = TextAnchor.UpperCenter;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        MakeButton(_side, LM.Get("city_plan_ui_fold"), ViewColor, () => Fold(true), 14, 26f);

        // 概况：城名、国家、人口与上限、民居与富庶、城郊生计、现城貌、规划
        RectTransform overview = Card(_side, LM.Get("city_plan_ui_overview"));
        _overviewName = Label(overview, "Name", 20, TextColor, TextAnchor.MiddleLeft, true);
        _overview = Label(overview, "Lines", 15, TextColor, TextAnchor.UpperLeft, true);
        _overview.lineSpacing = 1.1f;

        // 营建：繁荣、加减人口、营宫殿(同城池小窗口，同神力)
        RectTransform build = Card(_side, LM.Get("city_window_build"));
        RectTransform actions = Columns(build, 30f);
        MakeButton(actions, LM.Get("city_flourish"), ToolColor, Flourish);
        MakeButton(actions, LM.Get("city_window_people_add"), ToolColor, () => People(true));
        MakeButton(actions, LM.Get("city_window_people_remove"), ToolColor, () => People(false));
        MakeButton(actions, LM.Get("build_palace"), ToolColor, BuildPalace);

        // 城貌：各文化系画法，末尾「照现文化」(同城池小窗口，见 CityStyleSystem.SetByHand、FollowCulture)
        RectTransform styles = Card(_side, LM.Get("city_style_window_styles"));
        RectTransform grid = Columns(styles, 28f);
        StyleButtons.Clear();
        foreach (string option in CityStyleSystem.ManualStyles)
        {
            string choice = option;
            StyleButtons.Add((choice, MakeButton(grid, CityStyleSystem.StyleName(choice), ToolColor, () => Restyle(choice), 15, 28f)));
        }
        MakeButton(grid, LM.Get("city_style_follow"), ViewColor, () => Restyle(null), 15, 28f);
        Text hint = Label(styles, "Hint", 13, DimText, TextAnchor.UpperLeft, true);
        hint.text = LM.Get("city_style_window_hint");

        // 收起后剩的一道边：点开再展开
        _strip = Child("SideStrip", body);
        Place(_strip, new Vector2(1f, 0f), Vector2.one, new Vector2(-SideFolded, 0f), Vector2.zero);
        Btn open = MakeButton(_strip, "<", ViewColor, () => Fold(false), 16, 0f);
        Place((RectTransform)open.Button.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _strip.gameObject.SetActive(false);
    }

    private static RectTransform Child(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Image Fill(RectTransform rect, Color color, bool raycast = false)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    // 金色细边(方块描一圈)
    private static void Frame(RectTransform rect)
    {
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = FrameColor;
        outline.effectDistance = new Vector2(1.5f, 1.5f);
    }

    private static void Row(RectTransform rect, float spacing, RectOffset padding)
    {
        var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.padding = padding;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
    }

    private static void Spacer(RectTransform parent) => Child("Spacer", parent).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

    private static Text Label(Transform parent, string name, int size, Color color, TextAnchor anchor, bool wrap = false)
    {
        Text text = Child(name, parent).gameObject.AddComponent<Text>();
        text.font = LocalizedTextManager.current_font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.supportRichText = true;
        text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    // 平色按钮：宽随字(按钮自带横排把字撑开)，高由 height 定；点完取消选中，免得空格、回车又按一次
    private static Btn MakeButton(Transform parent, string text, Color color, UnityAction action, int size = 15, float height = 28f)
    {
        RectTransform rect = Child("Button", parent);
        var button = new Btn { Image = Fill(rect, color, true) };
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);
        button.Button = rect.gameObject.AddComponent<Button>();
        button.Button.targetGraphic = button.Image;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        colors.fadeDuration = 0.06f;
        button.Button.colors = colors;
        button.Button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.Button.onClick.AddListener(() =>
        {
            EventSystem.current?.SetSelectedGameObject(null);
            action();
        });
        var inner = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        inner.padding = new RectOffset(12, 12, 2, 2);
        inner.childAlignment = TextAnchor.MiddleCenter;
        inner.childControlWidth = inner.childControlHeight = true;
        inner.childForceExpandWidth = inner.childForceExpandHeight = true;
        var element = rect.gameObject.AddComponent<LayoutElement>();
        if (height > 0f) element.minHeight = element.preferredHeight = height;
        button.Label = Label(rect, "Text", size, TextColor, TextAnchor.MiddleCenter);
        button.Label.text = text;
        return button;
    }

    // 右栏的一段：深底金边，段名在上，下面竖排
    private static RectTransform Card(Transform parent, string title)
    {
        RectTransform card = Child("Card", parent);
        Fill(card, PanelColor);
        Frame(card);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 10);
        layout.spacing = 6f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        Label(card, "Title", 18, HeadColor, TextAnchor.MiddleLeft).text = title;
        return card;
    }

    // 两列按钮
    private static RectTransform Columns(Transform parent, float cellHeight)
    {
        RectTransform rect = Child("Grid", parent);
        var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CellWidth, cellHeight);
        grid.spacing = new Vector2(8f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperLeft;
        return rect;
    }

    private static void Fold(bool folded)
    {
        _folded = folded;
        if (_side == null) return;
        _side.gameObject.SetActive(!folded);
        _strip.gameObject.SetActive(folded);
        _mapArea.offsetMax = new Vector2(-((folded ? SideFolded : SideWidth) + Margin), 0f);
        Layout();
    }

    #endregion

    #region 每帧

    internal static void Tick(CityPlanScreenHost host)
    {
        if (_root == null || host == null || host.gameObject != _root) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }
        if (!Alive(out bool gone))
        {
            Close();
            if (gone) WorldTip.showNow(LM.Get("city_style_none"), false, "top", 3f);
            return;
        }
        try
        {
            float now = Time.unscaledTime;
            // 城自己长大、改建(平面图指纹变了)：每秒比一次，至多每 5 秒重画一次
            if (now - _panelAt >= PanelSeconds)
            {
                RefreshPanel();
                RefreshToolbar();
                if (now - _renderedAt >= RenderSeconds && CityPlanEditing.PlanKey(_city) != _planKey) _renderDue = true;
            }
            // 改了就重画(拖着刷城区时限流，松手立刻补)
            if (_renderDue || CityPlanEditing.Revision != _revision && (!_painting || now - _renderedAt >= StrokeRenderSeconds))
            {
                Rerender();
                RefreshToolbar();
            }
            if (_autoFit > 0)
            {
                _autoFit--;
                Fit();
            }
            if (_resetArmedAt > 0f && now - _resetArmedAt > ConfirmSeconds)
            {
                _resetArmedAt = -100f;
                RefreshToolbar();
            }
            HandleMouse();
            UpdateOverlay();
            UpdateTip();
            UpdateStatus();
        }
        catch (global::System.Exception exception)
        {
            // 出错就关掉，免得全屏盖着回不去
            LogService.LogWarning($"[EmpireCraft] 城池全图出错，已关闭: {exception}");
            Close();
        }
    }

    // 城还在、还是打开时那座(换存档、重新生成世界后城对象换了，或正在清场、读档)
    private static bool Alive(out bool gone)
    {
        gone = false;
        if (ModClass.IS_CLEAR || SmoothLoader.isLoading() || World.world?.cities == null) return false;
        City now = World.world.cities.get(_cityId);
        if (now != null && now == _city && now.data != null && !now.isRekt()) return true;
        gone = now == null || now == _city;
        return false;
    }

    private static void HandleMouse()
    {
        Vector2 mouse = Input.mousePosition;
        bool inside = RectTransformUtility.RectangleContainsScreenPoint(_viewport, mouse, null);
        int tx = 0, ty = 0;
        bool onTile = inside && TileAt(mouse, out tx, out ty);
        _hoverTile = onTile ? World.world.GetTile(tx, ty) : null;
        TileZone zone = _hoverTile?.zone;

        // 滚轮：以光标为中心缩放
        float wheel = Input.mouseScrollDelta.y;
        if (inside && Mathf.Abs(wheel) > 0.01f) ZoomAt(mouse, Mathf.Pow(1.2f, wheel));

        // 平移：右键、中键拖动；选「查看」(或改不了)时左键也拖
        bool leftPans = _tool == Tool.View || !_editable;
        if (_dragButton < 0)
        {
            int button = Input.GetMouseButtonDown(1) ? 1 : Input.GetMouseButtonDown(2) ? 2
                : Input.GetMouseButtonDown(0) && leftPans ? 0 : -1;
            if (button >= 0 && inside && RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse, null, out _dragLast))
            {
                _dragButton = button;
                _autoFit = 0;
            }
        }
        else if (!Input.GetMouseButton(_dragButton)) _dragButton = -1;
        else if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse, null, out Vector2 local))
        {
            _center -= (local - _dragLast) / _zoom;
            _dragLast = local;
            Layout();
        }

        // 宫城工具下右键点一下(没拖)：宫城交还规矩
        if (Input.GetMouseButtonDown(1) && inside)
        {
            _rightPressed = true;
            _rightAt = mouse;
        }
        if (Input.GetMouseButtonUp(1) && _rightPressed)
        {
            _rightPressed = false;
            if (_tool == Tool.Palace && _editable && (mouse - _rightAt).sqrMagnitude <= ClickSlop * ClickSlop)
                Act(CityPlanEditing.MovePalace(_city, null) ?? LM.Get("city_plan_palace_auto"));
        }

        if (leftPans)
        {
            _pressed = false;
            EndPaint();
            return;
        }
        if (Input.GetMouseButtonDown(0) && inside)
        {
            _pressed = true;
            _pressAt = mouse;
            // 城区：头一块定"画"还是"擦"，当场切换；按住拖的这一笔只记一步撤销
            if (_tool == Tool.Zone && zone != null && ScreenToWorld(mouse, out _paintWorld))
            {
                EndPaint();
                _painting = true;
                _paintAdd = !CityPlanEditing.InTown(_city, zone);
                _paintZone = zone;
                CityPlanEditing.BeginStroke(_city);
                PaintZone(zone);
            }
        }
        // 拖到视口外(看不见的地方)不画，只跟着记位置
        if (_painting)
        {
            if (!Input.GetMouseButton(0)) EndPaint();
            else if (inside) PaintTo(mouse);
            else ScreenToWorld(mouse, out _paintWorld);
        }
        if (!Input.GetMouseButtonUp(0) || !_pressed) return;
        _pressed = false;
        if (!onTile || (mouse - _pressAt).sqrMagnitude > ClickSlop * ClickSlop) return;
        if (_tool == Tool.Palace && zone != null) Act(CityPlanEditing.MovePalace(_city, zone));
        else if (_tool == Tool.Axis) Act(CityPlanEditing.MoveAxis(_city, tx, ty));
    }

    // 拖着画城区：从上一处到光标逐段取格(一帧挪得远也不漏地块)，只动与这回模式不符、又能改的地块
    private static void PaintTo(Vector2 screen)
    {
        if (!ScreenToWorld(screen, out Vector2 world)) return;
        int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(_paintWorld, world) / 2f), 1, 256);
        for (int i = 1; i <= steps; i++)
        {
            Vector2 at = Vector2.Lerp(_paintWorld, world, i / (float)steps);
            int x = Mathf.FloorToInt(at.x), y = Mathf.FloorToInt(at.y);
            if (x < _x0 || y < _y0 || x >= _x0 + _w || y >= _y0 + _h) continue;
            TileZone zone = World.world.GetTile(x, y)?.zone;
            if (zone == null || zone == _paintZone) continue;
            _paintZone = zone;
            if (CityPlanEditing.InTown(_city, zone) != _paintAdd && CityPlanEditing.ZoneEditable(_city, zone)) PaintZone(zone);
        }
        _paintWorld = world;
    }

    // 并入/划出一块；改成了就让叠加层只重问这一块(拒绝时也有提示，改没改看 Revision)
    private static void PaintZone(TileZone zone)
    {
        int before = CityPlanEditing.Revision;
        Act(CityPlanEditing.ToggleZone(_city, zone));
        if (CityPlanEditing.Revision == before) return;
        ZoneFlags.Remove(zone.id);
        _baseDirty = true;
    }

    // 一笔画完(松手、换工具、关窗)：整笔记作一步撤销(见 CityPlanEditing.BeginStroke)，全图下一帧补画
    private static void EndPaint()
    {
        if (!_painting) return;
        _painting = false;
        _paintZone = null;
        CityPlanEditing.EndStroke();
    }

    // 屏幕点 → 预览上的格：RawImage 局部点 → 纹理像素 → 世界格(x0 + px/scale, y0 + py/scale)；Overlay 画布不传相机
    private static bool TileAt(Vector2 screen, out int x, out int y)
    {
        x = y = 0;
        if (_preview == null || _w <= 0) return false;
        RectTransform rect = _map.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out Vector2 local)) return false;
        Rect r = rect.rect;
        if (r.width <= 0f || r.height <= 0f) return false;
        float u = (local.x - r.xMin) / r.width, v = (local.y - r.yMin) / r.height;
        if (u < 0f || v < 0f || u >= 1f || v >= 1f) return false;
        int scale = Mathf.Max(1, _scale);
        x = _x0 + Mathf.FloorToInt(u * _preview.width) / scale;
        y = _y0 + Mathf.FloorToInt(v * _preview.height) / scale;
        return x < _x0 + _w && y < _y0 + _h;
    }

    // 屏幕点 → 世界格坐标(带小数，视口中心对着 _center)
    private static bool ScreenToWorld(Vector2 screen, out Vector2 world)
    {
        world = default;
        if (_zoom <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screen, null, out Vector2 local))
            return false;
        world = _center + (local - _viewport.rect.center) / _zoom;
        return true;
    }

    #endregion

    #region 视图

    // 换纹理(打开、改了、定时)：视图按世界格记，城长大、范围变了也不跳
    private static void Rerender()
    {
        _renderedAt = Time.unscaledTime;
        _revision = CityPlanEditing.Revision;
        _renderDue = false;
        _renderSerial++;
        Texture2D texture = CityPlanEditing.RenderPreview(_city, out int x0, out int y0, out int width, out int height, out int scale);
        _planKey = CityPlanEditing.PlanKey(_city);
        if (_preview != null && _preview != texture) Object.Destroy(_preview);
        _preview = texture;
        if (texture == null || width <= 0 || height <= 0)
        {
            _w = _h = 0;
            _map.enabled = _over.enabled = false;
            CityPlanEditing.Editable(_city, out string reason);
            _empty.text = string.IsNullOrEmpty(reason) ? LM.Get("city_plan_ui_no_preview") : reason;
            return;
        }
        if (_w <= 0) _autoFit = 3;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        _map.texture = texture;
        _map.enabled = true;
        _empty.text = "";
        // 城太大、全图只画了城心附近时，标题行右边写一句(见 CityPlanEditing.PreviewNote)
        string note = CityPlanEditing.PreviewNote;
        _mapHint.text = string.IsNullOrEmpty(note) ? LM.Get("city_plan_ui_map_hint") : note.ColorString("#E9A85B");
        _x0 = x0;
        _y0 = y0;
        _w = width;
        _h = height;
        _scale = Mathf.Max(1, scale);
        Layout();
    }

    private static float FitZoom()
    {
        Rect view = _viewport.rect;
        return _w <= 0 || view.width < 2f || view.height < 2f ? 0f : Mathf.Min(view.width / _w, view.height / _h);
    }

    // 看全城：整座城(连同城郊)正好装进视口
    private static void Fit()
    {
        float fit = FitZoom();
        if (fit <= 0f) return;
        _zoom = fit * 0.97f;
        _center = new Vector2(_x0 + _w / 2f, _y0 + _h / 2f);
        Layout();
    }

    // 以光标为中心缩放：光标下那一格缩放前后不动；最小到整图一半大，最大到一格 36 单位(或整图两倍)
    private static void ZoomAt(Vector2 screen, float factor)
    {
        float fit = FitZoom();
        if (fit <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screen, null, out Vector2 local)) return;
        _autoFit = 0;
        local -= _viewport.rect.center;
        Vector2 world = _center + local / _zoom;
        _zoom = Mathf.Clamp(_zoom * factor, fit * 0.5f, Mathf.Max(fit * 2f, 36f));
        _center = world - local / _zoom;
        Layout();
    }

    // 按视图摆预览：中心别拖出整图
    private static void Layout()
    {
        if (_map == null || _w <= 0) return;
        _center.x = Mathf.Clamp(_center.x, _x0, _x0 + _w);
        _center.y = Mathf.Clamp(_center.y, _y0, _y0 + _h);
        RectTransform rect = _map.rectTransform;
        rect.sizeDelta = new Vector2(_w * _zoom, _h * _zoom);
        rect.anchoredPosition = (new Vector2(_x0 + _w / 2f, _y0 + _h / 2f) - _center) * _zoom;
    }

    #endregion

    #region 叠加层

    private static void UpdateOverlay()
    {
        if (_over == null) return;
        if (_tool == Tool.View || !_editable || _preview == null || _w <= 0)
        {
            if (_over.enabled) _over.enabled = false;
            return;
        }
        bool fresh = _baseSerial != _renderSerial || _baseTool != _tool || _base == null;
        bool rebuilt = fresh || _baseDirty;
        if (rebuilt) BuildBase(fresh);
        int hover = HoverKey();
        if (!rebuilt && hover == _hoverKey && _over.enabled) return;
        _hoverKey = hover;
        _base.CopyTo(_pixels, 0);
        DrawHover();
        _overlay.SetPixels32(_pixels);
        _overlay.Apply(false);
        _over.texture = _overlay;
        _over.enabled = true;
    }

    // 底层：每格一像素铺满预览范围；fresh(重画过、换了工具)时各地块重新问一遍
    private static void BuildBase(bool fresh)
    {
        _baseDirty = false;
        _baseSerial = _renderSerial;
        _baseTool = _tool;
        if (_overlay == null || _overlay.width != _w || _overlay.height != _h)
        {
            if (_overlay != null) Object.Destroy(_overlay);
            _overlay = new Texture2D(_w, _h, TextureFormat.RGBA32, false)
            {
                name = "EmpireCraftCityPlanScreen", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
        }
        int count = _w * _h;
        if (_base == null || _base.Length != count)
        {
            _base = new Color32[count];
            _pixels = new Color32[count];
        }
        if (fresh) ZoneFlags.Clear();
        var state = CityPlanEditing.State(_city);
        _palaceZone = state.palaceZone;
        MapBox world = World.world;
        int size = Config.CITY_ZONE_SIZE;
        for (int y = 0; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            WorldTile tile = world.GetTile(_x0 + x, _y0 + y);
            TileZone zone = tile?.zone;
            Color32 color = Clear;
            if (zone != null)
            {
                int lx = tile.x - zone.x * size, ly = tile.y - zone.y * size;
                bool grid = lx == 0 || ly == 0;
                switch (_tool)
                {
                    case Tool.Zone:
                    {
                        int flags = Flags(zone);
                        if ((flags & FlagEditable) != 0) color = (flags & FlagTown) != 0 ? EraseTint : AddTint;
                        if (grid) color = GridLine;
                        break;
                    }
                    case Tool.Palace:
                        if (zone.id == state.palaceZone)
                            color = grid || lx == size - 1 || ly == size - 1 ? PalaceEdge : PalaceFill;
                        else if (zone.city == _city) color = grid ? GridLine : OwnTint;
                        break;
                    case Tool.Axis:
                        if (state.axis && (tile.x == state.axisX || tile.y == state.axisY)) color = AxisLine;
                        break;
                }
            }
            _base[y * _w + x] = color;
        }
    }

    // 这块地能不能改、眼下是不是城区(一次底层重建里每块地只问一次)
    private static int Flags(TileZone zone)
    {
        if (zone == null) return 0;
        if (ZoneFlags.TryGetValue(zone.id, out int flags)) return flags;
        flags = (CityPlanEditing.ZoneEditable(_city, zone) ? FlagEditable : 0) | (CityPlanEditing.InTown(_city, zone) ? FlagTown : 0);
        ZoneFlags[zone.id] = flags;
        return flags;
    }

    private static int HoverKey() => _hoverTile == null ? -1
        : _tool == Tool.Axis ? _hoverTile.x * 65536 + _hoverTile.y : _hoverTile.zone?.id ?? -1;

    private static void DrawHover()
    {
        TileZone zone = _hoverTile?.zone;
        if (zone == null) return;
        switch (_tool)
        {
            case Tool.Zone:
            {
                int flags = Flags(zone);
                bool editable = (flags & FlagEditable) != 0, town = (flags & FlagTown) != 0;
                OutlineZone(zone, !editable ? LockedEdge : town ? EraseEdge : AddEdge, !editable ? LockedFill : town ? EraseFill : AddFill);
                break;
            }
            case Tool.Palace:
                OutlineZone(zone, zone.city == _city ? PalaceEdge : LockedEdge, zone.city == _city ? PalaceHover : LockedFill);
                break;
            case Tool.Axis:
            {
                int hx = _hoverTile.x - _x0, hy = _hoverTile.y - _y0;
                if (hx < 0 || hy < 0 || hx >= _w || hy >= _h) return;
                for (int x = 0; x < _w; x++) _pixels[hy * _w + x] = CrossLine;
                for (int y = 0; y < _h; y++) _pixels[y * _w + hx] = CrossLine;
                _pixels[hy * _w + hx] = CrossCenter;
                break;
            }
        }
    }

    // 一块地描边、铺底
    private static void OutlineZone(TileZone zone, Color32 edge, Color32 fill)
    {
        if (zone?.tiles == null) return;
        int size = Config.CITY_ZONE_SIZE;
        foreach (WorldTile tile in zone.tiles)
        {
            if (tile == null) continue;
            int x = tile.x - _x0, y = tile.y - _y0;
            if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
            int lx = tile.x - zone.x * size, ly = tile.y - zone.y * size;
            _pixels[y * _w + x] = lx == 0 || ly == 0 || lx == size - 1 || ly == size - 1 ? edge : fill;
        }
    }

    private static void UpdateTip()
    {
        string text = _dragButton < 0 && _editable ? TipText() : null;
        if (string.IsNullOrEmpty(text))
        {
            if (_tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
            return;
        }
        if (_tipText.text != text) _tipText.text = text;
        if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Input.mousePosition, null, out Vector2 local)) return;
        // 靠右边时提示翻到光标左边
        bool flip = local.x > _canvasRect.rect.xMax - 280f;
        _tip.pivot = new Vector2(flip ? 1f : 0f, 1f);
        _tip.anchoredPosition = local + new Vector2(flip ? -14f : 18f, -18f);
    }

    private static string TipText()
    {
        TileZone zone = _hoverTile?.zone;
        if (zone == null) return null;
        switch (_tool)
        {
            case Tool.Zone:
            {
                int flags = Flags(zone);
                return LM.Get((flags & FlagEditable) == 0 ? "city_plan_ui_tip_locked"
                    : (flags & FlagTown) != 0 ? "city_plan_ui_tip_remove" : "city_plan_ui_tip_add");
            }
            case Tool.Palace:
                return LM.Get(zone.id == _palaceZone ? "city_plan_ui_tip_palace_here"
                    : zone.city == _city ? "city_plan_ui_tip_palace" : "city_plan_ui_tip_not_own");
            case Tool.Axis:
                return LM.Get("city_plan_ui_tip_axis");
            default:
                return null;
        }
    }

    #endregion

    #region 工具与右栏

    private static void SetTool(Tool tool)
    {
        _tool = tool;
        _pressed = false;
        EndPaint();
        _hoverKey = int.MinValue;
        _message = null;
        RefreshToolbar();
    }

    // 各改动返回的提示写进状态栏(null 表示没变)；工具栏跟着刷新，预览等 Revision 变了下一帧重画
    private static void Act(string tip)
    {
        if (!string.IsNullOrEmpty(tip)) Say(tip);
        RefreshToolbar();
    }

    private static void Say(string text)
    {
        _message = text;
        _messageAt = Time.unscaledTime;
    }

    // 交还规矩：五秒内再点一次才执行(同营宫殿的二次确认)
    private static void ClickReset()
    {
        if (_city == null) return;
        if (_resetArmedAt > 0f && Time.unscaledTime - _resetArmedAt <= ConfirmSeconds)
        {
            _resetArmedAt = -100f;
            CityPlanEditing.Reset(_city);
            Say(LM.Get("city_plan_ui_reset_done"));
        }
        else
        {
            _resetArmedAt = Time.unscaledTime;
            Say(LM.Get("city_plan_ui_reset_confirm"));
        }
        RefreshToolbar();
    }

    private static string OnOff(string key, bool on) =>
        string.Format(LM.Get("city_plan_ui_toggle"), LM.Get(key), LM.Get(on ? "city_plan_ui_on" : "city_plan_ui_off"));

    // 工具栏：能不能改、城墙护城河中轴开没开、坊市哪一档
    private static void RefreshToolbar()
    {
        if (_viewBtn == null || _city == null) return;
        _editable = CityPlanEditing.Editable(_city, out _reason);
        if (!_editable && _tool != Tool.View)
        {
            _tool = Tool.View;
            _pressed = false;
            EndPaint();
        }
        var state = CityPlanEditing.State(_city);
        _palaceZone = state.palaceZone;
        bool armed = _resetArmedAt > 0f && Time.unscaledTime - _resetArmedAt <= ConfirmSeconds;
        _viewBtn.Set(LM.Get("city_plan_ui_tool_view"), _tool == Tool.View ? ActiveColor : ToolColor, true);
        _zoneBtn.Set(LM.Get("city_plan_ui_tool_zone"), _tool == Tool.Zone ? ActiveColor : ToolColor, _editable);
        _palaceBtn.Set(LM.Get("city_plan_ui_tool_palace"), _tool == Tool.Palace ? ActiveColor : ToolColor, _editable);
        _wallsBtn.Set(OnOff("city_plan_ui_walls", state.walls), state.walls ? ToolColor : OffColor, _editable);
        _moatBtn.Set(OnOff("city_plan_ui_moat", state.moat), state.moat ? ToolColor : OffColor, _editable);
        _axisBtn.Set(OnOff("city_plan_ui_tool_axis", state.axis), _tool == Tool.Axis ? ActiveColor : state.axis ? ToolColor : OffColor,
            _editable);
        _blocksBtn.Set(string.Format(LM.Get("city_plan_ui_blocks"), LM.Get("city_plan_ui_blocks_" + Mathf.Clamp(state.blocks, 0, 2))),
            ToolColor, _editable);
        _rerollBtn.Set(LM.Get("city_plan_ui_reroll"), ToolColor, _editable);
        _fitBtn.Set(LM.Get("city_plan_ui_fit"), ViewColor, true);
        _undoBtn.Set(LM.Get("city_plan_ui_undo"), ViewColor, _editable);
        _resetBtn.Set(LM.Get("city_plan_ui_reset"), armed ? ActiveColor : ViewColor, _editable && CityPlanEditing.IsManual(_city));
    }

    private static string ToolHint() => LM.Get(_tool switch
    {
        Tool.Zone => "city_plan_ui_hint_zone",
        Tool.Palace => "city_plan_ui_hint_palace",
        Tool.Axis => "city_plan_ui_hint_axis",
        _ => "city_plan_ui_hint_view"
    });

    // 状态栏：刚返回的提示 > 改不了的原因 > 当前工具怎么用
    private static void UpdateStatus()
    {
        string text = !string.IsNullOrEmpty(_message) && Time.unscaledTime - _messageAt <= MessageSeconds ? _message.ColorString("#F2EEE2")
            : !_editable && !string.IsNullOrEmpty(_reason) ? _reason.ColorString("#E9A85B")
            : ToolHint().ColorString("#8FA0A8");
        if (_status.text != text) _status.text = text;
    }

    private static string PeopleLine(City city) => string.Format(LM.Get("city_window_people"),
        Mathf.RoundToInt(CityPopulationSystem.GetTotal(city)), CityPopulationSystem.Households(city), city.getPopulationMaximum());

    // 标题与右栏文字(每秒一次，营建、城貌按过当场)：取数同城池小窗口(CityStyleWindow.Draw)
    private static void RefreshPanel()
    {
        _panelAt = Time.unscaledTime;
        City city = _city;
        if (city?.data == null || _overview == null) return;
        bool byHand = city.GetOrCreate().map_style_by_hand;
        string family = CityStyleSystem.Established(city);
        (string target, float progress) = CityStyleSystem.Rebuild(city);
        bool winter = PureMapMode.Enabled && family == "nomad" && PureMapPatch.WinterCamp(city);
        _option = CityStyleSystem.OptionOf(city, winter);
        string kingdom = city.kingdom?.GetKingdomName() ?? LM.Get("settlement_unowned");
        _title.text = string.Format(LM.Get("city_plan_ui_title"), city.GetCityName());
        _subtitle.text = string.Format(LM.Get("city_plan_ui_subtitle"), kingdom, CityStyleSystem.StyleName(_option));
        _overviewName.text = city.GetCityName();

        var trades = new List<string>();
        for (int trade = 0; trade < LivelihoodSystem.Count; trade++)
        {
            float shares = LivelihoodSystem.Shares(city, trade);
            if (shares > 0f) trades.Add(LivelihoodSystem.Name(trade) + " " + shares.ToString("0"));
        }
        _styleLine = string.Format(LM.Get(byHand ? "city_style_now_hand" : "city_style_now_culture"), CityStyleSystem.StyleName(_option));
        var lines = new List<string>
        {
            kingdom.ColorString("#7FD8EA"),
            PeopleLine(city).ColorString("#65D66E"),
            string.Format(LM.Get("city_window_houses"), city.countBuildingsType("type_house", false), city.status?.houses_max ?? 0,
                Mathf.RoundToInt(CityConstructionSystem.Wealth(city) * 100f)).ColorString("#F2EEE2"),
            (trades.Count == 0 ? LM.Get("city_window_trades_none")
                : string.Format(LM.Get("city_window_trades"), string.Join(LM.Get("city_window_trades_separator"), trades))).ColorString("#7FD8EA"),
            _styleLine.ColorString("#65D66E")
        };
        if (target != null)
            lines.Add(string.Format(LM.Get("city_style_rebuilding"), CityStyleSystem.StyleName(target), Mathf.RoundToInt(progress * 100f))
                .ColorString("#E9A85B"));
        lines.Add(string.Format(LM.Get("city_style_culture"), CityStyleSystem.StyleName(CityStyleSystem.CurrentFamily(city)))
            .ColorString("#8FA0A8"));
        lines.Add(CityPlanEditing.Summary(city).ColorString("#E9A85B"));
        _overview.text = string.Join("\n", lines);

        foreach ((string option, Btn button) in StyleButtons)
        {
            bool current = option == _option;
            string text = CityStyleSystem.StyleName(option);
            button.Set(current ? "· " + text + " ·" : text, current ? ActiveColor : ToolColor, true);
        }
    }

    // 营建、城貌动了城里的房子与画法：右栏当场刷新，全图下一帧重画
    private static void AfterWorks()
    {
        _renderDue = true;
        RefreshPanel();
    }

    private static void Flourish()
    {
        if (_city == null) return;
        (int built, int upgraded) = CityConstructionSystem.Flourish(_city);
        Say(built + upgraded == 0 ? LM.Get("city_flourish_full") : string.Format(LM.Get("city_flourish_done"), built, upgraded));
        AfterWorks();
    }

    private static void People(bool add)
    {
        if (_city == null) return;
        EmpireCraft.Scripts.GodPowers.CityResourcePowers.ChangePopulation(_city, add);
        Say(PeopleLine(_city));
        AfterWorks();
    }

    // 营宫殿：五秒内再点一次才营建(确认在 CityPlacementSystem.BuildPalace 里)
    private static void BuildPalace()
    {
        if (_city == null) return;
        string tip = CityPlacementSystem.BuildPalace(_city.getTile());
        if (!string.IsNullOrEmpty(tip)) Say(tip);
        AfterWorks();
    }

    // 换城貌：option 为空是「照现文化」
    private static void Restyle(string option)
    {
        if (_city == null) return;
        if (option == null) CityStyleSystem.FollowCulture(_city);
        else CityStyleSystem.SetByHand(_city, option);
        AfterWorks();
        Say(_styleLine);
    }

    #endregion
}

// 城池全图的每帧输入(Esc、滚轮、拖动、悬停)与定时刷新，挂在全图画布上，画布关掉随之销毁
public sealed class CityPlanScreenHost : MonoBehaviour
{
    private void Update() => CityPlanScreen.Tick(this);
}
