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
        // 要与同盟里某一国接壤、且对它好感好；通达层算好后(用户 2026-11-06 同意，见 ConnectivitySystem)不接壤但本国使节往来的
        // 强度不低于 diplomacy_min_strength(0.25，航海档、不要战舰)的也行——隔着窄海、商路相通的近邻能结盟，隔着高原大漠的接壤反倒难
        foreach (Kingdom targetKingdom in __instance.kingdoms_hashset)
        {
            if ((targetKingdom.IsNeighbourWith(pKingdom) || ConnectivitySystem.CanContact(pKingdom, targetKingdom) == true) &&
                pKingdom.isOpinionTowardsKingdomGood(targetKingdom))
            {
                __result = true;
                return true;
            }
        }
        return false; // 允许原方法执行
    }
}
