using System;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GamePatches;

public class TemporaryFactionExecutePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        var patcher = new Harmony(nameof(TemporaryFactionExecutePatch));
        var executeName = nameof(TemporaryFaction.Execute);
        

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(t => typeof(TemporaryFaction).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var executeMethod = AccessTools.Method(type, executeName);
            if (executeMethod == null || executeMethod.DeclaringType == typeof(TemporaryFaction))
            {
                continue;
            }
            patcher.Patch(
                executeMethod,
                finalizer: new HarmonyMethod(typeof(TemporaryFactionExecutePatch), nameof(RecordExecutionResult))
            );
        }
    }
    

    public static Exception RecordExecutionResult(TemporaryFaction __instance, Exception __exception)
    {
        try
        {
            Empire empire = __instance?.GetEmpire();
            string claimName = __instance?.type.ToString();
            string targetName = TranslateHelper.GetTemporaryFactionTargetText(__instance?.TargetType ?? MetaType.None, __instance?.TargetID ?? -1L);
            string crimeName = __instance?.GetCurrentTargetCrimeName();
            if (empire == null || string.IsNullOrWhiteSpace(claimName))
            {
                return __exception;
            }

            if (__exception == null)
            {
                TranslateHelper.LogTemporaryFactionSucceeded(empire, claimName, targetName, crimeName);
            }
            else
            {
                TranslateHelper.LogTemporaryFactionFailed(empire, claimName, targetName, crimeName);
            }
        }
        catch (Exception e)
        {
            LogService.LogWarning($"TemporaryFaction execution record failed: {e}");
        }

        return __exception;
    }
}
