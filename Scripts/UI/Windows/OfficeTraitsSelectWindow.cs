using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class OfficeTraitsSelectWindow : AutoLayoutWindow<OfficeTraitsSelectWindow>
{
    private BureauSetting _setting;
    private List<GameObject> _group = new();
    protected override void Init()
    {
        layout.spacing = 6;
        layout.padding = new RectOffset(3, 3, 80, 3);
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
        base.OnNormalEnable();
        Clear();
        var content = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing: 10);
        content.AddTextIntoVertLayout(LM.Get("traits_add_title"), true, TextAnchor.MiddleCenter, new Vector2(45, 20));
        var traitGroup = AssetManager.traits.list.GroupBy(t => t.getGroup());
        foreach (var item in traitGroup)
        {
            content.AddTextIntoVertLayout(LM.Get(item.Key.getLocaleID()).ColorString(pColor:item.Key.getColor()), hideBackground:true, anchor: TextAnchor.MiddleCenter, new Vector2(40, 20));
            var grid = content.BeginGridGroup(6, pSpacing:new Vector2(15, 15), pStartCorner: GridLayoutGroup.Corner.UpperLeft);
            foreach (var trait in item)
            {
                if (_setting.require_traits.Contains(trait.id)) continue;
                var traitSpace = grid.BeginVertGroup(pSpacing:-5, pAlignment: TextAnchor.MiddleCenter);
                traitSpace.AddButtonIntoVertLayout("add_button", "", action: () =>
                {
                    if (_setting.require_traits.Count < 5)
                    {
                        _setting.require_traits.Add(trait.id);
                        ActionLibrary.showWhisperTip("add_trait_success");
                    }
                    else
                    {
                        ActionLibrary.showWhisperTip("over_trait_limit");
                    }
                }, icon: SpriteTextureLoader.getSprite("ui/setOfficer"), size:new Vector2(8, 8));
                traitSpace.AddTraitIntoVertLayout(trait.id);
            }
        }
        _group.Add(content.gameObject);
    }
}
