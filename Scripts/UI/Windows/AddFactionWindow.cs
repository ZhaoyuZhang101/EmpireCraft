using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

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

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        FactionManager.Save();
    }

    public void ShowFactions()
    {
        var content = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        var count = 1;
        AutoHoriLayoutGroup currentAutoHoriLayout = content.BeginHoriGroup();
        var addCard = currentAutoHoriLayout.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(55, 90));
        addCard?.transform.AddStretchBackground("FactionFrame", size: new Vector2(55, 90));
        addCard.AddButtonIntoVertLayout("add_blank_faction", "", () =>
        {
            FixedFaction blank = new FixedFaction
            {
                _id = Guid.NewGuid().ToString(),
                TemporaryFactions = new List<TemporaryFaction>(),
                TemporaryFactionTypesRecord = new List<TemporaryFactionType>(),
                Type = FactionType.无
            };
            FactionManager.Config.PlayerFactions.Insert(0, blank);
            Clear();
            ShowFactions();
        }, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(20, 20));
        foreach (var faction in FactionManager.Config.PlayerFactions)
        {
            count = (count + 1) % 3;
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