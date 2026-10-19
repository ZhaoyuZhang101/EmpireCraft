using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
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

// 宽窗口(480×380)横屏骨架：左栏派系档案(名称/领袖/规模/所需特质/操作)固定不随滚动，右侧是成员、诉求、制度立场、阶层基本盘
public class FactionDetailWindow : AbstractWideWindow<FactionDetailWindow>
{
    private AutoVertLayoutGroup _root;
    private AutoVertLayoutGroup _rail;
    private ScrollWindow ScrollWindowComponent => GetComponent<ScrollWindow>();
    private const float ContentWidth = UIHelper.PageMainWidth;
    private FixedFaction _faction;
    private Kingdom _kingdom;
    private TextInput _factionNameInput;
    private List<GameObject> _groups = new List<GameObject>();
    private AutoVertLayoutGroup topPart;
    private bool saveIntoCurrentFaction = false;
    private AdvancedButton SaveOption = null;
    protected override void Init()
    {
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        _root = UIHelper.SetupWideWindowContentRoot(ContentTransform, 3f, new RectOffset(3, 3, 3, 3));
        _rail = UIHelper.CreateWideWindowRail(BackgroundTransform, UIHelper.RailWidth, UIHelper.RailInset);
        _factionNameInput = Instantiate(TextInput.Prefab, this.transform);
        // 宽窗口脚本挂在窗口根上：输入框挂窗口根并脱离布局、锚点居中(同帝国窗口)
        var inputElement = _factionNameInput.gameObject.GetComponent<LayoutElement>() ?? _factionNameInput.gameObject.AddComponent<LayoutElement>();
        inputElement.ignoreLayout = true;
        var inputRect = (RectTransform)_factionNameInput.transform;
        inputRect.anchorMin = inputRect.anchorMax = inputRect.pivot = new Vector2(0.5f, 0.5f);
        _factionNameInput.Setup("", ChangeFactionName);
    }
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        UIHelper.FixWideWindowScrollArea(BackgroundTransform);
        StartCoroutine(UIHelper.StabilizeWideWindowScrollArea(BackgroundTransform));
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
    // 左栏派系档案：名称与理念 → 领袖 → 规模 → 所需特质(可增删) → 恢复诉求 / 存档
    [Hotfixable]
    public void InitialTopPart()
    {
        _faction?.Update();
        UIHelper.ClearRail(_rail);
        if (_rail == null || _faction == null) return;
        var identity = _rail.BeginRailCard("FactionFrame_dominate");
        topPart = identity;
        identity.AddRailText(_faction.Name.ColorString("#F3C34A"), 10, 14f);
        PartyIdeology ideology = PartySystem.LeaningOf(_faction);
        AddIdeologyRow(identity, ideology);
        AddNamingRow(identity);
        var leaderRow = identity.BeginHoriGroup(new Vector2(UIHelper.RailWidth - 8f, 32f), TextAnchor.MiddleLeft, 3,
            new RectOffset(0, 0, 0, 0));
        leaderRow.transform.localPosition = Vector3.zero;
        Actor leader = _faction.GetLeader();
        leaderRow.AddActorViewIntoHoriLayout(leader);
        var leaderText = leaderRow.BeginVertGroup(new Vector2(UIHelper.RailWidth - 42f, 28f), pSpacing: 0,
            pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(0, 0, 0, 0));
        leaderText.transform.localPosition = Vector3.zero;
        leaderText.AddTextIntoVertLayout(LM.Get("faction_leader").ColorString("#FFCC00"), true, TextAnchor.MiddleLeft,
            new Vector2(UIHelper.RailWidth - 42f, 10f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        leaderText.AddTextIntoVertLayout((leader?.getName() ?? LM.Get("label_none")).ColorString("#F2EEE2"), true,
            TextAnchor.MiddleLeft, new Vector2(UIHelper.RailWidth - 42f, 12f)).UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        var actions = identity.BeginHoriGroup(new Vector2(UIHelper.RailWidth - 8f, 16f), TextAnchor.MiddleCenter, 6,
            new RectOffset(0, 0, 0, 0));
        actions.AddButtonIntoHoriLayout("recover_tfaction", icon: SpriteTextureLoader.getSprite("ui/changeOfficer"), size: new Vector2(15, 15), showTip:true, action:
            () =>
            {
                _faction.TemporaryFactionTypesRecord = _faction.IsParty && _faction.TemporaryFactionTypesRecord != null
                    ? _faction.TemporaryFactionTypesRecord
                    : new List<TemporaryFactionType>(_faction.TemporaryFactionTypes ?? new List<TemporaryFactionType>());
                _faction.TemporaryFactions = _faction.ConvertToObjectFromFactionType();
                ShowClaims();
            });
        actions.AddButtonIntoHoriLayout("save_faction", icon:SpriteTextureLoader.getSprite("ui/icons/iconSaveLocal"), size:new Vector2(15, 15), showTip:true, action:
            ShowFactionSaveSpace);

        var stats = _rail.BeginRailCard();
        float rowWidth = UIHelper.RailWidth - 8f;
        stats.AddRailRow(rowWidth, LM.Get("label_member_count"), _faction.Count.ToString(), "#F2EEE2", 0);
        stats.AddRailRow(rowWidth, LM.Get("label_central_ratio"), $"{_faction.CentralRatio}%", "#F3C34A", 1);
        stats.AddRailRow(rowWidth, LM.Get("faction_rail_class_power"), $"{_faction.ClassPower:0.#}%", "#7FD8EA", 2);

        var traits = _rail.BeginRailCard();
        traits.AddRailText(LM.Get("faction_required_traits").ColorString("#8FA0A8"), 6, 9f);
        var traitsPart = traits.BeginHoriGroup(pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(0, 0, 0, 0));
        InitTraitPart(traitsPart);
        BringNameInputToFront();
    }

    /// <summary>
    /// 显示派系存储配置
    /// </summary>
    public void ShowFactionSaveSpace()
    {
        Clear();
        InitialTopPart();
        var factionSavePart = _root.BeginVertGroup(pAlignment:  TextAnchor.MiddleCenter);
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
        var addOrRecoverSpace = parent.BeginVertGroup(pSize: new Vector2(14, 30), TextAnchor.MiddleCenter);
            
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
            var traitEdit = parent.BeginVertGroup(pSize:new Vector2(20, 30), pSpacing: -5);
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
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "show_institution_stance"))
        {
            var institutionTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            institutionTab.Setup("show_institution_stance", ScrollWindowComponent, action: ShowInstitutionStance,
                sprite: SpriteTextureLoader.getSprite("ChineseCrown"));
        }
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "show_faction_class_base"))
        {
            var classTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            classTab.Setup("show_faction_class_base", ScrollWindowComponent, action: ShowClassBase,
                sprite: SpriteTextureLoader.getSprite("ui/icons/actor_traits/iconJingshi"));
        }
    }

    [Hotfixable]
    public void ShowClassBase(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        Empire empire = _faction?.Empire ?? _kingdom?.GetEmpire();
        if (empire == null || _faction == null) return;
        List<FixedFaction> factions = empire.CoreKingdom?.GetRegime()?.GetPlayerFactions() ?? new List<FixedFaction>();
        FactionClassSystem.EnsureEmpireProfiles(empire, factions);
        Dictionary<SocialClass, float> population = InstitutionSystem.BuildClassShares(empire);
        _faction.ClassPower = population.Sum(pair => pair.Value * _faction.ClassSupport[pair.Key]);

        var content = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        content.AddTextIntoVertLayout(LM.Get("faction_class_foundation_title").ColorString("#7FD8EA"), true,
            TextAnchor.MiddleCenter, new Vector2(ContentWidth, 15));
        content.AddTextIntoVertLayout(
            string.Format(LM.Get("faction_class_power_summary"), _faction.ClassPower, _faction.CentralRatio), true,
            TextAnchor.MiddleCenter, new Vector2(ContentWidth, 14));
        content.AddTextIntoVertLayout(LM.Get("faction_class_model_hint").ColorString("#8FA0A8"), true,
            TextAnchor.MiddleCenter, new Vector2(ContentWidth, 28));

        List<SocialClass> classes = Enum.GetValues(typeof(SocialClass)).Cast<SocialClass>()
            .OrderByDescending(socialClass => population.TryGetValue(socialClass, out float share)
                ? share * _faction.ClassSupport[socialClass]
                : 0f)
            .ToList();
        foreach (SocialClass socialClass in classes)
        {
            float populationShare = population.TryGetValue(socialClass, out float share) ? share * 100f : 0f;
            float affinity = _faction.ClassAffinities[socialClass];
            float favor = _faction.ClassFavor[socialClass];
            float support = _faction.ClassSupport[socialClass];
            float contribution = populationShare * support / 100f;
            string affinityText = GetAffinityText(affinity);
            string affinityColor = affinity >= 20f ? "#65D66E" : affinity <= -20f ? "#D98C8C" : "#B8C6CC";
            string favorColor = favor >= 60f ? "#65D66E" : favor <= 40f ? "#D98C8C" : "#F3C34A";

            var row = content.BeginVertGroup(pSpacing: 0, pAlignment: TextAnchor.MiddleCenter);
            row.AddTextIntoVertLayout(
                $"{LM.Get($"class_{socialClass}").ColorString("#F3C34A")}  " +
                $"[{LM.Get(FactionClassSystem.GetStratumKey(socialClass))}]  " +
                string.Format(LM.Get("faction_class_population"), populationShare), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 12));
            row.AddTextIntoVertLayout(
                string.Format(LM.Get("faction_class_attitude_line"),
                    affinityText.ColorString(affinityColor), favor.ToString("0").ColorString(favorColor)), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 12));
            row.AddTextIntoVertLayout(
                string.Format(LM.Get("faction_class_support_line"), support, contribution), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 12));
            row.AddTextIntoVertLayout(
                string.Format(LM.Get("faction_class_recent_source"),
                    FactionClassSystem.GetFavorSourceText(_faction, socialClass)).ColorString("#8FA0A8"), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 16));
            List<string> claimOptions = (_faction.TemporaryFactionTypesRecord ?? new List<TemporaryFactionType>())
                .Select(claim => (claim, effect: FactionClassSystem.GetClaimClassEffect(claim, socialClass)))
                .Where(item => item.effect > 0f).OrderByDescending(item => item.effect).Take(2)
                .Select(item => $"{LM.Get(item.claim.ToString())} +{item.effect:0}").ToList();
            List<string> institutionOptions = InstitutionSystem.GetFactionInstitutionStance(
                    InstitutionSystem.GetPrimaryCulture(empire), _faction.Type).supports
                .Where(node => !InstitutionSystem.IsEnacted(empire, node.id) &&
                               node.politics.support_classes.TryGetValue(socialClass, out float weight) && weight > 0f)
                .OrderByDescending(node => node.politics.support_classes[socialClass]).Take(2)
                .Select(InstitutionSystem.GetNodeName).ToList();
            string options = string.Join(" · ", claimOptions.Concat(institutionOptions));
            row.AddTextIntoVertLayout(
                string.Format(LM.Get("faction_class_courting_options"), string.IsNullOrWhiteSpace(options)
                    ? LM.Get("faction_class_no_courting_option")
                    : options).ColorString("#B8C6CC"), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 22));
        }
        _groups.Add(content.gameObject);
    }

    private static string GetAffinityText(float affinity)
    {
        if (affinity >= 60f) return LM.Get("faction_class_affinity_core");
        if (affinity >= 20f) return LM.Get("faction_class_affinity_friendly");
        if (affinity > -20f) return LM.Get("faction_class_affinity_neutral");
        if (affinity > -60f) return LM.Get("faction_class_affinity_distant");
        return LM.Get("faction_class_affinity_hostile");
    }

    [Hotfixable]
    public void ShowInstitutionStance(WindowMetaTab window = null)
    {
        Clear();
        InitialTopPart();
        Empire empire = _faction?.Empire ?? _kingdom?.GetEmpire();
        if (empire == null || _faction == null) return;

        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        (List<InstitutionNodeConfig> supports, List<InstitutionNodeConfig> opposes) =
            InstitutionSystem.GetFactionInstitutionStance(culture, _faction.Type);
        Dictionary<string, InstitutionNodeView> views = InstitutionSystem.BuildLineView(empire)
            .ToDictionary(view => view.Node.id, view => view);

        var content = _root.BeginVertGroup(pSpacing: 1, pAlignment: TextAnchor.UpperCenter);
        content.AddTextIntoVertLayout(LM.Get("institution_faction_stance_title").ColorString("#7FD8EA"), true,
            TextAnchor.MiddleCenter, new Vector2(ContentWidth, 16));

        InstitutionReformState active = empire.data.institution_state?.active_reform;
        if (active != null)
        {
            InstitutionNodeConfig activeNode = InstitutionDefinitionRegistry.Get(active.node_id);
            FixedFaction sponsor = InstitutionSystem.GetReformSponsor(empire, active);
            string role = sponsor == _faction
                ? LM.Get("institution_faction_role_sponsor")
                : activeNode?.politics.support_factions.ContainsKey(_faction.Type) == true
                    ? LM.Get("institution_faction_role_supporter")
                    : activeNode?.politics.oppose_factions.ContainsKey(_faction.Type) == true
                        ? LM.Get("institution_faction_role_opponent")
                        : LM.Get("institution_faction_role_neutral");
            content.AddTextIntoVertLayout(string.Format(LM.Get("institution_faction_active_reform"),
                    InstitutionSystem.GetNodeName(empire, activeNode), role, active.progress, active.radicalism), true,
                TextAnchor.MiddleCenter, new Vector2(ContentWidth, 28));
        }

        AddInstitutionStanceGroup(content, LM.Get("institution_faction_supports"), supports, views, "#65D66E");
        AddInstitutionStanceGroup(content, LM.Get("institution_faction_opposes"), opposes, views, "#D98C8C");
        _groups.Add(content.gameObject);
    }

    private static void AddInstitutionStanceGroup(AutoVertLayoutGroup parent, string title,
        IEnumerable<InstitutionNodeConfig> nodes, Dictionary<string, InstitutionNodeView> views, string color)
    {
        List<InstitutionNodeConfig> list = nodes?.ToList() ?? new List<InstitutionNodeConfig>();
        parent.AddTextIntoVertLayout(title.ColorString(color), true, TextAnchor.MiddleCenter,
            new Vector2(ContentWidth, 14));
        if (list.Count == 0)
        {
            parent.AddTextIntoVertLayout(LM.Get("institution_faction_no_stance").ColorString("#8FA0A8"), true,
                TextAnchor.MiddleCenter, new Vector2(ContentWidth, 14));
            return;
        }

        foreach (InstitutionNodeConfig node in list)
        {
            InstitutionNodeStatus status = views.TryGetValue(node.id, out InstitutionNodeView view)
                ? view.Status
                : InstitutionNodeStatus.Locked;
            string statusText = status switch
            {
                InstitutionNodeStatus.Enacted => LM.Get("institution_status_enacted"),
                InstitutionNodeStatus.Reforming => LM.Get("institution_status_reforming"),
                InstitutionNodeStatus.Available => LM.Get("institution_status_available"),
                InstitutionNodeStatus.Forceable => LM.Get("institution_status_force_available"),
                InstitutionNodeStatus.Superseded => LM.Get("institution_status_superseded"),
                _ => LM.Get("institution_status_locked")
            };
            parent.AddTextIntoVertLayout(
                $"{InstitutionSystem.GetNodeName(node, view?.Culture)}  [{statusText}]".ColorString(color), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 10f, 14));
        }
    }

    public void Clear()
    {
        UIHelper.ClearRail(_rail);
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
        var content = _root.BeginVertGroup();
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

        var buttonSpace = parent.BeginVertGroup(pSize: new Vector2(ContentWidth - 15f, 55), TextAnchor.MiddleCenter);
        buttonSpace.AddButtonIntoVertLayout("add_member", "", () =>
        {
            if (!_faction.Empire.isRekt())
            {
                ScrollWindow.showWindow(nameof(AddFactionMemberWindow));
            }
            ActionLibrary.showWhisperTip("inactivated_faction");
        }, icon: SpriteTextureLoader.getSprite("ui/setOfficer"), size:new Vector2(25, 25), hideBackground:false);
        buttonSpace.transform.transform.AddStretchBackground("FactionFrame", size: new Vector2(ContentWidth - 15f, 45));
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
            _faction.GetLeader() == pActor ? "FactionFrame_dominate" : "FactionFrame", size: new Vector2(ContentWidth - 15f, 45));
    }
    public void RemoveMember(Actor pActor)
    {
        _faction.RemoveMember(pActor);
        StartCoroutine(ShowMembers());
    }
    public void SetLeader(Actor pActor)
    {
        _faction.SetLeader(pActor);
        pActor?.GetIdentity()?.memberCardSpace.transform.AddStretchBackground("FactionFrame_dominate", size: new Vector2(ContentWidth - 15f, 45));
        pActor?.GetIdentity()?.setLeaderButton?.SetStatus(true);
        InitialTopPart();
        foreach (var actor in _faction.AllMembers)
        {
            var identity = actor.GetIdentity();
            if (actor == pActor) continue;
            if (identity != null)
            {
                identity.memberCardSpace?.transform.AddStretchBackground("FactionFrame", size: new Vector2(ContentWidth - 15f, 45));
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
        var claimSpace = _root.BeginVertGroup();
        string agendaTitle = _faction.IsParty
            ? string.Format(LM.Get("agenda_party_title"), PartySystem.GetIdeologyName(_faction.Ideology))
            : LM.Get("label_all_claims");
        claimSpace.AddTextIntoVertLayout(agendaTitle, size: new Vector2(ContentWidth, 18), hideBackground:true,
            anchor: TextAnchor.MiddleCenter);
        ClaimAgendaContext context = ClaimAgendaSystem.BuildContext(_faction?.Empire ?? _kingdom?.GetEmpire());
        var entries = _faction.TemporaryFactions.Where(tf => tf != null)
            .Select(tf => (claim: tf, view: ClaimAgendaSystem.Evaluate(context, _faction, tf)))
            .OrderByDescending(entry => entry.view.Score).ToList();
        // 议题分两组：本派独有的议题，与各派都能上奏的公共议题(赋税、战和、取士)
        foreach ((string key, bool isPublic) in new[] { ("court_unique_issues", false), ("court_public_issues", true) })
        {
            var group = entries.Where(entry => CourtIssues.IsPublic(_faction, entry.claim.type) == isPublic).ToList();
            if (group.Count == 0) continue;
            claimSpace.AddTextIntoVertLayout(LM.Get(key).ColorString("#F3C34A"), size: new Vector2(ContentWidth, 14),
                hideBackground: true, anchor: TextAnchor.MiddleLeft);
            foreach (var entry in group) ShowClaim(entry.claim, entry.view, claimSpace, context);
        }
        var addSpace = claimSpace.BeginHoriGroup(new Vector2(ContentWidth, 30), TextAnchor.MiddleCenter);
        addSpace.AddButtonIntoHoriLayout("add_tfaction", "", () =>
        {
            Clear();
            InitialTopPart();
            var selectPart = _root.BeginVertGroup(pSize: new Vector2(ContentWidth, 800), pAlignment: TextAnchor.MiddleCenter);
            foreach (var tf in FactionManager.FactionConfig)
            {
                selectPart.AddTextIntoVertLayout(tf.Key.ToString(), size: new Vector2(35, 18), anchor: TextAnchor.MiddleCenter, hideBackground:true);
                var tFactionContent = selectPart.BeginGridGroup(5, pCellSize: new Vector2(58, 18));
                foreach (var tf2 in tf.Value)
                {
                    tFactionContent.AddButtonIntoGirdLayout(tf2.ToString(), TranslateHelper.GetTemporaryFactionClaimText(tf2), () =>
                    {
                        _faction.TemporaryFactionTypesRecord.Add(tf2);
                        _faction.TemporaryFactions = _faction.ConvertToObjectFromFactionType();
                        ShowClaims();
                    }, size: new Vector2(58, 18));
                }
            }
            _groups.Add(selectPart.gameObject);
        },SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(20, 20));
        addSpace.transform.AddStretchBackground("FactionFrame", new Vector2(ContentWidth, 30));
        _groups.Add(claimSpace.gameObject);
    }
    [Hotfixable]
    public void ShowClaim(TemporaryFaction pFaction, ClaimAgendaView agenda, AutoVertLayoutGroup parent,
        ClaimAgendaContext context)
    {
        var card = parent.BeginVertGroup(pSpacing: 0, pAlignment: TextAnchor.MiddleCenter);
        var tfSpace = card.BeginHoriGroup(new Vector2(ContentWidth, 36), pSpacing:20);
        var firstPart = tfSpace.BeginVertGroup();
        firstPart.AddTextIntoVertLayout(TranslateHelper.GetTemporaryFactionClaimText(pFaction.type), hideBackground:true);
        firstPart.AddTextIntoVertLayout($"{LM.Get("label_budget")}: {pFaction.Budget}");
        var secondPart = tfSpace.BeginVertGroup(pSpacing:-5);
        var hideButton = secondPart.transform.AddNormalOptionIntoHori(_root.BeginHoriGroup(), "hide_tfaction", () =>
        {
            pFaction.Hide = !pFaction.Hide;
            pFaction.hideButton?.SetStatus(pFaction.Hide);
        }, pFaction.Hide, size: new Vector2(12, 12), isOption:true);
        var localPushButton = secondPart.transform.AddNormalOptionIntoHori(_root.BeginHoriGroup(), "localpush_tfaction", () =>
        {
            pFaction.canBePushByLocal = !pFaction.canBePushByLocal;
            pFaction.localPushButton?.SetStatus(pFaction.canBePushByLocal);
        }, pFaction.canBePushByLocal, size: new Vector2(12, 12), isOption:false, hasTitle:false);
        var activeButton = secondPart.transform.AddNormalOptionIntoHori(_root.BeginHoriGroup(), "active_tfaction", () =>
        {
            pFaction.Active = !pFaction.Active;
            pFaction.activeButton?.SetStatus(pFaction.Active);
        }, pFaction.Active, size: new Vector2(12, 12), isOption:false, hasTitle:false);
        pFaction.hideButton =  hideButton;
        pFaction.activeButton =  activeButton;
        pFaction.localPushButton = localPushButton;
        if (!_faction.IsParty)
        {
            var thirdPart = tfSpace.BeginVertGroup(new Vector2(30, 30));
            var showButton = thirdPart.transform.AddNormalOptionIntoHori(_root.BeginHoriGroup(), "show_tfaction_as_plot", () =>
            {
                pFaction.ShowAsPlot = !pFaction.ShowAsPlot;
                if ((_kingdom?.king?.plot?.name??"") == pFaction.type.ToString())
                    _kingdom?.king?.plot?.setAlive(false);
                if (pFaction.IsStarted()) pFaction.End();
                pFaction.showButton?.SetStatus(pFaction.ShowAsPlot);
            }, pFaction.ShowAsPlot, size: new Vector2(12, 12), isOption:true);
            pFaction.showButton = showButton;
        }
        
        var fourthPart = tfSpace.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        fourthPart.AddButtonIntoVertLayout("remove_tfaction", "", () =>
        {
            _faction.TemporaryFactions.Remove(pFaction);
            _faction.TemporaryFactionTypesRecord.Remove(pFaction.type);
            ShowClaims();
        }, icon: SpriteTextureLoader.getSprite("ui/iconRemove"), size: new Vector2(13, 13));
        string statusKey = !pFaction.Active ? "agenda_status_inactive" :
            pFaction.IsStarted() ? "agenda_status_running" :
            pFaction.CountDown > 0 ? "agenda_status_cooldown" :
            !agenda.CanPropose ? "agenda_status_blocked" : "agenda_status_eligible";
        string status = LM.Get(statusKey);
        if (!agenda.CanPropose) status += $": {(LM.Has(agenda.Blocker) ? LM.Get(agenda.Blocker) : agenda.Blocker)}";
        card.AddTextIntoVertLayout(status.ColorString(agenda.CanPropose ? "#65D6C4" : "#D98C8C"), true,
            TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 14));
        card.AddTextIntoVertLayout(string.Format(LM.Get("agenda_support_line"), agenda.Support,
                agenda.Opposition), true,
            TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 14));
        if (!_faction.IsParty)
            card.AddTextIntoVertLayout(string.Format(LM.Get("court_stance_line"),
                    CourtPanel.StanceMark(CourtIssues.StanceLevel(_faction, pFaction.type)),
                    CourtIssues.Stance(_faction, pFaction.type).ToString("0")), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 14));
        string technology = string.IsNullOrEmpty(agenda.TechnologyName) ? LM.Get("agenda_no_technology") :
            agenda.TechnologyName;
        if (_faction.IsParty)
        {
            string seats = context?.ParliamentSeats > 0 ? $"{agenda.LegislativeShare:0}%" :
                LM.Get("agenda_no_parliament");
            card.AddTextIntoVertLayout(string.Format(LM.Get("agenda_political_line"), agenda.PopulationShare,
                    seats, agenda.Urgency), true, TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 14));
            card.AddTextIntoVertLayout(string.Format(LM.Get("agenda_technology_line"), technology,
                    agenda.IdeologyStage, pFaction.progress, pFaction.progressMax), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 14));
        }
        else
        {
            card.AddTextIntoVertLayout(string.Format(LM.Get("agenda_faction_progress_line"), technology,
                    pFaction.progress, pFaction.progressMax), true, TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 14));
        }
        if (pFaction.type == TemporaryFactionType.提高福利 && context?.Empire?.data?.constitutional_economy != null)
        {
            var state = context.Empire.data.constitutional_economy;
            card.AddTextIntoVertLayout(string.Format(LM.Get("agenda_welfare_line"), state.welfare_level,
                    ConstitutionalEconomySystem.GetWelfareAnnualCost(context.Empire, state.welfare_level),
                    LM.Get(state.welfare_funded ? "agenda_welfare_funded" : "agenda_welfare_unfunded")),
                true, TextAnchor.MiddleLeft, new Vector2(ContentWidth - 8f, 14));
        }
        card.transform.AddStretchBackground("FactionFrame", new Vector2(ContentWidth,
            (_faction.IsParty ? 94 : 80) + (pFaction.type == TemporaryFactionType.提高福利 ? 14 : 0)));
    }
    
    // 改名(窗口标题就是输入框，同帝国窗口)：空的不改；改了左栏档案随即换成新名字
    public void ChangeFactionName(string newName)
    {
        string name = newName?.Trim();
        if (_faction == null || string.IsNullOrEmpty(name) || name == _faction.Name) return;
        // 手改了名字：起名方式记为自定，以后不再自动改(见 FactionEditor)
        FactionEditor.Rename(_faction, name);
        InitialTopPart();
        BringNameInputToFront();
        UIHelper.RefreshFactionSpace(_kingdom);
    }

    // 左栏"理念 · 更改"(用户 2026-10-19 转玩家反馈"自定义派系为啥只有中间派"，看过示意图定"照这个做")：
    // 理念名按理念上色，点"更改"右侧换成理念选择页
    private void AddIdeologyRow(AutoVertLayoutGroup card, PartyIdeology ideology)
    {
        float inner = UIHelper.RailWidth - 8f;
        var row = card.BeginHoriGroup(new Vector2(inner, 14f), TextAnchor.MiddleLeft, 3, new RectOffset(3, 3, 0, 0));
        UIHelper.AddInsetBackground(row, new Vector2(inner, 14f));
        row.AddTextIntoHoriLayout(LM.Get("faction_ideology_label").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
            new Vector2(20f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        string hex = WideKit.Hex(UIHelper.IdeologyColor(ideology));
        row.AddTextIntoHoriLayout(PartySystem.GetIdeologyName(ideology).ColorString(hex), true, TextAnchor.MiddleLeft,
            new Vector2(inner - 62f, 12f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        row.AddButtonIntoHoriLayout("faction_change_ideology", LM.Get("faction_change_ideology"), ShowIdeologyPicker,
            size: new Vector2(30f, 12f));
    }

    // 左栏起名方式：自定 / 默认名 / 魁首之姓(用户 2026-10-19："派系名称的设置允许派系自动用魁首名字命名比如严党这种，也可以允许使用默认名称")
    private void AddNamingRow(AutoVertLayoutGroup card)
    {
        float inner = UIHelper.RailWidth - 8f;
        var row = card.BeginHoriGroup(new Vector2(inner, 14f), TextAnchor.MiddleLeft, 2, new RectOffset(0, 0, 0, 0));
        row.AddTextIntoHoriLayout(LM.Get("faction_naming_label").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
            new Vector2(20f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        float width = (inner - 20f - 6f) / 3f;
        foreach ((int mode, string key) in new[]
                 {
                     (FactionEditor.NameCustom, "faction_naming_custom"), (FactionEditor.NameDefault, "faction_naming_default"),
                     (FactionEditor.NameLeader, "faction_naming_leader")
                 })
        {
            int target = mode;
            string label = LM.Get(key);
            row.AddButtonIntoHoriLayout(key, _faction.NameMode == mode ? label.ColorString("#F3C34A") : label, () =>
            {
                FactionEditor.SetNameMode(_kingdom, _faction, target);
                if (target == FactionEditor.NameLeader && FactionEditor.LeaderName(_faction) == null)
                    WorldTip.showNow(LM.Get("faction_naming_no_leader"), false, "top", 2.5f);
                AfterFactionChanged();
            }, size: new Vector2(width, 12f), showTip: true);
        }
    }

    // 理念选择页：本朝堂能选的理念各一格(古代：诸子百家只有华夏有、按科技出现；政党时代：已传入的近代理念)，
    // 现在的标金框；不能选的置灰、悬停看原因。改了按新理念重设诉求与阶层亲疏(见 FactionEditor.ChangeIdeology)
    public void ShowIdeologyPicker()
    {
        Clear();
        InitialTopPart();
        var content = _root.BeginVertGroup(pSpacing: 2, pAlignment: TextAnchor.UpperCenter);
        _groups.Add(content.gameObject);
        content.AddPageHeader(ContentWidth, LM.Get("faction_ideology_pick"), LM.Get("faction_ideology_pick_note"));
        var options = FactionEditor.Options(_kingdom, _faction);
        bool modern = options.Count > 0 && !IdeologyFamilies.IsAncient(options[0].ideology);
        content.AddSectionHeader(ContentWidth, LM.Get(modern ? "faction_ideology_modern" : "faction_ideology_ancient"), localize: false);
        const int columns = 5;
        float tile = (ContentWidth - (columns - 1) * UIHelper.CardGap) / columns;
        PartyIdeology current = PartySystem.LeaningOf(_faction);
        for (int start = 0; start < options.Count; start += columns)
        {
            var row = content.BeginColumns(ContentWidth);
            for (int i = start; i < Mathf.Min(options.Count, start + columns); i++)
            {
                (PartyIdeology ideology, string reason) = options[i];
                AddIdeologyTile(row, tile, ideology, reason, ideology == current);
            }
        }
        if (!modern)
            content.AddTextIntoVertLayout(LM.Get("faction_ideology_modern_later").ColorString("#8FA0A8"), true,
                TextAnchor.MiddleLeft, new Vector2(ContentWidth, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        BringNameInputToFront();
    }

    private void AddIdeologyTile(AutoHoriLayoutGroup row, float width, PartyIdeology ideology, string reason, bool current)
    {
        bool ok = reason == null;
        var cell = row.BeginVertGroup(new Vector2(width, 18f), pSpacing: 0, pAlignment: TextAnchor.MiddleCenter,
            pPadding: new RectOffset(0, 0, 1, 1));
        UIHelper.FixPanelWidth(cell.transform, width);
        UIHelper.AddInsetBackground(cell, new Vector2(width, 18f));
        string name = PartySystem.GetIdeologyName(ideology);
        string hex = current ? "#F3C34A" : ok ? WideKit.Hex(UIHelper.IdeologyColor(ideology)) : "#6E746A";
        cell.AddTextIntoVertLayout((current ? $"[{name}]" : name).ColorString(hex), true, TextAnchor.MiddleCenter,
            new Vector2(width - 2f, 14f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        UIHelper.AttachTextTooltip(cell.gameObject, $"faction_pick_{ideology}", name,
            ok ? IdeologyFamilies.FactionLeaning(ideology) : LM.Get(reason));
        if (!ok || current) return;
        UIHelper.MakeClickable(cell.gameObject, () =>
        {
            if (!FactionEditor.ChangeIdeology(_kingdom, _faction, ideology, out string why))
            {
                if (why != null) WorldTip.showNow(LM.Get(why), false, "top", 2.5f);
                return;
            }
            WorldTip.showNow(string.Format(LM.Get("faction_ideology_changed"), _faction.Name, name), false, "top", 2.5f);
            AfterFactionChanged();
            ShowClaims();
        });
    }

    // 理念、名字改了：左栏、标题改名框、帝国主页的派系格局跟着换
    private void AfterFactionChanged()
    {
        InitialTopPart();
        if (_factionNameInput != null && !_factionNameInput.input.isFocused) _factionNameInput.input.text = _faction.Name;
        BringNameInputToFront();
        UIHelper.RefreshFactionSpace(_kingdom);
    }
    
    public void InitialTextInput()
    {
        _faction = ConfigData.CURRENT_SELECTED_FACTION;
        var text = _faction.Name;
        this.transform.GenerateTextInput(offset:new Vector2(0, 218), default_text:text, input:_factionNameInput);
        BringNameInputToFront();
    }

    // 改名框挂在窗口根上，之后画的左栏、内容会盖在它上面、点不到(玩家反馈"派系名称现在改不了了")：
    // 每次画完都把它挪到最上层(同帝国窗口 SyncNameInputText)
    private void BringNameInputToFront()
    {
        if (_factionNameInput == null) return;
        _factionNameInput.gameObject.SetActive(true);
        _factionNameInput.transform.SetAsLastSibling();
    }
}
