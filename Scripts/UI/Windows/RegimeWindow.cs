using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
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

namespace EmpireCraft.Scripts.UI.Windows;

public class RegimeWindow : AutoLayoutWindow<RegimeWindow>
{
    private TextInput _regimeInput;
    private TextInput _customCountryNameInput;
    private TextInput _customCountrySuffixInput;
    private bool _refreshingNameInputs;
    private Kingdom _kingdom;
    private Regime _regime => _kingdom.GetRegime();
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
        InitialCustomNaming();
        UIHelper.InitialFactionSpace(this.BeginHoriGroup(), _kingdom, _groups);
        InitialSetting();
    }

    private void InitialCustomNaming()
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(_kingdom)) return;
        var namingSpace = this.BeginVertGroup(pSize: new Vector2(200, 63), pSpacing: 2,
            pAlignment: TextAnchor.MiddleCenter);
        namingSpace.AddTextIntoVertLayout(LM.Get("kingdom_custom_naming"), true,
            TextAnchor.MiddleCenter, new Vector2(190, 15));

        _refreshingNameInputs = true;
        try
        {
            AddNamingInputRow(namingSpace, LM.Get("kingdom_custom_name"),
                _kingdom.GetCustomCountryName(), ChangeCustomCountryName, out _customCountryNameInput);
            AddNamingInputRow(namingSpace, LM.Get("kingdom_custom_suffix"),
                _kingdom.GetCustomCountrySuffix(), ChangeCustomCountrySuffix, out _customCountrySuffixInput);
        }
        finally
        {
            _refreshingNameInputs = false;
        }

        namingSpace.transform.AddStretchBackground("regimeFrame", size: new Vector2(200, 63));
        _groups.Add(namingSpace.gameObject);
    }

    private static void AddNamingInputRow(AutoVertLayoutGroup parent, string label, string value,
        UnityAction<string> action, out TextInput input)
    {
        var row = parent.BeginHoriGroup(pSize: new Vector2(194, 19), pSpacing: 2,
            pAlignment: TextAnchor.MiddleCenter);
        var labelText = row.AddTextIntoHoriLayout(label, true, TextAnchor.MiddleRight,
            new Vector2(64, 15));
        labelText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        input = UnityEngine.Object.Instantiate(TextInput.Prefab, null);
        input.Setup(value ?? "", action);
        input.SetSize(new Vector2(124, 18));
        row.AddChild(input.gameObject);
    }

    private void InitialSetting()
    {
        var settingSpace = this.BeginVertGroup();
        
        foreach (var option in _kingdom.GetRegime().options)
        {
            if (option.Key.Contains("toggle_"))
            {
                var button = settingSpace.transform.AddNormalOptionIntoHori(this.BeginHoriGroup(), option.Key, ()=>Toggle(option.Key), Convert.ToBoolean(option.Value[0]), hasIcon:false);
                _toggleButtons[option.Key]  = button;
            } else if (option.Key.Contains("option_"))
            {
                var optionButton = settingSpace.transform.AddMultipleOption(this.BeginHoriGroup(), option.Key, Option, option.Value[0], option.Value[1], hasIcon:false);
                _optionButtons[option.Key] = optionButton;
            }
        }
        settingSpace.transform.AddStretchBackground("regimeFrame", size:new Vector2(200, 137));
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
        RefreshKingdomStatus();
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
        RefreshKingdomStatus();
    }

    private void RefreshKingdomStatus()
    {
        if (_kingdom == null || _kingdom.isRekt()) return;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(_kingdom);
        RefreshNameInputs();
    }

    private void RefreshNameInputs()
    {
        if (_kingdom?.data == null) return;
        _refreshingNameInputs = true;
        try
        {
            if (_regimeInput?.input != null) _regimeInput.input.text = _kingdom.GetKingdomFullName();
            if (_customCountryNameInput?.input != null)
                _customCountryNameInput.input.text = _kingdom.GetCustomCountryName();
            if (_customCountrySuffixInput?.input != null)
                _customCountrySuffixInput.input.text = _kingdom.GetCustomCountrySuffix();
        }
        finally
        {
            _refreshingNameInputs = false;
        }
    }

    private void RefreshHeaderName()
    {
        if (_kingdom?.data == null || _regimeInput?.input == null) return;
        _refreshingNameInputs = true;
        try
        {
            _regimeInput.input.text = _kingdom.GetKingdomFullName();
        }
        finally
        {
            _refreshingNameInputs = false;
        }
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
        _customCountryNameInput = null;
        _customCountrySuffixInput = null;
    }
    [Hotfixable]
    private void InitialRegimeSelection()
    {
        var regimeSpace = this.BeginVertGroup();
        regimeSpace.AddTextIntoVertLayout(LM.Get("regime_title"), true, TextAnchor.MiddleCenter, new Vector2(25, 15));
        var regimeIconPart = this.BeginHoriGroup();
        LoadRegimeButton(regimeIconPart.transform, RegimeType.LvLing);
        LoadRegimeButton(regimeIconPart.transform, RegimeType.ZhouFeudalism);
        LoadRegimeButton(regimeIconPart.transform, RegimeType.Feudalism);
        LoadRegimeButton(regimeIconPart.transform, RegimeType.Modern);
        
        regimeSpace.AddChild(regimeIconPart.gameObject);
        regimeSpace.transform.AddStretchBackground("regimeFrame");
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
        RefreshKingdomStatus();
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
        InitialContent();
    }

    public void InitialTextInput()
    {
        _kingdom = SelectedMetas.selected_kingdom;
        RefreshKingdomStatus();
        _refreshingNameInputs = true;
        try
        {
            UIHelper.GenerateTextInput(this.transform.parent.transform.parent, offset: new Vector2(0, 152),
                default_text: _kingdom.GetKingdomFullName(), input: _regimeInput);
        }
        finally
        {
            _refreshingNameInputs = false;
        }
    }

    public void ChangeKingdomName(string text)
    {
        if (_refreshingNameInputs || _kingdom?.data == null) return;
        _kingdom.SetCustomCountryName(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            RefreshNameInputs();
            return;
        }
        if (_customCountryNameInput?.input == null) return;
        _refreshingNameInputs = true;
        try
        {
            _customCountryNameInput.input.text = text ?? "";
        }
        finally
        {
            _refreshingNameInputs = false;
        }
    }

    private void ChangeCustomCountryName(string text)
    {
        if (_refreshingNameInputs || _kingdom?.data == null) return;
        _kingdom.SetCustomCountryName(text);
        RefreshHeaderName();
    }

    private void ChangeCustomCountrySuffix(string text)
    {
        if (_refreshingNameInputs || _kingdom?.data == null) return;
        _kingdom.SetCustomCountrySuffix(text);
        RefreshHeaderName();
    }
}
