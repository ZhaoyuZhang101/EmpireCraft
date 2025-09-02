using NeoModLoader;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using DG.Tweening;
namespace EmpireCraft.Scripts.UI.Components;

public class AdvancedButton:APrefab<AdvancedButton>
{
    /// <summary>
    ///     The <see cref="toggleStatus" /> component
    /// </summary>
    public bool toggleStatus;
    /// <summary>
    ///     The <see cref="buttonName" /> component
    /// </summary>
    public string buttonName;
    [SerializeField] private Button button;

    [SerializeField] private TipButton tipButton;

    [SerializeField] private Image background;

    [SerializeField] private Image icon;

    [SerializeField] private Text text;
    /// <summary>
    ///     The <see cref="Button" /> component
    /// </summary>
    public Button Button => button;

    /// <summary>
    ///     The <see cref="TipButton" /> component
    /// </summary>
    public TipButton TipButton => tipButton;

    /// <summary>
    ///     The <see cref="Image" /> component of the background
    /// </summary>
    public Image Background => background;

    /// <summary>
    ///     The <see cref="Image" /> component of the button icon
    /// </summary>
    public Image Icon => icon;

    /// <summary>
    ///     The <see cref="Text" /> component of the button text
    /// </summary>
    public Text Text => text;
    private void Awake()
    {
        if (!Initialized) Init();
    }
    public void Setup(string buttonID, UnityAction pClickAction, Sprite pIcon, string pText = null, Vector2 pSize = default,
        bool isToggle=false, Sprite backgroundSprite=null, bool showTip = false)
    {
        OriginalSetup(pClickAction, pIcon, pText, pSize,"normal", showTip);
        toggleStatus = false;
        if (backgroundSprite != null)
        {
            Background.sprite = backgroundSprite;
        }

        buttonName = buttonID;
        
        if (isToggle)
        {
            Button.onClick.AddListener(ChangeStatus);
            string spritePath = $"ui/buttons/{buttonName}";
            Sprite offSprite = SpriteTextureLoader.getSprite(spritePath + "Off");
            Icon.sprite = offSprite;
        }

        if (showTip)
        {
            TipButton.textOnClick = buttonID;
            TipButton.textOnClickDescription = buttonID+"_description";
            TipButton.text_description_2 = "empire_mod_tag_on_tip";
        }
    }

    private void ChangeStatus()
    {
        string spritePath = $"ui/buttons/{buttonName}";
        Sprite onSprite = SpriteTextureLoader.getSprite(spritePath + "On");
        Sprite offSprite = SpriteTextureLoader.getSprite(spritePath + "Off");
        toggleStatus = !toggleStatus;
        Icon.sprite = toggleStatus ? onSprite : offSprite;
        
    }
    
    
    public void OriginalSetup(UnityAction pClickAction, Sprite pIcon, string pText = null, Vector2 pSize = default,
        string pTipType = null, bool showTip = false)
    {
        if (pSize == default)
        {
            pSize = new Vector2(32, 32);
        }

        SetSize(pSize);
        if (string.IsNullOrEmpty(pText))
        {
            Text.gameObject.SetActive(false);
            Icon.gameObject.SetActive(true);
        }
        else
        {
            Icon.gameObject.SetActive(false);
            Text.gameObject.SetActive(true);
        }

        Icon.sprite = pIcon;
        Text.text = pText;
        Button.onClick.RemoveAllListeners();
        Button.onClick.AddListener(pClickAction);

        if (string.IsNullOrEmpty(pTipType))
        {
            TipButton.enabled = false;
        }
        else
        {
            TipButton.enabled = true;
            TipButton.type = pTipType;
            if (showTip)
            {
                TipButton.hoverAction = TipButton.showTooltipDefault;
            }
        }
    }

    /// <inheritdoc cref="APrefab{T}.SetSize" />
    public override void SetSize(Vector2 pSize)
    {
        GetComponent<RectTransform>().sizeDelta = pSize;
        float minEdge = Mathf.Min(pSize.x, pSize.y);
        Icon.GetComponent<RectTransform>().sizeDelta = new Vector2(minEdge, minEdge) * 0.875f;
        Text.GetComponent<RectTransform>().sizeDelta = pSize * 0.875f;
    }

    internal static void _init()
    {
        GameObject obj = new GameObject(nameof(AdvancedButton), typeof(Button), typeof(Image), typeof(TipButton));
        obj.transform.SetParent(ModClass.prefab_library);
        obj.GetComponent<TipButton>().enabled = false;
        obj.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/special/special_buttonRed");
        obj.GetComponent<Image>().type = Image.Type.Sliced;

        GameObject icon = new GameObject("Icon", typeof(Image));
        icon.transform.SetParent(obj.transform);
        icon.transform.localPosition = Vector3.zero;
        icon.transform.localScale = Vector3.one;

        GameObject text = new GameObject("Text", typeof(Text));
        text.transform.SetParent(obj.transform);
        text.transform.localPosition = Vector3.zero;
        text.transform.localScale = Vector3.one;
        Text textText = text.GetComponent<Text>();
        textText.font = LocalizedTextManager.current_font;
        textText.color = Color.white;
        textText.resizeTextForBestFit = true;
        textText.resizeTextMinSize = 1;
        textText.resizeTextMaxSize = 10;
        textText.alignment = TextAnchor.MiddleCenter;
        text.SetActive(false);

        Prefab = obj.AddComponent<AdvancedButton>();
        Prefab.button = obj.GetComponent<Button>();
        Prefab.tipButton = obj.GetComponent<TipButton>();
        Prefab.background = obj.GetComponent<Image>();
        Prefab.icon = icon.GetComponent<Image>();
        Prefab.text = textText;
    }
}