using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 文化藏书：本文化写出的所有书，按政治/经济/军事/学术/史学/文学分类，时代名著单列一类。
// 书的归属：写书时作者的模组文化记在书的扩展上；这个功能之前写的旧书按作者所在城市现在的文化推断。
// 名著排在最前，其余按新到旧；每页 40 本。鼠标悬停一行显示原版的书籍详情。
public class CultureBooksWindow : AbstractWideWindow<CultureBooksWindow>
{
    private const float PanelWidth = 440f;
    private const int PageSize = 40;
    private const string Landmarks = "landmark";
    private const string All = "all";
    private static readonly string[] Categories =
        { All, Landmarks, "politics", "economy", "military", "science", "history", "literature" };

    private static readonly Dictionary<string, string> TypeCategory = new(StringComparer.Ordinal)
    {
        ["stewardship_manual"] = "politics", ["diplomacy_manual"] = "politics", ["bad_story_about_king"] = "politics",
        ["economy_manual"] = "economy",
        ["warfare_manual"] = "military",
        ["mathbook"] = "science", ["biology_book"] = "science",
        ["history_book"] = "history",
        ["family_story"] = "literature", ["love_story"] = "literature", ["friendship_story"] = "literature",
        ["fable"] = "literature"
    };

    private static string _pendingCulture = "";

    private readonly List<GameObject> _content = new();
    private AutoVertLayoutGroup _root;
    private string _culture = "";
    private string _category = All;
    private int _page;

    public static void Open(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return;
        _pendingCulture = culture;
        ScrollWindow.showWindow(nameof(CultureBooksWindow));
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
        if (_culture != _pendingCulture)
        {
            _category = All;
            _page = 0;
        }
        _culture = _pendingCulture;
        Rebuild();
        StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        Clear();
    }

    private void Clear()
    {
        foreach (GameObject item in _content)
            if (item != null) Destroy(item);
        _content.Clear();
    }

    #region 数据

    public static string GetCultureOf(Book book)
    {
        if (book == null) return "";
        BookExtension.BookExtraData data = ExtensionManager<Book, BookExtension.BookExtraData>.GetOrCreate(book, true);
        if (!string.IsNullOrEmpty(data?.culture)) return data.culture;
        if (!string.IsNullOrEmpty(data?.origin_culture)) return data.origin_culture;
        City city = World.world.cities.get(book.data.author_city_id);
        if (city != null && !city.isRekt()) return CultureService.GetMainCulture(city, false) ?? "";
        Kingdom kingdom = World.world.kingdoms.get(book.data.author_kingdom_id);
        return kingdom != null && !kingdom.isRekt() ? CultureService.GetRealmCulture(kingdom) : "";
    }

    public static string GetCategory(Book book)
    {
        if (book.TryGetLandmark(out BookExtension.BookExtraData data) &&
            LandmarkBookSystem.TryGetBook(data.landmark_id, out LandmarkBookConfig config) &&
            !string.IsNullOrEmpty(config.category))
            return config.category;
        return TypeCategory.TryGetValue(book.data.book_type ?? "", out string category) ? category : "literature";
    }

    private static bool IsLandmark(Book book) => book.TryGetLandmark(out _);

    private List<Book> CultureBooks() => World.world.books
        .Where(book => book != null && !book.isRekt() && GetCultureOf(book) == _culture).ToList();

    #endregion

    #region 界面

    private void Rebuild()
    {
        Clear();
        if (!CultureService.IsValidCulture(_culture) || World.world?.books == null)
        {
            var empty = Section(20f);
            Line(empty, LM.Get("label_none"));
            return;
        }
        List<Book> books = CultureBooks();
        AddHeader(books);
        AddFilters(books);
        AddList(books);
    }

    private void AddHeader(List<Book> books)
    {
        var panel = Section(40f, "FactionFrame_dominate");
        string colorHex = "#" + ColorUtility.ToHtmlStringRGB(_culture.GetCultureColor());
        Line(panel, $"{_culture.GetCultureTranslate().ColorString(colorHex)}  ·  " +
                    LM.Get("culture_books_title").ColorString("#7FD8EA"), 12f, 9);
        Line(panel, string.Format(LM.Get("culture_books_summary"), books.Count, books.Count(IsLandmark),
            books.Sum(book => book.data.times_read)));
    }

    private void AddFilters(List<Book> books)
    {
        var panel = Section(18f);
        var row = panel.BeginHoriGroup(new Vector2(PanelWidth - 8f, 14f), TextAnchor.MiddleCenter, 2);
        foreach (string category in Categories)
        {
            int count = category == All ? books.Count
                : category == Landmarks ? books.Count(IsLandmark)
                : books.Count(book => GetCategory(book) == category);
            string label = $"{LM.Get($"culture_books_category_{category}")} {count}";
            if (category == _category) label = label.ColorString("#F3C34A");
            string captured = category;
            row.AddButtonIntoHoriLayout($"culture_books_filter_{category}", label, () =>
            {
                _category = captured;
                _page = 0;
                Rebuild();
            }, size: new Vector2(52, 12));
        }
    }

    private void AddList(List<Book> books)
    {
        IEnumerable<Book> filtered = _category switch
        {
            All => books,
            Landmarks => books.Where(IsLandmark),
            _ => books.Where(book => GetCategory(book) == _category)
        };
        List<Book> ordered = filtered.OrderByDescending(IsLandmark)
            .ThenByDescending(book => book.data.created_time).ToList();
        int pages = Math.Max(1, (ordered.Count + PageSize - 1) / PageSize);
        _page = Mathf.Clamp(_page, 0, pages - 1);
        List<Book> shown = ordered.Skip(_page * PageSize).Take(PageSize).ToList();

        var panel = Section(18f + Math.Max(1, shown.Count) * 12f + 16f, "FactionFrame");
        if (shown.Count == 0)
        {
            Line(panel, LM.Get("culture_books_empty").ColorString("#8A8F99"));
        }
        foreach (Book book in shown) AddRow(panel, book);

        var nav = panel.BeginHoriGroup(new Vector2(PanelWidth - 8f, 13f), TextAnchor.MiddleCenter, 6);
        nav.AddButtonIntoHoriLayout("culture_books_prev", "◀", () =>
        {
            _page--;
            Rebuild();
        }, size: new Vector2(24, 11));
        Label(nav, string.Format(LM.Get("culture_books_page"), _page + 1, pages), 80f, 7, TextAnchor.MiddleCenter);
        nav.AddButtonIntoHoriLayout("culture_books_next", "▶", () =>
        {
            _page++;
            Rebuild();
        }, size: new Vector2(24, 11));
    }

    private void AddRow(AutoVertLayoutGroup panel, Book book)
    {
        bool landmark = IsLandmark(book);
        var row = panel.BeginHoriGroup(new Vector2(PanelWidth - 10f, 11f), TextAnchor.MiddleLeft, 3);
        string title = (landmark ? "★ " : "") + "《" + book.name + "》";
        Label(row, landmark ? title.ColorString("#FFD273") : title, 150f, 7, TextAnchor.MiddleLeft);
        Label(row, LM.Get($"culture_books_category_{GetCategory(book)}").ColorString("#7FD8EA"), 34f, 6,
            TextAnchor.MiddleCenter);
        string author = book.data.author_name ?? "";
        string place = book.data.author_city_name ?? book.data.author_kingdom_name ?? "";
        Label(row, $"{author} · {place}".ColorString("#B8C6CC"), 140f, 6, TextAnchor.MiddleLeft);
        int years = Math.Max(0, Date.getYearsSince(book.data.created_time));
        Label(row, string.Format(LM.Get("culture_books_row_meta"), book.data.times_read, years), 90f, 6,
            TextAnchor.MiddleRight);

        // 悬停整行显示原版的书籍详情
        GameObject rowObject = row.gameObject;
        Image hit = rowObject.GetComponent<Image>() ?? rowObject.AddComponent<Image>();
        if (hit.sprite == null) hit.color = new Color(0f, 0f, 0f, 0.001f);
        hit.raycastTarget = true;
        TipButton tip = rowObject.GetComponent<TipButton>() ?? rowObject.AddComponent<TipButton>();
        Book target = book;
        tip.hoverAction = () => Tooltip.show(rowObject, "book", new TooltipData { book = target });
        tip.enabled = true;
    }

    private AutoVertLayoutGroup Section(float height, string frame = null)
    {
        var panel = _root.BeginVertGroup(new Vector2(PanelWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(4, 4, 3, 3));
        _content.Add(panel.gameObject);
        if (frame != null) panel.transform.AddStretchBackground(frame, new Vector2(PanelWidth, height));
        return panel;
    }

    private static void Line(AutoVertLayoutGroup panel, string text, float height = 11f, int fontSize = 7)
    {
        SimpleText line = panel.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter,
            new Vector2(PanelWidth - 10f, height));
        line.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
    }

    private static void Label(AutoHoriLayoutGroup row, string text, float width, int fontSize, TextAnchor anchor)
    {
        SimpleText label = row.AddTextIntoHoriLayout(text, true, anchor, new Vector2(width, 11f));
        label.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
    }

    #endregion
}
