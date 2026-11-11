using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static EmpireCraft.Scripts.UI.Components.RealmMiniMap;

namespace EmpireCraft.Scripts.UI.Windows;

// 发起诉求时选目标(「卷册」宽窗口；用户 2026-11-11 "这里在发起诉求时应该给个目标""这个目标国的选择窗口可以使用地图的简化图"
// "城市什么的也是一样，但是小人可以直接显示小人卡片，然后用身份职业氏族什么的分类""还有帝国层级呢"，
// 示意图 Tasks/ui-mockups/claim-target-picker.png)。从派系窗口诉求页的「发起」打开(见 FactionDetailWindow.Pages)；
// 谁能当目标、不可选的原因、朝廷自己会挑谁都来自 ClaimRules.json 的 target(见 ClaimTargetRules，政体编辑器可改)。
//   · 左栏：诉求 → 目标层级(帝国 / 国家 / 城 / 人物，用不上的写不适用) → 已选目标的详情；
//   · 右栏(帝国、国家、城)：简化地图(亮色可选、金色本朝、灰色不可选、白框已选，点图即选) → 可选名单 → 上奏；
//   · 右栏(人物)：按身份 / 官职 / 氏族 / 派系分组的人物卡 → 上奏。
// 选帝国 = 以它的朝廷(核心国)为目标。
// 个别决议的目标是法理头衔(设置行政区、文化转化)或宗教(宗教融入)(用户 2026-11-11 "覆盖全部需要目标的决议")：
// 这两个层级有候选才列；法理头衔照城的画法上图(辖下各城染青、合成一块)，宗教只列名单
public class ClaimTargetWindow : WideScrollWindow<ClaimTargetWindow>
{
    private enum Level
    {
        Empire,
        Kingdom,
        City,
        Actor,
        Title,
        Religion
    }

    private const float Pad = 12f;
    private const int ListRows = 10, NameList = 8;
    private const float MapWidth = PageWidth - Pad, MapMaxHeight = 150f;
    private static long _pendingEmpire = -1L;
    private static string _pendingFaction = "";
    private static TemporaryFactionType _pendingClaim;

    // 状态(返回时还原)
    private long _empireId = -1L;
    private string _factionId = "";
    private TemporaryFactionType _claimType;
    private Level _level = Level.Kingdom;
    private long _selected = -1L;
    private int _grouping;
    private List<ClaimTargetOption> _options;
    private long _autoId = -1L;
    private MetaType _autoType = MetaType.None;

    // 每次重画前(Prepare)
    private Empire _empire;
    private FixedFaction _faction;
    private TemporaryFaction _claim;
    private string _blocked;
    private SimpleText _hoverText;

    public static void Open(Empire empire, FixedFaction faction, TemporaryFaction claim)
    {
        if (empire == null || faction == null || claim == null) return;
        _pendingEmpire = empire.getID();
        _pendingFaction = faction.GetID();
        _pendingClaim = claim.type;
        ScrollWindow.showWindow(nameof(ClaimTargetWindow));
    }

    protected override void OnOpen()
    {
        _empireId = _pendingEmpire;
        _factionId = _pendingFaction;
        _claimType = _pendingClaim;
        _options = null;
        _selected = -1L;
        _grouping = 0;
        RealmMiniMap.Invalidate();
        Resolve();
        if (_claim == null) return;
        _options = _claim.TargetOptions() ?? new List<ClaimTargetOption>();
        ClaimTargetOption auto = _claim.AutoTarget();
        _autoId = auto?.Id ?? -1L;
        _autoType = auto?.Type ?? MetaType.None;
        MetaType type = auto?.Type ?? _options.FirstOrDefault(option => option.Selectable)?.Type ?? _options.FirstOrDefault()?.Type ?? MetaType.Kingdom;
        _level = type == MetaType.City ? Level.City : type == MetaType.Unit ? Level.Actor : type == MetaType.Religion ? Level.Religion
            : type == MetaTypeExtension.KingdomTitle ? Level.Title : Level.Kingdom;
        ClaimTargetOption first = auto != null ? Find(auto.Type, auto.Id) : null;
        first ??= Listed(_level).FirstOrDefault(option => option.Selectable);
        _selected = first?.Id ?? -1L;
    }

    private void Resolve()
    {
        _empire = ModClass.EMPIRE_MANAGER?.get(_empireId);
        if (_empire != null && (_empire.isRekt() || _empire.CoreKingdom == null)) _empire = null;
        _faction = _empire == null ? null : CourtSystem.FindFaction(_empire, _factionId)
                                             ?? _empire.CoreKingdom.GetRegime()?.GetPlayerFactions()?.Find(faction => faction.GetID() == _factionId);
        _claim = _faction?.TemporaryFactions?.FirstOrDefault(claim => claim != null && claim.type == _claimType);
    }

    protected override void Prepare()
    {
        Resolve();
        _options ??= _claim?.TargetOptions() ?? new List<ClaimTargetOption>();
        _blocked = _claim == null ? "court_god_propose_invalid" : CourtSystem.CannotGodPropose(_empire, _faction, _claim);
        // 朝廷自己挑不出、而神力能指定的不算拦着
        if (_blocked == "court_god_propose_condition" && _options.Any(option => option.Selectable)) _blocked = null;
        _hoverText = null;
    }

    #region 候选

    private static MetaType TypeOf(Level level) => level == Level.City ? MetaType.City : level == Level.Actor ? MetaType.Unit
        : level == Level.Religion ? MetaType.Religion : level == Level.Title ? MetaTypeExtension.KingdomTitle : MetaType.Kingdom;

    // 按城画图的层级：城，法理头衔(辖下各城)
    private bool ByCity => _level == Level.City || _level == Level.Title;

    private ClaimTargetOption Find(MetaType type, long id) =>
        id < 0 ? null : _options?.FirstOrDefault(option => option.Type == type && option.Id == id);

    // 别的帝国的朝廷(核心国)
    private bool IsForeignCourt(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire != null && !empire.isRekt() && empire != _empire && empire.CoreKingdom == kingdom;
    }

    // 这一层级的候选：帝国层级只列别的帝国的朝廷
    private List<ClaimTargetOption> Listed(Level level)
    {
        MetaType type = TypeOf(level);
        IEnumerable<ClaimTargetOption> list = (_options ?? new List<ClaimTargetOption>()).Where(option => option.Type == type);
        if (level == Level.Empire) list = list.Where(option => IsForeignCourt(option.Kingdom));
        return list.OrderByDescending(option => option.Selectable).ThenByDescending(IsAuto).ThenByDescending(option => option.Score).ToList();
    }

    private bool IsAuto(ClaimTargetOption option) => option != null && option.Type == _autoType && option.Id == _autoId;

    private ClaimTargetOption Selected => Find(TypeOf(_level), _selected);

    private string NameAt(Level level, ClaimTargetOption option) =>
        level == Level.Empire ? option.Kingdom?.GetEmpire()?.GetEmpireName() ?? option.Name : option.Name;

    // 候选的国家都在本朝之内(文化同化的行政区)：层级叫"行政区"
    private bool KingdomsAreMembers() =>
        _options != null && _options.Any(option => option.Type == MetaType.Kingdom) &&
        _options.Where(option => option.Type == MetaType.Kingdom).All(option => option.Kingdom?.GetEmpire() == _empire);

    private string LevelName(Level level) => LM.Get(level switch
    {
        Level.Empire => "claim_target_level_empire",
        Level.Kingdom => KingdomsAreMembers() ? "claim_target_level_member" : "claim_target_level_kingdom",
        Level.City => "claim_target_level_city",
        Level.Title => "claim_target_level_title",
        Level.Religion => "claim_target_level_religion",
        _ => "claim_target_level_actor"
    });

    private void Select(Level level, long id)
    {
        _level = level;
        _selected = id;
        Refresh();
    }

    // 把目标临时换成这个候选，估各派表态与准奏率(册立储君这类各派因人而异，见 TemporaryFaction.TargetStance)
    private (float chance, int pro, int con) Outlook(ClaimTargetOption option)
    {
        if (option == null || _claim == null || _empire == null || !CourtSystem.Applies(_empire)) return (-1f, 0, 0);
        (long id, MetaType type) old = _claim.SwapTarget(option);
        try
        {
            List<(FixedFaction faction, int stance, float weight)> stances = CourtSystem.DebateStances(_empire, _claim, _faction);
            var memorial = new CourtMemorial
            {
                claim = _claim.type.ToString(), proposer = _faction.GetID(), support = CourtSystem.SupportShare(stances)
            };
            float chance = CourtSystem.Chance(CourtSystem.ApprovalFactors(_empire, memorial, _claim.type, _faction));
            return (chance, stances.Count(item => item.faction != _faction && item.stance > 0), stances.Count(item => item.stance < 0));
        }
        catch (Exception)
        {
            return (-1f, 0, 0);
        }
        finally
        {
            _claim.RestoreTarget(old);
        }
    }

    private static string ChanceHex(float chance) => chance >= 0.6f ? WideKit.Green : chance >= 0.3f ? WideKit.Gold : WideKit.Red;

    #endregion

    #region 左栏

    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        float width = UIHelper.RailWidth - 8f;
        string claimName = _claim == null ? LM.Get("label_none") : TranslateHelperName();
        var title = rail.AddRailTitle(claimName, string.Format(LM.Get("claim_target_rail_sub"), _faction?.Name ?? ""));
        title.AddRailText(LM.Get("claim_target_rail_hint").ColorString(WideKit.Muted), 6, 20f, true);
        if (_claim == null) return;

        var menu = rail.BeginRailMenu(LM.Get("claim_target_levels"));
        int index = 0;
        // 目标是法理头衔、宗教的决议只列有候选的层级；其余照旧列四级，用不上的写不适用
        var levels = new List<Level> { Level.Empire, Level.Kingdom, Level.City, Level.Actor };
        if (_options.Any(option => option.Type == MetaType.Religion || option.Type == MetaTypeExtension.KingdomTitle))
            levels = new[] { Level.Title, Level.Religion, Level.Empire, Level.Kingdom, Level.City, Level.Actor }
                .Where(level => Listed(level).Count > 0).ToList();
        foreach (Level level in levels)
        {
            List<ClaimTargetOption> listed = Listed(level);
            string name = LevelName(level);
            if (listed.Count == 0)
            {
                menu.AddRailRow(width, name, LM.Get("claim_target_level_na"), WideKit.Muted, index++);
                continue;
            }
            Level picked = level;
            long keep = listed.Any(option => option.Id == _selected) ? _selected
                : listed.FirstOrDefault(option => option.Selectable)?.Id ?? -1L;
            menu.AddRailOption(null, name, $"{listed.Count(option => option.Selectable)}/{listed.Count}", level == _level,
                () => Select(picked, picked == _level ? _selected : keep), index++);
        }

        var card = rail.BeginRailCard();
        card.AddRailText(LM.Get("claim_target_picked").ColorString(WideKit.Cyan), 7, 10f);
        ClaimTargetOption selected = Selected;
        if (selected == null)
        {
            card.AddRailText(LM.Get("claim_target_picked_none").ColorString(WideKit.Muted), 6, 10f);
            return;
        }
        int row = 0;
        if (_level == Level.Empire)
        {
            Empire target = selected.Kingdom.GetEmpire();
            card.AddRailRow(width, LM.Get("claim_target_level_empire"), target?.GetEmpireName() ?? "", WideKit.Gold, row++);
            card.AddRailRow(width, LM.Get("claim_target_row_court"), selected.Name, WideKit.Soft, row++);
            card.AddRailRow(width, LM.Get("claim_target_row_members"),
                Math.Max(0, (target?.kingdoms_list?.Count ?? 1) - 1).ToString(), WideKit.Soft, row++);
        }
        else if (_level == Level.Kingdom)
        {
            Empire target = selected.Kingdom.GetEmpire();
            card.AddRailRow(width, LevelName(Level.Kingdom), selected.Name, WideKit.Gold, row++);
            card.AddRailRow(width, LM.Get("claim_target_row_belongs"),
                target != null && !target.isRekt() ? target.GetEmpireName() : LM.Get("claim_target_independent"), WideKit.Soft, row++);
        }
        else if (_level == Level.City)
        {
            card.AddRailRow(width, LM.Get("claim_target_level_city"), selected.Name, WideKit.Gold, row++);
            card.AddRailRow(width, LM.Get("claim_target_row_belongs"), selected.City.kingdom?.GetKingdomName() ?? "", WideKit.Soft, row++);
        }
        else if (_level == Level.Title)
        {
            City seat = selected.Title?.title_capital;
            card.AddRailRow(width, LM.Get("claim_target_level_title"), selected.Name, WideKit.Gold, row++);
            if (seat != null && !seat.isRekt())
            {
                card.AddRailRow(width, LM.Get("claim_target_row_seat"), seat.GetCityName(), WideKit.Soft, row++);
                card.AddRailRow(width, LM.Get("claim_target_row_belongs"), seat.kingdom?.GetKingdomName() ?? "", WideKit.Soft, row++);
            }
        }
        else if (_level == Level.Religion)
            card.AddRailRow(width, LM.Get("claim_target_level_religion"), selected.Name, WideKit.Gold, row++);
        else
        {
            Actor actor = selected.Actor;
            card.AddRailRow(width, LM.Get("claim_target_level_actor"), selected.Name, WideKit.Gold, row++);
            card.AddRailRow(width, LM.Get("claim_target_group_clan"), ClanOf(actor), WideKit.Soft, row++);
            card.AddRailRow(width, LM.Get("claim_target_group_office"), OfficeOf(actor), WideKit.Soft, row++);
            card.AddRailRow(width, LM.Get("claim_target_group_faction"), FactionOf(actor),
                actor.GetFaction() == _faction ? WideKit.Green : WideKit.Soft, row++);
        }
        if (!string.IsNullOrEmpty(selected.Figure))
            card.AddRailRow(width, LM.Get(FigureKey(_level)), selected.Figure, selected.FigureHex, row++);
        if (!string.IsNullOrEmpty(selected.Why))
            card.AddRailRow(width, LM.Get(selected.Selectable ? "claim_target_row_why" : "claim_target_row_blocked"), selected.Why,
                selected.Selectable ? WideKit.Orange : WideKit.Red, row++);
        if (IsAuto(selected)) card.AddRailRow(width, LM.Get("claim_target_row_auto"), LM.Get("claim_target_auto_pick"), WideKit.Orange, row++);
        if (!selected.Selectable) return;
        (float chance, int pro, int con) = Outlook(selected);
        if (chance < 0f) return;
        card.AddRailRow(width, LM.Get("claim_target_row_sides"), string.Format(LM.Get("claim_target_sides"), pro, con),
            pro >= con ? WideKit.Green : WideKit.Red, row++);
        card.AddRailRow(width, LM.Get("claim_target_row_chance"), $"{Mathf.RoundToInt(chance * 100f)}%", ChanceHex(chance), row);
    }

    private string TranslateHelperName() =>
        EmpireCraft.Scripts.HelperFunc.TranslateHelper.GetTemporaryFactionClaimText(_claim.type, _empire);

    private string FigureKey(Level level) => level switch
    {
        Level.City => "claim_target_col_population",
        Level.Actor => "claim_target_col_age",
        Level.Title => "claim_target_col_cities",
        Level.Religion => "claim_target_col_share",
        _ => KingdomsAreMembers() ? "claim_target_col_cities" : "claim_target_col_power"
    };

    #endregion

    #region 右栏

    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (_claim == null)
        {
            page.AddPageHeader(PageWidth, LM.Get("claim_target_title"));
            page.AddEmptyCard(PageWidth, LM.Get("claim_target_title"), LM.Get("claim_target_gone"));
            return;
        }
        List<ClaimTargetOption> listed = Listed(_level);
        int selectable = listed.Count(option => option.Selectable);
        page.AddPageHeader(PageWidth, LM.Get("claim_target_title"),
            string.Format(LM.Get("claim_target_counts"), selectable, listed.Count - selectable));
        if (_blocked != null)
        {
            var warn = page.BeginCard(PageWidth, "claim_target_card_blocked", WideKit.Red);
            warn.AddParagraph(PageWidth, LM.Has(_blocked) ? LM.Get(_blocked) : _blocked, 7, WideKit.Soft);
        }
        if (_level == Level.Actor) DrawActors(page, listed);
        else
        {
            if (_level != Level.Religion) DrawMap(page, listed);
            DrawList(page, listed);
        }
        DrawActions(page);
    }

    private void DrawActions(AutoVertLayoutGroup page)
    {
        ClaimTargetOption selected = Selected;
        bool ready = _blocked == null && selected != null && selected.Selectable;
        string name = selected == null ? "" : NameAt(_level, selected);
        page.AddActionRow(PageWidth,
            (ready ? string.Format(LM.Get("claim_target_confirm"), name) : LM.Get("claim_target_confirm_none"), WideKit.Gold, ready, () =>
            {
                if (CourtSystem.GodPropose(_empire, _faction, _claim, selected)) ScrollWindow.getCurrentWindow()?.clickBack();
                else
                {
                    ActionLibrary.showWhisperTip("claim_target_failed");
                    _options = null;
                    Refresh();
                }
            }),
            (LM.Get("claim_target_auto"), WideKit.Value, _blocked == null && _autoId >= 0, () =>
            {
                if (CourtSystem.GodPropose(_empire, _faction, _claim, null)) ScrollWindow.getCurrentWindow()?.clickBack();
                else
                {
                    ActionLibrary.showWhisperTip("claim_target_failed");
                    _options = null;
                    Refresh();
                }
            }),
            (LM.Get("claim_target_cancel"), WideKit.Red, true, () => ScrollWindow.getCurrentWindow()?.clickBack()));
    }

    // 可选名单：名字 · 中间一列(国力比、城数、人口) · 缘由；不可选的并成一行写原因
    private void DrawList(AutoVertLayoutGroup page, List<ClaimTargetOption> listed)
    {
        var card = page.BeginCard(PageWidth, string.Format(LM.Get("claim_target_card_list"), LevelName(_level)), WideKit.Gold,
            LM.Get("claim_target_list_hint"), "ui/icons/iconBooks");
        float nameWidth = (PageWidth - Pad) * 0.34f;
        List<ClaimTargetOption> open = listed.Where(option => option.Selectable).ToList();
        if (open.Count == 0) card.AddParagraph(PageWidth, LM.Get("claim_target_list_none"), 7, WideKit.Muted);
        else card.AddTableRow(PageWidth, -1, null, LevelName(_level), nameWidth, (LM.Get(FigureKey(_level)), null), (LM.Get("claim_target_col_why"), null));
        // 选中的排不进前几行时也要露出来
        List<ClaimTargetOption> shown = open.Take(ListRows).ToList();
        ClaimTargetOption picked = Selected;
        if (picked != null && picked.Selectable && !shown.Contains(picked) && shown.Count > 0) shown[shown.Count - 1] = picked;
        for (int i = 0; i < shown.Count; i++)
        {
            ClaimTargetOption option = shown[i];
            bool selected = option.Id == _selected;
            string name = NameAt(_level, option);
            if (selected) name = ("› " + name).ColorString(WideKit.Gold);
            string why = IsAuto(option)
                ? (string.IsNullOrEmpty(option.Why) ? "" : option.Why + " · ") + LM.Get("claim_target_auto_pick").ColorString(WideKit.Orange)
                : option.Why;
            var row = card.AddTableRow(PageWidth, i, null, name, nameWidth, (option.Figure, option.FigureHex), (why, WideKit.Soft));
            long id = option.Id;
            UIHelper.MakeClickable(row.gameObject, () => Select(_level, id));
        }
        if (open.Count > shown.Count)
            card.AddParagraph(PageWidth, string.Format(LM.Get("claim_target_list_more"), open.Count - shown.Count), 6, WideKit.Muted);
        List<ClaimTargetOption> closed = listed.Where(option => !option.Selectable).ToList();
        if (closed.Count == 0) return;
        string names = string.Join(LM.Get("claim_target_list_sep"), closed.Take(NameList).Select(option => $"{NameAt(_level, option)}({option.Why})"));
        if (closed.Count > NameList) names += string.Format(LM.Get("claim_target_list_etc"), closed.Count);
        card.AddParagraph(PageWidth, string.Format(LM.Get("claim_target_list_closed"), names), 6, WideKit.Muted);
    }

    #endregion

    #region 简化地图

    // 地图的快照(每格归哪国、哪座城)、配色、国色都在 RealmMiniMap(外交界面的简图也用它)，这里只管选目标的画法
    private void DrawMap(AutoVertLayoutGroup page, List<ClaimTargetOption> listed)
    {
        MapData map;
        try
        {
            map = Snapshot();
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][选目标] 简化地图没画成: {exception.Message}");
            return;
        }
        bool cityLevel = ByCity;
        var card = page.BeginCard(PageWidth, cityLevel ? "claim_target_card_map_own" : "claim_target_card_map", WideKit.Gold, LevelName(_level),
            "ui/icons/iconKingdom");
        int kingdoms = map.Kingdoms.Count, cities = map.Cities.Count;
        // 每国：算哪一组(帝国层级同一帝国一组)、什么颜色；每组对着哪个候选
        var group = new int[kingdoms];
        var color = new Color32[kingdoms];
        var optionOf = new ClaimTargetOption[kingdoms];
        var own = new bool[kingdoms];
        var byKingdom = new Dictionary<long, ClaimTargetOption>();
        foreach (ClaimTargetOption option in listed)
            if (option.Type == MetaType.Kingdom && option.Kingdom != null) byKingdom[option.Kingdom.id] = option;
        for (int i = 0; i < kingdoms; i++)
        {
            Kingdom kingdom = map.Kingdoms[i];
            Empire empire = kingdom.GetEmpire();
            group[i] = i;
            ClaimTargetOption option = null;
            if (!cityLevel)
            {
                if (_level == Level.Empire)
                {
                    Kingdom court = empire != null && !empire.isRekt() && empire != _empire ? empire.CoreKingdom : null;
                    if (court != null)
                    {
                        byKingdom.TryGetValue(court.id, out option);
                        if (map.KingdomIndex.TryGetValue(court.id, out int courtIndex)) group[i] = courtIndex;
                    }
                }
                else byKingdom.TryGetValue(kingdom.id, out option);
            }
            optionOf[i] = option;
            own[i] = kingdom == _empire.CoreKingdom || empire == _empire;
            if (option != null && option.Selectable)
                color[i] = KingdomColor(_level == Level.Empire ? option.Kingdom : kingdom);
            else if (own[i]) color[i] = cityLevel ? Mix(i % 2 == 0 ? OwnGold : OwnGoldDark, Wild, 0.45f) : i % 2 == 0 ? OwnGold : OwnGoldDark;
            else color[i] = cityLevel ? Dim : Mix(KingdomColor(kingdom), Grey, 0.82f);
        }
        // 城(城、法理头衔层级)：能选的城的地块染青；法理头衔辖下各城都算它的，画边线时合成一块(cityGroup)
        var cityOption = new ClaimTargetOption[cities];
        var cityGroup = new int[cities];
        for (int i = 0; i < cities; i++) cityGroup[i] = i;
        if (cityLevel)
            foreach (ClaimTargetOption option in listed)
            {
                if (option.City != null && map.CityIndex.TryGetValue(option.City.id, out int cityIndex)) cityOption[cityIndex] = option;
                if (option.Title?.city_list == null) continue;
                int first = -1;
                foreach (City city in option.Title.city_list)
                {
                    if (city == null || !map.CityIndex.TryGetValue(city.id, out int titleCity)) continue;
                    if (first < 0) first = titleCity;
                    cityOption[titleCity] = option;
                    cityGroup[titleCity] = first;
                }
            }
        ClaimTargetOption selected = Selected;
        int selectedGroup = -3, selectedCity = -1;
        if (selected != null)
        {
            if (cityLevel)
            {
                City anchor = selected.City ?? selected.Title?.city_list?.FirstOrDefault(city => city != null && map.CityIndex.ContainsKey(city.id));
                selectedCity = anchor != null && map.CityIndex.TryGetValue(anchor.id, out int index) ? cityGroup[index] : -1;
            }
            else if (selected.Kingdom != null && map.KingdomIndex.TryGetValue(selected.Kingdom.id, out int kingdomAt)) selectedGroup = group[kingdomAt];
        }

        int width = map.Width, height = map.Height;
        var pixels = new Color32[width * height];
        var cell = new int[width * height];   // 画边线用的分组：水 -2、无主 -1、其余按国(城层级本朝按城)
        var sumX = new float[kingdoms];
        var sumY = new float[kingdoms];
        var count = new int[kingdoms];
        for (int at = 0; at < pixels.Length; at++)
        {
            int owner = map.Owner[at];
            if (owner < 0)
            {
                pixels[at] = owner == -2 ? Water : Wild;
                cell[at] = owner;
                continue;
            }
            Color32 c = color[owner];
            int g = group[owner];
            if (cityLevel)
            {
                int city = map.City[at];
                ClaimTargetOption option = city >= 0 ? cityOption[city] : null;
                if (option != null && option.Selectable) c = Mix(c, Cyan, option == selected ? 0.75f : 0.4f);
                g = own[owner] && city >= 0 ? kingdoms + cityGroup[city] : owner;
            }
            pixels[at] = c;
            cell[at] = g;
            int labelGroup = cityLevel ? owner : group[owner];
            sumX[labelGroup] += at % width;
            sumY[labelGroup] += at / width;
            count[labelGroup]++;
        }
        int chosen = cityLevel ? selectedCity >= 0 ? kingdoms + selectedCity : -3 : selectedGroup;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int at = y * width + x, g = cell[at];
            for (int side = 0; side < 2; side++)
            {
                int other = side == 0 ? (x + 1 < width ? at + 1 : -1) : (y + 1 < height ? at + width : -1);
                if (other < 0 || cell[other] == g) continue;
                int h = cell[other];
                if (g == chosen || h == chosen)
                {
                    pixels[at] = White;
                    pixels[other] = White;
                }
                else if ((g >= 0 || h >= 0) && pixels[at].r != 255) pixels[at] = Mix(pixels[at], Ink, 0.55f);
            }
        }
        // 城的标记(城层级；法理头衔标在首府)
        var markers = new List<(int x, int y, ClaimTargetOption option)>();
        if (cityLevel)
            foreach (ClaimTargetOption option in listed)
            {
                WorldTile tile = (option.City ?? option.Title?.title_capital)?.getTile();
                if (tile == null) continue;
                int mx = Mathf.Clamp(tile.x / map.Step, 0, width - 1), my = Mathf.Clamp(tile.y / map.Step, 0, height - 1);
                markers.Add((mx, my, option));
                Color32 fill = option.Id == _selected ? White : option.Selectable ? Cyan : MarkerGrey;
                for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int px = mx + dx, py = my + dy;
                    if (px < 0 || py < 0 || px >= width || py >= height) continue;
                    pixels[py * width + px] = Math.Abs(dx) == 3 || Math.Abs(dy) == 3 ? Ink : fill;
                }
            }

        float scale = Mathf.Min(MapWidth / width, MapMaxHeight / height);
        ClaimTargetMap view = ClaimTargetMap.Create(card.transform, width * scale, height * scale, width, height, pixels,
            (x, y) => ClickMap(map, group, optionOf, cityOption, markers, x, y),
            (x, y) => HoverMap(map, group, optionOf, cityOption, own, x, y));

        // 图上的名字：本朝、已选、可选的(按大小取前几个)、不可选里大的
        int minimum = Mathf.Max(12, pixels.Length / 260);
        if (cityLevel)
        {
            foreach ((int x, int y, ClaimTargetOption option) in markers.Where(marker => marker.option.Selectable || marker.option.Id == _selected).Take(16))
                view.AddLabel(x, y + 6, option.Name, option.Id == _selected ? Color.white : (Color)Cyan, 6);
        }
        else
        {
            var labelled = new HashSet<int>();
            IEnumerable<int> order = Enumerable.Range(0, kingdoms).Where(i => group[i] == i && count[i] >= minimum && (!own[i] || optionOf[i] != null))
                .OrderByDescending(i => i == selectedGroup).ThenByDescending(i => optionOf[i]?.Selectable == true).ThenByDescending(i => count[i]);
            int open = 0, closed = 0;
            foreach (int i in order)
            {
                ClaimTargetOption option = optionOf[i];
                bool can = option?.Selectable == true;
                if (i != selectedGroup && (can ? open++ >= 12 : closed++ >= 6)) continue;
                if (!labelled.Add(i)) continue;
                string name = _level == Level.Empire ? map.Kingdoms[i].GetEmpire()?.GetEmpireName() ?? map.Kingdoms[i].GetKingdomName()
                    : map.Kingdoms[i].GetKingdomName();
                view.AddLabel(sumX[i] / count[i], sumY[i] / count[i], name,
                    i == selectedGroup ? Color.white : can ? new Color(0.95f, 0.93f, 0.89f) : new Color(0.6f, 0.64f, 0.6f), i == selectedGroup ? 7 : 6);
            }
            // 本朝：各成员合起来标一个名字
            float ownX = 0f, ownY = 0f;
            int ownCount = 0;
            for (int i = 0; i < kingdoms; i++)
            {
                if (!own[i] || optionOf[i]?.Selectable == true) continue;
                ownX += sumX[i];
                ownY += sumY[i];
                ownCount += count[i];
            }
            if (ownCount >= minimum)
                view.AddLabel(ownX / ownCount, ownY / ownCount, _empire.GetEmpireName(), new Color(0.95f, 0.76f, 0.29f), 7);
        }

        _hoverText = card.AddParagraph(PageWidth, LegendText(), 6, WideKit.Soft);
    }

    // 图例(字色即图上的颜色)
    private string LegendText() => string.Join("  ",
        LM.Get("claim_target_legend_own").ColorString(ByCity ? "#B8A465" : "#C9A44A"),
        LM.Get(_level == Level.Title ? "claim_target_legend_open_title" : _level == Level.City ? "claim_target_legend_open_city"
            : "claim_target_legend_open").ColorString(WideKit.Cyan),
        LM.Get("claim_target_legend_closed").ColorString("#8A8F80"),
        LM.Get("claim_target_legend_picked").ColorString("#FFFFFF"),
        LM.Get("claim_target_legend_click").ColorString(WideKit.Muted));

    // 指着图上哪一格：图例那行换成它是谁、能不能选、为什么
    private void HoverMap(MapData map, int[] group, ClaimTargetOption[] optionOf, ClaimTargetOption[] cityOption, bool[] own, int x, int y)
    {
        if (_hoverText?.text == null) return;
        bool cityLevel = ByCity;
        string text = LegendText();
        int owner = x < 0 ? -2 : map.Owner[y * map.Width + x];
        if (owner >= 0)
        {
            Kingdom kingdom = map.Kingdoms[owner];
            ClaimTargetOption option;
            string name;
            if (cityLevel)
            {
                int city = map.City[y * map.Width + x];
                option = city >= 0 ? cityOption[city] : null;
                name = city >= 0 ? $"{map.Cities[city].GetCityName()}({kingdom.GetKingdomName()})" : kingdom.GetKingdomName();
                if (option?.Title != null) name = $"{option.Name} · {name}";
            }
            else
            {
                option = optionOf[owner];
                name = _level == Level.Empire && kingdom.GetEmpire() is Empire empire && !empire.isRekt()
                    ? $"{empire.GetEmpireName()} · {kingdom.GetKingdomName()}" : kingdom.GetKingdomName();
            }
            string state = option == null ? LM.Get(own[owner] ? "claim_target_hover_own" : "claim_target_hover_none")
                : option.Selectable ? LM.Get("claim_target_hover_open") + (string.IsNullOrEmpty(option.Why) ? "" : " · " + option.Why)
                : LM.Get("claim_target_hover_closed") + option.Why;
            text = name.ColorString(WideKit.Gold) + "  " +
                   state.ColorString(option == null ? WideKit.Muted : option.Selectable ? WideKit.Green : WideKit.Red);
        }
        _hoverText.text.text = text;
    }

    private void ClickMap(MapData map, int[] group, ClaimTargetOption[] optionOf, ClaimTargetOption[] cityOption,
        List<(int x, int y, ClaimTargetOption option)> markers, int x, int y)
    {
        int at = y * map.Width + x;
        if (ByCity)
        {
            int city = map.City[at];
            ClaimTargetOption option = city >= 0 ? cityOption[city] : null;
            // 没点在候选城的地上：取近处的城标
            if (option == null)
            {
                int best = 81;
                foreach ((int mx, int my, ClaimTargetOption marker) in markers)
                {
                    int distance = (mx - x) * (mx - x) + (my - y) * (my - y);
                    if (distance >= best) continue;
                    best = distance;
                    option = marker;
                }
            }
            if (option != null) Select(_level, option.Id);
            return;
        }
        int owner = map.Owner[at];
        if (owner < 0 || optionOf[owner] == null) return;
        Select(_level, optionOf[owner].Id);
    }

    #endregion

    #region 人物

    private string OfficeOf(Actor actor) => actor != null && actor.IsOnOffice() ? actor.GetOffice().GetOfficeName() : LM.Get("claim_target_no_office");

    private string FactionOf(Actor actor) => actor?.GetFaction()?.Name ?? LM.Get("claim_target_no_faction");

    private static string ClanOf(Actor actor) =>
        actor?.GetPersonalIdentity()?._specificClan?.name ?? actor?.clan?.name ?? LM.Get("claim_target_no_clan");

    // 身份：储君、皇帝的子女、皇帝的兄弟、宗室(与皇帝同族)、朝臣、其余
    private string IdentityOf(Actor actor)
    {
        Actor emperor = _empire.Emperor;
        if (actor == _empire.CoreKingdom.GetHeir()) return LM.Get("claim_target_identity_heir");
        if (emperor?.data != null && actor.data != null)
        {
            long id = emperor.data.id;
            if (actor.data.parent_id_1 == id || actor.data.parent_id_2 == id)
                return LM.Get(actor.isSexFemale() ? "claim_target_identity_princess" : "claim_target_identity_prince");
            long father = emperor.data.parent_id_1, mother = emperor.data.parent_id_2;
            if (father >= 0 && (actor.data.parent_id_1 == father || actor.data.parent_id_2 == father) ||
                mother >= 0 && (actor.data.parent_id_1 == mother || actor.data.parent_id_2 == mother))
                return LM.Get("claim_target_identity_sibling");
            if (ClanOf(actor) == ClanOf(emperor)) return LM.Get("claim_target_identity_royal");
        }
        return LM.Get(actor.IsOnOffice() ? "claim_target_identity_official" : "claim_target_identity_other");
    }

    private string GroupOf(Actor actor) => _grouping switch
    {
        1 => actor.IsOnOffice() ? OfficialRankSystem.PanelLabel(actor) : LM.Get("claim_target_no_office"),
        2 => ClanOf(actor),
        3 => FactionOf(actor),
        _ => IdentityOf(actor)
    };

    private void DrawActors(AutoVertLayoutGroup page, List<ClaimTargetOption> listed)
    {
        var bar = page.BeginCard(PageWidth, "claim_target_card_grouping", WideKit.Cyan, LM.Get("claim_target_grouping_hint"));
        string[] keys = { "claim_target_group_identity", "claim_target_group_office", "claim_target_group_clan", "claim_target_group_faction" };
        bar.AddChipRow(PageWidth - Pad, keys.Select((key, index) => (LM.Get(key), index == _grouping, (UnityAction)(() =>
        {
            _grouping = index;
            Refresh();
        }))).ToArray());

        var groups = new List<(string name, List<ClaimTargetOption> people)>();
        foreach (ClaimTargetOption option in listed)
        {
            if (option.Actor?.data == null || option.Actor.isRekt()) continue;
            string name = GroupOf(option.Actor);
            int at = groups.FindIndex(group => group.name == name);
            if (at < 0)
            {
                groups.Add((name, new List<ClaimTargetOption>()));
                at = groups.Count - 1;
            }
            groups[at].people.Add(option);
        }
        if (groups.Count == 0) page.AddEmptyCard(PageWidth, LM.Get("claim_target_title"), LM.Get("claim_target_list_none"));
        foreach ((string name, List<ClaimTargetOption> people) in groups)
        {
            var card = page.BeginCard(PageWidth, string.Format(LM.Get("claim_target_card_group"), name, people.Count), WideKit.Gold, null,
                "ui/icons/iconPopulation");
            for (int i = 0; i < people.Count; i++) DrawPerson(card, people[i], i);
        }
    }

    // 一张人物卡：头像 + 标记(本派、朝廷会挑、各派赞反；不可选的写原因) / 姓名 / 年岁、官职、派系、名望；整行可点
    private void DrawPerson(AutoVertLayoutGroup card, ClaimTargetOption option, int index)
    {
        Actor actor = option.Actor;
        bool selected = option.Id == _selected;
        var tags = new List<string>();
        if (!option.Selectable) tags.Add((LM.Get("claim_target_hover_closed") + option.Why).ColorString(WideKit.Red));
        else
        {
            if (!string.IsNullOrEmpty(option.Why)) tags.Add(option.Why.ColorString(WideKit.Green));
            if (IsAuto(option)) tags.Add(LM.Get("claim_target_auto_pick").ColorString(WideKit.Orange));
            (float chance, int pro, int con) = Outlook(option);
            if (chance >= 0f)
                tags.Add(string.Format(LM.Get("claim_target_sides"), pro, con).ColorString(pro >= con ? WideKit.Green : WideKit.Red) + " " +
                         string.Format(LM.Get("claim_target_chance"), Mathf.RoundToInt(chance * 100f)).ColorString(ChanceHex(chance)));
        }
        string detail = string.Format(LM.Get("claim_target_person_detail"), actor.getAge(), OfficeOf(actor), FactionOf(actor), actor.renown);
        var row = card.AddPersonRow(PageWidth, actor, string.Join("  ", tags), (selected ? "› " : "") + actor.getName(), detail);
        if (selected || index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(PageWidth - Pad, 32f));
        long id = option.Id;
        UIHelper.MakeClickable(row.gameObject, () => Select(Level.Actor, id));
    }

    #endregion
}
