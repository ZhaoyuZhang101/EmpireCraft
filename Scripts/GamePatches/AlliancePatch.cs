using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using HarmonyLib;
using NeoModLoader.api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches;
public class AlliancePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(can_join)).Patch(
            AccessTools.Method(typeof(Alliance), nameof(Alliance.canJoin)),
            prefix: new HarmonyMethod(GetType(), nameof(can_join))
        );
    }

    public static bool can_join(Alliance __instance, Kingdom pKingdom, ref bool __result)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pKingdom)) return true;
        // 世界法则「禁止AI结盟」(见 DiplomacyLaws)：玩家强制结盟在 ByGod 作用域里，不受此限
        if (DiplomacyLaws.AllianceBlocked)
        {
            __result = false;
            return false;
        }
        if (!FeudalVassalService.CanJoinAlliance(pKingdom, __instance))
        {
            __result = false;
            return false;
        }
        if (pKingdom.IsInEmpire())
        {
            __result = false;
            return false; // 阻止原方法执行
        }
        foreach (Kingdom targetKingdom in __instance.kingdoms_hashset)
        {
            if (targetKingdom.IsNeighbourWith(pKingdom) && pKingdom.isOpinionTowardsKingdomGood(targetKingdom))
            {
                __result = true;
                return true;
            }
        }
        return false; // 允许原方法执行
    }
}
