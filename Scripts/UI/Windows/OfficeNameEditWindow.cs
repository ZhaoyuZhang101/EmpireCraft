using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;
public class OfficeNameEditWindow : AutoLayoutWindow<OfficeNameEditWindow>
{
    private BureauSetting _setting;
    private TextInput _input;
    protected override void Init()
    {
        layout.spacing = 6;
        layout.padding = new RectOffset(3, 3, 3, 3);
    }
    [Hotfixable]
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        _setting = ConfigData.CURRENT_SELECTED_BUREAU_SETTING;
        var content = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 8);
        content.AddTextIntoVertLayout(LM.Get("name_edit_title"), true, TextAnchor.MiddleCenter, new Vector2(45, 20));
        _input = UIHelper.GenerateTextInput(this.transform.parent.transform, new Vector2(100, 14), default, _setting.pre, s => { });
        content.AddButtonIntoVertLayout("confirm_edit_name", LM.Get("confirm"), () =>
        {
            _setting.pre = _input.input.text;
            ActionLibrary.showWhisperTip("office_name_updated");
        }, size: new Vector2(25, 12));
    }
}
