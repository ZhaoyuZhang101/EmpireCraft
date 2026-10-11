using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 独立的"城市文化地名历史"窗口——之前(Phase 13)这套改名+历史列表是直接塞进城市自己的
// "设置"标签页内容里的，用户明确要求挪出来单独开一个窗口，图标点开，样式跟法理窗口的
// "文化地名历史"标签页（KingdomTitleWindow.BuildNameHistorySection 等）保持同一套规范：
// AutoLayoutWindow<T> 独立窗口 + 文化标签/可编辑输入框/删除按钮的一行一条样式。
public class CityNameHistoryWindow : AutoLayoutWindow<CityNameHistoryWindow>
{
    private static readonly Color SectionColor = new Color(0.7f, 0.9f, 1f);
    private static readonly Color GoldColor = new Color(1f, 0.78f, 0.28f);

    public City city { get; set; }
    private AutoVertLayoutGroup _topPart;
    private AutoVertLayoutGroup _content;
    private ScrollRect _cachedScrollRect;

    // Phase 16.6：跟 KingdomTitleWindow._manualNameHistoryCultures 同一个道理——玩家手动
    // 点了"添加文化"但还没输入名字的候选文化，RecordCityCulturalName 对空字符串直接无视，
    // 所以只能靠这个纯 UI 层的瞬时集合先占位，只在切换选中城市（OnNormalEnable）时重置。
    private HashSet<string> _manualNameHistoryCultures = new HashSet<string>();

    protected override void Init()
    {
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 60, 3);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 60, 3);
        city = SelectedMetas.selected_city;
        _manualNameHistoryCultures = new HashSet<string>();
        Clear();
        if (city == null || city.isRekt() || city.data == null) return;
        BuildContent();
    }

    private void Clear()
    {
        if (_topPart != null)
        {
            Destroy(_topPart.gameObject);
            _topPart = null;
        }
        if (_content != null)
        {
            Destroy(_content.gameObject);
            _content = null;
        }
    }

    private void BuildContent()
    {
        BuildIdentityCard();
        _content = this.BeginVertGroup(pSpacing: 4, pAlignment: TextAnchor.UpperCenter);
        AddChild(_content.gameObject);
        BuildNameHistorySection();
    }

    private void BuildIdentityCard()
    {
        _topPart = this.BeginVertGroup(pSize: new Vector2(205, 30), pSpacing: 2,
            pAlignment: TextAnchor.MiddleCenter);
        HoverMarqueeText.Attach(_topPart.AddTextIntoVertLayout(
            (city.GetCityName() ?? LM.Get("label_none")).ColorString(pColor: GoldColor), true,
            TextAnchor.MiddleCenter, new Vector2(190, 16)));
        _topPart.transform.AddStretchBackground("clanFrame", new Vector2(205, 30));
        _topPart.gameObject.AdjustTopPart(transform.parent.transform, new Vector2(0, 1));
        RectTransform topRect = _topPart.GetComponent<RectTransform>();
        topRect.sizeDelta = new Vector2(205, 30);
        LayoutRebuilder.ForceRebuildLayoutImmediate(topRect);
    }

    // 按文化列出这座城市历史上用过的所有名字（包括还没被玩家手动改过、但确实是这座城市
    // 当前主流文化对应的那一条——不然一座从没转化过文化的城市，连自己现在这个名字对应
    // 哪个文化都看不出来，也没法从这里编辑），每条都能直接改名，也能删掉某个文化的记录。
    private void BuildNameHistorySection()
    {
        AddSectionTitle(LM.Get("city_name_history_title"));
        _content.AddTextIntoVertLayout(LM.Get("city_name_history_hint"), true,
            TextAnchor.MiddleCenter, new Vector2(190, 22));

        Dictionary<string, string> history = city.GetOrCreate().name_history;
        string mainCulture = CultureService.GetMainCulture(city);
        // Phase 16.6：直接复用 CultureService.GetCityCultureShares(city)——城市本来就已经有
        // 这份现成的占比数据，不用另外再算一遍。占比明显大于零但还没被玩家手动改过名字的
        // 文化也要露面，标成"未成为主流文化"，玩家可以先预设名字，等这个文化真的变成主流
        // 文化（城市改名决议触发）时就会自动用上。
        Dictionary<string, float> shares = CultureService.GetCityCultureShares(city);
        List<string> cultures = (history?.Keys ?? Enumerable.Empty<string>())
            .Concat(shares.Where(pair => pair.Value > 0.01f).Select(pair => pair.Key))
            .Concat(_manualNameHistoryCultures)
            .Where(CultureService.IsValidCulture)
            .Distinct()
            .ToList();
        if (CultureService.IsValidCulture(mainCulture) && !cultures.Contains(mainCulture))
            cultures.Add(mainCulture);
        cultures = cultures
            .OrderByDescending(culture => string.Equals(culture, mainCulture, StringComparison.Ordinal))
            .ThenByDescending(culture => shares.TryGetValue(culture, out float share) ? share : 0f)
            .ThenBy(culture => culture.GetCultureTranslate())
            .ToList();

        if (cultures.Count == 0)
        {
            _content.AddTextIntoVertLayout(LM.Get("label_none"), true, TextAnchor.MiddleCenter,
                new Vector2(100, 12));
        }
        else
        {
            foreach (string culture in cultures)
            {
                bool isCurrent = string.Equals(culture, mainCulture, StringComparison.Ordinal);
                float sharePercent = shares.TryGetValue(culture, out float share) ? share : 0f;
                string name = history != null && history.TryGetValue(culture, out string historic)
                    ? historic
                    : (isCurrent ? city.data.name : "");
                // 当前主流文化对应的名字之前只是临时拿城市现名顶上显示，并没有真的写进
                // name_history——玩家不手动编辑的话，这条记录其实并不存在。现在第一次
                // 展示的时候就直接持久化，这样文化来回切换时才能可靠地恢复这个名字。
                // 非当前文化（候选/手动添加）不借用现在的真实名字冒充，留空等玩家自己输入。
                if (isCurrent && (history == null || !history.ContainsKey(culture)) &&
                    !string.IsNullOrWhiteSpace(name))
                    CultureService.RecordCityCulturalName(city, culture, name);
                AddNameHistoryRow(culture, name, isCurrent, sharePercent);
            }
        }

        // Phase 16.6：玩家也可以手动把一个到目前为止完全没出现过（占比为 0）的文化加进
        // 这个列表——直接复用 KingdomTitleWindow.BuildCultureSection 里选文化用的那一套
        // 调色板网格按钮样式，只列出还没在上面出现过的文化。
        List<string> addableCultures = CultureService.CultureKeys.Where(c => !cultures.Contains(c)).ToList();
        if (addableCultures.Count > 0)
        {
            AddSectionTitle(LM.Get("city_name_history_add_culture"));
            AutoGridLayoutGroup addGrid = _content.BeginGridGroup(4, GridLayoutGroup.Constraint.FixedColumnCount,
                pCellSize: new Vector2(48, 16), pSpacing: new Vector2(2, 2));
            foreach (string culture in addableCultures)
            {
                string cultureKey = culture;
                addGrid.AddButtonIntoGirdLayout($"add_city_name_history_{culture}", culture.GetCultureTranslate(),
                    () => AddManualNameHistoryCulture(cultureKey), size: new Vector2(48, 16));
            }
        }
    }

    private void AddManualNameHistoryCulture(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return;
        _manualNameHistoryCultures.Add(culture);
        RefreshContent();
    }

    private void AddNameHistoryRow(string culture, string name, bool isCurrent, float sharePercent)
    {
        const float cardHeight = 22 + 12 + 2;
        var card = _content.BeginVertGroup(pSize: new Vector2(200, cardHeight), pSpacing: 1,
            pAlignment: TextAnchor.MiddleCenter);

        string shareLabel = $"{LM.Get("city_name_history_culture_share")}: {sharePercent:0.#}%";
        if (!isCurrent)
            shareLabel += $" · {LM.Get("city_name_history_not_mainstream_culture").ColorString(pColor: Color.gray)}";
        var shareText = card.AddTextIntoVertLayout(shareLabel, true, TextAnchor.MiddleCenter, new Vector2(190, 11));
        shareText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);

        var row = card.BeginHoriGroup(pSize: new Vector2(196, 22), pSpacing: 2,
            pAlignment: TextAnchor.MiddleCenter);
        string label = culture.GetCultureTranslate();
        if (isCurrent) label = label.ColorString(pColor: GoldColor);
        var labelText = row.AddTextIntoHoriLayout(label, true, TextAnchor.MiddleLeft, new Vector2(46, 18));
        labelText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        TextInput input = UnityEngine.Object.Instantiate(TextInput.Prefab, null);
        input.Setup(name ?? "", value => NameHistoryChange(culture, value));
        input.SetSize(new Vector2(112, 18));
        row.AddChild(input.gameObject);
        row.AddButtonIntoHoriLayout($"delete_city_name_history_{culture}", "",
            () => DeleteNameHistory(culture), SpriteTextureLoader.getSprite("ui/iconRemove"),
            size: new Vector2(18, 18), showTip: true);

        card.transform.AddStretchBackground("FactionFrame", new Vector2(200, cardHeight));
    }

    private void NameHistoryChange(string culture, string value)
    {
        if (city?.data == null || string.IsNullOrWhiteSpace(value)) return;
        CultureService.RecordCityCulturalName(city, culture, value);
        // 如果改的正是城市现在生效的主流文化对应的名字，顺便同步一下城市当前显示的名字，
        // 跟法理窗口 TitleHistoryNameChange/ProvinceHistoryNameChange 的做法保持一致。
        if (string.Equals(CultureService.GetMainCulture(city), culture, StringComparison.Ordinal))
            city.setName(value);
        RefreshContent();
    }

    private void DeleteNameHistory(string culture)
    {
        if (city?.data == null) return;
        CultureService.RemoveCityCulturalName(city, culture);
        _manualNameHistoryCultures.Remove(culture);
        RefreshContent();
    }

    private void RefreshContent()
    {
        ScrollRect scrollRect = GetOwningScrollRect();
        float? savedAnchoredY = null;
        if (scrollRect != null && scrollRect.content != null)
            savedAnchoredY = scrollRect.content.anchoredPosition.y;

        Clear();
        if (city == null || city.isRekt() || city.data == null) return;
        BuildContent();

        if (scrollRect != null && savedAnchoredY.HasValue)
            RestoreScrollPosition(scrollRect, savedAnchoredY.Value);
    }

    private ScrollRect GetOwningScrollRect()
    {
        if (_cachedScrollRect == null)
            _cachedScrollRect = GetComponentInParent<ScrollRect>();
        return _cachedScrollRect;
    }

    private static void RestoreScrollPosition(ScrollRect scrollRect, float anchoredY)
    {
        if (scrollRect?.content == null) return;
        RectTransform viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.GetComponent<RectTransform>();
        float viewportHeight = viewport != null ? viewport.rect.height : 0f;
        float maxY = Mathf.Max(0f, scrollRect.content.rect.height - viewportHeight);
        Vector2 pos = scrollRect.content.anchoredPosition;
        pos.y = Mathf.Clamp(anchoredY, 0f, maxY);
        scrollRect.content.anchoredPosition = pos;
    }

    private void AddSectionTitle(string text)
    {
        _content.AddTextIntoVertLayout(text.ColorString(pColor: SectionColor), true,
            TextAnchor.MiddleCenter, new Vector2(150, 13));
    }
}
