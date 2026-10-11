using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Serialization;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.UI.Windows;

// 宽窗口(480×180)：两张半宽卡——左「纪年」右「外观」，与帝国窗口同一套卡片设计语言
public class EmpireSettingWindow : AbstractWideWindow<EmpireSettingWindow>
{
    private Empire _empire;
    private AutoVertLayoutGroup _root;
    private SimpleText _calendarLabel;
    [FormerlySerializedAs("year_name_button")] public SimpleButton yearNameButton;
    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
        var columns = _root.BeginColumns();

        // 纪年卡：君主制切年号，共和国切纪年方式
        var calendarCard = columns.BeginColumnCard("empire_setting_calendar", icons: "ui/icons/iconClock");
        var toggleRow = calendarCard.BeginHoriGroup(new Vector2(UIHelper.CardColumnWidth - 8f, 18f), TextAnchor.MiddleLeft, 4);
        _calendarLabel = Instantiate(SimpleText.Prefab, null);
        _calendarLabel.Setup(LM.Get("open_year_name"), TextAnchor.MiddleLeft, new Vector2(UIHelper.CardColumnWidth - 40f, 15f));
        _calendarLabel.background.enabled = false;
        _calendarLabel.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        yearNameButton = Instantiate(SimpleButton.Prefab, null);
        yearNameButton.Setup(ToggleYearName, SpriteTextureLoader.getSprite("ui/icons/iconArrowUP"));
        yearNameButton.Background.enabled = false;
        yearNameButton.SetSize(new Vector2(14, 14));
        toggleRow.AddChild(_calendarLabel.gameObject);
        toggleRow.AddChild(yearNameButton.gameObject);

        // 外观卡：颜色与旗帜借原版"自定义王国"窗口编辑核心王国，见 EmpireColorPatch
        var lookCard = columns.BeginColumnCard("empire_setting_look", icons: "ui/icons/iconColor");
        SimpleButton colorButton = Instantiate(SimpleButton.Prefab, null);
        colorButton.Setup(() => EmpireColorPatch.Open(EmpireCraftMetaTypeLibrary.selected_empire),
            SpriteTextureLoader.getSprite("TabColor"), LM.Get("empire_change_color"), new Vector2(UIHelper.CardColumnWidth - 12f, 18f));
        lookCard.AddChild(colorButton.gameObject);
    }

    private void ToggleYearName()
    {
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        if (_empire == null) return;
        if (RepublicSystem.IsRepublic(_empire))
        {
            int mode = RepublicSystem.GetCalendarMode(_empire) == RepublicSystem.CommonEraCalendar
                ? RepublicSystem.RepublicEraCalendar : RepublicSystem.CommonEraCalendar;
            RepublicSystem.SetCalendarMode(_empire, mode);
            RefreshToggle();
            return;
        }
        _empire.data.has_year_name = !_empire.data.has_year_name;
        RefreshToggle();
    }

    public override void OnNormalEnable()
    {
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        RefreshToggle();
    }

    private void RefreshToggle()
    {
        if (_empire == null) return;
        bool republic = RepublicSystem.IsRepublic(_empire);
        string label = republic
            ? LM.Get(RepublicSystem.GetCalendarMode(_empire) == RepublicSystem.CommonEraCalendar
                ? "republic_common_era_label" : "republic_era_label")
            : LM.Get("open_year_name");
        _calendarLabel.Setup(label, TextAnchor.MiddleLeft, new Vector2(UIHelper.CardColumnWidth - 40f, 15f));
        _calendarLabel.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        SetToggle(republic
            ? RepublicSystem.GetCalendarMode(_empire) == RepublicSystem.CommonEraCalendar
            : _empire.data.has_year_name);
    }

    public void SetToggle (bool toggle)
    {
        yearNameButton.Icon.sprite = SpriteTextureLoader.getSprite(toggle ? "ui/toggle_open" : "ui/toggle_close");
    }
}
