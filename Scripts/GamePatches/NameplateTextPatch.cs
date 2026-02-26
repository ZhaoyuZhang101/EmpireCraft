using System;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

public class NameplateTextPatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(prepare)).Patch(AccessTools.Method(typeof(NameplateText), nameof(NameplateText.prepare)),
            postfix: new HarmonyMethod(GetType(), nameof(prepare)));
    }

    public static void prepare(NameplateText __instance, NameplateAsset pAsset, NanoObject pMeta, float pGlobalScale,
        NameplateRenderingType pNameplateMode, bool pNanoObjectSet, NanoObject pSelectedNanoObject)
    {
        __instance._banner_kingdoms.transform.localScale = Vector3.one;
        __instance._text_name.fontStyle = FontStyle.Normal;
        __instance._text_name.transform.localScale = Vector3.one;
        __instance._text_name.enabled = true;
        __instance._text_name.gameObject.SetActive(true);
        __instance.setShowing(true);
        if (pAsset != null)
        {
            if (pAsset.map_mode == MetaType.Kingdom)
            {
                __instance._show_banner_kingdom = true;
                try
                {
                    __instance._banner_kingdoms.load(pMeta);
                }
                catch
                {
                }
            }
            else
            {
                __instance._show_banner_kingdom = false;
            }
            if (pAsset.map_mode == MetaType.City)
            {
                __instance._show_banner_city = true;
                __instance._banner_city.load(pMeta as City);
            }
            else
            {
                __instance._show_banner_city = false;
            }
        }
    }
}
