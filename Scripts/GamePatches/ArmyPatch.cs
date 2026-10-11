using System;
using System.Collections.Generic;
using System.Linq;
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
        new Harmony(nameof(load_captains)).Patch(
            AccessTools.Method(typeof(Army), nameof(Army.loadDataCaptains)),
            prefix: new HarmonyMethod(GetType(), nameof(load_captains))
        );
        new Harmony(nameof(attacking_zone_available)).Patch(
            AccessTools.Method(typeof(ai.behaviours.BehCityActorCheckAttack), nameof(ai.behaviours.BehCityActorCheckAttack.isAttackingZoneAvailable)),
            prefix: new HarmonyMethod(GetType(), nameof(attacking_zone_available))
        );
    }

    // 原版出征检查直接读 pActor.army：士兵刚征召还没编入军队、或所在军队刚解散时为空，空引用后整条 AI 批处理报错。
    // 没有军队、没有城市的士兵这一轮不出征
    public static bool attacking_zone_available(Actor pActor, ref bool __result)
    {
        if (pActor?.army != null && pActor.city != null) return true;
        __result = false;
        return false;
    }

    // 读档时军队的王国已不存在(存档时王国已灭亡，save 把它清成了 -1)：原版 loadDataCaptains 取王国颜色时
    // 空引用，整个读档中断、改为生成新地图。这里先把军队交给士兵所属的王国；士兵也都没有王国的就解散这支军队
    public static bool load_captains(Army __instance)
    {
        if (__instance == null) return false;
        try
        {
            if (__instance.getKingdom() != null) return true;
            Kingdom owner = __instance.units?.FirstOrDefault(unit => unit != null && !unit.isRekt() &&
                                                                      unit.kingdom != null && unit.kingdom.data != null)
                ?.kingdom;
            if (owner != null)
            {
                __instance._kingdom = owner;
                return true;
            }
            foreach (Actor unit in __instance.units?.ToList() ?? new List<Actor>())
                if (unit != null && !unit.isRekt()) unit.stopBeingWarrior();
            __instance._captain = null;
            LogService.LogWarning($"[EmpireCraft] 读档：军队 {__instance.data?.id} 的王国已不存在，已解散");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 读档修复失去王国的军队失败: {exception.Message}");
        }
        return false;
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
