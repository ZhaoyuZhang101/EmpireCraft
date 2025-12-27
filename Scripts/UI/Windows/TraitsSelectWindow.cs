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

public class TraitsSelectWindow: AutoLayoutWindow<TraitsSelectWindow>
{
    private FixedFaction _faction;
    private List<GameObject> _group = new();
    protected override void Init()
    {
    }

    public void Clear()
    {
        foreach (var item in _group)
        {
            Destroy(item);
        }
    }
    [Hotfixable]
    public override void OnNormalEnable()
    {
        _faction = ConfigData.CURRENT_SELECTED_FACTION;
        base.OnNormalEnable();
        Clear();
        var content = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSpacing:10);
        
        var traitGroup = AssetManager.traits.list.GroupBy(t => t.getGroup());
        foreach (var item in traitGroup)
        {
            content.AddTextIntoVertLayout(LM.Get(item.Key.getLocaleID()).ColorString(pColor:item.Key.getColor()), hideBackground:true, anchor: TextAnchor.MiddleCenter, new Vector2(40, 20));
            var grid = content.BeginGridGroup(6, pSpacing:new Vector2(15, 15), pStartCorner: GridLayoutGroup.Corner.UpperLeft);
            foreach (var trait in item)
            {
                if (_faction.RequiredTraits.Contains(trait.id)) continue;
                var traitSpace = grid.BeginVertGroup(pSpacing:-5);
                traitSpace.AddButtonIntoVertLayout("add_button", "", action: () =>
                {
                    if (_faction.RequiredTraits.Count < 5)
                    {
                        _faction.RequiredTraits.Add(trait.id);
                        ScrollWindow.getCurrentWindow().clickBack();
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