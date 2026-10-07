using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

public class TerritoryFontWindow : AutoLayoutWindow<TerritoryFontWindow>
{
    private const float Width = 204f;
    private const int PageSize = 8;
    private readonly List<GameObject> _groups = new();
    private string _search = "";
    private int _page;
    // 点字体卡片是设主字体还是设兜底字体
    private bool _pickFallback;

    protected override void Init()
    {
        layout.spacing = 4;
        layout.padding = new RectOffset(6, 6, 8, 8);
        layout.childAlignment = TextAnchor.UpperCenter;
        TextInput search = Instantiate(TextInput.Prefab);
        search.Setup(LM.Get("territory_font_search"), value =>
        {
            _search = value == LM.Get("territory_font_search") ? "" : (value ?? "").Trim();
            _page = 0;
            Rebuild();
        });
        search.SetSize(new Vector2(Width, 18f));
        AddChild(search.gameObject);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (GameObject group in _groups)
            if (group != null) { group.SetActive(false); Destroy(group); }
        _groups.Clear();
        var panel = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
        _groups.Add(panel.gameObject);
        string choice = TerritoryFontSettings.Choice;
        string current = choice == TerritoryFontSettings.Game ? LM.Get("territory_font_game")
            : BundledTerritoryFonts.DisplayName(TerritoryFontSettings.ResolveFontName()) ?? LM.Get("territory_font_game");
        AddLine(panel, LM.Get("territory_font_current").Replace("{0}", current), 8, 16f, "#D5B982");
        AddLine(panel, LM.Get("territory_font_hint"), 6, 28f, "#ADB3B8", HorizontalWrapMode.Wrap);
        var modes = panel.BeginHoriGroup(new Vector2(Width, 18f), TextAnchor.MiddleCenter, 4);
        Mode(modes, "territory_font_auto", choice == TerritoryFontSettings.Auto, () => Choose(TerritoryFontSettings.Auto));
        Mode(modes, "territory_font_game", choice == TerritoryFontSettings.Game, () => Choose(TerritoryFontSettings.Game));
        Mode(modes, "territory_font_rescan", false, () => { TerritoryFontSettings.Rescan(); Rebuild(); });
        // 华夏国号用大篆(不带后缀)
        bool seal = TerritoryFontSettings.SealHuaxiaNames;
        AdvancedButton sealToggle = panel.AddButtonIntoVertLayout("territory_font_seal_huaxia",
            LM.Get(seal ? "territory_font_seal_huaxia_on" : "territory_font_seal_huaxia_off"),
            () => { TerritoryFontSettings.SealHuaxiaNames = !TerritoryFontSettings.SealHuaxiaNames; Rebuild(); },
            size: new Vector2(Width, 17f));
        sealToggle.Background.color = seal ? new Color(0.38f, 0.30f, 0.18f) : new Color(0.17f, 0.19f, 0.21f);
        sealToggle.Text.fontSize = 7;
        if (!BundledTerritoryFonts.Available)
            AddLine(panel, LM.Get("territory_font_seal_missing"), 6, 14f, "#E07A6A");
        // 兜底字体：默认隶书，可点"选兜底"后再点字体卡片另选
        string fallback = TerritoryFontSettings.FallbackChoice;
        string fallbackName = fallback == TerritoryFontSettings.FallbackClerical ? LM.Get("territory_font_fallback_clerical")
            : fallback == TerritoryFontSettings.Game ? LM.Get("territory_font_game")
            : BundledTerritoryFonts.DisplayName(fallback);
        AddLine(panel, LM.Get("territory_font_fallback_current").Replace("{0}", fallbackName), 7, 13f, "#D5B982");
        var fallbackRow = panel.BeginHoriGroup(new Vector2(Width, 18f), TextAnchor.MiddleCenter, 4);
        Mode(fallbackRow, "territory_font_fallback_pick", _pickFallback, () => { _pickFallback = !_pickFallback; Rebuild(); });
        Mode(fallbackRow, "territory_font_fallback_clerical", fallback == TerritoryFontSettings.FallbackClerical,
            () => { TerritoryFontSettings.FallbackChoice = TerritoryFontSettings.FallbackClerical; _pickFallback = false; Rebuild(); });
        Mode(fallbackRow, "territory_font_game", fallback == TerritoryFontSettings.Game,
            () => { TerritoryFontSettings.FallbackChoice = TerritoryFontSettings.Game; _pickFallback = false; Rebuild(); });
        if (_pickFallback) AddLine(panel, LM.Get("territory_font_fallback_pick_hint"), 6, 13f, "#E2C796");
        else AddLine(panel, LM.Get("territory_font_seal_install_hint"), 6, 22f, "#ADB3B8", HorizontalWrapMode.Wrap);

        var fonts = TerritoryFontSettings.TraditionalFonts()
            .Select(item => (item.name, item.styleKey)).Concat(TerritoryFontSettings.OtherFonts()
                .Select(name => (name, styleKey: "territory_font_style_other")))
            .Where(item => string.IsNullOrEmpty(_search) ||
                (BundledTerritoryFonts.DisplayName(item.name) + " " + item.name + " " + LM.Get(item.styleKey))
                    .IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        int pages = Math.Max(1, (fonts.Count + PageSize - 1) / PageSize);
        _page = Math.Max(0, Math.Min(_page, pages - 1));
        AddLine(panel, string.Format(LM.Get("territory_font_results"), fonts.Count, _page + 1, pages), 7, 13f, "#8FA0A8");
        string previousStyle = null;
        foreach ((string name, string styleKey) in fonts.Skip(_page * PageSize).Take(PageSize))
        {
            if (previousStyle != styleKey)
            {
                AddLine(panel, LM.Get(styleKey), 7, 12f, "#D5B982");
                previousStyle = styleKey;
            }
            string fontName = name;
            bool selected = choice == name;
            var card = panel.BeginVertGroup(new Vector2(Width, 38f), pSpacing: 1, pAlignment: TextAnchor.MiddleCenter);
            AddLine(card, BundledTerritoryFonts.DisplayName(name) + (selected ? "  " + LM.Get("territory_font_selected") : ""),
                7, 12f, selected ? "#E2C796" : "#BBC0C5");
            AdvancedButton button = card.AddButtonIntoVertLayout("territory_font_" + name,
                LM.Get("territory_font_preview"), () => Choose(fontName), size: new Vector2(Width, 24f));
            button.Background.color = selected ? new Color(0.32f, 0.26f, 0.18f, 0.9f) : new Color(0.13f, 0.15f, 0.17f, 0.9f);
            Text text = button.GetComponentInChildren<Text>();
            Font font = TerritoryLabelRenderer.GetOsFont(name);
            if (text != null)
            {
                BundledFont bundled = BundledTerritoryFonts.Of(font);
                string shown = text.text;
                // 繁体篆书先把预览字转成繁体；缺字或画不出来(空白)时用隶书预览
                bool usable = bundled != null
                    ? bundled.TryAdapt(text.text, false, out shown)
                    : font != null && text.text.Where(c => !char.IsWhiteSpace(c)).All(font.HasCharacter);
                if (usable && TerritoryLabelRenderer.CanRender(font, shown, false))
                {
                    text.text = shown;
                    text.font = font;
                }
                else
                {
                    Font clerical = TerritoryLabelRenderer.ClericalFallback(text.text, false, out string fallbackText);
                    if (clerical != null)
                    {
                        text.text = fallbackText;
                        text.font = clerical;
                    }
                }
                text.material = null;
                text.fontSize = 12;
                text.resizeTextForBestFit = false;
                text.supportRichText = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.color = selected ? new Color32(226, 199, 150, 255) : new Color32(215, 219, 223, 255);
            }
        }
        if (fonts.Count == 0) AddLine(panel, LM.Get("territory_font_no_results"), 7, 20f, "#ADB3B8");
        if (pages > 1)
        {
            var paging = panel.BeginHoriGroup(new Vector2(Width, 18f), TextAnchor.MiddleCenter, 8);
            var prev = paging.AddButtonIntoHoriLayout("font_prev", LM.Get("territory_font_previous"),
                () => { _page--; Rebuild(); }, size: new Vector2(90f, 16f));
            var next = paging.AddButtonIntoHoriLayout("font_next", LM.Get("territory_font_next"),
                () => { _page++; Rebuild(); }, size: new Vector2(90f, 16f));
            prev.Button.interactable = _page > 0;
            next.Button.interactable = _page + 1 < pages;
        }
    }

    private void Choose(string choice)
    {
        // 选兜底模式下点字体卡片：设为兜底字体
        if (_pickFallback && choice != TerritoryFontSettings.Auto && choice != TerritoryFontSettings.Game)
        {
            TerritoryFontSettings.FallbackChoice = choice;
            _pickFallback = false;
        }
        else TerritoryFontSettings.Choice = choice;
        Rebuild();
    }

    private static void Mode(AutoHoriLayoutGroup row, string key, bool selected, UnityEngine.Events.UnityAction action)
    {
        AdvancedButton button = row.AddButtonIntoHoriLayout(key, LM.Get(key), action, size: new Vector2(64f, 17f));
        button.Background.color = selected ? new Color(0.38f, 0.30f, 0.18f) : new Color(0.17f, 0.19f, 0.21f);
        button.Text.fontSize = 7;
    }

    private static void AddLine(AutoVertLayoutGroup panel, string text, int fontSize, float height, string color,
        HorizontalWrapMode wrap = HorizontalWrapMode.Overflow)
    {
        SimpleText line = panel.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter, new Vector2(Width, height), mode: wrap);
        line.background.enabled = false;
        line.text.text = text;
        ColorUtility.TryParseHtmlString(color, out Color tint);
        line.text.color = tint;
        line.UseFixedFontSize(fontSize, wrap);
    }
}
