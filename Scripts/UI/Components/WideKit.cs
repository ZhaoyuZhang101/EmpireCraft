using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 「卷册」组件库：宽窗口(WideScrollWindow)常用部件，一行调用。约定：
//   · 左栏部件宽度固定为 UIHelper.RailWidth；
//   · 右栏页面级部件(AddKpiRow、AddCardGrid、AddEmptyCard)传页宽(PageWidth / UIHelper.PageMainWidth)；
//   · 卡片内部件(AddColorStrip、AddPersonRow、AddMetricRow、AddListRow)传卡片外宽，内部自动扣掉卡片左右内边距 12，
//     与 UIHelper.AddCardTitle / AddHeroStat 一致；
//   · 颜色用 WideKit 里的常量(金、青、灰、绿、红、橙、值)，数值好坏用 Grade 取色。
// 典型页面：
//   rail.AddRailTitle("天下帝国", "现存 5 · 已亡 0");
//   var menu = rail.BeginRailMenu("按文化");  menu.AddRailOption(icon, "华夏", "3", selected, () => {...}, 0);
//   page.AddPageHeader(PageWidth, "现存帝国", "5 国");
//   page.AddKpiRow(PageWidth, ("辖城", "197", WideKit.Green), ("人口", "40万", WideKit.Cyan), ("最久", "633年", WideKit.Gold));
//   page.AddCardGrid(PageWidth, empires, 2, (row, width, empire) =>
//   {
//       var card = row.BeginColumnCard(width);
//       card.AddColorStrip(width, color);
//       UIHelper.AddCardTitle(card, width, name, WideKit.Hex(textColor), "帝制", 0f);
//       card.AddPersonRow(width, emperor, "皇帝", emperor.getName(), "年号 崇恒 · 在位 26 年");
//       card.AddMetricRow(width, ("城", "31", WideKit.Green), ("正统", "71", WideKit.Grade(71, 70, 40)));
//       UIHelper.MakeClickable(card.gameObject, () => Open(empire));
//   });
//   var list = page.BeginCard(PageWidth, "已亡帝国", WideKit.Muted, "3");
//   list.AddListRow(PageWidth, 0, icon, "某朝", "延续 120 年 · 末代 某某", () => OpenHistory());
//   card.AddButtonRow(PageWidth, ("act_a", "加冕", () => {...}), ("act_b", "绝罚", () => {...}));
//   card.AddTableRow(PageWidth, -1, null, "物资", 80f, ("年产", null), ("年需", null));          // 表头
//   card.AddTableRow(PageWidth, 0, icon, "粮食", 80f, ("1.2万", WideKit.Value), ("1.5万", WideKit.Red));
public static class WideKit
{
    public const string Gold = "#F3C34A", Cyan = "#7FD8EA", Muted = "#8FA0A8", Green = "#65D66E",
        Red = "#FF6666", Orange = "#E9A85B", Value = "#F2EEE2", Soft = "#C8C2B4";

    private const float CardPadding = 12f;

    public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

    // 数值好坏取色：不低于 good 绿，不低于 fair 金，否则红(反过来"越低越好"的传 higherIsBetter: false)
    public static string Grade(float value, float good, float fair, bool higherIsBetter = true) => higherIsBetter
        ? value >= good ? Green : value >= fair ? Gold : Red
        : value <= good ? Green : value <= fair ? Gold : Red;

    #region 左栏

    // 左栏首卡：金色标题 + 灰色副标题
    public static AutoVertLayoutGroup AddRailTitle(this AutoVertLayoutGroup rail, string title, string subtitle = null,
        string frame = "FactionFrame_dominate")
    {
        var card = rail.BeginRailCard(frame);
        card.AddRailText(title.ColorString(Gold), 10, 14f);
        if (!string.IsNullOrEmpty(subtitle)) card.AddRailText(subtitle.ColorString(Muted), 6, 9f);
        return card;
    }

    // 左栏菜单卡(放 AddRailOption)：可带青色小标题
    public static AutoVertLayoutGroup BeginRailMenu(this AutoVertLayoutGroup rail, string heading = null)
    {
        var card = rail.BeginRailCard();
        if (!string.IsNullOrEmpty(heading)) card.AddRailText(heading.ColorString(Cyan), 7, 10f);
        return card;
    }

    // 左栏可选一行：图标 + 名称 + 右侧数量，选中的金字垫深色底，整行可点(筛选、翻页、主从列表)
    public static AutoHoriLayoutGroup AddRailOption(this AutoVertLayoutGroup menu, Sprite icon, string label, string count,
        bool selected, UnityAction action, int index)
    {
        float inner = UIHelper.RailWidth - 8f;
        var row = menu.BeginHoriGroup(new Vector2(inner, 15f), TextAnchor.MiddleLeft, 3, new RectOffset(3, 3, 0, 0));
        if (selected || index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, 15f));
        UIHelper.AddLayoutIcon(row.transform, icon, 10f);
        float countWidth = string.IsNullOrEmpty(count) ? 0f : 21f;
        float labelWidth = inner - 6f - (icon != null ? 13f : 0f) - countWidth - (countWidth > 0f ? 3f : 0f);
        var text = row.AddTextIntoHoriLayout(label.ColorString(selected ? Gold : Value), true, TextAnchor.MiddleLeft,
            new Vector2(labelWidth, 13f));
        text.UseFixedFontSize(selected ? 8 : 7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(text);
        if (countWidth > 0f)
            row.AddTextIntoHoriLayout(count.ColorString(selected ? Gold : Muted), true, TextAnchor.MiddleRight,
                new Vector2(countWidth, 13f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        UIHelper.MakeClickable(row.gameObject, action);
        return row;
    }

    // 左栏整宽按钮(单独一张卡)
    public static void AddRailButton(this AutoVertLayoutGroup rail, string id, string label, UnityAction action)
    {
        var card = rail.BeginRailCard();
        card.AddButtonIntoVertLayout(id, label, action, size: new Vector2(UIHelper.RailWidth - 8f, 14f), showTip: false);
    }

    #endregion

    #region 右栏(页面级)

    // 一排主指标格(等宽平分页宽)
    public static AutoHoriLayoutGroup AddKpiRow(this AutoVertLayoutGroup page, float width,
        params (string label, string value, string hex)[] tiles)
    {
        var row = page.BeginColumns(width);
        if (tiles.Length == 0) return row;
        float tile = (width - (tiles.Length - 1) * UIHelper.CardGap) / tiles.Length;
        foreach ((string label, string value, string hex) in tiles) UIHelper.AddKpiTile(row, tile, label, value, hex);
        return row;
    }

    // 卡片网格：每行 columns 张、等宽，逐个调用 drawCard(行, 卡宽, 数据)；卡片在回调里用 row.BeginColumnCard(卡宽) 开
    public static void AddCardGrid<TItem>(this AutoVertLayoutGroup page, float width, IList<TItem> items, int columns,
        Action<AutoHoriLayoutGroup, float, TItem> drawCard)
    {
        if (items == null || items.Count == 0 || drawCard == null) return;
        columns = Mathf.Max(1, columns);
        float cardWidth = (width - (columns - 1) * UIHelper.CardGap) / columns;
        for (int start = 0; start < items.Count; start += columns)
        {
            var row = page.BeginColumns(width);
            for (int i = start; i < Mathf.Min(items.Count, start + columns); i++) drawCard(row, cardWidth, items[i]);
        }
    }

    // 空状态：一张卡，标题 + 可选的说明
    public static AutoVertLayoutGroup AddEmptyCard(this AutoVertLayoutGroup page, float width, string title, string hint = null)
    {
        var card = page.BeginCard(width, title);
        if (!string.IsNullOrEmpty(hint))
            card.AddTextIntoVertLayout(hint.ColorString(Muted), true, TextAnchor.UpperLeft,
                    new Vector2(width - CardPadding, 30f), mode: HorizontalWrapMode.Wrap)
                .UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        return card;
    }

    #endregion

    #region 卡片内

    // 色条(国色、派系色)：卡片顶端一条定高色块
    public static void AddColorStrip(this AutoVertLayoutGroup card, float cardWidth, Color color, float height = 3f)
    {
        float width = cardWidth - CardPadding;
        var strip = new GameObject("ColorStrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var rect = strip.GetComponent<RectTransform>();
        rect.SetParent(card.transform, false);
        // NML 的布局组不接管子物体尺寸，现建的物体必须自己设 sizeDelta(默认 100×100 会撑满卡片)
        rect.sizeDelta = new Vector2(width, height);
        Image image = strip.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        LayoutElement layout = strip.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
    }

    // 人物行：头像 + 小灰称谓 / 金色姓名 / 一行说明(年号、在位、官职……)
    public static AutoHoriLayoutGroup AddPersonRow(this AutoVertLayoutGroup card, float cardWidth, Actor actor,
        string caption, string name, string detail = null)
    {
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 32f), TextAnchor.MiddleLeft, 4, new RectOffset(0, 0, 0, 0));
        row.AddActorViewIntoHoriLayout(actor);
        float textWidth = inner - 36f;
        var lines = row.BeginVertGroup(new Vector2(textWidth, 30f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        if (!string.IsNullOrEmpty(caption))
            lines.AddTextIntoVertLayout(caption.ColorString(Muted), true, TextAnchor.MiddleLeft, new Vector2(textWidth, 9f))
                .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        var nameText = lines.AddTextIntoVertLayout((name ?? "").ColorString(Gold), true, TextAnchor.MiddleLeft,
            new Vector2(textWidth, 12f));
        nameText.UseFixedFontSize(9, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(nameText);
        if (!string.IsNullOrEmpty(detail))
        {
            var detailText = lines.AddTextIntoVertLayout(detail.ColorString(Soft), true, TextAnchor.MiddleLeft,
                new Vector2(textWidth, 9f));
            detailText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(detailText);
        }
        return row;
    }

    // 指标行：深色内嵌底，等宽几格(上小灰标签、下亮色数值)
    public static AutoHoriLayoutGroup AddMetricRow(this AutoVertLayoutGroup card, float cardWidth,
        params (string label, string value, string hex)[] cells)
    {
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 22f), TextAnchor.MiddleLeft, 0, new RectOffset(0, 0, 0, 0));
        UIHelper.AddInsetBackground(row, new Vector2(inner, 22f));
        if (cells.Length == 0) return row;
        float cell = inner / cells.Length;
        foreach ((string label, string value, string hex) in cells)
            UIHelper.AddMetricCell(row, cell, label, value, string.IsNullOrEmpty(hex) ? Value : hex);
        return row;
    }

    // 按钮行：几个按钮等宽平分卡片内宽
    public static AutoHoriLayoutGroup AddButtonRow(this AutoVertLayoutGroup card, float cardWidth,
        params (string id, string label, UnityAction action)[] buttons)
    {
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 16f), TextAnchor.MiddleCenter, 3, new RectOffset(0, 0, 0, 0));
        if (buttons.Length == 0) return row;
        float width = (inner - 3f * (buttons.Length - 1)) / buttons.Length;
        foreach ((string id, string label, UnityAction action) in buttons)
            row.AddButtonIntoHoriLayout(id, label, action, size: new Vector2(width, 15f));
        return row;
    }

    // 列表行：隔行垫底，图标 + 标题(定宽) + 说明(其余宽度，过长悬停滚动)，可点
    public static AutoHoriLayoutGroup AddListRow(this AutoVertLayoutGroup card, float cardWidth, int index, Sprite icon,
        string title, string detail, UnityAction action = null, string titleHex = Soft, float titleWidth = 100f)
    {
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 15f), TextAnchor.MiddleLeft, 3, new RectOffset(3, 3, 0, 0));
        if (index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, 15f));
        UIHelper.AddLayoutIcon(row.transform, icon, 10f);
        float used = 6f + (icon != null ? 13f : 0f);
        var name = row.AddTextIntoHoriLayout((title ?? "").ColorString(titleHex), true, TextAnchor.MiddleLeft,
            new Vector2(titleWidth, 13f));
        name.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(name);
        if (!string.IsNullOrEmpty(detail))
        {
            var text = row.AddTextIntoHoriLayout(detail.ColorString(Muted), true, TextAnchor.MiddleLeft,
                new Vector2(Mathf.Max(10f, inner - used - titleWidth - 3f), 13f));
            text.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(text);
        }
        if (action != null) UIHelper.MakeClickable(row.gameObject, action);
        return row;
    }

    // 表格行：图标 + 名称(定宽) + 其余各列等分、右对齐；index 为 -1 是表头(灰色小字、不垫底)，否则隔行垫底
    public static AutoHoriLayoutGroup AddTableRow(this AutoVertLayoutGroup card, float cardWidth, int index, Sprite icon,
        string name, float nameWidth, params (string text, string hex)[] cells)
    {
        bool header = index < 0;
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, header ? 11f : 14f), TextAnchor.MiddleLeft, 2, new RectOffset(3, 3, 0, 0));
        if (!header && index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, 14f));
        float iconSpace = 0f;
        if (icon != null)
        {
            UIHelper.AddLayoutIcon(row.transform, icon, 9f);
            iconSpace = 11f;
        }
        var label = row.AddTextIntoHoriLayout((name ?? "").ColorString(header ? Muted : Soft), true, TextAnchor.MiddleLeft,
            new Vector2(Mathf.Max(10f, nameWidth - iconSpace), 12f));
        label.UseFixedFontSize(header ? 6 : 7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(label);
        if (cells.Length == 0) return row;
        float cell = Mathf.Max(10f, (inner - 6f - nameWidth - 2f * cells.Length) / cells.Length);
        foreach ((string text, string hex) in cells)
        {
            var value = row.AddTextIntoHoriLayout((text ?? "").ColorString(header ? Muted : string.IsNullOrEmpty(hex) ? Value : hex),
                true, TextAnchor.MiddleRight, new Vector2(cell, 12f));
            value.UseFixedFontSize(header ? 6 : 7, HorizontalWrapMode.Overflow);
        }
        return row;
    }

    #endregion
}
