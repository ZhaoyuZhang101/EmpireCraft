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

namespace EmpireCraft.Scripts.UI.Windows;

public class EmpireSettingWindow : AutoLayoutWindow<EmpireSettingWindow>
{
    private Empire _empire;
    private SimpleText _calendarLabel;
    [FormerlySerializedAs("year_name_button")] public SimpleButton yearNameButton;
    protected override void Init()
    {
        // Monarchy: era-name toggle. Republic: calendar choice.
        AutoVertLayoutGroup vertLayout = this.BeginVertGroup(new Vector2(90, 30), pSpacing: 3);
        SimpleText ToggleText = Instantiate(SimpleText.Prefab, null);
        _calendarLabel = ToggleText;
        string open_year_name = LM.Get("open_year_name");
        ToggleText.Setup($"{open_year_name}: ", TextAnchor.MiddleCenter, new Vector2(30, 15));
        ToggleText.background.enabled = false;
        yearNameButton = Instantiate(SimpleButton.Prefab, null);
        yearNameButton.Setup(ToggleYearName, SpriteTextureLoader.getSprite("ui/icons/iconArrowUP"));
        yearNameButton.Background.enabled = false;
        yearNameButton.SetSize(new Vector2(10, 10));
        vertLayout.AddChild(ToggleText.gameObject);
        vertLayout.AddChild(yearNameButton.gameObject);

        AddChild(vertLayout.gameObject);

        // 颜色与旗帜：借原版"自定义王国"窗口编辑核心王国，见 EmpireColorPatch
        SimpleButton colorButton = Instantiate(SimpleButton.Prefab, null);
        colorButton.Setup(() => EmpireColorPatch.Open(EmpireCraftMetaTypeLibrary.selected_empire),
            SpriteTextureLoader.getSprite("TabColor"), LM.Get("empire_change_color"), new Vector2(80, 18));
        AddChild(colorButton.gameObject);
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
        _calendarLabel.Setup(label, TextAnchor.MiddleCenter, new Vector2(85, 15));
        SetToggle(republic
            ? RepublicSystem.GetCalendarMode(_empire) == RepublicSystem.CommonEraCalendar
            : _empire.data.has_year_name);
    }

    public void SetToggle (bool toggle)
    {
        yearNameButton.Icon.sprite = SpriteTextureLoader.getSprite(toggle ? "ui/toggle_open" : "ui/toggle_close");
    }
}
