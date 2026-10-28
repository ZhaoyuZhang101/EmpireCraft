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
// 主西方三档)，生成一张按地球高程铺地表、按所选时代摆城建国的世界。版式同纯净地图提示窗：金框标题卡 + 卡片 + 按钮行
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
        WorldMapEra selected = WorldMapData.Eras.FirstOrDefault(era => era.id == _era) ?? WorldMapData.Eras[0];
        _era = selected.id;

        WorldMapFrame frame = EarthTerrain.Frames[Mathf.Clamp(_frame, 0, EarthTerrain.Frames.Length - 1)];
        var area = panel.BeginCard(Width, "world_map_frame", WideKit.Cyan);
        area.AddButtonRow(Width, EarthTerrain.Frames.Select((item, index) =>
            ("world_map_frame_" + item.id, Choice(LM.Get("world_map_frame_" + item.id), index == _frame),
                (UnityEngine.Events.UnityAction)(() => Select(() => _frame = index)))).ToArray());
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
                    () => Select(() => _era = id), era == selected ? WideKit.Gold : WideKit.Soft, Width - 24f);
            }
        }

        var detail = panel.BeginCard(Width, "world_map_era_desc", WideKit.Green, selected.Name);
        Paragraph(detail, selected.Description.ColorString(WideKit.Value));

        var actions = panel.BeginCard(Width);
        Paragraph(actions, LM.Get("world_map_replace_notice").ColorString(WideKit.Orange));
        actions.AddButtonRow(Width,
            ("world_map_generate", LM.Get("world_map_generate").ColorString(WideKit.Gold),
                () => WorldMapGenerator.Start(frame, _size, selected)),
            ("world_map_cancel", LM.Get("world_map_cancel"), () => ScrollWindow.hideAllEvent()));
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
