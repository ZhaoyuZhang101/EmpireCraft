using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using NotImplementedException = System.NotImplementedException;

namespace EmpireCraft.Scripts.UI.Windows;

public class RegimeWindow : AutoLayoutWindow<RegimeWindow>
{
    private TextInput _regimeInput;
    private Kingdom _kingdom;
    private Dictionary<string, AdvancedButton> _toggleButtons = new Dictionary<string, AdvancedButton>();
    private Dictionary<string, List<AdvancedButton>> _optionButtons = new Dictionary<string, List<AdvancedButton>>();
    private List<GameObject> _groups = new List<GameObject>();
    private Dictionary<RegimeType, AdvancedButton> _regimeButtons = new Dictionary<RegimeType, AdvancedButton>();
    protected override void Init()
    {
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 3, 3);
        _regimeInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
        _regimeInput.Setup("", ChangeKingdomName);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        InitialTextInput();
        InitialContent();
    }

    private void InitialContent()
    {
        Clear();
        InitialRegimeSelection();
        InitialActorSpace();
        InitialSetting();
    }

    private void InitialActorSpace()
    {
        var kingSpace = this.BeginHoriGroup();

        var leftPart = kingSpace.BeginVertGroup();
        leftPart.AddTextIntoVertLayout(_kingdom?.king?.name??"", hideBackground:true, TextAnchor.MiddleCenter);
        leftPart.AddActorViewIntoVertLayout(_kingdom?.king);
        
        var rightPart = kingSpace.BeginVertGroup();
        rightPart.AddTextIntoVertLayout("-");
        rightPart.AddTextIntoVertLayout("-");
        rightPart.AddTextIntoVertLayout("-");
        kingSpace.transform.AddStretchBackground(SpriteTextureLoader.getSprite("ui/regimeFrame"), size:new Vector2(180, 55));
        _groups.Add(kingSpace.gameObject);
    }

    private void InitialSetting()
    {
        var settingSpace = this.BeginVertGroup();
        
        foreach (var option in _kingdom.GetRegime().options)
        {
            if (option.Key.Contains("toggle_"))
            {
                var button = settingSpace.transform.AddNormalOption(this.BeginHoriGroup(), option.Key, ()=>Toggle(option.Key), Convert.ToBoolean(option.Value[0]), hasIcon:false);
                _toggleButtons[option.Key]  = button;
            } else if (option.Key.Contains("option_"))
            {
                var optionButton = settingSpace.transform.AddMultipleOption(this.BeginHoriGroup(), option.Key, Option, option.Value[0], option.Value[1], hasIcon:false);
                _optionButtons[option.Key] = optionButton;
            }
        }
        settingSpace.transform.AddStretchBackground(SpriteTextureLoader.getSprite("ui/regimeFrame"), size:new Vector2(200, 137));
        _groups.Add(settingSpace.gameObject);
    }

    private void Option(string title, int option)
    {
        _kingdom.GetRegime().options[title][0] = option;
        var index = 0;
        foreach (var optionButton in _optionButtons[title])
        {
            optionButton.SetStatus(option==index);
            index++;
        }
        LogService.LogInfo(title+option);
    }


    private void Toggle(string option)
    {

        if (_kingdom.GetRegime().options[option][0] == 0)
        {
            _kingdom.GetRegime().options[option][0] = 1;
        }
        else
        {
            _kingdom.GetRegime().options[option][0] = 0;
        }
        _toggleButtons[option].SetStatus(Convert.ToBoolean(_kingdom.GetRegime().options[option][0]));
    }

    private void Clear()
    {
        foreach (var regimeOption in _regimeButtons)
        {
            Destroy(regimeOption.Value.gameObject);
        }
        _regimeButtons.Clear();
        foreach (var group in _groups)
        {
            Destroy(group.gameObject);
        }
        foreach (var button in _toggleButtons)
        {
            Destroy(button.Value.gameObject);
        }
        _toggleButtons.Clear();
        _groups.Clear();
    }
    [Hotfixable]
    private void InitialRegimeSelection()
    {
        var regimeSpace = this.BeginVertGroup();
        regimeSpace.AddTextIntoVertLayout("政体", true, TextAnchor.MiddleCenter, new Vector2(25, 15));
        var regimeIconPart = this.BeginHoriGroup();
        LoadRegimeButton(regimeIconPart.transform, RegimeType.LvLing);
        LoadRegimeButton(regimeIconPart.transform, RegimeType.ZhouFeudalism);
        LoadRegimeButton(regimeIconPart.transform, RegimeType.Feudalism);
        LoadRegimeButton(regimeIconPart.transform, RegimeType.Republic);
        
        regimeSpace.AddChild(regimeIconPart.gameObject);
        regimeSpace.transform.AddStretchBackground(SpriteTextureLoader.getSprite("ui/regimeFrame"));
        _groups.Add(regimeSpace.gameObject);
    }
    [Hotfixable]
    public void LoadRegimeButton(Transform parent, RegimeType pType)
    {
        var toggle = GameObject.Instantiate(AdvancedButton.Prefab, parent);
        toggle.Setup(pType.ToString(), ()=>ChangeRegime(pType),pSize:new Vector2(25, 25), isToggle:true, showTip:true, customIcon:true);
        toggle.Background.enabled = false;
        _regimeButtons[pType]  = toggle;
        toggle.SetStatus(_kingdom.GetRegime().type == pType);
    }

    private void ChangeRegime(RegimeType pType)
    {
        foreach (var regimeOption in _regimeButtons)
        {
            regimeOption.Value.SetStatus(regimeOption.Key == pType);
        }
        _kingdom.SetRegimeType(pType);
        _kingdom.LoadRegime();
        foreach (var option in _kingdom.GetRegime().options)
        {
            if (option.Key.Contains("toggle_"))
            {
                _toggleButtons[option.Key].SetStatus(Convert.ToBoolean(option.Value[0]));
            } 
            else if (option.Key.Contains("option_"))
            {
                var index = 0;
                foreach (var optionButton in _optionButtons[option.Key])
                {
                    optionButton.SetStatus(index==option.Value[0]);
                    index++;
                }
            }
        }
    }

    public void InitialTextInput()
    {
        _kingdom = SelectedMetas.selected_kingdom;
        var text = _kingdom.name;
        UIHelper.GenerateTextInput(this.transform.parent.transform.parent, offset:new Vector2(0, 152), default_text:text, input:_regimeInput);
    }

    public void ChangeKingdomName(string text)
    {
        var namePart = text.Split('\u200A');
        _regimeInput.input.text = namePart[0] + "\u200A" + LM.Get(EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(_kingdom).ToString());
        LogService.LogInfo("changing clan name");
    }
}