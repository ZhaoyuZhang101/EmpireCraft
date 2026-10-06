using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
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
    public static SimpleButton CreateAvatarView(long actor_id, UnityAction action=null, bool pIsAlive=true,
        PersonalClanIdentity pIdentity=null)
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
        if (actor?.data == null || actor.isRekt()) actor = null;
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
        if (actor != null || actor_id > 0 || pIdentity != null)
        {
            clickFrame.Button.OnHover(() =>
            {
                Tooltip.show(unitLoader, "empirecraft_actor", new TooltipData
                {
                    actor = actor,
                    tip_name = actor_id.ToString(),
                    tip_description = (pIdentity?.id ?? -1L).ToString()
                });
            });
            clickFrame.Button.OnHoverOut(Tooltip.hideTooltip);
        }
        if (actor != null)
        {
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
    // AbstractWideWindow<T> 不像 AutoLayoutWindow<T> 那样把窗口自身就做成一个 AutoVertLayoutGroup——
    // 它给的只是一个裸的 ContentTransform，要自己在上面搭一层 AutoVertLayoutGroup 才能用
    // BeginVertGroup/AddTextIntoVertLayout 这套。这段就是照抄 NML 里 AutoLayoutWindow.CreateWindow
    // 给 transform_content 做的那一遍初始化(VerticalLayoutGroup 的六个 child* 开关、spacing/padding、
    // ContentSizeFitter)，两个宽窗口共用一份，避免各写一遍还漏配置项。
    public static AutoVertLayoutGroup SetupWideWindowContentRoot(Transform contentTransform,
        float spacing = 2f, RectOffset padding = null, TextAnchor alignment = TextAnchor.UpperCenter)
    {
        contentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
        AutoVertLayoutGroup root = contentTransform.gameObject.AddComponent<AutoVertLayoutGroup>();
        VerticalLayoutGroup layoutGroup = root.layout;
        layoutGroup.childAlignment = alignment;
        layoutGroup.childControlHeight = false;
        layoutGroup.childControlWidth = false;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childScaleHeight = false;
        layoutGroup.childScaleWidth = false;
        layoutGroup.spacing = spacing;
        layoutGroup.padding = padding ?? new RectOffset(3, 3, 3, 3);
        ContentSizeFitter fitter = contentTransform.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return root;
    }

    // AbstractWideWindow.SetSize 只改 BackgroundTransform(和标题/关闭按钮)的尺寸，里面的
    // Scroll View、Viewport、Content 和滚动条仍保留窄窗口尺寸。宽窗口必须把这三层一起拉伸；
    // Content 上原版的窄背景也要关掉，否则会在宽图中间留下一个黑色小窗口。
    // 宽窗口的标题栏压在窗口上沿、并不占用窗口内部空间，顶部只需让出边框的厚度；
    // 之前给 40 是照搬窄窗口，结果滚动区可见范围从窗口中部才开始，上面空一大截。
    public const float WideWindowTopMargin = 12f;

    public static void FixWideWindowScrollArea(Transform backgroundTransform, float topMargin = WideWindowTopMargin,
        float bottomMargin = 6f, float sideMargin = 6f)
    {
        if (backgroundTransform == null) return;
        WideWindowScrollArea area = backgroundTransform.GetComponent<WideWindowScrollArea>();
        if (area == null) area = backgroundTransform.gameObject.AddComponent<WideWindowScrollArea>();
        area.Configure(topMargin, bottomMargin, sideMargin);
    }

    public static IEnumerator StabilizeWideWindowScrollArea(Transform backgroundTransform,
        float topMargin = WideWindowTopMargin, float bottomMargin = 6f, float sideMargin = 6f)
    {
        // Re-apply after the empty-window opening animation and its first layout passes. Those
        // passes restore the prefab's original narrow scrollbar coordinates.
        for (int frame = 0; frame < 3; frame++)
        {
            yield return CoroutineHelper.wait_for_next_frame;
            FixWideWindowScrollArea(backgroundTransform, topMargin, bottomMargin, sideMargin);
        }
        yield return new WaitForSecondsRealtime(0.2f);
        FixWideWindowScrollArea(backgroundTransform, topMargin, bottomMargin, sideMargin);
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

        long id = actor?.data == null || actor.isRekt() ? -1L : actor.getID();
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

        long id = actor?.data == null || actor.isRekt() ? -1L : actor.getID();
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
        // 政体窗口和帝国界面统一用同一套横式派系面板，不再各画一套(原来这里是三张竖卡片)。
        if (layout == null) return;
        InitialEmpireFactionSpace(layout, kingdom);
        groups?.Add(layout.gameObject);
    }

    /// <summary>
    /// 帝国总览使用的紧凑派系面板。配置窗口仍使用竖卡片，这里只呈现政治格局摘要。
    /// </summary>
    public static void InitialEmpireFactionSpace(AutoHoriLayoutGroup layout, Kingdom kingdom)
    {
        if (layout == null || kingdom == null || kingdom.isRekt()) return;
        layout.gameObject.name = "EmpireFactionOverview";

        Regime regime = kingdom.GetRegime();
        if (regime == null)
        {
            kingdom.LoadRegime();
            regime = kingdom.GetRegime();
        }
        if (regime == null) return;

        List<FixedFaction> factions = regime.GetPlayerFactions()?.Where(faction => faction != null).ToList()
                                      ?? new List<FixedFaction>();
        Empire empire = kingdom.GetEmpire();
        FactionClassSystem.EnsureEmpireProfiles(empire, factions);

        // 开放党禁后：底部换成"组建X党"按钮(每个可用理念一个，两个一行)
        bool partyMode = empire != null && PartySystem.IsActive(empire);
        Dictionary<PartyIdeology, int> ideologyCounts = partyMode
            ? IdeologyPopulationSystem.GetEmpireCounts(empire) : null;
        int ideologyPopulation = ideologyCounts?.Values.Sum() ?? 0;
        List<PartyIdeology> foundable = partyMode
            ? Enum.GetValues(typeof(PartyIdeology)).Cast<PartyIdeology>()
                .Where(ideology => ideologyPopulation > 0 &&
                    ideologyCounts.TryGetValue(ideology, out int supporters) &&
                    supporters >= ideologyPopulation * IdeologyPopulationSystem.PartyFoundingShare).ToList()
            : new List<PartyIdeology>();
        float bottomHeight = partyMode ? Mathf.CeilToInt(foundable.Count / 2f) * 15f + 4f
            : factions.Count < 3 ? 20f : 0f;
        float panelHeight = 19f + factions.Count * (FactionCardHeight + 2f) + bottomHeight;
        RectTransform overviewRect = layout.GetComponent<RectTransform>();
        overviewRect.sizeDelta = new Vector2(196, panelHeight);
        LayoutElement overviewElement = layout.GetComponent<LayoutElement>() ?? layout.gameObject.AddComponent<LayoutElement>();
        overviewElement.minWidth = 196f;
        overviewElement.preferredWidth = 196f;
        overviewElement.minHeight = panelHeight;
        overviewElement.preferredHeight = panelHeight;
        var panel = layout.BeginVertGroup(new Vector2(194, panelHeight), pSpacing: 2,
            pAlignment: TextAnchor.UpperCenter, pPadding: new RectOffset(3, 3, 2, 2));
        var header = panel.BeginHoriGroup(new Vector2(188, 13), TextAnchor.MiddleCenter, 2);
        // 开放党禁后派系已改组为政党
        string landscapeKey = factions.Any(faction => faction.IsParty) ? "empire_party_landscape" : "empire_faction_landscape";
        var title = header.AddTextIntoHoriLayout(LM.Get(landscapeKey).ColorString("#7FD8EA"),
            true, TextAnchor.MiddleLeft, new Vector2(158, 12));
        title.UseFixedFontSize(8, HorizontalWrapMode.Overflow);
        header.AddButtonIntoHoriLayout("recover_faction", "", () =>
        {
            regime.RecoverFactions();
            RefreshEmpireFactionSpace(layout, kingdom);
            bool saved = FactionManager.Save();
            ActionLibrary.showWhisperTip(saved ? "save_success" : "save_failed");
        }, SpriteTextureLoader.getSprite("ui/changeOfficer"), size: new Vector2(11, 11), showTip: true);
        header.AddButtonIntoHoriLayout("save_faction", "", () =>
        {
            FactionManager.Config.PlayerRegimeFactions[regime.type] = regime.GetPlayerFactions()
                .Select(faction => faction.DeepClone()).ToList();
            bool saved = FactionManager.Save();
            ActionLibrary.showWhisperTip(saved ? "save_success" : "save_failed");
        }, SpriteTextureLoader.getSprite("ui/icons/iconSaveLocal"), size: new Vector2(11, 11), showTip: true);

        foreach (FixedFaction faction in factions)
        {
            try
            {
                faction.FixMissedTemporaryFactions();
                AddEmpireFactionRow(panel, faction, kingdom, layout);
            }
            catch (Exception exception)
            {
                LogService.LogError($"帝国派系摘要绘制失败 ({faction.Name ?? faction.Type.ToString()}): {exception}");
            }
        }

        if (partyMode)
        {
            for (int start = 0; start < foundable.Count; start += 2)
            {
                var foundRow = panel.BeginHoriGroup(new Vector2(188, 14), TextAnchor.MiddleCenter, 2);
                foreach (PartyIdeology ideology in foundable.Skip(start).Take(2))
                {
                    PartyIdeology target = ideology;
                    bool can = PartySystem.CanPlayerFound(empire, target, out string reason);
                    var button = foundRow.AddButtonIntoHoriLayout($"party_found_{target}",
                        string.Format(LM.Get("party_found_button"), PartySystem.GetIdeologyName(target))
                            .ColorString(can ? "#FFFFFF" : "#8FA0A8"),
                        () =>
                        {
                            if (PartySystem.PlayerFoundParty(empire, target) != null)
                                RefreshEmpireFactionSpace(layout, kingdom);
                            else ActionLibrary.showWhisperTip(reason);
                        }, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(91, 13));
                    AttachTextTooltip(button.gameObject, $"party_found_{target}",
                        string.Format(LM.Get("party_found_button"), PartySystem.GetIdeologyName(target)),
                        can ? LM.Get("party_found_tip") : LM.Get(reason));
                }
            }
        }
        else if (factions.Count < 3)
        {
            var addRow = panel.BeginHoriGroup(new Vector2(188, 18), TextAnchor.MiddleCenter, 2);
            addRow.AddButtonIntoHoriLayout("add_faction", LM.Get("empire_add_faction_slot"),
                () => OpenSelectionWindow(kingdom), SpriteTextureLoader.getSprite("ui/setOfficer"),
                size: new Vector2(184, 15), showTip: true);
        }

        panel.transform.AddStretchBackground("regimeFrame", new Vector2(194, panelHeight));
        regime.FactionSpace = layout;
    }

    // 三种用法共用这一张横式卡片：
    //   普通(派系格局面板) —— 详情 + 锁定 / 提升占比 / 移除；
    //   readOnly(官署窗口顶部只展示主导派系) —— 只留详情；
    //   addMode(添加派系窗口里的派系库) —— 详情 + 加入本国 / 从派系库删除。
    // 派系卡片高度：名称、理念、占比条、阶层与立场四行
    private const float FactionCardHeight = 40f;

    private static void AddEmpireFactionRow(AutoVertLayoutGroup panel, FixedFaction faction, Kingdom kingdom,
        AutoHoriLayoutGroup overview, bool readOnly = false, bool addMode = false, UnityAction onLibraryChanged = null)
    {
        faction.Update();
        Regime regime = kingdom.GetRegime();
        Empire empire = kingdom.GetEmpire();
        bool isDominate = regime.GetDominateFaction() == faction;
        string culture = CultureService.GetRealmCulture(kingdom);
        (List<InstitutionNodeConfig> supports, List<InstitutionNodeConfig> opposes) =
            InstitutionSystem.GetFactionInstitutionStance(culture, faction.Type);
        InstitutionReformState activeReform = empire?.data.institution_state?.active_reform;

        // 卡片：理念色条 | 理念徽章 | 领袖头像 | 名称·理念·中央占比条·阶层与立场 | 按钮
        PartyIdeology ideology = PartySystem.LeaningOf(faction);
        Color ideologyColor = IdeologyColor(ideology);
        var row = panel.BeginHoriGroup(new Vector2(188, FactionCardHeight), TextAnchor.MiddleLeft, 2,
            new RectOffset(2, 2, 1, 1));
        AddColorBlock(row.transform, new Vector2(3, FactionCardHeight - 4f), ideologyColor);
        var badge = row.BeginVertGroup(new Vector2(15, FactionCardHeight - 4f), pSpacing: 0, pAlignment: TextAnchor.MiddleCenter);
        AddLayoutIcon(badge.transform,
            SpriteTextureLoader.getSprite(EmpireCraft.Scripts.GameLibrary.IdeologyTraitIcons.Path(ideology)), 15f);
        row.AddActorViewIntoHoriLayout(faction.GetLeader());

        // 政党行多一个"合并"按钮，按钮栏加宽一格、信息栏让出来
        bool partyRow = faction.IsParty && !readOnly && !addMode;
        float infoWidth = partyRow ? 84f : 94f;
        var details = row.BeginVertGroup(new Vector2(infoWidth, FactionCardHeight - 2f), pSpacing: 1,
            pAlignment: TextAnchor.MiddleLeft);
        string state = addMode ? "" : kingdom.IsEmpire()
            ? isDominate ? LM.Get("empire_faction_dominant_short").ColorString("#F3C34A") : ""
            : LM.Get("empire_faction_inactive_short").ColorString("#D98C8C");
        if (!addMode && faction.IsParty && empire != null && PartyBanSystem.IsClosed(empire) &&
            empire.data.constitutional_economy?.allied_party_ids?.Contains(faction.GetID()) == true)
            state = (string.IsNullOrEmpty(state) ? "" : state + " ") +
                    LM.Get("party_consultative_short").ColorString("#7FD8EA");
        // 第一行：名称(主导派系标金色"主导")
        string title = faction.Name.ColorString(isDominate ? "#FFE9A8" : "#F2EEE2");
        var name = details.AddTextIntoVertLayout(string.IsNullOrEmpty(state) ? title : $"{title} {state}", true,
            TextAnchor.MiddleLeft, new Vector2(infoWidth, 8.5f));
        name.UseFixedFontSize(7, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(name);
        // 第二行：理念(党禁前的派系标"倾向")，政党再标民族色彩
        string ideologyLabel = faction.IsParty
            ? PartySystem.GetIdeologyName(ideology)
            : string.Format(LM.Get("faction_ideology_leaning"), PartySystem.GetIdeologyName(ideology));
        string ideologyLine = ideologyLabel.ColorString(pColor: Color.Lerp(ideologyColor, Color.white, 0.35f));
        if (faction.IsParty && faction.Empire != null)
            ideologyLine += (" · " + LM.Get(EmpireCraft.Scripts.GeneralSystems.NationalSentimentSystem.ColorKey(
                EmpireCraft.Scripts.GeneralSystems.NationalSentimentSystem.PartyColor(faction.Empire, faction.Ideology))))
                .ColorString("#A8B8BE");
        details.AddTextIntoVertLayout(ideologyLine, true, TextAnchor.MiddleLeft, new Vector2(infoWidth, 7.5f))
            .UseFixedFontSize(5, HorizontalWrapMode.Overflow);
        // 第三行：中央占比条
        var shareRow = details.BeginHoriGroup(new Vector2(infoWidth, 7f), TextAnchor.MiddleLeft, 2);
        AddShareBar(shareRow.transform, new Vector2(infoWidth - 26f, 3f), faction.CentralRatio / 100f, ideologyColor);
        shareRow.AddTextIntoHoriLayout($"{faction.CentralRatio}%".ColorString("#E6E0CF"), true, TextAnchor.MiddleLeft,
            new Vector2(22f, 7f)).UseFixedFontSize(5, HorizontalWrapMode.Overflow);
        // 第四行：阶层力量与制度立场
        details.AddTextIntoVertLayout(string.Format(LM.Get("empire_faction_card_footer"),
                    faction.ClassPower.ToString("0.#"), supports.Count, opposes.Count).ColorString("#A8B8BE"), true,
                TextAnchor.MiddleLeft, new Vector2(infoWidth, 7f))
            .UseFixedFontSize(5, HorizontalWrapMode.Overflow);
        AttachFactionTooltip(details.gameObject, faction, supports, opposes, empire, activeReform,
            includeBasicInfo: true);

        var actions = row.BeginVertGroup(new Vector2(partyRow ? 42 : 32, FactionCardHeight - 4f), pSpacing: 1,
            pAlignment: TextAnchor.MiddleCenter);
        var detailButton = actions.AddButtonIntoVertLayout("EnterFactionCard", LM.Get("empire_faction_details_short"),
            () =>
            {
                ConfigData.CURRENT_SELECTED_FACTION = faction;
                SelectedMetas.selected_kingdom = kingdom;
                ScrollWindow.showWindow(nameof(FactionDetailWindow));
            }, size: new Vector2(25, 10));
        AttachFactionTooltip(detailButton.gameObject, faction, supports, opposes, empire, activeReform,
            includeBasicInfo: true);
        if (readOnly) { }
        else if (addMode)
        {
            var libraryButtons = actions.BeginHoriGroup(new Vector2(31, 10), TextAnchor.MiddleCenter, 3);
            libraryButtons.AddButtonIntoHoriLayout("add_faction", "", () =>
            {
                regime.GetPlayerFactions().Add(faction);
                AutoHoriLayoutGroup space = regime.FactionSpace;
                if (space != null) RefreshEmpireFactionSpace(space, kingdom);
                ScrollWindow.getCurrentWindow().clickBack();
            }, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(9, 9), showTip: true);
            libraryButtons.AddButtonIntoHoriLayout("delete_faction_preset", "", () =>
            {
                FactionManager.Config.PlayerFactions.Remove(faction);
                onLibraryChanged?.Invoke();
            }, SpriteTextureLoader.getSprite("ui/iconDeleteFaction"), size: new Vector2(9, 9), showTip: true);
        }
        else
        {
            var switches = actions.BeginHoriGroup(new Vector2(partyRow ? 41 : 31, 10), TextAnchor.MiddleCenter, 1);
            var lockButton = actions.transform.AddNormalOptionIntoHori(switches, "LockFaction", () =>
            {
                faction.Force = !faction.Force;
                if (faction.Force)
                {
                    foreach (FixedFaction other in regime.GetPlayerFactions().Where(other => other != faction))
                    {
                        other.Force = false;
                        other.LockButton?.SetStatus(false);
                    }
                }
                faction.LockButton?.SetStatus(faction.Force);
            }, faction.Force, isOption: true, size: new Vector2(9, 9));
            faction.LockButton = lockButton;
            AddFactionRatioUpButton(switches, faction, kingdom, () => RefreshEmpireFactionSpace(overview, kingdom));
            // 政党：理念相近的小党可以合并(有可合并对象时才出现)
            FixedFaction mergePartner = faction.IsParty ? PartySystem.FindMergePartner(empire, faction) : null;
            if (mergePartner != null)
            {
                var mergeButton = switches.AddButtonIntoHoriLayout("merge_party", "", () =>
                {
                    if (PartySystem.MergeParties(empire, faction)) RefreshEmpireFactionSpace(overview, kingdom);
                }, SpriteTextureLoader.getSprite("ui/changeOfficer"), size: new Vector2(9, 9));
                AttachTextTooltip(mergeButton.gameObject, $"merge_party_{faction.GetID()}", LM.Get("merge_party"),
                    string.Format(LM.Get("merge_party_tip"), faction.Name, mergePartner.Name));
            }
            // 政党：× 是"解散政党"(党员各投立场最近的党)；派系：× 是从格局里移除
            switches.AddButtonIntoHoriLayout(faction.IsParty ? "dissolve_party" : "remove_faction", "", () =>
            {
                if (faction.IsParty)
                {
                    if (!PartySystem.DissolveParty(empire, faction))
                        ActionLibrary.showWhisperTip("party_dissolve_last");
                }
                else regime.GetPlayerFactions().Remove(faction);
                RefreshEmpireFactionSpace(overview, kingdom);
            }, SpriteTextureLoader.getSprite("ui/iconDeleteFaction"), size: new Vector2(9, 9), showTip: true);
        }

        row.transform.AddStretchBackground(isDominate ? "FactionFrame_dominate" : "FactionFrame",
            new Vector2(188, FactionCardHeight));
        if (!readOnly && !addMode) faction.CardUI = details;
    }

    private static Color IdeologyColor(PartyIdeology ideology)
    {
        ColorAsset asset = EmpireCraft.Scripts.GameLibrary.EmpireCraftNamePlateLibrary.GetIdeologyColorAsset(ideology);
        Color color = asset?.getColorBanner() ?? new Color(0.5f, 0.85f, 0.92f);
        color.a = 1f;
        return color;
    }

    private static void AddColorBlock(Transform parent, Vector2 size, Color color)
    {
        var block = new GameObject("ColorBlock", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var rect = block.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        Image image = block.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        LayoutElement element = block.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size.x;
        element.preferredHeight = element.minHeight = size.y;
    }

    // 占比条：深色底 + 按比例填充的颜色
    private static void AddShareBar(Transform parent, Vector2 size, float fill, Color color)
    {
        var track = new GameObject("ShareBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var trackRect = track.GetComponent<RectTransform>();
        trackRect.SetParent(parent, false);
        trackRect.sizeDelta = size;
        Image trackImage = track.GetComponent<Image>();
        trackImage.color = new Color(0f, 0f, 0f, 0.55f);
        trackImage.raycastTarget = false;
        LayoutElement element = track.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size.x;
        element.preferredHeight = element.minHeight = size.y;
        var bar = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var barRect = bar.GetComponent<RectTransform>();
        barRect.SetParent(trackRect, false);
        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
        barRect.offsetMin = Vector2.zero;
        barRect.offsetMax = Vector2.zero;
        Image barImage = bar.GetComponent<Image>();
        barImage.color = color;
        barImage.raycastTarget = false;
    }

    // 提升该派系的中央占比：调用 TryIncreaseFactionRatio，总量已满时会按比例挤压其他派系
    public const int FactionRatioStep = 5;

    private static void AddFactionRatioUpButton(AutoHoriLayoutGroup parent, FixedFaction faction, Kingdom kingdom,
        Action refresh)
    {
        parent.AddButtonIntoHoriLayout("faction_ratio_up", "", () =>
        {
            if (kingdom == null || kingdom.isRekt()) return;
            if (kingdom.TryIncreaseFactionRatio(faction, FactionRatioStep)) refresh?.Invoke();
        }, SpriteTextureLoader.getSprite("ui/setOfficer"), size: new Vector2(9, 9), showTip: true);
    }

    private static void RefreshEmpireFactionSpace(AutoHoriLayoutGroup layout, Kingdom kingdom)
    {
        if (layout == null) return;
        layout.transform.ClearChildren();
        InitialEmpireFactionSpace(layout, kingdom);
    }
    /// <summary>
    /// 单个派系显示组件
    /// </summary>
    /// <param name="groups">UI区块组</param>
    /// <param name="layout">决定派系卡片的排列方式</param>
    /// <param name="kingdom">派系所属国家</param>
    public static void AddFactionCard(FixedFaction faction, Kingdom kingdom, AutoHoriLayoutGroup parentH = null, AutoVertLayoutGroup parentV = null, bool addMode = false, UnityAction action=null)
    {
        // 所有地方的派系卡片都统一成帝国界面那种横式卡片(头像 | 名称/占比/立场 | 按钮)。
        // 不在派系格局面板里的(官署窗口顶部)只展示，不给锁定/占比/移除这些会改动格局的按钮。
        if (faction == null || kingdom == null || (parentH == null && parentV == null)) return;
        if (kingdom.GetRegime() == null) kingdom.LoadRegime();
        if (kingdom.GetRegime() == null) return;
        AutoVertLayoutGroup panel = parentV ?? parentH.BeginVertGroup(pSpacing: 0,
            pAlignment: TextAnchor.MiddleCenter);
        AddEmpireFactionRow(panel, faction, kingdom, kingdom.GetRegime().FactionSpace, readOnly: !addMode,
            addMode: addMode, onLibraryChanged: action);
    }

    // 卡片上只放得下"支持几项/反对几项"的摘要，具体是哪些制度节点、节点背后是哪些阶层在
    // 撑腰、以及原来铺在卡片上的那份诉求(TemporaryFaction)列表，都挪到悬浮提示里——
    // 跟制度科技树节点的 tooltip 用的是同一套 LM.AddToCurrentLocale 注册手法。
    private static void AttachFactionTooltip(GameObject target, FixedFaction faction,
        List<InstitutionNodeConfig> supports, List<InstitutionNodeConfig> opposes,
        Empire empire, InstitutionReformState activeReform = null, bool includeBasicInfo = false)
    {
        // 支持/反对的节点里如果正好有一个是当前正在推进的那个改革，直接标出来进度——
        // 派系卡片跟"眼下到底在发生什么"之前完全脱节，这里把两者接上。
        string MarkIfActive(InstitutionNodeConfig node)
        {
            if (activeReform == null || activeReform.node_id != node.id) return "";
            string role = activeReform.sponsor_faction_id == faction.GetID()
                ? LM.Get("institution_faction_role_sponsor")
                : node.politics.oppose_factions.ContainsKey(faction.Type)
                    ? LM.Get("institution_faction_role_opponent")
                    : LM.Get("institution_faction_role_supporter");
            return $"({role} · {LM.Get("institution_status_reforming")} {activeReform.progress:0.0}%)"
                .ColorString("#65D6C4");
        }

        Dictionary<SocialClass, float> classShares = empire != null ? InstitutionSystem.BuildClassShares(empire) : null;

        var lines = new List<string>();
        if (includeBasicInfo)
        {
            lines.Add($"{LM.Get("label_central_ratio")}: {faction.CentralRatio}%".ColorString("#7FD8EA") +
                      "  ·  " + $"人数: {faction.Count}".ColorString("#7FD8EA"));
        }
        if (supports.Count > 0)
            lines.Add(($"{LM.Get("institution_support")}: " + string.Join("、", supports.Select(node =>
                $"{InstitutionSystem.GetNodeName(node)}({FormatClassContributions(node.politics.support_classes, empire, classShares)}){MarkIfActive(node)}")))
                .ColorString("#65D66E"));
        if (opposes.Count > 0)
            lines.Add(($"{LM.Get("institution_opposition")}: " + string.Join("、", opposes.Select(node =>
                $"{InstitutionSystem.GetNodeName(node)}({FormatClassContributions(node.politics.oppose_classes, empire, classShares)}){MarkIfActive(node)}")))
                .ColorString("#D98C8C"));
        if (supports.Count > 0 || opposes.Count > 0)
            lines.Add(LM.Get("institution_contribution_hint").ColorString("#8FA0A8"));

        // 诉求分三种状态显示，用颜色区分：正在推进中(青)、眼下条件已经满足、可以推进(绿)、
        // 其它(暂时不满足条件/冷却中/未激活，灰)——CheckCondition 本来就是游戏每月都会调用
        // 一次的只读检查，这里额外调用一次做展示不会有副作用。
        var claimLines = new List<string>();
        foreach (TemporaryFaction tempFac in faction.TemporaryFactions ?? new List<TemporaryFaction>())
        {
            if (tempFac == null || (tempFac.Hide && !tempFac.IsStarted())) continue;
            string label = tempFac.type.ToString();
            if (tempFac.IsStarted())
            {
                string startContent = tempFac.ShowAsPlot
                    ? $"({LM.Get("tf_starting")})"
                    : $" {LM.Get("tf_starting")}:({(int)(tempFac.progress / tempFac.progressMax * 100.0f)}/100)";
                claimLines.Add((label + startContent).ColorString("#65D6C4"));
            }
            else if (!tempFac.Active || tempFac.CountDown > 0)
            {
                claimLines.Add((label + (!tempFac.Active ? "(未激活)" : "(冷却中)")).ColorString("#8FA0A8"));
            }
            else
            {
                bool ready;
                try { ready = tempFac.CheckCondition() && tempFac.CheckTarget(); }
                catch { ready = false; }
                claimLines.Add(ready
                    ? (label + $"({LM.Get("faction_claim_ready")})").ColorString("#65D66E")
                    : label.ColorString("#B8B8B8"));
            }
        }
        if (claimLines.Count > 0) lines.Add($"<核心诉求>\n{string.Join("\n", claimLines)}");

        if (lines.Count == 0) return;
        TipButton tip = target.GetComponent<TipButton>() ?? target.AddComponent<TipButton>();
        // faction._id 是个带连字符的 GUID——LM.Get 查不到 key 时会把连字符也当非法字符处理掉
        // (返回一个连字符换成下划线的"猜测版本")，导致这里注册的 key 和查找时用的 key 对不上，
        // tooltip 直接显示原始 key 名。这里自己先把连字符换掉，两头用同一个字符串。
        // "制度倾向"和"详情"两个按钮内容不完全一样(后者多一段基础信息)，key 也要分开，
        // 不然后注册的会把先注册的覆盖掉。
        string safeId = (faction._id ?? faction.GetHashCode().ToString()).Replace("-", "_");
        string variant = includeBasicInfo ? "full" : "stance";
        string titleKey = $"faction_tip_title_{safeId}_{variant}";
        string bodyKey = $"faction_tip_body_{safeId}_{variant}";
        LM.AddToCurrentLocale(titleKey, faction.Name);
        LM.AddToCurrentLocale(bodyKey, string.Join("\n", lines));
        tip.type = "normal";
        tip.textOnClick = titleKey;
        tip.textOnClickDescription = bodyKey;
        tip.text_description_2 = "";
        tip.hoverAction = tip.showTooltipDefault;
        tip.enabled = true;
    }

    // 按顺序试几个图标名，取第一个存在的——游戏版本之间图标偶尔改名，找不到返回 null，调用方就不画图标。
    public static Sprite FirstSprite(params string[] paths)
    {
        foreach (string path in paths)
        {
            Sprite sprite = SpriteTextureLoader.getSprite(path);
            if (sprite != null) return sprite;
        }
        return null;
    }

    // 往布局组里塞一个固定大小的小图标(不接收点击)；sprite 为空时什么都不加，不会出白块。
    public static void AddLayoutIcon(Transform parent, Sprite sprite, float size)
    {
        if (sprite == null) return;
        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(LayoutElement));
        var rect = iconObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(size, size);
        Image image = iconObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        LayoutElement element = iconObject.GetComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size;
        element.preferredHeight = element.minHeight = size;
    }

    // 给布局组垫一块铺满的深色内嵌底(SimpleText 自带的 windowInnerSliced)，不参与布局、不挡点击。
    public static void AddInsetBackground(AutoHoriLayoutGroup group, Vector2 size)
    {
        SimpleText background = group.AddTextIntoHoriLayout("", false, TextAnchor.MiddleLeft, size,
            ignorePosition: true);
        RectTransform rect = background.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        background.transform.SetAsFirstSibling();
    }

    // 给任意界面元素挂一个悬浮说明。title/body 是已经本地化好的文字，key 用来在当前语言里注册，
    // 同一个 key 重复调用会覆盖旧内容（与派系卡片 tooltip 相同的 LM.AddToCurrentLocale 手法）。
    public static void AttachTextTooltip(GameObject target, string key, string title, string body)
    {
        if (target == null || string.IsNullOrWhiteSpace(key)) return;
        // 查找时 key 会被规范化(大写/连字符等会被改写，如 ZhouFeudalism → zhou_feudalism)，
        // 注册前先统一成全小写 + 下划线，两头用同一个字符串，否则 tooltip 显示原始 key 名。
        string safeKey = new string(key.ToLowerInvariant()
            .Select(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : '_').ToArray());
        string titleKey = $"empirecraft_tip_title_{safeKey}";
        string bodyKey = $"empirecraft_tip_body_{safeKey}";
        LM.AddToCurrentLocale(titleKey, title ?? "");
        LM.AddToCurrentLocale(bodyKey, body ?? "");
        // 原版 TipButton 只在同物体上有 Button 或 Slider 时才挂悬停事件(见 TipButton.Start)，
        // 纯文字没有 Button，悬停就什么都不显示：补一个不带视觉过渡的 Button，并让文字能接收指针
        if (target.GetComponent<Button>() == null && target.GetComponent<Slider>() == null)
        {
            Button button = target.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        Graphic graphic = target.GetComponent<Graphic>() ?? target.GetComponentInChildren<Graphic>(true);
        if (graphic != null) graphic.raycastTarget = true;
        TipButton tip = target.GetComponent<TipButton>() ?? target.AddComponent<TipButton>();
        tip.type = "normal";
        tip.textOnClick = titleKey;
        tip.textOnClickDescription = bodyKey;
        tip.text_description_2 = "";
        tip.hoverAction = tip.showTooltipDefault;
        tip.enabled = true;
    }

    private static string FormatClassContributions(Dictionary<SocialClass, float> classes, Empire empire,
        Dictionary<SocialClass, float> classShares)
    {
        if (classes == null || classes.Count == 0) return "";
        return string.Join(",", classes.Select(pair =>
            $"{LM.Get($"class_{pair.Key}")}{InstitutionWindow.FormatContribution(InstitutionSystem.GetClassContribution(empire, pair.Key, pair.Value, classShares))}"));
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
