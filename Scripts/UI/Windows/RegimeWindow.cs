using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
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
        // 这个窗口跟 KingdomTitleWindow 不一样：它没有一个统一的 _content 包一层再把
        // 各个板块（军政选择/自定义国名/阵营位/杂项设置）都塞进去居中对齐，而是直接把
        // InitialRegimeSelection/InitialCustomNaming/InitialSetting 各自建的组当成
        // layout 自己的直接子节点。这些子组各自的宽度（比如自定义国名卡是 200）通常都
        // 小于窗口本身的可用宽度，而 VerticalLayoutGroup 的 childAlignment 缺省是
        // UpperLeft，于是每个板块都贴着窗口左边，看起来整体偏左、不居中。这里显式把
        // layout 自己的 childAlignment 设成 UpperCenter（参考 UIHelper.cs 里同样的
        // 用法），让这些顶层子组统一在窗口宽度内居中，不用逐个板块再单独改。
        layout.childAlignment = TextAnchor.UpperCenter;
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

    // ── 政体选项：每行 = 游戏图标 + 选项名 + 带文字的档位按钮（开关类为 开/关）──
    private const float SettingWidth = 196f;
    private static readonly Color ChoiceOnColor = new Color(0.95f, 0.76f, 0.29f);
    private static readonly Color ChoiceOffColor = new Color(0.72f, 0.72f, 0.72f);
    private static readonly Color ChoiceLockedColor = new Color(0.4f, 0.4f, 0.4f);

    // 每个选项行用的图标：依次尝试，取第一个能加载到的游戏自带图标
    private static readonly Dictionary<string, string[]> OptionIcons = new()
    {
        ["toggle_allow_diplomacy"] = new[] { "ui/icons/iconAlliance", "ui/icons/iconDiplomacy" },
        ["toggle_allow_army"] = new[] { "ui/icons/iconWar", "ui/icons/iconArmy" },
        ["toggle_support_army_to_center"] = new[] { "ui/icons/iconKingdom", "ui/icons/iconWar" },
        ["option_tax_level"] = new[] { "ui/icons/iconMoney", "ui/icons/iconGold", "ui/icons/iconKingdom" },
        ["option_leader_select_method"] = new[] { "ui/icons/iconKing", "ui/icons/actor_traits/iconJingshi" },
        ["option_religion_type"] = new[] { "ui/icons/iconReligion", "ui/icons/iconCulture" },
        ["option_succession_law"] = new[] { "ui/icons/iconHeir", "ui/icons/iconFamily", "ui/specificClanIcon" }
    };

    private static Sprite LoadFirstSprite(IEnumerable<string> paths, string fallback = "ui/icons/iconOptions")
    {
        foreach (string path in paths ?? Array.Empty<string>())
        {
            Sprite sprite = SpriteTextureLoader.getSprite(path);
            if (sprite != null) return sprite;
        }
        return SpriteTextureLoader.getSprite(fallback);
    }

    private void InitialSetting()
    {
        var options = _kingdom.GetRegime().options;
        int rows = options.Keys.Count(key => key.Contains("toggle_") || key.Contains("option_"));
        float height = 6f + rows * 17f;
        var settingSpace = this.BeginVertGroup(new Vector2(SettingWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 3, 3));
        foreach (var option in options)
        {
            if (option.Key.Contains("toggle_")) AddToggleRow(settingSpace, option.Key, option.Value[0] != 0);
            else if (option.Key.Contains("option_")) AddChoiceRow(settingSpace, option.Key, option.Value[0], option.Value[1]);
        }
        settingSpace.transform.AddStretchBackground("regimeFrame", size: new Vector2(SettingWidth + 4f, height));
        _groups.Add(settingSpace.gameObject);
    }

    private AutoHoriLayoutGroup BeginSettingRow(AutoVertLayoutGroup parent, string key)
    {
        var row = parent.BeginHoriGroup(new Vector2(SettingWidth - 6f, 16), TextAnchor.MiddleLeft, 2);
        OptionIcons.TryGetValue(key, out string[] icons);
        row.AddButtonIntoHoriLayout(key, "", () => { }, LoadFirstSprite(icons), size: new Vector2(12, 12),
            showTip: true, hideBackground: true);
        var label = row.AddTextIntoHoriLayout(LM.Get(key), true, TextAnchor.MiddleLeft, new Vector2(52, 14));
        label.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        return row;
    }

    private void AddToggleRow(AutoVertLayoutGroup parent, string key, bool on)
    {
        var row = BeginSettingRow(parent, key);
        var button = row.AddButtonIntoHoriLayout($"{key}_switch", LM.Get(on ? "regime_switch_on" : "regime_switch_off"),
            () => Toggle(key), size: new Vector2(34, 12));
        ApplyChoiceVisual(button, on, true);
        _toggleButtons[key] = button;
    }

    private void AddChoiceRow(AutoVertLayoutGroup parent, string key, int current, int count)
    {
        var row = BeginSettingRow(parent, key);
        float width = Math.Max(14f, (SettingWidth - 80f) / Math.Max(1, count) - 2f);
        var buttons = new List<AdvancedButton>();
        var laws = (SuccessionLawType[])Enum.GetValues(typeof(SuccessionLawType));
        for (int i = 0; i < count; i++)
        {
            int index = i;
            string shortKey = $"{key}{i}_short";
            string label = LM.Has(shortKey) ? LM.Get(shortKey) : LM.Get($"{key}{i}");
            var button = row.AddButtonIntoHoriLayout($"{key}{i}", label, () => Option(key, index),
                size: new Vector2(width, 12), showTip: true);
            bool enabled = key != "option_succession_law" || i >= laws.Length ||
                           SuccessionLawSystem.IsSuccessionLawUnlocked(_kingdom, laws[i]);
            if (!enabled && button.TipButton != null)
                button.TipButton.textOnClickDescription = "succession_law_locked_description";
            ApplyChoiceVisual(button, i == current, enabled);
            buttons.Add(button);
        }
        _optionButtons[key] = buttons;
    }

    private static void ApplyChoiceVisual(AdvancedButton button, bool selected, bool enabled)
    {
        if (button == null) return;
        button.Button.interactable = enabled;
        if (button.Text != null)
        {
            button.Text.color = !enabled ? ChoiceLockedColor : selected ? ChoiceOnColor : ChoiceOffColor;
            button.Text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }
        if (button.Background != null)
            button.Background.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.35f);
    }

    private void Option(string title, int option)
    {
        Regime regime = _kingdom.GetRegime();
        if (title == "option_succession_law")
        {
            regime.SetSuccessionLaw((SuccessionLawType)option);
        }
        else if (title == "option_tax_level")
        {
            // 议会掌握征税同意权时，税率可能被否决：以实际生效的值为准
            regime.SetTaxLevel((TaxLevel)option);
            option = (int)regime.GetTaxLevel();
        }
        else
        {
            regime.options[title][0] = option;
        }
        if (_optionButtons.TryGetValue(title, out List<AdvancedButton> buttons))
        {
            var laws = (SuccessionLawType[])Enum.GetValues(typeof(SuccessionLawType));
            for (int i = 0; i < buttons.Count; i++)
            {
                bool enabled = title != "option_succession_law" || i >= laws.Length ||
                               SuccessionLawSystem.IsSuccessionLawUnlocked(_kingdom, laws[i]);
                ApplyChoiceVisual(buttons[i], i == option, enabled);
            }
        }
        RefreshKingdomStatus();
    }

    private void Toggle(string option)
    {
        int[] value = _kingdom.GetRegime().options[option];
        value[0] = value[0] == 0 ? 1 : 0;
        if (_toggleButtons.TryGetValue(option, out AdvancedButton button))
        {
            bool on = value[0] != 0;
            if (button.Text != null) button.Text.text = LM.Get(on ? "regime_switch_on" : "regime_switch_off");
            ApplyChoiceVisual(button, on, true);
        }
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
        _optionButtons.Clear();
        _groups.Clear();
        _customCountryNameInput = null;
        _customCountrySuffixInput = null;
    }
    // ── 政体选择：列出所有已注册政体（读配置，不写死），图标 + 名称的网格；当前政体高亮，
    //    下方给出当前政体说明与君主立宪状态 ──
    private const int RegimesPerRow = 5;

    // 没有专属按钮图标的政体借用相近的图标，最后退回游戏自带的王国图标
    private static readonly Dictionary<RegimeType, RegimeType> RegimeIconFallback = new()
    {
        [RegimeType.ClassicalRepublic] = RegimeType.Republic,
        [RegimeType.Modern] = RegimeType.Republic
    };

    private static Sprite GetRegimeIcon(RegimeType type, bool selected)
    {
        string state = selected ? "On" : "Off";
        var paths = new List<string> { $"ui/buttons/{type}{state}" };
        if (RegimeIconFallback.TryGetValue(type, out RegimeType alias)) paths.Add($"ui/buttons/{alias}{state}");
        paths.Add("ui/icons/iconKingdom");
        return LoadFirstSprite(paths);
    }

    [Hotfixable]
    private void InitialRegimeSelection()
    {
        RegimeType current = _kingdom.GetRegime().type;
        List<RegimeType> regimes = (RegimeManager.regimes?.Keys ?? Enumerable.Empty<RegimeType>())
            .OrderBy(type => (int)type).ToList();
        int rows = (regimes.Count + RegimesPerRow - 1) / RegimesPerRow;
        float height = 18f + rows * 36f + 30f;
        var regimeSpace = this.BeginVertGroup(new Vector2(SettingWidth, height), pSpacing: 1,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 3, 3));
        var title = regimeSpace.AddTextIntoVertLayout(LM.Get("regime_title"), true, TextAnchor.MiddleCenter,
            new Vector2(SettingWidth - 6f, 13));
        title.UseFixedFontSize(9, HorizontalWrapMode.Overflow);

        for (int start = 0; start < regimes.Count; start += RegimesPerRow)
        {
            var row = regimeSpace.BeginHoriGroup(new Vector2(SettingWidth - 6f, 35), TextAnchor.MiddleCenter, 3);
            foreach (RegimeType type in regimes.Skip(start).Take(RegimesPerRow))
            {
                bool selected = type == current;
                var cell = row.BeginVertGroup(new Vector2(34, 35), pSpacing: 0, pAlignment: TextAnchor.UpperCenter);
                RegimeType target = type;
                var button = cell.AddButtonIntoVertLayout(type.ToString(), "", () => ChangeRegime(target),
                    GetRegimeIcon(type, selected), size: new Vector2(22, 22));
                button.Background.enabled = selected;
                UIHelper.AttachTextTooltip(button.gameObject, $"regime_select_{type}",
                    CompositeEmpireService.GetRegimeName(type), RegimeManager.GetTemplate(type)?.description ?? "");
                _regimeButtons[type] = button;
                var name = cell.AddTextIntoVertLayout(CompositeEmpireService.GetRegimeName(type)
                        .ColorString(pColor: selected ? ChoiceOnColor : ChoiceOffColor), true,
                    TextAnchor.MiddleCenter, new Vector2(34, 10));
                name.UseFixedFontSize(6, HorizontalWrapMode.Overflow);
            }
        }

        // 当前政体说明 + 君主立宪状态
        string status = LM.Get("constitution_status_pending");
        Empire empire = _kingdom.GetEmpire();
        if (empire?.CoreKingdom == _kingdom)
        {
            ConstitutionalEconomyView economy = ConstitutionalEconomySystem.GetView(empire);
            if (economy.constitutional) status = LM.Get("constitution_status_enacted");
            else if (economy.reforming) status = LM.Get("constitution_status_reforming");
        }
        var summary = regimeSpace.AddTextIntoVertLayout(
            string.Format(LM.Get("regime_current_line"),
                CompositeEmpireService.GetRegimeName(current).ColorString(pColor: ChoiceOnColor),
                LM.Get("constitution_title"), status), true, TextAnchor.MiddleCenter,
            new Vector2(SettingWidth - 6f, 11));
        summary.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        string description = RegimeManager.GetTemplate(current)?.description ?? "";
        if (!string.IsNullOrWhiteSpace(description))
        {
            var desc = regimeSpace.AddTextIntoVertLayout(description.ColorString("#B8C6CC"), true,
                TextAnchor.UpperCenter, new Vector2(SettingWidth - 8f, 18));
            desc.UseFixedFontSize(6, HorizontalWrapMode.Wrap);
        }

        regimeSpace.transform.AddStretchBackground("regimeFrame", size: new Vector2(SettingWidth + 4f, height));
        _groups.Add(regimeSpace.gameObject);
    }

    private void ChangeRegime(RegimeType pType)
    {
        if (_kingdom.GetRegime()?.type == pType) return;
        _kingdom.SetRegimeType(pType);
        _kingdom.LoadRegime();
        RefreshKingdomStatus();
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
