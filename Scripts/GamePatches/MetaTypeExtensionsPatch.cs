using EmpireCraft.Scripts.GameClassExtensions;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

public class MetaTypeExtensionsPatch:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(AsString)).Patch(AccessTools.Method(typeof(MetaTypeExtensions), nameof(MetaTypeExtensions.AsString)),
            prefix: new HarmonyMethod(GetType(), nameof(AsString)));
    }
    public static bool AsString(MetaType pType, ref string __result)
    {
        switch (pType)
        {
            case MetaType.None:
                __result = "none";
                return false;
            case MetaType.Subspecies:
                __result = "subspecies";
                return false;
            case MetaType.Family:
                __result = "family";
                return false;
            case MetaType.Language:
                __result = "language";
                return false;
            case MetaType.Culture:
                __result = "culture";
                return false;
            case MetaType.Religion:
                __result = "religion";
                return false;
            case MetaType.Clan:
                __result = "clan";
                return false;
            case MetaType.City:
                __result = "city";
                return false;
            case MetaType.Kingdom:
                __result = "kingdom";
                return false;
            case MetaType.Alliance:
                __result = "alliance";
                return false;
            case MetaType.War:
                __result = "war";
                return false;
            case MetaType.Plot:
                __result = "plot";
                return false;
            case MetaType.Unit:
                __result = "unit";
                return false;
            case MetaType.Building:
                __result = "building";
                return false;
            case MetaType.Item:
                __result = "item";
                return false;
            case MetaType.World:
                __result = "world";
                return false;
            case MetaType.Special:
                __result = "special";
                return false;
            case MetaType.Army:
                __result = "army";
                return false;
            case MetaTypeExtension.Empire:
                __result = "empire";
                return false;
            case MetaTypeExtension.KingdomTitle:
                __result = "kingdomTitle";
                return false;
            default:
                Debug.LogError((object) ("MetaTypeExtensions.AsString missing option for : " + pType.ToString()));
                __result = pType.ToString().ToLower();
                return false;
        }
    }
}