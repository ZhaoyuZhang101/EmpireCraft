using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

// 厂区小窗口(用户 2026-10-17 "如果打开工厂区, 我能看点里面有什么厂(图标) 而且可以给他们升级降级添加并取消还能看到他们的产出")：
// 拉近后点厂区打开(见 MapLabelOverlay.HandleClick)。宽窗口 480×200、两张半宽卡，同村镇小窗口——
//   左「概况」：名字(可改)、大小(小 → 中 → 大厂区，扩建缩建)、厂几座、合计岗位与年产值、官营民营(可改)、属哪座城哪国，换个样子、拆除；
//   右「工厂」：每座厂一行(门类图标、名字、等级、岗位、年产值，升级、降级、拆掉)，没满时末尾一排已解锁门类的图标，点哪个添哪个。
public class FactoryWindow : AbstractWideWindow<FactoryWindow>
{
    private const float CardWidth = (UIHelper.WidePanelWidth - UIHelper.CardGap) / 2f;
    private const float RowWidth = CardWidth - 12f;
    private static int _selected = -1;
    private AutoVertLayoutGroup _root;

    public static void Open(FactorySite site)
    {
        if (site == null) return;
        _selected = site.id;
        ScrollWindow.showWindow(nameof(FactoryWindow));
    }

    public static Sprite IconOf(string type) => SpriteTextureLoader.getSprite("ui/icons/factories/" + type);

    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        Draw();
    }

    private static SimpleText Line(AutoVertLayoutGroup card, string text, int size = 7, float height = 11f)
    {
        SimpleText label = card.AddTextIntoVertLayout(text, true, TextAnchor.MiddleLeft, new Vector2(RowWidth, height));
        label.UseFixedFontSize(size, HorizontalWrapMode.Overflow);
        return label;
    }

    private static void Tip(string text)
    {
        if (!string.IsNullOrEmpty(text)) WorldTip.showNow(text, false, "top", 3f);
    }

    private void Draw()
    {
        if (_root == null) return;
        _root.transform.ClearChildren();
        FactorySite site = FactorySystem.ById(_selected);
        if (site == null)
        {
            Line(_root.BeginCard(UIHelper.WidePanelWidth), LM.Get("factory_none").ColorString("#8FA0A8"), 8, 14f);
            return;
        }
        City city = FactorySystem.CityOf(site);
        var columns = _root.BeginColumns(UIHelper.WidePanelWidth);

        // 概况
        var info = columns.BeginColumnCard(CardWidth, "settlement_window_overview", icons: "ui/icons/godpowers/factory_medium");
        Line(info, LM.Get("settlement_window_name").ColorString("#8FA0A8"), 6, 9f);
        TextInput nameInput = Instantiate(TextInput.Prefab, null);
        nameInput.Setup(FactorySystem.Label(site), text =>
        {
            FactorySystem.Rename(FactorySystem.ById(_selected), text);
            Draw();
        });
        nameInput.SetSize(new Vector2(RowWidth, 18f));
        Text inputText = nameInput.input.textComponent;
        if (inputText != null)
        {
            inputText.resizeTextMinSize = 6;
            inputText.resizeTextMaxSize = 10;
        }
        nameInput.input.characterLimit = 8;
        info.AddChild(nameInput.gameObject);
        var sizeRow = info.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        sizeRow.AddTextIntoHoriLayout(string.Format(LM.Get("factory_window_size"), LM.Get("factory_size_" + site.size),
                site.units.Count, FactorySystem.Capacity(site.size)).ColorString("#F2EEE2"), true, TextAnchor.MiddleLeft,
            new Vector2(RowWidth - 80f, 14f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        if (site.size < FactorySystem.MaxSize)
            sizeRow.AddButtonIntoHoriLayout("factory_expand", LM.Get("factory_expand"), () =>
            {
                FactorySite now = FactorySystem.ById(_selected);
                if (now != null) Tip(FactorySystem.Resize(now, now.size + 1));
                Draw();
            }, size: new Vector2(36f, 14f));
        if (site.size > 1)
            sizeRow.AddButtonIntoHoriLayout("factory_shrink", LM.Get("factory_shrink"), () =>
            {
                FactorySite now = FactorySystem.ById(_selected);
                if (now != null) Tip(FactorySystem.Resize(now, now.size - 1));
                Draw();
            }, size: new Vector2(36f, 14f));
        int jobs = site.units.Sum(FactorySystem.JobsOf);
        float value = site.units.Sum(FactorySystem.ValueOf);
        Line(info, string.Format(LM.Get("factory_window_total"), jobs, Mathf.RoundToInt(value)).ColorString("#65D66E"));
        // 官营民营(见 EnterpriseSystem：官营的利润上缴国库)
        var ownRow = info.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        ownRow.AddTextIntoHoriLayout(string.Format(LM.Get("enterprise_row"), LM.Get(site.state ? "enterprise_state" : "enterprise_private"))
            .ColorString("#D9B36A"), true, TextAnchor.MiddleLeft, new Vector2(RowWidth - 46f, 14f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        ownRow.AddButtonIntoHoriLayout("factory_state", LM.Get(site.state ? "enterprise_to_private" : "enterprise_to_state"), () =>
        {
            FactorySite now = FactorySystem.ById(_selected);
            if (now != null) now.state = !now.state;
            Draw();
        }, size: new Vector2(42f, 14f));
        Line(info, (city == null
            ? LM.Get("settlement_unowned")
            : string.Format(LM.Get("settlement_owner"), city.GetCityName(), city.kingdom?.GetKingdomName() ?? "")).ColorString("#7FD8EA"));
        var buttons = info.BeginHoriGroup(new Vector2(RowWidth, 18f), TextAnchor.MiddleLeft, 3, new RectOffset(0, 0, 0, 0));
        buttons.AddButtonIntoHoriLayout("factory_reroll", LM.Get("settlement_reroll"), () =>
        {
            FactorySystem.Reroll(FactorySystem.ById(_selected));
            Draw();
        }, size: new Vector2(64f, 16f));
        buttons.AddButtonIntoHoriLayout("factory_remove", LM.Get("factory_remove"), () =>
        {
            Tip(FactorySystem.Remove(FactorySystem.ById(_selected)));
            Draw();
        }, size: new Vector2(64f, 16f));

        // 工厂
        var list = columns.BeginColumnCard(CardWidth, "factory_window_units", icons: "ui/icons/godpowers/factory_small");
        for (int i = 0; i < site.units.Count; i++)
        {
            int index = i;
            FactoryUnit unit = site.units[i];
            var row = list.BeginHoriGroup(new Vector2(RowWidth, 14f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            UIHelper.AddLayoutIcon(row.transform, IconOf(unit.type), 12f);
            row.AddTextIntoHoriLayout(string.Format(LM.Get("factory_window_unit"), FactorySystem.TypeName(unit.type), unit.level,
                    FactorySystem.JobsOf(unit), Mathf.RoundToInt(FactorySystem.ValueOf(unit))).ColorString("#F2EEE2"), true,
                TextAnchor.MiddleLeft, new Vector2(RowWidth - 62f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            if (unit.level < FactorySystem.MaxLevel)
                row.AddButtonIntoHoriLayout($"factory_up_{i}", LM.Get("factory_level_up"), () => Level(index, +1), size: new Vector2(14f, 12f));
            if (unit.level > 1)
                row.AddButtonIntoHoriLayout($"factory_down_{i}", LM.Get("factory_level_down"), () => Level(index, -1), size: new Vector2(14f, 12f));
            row.AddButtonIntoHoriLayout($"factory_del_{i}", LM.Get("factory_unit_remove"), () =>
            {
                FactorySystem.RemoveUnit(FactorySystem.ById(_selected), index);
                Draw();
            }, size: new Vector2(14f, 12f));
        }
        if (site.units.Count == 0) Line(list, LM.Get("factory_window_empty").ColorString("#8FA0A8"));
        if (site.units.Count < FactorySystem.Capacity(site.size))
        {
            List<string> unlocked = FactorySystem.Unlocked(city);
            Line(list, LM.Get(unlocked.Count > 0 ? "factory_window_add" : "factory_window_no_tech").ColorString("#8FA0A8"), 6, 10f);
            var add = list.BeginHoriGroup(new Vector2(RowWidth, 16f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
            foreach (string type in unlocked)
            {
                string chosen = type;
                add.AddButtonIntoHoriLayout("factory_type_" + type, "", () =>
                {
                    Tip(FactorySystem.AddUnit(FactorySystem.ById(_selected), chosen));
                    Draw();
                }, IconOf(type), size: new Vector2(16f, 16f), showTip: true);
            }
        }
    }

    private void Level(int index, int delta)
    {
        FactorySite site = FactorySystem.ById(_selected);
        if (site == null || index < 0 || index >= site.units.Count) return;
        FactorySystem.SetLevel(site, index, site.units[index].level + delta);
        Draw();
    }
}
