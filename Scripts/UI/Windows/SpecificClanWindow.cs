using System.Collections;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.System;
using NeoModLoader.api.attributes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class SpecificClanWindow : AutoLayoutWindow<SpecificClanWindow>
{
    Actor _actor;
    private string _lastSearchContent = "";
    private readonly Dictionary<string, AutoVertLayoutGroup> _groups = new ();
    private readonly Dictionary<string, AutoHoriLayoutGroup> _hGroups = new ();
    SpecificClan _sc;
    PersonalClanIdentity _identity;
    private TextInput _clanInput;
    protected override void Init()
    {
        this.layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 80, 3);
        _clanInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
        _clanInput.Setup("", ChangeClanName);
    }
    private void InitialTabButtons()
    {
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "specific_clan_relations"))
        {
            var tab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            tab.Setup("specific_clan_relations", ScrollWindowComponent, action: _ => refreshAll(),
                sprite: SpriteTextureLoader.getSprite("ui/iconOptions"));
        }
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "specific_clan_history"))
        {
            var tab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            tab.Setup("specific_clan_history", ScrollWindowComponent, action: ShowPersonalHistory,
                sprite: SpriteTextureLoader.getSprite("ui/iconHistory"));
        }
        if (ScrollWindowComponent.tabs._tabs.All(p => p.name != "specific_clan_related_history"))
        {
            var tab = GameObject.Instantiate(SimpleWindowTab.Prefab);
            tab.Setup("specific_clan_related_history", ScrollWindowComponent, action: ShowRelatedHistory,
                sprite: SpriteTextureLoader.getSprite("ui/iconHistory"));
        }
    }

    public void ChangeClanName(string text)
    {
        var namePart = text.Split('\u200A');
        _clanInput.input.text = namePart[0] + "\u200A" + LM.Get("specific_clan");
        _sc.name = namePart[0];
        foreach (var member in _sc._cache.Where(member => member.Value.is_alive))
        {
            member.Value._actor.GetModName().familyName = namePart[0];
            member.Value._actor.GetModName().SetName(member.Value._actor);
            if (member.Value._actor.hasClan())
            {
                member.Value._actor.clan.data.name = namePart[0] + "\u200A" + LM.Get("Clan");
            }
        }
        LogService.LogInfo("changing clan name");
    }
    
    [Hotfixable]
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        this._actor = SelectedUnit.unit;
        if (this._actor == null) return;
        if (!this._actor.HasSpecificClan()) return;
        _sc = _actor.GetSpecificClan();
        _identity = _actor.GetPersonalIdentity();
        InitialTabButtons();
        refreshAll();
    }

    public void showTopPart()
    {
        InitialTextInput();
        InitialTop();
        InitialSearchSpace();
    }
    public void refreshAll()
    {
        Clear();
        showTopPart();
        ShowLoversActorSpace();
        ShowParentGenerationSpace();
        ShowChildrenGenerationSpace();
        ShowGrandChildGenerationSpace();
        ShowSameGenerationSpace();
        ShowSiblingChildGenerationSpace();
    }
    private void ShowPersonalHistory(WindowMetaTab _)
    {
        if (_identity == null) return;
        Clear();
        InitialTextInput();
        InitialTop();
        var historySpace = this.BeginVertGroup();
        var heading = GameObject.Instantiate(SimpleText.Prefab);
        heading.Setup(LM.Get("specific_clan_history"), TextAnchor.MiddleCenter, new Vector2(180, 22));
        heading.UseFixedFontSize(13);
        historySpace.AddChild(heading.gameObject);

        _identity.BackfillCurrentEraNameHistory();
        _identity.BackfillChildBirthHistoryNames();
        List<PersonalHistoryRecord> records = (_identity.personal_history ?? new List<PersonalHistoryRecord>())
            .OrderBy(record => record.timestamp)
            .ToList();
        if (records.Count == 0)
        {
            historySpace.AddTextIntoVertLayout(LM.Get("personal_history_empty"), true, TextAnchor.MiddleCenter, new Vector2(180, 22));
        }
        else
        {
            foreach (IGrouping<string, PersonalHistoryRecord> yearRecords in records.GroupBy(GetHistoryYear))
            {
                var yearText = historySpace.AddTextIntoVertLayout(yearRecords.Key.ColorString(pColor: new Color(1f, 0.78f, 0.2f)), true,
                    TextAnchor.MiddleCenter, new Vector2(130, 18));
                yearText.UseFixedFontSize(9, HorizontalWrapMode.Overflow);

                foreach (PersonalHistoryRecord record in yearRecords)
                {
                    PersonalClanIdentity relatedIdentity = GetRelatedHistoryIdentity(record);
                    bool hasRelatedAvatar = relatedIdentity != null;
                    var eventCard = this.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(196, 28));
                    var dateText = eventCard.AddTextIntoHoriLayout(GetHistoryMonthDay(record).ColorString(pColor: new Color(0.25f, 0.9f, 0.8f)), true,
                        TextAnchor.MiddleCenter, new Vector2(38, 22));
                    dateText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
                    string kingdomSuffix = string.IsNullOrWhiteSpace(record.kingdom_name) ? "" : $" · {record.kingdom_name}";
                    var contentText = eventCard.AddTextIntoHoriLayout(GetHistoryContent(record) + kingdomSuffix, true, TextAnchor.MiddleLeft,
                        new Vector2(hasRelatedAvatar ? 108 : 136, 22));
                    contentText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
                    if (hasRelatedAvatar)
                    {
                        AutoVertLayoutGroup avatarLayout = this.BeginVertGroup(new Vector2(24, 24), pSpacing: 0,
                            pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
                        SimpleButton avatar = UIHelper.CreateAvatarView(relatedIdentity.actor_id,
                            () => ShowRelatedPersonHistory(relatedIdentity), pIsAlive: relatedIdentity.is_alive);
                        avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
                        avatarLayout.AddChild(avatar.gameObject);
                        avatarLayout.transform.localPosition = Vector3.zero;
                        eventCard.AddChild(avatarLayout.gameObject);
                    }
                    AddHistoryButton(eventCard, record);
                    eventCard.transform.AddStretchBackground("clanFrame", size: new Vector2(196, 28));
                    historySpace.AddChild(eventCard.gameObject);
                }
            }
        }
        AddChild(historySpace.gameObject);
        _groups["personal_history"] = historySpace;
    }

    private static void AddHistoryButton(AutoHoriLayoutGroup parent, PersonalHistoryRecord record)
    {
        Empire empire = record?.empire_id > 0 ? ModClass.EMPIRE_MANAGER.get(record.empire_id) : null;
        if (empire == null || empire.IsArchived() || empire.data == null) return;

        EmpireCraftHistory history = empire.data.currentHistory ?? empire.data.history?.LastOrDefault();
        if (history == null) return;

        parent.AddButtonIntoHoriLayout("open_history", "", () =>
        {
            EmpireCraftMetaTypeLibrary.selected_empire = empire;
            ConfigData.CURRENT_SELECTED_HISTORY = history;
            ScrollWindow.showWindow(nameof(EmpireHistoryWindow));
        }, SpriteTextureLoader.getSprite("ui/iconHistory"), size: new Vector2(14, 14), showTip: true);
    }

    private static string GetHistoryContent(PersonalHistoryRecord record)
    {
        if (record?.event_key != "personal_history_child_born") return record?.content ?? "";
        if (!record.content?.Contains("§") ?? true) return record.content ?? "";
        PersonalClanIdentity childIdentity = record.related_personal_identity_id > 0
            ? SpecificClanManager.getPerson(record.related_personal_identity_id)
            : record.related_actor_id > 0 ? World.world.units.get(record.related_actor_id)?.GetPersonalIdentity() : null;
        childIdentity?.BackfillRelatedHistoryRecords();
        return record.content;
    }

    private static PersonalClanIdentity GetRelatedHistoryIdentity(PersonalHistoryRecord record)
    {
        if (record == null) return null;
        if (record.related_personal_identity_id > 0)
        {
            return SpecificClanManager.getPerson(record.related_personal_identity_id);
        }
        return record.related_actor_id > 0
            ? World.world.units.get(record.related_actor_id)?.GetPersonalIdentity()
            : null;
    }

    private void ShowRelatedPersonHistory(PersonalClanIdentity identity)
    {
        if (identity == null) return;
        _identity = identity;
        _sc = identity._specificClan;
        ShowPersonalHistory(null);
    }

    private void ShowRelatedHistory(WindowMetaTab _)
    {
        if (_identity == null) return;
        Clear();
        InitialTextInput();
        InitialTop();

        var historySpace = this.BeginVertGroup(pSpacing: 3, pAlignment: TextAnchor.UpperCenter);
        var heading = GameObject.Instantiate(SimpleText.Prefab);
        heading.Setup(LM.Get("specific_clan_related_history"), TextAnchor.MiddleCenter, new Vector2(180, 22));
        heading.UseFixedFontSize(13);
        historySpace.AddChild(heading.gameObject);

        _identity.BackfillRelatedHistoryRecords();
        List<PersonalHistoryRecord> records = (_identity.related_history_records ?? new List<PersonalHistoryRecord>())
            .Where(record => record != null)
            .OrderByDescending(record => record.timestamp)
            .ToList();
        if (records.Count == 0)
        {
            historySpace.AddTextIntoVertLayout(LM.Get("related_history_empty"), true, TextAnchor.MiddleCenter,
                new Vector2(180, 24));
        }
        else
        {
            foreach (PersonalHistoryRecord record in records)
            {
                PersonalClanIdentity owner = record.owner_personal_identity_id > 0
                    ? SpecificClanManager.getPerson(record.owner_personal_identity_id)
                    : null;
                bool hasOwnerAvatar = owner != null;
                var eventCard = this.BeginHoriGroup(pSpacing: 2, pAlignment: TextAnchor.MiddleLeft,
                    pSize: new Vector2(196, 28));
                var dateText = eventCard.AddTextIntoHoriLayout(GetHistoryMonthDay(record).ColorString(
                    pColor: new Color(0.25f, 0.9f, 0.8f)), true, TextAnchor.MiddleCenter, new Vector2(38, 22));
                dateText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);

                var details = this.BeginVertGroup(pSpacing: -2, pAlignment: TextAnchor.MiddleLeft,
                    pSize: new Vector2(hasOwnerAvatar ? 124 : 150, 24));
                string ownerName = owner?.name;
                if (string.IsNullOrWhiteSpace(ownerName)) ownerName = LM.Get("related_history_owner_unknown");
                var ownerText = details.AddTextIntoVertLayout($"{LM.Get("related_history_owner")}: {ownerName}".ColorString(
                    pColor: new Color(1f, 0.78f, 0.2f)), true, TextAnchor.MiddleLeft, new Vector2(hasOwnerAvatar ? 122 : 148, 11));
                ownerText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
                var contentText = details.AddTextIntoVertLayout(string.Format(LM.Get("related_history_event"), GetHistoryContent(record)),
                    true, TextAnchor.MiddleLeft, new Vector2(hasOwnerAvatar ? 122 : 148, 11));
                contentText.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
                eventCard.AddChild(details.gameObject);

                if (owner != null)
                {
                    // Match the genealogy cards so dead related people keep their tombstone avatar.
                    AutoVertLayoutGroup avatarLayout = this.BeginVertGroup(new Vector2(24, 24), pSpacing: 0,
                        pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
                    SimpleButton avatar = UIHelper.CreateAvatarView(owner.actor_id,
                        () => ShowRelatedPersonHistory(owner), pIsAlive: owner.is_alive);
                    avatar.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 24);
                    avatarLayout.AddChild(avatar.gameObject);
                    avatarLayout.transform.localPosition = Vector3.zero;
                    eventCard.AddChild(avatarLayout.gameObject);
                }
                eventCard.transform.AddStretchBackground("clanFrame", size: new Vector2(196, 28));
                historySpace.AddChild(eventCard.gameObject);
            }
        }
        AddChild(historySpace.gameObject);
        _groups["related_history"] = historySpace;
    }

    private static string GetHistoryYear(PersonalHistoryRecord record)
    {
        string date = record?.date ?? "";
        int yearEnd = date.IndexOf('年');
        return yearEnd >= 0 ? date.Substring(0, yearEnd + 1) : date;
    }

    private static string GetHistoryMonthDay(PersonalHistoryRecord record)
    {
        if (record == null) return "";
        string date = Date.getDate(record.timestamp);
        int yearEnd = date.IndexOf('年');
        return yearEnd >= 0 ? date.Substring(yearEnd + 1) : date;
    }
    public void ShowFatherSideSpace()
    {
        Clear();
        showTopPart();
        ShowFatherGrandGeneration();
        ShowFatherGreatGeneration();
        ShowFatherSameGeneration();
    }
    public void ShowFatherGrandGeneration()
    {
        string title = "father_grand_generation";
        ShowSpaceBase(title, SpecificClanManager.GetFatherGrandGeneration(_identity));
    }
    public void ShowFatherGreatGeneration()
    {
        string title = "father_great_generation";
        ShowSpaceBase(title, SpecificClanManager.GetFatherGreatGeneration(_identity));
    }
    public void ShowFatherSameGeneration()
    {
        string title = "father_same_generation";
        ShowSpaceBase(title, SpecificClanManager.GetFatherSameGeneration(_identity));
    }
    public void ShowMotherSideSpace()
    {
        Clear();
        showTopPart();
        ShowMotherGrandGeneration();
        ShowMotherGreatGeneration();
        ShowMotherSameGeneration();
    }
    public void ShowMotherGrandGeneration()
    {
        string title = "mother_grand_generation";
        ShowSpaceBase(title, SpecificClanManager.GetMotherGrandGeneration(_identity));
    }
    public void ShowMotherGreatGeneration()
    {
        string title = "mother_great_generation";
        ShowSpaceBase(title, SpecificClanManager.GetMotherGreatGeneration(_identity));
    }
    public void ShowMotherSameGeneration()
    {
        string title = "mother_same_generation";
        ShowSpaceBase(title, SpecificClanManager.GetMotherSameGeneration(_identity));
    }

    public void StartSearchActor(string content)
    {
        _lastSearchContent = content;
        Clear();
        showTopPart();
        List<(ClanRelation, PersonalClanIdentity)> result = OverallHelperFunc.SearchPersonalClanIdentityHelper(content, SpecificClanManager.FindAllRelations(_identity));
        string title = "title_search_result";
        ShowSpaceBase(title, result);
    }

    [Hotfixable]
    public void InitialSearchSpace()
    {
        var topSearchSpace = this.BeginHoriGroup();
        topSearchSpace.transform.SetParent(this.transform.parent, false);
        SimpleButton fatherSideButton = Instantiate(SimpleButton.Prefab);
        fatherSideButton.Setup(ShowFatherSideSpace, null, LM.Get("father_relation"), new Vector2(30, 15));
        topSearchSpace.AddChild(fatherSideButton.gameObject);
        
        //搜索框
        TextInput relationSearchInput = UIHelper.GenerateTextInput(topSearchSpace.transform, action:StartSearchActor, default_text:_lastSearchContent);
        
        SimpleButton motherSideButton = Instantiate(SimpleButton.Prefab);
        motherSideButton.Setup(ShowMotherSideSpace, null, LM.Get("mother_relation"), new Vector2(30, 15));
        topSearchSpace.AddChild(motherSideButton.gameObject);
        
        var rt = topSearchSpace.GetComponent<RectTransform>();
        
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        
        rt.pivot = new Vector2(0.5f, 1f);
        
        rt.anchoredPosition = Vector2.zero+Vector2.down*62;
        
        rt.localScale = Vector3.one;
        
        var layout = this.transform.parent.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
            layout.childAlignment = TextAnchor.MiddleCenter;
        
        topSearchSpace.transform.SetAsLastSibling();
        
        _hGroups.Add("InitialSearchSpace", topSearchSpace);
    }
    [Hotfixable]
    public void InitialTop()
    {
        var topActorSpace = ShowCurrentActorSpace();
        topActorSpace.transform.SetParent(this.transform.parent, false);
        
        var rt = topActorSpace.GetComponent<RectTransform>();

        rt.pivot = new Vector2(0.5f, 1f);
        
        rt.anchoredPosition = Vector2.zero;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); 
        // 关闭拉伸，用 sizeDelta 定宽高
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        // 局部坐标归零（中心对中心）
        rt.anchoredPosition = new Vector2(0, 10);
        topActorSpace.transform.SetAsLastSibling();
        var le = topActorSpace.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        
        topActorSpace.transform.SetAsLastSibling();

    }

    public void InitialTextInput()
    {
        string text = _sc.name + "\u200A" + LM.Get("specific_clan");
        UIHelper.GenerateTextInput(this.transform.parent.transform.parent, offset:new Vector2(0, 152), default_text:text, input:_clanInput);
    }

    public AutoVertLayoutGroup ShowCurrentActorSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();
        personalClanIdentities.Add((ClanRelation.SELF, _identity));
        return ShowSpaceBase("current_actor_generation", personalClanIdentities, true);
    }    
    public void ShowLoversActorSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();
        if (_identity.hasLover())
        {
            personalClanIdentities.Add((ClanRelation.LOV, SpecificClanManager.getPerson(_identity.lover.identity)));
        }
        foreach (var cob in _identity.concubines)
        {
            personalClanIdentities.Add((ClanRelation.COB, SpecificClanManager.getPerson(cob.identity)));
        }
        ShowSpaceBase("current_lovers_generation", personalClanIdentities);
    }
    public void Clear()
    {
        foreach (var group in _groups)
        {
            Destroy(group.Value.gameObject);
        }
        _groups.Clear();
        foreach (var group in _hGroups)
        {
            Destroy(group.Value.gameObject);
        }
        _hGroups.Clear();
    }
    //显示直系长辈空间
    public AutoVertLayoutGroup ShowParentGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();

        PersonalClanIdentity fatherIdentity = SpecificClanManager.getPerson(_identity.father);
        if (fatherIdentity != null) personalClanIdentities.Add((ClanRelation.FAT, fatherIdentity));

        PersonalClanIdentity motherIdentity = SpecificClanManager.getPerson(_identity.mother);
        if (motherIdentity != null) personalClanIdentities.Add((ClanRelation.MOM, motherIdentity));

        PersonalClanIdentity filIdentity = SpecificClanManager.getPerson(_identity.father_in_law);
        if (filIdentity != null) personalClanIdentities.Add((ClanRelation.FIL, filIdentity));

        PersonalClanIdentity milIdentity = SpecificClanManager.getPerson(_identity.mother_in_law);
        if (milIdentity != null) personalClanIdentities.Add((ClanRelation.MIL, milIdentity));

        return ShowSpaceBase("current_parent_generation", personalClanIdentities);
    }
    public AutoVertLayoutGroup ShowGrandChildGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> grandChildrenWithRelations = SpecificClanManager.GetGrandChildren(_identity);
        string title = "current_grand_children_generation";
        return ShowSpaceBase("current_grand_children_generation", grandChildrenWithRelations);
    }

    //显示直系同辈空间
    public AutoVertLayoutGroup ShowSameGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> siblingsWithRelations = SpecificClanManager.GetSiblingsWithRelation(_identity);
        return ShowSpaceBase("current_same_generation", siblingsWithRelations);
    }
    //显示直系子辈空间
    public AutoVertLayoutGroup ShowChildrenGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();
        personalClanIdentities.AddRange(SpecificClanManager.getChildren(_identity));
        return ShowSpaceBase("current_children_generation", personalClanIdentities);
    }
    //显示同辈子辈空间
    public AutoVertLayoutGroup ShowSiblingChildGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();
        personalClanIdentities.AddRange(SpecificClanManager.GetSiblingChildGeneration(_identity));
        return ShowSpaceBase("sibling_child_generation", personalClanIdentities);
    }

    public AutoVertLayoutGroup ShowSpaceBase(string spaceName, List<(ClanRelation relation, PersonalClanIdentity identity)> relationthip=null, bool is_single = false)
    {
        if (!relationthip.Any()) return null;
        var baseSpace = this.BeginVertGroup();
        if (is_single)
        {
            AutoVertLayoutGroup baseActorGrid = this.BeginVertGroup();
            ShowMainInfo(baseActorGrid, relationthip[0].identity, relationthip[0].relation);
            baseSpace.AddChild(baseActorGrid.gameObject);
        }
        else
        {
            SimpleText baseSpaceTitle = GameObject.Instantiate(SimpleText.Prefab);
            baseSpaceTitle.Setup(LM.Get(spaceName), TextAnchor.MiddleCenter);
            baseSpaceTitle.background.enabled = false;
            baseSpace.AddChild(baseSpaceTitle.gameObject);
            
            AutoGridLayoutGroup baseActorGrid = this.BeginGridGroup(2, pCellSize: new Vector2(100, 50), pSpacing:new Vector2(0, 0));
            if (relationthip != null)
            {
                ShowPersonalInfos(baseActorGrid, relationthip);
            }
            baseSpace.AddChild(baseActorGrid.gameObject);
        }
        
        AddChild(baseSpace.gameObject);

        _groups.Add(spaceName, baseSpace);
        return baseSpace;
    }

    public void ShowPersonalInfos(AutoGridLayoutGroup baseActorGrid, List<(ClanRelation relation, PersonalClanIdentity identity)> relationthip)
    {
        foreach(var identity in relationthip)
        {
            if (identity.identity == null) continue;
            ShowPersonalInfo(baseActorGrid, identity.identity, identity.relation);
        }
    }
    public void ChangeActor(PersonalClanIdentity actor_identity)
    {
        this._identity = actor_identity;
        this._sc = actor_identity._specificClan;
        refreshAll();
    }
    [Hotfixable]
    public void ShowPersonalInfo(AutoGridLayoutGroup parent, PersonalClanIdentity actor, ClanRelation relation = ClanRelation.NONE)
    {
        
        AutoHoriLayoutGroup personalGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:12, pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
        
        SimpleButton clickframe = UIHelper.CreateAvatarView(actor.actor_id, () => ChangeActor(actor), pIsAlive:actor.is_alive);
        avatarLayoutGroup.AddChild(clickframe.gameObject);
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(avatarLayoutGroup.gameObject);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);
        
        
        leftVertGroup.AddTextIntoVertLayout($"<color=#FF4500>{(actor.is_alive?"":LM.Get("is_dead")+"-")}</color>{actor.name} ({LM.Get($"relation_{relation.ToString()}")}-{LM.Get(actor.isMainText)})", size: new Vector2(50, 10));
        leftVertGroup.AddTextIntoVertLayout($"{actor.birthday+"-"+actor.getDeathday()}", size: new Vector2(50, 10));
        var flag = actor.is_alive && actor._actor.IsOnOffice();
        leftVertGroup.AddTextIntoVertLayout($"官职：{(string.IsNullOrEmpty(actor.officeName)?"无":actor.officeName).ColorString(pColor:flag?Color.yellow:Color.gray)}");
        leftVertGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(leftVertGroup.gameObject);
        personalGroup.transform.AddStretchBackground("clanFrame", size: new Vector2(100, 50));
        parent.AddChild(personalGroup.gameObject);
    }
    [Hotfixable]
    
    public void ShowMainInfo(AutoVertLayoutGroup parent, PersonalClanIdentity actor, ClanRelation relation = ClanRelation.NONE)
    {
        AutoHoriLayoutGroup personalGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter, pSize:new Vector2(200, 70), pSpacing: 10);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.UpperCenter);

        SimpleText nameText = GameObject.Instantiate(SimpleText.Prefab);
        nameText.Setup($"<color=#FF4500>{(actor.is_alive?"":LM.Get("is_dead")+"-")}</color>{actor.name}-{LM.Get(actor.isMainText)}", pSize: new Vector2(50, 10));

        SimpleText levelText = GameObject.Instantiate(SimpleText.Prefab);
        var flag = actor.is_alive && actor._actor.IsOnOffice();
        levelText.Setup($"官职：{(string.IsNullOrEmpty(actor.officeName)?"无":actor.officeName).ColorString(pColor:flag?Color.yellow:Color.gray)}", pSize: new Vector2(50, 10));

        SimpleText timeText = GameObject.Instantiate(SimpleText.Prefab);
        timeText.Setup($"{actor.birthday+"-"+actor.getDeathday()}", pSize: new Vector2(50, 10));


        leftVertGroup.AddChild(nameText.gameObject);
        leftVertGroup.AddChild(levelText.gameObject);
        leftVertGroup.AddChild(timeText.gameObject);
        leftVertGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(leftVertGroup.gameObject);
        
        //中间头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:15, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 30));

        SimpleButton backToMain = Instantiate(SimpleButton.Prefab);
        backToMain.Setup(refreshAll, null, LM.Get("i_back_to_main"), new Vector2(20,10));


        SimpleButton clickframe = UIHelper.CreateAvatarView(actor.actor_id, pIsAlive:actor.is_alive);

        avatarLayoutGroup.AddChild(backToMain.gameObject);
        avatarLayoutGroup.AddChild(clickframe.gameObject);
        avatarLayoutGroup.AddTextIntoVertLayout($"历史称帝法理：{(actor._specificClan.HasHistoryEmpire()?actor._specificClan.empire_name:"无").ColorString(pColor:Color.yellow)}", hideBackground:true);
        // avatarLayoutGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(avatarLayoutGroup.gameObject);

        //右边信息栏
        AutoVertLayoutGroup rightVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.UpperCenter);

        SimpleText generationText = GameObject.Instantiate(SimpleText.Prefab);
        generationText.Setup($"{LM.Get("i_generation") + ": "+actor.generation}", pSize: new Vector2(50, 10));

        SimpleText memberCount = GameObject.Instantiate(SimpleText.Prefab);
        memberCount.Setup(LM.Get("total_sc_count")+": "+_sc.Count+"/"+_sc.CountTotal, pSize: new Vector2(50, 10));

        SimpleText founderText = GameObject.Instantiate(SimpleText.Prefab);
        founderText.Setup($"{LM.Get("i_founder")}: {SpecificClanManager.getPerson(_sc.founder).name}", pSize: new Vector2(50, 10));


        rightVertGroup.AddChild(generationText.gameObject);
        rightVertGroup.AddChild(memberCount.gameObject);
        rightVertGroup.AddChild(founderText.gameObject);
        rightVertGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(rightVertGroup.gameObject);
        personalGroup.transform.AddStretchBackground("clanFrame");
        
        parent.AddChild(personalGroup.gameObject);
    }
}
