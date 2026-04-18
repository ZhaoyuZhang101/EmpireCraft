using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;
namespace EmpireCraft.Scripts.UI.Components;

/// <summary>
///     A simple text prefab. Documentation is coming soon.
///     <para>一个简单的可选背景的文本框预制体, 文档很快就来了</para>
/// </summary>
public class SimpleText : APrefab<SimpleText>
{
    public Text text { get; private set; }
    public Image background { get; private set; }
    protected override void Init()
    {
        if (Initialized) return;
        base.Init();
        text = transform.Find("Text").GetComponent<Text>();
        background = GetComponent<Image>();
    }
    public void Setup(string pText, TextAnchor pAlignment = TextAnchor.MiddleLeft, Vector2 pSize = default, Sprite pBackground = null)
    {
        Init();
        SetSize(pSize == default ? new Vector2(200, 18) : pSize);
        text.text = pText;
        text.alignment = pAlignment;
        background.sprite = pBackground == null ? SpriteTextureLoader.getSprite("ui/special/windowInnerSliced") : pBackground;
    }
    public void UseFixedFontSize(int pFontSize, HorizontalWrapMode pHorizontalWrap = HorizontalWrapMode.Wrap,
        VerticalWrapMode pVerticalWrap = VerticalWrapMode.Overflow)
    {
        Init();
        text.resizeTextForBestFit = false;
        text.fontSize = pFontSize;
        text.horizontalOverflow = pHorizontalWrap;
        text.verticalOverflow = pVerticalWrap;
    }
    public void RefreshAutoHeight(float pMinHeight = 18f, float pPadding = 6f)
    {
        Init();
        var textRect = text.GetComponent<RectTransform>();
        var rootRect = GetComponent<RectTransform>();
        var layout = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);

        float preferredHeight = LayoutUtility.GetPreferredHeight(textRect) + pPadding;
        float finalHeight = Mathf.Max(pMinHeight, preferredHeight);

        rootRect.sizeDelta = new Vector2(rootRect.sizeDelta.x, finalHeight);
        textRect.sizeDelta = new Vector2(textRect.sizeDelta.x, finalHeight * 0.95f);
        layout.preferredHeight = finalHeight;
        layout.minHeight = finalHeight;
    }
    public override void SetSize(Vector2 pSize)
    {
        base.SetSize(pSize);
        text.GetComponent<RectTransform>().sizeDelta = new Vector2(pSize.x * 0.9f, pSize.y * 0.95f);
    }

    private static void _init()
    {
        GameObject obj = new("SimpleText", typeof(Image));
        obj.transform.SetParent(ModClass.prefab_library);
        obj.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
        obj.GetComponent<Image>().type = Image.Type.Sliced;
        obj.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 18);

        GameObject text = new("Text", typeof(Text));
        text.transform.SetParent(obj.transform);
        text.transform.localScale = Vector3.one;
        text.transform.localPosition = Vector3.one;

        Text textComponent = text.GetComponent<Text>();
        textComponent.alignment = TextAnchor.MiddleLeft;
        textComponent.resizeTextForBestFit = true;
        textComponent.resizeTextMinSize = 1;
        textComponent.resizeTextMaxSize = 18;
        textComponent.text = "";
        textComponent.color = Color.white;
        textComponent.font = LocalizedTextManager.current_font;

        Prefab = obj.AddComponent<SimpleText>();
    }
}
