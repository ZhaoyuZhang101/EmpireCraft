using EmpireCraft.Scripts.Data;
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
    [FormerlySerializedAs("year_name_button")] public SimpleButton yearNameButton;
    protected override void Init()
    {
        //年号按钮
        AutoVertLayoutGroup vertLayout = this.BeginVertGroup(new Vector2(75, 30), pSpacing: 3);
        SimpleText ToggleText = Instantiate(SimpleText.Prefab, null);
        string open_year_name = LM.Get("open_year_name");
        ToggleText.Setup($"{open_year_name}: ", TextAnchor.MiddleCenter, new Vector2(30, 15));
        ToggleText.background.enabled = false;
        yearNameButton = Instantiate(SimpleButton.Prefab, null);
        yearNameButton.Setup(ToggleYearName, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"));
        yearNameButton.Background.enabled = false;
        yearNameButton.SetSize(new Vector2(10, 10));
        vertLayout.AddChild(ToggleText.gameObject);
        vertLayout.AddChild(yearNameButton.gameObject);

        AddChild(vertLayout.gameObject);

    }
    
    private void ToggleYearName()
    {
        _empire = ConfigData.CURRENT_SELECTED_EMPIRE;
        _empire.data.has_year_name = !_empire.data.has_year_name;
        SetToggle(_empire.data.has_year_name);
    }

    public override void OnNormalEnable()
    {
        _empire = ConfigData.CURRENT_SELECTED_EMPIRE;
        base.OnNormalEnable();
        SetToggle(_empire.data.has_year_name);
    }

    public void SetToggle (bool toggle)
    {
        yearNameButton.Icon.sprite = SpriteTextureLoader.getSprite(toggle ? "ui/toggle_open" : "ui/toggle_close");
    }
}