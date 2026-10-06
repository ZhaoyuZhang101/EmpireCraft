using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 领土铭牌字体(见 TerritoryFontSettings)：自动(传统书体优先) / 游戏字体 / 本机已安装的任一字体。
// 传统书体按隶书、篆书、草书、行书、魏碑、楷书、宋明体分类列在前面，并用该字体本身显示预览。
public class TerritoryFontWindow : AutoLayoutWindow<TerritoryFontWindow>
{
    private const float Width = 190f;
    private const int MaxOtherFonts = 200;
    private readonly List<GameObject> _groups = new();

    protected override void Init()
    {
        layout.spacing = 2;
        layout.padding = new RectOffset(4, 4, 6, 4);
        layout.childAlignment = TextAnchor.UpperCenter;
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (GameObject group in _groups)
            if (group != null) Destroy(group);
        _groups.Clear();

        var panel = this.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.UpperCenter);
        _groups.Add(panel.gameObject);

        string choice = TerritoryFontSettings.Choice;
        string current = choice == TerritoryFontSettings.Auto
            ? string.Format(LM.Get("territory_font_auto_current"),
                TerritoryFontSettings.ResolveFontName() ?? LM.Get("territory_font_game"))
            : choice == TerritoryFontSettings.Game
                ? LM.Get("territory_font_game")
                : choice + (TerritoryFontSettings.IsInstalled(choice) ? "" : LM.Get("territory_font_missing"));
        AddLine(panel, string.Format(LM.Get("territory_font_current"), current).ColorString("#F3C34A"), 8, 12f);
        AddLine(panel, LM.Get("territory_font_hint").ColorString("#A8B8BE"), 6, 30f, HorizontalWrapMode.Wrap);

        var modes = panel.BeginHoriGroup(new Vector2(Width, 13f), TextAnchor.MiddleCenter, 3);
        modes.AddButtonIntoHoriLayout("territory_font_auto", Highlight(LM.Get("territory_font_auto"),
            choice == TerritoryFontSettings.Auto), () => Choose(TerritoryFontSettings.Auto), size: new Vector2(62f, 12f));
        modes.AddButtonIntoHoriLayout("territory_font_game", Highlight(LM.Get("territory_font_game"),
            choice == TerritoryFontSettings.Game), () => Choose(TerritoryFontSettings.Game), size: new Vector2(62f, 12f));
        modes.AddButtonIntoHoriLayout("territory_font_rescan", LM.Get("territory_font_rescan"), () =>
        {
            TerritoryFontSettings.Rescan();
            Rebuild();
        }, size: new Vector2(56f, 12f));

        // 传统书体：用字体本身显示预览
        List<(string name, string styleKey)> traditional = TerritoryFontSettings.TraditionalFonts();
        AddLine(panel, string.Format(LM.Get("territory_font_traditional"), traditional.Count).ColorString("#7FD8EA"), 7, 11f);
        if (traditional.Count == 0)
            AddLine(panel, LM.Get("territory_font_none_traditional").ColorString("#8FA0A8"), 6, 10f);
        string preview = LM.Get("territory_font_preview");
        foreach ((string name, string styleKey) in traditional)
        {
            string fontName = name;
            AdvancedButton button = panel.AddButtonIntoVertLayout($"territory_font_{fontName}",
                Highlight($"{preview}  {LM.Get(styleKey)} · {fontName}", choice == fontName),
                () => Choose(fontName), size: new Vector2(Width, 16f));
            Font font = TerritoryLabelRenderer.GetOsFont(fontName);
            Text text = button.GetComponentInChildren<Text>();
            if (font != null && text != null) text.font = font;
        }

        // 其他已安装字体
        List<string> others = TerritoryFontSettings.OtherFonts();
        AddLine(panel, string.Format(LM.Get("territory_font_others"), others.Count).ColorString("#7FD8EA"), 7, 11f);
        foreach (string name in others.Take(MaxOtherFonts))
        {
            string fontName = name;
            panel.AddButtonIntoVertLayout($"territory_font_{fontName}", Highlight(fontName, choice == fontName),
                () => Choose(fontName), size: new Vector2(Width, 11f));
        }
        if (others.Count > MaxOtherFonts)
            AddLine(panel, string.Format(LM.Get("territory_font_more"), others.Count - MaxOtherFonts)
                .ColorString("#8FA0A8"), 6, 10f);
    }

    private void Choose(string choice)
    {
        TerritoryFontSettings.Choice = choice;
        Rebuild();
    }

    private static string Highlight(string text, bool selected) =>
        selected ? text.ColorString("#F3C34A") : text;

    private static void AddLine(AutoVertLayoutGroup panel, string text, int fontSize, float height,
        HorizontalWrapMode wrap = HorizontalWrapMode.Overflow)
    {
        SimpleText line = panel.AddTextIntoVertLayout(text, true, TextAnchor.MiddleCenter, new Vector2(Width, height),
            mode: wrap);
        line.UseFixedFontSize(fontSize, wrap);
    }
}
