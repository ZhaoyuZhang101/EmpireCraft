using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
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
        _groups.Clear();
    }

    public override void OnNormalDisable()
    {
        base.OnNormalDisable();
        FactionManager.Save();
    }

    // 派系库也用横式卡片，一行一个：最上面一条"新建空白派系"，下面依次是库里的派系。
    public void ShowFactions()
    {
        var content = this.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.UpperCenter);
        var addRow = content.BeginHoriGroup(new Vector2(188, 18), TextAnchor.MiddleCenter, 2);
        addRow.AddButtonIntoHoriLayout("add_blank_faction", LM.Get("add_blank_faction"), () =>
        {
            FixedFaction blank = new FixedFaction
            {
                _id = Guid.NewGuid().ToString(),
                TemporaryFactions = new List<TemporaryFaction>(),
                TemporaryFactionTypesRecord = new List<TemporaryFactionType>(),
                Type = FactionType.无
            };
            FactionManager.Config.PlayerFactions.Insert(0, blank);
            RefreshWindow();
        }, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(184, 15), showTip: true);
        foreach (var faction in FactionManager.Config.PlayerFactions)
            UIHelper.AddFactionCard(faction, _kingdom, parentV: content, addMode: true, action: RefreshWindow);
        _groups.Add(content.gameObject);
    }

    public void RefreshWindow()
    {
        Clear();
        ShowFactions();
    }

}