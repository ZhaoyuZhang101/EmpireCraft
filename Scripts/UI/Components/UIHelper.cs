using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EmpireCraft.Scripts.UI.Components;
public static class UIHelper
{
    /// <summary>
    /// 在运行时动态创建一个 Panel（Image）并挂到 Canvas 下
    /// </summary>
    public static RectTransform CreateUIPanel(string name, Transform parentCanvas)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        RectTransform rt = go.GetComponent<RectTransform>();

        rt.SetParent(parentCanvas, false);

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;

        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0);

        return rt;
    }
    public static void ClearChildren(this Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(parent.GetChild(i).gameObject);
        }
    }
    /// <summary>
    /// 在 parent 下创建一个 VerticalLayoutGroup 容器
    /// </summary>
    public static RectTransform CreateVerticalLayoutContainer(string name, Vector2 size)
    {
        // 1. 新 GameObject：它自带 RectTransform、CanvasRenderer、Image（可无）、VerticalLayoutGroup
        var go = new GameObject(name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),               // 如果你想给它一个背景色／图块
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)    // 自动根据内部内容调整尺寸
        );

        // 2. 设置到父节点，并保持本地坐标不变
        var rt = go.GetComponent<RectTransform>();

        // 3. 刚创建的 RectTransform 默认 anchorMin=anchorMax=(0.5,0.5)，pivot=(0.5,0.5)
        //    这里举例让它铺满整个父容器（根据需求自行调整）
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        // 4. 配置 VerticalLayoutGroup
        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;  // 子元素对齐方式
        vlg.spacing = 10;                             // 元素之间的间隔
        vlg.padding = new RectOffset(5, 5, 5, 5);        // 容器四周留白
        vlg.childForceExpandWidth = false;            // 强制子物体宽度填满
        vlg.childForceExpandHeight = false;           // 不自动撑高
        vlg.childControlWidth = false;                 // 允许它控制子宽度
        vlg.childControlHeight = false;                // 允许它控制子高度

        rt.sizeDelta = size;

        // 5. 配置 ContentSizeFitter （如果你想要容器随内容增高／减高）
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 6. （可选）给背景上个半透明，方便调试和视觉分层
        var img = go.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0);

        return rt;
    }

    /// <summary>
    /// 在 parent 下创建一个 HorizontalLayoutGroup 容器
    /// </summary>
    public static RectTransform CreateHorizontalLayoutContainer(string name, Vector2 size)
    {
        // 1. 新 GameObject：它自带 RectTransform、CanvasRenderer、Image（可无）、HorizontalLayoutGroup
        var go = new GameObject(name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),               // 如果你想给它一个背景色／图块
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter)    // 自动根据内部内容调整尺寸
        );

        // 2. 设置到父节点，并保持本地坐标不变
        var rt = go.GetComponent<RectTransform>();

        // 3. 刚创建的 RectTransform 默认 anchorMin=anchorMax=(0.5,0.5)，pivot=(0.5,0.5)
        //    这里举例让它铺满整个父容器（根据需求自行调整）
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        // 4. 配置 HorizontalLayoutGroup
        var vlg = go.GetComponent<HorizontalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;  // 子元素对齐方式
        vlg.spacing = 10;                             // 元素之间的间隔
        vlg.padding = new RectOffset(5, 5, 5, 5);        // 容器四周留白
        vlg.childForceExpandWidth = false;            // 强制子物体宽度填满
        vlg.childForceExpandHeight = true;           // 不自动撑高
        vlg.childControlWidth = false;                 // 允许它控制子宽度
        vlg.childControlHeight = true;                // 允许它控制子高度

        rt.sizeDelta = size;

        // 5. 配置 ContentSizeFitter （如果你想要容器随内容增高／减高）
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.MinSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.MinSize;

        // 6. （可选）给背景上个半透明，方便调试和视觉分层
        var img = go.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0);

        return rt;
    }
    public static void actorClick(Actor actor)
    {
        if (actor != null)
        {
            ActionLibrary.openUnitWindow(actor);
        }
        LogService.LogInfo("点击角色");
    }
    [Hotfixable]
    public static SimpleButton CreateAvatarView(long actor_id, UnityAction action=null, bool pIsAlive=true)
    {
        UnitAvatarLoader pPrefab = Resources.Load<UnitAvatarLoader>($"ui/AvatarLoaderFramed");
        SimpleButton clickFrame = UnityEngine.Object.Instantiate(SimpleButton.Prefab);
        UnitAvatarLoader unitLoader = UnityEngine.Object.Instantiate(pPrefab, clickFrame.transform, true);
        RectTransform rt = clickFrame.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        clickFrame.Icon.raycastTarget = true;

        Actor actor = World.world.units.get(actor_id);
        if (action == null)
        {
            clickFrame.Setup(() => actorClick(actor), SpriteTextureLoader.getSprite(""), pSize: new Vector2(30, 30));
        }
        else
        {
            clickFrame.Setup(action, SpriteTextureLoader.getSprite(""), pSize: new Vector2(30, 30));
        }
        clickFrame.Background.color = new Color(0, 0, 0, 0.0f);
        clickFrame.Icon.color = new Color(0, 0, 0, 0.0f);
        if (actor != null)
        {
            clickFrame.Button.OnHover(() =>
            {
                actor.showTooltip(unitLoader);
            });
            clickFrame.Button.OnHoverOut(Tooltip.hideTooltip);
            unitLoader._actor_image.gameObject.SetActive(true);
            unitLoader.load(actor);
        }
        else
        {
            unitLoader._actor_image.gameObject.SetActive(false);
        }
        if (!pIsAlive)
        {
            unitLoader._actor_image.sprite = SpriteTextureLoader.getSprite("ui/deadIcon");
            unitLoader._actor_image.transform.localScale = new Vector2(1, 1);
            var rt1 = unitLoader._actor_image.rectTransform;            // shortcut for GetComponent<RectTransform>()
            rt1.anchorMin       = new Vector2(0.5f, 0.5f);
            rt1.anchorMax       = new Vector2(0.5f, 0.5f);
            rt1.pivot           = new Vector2(0.5f, 0.5f);
            rt1.anchoredPosition = Vector2.up*1;    // 正中央
            rt1.localScale      = Vector3.one;      // 保持原始大小
            unitLoader._actor_image.gameObject.SetActive(true);
        }

        unitLoader.transform.SetAsLastSibling();
        unitLoader.transform.localScale = new Vector2(1.2f, 1.2f);
        return clickFrame;
    }
    public static TextInput GenerateTextInput(this Transform parent, Vector2 size=default, Vector2 offset=default, string default_text = "", UnityAction<string> action = null, TextInput input=null)
    {
        Vector2 dSize = size==default?new Vector2(130, 15):size;
        TextInput inputComp;
        if (input == null)
        {
            inputComp = GameObject.Instantiate(TextInput.Prefab, parent);
        }
        else
        {
            inputComp = input;
        }

        if (action != null)
        {
            inputComp.Setup("", action);
        }
        inputComp.SetSize(dSize);
        inputComp.input.textComponent.alignment = TextAnchor.MiddleCenter;
        var rt1 = inputComp.input.GetComponent<RectTransform>();
        rt1.sizeDelta = dSize;
        if (offset != default)
        {
            inputComp.transform.localPosition = Vector3.up*offset.y+Vector3.left*offset.x;
        }
        inputComp.input.text = default_text;

        if (inputComp.input.placeholder == null)
        {
            inputComp.input.SetupPlaceholder(inputComp.text.font,"请输入您的内容", Color.yellow);
        }
        inputComp.transform.SetAsLastSibling();
        return inputComp;
    }

    public static AutoVertLayoutGroup AddActorViewIntoVertLayout(this AutoVertLayoutGroup layout, Actor actor, SimpleButton button=null, string description="")
    {
        AutoVertLayoutGroup avatarLayoutGroup = layout.BeginVertGroup(new Vector2(30, 30), pSpacing:15, pAlignment: TextAnchor.MiddleCenter);

        long id = actor?.getID() ?? -1L;
        SimpleButton clickFrame = CreateAvatarView(id, pIsAlive:true);
        if (button != null)
        {
            avatarLayoutGroup.AddChild(button.gameObject);
        }
        avatarLayoutGroup.AddChild(clickFrame.gameObject);
        if (description != "")
        {
            avatarLayoutGroup.AddTextIntoVertLayout("\n\n"+description, true, TextAnchor.MiddleCenter, new Vector2(20, 20), ignorePosition:true);
        }
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        return avatarLayoutGroup;
    }

    public static void ShowTrait(this Transform parent, string traitName)
    {
        ActorTraitButton traitButton = Resources.Load<ActorTraitButton>($"ui/unit_window_elements/ActorTraitButton");
        var go = Object.Instantiate(traitButton.gameObject, parent, false);
        var comp = go.GetComponent<ActorTraitButton>();
        comp.load(traitName);
        comp.unlockElement();
        comp.fillTooltipData(AssetManager.traits.get(traitName));
        if (go.TryGetComponent(out DraggableLayoutElement component))
        {
            component.enabled = false;
        }
    }
    public static void AddTraitIntoVertLayout(this AutoVertLayoutGroup parent, string traitName)
    {
        parent.transform.ShowTrait(traitName);
    }
    public static void AddTraitIntoHoriLayout(this AutoHoriLayoutGroup parent, string traitName)
    {
        parent.transform.ShowTrait(traitName);
    }
    [Hotfixable]
    public static void ShowActorPowerCard(this Actor pActor, OfficerPowerType pPower, AutoHoriLayoutGroup parent, Empire empire, UnityAction action = null)
    {
        var contentSpace = parent.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
        contentSpace.AddTextIntoVertLayout(pActor.name, hideBackground:true, TextAnchor.MiddleCenter);
        var actorSpace = contentSpace.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        actorSpace.AddActorViewIntoHoriLayout(pActor);
        actorSpace.AddButtonIntoHoriLayout("add_member", "", action: action, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(8, 8));
        contentSpace.AddTextIntoVertLayout(pActor.IsOnOffice() ? (pActor.GetOffice()?.GetName()??"无") : "无", hideBackground:true, TextAnchor.MiddleCenter);
        EmpireAddition personalAddition = new EmpireAddition();
        foreach (var power in OfficeManager.AllPower)
        {
            var addition = pActor.CalcPower(power, empire);
            personalAddition.Add(addition);
        }
        contentSpace.AddTextIntoVertLayout($"{pPower.ToString()}: {personalAddition.addition[pPower].ToString().ColorString(pColor:personalAddition.addition[pPower]>=0? Color.green : Color.red)}", 
            anchor: TextAnchor.MiddleCenter, size: new Vector2(80, 15));
        contentSpace.transform.AddStretchBackground("FactionFrame", size: new Vector2(85, 90));
    }
    [Hotfixable]
    public static void ShowActorPerformanceCard(this Actor pActor, AutoHoriLayoutGroup parent, UnityAction action = null)
    {
        var identity = pActor.GetIdentity();
        if (identity == null) return;
        var contentSpace = parent.BeginVertGroup(pAlignment:TextAnchor.MiddleCenter);
        contentSpace.AddTextIntoVertLayout(pActor.name, hideBackground:true, TextAnchor.MiddleCenter);
        var actorSpace = contentSpace.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        actorSpace.AddActorViewIntoHoriLayout(pActor);
        actorSpace.AddButtonIntoHoriLayout("add_member", "", action: action, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(8, 8));
        contentSpace.AddTextIntoVertLayout(pActor.IsOnOffice() ? (pActor.GetOffice()?.GetName()??"无") : "无", hideBackground:true, TextAnchor.MiddleCenter);
        contentSpace.AddTextIntoVertLayout($"绩效值: {(int)identity.TotalPerformance}", anchor: TextAnchor.MiddleCenter, size: new Vector2(80, 15));
        contentSpace.transform.AddStretchBackground("FactionFrame", size: new Vector2(85, 90));
    }
    public static AutoVertLayoutGroup AddActorViewIntoHoriLayout(this AutoHoriLayoutGroup layout, Actor actor, SimpleButton button=null, string description="")
    {
        AutoVertLayoutGroup avatarLayoutGroup = layout.BeginVertGroup(new Vector2(30, 30), pSpacing:15, pAlignment: TextAnchor.MiddleCenter);

        long id = actor?.getID() ?? -1L;
        SimpleButton clickFrame = CreateAvatarView(id, pIsAlive:true);
        if (button != null)
        {
            avatarLayoutGroup.AddChild(button.gameObject);
        }
        avatarLayoutGroup.AddChild(clickFrame.gameObject);
        if (description != "")
        {
            avatarLayoutGroup.AddTextIntoVertLayout("\n\n"+description, true, TextAnchor.MiddleCenter, new Vector2(20, 20), ignorePosition:true);
        }
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        return avatarLayoutGroup;
    }
    public static SimpleText AddTextIntoVertLayout(this AutoVertLayoutGroup layout, string text, bool hideBackground=false, TextAnchor anchor=TextAnchor.MiddleLeft, Vector2 size=default, bool ignorePosition=false, HorizontalWrapMode mode= HorizontalWrapMode.Overflow)
    {
        SimpleText timeText = Object.Instantiate(SimpleText.Prefab, layout.transform);
        timeText.Setup(text, pSize: size==default?new Vector2(50, 10):size, pAlignment:anchor);
        if (hideBackground)
        {
            timeText.background.enabled = false;
        }

        if (ignorePosition)
        {
            var le = timeText.GetComponent<LayoutElement>() ?? timeText.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            // 关键：让鼠标穿透
            foreach (var g in timeText.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
        }

        return timeText;
    }
    public static SimpleText AddTextIntoGridLayout(this AutoGridLayoutGroup layout, string text, bool hideBackground=false, TextAnchor anchor=TextAnchor.MiddleLeft, Vector2 size=default, bool ignorePosition=false, HorizontalWrapMode mode= HorizontalWrapMode.Overflow)
    {
        SimpleText timeText = Object.Instantiate(SimpleText.Prefab, layout.transform);
        timeText.Setup(text, pSize: size==default?new Vector2(50, 10):size, pAlignment:anchor);
        if (hideBackground)
        {
            timeText.background.enabled = false;
        }

        if (ignorePosition)
        {
            var le = timeText.GetComponent<LayoutElement>() ?? timeText.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            // 关键：让鼠标穿透
            foreach (var g in timeText.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
        }

        return timeText;
    }
    public static SimpleText AddTextIntoHoriLayout(this AutoHoriLayoutGroup layout, string text, bool hideBackground=false, TextAnchor anchor=TextAnchor.MiddleLeft, Vector2 size=default, bool ignorePosition=false, HorizontalWrapMode mode= HorizontalWrapMode.Overflow)
    {
        SimpleText timeText = Object.Instantiate(SimpleText.Prefab, layout.transform);
        timeText.Setup(text, pSize: size==default?new Vector2(50, 10):size, pAlignment:anchor);
        if (hideBackground)
        {
            timeText.background.enabled = false;
        }
        if (ignorePosition)
        {
            var le = timeText.GetComponent<LayoutElement>() ?? timeText.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            // 关键：让鼠标穿透
            foreach (var g in timeText.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
        }
        
        return timeText;
    }

    public static AdvancedButton AddButtonIntoVertLayout(this AutoVertLayoutGroup layout, string buttonID, string text="", UnityAction action=null, Sprite icon=null, Sprite background=null, Vector2 size=default, bool isToggle=false, bool showTip=false, bool hideBackground=false, bool customIcon=false, int iconType=0)
    {
        AdvancedButton button = Object.Instantiate(AdvancedButton.Prefab, layout.transform);
        button.Setup(buttonID, action, icon, text, size, backgroundSprite:background, isToggle:isToggle,  showTip:showTip, iconType:iconType);
        button.Background.enabled = !hideBackground;
        return button;
    }    
    public static AdvancedButton AddNormalOptionIntoHori(this Transform parent, AutoHoriLayoutGroup container, string title, UnityAction action, bool option, bool hasIcon=false, bool isOption=false, int index = -1, Vector2 size=default, bool hasTitle=true)
    {
        int toggleType;
        if (!isOption)
        {
            toggleType = 1;
            if (hasTitle)
            {
                container.AddTextIntoHoriLayout(LM.Get(title), hideBackground:true);
            }
        }
        else
        {
            toggleType = 0;
        }
        var button = container.AddButtonIntoHoriLayout(isOption?title+index:title, isToggle:true, size:size==default?new Vector2(15, 15):size, showTip:true, customIcon:hasIcon, iconType:toggleType);
        button.Button.onClick.RemoveAllListeners();
        button.Button.onClick.AddListener(action);
        button.SetStatus(option);
        button.Background.enabled = false;
        container.transform.SetParent(parent);
        return button;
    }
    public static AdvancedButton AddNormalOptionIntoVert(this Transform parent, AutoVertLayoutGroup container, string title, UnityAction action, bool option, bool hasIcon=false, bool isOption=false, int index = -1, Vector2 size=default, bool hasTitle=true)
    {
        int toggleType;
        if (!isOption)
        {
            toggleType = 1;
            if (hasTitle)
            {
                container.AddTextIntoVertLayout(LM.Get(title), hideBackground:true);
            }
        }
        else
        {
            toggleType = 0;
        }
        var button = container.AddButtonIntoVertLayout(isOption?title+index:title, isToggle:true, size:size==default?new Vector2(15, 15):size, showTip:true, customIcon:hasIcon, iconType:toggleType);
        button.Button.onClick.RemoveAllListeners();
        button.Button.onClick.AddListener(action);
        button.SetStatus(option);
        button.Background.enabled = false;
        container.transform.SetParent(parent);
        return button;
    }
    public static List<AdvancedButton> AddMultipleOption(this Transform parent, AutoHoriLayoutGroup container, string title, UnityAction<string, int> action, int option, int num, bool hasIcon=false)
    {
        List<AdvancedButton> options = new List<AdvancedButton>();
        container.AddTextIntoHoriLayout(LM.Get(title), hideBackground:true);
        for (int i = 0; i < num; i++)
        {
            int index = i; 
            var isOn = option == i;
            var button = parent.AddNormalOptionIntoHori(container, title, ()=>action(title, index), isOn, hasIcon, true, index, size:new Vector2(10, 10));
            options.Add(button);
        }
        return options;
    }
    public static AdvancedButton AddButtonIntoHoriLayout(this AutoHoriLayoutGroup layout, string buttonID, string text="", UnityAction action=null, Sprite icon=null, 
        Sprite background=null, Vector2 size=default, bool isToggle=false, bool showTip=false, bool customIcon=false, int iconType=0, bool hideBackground=false)
    {
        AdvancedButton button = GameObject.Instantiate(AdvancedButton.Prefab, layout.transform);
        button.Setup(buttonID, action, icon, text, size, backgroundSprite: background, isToggle: isToggle,
            showTip: showTip, customIcon: customIcon, iconType: iconType);
        button.Background.enabled = !hideBackground;
        return button;
    }
    public static AdvancedButton AddButtonIntoGirdLayout(this AutoGridLayoutGroup layout, string buttonID, string text="", UnityAction action=null, Sprite icon=null, 
        Sprite background=null, Vector2 size=default, bool isToggle=false, bool showTip=false, bool customIcon=false, int iconType=0, bool hideBackground=false)
    {
        AdvancedButton button = GameObject.Instantiate(AdvancedButton.Prefab, layout.transform);
        button.Setup(buttonID, action, icon, text, size, backgroundSprite:background, isToggle:isToggle,  showTip:showTip, customIcon:customIcon, iconType:iconType);
        button.Background.enabled = !hideBackground;
        return button;
    }
    public static void SetupPlaceholder(this InputField inputField, Font font, string placeholderText, Color color)
    {
        // 1. 创建 Placeholder 子物体
        var go = new GameObject("Placeholder", typeof(RectTransform));
        go.transform.SetParent(inputField.transform, false);

        // 2. 拉伸整个区域
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // 3. 加 Text 组件做占位
        var txt = go.AddComponent<Text>();
        txt.font = font;
        txt.text = placeholderText;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleLeft;    // 根据你的需求调整
        txt.fontStyle = FontStyle.Italic;

        // 4. 把它赋给 InputField.placeholder
        inputField.placeholder = txt;

        // 5. （可选）为了风格一致，也可以把这个 Text 拖到 inputField.textComponent 的兄弟顺序下
        go.transform.SetAsFirstSibling();
    }
    /// <summary>
    /// 派系显示组件
    /// </summary>
    /// <param name="groups">UI区块组</param>
    /// <param name="layout">决定派系卡片的排列方式</param>
    /// <param name="kingdom">派系所属国家</param>
    public static void InitialFactionSpace(AutoHoriLayoutGroup layout, Kingdom kingdom, List<GameObject> groups=null)
    {
        var factionSpace = layout;
        foreach (FixedFaction faction in kingdom.GetRegime().GetPlayerFactions())
        {
            AddFactionCard(faction, kingdom, factionSpace);
        }
        
        if (kingdom.GetRegime().GetPlayerFactions().Count < 3)
        {
            for (int i = 0; i < 3-kingdom.GetRegime().GetPlayerFactions().Count; i++)
            {
                var addFaction = factionSpace.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(55, 90));
                addFaction.AddButtonIntoVertLayout("add_faction", "", () => OpenSelectionWindow(kingdom), SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(15, 15));
                addFaction?.transform.AddStretchBackground("FactionFrame", size: new Vector2(55, 90));
            }
        }
        factionSpace.transform.AddStretchBackground("regimeFrame", size:new Vector2(180, 100));
        kingdom.GetRegime().FactionSpace = factionSpace;
        var bottom = factionSpace.BeginHoriGroup(pAlignment: TextAnchor.MiddleCenter);
        bottom.AddButtonIntoHoriLayout("recover_faction", "", () =>
        {
            factionSpace.transform.ClearChildren();
            kingdom.GetRegime().RecoverFactions();
            InitialFactionSpace(factionSpace, kingdom, groups);
            var res = FactionManager.Save();
            ActionLibrary.showWhisperTip(res ? "save_success" : "save_failed");
        }, SpriteTextureLoader.getSprite("ui/changeOfficer"), size: new Vector2(10, 10), showTip:true);
        bottom.AddButtonIntoHoriLayout("save_faction", "", () =>
        {
            FactionManager.Config.PlayerRegimeFactions[kingdom.GetRegime().type] = kingdom.GetRegime().GetPlayerFactions().Select(f=>f.DeepClone()).ToList();
            var res = FactionManager.Save();
            ActionLibrary.showWhisperTip(res ? "save_success" : "save_failed");
        }, SpriteTextureLoader.getSprite("ui/icons/iconSaveLocal"), size: new Vector2(10, 10), showTip:true);
        bottom?.gameObject.AdjustTopPart(bottom.transform, Vector2.up*8);
        groups?.Add(factionSpace.gameObject);
    }
    /// <summary>
    /// 单个派系显示组件
    /// </summary>
    /// <param name="groups">UI区块组</param>
    /// <param name="layout">决定派系卡片的排列方式</param>
    /// <param name="kingdom">派系所属国家</param>
    public static void AddFactionCard(FixedFaction faction, Kingdom kingdom, AutoHoriLayoutGroup parentH = null, AutoVertLayoutGroup parentV = null, bool addMode = false, UnityAction action=null)
    {
        if (parentH == null&&parentV==null) return;
        var isDominate = kingdom.GetRegime().GetDominateFaction() == faction;
        var factionPart = parentH?.BeginVertGroup(pSpacing:-3)??parentV?.BeginVertGroup(pSpacing:-3);
        factionPart.AddTextIntoVertLayout(faction.Name+$"{(kingdom.IsEmpire()?isDominate?"(主导)".ColorString(pColor:new Color(0.0f, 1, 0.5f)):"":"(未激活)".ColorString(pColor:new Color(0.8f, 0, 0.2f)))}", true, TextAnchor.LowerCenter);
        factionPart.AddActorViewIntoVertLayout(faction.GetLeader());
        factionPart.AddTextIntoVertLayout($"人数：{faction.Count}\n", true, TextAnchor.MiddleCenter);
        factionPart.AddTextIntoVertLayout($"综合力量：{faction.TotalPower}\n", true, TextAnchor.MiddleCenter);
        var content = "<核心诉求>\n";
        foreach (var tempFac in faction.TemporaryFactions)
        {
            if (!tempFac.Hide||tempFac.IsStarted())
            {
                var startContent = $"\n执行中:({(int)((tempFac.progress/(tempFac.progressMax))*100.0f)}/100)";
                content += tempFac.type.ToString().ColorString(pColor:new Color(0.7f, 0.9f, tempFac.IsStarted()?0.1f:0.9f))+(tempFac.IsStarted()?startContent:"")+$"{(!tempFac.Active?"(未激活)":"")}"+"\n";
            }
        }
        factionPart.AddTextIntoVertLayout(content, true, TextAnchor.UpperCenter, size: new Vector2(30, 40));
        var bottom = factionPart.BeginHoriGroup();
        if (!addMode)
        {
            var button = factionPart?.transform.AddNormalOptionIntoHori(bottom, "LockFaction", () =>
            {
                if (faction.Force)
                {
                    faction.Force = false;
                }
                else
                {
                    faction.Force = true;
                    foreach (var f in kingdom.GetRegime().GetPlayerFactions())
                    {
                        if (f != faction)
                        {
                            f.LockButton?.SetStatus(false);
                            f.Force = false;
                        }
                    }
                }
                faction.LockButton.SetStatus(faction.Force);
            }, faction.Force, isOption:true, size: new Vector2(10, 10));
            faction.LockButton = button;
        }
        bottom.AddButtonIntoHoriLayout("EnterFactionCard", "详情", () =>
        {
            ConfigData.CURRENT_SELECTED_FACTION = faction;
            SelectedMetas.selected_kingdom = kingdom;   
            ScrollWindow.showWindow(nameof(FactionDetailWindow));
        }, size: new Vector2(20, 10));
        if (addMode)
        {
            bottom.AddButtonIntoHoriLayout("add_faction", action: () =>
            {
                kingdom.GetRegime().GetPlayerFactions().Add(faction);
                kingdom.GetRegime().FactionSpace.transform.ClearChildren();
                foreach (var f in kingdom.GetRegime().GetPlayerFactions())
                {
                    AddFactionCard(f, kingdom, kingdom.GetRegime().FactionSpace);
                }
                if (kingdom.GetRegime().GetPlayerFactions().Count < 3)
                {
                    for (int i = 0; i < 3-kingdom.GetRegime().GetPlayerFactions().Count; i++)
                    {
                        var addFaction = kingdom.GetRegime().FactionSpace.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(55, 90));
                        addFaction.AddButtonIntoVertLayout("add_faction", "", () => OpenSelectionWindow(kingdom), SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(15, 15));
                        addFaction?.transform.AddStretchBackground("FactionFrame", size: new Vector2(55, 90));
                    }
                }
                ScrollWindow.getCurrentWindow().clickBack();
            }, size: new Vector2(10, 10), icon: SpriteTextureLoader.getSprite("ui/setOfficer"));
            bottom.AddButtonIntoHoriLayout("remove_faction", action: () =>
            {
                FactionManager.Config.PlayerFactions.Remove(faction);
                action?.Invoke();
            }, size: new Vector2(10, 10), icon: SpriteTextureLoader.getSprite("ui/iconRemove"));
        }
        else
        {
            bottom.AddButtonIntoHoriLayout("remove_faction", action: () =>
            {
                kingdom.GetRegime().GetPlayerFactions().Remove(faction);
                if (faction.CardUI)
                {
                    Object.Destroy(faction.CardUI.gameObject);
                    var addFaction = kingdom.GetRegime().FactionSpace?.BeginVertGroup(pAlignment: TextAnchor.MiddleCenter, pSize: new Vector2(55, 90));
                    addFaction?.AddButtonIntoVertLayout("add_faction", "", () => OpenSelectionWindow(kingdom), SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(15, 15));
                    addFaction?.transform?.AddStretchBackground("FactionFrame", size: new Vector2(55, 90));
                }
            }, size: new Vector2(10, 10), icon: SpriteTextureLoader.getSprite("ui/iconRemove")); 
        }
        
        bottom?.gameObject.AdjustTopPart(bottom.transform, Vector2.down*82);
        factionPart?.transform.AddStretchBackground(isDominate?"FactionFrame_dominate":"FactionFrame", size: new Vector2(55, 90));
        faction.CardUI = factionPart;
    }

    public static void OpenSelectionWindow(Kingdom kingdom)
    {
        SelectedMetas.selected_kingdom = kingdom;
        ScrollWindow.showWindow(nameof(AddFactionWindow));
    }
    /// <summary>
    /// 在 personalGroup 下插入一个全铺满的 Image 背景，
    /// 并保留所有其他子对象在它之上。
    /// </summary>
    /// <param name="personalGroup"></param>
    /// <param name="backAddress">背景图片地址，从“ui/”开始</param>
    /// <param name="size">背景大小</param>
    /// <param name="offset">偏移值</param>
    [Hotfixable]
    public static void AddStretchBackground(this Transform personalGroup, string backAddress, Vector2 size=default, Vector2 offset=default)
    {
        var bgSprite = SpriteTextureLoader.getSprite($"ui/{backAddress}");
        var text = bgSprite.texture;
        var rect = bgSprite.rect;
        var pivot = new Vector2(0.5f, 0.5f);;
        float ppu = bgSprite.pixelsPerUnit;
        var sliced = Sprite.Create(text, rect, pivot, ppu, 0, SpriteMeshType.FullRect, new Vector4(11, 11, 24, 24));
        var back = personalGroup.transform.Find("CardBackground");
        if (back != null)
        {
            Object.Destroy(back.gameObject);
        }
        // 1. 在 personalGroup 下建一个专用子物体做背景
        var bgGO = new GameObject("CardBackground", typeof(RectTransform));
        bgGO.transform.SetParent(personalGroup, false);

        // 2. 铺满父容器
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = bgRT.anchorMax = new Vector2(0.5f, 0.5f);
        // 关闭拉伸，用 sizeDelta 定宽高
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        bgRT.localScale = Vector3.one;
        bgRT.sizeDelta = size == default
            ? new Vector2(200, 60)
            : new Vector2(size.x, size.y);
        
        // 局部坐标归零（中心对中心）
        bgRT.anchoredPosition = offset == default 
            ? Vector2.zero 
            : new Vector2(offset.x, -offset.y);

        // 3. Image + 9-slice
        var img = bgGO.AddComponent<Image>();
        img.sprite = sliced;
        img.type   = Image.Type.Sliced;
        img.color  = Color.white;

        // 3.1 忽略父布局对它的控制
        var le = bgGO.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // 4. 保证在所有其他子物体下方
        bgGO.transform.SetAsFirstSibling();
    }
    public static SimpleButton CreateToggleButton(UnityAction action)
    {
        SimpleButton year_name_button = UnityEngine.Object.Instantiate(SimpleButton.Prefab, null);
        year_name_button.Setup(action, SpriteTextureLoader.getSprite("ui/buttonToggleIndicator_1"));
        year_name_button.Background.enabled = false;
        year_name_button.SetSize(new Vector2(15, 15));
        return year_name_button;
    }
    /// <summary>
    /// 能够将任何一个组件放到其父结点上并且无视其布局
    /// </summary>
    /// <param name="gObj"></param>
    /// <param name="windowRoot"></param>
    /// <param name="offset"></param>
    public static void AdjustTopPart(this GameObject gObj, Transform windowRoot, Vector2 offset = default)
    {
        var rt = gObj.GetComponent<RectTransform>();
        var parentRt = windowRoot.parent as RectTransform;

        // 1) 先忽略布局（第一次就生效）
        var le = gObj.GetComponent<LayoutElement>() ?? gObj.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // 2) 改父节点，并立即重建一次父布局，释放驱动
        gObj.transform.SetParent(windowRoot.parent, false);
        if (parentRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);

        // 3) 现在自己控制位置
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        rt.anchoredPosition = (offset == default) ? new Vector2(0, 10) : offset;

        gObj.transform.SetAsLastSibling(); // 你只需要调一次
    }

}