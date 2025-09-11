using EmpireCraft.Scripts.GamePatches;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.Regimes;

public class KingdomKingOfficeDetect : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
             
        new Harmony(nameof(setKing)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.setKing)),
            prefix: new HarmonyMethod(GetType(), nameof(setKing))
        );           
        new Harmony(nameof(removeKing)).Patch(
            AccessTools.Method(typeof(Kingdom), nameof(Kingdom.removeKing)),
            prefix: new HarmonyMethod(GetType(), nameof(removeKing))
        );   
    }

    public static void setKing(Kingdom __instance, Actor pActor, bool pNewKing)
    {
        
    }

    public static void removeKing(Kingdom __instance)
    {
        
    }
}