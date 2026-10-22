using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using NeoModLoader.General.UI.Prefabs;
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
//   card.AddToggleRow(PageWidth, 0, "平滑国界", "国界逐像素判定……", on, "开启", () => {...}, "重启生效");   // 设置页开关
//   card.AddStepperRow(PageWidth, 1, "战争强制结束", "打满这么多年……", "10", "年", text => {...}, () => {...}, () => {...});
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

    // 开关行(设置页)：名称(可带橙色小标，如"重启生效") + 灰色说明 | 右侧开关块(色点 + 状态字)；整行可点
    public static AutoHoriLayoutGroup AddToggleRow(this AutoVertLayoutGroup card, float cardWidth, int index, string title,
        string detail, bool on, string state, UnityAction toggle, string badge = null)
    {
        var row = BeginSettingRow(card, cardWidth, index, title, detail, badge, out AutoHoriLayoutGroup control);
        const float width = 46f;
        var pill = control.BeginHoriGroup(new Vector2(width, 14f), TextAnchor.MiddleLeft, 3, new RectOffset(5, 3, 0, 0));
        UIHelper.AddInsetBackground(pill, new Vector2(width, 14f));
        AddDot(pill.transform, on ? Green : Muted, 5f);
        pill.AddTextIntoHoriLayout((state ?? "").ColorString(on ? Green : Muted), true, TextAnchor.MiddleLeft,
            new Vector2(width - 16f, 13f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        UIHelper.MakeClickable(row.gameObject, toggle);
        return row;
    }

    // 数值行(设置页)：名称 + 灰色说明 | 右侧 [-] 输入框 [+] 单位；输入框只收整数，回车或离开时交给 commit
    public static TextInput AddStepperRow(this AutoVertLayoutGroup card, float cardWidth, int index, string title,
        string detail, string value, string unit, UnityAction<string> commit, UnityAction minus, UnityAction plus,
        string badge = null)
    {
        BeginSettingRow(card, cardWidth, index, title, detail, badge, out AutoHoriLayoutGroup control);
        control.AddButtonIntoHoriLayout("wide_step_minus", "-", minus, size: new Vector2(13f, 14f));
        TextInput input = UnityEngine.Object.Instantiate(TextInput.Prefab, null);
        input.Setup(value ?? "", commit);
        const float inputWidth = 30f, inputHeight = 16f;
        input.SetSize(new Vector2(inputWidth, inputHeight));
        // 窄框里不要右侧的笔形图标，文字铺满居中
        input.icon.gameObject.SetActive(false);
        Text text = input.input.textComponent;
        if (text != null)
        {
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.sizeDelta = new Vector2(inputWidth - 4f, inputHeight - 2f);
            text.transform.localPosition = new Vector3(-inputWidth / 2f + 2f, 0f);
        }
        input.input.contentType = InputField.ContentType.IntegerNumber;
        input.input.characterLimit = 6;
        control.AddChild(input.gameObject);
        control.AddButtonIntoHoriLayout("wide_step_plus", "+", plus, size: new Vector2(13f, 14f));
        control.AddTextIntoHoriLayout((unit ?? "").ColorString(Muted), true, TextAnchor.MiddleLeft,
            new Vector2(SettingControlWidth - 13f * 2f - inputWidth - 2f * 3f, 13f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        return input;
    }

    private const float SettingControlWidth = 80f;

    // 设置行骨架：隔行垫底；左边名称与说明(说明按字数估行数定高，不会压到下一行)，右边留出控件区
    private static AutoHoriLayoutGroup BeginSettingRow(AutoVertLayoutGroup card, float cardWidth, int index, string title,
        string detail, string badge, out AutoHoriLayoutGroup control)
    {
        float inner = cardWidth - CardPadding;
        float textWidth = inner - 6f - 4f - SettingControlWidth;
        float detailHeight = string.IsNullOrEmpty(detail) ? 0f
            : Mathf.Ceil((UIHelper.EstimateTextWidth(detail, 6) * 1.1f + 6f) / textWidth) * 8f;
        float height = Mathf.Max(18f, 12f + detailHeight + 5f);
        var row = card.BeginHoriGroup(new Vector2(inner, height), TextAnchor.MiddleLeft, 4, new RectOffset(3, 3, 2, 3));
        if (index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, height));
        var lines = row.BeginVertGroup(new Vector2(textWidth, height - 5f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        string name = (title ?? "").ColorString(Value);
        if (!string.IsNullOrEmpty(badge)) name += "  " + badge.ColorString(Orange);
        var nameText = lines.AddTextIntoVertLayout(name, true, TextAnchor.MiddleLeft, new Vector2(textWidth, 12f));
        nameText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(nameText);
        if (detailHeight > 0f)
            lines.AddTextIntoVertLayout(detail.ColorString(Muted), true, TextAnchor.UpperLeft, new Vector2(textWidth, detailHeight))
                .UseFixedFontSize(6, HorizontalWrapMode.Wrap);
        control = row.BeginHoriGroup(new Vector2(SettingControlWidth, 16f), TextAnchor.MiddleRight, 2, new RectOffset(0, 0, 0, 0));
        return row;
    }

    // 族谱头像(在世、已故、虚拟族人都行)：外面包一层定宽竖排组，直接塞进横排会错位
    public static void AddIdentityAvatar(this AutoHoriLayoutGroup row, PersonalClanIdentity person, UnityAction click,
        float size = 30f)
    {
        var holder = row.BeginVertGroup(new Vector2(size, size), pSpacing: 0, pAlignment: TextAnchor.MiddleCenter,
            pPadding: new RectOffset(0, 0, 0, 0));
        if (person == null) return;
        SimpleButton frame = UIHelper.CreateAvatarView(person.actor_id, click, person.is_alive, person);
        holder.AddChild(frame.gameObject);
        if (size != 30f) frame.transform.localScale = Vector3.one * (size / 30f);
        holder.transform.localPosition = Vector3.zero;
    }

    // 族谱人物行(在世、已故、虚拟族人都行)：头像 + 小灰称谓 / 姓名(在世金、已故灰) / 一行说明
    public static AutoHoriLayoutGroup AddIdentityRow(this AutoVertLayoutGroup card, float cardWidth, PersonalClanIdentity person,
        string caption, string detail, UnityAction click = null)
    {
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 32f), TextAnchor.MiddleLeft, 4, new RectOffset(0, 0, 0, 0));
        row.AddIdentityAvatar(person, click);
        float textWidth = inner - 34f;
        var lines = row.BeginVertGroup(new Vector2(textWidth, 30f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        if (!string.IsNullOrEmpty(caption))
        {
            var captionText = lines.AddTextIntoVertLayout(caption.ColorString(Muted), true, TextAnchor.MiddleLeft,
                new Vector2(textWidth, 9f));
            captionText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            HoverMarqueeText.Attach(captionText);
        }
        var nameText = lines.AddTextIntoVertLayout((person?.name ?? "?").ColorString(person?.is_alive == true ? Gold : Soft), true,
            TextAnchor.MiddleLeft, new Vector2(textWidth, 12f));
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

    // 人物格(网格里一格)：像素框 + 头像 + 三行(称谓·姓名 / 生卒 / 官职)；highlighted 用金框标"当前这个人"
    public static AutoVertLayoutGroup AddPersonTile(this AutoHoriLayoutGroup row, float width, PersonalClanIdentity person,
        string tag, string tagHex, string life, string post, string postHex, bool highlighted, UnityAction click)
    {
        var tile = row.BeginVertGroup(pSpacing: 0, pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(4, 4, 4, 4));
        UIHelper.FixPanelWidth(tile.transform, width);
        UIHelper.AddCardFrame(tile.transform, highlighted ? "FactionFrame_dominate" : "FactionFrame");
        float inner = width - 8f;
        var line = tile.BeginHoriGroup(new Vector2(inner, 28f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        line.AddIdentityAvatar(person, click, 26f);
        float textWidth = inner - 29f;
        var lines = line.BeginVertGroup(new Vector2(textWidth, 28f), pSpacing: 0, pAlignment: TextAnchor.MiddleLeft,
            pPadding: new RectOffset(0, 0, 0, 0));
        string head = (string.IsNullOrEmpty(tag) ? "" : tag.ColorString(tagHex) + " ") +
                      (person?.name ?? "?").ColorString(person?.is_alive == true ? Value : Soft);
        var name = lines.AddTextIntoVertLayout(head, true, TextAnchor.MiddleLeft, new Vector2(textWidth, 10f));
        name.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(name);
        lines.AddTextIntoVertLayout((life ?? "").ColorString(Muted), true, TextAnchor.MiddleLeft, new Vector2(textWidth, 8f))
            .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        var postText = lines.AddTextIntoVertLayout((post ?? "").ColorString(postHex ?? Muted), true, TextAnchor.MiddleLeft,
            new Vector2(textWidth, 8f));
        postText.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(postText);
        UIHelper.MakeClickable(tile.gameObject, click);
        return tile;
    }

    // 一排小切换块(筛选、排序、本家/父系/母系)：选中的金字描边；返回这一排，调用方可在尾部再加东西
    public static AutoHoriLayoutGroup AddChipRow(this AutoVertLayoutGroup parent, float width,
        params (string label, bool on, UnityAction action)[] chips)
    {
        var row = parent.BeginHoriGroup(new Vector2(width, 15f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        foreach ((string label, bool on, UnityAction action) in chips) AddChip(row, label, on, action);
        return row;
    }

    public static void AddChip(this AutoHoriLayoutGroup row, string label, bool on, UnityAction action)
    {
        float width = Mathf.Max(26f, UIHelper.EstimateTextWidth(label, 7) + 12f);
        var chip = row.BeginHoriGroup(new Vector2(width, 14f), TextAnchor.MiddleCenter, 0, new RectOffset(0, 0, 0, 0));
        UIHelper.AddInsetBackground(chip, new Vector2(width, 14f));
        chip.AddTextIntoHoriLayout((label ?? "").ColorString(on ? Gold : Soft), true, TextAnchor.MiddleCenter,
            new Vector2(width, 13f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        UIHelper.MakeClickable(chip.gameObject, action);
    }

    // 翻页：‹ 第 2 / 5 页 ›，只有一页时不画
    public static void AddPager(this AutoVertLayoutGroup parent, float width, int page, int pages, string format,
        UnityAction<int> go)
    {
        if (pages <= 1) return;
        var row = parent.BeginHoriGroup(new Vector2(width, 16f), TextAnchor.MiddleCenter, 6, new RectOffset(0, 0, 0, 0));
        row.AddButtonIntoHoriLayout("wide_page_prev", "<", () => go(Mathf.Max(0, page - 1)), size: new Vector2(18f, 14f));
        row.AddTextIntoHoriLayout(string.Format(format, page + 1, pages).ColorString(Soft), true, TextAnchor.MiddleCenter,
            new Vector2(70f, 13f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        row.AddButtonIntoHoriLayout("wide_page_next", ">", () => go(Mathf.Min(pages - 1, page + 1)), size: new Vector2(18f, 14f));
    }

    // 成段文字(列传、说明)：按字数估行数定高，自动折行
    public static SimpleText AddParagraph(this AutoVertLayoutGroup card, float cardWidth, string text, int fontSize = 7,
        string hex = Soft)
    {
        float width = cardWidth - CardPadding;
        float lineHeight = fontSize + 2.5f;
        float height = Mathf.Ceil((UIHelper.EstimateTextWidth(text ?? "", fontSize) * 1.08f + fontSize) / width) * lineHeight + 2f;
        var label = card.AddTextIntoVertLayout((text ?? "").ColorString(hex), true, TextAnchor.UpperLeft,
            new Vector2(width, Mathf.Max(lineHeight, height)));
        label.UseFixedFontSize(fontSize, HorizontalWrapMode.Wrap);
        return label;
    }

    // 键值行：左灰标签、右彩色数值，隔行垫底，可点
    public static AutoHoriLayoutGroup AddValueRow(this AutoVertLayoutGroup card, float cardWidth, int index, string label,
        string value, string hex = Value, UnityAction action = null, float labelWidth = 0f)
    {
        float inner = cardWidth - CardPadding;
        var row = card.BeginHoriGroup(new Vector2(inner, 13f), TextAnchor.MiddleLeft, 2, new RectOffset(3, 3, 0, 0));
        if (index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, 13f));
        if (labelWidth <= 0f) labelWidth = (inner - 8f) * 0.42f;
        var left = row.AddTextIntoHoriLayout((label ?? "").ColorString(Muted), true, TextAnchor.MiddleLeft,
            new Vector2(labelWidth, 12f));
        left.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(left);
        var right = row.AddTextIntoHoriLayout((value ?? "").ColorString(hex), true, TextAnchor.MiddleRight,
            new Vector2(inner - 8f - labelWidth, 12f));
        right.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(right);
        if (action != null) UIHelper.MakeClickable(row.gameObject, action);
        return row;
    }

    // 柱状走势(门第、人口这类逐年的数)：深色底上一排细柱，柱高按 max 折算；guides 画几条淡横线(等第分界)
    public static void AddColumnChart(this AutoVertLayoutGroup card, float cardWidth, IList<float> values, float max,
        string hex, float height = 34f, IList<float> guides = null)
    {
        float inner = cardWidth - CardPadding;
        var box = new GameObject("ColumnChart", typeof(RectTransform), typeof(LayoutElement));
        var rect = box.GetComponent<RectTransform>();
        rect.SetParent(card.transform, false);
        rect.sizeDelta = new Vector2(inner, height);
        LayoutElement layout = box.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = inner;
        layout.minHeight = layout.preferredHeight = height;
        Plain(rect, Vector2.zero, new Vector2(inner, height), new Color(0.137f, 0.153f, 0.122f, 1f));
        if (guides != null && max > 0f)
            foreach (float guide in guides)
                Plain(rect, new Vector2(0f, Mathf.Clamp01(guide / max) * (height - 2f) + 1f), new Vector2(inner, 1f),
                    new Color(0.56f, 0.63f, 0.66f, 0.25f));
        if (values == null || values.Count == 0 || max <= 0f) return;
        ColorUtility.TryParseHtmlString(hex, out Color color);
        float step = inner / values.Count;
        float bar = Mathf.Max(1f, step - (step >= 3f ? 1f : 0f));
        for (int i = 0; i < values.Count; i++)
        {
            float h = Mathf.Max(1f, Mathf.Clamp01(values[i] / max) * (height - 2f));
            Plain(rect, new Vector2(i * step, 1f), new Vector2(bar, h), color);
        }
    }

    // 左下角定位的纯色块(图表用)
    private static void Plain(RectTransform parent, Vector2 position, Vector2 size, Color color)
    {
        var block = new GameObject("Block", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = block.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = block.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    // 编年一行：左边日期(青)，右边事件(按字数折行定高)，隔行垫底，可点
    // extra：行尾再放一个小图标按钮(比如"打开史书")，跟整行的 action 各管各的
    public static AutoHoriLayoutGroup AddTimelineRow(this AutoVertLayoutGroup card, float cardWidth, int index, string date,
        string text, string hex = Soft, UnityAction action = null, float dateWidth = 44f, UnityAction extra = null,
        Sprite extraIcon = null)
    {
        float inner = cardWidth - CardPadding;
        float textWidth = inner - 6f - 3f - dateWidth - (extra != null ? 15f : 0f);
        float height = Mathf.Max(12f,
            Mathf.Ceil((UIHelper.EstimateTextWidth(text ?? "", 7) * 1.08f + 7f) / textWidth) * 9.5f + 2f);
        var row = card.BeginHoriGroup(new Vector2(inner, height), TextAnchor.UpperLeft, 3, new RectOffset(3, 3, 1, 1));
        if (index % 2 == 0) UIHelper.AddInsetBackground(row, new Vector2(inner, height));
        row.AddTextIntoHoriLayout((date ?? "").ColorString(Cyan), true, TextAnchor.UpperLeft, new Vector2(dateWidth, 10f))
            .UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        row.AddTextIntoHoriLayout((text ?? "").ColorString(hex), true, TextAnchor.UpperLeft, new Vector2(textWidth, height - 2f))
            .UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        if (extra != null)
            row.AddButtonIntoHoriLayout("wide_timeline_extra", "", extra, extraIcon, size: new Vector2(12f, 12f), showTip: false);
        if (action != null) UIHelper.MakeClickable(row.gameObject, action);
        return row;
    }

    // 页面里的搜索框：整宽，回车或离开时交给 commit
    public static TextInput AddSearchInput(this AutoVertLayoutGroup parent, float width, string value, UnityAction<string> commit)
    {
        TextInput input = UnityEngine.Object.Instantiate(TextInput.Prefab, null);
        input.Setup(value ?? "", commit);
        input.SetSize(new Vector2(width, 16f));
        Text text = input.input.textComponent;
        if (text != null)
        {
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
        }
        input.input.characterLimit = 24;
        parent.AddChild(input.gameObject);
        return input;
    }

    // 小色点(开关状态灯)
    private static void AddDot(Transform parent, string hex, float size)
    {
        var dot = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        var rect = dot.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(size, size);
        Image image = dot.GetComponent<Image>();
        image.color = ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
        image.raycastTarget = false;
        LayoutElement layout = dot.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = size;
        layout.minHeight = layout.preferredHeight = size;
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
