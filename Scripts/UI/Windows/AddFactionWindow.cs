using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using NotImplementedException = System.NotImplementedException;

namespace EmpireCraft.Scripts.UI.Windows;

public class AddFactionWindow: AutoLayoutWindow<AddFactionWindow>
{
    public List<GameObject> _groups = new();
    private Kingdom _kingdom;
    private Regime _regime;
    protected override void Init()
    {
    }

    public override void OnNormalEnable()
    {
        _kingdom = SelectedMetas.selected_kingdom;
        _regime = _kingdom.GetRegime();
        base.OnNormalEnable();
        Clear();
        ShowFactions();
    }

    public void Clear()
    {
        foreach (var group in _groups)
        {
            Destroy(group);
        }
    }
    public void ShowFactions()
    {
        var content = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        var count = 0;
        AutoHoriLayoutGroup currentAutoHoriLayout = null;
        foreach (var faction in FactionManager.Config.PlayerFactions)
        {
            count = (count + 1) % 2;
            if (count == 1)
            {
                currentAutoHoriLayout = content.BeginHoriGroup();
            }

            if (currentAutoHoriLayout != null)
            {
                UIHelper.AddFactionCard(faction, _kingdom, currentAutoHoriLayout, addMode:true, action:RefreshWindow);
            }
        }
        _groups.Add(content.gameObject);
    }

    public void RefreshWindow()
    {
        Clear();
        ShowFactions();
    }

}