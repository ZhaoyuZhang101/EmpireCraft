using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

public class AddFactionMemberWindow: AutoLayoutWindow<AddFactionMemberWindow>
{
    private FixedFaction _faction;
    private Empire _empire;
    private List<GameObject> _group = new();
    private List<Actor> _actors = new();
    protected override void Init()
    {
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 3, 3);
    }
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        InitData();
        Clear();
        InitActors();
    }

    public void InitData()
    {
        _faction = ConfigData.CURRENT_SELECTED_FACTION;
        _empire = _faction.Empire;
        _actors = _empire.getUnits().ToList().FindAll(a => a.HasOfficeIdentity());
    }

    public void Clear()
    {
        _group.Clear();
        foreach (var item in _group)
        {
            Destroy(item);
        }
    }
    [Hotfixable]
    public void InitActors()
    {
        var count = 0;
        var actorSpace = this.BeginVertGroup();
        actorSpace.AddTextIntoVertLayout("官员列表", hideBackground:true, anchor: TextAnchor.MiddleCenter, size: new Vector2(32, 18));
        _actors = _actors.OrderByDescending(a => a.GetIdentity()?.TotalPerformance ?? 0).ToList();
        AutoHoriLayoutGroup currentAutoHoriLayout = null;
        foreach (var actor in _actors)
        {
            if (actor.isRekt()) continue;
            if (_faction.AllMembers.Contains(actor)) continue;
            count = (count+1)%2;
            if (count == 1)
            {
                currentAutoHoriLayout = actorSpace.BeginHoriGroup();
            }

            if (currentAutoHoriLayout != null)
            {
                actor.ShowActorPerformanceCard(currentAutoHoriLayout, () =>
                {
                    _faction.AddMember(actor);
                    ScrollWindow.getCurrentWindow().clickBack();
                });
            }
        }
        _group.Add(actorSpace.gameObject);
    }
}