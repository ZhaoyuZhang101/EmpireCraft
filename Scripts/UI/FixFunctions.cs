using System;
using System.Runtime.CompilerServices;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using EmpireCraft.Scripts.GameClassExtensions;
using JetBrains.Annotations;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace EmpireCraft.Scripts.UI;
public static class FixFunctions
{
    public static PowerButton CloneExistingLayerButton(MetaType mapType, Sprite fallbackIcon)
    {
        MetaTypeAsset mapAsset = mapType.getAsset();
        string powerId = mapAsset?.power_option_zone_id;
        GodPower power = string.IsNullOrEmpty(powerId) ? null : AssetManager.powers.get(powerId);
        if (power == null)
        {
            LogService.LogError($"Unable to copy the {mapType} layer button: its original power is unavailable.");
            return null;
        }

        PowerButton prefab = ResourcesFinder.FindResource<PowerButton>(powerId);
        if (prefab == null)
        {
            // Keep the shortcut functional if another mod replaced the original toolbar prefab.
            PowerButton fallback = PowerButtonCreator.CreateGodPowerButton(powerId, fallbackIcon);
            TipButton fallbackTip = fallback?.GetComponent<TipButton>();
            if (fallbackTip != null)
            {
                fallbackTip.textOnClick = power.getLocaleID();
                fallbackTip.textOnClickDescription = power.getDescriptionID();
                fallbackTip.type = "normal";
            }
            return fallback;
        }

        // Clone the vanilla button itself so its multi-toggle icons, tooltip and click handling
        // continue to use the original culture-layer power and option state.
        bool wasActive = prefab.gameObject.activeSelf;
        if (wasActive) prefab.gameObject.SetActive(false);
        PowerButton clone = UnityEngine.Object.Instantiate(prefab);
        if (wasActive) prefab.gameObject.SetActive(true);

        clone.name = powerId;
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localScale = Vector3.one;
        clone.gameObject.SetActive(true);
        clone.init();
        return clone;
    }

    public static PowerButton CreateLayerButton(MetaType mapType, Sprite pIcon, [CanBeNull] Transform pParent = null,
        Vector2 pLocalPosition = default, int maxOption=3)
    {
        if (maxOption <= 0)
        {
            LogService.LogError("错误，开关最大选项小于0");
            maxOption = 1;
        }
        PowerLibrary powerLib = AssetManager.powers;
        GodPower power = powerLib.add(new GodPower
        {
            id = $"{mapType.ToMetaString()}_layer",
            name = $"{mapType.ToMetaString()}_layer",
            unselect_when_window = true,
        });
        power.tester_enabled = false;
        power.map_modes_switch = true;
        power.toggle_name = $"map_{mapType.ToMetaString()}_layer";
        power.toggle_action = (PowerToggleAction) Delegate.Combine(power.toggle_action, new PowerToggleAction(powerLib.toggleOptionZone));
        AssetManager.options_library.add(new OptionAsset()
        {
            id = $"map_{mapType.ToMetaString()}_layer",
            default_int = 0,
            max_value = maxOption-1,
            multi_toggle = maxOption>1,
            type = OptionType.Bool,
            locale_options_ids = AssetLibrary<OptionAsset>.a($"ui_zone_mode_{mapType.ToMetaString()}_0", $"ui_zone_mode_{mapType.ToMetaString()}_1", $"ui_zone_mode_{mapType.ToMetaString()}_2")
        });
        var option = PlayerConfig.instance.data.add(new PlayerOptionData(power.toggle_name)
        {
            boolVal = false,
            intVal = 0
        });
        LogService.LogInfo("Map option added:"+power.toggle_name);
        powerLib.linkAssets();
        AssetManager.options_library.linkAssets();
        var prefab = ResourcesFinder.FindResource<PowerButton>("subspecies_layer");

        bool foundActive = prefab.gameObject.activeSelf;
        if (foundActive)
        {
            prefab.gameObject.SetActive(false);
        }
        var obj = pParent == null ? UnityEngine.Object.Instantiate(prefab) : UnityEngine.Object.Instantiate(prefab, pParent);

        if (foundActive)
        {
            prefab.gameObject.SetActive(true);
        }
        obj.name = $"{mapType.ToMetaString()}_layer";
        obj.icon.sprite = pIcon;
        obj.icon.overrideSprite = pIcon;
        obj.open_window_id = null;
        obj.type = PowerButtonType.Special;
        obj.transform.Find("ToggleIcon").GetComponent<ToggleIcon>()?.updateIcon(option.boolVal);
        for(int i=0; i<maxOption; i++) {
            obj.transform.Find($"toggle_{(i+1>=maxOption?0:i+1)}").GetComponent<ToggleIcon>()?.updateIconMultiToggle(true, option.intVal==i);
        }
        
        var transform = obj.transform;
        power.toggle_action = (PowerToggleAction) Delegate.Combine(power.toggle_action, new PowerToggleAction(p=>ChangeIcon(p, obj)));
        transform.localPosition = pLocalPosition;
        transform.localScale = Vector3.one;
        
        obj.gameObject.SetActive(true);
        obj.init();
        var tipButton = obj.GetComponent<TipButton>();
        tipButton.textOnClick = obj.godPower.id;
        tipButton.textOnClickDescription = obj.godPower.id + "_description";
        tipButton.text_description_2 = "empirecraft_layer_switch_hint";
        return obj;
    }
    private static void ChangeIcon(string pPower, PowerButton obj)
    {
        OptionAsset option = AssetManager.powers.get(pPower).option_asset;
        if (option.isActive())
        {
            for (int i = 0; i < option.max_value+1; i++)
            {
                obj.transform.Find($"toggle_{(i+1>option.max_value?0:i+1)}").GetComponent<ToggleIcon>()?.updateIconMultiToggle(true, option.current_int_value==i);
            }
        }
        else
        {
            obj.transform.Find("toggle_0").GetComponent<ToggleIcon>()?.updateIconMultiToggle(false, false);
            obj.transform.Find("toggle_1").GetComponent<ToggleIcon>()?.updateIconMultiToggle(false, false);
            obj.transform.Find("toggle_2").GetComponent<ToggleIcon>()?.updateIconMultiToggle(false, false);
        }
    }
}
