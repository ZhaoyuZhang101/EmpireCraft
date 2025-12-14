using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using UnityEngine;

namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftHotKeyLibrary
{
    public static HotkeyAsset EmpireLayer;
    public static HotkeyAsset KingdomTitleLayer;
    public static MetaType[] _meta_zones = new MetaType[12]
    {
        MetaType.Army,
        MetaType.Alliance,
        MetaTypeExtension.KingdomTitle,
        MetaTypeExtension.Empire,
        MetaType.Kingdom,
        MetaType.City,
        MetaType.Clan,
        MetaType.Religion,
        MetaType.Culture,
        MetaType.Language,
        MetaType.Family,
        MetaType.Subspecies
    };
    public static void init()
    {
        var lib = AssetManager.hotkey_library;
        lib._meta_zones = _meta_zones;
        EmpireLayer = lib.add(new HotkeyAsset
        {
            id = "empire_layer",
            default_key_1 = KeyCode.E,
            check_window_not_active = true,
            check_controls_locked = true,
            just_pressed_action = delegate
            {
                switchToZones(10, lib._meta_zones);
            }
        });
        KingdomTitleLayer = lib.add(new HotkeyAsset
        {
            id = "kingdom_title_layer",
            default_key_1 = KeyCode.T,
            check_window_not_active = true,
            check_controls_locked = true,
            just_pressed_action = delegate
            {
                switchToZones(11, lib._meta_zones);
            }
        });
    }
    private static void switchToZones(int pIndex, MetaType[] _meta_zones)
    {
        MetaType currentMapBorderMode = Zones.getCurrentMapBorderMode(pCheckOnlyOption: true);
        pIndex = Toolbox.loopIndex(pIndex, _meta_zones.Length);
        currentMapBorderMode = _meta_zones[pIndex];
        MetaTypeAsset asset = AssetManager.meta_type_library.getAsset(currentMapBorderMode);
        AssetManager.powers.get(asset.power_option_zone_id).toggle_action(asset.power_option_zone_id);
        PowerButtonSelector.instance.checkToggleIcons();
        GodPower pPower = AssetManager.powers.get(asset.power_option_zone_id);
        WorldTip.instance.showToolbarText(pPower);
    }
}