using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 通用的自定义窗口：照搬原版"自定义王国"窗口(旗帜预览 + 底纹/纹章左右切换 + 原版 ColorElement 色块 + 随机)。
// 用于没有原版自定义窗口可借的对象(法理头衔等)；帝国直接借原版窗口，见 EmpireColorPatch。
public class MetaColorWindow : AutoLayoutWindow<MetaColorWindow>
{
    // 旗帜部分：不传则窗口只有颜色区
    public sealed class BannerSpec
    {
        public Func<Sprite> background;
        public Func<Sprite> icon;
        public Func<int> background_get;
        public Action<int> background_set;
        public Func<int> background_count;
        public Func<int> icon_get;
        public Action<int> icon_set;
        public Func<int> icon_count;
    }

    private const int Columns = 10;
    private static readonly Vector2 CellSize = new(14, 14);
    private const float PreviewSize = 36f;

    private static string _titleKey;
    private static Func<ColorLibrary> _library;
    private static Func<int> _get;
    private static Action<int> _set;
    private static BannerSpec _banner;

    private static ColorElement _prefab;
    private readonly List<ColorElement> _elements = new();
    private AutoVertLayoutGroup _root;
    private SimpleText _header;
    private SimpleText _backgroundCounter;
    private SimpleText _iconCounter;
    private Image _previewBackground;
    private Image _previewIcon;
    private MetaCustomizationAsset _tooltipAsset;

    public static void Open(string titleKey, Func<ColorLibrary> library, Func<int> get, Action<int> set,
        BannerSpec banner = null)
    {
        _titleKey = titleKey;
        _library = library;
        _get = get;
        _set = set;
        _banner = banner;
        ScrollWindow.showWindow(nameof(MetaColorWindow));
    }

    // 原版色块预制体挂在"自定义王国"窗口的 CustomizeWindow 上；只读取预制体，不实例化窗口本身
    private static ColorElement Prefab
    {
        get
        {
            if (_prefab != null) return _prefab;
            ScrollWindow window = Resources.Load<ScrollWindow>("windows/kingdom_customize");
            _prefab = window != null ? window.GetComponentInChildren<CustomizeWindow>(true)?.color_element_prefab : null;
            return _prefab;
        }
    }

    protected override void Init()
    {
        layout.spacing = 4;
        layout.padding = new RectOffset(4, 4, 6, 4);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        Rebuild();
    }

    private void Rebuild()
    {
        if (_root != null) Destroy(_root.gameObject);
        _root = null;
        _header = _backgroundCounter = _iconCounter = null;
        _previewBackground = _previewIcon = null;
        _elements.Clear();
        ColorLibrary library = _library?.Invoke();
        if (library == null || _get == null || _set == null || Prefab == null) return;

        _root = this.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter);
        AddChild(_root.gameObject);

        if (_banner != null) BuildBanner();

        _tooltipAsset = new MetaCustomizationAsset
        {
            color_locale = _titleKey,
            color_count = () => library.list.Count
        };
        _header = NewText(new Vector2(170, 16));
        _root.AddChild(_header.gameObject);

        AutoGridLayoutGroup grid = _root.BeginGridGroup(Columns, GridLayoutGroup.Constraint.FixedColumnCount,
            pCellSize: CellSize, pSpacing: new Vector2(2, 2));
        for (int i = 0; i < library.list.Count; i++)
        {
            int index = i;
            ColorAsset color = library.getColorByIndex(index);
            ColorElement element = Instantiate(Prefab, grid.transform);
            element.transform.localScale = Vector3.one;
            element.setColor(color.getColorMainSecond(), color.getColorBorderInsideAlpha32());
            element.index = index;
            element.asset = _tooltipAsset;
            element.setAction(() => PickColor(index));
            element.GetComponent<TipButton>()?.setHoverAction(element.showTooltip, pAddAnimation: false);
            _elements.Add(element);
        }
        _root.AddChild(grid.gameObject);

        SimpleButton random = Instantiate(SimpleButton.Prefab, null);
        random.Setup(Randomize, null, LM.Get("color_random"), new Vector2(50, 16));
        _root.AddChild(random.gameObject);
        Refresh();
    }

    // 原版布局：左边旗帜预览，右边"底纹""纹章"两行 < 计数 >
    private void BuildBanner()
    {
        AutoHoriLayoutGroup row = _root.BeginHoriGroup(pSpacing: 6, pAlignment: TextAnchor.MiddleCenter);
        _root.AddChild(row.gameObject);

        GameObject preview = new("BannerPreview", typeof(RectTransform), typeof(LayoutElement));
        LayoutElement previewLayout = preview.GetComponent<LayoutElement>();
        previewLayout.minWidth = previewLayout.preferredWidth = PreviewSize;
        previewLayout.minHeight = previewLayout.preferredHeight = PreviewSize;
        _previewBackground = StretchImage(preview.transform, "Background");
        _previewIcon = StretchImage(preview.transform, "Icon");
        row.AddChild(preview);

        AutoVertLayoutGroup options = row.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.MiddleCenter);
        row.AddChild(options.gameObject);
        _backgroundCounter = AddOptionRow(options, step => Step(_banner.background_get, _banner.background_set,
            _banner.background_count, step));
        _iconCounter = AddOptionRow(options, step => Step(_banner.icon_get, _banner.icon_set,
            _banner.icon_count, step));
    }

    private SimpleText AddOptionRow(AutoVertLayoutGroup parent, Action<int> step)
    {
        AutoHoriLayoutGroup line = parent.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleCenter);
        parent.AddChild(line.gameObject);
        SimpleButton left = Instantiate(SimpleButton.Prefab, null);
        left.Setup(() => step(-1), null, "<", new Vector2(14, 14));
        line.AddChild(left.gameObject);
        SimpleText counter = NewText(new Vector2(90, 14));
        line.AddChild(counter.gameObject);
        SimpleButton right = Instantiate(SimpleButton.Prefab, null);
        right.Setup(() => step(1), null, ">", new Vector2(14, 14));
        line.AddChild(right.gameObject);
        return counter;
    }

    private static Image StretchImage(Transform parent, string name)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.preserveAspect = true;
        return image;
    }

    private static SimpleText NewText(Vector2 size)
    {
        SimpleText text = Instantiate(SimpleText.Prefab, null);
        text.Setup("", TextAnchor.MiddleCenter, size);
        text.background.enabled = false;
        return text;
    }

    // 按住原版"批量"热键(同原版窗口)一次跳 5 个
    private void Step(Func<int> get, Action<int> set, Func<int> count, int direction)
    {
        int total = count?.Invoke() ?? 0;
        if (total <= 0 || get == null || set == null) return;
        int change = HotkeyLibrary.many_mod.isHolding() ? 5 : 1;
        set(((get() + direction * change) % total + total) % total);
        Refresh();
    }

    private void PickColor(int index)
    {
        _set?.Invoke(index);
        World.world.zone_calculator.dirtyAndClear();
        Refresh();
    }

    private void Randomize()
    {
        if (_banner != null)
        {
            int backgrounds = _banner.background_count?.Invoke() ?? 0;
            int icons = _banner.icon_count?.Invoke() ?? 0;
            if (backgrounds > 0) _banner.background_set?.Invoke(UnityEngine.Random.Range(0, backgrounds));
            if (icons > 0) _banner.icon_set?.Invoke(UnityEngine.Random.Range(0, icons));
        }
        PickColor(UnityEngine.Random.Range(0, _elements.Count));
    }

    private void Refresh()
    {
        int current = _get?.Invoke() ?? -1;
        for (int i = 0; i < _elements.Count; i++) _elements[i].setSelected(i == current);
        _header?.Setup($"{LM.Get(_titleKey)}  {current + 1}/{_elements.Count}", TextAnchor.MiddleCenter,
            new Vector2(170, 16));
        if (_banner == null || _previewBackground == null) return;

        ColorLibrary library = _library?.Invoke();
        ColorAsset color = library != null && current >= 0 && current < library.list.Count
            ? library.list[current] : null;
        _previewBackground.sprite = _banner.background?.Invoke();
        _previewIcon.sprite = _banner.icon?.Invoke();
        _previewBackground.enabled = _previewBackground.sprite != null;
        _previewIcon.enabled = _previewIcon.sprite != null;
        if (color != null)
        {
            _previewBackground.color = color.getColorMainSecond();
            _previewIcon.color = color.getColorBanner();
        }
        _backgroundCounter?.Setup(
            $"{LM.Get("banner_design")} {(_banner.background_get?.Invoke() ?? 0) + 1}/{_banner.background_count?.Invoke() ?? 0}",
            TextAnchor.MiddleCenter, new Vector2(90, 14));
        _iconCounter?.Setup(
            $"{LM.Get("banner_emblem")} {(_banner.icon_get?.Invoke() ?? 0) + 1}/{_banner.icon_count?.Invoke() ?? 0}",
            TextAnchor.MiddleCenter, new Vector2(90, 14));
    }
}
