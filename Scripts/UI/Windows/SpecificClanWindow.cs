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
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;
public class SpecificClanWindow : AutoLayoutWindow<SpecificClanWindow>
{
    public Actor _actor;
    public Dictionary<string, AutoVertLayoutGroup> _groups = new ();
    public SpecificClan _sc;
    public PersonalClanIdentity _identity;
    protected override void Init()
    {
        var gridLayout = this.BeginGridGroup(5, GridLayoutGroup.Constraint.FixedRowCount);
    }

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
        Clear();
        ShowParentGenerationSpace();
        ShowSameGenerationSpace();
        ShowChildrenGenerationSpace();
    }
    public void Clear()
    {
        foreach (var group in _groups)
        {
            GameObject.Destroy(group.Value.gameObject);
        }
        _groups.Clear();
    }
    //显示直系长辈空间
    public void ShowParentGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();

        PersonalClanIdentity fatherIdentity = _sc.GetPerson(_identity.father);
        if (fatherIdentity != null) personalClanIdentities.Add((ClanRelation.FAT, fatherIdentity));

        PersonalClanIdentity motherIdentity = _sc.GetPerson(_identity.mother);
        if (motherIdentity != null) personalClanIdentities.Add((ClanRelation.MOM, motherIdentity));

        PersonalClanIdentity filIdentity = _sc.GetPerson(_identity.father_in_law);
        if (filIdentity != null) personalClanIdentities.Add((ClanRelation.FIL, filIdentity));

        PersonalClanIdentity milIdentity = _sc.GetPerson(_identity.mother_in_law);
        if (milIdentity != null) personalClanIdentities.Add((ClanRelation.MIL, milIdentity));

        ShowSpaceBase("current_parent_generation", personalClanIdentities);
    }

    //显示直系同辈空间
    public void ShowSameGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> siblingsWithRelations = _sc.GetSiblingsWithRelation(_identity);
        ShowSpaceBase("current_same_generation", siblingsWithRelations);
    }
    //显示直系子辈空间
    public void ShowChildrenGenerationSpace()
    {
        List<(ClanRelation, PersonalClanIdentity)> personalClanIdentities = new List<(ClanRelation, PersonalClanIdentity)>();
        personalClanIdentities.AddRange(_identity.children.Select(id => (ClanRelation.CHILD,_sc.GetPerson(id))));
        ShowSpaceBase("current_children_generation", personalClanIdentities);
    }

    public void ShowSpaceBase(string spaceName, List<(ClanRelation relation, PersonalClanIdentity identity)> relationthip=null)
    {
        var baseSpace = this.BeginVertGroup();
        SimpleText baseSpaceTitle = GameObject.Instantiate(SimpleText.Prefab);
        baseSpaceTitle.Setup(LM.Get(spaceName), TextAnchor.MiddleCenter);
        baseSpaceTitle.background.enabled = false;
        baseSpace.AddChild(baseSpaceTitle.gameObject);

        AutoGridLayoutGroup baseActorGrid = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount);
        if (relationthip != null) 
        {
            foreach(var identity in relationthip)
            {
                ShowPersonalInfo(baseActorGrid, identity.identity, identity.relation);
            }
        }
        baseSpace.AddChild(baseActorGrid.gameObject);
        AddChild(baseSpace.gameObject);

        _groups.Add(spaceName, baseSpace);
    }

    public void ShowPersonalInfo(AutoGridLayoutGroup parent, PersonalClanIdentity actor, ClanRelation relation = ClanRelation.NONE)
    {
        bool selected = false;
        bool showRelation = false;
        //当有显示的角色是主要角色的同辈时应显示相应的关系，如同父异母，同母异父，同母同父等
        if (relation == ClanRelation.SELF) selected = true;
        if (relation != ClanRelation.NONE&& relation != ClanRelation.SELF) showRelation = true;
        if (selected)
        {
            //显示选择的标识
            SimpleButton avatorView = UIHelper.CreateAvatarView(actor.actor_id);
            parent.AddChild(avatorView.gameObject);
        }
        if (showRelation) 
        {
            //显示亲属关系文字
        }
    }
}
