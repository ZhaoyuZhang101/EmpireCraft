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
                prefix: new HarmonyMethod(typeof(TemporaryFactionExecutePatch), nameof(CaptureExecutionContext)),
                postfix: new HarmonyMethod(typeof(TemporaryFactionExecutePatch), nameof(RecordExecutionMessage))
            );
        }
    }

    public static void CaptureExecutionContext(TemporaryFaction __instance, ref TemporaryFactionExecutionState __state)
    {
        if (__instance == null)
        {
            return;
        }

        __state = new TemporaryFactionExecutionState
        {
            Empire = __instance.GetEmpire(),
            ClaimName = __instance.type.ToString(),
            TargetName = GetTargetName(__instance.TargetType, __instance.TargetID)
        };
    }

    public static void RecordExecutionMessage(TemporaryFaction __instance, TemporaryFactionExecutionState __state)
    {
        try
        {
            if (__state?.Empire == null || string.IsNullOrWhiteSpace(__state.ClaimName))
            {
                return;
            }

            TranslateHelper.LogTemporaryFactionExecuted(__state.Empire, __state.ClaimName, __state.TargetName);
        }
        catch (Exception e)
        {
            LogService.LogWarning($"TemporaryFaction execution record failed: {e}");
        }
    }

    private static string GetTargetName(MetaType targetType, long targetId)
    {
        if (targetId < 0)
        {
            return null;
        }

        switch (targetType)
        {
            case MetaType.Kingdom:
                return World.world.kingdoms.get(targetId)?.GetKingdomName();
            case MetaType.City:
                return World.world.cities.get(targetId)?.GetCityName();
            case MetaType.Religion:
                return World.world.religions.get(targetId)?.data?.name;
            case MetaType.Unit:
                return World.world.units.get(targetId)?.getName();
            case MetaType.None:
                return null;
            default:
                if (targetType == MetaTypeExtension.KingdomTitle)
                {
                    return ModClass.KINGDOM_TITLE_MANAGER.get(targetId)?.data?.name;
                }

                return null;
        }
    }
}

public class TemporaryFactionExecutionState
{
    public Empire Empire;
    public string ClaimName;
    public string TargetName;
}
