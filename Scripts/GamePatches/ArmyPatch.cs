using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;
public class ArmyPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public void Initialize()
    {
        new Harmony(nameof(save)).Patch(
            AccessTools.Method(typeof(Army), nameof(Army.save)),
            prefix: new HarmonyMethod(GetType(), nameof(save))
        );
    }
    public static bool save(Army __instance)
    {
        if (__instance == null) return false;
        try
        {
            if (__instance.data == null)
            {
                LogService.LogInfo("跳过保存：Army.data 为空");
                return false;
            }
            if (__instance.units != null)
            {
                for (int i = __instance.units.Count - 1; i >= 0; i--)
                {
                    var u = __instance.units[i];
                    if (u == null || u.isRekt())
                    {
                        __instance.units.RemoveAt(i);
                    }
                }
            }
            if (__instance._captain != null && __instance._captain.isRekt())
            {
                __instance._captain = null;
            }
        }
        catch
        {
            return false;
        }
        return true;
    }
}
