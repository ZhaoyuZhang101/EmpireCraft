using NeoModLoader.api;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using NCMS.Extensions;
using EpPathFinding.cs;
using System.Drawing.Printing;
using DG.Tweening;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine.Events;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General.UI.Window;
using UnityEngine.Pool;
using NeoModLoader.services;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.api.attributes;

namespace EmpireCraft.Scripts.UI.Windows;
public class EmpireBeaurauWindow : AutoLayoutWindow<EmpireBeaurauWindow>
{
    public Empire _empire;
    public string culture = "Huaxia";
    AutoHoriLayoutGroup topSpace;
    AutoGridLayoutGroup topOfficeGroup1;
    AutoGridLayoutGroup topOfficeGroup2;

    AutoVertLayoutGroup coreOfficeSpace;
    AutoGridLayoutGroup coreOfficeGroup;

    AutoVertLayoutGroup divisionsSpace;
    AutoGridLayoutGroup divisionsGroup;

    AutoVertLayoutGroup provincesSpace;
    AutoGridLayoutGroup provincesGroup;
    [Header("UI Prefab & 根容器")]
    public GameObject _itemPrefab;
    public ListPool<GameObject> pool = new ListPool<GameObject>();
    protected override void Init()
    {
        layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 95, 3);
    }
    [Hotfixable]
    private void InitialTopPartInfoLvLing()
    {
        //总容器
        topSpace = this.BeginHoriGroup();
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing:-3);
        centerPart.AddTextIntoVertLayout("内阁首辅", true, TextAnchor.MiddleCenter);
        centerPart.AddActorViewIntoVertLayout(_empire.GetCabinetLeader(), 
            description:_empire.GetCabinetLeader()?.GetFaction()?.Name.ColorString(pColor:new Color(0.0f, 1, 0.5f))??"无");
        centerPart.AddTextIntoVertLayout("内阁大臣", true, TextAnchor.MiddleCenter);
        var cabinetMemberSpace = centerPart.BeginHoriGroup(pSpacing:-5);
        var members = _empire.GetCabinetMembers();
        for(int i=1; i<5; i++)
        {
            int cCount = members.Count;
            cabinetMemberSpace.AddActorViewIntoHoriLayout(i<cCount?members[i]:null, description: i < cCount
                ? members[i].GetFaction().Name.ColorString(pColor:new Color(0.0f, 1, 0.5f))
                : "无");
        }
        
        UIHelper.AddFactionCard(_empire.CoreKingdom.GetRegime().GetDominateFaction(), _empire.CoreKingdom, parentH:topSpace);
        
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    [Hotfixable]
    private void InitialTopPartInfoFeudalism()
    {
        //总容器
        topSpace = this.BeginHoriGroup();
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing:-3);
        var members = _empire.GetCabinetMembers();
        centerPart.AddTextIntoVertLayout("教廷选帝侯", true, TextAnchor.MiddleCenter);
        var cabinetMemberSpace = centerPart.BeginHoriGroup(pSpacing:-5);
        for (int i=0; i<3; i++ )
        {
            int cCount = members.Count;
            var member = i<cCount ? members[i] : null;
            cabinetMemberSpace.AddActorViewIntoHoriLayout(member, 
                description: member?.GetFaction()?.Name?.ColorString(pColor:new Color(0.0f, 1, 0.5f))??"无");
        }
        centerPart.AddTextIntoVertLayout("世俗选帝侯", true, TextAnchor.MiddleCenter);
        var cabinetMemberSpace2 = centerPart.BeginHoriGroup(pSpacing:-5);
        for(int i=3; i<7; i++)
        {
            int cCount = members.Count;
            var member = i<cCount ? members[i] : null;
            cabinetMemberSpace2.AddActorViewIntoHoriLayout(member, 
                description: member?.GetFaction()?.Name?.ColorString(pColor:new Color(0.0f, 1, 0.5f))??"无");
        }
        var dominate = _empire.CoreKingdom.GetRegime().GetDominateFaction();
        if (dominate != null)
        {
            UIHelper.AddFactionCard(dominate, _empire.CoreKingdom, parentH:topSpace);
        }
        
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    [Hotfixable]
    private void InitialTopPartInfoNormal()
    {
        //总容器
        topSpace = this.BeginHoriGroup();
        topSpace.transform.AddStretchBackground("clanFrame", new Vector2(220, 100));

        var centerPart = topSpace.BeginVertGroup(pSpacing:-3);
        centerPart.AddTextIntoVertLayout("无内阁", true, TextAnchor.MiddleCenter, new Vector2(80, 40));
        var dominate = _empire.CoreKingdom.GetRegime().GetDominateFaction();
        if (dominate != null)
        {
            UIHelper.AddFactionCard(dominate, _empire.CoreKingdom, parentH:topSpace);
        }
        topSpace.gameObject.AdjustTopPart(transform.parent.transform, offset:new Vector2(0, 0));
    }
    public void ShowCoreSpace()
    {
        coreOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        SimpleText coreOfficeTitle = Instantiate(SimpleText.Prefab);
        coreOfficeTitle.Setup(LM.Get("CoreOffice"), TextAnchor.MiddleCenter);
        coreOfficeSpace.AddChild(coreOfficeTitle.gameObject);

        coreOfficeGroup = this.BeginGridGroup(2, pCellSize: new Vector2(100, 55));
        foreach (var oid in _empire.data.centerOffice.CoreOffices)
        {
            SetOfficeView(oid, ref coreOfficeGroup);
        }
        coreOfficeSpace.AddChild(coreOfficeGroup.gameObject);

        AddChild(coreOfficeSpace.gameObject);
    }

    public void ShowDivisionSpace()
    {
        divisionsSpace = this.BeginVertGroup();
        //中央二级部门
        SimpleText divisionsTitle = Instantiate(SimpleText.Prefab);
        divisionsTitle.Setup(LM.Get("Divisions"), TextAnchor.MiddleCenter);
        divisionsSpace.AddChild(divisionsTitle.gameObject);

        divisionsGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize:new Vector2(100, 55));
        foreach (var o2 in _empire.data.centerOffice.Divisions)
        {
            SetOfficeView(o2, ref divisionsGroup);
        }
        divisionsSpace.AddChild(divisionsGroup.gameObject);

        AddChild(divisionsSpace.gameObject);
    }

    public void ShowProvincesSpace()
    {
        provincesSpace = this.BeginVertGroup();
        //省级部门
        SimpleText provinceTitle = Instantiate(SimpleText.Prefab);
        provinceTitle.Setup(LM.Get("province"), TextAnchor.MiddleCenter);
        provincesSpace.AddChild(provinceTitle.gameObject);

        provincesGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 55));
        foreach (Kingdom kingdom in _empire.kingdoms_hashset)
        {
            SetOfficeView(kingdom.GetOfficeID(), ref provincesGroup, kingdom);
        }

        provincesSpace.AddChild(provincesGroup.gameObject);

        AddChild(provincesSpace.gameObject);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 95, 3);
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        Clear();
        Regime regime = _empire.CoreKingdom.GetRegime();
        switch (regime.type)
        {
            case RegimeType.Feudalism:
                InitialTopPartInfoFeudalism();
                break;
            case RegimeType.LvLing:
                InitialTopPartInfoLvLing();
                break;
            default:
                InitialTopPartInfoNormal();
                break;
        }

        if (_empire.data.centerOffice.CoreOffices.Count > 0)
        {
            ShowCoreSpace();
        }

        if (_empire.data.centerOffice.Divisions.Count > 0)
        {
            ShowDivisionSpace();
        }
        

        ShowProvincesSpace();
    }

    public override void OnFirstEnable()
    {
        base.OnFirstEnable();
        this.DORestart();
        layout.spacing = 3;
        this.layout.padding = new RectOffset(3, 3, 95, 3);
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        Clear();
        Regime regime = _empire.CoreKingdom.GetRegime();
        switch (regime.type)
        {
            case RegimeType.Feudalism:
                InitialTopPartInfoFeudalism();
                break;
            case RegimeType.LvLing:
                InitialTopPartInfoLvLing();
                break;
            default:
                InitialTopPartInfoNormal();
                break;
        }

        if (_empire.data.centerOffice.CoreOffices.Count > 0)
        {
            ShowCoreSpace();
        }

        if (_empire.data.centerOffice.Divisions.Count > 0)
        {
            ShowDivisionSpace();
        }

        ShowProvincesSpace();
    }

    public void Clear()
    {

        if (pool == null) return;
        float deleteTime = 0.1f;
        foreach (GameObject go in pool)
        {
            go.SetActive(false);
            Destroy(go, deleteTime);
            deleteTime += 0.1f;
        }
        if (topSpace != null)
        {
            topSpace.gameObject.SetActive(false);
            Destroy(topSpace, deleteTime);
        }
        if (coreOfficeSpace != null)
        {
            coreOfficeSpace.gameObject.SetActive(false);
            Destroy(coreOfficeSpace, deleteTime);
        }
        if (divisionsSpace != null)
        {
            divisionsSpace.gameObject.SetActive(false);
            Destroy(divisionsSpace, deleteTime);
        }
        if (provincesSpace != null)
        {
            provincesSpace.gameObject.SetActive(false);
            Destroy(provincesSpace, deleteTime);
        }
        pool.Clear();
    }
    [Hotfixable]
    public void SetOfficeView(long oid, ref AutoGridLayoutGroup parent, NanoObject o = null)
    {
        //寻找存在的官制
        if (!OfficeManager.Offices.TryGetValue(oid, out var officeObject))
        {
            return;
        }
        AutoHoriLayoutGroup officePositionGroup = this.BeginHoriGroup(pSpacing:-10, pAlignment: TextAnchor.MiddleCenter, pSize:new (100, 70));

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(pSpacing:-3, pAlignment: TextAnchor.MiddleCenter);
        avatarLayoutGroup.AddTextIntoVertLayout(officeObject.GetOfficeName(o)+$"({officeObject.history_officers.Count})", true, TextAnchor.MiddleCenter);
        avatarLayoutGroup.AddActorViewIntoVertLayout(officeObject.GetActor());

        SimpleButton changeAvatar = Instantiate(SimpleButton.Prefab);
        changeAvatar.Setup(() => ChangeOfficer(officeObject), SpriteTextureLoader.getSprite("ui/changeOfficer"), pSize: new Vector2(20, 10));
        
        avatarLayoutGroup.AddChild(changeAvatar.gameObject);
        
        officePositionGroup.AddChild(avatarLayoutGroup.gameObject);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter);

        var content =
            $"{LM.Get("i_name")}: {(officeObject.GetActor() == null ? "-" : officeObject.GetActor().data.name)}\n" +
            $"{LM.Get("OfficialLevel").ColorString(pColor: new Color(0.2f, 0.7f, 0.4f))}: {officeObject.GetName(o)}\n" +
            $"{LM.Get("i_on_office_time")}: {officeObject.GetOnTime()}";
        
        leftVertGroup.AddTextIntoVertLayout(content, true, TextAnchor.MiddleCenter, new Vector2(40, 25));
        var powerContent = "<权能>\n";
        if (officeObject.powers.Count <= 0)
        {
            powerContent += "无";
        }
        else
        {
            foreach (var power in officeObject.powers)
            {
                if (OfficeManager.AllPower.Contains(power))
                {
                    powerContent += $"{power}({officeObject.GetActor()?.CalcPower(power, _empire).addition[power]??0})".ColorString(pColor:new Color(0.0f, 1, 0.5f))+"\n";
                }
                else
                {
                    powerContent += power.ToString().ColorString(pColor:new Color(0.0f, 1, 0.5f))+"\n";
                }
            }
        }
        leftVertGroup.AddTextIntoVertLayout(powerContent, true, TextAnchor.MiddleCenter, new Vector2(40, 20));
        
        leftVertGroup.transform.localPosition = Vector3.zero;
        officePositionGroup.AddChild(leftVertGroup.gameObject);

        parent.AddChild(officePositionGroup.gameObject);
        
        officePositionGroup.transform.AddStretchBackground("FactionFrame", size:new Vector2(100, 55));
        pool.Add(officePositionGroup.gameObject);
    }

    private void ChangeOfficer(OfficeObject o=null, Kingdom province=null)
    {
        ConfigData.CURRENT_SELECTED_OFFICE = o;
        SelectedMetas.selected_city = null;
        ScrollWindow.showWindow(nameof(ChangeUnitWindow));
    }
}