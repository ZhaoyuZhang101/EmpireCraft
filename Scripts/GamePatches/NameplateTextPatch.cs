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
    }
}