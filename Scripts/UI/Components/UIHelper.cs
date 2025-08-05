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
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
    public static TextInput GenerateTextInput(Transform parent, Vector2 size=default, Vector2 offset=default, string default_text = "", UnityAction<string> action = null, TextInput input=null)
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

    public static AutoVertLayoutGroup AddActorViewIntoVertLayout(this AutoVertLayoutGroup layout, Actor actor, SimpleButton button=null)
    {
        AutoVertLayoutGroup avatarLayoutGroup = layout.BeginVertGroup(new Vector2(30, 30), pSpacing:15, pAlignment: TextAnchor.MiddleCenter);

        long id = actor?.getID() ?? -1L;
        SimpleButton clickFrame = CreateAvatarView(id, pIsAlive:true);
        if (button != null)
        {
            avatarLayoutGroup.AddChild(button.gameObject);
        }
        avatarLayoutGroup.AddChild(clickFrame.gameObject);
        avatarLayoutGroup.transform.localPosition = Vector3.zero;
        return avatarLayoutGroup;
    }
    public static void AddTextIntoVertLayout(this AutoVertLayoutGroup layout, string text, bool hideBackground=false, TextAnchor anchor=TextAnchor.MiddleLeft, Vector2 size=default)
    {
        SimpleText timeText = GameObject.Instantiate(SimpleText.Prefab, layout.transform);
        timeText.Setup(text, pSize: size==default?new Vector2(50, 10):size, pAlignment:anchor);
        if (hideBackground)
        {
            timeText.background.enabled = false;
        }
    }

    public static void AddButtonIntoVertLayout(this AutoVertLayoutGroup layout, string buttonID, string text="", UnityAction action=null, Sprite icon=null, Sprite background=null, Vector2 size=default, bool isToggle=false, bool showTip=false)
    {
        AdvancedButton button = GameObject.Instantiate(AdvancedButton.Prefab, layout.transform);
        button.Setup(buttonID, action, icon, text, size, backgroundSprite:background, isToggle:isToggle,  showTip:showTip);
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
    /// 在 personalGroup 下插入一个全铺满的 Image 背景，
    /// 并保留所有其他子对象在它之上。
    /// </summary>
    [Hotfixable]
    public static void AddStretchBackground(this Transform personalGroup, Sprite bgSprite, Vector2 size=default, Vector2 offset=default)
    {
        var text = bgSprite.texture;
        var rect = bgSprite.rect;
        var pivot = bgSprite.pivot;
        float ppu = bgSprite.pixelsPerUnit;
        var sliced = Sprite.Create(text, rect, pivot, ppu, 0, SpriteMeshType.FullRect, new Vector4(12, 24, 24, 12));
        
        // 1. 在 personalGroup 下建一个专用子物体做背景
        var bgGO = new GameObject("Background", typeof(RectTransform));
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

    public static void AdjustTopPart(this GameObject gObj, Transform windowRoot, Vector2 offset=default)
    {
        gObj.transform.SetParent(windowRoot.parent, false);
            
        var rt = gObj.GetComponent<RectTransform>();

        rt.pivot = new Vector2(0.5f, 1f);
        
        rt.anchoredPosition = Vector2.zero;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); 
        // 关闭拉伸，用 sizeDelta 定宽高
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        // 局部坐标归零（中心对中心）
        rt.anchoredPosition = offset==default?new Vector2(0, 10):offset;
        gObj.transform.SetAsLastSibling();
        var le = gObj.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        
        gObj.transform.SetAsLastSibling();
    }

}