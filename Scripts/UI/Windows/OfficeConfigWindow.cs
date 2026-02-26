using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EmpireCraft.Scripts.UI.Windows;
public class OfficeConfigWindow : AutoLayoutWindow<OfficeConfigWindow>
{
    private Kingdom _kingdom;
    private Regime _regime => _kingdom.GetRegime();
    private List<GameObject> _groups = new();
    private List<GameObject> _popups = new();
    private GameObject _header;
    private GameObject _content;
    private enum EditCategory { Armies, Kingdoms, Cities }
    private EditCategory _currentCategory = EditCategory.Kingdoms;
    protected override void Init()
    {
        layout.spacing = 6;
        layout.padding = new RectOffset(3, 3, 70, 3);
    }
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        _kingdom = SelectedMetas.selected_kingdom;
        Clear();
        InitialHeader();
        ShowCategory();
    }
    private void Clear()
    {
        foreach (var g in _groups) Destroy(g);
        foreach (var p in _popups) Destroy(p);
        _groups.Clear();
        _popups.Clear();
        _header = null;
        _content = null;
    }
    [Hotfixable]
    private void InitialHeader()
    {
        var header = this.BeginVertGroup(pSpacing: 6);
        header.AddTextIntoVertLayout(LM.Get("office_config_title"), true, TextAnchor.MiddleCenter, new Vector2(40, 20));
        var tabs = header.BeginHoriGroup(pAlignment:TextAnchor.MiddleCenter);
        tabs.AddButtonIntoHoriLayout("edit_armies", LM.Get("office_tab_armies"), () =>
        {
            _currentCategory = EditCategory.Armies;
            ShowCategory(true);
        }, size: new Vector2(25, 12));
        tabs.AddButtonIntoHoriLayout("edit_kingdoms", LM.Get("office_tab_kingdoms"), () =>
        {
            _currentCategory = EditCategory.Kingdoms;
            ShowCategory(true);
        }, size: new Vector2(25, 12));
        tabs.AddButtonIntoHoriLayout("edit_cities", LM.Get("office_tab_cities"), () =>
        {
            _currentCategory = EditCategory.Cities;
            ShowCategory(true);
        }, size: new Vector2(25, 12));
        var actionBar = header.BeginHoriGroup(pAlignment:TextAnchor.MiddleCenter);
        actionBar.AddButtonIntoHoriLayout("save_office_config", LM.Get("office_save"), SaveConfig, size: new Vector2(25, 12));
        actionBar.AddButtonIntoHoriLayout("reload_office_config", LM.Get("office_reload"), ReloadConfig, size: new Vector2(25, 12));
        actionBar.AddButtonIntoHoriLayout("delete_user_office_config", LM.Get("office_delete_config"), DeleteUserConfig, size: new Vector2(25, 12));
        actionBar.AddButtonIntoHoriLayout("restore_default_office_config", LM.Get("office_restore_default"), RestoreDefaultConfig, size: new Vector2(25, 12));
        header.gameObject.AdjustTopPart(transform.parent.transform);
        header.transform.AddStretchBackground("regimeFrame", new Vector2(220, 70));
        _groups.Add(header.gameObject);
        _header = header.gameObject;
    }
    [Hotfixable]
    private void ShowCategory(bool clearOld = false)
    {
        if (clearOld && _content != null)
        {
            Destroy(_content);
            _content = null;
        }
        var content = this.BeginVertGroup(pSpacing: 6);
        switch (_currentCategory)
        {
            case EditCategory.Armies:
                ShowArmies(content);
                break;
            case EditCategory.Kingdoms:
                ShowKingdoms(content);
                break;
            case EditCategory.Cities:
                ShowCities(content);
                break;
        }
        _groups.Add(content.gameObject);
        _content = content.gameObject;
    }
    private void ShowArmies(AutoVertLayoutGroup parent)
    {
        if (_regime.bureau_config.armies == null) return;
        parent.AddTextIntoVertLayout(LM.Get("office_armies_title"), true, TextAnchor.MiddleCenter, new Vector2(50, 18));
        var addBar = parent.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        addBar.AddButtonIntoHoriLayout("add_army_office", LM.Get("office_add"), ShowAddArmyOfficePopup, size: new Vector2(28, 12));
        var list = parent.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 10);
        foreach (var kv in _regime.bureau_config.armies)
        {
            var item = list.BeginVertGroup(pSpacing: 6, pAlignment: TextAnchor.MiddleCenter);
            ShowSingleSetting(item, kv.Value, ctx: "army", key: kv.Key);
            list.AddChild(item.gameObject);
        }
    }
    private void ShowKingdoms(AutoVertLayoutGroup parent)
    {
        if (_regime.bureau_config.kingdoms == null) return;
        parent.AddTextIntoVertLayout(LM.Get("office_kingdoms_title"), true, TextAnchor.MiddleCenter, new Vector2(50, 18));
        var addBar = parent.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        addBar.AddButtonIntoHoriLayout("add_kingdom_office", LM.Get("office_add"), () =>
        {
            ShowAddKingdomOfficePopup();
        }, size: new Vector2(28, 12));
        var list = parent.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 10);
        foreach (var kv in _regime.bureau_config.kingdoms)
        {
            var item = list.BeginVertGroup(pSpacing: 6, pAlignment: TextAnchor.MiddleCenter);
            ShowSingleSetting(item, kv.Value, ctx: "kingdom", key: kv.Key.ToString());
            list.AddChild(item.gameObject);
        }
    }
    private void ShowCities(AutoVertLayoutGroup parent)
    {
        if (_regime.bureau_config.cities == null) return;
        parent.AddTextIntoVertLayout(LM.Get("office_cities_title"), true, TextAnchor.MiddleCenter, new Vector2(50, 18));
        var addBar = parent.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        addBar.AddButtonIntoHoriLayout("add_city_office", LM.Get("office_add"), ShowAddCityOfficePopup, size: new Vector2(28, 12));
        var list = parent.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 10);
        foreach (var kv in _regime.bureau_config.cities)
        {
            var item = list.BeginVertGroup(pSpacing: 6, pAlignment: TextAnchor.MiddleCenter);
            ShowSingleSetting(item, kv.Value, ctx: "city", key: kv.Key.ToString());
            list.AddChild(item.gameObject);
        }
    }
    [Hotfixable]
    private void ShowSingleSetting(AutoVertLayoutGroup layout, BureauSetting setting, string ctx, string key)
    {
        var nameBar = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        nameBar.AddTextIntoHoriLayout(LM.Get(setting.pre), hideBackground: true, anchor: TextAnchor.MiddleCenter, size: new Vector2(70, 14));
        layout.AddTextIntoVertLayout(
            $"{LM.Get("office_type_label")}: {LM.Get(string.Join("_", _regime.type.ToString(), "officiallevel", setting.type))}", hideBackground: true, anchor: TextAnchor.MiddleCenter, size: new Vector2(55, 12));
        var opBar = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        opBar.AddButtonIntoHoriLayout("remove_office", LM.Get("office_remove"), () =>
        {
            RemoveOffice(ctx, key);
        }, size: new Vector2(28, 12));
        ShowSelectionEditor(layout, setting);
        var traitBar = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        ShowTraitsEditor(traitBar, setting);
        var condTitle = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        condTitle.AddTextIntoHoriLayout(LM.Get("office_dynamic_conditions"), hideBackground: true, anchor: TextAnchor.MiddleCenter, size: new Vector2(55, 12));
        condTitle.AddButtonIntoHoriLayout("edit_conditions", "", () =>
        {
            ConfigData.CURRENT_SELECTED_BUREAU_SETTING = setting;
            ConfigData.CURRENT_SELECTED_BUREAU_CTX = ctx;
            ScrollWindow.showWindow(nameof(OfficeConditionEditorWindow));
        }, SpriteTextureLoader.getSprite("ui/editor"), size: new Vector2(10, 10));
        ShowConditionList(layout, setting);
        layout.transform.AddStretchBackground("FactionFrame", new Vector2(180, 270));
    }
    [Hotfixable]
    private void ShowSelectionEditor(AutoVertLayoutGroup layout, BureauSetting setting)
    {
        var titleRow = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        titleRow.AddTextIntoHoriLayout(LM.Get("office_selection_title"), hideBackground: true, anchor: TextAnchor.MiddleCenter, size: new Vector2(55, 12));
        var localRow = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        layout.transform.AddNormalOptionIntoHori(localRow, "office_select_local", () =>
        {
            setting.select_from_local = !setting.select_from_local;
            localRow.transform.ClearChildren();
            layout.transform.AddNormalOptionIntoHori(localRow, "office_select_local", () => { setting.select_from_local = !setting.select_from_local; }, setting.select_from_local, false);
        }, setting.select_from_local, false);
        var methodRow = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        var methods = new[] { LeaderSelectMethod.Default, LeaderSelectMethod.Exam, LeaderSelectMethod.Succession, LeaderSelectMethod.Vote, LeaderSelectMethod.Army, LeaderSelectMethod.Harem };
        var currentIndex = Array.IndexOf(methods, setting.leader_select_method);
        List<AdvancedButton> methodButtons = null;
        methodButtons = layout.transform.AddMultipleOption(methodRow, "office_leader_method", (t, i) =>
        {
            setting.leader_select_method = methods[i];
            for (int j = 0; j < methodButtons.Count; j++)
            {
                methodButtons[j].SetStatus(j == i);
            }
        }, currentIndex < 0 ? 0 : currentIndex, methods.Length, false);
        var honoraryRow = layout.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        int currentHonorIndex = Mathf.Clamp(setting.honorary - 1, 0, 8);
        List<AdvancedButton> honorButtons = null;
        honorButtons = layout.transform.AddMultipleOption(honoraryRow, "office_honorary_label", (t, i) =>
        {
            setting.honorary = i + 1;
            for (int j = 0; j < honorButtons.Count; j++)
            {
                honorButtons[j].SetStatus(j == i);
            }
        }, currentHonorIndex, 9, false);
    }
    private string FormatConditionText(string cond)
    {
        if (string.IsNullOrEmpty(cond)) return cond;
        var parts = cond.Split(':');
        var key = parts[0];
        var value = parts.Length > 1 ? parts[1] : "";
        var keyText = LM.Get(key);
        if (value.Contains("|"))
        {
            var opVal = value.Split('|');
            var op = opVal[0];
            var v = opVal.Length > 1 ? opVal[1] : "";
            return $"{keyText} {LM.Get(op)} {v}";
        }
        else
        {
            var vText = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? LM.Get("cond_true") :
                value.Equals("false", StringComparison.OrdinalIgnoreCase) ? LM.Get("cond_false") : value;
            return $"{keyText}: {vText}";
        }
    }
    [Hotfixable]
    private void ShowTraitsEditor(AutoHoriLayoutGroup parent, BureauSetting setting)
    {
        foreach (var trait in setting.require_traits.ToList())
        {
            var chip = parent.BeginVertGroup(pSize: new Vector2(20, 40), pAlignment: TextAnchor.MiddleCenter, pSpacing: -5);
            chip.AddTraitIntoVertLayout(trait);
            chip.AddButtonIntoVertLayout("remove_trait", "", () =>
            {
                setting.require_traits.Remove(trait);
                parent.transform.ClearChildren();
                ShowTraitsEditor(parent, setting);
            }, SpriteTextureLoader.getSprite("ui/iconRemove"), size: new Vector2(8, 8));
        }
        var add = parent.BeginVertGroup(pSize: new Vector2(20, 40), pAlignment: TextAnchor.MiddleCenter);
        add.AddButtonIntoVertLayout("add_trait", "", () =>
        {
            ConfigData.CURRENT_SELECTED_BUREAU_SETTING = setting;
            ScrollWindow.showWindow(nameof(OfficeTraitsSelectWindow));
        }, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(10, 10));
    }
    [Hotfixable]
    private void ShowConditionList(AutoVertLayoutGroup parent, BureauSetting setting)
    {
        var condList = parent.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        foreach (var c in setting.condition.ToList())
        {
            var row = condList.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
            row.AddTextIntoHoriLayout(FormatConditionText(c), hideBackground: true, anchor: TextAnchor.MiddleCenter, size: new Vector2(80, 12));
            row.AddButtonIntoHoriLayout("remove_condition", "", () =>
            {
                setting.condition.Remove(c);
                condList.transform.ClearChildren();
                ShowConditionList(parent, setting);
            }, SpriteTextureLoader.getSprite("ui/iconRemove"), size: new Vector2(8, 8));
        }
    }
    private void RemoveOffice(string ctx, string key)
    {
        switch (ctx)
        {
            case "army":
                _regime.bureau_config.armies.Remove(key);
                break;
            case "kingdom":
                if (Enum.TryParse<KingdomType>(key, out var k)) _regime.bureau_config.kingdoms.Remove(k);
                break;
            case "city":
                if (Enum.TryParse<CityType>(key, out var c)) _regime.bureau_config.cities.Remove(c);
                break;
        }
        ShowCategory(true);
    }
    private void ShowAddArmyOfficePopup()
    {
        foreach (var p in _popups) Destroy(p);
        _popups.Clear();
        var popup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 6);
        popup.AddTextIntoVertLayout(LM.Get("office_add"), true, TextAnchor.MiddleCenter, new Vector2(35, 15));
        var input = UIHelper.GenerateTextInput(popup.transform, new Vector2(60, 12), default, "custom_army_office", s => { });
        popup.AddButtonIntoVertLayout("confirm_add_army_office", LM.Get("confirm"), () =>
        {
            var key = input.input.text;
            if (string.IsNullOrEmpty(key) || _regime.bureau_config.armies.ContainsKey(key)) return;
            var template = _regime.bureau_config.armies.Values.FirstOrDefault();
            var setting = new BureauSetting
            {
                pre = template?.pre ?? "",
                type = template?.type ?? 1,
                description = template?.description ?? "",
                powers = template?.powers?.ToList() ?? new List<OfficerPowerType>(),
                merit = template?.merit ?? -1,
                honorary = template?.honorary ?? -1,
                select_from_local = template?.select_from_local ?? false,
                leader_select_method = template?.leader_select_method ?? LeaderSelectMethod.Default,
                require_traits = new List<string>(),
                condition = new List<string>()
            };
            _regime.bureau_config.armies[key] = setting;
            foreach (var p in _popups) Destroy(p);
            _popups.Clear();
            ShowCategory(true);
        }, size: new Vector2(25, 12));
        _popups.Add(popup.gameObject);
    }
    private void ShowAddKingdomOfficePopup()
    {
        foreach (var p in _popups) Destroy(p);
        _popups.Clear();
        var popup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 6);
        popup.AddTextIntoVertLayout(LM.Get("office_add"), true, TextAnchor.MiddleCenter, new Vector2(35, 15));
        var list = popup.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 8);
        var existing = _regime.bureau_config.kingdoms.Keys.ToHashSet();
        foreach (var value in Enum.GetValues(typeof(KingdomType)).Cast<KingdomType>())
        {
            if (existing.Contains(value)) continue;
            var line = list.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
            line.AddButtonIntoHoriLayout("add_k_"+value, value.ToString(), () =>
            {
                var template = _regime.bureau_config.kingdoms.Values.FirstOrDefault();
                var setting = new BureauSetting
                {
                    pre = template?.pre ?? "",
                    type = template?.type ?? 5,
                    description = template?.description ?? "",
                    powers = template?.powers?.ToList() ?? new List<OfficerPowerType>(),
                    merit = template?.merit ?? -1,
                    honorary = template?.honorary ?? -1,
                    select_from_local = template?.select_from_local ?? false,
                    leader_select_method = template?.leader_select_method ?? LeaderSelectMethod.Default,
                    require_traits = new List<string>(),
                    condition = new List<string>(),
                    city_type = template?.city_type ?? CityType.Feudalism_city
                };
                _regime.bureau_config.kingdoms[value] = setting;
                foreach (var p in _popups) Destroy(p);
                _popups.Clear();
                ShowCategory(true);
            }, size: new Vector2(50, 14));
            list.AddChild(line.gameObject);
        }
        _popups.Add(popup.gameObject);
    }
    private void ShowAddCityOfficePopup()
    {
        foreach (var p in _popups) Destroy(p);
        _popups.Clear();
        var popup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 6);
        popup.AddTextIntoVertLayout(LM.Get("office_add"), true, TextAnchor.MiddleCenter, new Vector2(35, 15));
        var list = popup.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 8);
        var existing = _regime.bureau_config.cities.Keys.ToHashSet();
        foreach (var value in Enum.GetValues(typeof(CityType)).Cast<CityType>())
        {
            if (existing.Contains(value)) continue;
            var line = list.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
            line.AddButtonIntoHoriLayout("add_c_"+value, value.ToString(), () =>
            {
                var template = _regime.bureau_config.cities.Values.FirstOrDefault();
                var setting = new BureauSetting
                {
                    pre = template?.pre ?? "",
                    type = template?.type ?? 9,
                    description = template?.description ?? "",
                    powers = template?.powers?.ToList() ?? new List<OfficerPowerType>(),
                    merit = template?.merit ?? -1,
                    honorary = template?.honorary ?? -1,
                    select_from_local = template?.select_from_local ?? false,
                    leader_select_method = template?.leader_select_method ?? LeaderSelectMethod.Default,
                    require_traits = new List<string>(),
                    condition = new List<string>(),
                    city_type = value
                };
                _regime.bureau_config.cities[value] = setting;
                foreach (var p in _popups) Destroy(p);
                _popups.Clear();
                ShowCategory(true);
            }, size: new Vector2(50, 14));
            list.AddChild(line.gameObject);
        }
        _popups.Add(popup.gameObject);
    }
    private void SaveConfig()
    {
        try
        {
            var res = OfficeUserConfig.Save(_regime.type, _regime.bureau_config);
            ActionLibrary.showWhisperTip(res ? "save_success" : "save_failed");
        }
        catch (Exception e)
        {
            LogService.LogInfo("保存用户官位配置失败: " + e.Message);
            ActionLibrary.showWhisperTip("save_failed");
        }
    }
    private void ReloadConfig()
    {
        try
        {
            var filePath = Path.Combine(ModClass._declare.FolderPath, "Scripts", "Regimes", "Configs", _regime.type.ToString(), "SystemConfig.json");
            if (!File.Exists(filePath))
            {
                ActionLibrary.showWhisperTip("save_failed");
                return;
            }
            var text = File.ReadAllText(filePath);
            var dict = JsonConvert.DeserializeObject<Dictionary<RegimeType, Regime>>(text);
            if (dict != null && dict.TryGetValue(_regime.type, out var newRegime))
            {
                newRegime.type = _regime.type;
                OfficeUserConfig.Load();
                if (OfficeUserConfig.Config.TryGetValue(_regime.type, out var overrideCfg))
                {
                    newRegime.bureau_config = overrideCfg;
                }
                RegimeManager.regimes[_regime.type] = newRegime;
                _kingdom.LoadRegime();
                ActionLibrary.showWhisperTip("save_success");
                Clear();
                InitialHeader();
                ShowCategory();
            }
            else
            {
                ActionLibrary.showWhisperTip("save_failed");
            }
        }
        catch (Exception e)
        {
            LogService.LogInfo("重新加载官位配置失败: " + e.Message);
            ActionLibrary.showWhisperTip("save_failed");
        }
    }
    private void DeleteUserConfig()
    {
        var res = OfficeUserConfig.Remove(_regime.type);
        ActionLibrary.showWhisperTip(res ? "save_success" : "save_failed");
    }
    private void RestoreDefaultConfig()
    {
        var res = OfficeUserConfig.Remove(_regime.type);
        ReloadConfig();
        ActionLibrary.showWhisperTip(res ? "save_success" : "save_failed");
    }
}
