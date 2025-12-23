using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

public class FactionDetailWindow: AutoLayoutWindow<FactionDetailWindow>
{
    private FixedFaction _faction;
    private TextInput _factionNameInput;
    private List<GameObject> _groups = new List<GameObject>();
    protected override void Init()
    {
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 3, 3);
        _factionNameInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
        _factionNameInput.Setup("", ChangeFactionName);
    }
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        //初始化输入框
        InitialTextInput();
        //初始化信息栏
        InitialTopPart();
        //初始化边缘按钮
        InitialTabButtons();
    }
    [Hotfixable]
    public void InitialTopPart()
    {
        var topPart = this.BeginHoriGroup();
        topPart.AddTextIntoHoriLayout(""+_faction.Count);
        topPart.AddActorViewIntoHoriLayout(_faction.GetLeader());
        topPart.AddTextIntoHoriLayout(""+_faction.Count);
        topPart.gameObject.AdjustTopPart(transform.parent.transform, Vector2.down);
        topPart.transform.AddStretchBackground("regimeFrame", new Vector2(220, 80));
        _groups.Add(topPart.gameObject);
    }
    /// <summary>
    /// 添加侧边按钮
    /// </summary>
    private void InitialTabButtons()
    {
        //查看与编辑成员
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "show_faction_members"))
        {
            var kingdomsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            kingdomsWindowTab.Setup("show_faction_members", this.ScrollWindowComponent, action:ShowMembers,
                sprite: SpriteTextureLoader.getSprite("SplitAllUnderHeaven"));
        }
        //查看与编辑诉求
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "show_claims"))
        {
            var pastEmperorsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            pastEmperorsWindowTab.Setup("show_claims", this.ScrollWindowComponent, action:ShowClaims,
                sprite:SpriteTextureLoader.getSprite("ui/icons/actor_traits/iconJingshi"));
        }
    }

    public void Clear()
    {
        foreach (var part in _groups)
        {
            Destroy(part);
        }
    }
    public void ShowMembers(WindowMetaTab window)
    {
        Clear();
        InitialTopPart();
    }

    public void ShowClaims(WindowMetaTab window)
    {
        Clear();
        InitialTopPart();
    }
    
    public void ChangeFactionName(string newName)
    {
        _faction.Name = newName;
    }
    
    public void InitialTextInput()
    {
        _faction = ConfigData.CURRENT_SELECTED_FACTION;
        var text = _faction.Name;
        UIHelper.GenerateTextInput(this.transform.parent.transform.parent, offset:new Vector2(0, 152), default_text:text, input:_factionNameInput);
    }
}