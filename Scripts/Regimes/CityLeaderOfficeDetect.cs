using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.Regimes;

public class CityLeaderOfficeDetect:GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {    
        new Harmony(nameof(setLeader)).Patch(
            AccessTools.Method(typeof(City), nameof(City.setLeader)),
            postfix: new HarmonyMethod(GetType(), nameof(setLeader))
        );

        new Harmony(nameof(removeLeader)).Patch(
            AccessTools.Method(typeof(City), nameof(City.removeLeader)),
            postfix: new HarmonyMethod(GetType(), nameof(removeLeader))
        );
    }

    public static void setLeader(City __instance, Actor pActor, bool pNew)
    {
        var pKingdom = __instance.kingdom;
        var pRegime = pKingdom.GetRegime();
        switch (pRegime.type)
        {
            case RegimeType.Feudalism:
                break;
            case RegimeType.LvLing:
                break;
            case RegimeType.Arabic:
                break;
            case RegimeType.Republic:
                break;
            case RegimeType.ZhouFeudalism:
                break;
        }
    }

    public static void removeLeader(City __instance)
    {
        
    }

}