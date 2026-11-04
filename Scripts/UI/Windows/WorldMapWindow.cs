using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 世界地图(从原版新世界窗口的模板按钮打开，见 WorldMapTemplatePatch)，「卷册」宽窗口：
//   · 左栏(固定)：标题 → 地图范围 → 地图大小 → 生成世界
//   · 右栏：选中的剧本(说明、主指标) → 东方、西方两组预设卡 → 自定剧本卡与操作
// 预设可"存为自定剧本"复制一份去改，"打开剧本文件夹"拷给别人分享；"从当前世界导入"把读着的存档(连同行政区、法理、外交)
// 写成自定剧本，直接进编辑器接着改(见 ScenarioWorldExport)；自定剧本可删，删除要连点两下
public class WorldMapWindow : WideScrollWindow<WorldMapWindow>
{
    private const float CardLine = 12f, CardSubLine = 9f;
    private const string MapIcon = "ui/icons/iconKingdomZones";

    private static int _frame;
    private static int _size = 1;
    private static string _era;
    // 第一下点删除记下要删哪个(按钮变红字)，再点才删，换选别的剧本就作废
    private static string _deleting;

    private bool _ready;
    private WorldMapEra _selected;
    private WorldMapFrame _map;
    private string _importNote;

    protected override void OnOpen()
    {
        _deleting = null;
        WorldMapData.LoadCustom();
    }

    protected override void Prepare()
    {
        _ready = WorldMapData.EnsureLoaded();
        if (!_ready) return;
        _selected = WorldMapData.Eras.Concat(WorldMapData.Custom).FirstOrDefault(era => era.id == _era) ?? WorldMapData.Eras[0];
        _era = _selected.id;
        _map = EarthTerrain.Frames[Mathf.Clamp(_frame, 0, EarthTerrain.Frames.Length - 1)];
        _importNote = null;
        // 导入地图的自定剧本：范围照图(见 WorldMapImporter)，只选大小
        if (!WorldMapImporter.Uses(_selected)) return;
        WorldMapImporter.Loaded loaded = WorldMapImporter.Load(_selected, out string importError);
        if (loaded != null) _map = loaded.frame;
        _importNote = (loaded != null
                ? string.Format(LM.Get("world_map_imported_frame"), loaded.cols, loaded.rows)
                : LM.Get(importError)).ColorString(loaded != null ? WideKit.Value : WideKit.Orange);
    }

    #region 左栏

    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        rail.AddRailTitle(LM.Get("world_map_title"), LM.Get("world_map_subtitle"));
        if (!_ready) return;

        var scope = rail.BeginRailMenu(LM.Get("world_map_rail_frame"));
        if (_importNote != null)
            scope.AddRailText(_importNote, 6, RailLines(_importNote) * 8.5f + 2f, true);
        else
            for (int i = 0; i < EarthTerrain.Frames.Length; i++)
            {
                int index = i;
                scope.AddRailOption(null, LM.Get("world_map_frame_" + EarthTerrain.Frames[i].id), null, i == _frame,
                    () => Select(() => _frame = index), i);
            }

        var sizes = rail.BeginRailMenu(LM.Get("world_map_rail_size"));
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            sizes.AddRailOption(null, LM.Get("world_map_size_" + i), null, i == _size, () => Select(() => _size = index), i);
        }
        Vector2Int size = _map.sizes[Mathf.Clamp(_size, 0, _map.sizes.Length - 1)];
        string hint = string.Format(LM.Get("world_map_size_hint"), size.x * 64, size.y * 64,
            LM.Get(_size == 0 ? "world_map_size_hint_small" : "world_map_size_hint_full"));
        sizes.AddRailText(hint.ColorString(WideKit.Muted), 6, RailLines(hint) * 8.5f + 2f, true);

        var go = rail.BeginRailCard("FactionFrame_dominate");
        string notice = LM.Get("world_map_replace_notice");
        go.AddRailText(notice.ColorString(WideKit.Orange), 6, RailLines(notice) * 8.5f + 2f, true);
        WorldMapEra chosen = _selected;
        WorldMapFrame frame = _map;
        go.AddRailLink(SpriteTextureLoader.getSprite(MapIcon), LM.Get("world_map_generate"),
            () => WorldMapGenerator.Start(frame, _size, chosen), WideKit.Gold);
    }

    private static int RailLines(string text) =>
        Mathf.Max(1, Mathf.CeilToInt(UIHelper.EstimateTextWidth(text, 6) * 1.08f / (UIHelper.RailWidth - 12f)));

    #endregion

    #region 右栏

    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        if (!_ready)
        {
            page.AddEmptyCard(PageWidth, "world_map_data_missing");
            return;
        }
        DrawSelected(page);
        DrawGroup(page, "world_map_east", WideKit.Orange, WorldMapData.Eras.Where(era => era.East).ToList());
        DrawGroup(page, "world_map_west", WideKit.Cyan, WorldMapData.Eras.Where(era => !era.East).ToList());
        DrawCustom(page);
    }

    private void DrawSelected(AutoVertLayoutGroup page)
    {
        var card = page.BeginCard(PageWidth, _selected.Name, WideKit.Gold, _selected.YearText.ColorString(WideKit.Cyan));
        card.AddParagraph(PageWidth, _selected.Description, 7, WideKit.Value);
        if (_selected.Custom)
        {
            if (!string.IsNullOrWhiteSpace(_selected.author))
                card.AddParagraph(PageWidth, string.Format(LM.Get("world_map_custom_author"), _selected.author), 6, WideKit.Muted);
            if (_selected.OwnWorld)
                card.AddParagraph(PageWidth, string.Format(LM.Get("world_map_custom_own_world"), _selected.cities.Count), 6, WideKit.Muted);
            if (_selected.problems.Count > 0)
                card.AddParagraph(PageWidth, string.Format(LM.Get("world_map_custom_problems"), _selected.problems.Count,
                    string.Join(LM.Get("list_separator"), _selected.problems.Take(3))), 6, WideKit.Orange);
        }
        page.AddKpiRow(PageWidth,
            (LM.Get("world_map_kpi_realms"), _selected.polities.Count.ToString(), WideKit.Gold),
            (LM.Get("world_map_kpi_cities"), WorldMapData.CityCount(_selected, _size == 0).ToString(), WideKit.Green),
            (LM.Get("world_map_kpi_wars"), (_selected.relations?.Count(item => item.kind == "war") ?? 0).ToString(), WideKit.Red),
            (LM.Get("world_map_kpi_events"), (_selected.events?.Count ?? 0).ToString(), WideKit.Cyan));
    }

    private void DrawGroup(AutoVertLayoutGroup page, string title, string hex, List<WorldMapEra> eras)
    {
        page.AddSectionHeader(PageWidth, title, hex);
        page.AddCardGrid(PageWidth, eras, 2, AddEraCard);
    }

    // 一个剧本一张卡：名字 + 开局年、国家数；选中的金框金字，整卡可点
    private void AddEraCard(AutoHoriLayoutGroup row, float width, WorldMapEra era)
    {
        bool on = era.id == _era;
        var card = row.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(7, 6, 5, 5));
        UIHelper.FixPanelWidth(card.transform, width);
        UIHelper.AddCardFrame(card.transform, on ? "FactionFrame_dominate" : "FactionFrame");
        var name = card.AddTextIntoVertLayout(era.Name.ColorString(on ? WideKit.Gold : WideKit.Value), true, TextAnchor.MiddleLeft,
            new Vector2(width - 13f, CardLine));
        name.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(name);
        string meta = string.Format(LM.Get("world_map_card_meta"), era.YearText, era.polities.Count);
        var sub = card.AddTextIntoVertLayout(meta.ColorString(on ? WideKit.Soft : WideKit.Muted), true, TextAnchor.MiddleLeft,
            new Vector2(width - 13f, CardSubLine));
        sub.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(sub);
        UIHelper.MakeClickable(card.gameObject, () => Select(() => PickEra(era)));
    }

    private void DrawCustom(AutoVertLayoutGroup page)
    {
        page.AddSectionHeader(PageWidth, $"{LM.Get("world_map_custom")}  ×{WorldMapData.Custom.Count}", WideKit.Green, false);
        page.AddCardGrid(PageWidth, WorldMapData.Custom, 2, AddEraCard);
        var tools = page.BeginCard(PageWidth);
        if (WorldMapData.Custom.Count == 0)
            tools.AddParagraph(PageWidth, LM.Get("world_map_custom_empty"), 7, WideKit.Muted);
        // 选中自定剧本：编辑、删除它；选中预设：存为自定剧本
        WorldMapEra selected = _selected;
        if (selected.Custom)
            tools.AddButtonRow(PageWidth,
                ("world_map_custom_edit", LM.Get("world_map_custom_edit").ColorString(WideKit.Gold), () => Edit(selected)),
                ("world_map_custom_delete", _deleting == selected.id
                    ? LM.Get("world_map_custom_delete_confirm").ColorString(WideKit.Red)
                    : LM.Get("world_map_custom_delete").ColorString(WideKit.Orange), () => Delete(selected)));
        else
            tools.AddButtonRow(PageWidth,
                ("world_map_custom_copy", LM.Get("world_map_custom_copy").ColorString(WideKit.Gold), () => CopyAsCustom(selected)));
        // 从当前世界导入：读着的世界没有文明城时按钮灰着，点了只提示
        tools.AddButtonRow(PageWidth,
            ("world_map_custom_blank", LM.Get("world_map_custom_blank"), NewBlank),
            ("world_map_custom_import", LM.Get("world_map_custom_import").ColorString(ScenarioWorldExport.CanExport ? WideKit.Gold : WideKit.Muted),
                ImportWorld));
        tools.AddButtonRow(PageWidth,
            ("world_map_custom_folder", LM.Get("world_map_custom_folder"), OpenFolder),
            ("world_map_custom_refresh", LM.Get("world_map_custom_refresh"), Reload));
    }

    #endregion

    #region 操作

    // 选剧本：剧本记了地图范围(自定剧本，见剧本编辑器)就换到那档
    private static void PickEra(WorldMapEra era)
    {
        _era = era.id;
        int index = global::System.Array.FindIndex(EarthTerrain.Frames, frame => frame.id == era.frame);
        if (index >= 0) _frame = index;
    }

    private static string FrameId => EarthTerrain.Frames[Mathf.Clamp(_frame, 0, EarthTerrain.Frames.Length - 1)].id;

    // 预设存为自定剧本：拷进剧本文件夹并选中它(记下现在选的地图范围)
    private void CopyAsCustom(WorldMapEra preset)
    {
        string name = WorldMapData.CopyAsCustom(preset, FrameId);
        if (name == null)
        {
            WorldTip.showNow("world_map_custom_copy_failed", true, "top", 3f);
            return;
        }
        WorldMapData.LoadCustom();
        _era = "custom/" + name;
        WorldTip.showNow(string.Format(LM.Get("world_map_custom_copied"), global::System.IO.Path.Combine(WorldMapData.CustomFolder, name)),
            false, "top", 5f);
        Refresh();
    }

    // 删除自定剧本：连点两下才删，删的是整个剧本文件夹，找不回来
    private void Delete(WorldMapEra custom)
    {
        if (_deleting != custom.id)
        {
            _deleting = custom.id;
            Refresh();
            return;
        }
        _deleting = null;
        string name = custom.Name;
        if (WorldMapData.DeleteCustom(custom))
        {
            _era = null;
            WorldTip.showNow(string.Format(LM.Get("world_map_custom_deleted"), name), false, "top", 3f);
        }
        else WorldTip.showNow("world_map_custom_delete_failed", true, "top", 3f);
        Refresh();
    }

    // 打开剧本编辑器(全屏，见 ScenarioEditorScreen)
    private static void Edit(WorldMapEra custom)
    {
        if (custom == null || !custom.Custom) return;
        ScrollWindow.hideAllEvent();
        ScenarioEditorScreen.Open(custom);
    }

    // 新建空白剧本：内置地球地图上的城各自独立、没有国家，建好直接进编辑器
    private void NewBlank()
    {
        string name = ScenarioEditing.CreateBlank(FrameId);
        if (name == null)
        {
            WorldTip.showNow("world_map_custom_copy_failed", true, "top", 3f);
            return;
        }
        WorldMapData.LoadCustom();
        _era = "custom/" + name;
        WorldMapEra created = WorldMapData.Custom.FirstOrDefault(era => era.id == _era);
        if (created != null) Edit(created);
        else Refresh();
    }

    // 从当前世界导入(用户 10-30 "现在的剧本编辑器可以导入已经设置好行政区的存档嘛"→"都做")：先在游戏里读好存档再来点，
    // 不另问什么——剧本名取世界名(重名的文件夹加 _2…)，写好就选中它、进编辑器(同「编辑剧本」)
    private void ImportWorld()
    {
        if (!ScenarioWorldExport.CanExport)
        {
            WorldTip.showNow("scenario_export_no_world", true, "top", 3f);
            return;
        }
        if (!ScenarioWorldExport.Export(ScenarioWorldExport.DefaultName(), out string name, out string error))
        {
            WorldTip.showNow(error ?? "scenario_export_failed", true, "top", 4f);
            return;
        }
        WorldMapData.LoadCustom();
        _era = "custom/" + name;
        WorldMapEra created = WorldMapData.Custom.FirstOrDefault(era => era.id == _era);
        if (created != null)
            WorldTip.showNow(string.Format(LM.Get("world_map_custom_imported"), global::System.IO.Path.Combine(WorldMapData.CustomFolder, name),
                created.cities?.Count ?? 0, created.polities.Count, created.polities.Sum(polity => polity.divisions?.Count ?? 0)), false, "top", 5f);
        if (created != null) Edit(created);
        else Refresh();
    }

    private static void OpenFolder()
    {
        try
        {
            global::System.IO.Directory.CreateDirectory(WorldMapData.CustomFolder);
            Application.OpenURL("file:///" + WorldMapData.CustomFolder.Replace('\\', '/'));
        }
        catch (global::System.Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][世界地图] 打不开剧本文件夹: {exception.Message}");
        }
    }

    private void Reload()
    {
        WorldMapData.LoadCustom();
        Refresh();
    }

    private void Select(global::System.Action change)
    {
        change();
        _deleting = null;
        Refresh();
    }

    #endregion
}
