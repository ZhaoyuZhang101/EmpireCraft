using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 世界地图(从原版新世界窗口的模板按钮打开，见 WorldMapTemplatePatch)：选范围(欧亚非 / 亚欧非全图)、大小三档、时代(主东方三档、
// 主西方三档)，生成一张按地球高程铺地表、按所选时代摆城建国的世界。版式同纯净地图提示窗：金框标题卡 + 卡片 + 按钮行。
// 自定剧本(剧本编辑器第一期，见 WorldMapData.LoadCustom)另列一卡：剧本文件夹里的剧本与预设一样选了就能开局，
// 预设可"存为自定剧本"复制一份去改，"打开剧本文件夹"拷给别人分享；"从当前世界导入"把读着的存档(连同行政区、法理、外交)
// 写成自定剧本，直接进编辑器接着改(见 ScenarioWorldExport)
public class WorldMapWindow : AutoLayoutWindow<WorldMapWindow>
{
    private const float Width = 220f;
    private static int _frame;
    private static int _size = 1;
    private static string _era;
    private readonly List<GameObject> _groups = new();

    protected override void Init()
    {
        layout.spacing = 4;
        layout.padding = new RectOffset(4, 4, 6, 4);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        WorldMapData.LoadCustom();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (GameObject group in _groups)
            if (group != null) Destroy(group);
        _groups.Clear();
        AutoVertLayoutGroup panel = this.BeginVertGroup(pSpacing: UIHelper.CardGap, pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(0, 0, 0, 0));
        UIHelper.FixPanelWidth(panel.transform, Width);
        _groups.Add(panel.gameObject);

        var head = panel.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(6, 6, 5, 5));
        UIHelper.FixPanelWidth(head.transform, Width);
        UIHelper.AddCardFrame(head.transform, "FactionFrame_dominate");
        Line(head, LM.Get("world_map_title").ColorString(WideKit.Gold), 11, 15f, TextAnchor.MiddleCenter);
        Line(head, LM.Get("world_map_subtitle").ColorString(WideKit.Muted), 7, 10f, TextAnchor.MiddleCenter);

        if (!WorldMapData.EnsureLoaded())
        {
            var missing = panel.BeginCard(Width);
            Paragraph(missing, LM.Get("world_map_data_missing").ColorString(WideKit.Orange));
            return;
        }
        WorldMapEra selected = WorldMapData.Eras.Concat(WorldMapData.Custom).FirstOrDefault(era => era.id == _era) ??
                               WorldMapData.Eras[0];
        _era = selected.id;

        WorldMapFrame frame = EarthTerrain.Frames[Mathf.Clamp(_frame, 0, EarthTerrain.Frames.Length - 1)];
        var area = panel.BeginCard(Width, "world_map_frame", WideKit.Cyan);
        // 导入地图的自定剧本：范围照图(见 WorldMapImporter)，只选大小
        if (WorldMapImporter.Uses(selected))
        {
            WorldMapImporter.Loaded loaded = WorldMapImporter.Load(selected, out string importError);
            if (loaded != null) frame = loaded.frame;
            Line(area, (loaded != null
                    ? string.Format(LM.Get("world_map_imported_frame"), loaded.cols, loaded.rows)
                    : LM.Get(importError)).ColorString(loaded != null ? WideKit.Value : WideKit.Orange),
                7, 10f, TextAnchor.MiddleCenter);
        }
        else
            // 四档两行：全图两档、半幅两档
            for (int first = 0; first < EarthTerrain.Frames.Length; first += 2)
                area.AddButtonRow(Width, EarthTerrain.Frames.Select((item, index) =>
                        ("world_map_frame_" + item.id, Choice(LM.Get("world_map_frame_" + item.id), index == _frame),
                            (UnityEngine.Events.UnityAction)(() => Select(() => _frame = index))))
                    .Skip(first).Take(2).ToArray());
        area.AddButtonRow(Width, Enumerable.Range(0, 3).Select(index =>
            ("world_map_size_" + index, Choice(LM.Get("world_map_size_" + index), index == _size),
                (UnityEngine.Events.UnityAction)(() => Select(() => _size = index)))).ToArray());
        Vector2Int size = frame.sizes[Mathf.Clamp(_size, 0, frame.sizes.Length - 1)];
        Line(area, string.Format(LM.Get("world_map_size_hint"), size.x * 64, size.y * 64,
            LM.Get(_size == 0 ? "world_map_size_hint_small" : "world_map_size_hint_full")).ColorString(WideKit.Muted),
            6, 10f, TextAnchor.MiddleCenter);

        foreach (bool east in new[] { true, false })
        {
            var card = panel.BeginCard(Width, east ? "world_map_east" : "world_map_west", east ? WideKit.Orange : WideKit.Cyan);
            int row = 0;
            foreach (WorldMapEra era in WorldMapData.Eras.Where(era => era.East == east))
            {
                string id = era.id;
                card.AddListRow(Width, row++, null, $"{era.Name}  {era.YearText}", null,
                    () => Select(() => PickEra(era)), era == selected ? WideKit.Gold : WideKit.Soft, Width - 24f);
            }
        }

        var custom = panel.BeginCard(Width, "world_map_custom", WideKit.Green);
        if (WorldMapData.Custom.Count == 0) Paragraph(custom, LM.Get("world_map_custom_empty").ColorString(WideKit.Muted));
        int customRow = 0;
        foreach (WorldMapEra era in WorldMapData.Custom)
        {
            string id = era.id;
            custom.AddListRow(Width, customRow++, null, $"{era.Name}  {era.YearText}", null,
                () => Select(() => PickEra(era)), era == selected ? WideKit.Gold : WideKit.Soft, Width - 24f);
        }
        // 选中自定剧本：编辑它；选中预设：存为自定剧本。都可以新建空白剧本(建好直接进编辑器)
        if (selected.Custom)
            custom.AddButtonRow(Width,
                ("world_map_custom_edit", LM.Get("world_map_custom_edit").ColorString(WideKit.Gold), () => Edit(selected)),
                ("world_map_custom_blank", LM.Get("world_map_custom_blank"), NewBlank));
        else
            custom.AddButtonRow(Width,
                ("world_map_custom_copy", LM.Get("world_map_custom_copy").ColorString(WideKit.Gold), () => CopyAsCustom(selected)),
                ("world_map_custom_blank", LM.Get("world_map_custom_blank"), NewBlank));
        // 从当前世界导入：读着的世界没有文明城时按钮灰着，点了只提示
        custom.AddButtonRow(Width,
            ("world_map_custom_import", LM.Get("world_map_custom_import").ColorString(ScenarioWorldExport.CanExport ? WideKit.Gold : WideKit.Muted),
                ImportWorld));
        custom.AddButtonRow(Width,
            ("world_map_custom_folder", LM.Get("world_map_custom_folder"), OpenFolder),
            ("world_map_custom_refresh", LM.Get("world_map_custom_refresh"), Refresh));

        var detail = panel.BeginCard(Width, "world_map_era_desc", WideKit.Green, selected.Name);
        Paragraph(detail, selected.Description.ColorString(WideKit.Value));
        if (selected.Custom)
        {
            if (!string.IsNullOrWhiteSpace(selected.author))
                Paragraph(detail, string.Format(LM.Get("world_map_custom_author"), selected.author).ColorString(WideKit.Muted));
            if (selected.OwnWorld)
                Paragraph(detail, string.Format(LM.Get("world_map_custom_own_world"), selected.cities.Count).ColorString(WideKit.Muted));
            if (selected.problems.Count > 0)
                Paragraph(detail, string.Format(LM.Get("world_map_custom_problems"), selected.problems.Count,
                    string.Join(LM.Get("list_separator"), selected.problems.Take(3))).ColorString(WideKit.Orange));
        }

        var actions = panel.BeginCard(Width);
        Paragraph(actions, LM.Get("world_map_replace_notice").ColorString(WideKit.Orange));
        actions.AddButtonRow(Width,
            ("world_map_generate", LM.Get("world_map_generate").ColorString(WideKit.Gold),
                () => WorldMapGenerator.Start(frame, _size, selected)),
            ("world_map_cancel", LM.Get("world_map_cancel"), () => ScrollWindow.hideAllEvent()));
    }

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
        Rebuild();
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
        else Rebuild();
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
        else Rebuild();
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

    private void Refresh()
    {
        WorldMapData.LoadCustom();
        Rebuild();
    }

    private void Select(global::System.Action change)
    {
        change();
        Rebuild();
    }

    private static string Choice(string label, bool selected) => selected ? $"[{label}]".ColorString(WideKit.Gold) : label;

    private static void Paragraph(AutoVertLayoutGroup card, string text)
    {
        float inner = Width - 12f;
        int lines = 0;
        foreach (string paragraph in (text ?? "").Split('\n'))
            lines += Mathf.Max(1, Mathf.CeilToInt(UIHelper.EstimateTextWidth(paragraph, 7) / (inner - 2f)));
        var body = card.AddTextIntoVertLayout(text, true, TextAnchor.UpperLeft, new Vector2(inner, lines * 10f + 2f));
        body.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
    }

    private static void Line(AutoVertLayoutGroup card, string text, int fontSize, float height, TextAnchor anchor)
    {
        var label = card.AddTextIntoVertLayout(text, true, anchor, new Vector2(Width - 12f, height));
        label.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
    }
}
