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
public partial class FactionDetailWindow : AbstractWideWindow<FactionDetailWindow>
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
        // 党务：党产、民调、党内、党际(用户 2026-11-10 "政党总感觉是空壳"，见 PartyAffairsPanel)
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "show_party_affairs"))
        {
            var affairsTab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            affairsTab.Setup("show_party_affairs", ScrollWindowComponent, action: ShowPartyAffairs,
                sprite: SpriteTextureLoader.getSprite("TabPartyAffairs"));
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
        row.AddTextIntoHoriLayout(LM.Get(IdeologyFamilies.HasExplicitIdeology(_faction)
                ? "faction_ideology_label" : "faction_stance_label").ColorString("#8FA0A8"), true, TextAnchor.MiddleLeft,
            new Vector2(20f, 12f)).UseFixedFontSize(6, HorizontalWrapMode.Overflow);
        string hex = WideKit.Hex(UIHelper.IdeologyColor(ideology));
        // 古代学说按本朝堂的文化线叫(西方是经院正统、骑士派……见 CultureDoctrines)
        string culture = FactionEditor.CultureOf(_kingdom);
        string stance = IdeologyFamilies.HasExplicitIdeology(_faction)
            ? PartySystem.GetIdeologyName(ideology, culture) : IdeologyFamilies.FactionLeaning(_faction, culture);
        row.AddTextIntoHoriLayout(stance.ColorString(hex), true, TextAnchor.MiddleLeft,
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

    // 理念选择页：本朝堂能选的理念各一格(古代：本文化线的学说，按科技出现；政党时代：已传入的近代理念)，
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
        string culture = FactionEditor.CultureOf(_kingdom);
        string name = PartySystem.GetIdeologyName(ideology, culture);
        string hex = current ? "#F3C34A" : ok ? WideKit.Hex(UIHelper.IdeologyColor(ideology)) : "#6E746A";
        cell.AddTextIntoVertLayout((current ? $"[{name}]" : name).ColorString(hex), true, TextAnchor.MiddleCenter,
            new Vector2(width - 2f, 14f)).UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        UIHelper.AttachTextTooltip(cell.gameObject, $"faction_pick_{ideology}", name,
            ok ? DoctrineTip(ideology, culture) : LM.Get(reason));
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

    // 悬停一格理念：本文化的说法("属骑士派")与说明
    internal static string DoctrineTip(PartyIdeology ideology, string culture)
    {
        string leaning = IdeologyFamilies.FactionLeaning(ideology, culture);
        string description = IdeologyFamilies.IsAncient(ideology) ? CultureDoctrines.Description(ideology, culture) : "";
        return string.IsNullOrEmpty(description) ? leaning : $"{leaning}\n{description}";
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
