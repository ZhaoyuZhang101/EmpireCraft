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
            // 原版 save 用 ?. 取城市/王国的 id，只防 null 不防已销毁(data 已清空)的对象：
            // 军队还指着已灭亡的城市或王国时读 id 空引用，整个存档(含自动存档)失败
            if (__instance._city != null &&
                (__instance._city.data == null || __instance._city.kingdom != null && __instance._city.kingdom.data == null))
            {
                __instance._city = null;
            }
            if (__instance._kingdom != null && __instance._kingdom.data == null)
            {
                __instance._kingdom = null;
            }
        }
        catch
        {
            return false;
        }
        return true;
    }
}
