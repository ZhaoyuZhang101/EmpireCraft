using System;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.UI.Windows;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GamePatches;

// 原版"新世界"模板窗口(new_world_templates，一格格地图模板)里加一格「世界地图」：
// 模板按钮是预制体里的固定子物体，按物体名去模板库取模板(ButtonClickMaptemplate.Awake)，所以克隆时先停用源物体，
// 删掉克隆上的 ButtonClickMaptemplate 再启用，点击改开 WorldMapWindow，悬停提示照原版写法
public class WorldMapTemplatePatch : GamePatch
{
    private const string WindowId = "new_world_templates";
    private const string ButtonName = "empirecraft_world_map";
    private const string IconPath = "ui/new_world_templates_icons/template_empirecraft_world_map";

    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        ScrollWindow.addCallbackShowStarted(OnShowStarted);
        // 世界地图前史里的谥号、庙号按语言键存，换语言后语言表重建，要再登记一遍
        new Harmony("EmpireCraft.WorldMapTexts").Patch(
            AccessTools.Method(typeof(LocalizedTextManager), nameof(LocalizedTextManager.setLanguage)),
            postfix: new HarmonyMethod(typeof(WorldMapTemplatePatch), nameof(AfterSetLanguage)));
        WorldMapHistory.RegisterTexts();
    }

    public static void AfterSetLanguage() => WorldMapHistory.RegisterTexts();

    private static void OnShowStarted(string windowId)
    {
        if (!string.Equals(windowId, WindowId, StringComparison.Ordinal)) return;
        try
        {
            ScrollWindow window = ScrollWindow.get(WindowId);
            Transform grid = window == null ? null : FindChild(window.transform, "Grid");
            if (grid == null || grid.Find(ButtonName) != null) return;
            Transform source = null;
            foreach (Transform child in grid)
                if (child.GetComponent<ButtonClickMaptemplate>() != null)
                {
                    source = child;
                    break;
                }
            if (source == null) return;
            bool wasActive = source.gameObject.activeSelf;
            source.gameObject.SetActive(false);
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, grid);
            source.gameObject.SetActive(wasActive);
            UnityEngine.Object.DestroyImmediate(clone.GetComponent<ButtonClickMaptemplate>());
            clone.name = ButtonName;
            Image icon = clone.transform.Find("preview_icon")?.GetComponent<Image>();
            Sprite sprite = SpriteTextureLoader.getSprite(IconPath);
            if (icon != null && sprite != null) icon.sprite = sprite;
            Button button = clone.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ScrollWindow.showWindow(nameof(WorldMapWindow)));
                button.OnHover(() => Tooltip.show(clone, "normal", new TooltipData
                {
                    tip_name = "world_map_template",
                    tip_description = "world_map_template_description"
                }));
                button.OnHoverOut(Tooltip.hideTooltip);
            }
            clone.transform.SetAsFirstSibling();
            clone.SetActive(true);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] 新世界窗口加按钮失败: {exception.Message}");
        }
    }

    private static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
