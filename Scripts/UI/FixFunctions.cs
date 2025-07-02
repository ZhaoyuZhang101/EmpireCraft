using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace EmpireCraft.Scripts.UI;
public static class FixFunctions
{
    public static PowerButton CreateToggleButton(string pGodPowerId, Sprite pIcon, [CanBeNull] Transform pParent = null,
        Vector2 pLocalPosition = default, bool pNoAutoSetToggleAction = false)
    {
        GodPower god_power = AssetManager.powers.get(pGodPowerId);
        if (god_power == null)
        {
            LogService.LogError("Cannot find GodPower with id " + pGodPowerId);
            return null;
        }

        void toggleOption(string pPower)
        {
            GodPower power = AssetManager.powers.get(pPower);
            WorldTip.instance.showToolbarText(power);

            if (!PlayerConfig.dict.TryGetValue(power.toggle_name, out var _option))
            {
                _option = new PlayerOptionData(power.toggle_name)
                {
                    boolVal = false
                };
                PlayerConfig.instance.data.add(_option);
            }

            _option.boolVal = !_option.boolVal;
            if (_option.boolVal && power.map_modes_switch)
                AssetManager.powers.disableAllOtherMapModes(pPower);
            PlayerConfig.saveData();
        }

        if (god_power.toggle_action == null)
        {
            god_power.toggle_action = toggleOption;
        }
        else if (!pNoAutoSetToggleAction)
        {
            god_power.toggle_action = (PowerToggleAction)Delegate.Combine(god_power.toggle_action,
                new PowerToggleAction(toggleOption));
        }

        if (!PlayerConfig.dict.TryGetValue(god_power.toggle_name, out var option))
        {
            AssetManager.options_library.add(new OptionAsset()
            {
                id = god_power.toggle_name,
                default_bool = false,
                type = OptionType.Bool
            });
            option = PlayerConfig.instance.data.add(new PlayerOptionData(god_power.toggle_name)
            {
                boolVal = false
            });
        }

        var prefab = ResourcesFinder.FindResource<PowerButton>("history_log");

        bool found_active = prefab.gameObject.activeSelf;
        if (found_active)
        {
            prefab.gameObject.SetActive(false);
        }

        PowerButton obj;
        obj = pParent == null ? UnityEngine.Object.Instantiate(prefab) : UnityEngine.Object.Instantiate(prefab, pParent);

        if (found_active)
        {
            prefab.gameObject.SetActive(true);
        }
        obj.name = pGodPowerId;
        obj.icon.sprite = pIcon;
        obj.icon.overrideSprite = pIcon;
        obj.open_window_id = null;
        obj.type = PowerButtonType.Special;
        obj.transform.Find("ToggleIcon").GetComponent<ToggleIcon>().updateIcon(option.boolVal);
        // More settings for it

        var transform = obj.transform;

        transform.localPosition = pLocalPosition;
        transform.localScale = Vector3.one;

        obj.gameObject.SetActive(true);
        return obj;
    }

}
