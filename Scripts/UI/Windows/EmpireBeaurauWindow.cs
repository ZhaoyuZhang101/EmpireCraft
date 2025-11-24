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
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine.Events;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General.UI.Window;
using UnityEngine.Pool;
using NeoModLoader.services;
using EmpireCraft.Scripts.UI.Components;

namespace EmpireCraft.Scripts.UI.Windows;
public class EmpireBeaurauWindow : AutoLayoutWindow<EmpireBeaurauWindow>
{
    public Empire _empire;
    public string culture = "Huaxia";
    AutoVertLayoutGroup topOfficeSpace;
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
    }

    public void ShowCoreSpace()
    {
        coreOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        SimpleText coreOfficeTitle = Instantiate(SimpleText.Prefab);
        coreOfficeTitle.Setup(LM.Get("CoreOffice"), TextAnchor.MiddleCenter);
        coreOfficeSpace.AddChild(coreOfficeTitle.gameObject);

        coreOfficeGroup = this.BeginGridGroup(2, pCellSize: new Vector2(100, 50));
        foreach (var oid in _empire.data.centerOffice.CoreOffices)
        {
            SetOfficeView(oid, ref coreOfficeGroup);
        }
        coreOfficeSpace.AddChild(coreOfficeGroup.gameObject);

        AddChild(coreOfficeSpace.gameObject);
    }
    /// <summary>
    /// 显示内阁
    /// </summary>
    public void ShowTopOfficeSpace()
    {
        topOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        topOfficeSpace.AddTextIntoVertLayout(LM.Get("TopOffice"), true, TextAnchor.MiddleCenter);
        
    }

    public void ShowDivisionSpace()
    {
        divisionsSpace = this.BeginVertGroup();
        //中央二级部门
        SimpleText divisionsTitle = Instantiate(SimpleText.Prefab);
        divisionsTitle.Setup(LM.Get("Divisions"), TextAnchor.MiddleCenter);
        divisionsSpace.AddChild(divisionsTitle.gameObject);

        divisionsGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize:new Vector2(100, 50));
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

        provincesGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
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
        _empire = EmpireCraftMetaTypeLibrary.selected_empire;
        Clear();
        // ShowTopOfficeSpace();

        ShowCoreSpace();

        ShowDivisionSpace();

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
        if (topOfficeSpace != null)
        {
            topOfficeSpace.gameObject.SetActive(false);
            Destroy(topOfficeSpace, deleteTime);
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

    public void SetOfficeView(long oid, ref AutoGridLayoutGroup parent, NanoObject o = null)
    {
        //寻找存在的官制
        if (!OfficeManager.Offices.TryGetValue(oid, out var officeObject))
        {
            return;
        }
        AutoHoriLayoutGroup officePositionGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:12, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 20));
        SimpleText title = Instantiate(SimpleText.Prefab);
        title.Setup(officeObject.GetOfficeName(o)+$"({officeObject.history_officers.Count})", TextAnchor.MiddleCenter, new Vector2(30, 10));
        title.background.enabled = false;

        LogService.LogInfo($"{officeObject.GetOfficeName()}: "+officeObject.actor_id);
        SimpleButton clickframe = UIHelper.CreateAvatarView(officeObject.actor_id);

        SimpleButton changeAvatar = Instantiate(SimpleButton.Prefab);
        changeAvatar.Setup(() => ChangeOfficer(officeObject), SpriteTextureLoader.getSprite("ui/changeOfficer"), pSize: new Vector2(20, 10));

        avatarLayoutGroup.AddChild(title.gameObject);
        avatarLayoutGroup.AddChild(clickframe.gameObject);
        avatarLayoutGroup.AddChild(changeAvatar.gameObject);
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        officePositionGroup.AddChild(avatarLayoutGroup.gameObject);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.UpperCenter);

        SimpleText nameText = GameObject.Instantiate(SimpleText.Prefab);
        nameText.Setup($"{LM.Get("i_name")}: {(officeObject.GetActor() == null ? "-" : officeObject.GetActor().data.name)}", pSize: new Vector2(50, 10));

        SimpleText levelText = GameObject.Instantiate(SimpleText.Prefab);
        levelText.Setup($"{LM.Get("OfficialLevel")}: {officeObject.GetName(o)}", pSize: new Vector2(50, 10));

        SimpleText timeText = GameObject.Instantiate(SimpleText.Prefab);
        timeText.Setup($"{LM.Get("i_on_office_time")}: {officeObject.GetOnTime()}", pSize: new Vector2(50, 10));


        leftVertGroup.AddChild(nameText.gameObject);
        leftVertGroup.AddChild(levelText.gameObject);
        leftVertGroup.AddChild(timeText.gameObject);
        leftVertGroup.transform.localPosition = Vector3.zero;
        officePositionGroup.AddChild(leftVertGroup.gameObject);

        parent.AddChild(officePositionGroup.gameObject);
        LogService.LogInfo($"加载官位{name}");

        pool.Add(officePositionGroup.gameObject);
    }

    private void ChangeOfficer(OfficeObject o=null, Kingdom province=null)
    {
        ConfigData.CURRENT_SELECTED_OFFICE = o;
        SelectedMetas.selected_city = null;
        LogService.LogInfo($"撤换{o}");
        ScrollWindow.showWindow(nameof(ChangeUnitWindow));
    }
}