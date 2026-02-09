using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class OfficeConditionEditorWindow : AutoLayoutWindow<OfficeConditionEditorWindow>
{
    private BureauSetting _setting;
    private string _ctx;
    private List<GameObject> _group = new();
    protected override void Init()
    {
        layout.spacing = 6;
        layout.padding = new RectOffset(3, 3, 3, 3);
    }
    public void Clear()
    {
        foreach (var item in _group)
        {
            Destroy(item);
        }
        _group.Clear();
    }
    [Hotfixable]
    public override void OnNormalEnable()
    {
        _setting = ConfigData.CURRENT_SELECTED_BUREAU_SETTING;
        _ctx = ConfigData.CURRENT_SELECTED_BUREAU_CTX;
        base.OnNormalEnable();
        Clear();
        var content = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 10);
        content.AddTextIntoVertLayout(LM.Get("conditions_edit_title"), true, TextAnchor.MiddleCenter, new Vector2(45, 20));
        var list = content.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        foreach (var c in _setting.condition.ToList())
        {
            var row = list.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
            row.AddTextIntoHoriLayout(FormatConditionText(c), hideBackground:true, anchor: TextAnchor.MiddleCenter, size:new Vector2(100, 12));
            row.AddButtonIntoHoriLayout("remove_condition", "", () =>
            {
                _setting.condition.Remove(c);
            }, SpriteTextureLoader.getSprite("ui/iconRemove"), size:new Vector2(10, 10));
        }
        var add = content.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        add.AddTextIntoVertLayout(LM.Get("conditions_add_title"), hideBackground:true, anchor: TextAnchor.MiddleCenter, size:new Vector2(35, 15));
        var keys = GetAvailableConditionKeys(_ctx);
        var keyList = add.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 8);
        foreach (var key in keys)
        {
            var line = keyList.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
            line.AddButtonIntoHoriLayout("cond_"+key, LM.Get(key), () =>
            {
                BuildEditor(content, key);
            }, size: new Vector2(50, 14));
            keyList.AddChild(line.gameObject);
        }
        _group.Add(content.gameObject);
    }
    private void BuildEditor(AutoVertLayoutGroup content, string key)
    {
        var editor = content.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 5);
        editor.AddTextIntoVertLayout($"{LM.Get("edit_label")}: {LM.Get(key)}", true, TextAnchor.MiddleCenter, new Vector2(35, 15));
        if (key is "controlled_titles" or "cities_count")
        {
            var opBar = editor.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
            var ops = new[] { ">=", "<=", ">", "<", "==" };
            int selected = 0;
            List<AdvancedButton> operatorButtons = null;
            operatorButtons = editor.transform.AddMultipleOption(opBar, "operator", (t, i) =>
            {
                selected = i;
                for (int j = 0; j < operatorButtons.Count; j++)
                {
                    operatorButtons[j].SetStatus(j == i);
                }
            }, 0, ops.Length, false);
            var valInput = editor.transform.GenerateTextInput(new Vector2(50, 12), default, "1", s => { });
            editor.AddButtonIntoVertLayout("confirm_add_condition", LM.Get("cond_add"), () =>
            {
                int v;
                if (!int.TryParse(valInput.input.text, out v)) v = 1;
                var cond = $"{key}:{ops[selected]}|{v}";
                _setting.condition.Add(cond);
                Clear();
                OnNormalEnable();
            }, size: new Vector2(20, 12));
        }
        else
        {
            bool value = false;
            var bar = editor.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
            AdvancedButton btn = null;
            btn = bar.transform.AddNormalOptionIntoHori(bar, "cond_value", () =>
            {
                value = !value;
                btn.SetStatus(value);
            }, value, hasIcon: false, isOption: false, size: new Vector2(15, 15));
            editor.AddButtonIntoVertLayout("confirm_add_condition", LM.Get("cond_add"), () =>
            {
                _setting.condition.Add($"{key}:{value.ToString().ToLower()}");
                Clear();
                OnNormalEnable();
            }, size: new Vector2(20, 12));
        }
        _group.Add(editor.gameObject);
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
    private List<string> GetAvailableConditionKeys(string ctx)
    {
        switch (ctx)
        {
            case "army":
                return new List<string> { "empire_center", "is_capital", "city_is_border" };
            case "kingdom":
                return new List<string> { "empire_center", "succession", "allow_diplomacy", "another_race", "controlled_titles", "cities_count", "empire_royal" };
            case "city":
                return new List<string> { "city_is_border" };
            default:
                return new List<string>();
        }
    }
}
