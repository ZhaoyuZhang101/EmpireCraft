using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EmpireCraft.Scripts.UI.Windows;

public class FactionDetailWindow: AutoLayoutWindow<FactionDetailWindow>
{
    private FixedFaction _faction;
    private Kingdom _kingdom;
    private TextInput _factionNameInput;
    private List<GameObject> _groups = new List<GameObject>();
    private AutoVertLayoutGroup topPart;
    private bool saveIntoCurrentFaction = false;
    private AdvancedButton SaveOption = null;
    protected override void Init()
    {
        layout.spacing = 3;
        layout.padding = new RectOffset(3, 3, 80, 3);
        _factionNameInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
        _factionNameInput.Setup("", ChangeFactionName);
    }
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        _kingdom = SelectedMetas.selected_kingdom;   
        Clear();
        //初始化输入框
        InitialTextInput();
        //初始化信息栏
        InitialTopPart();
        //初始化边缘按钮
        InitialTabButtons();
        //初始化成员列表
        StartCoroutine(ShowMembers());
    }
    [Hotfixable]
    public void InitialTopPart()
    {
        if (topPart)
        {
            Destroy(topPart.gameObject);
        }   
        topPart = this.BeginVertGroup(pSpacing:-12);
        var infoPart = topPart.BeginHoriGroup();
        infoPart.AddActorViewIntoHoriLayout(_faction.GetLeader());
        var leftPart = infoPart.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        leftPart.AddTextIntoVertLayout($"{LM.Get("label_member_count")}: "+_faction.Count);
        leftPart.AddTextIntoVertLayout($"{LM.Get("label_total_power")}: "+_faction.TotalPower);
        var rightPart = infoPart.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        rightPart.AddButtonIntoHoriLayout("recover_tfaction", icon: SpriteTextureLoader.getSprite("ui/changeOfficer"), size: new Vector2(15, 15), showTip:true, action:
            () =>
            {
                _faction.TemporaryFactionTypesRecord = new List<TemporaryFactionType>(_faction.TemporaryFactionTypes);
                _faction.TemporaryFactions = _faction.ConvertToObjectFromFactionType();
                ShowClaims();
            });
        rightPart.AddButtonIntoHoriLayout("save_faction", icon:SpriteTextureLoader.getSprite("ui/icons/iconSaveLocal"), size:new Vector2(15, 15), showTip:true, action:
            ShowFactionSaveSpace);
        topPart.AddTextIntoVertLayout("所需特质: ", hideBackground:true, TextAnchor.LowerCenter, size: new Vector2(25, 15));
        var traitsPart =  topPart.BeginHoriGroup();
        InitTraitPart(traitsPart);
        topPart.gameObject.AdjustTopPart(transform.parent.transform);
        topPart.transform.AddStretchBackground("regimeFrame", new Vector2(220, 75));
    }
    /// <summary>
    /// 显示派系存储配置
    /// </summary>
    public void ShowFactionSaveSpace()
    {
        Clear();
        InitialTopPart();
        var factionSavePart = this.BeginVertGroup(pAlignment:  TextAnchor.MiddleCenter);
        factionSavePart.AddTextIntoVertLayout(LM.Get("save_faction_setting"), true, size: new Vector2(45, 25));
        var saveCardSpace = factionSavePart.BeginHoriGroup();
        SaveOption = saveCardSpace.transform.AddNormalOptionIntoHori(saveCardSpace.BeginHoriGroup(), "save_into_current_title",
            () =>
            {
                saveIntoCurrentFaction = !saveIntoCurrentFaction;
                if (SaveOption != null)
                {
                    SaveOption.SetStatus(saveIntoCurrentFaction);
                }
            }, saveIntoCurrentFaction, size: new Vector2(25, 25));
        
        saveCardSpace.AddButtonIntoHoriLayout("save_faction_confirm", "", () =>
        {
            FactionManager.Config.PlayerFactions.Add(_faction.DeepClone());
            if (saveIntoCurrentFaction)
            {
                FactionManager.Config.PlayerRegimeFactions[_kingdom.GetRegime().type] = _kingdom.GetRegime().GetPlayerFactions().Select(f=>f.DeepClone()).ToList();
            }
            var res = FactionManager.Save();
            ActionLibrary.showWhisperTip(res ? "save_success" : "save_failed");
        }, size: new Vector2(25, 25), icon:SpriteTextureLoader.getSprite("ui/icons/iconSaveLocal"));
        _groups.Add(factionSavePart.gameObject);
    }
    public void InitTraitPart(AutoHoriLayoutGroup parent)
    {
        
        foreach (var trait in _faction.RequiredTraits)
        {
            ShowTrait(trait, parent);
        }
        var addOrRecoverSpace = parent.BeginVertGroup(pSize: new Vector2(20, 45), TextAnchor.MiddleCenter);
            
        addOrRecoverSpace.AddButtonIntoVertLayout("addNewTrait", "", () =>
        {
            ScrollWindow.showWindow(nameof(TraitsSelectWindow));
        }, SpriteTextureLoader.getSprite("ui/setOfficer"), showTip:true, size:new Vector2(10, 10));
        addOrRecoverSpace.AddButtonIntoVertLayout("recoverTrait", "", ()=> RecoverTrait(parent), SpriteTextureLoader.getSprite("ui/changeOfficer"), showTip:true, size:new Vector2(10, 10));
    }
    public void ShowTrait(string trait, AutoHoriLayoutGroup parent)
    {
        var actorTrait = AssetManager.traits.get(trait);
        if (actorTrait != null)
        {
            var traitEdit = parent.BeginVertGroup(pSize:new Vector2(20, 45), pSpacing: -5);
            traitEdit.AddTraitIntoVertLayout(trait);
            traitEdit.AddButtonIntoVertLayout("remove_trait", "", ()=>RemoveTrait(trait, parent), icon: SpriteTextureLoader.getSprite("ui/iconRemove"), size: new Vector2(8, 8));
        }
    }
    public void RemoveTrait(string trait, AutoHoriLayoutGroup parent)
    {
        _faction.RequiredTraits.Remove(trait);
        parent.transform.ClearChildren();
        InitTraitPart(parent);
    }
    public void RecoverTrait(AutoHoriLayoutGroup parent)
    {
        _faction.RecoverTrait();
        parent.transform.ClearChildren();
        InitTraitPart(parent);
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
            kingdomsWindowTab.Setup("show_faction_members", this.ScrollWindowComponent, action: _ =>
                {
                    StartCoroutine(ShowMembers());
                }, 
                sprite: SpriteTextureLoader.getSprite("ui/icons/actor_traits/iconJingshi"));
        }
        //查看与编辑诉求
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "show_claims"))
        {
            var pastEmperorsWindowTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            pastEmperorsWindowTab.Setup("show_claims", this.ScrollWindowComponent, action:ShowClaims,
                sprite:SpriteTextureLoader.getSprite("SplitAllUnderHeaven"));
        }
    }

    public void Clear()
    {
        foreach (var part in _groups)
        {
            Destroy(part);
        }
        _groups.Clear();
    }
    /// <summary>
    /// 显示和编辑派系成员
    /// </summary>
    /// <param name="window"></param>
    [Hotfixable]
    public IEnumerator ShowMembers(WindowMetaTab window=null)
    {
        Clear();
        InitialTopPart();
        var content = this.BeginVertGroup();
        content.AddTextIntoVertLayout(LM.Get("label_all_members"), true, TextAnchor.MiddleCenter, size:new Vector2(40, 15));
        yield  return CoroutineHelper.wait_for_next_frame;
        var card = content.BeginVertGroup(pSpacing:-5);
        StartCoroutine(ShowAllMembers(card));
        yield  return CoroutineHelper.wait_for_next_frame;
        _groups.Add(content.gameObject);
    }
    [Hotfixable]
    public IEnumerator ShowAllMembers(AutoVertLayoutGroup parent)
    {
        foreach (var actor in _faction.AllMembers)
        {
            try
            {
                ShowActorCard(parent, actor);
            }
            catch
            {
                // ignored
            }

            yield return CoroutineHelper.wait_for_next_frame;
        }

        var buttonSpace = parent.BeginVertGroup(pSize: new Vector2(185, 55), TextAnchor.MiddleCenter);
        buttonSpace.AddButtonIntoVertLayout("add_member", "", () =>
        {
            if (!_faction.Empire.isRekt())
            {
                ScrollWindow.showWindow(nameof(AddFactionMemberWindow));
            }
            ActionLibrary.showWhisperTip("inactivated_faction");
        }, icon: SpriteTextureLoader.getSprite("ui/setOfficer"), size:new Vector2(25, 25), hideBackground:false);
        buttonSpace.transform.transform.AddStretchBackground("FactionFrame", size: new Vector2(185, 45));
        yield return CoroutineHelper.wait_for_next_frame;
        
    }
    /// <summary>
    /// 显示单个的成员信息
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="pActor"></param>
    [Hotfixable]
    public void ShowActorCard(AutoVertLayoutGroup parent, Actor pActor)
    {
        if (!pActor.HasOfficeIdentity()) return;
        var cardSpace = parent.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        //角色部分
        var actorSpace = cardSpace.BeginHoriGroup(pAlignment:TextAnchor.MiddleCenter);
        actorSpace.transform.ClearChildren();
        //角色基本信息
        actorSpace.AddActorViewIntoHoriLayout(pActor, description:pActor.IsOnOffice()?pActor.GetOffice().GetOfficeName():LM.Get("label_none"));
        //角色官位信息
        var powerPart = actorSpace.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
        powerPart.AddTextIntoVertLayout($"{LM.Get("label_name")}: {pActor.name}", anchor: TextAnchor.MiddleLeft);
        var performanceText = powerPart.AddTextIntoVertLayout($"{LM.Get("label_total_performance")}: {(int)pActor.GetIdentity().TotalPerformance}", anchor: TextAnchor.MiddleLeft);
        var levelText = powerPart.AddTextIntoVertLayout($"{LM.Get("label_rank")}: {pActor.GetIdentity().GetHonoraryOfficialName()}", anchor: TextAnchor.MiddleLeft);
        RefreshText(pActor, performanceText, levelText);
        //编辑部分
        var infoSpace = cardSpace.BeginHoriGroup(pAlignment:TextAnchor.MiddleCenter);
        //编辑绩效部分
        var editPerformancePart = infoSpace.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
        editPerformancePart.AddButtonIntoVertLayout("add_performance", LM.Get("action_add_performance"), () => AddPerformance(pActor.GetIdentity(), performanceText, levelText), size: new Vector2(20, 12));
        editPerformancePart.AddButtonIntoVertLayout("sub_performance", LM.Get("action_sub_performance"), () => SubPerformance(pActor.GetIdentity(), performanceText, levelText), size: new Vector2(20, 12));
        //删改角色部分
        var editActorPart = infoSpace.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
        var leaderTurnOnButton = editActorPart.transform.AddNormalOptionIntoVert(editActorPart, "set_leader", () =>
        
        {
            SetLeader(pActor);
        }, _faction.GetLeader()==pActor, false, true);
        pActor.GetIdentity().setLeaderButton = leaderTurnOnButton;
        pActor.GetIdentity().memberCardSpace = cardSpace;
        pActor.GetIdentity()?.setLeaderButton?.SetStatus(_faction.GetLeader()==pActor);
        editActorPart.AddButtonIntoVertLayout("remove_member", action:() => RemoveMember(pActor), showTip:true, size: new Vector2(12, 12), icon:SpriteTextureLoader.getSprite("ui/iconRemove"));
        pActor.GetIdentity()?.memberCardSpace.transform.AddStretchBackground(
            _faction.GetLeader() == pActor ? "FactionFrame_dominate" : "FactionFrame", size: new Vector2(185, 45));
    }
    public void RemoveMember(Actor pActor)
    {
        _faction.RemoveMember(pActor);
        StartCoroutine(ShowMembers());
    }
    public void SetLeader(Actor pActor)
    {
        _faction.SetLeader(pActor);
        pActor?.GetIdentity()?.memberCardSpace.transform.AddStretchBackground("FactionFrame_dominate", size: new Vector2(185, 45));
        pActor?.GetIdentity()?.setLeaderButton?.SetStatus(true);
        InitialTopPart();
        foreach (var actor in _faction.AllMembers)
        {
            var identity = actor.GetIdentity();
            if (actor == pActor) continue;
            if (identity != null)
            {
                identity.memberCardSpace?.transform.AddStretchBackground("FactionFrame", size: new Vector2(185, 45));
                identity.setLeaderButton?.SetStatus(false);
            }
        }
    }
    public void AddPerformance(OfficeIdentity oid, SimpleText performanceText, SimpleText levelText)
    {
        oid.TotalPerformance += 100;
        oid.actor.JudgeOfficeLevel();
        RefreshText(oid.actor, performanceText, levelText);
    }
    public void SubPerformance(OfficeIdentity oid, SimpleText performanceText, SimpleText levelText)
    {
        if (oid.TotalPerformance >= 100)
        {
            oid.TotalPerformance -= 100;
        }
        else
        {
            oid.TotalPerformance = 0;
        }
        oid.actor.JudgeOfficeLevel();
        RefreshText(oid.actor, performanceText, levelText);
    }
    /// <summary>
    /// 刷新角色信息
    /// </summary>
    /// <param name="performance"></param>
    /// <param name="level"></param>
    [Hotfixable]
    public void RefreshText(Actor pActor, SimpleText performance, SimpleText level)
    {
        performance.text.text = $"{LM.Get("label_total_performance")}: {(int)pActor.GetIdentity().TotalPerformance}";
        level.text.text = $"{LM.Get("label_rank")}: {pActor.GetIdentity().GetHonoraryOfficialName()}";
    }
    /// <summary>
    /// 显示和编辑诉求
    /// </summary>
    /// <param name="window"></param>
    [Hotfixable]
    public void ShowClaims(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        var claimSpace = this.BeginVertGroup();
        claimSpace.AddTextIntoVertLayout(LM.Get("label_all_claims"), size: new Vector2(35, 18), hideBackground:true, anchor: TextAnchor.MiddleCenter);
        foreach (var tf in _faction.TemporaryFactions)
        {
            ShowClaim(tf, claimSpace);
        }
        var addSpace = claimSpace.BeginHoriGroup(new Vector2(200, 30), TextAnchor.MiddleCenter);
        addSpace.AddButtonIntoHoriLayout("add_tfaction", "", () =>
        {
            Clear();
            InitialTopPart();
            var selectPart = this.BeginVertGroup(pSize: new Vector2(200, 800), pAlignment: TextAnchor.MiddleCenter);
            foreach (var tf in FactionManager.FactionConfig)
            {
                selectPart.AddTextIntoVertLayout(tf.Key.ToString(), size: new Vector2(35, 18), anchor: TextAnchor.MiddleCenter, hideBackground:true);
                var tFactionContent = selectPart.BeginGridGroup(5, pCellSize: new Vector2(30, 18));
                foreach (var tf2 in tf.Value)
                {
                    tFactionContent.AddButtonIntoGirdLayout(tf2.ToString(), LM.Get(tf2.ToString()), () =>
                    {
                        _faction.TemporaryFactionTypesRecord.Add(tf2);
                        _faction.TemporaryFactions = _faction.ConvertToObjectFromFactionType();
                        ShowClaims();
                    }, size: new Vector2(30, 18));
                }
            }
            _groups.Add(selectPart.gameObject);
        },SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(20, 20));
        addSpace.transform.AddStretchBackground("FactionFrame", new Vector2(200, 30));
        _groups.Add(claimSpace.gameObject);
    }
    [Hotfixable]
    public void ShowClaim(TemporaryFaction pFaction, AutoVertLayoutGroup parent)
    {
        var tfSpace = parent.BeginHoriGroup(new Vector2(200, 40), pSpacing:20);
        var firstPart = tfSpace.BeginVertGroup();
        firstPart.AddTextIntoVertLayout(LM.Get(pFaction.type.ToString()), hideBackground:true);
        firstPart.AddTextIntoVertLayout($"{LM.Get("label_budget")}: {pFaction.Budget}");
        var secondPart = tfSpace.BeginVertGroup(pSpacing:-5);
        var hideButton = secondPart.transform.AddNormalOptionIntoHori(this.BeginHoriGroup(), "hide_tfaction", () =>
        {
            pFaction.Hide = !pFaction.Hide;
            pFaction.hideButton?.SetStatus(pFaction.Hide);
        }, pFaction.Hide, size: new Vector2(12, 12), isOption:true);
        var activeButton = secondPart.transform.AddNormalOptionIntoHori(this.BeginHoriGroup(), "active_tfaction", () =>
        {
            pFaction.Active = !pFaction.Active;
            pFaction.activeButton?.SetStatus(pFaction.Active);
        }, pFaction.Active, size: new Vector2(12, 12), isOption:false, hasTitle:false);
        pFaction.hideButton =  hideButton;
        pFaction.activeButton =  activeButton;
        var thirdPart = tfSpace.BeginVertGroup(new Vector2(30, 30));
        var showButton = thirdPart.transform.AddNormalOptionIntoHori(this.BeginHoriGroup(), "show_tfaction_as_plot", () =>
        {
            pFaction.ShowAsPlot = !pFaction.ShowAsPlot;
            if ((_kingdom?.king?.plot?.name??"") == pFaction.type.ToString())
            {
                _kingdom?.king?.plot?.setAlive(false);
            }
            if (pFaction.IsStarted())
            {
                pFaction.End();
            }
            pFaction.showButton?.SetStatus(pFaction.ShowAsPlot);
        }, pFaction.ShowAsPlot, size: new Vector2(12, 12), isOption:true);
        pFaction.showButton =  showButton;
        
        var fourthPart = tfSpace.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        fourthPart.AddButtonIntoVertLayout("remove_tfaction", "", () =>
        {
            _faction.TemporaryFactions.Remove(pFaction);
            _faction.TemporaryFactionTypesRecord.Remove(pFaction.type);
            ShowClaims();
        }, icon: SpriteTextureLoader.getSprite("ui/iconRemove"), size: new Vector2(13, 13));
        tfSpace.transform.AddStretchBackground("FactionFrame", new Vector2(200, 42));
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
