using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Window;
using NeoModLoader.services;
using EmpireCraft.Scripts.UI.Components;

namespace EmpireCraft.Scripts.UI.Windows;
public class EmpireBeaurauWindow : AutoLayoutWindow<EmpireBeaurauWindow>
{
    private Empire _empire;
    public string culture = "Huaxia";
    private AutoVertLayoutGroup _topOfficeSpace;
    private AutoGridLayoutGroup _topOfficeGroup1;
    private AutoGridLayoutGroup _topOfficeGroup2;

    private AutoVertLayoutGroup _coreOfficeSpace;
    private AutoGridLayoutGroup _coreOfficeGroup;

    private AutoVertLayoutGroup _divisionsSpace;
    private AutoGridLayoutGroup _divisionsGroup;

    private AutoVertLayoutGroup _provincesSpace;
    private AutoGridLayoutGroup _provincesGroup;

    private readonly ListPool<GameObject> _pool = new ListPool<GameObject>();
    protected override void Init()
    {
    }

    public void ShowCoreSpace()
    {
        _coreOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        SimpleText coreOfficeTitle = Instantiate(SimpleText.Prefab);
        coreOfficeTitle.Setup(LM.Get("CoreOffice"), TextAnchor.MiddleCenter);
        _coreOfficeSpace.AddChild(coreOfficeTitle.gameObject);

        _coreOfficeGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
        foreach (var o in _empire.data.centerOffice.CoreOffices)
        {
            SetCenterOfficeView(o.Key, o.Value, ref _coreOfficeGroup);
        }
        _coreOfficeSpace.AddChild(_coreOfficeGroup.gameObject);

        AddChild(_coreOfficeSpace.gameObject);
    }

    public void ShowTopOfficeSpace()
    {
        _topOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        SimpleText topOfficeTitle = Instantiate( SimpleText.Prefab);
        topOfficeTitle.Setup(LM.Get("TopOffice"), TextAnchor.MiddleCenter);
        _topOfficeSpace.AddChild(topOfficeTitle.gameObject);

        _topOfficeGroup1 = this.BeginGridGroup(1, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
        OfficeObject obj = _empire.data.centerOffice.GreaterGeneral;
        SetCenterOfficeView(obj.name, obj, ref _topOfficeGroup1);
        
        _topOfficeGroup2 = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
        obj = _empire.data.centerOffice.Minister;
        SetCenterOfficeView(obj.name, obj, ref _topOfficeGroup2);
        obj = _empire.data.centerOffice.General;
        SetCenterOfficeView(obj.name, obj, ref _topOfficeGroup2);

        _topOfficeSpace.AddChild(_topOfficeGroup1.gameObject);
        _topOfficeSpace.AddChild(_topOfficeGroup2.gameObject);
    }

    public void ShowDivisionSpace()
    {
        _divisionsSpace = this.BeginVertGroup();
        //中央二级部门
        SimpleText divisionsTitle = Instantiate(SimpleText.Prefab);
        divisionsTitle.Setup(LM.Get("Divisions"), TextAnchor.MiddleCenter);
        _divisionsSpace.AddChild(divisionsTitle.gameObject);

        _divisionsGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize:new Vector2(100, 50));
        foreach (var o2 in _empire.data.centerOffice.Divisions)
        {
            SetCenterOfficeView(o2.Key, o2.Value, ref _divisionsGroup);
        }
        _divisionsSpace.AddChild(_divisionsGroup.gameObject);

        AddChild(_divisionsSpace.gameObject);
    }

    public void ShowProvincesSpace()
    {
        _provincesSpace = this.BeginVertGroup();
        //省级部门
        SimpleText provinceTitle = Instantiate(SimpleText.Prefab);
        provinceTitle.Setup(LM.Get("province"), TextAnchor.MiddleCenter);
        _provincesSpace.AddChild(provinceTitle.gameObject);

        _provincesGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
        _empire.ProvinceList = _empire.ProvinceList.Distinct().ToList();
        foreach (ModObject province in _empire.ProvinceList)
        {
            if(!province.data.is_set_to_country)
            {
                SetProvinceView(province, ref _provincesGroup);
            }
        }

        _provincesSpace.AddChild(_provincesGroup.gameObject);

        AddChild(_provincesSpace.gameObject);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        _empire = ConfigData.CURRENT_SELECTED_EMPIRE;
        Clear();
        ShowTopOfficeSpace();

        ShowCoreSpace();

        ShowDivisionSpace();

        ShowProvincesSpace();
    }
    public void Clear()
    {

        if (_pool == null) return;
        float deleteTime = 0.1f;
        foreach (GameObject go in _pool)
        {
            go.SetActive(false);
            Destroy(go, deleteTime);
            deleteTime += 0.1f;
        }
        if (_topOfficeSpace != null)
        {
            _topOfficeSpace.gameObject.SetActive(false);
            Destroy(_topOfficeSpace, deleteTime);
        }
        if (_coreOfficeSpace != null)
        {
            _coreOfficeSpace.gameObject.SetActive(false);
            Destroy(_coreOfficeSpace, deleteTime);
        }
        if (_divisionsSpace != null)
        {
            _divisionsSpace.gameObject.SetActive(false);
            Destroy(_divisionsSpace, deleteTime);
        }
        if (_provincesSpace != null)
        {
            _provincesSpace.gameObject.SetActive(false);
            Destroy(_provincesSpace, deleteTime);
        }
        _pool.Clear();
    }

    public void SetCenterOfficeView(string name, OfficeObject officeObject, ref AutoGridLayoutGroup parent)
    {
        AutoHoriLayoutGroup officePositionGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:12, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 20));
        LogService.LogInfo($"官职名称{name}");
        SimpleText title = Instantiate(SimpleText.Prefab);
        title.Setup(LM.Get(name)+$"({officeObject.history_officers.Count})", TextAnchor.MiddleCenter, new Vector2(30, 10));
        title.background.enabled = false;


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
        levelText.Setup($"{LM.Get("OfficialLevel")}: {LM.Get(String.Join("_", culture, officeObject.level.ToString()))}", pSize: new Vector2(50, 10));

        SimpleText timeText = GameObject.Instantiate(SimpleText.Prefab);
        timeText.Setup($"{LM.Get("i_on_office_time")}: {officeObject.GetOnTime()}", pSize: new Vector2(50, 10));


        leftVertGroup.AddChild(nameText.gameObject);
        leftVertGroup.AddChild(levelText.gameObject);
        leftVertGroup.AddChild(timeText.gameObject);
        leftVertGroup.transform.localPosition = Vector3.zero;
        officePositionGroup.AddChild(leftVertGroup.gameObject);

        parent.AddChild(officePositionGroup.gameObject);
        LogService.LogInfo($"加载官位{name}");

        _pool.Add(officePositionGroup.gameObject);
    }
    public void SetProvinceView(ModObject province, ref AutoGridLayoutGroup parent)
    {
        AutoHoriLayoutGroup provinceGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:12, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 20));

        SimpleText title = Instantiate(SimpleText.Prefab);
        string text = province.data.name + $"({province.data.history_officers.Count})";
        if (province.IsTotalVassaled()) 
        {
            text = LM.Get("provinceVassaled") + "|" + text;
        }
        title.Setup(text, TextAnchor.MiddleCenter, new Vector2(30, 10));
        title.background.enabled = false;

        long actor_id = -1L;
        if (province.HasOfficer())
        {
            actor_id = province.Officer.getID();
        } else
        {
            actor_id = -1L;
        }
        SimpleButton clickframe = UIHelper.CreateAvatarView(actor_id);

        SimpleButton changeAvatar = Instantiate(SimpleButton.Prefab);
        changeAvatar.Setup(() => ChangeOfficer(province:province), SpriteTextureLoader.getSprite("ui/changeOfficer"), pSize: new Vector2(20, 10));

        avatarLayoutGroup.AddChild(title.gameObject);
        avatarLayoutGroup.AddChild(clickframe.gameObject);
        avatarLayoutGroup.AddChild(changeAvatar.gameObject);
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        provinceGroup.AddChild(avatarLayoutGroup.gameObject);

        //左边信息栏
        AutoVertLayoutGroup leftVertGroup = this.BeginVertGroup(pAlignment: TextAnchor.UpperCenter);

        SimpleText nameText = GameObject.Instantiate(SimpleText.Prefab);
        nameText.Setup($"{LM.Get("i_name")}: {(!province.HasOfficer() ? "-" : province.Officer.data.name)}", pSize: new Vector2(50, 10));

        SimpleText levelText = GameObject.Instantiate(SimpleText.Prefab);
        levelText.Setup($"{LM.Get("OfficialLevel")}: {LM.Get(String.Join("_", culture, province.data.officialLevel.ToString()))}", pSize: new Vector2(50, 10));

        SimpleText timeText = GameObject.Instantiate(SimpleText.Prefab);
        timeText.Setup($"{LM.Get("i_on_office_time")}: {province.GetNewOfficerOnTime()}", pSize: new Vector2(50, 10));


        leftVertGroup.AddChild(nameText.gameObject);
        leftVertGroup.AddChild(levelText.gameObject);
        leftVertGroup.AddChild(timeText.gameObject);
        leftVertGroup.transform.localPosition = Vector3.zero;
        provinceGroup.AddChild(leftVertGroup.gameObject);

        parent.AddChild(provinceGroup.gameObject);
        LogService.LogInfo($"加载官位{province.data.name}");

        _pool.Add(provinceGroup.gameObject);
    }

    private void ChangeOfficer(OfficeObject o=null, ModObject province=null)
    {
        if (o != null)
        {
            ConfigData.CURRENT_SELECTED_OFFICE = o.name;
            ConfigData.CurrentSelectedModObject = null;
            LogService.LogInfo($"撤换{o.name}");
            ScrollWindow.showWindow(nameof(ChangeUnitWindow));
        }
        else if (province !=null)
        {
            ConfigData.CURRENT_SELECTED_OFFICE = "";
            ConfigData.CurrentSelectedModObject = province;
            ScrollWindow.showWindow(nameof(ChangeUnitWindow));
        }
    }
}