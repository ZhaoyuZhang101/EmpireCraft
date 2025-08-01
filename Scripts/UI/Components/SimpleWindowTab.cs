using LayoutGroupExt;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.services;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;
public class SimpleWindowTab : APrefab<SimpleWindowTab>
{
    public WindowMetaTab _tab { get; private set; }

    public Image Icon { get; private set; }
    public static Vector2 _default_size { get; private set; } = new Vector2(22, 22);
    public static Sprite _default_icon { get; private set; } = SpriteTextureLoader.getSprite("ui/icons/iconOptions");


    protected override void Init()
    {
        if (Initialized) return;
        base.Init();
        _tab = transform.GetComponent<WindowMetaTab>();
    }

    public void Setup(string pName, StatsWindow window, List<Transform> contents = null, Sprite sprite = null, UnityAction<WindowMetaTab> action=null)
    {
        name = pName;
        _tab = transform.GetComponent<WindowMetaTab>();
        _tab.name = pName;
        _tab._canvas_group = _tab.GetComponent<CanvasGroup>();
        if (_tab._canvas_group == null)
        {
            this.AddComponent<CanvasGroup>();
            _tab._canvas_group = _tab.GetComponent<CanvasGroup>();
            LogService.LogInfo("添加渲染组");
        }
        window.tabs._tabs.Add(_tab);
        if (contents != null)
        {
            _tab.tab_elements = contents;
            foreach (Transform t in contents)
            {
                t.SetParent(window.scroll_window.transform_content);
                t.gameObject.SetActive(true);
                t.localPosition = Vector3.zero;
            }
        }
        _tab._tip_button.textOnClick = LM.Get(pName);
        _tab._tip_button.textOnClickDescription = pName + "_description";
        transform.SetParent(window.tabs.transform);
        _tab.toggleActive(true);
        Icon = _tab.transform.Find("Icon").GetComponent<Image>();
        Icon.sprite = sprite ?? _default_icon;
        if (action != null)
        {
            _tab.tab_action.AddListener(action);
        } else
        {
            _tab.tab_action.AddListener(delegate
            {
                window.tabs.showTab(pName);
            });
        }
        SetSize(new Vector2(22, 22));
        _tab.transform.localScale = Vector3.one;
    }

    public override void SetSize(Vector2 pSize)
    {
        base.SetSize(pSize);
    }

    private static void _init()
    {
        //初始化按钮状态
        GameObject obj = new("AdditionalTab", typeof(Button));
        obj.AddComponent<Image>();
        obj.AddComponent<TipButton>();
        var tip_button = obj.GetComponent<TipButton>();
        tip_button.textOnClick = "AdditionalTab";
        tip_button.textOnClickDescription = "AdditionalTab_description";
        tip_button.text_description_2 = "AdditionalTab_description_2";
        obj.AddComponent<WindowMetaTab>();
        obj.AddComponent<ScrollableButton>();
        obj.AddComponent<DragOrderElement>();
        obj.AddComponent<ButtonSfx>();
        obj.transform.SetParent(ModClass.prefab_library);

        GameObject Icon = new("Icon", typeof(Image));
        Icon.transform.SetParent(obj.transform);
        Icon.transform.localScale = Vector3.one;

        var iconRect = Icon.GetComponent<RectTransform>();
        if (iconRect == null)
        {
            iconRect = Icon.AddComponent<RectTransform>();
        }

        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;

        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        Prefab = obj.AddComponent<SimpleWindowTab>();
    }
}
