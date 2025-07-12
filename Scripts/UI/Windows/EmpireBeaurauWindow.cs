using EmpireCraft.Scripts.HelperFunc;
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
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General.UI.Window;
using UnityEngine.Pool;
using NeoModLoader.services;

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
    AutoVertLayoutGroup provincesGroup;
    [Header("UI Prefab & 根容器")]
    public GameObject _itemPrefab;
    public List<GameObject> pool = new List<GameObject>();
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

        coreOfficeGroup = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
        foreach (var o in _empire.data.centerOffice.CoreOffices)
        {
            SetCenterOfficeView(o.Key, o.Value, ref coreOfficeGroup);
        }
        coreOfficeSpace.AddChild(coreOfficeGroup.gameObject);

        AddChild(coreOfficeSpace.gameObject);
    }

    public void ShowTopOfficeSpace()
    {
        topOfficeSpace = this.BeginVertGroup();
        //中央核心部门
        SimpleText topOfficeTitle = Instantiate( SimpleText.Prefab);
        topOfficeTitle.Setup(LM.Get("TopOffice"), TextAnchor.MiddleCenter);
        topOfficeSpace.AddChild(topOfficeTitle.gameObject);

        topOfficeGroup1 = this.BeginGridGroup(1, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
        SetCenterOfficeView(String.Join("_",culture, "officiallevel_1"), _empire.data.centerOffice.GreaterGeneral, ref topOfficeGroup1);

        topOfficeGroup2 = this.BeginGridGroup(2, GridLayoutGroup.Constraint.FixedColumnCount, pCellSize: new Vector2(100, 50));
        SetCenterOfficeView(String.Join("_", culture, "officiallevel_2"), _empire.data.centerOffice.Minister, ref topOfficeGroup2);
        SetCenterOfficeView(String.Join("_", culture, "officiallevel_3"), _empire.data.centerOffice.General, ref topOfficeGroup2);

        topOfficeSpace.AddChild(topOfficeGroup1.gameObject);
        topOfficeSpace.AddChild(topOfficeGroup2.gameObject);
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
            SetCenterOfficeView(o2.Key, o2.Value, ref divisionsGroup);
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

        provincesGroup = this.BeginVertGroup();

        provincesSpace.AddChild(provinceTitle.gameObject);
        provincesSpace.AddChild(provincesGroup.gameObject);

        AddChild(provincesSpace.gameObject);
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
        if (topOfficeSpace!=null)
        {
            topOfficeSpace.gameObject.SetActive(false);
            Destroy(topOfficeSpace);
        }
        if (coreOfficeSpace != null) 
        {
            coreOfficeSpace.gameObject.SetActive(false);
            Destroy(coreOfficeSpace);
        }
        if (divisionsSpace != null)
        {
            divisionsSpace.gameObject.SetActive(false);
            Destroy(divisionsSpace);
        }
        if (provincesSpace != null)
        {
            provincesSpace.gameObject.SetActive(false);
            Destroy(provincesSpace);
        }
        if (pool == null) return;
        float deleteTime = 0.2f;
        foreach (GameObject go in pool)
        {
            go.SetActive(false);
            Destroy(go, deleteTime);
            deleteTime += 0.2f;
        }
        pool.Clear();
    }

    public void actorClick(Actor actor)
    {
        if (actor != null) 
        {
            ActionLibrary.openUnitWindow(actor);
        }
        LogService.LogInfo("点击角色");
    }

    public void SetCenterOfficeView(string name, OfficeObject officeObject, ref AutoGridLayoutGroup parent)
    {
        AutoHoriLayoutGroup officePositionGroup = this.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);

        //右边头像
        AutoVertLayoutGroup avatarLayoutGroup = this.BeginVertGroup(new Vector2(30, 30), pSpacing:5, pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(0, 0, 0, 20));

        SimpleText title = Instantiate(SimpleText.Prefab);
        title.Setup(LM.Get(name)+$"({officeObject.history_officers.Count})", TextAnchor.MiddleCenter, new Vector2(30, 10));
        title.background.enabled = false;
        UnitAvatarLoader pPrefab = Resources.Load<UnitAvatarLoader>("ui/AvatarLoaderFramed");
        UnitAvatarLoader unit_loader = GameObject.Instantiate<UnitAvatarLoader>(pPrefab);
        Transform frame = unit_loader.transform.Find("frame");
        Actor actor = World.world.units.get(officeObject.actor_id);
        if (frame != null)
        {

            Button button = frame.gameObject.AddComponent<Button>();

            button.onClick.AddListener(() => {
                Debug.Log("Frame按钮被点击");
                actorClick(actor);
            });

            button.interactable = true;

            // 如果需要改变按钮的视觉效果
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            button.colors = colors;
        }
        else
        {
            Debug.LogError("找不到frame对象！");
        }
        if (actor != null)
        {
            unit_loader._actor_image.gameObject.SetActive(true);
            unit_loader.load(actor);
        } else
        {
            unit_loader._actor_image.gameObject.SetActive(false);
        }

        SimpleButton changeAvatar = Instantiate(SimpleButton.Prefab);
        changeAvatar.Setup(() => ChangeOfficer(name), SpriteTextureLoader.getSprite("ui/changeOfficer"), pSize: new Vector2(20, 10));

        avatarLayoutGroup.AddChild(title.gameObject);
        avatarLayoutGroup.AddChild(unit_loader.gameObject);
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

        pool.Add(officePositionGroup.gameObject);
    }

    private void ChangeOfficer(string name)
    {
    }
}