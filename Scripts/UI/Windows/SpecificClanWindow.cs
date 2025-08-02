using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using NeoModLoader.services;
using System.Collections.Generic;
using System.Linq;
using NeoModLoader.api.attributes;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class SpecificClanWindow : AutoLayoutWindow<SpecificClanWindow>
{
    public Actor _actor;
    public Dictionary<string, AutoVertLayoutGroup> _groups = new ();
    public SpecificClan _sc;
    public PersonalClanIdentity _identity;
    public TextInput clanInput;
    protected override void Init()
    {
        this.layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 50, 3);
        clanInput = Instantiate(TextInput.Prefab, this.transform.parent.transform.parent);
        clanInput.Setup("", changeClanName);
    }

    public void changeClanName(string name)
    {
        clanInput.input.text = name + "\u200A" + LM.Get("specific_clan");
        _sc.name = name;
        foreach (var member in _sc._cache)
        {
            if (member.Value.is_alive)
            {
                member.Value._actor.GetModName().familyName = name;
                member.Value._actor.GetModName().SetName(member.Value._actor);
            }

            foreach (var clan in World.world.clans)
            {
                if (clan.HasSpecificClan())
                {
                    clan.data.name = clan.data.name = name + "\u200A" + LM.Get("Clan");
                }
            }
        }
        LogService.LogInfo("changing clan name");
    }
    
    [Hotfixable]
    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        this._actor = SelectedUnit.unit;
        
        LogService.LogInfo($"选择角色的名称为{this._actor.name}");
        if (this._actor == null) return;
        if (!this._actor.HasSpecificClan()) return;
        _sc = _actor.GetSpecificClan();
        LogService.LogInfo($"{_sc.name}氏族,成员{_sc.Count}");
        _identity = _actor.GetPersonalIdentity();
        refreshAll();
    }

    public void refreshAll()
    {
        Clear();
        InitialTextInput();
        InitialTop();
        ShowLoversActorSpace();
        ShowParentGenerationSpace();
        ShowSameGenerationSpace();
        ShowChildrenGenerationSpace();
    }
    public void InitialTop()
    {

        var topActorSpace = ShowCurrentActorSpace();
        topActorSpace.transform.SetParent(this.transform.parent, false);
        
        var rt = topActorSpace.GetComponent<RectTransform>();
        
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        
        rt.pivot = new Vector2(0.5f, 1f);
        
        rt.anchoredPosition = Vector2.zero+Vector2.down*15+Vector2.left*68;
        
        rt.localScale = Vector3.one;
        topActorSpace.transform.SetAsLastSibling();
        
        var layout = this.transform.parent.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
            layout.childAlignment = TextAnchor.MiddleCenter;
        
        topActorSpace.transform.SetAsLastSibling();

    }

    public void InitialTextInput()
    {
        clanInput.SetSize(new Vector2(130, 15));
        clanInput.input.textComponent.alignment = TextAnchor.MiddleCenter;
        var rt1 = clanInput.input.GetComponent<RectTransform>();
        rt1.sizeDelta = new Vector2(130, 15);
        clanInput.transform.localPosition = Vector3.up*152;
        clanInput.transform.SetAsLastSibling();
        clanInput.input.text = _sc.name + "\u200A" + LM.Get("specific_clan");
    }

    public AutoVertLayoutGroup ShowCurrentActorSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();
        personalClanIdentities.Add((ClanRelation.SELF, _identity));
        return ShowSpaceBase("current_actor_generation", personalClanIdentities, true);
    }    
    public AutoVertLayoutGroup ShowLoversActorSpace()
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
        return ShowSpaceBase("current_lovers_generation", personalClanIdentities);
    }
    public void Clear()
    {
        foreach (var group in _groups)
        {
            Destroy(group.Value.gameObject);
        }
        _groups.Clear();
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

    //显示直系同辈空间
    public AutoVertLayoutGroup ShowSameGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> siblingsWithRelations = _sc.GetSiblingsWithRelation(_identity);
        return ShowSpaceBase("current_same_generation", siblingsWithRelations);
    }
    //显示直系子辈空间
    public AutoVertLayoutGroup ShowChildrenGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();
        personalClanIdentities.AddRange(SpecificClanManager.getChildren(_identity));
        LogService.LogInfo("子嗣数量: "+ personalClanIdentities.Count.ToString());
        return ShowSpaceBase("current_children_generation", personalClanIdentities);
    }

    public AutoVertLayoutGroup ShowSpaceBase(string spaceName, List<(ClanRelation relation, PersonalClanIdentity identity)> relationthip=null, bool is_single = false)
    {
        if (!relationthip.Any()) return null;
        var baseSpace = this.BeginVertGroup();
        if (is_single)
        {
            AutoGridLayoutGroup baseActorGrid = this.BeginGridGroup(1);
            ShowMainInfo(baseActorGrid, relationthip[0].identity, relationthip[0].relation);
            baseSpace.AddChild(baseActorGrid.gameObject);
        }
        else
        {
            SimpleText baseSpaceTitle = GameObject.Instantiate(SimpleText.Prefab);
            baseSpaceTitle.Setup(LM.Get(spaceName), TextAnchor.MiddleCenter);
            baseSpaceTitle.background.enabled = false;
            baseSpace.AddChild(baseSpaceTitle.gameObject);
            
            AutoGridLayoutGroup baseActorGrid = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 30), pSpacing:new Vector2(0, 0));
            if (relationthip != null) 
            {
                foreach(var identity in relationthip)
                {
                    ShowPersonalInfo(baseActorGrid, identity.identity, identity.relation);
                }
            }
            baseSpace.AddChild(baseActorGrid.gameObject);
        }
        
        AddChild(baseSpace.gameObject);

        _groups.Add(spaceName, baseSpace);
        return baseSpace;
    }

    public void ChangeActor(PersonalClanIdentity actor_identity)
    {
        this._identity = actor_identity;
        this._sc = actor_identity._specificClan;
        refreshAll();
    }
    public void ShowPersonalInfo(AutoGridLayoutGroup parent, PersonalClanIdentity actor, ClanRelation relation = ClanRelation.NONE)
    {
        
        AutoHoriLayoutGroup personalGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:12, pAlignment: TextAnchor.MiddleCenter, pPadding: new RectOffset(0, 0, 0, 0));
        
        SimpleButton clickframe = UIHelper.CreateAvatarView(actor.actor_id, () => ChangeActor(actor));
        avatarLayoutGroup.AddChild(clickframe.gameObject);
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(avatarLayoutGroup.gameObject);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);

        SimpleText nameText = GameObject.Instantiate(SimpleText.Prefab);
        nameText.Setup($"{actor.name} ({LM.Get($"relation_{relation.ToString()}")})", pSize: new Vector2(50, 10));
        
        SimpleText timeText = GameObject.Instantiate(SimpleText.Prefab);
        timeText.Setup($"{actor.birthday+"-"+actor.getDeathday()}", pSize: new Vector2(50, 10));

        leftVertGroup.AddChild(nameText.gameObject);
        leftVertGroup.AddChild(timeText.gameObject);
        leftVertGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(leftVertGroup.gameObject);

        parent.AddChild(personalGroup.gameObject);
    }

    public void ShowMainInfo(AutoGridLayoutGroup parent, PersonalClanIdentity actor, ClanRelation relation = ClanRelation.NONE)
    {
        AutoHoriLayoutGroup personalGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.UpperCenter);

        SimpleText nameText = GameObject.Instantiate(SimpleText.Prefab);
        nameText.Setup($"{actor.name}", pSize: new Vector2(50, 10));

        SimpleText levelText = GameObject.Instantiate(SimpleText.Prefab);
        levelText.Setup(LM.Get($"relation_{relation.ToString()}"), pSize: new Vector2(50, 10));

        SimpleText timeText = GameObject.Instantiate(SimpleText.Prefab);
        timeText.Setup($"{actor.birthday+"-"+actor.getDeathday()}", pSize: new Vector2(50, 10));


        leftVertGroup.AddChild(nameText.gameObject);
        leftVertGroup.AddChild(levelText.gameObject);
        leftVertGroup.AddChild(timeText.gameObject);
        leftVertGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(leftVertGroup.gameObject);
        
        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:12, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 20));

        SimpleText title = Instantiate(SimpleText.Prefab);
        title.Setup(relation.ToString(), TextAnchor.MiddleCenter, new Vector2(30, 10));
        title.background.enabled = false;


        SimpleButton clickframe = UIHelper.CreateAvatarView(actor.actor_id);
        

        avatarLayoutGroup.AddChild(title.gameObject);
        avatarLayoutGroup.AddChild(clickframe.gameObject);
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(avatarLayoutGroup.gameObject);

        //右边信息栏
        AutoVertLayoutGroup rightVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.UpperCenter);

        SimpleText generationText = GameObject.Instantiate(SimpleText.Prefab);
        generationText.Setup($"{actor.generation}", pSize: new Vector2(50, 10));

        SimpleText memberCount = GameObject.Instantiate(SimpleText.Prefab);
        memberCount.Setup(LM.Get("total_sc_count")+": "+_sc.Count, pSize: new Vector2(50, 10));

        SimpleText founderText = GameObject.Instantiate(SimpleText.Prefab);
        founderText.Setup($"{LM.Get("i_founder")}: {SpecificClanManager.getPerson(_sc.founder).name}", pSize: new Vector2(50, 10));


        rightVertGroup.AddChild(generationText.gameObject);
        rightVertGroup.AddChild(memberCount.gameObject);
        rightVertGroup.AddChild(founderText.gameObject);
        rightVertGroup.transform.localPosition = Vector3.zero;
        personalGroup.AddChild(rightVertGroup.gameObject);
        personalGroup.transform.AddStretchBackground(SpriteTextureLoader.getSprite("ui/clanFrame"));
        
        parent.AddChild(personalGroup.gameObject);
    }
}
