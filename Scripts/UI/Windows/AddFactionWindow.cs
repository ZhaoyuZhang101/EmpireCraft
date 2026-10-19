using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 添加派系(用户 2026-10-19 转玩家反馈"党派设置能不能再优化一下，比如可以直接添加默认派系""自定义派系为啥只有中间派"，
// 看过示意图定"照这个做"，见 Tasks/ui-mockups/faction-windows.png)：卷册宽窗口——
//   左栏：本国朝堂(政体、党禁与否、几席)、在朝派系(点开详情)、说明、恢复默认派系；
//   右栏：本政体默认派系(固定派系的朝堂：一键添加，已在朝的置灰)、按理念新建(古代或近代理念，各一格)、派系库(存过的派系)。
// 立派、入朝的规则见 FactionEditor
public class AddFactionWindow : WideScrollWindow<AddFactionWindow>
{
    private const int IdeologyColumns = 5;
    private Kingdom _kingdom;
    private Regime _regime;
    private List<FixedFaction> _current = new();
    private FactionEditor.Court _court;

    protected override void OnOpen() => _kingdom = SelectedMetas.selected_kingdom;

    protected override void Prepare()
    {
        _regime = _kingdom?.data != null && !_kingdom.isRekt() ? _kingdom.GetRegime() : null;
        _current = FactionEditor.Current(_kingdom);
        _court = FactionEditor.CourtOf(_kingdom);
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        FactionManager.Save();
    }

    private static Sprite IdeologyIcon(PartyIdeology ideology) =>
        SpriteTextureLoader.getSprite(EmpireCraft.Scripts.GameLibrary.IdeologyTraitIcons.Path(ideology));

    private static string IdeologyHex(PartyIdeology ideology) => WideKit.Hex(UIHelper.IdeologyColor(ideology));

    #region 左栏

    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        if (_regime == null)
        {
            rail.AddRailTitle(LM.Get("AddFactionWindow Title"), LM.Get("faction_add_none"));
            return;
        }
        string court = LM.Get("faction_court_" + _court);
        var title = rail.AddRailTitle(string.Format(LM.Get("faction_add_rail_title"), _kingdom.GetKingdomName()),
            $"{CompositeEmpireService.GetRegimeName(_regime.type)} · {court}");
        title.AddRailText(string.Format(LM.Get("faction_add_seats"), _current.Count, FactionEditor.Seats(_kingdom)), 7, 10f);

        var menu = rail.BeginRailMenu(LM.Get("faction_add_in_court"));
        int index = 0;
        foreach (FixedFaction faction in _current)
        {
            FixedFaction target = faction;
            PartyIdeology ideology = PartySystem.LeaningOf(faction);
            menu.AddRailOption(IdeologyIcon(ideology), faction.Name, $"{faction.CentralRatio}%", false,
                () => OpenDetail(target), index++);
        }
        if (FactionEditor.HasRoom(_kingdom))
            menu.AddRailOption(null, LM.Get("faction_add_empty_seat"),
                (FactionEditor.Seats(_kingdom) - _current.Count).ToString(), false, () => { }, index);

        var hint = rail.BeginRailCard();
        hint.AddRailText(LM.Get("faction_add_hint_" + _court).ColorString(WideKit.Muted), 6, 34f, wrap: true);

        rail.AddRailButton("faction_recover_defaults", LM.Get("faction_recover_defaults"), RecoverDefaults);
    }

    private void OpenDetail(FixedFaction faction)
    {
        ConfigData.CURRENT_SELECTED_FACTION = faction;
        SelectedMetas.selected_kingdom = _kingdom;
        ScrollWindow.showWindow(nameof(FactionDetailWindow));
    }

    // 恢复默认派系：整套换回本政体的默认派系(在朝的撤下)
    private void RecoverDefaults()
    {
        _regime?.RecoverFactions();
        FactionManager.Save();
        AfterChange("faction_recovered");
    }

    #endregion

    #region 右栏

    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        page.AddPageHeader(PageWidth, LM.Get("AddFactionWindow Title"),
            string.Format(LM.Get("faction_add_seats"), _current.Count, FactionEditor.Seats(_kingdom)));
        if (_regime == null) return;
        List<FixedFaction> defaults = FactionEditor.Defaults(_kingdom);
        if (defaults.Count > 0)
        {
            page.AddSectionHeader(PageWidth, LM.Get("faction_add_defaults") + " · " +
                                             CompositeEmpireService.GetRegimeName(_regime.type), localize: false);
            page.AddCardGrid(PageWidth, defaults, 2, DrawDefaultCard);
        }
        DrawIdeologies(page);
        DrawLibrary(page);
    }

    private void DrawDefaultCard(AutoHoriLayoutGroup row, float width, FixedFaction template)
    {
        PartyIdeology ideology = PartySystem.LeaningOf(template);
        var card = row.BeginColumnCard(width, template.Name, IdeologyHex(ideology), IdeologyFamilies.FactionLeaning(template));
        Empire empire = FactionEditor.EmpireOf(_kingdom);
        string claims = string.Join("·", (template.TemporaryFactionTypes ?? new List<EmpireCraft.Scripts.Regimes.TemporaryFactions.TemporaryFactionType>())
            .Take(3).Select(type => TranslateHelper.GetTemporaryFactionClaimText(type, empire)));
        if (!string.IsNullOrEmpty(claims))
            Line(card, width, LM.Get("faction_add_claims"), claims);
        string classes = string.Join("·", (template.ClassAffinities ?? new Dictionary<SocialClass, float>())
            .Where(pair => pair.Value > 0f).OrderByDescending(pair => pair.Value).Take(2)
            .Select(pair => LM.Get($"class_{pair.Key}")));
        if (!string.IsNullOrEmpty(classes))
            Line(card, width, LM.Get("faction_add_classes"), classes);
        bool seated = FactionEditor.DefaultSeated(_kingdom, template);
        card.AddButtonRow(width, ("faction_add_default", LM.Get(seated ? "faction_add_seated" : "faction_add_button"), () =>
        {
            FixedFaction added = FactionEditor.AddDefault(_kingdom, template, out string reason);
            AfterChange(added != null ? "faction_added" : reason);
        }));
    }

    private static void Line(AutoVertLayoutGroup card, float width, string label, string value)
    {
        card.AddTextIntoVertLayout($"{label.ColorString(WideKit.Muted)} {value.ColorString(WideKit.Soft)}", true,
                TextAnchor.MiddleLeft, new Vector2(width - 12f, 10f))
            .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
    }

    // 按理念新建：每个理念一格(名字照史书习惯、底下小字是理念)；已在朝、不能立的置灰，悬停看原因
    private void DrawIdeologies(AutoVertLayoutGroup page)
    {
        page.AddSectionHeader(PageWidth, LM.Get(_court == FactionEditor.Court.Party ? "faction_add_by_party" : "faction_add_by_ideology"),
            localize: false);
        var options = FactionEditor.Options(_kingdom);
        float tile = (PageWidth - (IdeologyColumns - 1) * UIHelper.CardGap) / IdeologyColumns;
        for (int start = 0; start < options.Count; start += IdeologyColumns)
        {
            var row = page.BeginColumns(PageWidth);
            for (int i = start; i < Mathf.Min(options.Count, start + IdeologyColumns); i++)
            {
                (PartyIdeology ideology, string reason) = options[i];
                bool seated = FactionEditor.Seated(_kingdom, ideology);
                string why = seated ? "faction_add_seated" : reason;
                DrawIdeologyTile(row, tile, ideology, why);
            }
        }
    }

    private void DrawIdeologyTile(AutoHoriLayoutGroup row, float width, PartyIdeology ideology, string why)
    {
        bool ok = why == null;
        string label = _court == FactionEditor.Court.Party ? PartySystem.GetIdeologyName(ideology)
            : ideology == PartyIdeology.Centrism ? LM.Get("faction_blank_name") : LM.Get("clique_label_" + ideology);
        string sub = why == "faction_add_seated" ? LM.Get("faction_add_seated")
            : _court == FactionEditor.Court.Party ? LM.Get("faction_add_found") : PartySystem.GetIdeologyName(ideology);
        var cell = row.BeginVertGroup(new Vector2(width, 24f), pSpacing: 0, pAlignment: TextAnchor.MiddleCenter,
            pPadding: new RectOffset(0, 0, 1, 1));
        UIHelper.FixPanelWidth(cell.transform, width);
        UIHelper.AddInsetBackground(cell, new Vector2(width, 24f));
        cell.AddTextIntoVertLayout(label.ColorString(ok ? IdeologyHex(ideology) : "#6E746A"), true, TextAnchor.MiddleCenter,
            new Vector2(width - 2f, 12f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        cell.AddTextIntoVertLayout(sub.ColorString(ok ? WideKit.Muted : "#5E645A"), true, TextAnchor.MiddleCenter,
            new Vector2(width - 2f, 9f)).UseFixedFontSize(5, HorizontalWrapMode.Overflow);
        string tipTitle = string.Format(LM.Get("faction_add_tip_title"), label, PartySystem.GetIdeologyName(ideology));
        UIHelper.AttachTextTooltip(cell.gameObject, $"faction_add_{ideology}", tipTitle,
            ok ? IdeologyFamilies.FactionLeaning(ideology) : LM.Get(why));
        if (!ok) return;
        UIHelper.MakeClickable(cell.gameObject, () =>
        {
            FixedFaction added = FactionEditor.CreateByIdeology(_kingdom, ideology, out string reason);
            AfterChange(added != null ? "faction_added" : reason);
        });
    }

    // 派系库：以前存下的派系，添加(复制一份入朝)或删除
    private void DrawLibrary(AutoVertLayoutGroup page)
    {
        page.AddSectionHeader(PageWidth, LM.Get("faction_add_library"), localize: false);
        List<FixedFaction> library = FactionManager.Config?.PlayerFactions?.Where(faction => faction != null).ToList() ??
                                     new List<FixedFaction>();
        if (library.Count == 0)
        {
            page.AddEmptyCard(PageWidth, LM.Get("faction_add_library_empty"), LM.Get("faction_add_library_hint"));
            return;
        }
        var card = page.BeginCard(PageWidth);
        float inner = PageWidth - 12f;
        int index = 0;
        foreach (FixedFaction preset in library)
        {
            FixedFaction target = preset;
            PartyIdeology ideology = PartySystem.LeaningOf(preset);
            var row = card.BeginHoriGroup(new Vector2(inner, 15f), TextAnchor.MiddleLeft, 3, new RectOffset(3, 3, 0, 0));
            if (index++ % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, 15f));
            UIHelper.AddLayoutIcon(row.transform, IdeologyIcon(ideology), 10f);
            row.AddTextIntoHoriLayout((preset.Name ?? "").ColorString(IdeologyHex(ideology)), true, TextAnchor.MiddleLeft,
                new Vector2(70f, 13f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
            string traits = string.Join("、", (preset.RequiredTraits ?? new List<string>()).Take(3)
                .Select(trait => AssetManager.traits.get(trait)?.getTranslatedName() ?? trait));
            row.AddTextIntoHoriLayout($"{IdeologyFamilies.FactionLeaning(preset)}  {traits}".ColorString(WideKit.Muted), true,
                TextAnchor.MiddleLeft, new Vector2(inner - 70f - 13f - 50f, 13f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            row.AddButtonIntoHoriLayout("add_faction", "", () =>
            {
                FixedFaction added = FactionEditor.AddFromLibrary(_kingdom, target, out string reason);
                AfterChange(added != null ? "faction_added" : reason);
            }, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(11, 11), showTip: true);
            row.AddButtonIntoHoriLayout("delete_faction_preset", "", () =>
            {
                FactionManager.Config.PlayerFactions.Remove(target);
                FactionManager.Save();
                Refresh();
            }, SpriteTextureLoader.getSprite("ui/iconDeleteFaction"), size: new Vector2(11, 11), showTip: true);
        }
    }

    #endregion

    // 添加、恢复之后：提示一句，重画本窗口与帝国主页的派系格局
    private void AfterChange(string tipKey)
    {
        if (!string.IsNullOrEmpty(tipKey)) WorldTip.showNow(LM.Get(tipKey), false, "top", 2.5f);
        UIHelper.RefreshFactionSpace(_kingdom);
        Refresh();
    }
}
